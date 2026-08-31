[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string] $PackageDirectory,

    [Parameter(Mandatory = $true)]
    [string] $ExpectedVersion,

    [Parameter(Mandatory = $true)]
    [string] $ExpectedCommit,

    [string] $ExpectedRepositoryUrl,

    [string] $ManifestPath,

    [switch] $RequireCleanWorktree
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Invoke-Git {
    param([Parameter(Mandatory = $true)][string[]] $Arguments)

    $output = @(& git -C $script:repositoryRoot @Arguments 2>&1)
    if ($LASTEXITCODE -ne 0) {
        throw "git $($Arguments -join ' ') failed:`n$($output -join [Environment]::NewLine)"
    }
    return ($output -join [Environment]::NewLine).Trim()
}

function Read-Nuspec {
    param([Parameter(Mandatory = $true)][System.IO.Compression.ZipArchiveEntry] $Entry)

    $reader = [System.IO.StreamReader]::new($Entry.Open())
    try {
        return [xml] $reader.ReadToEnd()
    }
    finally {
        $reader.Dispose()
    }
}

function Copy-ArchiveEntry {
    param(
        [Parameter(Mandatory = $true)][System.IO.Compression.ZipArchiveEntry] $Entry,
        [Parameter(Mandatory = $true)][string] $Destination
    )

    $source = $Entry.Open()
    $target = [System.IO.File]::Create($Destination)
    try {
        $source.CopyTo($target)
    }
    finally {
        $target.Dispose()
        $source.Dispose()
    }
}

$repositoryRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$packageRoot = (Resolve-Path -LiteralPath $PackageDirectory).Path
if (-not [regex]::IsMatch($ExpectedCommit, '\A[0-9a-fA-F]{40}\z')) {
    throw "ExpectedCommit must be a full 40-character Git SHA: '$ExpectedCommit'."
}
$expectedCommitValue = $ExpectedCommit.ToLowerInvariant()
$headCommit = (Invoke-Git @('rev-parse', 'HEAD')).ToLowerInvariant()
if (-not [string]::Equals($headCommit, $expectedCommitValue, [StringComparison]::Ordinal)) {
    throw "Expected commit $expectedCommitValue does not match repository HEAD $headCommit."
}
if ($RequireCleanWorktree) {
    $status = Invoke-Git @('status', '--porcelain=v1', '--untracked-files=all')
    if (-not [string]::IsNullOrWhiteSpace($status)) {
        throw "Package provenance requires a clean worktree:`n$status"
    }
}

if ([string]::IsNullOrWhiteSpace($ExpectedRepositoryUrl)) {
    [xml] $buildProperties = Get-Content -LiteralPath (Join-Path $repositoryRoot 'Directory.Build.props') -Raw
    $repositoryUrls = @(
        $buildProperties.Project.PropertyGroup |
            ForEach-Object { [string] $_.RepositoryUrl } |
            Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
    )
    if ($repositoryUrls.Count -ne 1) {
        throw "Expected exactly one RepositoryUrl in Directory.Build.props, found $($repositoryUrls.Count)."
    }
    $ExpectedRepositoryUrl = $repositoryUrls[0]
}

$expectedPackageIds = @(
    'OpenVisionLab.Core',
    'OpenVisionLab.Inspection',
    'OpenVisionLab.Vision2D',
    'OpenVisionLab.Vision2D.Blob',
    'OpenVisionLab.Vision3D'
)
$expectedDependencies = @{
    'OpenVisionLab.Core' = @()
    'OpenVisionLab.Inspection' = @('OpenVisionLab.Vision2D', 'OpenVisionLab.Vision3D')
    'OpenVisionLab.Vision2D' = @('OpenVisionLab.Core')
    'OpenVisionLab.Vision2D.Blob' = @('OpenVisionLab.Core', 'OpenVisionLab.Vision2D')
    'OpenVisionLab.Vision3D' = @()
}

Add-Type -AssemblyName System.IO.Compression.FileSystem
$packages = @(Get-ChildItem -LiteralPath $packageRoot -Filter '*.nupkg' -File | Sort-Object Name)
if ($packages.Count -ne $expectedPackageIds.Count) {
    throw "Expected $($expectedPackageIds.Count) packages, found $($packages.Count) in $packageRoot."
}

