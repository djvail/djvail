# 1. How VailITMonitor works

## The big picture

```
                every minute                        every 15 minutes (MaintenanceIntervalMinutes)
                     │                                          │
                     ▼                                          ▼
 ┌──────────────────────────┐    ┌───────────────┐    ┌──────────────────────────────────────────┐
 │ MetricCollector          │    │ SQLite file   │    │ MaintenanceService                       │
 │  CPU, memory, disks,     │───▶│ samples       │───▶│ 1. RollupService  raw  ──▶ day           │
 │  disk I/O, network,      │    │ rollups       │    │                   day  ──▶ week/month/   │
 │  system, itself          │    │ task_state    │◀───│                           quarter/year   │
 └──────────────────────────┘    │ report_log    │    │ 2. ReportGenerator  ──▶ PDF files        │
                                 └───────────────┘    │ 3. CleanupService   ──▶ delete old data  │
                                                      └──────────────────────────────────────────┘
```

Two timers run inside the service:

1. **Sampling** (every `SampleIntervalSeconds`, default 60). It takes one reading of every enabled
   metric and writes it to the database in a single transaction. The timer is lined up with
   the clock, so readings land on the minute.
2. **Maintenance** (every `MaintenanceIntervalMinutes`, default 15, with the first run 1 minute after
   start-up). It does three jobs in order:
   1. **Rollups:** summarise any finished day that has no summary yet, then any finished
      week, month, quarter or year.
   2. **Reports:** create the PDF for any finished period that has no report yet.
   3. **Cleanup:** delete data and PDFs past their keep-time, for each tier whose cleanup is due.

Every maintenance step is **idempotent and self-healing**. It doesn't rely on "it is midnight,
so do X". Each run looks at what exists and fills in whatever is missing. If the server was
switched off over a weekend, the next start-up creates the missing summaries and reports (as
long as the raw readings still exist).

## What is measured

