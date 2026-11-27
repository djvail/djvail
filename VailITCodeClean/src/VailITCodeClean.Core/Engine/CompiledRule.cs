using System.Text;
using System.Text.RegularExpressions;
using VailIT.CodeClean.Configuration;

namespace VailIT.CodeClean.Engine;

/// <summary>A <see cref="FilterRule"/> turned into ready-to-run regular expressions.</summary>
public sealed class CompiledRule
{
    private readonly Regex? _ignoreLine;
    private readonly Regex? _ignoreValue;
    private readonly HashSet<string> _ignoreValues;
    private readonly GlobMatcher _include;
    private readonly GlobMatcher _exclude;

    public FilterRule Rule { get; }

    /// <summary>Position of the rule in the profile (used to break ties).</summary>
    public int Order { get; }

    public Regex Regex { get; }

    /// <summary>Label used in placeholders when a whole match / the <c>secret</c> group is replaced.</summary>
    public string Label { get; }

    /// <summary>Names of the groups to replace (empty = replace the whole match).</summary>
    public IReadOnlyList<string> SecretGroups { get; }

    private CompiledRule(FilterRule rule, int order, TimeSpan timeout)
    {
        Rule = rule;
        Order = order;
        Label = MakeLabel(string.IsNullOrWhiteSpace(rule.TokenLabel) ? rule.Category : rule.TokenLabel);

        var options = RegexOptions.CultureInvariant | RegexOptions.Multiline | RegexOptions.Compiled;
        if (!rule.CaseSensitive) options |= RegexOptions.IgnoreCase;

        Regex = new Regex(BuildPattern(rule), options, timeout);
        SecretGroups = Regex.GetGroupNames()
            .Where(n => n.Equals("secret", StringComparison.OrdinalIgnoreCase) || n.StartsWith("secret_", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var helperOptions = RegexOptions.CultureInvariant | RegexOptions.IgnoreCase;
        if (!string.IsNullOrWhiteSpace(rule.IgnoreLinePattern)) _ignoreLine = new Regex(rule.IgnoreLinePattern, helperOptions, timeout);
        if (!string.IsNullOrWhiteSpace(rule.IgnoreValuePattern)) _ignoreValue = new Regex(rule.IgnoreValuePattern, helperOptions, timeout);
        _ignoreValues = new HashSet<string>(rule.IgnoreValues.Where(v => !string.IsNullOrEmpty(v)), StringComparer.OrdinalIgnoreCase);
        _include = new GlobMatcher(rule.IncludeFiles);
        _exclude = new GlobMatcher(rule.ExcludeFiles);
    }

    /// <summary>Compiles a rule. Throws <see cref="ArgumentException"/> with a readable message when a pattern is invalid.</summary>
    public static CompiledRule Compile(FilterRule rule, int order = 0, int timeoutMs = 3000)
    {
        if (string.IsNullOrWhiteSpace(rule.Pattern))
            throw new ArgumentException($"Rule '{rule.Id}' has an empty pattern.");
        try
        {
            return new CompiledRule(rule, order, TimeSpan.FromMilliseconds(Math.Max(100, timeoutMs)));
        }
        catch (ArgumentException ex)
        {
            throw new ArgumentException($"Rule '{rule.Id}' ({rule.Name}) has an invalid pattern: {ex.Message}", ex);
        }
    }

    /// <summary>Checks a rule without keeping it. Returns null when it is fine, otherwise the error text.</summary>
    public static string? Validate(FilterRule rule)
    {
        try { Compile(rule); return null; }
        catch (ArgumentException ex) { return ex.Message; }
    }

    /// <summary>Should this rule run on the given file?</summary>
    public bool AppliesTo(string relativePath)
    {
        if (string.IsNullOrEmpty(relativePath)) return true;
        if (!_include.IsEmpty && !_include.IsMatch(relativePath)) return false;
        return _exclude.IsEmpty || !_exclude.IsMatch(relativePath);
    }

    /// <summary>Applies the rule's own value filters (length, entropy, checksum, ignore lists).</summary>
    public bool AcceptsValue(string value)
    {
        if (value.Length == 0) return false;
        if (Rule.MinLength > 0 && value.Length < Rule.MinLength) return false;
        if (_ignoreValues.Contains(value.Trim())) return false;
        if (Rule.MinEntropy > 0 && ValueChecks.ShannonEntropy(value) < Rule.MinEntropy) return false;
        if (Rule.Validator == ValueValidator.Luhn && !ValueChecks.PassesLuhn(value)) return false;
        if (_ignoreValue != null && SafeIsMatch(_ignoreValue, value)) return false;
        return true;
    }

    /// <summary>True when the line that contains the match should be skipped.</summary>
    public bool IgnoresLine(string line) => _ignoreLine != null && SafeIsMatch(_ignoreLine, line);

    /// <summary>Converts a label to UPPER_SNAKE_CASE so it can be used inside a placeholder.</summary>
    public static string MakeLabel(string text)
    {
        var sb = new StringBuilder();
        foreach (var ch in text.Trim())
        {
            if (char.IsAsciiLetterOrDigit(ch)) sb.Append(char.ToUpperInvariant(ch));
            else if (sb.Length > 0 && sb[^1] != '_') sb.Append('_');
        }
        var label = sb.ToString().Trim('_');
        return label.Length == 0 ? "VALUE" : label;
    }

    private static string BuildPattern(FilterRule rule)
    {
        if (rule.Type == PatternType.Regex) return rule.Pattern;

        // Exact: every non-empty line is a literal term. Longest first so "Contoso Ltd" wins over "Contoso".
        var terms = rule.Pattern
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries)
            .Where(t => t.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .OrderByDescending(t => t.Length)
            .Select(Regex.Escape)
            .ToList();
        if (terms.Count == 0) throw new ArgumentException("no text to search for");
        var body = string.Join("|", terms);
        return rule.WholeWord ? $@"(?<![\w])(?:{body})(?![\w])" : $"(?:{body})";
    }

    private static bool SafeIsMatch(Regex regex, string input)
    {
        try { return regex.IsMatch(input); }
        catch (RegexMatchTimeoutException) { return false; }
    }

    public override string ToString() => Rule.ToString();
}
