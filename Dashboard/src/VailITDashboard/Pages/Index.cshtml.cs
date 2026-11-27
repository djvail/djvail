using Microsoft.AspNetCore.Mvc.RazorPages;

namespace VailITDashboard.Pages;

/// <summary>The overview: every machine, grouped by server type, problems first.</summary>
public sealed class IndexModel(StatusService status) : PageModel
{
    public List<MachineStatus> All { get; private set; } = [];
    public List<IGrouping<string, MachineStatus>> Groups { get; private set; } = [];

    /// <summary>?show=problems hides healthy machines; ?group=SQL shows one group.</summary>
    public string? Show { get; private set; }
    public string? Group { get; private set; }

    public int Count(MachineHealth h) => All.Count(s => s.Health == h);

    public void OnGet(string? show, string? group)
    {
        Show = show;
        Group = group;
        All = status.GetAll();

        IEnumerable<MachineStatus> visible = All;
        if (show == "problems") visible = visible.Where(s => s.Health != MachineHealth.Ok);
        else if (Enum.TryParse<MachineHealth>(show, true, out var only)) visible = visible.Where(s => s.Health == only);
        if (!string.IsNullOrEmpty(group)) visible = visible.Where(s => string.Equals(s.Machine.GroupName, group, StringComparison.OrdinalIgnoreCase));

        // Groups with problems first, then by name; machines inside a group the same way.
        Groups = visible
            .OrderBy(s => StatusService.Severity(s.Health)).ThenBy(s => s.Machine.Name, StringComparer.OrdinalIgnoreCase)
            .GroupBy(s => s.Machine.GroupName, StringComparer.OrdinalIgnoreCase)
            .OrderBy(g => g.Min(s => StatusService.Severity(s.Health))).ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
