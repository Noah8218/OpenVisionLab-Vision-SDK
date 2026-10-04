# OpenVisionLab Vision SDK

Developers opening the source can follow [Start Here](./docs/README.md#start-here)
for the solution, executable checks and code-reading order. Consumers can use the
[2D input/error/lifetime contracts](./src/OpenVisionLab.Vision2D/README.md#input-and-option-contracts)
and [3D execution/outcome contracts](./docs/three-d-inspection.md#execution-failure-and-lifetime-boundaries).

> **3.0 naming change:** `Library-Noah` and `Lib.* 2.9.1` remain available as
> the compatibility baseline for existing consumers. This source builds the
> `OpenVisionLab.* 3.0` packages, DLLs, and namespaces. Before migrating an
> existing project, read the
> [2.9.1 to 3.0.0 migration guide](./docs/MIGRATING_LIB_2_9_1_TO_OPENVISIONLAB_3_0.md).

OpenVisionLab Vision SDK is a C# vision inspection library for OpenCvSharp-based 2D inspection and UI-independent height-map/full-XYZ 3D computation.

It provides application-ready 2D image-processing tools, 3D feature extraction and measurement algorithms, and shared result states and metrics.

## Version

Current source version: `v3.1.0`.

This project is maintained using explicit version numbers. The source version is
defined in [`Directory.Build.props`](./Directory.Build.props). CLR assembly identity
remains `3.0.0.0` for 3.x compatibility. A source update does not imply that a NuGet
package or binary release has been published.

### Recent version history

#### `v3.1.0` (2026-10-04)

- Add optional CUDA scoring for edge-based template matching, tiled large-image processing and automatic CPU fallback.
- Reduce gradient and transfer temporary memory, reuse GPU workspace within each search and release it on completion, cancellation or failure.
- Improve 2D subpixel/model-artifact workflows and 3D surface matching, datum fitting and numerical validation.

#### `v3.0.0` (2026-08-05)

- Introduce the `OpenVisionLab.*` package, DLL and namespace names, with a migration contract for `Lib.* 2.9.1` consumers.

## 1-Minute Overview

- `OpenVisionLab.Compute` provides optional CUDA discovery, host-owned sessions and per-execution fallback evidence.
- `OpenVisionLab.Core` provides UI-independent coordinate and line calculations and packages the native OpenCV DLL.
- `OpenVisionLab.Vision2D` provides primary inspection tools including Threshold, Filter, Edge, Contour, Matching, and LineGauge.
- `OpenVisionLab.Vision2D.Blob` provides Blob labeling and area filtering.
- `OpenVisionLab.Vision3D` provides UI-independent 3D contracts and algorithms for height maps, connected-region labeling/metrics/presence, full-XYZ geometry, rigid point-pair alignment, affine/regrid operations, thickness, warpage, flatness, gap/flush, volume, and more.
- `OpenVisionLab.Inspection` preserves existing 2D tools and `IThreeDInspectionTool` results in one combined run result.
- Run 2D tools with `Execute(Mat source)` and height-map inspection tools with `Execute(HeightMap3D source)`.
- The SDK has no direct UI-framework dependency. The host application owns rendering, ROI editing, and recipe management around the measurements.

## Clone and Develop on Another PC

Use a short checkout path on a Windows x64 machine. Install Git, the .NET SDK
selected by [`global.json`](./global.json), and PowerShell 7. The first restore needs
access to NuGet.org. Windows Server also needs the Media Foundation feature used by
the bundled OpenCV runtime.

```powershell
git clone https://github.com/Noah8218/OpenVisionLab-Vision-SDK.git OpenVisionLab-Vision-SDK
Set-Location .\OpenVisionLab-Vision-SDK
dotnet --version
pwsh --version
dotnet tool restore

$testRoot = Join-Path ([IO.Path]::GetTempPath()) "OpenVisionLab-Vision-SDK-check"
New-Item -ItemType Directory -Force -Path $testRoot | Out-Null
dotnet build OpenVisionLab.VisionSdk.sln -c Release --artifacts-path "$testRoot\build"
$smokeAssembly = "$testRoot\build\bin\OpenVisionLab.Inspection.Smoke\release\OpenVisionLab.Inspection.Smoke.dll"
dotnet $smokeAssembly
```

`dotnet --version` must resolve the SDK requested by `global.json`; if it does not,
install that SDK before building. The console smoke suite is the repository's source
checkout check. Application projects that reference the SDK source directly must
also follow the managed/native OpenCvSharp reference example below.

## Installation and References

NuGet is the recommended consumer path because it carries the managed and native
OpenCvSharp assets together. For development against a checkout, add the source
projects you use **and** reference Core's managed OpenCvSharp assembly and copy its
native runtime asset. A project reference alone does not make `OpenCvSharp` types
available to the consumer or place `OpenCvSharpExtern.dll` beside the executable.

```xml
<PropertyGroup>
  <VisionSdkRoot>..\OpenVisionLab-Vision-SDK</VisionSdkRoot>
</PropertyGroup>

<ItemGroup>
  <ProjectReference Include="$(VisionSdkRoot)\src\OpenVisionLab.Core\OpenVisionLab.Core.csproj" />
  <ProjectReference Include="$(VisionSdkRoot)\src\OpenVisionLab.Vision2D\OpenVisionLab.Vision2D.csproj" />
  <ProjectReference Include="$(VisionSdkRoot)\src\OpenVisionLab.Vision2D.Blob\OpenVisionLab.Vision2D.Blob.csproj" />
  <ProjectReference Include="$(VisionSdkRoot)\src\OpenVisionLab.Vision3D\OpenVisionLab.Vision3D.csproj" />
  <ProjectReference Include="$(VisionSdkRoot)\src\OpenVisionLab.Inspection\OpenVisionLab.Inspection.csproj" />
</ItemGroup>

<ItemGroup>
  <Reference Include="OpenCvSharp">
    <HintPath>$(VisionSdkRoot)\src\OpenVisionLab.Core\DLL\OpenCvSharp.dll</HintPath>
    <Private>True</Private>
  </Reference>
  <None Include="$(VisionSdkRoot)\src\OpenVisionLab.Core\DLL\OpenCvSharpExtern.dll">
    <Link>OpenCvSharpExtern.dll</Link>
    <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
  </None>
</ItemGroup>
```

The example assumes the consumer project is next to the SDK checkout; adjust
`VisionSdkRoot` for another layout. The direct-reference route is Windows x64
only and is intended for local development. Use the package route for a clean
consumer-output contract.

Package roles and runtime requirements are listed in [Packaging Notes](#packaging-notes).
Use the source build below for this checkout and keep the managed and native OpenCV
assets together. Do not infer an available NuGet release from the source version.

## 2D Quick Start

The following example reads the sample image and saves the Canny edge result to `artifacts/smoke_edge.png`.

```csharp
using System;
using System.IO;
using OpenVisionLab.Vision2D;
using OpenVisionLab.Vision2D.Property;
using OpenVisionLab.Vision2D.Tool;
using OpenCvSharp;

Directory.CreateDirectory("artifacts");

using (Mat source = Cv2.ImRead("docs/samples/vision_sample.png", ImreadModes.Grayscale))
{
    using EdgeDetectionTool tool = new EdgeDetectionTool();
    tool.SetProperty(new EdgeDetectionToolProperty
    {
        EdgeType = EdgeDetectionToolType.Canny,
        CannyThresholdLow = 80,
        CannyThresholdHigh = 160,
        CannyApertureSize = 3
    });

    using VisionToolResult result = tool.Execute(source);
    if (!result.Success)
    {
        throw new InvalidOperationException($"{result.ErrorName}: {result.Message}");
    }

    Cv2.ImWrite("artifacts/smoke_edge.png", result.ResultImage);
}
```

## 3D Quick Start

The following example declares the X/Y grid unit, height unit, coordinate frame, and minimum valid coverage before inspecting thickness.

```csharp
using System;
using OpenVisionLab.Vision3D.Geometry;
using OpenVisionLab.Vision3D.Inspection;

HeightMap3D heightMap = HeightMap3D.FromArray(
    values: new[,]
    {
        { 1.00, 1.05, 1.10 },
        { 1.15, double.NaN, 1.20 }
    },
    originX: 0.0,
    originY: 0.0,
    columnPitch: 0.1,
    rowPitch: 0.1,
    planarUnit: "mm",
    heightUnit: "mm",
    frameId: "fixture-top",
    sourceId: "scan-001");

ThicknessInspectionTool tool = new ThicknessInspectionTool(
    new ThicknessInspectionOptions
    {
        MinimumThickness = 0.95,
        MaximumThickness = 1.25,
        MinimumValidSamples = 5,
        MinimumValidCoverageRatio = 0.8,
        InputRequirements = new HeightMapInputRequirements("mm", "mm", "fixture-top")
    });

ThreeDInspectionResult result = tool.Execute(heightMap);
if (result.MeasurementOutcome == ThreeDMeasurementOutcome.NotMeasured)
{
    throw new InvalidOperationException($"{result.ErrorName}: {result.Message}");
}

if (!result.TryGetMetric(ThreeDInspectionMetricNames.Thickness.Mean, out double mean, out string meanUnit))
{
    throw new InvalidOperationException("Thickness mean was not produced.");
}

Console.WriteLine($"{result.MeasurementOutcome}, Mean={mean} {meanUnit}");
```

`MeasurementOutcome` distinguishes `Passed`, `OutOfTolerance`, and `NotMeasured` directly. The former combination of `Success=false` and `HasMeasurement=true` maps to `OutOfTolerance`. Unit or frame mismatches, invalid ROIs, insufficient samples, and insufficient coverage map to `NotMeasured`. See [3D inspection](./docs/three-d-inspection.md) for the complete contract.

## Optional CUDA Acceleration

CPU execution is the default. The current CUDA path accelerates edge-based template
matching scores; it does not move every 2D or 3D algorithm onto the GPU. Public 3D
surface-pose search currently uses CPU execution.

Create one host-owned session and pass it to the tool. `UseCuda` is the single
enable option; changing a settings object after session construction does not
change that session. The host owns settings persistence and session disposal.
Tools borrow the session and retain ownership of their images and models.

With caller-owned `source` and `template` Mats:

```csharp
using System;
using OpenVisionLab.Compute;
using OpenVisionLab.Vision2D.Property;
using OpenVisionLab.Vision2D.Tool;

using var session = new VisionComputeSession(new VisionComputeOptions { UseCuda = true });
using var tool = new EdgeBasedTemplateMatchingTool(session);
tool.SetProperty(new EdgeBasedTemplateMatchingToolProperty());
tool.SetTemplateImage(template);
using VisionToolResult result = tool.Execute(source);

if (result.ComputeDiagnostics != null)
{
    Console.WriteLine($"CUDA applied: {result.ComputeDiagnostics.AccelerationApplied}");
    Console.WriteLine($"Fallback: {result.ComputeDiagnostics.FallbackReason}");
}
```

Windows x64 CUDA execution needs a compatible NVIDIA GPU/driver and
`OpenVisionLab.Cuda.Native.dll` beside `OpenVisionLab.Compute.dll`, or under its
`runtimes/win-x64/native` directory. The CUDA module is optional: an absent driver
or module, an incompatible module, insufficient GPU memory or failed GPU execution
uses the CPU path. Small or unqualified workloads can also select CPU execution.
Disabling CUDA avoids loading the optional module.

Consumers do not need the CUDA Toolkit, NVRTC or `cudart` DLLs to run the built
module. Building that module from source requires MSVC x64, the Windows SDK and
NVRTC for offline kernel compilation; see [`eng/Build-CudaProbe.ps1`](./eng/Build-CudaProbe.ps1).
The SDK reports installation actions through `CudaSupportReport.InstallationAction`;
it does not install or update drivers automatically.

Large score grids use bounded GPU tiles and release their workspace after each
search. This bounds temporary GPU storage, not the host application's total RAM.
Preprocessing, full-image CPU gradients and output images still use host memory.
The [Compute guide](./src/OpenVisionLab.Compute/README.md) describes discovery,
execution evidence and session lifetime.

### Measured large-image example

The following measurements are a 2026-10-04 source snapshot using a Ryzen 5 2600,
GTX 1060 3GB and NVIDIA driver 582.28. The input is a full synthetic image with one
192 × 192 template, search step 2 and a 500-point model. Images are not resized or
cropped. Values are the median of three warm calls after a first call, with the
session retained. Timed work includes preprocessing, gradients, search, transfers,
refinement, drawing and result-image copying; input setup and forced GC are outside
the timer.

| Full image | CPU | CUDA |
| --- | ---: | ---: |
| 5,000 × 5,000 | 1.027 s | 0.697 s |
| 10,000 × 10,000 | 4.517 s | 3.025 s |
| 15,000 × 15,000 | 10.879 s | 7.214 s |

CPU and CUDA returned the same matching results and output pixels in these cases.
First-call initialization costs are excluded from the table. These observations
do not establish performance on other GPUs, textured/dense-edge scenes, multiple
models, real sensors or production takt time. The reproducible entry point is
[`CudaKernelSmokeSuite.LargeImageBenchmark`](./tests/OpenVisionLab.Inspection.Smoke/Suites/CudaKernelSmokeSuite.cs).

## Companion Verification Applications

OpenVisionLab Vision SDK does not include a UI. The following public applications develop and verify real editing, execution, and review workflows.

| Application | OpenVisionLab Vision SDK Usage Boundary |
| --- | --- |
| [OpenVisionLab](https://github.com/Noah8218/OpenVisionLab) | An OpenCvSharp 4-based, rule-based 2D inspection workbench. It verifies tools, layers, pipelines, and result-display workflows from `OpenVisionLab.Core`, `OpenVisionLab.Vision2D`, and `OpenVisionLab.Vision2D.Blob`. |
| [OpenVisionLab 3D Studio](https://github.com/Noah8218/OpenVisionLab-3D-Studio) | A 3D inspection workbench for C3D, meshes, point clouds, and height maps. It verifies ROIs, Preview/Run, metrics, overlays, and recipe replay through a pinned `OpenVisionLab.Vision3D` NuGet package and explicit adapters. |

Neither application is implicitly coupled to an OpenVisionLab Vision SDK source checkout. In particular, 3D Studio pins a verified package version, so a new API can be used only after explicitly updating the package, hash, and adapter.

## 3D Input Contract

`HeightMap3D` uses the following fixed coordinate convention.

```text
X = OriginX + Column * ColumnPitch
Y = OriginY + Row * RowPitch
H = Values[Row * Columns + Column]
```

| Item | Contract |
| --- | --- |
| `PlanarUnit` | Unit for `OriginX`, `OriginY`, `ColumnPitch`, and `RowPitch` |
| `HeightUnit` | Unit for scalar height `H` and height-based tolerances |
| `FrameId` | Coordinate-frame ID in which the X/Y/H data is declared |
| `SourceId` | Input traceability ID; it does not prove coordinate compatibility |
| `double.NaN` | Missing sample; excluded without interpolation or neighbor bridging |
| `±Infinity` | Corrupt input; rejected when creating `HeightMap3D` |

When `HeightMapInputRequirements` is present, units and frames are compared exactly, including case. The SDK performs no automatic unit conversion, alias inference, or coordinate transformation. Measurement begins only when both `MinimumValidSamples` and `MinimumValidCoverageRatio` are satisfied. For compatibility, the legacy single-`Unit` constructor declares the same unit for both planar coordinates and height.

## Sample Data

- Input sample: `docs/samples/vision_sample.png`
- README detection-result images: `docs/images/*.png`

The basic examples assume execution from the repository root and use
`docs/samples/vision_sample.png`. That file is a legacy-branded demonstration input,
not a calibration or production artifact. The six `docs/images/*.png` files are
synthetic visual-reference captures made from other scenes; they were not generated
from `vision_sample.png`. No tracked generator, parameter manifest, source revision,
or checksum record currently makes those captures reproducible, so they are
illustrations rather than test or release evidence. When running elsewhere, adjust
the sample path relative to the executable.

## Matching Contract References

- [Auto MPoint teaching core](./docs/AUTO_MPOINT_V1.md)
- [Edge-based fail-closed unique result](./docs/EDGE_BASED_UNIQUE_MATCH_V1.md)

## 2D object candidate evidence

Blob and Contour expose the additive, single-pass candidate contract described
in [`docs/OBJECT_CANDIDATE_CONTRACT.md`](./docs/OBJECT_CANDIDATE_CONTRACT.md).

## Build / Smoke Check

Build check:

```powershell
dotnet restore OpenVisionLab.VisionSdk.sln
dotnet build OpenVisionLab.VisionSdk.sln -c Debug
dotnet run --project tests\OpenVisionLab.Inspection.Smoke\OpenVisionLab.Inspection.Smoke.csproj -c Debug --no-build
```

Smoke check including packaging:

```powershell
dotnet restore OpenVisionLab.VisionSdk.sln
dotnet build OpenVisionLab.VisionSdk.sln -c Debug
dotnet run --project tests\OpenVisionLab.Inspection.Smoke\OpenVisionLab.Inspection.Smoke.csproj -c Debug --no-build
$packageVersion = "3.1.0-dev.$([DateTimeOffset]::UtcNow.ToUnixTimeMilliseconds())"
dotnet pack OpenVisionLab.VisionSdk.sln -c Debug --no-build "-p:PackageVersion=$packageVersion"
```

`OpenVisionLab.Inspection.Smoke` checks deterministic contracts and regressions with synthetic 2D and 3D inputs. It does not replace real sensor data, calibration, Gauge R&amp;R, or production-approval testing.

## CI

The [GitHub Actions workflow](./.github/workflows/build.yml) defines the checks for
`main` pushes and pull requests: Release build, synthetic smoke/coverage checks,
public-API and analyzer baselines, package provenance and an isolated package-only
consumer. Its exact commands and package version are owned by the workflow and
[`eng`](./eng). The workflow does not publish packages or create releases.

## License

The OpenVisionLab-authored portions are distributed under the MIT License. Use of
those portions must retain the copyright notice, license text, and attribution in
`NOTICE` when the MIT terms require it. This statement does not relicense bundled
third-party software.

Copyright (c) 2026 Noah Choi (최노아)

- Full license: [LICENSE](./LICENSE)
- Attribution notices: [NOTICE](./NOTICE)
- Core third-party provenance, notices, and approval boundary:
  [src/OpenVisionLab.Core/ThirdParty/NOTICE.md](./src/OpenVisionLab.Core/ThirdParty/NOTICE.md)
- Machine-readable binary lock:
  [src/OpenVisionLab.Core/ThirdParty/provenance.json](./src/OpenVisionLab.Core/ThirdParty/provenance.json)

`OpenVisionLab.Core` currently mixes official OpenCvSharp managed binaries from
`4.4.0.20200915` with an official native binary from `4.3.0.20200708`. Their exact
bytes are proven. The current binary inventory, preserved terms, and remaining
distribution-owner approval are authoritative in
[Core's notice record](./src/OpenVisionLab.Core/ThirdParty/NOTICE.md) and the
[redistribution checklist](./docs/THIRD_PARTY_REDISTRIBUTION_CLEARANCE_CHECKLIST.md).
`OpenCvSharp.Blob` and its Blob-only LGPL evidence were removed from the current
bundle; their historical provenance remains in Git. Redistribution clearance for
the two remaining binaries still requires approval of the final notices and
distribution workflow. The repository's MIT license and technical provenance
checks do not grant that approval.

Run `pwsh -File ./eng/Verify-ThirdPartyBinaries.ps1` to check the reviewed source bytes,
managed/native identities, exact official-artifact lock, and reviewed evidence.
The package provenance verifier runs the same check and additionally compares every
Core `third-party/` and vendored-binary entry with the repository source.

## Development Environment

- Visual Studio 2022 or the .NET SDK
- C# / .NET Standard 2.0
- PowerShell 7 or later for the provenance and quality scripts
- Windows runtime recommended
- OpenCvSharp-related DLLs are included under `src/OpenVisionLab.Core/DLL`; read
  `src/OpenVisionLab.Core/ThirdParty/NOTICE.md` before any redistribution decision.

Build:

```powershell
dotnet restore OpenVisionLab.VisionSdk.sln
dotnet build OpenVisionLab.VisionSdk.sln -c Release
```

## Project Layout

```text
OpenVisionLab-Vision-SDK
|- src
|  |- OpenVisionLab.Compute
|  |  `- Infrastructure
|  |- OpenVisionLab.Core
|  |  |- Converter
|  |  |- Line
|  |  |- DLL
|  |  `- build
|  |- OpenVisionLab.Vision2D
|  |  `- OpenCV
|  |     |- Pipeline
|  |     |- Property
|  |     |- Result
|  |     `- Tool
|  |- OpenVisionLab.Vision2D.Blob
|  |- OpenVisionLab.Vision3D
|  |  |- Geometry
|  |  |- FeatureExtraction
|  |  |  |- Filtering
|  |  |  |- GeometryConstruction
|  |  |  |- GridAndStatistics
|  |  |  |- Metrology
|  |  |  |- Mesh
|  |  |  |- Registration
|  |  |  `- SurfaceMatching
|  |  `- Inspection
|  `- OpenVisionLab.Inspection
|- native
|  `- OpenVisionLab.Cuda
`- tests
   |- OpenVisionLab.Inspection.Smoke
   |  |- Suites
   |  `- Support
   |- OpenVisionLab.Vision3D.Benchmark
   `- OpenVisionLab.PackageConsumer.Smoke
```

| Project | Role |
| --- | --- |
| `OpenVisionLab.Compute` | Optional acceleration discovery, session lifetime and execution/fallback evidence |
| `OpenVisionLab.Core` | UI-independent coordinate/ROI conversion, numerical and geometric calculations, line calculations, and OpenCV runtime assets |
| `OpenVisionLab.Vision2D` | Primary OpenCV inspection tools, property interfaces, result models, and pipeline execution |
| `OpenVisionLab.Vision2D.Blob` | Blob labeling and area-filtering tools |
| `OpenVisionLab.Vision3D` | UI-independent height-map/full-XYZ contracts, feature extraction, and 3D inspection algorithms |
| `OpenVisionLab.Inspection` | Execution contract that runs 2D and 3D tools in sequence while preserving each original result |
| `OpenVisionLab.Inspection.Smoke` | Executable contract and regression checks with synthetic input, separated into an entry point, domain suites, and shared support code |
| `OpenVisionLab.Vision3D.Benchmark` | Isolated deterministic benchmark harness; it is not production-performance evidence |
| `OpenVisionLab.PackageConsumer.Smoke` | Package-only consumer restored from a freshly packed isolated source/cache; intentionally outside the solution |

Reference relationships (arrow means depends on):

```text
Vision2D -> Core + Compute
Vision2D.Blob -> Vision2D + Core
Vision3D -> Compute
Inspection -> Vision2D + Vision3D
Inspection.Smoke -> Inspection + Vision2D.Blob
Vision3D.Benchmark -> Vision3D
Core / Compute -> no project references
```

## Code Organization

### OpenVisionLab.Core

- `Converter`: UI-independent coordinate and geometry conversion utilities for `Point`, `Rect`, `Rectangle`, and related types
- `Line`: Models and calculators for line fitting, perpendicular-line construction, and intersection calculation
- `CFormula`, `FormulaUtil`: Formula utilities for angles, intersections, perspective transforms, polygon tests, and related calculations
- `DLL`, `build`: OpenCvSharp managed/native runtime assets and the consumer-output copy contract

### OpenVisionLab.Vision2D

- `OpenCV/Tool`: Inspection-tool implementations
- `OpenCV/Property`: Configuration interfaces and selected ready-to-use property classes for each tool
- `OpenCV/Result`: Tool-specific result models for Matching, Contour, Mean, LineGauge, and other tools
- `OpenCV/Pipeline`: Pipeline models and runtime for executing multiple tools in sequence
- `OpenCvHelper`: Utilities for Mat validation and channel conversion

### OpenVisionLab.Vision2D.Blob

- `BlobTool`: Blob tool using the current execution model
- `BlobResult`: Blob result model
- `CVBlob`, `CResultBlob`: Legacy APIs retained for existing-code compatibility

### OpenVisionLab.Vision3D

- `Geometry`: Immutable `HeightMap3D` and X/Y/H grid and ROI contracts
- `FeatureExtraction`: Source-neutral full-XYZ line, plane, affine, reference-grid regrid, median, edge, and line-fit algorithms
- `Inspection`: Thickness, warpage, datum deviation, and independent 3D dimensional inspections

### OpenVisionLab.Inspection

- `CombinedInspectionRunner`: Runs 2D `IVisionTool` and 3D `IThreeDInspectionTool` instances independently
- `CombinedInspectionRunResult`: Preserves original result types, including evidence from stages after a failure

## 2D Tool Execution Model

Most current 2D image tools inherit from `OpenCvAlgorithmBase`.

```text
IVisionTool
`- OpenCvAlgorithmBase
   |- ThresholdTool
   |- MorphologyTool
   |- FilterTool
   |- EdgeDetectionTool
   |- RotateScaleTool
   |- AffineTransformTool
   |- ContourTool
   |- CornerTool
   |- MatchingTool
   |- EdgeBasedTemplateMatchingTool
   |- AutoMPointTool
   |- SiftTool
   |- LineGaugeTool
   |- MeanTool
   `- BlobTool
```

Basic execution flow:

1. Create a tool instance.
2. Set its property object.
3. Call `Execute(Mat source)`.
4. Inspect success, the result image, error codes, metrics, and overlays in `VisionToolResult`.

```csharp
using VisionToolResult result = tool.Execute(source);

if (result.Success)
{
    Mat output = result.ResultImage;
}
else
{
    string error = $"{result.ErrorName}: {result.Message}";
}
```

`Execute` provides common handling for input-image validation, parameter validation, exception handling, result-image copying, and metric collection. Compatibility-oriented `CV*` classes retain the older pattern of calling `Run()` and then reading `results` or `resultList` directly.

## Supported 2D Tools

| Tool | Primary Use | Property |
| --- | --- | --- |
| `ThresholdTool` | Binary, range, and adaptive thresholding | `ThresholdToolProperty` |
| `MorphologyTool` | Morphological operations such as Erode, Dilate, Open, and Close | `MorphologyToolProperty` |
| `FilterTool` | Blur, Gaussian, Median, Bilateral, and related filters | `FilterToolProperty` |
| `EdgeDetectionTool` | Canny, Sobel, Scharr, and Laplacian edge detection | `EdgeDetectionToolProperty` |
| `RotateScaleTool` | Image rotation and scale transforms | `RotateScaleToolProperty` |
| `AffineTransformTool` | Explicit affine-matrix image transform | `AffineTransformToolProperty` |
| `ContourTool` | Contour detection and area filtering | `ContourToolProperty` or an `IOpenCVPropertyContour` implementation |
| `CornerTool` | Sub-pixel corner detection with global-coordinate results | `ContourToolProperty` or an `IOpenCVPropertyContour` implementation |
| `BlobTool` | Blob labeling and area filtering | `BlobToolProperty` or an `IOpenCVPropertyBlob` implementation |
| `MatchingTool` | Template matching with scale and angle search | `MatchingToolProperty` or an `IOpenCVPropertyMatching` implementation |
| `EdgeBasedTemplateMatchingTool` | Edge-based template matching | `EdgeBasedTemplateMatchingToolProperty` or an `IOpenCVPropertyEdgeBasedTemplateMatching` implementation |
| `AutoMPointTool` | Automatic fixed-size match-candidate proposal with uniqueness, synthetic-transform, and performance checks | `AutoMPointToolProperty` |
| `SiftTool` | SIFT feature-point matching, with an ORB fallback when the native runtime lacks SIFT | `SiftToolProperty` or an `IOpenCVPropertyFeatureSIFT` implementation |
| `LineGaugeTool` | Edge detection and line fitting inside an ROI | `LineGaugeToolProperty` or an `IOpenCvPropertyLineGauge` implementation |
| `MeanTool` | ROI mean and standard-deviation calculation | `MeanToolProperty` or an `IOpenCVPropertyMean` implementation |

Multi-ROI execution in `MeanTool` measures each region in `CvROIS` order and returns `MeanResult.index` values in the same order. `CornerTool` returns each sub-pixel-refined point as a `CornerResult` in global image coordinates and returns `CornerNoResult` when no point is detected.

`SiftTool` first creates an OpenCV SIFT detector. The currently bundled native
runtime does not export that entry point, so the tool uses ORB as a documented
compatibility fallback. Read `FeatureDetector.Sift` and `FeatureDetector.OrbFallback`
with `VisionToolResult.Metrics.TryGetValue` to record which detector actually ran.
Early validation/exception results may omit these keys; absence is not a SIFT selection.

## Supported 3D Features

The 3D API is used through three layers based on input shape. `IThreeDInspectionTool` is intentionally narrow and supports only a single `HeightMap3D` inspection; multi-surface and mesh tools do not implement this interface.

| Layer | Input / Result | When to Use | `CombinedInspectionRunner` |
| --- | --- | --- | --- |
| Height-map inspection | `HeightMap3D` → `ThreeDInspectionResult` | Inspecting one regular grid for thickness, warpage, datum deviation, and similar measurements | Supported |
| Source-neutral tool | Tool-specific typed input/options/result | Full-XYZ geometry, regrid, filtering, matching, and mesh comparison | Not supported; execute the tool directly |
| Multi-input dimensional inspection | Caller-prepared points, regions, or statistics → typed result | Flatness, point pair, gap/flush, volume, and cross-section measurements | Not supported; execute the tool directly |

Height-map inspections return input, ROI, and coverage errors as controlled `NotMeasured` results. Source-neutral and multi-input tools use the `Success` or `Passed` contract of their typed results and may reject an invalid call configuration with `ArgumentException`. See the [3D inspection documentation](./docs/three-d-inspection.md#public-tool-catalog) for the complete public tool catalog and input-selection guidance.

| Area | Primary Types | Role |
| --- | --- | --- |
| Height-map inspection | `ThicknessInspectionTool`, `WarpageInspectionTool`, `DatumPlaneRawHeightDeviationInspectionTool` | Measure a scalar map after validating unit, frame, ROI, and missing-sample coverage contracts |
| Geometry and registration | `TwoPointLineTool`, `ThreePointPlaneTool`, `LineIntersectionTool`, `RigidPointPairAlignmentTool`, `ConstrainedBestFitRigidAlignmentTool`, `FullXyzAffineSolveTool`, `AffinePointCloudApplyTool` | Pure geometry calculation plus deterministic exact-three rigid, bounded all-pair proper-rigid best-fit, and affine solve/apply for explicit full-XYZ input |
| Regular-grid construction | `ReferenceGridRegridTool` | Nearest-cell regrid on explicit right-handed U/V/H axes, preserving holes and reporting coverage |
| Feature extraction | `DeterministicMedianFilterTool`, `DeterministicHeightDifferenceEdgeTool`, `DeterministicLineFitTool`, `LeastSquaresHeightFieldPlaneFitTool` | Deterministic filtering, edge detection, and line/plane fitting |
| Dimensional inspection | `PlaneFlatnessInspectionTool`, `PointPairDimensionsInspectionTool`, `GapFlushInspectionTool`, `VolumeInspectionTool`, `CrossSectionDimensionsInspectionTool` | Independent measurements using caller-prepared points, regions, and planes |

`ConstrainedBestFitRigidAlignmentTool` accepts four to sixty-four ordered
source/reference full-XYZ pairs and fits one proper rotation plus translation
using every pair. The route is deliberately constrained: it uses no scale,
shear, reflection, weighting, or automatic outlier rejection. It rejects
non-finite, duplicate, over-cap, and collinear correspondence sets, returns
per-pair residuals plus RMS/maximum diagnostics, and honors cancellation. Unit,
frame, identity, acceptance, and point-cloud lifecycle policy remain with the
caller; the tool produces pose evidence and does not move a cloud.

## Basic Usage Examples

### ThresholdTool

```csharp
using System;
using OpenVisionLab.Vision2D;
using OpenVisionLab.Vision2D.Property;
using OpenVisionLab.Vision2D.Tool;
using OpenCvSharp;

public static class ThresholdExample
{
    public static void Run()
    {
        using (Mat source = Cv2.ImRead("docs/samples/vision_sample.png", ImreadModes.Color))
        {
            using ThresholdTool tool = new ThresholdTool();
            tool.SetProperty(new ThresholdToolProperty
            {
                Mode = ThresholdToolMode.Threshold,
                Threshold = 120,
                MaxValue = 255,
                ThresholdType = ThresholdTypes.Binary
            });

            using VisionToolResult result = tool.Execute(source);
            if (!result.Success)
            {
                throw new InvalidOperationException($"{result.ErrorName}: {result.Message}");
            }

            Cv2.ImWrite("result_threshold.png", result.ResultImage);
        }
    }
}
```

### Filter Then Edge Detection

Canny-based edge detection is safest with single-channel input.

```csharp
using OpenVisionLab.Vision2D;
using OpenVisionLab.Vision2D.Property;
using OpenVisionLab.Vision2D.Tool;
using OpenCvSharp;

using (Mat source = Cv2.ImRead("docs/samples/vision_sample.png", ImreadModes.Grayscale))
{
    using FilterTool filter = new FilterTool();
    filter.SetProperty(new FilterToolProperty
    {
        FilterType = FilterToolType.GaussianBlur,
        KernelWidth = 5,
        KernelHeight = 5
    });

    using VisionToolResult filtered = filter.Execute(source);
    if (!filtered.Success)
    {
        throw new Exception(filtered.Message);
    }

    using EdgeDetectionTool edge = new EdgeDetectionTool();
    edge.SetProperty(new EdgeDetectionToolProperty
    {
        EdgeType = EdgeDetectionToolType.Canny,
        CannyThresholdLow = 80,
        CannyThresholdHigh = 160,
        CannyApertureSize = 3
    });

    using VisionToolResult edgeResult = edge.Execute(filtered.ResultImage);
    if (!edgeResult.Success)
    {
        throw new Exception(edgeResult.Message);
    }

    Cv2.ImWrite("result_edge.png", edgeResult.ResultImage);

}
```

### BlobTool

`BlobToolProperty` provides every required `IOpenCVPropertyBlob` value, so it can be used directly without writing a separate configuration class. If your application needs its own persistence model, it can instead implement the existing interface.

```csharp
using OpenVisionLab.Vision2D.Blob;

BlobToolProperty property = new BlobToolProperty();
```

Usage:

```csharp
using System;
using OpenVisionLab.Vision2D.Blob;
using OpenVisionLab.Vision2D.Tool;
using OpenCvSharp;

using (Mat source = Cv2.ImRead("docs/samples/vision_sample.png", ImreadModes.Grayscale))
{
    using BlobTool tool = new BlobTool();
    tool.SetProperty(new BlobToolProperty
    {
        USE_THRESHOLD = true,
        THRESHOLD = 120,
        MIN_AREA = 50,
        MAX_AREA = 5000,
        USE_ROI = true,
        CvROI = new Rect(100, 100, 300, 200)
    });

    using VisionToolResult result = tool.Execute(source);
    if (!result.Success)
    {
        throw new Exception(result.Message);
    }

    foreach (BlobResult blob in tool.results)
    {
        Console.WriteLine($"#{blob.Index}, Area={blob.Area}, Center={blob.Center}");
    }

}
```

## Pipeline Usage

Pipelines execute multiple tools sequentially through named layers.

`VisionPipelineToolFactory` owns 14 canonical Tool IDs: `threshold`, `morphology`,
`filter`, `edgeDetection`, `rotateScale`, `affineTransform`, `contour`, `corner`,
`matching`, `edgeBasedTemplateMatching`, `autoMPoint`, `sift`, `lineGauge`, and
`mean`. `VisionPipelineBlobToolFactory` composes `blob` with those 14. Their
immutable `Descriptors` catalogs expose aliases, package/type identity, parameters,
invariant defaults/value kinds, and artifact requirements without reflection or a
global registry.

Pipeline configuration fails closed:

- `VisionPipeline.SchemaVersion` defaults to version 2. Use
  `VisionPipelineSerializer.Serialize` and `Deserialize` for the SDK-owned in-memory
  XML contract. Explicit version 1 and original unversioned XML remain readable;
  version 1 rejects artifact references. The host owns file or database persistence.
- Omitted built-in tool parameters use documented defaults. Supplied values must be finite and valid for their declared type.
- Unknown, empty, or case-insensitive duplicate parameter names are rejected with `ArgumentException` before tool execution.
- `MatchingTool`, `EdgeBasedTemplateMatchingTool`, and `SiftTool` require one
  schema 2 `template` artifact with a stable host ID, `encoded-image` format version
  1, and SHA-256. `Create(step, resolver)` validates metadata and returned bytes
  before decoding; Pipeline XML stores no host path or model bytes.
- Empty and disabled-only pipelines return `Success == false`; a pipeline must execute at least one enabled step to pass.
- `UseAcceptance = true` makes the acceptance contract authoritative. Metric values and active metric/time limits must be finite; `ExpectedSuccess = false` is supported only on the final enabled step and never creates a synthetic output layer.
- `MaxElapsedMilliseconds` is a post-execution acceptance limit. It does not abort a
  Tool call.
- `MatchingTool`, `EdgeBasedTemplateMatchingTool`, `AutoMPointTool`, and `SiftTool`
  implement `ICancellableVisionTool`. The token overloads of `Run` and
  `RunWithFailureResults` use that contract and stop before later steps.

Example:

```csharp
using OpenVisionLab.Vision2D.Pipeline;
using OpenVisionLab.Vision2D.Property;
using OpenCvSharp;

VisionPipeline pipeline = new VisionPipeline
{
    Name = "Preprocess"
};

VisionPipelineStep threshold = new VisionPipelineStep
{
    Name = "Binary",
    ToolType = "threshold",
    InputLayer = "input",
    OutputLayer = "binary"
};

threshold.Parameters[nameof(ThresholdToolProperty.Mode)] = "Threshold";
threshold.Parameters[nameof(ThresholdToolProperty.Threshold)] = "120";
threshold.Parameters[nameof(ThresholdToolProperty.MaxValue)] = "255";

pipeline.Steps.Add(threshold);
string pipelineXml = VisionPipelineSerializer.Serialize(pipeline);
pipeline = VisionPipelineSerializer.Deserialize(pipelineXml);

using (Mat source = Cv2.ImRead("docs/samples/vision_sample.png", ImreadModes.Color))
using (VisionPipelineContext context = new VisionPipelineContext())
{
    context.SetLayer("input", source);

    VisionPipelineRuntime runtime = new VisionPipelineRuntime();
    using VisionPipelineRunResult runResult = runtime.RunWithFailureResults(pipeline, context);

    if (!runResult.Success)
    {
        VisionPipelineStepResult failed = runResult.StepResults[runResult.StepResults.Count - 1];
        throw new Exception(failed.ToolResult?.Message ?? failed.AcceptanceMessage);
    }

    using (Mat binary = context.GetLayer("binary"))
    {
        Cv2.ImWrite("result_pipeline.png", binary);
    }
}
```

`Run` preserves the original 3.x exception contract. `RunWithFailureResults` is the
additive host boundary: a missing layer, factory exception/null return, or throwing/
null custom Tool result becomes a typed failed step. Invalid Pipeline definitions
still throw before execution. See the
[Vision2D execution contract](./src/OpenVisionLab.Vision2D/README.md#pipeline-descriptors-and-tool-reconstruction).
The [Vision2D package guide](./src/OpenVisionLab.Vision2D/README.md#pipeline-descriptors-and-tool-reconstruction)
contains the resolver, parameter-format, integrity, and ownership example.

Both methods also have `CancellationToken` overloads. Cancellable `Run` propagates
`OperationCanceledException`. Cancellable `RunWithFailureResults` records one
`StepCanceled` result with status `Canceled`; both forms dispose any unreturned
step results and stop before a later step. Cancellation is cooperative and cannot
interrupt a native OpenCV call already in progress.

## Native Image Resource Ownership

- The caller continues to own the input `Mat` passed to either `Execute` overload. Neither a tool nor a runner disposes this input.
- An `OpenCvAlgorithmBase`-based tool owns its internal source, result, and template copies, so dispose the tool after use.
- `VisionToolResult` owns `ResultImage`. Call `VisionToolResult.Dispose()` after consuming the result, and do not use an existing `ResultImage` reference afterward.
- `VisionPipelineContext.SetLayer` stores a clone of the input image. `GetLayer` returns a new copy that the caller must dispose.
- `VisionPipelineRunResult.Dispose()` disposes every step's `VisionToolResult` and result image. The default runtime also disposes tools created by the default factory.
- For compatibility, `VisionPipelineRuntime(factory)` keeps tools created by a custom factory under caller ownership. Use `VisionPipelineRuntime(factory, true)` if the runtime should own those tools.
- `CombinedInspectionRunResult.Dispose()` disposes only its contained 2D result images. The caller owns the input `Image`, `HeightMap`, and supplied tools.

## Inspecting Results

Primary `VisionToolResult` fields:

| Field | Meaning |
| --- | --- |
| `Success` | Whether tool execution succeeded |
| `Message` | Failure or validation message |
| `ErrorCode`, `ErrorName` | Error code and name identifying the failure cause |
| `ResultStatus` | Status such as `Passed`, `InvalidInput`, `InvalidParameter`, `InvalidRoi`, or `Exception` |
| `ResultImage` | Result image after tool execution |
| `Elapsed` | Execution time |
| `Metrics` | Numeric information such as result count, image dimensions, area, score, and angle |
| `Overlays` | Overlay information such as rectangles, points, and lines for UI display |

## Displaying Detection Results

Inspection applications often need to display tool results immediately. To avoid a direct UI-framework dependency, this library provides `Mat` output and `VisionToolResult.Overlays`.

Recommended flow:

1. Pass the source-image `Mat` to the tool.
2. Receive a `VisionToolResult`.
3. Clone the source image for display and draw the `Overlays` on the clone.
4. In the UI project, use a framework-specific adapter to convert the display `Mat` into the type required by the screen control. SDK Core does not provide WinForms/WPF image types or conversion APIs.

### Example Detection Images

The following synthetic captures illustrate Edge, Matching, Edge-Based Matching,
Contour, Blob, and LineGauge output. They use scenes other than the README sample
image and have no tracked generator or parameter manifest, so do not use them as
reproducible verification evidence.

<table>
  <tr>
    <th>Edge Detection</th>
    <th>Matching</th>
    <th>Edge-Based Matching</th>
  </tr>
  <tr>
    <td><img src="./docs/images/edge_detection_result.png" alt="Edge Detection result" width="280"></td>
    <td><img src="./docs/images/matching_detection_result.png" alt="Template Matching result" width="280"></td>
    <td><img src="./docs/images/edge_based_matching_result.png" alt="Edge-Based Matching result" width="280"></td>
  </tr>
  <tr>
    <th>Contour</th>
    <th>Blob</th>
    <th>LineGauge</th>
  </tr>
  <tr>
    <td><img src="./docs/images/contour_detection_result.png" alt="Contour detection result" width="280"></td>
    <td><img src="./docs/images/blob_detection_result.png" alt="Blob detection result" width="280"></td>
    <td><img src="./docs/images/line_gauge_result.png" alt="LineGauge result" width="280"></td>
  </tr>
</table>

### Shared Overlay Renderer

`MatchingTool`, `EdgeBasedTemplateMatchingTool`, `ContourTool`, `BlobTool`, and `LineGaugeTool` place rectangle, point, point-list, and line data in `VisionToolResult.Overlays`. Add the following helper to a UI project to display most detection results consistently.

```csharp
using System;
using System.Drawing;
using OpenVisionLab.Vision2D;
using OpenVisionLab.Vision2D.Tool;
using OpenCvSharp;
using CvPoint = OpenCvSharp.Point;

public static class VisionDisplayHelper
{
    public static Mat DrawVisionResult(Mat source, VisionToolResult result)
    {
        if (source == null || source.Empty())
        {
            return new Mat();
        }

        Mat display = source.Clone();
        OpenCvHelper.SetImageChannel3(display);

        if (result == null || !result.Success)
        {
            return display;
        }

        foreach (VisionToolOverlay overlay in result.Overlays)
        {
            DrawOverlay(display, overlay);
        }

        return display;
    }

    private static void DrawOverlay(Mat image, VisionToolOverlay overlay)
    {
        Scalar color = new Scalar(50, 205, 50);

        switch (overlay.Kind)
        {
            case VisionToolOverlayKind.Rectangle:
                DrawRectangle(image, overlay.Bounds, color);
                DrawText(image, overlay.Label, overlay.Bounds.X, overlay.Bounds.Y - 6, color);
                if (overlay.Center != PointF.Empty)
                {
                    DrawPoint(image, overlay.Center, Scalar.Yellow);
                }
                break;

            case VisionToolOverlayKind.Point:
                DrawPoint(image, overlay.Center, color);
                DrawText(image, overlay.Label, overlay.Center.X + 5, overlay.Center.Y - 5, color);
                break;

            case VisionToolOverlayKind.Points:
                foreach (PointF point in overlay.Points)
                {
                    DrawPoint(image, point, Scalar.Yellow, 2);
                }
                DrawText(image, overlay.Label, overlay.Center.X + 5, overlay.Center.Y - 5, color);
                break;

            case VisionToolOverlayKind.Line:
                Scalar lineColor = new Scalar(255, 191, 0);
                Cv2.Line(image, ToCvPoint(overlay.Start), ToCvPoint(overlay.End), lineColor, 2, LineTypes.AntiAlias);
                DrawText(image, overlay.Label, overlay.Center.X + 5, overlay.Center.Y - 5, lineColor);
                break;
        }
    }

    private static void DrawRectangle(Mat image, RectangleF bounds, Scalar color)
    {
        Rect rect = new Rect(
            (int)Math.Round(bounds.X),
            (int)Math.Round(bounds.Y),
            Math.Max(1, (int)Math.Round(bounds.Width)),
            Math.Max(1, (int)Math.Round(bounds.Height)));

        Cv2.Rectangle(image, rect, color, 2, LineTypes.AntiAlias);
    }

    private static void DrawPoint(Mat image, PointF point, Scalar color, int radius = 4)
    {
        Cv2.Circle(image, ToCvPoint(point), radius, color, Cv2.FILLED, LineTypes.AntiAlias);
    }

    private static void DrawText(Mat image, string text, float x, float y, Scalar color)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        Cv2.PutText(
            image,
            text,
            new CvPoint(Math.Max(0, (int)Math.Round(x)), Math.Max(15, (int)Math.Round(y))),
            HersheyFonts.HersheySimplex,
            0.45,
            color,
            1,
            LineTypes.AntiAlias);
    }

    private static CvPoint ToCvPoint(PointF point)
    {
        return new CvPoint((int)Math.Round(point.X), (int)Math.Round(point.Y));
    }
}
```

Usage:

```csharp
using VisionToolResult result = tool.Execute(source);

using (Mat display = VisionDisplayHelper.DrawVisionResult(source, result))
{
    Cv2.ImWrite("display_result.png", display);

    // If a UI is required, convert the display Mat in the consumer project's framework-specific adapter.
}
```

### Display Rules by Tool

| Tool | Display Method |
| --- | --- |
| `EdgeDetectionTool` | `result.ResultImage` is the edge image. Display it directly, or call `OpenCvHelper.SetImageChannel3` and add color rendering if needed. |
| `MatchingTool` | `tool.results` contains `MatchingResult` entries, while `result.Overlays` contains match rectangles, center points, and score labels. Use the shared overlay renderer. |
| `EdgeBasedTemplateMatchingTool` | Uses the same `MatchingResult` structure as `MatchingTool`. When `USE_DRAW_IMAGE = true`, the tool draws the edge-model outline on `ResultImage`. |
| `ContourTool` | When `USE_DRAW_IMAGE = true`, contours are drawn on `ResultImage`. Use the shared overlay renderer when the UI needs a consistent style. |
| `BlobTool` | `tool.results` contains `BlobResult` entries, while `result.Overlays` contains bounding, center, and area data. Use the shared overlay renderer. |
| `LineGaugeTool` | `tool.resultList` contains the fitted line and edge list, while `result.Overlays` contains edge points and the fitted line. Use the shared overlay renderer. |

### Matching / EdgeBasedMatching Display Example

```csharp
using OpenVisionLab.Vision2D.Result;
using OpenVisionLab.Vision2D.Property;
using OpenVisionLab.Vision2D.Tool;
using OpenCvSharp;

using MatchingTool tool = new MatchingTool();
tool.SetProperty(new MatchingToolProperty
{
    USE_FIND_ANGLE = false,
    NUM_MATCH = 1
});
tool.SetTemplateImage(template);

using VisionToolResult result = tool.Execute(source);

using (Mat display = VisionDisplayHelper.DrawVisionResult(source, result))
{
    Cv2.ImWrite("display_matching.png", display);
}

foreach (MatchingResult match in tool.results)
{
    Console.WriteLine($"#{match.Index}, Score={match.Score:0.000}, Center={match.Center}, Angle={match.Angle:0.00}, Scale={match.Scale:0.000}");
}
```

Edge-based matching uses the same display approach.

```csharp
using OpenVisionLab.Vision2D.Property;
using OpenVisionLab.Vision2D.Tool;

using EdgeBasedTemplateMatchingTool tool = new EdgeBasedTemplateMatchingTool();
tool.SetProperty(new EdgeBasedTemplateMatchingToolProperty());
tool.SetTemplateImage(template);

using VisionToolResult result = tool.Execute(source);

using (Mat display = VisionDisplayHelper.DrawVisionResult(source, result))
{
    Cv2.ImWrite("display_edge_matching.png", display);
}
```

### Contour / Blob Display Example

```csharp
using OpenVisionLab.Vision2D.Property;
using OpenVisionLab.Vision2D.Tool;

using ContourTool contourTool = new ContourTool();
contourTool.SetProperty(new ContourToolProperty
{
    MIN_AREA = 50,
    MAX_AREA = 5000
});

using VisionToolResult contourResult = contourTool.Execute(source);

using (Mat contourDisplay = VisionDisplayHelper.DrawVisionResult(source, contourResult))
{
    Cv2.ImWrite("display_contour.png", contourDisplay);
}
```

```csharp
using OpenVisionLab.Vision2D.Blob;

using BlobTool blobTool = new BlobTool();
blobTool.SetProperty(new BlobToolProperty
{
    MIN_AREA = 50,
    MAX_AREA = 5000
});

using VisionToolResult blobResult = blobTool.Execute(source);

using (Mat blobDisplay = VisionDisplayHelper.DrawVisionResult(source, blobResult))
{
    Cv2.ImWrite("display_blob.png", blobDisplay);
}
```

### LineGauge Display Example

```csharp
using OpenCvSharp;
using OpenVisionLab.Vision2D.Property;
using OpenVisionLab.Vision2D.Tool;

using LineGaugeTool lineTool = new LineGaugeTool();
lineTool.SetProperty(new LineGaugeToolProperty
{
    CvROI = new Rect(100, 100, 300, 200)
});

using VisionToolResult lineResult = lineTool.Execute(source);

using (Mat lineDisplay = VisionDisplayHelper.DrawVisionResult(source, lineResult))
{
    Cv2.ImWrite("display_line_gauge.png", lineDisplay);
}

foreach (var item in lineTool.resultList)
{
    Console.WriteLine($"#{item.Index}, EdgeCount={item.EdgePointCount}, FitLine={item.FitLine.Start}->{item.FitLine.End}");
}
```

## ROI and Preprocessing Rules

Tools that implement `IOpenCVPropertyBase` can use the shared preprocessing options.

- `USE_ROI`: Use a single ROI
- `USE_MULTI_ROI`: Use multiple ROIs
- `CvROI`: Single ROI
- `CvROIS`: List of multiple ROIs
- `CvMASKS`: Regions excluded from results
- `USE_THRESHOLD`: Apply Threshold before execution
- `USE_ADAPTIVE_THRESHOLD`: Apply Adaptive Threshold before execution
- `USE_BITWISENOT`: Invert black and white

When an ROI has zero width or height, the tool either substitutes the full image or fails, depending on its contract. Tools that require an ROI, such as `LineGaugeTool`, must receive a valid `CvROI` or `CvROIS`. The modern `LineGaugeTool` accepts only `CV_8U` input depth; supported multi-channel input is converted to grayscale single-channel data, while another depth fails with `InputImageInvalid`.

## Legacy API

The `CV*` and `C*` class families remain for existing-code compatibility.

Examples:

- `CVBlob`, `CResultBlob`
- `CVMatching`, `CResultMatching`
- `CVLineGuage`, `CVLineGuage_Result`
- `COpenCVAlgorithmBase`
- `COpenCVHelper`

New code should use APIs based on `BlobTool`, `MatchingTool`, `LineGaugeTool`, `OpenCvAlgorithmBase`, and `VisionToolResult` whenever possible. Legacy APIs remain available for existing application compatibility.

These APIs remain available throughout 3.x.

## Known Limitations

- Windows x64 is the primary supported environment. `OpenCvSharpExtern.dll` is packaged under `runtimes/win-x64/native`.
- No UI framework is included. Applications must render `VisionToolResult.ResultImage` and `VisionToolResult.Overlays` themselves.
- Selected legacy APIs in the `CV*` and `C*` families remain for compatibility. New code should use `*Tool` and `VisionToolResult`-based APIs.
- `OpenVisionLab.Inspection.Smoke` is a synthetic-data contract regression suite; it does not establish real sensor, calibration, or production metrology performance.
- Omitting `HeightMapInputRequirements` enables 2.x compatibility mode, which validates only numerical values and ROIs. Production recipes must declare the expected units and frame.
- OpenCvSharp operates against the version included in the repository. When replacing its DLLs, verify native-DLL compatibility and packaging output together.

## Packaging Notes

[`Directory.Build.props`](./Directory.Build.props) owns the source, package and
assembly version settings. CI uses a unique prerelease package version for each
run. Keep all six packages on one compatible version and do not replace shared or
published bytes under an existing package ID/version.

The package contents, required files, internal dependencies and source commit are
checked by [`Verify-PackageProvenance.ps1`](./eng/Verify-PackageProvenance.ps1).
[`OpenVisionLab.PackageConsumer.Smoke`](./tests/OpenVisionLab.PackageConsumer.Smoke)
checks the package-only consumer path. A source build or CI package check does not
establish that a package has been published.

Each NuGet package includes a dedicated README for its specific role and first-use workflow.

| Package | Package README |
| --- | --- |
| `OpenVisionLab.Compute` | [Optional CUDA sessions and CPU fallback](./src/OpenVisionLab.Compute/README.md) |
| `OpenVisionLab.Core` | [Native runtime and shared support](./src/OpenVisionLab.Core/README.md) |
| `OpenVisionLab.Vision2D` | [2D Tool Quick Start](./src/OpenVisionLab.Vision2D/README.md) |
| `OpenVisionLab.Vision2D.Blob` | [Blob Tool contract](./src/OpenVisionLab.Vision2D.Blob/README.md) |
| `OpenVisionLab.Vision3D` | [Surface Match and Mesh Quick Start](./src/OpenVisionLab.Vision3D/README.md) |
| `OpenVisionLab.Inspection` | [Combined 2D/3D execution Quick Start](./src/OpenVisionLab.Inspection/README.md) |

`OpenVisionLab.Core` packages `OpenCvSharpExtern.dll` under
`runtimes/win-x64/native`. Modern SDK-style `win-x64` consumers resolve that runtime
asset to the output root. `buildTransitive/OpenVisionLab.Core.targets` is a
`.NET Framework` fallback only; its source contract is reviewed, but no .NET
Framework runtime consumer has been executed.

The Core package also carries `third-party/provenance.json`, the current
`third-party/NOTICE.md`, and the exact upstream license/scope evidence named by
that manifest. The package provenance verifier requires those files and both
vendored DLLs to be byte-identical to the reviewed repository sources; the other
five packages must not contain Core's vendored DLL or `third-party/` entries. This
technical gate does not change the blocked redistribution-clearance status.

GitHub Actions separately restores and runs
`tests/OpenVisionLab.PackageConsumer.Smoke`, which references only the packed output. This check verifies that 2D native calls, height-map inspection, Surface Match, and Mesh Comparison work without a ProjectReference.
