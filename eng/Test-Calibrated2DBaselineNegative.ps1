#requires -Version 7.0

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string] $OutputDirectory,
    [string] $VerifierPath = (Join-Path $PSScriptRoot 'Verify-Calibrated2DBaseline.ps1'),
    [switch] $IncludePathAliases,
    [string[]] $CaseIds = @()
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$outputRoot = [System.IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $outputRoot) {
    throw 'Select a new output directory to preserve earlier evidence.'
}
[System.IO.Directory]::CreateDirectory($outputRoot) | Out-Null
$verifier = (Resolve-Path -LiteralPath $VerifierPath).Path
$templatePath = Join-Path $PSScriptRoot '../docs/CALIBRATED_2D_BASELINE_MANIFEST_TEMPLATE.json'
$template = Get-Content -LiteralPath $templatePath -Raw
$reports = Join-Path $outputRoot 'reports'
[System.IO.Directory]::CreateDirectory($reports) | Out-Null
$utf8 = [System.Text.UTF8Encoding]::new($false)
$inputFiles = [ordered]@{
    'calibration/calibration.json' = '{"evidence":"synthetic-only-no-physical-approval"}'
    'recipe/recipe.json' = '{"operation":"contract-test-only"}'
    'raw/nominal-001.bin' = 'synthetic-nominal-input'
    'raw/defect-001.bin' = 'synthetic-defect-input'
    'ground-truth/nominal-001.json' = '{"source":"independent-synthetic-formula","value":0}'
    'ground-truth/defect-001.json' = '{"source":"independent-synthetic-formula","value":1}'
}
foreach ($entry in $inputFiles.GetEnumerator()) {
    $path = Join-Path $outputRoot $entry.Key
    [System.IO.Directory]::CreateDirectory((Split-Path -Parent $path)) | Out-Null
    [System.IO.File]::WriteAllText($path, $entry.Value, $utf8)
}
$hashes = @{}
foreach ($name in $inputFiles.Keys) {
    $hashes[$name] = (Get-FileHash -LiteralPath (Join-Path $outputRoot $name) -Algorithm SHA256).Hash
}
$cases = [System.Collections.Generic.List[object]]::new()

function New-ProbeSymbolicLink {
    param([string] $Name, [string] $LinkPath, [string] $TargetPath)

    try {
        [System.IO.File]::CreateSymbolicLink($LinkPath, $TargetPath) | Out-Null
        return $true
    }
    catch {
        $cases.Add([ordered]@{
            id = $Name; passed = $false; outcome = 'unavailable'; verifierExecuted = $false
            limitation = $_.Exception.Message; inputUnchanged = $true; linkPath = $LinkPath; targetPath = $TargetPath
        })
        Write-Host "$Name : unavailable (symbolic-link creation was denied; verifier not executed)"
        return $false
    }
}

