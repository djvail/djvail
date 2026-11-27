<#
.SYNOPSIS
    Removes the VailITDashboard service. Keeps the database unless -RemoveData is given.
#>
[CmdletBinding()]
param(
    [string]$InstallPath = "$env:ProgramFiles\VailITDashboard",
    [string]$ServiceName = 'VailITDashboard',
    [switch]$RemoveData
)
$ErrorActionPreference = 'Stop'

if (Get-Service -Name $ServiceName -ErrorAction SilentlyContinue) {
    Stop-Service -Name $ServiceName -Force -ErrorAction SilentlyContinue
    sc.exe delete $ServiceName | Out-Null
    Write-Host "Service '$ServiceName' removed."
}
Get-NetFirewallRule -DisplayName 'VailIT Dashboard (TCP *' -ErrorAction SilentlyContinue | Remove-NetFirewallRule
if (Test-Path $InstallPath) { Remove-Item $InstallPath -Recurse -Force; Write-Host "Removed $InstallPath" }
if ($RemoveData) {
    $data = Join-Path $env:ProgramData 'VailITDashboard'
    if (Test-Path $data) { Remove-Item $data -Recurse -Force; Write-Host "Removed $data" }
}
