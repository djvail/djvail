namespace ValiITMonitor.Processing;

/// <summary>The maths behind every summary. Pure functions - easy to test and to change.</summary>
public static class Stats
{
    /// <summary>Summarises raw readings: min, average, max and 95th percentile.</summary>
    public static RollupRow FromValues(MetricKey key, IEnumerable<double> values)
    {
        var sorted = values.Order().ToArray();   // Order() is .NET 7+; the Framework edition uses OrderBy(v => v)
        if (sorted.Length == 0) return new RollupRow(key, 0, 0, 0, 0, 0);

        return new RollupRow(
            key,
            Min: sorted[0],
            Avg: Math.Round(sorted.Average(), 3),
            Max: sorted[^1],                      // ^1 = "last element" (C# 8 index-from-end)
            P95: Percentile(sorted, 0.95),
            Count: sorted.Length);
    }

    /// <summary>
    /// Combines smaller summaries (e.g. 7 days) into a bigger one (a week).
    /// Min/max are exact. The average is weighted by how many readings each day had.
    /// P95 is the weighted average of the daily P95s - an approximation, because the raw
    /// readings needed for an exact value were (deliberately) cleaned up.
    /// </summary>
    public static RollupRow Combine(MetricKey key, IReadOnlyCollection<RollupRow> parts)
    {
        long count = parts.Sum(p => (long)p.Count);
        if (count == 0) return new RollupRow(key, 0, 0, 0, 0, 0);

        return new RollupRow(
            key,
            Min: parts.Min(p => p.Min),
            Avg: Math.Round(parts.Sum(p => p.Avg * p.Count) / count, 3),
            Max: parts.Max(p => p.Max),
            P95: Math.Round(parts.Sum(p => p.P95 * p.Count) / count, 3),
            Count: (int)Math.Min(count, int.MaxValue));
    }

    /// <summary>Nearest-rank-with-interpolation percentile of an already sorted array.</summary>
    public static double Percentile(double[] sorted, double p)
    {
        if (sorted.Length == 1) return sorted[0];
        double rank = p * (sorted.Length - 1);
        int lower = (int)Math.Floor(rank);
        int upper = Math.Min(lower + 1, sorted.Length - 1);
        double value = sorted[lower] + (sorted[upper] - sorted[lower]) * (rank - lower);
        return Math.Round(value, 3);
    }
}
