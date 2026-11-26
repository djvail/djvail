# 5. .NET Framework vs modern .NET: a guided comparison

This guide is for someone who knows **.NET Framework** well and is learning **modern .NET**.
VailITMonitor is written twice with identical behaviour, identical class names and the same
database, so every difference you see is a difference between the platforms, not between programs.

**How to use it:** open `VailITMonitor.sln` in Visual Studio, then open the two files named in each
section side by side (right-click a tab → *New Vertical Document Group*). Each section gives
the "Framework way" (what you already know), the "modern way" and why it changed. Work through the
sections in order, then try the exercises at the end.

---

## Part A: The platform

### A1. Names and versions

| | .NET Framework | Modern .NET |
|---|---|---|
| Versions | 1.0 (2002) → **4.8.1** (2022, the last one) | .NET Core 1.0 (2016) → 3.1, then .NET 5, 6, 7, 8, 9, **10** (Nov 2025), 11 (Nov 2026) |
| Where it runs | Windows only | Windows, Linux, macOS |
| Installed | Built into Windows, updated by Windows Update | Bundled with your app (self-contained), or installed side by side, any number of versions |
| New features | None: security and reliability fixes only | A new version every November |
| Support | As long as the Windows version it ships with | Even versions are **LTS** (3 years), odd versions **STS** (2 years). .NET 10 LTS is supported to **Nov 2028**. |
| Open source | Reference source only | Fully open source (github.com/dotnet) |

The key idea: **.NET Framework is finished, not abandoned.** It will run your existing apps for
many years, but every new language feature, library and performance improvement goes into modern .NET.
"Modern .NET" is simply called ".NET" now. The "Core" name was dropped at version 5.

### A2. The project file

📂 Compare `src/VailITMonitor.Net48/VailITMonitor.Net48.csproj` with `src/VailITMonitor.Net10/VailITMonitor.Net10.csproj`.

Both use the **SDK-style** format, which came with .NET Core and also works for .NET Framework
projects. The old Framework format looked like this:

```xml
<!-- Old-style .csproj (what VS 2017 and earlier created for .NET Framework) -->
<Project ToolsVersion="15.0" xmlns="http://schemas.microsoft.com/developer/msbuild/2003">
  <Import Project="$(MSBuildExtensionsPath)\$(MSBuildToolsVersion)\Microsoft.Common.props" />
  <PropertyGroup>
    <ProjectGuid>{8F1A...}</ProjectGuid>
    <TargetFrameworkVersion>v4.8</TargetFrameworkVersion>
    ...
  </PropertyGroup>
  <ItemGroup>
    <Reference Include="QuestPDF, Version=2026.9.2.0, ...">
      <HintPath>..\packages\QuestPDF.2026.9.2\lib\netstandard2.0\QuestPDF.dll</HintPath>
    </Reference>
    ...
  </ItemGroup>
  <ItemGroup>
    <Compile Include="Program.cs" />
    <Compile Include="Collection\MetricCollector.cs" />
    <Compile Include="Collection\Metrics.cs" />
    ... one line for every .cs file ...
  </ItemGroup>
  <ItemGroup>
    <None Include="packages.config" />
  </ItemGroup>
  <Import Project="$(MSBuildToolsPath)\Microsoft.CSharp.targets" />
</Project>
```

| | Old style | SDK style |
|---|---|---|
| Source files | Listed one by one (merge conflicts!) | Every `.cs` in the folder is included automatically |
| NuGet packages | `packages.config` + `<HintPath>` to a `packages` folder | `<PackageReference Include="..." Version="..." />` in the project file; restored to a shared cache |
| Transitive dependencies | All listed in packages.config | Resolved automatically |
| Binding redirects | Hand-edited in app.config | Generated (`AutoGenerateBindingRedirects`). Modern .NET doesn't need them at all. |
| AssemblyInfo.cs | Separate file | `<Version>`, `<Product>` etc. in the project |
| Build from the command line | `msbuild` (needs VS Build Tools) | `dotnet build`, on any OS |

**What's only in the .NET 10 file:**

```xml
<TargetFramework>net10.0-windows</TargetFramework>  <!-- "-windows" unlocks Windows-only APIs -->
<Nullable>enable</Nullable>                          <!-- null-safety checking, see C3 -->
<ImplicitUsings>enable</ImplicitUsings>              <!-- no more 'using System;' at the top of each file -->
<Project Sdk="Microsoft.NET.Sdk.Worker">             <!-- the SDK flavour for services/background apps -->
```

