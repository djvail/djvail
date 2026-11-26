using ScottPlot;

namespace ValiITMonitor.Reporting;

/// <summary>One line on a chart.</summary>
public sealed record ChartSeries(string Label, DateTime[] Times, double[] Values, int ColorSlot);

/// <summary>Everything needed to draw one chart.</summary>
/// <param name="TimeFormat">How dates on the bottom axis are written, e.g. "HH:mm" or "MMM d".</param>
/// <param name="LabelPoints">true = put date labels on the data points themselves (about 12 at most); false = let ScottPlot choose.</param>
public sealed record ChartSpec(string Title, string Unit, IReadOnlyList<ChartSeries> Series, double? YMax = null, string TimeFormat = "MMM d", bool LabelPoints = false);

/// <summary>
/// Draws line charts as PNG images using ScottPlot (https://scottplot.net).
/// Change colours, sizes or styles here and every report picks them up.
/// </summary>
public static class ChartRenderer
{
    // Colour-blind-safe categorical palette, used in this fixed order.
    private static readonly string[] Palette =
        ["#257de0", "#f47230", "#00b894", "#6a4ce2", "#ee7fac", "#008a17", "#f2a600", "#ec4647"];

    private const string TextPrimary = "#191919";
    private const string TextSecondary = "#666666";
    private const string GridColor = "#e8e8e8";

    // Rendered at 2x the size it is shown in the PDF, so it stays sharp when printed.
    public const int Width = 1600;
    public const int Height = 520;

    public static byte[] RenderPng(ChartSpec spec)
    {
        using var plot = new Plot();
        plot.ScaleFactor = 2;

        foreach (var series in spec.Series.Where(s => s.Values.Length > 0))
        {
            double[] xs = series.Times.Select(t => t.ToOADate()).ToArray();
            var line = plot.Add.Scatter(xs, series.Values);
            line.LegendText = series.Label;
            line.Color = Color.FromHex(Palette[series.ColorSlot % Palette.Length]);
            line.LineWidth = 1.5f;
            line.MarkerSize = xs.Length <= 62 ? 4 : 0;   // show dots only when there are few points
        }

        if (spec.LabelPoints)
        {
            // Date labels sit exactly on the data points: every point, or every Nth so ~12 fit.
            var days = spec.Series.SelectMany(s => s.Times).Distinct().Order().ToList();
            int step = (int)Math.Ceiling(days.Count / 12.0);
            var tickDays = days.Where((_, i) => i % Math.Max(1, step) == 0).ToList();
            plot.Axes.Bottom.SetTicks(
                tickDays.Select(d => d.ToOADate()).ToArray(),
                tickDays.Select(d => d.ToString(spec.TimeFormat)).ToArray());
        }
        else
        {
            var timeAxis = plot.Axes.DateTimeTicksBottom();
            if (timeAxis.TickGenerator is ScottPlot.TickGenerators.DateTimeAutomatic ticks)
                ticks.LabelFormatter = d => d.ToString(spec.TimeFormat);
        }

        plot.Title(spec.Title);
        plot.Axes.Title.Label.FontSize = 15;
        plot.Axes.Title.Label.ForeColor = Color.FromHex(TextPrimary);
        plot.YLabel(spec.Unit);
        plot.Axes.Left.Label.FontSize = 12;
        plot.Axes.Left.Label.ForeColor = Color.FromHex(TextSecondary);

        // Quiet axes and grid so the data stands out.
        plot.Axes.Color(Color.FromHex(TextSecondary));
        plot.Axes.Frame(false);
        plot.Axes.Bottom.FrameLineStyle.IsVisible = true;
        plot.Grid.MajorLineColor = Color.FromHex(GridColor);

        plot.Axes.AutoScale();
        var limits = plot.Axes.GetLimits();
        double top = spec.YMax ?? Math.Max(1, limits.Top * 1.1);
        plot.Axes.SetLimitsY(0, top);

        if (spec.Series.Count > 1)
        {
            plot.ShowLegend(Edge.Bottom);
            plot.Legend.FontSize = 12;
            plot.Legend.OutlineWidth = 0;
        }
        else
        {
            plot.HideLegend();   // one line: the chart title already says what it is
        }

        return plot.GetImageBytes(Width, Height, ImageFormat.Png);
    }
}
