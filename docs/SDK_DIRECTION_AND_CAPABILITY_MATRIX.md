# OpenVisionLab Vision SDK direction and capability matrix

Updated: 2026-09-14
Work item: `PL-0014`
API baseline: `3.0.0`

## Product direction

OpenVisionLab Vision SDK is the UI-independent, deterministic 2D/3D rule-based
inspection kernel used by OpenVisionLab host applications. Its purpose is to make
algorithm behavior reusable and reviewable through typed input, settings, model,
result, error, unit, coordinate-frame, lifetime, and evidence contracts.

The SDK owns:

- image, height-map, full-XYZ, mesh, ROI, unit, frame, and missing-sample contracts;
- deterministic algorithms and their validation, numerical, result, and diagnostic
  evidence;
- Tool settings and model-artifact formats, schema versions, validation, and
  migrations;
- synchronous execution and any explicitly declared cooperative cancellation;
- package, native-runtime, public-API, and regression-test compatibility.

The consuming host owns:

- camera/sensor acquisition, device lifetime, and acquisition provenance;
- calibration creation, validity approval, and traceability;
- Teaching UI, rendering, Preview/Run, recipe storage and lifecycle;
- product tolerances, final acceptance policy, PLC/MES/I/O, deployment, and users;
- process isolation when a native call must be terminated forcibly.

The SDK does not attempt to reproduce the Cognex VisionPro, HALCON HDevelop, or
Open eVision development environment. It provides the kernel contracts that let a
host build those workflows without reimplementing each Tool's failure and evidence
rules.

## Current maturity boundary

- Release build, 235 synthetic smoke cases, coverage floors, exact public API,
  analyzer identities, package provenance, negative probes, and isolated
  `net8.0/win-x64` consumption are automated.
- Synthetic evidence does not establish real-sensor accuracy, calibration validity,
  Gauge R&R, production false-accept/false-reject rates, production Takt, or
  long-running process stability.
- Windows x64 is the current native runtime contract.
- Existing `C*`, `CV*`, and `LineGuage` identities remain supported throughout 3.x.

## 2D Tool capability matrix

All current 2D Tools implement `IVisionTool.Execute(Mat)` and return an owned,
disposable `VisionToolResult`. The caller owns the input `Mat`. One Tool instance is
for one sequential worker; concurrent execution, settings/template mutation, input
mutation, and disposal on the same instance are unsupported. The common API has no
cooperative-cancellation parameter.

`Built-in` means the default `VisionPipelineToolFactory` creates the Tool from a
`VisionPipelineStep`. `Custom factory` means direct typed execution is public but a
host must currently use the existing `VisionPipelineRuntime(Func<...>)` seam.

| Tool | Package | Primary input/output | Pipeline status | Additional artifact/setup |
| --- | --- | --- | --- | --- |
| `ThresholdTool` | Vision2D | image -> binary/range image | Built-in | typed threshold settings |
| `MorphologyTool` | Vision2D | image/mask -> transformed image | Built-in | kernel/operator settings |
| `FilterTool` | Vision2D | image -> filtered image | Built-in | filter/kernel settings |
| `EdgeDetectionTool` | Vision2D | image -> edge image | Built-in | edge/operator settings |
| `RotateScaleTool` | Vision2D | image -> transformed image | Built-in | angle/scale/output policy |
| `AffineTransformTool` | Vision2D | image -> transformed image | Built-in | taught source/destination triangles |
| `ContourTool` | Vision2D | image/mask -> candidates/contours | Custom factory | ROI, threshold and area settings |
| `CornerTool` | Vision2D | image -> corner points | Custom factory | ROI and contour-family settings |
| `MatchingTool` | Vision2D | image + template -> poses/scores | Custom factory | owned template image; future model resolver required |
| `EdgeBasedTemplateMatchingTool` | Vision2D | image + edge template -> poses/evidence | Custom factory | trained edge model; future model resolver required |
| `AutoMPointTool` | Vision2D | teaching and representative images -> candidate | Custom factory | analysis ROI and representative-set provenance |
| `SiftTool` | Vision2D | image + template -> homography/match | Custom factory | owned template; bundled runtime currently uses ORB fallback |
| `LineGaugeTool` | Vision2D | image + scan ROI -> subpixel line evidence | Custom factory | required taught scan ROI |
| `MeanTool` | Vision2D | image/ROI -> scalar statistics | Custom factory | ROI and preprocessing settings |
| `BlobTool` | Vision2D.Blob | binary image/ROI -> blob candidates | Package custom factory | threshold, ROI and area settings |

