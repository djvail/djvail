using System.Text.Json.Serialization;
using VailIT.CodeClean.Configuration;
using VailIT.CodeClean.Engine;

namespace VailIT.CodeClean.Reporting;

/// <summary>What happened to one file.</summary>
public enum FileStatus
{
    /// <summary>Scanned, nothing found, copied unchanged.</summary>
    Clean,

    /// <summary>Scanned, findings replaced, scrubbed copy written.</summary>
    Scrubbed,

    /// <summary>Binary file, not copied (CopyBinaryFiles is off).</summary>
    BinarySkipped,

    /// <summary>Binary file copied unchanged (it could not be scanned).</summary>
    BinaryCopied,

    /// <summary>Larger than MaxFileSizeKB: not scanned and not copied.</summary>
    TooLarge,

    /// <summary>Matched an excluded-file pattern, or not matched by the include patterns.</summary>
    Excluded,

    /// <summary>Matched a sensitive-file pattern: never copied.</summary>
    Sensitive,

    /// <summary>Could not be read or written.</summary>
    Error,
}

public sealed class FileResult
{
    /// <summary>Path relative to the source folder.</summary>
    public string Path { get; set; } = "";

    /// <summary>Path relative to the Scrubbed folder (differs when file names were scrubbed). Empty when not written.</summary>
    public string OutputPath { get; set; } = "";

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public FileStatus Status { get; set; }

    public long SizeBytes { get; set; }
    public int Findings { get; set; }
    public string Encoding { get; set; } = "";
    public string Message { get; set; } = "";
}

/// <summary>One thing that was found and replaced.</summary>
public sealed class Finding
{
    /// <summary>Path relative to the source folder.</summary>
    public string File { get; set; } = "";

    /// <summary>1-based line in the ORIGINAL file (0 = the file/folder name itself).</summary>
    public int Line { get; set; }

    public int Column { get; set; }

    /// <summary>Last line of the value (differs from <see cref="Line"/> for multi-line values such as keys).</summary>
    public int EndLine { get; set; }

    /// <summary>1-based line in the scrubbed file (same as <see cref="Line"/> when line numbers are preserved).</summary>
    public int OutputLine { get; set; }

    public string RuleId { get; set; } = "";
    public string RuleName { get; set; } = "";
    public string Category { get; set; } = "";

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public Severity Severity { get; set; }

    /// <summary>The placeholder written into the scrubbed file.</summary>
    public string Token { get; set; } = "";

    /// <summary>Masked view of the original value, e.g. <c>Sp*******4!</c>.</summary>
    public string Preview { get; set; } = "";

    /// <summary>The original value. Only filled when IncludeOriginalValuesInReport is on.</summary>
    public string? Original { get; set; }

    public int Length { get; set; }

    /// <summary>The line as it reads in the scrubbed output (safe to show).</summary>
    public string Context { get; set; } = "";

    /// <summary>Other rules that matched the same (overlapping) text.</summary>
    public List<string> AlsoMatched { get; set; } = [];
}

/// <summary>The outcome of a whole run. Serialised as <c>findings.json</c>.</summary>
public sealed class RunResult
{
    public string Tool { get; set; } = "VailIT Code Clean";
    public string ToolVersion { get; set; } = typeof(RunResult).Assembly.GetName().Version?.ToString(3) ?? "";
    public string ProfileName { get; set; } = "";
    public string SourceFolder { get; set; } = "";
    public string RunFolder { get; set; } = "";
    public string ScrubbedFolder { get; set; } = "";
    public string ReportFolder { get; set; } = "";
    public DateTime StartedLocal { get; set; }
    public TimeSpan Duration { get; set; }
    public bool ScanOnly { get; set; }
    public bool Cancelled { get; set; }
    public string MachineName { get; set; } = Environment.MachineName;

    /// <summary>The options the run used (paths, styles, filters).</summary>
    public ScanOptions Options { get; set; } = new();

    public int RulesUsed { get; set; }
    public List<FileResult> Files { get; set; } = [];
    public List<Finding> Findings { get; set; } = [];
    public List<string> Warnings { get; set; } = [];
    public List<string> RuleErrors { get; set; } = [];

    /// <summary>Report files that were written.</summary>
    public List<string> ReportFiles { get; set; } = [];

    /// <summary>Placeholders and original values. Never written to the report itself.</summary>
    [JsonIgnore]
    public List<TokenMapping> Mappings { get; set; } = [];

    // ---- handy totals ----
    public int FilesScanned => Files.Count(f => f.Status is FileStatus.Clean or FileStatus.Scrubbed);
    public int FilesWithFindings => Files.Count(f => f.Findings > 0);
    public int FindingCount => Findings.Count;
    public int CountBySeverity(Severity s) => Findings.Count(f => f.Severity == s);
}
