using VailIT.CodeClean.Configuration;
using VailIT.CodeClean.Engine;
using VailIT.CodeClean.Reporting;

namespace VailIT.CodeClean.App;

internal sealed partial class MainForm
{
    private TabPage _scanTab = null!;
    private readonly TextBox _source = Ui.Text();
    private readonly TextBox _output = Ui.Text();

    private readonly CheckBox _timestamped = Ui.Check("New dated sub folder for each run");
    private readonly CheckBox _preserveLines = Ui.Check("Preserve line numbers");
    private readonly CheckBox _consistent = Ui.Check("Same value = same placeholder");
    private readonly CheckBox _scrubNames = Ui.Check("Scrub file and folder names");
    private readonly CheckBox _scanOnly = Ui.Check("Scan only (report, no scrubbed copy)");
    private readonly CheckBox _mapping = Ui.Check("Write mapping file (ORIGINAL values - keep private)");
    private readonly CheckBox _originalsInReport = Ui.Check("Show original values in report (not recommended)");

    private readonly ComboBox _style = Ui.Combo(ObfuscationStyle.Token);
    private readonly TextBox _prefix = Ui.Text();
    private readonly TextBox _template = Ui.Text(mono: true);
    private readonly TextBox _fixedText = Ui.Text();
    private readonly TextBox _maskChar = Ui.Text();
    private readonly TextBox _salt = Ui.Text();
    private readonly Label _placeholderPreview = Ui.Label("");

    private readonly CheckBox _html = Ui.Check("HTML", true);
    private readonly CheckBox _csv = Ui.Check("CSV", true);
    private readonly CheckBox _json = Ui.Check("JSON", true);

    private Button _start = null!, _cancel = null!, _openOutput = null!, _openReport = null!, _openRestore = null!;
    private readonly ProgressBar _progress = new() { Dock = DockStyle.Fill, Margin = Ui.Gap, Height = 22 };
    private readonly Label _progressText = Ui.Label("Ready.");
    private readonly TextBox _log = Ui.Text(multiline: true, mono: true);

    private CancellationTokenSource? _cts;
    private bool _running;
    private RunResult? _lastResult;

    private TabPage BuildScanTab()
    {
        _scanTab = new TabPage("Scan") { UseVisualStyleBackColor = true };
        _log.ReadOnly = true;
        _log.ScrollBars = ScrollBars.Vertical;
        _log.WordWrap = true;
        _maskChar.MaxLength = 1;
        _maskChar.Width = 40;
        _maskChar.Dock = DockStyle.None;

        var grid = Ui.Grid(0, -100, 0);

        // ---- folders ----
        grid.AddRow(Ui.Label("Code folder to scan"), _source, Ui.Button("Browse...", (_, _) => BrowseFolder(_source, "Choose the folder with the code to scan (all sub folders are included)")));
        grid.AddRow(Ui.Label("Output folder"), _output, Ui.Button("Browse...", (_, _) => BrowseFolder(_output, "Choose where the scrubbed copy and the report are written")));
        grid.AddRow(null, Ui.Note("The original code is never changed. Each run writes <Output>\\<Name>_<date>\\Scrubbed (share this) and \\Report (keep private)."));

        // ---- options ----
        var options = Ui.InnerGrid(-50, -50);
        options.AddRow(_preserveLines, _consistent);
        options.AddRow(_timestamped, _scrubNames);
        options.AddRow(_scanOnly, _mapping);
        options.AddRow(_originalsInReport, null);

        var placeholders = Ui.InnerGrid(0, -100, 0, 0);
        placeholders.AddRow(Ui.Label("Style"), _style, Ui.Label("Prefix"), _prefix);
        placeholders.AddRow(Ui.Label("Template"), _template, Ui.Label("Hash salt"), _salt);
        placeholders.AddRow(Ui.Label("Fixed text"), _fixedText, Ui.Label("Mask char"), _maskChar);
        placeholders.AddRow(Ui.Label("Example"), _placeholderPreview);
        placeholders.SetColumnSpan(_placeholderPreview, 3);
        placeholders.AddRow(null, Ui.Note("Template fields: {PREFIX} {LABEL} {ID} {HASH}. {HASH} is the same for the same value on every run if you set a salt."));
        placeholders.SetColumnSpan(placeholders.GetControlFromPosition(1, placeholders.RowCount - 1)!, 3);

        var reports = Ui.Flow(Ui.Label("Report formats:"), _html, _csv, _json);

        var row = Ui.InnerGrid(-50, -50);
        row.AddRow(Ui.Group("Options", options), Ui.Group("Placeholders", placeholders));
        grid.AddWide(row);
        grid.AddWide(reports);

        // ---- run ----
        _start = Ui.Button("Start", (_, _) => StartRun(), 120);
        _start.Font = new Font(_start.Font, FontStyle.Bold);
        _cancel = Ui.Button("Cancel", (_, _) => _cts?.Cancel());
        _cancel.Enabled = false;
        _openOutput = Ui.Button("Open output folder", (_, _) => { if (_lastResult != null) Ui.Open(_lastResult.RunFolder); }, 140);
        _openReport = Ui.Button("Open report", (_, _) => OpenReport(), 110);
        _openRestore = Ui.Button("Restore values...", (_, _) => OpenRestore(_lastResult == null ? null : Path.Combine(_lastResult.ReportFolder, "mapping.json")), 130);
        _openOutput.Enabled = _openReport.Enabled = false;
        grid.AddWide(Ui.Flow(_start, _cancel, _openOutput, _openReport, _openRestore));
        grid.AddRow(Ui.Label("Progress"), _progress);
        grid.SetColumnSpan(_progress, 2);
        grid.AddRow(null, _progressText);
        grid.SetColumnSpan(_progressText, 2);
        grid.AddWide(_log, 100);

        foreach (Control c in new Control[] { _style, _prefix, _template, _fixedText, _maskChar })
            c.TextChanged += (_, _) => UpdatePlaceholderPreview();
        _style.SelectedIndexChanged += (_, _) => UpdatePlaceholderPreview();

        _scanTab.Controls.Add(grid);
        return _scanTab;
    }