| Metric (name in the database) | How it is read | Cost |
|---|---|---|
| `cpu.total_pct`: CPU % | `GetSystemTimes` (Windows API): busy time ÷ total time since the last reading | 1 API call |
| `cpu.core_pct` [core#]: CPU % per logical processor | `NtQuerySystemInformation` (Windows API), same method | 1 API call |
| `mem.used_pct`, `mem.used_mb`, `mem.available_mb`, `mem.cache_mb`: RAM | `GetPerformanceInfo` (Windows API) | 1 API call (shared) |
| `mem.commit_pct`: commit charge vs. limit (RAM + page file) | same call | – |
| `sys.processes`, `sys.threads`, `sys.handles` | same call | – |
| `sys.uptime_hours` | `Environment.TickCount64` / `GetTickCount64` | trivial |
| `sys.cpu_queue`: threads waiting for a CPU | performance counter `System\Processor Queue Length` | small |
| `disk.free_pct`, `disk.free_gb`, `disk.used_gb` [drive] | `DriveInfo` for each **fixed** drive | 1 call per drive |
| `diskio.read_kbps`, `diskio.write_kbps`, `diskio.busy_pct`, `diskio.queue` | performance counters `PhysicalDisk(_Total)` | small |
| `net.recv_kbps`, `net.sent_kbps` [adapter, and `_Total`] | `NetworkInterface.GetIPStatistics()` byte counters ÷ seconds | small |
| `self.private_mb`, `self.cpu_pct`: the monitor itself | `Process.GetCurrentProcess()` | trivial |

**Why the numbers are averages:** CPU, disk I/O and network are *counters that only go up*. Each
reading subtracts the previous one and divides by the elapsed time. So a "CPU 37%" reading means
37% on average over the whole minute, not one moment that happened to be caught. That's more
accurate than sampling once a minute, and cheaper than sampling every second.

**Deliberately not measured:** CPU temperature and GPU. Windows does not expose these reliably
without vendor drivers or WMI providers, which would make the service heavier and less portable.
Per-process "top 5" lists are also left out: they mean walking every process every minute, which
costs more than everything else combined. See [04 Editing the code](04-Editing-The-Code.md) if you
want to add one.

## The database

The data lives in one SQLite file, `VailITMonitor.db` in the data folder. You can open it with
[DB Browser for SQLite](https://sqlitebrowser.org) to look around. Open it read-only, or stop the
service first.

| Table | One row per | Columns |
|---|---|---|
| `metrics` | series | `id`, `name` (e.g. `disk.free_pct`), `instance` (e.g. `C:`, or empty) |
| `samples` | series × reading | `ts` (Unix seconds, UTC), `metric_id`, `value` |
| `rollups` | series × period | `period` (1 = day, 2 = week, 3 = month, 4 = quarter, 5 = year), `start` (local date `yyyy-MM-dd`), `metric_id`, `min`, `avg`, `max`, `p95`, `count` |
| `task_state` | cleanup task | `name`, `last_run` |
| `report_log` | report produced | `period`, `start`, `file`, `created` |

Some useful queries:

```sql
-- Latest reading of every series
SELECT m.name, m.instance, datetime(s.ts, 'unixepoch', 'localtime') AS at, s.value
FROM samples s JOIN metrics m ON m.id = s.metric_id
WHERE s.ts = (SELECT MAX(ts) FROM samples);

-- Daily CPU summary for the last 14 days
SELECT r.start, r.avg, r.max, r.p95
FROM rollups r JOIN metrics m ON m.id = r.metric_id
WHERE r.period = 1 AND m.name = 'cpu.total_pct'
ORDER BY r.start DESC LIMIT 14;
```

**Size:** with about 30 series (4 CPU cores, 2 drives, 1 network adapter) you get about
43,000 rows a day, roughly 1 MB a day. With the default 30-day raw retention the file settles
at around **30–40 MB**. Summaries are tiny: a year of daily summaries is about 1 MB.

The file uses WAL mode, so the sampler can write while a report is being read. After big
deletions, `PRAGMA incremental_vacuum` gives the free space back to the disk.

## Rollups (summaries)

| Tier | Built from | When |
|---|---|---|
| Day | raw readings | once the day has finished (plus 2 sample intervals' grace) |
| Week | daily summaries | once the week has finished (weeks start on `FirstDayOfWeek`, default Monday) |
| Month / Quarter / Year | daily summaries | once the period has finished |

For each series a summary stores:

* **min** and **max:** exact.
* **avg:** for a day, the mean of the readings. For longer periods, the mean of the daily
  averages *weighted by how many readings each day had*, so a half day of data doesn't count
  as a full day.
* **p95:** the 95th percentile, meaning "95% of the time it was at or below this". It is a better
  "how busy does it really get" figure than max, which a single spike can set. For a day it is exact.
  For longer periods it is the reading-weighted average of the daily p95s, because the raw
  readings needed for an exact figure have been cleaned up by then. This is a well-known
  approximation, and the reports say so.
* **count:** how many readings it covers. The reports use it to show coverage, e.g.
  "1,440 readings (100% of the period)".

All "days" are **local** days (midnight to midnight on the server's clock). Readings are stored
in UTC, so daylight-saving changes don't create duplicate or missing hours.

## Reports

| Report | Created | Charts | Points on the charts |
|---|---|---|---|
| Daily | just after midnight (after `DelayMinutes`) | CPU, memory, free space per drive, disk activity, network | every raw reading |
| Weekly | Monday morning | CPU (avg / p95 / peak), memory (avg / peak), lowest free space per drive, disk activity, network | one per day |
| Monthly | 1st of the month | same | one per day |
| Quarterly | 1st of Jan / Apr / Jul / Oct | same | one per day |
| Yearly | 1 January | same | one per week |

If the finer summaries have already been cleaned up, longer reports automatically fall back to
coarser points (day → week → month), so a report can always be regenerated while its data exists.

Each PDF has:

1. A header with your company name, the period, the machine name and the coverage.
2. Headline tiles: average and peak CPU, average memory, the drive with the least free space,
   and the monitor's own memory use.
3. The charts.
4. On a new page, a table of every metric (min / average / max / p95) and a separate per-core
   CPU table.

Files are named `VailITMonitor_<MACHINE>_<Type>_<period>.pdf`, e.g.
`VailITMonitor_SERVER01_Daily_2026-11-25.pdf`, and saved under `Reports\Daily`, `Reports\Weekly`
and so on.

The `report_log` table records each report produced, so a report is **never produced twice**,
even after cleanup deletes the PDF. To re-create one on purpose, use the command line:
`VailITMonitor.exe report monthly 2026-10-01`.

## Cleanup

Each tier has two settings: `KeepDays` (how long to keep it) and `CleanupEveryDays` (how often
to check).

| Tier | KeepDays | CleanupEveryDays |
|---|---|---|
| Raw readings | 30 | 1 |
| Daily summaries | 730 (2 years) | 7 |
| Weekly summaries | 1095 (3 years) | 30 |
| Monthly summaries | 1825 (5 years) | 90 |
| Quarterly summaries | 0 = forever | 365 |
| Yearly summaries | 0 = forever | 365 |

PDF reports have their own `KeepDays`: daily 90, weekly 365, and forever for the rest.

Safety rules built into the cleanup:

* Rollups always run **before** cleanup in the same pass, so raw readings are summarised before
  they can be deleted.
* Raw readings are never cut to fewer than 2 days, even if `KeepDays` is set to 1.
* A summary period is deleted only once it has *completely* aged out.
* When you change retention, keep each tier's `KeepDays` at least as long as the tier below it
  (raw ≤ daily ≤ weekly ≤ ...). Otherwise a coarser summary could be built after its
  source data is gone.

`VailITMonitor.exe cleanup` runs every cleanup immediately, ignoring `CleanupEveryDays`.

## Resource use

* **CPU:** each reading takes a few milliseconds. The service is idle the rest of the minute.
* **Memory:** the service records its own private memory as `self.private_mb`, and the reports show
  it in the "Monitor's own memory" tile. Expect tens of MB. Making a PDF (charts plus layout)
  briefly raises it. Afterwards the service runs a compacting garbage collection to hand
  that memory back to Windows.
* **Disk:** about 1 MB a day of raw data (see above), plus about 300–400 KB per PDF report.
* **Settings that help:** both editions turn off the background ("concurrent") garbage collector
  and use the workstation garbage collector. The .NET 10 edition also sets `System.GC.ConserveMemory`.

Run both editions side by side for a few days (see [02 Build, install & remove](02-Build-Install.md))
and compare their `self.private_mb` and `self.cpu_pct` to see the difference on your own hardware.

## Logging

* **Windows Event Log** → *Application* log, source **VailITMonitor**. Warnings and errors always
  go here. Information messages (summaries and reports created, cleanup results) do too by default.
  Turn them down in `appsettings.json` → `Logging:EventLog`, or with `Logging.Verbose=false` in the
  .NET Framework edition.
* **Console:** when you run `VailITMonitor.exe console`, the same messages appear in the window.
