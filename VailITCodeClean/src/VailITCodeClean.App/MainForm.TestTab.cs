using VailIT.CodeClean.Configuration;
using VailIT.CodeClean.Engine;

namespace VailIT.CodeClean.App;

internal sealed partial class MainForm
{
    private TabPage _testTab = null!;
    private readonly RichTextBox _testInput = new() { Dock = DockStyle.Fill, Font = Ui.Mono, WordWrap = false, AcceptsTab = true, DetectUrls = false, HideSelection = false };
    private readonly TextBox _testOutput = Ui.Text(multiline: true, mono: true);
    private readonly TextBox _testFileName = new() { Width = 220, Margin = Ui.Gap, PlaceholderText = "e.g. appsettings.json" };
    private readonly CheckBox _testOnlySelected = Ui.Check("Only the rules selected on the Rules tab");
    private readonly ListView _testHits = new() { Dock = DockStyle.Fill, View = View.Details, FullRowSelect = true, HideSelection = false };
    private readonly System.Windows.Forms.Timer _testTimer = new() { Interval = 500 };
    private Detector? _detectorCache;
    private bool _highlighting;

    private const string TestSample = """
        // Paste any code or config here: matches are highlighted and the scrubbed version is shown below.
        var conn = "Server=PROD-SQL01;Database=Sales;User Id=svc_sales;Password=Spring2024!;";
        string apiKey = "a8F3kL0pQ7zX2mN5vB9cR4tY6uI1oP3s";
        var url = "https://orders-api.contoso.com/v2/orders";
        var share = @"\\fs01.contoso.local\Finance$";
        // Contact jane.smith@contoso.com, server 10.20.30.40
        var notASecret = GetPasswordFromVault();
        """;

    private TabPage BuildTestTab()
    {
        _testTab = new TabPage("Test") { UseVisualStyleBackColor = true };
        _testOutput.ReadOnly = true;
        _testInput.Text = TestSample.ReplaceLineEndings("\n");

        _testHits.Columns.Add("Line", 50);
        _testHits.Columns.Add("Severity", 75);
        _testHits.Columns.Add("Rule", 260);
        _testHits.Columns.Add("Value (masked)", 220);
        _testHits.Columns.Add("Also matched", 260);
        _testHits.SelectedIndexChanged += (_, _) =>
        {
            if (_testHits.SelectedItems.Count == 0 || _testHits.SelectedItems[0].Tag is not DetectionGroup g) return;
            _testInput.Select(g.Start, g.Length);
            _testInput.ScrollToCaret();
        };

        _testTimer.Tick += (_, _) => { _testTimer.Stop(); RunTest(); };
        _testInput.TextChanged += (_, _) => { if (!_highlighting) { _testTimer.Stop(); _testTimer.Start(); } };
        _testFileName.TextChanged += (_, _) => { _testTimer.Stop(); _testTimer.Start(); };
        _testOnlySelected.CheckedChanged += (_, _) => RunTest();

        var top = Ui.Flow(Ui.Label("File name (for rules limited to some files):"), _testFileName, _testOnlySelected,
            Ui.Button("Test now", (_, _) => RunTest(), 90),
            Ui.Button("Load file...", (_, _) => LoadTestFile(), 90));

        var inner = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal };
        inner.Panel1.Controls.Add(_testHits);
        inner.Panel2.Controls.Add(_testOutput);

        var outer = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal };
        outer.Panel1.Controls.Add(_testInput);
        outer.Panel2.Controls.Add(inner);
        _testTab.Layout += (_, _) =>
        {
            // Splitter positions need the real size, so set them once the tab has one.
            if (outer.Tag != null || outer.Height < 200) return;
            outer.Tag = true;
            outer.SplitterDistance = outer.Height * 45 / 100;
            inner.SplitterDistance = Math.Max(60, inner.Height * 40 / 100);
        };

        var grid = Ui.Grid(-100);
        grid.AddRow(top);
        grid.AddRow(100, outer);
        _testTab.Controls.Add(grid);
        return _testTab;
    }

    private void LoadTestFile()
    {
        using var dlg = new OpenFileDialog { Filter = "All files|*.*" };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            var loaded = TextFileCodec.Read(dlg.FileName);
            if (loaded.IsBinary) { Ui.Error(this, "That looks like a binary file."); return; }
            _testFileName.Text = Path.GetFileName(dlg.FileName);
            _testInput.Text = loaded.Text.ReplaceLineEndings("\n");
        }
        catch (Exception ex) { Ui.Error(this, ex.Message); }
    }

    /// <summary>Runs the rules over the test text, highlights the matches and shows the scrubbed text.</summary>
    private void RunTest()
    {
        if (_testTab == null) return;
        ReadScreen();
        Detector detector;
        if (_testOnlySelected.Checked)
        {
            var selected = SelectedRules();
            if (selected.Count == 0) { _testOutput.Text = "Select one or more rules on the Rules tab first (or untick 'Only the rules selected')."; return; }
            detector = new Detector(_profile, selected);
        }
        else
        {
            detector = _detectorCache ??= new Detector(_profile);
        }

        // RichTextBox uses \n internally, so offsets line up with its Select().
        var text = _testInput.Text;
        var warnings = new List<string>();
        var groups = detector.Detect(text, _testFileName.Text.Trim(), warnings);
        var output = new Scrubber(_profile.Options, new TokenProvider(_profile.Options)).Apply(text, groups, "", out _);
        _testOutput.Text = output.ReplaceLineEndings(Environment.NewLine);

        var lines = new LineIndex(text);
        _testHits.BeginUpdate();
        _testHits.Items.Clear();
        foreach (var g in groups)
        {
            var item = new ListViewItem([
                lines.GetLine(g.Start).ToString(), g.Severity.ToString(), g.Primary.Rule.Rule.Name,
                ValueChecks.MaskedPreview(g.Value), string.Join(", ", g.Members.Where(m => m != g.Primary).Select(m => m.Rule.Rule.Id).Distinct()),
            ]) { Tag = g, BackColor = Ui.SeverityColor(g.Severity) };
            _testHits.Items.Add(item);
        }
        foreach (var e in detector.Errors.Concat(warnings))
            _testHits.Items.Add(new ListViewItem(["", "Error", e, "", ""]) { ForeColor = Color.Firebrick });
        _testHits.EndUpdate();

        Highlight(groups);
    }

    private void Highlight(List<DetectionGroup> groups)
    {
        _highlighting = true;
        int selStart = _testInput.SelectionStart, selLength = _testInput.SelectionLength;
        _testInput.SuspendLayout();
        _testInput.SelectAll();
        _testInput.SelectionBackColor = _testInput.BackColor;
        foreach (var g in groups)
        {
            if (g.End > _testInput.TextLength) continue;
            _testInput.Select(g.Start, g.Length);
            _testInput.SelectionBackColor = Ui.SeverityColor(g.Severity == Severity.Info ? Severity.Low : g.Severity);
        }
        _testInput.Select(selStart, selLength);
        _testInput.ResumeLayout();
        _highlighting = false;
    }
}
