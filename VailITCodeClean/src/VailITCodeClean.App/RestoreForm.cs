using VailIT.CodeClean.Engine;

namespace VailIT.CodeClean.App;

/// <summary>
/// Puts original values back into code that was produced from a scrubbed copy (for example code an AI
/// wrote or changed), using the mapping.json file of the run.
/// </summary>
internal sealed class RestoreForm : Form
{
    private readonly TextBox _mapping = Ui.Text();
    private readonly TextBox _input = Ui.Text();
    private readonly TextBox _output = Ui.Text();
    private readonly TextBox _pasteIn = Ui.Text(multiline: true, mono: true);
    private readonly TextBox _pasteOut = Ui.Text(multiline: true, mono: true);

    public RestoreForm(string? mappingPath = null)
    {
        Text = "Restore original values";
        StartPosition = FormStartPosition.CenterParent;
        AutoScaleMode = AutoScaleMode.Dpi;
        Size = new Size(900, 700);
        MinimumSize = new Size(640, 480);
        ShowInTaskbar = false;
        _mapping.Text = mappingPath ?? "";
        _pasteOut.ReadOnly = true;

        var grid = Ui.Grid(0, -100, 0);
        grid.AddWide(Ui.Note("Replaces VAILIT_... placeholders with the real values from a run's Report\\mapping.json " +
                             "(only written when 'Write mapping file' was on). The result contains your secrets again: keep it private."));
        grid.AddRow(Ui.Label("Mapping file"), _mapping, Ui.Button("Browse...", (_, _) => BrowseFile(_mapping, "Mapping file|mapping.json;*.json")));
        grid.AddRow(Ui.Label("Input file or folder"), _input, Ui.Flow(
            Ui.Button("File...", (_, _) => BrowseFile(_input, "All files|*.*")),
            Ui.Button("Folder...", (_, _) => BrowseFolder(_input))));
        grid.AddRow(Ui.Label("Write restored copy to"), _output, Ui.Button("Folder...", (_, _) => BrowseFolder(_output)));
        grid.AddRow(null, Ui.Button("Restore files", (_, _) => RestoreFiles(), 140));

        grid.AddWide(Ui.Label("Or paste text (e.g. an AI answer) and restore it here:", bold: true));
        var split = new SplitContainer { Dock = DockStyle.Fill, Orientation = Orientation.Vertical };
        split.Panel1.Controls.Add(_pasteIn);
        split.Panel2.Controls.Add(_pasteOut);
        grid.AddWide(split, 100);
        grid.AddRow(null, Ui.Flow(
            Ui.Button("Restore text", (_, _) => RestoreText(), 120),
            Ui.Button("Copy result", (_, _) => { if (_pasteOut.TextLength > 0) Clipboard.SetText(_pasteOut.Text); }, 120)));

        Controls.Add(grid);
    }

    private Restorer? LoadRestorer()
    {
        if (!File.Exists(_mapping.Text)) { Ui.Error(this, "Choose the mapping.json file of the run."); return null; }
        try { return new Restorer(MappingFile.Load(_mapping.Text)); }
        catch (Exception ex) { Ui.Error(this, "Could not read the mapping file: " + ex.Message); return null; }
    }

    private void RestoreFiles()
    {
        var restorer = LoadRestorer();
        if (restorer == null) return;
        if (string.IsNullOrWhiteSpace(_input.Text) || string.IsNullOrWhiteSpace(_output.Text)) { Ui.Error(this, "Choose the input and the output."); return; }
        try
        {
            Cursor = Cursors.WaitCursor;
            var (files, replaced) = restorer.RestorePath(_input.Text, _output.Text);
            Ui.Info(this, $"Restored {replaced:N0} placeholder(s) in {files:N0} file(s).\n\nOutput: {_output.Text}");
        }
        catch (Exception ex) { Ui.Error(this, ex.Message); }
        finally { Cursor = Cursors.Default; }
    }

    private void RestoreText()
    {
        var restorer = LoadRestorer();
        if (restorer == null) return;
        _pasteOut.Text = restorer.Restore(_pasteIn.Text, out var n);
        Text = $"Restore original values - {n} placeholder(s) restored";
    }

    private void BrowseFile(TextBox target, string filter)
    {
        using var dlg = new OpenFileDialog { Filter = filter, CheckFileExists = true };
        if (dlg.ShowDialog(this) == DialogResult.OK) target.Text = dlg.FileName;
    }

    private void BrowseFolder(TextBox target)
    {
        using var dlg = new FolderBrowserDialog { UseDescriptionForTitle = true, Description = "Choose a folder" };
        if (Directory.Exists(target.Text)) dlg.InitialDirectory = target.Text;
        if (dlg.ShowDialog(this) == DialogResult.OK) target.Text = dlg.SelectedPath;
    }
}