**What's only in the 4.8 file:**

```xml
<LangVersion>7.3</LangVersion>   <!-- the C# version .NET Framework projects default to -->
<Reference Include="System.ServiceProcess" />   <!-- parts of the Framework in the GAC -->
<PackageReference Include="Microsoft.NETFramework.ReferenceAssemblies" ... />  <!-- build without the Developer Pack -->
```

> 💡 You can convert your existing Framework projects to SDK style without changing the target.
> It's often the first step of a migration, and it makes the project files about 80% shorter.

### A3. Building and deploying

| | .NET Framework 4.8 | .NET 10 |
|---|---|---|
| Output | `VailITMonitor.exe` + library DLLs. Needs the Framework on the machine (it is). | **Framework-dependent:** like 4.8, but needs the .NET 10 runtime installed.<br>**Self-contained:** the runtime is copied in; runs on a clean Windows.<br>**Single-file:** all of that inside one `.exe`. |
| Our build size | about 61 MB (mostly the PDF/chart engines) | about 106 MB self-contained single-file (the runtime adds ~70 MB); about 36 MB framework-dependent |
| Choosing the CPU | `PlatformTarget` x64/x86/AnyCPU + `Prefer32Bit` | A **Runtime Identifier** (RID): `win-x64`, `win-arm64`, `linux-x64`, ... |
| Updating the runtime | Windows Update does it for you | Self-contained: **you** rebuild and redeploy to get runtime security fixes (monthly "Patch Tuesday" releases). Framework-dependent: Windows/Microsoft Update can update the shared runtime. |
| Extra options | – | Trimming (removes unused code), ReadyToRun (faster start-up), **Native AOT** (compiles to a native .exe with no JIT; tiny and fast, but limits reflection-heavy libraries) |

This project chose self-contained single-file for "copy it and it runs". The build script's
`-FrameworkDependent` switch produces the small version instead.

---

## Part B: The application model

### B1. The entry point

📂 `Net48/Program.cs` vs `Net10/Program.cs`

```csharp
// .NET Framework
namespace VailITMonitor
{
    internal static class Program
    {
        private static int Main(string[] args)
        {
            ...
        }
    }
}
```

```csharp
// .NET 10 - "top-level statements" (C# 9): the file IS the Main method
string command = args.Length > 0 ? args[0] : "run";
var builder = Host.CreateApplicationBuilder(...);
...
await host.RunAsync();
```

The compiler generates the `Program` class and `Main` for you; `args` is available automatically.
`await` works directly, because the generated Main is `async Task<int>`. It's just less ceremony. You can still
write a classic `Main` in .NET 10 if you prefer.

### B2. The Windows service

📂 `Net48/Service/MonitorService.cs` vs `Net10/Workers/SamplingWorker.cs` + `MaintenanceWorker.cs`

**Framework:** you inherit from `ServiceBase` and override `OnStart`/`OnStop`. `OnStart` must
return within about 30 seconds, so you start timers or threads and return. In `Main` you call
`ServiceBase.Run(...)`. To debug you need a trick, such as our `console` mode
(`StartInConsole()`), because a ServiceBase can't simply run from Visual Studio.

```csharp
public sealed class MonitorService : ServiceBase
{
    protected override void OnStart(string[] args)
    {
        _samplingTimer = new Timer(OnSamplingTick, null, firstDelay, interval);
        _maintenanceTimer = new Timer(OnMaintenanceTick, null, TimeSpan.FromMinutes(1), every);
    }

    protected override void OnStop()
    {
        _samplingTimer.Dispose();
        _maintenanceTimer.Dispose();
    }
}
```

**.NET 10:** the **Generic Host** runs your app, the same host ASP.NET Core web apps use. Each
piece of background work is a class deriving from `BackgroundService` with one method,
`ExecuteAsync(CancellationToken)`. The host starts it, and when Windows asks the service to stop,
the host cancels the token. `AddWindowsService()` connects the host to the Windows Service Control Manager.
The same .exe runs as a console app when you start it by hand, so F5 debugging just works.

```csharp
public sealed class SamplingWorker(...) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(interval);
        do { TakeReading(); }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}

// Program.cs
builder.Services.AddWindowsService(o => o.ServiceName = "VailITMonitor");
builder.Services.AddHostedService<SamplingWorker>();
builder.Services.AddHostedService<MaintenanceWorker>();
```

