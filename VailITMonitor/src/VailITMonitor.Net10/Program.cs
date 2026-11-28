// VailITMonitor - .NET 10 edition - entry point.
//
// This file uses "top-level statements" (C# 9): there is no 'class Program' / 'static void Main',
// the compiler generates them. 'args' is available automatically.
//
//   VailITMonitor.exe                     run (as a Windows service, or in the console if started by hand)
//   VailITMonitor.exe console             same, explicitly in the console (Ctrl+C to stop)
//   VailITMonitor.exe sample              take one reading and print it
//   VailITMonitor.exe maintenance         run summaries + reports + cleanup now
//   VailITMonitor.exe cleanup             run every cleanup now, ignoring CleanupEveryDays
//   VailITMonitor.exe report daily [date] create a report now (daily|weekly|monthly|quarterly|yearly)
//   VailITMonitor.exe demo [folder]       create a demo database full of fake data, and sample reports
//   VailITMonitor.exe connect <url> <apiKey> [serverType]
//                                         turn on reporting to VailITDashboard (saved in reporting.json)
//   VailITMonitor.exe report-test         take one reading, send it to the dashboard, print the reply
//
// Installing/removing the service: see scripts\Install-VailITMonitor.ps1 and docs\02-Build-Install.md.

using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.EventLog;
using Microsoft.Extensions.Options;
using VailITMonitor.Tools;
using VailITMonitor.Workers;

const string ServiceName = "VailITMonitor";
const string ReportingFile = "reporting.json";

// "--Section:Key=value" arguments are settings overrides; everything else is the command and its arguments.
string[] positional = args.Where(a => !a.StartsWith("--")).ToArray();
string command = positional.Length > 0 ? positional[0].ToLowerInvariant() : "run";
string[] rest = positional.Skip(1).ToArray();

// --- 1. Build the "host": configuration + logging + dependency injection -------------------
var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
{
    // Services start in C:\Windows\System32, so always look for appsettings.json next to the .exe.
    ContentRootPath = AppContext.BaseDirectory,
    Args = args.Where(a => a.StartsWith("--")).ToArray(),   // allow overrides like --Monitor:SampleIntervalSeconds=10
});

// reporting.json (written by the "connect" command) sits beside appsettings.json and overrides it,
// so upgrades that replace appsettings.json never lose the dashboard settings.
builder.Configuration.AddJsonFile(Path.Combine(AppContext.BaseDirectory, ReportingFile), optional: true, reloadOnChange: false);
if (args.Length > 0) builder.Configuration.AddCommandLine(args.Where(a => a.StartsWith("--")).ToArray());   // keep --overrides on top

builder.Services.AddWindowsService(o => o.ServiceName = ServiceName);   // no-op when not running as a service
if (OperatingSystem.IsWindows())
    builder.Services.Configure<EventLogSettings>(s => s.SourceName = ServiceName);

builder.Services.Configure<MonitorOptions>(builder.Configuration.GetSection(MonitorOptions.SectionName));

// Every class asks for what it needs in its constructor; the container supplies it.
builder.Services.AddSingleton<MetricCollector>();
builder.Services.AddSingleton<MetricStore>();
builder.Services.AddSingleton<RollupService>();
builder.Services.AddSingleton<CleanupService>();
builder.Services.AddSingleton<ReportGenerator>();
builder.Services.AddSingleton<MaintenanceService>();
builder.Services.AddSingleton<MachineInfo>();
builder.Services.AddSingleton<DashboardClient>();
builder.Services.AddSingleton<DashboardReporter>();

var reportingOptions = builder.Configuration.GetSection(MonitorOptions.SectionName).Get<MonitorOptions>()?.Reporting ?? new ReportingOptions();
bool reportingUsable = reportingOptions.Enabled && Uri.TryCreate(reportingOptions.DashboardUrl, UriKind.Absolute, out _);

if (command is "run" or "console")
{
    builder.Services.AddHostedService<SamplingWorker>();
    builder.Services.AddHostedService<MaintenanceWorker>();
    if (reportingUsable) builder.Services.AddHostedService<ReportingWorker>();
}

using var host = builder.Build();
var services = host.Services;
var options = services.GetRequiredService<IOptions<MonitorOptions>>().Value;

