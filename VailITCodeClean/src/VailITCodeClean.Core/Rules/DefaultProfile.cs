using VailIT.CodeClean.Configuration;

namespace VailIT.CodeClean.Rules;

/// <summary>Builds the profile the app starts with: sensible options, the file filters and all sample rules.</summary>
public static class DefaultProfile
{
    public static CleanProfile Create() => new()
    {
        Name = "Default",
        Description = "All built-in sample rules for credentials, keys, cloud accounts, infrastructure, endpoints and personal data.",
        Options = new ScanOptions
        {
            ExcludedFolders =
            [
                ".git", ".svn", ".hg", ".vs", ".idea", "bin", "obj", "node_modules", "packages", "TestResults",
                ".terraform", ".terragrunt-cache", "__pycache__", ".venv", "venv", ".tox", ".mypy_cache", ".pytest_cache",
                ".gradle", ".next", ".nuxt", ".angular", ".sass-cache", ".parcel-cache", "bower_components",
                ".nuget", ".cache", "coverage", ".nyc_output",
            ],
            ExcludedFilePatterns =
            [
                "*.suo", "Thumbs.db", ".DS_Store", "desktop.ini", "*.swp", "*.swo", "*~",
            ],
            SensitiveFilePatterns =
            [
                // keys and certificates with private keys
                "*.pfx", "*.p12", "*.pkcs12", "*.jks", "*.keystore", "*.truststore", "*.bks", "*.key", "*.p8",
                "*.ppk", "id_rsa", "id_dsa", "id_ecdsa", "id_ed25519", "*.gpg", "*.pgp", "*.kdbx", "*.kdb",
                "*.psafe3", "*.1pux", "*.keychain", "*.keychain-db", "*.mobileprovision", "*.snk",
                // files that are full of secrets by design
                "*.tfstate", "*.tfstate.backup", "*.publishsettings", "*.pubxml.user", "*.azurePubxml.user",
                ".git-credentials", ".netrc", "_netrc", ".pgpass", ".my.cnf", "*.rdp", "*.ovpn", "*.har",
                ".bash_history", ".zsh_history", ".psql_history", ".mysql_history", "ConsoleHost_history.txt",
                // data and memory dumps
                "*.mdf", "*.ldf", "*.ndf", "*.bak", "*.bacpac", "*.sqlite", "*.sqlite3", "*.db", "*.dmp", "*.hprof", "*.mdmp",
            ],
        },
        AllowValues =
        [
            "localhost", "127.0.0.1", "::1", "0.0.0.0", "example.com", "example.org", "example.net",
            "user@example.com", "test@example.com", "admin@example.com", "(local)", "(localdb)\\MSSQLLocalDB",
        ],
        AllowPatterns =
        [
            // Placeholders produced by this tool (so re-scanning a cleaned folder finds nothing new).
            """^VAILIT_[A-Z0-9_]+_(?:\d{4,}|[0-9A-F]{8})$""",
        ],
        Rules = DefaultRules.All(),
    };
}
