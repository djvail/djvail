using System.Diagnostics;
using System.Net.NetworkInformation;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace VailITMonitor.Collection;

/// <summary>
/// Takes one reading of every enabled metric.
///
/// Rates (CPU %, KB/s) are worked out from the difference between this reading and the previous
/// one, so a value is the AVERAGE over the whole interval, not a single instant. That makes a
/// one-minute interval both cheap and accurate. Call <see cref="Prime"/> once before the first
/// <see cref="Collect"/> so there is a "previous" to compare against.
/// </summary>
public sealed class MetricCollector : IDisposable
{
    private readonly MetricOptions _options;
    private readonly ILogger<MetricCollector> _log;

    // Previous values for delta calculations.
    private long _prevIdle, _prevKernel, _prevUser;
    private NativeMethods.ProcessorPerformanceInformation[] _prevCores = [];
    private readonly Dictionary<string, (long Rx, long Tx)> _prevNet = new(StringComparer.OrdinalIgnoreCase);
    private long _prevNetTimestamp;
    private TimeSpan _prevSelfCpu;
    private long _prevSelfTimestamp;

    // Performance counters (only for disk I/O and the CPU queue - nothing cheaper exists for those).
    private PerformanceCounter? _diskRead, _diskWrite, _diskIdle, _diskQueue, _cpuQueue;
    private bool _primed;
    private readonly Dictionary<string, DateTime> _lastWarning = [];

    public MetricCollector(IOptions<MonitorOptions> options, ILogger<MetricCollector> log)
    {
        _options = options.Value.Metrics;
        _log = log;
    }

    /// <summary>Reads the starting values. The first real reading is then taken one interval later.</summary>
    public void Prime()
    {
        // Same isolation as Collect(): a metric that fails here is simply skipped.
        Safe("CPU", _options.Cpu, () => ReadCpu([]));
        Safe("per-core CPU", _options.CpuPerCore, () => ReadCores([]));
        Safe("network", _options.Network, () => ReadNetwork([]));
        Safe("self", _options.Self, () => ReadSelf([]));
        Safe("disk I/O", _options.DiskIo, CreateDiskCounters);
        Safe("CPU queue", _options.System, () => _cpuQueue = TryCreateCounter("System", "Processor Queue Length", ""));
        _primed = true;
    }

    public List<Reading> Collect()
    {
        if (!_primed) Prime();

        var readings = new List<Reading>(64);

        // Each block is isolated: one failing metric never stops the others being recorded.
        Safe("CPU", _options.Cpu, () => ReadCpu(readings));
        Safe("per-core CPU", _options.CpuPerCore, () => ReadCores(readings));
        Safe("memory/system", _options.Memory || _options.System, () => ReadPerformanceInfo(readings));
        Safe("disk space", _options.Disks, () => ReadDrives(readings));
        Safe("disk I/O", _options.DiskIo, () => ReadDiskIo(readings));
        Safe("network", _options.Network, () => ReadNetwork(readings));
        Safe("self", _options.Self, () => ReadSelf(readings));

        return readings;
    }

    private void Safe(string what, bool enabled, Action action)
    {
        if (!enabled) return;
        try
        {
            action();
        }
        catch (Exception ex)
        {
            // Warn at most once an hour per metric, so a broken counter can't flood the Event Log.
            var now = DateTime.UtcNow;
            if (!_lastWarning.TryGetValue(what, out var last) || now - last > TimeSpan.FromHours(1))
            {
                _lastWarning[what] = now;
                _log.LogWarning(ex, "Could not read {What} metrics (repeats are hidden for an hour)", what);
            }
        }
    }

    // ---------------------------------------------------------------- CPU

    private void ReadCpu(List<Reading> readings)
    {
        if (!NativeMethods.GetSystemTimes(out var idle, out var kernel, out var user)) return;

        long idleDelta = idle - _prevIdle;
        long totalDelta = (kernel - _prevKernel) + (user - _prevUser); // kernel time includes idle time
        (_prevIdle, _prevKernel, _prevUser) = (idle, kernel, user);     // tuple assignment

        if (totalDelta > 0 && _primed)
            readings.Add(new Reading(MetricNames.CpuTotal, "", Clamp(100.0 * (totalDelta - idleDelta) / totalDelta)));
    }

