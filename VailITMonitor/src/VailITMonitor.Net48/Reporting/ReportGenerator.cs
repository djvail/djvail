using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using VailITMonitor.Collection;
using VailITMonitor.Configuration;
using VailITMonitor.Processing;
using VailITMonitor.Storage;

namespace VailITMonitor.Reporting
{
    /// <summary>
    /// Builds the PDF reports with QuestPDF (https://www.questpdf.com).
    /// Daily reports chart every raw reading; longer reports chart one point per day (or per
    /// week/month once that is all that is left after cleanup).
    /// </summary>
    public sealed class ReportGenerator
    {
        private const string TextPrimary = "#191919";
        private const string TextSecondary = "#666666";
        private const string Rule = "#dddddd";
        private const string Shade = "#f5f5f5";

        private readonly MetricStore _store;
        private readonly RollupService _rollups;
        private readonly MonitorSettings _options;

        static ReportGenerator()
        {
            // Free Community licence: individuals and companies under USD 1M annual revenue.
            QuestPDF.Settings.License = LicenseType.Community;
        }

        public ReportGenerator(MetricStore store, RollupService rollups, MonitorSettings options)
        {
            _store = store;
            _rollups = rollups;
            _options = options;
        }

        /// <summary>Creates the report and returns its path, or null if there is no data for the period.</summary>
        public string Generate(PeriodType period, DateTime start, string folder)
        {
            var end = Periods.Next(period, start);
            var data = period == PeriodType.Day ? BuildDaily(start, end) : BuildPeriod(period, start, end);
            if (data == null) return null;

            if (folder == null) folder = Path.Combine(_options.ResolvedReportFolder, Periods.Adjective(period));
            Directory.CreateDirectory(folder);
            var path = Path.Combine(folder, string.Format("VailITMonitor_{0}_{1}_{2}.pdf",
                Environment.MachineName, Periods.Adjective(period), Periods.FileStem(period, start)));

            Render(data, path);
            return path;
        }

        // ================================================================ gather the data

        private sealed class ReportData
        {
            public ReportData(PeriodType period, DateTime start, List<RollupRow> summary)
            {
                Period = period;
                Start = start;
                Summary = summary;
                Charts = new List<ChartSpec>();
                Coverage = "";
            }

            public PeriodType Period { get; }
            public DateTime Start { get; }
            public List<RollupRow> Summary { get; }
            public List<ChartSpec> Charts { get; }
            public string Coverage { get; set; }
        }

        private ReportData BuildDaily(DateTime start, DateTime end)
        {
            var samples = _store.GetSamples(Periods.ToUnix(start), Periods.ToUnix(end));
            if (samples.Count == 0) return null;

            var summary = _store.GetRollups(PeriodType.Day, start, end).Values.FirstOrDefault()
                          ?? _rollups.SummariseRaw(start, end);

            var data = new ReportData(PeriodType.Day, start, summary);

            // Local functions are available from C# 7.0.
            ChartSeries Line(string metric, string instance, string label, int slot)
            {
                List<SamplePoint> points;
                if (!samples.TryGetValue(new MetricKey(metric, instance), out points)) return null;
                return new ChartSeries(label,
                    points.Select(p => Periods.FromUnix(p.Ts)).ToArray(),
                    points.Select(p => p.Value).ToArray(),
                    slot);
            }

            AddChart(data, "CPU usage", "%", 100, Line(MetricNames.CpuTotal, "", "CPU", 0));
            AddChart(data, "Memory", "%", 100,
                Line(MetricNames.MemUsedPct, "", "RAM used", 0),
                Line(MetricNames.MemCommitPct, "", "Commit charge (RAM + page file)", 1));
            AddChart(data, "Free disk space", "% free", 100,
                Instances(samples.Keys, MetricNames.DiskFreePct)
                    .Select((drive, i) => Line(MetricNames.DiskFreePct, drive, drive, i)).ToArray());
            AddChart(data, "Disk activity", "KB/s", null,
                Line(MetricNames.DiskReadKBps, "", "Read", 0),
                Line(MetricNames.DiskWriteKBps, "", "Write", 1));
            AddChart(data, "Network (all adapters)", "KB/s", null,
                Line(MetricNames.NetRecvKBps, MetricNames.Total, "Received", 0),
                Line(MetricNames.NetSentKBps, MetricNames.Total, "Sent", 1));

            data.Coverage = CoverageText(summary, (end - start).TotalSeconds);
            return data;
        }

