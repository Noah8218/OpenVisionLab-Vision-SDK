# OpenVisionLab Vision SDK

Use 2D inspection tools and height-map / full-XYZ 3D algorithms in your own C# application.

[![Source v3.2.0-rc.5](https://img.shields.io/badge/source-v3.2.0--rc.5-blue)](Directory.Build.props) [![Libraries .NET Standard 2.0](https://img.shields.io/badge/libraries-.NET_Standard_2.0-512BD4)](src/OpenVisionLab.Vision3D/OpenVisionLab.Vision3D.csproj) [![Native runtime Windows x64](https://img.shields.io/badge/native_runtime-Windows_x64-0078D6)](src/OpenVisionLab.Core/OpenVisionLab.Core.csproj)

**English** · [한국어](README.ko.md)

The SDK returns images, measurements and result evidence. Your application owns
acquisition, display, ROI editing, recipe storage and the final equipment decision.

## A small inspection

```csharp
using System;
using OpenCvSharp;
using OpenVisionLab.Vision2D;
using OpenVisionLab.Vision2D.Property;
using OpenVisionLab.Vision2D.Tool;

using Mat source = new Mat(16, 16, MatType.CV_8UC1, new Scalar(100));
using ThresholdTool tool = new ThresholdTool();
tool.SetProperty(new ThresholdToolProperty { Threshold = 50 });
using VisionToolResult result = tool.Execute(source);
if (!result.Success) throw new InvalidOperationException($"{result.ErrorName}: {result.Message}");
Console.WriteLine($"{result.ResultImage.Width}x{result.ResultImage.Height}");
```

Expected output after configuring the managed/native dependencies: `16x16`.
The default binary threshold produces a white image for this constant input.
The caller owns `source`; the result owns its output image. Dispose both and the tool.

## Start in three steps

1. **Prepare a Windows x64 environment.** Source builds select .NET SDK `8.0.423`
   through [global.json](global.json); verification scripts need PowerShell 7.
   The libraries target `netstandard2.0`. The SDK version used to build them is
   separate from a consumer's target framework.
2. **Reference the modules you need.** Use an exact immutable package version
   from your selected feed and keep the SDK packages on one compatible version.
   Start with the [Vision2D package guide](src/OpenVisionLab.Vision2D/README.md).
   For source references, follow the [managed/native reference example](docs/sdk-reference.md#installation-and-references);
   a project reference alone does not supply the complete OpenCvSharp runtime.
3. **Run and inspect the result.** Try the in-memory example above, then follow
   the [first inspection workflow](#first-inspection-example) or the
   [3D quick start](docs/sdk-reference.md#3d-quick-start). Check errors, metrics,
   units and ownership before connecting the result to your application's judgment.

This checkout's source version is **`3.2.0-rc.5`**, a prerelease defined in
[Directory.Build.props](Directory.Build.props); CLR assembly identity remains
`3.0.0.0`. A source version does not establish a published NuGet package or binary
release. Read the [version history](docs/sdk-reference.md#version) and
[2.9.1 → 3.0 migration guide](docs/MIGRATING_LIB_2_9_1_TO_OPENVISIONLAB_3_0.md)
before updating an existing application.

## Core capabilities

<a id="blobtool"></a>

| Need | Module and contract |
| --- | --- |
| Threshold, filters, edges, contours, matching, LineGauge and QR reading | [Vision2D](src/OpenVisionLab.Vision2D/README.md) |
| Connected objects and area filtering | [Vision2D.Blob](src/OpenVisionLab.Vision2D.Blob/README.md) |
| Height-map measurement, full-XYZ geometry, filtering, registration and surface/mesh tools | [Vision3D](src/OpenVisionLab.Vision3D/README.md) · [tool catalog](docs/three-d-inspection.md#public-tool-catalog) |
| Sequential 2D and single-height-map inspection with original results | [Inspection](src/OpenVisionLab.Inspection/README.md) |
| Coordinate/line calculations and OpenCV runtime assets | [Core](src/OpenVisionLab.Core/README.md) |
| Optional CUDA sessions and per-execution fallback evidence | [Compute](src/OpenVisionLab.Compute/README.md) |

CPU is the default. Optional CUDA applies to selected edge-based matching scores
and deterministic 3D Median/LocalMedian operations. Normal preparation, Scharr and
3D surface-pose search retain CPU execution. The compatible native CUDA module is
separate; device discovery alone does not prove acceleration. Read each result's
`AccelerationApplied` and `FallbackReason` and the [Compute contracts](src/OpenVisionLab.Compute/README.md#local-operations).

## Inspection workflow

```text
Host acquires input → configure/teach → execute explicitly
    → inspect errors, metrics and evidence → Host decides and displays → dispose
```

For 2D, configure a Tool and call `Execute(Mat)`. Preserve the caller's input and
consume `VisionToolResult` before disposal. Serialize settings, execution and
disposal on one instance. [Pipeline XML](docs/sdk-reference.md#pipeline-usage)
stores configuration; the host owns files, databases and artifact resolution.

For 3D, choose a height map, full-XYZ typed input or multi-input measurement from
the [3D contract](docs/three-d-inspection.md). Declare units, frame and coverage.
Height-map `NotMeasured` and a typed tool's `Success`/`Passed` have distinct meanings;
multi-input and mesh tools run directly rather than through `IThreeDInspectionTool`.

### First inspection example

The [package-only console sample](samples/OpenVisionLab.FirstInspection/Program.cs)
connects teaching, datum location, translated ROIs, presence and a pixel edge
measurement: **prepare → teach → inspect → load → inspect again**.
Use its [full commands and result/exit-code contract](docs/sdk-reference.md#first-inspection-example)
with an exact package version and feed. `load` validates and restores without
inspection or a CUDA probe. The example's `Accepted` state is bounded evidence;
your application retains the final equipment decision.

<a id="qr-configure-restore-and-explicit-inspection"></a>

The same sample's [QR workflow](docs/sdk-reference.md#qr-configure-restore-and-explicit-inspection)
adds **configure → load → inspect**, including located ROIs, expected text and
failure recovery. QR reading success and the host's text comparison are separate.

## Build from source

<a id="clone-and-develop-on-another-pc"></a>

Use a short checkout path on Windows x64 and the tools selected above. From the
repository root, choose an output directory outside the source tree:

```powershell
dotnet --version
dotnet tool restore
$outputRoot = Read-Host "Absolute directory for build output"
dotnet build OpenVisionLab.VisionSdk.sln -c Release --artifacts-path "$outputRoot/build"
if ($LASTEXITCODE -ne 0) { throw "Build failed." }
$smoke = "$outputRoot/build/bin/OpenVisionLab.Inspection.Smoke/release/OpenVisionLab.Inspection.Smoke.dll"
dotnet $smoke
```

The smoke executable checks synthetic contracts and regressions. Windows Server
also needs Media Foundation for the bundled OpenCV runtime. See the
[complete source setup](docs/sdk-reference.md#clone-and-develop-on-another-pc),
[developer Start Here](docs/README.md#start-here) and
[CI workflow](.github/workflows/build.yml) for focused/API/package checks.
The workflow defines checks; it does not publish packages or create releases.

## Detailed documentation

<a id="2d-quick-start"></a>
<a id="packaging-notes"></a>

| Topic | Guide |
| --- | --- |
| 2D Canny and 3D thickness examples | [2D quick start](docs/sdk-reference.md#2d-quick-start) · [3D quick start](docs/sdk-reference.md#3d-quick-start) |
| Tool catalog, properties, Pipeline, overlays and native image lifetime | [SDK reference](docs/sdk-reference.md#2d-tool-execution-model) |
| Input, channel, ROI, error, cancellation and recovery contracts | [2D contracts](src/OpenVisionLab.Vision2D/README.md#input-and-option-contracts) · [3D contracts](docs/three-d-inspection.md#execution-failure-and-lifetime-boundaries) |
| Matching and object evidence | [Auto MPoint](docs/AUTO_MPOINT_V1.md) · [unique edge match](docs/EDGE_BASED_UNIQUE_MATCH_V1.md) · [Blob/Contour candidates](docs/OBJECT_CANDIDATE_CONTRACT.md) |
| Version history, project structure, legacy APIs and packaging | [Complete reference](docs/sdk-reference.md) · [packaging notes](docs/sdk-reference.md#packaging-notes) |
| Build, native dependencies and migration | [Source setup](docs/sdk-reference.md#clone-and-develop-on-another-pc) · [references](docs/sdk-reference.md#installation-and-references) · [migration](docs/MIGRATING_LIB_2_9_1_TO_OPENVISIONLAB_3_0.md) |

Package-specific READMEs remain with their projects and are included in their
packages. The [documentation index](docs/README.md) retains the existing developer
reading order. The detailed reference preserves the former root README's API,
build, migration and version information.

## Limits and validation scope

- Windows x64 is the primary native-runtime environment. `netstandard2.0` does not
  establish support for every OS or host; existing package-consumer checks cover
  .NET 8 and .NET Framework 4.8/x64.
- The SDK supplies computation and evidence. Acquisition, rendering, recipe
  persistence, calibration approval and production acceptance belong to the host.
- Synthetic smoke checks do not establish sensor accuracy, Gauge R&R, customer
  tolerances or production takt time. The [existing result images](docs/sdk-reference.md#example-detection-images)
  are synthetic illustrations without a tracked reproducibility manifest.
- Cancellation is cooperative. A timeout does not forcibly interrupt native work.
  Share a Compute session only with its documented serial execution/lifetime rules.
- Height maps require explicit unit/frame/coverage contracts. Compatibility mode
  without `HeightMapInputRequirements` is not a production recipe declaration.
- Selected `CV*`/`C*` legacy APIs remain available in 3.x. The bundled native runtime
  makes `SiftTool` use its documented ORB fallback; inspect detector evidence.

## Ecosystem

| Application | Integration boundary |
| --- | --- |
| [OpenVisionLab](https://github.com/Noah8218/OpenVisionLab) | 2D workbench for tool, layer, Pipeline and result-display workflows. |
| [OpenVisionLab 3D Studio](https://github.com/Noah8218/OpenVisionLab-3D-Studio) | 3D workbench using a pinned SDK package and explicit adapters. |

Applications select their own package versions and integration code. A change in
this SDK checkout does not automatically update a companion application.

## License and redistribution

OpenVisionLab-authored portions use the [MIT License](LICENSE), with copyright
and attribution preserved as required by [NOTICE](NOTICE).

**Commercial redistribution of Core's bundled OpenCvSharp/OpenCV binaries remains
blocked pending distribution/legal-owner approval** of the final notice bundle,
Intel notice conditions and distribution workflow. The [Core notice](src/OpenVisionLab.Core/ThirdParty/NOTICE.md),
[binary provenance](src/OpenVisionLab.Core/ThirdParty/provenance.json) and
[clearance checklist](docs/THIRD_PARTY_REDISTRIBUTION_CLEARANCE_CHECKLIST.md) define
the exact scope. MIT labeling and technical provenance checks do not grant that
approval.
