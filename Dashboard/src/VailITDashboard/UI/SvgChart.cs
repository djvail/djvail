using System.Globalization;
using System.Net;
using System.Text;
using Microsoft.AspNetCore.Html;

namespace VailITDashboard;

/// <summary>
/// Draws a simple line chart as inline SVG on the server, so the dashboard needs no JavaScript chart
/// library and works with no internet access. Colours come from CSS (.series-0, .series-1, ...).
/// </summary>
public static class SvgChart
{
    public sealed record Series(string Name, IReadOnlyList<ChartPoint> Points);

    private const int Width = 640, Height = 200, Left = 44, Right = 10, Top = 10, Bottom = 24;

    public static IHtmlContent Render(
        IReadOnlyList<Series> series, long fromTs, long toTs, string unit,
        double? warning = null, double? critical = null, double? fixedMax = null)
    {
        var ci = CultureInfo.InvariantCulture;
        var all = series.SelectMany(s => s.Points).ToList();

        double max = fixedMax ?? Math.Max(1, NiceCeiling(Math.Max(all.Count == 0 ? 1 : all.Max(p => p.Value), Math.Max(warning ?? 0, critical ?? 0))));
        double plotW = Width - Left - Right, plotH = Height - Top - Bottom;
        double X(long ts) => Left + (ts - fromTs) / (double)Math.Max(1, toTs - fromTs) * plotW;
        double Y(double v) => Top + plotH - Math.Clamp(v / max, 0, 1) * plotH;

        var sb = new StringBuilder();
        sb.Append(ci, $"<svg class=\"chart\" viewBox=\"0 0 {Width} {Height}\" role=\"img\" preserveAspectRatio=\"none\">");

        // Grid lines and y labels at 0, 25, 50, 75, 100 % of the range.
        for (int i = 0; i <= 4; i++)
        {
            double v = max * i / 4, y = Y(v);
            sb.Append(ci, $"<line class=\"grid\" x1=\"{Left}\" x2=\"{Width - Right}\" y1=\"{y:0.#}\" y2=\"{y:0.#}\"/>");
            sb.Append(ci, $"<text class=\"axis\" x=\"{Left - 6}\" y=\"{y + 4:0.#}\" text-anchor=\"end\">{Format.Value(v, "")}</text>");
        }

        // Time labels: 5 evenly spaced.
        for (int i = 0; i <= 4; i++)
        {
            long ts = fromTs + (toTs - fromTs) * i / 4;
            var local = DateTimeOffset.FromUnixTimeSeconds(ts).ToLocalTime();
            string label = toTs - fromTs > 2 * 86400 ? local.ToString("MMM d", ci) : local.ToString("HH:mm", ci);
            string anchor = i == 0 ? "start" : i == 4 ? "end" : "middle";
            sb.Append(ci, $"<text class=\"axis\" x=\"{X(ts):0.#}\" y=\"{Height - 6}\" text-anchor=\"{anchor}\">{label}</text>");
        }

        if (warning is double w) sb.Append(ci, $"<line class=\"limit warning\" x1=\"{Left}\" x2=\"{Width - Right}\" y1=\"{Y(w):0.#}\" y2=\"{Y(w):0.#}\"><title>Warning {Format.Value(w, unit)}</title></line>");
        if (critical is double c) sb.Append(ci, $"<line class=\"limit critical\" x1=\"{Left}\" x2=\"{Width - Right}\" y1=\"{Y(c):0.#}\" y2=\"{Y(c):0.#}\"><title>Critical {Format.Value(c, unit)}</title></line>");

        for (int s = 0; s < series.Count; s++)
        {
            var pts = series[s].Points;
            if (pts.Count == 0) continue;
            sb.Append(ci, $"<polyline class=\"line series-{s % 6}\" points=\"");
            foreach (var p in pts) sb.Append(ci, $"{X(p.Ts):0.#},{Y(p.Value):0.#} ");
            var last = pts[^1];
            sb.Append(ci, $"\"><title>{WebUtility.HtmlEncode(series[s].Name)}: latest {Format.Value(last.Value, unit)}</title></polyline>");
        }

        if (all.Count == 0)
            sb.Append(ci, $"<text class=\"axis\" x=\"{Width / 2}\" y=\"{Height / 2}\" text-anchor=\"middle\">No readings in this period</text>");

        sb.Append("</svg>");
        return new HtmlString(sb.ToString());
    }

    private static double NiceCeiling(double v)
    {
        if (v <= 0) return 1;
        double mag = Math.Pow(10, Math.Floor(Math.Log10(v)));
        foreach (var step in new[] { 1, 2, 2.5, 5, 10 })
            if (step * mag >= v) return step * mag;
        return 10 * mag;
    }
}
