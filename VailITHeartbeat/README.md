# VailITHeartbeat

A small standalone C# program that checks the VailIT dashboard **from the outside** and emails you
when it cannot be reached. The dashboard can alert you about your servers, but it cannot tell you
that it is itself down, offline or unreachable. That is this program's job.

It runs on a different machine from the dashboard (see [Hosting options](docs/Hosting-Options.md)),
calls the dashboard's health endpoint, and sends:

* a **DOWN** email after a few failed checks in a row (one blip does not wake you up),
* a **STILL DOWN** reminder every hour while it stays down (configurable),
* a **RECOVERED** email when it answers again.

## What counts as "down"

The dashboard exposes `GET /api/v1/health` (no login needed). It answers
`200 {"status":"ok","db":"ok","monitorsOnline":N}` when the web app and its database are fine,
and `503` when not. A check fails when any of these happen:

| Problem | Example message in the email |
|---|---|
| No connection (server off, DNS, firewall, network) | `Could not connect: Connection refused` |
| No answer within the timeout (default 10 s) | `No answer within 10 s (timed out).` |
| Wrong HTTP status (503, 500, a redirect to a login page...) | `HTTP 503 (expected 200)` |
| JSON `status` is not `ok` | `"status" is "unhealthy" (expected "ok")` |
| Invalid HTTPS certificate | `Could not connect: The SSL connection could not be established` |
| HTTPS certificate expires within 14 days | `HTTPS certificate expires 2027-01-02 (5 days left).` |

## Quick start

```powershell
# Build a single self-contained .exe (no .NET needed on the target machine)
dotnet publish src\VailITHeartbeat -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o publish\win-x64

# Edit publish\win-x64\appsettings.json: the dashboard URL and your SMTP settings, then:
publish\win-x64\VailITHeartbeat.exe test-email   # check email works
publish\win-x64\VailITHeartbeat.exe once         # one check; exit code 0 = healthy, 1 = failing

# Install as a Windows service (elevated PowerShell)
.\deploy\Install-VailITHeartbeat.ps1
```

For Linux use `-r linux-x64` and [deploy/vailit-heartbeat.service](deploy/vailit-heartbeat.service).

## Commands

| Command | What it does |
|---|---|
| *(none)* | Runs continuously, checking every `IntervalSeconds`. Works as a console app, Windows service or systemd service. |
| `once` | One round of checks, then exits. Use with Task Scheduler, cron or GitHub Actions. Exit code 0 = all healthy, 1 = something failing, 2 = bad configuration. |
| `test-email` | Sends a test email so you can check the SMTP settings. |

## Settings (`appsettings.json`)

| Setting | Default | Meaning |
|---|---|---|
| `Heartbeat:IntervalSeconds` | 60 | How often the service checks. |
| `Heartbeat:StateFile` | `heartbeat-state.json` | Remembers failures and sent emails between runs, so `once` mode does not email on every run. |
| `Heartbeat:SelfPingUrl` | *(empty)* | Optional URL pinged after every round, e.g. a free [healthchecks.io](https://healthchecks.io) check, so you also hear about it if the **checker** stops running. |
| `Targets[]:Name` / `Url` | | What to check. You can list several targets (e.g. the public URL and the internal one). |
| `Targets[]:TimeoutSeconds` | 10 | Give up after this long. |
| `Targets[]:ExpectedStatusCodes` | `[200]` | HTTP codes that count as healthy. |
| `Targets[]:ExpectedJsonProperty` / `ExpectedJsonValue` | `status` / `ok` | The JSON body must contain this property with this value. Leave the property empty to skip. |
| `Targets[]:ExpectedBodyContains` | *(empty)* | Plain-text alternative for endpoints that do not return JSON. |
| `Targets[]:FailuresBeforeAlert` | 3 | Failed checks in a row before the first email. |
| `Targets[]:ReminderMinutes` | 60 | Repeat the email while still down. 0 = only once. |
| `Targets[]:CertificateExpiryWarningDays` | 14 | Fail when the HTTPS certificate is about to expire. 0 = off. |
| `Email:SmtpHost` / `SmtpPort` | | Your mail server, e.g. `smtp.office365.com` / 587. |
| `Email:Security` | `StartTls` | `StartTls` (587), `SslOnConnect` (465) or `None` (internal relay on 25). |
| `Email:Username` / `Password` | | SMTP login. Leave empty for an anonymous relay. |
| `Email:From` / `To` | | Sender, and a list of recipients. |
| `Email:SubjectPrefix` | `[VailIT Heartbeat]` | Makes the emails easy to filter. |

Any setting can also come from an environment variable, which is the better place for the password:
`Email__Password`, `Heartbeat__Targets__0__Url` and so on (double underscore between levels).

## The code

| File | What it does |
|---|---|
| `Program.cs` | Commands, settings and dependency-injection wiring. |
| `HealthChecker.cs` | Makes the HTTP request. `Evaluate()` holds the pass/fail rules. Change these to alter what "healthy" means. |
| `AlertState.cs` | `AlertRules`: when to send DOWN / STILL DOWN / RECOVERED. Pure logic, no I/O. |
| `HeartbeatRunner.cs` | One round: check, decide, email, save state, self-ping. Also the service loop. |
| `EmailNotifier.cs` | Builds and sends the emails (MailKit). Change the wording here. |
| `StateStore.cs` | Saves the per-target state as JSON. |
| `Options.cs` | The settings classes that `appsettings.json` maps to. |

Run the tests with `dotnet test` from this folder.
