# 6. Reporting to VailIT Dashboard

Each monitor can send its readings to **VailIT Dashboard** (in the `Dashboard` folder at the root
of this repository), which shows every server in one place, checks thresholds and emails you
when something needs a look. Reporting is **off by default**. Nothing changes until you connect a monitor.

```
 VailITMonitor (each server)                                   VailIT Dashboard
 ┌───────────────────────────────┐   POST /api/v1/reports     ┌────────────────────┐
 │ sampling ─▶ SQLite "samples"  │   every 60 s, X-Api-Key    │ groups, thresholds,│
 │                 │             │ ─────────────────────────▶ │ alerts, offline    │
 │ reporter ◀──────┘ (outbox)    │ ◀── 200 acceptedThroughUtc │ detection          │
 └───────────────────────────────┘                            └────────────────────┘
```

## Connect a server

1. In the dashboard: **Monitors & keys → Create key**. Copy the `vmk_...` key; it is shown once.
2. On the server, as Administrator, either pass the key to the installer:

   ```powershell
   .\scripts\Install-VailITMonitor.ps1 -Edition Net10 -DashboardUrl https://dashboard:5443 -ApiKey vmk_... -ServerType SQL
   ```

   or connect an existing installation and restart it:

   ```powershell
   & "C:\Program Files\VailITMonitor\VailITMonitor.exe" connect https://dashboard:5443 vmk_... SQL
   Restart-Service VailITMonitor
   ```

3. Check it: `VailITMonitor.exe report-test` takes one reading, sends it, and prints the dashboard's
   reply. Within a minute the server appears on the dashboard in the `SQL` group.

`connect` writes the settings to **`reporting.json`** (.NET 10) or **`reporting.config`** (.NET 4.8)
next to the program. The API key is encrypted with Windows DPAPI for this machine (`dpapi:...`), so
the file is useless if copied elsewhere. These files are not part of the build output, so upgrades
keep them. To disconnect, delete the file (or set `Enabled` to false) and restart the service.

## What is sent

Every report carries who the machine is (machine id, name, server type, tags, OS, CPUs, RAM, drives,
last boot, monitor version) and the readings, using exactly the metric names in
[01 How it works](01-How-It-Works.md#what-is-measured). The C# shapes are in
`Shared/VailIT.Reporting.Contracts/Contracts.cs`, shared by both editions and the dashboard.

* **Machine id.** A GUID made once and kept in `<DataFolder>\machine-id.txt` (or set
  `Reporting:MachineId`). The dashboard knows the server by it, so renaming a server does not make a
  new one.
* **Server type and tags** only choose a new server's first group. After that, the group set in the
  dashboard wins.

## How it behaves

| Situation | What the monitor does |
|---|---|
| Normal | One report about 5 s after each reading (every 60 s) |
| First connection | Sends the last `InitialBacklogMinutes` (60) of history, not the whole database |
| Dashboard or network down | Keeps sampling locally; retries after 1, 2, 4, 8, then every 15 min (±20%) |
| Back online | Sends everything it missed, 60 readings per report, gzip-compressed, 7 s apart (the dashboard allows 10 requests a minute per key) |
| Key revoked or wrong (401) | Warns in the event log; retries every 15 min |
| Report refused (400) | Warns and skips that batch so it can't block the rest |
| Report too large (413) | Halves the number of readings per report |
| Dashboard busy (429/503) | Waits for the `Retry-After` time |
| Dashboard asks for another interval | Uses it (never faster than `SampleIntervalSeconds`) |
| Clocks differ by over 2 min | Warns: the dashboard may think the server is late |

Readings are never lost while the outage is shorter than raw retention (30 days by default),
because the local `samples` table is the outbox. The reporter keeps one value in `task_state`,
`report.last_ack_ts`: the newest reading the dashboard has confirmed.

Failures are logged once, then at most hourly while they last, and recovery is logged too, so a long
outage doesn't flood the event log.

## Security

* Use **https** outside a lab. Plain http works for testing, but the monitor logs a warning, because
  the key would cross the network unencrypted. The .NET 4.8 edition switches TLS 1.2 on explicitly.
* For a self-signed dashboard certificate, put its thumbprint in `PinnedCertificateThumbprint`
  rather than turning certificate checks off. The monitor then accepts that certificate and no other.
* Each server has its own key. Revoke one in the dashboard to cut off that server alone.

## Settings

| Setting (.NET 10: `Monitor:Reporting:*`, .NET 4.8: `Reporting.*`) | Default | Meaning |
|---|---|---|
| `Enabled` | `false` | Turn reporting on |
| `DashboardUrl` | | e.g. `https://dashboard:5443` |
| `ApiKeyProtected` | | `dpapi:...` from `connect`, or a plain `vmk_...` key for testing |
| `MachineId` | | Empty = generated once in the data folder |
| `ServerType` | | Group hint, e.g. `SQL` |
| `Tags` | | e.g. `prod, denver` |
| `IntervalSeconds` | `60` | Starting interval; the dashboard can change it |
| `TimeoutSeconds` | `15` | Per request |
| `PinnedCertificateThumbprint` | | Only for a self-signed certificate |
| `InitialBacklogMinutes` | `60` | History sent on first connection |

## Where the code is

| | .NET 10 | .NET 4.8 |
|---|---|---|
| Report shapes | `Shared/VailIT.Reporting.Contracts` | same project |
| Timing and retries | `Dashboard/DashboardReporter.cs` | `Dashboard/DashboardReporter.cs` (no async) |
| HTTP | `Dashboard/DashboardClient.cs` (`SocketsHttpHandler`) | `Dashboard/DashboardClient.cs` (`HttpClientHandler`) |
| Machine description | `Dashboard/MachineInfo.cs` | `Dashboard/MachineInfo.cs` |
| Key encryption | `Dashboard/ApiKeyProtector.cs` | `Dashboard/ApiKeyProtector.cs` |
| Schedule | `Workers/ReportingWorker.cs` (`BackgroundService`) | third `Timer` in `Service/MonitorService.cs`, re-armed after each cycle |
| Outbox query | `MetricStore.GetSampleBatchesAfter` | same |
