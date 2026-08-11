Set-StrictMode -Version Latest

function Get-RelayRepositoryRoot {
    param([Parameter(Mandatory)][string]$ScriptDirectory)
    (Resolve-Path (Join-Path $ScriptDirectory '..\..')).Path
}

function Get-RelayStateRoot {
    param([Parameter(Mandatory)][string]$RepositoryRoot)
    $canonical = [IO.Path]::GetFullPath($RepositoryRoot).TrimEnd('\', '/')
    $sha = [Security.Cryptography.SHA256]::Create()
    try {
        $key = ([BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($canonical)))).Replace('-', '').Substring(0, 16).ToLowerInvariant()
    }
    finally { $sha.Dispose() }
    Join-Path (Join-Path $env:LOCALAPPDATA 'DominionWarsAutoRelay\state') $key
}

function Get-RelayAllowedSids {
    $current = [Security.Principal.WindowsIdentity]::GetCurrent().User
    if (-not $current) { throw 'Unable to resolve the current Windows user SID.' }
    @(
        $current,
        [Security.Principal.SecurityIdentifier]::new('S-1-5-18'),
        [Security.Principal.SecurityIdentifier]::new('S-1-5-32-544')
    )
}

function Set-RelayPrivateDirectoryAcl {
    param([Parameter(Mandatory)][string]$Path)
    $sids = @(Get-RelayAllowedSids)
    $acl = [Security.AccessControl.DirectorySecurity]::new()
    $acl.SetOwner($sids[0])
    $acl.SetAccessRuleProtection($true, $false)
    $inheritance = [Security.AccessControl.InheritanceFlags]::ContainerInherit -bor [Security.AccessControl.InheritanceFlags]::ObjectInherit
    foreach ($sid in $sids) {
        $rule = [Security.AccessControl.FileSystemAccessRule]::new(
            $sid,
            [Security.AccessControl.FileSystemRights]::FullControl,
            $inheritance,
            [Security.AccessControl.PropagationFlags]::None,
            [Security.AccessControl.AccessControlType]::Allow
        )
        $null = $acl.AddAccessRule($rule)
    }
    Set-Acl -LiteralPath $Path -AclObject $acl
}

function Set-RelayPrivateFileAcl {
    param([Parameter(Mandatory)][string]$Path)
    $sids = @(Get-RelayAllowedSids)
    $acl = Get-Acl -LiteralPath $Path
    $acl.SetAccessRuleProtection($true, $false)
    foreach ($rule in @($acl.Access)) {
        $acl.PurgeAccessRules($rule.IdentityReference)
    }
    $acl.SetOwner($sids[0])
    foreach ($sid in $sids) {
        $rule = [Security.AccessControl.FileSystemAccessRule]::new(
            $sid,
            [Security.AccessControl.FileSystemRights]::FullControl,
            [Security.AccessControl.AccessControlType]::Allow
        )
        $null = $acl.AddAccessRule($rule)
    }
    Set-Acl -LiteralPath $Path -AclObject $acl
}

function Test-RelayPrivateAcl {
    param([Parameter(Mandatory)][string]$Path)
    if (-not (Test-Path -LiteralPath $Path -PathType Container)) { return $false }
    $item = Get-Item -LiteralPath $Path -Force
    if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { return $false }
    $acl = Get-Acl -LiteralPath $Path
    $currentSid = [Security.Principal.WindowsIdentity]::GetCurrent().User.Value
    $allowed = @((Get-RelayAllowedSids) | ForEach-Object { $_.Value } | Sort-Object -Unique)
    $ownerSid = try { ([Security.Principal.NTAccount]$acl.Owner).Translate([Security.Principal.SecurityIdentifier]).Value } catch { [string]$acl.Owner }
    if (-not $acl.AreAccessRulesProtected -or $ownerSid -ne $currentSid) { return $false }
    $rules = @($acl.Access)
    if ($rules.Count -ne 3) { return $false }
    foreach ($rule in $rules) {
        $sid = try { $rule.IdentityReference.Translate([Security.Principal.SecurityIdentifier]).Value } catch { [string]$rule.IdentityReference }
        if ($allowed -notcontains $sid -or $rule.AccessControlType -ne [Security.AccessControl.AccessControlType]::Allow -or
            ($rule.FileSystemRights -band [Security.AccessControl.FileSystemRights]::FullControl) -ne [Security.AccessControl.FileSystemRights]::FullControl -or
            $rule.IsInherited -or
            $rule.InheritanceFlags -ne ([Security.AccessControl.InheritanceFlags]::ContainerInherit -bor [Security.AccessControl.InheritanceFlags]::ObjectInherit) -or
            $rule.PropagationFlags -ne [Security.AccessControl.PropagationFlags]::None) { return $false }
    }
    return $true
}

