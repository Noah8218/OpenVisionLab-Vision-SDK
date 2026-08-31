#requires -Version 7.0

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

function Get-ArchiveEntrySha256 {
    param([Parameter(Mandatory = $true)][System.IO.Compression.ZipArchiveEntry] $Entry)

    $stream = $Entry.Open()
    $algorithm = [System.Security.Cryptography.SHA256]::Create()
    try {
        return ([BitConverter]::ToString($algorithm.ComputeHash($stream))).Replace('-', '')
    }
    finally {
        $algorithm.Dispose()
        $stream.Dispose()
    }
}

function Compare-ArchiveEntryToFile {
    param(
        [Parameter(Mandatory = $true)][System.IO.Compression.ZipArchive] $Archive,
        [Parameter(Mandatory = $true)][string] $EntryPath,
        [Parameter(Mandatory = $true)][string] $SourcePath,
        [Parameter(Mandatory = $true)][string] $PackageId,
        [Parameter(Mandatory = $true)] $Failures
    )

    $entries = @($Archive.Entries | Where-Object { $_.FullName -ceq $EntryPath })
    if ($entries.Count -ne 1) {
        return
    }
    $archiveHash = Get-ArchiveEntrySha256 $entries[0]
    $sourceHash = (Get-FileHash -LiteralPath $SourcePath -Algorithm SHA256).Hash
    if (-not [string]::Equals($archiveHash, $sourceHash, [StringComparison]::Ordinal)) {
        $Failures.Add("$PackageId $EntryPath SHA-256 $archiveHash does not match repository source $sourceHash.")
    }
}

$repositoryRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
$packageRoot = (Resolve-Path -LiteralPath $PackageDirectory).Path
$thirdPartyProvenanceFile = (Resolve-Path -LiteralPath (
    Join-Path $repositoryRoot 'src/OpenVisionLab.Core/ThirdParty/provenance.json')).Path
& (Join-Path $PSScriptRoot 'Verify-ThirdPartyBinaries.ps1') `
    -RepositoryRoot $repositoryRoot `
    -ProvenancePath $thirdPartyProvenanceFile
