# OpenVisionLab.Vision2D

OpenCvSharp-based 2D inspection tools with explicit properties and disposable `VisionToolResult` output.

`3.0.0` is the API baseline. Source, package and CLR assembly version values
are owned by [Directory.Build.props](../../Directory.Build.props); these are
distinct identities. Install the exact immutable version from your selected
package source and keep all OpenVisionLab packages on that version. A local
development default does not establish that a package has been published.

```powershell
$packageVersion = Read-Host "Exact SDK package version from your selected feed"
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

## Scharr execution evidence

`EdgeDetectionTool` can borrow a host-owned `VisionComputeSession`, directly or
through the existing Factory overload. Read the [Compute local-operation guide](../OpenVisionLab.Compute/README.md#local-operations)
for the measured CPU policy, result diagnostics and serial disposal. Public
Scharr remains CPU; the qualified CUDA subset is internal verification only.
Construction/restoration never executes. CPU remains the default.

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
| `QrCodeTool` | `QrCodeToolProperty` |

Models derived from `OpenCvToolPropertyBase` default to whole-image processing with thresholds and inversion disabled. Threshold, Morphology, Filter, EdgeDetection, RotateScale, Affine and AutoMPoint have their own property contracts. `LineGaugeTool` requires a taught `CvROI` because scan direction and extent are part of the measurement definition.

`SiftTool` attempts SIFT first. The currently bundled native OpenCV runtime does
not export SIFT, so it uses ORB as a compatibility fallback. The result metrics
`FeatureDetector.Sift` and `FeatureDetector.OrbFallback` are `1` for the detector
that ran, so a host can persist the actual algorithm with its inspection record.
On success or a completed no-match attempt, exactly one is `1` once a detector was
selected. Early validation and exception results can omit both keys. Missing keys
do not establish that SIFT ran. `SCORE_MIN` here is the descriptor nearest-neighbor
ratio threshold (`0.6` by default), not a confidence percentage.

## QR text reading

`QrCodeTool` accepts a non-empty 8-bit grayscale or **BGR** Mat and non-empty
ASCII/UTF-8 text. It uses the bundled native decoder. Set an optional single
in-bounds `CvROI` and `ExpectedCodeCount` (default 1). It rejects BGRA/other
depths, active common threshold/inversion/multi-ROI options and non-empty masks.
This BGR contract is separate from the legacy common preprocessing described below.

```csharp
using QrCodeTool reader = new QrCodeTool();
reader.property.ExpectedCodeCount = 1;
using VisionToolResult reading = reader.Execute(source);
if (reading.QrCodeReading != null)
    foreach (var candidate in reading.QrCodeReading.Candidates)
        Console.WriteLine($"{candidate.CandidateIndex}: {candidate.Text} ({candidate.Decoded})");
```

Check `Success` and `ErrorCode` before accepting a reading. `QrCodeNotDetected`,
`QrCodeCountMismatch` and `QrCodeDecodeFailed` preserve the completed observation,
including readable text and four corners of unreadable candidates. Infrastructure
exceptions and pre-run rejection have no completed QR evidence. Backend load/export
failures are distinct from absence; complete native absence can fail Tool construction.

`QrCodeReading` is immutable managed evidence with input size, applied ROI,
expected/detected/decoded counts and candidates. Corners are in the current input
Mat's pixels with the ROI offset applied once. Candidate order can change between
runs; text and corners stay together. The observed count does not guarantee every
code in the scene was detected. Text is neither trimmed nor normalized, and identical
payloads remain separate candidates. The host owns expected-text comparison and
part acceptance; `Passed` means the expected number of observed candidates decoded.

The caller owns input Mat, Tool and returned result. `ResultImage` is a separate
full-frame snapshot. `LatestReading` is cleared on new execution, cancellation and
infrastructure failure; older returned evidence survives image/Tool disposal.
Serialize settings through the existing `qrcode`/`QrCodeTool` Pipeline descriptor,
which exposes only `NAME`, `USE_ROI`, `CvROI`, `ExpectedCodeCount` and no artifacts.
Construction/restoration performs no QR inspection. Cancellation is cooperative
between native calls; there is no native hard timeout or same-instance concurrency.
Empty/binary/NUL payloads, other encodings, DPM/quality grading and industrial
reading-rate guarantees are outside this text contract. See [the Tool contract](OpenCV/Tool/QrCodeTool.cs)
and [its focused cases](../../tests/OpenVisionLab.Inspection.Smoke/Suites/QrCodeSmokeSuite.cs).

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

For a file whose original type must be preserved, read with `ImreadModes.Unchanged`
and validate its depth/channels before conversion. For a configured `tool` and
caller-selected `path`, this host boundary accepts 8-bit gray/BGR/BGRA and rejects
other inputs rather than silently reducing their depth:

```csharp
using Mat raw = Cv2.ImRead(path, ImreadModes.Unchanged);
if (raw.Empty() || raw.Depth() != MatType.CV_8U || (raw.Channels() != 1 && raw.Channels() != 3 && raw.Channels() != 4))
    throw new ArgumentException("Expected a nonempty 8-bit gray, BGR or BGRA file.", nameof(path));