The next 2D factory work must preserve direct typed APIs. Model-backed Tools need an
artifact ID/hash/format contract and a host-supplied resolver before they can be
reconstructed safely. The SDK must not persist host file paths or own a global
service locator.

## 3D Tool execution matrix

The three height-map inspection Tools below share
`IThreeDInspectionTool.Execute(HeightMap3D)` and can run directly through
`CombinedInspectionRunner`:

- `ThicknessInspectionTool`
- `WarpageInspectionTool`
- `DatumPlaneRawHeightDeviationInspectionTool`

The remaining public Tools keep source-neutral typed inputs/options/results. They
run through their own `Execute` overloads and require a host adapter when a single
recipe runner is needed. This preserves compile-time geometry and result contracts
instead of forcing unrelated inputs into one generic DTO.

| Typed execution area | Public Tools | Current common-runner path |
| --- | --- | --- |
| Basic geometry/alignment | `TwoPointLineTool`, `ThreePointPlaneTool`, `LineIntersectionTool`, `FullXyzAffineSolveTool`, `ConstrainedBestFitRigidAlignmentTool`, `RigidPointPairAlignmentTool`, `AffinePointCloudApplyTool`, `LandmarkCorrespondenceValidationTool`, `RigidTransformDiagnosticsTool`, `RigidPoseSymmetryEquivalenceTool`, `LevelFrameTool` | Typed direct `Execute`; host adapter required |
| Grid/reconstruction/preprocessing | `GridDiagnosticsTool`, `HeightMapCropTool`, `HeightMapDomainMaskTool`, `HeightMapThresholdBackgroundRemovalTool`, `HeightMapBackgroundSubtractionTool`, `HeightMapNormalPreparationTool`, `ReferenceGridRegridTool`, `ReferenceGridPointReconstructionTool`, `DeterministicMedianFilterTool`, `DeterministicLocalMedianOutlierFilterTool`, `LevelSurfaceTool`, `PointCloudBackgroundFilterTool`, `PointCloudVoxelDownsampleTool`, `HeightGridSummaryTool`, `HeightDistributionStatisticsTool`, `HeightMapRegionStatisticsTool`, `ConnectedRegionTool`, `ConnectedRegionMetricsTool`, `ConnectedRegionPresenceTool`, `ConnectedRegionFillHeightTool`, `LeastSquaresHeightFieldPlaneFitTool` | Typed direct `Execute`; host adapter required |
| Edge/feature selection | `DeterministicHeightDifferenceEdgeTool`, `DeterministicLineFitTool`, `DeterministicModelSurfaceSelectionTool`, `DeterministicModelKeyPointExtractionTool`, `DeterministicModelSurfaceEdgeExtractionTool`, `DeterministicOrganizedSceneSurfaceEdgeExtractionTool` | Typed direct `Execute`; host adapter required |
| Surface matching/mesh | `DeterministicSurfaceModelPreparationTool`, `DeterministicPreparedScenePreparationTool`, `DeterministicRigidSurfacePoseSearchTool`, `DeterministicSurfaceCoverageTool`, `DeterministicSurfaceEdgeCoverageTool`, `DeterministicMultipleSurfaceMatchTool`, `TriangleMeshDistanceTool`, `NominalActualMeshComparisonTool`, `DeclaredMeshNormalQualityTool`, `AcquisitionDirectionOrientationTool` | Typed direct `Execute`; host adapter required |
| Statistics/decision | `CompletenessGridInspectionTool`, `DualSurfaceThicknessInspectionTool`, `HeightDeviationInspectionTool`, `RepeatabilityStatisticsTool`, `LabeledEvidenceStatisticsTool`, `ThresholdCandidateAnalysisTool` | Typed direct `Execute`; host adapter required |
| Multi-input metrology | `PlaneFlatnessInspectionTool`, `PointPairDimensionsInspectionTool`, `GapFlushInspectionTool`, `VolumeInspectionTool`, `CrossSectionDimensionsInspectionTool` | Typed direct `Execute`; host adapter required |

Selected typed 3D overloads already accept `CancellationToken`; the height-map
interface and combined runner do not. A future adapter must report each Tool's real
cancellation capability and must not imply that every native or numerical call can
be interrupted.

## Pipeline execution choices

| API | Intended compatibility contract |
| --- | --- |
| `VisionPipelineRuntime.Run` | Existing 3.x behavior. Invalid Pipeline definitions, factory failures, and thrown custom Tool exceptions propagate. Completed step results are disposed if execution throws. |
| `VisionPipelineRuntime.RunWithFailureResults` | Additive host boundary. Missing input layers, factory exceptions/null returns, and thrown/null Tool results become failed `VisionPipelineStepResult` entries. Invalid Pipeline definitions still throw before execution. |

