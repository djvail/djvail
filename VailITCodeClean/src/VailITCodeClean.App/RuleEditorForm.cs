using VailIT.CodeClean.Configuration;
using VailIT.CodeClean.Engine;

namespace VailIT.CodeClean.App;

/// <summary>Add / edit one rule, with a live test of the pattern against the example text.</summary>
internal sealed class RuleEditorForm : Form
{
    private readonly FilterRule _rule;
    private readonly CleanProfile _profile;

    private readonly TextBox _id = Ui.Text();
    private readonly TextBox _name = Ui.Text();
    private readonly ComboBox _category = new() { Dock = DockStyle.Fill, Margin = Ui.Gap };
    private readonly TextBox _description = Ui.Text(multiline: true, height: 48);
    private readonly CheckBox _enabled = Ui.Check("Enabled");
    private readonly ComboBox _type = Ui.Combo(PatternType.Regex);
    private readonly TextBox _pattern = Ui.Text(multiline: true, mono: true, height: 90);
    private readonly CheckBox _caseSensitive = Ui.Check("Case sensitive");
    private readonly CheckBox _wholeWord = Ui.Check("Whole word (exact text only)");
    private readonly ComboBox _severity = Ui.Combo(Severity.Medium);
    private readonly TextBox _label = Ui.Text();
    private readonly ComboBox _scope = Ui.Combo(ReplaceScope.Match);
    private readonly NumericUpDown _minLength = Ui.Number(0, 0, 10000);
    private readonly NumericUpDown _minEntropy = Ui.Number(0, 0, 8, 1, 0.1m);
    private readonly ComboBox _validator = Ui.Combo(ValueValidator.None);
    private readonly CheckBox _allParts = Ui.Check("All parts required (multi-group rules)");
    private readonly TextBox _ignoreValues = Ui.Text(multiline: true, height: 48);
    private readonly TextBox _ignoreValuePattern = Ui.Text(mono: true);
    private readonly TextBox _ignoreLinePattern = Ui.Text(mono: true);
    private readonly TextBox _includeFiles = Ui.Text(multiline: true, height: 40);
    private readonly TextBox _excludeFiles = Ui.Text(multiline: true, height: 40);
    private readonly TextBox _example = Ui.Text(multiline: true, mono: true, height: 70);
    private readonly TextBox _testResult = Ui.Text(multiline: true, mono: true, height: 70);
    private readonly Label _status = Ui.Label("");
    private readonly System.Windows.Forms.Timer _debounce = new() { Interval = 400 };

    /// <summary>The edited copy. Only valid when the dialog returns OK.</summary>
    public FilterRule Result => _rule;

