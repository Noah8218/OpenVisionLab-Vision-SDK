[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $SolutionPath,

    [Parameter(Mandatory = $true)]
    [string] $ArtifactsPath,

    [string] $BaselinePath = (Join-Path $PSScriptRoot 'analyzer-baseline.json'),

    [string] $CompatibilityBaselinePath = (Join-Path $PSScriptRoot 'analyzer-compatibility-baseline.json'),

    [switch] $UpdateBaseline
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$solution = (Resolve-Path -LiteralPath $SolutionPath).Path
$artifacts = [System.IO.Path]::GetFullPath($ArtifactsPath)
New-Item -ItemType Directory -Force -Path $artifacts | Out-Null
$env:DOTNET_CLI_UI_LANGUAGE = 'en-US'
$analysisLevel = 'latest-recommended'
$analysisMode = 'All'

function Get-CA1051Identity {
    param(
        [Parameter(Mandatory = $true)]
        [string] $SourcePath,

        [Parameter(Mandatory = $true)]
        [int] $Line,

        [Parameter(Mandatory = $true)]
        [int] $Column
    )

    $sourceLines = @(Get-Content -LiteralPath $SourcePath)
    $lineIndex = $Line - 1
    $columnIndex = $Column - 1
    if ($lineIndex -lt 0 -or $lineIndex -ge $sourceLines.Count) {
        throw "CA1051 source line is outside '$SourcePath': $Line."
    }

    $sourceLine = [string] $sourceLines[$lineIndex]
    if ($columnIndex -lt 0 -or $columnIndex -ge $sourceLine.Length) {
        throw "CA1051 source column is outside '$SourcePath' at line ${Line}: $Column."
    }

    $identifierMatch = [regex]::Match(
        $sourceLine.Substring($columnIndex),
        '^[A-Za-z_][A-Za-z0-9_]*')
    if (-not $identifierMatch.Success) {
        throw "Could not identify the CA1051 field in '$SourcePath' at ${Line}:$Column."
    }

    $prefix = $sourceLines[0..$lineIndex] -join [Environment]::NewLine
    $namespaceMatches = [regex]::Matches(
        $prefix,
        '(?m)^\s*namespace\s+(?<name>[A-Za-z_][A-Za-z0-9_.]*)\s*(?:;|\{)')
    $typeMatches = [regex]::Matches(
        $prefix,
        '(?m)^\s*(?:(?:public|protected|internal|private|abstract|sealed|static|partial)\s+)*(?:class|struct|record(?:\s+(?:class|struct))?)\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)')
    if ($namespaceMatches.Count -eq 0 -or $typeMatches.Count -eq 0) {
        throw "Could not identify the CA1051 owner in '$SourcePath' at line $Line."
    }

    $namespaceName = $namespaceMatches[$namespaceMatches.Count - 1].Groups['name'].Value
    $typeName = $typeMatches[$typeMatches.Count - 1].Groups['name'].Value
    return "CA1051|field|$namespaceName.$typeName.$($identifierMatch.Value)"
}

function Get-CompatibilityIdentity {
    param(
        [Parameter(Mandatory = $true)]
        [string] $Code,

        [Parameter(Mandatory = $true)]
        [string] $Message,

        [Parameter(Mandatory = $true)]
        [string] $SourcePath,

        [Parameter(Mandatory = $true)]
        [int] $Line,

        [Parameter(Mandatory = $true)]
        [int] $Column
    )

    switch ($Code) {
        'CA1051' {
            return Get-CA1051Identity -SourcePath $SourcePath -Line $Line -Column $Column
        }
        'CA1707' {
            if ($Message -match '^Remove the underscores from type name (?<symbol>.+)$') {
                return "CA1707|type|$($Matches['symbol'])"
            }
            if ($Message -match '^Remove the underscores from member name (?<symbol>.+)$') {
                return "CA1707|member|$($Matches['symbol'])"
            }
            if ($Message -match '^In member (?<member>.+), remove the underscores from parameter name (?<parameter>.+)$') {
                return "CA1707|parameter|$($Matches['member'])|$($Matches['parameter'])"
            }
        }
        'CA1716' {
            if ($Message -match '^Rename namespace (?<symbol>.+?) so that ') {
                return "CA1716|namespace|$($Matches['symbol'])"
            }
            if ($Message -match '^Rename virtual/interface member (?<symbol>.+?) so that ') {
                return "CA1716|interface-member|$($Matches['symbol'])"
            }
        }
    }

    throw "Unsupported $Code diagnostic identity: $Message"
}

$restoreOutput = @(& dotnet restore $solution --artifacts-path $artifacts 2>&1)
if ($LASTEXITCODE -ne 0) {
    $restoreOutput | ForEach-Object { Write-Host $_ }
    throw "Analyzer restore failed with exit code $LASTEXITCODE."
}

$arguments = @(
    'build',
    $solution,
    '-c', 'Release',
    '--no-restore',
    '--no-incremental',
    '--artifacts-path', $artifacts,
    '-p:EnableNETAnalyzers=true',
    '-p:RunAnalyzersDuringBuild=true',
    "-p:AnalysisLevel=$analysisLevel",
    "-p:AnalysisMode=$analysisMode",
    '-p:TreatWarningsAsErrors=false',
    '-p:UseSharedCompilation=false',
    '-v:minimal',
    '-consoleloggerparameters:NoSummary;ForceNoAlign'
)

$output = @(& dotnet @arguments 2>&1)
if ($LASTEXITCODE -ne 0) {
    $output | ForEach-Object { Write-Host $_ }
    throw "Analyzer build failed with exit code $LASTEXITCODE."
}

$counts = @{}
$compatibilityCodes = @('CA1051', 'CA1707', 'CA1716')
$compatibilityIdentities = [System.Collections.Generic.List[string]]::new()
foreach ($line in $output) {
    $match = [regex]::Match([string] $line, '\bwarning (?<code>CA\d{4})\b')
    if (-not $match.Success) {
        continue
    }

    $code = $match.Groups['code'].Value
    if (-not $counts.ContainsKey($code)) {
        $counts[$code] = 0
    }
    $counts[$code]++

    if ($compatibilityCodes -notcontains $code) {
        continue
    }

    $diagnosticMatch = [regex]::Match(
        [string] $line,
        '^(?<path>.+)\((?<line>\d+),(?<column>\d+)\): warning CA\d{4}: (?<message>.+?) \[[^\]]+\]$')
    if (-not $diagnosticMatch.Success) {
        throw "Could not parse compatibility diagnostic: $line"
    }

    $message = [regex]::Replace(
        $diagnosticMatch.Groups['message'].Value,
        '\s+\(https?://[^)]+\)$',
        '')
    $identity = Get-CompatibilityIdentity `
        -Code $code `
        -Message $message `
        -SourcePath $diagnosticMatch.Groups['path'].Value `
        -Line ([int] $diagnosticMatch.Groups['line'].Value) `
        -Column ([int] $diagnosticMatch.Groups['column'].Value)
    $compatibilityIdentities.Add($identity)
}

$orderedCounts = [ordered] @{}
foreach ($code in @($counts.Keys | Sort-Object)) {
    $orderedCounts[$code] = $counts[$code]
}

$actualTotal = ($orderedCounts.Values | Measure-Object -Sum).Sum
if ($null -eq $actualTotal) {
    $actualTotal = 0
}
if ($actualTotal -eq 0) {
    throw 'Analyzer execution produced no CA diagnostics; the configured analysis or diagnostic parser may not have run.'
}

$compatibilityBaselineFile = (Resolve-Path -LiteralPath $CompatibilityBaselinePath).Path
$compatibilityBaseline = Get-Content -LiteralPath $compatibilityBaselineFile -Raw | ConvertFrom-Json
$failures = [System.Collections.Generic.List[string]]::new()
if ([int] $compatibilityBaseline.schemaVersion -ne 1) {
    $failures.Add(
        "Compatibility baseline schemaVersion '$($compatibilityBaseline.schemaVersion)' is not supported.")
}
if ([string] $compatibilityBaseline.analysisLevel -ne $analysisLevel) {
    $failures.Add(
        "Compatibility baseline analysisLevel '$($compatibilityBaseline.analysisLevel)' does not match '$analysisLevel'.")
}
if ([string] $compatibilityBaseline.analysisMode -ne $analysisMode) {
    $failures.Add(
        "Compatibility baseline analysisMode '$($compatibilityBaseline.analysisMode)' does not match '$analysisMode'.")
}

$expectedCompatibilityIdentities = @($compatibilityBaseline.diagnostics | ForEach-Object { [string] $_.identity })
$duplicateExpectedIdentities = @($expectedCompatibilityIdentities | Group-Object | Where-Object { $_.Count -ne 1 })
$duplicateActualIdentities = @($compatibilityIdentities | Group-Object | Where-Object { $_.Count -ne 1 })
if ($duplicateExpectedIdentities.Count -gt 0) {
    $failures.Add(
        "Compatibility baseline contains duplicate identities: $($duplicateExpectedIdentities.Name -join ', ')")
}
if ($duplicateActualIdentities.Count -gt 0) {
    $failures.Add(
        "Analyzer produced duplicate compatibility identities: $($duplicateActualIdentities.Name -join ', ')")
}

foreach ($code in $compatibilityCodes) {
    $expectedProperty = $compatibilityBaseline.expectedWarningsByCode.PSObject.Properties[$code]
    if ($null -eq $expectedProperty) {
        $failures.Add("Compatibility baseline is missing the expected count for $code.")
        continue
    }

    $actualCount = if ($counts.ContainsKey($code)) { [int] $counts[$code] } else { 0 }
    if ($actualCount -ne [int] $expectedProperty.Value) {
        $failures.Add(
            "Compatibility diagnostic $code changed from $($expectedProperty.Value) to $actualCount; review and update the exact contract.")
    }
}

$unexpectedCompatibilityIdentities = @($compatibilityIdentities | Where-Object { $expectedCompatibilityIdentities -notcontains $_ } | Sort-Object)
$missingCompatibilityIdentities = @($expectedCompatibilityIdentities | Where-Object { $compatibilityIdentities -notcontains $_ } | Sort-Object)
foreach ($identity in $unexpectedCompatibilityIdentities) {
    $failures.Add("Unreviewed compatibility diagnostic: $identity")
}
foreach ($identity in $missingCompatibilityIdentities) {
    $failures.Add("Reviewed compatibility diagnostic disappeared or changed: $identity")
}

foreach ($entry in $orderedCounts.GetEnumerator()) {
    Write-Host "Analyzer $($entry.Key): $($entry.Value)"
}
if ($failures.Count -gt 0) {
    throw ($failures -join [Environment]::NewLine)
}

Write-Host "Compatibility diagnostic contract passed: $($compatibilityIdentities.Count) exact identities."

if ($UpdateBaseline) {
    $baseline = [ordered] @{
        analysisLevel = $analysisLevel
        analysisMode = $analysisMode
        maximumWarningsByCode = $orderedCounts
    }
    $json = $baseline | ConvertTo-Json -Depth 4
    [System.IO.File]::WriteAllText(
        [System.IO.Path]::GetFullPath($BaselinePath),
        $json + [Environment]::NewLine,
        [System.Text.UTF8Encoding]::new($false))
    Write-Host "Analyzer baseline updated: $BaselinePath"
    return
}

$baselineFile = (Resolve-Path -LiteralPath $BaselinePath).Path
$baseline = Get-Content -LiteralPath $baselineFile -Raw | ConvertFrom-Json
$maximums = $baseline.maximumWarningsByCode
$failures = [System.Collections.Generic.List[string]]::new()
if ([string] $baseline.analysisLevel -ne $analysisLevel) {
    $failures.Add(
        "Analyzer baseline analysisLevel '$($baseline.analysisLevel)' does not match '$analysisLevel'.")
}
if ([string] $baseline.analysisMode -ne $analysisMode) {
    $failures.Add(
        "Analyzer baseline analysisMode '$($baseline.analysisMode)' does not match '$analysisMode'.")
}

foreach ($entry in $orderedCounts.GetEnumerator()) {
    $property = $maximums.PSObject.Properties[$entry.Key]
    if ($null -eq $property) {
        $failures.Add("New analyzer diagnostic $($entry.Key): $($entry.Value)")
        continue
    }
    if ($entry.Value -gt [int] $property.Value) {
        $failures.Add(
            "Analyzer diagnostic $($entry.Key) increased from $($property.Value) to $($entry.Value).")
    }
}

if ($failures.Count -gt 0) {
    throw ($failures -join [Environment]::NewLine)
}

Write-Host "Analyzer no-regression gate passed: $actualTotal diagnostics at or below baseline."
