# OpenVisionLab Vision SDK

> **3.0 naming change:** `Library-Noah` and `Lib.* 2.9.1` remain available as
> the compatibility baseline for existing consumers. This source builds the
> `OpenVisionLab.* 3.0` packages, DLLs, and namespaces. Before migrating an
> existing project, read the
> [2.9.1 to 3.0.0 migration guide](docs/MIGRATING_LIB_2_9_1_TO_OPENVISIONLAB_3_0.md).

OpenVisionLab Vision SDK is a C# vision inspection library for OpenCvSharp-based 2D inspection and UI-independent height-map/full-XYZ 3D computation.

It provides application-ready 2D image-processing tools, 3D feature extraction and measurement algorithms, and shared result states and metrics.

## 1-Minute Overview

- `OpenVisionLab.Core` provides UI-independent coordinate and line calculations and packages the native OpenCV DLL.
- `OpenVisionLab.Vision2D` provides primary inspection tools including Threshold, Filter, Edge, Contour, Matching, and LineGauge.
- `OpenVisionLab.Vision2D.Blob` provides Blob labeling and area filtering.
- `OpenVisionLab.Vision3D` provides UI-independent 3D contracts and algorithms for height maps, connected-region labeling/metrics/presence, full-XYZ geometry, rigid point-pair alignment, affine/regrid operations, thickness, warpage, flatness, gap/flush, volume, and more.
- `OpenVisionLab.Inspection` preserves existing 2D tools and `IThreeDInspectionTool` results in one combined run result.
- Run 2D tools with `Execute(Mat source)` and height-map inspection tools with `Execute(HeightMap3D source)`.
- The SDK has no direct UI-framework dependency. The host application owns rendering, ROI editing, and recipe management around the measurements.

## Installation and References

To reference the source projects directly, add only the projects required by your application.

```xml
<ItemGroup>
  <ProjectReference Include="..\OpenVisionLab-Vision-SDK\src\OpenVisionLab.Vision2D\OpenVisionLab.Vision2D.csproj" />
  <ProjectReference Include="..\OpenVisionLab-Vision-SDK\src\OpenVisionLab.Vision2D.Blob\OpenVisionLab.Vision2D.Blob.csproj" />
  <ProjectReference Include="..\OpenVisionLab-Vision-SDK\src\OpenVisionLab.Vision3D\OpenVisionLab.Vision3D.csproj" />
  <ProjectReference Include="..\OpenVisionLab-Vision-SDK\src\OpenVisionLab.Inspection\OpenVisionLab.Inspection.csproj" />
</ItemGroup>
```

To use local NuGet packages, assign a unique prerelease version, build the packages, and then add `artifacts/packages` as a package source. Never reuse the example version after changing package content.

```powershell
$packageVersion = "3.0.1-dev.20260821.1"
dotnet pack OpenVisionLab.VisionSdk.sln -c Release "-p:PackageVersion=$packageVersion"
dotnet add package OpenVisionLab.Vision2D --version $packageVersion --source .\artifacts\packages
dotnet add package OpenVisionLab.Vision2D.Blob --version $packageVersion --source .\artifacts\packages
dotnet add package OpenVisionLab.Vision3D --version $packageVersion --source .\artifacts\packages
dotnet add package OpenVisionLab.Inspection --version $packageVersion --source .\artifacts\packages
```

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

`MeasurementOutcome` distinguishes `Passed`, `OutOfTolerance`, and `NotMeasured` directly. The former combination of `Success=false` and `HasMeasurement=true` maps to `OutOfTolerance`. Unit or frame mismatches, invalid ROIs, insufficient samples, and insufficient coverage map to `NotMeasured`. See [3D inspection](docs/three-d-inspection.md) for the complete contract.

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

The basic examples assume execution from the repository root and use `docs/samples/vision_sample.png`. When running elsewhere, adjust the image path relative to the executable.

## Matching Contract References

- Auto MPoint teaching core: `docs/AUTO_MPOINT_V1.md`
- Edge-based fail-closed unique result: `docs/EDGE_BASED_UNIQUE_MATCH_V1.md`
- Matching responsibility boundaries and production-baseline plan: `docs/MATCHING_RESPONSIBILITY_AND_PRODUCTION_BASELINE_PLAN_20260821.md`
- Versioned synthetic accuracy/performance baseline and paired protocol v2: `docs/OPENVISIONLAB_VISION_SDK_IDENTITY_AND_V3_MIGRATION_PLAN_20260805.md` sections 27-28

## 2D object candidate evidence

