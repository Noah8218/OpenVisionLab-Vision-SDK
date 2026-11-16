#requires -Version 7.0
[CmdletBinding()]
param(
    [string] $PackageDirectory,
    [string[]] $DependencyCache = @(),
    [Parameter(Mandatory)][string] $ExpectedVersion,
    [Parameter(Mandatory)][string] $ExpectedCommit,
    [Parameter(Mandatory)][string] $OutputDirectory,
    [switch] $VerifyOnly
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$root = [IO.Path]::GetFullPath($OutputDirectory)
$feed = Join-Path $root 'feed'
$manifestPath = Join-Path $root 'manifest.json'
if ((Test-Path 'D:\') -and [IO.Path]::GetPathRoot($root) -ne 'D:\') { throw 'Generated kit/test files must be on D: when available.' }
if ($ExpectedCommit -notmatch '^[a-f0-9]{40}$' -or $ExpectedVersion -notmatch '^\d+\.\d+\.\d+(-[a-zA-Z0-9.-]+)?$') { throw 'Specify the exact version and 40-character source commit.' }

function Read-Package([IO.FileInfo] $File) {
    $zip = [IO.Compression.ZipFile]::OpenRead($File.FullName)
    try {
        $nuspec = @($zip.Entries | Where-Object { $_.FullName -like '*.nuspec' })
        if ($nuspec.Count -ne 1) { throw "Expected one nuspec: $($File.Name)" }
        $reader = [IO.StreamReader]::new($nuspec[0].Open())
        try { [xml] $xml = $reader.ReadToEnd() } finally { $reader.Dispose() }
        $metadata = $xml.SelectSingleNode('//*[local-name()="metadata"]')
        $id = $metadata.SelectSingleNode('*[local-name()="id"]').InnerText
        $version = $metadata.SelectSingleNode('*[local-name()="version"]').InnerText
        $repository = $metadata.SelectSingleNode('*[local-name()="repository"]')
        $commit = if ($repository) { $repository.GetAttribute('commit') } else { $null }
        $binaries = @()
        if ($id -like 'OpenVisionLab.*') {
            foreach ($entry in $zip.Entries | Where-Object { $_.FullName -match '^(lib/.*|runtimes/win-x64/native)/[^/]+\.dll$' }) {
                $stream = $entry.Open(); $hash = [Security.Cryptography.SHA256]::Create()
                try { $binaries += [ordered]@{ Entry = $entry.FullName; File = [IO.Path]::GetFileName($entry.FullName); Sha256 = [Convert]::ToHexString($hash.ComputeHash($stream)) } }
                finally { $stream.Dispose(); $hash.Dispose() }
            }
        }
        return [pscustomobject]@{ Id = $id; Version = $version; Commit = $commit; File = $File.Name; Length = $File.Length; Sha256 = (Get-FileHash -LiteralPath $File.FullName -Algorithm SHA256).Hash; Binaries = $binaries }
    }
    finally { $zip.Dispose() }
}

$sdkIds = @('OpenVisionLab.Compute','OpenVisionLab.Core','OpenVisionLab.Inspection','OpenVisionLab.Vision2D','OpenVisionLab.Vision2D.Blob','OpenVisionLab.Vision3D') | Sort-Object
if (-not $VerifyOnly) {
    if (Test-Path -LiteralPath $root) { throw 'Choose a new kit directory; immutable previous kits are retained.' }
    $sdkFiles = @(Get-ChildItem -LiteralPath (Resolve-Path -LiteralPath $PackageDirectory).Path -Filter '*.nupkg' -File)
    $sdk = @($sdkFiles | ForEach-Object { Read-Package $_ })
    if ($sdk.Count -ne 6 -or (($sdk.Id | Sort-Object) -join '|') -ne ($sdkIds -join '|')) { throw 'The provenance directory must contain exactly the six SDK packages, not a dependency feed.' }
    foreach ($package in $sdk) {
        if ($package.Version -cne $ExpectedVersion -or $package.Commit -cne $ExpectedCommit) { throw "SDK version/source mismatch: $($package.File)" }
    }
    New-Item -ItemType Directory -Path $feed -Force | Out-Null
    $copied = @{}
    foreach ($file in $sdkFiles) { Copy-Item -LiteralPath $file.FullName -Destination $feed; $copied[$file.Name] = (Get-FileHash -LiteralPath $file.FullName).Hash }
    foreach ($cachePath in $DependencyCache) {
        foreach ($file in Get-ChildItem -LiteralPath (Resolve-Path -LiteralPath $cachePath).Path -Recurse -Filter '*.nupkg' -File) {
            if ($file.Name -like 'openvisionlab.*' -or $file.Name -like 'dotnet-coverage.*') { continue }
            $hash = (Get-FileHash -LiteralPath $file.FullName).Hash
            if ($copied.ContainsKey($file.Name)) {
                if ($copied[$file.Name] -ne $hash) { throw "Conflicting dependency bytes: $($file.Name)" }
            }
            else { Copy-Item -LiteralPath $file.FullName -Destination $feed; $copied[$file.Name] = $hash }
        }
    }
    $files = @(Get-ChildItem -LiteralPath $feed -Filter '*.nupkg' -File | Sort-Object Name | ForEach-Object { Read-Package $_ })
    [ordered]@{
        SchemaVersion = 1; Status = 'Prepared'; PackageVersion = $ExpectedVersion; SourceCommit = $ExpectedCommit; Files = $files
        Prerequisites = @('.NET SDK selected by global.json (8.0.423), including net8 targeting/Windows x64 host packs', '.NET Framework 4.8 runtime and developer/reference assemblies for the x64 C# 7.3 consumer', 'Windows x64 and the existing native runtime installation prerequisites; no SDK/runtime installer is supplied')
        Boundary = 'Dependency feed candidate; completion requires fresh-cache restore/build/run. Local NuGet sources only do not prove network isolation or installation readiness. No publication or distribution clearance is granted.'
    } | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath $manifestPath -Encoding utf8
    $escapedFeed = [Security.SecurityElement]::Escape($feed)
    [IO.File]::WriteAllText((Join-Path $root 'NuGet.Config'), "<configuration><packageSources><clear/><add key=`"offline`" value=`"$escapedFeed`"/></packageSources></configuration>")
}

$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
if ($manifest.SchemaVersion -ne 1 -or $manifest.PackageVersion -cne $ExpectedVersion -or $manifest.SourceCommit -cne $ExpectedCommit) { throw 'Kit manifest version/source mismatch.' }
$actualFiles = @(Get-ChildItem -LiteralPath $feed -Filter '*.nupkg' -File)
if ($actualFiles.Count -ne @($manifest.Files).Count) { throw 'Kit package count differs from the frozen manifest.' }
$sdk = @($manifest.Files | Where-Object { $_.Id -like 'OpenVisionLab.*' })
if ($sdk.Count -ne 6 -or (($sdk.Id | Sort-Object) -join '|') -ne ($sdkIds -join '|')) { throw 'Kit must contain exactly six SDK identities.' }
foreach ($package in $manifest.Files) {
    if ([IO.Path]::GetFileName($package.File) -cne $package.File) { throw 'Manifest filename must remain inside the feed.' }
    $path = Join-Path $feed $package.File
    if (-not (Test-Path -LiteralPath $path -PathType Leaf) -or (Get-Item -LiteralPath $path).Length -ne $package.Length -or (Get-FileHash -LiteralPath $path).Hash -cne $package.Sha256) { throw "Kit bytes differ from the frozen manifest: $($package.File)" }
    if ($package.Id -like 'OpenVisionLab.*' -and ($package.Version -cne $ExpectedVersion -or $package.Commit -cne $ExpectedCommit)) { throw "SDK version/source mismatch: $($package.File)" }
}
Write-Output "Verified immutable kit files: $(@($manifest.Files).Count); six SDK packages; version=$ExpectedVersion; source=$ExpectedCommit. Fresh-cache consumer execution is a separate check."
