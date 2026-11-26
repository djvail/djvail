using System;
using System.IO;
using VailITMonitor.Configuration;
using VailITMonitor.Storage;

namespace VailITMonitor.Processing
{
    /// <summary>
    /// Deletes data and report files older than their configured KeepDays. Each tier has its own
    /// schedule (CleanupEveryDays), so long-term history is touched far less often.
    /// </summary>
    public sealed class CleanupService
    {
        private readonly MetricStore _store;
        private readonly MonitorSettings _options;

        public CleanupService(MetricStore store, MonitorSettings options)
        {
            _store = store;
            _options = options;
        }

        public void Run(DateTime now, bool force = false)
        {
            bool deletedAnything = false;

            if (_options.Retention.Raw.KeepDays > 0 && IsDue("cleanup.raw", _options.Retention.Raw, now, force))
            {
                int keep = Math.Max(_options.Retention.Raw.KeepDays, 2);
                int rows = _store.DeleteSamplesBefore(Periods.ToUnix(now.Date.AddDays(-keep)));
                Log.Info("Raw cleanup: removed " + rows + " readings older than " + keep + " days");
                deletedAnything |= rows > 0;
                _store.SetLastRun("cleanup.raw", now);
            }

            foreach (var period in Periods.All)
            {
                var tier = _options.Retention.For(period);
                var task = "cleanup." + period.ToString().ToLowerInvariant();
                if (!IsDue(task, tier, now, force)) continue;

                if (tier.KeepDays > 0)
                {
                    var cutoff = Periods.StartOf(period, now.Date.AddDays(-tier.KeepDays), _options.FirstDayOfWeek);
                    int rows = _store.DeleteRollupsBefore(period, cutoff);
                    Log.Info(Periods.Adjective(period) + " cleanup: removed " + rows + " summary rows before " + cutoff.ToString("yyyy-MM-dd"));
                    deletedAnything |= rows > 0;
                }

                CleanReportFiles(period, now);
                _store.SetLastRun(task, now);
            }

            if (deletedAnything) _store.Compact();
        }

        private bool IsDue(string task, TierRetention tier, DateTime now, bool force)
        {
            if (force) return true;
            DateTime? last = _store.GetLastRun(task);
            return !last.HasValue || (now - last.Value).TotalDays >= Math.Max(1, tier.CleanupEveryDays) - 0.01;
        }

        private void CleanReportFiles(PeriodType period, DateTime now)
        {
            int keepDays = _options.Reports.For(period).KeepDays;
            var folder = Path.Combine(_options.ResolvedReportFolder, Periods.Adjective(period));
            if (keepDays <= 0 || !Directory.Exists(folder)) return;

            foreach (var file in new DirectoryInfo(folder).EnumerateFiles("*.pdf"))
            {
                if (file.LastWriteTime >= now.AddDays(-keepDays)) continue;
                try
                {
                    file.Delete();
                    Log.Info("Deleted old report " + file.Name);
                }
                catch (IOException ex)
                {
                    Log.Warn("Could not delete " + file.FullName + ": " + ex.Message);
                }
            }
        }
    }
}