    private void ShowScanOptions()
    {
        var o = _profile.Options;
        _source.Text = o.SourceFolder;
        _output.Text = o.OutputFolder;
        _timestamped.Checked = o.UseTimestampedRunFolder;
        _preserveLines.Checked = o.PreserveLineNumbers;
        _consistent.Checked = o.ConsistentTokens;
        _scrubNames.Checked = o.ScrubFileNames;
        _scanOnly.Checked = o.ScanOnly;
        _mapping.Checked = o.WriteMappingFile;
        _originalsInReport.Checked = o.IncludeOriginalValuesInReport;
        _style.SelectedItem = o.Style;
        _prefix.Text = o.TokenPrefix;
        _template.Text = o.TokenTemplate;
        _fixedText.Text = o.FixedText;
        _maskChar.Text = o.MaskChar.ToString();
        _salt.Text = o.HashSalt;
        _html.Checked = o.WriteHtmlReport;
        _csv.Checked = o.WriteCsvReport;
        _json.Checked = o.WriteJsonReport;
        UpdatePlaceholderPreview();
    }

    private void ReadScanOptions()
    {
        var o = _profile.Options;
        o.SourceFolder = _source.Text.Trim();
        o.OutputFolder = _output.Text.Trim();
        o.UseTimestampedRunFolder = _timestamped.Checked;
        o.PreserveLineNumbers = _preserveLines.Checked;
        o.ConsistentTokens = _consistent.Checked;
        o.ScrubFileNames = _scrubNames.Checked;
        o.ScanOnly = _scanOnly.Checked;
        o.WriteMappingFile = _mapping.Checked;
        o.IncludeOriginalValuesInReport = _originalsInReport.Checked;
        o.Style = (ObfuscationStyle)(_style.SelectedItem ?? ObfuscationStyle.Token);
        o.TokenPrefix = string.IsNullOrWhiteSpace(_prefix.Text) ? "VAILIT" : _prefix.Text.Trim();
        o.TokenTemplate = string.IsNullOrWhiteSpace(_template.Text) ? "{PREFIX}_{LABEL}_{ID}" : _template.Text.Trim();
        o.FixedText = _fixedText.Text;
        o.MaskChar = _maskChar.Text.Length > 0 ? _maskChar.Text[0] : '*';
        o.HashSalt = _salt.Text;
        o.WriteHtmlReport = _html.Checked;
        o.WriteCsvReport = _csv.Checked;
        o.WriteJsonReport = _json.Checked;
    }

    private void UpdatePlaceholderPreview()
    {
        var style = (ObfuscationStyle)(_style.SelectedItem ?? ObfuscationStyle.Token);
        _template.Enabled = _prefix.Enabled = _salt.Enabled = style == ObfuscationStyle.Token;
        _fixedText.Enabled = style == ObfuscationStyle.Fixed;
        _maskChar.Enabled = style == ObfuscationStyle.Mask;
        _placeholderPreview.Text = style switch
        {
            ObfuscationStyle.Mask => "password = \"" + new string(_maskChar.Text.Length > 0 ? _maskChar.Text[0] : '*', 11) + "\"",
            ObfuscationStyle.Fixed => "password = \"" + _fixedText.Text + "\"",
            _ => "password = \"" + (_template.Text.Length > 0 ? _template.Text : "{PREFIX}_{LABEL}_{ID}")
                .Replace("{PREFIX}", _prefix.Text, StringComparison.OrdinalIgnoreCase)
                .Replace("{LABEL}", "PASSWORD", StringComparison.OrdinalIgnoreCase)
                .Replace("{ID}", "0001", StringComparison.OrdinalIgnoreCase)
                .Replace("{HASH}", "3FA94C1D", StringComparison.OrdinalIgnoreCase) + "\"",
        };
    }

