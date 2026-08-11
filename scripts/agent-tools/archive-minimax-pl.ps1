[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$redirectingGitEnvironment = @(Get-ChildItem Env: | Where-Object {
        $_.Name -in @(
            'GIT_DIR', 'GIT_WORK_TREE', 'GIT_INDEX_FILE', 'GIT_OBJECT_DIRECTORY',
            'GIT_ALTERNATE_OBJECT_DIRECTORIES', 'GIT_CONFIG', 'GIT_CONFIG_SYSTEM',
            'GIT_CONFIG_GLOBAL', 'GIT_CONFIG_PARAMETERS'
        )
    })
if ($redirectingGitEnvironment.Count -gt 0) {
    throw "Refusing redirected Git environment: $($redirectingGitEnvironment.Name -join ', ')"
}

if (Test-Path Env:GIT_CONFIG_COUNT) {
    $configCount = 0
    if (-not [int]::TryParse($env:GIT_CONFIG_COUNT, [ref]$configCount) -or $configCount -lt 0 -or $configCount -gt 8) {
        throw 'Invalid GIT_CONFIG_COUNT environment value.'
    }
    for ($index = 0; $index -lt $configCount; $index++) {
        $key = [Environment]::GetEnvironmentVariable("GIT_CONFIG_KEY_$index")
        if ($key -ne 'safe.directory') {
            throw "Only sandbox-injected safe.directory Git configuration is accepted; found '$key'."
        }
    }
}

$gitCommand = Get-Command git.exe -CommandType Application -ErrorAction Stop | Select-Object -First 1
$script:GitExe = [IO.Path]::GetFullPath($gitCommand.Source)

function Invoke-Git {
    param(
        [Parameter(Mandatory)][string[]]$Arguments,
        [switch]$AllowFailure
    )

    $previousErrorPreference = $ErrorActionPreference
    try {
        $ErrorActionPreference = 'Continue'
        $output = @(& $script:GitExe -C $script:RepoRoot @Arguments 2>&1)
        $exitCode = $LASTEXITCODE
    }
    finally {
        $ErrorActionPreference = $previousErrorPreference
    }
    if (-not $AllowFailure -and $exitCode -ne 0) {
        throw "git $($Arguments -join ' ') failed:`n$($output -join "`n")"
    }
    return [pscustomobject]@{ ExitCode = $exitCode; Output = @($output) }
}

function Normalize-GitPath {
    param([Parameter(Mandatory)][string]$Path)
    return $Path.Trim().Replace('\', '/')
}

function Test-MiniMaxOwnedPath {
    param([Parameter(Mandatory)][string]$Path)

    $normalized = Normalize-GitPath $Path
    return (
        $normalized -match '^docs/PL_REPORT_\d{4}-\d{2}-\d{2}\.md$' -or
        $normalized -match '^docs/PROPOSAL_[A-Za-z0-9][A-Za-z0-9._-]{0,100}\.md$'
    )
}

function Get-GitLines {
    param([Parameter(Mandatory)][string[]]$Arguments)
    return @((Invoke-Git -Arguments $Arguments).Output) |
        ForEach-Object { Normalize-GitPath $_ } |
        Where-Object { $_ }
}

$script:RepoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..'))
$selfPath = 'scripts/agent-tools/archive-minimax-pl.ps1'
$selfFullPath = [IO.Path]::GetFullPath((Join-Path $script:RepoRoot $selfPath))
if ($script:GitExe.StartsWith($script:RepoRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'Refusing a repository-local git.exe.'
}

$reportedRoot = ((Invoke-Git -Arguments @('rev-parse', '--show-toplevel')).Output -join '').Trim()
if (-not $reportedRoot) { throw 'Git did not return a repository root.' }
$reportedRoot = [IO.Path]::GetFullPath($reportedRoot)
if (-not $reportedRoot.Equals($script:RepoRoot, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Repository root mismatch. Expected '$script:RepoRoot', got '$reportedRoot'."
}

$trackedSelf = Invoke-Git -Arguments @('ls-files', '--error-unmatch', '--', $selfPath) -AllowFailure
$selfFlags = ((Invoke-Git -Arguments @('ls-files', '-v', '--', $selfPath) -AllowFailure).Output -join '').Trim()
$headSelfHash = ((Invoke-Git -Arguments @('rev-parse', "HEAD:$selfPath") -AllowFailure).Output -join '').Trim()
$workingSelfHash = ((Invoke-Git -Arguments @('hash-object', '--path', $selfPath, $selfFullPath) -AllowFailure).Output -join '').Trim()
if (
    $trackedSelf.ExitCode -ne 0 -or
    -not $selfFlags.StartsWith('H ') -or
    -not $headSelfHash -or
    $workingSelfHash -ne $headSelfHash
) {
    throw 'The PL archive guard is not the reviewed version from HEAD. Ask Codex to restore or commit it.'
}

$branch = ((Invoke-Git -Arguments @('branch', '--show-current')).Output -join '').Trim()
if ($branch -ne 'main') { throw "PL closeout is allowed only on main, not '$branch'." }

$gitDirText = ((Invoke-Git -Arguments @('rev-parse', '--git-dir')).Output -join '').Trim()
$gitDir = if ([IO.Path]::IsPathRooted($gitDirText)) {
    [IO.Path]::GetFullPath($gitDirText)
} else {
    [IO.Path]::GetFullPath((Join-Path $script:RepoRoot $gitDirText))
}
$commonDirText = ((Invoke-Git -Arguments @('rev-parse', '--git-common-dir')).Output -join '').Trim()
$commonDir = if ([IO.Path]::IsPathRooted($commonDirText)) {
    [IO.Path]::GetFullPath($commonDirText)
} else {
    [IO.Path]::GetFullPath((Join-Path $script:RepoRoot $commonDirText))
}

$sha = [Security.Cryptography.SHA256]::Create()
try {
    $mutexSuffix = [BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($commonDir.ToUpperInvariant()))).Replace('-', '').Substring(0, 32)
}
finally {
    $sha.Dispose()
}
$mutex = [Threading.Mutex]::new($false, "Local\DominionWarsMiniMaxArchive-$mutexSuffix")
$mutexHeld = $false
try {
    $mutexHeld = $mutex.WaitOne(0)
    if (-not $mutexHeld) { throw 'Another PL archive operation is already running.' }

foreach ($marker in @('MERGE_HEAD', 'CHERRY_PICK_HEAD', 'REVERT_HEAD', 'rebase-apply', 'rebase-merge')) {
    if (Test-Path -LiteralPath (Join-Path $gitDir $marker)) {
        throw "Refusing to archive while Git operation '$marker' is active."
    }
}

$customHooks = Invoke-Git -Arguments @('config', '--get', 'core.hooksPath') -AllowFailure
if ($customHooks.ExitCode -eq 0 -and (($customHooks.Output -join '').Trim())) {
    throw 'Refusing to auto-commit while core.hooksPath is configured.'
}
$hooksDir = Join-Path $commonDir 'hooks'
if (Test-Path -LiteralPath $hooksDir) {
    $activeHooks = @(Get-ChildItem -LiteralPath $hooksDir -File -ErrorAction Stop |
        Where-Object { $_.Name -notlike '*.sample' })
    if ($activeHooks.Count -gt 0) {
        throw "Refusing to auto-commit with active Git hooks: $($activeHooks.Name -join ', ')"
    }
}

$alreadyStaged = @(Get-GitLines -Arguments @('diff', '--cached', '--name-only'))
if ($alreadyStaged.Count -gt 0) {
    throw "Refusing to mix with existing staged changes: $($alreadyStaged -join ', ')"
}

$deletedOrRenamed = @(
    @(Get-GitLines -Arguments @('diff', 'HEAD', '--name-only', '--diff-filter=DR')) |
        Where-Object { Test-MiniMaxOwnedPath $_ }
)
if ($deletedOrRenamed.Count -gt 0) {
    throw "PL closeout cannot delete or rename files: $($deletedOrRenamed -join ', ')"
}

$tracked = @(Get-GitLines -Arguments @('diff', 'HEAD', '--name-only', '--diff-filter=ACMRTUXB'))
$untracked = @(Get-GitLines -Arguments @('ls-files', '--others', '--exclude-standard'))
$eligible = @(
    @($tracked + $untracked) |
        Where-Object { Test-MiniMaxOwnedPath $_ } |
        Sort-Object -Unique
)

if ($eligible.Count -eq 0) {
    Write-Output 'NO_CHANGES: no MiniMax-owned PL report or proposal needs archiving.'
    exit 0
}
if ($eligible.Count -gt 20) {
    throw "Refusing to archive more than 20 MiniMax documents at once (found $($eligible.Count))."
}

foreach ($relativePath in $eligible) {
    $fullPath = [IO.Path]::GetFullPath((Join-Path $script:RepoRoot $relativePath))
    if (-not $fullPath.StartsWith($script:RepoRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Path escaped the repository: $relativePath"
    }
    $item = Get-Item -LiteralPath $fullPath -Force
    if ($item.PSIsContainer -or ($item.Attributes -band [IO.FileAttributes]::ReparsePoint)) {
        throw "Only regular planning files may be archived: $relativePath"
    }
    if ($item.Length -gt 1MB) { throw "Planning file exceeds 1 MiB: $relativePath" }
    $bytes = [IO.File]::ReadAllBytes($fullPath)
    if ($bytes -contains 0) { throw "Binary content is not allowed in planning files: $relativePath" }
    $header = (Get-Content -LiteralPath $fullPath -TotalCount 40) -join "`n"
    if ($header -notmatch '(?im)^>[^\r\n]*MiniMax[^\r\n]*$') {
        throw "Planning ownership marker is missing from the first 40 lines: $relativePath"
    }
    $filter = ((Invoke-Git -Arguments @('check-attr', 'filter', '--', $relativePath)).Output -join '').Trim()
    if ($filter -notmatch ': filter: unspecified$') {
        throw "Git clean filters are not allowed for auto-archived planning files: $relativePath"
    }
}

$startingHead = ((Invoke-Git -Arguments @('rev-parse', 'HEAD')).Output -join '').Trim()
$committed = $false
try {
    $null = Invoke-Git -Arguments (@('add', '--') + $eligible)

    $stagedNow = @(
        @(Get-GitLines -Arguments @('diff', '--cached', '--name-only')) |
            Sort-Object -Unique
    )
    $pathMismatch = @(Compare-Object -ReferenceObject $eligible -DifferenceObject $stagedNow)
    if ($pathMismatch.Count -gt 0 -or @($stagedNow | Where-Object { -not (Test-MiniMaxOwnedPath $_) }).Count -gt 0) {
        throw "Staged paths do not exactly match the approved PL documents. Expected: $($eligible -join ', '); actual: $($stagedNow -join ', ')"
    }

    $headBeforeCommit = ((Invoke-Git -Arguments @('rev-parse', 'HEAD')).Output -join '').Trim()
    if ($headBeforeCommit -ne $startingHead) { throw 'HEAD changed while preparing the PL archive.' }
    $diffCheck = Invoke-Git -Arguments @('diff', '--cached', '--check') -AllowFailure
    if ($diffCheck.ExitCode -ne 0) {
        throw "PL archive failed git diff --check:`n$($diffCheck.Output -join "`n")"
    }

    $message = 'PL archive {0}' -f (Get-Date -Format 'yyyy-MM-dd HH:mm')
    $null = Invoke-Git -Arguments (@('-c', 'commit.gpgSign=false', 'commit', '--only', '-m', $message, '--') + $eligible)
    $committed = $true

    $parent = ((Invoke-Git -Arguments @('rev-parse', 'HEAD^')).Output -join '').Trim()
    if ($parent -ne $startingHead) {
        throw 'The PL archive commit was not based on the reviewed starting HEAD. Stop and ask Codex to inspect it.'
    }

    $committedPaths = @(
        @(Get-GitLines -Arguments @('diff-tree', '--no-commit-id', '--name-only', '-r', 'HEAD')) |
            Sort-Object -Unique
    )
    if (@(Compare-Object -ReferenceObject $eligible -DifferenceObject $committedPaths).Count -gt 0) {
        throw 'The completed commit contains paths outside the reviewed PL archive set. Stop and ask Codex to inspect it.'
    }

    $commit = ((Invoke-Git -Arguments @('rev-parse', '--short', 'HEAD')).Output -join '').Trim()
    Write-Output "ARCHIVED: local commit $commit contains only MiniMax-owned PL documents. No push was performed."
}
finally {
    if (-not $committed) {
        $headNow = ((Invoke-Git -Arguments @('rev-parse', 'HEAD') -AllowFailure).Output -join '').Trim()
        if ($headNow -eq $startingHead) {
            $stagedAfterFailure = @(Get-GitLines -Arguments @('diff', '--cached', '--name-only'))
            $extraStaged = @($stagedAfterFailure | Where-Object { $_ -notin $eligible })
            if ($extraStaged.Count -eq 0) {
                $restore = Invoke-Git -Arguments (@('restore', '--staged', '--') + $eligible) -AllowFailure
                if ($restore.ExitCode -ne 0) {
                    Write-Error "Failed to restore the PL archive index after an error: $($restore.Output -join "`n")"
                }
            }
        }
    }
}
}
finally {
    if ($mutexHeld) { $mutex.ReleaseMutex() }
    $mutex.Dispose()
}
