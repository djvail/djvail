using Microsoft.Extensions.Options;
using VailITDashboard.Alerts;

namespace VailITDashboard.Workers;

/// <summary>
/// Runs in the background: every 30 seconds it looks for monitors that have gone quiet and raises an
/// "offline" alert for them; once an hour it deletes old readings and alerts.
/// </summary>
public sealed class WatchdogWorker(
    MachineStore machines,
    AlertStore alerts,
    AlertEngine engine,
    IOptions<DashboardOptions> options,
    ILogger<WatchdogWorker> log) : BackgroundService
{
    private static readonly TimeSpan CheckEvery = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan CleanupEvery = TimeSpan.FromHours(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            // Give monitors a chance to report after the dashboard itself has been down,
            // so a dashboard restart doesn't flag every server as offline.
            await Task.Delay(options.Value.OfflineAfter, stoppingToken);

            var lastCleanup = DateTime.MinValue;
            using var timer = new PeriodicTimer(CheckEvery);
            do
            {
                try
                {
                    CheckOffline();
                    if (DateTime.UtcNow - lastCleanup >= CleanupEvery)
                    {
                        int r = machines.DeleteOldReadings();
                        int a = alerts.DeleteOldAlerts();
                        if (r + a > 0) log.LogInformation("Cleanup removed {Readings} readings and {Alerts} alerts", r, a);
                        lastCleanup = DateTime.UtcNow;
                    }
                }
                catch (Exception ex)
                {
                    // Never let one bad pass stop the watchdog.
                    log.LogError(ex, "Watchdog check failed");
                }
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown.
        }
    }

    private void CheckOffline()
    {
        long now = UnixTime.Now;
        foreach (var m in machines.GetAll())
        {
            var silent = TimeSpan.FromSeconds(now - m.LastSeen);
            if (silent >= options.Value.OfflineAfter)
                engine.EvaluateOffline(m, silent);
        }
    }
}
