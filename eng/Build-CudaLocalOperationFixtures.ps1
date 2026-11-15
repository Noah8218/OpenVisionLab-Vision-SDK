#requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string] $OutputDirectory,
    [Parameter(Mandatory)][string] $PtxHeaderPath,
    [Parameter(Mandatory)][string] $DiscoveryModulePath
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repository = Split-Path $PSScriptRoot -Parent
$output = [IO.Path]::GetFullPath($OutputDirectory)
if (-not [IO.Path]::IsPathRooted($OutputDirectory) -or -not $output.StartsWith('D:\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Choose a new absolute D: fixture output directory.' }
if (Test-Path -LiteralPath $output) { throw 'OutputDirectory already exists; preserve earlier evidence and choose a new directory.' }
$ptx = (Resolve-Path -LiteralPath $PtxHeaderPath).Path
$discovery = (Resolve-Path -LiteralPath $DiscoveryModulePath).Path
if (-not (Test-Path -LiteralPath $ptx -PathType Leaf) -or -not (Test-Path -LiteralPath $discovery -PathType Leaf)) { throw 'PTX header and discovery module must be existing files.' }

# Native .7 baseline; normalize line endings only, without changing source bytes.
$baseline = [ordered]@{
    'KernelExecution.cpp' = 'E475F72B4D9A0049D686CB5BD998291927C8E4BBA0EB3118D73BE2BD7F2321E1'
    'DeviceProbe.cpp' = '750305D0E19123A91FD33F63C49F337CEF9D4E8541739A2749E19BE2DBDCA544'
    'Kernels.cu' = '84928062026E8F8211BAECDAB7D8E8E8333D88C71DDCF9DEA293ADFDD8B372EA'
}
$sources = foreach ($name in $baseline.Keys) {
    $path = Join-Path "$repository\native\OpenVisionLab.Cuda" $name
    $sha = [Security.Cryptography.SHA256]::Create()
    try { $canonical = [BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes([IO.File]::ReadAllText($path).Replace("`r`n", "`n")))).Replace('-', '') }
    finally { $sha.Dispose() }
    if ($canonical -cne $baseline[$name]) { throw "Native source differs from the reviewed .7 fixture baseline: $name" }
    [ordered]@{ File = $path; Sha256 = (Get-FileHash -LiteralPath $path).Hash; CanonicalLfSha256 = $canonical; BaselineCanonicalLfSha256 = $baseline[$name]; MatchesBaseline = $true }
}
$fixture = "$repository\tests\OpenVisionLab.Inspection.Smoke\NativeFixtures\CudaLocalOperationFixture.cpp"
if (-not (Test-Path -LiteralPath $fixture -PathType Leaf)) { throw 'CudaLocalOperationFixture.cpp is missing.' }
$locator = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
$installation = & $locator -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath | Select-Object -First 1
if (-not $installation) { throw 'An installed MSVC x64 compiler is required; no tools are installed by this script.' }
$toolVersion = (Get-Content -LiteralPath "$installation\VC\Auxiliary\Build\Microsoft.VCToolsVersion.default.txt" -Raw).Trim()
$tools = "$installation\VC\Tools\MSVC\$toolVersion"
$sdk = (Get-ItemProperty -LiteralPath 'HKLM:\SOFTWARE\Microsoft\Windows Kits\Installed Roots').KitsRoot10
$sdkVersion = Get-ChildItem -LiteralPath "$sdk\Include" -Directory | Where-Object { Test-Path -LiteralPath "$($_.FullName)\um\windows.h" } |
    Sort-Object { [version]$_.Name } -Descending | Select-Object -First 1 -ExpandProperty Name
if (-not $sdkVersion) { throw 'An installed Windows SDK is required.' }
$compiler = "$tools\bin\Hostx64\x64\cl.exe"
New-Item -ItemType Directory -Path $output, "$output\inputs", "$output\temp", "$output\discovery" | Out-Null
Copy-Item -LiteralPath $ptx -Destination "$output\inputs\KernelPtx.h"
Copy-Item -LiteralPath $discovery -Destination "$output\discovery\OpenVisionLab.Cuda.Native.dll"
if ((Get-FileHash -LiteralPath $ptx).Hash -ne (Get-FileHash -LiteralPath "$output\inputs\KernelPtx.h").Hash -or
    (Get-FileHash -LiteralPath $discovery).Hash -ne (Get-FileHash -LiteralPath "$output\discovery\OpenVisionLab.Cuda.Native.dll").Hash) { throw 'An explicit input copy differs from its original.' }
