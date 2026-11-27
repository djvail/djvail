using VailIT.CodeClean.Engine;

namespace VailIT.CodeClean.Samples;

/// <summary>
/// Writes a small fake project full of planted secrets, so you can see what the tool does before
/// pointing it at real code (File &gt; Create sample project in the app, or <c>make-sample</c> on the
/// command line). All "secrets" are generated fakes.
/// </summary>
public static class SampleProject
{
    /// <summary>Text of every planted value, so tests can check none of them survive scrubbing.</summary>
    public static readonly string[] PlantedValues =
    [
        "Spring2024!", "svc_sales_app", "PROD-SQL01", "192.168.10.25", "sql01.contoso.local", "jane.smith@contoso.com",
        "contosoprod", "Corp-WiFi-2024!", "N3tw0rkRO", "jsmith", "rabbit01.contoso.local", "Winter2025#",
        "fs01.contoso.local", "524-31-9876", "4539 1488 0343 6467", "orders-api.contoso.com", "pg-prod.contoso.local",
    ];

    public static void Create(string folder)
    {
        Directory.CreateDirectory(folder);
        foreach (var (path, content) in Files())
        {
            var full = Path.Combine(folder, path.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllText(full, ExampleText.Expand(content).ReplaceLineEndings(path.EndsWith(".sh") || path.EndsWith(".env") ? "\n" : "\r\n"));
        }
        // A "sensitive" file that must never be copied and a binary file.
        Directory.CreateDirectory(Path.Combine(folder, "certs"));
        File.WriteAllBytes(Path.Combine(folder, "certs", "site.pfx"), [0x30, 0x82, 0x0A, 0x00, 0x02, 0x01, 0x03]);
        File.WriteAllBytes(Path.Combine(folder, "src", "logo.png"), [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00]);
    }

    private static IEnumerable<(string Path, string Content)> Files()
    {
        yield return ("README.md", """
            # Sales Portal (SAMPLE - fake secrets for VailIT Code Clean)

            Deploy with `scp build.zip deploy@web01.contoso.local:/var/www/`.
            Questions: jane.smith@contoso.com or call (303) 555-0147.
            Clean code below should NOT be flagged:
            `var password = GetPasswordFromVault();`  `<Version>1.2.3.4</Version>`
            """);

        yield return ("src/Sales.Api/appsettings.json", """
            {
              "ConnectionStrings": {
                "Sales": "Server=PROD-SQL01;Database=Sales;User Id=svc_sales_app;Password=Spring2024!;TrustServerCertificate=True",
                "Reporting": "Data Source=sql01.contoso.local;Initial Catalog=Reporting_Prod;Integrated Security=SSPI;"
              },
              "Storage": "DefaultEndpointsProtocol=https;AccountName=contosoprod;AccountKey={{B64:86}}==;EndpointSuffix=core.windows.net",
              "AzureAd": {
                "Instance": "https://login.microsoftonline.com/",
                "TenantId": "{{HEX:8}}-{{HEX:4}}-{{HEX:4}}-{{HEX:4}}-{{HEX:12}}",
                "ClientId": "{{HEX:8}}-1111-2222-3333-{{HEX:12}}",
                "ClientSecret": "abc8Q~{{URLSAFE:34}}"
              },
              "Stripe": { "SecretKey": "sk_live_{{ALNUM:24}}" },
              "Smtp": { "Host": "mail01.contoso.local", "UserName": "alerts@contoso.com", "Password": "Winter2025#" },
              "OrdersApi": "https://orders-api.contoso.com/v2/orders",
              "Logging": { "LogLevel": { "Default": "Information" } }
            }
            """);

        yield return ("src/Sales.Api/Services/LegacyFtp.cs", """
            using System.Net;

            namespace Sales.Api.Services;

            public class LegacyFtp
            {
                // TODO: move to Key Vault
                private const string ApiKey = "a8F3kL0pQ7zX2mN5vB9cR4tY6uI1oP3s";
                private static readonly string Share = @"\\fs01.contoso.local\Finance$\Exports";

                public NetworkCredential Credentials() => new NetworkCredential("svc_ftp", "Spring2024!", "CONTOSO");

                public string Server => "192.168.10.25";

                // These are fine and should be left alone:
                public string GetPassword(string user) => Vault.Get(user + ":password");
                public bool IsValid(string password) => password.Length > 12;
            }
            """);

        yield return ("src/Sales.Api/Properties/AssemblyInfo.cs", """
            [assembly: AssemblyVersion("1.2.3.4")]
            [assembly: AssemblyFileVersion("10.0.0.1")]
            """);

        yield return ("deploy/.env", """
            # Production settings
            DB_HOST=pg-prod.contoso.local
            DB_USERNAME=svc_sales_app
            DB_PASSWORD=Spring2024!
            DATABASE_URL=postgresql://svc_sales_app:Spring2024!@pg-prod.contoso.local:5432/orders
            RABBIT_URL=amqp://orders:Winter2025#@rabbit01.contoso.local:5672/
            GITHUB_TOKEN=ghp_{{ALNUM:36}}
            AWS_ACCESS_KEY_ID=AKIA{{UPNUM:16}}
            AWS_SECRET_ACCESS_KEY={{B64:40}}
            SLACK_WEBHOOK=https://hooks.slack.com/services/T{{UPNUM:8}}/B{{UPNUM:8}}/{{ALNUM:24}}
            OPENAI_API_KEY=sk-proj-{{URLSAFE:64}}
            DB_PASSWORD_FROM_VAULT=${DB_PASSWORD}
            """);

        yield return ("deploy/deploy.sh", """
            #!/bin/bash
            sshpass -p Spring2024! ssh deploy@10.20.30.40 'systemctl restart sales'
            curl -u deploy:Winter2025# https://build.contoso.local/api/trigger
            mysql -h 10.20.30.41 -u root -pSpring2024! sales < seed.sql
            mount -t cifs //nas01/backups /mnt/backups -o username=jsmith
            export PATH=/home/jsmith/.local/bin:$PATH
            """);

        yield return ("deploy/Install.ps1", """
            $pw = ConvertTo-SecureString "Spring2024!" -AsPlainText -Force
            $cred = New-Object System.Management.Automation.PSCredential("CONTOSO\jsmith", $pw)
            Invoke-Command -ComputerName PROD-SQL01 -Credential $cred -ScriptBlock { Get-Service }
            Copy-Item C:\Users\jsmith\Downloads\setup.msi \\fs01.contoso.local\Installs$\
            """);

        yield return ("deploy/k8s/secret.yaml", """
            apiVersion: v1
            kind: Secret
            metadata:
              name: sales-db
              namespace: payments
            type: Opaque
            data:
              db-password: {{B64:24}}==
              api-token: {{B64:32}}
            ---
            # service: redis-master.payments.svc.cluster.local:6379
            """);

        yield return ("deploy/keys/deploy_key.pem", """
            -----BEGIN RSA PRIVATE KEY-----
            {{B64:64}}
            {{B64:64}}
            {{B64:64}}
            {{B64:40}}==
            -----END RSA PRIVATE KEY-----
            """);

        yield return ("network/core-switch.cfg", """
            hostname core-sw01
            enable secret 5 $1$mERr$hx5rVt7rPNoS4wqbXKX7m0
            snmp-server community N3tw0rkRO RO
            interface Vlan10
             ip address 10.10.0.1 255.255.255.0
             mac-address 00:1A:2B:3C:4D:5E
            wpa_passphrase="Corp-WiFi-2024!"
            """);

        yield return ("web/Web.config", """
            <?xml version="1.0" encoding="utf-8"?>
            <configuration>
              <appSettings>
                <add key="SmtpPassword" value="Winter2025#" />
                <add key="ReportServer" value="http://ssrs01/ReportServer" />
              </appSettings>
              <system.web>
                <machineKey validationKey="{{HEXU:64}}" decryptionKey="{{HEXU:48}}" validation="SHA1" />
              </system.web>
            </configuration>
            """);

        yield return ("db/seed.sql", """
            INSERT INTO Customers (Name, Email, SSN, Card) VALUES
            ('Jane Smith', 'jane.smith@contoso.com', '524-31-9876', '4539 1488 0343 6467');
            -- test card, ignored: 4111 1111 1111 1111
            """);

        yield return ("tests/http/orders.http", """
            GET https://orders-api.contoso.com/v2/orders?api_key={{HEX:32}}
            Authorization: Bearer eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiIxMjM0NTY3ODkwIn0.{{URLSAFE:43}}
            """);
    }
}
