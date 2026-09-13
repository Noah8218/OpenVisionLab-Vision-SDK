# OpenVisionLab.Inspection

One runner for independent OpenVisionLab 2D tools and height-map `IThreeDInspectionTool` steps.

`3.0.0` is the API/assembly baseline, not the install version. Use the exact
immutable version from your package source; `3.0.1-dev.1` is only the current
repository-local default.

```powershell
$packageVersion = "3.0.1-dev.1" # Replace when pack or your feed uses another version.
dotnet add package OpenVisionLab.Inspection --version $packageVersion
```

```csharp
using System;
using OpenCvSharp;
using OpenVisionLab.Inspection;
using OpenVisionLab.Vision2D.Property;
using OpenVisionLab.Vision2D.Tool;
using OpenVisionLab.Vision3D.Geometry;
using OpenVisionLab.Vision3D.Inspection;

using Mat image = new Mat(2, 2, MatType.CV_8UC1, new Scalar(100));
using ThresholdTool threshold = new ThresholdTool();
threshold.SetProperty(new ThresholdToolProperty { Threshold = 50 });

HeightMap3D heightMap = HeightMap3D.FromArray(
    new[,] { { 1.0, 1.1 }, { 1.2, 1.3 } },
    0, 0, 1, 1, "mm", "mm", "fixture", "scan-001");
ThicknessInspectionTool thickness = new ThicknessInspectionTool(
    new ThicknessInspectionOptions
    {
        MinimumThickness = 0.9,
        MaximumThickness = 1.4,
        InputRequirements = new HeightMapInputRequirements("mm", "mm", "fixture")
    });

using CombinedInspectionRunResult result = new CombinedInspectionRunner().Run(
    new CombinedInspectionInput { Image = image, HeightMap = heightMap },
    new IVisionTool[] { threshold },
    new IThreeDInspectionTool[] { thickness });

Console.WriteLine($"success={result.Success}, steps={result.Steps.Count}");
```

Every configured step runs even after an earlier failure so the caller retains all evidence. The runner owns and disposes collected 2D result snapshots; it never disposes the caller's image, height map, or supplied tools. Source-neutral surface-match and mesh Tools are executed directly, not through this height-map-only runner.

All 2D steps execute in list order, followed by all 3D steps in list order. They
receive the same original input for their domain; one result is not fed into the
next step. Use `VisionPipelineRuntime` for 2D image-layer routing. A missing/null
Tool, thrown Tool exception, or null Tool result becomes a failed step. An empty
configuration returns `Success=false`. Exceptions from a user-supplied enumerable
itself are outside the per-Tool execution boundary; pass stable materialized lists.

`CombinedInspectionRunResult.Success` is true only when every step passes. Inspect
`step.VisionResult.ErrorCode/Exception` for 2D evidence. For height maps, inspect
`step.ThreeDResult.MeasurementOutcome`: `OutOfTolerance` retains a valid measurement,
while `NotMeasured` requires correction of input, ROI, units, coverage or execution.
The runner does not retry failed steps or cancel an executing native call.

Run synchronously on a host-selected worker, with no concurrent mutations of its
inputs, properties, lists or Tools. Keep them alive until `Run` finishes even when
the host stops awaiting it. Dispose the combined result only after all consumers
finish reading its snapshots. A processing deadline is an observation after the
call unless the particular lower-level Tool explicitly supports cancellation.

See the [2D result/lifetime contract](../OpenVisionLab.Vision2D/README.md#results-errors-and-recovery)
and [3D outcome contract](../../docs/three-d-inspection.md#result-units-and-status).

[Repository and full documentation](https://github.com/Noah8218/OpenVisionLab-Vision-SDK)
