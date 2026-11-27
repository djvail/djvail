<#
.SYNOPSIS
    Builds VailITDashboard into ..\publish as a self-contained 64-bit Windows program
    (no .NET install needed on the server).

.EXAMPLE
    .\Publish-VailITDashboard.ps1
#>
[CmdletBinding()]
param([string]$Output)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
if (-not $Output) { $Output = Join-Path $root 'publish' }

dotnet publish (Join-Path $root 'src\VailITDashboard\VailITDashboard.csproj') `
    -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -o $Output
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed." }
Write-Host "Published to $Output"
