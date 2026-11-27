using System.Security.Cryptography;
using System.Text;
using VailIT.CodeClean.Configuration;

namespace VailIT.CodeClean.Engine;

/// <summary>One placeholder and the original value it stands for (written to mapping.json when enabled).</summary>
public sealed class TokenMapping
{
    public string Token { get; set; } = "";
    public string Label { get; set; } = "";
    public string Original { get; set; } = "";

    /// <summary>The original spanned several lines and was replaced line by line.</summary>
    public bool MultiLine { get; set; }

    public int Occurrences { get; set; }
    public string FirstFile { get; set; } = "";
    public int FirstLine { get; set; }
}

/// <summary>
/// Hands out placeholders. With <see cref="ScanOptions.ConsistentTokens"/> the same original value
/// always gets the same placeholder for the whole run, so an AI (or a person) can still see that two
/// places use the same password, host or key. Thread safe.
/// </summary>
public sealed class TokenProvider
{
    private readonly ScanOptions _options;
    private readonly byte[] _salt;
    private readonly Dictionary<string, TokenMapping> _byValue = new(StringComparer.Ordinal);
    private readonly Dictionary<string, TokenMapping> _byToken = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _counters = new(StringComparer.Ordinal);
    private readonly object _lock = new();

    public TokenProvider(ScanOptions options)
    {
        _options = options;
        _salt = string.IsNullOrEmpty(options.HashSalt)
            ? RandomNumberGenerator.GetBytes(32)
            : Encoding.UTF8.GetBytes(options.HashSalt);
    }

    /// <summary>Every placeholder handed out so far, in the order they were created.</summary>
    public List<TokenMapping> Mappings
    {
        get { lock (_lock) return _byToken.Values.ToList(); }
    }

    /// <summary>Returns the placeholder for <paramref name="value"/> (creating it if needed). Not used for <see cref="ObfuscationStyle.Mask"/>.</summary>
    public string GetToken(string label, string value, string file, int line, bool multiLine)
    {
        if (_options.Style == ObfuscationStyle.Fixed)
            return string.IsNullOrEmpty(_options.FixedText) ? "[REDACTED]" : _options.FixedText;

        lock (_lock)
        {
            if (_options.ConsistentTokens && _byValue.TryGetValue(value, out var existing))
            {
                existing.Occurrences++;
                return existing.Token;
            }

            var token = MakeToken(label, value);
            var mapping = new TokenMapping
            {
                Token = token,
                Label = label,
                Original = value,
                MultiLine = multiLine,
                Occurrences = 1,
                FirstFile = file,
                FirstLine = line,
            };
            _byToken[token] = mapping;
            if (_options.ConsistentTokens) _byValue[value] = mapping;
            return token;
        }
    }

    private string MakeToken(string label, string value)
    {
        var template = string.IsNullOrWhiteSpace(_options.TokenTemplate) ? "{PREFIX}_{LABEL}_{ID}" : _options.TokenTemplate;
        var hash = template.Contains("{HASH}", StringComparison.OrdinalIgnoreCase) ? Hash(value) : "";

        for (int attempt = 0; ; attempt++)
        {
            _counters[label] = _counters.TryGetValue(label, out var n) ? n + 1 : 1;
            var token = template
                .Replace("{PREFIX}", _options.TokenPrefix, StringComparison.OrdinalIgnoreCase)
                .Replace("{LABEL}", label, StringComparison.OrdinalIgnoreCase)
                .Replace("{ID}", _counters[label].ToString("0000"), StringComparison.OrdinalIgnoreCase)
                .Replace("{HASH}", attempt == 0 ? hash : $"{hash}{attempt}", StringComparison.OrdinalIgnoreCase);
            // Templates without {ID}/{HASH}, or a hash collision, would give two values the same token: add a suffix.
            if (!_byToken.ContainsKey(token)) return token;
            if (!template.Contains("{ID}", StringComparison.OrdinalIgnoreCase) && !template.Contains("{HASH}", StringComparison.OrdinalIgnoreCase))
            {
                var suffixed = $"{token}_{_counters[label]:0000}";
                if (!_byToken.ContainsKey(suffixed)) return suffixed;
            }
        }
    }

    private string Hash(string value)
    {
        var bytes = HMACSHA256.HashData(_salt, Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes, 0, 4);
    }
}
