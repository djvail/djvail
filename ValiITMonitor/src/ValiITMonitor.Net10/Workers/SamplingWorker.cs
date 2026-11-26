using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ValiITMonitor.Workers;

/// <summary>
/// Takes a reading every SampleIntervalSeconds and saves it.
/// A BackgroundService is the modern equivalent of the OnStart/OnStop + Timer pattern
/// in a .NET Framework ServiceBase - the host starts and stops it for us.
/// </summary>
public sealed class SamplingWorker(
    MetricCollector collector,
    MetricStore store,
    IOptions<MonitorOptions> options,
    ILogger<SamplingWorker> log) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = options.Value.SampleInterval;
        log.LogInformation("Sampling every {Seconds}s into {Database}", interval.TotalSeconds, store.DatabasePath);

        collector.Prime();

        try
        {
            // Line readings up with the clock (e.g. on the minute) so charts and rollups are tidy.
            await Task.Delay(DelayToNextBoundary(interval), stoppingToken);

            // PeriodicTimer (.NET 6) ticks on a fixed schedule and never overlaps itself.
            using var timer = new PeriodicTimer(interval);
            do
            {
                TakeReading();
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown.
        }
    }

    private void TakeReading()
    {
        try
        {
            var readings = collector.Collect();
            store.WriteSamples(DateTimeOffset.UtcNow.ToUnixTimeSeconds(), readings);
        }
        catch (Exception ex)
        {
            // Never let one bad reading stop the service.
            log.LogError(ex, "Failed to take or save a reading");
        }
    }

    private static TimeSpan DelayToNextBoundary(TimeSpan interval)
    {
        long ticks = interval.Ticks;
        long now = DateTime.Now.Ticks;
        return TimeSpan.FromTicks(ticks - (now % ticks));
    }
}
