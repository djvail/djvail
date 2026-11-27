namespace VailIT.CodeClean.Engine;

/// <summary>Small helper calculations used to filter matches.</summary>
public static class ValueChecks
{
    /// <summary>
    /// Shannon entropy in bits per character. "aaaaaaaa" = 0, "password" ≈ 2.75,
    /// a random Base64 key ≈ 4.5 to 6. Used to tell random keys from ordinary words.
    /// </summary>
    public static double ShannonEntropy(string value)
    {
        if (string.IsNullOrEmpty(value)) return 0;
        var counts = new Dictionary<char, int>();
        foreach (var ch in value) counts[ch] = counts.TryGetValue(ch, out var n) ? n + 1 : 1;
        double entropy = 0, len = value.Length;
        foreach (var n in counts.Values)
        {
            var p = n / len;
            entropy -= p * Math.Log2(p);
        }
        return entropy;
    }

    /// <summary>True when the digits in <paramref name="value"/> (spaces and dashes ignored) pass the Luhn checksum.</summary>
    public static bool PassesLuhn(string value)
    {
        int sum = 0, digits = 0;
        bool doubleIt = false;
        for (int i = value.Length - 1; i >= 0; i--)
        {
            char ch = value[i];
            if (ch is ' ' or '-') continue;
            if (ch < '0' || ch > '9') return false;
            int d = ch - '0';
            if (doubleIt) { d *= 2; if (d > 9) d -= 9; }
            sum += d;
            doubleIt = !doubleIt;
            digits++;
        }
        return digits >= 12 && sum % 10 == 0;
    }

    /// <summary>
    /// A safe preview of a secret for the report: the first and last two characters for long values,
    /// stars for the rest (<c>Spring2024!</c> → <c>Sp*******4!</c>; short values become all stars).
    /// Line breaks are shown as ⏎ so the preview stays on one line.
    /// </summary>
    public static string MaskedPreview(string value)
    {
        var v = value.Replace("\r\n", "⏎").Replace('\n', '⏎').Replace('\r', '⏎');
        if (v.Length > 60) v = v[..2] + new string('*', 20) + $"…({value.Length} chars)";
        else if (v.Length >= 10) v = v[..2] + new string('*', v.Length - 4) + v[^2..];
        else v = new string('*', v.Length);
        return v;
    }
}
