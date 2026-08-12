[CmdletBinding()]
param(
    [switch]$NoRestore
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$project = Join-Path $repoRoot 'tools\SchemaValidator\SchemaValidator.csproj'
$dotnetHome = Join-Path $repoRoot '.schema-validator-dotnet-home'
$nugetPackages = Join-Path $repoRoot '.schema-validator-packages'

$env:DOTNET_CLI_HOME = $dotnetHome
$env:NUGET_PACKAGES = $nugetPackages
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'

$arguments = @('run', '--project', $project, '--configuration', 'Release')
if ($NoRestore) { $arguments += '--no-restore' }
& dotnet @arguments
exit $LASTEXITCODE
