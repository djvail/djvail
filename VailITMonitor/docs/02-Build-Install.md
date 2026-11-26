# 2. Build, install and remove

All commands are for **PowerShell**. Installing and removing need an **elevated** window
(right-click PowerShell → *Run as administrator*).

If Windows refuses to run the scripts ("running scripts is disabled on this system"), allow
them for the current window only:

```powershell
Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass
```

Files downloaded from the internet may also be "blocked". Unblock the whole folder once:

```powershell
Get-ChildItem -Recurse C:\Path\To\VailITMonitor | Unblock-File
```

---

## 2.1 What you need

| To... | You need |
|---|---|
| **Run** the .NET 10 edition (self-contained build) | Nothing. The runtime is inside the .exe. Windows 10 / Server 2016 or later, 64-bit. |
| **Run** the .NET 10 edition (framework-dependent build) | The .NET 10 Runtime: `winget install Microsoft.DotNet.Runtime.10` |
| **Run** the .NET Framework 4.8 edition | .NET Framework 4.8. It is built into Windows 10 1903+ / Server 2022+, and a free update for older versions. |
| **Build** either edition | The .NET 10 SDK: `winget install Microsoft.DotNet.SDK.10`. Visual Studio 2026, or Visual Studio 2022 17.14+ with the ".NET desktop development" workload, also works. |

You don't need the .NET Framework 4.8 Developer Pack to build. The project pulls in the
reference assemblies it needs from NuGet.

## 2.2 Build

### With the script (easiest)

From the `VailITMonitor` folder:

```powershell
.\scripts\Build-VailITMonitor.ps1                       # both editions
.\scripts\Build-VailITMonitor.ps1 -Edition Net10        # just one
.\scripts\Build-VailITMonitor.ps1 -Edition Net10 -FrameworkDependent   # smaller .NET 10 build, a folder of DLLs (needs the runtime on the server)
```

Output:

* `publish\Net10\` contains `VailITMonitor.exe` (one file, about 80 MB including the .NET runtime),
  `appsettings.json` and a few native DLLs (SQLite, the PDF and chart engines).
* `publish\Net48\` contains `VailITMonitor.exe`, `VailITMonitor.exe.config` and the library DLLs.

### With the dotnet command line (what the script runs)

```powershell
# .NET 10 - self-contained, single file
dotnet publish src\VailITMonitor.Net10\VailITMonitor.Net10.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:DebugType=none -o publish\Net10

# .NET Framework 4.8
dotnet build src\VailITMonitor.Net48\VailITMonitor.Net48.csproj -c Release -p:DebugType=none -o publish\Net48
```

### With Visual Studio

1. Open `VailITMonitor.sln`. Both projects are in it.
2. Pick **Release** in the toolbar and use **Build → Build Solution**.
   * .NET 10: for an installable folder, right-click the project → **Publish** → *Folder*. Then choose
     *Deployment mode: Self-contained*, *Target runtime: win-x64* and *Produce single file*.
   * .NET 4.8: the build output in `src\VailITMonitor.Net48\bin\Release\net48\` is the installable folder.
3. To debug, set the startup project and add `console` as the command-line argument
   (*Project → Properties → Debug → Command line arguments*), then press F5.

## 2.3 Install

```powershell
.\scripts\Install-VailITMonitor.ps1 -Edition Net10
# or
.\scripts\Install-VailITMonitor.ps1 -Edition Net48
```

What the script does:

1. Stops the service if it already exists (so the same command also **upgrades**).
2. Copies the program to `C:\Program Files\VailITMonitor` (Net10) or
   `C:\Program Files\VailITMonitor-Net48` (Net48).
   **An existing settings file is kept.** The new default is saved beside it as
   `appsettings.json.new` / `VailITMonitor.exe.config.new` so you can compare the two.
   Add `-OverwriteConfig` to replace it.
3. Registers the **VailITMonitor** Event Log source.
4. Creates the service:
   * Name `VailITMonitor`, display name *VailIT Monitor*
   * Startup: *Automatic (Delayed Start)*, which begins about 2 minutes after boot
   * Recovery: restart after 1 minute on each of the first three failures
   * Runs as *LocalSystem*
5. Starts it.

Options: `-SourcePath` (where the built files are), `-InstallPath`, `-ServiceName`, `-NoStart`.

### Doing it by hand (what the script does)

```powershell
New-Item -ItemType Directory "C:\Program Files\VailITMonitor" -Force
Copy-Item .\publish\Net10\* "C:\Program Files\VailITMonitor" -Recurse -Force
[System.Diagnostics.EventLog]::CreateEventSource('VailITMonitor', 'Application')
New-Service -Name VailITMonitor -BinaryPathName '"C:\Program Files\VailITMonitor\VailITMonitor.exe"' `
            -DisplayName 'VailIT Monitor' -StartupType Automatic
sc.exe config VailITMonitor start= delayed-auto
sc.exe failure VailITMonitor reset= 86400 actions= restart/60000/restart/60000/restart/60000
Start-Service VailITMonitor
```

The .NET Framework edition can also be installed the traditional way with **InstallUtil**,
because it contains a `ProjectInstaller` class:

```powershell
& "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\InstallUtil.exe" "C:\Program Files\VailITMonitor-Net48\VailITMonitor.exe"
```

(.NET 10 has no InstallUtil. Use the script, `New-Service` or `sc.exe create`.)

## 2.4 Check it is working

