using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.ServiceProcess;
using System.Threading;
using VailITMonitor.Collection;
using VailITMonitor.Configuration;
using VailITMonitor.Dashboard;
using VailITMonitor.Processing;
using VailITMonitor.Reporting;
using VailITMonitor.Service;
using VailITMonitor.Storage;
using VailITMonitor.Tools;

namespace VailITMonitor
{
    /// <summary>
    /// VailITMonitor - .NET Framework 4.8 edition - entry point.
    ///
    ///   VailITMonitor.exe                     run as a Windows service (started by Windows)
    ///   VailITMonitor.exe console             run in this console window (Ctrl+C to stop)
    ///   VailITMonitor.exe sample              take one reading and print it
    ///   VailITMonitor.exe maintenance         run summaries + reports + cleanup now
    ///   VailITMonitor.exe cleanup             run every cleanup now, ignoring CleanupEveryDays
    ///   VailITMonitor.exe report daily [date] create a report now (daily|weekly|monthly|quarterly|yearly)
    ///   VailITMonitor.exe demo [folder]       create a demo database full of fake data, and sample reports
    ///   VailITMonitor.exe connect &lt;url&gt; &lt;apiKey&gt; [serverType]
    ///                                         turn on reporting to VailITDashboard (saved in reporting.config)
    ///   VailITMonitor.exe report-test         take one reading, send it to the dashboard, print the reply
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
                        ServiceBase.Run(new MonitorService(settings, new MetricCollector(settings.Metrics), store, maintenance, CreateReporter(settings, store)));
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

                    case "connect":
                        return Connect(rest, settings);

                    case "report-test":
                        return ReportTest(settings);

                    case "demo":
                        return RunDemo(rest.Length > 0 ? rest[0] : Path.Combine(settings.ResolvedDataFolder, "Demo"), settings);

