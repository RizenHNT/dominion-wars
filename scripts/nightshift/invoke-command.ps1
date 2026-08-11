[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidateNotNullOrEmpty()][string]$CommandSpecFile
)

$ErrorActionPreference = 'Stop'
$spec = Get-Content -Raw -LiteralPath $CommandSpecFile | ConvertFrom-Json
$command = [string]$spec.command
$arguments = @($spec.arguments | ForEach-Object { [string]$_ })
$workingDirectory = [string]$spec.workingDirectory
$outputFile = [string]$spec.outputFile
$resultFile = [string]$spec.resultFile
$startGateFile = [string]$spec.startGateFile
$maxOutputChars = [int]$spec.maxOutputChars
$sanitizeEnvironment = [bool]$spec.sanitizeEnvironment

if (-not $command -or -not (Test-Path -LiteralPath $workingDirectory -PathType Container)) {
    throw 'Invalid command specification.'
}
if ($maxOutputChars -lt 1000) { throw 'maxOutputChars is unreasonably small.' }
if (-not $startGateFile) { throw 'Command specification has no start gate.' }

$gateDeadline = [DateTime]::UtcNow.AddSeconds(30)
while (-not (Test-Path -LiteralPath $startGateFile -PathType Leaf)) {
    if ([DateTime]::UtcNow -ge $gateDeadline) { throw 'Parent did not release the command start gate.' }
    Start-Sleep -Milliseconds 50
}

if ($sanitizeEnvironment) {
    $allowedEnvironmentNames = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($name in @(
        'PATH', 'SystemRoot', 'WINDIR', 'COMSPEC', 'PATHEXT',
        'TEMP', 'TMP', 'TMPDIR', 'USERPROFILE', 'LOCALAPPDATA', 'APPDATA', 'PROGRAMDATA',
        'ProgramFiles', 'ProgramFiles(x86)', 'CommonProgramFiles', 'CommonProgramFiles(x86)',
        'HOMEDRIVE', 'HOMEPATH', 'USERNAME', 'USERDOMAIN', 'LOGONSERVER',
        'NUMBER_OF_PROCESSORS', 'PROCESSOR_ARCHITECTURE', 'PROCESSOR_IDENTIFIER',
        'JAVA_HOME', 'CODEX_HOME', 'LANG', 'PYTHONUTF8', 'PYTHONIOENCODING'
    )) { $null = $allowedEnvironmentNames.Add($name) }
    foreach ($entry in Get-ChildItem Env:) {
        if (-not $allowedEnvironmentNames.Contains([string]$entry.Name)) {
            [Environment]::SetEnvironmentVariable([string]$entry.Name, $null, [EnvironmentVariableTarget]::Process)
        }
    }
}

if ($spec.environment) {
    foreach ($property in $spec.environment.PSObject.Properties) {
        [Environment]::SetEnvironmentVariable([string]$property.Name, [string]$property.Value, [EnvironmentVariableTarget]::Process)
    }
}

Set-Location -LiteralPath $workingDirectory
$utf8 = [System.Text.UTF8Encoding]::new($false)
$writer = [System.IO.StreamWriter]::new($outputFile, $false, $utf8)
$written = 0
$truncated = $false
$exitCode = 1
$failure = ''

try {
    $resolvedCommand = Get-Command -Name $command -ErrorAction Stop
    $isNativeCommand = $resolvedCommand.CommandType -eq [Management.Automation.CommandTypes]::Application
    $global:LASTEXITCODE = $null
    $previousPreference = $ErrorActionPreference
    $previousNativePreference = $PSNativeCommandUseErrorActionPreference
    $ErrorActionPreference = 'Continue'
    $PSNativeCommandUseErrorActionPreference = $false
    try {
        & $command @arguments 2>&1 | ForEach-Object {
            $text = ($_ | Out-String).TrimEnd("`r", "`n")
            if ($written -lt $maxOutputChars) {
                $remaining = $maxOutputChars - $written
                $chunk = if ($text.Length -le $remaining) { $text } else { $text.Substring(0, $remaining) }
                $writer.WriteLine($chunk)
                $written += $chunk.Length + [Environment]::NewLine.Length
                if ($chunk.Length -lt $text.Length) { $truncated = $true }
            }
            else {
                $truncated = $true
            }
        }
        $commandSucceeded = $?
        if (-not $commandSucceeded) {
            $exitCode = if ($null -ne $LASTEXITCODE -and [int]$LASTEXITCODE -ne 0) { [int]$LASTEXITCODE } else { 1 }
        }
        elseif ($isNativeCommand -and $null -ne $LASTEXITCODE) { $exitCode = [int]$LASTEXITCODE }
        else { $exitCode = 0 }
    }
    finally {
        $ErrorActionPreference = $previousPreference
        $PSNativeCommandUseErrorActionPreference = $previousNativePreference
    }
}
catch {
    $failure = $_.Exception.Message
    $writer.WriteLine("COMMAND WRAPPER ERROR: $failure")
    $exitCode = 1
}
finally {
    if ($truncated) { $writer.WriteLine('[OUTPUT TRUNCATED BY NIGHT-SHIFT LIMIT]') }
    $writer.Dispose()
}

$result = [ordered]@{
    exitCode = $exitCode
    outputTruncated = $truncated
    failure = $failure
}
[System.IO.File]::WriteAllText($resultFile, ($result | ConvertTo-Json -Depth 5), $utf8)
exit 0
