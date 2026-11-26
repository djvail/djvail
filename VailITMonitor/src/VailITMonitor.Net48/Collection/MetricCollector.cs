using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using VailITMonitor.Configuration;
using VailITMonitor.Processing;

namespace VailITMonitor.Collection
{
    /// <summary>
    /// Takes one reading of every enabled metric.
    ///
    /// Rates (CPU %, KB/s) are worked out from the difference between this reading and the previous
    /// one, so a value is the AVERAGE over the whole interval, not a single instant. Call
    /// <see cref="Prime"/> once before the first <see cref="Collect"/>.
    /// </summary>
    public sealed class MetricCollector : IDisposable
    {
        private readonly MetricSettings _options;

        private long _prevIdle, _prevKernel, _prevUser;
        private NativeMethods.ProcessorPerformanceInformation[] _prevCores = new NativeMethods.ProcessorPerformanceInformation[0];
        private readonly Dictionary<string, long[]> _prevNet = new Dictionary<string, long[]>(StringComparer.OrdinalIgnoreCase);
        private readonly Stopwatch _netClock = new Stopwatch();
        private readonly Stopwatch _selfClock = new Stopwatch();
        private TimeSpan _prevSelfCpu;

        // Performance counters are built into .NET Framework (System.dll) - no NuGet package needed.
        private PerformanceCounter _diskRead, _diskWrite, _diskIdle, _diskQueue, _cpuQueue;
        private bool _primed;
        private readonly Dictionary<string, DateTime> _lastWarning = new Dictionary<string, DateTime>();

        public MetricCollector(MetricSettings options)
        {
            _options = options;
        }

        public void Prime()
        {
            // Same isolation as Collect(): a metric that fails here is simply skipped.
            Safe("CPU", _options.Cpu, () => ReadCpu(new List<Reading>()));
            Safe("per-core CPU", _options.CpuPerCore, () => ReadCores(new List<Reading>()));
            Safe("network", _options.Network, () => ReadNetwork(new List<Reading>()));
            Safe("self", _options.Self, () => ReadSelf(new List<Reading>()));
            Safe("disk I/O", _options.DiskIo, CreateDiskCounters);
            Safe("CPU queue", _options.System, () => _cpuQueue = TryCreateCounter("System", "Processor Queue Length", ""));
            _primed = true;
        }

        public List<Reading> Collect()
        {
            if (!_primed) Prime();

            var readings = new List<Reading>(64);
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
                DateTime last;
                var now = DateTime.UtcNow;
                if (!_lastWarning.TryGetValue(what, out last) || now - last > TimeSpan.FromHours(1))
                {
                    _lastWarning[what] = now;
                    Log.Warn("Could not read " + what + " metrics (repeats are hidden for an hour): " + ex.Message);
                }
            }
        }

        // ---------------------------------------------------------------- CPU

        private void ReadCpu(List<Reading> readings)
        {
            long idle, kernel, user;
            if (!NativeMethods.GetSystemTimes(out idle, out kernel, out user)) return;

            long idleDelta = idle - _prevIdle;
            long totalDelta = (kernel - _prevKernel) + (user - _prevUser);
            _prevIdle = idle;
            _prevKernel = kernel;
            _prevUser = user;

            if (totalDelta > 0 && _primed)
                readings.Add(new Reading(MetricNames.CpuTotal, "", Stats.ClampPercent(100.0 * (totalDelta - idleDelta) / totalDelta)));
        }

        private void ReadCores(List<Reading> readings)
        {
            int count = Environment.ProcessorCount;
            var current = new NativeMethods.ProcessorPerformanceInformation[count];
            int itemSize = Marshal.SizeOf(typeof(NativeMethods.ProcessorPerformanceInformation));
            int returned;
            if (NativeMethods.NtQuerySystemInformation(NativeMethods.SystemProcessorPerformanceInformation, current, itemSize * count, out returned) != 0)
                return;
            count = Math.Min(count, returned / itemSize);

            if (_primed && _prevCores.Length >= count)
            {
                for (int i = 0; i < count; i++)
                {
                    long idle = current[i].IdleTime - _prevCores[i].IdleTime;
                    long total = (current[i].KernelTime - _prevCores[i].KernelTime) + (current[i].UserTime - _prevCores[i].UserTime);
                    if (total > 0)
                        readings.Add(new Reading(MetricNames.CpuCore, i.ToString(), Stats.ClampPercent(100.0 * (total - idle) / total)));
                }
            }

            _prevCores = current;
        }

        // ---------------------------------------------------------------- Memory + system counts

        private void ReadPerformanceInfo(List<Reading> readings)
        {
            var info = new NativeMethods.PerformanceInformation();
            info.Size = Marshal.SizeOf(typeof(NativeMethods.PerformanceInformation));
            if (!NativeMethods.GetPerformanceInfo(ref info, info.Size)) return;

            double page = info.PageSize.ToUInt64();
            const double mb = 1024 * 1024;

            if (_options.Memory)
            {
                double total = info.PhysicalTotal.ToUInt64() * page / mb;
                double available = info.PhysicalAvailable.ToUInt64() * page / mb;
                double used = total - available;

                readings.Add(new Reading(MetricNames.MemUsedMb, "", Math.Round(used, 1)));
                readings.Add(new Reading(MetricNames.MemAvailableMb, "", Math.Round(available, 1)));
                readings.Add(new Reading(MetricNames.MemUsedPct, "", total > 0 ? Stats.ClampPercent(100 * used / total) : 0));
                readings.Add(new Reading(MetricNames.MemCacheMb, "", Math.Round(info.SystemCache.ToUInt64() * page / mb, 1)));
                double limit = info.CommitLimit.ToUInt64();
                if (limit > 0)
                    readings.Add(new Reading(MetricNames.MemCommitPct, "", Stats.ClampPercent(100.0 * info.CommitTotal.ToUInt64() / limit)));
            }

            if (_options.System)
            {
                readings.Add(new Reading(MetricNames.Processes, "", info.ProcessCount));
                readings.Add(new Reading(MetricNames.Threads, "", info.ThreadCount));
                readings.Add(new Reading(MetricNames.Handles, "", info.HandleCount));
                readings.Add(new Reading(MetricNames.UptimeHours, "", Math.Round(NativeMethods.GetTickCount64() / 3600000.0, 2)));
                if (_cpuQueue != null)
                    readings.Add(new Reading(MetricNames.CpuQueue, "", _cpuQueue.NextValue()));
            }
        }

