# Samples

| File | What it is |
|---|---|
| `Default.profile.json` | The built-in profile (all 178 rules, default options, file filters, allow list) as JSON. Open it with *File > Open profile* or pass it to the command line with `--profile`. Regenerate with `vailit-codeclean export-profile samples/Default.profile.json`. |
| `My-Company.rules.json` | A rule pack template: company names, domains, server naming scheme, project code names, id formats, ticket keys, and "remove lines marked CONFIDENTIAL". Edit the patterns, then *File > Import rules...*. |

To get a test project full of fake secrets, use *File > Create sample project...* in the app or
`vailit-codeclean make-sample <folder>`.