// --- 2. Do what was asked -----------------------------------------------------------------
switch (command)
{
    case "run":
    case "console":
        if (command == "console" || Environment.UserInteractive)
            Console.WriteLine($"VailITMonitor running in the console. Data: {options.ResolvedDataFolder}. Press Ctrl+C to stop.");
        if (reportingOptions.Enabled && !reportingUsable)
            host.Services.GetRequiredService<ILogger<MonitorOptions>>().LogWarning(
                "Reporting is enabled but Reporting:DashboardUrl '{Url}' is not a valid URL; not reporting to the dashboard.", reportingOptions.DashboardUrl);
        await host.RunAsync();
        return 0;

    case "sample":
    {
        var collector = services.GetRequiredService<MetricCollector>();
        collector.Prime();
        Console.WriteLine("Measuring for 2 seconds...");
        await Task.Delay(2000);
        foreach (var r in collector.Collect().OrderBy(r => MetricNames.SortOrder(r.Metric)))
            Console.WriteLine($"  {MetricNames.Label(new MetricKey(r.Metric, r.Instance)),-40} {r.Value,12:N2} {MetricNames.Unit(r.Metric)}");
        return 0;
    }

    case "maintenance":
        services.GetRequiredService<MaintenanceService>().RunOnce(DateTime.Now);
        Console.WriteLine("Maintenance complete.");
        return 0;

    case "cleanup":
        services.GetRequiredService<RollupService>().Run(DateTime.Now);   // never delete what isn't summarised
        services.GetRequiredService<CleanupService>().Run(DateTime.Now, force: true);
        Console.WriteLine("Cleanup complete.");
        return 0;

    case "report":
    {
        if (rest.Length == 0 || !Periods.TryParse(rest[0], out var period))
        {
            Console.Error.WriteLine("Usage: VailITMonitor report <daily|weekly|monthly|quarterly|yearly> [yyyy-MM-dd|today]");
            return 2;
        }

        // Default: the most recent completed period. A date picks the period containing it.
        DateTime start = rest.Length > 1
            ? Periods.StartOf(period, rest[1] == "today" ? DateTime.Today : DateTime.Parse(rest[1], CultureInfo.CurrentCulture), options.FirstDayOfWeek)
            : Periods.Previous(period, Periods.StartOf(period, DateTime.Today, options.FirstDayOfWeek), options.FirstDayOfWeek);

        services.GetRequiredService<RollupService>().Run(DateTime.Now);
        var file = services.GetRequiredService<ReportGenerator>().Generate(period, start);
        Console.WriteLine(file is null ? $"No data for {Periods.Label(period, start)}." : $"Created {file}");
        return file is null ? 1 : 0;
    }

    case "connect":
        return Connect(rest, options);

    case "report-test":
        return await ReportTest(services, options);

    case "demo":
        return RunDemo(rest.Length > 0 ? rest[0] : Path.Combine(options.ResolvedDataFolder, "Demo"), options);

    default:
        Console.WriteLine("Commands: (none) | console | sample | maintenance | cleanup | report <type> [date] | demo [folder] | connect <url> <apiKey> [serverType] | report-test");
        return command is "help" or "-h" or "--help" or "/?" ? 0 : 2;
}

// --- Demo: separate database + reports so it never mixes with real data ---------------------
static int RunDemo(string folder, MonitorOptions real)
{
    var demo = new MonitorOptions
    {
        DataFolder = Path.GetFullPath(folder),
        SampleIntervalSeconds = real.SampleIntervalSeconds,
        FirstDayOfWeek = real.FirstDayOfWeek,
        Retention = real.Retention,
        Reports = real.Reports,
    };
    demo.Reports.Folder = "";
    var opts = Options.Create(demo);

    if (File.Exists(demo.DatabasePath)) File.Delete(demo.DatabasePath);
    var store = new MetricStore(opts);
    var rollups = new RollupService(store, opts, Microsoft.Extensions.Logging.Abstractions.NullLogger<RollupService>.Instance);
    var reports = new ReportGenerator(store, rollups, opts);

    var today = DateTime.Today;
    var from = new DateTime(today.Year - 1, 1, 1);
    Console.WriteLine($"Creating fake readings from {from:d} to {today.AddDays(-1):d} in {demo.DatabasePath} ...");
    DemoData.Seed(store, from, today, demo.SampleInterval, demo.Retention.Raw.KeepDays);
    rollups.Run(DateTime.Now);

    foreach (var period in Periods.All)
    {
        var start = Periods.Previous(period, Periods.StartOf(period, today, demo.FirstDayOfWeek), demo.FirstDayOfWeek);
        var file = reports.Generate(period, start);
        Console.WriteLine(file is null ? $"  (no data for {period})" : $"  {file}");
    }

    Console.WriteLine("Done. Open the PDFs above to see what the real reports will look like.");
    return 0;
}