$flags = @('/nologo', '/MT', '/W4', '/WX', '/O2', '/EHsc', '/std:c++14', "/I$tools\include", "/I$output\inputs")
foreach ($part in @('ucrt', 'shared', 'um')) { $flags += "/I$sdk\Include\$sdkVersion\$part" }
$includedSource = "$repository/native/OpenVisionLab.Cuda/KernelExecution.cpp" -replace '\\', '/'
$flags += '/DOVL_KERNEL_EXECUTION_SOURCE="' + $includedSource + '"'
$libraries = @('/link', "/LIBPATH:$tools\lib\x64", "/LIBPATH:$sdk\Lib\$sdkVersion\ucrt\x64", "/LIBPATH:$sdk\Lib\$sdkVersion\um\x64")
$previousPath = $env:PATH; $previousTemp = $env:TEMP; $previousTmp = $env:TMP
$commands = [Collections.Generic.List[object]]::new()
try {
    $env:PATH = "$tools\bin\Hostx64\x64;$previousPath"
    $env:TEMP = "$output\temp"; $env:TMP = $env:TEMP
    foreach ($variant in @(@('adaptive-budget', 1), @('tile-fault', 2), @('fault', 3), @('old', 4))) {
        $name = $variant[0]; $kind = $variant[1]; $directory = "$output\$name"
        New-Item -ItemType Directory -Path $directory | Out-Null
        $arguments = $flags + @('/LD', "/DOVL_LOCAL_FIXTURE_KIND=$kind", $fixture, "$repository\native\OpenVisionLab.Cuda\DeviceProbe.cpp",
            "/Fo$directory\", "/Fe$directory\OpenVisionLab.Cuda.Native.dll") + $libraries + @("/IMPLIB:$directory\fixture.lib")
        & $compiler @arguments *> "$directory\build.log"
        $commands.Add([ordered]@{ Variant = $name; Compiler = $compiler; Arguments = $arguments; ExitCode = $LASTEXITCODE; Log = "$directory\build.log" })
        if ($LASTEXITCODE -ne 0) { Get-Content -LiteralPath "$directory\build.log" -Tail 20 | Write-Host; throw "Native fixture build failed: $name" }
    }
    [ordered]@{
        Status = 'Complete'; Scope = 'Four test-only fixture builds and exact discovery/PTX input copies; execution is a separate gate';
        BaselineSourceCommit = '87e5a255d18c0144165063729ab6e64d8af94f80'; NativeSources = @($sources);
        IncludedKernelSource = $includedSource; FixtureSource = $fixture; FixtureSha256 = (Get-FileHash -LiteralPath $fixture).Hash;
        PtxInput = $ptx; PtxHeaderSha256 = (Get-FileHash -LiteralPath $ptx).Hash;
        DiscoveryInput = $discovery; DiscoverySha256 = (Get-FileHash -LiteralPath $discovery).Hash;
        Compiler = $compiler; WindowsSdk = $sdkVersion; Commands = $commands.ToArray();
        Dlls = @(Get-ChildItem -LiteralPath $output -Recurse -Filter '*.dll' -File | ForEach-Object { [ordered]@{ Path = $_.FullName; Sha256 = (Get-FileHash -LiteralPath $_.FullName).Hash } });
        Boundary = 'Existing PTX is identified by its input hash, not regenerated here. Fixture fault/cancellation behavior is test-only; no production source, system files or installed tools are modified.'
    } | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath "$output\build-manifest.json" -Encoding utf8
    Write-Output "Built four CUDA local fixtures; manifest: $output\build-manifest.json"
}
finally { $env:PATH = $previousPath; $env:TEMP = $previousTemp; $env:TMP = $previousTmp }
