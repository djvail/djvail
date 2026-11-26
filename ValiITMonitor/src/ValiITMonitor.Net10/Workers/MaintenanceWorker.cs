using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ValiITMonitor.Workers;

/// <summary>Runs the housekeeping pass (summaries, reports, cleanup) every MaintenanceIntervalMinutes.</summary>
public sealed class MaintenanceWorker(
    MaintenanceService maintenance,
    IOptions<MonitorOptions> options,
    ILogger<MaintenanceWorker> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            // Let Windows finish booting before doing any heavy lifting.
            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);

            using var timer = new PeriodicTimer(TimeSpan.FromMinutes(Math.Max(1, options.Value.MaintenanceIntervalMinutes)));
            do
            {
                try
                {
                    // Task.Run moves the (synchronous) work off the host's thread.
                    await Task.Run(() => maintenance.RunOnce(DateTime.Now), stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)   // exception filter
                {
                    log.LogError(ex, "Maintenance pass failed");
                }
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown.
        }
    }
}