function Invoke-ContractProbe {
    param(
        [Parameter(Mandatory = $true)][string] $Name,
        [scriptblock] $Mutate = {},
        [string] $ExpectedCode = '',
        [string] $RawJson = '',
        [string] $CollisionPath = '',
        [switch] $ArrayRoot,
        [ValidateSet('', 'HardLink', 'SymbolicLink', 'DirectoryLink')][string] $ReportAlias = '',
        [switch] $ReferenceAlias,
        [switch] $PreviousReport,
        [switch] $LockedReport
    )

    if ($CaseIds.Count -gt 0 -and $Name -notin $CaseIds) { return }

    $manifest = $template | ConvertFrom-Json -DateKind String
    $manifest.dataset.approvalId = 'synthetic-not-approved'
    $manifest.calibration.approvalId = 'synthetic-not-approved'
    $manifest.calibration.sha256 = $hashes[$manifest.calibration.path]
    $manifest.recipe.sha256 = $hashes[$manifest.recipe.path]
    foreach ($sample in $manifest.samples) {
        $sample.sourceSha256 = $hashes[$sample.sourcePath]
        $sample.groundTruthSha256 = $hashes[$sample.groundTruthPath]
    }
    & $Mutate $manifest
    if ($ReferenceAlias) {
        $alias = "recipe/$Name-link.json"
        if (-not (New-ProbeSymbolicLink $Name (Join-Path $outputRoot $alias) (Join-Path $outputRoot 'recipe/recipe.json'))) { return }
        $manifest.recipe.path = $alias
    }
    $manifestPath = Join-Path $outputRoot "$Name.json"
    $json = if ($RawJson.Length -gt 0) { $RawJson } else { $manifest | ConvertTo-Json -Depth 20 }
    if ($ArrayRoot) { $json = '[' + $json + ']' }
    [System.IO.File]::WriteAllText($manifestPath, $json, $utf8)
    $reportPath = Join-Path $reports "$Name.json"
    if ($CollisionPath -eq 'manifest') { $reportPath = $manifestPath }
    elseif ($CollisionPath.Length -gt 0) { $reportPath = Join-Path $outputRoot $CollisionPath }
    if ($ReportAlias.Length -gt 0) {
        $target = $reportPath
        if ($ReportAlias -eq 'DirectoryLink') {
            $directoryAlias = Join-Path $outputRoot "$Name-directory"
            $linkType = if ($IsWindows) { 'Junction' } else { 'SymbolicLink' }
            New-Item -ItemType $linkType -Path $directoryAlias -Target $outputRoot | Out-Null
            $reportPath = Join-Path $directoryAlias ([System.IO.Path]::GetRelativePath($outputRoot, $target))
        }
        else {
            $reportPath = Join-Path $reports "$Name.json"
            if ($ReportAlias -eq 'SymbolicLink') {
                if (-not (New-ProbeSymbolicLink $Name $reportPath $target)) { return }
            }
            else { New-Item -ItemType $ReportAlias -Path $reportPath -Target $target | Out-Null }
        }
    }
    $expectCollision = $CollisionPath.Length -gt 0 -and $ReportAlias -ne 'HardLink'
    $previousHash = ''
    if ($PreviousReport -or $LockedReport) {
        [System.IO.File]::WriteAllText($reportPath, '{"previousReport":"owned-test"}', $utf8)
        $previousHash = (Get-FileHash -LiteralPath $reportPath -Algorithm SHA256).Hash
    }
    $protected = @($manifestPath) + @($inputFiles.Keys | ForEach-Object { Join-Path $outputRoot $_ })
    $before = @{}
    foreach ($path in $protected) {
        $before[$path] = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash
    }
    $processName = if ($IsWindows) { 'pwsh.exe' } else { 'pwsh' }
    $reportLock = $null
    try {
        if ($LockedReport) { $reportLock = [System.IO.File]::Open($reportPath, [System.IO.FileMode]::Open, [System.IO.FileAccess]::Read, [System.IO.FileShare]::None) }
        $processOutput = @(& (Join-Path $PSHOME $processName) -NoProfile -File $verifier -ManifestPath $manifestPath -ReportPath $reportPath 2>&1)
        $exitCode = $LASTEXITCODE
    }
    finally {
        if ($null -ne $reportLock) { $reportLock.Dispose() }
    }
    [System.IO.File]::WriteAllText((Join-Path $reports "$Name.log"), ($processOutput -join [Environment]::NewLine), $utf8)
    $unchanged = $true
    foreach ($path in $protected) {
        $unchanged = $unchanged -and ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -eq $before[$path])
    }
    $actualReport = $null
    if (-not $expectCollision -and -not $LockedReport -and (Test-Path -LiteralPath $reportPath -PathType Leaf)) {
        $actualReport = Get-Content -LiteralPath $reportPath -Raw | ConvertFrom-Json -DateKind String
    }
    $passed = $unchanged
    $codes = @()
    if ($expectCollision) {
        $passed = $passed -and $exitCode -ne 0 -and ($processOutput -join ' ') -match 'report.inputCollision'
    }
    elseif ($LockedReport) {
        $passed = $passed -and $exitCode -ne 0 -and (Get-FileHash -LiteralPath $reportPath -Algorithm SHA256).Hash -eq $previousHash
    }
    elseif ($null -eq $actualReport) {
        $passed = $false
    }
    else {
        $codes = @($actualReport.errors | ForEach-Object { $_.code })
        if ($ExpectedCode.Length -eq 0) {
            $passed = $passed -and $exitCode -eq 0 -and $actualReport.status -eq 'passed' -and $actualReport.evidenceClass -eq 'synthetic-contract'
            $passed = $passed -and $actualReport.validatedFiles.Count -eq 6 -and @($actualReport.validatedFiles | Where-Object status -ne 'match').Count -eq 0
        }
        else {
            $passed = $passed -and $exitCode -ne 0 -and $actualReport.status -eq 'failed' -and $actualReport.evidenceClass -eq 'invalid' -and $codes -contains $ExpectedCode
        }
        $passed = $passed -and -not $actualReport.sideEffects.algorithmsExecuted -and -not $actualReport.sideEffects.calibrationApproved -and -not $actualReport.sideEffects.dataCopied
    }
    $temporaryPattern = '.' + [System.IO.Path]::GetFileName($reportPath) + '.*.tmp'
    $temporaryFiles = @(Get-ChildItem -LiteralPath (Split-Path -Parent $reportPath) -File -Filter $temporaryPattern)
    $passed = $passed -and $temporaryFiles.Count -eq 0
    if ($null -ne $actualReport) {
        [System.IO.File]::WriteAllText((Join-Path $reports "$Name-observed.json"), ($actualReport | ConvertTo-Json -Depth 12), $utf8)
    }
    $cases.Add([ordered]@{
        id = $Name; expectedCode = $ExpectedCode; collisionPath = $CollisionPath
        exitCode = $exitCode; codes = $codes; inputUnchanged = $unchanged; passed = $passed
        outcome = if ($passed) { 'passed' } else { 'failed' }; verifierExecuted = $true
        reportAlias = $ReportAlias; referenceAlias = [bool] $ReferenceAlias; lockedReport = [bool] $LockedReport; temporaryFilesRemaining = $temporaryFiles.Count
        manifestSha256 = $before[$manifestPath]; manifestPath = $manifestPath; reportPath = $reportPath
        evidenceClass = if ($null -eq $actualReport) { '' } else { $actualReport.evidenceClass }
    })
    # Restore only this run's generated fixture if an old verifier overwrote it.
    foreach ($entry in $inputFiles.GetEnumerator()) {
        [System.IO.File]::WriteAllText((Join-Path $outputRoot $entry.Key), $entry.Value, $utf8)
    }
    if (-not $unchanged) { [System.IO.File]::WriteAllText($manifestPath, $json, $utf8) }
    Write-Host "$Name : $passed"
}

