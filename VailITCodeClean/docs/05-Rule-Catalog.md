# Rule catalog

Every rule that ships with VailIT Code Clean (the `Default` profile). **On** = enabled by default. Rules marked *off* are noisy in most code bases or are templates you must edit first.

The source of these rules is `src/VailITCodeClean.Core/Rules/DefaultRules.*.cs`; to regenerate this page run `vailit-codeclean export-profile samples/Default.profile.json` and the script in `scripts/Update-RuleCatalog.py`.

| Category | Rules | On by default |
|---|---:|---:|
| [Keys & Certificates](#keys--certificates) | 14 | 12 |
| [Cloud Providers](#cloud-providers) | 35 | 35 |
| [Service Tokens](#service-tokens) | 57 | 57 |
| [Credentials](#credentials) | 25 | 25 |
| [Infrastructure](#infrastructure) | 22 | 22 |
| [Endpoints](#endpoints) | 7 | 7 |
| [Personal Data](#personal-data) | 11 | 8 |
| [Custom (examples)](#custom-examples) | 7 | 0 |
| **Total** | **178** | **166** |

## Keys & Certificates

| On | Severity | Id | What it finds | Placeholder |
|---|---|---|---|---|
| on | Critical | `pem-private-key` | Private key block (PEM / OpenSSH / PGP) - RSA, EC, DSA, OpenSSH, PKCS#8 (encrypted or not) and PGP private keys. The BEGIN/END lines are kept so readers know what was there; the key body is replaced line by line. | `VAILIT_PRIVATE_KEY_0001` |
| on | Critical | `putty-private-key` | PuTTY private key (.ppk) - The private part of a PuTTY key file. | `VAILIT_PRIVATE_KEY_0001` |
| on | Low | `pem-certificate` | Certificate block (PEM) - Certificates are public, but they name your servers, organisation and internal CA. Body replaced, BEGIN/END kept. | `VAILIT_CERTIFICATE_0001` |
| on | Medium | `ssh-public-key` | SSH public key - authorized_keys / known_hosts entries. Public, but they identify people and machines (the user@host comment is replaced too). | `VAILIT_SSH_PUBLIC_KEY_0001` |
| on | High | `jwt` | JSON Web Token (JWT) - Signed tokens (often access or ID tokens) copied into code, tests or HTTP files. | `VAILIT_JWT_0001` |
| on | Critical | `machine-key` | ASP.NET <machineKey> validation / decryption key - web.config machineKey values (used to sign/encrypt view state, forms auth cookies). | `VAILIT_MACHINE_KEY_0001` |
| on | Critical | `kubeconfig-data` | Kubernetes kubeconfig key / certificate data - Base64 client keys, certificates and tokens inside kubeconfig files. | `VAILIT_KUBE_SECRET_0001` |
| on | High | `k8s-secret-data` | Kubernetes Secret data values - Base64 values in YAML (as used in kind: Secret manifests and Helm values). Only applied to YAML files and only to values that look random. | `VAILIT_KUBE_SECRET_0001` |
| on | Medium | `cert-thumbprint` | Certificate thumbprint - SHA-1/SHA-256 certificate thumbprints (they identify certificates in your stores). | `VAILIT_THUMBPRINT_0001` |
| on | Critical | `pfx-password` | Certificate / keystore password - Passwords for .pfx/.p12 files, Java keystores and code-signing certificates. | `VAILIT_PASSWORD_0001` |
| on | Medium | `byte-array-key` | Hard-coded key as a byte array - 16+ hex bytes in a row, the usual way AES keys, IVs and salts are pasted into code. | `VAILIT_BYTE_KEY_0001` |
| on | Critical | `age-secret-key` | age encryption secret key - Secret keys for the age file encryption tool. | `VAILIT_PRIVATE_KEY_0001` |
| *off* | Low | `long-base64` | Long Base64 blob - Very long Base64 strings (embedded keys, certificates, packed config). Off by default: it also hits images and fonts embedded in CSS/HTML. | `VAILIT_BASE64_BLOB_0001` |
| *off* | Low | `generic-guid` | Any GUID - Every GUID. Off by default (project and COM GUIDs are everywhere); the Azure rules already catch tenant, subscription and client ids by context. | `VAILIT_GUID_0001` |

## Cloud Providers

| On | Severity | Id | What it finds | Placeholder |
|---|---|---|---|---|
| on | Critical | `aws-access-key-id` | AWS access key id - AWS access key ids (AKIA... long-term, ASIA... temporary) and other IAM unique ids. | `VAILIT_AWS_ACCESS_KEY_0001` |
| on | Critical | `aws-secret-key` | AWS secret access key - The 40-character secret that goes with an access key id. | `VAILIT_AWS_SECRET_KEY_0001` |
| on | Critical | `aws-session-token` | AWS session token - Temporary STS session tokens. | `VAILIT_AWS_SESSION_TOKEN_0001` |
| on | Medium | `aws-account-arn` | AWS account id in ARN - 12-digit account ids inside Amazon Resource Names (the rest of the ARN is kept). | `VAILIT_AWS_ACCOUNT_0001` |
| on | Medium | `aws-account-id` | AWS account id setting - aws_account_id = 123456789012 style settings. | `VAILIT_AWS_ACCOUNT_0001` |
| on | High | `aws-host` | AWS resource endpoint (RDS, ELB, API Gateway, ECR...) - Hostnames that name YOUR resources, such as mydb.abc123.us-east-1.rds.amazonaws.com or 123456789012.dkr.ecr.eu-west-1.amazonaws.com. Plain service endpoints (s3.amazonaws.com, sts.us-east-1.amazonaws.com) are left alone. | `VAILIT_AWS_HOST_0001` |
| on | Medium | `aws-s3-bucket` | S3 bucket name - Bucket names in s3:// URIs and S3 URLs. | `VAILIT_S3_BUCKET_0001` |
| on | Medium | `aws-cloudfront` | CloudFront distribution host - CloudFront distribution hostnames. | `VAILIT_AWS_HOST_0001` |
| on | Critical | `aws-mws-key` | Amazon MWS auth token - Amazon Marketplace Web Service tokens. | `VAILIT_AWS_MWS_0001` |
| on | Critical | `azure-storage-key` | Azure storage / Cosmos DB account key - 88-character Base64 keys for storage accounts and Cosmos DB (AccountKey=... in connection strings). | `VAILIT_AZURE_ACCOUNT_KEY_0001` |
| on | Medium | `azure-storage-account-name` | Azure storage account name - AccountName=... in storage connection strings. | `VAILIT_AZURE_STORAGE_ACCOUNT_0001` |
| on | Critical | `azure-shared-access-key` | Azure Service Bus / Event Hubs / IoT shared access key - SharedAccessKey=... in Service Bus, Event Hubs, Relay and IoT Hub connection strings. | `VAILIT_AZURE_SAS_KEY_0001` |
| on | Critical | `azure-sas-signature` | Azure SAS token signature (sig=) - The signature part of shared access signature URLs and tokens. | `VAILIT_AZURE_SAS_0001` |
| on | Critical | `azure-ad-client-secret` | Microsoft Entra ID (Azure AD) client secret - App registration client secrets (they contain Q~ at the 5th/6th character). | `VAILIT_AZURE_CLIENT_SECRET_0001` |
| on | Medium | `azure-ids` | Azure tenant / subscription / client id - GUIDs assigned to TenantId, SubscriptionId, ClientId/AppId and similar names. | `VAILIT_AZURE_ID_0001` |
| on | Medium | `azure-resource-id` | Azure subscription in resource id / URL - Subscription ids and resource group names in /subscriptions/.../resourceGroups/... paths. | `VAILIT_AZURE_SUBSCRIPTION_0001` |
| on | Medium | `azure-tenant-login-url` | Tenant id in login URL - Tenant ids or onmicrosoft.com names in authority URLs. | `VAILIT_AZURE_TENANT_0001` |
| on | Medium | `azure-onmicrosoft` | Tenant name (*.onmicrosoft.com) - Tenant domains, SharePoint sites and B2C login hosts. | `VAILIT_AZURE_TENANT_0001` |
| on | Medium | `azure-instrumentation-key` | Application Insights instrumentation key - Application Insights keys (also in APPLICATIONINSIGHTS_CONNECTION_STRING). | `VAILIT_APPINSIGHTS_KEY_0001` |
| on | High | `azure-function-key` | Azure Functions key (code=) - Function keys passed in ?code= query strings or the x-functions-key header. | `VAILIT_AZURE_FUNCTION_KEY_0001` |
| on | Critical | `azure-devops-pat` | Azure DevOps personal access token - Azure DevOps PATs in settings or inside git remote URLs. | `VAILIT_AZURE_DEVOPS_PAT_0001` |
| on | High | `azure-host` | Azure resource host (storage, SQL, Key Vault, App Service...) - Hostnames that name your own Azure resources, e.g. contosoprod.blob.core.windows.net, sales-sql.database.windows.net, kv-prod.vault.azure.net. | `VAILIT_AZURE_HOST_0001` |
| on | Medium | `azure-devops-org` | Azure DevOps organisation URL - Organisation names in dev.azure.com/... and *.visualstudio.com URLs. | `VAILIT_AZURE_DEVOPS_ORG_0001` |
| on | Critical | `gcp-api-key` | Google API key - Google Cloud / Maps / Firebase / Gemini API keys. | `VAILIT_GOOGLE_API_KEY_0001` |
| on | Critical | `gcp-oauth-secret` | Google OAuth client secret - OAuth 2.0 client secrets for Google sign-in. | `VAILIT_GOOGLE_CLIENT_SECRET_0001` |
| on | Medium | `gcp-oauth-client-id` | Google OAuth client id - OAuth client ids (identify your Google Cloud project). | `VAILIT_GOOGLE_CLIENT_ID_0001` |
| on | High | `gcp-service-account-key-id` | Google service account private_key_id - The key id inside service-account JSON files (the private key itself is caught by the PEM rule). | `VAILIT_GOOGLE_KEY_ID_0001` |
| on | Medium | `gcp-service-account-email` | Google service account email - Service accounts reveal the project id and purpose. | `VAILIT_GOOGLE_SERVICE_ACCOUNT_0001` |
| on | Medium | `gcp-host` | Google Cloud / Firebase app host - Firebase databases, App Engine, Cloud Run and Cloud Functions hostnames. | `VAILIT_GCP_HOST_0001` |
| on | Medium | `gcp-project-id` | Google Cloud project id setting - GCP project ids in settings and Terraform. | `VAILIT_GCP_PROJECT_0001` |
| on | Critical | `gcp-fcm-server-key` | Firebase Cloud Messaging server key - Legacy FCM server keys. | `VAILIT_FCM_KEY_0001` |
| on | Critical | `digitalocean-token` | DigitalOcean token - DigitalOcean personal access, OAuth and refresh tokens. | `VAILIT_DIGITALOCEAN_TOKEN_0001` |
| on | Critical | `alibaba-access-key` | Alibaba Cloud access key id - Alibaba Cloud (Aliyun) AccessKey ids. | `VAILIT_ALIBABA_KEY_0001` |
| on | High | `ibm-cloud-key` | IBM Cloud / Oracle Cloud key setting - IBM Cloud API keys and Oracle Cloud tenancy/user OCIDs. | `VAILIT_CLOUD_KEY_0001` |
| on | Medium | `oracle-ocid` | Oracle Cloud OCID - Oracle Cloud resource identifiers. | `VAILIT_OCI_OCID_0001` |

## Service Tokens

| On | Severity | Id | What it finds | Placeholder |
|---|---|---|---|---|
| on | High | `auth-header` | Authorization header value - The credential part of Authorization: Bearer/Basic/Token ... headers (code, .http files, Postman, curl). | `VAILIT_AUTH_HEADER_0001` |
| on | High | `bearer-token` | Bearer token - Any 'Bearer xxxx' string with a long token after it. | `VAILIT_BEARER_TOKEN_0001` |
| on | High | `auth-header-value-object` | AuthenticationHeaderValue("Bearer", "...") - .NET HttpClient headers with a hard-coded token. | `VAILIT_AUTH_HEADER_0001` |
| on | High | `api-key-header` | API key header (x-api-key, Ocp-Apim-Subscription-Key...) - Common API-key headers used by API gateways and SaaS services. | `VAILIT_API_KEY_0001` |
| on | Critical | `github-token` | GitHub token - GitHub personal access (classic and fine-grained), OAuth, app and refresh tokens. | `VAILIT_GITHUB_TOKEN_0001` |
| on | Critical | `gitlab-token` | GitLab token - GitLab personal, deploy, runner, pipeline trigger and other tokens. | `VAILIT_GITLAB_TOKEN_0001` |
| on | Critical | `bitbucket-token` | Bitbucket / Atlassian API token - Atlassian (Jira, Confluence, Bitbucket) API tokens and app passwords. | `VAILIT_ATLASSIAN_TOKEN_0001` |
| on | Critical | `npm-token` | npm access token - npm automation / publish tokens. | `VAILIT_NPM_TOKEN_0001` |
| on | Critical | `pypi-token` | PyPI upload token - PyPI API tokens. | `VAILIT_PYPI_TOKEN_0001` |
| on | Critical | `nuget-key` | NuGet API key - nuget.org API keys. | `VAILIT_NUGET_KEY_0001` |
| on | Critical | `docker-hub-token` | Docker Hub token - Docker Hub personal / organisation access tokens. | `VAILIT_DOCKER_TOKEN_0001` |
| on | Critical | `docker-config-auth` | Docker config.json auth - Base64 user:password pairs and identity tokens in ~/.docker/config.json. | `VAILIT_DOCKER_AUTH_0001` |
| on | Critical | `jfrog-token` | JFrog Artifactory token - Artifactory API keys and reference tokens. | `VAILIT_JFROG_TOKEN_0001` |
| on | Critical | `terraform-cloud-token` | Terraform Cloud / Enterprise token - HCP Terraform API tokens. | `VAILIT_TERRAFORM_TOKEN_0001` |
| on | Critical | `vault-token` | HashiCorp Vault token - Vault service, batch and recovery tokens. | `VAILIT_VAULT_TOKEN_0001` |
| on | Critical | `pulumi-token` | Pulumi access token - Pulumi Cloud access tokens. | `VAILIT_PULUMI_TOKEN_0001` |
| on | Critical | `doppler-token` | Doppler token - Doppler secrets-manager tokens. | `VAILIT_DOPPLER_TOKEN_0001` |
| on | Critical | `postman-key` | Postman API key - Postman API keys. | `VAILIT_POSTMAN_KEY_0001` |
| on | Critical | `snyk-sonar-token` | Snyk / SonarQube token - SonarQube user/project/analysis tokens and Snyk tokens. | `VAILIT_SCANNER_TOKEN_0001` |
| on | Critical | `circleci-buildkite` | CircleCI / Buildkite token - CircleCI personal tokens and Buildkite agent tokens. | `VAILIT_CI_TOKEN_0001` |
| on | Critical | `stripe-secret-key` | Stripe secret / restricted key - Stripe secret and restricted API keys. | `VAILIT_STRIPE_KEY_0001` |
| on | Low | `stripe-publishable-key` | Stripe publishable key - Publishable keys are meant for browsers but still identify your account. | `VAILIT_STRIPE_PUBLISHABLE_KEY_0001` |
| on | Critical | `stripe-webhook-secret` | Stripe webhook signing secret - Webhook endpoint signing secrets. | `VAILIT_STRIPE_WEBHOOK_SECRET_0001` |
| on | Critical | `square-token` | Square access token / secret - Square access tokens and OAuth secrets. | `VAILIT_SQUARE_TOKEN_0001` |
| on | Critical | `braintree-token` | Braintree / PayPal access token - Braintree gateway access tokens. | `VAILIT_PAYPAL_TOKEN_0001` |
| on | Critical | `shopify-token` | Shopify token - Shopify admin, custom app, private app tokens and shared secrets. | `VAILIT_SHOPIFY_TOKEN_0001` |
| on | Critical | `slack-token` | Slack token - Slack bot, user, app and configuration tokens. | `VAILIT_SLACK_TOKEN_0001` |
| on | Critical | `slack-webhook` | Slack incoming webhook URL - Anyone with this URL can post into your Slack channel. | `VAILIT_SLACK_WEBHOOK_0001` |
| on | Critical | `teams-webhook` | Microsoft Teams / Office 365 webhook URL - Incoming webhooks and Power Automate trigger URLs for Teams. | `VAILIT_TEAMS_WEBHOOK_0001` |
| on | Critical | `discord-webhook` | Discord webhook URL - Discord channel webhooks. | `VAILIT_DISCORD_WEBHOOK_0001` |
| on | Critical | `discord-bot-token` | Discord bot token - Discord bot tokens. | `VAILIT_DISCORD_TOKEN_0001` |
| on | Critical | `telegram-bot-token` | Telegram bot token - Telegram Bot API tokens. | `VAILIT_TELEGRAM_TOKEN_0001` |
| on | High | `twilio-key` | Twilio API key / account SID - Twilio API key SIDs (SK...) and account SIDs (AC...). | `VAILIT_TWILIO_0001` |
| on | Critical | `sendgrid-key` | SendGrid API key - SendGrid email API keys. | `VAILIT_SENDGRID_KEY_0001` |
| on | Critical | `mailgun-key` | Mailgun API key - Mailgun private API keys. | `VAILIT_MAILGUN_KEY_0001` |
| on | Critical | `mailchimp-key` | Mailchimp API key - Mailchimp marketing API keys. | `VAILIT_MAILCHIMP_KEY_0001` |
| on | Critical | `postmark-mandrill` | Postmark / Mandrill / Brevo key - Brevo (Sendinblue) and Mandrill API keys. | `VAILIT_EMAIL_API_KEY_0001` |
| on | Critical | `notion-token` | Notion integration token - Notion internal integration secrets. | `VAILIT_NOTION_TOKEN_0001` |
| on | Critical | `linear-key` | Linear API key - Linear API keys. | `VAILIT_LINEAR_KEY_0001` |
| on | Critical | `asana-figma-airtable` | Airtable / Figma / Asana token - Airtable personal access tokens, Figma tokens and Asana PATs. | `VAILIT_SAAS_TOKEN_0001` |
| on | High | `sentry-dsn` | Sentry DSN / token - Sentry DSNs (contain the project key) and auth tokens. | `VAILIT_SENTRY_0001` |
| on | Critical | `newrelic-key` | New Relic key - New Relic user, ingest, browser and license keys. | `VAILIT_NEWRELIC_KEY_0001` |
| on | Critical | `datadog-key` | Datadog API / application key - Datadog keys (recognised by their setting name). | `VAILIT_DATADOG_KEY_0001` |
| on | Critical | `grafana-token` | Grafana token - Grafana service account and Grafana Cloud tokens. | `VAILIT_GRAFANA_TOKEN_0001` |
| on | High | `splunk-hec` | Splunk HEC token - Splunk HTTP Event Collector tokens. | `VAILIT_SPLUNK_TOKEN_0001` |
| on | Critical | `dynatrace-token` | Dynatrace token - Dynatrace API tokens. | `VAILIT_DYNATRACE_TOKEN_0001` |
| on | Critical | `openai-key` | OpenAI API key - OpenAI project, service-account, admin and legacy keys. | `VAILIT_OPENAI_KEY_0001` |
| on | Critical | `anthropic-key` | Anthropic API key - Anthropic (Claude) API and admin keys. | `VAILIT_ANTHROPIC_KEY_0001` |
| on | Critical | `huggingface-token` | Hugging Face token - Hugging Face user access tokens. | `VAILIT_HUGGINGFACE_TOKEN_0001` |
| on | Critical | `other-ai-keys` | Groq / xAI / Perplexity / Replicate key - Keys for other AI APIs. | `VAILIT_AI_KEY_0001` |
| on | Critical | `databricks-token` | Databricks token - Databricks personal access tokens. | `VAILIT_DATABRICKS_TOKEN_0001` |
| on | Critical | `supabase-planetscale` | Supabase / PlanetScale token - Supabase management/secret keys and PlanetScale passwords/tokens. | `VAILIT_DB_PLATFORM_TOKEN_0001` |
| on | Critical | `mapbox-token` | Mapbox secret token - Mapbox secret access tokens. | `VAILIT_MAPBOX_TOKEN_0001` |
| on | Critical | `dropbox-token` | Dropbox token - Dropbox short-lived access tokens. | `VAILIT_DROPBOX_TOKEN_0001` |
| on | Critical | `facebook-twitter-token` | Facebook / Twitter (X) token - Facebook Graph access tokens and Twitter/X app bearer tokens. | `VAILIT_SOCIAL_TOKEN_0001` |
| on | Critical | `okta-token` | Okta API token - Okta API tokens in SSWS authorisation headers. | `VAILIT_OKTA_TOKEN_0001` |
| on | Critical | `cloudflare-heroku-vercel` | Cloudflare / Heroku / Vercel / Netlify token setting - Tokens for many platforms that have no fixed prefix, recognised by their setting name (CLOUDFLARE_API_TOKEN, HEROKU_API_KEY, VERCEL_TOKEN...). | `VAILIT_PLATFORM_TOKEN_0001` |

## Credentials

| On | Severity | Id | What it finds | Placeholder |
|---|---|---|---|---|
| on | High | `password-assignment` | Password in code / quoted setting - A quoted value assigned to a name ending in password, passwd, pwd, passphrase... in any language or JSON/YAML/XML. | `VAILIT_PASSWORD_0001` |
| on | High | `password-config-line` | Password in config file (unquoted) - NAME=value / name: value lines in .env, .ini, .properties, YAML, shell scripts and similar files. | `VAILIT_PASSWORD_0001` |
| on | Critical | `connection-string-password` | Password in connection string - Password=... or Pwd=... inside SQL Server, MySQL, PostgreSQL, Oracle and ODBC connection strings. | `VAILIT_PASSWORD_0001` |
| on | High | `xml-key-value-secret` | Secret in <add key=... value=...> - app.config / web.config / NuGet.config entries like <add key="SmtpPassword" value="..."/>. | `VAILIT_SECRET_0001` |
| on | High | `xml-element-secret` | Secret in XML element - Values inside elements such as <Password>...</Password>, <ApiKey>...</ApiKey> (Maven settings.xml, .pubxml, FileZilla, etc.). | `VAILIT_SECRET_0001` |
| on | Medium | `username-assignment` | User name in code / quoted setting - A quoted value assigned to user, username, login, uid, serviceAccount... Common built-in names (admin, root, sa...) are ignored. | `VAILIT_USERNAME_0001` |
| on | Medium | `username-config-line` | User name in config file (unquoted) - NAME=value / name: value user name lines in .env, .ini, .properties, YAML, shell scripts and similar files. | `VAILIT_USERNAME_0001` |
| on | Medium | `connection-string-user` | User name in connection string - User Id=..., UID=..., User=... inside connection strings. | `VAILIT_USERNAME_0001` |
| on | Medium | `windows-domain-user` | Windows DOMAIN\user account - Down-level logon names such as CONTOSO\jsmith or CONTOSO\\svc_app (both the domain and the user are replaced). | `VAILIT_DOMAIN_USER_0001` |
| on | Medium | `upn-service-account` | Service account / UPN - User principal names of service accounts such as svc_backup@contoso.com. | `VAILIT_USERNAME_0001` |
| on | High | `secret-assignment` | Secret / token / API key in code - A quoted value without spaces assigned to a name ending in secret, apiKey, token, accessKey, privateKey, salt... (publicKeyToken, tokenType, keyVaultName and similar names are skipped). | `VAILIT_SECRET_0001` |
| on | High | `secret-config-line` | Secret / token / API key in config file (unquoted) - NAME=value / name: value secret lines in .env, .ini, .properties, YAML, .npmrc and similar files. | `VAILIT_SECRET_0001` |
| on | Medium | `high-entropy-assignment` | High-entropy value assigned to key-like name - Catches random-looking strings (keys, hashes, tokens) assigned to anything with key/secret/token/hash/sig in its name. | `VAILIT_SECRET_0001` |
| on | Critical | `network-credential` | new NetworkCredential(user, password) - .NET NetworkCredential created with hard-coded user name, password and optional domain. | `VAILIT_PASSWORD_0001` |
| on | Critical | `basic-auth-encode` | Basic auth built from user:password - Code that Base64-encodes a hard-coded "user:password" string for an Authorization header. | `VAILIT_BASIC_AUTH_0001` |
| on | Critical | `powershell-securestring` | PowerShell ConvertTo-SecureString -AsPlainText - Plain-text passwords turned into SecureString objects in PowerShell scripts. | `VAILIT_PASSWORD_0001` |
| on | High | `cli-password-argument` | Password / token passed as a command-line argument - Arguments like --password X, -Password 'X', /p:Password=X, -storepass X in scripts, Dockerfiles and CI files. | `VAILIT_PASSWORD_0001` |
| on | High | `sqlcmd-password` | sqlcmd / bcp / mysql password switch - sqlcmd -P password, bcp -P password, mysql -pPassword. | `VAILIT_PASSWORD_0001` |
| on | Critical | `curl-user` | curl -u user:password - curl/wget calls with embedded credentials. | `VAILIT_BASIC_AUTH_0001` |
| on | Medium | `net-use-user` | net use / runas /user: - User names passed to net use, runas, psexec, schtasks and cmdkey. | `VAILIT_USERNAME_0001` |
| on | High | `mount-credentials` | Credentials in mount / fstab options - username=, password= and domain= options of mount -o and /etc/fstab CIFS/SMB entries. | `VAILIT_USERNAME_0001` |
| on | Critical | `sshpass` | sshpass -p password - Passwords handed to ssh through sshpass. | `VAILIT_PASSWORD_0001` |
| on | High | `snmp-community` | SNMP community string - SNMP community strings in device configs, monitoring settings and scripts. | `VAILIT_SNMP_COMMUNITY_0001` |
| on | High | `wifi-psk` | Wi-Fi / VPN pre-shared key - Wireless passphrases and IPsec/IKE pre-shared keys. | `VAILIT_PSK_0001` |
| on | High | `cisco-secret` | Cisco enable secret / password hash - Cisco IOS enable secrets and local user passwords (type 0, 5, 7, 8, 9). | `VAILIT_DEVICE_SECRET_0001` |

## Infrastructure

| On | Severity | Id | What it finds | Placeholder |
|---|---|---|---|---|
| on | High | `ipv4-private` | Private IPv4 address (10.x, 172.16-31.x, 192.168.x) - Internal addresses reveal your network layout (RFC 1918 and carrier-grade NAT ranges). | `VAILIT_PRIVATE_IP_0001` |
| on | Medium | `ipv4` | IPv4 address - Any IPv4 address. 0.0.0.0, 127.0.0.1, 255.255.255.x masks and lines that look like version numbers are skipped. | `VAILIT_IP_0001` |
| on | Medium | `ipv6` | IPv6 address - Full and compressed IPv6 addresses (::1 and fe80:: link-local are skipped). | `VAILIT_IPV6_0001` |
| on | Medium | `mac-address` | MAC address - Network card hardware addresses (identify physical machines). | `VAILIT_MAC_0001` |
| on | High | `internal-hostname` | Internal host name (.local, .corp, .internal, .lan...) - Fully qualified names in private DNS zones, e.g. sql01.corp.contoso.local or build.internal. | `VAILIT_INTERNAL_HOST_0001` |
| on | Medium | `k8s-service-host` | Kubernetes service DNS name - service.namespace.svc.cluster.local names reveal cluster layout. | `VAILIT_K8S_SERVICE_0001` |
| on | High | `connection-string-server` | Server / host in connection string - Server=..., Data Source=..., Host=..., Failover Partner=... in database, Redis and message-bus connection strings. | `VAILIT_DB_SERVER_0001` |
| on | Low | `connection-string-database` | Database name in connection string - Database=... / Initial Catalog=... names. | `VAILIT_DB_NAME_0001` |
| on | Medium | `host-assignment` | Host / server name in code or settings - Quoted values assigned to host, server, smtpServer, dbHost, bootstrapServers, bastion... | `VAILIT_HOST_0001` |
| on | Medium | `cli-host-argument` | Server passed as a command-line argument - Host names in PowerShell (-ComputerName, -ServerInstance...), WMIC (/node:) and CLI (--host) arguments. | `VAILIT_HOST_0001` |
| on | Medium | `host-config-line` | Host / server in config file (unquoted) - HOST=... / server: ... lines in .env, .ini, .properties, YAML and similar files. | `VAILIT_HOST_0001` |
| on | High | `unc-path` | UNC network path (\\server\share) - Server and share names in \\fileserver\share paths (single or doubled backslashes). | `VAILIT_UNC_PATH_0001` |
| on | High | `smb-nfs-path` | SMB / NFS mount source - //server/share in fstab, mount commands and smb:// URLs. | `VAILIT_NETWORK_PATH_0001` |
| on | Medium | `windows-user-path` | Windows user profile path - C:\Users\<name>\... paths reveal user names. | `VAILIT_USERNAME_0001` |
| on | Medium | `unix-home-path` | Linux / macOS home folder path - /home/<name>/... and /Users/<name>/... paths reveal user names. | `VAILIT_USERNAME_0001` |
| on | Medium | `ldap-dn` | LDAP / Active Directory distinguished name - CN=...,OU=...,DC=... paths reveal your directory structure, groups and accounts. | `VAILIT_LDAP_DN_0001` |
| on | High | `db-uri` | Database / broker / service connection URI - URIs such as mongodb+srv://..., postgresql://..., redis://..., amqp://..., jdbc:sqlserver://..., ldap://..., ssh://... | `VAILIT_CONNECTION_URI_0001` |
| on | Medium | `ssh-target` | SSH / SCP user@host - user@host targets in ssh, scp, sftp and rsync commands. | `VAILIT_SSH_TARGET_0001` |
| on | Medium | `docker-private-registry` | Private container registry - Registry host in image references such as registry.contoso.com:5000/team/api:1.2. Public registries (docker.io, ghcr.io, mcr.microsoft.com, quay.io, gcr.io...) are skipped. | `VAILIT_REGISTRY_0001` |
| on | Medium | `terraform-backend` | Terraform backend / state location - Bucket, storage account, resource group and organisation names in Terraform files. | `VAILIT_TF_BACKEND_0001` |
| on | Medium | `domain-controller-ou` | Domain / realm name setting - Quoted values assigned to domain, adDomain, realm, workgroup, forest... | `VAILIT_AD_DOMAIN_0001` |
| on | Medium | `port-binding-host` | Hosts in hosts file / nginx / Apache config - server_name, ServerName/ServerAlias, ssh_config Host/HostName entries. | `VAILIT_HOST_0001` |

## Endpoints

| On | Severity | Id | What it finds | Placeholder |
|---|---|---|---|---|
| on | Critical | `url-credentials` | Credentials inside a URL (user:password@host) - Any URL or URI with an embedded user name and password, e.g. https://admin:pa55@host, postgres://app:secret@db:5432. | `VAILIT_URL_CREDENTIALS_0001` |
| on | High | `url-secret-query` | Secret in URL query string - ?api_key=..., &token=..., &password=... and similar query-string parameters. | `VAILIT_URL_SECRET_0001` |
| on | High | `url-ip-host` | URL pointing at an IP address - http(s)://10.1.2.3:8080/... style URLs (servers reached by address). | `VAILIT_URL_IP_0001` |
| on | High | `url-internal-host` | URL with an internal host name - URLs whose host is in a private DNS zone, e.g. https://jira.corp.contoso.local/browse/ABC-1. | `VAILIT_INTERNAL_URL_0001` |
| on | Medium | `url-single-label-host` | URL with a single-word host (http://server/...) - Intranet-style URLs such as http://reportserver/Reports or http://build01:8080. | `VAILIT_INTERNAL_URL_0001` |
| on | Low | `url-api-endpoint` | Web address / API endpoint - Every other http(s), ws(s) and ftp URL. Well-known public sites (w3.org, microsoft docs, nuget, npm, CDNs, licences...) are skipped. Turn this off if you only care about secrets. | `VAILIT_URL_0001` |
| on | Medium | `graphql-grpc-endpoint` | gRPC / GraphQL endpoint setting - Quoted values assigned to endpoint, baseUrl, apiUrl, authority, issuer, audience, redirectUri, webhookUrl... | `VAILIT_ENDPOINT_0001` |

## Personal Data

| On | Severity | Id | What it finds | Placeholder |
|---|---|---|---|---|
| on | Medium | `email` | Email address - Email addresses of people, distribution lists and service accounts. example.com/org/net, noreply and git@ addresses are skipped. | `VAILIT_EMAIL_0001` |
| on | Medium | `phone-us` | Phone number (North America) - Numbers like (303) 555-0147, 303-555-0147, +1 303.555.0147. | `VAILIT_PHONE_0001` |
| *off* | Medium | `phone-international` | Phone number (international +CC format) - E.164-style numbers such as +44 20 7946 0958 or +61-2-5550-1234. Off by default (can match arithmetic in code). | `VAILIT_PHONE_0001` |
| on | Critical | `us-ssn` | US Social Security Number - 123-45-6789 style numbers (invalid ranges excluded). | `VAILIT_SSN_0001` |
| on | Critical | `credit-card` | Credit card number - Visa, Mastercard, Amex, Discover and Diners numbers that pass the Luhn checksum. Well-known test numbers (4111 1111 1111 1111 etc.) are skipped. | `VAILIT_CARD_NUMBER_0001` |
| on | High | `iban` | Bank account (IBAN) - International bank account numbers. | `VAILIT_IBAN_0001` |
| on | High | `us-bank-routing` | US bank routing / account number setting - Routing and account numbers in settings, fixtures and seed data. | `VAILIT_BANK_ACCOUNT_0001` |
| on | High | `passport-license` | Passport / driver licence / national id setting - Government identifiers by setting name. | `VAILIT_GOV_ID_0001` |
| on | Medium | `date-of-birth` | Date of birth - Dates assigned to dob / dateOfBirth / birthDate. | `VAILIT_DOB_0001` |
| *off* | Low | `person-name-field` | Person name in test / seed data - Quoted values assigned to firstName, lastName, fullName, displayName... Off by default: enable when you have real names in fixtures. | `VAILIT_PERSON_NAME_0001` |
| *off* | Low | `street-address` | Street address (US style) - Street addresses in fixtures and comments. Off by default. | `VAILIT_ADDRESS_0001` |

## Custom (examples)

| On | Severity | Id | What it finds | Placeholder |
|---|---|---|---|---|
| *off* | Medium | `custom-company-names` | Company & product names (edit me) - Exact text, one per line. Replace these examples with your company, subsidiary, client and product names. | `VAILIT_COMPANY_0001` |
| *off* | High | `custom-domains` | Company domains (edit me) - Regex for your public and internal DNS domains, including every sub-domain. | `VAILIT_DOMAIN_0001` |
| *off* | High | `custom-server-naming` | Server naming convention (edit me) - Matches names such as PROD-SQL01, UAT_WEB2, DEVAPP003. Change it to match how your servers are named. | `VAILIT_SERVER_0001` |
| *off* | Medium | `custom-project-codenames` | Project code names (edit me) - Exact text, one per line: internal project, programme or customer code names. | `VAILIT_PROJECT_0001` |
| *off* | Medium | `custom-employee-id` | Employee / customer id format (edit me) - Example id formats like EMP-004512 or CUST12345678. Adjust to your own. | `VAILIT_EMPLOYEE_ID_0001` |
| *off* | Low | `custom-ticket-links` | Internal ticket references (edit me) - Ticket keys from internal projects whose names are sensitive. Replace the keys with your own. | `VAILIT_TICKET_0001` |
| *off* | High | `custom-sensitive-line` | Remove whole line containing a marker (edit me) - Exact markers: the WHOLE line containing one of them is replaced (Scope = Line). | `VAILIT_REMOVED_LINE_0001` |
