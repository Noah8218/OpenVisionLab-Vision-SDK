[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $SmokeAssembly,

    [Parameter(Mandatory = $true)]
    [string] $OutputPath,

    [string] $BaselinePath = (Join-Path $PSScriptRoot 'coverage-baseline.json')
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$smokePath = (Resolve-Path -LiteralPath $SmokeAssembly).Path
$baselineFile = (Resolve-Path -LiteralPath $BaselinePath).Path
$coveragePath = [System.IO.Path]::GetFullPath($OutputPath)
$coverageDirectory = Split-Path -Parent $coveragePath
if ($coverageDirectory) {
    New-Item -ItemType Directory -Force -Path $coverageDirectory | Out-Null
}

& dotnet coverage collect dotnet $smokePath `
    --output-format cobertura `
    --output $coveragePath `
    --nologo `
    --disable-console-output
if ($LASTEXITCODE -ne 0) {
    throw "Coverage execution failed with exit code $LASTEXITCODE."
}

[xml] $report = Get-Content -LiteralPath $coveragePath -Raw
$packages = @($report.coverage.packages.package)
$minimums = Get-Content -LiteralPath $baselineFile -Raw | ConvertFrom-Json
$failures = [System.Collections.Generic.List[string]]::new()

foreach ($minimum in $minimums.PSObject.Properties) {
    $package = $packages | Where-Object { $_.name -eq $minimum.Name } | Select-Object -First 1
    if ($null -eq $package) {
        $failures.Add("Missing coverage package: $($minimum.Name)")
        continue
    }

    $actual = [double]::Parse(
        [string] $package.'line-rate',
        [System.Globalization.CultureInfo]::InvariantCulture)
    $required = [double] $minimum.Value
    Write-Host ("Coverage {0}: {1:P2} (minimum {2:P2})" -f $minimum.Name, $actual, $required)
    if ($actual + 1e-12 -lt $required) {
        $failures.Add(
            ("{0} line coverage {1:P2} is below {2:P2}." -f $minimum.Name, $actual, $required))
    }
}

if ($failures.Count -gt 0) {
    throw ($failures -join [Environment]::NewLine)
}

Write-Host "Coverage gate passed: $coveragePath"
