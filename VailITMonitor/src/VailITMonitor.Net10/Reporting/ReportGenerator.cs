using System.Globalization;
using Microsoft.Extensions.Options;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace VailITMonitor.Reporting;

/// <summary>
/// Builds the PDF reports with QuestPDF (https://www.questpdf.com).
///
/// Daily reports chart every raw reading of the day.
/// Weekly/monthly/quarterly/yearly reports chart one point per day (the daily summaries),
/// so they keep working long after the raw readings have been cleaned up.
/// </summary>
public sealed class ReportGenerator
{
    private const string TextPrimary = "#191919";
    private const string TextSecondary = "#666666";
    private const string Rule = "#dddddd";
    private const string Shade = "#f5f5f5";

    private readonly MetricStore _store;
    private readonly RollupService _rollups;
    private readonly MonitorOptions _options;

    static ReportGenerator()
    {
        // QuestPDF is free under its Community licence for individuals and companies
        // with less than USD 1M annual revenue. See https://www.questpdf.com/license/
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public ReportGenerator(MetricStore store, RollupService rollups, IOptions<MonitorOptions> options)
    {
        _store = store;
        _rollups = rollups;
        _options = options.Value;
    }

    /// <summary>Creates the report and returns its path, or null if there is no data for the period.</summary>
    public string? Generate(PeriodType period, DateTime start, string? folder = null)
    {
        var end = Periods.Next(period, start);
        var data = period == PeriodType.Day ? BuildDaily(start, end) : BuildPeriod(period, start, end);
        if (data is null) return null;

        folder ??= Path.Combine(_options.ResolvedReportFolder, Periods.Adjective(period));
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, $"VailITMonitor_{Environment.MachineName}_{Periods.Adjective(period)}_{Periods.FileStem(period, start)}.pdf");

        Render(data, path);
        return path;
    }

    // ================================================================ gather the data

    private sealed class ReportData
    {
        public required PeriodType Period { get; init; }        // "required" (C# 11): must be set when created
        public required DateTime Start { get; init; }
        public required List<RollupRow> Summary { get; init; }
        public List<ChartSpec> Charts { get; } = [];
        public string Coverage { get; set; } = "";
    }

