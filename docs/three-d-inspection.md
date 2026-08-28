# OpenVisionLab Vision SDK 3D inspection

## Purpose

`OpenVisionLab.Vision3D` adds pure, UI-free height-map inspection and full-XYZ algorithms to OpenVisionLab Vision SDK.
`OpenVisionLab.Inspection` runs existing 2D `IVisionTool` instances and new
`IThreeDInspectionTool` instances in one ordered run while preserving every result.

The 3D libraries target `netstandard2.0`. They do not reference WPF, SharpGL, a
viewer control, or the OpenVisionLab 3D Studio application. A host can render the
same result separately without making rendering part of the measurement algorithm.

## Choose the API layer

| Layer | Normal call | Use it for | Failure contract |
| --- | --- | --- | --- |
| Height-map inspection | `HeightMap3D -> IThreeDInspectionTool -> ThreeDInspectionResult` | One regular scalar grid with ROI, unit/frame and coverage gates | Controlled `MeasurementOutcome`; no measurement exception is required for expected bad data |
| Source-neutral Tool | Tool-specific typed input/options/result | Full-XYZ geometry, regrid, filtering, matching and mesh comparison | Inspect the typed `Success`/`Message` contract where present; invalid programmer input may throw `ArgumentException` |
| Multi-input dimensional inspection | Caller-prepared points/regions/statistics -> typed result | Flatness, point-pair, gap/flush, volume and cross-section | Inspect typed `Passed`; invalid programmer input may throw `ArgumentException` |

`IThreeDInspectionTool` is intentionally limited to one `HeightMap3D` input. Do not
force mesh, matching, or multiple-surface tools through that interface. A host that
needs one recipe model owns the adapter from its source/recipe contract to the
appropriate typed Tool call.

## Companion validation applications

