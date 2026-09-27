#requires -Version 7.0

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $ManifestPath,

    [Parameter(Mandatory = $true)]
    [string] $ReportPath
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$script:errors = [System.Collections.Generic.List[object]]::new()
$script:validatedFiles = [System.Collections.Generic.List[object]]::new()

function Add-ValidationError {
    param(
        [Parameter(Mandatory = $true)][string] $Code,
        [Parameter(Mandatory = $true)][string] $Path,
        [Parameter(Mandatory = $true)][string] $Message
    )

    $script:errors.Add([ordered]@{
            code = $Code
            path = $Path
            message = $Message
        })
}

function Get-Field {
    param(
        [Parameter(Mandatory = $true)] $Object,
        [Parameter(Mandatory = $true)][string] $Name
    )

    if ($null -eq $Object) {
        return $null
    }
    $property = $Object.PSObject.Properties[$Name]
    if ($null -eq $property) {
        return $null
    }
    if ($property.Value -is [array]) {
        return ,$property.Value
    }
    return $property.Value
}

function Test-NonEmptyString {
    param(
        [Parameter(Mandatory = $true)] $Object,
        [Parameter(Mandatory = $true)][string] $Name,
        [Parameter(Mandatory = $true)][string] $Path
    )

    $value = Get-Field $Object $Name
    if ($value -isnot [string] -or [string]::IsNullOrWhiteSpace($value)) {
        Add-ValidationError 'required.string' $Path "Required non-empty string '$Name' is missing."
        return $null
    }
    return [string] $value
}

function Test-RequiredObject {
    param(
        [Parameter(Mandatory = $true)] $Object,
        [Parameter(Mandatory = $true)][string] $Name,
        [Parameter(Mandatory = $true)][string] $Path
    )

    $value = Get-Field $Object $Name
    if ($null -eq $value -or $value -is [array] -or $value -is [string] -or @($value.PSObject.Properties).Count -eq 0) {
        Add-ValidationError 'required.object' $Path "Required object '$Name' is missing."
        return $null
    }
    return $value
}

function Test-RequiredArray {
    param(
        [Parameter(Mandatory = $true)] $Object,
        [Parameter(Mandatory = $true)][string] $Name,
        [Parameter(Mandatory = $true)][string] $Path
    )

    $value = Get-Field $Object $Name
    if ($null -eq $value -or $value -isnot [array] -or @($value).Count -eq 0) {
        Add-ValidationError 'required.array' $Path "Required non-empty array '$Name' is missing."
        return @()
    }
    return @($value)
}

function Test-FiniteNumber {
    param(
        [Parameter(Mandatory = $true)] $Object,
        [Parameter(Mandatory = $true)][string] $Name,
        [Parameter(Mandatory = $true)][string] $Path,
        [double] $Minimum = [double]::NegativeInfinity,
        [double] $Maximum = [double]::PositiveInfinity
    )

    $value = Get-Field $Object $Name
    $numericTypes = @(
        [byte], [sbyte], [short], [ushort], [int], [uint], [long], [ulong], [float], [double], [decimal]
    )
    if ($null -eq $value -or $value -is [bool] -or $numericTypes -notcontains $value.GetType()) {
        Add-ValidationError 'required.number' $Path "Finite number '$Name' is missing or not a JSON number."
        return $null
    }
    $number = [double]::NaN
    $valid = $true
    try {
        $number = [double] $value
    }
    catch {
        $valid = $false
    }
    if (-not $valid -or [double]::IsNaN($number) -or [double]::IsInfinity($number) -or
        $number -lt $Minimum -or $number -gt $Maximum) {
        Add-ValidationError 'required.number' $Path "Finite number '$Name' is missing or outside the allowed range."
        return $null
    }
    return $number
}

function Test-PositiveInteger {
    param(
        [Parameter(Mandatory = $true)] $Object,
        [Parameter(Mandatory = $true)][string] $Name,
        [Parameter(Mandatory = $true)][string] $Path,
        [int] $Minimum = 1
    )

    $number = Test-FiniteNumber $Object $Name $Path $Minimum
    if ($null -ne $number -and [Math]::Truncate($number) -ne $number) {
        Add-ValidationError 'required.integer' $Path "Positive integer '$Name' is required."
        return $null
    }
    return $number
}

