using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;

namespace VailITDashboard.Pages;

/// <summary>API keys for monitors: create one per server, see when each was last used, revoke.</summary>
public sealed class MonitorsModel(ApiKeyStore keys, MachineStore machines, IOptions<DashboardOptions> options) : PageModel
{
    public List<ApiKeyInfo> Keys { get; private set; } = [];
    public Dictionary<string, Machine> Machines { get; private set; } = [];
    public string PublicUrl => options.Value.PublicUrl.TrimEnd('/');

    /// <summary>Set straight after creating a key; shown once.</summary>
    [TempData] public string? NewKey { get; set; }
    [TempData] public string? NewKeyLabel { get; set; }

    public void OnGet()
    {
        Keys = keys.List();
        Machines = machines.GetAll().ToDictionary(m => m.MachineId, StringComparer.OrdinalIgnoreCase);
    }

    public string NameOf(string? id) => id is null ? "not used yet" : Machines.TryGetValue(id, out var m) ? m.Name : "(removed machine)";

    public IActionResult OnPostCreate(string label)
    {
        label = string.IsNullOrWhiteSpace(label) ? "monitor" : label.Trim();
        NewKey = keys.Create(label);
        NewKeyLabel = label;
        return RedirectToPage();
    }

    public IActionResult OnPostRevoke(long id)
    {
        keys.Revoke(id);
        TempData["Message"] = "Key revoked. A monitor using it gets 401 from its next report.";
        return RedirectToPage();
    }
}
