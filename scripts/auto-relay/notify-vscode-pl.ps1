[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidateLength(1, 120)][ValidatePattern('^[^\r\n]+$')][string]$MailboxHeading
)

$ErrorActionPreference = 'Stop'
throw "Disabled fail-closed: code chat targets the ordinary Chat view and cannot verify delivery to the Agents Window MiniMax PL session. Mailbox heading was not sent: $MailboxHeading"
