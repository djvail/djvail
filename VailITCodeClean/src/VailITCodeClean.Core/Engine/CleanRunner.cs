using System.Collections.Concurrent;
using System.Diagnostics;
using VailIT.CodeClean.Configuration;
using VailIT.CodeClean.Reporting;

namespace VailIT.CodeClean.Engine;

/// <summary>Progress information raised while a run is going.</summary>
/// <param name="Phase">"Listing", "Scanning", "Writing" or "Reporting".</param>
public sealed record RunProgress(string Phase, int Done, int Total, string CurrentFile, int FindingsSoFar);

/// <summary>
/// Runs a complete clean: lists the files, scans them, writes the scrubbed copy and the reports.
/// The source folder is only ever read.
/// </summary>
/// <remarks>
/// The work happens in two passes. Pass 1 scans files in parallel (the slow part). Pass 2 goes through
/// the files one by one in alphabetical order, hands out placeholders and writes the output, so the
/// numbering (VAILIT_PASSWORD_0001, 0002...) is the same every time the same code is cleaned.
/// </remarks>
public sealed class CleanRunner
{
    /// <summary>Name of the marker file put in every run folder, so the tool only ever clears folders it created.</summary>
    public const string MarkerFileName = ".vailit-codeclean";

    private readonly CleanProfile _profile;
    private readonly ScanOptions _o;
    private readonly IProgress<RunProgress>? _progress;
    private readonly IProgress<string>? _log;

    public CleanRunner(CleanProfile profile, IProgress<RunProgress>? progress = null, IProgress<string>? log = null)
    {
        _profile = profile;
        _o = profile.Options;
        _progress = progress;
        _log = log;
    }

    public RunResult Run(CancellationToken cancel = default)
    {
        var sw = Stopwatch.StartNew();
        var source = Path.GetFullPath(_o.SourceFolder ?? "");
        if (string.IsNullOrWhiteSpace(_o.SourceFolder) || !Directory.Exists(source))
            throw new DirectoryNotFoundException($"Source folder not found: '{_o.SourceFolder}'.");
        if (string.IsNullOrWhiteSpace(_o.OutputFolder))
            throw new ArgumentException("Choose an output folder.");

        var outputRoot = Path.GetFullPath(_o.OutputFolder);
        if (PathEquals(outputRoot, source))
            throw new ArgumentException("The output folder must be different from the source folder.");

        var result = new RunResult
        {
            ProfileName = _profile.Name,
            SourceFolder = source,
            StartedLocal = DateTime.Now,
            ScanOnly = _o.ScanOnly,
            Options = _o,
        };

        // ---- output folders ----
        var runFolder = _o.UseTimestampedRunFolder
            ? Path.Combine(outputRoot, $"{SafeName(Path.GetFileName(source.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)))}_{result.StartedLocal:yyyyMMdd_HHmmss}")
            : outputRoot;
        PrepareRunFolder(runFolder);
        result.RunFolder = runFolder;
        result.ScrubbedFolder = Path.Combine(runFolder, "Scrubbed");
        result.ReportFolder = Path.Combine(runFolder, "Report");
        Directory.CreateDirectory(result.ReportFolder);
        if (!_o.ScanOnly) Directory.CreateDirectory(result.ScrubbedFolder);

        // ---- rules ----
        var detector = new Detector(_profile);
        result.RuleErrors.AddRange(detector.Errors);
        result.RulesUsed = detector.Rules.Count;
        foreach (var e in detector.Errors) Log("Rule skipped: " + e);
        Log($"Profile '{_profile.Name}': {detector.Rules.Count} rules enabled.");

        // ---- list files ----
        Report("Listing", 0, 0, "", 0);
        var excludeOutput = IsInside(outputRoot, source) ? outputRoot : null;
        var files = ListFiles(source, excludeOutput, cancel);
        Log($"Found {files.Count:N0} files under {source}.");

        var include = new GlobMatcher(_o.IncludeFilePatterns);
        var excluded = new GlobMatcher(_o.ExcludedFilePatterns);
        var sensitive = new GlobMatcher(_o.SensitiveFilePatterns);
        long maxBytes = _o.MaxFileSizeKB > 0 ? _o.MaxFileSizeKB * 1024L : long.MaxValue;

        // ---- pass 1: classify and scan (parallel) ----
        var scans = new ScanItem[files.Count];
        var warnings = new ConcurrentBag<string>();
        int done = 0, found = 0;
        var po = new ParallelOptions
        {
            CancellationToken = cancel,
            MaxDegreeOfParallelism = _o.MaxParallelism > 0 ? _o.MaxParallelism : Environment.ProcessorCount,
        };
        try
        {
            Parallel.For(0, files.Count, po, i =>
            {
                var (full, rel) = files[i];
                var item = new ScanItem(full, rel);
                scans[i] = item;
                try
                {
                    var info = new FileInfo(full);
                    item.Size = info.Length;
                    if (excluded.IsMatch(rel) || (!include.IsEmpty && !include.IsMatch(rel))) item.Status = FileStatus.Excluded;
                    else if (sensitive.IsMatch(rel)) item.Status = FileStatus.Sensitive;
                    else if (info.Length > maxBytes) item.Status = FileStatus.TooLarge;
                    else
                    {
                        var loaded = TextFileCodec.Read(full);
                        item.EncodingName = loaded.EncodingName;
                        if (loaded.IsBinary) item.Status = _o.CopyBinaryFiles ? FileStatus.BinaryCopied : FileStatus.BinarySkipped;
                        else
                        {
                            var local = new List<string>();
                            item.Groups = detector.Detect(loaded.Text, rel, local, cancel);
                            foreach (var w in local) warnings.Add(w);
                            item.Status = item.Groups.Count > 0 ? FileStatus.Scrubbed : FileStatus.Clean;
                            if (item.Groups.Count > 0) item.Loaded = loaded; // keep it for pass 2 (only files with findings)
                            Interlocked.Add(ref found, item.Groups.Count);
                        }
                    }
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    item.Status = FileStatus.Error;
                    item.Message = ex.Message;
                }
                var n = Interlocked.Increment(ref done);
                if (n % 25 == 0 || n == files.Count) Report("Scanning", n, files.Count, rel, Volatile.Read(ref found));
            });
        }
        catch (OperationCanceledException)
        {
            result.Cancelled = true;
            Log("Cancelled while scanning.");
        }

        // ---- pass 2: placeholders, output and findings (in order) ----
        var tokens = new TokenProvider(_o);
        var scrubber = new Scrubber(_o, tokens);
        var nameMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase); // scrubbed folder/file names

