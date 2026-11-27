using VailITDashboard.Alerts;

namespace VailITDashboard.Tools;

/// <summary>
/// Fills an empty database with made-up servers and a day of readings, so you can try the dashboard
/// without any monitors. Turn it on with "SeedDemoData": true (or --Dashboard:SeedDemoData=true).
/// It goes through the same code path as a real report, so it also exercises the alert rules.
/// </summary>
public sealed class DemoData(Database db, MachineStore machines, AlertEngine engine, ApiKeyStore keys, ILogger<DemoData> log)
{
    private sealed record DemoServer(string Name, string Type, double Cpu, double Mem, double CFree, double? DFree, bool Offline = false);

    private static readonly DemoServer[] Servers =
    [
        new("DC01", "Domain Controllers", 8, 45, 62, null),
        new("DC02", "Domain Controllers", 7, 41, 58, null),
        new("SQL01", "SQL", 35, 82, 40, 22),
        new("SQL02", "SQL", 48, 91, 4, 35),          // C: nearly full -> critical; memory high -> warning
        new("WEB01", "Web", 22, 55, 51, null),
        new("WEB02", "Web", 96, 63, 47, null),       // CPU busy -> warning or critical
        new("FILE01", "File", 5, 38, 71, 9, Offline: true),
        new("APP01", "Application", 18, 60, 33, 64),
    ];

    public void SeedIfEmpty()
    {
        using (var c = db.Open())
        {
            if (Database.Scalar<long>(c, "SELECT count(*) FROM machines") > 0) return;
        }

        log.LogWarning("SeedDemoData is on and the database is empty: adding {Count} demo machines", Servers.Length);
        var rng = new Random(42);
        var end = DateTime.UtcNow.AddSeconds(-DateTime.UtcNow.Second);
        var start = end.AddHours(-24);

        foreach (var s in Servers)
        {
            var id = new MonitorIdentity
            {
                MachineId = Guid.NewGuid(), MachineName = s.Name, ServerType = s.Type, Tags = ["demo"],
                AgentVersion = "1.1.0", Edition = s.Name.StartsWith("DC") ? "Net48" : "Net10",
                Os = "Windows Server 2022 Standard 10.0.20348", LogicalProcessors = 8, TotalMemoryMb = 32768,
                Drives = s.DFree is null ? [new DriveDto { Name = "C:", SizeGb = 237.9 }]
                                         : [new DriveDto { Name = "C:", SizeGb = 237.9 }, new DriveDto { Name = "D:", SizeGb = 1862 }],
                LastBootUtc = end.AddDays(-6.3),
            };

            var samples = new List<SampleDto>();
            var stopAt = s.Offline ? end.AddMinutes(-25) : end;
            for (var t = start; t <= stopAt; t = t.AddMinutes(1))
            {
                double hour = t.ToLocalTime().TimeOfDay.TotalHours;
                double business = hour is >= 8 and <= 18 ? 1.0 : 0.4;   // busier in office hours
                double Noise(double scale) => (rng.NextDouble() - 0.5) * scale;

                var readings = new List<ReadingDto>
                {
                    new() { Metric = "cpu.total_pct", Value = Clamp(s.Cpu * (0.85 + 0.15 * business) + Noise(6)) },
                    new() { Metric = "mem.used_pct", Value = Clamp(s.Mem + Noise(3)) },
                    new() { Metric = "mem.commit_pct", Value = Clamp(s.Mem * 0.9 + Noise(3)) },
                    new() { Metric = "disk.free_pct", Instance = "C:", Value = Clamp(s.CFree + Noise(0.4)) },
                    new() { Metric = "disk.free_gb", Instance = "C:", Value = Clamp(s.CFree + Noise(0.4)) * 2.379 },
                    new() { Metric = "diskio.busy_pct", Instance = "_Total", Value = Clamp(s.Cpu * 0.5 * business + Noise(10)) },
                    new() { Metric = "net.recv_kbps", Instance = "_Total", Value = Math.Max(0, 300 * business + Noise(200)) },
                    new() { Metric = "net.sent_kbps", Instance = "_Total", Value = Math.Max(0, 120 * business + Noise(80)) },
                    new() { Metric = "sys.processes", Value = Math.Round(140 + Noise(10)) },
                    new() { Metric = "sys.uptime_hours", Value = (t - id.LastBootUtc).TotalHours },
                    new() { Metric = "self.private_mb", Value = 28 + Noise(2) },
                };
                if (s.DFree is double d)
                {
                    readings.Add(new() { Metric = "disk.free_pct", Instance = "D:", Value = Clamp(d + Noise(0.3)) });
                    readings.Add(new() { Metric = "disk.free_gb", Instance = "D:", Value = Clamp(d + Noise(0.3)) * 18.62 });
                }
                samples.Add(new SampleDto { TimestampUtc = t, Readings = readings });
            }

            machines.SaveReport(id, samples);
            keys.Create($"demo key for {s.Name}", id.MachineId.ToString("D"));

            // Run the newest few samples through the alert rules, as if they had just arrived.
            var machine = machines.Get(id.MachineId.ToString("D"))!;
            engine.EvaluateReport(machine, samples.TakeLast(5));

            if (s.Offline)
            {
                using var c = db.Open();
                using var cmd = c.Command("UPDATE machines SET last_seen = $t WHERE machine_id = $id")
                    .With("$id", machine.MachineId).With("$t", UnixTime.From(stopAt));
                cmd.ExecuteNonQuery();
            }
        }
    }

    private static double Clamp(double v) => Math.Round(Math.Clamp(v, 0, 100), 2);
}
