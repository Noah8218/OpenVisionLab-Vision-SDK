#requires -Version 7.0

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)][string] $PackageDirectory,
    [Parameter(Mandatory = $true)][string] $ExpectedVersion,
    [Parameter(Mandatory = $true)][string] $ExpectedCommit,
    [Parameter(Mandatory = $true)][string] $OutputDirectory
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
Add-Type -AssemblyName System.IO.Compression.FileSystem

$repositoryRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$packageRoot = (Resolve-Path -LiteralPath $PackageDirectory).Path
$outputRoot = [System.IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $outputRoot) {
    throw "Negative-test output already exists: $outputRoot"
}
[System.IO.Directory]::CreateDirectory($outputRoot) | Out-Null

$packageFiles = @(Get-ChildItem -LiteralPath $packageRoot -Filter '*.nupkg' -File)
if ($packageFiles.Count -ne 6) {
    throw "Expected six source packages, found $($packageFiles.Count)."
}
$verifier = Join-Path $PSScriptRoot 'Verify-PackageProvenance.ps1'
$provenance = Get-Content -LiteralPath (
    Join-Path $repositoryRoot 'src/OpenVisionLab.Core/ThirdParty/provenance.json') -Raw | ConvertFrom-Json
$nativeBinaries = @($provenance.binaries | Where-Object { $_.identity.format -eq 'native' })
if ($nativeBinaries.Count -ne 1) {
    throw "Expected one native provenance entry, found $($nativeBinaries.Count)."
}
$nativeSource = Join-Path $repositoryRoot ([string] $nativeBinaries[0].sourcePath)
$results = [System.Collections.Generic.List[object]]::new()

function Copy-PackageSet {
    param([Parameter(Mandatory = $true)][string] $Name)

    $destination = Join-Path $outputRoot $Name
    [System.IO.Directory]::CreateDirectory($destination) | Out-Null
    Copy-Item -LiteralPath $packageFiles.FullName -Destination $destination
    return $destination
}

function Get-PackagePath {
    param(
        [Parameter(Mandatory = $true)][string] $Directory,
        [Parameter(Mandatory = $true)][string] $PackageId
    )

    return (Get-ChildItem -LiteralPath $Directory -Filter "$PackageId.$ExpectedVersion.nupkg" -File).FullName
}

function Add-ZipBytes {
    param(
        [Parameter(Mandatory = $true)][string] $ZipPath,
        [Parameter(Mandatory = $true)][string] $EntryPath,
        [Parameter(Mandatory = $true)][byte[]] $Bytes,
        [switch] $ReplaceExisting
    )

    $archive = [System.IO.Compression.ZipFile]::Open(
        $ZipPath,
        [System.IO.Compression.ZipArchiveMode]::Update)
    try {
        if ($ReplaceExisting) {
            $existingEntry = $archive.GetEntry($EntryPath)
            if ($null -eq $existingEntry) {
                throw "Cannot replace missing package entry '$EntryPath'."
            }
            $existingEntry.Delete()
        }
        $entry = $archive.CreateEntry($EntryPath, [System.IO.Compression.CompressionLevel]::Optimal)
        $stream = $entry.Open()
        try {
            $stream.Write($Bytes, 0, $Bytes.Length)
        }
        finally {
            $stream.Dispose()
        }
    }
    finally {
        $archive.Dispose()
    }
}

function Edit-CoreNuspec {
    param([string] $Directory, [scriptblock] $Edit)

    $path = Get-PackagePath $Directory 'OpenVisionLab.Core'
    $archive = [System.IO.Compression.ZipFile]::OpenRead($path)
    try {
        $entry = $archive.GetEntry('OpenVisionLab.Core.nuspec')
        $reader = [System.IO.StreamReader]::new($entry.Open())
        try { [xml] $nuspec = $reader.ReadToEnd() }
        finally { $reader.Dispose() }
    }
    finally { $archive.Dispose() }
    & $Edit $nuspec
    Add-ZipBytes -ZipPath $path -EntryPath 'OpenVisionLab.Core.nuspec' -Bytes ([System.Text.Encoding]::UTF8.GetBytes($nuspec.OuterXml)) -ReplaceExisting
}

function Invoke-NegativeProbe {
    param(
        [Parameter(Mandatory = $true)][string] $Name,
        [Parameter(Mandatory = $true)][scriptblock] $Mutate,
        [Parameter(Mandatory = $true)][string] $ExpectedDiagnostic
    )

    $probePackages = Copy-PackageSet $Name
    & $Mutate $probePackages
    $probeArguments = @($verifier, $probePackages, $ExpectedVersion, $ExpectedCommit) |
        ForEach-Object { "'" + $_.Replace("'", "''") + "'" }
    $probeCommand = 'try {{ & {0} -PackageDirectory {1} -ExpectedVersion {2} -ExpectedCommit {3} }} catch {{ [Console]::Error.WriteLine($_.Exception.Message); exit 1 }}' -f $probeArguments
    $output = @(
        & pwsh -NoProfile -Command $probeCommand 2>&1 |
            ForEach-Object { $_.ToString() })
    $exitCode = $LASTEXITCODE
    $logPath = Join-Path $outputRoot "$Name.log"
    [System.IO.File]::WriteAllLines(
        $logPath,
        $output,
        [System.Text.UTF8Encoding]::new($false))
    $joinedOutput = $output -join [Environment]::NewLine
    $normalizedOutput = [regex]::Replace($joinedOutput, '\s*\|\s*', ' ')
    $normalizedOutput = [regex]::Replace($normalizedOutput, '\s+', ' ')
    $normalizedDiagnostic = [regex]::Replace($ExpectedDiagnostic, '\s+', ' ')
    if ($exitCode -eq 0 -or -not $normalizedOutput.Contains($normalizedDiagnostic, [StringComparison]::Ordinal)) {
        throw "Negative probe '$Name' did not fail with '$ExpectedDiagnostic'. See $logPath."
    }
    $null = $results.Add([ordered] @{
        name = $Name
        result = 'fail-closed'
        diagnostic = $ExpectedDiagnostic
        log = $logPath
    })
}

