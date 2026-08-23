# OpenVisionLab.Vision3D

UI-independent height-map, full-XYZ geometry, surface matching, mesh comparison, and inspection algorithms for OpenVisionLab Vision SDK 3.0.

```powershell
dotnet add package OpenVisionLab.Vision3D --version 3.0.0
```

The caller owns unit, coordinate-frame, source identity, calibration, recipe tolerance, and product lifecycle. `NaN` is the missing height-map sample; infinity is rejected.

## Organized-grid diagnostics quick start

```csharp
using OpenVisionLab.Vision3D.FeatureExtraction;

GridDiagnosticsResult diagnostics = new GridDiagnosticsTool().Execute(
    width,
    height);
```

Use the two-argument overload for an implicit row-major grid. Explicit samples
produce fixed-order topology, locator-order, duplicate-locator, and finite-XYZ
evidence without owning file format, source identity, or acceptance policy.

## Height-map crop quick start

```csharp
using OpenVisionLab.Vision3D.FeatureExtraction;
using OpenVisionLab.Vision3D.Geometry;

HeightMapCropResult crop = new HeightMapCropTool().Execute(
    source,
    new HeightMapRoi(row: 10, column: 20, rowCount: 50, columnCount: 80));

if (!crop.Success)
{
    throw new InvalidOperationException(crop.Message);
}

HeightMap3D output = crop.Output;
```

The output keeps the source frame, units, pitches, source ID, finite values, and
missing cells. Its origin advances to the selected source row and column. The
tool does not own file I/O, recipe identity, Preview/Publish, or acceptance policy.

## Surface-match pose quick start

```csharp
using OpenVisionLab.Vision3D.FeatureExtraction;

SurfaceMatchSample[] model =
{
    new SurfaceMatchSample(0, new ThreeDPoint(0, 0, 0)),
    new SurfaceMatchSample(1, new ThreeDPoint(2, 0, 0)),
    new SurfaceMatchSample(2, new ThreeDPoint(0, 1, 0)),
    new SurfaceMatchSample(3, new ThreeDPoint(0, 0, 1))
};
SurfaceMatchSample[] scene =
{
    new SurfaceMatchSample(0, new ThreeDPoint(10, -4, 2)),
    new SurfaceMatchSample(1, new ThreeDPoint(12, -4, 2)),
    new SurfaceMatchSample(2, new ThreeDPoint(10, -3, 2)),
    new SurfaceMatchSample(3, new ThreeDPoint(10, -4, 3))
};

DeterministicRigidSurfacePoseSearchResult match =
    new DeterministicRigidSurfacePoseSearchTool().Execute(
        model,
        scene,
        new DeterministicRigidSurfacePoseSearchOptions
        {
            MinimumRotationXDegrees = 0,
            MaximumRotationXDegrees = 0,
            RotationStepXDegrees = 1,
            MinimumRotationYDegrees = 0,
            MaximumRotationYDegrees = 0,
            RotationStepYDegrees = 1,
            MinimumRotationZDegrees = 0,
            MaximumRotationZDegrees = 0,
            RotationStepZDegrees = 1,
            MinimumTranslationX = 9,
            MaximumTranslationX = 11,
            MinimumTranslationY = -5,
            MaximumTranslationY = -3,
            MinimumTranslationZ = 1,
            MaximumTranslationZ = 3,
            MaximumCorrespondenceDistance = 1e-9,
            MinimumMatchedSampleCount = 4,
            MaximumCandidateCount = 1
        });

if (!match.Success || !match.Matched)
{
    throw new InvalidOperationException(match.Message + match.RejectionReason);
}

Console.WriteLine(
    $"T=({match.Pose.TranslationX}, {match.Pose.TranslationY}, {match.Pose.TranslationZ}), " +
    $"coverage={match.Coverage.CoverageRatio:P0}, rmse={match.Coverage.InlierRmse}");
```

Search bounds and `MaximumCandidateCount` are mandatory safety limits. Matching reports pose, coverage, RMSE, and correspondence evidence; it does not decide the product pass/fail tolerance.

## Mesh comparison quick start

```csharp
using OpenVisionLab.Vision3D.FeatureExtraction;

MeshTriangle[] nominal =
{
    new MeshTriangle(
        sourceTriangleIndex: 3,
        a: new ThreeDPoint(0, 0, 0),
        b: new ThreeDPoint(2, 0, 0),
        c: new ThreeDPoint(0, 2, 0))
};
ThreeDPoint[] actual =
{
    new ThreeDPoint(0.5, 0.5, 1.0),
    new ThreeDPoint(0.5, 0.5, -2.0)
};

NominalActualMeshComparisonResult comparison =
    new NominalActualMeshComparisonTool().Execute(
        nominal,
        actual,
        new NominalActualMeshComparisonOptions(
            expectedPointCount: actual.Length,
            lowerTolerance: -1.5,
            upperTolerance: 1.5,
            maximumDisplaySamples: 100));

if (!comparison.Success)
{
    throw new InvalidOperationException(comparison.Message);
}

Console.WriteLine(
    $"within={comparison.WithinToleranceCount}, " +
    $"below={comparison.BelowToleranceCount}, above={comparison.AboveToleranceCount}, " +
    $"signed mean={comparison.SignedStatistics.Mean}");
```

Boundary signed distances are not guessed. `TriangleMeshDistanceTool.ExecuteRobustSign` and the comparison result expose whether robust sign recovery was required.

[Complete 3D input and result contract](https://github.com/Noah8218/OpenVisionLab-Vision-SDK/blob/main/docs/three-d-inspection.md)
