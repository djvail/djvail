# 4. Editing the code

Both editions have the same classes with the same names, so a change is made in the same place
in each. If you only use one edition, you only need to change that one.

## 4.1 Tour of the source

| File | .NET 10 | .NET 4.8 | What it does |
|---|---|---|---|
| Entry point | `Program.cs` | `Program.cs` | Reads the command line, wires the objects together, runs the service or a command |
| Service plumbing | `Workers/SamplingWorker.cs`, `Workers/MaintenanceWorker.cs` | `Service/MonitorService.cs` (+ `ProjectInstaller.cs`) | The two timers: take readings; run maintenance |
| Settings | `Configuration/MonitorOptions.cs` + `appsettings.json` | `Configuration/MonitorSettings.cs` + `App.config` | One property per setting |
| Taking readings | `Collection/MetricCollector.cs` | same | Reads CPU, memory, disks, network, ... |
| Windows API calls | `Collection/NativeMethods.cs` | same | `GetSystemTimes`, `GetPerformanceInfo`, `NtQuerySystemInformation` |
| Metric names & labels | `Collection/Metrics.cs` | same | Names, report labels, units, table order |
| Database | `Storage/MetricStore.cs` | same | All SQL lives here |
| Calendar maths | `Processing/Periods.cs` | same | Start/end of day/week/month/quarter/year, labels, file names |
| Statistics | `Processing/Stats.cs` | same | min/avg/max/p95, combining days into longer periods |
| Rollups | `Processing/RollupService.cs` | same | Builds missing summaries |
| Cleanup | `Processing/CleanupService.cs` | same | Deletes old data and PDFs |
| Housekeeping order | `Processing/MaintenanceService.cs` | same | rollups → reports → cleanup; decides which reports are due |
| Charts | `Reporting/ChartRenderer.cs` | same | ScottPlot line charts → PNG |
| PDF layout | `Reporting/ReportGenerator.cs` | same | Chooses the data and charts, and lays out the PDF with QuestPDF |
| Demo data | `Tools/DemoData.cs` | same | Fake readings for the `demo` command |
| Logging | (built in: `ILogger<T>`) | `Log.cs` | Event Log + console |

## 4.2 The edit → build → test loop

You don't need to install the service to try a change:

```powershell
cd src\ValiITMonitor.Net10            # or src\ValiITMonitor.Net48
dotnet build
.\bin\Debug\net10.0-windows\win-x64\ValiITMonitor.exe sample          # Net48: .\bin\Debug\net48\ValiITMonitor.exe sample
.\bin\Debug\net10.0-windows\win-x64\ValiITMonitor.exe demo C:\Temp\Demo
.\bin\Debug\net10.0-windows\win-x64\ValiITMonitor.exe console --Monitor:SampleIntervalSeconds=5 --Monitor:DataFolder=C:\Temp\Dev
```

In Visual Studio, set the command-line arguments (e.g. `console` or `demo C:\Temp\Demo`) under
*Project → Properties → Debug* and press **F5**. You can set breakpoints anywhere, including
inside the timers.

When you're happy: run `scripts\Build-ValiITMonitor.ps1` and then `scripts\Install-ValiITMonitor.ps1`
(it upgrades in place).

## 4.3 Common changes

### Change a default
Edit the settings file. The defaults in `MonitorOptions.cs` / `MonitorSettings.cs` only apply
when a setting is missing from the file.

### Add a new setting
1. Add a property to `MonitorOptions` (.NET 10) or `MonitorSettings` (.NET 4.8).
2. **.NET 4.8 only:** read it in `MonitorSettings.Load()`, e.g. `MySetting = s.Int("MySetting", 42)`.
   .NET 10 binds it automatically.
3. Add it to `appsettings.json` / `App.config`.
4. Use it: `_options.MySetting`.

### Add a new metric
Example: the number of TCP connections.

1. **Name it** in `Collection/Metrics.cs`:
   ```csharp
   public const string TcpConnections = "net.tcp_connections";
   // and in the Info table:
   [TcpConnections] = ("TCP connections", "", 52),                  // .NET 10
   { TcpConnections, new MetricInfo("TCP connections", "", 52) },   // .NET 4.8
   ```
