using System.Text.Json;
using System.Text.Json.Serialization;
using VailIT.CodeClean.Rules;

namespace VailIT.CodeClean.Configuration;

/// <summary>Loads and saves profiles and rule packs as JSON.</summary>
public static class ProfileStore
{
    /// <summary>JSON settings used everywhere: indented, enums as text, comments and trailing commas allowed when reading.</summary>
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static CleanProfile Load(string path)
    {
        var json = File.ReadAllText(path);
        var profile = JsonSerializer.Deserialize<CleanProfile>(json, JsonOptions)
                      ?? throw new InvalidDataException($"'{path}' does not contain a profile.");
        Normalise(profile);
        return profile;
    }

    public static void Save(CleanProfile profile, string path)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(path, JsonSerializer.Serialize(profile, JsonOptions));
    }

    /// <summary>
    /// Reads rules from a file. Accepts either a full profile (its rules are used) or a plain JSON
    /// array of rules (a "rule pack").
    /// </summary>
    public static List<FilterRule> LoadRules(string path)
    {
        var json = File.ReadAllText(path).TrimStart();
        if (json.StartsWith('['))
            return JsonSerializer.Deserialize<List<FilterRule>>(json, JsonOptions) ?? [];
        return Load(path).Rules;
    }

    /// <summary>Writes a plain JSON array of rules (a rule pack) that can be imported into any profile.</summary>
    public static void SaveRules(IEnumerable<FilterRule> rules, string path)
        => File.WriteAllText(path, JsonSerializer.Serialize(rules.ToList(), JsonOptions));

    /// <summary>
    /// Adds rules to a profile. A rule whose id already exists replaces the old one when
    /// <paramref name="replaceExisting"/> is true, otherwise it gets a new id.
    /// Returns how many rules were added or replaced.
    /// </summary>
    public static int MergeRules(CleanProfile profile, IEnumerable<FilterRule> rules, bool replaceExisting)
    {
        int count = 0;
        foreach (var rule in rules)
        {
            var index = profile.Rules.FindIndex(r => string.Equals(r.Id, rule.Id, StringComparison.OrdinalIgnoreCase));
            if (index >= 0 && replaceExisting)
            {
                profile.Rules[index] = rule;
            }
            else
            {
                if (index >= 0 || string.IsNullOrWhiteSpace(rule.Id)) rule.Id = UniqueId(profile, rule.Id);
                profile.Rules.Add(rule);
            }
            count++;
        }
        return count;
    }

    /// <summary>Returns an id based on <paramref name="wanted"/> that no rule in the profile uses yet.</summary>
    public static string UniqueId(CleanProfile profile, string wanted)
    {
        var baseId = string.IsNullOrWhiteSpace(wanted) ? "custom-rule" : wanted.Trim();
        var id = baseId;
        for (int n = 2; profile.Rules.Any(r => string.Equals(r.Id, id, StringComparison.OrdinalIgnoreCase)); n++)
            id = $"{baseId}-{n}";
        return id;
    }

    /// <summary>The built-in profile with every sample rule (see the <c>Rules</c> folder).</summary>
    public static CleanProfile CreateDefault() => DefaultProfile.Create();

    /// <summary>Fills in anything missing from an older or hand-written file so the rest of the code can rely on it.</summary>
    private static void Normalise(CleanProfile profile)
    {
        profile.Options ??= new ScanOptions();
        profile.Rules ??= [];
        profile.AllowValues ??= [];
        profile.AllowPatterns ??= [];
        var o = profile.Options;
        o.ExcludedFolders ??= [];
        o.IncludeFilePatterns ??= [];
        o.ExcludedFilePatterns ??= [];
        o.SensitiveFilePatterns ??= [];
        foreach (var r in profile.Rules)
        {
            r.IgnoreValues ??= [];
            r.IncludeFiles ??= [];
            r.ExcludeFiles ??= [];
            r.Pattern ??= "";
            r.IgnoreLinePattern ??= "";
            r.IgnoreValuePattern ??= "";
            r.TokenLabel ??= "";
            if (string.IsNullOrWhiteSpace(r.Id)) r.Id = UniqueId(profile, r.Name.ToLowerInvariant().Replace(' ', '-'));
        }
    }
}