Blob and Contour expose the additive, single-pass candidate contract described
in [`docs/OBJECT_CANDIDATE_CONTRACT.md`](docs/OBJECT_CANDIDATE_CONTRACT.md).

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
$packageVersion = "3.0.1-dev.20260821.1"
dotnet pack OpenVisionLab.VisionSdk.sln -c Debug --no-build "-p:PackageVersion=$packageVersion"
```

`OpenVisionLab.Inspection.Smoke` checks deterministic contracts and regressions with synthetic 2D and 3D inputs. It does not replace real sensor data, calibration, Gauge R&amp;R, or production-approval testing.

## CI

The GitHub Actions workflow is defined in `.github/workflows/build.yml`. It performs the following steps for pushes to `main` and for pull requests.

1. Install the .NET SDK
2. `dotnet restore OpenVisionLab.VisionSdk.sln`
3. `dotnet build OpenVisionLab.VisionSdk.sln -c Release --no-restore`
4. `dotnet run --project tests/OpenVisionLab.Inspection.Smoke/OpenVisionLab.Inspection.Smoke.csproj -c Release --no-build`
5. Pack all five packages with a unique `3.0.1-ci.<run>.<attempt>` version
6. Restore and run the package-only consumer from the packed output and an isolated NuGet cache

## License

This project is distributed under the MIT License. Commercial use, modification, and distribution are permitted, but any use of this project or a substantial portion of its source must retain the copyright notice, license text, and attribution notices in `NOTICE`.

Copyright (c) 2026 Noah Choi (최노아)

- Full license: [LICENSE](LICENSE)
- Attribution notices: [NOTICE](NOTICE)

If redistribution, packaging, or derivative work includes a substantial part of this library, do not remove or obscure `LICENSE` or `NOTICE`.

## Development Environment

- Visual Studio 2022 or the .NET SDK
- C# / .NET Standard 2.0
- Windows runtime recommended
- OpenCvSharp-related DLLs are included under `src/OpenVisionLab.Core/DLL`.

Build:

```powershell
dotnet restore OpenVisionLab.VisionSdk.sln
dotnet build OpenVisionLab.VisionSdk.sln -c Release
```

## Project Layout

```text
OpenVisionLab-Vision-SDK
|- src
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
`- tests
   `- OpenVisionLab.Inspection.Smoke
      |- Suites
      `- Support
```

| Project | Role |
| --- | --- |
| `OpenVisionLab.Core` | UI-independent coordinate/ROI conversion, numerical and geometric calculations, line calculations, and OpenCV runtime assets |
| `OpenVisionLab.Vision2D` | Primary OpenCV inspection tools, property interfaces, result models, and pipeline execution |
| `OpenVisionLab.Vision2D.Blob` | Blob labeling and area-filtering tools |
| `OpenVisionLab.Vision3D` | UI-independent height-map/full-XYZ contracts, feature extraction, and 3D inspection algorithms |
| `OpenVisionLab.Inspection` | Execution contract that runs 2D and 3D tools in sequence while preserving each original result |
| `OpenVisionLab.Inspection.Smoke` | Executable contract and regression checks with synthetic input, separated into an entry point, domain suites, and shared support code |

Reference relationships:

```text
OpenVisionLab.Core
|- OpenVisionLab.Vision2D
|  `- OpenVisionLab.Vision2D.Blob
|- OpenVisionLab.Vision3D
`- OpenVisionLab.Inspection
   |- OpenVisionLab.Vision2D
   `- OpenVisionLab.Vision3D
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
   |- ContourTool
   |- CornerTool
   |- MatchingTool
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
| `ContourTool` | Contour detection and area filtering | `ContourToolProperty` or an `IOpenCVPropertyContour` implementation |
| `CornerTool` | Sub-pixel corner detection with global-coordinate results | `ContourToolProperty` or an `IOpenCVPropertyContour` implementation |
| `BlobTool` | Blob labeling and area filtering | `BlobToolProperty` or an `IOpenCVPropertyBlob` implementation |
| `MatchingTool` | Template matching with scale and angle search | `MatchingToolProperty` or an `IOpenCVPropertyMatching` implementation |
| `EdgeBasedTemplateMatchingTool` | Edge-based template matching | `EdgeBasedTemplateMatchingToolProperty` or an `IOpenCVPropertyEdgeBasedTemplateMatching` implementation |
| `AutoMPointTool` | Automatic fixed-size match-candidate proposal with uniqueness, synthetic-transform, and performance checks | `AutoMPointToolProperty` |
| `SiftTool` | SIFT feature-point matching | `SiftToolProperty` or an `IOpenCVPropertyFeatureSIFT` implementation |
| `LineGaugeTool` | Edge detection and line fitting inside an ROI | `LineGaugeToolProperty` or an `IOpenCvPropertyLineGauge` implementation |
| `MeanTool` | ROI mean and standard-deviation calculation | `MeanToolProperty` or an `IOpenCVPropertyMean` implementation |

