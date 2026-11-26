using System;
using System.Collections.Generic;
using System.Linq;
using VailITMonitor.Configuration;
using VailITMonitor.Storage;

namespace VailITMonitor.Processing
{
    /// <summary>
    /// Turns raw readings into daily summaries, and daily summaries into weekly, monthly,
    /// quarterly and yearly ones. Every run fills in any finished period that has no summary yet,
    /// so downtime is caught up automatically.
    /// </summary>
    public sealed class RollupService
    {
        private readonly MetricStore _store;
        private readonly MonitorSettings _options;

        public RollupService(MetricStore store, MonitorSettings options)
        {
            _store = store;
            _options = options;
        }

        public int Run(DateTime now)
        {
            var firstUnfinishedDay = (now - TimeSpan.FromTicks(_options.SampleInterval.Ticks * 2)).Date;
            int created = RollupDays(firstUnfinishedDay);

            foreach (var period in Periods.AboveDay)
                created += RollupPeriods(period, firstUnfinishedDay);

            return created;
        }

        private int RollupDays(DateTime firstUnfinishedDay)
        {
            long? earliest = _store.GetEarliestSampleTime();
            if (!earliest.HasValue) return 0;

            int created = 0;
            for (var day = Periods.FromUnix(earliest.Value).Date; day < firstUnfinishedDay; day = day.AddDays(1))
            {
                if (_store.RollupExists(PeriodType.Day, day)) continue;
                if (RollupDay(day) > 0)
                {
                    created++;
                    Log.Info("Created daily summary for " + day.ToString("yyyy-MM-dd"));
                }
            }

            return created;
        }

        public int RollupDay(DateTime day)
        {
            var rows = SummariseRaw(day, day.AddDays(1));
            if (rows.Count > 0) _store.SaveRollups(PeriodType.Day, day, rows);
            return rows.Count;
        }

        public List<RollupRow> SummariseRaw(DateTime from, DateTime to)
        {
            var samples = _store.GetSamples(Periods.ToUnix(from), Periods.ToUnix(to));
            return samples.Select(s => Stats.FromValues(s.Key, s.Value.Select(v => v.Value))).ToList();
        }

        private int RollupPeriods(PeriodType period, DateTime firstUnfinishedDay)
        {
            DateTime? firstDay = _store.GetEarliestRollupStart(PeriodType.Day);
            if (!firstDay.HasValue) return 0;

            int created = 0;
            for (var start = Periods.StartOf(period, firstDay.Value, _options.FirstDayOfWeek);
                 Periods.Next(period, start) <= firstUnfinishedDay;
                 start = Periods.Next(period, start))
            {
                if (_store.RollupExists(period, start)) continue;
                if (RollupPeriod(period, start) > 0)
                {
                    created++;
                    Log.Info("Created " + Periods.Adjective(period).ToLowerInvariant() + " summary for " + start.ToString("yyyy-MM-dd"));
                }
            }

            return created;
        }

        public int RollupPeriod(PeriodType period, DateTime start)
        {
            var rows = SummariseDays(start, Periods.Next(period, start));
            if (rows.Count > 0) _store.SaveRollups(period, start, rows);
            return rows.Count;
        }

        public List<RollupRow> SummariseDays(DateTime from, DateTime to)
        {
            var days = _store.GetRollups(PeriodType.Day, from, to);
            return days.Values
                .SelectMany(rows => rows)
                .GroupBy(row => row.Key)
                .Select(g => Stats.Combine(g.Key, g.ToList()))
                .ToList();
        }
    }
}
