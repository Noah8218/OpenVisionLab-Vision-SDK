param(
    [Parameter(Mandatory = $true)][string]$AssemblyDirectory,
    [Parameter(Mandatory = $true)][string]$EvidenceDirectory,
    [Parameter(Mandatory = $true)][string]$PackageProof,
    [string]$CudaModule,
    [string]$NativeFixtures
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if ($PSVersionTable.PSVersion.Major -lt 7) { throw 'Use PowerShell 7 for ArgumentList and owned-process control.' }
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$physicalRoot = 'D:\OpenVisionLab-TestData\OpenVisionLab-Vision-SDK\'
$AssemblyDirectory = [IO.Path]::GetFullPath($AssemblyDirectory)
$EvidenceDirectory = [IO.Path]::GetFullPath($EvidenceDirectory)
foreach ($directory in @($AssemblyDirectory, $EvidenceDirectory)) {
    if (!$directory.StartsWith($physicalRoot, [StringComparison]::OrdinalIgnoreCase)) { throw 'Binaries and evidence must use the project D: root.' }
}
$binary = Join-Path $AssemblyDirectory 'OpenVisionLab.Inspection.Smoke.dll'
if (!(Test-Path -LiteralPath $binary)) { throw 'Build the focused Smoke project into D: first.' }
if (Test-Path -LiteralPath (Join-Path $EvidenceDirectory 'reports\summary.json')) { throw 'Completed evidence is immutable; choose a fresh directory.' }
foreach ($part in @('reports', 'logs', 'inputs', 'temp')) { [void][IO.Directory]::CreateDirectory((Join-Path $EvidenceDirectory $part)) }
$reports = Join-Path $EvidenceDirectory 'reports'
$logs = Join-Path $EvidenceDirectory 'logs'
$inputs = Join-Path $EvidenceDirectory 'inputs'
$dotnet = (Get-Command dotnet).Source
$utf8 = [Text.UTF8Encoding]::new($false)
$commands = [Collections.Generic.List[object]]::new()
$cases = [Collections.Generic.List[object]]::new()

function Write-Json([string]$Path, $Value) {
    [IO.File]::WriteAllText($Path, ($Value | ConvertTo-Json -Depth 40), $utf8)
}

function File-Identity([string]$Path) {
    $file = Get-Item -LiteralPath $Path
    @{ Path = $file.FullName; Length = $file.Length; Sha256 = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash }
}

function Require([bool]$Condition, [string]$Message) {
    if (!$Condition) { throw $Message }
}

function Invoke-OwnedProcess([string]$Name, [string]$File, [string[]]$Arguments, [int]$ExpectedExit = 0, [string]$WorkingDirectory = $AssemblyDirectory, [hashtable]$Environment = @{}) {
    $start = [Diagnostics.ProcessStartInfo]::new($File)
    foreach ($argument in $Arguments) { $start.ArgumentList.Add($argument) }
    $start.WorkingDirectory = $WorkingDirectory
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    $start.Environment['TEMP'] = Join-Path $EvidenceDirectory 'temp'
    $start.Environment['TMP'] = $start.Environment['TEMP']
    $start.Environment['OVL_CUDA_TEST_MODULE'] = if ($CudaModule) { $CudaModule } else { '' }
    $start.Environment['OVL_CUDA_EXECUTION_FIXTURES'] = if ($NativeFixtures) { $NativeFixtures } else { '' }
    foreach ($key in $Environment.Keys) { $start.Environment[$key] = $Environment[$key] }
    $process = [Diagnostics.Process]::new()
    $process.StartInfo = $start
    $clock = [Diagnostics.Stopwatch]::StartNew()
    $ready = $null
    try {
        [void]$process.Start()
        $errors = $process.StandardError.ReadToEndAsync()
        $prefix = ''
        if ($Arguments.Count -gt 1 -and $Arguments[1] -eq '--operation-study') {
            $line = $process.StandardOutput.ReadLineAsync()
            Require ($line.Wait(20000)) "$Name did not reach its startup marker."
            $prefix = $line.Result + [Environment]::NewLine
            Require ($line.Result -eq 'READY | local operation cost') "$Name returned an unexpected startup marker."
            $ready = $clock.Elapsed.TotalMilliseconds
        }
        $output = $process.StandardOutput.ReadToEndAsync()
        $guard = if ($Arguments.Count -gt 0 -and $Arguments[0] -eq 'build') { 180000 } else { 60000 }
        Require ($process.WaitForExit($guard)) "$Name exceeded the test guard; this is not an SDK timeout contract."
        $clock.Stop()
        $stdout = $prefix + $output.GetAwaiter().GetResult()
        $stderr = $errors.GetAwaiter().GetResult()
        [IO.File]::WriteAllText((Join-Path $logs "$Name.stdout.log"), $stdout, $utf8)
        [IO.File]::WriteAllText((Join-Path $logs "$Name.stderr.log"), $stderr, $utf8)
        $record = @{ Name = $Name; File = $File; Arguments = $Arguments; WorkingDirectory = $WorkingDirectory; EnvironmentOverrides = $Environment; Exit = $process.ExitCode; ExpectedExit = $ExpectedExit; ProcessMilliseconds = $clock.Elapsed.TotalMilliseconds; StartupReadyMilliseconds = $ready }
        $commands.Add($record)
        Write-Json (Join-Path $reports 'commands.json') $commands.ToArray()
        Require ($process.ExitCode -eq $ExpectedExit) "$Name exit $($process.ExitCode), expected $ExpectedExit; see owned logs."
        Write-Host "PASS | $Name | exit $ExpectedExit"
        return $stdout
    }
    finally {
        if ($process.Id -and !$process.HasExited) { $process.Kill($true); [void]$process.WaitForExit(10000) }
        $process.Dispose()
    }
}

function Host-Case([string]$Name, [string]$Action, [int]$Exit, [string]$ExpectedStatus, [string]$Directory = $inputs, [string]$BinaryDirectory = $AssemblyDirectory, [hashtable]$Environment = @{}) {
    $result = Invoke-OwnedProcess $Name $dotnet @((Join-Path $BinaryDirectory 'OpenVisionLab.Inspection.Smoke.dll'), '--operation-host', $Action, $Directory) $Exit $BinaryDirectory $Environment
    $report = $result | ConvertFrom-Json
    Require ($report.Status -eq $ExpectedStatus) "$Name status differs from the authored case."
    $cases.Add(@{ Case = $Name; Action = $Action; ExpectedStatus = $ExpectedStatus; ExpectedExit = $Exit; Actual = $report; Status = 'Passed' })
    Write-Json (Join-Path $reports 'process-cases.json') $cases.ToArray()
    return $report
}

function Interrupt-OwnedHost([string]$Action, [string]$ExpectedStatus) {
    $start = [Diagnostics.ProcessStartInfo]::new($dotnet)
    foreach ($argument in @($binary, '--operation-host', $Action, $inputs)) { $start.ArgumentList.Add($argument) }
    $start.WorkingDirectory = $AssemblyDirectory
    $start.UseShellExecute = $false
    $start.CreateNoWindow = $true
    $start.RedirectStandardInput = $true
    $start.RedirectStandardOutput = $true
    $start.RedirectStandardError = $true
    $start.Environment['TEMP'] = Join-Path $EvidenceDirectory 'temp'
    $start.Environment['TMP'] = $start.Environment['TEMP']
    $process = [Diagnostics.Process]::new()
    $process.StartInfo = $start
    try {
        [void]$process.Start()
        $errors = $process.StandardError.ReadToEndAsync()
        $line = $process.StandardOutput.ReadLineAsync()
        Require ($line.Wait(20000)) 'Owned child did not reach the selected storage boundary.'
        $boundary = $line.Result | ConvertFrom-Json
        Require ($boundary.Status -eq $ExpectedStatus) 'Owned child storage boundary differs.'
        $process.Kill($true)
        Require ($process.WaitForExit(10000)) 'Owned child did not terminate.'
        [IO.File]::WriteAllText((Join-Path $logs "$Action.stdout.log"), $line.Result, $utf8)
        [IO.File]::WriteAllText((Join-Path $logs "$Action.stderr.log"), $errors.GetAwaiter().GetResult(), $utf8)
        $temporary = [IO.Path]::GetFullPath($boundary.Temporary)
        Require ($temporary.StartsWith($inputs + '\', [StringComparison]::OrdinalIgnoreCase)) 'Unexpected owned temporary path.'
        $residual = if (Test-Path -LiteralPath $temporary) { File-Identity $temporary } else { $null }
        $cases.Add(@{ Case = $Action; Status = 'Passed'; Boundary = $boundary; ActualKilledExit = $process.ExitCode; Residual = $residual; Meaning = 'Owned test Host interrupted after an explicit handshake; finally on forced exit is not promised.' })
        Write-Json (Join-Path $reports 'process-cases.json') $cases.ToArray()
        return $boundary
    }
    finally {
        if ($process.Id -and !$process.HasExited) { $process.Kill($true); [void]$process.WaitForExit(10000) }
        $process.Dispose()
    }
}

$os = Get-CimInstance Win32_OperatingSystem
$monitors = @()
try {
    Add-Type -AssemblyName System.Windows.Forms
    $monitors = @([Windows.Forms.Screen]::AllScreens | ForEach-Object { @{ Name = $_.DeviceName; Primary = $_.Primary; Bounds = $_.Bounds.ToString(); WorkingArea = $_.WorkingArea.ToString() } })
}
catch { $monitors = @(@{ Fallback = 'Topology unavailable; redirected nonvisual console tests do not launch desktop windows.' }) }
Write-Json (Join-Path $reports 'environment.json') @{
    ObservedAt = [DateTimeOffset]::UtcNow.ToString('o'); SourceCommit = (& git -C $repoRoot rev-parse HEAD); Remote = @(& git -C $repoRoot remote -v);
    OS = @{ Caption = $os.Caption; Version = $os.Version; Architecture = $os.OSArchitecture; PhysicalMemoryKiB = $os.TotalVisibleMemorySize; AvailableMemoryKiB = $os.FreePhysicalMemory };
    CPU = @(Get-CimInstance Win32_Processor | Select-Object Name, NumberOfCores, NumberOfLogicalProcessors);
    GPU = @(Get-CimInstance Win32_VideoController | Select-Object Name, DriverVersion);
    Dotnet = @(& $dotnet --info); Monitors = $monitors; WindowMode = 'dotnet DLL child processes with redirected streams and CreateNoWindow=true';
    TEMP = (Join-Path $EvidenceDirectory 'temp'); TMP = (Join-Path $EvidenceDirectory 'temp');
    Selection = @{ Repeats = 6; IndependentWorkers = 2; Reason = 'Bounded first/warm and independent state-transition selection, not an approved operating capacity.' };
    Binaries = @(Get-ChildItem -LiteralPath $AssemblyDirectory -Filter '*.dll' | ForEach-Object { File-Identity $_.FullName });
    OperationSource = @('eng/Test-SdkOperation.ps1', 'tests/OpenVisionLab.Inspection.Smoke/Program.cs', 'tests/OpenVisionLab.Inspection.Smoke/Suites/SdkOperationSmokeSuite.cs', 'tests/OpenVisionLab.Inspection.Smoke/Suites/SdkOperationEvaluationSmokeSuite.cs' | ForEach-Object { File-Identity (Join-Path $repoRoot $_) });
    CudaModule = if ($CudaModule) { File-Identity $CudaModule } else { $null };
    NativeFixtures = if ($NativeFixtures) { @('execution-3.dll','execution-5.dll' | ForEach-Object { File-Identity (Join-Path $NativeFixtures $_) }) } else { @() }
}

[void](Invoke-OwnedProcess 'operation-smoke' $dotnet @($binary, '--filter', 'SDK operation', '--report', (Join-Path $reports 'smoke.json')))
[void](Host-Case 'prepare' 'prepare' 0 'Prepared')
$profileHash = (File-Identity (Join-Path $inputs 'profile.xml')).Sha256
$loaded = Host-Case 'normal-restart-load' 'load' 0 'LoadedOnly'
Require (!$loaded.InspectionPerformed) 'A fresh process restore must not inspect.'
[void](Host-Case 'explicit-a' 'inspect-a' 0 'Completed')
[void](Host-Case 'blank-reject' 'inspect-blank' 2 'Rejected')
[void](Host-Case 'damaged-image-reject' 'inspect-corrupt' 3 'InvalidInputOrSettings')
[void](Host-Case 'partial-reject' 'inspect-partial' 2 'Rejected')
[void](Host-Case 'after-failures-b' 'inspect-b' 0 'Completed')
[void](Host-Case 'save-fault' 'save-existing' 3 'InvalidInputOrSettings')
Require ((File-Identity (Join-Path $inputs 'profile.xml')).Sha256 -eq $profileHash) 'Save failure changed committed profile bytes.'
$boundary = Interrupt-OwnedHost 'stage' 'Staged'
$corruptDirectory = Join-Path $EvidenceDirectory 'corrupt-profile'
[void][IO.Directory]::CreateDirectory($corruptDirectory)
Copy-Item -LiteralPath $boundary.Temporary -Destination (Join-Path $corruptDirectory 'profile.xml')
[void](Host-Case 'interrupted-profile-reject' 'load' 3 'InvalidInputOrSettings' $corruptDirectory)
Require ((File-Identity (Join-Path $inputs 'profile.xml')).Sha256 -eq $profileHash) 'Interrupted temp write changed committed bytes.'
Remove-Item -LiteralPath $boundary.Temporary
[void](Interrupt-OwnedHost 'commit-stage' 'Committed')
Require ((File-Identity (Join-Path $inputs 'committed.xml')).Sha256 -eq $profileHash) 'Commit boundary did not retain complete profile bytes.'
$reloaded = Host-Case 'after-interruption-load' 'load' 0 'LoadedOnly'
Require (!$reloaded.InspectionPerformed) 'Restart after interruption must remain explicit.'
[void](Host-Case 'after-interruption-b' 'inspect-b' 0 'Completed')

foreach ($kind in @('missing', 'invalid')) {
    $copy = Join-Path $EvidenceDirectory "native-$kind"
    [void][IO.Directory]::CreateDirectory($copy)
    Get-ChildItem -LiteralPath $AssemblyDirectory -File | ForEach-Object { Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $copy $_.Name) }
    $native = Join-Path $copy 'OpenCvSharpExtern.dll'
    if ($kind -eq 'missing') { Remove-Item -LiteralPath $native }
    else { [IO.File]::WriteAllBytes($native, [byte[]](0,1,2,3)) }
    [void](Host-Case "mandatory-native-$kind" 'inspect-a' 3 'BackendUnavailable' $inputs $copy)
    [void](Host-Case "mandatory-native-$kind-recovery" 'inspect-b' 0 'Completed')
    $optionalPath = Join-Path $EvidenceDirectory "optional-$kind.dll"
    if ($kind -eq 'invalid') { [IO.File]::WriteAllBytes($optionalPath, [byte[]](0,1,2,3)) }
    $optional = Host-Case "optional-native-$kind" 'edge' 0 'Completed' $inputs $AssemblyDirectory @{ OVL_CUDA_TEST_MODULE = $optionalPath }
    $expected = if ($kind -eq 'missing') { 'NativeModuleMissing' } else { 'NativeModuleInvalidImage' }
    Require (!$optional.Diagnostics.AccelerationApplied -and $optional.Diagnostics.FallbackReason -eq $expected) 'Optional CUDA failure must preserve the precise fallback and correct CPU result.'
}
if ($CudaModule) {
    $restored = Host-Case 'optional-native-recovery' 'edge' 0 'Completed'
    Require ($restored.Diagnostics.AccelerationApplied) 'The restored study module did not actually accelerate on this PC.'
}

$scenarios = @('qr','qr-blank','qr-partial','qr-cancel','edge-cpu','edge3-cpu')
if ($CudaModule) { $scenarios += @('edge-cuda','edge3-cuda') }
foreach ($scenario in $scenarios) {
    $measurement = Join-Path $reports "cost-$scenario.json"
    [void](Invoke-OwnedProcess "cost-$scenario" $dotnet @($binary, '--operation-study', $scenario, $inputs, $measurement))
    $cost = Get-Content -LiteralPath $measurement -Raw | ConvertFrom-Json
    Require ($cost.Status -eq 'Passed' -and $cost.Samples.Count -eq 6 -and $cost.RetainedSnapshotsReleased) 'Cost/resource result is incomplete.'
    if ($scenario.EndsWith('-cuda')) { Require (@($cost.Samples | Where-Object { @($_.Diagnostics | Where-Object AccelerationApplied).Count -gt 0 }).Count -eq 6) 'A CUDA study silently ran CPU.' }
}

$proof = Get-Content -LiteralPath $PackageProof -Raw | ConvertFrom-Json
foreach ($package in $proof.Packages) { Require ((File-Identity $package.file).Sha256 -eq $package.sha256.ToUpperInvariant()) 'Existing proof package bytes changed.' }
$consumer = Join-Path $EvidenceDirectory 'consumer'
[void][IO.Directory]::CreateDirectory($consumer)
Copy-Item -LiteralPath (Join-Path $repoRoot 'tests\OpenVisionLab.Inspection.Smoke\Support\QrCodeFixtures.cs') -Destination (Join-Path $consumer 'QrCodeFixtures.cs')
$feed = Split-Path -Parent $proof.Packages[0].file
$configuration = '<configuration><packageSources><clear/><add key="exact-local-proof" value="' + [Security.SecurityElement]::Escape($feed) + '"/></packageSources></configuration>'
[IO.File]::WriteAllText((Join-Path $consumer 'NuGet.Config'), $configuration, $utf8)
$project = '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net8.0</TargetFramework><IsPackable>false</IsPackable><ImplicitUsings>enable</ImplicitUsings></PropertyGroup><ItemGroup><PackageReference Include="OpenVisionLab.Inspection" Version="3.1.0-dev.1"/><PackageReference Include="OpenVisionLab.Vision2D.Blob" Version="3.1.0-dev.1"/></ItemGroup></Project>'
[IO.File]::WriteAllText((Join-Path $consumer 'OperationPackageConsumer.csproj'), $project, $utf8)
$caller = @'
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using OpenCvSharp;
using OpenVisionLab.Vision2D.Pipeline;
using OpenVisionLab.Vision2D.Tool;
using OpenVisionLab.Inspection.Smoke;
VisionPipeline profile = new VisionPipeline();
VisionPipelineStep step = new VisionPipelineStep { ToolType = "qrcode", InputLayer = "Source", OutputLayer = "Current" };
step.Parameters["ExpectedCodeCount"] = "1";
profile.Steps.Add(step);
string xml = VisionPipelineSerializer.Serialize(profile);
using Mat a = QrCodeFixtures.Scene("a");
using Mat b = QrCodeFixtures.Scene("b");
using VisionPipelineContext firstContext = new VisionPipelineContext();
firstContext.SetLayer("Source", a);
using VisionPipelineRunResult first = new VisionPipelineRuntime().Run(profile, firstContext);
if (!first.Success || first.StepResults[0].ToolResult.QrCodeReading.Candidates.Single().Text != "PART-A") throw new Exception("First oracle differs.");
Mat retained = first.StepResults[0].ToolResult.ResultImage;
using (VisionPipelineContext missing = new VisionPipelineContext())
using (VisionPipelineRunResult failed = new VisionPipelineRuntime().RunWithFailureResults(profile, missing)) {
    if (failed.StepResults.Single().ToolResult.ErrorCode != VisionToolErrorCode.InputLayerMissing || missing.GetLayer("Current") != null) throw new Exception("Failure retained stale output.");
}
VisionPipeline restored = VisionPipelineSerializer.Deserialize(xml);
using (QrCodeTool loaded = (QrCodeTool)VisionPipelineToolFactory.Create(restored.Steps.Single())) {
    if (loaded.LatestReading != null || !loaded.imageSource.Empty()) throw new Exception("Restore executed.");
}
using VisionPipelineContext next = new VisionPipelineContext();
next.SetLayer("Source", b);
using VisionPipelineRunResult recovered = new VisionPipelineRuntime().Run(restored, next);
if (!recovered.Success || recovered.StepResults[0].ToolResult.QrCodeReading.Candidates.Single().Text != "PART-B" || retained.IsDisposed) throw new Exception("Recovery or retained lifetime differs.");
restored.Steps[0].Parameters["ExpectedCodeCount"] = "0";
bool rejected = false;
try { using var invalid = (QrCodeTool)VisionPipelineToolFactory.Create(restored.Steps[0]); }
catch (ArgumentException) { rejected = true; }
if (!rejected) throw new Exception("Invalid restored setting accepted.");
first.Dispose();
if (!retained.IsDisposed || a.IsDisposed || b.IsDisposed) throw new Exception("Caller ownership differs.");
Assembly sdk = typeof(QrCodeTool).Assembly;
Console.WriteLine(JsonSerializer.Serialize(new { Status="Passed", Checks=6, PackageOnly=true, Runtime=RuntimeInformation.FrameworkDescription,
    Source=sdk.Location, Vision2DSha256=Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(sdk.Location))),
    Gates=new[]{"authored first reading","missing input has no stale output","restore without execution","different input recovery and retained snapshot","invalid persisted setting rejection","result release preserves caller inputs"} }));
