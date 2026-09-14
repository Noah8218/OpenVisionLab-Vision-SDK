# OpenVisionLab Vision SDK direction and capability matrix

Updated: 2026-09-15
Work item: `PL-0016`
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

- Release build, 237 synthetic smoke cases, coverage floors, exact public API,
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
mutation, and disposal on the same instance are unsupported. `MatchingTool`,
`EdgeBasedTemplateMatchingTool`, `AutoMPointTool`, and `SiftTool` also implement
`ICancellableVisionTool.Execute(Mat, CancellationToken)`. Cancellation is observed
inside their managed search loops and around native calls; it cannot preempt a
native OpenCV call already in progress.

`Core factory` means `VisionPipelineToolFactory` owns the descriptor and creates the
Tool from a `VisionPipelineStep`. `Blob composite` means
`VisionPipelineBlobToolFactory` adds `BlobTool` and delegates the other 14 Tools to
the Core factory. Both catalogs expose canonical IDs, aliases, package and property
identity, invariant parameter metadata, defaults, and required artifacts.

| Tool | Package | Primary input/output | Pipeline status | Additional artifact/setup |
| --- | --- | --- | --- | --- |
| `ThresholdTool` | Vision2D | image -> binary/range image | Core factory | typed threshold settings |
| `MorphologyTool` | Vision2D | image/mask -> transformed image | Core factory | kernel/operator settings |
| `FilterTool` | Vision2D | image -> filtered image | Core factory | filter/kernel settings |
| `EdgeDetectionTool` | Vision2D | image -> edge image | Core factory | edge/operator settings |
| `RotateScaleTool` | Vision2D | image -> transformed image | Core factory | angle/scale/output policy |
| `AffineTransformTool` | Vision2D | image -> transformed image | Core factory | taught source/destination triangles |
| `ContourTool` | Vision2D | image/mask -> candidates/contours | Core factory | ROI, threshold and area settings |
| `CornerTool` | Vision2D | image -> corner points | Core factory | ROI and contour-family settings |
| `MatchingTool` | Vision2D | image + template -> poses/scores | Core factory | required host-resolved encoded template |
| `EdgeBasedTemplateMatchingTool` | Vision2D | image + edge template -> poses/evidence | Core factory | required host-resolved encoded template |
| `AutoMPointTool` | Vision2D | teaching and representative images -> candidate | Core factory | Pipeline executes one input image; representative-set teaching remains a typed direct workflow |
| `SiftTool` | Vision2D | image + template -> homography/match | Core factory | required host-resolved encoded template; bundled runtime currently uses ORB fallback |
| `LineGaugeTool` | Vision2D | image + scan ROI -> subpixel line evidence | Core factory | required taught `CvROI` parameter |
| `MeanTool` | Vision2D | image/ROI -> scalar statistics | Core factory | ROI and preprocessing settings |
| `BlobTool` | Vision2D.Blob | binary image/ROI -> blob candidates | Blob composite | threshold, ROI and area settings |

Direct typed APIs remain available. Factory construction uses explicit parameter
mapping without property reflection, `Activator`, or global registration. Matching,
edge-based matching, and SIFT use the schema-versioned artifact contract below;
their `PATTERN_PATH` compatibility property is deliberately excluded from Pipeline
parameters.

## 3D Tool execution matrix

The three height-map inspection Tools below share
`IThreeDInspectionTool.Execute(HeightMap3D)` and can run directly through
`CombinedInspectionRunner`:

- `ThicknessInspectionTool`
- `WarpageInspectionTool`
- `DatumPlaneRawHeightDeviationInspectionTool`

The remaining public Tools keep source-neutral typed inputs/options/results. They
run through their own `Execute` overloads. A host can opt into
`ThreeDToolAdapter<TResult>` and `ThreeDToolExecutionRunner` when one ordered
execution report is needed. The adapter preserves each exact typed result and keeps
`Completed`/`Canceled`/`Faulted` execution status separate from domain `Success`,
`Passed`, measurements, and tolerances.

