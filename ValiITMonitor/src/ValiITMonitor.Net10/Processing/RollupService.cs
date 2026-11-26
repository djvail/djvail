using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ValiITMonitor.Processing;

/// <summary>
/// Turns raw readings into daily summaries, and daily summaries into weekly, monthly,
/// quarterly and yearly ones.
///
/// It is "self-healing": every run looks for any finished period that has no summary yet and
/// creates it. So if the server was off for a week, the missing days are filled in at the next
/// start-up (as long as their raw readings still exist).
/// </summary>
public sealed class RollupService(MetricStore store, IOptions<MonitorOptions> options, ILogger<RollupService> log)
{
    // ^ A "primary constructor" (C# 12): the parameters are available throughout the class,
    //   replacing the usual private readonly fields + constructor boilerplate.

    private readonly MonitorOptions _options = options.Value;

    public int Run(DateTime now)
    {
        // A day counts as finished once the last reading of the day has had time to be written.
        var firstUnfinishedDay = (now - _options.SampleInterval * 2).Date;
        int created = RollupDays(firstUnfinishedDay);

        foreach (var period in Periods.AboveDay)
            created += RollupPeriods(period, firstUnfinishedDay);

        return created;
    }

    private int RollupDays(DateTime firstUnfinishedDay)
    {
        if (store.GetEarliestSampleTime() is not long earliest) return 0;

        int created = 0;
        for (var day = Periods.FromUnix(earliest).Date; day < firstUnfinishedDay; day = day.AddDays(1))
        {
            if (store.RollupExists(PeriodType.Day, day)) continue;
            if (RollupDay(day) > 0)
            {
                created++;
                log.LogInformation("Created daily summary for {Day:yyyy-MM-dd}", day);
            }
        }

        return created;
    }

    /// <summary>Summarises one day of raw readings. Returns the number of series saved.</summary>
    public int RollupDay(DateTime day)
    {
        var rows = SummariseRaw(day, day.AddDays(1));
        if (rows.Count > 0) store.SaveRollups(PeriodType.Day, day, rows);
        return rows.Count;
    }

    /// <summary>Summaries straight from raw readings (also used for "today so far" reports).</summary>
    public List<RollupRow> SummariseRaw(DateTime from, DateTime to)
    {
        var samples = store.GetSamples(Periods.ToUnix(from), Periods.ToUnix(to));
        return samples.Select(s => Stats.FromValues(s.Key, s.Value.Select(v => v.Value))).ToList();
    }

    private int RollupPeriods(PeriodType period, DateTime firstUnfinishedDay)
    {
        if (store.GetEarliestRollupStart(PeriodType.Day) is not DateTime firstDay) return 0;

        int created = 0;
        for (var start = Periods.StartOf(period, firstDay, _options.FirstDayOfWeek);
             Periods.Next(period, start) <= firstUnfinishedDay;
             start = Periods.Next(period, start))
        {
            if (store.RollupExists(period, start)) continue;
            if (RollupPeriod(period, start) > 0)
            {
                created++;
                log.LogInformation("Created {Period} summary for {Start:yyyy-MM-dd}", Periods.Adjective(period).ToLowerInvariant(), start);
            }
        }

        return created;
    }

    public int RollupPeriod(PeriodType period, DateTime start)
    {
        var rows = SummariseDays(start, Periods.Next(period, start));
        if (rows.Count > 0) store.SaveRollups(period, start, rows);
        return rows.Count;
    }

    /// <summary>Combines the daily summaries in [from, to) into one summary per series.</summary>
    public List<RollupRow> SummariseDays(DateTime from, DateTime to)
    {
        var days = store.GetRollups(PeriodType.Day, from, to);
        return days.Values
            .SelectMany(rows => rows)
            .GroupBy(row => row.Key)
            .Select(g => Stats.Combine(g.Key, g.ToList()))
            .ToList();
    }
}