$failures = [System.Collections.Generic.List[string]]::new()
$seenIds = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
$packageEvidence = [System.Collections.Generic.List[object]]::new()
$temporaryRoot = Join-Path ([System.IO.Path]::GetTempPath()) (
    'OpenVisionLab-PackageProvenance-' + [Guid]::NewGuid().ToString('N'))
[System.IO.Directory]::CreateDirectory($temporaryRoot) | Out-Null

try {
    foreach ($package in $packages) {
        $archive = [System.IO.Compression.ZipFile]::OpenRead($package.FullName)
        try {
            $nuspecEntries = @($archive.Entries | Where-Object { $_.FullName -like '*.nuspec' })
            if ($nuspecEntries.Count -ne 1) {
                $failures.Add("$($package.Name) contains $($nuspecEntries.Count) nuspec files; expected one.")
                continue
            }

            $nuspec = Read-Nuspec $nuspecEntries[0]
            $metadata = $nuspec.package.metadata
            $packageId = [string] $metadata.id
            $isExpectedPackageId = @(
                $expectedPackageIds |
                    Where-Object { [string]::Equals($_, $packageId, [StringComparison]::Ordinal) }
            ).Count -eq 1
            if (-not $isExpectedPackageId) {
                $failures.Add("Unexpected package ID '$packageId' in $($package.Name).")
                continue
            }
            if (-not $seenIds.Add($packageId)) {
                $failures.Add("Duplicate package ID '$packageId'.")
                continue
            }

            $expectedFileName = "$packageId.$ExpectedVersion.nupkg"
            if (-not [string]::Equals($package.Name, $expectedFileName, [StringComparison]::OrdinalIgnoreCase)) {
                $failures.Add("$($package.Name) must be named $expectedFileName.")
            }
            if (-not [string]::Equals([string] $metadata.version, $ExpectedVersion, [StringComparison]::Ordinal)) {
                $failures.Add("$packageId version '$($metadata.version)' does not match '$ExpectedVersion'.")
            }
            if (-not [string]::Equals([string] $metadata.repository.type, 'git', [StringComparison]::OrdinalIgnoreCase)) {
                $failures.Add("$packageId repository type '$($metadata.repository.type)' is not git.")
            }
            if (-not [string]::Equals([string] $metadata.repository.url, $ExpectedRepositoryUrl, [StringComparison]::OrdinalIgnoreCase)) {
                $failures.Add("$packageId repository URL '$($metadata.repository.url)' does not match '$ExpectedRepositoryUrl'.")
            }
            if (-not [string]::Equals([string] $metadata.repository.commit, $expectedCommitValue, [StringComparison]::OrdinalIgnoreCase)) {
                $failures.Add("$packageId repository commit '$($metadata.repository.commit)' does not match '$expectedCommitValue'.")
            }
            if (-not [string]::Equals(
                    $nuspecEntries[0].FullName,
                    "$packageId.nuspec",
                    [StringComparison]::Ordinal)) {
                $failures.Add("$packageId nuspec must be named $packageId.nuspec.")
            }

            $requiredEntries = @(
                'README.md',
                'LICENSE',
                'NOTICE',
                "lib/netstandard2.0/$packageId.dll",
                "lib/netstandard2.0/$packageId.xml")
            if ($packageId -eq 'OpenVisionLab.Core') {
                $requiredEntries += @(
                    'lib/netstandard2.0/OpenCvSharp.dll',
                    'lib/netstandard2.0/OpenCvSharp.Blob.dll',
                    'runtimes/win-x64/native/OpenCvSharpExtern.dll',
                    'buildTransitive/OpenVisionLab.Core.targets')
            }
            if ($packageId -eq 'OpenVisionLab.Vision3D') {
                $requiredEntries += 'docs/three-d-inspection.md'
            }
            foreach ($requiredEntry in $requiredEntries) {
                $entryCount = @(
                    $archive.Entries | Where-Object { $_.FullName -ceq $requiredEntry }
                ).Count
                if ($entryCount -ne 1) {
                    $failures.Add(
                        "$packageId contains $entryCount copies of $requiredEntry; expected one.")
                }
            }

            $actualDependencies = @(
                $nuspec.SelectNodes("//*[local-name()='dependency']") |
                    Where-Object { ([string] $_.id).StartsWith('OpenVisionLab.', [StringComparison]::Ordinal) }
            )
            $requiredDependencies = @($expectedDependencies[$packageId])
            if ($actualDependencies.Count -ne $requiredDependencies.Count) {
                $failures.Add(
                    "$packageId has $($actualDependencies.Count) internal dependencies; expected $($requiredDependencies.Count).")
            }
            foreach ($dependencyId in $requiredDependencies) {
                $matches = @($actualDependencies | Where-Object { [string] $_.id -ceq $dependencyId })
                if ($matches.Count -ne 1) {
                    $failures.Add("$packageId must contain one dependency on $dependencyId.")
                    continue
                }
                if (-not [string]::Equals([string] $matches[0].version, $ExpectedVersion, [StringComparison]::Ordinal)) {
                    $failures.Add(
                        "$packageId dependency $dependencyId version '$($matches[0].version)' does not match '$ExpectedVersion'.")
                }
            }
            foreach ($dependency in $actualDependencies) {
                if ($requiredDependencies -notcontains [string] $dependency.id) {
                    $failures.Add("$packageId contains unexpected internal dependency '$($dependency.id)'.")
                }
            }

            $mainAssemblyEntries = @(
                $archive.Entries |
                    Where-Object { $_.FullName -ceq "lib/netstandard2.0/$packageId.dll" }
            )
            $productVersion = '<missing>'
            if ($mainAssemblyEntries.Count -eq 1) {
                $temporaryAssembly = Join-Path $temporaryRoot ($packageId + '.dll')
                Copy-ArchiveEntry $mainAssemblyEntries[0] $temporaryAssembly
                $productVersion = [System.Diagnostics.FileVersionInfo]::GetVersionInfo(
                    $temporaryAssembly).ProductVersion
                $hasExpectedProductVersion = -not [string]::IsNullOrWhiteSpace($productVersion) `
                    -and $productVersion.EndsWith(
                        "+$expectedCommitValue",
                        [StringComparison]::OrdinalIgnoreCase)
                if (-not $hasExpectedProductVersion) {
                    $failures.Add(
                        "$packageId assembly ProductVersion '$productVersion' does not end with '+$expectedCommitValue'.")
                }
            }

            $hash = (Get-FileHash -LiteralPath $package.FullName -Algorithm SHA256).Hash
            $packageEvidence.Add([ordered] @{
                id = $packageId
                file = $package.Name
                sha256 = $hash
                repositoryCommit = [string] $metadata.repository.commit
                assemblyProductVersion = $productVersion
            })
        }
        finally {
            $archive.Dispose()
        }
    }
}
finally {
    if (Test-Path -LiteralPath $temporaryRoot) {
        [System.IO.Directory]::Delete($temporaryRoot, $true)
    }
}

foreach ($packageId in $expectedPackageIds) {
    if (-not $seenIds.Contains($packageId)) {
        $failures.Add("Missing package ID '$packageId'.")
    }
}
if ($failures.Count -gt 0) {
    throw ($failures -join [Environment]::NewLine)
}

$manifest = [ordered] @{
    schemaVersion = 1
    repositoryUrl = $ExpectedRepositoryUrl
    commit = $expectedCommitValue
    version = $ExpectedVersion
    packages = @($packageEvidence | Sort-Object { $_.id })
}
if (-not [string]::IsNullOrWhiteSpace($ManifestPath)) {
    $manifestFile = [System.IO.Path]::GetFullPath($ManifestPath)
    $manifestDirectory = Split-Path -Parent $manifestFile
    if ($manifestDirectory) {
        [System.IO.Directory]::CreateDirectory($manifestDirectory) | Out-Null
    }
    [System.IO.File]::WriteAllText(
        $manifestFile,
        ($manifest | ConvertTo-Json -Depth 5) + [Environment]::NewLine,
        [System.Text.UTF8Encoding]::new($false))
    Write-Host "Package provenance manifest: $manifestFile"
}

foreach ($package in $manifest.packages) {
    Write-Host "$($package.id): $($package.sha256)"
}
Write-Host "Package provenance passed: $($packages.Count) packages, version $ExpectedVersion, commit $expectedCommitValue."