                    default:
                        Console.WriteLine("Commands: (none) | console | sample | maintenance | cleanup | report <type> [date] | demo [folder] | connect <url> <apiKey> [serverType] | report-test");
                        return command == "help" || command == "/?" ? 0 : 2;
                }
            }
            catch (Exception ex)
            {
                Log.Error("VailITMonitor failed", ex);
                return 1;
            }
        }

        private static int RunInConsole(MonitorSettings settings, MetricStore store, MaintenanceService maintenance)
        {
            Console.WriteLine("VailITMonitor running in the console. Data: " + settings.ResolvedDataFolder + ". Press Ctrl+C to stop.");
            var service = new MonitorService(settings, new MetricCollector(settings.Metrics), store, maintenance, CreateReporter(settings, store));
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

        /// <summary>The dashboard reporter, or null when reporting is off or not set up correctly.</summary>
        private static DashboardReporter CreateReporter(MonitorSettings settings, MetricStore store)
        {
            if (!settings.Reporting.Enabled) return null;
            if (!settings.Reporting.HasValidUrl)
            {
                Log.Warn("Reporting is enabled but Reporting.DashboardUrl '" + settings.Reporting.DashboardUrl + "' is not a valid URL; not reporting to the dashboard.");
                return null;
            }
            return new DashboardReporter(store, new DashboardClient(settings.Reporting), new MachineInfo(settings), settings);
        }

        /// <summary>Saves the dashboard settings to reporting.config, which App.config's appSettings includes.</summary>
        private static int Connect(string[] rest, MonitorSettings settings)
        {
            Uri url;
            if (rest.Length < 2 || !Uri.TryCreate(rest[0], UriKind.Absolute, out url) || (url.Scheme != "https" && url.Scheme != "http"))
            {
                Console.Error.WriteLine("Usage: VailITMonitor connect <https://dashboard-url> <apiKey> [serverType]");
                return 2;
            }

            var r = settings.Reporting;
            var values = new[]
            {
                new[] { "Reporting.Enabled", "true" },
                new[] { "Reporting.DashboardUrl", url.ToString().TrimEnd('/') },
                new[] { "Reporting.ApiKeyProtected", ApiKeyProtector.Protect(rest[1]) },
                new[] { "Reporting.MachineId", r.MachineId ?? "" },
                new[] { "Reporting.ServerType", rest.Length > 2 ? rest[2] : r.ServerType ?? "" },
                new[] { "Reporting.Tags", string.Join(",", r.Tags ?? new string[0]) },
                new[] { "Reporting.IntervalSeconds", r.IntervalSeconds.ToString(CultureInfo.InvariantCulture) },
                new[] { "Reporting.TimeoutSeconds", r.TimeoutSeconds.ToString(CultureInfo.InvariantCulture) },
                new[] { "Reporting.PinnedCertificateThumbprint", r.PinnedCertificateThumbprint ?? "" },
                new[] { "Reporting.InitialBacklogMinutes", r.InitialBacklogMinutes.ToString(CultureInfo.InvariantCulture) },
            };

            var doc = new System.Xml.Linq.XDocument(
                new System.Xml.Linq.XComment(" Written by \"VailITMonitor.exe connect\". Overrides the Reporting.* keys in VailITMonitor.exe.config. "),
                new System.Xml.Linq.XElement("appSettings",
                    values.Select(v => new System.Xml.Linq.XElement("add",
                        new System.Xml.Linq.XAttribute("key", v[0]),
                        new System.Xml.Linq.XAttribute("value", v[1])))));
            var file = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "reporting.config");
            doc.Save(file);

            Console.WriteLine("Saved dashboard settings to " + file + " (the API key is encrypted for this machine).");
            if (url.Scheme == "http") Console.WriteLine("Warning: plain http sends the API key unencrypted. Use https outside a lab.");
            Console.WriteLine("Restart the service to start reporting:  Restart-Service VailITMonitor");
            return 0;
        }

        /// <summary>Takes one reading, sends it to the dashboard and prints what came back.</summary>
        private static int ReportTest(MonitorSettings settings)
        {
            if (!settings.Reporting.HasValidUrl)
            {
                Console.Error.WriteLine("No dashboard configured. Run: VailITMonitor connect <url> <apiKey> [serverType]");
                return 2;
            }

            using (var client = new DashboardClient(settings.Reporting))
            {
                var machine = new MachineInfo(settings);
                Console.WriteLine("Dashboard: " + client.BaseUri + "   machine id: " + machine.MachineId);

                var me = client.GetMe();
                Console.WriteLine(me.Outcome == SendOutcome.Ok
                    ? "  Key accepted. Dashboard name \"" + me.Value.DisplayName + "\", group \"" + me.Value.Group + "\"."
                    : "  monitors/me: " + me.Outcome + " (" + me.Explain() + ")");

                var collector = new MetricCollector(settings.Metrics);
                collector.Prime();
                Thread.Sleep(2000);
                var readings = collector.Collect();
                var now = DateTime.UtcNow;
                var report = new VailIT.Reporting.Contracts.MonitorReport
                {
                    SentAtUtc = now,
                    Monitor = machine.Build(),
                };
                report.Samples.Add(new VailIT.Reporting.Contracts.SampleDto
                {
                    TimestampUtc = new DateTime(now.Ticks - now.Ticks % TimeSpan.TicksPerSecond, DateTimeKind.Utc),
                    Readings = readings.Select(x => new VailIT.Reporting.Contracts.ReadingDto { Metric = x.Metric, Instance = x.Instance, Value = x.Value }).ToList(),
                });

                var result = client.SendReport(report);
                if (result.Outcome != SendOutcome.Ok)
                {
                    Console.WriteLine("  Report failed: " + result.Outcome + " (" + result.Explain() + ")");
                    return 1;
                }
                Console.WriteLine("  Report accepted: " + readings.Count + " readings, dashboard time " +
                                  result.Value.ServerTimeUtc.ToString("u", CultureInfo.InvariantCulture) + ", interval " + result.Value.ReportIntervalSeconds + "s.");
                return 0;
            }
        }

        private static int RunReport(string[] rest, MonitorSettings settings, RollupService rollups, ReportGenerator reports)
        {
            PeriodType period;
            if (rest.Length == 0 || !Periods.TryParse(rest[0], out period))
            {
                Console.Error.WriteLine("Usage: VailITMonitor report <daily|weekly|monthly|quarterly|yearly> [yyyy-MM-dd|today]");
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
