# Build and run

## Requirements

* Windows 10 or 11 (or Windows Server 2019+) to run the app. The engine and the command-line tool also run on
  Linux and macOS.
* The **.NET 10 SDK** to build: `winget install Microsoft.DotNet.SDK.10`. Visual Studio 2022 17.14+ or Visual
  Studio 2026 includes it.

## Build

```powershell
.\scripts\Build-VailITCodeClean.ps1                 # runs the tests, then builds both programs
.\scripts\Build-VailITCodeClean.ps1 -SkipTests
.\scripts\Build-VailITCodeClean.ps1 -FrameworkDependent   # small .exe files that need the .NET 10 Desktop Runtime installed
```

| Output | What it is |
|---|---|
| `publish\App\VailITCodeClean.exe` | The Windows app: one self-contained file, nothing to install |
| `publish\Cli\vailit-codeclean.exe` | The command-line tool |
| `publish\Default.profile.json` | All built-in rules as a profile you can edit or share |

From Visual Studio, open `VailITCodeClean.sln`, right-click **VailITCodeClean.App** > *Set as Startup Project*,
and press F5. From a terminal:

```powershell
dotnet run --project src\VailITCodeClean.App
dotnet test
```

## Where settings are kept

| File | Contents |
|---|---|
| `%APPDATA%\VailIT\CodeClean\Default.profile.json` | Created on the first start: your folders, options, rules and allow list. Saved when you press Ctrl+S, start a run, or close the app. |
| `%APPDATA%\VailIT\CodeClean\app-settings.json` | Which profile was used last, and the window size |
| Any `*.profile.json` | Profiles you save with *File > Save profile as*. You can share them with your team or keep them in source control. |

*Help > Open settings folder* opens that folder.

## Command line

```text
vailit-codeclean scan --source C:\Code\Sales --output D:\Clean [--profile team.profile.json]
                      [--scan-only] [--style token|mask|fixed] [--no-preserve-lines]
                      [--scrub-names] [--mapping] [--fixed-folder] [--fail-on High] [--quiet]
vailit-codeclean restore --mapping D:\Clean\Sales_...\Report\mapping.json --input answer.cs --output answer.restored.cs
vailit-codeclean test --file appsettings.json          # show what the rules find in one file, and the scrubbed result
vailit-codeclean test --text "Password=Spring2024!"
vailit-codeclean list-rules [--all] [--profile x]
vailit-codeclean export-profile my.profile.json        # write the built-in profile for editing
vailit-codeclean make-sample C:\Temp\DirtySample        # create the fake test project
```

Exit codes: `0` = OK, `1` = a finding at or above the `--fail-on` severity, `2` = an error or a cancelled run.

### In a CI pipeline

Use the scan as a gate that fails the build when something serious is committed:

```yaml
# Azure DevOps / GitHub Actions step (Windows or Linux agent with the .NET 10 SDK)
- run: dotnet run --project VailITCodeClean/src/VailITCodeClean.Cli -- scan --source . --output $(Agent.TempDirectory)/codeclean --scan-only --fail-on High --quiet
```

The HTML report in the output folder shows what was found.