        private ReportData BuildPeriod(PeriodType period, DateTime start, DateTime end)
        {
            PeriodType[] candidates;
            switch (period)
            {
                case PeriodType.Year: candidates = new[] { PeriodType.Week, PeriodType.Day, PeriodType.Month }; break;
                case PeriodType.Quarter: candidates = new[] { PeriodType.Day, PeriodType.Week, PeriodType.Month }; break;
                default: candidates = new[] { PeriodType.Day, PeriodType.Week }; break;
            }

            var pointType = PeriodType.Day;
            var points = new SortedDictionary<DateTime, List<RollupRow>>();
            foreach (var candidate in candidates.Where(c => c < period))
            {
                pointType = candidate;
                points = LoadPoints(candidate, start, end);
                if (points.Count > 0) break;
            }
            if (points.Count == 0) return null;

            string each = Periods.Adjective(pointType).ToLowerInvariant();
            string eachNoun = pointType.ToString().ToLowerInvariant();

            List<RollupRow> summary;
            if (!_store.GetRollups(period, start, end).TryGetValue(start, out summary))
                summary = _rollups.SummariseDays(start, end);
            if (summary.Count == 0)
                summary = points.Values.SelectMany(r => r).GroupBy(r => r.Key).Select(g => Stats.Combine(g.Key, g.ToList())).ToList();

            var data = new ReportData(period, start, summary);
            var keys = new HashSet<MetricKey>(points.Values.SelectMany(r => r).Select(r => r.Key));

            ChartSeries Line(string metric, string instance, string label, int slot, Func<RollupRow, double> pick)
            {
                var key = new MetricKey(metric, instance);
                var found = points
                    .Select(p => new { Day = p.Key, Row = p.Value.FirstOrDefault(r => r.Key == key) })   // anonymous type
                    .Where(p => p.Row != null)
                    .ToList();
                if (found.Count == 0) return null;
                return new ChartSeries(label, found.Select(f => f.Day).ToArray(), found.Select(f => pick(f.Row)).ToArray(), slot);
            }

            AddChart(data, "CPU usage (" + each + ")", "%", 100,
                Line(MetricNames.CpuTotal, "", "Average", 0, r => r.Avg),
                Line(MetricNames.CpuTotal, "", "95th percentile", 1, r => r.P95),
                Line(MetricNames.CpuTotal, "", "Peak", 2, r => r.Max));
            AddChart(data, "Memory used (" + each + ")", "%", 100,
                Line(MetricNames.MemUsedPct, "", "Average", 0, r => r.Avg),
                Line(MetricNames.MemUsedPct, "", "Peak", 2, r => r.Max));
            AddChart(data, "Free disk space (lowest each " + eachNoun + ")", "% free", 100,
                Instances(keys, MetricNames.DiskFreePct)
                    .Select((drive, i) => Line(MetricNames.DiskFreePct, drive, drive, i, r => r.Min)).ToArray());
            AddChart(data, "Disk activity (" + each + " average)", "KB/s", null,
                Line(MetricNames.DiskReadKBps, "", "Read", 0, r => r.Avg),
                Line(MetricNames.DiskWriteKBps, "", "Write", 1, r => r.Avg));
            AddChart(data, "Network (" + each + " average, all adapters)", "KB/s", null,
                Line(MetricNames.NetRecvKBps, MetricNames.Total, "Received", 0, r => r.Avg),
                Line(MetricNames.NetSentKBps, MetricNames.Total, "Sent", 1, r => r.Avg));

            data.Coverage = CoverageText(summary, (end - start).TotalSeconds);
            return data;
        }

        /// <summary>A week straddling the period boundary belongs to the side holding its Thursday.</summary>
        private SortedDictionary<DateTime, List<RollupRow>> LoadPoints(PeriodType pointType, DateTime start, DateTime end)
        {
            if (pointType != PeriodType.Week) return _store.GetRollups(pointType, start, end);

            var weeks = _store.GetRollups(PeriodType.Week, start.AddDays(-6), end);
            foreach (var week in weeks.Keys.ToList())
            {
                var middle = week.AddDays(3);
                if (middle < start || middle >= end) weeks.Remove(week);
            }
            return weeks;
        }

        private static void AddChart(ReportData data, string title, string unit, double? yMax, params ChartSeries[] series)
        {
            var present = series.Where(s => s != null).ToList();
            if (present.Count == 0) return;

            string timeFormat;
            bool labelPoints;
            switch (data.Period)
            {
                case PeriodType.Day: timeFormat = "HH:mm"; labelPoints = false; break;
                case PeriodType.Week: timeFormat = "ddd d"; labelPoints = true; break;
                default: timeFormat = "MMM d"; labelPoints = true; break;
            }

            data.Charts.Add(new ChartSpec(title, unit, present, yMax, timeFormat, labelPoints));
        }

