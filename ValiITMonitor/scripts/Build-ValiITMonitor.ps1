<#
.SYNOPSIS
    Builds both editions of ValiITMonitor into the "publish" folder, ready to install.

.DESCRIPTION
    publish\Net10   .NET 10 edition. Self-contained by default (nothing to install on the server).
    publish\Net48   .NET Framework 4.8 edition (uses the .NET Framework built into Windows).

    Requires the .NET 10 SDK:  winget install Microsoft.DotNet.SDK.10
    (Visual Studio 2022 17.14+/2026 includes it.) The SDK also builds the 4.8 edition.

.PARAMETER Edition
    Net10, Net48 or Both (default).

.PARAMETER FrameworkDependent
    Build the .NET 10 edition WITHOUT the runtime inside it (a folder of DLLs, ~36 MB). The server then
    needs the ".NET 10 Runtime" installed:  winget install Microsoft.DotNet.Runtime.10

.EXAMPLE
    .\scripts\Build-ValiITMonitor.ps1
.EXAMPLE
    .\scripts\Build-ValiITMonitor.ps1 -Edition Net10 -FrameworkDependent
#>
[CmdletBinding()]
param(
    [ValidateSet('Net10', 'Net48', 'Both')]
    [string]$Edition = 'Both',
    [switch]$FrameworkDependent
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$publish = Join-Path $root 'publish'

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw "The 'dotnet' command was not found. Install the .NET 10 SDK: winget install Microsoft.DotNet.SDK.10"
}

if ($Edition -in 'Net10', 'Both') {
    $out = Join-Path $publish 'Net10'
    Write-Host "Building .NET 10 edition -> $out" -ForegroundColor Cyan
    if (Test-Path $out) { Remove-Item $out -Recurse -Force }

    $project = Join-Path $root 'src\ValiITMonitor.Net10\ValiITMonitor.Net10.csproj'
    if ($FrameworkDependent) {
        # A normal folder of DLLs that runs on the installed .NET 10 runtime (~36 MB).
        dotnet publish $project -c Release -r win-x64 --self-contained false -p:DebugType=none -o $out
    }
    else {
        # One ValiITMonitor.exe with the .NET runtime inside (~80 MB) + a few native DLLs.
        dotnet publish $project -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:DebugType=none -o $out
    }
    if ($LASTEXITCODE -ne 0) { throw ".NET 10 build failed." }
}

if ($Edition -in 'Net48', 'Both') {
    $out = Join-Path $publish 'Net48'
    Write-Host "Building .NET Framework 4.8 edition -> $out" -ForegroundColor Cyan
    if (Test-Path $out) { Remove-Item $out -Recurse -Force }

    dotnet build (Join-Path $root 'src\ValiITMonitor.Net48\ValiITMonitor.Net48.csproj') `
        -c Release -p:DebugType=none -o $out
    if ($LASTEXITCODE -ne 0) { throw ".NET Framework 4.8 build failed." }
}

Write-Host ""
Write-Host "Done. Install with (as Administrator):" -ForegroundColor Green
Write-Host "  .\scripts\Install-ValiITMonitor.ps1 -Edition Net10"
Write-Host "  .\scripts\Install-ValiITMonitor.ps1 -Edition Net48"
