# OpenVisionLab.Inspection

Execution contracts for independent OpenVisionLab 2D tools, height-map
`IThreeDInspectionTool` steps, and opt-in heterogeneous typed 3D adapters.

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

Use `ThreeDToolAdapter<TResult>` when heterogeneous typed 3D Tool calls need one
ordered execution report. The adapter retains the exact result type and reports only
execution completion, cancellation, or an exception. It never interprets a result's
`Success`, `Passed`, measurement, or tolerance fields.

```csharp
using System.Threading;
using OpenVisionLab.Inspection;
using OpenVisionLab.Vision3D.FeatureExtraction;

TwoPointLineTool lineTool = new TwoPointLineTool();
TwoPointLineInput lineInput = new TwoPointLineInput(
    new ThreeDPoint(0, 0, 0),
    new ThreeDPoint(3, 4, 0));

ThreeDToolAdapter<TwoPointLineResult> adapter =
    new ThreeDToolAdapter<TwoPointLineResult>(
        "Datum line",
        token => lineTool.Execute(lineInput, token));

ThreeDToolExecutionReport execution = ThreeDToolExecutionRunner.Run(
    new IThreeDToolAdapter[] { adapter },
    CancellationToken.None);
ThreeDToolExecutionResult<TwoPointLineResult> lineStep =
    (ThreeDToolExecutionResult<TwoPointLineResult>)execution.Steps[0];

Console.WriteLine($"execution={lineStep.Status}, line={lineStep.Result.Success}");
```

The token-taking adapter constructor declares real cooperative support; the
tokenless constructor declares only pre/post runner checkpoints. A faulted adapter
does not suppress later independent evidence. Cancellation stops later adapters. If
a tokenless call completes while cancellation is requested, its completed typed
result remains available and the report stops before the next adapter. The report
does not dispose captured inputs, Tools, or returned result objects; the host owns
their lifetimes. A null typed result is an execution fault.

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