Both APIs remain synchronous. `MaxElapsedMilliseconds` is a post-execution
acceptance limit and does not abort work.

## Pipeline artifact contract

- `VisionPipeline.SchemaVersion` defaults to `1` and is serialized as the root
  `schemaVersion` XML attribute.
- `VisionPipelineSerializer.Serialize` and `Deserialize` are the SDK-owned in-memory
  XML entry points. The host owns file or database persistence.
- Original XML without `schemaVersion` loads as schema version 1.
- Unknown or invalid versions fail closed with `NotSupportedException`.
- DTD processing is prohibited and external entity resolution is disabled.
- Duplicate parameter names remain invalid without regard to case.
- Version 1 retains invariant string parameter values for 3.x compatibility.
- No migration is needed while version 1 is the only supported schema. Adding a
  later version requires an explicit migration and fixtures for every accepted old
  version before the supported version changes.
- Loading or restoring a Pipeline never executes it.

Template images, trained edge models, calibration and large binary data are not
embedded by this first contract. A later model-artifact contract must carry a stable
ID, format version, SHA-256, and a host-supplied resolver.

## Error producer matrix

Use `ErrorCode`/`ResultStatus` for control flow and `Message` only for diagnosis.

| Error code | Current producer | Host action |
| --- | --- | --- |
| `InputImageInvalid` | Direct 2D Tool validation/execution | Correct image type, size, depth or channel contract |
| `InputLayerMissing` | `RunWithFailureResults` before factory creation | Fix the step input-layer name or prior output routing |
| `ToolFactoryFailed` | `RunWithFailureResults` for factory exception/null | Correct Tool ID/parameters/registration; inspect preserved exception |
| `ToolExecutionException` | `RunWithFailureResults` for a throwing/null custom Tool result and other declared boundaries | Preserve exception and input/settings evidence; do not reuse stale output |
| `OpenCvExecutionFailed` and Tool-specific codes | Modern 2D `Execute` boundary | Branch on the typed code; diagnose native/input or controlled no-result condition |
| `StepTimeout` | Reserved; no execution producer | Do not treat `MaxElapsedMilliseconds` as cancellation |
| `StepCanceled` | Reserved; no common 2D execution producer | Use only after a real cooperative cancellation contract exists |

Infrastructure failures returned by `RunWithFailureResults` cannot satisfy a
terminal `ExpectedSuccess=false` acceptance rule. Expected inspection failures must
come from an executed Tool's controlled result.

## Shortest code-reading order

1. `VisionPipeline.cs` and `VisionPipelineStep.cs` — serialized state owner.
2. `VisionPipelineSerializer.cs` — schema/load/save validation owner.
3. `VisionPipelineRuntime.cs` — layer, factory, Tool execution and failure-result
   call path.
4. `VisionPipelineContext.cs` and `VisionPipelineRunResult.cs` — mutable layer owner
   and result release owner.
5. `VisionToolResult.cs` — public 2D status/error/lifetime contract.
6. Search the Tool name in `Vision2DSmokeSuite.cs` or the matching 3D smoke suite.

## Ordered engineering priorities

1. Add machine-readable Tool descriptors and complete safe 2D factory/model
   reconstruction without reflection or global registration | Recommended model:
   `gpt-5.6-sol` | Reasoning effort: `high`
2. Add opt-in 3D typed adapters and a common execution report while retaining every
   typed result | Recommended model: `gpt-5.6-sol` | Reasoning effort: `high`
3. Add cooperative cancellation first to long-running matching/search loops |
   Recommended model: `gpt-6-astra` | Reasoning effort: `high`
4. Characterize Blob/Contour behavior, replace `OpenCvSharp.Blob`, then test an
   OpenCvSharp4 4.13 migration. Treat OpenCvSharp5/.NET 8 as a separate 4.0 decision
   | Recommended model: `gpt-6-astra` | Reasoning effort: `high`
5. After the contract and representative-data gates exist, add calibrated 2D
   fixture/metrology, gauge primitives, golden comparison and color inspection |
   Recommended model: `gpt-6-astra` | Reasoning effort: `high`

Production qualification remains blocked until sensor/acquisition settings,
calibration ID/hash, representative normal/defect data, independent ground truth
and uncertainty, LSL/USL, allowed false-accept/false-reject rates, and Takt limits
are supplied and approved.
