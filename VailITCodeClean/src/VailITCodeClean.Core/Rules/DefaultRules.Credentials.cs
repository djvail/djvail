using VailIT.CodeClean.Configuration;

namespace VailIT.CodeClean.Rules;

public static partial class DefaultRules
{
    /// <summary>Passwords, user names, generic secrets/tokens and credentials passed on command lines.</summary>
    public static IEnumerable<FilterRule> Credentials()
    {
        const string c = CatCredentials;

        // ---- passwords -------------------------------------------------------------------------
        yield return Rule("password-assignment", "Password in code / quoted setting", c, Severity.High, "PASSWORD",
                QuotedAssignment("""pass(?:word|wd|phrase|code)?|pwd|pswd|passwort|contrase[nñ]a|mot_?de_?passe|sa_?pwd"""),
                "A quoted value assigned to a name ending in password, passwd, pwd, passphrase... in any language or JSON/YAML/XML.",
                """var dbPassword = "Spring2024!";""")
            .IgnorePlaceholders().MinLen(3);

        yield return Rule("password-config-line", "Password in config file (unquoted)", c, Severity.High, "PASSWORD",
                LineAssignment("""pass(?:word|wd|phrase)?|pwd"""),
                "NAME=value / name: value lines in .env, .ini, .properties, YAML, shell scripts and similar files.",
                """DB_PASSWORD=Spring2024!""")
            .Files(ConfigFiles).IgnorePlaceholders()
            .IgnoreValue("""(?i)^(?:\$\{[^}]*\}|\$\(.*\)|\$\w+|%\w+%|\{\{.*\}\}|<[^>]*>|null|none|true|false|~|\|[-+]?|>[-+]?|['"]{2})$|[()\[\]{};]$""")
            .MinLen(3);

        yield return Rule("connection-string-password", "Password in connection string", c, Severity.Critical, "PASSWORD",
                """(?i)(?:(?<=[;"'`])\s*(?:Password|Pwd)\s*=(?!=)\s*|^[ \t]*(?:Password|Pwd)=(?!=))(?<secret>'[^'\r\n]+'|"[^"\r\n]+"|\{[^}\r\n]+\}|[^;"'`\r\n<>\s][^;"'`\r\n<>]*)""",
                "Password=... or Pwd=... inside SQL Server, MySQL, PostgreSQL, Oracle and ODBC connection strings.",
                """Server=sql01;Database=Sales;User Id=app;Password=Spring2024!;""")
            .IgnoreValue("""(?i)^\s*["']?\s*(?:\$\{.*\}|\$\(.*\)|%\w+%|\{\d+\}|\{\w+\}|<.*>|\*+|\[\w+\])?\s*["']?\s*$""")
            .MinLen(2);

        yield return Rule("xml-key-value-secret", "Secret in <add key=... value=...>", c, Severity.High, "SECRET",
                """(?i)<add\s+(?:name|key)\s*=\s*"[^"]*(?:pass(?:word)?|pwd|secret|token|api_?key|apikey|credential|accountkey|sas|signature|privatekey|clienttextpassword|cleartextpassword)[^"]*"[^>]*?\s(?:value|connectionString)\s*=\s*"(?<secret>[^"]+)""",
                "app.config / web.config / NuGet.config entries like <add key=\"SmtpPassword\" value=\"...\"/>.",
                """<add key="SmtpPassword" value="Spring2024!" />""")
            .IgnorePlaceholders();

        yield return Rule("xml-element-secret", "Secret in XML element", c, Severity.High, "SECRET",
                """(?i)<(?<tag>[\w:.\-]*(?:password|passwd|pwd|secret|apikey|api_key|token|clientsecret|privatekey|passphrase|credential))\b[^>]*>(?<secret>[^<\r\n]{2,512})</\k<tag>\s*>""",
                "Values inside elements such as <Password>...</Password>, <ApiKey>...</ApiKey> (Maven settings.xml, .pubxml, FileZilla, etc.).",
                """<Password>Spring2024!</Password>""")
            .IgnorePlaceholders();

        // ---- user names -------------------------------------------------------------------------
        yield return Rule("username-assignment", "User name in code / quoted setting", c, Severity.Medium, "USERNAME",
                QuotedAssignment("""user(?:_?name)?|login(?:_?name)?|user_?id|uid|admin_?user|db_?user|sa_?user|account_?name|service_?account|svc_?account|principal(?:_?name)?|upn|samaccountname|run_?as""", noSpaces: true),
                "A quoted value assigned to user, username, login, uid, serviceAccount... Common built-in names (admin, root, sa...) are ignored.",
                """string sqlUser = "svc_sales_app";""")
            .IgnorePlaceholders().MinLen(2)
            .Ignore("admin", "administrator", "root", "sa", "guest", "user", "test", "anonymous", "postgres", "nobody", "system", "username", "userid", "login", "default", "sysadmin", "dbo", "public", "everyone");

        yield return Rule("username-config-line", "User name in config file (unquoted)", c, Severity.Medium, "USERNAME",
                LineAssignment("""user(?:_?name)?|login|uid|db_?user|admin_?user|service_?account"""),
                "NAME=value / name: value user name lines in .env, .ini, .properties, YAML, shell scripts and similar files.",
                """DB_USERNAME=svc_sales_app""")
            .Files(ConfigFiles).IgnorePlaceholders().MinLen(2)
            .IgnoreValue("""(?i)^(?:\$\{[^}]*\}|\$\(.*\)|\$\w+|%\w+%|\{\{.*\}\}|<[^>]*>|null|none|true|false|~)$|[()\[\]{};]$""")
            .Ignore("admin", "administrator", "root", "sa", "guest", "user", "test", "anonymous", "postgres", "nobody", "system");

        yield return Rule("connection-string-user", "User name in connection string", c, Severity.Medium, "USERNAME",
                """(?i)(?:(?<=[;"'`])\s*(?:User\s?ID|UID|User\s?Name|User|Login\s?ID)\s*=(?!=)\s*|^[ \t]*(?:User\s?ID|UID|User\s?Name|User|Login\s?ID)=(?!=))(?<secret>[^;"'`\r\n<>]+)""",
                "User Id=..., UID=..., User=... inside connection strings.",
                """Server=sql01;Database=Sales;User Id=svc_sales_app;Password=x;""")
            .IgnoreValue("""(?i)^\s*(?:\$\{.*\}|%\w+%|\{\d+\}|\{\w+\}|<.*>)?\s*$""")
            .Ignore("sa", "root", "postgres");

        yield return Rule("windows-domain-user", "Windows DOMAIN\\user account", c, Severity.Medium, "DOMAIN_USER",
                """(?<![\w\\.$%/\-])(?<secret_DOMAIN>[A-Z][A-Z0-9\-]{1,14})(?:\\\\|\\)(?<secret_USERNAME>[a-zA-Z][\w.\-]{2,30})\b""",
                "Down-level logon names such as CONTOSO\\jsmith or CONTOSO\\\\svc_app (both the domain and the user are replaced).",
                """runas /user:CONTOSO\jsmith cmd.exe""")
            .Cs()
            .IgnoreValue("""^(?:HK(?:LM|CU|CR|CC|U)|HKEY_\w+|BUILTIN|NT|NT AUTHORITY|IIS|SYSTEM32|WINDOWS|PROGRA~\d|SOFTWARE|SYSTEM|TEMP|TMP|LOCAL|CON|PRN|AUX|NUL|COM\d|LPT\d|ERROR|WARN|INFO|DEBUG|TRACE|AUTHORITY|SERVICE|NETWORK|WINDIR|SYSTEMROOT|PROGRAMFILES|PROGRAMDATA|APPDATA|LOCALAPPDATA|USERPROFILE|COMPUTERNAME|DOMAIN|SERVER|MACHINE|USER|NET|BIN|OBJ|SRC|DOCS|LIB)$""")
            .AllParts()
            .IgnoreLine("""(?i)\bHK(?:LM|CU|EY_)|\bNT AUTHORITY\\|\\[nrt]["']""");

        yield return Rule("upn-service-account", "Service account / UPN", c, Severity.Medium, "USERNAME",
                """(?i)\b(?:svc|sa|srv|service|app|sql|adm|admin)[_.\-][\w.\-]{2,40}@[\w\-]+(?:\.[\w\-]+)+\b""",
                "User principal names of service accounts such as svc_backup@contoso.com.",
                """svc_backup@contoso.local""");

        // ---- generic secrets ----------------------------------------------------------------------
        yield return Rule("secret-assignment", "Secret / token / API key in code", c, Severity.High, "SECRET",
                """(?i)(?<![\w$])(?!(?:public_?key_?token|publicKey|public_?key|token_?type|grant_?type|key_?type|key_?size|key_?length|token_?endpoint|token_?url|token_?uri|key_?vault_?(?:name|uri|url)|secret_?name|key_?name|key_?id)["']?\s*[=:])""" + QuotedAssignment("""secret|client_?secret|app_?secret|consumer_?secret|shared_?secret|secret_?key|api_?key|apikey|api_?secret|api_?token|access_?key(?:_?id)?|access_?token|auth_?token|auth_?key|refresh_?token|bearer_?token|id_?token|session_?token|session_?secret|cookie_?secret|csrf_?secret|token|private_?key|signing_?key|encryption_?key|enc_?key|aes_?key|hmac_?key|master_?key|license_?key|licence_?key|subscription_?key|webhook_?secret|jwt_?secret|jwt_?key|client_?key|app_?key|consumer_?key|credentials?|salt|pepper|aes_?iv|init(?:ialization)?_?vector|nonce|otp_?secret|totp_?secret|mfa_?secret|recovery_?code""", noSpaces: true),
                "A quoted value without spaces assigned to a name ending in secret, apiKey, token, accessKey, privateKey, salt... (publicKeyToken, tokenType, keyVaultName and similar names are skipped).",
                """const stripeApiKey = "a8F3kL0pQ7zX2mN5vB9cR4tY6uI1oP3s";""")
            .IgnorePlaceholders().MinLen(8)
            .IgnoreValue(VersionValue).IgnoreValue(FileNameValue).NotFiles(LockFiles);

        yield return Rule("secret-config-line", "Secret / token / API key in config file (unquoted)", c, Severity.High, "SECRET",
                LineAssignment("""secret|client_?secret|secret_?key|api_?key|apikey|api_?secret|api_?token|access_?key(?:_?id)?|access_?token|auth_?token|refresh_?token|token|private_?key|signing_?key|encryption_?key|master_?key|license_?key|webhook_?secret|jwt_?secret|credentials?|salt|_authToken|_auth"""),
                "NAME=value / name: value secret lines in .env, .ini, .properties, YAML, .npmrc and similar files.",
                """STRIPE_API_KEY=a8F3kL0pQ7zX2mN5vB9cR4tY6uI1oP3s""")
            .Files(ConfigFiles).IgnorePlaceholders().MinLen(8)
            .IgnoreValue("""(?i)^(?:\$\{[^}]*\}|\$\(.*\)|\$\w+|%\w+%|\{\{.*\}\}|<[^>]*>|null|none|true|false|~|\|[-+]?|>[-+]?)$|[()\[\]{};]$|\s""");

        yield return Rule("high-entropy-assignment", "High-entropy value assigned to key-like name", c, Severity.Medium, "SECRET",
                """(?i)(?<![\w$])(?!(?:key|name|id|keyname|keyid)["']?\s*=)[\w.\-$]{0,40}?(?:key|secret|token|pass|pwd|auth|cred|sig|signature|hash|seed|cipher|crypt)\w{0,20}["']?\s*(?:=|:=|:|=>)\s*@?(["'`])(?<secret>[A-Za-z0-9+/=_\-.~!@#$%^&*]{20,512})\1""",
                "Catches random-looking strings (keys, hashes, tokens) assigned to anything with key/secret/token/hash/sig in its name.",
                """ "cacheSignatureSeed": "Zq8Lm2Xv9Rt4Wc7Ny1Bp5Hs3Kd6Fg0Ja",""")
            .Entropy(3.6).IgnorePlaceholders()
            .IgnoreValue("""^[^0-9]+$|^[0-9]+$""") // real keys mix letters and digits
            .IgnoreValue(FileNameValue)
            .IgnoreLine("""(?i)\b(?:publicKeyToken|hashPath|"sha512"|"integrity")\b""")
            .NotFiles(LockFiles);

        yield return Rule("network-credential", "new NetworkCredential(user, password)", c, Severity.Critical, "PASSWORD",
                """new\s+(?:System\.Net\.)?NetworkCredential\s*\(\s*@?"(?<secret_USERNAME>[^"]+)"\s*,\s*@?"(?<secret_PASSWORD>[^"]*)"(?:\s*,\s*@?"(?<secret_DOMAIN>[^"]+)")?""",
                ".NET NetworkCredential created with hard-coded user name, password and optional domain.",
                """var cred = new NetworkCredential("svc_ftp", "Spring2024!", "CONTOSO");""");

        yield return Rule("basic-auth-encode", "Basic auth built from user:password", c, Severity.Critical, "BASIC_AUTH",
                """(?i)(?:GetBytes|btoa|b64encode|encodeToString|Base64\.encode\w*)\s*\(\s*[bf]?["'](?<secret_USERNAME>[^:"'\s]{1,64}):(?<secret_PASSWORD>[^"'\r\n]{1,128})["']""",
                "Code that Base64-encodes a hard-coded \"user:password\" string for an Authorization header.",
                """var auth = Convert.ToBase64String(Encoding.ASCII.GetBytes("apiuser:Spring2024!"));""");

        // ---- command lines ----------------------------------------------------------------------
        yield return Rule("powershell-securestring", "PowerShell ConvertTo-SecureString -AsPlainText", c, Severity.Critical, "PASSWORD",
                """(?i)ConvertTo-SecureString\s+(?:-String\s+)?(["'])(?<secret>(?:(?!\1).){1,256})\1(?=[^\r\n]*-AsPlainText)""",
                "Plain-text passwords turned into SecureString objects in PowerShell scripts.",
                """$pw = ConvertTo-SecureString "Spring2024!" -AsPlainText -Force""");

        yield return Rule("cli-password-argument", "Password / token passed as a command-line argument", c, Severity.High, "PASSWORD",
                """(?i)(?<![\w\-])(?:--?|/)(?:password|passwd|pass|pwd|secret|token|api-?key|client-?secret|access-?key|secret-?key|auth-?token|db-?password|admin-?password|storepass|keypass|passphrase)(?:[ \t]*=[ \t]*|[ \t]+|:)(["']?)(?<secret>(?!-)[^\s"'`|;&]{3,256})\1""",
                "Arguments like --password X, -Password 'X', /p:Password=X, -storepass X in scripts, Dockerfiles and CI files.",
                """docker login myregistry -u admin --password Spring2024!""")
            .IgnorePlaceholders()
            .IgnoreValue("""(?i)^(?:\$\{?[\w:]+\}?|\$\(.*\)|%\w+%|\{\{.*\}\}|<[^>]*>|stdin|file|env|prompt|-)$""");

        yield return Rule("sqlcmd-password", "sqlcmd / bcp / mysql password switch", c, Severity.High, "PASSWORD",
                """(?:\b(?:sqlcmd|osql|bcp|isql)\b[^\r\n]*?\s-P\s*|\bmysql(?:dump|admin)?\b[^\r\n]*?\s-p)(["']?)(?<secret>[^\s"']{2,128})\1""",
                "sqlcmd -P password, bcp -P password, mysql -pPassword.",
                """sqlcmd -S sql01 -U sa -P Spring2024! -Q "select 1" """)
            .Cs().IgnorePlaceholders();

        yield return Rule("curl-user", "curl -u user:password", c, Severity.Critical, "BASIC_AUTH",
                """(?i)\b(?:curl|wget|http)\b[^\r\n]*?\s(?:-u|--user|--http-user)[ =]+(["']?)(?<secret_USERNAME>[^:\s"']{1,64}):(?<secret_PASSWORD>[^\s"']{1,128})\1""",
                "curl/wget calls with embedded credentials.",
                """curl -u deploy:Spring2024! https://build.contoso.local/api""");

        yield return Rule("net-use-user", "net use / runas /user:", c, Severity.Medium, "USERNAME",
                """(?i)\b(?:net\s+use|runas|psexec|schtasks|sc\.exe|cmdkey)\b[^\r\n]*?/(?:user|u|ru|generic):(?<secret>[^\s"']+)""",
                "User names passed to net use, runas, psexec, schtasks and cmdkey.",
                """net use Z: \\fs01\share /user:svc_backup""");

        yield return Rule("mount-credentials", "Credentials in mount / fstab options", c, Severity.High, "USERNAME",
                """(?i)(?<=\b(?:mount|cifs|smbfs|smb3|nfs4?|fstab|mount\.cifs)\b[^\r\n]*[\s,])(?:(?<user>username|user)|(?<pw>password|pass)|domain)=(?<secret>[^,\s"';]+)""",
                "username=, password= and domain= options of mount -o and /etc/fstab CIFS/SMB entries.",
                "mount -t cifs //nas01/backups /mnt/backups -o username=jsmith,password=Spring2024!")
            .IgnorePlaceholders();

        yield return Rule("sshpass", "sshpass -p password", c, Severity.Critical, "PASSWORD",
                """\bsshpass\s+-p\s*(["']?)(?<secret>[^\s"']+)\1""",
                "Passwords handed to ssh through sshpass.",
                """sshpass -p Spring2024! ssh deploy@10.1.2.3""")
            .Cs();

        // ---- network devices ----------------------------------------------------------------------
        yield return Rule("snmp-community", "SNMP community string", c, Severity.High, "SNMP_COMMUNITY",
                """(?i)(?:\b(?:snmp-server\s+community|rocommunity6?|rwcommunity6?)\s+|\b(?:snmp_?)?(?:community(?:_?string)?|read_?community|write_?community)["']?\s*[=:]\s*["']?)(?<secret>[^\s"';,]{3,64})""",
                "SNMP community strings in device configs, monitoring settings and scripts.",
                """snmp-server community N3tw0rkRO RO""")
            .IgnorePlaceholders();

        yield return Rule("wifi-psk", "Wi-Fi / VPN pre-shared key", c, Severity.High, "PSK",
                """(?i)\b(?:wpa_?psk|wpa_?passphrase|psk|pre[_\-]?shared[_\-]?key|preshared[_\-]?key|wifi_?password|wlan_?key|ipsec[_\-]?secret|ike[_\-]?psk)["']?\s*(?:=|:|\s)\s*["']?(?<secret>[^\s"';,]{8,128})""",
                "Wireless passphrases and IPsec/IKE pre-shared keys.",
                """wpa_passphrase="Corp-WiFi-2024!" """)
            .IgnorePlaceholders();

        yield return Rule("cisco-secret", "Cisco enable secret / password hash", c, Severity.High, "DEVICE_SECRET",
                """(?i)\b(?:enable\s+(?:secret|password)|username\s+\S+\s+(?:privilege\s+\d+\s+)?(?:secret|password))\s+(?:\d\s+)?(?<secret>\S{4,})""",
                "Cisco IOS enable secrets and local user passwords (type 0, 5, 7, 8, 9).",
                """enable secret 5 $1$mERr$hx5rVt7rPNoS4wqbXKX7m0""");
    }
}
