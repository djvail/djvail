using System.Text;
using VailIT.CodeClean.Configuration;
using VailIT.CodeClean.Engine;
using VailIT.CodeClean.Reporting;
using VailIT.CodeClean.Samples;

namespace VailIT.CodeClean.Tests;

public class EngineTests
{
    private static string Scrub(string text, Action<ScanOptions>? configure = null, string file = "a.txt")
    {
        var profile = ProfileStore.CreateDefault();
        configure?.Invoke(profile.Options);
        var groups = new Detector(profile).Detect(text, file);
        return new Scrubber(profile.Options, new TokenProvider(profile.Options)).Apply(text, groups, file, out _);
    }

    [Fact]
    public void Multi_line_key_keeps_line_count_when_preserving()
    {
        var key = ExampleText.Expand("a\r\n-----BEGIN RSA PRIVATE KEY-----\r\n{{B64:64}}\r\n{{B64:64}}\r\n-----END RSA PRIVATE KEY-----\r\nb\r\n");
        var output = Scrub(key);
        Assert.Equal(key.Split('\n').Length, output.Split('\n').Length);
        Assert.Contains("-----BEGIN RSA PRIVATE KEY-----\r\nVAILIT_PRIVATE_KEY_0001\r\nVAILIT_PRIVATE_KEY_0001\r\n-----END", output);
        Assert.EndsWith("b\r\n", output);
    }

    [Fact]
    public void Multi_line_key_collapses_when_not_preserving()
    {
        var key = ExampleText.Expand("-----BEGIN RSA PRIVATE KEY-----\n{{B64:64}}\n{{B64:64}}\n-----END RSA PRIVATE KEY-----");
        var output = Scrub(key, o => o.PreserveLineNumbers = false);
        Assert.Equal("-----BEGIN RSA PRIVATE KEY-----VAILIT_PRIVATE_KEY_0001-----END RSA PRIVATE KEY-----", output);
    }

    [Fact]
    public void Same_value_gets_same_token()
    {
        var output = Scrub("a = \"10.1.2.3\"\nb = \"10.1.2.4\"\nc = \"10.1.2.3\"\n");
        Assert.Equal("a = \"VAILIT_PRIVATE_IP_0001\"\nb = \"VAILIT_PRIVATE_IP_0002\"\nc = \"VAILIT_PRIVATE_IP_0001\"\n", output);
    }

    [Fact]
    public void Mask_style_keeps_length()
    {
        var input = "Server=x;Password=Spring2024!;";
        var output = Scrub(input, o => o.Style = ObfuscationStyle.Mask);
        Assert.Equal(input.Length, output.Length);
        Assert.Contains("Password=***********;", output);
    }

    [Fact]
    public void Hash_template_is_stable_with_salt()
    {
        void Configure(ScanOptions o) { o.TokenTemplate = "{PREFIX}_{LABEL}_{HASH}"; o.HashSalt = "team-salt"; }
        var a = Scrub("ip 10.1.2.3", Configure);
        var b = Scrub("ip 10.1.2.3", Configure);
        Assert.Equal(a, b);
        Assert.Matches("^ip VAILIT_PRIVATE_IP_[0-9A-F]{8}$", a);
    }

    [Fact]
    public void Url_with_password_is_fully_hidden()
    {
        var output = Scrub("RABBIT_URL=amqp://orders:Winter2025#@rabbit01.contoso.local:5672/", file: ".env");
        Assert.DoesNotContain("Winter2025", output);
        Assert.DoesNotContain("rabbit01", output);
        Assert.DoesNotContain("orders:", output);
    }

    [Fact]
    public void Encoding_and_bom_are_preserved()
    {
        var text = "pwd = \"Spring2024!\"\r\nnaïve café\r\n";
        foreach (var enc in new Encoding[] { new UTF8Encoding(true), new UnicodeEncoding(false, true), Encoding.Latin1 })
        {
            var bytes = enc.GetPreamble().Concat(enc.GetBytes(text)).ToArray();
            var loaded = TextFileCodec.Decode(bytes);
            Assert.False(loaded.IsBinary);
            Assert.Equal(text, loaded.Text);
            var path = Path.GetTempFileName();
            try
            {
                TextFileCodec.Write(path, loaded, loaded.Text);
                Assert.Equal(bytes, File.ReadAllBytes(path));
            }
            finally { File.Delete(path); }
        }
    }