| Typed execution area | Public Tools | Current common-runner path |
| --- | --- | --- |
| Basic geometry/alignment | `TwoPointLineTool`, `ThreePointPlaneTool`, `LineIntersectionTool`, `FullXyzAffineSolveTool`, `ConstrainedBestFitRigidAlignmentTool`, `RigidPointPairAlignmentTool`, `AffinePointCloudApplyTool`, `LandmarkCorrespondenceValidationTool`, `RigidTransformDiagnosticsTool`, `RigidPoseSymmetryEquivalenceTool`, `LevelFrameTool` | Typed direct `Execute`; host adapter required |
| Grid/reconstruction/preprocessing | `GridDiagnosticsTool`, `HeightMapCropTool`, `HeightMapDomainMaskTool`, `HeightMapThresholdBackgroundRemovalTool`, `HeightMapBackgroundSubtractionTool`, `HeightMapNormalPreparationTool`, `ReferenceGridRegridTool`, `ReferenceGridPointReconstructionTool`, `DeterministicMedianFilterTool`, `DeterministicLocalMedianOutlierFilterTool`, `LevelSurfaceTool`, `PointCloudBackgroundFilterTool`, `PointCloudVoxelDownsampleTool`, `HeightGridSummaryTool`, `HeightDistributionStatisticsTool`, `HeightMapRegionStatisticsTool`, `ConnectedRegionTool`, `ConnectedRegionMetricsTool`, `ConnectedRegionPresenceTool`, `ConnectedRegionFillHeightTool`, `LeastSquaresHeightFieldPlaneFitTool` | Typed direct `Execute`; host adapter required |
| Edge/feature selection | `DeterministicHeightDifferenceEdgeTool`, `DeterministicLineFitTool`, `DeterministicModelSurfaceSelectionTool`, `DeterministicModelKeyPointExtractionTool`, `DeterministicModelSurfaceEdgeExtractionTool`, `DeterministicOrganizedSceneSurfaceEdgeExtractionTool` | Typed direct `Execute`; host adapter required |
| Surface matching/mesh | `DeterministicSurfaceModelPreparationTool`, `DeterministicPreparedScenePreparationTool`, `DeterministicRigidSurfacePoseSearchTool`, `DeterministicSurfaceCoverageTool`, `DeterministicSurfaceEdgeCoverageTool`, `DeterministicMultipleSurfaceMatchTool`, `TriangleMeshDistanceTool`, `NominalActualMeshComparisonTool`, `DeclaredMeshNormalQualityTool`, `AcquisitionDirectionOrientationTool` | Typed direct `Execute`; host adapter required |
| Statistics/decision | `CompletenessGridInspectionTool`, `DualSurfaceThicknessInspectionTool`, `HeightDeviationInspectionTool`, `RepeatabilityStatisticsTool`, `LabeledEvidenceStatisticsTool`, `ThresholdCandidateAnalysisTool` | Typed direct `Execute`; host adapter required |
| Multi-input metrology | `PlaneFlatnessInspectionTool`, `PointPairDimensionsInspectionTool`, `GapFlushInspectionTool`, `VolumeInspectionTool`, `CrossSectionDimensionsInspectionTool` | Typed direct `Execute`; host adapter required |

Selected typed 3D overloads accept `CancellationToken`; the height-map interface and
combined runner do not. The token-taking adapter constructor reports cooperative
support while the tokenless constructor reports only runner pre/post checkpoints.
Cancellation stops later adapters, but a completed tokenless result remains in the
report. The report owns none of the captured inputs, Tools, or returned result
objects.

## Pipeline execution choices

| API | Intended compatibility contract |
| --- | --- |
| `VisionPipelineRuntime.Run` | Existing 3.x behavior. Invalid Pipeline definitions, factory failures, and thrown custom Tool exceptions propagate. Completed step results are disposed if execution throws. |
| `VisionPipelineRuntime.RunWithFailureResults` | Additive host boundary. Missing input layers, factory exceptions/null returns, and thrown/null Tool results become failed `VisionPipelineStepResult` entries. Invalid Pipeline definitions still throw before execution. |
| `VisionPipelineRuntime.Run(..., CancellationToken)` | Additive cooperative path. The token is forwarded to `ICancellableVisionTool`; cancellation propagates as `OperationCanceledException`, disposes unreturned completed results, and stops later steps. |
| `VisionPipelineRuntime.RunWithFailureResults(..., CancellationToken)` | Additive captured path. Cancellation creates exactly one `StepCanceled` / `Canceled` step, disposes any result returned after cancellation, and stops later steps. |

All four APIs remain synchronous. A non-cooperative `IVisionTool` receives only
pre/post-call cancellation checks. `MaxElapsedMilliseconds` is a post-execution
acceptance limit and does not abort work.

## Pipeline artifact contract

- `VisionPipeline.SchemaVersion` defaults to `2` and is serialized as the root
  `schemaVersion` XML attribute.
- `VisionPipelineSerializer.Serialize` and `Deserialize` are the SDK-owned in-memory
  XML entry points. The host owns file or database persistence.
- Original XML without `schemaVersion` loads as schema version 1.
- Unknown or invalid versions fail closed with `NotSupportedException`.
- DTD processing is prohibited and external entity resolution is disabled.
- Duplicate parameter names remain invalid without regard to case.
- Version 1 retains invariant string parameter values for 3.x compatibility and
  cannot contain artifact references. Original XML without the version attribute
  is treated as version 1.
