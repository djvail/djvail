using VailIT.CodeClean.Configuration;
using VailIT.CodeClean.Rules;

namespace VailIT.CodeClean.App;

internal sealed partial class MainForm
{
    private TabPage _rulesTab = null!;
    private readonly DataGridView _rulesGrid = new()
    {
        Dock = DockStyle.Fill,
        AllowUserToAddRows = false,
        AllowUserToDeleteRows = false,
        AllowUserToResizeRows = false,
        SelectionMode = DataGridViewSelectionMode.FullRowSelect,
        MultiSelect = true,
        RowHeadersVisible = false,
        AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.None,
        BackgroundColor = SystemColors.Window,
        BorderStyle = BorderStyle.None,
    };
    private readonly TextBox _ruleFilter = new() { Width = 260, Margin = Ui.Gap, PlaceholderText = "Search name, id, pattern..." };
    private readonly ComboBox _categoryFilter = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 200, Margin = Ui.Gap };
    private readonly Label _ruleDescription = Ui.Note("");

    private TabPage BuildRulesTab()
    {
        _rulesTab = new TabPage("Rules") { UseVisualStyleBackColor = true };

        _rulesGrid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "On", HeaderText = "On", Width = 40 });
        AddTextColumn("Severity", 75);
        AddTextColumn("Category", 140);
        AddTextColumn("Name", 280);
        AddTextColumn("Type", 60);
        AddTextColumn("Label", 150);
        AddTextColumn("Pattern", 400, fill: true);
        _rulesGrid.Columns["Pattern"]!.DefaultCellStyle.Font = Ui.Mono;

        _rulesGrid.CellDoubleClick += (_, e) => { if (e.RowIndex >= 0 && e.ColumnIndex != 0) EditSelectedRule(); };
        _rulesGrid.CurrentCellDirtyStateChanged += (_, _) =>
        {
            if (_rulesGrid.IsCurrentCellDirty && _rulesGrid.CurrentCell is DataGridViewCheckBoxCell) _rulesGrid.CommitEdit(DataGridViewDataErrorContexts.Commit);
        };
        _rulesGrid.CellValueChanged += (_, e) =>
        {
            if (e.RowIndex < 0 || e.ColumnIndex != 0) return;
            var row = _rulesGrid.Rows[e.RowIndex];
            if (row.Tag is FilterRule rule) { rule.Enabled = row.Cells[0].Value is true; RulesChanged(refill: false); StyleRow(row); }
        };
        _rulesGrid.SelectionChanged += (_, _) =>
        {
            var r = SelectedRules().FirstOrDefault();
            _ruleDescription.Text = r == null ? "" : $"{r.Name} [{r.Id}]: {r.Description}";
        };
        _rulesGrid.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Enter) { EditSelectedRule(); e.Handled = true; }
            if (e.KeyCode == Keys.Delete) { DeleteSelectedRules(); e.Handled = true; }
        };

        _ruleFilter.TextChanged += (_, _) => FillRules();
        _categoryFilter.SelectedIndexChanged += (_, _) => FillRules();

        var toolbar = Ui.Flow(
            _ruleFilter, _categoryFilter,
            Ui.Button("Add...", (_, _) => AddRule(), 70),
            Ui.Button("Edit...", (_, _) => EditSelectedRule(), 70),
            Ui.Button("Duplicate", (_, _) => DuplicateRule(), 80),
            Ui.Button("Delete", (_, _) => DeleteSelectedRules(), 70),
            Ui.Button("Enable shown", (_, _) => SetShownEnabled(true), 100),
            Ui.Button("Disable shown", (_, _) => SetShownEnabled(false), 100),
            Ui.Button("Test selected", (_, _) => { _testOnlySelected.Checked = true; _tabs.SelectedTab = _testTab; }, 100));

        var grid = Ui.Grid(-100);
        grid.AddRow(toolbar);
        grid.AddRow(Ui.Note("Double-click a rule to edit it. Tick/untick 'On' to switch rules on or off. " +
                            "Changes are saved in your profile (File > Save profile, or automatically on exit)."));
        grid.AddRow(100, _rulesGrid);
        grid.AddRow(_ruleDescription);
        _rulesTab.Controls.Add(grid);
        return _rulesTab;
    }

    private void AddTextColumn(string name, int width, bool fill = false)
    {
        var col = new DataGridViewTextBoxColumn
        {
            Name = name,
            HeaderText = name,
            Width = width,
            ReadOnly = true,
            SortMode = DataGridViewColumnSortMode.Automatic,
        };
        if (fill) col.AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
        _rulesGrid.Columns.Add(col);
    }

    /// <summary>Rebuilds the rule list, applying the search box and category filter.</summary>
    private void FillRules()
    {
        // keep the category list up to date
        var current = _categoryFilter.SelectedItem as string ?? "(all categories)";
        var categories = new List<string> { "(all categories)", "(enabled only)", "(disabled only)" };
        categories.AddRange(_profile.Rules.Select(r => r.Category).Distinct().OrderBy(c => c));
        if (!_categoryFilter.Items.Cast<string>().SequenceEqual(categories))
        {
            _categoryFilter.Items.Clear();
            _categoryFilter.Items.AddRange(categories.Cast<object>().ToArray());
            _categoryFilter.SelectedItem = categories.Contains(current) ? current : categories[0];
            return; // SelectedIndexChanged calls FillRules again
        }

        var q = _ruleFilter.Text.Trim();
        var cat = _categoryFilter.SelectedItem as string ?? "(all categories)";
        _rulesGrid.SuspendLayout();
        _rulesGrid.Rows.Clear();
        foreach (var r in _profile.Rules)
        {
            if (cat == "(enabled only)" && !r.Enabled) continue;
            if (cat == "(disabled only)" && r.Enabled) continue;
            if (!cat.StartsWith('(') && r.Category != cat) continue;
            if (q.Length > 0 && !(r.Name.Contains(q, StringComparison.OrdinalIgnoreCase) || r.Id.Contains(q, StringComparison.OrdinalIgnoreCase)
                                 || r.Pattern.Contains(q, StringComparison.OrdinalIgnoreCase) || r.Description.Contains(q, StringComparison.OrdinalIgnoreCase)
                                 || r.TokenLabel.Contains(q, StringComparison.OrdinalIgnoreCase)))
                continue;
            var pattern = r.Pattern.Replace("\r", "").Replace('\n', '|');
            if (pattern.Length > 300) pattern = pattern[..300] + "…";
            int i = _rulesGrid.Rows.Add(r.Enabled, r.Severity.ToString(), r.Category, r.Name, r.Type.ToString(),
                string.IsNullOrEmpty(r.TokenLabel) ? "(category)" : r.TokenLabel, pattern);
            var row = _rulesGrid.Rows[i];
            row.Tag = r;
            StyleRow(row);
        }
        _rulesGrid.ResumeLayout();
        UpdateStatus();
    }

    private static void StyleRow(DataGridViewRow row)
    {
        if (row.Tag is not FilterRule r) return;
        row.DefaultCellStyle.ForeColor = r.Enabled ? SystemColors.WindowText : SystemColors.GrayText;
        row.Cells[1].Style.BackColor = r.Enabled ? Ui.SeverityColor(r.Severity) : SystemColors.Window;
    }

    private List<FilterRule> SelectedRules() =>
        _rulesGrid.SelectedRows.Cast<DataGridViewRow>().OrderBy(r => r.Index).Select(r => r.Tag).OfType<FilterRule>().ToList();

    private void RulesChanged(bool refill = true)
    {
        _detectorCache = null;
        if (refill) FillRules(); else UpdateStatus();
    }

    private void AddRule()
    {
        var rule = new FilterRule
        {
            Id = "",
            Name = "",
            Category = _categoryFilter.SelectedItem is string c && !c.StartsWith('(') ? c : "Custom",
            Type = PatternType.Exact,
            Severity = Severity.High,
            Description = "",
        };
        using var form = new RuleEditorForm(rule, _profile, isNew: true).Editing(null);
        if (form.ShowDialog(this) != DialogResult.OK) return;
        _profile.Rules.Add(form.Result);
        RulesChanged();
        SelectRule(form.Result);
    }

    private void EditSelectedRule()
    {
        var rule = SelectedRules().FirstOrDefault();
        if (rule == null) return;
        using var form = new RuleEditorForm(rule, _profile, isNew: false).Editing(rule);
        if (form.ShowDialog(this) != DialogResult.OK) return;
        var index = _profile.Rules.IndexOf(rule);
        _profile.Rules[index] = form.Result;
        RulesChanged();
        SelectRule(form.Result);
    }

    private void DuplicateRule()
    {
        var rule = SelectedRules().FirstOrDefault();
        if (rule == null) return;
        var copy = rule.Clone();
        copy.Id = ProfileStore.UniqueId(_profile, rule.Id + "-copy");
        copy.Name = rule.Name + " (copy)";
        _profile.Rules.Insert(_profile.Rules.IndexOf(rule) + 1, copy);
        RulesChanged();
        SelectRule(copy);
    }

    private void DeleteSelectedRules()
    {
        var rules = SelectedRules();
        if (rules.Count == 0) return;
        var what = rules.Count == 1 ? $"the rule '{rules[0].Name}'" : $"{rules.Count} rules";
        if (MessageBox.Show(this, $"Delete {what}?\n\nTip: unticking 'On' switches a rule off without deleting it.", "Delete",
                MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
        foreach (var r in rules) _profile.Rules.Remove(r);
        RulesChanged();
    }

    private void SetShownEnabled(bool enabled)
    {
        foreach (DataGridViewRow row in _rulesGrid.Rows)
            if (row.Tag is FilterRule r) r.Enabled = enabled;
        RulesChanged();
    }

    private void SelectRule(FilterRule rule)
    {
        foreach (DataGridViewRow row in _rulesGrid.Rows)
        {
            if (!ReferenceEquals(row.Tag, rule)) continue;
            _rulesGrid.ClearSelection();
            row.Selected = true;
            _rulesGrid.CurrentCell = row.Cells[3];
            return;
        }
    }

    private void ImportRules()
    {
        using var dlg = new OpenFileDialog { Filter = "Rule packs or profiles|*.json|All files|*.*", Title = "Import rules" };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            var rules = ProfileStore.LoadRules(dlg.FileName);
            var answer = MessageBox.Show(this,
                $"{rules.Count} rule(s) found.\n\nYes = rules with the same id REPLACE yours\nNo = add them all as new rules\nCancel = do nothing",
                "Import rules", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
            if (answer == DialogResult.Cancel) return;
            var n = ProfileStore.MergeRules(_profile, rules, replaceExisting: answer == DialogResult.Yes);
            RulesChanged();
            Ui.Info(this, $"{n} rule(s) imported.");
        }
        catch (Exception ex) { Ui.Error(this, "Could not import: " + ex.Message); }
    }

    private void ExportRules()
    {
        var selected = SelectedRules();
        var rules = selected.Count > 1 ? selected : _profile.Rules;
        using var dlg = new SaveFileDialog { Filter = "Rule pack|*.rules.json", FileName = selected.Count > 1 ? "selected.rules.json" : "all.rules.json", Title = selected.Count > 1 ? $"Export {selected.Count} selected rules" : "Export all rules" };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        try { ProfileStore.SaveRules(rules, dlg.FileName); Ui.Info(this, $"{rules.Count} rule(s) exported to {dlg.FileName}."); }
        catch (Exception ex) { Ui.Error(this, "Could not export: " + ex.Message); }
    }

    /// <summary>Adds built-in rules that are not in the profile (e.g. after updating the app). Existing rules are not touched.</summary>
    private void AddMissingBuiltIns()
    {
        var missing = DefaultRules.All().Where(b => !_profile.Rules.Any(r => string.Equals(r.Id, b.Id, StringComparison.OrdinalIgnoreCase))).ToList();
        if (missing.Count == 0) { Ui.Info(this, "Your profile already has every built-in rule."); return; }
        _profile.Rules.AddRange(missing);
        RulesChanged();
        Ui.Info(this, $"Added {missing.Count} built-in rule(s):\n\n" + string.Join("\n", missing.Take(30).Select(r => "• " + r.Name)) + (missing.Count > 30 ? "\n..." : ""));
    }
}
