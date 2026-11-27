# How it works

## A run, step by step

1. **List files.** Every file under the code folder is listed. Folders named in *Folders to skip* (`bin`, `obj`,
   `.git`, `node_modules` and so on) are not entered. Symbolic links and junctions are not followed, so a link
   cannot cause an endless loop. If the output folder is inside the code folder, it is skipped too.
2. **Sort each file** into one of these groups:
   * matches *Files to ignore*, or is not in *Only scan these files*: **Excluded**, which means it is not
     copied, only counted
   * matches *Sensitive files* (`*.pfx`, `id_rsa`, `*.tfstate`...): **Sensitive**. It is never copied and
     is reported as a High finding
   * larger than *Max file size*: **TooLarge**. It is not scanned, not copied and gets a warning
   * contains a NUL byte in its first 8 KB: **Binary**. It is skipped, or copied unchanged if *Copy binary
     files* is on
   * everything else is a **text file** and is scanned.
3. **Scan (in parallel).** Each enabled rule runs over the file's text. A rule can be limited to certain files,
   for example only `*.yml`. Each hit goes through that rule's checks: minimum length, entropy, the Luhn
   checksum, ignore values, ignore-value regex, ignore-line regex and the global allow list.
4. **Merge overlaps.** When several hits overlap they become one replacement that covers all of them, so
   nothing is left half hidden. For example, `amqp://orders:Winter2025#@rabbit01.corp.local` is matched by the
   URL-credentials, internal-host and connection-URI rules, and becomes one placeholder. The label comes from the
   rule whose match covers the whole span. If several do, the most severe one is used. The other rules are listed
   as "also matched" in the report.
5. **Write (in order).** The files are processed alphabetically. Each value gets its placeholder, the scrubbed
   text is written with the original encoding, byte-order mark and line endings, and the findings are recorded.
   Because the order is fixed, the same code always gets the same placeholder numbers.
6. **Report.** `report.html`, `findings.csv`, `files.csv` and `findings.json` are written, plus `mapping.json`
   if you asked for it.

Scanning a large folder (5,600 files, 640 MB, mostly binaries) takes about 6 seconds on a 4-core machine. Every
regular expression has a time limit of 3 s per file by default, so one bad pattern cannot hang a run. If a rule
hits the limit, it is skipped for that file and a warning appears in the report.

## The output folder

```text
<Output folder>\
  MyProject_20260412_101500\        one folder per run (unless you turn this off)
    README.txt                      which folder is which
    Scrubbed\                       the cleaned copy, same folder structure: THIS is what you share
    Report\                         keep private
      report.html                   summary, by rule, by file, every finding (filter + sort)
      findings.csv                  one line per finding (opens in Excel)
      files.csv                     what happened to every file
      findings.json                 everything, for scripts
      mapping.json                  only if enabled: placeholder -> ORIGINAL value
    .vailit-codeclean               marker file
```

If you turn off *New dated sub folder for each run*, the tool writes straight into the output folder. It only
clears that folder's `Scrubbed` and `Report` sub folders if the marker file shows that VailIT Code Clean created
it. It refuses to use any other folder that is not empty.

## Placeholders

| Style | Looks like | Notes |
|---|---|---|
| Token (default) | `VAILIT_PASSWORD_0001` | Built from the template `{PREFIX}_{LABEL}_{ID}` |
| Token with hash | `VAILIT_PASSWORD_3FA94C1D` | Template `{PREFIX}_{LABEL}_{HASH}`. With a fixed *Hash salt*, the same value gets the same placeholder on every run and on every PC |
| Mask | `***********` | The same length as the original, so columns are kept too |
| Fixed | `[REDACTED]` | Every value looks the same |

* `{LABEL}` comes from the rule's *Placeholder label* (`PASSWORD`, `AWS_ACCESS_KEY`...), or from a
  `secret_LABEL` group in the pattern.
* `{ID}` counts up separately for each label.
* With *Same value = same placeholder* on (the default), `Spring2024!` becomes `VAILIT_PASSWORD_0001`
  everywhere in the run, even in another file or under another rule. A reader can still see that two places
  share a secret without knowing what it is.
* `{HASH}` is HMAC-SHA256(salt, value). With no salt, a random salt is used for each run. This stops anyone
  guessing short values by hashing likely passwords.
* Placeholders are letters, digits and `_` only, so code that used them in a string, an identifier or a URL
  still looks like valid code.

## Keeping line numbers

With *Preserve line numbers* on (the default):

* Nothing that is removed contains a line break, except the matched value itself.
* A value that spans several lines is replaced **line by line**. Each line that had content gets the
  placeholder, keeping its indentation, and every line break is kept:

```text
-----BEGIN RSA PRIVATE KEY-----        -----BEGIN RSA PRIVATE KEY-----
MIIEowIBAAKCAQEAx3...            ->    VAILIT_PRIVATE_KEY_0001
k9Jh2...                               VAILIT_PRIVATE_KEY_0001
-----END RSA PRIVATE KEY-----          -----END RSA PRIVATE KEY-----
```

So a line number from an AI's answer, a compiler error or a code review points to the same line in the
original. When the option is off, a multi-line value becomes one placeholder and the file gets shorter.

Rules with *Replace = Line* replace the whole content of the line but keep the line itself. That is useful for
"remove any line that mentions CONFIDENTIAL".

## Restore

If *Write mapping file* is on, `Report\mapping.json` lists every placeholder with its original value.
**File > Restore original values** (or `vailit-codeclean restore`) puts the real values back into files, or into
pasted text such as code an AI wrote for you. Multi-line values that were replaced line by line are put back as
one block. The mapping file contains your secrets in plain text, so keep it private and delete it when you no
longer need it.

## What it does not do

* It does not look inside binary files (DLLs, images, Office documents, zip files). They are skipped, or copied
  unchanged if you choose that.
* It does not understand code. It matches patterns, so a secret that is put together at run time
  (`"Spr" + "ing2024"`) or is unusual enough not to match any rule gets through. Review the output and add rules
  for what you find.
* It does not change git history or the original files.
