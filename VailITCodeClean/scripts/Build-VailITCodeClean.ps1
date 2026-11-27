<#
.SYNOPSIS
    Builds VailIT Code Clean into the "publish" folder.

.DESCRIPTION
    publish\App   VailITCodeClean.exe - the Windows application.
    publish\Cli   vailit-codeclean.exe - the command-line version (scripts, CI pipelines).

    Both are self-contained by default: one .exe with the .NET runtime inside, nothing to install.
    Requires the .NET 10 SDK:  winget install Microsoft.DotNet.SDK.10
    (Visual Studio 2022 17.14+/2026 includes it.)

.PARAMETER FrameworkDependent
    Build small .exe files WITHOUT the runtime inside. The PC then needs the ".NET 10 Desktop Runtime":
    winget install Microsoft.DotNet.DesktopRuntime.10

.PARAMETER SkipTests
    Do not run the unit tests first.

.EXAMPLE
    .\scripts\Build-VailITCodeClean.ps1
.EXAMPLE
    .\scripts\Build-VailITCodeClean.ps1 -FrameworkDependent
#>
[CmdletBinding()]
param(
    [switch]$FrameworkDependent,
    [switch]$SkipTests
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$publish = Join-Path $root 'publish'

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw "The 'dotnet' command was not found. Install the .NET 10 SDK: winget install Microsoft.DotNet.SDK.10"
}

if (-not $SkipTests) {
    Write-Host 'Running tests...' -ForegroundColor Cyan
    dotnet test (Join-Path $root 'tests\VailITCodeClean.Tests\VailITCodeClean.Tests.csproj') -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
}

$common = @('-c', 'Release', '-r', 'win-x64', '-p:DebugType=none')
if ($FrameworkDependent) { $common += @('--self-contained', 'false') }
else { $common += @('--self-contained', 'true', '-p:PublishSingleFile=true', '-p:IncludeNativeLibrariesForSelfExtract=true') }

foreach ($item in @(
        @{ Name = 'App'; Project = 'src\VailITCodeClean.App\VailITCodeClean.App.csproj' },
        @{ Name = 'Cli'; Project = 'src\VailITCodeClean.Cli\VailITCodeClean.Cli.csproj' })) {
    $out = Join-Path $publish $item.Name
    Write-Host "Building $($item.Name) -> $out" -ForegroundColor Cyan
    if (Test-Path $out) { Remove-Item $out -Recurse -Force }
    dotnet publish (Join-Path $root $item.Project) @common -o $out
    if ($LASTEXITCODE -ne 0) { throw "$($item.Name) build failed." }
}

Copy-Item (Join-Path $root 'samples\Default.profile.json') $publish -Force
Write-Host ''
Write-Host "Done. Start the app with: $publish\App\VailITCodeClean.exe" -ForegroundColor Green