$thirdPartyProvenance = Get-Content -LiteralPath $thirdPartyProvenanceFile -Raw | ConvertFrom-Json
$rootPackageFiles = [ordered] @{
    'LICENSE' = Join-Path $repositoryRoot 'LICENSE'
    'NOTICE' = Join-Path $repositoryRoot 'NOTICE'
}
$coreThirdPartyFiles = [ordered] @{
    'third-party/provenance.json' = $thirdPartyProvenanceFile
}
foreach ($document in @($thirdPartyProvenance.documents)) {
    $coreThirdPartyFiles[[string] $document.packagePath] = Join-Path $repositoryRoot ([string] $document.sourcePath)
}
$coreBinaryFiles = [ordered] @{}
$coreBinaryPathsByHash = [ordered] @{}
$coreBinarySizes = [System.Collections.Generic.HashSet[long]]::new()
foreach ($binary in @($thirdPartyProvenance.binaries)) {
    $coreBinaryFiles[[string] $binary.packagePath] = Join-Path $repositoryRoot ([string] $binary.sourcePath)
    $coreBinaryPathsByHash[[string] $binary.sha256] = [string] $binary.packagePath
    $null = $coreBinarySizes.Add([long] $binary.size)
}
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
            $entryNames = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
            foreach ($entry in $archive.Entries) {
                $entryPath = $entry.FullName
                $segments = @($entryPath.Split('/'))
                $hasUnsafeSegment = @(
                    $segments |
                        Where-Object {
                            [string]::IsNullOrWhiteSpace($_) -or
                            $_ -eq '.' -or
                            $_ -eq '..' -or
                            -not [string]::Equals($_, $_.Trim(), [StringComparison]::Ordinal) -or
                            $_.EndsWith('.', [StringComparison]::Ordinal) -or
                            $_.Contains(':', [StringComparison]::Ordinal)
                        }).Count -gt 0
                if ([string]::IsNullOrWhiteSpace($entryPath) -or
                    $entryPath.StartsWith('/', [StringComparison]::Ordinal) -or
                    $entryPath.EndsWith('/', [StringComparison]::Ordinal) -or
                    $entryPath.Contains('\', [StringComparison]::Ordinal) -or
                    $hasUnsafeSegment) {
                    $failures.Add("$($package.Name) contains unsafe archive path '$entryPath'.")
                }
                if (-not $entryNames.Add($entry.FullName)) {
                    $failures.Add("$($package.Name) contains duplicate archive path '$($entry.FullName)' ignoring case.")
                }
            }

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
                $requiredEntries += @('buildTransitive/OpenVisionLab.Core.targets')
                $requiredEntries += @($coreBinaryFiles.Keys)
                $requiredEntries += @($coreThirdPartyFiles.Keys)
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

            foreach ($entryPath in $rootPackageFiles.Keys) {
                Compare-ArchiveEntryToFile `
                    -Archive $archive `
                    -EntryPath $entryPath `
                    -SourcePath $rootPackageFiles[$entryPath] `
                    -PackageId $packageId `
                    -Failures $failures
            }
            $packageReadmePath = Join-Path $repositoryRoot "src/$packageId/README.md"
            Compare-ArchiveEntryToFile `
                -Archive $archive `
                -EntryPath 'README.md' `
                -SourcePath $packageReadmePath `
                -PackageId $packageId `
                -Failures $failures

            $vendoredFileNames = @(
                $coreBinaryFiles.Keys |
                    ForEach-Object { [System.IO.Path]::GetFileName($_) })
            $vendoredHashEntries = [System.Collections.Generic.List[object]]::new()
            foreach ($archiveEntry in $archive.Entries) {
                if (-not $coreBinarySizes.Contains([long] $archiveEntry.Length)) {
                    continue
                }
                $entryHash = Get-ArchiveEntrySha256 $archiveEntry
                if ($coreBinaryPathsByHash.Contains($entryHash)) {
                    $vendoredHashEntries.Add([ordered] @{
                        path = $archiveEntry.FullName
                        sha256 = $entryHash
                    })
                }
            }
            if ($packageId -eq 'OpenVisionLab.Core') {
                $vendoredEntries = @(
                    $archive.Entries |
                        Where-Object {
                            $vendoredFileNames -contains [System.IO.Path]::GetFileName($_.FullName)
                        })
                if ($vendoredEntries.Count -ne $coreBinaryFiles.Count) {
                    $failures.Add(
                        "$packageId contains $($vendoredEntries.Count) vendored OpenCvSharp DLL entries; expected $($coreBinaryFiles.Count).")
                }
                foreach ($vendoredEntry in $vendoredEntries) {
                    if ($coreBinaryFiles.Keys -cnotcontains $vendoredEntry.FullName) {
                        $failures.Add("$packageId contains vendored DLL at unexpected path '$($vendoredEntry.FullName)'.")
                    }
                }
                if ($vendoredHashEntries.Count -ne $coreBinaryFiles.Count) {
                    $failures.Add(
                        "$packageId contains $($vendoredHashEntries.Count) exact vendored binary byte copies; expected $($coreBinaryFiles.Count).")
                }
                foreach ($vendoredHashEntry in $vendoredHashEntries) {
                    $expectedPath = $coreBinaryPathsByHash[[string] $vendoredHashEntry.sha256]
                    if (-not [string]::Equals(
                            [string] $vendoredHashEntry.path,
                            $expectedPath,
                            [StringComparison]::Ordinal)) {
                        $failures.Add(
                            "$packageId contains vendored binary bytes at unexpected path '$($vendoredHashEntry.path)'.")
                    }
                }

                $thirdPartyEntries = @(
                    $archive.Entries |
                        Where-Object {
                            -not $_.FullName.EndsWith('/', [StringComparison]::Ordinal) -and
                            $_.FullName.StartsWith('third-party/', [StringComparison]::OrdinalIgnoreCase)
                        })
                if ($thirdPartyEntries.Count -ne $coreThirdPartyFiles.Count) {
                    $failures.Add(
                        "$packageId contains $($thirdPartyEntries.Count) third-party evidence entries; expected $($coreThirdPartyFiles.Count).")
                }
                foreach ($thirdPartyEntry in $thirdPartyEntries) {
                    if ($coreThirdPartyFiles.Keys -cnotcontains $thirdPartyEntry.FullName) {
                        $failures.Add("$packageId contains unexpected third-party entry '$($thirdPartyEntry.FullName)'.")
                    }
                }

                foreach ($entryPath in $coreBinaryFiles.Keys) {
                    Compare-ArchiveEntryToFile `
                        -Archive $archive `
                        -EntryPath $entryPath `
                        -SourcePath $coreBinaryFiles[$entryPath] `
                        -PackageId $packageId `
                        -Failures $failures
                }
                foreach ($entryPath in $coreThirdPartyFiles.Keys) {
                    Compare-ArchiveEntryToFile `
                        -Archive $archive `
                        -EntryPath $entryPath `
                        -SourcePath $coreThirdPartyFiles[$entryPath] `
                        -PackageId $packageId `
                        -Failures $failures
                }
            }
            else {
                foreach ($vendoredHashEntry in $vendoredHashEntries) {
                    $failures.Add(
                        "$packageId must not contain vendored Core binary bytes at '$($vendoredHashEntry.path)'.")
                }
                $forbiddenEntries = @(
                    $archive.Entries |
                        Where-Object {
                            $_.FullName.StartsWith('third-party/', [StringComparison]::OrdinalIgnoreCase) -or
                            $vendoredFileNames -contains [System.IO.Path]::GetFileName($_.FullName)
                        })
                foreach ($forbiddenEntry in $forbiddenEntries) {
                    $failures.Add("$packageId must not contain third-party Core entry '$($forbiddenEntry.FullName)'.")
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
