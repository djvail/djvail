using System.Text.RegularExpressions;

namespace VailIT.CodeClean.App;

internal sealed partial class MainForm
{
    private TabPage _filesTab = null!, _allowTab = null!;
    private readonly TextBox _excludedFolders = Ui.Text(multiline: true, mono: true);
    private readonly TextBox _includePatterns = Ui.Text(multiline: true, mono: true);
    private readonly TextBox _excludedPatterns = Ui.Text(multiline: true, mono: true);
    private readonly TextBox _sensitivePatterns = Ui.Text(multiline: true, mono: true);
    private readonly NumericUpDown _maxSize = Ui.Number(10240, 0, 10_000_000);
    private readonly CheckBox _copyBinary = Ui.Check("Copy binary files (images, DLLs...) unchanged - they cannot be scanned");
    private readonly NumericUpDown _timeout = Ui.Number(3000, 100, 600_000);
    private readonly NumericUpDown _parallel = Ui.Number(0, 0, 256);

    private readonly TextBox _allowValues = Ui.Text(multiline: true, mono: true);
    private readonly TextBox _allowPatterns = Ui.Text(multiline: true, mono: true);
    private readonly Label _allowStatus = Ui.Label("");

    private TabPage BuildFilesTab()
    {
        _filesTab = new TabPage("Files") { UseVisualStyleBackColor = true };
        var grid = Ui.Grid(-50, -50);

        grid.AddRow(Ui.Label("Folders to skip (by name or path, one per line)", bold: true), Ui.Label("Only scan these files (empty = all files)", bold: true));
        grid.AddRow(50, _excludedFolders, _includePatterns);
        grid.AddRow(Ui.Note("e.g. bin, obj, node_modules, .git, src/legacy/**"), Ui.Note("e.g. *.cs, *.json, *.config, src/**/*.yml"));

        grid.AddRow(Ui.Label("Files to ignore completely (not scanned, not copied)", bold: true), Ui.Label("Sensitive files (never copied, reported as High)", bold: true));
        grid.AddRow(50, _excludedPatterns, _sensitivePatterns);
        grid.AddRow(Ui.Note("e.g. *.suo, Thumbs.db"), Ui.Note("Key stores, certificates with private keys, Terraform state, database files, shell history..."));

        var limits = Ui.InnerGrid(0, 0, 0, 0, -100);
        limits.AddRow(Ui.Label("Max file size (KB, 0 = no limit)"), _maxSize, Ui.Label("   Regex timeout per file (ms)"), _timeout);
        limits.AddRow(Ui.Label("Files scanned at once (0 = one per CPU)"), _parallel, null, null);
        limits.AddRow(_copyBinary);
        limits.SetColumnSpan(_copyBinary, 4);
        grid.AddWide(Ui.Group("Limits", limits));
        grid.AddWide(Ui.Note("Patterns: * = any characters, ? = one character, ** = any folders. A pattern without / matches the file name; with / it matches the path under the scanned folder."));

        _filesTab.Controls.Add(grid);
        return _filesTab;
    }

    private TabPage BuildAllowTab()
    {
        _allowTab = new TabPage("Allow list") { UseVisualStyleBackColor = true };
        var grid = Ui.Grid(-50, -50);
        grid.AddWide(Ui.Note("Values listed here are NEVER scrubbed, whatever rule matches them. Use it for things that look sensitive but are not, " +
                             "such as public host names, test accounts or well-known example values."));
        grid.AddRow(Ui.Label("Exact values (one per line, not case sensitive)", bold: true), Ui.Label("Regular expressions (one per line)", bold: true));
        grid.AddRow(100, _allowValues, _allowPatterns);
        grid.AddRow(Ui.Note("e.g. localhost, build-agent-01, test@example.com"), Ui.Note(@"e.g. ^10\.0\.2\.\d+$   ^.*\.public-cdn\.com$"));
        grid.AddWide(_allowStatus);
        _allowPatterns.Leave += (_, _) => ValidateAllowPatterns();
        _allowTab.Controls.Add(grid);
        return _allowTab;
    }

    private void ShowFileOptions()
    {
        var o = _profile.Options;
        _excludedFolders.SetLines(o.ExcludedFolders);
        _includePatterns.SetLines(o.IncludeFilePatterns);
        _excludedPatterns.SetLines(o.ExcludedFilePatterns);
        _sensitivePatterns.SetLines(o.SensitiveFilePatterns);
        _maxSize.Value = Math.Clamp(o.MaxFileSizeKB, 0, 10_000_000);
        _copyBinary.Checked = o.CopyBinaryFiles;
        _timeout.Value = Math.Clamp(o.RegexTimeoutMs, 100, 600_000);
        _parallel.Value = Math.Clamp(o.MaxParallelism, 0, 256);
    }

    private void ReadFileOptions()
    {
        var o = _profile.Options;
        o.ExcludedFolders = _excludedFolders.Lines();
        o.IncludeFilePatterns = _includePatterns.Lines();
        o.ExcludedFilePatterns = _excludedPatterns.Lines();
        o.SensitiveFilePatterns = _sensitivePatterns.Lines();
        o.MaxFileSizeKB = (int)_maxSize.Value;
        o.CopyBinaryFiles = _copyBinary.Checked;
        o.RegexTimeoutMs = (int)_timeout.Value;
        o.MaxParallelism = (int)_parallel.Value;
    }

    private void ShowAllowList()
    {
        _allowValues.SetLines(_profile.AllowValues);
        _allowPatterns.SetLines(_profile.AllowPatterns);
        _allowStatus.Text = "";
    }

    private void ReadAllowList()
    {
        var values = _allowValues.Lines();
        var patterns = _allowPatterns.Lines();
        if (!values.SequenceEqual(_profile.AllowValues) || !patterns.SequenceEqual(_profile.AllowPatterns)) _detectorCache = null;
        _profile.AllowValues = values;
        _profile.AllowPatterns = patterns;
    }

    private void ValidateAllowPatterns()
    {
        var bad = new List<string>();
        foreach (var p in _allowPatterns.Lines())
        {
            try { _ = new Regex(p); }
            catch (ArgumentException ex) { bad.Add($"{p}  ->  {ex.Message}"); }
        }
        _allowStatus.ForeColor = bad.Count > 0 ? Color.Firebrick : Color.DarkGreen;
        _allowStatus.Text = bad.Count > 0 ? "Invalid patterns (they will be skipped):\n" + string.Join("\n", bad) : "All patterns are valid.";
    }
}
