param(
    [switch]$SelfTest,
    [string]$AttemptRoot,
    [string]$BaselineApplication,
    [string]$CurrentApplication,
    [string]$ComparerApplication,
    [string]$ComparerCommit
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$BaselineCommit = 'c74b3bb5bf2f237eef800e50ef6951109bf07cc5'
$CurrentCommit = '3f6e35beb951b8412e6fcd116c959f0a5c4d9a99'
$HarnessCommit = '3f6e35beb951b8412e6fcd116c959f0a5c4d9a99'
$CpuThresholdPercent = 20.0
$CpuSampleCount = 6
$ReadinessAdmissionSeconds = 900
$ReadinessRetrySeconds = 10

function Require {
    param(
        [bool]$Condition,
        [string]$Message
    )

    if (-not $Condition) {
        throw $Message
    }
}

function Get-SessionPlan {
    $plan = [System.Collections.Generic.List[object]]::new()
    foreach ($workload in @('planar-direct-v1', 'planar-boundary-v1')) {
        for ($round = 1; $round -le 5; $round++) {
            $prefix = 'R' + $round.ToString('00') + '-'
            foreach ($session in @(
                    [pscustomobject]@{ Suffix = 'A1'; Target = 'baseline' },
                    [pscustomobject]@{ Suffix = 'B1'; Target = 'current' },
                    [pscustomobject]@{ Suffix = 'B2'; Target = 'current' },
                    [pscustomobject]@{ Suffix = 'A2'; Target = 'baseline' }
                )) {
                $plan.Add([pscustomobject]@{
                        Workload = $workload
                        Round = $round
                        Session = $prefix + $session.Suffix
                        Target = $session.Target
                    })
            }
        }
    }

    return @($plan)
}

function Get-BlockingWorkloads {
    $blocking = [System.Collections.Generic.List[object]]::new()
    foreach ($process in @(Get-CimInstance Win32_Process)) {
        $name = [string]$process.Name
        $commandLine = [string]$process.CommandLine
        $isDirect = $name -in @(
            'vstest.console.exe',
            'testhost.exe',
            'OpenVisionLab.Inspection.Smoke.exe',
            'OpenVisionLab.Vision3D.Benchmark.exe')
        $isDotnetWorkload = $name -eq 'dotnet.exe' `
            -and ($commandLine -match '(?i)(^|\s)(build|test|vstest|run)(\s|$)' `
                -or $commandLine -match '(?i)(Tests|Smoke|Benchmark)\.dll')
        if ($isDirect -or $isDotnetWorkload) {
            $blocking.Add([pscustomobject]@{
                    ProcessId = [int]$process.ProcessId
                    Name = $name
                    CommandLine = $commandLine
                })
        }
    }

    return @($blocking | Sort-Object ProcessId)
}

