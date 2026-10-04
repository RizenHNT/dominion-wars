# Compatible entry: build both CLIs against one current-source PlCsim driver.
$ErrorActionPreference = 'Stop'
& (Join-Path $PSScriptRoot 'build-shared-driver.ps1')
