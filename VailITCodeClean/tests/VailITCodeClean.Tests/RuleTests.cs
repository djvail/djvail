using VailIT.CodeClean.Configuration;
using VailIT.CodeClean.Engine;

namespace VailIT.CodeClean.Tests;

/// <summary>Checks every built-in rule compiles and catches its own example.</summary>
public class RuleTests
{
    public static IEnumerable<object[]> AllRuleIds() =>
        ProfileStore.CreateDefault().Rules.Select(r => new object[] { r.Id });

    [Fact]
    public void Rule_ids_are_unique()
    {
        var dupes = ProfileStore.CreateDefault().Rules.GroupBy(r => r.Id).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        Assert.Empty(dupes);
    }

    [Fact]
    public void Has_lots_of_sample_rules()
    {
        Assert.True(ProfileStore.CreateDefault().Rules.Count >= 120);
    }

    [Theory]
    [MemberData(nameof(AllRuleIds))]
    public void Rule_compiles_and_matches_its_example(string id)
    {
        var profile = ProfileStore.CreateDefault();
        var rule = profile.Rules.Single(r => r.Id == id);
        Assert.Null(CompiledRule.Validate(rule));
        Assert.False(string.IsNullOrWhiteSpace(rule.Example), $"Rule {id} has no example");

        var detector = new Detector(profile, [rule]);
        var example = ExampleText.Expand(rule.Example);
        var hits = detector.FindAll(example);
        Assert.True(hits.Count > 0, $"Rule {id} did not match its example: {example}");

        // Windows line endings and surrounding lines must not stop it matching ("$" does not match before \r).
        var crlf = "first line\r\n" + example.ReplaceLineEndings("\r\n") + "\r\nlast line\r\n";
        Assert.True(detector.FindAll(crlf).Count > 0, $"Rule {id} did not match its example with CRLF line endings");
    }

    [Fact]
    public void Exact_rule_matches_each_line_as_literal_text()
    {
        var profile = ProfileStore.CreateDefault();
        var rule = new FilterRule { Id = "x", Type = PatternType.Exact, Pattern = "Contoso (Ltd)\nFab.rikam", WholeWord = true, TokenLabel = "CO" };
        var hits = new Detector(profile, [rule]).FindAll("by Contoso (Ltd) and Fab.rikam but not Fabxrikam or ContosoX");
        Assert.Equal(["Contoso (Ltd)", "Fab.rikam"], hits.Select(h => h.Value));
    }

    [Fact]
    public void Clean_code_is_not_flagged_high()
    {
        // Everyday code that must not be reported as High/Critical.
        const string code = """
            using System;
            namespace Demo;
            public class Login
            {
                public string Password { get; set; } = string.Empty;
                public bool Check(string password) => password == Password;
                public string Load() { var password = GetPasswordFromVault(); return password; }
                private string GetPasswordFromVault() => Environment.GetEnvironmentVariable("DB_PASSWORD")!;
                // see https://learn.microsoft.com/dotnet/api/system.string
                private const string Schema = "http://schemas.microsoft.com/winfx/2006/xaml";
                public static readonly Version V = new Version("1.2.3.4");
                public int PasswordMinLength = 12;
                public string token = "${API_TOKEN}";
                public string label = "Enter your password";
            }
            """;
        var profile = ProfileStore.CreateDefault();
        var groups = new Detector(profile).Detect(code, "src/Login.cs");
        var serious = groups.Where(g => g.Severity >= Severity.High).Select(g => $"{g.Primary.Rule.Rule.Id}: {g.Value}").ToList();
        Assert.Empty(serious);
    }

    [Fact]
    public void Assignment_rule_keeps_the_name_and_replaces_the_value()
    {
        var profile = ProfileStore.CreateDefault();
        var groups = new Detector(profile).Detect("var dbPassword = \"Spring2024!\";", "a.cs");
        var g = Assert.Single(groups);
        Assert.Equal("Spring2024!", g.Value);
    }

    [Fact]
    public void Ip_in_assembly_version_is_ignored()
    {
        var profile = ProfileStore.CreateDefault();
        Assert.Empty(new Detector(profile).Detect("[assembly: AssemblyVersion(\"10.0.0.1\")]", "AssemblyInfo.cs"));
        Assert.NotEmpty(new Detector(profile).Detect("var host = \"10.0.0.1\";", "a.cs"));
    }

    [Fact]
    public void Luhn_validation_filters_random_numbers()
    {
        Assert.True(ValueChecks.PassesLuhn("4539 1488 0343 6467"));
        Assert.False(ValueChecks.PassesLuhn("4539 1488 0343 6468"));
    }

    [Fact]
    public void Default_profile_round_trips_through_json()
    {
        var path = Path.Combine(Path.GetTempPath(), $"vailit-{Guid.NewGuid():N}.profile.json");
        try
        {
            var original = ProfileStore.CreateDefault();
            ProfileStore.Save(original, path);
            var loaded = ProfileStore.Load(path);
            Assert.Equal(original.Rules.Count, loaded.Rules.Count);
            Assert.Equal(original.Rules[0].Pattern, loaded.Rules[0].Pattern);
            Assert.Equal(original.Options.SensitiveFilePatterns, loaded.Options.SensitiveFilePatterns);
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData("*.config", "src/web.config", true)]
    [InlineData("*.config", "src/web.configs", false)]
    [InlineData("id_rsa", "home/.ssh/id_rsa", true)]
    [InlineData("src/**/appsettings*.json", "src/a/b/appsettings.Production.json", true)]
    [InlineData("src/**/appsettings*.json", "src/appsettings.json", true)]
    [InlineData("src/**/appsettings*.json", "test/appsettings.json", false)]
    [InlineData(".env.*", ".env.local", true)]
    public void Glob_matching(string glob, string path, bool expected)
    {
        Assert.Equal(expected, new GlobMatcher([glob]).IsMatch(path));
    }
}