function Get-CpuSamples {
    $samples = [System.Collections.Generic.List[object]]::new()
    for ($index = 0; $index -lt $CpuSampleCount; $index++) {
        $processors = @(Get-CimInstance Win32_Processor)
        Require ($processors.Count -gt 0) 'Win32_Processor returned no records.'
        $loads = @(
            foreach ($processor in $processors) {
                Require ($null -ne $processor.LoadPercentage) `
                    'Win32_Processor.LoadPercentage is unavailable.'
                [double]$processor.LoadPercentage
            }
        )
        $samples.Add([pscustomobject]@{
                Ordinal = $index + 1
                CapturedUtc = [DateTimeOffset]::UtcNow
                ProcessorLoadPercentages = $loads
                MaximumLoadPercentage = [double](
                    ($loads | Measure-Object -Maximum).Maximum)
            })
        if ($index -lt ($CpuSampleCount - 1)) {
            Start-Sleep -Seconds 1
        }
    }

    return @($samples)
}

function Wait-ForReadiness {
    param(
        [string]$Stage
    )

    $started = [DateTimeOffset]::UtcNow
    $deadline = $started.AddSeconds($ReadinessAdmissionSeconds)
    $windows = [System.Collections.Generic.List[object]]::new()
    do {
        $before = @(Get-BlockingWorkloads)
        $samples = @(Get-CpuSamples)
        $after = @(Get-BlockingWorkloads)
        $passed = $before.Count -eq 0 `
            -and $after.Count -eq 0 `
            -and @(
                $samples |
                    Where-Object {
                        [double]$_.MaximumLoadPercentage `
                            -gt $CpuThresholdPercent
                    }
            ).Count -eq 0
        $window = [pscustomobject]@{
            Ordinal = $windows.Count + 1
            CapturedUtc = [DateTimeOffset]::UtcNow
            BlockingWorkloadsBefore = $before
            CpuSamples = $samples
            BlockingWorkloadsAfter = $after
            Passed = $passed
        }
        $windows.Add($window)
        if ($passed) {
            return [pscustomobject]@{
                SchemaVersion = 'openvisionlab-paired-readiness-v2'
                Stage = $Stage
                CapturedUtc = $window.CapturedUtc
                CpuThresholdPercent = $CpuThresholdPercent
                CpuSampleCount = $CpuSampleCount
                BlockingWorkloadsBefore = @($before)
                BlockingWorkloadsAfter = @($after)
                CpuSamples = @($samples)
                Windows = @($windows)
                Passed = $true
            }
        }

        Write-Host (
            'READINESS-WAIT | ' + $Stage + ' | window=' +
            $window.Ordinal)
        if ([DateTimeOffset]::UtcNow -lt $deadline) {
            Start-Sleep -Seconds $ReadinessRetrySeconds
        }
    } while ([DateTimeOffset]::UtcNow -lt $deadline)

    return [pscustomobject]@{
        SchemaVersion = 'openvisionlab-paired-readiness-v2'
        Stage = $Stage
        CapturedUtc = [DateTimeOffset]::UtcNow
        CpuThresholdPercent = $CpuThresholdPercent
        CpuSampleCount = $CpuSampleCount
        BlockingWorkloadsBefore = @()
        BlockingWorkloadsAfter = @()
        CpuSamples = @()
        Windows = @($windows)
        Passed = $false
    }
}

function Write-Json {
    param(
        [string]$Path,
        [object]$Value
    )

    $parent = Split-Path -Parent $Path
    if (-not (Test-Path -LiteralPath $parent)) {
        New-Item -ItemType Directory -Path $parent | Out-Null
    }
    $Value | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $Path -Encoding utf8
}

function Invoke-Application {
    param(
        [string]$Application,
        [string[]]$Arguments
    )

    if ([IO.Path]::GetExtension($Application) -eq '.dll') {
        & dotnet $Application @Arguments 2>&1 |
            ForEach-Object { Write-Host $_ }
    }
    else {
        & $Application @Arguments 2>&1 |
            ForEach-Object { Write-Host $_ }
    }
    $exitCode = $LASTEXITCODE
    return [int]$exitCode
}

function Invoke-Benchmark {
    param(
        [string]$Application,
        [string]$Mode,
        [string]$Workload,
        [string]$Session,
        [string]$TargetCommit,
        [string]$Output
    )

    Write-Host "RUN | $Mode | $Workload | $Session"
    $exitCode = Invoke-Application $Application @(
        '--mode', $Mode,
        '--workload', $Workload,
        '--output', $Output,
        '--target-commit', $TargetCommit,
        '--session', $Session)
    Require ($exitCode -eq 0) `
        "Benchmark failed with exit code ${exitCode}: $Session"
    Require (Test-Path -LiteralPath $Output -PathType Leaf) `
        "Benchmark output is missing: $Output"
}

function Get-RelativePath {
    param(
        [string]$Root,
        [string]$Path
    )

    return [IO.Path]::GetRelativePath($Root, $Path)
}

