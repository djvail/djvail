namespace VailITHeartbeat;

/// <summary>The "Heartbeat" section of appsettings.json.</summary>
public sealed class HeartbeatOptions
{
    public int IntervalSeconds { get; set; } = 60;
    public string StateFile { get; set; } = "heartbeat-state.json";
    public string SelfPingUrl { get; set; } = "";
    public List<TargetOptions> Targets { get; set; } = [];
}

/// <summary>One URL to watch.</summary>
public sealed class TargetOptions
{
    public string Name { get; set; } = "";
    public string Url { get; set; } = "";
    public int TimeoutSeconds { get; set; } = 10;
    public List<int> ExpectedStatusCodes { get; set; } = [];
    public string ExpectedBodyContains { get; set; } = "";
    /// <summary>If set, the body must be JSON whose top-level property of this name equals <see cref="ExpectedJsonValue"/>.</summary>
    public string ExpectedJsonProperty { get; set; } = "";
    public string ExpectedJsonValue { get; set; } = "";
    public int FailuresBeforeAlert { get; set; } = 3;
    public int ReminderMinutes { get; set; } = 60;
    public int CertificateExpiryWarningDays { get; set; } = 14;

    /// <summary>Name to show in emails and logs (falls back to the URL).</summary>
    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? Url : Name;
}

/// <summary>The "Email" section of appsettings.json.</summary>
public sealed class EmailOptions
{
    public string SmtpHost { get; set; } = "";
    public int SmtpPort { get; set; } = 587;
    public string Security { get; set; } = "StartTls";
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
    public string From { get; set; } = "";
    public List<string> To { get; set; } = [];
    public string SubjectPrefix { get; set; } = "[VailIT Heartbeat]";
}
