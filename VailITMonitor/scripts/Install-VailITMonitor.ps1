<#
.SYNOPSIS
    Installs (or upgrades) VailITMonitor as a Windows service.

.DESCRIPTION
    1. Stops the service if it is already installed (upgrade).
    2. Copies the program to the install folder. An existing config file is KEPT unless
       -OverwriteConfig is given, so your settings survive upgrades.
    3. Registers the "VailITMonitor" Event Log source.
    4. Creates the service (Automatic, delayed start), sets it to restart itself if it crashes,
       and starts it.

    Run from an elevated PowerShell ("Run as Administrator").
    If scripts are blocked:  Set-ExecutionPolicy -Scope Process Bypass

.PARAMETER Edition
    Net10 or Net48 - which build to install.

.PARAMETER SourcePath
    Folder containing the built program. Default: ..\publish\<Edition> next to this script.

.PARAMETER InstallPath
    Where to install. Default: C:\Program Files\VailITMonitor (Net10) or C:\Program Files\VailITMonitor-Net48.

.PARAMETER ServiceName
    Default "VailITMonitor". Use a different name (e.g. VailITMonitor48) to run both editions
    side by side for comparison - each edition keeps its data in its own folder by default.

.EXAMPLE
    .\Install-VailITMonitor.ps1 -Edition Net10
.EXAMPLE
    .\Install-VailITMonitor.ps1 -Edition Net48 -ServiceName VailITMonitor48
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateSet('Net10', 'Net48')]
    [string]$Edition,
    [string]$SourcePath,
    [string]$InstallPath,
    [string]$ServiceName = 'VailITMonitor',
    [switch]$OverwriteConfig,
    [switch]$NoStart
)

$ErrorActionPreference = 'Stop'

$principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw "Please run this script from an elevated PowerShell (Run as Administrator)."
}

if (-not $SourcePath) { $SourcePath = Join-Path (Split-Path -Parent $PSScriptRoot) "publish\$Edition" }
if (-not $InstallPath) {
    $InstallPath = if ($Edition -eq 'Net10') { "$env:ProgramFiles\VailITMonitor" } else { "$env:ProgramFiles\VailITMonitor-Net48" }
}
$exe = Join-Path $SourcePath 'VailITMonitor.exe'
if (-not (Test-Path $exe)) {
    throw "VailITMonitor.exe not found in '$SourcePath'. Build first (scripts\Build-VailITMonitor.ps1) or pass -SourcePath."
}

$configName = if ($Edition -eq 'Net10') { 'appsettings.json' } else { 'VailITMonitor.exe.config' }

# --- 1. Stop an existing installation ----------------------------------------------------------
$existing = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
if ($existing) {
    Write-Host "Stopping existing service '$ServiceName'..."
    if ($existing.Status -ne 'Stopped') { Stop-Service -Name $ServiceName -Force }
    (Get-Service -Name $ServiceName).WaitForStatus('Stopped', [TimeSpan]::FromSeconds(30))
}

# --- 2. Copy files -----------------------------------------------------------------------------
Write-Host "Copying files to $InstallPath ..."
New-Item -ItemType Directory -Path $InstallPath -Force | Out-Null
$keepConfig = (Test-Path (Join-Path $InstallPath $configName)) -and -not $OverwriteConfig
Get-ChildItem -Path $SourcePath | ForEach-Object {
    if ($keepConfig -and $_.Name -eq $configName) {
        Write-Host "  Keeping your existing $configName (use -OverwriteConfig to replace it)."
        Copy-Item $_.FullName (Join-Path $InstallPath "$configName.new") -Force
    }
    else {
        Copy-Item $_.FullName -Destination $InstallPath -Recurse -Force
    }
}

# --- 3. Event Log source -----------------------------------------------------------------------
if (-not [System.Diagnostics.EventLog]::SourceExists('VailITMonitor')) {
    [System.Diagnostics.EventLog]::CreateEventSource('VailITMonitor', 'Application')
    Write-Host "Registered Event Log source 'VailITMonitor' (Application log)."
}

# --- 4. Create / update the service ------------------------------------------------------------
$binPath = '"' + (Join-Path $InstallPath 'VailITMonitor.exe') + '"'
$displayName = if ($ServiceName -eq 'VailITMonitor') { 'VailIT Monitor' } else { "VailIT Monitor ($ServiceName)" }
$description = "VailITMonitor ($Edition): records CPU, memory, disk and network readings and produces PDF reports."

if ($existing) {
    # Upgrade: point the existing service at the (possibly new) path. CIM avoids sc.exe quoting quirks.
    $cim = Get-CimInstance Win32_Service -Filter "Name='$ServiceName'"
    Invoke-CimMethod -InputObject $cim -MethodName Change -Arguments @{ PathName = $binPath; DisplayName = $displayName } | Out-Null
}
else {
    New-Service -Name $ServiceName -BinaryPathName $binPath -DisplayName $displayName -StartupType Automatic | Out-Null
}
& sc.exe config $ServiceName start= delayed-auto | Out-Null
& sc.exe description $ServiceName $description | Out-Null
# Restart after 1 minute on the first three failures; reset the failure count after a day.
& sc.exe failure $ServiceName reset= 86400 actions= restart/60000/restart/60000/restart/60000 | Out-Null

if (-not $NoStart) {
    Start-Service -Name $ServiceName
    Start-Sleep -Seconds 2
}

Get-Service -Name $ServiceName | Format-Table Name, Status, StartType, DisplayName -AutoSize
Write-Host "Installed $Edition edition as '$ServiceName' in $InstallPath" -ForegroundColor Green
Write-Host "Settings: $(Join-Path $InstallPath $configName)   (restart the service after editing)"
Write-Host "Check it:  & '$(Join-Path $InstallPath 'VailITMonitor.exe')' sample"