Invoke-ContractProbe 'G01-valid'
Invoke-ContractProbe 'G02-null-root' -RawJson 'null' -ExpectedCode 'manifest.object'
Invoke-ContractProbe 'G03-empty-array-root' -RawJson '[]' -ExpectedCode 'manifest.object'
Invoke-ContractProbe 'G04-single-array-root' -ArrayRoot -ExpectedCode 'manifest.object'
Invoke-ContractProbe 'G05-string-schema' { param($m) $m.schemaVersion = '1' } 'schema.version'
Invoke-ContractProbe 'G06-boolean-schema' { param($m) $m.schemaVersion = $true } 'schema.version'
Invoke-ContractProbe 'G07-string-independence' { param($m) $m.groundTruth.independent = 'true' } 'groundTruth.independence'
Invoke-ContractProbe 'G08-numeric-independence' { param($m) $m.groundTruth.independent = 1 } 'groundTruth.independence'
Invoke-ContractProbe 'G09-sample-string-independence' { param($m) $m.samples[0].groundTruth.independent = 'true' } 'groundTruth.independence'
Invoke-ContractProbe 'G10-sample-numeric-independence' { param($m) $m.samples[1].groundTruth.independent = 1 } 'groundTruth.independence'
Invoke-ContractProbe 'G11-not-independent' { param($m) $m.groundTruth.independent = $false } 'groundTruth.independence'
Invoke-ContractProbe 'G12-null-object' { param($m) $m.calibration = $null } 'required.object'
Invoke-ContractProbe 'G13-scalar-object' { param($m) $m.sensor = 1 } 'required.object'
Invoke-ContractProbe 'G14-array-object' { param($m) $m.recipe = @($m.recipe) } 'required.object'
Invoke-ContractProbe 'G15-null-output' { param($m) $m.tolerance.outputs[0] = $null } 'required.object'
Invoke-ContractProbe 'G16-scalar-sample' { param($m) $m.samples[0] = 1 } 'sample.object'
Invoke-ContractProbe 'G17-string-number' { param($m) $m.sensor.acquisition.exposureUs = '1000' } 'required.number'
Invoke-ContractProbe 'G18-boolean-number' { param($m) $m.groundTruth.uncertainty = $true } 'required.number'
Invoke-ContractProbe 'G19-fractional-repeat' { param($m) $m.samples[0].groundTruth.repeatCount = 2.5 } 'required.integer'
Invoke-ContractProbe 'G20-duplicate-sample' { param($m) $m.samples[1].id = $m.samples[0].id } 'sample.id.duplicate'
Invoke-ContractProbe 'G21-duplicate-output' { param($m) $m.tolerance.outputs = @($m.tolerance.outputs[0], $m.tolerance.outputs[0]) } 'tolerance.output.duplicate'
Invoke-ContractProbe 'G22-wrong-unit' { param($m) $m.samples[0].groundTruth.unit = 'um' } 'unit.mismatch'
Invoke-ContractProbe 'G23-wrong-frame' { param($m) $m.groundTruth.frameId = 'other-fixture' } 'frame.mismatch'
Invoke-ContractProbe 'G24-no-utc' { param($m) $m.calibration.validFromUtc = '2026-01-01T00:00:00' } 'timestamp.utc'
Invoke-ContractProbe 'G25-invalid-period' { param($m) $m.calibration.validToUtc = $m.calibration.validFromUtc } 'calibration.validity'
Invoke-ContractProbe 'G26-wrong-hash' { param($m) $m.calibration.sha256 = '0' * 64 } 'file.hash'
Invoke-ContractProbe 'G27-missing-file' { param($m) $m.recipe.path = 'recipe/missing.json' } 'file.missing'
Invoke-ContractProbe 'G28-escape-path' { param($m) $m.recipe.path = '../outside.json' } 'path.escape'
Invoke-ContractProbe 'G29-absolute-path' { param($m) $m.recipe.path = Join-Path $outputRoot 'recipe/recipe.json' } 'path.rooted'
Invoke-ContractProbe 'G30-rate-out-of-range' { param($m) $m.tolerance.falseAcceptRate = 1.1 } 'required.number'
Invoke-ContractProbe 'G31-reversed-limit' { param($m) $m.tolerance.outputs[0].lsl = 1; $m.tolerance.outputs[0].usl = -1 } 'tolerance.order'
Invoke-ContractProbe 'G32-fractional-count' { param($m) $m.performance.cold.count = 2.5 } 'required.integer'
Invoke-ContractProbe 'G33-timing-order' { param($m) $m.performance.warm.p95Ms = 1 } 'performance.order'
Invoke-ContractProbe 'G34-truncated-json' -RawJson '{"dataset":' -ExpectedCode 'manifest.malformed'
Invoke-ContractProbe 'G35-report-is-manifest' -CollisionPath 'manifest'
Invoke-ContractProbe 'G36-report-is-calibration' -CollisionPath 'calibration/calibration.json'
Invoke-ContractProbe 'G37-report-is-raw' -CollisionPath 'raw/nominal-001.bin'
Invoke-ContractProbe 'G38-report-is-truth' -CollisionPath 'ground-truth/nominal-001.json'
Invoke-ContractProbe 'G39-report-is-recipe' -CollisionPath 'recipe/recipe.json'
Invoke-ContractProbe 'G40-valid-restart-and-recovery'
Invoke-ContractProbe 'G41-report-collision-with-invalid-hash' { param($m) $m.calibration.sha256 = 'invalid' } -CollisionPath 'calibration/calibration.json'
Invoke-ContractProbe 'G42-report-collision-with-missing-hash' { param($m) $m.recipe.sha256 = '' } -CollisionPath 'recipe/recipe.json'
Invoke-ContractProbe 'G43-scalar-root' -RawJson '1' -ExpectedCode 'manifest.object'
Invoke-ContractProbe 'G44-scalar-output' { param($m) $m.tolerance.outputs[0] = 1 } 'required.object'
Invoke-ContractProbe 'G45-null-sample' { param($m) $m.samples[0] = $null } 'sample.object'
Invoke-ContractProbe 'G46-valid-final-recovery'
if ($IncludePathAliases) {
    Invoke-ContractProbe 'G47-report-hard-link-manifest' -CollisionPath 'manifest' -ReportAlias 'HardLink'
    Invoke-ContractProbe 'G48-report-hard-link-calibration' -CollisionPath 'calibration/calibration.json' -ReportAlias 'HardLink'
    Invoke-ContractProbe 'G49-report-symbolic-link-manifest' -CollisionPath 'manifest' -ReportAlias 'SymbolicLink'
    Invoke-ContractProbe 'G50-report-symbolic-link-calibration' -CollisionPath 'calibration/calibration.json' -ReportAlias 'SymbolicLink'
    Invoke-ContractProbe 'G51-report-directory-link-manifest' -CollisionPath 'manifest' -ReportAlias 'DirectoryLink'
    Invoke-ContractProbe 'G52-report-directory-link-raw' -CollisionPath 'raw/nominal-001.bin' -ReportAlias 'DirectoryLink'
    Invoke-ContractProbe 'G53-reference-symbolic-link-report' -CollisionPath 'recipe/recipe.json' -ReferenceAlias
    Invoke-ContractProbe 'G54-replace-owned-previous-report' -PreviousReport
    Invoke-ContractProbe 'G55-locked-report-preserves-previous-and-input' -LockedReport
    Invoke-ContractProbe 'G56-valid-after-alias-and-write-failure'
}

