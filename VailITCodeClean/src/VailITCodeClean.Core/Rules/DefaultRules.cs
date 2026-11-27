using VailIT.CodeClean.Configuration;

namespace VailIT.CodeClean.Rules;

/// <summary>
/// The sample rules that ship with VailIT Code Clean.
/// </summary>
/// <remarks>
/// <para>The rules are split by subject into the other <c>DefaultRules.*.cs</c> files in this folder.
/// To change a built-in rule, edit it there; to add your own, either add one to
/// <c>DefaultRules.Custom.cs</c> or (easier) use the Rules tab in the app, which saves to your profile file.</para>
/// <para>Patterns are written as C# raw strings (<c>"""..."""</c>) so regular expressions can be pasted
/// in exactly as they are, with no extra escaping.</para>
/// <para>Rule order matters a little: when two rules match exactly the same text, the one with the higher
/// severity wins, and if that is equal the one that comes first wins.</para>
/// </remarks>
public static partial class DefaultRules
{
    /// <summary>Every built-in rule, in order.</summary>
    public static List<FilterRule> All() =>
    [
        .. KeysAndCertificates(),
        .. CloudProviders(),
        .. ServiceTokens(),
        .. Credentials(),
        .. Infrastructure(),
        .. Endpoints(),
        .. PersonalData(),
        .. Custom(),
    ];

    // Category names, so they are spelled the same everywhere.
    public const string CatKeys = "Keys & Certificates";
    public const string CatCloud = "Cloud Providers";
    public const string CatTokens = "Service Tokens";
    public const string CatCredentials = "Credentials";
    public const string CatInfrastructure = "Infrastructure";
    public const string CatEndpoints = "Endpoints";
    public const string CatPersonal = "Personal Data";
    public const string CatCustom = "Custom (examples)";

    /// <summary>
    /// Values that are obviously not real secrets: references to variables or settings
    /// (<c>${DB_PASS}</c>, <c>%PASSWORD%</c>, <c>{{ token }}</c>, <c>&lt;your-key&gt;</c>), empty-ish
    /// values and common placeholders. Used as <see cref="FilterRule.IgnoreValuePattern"/> by the
    /// assignment rules so they do not flag code like <c>password: ${DB_PASSWORD}</c>.
    /// </summary>
    public const string PlaceholderValue =
        """(?i)^(?:\$\{[^}]*\}|\$\([^)]*\)|\$[A-Za-z_]\w*|%[A-Za-z_]\w*%|\{\{.*\}\}|\{[A-Za-z_][\w.:\-]*\}|#\{[^}]*\}|<[^>]*>|\[[\w \-]+\]|\*+|x{3,}|\.{3,}|\?+|-+|_+|null|none|nil|undefined|true|false|yes|no|empty|string|secret|password|passwd|changeme|change[_\-]?me|todo|tbd|n/?a|your[_\- ]?[\w\- ]*|my[_\- ]?(?:password|secret|key|token)|(?:env|vault|secret|keyvault|ssm|aws-secretsmanager|azure-keyvault)\s*[:(].*|process\.env\..*|os\.environ.*|getenv.*)$""";

    /// <summary>
    /// Builds a pattern for <c>name = "value"</c> style assignments where the name contains <paramref name="key"/>.
    /// Works for C#, Java, JavaScript/TypeScript, Python, PowerShell, PHP, Go, JSON, YAML (quoted values),
    /// XML/HTML attributes, Terraform and most config formats. The quoted value becomes the <c>secret</c> group.
    /// </summary>
    /// <param name="key">Regex for the meaningful part of the name, e.g. <c>password|pwd</c>.
    /// Anything may come before it (<c>dbPassword</c>, <c>SMTP_PASSWORD</c>) but nothing after it,
    /// so <c>passwordHint</c> or <c>PasswordLength</c> are not matched.</param>
    /// <param name="noSpaces">Values may not contain spaces (good for keys and tokens).</param>
    internal static string QuotedAssignment(string key, bool noSpaces = false)
    {
        var ch = noSpaces ? """[^\\\r\n\s]""" : """[^\\\r\n]""";
        return $$"""(?<![\w$])[\w.\-$]{0,40}?(?:{{key}})(?:["']?\s*(?:=|:=|:|=>|==|!=|\+=)|["']\s*,)\s*(?:@|\$|[rbuf])?(["'`])(?<secret>(?:\\.|(?!\1){{ch}}){1,512}?)\1""";
    }

    /// <summary>
    /// Builds a pattern for unquoted <c>NAME=value</c> / <c>name: value</c> lines as found in .env, .ini,
    /// .properties, YAML and similar files. The value runs to the end of the line (or a <c> #</c> comment).
    /// </summary>
    internal static string LineAssignment(string key) =>
        $$"""(?m)^[ \t]*(?:export[ \t]+|set[ \t]+|-[ \t]+)?[\w.\-]{0,40}?(?:{{key}})[ \t]*(?:=|:)[ \t]*(?<secret>[^\s"'#;][^\r\n]*?)[ \t]*(?:[ \t]#[^\r\n]*)?(?=\r?$)""";

