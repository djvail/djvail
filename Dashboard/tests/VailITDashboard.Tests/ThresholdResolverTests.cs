using VailITDashboard.Alerts;
using VailITDashboard.Configuration;
using VailITDashboard.Data;

namespace VailITDashboard.Tests;

public class ThresholdResolverTests
{
    private static readonly Machine Sql01 = new() { MachineId = "aaaa", MachineName = "SQL01", GroupName = "SQL" };

    private static Threshold T(long id, ThresholdScope scope, string value, string instance, double warn, bool enabled = true) => new()
    {
        Id = id, Scope = scope, ScopeValue = value, Metric = "disk.free_pct", Instance = instance,
        Direction = ThresholdDirection.Below, Warning = warn, Enabled = enabled,
    };

    private static readonly SeriesKey DriveD = new("disk.free_pct", "D:");

    [Fact]
    public void Global_applies_when_nothing_else()
    {
        var all = new[] { T(1, ThresholdScope.Global, "", "*", 15) };
        Assert.Equal(1, ThresholdResolver.Resolve(all, Sql01, DriveD)?.Id);
    }

    [Fact]
    public void Machine_beats_group_beats_global()
    {
        var all = new[]
        {
            T(1, ThresholdScope.Global, "", "*", 15),
            T(2, ThresholdScope.Group, "sql", "*", 10),
            T(3, ThresholdScope.Machine, "AAAA", "*", 5),
        };
        Assert.Equal(3, ThresholdResolver.Resolve(all, Sql01, DriveD)?.Id);
        Assert.Equal(2, ThresholdResolver.Resolve(all[..2], Sql01, DriveD)?.Id);
    }

    [Fact]
    public void Exact_instance_beats_star_at_the_same_level()
    {
        var all = new[] { T(1, ThresholdScope.Group, "SQL", "*", 15), T(2, ThresholdScope.Group, "SQL", "D:", 2) };
        Assert.Equal(2, ThresholdResolver.Resolve(all, Sql01, DriveD)?.Id);
        Assert.Equal(1, ThresholdResolver.Resolve(all, Sql01, new SeriesKey("disk.free_pct", "C:"))?.Id);
    }

    [Fact]
    public void Disabled_machine_threshold_switches_the_check_off()
    {
        var all = new[] { T(1, ThresholdScope.Global, "", "*", 15), T(2, ThresholdScope.Machine, "aaaa", "D:", 0, enabled: false) };
        Assert.Null(ThresholdResolver.Resolve(all, Sql01, DriveD));
        Assert.Equal(2, ThresholdResolver.Resolve(all, Sql01, DriveD, includeDisabled: true)?.Id);
    }

    [Fact]
    public void Other_groups_and_machines_are_ignored()
    {
        var all = new[] { T(1, ThresholdScope.Group, "Web", "*", 15), T(2, ThresholdScope.Machine, "bbbb", "*", 15) };
        Assert.Null(ThresholdResolver.Resolve(all, Sql01, DriveD));
    }
}
