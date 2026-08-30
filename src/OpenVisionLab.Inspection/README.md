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

[Repository and full documentation](https://github.com/Noah8218/OpenVisionLab-Vision-SDK)