Multi-ROI execution in `MeanTool` measures each region in `CvROIS` order and returns `MeanResult.index` values in the same order. `CornerTool` returns each sub-pixel-refined point as a `CornerResult` in global image coordinates and returns `CornerNoResult` when no point is detected.

## Supported 3D Features

The 3D API is used through three layers based on input shape. `IThreeDInspectionTool` is intentionally narrow and supports only a single `HeightMap3D` inspection; multi-surface and mesh tools do not implement this interface.

| Layer | Input / Result | When to Use | `CombinedInspectionRunner` |
| --- | --- | --- | --- |
| Height-map inspection | `HeightMap3D` → `ThreeDInspectionResult` | Inspecting one regular grid for thickness, warpage, datum deviation, and similar measurements | Supported |
| Source-neutral tool | Tool-specific typed input/options/result | Full-XYZ geometry, regrid, filtering, matching, and mesh comparison | Not supported; execute the tool directly |
| Multi-input dimensional inspection | Caller-prepared points, regions, or statistics → typed result | Flatness, point pair, gap/flush, volume, and cross-section measurements | Not supported; execute the tool directly |

Height-map inspections return input, ROI, and coverage errors as controlled `NotMeasured` results. Source-neutral and multi-input tools use the `Success` or `Passed` contract of their typed results and may reject an invalid call configuration with `ArgumentException`. See the [3D inspection documentation](docs/three-d-inspection.md#public-tool-catalog) for the complete public tool catalog and input-selection guidance.

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
            ThresholdTool tool = new ThresholdTool();
            tool.SetProperty(new ThresholdToolProperty
            {
                Mode = ThresholdToolMode.Threshold,
                Threshold = 120,
                MaxValue = 255,
                ThresholdType = ThresholdTypes.Binary
            });

            VisionToolResult result = tool.Execute(source);
            if (!result.Success)
            {
                throw new InvalidOperationException($"{result.ErrorName}: {result.Message}");
            }

            Cv2.ImWrite("result_threshold.png", result.ResultImage);
            result.ResultImage?.Dispose();
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
    FilterTool filter = new FilterTool();
    filter.SetProperty(new FilterToolProperty
    {
        FilterType = FilterToolType.GaussianBlur,
        KernelWidth = 5,
        KernelHeight = 5
    });

    VisionToolResult filtered = filter.Execute(source);
    if (!filtered.Success)
    {
        throw new Exception(filtered.Message);
    }

    EdgeDetectionTool edge = new EdgeDetectionTool();
    edge.SetProperty(new EdgeDetectionToolProperty
    {
        EdgeType = EdgeDetectionToolType.Canny,
        CannyThresholdLow = 80,
        CannyThresholdHigh = 160,
        CannyApertureSize = 3
    });

    VisionToolResult edgeResult = edge.Execute(filtered.ResultImage);
    if (!edgeResult.Success)
    {
        throw new Exception(edgeResult.Message);
    }

    Cv2.ImWrite("result_edge.png", edgeResult.ResultImage);

    filtered.ResultImage?.Dispose();
    edgeResult.ResultImage?.Dispose();
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
    BlobTool tool = new BlobTool();
    tool.SetProperty(new BlobToolProperty
    {
        USE_THRESHOLD = true,
        THRESHOLD = 120,
        MIN_AREA = 50,
        MAX_AREA = 5000,
        USE_ROI = true,
        CvROI = new Rect(100, 100, 300, 200)
    });

    VisionToolResult result = tool.Execute(source);
    if (!result.Success)
    {
        throw new Exception(result.Message);
    }

    foreach (BlobResult blob in tool.results)
    {
        Console.WriteLine($"#{blob.Index}, Area={blob.Area}, Center={blob.Center}");
    }

    result.ResultImage?.Dispose();
}
```

## Pipeline Usage

Pipelines execute multiple tools sequentially through named layers.

The default `VisionPipelineToolFactory` currently creates the following tools.

- `threshold`
- `morphology`
- `filter`
- `edge` or `edgedetection`
- `rotatescale`
- `affine`, `affinematrix`, or `affinetransform`

Pipeline configuration fails closed:

- Omitted built-in tool parameters use documented defaults. Supplied values must be finite and valid for their declared type.
- Unknown, empty, or case-insensitive duplicate parameter names are rejected with `ArgumentException` before tool execution.
- Empty and disabled-only pipelines return `Success == false`; a pipeline must execute at least one enabled step to pass.
- `UseAcceptance = true` makes the acceptance contract authoritative. `ExpectedSuccess = false` is supported only on the final enabled step and never creates a synthetic output layer.

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

using (Mat source = Cv2.ImRead("docs/samples/vision_sample.png", ImreadModes.Color))
using (VisionPipelineContext context = new VisionPipelineContext())
{
    context.SetLayer("input", source);

    VisionPipelineRuntime runtime = new VisionPipelineRuntime();
    using VisionPipelineRunResult runResult = runtime.Run(pipeline, context);

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

## Native Image Resource Ownership

- The caller continues to own the input `Mat` passed to `Execute(Mat source)`. Neither a tool nor a runner disposes this input.
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

The following images show Edge, Matching, Edge-Based Matching, Contour, Blob, and LineGauge detection or fitting applied to the README sample image. They provide a quick visual reference for the output displayed by each tool.

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
VisionToolResult result = tool.Execute(source);

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

MatchingTool tool = new MatchingTool();
tool.SetProperty(new MatchingToolProperty
{
    USE_FIND_ANGLE = false,
    NUM_MATCH = 1
});
tool.SetTemplateImage(template);

VisionToolResult result = tool.Execute(source);

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

EdgeBasedTemplateMatchingTool tool = new EdgeBasedTemplateMatchingTool();
tool.SetProperty(new EdgeBasedTemplateMatchingToolProperty());
tool.SetTemplateImage(template);

VisionToolResult result = tool.Execute(source);

using (Mat display = VisionDisplayHelper.DrawVisionResult(source, result))
{
    Cv2.ImWrite("display_edge_matching.png", display);
}
```

