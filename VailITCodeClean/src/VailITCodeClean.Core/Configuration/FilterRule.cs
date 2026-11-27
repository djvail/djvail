namespace VailIT.CodeClean.Configuration;

/// <summary>
/// One thing to look for. A profile holds a list of these; every enabled rule is run against
/// every scanned file.
/// </summary>
/// <remarks>
/// <para><b>Regex rules and the "secret" group.</b> If the regular expression contains a named group
/// called <c>secret</c>, only that part of the match is replaced. For example
/// <c>password\s*=\s*"(?&lt;secret&gt;[^"]+)"</c> keeps <c>password = "</c> and replaces just the value.
/// Several groups can be used: any group whose name starts with <c>secret_</c> is replaced too, and the
/// text after the underscore becomes that value's token label (<c>secret_USERNAME</c> gives
/// <c>VAILIT_USERNAME_0001</c>). With no secret groups the whole match is replaced.</para>
/// <para><b>Exact rules.</b> The pattern is literal text. Put several strings on separate lines to
/// search for all of them with one rule.</para>
/// </remarks>
public sealed class FilterRule
{
    /// <summary>Short unique id, e.g. <c>aws-access-key-id</c>. Shown in the report.</summary>
    public string Id { get; set; } = "";

    /// <summary>Friendly name shown in the app and the report.</summary>
    public string Name { get; set; } = "";

    /// <summary>Grouping used in the app and the report, e.g. <c>Credentials</c>, <c>Infrastructure</c>.</summary>
    public string Category { get; set; } = "Custom";

    /// <summary>Free text explaining what the rule is for.</summary>
    public string Description { get; set; } = "";

    public bool Enabled { get; set; } = true;

    public PatternType Type { get; set; } = PatternType.Regex;

    /// <summary>The regular expression, or for <see cref="PatternType.Exact"/> one or more literal strings (one per line).</summary>
    public string Pattern { get; set; } = "";

    /// <summary>When false (the default) upper and lower case are treated the same.</summary>
    public bool CaseSensitive { get; set; }

    /// <summary>Exact rules only: the text must not be part of a longer word (e.g. <c>Contoso</c> does not match <c>ContosoApp</c>).</summary>
    public bool WholeWord { get; set; }

    public Severity Severity { get; set; } = Severity.Medium;

    /// <summary>
    /// The word used in the placeholder, e.g. <c>PASSWORD</c> gives <c>VAILIT_PASSWORD_0001</c>.
    /// Leave empty to use the category name.
    /// </summary>
    public string TokenLabel { get; set; } = "";

    /// <summary>Replace just the match or the whole line.</summary>
    public ReplaceScope Scope { get; set; } = ReplaceScope.Match;

    /// <summary>Values shorter than this are ignored (0 = no minimum).</summary>
    public int MinLength { get; set; }

    /// <summary>
    /// Minimum Shannon entropy (bits per character) the value must have, 0 = off.
    /// Random keys score roughly 3.5 to 6; English words and placeholders score lower.
    /// </summary>
    public double MinEntropy { get; set; }

    public ValueValidator Validator { get; set; } = ValueValidator.None;

    /// <summary>
    /// For regex rules with several <c>secret_...</c> groups: when one part is ignored (by the ignore lists
    /// or the global allow list), ignore the whole match. Example: in <c>\\server\share</c>, if "server" is a
    /// placeholder name, the share next to it is not interesting either.
    /// </summary>
    public bool AllPartsRequired { get; set; }

    /// <summary>Values that are never treated as findings for this rule (exact, case-insensitive).</summary>
    public List<string> IgnoreValues { get; set; } = [];

    /// <summary>Optional regex: if the value matches it, the finding is ignored (e.g. placeholders such as <c>${VAR}</c>).</summary>
    public string IgnoreValuePattern { get; set; } = "";

    /// <summary>Optional regex: if the line containing the match matches it, the finding is ignored (e.g. <c>AssemblyVersion</c> for IP rules).</summary>
    public string IgnoreLinePattern { get; set; } = "";

    /// <summary>When not empty, the rule only runs on files matching one of these patterns (e.g. <c>*.config</c>, <c>**/appsettings*.json</c>).</summary>
    public List<string> IncludeFiles { get; set; } = [];

    /// <summary>The rule never runs on files matching one of these patterns.</summary>
    public List<string> ExcludeFiles { get; set; } = [];

    /// <summary>
    /// Sample text that this rule should catch. Used by the rule tester in the app and by the unit tests.
    /// Fake secrets can be written as <c>{{ALNUM:36}}</c>, <c>{{HEX:32}}</c>, <c>{{DIGITS:12}}</c> etc.
    /// (see <see cref="Engine.ExampleText"/>) so that no real-looking key has to be stored in the file.
    /// </summary>
    public string Example { get; set; } = "";

    /// <summary>Makes a copy (used by the editor so Cancel really cancels).</summary>
    public FilterRule Clone()
    {
        var copy = (FilterRule)MemberwiseClone();
        copy.IgnoreValues = [.. IgnoreValues];
        copy.IncludeFiles = [.. IncludeFiles];
        copy.ExcludeFiles = [.. ExcludeFiles];
        return copy;
    }

    public override string ToString() => string.IsNullOrWhiteSpace(Name) ? Id : Name;
}
