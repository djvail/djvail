namespace VailITDashboard.Alerts;

/// <summary>
/// Picks the threshold that applies to one series on one machine.
/// </summary>
/// <remarks>
/// Most specific wins:
/// <list type="number">
///   <item>Machine level beats Group level, which beats Global.</item>
///   <item>Within a level, an exact instance ("C:") beats "*" (all instances).</item>
/// </list>
/// If the winner is disabled, the series isn't checked at all. That's how you switch a check off for one
/// machine (e.g. a D: drive that is meant to be full) without changing everyone else.
/// </remarks>
public static class ThresholdResolver
{
    /// <param name="includeDisabled">True to return a disabled winner too (the pages show it as "off").</param>
    public static Threshold? Resolve(IEnumerable<Threshold> all, Machine machine, SeriesKey key, bool includeDisabled = false)
    {
        Threshold? best = null;
        int bestScore = -1;
        foreach (var t in all)
        {
            if (!string.Equals(t.Metric, key.Metric, StringComparison.OrdinalIgnoreCase)) continue;

            int instanceScore = t.Instance == "*" ? 1
                              : string.Equals(t.Instance, key.Instance, StringComparison.OrdinalIgnoreCase) ? 2
                              : 0;
            if (instanceScore == 0) continue;

            int scopeScore = t.Scope switch
            {
                ThresholdScope.Machine when string.Equals(t.ScopeValue, machine.MachineId, StringComparison.OrdinalIgnoreCase) => 3,
                ThresholdScope.Group when string.Equals(t.ScopeValue, machine.GroupName, StringComparison.OrdinalIgnoreCase) => 2,
                ThresholdScope.Global => 1,
                _ => 0,
            };
            if (scopeScore == 0) continue;

            int score = scopeScore * 10 + instanceScore;
            if (score > bestScore)
            {
                best = t;
                bestScore = score;
            }
        }
        return best is not null && (best.Enabled || includeDisabled) ? best : null;
    }
}
