namespace VailITMonitor.Tools;

/// <summary>
/// Fills a separate demo database with realistic-looking fake readings, so you can see every
/// report type straight away instead of waiting a week/month/quarter/year.
/// </summary>
public static class DemoData
{
    public static void Seed(MetricStore store, DateTime fromDay, DateTime toDay, TimeSpan interval, int rawDays)
    {
        var random = new Random(42);
        string[] drives = ["C:", "D:"];
        double[] diskFree = [38, 71];
        double diskGrowthPerDay = 0.012;   // % of the drive filled per day
        var firstRawDay = toDay.AddDays(-rawDays);

        for (var day = fromDay; day < toDay; day = day.AddDays(1))
        {
            bool weekend = day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday;
            var rows = new Dictionary<MetricKey, List<double>>();
            var batch = new List<(long, IReadOnlyCollection<Reading>)>();

            for (var t = day; t < day.AddDays(1); t += interval)
            {
                double hour = t.TimeOfDay.TotalHours;
                // Busy during office hours, quiet at night, with a backup spike at 2am.
                double load = (weekend ? 0.3 : 1.0) * Math.Max(0, Math.Sin((hour - 7) / 11 * Math.PI)) + (hour is >= 2 and < 2.5 ? 0.8 : 0);
                double cpu = Clamp(6 + 45 * load + random.NextDouble() * 12);
                double mem = Clamp(48 + 20 * load + random.NextDouble() * 4 + (day - fromDay).TotalDays * 0.01);

                var readings = new List<Reading>
                {
                    new(MetricNames.CpuTotal, "", cpu),
                    new(MetricNames.MemUsedPct, "", mem),
                    new(MetricNames.MemUsedMb, "", Math.Round(mem / 100 * 16384, 1)),
                    new(MetricNames.MemAvailableMb, "", Math.Round((100 - mem) / 100 * 16384, 1)),
                    new(MetricNames.MemCommitPct, "", Clamp(mem * 0.8 + 5)),
                    new(MetricNames.DiskReadKBps, "", Math.Round(200 + 3000 * load * random.NextDouble(), 1)),
                    new(MetricNames.DiskWriteKBps, "", Math.Round(150 + 1800 * load * random.NextDouble(), 1)),
                    new(MetricNames.DiskBusyPct, "", Clamp(2 + 30 * load * random.NextDouble())),
                    new(MetricNames.NetRecvKBps, MetricNames.Total, Math.Round(40 + 900 * load * random.NextDouble(), 1)),
                    new(MetricNames.NetSentKBps, MetricNames.Total, Math.Round(20 + 400 * load * random.NextDouble(), 1)),
                    new(MetricNames.Processes, "", 140 + random.Next(0, 15)),
                    new(MetricNames.Threads, "", 1900 + random.Next(0, 300)),
                    new(MetricNames.SelfMemMb, "", 28 + random.NextDouble() * 4),
                    new(MetricNames.SelfCpuPct, "", Math.Round(random.NextDouble() * 0.05, 3)),
                };
                for (int core = 0; core < 4; core++)
                    readings.Add(new(MetricNames.CpuCore, core.ToString(), Clamp(cpu + random.NextDouble() * 10 - 5)));
                for (int d = 0; d < drives.Length; d++)
                {
                    double free = diskFree[d] - (day - fromDay).TotalDays * diskGrowthPerDay * (d + 1) + random.NextDouble() * 0.05;
                    readings.Add(new(MetricNames.DiskFreePct, drives[d], Math.Round(free, 2)));
                    readings.Add(new(MetricNames.DiskFreeGb, drives[d], Math.Round(free / 100 * 476, 2)));
                }

                if (day >= firstRawDay)
                    batch.Add((Periods.ToUnix(t), readings));
                else
                    foreach (var r in readings)
                    {
                        var key = new MetricKey(r.Metric, r.Instance);
                        if (!rows.TryGetValue(key, out var list)) rows[key] = list = [];
                        list.Add(r.Value);
                    }
            }

            if (batch.Count > 0) store.WriteSamples(batch);

            // Older days only get their daily summary - just like real data after the 30-day cleanup.
            if (rows.Count > 0)
                store.SaveRollups(PeriodType.Day, day, rows.Select(kv => Stats.FromValues(kv.Key, kv.Value)));
        }
    }

    private static double Clamp(double v) => Math.Round(Math.Clamp(v, 0, 100), 2);
}
