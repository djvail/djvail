namespace VailIT.CodeClean.Configuration;

/// <summary>
/// A complete, saveable configuration: options, rules and the global allow list.
/// Profiles are plain JSON files (<c>*.profile.json</c>) so they can be edited by hand,
/// kept in source control and shared with a team.
/// </summary>
public sealed class CleanProfile
{
    /// <summary>Format version of the file, for future upgrades.</summary>
    public int FormatVersion { get; set; } = 1;

    public string Name { get; set; } = "Default";

    public string Description { get; set; } = "";

    public ScanOptions Options { get; set; } = new();

    public List<FilterRule> Rules { get; set; } = [];

    /// <summary>Values that are never scrubbed by any rule (exact, case-insensitive), e.g. <c>localhost</c>.</summary>
    public List<string> AllowValues { get; set; } = [];

    /// <summary>Regular expressions: any value matching one of these is never scrubbed by any rule.</summary>
    public List<string> AllowPatterns { get; set; } = [];
}
