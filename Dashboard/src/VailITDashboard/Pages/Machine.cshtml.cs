using System.Text.Json;
using Microsoft.AspNetCore.Html;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using VailITDashboard.Alerts;

namespace VailITDashboard.Pages;

/// <summary>Drill-down for one machine: charts, current readings against thresholds, alerts, settings.</summary>
public sealed class MachineModel(MachineStore machines, AlertStore alerts, ThresholdStore thresholds, StatusService status) : PageModel
{
    /// <summary>Time ranges offered above the charts: label -> hours.</summary>
    public static readonly (string Label, int Hours)[] Ranges = [("6 h", 6), ("24 h", 24), ("7 days", 168), ("14 days", 336)];

    public Machine Machine { get; private set; } = null!;
    public MachineStatus Status { get; private set; } = null!;
    public List<Alert> OpenAlerts { get; private set; } = [];
    public List<Alert> History { get; private set; } = [];
    public List<string> Groups { get; private set; } = [];
    public List<DriveDto> Drives { get; private set; } = [];
    public int Hours { get; private set; }

    /// <summary>Each row of the "current readings" table.</summary>
    public sealed record Row(LatestReading Reading, string Category, Threshold? Threshold, AlertLevel Level);
    public List<Row> Rows { get; private set; } = [];

    public sealed record Chart(string Title, IHtmlContent Svg, IReadOnlyList<string> Legend);
    public List<Chart> Charts { get; private set; } = [];

    public IActionResult OnGet(string id, int hours = 24)
    {
        var m = machines.Get(id);
        if (m is null) return NotFound();
        Load(m, Math.Clamp(hours, 1, 24 * 31));
        return Page();
    }

    private void Load(Machine m, int hours)
    {
        Machine = m;
        Hours = hours;
        Status = status.Get(m);
        OpenAlerts = alerts.GetOpen(m.MachineId);
        History = alerts.GetRecent(50, m.MachineId);
        Groups = machines.GetGroups();
        try { Drives = JsonSerializer.Deserialize<List<DriveDto>>(m.DrivesJson, new JsonSerializerOptions(JsonSerializerDefaults.Web)) ?? []; }
        catch (JsonException) { Drives = []; }

        var all = thresholds.GetAll();
        Rows = machines.GetLatest(m.MachineId)
            .Select(r =>
            {
                var t = ThresholdResolver.Resolve(all, m, r.Key, includeDisabled: true);
                var level = t is { Enabled: true } ? AlertRules.LevelFor(r.Value, t.Direction, t.Warning, t.Critical) : AlertLevel.Ok;
                return new Row(r, MetricCatalog.Get(r.Metric).Category, t, level);
            })
            .OrderBy(r => MetricCatalog.SortOrder(r.Reading.Metric))
            .ThenBy(r => r.Reading.Instance == MetricCatalog.Total ? "" : r.Reading.Instance, StringComparer.OrdinalIgnoreCase)
            .ToList();

        BuildCharts(m, all, hours);
    }

    private void BuildCharts(Machine m, IReadOnlyList<Threshold> all, int hours)
    {
        long to = UnixTime.Now, from = to - hours * 3600L;
        // About 240 points per line whatever the range.
        int bucket = Math.Max(60, hours * 3600 / 240);
        var latest = Rows.Select(r => r.Reading).ToList();

        // Chart = one or more series that share a unit. Change this list to change which charts appear.
        Add("CPU", "%", 100, [new SeriesKey("cpu.total_pct", "")]);
        Add("Memory", "%", 100, [new SeriesKey("mem.used_pct", ""), new SeriesKey("mem.commit_pct", "")]);
        Add("Disk free", "%", 100, latest.Where(r => r.Metric == "disk.free_pct").Select(r => r.Key).ToList());
        Add("Disk busy", "%", 100, latest.Where(r => r.Metric == "diskio.busy_pct").Select(r => r.Key).ToList());
        Add("Network", "KB/s", null, latest.Where(r => r.Metric is "net.recv_kbps" or "net.sent_kbps" && r.Instance == MetricCatalog.Total).Select(r => r.Key).ToList());

        void Add(string title, string unit, double? fixedMax, List<SeriesKey> keys)
        {
            if (keys.Count == 0) return;
            var series = keys.Select(k => new SvgChart.Series(MetricCatalog.Label(k), machines.GetSeries(m.MachineId, k.Metric, k.Instance, from, to, bucket))).ToList();
            if (series.All(s => s.Points.Count == 0) && !latest.Any(r => keys.Contains(r.Key))) return;

            // Draw the threshold lines of the first series (e.g. CPU's 85/95 %).
            var t = ThresholdResolver.Resolve(all, m, keys[0]);
            Charts.Add(new Chart(title, SvgChart.Render(series, from, to, unit, t?.Warning, t?.Critical, fixedMax), series.Select(s => s.Name).ToList()));
        }
    }

    // ---------------------------------------------------------------- form posts

    public IActionResult OnPostSettings(string id, string? displayName, string group, string? notes)
    {
        if (machines.Get(id) is null) return NotFound();
        machines.Update(id, displayName, group, notes ?? "");
        TempData["Message"] = "Saved.";
        return RedirectToPage(new { id });
    }

    public IActionResult OnPostMute(string id, int hours)
    {
        machines.SetMute(id, hours <= 0 ? null : UnixTime.Now + hours * 3600L);
        TempData["Message"] = hours <= 0 ? "Emails turned back on." : $"Emails muted for {hours} hours. Alerts are still recorded.";
        return RedirectToPage(new { id });
    }

    public IActionResult OnPostAcknowledge(string id, long alertId)
    {
        alerts.Acknowledge(alertId, User.Identity?.Name ?? "dashboard");
        return RedirectToPage(new { id });
    }

    public IActionResult OnPostDelete(string id)
    {
        var m = machines.Get(id);
        if (m is null) return NotFound();
        machines.Delete(id);
        TempData["Message"] = $"{m.Name} and its history were removed, and its API keys revoked.";
        return RedirectToPage("/Index");
    }
}
