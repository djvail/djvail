using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using VailITDashboard.Alerts;

namespace VailITDashboard.Pages;

/// <summary>One machine's thresholds: what applies to each reading and where it comes from, plus its own overrides.</summary>
public sealed class MachineThresholdsModel(MachineStore machines, ThresholdStore thresholds) : PageModel
{
    public Machine Machine { get; private set; } = null!;
    public sealed record Effective(LatestReading Reading, Threshold? Threshold, AlertLevel Level);
    public List<Effective> Rows { get; private set; } = [];
    public ThresholdEditorModel Editor { get; private set; } = new();

    public IActionResult OnGet(string id)
    {
        var m = machines.Get(id);
        if (m is null) return NotFound();
        Machine = m;

        var all = thresholds.GetAll();
        var latest = machines.GetLatest(id);
        Rows = latest
            .Where(r => r.Metric != "cpu.core_pct" || ThresholdResolver.Resolve(all, m, r.Key) is not null)
            .Select(r =>
            {
                var t = ThresholdResolver.Resolve(all, m, r.Key, includeDisabled: true);
                return new Effective(r, t, t is { Enabled: true } ? AlertRules.LevelFor(r.Value, t.Direction, t.Warning, t.Critical) : AlertLevel.Ok);
            })
            .OrderBy(e => e.Threshold is null).ThenBy(e => MetricCatalog.SortOrder(e.Reading.Metric)).ThenBy(e => e.Reading.Instance)
            .ToList();

        Editor = new ThresholdEditorModel
        {
            Scope = ThresholdScope.Machine,
            ScopeValue = m.MachineId,
            Thresholds = thresholds.ForScope(ThresholdScope.Machine, m.MachineId),
            KnownInstances = latest.Select(r => r.Instance).Where(i => i.Length > 0).Prepend("*").Distinct().ToList(),
            ReturnUrl = $"/machines/{m.MachineId}/thresholds",
            EmptyText = "None: this machine uses its group's and the global thresholds.",
        };
        return Page();
    }
}
