# VailIT Code Clean

A Windows application that scans a folder of source code, and every folder under it, for things you should not
hand to an AI assistant or another outside organisation. That includes passwords, API keys, tokens, private keys,
connection strings, server names, IP addresses, internal URLs, cloud account IDs and personal data.

It writes a **scrubbed copy** of the code into a separate output folder. Every sensitive value is replaced with a
readable placeholder such as `VAILIT_PASSWORD_0001`, and it also writes a **report** of what it found and where.
The original code is never changed.

```text
var conn = "Server=PROD-SQL01;User Id=svc_sales;Password=Spring2024!;";      // before
var conn = "Server=VAILIT_HOST_0001;User Id=VAILIT_USERNAME_0001;Password=VAILIT_PASSWORD_0001;";  // after
```

## Quick start

```powershell
# 1. Build (needs the .NET 10 SDK:  winget install Microsoft.DotNet.SDK.10)
.\scripts\Build-VailITCodeClean.ps1

# 2. Start the app
.\publish\App\VailITCodeClean.exe
```

On the first start, choose **File > Create sample project...** to make a fake project full of planted secrets.
Then press **Start** to see what the tool does. After that, point **Code folder to scan** at your own code.

Or open `VailITCodeClean.sln` in Visual Studio, set **VailITCodeClean.App** as the startup project and press F5.

## What you get

* **178 sample rules** in 8 categories. 166 are on by default. They cover keys and certificates, AWS, Azure,
  Google Cloud and other clouds, 57 vendor token formats (GitHub, Stripe, Slack, OpenAI and others), passwords
  and user names, infrastructure (IPs, hosts, UNC paths, LDAP, connection URIs), endpoints and personal data.
  The full list is in [docs/05-Rule-Catalog.md](docs/05-Rule-Catalog.md).
* **Your own filters**: add rules for **exact text** (company names, server names, one per line) or **regular
  expressions**. You can test each one live against sample text before using it.
* **Line numbers preserved.** Line *N* of a scrubbed file is line *N* of the original, even when a value
  spans several lines, such as a private key. Line endings, encoding and byte-order marks are kept as they were.
* **Readable placeholders.** The same value always gets the same placeholder, so a reader (or an AI) can still
  see that two files use the same password or server. You can also use masking (`*****`) or fixed text
  (`[REDACTED]`).
* **A separate output folder** for each run: `Scrubbed\` (the copy to share) and `Report\` (keep this private).
* **Reports** in HTML (with filtering and sorting), CSV and JSON. They show each finding's file, line, rule,
  severity, a masked preview (`Sp*******4!`) and the scrubbed line.
* **Sensitive files are left out.** Files such as `.pfx`, `.jks`, `id_rsa`, `.tfstate` and `.kdbx` are never
  copied to the output. They are listed in the report instead.
* **Optional restore.** A mapping file lets you put the real values back into code that comes back from an AI.
* **A command-line version** (`vailit-codeclean.exe`) that uses the same engine and profiles, for scripts and
  CI pipelines.

## Folder layout

| Folder | What's in it |
|---|---|
| `src/VailITCodeClean.Core` | The engine: rules, scanning, scrubbing and reports. No user interface code. |
| `src/VailITCodeClean.Core/Rules` | **The sample rules**, one file per category. Edit these to change the built-in rules. |
| `src/VailITCodeClean.App` | The Windows application (WinForms, built in plain C#, with no designer files). |
| `src/VailITCodeClean.Cli` | The command-line tool. |
| `tests/VailITCodeClean.Tests` | Unit tests. Every rule is tested against its own example. |
| `samples/` | `Default.profile.json` (all rules as JSON) and `My-Company.rules.json` (a template for your own rules). |
| `scripts/` | Build script and the rule-catalog generator. |
| `docs/` | The guides below. |

## Documentation

| Guide | What's in it |
|---|---|
| [01 How it works](docs/01-How-It-Works.md) | What happens during a run, overlapping matches, placeholders, line preservation, the output folders |
| [02 Build and run](docs/02-Build-And-Run.md) | Building, publishing, the command line, using it in CI |
| [03 Using the app](docs/03-Using-The-App.md) | A tour of every tab and option |
| [04 Writing rules](docs/04-Writing-Rules.md) | Exact and regex rules, `secret` groups, ignore lists, file filters, tips and examples |
| [05 Rule catalog](docs/05-Rule-Catalog.md) | Every built-in rule |
| [06 Editing the code](docs/06-Editing-The-Code.md) | Where everything lives and how to change it |

## Important

No automated tool finds everything. A secret with an unusual name, a password in a comment or a customer's
name in a string can all get through. **Always look through the scrubbed copy before you share it.** Add a rule
for anything that was missed, so it is caught next time.