        for (int i = 0; i < scans.Length && !result.Cancelled; i++)
        {
            if (cancel.IsCancellationRequested) { result.Cancelled = true; Log("Cancelled while writing."); break; }
            var item = scans[i];
            if (item is null) continue;
            var fr = new FileResult { Path = item.Relative, Status = item.Status, SizeBytes = item.Size, Encoding = item.EncodingName, Message = item.Message };
            result.Files.Add(fr);

            try
            {
                string outRel = item.Relative;
                if (_o.ScrubFileNames && item.Status is FileStatus.Clean or FileStatus.Scrubbed or FileStatus.BinaryCopied)
                    outRel = ScrubPath(item.Relative, detector, tokens, nameMap, result);

                switch (item.Status)
                {
                    case FileStatus.Clean:
                    case FileStatus.BinaryCopied:
                        if (!_o.ScanOnly) { CopyFile(item.Full, OutPath(result, outRel)); fr.OutputPath = outRel; }
                        break;

                    case FileStatus.Scrubbed:
                    {
                        var loaded = item.Loaded ?? TextFileCodec.Read(item.Full);
                        item.Loaded = null;
                        var output = scrubber.Apply(loaded.Text, item.Groups!, item.Relative, out var applied);
                        if (!_o.ScanOnly) { TextFileCodec.Write(OutPath(result, outRel), loaded, output); fr.OutputPath = outRel; }
                        AddFindings(result, item.Relative, loaded.Text, output, applied);
                        fr.Findings = applied.Count;
                        break;
                    }

                    case FileStatus.Sensitive:
                        fr.Message = "Sensitive file type: not copied.";
                        result.Findings.Add(new Finding
                        {
                            File = item.Relative,
                            RuleId = "sensitive-file",
                            RuleName = "Sensitive file type (not copied)",
                            Category = "Files",
                            Severity = Severity.High,
                            Token = "(file left out)",
                            Preview = Path.GetFileName(item.Relative),
                            Context = "This file matched a Sensitive File pattern and was not copied to the output.",
                        });
                        fr.Findings = 1;
                        break;

                    case FileStatus.TooLarge:
                        fr.Message = $"Larger than {_o.MaxFileSizeKB:N0} KB: not scanned, not copied.";
                        result.Warnings.Add($"{item.Relative}: {fr.Message}");
                        break;

                    case FileStatus.Error:
                        result.Warnings.Add($"{item.Relative}: {item.Message}");
                        Log($"ERROR {item.Relative}: {item.Message}");
                        break;
                }
            }
            catch (Exception ex)
            {
                fr.Status = FileStatus.Error;
                fr.Message = ex.Message;
                result.Warnings.Add($"{item.Relative}: {ex.Message}");
                Log($"ERROR {item.Relative}: {ex.Message}");
            }

            if (i % 25 == 0 || i == scans.Length - 1) Report("Writing", i + 1, scans.Length, item.Relative, result.Findings.Count);
        }

