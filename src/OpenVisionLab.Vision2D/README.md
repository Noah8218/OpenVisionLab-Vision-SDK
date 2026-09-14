# OpenVisionLab.Vision2D

OpenCvSharp-based 2D inspection tools with explicit properties and disposable `VisionToolResult` output.

`3.0.0` is the API/assembly baseline, not the install version. Use the exact
immutable version from your package source; `3.0.1-dev.1` is only the current
repository-local default.

```powershell
$packageVersion = "3.0.1-dev.1" # Replace when pack or your feed uses another version.
dotnet add package OpenVisionLab.Vision2D --version $packageVersion
```

## Quick start

```csharp
using System;
using OpenCvSharp;
using OpenVisionLab.Vision2D.Property;
using OpenVisionLab.Vision2D.Tool;

using Mat source = new Mat(16, 16, MatType.CV_8UC1, new Scalar(100));
using ThresholdTool tool = new ThresholdTool();
tool.SetProperty(new ThresholdToolProperty
{
    Threshold = 50,
    MaxValue = 255,
    ThresholdType = ThresholdTypes.Binary
});

using VisionToolResult result = tool.Execute(source);
if (!result.Success)
{
    throw new InvalidOperationException($"{result.ErrorName}: {result.Message}");
}

Console.WriteLine($"Output: {result.ResultImage.Width}x{result.ResultImage.Height}");
```

## Ready-to-use property models

The package provides concrete configuration models for every current non-legacy 2D Tool. Implement the interfaces directly only when an application needs its own persistence model. `BlobToolProperty` is supplied by the separate `OpenVisionLab.Vision2D.Blob` package.

| Tool | Property model |
| --- | --- |
| `ThresholdTool` | `ThresholdToolProperty` |
| `MorphologyTool` | `MorphologyToolProperty` |
| `FilterTool` | `FilterToolProperty` |
| `EdgeDetectionTool` | `EdgeDetectionToolProperty` |
| `RotateScaleTool` | `RotateScaleToolProperty` |
| `AffineTransformTool` | `AffineTransformToolProperty` |
| `AutoMPointTool` | `AutoMPointToolProperty` |
| `ContourTool`, `CornerTool` | `ContourToolProperty` |
| `MatchingTool` | `MatchingToolProperty` |
| `EdgeBasedTemplateMatchingTool` | `EdgeBasedTemplateMatchingToolProperty` |
| `SiftTool` | `SiftToolProperty` |
| `MeanTool` | `MeanToolProperty` |
| `LineGaugeTool` | `LineGaugeToolProperty` |

Models derived from `OpenCvToolPropertyBase` default to whole-image processing with thresholds and inversion disabled. Threshold, Morphology, Filter, EdgeDetection, RotateScale, Affine and AutoMPoint have their own property contracts. `LineGaugeTool` requires a taught `CvROI` because scan direction and extent are part of the measurement definition.

`SiftTool` attempts SIFT first. The currently bundled native OpenCV runtime does
not export SIFT, so it uses ORB as a compatibility fallback. The result metrics
`FeatureDetector.Sift` and `FeatureDetector.OrbFallback` are `1` for the detector
that ran, so a host can persist the actual algorithm with its inspection record.
On success or a completed no-match attempt, exactly one is `1` once a detector was
selected. Early validation and exception results can omit both keys. Missing keys
do not establish that SIFT ran. `SCORE_MIN` here is the descriptor nearest-neighbor
ratio threshold (`0.6` by default), not a confidence percentage.

## Input and option contracts

Use non-empty `CV_8UC1` images as the common starting point. The table describes
the current execution path, not certification of every OpenCV depth/channel
combination. Depth is not automatically normalized by common preprocessing.
Unsupported combinations can return `OpenCvExecutionFailed` after native validation.

Common preprocessing converts 3/4 channels using **RGB/RGBA** weights, then applies
ordinary threshold **or** adaptive threshold, then optional inversion. If both
threshold flags are set, ordinary threshold takes precedence. `Cv2.ImRead` color
output is BGR: use `ImreadModes.Grayscale` or explicitly convert BGR before passing
it to an RGB-based grayscale path. Adaptive threshold needs 8-bit single-channel
working data, an odd `BlockSize > 1`, and `Binary`/`BinaryInv` mode.

For common ROI tools, set `USE_ROI` with an in-bounds, positive `CvROI`, or
`USE_MULTI_ROI` with a non-empty `CvROIS` list; multi-ROI takes precedence. Coordinates
use pixels, X right, Y down, and `Rect(x, y, width, height)`. Do not reuse a taught
ROI after resizing without transforming it. Detection coordinates include the ROI
offset. Pixel and area values are not calibrated physical measurements merely
because `PIXELPERMM` exists.

