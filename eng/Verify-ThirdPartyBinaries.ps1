#requires -Version 7.0

[CmdletBinding()]
param(
    [string] $RepositoryRoot,

    [string] $ProvenancePath
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Get-RepositoryPath {
    param(
        [Parameter(Mandatory = $true)][string] $Root,
        [Parameter(Mandatory = $true)][string] $RelativePath
    )

    if ([System.IO.Path]::IsPathRooted($RelativePath)) {
        throw "Third-party path must be repository-relative: '$RelativePath'."
    }
    $candidate = [System.IO.Path]::GetFullPath((Join-Path $Root $RelativePath))
    $rootPrefix = $Root.TrimEnd(
        [System.IO.Path]::DirectorySeparatorChar,
        [System.IO.Path]::AltDirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
    if (-not $candidate.StartsWith($rootPrefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Third-party path escapes the repository: '$RelativePath'."
    }
    return $candidate
}

function Get-Sha256 {
    param([Parameter(Mandatory = $true)][string] $Path)

    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToUpperInvariant()
}

function Invoke-Git {
    param([Parameter(Mandatory = $true)][string[]] $Arguments)

    $output = @(& git -C $script:repositoryRoot @Arguments 2>&1)
    if ($LASTEXITCODE -ne 0) {
        throw "git $($Arguments -join ' ') failed:`n$($output -join [Environment]::NewLine)"
    }
    return ($output -join [Environment]::NewLine).Trim()
}

if ([string]::IsNullOrWhiteSpace($RepositoryRoot)) {
    $RepositoryRoot = Join-Path $PSScriptRoot '..'
}
$repositoryRoot = (Resolve-Path -LiteralPath $RepositoryRoot).Path
if ([string]::IsNullOrWhiteSpace($ProvenancePath)) {
    $ProvenancePath = Join-Path $repositoryRoot 'src/OpenVisionLab.Core/ThirdParty/provenance.json'
}
$provenanceFile = (Resolve-Path -LiteralPath $ProvenancePath).Path
$expectedProvenanceSha256 = 'C53975A07ECEF70441BF25BC381062923F97D3D9BEC7E533A71733B30FC6CE32'
$actualProvenanceSha256 = Get-Sha256 $provenanceFile
if (-not [string]::Equals(
        $actualProvenanceSha256,
        $expectedProvenanceSha256,
        [StringComparison]::Ordinal)) {
    throw "Third-party provenance manifest SHA-256 $actualProvenanceSha256 does not match reviewed authority $expectedProvenanceSha256."
}
$provenance = Get-Content -LiteralPath $provenanceFile -Raw | ConvertFrom-Json

if ($provenance.schemaVersion -ne 1) {
    throw "Third-party provenance schemaVersion must be 1."
}
if (-not [string]::Equals(
        [string] $provenance.licenseEvidence.status,
        'unresolved',
        [StringComparison]::Ordinal)) {
    throw "Third-party license evidence must remain unresolved until the documented prerequisites are approved."
}
if (-not [string]::Equals(
        [string] $provenance.licenseEvidence.redistributionClearance,
        'blocked',
        [StringComparison]::Ordinal)) {
    throw "Third-party redistribution clearance must remain blocked until separately approved."
}
if (@($provenance.licenseEvidence.conflicts).Count -lt 3 -or
    [string]::IsNullOrWhiteSpace([string] $provenance.licenseEvidence.unblockCondition)) {
    throw "Third-party license evidence must preserve the known conflicts and unblock condition."
}

$expectedBinaries = @{
    'src/OpenVisionLab.Core/DLL/OpenCvSharp.dll' = [ordered] @{
        packagePath = 'lib/netstandard2.0/OpenCvSharp.dll'
        size = 862208
        sha256 = 'A5C477750EB4321B608F4B9183949915D4A42FE0B5D80CFB8376F5A326FA5F24'
        gitBlob = 'ba0134e38e3c8bf0c139a3b34e6197f0fc57145b'
        format = 'managed'
        assemblyName = 'OpenCvSharp'
        officialType = 'NuGet'
        officialId = 'OpenCvSharp4'
        officialVersion = '4.4.0.20200915'
        officialContainerSha256 = 'D6F6C98D45C84D0FFA0C9154400BFAAA65FF3957E290349BE9C9B1190E807BF1'
        officialEntryPath = 'lib/netstandard2.0/OpenCvSharp.dll'
    }
    'src/OpenVisionLab.Core/DLL/OpenCvSharp.Blob.dll' = [ordered] @{
        packagePath = 'lib/netstandard2.0/OpenCvSharp.Blob.dll'
        size = 40960
        sha256 = 'E03FE75D2C9D88886384EDBC445C63DA051EE3450286C8D0982FCD9F4BC24D54'
        gitBlob = 'd2fc535d357ea483ee3a822a2b990760ef3f66c8'
        format = 'managed'
        assemblyName = 'OpenCvSharp.Blob'
        officialType = 'NuGet'
        officialId = 'OpenCvSharp4'
        officialVersion = '4.4.0.20200915'
        officialContainerSha256 = 'D6F6C98D45C84D0FFA0C9154400BFAAA65FF3957E290349BE9C9B1190E807BF1'
        officialEntryPath = 'lib/netstandard2.0/OpenCvSharp.Blob.dll'
    }
    'src/OpenVisionLab.Core/DLL/OpenCvSharpExtern.dll' = [ordered] @{
        packagePath = 'runtimes/win-x64/native/OpenCvSharpExtern.dll'
        size = 53231104
        sha256 = 'C9E02A255DD83C9B06CA56EC6F435F15B53A863435238FCC5D8B9082B035F249'
        gitBlob = 'a7939cec94591c353014385d797345462a04a6a6'
        format = 'native'
        officialType = 'GitHubRelease'
        officialTag = '4.3.0.20200708'
        officialTagCommit = '206eba074db5e85b09843ae1f9275ef192969e1c'
        officialContainerSha256 = '1639AF0E08245F7A50D3A299636EF36ACC527CA5CCFFB1F97CEC861C774D97EB'
        officialEntryPath = 'NativeLib/win/x64/OpenCvSharpExtern.dll'
    }
}

$binaries = @($provenance.binaries)
if ($binaries.Count -ne $expectedBinaries.Count) {
    throw "Third-party provenance must contain exactly $($expectedBinaries.Count) binaries; found $($binaries.Count)."
}
$seenSourcePaths = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
$seenPackagePaths = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)

foreach ($binary in $binaries) {
    $sourcePath = [string] $binary.sourcePath
    if (-not $seenSourcePaths.Add($sourcePath)) {
        throw "Duplicate third-party source path: '$sourcePath'."
    }
    if (-not $seenPackagePaths.Add([string] $binary.packagePath)) {
        throw "Duplicate third-party package path: '$($binary.packagePath)'."
    }
    if (-not $expectedBinaries.ContainsKey($sourcePath)) {
        throw "Unexpected third-party binary: '$sourcePath'."
    }

    $expected = $expectedBinaries[$sourcePath]
    $sourceFile = Get-RepositoryPath -Root $repositoryRoot -RelativePath $sourcePath
    if (-not (Test-Path -LiteralPath $sourceFile -PathType Leaf)) {
        throw "Missing third-party binary: '$sourcePath'."
    }
    $null = Invoke-Git @('ls-files', '--error-unmatch', '--', $sourcePath)
    $actualGitBlob = Invoke-Git @('hash-object', '--', $sourcePath)
    $actualSize = (Get-Item -LiteralPath $sourceFile).Length
    $actualSha256 = Get-Sha256 $sourceFile

    if ($actualSize -ne $expected.size -or [long] $binary.size -ne $expected.size) {
        throw "$sourcePath size drift: file=$actualSize manifest=$($binary.size) expected=$($expected.size)."
    }
    if (-not [string]::Equals($actualSha256, $expected.sha256, [StringComparison]::Ordinal) -or
        -not [string]::Equals([string] $binary.sha256, $expected.sha256, [StringComparison]::Ordinal)) {
        throw "$sourcePath SHA-256 drift: file=$actualSha256 manifest=$($binary.sha256) expected=$($expected.sha256)."
    }
    if (-not [string]::Equals($actualGitBlob, $expected.gitBlob, [StringComparison]::Ordinal) -or
        -not [string]::Equals([string] $binary.gitBlob, $expected.gitBlob, [StringComparison]::Ordinal)) {
        throw "$sourcePath Git blob drift: file=$actualGitBlob manifest=$($binary.gitBlob) expected=$($expected.gitBlob)."
    }
    if (-not [string]::Equals([string] $binary.packagePath, $expected.packagePath, [StringComparison]::Ordinal)) {
        throw "$sourcePath package path must be '$($expected.packagePath)'."
    }
    if (-not [string]::Equals([string] $binary.identity.format, $expected.format, [StringComparison]::Ordinal)) {
        throw "$sourcePath format must be '$($expected.format)'."
    }

    $stream = [System.IO.File]::OpenRead($sourceFile)
    $peReader = $null
    try {
        $peReader = [System.Reflection.PortableExecutable.PEReader]::new($stream)
        $machine = $peReader.PEHeaders.CoffHeader.Machine.ToString()
        if (-not [string]::Equals($machine, [string] $binary.identity.peMachine, [StringComparison]::Ordinal)) {
            throw "$sourcePath PE machine '$machine' does not match '$($binary.identity.peMachine)'."
        }

        if ($expected.format -eq 'managed') {
            if (-not $peReader.HasMetadata -or $null -eq $peReader.PEHeaders.CorHeader -or
                -not $peReader.PEHeaders.CorHeader.Flags.HasFlag(
                    [System.Reflection.PortableExecutable.CorFlags]::ILOnly) -or
                -not $peReader.PEHeaders.CorHeader.Flags.HasFlag(
                    [System.Reflection.PortableExecutable.CorFlags]::StrongNameSigned)) {
                throw "$sourcePath must be an IL-only, strong-name-signed managed assembly."
            }
            $assemblyName = [System.Reflection.AssemblyName]::GetAssemblyName($sourceFile)
            $publicKeyToken = ($assemblyName.GetPublicKeyToken() | ForEach-Object { $_.ToString('x2') }) -join ''
            $versionInfo = [System.Diagnostics.FileVersionInfo]::GetVersionInfo($sourceFile)
            $metadataReader = [System.Reflection.Metadata.PEReaderExtensions]::GetMetadataReader($peReader)
            $module = $metadataReader.GetModuleDefinition()
            $mvid = $metadataReader.GetGuid($module.Mvid).ToString()

            if (-not [string]::Equals($assemblyName.Name, $expected.assemblyName, [StringComparison]::Ordinal) -or
                -not [string]::Equals($assemblyName.Version.ToString(), [string] $binary.identity.assemblyVersion, [StringComparison]::Ordinal) -or
                -not [string]::Equals($publicKeyToken, [string] $binary.identity.publicKeyToken, [StringComparison]::OrdinalIgnoreCase) -or
                -not [string]::Equals($versionInfo.FileVersion, [string] $binary.identity.fileVersion, [StringComparison]::Ordinal) -or
                -not [string]::Equals($versionInfo.ProductVersion, [string] $binary.identity.productVersion, [StringComparison]::Ordinal) -or
                -not [string]::Equals($mvid, [string] $binary.identity.mvid, [StringComparison]::OrdinalIgnoreCase)) {
                throw "$sourcePath managed identity does not match the provenance manifest."
            }
        }
        else {
            $timestamp = [DateTimeOffset]::FromUnixTimeSeconds(
                $peReader.PEHeaders.CoffHeader.TimeDateStamp).UtcDateTime.ToString('yyyy-MM-ddTHH:mm:ssZ')
            $expectedTimestamp = ([DateTimeOffset] $binary.identity.coffTimestampUtc).UtcDateTime.ToString(
                'yyyy-MM-ddTHH:mm:ssZ')
            $linkerVersion = "$($peReader.PEHeaders.PEHeader.MajorLinkerVersion).$($peReader.PEHeaders.PEHeader.MinorLinkerVersion)"
            if ($peReader.HasMetadata -or
                -not [string]::Equals($timestamp, $expectedTimestamp, [StringComparison]::Ordinal) -or
                -not [string]::Equals($linkerVersion, [string] $binary.identity.linkerVersion, [StringComparison]::Ordinal)) {
                throw "$sourcePath native PE identity does not match the provenance manifest."
            }
        }
    }
    finally {
        if ($null -ne $peReader) {
            $peReader.Dispose()
        }
        $stream.Dispose()
    }

    $signature = Get-AuthenticodeSignature -LiteralPath $sourceFile
    if (-not [string]::Equals($signature.Status.ToString(), [string] $binary.identity.authenticode, [StringComparison]::Ordinal)) {
        throw "$sourcePath Authenticode status '$($signature.Status)' does not match '$($binary.identity.authenticode)'."
    }

    $official = $binary.officialArtifact
    if (-not [string]::Equals([string] $official.type, $expected.officialType, [StringComparison]::Ordinal) -or
        -not [string]::Equals([string] $official.containerSha256, $expected.officialContainerSha256, [StringComparison]::Ordinal) -or
        -not [string]::Equals([string] $official.entryPath, $expected.officialEntryPath, [StringComparison]::Ordinal) -or
        [long] $official.entrySize -ne $expected.size -or
        -not [string]::Equals([string] $official.entrySha256, $expected.sha256, [StringComparison]::Ordinal) -or
        -not [string]::Equals([string] $official.byteMatch, 'exact', [StringComparison]::Ordinal)) {
        throw "$sourcePath official artifact identity does not match the reviewed exact-byte evidence."
    }
    if ($expected.officialType -eq 'NuGet') {
        if (-not [string]::Equals([string] $official.id, $expected.officialId, [StringComparison]::Ordinal) -or
            -not [string]::Equals([string] $official.version, $expected.officialVersion, [StringComparison]::Ordinal)) {
            throw "$sourcePath official NuGet identity does not match the reviewed package."
        }
    }
    else {
        if (-not [string]::Equals([string] $official.tag, $expected.officialTag, [StringComparison]::Ordinal) -or
            -not [string]::Equals([string] $official.tagCommit, $expected.officialTagCommit, [StringComparison]::OrdinalIgnoreCase)) {
            throw "$sourcePath official release identity does not match the reviewed release."
        }
    }
}

foreach ($sourcePath in $expectedBinaries.Keys) {
    if (-not $seenSourcePaths.Contains($sourcePath)) {
        throw "Missing third-party provenance entry: '$sourcePath'."
    }
}

$expectedDocumentPaths = @(
    'src/OpenVisionLab.Core/ThirdParty/NOTICE.md',
    'src/OpenVisionLab.Core/ThirdParty/licenses/OpenCvSharp-BSD-3-Clause.txt',
    'src/OpenVisionLab.Core/ThirdParty/licenses/OpenCV-4.3-BSD-3-Clause.txt',
    'src/OpenVisionLab.Core/ThirdParty/licenses/OpenCV-Contrib-BSD-3-Clause.txt',
    'src/OpenVisionLab.Core/ThirdParty/evidence/OpenCvSharp.Blob-ReadMe.txt'
)
$documents = @($provenance.documents)
if ($documents.Count -ne $expectedDocumentPaths.Count) {
    throw "Third-party provenance must contain exactly $($expectedDocumentPaths.Count) documents; found $($documents.Count)."
}
$seenDocumentSources = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
$seenDocumentPackages = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
foreach ($document in $documents) {
    $sourcePath = [string] $document.sourcePath
    if (-not $seenDocumentSources.Add($sourcePath) -or -not $seenDocumentPackages.Add([string] $document.packagePath)) {
        throw "Duplicate third-party document source or package path: '$sourcePath'."
    }
    if ($expectedDocumentPaths -cnotcontains $sourcePath) {
        throw "Unexpected third-party document: '$sourcePath'."
    }
    if (-not ([string] $document.packagePath).StartsWith('third-party/', [StringComparison]::Ordinal)) {
        throw "$sourcePath package path must be under third-party/."
    }
    $sourceFile = Get-RepositoryPath -Root $repositoryRoot -RelativePath $sourcePath
    $actualSize = (Get-Item -LiteralPath $sourceFile).Length
    $actualSha256 = Get-Sha256 $sourceFile
    if ($actualSize -ne [long] $document.size -or
        -not [string]::Equals($actualSha256, [string] $document.sha256, [StringComparison]::Ordinal)) {
        throw "$sourcePath document drift: file size/hash $actualSize/$actualSha256 does not match the manifest."
    }
}
foreach ($sourcePath in $expectedDocumentPaths) {
    if (-not $seenDocumentSources.Contains($sourcePath)) {
        throw "Missing third-party document entry: '$sourcePath'."
    }
}

$noticePath = Get-RepositoryPath -Root $repositoryRoot -RelativePath 'src/OpenVisionLab.Core/ThirdParty/NOTICE.md'
$notice = Get-Content -LiteralPath $noticePath -Raw
$requiredNoticeValues = @(
    'Redistribution status: blocked',
    '4.4.0.20200915',
    '4.3.0.20200708',
    'BSD-3-Clause',
    'LGPL',
    'IPPICV',
    'ittnotify'
) + @($expectedBinaries.Values | ForEach-Object { $_.sha256 })
foreach ($value in $requiredNoticeValues) {
    if (-not $notice.Contains([string] $value, [StringComparison]::Ordinal)) {
        throw "Third-party notice is missing required evidence '$value'."
    }
}

Write-Host "Third-party binary provenance passed: $($binaries.Count) exact binaries, $($documents.Count) evidence documents; redistribution clearance remains blocked."