    private void BrowseFolder(TextBox target, string description)
    {
        using var dlg = new FolderBrowserDialog { UseDescriptionForTitle = true, Description = description, ShowNewFolderButton = true };
        if (Directory.Exists(target.Text)) dlg.InitialDirectory = target.Text;
        if (dlg.ShowDialog(this) == DialogResult.OK) target.Text = dlg.SelectedPath;
    }

    // ------------------------------------------------------------------ running

    private async void StartRun()
    {
        if (_running) return;
        ReadScreen();
        var o = _profile.Options;
        if (!Directory.Exists(o.SourceFolder)) { Ui.Error(this, "Choose the code folder to scan."); return; }
        if (string.IsNullOrWhiteSpace(o.OutputFolder)) { Ui.Error(this, "Choose an output folder."); return; }
        if (!_profile.Rules.Any(r => r.Enabled)) { Ui.Error(this, "No rules are enabled (see the Rules tab)."); return; }
        if (o.WriteMappingFile &&
            MessageBox.Show(this, "The mapping file will contain every ORIGINAL secret in plain text (in the Report folder).\n\nKeep it private and delete it when you no longer need it. Continue?",
                "Mapping file", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning) != DialogResult.OK) return;

        SaveProfile(_profilePath, quiet: true);
        _log.Clear();
        _progress.Value = 0;
        SetRunning(true);
        _cts = new CancellationTokenSource();

        // Progress<T> created here (on the UI thread) delivers updates back on the UI thread.
        var progress = new Progress<RunProgress>(p =>
        {
            if (p.Total > 0)
            {
                _progress.Maximum = p.Total;
                _progress.Value = Math.Min(p.Done, p.Total);
            }
            _progressText.Text = $"{p.Phase}... {p.Done:N0} / {p.Total:N0} files, {p.FindingsSoFar:N0} findings   {p.CurrentFile}";
        });
        var log = new Progress<string>(AppendLog);

        try
        {
            // Run on a worker thread with a snapshot of the profile, so editing the screen cannot affect the run.
            var snapshot = Clone(_profile);
            var runner = new CleanRunner(snapshot, progress, log);
            var token = _cts.Token;
            var result = await Task.Run(() => runner.Run(token));
            _lastResult = result;
            ShowResult(result);
            _progress.Value = _progress.Maximum;
            _progressText.Text = result.Cancelled
                ? "Cancelled. Partial results are in the output folder."
                : $"Done in {result.Duration.TotalSeconds:N1} s: {result.FindingCount:N0} findings in {result.FilesWithFindings:N0} of {result.FilesScanned:N0} files.";
            AppendLog(_progressText.Text);
            if (!result.Cancelled && result.FindingCount > 0) _tabs.SelectedTab = _resultsTab;
        }
        catch (Exception ex)
        {
            _progressText.Text = "Failed: " + ex.Message;
            AppendLog("ERROR: " + ex);
            Ui.Error(this, ex.Message);
        }
        finally
        {
            SetRunning(false);
            _cts.Dispose();
            _cts = null;
        }
    }

    private void SetRunning(bool running)
    {
        _running = running;
        _start.Enabled = !running;
        _cancel.Enabled = running;
        _openOutput.Enabled = _openReport.Enabled = !running && _lastResult != null;
        _rulesTab.Enabled = _filesTab.Enabled = _allowTab.Enabled = !running;
        UseWaitCursor = running;
    }

    private void AppendLog(string message)
    {
        if (_log.TextLength > 2_000_000) _log.Clear();
        _log.AppendText($"{DateTime.Now:HH:mm:ss}  {message}{Environment.NewLine}");
    }

    private void OpenReport()
    {
        if (_lastResult == null) return;
        var html = Path.Combine(_lastResult.ReportFolder, "report.html");
        Ui.Open(File.Exists(html) ? html : _lastResult.ReportFolder);
    }

    private static CleanProfile Clone(CleanProfile p) =>
        System.Text.Json.JsonSerializer.Deserialize<CleanProfile>(System.Text.Json.JsonSerializer.Serialize(p, ProfileStore.JsonOptions), ProfileStore.JsonOptions)!;
}
