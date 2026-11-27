using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace VailITDashboard.Pages;

/// <summary>
/// Global and per-group thresholds, plus the Save/Delete handlers every threshold form posts to
/// (including the ones on a machine's own thresholds page).
/// </summary>
public sealed class ThresholdsModel(ThresholdStore thresholds, MachineStore machines) : PageModel
{
    public ThresholdScope Scope { get; private set; }
    public string ScopeValue { get; private set; } = "";
    public List<(string Group, int Machines, int Overrides)> Groups { get; private set; } = [];
    public ThresholdEditorModel Editor { get; private set; } = new();
    public List<(Machine Machine, int Overrides)> MachineOverrides { get; private set; } = [];

    public void OnGet(string? scope, string? value)
    {
        Scope = scope?.ToLowerInvariant() == "group" && !string.IsNullOrWhiteSpace(value) ? ThresholdScope.Group : ThresholdScope.Global;
        ScopeValue = Scope == ThresholdScope.Group ? value!.Trim() : "";

        var all = thresholds.GetAll();
        var machineList = machines.GetAll();
        Groups = machineList.Select(m => m.GroupName)
            .Concat(all.Where(t => t.Scope == ThresholdScope.Group).Select(t => t.ScopeValue))
            .Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase)
            .Select(g => (g, machineList.Count(m => m.GroupName.Equals(g, StringComparison.OrdinalIgnoreCase)),
                          all.Count(t => t.Scope == ThresholdScope.Group && t.ScopeValue.Equals(g, StringComparison.OrdinalIgnoreCase))))
            .ToList();
        MachineOverrides = machineList
            .Select(m => (m, all.Count(t => t.Scope == ThresholdScope.Machine && t.ScopeValue.Equals(m.MachineId, StringComparison.OrdinalIgnoreCase))))
            .Where(x => x.Item2 > 0).ToList();

        Editor = new ThresholdEditorModel
        {
            Scope = Scope,
            ScopeValue = ScopeValue,
            Thresholds = thresholds.ForScope(Scope, ScopeValue),
            KnownInstances = ["*", "C:", "D:", "_Total"],
            ReturnUrl = Scope == ThresholdScope.Global ? "/thresholds" : $"/thresholds?scope=group&value={Uri.EscapeDataString(ScopeValue)}",
            EmptyText = Scope == ThresholdScope.Global ? "No global thresholds, so nothing is checked." : "None: this group uses the global thresholds.",
        };
    }

    public IActionResult OnPostSave(ThresholdScope scope, string? scopeValue, string metric, string? instance,
        ThresholdDirection direction, double? warning, double? critical, bool enabled, string? returnUrl)
    {
        if (string.IsNullOrWhiteSpace(metric)) return BadRequest();
        if (scope != ThresholdScope.Global && string.IsNullOrWhiteSpace(scopeValue)) return BadRequest();
        if (enabled && warning is null && critical is null)
        {
            TempData["Message"] = "Give a warning or a critical value (or untick \"on\" to switch the check off here).";
            return LocalRedirect(Safe(returnUrl));
        }

        thresholds.Save(new Threshold
        {
            Scope = scope, ScopeValue = scopeValue?.Trim() ?? "", Metric = metric.Trim(),
            Instance = string.IsNullOrWhiteSpace(instance) ? "*" : instance.Trim(),
            Direction = direction, Warning = warning, Critical = critical, Enabled = enabled,
        });
        TempData["Message"] = $"Saved the {MetricCatalog.Get(metric).Label} threshold. It applies from the next report.";
        return LocalRedirect(Safe(returnUrl));
    }

    public IActionResult OnPostDelete(long id, string? returnUrl)
    {
        thresholds.Delete(id);
        TempData["Message"] = "Threshold removed.";
        return LocalRedirect(Safe(returnUrl));
    }

    private string Safe(string? url) => !string.IsNullOrEmpty(url) && Url.IsLocalUrl(url) ? url : "/thresholds";
}