function Test-RelayPrivateFileAcl {
    param([Parameter(Mandatory)][string]$Path, [switch]$AllowSafeInheritance)
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) { return $false }
    $item = Get-Item -LiteralPath $Path -Force
    if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { return $false }
    $acl = Get-Acl -LiteralPath $Path
    $currentSid = [Security.Principal.WindowsIdentity]::GetCurrent().User.Value
    $allowed = @((Get-RelayAllowedSids) | ForEach-Object { $_.Value } | Sort-Object -Unique)
    $ownerSid = try { ([Security.Principal.NTAccount]$acl.Owner).Translate([Security.Principal.SecurityIdentifier]).Value } catch { [string]$acl.Owner }
    if ($ownerSid -ne $currentSid -or (-not $AllowSafeInheritance -and -not $acl.AreAccessRulesProtected)) { return $false }
    $rules = @($acl.Access)
    if ($rules.Count -ne 3) { return $false }
    foreach ($rule in $rules) {
        $sid = try { $rule.IdentityReference.Translate([Security.Principal.SecurityIdentifier]).Value } catch { [string]$rule.IdentityReference }
        if ($allowed -notcontains $sid -or $rule.AccessControlType -ne [Security.AccessControl.AccessControlType]::Allow -or
            ($rule.FileSystemRights -band [Security.AccessControl.FileSystemRights]::FullControl) -ne [Security.AccessControl.FileSystemRights]::FullControl -or
            ($rule.IsInherited -and -not $AllowSafeInheritance)) { return $false }
    }
    return $true
}

function Get-MiniMaxRelayConfigDirectory {
    Join-Path $env:LOCALAPPDATA 'DominionWarsAutoRelay\mmx'
}

