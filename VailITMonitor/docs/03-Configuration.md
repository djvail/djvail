# 3. Configuration

| Edition | File (in the install folder) | Format |
|---|---|---|
| .NET 10 | `appsettings.json` | JSON, nested sections. Comments (`//`) are allowed. |
| .NET Framework 4.8 | `VailITMonitor.exe.config` | XML `<appSettings>`, flat keys with dots |

**Restart the service after any change:** `Restart-Service VailITMonitor`

The two files hold exactly the same settings, only written differently. For example:

```jsonc
// appsettings.json (.NET 10)
"Monitor": {
  "Retention": {
    "Raw": { "KeepDays": 30, "CleanupEveryDays": 1 }
  }
}
```

```xml
<!-- VailITMonitor.exe.config (.NET Framework) -->
<add key="Retention.Raw.KeepDays" value="30" />
<add key="Retention.Raw.CleanupEveryDays" value="1" />
```

In the tables below, the .NET 10 name is relative to the `Monitor` section, and the .NET Framework
key is in brackets where it differs.

## General

| Setting | Default | Meaning |
|---|---|---|
| `SampleIntervalSeconds` | `60` | Seconds between readings. Minimum 5. Smaller values mean more detail and a bigger database: 10 s ≈ 6 MB/day. |
| `DataFolder` | `%ProgramData%\VailITMonitor` (Net10)<br>`%ProgramData%\VailITMonitor-Net48` (Net48) | Where the database and reports go. Environment variables are expanded. |
| `MaintenanceIntervalMinutes` | `15` | How often rollups, reports and cleanup check for work. A report appears at most this long after it is due. |
| `FirstDayOfWeek` | `Monday` | First day of a week for weekly summaries and reports (`Sunday`, `Monday`, ...). |

## Metrics: what to measure

| Setting | Default | What it switches on |
|---|---|---|
| `Metrics.Cpu` | `true` | Total CPU % |
| `Metrics.CpuPerCore` | `true` | CPU % per logical processor. Turn off on machines with many cores to save space. |
| `Metrics.Memory` | `true` | RAM used/available (MB and %), commit charge %, file cache |
| `Metrics.Disks` | `true` | Free %, free GB and used GB for each fixed drive |
| `Metrics.DiskIo` | `true` | Disk read/write KB/s, busy %, queue length |
| `Metrics.Network` | `true` | KB/s received/sent per adapter, and in total |
| `Metrics.System` | `true` | Processes, threads, handles, CPU queue length, uptime |
| `Metrics.Self` | `true` | The monitor's own memory and CPU |
| `Metrics.Drives` | *empty* = all | Only these drives. JSON: `[ "C:", "D:" ]`. XML: `C:,D:` |
| `Metrics.ExcludeAdapters` | `Loopback, isatap, Teredo, Bluetooth` | Ignore network adapters whose name or description contains any of these (not case-sensitive), e.g. `vEthernet` for Hyper-V virtual switches. |

## Retention: how long data is kept

`KeepDays` = how many days to keep (**0 = forever**). `CleanupEveryDays` = how often that tier's
cleanup runs.

| Tier | .NET 10 | .NET Framework keys | KeepDays | CleanupEveryDays |
|---|---|---|---|---|
| Raw readings | `Retention.Raw` | `Retention.Raw.*` | 30 | 1 |
| Daily summaries | `Retention.Daily` | `Retention.Daily.*` | 730 | 7 |
| Weekly summaries | `Retention.Weekly` | `Retention.Weekly.*` | 1095 | 30 |
| Monthly summaries | `Retention.Monthly` | `Retention.Monthly.*` | 1825 | 90 |
| Quarterly summaries | `Retention.Quarterly` | `Retention.Quarterly.*` | 0 | 365 |
| Yearly summaries | `Retention.Yearly` | `Retention.Yearly.*` | 0 | 365 |

Rules of thumb:

* Keep each tier at least as long as the one below it (raw ≤ daily ≤ weekly ≤ monthly ...).
* Daily reports need raw readings, so `Raw.KeepDays` must cover any daily report you might
  want to re-create.
* Raw readings are never cut to fewer than 2 days.

## Reports

| Setting | Default | Meaning |
|---|---|---|
| `Reports.Enabled` | `true` | Turn all automatic reports off or on. The `report` command still works when off. |
| `Reports.Folder` | *empty* | Where PDFs go. Empty = `<DataFolder>\Reports`. A network share (`\\server\share\Reports`) works if the service account can write to it. *LocalSystem* writes as the computer account `DOMAIN\SERVER$`. |
| `Reports.CompanyName` | `Vail IT` | Shown at the top of every report |
| `Reports.PageSize` | `Letter` | `Letter` or `A4` |
| `Reports.DelayMinutes` | `5` | Wait this long after a period ends before reporting on it |
| `Reports.CatchUpPeriods` | `3` | After downtime, create missed reports for up to this many past periods of each type |
| `Reports.Daily.Enabled` / `.KeepDays` | `true` / `90` | Daily PDFs, and how many days to keep the files |
| `Reports.Weekly.Enabled` / `.KeepDays` | `true` / `365` | |
| `Reports.Monthly.Enabled` / `.KeepDays` | `true` / `0` (forever) | |
| `Reports.Quarterly.Enabled` / `.KeepDays` | `true` / `0` | |
| `Reports.Yearly.Enabled` / `.KeepDays` | `true` / `0` | |

## Logging

| .NET 10 (`appsettings.json`, outside `Monitor`) | .NET Framework | Meaning |
|---|---|---|
| `Logging:EventLog:LogLevel:VailITMonitor` = `Information` | `Logging.Verbose` = `true` | Also log information messages (summaries/reports created, cleanup results) to the Event Log. Set to `Warning` / `false` for warnings and errors only. |

## Example: a busy file server

```jsonc
"Monitor": {
  "SampleIntervalSeconds": 30,
  "Metrics": { "CpuPerCore": false, "Drives": [ "C:", "E:", "F:" ], "ExcludeAdapters": [ "vEthernet", "Loopback" ] },
  "Retention": { "Raw": { "KeepDays": 14, "CleanupEveryDays": 1 } },
  "Reports": { "Folder": "\\\\fileserver\\IT\\Reports\\%COMPUTERNAME%", "Daily": { "Enabled": true, "KeepDays": 30 } }
}
```

(In JSON every `\` must be written as `\\`.)
