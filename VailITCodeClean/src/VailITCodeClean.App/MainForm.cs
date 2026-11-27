using VailIT.CodeClean.Configuration;
using VailIT.CodeClean.Samples;

namespace VailIT.CodeClean.App;

/// <summary>
/// The main window. Each tab is built in its own partial file:
/// <list type="bullet">
/// <item><c>MainForm.ScanTab.cs</c>    - folders, options, run button, progress and log</item>
/// <item><c>MainForm.RulesTab.cs</c>   - the rule list (add / edit / enable / import / export)</item>
/// <item><c>MainForm.TestTab.cs</c>    - paste text and see what the rules find</item>
/// <item><c>MainForm.FilesTab.cs</c>   - which files and folders are scanned, and the allow list</item>
/// <item><c>MainForm.ResultsTab.cs</c> - the findings of the last run</item>
/// </list>
/// The profile (options + rules) is kept in <see cref="_profile"/>; the screens copy their values into it
/// before a run and when saving (<see cref="ReadScreen"/>), and from it when a profile is loaded (<see cref="ShowProfile"/>).
/// </summary>
internal sealed partial class MainForm : Form
{
    private readonly AppSettings _settings = AppSettings.Load();
    private CleanProfile _profile = new();
    private string _profilePath = "";

    private readonly TabControl _tabs = new() { Dock = DockStyle.Fill };
    private readonly ToolStripStatusLabel _statusProfile = new() { Spring = true, TextAlign = ContentAlignment.MiddleLeft };
    private readonly ToolStripStatusLabel _statusRules = new();

    public MainForm()
    {
        Text = "VailIT Code Clean";
        AutoScaleMode = AutoScaleMode.Dpi;
        MinimumSize = new Size(900, 640);
        Size = new Size(_settings.WindowWidth > 0 ? _settings.WindowWidth : 1200, _settings.WindowHeight > 0 ? _settings.WindowHeight : 860);
        StartPosition = FormStartPosition.CenterScreen;
        if (_settings.WindowMaximized) WindowState = FormWindowState.Maximized;
        try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { /* no icon is fine */ }

        _tabs.TabPages.Add(BuildScanTab());
        _tabs.TabPages.Add(BuildRulesTab());
        _tabs.TabPages.Add(BuildTestTab());
        _tabs.TabPages.Add(BuildFilesTab());
        _tabs.TabPages.Add(BuildAllowTab());
        _tabs.TabPages.Add(BuildResultsTab());
        _tabs.SelectedIndexChanged += (_, _) => { if (_tabs.SelectedTab == _testTab) { _detectorCache = null; RunTest(); } };

        var status = new StatusStrip();
        status.Items.AddRange([_statusProfile, _statusRules]);

        Controls.Add(_tabs);
        Controls.Add(BuildMenu());
        Controls.Add(status);

        Load += (_, _) => LoadStartupProfile();
        FormClosing += OnClosing;
    }

    // ------------------------------------------------------------------ menu

    private MenuStrip BuildMenu()
    {
        var menu = new MenuStrip();

        var file = new ToolStripMenuItem("&File");
        file.DropDownItems.Add("&New profile (built-in rules)", null, (_, _) => NewProfile());
        file.DropDownItems.Add("&Open profile...", null, (_, _) => OpenProfile());
        file.DropDownItems.Add(new ToolStripMenuItem("&Save profile", null, (_, _) => SaveProfile(_profilePath), Keys.Control | Keys.S));
        file.DropDownItems.Add("Save profile &as...", null, (_, _) => SaveProfileAs());
        file.DropDownItems.Add(new ToolStripSeparator());
        file.DropDownItems.Add("&Import rules...", null, (_, _) => ImportRules());
        file.DropDownItems.Add("&Export rules...", null, (_, _) => ExportRules());
        file.DropDownItems.Add("Add &missing built-in rules", null, (_, _) => AddMissingBuiltIns());
        file.DropDownItems.Add(new ToolStripSeparator());
        file.DropDownItems.Add("Create sample &project...", null, (_, _) => CreateSample());
        file.DropDownItems.Add("&Restore original values...", null, (_, _) => OpenRestore(null));
        file.DropDownItems.Add(new ToolStripSeparator());
        file.DropDownItems.Add("E&xit", null, (_, _) => Close());

        var help = new ToolStripMenuItem("&Help");
        help.DropDownItems.Add("Open &settings folder", null, (_, _) => { Directory.CreateDirectory(AppSettings.Folder); Ui.Open(AppSettings.Folder); });
        help.DropDownItems.Add("&About", null, (_, _) => Ui.Info(this,
            $"VailIT Code Clean {Application.ProductVersion}\n\n" +
            "Finds passwords, keys, tokens, servers, endpoints and personal data in source code and " +
            "writes a scrubbed copy that is safer to share with AI tools and other third parties.\n\n" +
            "Automated scrubbing can miss things: always review the scrubbed copy before sharing it."));

        menu.Items.AddRange([file, help]);
        return menu;
    }

    // ------------------------------------------------------------------ profiles

