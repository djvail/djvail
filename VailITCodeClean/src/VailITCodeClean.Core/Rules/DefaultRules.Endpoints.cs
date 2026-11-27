using VailIT.CodeClean.Configuration;

namespace VailIT.CodeClean.Rules;

public static partial class DefaultRules
{
    /// <summary>Web addresses: credentials inside URLs, internal URLs, API endpoints and webhooks.</summary>
    public static IEnumerable<FilterRule> Endpoints()
    {
        const string c = CatEndpoints;

        // Well-known public sites that appear in almost every code base and say nothing about you.
        const string publicSites = """(?i)^[a-z]+://(?:[\w\-]+\.)*(?:w3\.org|schemas\.microsoft\.com|schemas\.xmlsoap\.org|schemas\.openxmlformats\.org|openxmlformats\.org|json-schema\.org|schema\.org|schemastore\.org|json\.schemastore\.org|login\.microsoftonline\.com|login\.windows\.net|login\.live\.com|graph\.microsoft\.com|management\.azure\.com|management\.core\.windows\.net|accounts\.google\.com|oauth2\.googleapis\.com|www\.googleapis\.com|api\.github\.com|raw\.githubusercontent\.com|example\.(?:com|org|net)|tempuri\.org|purl\.org|xmlsoap\.org|go\.microsoft\.com|aka\.ms|learn\.microsoft\.com|docs\.microsoft\.com|msdn\.microsoft\.com|support\.microsoft\.com|nuget\.org|api\.nuget\.org|npmjs\.(?:com|org)|registry\.npmjs\.org|yarnpkg\.com|pypi\.org|python\.org|apache\.org|opensource\.org|mozilla\.org|developer\.mozilla\.org|gnu\.org|creativecommons\.org|choosealicense\.com|semver\.org|keepachangelog\.com|ietf\.org|tools\.ietf\.org|rfc-editor\.org|ecma-international\.org|unicode\.org|iana\.org|stackoverflow\.com|wikipedia\.org|google\.com|fonts\.googleapis\.com|fonts\.gstatic\.com|ajax\.googleapis\.com|cdnjs\.cloudflare\.com|cdn\.jsdelivr\.net|unpkg\.com|code\.jquery\.com|getbootstrap\.com|maxcdn\.bootstrapcdn\.com|stackpath\.bootstrapcdn\.com|reactjs\.org|react\.dev|angular\.io|vuejs\.org|nodejs\.org|dotnet\.microsoft\.com|visualstudio\.microsoft\.com|marketplace\.visualstudio\.com|swagger\.io|openapis\.org|spdx\.org|maven\.apache\.org|repo\.maven\.apache\.org|plugins\.gradle\.org|gradle\.org|rubygems\.org|crates\.io|golang\.org|go\.dev|pkg\.go\.dev|docker\.com|hub\.docker\.com|kubernetes\.io|helm\.sh|terraform\.io|registry\.terraform\.io|hashicorp\.com|localhost|127\.0\.0\.1|0\.0\.0\.0|\[::1\])(?=[:/?#]|$)""";

        yield return Rule("url-credentials", "Credentials inside a URL (user:password@host)", c, Severity.Critical, "URL_CREDENTIALS",
                """(?i)\b[a-z][a-z0-9+.\-]{1,20}://(?<secret_USERNAME>[^:@/\s"'<>]{1,128}):(?<secret_PASSWORD>[^@/\s"'<>]{1,256})@(?=[\w\[])""",
                "Any URL or URI with an embedded user name and password, e.g. https://admin:pa55@host, postgres://app:secret@db:5432.",
                "amqp://orders:Spring2024!@rabbit01.contoso.local:5672/")
            .IgnoreValue("""(?i)^(?:\$\{[^}]*\}|\$\w+|%\w+%|\{\{.*\}\}|<[^>]*>|\{\w+\}|\*+|user(?:name)?|pass(?:word)?)$""");

        yield return Rule("url-secret-query", "Secret in URL query string", c, Severity.High, "URL_SECRET",
                """(?i)[?&](?:api_?key|apikey|key|token|access_?token|auth(?:_?token)?|secret|client_?secret|password|pwd|pass|sig|signature|code|session(?:_?id)?|sid|jwt|private_?token|subscription-key|x-api-key)=(?<secret>[^&#\s"'<>]{8,})""",
                "?api_key=..., &token=..., &password=... and similar query-string parameters.",
                "https://api.weather.example.net/v1/forecast?city=Denver&api_key={{HEX:32}}")
            .IgnorePlaceholders()
            .IgnoreValue("""(?i)^(?:\$\{.*\}|\{\w+\}|%\w+%|\{\{.*\}\}|<.*>|\[\w+\]|xxx+)$""");

        yield return Rule("url-ip-host", "URL pointing at an IP address", c, Severity.High, "URL_IP",
                """(?i)\b(?:https?|wss?|ftp)://(?:\d{1,3}\.){3}\d{1,3}(?::\d+)?(?:/[^\s"'`<>()\[\]{}]*)?(?<![.,;:])""",
                "http(s)://10.1.2.3:8080/... style URLs (servers reached by address).",
                "var baseUrl = \"http://10.20.30.40:8080/api/orders\";")
            .IgnoreValue("""(?i)^[a-z]+://(?:127\.0\.0\.1|0\.0\.0\.0)(?:[:/]|$)""");

        yield return Rule("url-internal-host", "URL with an internal host name", c, Severity.High, "INTERNAL_URL",
                """(?i)\b(?:https?|wss?|ftp)://(?:[a-z0-9\-]+\.)*(?:[a-z0-9\-]+)\.(?:local|localdomain|internal|intranet|corp|lan|private|home\.arpa|ad|intra|priv)(?::\d+)?(?:[/?#][^\s"'`<>()\[\]{}]*)?(?<![.,;:])""",
                "URLs whose host is in a private DNS zone, e.g. https://jira.corp.contoso.local/browse/ABC-1.",
                "https://jenkins.build.internal:8443/job/deploy/")
            .IgnoreValue("""(?i)^[a-z]+://(?:host\.docker\.internal|kubernetes\.docker\.internal|metadata\.google\.internal)""");

        yield return Rule("url-single-label-host", "URL with a single-word host (http://server/...)", c, Severity.Medium, "INTERNAL_URL",
                """(?i)\b(?:https?|wss?|ftp)://(?<secret>[a-z][a-z0-9\-]{1,62})(?::\d+)?(?=[/?#"'\s]|$)""",
                "Intranet-style URLs such as http://reportserver/Reports or http://build01:8080.",
                "<add key=\"ReportServer\" value=\"http://ssrs01/ReportServer\" />")
            .Ignore("localhost", "host", "server", "hostname", "example", "your-server", "yourserver", "myserver", "servername", "tempuri", "schemas", "proxy", "api", "app", "web", "db");

        yield return Rule("url-api-endpoint", "Web address / API endpoint", c, Severity.Low, "URL",
                """(?i)\b(?:https?|wss?|ftps?)://[^\s"'`<>()\[\]{}|\\^]+(?<![.,;:!?])""",
                "Every other http(s), ws(s) and ftp URL. Well-known public sites (w3.org, microsoft docs, nuget, npm, CDNs, licences...) are skipped. Turn this off if you only care about secrets.",
                "const string OrdersApi = \"https://orders-api.contoso.com/v2/orders\";")
            .IgnoreValue(publicSites);

        yield return Rule("graphql-grpc-endpoint", "gRPC / GraphQL endpoint setting", c, Severity.Medium, "ENDPOINT",
                QuotedAssignment("""endpoint|base_?url|base_?uri|api_?url|api_?uri|api_?base|service_?url|grpc_?(?:url|host|endpoint|address)|graphql_?(?:url|endpoint)|webhook_?url|callback_?url|redirect_?uri|authority|issuer|audience|token_?(?:url|endpoint)|metadata_?(?:url|address)|wsdl|soap_?url""", noSpaces: true),
                "Quoted values assigned to endpoint, baseUrl, apiUrl, authority, issuer, audience, redirectUri, webhookUrl...",
                "\"Authority\": \"sts.contoso.com\",")
            .IgnorePlaceholders()
            .IgnoreValue(publicSites)
            .Ignore("/", "localhost", "api://default");
    }
}