```powershell
Get-Service VailITMonitor                                     # Status should be Running

& "C:\Program Files\VailITMonitor\VailITMonitor.exe" sample   # prints one live reading of everything

Get-EventLog -LogName Application -Source VailITMonitor -Newest 20 | Format-Table TimeGenerated, EntryType, Message -Wrap
```

After a minute or two the database appears at `C:\ProgramData\VailITMonitor\VailITMonitor.db`
(or `...\VailITMonitor-Net48\...`). The first daily report appears shortly after the first
midnight, in `C:\ProgramData\VailITMonitor\Reports\Daily`.

You don't have to wait to see reports:

```powershell
$exe = "C:\Program Files\VailITMonitor\VailITMonitor.exe"
& $exe report daily today        # today so far, from the real data
& $exe demo C:\Temp\VailITDemo   # a demo database with ~2 years of fake data, plus one report of each type
```

## 2.5 Command-line reference

The same commands work in both editions:

| Command | What it does |
|---|---|
| *(none)* | Runs as a service when started by Windows, or in the console when started by hand |
| `console` | Runs in the console window until you press Ctrl+C. Useful for testing and debugging. |
| `sample` | Takes one reading and prints it |
| `maintenance` | Runs one maintenance pass now (rollups → reports → cleanup) |
| `cleanup` | Runs every cleanup now, ignoring `CleanupEveryDays` |
| `report <daily\|weekly\|monthly\|quarterly\|yearly> [date\|today]` | Creates (or re-creates) a report for the period containing `date`. Without a date: the last finished period. |
| `demo [folder]` | Creates a separate demo database with fake data and one report of each type |

Any setting can be overridden for a single run:

```powershell
# .NET 10: section:key=value
VailITMonitor.exe console --Monitor:SampleIntervalSeconds=5
# .NET Framework: key=value, as in appSettings
VailITMonitor.exe console --SampleIntervalSeconds=5
```

> When you run commands by hand while the service is running, both use the same database.
> That is fine: SQLite handles it. But don't run `console` at the same time as the service,
> or you'll record every reading twice. Stop the service first.

## 2.6 Changing settings

1. Edit `C:\Program Files\VailITMonitor\appsettings.json` (or `VailITMonitor.exe.config`) in Notepad.
   See [03 Configuration](03-Configuration.md).
2. `Restart-Service VailITMonitor`

## 2.7 Upgrade

Build the new version, then run the install script again. It stops the service, replaces the
program files, keeps your settings and data, and starts the service again.

## 2.8 Uninstall

```powershell
.\scripts\Uninstall-VailITMonitor.ps1                           # remove the service, keep files + data
.\scripts\Uninstall-VailITMonitor.ps1 -RemoveFiles              # ...and delete the program folder
.\scripts\Uninstall-VailITMonitor.ps1 -RemoveFiles -RemoveData  # ...and the database and reports
```

By hand: `Stop-Service VailITMonitor; sc.exe delete VailITMonitor`, then delete the folders.

## 2.9 Running both editions side by side (to compare them)

The editions keep their data in different folders by default, so they don't conflict. Only the
service name needs to differ:

```powershell
.\scripts\Install-VailITMonitor.ps1 -Edition Net10                              # service "VailITMonitor"
.\scripts\Install-VailITMonitor.ps1 -Edition Net48 -ServiceName VailITMonitor48 # service "VailITMonitor48"
```

Compare them in Task Manager → *Details* (both appear as `VailITMonitor.exe`; add the *Command line*
column to tell them apart). You can also compare the "Monitor's own memory" tile in each edition's
daily report.

To remove the second one: `.\scripts\Uninstall-VailITMonitor.ps1 -ServiceName VailITMonitor48`

## 2.10 Running under a less-privileged account (optional)

By default the service runs as *LocalSystem*, which can read every counter and write to
`C:\ProgramData`. To use *Local Service* instead:

```powershell
$data = "$env:ProgramData\VailITMonitor"
New-Item -ItemType Directory $data -Force | Out-Null
icacls $data /grant "NT AUTHORITY\LOCAL SERVICE:(OI)(CI)M"
net localgroup "Performance Monitor Users" "NT AUTHORITY\LOCAL SERVICE" /add
sc.exe config VailITMonitor obj= "NT AUTHORITY\LocalService" password= ""
Restart-Service VailITMonitor
```

## 2.11 Troubleshooting

| Symptom | Fix |
|---|---|
| Service starts then stops | Check the Event Log (2.4). Then run `VailITMonitor.exe console` in an admin window: errors are printed there. |
| "Performance counter ... is unavailable" warning | Performance counters are damaged. Fix them in an admin prompt with `lodctr /R`, then restart the service. Disk I/O and CPU queue are skipped until then; everything else keeps working. |
| No reports appear | Reports are made for *finished* periods only. Run `VailITMonitor.exe report daily today` to test. Check `Reports.Enabled` and the Event Log. |
| `.NET Framework 4.8 edition` won't start: "BadImageFormatException" | Use 64-bit Windows. The build is x64 only. |
| `appsettings.json` change ignored | Restart the service. Check the JSON is valid: a missing comma makes the service fail at start, with the reason in the Event Log. |
| Want to start over | Stop the service, delete `C:\ProgramData\VailITMonitor\VailITMonitor.db*`, start it again. |
| SmartScreen / "unknown publisher" warning | The .exe is not code-signed. Use *More info → Run anyway*, or sign it with your own certificate (`signtool sign`). |