| Tool / configuration source | Input and ROI path | Initial options and actionable failures |
| --- | --- | --- |
| `ThresholdTool` / [properties](OpenCV/Property/ThresholdToolProperty.cs) | Whole image; converts color to gray; Range outputs a binary mask | `Threshold=1`, `MaxValue=255`, Binary. Validate finite values, ordered range and adaptive block size; `ThresholdInvalid*` identifies the setting. |
| `MorphologyTool` / [properties](OpenCV/Property/MorphologyToolProperty.cs) | Whole image; preserves channels; start with a binary 8-bit mask | Rect 3×3, Erode, one iteration. `MorphologyInvalidKernel/Iterations`: use positive sizes/count. |
| `FilterTool` / [properties](OpenCV/Property/FilterToolProperty.cs) | Whole image; no automatic gray conversion; depth support depends on filter | Blur 3×3. Gaussian/Median require odd kernels; bilateral uses a separate destination. `FilterInvalidKernel/Sigma`: correct the selected filter's options. |
| `EdgeDetectionTool` / [properties](OpenCV/Property/EdgeDetectionToolProperty.cs) | Whole image; start with `CV_8UC1`; no gray conversion | Canny 100/200, aperture 3. Sobel/Scharr need explicit nonzero derivative orders; their output is unsigned 8-bit, so negative responses are clipped. Check `EdgeDetectionInvalid*`. |
| `RotateScaleTool` / [properties](OpenCV/Property/RotateScaleToolProperty.cs) | Whole image; resizes, then rotates within the scaled canvas | 100% X/Y, 0 degrees, Linear. Rotation may clip content. `RotateScaleInvalidScale`: use finite positive scales. |
| `AffineTransformTool` / [properties](OpenCV/Property/AffineTransformToolProperty.cs) | Whole image; explicit source/destination triangle and output canvas | Identity triangles; output size zero uses source size. Check `AffineDegenerate*`, `AffineInvalid*`, `AffineInsufficientCoverage`; [full contract](../../docs/AFFINE_TRANSFORM_2D.md). |
| `ContourTool` / [properties](OpenCV/Property/ContourToolProperty.cs) | Common preprocessing/ROI; working image converted to `CV_8UC1` before labeling | External, ApproxSimple, area 200–1,000,000 px². Set threshold for a meaningful foreground. `ContourNoResult`: review polarity/area; `ContourRoiInvalid`: reteach ROI. |
| `CornerTool` / [properties](OpenCV/Property/ContourToolProperty.cs) | Common preprocessing/ROI; uses the shared Contour property type | Inspect returned corners; contour-specific fields do not all govern corner extraction. `CornerNoResult`: review image features/preprocessing; `CornerRoiInvalid`: correct bounds. |
| `MatchingTool` / [properties](OpenCV/Property/MatchingToolProperty.cs) | Template plus source; common ROI; template must fit the search working image | CCoeffNormed, score 0.6, angle search enabled. Supply `SetTemplateImage`; review `MatchingTemplate*`, `MatchingInvalidScale/AngleStep`, `MatchingNoResult`. Budget angle/scale search explicitly. |
| `EdgeBasedTemplateMatchingTool` / [properties](OpenCV/Property/EdgeBasedTemplateMatchingToolProperty.cs) | Edge template plus source; common preprocessing/ROI | Score 0.75, one match; unique validation is opt-in. `MatchingNoResult/Ambiguous` are controlled detection failures; preserve diagnostic evidence. [Unique contract](../../docs/EDGE_BASED_UNIQUE_MATCH_V1.md). |
| `SiftTool` / [properties](OpenCV/Property/SiftToolProperty.cs) | Textured template plus source; common preprocessing/ROI; start with 8-bit gray | Ratio 0.6, RANSAC reprojection threshold 3 px. `FeatureNoKeypoints/NotEnoughMatches/HomographyFailed`: review texture, overlap and model suitability before changing limits. |
| `AutoMPointTool` / [properties](OpenCV/Property/AutoMPointToolProperty.cs) | Teaching image and representative images; separate `UseAnalysisRoi/AnalysisRoi` | Grid candidates, pattern 96×96, stride 16. `AutoMPointInvalid*` corrects setup; `AutoMPointNoCandidate` needs evidence review. [Teaching contract](../../docs/AUTO_MPOINT_V1.md). |
| `LineGaugeTool` / [properties](OpenCV/Property/LineGaugeToolProperty.cs) | Requires 8-bit unsigned input and taught scan ROI; common preprocessing | Contrast 30, thickness 5, sampling step 10. `InputImageInvalid` rejects other depths; `LineGaugeEdgeNotFound/FitFailed` requires scan direction/polarity and edge review. |
| `MeanTool` / [properties](OpenCV/Property/MeanToolProperty.cs) | Common preprocessing/ROI; measures the working gray image | Mean mode by default; MeanStdDev returns standard deviation. Values are rounded to one decimal. `MEAN_MIN/MAX` do not enforce acceptance in Run; the host applies limits. `MeanRoiInvalid/InvalidAdaptiveBlockSize` requires setup correction. |
| `BlobTool` / [separate package contract](../OpenVisionLab.Vision2D.Blob/README.md) | Binary foreground after common preprocessing/ROI | Threshold enabled at 120, area 20–100,000 px². `BlobNoResult` differs from `BlobLabelingFailed`. |

