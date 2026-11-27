<#
.SYNOPSIS
    Installs (or upgrades) VailITDashboard as a Windows service.

.DESCRIPTION
    1. Stops the service if it is already installed (upgrade).
    2. Copies the program to the install folder. An existing appsettings.json is KEPT unless
       -OverwriteConfig is given, so your settings survive upgrades (the new one is saved as .new).
    3. Creates the service (Automatic, delayed start), sets it to restart itself if it crashes.
    4. Opens the listening port in Windows Firewall so monitors can reach it.
    5. Starts it.

    Run from an elevated PowerShell ("Run as Administrator"). Build first with Publish-VailITDashboard.ps1.

.EXAMPLE
    .\Install-VailITDashboard.ps1
.EXAMPLE
    .\Install-VailITDashboard.ps1 -Port 8080
#>
[CmdletBinding()]
param(
    [string]$SourcePath,
    [string]$InstallPath = "$env:ProgramFiles\VailITDashboard",
    [string]$ServiceName = 'VailITDashboard',
    [int]$Port = 5080,
    [switch]$OverwriteConfig,
    [switch]$NoFirewallRule,
    [switch]$NoStart
)

$ErrorActionPreference = 'Stop'

$principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw "Please run this script from an elevated PowerShell (Run as Administrator)."
}

if (-not $SourcePath) { $SourcePath = Join-Path (Split-Path -Parent $PSScriptRoot) 'publish' }
if (-not (Test-Path (Join-Path $SourcePath 'VailITDashboard.exe'))) {
    throw "VailITDashboard.exe not found in '$SourcePath'. Run scripts\Publish-VailITDashboard.ps1 first or pass -SourcePath."
}

# --- 1. Stop an existing installation ----------------------------------------------------------
$existing = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($existing -and $existing.Status -ne 'Stopped') {
    Write-Host "Stopping existing service '$ServiceName'..."
    Stop-Service -Name $ServiceName -Force
    (Get-Service -Name $ServiceName).WaitForStatus('Stopped', [TimeSpan]::FromSeconds(30))
}

# --- 2. Copy files -----------------------------------------------------------------------------
Write-Host "Copying files to $InstallPath ..."
New-Item -ItemType Directory -Path $InstallPath -Force | Out-Null
$config = Join-Path $InstallPath 'appsettings.json'
$keepConfig = (Test-Path $config) -and -not $OverwriteConfig
Get-ChildItem -Path $SourcePath | ForEach-Object {
    if ($_.Name -eq 'appsettings.Development.json') { return }
    if ($keepConfig -and $_.Name -eq 'appsettings.json') {
        Write-Host "  Keeping your existing appsettings.json (new default saved as appsettings.json.new)."
        Copy-Item $_.FullName "$config.new" -Force
    }
    else {
        Copy-Item $_.FullName (Join-Path $InstallPath $_.Name) -Recurse -Force
    }
}

# --- 3. Create the service ---------------------------------------------------------------------
$exe = Join-Path $InstallPath 'VailITDashboard.exe'
if (-not (Get-Service -Name $ServiceName -ErrorAction SilentlyContinue)) {
    Write-Host "Creating service '$ServiceName'..."
    New-Service -Name $ServiceName -BinaryPathName "`"$exe`"" -DisplayName 'VailIT Dashboard' `
        -Description 'Receives VailITMonitor reports, shows server health and emails alerts.' -StartupType Automatic | Out-Null
    sc.exe config $ServiceName start= delayed-auto | Out-Null
}
# Restart after 1 minute if it crashes (three times, reset the count daily).
sc.exe failure $ServiceName reset= 86400 actions= restart/60000/restart/60000/restart/60000 | Out-Null

# --- 4. Firewall -------------------------------------------------------------------------------
if (-not $NoFirewallRule) {
    $rule = 'VailIT Dashboard (TCP ' + $Port + ')'
    if (-not (Get-NetFirewallRule -DisplayName $rule -ErrorAction SilentlyContinue)) {
        New-NetFirewallRule -DisplayName $rule -Direction Inbound -Protocol TCP -LocalPort $Port -Action Allow | Out-Null
        Write-Host "Opened TCP $Port in Windows Firewall."
    }
}

# --- 5. Start ----------------------------------------------------------------------------------
if (-not $NoStart) {
    Start-Service -Name $ServiceName
    Write-Host "Started. Open http://localhost:$Port"
}
Write-Host ""
Write-Host "Next: set a sign-in user (VailITDashboard.exe hash-password), SMTP and alert recipients in"
Write-Host "  $config"
Write-Host "then:  Restart-Service $ServiceName"
