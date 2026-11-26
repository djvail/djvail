using System;
using System.Collections.Generic;
using System.Linq;
using VailITMonitor.Collection;
using VailITMonitor.Processing;
using VailITMonitor.Storage;

namespace VailITMonitor.Tools
{
    /// <summary>Fills a separate demo database with realistic-looking fake readings.</summary>
    public static class DemoData
    {
        public static void Seed(MetricStore store, DateTime fromDay, DateTime toDay, TimeSpan interval, int rawDays)
        {
            var random = new Random(42);
            string[] drives = { "C:", "D:" };
            double[] diskFree = { 38, 71 };
            const double diskGrowthPerDay = 0.012;
            var firstRawDay = toDay.AddDays(-rawDays);

            for (var day = fromDay; day < toDay; day = day.AddDays(1))
            {
                bool weekend = day.DayOfWeek == DayOfWeek.Saturday || day.DayOfWeek == DayOfWeek.Sunday;
                var rows = new Dictionary<MetricKey, List<double>>();
                var batch = new List<KeyValuePair<long, ICollection<Reading>>>();

                for (var t = day; t < day.AddDays(1); t += interval)
                {
                    double hour = t.TimeOfDay.TotalHours;
                    double load = (weekend ? 0.3 : 1.0) * Math.Max(0, Math.Sin((hour - 7) / 11 * Math.PI)) + (hour >= 2 && hour < 2.5 ? 0.8 : 0);
                    double cpu = Stats.ClampPercent(6 + 45 * load + random.NextDouble() * 12);
                    double mem = Stats.ClampPercent(48 + 20 * load + random.NextDouble() * 4 + (day - fromDay).TotalDays * 0.01);

                    var readings = new List<Reading>
                    {
                        new Reading(MetricNames.CpuTotal, "", cpu),
                        new Reading(MetricNames.MemUsedPct, "", mem),
                        new Reading(MetricNames.MemUsedMb, "", Math.Round(mem / 100 * 16384, 1)),
                        new Reading(MetricNames.MemAvailableMb, "", Math.Round((100 - mem) / 100 * 16384, 1)),
                        new Reading(MetricNames.MemCommitPct, "", Stats.ClampPercent(mem * 0.8 + 5)),
                        new Reading(MetricNames.DiskReadKBps, "", Math.Round(200 + 3000 * load * random.NextDouble(), 1)),
                        new Reading(MetricNames.DiskWriteKBps, "", Math.Round(150 + 1800 * load * random.NextDouble(), 1)),
                        new Reading(MetricNames.DiskBusyPct, "", Stats.ClampPercent(2 + 30 * load * random.NextDouble())),
                        new Reading(MetricNames.NetRecvKBps, MetricNames.Total, Math.Round(40 + 900 * load * random.NextDouble(), 1)),
                        new Reading(MetricNames.NetSentKBps, MetricNames.Total, Math.Round(20 + 400 * load * random.NextDouble(), 1)),
                        new Reading(MetricNames.Processes, "", 140 + random.Next(0, 15)),
                        new Reading(MetricNames.Threads, "", 1900 + random.Next(0, 300)),
                        new Reading(MetricNames.SelfMemMb, "", 28 + random.NextDouble() * 4),   // fake - run the real service to compare editions
                        new Reading(MetricNames.SelfCpuPct, "", Math.Round(random.NextDouble() * 0.05, 3)),
                    };
                    for (int core = 0; core < 4; core++)
                        readings.Add(new Reading(MetricNames.CpuCore, core.ToString(), Stats.ClampPercent(cpu + random.NextDouble() * 10 - 5)));
                    for (int d = 0; d < drives.Length; d++)
                    {
                        double free = diskFree[d] - (day - fromDay).TotalDays * diskGrowthPerDay * (d + 1) + random.NextDouble() * 0.05;
                        readings.Add(new Reading(MetricNames.DiskFreePct, drives[d], Math.Round(free, 2)));
                        readings.Add(new Reading(MetricNames.DiskFreeGb, drives[d], Math.Round(free / 100 * 476, 2)));
                    }

                    if (day >= firstRawDay)
                    {
                        batch.Add(new KeyValuePair<long, ICollection<Reading>>(Periods.ToUnix(t), readings));
                    }
                    else
                    {
                        foreach (var r in readings)
                        {
                            var key = new MetricKey(r.Metric, r.Instance);
                            List<double> list;
                            if (!rows.TryGetValue(key, out list))
                            {
                                list = new List<double>();
                                rows[key] = list;
                            }
                            list.Add(r.Value);
                        }
                    }
                }

                if (batch.Count > 0) store.WriteSamples(batch);
                if (rows.Count > 0)
                    store.SaveRollups(PeriodType.Day, day, rows.Select(kv => Stats.FromValues(kv.Key, kv.Value)));
            }
        }
    }
}