**Why it's better:** one small class per job instead of one class juggling several timers;
clean cancellation; no special console/debug mode; and the same pattern works on Linux (systemd)
and in containers.

**Installation:** Framework services traditionally use an `Installer` class + `InstallUtil.exe`
(see `Net48/Service/ProjectInstaller.cs`). Modern .NET has no InstallUtil. You use
`sc.exe create` or PowerShell's `New-Service`, which also work for Framework services. That's
why our install script handles both editions the same way.

### B3. Timers and async

📂 `MonitorService.OnMaintenanceTick` vs `MaintenanceWorker.ExecuteAsync`

| | Framework (`System.Threading.Timer`) | .NET 10 (`PeriodicTimer`, .NET 6+) |
|---|---|---|
| Model | Callback on a thread-pool thread | `await` in a loop |
| Overlap | Callbacks **can overlap** if one runs long, so we guard with `Interlocked.Exchange` | Never overlaps: the next tick is awaited only after the work finishes |
| Stopping | Dispose the timer, and hope no callback is mid-flight | Cancel the token; the loop ends |
| Errors | An unhandled exception in a callback crashes the process | Caught around the work in the loop |

```csharp
// Framework: guard against overlapping callbacks
if (Interlocked.Exchange(ref _maintenanceRunning, 1) == 1) return;
try { _maintenance.RunOnce(DateTime.Now); }
finally { Interlocked.Exchange(ref _maintenanceRunning, 0); }
```

```csharp
// .NET 10: overlap is impossible by construction
using var timer = new PeriodicTimer(TimeSpan.FromMinutes(15));
do { await Task.Run(() => maintenance.RunOnce(DateTime.Now), stoppingToken); }
while (await timer.WaitForNextTickAsync(stoppingToken));
```

### B4. Dependency injection (DI)

📂 `Net48/Program.cs` (top of `Main`) vs `Net10/Program.cs` (`builder.Services...`)

**Framework, the "poor man's DI":** you create every object yourself, in the right order, and pass
it to the next.

```csharp
var store = new MetricStore(settings.DatabasePath);
var rollups = new RollupService(store, settings);
var reports = new ReportGenerator(store, rollups, settings);
var cleanup = new CleanupService(store, settings);
var maintenance = new MaintenanceService(rollups, reports, cleanup, store, settings);
```

**.NET 10:** you register the classes once. The container reads each constructor's parameters
and supplies them.

```csharp
builder.Services.AddSingleton<MetricStore>();
builder.Services.AddSingleton<RollupService>();
builder.Services.AddSingleton<ReportGenerator>();
...
var maintenance = services.GetRequiredService<MaintenanceService>();   // built with all its dependencies
```

Add a parameter to a constructor and it's supplied automatically, with no wiring code to update.
DI is built into modern .NET (`Microsoft.Extensions.DependencyInjection`) and used everywhere:
ASP.NET Core, workers, MAUI, and more. For a project this size the manual version is perfectly fine.
The container pays off as an app grows, and for testing, where you can register fakes.

### B5. Configuration

📂 `Net48/App.config` + `Configuration/MonitorSettings.cs` vs `Net10/appsettings.json` + `Configuration/MonitorOptions.cs`

**Framework:** `ConfigurationManager.AppSettings["key"]` returns a string; you convert it
yourself. Nested settings mean either dotted key names (what we did) or writing a custom
`ConfigurationSection` class. Our `MonitorSettings.Load()` is about 100 lines of
"read string, parse, fall back to the default".

```csharp
SampleIntervalSeconds = s.Int("SampleIntervalSeconds", 60),
Retention = new RetentionSettings { Raw = s.Tier("Retention.Raw", 30, 1), ... },
```

**.NET 10:** configuration is a layered set of **providers** merged into one tree:
`appsettings.json` → `appsettings.{Environment}.json` → environment variables → command line
(later ones win). One line binds a whole section, nested classes and arrays included, onto your class:

```csharp
builder.Services.Configure<MonitorOptions>(builder.Configuration.GetSection("Monitor"));

// any class can then ask for it:
public RollupService(MetricStore store, IOptions<MonitorOptions> options, ...)
```

Things you get for free in .NET 10 that took custom code in the Framework edition:

| Feature | .NET 10 | Framework edition |
|---|---|---|
| Nested settings and arrays | `"Drives": ["C:", "D:"]` | `"C:,D:"` split by hand |
| Type conversion (int, bool, enum, TimeSpan...) | automatic | `int.TryParse` etc. by hand |
| Command-line override | `--Monitor:SampleIntervalSeconds=10` | We wrote it in `MonitorSettings.Load` |
| Environment variable override | `Monitor__SampleIntervalSeconds=10` (double underscore = `:`) | not available |
| Comments in the file | `//` in JSON | `<!-- -->` in XML |
| Validation | `.ValidateDataAnnotations()`, `.ValidateOnStart()` | by hand |
| Reload on change | `IOptionsMonitor<T>` (we restart instead, for simplicity) | not available |

### B6. Logging

📂 `Net48/Log.cs` vs every `ILogger<...>` in `Net10`

**Framework:** no built-in abstraction. We wrote a static `Log` class that writes to the Event
Log and the console. log4net, NLog and Serilog filled this gap for years.

**.NET 10:** `ILogger<T>` is built in. Each class asks for a logger, and where messages go
(console, Event Log, debug window, files via Serilog/NLog, Application Insights, ...) is decided
in one place by **providers** and filtered by the `Logging` section of `appsettings.json`.

```csharp
log.LogInformation("Created {Period} summary for {Start:yyyy-MM-dd}", period, start);
```

Note the `{Period}` placeholders. This is **structured logging**: the values are kept as named
fields, not just glued into a string, so log tools can search "all messages where Period = weekly".
Don't write `$"Created {period}..."` here. It works, but you lose the structure.

---

## Part C: The C# language (7.3 → 14)

.NET Framework projects default to **C# 7.3** (2018). .NET 10 uses **C# 14** (2025). Most newer
features are compiler-only syntax sugar. A few need runtime support that only modern .NET has.

📂 Most examples come from `Metrics.cs`, `MetricStore.cs`, `ReportGenerator.cs`, `Periods.cs` and `RollupService.cs`.

### C1. Features used in this project