        private static IEnumerable<string> Instances(IEnumerable<MetricKey> keys, string metric)
        {
            return keys.Where(k => k.Metric == metric).Select(k => k.Instance).Distinct().OrderBy(x => x);
        }

        private string CoverageText(List<RollupRow> summary, double periodSeconds)
        {
            var cpu = summary.FirstOrDefault(r => r.Key.Metric == MetricNames.CpuTotal)
                      ?? summary.OrderByDescending(r => r.Count).FirstOrDefault();
            if (cpu == null) return "";
            double expected = periodSeconds / _options.SampleInterval.TotalSeconds;
            double pct = Math.Min(100, 100 * cpu.Count / expected);
            return string.Format("{0:N0} readings ({1:N0}% of the period)", cpu.Count, pct);
        }

        // ================================================================ draw the PDF

        private void Render(ReportData data, string path)
        {
            var pageSize = string.Equals(_options.Reports.PageSize, "A4", StringComparison.OrdinalIgnoreCase) ? PageSizes.A4 : PageSizes.Letter;
            var charts = data.Charts.Select(ChartRenderer.RenderPng).ToList();
            string title = Periods.Adjective(data.Period) + " System Report";
            string subtitle = data.Period == PeriodType.Day
                ? Periods.Label(data.Period, data.Start)
                : string.Format("{0}  ({1:MMM d, yyyy} - {2:MMM d, yyyy})",
                    Periods.Label(data.Period, data.Start), data.Start, Periods.Next(data.Period, data.Start).AddDays(-1));

            Document.Create(document => document.Page(page =>
            {
                page.Size(pageSize);
                page.Margin(36);
                page.PageColor(Colors.White);
                page.DefaultTextStyle(t => t.FontSize(9).FontColor(TextPrimary));

                page.Header().Column(header =>
                {
                    header.Item().Row(row =>
                    {
                        row.RelativeItem().Column(c =>
                        {
                            c.Item().Text(_options.Reports.CompanyName).FontSize(9).FontColor(TextSecondary);
                            c.Item().Text(title).FontSize(18).Bold();
                            c.Item().Text(subtitle).FontSize(11);
                        });
                        row.AutoItem().AlignRight().Column(c =>
                        {
                            c.Item().AlignRight().Text(Environment.MachineName).FontSize(11).Bold();
                            c.Item().AlignRight().Text("Generated " + DateTime.Now.ToString("g")).FontSize(8).FontColor(TextSecondary);
                            c.Item().AlignRight().Text(data.Coverage).FontSize(8).FontColor(TextSecondary);
                        });
                    });
                    header.Item().PaddingTop(6).LineHorizontal(1).LineColor(Rule);
                });

                page.Content().PaddingTop(10).Column(col =>
                {
                    col.Spacing(10);
                    col.Item().Element(c => ComposeHighlights(c, data.Summary));
                    foreach (var png in charts)
                        col.Item().Image(png).FitWidth();

                    col.Item().PageBreak();
                    col.Item().Text("Summary of all readings").FontSize(12).Bold();
                    col.Item().Text(data.Period == PeriodType.Day
                            ? "Min / average / max / 95th percentile of every reading taken during the day."
                            : "Built from the daily summaries. P95 is the reading-weighted average of the daily 95th percentiles (an approximation).")
                        .FontSize(8).FontColor(TextSecondary);
                    col.Item().Element(c => ComposeTable(c, data.Summary.Where(r => r.Key.Metric != MetricNames.CpuCore)));

                    var cores = data.Summary.Where(r => r.Key.Metric == MetricNames.CpuCore).ToList();
                    if (cores.Count > 0)
                    {
                        col.Item().PaddingTop(6).Text("CPU by logical processor").FontSize(12).Bold();
                        col.Item().Element(c => ComposeTable(c, cores));
                    }
                });

                page.Footer().AlignCenter().Text(t =>
                {
                    t.DefaultTextStyle(s => s.FontSize(8).FontColor(TextSecondary));
                    t.Span("VailITMonitor  ·  Page ");
                    t.CurrentPageNumber();
                    t.Span(" of ");
                    t.TotalPages();
                });
            })).GeneratePdf(path);
        }