    /// <summary>File types where unquoted <c>key=value</c> settings are normal.</summary>
    internal static readonly string[] ConfigFiles =
    [
        ".env", "*.env", ".env.*", "*.ini", "*.cfg", "*.conf", "*.cnf", "*.properties", "*.yml", "*.yaml",
        "*.toml", "*.tfvars", "*.txt", "*.config", "Dockerfile", "*.dockerfile", ".npmrc", ".pypirc", ".netrc",
        "*.service", "*.desktop", "*.reg", "*.inf", "*.sh", "*.bash", "*.zsh", "*.bat", "*.cmd", "*.ps1", "*.psm1",
        "*.cmake", "*.gradle", "*.sbt", "*.cron", "*.htaccess", "*.vbs",
    ];

    /// <summary>
    /// Generated dependency / lock files. They are full of version numbers and package hashes that look like
    /// IP addresses and keys, so the noisiest rules skip them (URL and token rules still run on them).
    /// </summary>
    internal static readonly string[] LockFiles =
    [
        "*.deps.json", "*.runtimeconfig.json", "packages.lock.json", "project.assets.json", "package-lock.json",
        "npm-shrinkwrap.json", "yarn.lock", "pnpm-lock.yaml", "poetry.lock", "Pipfile.lock", "Cargo.lock",
        "Gemfile.lock", "composer.lock", "go.sum", "*.nuspec", "paket.lock", "pubspec.lock", "Podfile.lock",
    ];

    /// <summary>Version numbers such as 1.2.3, 10.0.0.1 or 7.0.0-preview.1.</summary>
    internal const string VersionValue = """^v?\d+(?:\.\d+){1,3}(?:[-+][\w.\-]+)?$""";

    /// <summary>Values that are file names (e.g. "pkg.1.0.0.nupkg.sha512") rather than secrets.</summary>
    internal const string FileNameValue = """(?i)\.(?:sha\d+|nupkg|snupkg|dll|exe|pdb|json|xml|ya?ml|txt|md|js|ts|css|png|svg|jpe?g|gif|ico|html?|config|props|targets|csproj|sln|zip|tgz|gz|whl|jar)$""";

    /// <summary>Standard pattern for a GUID (used by several Azure rules).</summary>
    internal const string Guid = """[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}""";

    // ------------------------------------------------------------------ small builder helpers
    // They keep the rule lists short and readable. Each returns the rule so calls can be chained.

    internal static FilterRule Rule(string id, string name, string category, Severity severity, string label,
        string pattern, string description, string example = "") => new()
        {
            Id = id,
            Name = name,
            Category = category,
            Severity = severity,
            TokenLabel = label,
            Pattern = pattern,
            Description = description,
            Example = example,
            Type = PatternType.Regex,
        };

    /// <summary>Match upper and lower case exactly (most vendor key formats are case sensitive).</summary>
    internal static FilterRule Cs(this FilterRule r) { r.CaseSensitive = true; return r; }

    /// <summary>Ship the rule switched off (noisy or needs customising first).</summary>
    internal static FilterRule Off(this FilterRule r) { r.Enabled = false; return r; }

    internal static FilterRule MinLen(this FilterRule r, int length) { r.MinLength = length; return r; }

    internal static FilterRule Entropy(this FilterRule r, double bits) { r.MinEntropy = bits; return r; }

    /// <summary>Adds an "ignore values matching this regex" condition (several calls are combined with OR).</summary>
    internal static FilterRule IgnoreValue(this FilterRule r, string regex)
    {
        r.IgnoreValuePattern = string.IsNullOrEmpty(r.IgnoreValuePattern) ? regex : $"(?:{r.IgnoreValuePattern})|(?:{regex})";
        return r;
    }

    internal static FilterRule IgnorePlaceholders(this FilterRule r) => r.IgnoreValue(PlaceholderValue);

    internal static FilterRule IgnoreLine(this FilterRule r, string regex) { r.IgnoreLinePattern = regex; return r; }

    internal static FilterRule Ignore(this FilterRule r, params string[] values) { r.IgnoreValues.AddRange(values); return r; }

    internal static FilterRule Files(this FilterRule r, params string[] globs) { r.IncludeFiles.AddRange(globs); return r; }

    internal static FilterRule NotFiles(this FilterRule r, params string[] globs) { r.ExcludeFiles.AddRange(globs); return r; }

    internal static FilterRule Luhn(this FilterRule r) { r.Validator = ValueValidator.Luhn; return r; }

    internal static FilterRule AllParts(this FilterRule r) { r.AllPartsRequired = true; return r; }

    internal static FilterRule WholeLine(this FilterRule r) { r.Scope = ReplaceScope.Line; return r; }

    internal static FilterRule Exact(this FilterRule r, bool wholeWord = true) { r.Type = PatternType.Exact; r.WholeWord = wholeWord; return r; }
}
