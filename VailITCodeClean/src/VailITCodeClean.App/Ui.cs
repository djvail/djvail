using System.Diagnostics;

namespace VailIT.CodeClean.App;

/// <summary>
/// Small helpers for building the screens in code. The forms in this app are laid out with
/// TableLayoutPanels instead of the Visual Studio designer, so every control is visible and easy to
/// change in plain C#. Layouts grow and shrink with the window and with Windows display scaling.
/// </summary>
internal static class Ui
{
    public static readonly Font Mono = new("Consolas", 9.75f);
    public static readonly Padding Gap = new(3, 3, 3, 3);

    public static Label Label(string text, bool bold = false) => new()
    {
        Text = text,
        AutoSize = true,
        Anchor = AnchorStyles.Left,
        Margin = new Padding(3, 7, 3, 3),
        Font = bold ? new Font(SystemFonts.MessageBoxFont ?? Control.DefaultFont, FontStyle.Bold) : null,
    };

    public static Label Note(string text) => new()
    {
        Text = text,
        AutoSize = true,
        ForeColor = SystemColors.GrayText,
        Margin = new Padding(3, 0, 3, 6),
        MaximumSize = new Size(900, 0),
    };

    public static TextBox Text(string value = "", bool multiline = false, bool mono = false, int height = 0)
    {
        var tb = new TextBox
        {
            Text = value,
            Dock = DockStyle.Fill,
            Margin = Gap,
            Multiline = multiline,
            ScrollBars = multiline ? ScrollBars.Both : ScrollBars.None,
            WordWrap = false,
            AcceptsReturn = multiline,
            Font = mono ? Mono : null,
        };
        if (height > 0) tb.Height = height;
        return tb;
    }

    public static CheckBox Check(string text, bool value = false) => new()
    {
        Text = text,
        Checked = value,
        AutoSize = true,
        Margin = new Padding(3, 4, 12, 4),
    };

    public static Button Button(string text, EventHandler onClick, int minWidth = 90)
    {
        var b = new Button
        {
            Text = text,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowOnly,
            MinimumSize = new Size(minWidth, 28),
            Margin = Gap,
            UseVisualStyleBackColor = true,
        };
        b.Click += onClick;
        return b;
    }

    public static ComboBox Combo<T>(T selected) where T : struct, Enum
    {
        var cb = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Margin = Gap, Width = 160 };
        foreach (var v in Enum.GetValues<T>()) cb.Items.Add(v);
        cb.SelectedItem = selected;
        return cb;
    }

    public static NumericUpDown Number(decimal value, decimal min, decimal max, int decimals = 0, decimal step = 1) => new()
    {
        Minimum = min,
        Maximum = max,
        DecimalPlaces = decimals,
        Increment = step,
        Value = Math.Clamp(value, min, max),
        Width = 100,
        Margin = Gap,
        ThousandsSeparator = decimals == 0,
    };

    /// <summary>A grid that sizes itself to its contents (for use inside group boxes and auto-size rows).</summary>
    public static TableLayoutPanel InnerGrid(params float[] columns)
    {
        var t = Grid(columns);
        t.AutoSize = true;
        t.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        t.Padding = new Padding(0);
        t.Dock = DockStyle.Top;
        return t;
    }

    /// <summary>A grid with the given column widths (use 0 for AutoSize, negative for a percentage share).</summary>
    public static TableLayoutPanel Grid(params float[] columns)
    {
        var t = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = columns.Length, AutoSize = false, Padding = new Padding(6) };
        foreach (var c in columns)
            t.ColumnStyles.Add(c == 0 ? new ColumnStyle(SizeType.AutoSize)
                : c < 0 ? new ColumnStyle(SizeType.Percent, -c)
                : new ColumnStyle(SizeType.Absolute, c));
        return t;
    }

    /// <summary>Adds a row of controls; the row is AutoSize unless <paramref name="percent"/> is given.</summary>
    public static void AddRow(this TableLayoutPanel t, float percent, params Control?[] controls)
    {
        int row = t.RowCount;
        t.RowCount++;
        t.RowStyles.Add(percent > 0 ? new RowStyle(SizeType.Percent, percent) : new RowStyle(SizeType.AutoSize));
        int col = 0;
        foreach (var c in controls)
        {
            if (c != null) t.Controls.Add(c, col, row);
            col++;
        }
    }

    public static void AddRow(this TableLayoutPanel t, params Control?[] controls) => t.AddRow(0, controls);

    /// <summary>Adds one control spanning every column.</summary>
    public static void AddWide(this TableLayoutPanel t, Control control, float percent = 0)
    {
        t.AddRow(percent, control);
        t.SetColumnSpan(control, t.ColumnCount);
    }

    /// <summary>A left-to-right row of controls that wraps when the window is narrow.</summary>
    public static FlowLayoutPanel Flow(params Control[] controls)
    {
        var f = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, WrapContents = true, Margin = new Padding(0) };
        f.Controls.AddRange(controls);
        return f;
    }

    public static GroupBox Group(string title, Control content)
    {
        var g = new GroupBox { Text = title, Dock = DockStyle.Fill, AutoSize = true, Padding = new Padding(6), Margin = new Padding(3, 6, 3, 3) };
        content.Dock = DockStyle.Fill;
        g.Controls.Add(content);
        return g;
    }

    /// <summary>Lines of a multi-line text box (blank lines and surrounding spaces removed).</summary>
    public static List<string> Lines(this TextBox tb) =>
        tb.Text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

    public static void SetLines(this TextBox tb, IEnumerable<string> lines) =>
        tb.Text = string.Join(Environment.NewLine, lines);

    public static void Open(string path)
    {
        try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
        catch (Exception ex) { MessageBox.Show($"Could not open {path}:\n{ex.Message}", "VailIT Code Clean", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
    }

    public static void Error(IWin32Window owner, string message) =>
        MessageBox.Show(owner, message, "VailIT Code Clean", MessageBoxButtons.OK, MessageBoxIcon.Error);

    public static void Info(IWin32Window owner, string message) =>
        MessageBox.Show(owner, message, "VailIT Code Clean", MessageBoxButtons.OK, MessageBoxIcon.Information);

    public static Color SeverityColor(Configuration.Severity s) => s switch
    {
        Configuration.Severity.Critical => Color.FromArgb(255, 205, 205),
        Configuration.Severity.High => Color.FromArgb(255, 222, 196),
        Configuration.Severity.Medium => Color.FromArgb(255, 243, 191),
        Configuration.Severity.Low => Color.FromArgb(214, 234, 255),
        _ => SystemColors.Window,
    };
}