    private unsafe void ReadCores(List<Reading> readings)
    {
        int count = Environment.ProcessorCount;
        var current = new NativeMethods.ProcessorPerformanceInformation[count];
        int size = sizeof(NativeMethods.ProcessorPerformanceInformation) * count;

        fixed (NativeMethods.ProcessorPerformanceInformation* p = current)
        {
            if (NativeMethods.NtQuerySystemInformation(NativeMethods.SystemProcessorPerformanceInformation, p, size, out var returned) != 0)
                return;
            count = Math.Min(count, returned / sizeof(NativeMethods.ProcessorPerformanceInformation));
        }

        if (_primed && _prevCores.Length >= count)
        {
            for (int i = 0; i < count; i++)
            {
                long idle = current[i].IdleTime - _prevCores[i].IdleTime;
                long total = (current[i].KernelTime - _prevCores[i].KernelTime) + (current[i].UserTime - _prevCores[i].UserTime);
                if (total > 0)
                    readings.Add(new Reading(MetricNames.CpuCore, i.ToString(), Clamp(100.0 * (total - idle) / total)));
            }
        }

        _prevCores = current;
    }

    // ---------------------------------------------------------------- Memory + system counts

    private void ReadPerformanceInfo(List<Reading> readings)
    {
        var info = new NativeMethods.PerformanceInformation { Size = System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.PerformanceInformation>() };
        if (!NativeMethods.GetPerformanceInfo(ref info, info.Size)) return;

        double page = info.PageSize;
        const double mb = 1024 * 1024;

        if (_options.Memory)
        {
            double total = info.PhysicalTotal * page / mb;
            double available = info.PhysicalAvailable * page / mb;
            double used = total - available;

            readings.Add(new Reading(MetricNames.MemUsedMb, "", Math.Round(used, 1)));
            readings.Add(new Reading(MetricNames.MemAvailableMb, "", Math.Round(available, 1)));
            readings.Add(new Reading(MetricNames.MemUsedPct, "", total > 0 ? Clamp(100 * used / total) : 0));
            readings.Add(new Reading(MetricNames.MemCacheMb, "", Math.Round(info.SystemCache * page / mb, 1)));
            if (info.CommitLimit > 0)
                readings.Add(new Reading(MetricNames.MemCommitPct, "", Clamp(100.0 * info.CommitTotal / info.CommitLimit)));
        }

        if (_options.System)
        {
            readings.Add(new Reading(MetricNames.Processes, "", info.ProcessCount));
            readings.Add(new Reading(MetricNames.Threads, "", info.ThreadCount));
            readings.Add(new Reading(MetricNames.Handles, "", info.HandleCount));
            readings.Add(new Reading(MetricNames.UptimeHours, "", Math.Round(Environment.TickCount64 / 3_600_000.0, 2)));
            if (_cpuQueue is not null)
                readings.Add(new Reading(MetricNames.CpuQueue, "", _cpuQueue.NextValue()));
        }
    }

    // ---------------------------------------------------------------- Disk space

    private void ReadDrives(List<Reading> readings)
    {
        const double gb = 1024.0 * 1024 * 1024;
        var wanted = _options.Drives.Select(d => d.TrimEnd('\\').ToUpperInvariant()).ToHashSet();

        foreach (var drive in DriveInfo.GetDrives())
        {
            if (drive.DriveType != DriveType.Fixed || !drive.IsReady) continue;

            var name = drive.Name.TrimEnd('\\').ToUpperInvariant(); // "C:"
            if (wanted.Count > 0 && !wanted.Contains(name)) continue;

            double total = drive.TotalSize, free = drive.TotalFreeSpace;
            if (total <= 0) continue;

            readings.Add(new Reading(MetricNames.DiskFreeGb, name, Math.Round(free / gb, 2)));
            readings.Add(new Reading(MetricNames.DiskUsedGb, name, Math.Round((total - free) / gb, 2)));
            readings.Add(new Reading(MetricNames.DiskFreePct, name, Clamp(100 * free / total)));
        }
    }

    // ---------------------------------------------------------------- Disk I/O

    private void CreateDiskCounters()
    {
        _diskRead = TryCreateCounter("PhysicalDisk", "Disk Read Bytes/sec", "_Total");
        _diskWrite = TryCreateCounter("PhysicalDisk", "Disk Write Bytes/sec", "_Total");
        _diskIdle = TryCreateCounter("PhysicalDisk", "% Idle Time", "_Total");
        _diskQueue = TryCreateCounter("PhysicalDisk", "Current Disk Queue Length", "_Total");
    }