    public RuleEditorForm(FilterRule rule, CleanProfile profile, bool isNew)
    {
        _rule = rule.Clone();
        _profile = profile;

        Text = isNew ? "Add rule" : $"Edit rule - {rule.Name}";
        StartPosition = FormStartPosition.CenterParent;
        AutoScaleMode = AutoScaleMode.Dpi;
        MinimumSize = new Size(760, 600);
        Size = new Size(940, 860);
        ShowInTaskbar = false;
        MinimizeBox = false;

        _testResult.ReadOnly = true;
        _status.MaximumSize = new Size(860, 0);
        _category.Items.AddRange(profile.Rules.Select(r => r.Category).Distinct().OrderBy(c => c).Cast<object>().ToArray());

        var grid = Ui.Grid(0, -100, 0, -100);
        grid.AutoScroll = true;
        grid.AddRow(Ui.Label("Name"), _name, Ui.Label("Id"), _id);
        grid.AddRow(Ui.Label("Category"), _category, Ui.Label("Severity"), _severity);
        grid.AddRow(Ui.Label("Description"), _description);
        grid.SetColumnSpan(_description, 3);
        grid.AddRow(Ui.Label("Type"), Ui.Flow(_type, _enabled, _caseSensitive, _wholeWord));
        grid.SetColumnSpan(grid.GetControlFromPosition(1, grid.RowCount - 1)!, 3);

        grid.AddRow(Ui.Label("Pattern"), _pattern);
        grid.SetColumnSpan(_pattern, 3);
        grid.AddRow(null, Ui.Note("Regex: use (?<secret>...) to replace only part of the match, or several (?<secret_LABEL>...) groups. " +
                                  "Exact: one literal text per line. Use \\r?$ (not just $) to anchor at the end of a line in Windows files."));
        grid.SetColumnSpan(grid.GetControlFromPosition(1, grid.RowCount - 1)!, 3);

        grid.AddRow(Ui.Label("Placeholder label"), _label, Ui.Label("Replace"), _scope);
        grid.AddRow(Ui.Label("Min length"), _minLength, Ui.Label("Min entropy"), _minEntropy);
        grid.AddRow(Ui.Label("Validator"), _validator, null, _allParts);

        grid.AddRow(Ui.Label("Ignore values"), _ignoreValues);
        grid.SetColumnSpan(_ignoreValues, 3);
        grid.AddRow(Ui.Label("Ignore value regex"), _ignoreValuePattern);
        grid.SetColumnSpan(_ignoreValuePattern, 3);
        grid.AddRow(Ui.Label("Ignore line regex"), _ignoreLinePattern);
        grid.SetColumnSpan(_ignoreLinePattern, 3);
        grid.AddRow(Ui.Label("Only files"), _includeFiles, Ui.Label("Never files"), _excludeFiles);
        grid.AddRow(null, Ui.Note("File patterns, one per line: *.config, .env, src/**/appsettings*.json. Empty = all files."));
        grid.SetColumnSpan(grid.GetControlFromPosition(1, grid.RowCount - 1)!, 3);

        grid.AddRow(Ui.Label("Example / test text"), _example);
        grid.SetColumnSpan(_example, 3);
        grid.AddRow(Ui.Label("Test result"), _testResult);
        grid.SetColumnSpan(_testResult, 3);
        grid.AddRow(null, _status);
        grid.SetColumnSpan(_status, 3);

        var ok = Ui.Button("OK", (_, _) => Accept());
        var cancel = Ui.Button("Cancel", (_, _) => { DialogResult = DialogResult.Cancel; Close(); });
        AcceptButton = null; // Enter is needed inside the multi-line boxes
        CancelButton = cancel;
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, FlowDirection = FlowDirection.RightToLeft, AutoSize = true, Padding = new Padding(6) };
        buttons.Controls.AddRange([cancel, ok]);

        Controls.Add(grid);
        Controls.Add(buttons);

        LoadRule();

