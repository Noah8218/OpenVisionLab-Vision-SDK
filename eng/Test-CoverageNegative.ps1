#requires -Version 7.0

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string] $SmokeAssembly,
    [Parameter(Mandatory = $true)][string] $CoveragePath,
    [Parameter(Mandatory = $true)][string] $SmokeReportPath,
    [Parameter(Mandatory = $true)][string] $OutputDirectory
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$root = [System.IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $root) { throw "Negative-test output already exists: $root" }
[System.IO.Directory]::CreateDirectory($root) | Out-Null
$xmlText = Get-Content -LiteralPath $CoveragePath -Raw
$jsonText = Get-Content -LiteralPath $SmokeReportPath -Raw
$verifier = Join-Path $PSScriptRoot 'Verify-Coverage.ps1'
$results = [System.Collections.Generic.List[object]]::new()

# Verify the supplied control through the same owner before corrupting isolated copies.
& $verifier -SmokeAssembly $SmokeAssembly -OutputPath $CoveragePath -SmokeReportPath $SmokeReportPath -ValidateOnly

$mutations = [ordered]@{
    'compute-missing' = { $compute.ParentNode.RemoveChild($compute) | Out-Null }
    'compute-duplicate' = { $compute.ParentNode.AppendChild($compute.CloneNode($true)) | Out-Null }
    'compute-below-floor' = { $compute.SetAttribute('line-rate', '0.229') }
    'compute-rate-missing' = { $compute.RemoveAttribute('line-rate') }
    'compute-rate-malformed' = { $compute.SetAttribute('line-rate', 'invalid') }
    'compute-rate-nan' = { $compute.SetAttribute('line-rate', 'NaN') }
    'compute-rate-infinite' = { $compute.SetAttribute('line-rate', 'Infinity') }
    'compute-rate-negative' = { $compute.SetAttribute('line-rate', '-0.01') }
    'compute-rate-over-one' = { $compute.SetAttribute('line-rate', '1.01') }
    'coverage-missing' = { $omitCoverage = $true }
    'execution-missing' = { $omitExecution = $true }
    'execution-list' = { $execution['Mode'] = 'List'; $execution['Status'] = 'Listed' }
    'execution-failed' = { $execution['Status'] = 'Failed'; $execution['Failure'] = 'isolated negative case' }
    'execution-filtered' = { $execution['Filter'] = 'Compute' }
    'execution-incomplete' = { $execution['EnumerationComplete'] = $false }
    'execution-empty' = { $execution['Total'] = 0; $execution['Passed'] = 0; $execution['Cases'] = @() }
    'execution-count-mismatch' = { $execution['Passed'] -= 1 }
    'execution-case-failed' = { $execution['Cases'][0]['Outcome'] = 'Failed' }
    'execution-case-unknown-scope' = { $execution['Cases'][0]['Scope'] = 'unrecognized' }
    'execution-case-duplicate' = { $execution['Cases'][1]['Name'] = $execution['Cases'][0]['Name'] }
    'execution-binary-mismatch' = { $execution['Assembly']['Sha256'] = '00' }
    'scope-missing' = { $execution['Scopes'] = @($execution['Scopes'] | Where-Object { $_['Scope'] -ne 'cuda-device' }) }
    'scope-count-mismatch' = { $execution['Scopes'][0]['Passed'] -= 1 }
    'scope-disabled-success' = { $execution['Scopes'][0]['Enabled'] = $false }
    'cuda-unexecuted-success' = { ($execution['Scopes'] | Where-Object { $_['Scope'] -eq 'cuda-device' })['Status'] = 'Passed' }
    'cuda-required-absent' = { $required = @('cuda-device') }
    'stale-output' = { $validateOnly = $false }
}
foreach ($entry in $mutations.GetEnumerator()) {
    $directory = Join-Path $root $entry.Key
    [System.IO.Directory]::CreateDirectory($directory) | Out-Null
    [xml] $coverage = $xmlText
    $execution = $jsonText | ConvertFrom-Json -AsHashtable
    $compute = $coverage.coverage.packages.package | Where-Object { $_.name -eq 'OpenVisionLab.Compute' }
    $omitCoverage = $false; $omitExecution = $false; $validateOnly = $true; $required = @('cpu')
    . $entry.Value
    $xmlPath = Join-Path $directory 'coverage.cobertura.xml'
    $jsonPath = Join-Path $directory 'smoke.json'
    if (-not $omitCoverage) { $coverage.Save($xmlPath) }
    if (-not $omitExecution) { $execution | ConvertTo-Json -Depth 20 | Set-Content -LiteralPath $jsonPath -Encoding utf8 }
    $rejected = $false; $message = $null
    try {
        & $verifier -SmokeAssembly $SmokeAssembly -OutputPath $xmlPath -SmokeReportPath $jsonPath -ValidateOnly:$validateOnly -RequiredScope $required *> (Join-Path $directory 'gate.log')
    }
    catch { $rejected = $true; $message = $_.Exception.Message }
    $results.Add(@{ Case = $entry.Key; Rejected = $rejected; Message = $message; Evidence = $directory })
    Write-Host "$($entry.Key): rejected=$rejected"
}
$results | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $root 'results.json') -Encoding utf8
if (@($results | Where-Object { -not $_.Rejected }).Count -gt 0) { throw 'A coverage fail-closed negative case was accepted.' }
Write-Host "Coverage negative gate passed: $($results.Count) cases | $root"