    private void ReadDiskIo(List<Reading> readings)
    {
        // For "per second" counters, NextValue() returns the average since the previous call.
        if (_diskRead is not null) readings.Add(new Reading(MetricNames.DiskReadKBps, "", Math.Round(_diskRead.NextValue() / 1024.0, 1)));
        if (_diskWrite is not null) readings.Add(new Reading(MetricNames.DiskWriteKBps, "", Math.Round(_diskWrite.NextValue() / 1024.0, 1)));
        if (_diskIdle is not null) readings.Add(new Reading(MetricNames.DiskBusyPct, "", Clamp(100 - _diskIdle.NextValue())));
        if (_diskQueue is not null) readings.Add(new Reading(MetricNames.DiskQueue, "", _diskQueue.NextValue()));
    }

    private PerformanceCounter? TryCreateCounter(string category, string counter, string instance)
    {
        try
        {
            var pc = new PerformanceCounter(category, counter, instance, readOnly: true);
            pc.NextValue(); // first call primes the counter and always returns 0
            return pc;
        }
        catch (Exception ex)
        {
            // Happens if the counters are disabled or damaged (fix with: lodctr /R in an admin prompt).
            _log.LogWarning("Performance counter {Category}\\{Counter} is unavailable: {Message}", category, counter, ex.Message);
            return null;
        }
    }

    // ---------------------------------------------------------------- Network

    private void ReadNetwork(List<Reading> readings)
    {
        long now = Stopwatch.GetTimestamp();
        double seconds = _prevNetTimestamp == 0 ? 0 : Stopwatch.GetElapsedTime(_prevNetTimestamp, now).TotalSeconds;
        _prevNetTimestamp = now;

        double totalRx = 0, totalTx = 0;
        bool any = false;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up) continue;
            if (nic.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel) continue;
            if (_options.ExcludeAdapters.Any(x => Contains(nic.Name, x) || Contains(nic.Description, x))) continue;

            var stats = nic.GetIPStatistics();
            long rx = stats.BytesReceived, tx = stats.BytesSent;
            seen.Add(nic.Name);

            if (seconds > 0 && _prevNet.TryGetValue(nic.Name, out var prev) && rx >= prev.Rx && tx >= prev.Tx)
            {
                double rxKBps = (rx - prev.Rx) / 1024.0 / seconds;
                double txKBps = (tx - prev.Tx) / 1024.0 / seconds;
                readings.Add(new Reading(MetricNames.NetRecvKBps, nic.Name, Math.Round(rxKBps, 2)));
                readings.Add(new Reading(MetricNames.NetSentKBps, nic.Name, Math.Round(txKBps, 2)));
                totalRx += rxKBps;
                totalTx += txKBps;
                any = true;
            }

            _prevNet[nic.Name] = (rx, tx);
        }

        // Forget adapters that disappeared so a reconnect doesn't produce a bogus spike.
        foreach (var gone in _prevNet.Keys.Where(k => !seen.Contains(k)).ToList())
            _prevNet.Remove(gone);

        if (any)
        {
            readings.Add(new Reading(MetricNames.NetRecvKBps, MetricNames.Total, Math.Round(totalRx, 2)));
            readings.Add(new Reading(MetricNames.NetSentKBps, MetricNames.Total, Math.Round(totalTx, 2)));
        }
    }

    // ---------------------------------------------------------------- This service itself

    private void ReadSelf(List<Reading> readings)
    {
        using var me = Process.GetCurrentProcess();
        long now = Stopwatch.GetTimestamp();
        var cpu = me.TotalProcessorTime;

        if (_prevSelfTimestamp != 0)
        {
            double wall = Stopwatch.GetElapsedTime(_prevSelfTimestamp, now).TotalMilliseconds;
            double used = (cpu - _prevSelfCpu).TotalMilliseconds;
            if (wall > 0)
                readings.Add(new Reading(MetricNames.SelfCpuPct, "", Math.Round(100 * used / wall / Environment.ProcessorCount, 3)));
        }

        readings.Add(new Reading(MetricNames.SelfMemMb, "", Math.Round(me.PrivateMemorySize64 / 1024.0 / 1024.0, 1)));
        (_prevSelfCpu, _prevSelfTimestamp) = (cpu, now);
    }

    // ---------------------------------------------------------------- helpers

    private static double Clamp(double pct) => Math.Round(Math.Clamp(pct, 0, 100), 2);

    private static bool Contains(string? text, string part)
        => !string.IsNullOrEmpty(part) && text?.Contains(part, StringComparison.OrdinalIgnoreCase) == true;

    public void Dispose()
    {
        _diskRead?.Dispose();
        _diskWrite?.Dispose();
        _diskIdle?.Dispose();
        _diskQueue?.Dispose();
        _cpuQueue?.Dispose();
    }
}
