#requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string] $EvidenceDirectory,
    [Parameter(Mandatory)][string] $PackageVersion,
    [string] $ImageManifest,
    [string] $CudaExecutionModule,
    [string] $CudaDiscoveryModule,
    [string] $CudaFixtureDirectory,
    [string] $CudaLocalFixtureDirectory
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repository = Split-Path $PSScriptRoot -Parent
$root = [IO.Path]::GetFullPath($EvidenceDirectory)
if (-not $root.StartsWith('D:\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Hardening evidence must be physically on D:.' }
if (Test-Path -LiteralPath $root) { throw 'Choose a new evidence directory; old runs are retained.' }
if ($PackageVersion -notmatch '^(\d+\.\d+\.\d+-(hardening|p3)\.[0-9.]+|3\.3\.0-rc\.2)$') { throw 'Select a unique local hardening/P3 prerelease identity or the approved 3.3.0-rc.2 candidate.' }
$commit = (& git -C $repository rev-parse HEAD).Trim()
if ($LASTEXITCODE -ne 0 -or $commit -notmatch '^[0-9a-f]{40}$') { throw 'An exact committed source is required.' }
$dirty = @(& git -C $repository status --porcelain=v1 --untracked-files=all)
if ($LASTEXITCODE -ne 0 -or $dirty.Count -ne 0) { throw 'Run from an isolated clean checkout; preserve the development worktree.' }
New-Item -ItemType Directory -Path $root, "$root\temp", "$root\logs", "$root\packages", "$root\offline-feed" | Out-Null
$steps = [Collections.Generic.List[object]]::new()
$previousDirectory = Get-Location
$names = @('TEMP','TMP','NUGET_PACKAGES','NUGET_HTTP_CACHE_PATH','DOTNET_CLI_HOME','DOTNET_SKIP_FIRST_TIME_EXPERIENCE','DOTNET_GENERATE_ASPNET_CERTIFICATE','CUDA_CACHE_PATH','OVL_CUDA_TEST_MODULE','OVL_CUDA_EXECUTION_FIXTURES','OVL_COMPUTE_TEST_MODULES','OVL_COMPUTE_REAL_PROBE','OVL_CUDA_LOCAL_REPORTS','OVL_CUDA_LOCAL_FIXTURES')
$previous = @{}
foreach ($name in $names) { $previous[$name] = [Environment]::GetEnvironmentVariable($name, 'Process') }
$status = 'Incomplete'
$failure = $null
# Optional inputs enable these branches; every scope is required for full qualification.
$hardeningScope = [ordered]@{
    Study = [ordered]@{ Enabled = [bool]$ImageManifest; Status = $(if ($ImageManifest) { 'Pending' } else { 'NotEnabled' }); MissingInputs = @(if (-not $ImageManifest) { 'ImageManifest: readable image manifest for the hardening study' }) }
    CudaDevice = [ordered]@{ Enabled = [bool]$CudaExecutionModule; Status = $(if ($CudaExecutionModule) { 'Pending' } else { 'NotEnabled' }); MissingInputs = @(if (-not $CudaExecutionModule) { 'CudaExecutionModule: compatible CUDA execution module' }) }
    CudaProbe = [ordered]@{ Enabled = [bool]($CudaExecutionModule -and $CudaDiscoveryModule); Status = $(if ($CudaExecutionModule -and $CudaDiscoveryModule) { 'Pending' } else { 'NotEnabled' }); MissingInputs = @(if (-not $CudaExecutionModule) { 'CudaExecutionModule: compatible CUDA execution module' }; if (-not $CudaDiscoveryModule) { 'CudaDiscoveryModule: compatible CUDA discovery/probe module' }) }
    NativeFixtures = [ordered]@{ Enabled = [bool]($CudaExecutionModule -and $CudaFixtureDirectory); Status = $(if ($CudaExecutionModule -and $CudaFixtureDirectory) { 'Pending' } else { 'NotEnabled' }); MissingInputs = @(if (-not $CudaExecutionModule) { 'CudaExecutionModule: compatible CUDA execution module' }; if (-not $CudaFixtureDirectory) { 'CudaFixtureDirectory: native compute/cancellation fixtures' }) }
    CudaLocalFixtures = [ordered]@{ Enabled = [bool]($CudaExecutionModule -and $CudaLocalFixtureDirectory); Status = $(if ($CudaExecutionModule -and $CudaLocalFixtureDirectory) { 'Pending' } else { 'NotEnabled' }); MissingInputs = @(if (-not $CudaExecutionModule) { 'CudaExecutionModule: compatible CUDA execution module' }; if (-not $CudaLocalFixtureDirectory) { 'CudaLocalFixtureDirectory: adaptive, tile, primitive and old-module fixtures' }) }
}
function Invoke-Check([string] $Name, [string] $Executable, [string[]] $Arguments) {
    $log = Join-Path $root "logs/$Name.log"
    $timer = [Diagnostics.Stopwatch]::StartNew()
    & $Executable @Arguments *> $log
    $code = $LASTEXITCODE
    $steps.Add([ordered]@{ Name = $Name; Executable = $Executable; Arguments = $Arguments; ExitCode = $code; Seconds = $timer.Elapsed.TotalSeconds; Log = $log })
    if ($code -ne 0) { Get-Content -LiteralPath $log -Tail 16 | Write-Host; throw "$Name failed with exit $code. Evidence: $log" }
    Write-Host "Passed: $Name"
}
try {
    Set-Location -LiteralPath $repository
    $env:TEMP = "$root\temp"; $env:TMP = $env:TEMP
    $env:NUGET_PACKAGES = "$root\build-cache"; $env:NUGET_HTTP_CACHE_PATH = "$root\http-cache"
    $env:DOTNET_CLI_HOME = "$root\dotnet-home"; $env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'; $env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
    $env:CUDA_CACHE_PATH = "$root\cuda-cache"
    foreach ($name in $names | Where-Object { $_ -like 'OVL_*' }) { [Environment]::SetEnvironmentVariable($name, $null, 'Process') }
    $dotnet = (Get-Command dotnet).Source
    $pwsh = (Get-Command pwsh).Source
    $build = "$root\build"
    $smoke = "$build\bin\OpenVisionLab.Inspection.Smoke\release\OpenVisionLab.Inspection.Smoke.dll"
    Invoke-Check 'sdk-info' $dotnet @('--info')
    Invoke-Check 'tools' $dotnet @('tool','restore')
    Invoke-Check 'restore' $dotnet @('restore','OpenVisionLab.VisionSdk.sln','--artifacts-path',$build)
    Invoke-Check 'build' $dotnet @('build','OpenVisionLab.VisionSdk.sln','-c','Release','--no-restore','--artifacts-path',$build)
    Invoke-Check 'coverage' $pwsh @('-NoProfile','-File',"$PSScriptRoot\Verify-Coverage.ps1",'-SmokeAssembly',$smoke,'-OutputPath',"$root\coverage.cobertura.xml",'-SmokeReportPath',"$root\cpu-smoke.json")
    Invoke-Check 'coverage-negative' $pwsh @('-NoProfile','-File',"$PSScriptRoot\Test-CoverageNegative.ps1",'-SmokeAssembly',$smoke,'-CoveragePath',"$root\coverage.cobertura.xml",'-SmokeReportPath',"$root\cpu-smoke.json",'-OutputDirectory',"$root\coverage-negative")
    Invoke-Check 'api' $pwsh @('-NoProfile','-File',"$PSScriptRoot\Verify-PublicApi.ps1",'-AssemblyDirectory',(Split-Path $smoke -Parent))
    Invoke-Check 'analyzer' $pwsh @('-NoProfile','-File',"$PSScriptRoot\Verify-AnalyzerBaseline.ps1",'-SolutionPath','OpenVisionLab.VisionSdk.sln','-ArtifactsPath',"$root\analyzer")
    Invoke-Check 'pack' $dotnet @('pack','OpenVisionLab.VisionSdk.sln','-c','Release','--no-build','--artifacts-path',$build,'-o',"$root\packages","-p:PackageVersion=$PackageVersion")
    Invoke-Check 'provenance' $pwsh @('-NoProfile','-File',"$PSScriptRoot\Verify-PackageProvenance.ps1",'-PackageDirectory',"$root\packages",'-ExpectedVersion',$PackageVersion,'-ExpectedCommit',$commit,'-RequireCleanWorktree','-ManifestPath',"$root\package-manifest.json")
    Invoke-Check 'provenance-negative' $pwsh @('-NoProfile','-File',"$PSScriptRoot\Test-PackageProvenanceNegative.ps1",'-PackageDirectory',"$root\packages",'-ExpectedVersion',$PackageVersion,'-ExpectedCommit',$commit,'-OutputDirectory',"$root\provenance-negative")
    Invoke-Check 'net48-online-dependency-preparation' $pwsh @('-NoProfile','-File',"$PSScriptRoot\Test-NetFrameworkPackageConsumer.ps1",'-PackageDirectory',"$root\packages",'-ExpectedVersion',$PackageVersion,'-DependencySource','https://api.nuget.org/v3/index.json','-OutputDirectory',"$root\net48-online-preparation")

    # A separate complete feed; the provenance directory retains exactly the six SDK packages.
    Get-ChildItem -LiteralPath "$root\packages" -Filter '*.nupkg' -File | Copy-Item -Destination "$root\offline-feed"
    Get-ChildItem -LiteralPath $env:NUGET_PACKAGES -Recurse -Filter '*.nupkg' -File |
        Where-Object { $_.Directory.Parent.Name -ne 'dotnet-coverage' } | Copy-Item -Destination "$root\offline-feed"
    Get-ChildItem -LiteralPath "$root\net48-online-preparation\nuget" -Recurse -Filter '*.nupkg' -File | Copy-Item -Destination "$root\offline-feed"
    Get-ChildItem -LiteralPath "$root\offline-feed" -Filter '*.nupkg' -File | ForEach-Object {
        [ordered]@{ File = $_.Name; Length = $_.Length; Sha256 = (Get-FileHash -LiteralPath $_.FullName).Hash }
    } | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath "$root\offline-feed-manifest.json" -Encoding utf8
    Invoke-Check 'net48-offline' $pwsh @('-NoProfile','-File',"$PSScriptRoot\Test-NetFrameworkPackageConsumer.ps1",'-PackageDirectory',"$root\offline-feed",'-ExpectedVersion',$PackageVersion,'-OutputDirectory',"$root\net48-offline")

    $consumerRoot = "$root\consumer-inputs"
    $net8 = "$consumerRoot\OpenVisionLab.PackageConsumer.Smoke"
    $framework = "$consumerRoot\OpenVisionLab.PackageConsumer.NetFrameworkSmoke"
    New-Item -ItemType Directory -Path $net8,$framework | Out-Null
    Get-ChildItem -LiteralPath "$repository\tests\OpenVisionLab.PackageConsumer.Smoke" -File |
        Where-Object { $_.Extension -in @('.cs','.csproj') } | Copy-Item -Destination $net8
    Copy-Item -LiteralPath "$repository\tests\OpenVisionLab.PackageConsumer.NetFrameworkSmoke\HardeningChecks.cs" -Destination $framework
    Copy-Item -LiteralPath "$repository\global.json" -Destination $consumerRoot
    $config = "$consumerRoot\NuGet.Config"
    $escaped = [Security.SecurityElement]::Escape("$root\offline-feed")
    [IO.File]::WriteAllText($config,"<configuration><packageSources><clear/><add key=`"offline`" value=`"$escaped`"/></packageSources></configuration>")
    $project = "$net8\OpenVisionLab.PackageConsumer.Smoke.csproj"
    if (Select-String -LiteralPath $project -Pattern 'ProjectReference|HintPath|src[/\\]OpenVisionLab') { throw 'Package consumer contains an SDK-source bypass.' }
    Invoke-Check 'net8-offline-build' $dotnet @('build',$project,'-c','Release','--configfile',$config,'--artifacts-path',"$root\net8-build",'-o',"$root\net8-output","-p:PackageVersion=$PackageVersion","-p:RestorePackagesPath=$root\net8-empty-cache")
    Invoke-Check 'net8-offline-run' $dotnet @("$root\net8-output\OpenVisionLab.PackageConsumer.Smoke.dll")
    $native = @(Get-ChildItem -LiteralPath "$root\net8-output" -Recurse -Filter OpenCvSharpExtern.dll -File)
    if ($native.Count -ne 1 -or $native[0].DirectoryName -ne "$root\net8-output") { throw 'Expected one native DLL at the net8 output root.' }
    Invoke-Check 'export-qr-fixtures' $dotnet @($smoke,'--export-qr-fixtures',"$root\qr-fixtures")
    Invoke-Check 'first-inspection-offline' $pwsh @('-NoProfile','-File',"$PSScriptRoot\Test-FirstInspectionSample.ps1",'-PackageDirectory',"$root\offline-feed",'-ExpectedVersion',$PackageVersion,'-OutputDirectory',"$root\first-inspection-offline",'-QrFixtureDirectory',"$root\qr-fixtures")
    if ($hardeningScope.Study.Enabled) {
        Invoke-Check 'study' $dotnet @($smoke,'--hardening-study',"$root\study",(Resolve-Path -LiteralPath $ImageManifest).Path)
        $hardeningScope.Study.Status = 'Passed'
    }
    if ($CudaExecutionModule) {
        $env:OVL_CUDA_TEST_MODULE = (Resolve-Path -LiteralPath $CudaExecutionModule).Path
        $env:OVL_CUDA_LOCAL_REPORTS = "$root\cuda-local"
        if ($CudaDiscoveryModule) { $env:OVL_COMPUTE_REAL_PROBE = (Resolve-Path -LiteralPath $CudaDiscoveryModule).Path }
        if ($CudaFixtureDirectory) { $env:OVL_CUDA_EXECUTION_FIXTURES = (Resolve-Path -LiteralPath $CudaFixtureDirectory).Path; $env:OVL_COMPUTE_TEST_MODULES = $env:OVL_CUDA_EXECUTION_FIXTURES }
        if ($CudaLocalFixtureDirectory) { $env:OVL_CUDA_LOCAL_FIXTURES = (Resolve-Path -LiteralPath $CudaLocalFixtureDirectory).Path }
        $cudaLocalCopy = Join-Path (Split-Path $smoke -Parent) 'OpenVisionLab.Cuda.Native.dll'
        if (Test-Path -LiteralPath $cudaLocalCopy) { throw 'The isolated CUDA test must not replace an existing module.' }
        $cudaHash = (Get-FileHash -LiteralPath $env:OVL_CUDA_TEST_MODULE).Hash
        Copy-Item -LiteralPath $env:OVL_CUDA_TEST_MODULE -Destination $cudaLocalCopy
        try {
            Invoke-Check 'cuda-device-and-fixtures' $dotnet @($smoke,'--filter','Cuda','--report',"$root\cuda-smoke.json")
            $hardeningScope.CudaDevice.Status = 'Passed'
            if ($hardeningScope.CudaLocalFixtures.Enabled) { $hardeningScope.CudaLocalFixtures.Status = 'Passed' }
        }
        finally {
            if ((Get-FileHash -LiteralPath $cudaLocalCopy).Hash -ne $cudaHash) { throw 'The owned CUDA test copy changed; retain it for inspection.' }
            Remove-Item -LiteralPath $cudaLocalCopy
        }
        Invoke-Check 'compute-discovery-and-fallback' $dotnet @($smoke,'--filter','Compute','--report',"$root\compute-smoke.json")
        if ($hardeningScope.CudaProbe.Enabled) { $hardeningScope.CudaProbe.Status = 'Passed' }
        if ($hardeningScope.NativeFixtures.Enabled) {
            Invoke-Check 'native-shared-borrowers-cancellation' $dotnet @($smoke,'--filter','native shared borrowers','--report',"$root\native-shared-borrowers-smoke.json")
            $hardeningScope.NativeFixtures.Status = 'Passed'
        }
    }
    if (@(& git -C $repository status --porcelain=v1 --untracked-files=all).Count -ne 0) { throw 'Validation changed the clean source.' }
    $status = if (@($hardeningScope.Values | Where-Object { $_.Status -ne 'Passed' }).Count -eq 0) { 'Complete' } else { 'Blocked' }
}
catch { $failure = $_.Exception.Message; throw }
finally {
    [ordered]@{ Status = $status; Failure = $failure; Commit = $commit; Repository = $repository; PackageVersion = $PackageVersion;
        Steps = $steps.ToArray(); Offline = 'clear sources; local complete feed; independent initially empty net8/net48/sample caches';
        CudaExecutionModule = $CudaExecutionModule; CudaDiscoveryModule = $CudaDiscoveryModule; CudaFixtureDirectory = $CudaFixtureDirectory; CudaLocalFixtureDirectory = $CudaLocalFixtureDirectory;
        Boundary = 'Local technical qualification only; no publication, deployment, calibrated field accuracy or production soak';
        HardeningScope = $hardeningScope;
        Prerequisites = @($hardeningScope.Values | ForEach-Object { $_.MissingInputs } | Select-Object -Unique);
        ScopeBoundary = @('P3 Host implementation is device-free; no actual camera SDK or desktop UI framework');
        Unverified = @('Physical metrology/calibration/customer tolerances','Production operation/takt','Other OS/architectures','Distribution/legal clearance') + @(
            foreach ($scope in $hardeningScope.Keys) {
                if ($hardeningScope[$scope].Status -ne 'Passed') {
                    "$scope ($($hardeningScope[$scope].Status)): full qualification not verified; missing inputs: $($hardeningScope[$scope].MissingInputs -join '; ')"
                }
            }
        )
    } | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath "$root\qualification.json" -Encoding utf8
    foreach ($name in $names) { [Environment]::SetEnvironmentVariable($name, $previous[$name], 'Process') }
    Set-Location $previousDirectory
}
