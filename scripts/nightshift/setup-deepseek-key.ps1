[CmdletBinding()]
param(
    [switch]$HardenOnly
)

$ErrorActionPreference = 'Stop'
$targetDirectory = Join-Path $env:LOCALAPPDATA 'DominionWarsNightshift'
$targetFile = Join-Path $targetDirectory 'deepseek.key'

function Get-PrivateCredentialSids {
    @(
        [Security.Principal.WindowsIdentity]::GetCurrent().User
        [Security.Principal.SecurityIdentifier]::new('S-1-5-18')
        [Security.Principal.SecurityIdentifier]::new('S-1-5-32-544')
    )
}

function Assert-NotReparsePoint {
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][string]$Label
    )

    if (-not (Test-Path -LiteralPath $Path)) { return }
    $item = Get-Item -LiteralPath $Path -Force
    if (($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
        throw "$Label must not be a symbolic link, junction, or other reparse point: $Path"
    }
}

function Set-PrivateDirectoryAcl {
    param([Parameter(Mandatory)][string]$Path)

    $currentSid = [Security.Principal.WindowsIdentity]::GetCurrent().User
    $acl = [Security.AccessControl.DirectorySecurity]::new()
    $acl.SetOwner($currentSid)
    $acl.SetAccessRuleProtection($true, $false)
    $inheritance = [Security.AccessControl.InheritanceFlags]::ContainerInherit -bor [Security.AccessControl.InheritanceFlags]::ObjectInherit
    foreach ($sid in Get-PrivateCredentialSids) {
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

function Set-PrivateFileAcl {
    param([Parameter(Mandatory)][string]$Path)

    $currentSid = [Security.Principal.WindowsIdentity]::GetCurrent().User
    $acl = [Security.AccessControl.FileSecurity]::new()
    $acl.SetOwner($currentSid)
    $acl.SetAccessRuleProtection($true, $false)
    foreach ($sid in Get-PrivateCredentialSids) {
        $rule = [Security.AccessControl.FileSystemAccessRule]::new(
            $sid,
            [Security.AccessControl.FileSystemRights]::FullControl,
            [Security.AccessControl.AccessControlType]::Allow
        )
        $null = $acl.AddAccessRule($rule)
    }
    Set-Acl -LiteralPath $Path -AclObject $acl
}

function Assert-PrivateAcl {
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][string]$Label,
        [switch]$Directory
    )

    $acl = Get-Acl -LiteralPath $Path
    if (-not $acl.AreAccessRulesProtected) {
        throw "$Label ACL still inherits permissions from its parent."
    }
    $currentSid = [Security.Principal.WindowsIdentity]::GetCurrent().User.Value
    $ownerSid = $acl.GetOwner([Security.Principal.SecurityIdentifier]).Value
    if ($ownerSid -ne $currentSid) {
        throw "$Label owner is $ownerSid instead of the current user."
    }

    $rules = @($acl.GetAccessRules($true, $true, [Security.Principal.SecurityIdentifier]))
    $expectedSids = @(Get-PrivateCredentialSids)
    if ($rules.Count -ne $expectedSids.Count) {
        throw "$Label ACL must contain exactly $($expectedSids.Count) explicit access rules; found $($rules.Count)."
    }

    $expectedInheritance = if ($Directory) {
        [Security.AccessControl.InheritanceFlags]::ContainerInherit -bor [Security.AccessControl.InheritanceFlags]::ObjectInherit
    }
    else { [Security.AccessControl.InheritanceFlags]::None }
    foreach ($sid in $expectedSids) {
        $matches = @($rules | Where-Object { $_.IdentityReference.Value -eq $sid.Value })
        if ($matches.Count -ne 1) { throw "$Label ACL must contain exactly one rule for $($sid.Value)." }
        $rule = $matches[0]
        $hasFullControl = ($rule.FileSystemRights -band [Security.AccessControl.FileSystemRights]::FullControl) -eq [Security.AccessControl.FileSystemRights]::FullControl
        if ($rule.AccessControlType -ne [Security.AccessControl.AccessControlType]::Allow -or
            -not $hasFullControl -or $rule.IsInherited -or
            $rule.InheritanceFlags -ne $expectedInheritance -or
            $rule.PropagationFlags -ne [Security.AccessControl.PropagationFlags]::None) {
            throw "$Label ACL rule for $($sid.Value) is not the expected explicit FullControl rule."
        }
    }
}

function Protect-PrivateAcl {
    param(
        [Parameter(Mandatory)][string]$Path,
        [Parameter(Mandatory)][string]$Label,
        [switch]$Directory
    )

    try {
        Assert-PrivateAcl -Path $Path -Label $Label -Directory:$Directory
        return
    }
    catch {
        if ($Directory) { Set-PrivateDirectoryAcl -Path $Path }
        else { Set-PrivateFileAcl -Path $Path }
        Assert-PrivateAcl -Path $Path -Label $Label -Directory:$Directory
    }
}

Assert-NotReparsePoint -Path $targetDirectory -Label 'Credential directory'
Assert-NotReparsePoint -Path $targetFile -Label 'Credential file'
New-Item -ItemType Directory -Path $targetDirectory -Force | Out-Null
Assert-NotReparsePoint -Path $targetDirectory -Label 'Credential directory'
Protect-PrivateAcl -Path $targetDirectory -Label 'Credential directory' -Directory

if ($HardenOnly) {
    if (-not (Test-Path -LiteralPath $targetFile -PathType Leaf)) {
        throw "DeepSeek credential does not exist at: $targetFile"
    }
}
else {
    $secret = Read-Host 'Paste the DeepSeek API key (input is hidden)' -AsSecureString
    if ($secret.Length -eq 0) {
        throw 'No API key was entered.'
    }

    # ConvertFrom-SecureString uses Windows DPAPI. Only this Windows user on this
    # computer can decrypt the resulting value. Write a fully protected sibling
    # first, then atomically replace the old key so interruption preserves it.
    $temporaryFile = Join-Path $targetDirectory ('.deepseek.key.{0}.tmp' -f [Guid]::NewGuid().ToString('N'))
    try {
        $secret | ConvertFrom-SecureString | Set-Content -LiteralPath $temporaryFile -Encoding UTF8
        Set-PrivateFileAcl -Path $temporaryFile
        Assert-PrivateAcl -Path $temporaryFile -Label 'Temporary credential file'
        if (Test-Path -LiteralPath $targetFile -PathType Leaf) {
            [IO.File]::Replace($temporaryFile, $targetFile, $null, $true)
        }
        else {
            [IO.File]::Move($temporaryFile, $targetFile)
        }
    }
    finally {
        if ($temporaryFile -and (Test-Path -LiteralPath $temporaryFile)) {
            Remove-Item -LiteralPath $temporaryFile -Force
        }
    }
}

Assert-NotReparsePoint -Path $targetFile -Label 'Credential file'
Protect-PrivateAcl -Path $targetFile -Label 'Credential file'

if ($HardenOnly) {
    Write-Host "DeepSeek credential ACL hardened in place: $targetFile"
}
else {
    Write-Host "DeepSeek credential saved with Windows DPAPI at: $targetFile"
    Write-Host 'The plaintext key was not written to the repository.'
}
Write-Host 'ACL verified: only the current user, LocalSystem, and local Administrators have access.'
