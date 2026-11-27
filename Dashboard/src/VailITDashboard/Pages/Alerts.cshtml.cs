using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;
using VailITDashboard.Alerts;

namespace VailITDashboard.Pages;

/// <summary>Every open problem across all machines, recent history, and email settings at a glance.</summary>
public sealed class AlertsModel(AlertStore alerts, MachineStore machines, AlertNotifier notifier, IOptions<DashboardOptions> options) : PageModel
{
    public List<Alert> Open { get; private set; } = [];
    public List<Alert> Recent { get; private set; } = [];
    public Dictionary<string, Machine> Machines { get; private set; } = [];
    public AlertOptions Settings => options.Value.Alerts;

    public void OnGet()
    {
        Open = alerts.GetOpen();
        Recent = alerts.GetRecent(200).Where(a => !a.IsOpen).ToList();
        Machines = machines.GetAll().ToDictionary(m => m.MachineId, StringComparer.OrdinalIgnoreCase);
    }

    public string NameOf(string machineId) => Machines.TryGetValue(machineId, out var m) ? m.Name : machineId;

    public IActionResult OnPostAcknowledge(long alertId)
    {
        alerts.Acknowledge(alertId, User.Identity?.Name ?? "dashboard");
        return RedirectToPage();
    }

    public IActionResult OnPostTestEmail()
    {
        var a = options.Value.Alerts;
        if (a.To.Length == 0) TempData["Message"] = "Add at least one address to Dashboard:Alerts:To in appsettings.json first.";
        else if (string.IsNullOrWhiteSpace(a.Smtp.Host)) TempData["Message"] = "No SMTP host is set (Dashboard:Alerts:Smtp:Host), so the email will only be written to the log.";
        else TempData["Message"] = $"Test email queued to {string.Join(", ", a.To)}. Check the log if it doesn't arrive.";
        notifier.EnqueueTest();
        return RedirectToPage();
    }
}