2. **Read it** in `Collection/MetricCollector.cs`, for example inside `ReadNetwork`:
   ```csharp
   var tcp = IPGlobalProperties.GetIPGlobalProperties().GetTcpIPv4Statistics();
   readings.Add(new Reading(MetricNames.TcpConnections, "", tcp.CurrentConnections));
   ```
3. That's all. It's stored, rolled up, cleaned up and listed in every report's summary table
   automatically. To give it a **chart**, see below.

### Add a chart to the reports
In `Reporting/ReportGenerator.cs`, add a line in `BuildDaily` (raw points) and/or `BuildPeriod`
(daily points), next to the existing `AddChart(...)` calls:

```csharp
// BuildDaily
AddChart(data, "TCP connections", "count", null,
    Line(MetricNames.TcpConnections, "", "Connections", 0));

// BuildPeriod
AddChart(data, "TCP connections (daily)", "count", null,
    Line(MetricNames.TcpConnections, "", "Average", 0, r => r.Avg),
    Line(MetricNames.TcpConnections, "", "Peak", 2, r => r.Max));
```

Arguments: title, unit label, Y-axis maximum (`null` = automatic, `100` for percentages), then the lines.
The last number in `Line(...)` is the colour slot (0 = blue, 1 = orange, 2 = teal, ...).

### Change how charts look
`Reporting/ChartRenderer.cs` controls colours (`Palette`), line width, size (`Width`/`Height`),
fonts, grid and legend. Every chart in every report goes through it.

### Change the PDF layout
`Reporting/ReportGenerator.cs` → `Render()`. QuestPDF describes the page as nested containers:
`page.Header()`, `page.Content().Column(...)`, `page.Footer()`. Ideas:
* Add a logo: `row.ConstantItem(80).Image("logo.png");` in the header.
* Change the headline tiles: `ComposeHighlights()`.
* Change the table columns or number formats: `ComposeTable()` and `Format()`.
* Docs: <https://www.questpdf.com/api-reference/>. QuestPDF Companion can preview layouts live.

### Add a threshold warning (e.g. disk nearly full)
In the sampling step (`SamplingWorker.TakeReading` / `MonitorService.OnSamplingTick`), after
`Collect()`:

```csharp
foreach (var r in readings.Where(r => r.Metric == MetricNames.DiskFreePct && r.Value < 10))
    log.LogWarning("Drive {Drive} is down to {Free:N1}% free", r.Instance, r.Value);   // .NET 4.8: Log.Warn(...)
```

The warning lands in the Event Log, where Task Scheduler ("on an event") or your monitoring tool
can pick it up. Add a setting for the threshold and a "don't repeat for an hour" timer if you
make it permanent.

### Change when a week starts, or what a "quarter" is
`Processing/Periods.cs`. `StartOf()` and `Next()` define every period. For example, a fiscal year
starting in July only needs changes to the `Quarter` and `Year` cases.

### Change the statistics
`Processing/Stats.cs`. For example, add a P99 alongside P95: add a field to `RollupRow`, a column
to the `rollups` table (`MetricStore.Initialize`), and a column to `ComposeTable`.

> **Changing the database layout:** existing databases won't get new columns from
> `CREATE TABLE IF NOT EXISTS`. Either delete the database, or add an
> `ALTER TABLE ... ADD COLUMN` step that checks `PRAGMA user_version` (see `SchemaVersion` in `MetricStore`).

## 4.4 Things to keep in mind

* **Keep the sampling step cheap.** It runs every minute forever. Anything slow (WMI queries,
  walking all processes, network calls) belongs in maintenance, or behind a setting that's off by default.
* **Never let one failure stop the service.** Every reading block in `MetricCollector.Collect` is
  wrapped in `Safe(...)`, and the timers catch exceptions. Keep that pattern for new code.
* **Times:** store UTC (`ts` is Unix seconds); think in local dates for days and reports.
  `Periods.ToUnix()` / `FromUnix()` convert.
* **Both editions share the database format**, so you can switch between them without losing history.
  Point `DataFolder` at the same folder (one service at a time).
