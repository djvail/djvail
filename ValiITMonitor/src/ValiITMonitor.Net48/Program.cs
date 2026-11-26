using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.ServiceProcess;
using System.Threading;
using ValiITMonitor.Collection;
using ValiITMonitor.Configuration;
using ValiITMonitor.Processing;
using ValiITMonitor.Reporting;
using ValiITMonitor.Service;
using ValiITMonitor.Storage;
using ValiITMonitor.Tools;

namespace ValiITMonitor
{
    /// <summary>
    /// ValiITMonitor - .NET Framework 4.8 edition - entry point.
    ///
    ///   ValiITMonitor.exe                     run as a Windows service (started by Windows)
    ///   ValiITMonitor.exe console             run in this console window (Ctrl+C to stop)
    ///   ValiITMonitor.exe sample              take one reading and print it
    ///   ValiITMonitor.exe maintenance         run summaries + reports + cleanup now
    ///   ValiITMonitor.exe cleanup             run every cleanup now, ignoring CleanupEveryDays
    ///   ValiITMonitor.exe report daily [date] create a report now (daily|weekly|monthly|quarterly|yearly)
    ///   ValiITMonitor.exe demo [folder]       create a demo database full of fake data, and sample reports
    ///
    /// Any --Key=Value argument overrides the matching appSettings key, e.g. --SampleIntervalSeconds=10
    /// </summary>
    internal static class Program
    {
        private static int Main(string[] args)
        {
            // Services start in C:\Windows\System32; make relative paths resolve next to the .exe.
            Directory.SetCurrentDirectory(AppDomain.CurrentDomain.BaseDirectory);

            var positional = args.Where(a => !a.StartsWith("--", StringComparison.Ordinal)).ToArray();
            string command = positional.Length > 0 ? positional[0].ToLowerInvariant() : "run";
            string[] rest = positional.Skip(1).ToArray();

            var settings = MonitorSettings.Load(args);
            Log.Verbose = settings.VerboseLogging;

            // "Poor man's dependency injection": build the objects by hand, in order.
            // (.NET 10 registers them with a container and lets it do this.)
            var store = new MetricStore(settings.DatabasePath);
            var rollups = new RollupService(store, settings);
            var reports = new ReportGenerator(store, rollups, settings);
            var cleanup = new CleanupService(store, settings);
            var maintenance = new MaintenanceService(rollups, reports, cleanup, store, settings);

            try
            {
                switch (command)
                {
                    case "run":
                        if (Environment.UserInteractive) goto case "console";   // started by hand, not by Windows
                        ServiceBase.Run(new MonitorService(settings, new MetricCollector(settings.Metrics), store, maintenance));
                        return 0;

                    case "console":
                        return RunInConsole(settings, store, maintenance);

                    case "sample":
                    {
                        var collector = new MetricCollector(settings.Metrics);
                        collector.Prime();
                        Console.WriteLine("Measuring for 2 seconds...");
                        Thread.Sleep(2000);
                        foreach (var r in collector.Collect().OrderBy(r => MetricNames.SortOrder(r.Metric)))
                            Console.WriteLine("  {0,-40} {1,12:N2} {2}", MetricNames.Label(new MetricKey(r.Metric, r.Instance)), r.Value, MetricNames.Unit(r.Metric));
                        return 0;
                    }

                    case "maintenance":
                        maintenance.RunOnce(DateTime.Now);
                        Console.WriteLine("Maintenance complete.");
                        return 0;

                    case "cleanup":
                        rollups.Run(DateTime.Now);
                        cleanup.Run(DateTime.Now, true);
                        Console.WriteLine("Cleanup complete.");
                        return 0;

                    case "report":
                        return RunReport(rest, settings, rollups, reports);

                    case "demo":
                        return RunDemo(rest.Length > 0 ? rest[0] : Path.Combine(settings.ResolvedDataFolder, "Demo"), settings);

                    default:
                        Console.WriteLine("Commands: (none) | console | sample | maintenance | cleanup | report <type> [date] | demo [folder]");
                        return command == "help" || command == "/?" ? 0 : 2;
                }
            }
            catch (Exception ex)
            {
                Log.Error("ValiITMonitor failed", ex);
                return 1;
            }
        }

        private static int RunInConsole(MonitorSettings settings, MetricStore store, MaintenanceService maintenance)
        {
            Console.WriteLine("ValiITMonitor running in the console. Data: " + settings.ResolvedDataFolder + ". Press Ctrl+C to stop.");
            var service = new MonitorService(settings, new MetricCollector(settings.Metrics), store, maintenance);
            var stop = new ManualResetEvent(false);
            Console.CancelKeyPress += (sender, e) =>
            {
                e.Cancel = true;   // don't kill the process; let us stop cleanly
                stop.Set();
            };

            service.StartInConsole();
            stop.WaitOne();
            service.StopInConsole();
            return 0;
        }

        private static int RunReport(string[] rest, MonitorSettings settings, RollupService rollups, ReportGenerator reports)
        {
            PeriodType period;
            if (rest.Length == 0 || !Periods.TryParse(rest[0], out period))
            {
                Console.Error.WriteLine("Usage: ValiITMonitor report <daily|weekly|monthly|quarterly|yearly> [yyyy-MM-dd|today]");
                return 2;
            }

            DateTime start;
            if (rest.Length > 1)
            {
                var date = rest[1] == "today" ? DateTime.Today : DateTime.Parse(rest[1], CultureInfo.CurrentCulture);
                start = Periods.StartOf(period, date, settings.FirstDayOfWeek);
            }
            else
            {
                start = Periods.Previous(period, Periods.StartOf(period, DateTime.Today, settings.FirstDayOfWeek), settings.FirstDayOfWeek);
            }

            rollups.Run(DateTime.Now);
            var file = reports.Generate(period, start, null);
            Console.WriteLine(file == null ? "No data for " + Periods.Label(period, start) + "." : "Created " + file);
            return file == null ? 1 : 0;
        }

        private static int RunDemo(string folder, MonitorSettings real)
        {
            var demo = MonitorSettings.Load(new[] { "--DataFolder=" + Path.GetFullPath(folder), "--Reports.Folder=" });
            demo.SampleIntervalSeconds = real.SampleIntervalSeconds;
            demo.FirstDayOfWeek = real.FirstDayOfWeek;
            Log.Verbose = false;

            if (File.Exists(demo.DatabasePath)) File.Delete(demo.DatabasePath);
            var store = new MetricStore(demo.DatabasePath);
            var rollups = new RollupService(store, demo);
            var reports = new ReportGenerator(store, rollups, demo);

            var today = DateTime.Today;
            var from = new DateTime(today.Year - 1, 1, 1);
            Console.WriteLine("Creating fake readings from {0:d} to {1:d} in {2} ...", from, today.AddDays(-1), demo.DatabasePath);
            var quiet = Console.Out;
            Console.SetOut(TextWriter.Null);   // hide the per-summary log lines
            try
            {
                DemoData.Seed(store, from, today, demo.SampleInterval, demo.Retention.Raw.KeepDays);
                rollups.Run(DateTime.Now);
            }
            finally
            {
                Console.SetOut(quiet);
            }

            foreach (var period in Periods.All)
            {
                var start = Periods.Previous(period, Periods.StartOf(period, today, demo.FirstDayOfWeek), demo.FirstDayOfWeek);
                var file = reports.Generate(period, start, null);
                Console.WriteLine(file == null ? "  (no data for " + period + ")" : "  " + file);
            }

            Console.WriteLine("Done. Open the PDFs above to see what the real reports will look like.");
            return 0;
        }
    }
}
