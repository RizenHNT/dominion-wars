[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidateNotNullOrEmpty()][string]$Message,
    [ValidatePattern('^RELAY-[A-Z0-9][A-Z0-9-]{7,63}$')][string]$RelayId = ('RELAY-PROBE-{0}-{1}' -f (Get-Date -Format 'yyyyMMdd'), ([guid]::NewGuid().ToString('N').Substring(0, 8).ToUpperInvariant())),
    [switch]$ApprovedByHuman,
    [switch]$DryRun,
    [switch]$NoMailboxWrite
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'relay-common.ps1')
$repoRoot = Get-RelayRepositoryRoot -ScriptDirectory $PSScriptRoot
$stateRoot = Get-RelayStateRoot -RepositoryRoot $repoRoot
$script:requestMirrored = $false
trap {
    $failureMessage = ConvertTo-RelayOneLine -Text $_.Exception.Message -MaximumLength 500
    if ($script:requestMirrored -and -not $NoMailboxWrite) {
        $script:requestMirrored = $false
        try {
            Add-RelayMailboxEntry -RepositoryRoot $repoRoot -Entry "### [Auto Relay -> Codex] MiniMax relay failed ($RelayId)`n**Status**: FAILED. $failureMessage"
        }
        catch {}
    }
    Write-Error $failureMessage
    exit 1
}
if ($Message.Length -gt 4000 -or $Message -match '[\x00-\x08\x0B\x0C\x0E-\x1F]') { throw 'Relay message exceeds the 4,000-character safe text limit or contains control characters.' }
if (Test-RelayDisabled -StateRoot $stateRoot) { throw 'Auto relay is disabled.' }
if (-not $DryRun -and -not $ApprovedByHuman) { throw 'A paid MiniMax relay probe requires -ApprovedByHuman.' }

$mmx = @(
    (Join-Path $env:APPDATA 'npm\mmx.cmd'),
    (Join-Path $env:APPDATA 'npm\mmx.ps1')
) | Where-Object { Test-Path -LiteralPath $_ -PathType Leaf } | Select-Object -First 1
if (-not $mmx) { throw 'MiniMax CLI (mmx) was not found.' }

if ($DryRun) {
    [pscustomobject]@{
        relayId = $RelayId
        target = 'MiniMax'
        model = 'MiniMax-M3'
        paidCall = $false
        mailboxWrite = (-not $NoMailboxWrite)
        messageChars = $Message.Length
    } | ConvertTo-Json
    exit 0
}

$authAudit = Join-Path $PSScriptRoot 'harden-minimax-auth.ps1'
& powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File $authAudit | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'The private MiniMax relay credential copy is missing or unsafe. Run harden-minimax-auth.ps1 -Apply before invoking the relay.' }
$mmxConfigDirectory = Get-MiniMaxRelayConfigDirectory