        result.Warnings.AddRange(warnings.Distinct().OrderBy(w => w, StringComparer.Ordinal));
        result.Mappings = tokens.Mappings;
        result.Duration = sw.Elapsed;

        // ---- reports ----
        Report("Reporting", 0, 0, "", result.Findings.Count);
        ReportWriter.WriteAll(result, _o, _log);
        WriteReadme(result);
        result.Duration = sw.Elapsed;

        Log($"Done in {result.Duration.TotalSeconds:N1}s: {result.FilesScanned:N0} files scanned, {result.FindingCount:N0} findings in {result.FilesWithFindings:N0} files.");
        return result;
    }

    // ------------------------------------------------------------------ findings

    private void AddFindings(RunResult result, string file, string original, string output, List<AppliedReplacement> applied)
    {
        var inLines = new LineIndex(original);
        var outLines = new LineIndex(output);
        foreach (var a in applied)
        {
            var g = a.Group;
            var p = g.Primary.Rule.Rule;
            var outLine = outLines.GetLine(a.OutputStart);
            var context = outLines.GetLineText(output, a.OutputStart).Trim();
            if (context.Length > 300) context = context[..300] + "…";
            result.Findings.Add(new Finding
            {
                File = file,
                Line = inLines.GetLine(g.Start),
                Column = inLines.GetColumn(g.Start),
                EndLine = inLines.GetLine(Math.Max(g.Start, g.End - 1)),
                OutputLine = outLine,
                RuleId = p.Id,
                RuleName = p.Name,
                Category = p.Category,
                Severity = g.Severity,
                Token = a.Token.Length > 80 ? a.Token[..80] + "…" : a.Token,
                Preview = ValueChecks.MaskedPreview(g.Value),
                Original = _o.IncludeOriginalValuesInReport ? g.Value : null,
                Length = g.Length,
                Context = context,
                AlsoMatched = g.Members.Where(m => m != g.Primary).Select(m => m.Rule.Rule.Id).Distinct().ToList(),
            });
        }
    }

    // ------------------------------------------------------------------ file names

    /// <summary>Runs the rules over each folder and file name in the path and returns the renamed path.</summary>
    private string ScrubPath(string relative, Detector detector, TokenProvider tokens, Dictionary<string, string> cache, RunResult result)
    {
        var parts = relative.Split('/');
        var scrubber = new Scrubber(_o, tokens);
        var built = "";
        for (int i = 0; i < parts.Length; i++)
        {
            var original = built.Length == 0 ? parts[i] : built + "/" + parts[i];
            if (!cache.TryGetValue(original, out var renamedPart))
            {
                renamedPart = parts[i];
                var groups = detector.Detect(parts[i]);
                if (groups.Count > 0)
                {
                    renamedPart = scrubber.Apply(parts[i], groups, original, out var applied);
                    renamedPart = string.Concat(renamedPart.Select(ch => Path.GetInvalidFileNameChars().Contains(ch) || ch is '/' or '\\' ? '_' : ch));
                    foreach (var a in applied)
                    {
                        result.Findings.Add(new Finding
                        {
                            File = original,
                            Line = 0,
                            RuleId = a.Group.Primary.Rule.Rule.Id,
                            RuleName = a.Group.Primary.Rule.Rule.Name + " (in name)",
                            Category = a.Group.Primary.Rule.Rule.Category,
                            Severity = a.Group.Severity,
                            Token = a.Token,
                            Preview = ValueChecks.MaskedPreview(a.Group.Value),
                            Original = _o.IncludeOriginalValuesInReport ? a.Group.Value : null,
                            Length = a.Group.Length,
                            Context = (i < parts.Length - 1 ? "Folder renamed to " : "File renamed to ") + renamedPart,
                        });
                    }
                }
                cache[original] = renamedPart;
            }
            built = original;
            parts[i] = renamedPart;
        }
        return string.Join('/', parts);
    }

    // ------------------------------------------------------------------ folders and files

    private List<(string Full, string Relative)> ListFiles(string root, string? skipFolder, CancellationToken cancel)
    {
        var folders = new GlobMatcher(_o.ExcludedFolders);
        var list = new List<(string, string)>();
        var stack = new Stack<string>();
        stack.Push(root);
        var enumOptions = new EnumerationOptions { IgnoreInaccessible = true, RecurseSubdirectories = false, AttributesToSkip = 0 };

        while (stack.Count > 0)
        {
            cancel.ThrowIfCancellationRequested();
            var dir = stack.Pop();
            try
            {
                foreach (var file in Directory.EnumerateFiles(dir, "*", enumOptions))
                    list.Add((file, Relative(root, file)));

                foreach (var sub in Directory.EnumerateDirectories(dir, "*", enumOptions))
                {
                    if (skipFolder != null && PathEquals(sub, skipFolder)) continue;
                    var info = new DirectoryInfo(sub);
                    if (info.Attributes.HasFlag(FileAttributes.ReparsePoint)) continue; // junctions / symlinks: avoid loops
                    var rel = Relative(root, sub);
                    if (folders.IsMatch(rel)) continue;
                    stack.Push(sub);
                }
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
            {
                Log($"Cannot read folder {dir}: {ex.Message}");
            }
        }
        list.Sort((a, b) => string.Compare(a.Item2, b.Item2, StringComparison.OrdinalIgnoreCase));
        return list;
    }

    private void PrepareRunFolder(string runFolder)
    {
        if (Directory.Exists(runFolder) && Directory.EnumerateFileSystemEntries(runFolder).Any())
        {
            // Re-using a fixed output folder: only clear it if this tool created it.
            if (!File.Exists(Path.Combine(runFolder, MarkerFileName)))
                throw new IOException($"The output folder '{runFolder}' is not empty and was not created by VailIT Code Clean. Choose an empty folder or turn on 'new folder for each run'.");
            foreach (var sub in new[] { "Scrubbed", "Report" })
            {
                var p = Path.Combine(runFolder, sub);
                if (Directory.Exists(p)) Directory.Delete(p, recursive: true);
            }
        }
        Directory.CreateDirectory(runFolder);
        File.WriteAllText(Path.Combine(runFolder, MarkerFileName), "Created by VailIT Code Clean. The tool may clear the Scrubbed and Report sub folders of this folder on the next run.");
    }

    private static string OutPath(RunResult result, string relative) =>
        Path.Combine(result.ScrubbedFolder, relative.Replace('/', Path.DirectorySeparatorChar));

    private static void CopyFile(string from, string to)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(to)!);
        File.Copy(from, to, overwrite: true);
    }

    private static void WriteReadme(RunResult result)
    {
        var text =
            $"""
            VailIT Code Clean - run of {result.StartedLocal:yyyy-MM-dd HH:mm:ss}
            Source : {result.SourceFolder}

            Scrubbed\   The cleaned copy of your code. Review the report, then share THIS folder.
            Report\     What was found and where. Keep it private: it lists the files and lines
                        that held secrets{(result.Options.WriteMappingFile ? ", and mapping.json contains the ORIGINAL values" : "")}.

            Findings: {result.FindingCount:N0} in {result.FilesWithFindings:N0} files ({result.FilesScanned:N0} files scanned).
            Automated scrubbing can miss things: always look through the scrubbed code before uploading it.
            """;
        File.WriteAllText(Path.Combine(result.RunFolder, "README.txt"), text);
    }

    private void Report(string phase, int done, int total, string file, int found) =>
        _progress?.Report(new RunProgress(phase, done, total, file, found));

    private void Log(string message) => _log?.Report(message);

    private static string Relative(string root, string path) =>
        Path.GetRelativePath(root, path).Replace('\\', '/');

    private static bool PathEquals(string a, string b) =>
        string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(a)), Path.TrimEndingDirectorySeparator(Path.GetFullPath(b)),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    /// <summary>True when <paramref name="path"/> is inside (or equal to) <paramref name="folder"/>.</summary>
    private static bool IsInside(string path, string folder)
    {
        var rel = Path.GetRelativePath(folder, path);
        return rel == "." || (!rel.StartsWith("..", StringComparison.Ordinal) && !Path.IsPathRooted(rel));
    }

    private static string SafeName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var clean = new string(name.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray());
        return string.IsNullOrWhiteSpace(clean) ? "Source" : clean;
    }

    private sealed class ScanItem(string full, string relative)
    {
        public string Full { get; } = full;
        public string Relative { get; } = relative;
        public long Size { get; set; }
        public FileStatus Status { get; set; }
        public string EncodingName { get; set; } = "";
        public string Message { get; set; } = "";
        public List<DetectionGroup>? Groups { get; set; }
        public LoadedFile? Loaded { get; set; }
    }
}
