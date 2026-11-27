using VailITHeartbeat;

namespace VailITHeartbeat.Tests;

public class EvaluateTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 27, 12, 0, 0, TimeSpan.Zero);

    private static TargetOptions Target() => new()
    {
        Name = "Dashboard",
        Url = "https://dash.example.com/api/v1/health",
        ExpectedJsonProperty = "status",
        ExpectedJsonValue = "ok",
        CertificateExpiryWarningDays = 14,
    };

    [Fact]
    public void Ok_json_is_healthy() =>
        Assert.True(HealthChecker.Evaluate(Target(), 200, """{"status":"ok","db":"ok","monitorsOnline":4}""", null, Now, 50).Healthy);

    [Fact]
    public void Indented_json_is_still_healthy() =>
        Assert.True(HealthChecker.Evaluate(Target(), 200, "{\n  \"status\" : \"ok\"\n}", null, Now, 50).Healthy);

    [Fact]
    public void Http_503_fails() =>
        Assert.False(HealthChecker.Evaluate(Target(), 503, """{"status":"unhealthy"}""", null, Now, 50).Healthy);

    [Fact]
    public void Wrong_status_value_fails()
    {
        var result = HealthChecker.Evaluate(Target(), 200, """{"status":"degraded"}""", null, Now, 50);
        Assert.False(result.Healthy);
        Assert.Contains("degraded", result.Summary);
    }

    [Fact]
    public void Non_json_body_fails() =>
        Assert.False(HealthChecker.Evaluate(Target(), 200, "<html>login</html>", null, Now, 50).Healthy);

    [Fact]
    public void Body_text_check_when_no_json_check()
    {
        var t = Target();
        t.ExpectedJsonProperty = "";
        t.ExpectedBodyContains = "Healthy";
        Assert.True(HealthChecker.Evaluate(t, 200, "Healthy", null, Now, 5).Healthy);
        Assert.False(HealthChecker.Evaluate(t, 200, "nope", null, Now, 5).Healthy);
    }

    [Fact]
    public void Certificate_about_to_expire_fails()
    {
        var result = HealthChecker.Evaluate(Target(), 200, """{"status":"ok"}""", Now.AddDays(5), Now, 50);
        Assert.False(result.Healthy);
        Assert.Contains("certificate", result.Summary);
    }

    [Fact]
    public void Certificate_with_time_left_passes() =>
        Assert.True(HealthChecker.Evaluate(Target(), 200, """{"status":"ok"}""", Now.AddDays(60), Now, 50).Healthy);
}

public class AlertRulesTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 27, 12, 0, 0, TimeSpan.Zero);
    private static readonly CheckResult Ok = new(true, "ok", 200, 10);
    private static readonly CheckResult Bad = new(false, "HTTP 503", 503, 10);
    private static readonly TargetOptions Target = new() { Url = "u", FailuresBeforeAlert = 3, ReminderMinutes = 60 };

    private static AlertKind Step(TargetState s, CheckResult r, DateTimeOffset at)
    {
        var kind = AlertRules.Apply(s, Target, r, at);
        if (kind != AlertKind.None) AlertRules.MarkSent(s, kind, at);
        return kind;
    }

    [Fact]
    public void Alerts_only_after_threshold_then_reminds_then_recovers()
    {
        var s = new TargetState();
        Assert.Equal(AlertKind.None, Step(s, Bad, T0));
        Assert.Equal(AlertKind.None, Step(s, Bad, T0.AddMinutes(1)));
        Assert.Equal(AlertKind.Down, Step(s, Bad, T0.AddMinutes(2)));
        Assert.Equal(T0, s.FirstFailureUtc);
        Assert.Equal(AlertKind.None, Step(s, Bad, T0.AddMinutes(30)));
        Assert.Equal(AlertKind.StillDown, Step(s, Bad, T0.AddMinutes(62)));
        Assert.Equal(AlertKind.None, Step(s, Bad, T0.AddMinutes(100)));
        Assert.Equal(AlertKind.Recovered, Step(s, Ok, T0.AddMinutes(101)));
        Assert.Equal(AlertKind.None, Step(s, Ok, T0.AddMinutes(102)));
        Assert.Equal(0, s.ConsecutiveFailures);
    }

    [Fact]
    public void Single_blip_never_emails()
    {
        var s = new TargetState();
        Assert.Equal(AlertKind.None, Step(s, Bad, T0));
        Assert.Equal(AlertKind.None, Step(s, Ok, T0.AddMinutes(1)));
        Assert.Equal(AlertKind.None, Step(s, Bad, T0.AddMinutes(2)));
        Assert.Equal(1, s.ConsecutiveFailures);
    }

    [Fact]
    public void Failed_email_is_retried_next_round()
    {
        var s = new TargetState { ConsecutiveFailures = 2 };
        Assert.Equal(AlertKind.Down, AlertRules.Apply(s, Target, Bad, T0)); // email "fails": no MarkSent
        Assert.Equal(AlertKind.Down, AlertRules.Apply(s, Target, Bad, T0.AddMinutes(1)));
    }

    [Fact]
    public void No_reminders_when_zero()
    {
        var t = new TargetOptions { Url = "u", FailuresBeforeAlert = 1, ReminderMinutes = 0 };
        var s = new TargetState();
        var k = AlertRules.Apply(s, t, Bad, T0); AlertRules.MarkSent(s, k, T0);
        Assert.Equal(AlertKind.Down, k);
        Assert.Equal(AlertKind.None, AlertRules.Apply(s, t, Bad, T0.AddDays(1)));
    }
}