Initialize-RelayStateRoot -Path $stateRoot
$probeRoot = Join-Path $stateRoot 'minimax-probes'
if (-not (Test-Path -LiteralPath $probeRoot -PathType Container)) { New-Item -ItemType Directory -Path $probeRoot | Out-Null }
if (-not (Test-RelayPrivateAcl -Path $probeRoot)) { Set-RelayPrivateDirectoryAcl -Path $probeRoot }
$messageHasher = [Security.Cryptography.SHA256]::Create()
try { $messageHash = ([BitConverter]::ToString($messageHasher.ComputeHash([Text.Encoding]::UTF8.GetBytes($Message)))).Replace('-', '').ToLowerInvariant() }
finally { $messageHasher.Dispose() }
$runRoot = Join-Path $probeRoot $RelayId
$requestRecordFile = Join-Path $runRoot 'request.json'
$providerAckFile = Join-Path $runRoot 'provider-ack.json'
$finalResultFile = Join-Path $runRoot 'result.json'
if (Test-Path -LiteralPath $runRoot) {
    if (-not (Test-Path -LiteralPath $requestRecordFile -PathType Leaf)) { throw "Relay ID exists without a request record: $RelayId" }
    $requestRecord = Get-Content -Raw -LiteralPath $requestRecordFile | ConvertFrom-Json
    if ([string]$requestRecord.messageSha256 -ne $messageHash) { throw "Relay ID is already bound to a different message: $RelayId" }
    if (Test-Path -LiteralPath $finalResultFile -PathType Leaf) {
        $saved = Get-Content -Raw -LiteralPath $finalResultFile | ConvertFrom-Json
        if ($saved.status -eq 'ACKNOWLEDGED' -and $saved.relayId -eq $RelayId -and ($NoMailboxWrite -or $saved.mailboxAckRecorded)) {
            $saved | ConvertTo-Json -Depth 10
            exit 0
        }
    }
    if (Test-Path -LiteralPath $providerAckFile -PathType Leaf) {
        $saved = Get-Content -Raw -LiteralPath $providerAckFile | ConvertFrom-Json
        if ($saved.status -ne 'ACKNOWLEDGED' -or $saved.relayId -ne $RelayId) { throw "Relay provider acknowledgement is invalid: $RelayId" }
        if (-not $NoMailboxWrite) {
            if (-not $requestRecord.mailboxRequestRecorded) {
                $safeRequest = ConvertTo-RelayOneLine -Text $Message -MaximumLength 500
                Add-RelayMailboxEntry -RepositoryRoot $repoRoot -Entry "### [Codex -> MiniMax] @MiniMax auto-relay request ($RelayId)`n**Request**: $safeRequest"
                $requestRecord.mailboxRequestRecorded = $true
                Write-RelayAtomicJson -Path $requestRecordFile -Value $requestRecord
            }
            Add-RelayMailboxEntry -RepositoryRoot $repoRoot -Entry "### [MiniMax -> Codex] Auto-relay acknowledgement ($RelayId)`n**Status**: ACKNOWLEDGED. $($saved.summary) Next: $($saved.nextAction)"
            $saved.mailboxAckRecorded = $true
            Write-RelayAtomicJson -Path $providerAckFile -Value $saved
        }
        Write-RelayAtomicJson -Path $finalResultFile -Value $saved
        $saved | ConvertTo-Json -Depth 10
        exit 0
    }
    throw "Relay ID already exists without a successful acknowledgement: $RelayId"
}

$ledgerFile = Join-Path $stateRoot 'paid-probe-ledger.json'
$ledgerMutex = [Threading.Mutex]::new($false, "Local\DominionWarsAutoRelayPaidProbe-$([IO.Path]::GetFileName($stateRoot))")
$ledgerLock = $false
try {
    $ledgerLock = $ledgerMutex.WaitOne([TimeSpan]::FromSeconds(15))
    if (-not $ledgerLock) { throw 'Timed out waiting for the paid-probe ledger.' }
    $today = Get-Date -Format 'yyyy-MM-dd'
    $ledger = if (Test-Path -LiteralPath $ledgerFile -PathType Leaf) { Get-Content -Raw -LiteralPath $ledgerFile | ConvertFrom-Json } else { $null }
    if (-not $ledger -or [string]$ledger.date -ne $today) {
        $ledger = [pscustomobject]@{ date = $today; maxAttempts = 3; attempts = @() }
    }
    elseif ([int]$ledger.maxAttempts -ne 3 -or @($ledger.attempts).Count -gt 3) { throw 'Paid-probe ledger failed integrity validation.' }
    if (@($ledger.attempts).Count -ge 3) { throw 'The daily MiniMax relay probe limit (3 paid attempts) has been reached.' }
    $ledger.attempts = @($ledger.attempts) + @([pscustomobject]@{ relayId = $RelayId; messageSha256 = $messageHash; startedAt = [DateTime]::UtcNow.ToString('o') })
    Write-RelayAtomicJson -Path $ledgerFile -Value $ledger
}
finally {
    if ($ledgerLock) { $ledgerMutex.ReleaseMutex() }
    $ledgerMutex.Dispose()
}
New-Item -ItemType Directory -Path $runRoot | Out-Null
Set-RelayPrivateDirectoryAcl -Path $runRoot
$requestRecord = [ordered]@{
    relayId = $RelayId
    messageSha256 = $messageHash
    mailboxRequestRecorded = $false
    createdAt = [DateTime]::UtcNow.ToString('o')
}
Write-RelayAtomicJson -Path $requestRecordFile -Value $requestRecord

