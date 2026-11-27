# VailIT Dashboard

A small ASP.NET Core (.NET 10) web app that collects reports from every **VailITMonitor**, shows
your servers **grouped by type**, lets you **drill into one machine**, keeps **thresholds per machine
(or per group, or for everyone)**, and **emails you** when something needs a look.

```
VailITMonitor (each server) ──POST /api/v1/reports every minute──▶  VailIT Dashboard  ──email──▶ you
                                                                      ▲
                                    external heartbeat ──GET /api/v1/health
```

## Try it in two minutes

```powershell
cd Dashboard\src\VailITDashboard
dotnet run
```

Open http://localhost:5080. In Development the dashboard needs no sign-in, keeps its database in
`bin\...\App_Data`, and fills an empty database with eight demo servers (one critical, one offline, ...).

Run the tests with `dotnet test` from the `Dashboard` folder.

## Install on a server

```powershell
# as Administrator, from Dashboard\scripts
.\Publish-VailITDashboard.ps1            # builds a self-contained copy into Dashboard\publish
.\Install-VailITDashboard.ps1            # service "VailITDashboard", port 5080, firewall rule
& "C:\Program Files\VailITDashboard\VailITDashboard.exe" hash-password   # make a sign-in password hash
notepad "C:\Program Files\VailITDashboard\appsettings.json"              # user, SMTP, recipients
Restart-Service VailITDashboard
```

Then, for each server: **Monitors & keys → Create key**, and give the key and the dashboard URL to that
server's VailITMonitor. Before a real monitor is ready, `scripts\Send-TestReport.ps1` sends a fake report.

**HTTPS.** Monitors send an API key with every report, so outside a lab put the dashboard behind HTTPS:
either IIS / a reverse proxy in front of it, or give Kestrel a certificate in `appsettings.json`
(`"Kestrel": { "Endpoints": { "Https": { "Url": "https://*:5443", "Certificate": { "Subject": "dashboard.contoso.local", "Store": "My", "Location": "LocalMachine" } } } }`).

## Using it

| Page | What it does |
|---|---|
| **Overview** | Count of healthy / warning / critical / late / offline machines; every machine as a card in its group, problems first. Refreshes every minute. |
| **Machine** (click a card) | Open problems (acknowledge them), charts for 6 h – 14 days with threshold lines, every current reading against its threshold, machine facts, display name / group / notes, mute emails for maintenance, remove a decommissioned server. |
| **Machine → Thresholds** | What applies to each reading and where it comes from, plus this machine's own overrides. |
| **Thresholds** | Global thresholds and per-group ones. |
| **Alerts** | Open problems everywhere, recent history, email settings, "send a test email". |
| **Monitors & keys** | Create, see and revoke API keys. |

### Groups
A new machine is put in the group its monitor suggests (`serverType`, e.g. "SQL"). After that the
dashboard's group wins: change it on the machine page and the monitor can't change it back.

### Thresholds
Three levels, most specific wins: **machine → group → global**. Within a level, an exact instance
(`D:`) beats `*` (every drive). Untick **on** to switch a check off at that level, e.g. a D: drive that
is meant to be full. The defaults in `appsettings.json` are copied in as global thresholds the first time
only; after that edit them in the dashboard.

### Alerts and emails
* A reading must break a threshold for `ConsecutiveReports` reports in a row (default 3 = 3 minutes)
  before it alerts, and be fine for 3 in a row before it clears. One spike doesn't wake anyone.
* Emails go out when a problem **opens**, when warning becomes **critical**, as a **reminder** every
  `RemindEveryMinutes` while it lasts, and when it's **resolved**.
* A monitor that misses `StaleAfterMissedReports` (3) reports shows as *late*; after
  `OfflineAfterMissedReports` (5) it's *offline* and you get an email.
* **Mute** a machine during maintenance: alerts are still recorded, just not emailed.
* With no SMTP host set, emails are written to the log instead, which is handy while testing.

## API (for the monitors and the heartbeat)

| Call | Auth | Notes |
|---|---|---|
| `POST /api/v1/reports` | `X-Api-Key` | Body and response: `Shared/VailIT.Reporting.Contracts`. gzip allowed, 1 MB max, 10 per minute per key. Resends are idempotent. Answers `200` with `acceptedThroughUtc`, or `400` / `401` / `413` / `429`. |
| `GET /api/v1/monitors/me` | `X-Api-Key` | Which machine the key belongs to, its name and group. |
| `GET /api/v1/health` (also `/health`) | none | `200 {"status":"ok","db":"ok","monitorsOnline":N}`, or `503` when the database fails. For the external heartbeat. |
| `GET /api/v1/machines` | page sign-in | Every machine's status as JSON. |

API keys look like `vmk_…`, are shown once, and only their SHA-256 hash is stored. A key is tied to the
first machine that reports with it.

## Settings (`appsettings.json`, section `Dashboard`)

Everything is commented in the file. The ones you will want first: `PublicUrl` (used in email links),
`Auth` (`Password`, `Windows` for Active Directory sign-in, or `None`), `Alerts.To`, `Alerts.Smtp`.
Any setting can also be given on the command line, e.g. `--Dashboard:SeedDemoData=true`.

## Finding your way around the code

```
Dashboard/
├─ src/VailITDashboard/
│  ├─ Program.cs                 start-up: services, sign-in, rate limit, routes
│  ├─ appsettings.json           settings (commented)
│  ├─ Configuration/             DashboardOptions: one class per settings section
│  ├─ Api/ReportingEndpoints.cs  /api/v1/reports, /monitors/me, /health, /machines
│  ├─ Data/                      SQLite: Database (tables, migrations), MachineStore, ApiKeyStore,
│  │                             ThresholdStore, AlertStore, Models
│  ├─ Alerts/                    AlertRules (the rules, one pure function), ThresholdResolver,
│  │                             AlertEngine (applies them), AlertNotifier + EmailWorker (email)
│  ├─ Workers/WatchdogWorker.cs  offline detection every 30 s, hourly cleanup
│  ├─ UI/                        MetricCatalog (labels/units), StatusService, SvgChart, Format
│  ├─ Pages/                     Razor Pages: Index, Machine, MachineThresholds, Thresholds,
│  │                             Alerts, Monitors, Login (+ _ThresholdEditor partial)
│  ├─ Security/PasswordHasher.cs
│  ├─ Tools/DemoData.cs          demo servers for trying it out
│  └─ wwwroot/css/site.css       all styling; colours are variables at the top (light + dark)
├─ tests/VailITDashboard.Tests/  xUnit tests for the rules, thresholds, storage and keys
└─ scripts/                      publish, install, uninstall, send a test report
Shared/VailIT.Reporting.Contracts/   the report classes shared with the monitors (netstandard2.0)
```

Common changes:

* **Alert on a new reading:** add it to `MetricCatalog` (label, unit) and add a threshold on the Thresholds page.
* **Change the alert rules:** `Alerts/AlertRules.cs` (`Step`), with tests in `AlertRulesTests.cs`.
* **Change the email:** `AlertNotifier.Build`.
* **Add a chart:** one `Add(...)` line in `MachineModel.BuildCharts`.
* **Change the database:** add a step to `Database.Migrations`; existing databases upgrade on start-up.