'@
[IO.File]::WriteAllText((Join-Path $consumer 'Program.cs'), $caller, $utf8)
$consumerBin = Join-Path $consumer 'bin'
$consumerArguments = @('build',(Join-Path $consumer 'OperationPackageConsumer.csproj'),'-c','Release','--artifacts-path',(Join-Path $consumer 'build'),'-o',$consumerBin,'--configfile',(Join-Path $consumer 'NuGet.Config'),"-p:RestorePackagesPath=$(Join-Path $consumer 'nuget')",'-p:UseSharedCompilation=false','--nologo')
[void](Invoke-OwnedProcess 'package-consumer-build' $dotnet $consumerArguments 0 $consumer)
$consumerResult = Invoke-OwnedProcess 'package-consumer-flow' $dotnet @((Join-Path $consumerBin 'OperationPackageConsumer.dll')) 0 $consumerBin
$consumerReport = $consumerResult | ConvertFrom-Json
Require ($consumerReport.Status -eq 'Passed' -and $consumerReport.Vision2DSha256 -eq $proof.FinalVision2DSha256.ToUpperInvariant()) 'Package-only run did not load the exact recorded final Vision2D DLL.'
Write-Json (Join-Path $reports 'package-consumer.json') $consumerReport

$provenance = Get-Content -LiteralPath (Join-Path $repoRoot 'src\OpenVisionLab.Core\ThirdParty\provenance.json') -Raw | ConvertFrom-Json
$binaryChecks = @($provenance.binaries | ForEach-Object {
    $actual = File-Identity (Join-Path $repoRoot $_.sourcePath)
    Require ($actual.Sha256 -eq $_.sha256) 'Vendored native/managed bytes differ from the manifest.'
    @{ Component = $_.logicalName; Actual = $actual; PackagePath = $_.packagePath; OfficialArtifact = $_.officialArtifact; TechnicalStatus = 'Exact manifest match' }
})
Add-Type -AssemblyName System.IO.Compression
$inventory = @($proof.Packages | ForEach-Object {
    $package = $_
    $zip = [IO.Compression.ZipFile]::OpenRead($package.file)
    try {
        $names = @($zip.Entries | ForEach-Object FullName)
        Require (@($names | Where-Object { $_ -match 'PL-0069|Inspection\.Smoke|SdkOperation|FirstInspection|(^|/)eng/' }).Count -eq 0) 'Dev-only assets occurred in an existing proof package.'
        if ([IO.Path]::GetFileName($package.file).StartsWith('OpenVisionLab.Core.')) {
            foreach ($item in $provenance.binaries) {
                $entry = $zip.GetEntry($item.packagePath)
                Require ($null -ne $entry) 'Core proof package omitted a required vendored entry.'
                $stream = $entry.Open()
                try { $hash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData($stream)) }
                finally { $stream.Dispose() }
                Require ($hash -eq $item.sha256) 'Core packaged vendored bytes differ.'
            }
        }
        @{ File = File-Identity $package.file; Entries = $names }
    }
    finally { $zip.Dispose() }
})
Write-Json (Join-Path $reports 'dependency-review.json') @{
    Status = 'Local technical review complete; redistribution approval pending'; Provenance = File-Identity (Join-Path $repoRoot 'src\OpenVisionLab.Core\ThirdParty\provenance.json');
    Binaries = $binaryChecks; ExistingProofPackages = $inventory; PackageEvidenceSource = $PackageProof;
    NoticeOwner = 'Existing Core ThirdParty manifest/NOTICE and THIRD_PARTY_REDISTRIBUTION_CLEARANCE_CHECKLIST; unchanged';
    Decisions = @(
        @{ Subject='Mandatory OpenCvSharp/OpenCV'; Local='Exact source and Core package bytes; missing/invalid copy rejects, valid fresh child recovers'; Approval='Distribution/legal owner must approve exact final notice, Intel conditions, immutable candidate bytes and workflow' },
        @{ Subject='Optional CUDA'; Local='Exact explicitly selected native module/driver/runtime recorded; synthetic fault differs from real-device evidence; missing/invalid module retains CPU result'; Approval='Approved driver/device workload and RAM/Takt decision pending; study override does not change production policy' },
        @{ Subject='Compatibility'; Local='Current Windows x64/.NET 8 process and fresh exact package-only workflow; unchanged API and prior old-compiled-client gates reused at recorded bytes'; Approval='Other requested runtime/native/legacy environments not yet designated' },
        @{ Subject='Support/reproduction'; Local='Owned synthetic failure/recovery below; no user dataset or automatic collection/transmission'; Approval='Recipient, operator, retention, support responsibility/SLA and explicit sharing instruction pending' },
        @{ Subject='Release/deployment'; Local='No candidate version, tag, release, Original update or deployment selected/performed'; Approval='Exact immutable candidate, approval record, target, migration/rollback and named publication/deployment instruction required separately' }
    )
}

