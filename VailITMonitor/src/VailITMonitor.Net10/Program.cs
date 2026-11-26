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

if (command is "run" or "console")
{
    builder.Services.AddHostedService<SamplingWorker>();
    builder.Services.AddHostedService<MaintenanceWorker>();
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

    case "demo":
        return RunDemo(rest.Length > 0 ? rest[0] : Path.Combine(options.ResolvedDataFolder, "Demo"), options);

    default:
        Console.WriteLine("Commands: (none) | console | sample | maintenance | cleanup | report <type> [date] | demo [folder]");
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
