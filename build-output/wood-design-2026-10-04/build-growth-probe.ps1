# build-growth-probe.ps1 -- compiles the wood growth-timeline probe.
#
# It does NOT recompile the engine. It links against the assemblies already produced by
# build-output\dynamic\build-probe.ps1 (build-output\dynamic\probe\), which is the shared
# driver those CLIs run on. Run that build first:
#
#   powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File build-output\dynamic\build-probe.ps1
#   powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File build-output\wood-design-2026-10-04\build-growth-probe.ps1
#   dotnet build-output\wood-design-2026-10-04\probe\WoodPoolProbe.dll data\cards data\decks 10 1 --seed 1 --out build-output\wood-design-2026-10-04\runs\timeline-01
#
# The output FILE is named WoodPoolProbe.dll on purpose: PlCsim declares
#   [assembly: InternalsVisibleTo("WoodPoolProbe")]   (build-output/pl-csim/DriverTelemetry.cs:10)
# and SingleMatch / SimOptions / DriverObserver are internal to PlCsim.dll. The assembly
# name is the file name, so the output must be called WoodPoolProbe.dll to get access.
param(
    [string]$Source = 'build-output\wood-design-2026-10-04\WoodGrowthProbe.cs',
    [string]$ReferenceDirectory = 'build-output\dynamic\probe',
    [string]$OutputDirectory = 'build-output\wood-design-2026-10-04\probe'
)
$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
Set-Location -LiteralPath $repoRoot

$out = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Force -Path $out | Out-Null
$refDir = (Resolve-Path -LiteralPath $ReferenceDirectory).Path
$src = (Resolve-Path -LiteralPath $Source).Path

# Runtime assemblies are loaded from the driver directory beside the output; copy the
# whole reference set so the probe runs in place without editing the shared driver dir.
Copy-Item -Path (Join-Path $refDir '*.dll') -Destination $out -Force
'{"runtimeOptions":{"tfm":"net8.0","framework":{"name":"Microsoft.NETCore.App","version":"8.0.0"}}}' |
    Set-Content -LiteralPath (Join-Path $out 'WoodPoolProbe.runtimeconfig.json') -Encoding UTF8

$sdk = (& dotnet --list-sdks | Select-Object -Last 1)
$sdkVersion = ($sdk -split ' ')[0]
$sdkBase = ($sdk -replace '^.*\[','' -replace '\]$','')
$csc = Join-Path $sdkBase "$sdkVersion\Roslyn\bincore\csc.dll"
$dotnetRoot = Split-Path $sdkBase -Parent
$refRoot = Join-Path $dotnetRoot 'packs\Microsoft.NETCore.App.Ref'
$refVersion = Get-ChildItem -LiteralPath $refRoot -Directory | Where-Object Name -Like '8.*' |
    Sort-Object { [version]$_.Name } | Select-Object -Last 1
$refs = Join-Path $refVersion.FullName 'ref\net8.0'

$rsp = Join-Path $out 'WoodGrowthProbe.rsp'
$lines = @('/nologo', '/target:exe', '/langversion:9.0', '/nullable:enable', '/optimize+', '/deterministic+', '/nostdlib+',
           ('/out:"' + (Join-Path $out 'WoodPoolProbe.dll') + '"'))
$lines += Get-ChildItem -LiteralPath $refs -Filter '*.dll' | ForEach-Object { '/reference:"' + $_.FullName + '"' }
foreach ($name in @('DominionWars.Engine.dll','DominionWars.Data.dll','DominionWars.Adapters.dll','PlCsim.dll','Newtonsoft.Json.dll')) {
    $lines += '/reference:"' + (Join-Path $refDir $name) + '"'
}
$lines += '"' + $src + '"'
Set-Content -LiteralPath $rsp -Value $lines -Encoding UTF8

& dotnet $csc "@$rsp"
if ($LASTEXITCODE -ne 0) { throw "WoodGrowthProbe compilation failed" }

Write-Host "Compiled: $out\WoodPoolProbe.dll"
Get-FileHash -LiteralPath (Join-Path $out 'WoodPoolProbe.dll') -Algorithm SHA256 |
    ForEach-Object { Write-Host ("WoodPoolProbe.dll sha256=" + $_.Hash.ToLowerInvariant()) }