        // ---------------------------------------------------------------- Disk space

        private void ReadDrives(List<Reading> readings)
        {
            const double gb = 1024.0 * 1024 * 1024;
            var wanted = new HashSet<string>(_options.Drives.Select(d => d.TrimEnd('\\').ToUpperInvariant()));

            foreach (var drive in DriveInfo.GetDrives())
            {
                if (drive.DriveType != DriveType.Fixed || !drive.IsReady) continue;

                var name = drive.Name.TrimEnd('\\').ToUpperInvariant();
                if (wanted.Count > 0 && !wanted.Contains(name)) continue;

                double total = drive.TotalSize, free = drive.TotalFreeSpace;
                if (total <= 0) continue;

                readings.Add(new Reading(MetricNames.DiskFreeGb, name, Math.Round(free / gb, 2)));
                readings.Add(new Reading(MetricNames.DiskUsedGb, name, Math.Round((total - free) / gb, 2)));
                readings.Add(new Reading(MetricNames.DiskFreePct, name, Stats.ClampPercent(100 * free / total)));
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
            if (_diskRead != null) readings.Add(new Reading(MetricNames.DiskReadKBps, "", Math.Round(_diskRead.NextValue() / 1024.0, 1)));
            if (_diskWrite != null) readings.Add(new Reading(MetricNames.DiskWriteKBps, "", Math.Round(_diskWrite.NextValue() / 1024.0, 1)));
            if (_diskIdle != null) readings.Add(new Reading(MetricNames.DiskBusyPct, "", Stats.ClampPercent(100 - _diskIdle.NextValue())));
            if (_diskQueue != null) readings.Add(new Reading(MetricNames.DiskQueue, "", _diskQueue.NextValue()));
        }

        private static PerformanceCounter TryCreateCounter(string category, string counter, string instance)
        {
            try
            {
                var pc = new PerformanceCounter(category, counter, instance, true);
                pc.NextValue();
                return pc;
            }
            catch (Exception ex)
            {
                Log.Warn("Performance counter " + category + "\\" + counter + " is unavailable: " + ex.Message);
                return null;
            }
        }

        // ---------------------------------------------------------------- Network

        private void ReadNetwork(List<Reading> readings)
        {
            double seconds = _netClock.IsRunning ? _netClock.Elapsed.TotalSeconds : 0;
            _netClock.Restart();

            double totalRx = 0, totalTx = 0;
            bool any = false;
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up) continue;
                if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback || nic.NetworkInterfaceType == NetworkInterfaceType.Tunnel) continue;
                if (_options.ExcludeAdapters.Any(x => Contains(nic.Name, x) || Contains(nic.Description, x))) continue;

                var stats = nic.GetIPStatistics();
                long rx = stats.BytesReceived, tx = stats.BytesSent;
                seen.Add(nic.Name);

                long[] prev;
                if (seconds > 0 && _prevNet.TryGetValue(nic.Name, out prev) && rx >= prev[0] && tx >= prev[1])
                {
                    double rxKBps = (rx - prev[0]) / 1024.0 / seconds;
                    double txKBps = (tx - prev[1]) / 1024.0 / seconds;
                    readings.Add(new Reading(MetricNames.NetRecvKBps, nic.Name, Math.Round(rxKBps, 2)));
                    readings.Add(new Reading(MetricNames.NetSentKBps, nic.Name, Math.Round(txKBps, 2)));
                    totalRx += rxKBps;
                    totalTx += txKBps;
                    any = true;
                }

                _prevNet[nic.Name] = new[] { rx, tx };
            }

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
            using (var me = Process.GetCurrentProcess())
            {
                var cpu = me.TotalProcessorTime;
                if (_selfClock.IsRunning)
                {
                    double wall = _selfClock.Elapsed.TotalMilliseconds;
                    double used = (cpu - _prevSelfCpu).TotalMilliseconds;
                    if (wall > 0)
                        readings.Add(new Reading(MetricNames.SelfCpuPct, "", Math.Round(100 * used / wall / Environment.ProcessorCount, 3)));
                }

                readings.Add(new Reading(MetricNames.SelfMemMb, "", Math.Round(me.PrivateMemorySize64 / 1024.0 / 1024.0, 1)));
                _prevSelfCpu = cpu;
                _selfClock.Restart();
            }
        }

        private static bool Contains(string text, string part)
        {
            // string.Contains(string, StringComparison) only exists in .NET Core 2.1+.
            return !string.IsNullOrEmpty(part) && text != null && text.IndexOf(part, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public void Dispose()
        {
            if (_diskRead != null) _diskRead.Dispose();
            if (_diskWrite != null) _diskWrite.Dispose();
            if (_diskIdle != null) _diskIdle.Dispose();
            if (_diskQueue != null) _diskQueue.Dispose();
            if (_cpuQueue != null) _cpuQueue.Dispose();
        }
    }
}
