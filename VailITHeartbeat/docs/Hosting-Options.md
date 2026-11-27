# Where to run the heartbeat checker

The one rule: **run it somewhere that does not fail at the same time as the dashboard.** If it sits on
the dashboard server, in the same VM host, or behind the same internet line and power, a single outage
takes out both the dashboard and the thing that is meant to tell you about it. Ideally it also reaches
the dashboard the same way you do (the public URL, or through the VPN), so it tests that path too.

It also needs to be able to send email **without** depending on the dashboard's network. Use an
external SMTP service (Microsoft 365, Gmail/Google Workspace, SendGrid, Mailgun, Amazon SES and so on),
not an SMTP relay that lives in the same building as the dashboard.

## The options

| Option | Cost | Independence | Effort | Good for |
|---|---|---|---|---|
| **1. Small cloud VM** (Linux, systemd) | ~$4–6/month (Hetzner, DigitalOcean, Linode, Azure B1ls, AWS t4g.nano) | Excellent | Medium | The main recommendation if the dashboard is reachable from the internet. |
| **2. A Windows machine at another site** (service or Task Scheduler) | Free if you have one | Good (different power, network) | Low | When the dashboard is internal-only and you have a second office or a home machine on the VPN. |
| **3. GitHub Actions schedule** | Free | Excellent | Low | A free backup. Runs can be late by several minutes. |
| **4. Azure Container Apps job / AWS scheduled task** | Pennies per month | Excellent | Medium-high | If you already use that cloud. Run the `once` command on a timer. |
| **5. Hosted uptime service** (UptimeRobot, Better Stack, healthchecks.io, Pingdom) | Free tiers exist | Excellent | Lowest | An extra layer, or a stand-in while you set the others up. No code to maintain, but less to tweak. |

### 1. Small cloud VM (recommended)

Publish for Linux and install the systemd unit:

```bash
dotnet publish src/VailITHeartbeat -c Release -r linux-x64 --self-contained -p:PublishSingleFile=true -o publish/linux-x64
sudo mkdir -p /opt/vailit-heartbeat && sudo cp -r publish/linux-x64/* /opt/vailit-heartbeat/
sudo nano /opt/vailit-heartbeat/appsettings.json         # dashboard URL, SMTP
sudo cp deploy/vailit-heartbeat.service /etc/systemd/system/
sudo systemctl edit vailit-heartbeat                     # [Service] Environment=Email__Password=...
sudo systemctl enable --now vailit-heartbeat
journalctl -u vailit-heartbeat -f
```

Use `-r linux-arm64` for ARM VMs or a Raspberry Pi. The program is small and sleeps between checks, so the
smallest VM size is plenty.

If the dashboard is internal-only, the VM needs a way in: a site-to-site VPN, Tailscale/WireGuard on
both ends, or a reverse proxy that exposes **only** `/api/v1/health` to the VM's IP address.

### 2. Windows machine at another site

Either install the service:

```powershell
dotnet publish src\VailITHeartbeat -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o publish\win-x64
.\deploy\Install-VailITHeartbeat.ps1
```

or skip the service and use Task Scheduler to run `VailITHeartbeat.exe once` every 5 minutes
("Run whether user is logged on or not"). The state file stops it emailing you on every run.

A laptop that sleeps, or a PC that gets switched off at night, makes a poor watcher: pick something
that stays on.

### 3. GitHub Actions

Copy [deploy/github-actions-heartbeat.yml](../deploy/github-actions-heartbeat.yml) to
`.github/workflows/heartbeat.yml` and add the secrets listed at the top of that file. It runs
`once` every 5 minutes on GitHub's machines and keeps the state between runs with the Actions cache.

Caveats: GitHub treats schedules as best effort, so a run is often a few minutes late and occasionally
skipped when GitHub is busy. Scheduled workflows in a public repository are paused after 60 days with no
commits (GitHub emails you first). The dashboard must be reachable from the internet. Each run uses about
a minute of Actions time, which is free for public repositories and within the free monthly allowance
for many private ones (about 8,600 minutes/month at every 5 minutes, which is more than the 2,000 free
private minutes, so use every 15 minutes or a public repository).

### 4. Cloud scheduled container

Build a container from the published Linux output and run `VailITHeartbeat once` on a schedule
(Azure Container Apps job with a cron trigger, AWS EventBridge + ECS Fargate, Google Cloud Run job).
Settings go in environment variables. The state file must live on persistent storage (an Azure Files
share or similar), otherwise every run starts fresh and you lose the "only email once" behaviour.

### 5. Hosted uptime service

These services do the polling for you and email or text you. Point one at
`https://<your-dashboard>/api/v1/health` with a keyword check for `"status":"ok"`.

## Who watches the watcher?

If the heartbeat checker itself stops (the VM is deleted, the service crashes, the Task Scheduler job
is disabled) you would never hear about a dashboard outage. Two cheap fixes, which work well together:

1. **Self-ping (dead man's switch).** Create a free check at healthchecks.io (or Better Stack heartbeats,
   Cronitor), set its period to a little over your interval, and put its URL in `Heartbeat:SelfPingUrl`.
   The checker pings it after every round. If the pings stop, that service emails you.
2. **Two checkers.** Run the VM (option 1) as the main checker and GitHub Actions (option 3) or a hosted
   uptime service (option 5) as a backup. Both can email the same address.

## Suggested setup

* Main: a small Linux VM running the service, checking every 60 s, alert after 3 failures (about 3 minutes).
* Self-ping to a free healthchecks.io check.
* Optional backup: a free hosted uptime monitor on the same health URL.
