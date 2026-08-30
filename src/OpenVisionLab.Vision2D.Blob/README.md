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
using OpenCvSharp;
using OpenVisionLab.Vision2D.Blob;
using OpenVisionLab.Vision2D.Tool;

using Mat source = Cv2.ImRead("part.png", ImreadModes.Grayscale);
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

[Complete Blob property example](https://github.com/Noah8218/OpenVisionLab-Vision-SDK#blobtool)
