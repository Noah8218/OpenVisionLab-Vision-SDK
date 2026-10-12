#requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string] $PackageDirectory,
    [Parameter(Mandatory)][string] $ExpectedVersion,
    [Parameter(Mandatory)][string] $OutputDirectory,
    [string] $DependencySource,
    [string] $CudaModule,
    [string] $CudaDiscoveryModule,
    [string] $CudaOldModule,
    [switch] $CudaTilesOnly
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if ($CudaTilesOnly) {
    if (-not $CudaModule -or -not [IO.Path]::IsPathRooted($CudaModule) -or -not (Test-Path -LiteralPath $CudaModule -PathType Leaf)) { throw 'Tile mode requires an absolute execution DLL path.' }
}
elseif ($CudaModule) {
    foreach ($path in @($CudaModule, $CudaDiscoveryModule, $CudaOldModule)) {
        if ([string]::IsNullOrWhiteSpace($path) -or -not [IO.Path]::IsPathRooted($path) -or -not (Test-Path -LiteralPath $path -PathType Leaf)) { throw 'CUDA mode requires absolute execution, discovery-only and old execution DLL paths.' }
    }
}
elseif ($CudaDiscoveryModule -or $CudaOldModule) { throw 'Specify CudaModule when selecting CUDA fixtures.' }
$root = [IO.Path]::GetFullPath($OutputDirectory)
$feed = (Resolve-Path -LiteralPath $PackageDirectory).Path
if ((Test-Path 'D:\') -and ([IO.Path]::GetPathRoot($root) -ne 'D:\')) { throw 'Generated test outputs must be on D:.' }
if ((Test-Path -LiteralPath $root) -and (Get-ChildItem -LiteralPath $root -Force | Select-Object -First 1)) { throw 'Choose a new, empty output directory; previous evidence is retained.' }
$consumer = Join-Path $root 'consumer'
$reports = Join-Path $root 'reports'
$temp = Join-Path $root 'temp'
$cache = Join-Path $root 'nuget'
$build = Join-Path $root 'build'
New-Item -ItemType Directory -Force -Path $consumer, $reports, $temp, $cache | Out-Null
$commands = [Collections.Generic.List[object]]::new()
$dotnet = (Get-Command dotnet).Source
$source = Join-Path (Split-Path $PSScriptRoot -Parent) 'tests/OpenVisionLab.PackageConsumer.NetFrameworkSmoke'
foreach ($name in @('Program.cs', 'OpenVisionLab.PackageConsumer.NetFrameworkSmoke.csproj')) { Copy-Item -LiteralPath (Join-Path $source $name) -Destination $consumer }
# Keep the isolated consumer on the SDK selected by the repository and CI.
Copy-Item -LiteralPath (Join-Path (Split-Path $PSScriptRoot -Parent) 'global.json') -Destination $consumer
$project = Join-Path $consumer 'OpenVisionLab.PackageConsumer.NetFrameworkSmoke.csproj'
if (Select-String -LiteralPath $project -Pattern 'ProjectReference|HintPath|Compile Include') { throw 'Consumer must reference packages only.' }
$escapedFeed = [Security.SecurityElement]::Escape($feed)
$config = Join-Path $consumer 'NuGet.Config'
$dependencyEntry = ''
$sourceMapping = ''
if (-not [string]::IsNullOrWhiteSpace($DependencySource)) {
    $escapedDependencySource = [Security.SecurityElement]::Escape($DependencySource)
    $dependencyEntry = "<add key=`"dependencies`" value=`"$escapedDependencySource`"/>"
    $sourceMapping = '<packageSourceMapping><packageSource key="isolated-sdk"><package pattern="OpenVisionLab.*"/></packageSource><packageSource key="dependencies"><package pattern="*"/></packageSource></packageSourceMapping>'
}
[IO.File]::WriteAllText($config, "<configuration><packageSources><clear/><add key=`"isolated-sdk`" value=`"$escapedFeed`"/>$dependencyEntry</packageSources>$sourceMapping</configuration>")

function Invoke-OwnedProcess([string] $Name, [string] $Executable, [string[]] $Arguments, [string] $Directory) {
    $info = [Diagnostics.ProcessStartInfo]::new($Executable)
    $info.WorkingDirectory = $Directory
    $info.UseShellExecute = $false
    $info.CreateNoWindow = $true
    $info.RedirectStandardOutput = $true
    $info.RedirectStandardError = $true
    $info.StandardOutputEncoding = [Text.Encoding]::UTF8
    $info.StandardErrorEncoding = [Text.Encoding]::UTF8
    foreach ($entry in @{ TEMP = $temp; TMP = $temp; NUGET_PACKAGES = $cache; DOTNET_CLI_HOME = (Join-Path $temp 'dotnet-home'); CUDA_CACHE_PATH = (Join-Path $temp 'cuda-cache'); DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'; DOTNET_NOLOGO = '1' }.GetEnumerator()) { $info.Environment[$entry.Key] = $entry.Value }
    foreach ($argument in $Arguments) { $info.ArgumentList.Add($argument) }
    $process = [Diagnostics.Process]::new()
    $process.StartInfo = $info
    try {
        if (-not $process.Start()) { throw "Could not start $Name." }
        $stdout = $process.StandardOutput.ReadToEndAsync()
        $stderr = $process.StandardError.ReadToEndAsync()
        if (-not $process.WaitForExit(60000)) { $process.Kill($true); throw "Owned process timed out: $Name" }
        $output = $stdout.GetAwaiter().GetResult()
        $errors = $stderr.GetAwaiter().GetResult()
        [IO.File]::WriteAllText((Join-Path $reports "$Name.stdout.log"), $output, [Text.UTF8Encoding]::new($false))
        [IO.File]::WriteAllText((Join-Path $reports "$Name.stderr.log"), $errors, [Text.UTF8Encoding]::new($false))
        $commands.Add([ordered]@{ Name = $Name; Executable = $Executable; Arguments = $Arguments; ProcessId = $process.Id; ExitCode = $process.ExitCode; Output = $output; DesktopWindow = $false })
        if ($process.ExitCode -ne 0) { throw "$Name failed: exit $($process.ExitCode). See retained logs." }
        return $output
    }
    finally { $process.Dispose() }
}

try {
    # This console has no desktop window. Record topology without imposing UI placement on a nonvisual check.
    Add-Type -AssemblyName System.Windows.Forms
    $screens = @([Windows.Forms.Screen]::AllScreens | ForEach-Object { [ordered]@{ Name = $_.DeviceName; Bounds = $_.Bounds.ToString(); WorkingArea = $_.WorkingArea.ToString() } })
    $sdkVersion = (Invoke-OwnedProcess 'sdk-version' $dotnet @('--version') $consumer).Trim()
    $null = Invoke-OwnedProcess 'build' $dotnet @('build', $project, '-c', 'Release', '--artifacts-path', $build, '--configfile', $config, "-p:SdkPackageVersion=$ExpectedVersion") $consumer
    $bin = Join-Path $build 'bin/OpenVisionLab.PackageConsumer.NetFrameworkSmoke/release'
    $exe = Join-Path $bin 'OpenVisionLab.PackageConsumer.NetFrameworkSmoke.exe'
    $native = Join-Path $bin 'OpenCvSharpExtern.dll'
    if (@(Get-ChildItem -LiteralPath $bin -Filter 'OpenCvSharpExtern.dll' -Recurse).Count -ne 1) { throw 'Expected exactly one automatically copied native DLL.' }
    $assets = Get-Content -LiteralPath (Join-Path $build 'obj/OpenVisionLab.PackageConsumer.NetFrameworkSmoke/project.assets.json') -Raw | ConvertFrom-Json
    $libraries = @($assets.libraries.PSObject.Properties)
    $sdkLibraries = @($libraries | Where-Object Name -Like 'OpenVisionLab.*')
    $runtimeLibraries = @($libraries | Where-Object Name -NotLike 'OpenVisionLab.*' | ForEach-Object Name | Sort-Object)
    $expectedRuntime = @('System.Buffers/4.5.1', 'System.Memory/4.5.4', 'System.Numerics.Vectors/4.5.0', 'System.Runtime.CompilerServices.Unsafe/4.7.1')
    if ($sdkLibraries.Count -ne 6 -or @($libraries | Where-Object { $_.Value.type -ne 'package' }).Count -ne 0 -or
        @($sdkLibraries | Where-Object Name -NotLike "OpenVisionLab.*/$ExpectedVersion").Count -ne 0 -or
        @((Compare-Object $expectedRuntime $runtimeLibraries)).Count -ne 0) { throw 'Exact SDK packages and the vendored OpenCvSharp managed runtime closure differ.' }
    $cachedNative = Join-Path $cache "openvisionlab.core/$ExpectedVersion/runtimes/win-x64/native/OpenCvSharpExtern.dll"
    $nativeHash = (Get-FileHash -LiteralPath $native -Algorithm SHA256).Hash
    if ($nativeHash -ne (Get-FileHash -LiteralPath $cachedNative -Algorithm SHA256).Hash) { throw 'Copied native DLL differs from the restored package.' }
    if ($CudaModule) {
        $cases = if ($CudaTilesOnly) { @(@{ Name = 'cuda-large-tiles'; Mode = 'tiles'; Module = $CudaModule }) } else { @(
            @{ Name = 'cuda-missing'; Mode = 'missing'; Module = $null },
            @{ Name = 'cuda-discovery'; Mode = 'discovery'; Module = $CudaDiscoveryModule },
            @{ Name = 'cuda-old'; Mode = 'old'; Module = $CudaOldModule },
            @{ Name = 'cuda-execution'; Mode = 'cuda'; Module = $CudaModule },
            @{ Name = 'cuda-fresh-recovery'; Mode = 'cuda'; Module = $CudaModule }
        ) }
        foreach ($case in $cases) {
            $caseDirectory = Join-Path $root $case.Name
            New-Item -ItemType Directory -Path $caseDirectory | Out-Null
            Get-ChildItem -LiteralPath $bin -File | Copy-Item -Destination $caseDirectory
            if ($case.Module) { Copy-Item -LiteralPath $case.Module -Destination (Join-Path $caseDirectory 'OpenVisionLab.Cuda.Native.dll') }
            $arguments = if ($CudaTilesOnly) { @('--cuda-tiles') } else { @('--cuda-local', $case.Mode) }
            $actual = Invoke-OwnedProcess $case.Name (Join-Path $caseDirectory (Split-Path $exe -Leaf)) $arguments $caseDirectory
            if ($actual -notmatch 'PASS\|framework-cuda-consumer' -or $actual -notmatch [regex]::Escape("|$nativeHash")) { throw 'CUDA consumer did not report expected OpenCV identity and completion.' }
            if ($case.Module -and $actual -notmatch [regex]::Escape((Get-FileHash -LiteralPath $case.Module -Algorithm SHA256).Hash)) { throw 'Actual CUDA module identity differs from selected module.' }
        }
    }
    else {
        $valid = Invoke-OwnedProcess 'valid' $exe @() $bin
        if ($valid -notmatch 'PASS\|framework-package-consumer' -or $valid -notmatch 'PASS\|canny-package-consumer' -or $valid -notmatch [regex]::Escape("|$nativeHash")) { throw 'Runtime did not report expected native identity and completion.' }
        $missing = Join-Path $root 'native-missing'
        New-Item -ItemType Directory -Path $missing | Out-Null
        Get-ChildItem -LiteralPath $bin -File | Where-Object Name -ne 'OpenCvSharpExtern.dll' | Copy-Item -Destination $missing
        $failure = Invoke-OwnedProcess 'native-missing' (Join-Path $missing (Split-Path $exe -Leaf)) @('--expect-native-missing') $missing
        if ($failure -notmatch 'PASS\|native-missing-fails-explicitly') { throw 'Missing native DLL did not produce the expected explicit failure.' }
        $recovery = Invoke-OwnedProcess 'fresh-recovery' $exe @() $bin
        if ($recovery -notmatch 'PASS\|framework-package-consumer' -or $recovery -notmatch 'PASS\|canny-package-consumer') { throw 'Fresh consumer did not recover.' }
    }
    $status = 'Passed'
}
catch {
    $status = 'Failed'
    $failureMessage = $_.Exception.Message
    throw
}
finally {
    $failureText = if (Get-Variable failureMessage -ErrorAction SilentlyContinue) { $failureMessage } else { $null }
    $topology = if (Get-Variable screens -ErrorAction SilentlyContinue) { $screens } else { @() }
    $result = [ordered]@{ Status = $status; Failure = $failureText; TargetFramework = 'net48'; Platform = 'x64'; ExpectedVersion = $ExpectedVersion; CudaMode = [bool]$CudaModule; CudaTilesOnly = [bool]$CudaTilesOnly; CudaModules = @($CudaModule, $CudaDiscoveryModule, $CudaOldModule); Source = $source; Feed = $feed; DependencySource = $DependencySource; SdkVersion = $(if (Get-Variable sdkVersion -ErrorAction SilentlyContinue) { $sdkVersion } else { $null }); NuGetConfig = $config; NativeCopy = 'Existing Core buildTransitive target; no manual copy'; MonitorScope = 'Nonvisual console with CreateNoWindow=true; no desktop window or UI placement'; MonitorTopology = $topology; Commands = @($commands.ToArray()) }
    [IO.File]::WriteAllText((Join-Path $reports 'framework-consumer.json'), ($result | ConvertTo-Json -Depth 12), [Text.UTF8Encoding]::new($false))
}
Write-Output "Framework package consumer passed: $reports"
