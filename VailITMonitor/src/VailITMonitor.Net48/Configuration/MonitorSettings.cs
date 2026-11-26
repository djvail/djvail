using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Configuration;
using System.Globalization;
using System.IO;
using System.Linq;
using VailITMonitor.Processing;

namespace VailITMonitor.Configuration
{
    /// <summary>
    /// All settings, read from the &lt;appSettings&gt; section of VailITMonitor.exe.config.
    ///
    /// .NET Framework has no automatic "bind this section to a class" feature like .NET 10's
    /// IOptions, so each value is read and converted by hand in <see cref="Load"/>.
    /// To add a setting: add a property, read it in Load(), add the key to App.config.
    /// </summary>
    public sealed class MonitorSettings
    {
        public int SampleIntervalSeconds { get; set; }
        public string DataFolder { get; set; }
        public int MaintenanceIntervalMinutes { get; set; }
        public DayOfWeek FirstDayOfWeek { get; set; }
        public bool VerboseLogging { get; set; }

        public MetricSettings Metrics { get; set; }
        public RetentionSettings Retention { get; set; }
        public ReportSettings Reports { get; set; }

        // ----- Derived values -----

        public TimeSpan SampleInterval
        {
            get { return TimeSpan.FromSeconds(Math.Max(5, SampleIntervalSeconds)); }
        }

        public string ResolvedDataFolder
        {
            get { return Path.GetFullPath(Environment.ExpandEnvironmentVariables(DataFolder)); }
        }

        public string DatabasePath
        {
            get { return Path.Combine(ResolvedDataFolder, "VailITMonitor.db"); }
        }

        public string ResolvedReportFolder
        {
            get
            {
                return string.IsNullOrWhiteSpace(Reports.Folder)
                    ? Path.Combine(ResolvedDataFolder, "Reports")
                    : Path.GetFullPath(Environment.ExpandEnvironmentVariables(Reports.Folder));
            }
        }

        /// <summary>
        /// Reads the config file. Command-line arguments like --Retention.Raw.KeepDays=10
        /// override the file (handy for testing).
        /// </summary>
        public static MonitorSettings Load(IEnumerable<string> overrides)
        {
            var values = new NameValueCollection(ConfigurationManager.AppSettings);
            foreach (var arg in overrides ?? Enumerable.Empty<string>())
            {
                if (!arg.StartsWith("--", StringComparison.Ordinal)) continue;
                int eq = arg.IndexOf('=');
                if (eq > 2) values[arg.Substring(2, eq - 2)] = arg.Substring(eq + 1);
            }

            var s = new Reader(values);
            return new MonitorSettings
            {
                SampleIntervalSeconds = s.Int("SampleIntervalSeconds", 60),
                DataFolder = s.String("DataFolder", @"%ProgramData%\VailITMonitor-Net48"),
                MaintenanceIntervalMinutes = s.Int("MaintenanceIntervalMinutes", 15),
                FirstDayOfWeek = s.Enum("FirstDayOfWeek", DayOfWeek.Monday),
                VerboseLogging = s.Bool("Logging.Verbose", true),

                Metrics = new MetricSettings
                {
                    Cpu = s.Bool("Metrics.Cpu", true),
                    CpuPerCore = s.Bool("Metrics.CpuPerCore", true),
                    Memory = s.Bool("Metrics.Memory", true),
                    Disks = s.Bool("Metrics.Disks", true),
                    DiskIo = s.Bool("Metrics.DiskIo", true),
                    Network = s.Bool("Metrics.Network", true),
                    System = s.Bool("Metrics.System", true),
                    Self = s.Bool("Metrics.Self", true),
                    Drives = s.List("Metrics.Drives"),
                    ExcludeAdapters = s.List("Metrics.ExcludeAdapters"),
                },

                Retention = new RetentionSettings
                {
                    Raw = s.Tier("Retention.Raw", 30, 1),
                    Daily = s.Tier("Retention.Daily", 730, 7),
                    Weekly = s.Tier("Retention.Weekly", 1095, 30),
                    Monthly = s.Tier("Retention.Monthly", 1825, 90),
                    Quarterly = s.Tier("Retention.Quarterly", 0, 365),
                    Yearly = s.Tier("Retention.Yearly", 0, 365),
                },

                Reports = new ReportSettings
                {
                    Enabled = s.Bool("Reports.Enabled", true),
                    Folder = s.String("Reports.Folder", ""),
                    CompanyName = s.String("Reports.CompanyName", "Vail IT"),
                    PageSize = s.String("Reports.PageSize", "Letter"),
                    DelayMinutes = s.Int("Reports.DelayMinutes", 5),
                    CatchUpPeriods = s.Int("Reports.CatchUpPeriods", 3),
                    Daily = s.ReportType("Reports.Daily", 90),
                    Weekly = s.ReportType("Reports.Weekly", 365),
                    Monthly = s.ReportType("Reports.Monthly", 0),
                    Quarterly = s.ReportType("Reports.Quarterly", 0),
                    Yearly = s.ReportType("Reports.Yearly", 0),
                },
            };
        }

