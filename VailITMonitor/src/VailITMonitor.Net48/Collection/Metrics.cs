using System;
using System.Collections.Generic;

namespace VailITMonitor.Collection
{
    /// <summary>One value from one reading, e.g. ("disk.free_pct", "C:", 42.7).</summary>
    public struct Reading
    {
        public Reading(string metric, string instance, double value)
        {
            Metric = metric;
            Instance = instance;
            Value = value;
        }

        public string Metric { get; }
        public string Instance { get; }
        public double Value { get; }
    }

    /// <summary>
    /// Identifies a series: the metric name plus its instance ("" when there is only one).
    /// Used as a dictionary key, so it needs Equals and GetHashCode - written by hand here;
    /// the .NET 10 edition gets them for free from "record struct".
    /// </summary>
    public struct MetricKey : IEquatable<MetricKey>
    {
        public MetricKey(string metric, string instance)
        {
            Metric = metric;
            Instance = instance ?? "";
        }

        public string Metric { get; }
        public string Instance { get; }

        public bool Equals(MetricKey other)
        {
            return string.Equals(Metric, other.Metric, StringComparison.Ordinal)
                && string.Equals(Instance, other.Instance, StringComparison.Ordinal);
        }

        public override bool Equals(object obj)
        {
            return obj is MetricKey && Equals((MetricKey)obj);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return ((Metric ?? "").GetHashCode() * 397) ^ (Instance ?? "").GetHashCode();
            }
        }

        public static bool operator ==(MetricKey a, MetricKey b) { return a.Equals(b); }

        public static bool operator !=(MetricKey a, MetricKey b) { return !a.Equals(b); }

        public override string ToString()
        {
            return Instance.Length == 0 ? Metric : Metric + "[" + Instance + "]";
        }
    }

    /// <summary>Every metric name the collector produces, with a label and unit for reports.</summary>
    public static class MetricNames
    {
        public const string Total = "_Total";

        public const string CpuTotal = "cpu.total_pct";
        public const string CpuCore = "cpu.core_pct";

        public const string MemUsedPct = "mem.used_pct";
        public const string MemUsedMb = "mem.used_mb";
        public const string MemAvailableMb = "mem.available_mb";
        public const string MemCommitPct = "mem.commit_pct";
        public const string MemCacheMb = "mem.cache_mb";

        public const string DiskFreePct = "disk.free_pct";
        public const string DiskFreeGb = "disk.free_gb";
        public const string DiskUsedGb = "disk.used_gb";

        public const string DiskReadKBps = "diskio.read_kbps";
        public const string DiskWriteKBps = "diskio.write_kbps";
        public const string DiskBusyPct = "diskio.busy_pct";
        public const string DiskQueue = "diskio.queue";

        public const string NetRecvKBps = "net.recv_kbps";
        public const string NetSentKBps = "net.sent_kbps";

        public const string Processes = "sys.processes";
        public const string Threads = "sys.threads";
        public const string Handles = "sys.handles";
        public const string CpuQueue = "sys.cpu_queue";
        public const string UptimeHours = "sys.uptime_hours";

        public const string SelfMemMb = "self.private_mb";
        public const string SelfCpuPct = "self.cpu_pct";

        private sealed class MetricInfo
        {
            public MetricInfo(string label, string unit, int order)
            {
                Label = label;
                Unit = unit;
                Order = order;
            }

            public string Label { get; }
            public string Unit { get; }
            public int Order { get; }
        }

        private static readonly Dictionary<string, MetricInfo> Info = new Dictionary<string, MetricInfo>
        {
            { CpuTotal, new MetricInfo("CPU", "%", 10) },
            { CpuCore, new MetricInfo("CPU core", "%", 11) },
            { MemUsedPct, new MetricInfo("Memory used", "%", 20) },
            { MemUsedMb, new MetricInfo("Memory used", "MB", 21) },
            { MemAvailableMb, new MetricInfo("Memory available", "MB", 22) },
            { MemCommitPct, new MetricInfo("Commit charge", "%", 23) },
            { MemCacheMb, new MetricInfo("File cache", "MB", 24) },
            { DiskFreePct, new MetricInfo("Disk free", "%", 30) },
            { DiskFreeGb, new MetricInfo("Disk free", "GB", 31) },
            { DiskUsedGb, new MetricInfo("Disk used", "GB", 32) },
            { DiskReadKBps, new MetricInfo("Disk read", "KB/s", 40) },
            { DiskWriteKBps, new MetricInfo("Disk write", "KB/s", 41) },
            { DiskBusyPct, new MetricInfo("Disk busy", "%", 42) },
            { DiskQueue, new MetricInfo("Disk queue length", "", 43) },
            { NetRecvKBps, new MetricInfo("Network received", "KB/s", 50) },
            { NetSentKBps, new MetricInfo("Network sent", "KB/s", 51) },
            { Processes, new MetricInfo("Processes", "", 60) },
            { Threads, new MetricInfo("Threads", "", 61) },
            { Handles, new MetricInfo("Handles", "", 62) },
            { CpuQueue, new MetricInfo("CPU queue length", "", 63) },
            { UptimeHours, new MetricInfo("Uptime", "hours", 64) },
            { SelfMemMb, new MetricInfo("VailITMonitor memory", "MB", 70) },
            { SelfCpuPct, new MetricInfo("VailITMonitor CPU", "%", 71) },
        };

        public static string Label(MetricKey key)
        {
            MetricInfo info;
            string label = Info.TryGetValue(key.Metric, out info) ? info.Label : key.Metric;
            if (key.Instance.Length == 0 || key.Instance == Total) return label;
            return key.Metric == CpuCore ? label + " " + key.Instance : label + " (" + key.Instance + ")";
        }

        public static string Unit(string metric)
        {
            MetricInfo info;
            return Info.TryGetValue(metric, out info) ? info.Unit : "";
        }

        public static int SortOrder(string metric)
        {
            MetricInfo info;
            return Info.TryGetValue(metric, out info) ? info.Order : 999;
        }
    }
}
