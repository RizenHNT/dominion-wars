param([string]$OutputDirectory = 'build-output\dynamic\probe')
$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
Set-Location -LiteralPath $repoRoot
$out = [IO.Path]::GetFullPath($OutputDirectory)
$obj = Join-Path $out 'compile'
New-Item -ItemType Directory -Force -Path $out,$obj | Out-Null
$sdk = (& dotnet --list-sdks | Select-Object -Last 1)
$sdkVersion = ($sdk -split ' ')[0]
$sdkBase = ($sdk -replace '^.*\[','' -replace '\]$','')
$csc = Join-Path $sdkBase "$sdkVersion\Roslyn\bincore\csc.dll"
$dotnetRoot = Split-Path $sdkBase -Parent
$refRoot = Join-Path $dotnetRoot 'packs\Microsoft.NETCore.App.Ref'
$refVersion = Get-ChildItem -LiteralPath $refRoot -Directory | Where-Object Name -Like '8.*' | Sort-Object { [version]$_.Name } | Select-Object -Last 1
$refs = Join-Path $refVersion.FullName 'ref\net8.0'
$newtonsoft = (Resolve-Path 'build-output\wood-probe\run\Newtonsoft.Json.dll').Path
Copy-Item -LiteralPath $newtonsoft -Destination $out -Force

function Compile-Source([string]$Name, [string]$Target, [string[]]$Sources, [string[]]$References) {
    $rsp = Join-Path $obj "$Name.rsp"
    $lines = @('/nologo', "/target:$Target", '/langversion:9.0', '/nullable:enable', '/optimize+', '/deterministic+', '/nostdlib+', ('/out:"' + (Join-Path $out "$Name.dll") + '"'))
    $lines += Get-ChildItem -LiteralPath $refs -Filter '*.dll' | ForEach-Object { '/reference:"' + $_.FullName + '"' }
    $lines += $References | ForEach-Object { '/reference:"' + $_ + '"' }
    $lines += $Sources | ForEach-Object { '"' + $_ + '"' }
    Set-Content -LiteralPath $rsp -Value $lines -Encoding UTF8
    & dotnet $csc "@$rsp"
    if ($LASTEXITCODE -ne 0) { throw "$Name compilation failed" }
    Write-Host "Compiled $Name"
}
function Sources([string]$Directory) {
    @(Get-ChildItem -LiteralPath $Directory -Recurse -Filter '*.cs' | Where-Object { $_.FullName -notmatch '\\(Tests|obj|bin)\\' } | ForEach-Object FullName)
}
$engine = @(Sources 'src\Engine')
$data = @(Sources 'src\Data')
$adapters = @(Sources 'src\Adapters')
$driver = @(Get-ChildItem -LiteralPath 'build-output\pl-csim' -Filter '*.cs' | ForEach-Object FullName)
$probe = @((Resolve-Path 'build-output\wood-probe\Program.cs').Path, (Resolve-Path 'build-output\wood-probe\ProbeRunner.cs').Path)
$e = Join-Path $out 'DominionWars.Engine.dll'
$d = Join-Path $out 'DominionWars.Data.dll'
$a = Join-Path $out 'DominionWars.Adapters.dll'
Compile-Source 'DominionWars.Engine' 'library' $engine @()
Compile-Source 'DominionWars.Data' 'library' $data @($e,$newtonsoft)
Compile-Source 'DominionWars.Adapters' 'library' $adapters @($e,$d,$newtonsoft)
Compile-Source 'PlCsim' 'exe' $driver @($e,$d,$a,$newtonsoft)
Compile-Source 'WoodPoolProbe' 'exe' $probe @($e,$d,$a,$newtonsoft,(Join-Path $out 'PlCsim.dll'))
foreach ($name in @('PlCsim','WoodPoolProbe')) {
    '{"runtimeOptions":{"tfm":"net8.0","framework":{"name":"Microsoft.NETCore.App","version":"8.0.0"}}}' | Set-Content -LiteralPath (Join-Path $out "$name.runtimeconfig.json") -Encoding UTF8
}
$pinned = @('DominionWars.Engine.dll','DominionWars.Data.dll','DominionWars.Adapters.dll','Newtonsoft.Json.dll') | ForEach-Object {
    [ordered]@{ file = $_; sha256 = (Get-FileHash -LiteralPath (Join-Path $out $_) -Algorithm SHA256).Hash.ToLowerInvariant() }
}
[ordered]@{ utc = [DateTime]::UtcNow.ToString('o'); files = @($pinned) } | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $out 'PINNED_REVISION.json') -Encoding UTF8
$sourceHashes = @($engine + $data + $adapters + $driver + $probe) | Sort-Object -Unique | ForEach-Object {
    [ordered]@{ file = $_; sha256 = (Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash.ToLowerInvariant() }
}
[ordered]@{ utc = [DateTime]::UtcNow.ToString('o'); sdk = $sdkVersion; sources = @($sourceHashes) } | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $out 'SOURCE_REVISION.json') -Encoding UTF8
Write-Host "Shared driver ready: $out"
