# VailITMonitor

A lightweight Windows service that records system readings (CPU, memory, disk space by drive,
disk activity, network and more) every minute, summarises them into daily, weekly, monthly,
quarterly and yearly figures, produces PDF reports with charts, and cleans up old data on a
schedule you control.

It comes in **two editions with the same features**, so you can compare the old and new ways of
writing .NET:

| | .NET 10 edition | .NET Framework 4.8 edition |
|---|---|---|
| Folder | `src/VailITMonitor.Net10` | `src/VailITMonitor.Net48` |
| Runtime | .NET 10 (LTS, supported to Nov 2028), bundled inside the .exe | .NET Framework 4.8, built into Windows |
| Style | Generic Host, `BackgroundService`, dependency injection, `IOptions`, `ILogger`, C# 14 | `ServiceBase`, timers, hand-wired objects, `ConfigurationManager`, C# 7.3 |
| Settings file | `appsettings.json` | `VailITMonitor.exe.config` |
| Default data folder | `C:\ProgramData\VailITMonitor` | `C:\ProgramData\VailITMonitor-Net48` |

Both editions use the **same database format, the same reports and the same command line**.

## Quick start (on the Windows server, as Administrator)

```powershell
# 1. Build (needs the .NET 10 SDK: winget install Microsoft.DotNet.SDK.10)
.\scripts\Build-VailITMonitor.ps1

# 2. Install and start the service (pick one edition)
.\scripts\Install-VailITMonitor.ps1 -Edition Net10

# 3. Check it is reading the machine correctly
& "C:\Program Files\VailITMonitor\VailITMonitor.exe" sample

# 4. See what the reports look like straight away (uses fake data in a separate folder)
& "C:\Program Files\VailITMonitor\VailITMonitor.exe" demo C:\Temp\VailITDemo

# Remove it again (keeps your data unless you add -RemoveData)
.\scripts\Uninstall-VailITMonitor.ps1
```

If you downloaded the ready-built package, skip step 1: the `publish` folder is already there.

## What you get

* **A reading every minute** (configurable). Rates such as CPU % and KB/s are averages over
  the whole minute, not a single instant.
* **Daily summaries** (min / average / max / 95th percentile) of every metric, rolled up into
  **weekly, monthly, quarterly and yearly** summaries.
* **Cleanup per tier.** By default raw readings are kept for 30 days, daily summaries for 2 years,
  weekly for 3, monthly for 5, and quarterly/yearly forever. Each tier has its own cleanup
  schedule, so the long-term history is touched rarely.
* **PDF reports** for each day, week, month, quarter and year, with charts and a summary table.
  They are saved to `<DataFolder>\Reports\Daily`, `...\Weekly` and so on.
* **Optional reporting to VailIT Dashboard** every minute, with no readings lost during an outage.
  Off until you run `VailITMonitor.exe connect <url> <key>`. See [06](docs/06-Dashboard-Reporting.md).
* **A small footprint.** The service wakes once a minute for a few milliseconds. The service
  records its own memory and CPU use, and the reports show them.

## Documentation

| Guide | What's in it |
|---|---|
| [01 How it works](docs/01-How-It-Works.md) | Architecture, what's measured and how, the database, rollups, reports, cleanup |
| [02 Build, install & remove](docs/02-Build-Install.md) | Step-by-step for both editions, upgrades, side-by-side installs, troubleshooting |
| [03 Configuration](docs/03-Configuration.md) | Every setting, in both file formats |
| [04 Editing the code](docs/04-Editing-The-Code.md) | A tour of the source, and how to make common changes (add a metric, change a chart, ...) |
| [05 .NET Framework vs modern .NET](docs/05-Framework-vs-Modern-NET.md) | A guided side-by-side comparison of the two editions, for Framework developers learning modern .NET |
| [06 Reporting to the dashboard](docs/06-Dashboard-Reporting.md) | Connecting a monitor to VailIT Dashboard, what is sent, retries and outages, security |

## Folder layout

```
VailITMonitor/
├─ VailITMonitor.sln                 open this in Visual Studio (contains both editions)
├─ src/
│  ├─ VailITMonitor.Net10/           modern .NET 10 edition
│  └─ VailITMonitor.Net48/           .NET Framework 4.8 edition
├─ scripts/
│  ├─ Build-VailITMonitor.ps1        build both editions into .\publish
│  ├─ Install-VailITMonitor.ps1      install/upgrade the service
│  └─ Uninstall-VailITMonitor.ps1    remove the service
├─ docs/                             the guides above
├─ samples/                          example PDF reports made from demo data
└─ publish/                          (built output, in the ready-built package)
```

## Third-party components

| Component | Used for | Licence |
|---|---|---|
| [Microsoft.Data.Sqlite](https://www.nuget.org/packages/Microsoft.Data.Sqlite) | the database | MIT |
| [QuestPDF](https://www.questpdf.com) | PDF layout | Community licence: free for individuals and companies with under USD 1M annual revenue |
| [ScottPlot](https://scottplot.net) | charts | MIT |
| Microsoft.Extensions.Hosting.WindowsServices (.NET 10 only) | running as a service | MIT |

If QuestPDF's licence does not fit, the whole PDF layer is in one file per edition
(`Reporting/ReportGenerator.cs`) and can be switched to another library such as PdfSharp/MigraDoc (MIT).
