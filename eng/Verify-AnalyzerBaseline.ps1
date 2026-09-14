[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $SolutionPath,

    [Parameter(Mandatory = $true)]
    [string] $ArtifactsPath,

    [string] $BaselinePath = (Join-Path $PSScriptRoot 'analyzer-baseline.json'),

    [string] $CompatibilityBaselinePath = (Join-Path $PSScriptRoot 'analyzer-compatibility-baseline.json'),

    [string] $PerformanceBaselinePath = (Join-Path $PSScriptRoot 'analyzer-performance-baseline.json'),

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
$solutionDirectory = Split-Path -Parent $solution
$sourceLinesByPath = @{}

function Get-SourceLines {
    param(
        [Parameter(Mandatory = $true)]
        [string] $SourcePath
    )

    if (-not $sourceLinesByPath.ContainsKey($SourcePath)) {
        $sourceLinesByPath[$SourcePath] = @(Get-Content -LiteralPath $SourcePath)
    }

    return @($sourceLinesByPath[$SourcePath])
}

function Get-CA1051Identity {
    param(
        [Parameter(Mandatory = $true)]
        [string] $SourcePath,

        [Parameter(Mandatory = $true)]
        [int] $Line,

        [Parameter(Mandatory = $true)]
        [int] $Column
    )

    $sourceLines = Get-SourceLines -SourcePath $SourcePath
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

function Get-PerformanceIdentityBase {
    param(
        [Parameter(Mandatory = $true)]
        [string] $Code,

        [Parameter(Mandatory = $true)]
        [string] $Message,

        [Parameter(Mandatory = $true)]
        [string] $SourcePath,

        [Parameter(Mandatory = $true)]
        [int] $Line
    )

    $sourceLines = Get-SourceLines -SourcePath $SourcePath
    $lineIndex = $Line - 1
    if ($lineIndex -lt 0 -or $lineIndex -ge $sourceLines.Count) {
        throw "$Code source line is outside '$SourcePath': $Line."
    }

    $prefix = $sourceLines[0..$lineIndex] -join [Environment]::NewLine
    $namespaceMatches = [regex]::Matches(
        $prefix,
        '(?m)^\s*namespace\s+(?<name>[A-Za-z_][A-Za-z0-9_.]*)\s*(?:;|\{)')
    $typeMatches = [regex]::Matches(
        $prefix,
        '(?m)^\s*(?:(?:public|protected|internal|private|abstract|sealed|static|partial)\s+)*(?:class|struct|record(?:\s+(?:class|struct))?)\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)')
    if ($namespaceMatches.Count -eq 0 -or $typeMatches.Count -eq 0) {
        throw "Could not identify the $Code owner in '$SourcePath' at line $Line."
    }

    $namespaceName = $namespaceMatches[$namespaceMatches.Count - 1].Groups['name'].Value
    $ownerTypeMatches = $typeMatches
    if ($Code -in @('CA1805', 'CA1822')) {
        $publicTypeMatches = [regex]::Matches(
            $prefix,
            '(?m)^\s*public\s+(?:(?:abstract|sealed|static|partial)\s+)*(?:class|struct|record(?:\s+(?:class|struct))?)\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)')
        if ($publicTypeMatches.Count -gt 0) {
            $ownerTypeMatches = $publicTypeMatches
        }
    }
    $typeName = $ownerTypeMatches[$ownerTypeMatches.Count - 1].Groups['name'].Value
    $memberName = $null
    if ($Code -in @('CA1805', 'CA1822') -and $Message -match "^Member '(?<member>[^']+)'") {
        $memberName = $Matches['member']
    }
    else {
        for ($index = $lineIndex; $index -ge 0; $index--) {
            $candidate = ([string] $sourceLines[$index]).Trim()
            $declarationMatch = [regex]::Match(
                $candidate,
                '^(?:public|private|internal|protected)\s+(?:(?:static|unsafe|virtual|override|sealed|abstract|async|readonly|partial|extern|new)\s+)*(?:[A-Za-z_][A-Za-z0-9_<>,.\[\]? ]+\s+)?(?<name>[A-Za-z_][A-Za-z0-9_]*)\s*(?:<[^>]+>\s*)?\(')
            if ($declarationMatch.Success) {
                $memberName = $declarationMatch.Groups['name'].Value
                break
            }
        }
    }

    if ([string]::IsNullOrWhiteSpace($memberName)) {
        throw "Could not identify the $Code member in '$SourcePath' at line $Line."
    }

    $relativePath = [System.IO.Path]::GetRelativePath($solutionDirectory, $SourcePath).Replace('\', '/')
    $sourceLine = [regex]::Replace(([string] $sourceLines[$lineIndex]).Trim(), '\s+', ' ')
    return "$Code|$relativePath|$namespaceName.$typeName|$memberName|$sourceLine"
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
$performanceCodes = @('CA1805', 'CA1822', 'CA1825', 'CA1843', 'CA1859', 'CA1861', 'CA1869')
$performanceIdentityBases = [System.Collections.Generic.List[string]]::new()
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

    if ($compatibilityCodes -notcontains $code -and $performanceCodes -notcontains $code) {
        continue
    }

    $diagnosticMatch = [regex]::Match(
        [string] $line,
        '^(?<path>.+)\((?<line>\d+),(?<column>\d+)\): warning CA\d{4}: (?<message>.+?) \[[^\]]+\]$')
    if (-not $diagnosticMatch.Success) {
        throw "Could not parse reviewed analyzer diagnostic: $line"
    }

    $message = [regex]::Replace(
        $diagnosticMatch.Groups['message'].Value,
        '\s+\(https?://[^)]+\)$',
        '')
    if ($compatibilityCodes -contains $code) {
        $identity = Get-CompatibilityIdentity `
            -Code $code `
            -Message $message `
            -SourcePath $diagnosticMatch.Groups['path'].Value `
            -Line ([int] $diagnosticMatch.Groups['line'].Value) `
            -Column ([int] $diagnosticMatch.Groups['column'].Value)
        $compatibilityIdentities.Add($identity)
    }
    else {
        $identityBase = Get-PerformanceIdentityBase `
            -Code $code `
            -Message $message `
            -SourcePath $diagnosticMatch.Groups['path'].Value `
            -Line ([int] $diagnosticMatch.Groups['line'].Value)
        $performanceIdentityBases.Add($identityBase)
    }
}

$performanceIdentityOccurrences = @{}
$performanceIdentities = [System.Collections.Generic.List[string]]::new()
foreach ($identityBase in $performanceIdentityBases) {
    if (-not $performanceIdentityOccurrences.ContainsKey($identityBase)) {
        $performanceIdentityOccurrences[$identityBase] = 0
    }
    $performanceIdentityOccurrences[$identityBase]++
    $performanceIdentities.Add("$identityBase|occurrence=$($performanceIdentityOccurrences[$identityBase])")
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

$performanceBaselineFile = (Resolve-Path -LiteralPath $PerformanceBaselinePath).Path
$performanceBaseline = Get-Content -LiteralPath $performanceBaselineFile -Raw | ConvertFrom-Json
if ([int] $performanceBaseline.schemaVersion -ne 1) {
    $failures.Add(
        "Performance baseline schemaVersion '$($performanceBaseline.schemaVersion)' is not supported.")
}
if ([string] $performanceBaseline.analysisLevel -ne $analysisLevel) {
    $failures.Add(
        "Performance baseline analysisLevel '$($performanceBaseline.analysisLevel)' does not match '$analysisLevel'.")
}
if ([string] $performanceBaseline.analysisMode -ne $analysisMode) {
    $failures.Add(
        "Performance baseline analysisMode '$($performanceBaseline.analysisMode)' does not match '$analysisMode'.")
}

$expectedPerformanceIdentities = @($performanceBaseline.diagnostics | ForEach-Object { [string] $_.identity })
$duplicateExpectedPerformanceIdentities = @($expectedPerformanceIdentities | Group-Object | Where-Object { $_.Count -ne 1 })
$duplicateActualPerformanceIdentities = @($performanceIdentities | Group-Object | Where-Object { $_.Count -ne 1 })
if ($duplicateExpectedPerformanceIdentities.Count -gt 0) {
    $failures.Add(
        "Performance baseline contains duplicate identities: $($duplicateExpectedPerformanceIdentities.Name -join ', ')")
}
if ($duplicateActualPerformanceIdentities.Count -gt 0) {
    $failures.Add(
        "Analyzer produced duplicate performance identities: $($duplicateActualPerformanceIdentities.Name -join ', ')")
}

foreach ($code in $performanceCodes) {
    $expectedProperty = $performanceBaseline.expectedWarningsByCode.PSObject.Properties[$code]
    if ($null -eq $expectedProperty) {
        $failures.Add("Performance baseline is missing the expected count for $code.")
        continue
    }

    $actualCount = if ($counts.ContainsKey($code)) { [int] $counts[$code] } else { 0 }
    if ($actualCount -ne [int] $expectedProperty.Value) {
        $failures.Add(
            "Performance diagnostic $code changed from $($expectedProperty.Value) to $actualCount; review and update the exact contract.")
    }
}

$unexpectedPerformanceIdentities = @($performanceIdentities | Where-Object { $expectedPerformanceIdentities -notcontains $_ } | Sort-Object)
$missingPerformanceIdentities = @($expectedPerformanceIdentities | Where-Object { $performanceIdentities -notcontains $_ } | Sort-Object)
foreach ($identity in $unexpectedPerformanceIdentities) {
    $failures.Add("Unreviewed performance diagnostic: $identity")
}
foreach ($identity in $missingPerformanceIdentities) {
    $failures.Add("Reviewed performance diagnostic disappeared or changed: $identity")
}

foreach ($entry in $orderedCounts.GetEnumerator()) {
    Write-Host "Analyzer $($entry.Key): $($entry.Value)"
}
if ($failures.Count -gt 0) {
    throw ($failures -join [Environment]::NewLine)
}

Write-Host "Compatibility diagnostic contract passed: $($compatibilityIdentities.Count) exact identities."
Write-Host "Performance diagnostic contract passed: $($performanceIdentities.Count) exact identities."

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
