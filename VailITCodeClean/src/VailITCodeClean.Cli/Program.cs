using VailIT.CodeClean.Configuration;
using VailIT.CodeClean.Engine;
using VailIT.CodeClean.Reporting;
using VailIT.CodeClean.Samples;

namespace VailIT.CodeClean.Cli;

/// <summary>
/// vailit-codeclean: the command-line version of VailIT Code Clean.
/// Run without arguments for help.
/// </summary>
internal static class Program
{
    private const string Help = """
        VailIT Code Clean (command line)

        USAGE
          vailit-codeclean scan --source <folder> --output <folder> [options]
          vailit-codeclean restore --mapping <mapping.json> --input <file|folder> --output <file|folder>
          vailit-codeclean test (--file <path> | --text "<text>") [--profile <file>]
          vailit-codeclean list-rules [--profile <file>] [--all]
          vailit-codeclean export-profile <file>        Write the built-in default profile (to edit / share)
          vailit-codeclean make-sample <folder>         Create a fake project full of planted secrets

        SCAN OPTIONS
          --profile <file>        Profile (.profile.json) with options and rules. Default: built-in rules.
          --scan-only             Report only, do not write the scrubbed copy.
          --style <token|mask|fixed>
          --no-preserve-lines     Collapse multi-line values (e.g. private keys) into one placeholder.
          --scrub-names           Also scrub file and folder names.
          --mapping               Write Report\mapping.json (ORIGINAL VALUES - keep private) for 'restore'.
          --fixed-folder          Write straight into --output instead of a new dated sub folder.
          --fail-on <severity>    Exit code 1 if any finding is at least this severe (Low/Medium/High/Critical).
          --quiet                 Only print the summary.

        EXIT CODES
          0 = OK, 1 = findings at/above --fail-on, 2 = error.
        """;

    private static int Main(string[] args)
    {
        if (args.Length == 0 || args[0] is "-h" or "--help" or "help" or "/?")
        {
            Console.WriteLine(Help);
            return 0;
        }
        try
        {
            var a = new Args(args.Skip(1));
            return args[0].ToLowerInvariant() switch
            {
                "scan" => Scan(a),
                "restore" => Restore(a),
                "test" => Test(a),
                "list-rules" => ListRules(a),
                "export-profile" => ExportProfile(a),
                "make-sample" => MakeSample(a),
                _ => throw new ArgumentException($"Unknown command '{args[0]}'. Run without arguments for help."),
            };
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or DirectoryNotFoundException or InvalidDataException or System.Text.Json.JsonException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine("Error: " + ex.Message);
            return 2;
        }
    }

    private static CleanProfile LoadProfile(Args a)
    {
        var path = a.Value("--profile");
        return path == null ? ProfileStore.CreateDefault() : ProfileStore.Load(path);
    }

    private static int Scan(Args a)
    {
        var profile = LoadProfile(a);
        var o = profile.Options;
        o.SourceFolder = a.Value("--source") ?? o.SourceFolder;
        o.OutputFolder = a.Value("--output") ?? o.OutputFolder;
        if (a.Flag("--scan-only")) o.ScanOnly = true;
        if (a.Flag("--no-preserve-lines")) o.PreserveLineNumbers = false;
        if (a.Flag("--scrub-names")) o.ScrubFileNames = true;
        if (a.Flag("--mapping")) o.WriteMappingFile = true;
        if (a.Flag("--fixed-folder")) o.UseTimestampedRunFolder = false;
        if (a.Value("--style") is { } style) o.Style = Enum.Parse<ObfuscationStyle>(style, ignoreCase: true);
        var quiet = a.Flag("--quiet");
        Severity? failOn = a.Value("--fail-on") is { } f ? Enum.Parse<Severity>(f, ignoreCase: true) : null;

        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

        var log = new SyncProgress<string>(m => { if (!quiet) Console.WriteLine(m); });
        var lastPhase = "";
        var progress = new SyncProgress<RunProgress>(p =>
        {
            if (quiet || p.Phase == lastPhase && p.Done != p.Total) return;
            lastPhase = p.Phase;
            if (p.Total > 0) Console.WriteLine($"{p.Phase}: {p.Done:N0}/{p.Total:N0} files, {p.FindingsSoFar:N0} findings");
        });

        var result = new CleanRunner(profile, progress, log).Run(cts.Token);

        Console.WriteLine();
        Console.WriteLine($"Findings: {result.FindingCount:N0} in {result.FilesWithFindings:N0} of {result.FilesScanned:N0} scanned files");
        foreach (var s in Enum.GetValues<Severity>().Reverse())
            if (result.CountBySeverity(s) > 0) Console.WriteLine($"  {s,-9} {result.CountBySeverity(s),6:N0}");
        if (!o.ScanOnly) Console.WriteLine($"Scrubbed copy : {result.ScrubbedFolder}");
        Console.WriteLine($"Report        : {Path.Combine(result.ReportFolder, "report.html")}");
        if (result.Warnings.Count > 0) Console.WriteLine($"Warnings      : {result.Warnings.Count} (see report)");

        if (result.Cancelled) return 2;
        return failOn is { } level && result.Findings.Any(x => x.Severity >= level) ? 1 : 0;
    }