function Invoke-SelfTest {
    $plan = @(Get-SessionPlan)
    Require ($plan.Count -eq 40) 'Session plan count changed.'
    $expected = @(
        'planar-direct-v1|R01-A1|baseline',
        'planar-direct-v1|R01-B1|current',
        'planar-direct-v1|R01-B2|current',
        'planar-direct-v1|R01-A2|baseline',
        'planar-boundary-v1|R05-A1|baseline',
        'planar-boundary-v1|R05-B1|current',
        'planar-boundary-v1|R05-B2|current',
        'planar-boundary-v1|R05-A2|baseline')
    $actual = @(
        $plan[0..3] + $plan[36..39] |
            ForEach-Object {
                $_.Workload + '|' + $_.Session + '|' + $_.Target
            })
    Require (($actual -join ';') -eq ($expected -join ';')) `
        'Session plan order changed.'
    $pwsh = (Get-Process -Id $PID).Path
    $childExitCode = Invoke-Application $pwsh @(
        '-NoProfile',
        '-Command',
        'Write-Output paired-runner-child; exit 0')
    Require ($childExitCode -eq 0) `
        'Child process exit-code handling changed.'
    $childInvestigationExitCode = Invoke-Application $pwsh @(
        '-NoProfile',
        '-Command',
        'exit 2')
    Require ($childInvestigationExitCode -eq 2) `
        'Investigation exit-code handling changed.'
    Write-Host 'PASS | paired-runner-self-test | 40 sessions'
}

if ($SelfTest) {
    Invoke-SelfTest
    exit 0
}

Require ([Environment]::OSVersion.Platform -eq [PlatformID]::Win32NT) `
    'The official paired run requires Windows.'
foreach ($value in @(
        $AttemptRoot,
        $BaselineApplication,
        $CurrentApplication,
        $ComparerApplication,
        $ComparerCommit)) {
    Require (-not [string]::IsNullOrWhiteSpace($value)) `
        'Official paired-run parameters are incomplete.'
}
Require ($ComparerCommit -match '^[0-9a-fA-F]{40}$') `
    'ComparerCommit must be a full commit SHA.'

$AttemptRoot = [IO.Path]::GetFullPath($AttemptRoot)
$BaselineApplication = (Resolve-Path -LiteralPath $BaselineApplication).Path
$CurrentApplication = (Resolve-Path -LiteralPath $CurrentApplication).Path
$ComparerApplication = (Resolve-Path -LiteralPath $ComparerApplication).Path
if (-not (Test-Path -LiteralPath $AttemptRoot)) {
    New-Item -ItemType Directory -Path $AttemptRoot | Out-Null
}
Require (@(Get-ChildItem -LiteralPath $AttemptRoot -Force).Count -eq 0) `
    'AttemptRoot must be empty before an official run.'

$rawRoot = Join-Path $AttemptRoot 'raw'
$summaryRoot = Join-Path $AttemptRoot 'summary'
New-Item -ItemType Directory -Path $rawRoot, $summaryRoot | Out-Null
$provenance = [ordered]@{
    SchemaVersion = 'openvisionlab-paired-run-provenance-v2'
    CreatedUtc = [DateTimeOffset]::UtcNow
    BaselineCommit = $BaselineCommit
    CurrentCommit = $CurrentCommit
    HarnessCommit = $HarnessCommit
    ComparerCommit = $ComparerCommit.ToLowerInvariant()
    CpuThresholdPercent = $CpuThresholdPercent
    CpuSampleCount = $CpuSampleCount
    BaselineApplication = $BaselineApplication
    BaselineApplicationSha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $BaselineApplication).Hash
    CurrentApplication = $CurrentApplication
    CurrentApplicationSha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $CurrentApplication).Hash
    ComparerApplication = $ComparerApplication
    ComparerApplicationSha256 = (Get-FileHash -Algorithm SHA256 -LiteralPath $ComparerApplication).Hash
}
Write-Json (Join-Path $AttemptRoot 'runner-provenance.json') $provenance

$accuracy = [ordered]@{}
foreach ($workload in @('planar-direct-v1', 'planar-boundary-v1')) {
    foreach ($target in @(
            [pscustomobject]@{
                Name = 'Baseline'; Label = 'A-accuracy'
                Application = $BaselineApplication; Commit = $BaselineCommit
            },
            [pscustomobject]@{
                Name = 'Current'; Label = 'B-accuracy'
                Application = $CurrentApplication; Commit = $CurrentCommit
            })) {
        $output = Join-Path $rawRoot (
            "$workload-$($target.Label).json")
        Invoke-Benchmark `
            -Application $target.Application `
            -Mode 'accuracy' `
            -Workload $workload `
            -Session $target.Label `
            -TargetCommit $target.Commit `
            -Output $output
        $key = if ($workload -eq 'planar-direct-v1') {
            'Direct' + $target.Name
        }
        else {
            'Boundary' + $target.Name
        }
        $accuracy[$key] = Get-RelativePath $AttemptRoot $output
    }
}

$workloadInputs = [System.Collections.Generic.List[object]]::new()
foreach ($workload in @('planar-direct-v1', 'planar-boundary-v1')) {
    $rounds = [System.Collections.Generic.List[object]]::new()
    for ($round = 1; $round -le 5; $round++) {
        $roundInput = [ordered]@{ Ordinal = $round }
        $prefix = 'R' + $round.ToString('00') + '-'
        foreach ($session in @(
                [pscustomobject]@{
                    Suffix = 'A1'; Application = $BaselineApplication
                    Commit = $BaselineCommit
                },
                [pscustomobject]@{
                    Suffix = 'B1'; Application = $CurrentApplication
                    Commit = $CurrentCommit
                },
                [pscustomobject]@{
                    Suffix = 'B2'; Application = $CurrentApplication
                    Commit = $CurrentCommit
                },
                [pscustomobject]@{
                    Suffix = 'A2'; Application = $BaselineApplication
                    Commit = $BaselineCommit
                })) {
            $label = $prefix + $session.Suffix
            $stage = $workload + '|' + $label
            $readiness = Wait-ForReadiness $stage
            $readinessPath = Join-Path $rawRoot (
                "$workload-$label-readiness.json")
            Write-Json $readinessPath $readiness
            Require ([bool]$readiness.Passed) `
                "No quiet readiness window was admitted for $stage."
            $output = Join-Path $rawRoot ("$workload-$label.json")
            Invoke-Benchmark `
                -Application $session.Application `
                -Mode 'performance' `
                -Workload $workload `
                -Session $label `
                -TargetCommit $session.Commit `
                -Output $output
            $roundInput[$session.Suffix + 'Readiness'] =
                Get-RelativePath $AttemptRoot $readinessPath
            $roundInput[$session.Suffix] =
                Get-RelativePath $AttemptRoot $output
        }
        $rounds.Add([pscustomobject]$roundInput)
    }
    $workloadInputs.Add([pscustomobject]@{
            Id = $workload
            Rounds = @($rounds)
        })
}

$manifest = [ordered]@{
    SchemaVersion = 'openvisionlab-paired-baseline-input-v2'
    BaselineCommit = $BaselineCommit
    CurrentCommit = $CurrentCommit
    HarnessCommit = $HarnessCommit
    ComparerCommit = $ComparerCommit.ToLowerInvariant()
    Accuracy = [pscustomobject]$accuracy
    Workloads = @($workloadInputs)
}
$manifestPath = Join-Path $AttemptRoot 'paired-input.json'
$comparisonPath = Join-Path $summaryRoot 'paired-comparison.json'
Write-Json $manifestPath $manifest
$comparisonExitCode = Invoke-Application $ComparerApplication @(
    '--paired-compare',
    '--manifest', $manifestPath,
    '--output', $comparisonPath)
if ($comparisonExitCode -eq 2) {
    Write-Host "INVESTIGATE | official-paired-baseline | $comparisonPath"
    exit 2
}
Require ($comparisonExitCode -eq 0) `
    "Paired comparison is incomplete. ExitCode=$comparisonExitCode"
Write-Host "PASS | official-paired-baseline | $comparisonPath"
