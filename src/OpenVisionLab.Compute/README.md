# OpenVisionLab.Compute

Optional acceleration discovery, execution evidence and session lifetime for
OpenVisionLab. This .NET Standard 2.0 library has no project or OpenCV dependency.
Current CUDA execution is supported on Windows x64.

## First use

CPU is the default. To request supported CUDA work, create a `VisionComputeSession`
with `new VisionComputeOptions { UseCuda = true }` and pass it to
`EdgeBasedTemplateMatchingTool` or the supported local-operation tools below.
The session copies options at construction;
create another session to change the preference. The host owns session disposal
and settings persistence. A tool borrows the session and must be disposed before
its session. Construction does not load a CUDA module or probe hardware.

## Optional native module

CUDA execution needs a compatible NVIDIA GPU/driver and
`OpenVisionLab.Cuda.Native.dll` beside this assembly or under its
`runtimes/win-x64/native` directory. The module is not bundled in this package.
The built module does not require the CUDA Toolkit, NVRTC or `cudart` on consumer
machines. Source compilation requires the offline build tools described by
[`Build-CudaProbe.ps1`](../../eng/Build-CudaProbe.ps1). Its two modes produce the
same filename, so the name alone does not establish execution capability:

| Build input | Module and next step |
| --- | --- |
| Omit `NvrtcLibraryPath` | Discovery-only DLL. It queries devices but has no execution kernels. An explicit supported workload falls back with `KernelNotImplemented` and `UpdateAccelerationModule`. |
| Supply an existing absolute `NvrtcLibraryPath` | Execution bridge with offline PTX. Use a compiler compatible with the target in `CompileKernels.cpp`; initialization and numerical checks run on the first eligible explicit execution. |

For source development, set $outputRoot to an absolute writable build-output
directory and use separate subdirectories:

```powershell
./eng/Build-CudaProbe.ps1 -OutputDirectory (Join-Path $outputRoot 'cuda-probe')
# Set $nvrtc to the absolute path of the compatible build-time compiler DLL.
./eng/Build-CudaProbe.ps1 -OutputDirectory (Join-Path $outputRoot 'cuda-execution') -NvrtcLibraryPath $nvrtc
```

Select the module from its build record, ABI/exports and SHA-256, then confirm
actual application from the execution result. A completed build is not device
qualification. The current Pascal/compute 6.1 build evidence uses NVRTC 12.9.86;
changing the toolchain requires separate compatibility verification.

An absent or incompatible module/driver, unsupported workload, insufficient GPU
memory or GPU failure selects the CPU path. Disabled CUDA avoids optional module
loading. Public 3D surface-pose search currently remains on CPU.

## Evidence and lifetime

`Support` reads the current snapshot without starting work. `ProbeCuda` performs
explicit discovery; a detected device alone does not prove that an algorithm was
accelerated. Read `VisionToolResult.ComputeDiagnostics.AccelerationApplied` and
`FallbackReason` for the execution that produced a result. Early input-validation
results may have no compute diagnostics.

A successful `ProbeCuda` reports `Reason=None`, `DeviceDetected=true`,
`IsAccelerationAvailable=false` and `InstallationAction=None`. It has not checked
the execution ABI or run any algorithm kernel, even with an execution DLL present.
`None` means no failure in the stage completed so far. It does not promise that
the installed module can execute a workload.

The first eligible explicit execution checks the execution ABI and numerical
capability. After that check, `IsAccelerationAvailable=true` describes session
capability; only `AccelerationApplied=true` proves CUDA contributed to this result.
An unsupported algorithm can report `KernelNotImplemented` in execution evidence
while its discovery support still reports `None` and requires no module update.
Small Edge grids can report `WorkloadTooSmall`; normal preparation,
Scharr and surface-pose search report `NoMeasuredBenefit`. These are algorithm choices, not missing-driver diagnoses.

`CudaSupportReport.InstallationAction` describes the current setup condition.
A missing module requests installation; an incompatible module requests a compatible
module. If an explicit execution finds no execution ABI, the session records
`KernelNotImplemented` and requests `UpdateAccelerationModule`. Other initialization
failures retain their reason/error and CPU recovery rather than the old discovery
snapshot. The SDK never installs drivers automatically. Use `DeviceOrdinal` to
request one detected device; this is not a multi-GPU scheduler.

Session execution is serialized. The host must serialize operations on one tool.
Search workspace is released on success, cancellation or failure, and disposing
the session releases its native execution context. Temporary GPU budgets do not
limit total host RAM; input images, CPU gradients and outputs remain host-owned.
Cancellation is cooperative; it cannot interrupt a native CUDA/OpenCV operation
already in progress. Keep same-tool settings, execution and disposal serialized;
sharing a session does not enable parallel GPU execution. Failed initialization
and terminal GPU failures are remembered in that session. After correcting the
module or preference, dispose borrowers before the session and create a new session
for a retry. Restoring the preference alone never retries or executes inspection.

## Local operations