$systemPrompt = @"
You are MiniMax M3 acting as the temporary planning lead for Dominion Wars.
This is a transport-level relay acknowledgement, not permission to change product scope.
Return exactly one JSON object and no Markdown with this schema:
{"relayId":"$RelayId","status":"ACKNOWLEDGED","summary":"one concise plain-language sentence","nextAction":"one concise sentence"}
Echo the relayId exactly. Do not claim implementation or QA that is not stated in the message.
"@
$userMessage = "@MiniMax [$RelayId]`n$Message"
$messagesFile = Join-Path $runRoot 'messages.json'
$rawFile = Join-Path $runRoot 'raw.json'
$resultFile = Join-Path $runRoot 'command-result.json'
$specFile = Join-Path $runRoot 'command.json'
$messages = @(
    [ordered]@{ role = 'system'; content = $systemPrompt },
    [ordered]@{ role = 'user'; content = $userMessage }
)
Write-RelayAtomicJson -Path $messagesFile -Value $messages

if (-not $NoMailboxWrite) {
    $safeRequest = ConvertTo-RelayOneLine -Text $Message -MaximumLength 500
    Add-RelayMailboxEntry -RepositoryRoot $repoRoot -Entry "### [Codex -> MiniMax] @MiniMax auto-relay request ($RelayId)`n**Request**: $safeRequest"
    $script:requestMirrored = $true
    $requestRecord.mailboxRequestRecorded = $true
    Write-RelayAtomicJson -Path $requestRecordFile -Value $requestRecord
}

$arguments = @(
    'text', 'chat', '--model', 'MiniMax-M3', '--messages-file', $messagesFile,
    '--max-tokens', '512', '--temperature', '0.1', '--output', 'json',
    '--no-color', '--non-interactive', '--timeout', '115'
)
$spec = [ordered]@{
    command = $mmx
    arguments = $arguments
    workingDirectory = $repoRoot
    outputFile = $rawFile
    resultFile = $resultFile
    maxOutputChars = 50000
}
$ticketBytes = New-Object byte[] 32
$ticketRng = [Security.Cryptography.RandomNumberGenerator]::Create()
try { $ticketRng.GetBytes($ticketBytes) }
finally { $ticketRng.Dispose() }
$providerTicket = [Convert]::ToBase64String($ticketBytes)
[Array]::Clear($ticketBytes, 0, $ticketBytes.Length)
$ticketHasher = [Security.Cryptography.SHA256]::Create()
try { $spec['ticketSha256'] = ([BitConverter]::ToString($ticketHasher.ComputeHash([Text.Encoding]::UTF8.GetBytes($providerTicket)))).Replace('-', '').ToLowerInvariant() }
finally { $ticketHasher.Dispose() }
$spec['ticketExpiresAt'] = [DateTime]::UtcNow.AddMinutes(2).ToString('o')
Write-RelayAtomicJson -Path $specFile -Value $spec

