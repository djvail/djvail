using System;
using System.Runtime;
using VailITMonitor.Configuration;
using VailITMonitor.Reporting;
using VailITMonitor.Storage;

namespace VailITMonitor.Processing
{
    /// <summary>One housekeeping pass: summaries, then reports, then cleanup. Safe to run at any time.</summary>
    public sealed class MaintenanceService
    {
        private readonly RollupService _rollups;
        private readonly ReportGenerator _reports;
        private readonly CleanupService _cleanup;
        private readonly MetricStore _store;
        private readonly MonitorSettings _options;
        private readonly object _gate = new object();

        public MaintenanceService(RollupService rollups, ReportGenerator reports, CleanupService cleanup, MetricStore store, MonitorSettings options)
        {
            _rollups = rollups;
            _reports = reports;
            _cleanup = cleanup;
            _store = store;
            _options = options;
        }

        public void RunOnce(DateTime now)
        {
            lock (_gate)
            {
                _rollups.Run(now);
                int made = GenerateDueReports(now);
                _cleanup.Run(now);

                if (made > 0)
                {
                    GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
                    GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, true, true);
                }
            }
        }

        private int GenerateDueReports(DateTime now)
        {
            if (!_options.Reports.Enabled) return 0;

            int made = 0;
            var cutoff = (now - TimeSpan.FromMinutes(_options.Reports.DelayMinutes) - TimeSpan.FromTicks(_options.SampleInterval.Ticks * 2)).Date;

            foreach (var period in Periods.All)
            {
                if (!_options.Reports.For(period).Enabled) continue;

                var start = Periods.Previous(period, Periods.StartOf(period, cutoff, _options.FirstDayOfWeek), _options.FirstDayOfWeek);
                for (int i = 0; i < Math.Max(1, _options.Reports.CatchUpPeriods); i++, start = Periods.Previous(period, start, _options.FirstDayOfWeek))
                {
                    if (Periods.Next(period, start) > cutoff || _store.IsReportLogged(period, start)) continue;
                    try
                    {
                        var file = _reports.Generate(period, start, null);
                        if (file == null) continue;
                        _store.LogReport(period, start, file);
                        Log.Info("Created report " + file);
                        made++;
                    }
                    catch (Exception ex)
                    {
                        Log.Error("Failed to create the " + Periods.Adjective(period) + " report for " + start.ToString("yyyy-MM-dd"), ex);
                    }
                }
            }

            return made;
        }
    }
}