using Mat gray = new Mat();
if (raw.Channels() == 1) raw.CopyTo(gray);
else Cv2.CvtColor(raw, gray, raw.Channels() == 3 ? ColorConversionCodes.BGR2GRAY : ColorConversionCodes.BGRA2GRAY);
using VisionToolResult result = tool.Execute(gray);
```

Raw camera-memory RGB/RGBA is a different channel contract: use the corresponding
explicit conversion for that input. Record the original type and chosen conversion
with inspection evidence. The SDK does not infer a camera's channel order.

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
| `EdgeDetectionTool` / [properties](OpenCV/Property/EdgeDetectionToolProperty.cs) | Whole image; Canny accepts `CV_8UC1`/`CV_8UC3` and returns `CV_8UC1` without gray conversion | Canny 100/200, aperture 3. Sobel/Scharr need explicit nonzero derivative orders; their output is unsigned 8-bit, so negative responses are clipped. Check `EdgeDetectionInvalid*`. |
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

## Optional LineGauge peak measurement

Subpixel measurement is disabled by default. On `LineGaugeToolProperty`, set
`UseSubpixelMeasurement = true` to enable original-gray gradient centroid
evidence. To select logarithmic interpolation of the dominant gradient peak,
also set `UseSubpixelPeakMeasurement = true`; both options must be `true`.

Both Boolean options are supported in Pipeline parameters; restoring them
creates no inspection result. With `UseSubpixelMeasurement = false`, a saved
peak option is ignored and integer-edge behavior is retained. Recipes that
omit the peak option retain centroid evidence when subpixel measurement is enabled.

Peak measurement needs a complete profile window and positive gradients on both
sides of the maximum. Flat profiles and non-adjacent competing gradients within
two gray levels are rejected; two levels account for comparison of quantized
8-bit differences. With fewer than three admitted samples the Tool returns
`LineGaugeFitFailed`, with rejected-sample evidence. After this mode reaches
measurement, `LineGauge.SubpixelPeakMeasurement` is `1`; early validation failures
can omit it. Check `VisionToolResult.Success` and `SubpixelEvidence.HasFit` before
using values.

This option can separate a dominant transition from a weaker reflected response.
It estimates a pixel-domain response, not a physical surface boundary or calibrated
uncertainty. An asymmetric ramp has no unique peak and needs an independently
defined boundary for metrology. Review the [property contract](OpenCV/Property/LineGaugeToolProperty.cs)
and [focused checks](../../tests/OpenVisionLab.Inspection.Smoke/Suites/LineGaugeSubpixelSmokeSuite.cs)
for the exact implementation and tested scope.

## Pipeline descriptors and Tool reconstruction

[`VisionPipelineToolFactory.Descriptors`](OpenCV/Pipeline/VisionPipelineToolFactory.cs)
is the immutable machine-readable catalog for the Tools owned by this package,
including `qrcode`. Each `VisionPipelineToolDescriptor` exposes
the canonical `ToolType`, documented aliases, package and CLR type identity,
parameter names/defaults/value kinds, and required artifacts. Use
`TryGetDescriptor` to validate a selected Tool ID without constructing it. The Blob
package's [`VisionPipelineBlobToolFactory.Descriptors`](../OpenVisionLab.Vision2D.Blob/VisionPipelineBlobToolFactory.cs)
composes `BlobTool` with this base catalog.

Factory parameter values are invariant text. Integers and finite numbers use
invariant culture; Booleans use `true`/`false`; enums use a named value or a valid
named flags combination; rectangles use `x,y,width,height`; rectangle lists use
semicolon-separated rectangles; and colors use `#RRGGBB` or `#AARRGGBB`. Unknown,
empty, duplicate, malformed, non-finite, or undefined enum values fail with
`ArgumentException` before Tool execution. `LineGaugeTool` requires `CvROI`.

`MatchingTool`, `EdgeBasedTemplateMatchingTool`, and `SiftTool` require a schema 2
template reference. The Pipeline stores a stable host ID, `encoded-image` format
version 1, and SHA-256; it never stores a host path or the bytes. The host resolver
returns encoded PNG/JPEG/etc. bytes for that ID:

```csharp
using System;
using System.IO;
using System.Security.Cryptography;
using OpenVisionLab.Vision2D.Pipeline;
using OpenVisionLab.Vision2D.Tool;

byte[] encodedTemplate = File.ReadAllBytes("part-a-template.png"); // Host-owned storage.
string sha256;
using (SHA256 hash = SHA256.Create())
{
    sha256 = BitConverter.ToString(hash.ComputeHash(encodedTemplate)).Replace("-", string.Empty);
}

VisionPipelineStep step = new VisionPipelineStep { ToolType = "matching" };
step.Artifacts.Add(new VisionPipelineArtifactReference(
    VisionPipelineToolFactory.TemplateArtifactRole,
    "parts/part-a/template",
    VisionPipelineToolFactory.EncodedImageArtifactFormat,
    VisionPipelineToolFactory.EncodedImageArtifactFormatVersion,
    sha256));

using MatchingTool tool = (MatchingTool)VisionPipelineToolFactory.Create(step, artifact =>
{
    if (artifact.Id != "parts/part-a/template")
    {
        throw new InvalidOperationException($"Unknown template ID '{artifact.Id}'.");
    }

    return encodedTemplate;
});
```

