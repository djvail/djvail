using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace VailITMonitor.Processing;

/// <summary>
/// Deletes data and report files that are older than their configured KeepDays.
/// Each tier has its own schedule (CleanupEveryDays), so the long-term history is
/// touched far less often than the raw readings.
/// </summary>
public sealed class CleanupService(MetricStore store, IOptions<MonitorOptions> options, ILogger<CleanupService> log)
{
    private readonly MonitorOptions _options = options.Value;

    public void Run(DateTime now, bool force = false)
    {
        bool deletedAnything = false;

        // Raw readings. RollupService always runs first, so a day is summarised before it can be deleted.
        if (_options.Retention.Raw.KeepDays > 0 && IsDue("cleanup.raw", _options.Retention.Raw, now, force))
        {
            int keep = Math.Max(_options.Retention.Raw.KeepDays, 2);   // never less than 2 days
            int rows = store.DeleteSamplesBefore(Periods.ToUnix(now.Date.AddDays(-keep)));
            log.LogInformation("Raw cleanup: removed {Rows} readings older than {Days} days", rows, keep);
            deletedAnything |= rows > 0;
            store.SetLastRun("cleanup.raw", now);
        }

        foreach (var period in Periods.All)   // Day, Week, Month, Quarter, Year
        {
            var tier = _options.Retention.For(period);
            var task = $"cleanup.{period.ToString().ToLowerInvariant()}";
            if (!IsDue(task, tier, now, force)) continue;

            if (tier.KeepDays > 0)
            {
                // Only remove a period once it has completely aged out.
                var cutoff = Periods.StartOf(period, now.Date.AddDays(-tier.KeepDays), _options.FirstDayOfWeek);
                int rows = store.DeleteRollupsBefore(period, cutoff);
                log.LogInformation("{Period} cleanup: removed {Rows} summary rows before {Cutoff:yyyy-MM-dd}", Periods.Adjective(period), rows, cutoff);
                deletedAnything |= rows > 0;
            }

            CleanReportFiles(period, now);
            store.SetLastRun(task, now);
        }

        if (deletedAnything) store.Compact();
    }

    private bool IsDue(string task, TierRetention tier, DateTime now, bool force)
    {
        if (force) return true;
        var last = store.GetLastRun(task);
        return last is null || (now - last.Value).TotalDays >= Math.Max(1, tier.CleanupEveryDays) - 0.01;
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
                log.LogInformation("Deleted old report {File}", file.Name);
            }
            catch (IOException ex)
            {
                log.LogWarning("Could not delete {File}: {Message}", file.FullName, ex.Message);
            }
        }
    }
}
