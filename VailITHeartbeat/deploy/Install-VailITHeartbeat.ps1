<#
.SYNOPSIS
    Installs (or upgrades) VailITHeartbeat as a Windows service on a machine OTHER than the dashboard server.

.DESCRIPTION
    Copies the published program to the install folder (keeping an existing appsettings.json
    unless -OverwriteConfig), creates an automatic service that restarts itself on crash, and starts it.
    Run from an elevated PowerShell. Build first with:
        dotnet publish src\VailITHeartbeat -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o publish\win-x64

.EXAMPLE
    .\deploy\Install-VailITHeartbeat.ps1
.EXAMPLE
    .\deploy\Install-VailITHeartbeat.ps1 -Uninstall
#>
[CmdletBinding()]
param(
    [string]$SourcePath = (Join-Path (Split-Path -Parent $PSScriptRoot) 'publish\win-x64'),
    [string]$InstallPath = "$env:ProgramFiles\VailITHeartbeat",
    [string]$ServiceName = 'VailITHeartbeat',
    [switch]$OverwriteConfig,
    [switch]$Uninstall
)

$ErrorActionPreference = 'Stop'
$principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Please run this script from an elevated PowerShell (Run as Administrator).'
}

$existing = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($existing) {
    Write-Host "Stopping $ServiceName..."
    Stop-Service -Name $ServiceName -Force -ErrorAction SilentlyContinue
}

if ($Uninstall) {
    if ($existing) { sc.exe delete $ServiceName | Out-Null; Write-Host "Removed service $ServiceName." }
    Write-Host "Program files left in $InstallPath (delete them if you no longer need the settings)."
    return
}

$exe = Join-Path $SourcePath 'VailITHeartbeat.exe'
if (-not (Test-Path $exe)) { throw "VailITHeartbeat.exe not found in '$SourcePath'. Publish first (see the script help)." }

New-Item -ItemType Directory -Force -Path $InstallPath | Out-Null
$config = Join-Path $InstallPath 'appsettings.json'
Get-ChildItem $SourcePath | Where-Object { $_.Name -ne 'appsettings.json' -or $OverwriteConfig -or -not (Test-Path $config) } |
    Copy-Item -Destination $InstallPath -Recurse -Force

$binary = Join-Path $InstallPath 'VailITHeartbeat.exe'
if (-not $existing) {
    New-Service -Name $ServiceName -BinaryPathName "`"$binary`"" -DisplayName 'VailIT Heartbeat' `
        -Description 'Checks the VailIT dashboard from outside and emails when it cannot be reached.' -StartupType Automatic | Out-Null
}
sc.exe failure $ServiceName reset= 86400 actions= restart/60000/restart/60000/restart/60000 | Out-Null

Write-Host "Edit $config (dashboard URL, SMTP settings), then test with:"
Write-Host "    & `"$binary`" test-email"
Write-Host "    & `"$binary`" once"
Start-Service -Name $ServiceName
Write-Host "$ServiceName is running."
