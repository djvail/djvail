using VailIT.CodeClean.Configuration;

namespace VailIT.CodeClean.Rules;

public static partial class DefaultRules
{
    /// <summary>Servers, IP addresses, internal host names, network paths, databases and directory names.</summary>
    public static IEnumerable<FilterRule> Infrastructure()
    {
        const string c = CatInfrastructure;
        const string octet = """(?:25[0-5]|2[0-4]\d|1\d\d|[1-9]?\d)""";

        // Lines that usually contain version numbers rather than IP addresses.
        const string versionLine = """(?i)\b(?:assembly\w*version|file\s?version|product\s?version|version\s*[=:("]|<version>|"version"|\bv(?:er)?\.?\s*\d+\.\d+\.\d+\.\d+|PackageReference|package_version|sdk\s*version|__version__|bindingRedirect|oldVersion|newVersion|Version=\d)""";

        yield return Rule("ipv4-private", "Private IPv4 address (10.x, 172.16-31.x, 192.168.x)", c, Severity.High, "PRIVATE_IP",
                $$"""(?<![\w.])(?:10\.{{octet}}\.{{octet}}\.{{octet}}|172\.(?:1[6-9]|2\d|3[01])\.{{octet}}\.{{octet}}|192\.168\.{{octet}}\.{{octet}}|100\.(?:6[4-9]|[7-9]\d|1[01]\d|12[0-7])\.{{octet}}\.{{octet}})(?!\.?\d)(?![\w])""",
                "Internal addresses reveal your network layout (RFC 1918 and carrier-grade NAT ranges).",
                "Server=192.168.10.25;Database=Sales;")
            .IgnoreLine(versionLine).NotFiles(LockFiles);

        yield return Rule("ipv4", "IPv4 address", c, Severity.Medium, "IP",
                $$"""(?<![\w.])(?:{{octet}}\.){3}{{octet}}(?!\.?\d)(?![\w])""",
                "Any IPv4 address. 0.0.0.0, 127.0.0.1, 255.255.255.x masks and lines that look like version numbers are skipped.",
                "proxy_pass http://52.168.14.200:8080;")
            .IgnoreLine(versionLine).NotFiles(LockFiles)
            .Ignore("0.0.0.0", "127.0.0.1", "255.255.255.255", "255.255.255.0", "255.255.0.0", "255.0.0.0", "1.1.1.1", "8.8.8.8", "8.8.4.4")
            .IgnoreValue("""^(?:\d\.\d\.\d\.\d|1\.0\.0\.0|2\.0\.0\.0|3\.0\.0\.0|4\.0\.0\.0|127\.\d+\.\d+\.\d+|169\.254\.\d+\.\d+|224\.\d+\.\d+\.\d+)$""");

        yield return Rule("ipv6", "IPv6 address", c, Severity.Medium, "IPV6",
                """(?<![\w:.])(?:(?:[0-9A-Fa-f]{1,4}:){7}[0-9A-Fa-f]{1,4}|(?:[0-9A-Fa-f]{1,4}:){1,7}:(?:[0-9A-Fa-f]{1,4}(?::[0-9A-Fa-f]{1,4}){0,6})?|::(?:[0-9A-Fa-f]{1,4}:){1,6}[0-9A-Fa-f]{1,4})(?:/\d{1,3})?(?![\w:.])""",
                "Full and compressed IPv6 addresses (::1 and fe80:: link-local are skipped).",
                "listen [2001:db8:85a3::8a2e:370:7334]:443;")
            .Ignore("::1", "::", "fe80::")
            .IgnoreValue("""(?i)^(?:fe80:|::ffff:0|ff0\d:)|^[0-9a-f]{1,4}::$""")
            .IgnoreLine("""(?i)\b(?:std|boost|Qt|System|Microsoft)::|::\s*(?:new|class|$)""");

        yield return Rule("mac-address", "MAC address", c, Severity.Medium, "MAC",
                """(?<![\w:\-])(?:[0-9A-Fa-f]{2}[:\-]){5}[0-9A-Fa-f]{2}(?![\w:\-])""",
                "Network card hardware addresses (identify physical machines).",
                "HWADDR=00:1A:2B:3C:4D:5E")
            .Ignore("00:00:00:00:00:00", "ff:ff:ff:ff:ff:ff", "00-00-00-00-00-00", "FF-FF-FF-FF-FF-FF");

        yield return Rule("internal-hostname", "Internal host name (.local, .corp, .internal, .lan...)", c, Severity.High, "INTERNAL_HOST",
                """(?i)(?<![\w.\-/\\@])(?:[a-z0-9](?:[a-z0-9\-]{0,61}[a-z0-9])?\.)+(?:local|localdomain|internal|intranet|corp|lan|private|home\.arpa|ad|domain|priv|intra|loc|office)(?![\w\-])(?!\.\w)""",
                "Fully qualified names in private DNS zones, e.g. sql01.corp.contoso.local or build.internal.",
                "Data Source=sql01.contoso.local;")
            .IgnoreValue("""(?-i)^(?:[A-Z][A-Za-z0-9]*[a-z][A-Za-z0-9]*\.)+[A-Z][a-z]+$""") // code namespaces such as Contoso.Sales.Internal
            .NotFiles(LockFiles)
            .Ignore("localhost.localdomain", "host.docker.internal", "gateway.docker.internal", "kubernetes.docker.internal", "metadata.google.internal", "cluster.local", "svc.cluster.local", "default.svc.cluster.local", "myapp.local", "app.local");

        yield return Rule("k8s-service-host", "Kubernetes service DNS name", c, Severity.Medium, "K8S_SERVICE",
                """(?i)\b[a-z0-9\-]+\.[a-z0-9\-]+\.svc(?:\.cluster\.local)?\b""",
                "service.namespace.svc.cluster.local names reveal cluster layout.",
                "redis-master.payments.svc.cluster.local:6379");

        yield return Rule("connection-string-server", "Server / host in connection string", c, Severity.High, "DB_SERVER",
                """(?i)(?:(?<=[;"'`])\s*(?:Server|Data\s?Source|Host|Hostname|Address|Addr|Network\s?Address|Server\s?Name|DSN|Endpoint|Mongo(?:DB)?\s?Server|Failover\s?Partner)\s*=(?!=)\s*|^[ \t]*(?:Server|Data\s?Source|Host|Hostname|Address|Addr|Network\s?Address|Server\s?Name|DSN|Endpoint|Mongo(?:DB)?\s?Server|Failover\s?Partner)=(?!=))(?<secret>(?:tcp:|np:|lpc:)?[^;"'`\r\n<>]+)""",
                "Server=..., Data Source=..., Host=..., Failover Partner=... in database, Redis and message-bus connection strings.",
                "Server=PROD-SQL01\\SALES;Database=Sales;Trusted_Connection=True;")
            .Ignore("localhost", "(local)", ".", "(localdb)\\MSSQLLocalDB", "(localdb)\\mssqllocaldb", "127.0.0.1", "::1")
            .IgnoreValue("""(?i)^\s*(?:\$\{.*\}|%\w+%|\{\d+\}|\{\w+\}|<.*>|\|DataDirectory\|.*)?\s*$""");

        yield return Rule("connection-string-database", "Database name in connection string", c, Severity.Low, "DB_NAME",
                """(?i)(?:(?<=[;"'`])\s*(?:Database|Initial\s?Catalog|DbName|Default\s?Database)\s*=(?!=)\s*|^[ \t]*(?:Database|Initial\s?Catalog|DbName|Default\s?Database)=(?!=))(?<secret>[^;"'`\r\n<>]+)""",
                "Database=... / Initial Catalog=... names.",
                "Server=sql01;Initial Catalog=Payroll_Prod;Integrated Security=SSPI;")
            .Ignore("master", "tempdb", "model", "msdb", "postgres", "mysql", "admin", "test")
            .IgnoreValue("""(?i)^\s*(?:\$\{.*\}|%\w+%|\{\d+\}|\{\w+\}|<.*>)?\s*$""");

        yield return Rule("host-assignment", "Host / server name in code or settings", c, Severity.Medium, "HOST",
                QuotedAssignment("""host(?:_?name)?|server(?:_?name)?|db_?host|db_?server|sql_?server|smtp_?(?:host|server)|mail_?(?:host|server)|ldap_?(?:host|server)|ftp_?(?:host|server)|sftp_?host|ssh_?host|proxy_?host|redis_?host|rabbit(?:mq)?_?host|kafka_?(?:brokers?|bootstrap(?:_?servers)?)|bootstrap_?servers|broker(?:_?list)?|machine_?name|computer_?name|domain_?controller|dc_?host|jump_?host|bastion(?:_?host)?|vcenter|esxi_?host|nas_?host|printer""", noSpaces: true),
                "Quoted values assigned to host, server, smtpServer, dbHost, bootstrapServers, bastion...",
                "options.SmtpHost = \"mail01.contoso.com\";")
            .IgnorePlaceholders()
            .Ignore("localhost", "127.0.0.1", "0.0.0.0", "::1", "(local)", ".", "*", "+", "smtp.gmail.com", "smtp.office365.com", "smtp-mail.outlook.com");

        yield return Rule("cli-host-argument", "Server passed as a command-line argument", c, Severity.Medium, "HOST",
                """(?i)(?<![\w\-])(?:-ComputerName|-ServerInstance|-SqlInstance|-SqlServer|-Server|-ServerName|-HostName|-VMHost|-DomainController|--host|--hostname|--server|/server|/node|/computer)(?:[ \t]*[=:][ \t]*|[ \t]+)(["']?)(?<secret>[A-Za-z0-9][\w.\-\\]{1,100})\1""",
                "Host names in PowerShell (-ComputerName, -ServerInstance...), WMIC (/node:) and CLI (--host) arguments.",
                "Invoke-Command -ComputerName PROD-SQL01 -ScriptBlock { Get-Service }")
            .IgnorePlaceholders()
            .Ignore("localhost", "127.0.0.1", ".", "$env:COMPUTERNAME", "0.0.0.0");

        yield return Rule("host-config-line", "Host / server in config file (unquoted)", c, Severity.Medium, "HOST",
                LineAssignment("""host(?:_?name)?|server(?:_?name)?|db_?host|smtp_?(?:host|server)|ldap_?(?:host|server)|redis_?host|broker(?:s)?|bootstrap_?servers|proxy"""),
                "HOST=... / server: ... lines in .env, .ini, .properties, YAML and similar files.",
                "DB_HOST=prod-sql01.contoso.com")
            .Files(ConfigFiles).IgnorePlaceholders()
            .IgnoreValue("""(?i)^(?:\$\{[^}]*\}|\$\(.*\)|\$\w+|%\w+%|\{\{.*\}\}|<[^>]*>|null|none|true|false|~|localhost|127\.0\.0\.1|0\.0\.0\.0|\*)$|[()\[\]{};]$""");

        yield return Rule("unc-path", "UNC network path (\\\\server\\share)", c, Severity.High, "UNC_PATH",
                """(?<![\w\\])(?:\\\\){1,2}(?<secret>[A-Za-z0-9][\w.\-]{0,62})(?:\\){1,2}(?<secret_SHARE>[\w$.\- ]{1,80}?)(?=[\\"'\s;,)<>]|$)""",
                "Server and share names in \\\\fileserver\\share paths (single or doubled backslashes).",
                "var path = @\"\\\\fs01.contoso.local\\Finance$\\Reports\";")
            .AllParts()
            .IgnoreValue("""(?i)^(?:\?|\.|localhost|wsl\$|wsl\.localhost|tsclient|server|servername|fileserver|computer|computername|host|hostname|machine|myserver|yourserver|nas|share|sharename)$""");

        yield return Rule("smb-nfs-path", "SMB / NFS mount source", c, Severity.High, "NETWORK_PATH",
                """(?i)(?:\b(?:smb|cifs|nfs|afp)://|(?<=^|\s)//)(?<secret>[a-z0-9][\w.\-]{1,62})/(?<secret_SHARE>[\w$.\-]+)""",
                "//server/share in fstab, mount commands and smb:// URLs.",
                "//nas01/backups /mnt/backups cifs credentials=/root/.smb 0 0");

        yield return Rule("windows-user-path", "Windows user profile path", c, Severity.Medium, "USERNAME",
                """(?i)\b[A-Z]:(?:\\){1,2}(?:Users|Documents and Settings)(?:\\){1,2}(?<secret>[^\\/"'\s:*?<>|]{2,64})""",
                "C:\\Users\\<name>\\... paths reveal user names.",
                "<HintPath>C:\\Users\\jsmith\\source\\repos\\Lib\\bin\\Lib.dll</HintPath>")
            .Ignore("Public", "Default", "Default User", "All Users", "%USERNAME%", "$env:USERNAME", "${env:USERNAME}", "<username>", "username", "user", "runneradmin", "VssAdministrator");

        yield return Rule("unix-home-path", "Linux / macOS home folder path", c, Severity.Medium, "USERNAME",
                """(?<![\w.])/(?:home|Users)/(?<secret>[a-z_][\w.\-]{1,31})(?=/|\b)""",
                "/home/<name>/... and /Users/<name>/... paths reveal user names.",
                "export PATH=/home/jsmith/.local/bin:$PATH")
            .Cs()
            .Ignore("user", "username", "runner", "ubuntu", "ec2-user", "vscode", "node", "app", "admin", "Shared", "Guest", "root", "$USER", "jenkins", "circleci", "travis", "builder", "codespace");

        yield return Rule("ldap-dn", "LDAP / Active Directory distinguished name", c, Severity.Medium, "LDAP_DN",
                """(?i)\b(?:CN|OU|DC|UID)=[^,=\r\n"'<>;]+(?:\s*,\s*(?:CN|OU|DC|O|L|ST|C|UID)=[^,=\r\n"'<>;]+)+""",
                "CN=...,OU=...,DC=... paths reveal your directory structure, groups and accounts.",
                "searcher.SearchRoot = new DirectoryEntry(\"LDAP://OU=Finance,DC=contoso,DC=local\");")
            .IgnoreValue("""(?i)^(?:CN=\w+,\s*)?(?:DC=example,\s*DC=(?:com|org)|DC=contoso,\s*DC=com)$""");

        yield return Rule("db-uri", "Database / broker / service connection URI", c, Severity.High, "CONNECTION_URI",
                """(?i)\b(?:mongodb(?:\+srv)?|postgres(?:ql)?|mysql(?:x)?|mariadb|redis|rediss|valkey|amqps?|mssql|sqlserver|oracle|ldaps?|smtps?|imaps?|pop3s?|ftps?|sftp|ssh|scp|telnet|rdp|vnc|nats|kafka|mqtts?|cassandra|couchbases?|neo4j(?:\+s)?|bolt|clickhouse|snowflake|cockroachdb|influxdb|memcached|zookeeper|etcd|consul|elasticsearch|opensearch|rtsp|sip|git\+ssh|svn\+ssh|jdbc:[a-z0-9:]+|odbc|wasbs?|abfss?|adl|hdfs|gs|webhdfs|tcp|udp)://[^\s"'`<>()\[\]{}]+(?<![.,;:])""",
                "URIs such as mongodb+srv://..., postgresql://..., redis://..., amqp://..., jdbc:sqlserver://..., ldap://..., ssh://...",
                "spring.datasource.url=jdbc:postgresql://pg-prod.contoso.local:5432/orders")
            .IgnoreValue("""(?i)^[a-z0-9+:]+://(?:localhost|127\.0\.0\.1|0\.0\.0\.0|\[::1\]|host\.docker\.internal|example\.(?:com|org))(?:[:/]|$)""");

        yield return Rule("ssh-target", "SSH / SCP user@host", c, Severity.Medium, "SSH_TARGET",
                """(?i)\b(?:ssh|scp|sftp|rsync|ssh-copy-id)\b[^\r\n]*?\s(?<secret_USERNAME>[a-z_][\w.\-]{0,31})@(?<secret_HOST>[a-z0-9][\w.\-]{1,253})""",
                "user@host targets in ssh, scp, sftp and rsync commands.",
                "scp build.zip deploy@web01.contoso.com:/var/www/");

        yield return Rule("docker-private-registry", "Private container registry", c, Severity.Medium, "REGISTRY",
                """(?i)\b(?<secret>[a-z0-9][a-z0-9\-]*(?:\.[a-z0-9\-]+)+(?::\d{2,5})?)/(?:[a-z0-9._\-]+/)*[a-z0-9._\-]+(?::[\w.\-]+|@sha256:[a-f0-9]{64})(?=\s|$|["'])""",
                "Registry host in image references such as registry.contoso.com:5000/team/api:1.2. Public registries (docker.io, ghcr.io, mcr.microsoft.com, quay.io, gcr.io...) are skipped.",
                "FROM registry.contoso.com:5000/base/dotnet:8.0")
            .Files("Dockerfile", "*.dockerfile", "Containerfile", "*.yml", "*.yaml", "*.json", "*.tf", "*.sh", "*.ps1", "*.toml", "*.env")
            .IgnoreValue("""(?i)^(?:docker\.io|index\.docker\.io|registry-1\.docker\.io|ghcr\.io|mcr\.microsoft\.com|quay\.io|gcr\.io|[a-z\-]+\.gcr\.io|public\.ecr\.aws|registry\.k8s\.io|k8s\.gcr\.io|docker\.elastic\.co|registry\.gitlab\.com|nvcr\.io|cgr\.dev|lscr\.io|github\.com|gitlab\.com|bitbucket\.org)$""");

        yield return Rule("terraform-backend", "Terraform backend / state location", c, Severity.Medium, "TF_BACKEND",
                """(?i)\b(?:bucket|storage_account_name|container_name|resource_group_name|dynamodb_table|key_vault_name|workspace|organization|hostname)\s*=\s*"(?<secret>[^"$\r\n]{3,128})"(?=[^\r\n]*\r?$)""",
                "Bucket, storage account, resource group and organisation names in Terraform files.",
                "  storage_account_name = \"stcontosotfstate\"")
            .Files("*.tf", "*.tfvars", "*.hcl", "*.tf.json")
            .IgnorePlaceholders();

        yield return Rule("domain-controller-ou", "Domain / realm name setting", c, Severity.Medium, "AD_DOMAIN",
                QuotedAssignment("""domain(?:_?name)?|ad_?domain|netbios_?(?:name|domain)|realm|kerberos_?realm|default_?realm|workgroup|forest""", noSpaces: true),
                "Quoted values assigned to domain, adDomain, realm, workgroup, forest...",
                "string adDomain = \"CONTOSO.LOCAL\";")
            .IgnorePlaceholders()
            .Ignore("localhost", "WORKGROUP", "example.com", "example.org", "domain", "local");

        yield return Rule("port-binding-host", "Hosts in hosts file / nginx / Apache config", c, Severity.Medium, "HOST",
                """(?im)^[ \t]*(?:server_name|ServerName|ServerAlias|upstream|proxy_pass|ProxyPass(?:Reverse)?|Redirect|Host|HostName)[ \t]+(?:\S+[ \t]+)?(?<secret>[a-z0-9][\w.\-]*\.[a-z][\w.\-]*)""",
                "server_name, ServerName/ServerAlias, ssh_config Host/HostName entries.",
                "    server_name portal.contoso.com;")
            .Files("*.conf", "nginx*", "httpd*", "apache*", "*.vhost", "ssh_config", "config", "*.cfg", "hosts", "*.htaccess")
            .Ignore("localhost", "_");
    }
}
