using System.Text.Json;
using System.Text.RegularExpressions;
using VailIT.CodeClean.Configuration;

namespace VailIT.CodeClean.Engine;

/// <summary>The contents of <c>Report\mapping.json</c>.</summary>
public sealed class MappingFile
{
    public string Warning { get; set; } = "";
    public string Source { get; set; } = "";
    public DateTime Created { get; set; }
    public bool PreserveLineNumbers { get; set; }
    public List<TokenMapping> Entries { get; set; } = [];

    public static MappingFile Load(string path) =>
        JsonSerializer.Deserialize<MappingFile>(File.ReadAllText(path), ProfileStore.JsonOptions)
        ?? throw new InvalidDataException($"'{path}' is not a mapping file.");
}

/// <summary>
/// Puts the original values back: replaces every placeholder from a mapping file with the real value.
/// Use it on code (or an AI answer) that was produced from a scrubbed copy.
/// </summary>
public sealed class Restorer
{
    private readonly Regex? _regex;
    private readonly Dictionary<string, TokenMapping> _byToken;

    public Restorer(MappingFile mapping)
    {
        _byToken = mapping.Entries
            .Where(e => !string.IsNullOrEmpty(e.Token))
            .GroupBy(e => e.Token, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        if (_byToken.Count == 0) return;

        // Multi-line values were written as the same placeholder on consecutive lines; match the whole run.
        var parts = _byToken.Values
            .OrderByDescending(e => e.Token.Length)
            .Select(e =>
            {
                var t = Regex.Escape(e.Token);
                return e.MultiLine ? $@"{t}(?:(?:[ \t]*(?>\r\n|\n|\r))+[ \t]*{t})*" : t;
            });
        // Longest tokens first, so VAILIT_IP_10000 is never read as VAILIT_IP_1000 + "0".
        _regex = new Regex($"(?:{string.Join("|", parts)})", RegexOptions.CultureInvariant);
    }

    public int TokenCount => _byToken.Count;

    /// <summary>Returns <paramref name="text"/> with the placeholders replaced. <paramref name="replaced"/> counts them.</summary>
    public string Restore(string text, out int replaced)
    {
        int count = 0;
        if (_regex == null) { replaced = 0; return text; }
        var result = _regex.Replace(text, m =>
        {
            // The first token in the match tells us which mapping this is.
            var token = _byToken.Keys.Where(k => m.Value.StartsWith(k, StringComparison.Ordinal)).MaxBy(k => k.Length);
            if (token == null) return m.Value;
            count++;
            var entry = _byToken[token];
            // A multi-line value was written as "indent + placeholder" per line, with the line breaks and
            // the first line's indentation left outside the placeholders, so put back only the inner part.
            return entry.MultiLine ? entry.Original.Trim() : entry.Original;
        });
        replaced = count;
        return result;
    }

    /// <summary>Restores every text file in a folder (or a single file) into <paramref name="outputPath"/>.</summary>
    /// <returns>Number of files written and number of placeholders replaced.</returns>
    public (int Files, int Replaced) RestorePath(string inputPath, string outputPath, IProgress<string>? log = null)
    {
        int files = 0, total = 0;
        if (File.Exists(inputPath))
        {
            var target = Directory.Exists(outputPath) ? Path.Combine(outputPath, Path.GetFileName(inputPath)) : outputPath;
            total += RestoreFile(inputPath, target);
            return (1, total);
        }
        if (!Directory.Exists(inputPath)) throw new DirectoryNotFoundException(inputPath);
        if (Path.GetFullPath(inputPath).TrimEnd('\\', '/').Equals(Path.GetFullPath(outputPath).TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Restore to a different folder than the input.");

        foreach (var file in Directory.EnumerateFiles(inputPath, "*", SearchOption.AllDirectories))
        {
            var rel = Path.GetRelativePath(inputPath, file);
            var target = Path.Combine(outputPath, rel);
            int n = RestoreFile(file, target);
            total += n;
            files++;
            if (n > 0) log?.Report($"{rel}: {n} placeholder(s) restored");
        }
        return (files, total);
    }

    private int RestoreFile(string source, string target)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(target))!);
        var loaded = TextFileCodec.Read(source);
        if (loaded.IsBinary) { File.Copy(source, target, true); return 0; }
        var text = Restore(loaded.Text, out var n);
        TextFileCodec.Write(target, loaded, text);
        return n;
    }
}