### Contour / Blob Display Example

```csharp
using OpenVisionLab.Vision2D.Property;
using OpenVisionLab.Vision2D.Tool;

ContourTool contourTool = new ContourTool();
contourTool.SetProperty(new ContourToolProperty
{
    MIN_AREA = 50,
    MAX_AREA = 5000
});

VisionToolResult contourResult = contourTool.Execute(source);

using (Mat contourDisplay = VisionDisplayHelper.DrawVisionResult(source, contourResult))
{
    Cv2.ImWrite("display_contour.png", contourDisplay);
}
```

```csharp
using OpenVisionLab.Vision2D.Blob;

BlobTool blobTool = new BlobTool();
blobTool.SetProperty(new BlobToolProperty
{
    MIN_AREA = 50,
    MAX_AREA = 5000
});

VisionToolResult blobResult = blobTool.Execute(source);

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

LineGaugeTool lineTool = new LineGaugeTool();
lineTool.SetProperty(new LineGaugeToolProperty
{
    CvROI = new Rect(100, 100, 300, 200)
});

VisionToolResult lineResult = lineTool.Execute(source);

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

When an ROI has zero width or height, the tool either substitutes the full image or fails, depending on its contract. Tools that require an ROI, such as `LineGaugeTool`, must receive a valid `CvROI` or `CvROIS`.

## Legacy API

The `CV*` and `C*` class families remain for existing-code compatibility.

Examples:

- `CVBlob`, `CResultBlob`
- `CVMatching`, `CResultMatching`
- `CVLineGuage`, `CVLineGuage_Result`
- `COpenCVAlgorithmBase`
- `COpenCVHelper`

New code should use APIs based on `BlobTool`, `MatchingTool`, `LineGaugeTool`, `OpenCvAlgorithmBase`, and `VisionToolResult` whenever possible. Legacy APIs remain available for existing application compatibility.

The actual local consumers, replacement types, API differences, and required pre-removal gates for 24 public legacy types are recorded in the
[legacy C/CV/LineGuage usage and 4.0 removal design](docs/LEGACY_C_CV_LINEGUAGE_V4_REMOVAL_PLAN_20260805.md). These APIs remain available throughout 3.x.

## Known Limitations

- Windows x64 is the primary supported environment. `OpenCvSharpExtern.dll` is packaged under `runtimes/win-x64/native`.
- No UI framework is included. Applications must render `VisionToolResult.ResultImage` and `VisionToolResult.Overlays` themselves.
- Selected legacy APIs in the `CV*` and `C*` families remain for compatibility. New code should use `*Tool` and `VisionToolResult`-based APIs.
- `OpenVisionLab.Inspection.Smoke` is a synthetic-data contract regression suite; it does not establish real sensor, calibration, or production metrology performance.
- Omitting `HeightMapInputRequirements` enables 2.x compatibility mode, which validates only numerical values and ROIs. Production recipes must declare the expected units and frame.
- OpenCvSharp operates against the version included in the repository. When replacing its DLLs, verify native-DLL compatibility and packaging output together.

## Packaging Notes

Shared package metadata is defined in `Directory.Build.props`.

- `Version`: `3.0.0` API and assembly baseline
- `PackageVersion`: `3.0.1-dev.1` local-development default; CI overrides it with a unique prerelease version
- `PackageOutputPath`: `artifacts/packages`
- `GeneratePackageOnBuild`: `false`

Ordinary development commits, benchmark-protocol changes, and branch pushes do not change `Version` or the default `PackageVersion`, and do not authorize package publication. Reproducible benchmark runs pin the complete Git commit SHA; package versions change only for a separately approved immutable package candidate or release.

### Immutable package versions

A package ID and version identify exactly one package content. After a package has been shared, consumed, or published, do not rebuild different content under that version. Use one new version for every changed development or CI package, keep all five OpenVisionLab packages on that version, and remove the prerelease suffix only for an approved release.

```text
Development: 3.0.1-dev.20260821.1 -> 3.0.1-dev.20260821.2
CI:          3.0.1-ci.<run>.<attempt>
Release:     3.0.1
Next fix:    3.0.2; never replace the existing 3.0.1 package
```

`3.0.1-dev.1` is only a safe repository default after the former mutable `3.0.0` local packages. Pass a unique `PackageVersion` whenever package content can leave the current build directory. Build metadata such as `+commit` is not used as the uniqueness boundary; use a prerelease suffix.

### Isolated package-only verification

NuGet's global package cache is keyed by package ID and version. An older package with the same version can therefore hide a newly packed file. `RestoreAdditionalProjectSources` alone does not prove that the consumer used the new package. Verify the packed output with a dedicated empty cache and the packed directory as the only package source.

```powershell
$packageVersion = "3.0.1-dev.20260821.1"
$packageRoot = "D:\OpenVisionLab-TestData\OpenVisionLab-Vision-SDK\packages\$packageVersion"
$consumerCache = "D:\OpenVisionLab-TestData\OpenVisionLab-Vision-SDK\package-cache\$packageVersion"

