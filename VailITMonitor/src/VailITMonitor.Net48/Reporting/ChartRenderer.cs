using System;
using System.Collections.Generic;
using System.Linq;
using ScottPlot;

namespace VailITMonitor.Reporting
{
    /// <summary>One line on a chart.</summary>
    public sealed class ChartSeries
    {
        public ChartSeries(string label, DateTime[] times, double[] values, int colorSlot)
        {
            Label = label;
            Times = times;
            Values = values;
            ColorSlot = colorSlot;
        }

        public string Label { get; }
        public DateTime[] Times { get; }
        public double[] Values { get; }
        public int ColorSlot { get; }
    }

    /// <summary>Everything needed to draw one chart.</summary>
    public sealed class ChartSpec
    {
        public ChartSpec(string title, string unit, IList<ChartSeries> series, double? yMax, string timeFormat, bool labelPoints)
        {
            Title = title;
            Unit = unit;
            Series = series;
            YMax = yMax;
            TimeFormat = timeFormat;
            LabelPoints = labelPoints;
        }

        public string Title { get; }
        public string Unit { get; }
        public IList<ChartSeries> Series { get; }
        public double? YMax { get; }
        public string TimeFormat { get; }
        public bool LabelPoints { get; }
    }

    /// <summary>Draws line charts as PNG images using ScottPlot (https://scottplot.net).</summary>
    public static class ChartRenderer
    {
        private static readonly string[] Palette =
            { "#257de0", "#f47230", "#00b894", "#6a4ce2", "#ee7fac", "#008a17", "#f2a600", "#ec4647" };

        private const string TextPrimary = "#191919";
        private const string TextSecondary = "#666666";
        private const string GridColor = "#e8e8e8";

        public const int Width = 1600;
        public const int Height = 520;

        public static byte[] RenderPng(ChartSpec spec)
        {
            using (var plot = new Plot())
            {
                plot.ScaleFactor = 2;

                foreach (var series in spec.Series.Where(s => s.Values.Length > 0))
                {
                    double[] xs = series.Times.Select(t => t.ToOADate()).ToArray();
                    var line = plot.Add.Scatter(xs, series.Values);
                    line.LegendText = series.Label;
                    line.Color = Color.FromHex(Palette[series.ColorSlot % Palette.Length]);
                    line.LineWidth = 1.5f;
                    line.MarkerSize = xs.Length <= 62 ? 4 : 0;
                }

                if (spec.LabelPoints)
                {
                    var days = spec.Series.SelectMany(s => s.Times).Distinct().OrderBy(d => d).ToList();
                    int step = Math.Max(1, (int)Math.Ceiling(days.Count / 12.0));
                    var tickDays = days.Where((d, i) => i % step == 0).ToList();
                    plot.Axes.Bottom.SetTicks(
                        tickDays.Select(d => d.ToOADate()).ToArray(),
                        tickDays.Select(d => d.ToString(spec.TimeFormat)).ToArray());
                }
                else
                {
                    var timeAxis = plot.Axes.DateTimeTicksBottom();
                    var ticks = timeAxis.TickGenerator as ScottPlot.TickGenerators.DateTimeAutomatic;
                    if (ticks != null)
                        ticks.LabelFormatter = d => d.ToString(spec.TimeFormat);
                }

                plot.Title(spec.Title);
                plot.Axes.Title.Label.FontSize = 15;
                plot.Axes.Title.Label.ForeColor = Color.FromHex(TextPrimary);
                plot.YLabel(spec.Unit);
                plot.Axes.Left.Label.FontSize = 12;
                plot.Axes.Left.Label.ForeColor = Color.FromHex(TextSecondary);

                plot.Axes.Color(Color.FromHex(TextSecondary));
                plot.Axes.Frame(false);
                plot.Axes.Bottom.FrameLineStyle.IsVisible = true;
                plot.Grid.MajorLineColor = Color.FromHex(GridColor);

                plot.Axes.AutoScale();
                var limits = plot.Axes.GetLimits();
                double top = spec.YMax.HasValue ? spec.YMax.Value : Math.Max(1, limits.Top * 1.1);
                plot.Axes.SetLimitsY(0, top);

                if (spec.Series.Count > 1)
                {
                    plot.ShowLegend(Edge.Bottom);
                    plot.Legend.FontSize = 12;
                    plot.Legend.OutlineWidth = 0;
                }
                else
                {
                    plot.HideLegend();
                }

                return plot.GetImageBytes(Width, Height, ImageFormat.Png);
            }
        }
    }
}