| Feature (C# version) | Framework edition (C# 7.3) | .NET 10 edition | Works on 4.8 if you raise `LangVersion`? |
|---|---|---|---|
| **Records** (9) / **record struct** (10) | `MetricKey`: 39 lines with `Equals`, `GetHashCode`, `==`, `!=` | `public readonly record struct MetricKey(string Metric, string Instance);` | ✅ with a small polyfill (`IsExternalInit`) |
| **Primary constructors** (12) | constructor + `private readonly` fields | `public sealed class RollupService(MetricStore store, ...)` | ✅ |
| **Switch expressions** (8) | `switch` statement with `case ...: return ...;` | `period switch { PeriodType.Day => Daily, ... }` | ✅ |
| **Pattern matching** (8–9) | `value is long ? (long?)value : null` | `is long ts ? ts : null`, `is not null`, `is { } cpu`, `is "Metric" or "Unit"` | ✅ |
| **`using` declarations** (8) | `using (var db = Open()) { ... }` nesting | `using var db = Open();` (disposed at end of scope) | ✅ |
| **File-scoped namespaces** (10) | `namespace X { ... }` + one extra indent | `namespace X;` | ✅ |
| **Global usings** (10) | `using` lines in every file | `GlobalUsings.cs` + `ImplicitUsings` | ✅ |
| **Raw string literals** (11) | `@"..."` verbatim strings | `"""` ... `"""` (great for SQL) | ✅ |
| **Collection expressions** (12) | `new[] { ... }`, `new List<T>()` | `[PeriodType.Day, PeriodType.Week]`, `[]` | ✅ for arrays/lists |
| **Target-typed `new`** (9) | `new MetricInfo(...)` | `new() { KeepDays = 30 }` | ✅ |
| **Nullable reference types** (8) | everything may be null, unchecked | `string?` vs `string`; the compiler warns on possible null use | ⚠️ partly (attributes missing) |
| **`required` / `init`** (11/9) | constructor parameters | `public required DateTime Start { get; init; }` | ✅ with polyfill |
| **Index from end** `^1` (8) | `sorted[sorted.Length - 1]` | `sorted[^1]` | ❌ needs `System.Index` (polyfill packages exist) |
| **Tuples** (7.0) | `Tuple.Create(...)`, `.Item1` | `(string Label, string Value, string Note)` named tuples + deconstruction | (7.0+, works on 4.8 already) |
| **Local functions** (7.0) | used in both | used in both | – |
| **`System.Threading.Lock`** (13, .NET 9) | `lock (new object())` | `private readonly Lock _gate = new();` | ❌ runtime type |
| **Default interface methods** (8) | – | (not used) | ❌ needs runtime support |

> 🧪 **Try it:** in `VailITMonitor.Net48.csproj` change `<LangVersion>7.3</LangVersion>` to
> `<LangVersion>latest</LangVersion>` and rewrite one switch statement in `Periods.cs` as a switch
> expression. It compiles and runs on .NET Framework 4.8. This is the gentlest way to start
> using modern C# in existing Framework code.

### C2. The same code, side by side

**A value type used as a dictionary key:**

```csharp
// C# 7.3 - Metrics.cs (Net48)
public struct MetricKey : IEquatable<MetricKey>
{
    public MetricKey(string metric, string instance) { Metric = metric; Instance = instance ?? ""; }
    public string Metric { get; }
    public string Instance { get; }
    public bool Equals(MetricKey other) => ...;
    public override bool Equals(object obj) => ...;
    public override int GetHashCode() { unchecked { ... } }
    public static bool operator ==(MetricKey a, MetricKey b) => ...;
    public static bool operator !=(MetricKey a, MetricKey b) => ...;
}
```

```csharp
// C# 14 - Metrics.cs (Net10)
public readonly record struct MetricKey(string Metric, string Instance);
```

**Choosing a value:**

```csharp
// C# 7.3 - Periods.cs (Net48)
switch (type)
{
    case PeriodType.Day: return start.AddDays(1);
    case PeriodType.Week: return start.AddDays(7);
    case PeriodType.Month: return start.AddMonths(1);
    ...
    default: throw new ArgumentOutOfRangeException("type");
}
```

```csharp
// C# 14 - Periods.cs (Net10)
public static DateTime Next(PeriodType type, DateTime start) => type switch
{
    PeriodType.Day => start.AddDays(1),
    PeriodType.Week => start.AddDays(7),
    PeriodType.Month => start.AddMonths(1),
    ...
    _ => throw new ArgumentOutOfRangeException(nameof(type)),
};
```

**Database code:**

```csharp
// C# 7.3 - MetricStore.cs (Net48)
using (var db = Open())
using (var cmd = db.CreateCommand())
{
    cmd.CommandText = "SELECT MIN(ts) FROM samples";
    var value = cmd.ExecuteScalar();
    return value is long ? (long?)value : null;
}
```

```csharp
// C# 14 - MetricStore.cs (Net10)
using var db = Open();
using var cmd = db.CreateCommand();
cmd.CommandText = "SELECT MIN(ts) FROM samples";
return cmd.ExecuteScalar() is long ts ? ts : null;
```

**Dependencies into a class:**

```csharp
// C# 7.3 - RollupService.cs (Net48)
public sealed class RollupService
{
    private readonly MetricStore _store;
    private readonly MonitorSettings _options;

    public RollupService(MetricStore store, MonitorSettings options)
    {
        _store = store;
        _options = options;
    }
```

```csharp
// C# 14 - RollupService.cs (Net10)
public sealed class RollupService(MetricStore store, IOptions<MonitorOptions> options, ILogger<RollupService> log)
{
```

### C3. Nullable reference types: the biggest mindset change

In the .NET 10 project `<Nullable>enable</Nullable>` is on. Now `string` means "never null" and
`string?` means "might be null", and the compiler **warns** wherever you might dereference a null.
Look at `ReportGenerator.Generate`, which returns `string?` because there may be no data, and at the callers,
which must check. In the Framework edition the same method returns `string`, and nothing reminds
you to check. Null-reference exceptions are the most common .NET bug, and this catches most of
them at compile time.

---

## Part D: Libraries and APIs

### D1. Calling Windows (P/Invoke)

📂 `Collection/NativeMethods.cs` in both

```csharp
// Framework: [DllImport] - marshalling code generated at RUN time (reflection)
[DllImport("kernel32.dll", SetLastError = true)]
internal static extern bool GetSystemTimes(out long idleTime, out long kernelTime, out long userTime);

// .NET 7+: [LibraryImport] - marshalling code generated at COMPILE time (source generator)
[LibraryImport("kernel32.dll", SetLastError = true)]
[return: MarshalAs(UnmanagedType.Bool)]
internal static partial bool GetSystemTimes(out long idleTime, out long kernelTime, out long userTime);
```

`LibraryImport` is faster to start and compatible with trimming/Native AOT. Note `partial`
instead of `extern`: the generator writes the method body. `DllImport` still works in .NET 10.

### D2. Small API differences you'll hit

| Need | .NET Framework 4.8 | .NET 10 |
|---|---|---|
| Milliseconds since boot | `Environment.TickCount` wraps after 49.7 days, so we P/Invoke `GetTickCount64` | `Environment.TickCount64` |
| Clamp a number | write it yourself (`Stats.ClampPercent`) | `Math.Clamp(v, 0, 100)` |
| Case-insensitive contains | `text.IndexOf(part, StringComparison.OrdinalIgnoreCase) >= 0` | `text.Contains(part, StringComparison.OrdinalIgnoreCase)` |
| Sort / min-by / max-by in LINQ | `OrderBy(v => v)`, `OrderBy(...).FirstOrDefault()` | `Order()`, `MinBy(...)`, `MaxBy(...)` |
| Dictionary lookup with default | `TryGetValue` + `if` | `GetValueOrDefault(key)` |
| Elapsed time | `Stopwatch` instance | `Stopwatch.GetElapsedTime(start)` (no allocation) |
| Performance counters | built into `System.dll` | NuGet package `System.Diagnostics.PerformanceCounter` (Windows-only) |
| `app.config` / `ConfigurationManager` | built in | NuGet package `System.Configuration.ConfigurationManager` (for porting only; use `IConfiguration` instead) |
| Is this Windows? | `Environment.OSVersion.Platform` | `OperatingSystem.IsWindows()` |
| Bigger GC knobs | `<gcServer>`, `<gcConcurrent>` in app.config | csproj properties + `System.GC.ConserveMemory`, DATAS, region-based GC |

### D3. Libraries that work on both

Microsoft.Data.Sqlite, QuestPDF and ScottPlot all target **.NET Standard 2.0** (or 4.6.2), the
"common subset" that both .NET Framework 4.6.1+ and modern .NET implement. So one NuGet package
serves both editions. Most of the NuGet ecosystem does this. When you plan a migration, check
your dependencies with `dotnet list package` and look for `netstandard2.0` or `net8.0`+ targets.

---

## Part E: Results and how to choose

### E1. This project in numbers

| | .NET Framework 4.8 | .NET 10 |
|---|---|---|
| C# lines (all files) | 2,687 | 2,131 (**−21%**) |
| `MetricKey` type | 39 lines | 1 line |
| Settings code | 261 lines (`MonitorSettings.cs`) | 113 lines (`MonitorOptions.cs`) |
| Service plumbing | 119 + 34 lines (`MonitorService` + `ProjectInstaller`) | 64 + 40 lines (two workers) |
| Logging code | 53 lines (`Log.cs`) | 0 (built in) |
| Deploy folder | ~61 MB (+ Framework already in Windows) | ~106 MB self-contained single-file, ~36 MB framework-dependent |
| Runs on a clean Windows Server | ✅ (4.8 is in-box on 2022+) | ✅ self-contained |

**Your turn: measure resource use on your server.** Install both side by side
(see [02 Build, install & remove](02-Build-Install.md#29-running-both-editions-side-by-side-to-compare-them)),
let them run for a day, then fill in this table:

| Measure (after 24 h) | Net48 | Net10 | Where to read it |
|---|---|---|---|
| Private memory (avg) | | | daily report → "Monitor's own memory" tile, or `self.private_mb` |
| CPU % (avg) | | | daily report summary table → "VailITMonitor CPU" |
| Working set | | | Task Manager → Details → Memory |
| Time to start | | | `Measure-Command { Restart-Service VailITMonitor }` |
| Time to make a monthly report | | | `Measure-Command { .\VailITMonitor.exe report monthly }` |

Expect both to be small (tens of MB, a fraction of a percent of CPU). Modern .NET is generally
faster at run time, after years of JIT, GC and library work. .NET Framework may use less memory
at idle, because parts of it are shared with every other .NET Framework process on the machine.

### E2. When to use which

| Situation | Choose |
|---|---|
| New code | **.NET 10** (or the newest LTS) |
| Existing Framework app that works and rarely changes | Leave it on 4.8. It's supported, and Windows Update patches it. |
| Existing app you actively develop | Plan a migration to modern .NET |
| Need WCF server, Web Forms, Workflow Foundation, .NET Remoting | These don't exist in modern .NET. Stay on 4.8 or rewrite (CoreWCF, ASP.NET Core, gRPC). |
| Must run on Linux / in containers | **.NET 10** |
| Can't install anything and can't ship a large .exe | **4.8** (in-box) |

### E3. A migration path (for your own Framework apps)

1. **Convert the project file to SDK style**, still targeting `net48`, as this repo's Net48 project does.
2. **Raise `LangVersion`** and start writing modern C# where it compiles.
3. **Move libraries to .NET Standard 2.0** so they work in both worlds during the transition.
4. **Swap Framework-only APIs** for their modern equivalents: `ConfigurationManager` →
   `IConfiguration`, `ServiceBase` → `BackgroundService`, `HttpWebRequest` → `HttpClient`,
   `BinaryFormatter` → System.Text.Json, ...
5. **Change the target** to `net10.0` / `net10.0-windows`, build, fix, test.

Microsoft's tooling can do much of steps 1 and 5 for you: the *Upgrade* option on a project in
Visual Studio, and GitHub Copilot's app-modernization agent.

---

## Exercises

Each one is small, and doing it in **both** editions shows the difference in practice.

1. **Add a metric** (*easy*). Add `net.tcp_connections` as described in
   [04 Editing the code](04-Editing-The-Code.md#add-a-new-metric). Notice that everything else,
   including storage, rollups and the report table, picks it up automatically.
2. **Add a setting** (*easy*). Add `Reports.Footer` (text for the bottom of every page). Compare
   the steps: .NET 10 needs a property and JSON; 4.8 also needs a line in `Load()`.
3. **Modern C# on the Framework** (*easy*). Set `<LangVersion>latest</LangVersion>` in the Net48 project.
   Convert `Periods.Next` to a switch expression and `MetricStore.GetEarliestSampleTime` to `using var`.
   Build. Then try `sorted[^1]` and see the error that a runtime type is missing.
4. **Override settings without editing files** (*easy*). On the .NET 10 edition run
   `$env:Monitor__SampleIntervalSeconds = 5; .\VailITMonitor.exe console`. Where would you add
   that for the Framework edition?
5. **Use DI in the Framework edition** (*medium*). Add the `Microsoft.Extensions.DependencyInjection`
   NuGet package to the Net48 project (it supports .NET Framework) and replace the hand wiring in `Program.Main`.
6. **Validate configuration** (*medium*). In .NET 10, add the `Microsoft.Extensions.Options.DataAnnotations` package, put `[Range(5, 3600)]` on
   `SampleIntervalSeconds` and change the `Configure` call to
   `AddOptions<MonitorOptions>().Bind(...).ValidateDataAnnotations().ValidateOnStart()`. Put a bad
   value in `appsettings.json` and start the program.
7. **Threshold alerts** (*medium*). Log a warning when a drive falls below 10% free (see 04). Use
   structured logging in .NET 10 and `Log.Warn` in 4.8.
8. **Native AOT** (*advanced*). Try `dotnet publish -r win-x64 -p:PublishAot=true` on the .NET 10
   project. Read the warnings: which libraries aren't AOT-friendly? What would you change?

## Glossary

| Term | Meaning |
|---|---|
| **TFM** | Target Framework Moniker: `net48`, `net10.0`, `net10.0-windows`, `netstandard2.0` |
| **RID** | Runtime Identifier: `win-x64`, `linux-arm64`, ... |
| **LTS / STS** | Long-Term Support (3 years) / Standard-Term Support (2 years) |
| **SDK-style project** | The short modern `.csproj` format (`<Project Sdk="...">`) |
| **Generic Host** | The `Host` object that provides config + DI + logging + lifetime for any app type |
| **Hosted service** | A class implementing `IHostedService`, usually via `BackgroundService`, started and stopped by the host |
| **Self-contained** | Deployment that carries its own copy of the .NET runtime |
| **.NET Standard** | A specification of APIs available on both .NET Framework and modern .NET; libraries target it to work everywhere |
| **Source generator** | Compiler plug-in that writes code at build time (e.g. `LibraryImport`, `System.Text.Json`) |
