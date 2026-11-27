namespace VailITHeartbeat;

/// <summary>What we remember about one target between checks (saved to the state file).</summary>
public sealed class TargetState
{
    public int ConsecutiveFailures { get; set; }
    public DateTimeOffset? FirstFailureUtc { get; set; }
    /// <summary>True once a "down" email has been sent and no "recovered" email has followed.</summary>
    public bool AlertSent { get; set; }
    public DateTimeOffset? LastAlertUtc { get; set; }
    public string LastError { get; set; } = "";
    public DateTimeOffset? LastCheckUtc { get; set; }
    public DateTimeOffset? LastSuccessUtc { get; set; }
}

public enum AlertKind { None, Down, StillDown, Recovered }

/// <summary>
/// The alerting rules: when to send "down", "still down" and "recovered" emails.
/// Pure logic with no I/O, so it is easy to read, change and test.
/// </summary>
public static class AlertRules
{
    public static AlertKind Apply(TargetState state, TargetOptions target, CheckResult result, DateTimeOffset now)
    {
        state.LastCheckUtc = now;

        if (result.Healthy)
        {
            var wasAlerted = state.AlertSent;
            state.ConsecutiveFailures = 0;
            state.FirstFailureUtc = null;
            state.LastSuccessUtc = now;
            state.LastError = "";
            return wasAlerted ? AlertKind.Recovered : AlertKind.None;
        }

        state.ConsecutiveFailures++;
        state.FirstFailureUtc ??= now;
        state.LastError = result.Summary;

        if (!state.AlertSent)
            return state.ConsecutiveFailures >= Math.Max(1, target.FailuresBeforeAlert) ? AlertKind.Down : AlertKind.None;

        if (target.ReminderMinutes > 0 && state.LastAlertUtc is { } last && now - last >= TimeSpan.FromMinutes(target.ReminderMinutes))
            return AlertKind.StillDown;

        return AlertKind.None;
    }

    /// <summary>Call after the email for <paramref name="kind"/> was sent successfully.</summary>
    public static void MarkSent(TargetState state, AlertKind kind, DateTimeOffset now)
    {
        switch (kind)
        {
            case AlertKind.Down:
            case AlertKind.StillDown:
                state.AlertSent = true;
                state.LastAlertUtc = now;
                break;
            case AlertKind.Recovered:
                state.AlertSent = false;
                state.LastAlertUtc = null;
                break;
        }
    }
}
