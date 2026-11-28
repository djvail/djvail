using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using VailIT.Reporting.Contracts;
using VailITMonitor.Collection;
using VailITMonitor.Configuration;

namespace VailITMonitor.Dashboard
{
    /// <summary>Describes this server for the "monitor" block of every report.</summary>
    public sealed class MachineInfo
    {
        private readonly MonitorSettings _settings;
        private readonly Lazy<Guid> _machineId;
        private readonly Lazy<string> _os = new Lazy<string>(ReadOsName);

        public MachineInfo(MonitorSettings settings)
        {
            _settings = settings;
            _machineId = new Lazy<Guid>(() => LoadOrCreateMachineId(settings));
        }

        public Guid MachineId
        {
            get { return _machineId.Value; }
        }

        public static string AgentVersion
        {
            get
            {
                var attr = typeof(MachineInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
                return attr == null ? "0.0.0" : attr.InformationalVersion.Split('+')[0];
            }
        }

        /// <summary>Built fresh for each report (cheap), so drive changes show up without a restart.</summary>
        public MonitorIdentity Build()
        {
            var boot = DateTime.UtcNow.AddMilliseconds(-(double)NativeMethods.GetTickCount64());
            return new MonitorIdentity
            {
                MachineId = MachineId,
                MachineName = Environment.MachineName,
                ServerType = _settings.Reporting.ServerType ?? "",
                Tags = (_settings.Reporting.Tags ?? new string[0]).ToList(),
                AgentVersion = AgentVersion,
                Edition = "Net48",
                Os = _os.Value,
                LogicalProcessors = Environment.ProcessorCount,
                TotalMemoryMb = ReadTotalMemoryMb(),
                Drives = ReadDrives(),
                // Rounded to the minute so the value doesn't wobble between reports.
                LastBootUtc = new DateTime(boot.Ticks - boot.Ticks % TimeSpan.TicksPerMinute, DateTimeKind.Utc),
            };
        }

        private List<DriveDto> ReadDrives()
        {
            const double gb = 1024.0 * 1024 * 1024;
            var wanted = new HashSet<string>(_settings.Metrics.Drives.Select(d => d.TrimEnd('\\').ToUpperInvariant()));
            var drives = new List<DriveDto>();
            foreach (var drive in DriveInfo.GetDrives())
            {
                try
                {
                    if (drive.DriveType != DriveType.Fixed || !drive.IsReady) continue;
                    var name = drive.Name.TrimEnd('\\').ToUpperInvariant();
                    if (wanted.Count > 0 && !wanted.Contains(name)) continue;
                    drives.Add(new DriveDto { Name = name, SizeGb = Math.Round(drive.TotalSize / gb, 1) });
                }
                catch (IOException)
                {
                    // A drive that disappears mid-call is simply left out.
                }
            }
            return drives;
        }

        private static long ReadTotalMemoryMb()
        {
            var info = new NativeMethods.PerformanceInformation { Size = Marshal.SizeOf(typeof(NativeMethods.PerformanceInformation)) };
            if (!NativeMethods.GetPerformanceInfo(ref info, info.Size)) return 0;
            return (long)((double)info.PhysicalTotal.ToUInt64() * info.PageSize.ToUInt64() / (1024 * 1024));
        }

        private static string ReadOsName()
        {
            try
            {
                using (var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion"))
                {
                    var product = key == null ? null : key.GetValue("ProductName") as string;
                    var build = key == null ? null : key.GetValue("CurrentBuild") as string;
                    if (!string.IsNullOrEmpty(product))
                    {
                        var v = Environment.OSVersion.Version;
                        return product + " " + v.Major + "." + v.Minor + "." + build;
                    }
                }
            }
            catch (Exception)
            {
                // Fall through to the generic description.
            }
            return Environment.OSVersion.VersionString;
        }

        /// <summary>
        /// The dashboard's key for this server. Taken from Reporting.MachineId when set; otherwise
        /// generated once and kept in the data folder, so it survives upgrades and renames.
        /// </summary>
        private static Guid LoadOrCreateMachineId(MonitorSettings settings)
        {
            Guid id;
            if (Guid.TryParse(settings.Reporting.MachineId, out id) && id != Guid.Empty) return id;

            var file = Path.Combine(settings.ResolvedDataFolder, "machine-id.txt");
            if (File.Exists(file) && Guid.TryParse(File.ReadAllText(file).Trim(), out id) && id != Guid.Empty) return id;

            id = Guid.NewGuid();
            Directory.CreateDirectory(settings.ResolvedDataFolder);
            File.WriteAllText(file, id.ToString("D"));
            return id;
        }
    }
}