- Version 2 adds per-step `Artifacts/Artifact` references with `role`, stable host
  `id`, `format`, positive `formatVersion`, and a 64-hex-character `sha256`.
- Matching, edge-based matching, and SIFT require exactly one `template` artifact
  in `encoded-image` format version 1. Other current Tools reject artifacts.
- `VisionPipelineToolFactory.Create(step, resolver)` validates metadata before it
  invokes the resolver, validates the returned bytes against SHA-256 before image
  decoding, copies the decoded image into the Tool, and releases the temporary
  `Mat`. The host owns persistence and returned byte arrays; the caller owns and
  disposes the returned Tool.
- The resolver receives the complete serialized reference. It maps the stable ID
  to host storage; no host file path is serialized or accepted as a model parameter.
- Loading or restoring a Pipeline never executes it.

## Error producer matrix

Use `ErrorCode`/`ResultStatus` for control flow and `Message` only for diagnosis.

| Error code | Current producer | Host action |
| --- | --- | --- |
| `InputImageInvalid` | Direct 2D Tool validation/execution | Correct image type, size, depth or channel contract |
| `InputLayerMissing` | `RunWithFailureResults` before factory creation | Fix the step input-layer name or prior output routing |
| `ToolFactoryFailed` | `RunWithFailureResults` for factory exception/null, including artifact resolution/integrity/decode failure | Correct Tool ID, parameters, or artifact metadata/bytes; inspect the preserved exception |
| `ToolExecutionException` | `RunWithFailureResults` for a throwing/null custom Tool result and other declared boundaries | Preserve exception and input/settings evidence; do not reuse stale output |
| `OpenCvExecutionFailed` and Tool-specific codes | Modern 2D `Execute` boundary | Branch on the typed code; diagnose native/input or controlled no-result condition |
| `StepTimeout` | Reserved; no execution producer | Do not treat `MaxElapsedMilliseconds` as cancellation |
| `StepCanceled` | Token overload of `RunWithFailureResults` | Treat it as caller-requested execution cancellation; inspect the retained `OperationCanceledException` and do not execute dependent steps |

Infrastructure failures returned by `RunWithFailureResults` cannot satisfy a
terminal `ExpectedSuccess=false` acceptance rule. Expected inspection failures must
come from an executed Tool's controlled result.

## Shortest code-reading order

1. `VisionPipeline.cs`, `VisionPipelineStep.cs`, and
   `VisionPipelineArtifactReference.cs` — serialized state owner.
2. `VisionPipelineToolDescriptor.cs` and `VisionPipelineBuiltInDescriptors.cs` —
   discoverable Core Tool/parameter/artifact contract.
3. `VisionPipelineSerializer.cs` — schema/load/save validation owner.
4. `VisionPipelineToolFactory.cs` and the Blob package's
   `VisionPipelineBlobToolFactory.cs` — explicit construction and model restoration.
5. `VisionPipelineRuntime.cs` — layer, factory, Tool execution and failure-result
   call path.
6. `VisionPipelineContext.cs` and `VisionPipelineRunResult.cs` — mutable layer owner
   and result release owner.
7. `VisionToolResult.cs` — public 2D status/error/lifetime contract.
8. Read `Vision2DCancellationSmokeSuite.cs` for cancellation/error/lifetime cases,
   then search the Tool name in `Vision2DSmokeSuite.cs` or the matching 3D suite.

## Ordered engineering priorities

The pre-implementation owner, call-path, compatibility, proof, and stop-gate review
is complete in
[`SDK_PRIORITY_1_4_IMPLEMENTATION_ANALYSIS.md`](SDK_PRIORITY_1_4_IMPLEMENTATION_ANALYSIS.md).
Implementation is tracked by `.proofline/issues/PL-0016.json` and proceeds in this
order:

1. **Complete (`PL-0016` M2):** add opt-in 3D typed adapters and a common execution
   report while retaining every typed result.
2. **Complete (`PL-0016` M3):** add cooperative cancellation first to long-running
   matching/search loops.
3. **Active (`PL-0016` M4):** characterize Blob/Contour behavior, replace `OpenCvSharp.Blob`, then test an
   OpenCvSharp4 4.13 migration. Treat OpenCvSharp5/.NET 8 as a separate 4.0 decision
   | Recommended model: `gpt-6-astra` | Reasoning effort: `high`
4. **Pending (`PL-0016` M5):** after the contract and representative-data gates exist, add calibrated 2D
   fixture/metrology, gauge primitives, golden comparison and color inspection |
   Recommended model: `gpt-6-astra` | Reasoning effort: `high`

Production qualification remains blocked until sensor/acquisition settings,
calibration ID/hash, representative normal/defect data, independent ground truth
and uncertainty, LSL/USL, allowed false-accept/false-reject rates, and Takt limits
are supplied and approved.
