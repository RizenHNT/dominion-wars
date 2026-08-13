[CmdletBinding()]
param(
    [switch]$Restore,
    [switch]$RunPython
)

$ErrorActionPreference = 'Stop'
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
Push-Location $repoRoot
try {
    $results = [System.Collections.Generic.List[object]]::new()

    function Invoke-RegressionStage {
        param(
            [Parameter(Mandatory)][string]$Name,
            [Parameter(Mandatory)][scriptblock]$Action,
            [switch]$Optional
        )

        $started = Get-Date
        $exitCode = 0
        $status = 'PASS'
        $detail = ''
        try {
            & $Action
            $exitCode = if ($null -eq $LASTEXITCODE) { 0 } else { [int]$LASTEXITCODE }
            if ($exitCode -ne 0) { $status = if ($Optional) { 'SKIPPED' } else { 'FAIL' } }
        }
        catch {
            $exitCode = 1
            $status = if ($Optional) { 'SKIPPED' } else { 'FAIL' }
            $detail = $_.Exception.Message
        }
        $seconds = [math]::Round(((Get-Date) - $started).TotalSeconds, 2)
        $results.Add([pscustomobject]@{
            stage = $Name
            status = $status
            exitCode = $exitCode
            seconds = $seconds
            detail = $detail
        })
        if ($status -eq 'FAIL') { throw "Regression stage failed: $Name (exit $exitCode)." }
    }

    $dotnetArgs = @('test', 'DominionWars.sln', '--nologo', '-c', 'Release')
    if (-not $Restore) { $dotnetArgs += '--no-restore' }
    Invoke-RegressionStage -Name 'dotnet-release' -Action { & dotnet @dotnetArgs }

    Invoke-RegressionStage -Name 'cards-schema' -Action {
        & powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass `
            -File (Join-Path $repoRoot 'scripts\validate-cards.ps1')
    }

    Invoke-RegressionStage -Name 'deck-validation' -Action {
        & powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass `
            -File (Join-Path $repoRoot 'scripts\validate-decks.ps1')
    }

    Invoke-RegressionStage -Name 'design-manifest' -Action {
        & powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass `
            -File (Join-Path $repoRoot 'scripts\validate-design-manifest.ps1')
    }

    Invoke-RegressionStage -Name 'java-build' -Action {
        & cmd.exe /d /c 'call scripts\build.bat'
    }

    Invoke-RegressionStage -Name 'java-regression' -Action {
        & java '-Dfile.encoding=UTF-8' '-cp' 'build\classes;build\test-classes' `
            'com.dominionwars.test.TestMain'
    }

    if ($RunPython) {
        $python = Get-Command python -ErrorAction SilentlyContinue
        if ($null -eq $python) {
            $results.Add([pscustomobject]@{ stage = 'python-alignment'; status = 'SKIPPED'; exitCode = 0; seconds = 0; detail = 'python executable not found' })
        }
        else {
            Invoke-RegressionStage -Name 'python-alignment' -Optional -Action {
                & $python.Source (Join-Path $repoRoot 'scripts\align_check.py')
            }
        }
    }
    else {
        $results.Add([pscustomobject]@{ stage = 'python-alignment'; status = 'SKIPPED'; exitCode = 0; seconds = 0; detail = 'not requested' })
    }

    # Unity requires an interactive Hub/Editor session on this machine. This is
    # intentionally a visible skip, never a synthetic pass.
    $results.Add([pscustomobject]@{
        stage = 'unity-editmode-and-windows'
        status = 'BLOCKED'
        exitCode = 0
        seconds = 0
        detail = 'requires interactive Unity Hub package resolution and license; not run by this offline gate'
    })

    $results | Format-Table stage,status,exitCode,seconds,detail -AutoSize
    $failed = @($results | Where-Object { $_.status -eq 'FAIL' })
    if ($failed.Count -gt 0) { exit 1 }
    exit 0
}
finally {
    Pop-Location
}
