namespace VailITMonitor.Collection;

/// <summary>One value from one reading, e.g. ("disk.free_pct", "C:", 42.7).</summary>
/// <remarks>
/// A "record struct" (C# 10) is a value type with Equals/GetHashCode/ToString written for you.
/// Compare with the .NET Framework edition, where the same type is written out by hand.
/// </remarks>
public readonly record struct Reading(string Metric, string Instance, double Value);

/// <summary>Identifies a series: the metric name plus its instance ("" when there is only one).</summary>
public readonly record struct MetricKey(string Metric, string Instance)
{
    public override string ToString() => Instance.Length == 0 ? Metric : $"{Metric}[{Instance}]";
}

/// <summary>Every metric name the collector produces, with a label and unit for reports.</summary>
public static class MetricNames
{
    public const string Total = "_Total";

    public const string CpuTotal = "cpu.total_pct";
    public const string CpuCore = "cpu.core_pct";        // instance = core number

    public const string MemUsedPct = "mem.used_pct";
    public const string MemUsedMb = "mem.used_mb";
    public const string MemAvailableMb = "mem.available_mb";
    public const string MemCommitPct = "mem.commit_pct";   // commit charge vs. limit (RAM + page file)
    public const string MemCacheMb = "mem.cache_mb";

    public const string DiskFreePct = "disk.free_pct";     // instance = drive, e.g. "C:"
    public const string DiskFreeGb = "disk.free_gb";
    public const string DiskUsedGb = "disk.used_gb";

    public const string DiskReadKBps = "diskio.read_kbps";
    public const string DiskWriteKBps = "diskio.write_kbps";
    public const string DiskBusyPct = "diskio.busy_pct";
    public const string DiskQueue = "diskio.queue";

    public const string NetRecvKBps = "net.recv_kbps";     // instance = adapter name or _Total
    public const string NetSentKBps = "net.sent_kbps";

    public const string Processes = "sys.processes";
    public const string Threads = "sys.threads";
    public const string Handles = "sys.handles";
    public const string CpuQueue = "sys.cpu_queue";
    public const string UptimeHours = "sys.uptime_hours";

    public const string SelfMemMb = "self.private_mb";
    public const string SelfCpuPct = "self.cpu_pct";

    private static readonly Dictionary<string, (string Label, string Unit, int Order)> Info = new()
    {
        [CpuTotal] = ("CPU", "%", 10),
        [CpuCore] = ("CPU core", "%", 11),
        [MemUsedPct] = ("Memory used", "%", 20),
        [MemUsedMb] = ("Memory used", "MB", 21),
        [MemAvailableMb] = ("Memory available", "MB", 22),
        [MemCommitPct] = ("Commit charge", "%", 23),
        [MemCacheMb] = ("File cache", "MB", 24),
        [DiskFreePct] = ("Disk free", "%", 30),
        [DiskFreeGb] = ("Disk free", "GB", 31),
        [DiskUsedGb] = ("Disk used", "GB", 32),
        [DiskReadKBps] = ("Disk read", "KB/s", 40),
        [DiskWriteKBps] = ("Disk write", "KB/s", 41),
        [DiskBusyPct] = ("Disk busy", "%", 42),
        [DiskQueue] = ("Disk queue length", "", 43),
        [NetRecvKBps] = ("Network received", "KB/s", 50),
        [NetSentKBps] = ("Network sent", "KB/s", 51),
        [Processes] = ("Processes", "", 60),
        [Threads] = ("Threads", "", 61),
        [Handles] = ("Handles", "", 62),
        [CpuQueue] = ("CPU queue length", "", 63),
        [UptimeHours] = ("Uptime", "hours", 64),
        [SelfMemMb] = ("VailITMonitor memory", "MB", 70),
        [SelfCpuPct] = ("VailITMonitor CPU", "%", 71),
    };

    public static string Label(MetricKey key)
    {
        var label = Info.TryGetValue(key.Metric, out var i) ? i.Label : key.Metric;
        if (key.Instance.Length == 0 || key.Instance == Total) return label;
        return key.Metric == CpuCore ? $"{label} {key.Instance}" : $"{label} ({key.Instance})";
    }

    public static string Unit(string metric) => Info.TryGetValue(metric, out var i) ? i.Unit : "";

    public static int SortOrder(string metric) => Info.TryGetValue(metric, out var i) ? i.Order : 999;
}
