using VailIT.CodeClean.Configuration;
using VailIT.CodeClean.Reporting;

namespace VailIT.CodeClean.App;

internal sealed partial class MainForm
{
    private TabPage _resultsTab = null!;
    private readonly Label _resultSummary = Ui.Label("No run yet.", bold: true);
    private readonly TextBox _resultFilter = new() { Width = 300, Margin = Ui.Gap, PlaceholderText = "Filter by file, rule, placeholder, text..." };
    private readonly ComboBox _resultSeverity = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 140, Margin = Ui.Gap };
    private readonly DataGridView _findings = new()
    {
        Dock = DockStyle.Fill,
        ReadOnly = true,
        AllowUserToAddRows = false,
        AllowUserToDeleteRows = false,
        AllowUserToResizeRows = false,
        SelectionMode = DataGridViewSelectionMode.FullRowSelect,
        RowHeadersVisible = false,
        BackgroundColor = SystemColors.Window,
        BorderStyle = BorderStyle.None,
    };

    private TabPage BuildResultsTab()
    {
        _resultsTab = new TabPage("Results") { UseVisualStyleBackColor = true };

        foreach (var (name, width, fill) in new[]
                 {
                     ("Severity", 75, false), ("File", 300, false), ("Line", 55, false), ("Rule", 230, false),
                     ("Original (masked)", 160, false), ("Placeholder", 220, false), ("Scrubbed line", 400, true),
                 })
        {
            var col = new DataGridViewTextBoxColumn { Name = name, HeaderText = name, Width = width, SortMode = DataGridViewColumnSortMode.Automatic };
            if (fill) col.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
            _findings.Columns.Add(col);
        }
        _findings.Columns["Scrubbed line"]!.DefaultCellStyle.Font = Ui.Mono;
        _findings.Columns["Placeholder"]!.DefaultCellStyle.Font = Ui.Mono;
        _findings.CellDoubleClick += (_, e) => OpenFindingFile(e.RowIndex);

        _resultSeverity.Items.AddRange(["All severities", "Critical", "High", "Medium", "Low", "Info"]);
        _resultSeverity.SelectedIndex = 0;
        _resultSeverity.SelectedIndexChanged += (_, _) => FillFindings();
        _resultFilter.TextChanged += (_, _) => FillFindings();

        var grid = Ui.Grid(-100);
        grid.AddRow(_resultSummary);
        grid.AddRow(Ui.Flow(_resultFilter, _resultSeverity,
            Ui.Button("Open report", (_, _) => OpenReport(), 100),
            Ui.Button("Open scrubbed folder", (_, _) => { if (_lastResult != null) Ui.Open(Directory.Exists(_lastResult.ScrubbedFolder) ? _lastResult.ScrubbedFolder : _lastResult.RunFolder); }, 150)));
        grid.AddRow(Ui.Note("Double-click a finding to open the scrubbed file. Always look through the scrubbed copy before sharing it: no tool catches everything."));
        grid.AddRow(100, _findings);
        _resultsTab.Controls.Add(grid);
        return _resultsTab;
    }

    private void ShowResult(RunResult r)
    {
        var bySeverity = string.Join(", ", Enum.GetValues<Severity>().Reverse().Where(s => r.CountBySeverity(s) > 0).Select(s => $"{r.CountBySeverity(s):N0} {s}"));
        _resultSummary.Text = $"{r.FindingCount:N0} findings in {r.FilesWithFindings:N0} of {r.FilesScanned:N0} scanned files" +
                              (bySeverity.Length > 0 ? $" ({bySeverity})" : "") +
                              (r.Warnings.Count > 0 ? $" - {r.Warnings.Count} warning(s), see report" : "") +
                              (r.Cancelled ? " - CANCELLED" : "");
        FillFindings();
    }

    private void FillFindings()
    {
        _findings.Rows.Clear();
        if (_lastResult == null) return;
        var q = _resultFilter.Text.Trim();
        var sev = _resultSeverity.SelectedIndex > 0 ? Enum.Parse<Severity>((string)_resultSeverity.SelectedItem!) : (Severity?)null;
        var rows = new List<DataGridViewRow>();
        foreach (var f in _lastResult.Findings.OrderByDescending(f => f.Severity).ThenBy(f => f.File).ThenBy(f => f.Line))
        {
            if (sev != null && f.Severity != sev) continue;
            if (q.Length > 0 && !(f.File.Contains(q, StringComparison.OrdinalIgnoreCase) || f.RuleName.Contains(q, StringComparison.OrdinalIgnoreCase)
                                 || f.Token.Contains(q, StringComparison.OrdinalIgnoreCase) || f.Context.Contains(q, StringComparison.OrdinalIgnoreCase)
                                 || f.Category.Contains(q, StringComparison.OrdinalIgnoreCase)))
                continue;
            var row = new DataGridViewRow();
            row.CreateCells(_findings, f.Severity.ToString(), f.File, f.Line == 0 ? "name" : f.Line.ToString(), f.RuleName, f.Preview, f.Token, f.Context);
            row.Cells[0].Style.BackColor = Ui.SeverityColor(f.Severity);
            row.Tag = f;
            rows.Add(row);
            if (rows.Count >= 20000) break; // the full list is always in the report
        }
        _findings.Rows.AddRange(rows.ToArray());
    }

    private void OpenFindingFile(int rowIndex)
    {
        if (rowIndex < 0 || _lastResult == null || _findings.Rows[rowIndex].Tag is not Finding f) return;
        var file = _lastResult.Files.FirstOrDefault(x => x.Path == f.File);
        if (file == null || string.IsNullOrEmpty(file.OutputPath))
        {
            Ui.Info(this, "This file was not written to the output (scan only, or a sensitive file).");
            return;
        }
        Ui.Open(Path.Combine(_lastResult.ScrubbedFolder, file.OutputPath.Replace('/', Path.DirectorySeparatorChar)));
    }
}
