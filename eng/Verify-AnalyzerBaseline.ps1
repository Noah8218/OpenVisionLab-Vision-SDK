[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $SolutionPath,

    [Parameter(Mandatory = $true)]
    [string] $ArtifactsPath,

    [string] $BaselinePath = (Join-Path $PSScriptRoot 'analyzer-baseline.json'),

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

foreach ($entry in $orderedCounts.GetEnumerator()) {
    Write-Host "Analyzer $($entry.Key): $($entry.Value)"
}
if ($failures.Count -gt 0) {
    throw ($failures -join [Environment]::NewLine)
}

Write-Host "Analyzer no-regression gate passed: $actualTotal diagnostics at or below baseline."
