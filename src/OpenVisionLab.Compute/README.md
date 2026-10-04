# OpenVisionLab.Compute

Optional acceleration discovery, execution evidence and session lifetime for
OpenVisionLab. This .NET Standard 2.0 library has no project or OpenCV dependency.
Current CUDA execution is supported on Windows x64.

## First use

CPU is the default. To request supported CUDA work, create a `VisionComputeSession`
with `new VisionComputeOptions { UseCuda = true }` and pass it to
`EdgeBasedTemplateMatchingTool`. The session copies options at construction;
create another session to change the preference. The host owns session disposal
and settings persistence. A tool borrows the session and must be disposed before
its session. Construction does not load a CUDA module or probe hardware.

## Optional native module

CUDA execution needs a compatible NVIDIA GPU/driver and
`OpenVisionLab.Cuda.Native.dll` beside this assembly or under its
`runtimes/win-x64/native` directory. The module is not bundled in this package.
The built module does not require the CUDA Toolkit, NVRTC or `cudart` on consumer
machines. Source compilation requires the offline build tools described by
[`Build-CudaProbe.ps1`](https://github.com/Noah8218/OpenVisionLab-Vision-SDK/blob/main/eng/Build-CudaProbe.ps1).

An absent or incompatible module/driver, unsupported workload, insufficient GPU
memory or GPU failure selects the CPU path. Disabled CUDA avoids optional module
loading. Public 3D surface-pose search currently remains on CPU.

## Evidence and lifetime

`Support` reads the current snapshot without starting work. `ProbeCuda` performs
explicit discovery; a detected device alone does not prove that an algorithm was
accelerated. Read `VisionToolResult.ComputeDiagnostics.AccelerationApplied` and
`FallbackReason` for the execution that produced a result. Early input-validation
results may have no compute diagnostics.

`CudaSupportReport.InstallationAction` describes the detected setup condition;
the SDK never installs drivers automatically. Use `DeviceOrdinal` to request one
detected device; this is not a multi-GPU scheduler.

Session execution is serialized. The host must serialize operations on one tool.
Search workspace is released on success, cancellation or failure, and disposing
the session releases its native execution context. Temporary GPU budgets do not
limit total host RAM; input images, CPU gradients and outputs remain host-owned.
Cancellation is cooperative; it cannot interrupt a native CUDA/OpenCV operation
already in progress.
