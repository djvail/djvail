# Writing rules

A rule is one thing to look for. You can create rules in the app (**Rules** tab > **Add...**), in a
`*.profile.json` or `*.rules.json` file, or in C# (`src/VailITCodeClean.Core/Rules`). All three use the same fields.

## Exact text rules

The easiest kind. Set **Type = Exact** and put one literal text per line:

```text
Contoso
Contoso Ltd
contoso-prod-sql01
Project Bluebird
```

* Nothing needs escaping. `.`, `(`, `\` and `$` mean exactly themselves.
* **Whole word** means `Contoso` does not match inside `ContosoApp`.
* **Case sensitive** is off by default.
* Longer entries are tried first, so `Contoso Ltd` wins over `Contoso`.

## Regular expression rules

Set **Type = Regex** and write a .NET regular expression. Multi-line mode is always on (`^` and `$` match at
each line). Case-insensitive mode is on unless you tick *Case sensitive*.

### Replace only part of the match: the `secret` group

Usually you want to keep the name and hide the value. Put the value in a group called `secret`:

```regex
(?i)smtp_?password\s*[=:]\s*["']?(?<secret>[^"'\s]+)
```

`SMTP_PASSWORD = "hunter22"` becomes `SMTP_PASSWORD = "VAILIT_PASSWORD_0001"`.

If there is no `secret` group, the whole match is replaced.

### Several values in one match: `secret_LABEL` groups

Every group whose name starts with `secret_` is replaced separately, and the text after the underscore becomes
the placeholder label:

```regex
\b(?<secret_USERNAME>[\w.]+):(?<secret_PASSWORD>[^@\s]+)@(?<secret_HOST>[\w.\-]+)
```

`deploy:Spring2024!@build01` becomes `VAILIT_USERNAME_0001:VAILIT_PASSWORD_0001@VAILIT_HOST_0001`.

Tick **All parts required** if the match should be ignored as soon as one part is on an ignore list. For example,
in `\\server\share`, if `server` is a placeholder name, the share next to it does not matter either.

### Line endings

`$` in .NET does **not** match before `\r`, and most Windows files end lines with `\r\n`. To anchor at the end
of a line, use `(?=\r?$)` or `\r?$` instead of a bare `$`. The unit tests check every built-in rule with both
kinds of line ending.

## All fields

| Field | Purpose |
|---|---|
| Name, Id, Category, Description | Shown in the app and the report. The id must be unique. |
| Severity | Info, Low, Medium, High, Critical. Used for sorting and colours, the `--fail-on` exit code, and to choose the label when matches overlap. |
| Placeholder label | `PASSWORD` gives `VAILIT_PASSWORD_0001`. When empty, the category name is used. |
| Replace | **Match** (default) or **Line**, which replaces the whole line's content and keeps the line break. |
| Min length | Shorter values are ignored. |
| Min entropy | Bits per character. Random keys score about 3.5 to 6, words and placeholders score lower. Use 3.5+ to catch "random-looking" values only. |
| Validator | `Luhn` = the digits must pass the credit-card checksum. |
| Ignore values | Exact values to ignore for this rule, one per line (`admin`, `root`, test card numbers...). |
| Ignore value regex | Skip values that match. Example: `^\$\{.*\}$` to skip `${DB_PASSWORD}` style references. |
| Ignore line regex | Skip a match when its line matches. Example: `AssemblyVersion` for the IP-address rules. |
| Only files / Never files | File patterns that limit where the rule runs: `*.yml`, `.env`, `src/**/appsettings*.json`. |
| Example / test text | Text the rule should catch. It is used by the live test in the editor and by the unit tests. Fake values can be written as `{{ALNUM:36}}`, `{{HEX:32}}`, `{{B64:40}}`, `{{DIGITS:12}}`, `{{UPNUM:16}}`, `{{URLSAFE:43}}` so no real-looking key is stored. |

The **Allow list** tab applies to every rule.

## Recipes

**Company domains and every sub-domain**

```regex
(?i)\b(?:[a-z0-9\-]+\.)*(?:contoso\.com|contoso\.local|contoso-int\.net)\b
```

**A server naming scheme** (PROD-SQL01, UAT_WEB2, DEVAPP003)

```regex
(?i)\b(?:PRD|PROD|UAT|STG|DEV|QA|DR)[\-_]?(?:SQL|DB|APP|WEB|DC|FS|SRV|VM)[\-_]?\d{1,3}[a-z]?\b
```

**A setting whose name you know, in any file format**

```regex
(?i)\b(?:LicenseKey|LICENSE_KEY|license-key)["']?\s*[=:]\s*["']?(?<secret>[A-Z0-9\-]{16,})
```

**Your own token format** (e.g. `CTS-` followed by 32 hex characters)

```regex
\bCTS-[0-9a-f]{32}\b
```

Tick *Case sensitive*, which also makes the match faster.

**Customer or employee names from a list**: an Exact rule with one name per line, *Whole word* ticked.

**Remove whole lines that carry a marker**: an Exact rule with `CONFIDENTIAL`, *Replace = Line*.

**IP addresses of one subnet only**

```regex
(?<![\d.])10\.42\.\d{1,3}\.\d{1,3}(?![\d.])
```

**A value only in certain files**: put `*.tfvars` and `*.auto.tfvars` in *Only files*.

## Helpers for C# rules

In `src/VailITCodeClean.Core/Rules/DefaultRules.cs`:

* `QuotedAssignment("password|pwd")` builds a pattern for `name = "value"` in C#, Java, JS/TS, Python,
  PowerShell, PHP, Go, JSON, YAML, XML attributes and Terraform. Any prefix before the name is allowed
  (`dbPassword`, `SMTP_PASSWORD`) but nothing after it, so `passwordHint` does not match.
* `LineAssignment("password|pwd")` builds a pattern for unquoted `NAME=value` and `name: value` lines in .env,
  .ini, .properties, YAML and shell files. Combine it with `.Files(ConfigFiles)`.
* Chainable helpers: `.Cs()`, `.Off()`, `.MinLen(n)`, `.Entropy(x)`, `.Ignore(values)`, `.IgnoreValue(regex)`,
  `.IgnorePlaceholders()`, `.IgnoreLine(regex)`, `.Files(globs)`, `.NotFiles(globs)`, `.Luhn()`,
  `.WholeLine()`, `.Exact()`, `.AllParts()`.

After adding a rule in C#, run `dotnet test`. It checks that the rule compiles, matches its example with
both `\n` and `\r\n` line endings, and has a unique id.
