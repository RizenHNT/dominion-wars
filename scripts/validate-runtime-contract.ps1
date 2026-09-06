[CmdletBinding()]
param([switch]$NoRestore)

$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$contractRoot = Join-Path $repoRoot 'design/runtime-kit-v1.31/contracts'
$schemaRoot = Join-Path $contractRoot 'schemas'
$fixtureRoot = Join-Path $contractRoot 'fixtures'

function Get-JsonKind([object]$Value) {
    if ($null -eq $Value) { return 'null' }
    if ($Value -is [System.Collections.IList] -and $Value -isnot [string]) { return 'array' }
    if ($Value -is [string]) { return 'string' }
    if ($Value -is [bool]) { return 'boolean' }
    if ($Value -is [byte] -or $Value -is [int16] -or $Value -is [int32] -or $Value -is [int64] -or $Value -is [decimal] -or $Value -is [double] -or $Value -is [single]) { return 'number' }
    return 'object'
}

function Get-Properties([object]$Value) {
    if ((Get-JsonKind $Value) -ne 'object') { return @() }
    return @($Value.PSObject.Properties | ForEach-Object { $_.Name })
}

function Get-Property([object]$Value, [string]$Name, [ref]$Found) {
    $Found.Value = $false
    if ((Get-JsonKind $Value) -ne 'object') { return $null }
    $property = $Value.PSObject.Properties[$Name]
    if ($null -ne $property) { $Found.Value = $true; return ,$property.Value }
    return $null
}

function Test-SchemaNode {
    param([object]$Value, [object]$Schema, [string]$Path, [object]$Root, [System.Collections.Generic.List[string]]$Errors)
    if ($null -eq $Schema) { return }
    if ($Schema.'$ref') {
        $name = [string]$Schema.'$ref'
        if ($name -notmatch '^#\/\$defs\/(.+)$') { [void]$Errors.Add("$Path unsupported ref $name"); return }
        $def = $Root.'$defs'.PSObject.Properties[$Matches[1]]
        if ($null -eq $def) { [void]$Errors.Add("$Path missing ref $name"); return }
        Test-SchemaNode -Value $Value -Schema $def.Value -Path $Path -Root $Root -Errors $Errors
        return
    }
    $allOfProperty = $Schema.PSObject.Properties['allOf']
    if ($null -ne $allOfProperty) {
        foreach ($subSchema in @($allOfProperty.Value)) {
            Test-SchemaNode -Value $Value -Schema $subSchema -Path $Path -Root $Root -Errors $Errors
        }
    }
    $ifProperty = $Schema.PSObject.Properties['if']
    if ($null -ne $ifProperty) {
        $conditionErrors = New-Object 'System.Collections.Generic.List[string]'
        Test-SchemaNode -Value $Value -Schema $ifProperty.Value -Path $Path -Root $Root -Errors $conditionErrors
        $branchProperty = if ($conditionErrors.Count -eq 0) {
            $Schema.PSObject.Properties['then']
        } else {
            $Schema.PSObject.Properties['else']
        }
        if ($null -ne $branchProperty) {
            Test-SchemaNode -Value $Value -Schema $branchProperty.Value -Path $Path -Root $Root -Errors $Errors
        }
    }
    $kind = Get-JsonKind $Value
    if ($Schema.type) {
        $allowed = @($Schema.type)
        $kindAllowed = $false
        foreach ($type in $allowed) {
            if ([string]$type -eq $kind -or ([string]$type -eq 'integer' -and $kind -eq 'number' -and [math]::Truncate([double]$Value) -eq [double]$Value)) { $kindAllowed = $true }
        }
        if (-not $kindAllowed) { [void]$Errors.Add("$Path type=$kind expected=$($allowed -join ',')"); return }
    }
    if ($Schema.PSObject.Properties.Name -contains 'const' -and $Value -ne $Schema.const) { [void]$Errors.Add("$Path const mismatch"); return }
    if ($Schema.enum -and -not (@($Schema.enum) -contains $Value)) { [void]$Errors.Add("$Path enum mismatch"); return }
    if ($Schema.pattern -and $kind -eq 'string' -and ([string]$Value -notmatch [string]$Schema.pattern)) { [void]$Errors.Add("$Path pattern mismatch"); return }
    if ($Schema.minLength -and $kind -eq 'string' -and ([string]$Value).Length -lt [int]$Schema.minLength) { [void]$Errors.Add("$Path minLength"); return }
    if ($Schema.maxLength -and $kind -eq 'string' -and ([string]$Value).Length -gt [int]$Schema.maxLength) { [void]$Errors.Add("$Path maxLength"); return }
    if ($Schema.PSObject.Properties.Name -contains 'minimum' -and $kind -eq 'number' -and [double]$Value -lt [double]$Schema.minimum) { [void]$Errors.Add("$Path minimum"); return }
    if ($Schema.minItems -and $kind -eq 'array' -and $Value.Count -lt [int]$Schema.minItems) { [void]$Errors.Add("$Path minItems"); return }
    if ($Schema.maxItems -and $kind -eq 'array' -and $Value.Count -gt [int]$Schema.maxItems) { [void]$Errors.Add("$Path maxItems"); return }
    if ($Schema.uniqueItems -and $kind -eq 'array') {
        $seen = New-Object 'System.Collections.Generic.HashSet[string]'
        foreach ($item in $Value) { if (-not $seen.Add(($item | ConvertTo-Json -Compress -Depth 20))) { [void]$Errors.Add("$Path uniqueItems"); break } }
    }
    if ($kind -eq 'object') {
        $names = Get-Properties $Value
        if ($null -ne $Schema.required) {
            foreach ($required in @($Schema.required)) {
                $found = $false; [void](Get-Property $Value ([string]$required) ([ref]$found))
                if (-not $found -and -not [string]::IsNullOrWhiteSpace([string]$required)) { [void]$Errors.Add("$Path missing=$required") }
            }
        }
        if ($Schema.additionalProperties -eq $false) {
            foreach ($name in $names) { if (-not ($Schema.properties.PSObject.Properties.Name -contains $name)) { [void]$Errors.Add("$Path unknown=$name") } }
        }
        foreach ($property in @($Schema.properties.PSObject.Properties)) {
            $found = $false; $child = Get-Property $Value $property.Name ([ref]$found)
            if ($found) { Test-SchemaNode -Value $child -Schema $property.Value -Path "$Path.$($property.Name)" -Root $Root -Errors $Errors }
        }
    }
    if ($kind -eq 'array' -and $Schema.items) {
        for ($i = 0; $i -lt $Value.Count; $i++) { Test-SchemaNode -Value $Value[$i] -Schema $Schema.items -Path "$Path[$i]" -Root $Root -Errors $Errors }
    }
}

