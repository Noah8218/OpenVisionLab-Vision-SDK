# OpenVisionLab.Vision2D.Blob

Blob labeling, area filtering, ROI handling, and ordered `BlobResult` output for OpenVisionLab Vision2D.

`3.0.0` is the API/assembly baseline, not the install version. Use the exact
immutable version from your package source; `3.0.1-dev.1` is only the current
repository-local default.

```powershell
$packageVersion = "3.0.1-dev.1" # Replace when pack or your feed uses another version.
dotnet add package OpenVisionLab.Vision2D.Blob --version $packageVersion
```

`BlobTool` follows the same execution contract as the other 2D tools:

```csharp
using System;
using OpenCvSharp;
using OpenVisionLab.Vision2D.Blob;
using OpenVisionLab.Vision2D.Result;
using OpenVisionLab.Vision2D.Tool;

using Mat source = new Mat(64, 64, MatType.CV_8UC1, Scalar.Black);
Cv2.Rectangle(source, new Rect(16, 16, 20, 20), Scalar.White, Cv2.FILLED);
using BlobTool tool = new BlobTool();
tool.SetProperty(new BlobToolProperty
{
    THRESHOLD = 120,
    MIN_AREA = 20,
    MAX_AREA = 100000
});

using VisionToolResult result = tool.Execute(source);
if (!result.Success)
{
    throw new InvalidOperationException($"{result.ErrorName}: {result.Message}");
}

foreach (BlobResult blob in tool.results)
{
    Console.WriteLine($"#{blob.Index}: area={blob.Area}, center={blob.Center}");
}
```

`BlobToolProperty` supplies safe defaults for every required field. Applications that need a custom persistence model may still provide their own `IOpenCVPropertyBlob` implementation.

For camera/file input, pass an owned non-empty 8-bit grayscale `Mat` (for example,
`Cv2.ImRead(path, ImreadModes.Grayscale)`). The working foreground is nonzero after
preprocessing. Threshold is enabled at 120 by default; disable it only when the
supplied foreground already represents the intended object. Ordinary threshold
takes precedence over adaptive threshold when both are enabled.

Use `USE_ROI/CvROI` for one in-bounds pixel rectangle or `USE_MULTI_ROI/CvROIS`
for a non-empty rectangle list. `MIN_AREA/MAX_AREA` filter pixel area; calibrated
physical area is a separate host calculation. `BlobInvalidAreaRange`,
`BlobRoiInvalid`, and `BlobInvalidAdaptiveBlockSize` identify setup errors;
`BlobNoResult` means no accepted object, while `BlobLabelingFailed` indicates a
native execution failure. Inspect `result.Exception` for the latter. Read
`tool.results` only after a successful current call and copy values needed beyond
the next execution. Dispose the result snapshot and Tool; the source remains yours.

## Pipeline composition

`VisionPipelineBlobToolFactory.Descriptors` exposes the complete immutable catalog
for all 15 non-legacy 2D Tools: `BlobTool` from this package plus the 14 Tools from
`OpenVisionLab.Vision2D`. `TryGetDescriptor` resolves canonical IDs and aliases.
`Create` constructs Blob directly and delegates the other Tools to
`VisionPipelineToolFactory`, including its host artifact resolver overload.

```csharp
using OpenVisionLab.Vision2D.Blob;
using OpenVisionLab.Vision2D.Pipeline;

VisionPipelineStep step = new VisionPipelineStep { ToolType = "blob" };
step.Parameters[nameof(BlobToolProperty.THRESHOLD)] = "135";
step.Parameters[nameof(BlobToolProperty.MIN_AREA)] = "25";

using BlobTool restored = (BlobTool)VisionPipelineBlobToolFactory.Create(step);
```

Blob parameters use the invariant representations documented in the
[shared Pipeline contract](../OpenVisionLab.Vision2D/README.md#pipeline-descriptors-and-tool-reconstruction).
Blob rejects artifact references because it has no external model. If a Pipeline
also contains Matching, edge-based matching, or SIFT, pass a resolver to the
composite `Create(step, resolver)` overload and construct the runtime with that
factory. The returned Tool remains caller-owned unless the runtime is explicitly
configured to own custom-factory Tools.

The [shared 2D contract](../OpenVisionLab.Vision2D/README.md#lifetime-threads-and-time-limits)
covers image channels, sequential use, failure recovery and cancellation limits.

[Complete Blob property example](https://github.com/Noah8218/OpenVisionLab-Vision-SDK#blobtool)
