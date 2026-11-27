using System.Text;

namespace VailIT.CodeClean.Engine;

/// <summary>A file read into memory, with what is needed to write it back the same way.</summary>
public sealed class LoadedFile
{
    public required bool IsBinary { get; init; }
    public string Text { get; init; } = "";
    public Encoding Encoding { get; init; } = Encoding.UTF8;
    public bool HasBom { get; init; }

    /// <summary>Friendly encoding name for the report.</summary>
    public string EncodingName { get; init; } = "";
}

/// <summary>
/// Reads and writes text files without changing anything we did not mean to change: the byte-order
/// mark, the encoding and the line endings all come back exactly as they were.
/// </summary>
public static class TextFileCodec
{
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    public static LoadedFile Read(string path) => Decode(File.ReadAllBytes(path));

    public static LoadedFile Decode(byte[] bytes)
    {
        // Byte-order marks first.
        if (StartsWith(bytes, 0xEF, 0xBB, 0xBF))
            return Text(new UTF8Encoding(true), bytes, 3, true, "UTF-8 (BOM)");
        if (StartsWith(bytes, 0xFF, 0xFE, 0x00, 0x00))
            return Text(new UTF32Encoding(false, true), bytes, 4, true, "UTF-32 LE");
        if (StartsWith(bytes, 0x00, 0x00, 0xFE, 0xFF))
            return Text(new UTF32Encoding(true, true), bytes, 4, true, "UTF-32 BE");
        if (StartsWith(bytes, 0xFF, 0xFE))
            return Text(new UnicodeEncoding(false, true), bytes, 2, true, "UTF-16 LE");
        if (StartsWith(bytes, 0xFE, 0xFF))
            return Text(new UnicodeEncoding(true, true), bytes, 2, true, "UTF-16 BE");

        if (LooksBinary(bytes)) return new LoadedFile { IsBinary = true, EncodingName = "binary" };

        // No BOM: UTF-8 if it decodes cleanly, otherwise Latin-1, which maps every byte to one
        // character and back again, so untouched bytes survive exactly (Windows-1252 files etc.).
        try
        {
            return new LoadedFile { IsBinary = false, Text = StrictUtf8.GetString(bytes), Encoding = new UTF8Encoding(false), EncodingName = "UTF-8" };
        }
        catch (DecoderFallbackException)
        {
            return new LoadedFile { IsBinary = false, Text = Encoding.Latin1.GetString(bytes), Encoding = Encoding.Latin1, EncodingName = "ANSI / Latin-1" };
        }
    }

    public static void Write(string path, LoadedFile original, string text)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using var fs = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        if (original.HasBom)
        {
            var preamble = original.Encoding.GetPreamble();
            fs.Write(preamble, 0, preamble.Length);
        }
        var bytes = original.Encoding.GetBytes(text);
        fs.Write(bytes, 0, bytes.Length);
    }

    /// <summary>A NUL byte in the first 8 KB (that is not explained by a UTF-16/32 BOM) means binary.</summary>
    public static bool LooksBinary(byte[] bytes)
    {
        int n = Math.Min(bytes.Length, 8192);
        for (int i = 0; i < n; i++) if (bytes[i] == 0) return true;
        return false;
    }

    private static LoadedFile Text(Encoding encoding, byte[] bytes, int skip, bool bom, string name)
    {
        try
        {
            return new LoadedFile { IsBinary = false, Text = encoding.GetString(bytes, skip, bytes.Length - skip), Encoding = encoding, HasBom = bom, EncodingName = name };
        }
        catch (DecoderFallbackException)
        {
            return new LoadedFile { IsBinary = true, EncodingName = "binary" };
        }
    }

    private static bool StartsWith(byte[] bytes, params byte[] prefix)
    {
        if (bytes.Length < prefix.Length) return false;
        for (int i = 0; i < prefix.Length; i++) if (bytes[i] != prefix[i]) return false;
        return true;
    }
}