if (-not (Test-Path -LiteralPath $schemaRoot) -or -not (Test-Path -LiteralPath $fixtureRoot)) { throw 'Contract schemas or fixtures are missing.' }
$schemas = @(Get-ChildItem -LiteralPath $schemaRoot -Filter '*.schema.json' -File)
if ($schemas.Count -ne 4) { throw "Expected 4 schemas, found $($schemas.Count)." }
$schemaMap = @{}
foreach ($file in $schemas) { $schemaMap[$file.BaseName -replace '\.schema$',''] = Get-Content -Raw -LiteralPath $file.FullName | ConvertFrom-Json }
$validCount = 0; $invalidCount = 0; $failCount = 0
foreach ($kind in $schemaMap.Keys) {
    $schema = $schemaMap[$kind]
    foreach ($expectation in @('valid','invalid')) {
        $dir = Join-Path (Join-Path $fixtureRoot $kind) $expectation
        $fixtures = @(Get-ChildItem -LiteralPath $dir -Filter '*.json' -File -ErrorAction SilentlyContinue)
        foreach ($fixture in $fixtures) {
            $errors = New-Object 'System.Collections.Generic.List[string]'
            try { $value = Get-Content -Raw -LiteralPath $fixture.FullName | ConvertFrom-Json; Test-SchemaNode -Value $value -Schema $schema -Path '$' -Root $schema -Errors $errors } catch { [void]$errors.Add($_.Exception.Message) }
            $isValid = $errors.Count -eq 0
            if ($expectation -eq 'valid') { $validCount++ } else { $invalidCount++ }
            if (($expectation -eq 'valid') -ne $isValid) { $failCount++; Write-Output "FAIL $expectation $($fixture.FullName): $($errors -join '; ')" } else { Write-Output "PASS $expectation $($fixture.Name)" }
        }
    }
}
Write-Output "RUNTIME_CONTRACT_VALIDATION schemas=$($schemas.Count) valid=$validCount invalid=$invalidCount fail=$failCount"
if ($failCount -gt 0) { exit 1 }
