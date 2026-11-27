using VailIT.CodeClean.Configuration;

namespace VailIT.CodeClean.Rules;

public static partial class DefaultRules
{
    /// <summary>
    /// API keys and tokens of well-known services, recognised by their fixed prefixes.
    /// These are very reliable (almost no false positives) so they are all on by default.
    /// </summary>
    public static IEnumerable<FilterRule> ServiceTokens()
    {
        const string c = CatTokens;

        // Small local helper: most token rules are "prefix + charset", case sensitive, critical.
        static FilterRule T(string id, string name, string label, string pattern, string description, string example,
            Severity severity = Severity.Critical) =>
            Rule(id, name, CatTokens, severity, label, pattern, description, example).Cs();

        // ---- HTTP authorisation --------------------------------------------------------------------
        yield return Rule("auth-header", "Authorization header value", c, Severity.High, "AUTH_HEADER",
                """(?i)\bAuthorization["']?\s*(?:[:=,]|\]\s*=)\s*["']?(?:Bearer|Basic|Token|Digest|Negotiate|NTLM|ApiKey|SSWS|Splunk|GenieKey|Key)\s+(?<secret>[A-Za-z0-9\-._~+/=]{8,})""",
                "The credential part of Authorization: Bearer/Basic/Token ... headers (code, .http files, Postman, curl).",
                "Authorization: Bearer {{URLSAFE:40}}");

        yield return Rule("bearer-token", "Bearer token", c, Severity.High, "BEARER_TOKEN",
                """\b[Bb]earer\s+(?<secret>[A-Za-z0-9\-._~+/]{20,}=*)""",
                "Any 'Bearer xxxx' string with a long token after it.",
                "client.DefaultRequestHeaders.Add(\"X\", \"Bearer {{URLSAFE:40}}\");");

        yield return Rule("auth-header-value-object", "AuthenticationHeaderValue(\"Bearer\", \"...\")", c, Severity.High, "AUTH_HEADER",
                """(?i)AuthenticationHeaderValue\s*\(\s*"(?:Bearer|Basic|Token|ApiKey)"\s*,\s*@?\$?"(?<secret>[^"{}]{8,})""",
                ".NET HttpClient headers with a hard-coded token.",
                "new AuthenticationHeaderValue(\"Bearer\", \"{{URLSAFE:40}}\")");

        yield return Rule("api-key-header", "API key header (x-api-key, Ocp-Apim-Subscription-Key...)", c, Severity.High, "API_KEY",
                """(?i)\b(?:x-api-key|x-apikey|api-key|apikey|x-auth-token|x-access-token|x-functions-key|ocp-apim-subscription-key|private-token|x-vault-token|x-api-token|x-client-secret|x-goog-api-key|x-rapidapi-key|x-algolia-api-key|x-figma-token|dd-api-key|dd-application-key)["']?\s*[:=,]\s*["']?(?<secret>[A-Za-z0-9\-._~+/=]{12,})""",
                "Common API-key headers used by API gateways and SaaS services.",
                "Ocp-Apim-Subscription-Key: {{HEX:32}}");

        // ---- source control / CI / package registries -----------------------------------------------
        yield return T("github-token", "GitHub token", "GITHUB_TOKEN",
            """\b(?:gh[pousr]_[A-Za-z0-9]{36,255}|github_pat_[A-Za-z0-9_]{22,255})\b""",
            "GitHub personal access (classic and fine-grained), OAuth, app and refresh tokens.",
            "ghp_{{ALNUM:36}}");

        yield return T("gitlab-token", "GitLab token", "GITLAB_TOKEN",
            """\bgl(?:pat|dt|rt|ptt|cbt|soat|ffct|imt|agent|oas)-[A-Za-z0-9_\-]{20,}\b""",
            "GitLab personal, deploy, runner, pipeline trigger and other tokens.",
            "glpat-{{URLSAFE:20}}");

        yield return T("bitbucket-token", "Bitbucket / Atlassian API token", "ATLASSIAN_TOKEN",
            """\b(?:ATATT3|ATCTT3|ATBB)[A-Za-z0-9_\-=]{24,}\b""",
            "Atlassian (Jira, Confluence, Bitbucket) API tokens and app passwords.",
            "ATATT3xFfGF0{{URLSAFE:60}}");

        yield return T("npm-token", "npm access token", "NPM_TOKEN",
            """\bnpm_[A-Za-z0-9]{36}\b""",
            "npm automation / publish tokens.",
            "//registry.npmjs.org/:_authToken=npm_{{ALNUM:36}}");

        yield return T("pypi-token", "PyPI upload token", "PYPI_TOKEN",
            """\bpypi-AgEIcHlwaS5vcmc[A-Za-z0-9_\-]{50,}\b""",
            "PyPI API tokens.",
            "password = pypi-AgEIcHlwaS5vcmc{{URLSAFE:60}}");

        yield return T("nuget-key", "NuGet API key", "NUGET_KEY",
            """\boy2[a-z0-9]{43}\b""",
            "nuget.org API keys.",
            "dotnet nuget push pkg.nupkg -k oy2{{LOWNUM:43}}");

        yield return T("docker-hub-token", "Docker Hub token", "DOCKER_TOKEN",
            """\bdckr_(?:pat|oat)_[A-Za-z0-9_\-]{27,}\b""",
            "Docker Hub personal / organisation access tokens.",
            "dckr_pat_{{URLSAFE:27}}");

        yield return Rule("docker-config-auth", "Docker config.json auth", c, Severity.Critical, "DOCKER_AUTH",
                """["](?:auth|identitytoken|registrytoken)["]\s*:\s*["](?<secret>[A-Za-z0-9+/=_\-.]{12,})["]""",
                "Base64 user:password pairs and identity tokens in ~/.docker/config.json.",
                "\"auth\": \"{{B64:32}}\"")
            .Cs();

        yield return T("jfrog-token", "JFrog Artifactory token", "JFROG_TOKEN",
            """\b(?:AKCp[A-Za-z0-9]{69}|cmVmdGtu[A-Za-z0-9]{56})\b""",
            "Artifactory API keys and reference tokens.",
            "AKCp{{ALNUM:69}}");

        yield return T("terraform-cloud-token", "Terraform Cloud / Enterprise token", "TERRAFORM_TOKEN",
            """\b[A-Za-z0-9]{14}\.atlasv1\.[A-Za-z0-9_\-=]{60,70}\b""",
            "HCP Terraform API tokens.",
            "{{ALNUM:14}}.atlasv1.{{URLSAFE:64}}");

        yield return T("vault-token", "HashiCorp Vault token", "VAULT_TOKEN",
            """\bhv[sbr]\.[A-Za-z0-9_\-]{24,}\b""",
            "Vault service, batch and recovery tokens.",
            "VAULT_TOKEN=hvs.{{URLSAFE:90}}");

        yield return T("pulumi-token", "Pulumi access token", "PULUMI_TOKEN",
            """\bpul-[a-f0-9]{40}\b""", "Pulumi Cloud access tokens.", "pul-{{HEX:40}}");

        yield return T("doppler-token", "Doppler token", "DOPPLER_TOKEN",
            """\bdp\.(?:pt|st|sa|ct|scim|audit)\.[A-Za-z0-9]{40,44}\b""", "Doppler secrets-manager tokens.", "dp.st.{{ALNUM:43}}");

        yield return T("postman-key", "Postman API key", "POSTMAN_KEY",
            """\bPMAK-[a-f0-9]{24}-[a-f0-9]{34}\b""", "Postman API keys.", "PMAK-{{HEX:24}}-{{HEX:34}}");

        yield return T("snyk-sonar-token", "Snyk / SonarQube token", "SCANNER_TOKEN",
            """\b(?:sq[apu]_[a-f0-9]{40}|snyk_[A-Za-z0-9_\-]{30,})\b""", "SonarQube user/project/analysis tokens and Snyk tokens.", "squ_{{HEX:40}}");

        yield return T("circleci-buildkite", "CircleCI / Buildkite token", "CI_TOKEN",
            """\b(?:CCIPAT_[A-Za-z0-9]{22}_[a-f0-9]{40}|bkua_[a-f0-9]{40})\b""", "CircleCI personal tokens and Buildkite agent tokens.", "bkua_{{HEX:40}}");

        // ---- payments / commerce --------------------------------------------------------------------
        yield return T("stripe-secret-key", "Stripe secret / restricted key", "STRIPE_KEY",
            """\b(?:sk|rk)_(?:live|test)_[A-Za-z0-9]{10,247}\b""", "Stripe secret and restricted API keys.", "sk_" + "live_{{ALNUM:24}}");

        yield return T("stripe-publishable-key", "Stripe publishable key", "STRIPE_PUBLISHABLE_KEY",
            """\bpk_(?:live|test)_[A-Za-z0-9]{10,247}\b""", "Publishable keys are meant for browsers but still identify your account.", "pk_" + "live_{{ALNUM:24}}",
            Severity.Low);

        yield return T("stripe-webhook-secret", "Stripe webhook signing secret", "STRIPE_WEBHOOK_SECRET",
            """\bwhsec_[A-Za-z0-9]{24,}\b""", "Webhook endpoint signing secrets.", "whsec_{{ALNUM:32}}");

        yield return T("square-token", "Square access token / secret", "SQUARE_TOKEN",
            """\b(?:sq0atp-[0-9A-Za-z\-_]{22}|sq0csp-[0-9A-Za-z\-_]{43}|EAAA[A-Za-z0-9\-_+=]{60})\b""", "Square access tokens and OAuth secrets.", "sq0atp-{{URLSAFE:22}}");

        yield return T("braintree-token", "Braintree / PayPal access token", "PAYPAL_TOKEN",
            """\baccess_token\$(?:production|sandbox)\$[0-9a-z]{16}\$[0-9a-f]{32}\b""", "Braintree gateway access tokens.", "access_token$production${{LOWNUM:16}}${{HEX:32}}");

        yield return T("shopify-token", "Shopify token", "SHOPIFY_TOKEN",
            """\bshp(?:at|ca|pa|ss)_[a-fA-F0-9]{32}\b""", "Shopify admin, custom app, private app tokens and shared secrets.", "shpat_{{HEX:32}}");

        // ---- messaging / collaboration -------------------------------------------------------------
        yield return T("slack-token", "Slack token", "SLACK_TOKEN",
            """\b(?:xox[abposr]-[A-Za-z0-9\-]{10,250}|xapp-\d-[A-Z0-9]+-\d+-[a-z0-9]+)\b""", "Slack bot, user, app and configuration tokens.", "xox" + "b-{{DIGITS:12}}-{{DIGITS:12}}-{{ALNUM:24}}");

        yield return T("slack-webhook", "Slack incoming webhook URL", "SLACK_WEBHOOK",
            """https://hooks\.slack\.com/(?:services|workflows|triggers)/[A-Za-z0-9+/_\-]{20,}""", "Anyone with this URL can post into your Slack channel.", "https://hooks.slack.com/services/T{{UPNUM:8}}/B{{UPNUM:8}}/{{ALNUM:24}}");

        yield return T("teams-webhook", "Microsoft Teams / Office 365 webhook URL", "TEAMS_WEBHOOK",
            """https://[\w\-]+\.(?:webhook\.office\.com/webhookb2|logic\.azure\.com:443/workflows|outlook\.office(?:365)?\.com/webhook)/[^\s"'<>]+""", "Incoming webhooks and Power Automate trigger URLs for Teams.", "https://contoso.webhook.office.com/webhookb2/{{HEX:8}}-{{HEX:4}}/IncomingWebhook/{{HEX:32}}");

        yield return T("discord-webhook", "Discord webhook URL", "DISCORD_WEBHOOK",
            """https://(?:ptb\.|canary\.)?discord(?:app)?\.com/api/webhooks/\d+/[\w\-]+""", "Discord channel webhooks.", "https://discord.com/api/webhooks/{{DIGITS:18}}/{{URLSAFE:68}}");

        yield return T("discord-bot-token", "Discord bot token", "DISCORD_TOKEN",
            """\b[MNO][A-Za-z\d]{23,25}\.[\w\-]{6}\.[\w\-]{27,38}\b""", "Discord bot tokens.", "M{{ALNUM:23}}.{{URLSAFE:6}}.{{URLSAFE:27}}");

        yield return T("telegram-bot-token", "Telegram bot token", "TELEGRAM_TOKEN",
            """\b\d{8,10}:AA[A-Za-z0-9_\-]{33}\b""", "Telegram Bot API tokens.", "{{DIGITS:10}}:AA{{URLSAFE:33}}");

        yield return T("twilio-key", "Twilio API key / account SID", "TWILIO",
            """\b(?:SK|AC)[0-9a-f]{32}\b""", "Twilio API key SIDs (SK...) and account SIDs (AC...).", "TWILIO_API_KEY=SK{{HEX:32}}", Severity.High);

        yield return T("sendgrid-key", "SendGrid API key", "SENDGRID_KEY",
            """\bSG\.[A-Za-z0-9_\-]{22}\.[A-Za-z0-9_\-]{43}\b""", "SendGrid email API keys.", "SG.{{URLSAFE:22}}.{{URLSAFE:43}}");

        yield return T("mailgun-key", "Mailgun API key", "MAILGUN_KEY",
            """\bkey-[0-9a-zA-Z]{32}\b""", "Mailgun private API keys.", "key-{{ALNUM:32}}");

        yield return T("mailchimp-key", "Mailchimp API key", "MAILCHIMP_KEY",
            """\b[0-9a-f]{32}-us\d{1,2}\b""", "Mailchimp marketing API keys.", "{{HEX:32}}-us21");

        yield return T("postmark-mandrill", "Postmark / Mandrill / Brevo key", "EMAIL_API_KEY",
            """\b(?:xkeysib-[a-f0-9]{64}-[A-Za-z0-9]{16}|md-[A-Za-z0-9_\-]{22})\b""", "Brevo (Sendinblue) and Mandrill API keys.", "xkeysib-{{HEX:64}}-{{ALNUM:16}}");

        yield return T("notion-token", "Notion integration token", "NOTION_TOKEN",
            """\b(?:secret_[A-Za-z0-9]{43}|ntn_[A-Za-z0-9]{40,})\b""", "Notion internal integration secrets.", "ntn_{{ALNUM:46}}");

        yield return T("linear-key", "Linear API key", "LINEAR_KEY",
            """\blin_api_[A-Za-z0-9]{40}\b""", "Linear API keys.", "lin_api_{{ALNUM:40}}");

        yield return T("asana-figma-airtable", "Airtable / Figma / Asana token", "SAAS_TOKEN",
            """\b(?:pat[A-Za-z0-9]{14}\.[a-f0-9]{64}|figd_[A-Za-z0-9_\-]{40,}|[0-9]/[0-9]{16}:[A-Za-z0-9]{32})\b""", "Airtable personal access tokens, Figma tokens and Asana PATs.", "figd_{{URLSAFE:40}}");

        // ---- monitoring / observability -------------------------------------------------------------
        yield return T("sentry-dsn", "Sentry DSN / token", "SENTRY",
            """(?:https://[0-9a-f]{32}@[\w.\-]*(?:sentry\.io|ingest\.[\w.\-]+)/\d+|\bsntry[su]_[A-Za-z0-9+/=_]{40,})""", "Sentry DSNs (contain the project key) and auth tokens.", "https://{{HEX:32}}@o123456.ingest.sentry.io/1234567", Severity.High);

        yield return T("newrelic-key", "New Relic key", "NEWRELIC_KEY",
            """\b(?:NRAK-[A-Z0-9]{27}|NRII-[A-Za-z0-9_\-]{32}|NRJS-[a-f0-9]{19}|[a-f0-9]{36}NRAL)\b""", "New Relic user, ingest, browser and license keys.", "NRAK-{{UPNUM:27}}");

        yield return Rule("datadog-key", "Datadog API / application key", c, Severity.Critical, "DATADOG_KEY",
                """(?i)\b(?:dd|datadog)_?(?:api|app|application|client)_?key["']?\s*(?:=|:|=>)\s*["']?(?<secret>[a-f0-9]{32}|[a-f0-9]{40})\b""",
                "Datadog keys (recognised by their setting name).",
                "DD_API_KEY={{HEX:32}}");

        yield return T("grafana-token", "Grafana token", "GRAFANA_TOKEN",
            """\b(?:glsa_[A-Za-z0-9]{32}_[A-Fa-f0-9]{8}|glc_[A-Za-z0-9+/]{32,}={0,2})""", "Grafana service account and Grafana Cloud tokens.", "glsa_{{ALNUM:32}}_{{HEX:8}}");

        yield return Rule("splunk-hec", "Splunk HEC token", c, Severity.High, "SPLUNK_TOKEN",
                $$"""(?i)\b(?:Splunk|hec_?token|splunk_?token)["']?\s*[:=\s]\s*["']?(?<secret>{{Guid}})""",
                "Splunk HTTP Event Collector tokens.",
                "Authorization: Splunk {{HEX:8}}-{{HEX:4}}-{{HEX:4}}-{{HEX:4}}-{{HEX:12}}");

        yield return T("dynatrace-token", "Dynatrace token", "DYNATRACE_TOKEN",
            """\bdt0[a-z]\d{2}\.[A-Z0-9]{24}\.[A-Z0-9]{64}\b""", "Dynatrace API tokens.", "dt0c01.{{UPNUM:24}}.{{UPNUM:64}}");

        // ---- AI / ML providers ------------------------------------------------------------------------
        yield return T("openai-key", "OpenAI API key", "OPENAI_KEY",
            """\bsk-(?:(?:proj|svcacct|admin)-[A-Za-z0-9_\-]{40,}|[A-Za-z0-9]{20}T3BlbkFJ[A-Za-z0-9]{20}|[A-Za-z0-9]{48})\b""", "OpenAI project, service-account, admin and legacy keys.", "sk-proj-{{URLSAFE:64}}");

        yield return T("anthropic-key", "Anthropic API key", "ANTHROPIC_KEY",
            """\bsk-ant-(?:api|admin|oat)\d{2}-[A-Za-z0-9_\-]{80,}\b""", "Anthropic (Claude) API and admin keys.", "sk-ant-api03-{{URLSAFE:93}}");

        yield return T("huggingface-token", "Hugging Face token", "HUGGINGFACE_TOKEN",
            """\bhf_[A-Za-z0-9]{34,}\b""", "Hugging Face user access tokens.", "hf_{{ALNUM:34}}");

        yield return T("other-ai-keys", "Groq / xAI / Perplexity / Replicate key", "AI_KEY",
            """\b(?:gsk_[A-Za-z0-9]{52}|xai-[A-Za-z0-9]{70,}|pplx-[A-Za-z0-9]{40,}|r8_[A-Za-z0-9]{37})\b""", "Keys for other AI APIs.", "gsk_{{ALNUM:52}}");

        // ---- data / backend platforms ---------------------------------------------------------------
        yield return T("databricks-token", "Databricks token", "DATABRICKS_TOKEN",
            """\bdapi[a-f0-9]{32}(?:-\d)?\b""", "Databricks personal access tokens.", "dapi{{HEX:32}}");

        yield return T("supabase-planetscale", "Supabase / PlanetScale token", "DB_PLATFORM_TOKEN",
            """\b(?:sbp_[a-f0-9]{40}|sb_secret_[A-Za-z0-9_\-]{20,}|pscale_(?:tkn|pw|oauth)_[A-Za-z0-9_\-.]{32,64})\b""", "Supabase management/secret keys and PlanetScale passwords/tokens.", "sbp_{{HEX:40}}");

        yield return T("mapbox-token", "Mapbox secret token", "MAPBOX_TOKEN",
            """\bsk\.eyJ[A-Za-z0-9_\-]+\.[A-Za-z0-9_\-]{20,}\b""", "Mapbox secret access tokens.", "sk.eyJ1Ijoi{{URLSAFE:40}}.{{URLSAFE:22}}");

        yield return T("dropbox-token", "Dropbox token", "DROPBOX_TOKEN",
            """\bsl\.[A-Za-z0-9_\-]{130,}\b""", "Dropbox short-lived access tokens.", "sl.{{URLSAFE:135}}");

        yield return T("facebook-twitter-token", "Facebook / Twitter (X) token", "SOCIAL_TOKEN",
            """\b(?:EAA[A-Za-z0-9]{90,}|AAAAAAAAAAAAAAAAAAAAA[A-Za-z0-9%]{30,})\b""", "Facebook Graph access tokens and Twitter/X app bearer tokens.", "EAA{{ALNUM:100}}");

        yield return Rule("okta-token", "Okta API token", c, Severity.Critical, "OKTA_TOKEN",
                """\bSSWS\s+(?<secret>[A-Za-z0-9_\-]{40,})""",
                "Okta API tokens in SSWS authorisation headers.",
                "Authorization: SSWS 00{{URLSAFE:40}}")
            .Cs();

        yield return Rule("cloudflare-heroku-vercel", "Cloudflare / Heroku / Vercel / Netlify token setting", c, Severity.Critical, "PLATFORM_TOKEN",
                """(?i)\b(?:cloudflare|cf|heroku|vercel|netlify|fly|render|railway|algolia|firebase|contentful|twilio_?auth|auth0|okta|segment|mixpanel|amplitude|launchdarkly|pagerduty|opsgenie|rollbar|bugsnag|honeycomb|logdna|loggly|papertrail|circleci|travis|codecov|jenkins)_?(?:api_?)?(?:token|key|secret|auth_?token|access_?token|admin_?key|write_?key|sdk_?key)["']?\s*(?:=|:|=>)\s*["']?(?<secret>[A-Za-z0-9_\-.:]{20,128})""",
                "Tokens for many platforms that have no fixed prefix, recognised by their setting name (CLOUDFLARE_API_TOKEN, HEROKU_API_KEY, VERCEL_TOKEN...).",
                "CLOUDFLARE_API_TOKEN={{URLSAFE:40}}")
            .IgnorePlaceholders();
    }
}
