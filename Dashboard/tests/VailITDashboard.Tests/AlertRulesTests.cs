using VailITDashboard.Alerts;
using VailITDashboard.Configuration;

namespace VailITDashboard.Tests;

public class AlertRulesTests
{
    private const int Required = 3, Remind = 3600;

    private static (AlertState, AlertChange) Run(AlertState s, long now, params AlertLevel[] levels)
    {
        var change = AlertChange.None;
        foreach (var l in levels) (s, change) = AlertRules.Step(s, l, now++, Required, Remind);
        return (s, change);
    }

    [Fact]
    public void One_spike_does_not_alert()
    {
        var (s, change) = Run(AlertState.Initial, 0, AlertLevel.Critical, AlertLevel.Ok, AlertLevel.Ok);
        Assert.Equal(AlertLevel.Ok, s.Active);
        Assert.Equal(AlertChange.None, change);
    }

    [Fact]
    public void Three_in_a_row_opens_at_the_latest_level()
    {
        var (s, change) = Run(AlertState.Initial, 0, AlertLevel.Warning, AlertLevel.Critical, AlertLevel.Critical);
        Assert.Equal(AlertChange.Opened, change);
        Assert.Equal(AlertLevel.Critical, s.Active);
    }

    [Fact]
    public void Resolving_also_needs_three_good_readings()
    {
        var (open, _) = Run(AlertState.Initial, 0, AlertLevel.Warning, AlertLevel.Warning, AlertLevel.Warning);
        var (s, change) = Run(open, 10, AlertLevel.Ok, AlertLevel.Ok);
        Assert.Equal(AlertLevel.Warning, s.Active);
        Assert.Equal(AlertChange.None, change);
        (s, change) = AlertRules.Step(s, AlertLevel.Ok, 12, Required, Remind);
        Assert.Equal(AlertChange.Resolved, change);
        Assert.Equal(AlertLevel.Ok, s.Active);
    }

    [Fact]
    public void Warning_to_critical_escalates_and_back_lowers()
    {
        var (s, _) = Run(AlertState.Initial, 0, AlertLevel.Warning, AlertLevel.Warning, AlertLevel.Warning);
        var (s2, change) = Run(s, 10, AlertLevel.Critical, AlertLevel.Critical, AlertLevel.Critical);
        Assert.Equal(AlertChange.Escalated, change);
        var (_, change3) = Run(s2, 20, AlertLevel.Warning, AlertLevel.Warning, AlertLevel.Warning);
        Assert.Equal(AlertChange.Lowered, change3);
    }

    [Fact]
    public void Reminder_after_the_interval_only()
    {
        var (s, _) = Run(AlertState.Initial, 0, AlertLevel.Critical, AlertLevel.Critical, AlertLevel.Critical);
        var (_, early) = AlertRules.Step(s, AlertLevel.Critical, 100, Required, Remind);
        Assert.Equal(AlertChange.None, early);
        var (_, late) = AlertRules.Step(s, AlertLevel.Critical, 2 + Remind, Required, Remind);
        Assert.Equal(AlertChange.Reminder, late);
    }

    [Theory]
    [InlineData(84.9, ThresholdDirection.Above, AlertLevel.Ok)]
    [InlineData(85, ThresholdDirection.Above, AlertLevel.Warning)]
    [InlineData(95, ThresholdDirection.Above, AlertLevel.Critical)]
    [InlineData(16, ThresholdDirection.Below, AlertLevel.Ok)]
    [InlineData(15, ThresholdDirection.Below, AlertLevel.Warning)]
    [InlineData(4, ThresholdDirection.Below, AlertLevel.Critical)]
    public void LevelFor_respects_direction(double value, ThresholdDirection d, AlertLevel expected)
    {
        double warn = d == ThresholdDirection.Above ? 85 : 15, crit = d == ThresholdDirection.Above ? 95 : 5;
        Assert.Equal(expected, AlertRules.LevelFor(value, d, warn, crit));
    }

    [Fact]
    public void Critical_only_threshold_works()
    {
        Assert.Equal(AlertLevel.Ok, AlertRules.LevelFor(91, ThresholdDirection.Above, null, 95));
        Assert.Equal(AlertLevel.Critical, AlertRules.LevelFor(96, ThresholdDirection.Above, null, 95));
    }
}