The linked property files are the complete default-value reference. Validation is
implemented by each Tool's `TryValidateBeforeRun`; native constraints still apply
after that validation. Representative executable calls live in
[`Vision2DSmokeSuite`](../../tests/OpenVisionLab.Inspection.Smoke/Suites/Vision2DSmokeSuite.cs)
and the [isolated package consumer](../../tests/OpenVisionLab.PackageConsumer.Smoke/Program.cs).

## Results, errors and recovery

| Observation | Meaning and caller action |
| --- | --- |
| `Success=true` | The Tool completed its own contract. Apply the configured inspection limits; this alone is not product acceptance. |
| `Success=false`, `Exception=null` | An expected validation or no-result condition. Branch on `ErrorCode`/`ResultStatus`, correct the named input or teaching, then execute again. |
| `Exception != null` | Execution failed. Preserve its type, `ErrorCode`, message, image metadata and parameters; diagnose native compatibility or algorithm input before retrying. |
| Missing image/metric | That evidence was not produced. Do not substitute zero, reuse a previous measurement, or read the Tool's mutable result list after a failed call. |

Use enum codes rather than parsing messages. The complete numeric mapping is in
[`VisionToolErrorCode`](OpenCV/Tool/VisionToolResult.cs). `Run`, constructors,
template loading and direct helpers can throw; the built-in `Execute` boundary
captures execution exceptions in `VisionToolResult`. Custom `IVisionTool`
implementations may throw and need a host boundary.

`VisionPipelineToolFactory.Create` throws `ArgumentException` for malformed,
unknown, or duplicate step parameters. Its `ParamName` is `parameters`, while the
message identifies the invalid key and expected value type. Use `ParamName` to find
the public input and the message to correct the individual recipe value.

`VisionPipeline.SchemaVersion` defaults to 1. Use
`VisionPipelineSerializer.Serialize` and `Deserialize` for the SDK-owned in-memory
XML contract. Original XML without the attribute loads as version 1; unknown
versions, duplicate parameter names, and DTD input fail closed. The host owns file
or database persistence, recipe lifecycle, and any later model-artifact resolver.
Loading a Pipeline never runs it.

`VisionPipelineRuntime.Run` preserves the original 3.x exception behavior.
`RunWithFailureResults` instead returns `InputLayerMissing`, `ToolFactoryFailed`, or
`ToolExecutionException` step results for missing layers, factory failures, and
throwing/null custom Tool results. Invalid Pipeline definitions still throw before
execution. `StepTimeout` and `StepCanceled` remain reserved until a real cooperative
execution contract exists.

```csharp
// After using VisionToolResult result = tool.Execute(source):
if (!result.Success)
{
    Console.Error.WriteLine($"{result.ErrorCodeValue}/{result.ErrorName}: {result.Message}");
    if (result.Exception != null) Console.Error.WriteLine(result.Exception.GetType().FullName);
}
else if (result.Metrics.TryGetValue("FeatureDetector.OrbFallback", out double usedOrb))
{
    Console.WriteLine($"ORB fallback used: {usedOrb == 1}");
}
```

## Lifetime, threads and time limits

Create a Tool, set its properties/template, call `Execute` sequentially, consume
the returned snapshot, and dispose the snapshot and Tool. `SetSourceImage` and
`SetTemplateImage` copy their input. Do not assign borrowed Mats directly to public
compatibility fields such as `imageSource`; those fields are owned by the Tool.
Properties and public result lists remain mutable; copy any required values before
another call. A failed preprocessing clone is released before the exception is
reported. To retain an output after disposing a result, make an owned `Clone()`.

Use one Tool per sequential worker. Concurrent execution, property changes,
template changes, input mutation and disposal on the same instance are unsupported.
`IVisionTool.Execute`, `VisionPipelineRuntime.Run` and `CombinedInspectionRunner.Run`
are synchronous and have no cancellation-token contract. Pipeline
`MaxElapsedMilliseconds` checks elapsed time **after** execution; it does not abort
a native call. Running a call inside `Task.Run` or stopping the await does not cancel
that work. Keep inputs/tools alive until it finishes. Hard stop/restart requirements
belong to host process orchestration.

The caller owns the input `Mat`. Dispose the Tool and `VisionToolResult`; the result
owns its output image snapshot. `VisionToolResult`, `VisionPipelineContext`, and
`VisionPipelineRunResult` release their owned images/results and suppress
finalization after successful disposal. Windows x64 is the supported native runtime.

[2D and 3D SDK documentation](https://github.com/Noah8218/OpenVisionLab-Vision-SDK#2d-quick-start)
