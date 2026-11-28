using System.Reflection;
using Microsoft.Extensions.Options;
using Microsoft.Win32;
using VailIT.Reporting.Contracts;

namespace VailITMonitor.Dashboard;

/// <summary>Describes this server for the "monitor" block of every report.</summary>
public sealed class MachineInfo(IOptions<MonitorOptions> options)
{
    private readonly MonitorOptions _options = options.Value;
    private readonly Lazy<Guid> _machineId = new(() => LoadOrCreateMachineId(options.Value));
    private readonly Lazy<string> _os = new(ReadOsName);

    public Guid MachineId => _machineId.Value;

    public static string AgentVersion =>
        typeof(MachineInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0]
        ?? "0.0.0";

    /// <summary>Built fresh for each report (cheap), so drive changes show up without a restart.</summary>
    public MonitorIdentity Build()
    {
        long bootTicksAgo = Environment.TickCount64;
        var boot = DateTime.UtcNow.AddMilliseconds(-bootTicksAgo);

        return new MonitorIdentity
        {
            MachineId = MachineId,
            MachineName = Environment.MachineName,
            ServerType = _options.Reporting.ServerType ?? "",
            Tags = [.. _options.Reporting.Tags ?? []],
            AgentVersion = AgentVersion,
            Edition = "Net10",
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
        var wanted = _options.Metrics.Drives.Select(d => d.TrimEnd('\\').ToUpperInvariant()).ToHashSet();
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
        if (!OperatingSystem.IsWindows()) return GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / (1024 * 1024);
        var info = new NativeMethods.PerformanceInformation { Size = System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.PerformanceInformation>() };
        if (!NativeMethods.GetPerformanceInfo(ref info, info.Size)) return 0;
        return (long)((double)info.PhysicalTotal * info.PageSize / (1024 * 1024));
    }

    private static string ReadOsName()
    {
        try
        {
            if (OperatingSystem.IsWindows())
            {
                using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
                var product = key?.GetValue("ProductName") as string;
                var build = key?.GetValue("CurrentBuild") as string;
                if (!string.IsNullOrEmpty(product))
                    return $"{product} {Environment.OSVersion.Version.Major}.{Environment.OSVersion.Version.Minor}.{build}";
            }
        }
        catch (Exception)
        {
            // Fall through to the generic description.
        }
        return System.Runtime.InteropServices.RuntimeInformation.OSDescription;
    }

    /// <summary>
    /// The dashboard's key for this server. Taken from Reporting:MachineId when set; otherwise
    /// generated once and kept in the data folder, so it survives upgrades and renames.
    /// </summary>
    private static Guid LoadOrCreateMachineId(MonitorOptions options)
    {
        if (Guid.TryParse(options.Reporting.MachineId, out var configured) && configured != Guid.Empty) return configured;

        var file = Path.Combine(options.ResolvedDataFolder, "machine-id.txt");
        if (File.Exists(file) && Guid.TryParse(File.ReadAllText(file).Trim(), out var stored) && stored != Guid.Empty) return stored;

        var id = Guid.NewGuid();
        Directory.CreateDirectory(options.ResolvedDataFolder);
        File.WriteAllText(file, id.ToString("D"));
        return id;
    }
}
