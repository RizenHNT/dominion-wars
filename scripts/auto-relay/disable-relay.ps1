[CmdletBinding(DefaultParameterSetName = 'Disable')]
param(
    [Parameter(ParameterSetName = 'Enable')][switch]$Enable,
    [Parameter(ParameterSetName = 'Status')][switch]$Status
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'relay-common.ps1')
$repoRoot = Get-RelayRepositoryRoot -ScriptDirectory $PSScriptRoot
$stateRoot = Get-RelayStateRoot -RepositoryRoot $repoRoot
Initialize-RelayStateRoot -Path $stateRoot
$marker = Join-Path $stateRoot 'AUTO_RELAY_DISABLED'

if ($Status) {
    [pscustomobject]@{ Disabled = (Test-Path -LiteralPath $marker -PathType Leaf); Marker = $marker } | ConvertTo-Json
    exit 0
}
if ($Enable) {
    if (Test-Path -LiteralPath $marker -PathType Leaf) { Remove-Item -LiteralPath $marker -Force }
    Write-Host 'Auto relay enabled for future starts.'
    exit 0
}
Write-RelayAtomicText -Path $marker -Content ("Disabled by {0} at {1:o}" -f [Security.Principal.WindowsIdentity]::GetCurrent().Name, [DateTime]::UtcNow)
Write-Host 'Auto relay disabled. An active daytime relay will stop before its next paid-model call or test stage; no process is force-killed mid-write.'