function Test-UtcTimestamp {
    param(
        [Parameter(Mandatory = $true)] $Object,
        [Parameter(Mandatory = $true)][string] $Name,
        [Parameter(Mandatory = $true)][string] $Path
    )

    $value = Test-NonEmptyString $Object $Name $Path
    if ($null -eq $value) {
        return $null
    }

    $parsed = [DateTimeOffset]::MinValue
    $valid = [DateTimeOffset]::TryParse(
        $value,
        [Globalization.CultureInfo]::InvariantCulture,
        [Globalization.DateTimeStyles]::RoundtripKind,
        [ref] $parsed)
    if (-not $valid -or $parsed.Offset -ne [TimeSpan]::Zero -or $value -notmatch '(Z|[+-]00:00)$') {
        Add-ValidationError 'timestamp.utc' $Path "'$Name' must be an ISO-8601 timestamp with an explicit UTC offset."
        return $null
    }
    return $parsed
}

function Test-Sha256 {
    param(
        [Parameter(Mandatory = $true)][string] $Value,
        [Parameter(Mandatory = $true)][string] $Path
    )

    if ($Value -notmatch '^[0-9A-Fa-f]{64}$') {
        Add-ValidationError 'hash.format' $Path 'SHA-256 must contain exactly 64 hexadecimal characters.'
        return $false
    }
    return $true
}

