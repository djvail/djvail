# Editing the code

The solution is plain C# 14 on .NET 10, in four projects:

```text
VailITCodeClean.sln
├─ src/VailITCodeClean.Core        class library, no UI, runs on Windows/Linux/macOS
│   ├─ Configuration/              the settings model, saved as JSON
│   │   ├─ CleanProfile.cs           options + rules + allow list (one *.profile.json file)
│   │   ├─ ScanOptions.cs            every option on the Scan and Files tabs
│   │   ├─ FilterRule.cs             one rule (all fields documented)
│   │   ├─ Enums.cs                  PatternType, Severity, ReplaceScope, ValueValidator, ObfuscationStyle
│   │   └─ ProfileStore.cs           load / save / import / merge
│   ├─ Rules/                      THE SAMPLE RULES, one file per category
│   │   ├─ DefaultRules.cs           helpers (QuotedAssignment, LineAssignment, .Cs(), .Off()...) and shared lists
│   │   ├─ DefaultRules.Credentials.cs / .KeysAndCertificates.cs / .CloudProviders.cs / .ServiceTokens.cs
│   │   ├─ DefaultRules.Infrastructure.cs / .Endpoints.cs / .PersonalData.cs / .Custom.cs
│   │   └─ DefaultProfile.cs         default options, file filters, sensitive file types, allow list
│   ├─ Engine/
│   │   ├─ CleanRunner.cs            a whole run: list files, scan in parallel, write in order, report
│   │   ├─ Detector.cs               runs the rules over text, applies filters, merges overlapping matches
│   │   ├─ CompiledRule.cs           turns a FilterRule into Regex objects, value checks
│   │   ├─ Scrubber.cs               writes placeholders (line-by-line for multi-line values)
│   │   ├─ TokenProvider.cs          placeholder names, consistent per value, {ID}/{HASH}
│   │   ├─ TextFileCodec.cs          reads/writes files keeping encoding, BOM and line endings
│   │   ├─ Restorer.cs               mapping.json -> original values
│   │   ├─ GlobMatcher.cs            *.config / src/**/x.json style patterns
│   │   ├─ LineIndex.cs, ValueChecks.cs, ExampleText.cs
│   ├─ Reporting/
│   │   ├─ RunResult.cs              results model (also findings.json)
│   │   ├─ ReportWriter.cs           CSV / JSON / mapping
│   │   └─ HtmlReport.cs             the single-file HTML report
│   └─ Samples/SampleProject.cs    the fake "dirty" project
├─ src/VailITCodeClean.App         WinForms app (no designer files: every control is created in code)
│   ├─ MainForm.cs                   window, menu, loading/saving profiles
│   ├─ MainForm.ScanTab.cs / RulesTab / TestTab / FilesTab / ResultsTab
│   ├─ RuleEditorForm.cs             add/edit rule dialog with live test
│   ├─ RestoreForm.cs                restore dialog
│   ├─ Ui.cs                         layout helpers (labels, text boxes, grids, colours)
│   └─ AppSettings.cs                last profile + window size
├─ src/VailITCodeClean.Cli         command-line tool (Program.cs)
└─ tests/VailITCodeClean.Tests     xUnit tests (RuleTests.cs, EngineTests.cs)
```

## Common changes

| I want to... | Change |
|---|---|
| Change or add a built-in rule | `Core/Rules/DefaultRules.<Category>.cs`. Then run `dotnet test`. Existing users get new rules with *File > Add missing built-in rules*. |
| Change the default excluded folders / sensitive file types / allow list | `Core/Rules/DefaultProfile.cs` |
| Change the default options (prefix, template, max size...) | Defaults in `Core/Configuration/ScanOptions.cs` |
| Add a new option | Add a property to `ScanOptions`, use it in `CleanRunner`/`Scrubber`, and add a control in `MainForm.ScanTab.cs` (`ShowScanOptions` and `ReadScanOptions`). The JSON file picks it up automatically. |
| Add a new rule field | Add a property to `FilterRule` (and to `Clone()` if it is a list), use it in `CompiledRule`/`Detector`, and add a control in `RuleEditorForm` (`LoadRule` and `ReadRule`). |
| Change how placeholders look | `TokenProvider.MakeToken` |
| Change the report | `Reporting/HtmlReport.cs` (HTML is built with raw string literals, CSS at the top) |
| Add a new validator (e.g. IBAN checksum) | Add to `ValueValidator`, implement it in `ValueChecks`, check it in `CompiledRule.AcceptsValue` |
| Add a new placeholder style | Add to `ObfuscationStyle`, handle it in `Scrubber.Apply` / `TokenProvider.GetToken` |
| Change the layout of a tab | That tab's `MainForm.*Tab.cs`. Rows are added top to bottom with `grid.AddRow(...)`. |

## Design notes

* **The engine has no UI code**, so the app, the command line and the tests always behave the same way.
* **Two passes.** Scanning is parallel. Writing is sequential in alphabetical order, so placeholder numbers
  are repeatable.
* **Overlaps are merged, not dropped.** If two rules overlap, the replacement covers both. This errs on the side
  of hiding a little more.
* **Files are copied byte for byte** when nothing is found. Files with findings are decoded (BOM, then strict
  UTF-8, then Latin-1, which round-trips every byte), changed only where matched, and encoded back the same way.
* **Regex timeouts** (`RegexTimeoutMs`) stop one runaway pattern from hanging a run.
* **Examples use `{{ALNUM:36}}`-style placeholders** (`ExampleText.Expand`), so the repository never contains
  anything that looks like a live key and does not trigger GitHub secret scanning.