    private void LoadStartupProfile()
    {
        var path = File.Exists(_settings.LastProfilePath) ? _settings.LastProfilePath : AppSettings.DefaultProfilePath;
        try
        {
            if (File.Exists(path))
            {
                SetProfile(ProfileStore.Load(path), path);
                return;
            }
        }
        catch (Exception ex)
        {
            Ui.Error(this, $"Could not load the profile '{path}':\n{ex.Message}\n\nThe built-in profile will be used instead.");
        }
        // First start: write the built-in profile to %APPDATA% so it can be edited and is remembered.
        var profile = ProfileStore.CreateDefault();
        SetProfile(profile, AppSettings.DefaultProfilePath);
        SaveProfile(AppSettings.DefaultProfilePath, quiet: true);
    }

    private void SetProfile(CleanProfile profile, string path)
    {
        _profile = profile;
        _profilePath = path;
        ShowProfile();
        UpdateStatus();
    }

    /// <summary>Copies the profile into every screen.</summary>
    private void ShowProfile()
    {
        ShowScanOptions();
        ShowFileOptions();
        ShowAllowList();
        FillRules();
        _detectorCache = null;
    }

    /// <summary>Copies every screen back into the profile.</summary>
    private void ReadScreen()
    {
        ReadScanOptions();
        ReadFileOptions();
        ReadAllowList();
    }

    private void UpdateStatus()
    {
        _statusProfile.Text = $"Profile: {_profile.Name}  ({_profilePath})";
        _statusRules.Text = $"{_profile.Rules.Count(r => r.Enabled)} of {_profile.Rules.Count} rules enabled";
    }

    private void NewProfile()
    {
        if (MessageBox.Show(this, "Start a new profile with all built-in rules and default options?\nYour current profile file is not changed until you save.",
                "New profile", MessageBoxButtons.OKCancel, MessageBoxIcon.Question) != DialogResult.OK) return;
        ReadScreen();
        var profile = ProfileStore.CreateDefault();
        profile.Options.SourceFolder = _profile.Options.SourceFolder;
        profile.Options.OutputFolder = _profile.Options.OutputFolder;
        SetProfile(profile, _profilePath);
    }

    private void OpenProfile()
    {
        using var dlg = new OpenFileDialog { Filter = "Profiles|*.profile.json;*.json|All files|*.*", InitialDirectory = AppSettings.Folder };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        try { SetProfile(ProfileStore.Load(dlg.FileName), dlg.FileName); }
        catch (Exception ex) { Ui.Error(this, $"Could not open the profile:\n{ex.Message}"); }
    }

    private bool SaveProfile(string path, bool quiet = false)
    {
        if (string.IsNullOrWhiteSpace(path)) return SaveProfileAs();
        try
        {
            ReadScreen();
            ProfileStore.Save(_profile, path);
            _profilePath = path;
            _settings.LastProfilePath = path;
            _settings.Save();
            UpdateStatus();
            if (!quiet) _statusProfile.Text = $"Saved {path}";
            return true;
        }
        catch (Exception ex)
        {
            Ui.Error(this, $"Could not save the profile:\n{ex.Message}");
            return false;
        }
    }

    private bool SaveProfileAs()
    {
        using var dlg = new SaveFileDialog
        {
            Filter = "Profiles|*.profile.json",
            FileName = SafeFileName(_profile.Name) + ".profile.json",
            InitialDirectory = AppSettings.Folder,
        };
        if (dlg.ShowDialog(this) != DialogResult.OK) return false;
        var name = Path.GetFileName(dlg.FileName).Replace(".profile.json", "", StringComparison.OrdinalIgnoreCase).Replace(".json", "");
        _profile.Name = name;
        return SaveProfile(dlg.FileName);
    }

    private static string SafeFileName(string name) =>
        string.Concat(name.Select(ch => Path.GetInvalidFileNameChars().Contains(ch) ? '_' : ch));

    private void OnClosing(object? sender, FormClosingEventArgs e)
    {
        if (_running)
        {
            if (MessageBox.Show(this, "A scan is still running. Stop it and exit?", "VailIT Code Clean", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes)
            {
                e.Cancel = true;
                return;
            }
            _cts?.Cancel();
        }
        // The profile is saved automatically so folders, options and rule changes are kept.
        SaveProfile(_profilePath, quiet: true);
        _settings.WindowMaximized = WindowState == FormWindowState.Maximized;
        if (WindowState == FormWindowState.Normal) { _settings.WindowWidth = Width; _settings.WindowHeight = Height; }
        _settings.Save();
    }

    // ------------------------------------------------------------------ tools

    private void CreateSample()
    {
        using var dlg = new FolderBrowserDialog { UseDescriptionForTitle = true, Description = "Where should the sample project be created? A sub folder 'VailIT-Sample' is made." };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        var folder = Path.Combine(dlg.SelectedPath, "VailIT-Sample");
        try
        {
            SampleProject.Create(folder);
            _source.Text = folder;
            if (string.IsNullOrWhiteSpace(_output.Text)) _output.Text = Path.Combine(dlg.SelectedPath, "VailIT-Sample-Output");
            _tabs.SelectedTab = _scanTab;
            Ui.Info(this, $"A fake project full of planted (fake) secrets was created in\n{folder}\n\nIt is now selected as the source folder: press Start to see what Code Clean does.");
        }
        catch (Exception ex) { Ui.Error(this, ex.Message); }
    }

    private void OpenRestore(string? mapping)
    {
        using var form = new RestoreForm(mapping);
        form.ShowDialog(this);
    }
}