Invoke-NegativeProbe `
    -Name 'case-insensitive-duplicate' `
    -ExpectedDiagnostic "contains duplicate archive path 'notice' ignoring case." `
    -Mutate {
        param($packages)
        Add-ZipBytes `
            -ZipPath (Get-PackagePath $packages 'OpenVisionLab.Core') `
            -EntryPath 'notice' `
            -Bytes ([System.Text.Encoding]::UTF8.GetBytes('duplicate'))
    }

Invoke-NegativeProbe `
    -Name 'unsafe-path' `
    -ExpectedDiagnostic "contains unsafe archive path 'third-party\escape.txt'." `
    -Mutate {
        param($packages)
        Add-ZipBytes `
            -ZipPath (Get-PackagePath $packages 'OpenVisionLab.Core') `
            -EntryPath 'third-party\escape.txt' `
            -Bytes ([System.Text.Encoding]::UTF8.GetBytes('unsafe'))
    }

Invoke-NegativeProbe `
    -Name 'third-party-license-drift' `
    -ExpectedDiagnostic 'OpenVisionLab.Core third-party/licenses/Intel-IPPICV-2020-Simplified-Software-License.rtf SHA-256' `
    -Mutate {
        param($packages)
        Add-ZipBytes `
            -ZipPath (Get-PackagePath $packages 'OpenVisionLab.Core') `
            -EntryPath 'third-party/licenses/Intel-IPPICV-2020-Simplified-Software-License.rtf' `
            -Bytes ([System.Text.Encoding]::UTF8.GetBytes('mutated')) `
            -ReplaceExisting
    }

Invoke-NegativeProbe `
    -Name 'renamed-non-core-binary' `
    -ExpectedDiagnostic "must not contain vendored Core binary bytes at 'lib/netstandard2.0/renamed-helper.dll'." `
    -Mutate {
        param($packages)
        $archive = [System.IO.Compression.ZipFile]::Open(
            (Get-PackagePath $packages 'OpenVisionLab.Inspection'),
            [System.IO.Compression.ZipArchiveMode]::Update)
        try {
            [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile(
                $archive,
                $nativeSource,
                'lib/netstandard2.0/renamed-helper.dll',
                [System.IO.Compression.CompressionLevel]::Optimal) | Out-Null
        }
        finally {
            $archive.Dispose()
        }
    }

foreach ($dependencyId in @('System.Memory', 'System.Runtime.CompilerServices.Unsafe')) {
    Invoke-NegativeProbe `
        -Name "$dependencyId-missing" `
        -ExpectedDiagnostic "OpenVisionLab.Core must contain one managed dependency on $dependencyId." `
        -Mutate {
            param($packages)
            Edit-CoreNuspec $packages {
                param($nuspec)
                $node = $nuspec.SelectSingleNode("//*[local-name()='dependency' and @id='$dependencyId']")
                $null = $node.ParentNode.RemoveChild($node)
            }
        }
    Invoke-NegativeProbe `
        -Name "$dependencyId-version" `
        -ExpectedDiagnostic "OpenVisionLab.Core managed dependency $dependencyId version '0.0.0' does not match Core.csproj declaration" `
        -Mutate {
            param($packages)
            Edit-CoreNuspec $packages {
                param($nuspec)
                $nuspec.SelectSingleNode("//*[local-name()='dependency' and @id='$dependencyId']").SetAttribute('version', '0.0.0')
            }
        }
}
Invoke-NegativeProbe `
    -Name 'managed-wrong-framework' `
    -ExpectedDiagnostic 'OpenVisionLab.Core must contain one .NETStandard2.0 dependency group.' `
    -Mutate {
        param($packages)
        Edit-CoreNuspec $packages {
            param($nuspec)
            $nuspec.SelectSingleNode("//*[local-name()='dependencies']/*[local-name()='group']").SetAttribute('targetFramework', '.NETFramework4.8')
        }
    }
Invoke-NegativeProbe `
    -Name 'managed-duplicate' `
    -ExpectedDiagnostic 'OpenVisionLab.Core must contain one managed dependency on System.Memory.' `
    -Mutate {
        param($packages)
        Edit-CoreNuspec $packages {
            param($nuspec)
            $node = $nuspec.SelectSingleNode("//*[local-name()='dependency' and @id='System.Memory']")
            $null = $node.ParentNode.AppendChild($node.CloneNode($true))
        }
    }

$summary = [ordered] @{
    schemaVersion = 1
    expectedVersion = $ExpectedVersion
    expectedCommit = $ExpectedCommit
    result = 'passed'
    probes = $results
}
$summaryPath = Join-Path $outputRoot 'summary.json'
[System.IO.File]::WriteAllText(
    $summaryPath,
    ($summary | ConvertTo-Json -Depth 5) + [Environment]::NewLine,
    [System.Text.UTF8Encoding]::new($false))
Write-Host "Package provenance negative probes passed: $($results.Count). Evidence: $summaryPath"
exit 0
