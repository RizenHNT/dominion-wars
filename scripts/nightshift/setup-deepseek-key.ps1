[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$targetDirectory = Join-Path $env:LOCALAPPDATA 'DominionWarsNightshift'
$targetFile = Join-Path $targetDirectory 'deepseek.key'

New-Item -ItemType Directory -Path $targetDirectory -Force | Out-Null
$secret = Read-Host 'Paste the DeepSeek API key (input is hidden)' -AsSecureString
if ($secret.Length -eq 0) {
    throw 'No API key was entered.'
}

# ConvertFrom-SecureString uses Windows DPAPI. Only this Windows user on this
# computer can decrypt the resulting value.
$secret | ConvertFrom-SecureString | Set-Content -LiteralPath $targetFile -Encoding UTF8
Write-Host "DeepSeek credential saved with Windows DPAPI at: $targetFile"
Write-Host 'The plaintext key was not written to the repository.'
