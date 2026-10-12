#requires -Version 7.0

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string] $SmokeAssembly,
    [Parameter(Mandatory = $true)][string] $OutputPath,
    [string] $BaselinePath = (Join-Path $PSScriptRoot 'coverage-baseline.json'),
    [string] $SmokeReportPath = "$OutputPath.smoke.json",
    [ValidateSet('cpu', 'cuda-probe', 'cuda-device', 'native-fixture')][string[]] $RequiredScope = @('cpu'),
    [switch] $ValidateOnly
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$smokePath = (Resolve-Path -LiteralPath $SmokeAssembly).Path
$baselineFile = (Resolve-Path -LiteralPath $BaselinePath).Path
$coveragePath = [System.IO.Path]::GetFullPath($OutputPath)
$executionPath = [System.IO.Path]::GetFullPath($SmokeReportPath)
if (-not $ValidateOnly) {
    foreach ($path in @($coveragePath, $executionPath)) {
        if (Test-Path -LiteralPath $path) { throw "Coverage output already exists: $path" }
        [System.IO.Directory]::CreateDirectory((Split-Path -Parent $path)) | Out-Null
    }
    & dotnet coverage collect dotnet $smokePath --report $executionPath `
        --output-format cobertura --output $coveragePath --nologo --disable-console-output
    if ($LASTEXITCODE -ne 0) { throw "Coverage execution failed with exit code $LASTEXITCODE." }
}
foreach ($path in @($coveragePath, $executionPath)) {
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Missing coverage evidence: $path" }
}

$execution = Get-Content -LiteralPath $executionPath -Raw | ConvertFrom-Json -AsHashtable
$cases = @($execution['Cases'])
if ($execution['SchemaVersion'] -ne 1 -or $execution['Mode'] -ne 'Execution' -or $execution['Status'] -ne 'Passed' -or
    $execution['EnumerationComplete'] -ne $true -or $null -ne $execution['Filter'] -or $null -ne $execution['Failure'] -or
    $execution['Total'] -le 0 -or $execution['Passed'] -ne $execution['Total'] -or $cases.Count -ne $execution['Total'] -or
    @($cases | Where-Object { $_['Outcome'] -ne 'Passed' }).Count -gt 0) {
    throw 'Coverage requires a complete, unfiltered, successful smoke execution report with consistent case counts.'
}
if ($null -eq $execution['Assembly'] -or $execution['Assembly']['Sha256'] -ne (Get-FileHash -LiteralPath $smokePath -Algorithm SHA256).Hash) {
    throw 'Smoke report assembly hash does not match the requested binary.'
}
$scopeNames = @('cpu', 'cuda-probe', 'cuda-device', 'native-fixture')
$scopes = @($execution['Scopes'])
if ($scopes.Count -ne $scopeNames.Count -or @($scopes | Select-Object -ExpandProperty Scope -Unique).Count -ne $scopeNames.Count -or
    @($cases | Where-Object { $_['Scope'] -notin $scopeNames -or [string]::IsNullOrWhiteSpace($_['Name']) }).Count -gt 0 -or
    @($cases | Select-Object -ExpandProperty Name -Unique).Count -ne $cases.Count) {
    throw 'Smoke report has missing, duplicate or unknown scopes/cases.'
}
foreach ($name in $scopeNames) {
    $scope = @($scopes | Where-Object { $_['Scope'] -eq $name })
    $count = @($cases | Where-Object { $_['Scope'] -eq $name }).Count
    if ($scope.Count -ne 1 -or $scope[0]['Selected'] -ne $count -or $scope[0]['Passed'] -ne $count -or
        $scope[0]['Failed'] -ne 0 -or $scope[0]['Listed'] -ne 0 -or $scope[0]['Available'] -lt $count -or
        ($count -gt 0 -and ($scope[0]['Enabled'] -ne $true -or $scope[0]['Status'] -ne 'Passed')) -or
        ($count -eq 0 -and $scope[0]['Status'] -notin @('NotEnabled', 'NotSelected'))) {
        throw "Inconsistent smoke execution scope: $name"
    }
    Write-Host "Smoke scope ${name}: $count executed; $($scope[0]['Status'])"
    if ($name -in $RequiredScope -and $count -eq 0) { throw "Required smoke execution scope was not exercised: $name" }
}

[xml] $report = Get-Content -LiteralPath $coveragePath -Raw
$packages = @($report.coverage.packages.package)
$minimums = Get-Content -LiteralPath $baselineFile -Raw | ConvertFrom-Json
if ('OpenVisionLab.Compute' -notin $minimums.PSObject.Properties.Name) { throw 'Missing Compute coverage baseline.' }
$failures = [System.Collections.Generic.List[string]]::new()
foreach ($minimum in $minimums.PSObject.Properties) {
    $package = @($packages | Where-Object { $_.name -eq $minimum.Name })
    if ($package.Count -ne 1) {
        $failures.Add("Missing or duplicate coverage package: $($minimum.Name)")
        continue
    }
    [double] $actual = 0
    [double] $required = $minimum.Value
    if (-not [double]::TryParse([string] $package[0].'line-rate', [System.Globalization.NumberStyles]::Float,
            [System.Globalization.CultureInfo]::InvariantCulture, [ref] $actual) -or
        -not [double]::IsFinite($actual) -or $actual -lt 0 -or $actual -gt 1 -or
        -not [double]::IsFinite($required) -or $required -lt 0 -or $required -gt 1) {
        $failures.Add("Invalid coverage rate: $($minimum.Name)")
        continue
    }
    Write-Host ("Coverage {0}: {1:P2} (minimum {2:P2})" -f $minimum.Name, $actual, $required)
    if ($actual + 1e-12 -lt $required) {
        $failures.Add(("{0} line coverage {1:P2} is below {2:P2}." -f $minimum.Name, $actual, $required))
    }
}
if ($failures.Count -gt 0) { throw ($failures -join [Environment]::NewLine) }
Write-Host "Coverage gate passed: $coveragePath | $executionPath"
