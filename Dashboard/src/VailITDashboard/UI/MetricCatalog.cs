namespace VailITDashboard;

/// <summary>
/// Labels, units and grouping for the metric names VailITMonitor sends (see MetricNames in the monitor).
/// An unknown metric still works: it is shown under "Other" with its raw name.
/// </summary>
public static class MetricCatalog
{
    public const string Total = "_Total";

    public sealed record Info(string Label, string Unit, string Category, int Order, bool Chart = false);

    private static readonly Dictionary<string, Info> Metrics = new(StringComparer.OrdinalIgnoreCase)
    {
        ["cpu.total_pct"] = new("CPU", "%", "CPU", 10, Chart: true),
        ["cpu.core_pct"] = new("CPU core", "%", "CPU", 11),
        ["sys.cpu_queue"] = new("CPU queue length", "", "CPU", 12),
        ["mem.used_pct"] = new("Memory used", "%", "Memory", 20, Chart: true),
        ["mem.used_mb"] = new("Memory used", "MB", "Memory", 21),
        ["mem.available_mb"] = new("Memory available", "MB", "Memory", 22),
        ["mem.commit_pct"] = new("Commit charge", "%", "Memory", 23),
        ["mem.cache_mb"] = new("File cache", "MB", "Memory", 24),
        ["disk.free_pct"] = new("Disk free", "%", "Disk space", 30, Chart: true),
        ["disk.free_gb"] = new("Disk free", "GB", "Disk space", 31),
        ["disk.used_gb"] = new("Disk used", "GB", "Disk space", 32),
        ["diskio.read_kbps"] = new("Disk read", "KB/s", "Disk activity", 40),
        ["diskio.write_kbps"] = new("Disk write", "KB/s", "Disk activity", 41),
        ["diskio.busy_pct"] = new("Disk busy", "%", "Disk activity", 42, Chart: true),
        ["diskio.queue"] = new("Disk queue length", "", "Disk activity", 43),
        ["net.recv_kbps"] = new("Network received", "KB/s", "Network", 50, Chart: true),
        ["net.sent_kbps"] = new("Network sent", "KB/s", "Network", 51, Chart: true),
        ["sys.processes"] = new("Processes", "", "System", 60),
        ["sys.threads"] = new("Threads", "", "System", 61),
        ["sys.handles"] = new("Handles", "", "System", 62),
        ["sys.uptime_hours"] = new("Uptime", "hours", "System", 64),
        ["self.private_mb"] = new("VailITMonitor memory", "MB", "VailITMonitor", 70),
        ["self.cpu_pct"] = new("VailITMonitor CPU", "%", "VailITMonitor", 71),
        ["monitor.offline"] = new("Monitor reporting", "min", "Monitor", 1),
    };

    /// <summary>Metrics offered in the "add threshold" drop-down (the ones that make sense to alert on).</summary>
    public static IEnumerable<(string Metric, Info Info)> Thresholdable =>
        Metrics.Where(m => m.Key != "monitor.offline").OrderBy(m => m.Value.Order).Select(m => (m.Key, m.Value));

    public static Info Get(string metric) =>
        Metrics.TryGetValue(metric, out var i) ? i : new Info(metric, "", "Other", 999);

    public static string Unit(string metric) => Get(metric).Unit;

    /// <summary>"Free" and "available" readings are bad when low; everything else when high.</summary>
    public static ThresholdDirection DefaultDirection(string metric) =>
        metric.Contains("free", StringComparison.OrdinalIgnoreCase) || metric.Contains("available", StringComparison.OrdinalIgnoreCase)
            ? ThresholdDirection.Below : ThresholdDirection.Above;
    public static int SortOrder(string metric) => Get(metric).Order;

    public static string Label(SeriesKey key)
    {
        var label = Get(key.Metric).Label;
        if (key.Instance.Length == 0 || key.Instance == Total) return label;
        return key.Metric.Equals("cpu.core_pct", StringComparison.OrdinalIgnoreCase) ? $"{label} {key.Instance}" : $"{label} ({key.Instance})";
    }

    public static string InstanceLabel(string instance) => instance switch
    {
        "*" => "all",
        "" => "-",
        Total => "total",
        _ => instance,
    };
}

/// <summary>Number, time and duration formatting used on the pages and in emails.</summary>
public static class Format
{
    public static string Value(double v, string unit)
    {
        string n = Math.Abs(v) >= 1000 ? v.ToString("N0") : Math.Abs(v) >= 100 ? v.ToString("0") : v.ToString("0.#");
        return unit switch
        {
            "" => n,
            "%" => n + " %",
            _ => $"{n} {unit}",
        };
    }

    public static string Duration(TimeSpan t) =>
        t.TotalSeconds < 90 ? $"{(int)t.TotalSeconds}s"
        : t.TotalMinutes < 90 ? $"{(int)t.TotalMinutes} min"
        : t.TotalHours < 48 ? $"{t.TotalHours:0.#} h"
        : $"{t.TotalDays:0.#} days";

    public static string Ago(long unixSeconds) => Ago(unixSeconds, UnixTime.Now);

    public static string Ago(long unixSeconds, long now)
    {
        long s = Math.Max(0, now - unixSeconds);
        return s < 10 ? "just now" : Duration(TimeSpan.FromSeconds(s)) + " ago";
    }

    /// <summary>Shown in the dashboard server's own time zone.</summary>
    public static string Local(long unixSeconds) => DateTimeOffset.FromUnixTimeSeconds(unixSeconds).ToLocalTime().ToString("yyyy-MM-dd HH:mm");
    public static string Local(long? unixSeconds) => unixSeconds is long s ? Local(s) : "-";
}