    [Fact]
    public void Full_run_on_sample_project()
    {
        var root = Path.Combine(Path.GetTempPath(), "vailit-test-" + Guid.NewGuid().ToString("N"));
        var src = Path.Combine(root, "Sample");
        var outDir = Path.Combine(root, "Out");
        try
        {
            SampleProject.Create(src);
            var profile = ProfileStore.CreateDefault();
            profile.Options.SourceFolder = src;
            profile.Options.OutputFolder = outDir;
            profile.Options.WriteMappingFile = true;

            var result = new CleanRunner(profile).Run();

            Assert.False(result.Cancelled);
            Assert.Empty(result.RuleErrors);
            Assert.True(result.FindingCount > 40, $"only {result.FindingCount} findings");
            Assert.True(File.Exists(Path.Combine(result.ReportFolder, "report.html")));
            Assert.True(File.Exists(Path.Combine(result.ReportFolder, "findings.csv")));
            Assert.True(File.Exists(Path.Combine(result.ReportFolder, "findings.json")));
            Assert.True(File.Exists(Path.Combine(result.ReportFolder, "mapping.json")));

            // sensitive file not copied, binary skipped, everything else present
            Assert.False(File.Exists(Path.Combine(result.ScrubbedFolder, "certs", "site.pfx")));
            Assert.Contains(result.Files, f => f.Path == "certs/site.pfx" && f.Status == FileStatus.Sensitive);
            Assert.Contains(result.Files, f => f.Path == "src/logo.png" && f.Status == FileStatus.BinarySkipped);

            // no planted value survives, and line counts are unchanged
            foreach (var file in Directory.EnumerateFiles(result.ScrubbedFolder, "*", SearchOption.AllDirectories))
            {
                var text = File.ReadAllText(file);
                foreach (var planted in SampleProject.PlantedValues)
                    Assert.False(text.Contains(planted, StringComparison.OrdinalIgnoreCase), $"'{planted}' left in {file}:\n{text}");
                var original = File.ReadAllText(Path.Combine(src, Path.GetRelativePath(result.ScrubbedFolder, file)));
                Assert.Equal(original.Split('\n').Length, text.Split('\n').Length);
            }

            // the clean lines in the sample stay untouched
            var ftp = File.ReadAllText(Path.Combine(result.ScrubbedFolder, "src", "Sales.Api", "Services", "LegacyFtp.cs"));
            Assert.Contains("public string GetPassword(string user) => Vault.Get(user + \":password\");", ftp);
            var asm = File.ReadAllText(Path.Combine(result.ScrubbedFolder, "src", "Sales.Api", "Properties", "AssemblyInfo.cs"));
            Assert.Contains("AssemblyVersion(\"1.2.3.4\")", asm);

            var secretYaml = File.ReadAllText(Path.Combine(result.ScrubbedFolder, "deploy", "k8s", "secret.yaml"));
            Assert.Contains("db-password: VAILIT_", secretYaml); // CRLF file: the line-end anchored rule must still work

            // restore puts everything back exactly
            var restored = Path.Combine(root, "Restored");
            var restorer = new Restorer(MappingFile.Load(Path.Combine(result.ReportFolder, "mapping.json")));
            restorer.RestorePath(result.ScrubbedFolder, restored);
            foreach (var file in Directory.EnumerateFiles(restored, "*", SearchOption.AllDirectories))
            {
                var rel = Path.GetRelativePath(restored, file);
                Assert.Equal(File.ReadAllText(Path.Combine(src, rel)), File.ReadAllText(file));
            }

            // a second run into the same fixed folder is allowed (marker file) ...
            profile.Options.UseTimestampedRunFolder = false;
            var fixedOut = Path.Combine(root, "Fixed");
            profile.Options.OutputFolder = fixedOut;
            new CleanRunner(profile).Run();
            new CleanRunner(profile).Run();
            // ... but a foreign non-empty folder is refused
            var foreign = Path.Combine(root, "Foreign");
            Directory.CreateDirectory(foreign);
            File.WriteAllText(Path.Combine(foreign, "keep.txt"), "mine");
            profile.Options.OutputFolder = foreign;
            Assert.Throws<IOException>(() => new CleanRunner(profile).Run());
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Scrub_file_names()
    {
        var root = Path.Combine(Path.GetTempPath(), "vailit-test-" + Guid.NewGuid().ToString("N"));
        try
        {
            var src = Path.Combine(root, "src");
            Directory.CreateDirectory(Path.Combine(src, "sql01.contoso.local"));
            File.WriteAllText(Path.Combine(src, "sql01.contoso.local", "notes.txt"), "hello");
            var profile = ProfileStore.CreateDefault();
            profile.Options.SourceFolder = src;
            profile.Options.OutputFolder = Path.Combine(root, "out");
            profile.Options.ScrubFileNames = true;
            var result = new CleanRunner(profile).Run();
            Assert.True(File.Exists(Path.Combine(result.ScrubbedFolder, "VAILIT_INTERNAL_HOST_0001", "notes.txt")));
        }
        finally { Directory.Delete(root, true); }
    }
}