$unmatchedCases = @($CaseIds | Where-Object { $_ -notin @($cases | ForEach-Object id) })
if ($unmatchedCases.Count -gt 0) { throw ('Selected cases did not run: ' + ($unmatchedCases -join ', ')) }

$unavailable = @($cases | Where-Object outcome -eq 'unavailable').Count
$failed = @($cases | Where-Object outcome -eq 'failed').Count
$passedCount = @($cases | Where-Object passed).Count
$summary = [ordered]@{
    status = if ($failed -gt 0) { 'failed' } elseif ($unavailable -gt 0) { 'blocked' } else { 'passed' }
    total = $cases.Count; passed = $passedCount; failed = $failed; unavailable = $unavailable; scope = 'synthetic-contract-only'
    verifierPath = $verifier; verifierSha256 = (Get-FileHash -LiteralPath $verifier -Algorithm SHA256).Hash
    templateSha256 = (Get-FileHash -LiteralPath $templatePath -Algorithm SHA256).Hash
    cases = $cases.ToArray(); physicalCalibrationApproved = $false
}
[System.IO.File]::WriteAllText((Join-Path $outputRoot 'summary.json'), (($summary | ConvertTo-Json -Depth 12) + [Environment]::NewLine), $utf8)
Write-Host "Calibrated input contract: $passedCount/$($cases.Count) passed; failed=$failed; unavailable=$unavailable"
if ($failed -gt 0) { exit 1 }
if ($unavailable -gt 0) { exit 2 }