    private static int Restore(Args a)
    {
        var mapping = a.Value("--mapping") ?? throw new ArgumentException("--mapping is required.");
        var input = a.Value("--input") ?? throw new ArgumentException("--input is required.");
        var output = a.Value("--output") ?? throw new ArgumentException("--output is required.");
        var restorer = new Restorer(MappingFile.Load(mapping));
        var (files, replaced) = restorer.RestorePath(input, output, new SyncProgress<string>(Console.WriteLine));
        Console.WriteLine($"Restored {replaced:N0} placeholder(s) in {files:N0} file(s) -> {output}");
        return 0;
    }

    private static int Test(Args a)
    {
        var profile = LoadProfile(a);
        string text, name;
        if (a.Value("--file") is { } file) { text = TextFileCodec.Read(file).Text; name = Path.GetFileName(file); }
        else { text = a.Value("--text") ?? throw new ArgumentException("Give --file or --text."); name = ""; }

        var detector = new Detector(profile);
        foreach (var e in detector.Errors) Console.Error.WriteLine("Rule skipped: " + e);
        var groups = detector.Detect(text, name);
        var lines = new LineIndex(text);
        foreach (var g in groups)
            Console.WriteLine($"line {lines.GetLine(g.Start),5}  {g.Severity,-8}  {g.Primary.Rule.Rule.Id,-32} {ValueChecks.MaskedPreview(g.Value)}");
        Console.WriteLine($"{groups.Count} finding(s).");
        Console.WriteLine();
        var output = new Scrubber(profile.Options, new TokenProvider(profile.Options)).Apply(text, groups, name, out _);
        Console.WriteLine(output);
        return 0;
    }

    private static int ListRules(Args a)
    {
        var profile = LoadProfile(a);
        var all = a.Flag("--all");
        foreach (var group in profile.Rules.Where(r => all || r.Enabled).GroupBy(r => r.Category))
        {
            Console.WriteLine($"[{group.Key}]");
            foreach (var r in group)
                Console.WriteLine($"  {(r.Enabled ? "on " : "off")}  {r.Severity,-8}  {r.Id,-32} {r.Name}");
        }
        Console.WriteLine($"{profile.Rules.Count(r => r.Enabled)} of {profile.Rules.Count} rules enabled.");
        return 0;
    }

    private static int ExportProfile(Args a)
    {
        var path = a.Positional ?? throw new ArgumentException("Give the file to write, e.g. export-profile Default.profile.json");
        ProfileStore.Save(ProfileStore.CreateDefault(), path);
        Console.WriteLine($"Default profile written to {Path.GetFullPath(path)}");
        return 0;
    }

    private static int MakeSample(Args a)
    {
        var folder = a.Positional ?? throw new ArgumentException("Give the folder to create, e.g. make-sample C:\\Temp\\DirtySample");
        SampleProject.Create(folder);
        Console.WriteLine($"Sample project with fake secrets created in {Path.GetFullPath(folder)}");
        return 0;
    }

    /// <summary>Progress that runs the callback immediately on the calling thread (console output).</summary>
    private sealed class SyncProgress<T>(Action<T> action) : IProgress<T>
    {
        private readonly object _lock = new();
        public void Report(T value) { lock (_lock) action(value); }
    }

    /// <summary>Tiny argument parser: --name value, --flag, and one positional value.</summary>
    private sealed class Args
    {
        private readonly Dictionary<string, string?> _named = new(StringComparer.OrdinalIgnoreCase);
        public string? Positional { get; }

        private static readonly HashSet<string> Flags = new(StringComparer.OrdinalIgnoreCase)
        {
            "--scan-only", "--no-preserve-lines", "--scrub-names", "--mapping", "--fixed-folder", "--quiet", "--all",
        };

        public Args(IEnumerable<string> args)
        {
            var list = args.ToList();
            for (int i = 0; i < list.Count; i++)
            {
                var arg = list[i];
                if (arg.StartsWith("--"))
                {
                    // "--mapping" is a flag for scan but takes a value for restore.
                    bool takesValue = i + 1 < list.Count && !list[i + 1].StartsWith("--") && (!Flags.Contains(arg) || arg == "--mapping" && LooksLikePath(list[i + 1]));
                    _named[arg] = takesValue ? list[++i] : null;
                }
                else Positional ??= arg;
            }
        }

        private static bool LooksLikePath(string s) => s.EndsWith(".json", StringComparison.OrdinalIgnoreCase);

        public string? Value(string name) => _named.TryGetValue(name, out var v) ? v : null;
        public bool Flag(string name) => _named.ContainsKey(name);
    }
}
