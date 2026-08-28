# OpenVisionLab.Vision3D

UI-independent height-map, full-XYZ geometry, surface matching, mesh comparison, and inspection algorithms for OpenVisionLab Vision SDK 3.0.

```powershell
dotnet add package OpenVisionLab.Vision3D --version 3.0.0
```

The caller owns unit, coordinate-frame, source identity, calibration, recipe tolerance, and product lifecycle. `NaN` is the missing height-map sample; infinity is rejected.

## Manual rigid point-pair alignment quick start

`RigidPointPairAlignmentTool` constructs one proper source-to-reference pose
from exactly three ordered, non-collinear point pairs. It is a deterministic
construction route, not a noisy best-fit solver; the caller owns identity,
units, frames, tolerance, and acceptance.

```csharp
using OpenVisionLab.Vision3D.FeatureExtraction;

var result = new RigidPointPairAlignmentTool().Execute(
    new[]
    {
        new RigidPointPairCorrespondence(
            new ThreeDPoint(0, 0, 0), new ThreeDPoint(10, -4, 2)),
        new RigidPointPairCorrespondence(
            new ThreeDPoint(1, 0, 0), new ThreeDPoint(10, -3, 2)),
        new RigidPointPairCorrespondence(
            new ThreeDPoint(0, 1, 0), new ThreeDPoint(9, -4, 2))
    },
    new RigidPointPairAlignmentOptions
    {
        MaximumPairLengthError = 1e-9,
        MinimumNormalizedCrossMagnitude = 1e-12
    });

if (!result.Success || result.Pose is null)
{
    throw new InvalidOperationException(result.Message);
}
```

The result includes the 4x4 row-major pose, pair-length and non-collinearity
diagnostics, and per-pair residual evidence. It does not transform a cloud or
make a product decision.

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

## Connected-region quick start

```csharp
using OpenVisionLab.Vision3D.FeatureExtraction;

HeightGridMask mask = new HeightGridMask(
    rowCount: 2,
    columnCount: 3,
    foreground: new[] { true, true, false, false, true, true });
ConnectedRegionResult regions = new ConnectedRegionTool().Execute(mask);
```

Regions are discovered by row-major seed order. Four-neighbor connectivity is
the default; set `ConnectedRegionOptions.Connectivity` to `Eight` when diagonal
cells belong to the same region. The tool returns only source-neutral cells and
grid bounds; source identity, units, artifacts, and acceptance policy remain
with the caller.

Use `ConnectedRegionMetricsTool` for deterministic per-region geometry after
labeling:

```csharp
ConnectedRegionMetricsResult metrics = new ConnectedRegionMetricsTool().Execute(
    regions,
    new ConnectedRegionMetricsOptions
    {
        OriginX = 0.0,
        OriginY = 0.0,
        ColumnPitch = 1.0,
        RowPitch = 1.0
    });
```

The result reports region count, cell-area totals, cell-center centroids,
principal orientation in degrees on `[0, 180)`, and cell-footprint bounds.
Point and isotropic regions report `HasOrientation == false` and `NaN` rather
than a fabricated direction. The bounds are geometry-only outputs, not
persisted recipe or editable downstream artifacts.

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

## Height-map domain mask quick start

```csharp
HeightGridMask domain = new HeightGridMask(
    rowCount: source.Rows,
    columnCount: source.Columns,
    foreground: foregroundCells);
HeightMapDomainMaskResult reduced = new HeightMapDomainMaskTool().Execute(
    source,
    domain);

if (!reduced.Success)
{
    throw new InvalidOperationException(reduced.Message);
}

HeightMap3D output = reduced.Output;
```

`HeightMapDomainMaskTool` keeps the exact source value at every foreground
cell and sets background cells to `NaN`. Existing foreground `NaN` values stay
missing; the tool never interpolates or fills samples. The output preserves the
source grid, pitches, units, frame, and source ID. A null, empty, dimensionally
mismatched, or incorrectly sized mask returns a controlled failure without an
output. Source identity, recipe lifecycle, file I/O, and acceptance policy
remain consumer responsibilities.

## Height-map threshold background-removal quick start

```csharp
HeightMapThresholdBackgroundRemovalResult threshold =
    new HeightMapThresholdBackgroundRemovalTool().Execute(
        source,
        new HeightMapThresholdBackgroundRemovalOptions
        {
            Threshold = 3.0,
            Mode = HeightThresholdBackgroundRemovalMode.KeepAtOrAboveThreshold
        });

if (!threshold.Success)
{
    throw new InvalidOperationException(threshold.Message);
}

HeightMap3D output = threshold.Output;
```

The predicate is inclusive (`>=` for `KeepAtOrAboveThreshold`, `<=` for
`KeepAtOrBelowThreshold`). Existing missing samples remain missing and finite
samples outside the predicate become `NaN` in a new same-grid value. The result
reports input/retained/removed counts; it does not infer a threshold, perform
morphology or region filtering, mutate the source, or decide product acceptance.

## Height-map saved-background subtraction quick start

```csharp
HeightMapBackgroundSubtractionResult subtraction =
    new HeightMapBackgroundSubtractionTool().Execute(
        current,
        savedBackground,
        new HeightMapBackgroundSubtractionOptions());

if (!subtraction.Success)
{
    throw new InvalidOperationException(subtraction.Message);
}

HeightMap3D delta = subtraction.Output;
```

The explicit policy is `current - savedBackground` on identical dimensions,
origin, pitches, units, and frame. A cell is missing when either input is
missing; no missing value is treated as zero. The result reports paired,
positive, negative, and exact-zero deltas. Alignment, interpolation,
resampling, tolerance, source identity, C3D encoding, and product acceptance
remain consumer responsibilities.

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
