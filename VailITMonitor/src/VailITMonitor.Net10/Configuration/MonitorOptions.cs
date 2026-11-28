namespace VailITMonitor.Configuration;

// These classes mirror the "Monitor" section of appsettings.json.
// The .NET configuration binder fills them in automatically (see Program.cs),
// so adding a new setting is: add a property here, add it to appsettings.json, use it.

public sealed class MonitorOptions
{
    public const string SectionName = "Monitor";

    public int SampleIntervalSeconds { get; set; } = 60;
    public string DataFolder { get; set; } = @"%ProgramData%\VailITMonitor";
    public int MaintenanceIntervalMinutes { get; set; } = 15;
    public DayOfWeek FirstDayOfWeek { get; set; } = DayOfWeek.Monday;

    public MetricOptions Metrics { get; set; } = new();
    public RetentionOptions Retention { get; set; } = new();
    public ReportOptions Reports { get; set; } = new();
    public ReportingOptions Reporting { get; set; } = new();

    // ----- Derived values (not read from the file) -----

    public TimeSpan SampleInterval => TimeSpan.FromSeconds(Math.Max(5, SampleIntervalSeconds));

    public string ResolvedDataFolder => Path.GetFullPath(Environment.ExpandEnvironmentVariables(DataFolder));

    public string DatabasePath => Path.Combine(ResolvedDataFolder, "VailITMonitor.db");

    public string ResolvedReportFolder => string.IsNullOrWhiteSpace(Reports.Folder)
        ? Path.Combine(ResolvedDataFolder, "Reports")
        : Path.GetFullPath(Environment.ExpandEnvironmentVariables(Reports.Folder));
}

public sealed class MetricOptions
{
    public bool Cpu { get; set; } = true;
    public bool CpuPerCore { get; set; } = true;
    public bool Memory { get; set; } = true;
    public bool Disks { get; set; } = true;
    public bool DiskIo { get; set; } = true;
    public bool Network { get; set; } = true;
    public bool System { get; set; } = true;
    public bool Self { get; set; } = true;

    /// <summary>Drive names to monitor, e.g. "C:". Empty = all fixed drives.</summary>
    public string[] Drives { get; set; } = [];

    /// <summary>Network adapters whose name or description contains any of these are ignored.</summary>
    public string[] ExcludeAdapters { get; set; } = [];
}

public sealed class TierRetention
{
    /// <summary>Delete data older than this many days. 0 = keep forever.</summary>
    public int KeepDays { get; set; }

    /// <summary>How often (in days) the cleanup for this tier runs.</summary>
    public int CleanupEveryDays { get; set; } = 1;
}

public sealed class RetentionOptions
{
    public TierRetention Raw { get; set; } = new() { KeepDays = 30, CleanupEveryDays = 1 };
    public TierRetention Daily { get; set; } = new() { KeepDays = 730, CleanupEveryDays = 7 };
    public TierRetention Weekly { get; set; } = new() { KeepDays = 1095, CleanupEveryDays = 30 };
    public TierRetention Monthly { get; set; } = new() { KeepDays = 1825, CleanupEveryDays = 90 };
    public TierRetention Quarterly { get; set; } = new() { KeepDays = 0, CleanupEveryDays = 365 };
    public TierRetention Yearly { get; set; } = new() { KeepDays = 0, CleanupEveryDays = 365 };

    // A "switch expression" - modern C#'s compact form of a switch statement.
    public TierRetention For(PeriodType period) => period switch
    {
        PeriodType.Day => Daily,
        PeriodType.Week => Weekly,
        PeriodType.Month => Monthly,
        PeriodType.Quarter => Quarterly,
        PeriodType.Year => Yearly,
        _ => throw new ArgumentOutOfRangeException(nameof(period)),
    };
}

public sealed class ReportTypeOptions
{
    public bool Enabled { get; set; } = true;

    /// <summary>Delete PDF files older than this many days. 0 = keep forever.</summary>
    public int KeepDays { get; set; }
}

public sealed class ReportOptions
{
    public bool Enabled { get; set; } = true;
    public string Folder { get; set; } = "";
    public string CompanyName { get; set; } = "Vail IT";
    public string PageSize { get; set; } = "Letter";
    public int DelayMinutes { get; set; } = 5;
    public int CatchUpPeriods { get; set; } = 3;

    public ReportTypeOptions Daily { get; set; } = new() { KeepDays = 90 };
    public ReportTypeOptions Weekly { get; set; } = new() { KeepDays = 365 };
    public ReportTypeOptions Monthly { get; set; } = new() { KeepDays = 0 };
    public ReportTypeOptions Quarterly { get; set; } = new() { KeepDays = 0 };
    public ReportTypeOptions Yearly { get; set; } = new() { KeepDays = 0 };

    public ReportTypeOptions For(PeriodType period) => period switch
    {
        PeriodType.Day => Daily,
        PeriodType.Week => Weekly,
        PeriodType.Month => Monthly,
        PeriodType.Quarter => Quarterly,
        PeriodType.Year => Yearly,
        _ => throw new ArgumentOutOfRangeException(nameof(period)),
    };
}

/// <summary>
/// Sending readings to VailITDashboard (see docs/06-Dashboard-Reporting.md).
/// Off by default. "VailITMonitor.exe connect" fills these in and saves them to reporting.json.
/// </summary>
public sealed class ReportingOptions
{
    public bool Enabled { get; set; }

    /// <summary>e.g. "https://dashboard.vailit.local:5443". The /api/v1/... paths are added automatically.</summary>
    public string DashboardUrl { get; set; } = "";

    /// <summary>The machine's API key, encrypted with Windows DPAPI ("dpapi:..."). A plain "vmk_..." key also works.</summary>
    public string ApiKeyProtected { get; set; } = "";

    /// <summary>Empty = generated once and kept in &lt;DataFolder&gt;\machine-id.txt.</summary>
    public string MachineId { get; set; } = "";

    /// <summary>Hint for the dashboard's first grouping, e.g. "SQL", "Web", "File".</summary>
    public string ServerType { get; set; } = "";
    public string[] Tags { get; set; } = [];

    public int IntervalSeconds { get; set; } = 60;
    public int TimeoutSeconds { get; set; } = 15;

    /// <summary>For a self-signed dashboard certificate: its thumbprint. Empty = normal certificate checks.</summary>
    public string PinnedCertificateThumbprint { get; set; } = "";

    /// <summary>How much existing history a newly connected monitor sends (minutes).</summary>
    public int InitialBacklogMinutes { get; set; } = 60;
}
