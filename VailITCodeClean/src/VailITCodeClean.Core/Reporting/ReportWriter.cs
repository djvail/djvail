using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using VailIT.CodeClean.Configuration;

namespace VailIT.CodeClean.Reporting;

/// <summary>Writes report.html, findings.csv, findings.json, files.csv and (optionally) mapping.json.</summary>
public static class ReportWriter
{
    public static void WriteAll(RunResult result, ScanOptions options, IProgress<string>? log = null)
    {
        Directory.CreateDirectory(result.ReportFolder);

        if (options.WriteHtmlReport) Write(result, "report.html", HtmlReport.Build(result), log);
        if (options.WriteCsvReport)
        {
            Write(result, "findings.csv", FindingsCsv(result), log);
            Write(result, "files.csv", FilesCsv(result), log);
        }
        if (options.WriteJsonReport) Write(result, "findings.json", JsonSerializer.Serialize(result, ProfileStore.JsonOptions), log);
        if (options.WriteMappingFile && options.Style != ObfuscationStyle.Mask && result.Mappings.Count > 0)
        {
            var mapping = new Engine.MappingFile
            {
                Warning = "THIS FILE CONTAINS THE ORIGINAL SECRET VALUES. Never share it or upload it anywhere.",
                Source = result.SourceFolder,
                Created = result.StartedLocal,
                PreserveLineNumbers = options.PreserveLineNumbers,
                Entries = result.Mappings,
            };
            Write(result, "mapping.json", JsonSerializer.Serialize(mapping, ProfileStore.JsonOptions), log);
        }
    }

    private static void Write(RunResult result, string name, string content, IProgress<string>? log)
    {
        var path = Path.Combine(result.ReportFolder, name);
        // CSV gets a byte-order mark so Excel opens it as UTF-8; JSON and HTML do not need one.
        File.WriteAllText(path, content, new UTF8Encoding(name.EndsWith(".csv", StringComparison.OrdinalIgnoreCase)));
        result.ReportFiles.Add(path);
        log?.Report($"Wrote {path}");
    }

    public static string FindingsCsv(RunResult r)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Severity,Category,Rule,RuleId,File,Line,Column,EndLine,OutputLine,Token,Preview,Original,Context,AlsoMatched");
        foreach (var f in Sorted(r.Findings))
        {
            sb.AppendJoin(',', [
                f.Severity.ToString(), Csv(f.Category), Csv(f.RuleName), Csv(f.RuleId), Csv(f.File),
                f.Line.ToString(CultureInfo.InvariantCulture), f.Column.ToString(CultureInfo.InvariantCulture),
                f.EndLine.ToString(CultureInfo.InvariantCulture), f.OutputLine.ToString(CultureInfo.InvariantCulture),
                Csv(f.Token), Csv(f.Preview), Csv(f.Original ?? ""), Csv(f.Context), Csv(string.Join(' ', f.AlsoMatched)),
            ]);
            sb.AppendLine();
        }
        return sb.ToString();
    }

    public static string FilesCsv(RunResult r)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Status,File,OutputFile,SizeBytes,Findings,Encoding,Message");
        foreach (var f in r.Files)
        {
            sb.AppendJoin(',', [
                f.Status.ToString(), Csv(f.Path), Csv(f.OutputPath), f.SizeBytes.ToString(CultureInfo.InvariantCulture),
                f.Findings.ToString(CultureInfo.InvariantCulture), Csv(f.Encoding), Csv(f.Message),
            ]);
            sb.AppendLine();
        }
        return sb.ToString();
    }

    internal static IEnumerable<Finding> Sorted(IEnumerable<Finding> findings) =>
        findings.OrderByDescending(f => f.Severity)
            .ThenBy(f => f.File, StringComparer.OrdinalIgnoreCase)
            .ThenBy(f => f.Line);

    /// <summary>CSV-escapes a value. A leading = + - @ is prefixed with ' so Excel does not treat it as a formula.</summary>
    private static string Csv(string value)
    {
        if (value.Length > 0 && "=+-@\t\r".Contains(value[0])) value = "'" + value;
        value = value.Replace("\r", " ").Replace("\n", " ");
        return value.Contains(',') || value.Contains('"') ? "\"" + value.Replace("\"", "\"\"") + "\"" : value;
    }

    internal static string Html(string? s) => WebUtility.HtmlEncode(s ?? "");
}
