namespace VailIT.CodeClean.Configuration;

/// <summary>Everything about a run except the rules themselves.</summary>
public sealed class ScanOptions
{
    // ---------------------------------------------------------------- folders

    /// <summary>The folder to scan. All subfolders are included.</summary>
    public string SourceFolder { get; set; } = "";

    /// <summary>
    /// Where results go. The original code is never changed. With <see cref="UseTimestampedRunFolder"/>
    /// on, each run creates <c>&lt;OutputFolder&gt;\&lt;SourceName&gt;_yyyyMMdd_HHmmss\</c> containing
    /// <c>Scrubbed\</c> (the cleaned copy to share) and <c>Report\</c> (keep this private).
    /// </summary>
    public string OutputFolder { get; set; } = "";

    /// <summary>Create a new dated sub folder for every run so earlier results are never overwritten.</summary>
    public bool UseTimestampedRunFolder { get; set; } = true;

    /// <summary>Only produce the report; do not write the scrubbed copy.</summary>
    public bool ScanOnly { get; set; }

    // ---------------------------------------------------------------- replacement

    /// <summary>
    /// Keep every line where it was, so line N in the scrubbed file is line N in the original.
    /// A value that spans several lines (e.g. a private key block) is replaced line by line instead of
    /// being collapsed into one placeholder.
    /// </summary>
    public bool PreserveLineNumbers { get; set; } = true;

    public ObfuscationStyle Style { get; set; } = ObfuscationStyle.Token;

    /// <summary>First part of every placeholder. Pick something that never appears in real code.</summary>
    public string TokenPrefix { get; set; } = "VAILIT";

    /// <summary>
    /// Layout of a placeholder. <c>{PREFIX}</c>, <c>{LABEL}</c> (from the rule), <c>{ID}</c>
    /// (0001, 0002, ... per label) and <c>{HASH}</c> (8 hex characters derived from the value) can be used.
    /// </summary>
    public string TokenTemplate { get; set; } = "{PREFIX}_{LABEL}_{ID}";

    /// <summary>Secret used to build <c>{HASH}</c>. Leave empty for a random one per run; set it to get the same hash on every run.</summary>
    public string HashSalt { get; set; } = "";

    /// <summary>The same original value always gets the same placeholder across the whole run.</summary>
    public bool ConsistentTokens { get; set; } = true;

    /// <summary>Text used by <see cref="ObfuscationStyle.Fixed"/>.</summary>
    public string FixedText { get; set; } = "[REDACTED]";

    /// <summary>Character used by <see cref="ObfuscationStyle.Mask"/>.</summary>
    public char MaskChar { get; set; } = '*';

    /// <summary>Run the rules against file and folder names too and rename them in the output.</summary>
    public bool ScrubFileNames { get; set; }

    // ---------------------------------------------------------------- report

    public bool WriteHtmlReport { get; set; } = true;
    public bool WriteCsvReport { get; set; } = true;
    public bool WriteJsonReport { get; set; } = true;

    /// <summary>
    /// Write <c>Report\mapping.json</c> listing each placeholder and the ORIGINAL value, so answers that
    /// come back from an AI can be turned back into real code with the Restore feature.
    /// The file contains your secrets in plain text: never share it.
    /// </summary>
    public bool WriteMappingFile { get; set; }

    /// <summary>Put the full original value in the report instead of a masked preview (e.g. <c>Pa*****d1</c>). Not recommended.</summary>
    public bool IncludeOriginalValuesInReport { get; set; }

    // ---------------------------------------------------------------- which files

    /// <summary>Folder names that are skipped entirely (matched by name at any depth).</summary>
    public List<string> ExcludedFolders { get; set; } = [];

    /// <summary>When not empty, only files matching one of these patterns are processed.</summary>
    public List<string> IncludeFilePatterns { get; set; } = [];

    /// <summary>Files that are ignored completely (not scanned, not copied, only counted).</summary>
    public List<string> ExcludedFilePatterns { get; set; } = [];

    /// <summary>
    /// Files that are dangerous by nature (certificates with private keys, key stores, Terraform state, ...).
    /// They are never copied to the output and are listed in the report as High findings.
    /// </summary>
    public List<string> SensitiveFilePatterns { get; set; } = [];

    /// <summary>Files larger than this are not scanned (and not copied). 0 = no limit.</summary>
    public int MaxFileSizeKB { get; set; } = 10240;

    /// <summary>Copy binary files (images, DLLs, ...) to the output unchanged. They cannot be scanned.</summary>
    public bool CopyBinaryFiles { get; set; }

    // ---------------------------------------------------------------- engine

    /// <summary>A single regex is stopped if it runs longer than this on one file, so one bad pattern cannot hang the run.</summary>
    public int RegexTimeoutMs { get; set; } = 3000;

    /// <summary>How many files are scanned at once. 0 = one per CPU core.</summary>
    public int MaxParallelism { get; set; }
}
