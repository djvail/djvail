using Microsoft.Extensions.Options;
using VailITDashboard.Alerts;

namespace VailITDashboard;

/// <summary>One machine as the overview page shows it.</summary>
public sealed record MachineStatus(
    Machine Machine, MachineHealth Health, bool Muted, int OpenAlerts, string? TopProblem,
    double? Cpu, double? Memory, (string Drive, double FreePct)? FullestDrive, long? LastSampleTs,
    AlertLevel CpuLevel, AlertLevel MemoryLevel, AlertLevel DiskLevel);

/// <summary>Works out how each machine is doing, for the pages and the JSON API.</summary>
public sealed class StatusService(MachineStore machines, AlertStore alerts, ThresholdStore thresholds, IOptions<DashboardOptions> options)
{
    public List<MachineStatus> GetAll()
    {
        long now = UnixTime.Now;
        var latest = machines.GetLatestForAll();
        var open = alerts.GetOpen().GroupBy(a => a.MachineId).ToDictionary(g => g.Key, g => g.ToList());
        return machines.GetAll().Select(m => Build(m, latest.GetValueOrDefault(m.MachineId) ?? [], open.GetValueOrDefault(m.MachineId) ?? [], now)).ToList();
    }

    public MachineStatus Get(Machine m) => Build(m, machines.GetLatest(m.MachineId), alerts.GetOpen(m.MachineId), UnixTime.Now);

    public MachineHealth HealthOf(Machine m, IEnumerable<Alert> openAlerts, long now)
    {
        var silent = TimeSpan.FromSeconds(now - m.LastSeen);
        if (silent >= options.Value.OfflineAfter) return MachineHealth.Offline;
        if (silent >= options.Value.StaleAfter) return MachineHealth.Stale;
        var worst = openAlerts.Where(a => a.Metric != AlertEngine.OfflineMetric).Select(a => a.Level).DefaultIfEmpty(AlertLevel.Ok).Max();
        return worst switch
        {
            AlertLevel.Critical => MachineHealth.Critical,
            AlertLevel.Warning => MachineHealth.Warning,
            _ => MachineHealth.Ok,
        };
    }

    private MachineStatus Build(Machine m, List<LatestReading> latest, List<Alert> open, long now)
    {
        var all = thresholds.GetAll();
        LatestReading? Find(string metric) => latest.FirstOrDefault(r => r.Metric == metric && (r.Instance == "" || r.Instance == MetricCatalog.Total));
        AlertLevel LevelOf(LatestReading? r) => r is null || ThresholdResolver.Resolve(all, m, r.Key) is not { } t
            ? AlertLevel.Ok : AlertRules.LevelFor(r.Value, t.Direction, t.Warning, t.Critical);

        var cpu = Find("cpu.total_pct");
        var mem = Find("mem.used_pct");
        var fullest = latest.Where(r => r.Metric == "disk.free_pct").OrderBy(r => r.Value).FirstOrDefault();
        return new MachineStatus(
            m, HealthOf(m, open, now), m.IsMuted(now), open.Count,
            open.OrderByDescending(a => a.Level).FirstOrDefault()?.Message,
            cpu?.Value, mem?.Value,
            fullest is null ? null : (fullest.Instance, fullest.Value),
            m.LastSampleTs,
            LevelOf(cpu), LevelOf(mem), LevelOf(fullest));
    }

    /// <summary>Order groups and machines so problems float to the top.</summary>
    public static int Severity(MachineHealth h) => h switch
    {
        MachineHealth.Offline => 0,
        MachineHealth.Critical => 1,
        MachineHealth.Warning => 2,
        MachineHealth.Stale => 3,
        _ => 4,
    };

    public static string CssClass(MachineHealth h) => h.ToString().ToLowerInvariant();

    public static string CssClass(AlertLevel l) => l == AlertLevel.Ok ? "" : l.ToString().ToLowerInvariant();
}