$helper = Join-Path $PSScriptRoot 'invoke-provider-command.ps1'
$powershellExe = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
$startInfo = [Diagnostics.ProcessStartInfo]::new()
$startInfo.FileName = $powershellExe
$startInfo.Arguments = "-NoProfile -NonInteractive -ExecutionPolicy Bypass -File `"$helper`" -CommandSpecFile `"$specFile`""
$startInfo.WorkingDirectory = $repoRoot
$startInfo.UseShellExecute = $false
$startInfo.CreateNoWindow = $true
$allowedEnvironment = @(
    'PATH','SystemRoot','WINDIR','COMSPEC','PATHEXT','TEMP','TMP','USERPROFILE','LOCALAPPDATA','APPDATA',
    'PROGRAMDATA','ProgramFiles','ProgramFiles(x86)','CommonProgramFiles','CommonProgramFiles(x86)',
    'HOMEDRIVE','HOMEPATH','USERNAME','USERDOMAIN','NUMBER_OF_PROCESSORS','PROCESSOR_ARCHITECTURE','LANG'
)
foreach ($name in @($startInfo.EnvironmentVariables.Keys)) {
    if ($allowedEnvironment -notcontains [string]$name) { $startInfo.EnvironmentVariables.Remove([string]$name) }
}
$startInfo.EnvironmentVariables['DOMINION_RELAY_PROVIDER_TICKET'] = $providerTicket
$startInfo.EnvironmentVariables['MMX_CONFIG_DIR'] = $mmxConfigDirectory
$process = [Diagnostics.Process]::new()
$process.StartInfo = $startInfo
$timedOut = $false
try {
    if (-not $process.Start()) { throw 'Failed to start the MiniMax relay process.' }
    if (-not $process.WaitForExit(120000)) {
        $timedOut = $true
        & (Join-Path $env:SystemRoot 'System32\taskkill.exe') /PID $process.Id /T /F 2>&1 | Out-Null
        $null = $process.WaitForExit(10000)
    }
}
finally { $process.Dispose() }
$providerTicket = $null
if ($timedOut) { throw 'MiniMax relay timed out after 120 seconds and its process tree was terminated.' }
if (-not (Test-Path -LiteralPath $resultFile -PathType Leaf)) { throw 'MiniMax relay produced no command result.' }
$commandResult = Get-Content -Raw -LiteralPath $resultFile | ConvertFrom-Json
if ([int]$commandResult.exitCode -ne 0) { throw "MiniMax relay failed with exit code $($commandResult.exitCode)." }

$wrapper = Get-Content -Raw -LiteralPath $rawFile | ConvertFrom-Json
if (-not $wrapper.content -or -not $wrapper.content[0].text) { throw 'MiniMax response did not contain content[0].text.' }
if (-not $wrapper.usage) { throw 'MiniMax response did not include usage evidence.' }
$modelText = [string]$wrapper.content[0].text
$clean = $modelText.Trim()
if ($clean.StartsWith('```')) { $clean = [regex]::Replace($clean, '^```(?:json)?\s*|\s*```$', '', 'IgnoreCase').Trim() }
$ack = $clean | ConvertFrom-Json
if ([string]$ack.relayId -ne $RelayId -or [string]$ack.status -ne 'ACKNOWLEDGED') {
    throw 'MiniMax reply did not acknowledge the exact relay ID.'
}
$summary = ConvertTo-RelayOneLine -Text ([string]$ack.summary) -MaximumLength 600
$nextAction = ConvertTo-RelayOneLine -Text ([string]$ack.nextAction) -MaximumLength 600
if (-not $summary -or -not $nextAction) { throw 'MiniMax acknowledgement omitted its summary or next action.' }
$result = [ordered]@{
    relayId = $RelayId
    target = 'MiniMax'
    model = 'MiniMax-M3'
    status = 'ACKNOWLEDGED'
    messageSha256 = $messageHash
    summary = $summary
    nextAction = $nextAction
    usage = $wrapper.usage
    mailboxAckRecorded = $false
    completedAt = [DateTime]::UtcNow.ToString('o')
    rawResponse = $rawFile
}
Write-RelayAtomicJson -Path $providerAckFile -Value $result
if (-not $NoMailboxWrite) {
    Add-RelayMailboxEntry -RepositoryRoot $repoRoot -Entry "### [MiniMax -> Codex] Auto-relay acknowledgement ($RelayId)`n**Status**: ACKNOWLEDGED. $summary Next: $nextAction"
    $result.mailboxAckRecorded = $true
    Write-RelayAtomicJson -Path $providerAckFile -Value $result
    $script:requestMirrored = $false
}
Write-RelayAtomicJson -Path $finalResultFile -Value $result
$result | ConvertTo-Json -Depth 10