        private static void ComposeHighlights(IContainer container, List<RollupRow> summary)
        {
            Func<string, string, RollupRow> find = (metric, instance) =>
                summary.FirstOrDefault(r => r.Key == new MetricKey(metric, instance));

            var tiles = new List<Tuple<string, string, string>>();
            var cpu = find(MetricNames.CpuTotal, "");
            if (cpu != null)
            {
                tiles.Add(Tuple.Create("Average CPU", cpu.Avg.ToString("N1") + "%", "95th percentile " + cpu.P95.ToString("N1") + "%"));
                tiles.Add(Tuple.Create("Peak CPU", cpu.Max.ToString("N1") + "%", "highest single reading"));
            }

            var mem = find(MetricNames.MemUsedPct, "");
            if (mem != null)
                tiles.Add(Tuple.Create("Average memory used", mem.Avg.ToString("N1") + "%", "peak " + mem.Max.ToString("N1") + "%"));

            var lowestDisk = summary.Where(r => r.Key.Metric == MetricNames.DiskFreePct).OrderBy(r => r.Min).FirstOrDefault();
            if (lowestDisk != null)
            {
                var freeGb = find(MetricNames.DiskFreeGb, lowestDisk.Key.Instance);
                tiles.Add(Tuple.Create("Lowest free space (" + lowestDisk.Key.Instance + ")", lowestDisk.Min.ToString("N1") + "%",
                    freeGb == null ? "" : freeGb.Min.ToString("N1") + " GB free"));
            }

            var self = find(MetricNames.SelfMemMb, "");
            if (self != null)
            {
                var selfCpu = find(MetricNames.SelfCpuPct, "");
                tiles.Add(Tuple.Create("Monitor's own memory", self.Avg.ToString("N0") + " MB",
                    selfCpu != null ? "avg CPU " + selfCpu.Avg.ToString("N2") + "%" : ""));
            }

            container.Row(row =>
            {
                row.Spacing(8);
                foreach (var tile in tiles)
                {
                    row.RelativeItem().Background(Shade).Padding(8).Column(c =>
                    {
                        c.Item().Text(tile.Item1).FontSize(8).FontColor(TextSecondary);
                        c.Item().Text(tile.Item2).FontSize(16).Bold();
                        c.Item().Text(tile.Item3).FontSize(7).FontColor(TextSecondary);
                    });
                }
            });
        }

        private static void ComposeTable(IContainer container, IEnumerable<RollupRow> rows)
        {
            var ordered = rows
                .OrderBy(r => MetricNames.SortOrder(r.Key.Metric))
                .ThenBy(r => r.Key.Instance == MetricNames.Total ? "" : r.Key.Instance, StringComparer.OrdinalIgnoreCase)
                .ThenBy(r => { int n; return int.TryParse(r.Key.Instance, out n) ? n : 0; })
                .ToList();

            container.Table(table =>
            {
                table.ColumnsDefinition(c =>
                {
                    c.RelativeColumn(4);
                    c.RelativeColumn(1.2f);
                    for (int i = 0; i < 4; i++) c.RelativeColumn(1.4f);
                });

                table.Header(h =>
                {
                    foreach (var heading in new[] { "Metric", "Unit", "Min", "Average", "Max", "P95" })
                    {
                        var cell = h.Cell().BorderBottom(1).BorderColor(TextSecondary).PaddingVertical(3);
                        if (heading == "Metric" || heading == "Unit") cell.Text(heading).Bold();
                        else cell.AlignRight().Text(heading).Bold();
                    }
                });

                foreach (var r in ordered)
                {
                    string unit = MetricNames.Unit(r.Key.Metric);
                    table.Cell().Element(Body).Text(MetricNames.Label(r.Key));
                    table.Cell().Element(Body).Text(unit).FontColor(TextSecondary);
                    table.Cell().Element(Body).AlignRight().Text(Format(r.Min, unit));
                    table.Cell().Element(Body).AlignRight().Text(Format(r.Avg, unit));
                    table.Cell().Element(Body).AlignRight().Text(Format(r.Max, unit));
                    table.Cell().Element(Body).AlignRight().Text(Format(r.P95, unit));
                }
            });
        }

        private static IContainer Body(IContainer c)
        {
            return c.BorderBottom(0.5f).BorderColor(Rule).PaddingVertical(2.5f);
        }

        private static string Format(double value, string unit)
        {
            if (unit == "") return value.ToString(value >= 100 ? "N0" : "N1", CultureInfo.CurrentCulture);
            if (unit == "%") return value.ToString("N1", CultureInfo.CurrentCulture);
            return value.ToString(value >= 1000 ? "N0" : "N1", CultureInfo.CurrentCulture);
        }
    }
}
