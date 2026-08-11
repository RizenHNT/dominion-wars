[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidateLength(1, 120)][ValidatePattern('^[^\r\n]+$')][string]$MailboxHeading,
    [switch]$DryRun
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'relay-common.ps1')

$repoRoot = Get-RelayRepositoryRoot -ScriptDirectory $PSScriptRoot
$relativeFiles = @('AGENTS.md', 'docs/AI_WORKFLOW.md', 'docs/AI_MAILBOX.md')
$attachmentPaths = @()
$combinedCharacters = 0
$credentialPattern = '(?i)(?:sk-[A-Za-z0-9_-]{20,}|gh[pousr]_[A-Za-z0-9_]{20,}|AIza[0-9A-Za-z_-]{30,}|Authorization\s*:\s*Bearer\s+[A-Za-z0-9._~-]{20,}|-----BEGIN (?:RSA |EC |OPENSSH )?PRIVATE KEY-----)'

foreach ($relativePath in $relativeFiles) {
    $fullPath = [IO.Path]::GetFullPath((Join-Path $repoRoot ($relativePath.Replace('/', '\'))))
    if (-not (Test-Path -LiteralPath $fullPath -PathType Leaf)) { throw "Approved PL attachment is missing: $relativePath" }
    $item = Get-Item -LiteralPath $fullPath -Force
    if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw "Approved PL attachment is a reparse point: $relativePath" }
    if ($item.Length -gt 1MB) { throw "Approved PL attachment exceeds 1 MB: $relativePath" }
    $text = [IO.File]::ReadAllText($fullPath, [Text.Encoding]::UTF8)
    if ($text -match $credentialPattern) { throw "Credential-like content detected in approved PL attachment: $relativePath" }
    $combinedCharacters += $text.Length
    $attachmentPaths += $fullPath
}
if ($combinedCharacters -gt 500000) { throw 'Combined PL attachment text exceeds 500,000 characters.' }

$mailboxText = [IO.File]::ReadAllText($attachmentPaths[2], [Text.Encoding]::UTF8)
if (-not $mailboxText.Contains($MailboxHeading)) { throw 'The requested PL handoff heading does not exist in docs/AI_MAILBOX.md.' }

$prompt = @"
This is an automatic internal Codex-to-MiniMax-PL handoff for a goal the human owner already approved.
Routine collaboration does not require another human confirmation. Use only the attached files and do not run terminal commands or edit files.
Locate this exact mailbox heading: $MailboxHeading
Reply in concise Chinese with: (1) whether the handoff is accepted, (2) the next safe action, and (3) whether HUMAN_REQUIRED exists.
Do not repeat completed work and do not infer authority outside the approved goal.
"@

if ($DryRun) {
    [pscustomobject]@{
        status = 'DRY_RUN'
        target = 'minimax-pl'
        mailboxHeading = $MailboxHeading
        attachments = $relativeFiles
        combinedCharacters = $combinedCharacters
        credentialPatternMatched = $false
        vscodeStarted = $false
    } | ConvertTo-Json -Depth 5
    exit 0
}

$code = Get-Command code -CommandType Application -ErrorAction SilentlyContinue
if (-not $code) { throw 'VS Code CLI was not found.' }
$arguments = @('chat', '--reuse-window', '--mode', 'minimax-pl')
foreach ($path in $attachmentPaths) { $arguments += @('--add-file', $path) }
$arguments += $prompt
& $code.Source @arguments
if ($LASTEXITCODE -ne 0) { throw "VS Code PL handoff failed with exit code $LASTEXITCODE." }

[pscustomobject]@{
    status = 'DELIVERED_TO_VSCODE'
    target = 'minimax-pl'
    mailboxHeading = $MailboxHeading
    attachments = $relativeFiles
    vscodeStarted = $true
    note = 'VS Code starts a new PL chat; inspect that window for the actual model reply.'
} | ConvertTo-Json -Depth 5