        _debounce.Tick += (_, _) => { _debounce.Stop(); RunTest(); };
        foreach (Control c in new Control[] { _pattern, _example, _ignoreValuePattern, _ignoreLinePattern, _ignoreValues, _label })
            c.TextChanged += (_, _) => { _debounce.Stop(); _debounce.Start(); };
        foreach (var cb in new[] { _type, _scope, _validator })
            cb.SelectedIndexChanged += (_, _) => RunTest();
        foreach (var chk in new[] { _caseSensitive, _wholeWord, _allParts })
            chk.CheckedChanged += (_, _) => RunTest();
        _minLength.ValueChanged += (_, _) => RunTest();
        _minEntropy.ValueChanged += (_, _) => RunTest();
        Shown += (_, _) => RunTest();
    }

    private void LoadRule()
    {
        var r = _rule;
        _id.Text = r.Id;
        _name.Text = r.Name;
        _category.Text = r.Category;
        _description.Text = r.Description;
        _enabled.Checked = r.Enabled;
        _type.SelectedItem = r.Type;
        _pattern.Text = r.Pattern.ReplaceLineEndings(Environment.NewLine);
        _caseSensitive.Checked = r.CaseSensitive;
        _wholeWord.Checked = r.WholeWord;
        _severity.SelectedItem = r.Severity;
        _label.Text = r.TokenLabel;
        _scope.SelectedItem = r.Scope;
        _minLength.Value = Math.Clamp(r.MinLength, 0, 10000);
        _minEntropy.Value = (decimal)Math.Clamp(r.MinEntropy, 0, 8);
        _validator.SelectedItem = r.Validator;
        _allParts.Checked = r.AllPartsRequired;
        _ignoreValues.SetLines(r.IgnoreValues);
        _ignoreValuePattern.Text = r.IgnoreValuePattern;
        _ignoreLinePattern.Text = r.IgnoreLinePattern;
        _includeFiles.SetLines(r.IncludeFiles);
        _excludeFiles.SetLines(r.ExcludeFiles);
        _example.Text = r.Example.ReplaceLineEndings(Environment.NewLine);
    }

    /// <summary>Copies the screen into a rule object.</summary>
    private FilterRule ReadRule()
    {
        var r = _rule.Clone();
        r.Id = _id.Text.Trim();
        r.Name = _name.Text.Trim();
        r.Category = string.IsNullOrWhiteSpace(_category.Text) ? "Custom" : _category.Text.Trim();
        r.Description = _description.Text.Trim();
        r.Enabled = _enabled.Checked;
        r.Type = (PatternType)_type.SelectedItem!;
        // Regex: keep the text as typed (one line normally). Exact: one term per line.
        r.Pattern = r.Type == PatternType.Exact ? string.Join("\n", _pattern.Lines()) : _pattern.Text.Trim('\r', '\n');
        r.CaseSensitive = _caseSensitive.Checked;
        r.WholeWord = _wholeWord.Checked;
        r.Severity = (Severity)_severity.SelectedItem!;
        r.TokenLabel = _label.Text.Trim();
        r.Scope = (ReplaceScope)_scope.SelectedItem!;
        r.MinLength = (int)_minLength.Value;
        r.MinEntropy = (double)_minEntropy.Value;
        r.Validator = (ValueValidator)_validator.SelectedItem!;
        r.AllPartsRequired = _allParts.Checked;
        r.IgnoreValues = _ignoreValues.Lines();
        r.IgnoreValuePattern = _ignoreValuePattern.Text.Trim();
        r.IgnoreLinePattern = _ignoreLinePattern.Text.Trim();
        r.IncludeFiles = _includeFiles.Lines();
        r.ExcludeFiles = _excludeFiles.Lines();
        r.Example = _example.Text.ReplaceLineEndings("\n");
        return r;
    }

    /// <summary>Runs the rule (as currently typed) over the example text and shows the scrubbed result.</summary>
    private void RunTest()
    {
        var r = ReadRule();
        if (string.IsNullOrWhiteSpace(r.Pattern)) { _status.Text = "Enter a pattern."; _testResult.Text = ""; return; }
        var error = CompiledRule.Validate(r);
        if (error != null)
        {
            _status.ForeColor = Color.Firebrick;
            _status.Text = error;
            _testResult.Text = "";
            return;
        }
        try
        {
            var text = ExampleText.Expand(r.Example).ReplaceLineEndings(Environment.NewLine);
            var detector = new Detector(_profile, [r]);
            var groups = detector.Detect(text);
            var options = _profile.Options;
            var output = new Scrubber(options, new TokenProvider(options)).Apply(text, groups, "", out _);
            _testResult.Text = output;
            _status.ForeColor = groups.Count > 0 ? Color.DarkGreen : Color.DarkOrange;
            _status.Text = groups.Count > 0
                ? $"Pattern OK - {groups.Count} match(es) in the example: " + string.Join(", ", groups.Select(g => "'" + ValueChecks.MaskedPreview(g.Value) + "'"))
                : "Pattern OK - but it does not match the example text.";
        }
        catch (Exception ex)
        {
            _status.ForeColor = Color.Firebrick;
            _status.Text = ex.Message;
        }
    }

    private void Accept()
    {
        var r = ReadRule();
        if (string.IsNullOrWhiteSpace(r.Name)) { Ui.Error(this, "Give the rule a name."); return; }
        if (string.IsNullOrWhiteSpace(r.Id)) r.Id = MakeId(r.Name);
        if (_profile.Rules.Any(x => !ReferenceEquals(x, _original) && string.Equals(x.Id, r.Id, StringComparison.OrdinalIgnoreCase)))
        { Ui.Error(this, $"Another rule already uses the id '{r.Id}'."); return; }
        var error = CompiledRule.Validate(r);
        if (error != null) { Ui.Error(this, error); return; }

        CopyInto(r, _rule);
        DialogResult = DialogResult.OK;
        Close();
    }

    // The rule being edited (so its own id does not count as a duplicate).
    private FilterRule? _original;

    /// <summary>Tells the editor which rule in the profile is being edited.</summary>
    public RuleEditorForm Editing(FilterRule? original)
    {
        _original = original;
        return this;
    }

    private static string MakeId(string name)
    {
        var chars = name.ToLowerInvariant().Select(ch => char.IsAsciiLetterOrDigit(ch) ? ch : '-').ToArray();
        var id = string.Join("-", new string(chars).Split('-', StringSplitOptions.RemoveEmptyEntries));
        return id.Length == 0 ? "custom-rule" : "custom-" + id;
    }

    private static void CopyInto(FilterRule from, FilterRule to)
    {
        foreach (var p in typeof(FilterRule).GetProperties().Where(p => p.CanWrite))
            p.SetValue(to, p.GetValue(from));
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _debounce.Dispose();
        base.Dispose(disposing);
    }
}
