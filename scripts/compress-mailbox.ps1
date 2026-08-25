<#
.SYNOPSIS
    AI_MAILBOX.md lazy compression script.
#>
[CmdletBinding()]
param(
    [int]$MaxLines = 400,
    [int]$ArchiveMaxLines = 5000,
    [int]$ArchiveRollLines = 4000,
    [string]$RepositoryRoot,
    [switch]$DryRun,
    [switch]$Force
)

$ErrorActionPreference = 'Stop'
try { [Console]::OutputEncoding = [System.Text.Encoding]::UTF8 } catch { }
if ($MaxLines -lt 1) { throw 'MaxLines must be at least 1.' }
if ($ArchiveRollLines -lt 0) { throw 'ArchiveRollLines must be at least 0.' }
if ($ArchiveMaxLines -le $ArchiveRollLines) { throw 'ArchiveMaxLines must be greater than ArchiveRollLines.' }

if ([string]::IsNullOrWhiteSpace($RepositoryRoot)) {
    $repoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
} else {
    $repoRoot = (Resolve-Path -LiteralPath $RepositoryRoot).Path
}
$mailboxPath = Join-Path $repoRoot 'docs\AI_MAILBOX.md'
$archivePath = Join-Path $repoRoot 'docs\AI_MAILBOX_ARCHIVE.md'
$v2Path = Join-Path $repoRoot 'docs\AI_MAILBOX_ARCHIVE_v2.md'
$gitPath = Join-Path $repoRoot '.git'
$lockPath = Join-Path $gitPath 'ai-mailbox-compress.lock'
$backupRoot = Join-Path $repoRoot '.nightshift\mailbox-backups'
$utf8NoBom = New-Object System.Text.UTF8Encoding($false)
$lockStream = $null
$tempPaths = New-Object 'System.Collections.Generic.List[string]'
$backupDir = $null
$records = @()
$committed = $false
$red = [char]::ConvertFromUtf32(0x1F534)
$yellow = [char]::ConvertFromUtf32(0x1F7E1)
$green = [char]::ConvertFromUtf32(0x1F7E2)
$white = [char]::ConvertFromUtf32(0x26AA)
$blue = [char]::ConvertFromUtf32(0x1F535)

function Split-Sections([string[]]$Source) {
    $header = New-Object 'System.Collections.Generic.List[string]'
    $sections = New-Object 'System.Collections.Generic.List[object]'
    $current = New-Object 'System.Collections.Generic.List[string]'
    $inHeader = $true
    foreach ($line in $Source) {
        if ($line -match '^##\s') {
            if (-not $inHeader -and $current.Count -gt 0) {
                $sections.Add(@{ Title = $current[0]; Lines = $current })
                $current = New-Object 'System.Collections.Generic.List[string]'
            } elseif ($inHeader) { $inHeader = $false }
            $current.Add($line)
        } elseif ($inHeader) { $header.Add($line) } else { $current.Add($line) }
    }
    if (-not $inHeader -and $current.Count -gt 0) {
        $sections.Add(@{ Title = $current[0]; Lines = $current })
    }
    [pscustomobject]@{ Header = $header; Sections = $sections }
}

function New-FlushedTemp([string]$Target, [System.Collections.IEnumerable]$Lines) {
    $dir = [System.IO.Path]::GetDirectoryName($Target)
    do { $temp = Join-Path $dir ('.{0}.{1}.tmp' -f [System.IO.Path]::GetFileName($Target), [guid]::NewGuid().ToString('N')) } while (Test-Path -LiteralPath $temp)
    $tempPaths.Add($temp)
    $stream = $null; $writer = $null
    try {
        $stream = [System.IO.FileStream]::new($temp, [System.IO.FileMode]::CreateNew, [System.IO.FileAccess]::Write, [System.IO.FileShare]::None)
        $writer = [System.IO.StreamWriter]::new($stream, $utf8NoBom, 4096, $true)
        foreach ($line in $Lines) { [void]$writer.WriteLine([string]$line) }
        $writer.Flush(); $stream.Flush($true)
    } finally {
        if ($null -ne $writer) { $writer.Dispose() }
        if ($null -ne $stream) { $stream.Dispose() }
    }
    $temp
}

