using System;
using System.Collections.Generic;
using System.Linq;
using VailITMonitor.Collection;
using VailITMonitor.Storage;

namespace VailITMonitor.Processing
{
    /// <summary>The maths behind every summary. Pure functions - easy to test and to change.</summary>
    public static class Stats
    {
        /// <summary>Summarises raw readings: min, average, max and 95th percentile.</summary>
        public static RollupRow FromValues(MetricKey key, IEnumerable<double> values)
        {
            var sorted = values.OrderBy(v => v).ToArray();
            if (sorted.Length == 0) return new RollupRow(key, 0, 0, 0, 0, 0);

            return new RollupRow(
                key,
                sorted[0],
                Math.Round(sorted.Average(), 3),
                sorted[sorted.Length - 1],
                Percentile(sorted, 0.95),
                sorted.Length);
        }

        /// <summary>
        /// Combines smaller summaries (e.g. 7 days) into a bigger one (a week).
        /// Min/max are exact; the average is weighted by reading count; P95 is the weighted
        /// average of the parts' P95s (an approximation - the raw readings are gone by then).
        /// </summary>
        public static RollupRow Combine(MetricKey key, ICollection<RollupRow> parts)
        {
            long count = parts.Sum(p => (long)p.Count);
            if (count == 0) return new RollupRow(key, 0, 0, 0, 0, 0);

            return new RollupRow(
                key,
                parts.Min(p => p.Min),
                Math.Round(parts.Sum(p => p.Avg * p.Count) / count, 3),
                parts.Max(p => p.Max),
                Math.Round(parts.Sum(p => p.P95 * p.Count) / count, 3),
                (int)Math.Min(count, int.MaxValue));
        }

        public static double Percentile(double[] sorted, double p)
        {
            if (sorted.Length == 1) return sorted[0];
            double rank = p * (sorted.Length - 1);
            int lower = (int)Math.Floor(rank);
            int upper = Math.Min(lower + 1, sorted.Length - 1);
            double value = sorted[lower] + (sorted[upper] - sorted[lower]) * (rank - lower);
            return Math.Round(value, 3);
        }

        /// <summary>Math.Clamp doesn't exist in .NET Framework.</summary>
        public static double ClampPercent(double value)
        {
            return Math.Round(Math.Max(0, Math.Min(100, value)), 2);
        }
    }
}
