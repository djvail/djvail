namespace VailIT.CodeClean.Configuration;

/// <summary>How a rule's <see cref="FilterRule.Pattern"/> is interpreted.</summary>
public enum PatternType
{
    /// <summary>
    /// Literal text. The pattern may hold several strings, one per line; each one is searched for
    /// exactly as typed (no wildcards, no escaping needed).
    /// </summary>
    Exact,

    /// <summary>A .NET regular expression. Named groups called <c>secret</c> (or starting with <c>secret_</c>) limit what is replaced.</summary>
    Regex,
}

/// <summary>How serious a finding is. Used for sorting, colouring and the report summary.</summary>
public enum Severity
{
    Info = 0,
    Low = 1,
    Medium = 2,
    High = 3,
    Critical = 4,
}

/// <summary>What gets replaced when a rule matches.</summary>
public enum ReplaceScope
{
    /// <summary>Only the matched text (or the <c>secret</c> group inside it).</summary>
    Match,

    /// <summary>The whole line that contains the match (the line break itself is kept).</summary>
    Line,
}

/// <summary>Extra check a match must pass before it counts.</summary>
public enum ValueValidator
{
    None,

    /// <summary>The digits must pass the Luhn checksum (credit card numbers).</summary>
    Luhn,
}

/// <summary>What the scrubbed value looks like in the output.</summary>
public enum ObfuscationStyle
{
    /// <summary>A readable placeholder built from <see cref="ScanOptions.TokenTemplate"/>, e.g. <c>VAILIT_PASSWORD_0001</c>.</summary>
    Token,

    /// <summary>Every character is replaced with <see cref="ScanOptions.MaskChar"/>; length and columns are kept.</summary>
    Mask,

    /// <summary>A fixed piece of text such as <c>[REDACTED]</c>.</summary>
    Fixed,
}
