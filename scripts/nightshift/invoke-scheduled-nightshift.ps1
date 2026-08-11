[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$startedAt = [DateTimeOffset]::Now
$launchId = [Guid]::NewGuid().ToString('N')
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$runner = Join-Path $PSScriptRoot 'run-nightshift.ps1'
$hasher = [Security.Cryptography.SHA256]::Create()
try {
    $stateKey = ([BitConverter]::ToString($hasher.ComputeHash([Text.Encoding]::UTF8.GetBytes($repoRoot)))).Replace('-', '').Substring(0, 16).ToLowerInvariant()
}
finally { $hasher.Dispose() }
$stateRoot = Join-Path (Join-Path $env:LOCALAPPDATA 'DominionWarsNightshift\state') $stateKey
$statusPath = Join-Path $stateRoot 'last-scheduled-status.json'
$logPath = Join-Path $stateRoot 'scheduler.log'

function Initialize-SafeStateDirectory {
    $trusted = [IO.Path]::GetFullPath($env:LOCALAPPDATA).TrimEnd('\', '/')
    $target = [IO.Path]::GetFullPath($stateRoot).TrimEnd('\', '/')
    if (-not $target.StartsWith($trusted + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Scheduled state escaped LOCALAPPDATA.' }
    $cursor = $trusted
    $relative = $target.Substring($trusted.Length).TrimStart('\', '/')
    foreach ($part in $relative.Split([char[]]@('\', '/'), [StringSplitOptions]::RemoveEmptyEntries)) {
        $cursor = Join-Path $cursor $part
        if (Test-Path -LiteralPath $cursor) {
            $item = Get-Item -LiteralPath $cursor -Force
            if (-not $item.PSIsContainer -or ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
                throw "Unsafe scheduled-state component: $cursor"
            }
        }
        else { New-Item -ItemType Directory -Path $cursor | Out-Null }
    }
}

function Write-LauncherLog {
    param([Parameter(Mandatory)][string]$Message)
    "$(Get-Date -Format 'o') LAUNCHER $Message" | Add-Content -LiteralPath $logPath -Encoding UTF8
}

function Write-LauncherStatus {
    param([Parameter(Mandatory)][string]$Status, [Parameter(Mandatory)][string]$Message, [int]$ExitCode)
    $temporary = Join-Path $stateRoot ('.last-scheduled-status.{0}.tmp' -f [Guid]::NewGuid().ToString('N'))
    $value = [ordered]@{
        status = $Status
        message = $Message
        exitCode = $ExitCode
        launchId = $launchId
        stage = 'SCHEDULED_LAUNCHER'
        updatedAt = [DateTimeOffset]::Now.ToString('o')
    } | ConvertTo-Json -Depth 5
    [IO.File]::WriteAllText($temporary, $value, [Text.UTF8Encoding]::new($false))
    try {
        if (Test-Path -LiteralPath $statusPath) {
            try { [IO.File]::Replace($temporary, $statusPath, $null) }
            catch { Move-Item -LiteralPath $temporary -Destination $statusPath -Force }
        }
        else { Move-Item -LiteralPath $temporary -Destination $statusPath }
    }
    finally { if (Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary -Force } }
}

function Test-NightShiftLockOwned {
    $lockPath = Join-Path $stateRoot 'nightshift.lock'
    try {
        $probe = [IO.File]::Open($lockPath, [IO.FileMode]::OpenOrCreate, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
        $probe.Dispose()
        return $false
    }
    catch [IO.IOException] { return $true }
}

function Test-FreshRunnerStatus {
    param([Parameter(Mandatory)][int]$RunnerExitCode)
    if (-not (Test-Path -LiteralPath $statusPath -PathType Leaf)) { return $false }
    try {
        $status = Get-Content -Raw -LiteralPath $statusPath | ConvertFrom-Json
        if ([string]$status.launchId -ne $launchId) { return $false }
        if ([string]$status.status -eq 'RUNNING') { return $false }
        if ([int]$status.exitCode -ne $RunnerExitCode) { return $false }
        return [DateTimeOffset]::Parse([string]$status.updatedAt) -ge $startedAt.AddSeconds(-2)
    }
    catch { return $false }
}

function Invoke-ContainedRunner {
    param([Parameter(Mandatory)][string]$PowerShellExe)
    if (-not ('DominionWarsNightShift.ScheduledJob' -as [type])) {
        Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
namespace DominionWarsNightShift {
    public static class ScheduledJob {
        [StructLayout(LayoutKind.Sequential)]
        private struct BasicLimits { public long A; public long B; public uint Flags; public UIntPtr C; public UIntPtr D; public uint E; public UIntPtr F; public uint G; public uint H; }
        [StructLayout(LayoutKind.Sequential)]
        private struct IoCounters { public ulong A; public ulong B; public ulong C; public ulong D; public ulong E; public ulong F; }
        [StructLayout(LayoutKind.Sequential)]
        private struct ExtendedLimits { public BasicLimits Basic; public IoCounters Io; public UIntPtr A; public UIntPtr B; public UIntPtr C; public UIntPtr D; }
        [DllImport("kernel32.dll", SetLastError=true)] private static extern IntPtr CreateJobObject(IntPtr attributes, string name);
        [DllImport("kernel32.dll", SetLastError=true)] private static extern bool SetInformationJobObject(IntPtr job, int cls, IntPtr info, uint length);
        [DllImport("kernel32.dll", SetLastError=true)] public static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);
        [DllImport("kernel32.dll", SetLastError=true)] public static extern bool CloseHandle(IntPtr handle);
        public static IntPtr Create() {
            IntPtr job = CreateJobObject(IntPtr.Zero, null); if (job == IntPtr.Zero) return IntPtr.Zero;
            ExtendedLimits limits = new ExtendedLimits();
            limits.Basic.Flags = 0x00002328;
            limits.Basic.E = 64;
            limits.Basic.G = 0x00004000;
            if (UIntPtr.Size == 4) {
                limits.A = new UIntPtr(1073741824U);
                limits.B = new UIntPtr(2147483648U);
            } else {
                limits.A = new UIntPtr(2147483648UL);
                limits.B = new UIntPtr(4294967296UL);
            }
            int size = Marshal.SizeOf(typeof(ExtendedLimits)); IntPtr ptr = Marshal.AllocHGlobal(size);
            try { Marshal.StructureToPtr(limits, ptr, false); if (!SetInformationJobObject(job, 9, ptr, (uint)size)) { CloseHandle(job); return IntPtr.Zero; } return job; }
            finally { Marshal.FreeHGlobal(ptr); }
        }
    }
}
'@
    }
    $job = [DominionWarsNightShift.ScheduledJob]::Create()
    if ($job -eq [IntPtr]::Zero) { throw 'Unable to create scheduled runner containment job.' }
    $process = [Diagnostics.Process]::new()
    $process.StartInfo = [Diagnostics.ProcessStartInfo]::new()
    $process.StartInfo.FileName = $PowerShellExe
    $process.StartInfo.Arguments = "-NoProfile -NonInteractive -ExecutionPolicy Bypass -File `"$runner`" -Scheduled"
    $process.StartInfo.WorkingDirectory = $repoRoot
    $process.StartInfo.UseShellExecute = $false
    $process.StartInfo.CreateNoWindow = $true
    $process.StartInfo.EnvironmentVariables['DOMINION_NIGHTSHIFT_LAUNCH_ID'] = $launchId
    try {
        if (-not $process.Start()) { throw 'Unable to start scheduled runner.' }
        if (-not [DominionWarsNightShift.ScheduledJob]::AssignProcessToJobObject($job, $process.Handle)) {
            try { $process.Kill() } catch {}
            throw 'Unable to contain scheduled runner process tree.'
        }
        if (-not $process.WaitForExit(20400000)) {
            $null = [DominionWarsNightShift.ScheduledJob]::CloseHandle($job)
            $job = [IntPtr]::Zero
            try { $process.WaitForExit(10000) | Out-Null } catch {}
            throw 'Scheduled runner exceeded the 5 hour 40 minute watchdog deadline and its process tree was terminated.'
        }
        $process.ExitCode
    }
    finally {
        if ($job -ne [IntPtr]::Zero) { $null = [DominionWarsNightShift.ScheduledJob]::CloseHandle($job) }
        $process.Dispose()
    }
}

$exitCode = 1
try {
    Initialize-SafeStateDirectory
    Write-LauncherLog -Message "START worktree=$repoRoot"
    if (-not (Test-Path -LiteralPath $runner -PathType Leaf)) { throw "Runner is missing: $runner" }
    $powershellExe = Join-Path $env:SystemRoot 'System32\WindowsPowerShell\v1.0\powershell.exe'
    $exitCode = Invoke-ContainedRunner -PowerShellExe $powershellExe
    if (-not (Test-FreshRunnerStatus -RunnerExitCode $exitCode)) {
        if ($exitCode -eq 0 -and (Test-NightShiftLockOwned)) {
            Write-LauncherLog -Message 'FINISH skipped because another runner owns the lock'
            exit 0
        }
        $message = "Runner exited $exitCode without writing a fresh scheduler status."
        Write-LauncherStatus -Status 'BOOTSTRAP_FAILED' -Message $message -ExitCode 1
        Write-LauncherLog -Message $message
        $exitCode = 1
    }
    else { Write-LauncherLog -Message "FINISH runnerExit=$exitCode" }
}
catch {
    $message = $_.Exception.Message
    try {
        Initialize-SafeStateDirectory
        Write-LauncherStatus -Status 'BOOTSTRAP_FAILED' -Message $message -ExitCode 1
        Write-LauncherLog -Message "FAILED $message"
    }
    catch { [Console]::Error.WriteLine("Night-shift launcher could not persist failure status: $($_.Exception.Message)") }
    [Console]::Error.WriteLine("Night-shift launcher failed: $message")
    $exitCode = 1
}
exit $exitCode