function New-CopyTemp([string]$Source, [string]$Target) {
    $dir = [System.IO.Path]::GetDirectoryName($Target)
    do { $temp = Join-Path $dir ('.{0}.{1}.restore.tmp' -f [System.IO.Path]::GetFileName($Target), [guid]::NewGuid().ToString('N')) } while (Test-Path -LiteralPath $temp)
    $tempPaths.Add($temp)
    $sourceStream = $null; $targetStream = $null
    try {
        $sourceStream = [System.IO.File]::Open($Source, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read, [System.IO.FileShare]::Read)
        $targetStream = [System.IO.FileStream]::new($temp, [System.IO.FileMode]::CreateNew, [System.IO.FileAccess]::Write, [System.IO.FileShare]::None)
        $sourceStream.CopyTo($targetStream); $targetStream.Flush($true)
    } finally {
        if ($null -ne $targetStream) { $targetStream.Dispose() }
        if ($null -ne $sourceStream) { $sourceStream.Dispose() }
    }
    $temp
}

function Replace-Target($Record, [string]$Temp) {
    $tempPath = [string]$Temp
    $targetPath = [string]$Record.Path
    if ([string]::IsNullOrWhiteSpace($tempPath) -or -not [System.IO.Path]::IsPathRooted($tempPath)) { throw ('Invalid replacement temp path: {0}' -f $tempPath) }
    if ([string]::IsNullOrWhiteSpace($targetPath) -or -not [System.IO.Path]::IsPathRooted($targetPath)) { throw ('Invalid replacement target path: {0}' -f $targetPath) }
    if ($Record.Exists) {
        $backupPath = [string]$Record.Backup
        if ([string]::IsNullOrWhiteSpace($backupPath) -or -not [System.IO.Path]::IsPathRooted($backupPath)) { throw ('Invalid replacement backup path: {0}' -f $backupPath) }
        [System.IO.File]::Replace($tempPath, $targetPath, $backupPath)
    }
    else { [System.IO.File]::Move($tempPath, $targetPath) }
    $Record.Applied = $true
}

function Restore-Targets($TargetRecords) {
    $errors = New-Object 'System.Collections.Generic.List[string]'
    for ($i = $TargetRecords.Count - 1; $i -ge 0; $i--) {
        $record = $TargetRecords[$i]
        try {
            if ($record.Exists) {
                $restoreTemp = New-CopyTemp $record.Backup $record.Path
                $restorePath = [string]$restoreTemp
                $targetPath = [string]$record.Path
                $backupPath = [string]$record.Backup
                if ([string]::IsNullOrWhiteSpace($restorePath) -or -not [System.IO.Path]::IsPathRooted($restorePath)) { throw ('Invalid rollback temp path: {0}' -f $restorePath) }
                if ([string]::IsNullOrWhiteSpace($targetPath) -or -not [System.IO.Path]::IsPathRooted($targetPath)) { throw ('Invalid rollback target path: {0}' -f $targetPath) }
                if ([string]::IsNullOrWhiteSpace($backupPath) -or -not [System.IO.Path]::IsPathRooted($backupPath)) { throw ('Invalid rollback backup path: {0}' -f $backupPath) }
                if (Test-Path -LiteralPath $targetPath -PathType Leaf) { [System.IO.File]::Replace($restorePath, $targetPath, $backupPath) }
                else { [System.IO.File]::Move($restorePath, $targetPath) }
            } elseif ($record.Applied -and (Test-Path -LiteralPath $record.Path -PathType Leaf)) {
                Remove-Item -LiteralPath $record.Path -Force
            }
        } catch { $errors.Add(('{0}: {1}' -f $record.Path, $_.Exception.Message)) }
    }
    if ($errors.Count -gt 0) { throw ('Rollback failed: ' + ($errors -join '; ')) }
}

