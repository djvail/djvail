using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using VailIT.Reporting.Contracts;
using VailITDashboard.Alerts;
using VailITDashboard.Api;
using VailITDashboard.Configuration;
using VailITDashboard.Data;
using VailITDashboard.Security;

namespace VailITDashboard.Tests;

/// <summary>End to end through a real (temporary) SQLite database, without the web server.</summary>
public sealed class EngineTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "vailit-test-" + Guid.NewGuid().ToString("N"));
    private readonly MachineStore _machines;
    private readonly AlertStore _alerts;
    private readonly AlertEngine _engine;
    private readonly ApiKeyStore _keys;

    public EngineTests()
    {
        var options = Options.Create(new DashboardOptions
        {
            DataFolder = _folder,
            Alerts = new AlertOptions { ConsecutiveReports = 3 },
            DefaultThresholds = [new ThresholdDefault { Metric = "cpu.total_pct", Instance = "*", Warning = 85, Critical = 95 }],
        });
        var db = new Database(options);
        db.EnsureCreated();
        var thresholds = new ThresholdStore(db, options, NullLogger<ThresholdStore>.Instance);
        thresholds.SeedDefaultsOnce();
        _machines = new MachineStore(db, options);
        _alerts = new AlertStore(db, options);
        _keys = new ApiKeyStore(db);
        _engine = new AlertEngine(db, _alerts, thresholds, new AlertNotifier(options), options, NullLogger<AlertEngine>.Instance);
    }

    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try { Directory.Delete(_folder, true); } catch (IOException) { }
    }

    private static readonly MonitorIdentity Id = new() { MachineId = Guid.NewGuid(), MachineName = "SRV01", ServerType = "SQL" };

    private Machine Report(DateTime ts, double cpu)
    {
        var samples = new List<SampleDto> { new() { TimestampUtc = ts, Readings = [new ReadingDto { Metric = "cpu.total_pct", Value = cpu }] } };
        _machines.SaveReport(Id, samples);
        var m = _machines.Get(Id.MachineId.ToString("D"))!;
        _engine.EvaluateReport(m, samples);
        return m;
    }

    [Fact]
    public void Three_busy_minutes_open_an_alert_and_three_quiet_ones_close_it()
    {
        var t = new DateTime(2026, 11, 27, 10, 0, 0, DateTimeKind.Utc);
        Report(t, 99); Report(t.AddMinutes(1), 99);
        Assert.Empty(_alerts.GetOpen());
        Report(t.AddMinutes(2), 99);
        var open = Assert.Single(_alerts.GetOpen());
        Assert.Equal(AlertLevel.Critical, open.Level);

        Report(t.AddMinutes(3), 10); Report(t.AddMinutes(4), 10); Report(t.AddMinutes(5), 10);
        Assert.Empty(_alerts.GetOpen());
        Assert.Single(_alerts.GetRecent(10));
    }

    [Fact]
    public void A_resent_report_is_stored_once_and_counted_once()
    {
        var t = new DateTime(2026, 11, 27, 10, 0, 0, DateTimeKind.Utc);
        Report(t, 99); Report(t, 99); Report(t, 99);
        Assert.Empty(_alerts.GetOpen());
        var series = _machines.GetSeries(Id.MachineId.ToString("D"), "cpu.total_pct", "", 0, long.MaxValue, 1);
        Assert.Single(series);
    }

    [Fact]
    public void Group_hint_only_used_for_a_new_machine()
    {
        var m = Report(DateTime.UtcNow, 5);
        Assert.Equal("SQL", m.GroupName);
        _machines.Update(m.MachineId, null, "Databases", "");
        m = Report(DateTime.UtcNow.AddMinutes(1), 5);
        Assert.Equal("Databases", m.GroupName);
    }

    [Fact]
    public void Keys_bind_to_the_first_machine_and_revoke()
    {
        string key = _keys.Create("test");
        var check = _keys.Check(key);
        Assert.True(check.Valid);
        Assert.Null(check.BoundMachineId);
        _keys.MarkUsed(check.KeyId, "abc");
        Assert.Equal("abc", _keys.Check(key).BoundMachineId);
        _keys.Revoke(check.KeyId);
        Assert.False(_keys.Check(key).Valid);
        Assert.False(_keys.Check("vmk_not-a-real-key").Valid);
    }

    [Fact]
    public void Validation_catches_bad_reports()
    {
        Assert.NotNull(ReportingEndpoints.Validate(new MonitorReport()));   // no machine id, no samples
        var ok = new MonitorReport
        {
            Monitor = new MonitorIdentity { MachineId = Guid.NewGuid() },
            Samples = [new SampleDto { TimestampUtc = DateTime.UtcNow, Readings = [new ReadingDto { Metric = "cpu.total_pct", Value = 1 }] }],
        };
        Assert.Null(ReportingEndpoints.Validate(ok));
        ok.Samples[0].Readings[0].Value = double.NaN;
        Assert.NotNull(ReportingEndpoints.Validate(ok));
    }

    [Fact]
    public void Password_hash_round_trip()
    {
        var hash = PasswordHasher.Hash("correct horse");
        Assert.True(PasswordHasher.Verify("correct horse", hash));
        Assert.False(PasswordHasher.Verify("wrong", hash));
    }
}