function Resolve-DataFile {
    param(
        [Parameter(Mandatory = $true)][string] $RelativePath,
        [Parameter(Mandatory = $true)][string] $FieldPath,
        [Parameter(Mandatory = $true)][string] $ManifestDirectory
    )

    if ([System.IO.Path]::IsPathRooted($RelativePath)) {
        Add-ValidationError 'path.rooted' $FieldPath 'Data paths must be relative to the manifest.'
        return $null
    }
    try {
        $candidate = [System.IO.Path]::GetFullPath((Join-Path $ManifestDirectory $RelativePath))
    }
    catch {
        Add-ValidationError 'path.invalid' $FieldPath 'Data path is not a valid relative file path.'
        return $null
    }
    $prefix = $ManifestDirectory.TrimEnd(
        [System.IO.Path]::DirectorySeparatorChar,
        [System.IO.Path]::AltDirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
    if (-not $candidate.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) {
        Add-ValidationError 'path.escape' $FieldPath 'Data path escapes the manifest directory.'
        return $null
    }
    return $candidate
}

function Test-ReferencedFile {
    param(
        [Parameter(Mandatory = $true)] $Object,
        [Parameter(Mandatory = $true)][string] $PathName,
        [Parameter(Mandatory = $true)][string] $HashName,
        [Parameter(Mandatory = $true)][string] $ObjectPath,
        [Parameter(Mandatory = $true)][string] $ManifestDirectory
    )

    $relativePath = Test-NonEmptyString $Object $PathName "$ObjectPath.$PathName"
    $expectedHash = Test-NonEmptyString $Object $HashName "$ObjectPath.$HashName"
    if ($null -eq $expectedHash) {
        return
    }
    $validHash = Test-Sha256 $expectedHash "$ObjectPath.$HashName"
    if ($null -eq $relativePath -or -not $validHash) {
        return
    }
    $filePath = Resolve-DataFile $relativePath "$ObjectPath.$PathName" $ManifestDirectory
    if ($null -eq $filePath) {
        return
    }
    if (-not (Test-Path -LiteralPath $filePath -PathType Leaf)) {
        Add-ValidationError 'file.missing' "$ObjectPath.$PathName" "Referenced file '$relativePath' does not exist."
        return
    }
    $actualHash = (Get-FileHash -LiteralPath $filePath -Algorithm SHA256).Hash.ToUpperInvariant()
    $status = if ([string]::Equals($actualHash, $expectedHash, [StringComparison]::OrdinalIgnoreCase)) { 'match' } else { 'mismatch' }
    $script:validatedFiles.Add([ordered]@{
            path = $relativePath.Replace('\', '/')
            expectedSha256 = $expectedHash.ToUpperInvariant()
            actualSha256 = $actualHash
            status = $status
        })
    if ($status -ne 'match') {
        Add-ValidationError 'file.hash' "$ObjectPath.$HashName" "Referenced file '$relativePath' has a SHA-256 mismatch."
    }
}

function Test-PerformanceWindow {
    param(
        [Parameter(Mandatory = $true)] $Object,
        [Parameter(Mandatory = $true)][string] $Name,
        [Parameter(Mandatory = $true)][string] $Path
    )

    $window = Test-RequiredObject $Object $Name $Path
    if ($null -eq $window) {
        return
    }
    [void] (Test-PositiveInteger $window 'count' "$Path.count")
    $median = Test-FiniteNumber $window 'medianMs' "$Path.medianMs" 0
    $p95 = Test-FiniteNumber $window 'p95Ms' "$Path.p95Ms" 0
    $p99 = Test-FiniteNumber $window 'p99Ms' "$Path.p99Ms" 0
    if ($null -ne $median -and $null -ne $p95 -and $p95 -lt $median) {
        Add-ValidationError 'performance.order' "$Path.p95Ms" 'p95Ms must be greater than or equal to medianMs.'
    }
    if ($null -ne $p95 -and $null -ne $p99 -and $p99 -lt $p95) {
        Add-ValidationError 'performance.order' "$Path.p99Ms" 'p99Ms must be greater than or equal to p95Ms.'
    }
}

function New-ValidationReport {
    param(
        [Parameter(Mandatory = $true)][string] $Status,
        [Parameter(Mandatory = $true)][string] $EvidenceClass,
        [Parameter(Mandatory = $true)][AllowEmptyString()][string] $ManifestSha256
    )

    $sortedErrors = @($script:errors | Sort-Object code, path, message)
    $sortedFiles = @($script:validatedFiles | Sort-Object path)
    return [ordered]@{
        schemaVersion = 1
        status = $Status
        evidenceClass = $EvidenceClass
        manifestSha256 = $ManifestSha256
        errors = $sortedErrors
        validatedFiles = $sortedFiles
        sideEffects = [ordered]@{
            algorithmsExecuted = $false
            calibrationApproved = $false
            dataCopied = $false
        }
    }
}

$manifestFullPath = $null
$manifestDirectory = $null
$reportFullPath = [System.IO.Path]::GetFullPath($ReportPath)
$reportParent = Split-Path -Parent $reportFullPath
if (-not (Test-Path -LiteralPath $reportParent -PathType Container)) {
    throw "ReportPath parent must already exist; no directories are created by this verifier: '$reportParent'."
}

try {
    $manifestFullPath = (Resolve-Path -LiteralPath $ManifestPath -ErrorAction Stop).Path
    $manifestDirectory = Split-Path -Parent $manifestFullPath
    $manifestBytes = [System.IO.File]::ReadAllBytes($manifestFullPath)
    $manifestSha256 = [System.BitConverter]::ToString(
        [System.Security.Cryptography.SHA256]::HashData($manifestBytes)).Replace('-', '').ToUpperInvariant()
    $manifest = Get-Content -LiteralPath $manifestFullPath -Raw | ConvertFrom-Json -DateKind String
}
catch {
    $manifestSha256 = ''
    Add-ValidationError 'manifest.malformed' '$' 'Manifest must be readable UTF-8 JSON.'
    $manifest = $null
}

$evidenceClass = 'invalid'
if ($null -ne $manifest) {
    $schemaVersion = Get-Field $manifest 'schemaVersion'
    if ($schemaVersion -ne 1) {
        Add-ValidationError 'schema.version' '$.schemaVersion' 'schemaVersion must be 1.'
    }

    $dataset = Test-RequiredObject $manifest 'dataset' '$.dataset'
    if ($null -ne $dataset) {
        $datasetId = Test-NonEmptyString $dataset 'id' '$.dataset.id'
        $datasetRevision = Test-NonEmptyString $dataset 'revision' '$.dataset.revision'
        $purpose = Test-NonEmptyString $dataset 'purpose' '$.dataset.purpose'
        $sourceCommit = Test-NonEmptyString $dataset 'sourceCommit' '$.dataset.sourceCommit'
        $split = Test-NonEmptyString $dataset 'split' '$.dataset.split'
        if ($null -ne $sourceCommit -and $sourceCommit -notmatch '^[0-9A-Fa-f]{40}$') {
            Add-ValidationError 'dataset.commit' '$.dataset.sourceCommit' 'sourceCommit must be a 40-character Git SHA-1.'
        }
        if ($null -ne $split -and $split -notin @('development', 'locked-validation', 'challenge')) {
            Add-ValidationError 'dataset.split' '$.dataset.split' 'split must be development, locked-validation, or challenge.'
        }
        if ($null -ne $purpose -and $purpose -notin @('synthetic', 'production')) {
            Add-ValidationError 'dataset.purpose' '$.dataset.purpose' 'purpose must be synthetic or production.'
        }
        if ($purpose -eq 'production') {
            [void] (Test-NonEmptyString $dataset 'approvalId' '$.dataset.approvalId')
            $evidenceClass = 'production-candidate'
        }
        elseif ($purpose -eq 'synthetic') {
            $evidenceClass = 'synthetic-contract'
        }
    }

    $sensor = Test-RequiredObject $manifest 'sensor' '$.sensor'
    if ($null -ne $sensor) {
        [void] (Test-NonEmptyString $sensor 'model' '$.sensor.model')
        [void] (Test-NonEmptyString $sensor 'serial' '$.sensor.serial')
        [void] (Test-NonEmptyString $sensor 'firmware' '$.sensor.firmware')
        [void] (Test-NonEmptyString $sensor 'lens' '$.sensor.lens')
        $acquisition = Test-RequiredObject $sensor 'acquisition' '$.sensor.acquisition'
        if ($null -ne $acquisition) {
            [void] (Test-FiniteNumber $acquisition 'exposureUs' '$.sensor.acquisition.exposureUs' 0)
            [void] (Test-FiniteNumber $acquisition 'gain' '$.sensor.acquisition.gain' 0)
            [void] (Test-NonEmptyString $acquisition 'lighting' '$.sensor.acquisition.lighting')
            [void] (Test-NonEmptyString $acquisition 'trigger' '$.sensor.acquisition.trigger')
            [void] (Test-NonEmptyString $acquisition 'fixture' '$.sensor.acquisition.fixture')
        }
    }

    $calibration = Test-RequiredObject $manifest 'calibration' '$.calibration'
    $calibrationUnit = $null
    $calibrationFrame = $null
    if ($null -ne $calibration) {
        [void] (Test-NonEmptyString $calibration 'id' '$.calibration.id')
        $calibrationFrame = Test-NonEmptyString $calibration 'frameId' '$.calibration.frameId'
        $calibrationUnit = Test-NonEmptyString $calibration 'unit' '$.calibration.unit'
        $validFromUtc = Test-UtcTimestamp $calibration 'validFromUtc' '$.calibration.validFromUtc'
        $validToUtc = Test-UtcTimestamp $calibration 'validToUtc' '$.calibration.validToUtc'
        [void] (Test-NonEmptyString $calibration 'approvalId' '$.calibration.approvalId')
        Test-ReferencedFile $calibration 'path' 'sha256' '$.calibration' $manifestDirectory
        if ($null -ne $validFromUtc -and $null -ne $validToUtc -and $validFromUtc -ge $validToUtc) {
            Add-ValidationError 'calibration.validity' '$.calibration.validToUtc' 'validToUtc must be later than validFromUtc.'
        }
        if ($null -ne $calibrationUnit -and $calibrationUnit -notin @('mm', 'um', 'px')) {
            Add-ValidationError 'calibration.unit' '$.calibration.unit' 'unit must be an explicit supported unit: mm, um, or px.'
        }
    }

    $recipe = Test-RequiredObject $manifest 'recipe' '$.recipe'
    if ($null -ne $recipe) {
        [void] (Test-NonEmptyString $recipe 'id' '$.recipe.id')
        [void] (Test-NonEmptyString $recipe 'version' '$.recipe.version')
        Test-ReferencedFile $recipe 'path' 'sha256' '$.recipe' $manifestDirectory
    }

    $samples = Test-RequiredArray $manifest 'samples' '$.samples'
    $hasNominal = $false
    $hasDefect = $false
    $sampleIds = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $sampleIndex = 0
    foreach ($sample in $samples) {
        $samplePath = "$.samples[$sampleIndex]"
        if ($null -eq $sample -or $sample -is [string]) {
            Add-ValidationError 'sample.object' $samplePath 'Each sample must be an object.'
            $sampleIndex++
            continue
        }
        $sampleId = Test-NonEmptyString $sample 'id' "$samplePath.id"
        if ($null -ne $sampleId -and -not $sampleIds.Add($sampleId)) {
            Add-ValidationError 'sample.id.duplicate' "$samplePath.id" "Sample id '$sampleId' is duplicated."
        }
        [void] (Test-NonEmptyString $sample 'partFamily' "$samplePath.partFamily")
        [void] (Test-NonEmptyString $sample 'lot' "$samplePath.lot")
        [void] (Test-NonEmptyString $sample 'fixture' "$samplePath.fixture")
        [void] (Test-NonEmptyString $sample 'operator' "$samplePath.operator")
        $labels = Test-RequiredArray $sample 'labels' "$samplePath.labels"
        foreach ($label in $labels) {
            if ($label -isnot [string] -or [string]::IsNullOrWhiteSpace($label)) {
                Add-ValidationError 'sample.label' "$samplePath.labels" 'Every sample label must be a non-empty string.'
                continue
            }
            $hasNominal = $hasNominal -or [string]::Equals($label, 'nominal', [StringComparison]::OrdinalIgnoreCase)
            $hasDefect = $hasDefect -or $label -match '^defect($|[:/])'
        }
        Test-ReferencedFile $sample 'sourcePath' 'sourceSha256' $samplePath $manifestDirectory
        Test-ReferencedFile $sample 'groundTruthPath' 'groundTruthSha256' $samplePath $manifestDirectory
        $sampleGroundTruth = Test-RequiredObject $sample 'groundTruth' "$samplePath.groundTruth"
        if ($null -ne $sampleGroundTruth) {
            [void] (Test-NonEmptyString $sampleGroundTruth 'method' "$samplePath.groundTruth.method")
            $sampleGroundTruthUnit = Test-NonEmptyString $sampleGroundTruth 'unit' "$samplePath.groundTruth.unit"
            $sampleGroundTruthFrame = Test-NonEmptyString $sampleGroundTruth 'frameId' "$samplePath.groundTruth.frameId"
            [void] (Test-FiniteNumber $sampleGroundTruth 'uncertainty' "$samplePath.groundTruth.uncertainty" 0)
            [void] (Test-PositiveInteger $sampleGroundTruth 'repeatCount' "$samplePath.groundTruth.repeatCount" 2)
            if ((Get-Field $sampleGroundTruth 'independent') -ne $true) {
                Add-ValidationError 'groundTruth.independence' "$samplePath.groundTruth.independent" 'Ground truth must be explicitly independent.'
            }
            if ($null -ne $calibrationUnit -and $null -ne $sampleGroundTruthUnit -and
                -not [string]::Equals($calibrationUnit, $sampleGroundTruthUnit, [StringComparison]::Ordinal)) {
                Add-ValidationError 'unit.mismatch' "$samplePath.groundTruth.unit" 'Ground-truth unit must match calibration unit.'
            }
            if ($null -ne $calibrationFrame -and $null -ne $sampleGroundTruthFrame -and
                -not [string]::Equals($calibrationFrame, $sampleGroundTruthFrame, [StringComparison]::Ordinal)) {
                Add-ValidationError 'frame.mismatch' "$samplePath.groundTruth.frameId" 'Ground-truth frameId must match calibration frameId.'
            }
        }
        $sampleIndex++
    }
    if (-not $hasNominal) {
        Add-ValidationError 'samples.nominal' '$.samples.labels' 'At least one nominal sample is required.'
    }
    if (-not $hasDefect) {
        Add-ValidationError 'samples.defect' '$.samples.labels' 'At least one labeled defect sample is required.'
    }

    $groundTruth = Test-RequiredObject $manifest 'groundTruth' '$.groundTruth'
    if ($null -ne $groundTruth) {
        [void] (Test-NonEmptyString $groundTruth 'method' '$.groundTruth.method')
        $groundTruthUnit = Test-NonEmptyString $groundTruth 'unit' '$.groundTruth.unit'
        $groundTruthFrame = Test-NonEmptyString $groundTruth 'frameId' '$.groundTruth.frameId'
        [void] (Test-FiniteNumber $groundTruth 'uncertainty' '$.groundTruth.uncertainty' 0)
        [void] (Test-PositiveInteger $groundTruth 'repeatCount' '$.groundTruth.repeatCount' 2)
        if ((Get-Field $groundTruth 'independent') -ne $true) {
            Add-ValidationError 'groundTruth.independence' '$.groundTruth.independent' 'Ground truth must be explicitly independent.'
        }
        if ($null -ne $calibrationUnit -and $null -ne $groundTruthUnit -and
            -not [string]::Equals($calibrationUnit, $groundTruthUnit, [StringComparison]::Ordinal)) {
            Add-ValidationError 'unit.mismatch' '$.groundTruth.unit' 'Ground-truth unit must match calibration unit.'
        }
        if ($null -ne $calibrationFrame -and $null -ne $groundTruthFrame -and
            -not [string]::Equals($calibrationFrame, $groundTruthFrame, [StringComparison]::Ordinal)) {
            Add-ValidationError 'frame.mismatch' '$.groundTruth.frameId' 'Ground-truth frameId must match calibration frameId.'
        }
    }

    $tolerance = Test-RequiredObject $manifest 'tolerance' '$.tolerance'
    if ($null -ne $tolerance) {
        $toleranceUnit = Test-NonEmptyString $tolerance 'unit' '$.tolerance.unit'
        $outputs = Test-RequiredArray $tolerance 'outputs' '$.tolerance.outputs'
        $outputNames = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
        $outputIndex = 0
        foreach ($output in $outputs) {
            $outputPath = "$.tolerance.outputs[$outputIndex]"
            $outputName = Test-NonEmptyString $output 'name' "$outputPath.name"
            if ($null -ne $outputName -and -not $outputNames.Add($outputName)) {
                Add-ValidationError 'tolerance.output.duplicate' "$outputPath.name" "Tolerance output '$outputName' is duplicated."
            }
            $lsl = Test-FiniteNumber $output 'lsl' "$outputPath.lsl"
            $usl = Test-FiniteNumber $output 'usl' "$outputPath.usl"
            if ($null -ne $lsl -and $null -ne $usl -and $lsl -ge $usl) {
                Add-ValidationError 'tolerance.order' $outputPath 'lsl must be less than usl.'
            }
            $outputIndex++
        }
        [void] (Test-FiniteNumber $tolerance 'falseAcceptRate' '$.tolerance.falseAcceptRate' 0 1)
        [void] (Test-FiniteNumber $tolerance 'falseRejectRate' '$.tolerance.falseRejectRate' 0 1)
        [void] (Test-NonEmptyString $tolerance 'ambiguousPolicy' '$.tolerance.ambiguousPolicy')
        if ($null -ne $calibrationUnit -and $null -ne $toleranceUnit -and
            -not [string]::Equals($calibrationUnit, $toleranceUnit, [StringComparison]::Ordinal)) {
            Add-ValidationError 'unit.mismatch' '$.tolerance.unit' 'Tolerance unit must match calibration unit.'
        }
    }

    $performance = Test-RequiredObject $manifest 'performance' '$.performance'
    if ($null -ne $performance) {
        [void] (Test-NonEmptyString $performance 'targetHardware' '$.performance.targetHardware')
        [void] (Test-NonEmptyString $performance 'runtime' '$.performance.runtime')
        Test-PerformanceWindow $performance 'cold' '$.performance.cold'
        Test-PerformanceWindow $performance 'warm' '$.performance.warm'
        [void] (Test-FiniteNumber $performance 'memoryMb' '$.performance.memoryMb' 0)
        [void] (Test-FiniteNumber $performance 'taktMs' '$.performance.taktMs' 0)
    }
}

if ([string]::IsNullOrWhiteSpace($manifestSha256)) {
    $manifestSha256 = ''
}
$status = if ($script:errors.Count -eq 0) { 'passed' } else { 'failed' }
if ($status -eq 'failed') {
    $evidenceClass = 'invalid'
}
$report = New-ValidationReport $status $evidenceClass $manifestSha256
[System.IO.File]::WriteAllText(
    $reportFullPath,
    (($report | ConvertTo-Json -Depth 8) + [Environment]::NewLine),
    [System.Text.UTF8Encoding]::new($false))

Write-Host "Calibrated 2D baseline verification: $status"
Write-Host "Report: $reportFullPath"
if ($status -ne 'passed') {
    Write-Host "Validation errors: $($script:errors.Count)"
    exit 1
}
