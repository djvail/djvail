<#
.SYNOPSIS
    Removes the ValiITMonitor Windows service.

.DESCRIPTION
    Stops and deletes the service. By default the program files, database and reports are left
    in place, so you can reinstall without losing history. Add -RemoveFiles and/or -RemoveData
    to delete them as well.

    Run from an elevated PowerShell ("Run as Administrator").

.PARAMETER InstallPath
    Program folder to delete with -RemoveFiles. Default: the folder the service runs from.

.PARAMETER DataFolder
    Data folder to delete with -RemoveData. Default: C:\ProgramData\ValiITMonitor (Net10 edition)
    or C:\ProgramData\ValiITMonitor-Net48 (Net48 edition), based on which program is installed.

.EXAMPLE
    .\Uninstall-ValiITMonitor.ps1
.EXAMPLE
    .\Uninstall-ValiITMonitor.ps1 -ServiceName ValiITMonitor48 -RemoveFiles -RemoveData
#>
[CmdletBinding(SupportsShouldProcess)]
param(
    [string]$ServiceName = 'ValiITMonitor',
    [string]$InstallPath,
    [string]$DataFolder,
    [switch]$RemoveFiles,
    [switch]$RemoveData
)

$ErrorActionPreference = 'Stop'

$principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw "Please run this script from an elevated PowerShell (Run as Administrator)."
}

$service = Get-CimInstance Win32_Service -Filter "Name='$ServiceName'" -ErrorAction SilentlyContinue
if (-not $service) {
    Write-Warning "Service '$ServiceName' is not installed."
}
else {
    if (-not $InstallPath) {
        $InstallPath = Split-Path -Parent ($service.PathName.Trim('"'))
    }
    if ($service.State -ne 'Stopped') {
        Write-Host "Stopping '$ServiceName'..."
        Stop-Service -Name $ServiceName -Force
        (Get-Service -Name $ServiceName).WaitForStatus('Stopped', [TimeSpan]::FromSeconds(30))
    }
    & sc.exe delete $ServiceName | Out-Null
    Write-Host "Service '$ServiceName' removed." -ForegroundColor Green
}

if ($RemoveData) {
    if (-not $DataFolder) {
        $isNet48 = $InstallPath -and (Test-Path (Join-Path $InstallPath 'ValiITMonitor.exe.config'))
        $DataFolder = if ($isNet48) { "$env:ProgramData\ValiITMonitor-Net48" } else { "$env:ProgramData\ValiITMonitor" }
    }
    if ((Test-Path $DataFolder) -and $PSCmdlet.ShouldProcess($DataFolder, 'Delete database and reports')) {
        Remove-Item $DataFolder -Recurse -Force
        Write-Host "Deleted data folder $DataFolder"
    }
}

if ($RemoveFiles -and $InstallPath -and (Test-Path $InstallPath)) {
    if ($PSCmdlet.ShouldProcess($InstallPath, 'Delete program files')) {
        Remove-Item $InstallPath -Recurse -Force
        Write-Host "Deleted program folder $InstallPath"
    }
}

if (-not $RemoveData) {
    Write-Host "Your database and reports were kept. Use -RemoveData to delete them."
}
