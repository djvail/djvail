namespace VailITDashboard.Configuration;

// These classes mirror the "Dashboard" section of appsettings.json, the same way MonitorOptions
// does in VailITMonitor. Adding a setting: add a property here, add it to appsettings.json, use it.

public sealed class DashboardOptions
{
    public const string SectionName = "Dashboard";

    /// <summary>Where the database lives. Environment variables are expanded.</summary>
    public string DataFolder { get; set; } = @"%ProgramData%\VailITDashboard";

    /// <summary>The address people use to reach the dashboard. Used for links in alert emails.</summary>
    public string PublicUrl { get; set; } = "http://localhost:5080";

    /// <summary>How often monitors should report. Sent back to them in every acknowledgement.</summary>
    public int ReportIntervalSeconds { get; set; } = 60;

    /// <summary>A machine is shown as "stale" (amber) after this many missed reports.</summary>
    public int StaleAfterMissedReports { get; set; } = 3;

    /// <summary>A machine is "offline" (red) and an alert is sent after this many missed reports.</summary>
    public int OfflineAfterMissedReports { get; set; } = 5;

    /// <summary>Detailed readings older than this are deleted. Charts can show at most this far back.</summary>
    public int KeepReadingsDays { get; set; } = 14;

    /// <summary>Closed alerts older than this are deleted. 0 = keep forever.</summary>
    public int KeepAlertsDays { get; set; } = 365;

    /// <summary>Group a new machine goes into when its monitor sends no serverType hint.</summary>
    public string DefaultGroup { get; set; } = "Unassigned";

    /// <summary>Most report requests one API key may make per minute.</summary>
    public int MaxReportsPerMinutePerKey { get; set; } = 10;

    /// <summary>Fill the database with made-up machines when it is empty (for trying the dashboard out).</summary>
    public bool SeedDemoData { get; set; }

    public AlertOptions Alerts { get; set; } = new();

    public AuthOptions Auth { get; set; } = new();

    /// <summary>Thresholds written into the database the first time the dashboard starts.</summary>
    public List<ThresholdDefault> DefaultThresholds { get; set; } = [];

    // ----- Derived values (not read from the file) -----

    /// <summary>A relative DataFolder is relative to the folder the .exe is in (a service starts in System32).</summary>
    public string ResolvedDataFolder => Path.GetFullPath(Environment.ExpandEnvironmentVariables(DataFolder), AppContext.BaseDirectory);
    public string DatabasePath => Path.Combine(ResolvedDataFolder, "VailITDashboard.db");
    public TimeSpan ReportInterval => TimeSpan.FromSeconds(Math.Max(5, ReportIntervalSeconds));
    public TimeSpan StaleAfter => ReportInterval * Math.Max(1, StaleAfterMissedReports);
    public TimeSpan OfflineAfter => ReportInterval * Math.Max(StaleAfterMissedReports + 1, OfflineAfterMissedReports);
}

/// <summary>Who may open the web pages. The monitor API always uses API keys instead.</summary>
public sealed class AuthOptions
{
    /// <summary>
    /// Password = a sign-in page checked against Users below (works anywhere).
    /// Windows   = Windows/Active Directory sign-in (Kerberos/NTLM), for a dashboard on the domain.
    /// None      = no sign-in at all. Only for a lab, or behind something else that does sign-in.
    /// </summary>
    public string Mode { get; set; } = "Password";

    /// <summary>For Mode = Password. Make a PasswordHash with: VailITDashboard.exe hash-password</summary>
    public List<DashboardUser> Users { get; set; } = [];

    /// <summary>For Mode = Windows: only these users or groups (e.g. "CONTOSO\IT Admins"). Empty = any signed-in user.</summary>
    public string[] AllowedWindowsUsersOrGroups { get; set; } = [];

    /// <summary>How long a password sign-in lasts without activity.</summary>
    public int SessionHours { get; set; } = 12;
}

public sealed class DashboardUser
{
    public string UserName { get; set; } = "";
    public string PasswordHash { get; set; } = "";
}

public sealed class AlertOptions
{
    /// <summary>Master switch for email. Alerts are still recorded and shown when this is off.</summary>
    public bool EmailEnabled { get; set; } = true;

    /// <summary>Who gets alert emails.</summary>
    public string[] To { get; set; } = [];
    public string From { get; set; } = "VailIT Dashboard <dashboard@example.com>";
    public string SubjectPrefix { get; set; } = "[VailIT]";

    /// <summary>A reading must break a threshold this many reports in a row before it alerts
    /// (and be back to normal this many in a row before it clears). Stops one spike alerting.</summary>
    public int ConsecutiveReports { get; set; } = 3;

    /// <summary>While a problem is still open, send a reminder this often. 0 = never remind.</summary>
    public int RemindEveryMinutes { get; set; } = 240;

    /// <summary>Send an email when a problem clears.</summary>
    public bool SendRecovery { get; set; } = true;

    /// <summary>Only email for problems at or above this level: Warning or Critical.</summary>
    public AlertLevel MinimumEmailLevel { get; set; } = AlertLevel.Warning;

    public SmtpOptions Smtp { get; set; } = new();
}

public sealed class SmtpOptions
{
    /// <summary>Empty = don't send; emails are written to the log instead (handy while testing).</summary>
    public string Host { get; set; } = "";
    public int Port { get; set; } = 587;

    /// <summary>StartTls (port 587), SslOnConnect (port 465) or None (an internal relay on port 25).</summary>
    public string Security { get; set; } = "StartTls";
    public string UserName { get; set; } = "";
    public string Password { get; set; } = "";
}

public sealed class ThresholdDefault
{
    public string Metric { get; set; } = "";
    /// <summary>"*" = every instance (every drive, every adapter), "" = the single series, or e.g. "C:".</summary>
    public string Instance { get; set; } = "*";
    public ThresholdDirection Direction { get; set; } = ThresholdDirection.Above;
    public double? Warning { get; set; }
    public double? Critical { get; set; }
}

public enum ThresholdDirection { Above, Below }

/// <summary>Ordered so that "worse" compares greater.</summary>
public enum AlertLevel { Ok = 0, Warning = 1, Critical = 2 }