    private ReportData? BuildDaily(DateTime start, DateTime end)
    {
        var samples = _store.GetSamples(Periods.ToUnix(start), Periods.ToUnix(end));
        if (samples.Count == 0) return null;

        var summary = _store.GetRollups(PeriodType.Day, start, end).Values.FirstOrDefault()
                      ?? _rollups.SummariseRaw(start, end);   // today / not summarised yet

        var data = new ReportData { Period = PeriodType.Day, Start = start, Summary = summary };

        // Local helper ("local function") that turns stored readings into one chart line.
        ChartSeries? Line(string metric, string instance, string label, int slot)
        {
            if (!samples.TryGetValue(new MetricKey(metric, instance), out var points)) return null;
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

    private ReportData? BuildPeriod(PeriodType period, DateTime start, DateTime end)
    {
        // Chart points: the finest summaries still available for the period (older ones get cleaned up).
        // The yearly report prefers weekly points because 365 daily points are hard to read.
        PeriodType[] candidates = period switch
        {
            PeriodType.Year => [PeriodType.Week, PeriodType.Day, PeriodType.Month],
            PeriodType.Quarter => [PeriodType.Day, PeriodType.Week, PeriodType.Month],
            _ => [PeriodType.Day, PeriodType.Week],
        };

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

        var summary = _store.GetRollups(period, start, end).GetValueOrDefault(start)
                      ?? _rollups.SummariseDays(start, end);
        if (summary.Count == 0)
            summary = points.Values.SelectMany(r => r).GroupBy(r => r.Key).Select(g => Stats.Combine(g.Key, g.ToList())).ToList();

        var data = new ReportData { Period = period, Start = start, Summary = summary };
        var keys = points.Values.SelectMany(r => r).Select(r => r.Key).ToHashSet();

        ChartSeries? Line(string metric, string instance, string label, int slot, Func<RollupRow, double> pick)
        {
            var key = new MetricKey(metric, instance);
            var found = points
                .Select(p => (Day: p.Key, Row: p.Value.FirstOrDefault(r => r.Key == key)))
                .Where(p => p.Row is not null)
                .ToList();
            if (found.Count == 0) return null;
            return new ChartSeries(label, found.Select(f => f.Day).ToArray(), found.Select(f => pick(f.Row!)).ToArray(), slot);
        }

        AddChart(data, $"CPU usage ({each})", "%", 100,
            Line(MetricNames.CpuTotal, "", "Average", 0, r => r.Avg),
            Line(MetricNames.CpuTotal, "", "95th percentile", 1, r => r.P95),
            Line(MetricNames.CpuTotal, "", "Peak", 2, r => r.Max));
        AddChart(data, $"Memory used ({each})", "%", 100,
            Line(MetricNames.MemUsedPct, "", "Average", 0, r => r.Avg),
            Line(MetricNames.MemUsedPct, "", "Peak", 2, r => r.Max));
        AddChart(data, $"Free disk space (lowest each {eachNoun})", "% free", 100,
            Instances(keys, MetricNames.DiskFreePct)
                .Select((drive, i) => Line(MetricNames.DiskFreePct, drive, drive, i, r => r.Min)).ToArray());
        AddChart(data, $"Disk activity ({each} average)", "KB/s", null,
            Line(MetricNames.DiskReadKBps, "", "Read", 0, r => r.Avg),
            Line(MetricNames.DiskWriteKBps, "", "Write", 1, r => r.Avg));
        AddChart(data, $"Network ({each} average, all adapters)", "KB/s", null,
            Line(MetricNames.NetRecvKBps, MetricNames.Total, "Received", 0, r => r.Avg),
            Line(MetricNames.NetSentKBps, MetricNames.Total, "Sent", 1, r => r.Avg));

        data.Coverage = CoverageText(summary, (end - start).TotalSeconds);
        return data;
    }

    /// <summary>
    /// Loads the chart points for a period. A week that straddles the start or end of the period
    /// (e.g. Dec 30 - Jan 5) belongs to whichever side holds most of it (its Thursday).
    /// </summary>
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

    // "params" with a nullable element type lets callers pass helpers that might return null.
    private static void AddChart(ReportData data, string title, string unit, double? yMax, params ChartSeries?[] series)
    {
        var present = series.OfType<ChartSeries>().ToList();   // drops the nulls
        // Tuple switch: picks the date format and label style for the bottom axis in one go.
        var (timeFormat, labelPoints) = data.Period switch
        {
            PeriodType.Day => ("HH:mm", false),
            PeriodType.Week => ("ddd d", true),
            _ => ("MMM d", true),
        };
        if (present.Count > 0) data.Charts.Add(new ChartSpec(title, unit, present, yMax, timeFormat, labelPoints));
    }

    private static IEnumerable<string> Instances(IEnumerable<MetricKey> keys, string metric)
        => keys.Where(k => k.Metric == metric).Select(k => k.Instance).Distinct().Order();

    private string CoverageText(List<RollupRow> summary, double periodSeconds)
    {
        var cpu = summary.FirstOrDefault(r => r.Key.Metric == MetricNames.CpuTotal) ?? summary.MaxBy(r => r.Count);
        if (cpu is null) return "";
        double expected = periodSeconds / _options.SampleInterval.TotalSeconds;
        double pct = Math.Min(100, 100 * cpu.Count / expected);
        return $"{cpu.Count:N0} readings ({pct:N0}% of the period)";
    }

    // ================================================================ draw the PDF

    private void Render(ReportData data, string path)
    {
        var pageSize = _options.Reports.PageSize.Equals("A4", StringComparison.OrdinalIgnoreCase) ? PageSizes.A4 : PageSizes.Letter;
        var charts = data.Charts.Select(ChartRenderer.RenderPng).ToList();   // method group as a delegate
        string title = $"{Periods.Adjective(data.Period)} System Report";
        string subtitle = data.Period == PeriodType.Day
            ? Periods.Label(data.Period, data.Start)
            : $"{Periods.Label(data.Period, data.Start)}  ({data.Start:MMM d, yyyy} - {Periods.Next(data.Period, data.Start).AddDays(-1):MMM d, yyyy})";

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
                        c.Item().AlignRight().Text($"Generated {DateTime.Now:g}").FontSize(8).FontColor(TextSecondary);
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
                        : "Built from the daily summaries. P95 is the reading-weighted average of the daily 95th percentiles.")
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

    /// <summary>The row of headline numbers at the top of the first page.</summary>
    private static void ComposeHighlights(IContainer container, List<RollupRow> summary)
    {
        RollupRow? Find(string metric, string instance = "") => summary.FirstOrDefault(r => r.Key == new MetricKey(metric, instance));

        var tiles = new List<(string Label, string Value, string Note)>();
        if (Find(MetricNames.CpuTotal) is { } cpu)   // property pattern: "is not null, call it cpu"
        {
            tiles.Add(("Average CPU", $"{cpu.Avg:N1}%", $"95th percentile {cpu.P95:N1}%"));
            tiles.Add(("Peak CPU", $"{cpu.Max:N1}%", "highest single reading"));
        }
        if (Find(MetricNames.MemUsedPct) is { } mem)
            tiles.Add(("Average memory used", $"{mem.Avg:N1}%", $"peak {mem.Max:N1}%"));

        var lowestDisk = summary.Where(r => r.Key.Metric == MetricNames.DiskFreePct).MinBy(r => r.Min);
        if (lowestDisk is not null)
        {
            var freeGb = Find(MetricNames.DiskFreeGb, lowestDisk.Key.Instance);
            tiles.Add(($"Lowest free space ({lowestDisk.Key.Instance})", $"{lowestDisk.Min:N1}%",
                freeGb is null ? "" : $"{freeGb.Min:N1} GB free"));
        }

        if (Find(MetricNames.SelfMemMb) is { } self)
            tiles.Add(("Monitor's own memory", $"{self.Avg:N0} MB",
                Find(MetricNames.SelfCpuPct) is { } selfCpu ? $"avg CPU {selfCpu.Avg:N2}%" : ""));

        container.Row(row =>
        {
            row.Spacing(8);
            foreach (var (label, value, note) in tiles)   // deconstructing a tuple in foreach
            {
                row.RelativeItem().Background(Shade).Padding(8).Column(c =>
                {
                    c.Item().Text(label).FontSize(8).FontColor(TextSecondary);
                    c.Item().Text(value).FontSize(16).Bold();
                    c.Item().Text(note).FontSize(7).FontColor(TextSecondary);
                });
            }
        });
    }

    private static void ComposeTable(IContainer container, IEnumerable<RollupRow> rows)
    {
        var ordered = rows
            .OrderBy(r => MetricNames.SortOrder(r.Key.Metric))
            .ThenBy(r => r.Key.Instance == MetricNames.Total ? "" : r.Key.Instance, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => int.TryParse(r.Key.Instance, out var n) ? n : 0)
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
                    if (heading is "Metric" or "Unit") cell.Text(heading).Bold();
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

        static IContainer Body(IContainer c) => c.BorderBottom(0.5f).BorderColor(Rule).PaddingVertical(2.5f);
    }

    private static string Format(double value, string unit) => unit switch
    {
        "" => value.ToString(value >= 100 ? "N0" : "N1", CultureInfo.CurrentCulture),
        "%" => value.ToString("N1", CultureInfo.CurrentCulture),
        _ => value.ToString(value >= 1000 ? "N0" : "N1", CultureInfo.CurrentCulture),
    };
}
