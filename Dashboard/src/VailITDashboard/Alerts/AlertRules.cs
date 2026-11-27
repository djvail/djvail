namespace VailITDashboard.Alerts;

/// <summary>What the rules remember about one series (e.g. SRV01 disk.free_pct C:) between reports.</summary>
/// <param name="Active">The level the dashboard currently says this series is at.</param>
/// <param name="ChangeCount">Readings in a row whose level differs from <paramref name="Active"/>.</param>
/// <param name="LastNotified">When an email last went out about it (Unix seconds).</param>
/// <param name="AlertId">The open alert row, if any.</param>
/// <param name="LastTs">Timestamp of the newest reading already counted, so a resent batch isn't counted twice.</param>
public sealed record AlertState(AlertLevel Active, int ChangeCount, long? LastNotified, long? AlertId, long? LastTs = null)
{
    public static readonly AlertState Initial = new(AlertLevel.Ok, 0, null, null);
}

public enum AlertChange
{
    None,
    /// <summary>Was OK, now has a problem.</summary>
    Opened,
    /// <summary>Warning became Critical.</summary>
    Escalated,
    /// <summary>Critical became Warning (recorded, not emailed).</summary>
    Lowered,
    /// <summary>Still a problem, and RemindEveryMinutes has passed.</summary>
    Reminder,
    /// <summary>Back to OK.</summary>
    Resolved,
}

/// <summary>
/// The alert rules, as one pure function so they are easy to read, change and unit test.
/// </summary>
/// <remarks>
/// A level change only "sticks" once it has been seen <c>required</c> readings in a row. So with the default of 3,
/// one CPU spike to 100% doesn't email anyone, three minutes of it does, and it must then be back under the
/// threshold for three readings before the "resolved" email goes out. This stops alerts flapping on and off.
/// </remarks>
public static class AlertRules
{
    public static (AlertState State, AlertChange Change) Step(
        AlertState state, AlertLevel observed, long now, int required, int remindSeconds)
    {
        required = Math.Max(1, required);

        if (observed == state.Active)
        {
            state = state with { ChangeCount = 0 };
            bool due = state.Active > AlertLevel.Ok && remindSeconds > 0
                       && (state.LastNotified is not long last || now - last >= remindSeconds);
            return due ? (state with { LastNotified = now }, AlertChange.Reminder) : (state, AlertChange.None);
        }

        int count = state.ChangeCount + 1;
        if (count < required)
            return (state with { ChangeCount = count }, AlertChange.None);

        var change = state.Active == AlertLevel.Ok ? AlertChange.Opened
                   : observed == AlertLevel.Ok ? AlertChange.Resolved
                   : observed > state.Active ? AlertChange.Escalated
                   : AlertChange.Lowered;

        // Lowering (Critical -> Warning) doesn't email, so it doesn't reset the reminder clock either.
        long? notified = change == AlertChange.Lowered ? state.LastNotified : now;
        return (new AlertState(observed, 0, notified, state.AlertId, state.LastTs), change);
    }

    /// <summary>The level one value is at against one threshold.</summary>
    public static AlertLevel LevelFor(double value, ThresholdDirection direction, double? warning, double? critical)
    {
        bool Breaks(double? limit) => limit is double l && (direction == ThresholdDirection.Above ? value >= l : value <= l);
        if (Breaks(critical)) return AlertLevel.Critical;
        if (Breaks(warning)) return AlertLevel.Warning;
        return AlertLevel.Ok;
    }
}