function Initialize-RelayStateRoot {
    param([Parameter(Mandatory)][string]$Path)
    $trusted = [IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA 'DominionWarsAutoRelay')).TrimEnd('\', '/')
    $target = [IO.Path]::GetFullPath($Path).TrimEnd('\', '/')
    if ($target -ne $trusted -and -not $target.StartsWith($trusted + '\', [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Relay state path escaped LOCALAPPDATA.'
    }
    $cursor = $trusted
    if (-not (Test-Path -LiteralPath $cursor -PathType Container)) { New-Item -ItemType Directory -Path $cursor | Out-Null }
    $cursorItem = Get-Item -LiteralPath $cursor -Force
    if (($cursorItem.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw "Unsafe relay state path: $cursor" }
    if (-not (Test-RelayPrivateAcl -Path $cursor)) { Set-RelayPrivateDirectoryAcl -Path $cursor }
    $relative = $target.Substring($trusted.Length).TrimStart('\', '/')
    foreach ($part in $relative.Split([char[]]@('\', '/'), [StringSplitOptions]::RemoveEmptyEntries)) {
        $cursor = Join-Path $cursor $part
        if (-not (Test-Path -LiteralPath $cursor -PathType Container)) { New-Item -ItemType Directory -Path $cursor | Out-Null }
        $item = Get-Item -LiteralPath $cursor -Force
        if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) { throw "Unsafe relay state path: $cursor" }
        if (-not (Test-RelayPrivateAcl -Path $cursor)) { Set-RelayPrivateDirectoryAcl -Path $cursor }
    }
    if (-not (Test-RelayPrivateAcl -Path $target)) { throw 'Relay state ACL is not private.' }
}

function Write-RelayAtomicText {
    param([Parameter(Mandatory)][string]$Path, [Parameter(Mandatory)][AllowEmptyString()][string]$Content)
    $directory = Split-Path -Parent $Path
    if (-not (Test-Path -LiteralPath $directory -PathType Container)) { throw "Relay output directory is missing: $directory" }
    $temporary = Join-Path $directory ('.{0}.{1}.tmp' -f ([IO.Path]::GetFileName($Path)), [guid]::NewGuid().ToString('N'))
    [IO.File]::WriteAllText($temporary, $Content, [Text.UTF8Encoding]::new($false))
    try {
        if (Test-Path -LiteralPath $Path -PathType Leaf) {
            try { [IO.File]::Replace($temporary, $Path, $null) }
            catch { Move-Item -LiteralPath $temporary -Destination $Path -Force }
        }
        else { Move-Item -LiteralPath $temporary -Destination $Path }
    }
    finally { if (Test-Path -LiteralPath $temporary) { Remove-Item -LiteralPath $temporary -Force } }
}

function Write-RelayAtomicJson {
    param([Parameter(Mandatory)][string]$Path, [Parameter(Mandatory)][object]$Value)
    Write-RelayAtomicText -Path $Path -Content ($Value | ConvertTo-Json -Depth 20)
}

function Test-RelayDisabled {
    param([Parameter(Mandatory)][string]$StateRoot)
    Test-Path -LiteralPath (Join-Path $StateRoot 'AUTO_RELAY_DISABLED') -PathType Leaf
}

function ConvertTo-RelayOneLine {
    param([AllowEmptyString()][string]$Text, [int]$MaximumLength = 600)
    $clean = ($Text -replace '[\x00-\x1F\x7F]', ' ' -replace '\s+', ' ').Trim()
    $clean = $clean.Replace('<!--', '').Replace('-->', '').Replace('|', '/')
    if ($clean.Length -gt $MaximumLength) { $clean = $clean.Substring(0, $MaximumLength) + '...' }
    $clean
}

function Add-RelayMailboxEntry {
    param(
        [Parameter(Mandatory)][string]$RepositoryRoot,
        [Parameter(Mandatory)][string]$Entry
    )
    if ($Entry.Length -gt 2000 -or $Entry -match '<!--|-->') { throw 'Mailbox entry failed the relay safety limit.' }
    $mailbox = Join-Path $RepositoryRoot 'docs\AI_MAILBOX.md'
    if (-not (Test-Path -LiteralPath $mailbox -PathType Leaf)) { throw 'AI mailbox is missing.' }
    $sha = [Security.Cryptography.SHA256]::Create()
    try { $mutexKey = ([BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($RepositoryRoot)))).Replace('-', '').Substring(0, 16) }
    finally { $sha.Dispose() }
    $mutex = [Threading.Mutex]::new($false, "Local\DominionWarsAutoRelayMailbox-$mutexKey")
    $acquired = $false
    try {
        $acquired = $mutex.WaitOne([TimeSpan]::FromSeconds(15))
        if (-not $acquired) { throw 'Timed out waiting for the mailbox append lock.' }
        $text = [IO.File]::ReadAllText($mailbox, [Text.Encoding]::UTF8)
        $todayHeader = '## ' + (Get-Date -Format 'yyyy-MM-dd')
        $today = [regex]::Match($text, '(?m)^' + [regex]::Escape($todayHeader) + '\s*$')
        if ($today.Success) {
            $dateHeadingRegex = [regex]::new('(?m)^##\s+\d{4}-\d{2}-\d{2}\s*$')
            $next = $dateHeadingRegex.Match($text, $today.Index + $today.Length)
            $insertAt = if ($next.Success) { $next.Index } else { $text.Length }
            $before = $text.Substring(0, $insertAt).TrimEnd()
            $after = $text.Substring($insertAt).TrimStart()
            $updated = $before + [Environment]::NewLine + [Environment]::NewLine + $Entry.Trim() + [Environment]::NewLine + [Environment]::NewLine + $after
        }
        else {
            $firstDate = [regex]::Match($text, '(?m)^##\s+\d{4}-\d{2}-\d{2}\s*$')
            $insertAt = if ($firstDate.Success) { $firstDate.Index } else { $text.Length }
            $before = $text.Substring(0, $insertAt).TrimEnd()
            $after = $text.Substring($insertAt).TrimStart()
            $datedEntry = $todayHeader + [Environment]::NewLine + [Environment]::NewLine + $Entry.Trim()
            $updated = $before + [Environment]::NewLine + [Environment]::NewLine + $datedEntry + [Environment]::NewLine + [Environment]::NewLine + $after
        }
        Write-RelayAtomicText -Path $mailbox -Content $updated
    }
    finally {
        if ($acquired) { $mutex.ReleaseMutex() }
        $mutex.Dispose()
    }
}