- [OpenVisionLab](https://github.com/Noah8218/OpenVisionLab) is the 2D rule-based
  workbench that exercises the OpenVisionLab Vision SDK image Tool, Layer, Pipeline, and result
  display contracts.
- [OpenVisionLab 3D Studio](https://github.com/Noah8218/OpenVisionLab-3D-Studio)
  exercises 3D source review, ROI teaching, explicit Preview/Run, metrics, overlays,
  and recipe replay. It consumes a fixed `OpenVisionLab.Vision3D` NuGet package through an
  explicit adapter rather than an adjacent source checkout.

An OpenVisionLab Vision SDK source change is not automatically present in either application.
The consuming application must intentionally update its binary or pinned package,
and 3D Studio must update the package hash and adapter contract together.

## Source-neutral feature extraction

`OpenVisionLab.Vision3D.FeatureExtraction` contains pure full-XYZ geometry tools that do
not know a camera, C3D file, recipe, UI, or calibration claim.

`DualSurfaceThicknessInspectionTool` and `HeightDeviationInspectionTool`
own deterministic height-residual/statistical evaluation and typed decisions.
Source identity, units, frames, recipe lifecycle, and UI evidence remain with
the consuming application.

- `TwoPointLineTool` constructs an ordered finite full-XYZ segment from two
  explicit points. It does not pick, snap, fit, or measure.
- `FullXyzAffineSolveTool` solves one source-to-reference affine matrix from
  exactly four independent correspondence pairs using scaled partial pivoting.
  It returns matrix, determinant, condition, and residual evidence only; it
  does not move a point cloud or create a height map.
- `ConstrainedBestFitRigidAlignmentTool` fits one source-to-reference proper
  rotation plus translation from four to sixty-four ordered full-XYZ pairs.
  Every pair participates in the deterministic Horn quaternion solve; scale,
  shear, reflection, weighting, and automatic outlier rejection are excluded.
  It returns pose, spatial-spread gates, per-pair residuals, and RMS/maximum
  diagnostics only; it does not move a point cloud or decide acceptance.
- `LineIntersectionTool` evaluates the closest approach, acute angle, and
  finite-segment support of two normalized full-XYZ line geometries. It does
  not choose lines, attach source/frame identity, or claim a physical corner.
- `DeterministicSurfaceCoverageTool` visits ordered model samples and assigns
  each one to the nearest still-unclaimed scene sample inside an inclusive
  distance limit. It reports one-way matched count, ratio, RMSE, and exact
  correspondences without applying a product acceptance threshold.
- `DeterministicRigidSurfacePoseSearchTool` enumerates caller-bounded X/Y/Z
  Euler candidates, derives translation from the two sample centroids, and
  ranks candidates by coverage count, RMSE, then stable enumeration order.
  Candidate and translation bounds are explicit and fail closed.
- `TriangleMeshDistanceTool` builds a deterministic triangle BVH and returns
  closest-point, closest-feature, unsigned-distance, and explicit direct or
  robust signed-distance evidence for source-neutral XYZ queries.
- `NominalActualMeshComparisonTool` streams ordered query points through that
  mesh-distance kernel and returns deterministic tolerance counts, signed and
  unsigned population statistics, sign-recovery counts, and bounded display
  samples. It does not own file identity, units, frames, or product lifecycle.
- `RigidTransformDiagnosticsTool` measures the homogeneous-row error,
  rotation orthogonality, determinant, translation magnitude, and rotation
  angle of a row-major 4x4 transform. The caller retains scenario limits and
  acceptance order.
- `HeightGridSummaryTool` computes finite/missing/zero counts, minimum,
  maximum, mean, and a deterministic fixed-bin distribution from
  single-precision height samples under an explicit zero-is-missing policy.
- `HeightDistributionStatisticsTool` computes the corresponding finite-value
  statistics and bins for double-precision scalar sequences, including an
  optional expected-valid-count guard.
- `HeightMapRegionStatisticsTool` owns deterministic finite count, coverage,
  sum, mean, and extrema for an explicit row-major rectangular region.
- `ConnectedRegionTool` labels an explicit binary height-grid mask in
  deterministic row-major seed order. `ConnectedRegionMetricsTool` computes
  geometry-only cell-footprint metrics, while `ConnectedRegionPresenceTool`
  evaluates each existing region against explicit finite-coverage and optional
  mean-height thresholds. The presence aggregate is Present when at least one
  region is Present; all-region acceptance remains consuming-application policy.
- `CompletenessGridInspectionTool` owns reference-region mean, rectangular
  cell placement, finite coverage, reference-relative mean, and typed
  per-cell/aggregate decisions under an optional inclusive policy. Its
  `ExecuteMaskAware` route accepts an exact source-grid `HeightGridMask` for
  inspection cells only; the mask must be non-empty, dimension-matched, and
  contained by the authored Inspection Grid ROI. Empty or incompatible masks
  fail closed, while the original rectangle-only `Execute` route remains
  compatible.
- `ReferenceGridPointReconstructionTool` maps finite grid cells to both
  declared-frame XYZ and reference-axis U/H/V coordinates under an explicit
  supported-coordinate range.
- `DeclaredMeshNormalQualityTool` evaluates declared per-position normals for
  finite/non-zero/unit length, topology validity, degenerate triangles, and
  corner alignment. It does not generate, repair, or promote normals and does
  not own source identity or admission policy.
- `LandmarkCorrespondenceValidationTool` evaluates the augmented rank and
  span-normalized tetrahedral volume of exactly four source/reference points.
  Pair identities, lineage, units, frames, recipe lifecycle, affine solving,
  and acceptance remain with the consuming application.
- `RepeatabilityStatisticsTool` calculates finite scalar mean, extrema, sample
  standard deviation, six-sigma spread, and range. Study identity, units,
  acceptance limits, Gauge R&R claims, and product decisions remain with the
  consuming application. Its explicit negative-variance policy lets a host
  preserve an established round-off contract without moving product policy
  into the Tool.

A host owns source binding, metadata, persistence, identity hashing, and any
display or inspection lifecycle around these pure results.

The surface-matching tools receive only contiguous ordered finite XYZ samples,
the source-neutral search domain, and a correspondence distance. They do not
receive a mesh, C3D file, prepared-scene artifact, unit, coordinate frame,
recipe, acceptance policy, Viewer state, or published-result lifecycle. Their
controlled synthetic smoke covers a known `30 degree` yaw with translation,
one-sample occlusion, translation no-match, and candidate-budget rejection.

## Public Tool catalog

The catalog below lists every public sealed `*Tool` in `OpenVisionLab.Vision3D 3.0.0`. A name in
this table means the numerical Tool is public; it does not imply that the Tool owns
source identity, units, calibration, recipes, overlays, or UI lifecycle.

| Area | Public Tools |
| --- | --- |
| Height-map inspection | `ThicknessInspectionTool`, `WarpageInspectionTool`, `DatumPlaneRawHeightDeviationInspectionTool` |
| Basic geometry and alignment | `TwoPointLineTool`, `ThreePointPlaneTool`, `LineIntersectionTool`, `FullXyzAffineSolveTool`, `ConstrainedBestFitRigidAlignmentTool`, `RigidPointPairAlignmentTool`, `AffinePointCloudApplyTool`, `LandmarkCorrespondenceValidationTool`, `RigidTransformDiagnosticsTool`, `RigidPoseSymmetryEquivalenceTool` |
| Grid, reconstruction and preprocessing | `GridDiagnosticsTool`, `HeightMapCropTool`, `ReferenceGridRegridTool`, `ReferenceGridPointReconstructionTool`, `DeterministicMedianFilterTool`, `DeterministicLocalMedianOutlierFilterTool`, `LevelSurfaceTool`, `HeightGridSummaryTool`, `HeightDistributionStatisticsTool`, `HeightMapRegionStatisticsTool`, `ConnectedRegionTool`, `ConnectedRegionMetricsTool`, `ConnectedRegionPresenceTool`, `LeastSquaresHeightFieldPlaneFitTool` |
| Edge and feature selection | `DeterministicHeightDifferenceEdgeTool`, `DeterministicLineFitTool`, `DeterministicModelSurfaceSelectionTool`, `DeterministicModelKeyPointExtractionTool`, `DeterministicModelSurfaceEdgeExtractionTool`, `DeterministicOrganizedSceneSurfaceEdgeExtractionTool` |
| Surface matching and mesh comparison | `DeterministicSurfaceModelPreparationTool`, `DeterministicPreparedScenePreparationTool`, `DeterministicRigidSurfacePoseSearchTool`, `DeterministicSurfaceCoverageTool`, `DeterministicSurfaceEdgeCoverageTool`, `DeterministicMultipleSurfaceMatchTool`, `TriangleMeshDistanceTool`, `NominalActualMeshComparisonTool`, `DeclaredMeshNormalQualityTool`, `AcquisitionDirectionOrientationTool` |
| Statistics and inspection decisions | `CompletenessGridInspectionTool`, `DualSurfaceThicknessInspectionTool`, `HeightDeviationInspectionTool`, `RepeatabilityStatisticsTool`, `LabeledEvidenceStatisticsTool`, `ThresholdCandidateAnalysisTool` |
| Multi-input dimensional inspection | `PlaneFlatnessInspectionTool`, `PointPairDimensionsInspectionTool`, `GapFlushInspectionTool`, `VolumeInspectionTool`, `CrossSectionDimensionsInspectionTool` |

## Height-map contract

`HeightMap3D` is an immutable regular grid with:

- rows and columns;
- origin and positive row/column pitch;
- one scalar value per grid cell;
- declared `PlanarUnit`, `HeightUnit`, `FrameId`, and `SourceId` metadata.

Its coordinate convention is fixed:

```text
X = OriginX + Column * ColumnPitch
Y = OriginY + Row * RowPitch
H = Values[Row * Columns + Column]
```

Columns increase X, rows increase Y, and the stored scalar is height H. H is not
implicitly Cartesian Y or Z. The final X/Y coordinate extent must remain finite.

The legacy constructor's single `Unit` declares both `PlanarUnit` and `HeightUnit`.
The legacy `Unit` property remains a scalar-height alias. New integrations should use
the separate units so a planar pitch in millimetres cannot be confused with height
samples in micrometres.

`double.NaN` means an unavailable sample and is ignored by height-map inspection tools.
Infinity and non-finite coordinate extents are rejected when `HeightMap3D` is
constructed. Invalid ROI values, insufficient usable samples, and insufficient valid
coverage produce controlled non-measurement result statuses.

Units and frame are declarations from the caller. They are not calibration,
traceability, Gauge R and R, repeatability, or physical-accuracy evidence.

`GridDiagnosticsTool` produces fixed-order topology, locator-order,
duplicate-locator, and coordinate-finiteness evidence. Call `Execute(width,
height)` for an implicit row-major grid or pass explicit `GridCoordinateSample`
values to retain the first affected ordinal, locator, and XYZ component.

`HeightMapCropTool` copies one valid `HeightMapRoi` into a smaller immutable
`HeightMap3D`. It preserves row-major finite and `NaN` values, pitches, units,
frame, and source ID. The output origin advances to the selected source row and
column, so the crop remains in the declared source frame. Invalid regions return
a controlled result without output; cancellation is propagated.

## Strict input requirements

`ThicknessInspectionOptions`, `WarpageInspectionOptions`, and
`DatumPlaneRawHeightDeviationInspectionOptions` accept:

- `InputRequirements`: exact planar unit, height unit, and frame ID;
- `MinimumValidSamples`: absolute finite-sample gate;
- `MinimumValidCoverageRatio`: finite samples divided by all ROI cells.

When `InputRequirements` is present, comparison uses exact ordinal strings. The
library does not convert units, accept aliases, infer a frame, or apply a coordinate
transform. A mismatch returns `InputContractMismatch`, `InvalidInput`, and
`HasMeasurement=false` before the numerical algorithm runs. A null requirement keeps
the 2.x compatibility path; production recipes should declare one explicitly.

```csharp
HeightMap3D map = HeightMap3D.FromArray(
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

ThreeDInspectionResult result = new ThicknessInspectionTool(
    new ThicknessInspectionOptions
    {
        MinimumThickness = 0.95,
        MaximumThickness = 1.25,
        MinimumValidSamples = 5,
        MinimumValidCoverageRatio = 0.8,
        InputRequirements = new HeightMapInputRequirements("mm", "mm", "fixture-top")
    }).Execute(map);

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

No height-map inspection interpolates or fills NaN cells. Results after ROI sampling
expose typed properties and matching metrics for `TotalSampleCount`,
`ValidSampleCount`, `MissingSampleCount`, `ValidCoverageRatio`,
`MinimumValidSamples`, and `MinimumValidCoverageRatio`.

## Thickness

`ThicknessInspectionTool` evaluates finite scalar values in its configured ROI.

- `MinimumThickness` and `MaximumThickness` are inclusive limits.
- Metrics include valid sample count, minimum, maximum, mean, range, and the count
  below or above each limit.
- An out-of-limit result has `HasMeasurement=true` and
  `ResultStatus=Failed`; invalid input is reported separately.

The caller must provide a map whose scalar values actually represent thickness. The
tool does not infer reference planes, material surfaces, or sensor calibration.

## Warpage

`WarpageInspectionTool` fits the least-squares plane `z = ax + by + c` to finite
ROI samples in the declared map frame. It then evaluates:

- residual peak-to-valley: `max(residual) - min(residual)`;
- residual RMS;
- fitted plane slope and intercept.

The configured peak-to-valley limit is required. RMS is optional. A collinear ROI,
fewer than three valid points, or a numerically unstable plane fit returns a
controlled non-measurement status.

This is a numerical planarity/warpage calculation. It is not a substitute for a
specified fixture, alignment scheme, mechanical warpage standard, or calibrated
metrology workflow.

Planar and height units may differ for this height-field fit. Residuals and intercept
use `HeightUnit`; slopes use `HeightUnit/PlanarUnit`.

## Datum-plane raw-height deviation

`DatumPlaneRawHeightDeviationInspectionTool` evaluates the explicit equation
`n.x * X + n.y * H + n.z * Y + d = 0`. It does not fit the plane. Because this
equation normalizes X, Y, and H as one Euclidean coordinate without unit conversion,
the tool rejects a map whose planar and height unit strings differ.

## Result units and status

`ThreeDInspectionResult` preserves `PlanarUnit`, `HeightUnit`, legacy `Unit`,
`FrameId`, `SourceId`, and the fixed coordinate convention. `MetricUnits` identifies
each metric independently; counts use `count`, ratios use `ratio`, residuals use the
height unit, and plane slopes use height unit divided by planar unit.

- `Success=true`, `HasMeasurement=true`: measurement completed within tolerance.
- `Success=false`, `HasMeasurement=true`: measurement completed outside tolerance.
- `HasMeasurement=false`: input, parameter, ROI, coverage, geometry, configuration,
  or execution failure prevented a valid measurement.

The additive `MeasurementOutcome` property maps those combinations to `Passed`,
`OutOfTolerance`, and `NotMeasured`. Use it when the caller needs one switchable state.
`ThreeDInspectionMetricNames` provides stable keys grouped by `Quality`, `Thickness`,
`Warpage`, and `DatumPlaneRawHeightDeviation`. `TryGetMetric` reads a value and its unit
without performing two dictionary lookups.

## Combined 2D and 3D execution

`CombinedInspectionRunner` deliberately does not modify the existing Mat/layer
`VisionPipeline`. It runs the two domains independently, preserves the native result
type for each step, and continues after individual failures so an operator can see all
available 2D and 3D evidence from one acquisition.

```csharp
using CombinedInspectionRunResult run = new CombinedInspectionRunner().Run(
    new CombinedInspectionInput
    {
        Image = image,
        HeightMap = heightMap
    },
    new IVisionTool[] { twoDTool },
    new IThreeDInspectionTool[]
    {
        new ThicknessInspectionTool(new ThicknessInspectionOptions
        {
            MinimumThickness = 1.00,
            MaximumThickness = 1.20
        }),
        new WarpageInspectionTool(new WarpageInspectionOptions
        {
            MaximumPeakToValley = 0.05,
            MaximumRms = 0.02
        })
    });
```

The caller owns `Image`, `HeightMap`, and the supplied tools; the combined runner
never disposes them. `CombinedInspectionRunResult` owns any 2D
`VisionToolResult.ResultImage` snapshots it contains, so dispose the run result after
all result inspection and rendering is complete.

## NuGet and IntelliSense

The `OpenVisionLab.Vision3D` package includes the repository `README.md` and generated
`OpenVisionLab.Vision3D.xml` API documentation next to the assembly. The package repository URL
identifies the canonical OpenVisionLab Vision SDK repository. A package consumer should pin the
package version and, for controlled application delivery, record the package SHA-256
and source commit used for validation.

The package does not include a camera or file-format adapter. Convert sensor or file
data at the application boundary, preserve the source unit/frame evidence, and then
construct the source-neutral typed input used by the selected Tool.

## Verification commands

```powershell
dotnet build OpenVisionLab.VisionSdk.sln -c Debug
dotnet run --project tests/OpenVisionLab.Inspection.Smoke/OpenVisionLab.Inspection.Smoke.csproj -c Debug --no-build
dotnet pack OpenVisionLab.VisionSdk.sln -c Debug --no-build
```

The smoke executable uses only deterministic synthetic height maps. It verifies
legacy constructor compatibility, strict unit/frame rejection, missing-sample coverage,
analytic-plane fitting, tolerance failures that retain measurements, controlled input
errors, and that a 3D step still runs after a 2D step fails. It is not a substitute for
sensor data, calibrated artifacts, or production acceptance testing.
