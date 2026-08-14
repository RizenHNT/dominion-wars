[CmdletBinding()]
param(
    [object]$Runs = '20,100,1000',
    [int]$TimeoutSeconds = 300,
    [switch]$SkipBuild,
    [string]$OutputPath
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
Push-Location $repoRoot
try {
    function Expand-RunTokens {
        param(
            [AllowNull()][object]$Value
        )

        if ($null -eq $Value) {
            Write-Output -NoEnumerate ''
            return
        }
        if ($Value -is [string]) {
            foreach ($token in ([string]$Value -split ',')) {
                Write-Output -NoEnumerate $token
            }
            return
        }
        if ($Value -is [Collections.IEnumerable]) {
            foreach ($item in $Value) {
                Expand-RunTokens -Value $item
            }
            return
        }
        Write-Output -NoEnumerate ([Convert]::ToString($Value, [Globalization.CultureInfo]::InvariantCulture))
    }

    $runTokens = @(Expand-RunTokens -Value $Runs)
    $runValues = @(
        foreach ($rawToken in $runTokens) {
            $value = ([string]$rawToken).Trim()
            $parsed = 0
            if ([string]::IsNullOrWhiteSpace($value) -or -not [int]::TryParse($value, [Globalization.NumberStyles]::Integer, [Globalization.CultureInfo]::InvariantCulture, [ref]$parsed)) {
                throw "Runs must be a comma-separated list of integers: $Runs"
            }
            $parsed
        }
    )
    if ($runValues.Count -eq 0 -or @($runValues | Where-Object { $_ -lt 1 -or $_ -gt 10000 }).Count -gt 0) {
        throw 'Runs must contain values from 1 through 10000.'
    }
    if ($TimeoutSeconds -lt 1 -or $TimeoutSeconds -gt 3600) {
        throw 'TimeoutSeconds must be between 1 and 3600.'
    }

    function Invoke-ProcessWithTimeout {
        param(
            [Parameter(Mandatory)][string]$FilePath,
            [Parameter(Mandatory)][string[]]$ArgumentList,
            [Parameter(Mandatory)][int]$Timeout,
            [Parameter(Mandatory)][string]$Label
        )

        $stdout = Join-Path ([IO.Path]::GetTempPath()) ("dw-stability-{0}-{1}.out" -f $PID, [Guid]::NewGuid().ToString('N'))
        $stderr = Join-Path ([IO.Path]::GetTempPath()) ("dw-stability-{0}-{1}.err" -f $PID, [Guid]::NewGuid().ToString('N'))
        $start = Get-Date
        $process = $null
        try {
            $psi = [Diagnostics.ProcessStartInfo]::new()
            $psi.FileName = $FilePath
            $psi.WorkingDirectory = $repoRoot
            $psi.UseShellExecute = $false
            $psi.CreateNoWindow = $true
            $psi.RedirectStandardOutput = $true
            $psi.RedirectStandardError = $true
            $utf8 = [System.Text.UTF8Encoding]::new($false)
            $psi.StandardOutputEncoding = $utf8
            $psi.StandardErrorEncoding = $utf8
            # All callers below use repository-relative, space-free arguments;
            # keeping the command line literal also preserves cmd.exe /c semantics
            # under Windows PowerShell 5.1.
            $psi.Arguments = $ArgumentList -join ' '
            $process = [Diagnostics.Process]::new()
            $process.StartInfo = $psi
            if (-not $process.Start()) { throw "Could not start $Label." }
            $stdoutTask = $process.StandardOutput.ReadToEndAsync()
            $stderrTask = $process.StandardError.ReadToEndAsync()
            if (-not $process.WaitForExit($Timeout * 1000)) {
                try { $process.Kill() } catch { }
                $process.WaitForExit()
                return [pscustomobject]@{ label = $Label; status = 'TIMEOUT'; exitCode = 124; seconds = $Timeout; output = ''; error = '' }
            }
            $stdoutText = $stdoutTask.GetAwaiter().GetResult()
            $stderrText = $stderrTask.GetAwaiter().GetResult()
            return [pscustomobject]@{
                label = $Label
                status = if ($process.ExitCode -eq 0) { 'PASS' } else { 'FAIL' }
                exitCode = $process.ExitCode
                seconds = [math]::Round(((Get-Date) - $start).TotalSeconds, 2)
                output = $stdoutText
                error = $stderrText
            }
        }
        finally {
            if ($process) { $process.Dispose() }
            Remove-Item -LiteralPath $stdout, $stderr -Force -ErrorAction SilentlyContinue
        }
    }

    $results = [Collections.Generic.List[object]]::new()
    if (-not $SkipBuild) {
        $build = Invoke-ProcessWithTimeout -FilePath 'cmd.exe' -ArgumentList @('/d', '/c', 'call scripts\build.bat') -Timeout $TimeoutSeconds -Label 'java-build'
        $results.Add($build)
        if ($build.status -ne 'PASS') { throw "Java build did not pass: $($build.status) exit=$($build.exitCode)" }
    }

    foreach ($run in $runValues) {
        $result = Invoke-ProcessWithTimeout -FilePath 'java' -ArgumentList @('-Dfile.encoding=UTF-8', '-Dstdout.encoding=UTF-8', '-Dstderr.encoding=UTF-8', '-cp', 'build\classes;build\test-classes', 'com.dominionwars.test.SimMain', [string]$run) -Timeout $TimeoutSeconds -Label ("java-sim-{0}" -f $run)
        $results.Add($result)
        if ($result.status -eq 'PASS') {
            $line = ($result.output -split "`r?`n" | Where-Object { $_ -match '240|1200|12000|局|games|回合' } | Select-Object -Last 1)
            if (-not $line) {
                $line = ($result.output -split "`r?`n" | Where-Object { $_.Trim().StartsWith('==') -and $_.Trim().EndsWith('==') } | Select-Object -Last 1)
            }
            $summary = (@($line) -replace '\s+', ' ') -join ' '
            Write-Output ("STABILITY run={0} status=PASS exit=0 seconds={1} summary={2}" -f $run, $result.seconds, $summary.Trim())
        }
        else {
            Write-Output ("STABILITY run={0} status={1} exit={2} seconds={3}" -f $run, $result.status, $result.exitCode, $result.seconds)
        }
    }

    if ($OutputPath) {
        $fullOutput = [IO.Path]::GetFullPath($OutputPath)
        $parent = Split-Path -Parent $fullOutput
        if (-not (Test-Path -LiteralPath $parent)) { New-Item -ItemType Directory -Path $parent -Force | Out-Null }
        $results | Select-Object label,status,exitCode,seconds | ConvertTo-Json -Depth 3 | Set-Content -LiteralPath $fullOutput -Encoding UTF8
        Write-Output ("STABILITY_REPORT path={0}" -f $fullOutput)
    }

    if (@($results | Where-Object status -ne 'PASS').Count -gt 0) { exit 1 }
    exit 0
}
finally {
    Pop-Location
}
