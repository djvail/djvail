namespace VailIT.CodeClean.Engine;

/// <summary>
/// Turns character offsets into line and column numbers. Understands \r\n (Windows), \n (Unix) and \r (old Mac) line breaks.
/// </summary>
public sealed class LineIndex
{
    private readonly List<int> _starts = [0];

    public LineIndex(string text)
    {
        for (int i = 0; i < text.Length; i++)
        {
            char ch = text[i];
            if (ch == '\r')
            {
                if (i + 1 < text.Length && text[i + 1] == '\n') i++;
                _starts.Add(i + 1);
            }
            else if (ch == '\n')
            {
                _starts.Add(i + 1);
            }
        }
    }

    public int LineCount => _starts.Count;

    /// <summary>1-based line number of an offset.</summary>
    public int GetLine(int offset)
    {
        int idx = _starts.BinarySearch(offset);
        return (idx >= 0 ? idx : ~idx - 1) + 1;
    }

    /// <summary>1-based column of an offset.</summary>
    public int GetColumn(int offset) => offset - _starts[GetLine(offset) - 1] + 1;

    /// <summary>Start and end (exclusive, before the line break) of the line containing <paramref name="offset"/>.</summary>
    public (int Start, int End) GetLineBounds(string text, int offset)
    {
        int line = GetLine(offset) - 1;
        int start = _starts[line];
        int end = line + 1 < _starts.Count ? _starts[line + 1] : text.Length;
        return (start, TrimBreak(text, start, end));
    }

    /// <summary>The text of the line containing <paramref name="offset"/>, without its line break.</summary>
    public string GetLineText(string text, int offset)
    {
        var (start, end) = GetLineBounds(text, offset);
        return text[start..end];
    }

    /// <summary>The text of a 1-based line number, without its line break (empty when out of range).</summary>
    public string GetLineTextByNumber(string text, int lineNumber)
    {
        if (lineNumber < 1 || lineNumber > _starts.Count) return "";
        return GetLineText(text, _starts[lineNumber - 1]);
    }

    private static int TrimBreak(string text, int start, int end)
    {
        while (end > start && (text[end - 1] == '\n' || text[end - 1] == '\r')) end--;
        return end;
    }
}
