[CmdletBinding()]
param([Parameter(Mandatory = $true)][string] $OutputDirectory, [string] $NvrtcLibraryPath)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if (-not [Environment]::Is64BitProcess) { throw 'Use a 64-bit PowerShell process.' }
if (-not [IO.Path]::IsPathRooted($OutputDirectory)) { throw 'OutputDirectory must be an absolute artifact path.' }
$output = [IO.Path]::GetFullPath($OutputDirectory)
$locator = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
$installation = @(& $locator -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath)[0]
if (-not $installation) { throw 'An installed MSVC x64 compiler is required. This script does not install tools.' }
$toolVersion = (Get-Content -LiteralPath (Join-Path $installation 'VC\Auxiliary\Build\Microsoft.VCToolsVersion.default.txt') -Raw).Trim()
$tools = Join-Path $installation "VC\Tools\MSVC\$toolVersion"
$compilerDirectory = Join-Path $tools 'bin\Hostx64\x64'
$sdkRoot = (Get-ItemProperty -LiteralPath 'HKLM:\SOFTWARE\Microsoft\Windows Kits\Installed Roots').KitsRoot10
$sdkVersion = Get-ChildItem -LiteralPath (Join-Path $sdkRoot 'Include') -Directory |
    Where-Object { Test-Path -LiteralPath (Join-Path $_.FullName 'um\windows.h') } |
    Sort-Object { [version]$_.Name } -Descending | Select-Object -First 1 -ExpandProperty Name
if (-not $sdkVersion) { throw 'An installed Windows SDK is required.' }
New-Item -ItemType Directory -Path $output -Force | Out-Null
$source = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\native\OpenVisionLab.Cuda\DeviceProbe.cpp'))
$arguments = @('/nologo', '/LD', '/MT', '/W4', '/WX', '/O2', '/std:c++14', $source, "/Fo$output\DeviceProbe.obj", "/Fe$output\OpenVisionLab.Cuda.Native.dll", "/I$tools\include")
foreach ($part in @('ucrt', 'shared', 'um')) { $arguments += "/I$sdkRoot\Include\$sdkVersion\$part" }
$arguments += @("/Fd$output\DeviceProbe.pdb", '/link', "/IMPLIB:$output\OpenVisionLab.Cuda.Native.lib", "/PDB:$output\OpenVisionLab.Cuda.Native.pdb", "/LIBPATH:$tools\lib\x64", "/LIBPATH:$sdkRoot\Lib\$sdkVersion\ucrt\x64", "/LIBPATH:$sdkRoot\Lib\$sdkVersion\um\x64")
$previousPath = $env:PATH
try
{
    $env:PATH = "$compilerDirectory;$previousPath"
    if ($NvrtcLibraryPath)
    {
        if (-not [IO.Path]::IsPathRooted($NvrtcLibraryPath) -or -not (Test-Path -LiteralPath $NvrtcLibraryPath)) { throw 'NvrtcLibraryPath must name an existing absolute build-time compiler DLL.' }
        $nativeRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\native\OpenVisionLab.Cuda'))
        $compileArguments = @('/nologo', '/MT', '/W4', '/WX', '/O2', '/EHsc', '/std:c++14', "$nativeRoot\CompileKernels.cpp", "/Fo$output\CompileKernels.obj", "/Fe$output\CompileKernels.exe", "/I$tools\include")
        foreach ($part in @('ucrt', 'shared', 'um')) { $compileArguments += "/I$sdkRoot\Include\$sdkVersion\$part" }
        $compileArguments += @('/link', "/LIBPATH:$tools\lib\x64", "/LIBPATH:$sdkRoot\Lib\$sdkVersion\ucrt\x64", "/LIBPATH:$sdkRoot\Lib\$sdkVersion\um\x64")
        & (Join-Path $compilerDirectory 'cl.exe') @compileArguments
        if ($LASTEXITCODE -ne 0) { throw 'Build-time CUDA compiler failed to build.' }
        $env:PATH = "$(Split-Path -Parent $NvrtcLibraryPath);$env:PATH"
        & "$output\CompileKernels.exe" "$nativeRoot\Kernels.cu" $NvrtcLibraryPath "$output\KernelPtx.h"
        if ($LASTEXITCODE -ne 0) { throw "Offline CUDA compilation failed: $LASTEXITCODE" }
        $executionArguments = @('/nologo', '/c', '/MT', '/W4', '/WX', '/O2', '/std:c++14', "$nativeRoot\KernelExecution.cpp", "/Fo$output\KernelExecution.obj", "/I$output", "/I$tools\include")
        foreach ($part in @('ucrt', 'shared', 'um')) { $executionArguments += "/I$sdkRoot\Include\$sdkVersion\$part" }
        & (Join-Path $compilerDirectory 'cl.exe') @executionArguments
        if ($LASTEXITCODE -ne 0) { throw 'CUDA execution bridge failed to compile.' }
        $arguments = @("$output\KernelExecution.obj") + $arguments
    }
    & (Join-Path $compilerDirectory 'cl.exe') @arguments
    if ($LASTEXITCODE -ne 0) { throw "Native probe build failed with exit code $LASTEXITCODE." }
}
finally { $env:PATH = $previousPath }
Write-Host "CUDA device probe built: $output\OpenVisionLab.Cuda.Native.dll"