// --- Dashboard: "connect" saves the settings, "report-test" proves they work ----------------
static int Connect(string[] rest, MonitorOptions options)
{
    if (rest.Length < 2 || !Uri.TryCreate(rest[0], UriKind.Absolute, out var url) || (url.Scheme != "https" && url.Scheme != "http"))
    {
        Console.Error.WriteLine("Usage: VailITMonitor connect <https://dashboard-url> <apiKey> [serverType]");
        return 2;
    }

    var reporting = options.Reporting;
    var settings = new Dictionary<string, object>
    {
        ["Enabled"] = true,
        ["DashboardUrl"] = url.ToString().TrimEnd('/'),
        ["ApiKeyProtected"] = ApiKeyProtector.Protect(rest[1]),
        ["MachineId"] = reporting.MachineId ?? "",
        ["ServerType"] = rest.Length > 2 ? rest[2] : reporting.ServerType ?? "",
        ["Tags"] = reporting.Tags ?? [],
        ["IntervalSeconds"] = reporting.IntervalSeconds,
        ["TimeoutSeconds"] = reporting.TimeoutSeconds,
        ["PinnedCertificateThumbprint"] = reporting.PinnedCertificateThumbprint ?? "",
        ["InitialBacklogMinutes"] = reporting.InitialBacklogMinutes,
    };
    var file = Path.Combine(AppContext.BaseDirectory, ReportingFile);
    var json = System.Text.Json.JsonSerializer.Serialize(
        new { Monitor = new { Reporting = settings } },
        new System.Text.Json.JsonSerializerOptions { WriteIndented = true });
    File.WriteAllText(file, json);

    Console.WriteLine($"Saved dashboard settings to {file} (the API key is encrypted for this machine).");
    if (url.Scheme == "http") Console.WriteLine("Warning: plain http sends the API key unencrypted. Use https outside a lab.");
    Console.WriteLine("Restart the service to start reporting:  Restart-Service VailITMonitor");
    return 0;
}

static async Task<int> ReportTest(IServiceProvider services, MonitorOptions options)
{
    if (!Uri.TryCreate(options.Reporting.DashboardUrl, UriKind.Absolute, out _))
    {
        Console.Error.WriteLine("No dashboard configured. Run: VailITMonitor connect <url> <apiKey> [serverType]");
        return 2;
    }

    var client = services.GetRequiredService<DashboardClient>();
    var machine = services.GetRequiredService<MachineInfo>();
    Console.WriteLine($"Dashboard: {client.BaseUri}   machine id: {machine.MachineId}");

    var (me, info) = await client.GetMeAsync(CancellationToken.None);
    Console.WriteLine(info is not null
        ? $"  Key accepted. Dashboard name \"{info.DisplayName}\", group \"{info.Group}\"."
        : $"  monitors/me: {me.Outcome} (HTTP {me.StatusCode}) {me.Message}");

    var collector = services.GetRequiredService<MetricCollector>();
    collector.Prime();
    await Task.Delay(2000);
    var readings = collector.Collect();
    var now = DateTime.UtcNow;
    var report = new VailIT.Reporting.Contracts.MonitorReport
    {
        SentAtUtc = now,
        Monitor = machine.Build(),
        Samples =
        [
            new VailIT.Reporting.Contracts.SampleDto
            {
                TimestampUtc = new DateTime(now.Ticks - now.Ticks % TimeSpan.TicksPerSecond, DateTimeKind.Utc),
                Readings = readings.Select(r => new VailIT.Reporting.Contracts.ReadingDto { Metric = r.Metric, Instance = r.Instance, Value = r.Value }).ToList(),
            },
        ],
    };

    var (result, ack) = await client.SendReportAsync(report, CancellationToken.None);
    if (ack is null)
    {
        Console.WriteLine($"  Report failed: {result.Outcome} (HTTP {result.StatusCode}) {result.Message}");
        return 1;
    }
    Console.WriteLine($"  Report accepted: {readings.Count} readings, dashboard time {ack.ServerTimeUtc:u}, interval {ack.ReportIntervalSeconds}s.");
    return 0;
}
