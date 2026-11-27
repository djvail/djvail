namespace VailITDashboard.Data;

// Plain data shapes read from the database and shown on the pages.

public sealed class Machine
{
    public string MachineId { get; set; } = "";
    public string MachineName { get; set; } = "";
    public string? DisplayName { get; set; }
    public string GroupName { get; set; } = "";
    public string ServerTypeHint { get; set; } = "";
    public string Tags { get; set; } = "";
    public string AgentVersion { get; set; } = "";
    public string Edition { get; set; } = "";
    public string Os { get; set; } = "";
    public int LogicalProcessors { get; set; }
    public long TotalMemoryMb { get; set; }
    public string DrivesJson { get; set; } = "[]";
    public long? LastBootUtc { get; set; }
    public long FirstSeen { get; set; }
    public long LastSeen { get; set; }
    public long? LastSampleTs { get; set; }
    public string Notes { get; set; } = "";
    public long? MutedUntil { get; set; }

    public string Name => string.IsNullOrWhiteSpace(DisplayName) ? MachineName : DisplayName!;
    public bool IsMuted(long now) => MutedUntil is long m && m > now;
}

/// <summary>How a machine is doing right now, worked out from its last report and open alerts.</summary>
public enum MachineHealth { Ok, Warning, Critical, Stale, Offline, Muted }

public readonly record struct SeriesKey(string Metric, string Instance)
{
    public override string ToString() => Instance.Length == 0 ? Metric : $"{Metric}[{Instance}]";
}

public sealed record LatestReading(string Metric, string Instance, long Ts, double Value)
{
    public SeriesKey Key => new(Metric, Instance);
}

public sealed record ChartPoint(long Ts, double Value);

public sealed class ApiKeyInfo
{
    public long Id { get; set; }
    public string KeyPrefix { get; set; } = "";
    public string? MachineId { get; set; }
    public string Label { get; set; } = "";
    public long Created { get; set; }
    public long? LastUsed { get; set; }
    public long? Revoked { get; set; }
}

public enum ThresholdScope { Global, Group, Machine }

public sealed class Threshold
{
    public long Id { get; set; }
    public ThresholdScope Scope { get; set; }
    /// <summary>Group name for Group scope, machine id for Machine scope, "" for Global.</summary>
    public string ScopeValue { get; set; } = "";
    public string Metric { get; set; } = "";
    /// <summary>"*" = all instances, "" = the single series, or a specific one such as "C:".</summary>
    public string Instance { get; set; } = "*";
    public ThresholdDirection Direction { get; set; }
    public double? Warning { get; set; }
    public double? Critical { get; set; }
    /// <summary>A disabled threshold at machine or group level switches the check off there.</summary>
    public bool Enabled { get; set; } = true;
}

public sealed class Alert
{
    public long Id { get; set; }
    public string MachineId { get; set; } = "";
    public string Metric { get; set; } = "";
    public string Instance { get; set; } = "";
    public AlertLevel Level { get; set; }
    public AlertLevel PeakLevel { get; set; }
    public double? Value { get; set; }
    public double? Threshold { get; set; }
    public string Message { get; set; } = "";
    public long Opened { get; set; }
    public long? Closed { get; set; }
    public long? Acknowledged { get; set; }
    public string? AcknowledgedBy { get; set; }

    public bool IsOpen => Closed is null;
}
