using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace VailITMonitor.Workers;

/// <summary>
/// Sends readings to VailITDashboard (only registered when Reporting:Enabled is true).
/// The delay between cycles comes from the reporter: normally a few seconds after each reading,
/// longer while backing off from an outage.
/// </summary>
public sealed class ReportingWorker(DashboardReporter reporter, ILogger<ReportingWorker> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await reporter.IntroduceAsync(stoppingToken);
            while (!stoppingToken.IsCancellationRequested)
            {
                TimeSpan delay;
                try
                {
                    delay = await reporter.RunCycleAsync(stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // A bug or a database hiccup: log it and carry on; sampling is unaffected.
                    log.LogError(ex, "Reporting cycle failed");
                    delay = TimeSpan.FromMinutes(1);
                }
                await Task.Delay(delay, stoppingToken);
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown.
        }
    }
}
