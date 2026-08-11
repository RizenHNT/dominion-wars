[CmdletBinding()]
param([switch]$Apply)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'relay-common.ps1')

$sourceDirectory = Join-Path $env:USERPROFILE '.mmx'
$sourceFile = Join-Path $sourceDirectory 'config.json'
$relayDirectory = Get-MiniMaxRelayConfigDirectory
$relayFile = Join-Path $relayDirectory 'config.json'

function Assert-PlainPath {
    param([Parameter(Mandatory)][string]$Path, [Parameter(Mandatory)][bool]$Directory)
    $pathType = if ($Directory) { 'Container' } else { 'Leaf' }
    if (-not (Test-Path -LiteralPath $Path -PathType $pathType)) { throw "MiniMax authentication path is missing: $Path" }
    $item = Get-Item -LiteralPath $Path -Force
    if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw "Refusing a reparse-point authentication path: $Path" }
}

if ($Apply) {
    Assert-PlainPath -Path $sourceDirectory -Directory $true
    Assert-PlainPath -Path $sourceFile -Directory $false
    Initialize-RelayStateRoot -Path $relayDirectory
    $temporary = Join-Path $relayDirectory ('.config.{0}.tmp' -f [guid]::NewGuid().ToString('N'))
    $backup = Join-Path $relayDirectory ('.config.{0}.bak' -f [guid]::NewGuid().ToString('N'))
    try {
        [IO.File]::Copy($sourceFile, $temporary, $false)
        Set-RelayPrivateFileAcl -Path $temporary
        if (-not (Test-RelayPrivateFileAcl -Path $temporary)) { throw 'The temporary MiniMax relay credential copy failed its ACL audit.' }
        if (Test-Path -LiteralPath $relayFile) {
            Assert-PlainPath -Path $relayFile -Directory $false
            [IO.File]::Replace($temporary, $relayFile, $backup, $true)
        }
        else {
            [IO.File]::Move($temporary, $relayFile)
        }
        if (-not (Test-RelayPrivateFileAcl -Path $relayFile -AllowSafeInheritance)) { Set-RelayPrivateFileAcl -Path $relayFile }
        if (-not (Test-RelayPrivateFileAcl -Path $relayFile -AllowSafeInheritance)) { throw 'The installed MiniMax relay credential copy failed its ACL audit.' }
        foreach ($oldBackup in @(Get-ChildItem -LiteralPath $relayDirectory -Force -Filter '.config.*.bak')) {
            if (($oldBackup.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw 'Refusing to remove a reparse-point credential backup.' }
            Remove-Item -LiteralPath $oldBackup.FullName -Force
        }
    }
    finally {
        if (Test-Path -LiteralPath $temporary -PathType Leaf) { Remove-Item -LiteralPath $temporary -Force }
    }
}

$directoryPrivate = Test-RelayPrivateAcl -Path $relayDirectory
$filePrivate = Test-RelayPrivateFileAcl -Path $relayFile -AllowSafeInheritance
[pscustomobject]@{
    RelayConfigDirectory = $relayDirectory
    RelayConfigPresent = (Test-Path -LiteralPath $relayFile -PathType Leaf)
    DirectoryPrivateAcl = $directoryPrivate
    FilePrivateAcl = $filePrivate
    SourceUnchanged = $true
    CredentialContentPrinted = $false
} | ConvertTo-Json -Depth 5
if (-not $directoryPrivate -or -not $filePrivate) { exit 2 }
