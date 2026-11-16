#requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string] $PackageDirectory,
    [Parameter(Mandatory)][string] $ExpectedVersion,
    [Parameter(Mandatory)][string] $ExpectedCommit,
    [Parameter(Mandatory)][string] $QrFixtureDirectory,
    [Parameter(Mandatory)][string] $OutputDirectory,
    [string] $DependencySource = 'https://api.nuget.org/v3/index.json',
    [string] $ProvenanceSourceDirectory
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repository = Split-Path $PSScriptRoot -Parent
if (-not $ProvenanceSourceDirectory) { $ProvenanceSourceDirectory = $repository }
$provenanceChecker = Join-Path (Resolve-Path -LiteralPath $ProvenanceSourceDirectory).Path 'eng/Verify-PackageProvenance.ps1'
$root = [IO.Path]::GetFullPath($OutputDirectory)
$packages = (Resolve-Path -LiteralPath $PackageDirectory).Path
if (Test-Path -LiteralPath $root) { throw 'Choose a new artifact-consumer directory; previous evidence is immutable.' }
if ((Test-Path 'D:\') -and [IO.Path]::GetPathRoot($root) -ne 'D:\') { throw 'Test evidence must be on D: when available.' }
New-Item -ItemType Directory -Path $root, "$root/logs", "$root/temp" -Force | Out-Null
$dotnet = (Get-Command dotnet).Source
$pwsh = (Get-Command pwsh).Source
$commands = [Collections.Generic.List[object]]::new()
$identityChecks = [Collections.Generic.List[object]]::new()
$priorTemp = $env:TEMP; $priorTmp = $env:TMP
$env:TEMP = "$root/temp"; $env:TMP = $env:TEMP
$status = 'Incomplete'; $failure = $null

function Invoke-Check([string] $Name, [string] $Executable, [string[]] $Arguments, [int] $ExpectedExit = 0) {
    $log = "$root/logs/$Name.log"
    & $Executable @Arguments *> $log
    $code = $LASTEXITCODE
    $commands.Add([ordered]@{ Name = $Name; Executable = $Executable; Arguments = $Arguments; ExitCode = $code; ExpectedExit = $ExpectedExit; Log = $log })
    if (($ExpectedExit -eq -1 -and $code -eq 0) -or ($ExpectedExit -ne -1 -and $code -ne $ExpectedExit)) { Get-Content -LiteralPath $log -Tail 12 | Write-Host; throw "$Name unexpected exit $code." }
    Write-Host "Passed: $Name (exit $code)"
}
function Assert-Binary([string] $Path, [string] $ReportedHash, [string] $Consumer) {
    $name = [IO.Path]::GetFileName($Path)
    if (-not $expectedBinaries.ContainsKey($name) -or $ReportedHash -cne $expectedBinaries[$name] -or (Get-FileHash -LiteralPath $Path).Hash -cne $expectedBinaries[$name]) { throw "Loaded/copy binary differs from frozen package: $Consumer / $name" }
    $identityChecks.Add([ordered]@{ Consumer = $Consumer; Path = $Path; Sha256 = $ReportedHash; PackageMatch = $true })
}
try {
    Invoke-Check 'provenance' $pwsh @('-NoProfile','-File',$provenanceChecker,'-PackageDirectory',$packages,'-ExpectedVersion',$ExpectedVersion,'-ExpectedCommit',$ExpectedCommit,'-ManifestPath',"$root/package-manifest.json")
    # Preparation is explicitly online. Only the separate subsequent consumers use the local complete feed.
    Invoke-Check 'prepare-net48-dependencies' $pwsh @('-NoProfile','-File',"$PSScriptRoot/Test-NetFrameworkPackageConsumer.ps1",'-PackageDirectory',$packages,'-ExpectedVersion',$ExpectedVersion,'-DependencySource',$DependencySource,'-OutputDirectory',"$root/prepare-net48")
    Invoke-Check 'prepare-first-inspection-dependencies' $pwsh @('-NoProfile','-File',"$PSScriptRoot/Test-FirstInspectionSample.ps1",'-PackageDirectory',$packages,'-ExpectedVersion',$ExpectedVersion,'-DependencySource',$DependencySource,'-OutputDirectory',"$root/prepare-cli",'-PackageSelectionOnly')
    & "$PSScriptRoot/New-SdkOfflineKit.ps1" -PackageDirectory $packages -ExpectedVersion $ExpectedVersion -ExpectedCommit $ExpectedCommit -DependencyCache @("$root/prepare-net48/nuget", "$root/prepare-cli/nuget") -OutputDirectory "$root/offline-kit"
    $manifest = Get-Content -LiteralPath "$root/offline-kit/manifest.json" -Raw | ConvertFrom-Json
    $expectedBinaries = @{}
    foreach ($package in $manifest.Files | Where-Object { $_.Id -like 'OpenVisionLab.*' }) {
        foreach ($binary in $package.Binaries) { $expectedBinaries[$binary.File] = $binary.Sha256 }
    }
    Invoke-Check 'net48-offline' $pwsh @('-NoProfile','-File',"$PSScriptRoot/Test-NetFrameworkPackageConsumer.ps1",'-PackageDirectory',"$root/offline-kit/feed",'-ExpectedVersion',$ExpectedVersion,'-OutputDirectory',"$root/net48")
    $consumer = "$root/consumer-inputs/OpenVisionLab.PackageConsumer.Smoke"
    $framework = "$root/consumer-inputs/OpenVisionLab.PackageConsumer.NetFrameworkSmoke"
    New-Item -ItemType Directory -Path $consumer,$framework -Force | Out-Null
    Get-ChildItem -LiteralPath "$repository/tests/OpenVisionLab.PackageConsumer.Smoke" -File | Where-Object { $_.Extension -in @('.cs','.csproj') } | Copy-Item -Destination $consumer
    Copy-Item -LiteralPath "$repository/tests/OpenVisionLab.PackageConsumer.NetFrameworkSmoke/HardeningChecks.cs" -Destination $framework
    Copy-Item -LiteralPath "$repository/global.json" -Destination "$root/consumer-inputs"
    $project = "$consumer/OpenVisionLab.PackageConsumer.Smoke.csproj"
    if (Select-String -LiteralPath $project -Pattern 'ProjectReference|HintPath|src[/\\]OpenVisionLab') { throw 'Consumer contains an SDK source bypass.' }
    Invoke-Check 'net8-offline-build' $dotnet @('build',$project,'-c','Release','--configfile',"$root/offline-kit/NuGet.Config",'--artifacts-path',"$root/net8-build",'-o',"$root/net8-bin","-p:PackageVersion=$ExpectedVersion","-p:RestorePackagesPath=$root/net8-empty-cache")
    Invoke-Check 'net8-offline-run' $dotnet @("$root/net8-bin/OpenVisionLab.PackageConsumer.Smoke.dll")
    Invoke-Check 'first-inspection-offline' $pwsh @('-NoProfile','-File',"$PSScriptRoot/Test-FirstInspectionSample.ps1",'-PackageDirectory',"$root/offline-kit/feed",'-ExpectedVersion',$ExpectedVersion,'-OutputDirectory',"$root/first-inspection",'-QrFixtureDirectory',$QrFixtureDirectory)
    foreach ($consumerName in @('net48','net8')) {
        $log = if ($consumerName -eq 'net48') { "$root/net48/reports/valid.stdout.log" } else { "$root/logs/net8-offline-run.log" }
        $lines = @(Get-Content -LiteralPath $log | Where-Object { $_ -match '^(ASSEMBLY|NATIVE)\|' })
        if ($lines.Count -ne 8) { throw "Expected seven loaded managed modules and one native identity: $consumerName" }
        foreach ($line in $lines) { $parts = $line.Split('|'); if ($parts[0] -eq 'NATIVE') { Assert-Binary $parts[1] $parts[2] $consumerName } else { Assert-Binary $parts[2] $parts[4] $consumerName } }
    }
    $cli = Get-Content -LiteralPath "$root/first-inspection/reports/inspect-translated.stdout.log" -Raw | ConvertFrom-Json
    foreach ($binary in $cli.Packages) { Assert-Binary $binary.Path $binary.Sha256 'FirstInspection' }
    foreach ($binary in $cli.NativeRuntimes) { Assert-Binary $binary.FileName $binary.Sha256 'FirstInspection' }
    if (@(Get-ChildItem -LiteralPath "$root/net8-bin" -Recurse -Filter OpenCvSharpExtern.dll -File).Count -ne 1) { throw 'Expected one net8 native runtime copy.' }

    # Negative feeds are owned copies. The frozen candidate and earlier complete results are never changed.
    foreach ($missingId in @('OpenVisionLab.Core','System.Memory')) {
        $negative = "$root/missing-$missingId"
        New-Item -ItemType Directory -Path "$negative/feed" -Force | Out-Null
        foreach ($file in $manifest.Files | Where-Object { $_.Id -ne $missingId }) { Copy-Item -LiteralPath (Join-Path "$root/offline-kit/feed" $file.File) -Destination "$negative/feed" }
        $escaped = [Security.SecurityElement]::Escape("$negative/feed")
        [IO.File]::WriteAllText("$negative/NuGet.Config", "<configuration><packageSources><clear/><add key=`"only`" value=`"$escaped`"/></packageSources></configuration>")
        Invoke-Check "missing-$missingId" $dotnet @('restore',$project,'--configfile',"$negative/NuGet.Config",'--artifacts-path',"$negative/build","-p:PackageVersion=$ExpectedVersion","-p:RestorePackagesPath=$negative/empty-cache") -1
    }
    Invoke-Check 'restore-after-missing' $dotnet @('restore',$project,'--configfile',"$root/offline-kit/NuGet.Config",'--artifacts-path',"$root/recovery-build","-p:PackageVersion=$ExpectedVersion","-p:RestorePackagesPath=$root/recovery-empty-cache")
    Invoke-Check 'build-after-missing' $dotnet @('build',$project,'-c','Release','--no-restore','--artifacts-path',"$root/recovery-build",'-o',"$root/recovery-bin","-p:PackageVersion=$ExpectedVersion","-p:RestorePackagesPath=$root/recovery-empty-cache")
    Invoke-Check 'run-after-missing' $dotnet @("$root/recovery-bin/OpenVisionLab.PackageConsumer.Smoke.dll")
    Invoke-Check 'wrong-version-rejected' $pwsh @('-NoProfile','-File',"$PSScriptRoot/New-SdkOfflineKit.ps1",'-ExpectedVersion','0.0.0-wrong','-ExpectedCommit',$ExpectedCommit,'-OutputDirectory',"$root/offline-kit",'-VerifyOnly') -1
    $tampered = "$root/tampered-kit"
    New-Item -ItemType Directory -Path $tampered | Out-Null
    Copy-Item -LiteralPath "$root/offline-kit/manifest.json" -Destination $tampered
    New-Item -ItemType Directory -Path "$tampered/feed" | Out-Null
    foreach ($file in $manifest.Files) { Copy-Item -LiteralPath (Join-Path "$root/offline-kit/feed" $file.File) -Destination "$tampered/feed" }
    $target = Join-Path "$tampered/feed" ($manifest.Files | Where-Object Id -eq 'OpenVisionLab.Core').File
    $stream = [IO.File]::Open($target, 'Open', 'ReadWrite', 'None')
    try { $first = $stream.ReadByte(); $stream.Position = 0; $stream.WriteByte($first -bxor 1) } finally { $stream.Dispose() }
    Invoke-Check 'modified-package-rejected' $pwsh @('-NoProfile','-File',"$PSScriptRoot/New-SdkOfflineKit.ps1",'-ExpectedVersion',$ExpectedVersion,'-ExpectedCommit',$ExpectedCommit,'-OutputDirectory',$tampered,'-VerifyOnly') -1
    $wrongNative = "$root/wrong-native/OpenCvSharpExtern.dll"
    New-Item -ItemType Directory -Path (Split-Path $wrongNative -Parent) | Out-Null
    Copy-Item -LiteralPath "$root/net8-bin/OpenVisionLab.Core.dll" -Destination $wrongNative
    $detected = $false
    try { Assert-Binary $wrongNative (Get-FileHash -LiteralPath $wrongNative).Hash 'WrongNativeNegative' } catch { $detected = $true }
    if (-not $detected) { throw 'Incorrect native copy was accepted.' }
    Invoke-Check 'final-frozen-kit' $pwsh @('-NoProfile','-File',"$PSScriptRoot/New-SdkOfflineKit.ps1",'-ExpectedVersion',$ExpectedVersion,'-ExpectedCommit',$ExpectedCommit,'-OutputDirectory',"$root/offline-kit",'-VerifyOnly')
    $status = 'Complete'
}
catch { $failure = $_.Exception.Message; throw }
finally {
    [ordered]@{
        Status = $status; Failure = $failure; SourceCommit = $ExpectedCommit; ProvenanceSourceDirectory = $ProvenanceSourceDirectory; ConsumerSourceCommit = (& git -C $repository rev-parse HEAD).Trim(); PackageVersion = $ExpectedVersion; Commands = $commands.ToArray(); LoadedBinaryChecks = $identityChecks.ToArray()
        KitManifest = "$root/offline-kit/manifest.json"; OS = [Runtime.InteropServices.RuntimeInformation]::OSDescription; Architecture = [Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString()
        Offline = 'Local sources only; independent initially empty caches; no firewall/network setting was changed. Online dependency preparation is separate.'
        Scope = 'Exact package bytes: net48/x64/C#7.3, net8/win-x64, FirstInspection CLI; missing SDK/dependency, wrong version/package/native identity and recovery.'
        NotRun = @('Network-disconnected execution','Console.CancelKeyPress control-event delivery','GPU/native fixture execution','Real camera, UI and field metrology','Publication or deployment')
    } | ConvertTo-Json -Depth 14 | Set-Content -LiteralPath "$root/artifact-consumers.json" -Encoding utf8
    $env:TEMP = $priorTemp; $env:TMP = $priorTmp
}
