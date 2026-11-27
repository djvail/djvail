using System.Text.Json;

namespace VailITHeartbeat;

/// <summary>Loads and saves <see cref="TargetState"/> per target URL as a small JSON file.</summary>
public sealed class StateStore
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };
    private readonly string _path;
    private readonly ILogger<StateStore> _log;

    public StateStore(string path, ILogger<StateStore> log)
    {
        _path = Path.IsPathRooted(path) ? path : Path.Combine(AppContext.BaseDirectory, path);
        _log = log;
    }

    public Dictionary<string, TargetState> Load()
    {
        try
        {
            if (File.Exists(_path))
                return JsonSerializer.Deserialize<Dictionary<string, TargetState>>(File.ReadAllText(_path), Json) ?? [];
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            _log.LogWarning(ex, "Could not read state file {Path}; starting fresh.", _path);
        }
        return [];
    }

    public void Save(Dictionary<string, TargetState> states)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var temp = _path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(states, Json));
            File.Move(temp, _path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.LogError(ex, "Could not save state file {Path}.", _path);
        }
    }
}