try {
    if (-not (Test-Path -LiteralPath $gitPath -PathType Container)) { throw "Git directory not found: $gitPath" }
    try {
        $lockStream = [System.IO.File]::Open($lockPath, [System.IO.FileMode]::OpenOrCreate, [System.IO.FileAccess]::ReadWrite, [System.IO.FileShare]::None)
    } catch { throw "Could not acquire mailbox compression lock '$lockPath': $($_.Exception.Message)" }
    if (-not (Test-Path -LiteralPath $mailboxPath -PathType Leaf)) { throw "Mailbox not found: $mailboxPath" }

    $lines = @(Get-Content -LiteralPath $mailboxPath -Encoding UTF8)
    $originalCount = $lines.Count
    if (-not $Force -and $originalCount -le $MaxLines) {
        Write-Output "OK: mailbox $originalCount lines <= $MaxLines; no compression needed."
        return
    }

    $parsed = Split-Sections $lines
    $keep = New-Object 'System.Collections.Generic.List[string]'
    $archive = New-Object 'System.Collections.Generic.List[string]'
    $archivedTitles = New-Object 'System.Collections.Generic.List[string]'
    foreach ($section in $parsed.Sections) {
        $title = [string]$section.Title
        $pending = ($title.IndexOf($red, [StringComparison]::Ordinal) -ge 0 -or $title.IndexOf($yellow, [StringComparison]::Ordinal) -ge 0)
        $settled = ($title.IndexOf($green, [StringComparison]::Ordinal) -ge 0 -or $title.IndexOf($white, [StringComparison]::Ordinal) -ge 0 -or $title.IndexOf($blue, [StringComparison]::Ordinal) -ge 0)
        if ($pending) {
            foreach ($line in $section.Lines) { $keep.Add($line) }
        } elseif ($settled) {
            foreach ($line in $section.Lines) { $archive.Add($line) }
            $archivedTitles.Add($title)
        } else {
            Write-Warning "Unknown or unmarked mailbox section retained (fail-closed): $title"
            foreach ($line in $section.Lines) { $keep.Add($line) }
        }
    }
    if ($archive.Count -eq 0) {
        Write-Output "INFO: mailbox $originalCount lines > $MaxLines but no archivable (settled) sections remain; pending/unknown items only."
        return
    }
    if ($DryRun) {
        Write-Output ("DRYRUN: {0} lines -> keep header ({1}) + pending/unknown ({2}) = {3} lines; archive {4} sections" -f $originalCount, $parsed.Header.Count, $keep.Count, ($parsed.Header.Count + $keep.Count), $archivedTitles.Count)
        Write-Output 'Archiving:'
        foreach ($title in $archivedTitles) { Write-Output "  - $title" }
        return
    }

    $newLines = New-Object 'System.Collections.Generic.List[string]'
    foreach ($line in $parsed.Header) { $newLines.Add($line) }
    foreach ($line in $keep) { $newLines.Add($line) }
    $entry = New-Object 'System.Collections.Generic.List[string]'
    $entry.Add(''); $entry.Add('---')
    $entry.Add("## Mailbox archive batch $((Get-Date).ToString('yyyy-MM-dd HH:mm')) - automatic lazy compression archive ($($archivedTitles.Count) sections)")
    $entry.Add(''); foreach ($line in $archive) { $entry.Add($line) }
    $archiveAll = New-Object 'System.Collections.Generic.List[string]'
    if (Test-Path -LiteralPath $archivePath -PathType Leaf) { foreach ($line in @(Get-Content -LiteralPath $archivePath -Encoding UTF8)) { $archiveAll.Add($line) } }
    foreach ($line in $entry) { $archiveAll.Add($line) }
    $finalArchive = New-Object 'System.Collections.Generic.List[string]'
    foreach ($line in $archiveAll) { $finalArchive.Add($line) }
    $rolled = New-Object 'System.Collections.Generic.List[string]'
    if ($finalArchive.Count -gt $ArchiveMaxLines) {
        $overflow = $finalArchive.Count - $ArchiveMaxLines + $ArchiveRollLines
        if ($overflow -gt 0 -and $overflow -lt $finalArchive.Count) {
            for ($i = 0; $i -lt $overflow; $i++) { $rolled.Add($finalArchive[$i]) }
            $finalArchive.RemoveRange(0, $overflow)
        }
    }
    $v2Changed = ($rolled.Count -gt 0)
    $finalV2 = New-Object 'System.Collections.Generic.List[string]'
    if ($v2Changed -and (Test-Path -LiteralPath $v2Path -PathType Leaf)) { foreach ($line in @(Get-Content -LiteralPath $v2Path -Encoding UTF8)) { $finalV2.Add($line) } }
    if ($v2Changed) { foreach ($line in $rolled) { $finalV2.Add($line) } }

    New-Item -ItemType Directory -Path $backupRoot -Force | Out-Null
    $backupDir = Join-Path $backupRoot ('{0}-{1}' -f (Get-Date).ToString('yyyyMMddHHmmssfff'), [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $backupDir | Out-Null
    $recordList = New-Object 'System.Collections.Generic.List[object]'
    foreach ($path in @($v2Path, $archivePath, $mailboxPath)) {
        $exists = Test-Path -LiteralPath $path
        if ($exists -and -not (Test-Path -LiteralPath $path -PathType Leaf)) {
            $targetError = 'Target path is not a file: {0}' -f $path
            throw $targetError
        }
        $backup = $null
        if ($exists) { $backup = Join-Path $backupDir ([System.IO.Path]::GetFileName($path)); Copy-Item -LiteralPath $path -Destination $backup -Force }
        $recordList.Add([pscustomobject]@{ Path = $path; Exists = [bool]$exists; Backup = $backup; Applied = $false })
    }
    $records = $recordList.ToArray()

    $temps = @{}
    if ($v2Changed) { $temps[$v2Path] = New-FlushedTemp $v2Path $finalV2 }
    $temps[$archivePath] = New-FlushedTemp $archivePath $finalArchive
    $temps[$mailboxPath] = New-FlushedTemp $mailboxPath $newLines
    $v2Record = @($records | Where-Object { $_.Path -eq $v2Path })[0]
    $archiveRecord = @($records | Where-Object { $_.Path -eq $archivePath })[0]
    $mailboxRecord = @($records | Where-Object { $_.Path -eq $mailboxPath })[0]
    if ($v2Changed) { Replace-Target $v2Record $temps[$v2Path] }
    Replace-Target $archiveRecord $temps[$archivePath]
    Replace-Target $mailboxRecord $temps[$mailboxPath]
    $committed = $true
    try {
        Remove-Item -LiteralPath $backupDir -Recurse -Force
        $backupDir = $null
    }
    catch {
        $cleanupError = $_.Exception.Message
        Write-Warning ('Mailbox compression succeeded, but backup cleanup failed; retained at {0}. {1}' -f $backupDir, $cleanupError)
    }
    Write-Output ("DONE: {0} -> {1} lines (kept header {2} + pending/unknown {3}); archived {4} sections" -f $originalCount, ($parsed.Header.Count + $keep.Count), $parsed.Header.Count, $keep.Count, $archivedTitles.Count)
    foreach ($title in $archivedTitles) { Write-Output "  - $title" }
} catch {
    $primary = $_
    $rollback = $null
    if (-not $committed -and $null -ne $backupDir -and $records.Count -gt 0) {
        try {
            Restore-Targets $records
        }
        catch {
            $rollback = $_.Exception.Message
        }
    }
    if ($null -ne $backupDir) {
        if ($null -ne $rollback) {
            Write-Warning ('Mailbox compression failed and rollback reported an error: {0}. Backup retained at {1}.' -f $rollback, $backupDir)
        }
        else {
            Write-Warning ('Mailbox compression failed; original targets were restored where possible. Backup retained at {0}.' -f $backupDir)
        }
    }
    throw $primary
} finally {
    foreach ($temp in $tempPaths.ToArray()) { if (Test-Path -LiteralPath $temp -PathType Leaf) { try { Remove-Item -LiteralPath $temp -Force -ErrorAction SilentlyContinue } catch { } } }
    if ($null -ne $lockStream) { $lockStream.Dispose(); $lockStream = $null }
}
