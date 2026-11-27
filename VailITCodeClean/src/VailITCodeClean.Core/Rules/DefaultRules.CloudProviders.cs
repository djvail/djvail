using VailIT.CodeClean.Configuration;

namespace VailIT.CodeClean.Rules;

public static partial class DefaultRules
{
    /// <summary>AWS, Azure, Google Cloud and other cloud platform keys, account ids and resource names.</summary>
    public static IEnumerable<FilterRule> CloudProviders()
    {
        const string c = CatCloud;

        // ---- Amazon Web Services ---------------------------------------------------------------
        yield return Rule("aws-access-key-id", "AWS access key id", c, Severity.Critical, "AWS_ACCESS_KEY",
                """\b(?:A3T[A-Z0-9]|AKIA|ASIA|ABIA|ACCA|AGPA|AIDA|AIPA|ANPA|ANVA|AROA|APKA)[A-Z0-9]{16}\b""",
                "AWS access key ids (AKIA... long-term, ASIA... temporary) and other IAM unique ids.",
                "aws_access_key_id = AKIA{{UPNUM:16}}")
            .Cs();

        yield return Rule("aws-secret-key", "AWS secret access key", c, Severity.Critical, "AWS_SECRET_KEY",
                """(?i)(?:aws_?secret_?(?:access_?)?key|secret_?access_?key|aws_?secret|SecretAccessKey)["']?\s*(?:=|:|=>|,)\s*["']?(?<secret>[A-Za-z0-9/+=]{40})(?![A-Za-z0-9/+=])""",
                "The 40-character secret that goes with an access key id.",
                "aws_secret_access_key = {{B64:40}}");

        yield return Rule("aws-session-token", "AWS session token", c, Severity.Critical, "AWS_SESSION_TOKEN",
                """(?i)(?:aws_?session_?token|aws_?security_?token|SessionToken)["']?\s*(?:=|:|=>|,)\s*["']?(?<secret>[A-Za-z0-9/+=]{100,})""",
                "Temporary STS session tokens.",
                "aws_session_token = IQoJb3JpZ2luX2Vj{{B64:120}}");

        yield return Rule("aws-account-arn", "AWS account id in ARN", c, Severity.Medium, "AWS_ACCOUNT",
                """\barn:aws(?:-cn|-us-gov|-iso[a-z\-]*)?:[a-z0-9\-]+:[a-z0-9\-]*:(?<secret>\d{12}):""",
                "12-digit account ids inside Amazon Resource Names (the rest of the ARN is kept).",
                "arn:aws:iam::{{DIGITS:12}}:role/DeployRole")
            .Cs();

        yield return Rule("aws-account-id", "AWS account id setting", c, Severity.Medium, "AWS_ACCOUNT",
                """(?i)\b(?:aws_?account(?:_?id)?|account_?id|AWS::AccountId|owner_?id|accountNumber)["']?\s*(?:=|:|=>)\s*["']?(?<secret>\d{12})\b""",
                "aws_account_id = 123456789012 style settings.",
                "aws_account_id = \"{{DIGITS:12}}\"");

        yield return Rule("aws-host", "AWS resource endpoint (RDS, ELB, API Gateway, ECR...)", c, Severity.High, "AWS_HOST",
                """\b(?:[a-z0-9][a-z0-9\-]*\.){2,}amazonaws\.com(?:\.cn)?\b""",
                "Hostnames that name YOUR resources, such as mydb.abc123.us-east-1.rds.amazonaws.com or 123456789012.dkr.ecr.eu-west-1.amazonaws.com. Plain service endpoints (s3.amazonaws.com, sts.us-east-1.amazonaws.com) are left alone.",
                "Server=prod-sales.c9akciq32.us-east-1.rds.amazonaws.com;")
            .IgnoreValue("""(?i)^(?:[a-z0-9\-]+\.)?(?:[a-z]{2}(?:-gov)?-[a-z]+-\d\.)?amazonaws\.com(?:\.cn)?$""");

        yield return Rule("aws-s3-bucket", "S3 bucket name", c, Severity.Medium, "S3_BUCKET",
                """(?i)(?:\bs3a?://(?<secret>[a-z0-9][a-z0-9.\-]{1,61}[a-z0-9])|\b(?<secret>[a-z0-9][a-z0-9.\-]{1,61}[a-z0-9])\.s3(?:[.\-](?:website[.\-])?[a-z0-9\-]+)?\.amazonaws\.com|\bs3(?:[.\-][a-z0-9\-]+)?\.amazonaws\.com/(?<secret>[a-z0-9][a-z0-9.\-]{1,61}[a-z0-9]))""",
                "Bucket names in s3:// URIs and S3 URLs.",
                "aws s3 cp build.zip s3://contoso-prod-backups/releases/");

        yield return Rule("aws-cloudfront", "CloudFront distribution host", c, Severity.Medium, "AWS_HOST",
                """\b[a-z0-9]{10,16}\.cloudfront\.net\b""",
                "CloudFront distribution hostnames.",
                "https://d1a2b3c4d5e6f7.cloudfront.net/app.js")
            .Cs();

        yield return Rule("aws-mws-key", "Amazon MWS auth token", c, Severity.Critical, "AWS_MWS",
                """\bamzn\.mws\.[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}\b""",
                "Amazon Marketplace Web Service tokens.",
                "amzn.mws.{{HEX:8}}-{{HEX:4}}-{{HEX:4}}-{{HEX:4}}-{{HEX:12}}")
            .Cs();

        // ---- Microsoft Azure ---------------------------------------------------------------------
        yield return Rule("azure-storage-key", "Azure storage / Cosmos DB account key", c, Severity.Critical, "AZURE_ACCOUNT_KEY",
                """(?i)\b(?:AccountKey|PrimaryKey|SecondaryKey|primary_?access_?key|storage_?(?:account_?)?key|cosmos_?(?:db_?)?key|masterKey)["']?\s*(?:=|:|=>)\s*["']?(?<secret>[A-Za-z0-9+/]{80,}={0,2})""",
                "88-character Base64 keys for storage accounts and Cosmos DB (AccountKey=... in connection strings).",
                "DefaultEndpointsProtocol=https;AccountName=contosoprod;AccountKey={{B64:86}}==;EndpointSuffix=core.windows.net");

        yield return Rule("azure-storage-account-name", "Azure storage account name", c, Severity.Medium, "AZURE_STORAGE_ACCOUNT",
                """(?i)\bAccountName\s*=\s*(?<secret>[a-z0-9]{3,24})\b""",
                "AccountName=... in storage connection strings.",
                "DefaultEndpointsProtocol=https;AccountName=contosoprod;AccountKey=x");

        yield return Rule("azure-shared-access-key", "Azure Service Bus / Event Hubs / IoT shared access key", c, Severity.Critical, "AZURE_SAS_KEY",
                """(?i)\bSharedAccessKey\s*=\s*(?<secret>[A-Za-z0-9+/]{30,}={0,2})""",
                "SharedAccessKey=... in Service Bus, Event Hubs, Relay and IoT Hub connection strings.",
                "Endpoint=sb://contoso-bus.servicebus.windows.net/;SharedAccessKeyName=RootManageSharedAccessKey;SharedAccessKey={{B64:43}}=");

        yield return Rule("azure-sas-signature", "Azure SAS token signature (sig=)", c, Severity.Critical, "AZURE_SAS",
                """(?i)[?&;]sig=(?<secret>[A-Za-z0-9%+/=]{20,})""",
                "The signature part of shared access signature URLs and tokens.",
                "https://contosoprod.blob.core.windows.net/files?sv=2022-11-02&ss=b&srt=co&sp=rl&se=2026-01-01&sig={{B64:40}}%3D");

        yield return Rule("azure-ad-client-secret", "Microsoft Entra ID (Azure AD) client secret", c, Severity.Critical, "AZURE_CLIENT_SECRET",
                """(?<![\w~.\-])[a-zA-Z0-9_~.\-]{3}\dQ~[a-zA-Z0-9_~.\-]{31,34}(?![\w~.\-])""",
                "App registration client secrets (they contain Q~ at the 5th/6th character).",
                "ClientSecret = \"abc8Q~{{URLSAFE:34}}\"")
            .Cs();

        yield return Rule("azure-ids", "Azure tenant / subscription / client id", c, Severity.Medium, "AZURE_ID",
                $$"""(?i)\b(?:tenant_?id|subscription_?id|client_?id|app_?id|application_?id|object_?id|directory_?id|principal_?id|ARM_(?:TENANT|CLIENT|SUBSCRIPTION)_ID|AZURE_(?:TENANT|CLIENT|SUBSCRIPTION)_ID|ida:(?:TenantId|ClientId))["']?\s*(?:=|:|=>|\s+value\s*=)\s*["']?(?<secret>{{Guid}})""",
                "GUIDs assigned to TenantId, SubscriptionId, ClientId/AppId and similar names.",
                "\"TenantId\": \"{{HEX:8}}-{{HEX:4}}-{{HEX:4}}-{{HEX:4}}-{{HEX:12}}\"");

        yield return Rule("azure-resource-id", "Azure subscription in resource id / URL", c, Severity.Medium, "AZURE_SUBSCRIPTION",
                $$"""(?i)/subscriptions/(?<secret>{{Guid}})(?:/resourceGroups/(?<secret_RESOURCE_GROUP>[\w.\-()]{1,90}))?""",
                "Subscription ids and resource group names in /subscriptions/.../resourceGroups/... paths.",
                "/subscriptions/{{HEX:8}}-{{HEX:4}}-{{HEX:4}}-{{HEX:4}}-{{HEX:12}}/resourceGroups/rg-sales-prod/providers/Microsoft.Web/sites/app");

        yield return Rule("azure-tenant-login-url", "Tenant id in login URL", c, Severity.Medium, "AZURE_TENANT",
                $$"""(?i)\blogin\.(?:microsoftonline\.com|windows\.net|microsoftonline\.us|chinacloudapi\.cn)/(?<secret>{{Guid}}|[\w\-]+\.onmicrosoft\.com)""",
                "Tenant ids or onmicrosoft.com names in authority URLs.",
                "https://login.microsoftonline.com/contoso.onmicrosoft.com/oauth2/v2.0/token");

        yield return Rule("azure-onmicrosoft", "Tenant name (*.onmicrosoft.com)", c, Severity.Medium, "AZURE_TENANT",
                """(?i)\b[\w\-]+\.(?:onmicrosoft\.com|sharepoint\.com|b2clogin\.com)\b""",
                "Tenant domains, SharePoint sites and B2C login hosts.",
                "https://contoso.sharepoint.com/sites/Finance");

        yield return Rule("azure-instrumentation-key", "Application Insights instrumentation key", c, Severity.Medium, "APPINSIGHTS_KEY",
                $$"""(?i)\b(?:InstrumentationKey|APPINSIGHTS_INSTRUMENTATIONKEY|instrumentation_?key)["']?\s*(?:=|:|=>)\s*["']?(?<secret>{{Guid}})""",
                "Application Insights keys (also in APPLICATIONINSIGHTS_CONNECTION_STRING).",
                "InstrumentationKey={{HEX:8}}-{{HEX:4}}-{{HEX:4}}-{{HEX:4}}-{{HEX:12}};IngestionEndpoint=https://x");

        yield return Rule("azure-function-key", "Azure Functions key (code=)", c, Severity.High, "AZURE_FUNCTION_KEY",
                """(?i)(?:[?&]code=|x-functions-key["']?\s*[:=,]\s*["']?)(?<secret>[A-Za-z0-9_\-]{30,}={0,2})""",
                "Function keys passed in ?code= query strings or the x-functions-key header.",
                "https://contoso-func.azurewebsites.net/api/Run?code={{URLSAFE:54}}==");

        yield return Rule("azure-devops-pat", "Azure DevOps personal access token", c, Severity.Critical, "AZURE_DEVOPS_PAT",
                """(?i)(?:\b(?:ado|azure_?devops|azdo|vsts|devops|system_?accesstoken)_?(?:pat|token|access_?token)?["']?\s*(?:=|:|=>)\s*["']?|https://[^:/\s"']*:)(?<secret>[a-z0-9]{52}|[A-Za-z0-9]{84})\b""",
                "Azure DevOps PATs in settings or inside git remote URLs.",
                "AZURE_DEVOPS_PAT={{LOWNUM:52}}");

        yield return Rule("azure-host", "Azure resource host (storage, SQL, Key Vault, App Service...)", c, Severity.High, "AZURE_HOST",
                """(?i)\b[a-z0-9](?:[a-z0-9\-]{0,61}[a-z0-9])?(?:\.[a-z0-9\-]+)*\.(?:core\.windows\.net|database\.windows\.net|vault\.azure\.net|vaultcore\.azure\.net|azurewebsites\.net|servicebus\.windows\.net|azurecr\.io|documents\.azure\.com|redis\.cache\.windows\.net|azure-devices\.net|cloudapp\.net|cloudapp\.azure\.com|azureedge\.net|trafficmanager\.net|azure-api\.net|search\.windows\.net|cognitiveservices\.azure\.com|openai\.azure\.com|azurefd\.net|azurestaticapps\.net|postgres\.database\.azure\.com|mysql\.database\.azure\.com|mariadb\.database\.azure\.com|azurehdinsight\.net|azuresynapse\.net|azuredatabricks\.net|azurecontainerapps\.io|azmk8s\.io|servicebus\.usgovcloudapi\.net|signalr\.net|webpubsub\.azure\.com|applicationinsights\.azure\.com|in\.applicationinsights\.azure\.com|azure-automation\.net|batch\.azure\.com|kusto\.windows\.net|purview\.azure\.com|dev\.azuresynapse\.net)\b""",
                "Hostnames that name your own Azure resources, e.g. contosoprod.blob.core.windows.net, sales-sql.database.windows.net, kv-prod.vault.azure.net.",
                "Server=tcp:sales-sql-prod.database.windows.net,1433;")
            .IgnoreValue("""(?i)^(?:scm|privatelink|management|login|portal)\.""")
            .IgnoreValue("""(?i)^(?:management\.core\.windows\.net|(?:blob|queue|table|file|dfs)\.core\.windows\.net|core\.windows\.net|database\.windows\.net|vault\.azure\.net|azurewebsites\.net)$""");

        yield return Rule("azure-devops-org", "Azure DevOps organisation URL", c, Severity.Medium, "AZURE_DEVOPS_ORG",
                """(?i)\b(?:dev\.azure\.com/(?<secret>[\w\-.]+)|(?<secret>[\w\-]+)\.visualstudio\.com)""",
                "Organisation names in dev.azure.com/... and *.visualstudio.com URLs.",
                "https://dev.azure.com/contoso-it/Sales/_git/Portal");

        // ---- Google Cloud / Firebase ------------------------------------------------------------
        yield return Rule("gcp-api-key", "Google API key", c, Severity.Critical, "GOOGLE_API_KEY",
                """\bAIza[0-9A-Za-z\-_]{35}\b""",
                "Google Cloud / Maps / Firebase / Gemini API keys.",
                "apiKey: \"AIza{{URLSAFE:35}}\"")
            .Cs();

        yield return Rule("gcp-oauth-secret", "Google OAuth client secret", c, Severity.Critical, "GOOGLE_CLIENT_SECRET",
                """\bGOCSPX-[A-Za-z0-9_\-]{28}\b""",
                "OAuth 2.0 client secrets for Google sign-in.",
                "client_secret=GOCSPX-{{URLSAFE:28}}")
            .Cs();

        yield return Rule("gcp-oauth-client-id", "Google OAuth client id", c, Severity.Medium, "GOOGLE_CLIENT_ID",
                """\b\d{8,14}-[a-z0-9]{32}\.apps\.googleusercontent\.com\b""",
                "OAuth client ids (identify your Google Cloud project).",
                "{{DIGITS:12}}-{{LOWNUM:32}}.apps.googleusercontent.com")
            .Cs();

        yield return Rule("gcp-service-account-key-id", "Google service account private_key_id", c, Severity.High, "GOOGLE_KEY_ID",
                """["]private_key_id["]\s*:\s*["](?<secret>[a-f0-9]{40})""",
                "The key id inside service-account JSON files (the private key itself is caught by the PEM rule).",
                "\"private_key_id\": \"{{HEX:40}}\"")
            .Cs();

        yield return Rule("gcp-service-account-email", "Google service account email", c, Severity.Medium, "GOOGLE_SERVICE_ACCOUNT",
                """\b[a-z0-9\-]{4,40}@[a-z0-9\-]{4,40}\.iam\.gserviceaccount\.com\b""",
                "Service accounts reveal the project id and purpose.",
                "deployer@contoso-prod-123456.iam.gserviceaccount.com")
            .Cs();

        yield return Rule("gcp-host", "Google Cloud / Firebase app host", c, Severity.Medium, "GCP_HOST",
                """(?i)\b[a-z0-9][a-z0-9\-]{2,62}(?:\.[a-z0-9\-]+)*\.(?:firebaseio\.com|firebaseapp\.com|web\.app|appspot\.com|run\.app|cloudfunctions\.net|a\.run\.app|firebasestorage\.app)\b""",
                "Firebase databases, App Engine, Cloud Run and Cloud Functions hostnames.",
                "databaseURL: \"https://contoso-prod-default-rtdb.firebaseio.com\"");

        yield return Rule("gcp-project-id", "Google Cloud project id setting", c, Severity.Medium, "GCP_PROJECT",
                """(?i)\b(?:project_?id|gcp_?project|google_?cloud_?project|CLOUDSDK_CORE_PROJECT|projectId)["']?\s*(?:=|:|=>)\s*["']?(?<secret>[a-z][a-z0-9\-]{4,28}[a-z0-9])\b""",
                "GCP project ids in settings and Terraform.",
                "\"project_id\": \"contoso-prod-123456\"");

        yield return Rule("gcp-fcm-server-key", "Firebase Cloud Messaging server key", c, Severity.Critical, "FCM_KEY",
                """\bAAAA[A-Za-z0-9_\-]{7}:[A-Za-z0-9_\-]{140}\b""",
                "Legacy FCM server keys.",
                "AAAA{{URLSAFE:7}}:{{URLSAFE:140}}")
            .Cs();

        // ---- other clouds -----------------------------------------------------------------------
        yield return Rule("digitalocean-token", "DigitalOcean token", c, Severity.Critical, "DIGITALOCEAN_TOKEN",
                """\bdo[opr]_v1_[a-f0-9]{64}\b""",
                "DigitalOcean personal access, OAuth and refresh tokens.",
                "dop_v1_{{HEX:64}}")
            .Cs();

        yield return Rule("alibaba-access-key", "Alibaba Cloud access key id", c, Severity.Critical, "ALIBABA_KEY",
                """\bLTAI[A-Za-z0-9]{12,20}\b""",
                "Alibaba Cloud (Aliyun) AccessKey ids.",
                "LTAI{{ALNUM:20}}")
            .Cs();

        yield return Rule("ibm-cloud-key", "IBM Cloud / Oracle Cloud key setting", c, Severity.High, "CLOUD_KEY",
                """(?i)\b(?:ibm_?cloud_?api_?key|ibmcloud_?api_?key|oci_?(?:api_?)?key|oracle_?cloud_?key|tenancy_?ocid|user_?ocid)["']?\s*(?:=|:|=>)\s*["']?(?<secret>[A-Za-z0-9_\-.]{20,})""",
                "IBM Cloud API keys and Oracle Cloud tenancy/user OCIDs.",
                "IBMCLOUD_API_KEY={{URLSAFE:44}}");

        yield return Rule("oracle-ocid", "Oracle Cloud OCID", c, Severity.Medium, "OCI_OCID",
                """\bocid1\.[a-z0-9]+\.oc\d\.[a-z0-9\-]*\.[a-z0-9]{40,}\b""",
                "Oracle Cloud resource identifiers.",
                "ocid1.tenancy.oc1..{{LOWNUM:60}}")
            .Cs();
    }
}
