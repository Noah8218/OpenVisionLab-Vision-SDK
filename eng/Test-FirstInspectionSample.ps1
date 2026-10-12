#requires -Version 7.0
[CmdletBinding()]
param(
    [Parameter(Mandatory)][string] $PackageDirectory,
    [Parameter(Mandatory)][string] $ExpectedVersion,
    [Parameter(Mandatory)][string] $OutputDirectory,
    [string] $DependencySource,
    [string] $CudaModule,
    [string] $QrFixtureDirectory,
    [switch] $PackageSelectionOnly
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repository = Split-Path $PSScriptRoot -Parent
$root = [IO.Path]::GetFullPath($OutputDirectory)
$feed = (Resolve-Path -LiteralPath $PackageDirectory).Path
if ((Test-Path -LiteralPath $root) -and (Get-ChildItem -LiteralPath $root -Force | Select-Object -First 1)) { throw 'Choose a new, empty output directory; previous evidence is retained.' }
if ((Test-Path 'D:\') -and ([IO.Path]::GetPathRoot($root) -ne 'D:\')) { throw 'On this workstation, generated test outputs must be on D:.' }
$reports = Join-Path $root 'reports'
$consumer = Join-Path $root 'consumer'
$bin = Join-Path $root 'bin'
$build = Join-Path $root 'build'
$temp = Join-Path $root 'temp'
$cache = Join-Path $root 'nuget'
New-Item -ItemType Directory -Force -Path $reports, $consumer, $bin, $temp, $cache | Out-Null
$cases = [Collections.Generic.List[object]]::new()
$commands = [Collections.Generic.List[object]]::new()
$dotnet = (Get-Command dotnet -ErrorAction Stop).Source
$dll = Join-Path $bin 'OpenVisionLab.FirstInspection.dll'

function Assert-Case([bool] $Condition, [string] $Message) {
    if (-not $Condition) { throw $Message }
}

function Invoke-OwnedProcess([string] $Name, [string[]] $Arguments) {
    $info = [Diagnostics.ProcessStartInfo]::new($dotnet)
    $info.WorkingDirectory = $consumer
    $info.UseShellExecute = $false
    $info.CreateNoWindow = $true
    $info.RedirectStandardOutput = $true
    $info.RedirectStandardError = $true
    $info.StandardOutputEncoding = [Text.Encoding]::UTF8
    $info.StandardErrorEncoding = [Text.Encoding]::UTF8
    $info.Environment['TEMP'] = $temp
    $info.Environment['TMP'] = $temp
    $info.Environment['NUGET_PACKAGES'] = $cache
    foreach ($argument in $Arguments) { $info.ArgumentList.Add($argument) }
    $process = [Diagnostics.Process]::new()
    $process.StartInfo = $info
    try {
        Assert-Case $process.Start() "Could not start $Name."
        $processId = $process.Id
        $stdout = $process.StandardOutput.ReadToEndAsync()
        $stderr = $process.StandardError.ReadToEndAsync()
        if (-not $process.WaitForExit(60000)) { $process.Kill($true); throw "Owned process timed out: $Name" }
        $output = $stdout.GetAwaiter().GetResult()
        $errors = $stderr.GetAwaiter().GetResult()
        [IO.File]::WriteAllText((Join-Path $reports "$Name.stdout.log"), $output, [Text.UTF8Encoding]::new($false))
        [IO.File]::WriteAllText((Join-Path $reports "$Name.stderr.log"), $errors, [Text.UTF8Encoding]::new($false))
        $commands.Add([ordered]@{ Name = $Name; ProcessId = $processId; Arguments = $Arguments; ExitCode = $process.ExitCode; Stdout = "$Name.stdout.log"; Stderr = "$Name.stderr.log" })
        return [pscustomobject]@{ ExitCode = $process.ExitCode; Output = $output; Errors = $errors; ProcessId = $processId }
    }
    finally { $process.Dispose() }
}

function Invoke-Sample([string] $Name, [string[]] $Arguments, [int] $ExpectedExit, [string] $ExpectedStatus) {
    $result = Invoke-OwnedProcess $Name (@($dll) + $Arguments)
    Assert-Case ($result.ExitCode -eq $ExpectedExit) "$Name exit $($result.ExitCode), expected $ExpectedExit. See retained stdout/stderr."
    $data = $result.Output | ConvertFrom-Json
    Assert-Case ($data.Status -eq $ExpectedStatus) "$Name status $($data.Status), expected $ExpectedStatus."
    return $data
}

function Record-Case([string] $Id, [string] $Scope, [string[]] $Evidence) {
    $cases.Add([ordered]@{ Id = $Id; Result = 'Pass'; Scope = $Scope; Evidence = $Evidence })
}

# Expectations are authored here from fixture geometry and the synthetic interpolation contract,
# never copied from the executed Tool result or the generated expected.json.
function Assert-Inspection($Data, [int] $Dx, [int] $Dy) {
    Assert-Case ($Data.Status -eq 'Accepted' -and $Data.Units -eq 'px' -and $Data.Geometry -eq 'translation-only') 'Inspection contract differs.'
    Assert-Case ([Math]::Abs($Data.Pose.X - (112 + $Dx)) -le 1 -and [Math]::Abs($Data.Pose.Y - (92 + $Dy)) -le 1) 'Location differs from authored center.'
    Assert-Case ($Data.Pose.Angle -eq 0 -and $Data.Pose.Scale -eq 1) 'Unexpected rotation/scale.'
    Assert-Case ($Data.BlobCount -eq 1 -and $Data.BlobAreaPx -eq 165) 'Presence differs from the 15x11 authored mark.'
    Assert-Case ($Data.Line.Points.Count -ge 3) 'Missing fitted points.'
    $intercept = 320 + $Dx + 47.25 - .03 * (240 + $Dy)
    $maximum = ($Data.Line.Points | ForEach-Object { [Math]::Abs($_.X - $intercept - .03 * $_.Y) } | Measure-Object -Maximum).Maximum
    Assert-Case ($maximum -le .125 -and $Data.Line.OrthogonalRmsePx -le .125 -and [Math]::Abs($Data.Line.Slope - .03) -le .002) "Analytic edge gate failed: maximum=$maximum."
}

function New-DamagedBundle([string] $Name, [scriptblock] $Mutation, [switch] $Duplicate) {
    $entries = @{}
    $archive = [IO.Compression.ZipFile]::OpenRead($bundle)
    try {
        foreach ($entry in $archive.Entries) {
            $stream = $entry.Open()
            $memory = [IO.MemoryStream]::new()
            try { $stream.CopyTo($memory); $entries[$entry.FullName] = $memory.ToArray() }
            finally { $stream.Dispose(); $memory.Dispose() }
        }
    }
    finally { $archive.Dispose() }
    & $Mutation $entries
    $destination = Join-Path $root "$Name.zip"
    $archive = [IO.Compression.ZipFile]::Open($destination, [IO.Compression.ZipArchiveMode]::Create)
    try {
        foreach ($name in $entries.Keys) {
            $stream = $archive.CreateEntry($name).Open()
            try { $stream.Write($entries[$name], 0, $entries[$name].Length) } finally { $stream.Dispose() }
        }
        if ($Duplicate) {
            $stream = $archive.CreateEntry('host.json').Open()
            try { $stream.Write($entries['host.json'], 0, $entries['host.json'].Length) } finally { $stream.Dispose() }
        }
    }
    finally { $archive.Dispose() }
    return $destination
}

function Change-Host($Entries, [scriptblock] $Change) {
    $hostSettings = [Text.Encoding]::UTF8.GetString($Entries['host.json']) | ConvertFrom-Json
    & $Change $hostSettings
    $Entries['host.json'] = [Text.Encoding]::UTF8.GetBytes(($hostSettings | ConvertTo-Json -Depth 10))
}

function Change-Xml($Entries, [scriptblock] $Change) {
    [xml] $xml = [Text.Encoding]::UTF8.GetString($Entries['pipeline.xml'])
    & $Change $xml
    $Entries['pipeline.xml'] = [Text.Encoding]::UTF8.GetBytes($xml.OuterXml)
}

try {
    $sample = Join-Path $repository 'samples/OpenVisionLab.FirstInspection'
    foreach ($name in @('Program.cs', 'OpenVisionLab.FirstInspection.csproj')) { Copy-Item -LiteralPath (Join-Path $sample $name) -Destination $consumer }
    Assert-Case (-not (Select-String -LiteralPath (Join-Path $consumer 'OpenVisionLab.FirstInspection.csproj') -Pattern 'ProjectReference|Compile Include')) 'Consumer must use packages only.'
    $escapedFeed = [Security.SecurityElement]::Escape($feed)
    $config = Join-Path $consumer 'NuGet.Config'
    $dependencyEntry = ''
    $sourceMapping = ''
    if ($DependencySource) {
        $escapedDependencySource = [Security.SecurityElement]::Escape($DependencySource)
        $dependencyEntry = "<add key=`"dependencies`" value=`"$escapedDependencySource`"/>"
        $sourceMapping = '<packageSourceMapping><packageSource key="isolated-sdk"><package pattern="OpenVisionLab.*"/></packageSource><packageSource key="dependencies"><package pattern="*"/></packageSource></packageSourceMapping>'
    }
    [IO.File]::WriteAllText($config, "<configuration><packageSources><clear/><add key=`"isolated-sdk`" value=`"$escapedFeed`"/>$dependencyEntry</packageSources>$sourceMapping</configuration>")
    $project = Join-Path $consumer 'OpenVisionLab.FirstInspection.csproj'
    foreach ($selection in @(@{ Name = 'version-omitted'; Value = $null }, @{ Name = 'version-empty'; Value = '' }, @{ Name = 'version-unavailable-cold'; Value = '0.0.0-selection-missing' })) {
        $arguments = @('restore', $project, '--artifacts-path', $build, '--configfile', $config)
        if ($null -ne $selection.Value) { $arguments += "-p:PackageVersion=$($selection.Value)" }
        $rejected = Invoke-OwnedProcess $selection.Name $arguments
        Assert-Case ($rejected.ExitCode -ne 0) "Package selection unexpectedly succeeded: $($selection.Name)."
        if ($selection.Name -ne 'version-unavailable-cold') { Assert-Case ($rejected.Output.Contains('Select an exact SDK package version')) 'Missing version did not explain the required selection.' }
    }
    $result = Invoke-OwnedProcess 'build' @('build', (Join-Path $consumer 'OpenVisionLab.FirstInspection.csproj'), '-c', 'Release', "-p:PackageVersion=$ExpectedVersion", '--artifacts-path', $build, '-o', $bin, '--configfile', $config)
    Assert-Case ($result.ExitCode -eq 0) 'Isolated package build failed.'
    $assetsPath = Join-Path $build 'obj/OpenVisionLab.FirstInspection/project.assets.json'
    $assets = Get-Content -LiteralPath $assetsPath -Raw | ConvertFrom-Json -AsHashtable
    $packages = @($assets.libraries.Keys | Where-Object { $_ -like 'OpenVisionLab.*/*' } | Sort-Object)
    $expectedPackages = @('OpenVisionLab.Compute', 'OpenVisionLab.Core', 'OpenVisionLab.Vision2D', 'OpenVisionLab.Vision2D.Blob') | ForEach-Object { "$_/$ExpectedVersion" } | Sort-Object
    Assert-Case (($packages -join '|') -eq ($expectedPackages -join '|')) 'Unexpected package dependency closure/version.'
    $rejected = Invoke-OwnedProcess 'version-unavailable-warm' @('restore', $project, '-p:PackageVersion=0.0.0-selection-missing', '--artifacts-path', $build, '--configfile', $config)
    Assert-Case ($rejected.ExitCode -ne 0) 'A populated cache substituted a different package version.'
    $recovery = Invoke-OwnedProcess 'build-version-recovery' @('build', $project, '-c', 'Release', "-p:PackageVersion=$ExpectedVersion", '--artifacts-path', $build, '-o', $bin, '--configfile', $config)
    Assert-Case ($recovery.ExitCode -eq 0) 'Correct version did not recover after failed restoration.'
    Record-Case 'F00-package-selection' 'Missing/empty version guidance; exact unavailable version rejected with empty/populated cache; valid version dependency closure/build recovery.' @('version-omitted.stdout.log', 'version-empty.stdout.log', 'version-unavailable-cold.stdout.log', 'version-unavailable-warm.stdout.log', 'build-version-recovery.stdout.log')
    if ($PackageSelectionOnly) {
        [ordered]@{ Status = 'Complete'; Scope = 'Package selection/restoration only'; ExpectedPackageVersion = $ExpectedVersion; Packages = $packages; Cases = $cases; Commands = $commands; RunningDllSha256 = (Get-FileHash -LiteralPath $dll).Hash; Unverified = @('Inspection workflow, native execution and first-use interpretation; no desktop window launched') } | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath (Join-Path $reports 'package-selection-summary.json') -Encoding utf8
        Write-Output "Passed package selection/restoration; $($commands.Count) separate processes. Evidence: $reports"
        return
    }
    $scene = Join-Path $root 'scene'
    $bundle = Join-Path $root 'inspection.zip'
    $null = Invoke-Sample 'prepare' @('prepare', $scene) 0 'Prepared'
    $taught = Invoke-Sample 'teach' @('teach', $scene, $bundle) 0 'Taught'
    Assert-Inspection $taught.SelfCheck 0 0
    $goodHash = (Get-FileHash -LiteralPath $bundle -Algorithm SHA256).Hash
    $translated = Invoke-Sample 'inspect-translated' @('inspect', $bundle, (Join-Path $scene 'translated.png')) 0 'Accepted'
    Assert-Inspection $translated 18 11
    Assert-Case (-not $translated.RequestedCuda -and -not $translated.Compute.AccelerationApplied -and $translated.Compute.FallbackReason -eq 'Disabled') 'CPU default changed.'
    Record-Case 'F01' 'Package-only isolated cache/build; explicit prepare/teach/inspect; independent location/mark/analytic edge; CPU default.' @('build.stdout.log', 'teach.stdout.log', 'inspect-translated.stdout.log')

    foreach ($name in @('translated-bgr', 'translated-bgra')) {
        $color = Invoke-Sample "inspect-$name" @('inspect', $bundle, (Join-Path $scene "$name.png")) 0 'Accepted'
        Assert-Inspection $color 18 11
        Assert-Case ($color.InputGray00 -eq 29) 'BGR/BGRA channel conversion differs from the blue pixel contract.'
    }
    foreach ($name in @('empty', 'truncated', 'unsupported-16bit')) { $null = Invoke-Sample "reject-$name" @('inspect', $bundle, (Join-Path $scene "$name.png")) 3 'InvalidInputOrSettings' }
    $null = Invoke-Sample 'reject-missing' @('inspect', $bundle, (Join-Path $scene 'missing.png')) 3 'InvalidInputOrSettings'
    $recovered = Invoke-Sample 'recover-input' @('inspect', $bundle, (Join-Path $scene 'translated.png')) 0 'Accepted'
    Assert-Inspection $recovered 18 11
    Record-Case 'F02' 'Gray/BGR/BGRA and authored blue pixel; empty/truncated/missing/16-bit rejection; valid recovery.' @('inspect-translated-bgr.stdout.log', 'inspect-translated-bgra.stdout.log', 'reject-truncated.stdout.log', 'recover-input.stdout.log')

    foreach ($item in @(@('pattern-absent', 'PatternNotFound', 'MatchingNoResult'), @('mark-absent', 'MarkNotFound', 'BlobNoResult'), @('edge-absent', 'EdgeNotFound', 'LineGaugeEdgeNotFound'))) {
        $absent = Invoke-Sample "inspect-$($item[0])" @('inspect', $bundle, (Join-Path $scene "$($item[0]).png")) 2 $item[1]
        Assert-Case ($absent.Stages[-1].ErrorName -eq $item[2] -and $null -eq $absent.Line) 'Stage error/absent measurement was replaced or stale.'
        if ($item[0] -eq 'pattern-absent') { Assert-Case ($null -eq $absent.Pose -and $null -eq $absent.BlobAreaPx) 'Failed localization published old values.' }
        if ($item[0] -eq 'mark-absent') { Assert-Case ($null -eq $absent.BlobAreaPx) 'Absent mark published a measurement.' }
    }
    Assert-Inspection (Invoke-Sample 'recover-absence' @('inspect', $bundle, (Join-Path $scene 'translated.png')) 0 'Accepted') 18 11
    Record-Case 'F03' 'Pattern/mark/edge absence retains exact SDK errors and unexecuted nulls; normal recovery.' @('inspect-pattern-absent.stdout.log', 'inspect-mark-absent.stdout.log', 'inspect-edge-absent.stdout.log', 'recover-absence.stdout.log')
    $border = Invoke-Sample 'inspect-border' @('inspect', $bundle, (Join-Path $scene 'border.png')) 2 'RoiRejected'
    Assert-Case ($null -eq $border.Line -and $border.Stages[-1].ErrorName -eq 'HostRoiOutsideImage') 'Out-of-image ROI was clipped or measured.'
    Record-Case 'F04' 'Odd taught ROI; floor/ceil transfer; outside image rejected before downstream execution.' @('inspect-translated.stdout.log', 'inspect-border.stdout.log')

    $filesBefore = @(Get-ChildItem -LiteralPath $root -Recurse -File | Where-Object { -not $_.FullName.StartsWith($reports) } | ForEach-Object { $_.FullName + ':' + (Get-FileHash -LiteralPath $_.FullName).Hash })
    $loaded = Invoke-Sample 'load' @('load', $bundle) 0 'LoadedOnly'
    $filesAfter = @(Get-ChildItem -LiteralPath $root -Recurse -File | Where-Object { -not $_.FullName.StartsWith($reports) } | ForEach-Object { $_.FullName + ':' + (Get-FileHash -LiteralPath $_.FullName).Hash })
    Assert-Case (($filesBefore -join '|') -eq ($filesAfter -join '|') -and -not $loaded.ProbePerformed -and -not $loaded.InspectionPerformed) 'Restoration wrote files or started work.'
    $reopened = Invoke-Sample 'inspect-reopened' @('inspect', $bundle, (Join-Path $scene 'translated.png')) 0 'Accepted'
    Assert-Inspection $reopened 18 11
    Assert-Case (($translated.Pose | ConvertTo-Json -Compress) -eq ($reopened.Pose | ConvertTo-Json -Compress) -and ($translated.Line | ConvertTo-Json -Depth 6 -Compress) -eq ($reopened.Line | ConvertTo-Json -Depth 6 -Compress) -and $loaded.ModelSha256 -eq $translated.ModelSha256) 'New process restoration changed values/identity.'
    Record-Case 'F05' 'Teach process exits; separate load has no file effects/inspection/probe; further process matches values and hashes.' @('teach.stdout.log', 'load.stdout.log', 'inspect-reopened.stdout.log')

    $damages = [ordered]@{
        'missing-entry' = { param($e) $e.Remove('model.bin') }
        'wrong-hash' = { param($e) Change-Xml $e { param($x) $x.SelectSingleNode('//Artifact').SetAttribute('sha256', ('0' * 64)) } }
        'truncated-model' = { param($e) $e['model.bin'] = [byte[]]$e['model.bin'][0..($e['model.bin'].Length - 9)] }
        'host-version' = { param($e) Change-Host $e { param($h) $h.Version = 99 } }
        'pipeline-version' = { param($e) Change-Xml $e { param($x) $x.DocumentElement.SetAttribute('schemaVersion', '99') } }
        'artifact-version' = { param($e) Change-Xml $e { param($x) $x.SelectSingleNode('//Artifact').SetAttribute('formatVersion', '99') } }
        'learning-mismatch' = { param($e) Change-Xml $e { param($x) $x.SelectSingleNode("//Step[1]/Parameters/Parameter[Key='CANNY_LOW']/Value").InnerText = '31' } }
        'frame-mismatch' = { param($e) Change-Host $e { param($h) $h.Frame = 'other-frame' } }
        'identity-mismatch' = { param($e) Change-Host $e { param($h) $h.ReferenceImageSha256 = '0' * 64 } }
        'nonfinite-pose' = { param($e) $e['host.json'] = [Text.Encoding]::UTF8.GetBytes(([Text.Encoding]::UTF8.GetString($e['host.json']) -replace '"X":\s*[-0-9.]+', '"X": 1e309')) }
        'invalid-roi' = { param($e) Change-Xml $e { param($x) $x.SelectSingleNode("//Step[2]/Parameters/Parameter[Key='CvROI']/Value").InnerText = '-1,0,31,29' } }
        'unknown-entry' = { param($e) $e['../unexpected'] = [byte[]]@(1) }
        'unsupported-runtime-gate' = { param($e) Change-Xml $e { param($x) $x.SelectSingleNode('//Step[1]/UseAcceptance').InnerText = 'true' } }
        'unsupported-layer-routing' = { param($e) Change-Xml $e { param($x) $x.SelectSingleNode('//Step[1]/InputLayer').InnerText = 'unrouted-input' } }
    }
    foreach ($name in $damages.Keys) {
        $damaged = New-DamagedBundle $name $damages[$name]
        $null = Invoke-Sample "reject-$name" @('load', $damaged) 3 'InvalidInputOrSettings'
    }
    $duplicate = New-DamagedBundle 'duplicate-entry' { param($e) } -Duplicate
    $null = Invoke-Sample 'reject-duplicate-entry' @('load', $duplicate) 3 'InvalidInputOrSettings'
    $truncated = Join-Path $root 'truncated-bundle.zip'
    $bytes = [IO.File]::ReadAllBytes($bundle)
    [IO.File]::WriteAllBytes($truncated, [byte[]]$bytes[0..31])
    $null = Invoke-Sample 'reject-truncated-bundle' @('load', $truncated) 3 'InvalidInputOrSettings'
    $null = Invoke-Sample 'reload-good' @('load', $bundle) 0 'LoadedOnly'
    Assert-Inspection (Invoke-Sample 'recover-bundle' @('inspect', $bundle, (Join-Path $scene 'translated.png')) 0 'Accepted') 18 11
    Assert-Case ((Get-FileHash -LiteralPath $bundle).Hash -eq $goodHash) 'Damage tests modified the good bundle.'
    Record-Case 'F06' 'Sixteen isolated malformed/unsupported bundles: entries/hash/truncation/versions/learning/frame/identity/nonfinite/ROI/runtime gate/layer routing; last good hash preserved and normal restore/inspection recovery.' @('reject-learning-mismatch.stdout.log', 'reject-nonfinite-pose.stdout.log', 'reject-truncated-bundle.stdout.log', 'reject-unsupported-runtime-gate.stdout.log', 'reject-unsupported-layer-routing.stdout.log', 'reload-good.stdout.log', 'recover-bundle.stdout.log')

    $null = Invoke-Sample 'reject-existing-destination' @('teach', $scene, $bundle) 3 'InvalidInputOrSettings'
    $parentFile = Join-Path $root 'parent-is-file'
    [IO.File]::WriteAllText($parentFile, 'Owned failure fixture')
    $null = Invoke-Sample 'reject-save-parent' @('teach', $scene, (Join-Path $parentFile 'inspection.zip')) 3 'InvalidInputOrSettings'
    $cancelBundle = Join-Path $root 'canceled.zip'
    $null = Invoke-Sample 'cancel-teach' @('teach', $scene, $cancelBundle, '--cancel-before') 130 'Canceled'
    # Exceeding the Windows filename-component limit fails finalization after the valid
    # temporary ZIP has been written/reopened. The owned temporary file must still be removed.
    $watcher = [IO.FileSystemWatcher]::new($root, '.first-inspection-*.tmp')
    $watchId = 'first-inspection-' + [Guid]::NewGuid().ToString('N')
    $null = Register-ObjectEvent -InputObject $watcher -EventName Created -SourceIdentifier $watchId
    $watcher.EnableRaisingEvents = $true
    try {
        $tooLong = Join-Path $root (('x' * 260) + '.zip')
        $null = Invoke-Sample 'reject-save-finalize' @('teach', $scene, $tooLong) 3 'InvalidInputOrSettings'
        $created = @(Get-Event -SourceIdentifier $watchId -ErrorAction SilentlyContinue)
        Assert-Case ($created.Count -gt 0) 'Finalization fault did not reach temporary bundle creation.'
        $created | ForEach-Object { $_.SourceEventArgs.FullPath } | Set-Content -LiteralPath (Join-Path $reports 'save-temporary-created.log') -Encoding utf8
    }
    finally {
        $watcher.EnableRaisingEvents = $false
        Unregister-Event -SourceIdentifier $watchId
        Get-Event -SourceIdentifier $watchId -ErrorAction SilentlyContinue | Remove-Event
        $watcher.Dispose()
    }
    Assert-Case (-not (Test-Path -LiteralPath $cancelBundle) -and (Get-FileHash -LiteralPath $bundle).Hash -eq $goodHash -and @(Get-ChildItem -LiteralPath $root -Filter '.first-inspection-*.tmp').Count -eq 0) 'Failed/canceled save changed a bundle or leaked temporary output.'
    $null = Invoke-Sample 'recover-save' @('teach', $scene, (Join-Path $root 'recovered.zip')) 0 'Taught'
    $null = Invoke-Sample 'load-recovered-save' @('load', (Join-Path $root 'recovered.zip')) 0 'LoadedOnly'
    Assert-Inspection (Invoke-Sample 'inspect-recovered-save' @('inspect', (Join-Path $root 'recovered.zip'), (Join-Path $scene 'translated.png')) 0 'Accepted') 18 11
    Record-Case 'F07' 'Existing destination, file as parent, pre-canceled teach and finalization fault after observed temp creation fail without overwriting; no owned temp leak; new-name teach/load/inspect recovery. Active console cancel during teach is a separate check.' @('reject-existing-destination.stdout.log', 'reject-save-parent.stdout.log', 'reject-save-finalize.stdout.log', 'save-temporary-created.log', 'cancel-teach.stdout.log', 'recover-save.stdout.log', 'inspect-recovered-save.stdout.log')

    $missingCuda = Invoke-Sample 'cuda-missing' @('inspect', $bundle, (Join-Path $scene 'translated.png'), '--cuda') 0 'Accepted'
    Assert-Inspection $missingCuda 18 11
    Assert-Case ($missingCuda.RequestedCuda -and -not $missingCuda.Compute.AccelerationApplied -and $missingCuda.Compute.FallbackReason -eq 'NativeModuleMissing') 'Missing CUDA did not explain CPU fallback.'
    $native = Join-Path $bin 'OpenVisionLab.Cuda.Native.dll'
    try {
        [IO.File]::WriteAllBytes($native, [Text.Encoding]::ASCII.GetBytes('Owned incompatible module fixture'))
        $invalidCuda = Invoke-Sample 'cuda-invalid' @('inspect', $bundle, (Join-Path $scene 'translated.png'), '--cuda') 0 'Accepted'
        Assert-Inspection $invalidCuda 18 11
        Assert-Case (-not $invalidCuda.Compute.AccelerationApplied -and $invalidCuda.Compute.FallbackReason -eq 'NativeModuleInvalidImage') 'Incompatible native module did not fall back honestly.'
    }
    finally { if (Test-Path -LiteralPath $native) { Remove-Item -LiteralPath $native } }
    $cudaEvidence = $null
    if ($CudaModule) {
        $sourceModule = (Resolve-Path -LiteralPath $CudaModule).Path
        Copy-Item -LiteralPath $sourceModule -Destination $native
        try {
            $actualCuda = Invoke-Sample 'cuda-optional' @('inspect', $bundle, (Join-Path $scene 'translated.png'), '--cuda') 0 'Accepted'
            Assert-Inspection $actualCuda 18 11
            $cudaEvidence = [ordered]@{ SourceModule = $sourceModule; Sha256 = (Get-FileHash -LiteralPath $sourceModule).Hash; Actual = $actualCuda.Compute }
            Assert-Case ((Get-FileHash -LiteralPath $sourceModule).Hash -eq (Get-FileHash -LiteralPath $native).Hash) 'Copied optional module differs.'
            # Discovery or a requested option is never counted as applied kernel execution.
            if ($actualCuda.Compute.AccelerationApplied) { Assert-Case ($actualCuda.Compute.GpuBatchCount -gt 0) 'Applied CUDA has no executed batch.' }
        }
        finally { Remove-Item -LiteralPath $native }
    }
    $cpuRecovery = Invoke-Sample 'recover-cpu' @('inspect', $bundle, (Join-Path $scene 'translated.png')) 0 'Accepted'
    Assert-Inspection $cpuRecovery 18 11
    Assert-Case (-not $cpuRecovery.Compute.AccelerationApplied) 'CPU retry retained GPU state.'
    $savedCuda = Join-Path $root 'cuda-policy.zip'
    $null = Invoke-Sample 'teach-cuda-policy' @('teach', $scene, $savedCuda, '--cuda') 0 'Taught'
    $loadedCuda = Invoke-Sample 'load-cuda-policy' @('load', $savedCuda) 0 'LoadedOnly'
    Assert-Case ($loadedCuda.Host.UseCuda -and -not $loadedCuda.ProbePerformed -and -not $loadedCuda.InspectionPerformed) 'Restored preference was hidden or started work.'
    $policyRun = Invoke-Sample 'inspect-cuda-policy' @('inspect', $savedCuda, (Join-Path $scene 'translated.png')) 0 'Accepted'
    Assert-Case ($policyRun.RequestedCuda -and -not $policyRun.Compute.AccelerationApplied) 'Persisted explicit CUDA policy was not used.'
    Record-Case 'F08' 'CPU default; missing/invalid module fallback; optional exact module evidence when supplied; run override does not alter saved bundle; explicit taught CUDA preference restores without executing; CPU retry.' @('cuda-missing.stdout.log', 'cuda-invalid.stdout.log', 'recover-cpu.stdout.log', 'load-cuda-policy.stdout.log', 'inspect-cuda-policy.stdout.log')

    $null = Invoke-Sample 'cancel-inspect-before' @('inspect', $bundle, (Join-Path $scene 'translated.png'), '--cancel-before') 130 'Canceled'
    $null = Invoke-Sample 'cancel-inspect-active' @('inspect', $bundle, (Join-Path $scene 'translated.png'), '--cancel-after-ms', '1') 130 'Canceled'
    Assert-Case ((Get-Content -LiteralPath (Join-Path $reports 'cancel-inspect-active.stderr.log') -Raw).Contains('MatchingStarted')) 'Cancellation did not enter the public execution path.'
    Assert-Inspection (Invoke-Sample 'recover-cancel' @('inspect', $bundle, (Join-Path $scene 'translated.png')) 0 'Accepted') 18 11
    Assert-Case ((Get-FileHash -LiteralPath $bundle).Hash -eq $goodHash) 'Run/cancel mutated saved settings.'
    Record-Case 'F09-managed-token' 'Pre-cancellation and timed cooperative cancellation at public Execute; no values published; fresh process recovers. Actual Console.CancelKeyPress control event is a separate check.' @('cancel-inspect-before.stdout.log', 'cancel-inspect-active.stdout.log', 'cancel-inspect-active.stderr.log', 'recover-cancel.stdout.log')

    if ($QrFixtureDirectory) {
        $qrData = (Resolve-Path -LiteralPath $QrFixtureDirectory).Path
        $qrProfile = Join-Path $root 'qr.xml'
        $configured = Invoke-Sample 'qr-configure' @('qr', 'configure', $qrProfile) 0 'ConfiguredOnly'
        Assert-Case (-not $configured.InspectionPerformed -and -not $configured.ProbePerformed -and $configured.Settings.ExpectedCodeCount -eq '1') 'QR configure ran inspection or lost visible defaults.'
        $profileHash = (Get-FileHash -LiteralPath $qrProfile).Hash
        $qrLoaded = Invoke-Sample 'qr-load-new-process' @('qr', 'load', $qrProfile) 0 'LoadedOnly'
        Assert-Case (-not $qrLoaded.InspectionPerformed -and -not $qrLoaded.ProbePerformed -and $qrLoaded.ProfileSha256 -eq $profileHash) 'QR load ran inspection or changed the confirmed profile.'
        foreach ($item in @(@('ascii', 'OVL-PART-A-20261006'), @('utf8', '오픈비전-부품A'), @('spaces', ' PART-A '), @('only-spaces', '   '))) {
            $read = Invoke-Sample "qr-read-$($item[0])" @('qr', 'inspect', $qrProfile, (Join-Path $qrData "$($item[0]).pgm"), $item[1]) 0 'TextMatched'
            Assert-Case ($read.Success -and $read.TextAgrees -and $read.Reading.DetectedCount -eq 1 -and $read.Reading.DecodedCount -eq 1 -and $read.Reading.Candidates[0].Text -ceq $item[1] -and $read.Reading.Candidates[0].Corners.Count -eq 4) 'QR original text/count/corners differ.'
        }
        $wrongPart = Invoke-Sample 'qr-host-text-mismatch' @('qr', 'inspect', $qrProfile, (Join-Path $qrData 'a.pgm'), 'OTHER-PART') 2 'TextMismatch'
        Assert-Case ($wrongPart.Success -and -not $wrongPart.TextAgrees -and $wrongPart.ErrorName -eq 'None') 'Host comparison corrupted the Tool success/error contract.'
        $blank = Invoke-Sample 'qr-blank' @('qr', 'inspect', $qrProfile, (Join-Path $qrData 'blank.pgm')) 2 'ReadingFailed'
        Assert-Case ($blank.ErrorName -eq 'QrCodeNotDetected' -and $blank.Reading.DetectedCount -eq 0) 'QR blank input retained stale evidence.'
        $two = Invoke-Sample 'qr-count-mismatch' @('qr', 'inspect', $qrProfile, (Join-Path $qrData 'two.pgm')) 2 'ReadingFailed'
        Assert-Case ($two.ErrorName -eq 'QrCodeCountMismatch' -and $two.Reading.DetectedCount -eq 2 -and $two.Reading.DecodedCount -eq 2) 'QR count mismatch lost partial evidence.'
        $twoProfile = Join-Path $root 'qr-two.xml'
        $null = Invoke-Sample 'qr-configure-two' @('qr', 'configure', $twoProfile, '2') 0 'ConfiguredOnly'
        $twoRead = Invoke-Sample 'qr-read-two' @('qr', 'inspect', $twoProfile, (Join-Path $qrData 'two.pgm')) 0 'Read'
        Assert-Case (($twoRead.Reading.Candidates.Text | Sort-Object) -join ',' -ceq 'PART-A,PART-B') 'QR multi-code text differs.'
        $partial = Invoke-Sample 'qr-partial-failure' @('qr', 'inspect', $twoProfile, (Join-Path $qrData 'partial-trial-7.pgm')) 2 'ReadingFailed'
        Assert-Case ($partial.ErrorName -eq 'QrCodeDecodeFailed' -and $partial.Reading.DetectedCount -eq 2 -and $partial.Reading.DecodedCount -eq 1) 'QR partial decoding was accepted or evidence lost.'
        $null = Invoke-Sample 'qr-read-two-recovery' @('qr', 'inspect', $twoProfile, (Join-Path $qrData 'two.pgm')) 0 'Read'
        Record-Case 'Q01-text-count-host' 'Fresh processes preserve original ASCII/UTF8/whitespace; visible settings restore without execution; tool success remains separate from ordinal part comparison; blank/count/partial failures and recovery.' @('qr-load-new-process.stdout.log', 'qr-read-utf8.stdout.log', 'qr-host-text-mismatch.stdout.log', 'qr-partial-failure.stdout.log', 'qr-read-two-recovery.stdout.log')

        $null = Invoke-Sample 'qr-reject-existing-profile' @('qr', 'configure', $qrProfile) 3 'InvalidInputOrSettings'
        $null = Invoke-Sample 'qr-reject-save-parent' @('qr', 'configure', (Join-Path $parentFile 'qr.xml')) 3 'InvalidInputOrSettings'
        $null = Invoke-Sample 'qr-reject-save-finalize' @('qr', 'configure', (Join-Path $root (('q' * 260) + '.xml'))) 3 'InvalidInputOrSettings'
        $damagedProfile = Join-Path $root 'qr-damaged.xml'
        [IO.File]::WriteAllText($damagedProfile, '<VisionPipeline')
        $null = Invoke-Sample 'qr-reject-truncated-profile' @('qr', 'load', $damagedProfile) 3 'InvalidInputOrSettings'
        [xml] $qrXml = Get-Content -LiteralPath $qrProfile -Raw
        $qrXml.SelectSingleNode('//Parameter[Key="ExpectedCodeCount"]/Key').InnerText = 'unknown-setting'
        $qrXml.Save($damagedProfile)
        $null = Invoke-Sample 'qr-reject-unknown-setting' @('qr', 'load', $damagedProfile) 3 'InvalidInputOrSettings'
        foreach ($name in @('empty', 'truncated', 'unsupported-16bit', 'translated-bgra')) {
            $null = Invoke-Sample "qr-reject-input-$name" @('qr', 'inspect', $qrProfile, (Join-Path $scene "$name.png")) 3 'InvalidInputOrSettings'
        }
        $null = Invoke-Sample 'qr-cancel-before' @('qr', 'inspect', $qrProfile, (Join-Path $qrData 'a.pgm'), '--cancel-before') 130 'Canceled'
        $null = Invoke-Sample 'qr-reload-good' @('qr', 'load', $qrProfile) 0 'LoadedOnly'
        $null = Invoke-Sample 'qr-recover-input-profile-cancel' @('qr', 'inspect', $qrProfile, (Join-Path $qrData 'a.pgm'), 'PART-A') 0 'TextMatched'
        Assert-Case ((Get-FileHash -LiteralPath $qrProfile).Hash -eq $profileHash -and @(Get-ChildItem -LiteralPath $root -Filter '.qr-profile-*.tmp').Count -eq 0) 'QR failures overwrote confirmed settings or leaked temporary files.'
        Record-Case 'Q02-save-restore-recovery' 'Existing/file-parent/finalization write failure; truncated/unknown profile; unsupported/empty/truncated image; pre-cancel; original confirmed hash and no temporary leak; new-process recovery.' @('qr-reject-save-finalize.stdout.log', 'qr-reject-truncated-profile.stdout.log', 'qr-reject-unknown-setting.stdout.log', 'qr-recover-input-profile-cancel.stdout.log')

        $locatedProfile = Join-Path $root 'qr-located.xml'
        $null = Invoke-Sample 'qr-configure-located' @('qr', 'configure', $locatedProfile, '1', '180,50,232,232') 0 'ConfiguredOnly'
        $locatedHash = (Get-FileHash -LiteralPath $locatedProfile).Hash
        foreach ($name in @('reference', 'translated')) {
            $locatedRead = Invoke-Sample "qr-located-$name" @('qr', 'inspect-located', $locatedProfile, $bundle, (Join-Path $qrData "located-$name.png"), 'PART-A') 0 'TextMatched'
            $dx = if ($name -eq 'translated') { 18 } else { 0 }
            $dy = if ($name -eq 'translated') { 11 } else { 0 }
            Assert-Case ($locatedRead.QrExecutionPerformed -and $locatedRead.Reading.Candidates[0].Text -ceq 'PART-A' -and $locatedRead.CoordinateFrame -eq 'current-input-Mat') 'Located QR did not execute/preserve original text/current frame.'
            Assert-Case ([Math]::Abs($locatedRead.Location.CurrentPose.X - (112 + $dx)) -le 1 -and [Math]::Abs($locatedRead.Location.CurrentPose.Y - (92 + $dy)) -le 1) 'Located QR matching disagrees with authored datum.'
            Assert-Case (@($locatedRead.Reading.Candidates[0].Corners | Where-Object { $_.X -lt (180 + $dx) -or $_.X -ge (412 + $dx) -or $_.Y -lt (50 + $dy) -or $_.Y -ge (282 + $dy) }).Count -eq 0) 'Located QR corners left the authored current code region.'
        }
        $locationFailed = Invoke-Sample 'qr-located-matching-failure' @('qr', 'inspect-located', $locatedProfile, $bundle, (Join-Path $qrData 'located-absent.png')) 2 'LocationFailed'
        $roiFailed = Invoke-Sample 'qr-located-roi-rejection' @('qr', 'inspect-located', $locatedProfile, $bundle, (Join-Path $qrData 'located-border.png')) 2 'RoiRejected'
        Assert-Case (-not $locationFailed.QrExecutionPerformed -and -not $roiFailed.QrExecutionPerformed -and $null -eq $locationFailed.QrCodeReading -and $null -eq $roiFailed.QrCodeReading) 'Failed matching or ROI transfer still invoked QR.'
        $null = Invoke-Sample 'qr-located-recovery' @('qr', 'inspect-located', $locatedProfile, $bundle, (Join-Path $qrData 'located-translated.png'), 'PART-A') 0 'TextMatched'
        Assert-Case ((Get-FileHash -LiteralPath $locatedProfile).Hash -eq $locatedHash) 'Located execution mutated saved reference ROI.'
        Record-Case 'Q03-matching-roi' 'Existing matching/bundle datum moves explicit reference ROI; current-input corners; matching failure/out-of-frame ROI suppress reader; saved ROI and valid recovery.' @('qr-located-translated.stdout.log', 'qr-located-matching-failure.stdout.log', 'qr-located-roi-rejection.stdout.log', 'qr-located-recovery.stdout.log')
    }

    $feedManifest = Get-ChildItem -LiteralPath $feed -Filter '*.nupkg' | Sort-Object Name | ForEach-Object { [ordered]@{ File = $_.Name; Sha256 = (Get-FileHash -LiteralPath $_.FullName).Hash } }
    $summary = [ordered]@{ Status = 'Complete'; Scope = $(if ($QrFixtureDirectory) { 'Isolated package consumer, bounded F01-F09 managed cases and Q01-Q03 QR workflow' } else { 'Isolated package consumer and bounded F01-F09 managed cases' }); ExpectedPackageVersion = $ExpectedVersion; Packages = $packages; Feed = $feedManifest; RunningDllSha256 = (Get-FileHash -LiteralPath $dll).Hash; LoadedPackages = $translated.Packages; ConsoleMode = 'redirected pipes; no desktop window'; Storage = $root; Cases = $cases; Commands = $commands; OptionalCuda = $cudaEvidence; Unverified = @('Actual first-developer observation F10', 'Console.CancelKeyPress requires separate actual control-event evidence', 'Sensor images, rotation/scale, physical calibration/tolerances, field soak/performance'); }
    $summary | ConvertTo-Json -Depth 15 | Set-Content -LiteralPath (Join-Path $reports 'summary.json') -Encoding utf8
    Write-Output "Passed $($cases.Count) bounded case groups; $($commands.Count) separate processes. Evidence: $reports"
}
catch {
    [ordered]@{ Status = 'Incomplete'; Failure = $_.Exception.Message; Cases = $cases; Commands = $commands } | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath (Join-Path $reports 'failure.json') -Encoding utf8
    throw
}
