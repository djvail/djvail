using System.Text;
using VailIT.CodeClean.Configuration;

namespace VailIT.CodeClean.Engine;

/// <summary>Where one replacement ended up.</summary>
/// <param name="Group">What was replaced.</param>
/// <param name="Replacement">The full text written in its place.</param>
/// <param name="Token">The placeholder (for Mask style, the masked text).</param>
/// <param name="OutputStart">Offset of the replacement in the scrubbed text.</param>
public sealed record AppliedReplacement(DetectionGroup Group, string Replacement, string Token, int OutputStart);

/// <summary>Writes placeholders into text.</summary>
public sealed class Scrubber
{
    private readonly ScanOptions _options;
    private readonly TokenProvider _tokens;

    public Scrubber(ScanOptions options, TokenProvider tokens)
    {
        _options = options;
        _tokens = tokens;
    }

    /// <summary>
    /// Replaces every group in <paramref name="text"/>. Groups must be sorted and must not overlap
    /// (as returned by <see cref="Detector.Detect"/>). Everything else, including line endings and
    /// encoding-relevant characters, is copied unchanged.
    /// </summary>
    public string Apply(string text, IReadOnlyList<DetectionGroup> groups, string file, out List<AppliedReplacement> applied)
    {
        applied = new List<AppliedReplacement>(groups.Count);
        if (groups.Count == 0) return text;

        var lines = new LineIndex(text);
        var sb = new StringBuilder(text.Length + groups.Count * 16);
        int pos = 0;
        foreach (var g in groups)
        {
            if (g.Start < pos) continue; // safety: never overlap
            sb.Append(text, pos, g.Start - pos);

            bool multiLine = g.Value.Contains('\n') || g.Value.Contains('\r');
            string token, replacement;
            if (_options.Style == ObfuscationStyle.Mask)
            {
                replacement = Mask(g.Value);
                token = replacement;
            }
            else
            {
                token = _tokens.GetToken(g.Primary.Label, g.Value, file, lines.GetLine(g.Start), multiLine);
                replacement = multiLine && _options.PreserveLineNumbers ? PerLine(g.Value, token) : token;
            }

            applied.Add(new AppliedReplacement(g, replacement, token, sb.Length));
            sb.Append(replacement);
            pos = g.End;
        }
        sb.Append(text, pos, text.Length - pos);
        return sb.ToString();
    }

    /// <summary>Replaces every character except line breaks, so lines and columns stay exactly where they were.</summary>
    private string Mask(string value)
    {
        var chars = value.ToCharArray();
        for (int i = 0; i < chars.Length; i++)
            if (chars[i] is not ('\r' or '\n')) chars[i] = _options.MaskChar;
        return new string(chars);
    }

    /// <summary>
    /// Keeps the line structure of a multi-line value: each line that had content gets the placeholder
    /// (its indentation is kept), blank lines stay blank, and every original line break is kept.
    /// </summary>
    public static string PerLine(string value, string token)
    {
        var sb = new StringBuilder();
        int i = 0;
        while (i <= value.Length)
        {
            int lineEnd = value.IndexOfAny(['\r', '\n'], i);
            if (lineEnd < 0) lineEnd = value.Length;
            var segment = value.AsSpan(i, lineEnd - i);
            if (!segment.IsWhiteSpace())
            {
                int indent = 0;
                while (indent < segment.Length && (segment[indent] == ' ' || segment[indent] == '\t')) indent++;
                sb.Append(segment[..indent]).Append(token);
            }
            else
            {
                sb.Append(segment);
            }
            if (lineEnd >= value.Length) break;

            // copy the line break itself (\r\n, \n or \r)
            if (value[lineEnd] == '\r' && lineEnd + 1 < value.Length && value[lineEnd + 1] == '\n')
            {
                sb.Append("\r\n");
                i = lineEnd + 2;
            }
            else
            {
                sb.Append(value[lineEnd]);
                i = lineEnd + 1;
            }
        }
        return sb.ToString();
    }
}
