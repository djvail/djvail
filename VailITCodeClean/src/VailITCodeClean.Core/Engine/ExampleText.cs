using System.Text;
using System.Text.RegularExpressions;

namespace VailIT.CodeClean.Engine;

/// <summary>
/// Expands fake-value placeholders used in rule examples, e.g. <c>ghp_{{ALNUM:36}}</c> becomes
/// <c>ghp_</c> followed by 36 letters and digits. This lets the rules, tests and sample project carry
/// realistic examples without storing anything that looks like a real key in source control
/// (which would also trip GitHub's secret scanning).
/// </summary>
/// <remarks>
/// Character sets: ALNUM (A-Z a-z 0-9), HEX (0-9 a-f), HEXU (0-9 A-F), UPNUM (A-Z 0-9), LOWNUM (a-z 0-9),
/// UPPER, LOWER, DIGITS, B64 (Base64 without padding), URLSAFE (A-Z a-z 0-9 - _), BECH32.
/// The output is deterministic (the same text every time) but varied enough to pass entropy checks.
/// </remarks>
public static partial class ExampleText
{
    private static readonly Dictionary<string, string> Sets = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ALNUM"] = "aB3dE5fG7hJ9kL2mN4pQ6rS8tU0vW1xY3zC5",
        ["HEX"] = "0123456789abcdef",
        ["HEXU"] = "0123456789ABCDEF",
        ["UPNUM"] = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567",
        ["LOWNUM"] = "abcdefghijklmnopqrstuvwxyz0123456789",
        ["UPPER"] = "ABCDEFGHIJKLMNOPQRSTUVWXYZ",
        ["LOWER"] = "abcdefghijklmnopqrstuvwxyz",
        ["DIGITS"] = "0123456789",
        ["B64"] = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/",
        ["URLSAFE"] = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-_",
        ["BECH32"] = "QPZRY9X8GF2TVDW0S3JN54KHCE6MUA7L",
    };

    [GeneratedRegex(@"\{\{([A-Z0-9]+):(\d{1,4})\}\}", RegexOptions.IgnoreCase)]
    private static partial Regex PlaceholderRegex();

    public static string Expand(string text)
    {
        if (string.IsNullOrEmpty(text) || !text.Contains("{{", StringComparison.Ordinal)) return text;
        int seed = 0;
        return PlaceholderRegex().Replace(text, m =>
        {
            if (!Sets.TryGetValue(m.Groups[1].Value, out var set)) return m.Value;
            int length = int.Parse(m.Groups[2].Value);
            var sb = new StringBuilder(length);
            // A simple stride through the character set: deterministic, no long repeats, high entropy.
            for (int i = 0; i < length; i++)
                sb.Append(set[(int)(((long)(i + seed) * 7 + (i / set.Length) * 3 + 5) % set.Length)]);
            seed += 11;
            return sb.ToString();
        });
    }
}
