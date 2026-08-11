[CmdletBinding()]
param([Parameter(Mandatory)][ValidateNotNullOrEmpty()][string]$CommandSpecFile)

$ErrorActionPreference = 'Stop'
$spec = Get-Content -Raw -LiteralPath $CommandSpecFile | ConvertFrom-Json
if (-not $spec.command -or -not $spec.outputFile -or -not $spec.resultFile -or -not $spec.workingDirectory) {
    throw 'Invalid provider command specification.'
}
$trustedState = [IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA 'DominionWarsAutoRelay\state')).TrimEnd('\', '/')
$specPath = [IO.Path]::GetFullPath($CommandSpecFile)
$specDirectory = [IO.Path]::GetDirectoryName($specPath).TrimEnd('\', '/')
if (-not $specPath.StartsWith($trustedState + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Provider command spec is outside private relay state.' }
foreach ($path in @([string]$spec.outputFile, [string]$spec.resultFile)) {
    $full = [IO.Path]::GetFullPath($path)
    if ([IO.Path]::GetDirectoryName($full).TrimEnd('\', '/') -ne $specDirectory) { throw 'Provider output escaped its relay run directory.' }
}
$expectedCommands = @(
    [IO.Path]::GetFullPath((Join-Path $env:APPDATA 'npm\mmx.cmd')),
    [IO.Path]::GetFullPath((Join-Path $env:APPDATA 'npm\mmx.ps1'))
)
$actualCommand = [IO.Path]::GetFullPath([string]$spec.command)
if (-not ($expectedCommands | Where-Object { $_.Equals($actualCommand, [StringComparison]::OrdinalIgnoreCase) })) {
    throw 'Provider helper only permits the installed MiniMax CLI.'
}
$arguments = @($spec.arguments | ForEach-Object { [string]$_ })
if ($arguments.Count -ne 16 -or $arguments[0] -ne 'text' -or $arguments[1] -ne 'chat' -or
    $arguments[2] -ne '--model' -or $arguments[3] -ne 'MiniMax-M3' -or $arguments[4] -ne '--messages-file' -or
    $arguments[6] -ne '--max-tokens' -or $arguments[7] -ne '512' -or $arguments[8] -ne '--temperature' -or
    $arguments[9] -ne '0.1' -or $arguments[10] -ne '--output' -or $arguments[11] -ne 'json' -or
    $arguments[12] -ne '--no-color' -or $arguments[13] -ne '--non-interactive' -or
    $arguments[14] -ne '--timeout' -or $arguments[15] -ne '115') { throw 'Provider arguments are outside the MiniMax relay allowlist.' }
$messagesFile = [IO.Path]::GetFullPath($arguments[5])
if ([IO.Path]::GetDirectoryName($messagesFile).TrimEnd('\', '/') -ne $specDirectory -or -not (Test-Path -LiteralPath $messagesFile -PathType Leaf)) {
    throw 'MiniMax messages file escaped its relay run directory.'
}
if ([int]$spec.maxOutputChars -ne 50000) { throw 'Provider output limit differs from the reviewed relay value.' }
$ticket = [string]$env:DOMINION_RELAY_PROVIDER_TICKET
if (-not $ticket -or [string]$spec.ticketSha256 -notmatch '^[0-9a-f]{64}$') { throw 'Provider helper is missing its one-time parent ticket.' }
$ticketExpiry = [DateTime]::MinValue
if (-not [DateTime]::TryParse([string]$spec.ticketExpiresAt, [Globalization.CultureInfo]::InvariantCulture, [Globalization.DateTimeStyles]::RoundtripKind, [ref]$ticketExpiry) -or
    $ticketExpiry.ToUniversalTime() -lt [DateTime]::UtcNow -or $ticketExpiry.ToUniversalTime() -gt [DateTime]::UtcNow.AddMinutes(5)) {
    throw 'Provider helper ticket is expired or malformed.'
}
$ticketHasher = [Security.Cryptography.SHA256]::Create()
try { $actualTicketHash = ([BitConverter]::ToString($ticketHasher.ComputeHash([Text.Encoding]::UTF8.GetBytes($ticket)))).Replace('-', '').ToLowerInvariant() }
finally { $ticketHasher.Dispose() }
if (-not $actualTicketHash.Equals([string]$spec.ticketSha256, [StringComparison]::Ordinal)) { throw 'Provider helper ticket did not match its parent invocation.' }
$ticketUsed = Join-Path $specDirectory 'provider-ticket.used'
$ticketStream = $null
try { $ticketStream = [IO.File]::Open($ticketUsed, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None) }
catch { throw 'Provider helper ticket was already consumed.' }
finally { if ($ticketStream) { $ticketStream.Dispose() } }
[Environment]::SetEnvironmentVariable('DOMINION_RELAY_PROVIDER_TICKET', $null, [EnvironmentVariableTarget]::Process)
$ticket = $null
$expectedMmxConfigDirectory = [IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA 'DominionWarsAutoRelay\mmx')).TrimEnd('\', '/')
$actualMmxConfigDirectory = [IO.Path]::GetFullPath([string]$env:MMX_CONFIG_DIR).TrimEnd('\', '/')
if (-not $actualMmxConfigDirectory.Equals($expectedMmxConfigDirectory, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'MiniMax relay configuration directory differs from the reviewed private path.'
}
Set-Location -LiteralPath ([string]$spec.workingDirectory)
$output = ''
$exitCode = 1
$failure = ''
try {
    $global:LASTEXITCODE = $null
    $records = @(& $actualCommand @arguments 2>&1)
    $succeeded = $?
    $output = ($records | Out-String)
    if ($output.Length -gt [int]$spec.maxOutputChars) {
        $output = $output.Substring(0, [int]$spec.maxOutputChars) + "`n[OUTPUT TRUNCATED]"
        $exitCode = 125
    }
    elseif (-not $succeeded) { $exitCode = if ($LASTEXITCODE) { [int]$LASTEXITCODE } else { 1 } }
    elseif ($null -ne $LASTEXITCODE) { $exitCode = [int]$LASTEXITCODE }
    else { $exitCode = 0 }
}
catch {
    $failure = $_.Exception.Message
    $output = "PROVIDER WRAPPER ERROR: $failure"
    $exitCode = 1
}
[IO.File]::WriteAllText([string]$spec.outputFile, $output, [Text.UTF8Encoding]::new($false))
[IO.File]::WriteAllText([string]$spec.resultFile, ([ordered]@{ exitCode = $exitCode; failure = $failure } | ConvertTo-Json), [Text.UTF8Encoding]::new($false))
exit 0
