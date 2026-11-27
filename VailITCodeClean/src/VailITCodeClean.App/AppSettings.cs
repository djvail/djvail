using System.Text.Json;
using VailIT.CodeClean.Configuration;

namespace VailIT.CodeClean.App;

/// <summary>
/// Remembers which profile was used last and the window size. Stored in
/// <c>%APPDATA%\VailIT\CodeClean\app-settings.json</c>. Your rules and options live in the profile file itself.
/// </summary>
internal sealed class AppSettings
{
    public static string Folder { get; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "VailIT", "CodeClean");
    public static string DefaultProfilePath { get; } = Path.Combine(Folder, "Default.profile.json");
    private static string FilePath => Path.Combine(Folder, "app-settings.json");

    public string LastProfilePath { get; set; } = "";
    public int WindowWidth { get; set; }
    public int WindowHeight { get; set; }
    public bool WindowMaximized { get; set; }

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), ProfileStore.JsonOptions) ?? new();
        }
        catch (Exception) { /* a broken settings file should never stop the app starting */ }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Folder);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, ProfileStore.JsonOptions));
        }
        catch (Exception) { /* not important enough to bother the user */ }
    }
}
