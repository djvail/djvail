<#
.SYNOPSIS
    Sends one made-up report to the dashboard, to check the URL and an API key work
    before any real monitor is pointed at it.

.EXAMPLE
    .\Send-TestReport.ps1 -DashboardUrl http://dashboard:5080 -ApiKey vmk_... -Cpu 97
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$DashboardUrl,
    [Parameter(Mandatory)][string]$ApiKey,
    [string]$MachineName = "TEST-$env:COMPUTERNAME",
    [string]$MachineId = '00000000-0000-4000-8000-000000000001',
    [string]$ServerType = 'Test',
    [double]$Cpu = 12.5,
    [double]$DiskFreePct = 42
)
$now = (Get-Date).ToUniversalTime().ToString('yyyy-MM-ddTHH:mm:00Z')
$body = @{
    schemaVersion = 1
    sentAtUtc = $now
    monitor = @{ machineId = $MachineId; machineName = $MachineName; serverType = $ServerType; agentVersion = 'test'; edition = 'Test' }
    samples = @(@{ timestampUtc = $now; readings = @(
        @{ metric = 'cpu.total_pct'; instance = ''; value = $Cpu },
        @{ metric = 'mem.used_pct'; instance = ''; value = 50 },
        @{ metric = 'disk.free_pct'; instance = 'C:'; value = $DiskFreePct }
    ) })
} | ConvertTo-Json -Depth 6

Invoke-RestMethod -Method Post -Uri "$($DashboardUrl.TrimEnd('/'))/api/v1/reports" `
    -Headers @{ 'X-Api-Key' = $ApiKey } -ContentType 'application/json' -Body $body