The factory validates artifact metadata before calling the resolver, validates the
returned bytes against SHA-256 before decoding, copies the decoded template into
the Tool, and releases its temporary `Mat`. The host continues to own the byte
array. The caller owns and disposes the returned Tool. `PATTERN_PATH` is excluded
from model-backed Pipeline descriptors and is rejected if supplied.

`VisionPipeline.SchemaVersion` defaults to 2. `VisionPipelineSerializer` round-trips
schema 2 artifact references, still reads explicit schema 1 and original XML with
no version attribute, and rejects artifacts in schema 1. Invalid/future versions,
invalid artifact metadata, duplicate parameters, and DTD input fail closed.
Deserialization is inert: it never calls a resolver or executes a Tool. The host
owns file/database persistence and maps stable artifact IDs to its storage.

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

`VisionPipelineToolFactory.Create` reports malformed step parameters with
`ArgumentException` and `ParamName == "parameters"`; its message identifies the
invalid key and expected representation. Unsupported Tool/artifact versions use
`NotSupportedException`. Missing resolvers, hash mismatches, empty bytes, and image
decode failures use `InvalidOperationException`. `RunWithFailureResults` converts
any of these factory failures to `ToolFactoryFailed` and preserves the exception.

`VisionPipelineRuntime.Run` preserves the original 3.x exception behavior.
`RunWithFailureResults` instead returns `InputLayerMissing`, `ToolFactoryFailed`, or
`ToolExecutionException` step results for missing layers, factory failures, and
throwing/null custom Tool results. Invalid Pipeline definitions still throw before
execution. The token overload of `Run` propagates `OperationCanceledException`; the
token overload of `RunWithFailureResults` returns exactly one `StepCanceled` /
`Canceled` step and stops before a later step. `StepTimeout` remains reserved.

```csharp
// After using VisionToolResult result = tool.Execute(source):
if (!result.Success)
{
    Console.Error.WriteLine($"{result.ErrorCodeValue}/{result.ErrorName}: {result.Message}");
    if (result.Exception != null) Console.Error.WriteLine(result.Exception.GetType().FullName);
}
// For a SiftTool result, detector evidence is independent of Success.
string detector = result.Metrics.TryGetValue("FeatureDetector.Sift", out double usedSift) && usedSift == 1
    ? "SIFT"
    : result.Metrics.TryGetValue("FeatureDetector.OrbFallback", out double usedOrb) && usedOrb == 1
        ? "ORB fallback"
        : "Not established for this execution";
Console.WriteLine(detector);
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
All 2D calls remain synchronous. `MatchingTool`,
`EdgeBasedTemplateMatchingTool`, `EdgeDetectionTool`, `AutoMPointTool`, `SiftTool`, and `LineGaugeTool` implement
`ICancellableVisionTool.Execute(Mat, CancellationToken)`. Their managed ROI,
candidate, angle, scale and feature loops observe the token, and native OpenCV calls
are checked before and after returning. A native call already in progress cannot be
preempted. Hard stop/restart requirements belong to host process orchestration.

The token overloads of `VisionPipelineRuntime.Run` and
`RunWithFailureResults` forward the token to `ICancellableVisionTool`. Other
`IVisionTool` implementations receive checks immediately before and after their
call. If such a non-cooperative Tool returns after cancellation, the runtime disposes
its returned result and does not publish its output layer. Pipeline
`MaxElapsedMilliseconds` still checks elapsed time **after** execution and does not
abort work. `CombinedInspectionRunner.Run` retains its existing tokenless contract.

```csharp
using System;
using System.Threading;

// matchingTool is configured and source remains caller-owned.
using CancellationTokenSource cancellation = new CancellationTokenSource();

try
{
    using VisionToolResult result =
        ((ICancellableVisionTool)matchingTool).Execute(source, cancellation.Token);
    // Consume result before disposing it.
}
catch (OperationCanceledException exception)
    when (exception.CancellationToken == cancellation.Token)
{
    // The caller requested cancellation; do not reuse mutable Tool result lists.
}
```

The caller owns the input `Mat`. Dispose the Tool and `VisionToolResult`; the result
owns its output image snapshot. `VisionToolResult`, `VisionPipelineContext`, and
`VisionPipelineRunResult` release their owned images/results and suppress
finalization after successful disposal. Windows x64 is the supported native runtime.

[2D and 3D SDK documentation](https://github.com/Noah8218/OpenVisionLab-Vision-SDK#2d-quick-start)
