using System.Text;
using VailIT.CodeClean.Configuration;
using static VailIT.CodeClean.Reporting.ReportWriter;

namespace VailIT.CodeClean.Reporting;

/// <summary>Builds the single-file HTML report (no external files or internet access needed to view it).</summary>
public static class HtmlReport
{
    public static string Build(RunResult r)
    {
        var sb = new StringBuilder();
        sb.Append($$"""
            <!DOCTYPE html>
            <html lang="en">
            <head>
            <meta charset="utf-8">
            <meta name="viewport" content="width=device-width, initial-scale=1">
            <title>VailIT Code Clean report - {{Html(Path.GetFileName(r.SourceFolder))}}</title>
            <style>
            :root { --bg:#f6f7f9; --card:#fff; --text:#1d2330; --muted:#5d6778; --line:#dfe3ea; --accent:#1f6feb;
                    --crit:#b42318; --high:#d9480f; --med:#b08800; --low:#1971c2; --info:#5d6778; }
            @media (prefers-color-scheme: dark) {
              :root { --bg:#0f1218; --card:#171b23; --text:#e6e9ef; --muted:#9aa4b5; --line:#2a303b; --accent:#5b9bff;
                      --crit:#ff6b6b; --high:#ff922b; --med:#ffd43b; --low:#74c0fc; --info:#adb5bd; }
            }
            * { box-sizing:border-box; }
            body { margin:0; font:14px/1.45 "Segoe UI", system-ui, -apple-system, sans-serif; background:var(--bg); color:var(--text); }
            header { padding:24px 32px 8px; }
            h1 { margin:0 0 4px; font-size:22px; }
            h2 { font-size:17px; margin:28px 0 10px; }
            .muted { color:var(--muted); }
            main { padding:0 32px 40px; }
            .cards { display:grid; grid-template-columns:repeat(auto-fit,minmax(140px,1fr)); gap:12px; margin-top:16px; }
            .card { background:var(--card); border:1px solid var(--line); border-radius:8px; padding:12px 14px; }
            .card .n { font-size:24px; font-weight:600; }
            .card .l { color:var(--muted); font-size:12px; text-transform:uppercase; letter-spacing:.04em; }
            table { width:100%; border-collapse:collapse; background:var(--card); border:1px solid var(--line); border-radius:8px; overflow:hidden; }
            th, td { text-align:left; padding:6px 10px; border-bottom:1px solid var(--line); vertical-align:top; }
            th { font-size:12px; color:var(--muted); text-transform:uppercase; letter-spacing:.04em; cursor:pointer; user-select:none; }
            tr:last-child td { border-bottom:none; }
            td.num, th.num { text-align:right; font-variant-numeric:tabular-nums; }
            code, .mono { font-family:Consolas, "Cascadia Mono", ui-monospace, monospace; font-size:12.5px; word-break:break-all; }
            .sev { display:inline-block; padding:1px 8px; border-radius:10px; font-size:12px; font-weight:600; color:#fff; }
            .Critical { background:var(--crit); } .High { background:var(--high); } .Medium { background:var(--med); color:#000; }
            .Low { background:var(--low); color:#000; } .Info { background:var(--info); }
            .warn { background:var(--card); border-left:4px solid var(--high); padding:10px 14px; border-radius:4px; margin:8px 0; }
            .filters { display:flex; flex-wrap:wrap; gap:8px; margin-bottom:10px; }
            input, select { font:inherit; padding:6px 8px; border:1px solid var(--line); border-radius:6px; background:var(--card); color:var(--text); }
            input { flex:1; min-width:220px; }
            .scroll { overflow-x:auto; }
            details { background:var(--card); border:1px solid var(--line); border-radius:8px; padding:10px 14px; }
            summary { cursor:pointer; font-weight:600; }
            footer { padding:16px 32px; color:var(--muted); font-size:12px; }
            @media (max-width:700px) { header, main, footer { padding-left:16px; padding-right:16px; } }
            </style>
            </head>
            <body>
            <header>
              <h1>VailIT Code Clean report</h1>
              <div class="muted">{{Html(r.SourceFolder)}} &middot; {{r.StartedLocal:yyyy-MM-dd HH:mm:ss}} &middot; profile <b>{{Html(r.ProfileName)}}</b> &middot; {{r.RulesUsed}} rules &middot; {{r.Duration.TotalSeconds:N1}} s{{(r.Cancelled ? " &middot; <b>CANCELLED - results are incomplete</b>" : "")}}</div>
            </header>
            <main>
            """);

        // ---- summary cards ----
        sb.Append("<div class=\"cards\">");
        Card(sb, r.FindingCount.ToString("N0"), "Findings");
        Card(sb, r.FilesWithFindings.ToString("N0"), "Files with findings");
        Card(sb, r.FilesScanned.ToString("N0"), "Files scanned");
        foreach (var s in new[] { Severity.Critical, Severity.High, Severity.Medium, Severity.Low })
            Card(sb, r.CountBySeverity(s).ToString("N0"), s.ToString(), s.ToString());
        sb.Append("</div>");

        sb.Append($"""
            <p class="muted">The scrubbed copy is in <code>{Html(r.ScanOnly ? "(scan only: no copy written)" : r.ScrubbedFolder)}</code>.
            Keep this report private: it shows where secrets were found. Automated scrubbing can miss things, so look through the scrubbed code before you share it.</p>
            """);

        if (r.RuleErrors.Count > 0)
            foreach (var e in r.RuleErrors) sb.Append($"<div class=\"warn\">Rule not used: {Html(e)}</div>");

        // ---- by category / rule ----
        sb.Append("<h2>By rule</h2><div class=\"scroll\"><table class=\"sortable\"><thead><tr><th>Severity</th><th>Category</th><th>Rule</th><th class=\"num\">Findings</th><th class=\"num\">Files</th></tr></thead><tbody>");
        foreach (var g in r.Findings.GroupBy(f => (f.RuleId, f.RuleName, f.Category))
                     .Select(g => new { g.Key, Sev = g.Max(f => f.Severity), Count = g.Count(), Files = g.Select(f => f.File).Distinct().Count() })
                     .OrderByDescending(x => x.Sev).ThenByDescending(x => x.Count))
        {
            sb.Append($"<tr><td data-v=\"{(int)g.Sev}\">{Sev(g.Sev)}</td><td>{Html(g.Key.Category)}</td><td>{Html(g.Key.RuleName)} <span class=\"muted mono\">{Html(g.Key.RuleId)}</span></td><td class=\"num\">{g.Count:N0}</td><td class=\"num\">{g.Files:N0}</td></tr>");
        }
        sb.Append("</tbody></table></div>");

        // ---- files ----
        sb.Append("<h2>Files with findings</h2><div class=\"scroll\"><table class=\"sortable\"><thead><tr><th>File</th><th>Worst</th><th class=\"num\">Findings</th><th>Output</th></tr></thead><tbody>");
        foreach (var f in r.Findings.GroupBy(f => f.File).Select(g => new { File = g.Key, Worst = g.Max(x => x.Severity), Count = g.Count() })
                     .OrderByDescending(x => x.Worst).ThenByDescending(x => x.Count))
        {
            var fr = r.Files.FirstOrDefault(x => x.Path == f.File);
            var output = fr == null ? "" : fr.Status == FileStatus.Sensitive ? "not copied" : fr.OutputPath;
            sb.Append($"<tr><td class=\"mono\">{Html(f.File)}</td><td data-v=\"{(int)f.Worst}\">{Sev(f.Worst)}</td><td class=\"num\">{f.Count:N0}</td><td class=\"mono muted\">{Html(output)}</td></tr>");
        }
        sb.Append("</tbody></table></div>");

        // ---- all findings ----
        sb.Append("""
            <h2>All findings</h2>
            <div class="filters">
              <input id="q" type="search" placeholder="Filter by file, rule, token, text...">
              <select id="sev"><option value="">All severities</option><option>Critical</option><option>High</option><option>Medium</option><option>Low</option><option>Info</option></select>
            </div>
            <div class="scroll"><table id="findings" class="sortable"><thead><tr>
              <th>Severity</th><th>File</th><th class="num">Line</th><th>Rule</th><th>Original (masked)</th><th>Placeholder</th><th>Scrubbed line</th>
            </tr></thead><tbody>
            """);
        foreach (var f in Sorted(r.Findings))
        {
            var line = f.Line == 0 ? "name" : f.EndLine > f.Line ? $"{f.Line}-{f.EndLine}" : f.Line.ToString();
            var original = f.Original != null ? $"<br><span class=\"muted\">original:</span> {Html(f.Original)}" : "";
            var also = f.AlsoMatched.Count > 0 ? $"<br><span class=\"muted\">also: {Html(string.Join(", ", f.AlsoMatched))}</span>" : "";
            sb.Append($"<tr data-sev=\"{f.Severity}\"><td data-v=\"{(int)f.Severity}\">{Sev(f.Severity)}</td><td class=\"mono\">{Html(f.File)}</td><td class=\"num\" data-v=\"{f.Line}\">{line}</td>" +
                      $"<td>{Html(f.RuleName)}{also}</td><td class=\"mono\">{Html(f.Preview)}{original}</td><td class=\"mono\">{Html(f.Token)}</td><td class=\"mono\">{Html(f.Context)}</td></tr>");
        }
        sb.Append("</tbody></table></div>");

        // ---- skipped files / warnings ----
        var skipped = r.Files.Where(f => f.Status is FileStatus.TooLarge or FileStatus.Error or FileStatus.BinarySkipped or FileStatus.BinaryCopied or FileStatus.Excluded).ToList();
        if (skipped.Count > 0)
        {
            sb.Append($"<h2>Files not scanned ({skipped.Count:N0})</h2><details><summary>Show list</summary><div class=\"scroll\"><table class=\"sortable\"><thead><tr><th>Status</th><th>File</th><th class=\"num\">Size</th><th>Note</th></tr></thead><tbody>");
            foreach (var f in skipped.OrderBy(f => f.Status).ThenBy(f => f.Path))
                sb.Append($"<tr><td>{f.Status}</td><td class=\"mono\">{Html(f.Path)}</td><td class=\"num\" data-v=\"{f.SizeBytes}\">{Size(f.SizeBytes)}</td><td>{Html(f.Message)}</td></tr>");
            sb.Append("</tbody></table></div></details>");
        }
        if (r.Warnings.Count > 0)
        {
            sb.Append($"<h2>Warnings ({r.Warnings.Count:N0})</h2><details><summary>Show warnings</summary><ul>");
            foreach (var w in r.Warnings) sb.Append($"<li>{Html(w)}</li>");
            sb.Append("</ul></details>");
        }

        // ---- settings ----
        var o = r.Options;
        sb.Append($"""
            <h2>Settings used</h2>
            <details><summary>Show settings</summary><table>
            <tr><td>Placeholder style</td><td>{o.Style} (<code>{Html(o.Style == ObfuscationStyle.Token ? o.TokenTemplate.Replace("{PREFIX}", o.TokenPrefix) : o.Style == ObfuscationStyle.Fixed ? o.FixedText : new string(o.MaskChar, 8))}</code>)</td></tr>
            <tr><td>Preserve line numbers</td><td>{(o.PreserveLineNumbers ? "Yes" : "No")}</td></tr>
            <tr><td>Same value = same placeholder</td><td>{(o.ConsistentTokens ? "Yes" : "No")}</td></tr>
            <tr><td>Scrub file names</td><td>{(o.ScrubFileNames ? "Yes" : "No")}</td></tr>
            <tr><td>Mapping file written</td><td>{(o.WriteMappingFile ? "Yes (Report\\mapping.json - contains original values!)" : "No")}</td></tr>
            <tr><td>Excluded folders</td><td class="mono">{Html(string.Join(", ", o.ExcludedFolders))}</td></tr>
            <tr><td>Sensitive files (never copied)</td><td class="mono">{Html(string.Join(", ", o.SensitiveFilePatterns))}</td></tr>
            <tr><td>Max file size</td><td>{(o.MaxFileSizeKB > 0 ? $"{o.MaxFileSizeKB:N0} KB" : "no limit")}</td></tr>
            </table></details>
            """);

        sb.Append($$"""
            </main>
            <footer>Generated by {{Html(r.Tool)}} {{Html(r.ToolVersion)}} on {{Html(r.MachineName)}}.</footer>
            <script>
            (function () {
              var q = document.getElementById('q'), sev = document.getElementById('sev');
              var rows = Array.prototype.slice.call(document.querySelectorAll('#findings tbody tr'));
              function apply() {
                var t = q.value.toLowerCase(), s = sev.value;
                rows.forEach(function (r) {
                  var ok = (!s || r.getAttribute('data-sev') === s) && (!t || r.textContent.toLowerCase().indexOf(t) >= 0);
                  r.style.display = ok ? '' : 'none';
                });
              }
              q.addEventListener('input', apply); sev.addEventListener('change', apply);
              document.querySelectorAll('table.sortable th').forEach(function (th, i) {
                th.addEventListener('click', function () {
                  var table = th.closest('table'), body = table.tBodies[0], idx = Array.prototype.indexOf.call(th.parentNode.children, th);
                  var asc = th.getAttribute('data-asc') !== '1'; th.setAttribute('data-asc', asc ? '1' : '0');
                  var list = Array.prototype.slice.call(body.rows);
                  list.sort(function (a, b) {
                    var ca = a.cells[idx], cb = b.cells[idx];
                    var va = ca.getAttribute('data-v'), vb = cb.getAttribute('data-v');
                    var x = va !== null ? parseFloat(va) : ca.textContent.toLowerCase();
                    var y = vb !== null ? parseFloat(vb) : cb.textContent.toLowerCase();
                    return (x < y ? -1 : x > y ? 1 : 0) * (asc ? 1 : -1);
                  });
                  list.forEach(function (r) { body.appendChild(r); });
                });
              });
            })();
            </script>
            </body>
            </html>
            """);
        return sb.ToString();
    }

    private static void Card(StringBuilder sb, string number, string label, string? severityClass = null)
    {
        var dot = severityClass != null ? $"<span class=\"sev {severityClass}\">&nbsp;</span> " : "";
        sb.Append($"<div class=\"card\"><div class=\"n\">{number}</div><div class=\"l\">{dot}{Html(label)}</div></div>");
    }

    private static string Sev(Severity s) => $"<span class=\"sev {s}\">{s}</span>";

    private static string Size(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:N1} KB",
        _ => $"{bytes / 1024.0 / 1024.0:N1} MB",
    };
}