        /// <summary>Small helper that converts the text values, falling back to defaults.</summary>
        private sealed class Reader
        {
            private readonly NameValueCollection _values;

            public Reader(NameValueCollection values)
            {
                _values = values;
            }

            public string String(string key, string fallback)
            {
                var v = _values[key];
                return v ?? fallback;
            }

            public int Int(string key, int fallback)
            {
                int result;
                return int.TryParse(_values[key], NumberStyles.Integer, CultureInfo.InvariantCulture, out result) ? result : fallback;
            }

            public bool Bool(string key, bool fallback)
            {
                bool result;
                return bool.TryParse(_values[key], out result) ? result : fallback;
            }

            public T Enum<T>(string key, T fallback) where T : struct
            {
                T result;
                return System.Enum.TryParse(_values[key], true, out result) ? result : fallback;
            }

            public string[] List(string key)
            {
                return (_values[key] ?? "")
                    .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(x => x.Trim())
                    .Where(x => x.Length > 0)
                    .ToArray();
            }

            public TierRetention Tier(string prefix, int keepDays, int everyDays)
            {
                return new TierRetention
                {
                    KeepDays = Int(prefix + ".KeepDays", keepDays),
                    CleanupEveryDays = Int(prefix + ".CleanupEveryDays", everyDays),
                };
            }

            public ReportTypeSettings ReportType(string prefix, int keepDays)
            {
                return new ReportTypeSettings
                {
                    Enabled = Bool(prefix + ".Enabled", true),
                    KeepDays = Int(prefix + ".KeepDays", keepDays),
                };
            }
        }
    }

    public sealed class MetricSettings
    {
        public bool Cpu { get; set; }
        public bool CpuPerCore { get; set; }
        public bool Memory { get; set; }
        public bool Disks { get; set; }
        public bool DiskIo { get; set; }
        public bool Network { get; set; }
        public bool System { get; set; }
        public bool Self { get; set; }
        public string[] Drives { get; set; }
        public string[] ExcludeAdapters { get; set; }
    }

    public sealed class TierRetention
    {
        public int KeepDays { get; set; }
        public int CleanupEveryDays { get; set; }
    }

    public sealed class RetentionSettings
    {
        public TierRetention Raw { get; set; }
        public TierRetention Daily { get; set; }
        public TierRetention Weekly { get; set; }
        public TierRetention Monthly { get; set; }
        public TierRetention Quarterly { get; set; }
        public TierRetention Yearly { get; set; }

        public TierRetention For(PeriodType period)
        {
            switch (period)
            {
                case PeriodType.Day: return Daily;
                case PeriodType.Week: return Weekly;
                case PeriodType.Month: return Monthly;
                case PeriodType.Quarter: return Quarterly;
                case PeriodType.Year: return Yearly;
                default: throw new ArgumentOutOfRangeException("period");
            }
        }
    }

    public sealed class ReportTypeSettings
    {
        public bool Enabled { get; set; }
        public int KeepDays { get; set; }
    }

    public sealed class ReportSettings
    {
        public bool Enabled { get; set; }
        public string Folder { get; set; }
        public string CompanyName { get; set; }
        public string PageSize { get; set; }
        public int DelayMinutes { get; set; }
        public int CatchUpPeriods { get; set; }
        public ReportTypeSettings Daily { get; set; }
        public ReportTypeSettings Weekly { get; set; }
        public ReportTypeSettings Monthly { get; set; }
        public ReportTypeSettings Quarterly { get; set; }
        public ReportTypeSettings Yearly { get; set; }

        public ReportTypeSettings For(PeriodType period)
        {
            switch (period)
            {
                case PeriodType.Day: return Daily;
                case PeriodType.Week: return Weekly;
                case PeriodType.Month: return Monthly;
                case PeriodType.Quarter: return Quarterly;
                case PeriodType.Year: return Yearly;
                default: throw new ArgumentOutOfRangeException("period");
            }
        }
    }
}