The existing Tool constructors without a session still use CPU. A borrowed
session with `UseCuda=true` requests eligible work; it does not promise lower
latency. Short calls and first-use initialization can cost more than CPU.
Check `Success` and `ComputeDiagnostics` independently on every returned result.

- [`DeterministicMedianFilterTool`](../OpenVisionLab.Vision3D/FeatureExtraction/Filtering/DeterministicMedianFilterTool.cs)
  and [`DeterministicLocalMedianOutlierFilterTool`](../OpenVisionLab.Vision3D/FeatureExtraction/Filtering/DeterministicLocalMedianOutlierFilterTool.cs)
  accept a host-owned session through additive constructors. Their existing
  Execute signatures, clipped neighborhoods, finite/missing semantics and
  ordered result counts/indices remain the contract. Regular Median falls back
  with `NumericalRangeUnsupported` for negative zero to preserve CPU zero-tie bits.
- [`EdgeDetectionTool`](../OpenVisionLab.Vision2D/OpenCV/Tool/EdgeDetectionTool.cs)
  accepts the same session, directly or through the existing Factory overload.
  Public Scharr execution remains CPU: whole-consumer measurements did not
  establish a stable benefit. Supported 8U single/three-channel Scharr X/Y
  reports `NoMeasuredBenefit`; other edge operators/depths use their existing
  CPU/validation path. The internal CUDA qualification subset uses scale 1,
  delta 0 and Reflect101, and does not establish public acceleration. The token
  Execute overload is cooperative. Non-8U CPU Scharr retains a separate input
  during destination-depth conversion.
- [`HeightMapNormalPreparationTool`](../OpenVisionLab.Vision3D/FeatureExtraction/HeightMapNormalPreparationTool.cs)
  accepts a session for diagnostics, while public execution remains CPU with
  `NoMeasuredBenefit`. Its optional CUDA qualification path is internal and
  does not establish public acceleration support.

New local-operation exports are optional additions to execution ABI 1. Normal
and Scharr exports are used only by internal qualification. An older
execution DLL can retain Edge CUDA while a newly requested primitive falls back
with `NativeExportMissing`. Use the result's fallback reason; the global Support
snapshot alone does not identify every algorithm export. Device discovery and
constructor/preference restoration never qualify or run these kernels.

With caller-owned `rowCount`, `columnCount` and row-major `values`:

```csharp
using OpenVisionLab.Compute;
using OpenVisionLab.Vision3D.FeatureExtraction;

using var session = new VisionComputeSession(new VisionComputeOptions { UseCuda = true });
var tool = new DeterministicMedianFilterTool(session);
var result = tool.Execute(rowCount, columnCount, values,
    new DeterministicMedianFilterOptions { KernelSize = 7 });
// Consume result.Success, Values, ChangedCount and ComputeDiagnostics here.
// Complete all borrowed-tool calls before disposing the host session.
```

For Scharr, pass the session to `new EdgeDetectionTool(session)`, configure
`EdgeDetectionToolProperty { EdgeType = EdgeDetectionToolType.Scharr,
ScharrDegreeX = 0, ScharrDegreeY = 1 }`, then explicitly Execute a caller-owned
8U grayscale Mat. Its public diagnostics report CPU policy rather than CUDA
application. Dispose the result and 2D Tool before the session.

Local-operation scratch uses the existing bounded native budget. Median and
LocalMedian process eligible larger inputs as serialized row tiles with the
neighborhood halo, using the existing per-call buffers and exports. No new
caller setting is needed. Tile sizing respects the available GPU memory at the
start of a call as well as the fixed scratch cap; a later allocation can still
fail if another consumer changes memory availability. The managed input/result
cap still applies; a row that cannot fit the native budget remains a complete
CPU fallback. See the
[budget and tile owner](../../native/OpenVisionLab.Cuda/KernelExecution.cpp).
Budget or allocation failures discard the CUDA result and compute the entire
CPU result.
Native execution/result-validation failures invalidate that session's GPU
execution handle; subsequent calls retain CPU recovery until a fresh session.
Cancellation propagates without publishing a partial accelerated result.

## Read the backend used by this execution

The [first inspection example](../../samples/OpenVisionLab.FirstInspection/Program.cs)
creates the host-owned session, restores a matching Tool with the base factory,
executes explicitly, copies diagnostics, and disposes Tools before the session.
The [consumer instructions](../../README.md#first-inspection-example) connect this
to save/restart and missing/incompatible-module recovery. Construction and saved
preference restoration perform no probe or inspection.

With a configured Tool and source, read each result directly:

```csharp
using VisionToolResult result = tool.Execute(source, cancellationToken);
ComputeExecutionEvidence evidence = result.ComputeDiagnostics;
Console.WriteLine(evidence == null
    ? "Backend not established for this execution"
    : $"Requested CUDA={evidence.RequestedCuda}; Applied={evidence.AccelerationApplied}; Fallback={evidence.FallbackReason}");
```

Keep inspection success/error separate from backend evidence. A successful CPU
fallback does not mean CUDA ran, and successful kernel execution does not by
itself establish an overall speed benefit. Persist only the explicitly confirmed
preference at the host's workflow scope; restoring it never starts execution.