$repro = Join-Path $EvidenceDirectory 'reproducer'
[void][IO.Directory]::CreateDirectory((Join-Path $repro 'bin'))
[void][IO.Directory]::CreateDirectory((Join-Path $repro 'input'))
Get-ChildItem -LiteralPath $AssemblyDirectory -File | Where-Object { $_.Extension -eq '.dll' -or $_.Name.EndsWith('.deps.json') -or $_.Name.EndsWith('.runtimeconfig.json') } | ForEach-Object { Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $repro 'bin') }
Copy-Item -LiteralPath (Join-Path $corruptDirectory 'profile.xml') -Destination (Join-Path $repro 'input\profile.xml')
Copy-Item -LiteralPath (Join-Path $corruptDirectory 'profile.xml') -Destination (Join-Path $repro 'input\bad-profile.xml')
Copy-Item -LiteralPath (Join-Path $inputs 'profile.xml') -Destination (Join-Path $repro 'input\good-profile.xml')
Copy-Item -LiteralPath (Join-Path $inputs 'b.png') -Destination (Join-Path $repro 'input\b.png')
[void](Host-Case 'reproducer-failure' 'load' 3 'InvalidInputOrSettings' (Join-Path $repro 'input') (Join-Path $repro 'bin'))
Copy-Item -LiteralPath (Join-Path $repro 'input\good-profile.xml') -Destination (Join-Path $repro 'input\profile.xml') -Force
$restored = Host-Case 'reproducer-restore-only' 'load' 0 'LoadedOnly' (Join-Path $repro 'input') (Join-Path $repro 'bin')
Require (!$restored.InspectionPerformed) 'Reproducer restore performed unintended inspection.'
[void](Host-Case 'reproducer-explicit-recovery' 'inspect-b' 0 'Completed' (Join-Path $repro 'input') (Join-Path $repro 'bin'))
Write-Json (Join-Path $repro 'reproduction-record.json') @{
    Status='Passed'; Origin='Owned synthetic truncated profile; invalid load -> complete-byte restore only -> explicit PART-B reading';
    Commands=@($commands | Where-Object { $_.Name.StartsWith('reproducer-') }); Files=@(Get-ChildItem -LiteralPath (Join-Path $repro 'input') -File | ForEach-Object { File-Identity $_.FullName });
    Boundary='The final directory is recovered. Re-copy input/bad-profile.xml to input/profile.xml to repeat failure. External delivery, user data collection and retention/SLA are unapproved.'
}
Write-Json (Join-Path $reports 'summary.json') @{
    Status='Complete'; Scope='Four approved Dev local operating-preparation steps only'; SourceCommit=(& git -C $repoRoot rev-parse HEAD);
    Smoke=(Get-Content -LiteralPath (Join-Path $reports 'smoke.json') -Raw | ConvertFrom-Json);
    ProcessCases=$cases.ToArray(); Costs=@($scenarios | ForEach-Object { Join-Path $reports "cost-$_.json" });
    PackageConsumer=$consumerReport; NativeHandles='Synthetic active-call/fault counters asserted by focused Smoke; native process address space released on owned child termination';
    Boundary='No field accuracy, approved operating environment/input/capacity, RAM/Takt acceptance, real GPU loss, soak, other runtime/OS, distribution/legal approval or deployment. SDK owners/code/versions unchanged.'
}
Write-Host "Complete | local operation reports: $reports"
