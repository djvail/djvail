using System.Runtime;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace VailITMonitor.Processing;

/// <summary>One housekeeping pass: summaries, then reports, then cleanup. Safe to run at any time.</summary>
public sealed class MaintenanceService(
    RollupService rollups,
    ReportGenerator reports,
    CleanupService cleanup,
    MetricStore store,
    IOptions<MonitorOptions> options,
    ILogger<MaintenanceService> log)
{
    private readonly MonitorOptions _options = options.Value;
    private readonly Lock _gate = new();   // System.Threading.Lock (.NET 9) - a dedicated lock type

    public void RunOnce(DateTime now)
    {
        lock (_gate)
        {
            rollups.Run(now);
            int made = GenerateDueReports(now);
            cleanup.Run(now);

            if (made > 0)
            {
                // PDF/chart creation allocates a lot of short-lived memory; give it back to Windows.
                GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
                GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true);
            }
        }
    }

    private int GenerateDueReports(DateTime now)
    {
        if (!_options.Reports.Enabled) return 0;

        int made = 0;
        var cutoff = (now - TimeSpan.FromMinutes(_options.Reports.DelayMinutes) - _options.SampleInterval * 2).Date;

        foreach (var period in Periods.All)
        {
            if (!_options.Reports.For(period).Enabled) continue;

            // Walk back from the most recently finished period, up to CatchUpPeriods of them.
            var start = Periods.Previous(period, Periods.StartOf(period, cutoff, _options.FirstDayOfWeek), _options.FirstDayOfWeek);
            for (int i = 0; i < Math.Max(1, _options.Reports.CatchUpPeriods); i++, start = Periods.Previous(period, start, _options.FirstDayOfWeek))
            {
                if (Periods.Next(period, start) > cutoff || store.IsReportLogged(period, start)) continue;
                try
                {
                    var file = reports.Generate(period, start);
                    if (file is null) continue;           // no data for that period
                    store.LogReport(period, start, file);
                    log.LogInformation("Created report {File}", file);
                    made++;
                }
                catch (Exception ex)
                {
                    log.LogError(ex, "Failed to create the {Period} report for {Start:yyyy-MM-dd}", Periods.Adjective(period), start);
                }
            }
        }

        return made;
    }
}