dotnet pack OpenVisionLab.VisionSdk.sln -c Release "-p:PackageVersion=$packageVersion" -o $packageRoot
dotnet restore tests\OpenVisionLab.PackageConsumer.Smoke\OpenVisionLab.PackageConsumer.Smoke.csproj `
  "-p:PackageVersion=$packageVersion" `
  "-p:RestorePackagesPath=$consumerCache" `
  "-p:RestoreSources=$packageRoot"
dotnet run --project tests\OpenVisionLab.PackageConsumer.Smoke\OpenVisionLab.PackageConsumer.Smoke.csproj `
  -c Release --no-restore `
  "-p:PackageVersion=$packageVersion" `
  "-p:RestorePackagesPath=$consumerCache"
```

Use another physical test-data root on a machine without `D:`. Do not clear the machine-wide NuGet cache as the normal verification path.

For a controlled release, record this minimal evidence:

```text
Package version: <immutable version>
Source commit: <full Git commit>
Package SHA-256: <one hash for each of the five nupkg files>
Verification: Release build, Smoke result, isolated package-only consumer result
```

Each NuGet package includes a dedicated README for its specific role and first-use workflow.

| Package | Package README |
| --- | --- |
| `OpenVisionLab.Core` | [Native runtime and shared support](src/OpenVisionLab.Core/README.md) |
| `OpenVisionLab.Vision2D` | [2D Tool Quick Start](src/OpenVisionLab.Vision2D/README.md) |
| `OpenVisionLab.Vision2D.Blob` | [Blob Tool contract](src/OpenVisionLab.Vision2D.Blob/README.md) |
| `OpenVisionLab.Vision3D` | [Surface Match and Mesh Quick Start](src/OpenVisionLab.Vision3D/README.md) |
| `OpenVisionLab.Inspection` | [Combined 2D/3D execution Quick Start](src/OpenVisionLab.Inspection/README.md) |

To create all five packages with one immutable version, pack the solution once.

```powershell
$packageVersion = "3.0.1-dev.20260821.1"
dotnet pack OpenVisionLab.VisionSdk.sln -c Release "-p:PackageVersion=$packageVersion"
```

`OpenVisionLab.Core` packages `OpenCvSharpExtern.dll` under `runtimes/win-x64/native` and copies it to the output directory through `buildTransitive/OpenVisionLab.Core.targets`.

GitHub Actions separately restores and runs
`tests/OpenVisionLab.PackageConsumer.Smoke`, which references only the packed output. This check verifies that 2D native calls, height-map inspection, Surface Match, and Mesh Comparison work without a ProjectReference.
