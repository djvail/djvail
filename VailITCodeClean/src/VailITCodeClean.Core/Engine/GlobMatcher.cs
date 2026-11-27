using System.Text;
using System.Text.RegularExpressions;

namespace VailIT.CodeClean.Engine;

/// <summary>
/// Matches file paths against simple wildcard patterns.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><c>*</c> any characters except a folder separator, <c>?</c> one character, <c>**</c> any number of folders.</item>
/// <item>A pattern without a <c>/</c> is matched against the file NAME only (<c>*.config</c>, <c>id_rsa</c>).</item>
/// <item>A pattern with a <c>/</c> is matched against the path relative to the scanned folder
/// (<c>src/**/appsettings*.json</c>). Back slashes and forward slashes are treated the same.</item>
/// <item>Matching is case-insensitive (as on Windows).</item>
/// </list>
/// </remarks>
public sealed class GlobMatcher
{
    private readonly List<Regex> _nameRegexes = [];
    private readonly List<Regex> _pathRegexes = [];

    public GlobMatcher(IEnumerable<string>? patterns)
    {
        foreach (var raw in patterns ?? [])
        {
            var p = raw?.Trim().Replace('\\', '/');
            if (string.IsNullOrEmpty(p)) continue;
            if (p.Contains('/'))
                _pathRegexes.Add(ToRegex(p.TrimStart('/')));
            else
                _nameRegexes.Add(ToRegex(p));
        }
    }

    public bool IsEmpty => _nameRegexes.Count == 0 && _pathRegexes.Count == 0;

    /// <param name="relativePath">Path relative to the scan root, either separator.</param>
    public bool IsMatch(string relativePath)
    {
        if (IsEmpty) return false;
        var path = relativePath.Replace('\\', '/');
        var name = path[(path.LastIndexOf('/') + 1)..];
        foreach (var r in _nameRegexes) if (r.IsMatch(name)) return true;
        foreach (var r in _pathRegexes) if (r.IsMatch(path)) return true;
        return false;
    }

    private static Regex ToRegex(string glob)
    {
        var sb = new StringBuilder("^");
        for (int i = 0; i < glob.Length; i++)
        {
            char ch = glob[i];
            if (ch == '*')
            {
                if (i + 1 < glob.Length && glob[i + 1] == '*')
                {
                    // "**/" = zero or more folders; "**" at the end = anything.
                    if (i + 2 < glob.Length && glob[i + 2] == '/') { sb.Append("(?:.*/)?"); i += 2; }
                    else { sb.Append(".*"); i++; }
                }
                else sb.Append("[^/]*");
            }
            else if (ch == '?') sb.Append("[^/]");
            else sb.Append(Regex.Escape(ch.ToString()));
        }
        sb.Append('$');
        return new Regex(sb.ToString(), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }
}
