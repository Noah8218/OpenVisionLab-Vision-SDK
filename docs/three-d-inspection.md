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
- `HeightMapThresholdBackgroundRemovalTool` applies an explicit inclusive
  keep-at-or-above or keep-at-or-below predicate to finite height samples,
  preserves existing missing cells, and returns a same-grid result plus exact
  retained/removed counts. It does not infer thresholds, perform morphology,
  mutate the source, or decide product acceptance.
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

The 3.x public signatures are owned by the [public API contract](../eng/public-api-baseline.txt).
The source version is owned by [Directory.Build.props](../Directory.Build.props);
install or verification work must pin the exact immutable package version that was packed.
A name in this table means the numerical Tool is
public; it does not imply that the Tool owns source identity, units, calibration,
recipes, overlays, or UI lifecycle.

| Area | Public Tools |
| --- | --- |
| Height-map inspection | `ThicknessInspectionTool`, `WarpageInspectionTool`, `DatumPlaneRawHeightDeviationInspectionTool` |
| Basic geometry and alignment | `TwoPointLineTool`, `ThreePointPlaneTool`, `LineIntersectionTool`, `FullXyzAffineSolveTool`, `ConstrainedBestFitRigidAlignmentTool`, `RigidPointPairAlignmentTool`, `AffinePointCloudApplyTool`, `LandmarkCorrespondenceValidationTool`, `RigidTransformDiagnosticsTool`, `RigidPoseSymmetryEquivalenceTool`, `LevelFrameTool` |
| Grid, reconstruction and preprocessing | `GridDiagnosticsTool`, `HeightMapCropTool`, `HeightMapDomainMaskTool`, `HeightMapThresholdBackgroundRemovalTool`, `HeightMapBackgroundSubtractionTool`, `HeightMapNormalPreparationTool`, `ReferenceGridRegridTool`, `ReferenceGridPointReconstructionTool`, `DeterministicMedianFilterTool`, `DeterministicLocalMedianOutlierFilterTool`, `LevelSurfaceTool`, `PointCloudBackgroundFilterTool`, `PointCloudVoxelDownsampleTool`, `HeightGridSummaryTool`, `HeightDistributionStatisticsTool`, `HeightMapRegionStatisticsTool`, `ConnectedRegionTool`, `ConnectedRegionMetricsTool`, `ConnectedRegionPresenceTool`, `ConnectedRegionFillHeightTool`, `LeastSquaresHeightFieldPlaneFitTool`, `DeterministicDatumPlaneFitTool` |
| Edge and feature selection | `DeterministicHeightDifferenceEdgeTool`, `DeterministicLineFitTool`, `DeterministicModelSurfaceSelectionTool`, `DeterministicModelKeyPointExtractionTool`, `DeterministicModelSurfaceEdgeExtractionTool`, `DeterministicOrganizedSceneSurfaceEdgeExtractionTool` |
| Surface matching and mesh comparison | `DeterministicSurfaceModelPreparationTool`, `DeterministicPreparedScenePreparationTool`, `DeterministicRigidSurfacePoseSearchTool`, `DeterministicSurfacePoseRefinementTool`, `DeterministicSurfaceCoverageTool`, `DeterministicSurfaceEdgeCoverageTool`, `DeterministicMultipleSurfaceMatchTool`, `TriangleMeshDistanceTool`, `NominalActualMeshComparisonTool`, `DeclaredMeshNormalQualityTool`, `AcquisitionDirectionOrientationTool` |
| Statistics and inspection decisions | `CompletenessGridInspectionTool`, `DualSurfaceThicknessInspectionTool`, `HeightDeviationInspectionTool`, `RepeatabilityStatisticsTool`, `LabeledEvidenceStatisticsTool`, `ThresholdCandidateAnalysisTool` |
| Multi-input dimensional inspection | `PlaneFlatnessInspectionTool`, `PointPairDimensionsInspectionTool`, `GapFlushInspectionTool`, `VolumeInspectionTool`, `CrossSectionDimensionsInspectionTool` |

`LeastSquaresHeightFieldPlaneFitTool` retains a local reference origin in its
fitted result. `EvaluateY` and the flatness caller use that origin to avoid
subtracting nearly equal global plane terms. The existing public result
constructor keeps coefficient-based evaluation; persisting only global
coefficients does not retain the fitted result's local arithmetic. Input
floating-point resolution and conditioning still limit representable precision.
Use the `Numerical reliability` and `3D analytic qualification` smoke cases for
the authored origins, sample distributions, residuals and exact acceptance
criteria; those fixtures do not guarantee arbitrary origins or sensor accuracy.
`LevelSurfaceTool` keeps double heights when preparing the shared fit; NaN keeps
its declared missing-sample meaning. `VolumeInspectionTool`
returns a typed result for finite, representable calculations; it throws
`InvalidOperationException` when an intermediate or accumulated volume becomes
non-finite. Treat that exception as a calculation failure, separate from a
finite result whose `Passed` value is `false`. `GapFlushInspectionTool` also
throws `InvalidOperationException` when its signed gap, flush, or reference flush
cannot be represented as a finite measurement. Discard that call and correct the
input magnitude at the operation boundary; subsequent valid calls remain independent.

For height-field plane fitting, positions represent **Y=f(X,Z)**; they must span
both horizontal axes and contain at least three finite samples. Vertical planes
cannot be represented by this model. `RawHeight` is fitted independently of the
display Y coordinate. `RootMeanSquareDistance` and flatness use orthogonal distance;
volume uses vertical Y residual multiplied by the caller's positive `SampleArea`.
Do not substitute orthogonal distance in that volume calculation. Flatness needs
independent reference and measurement regions; the host supplies compatible units,
frames, sampling area and calibrated acceptance limits.

## 기준면 이상점 선별

기준 영역의 이상점을 선택적으로 제외하면서 독립 측정 영역의 결함과 원본 높이를
보존한다. 선별 정책은 [DeterministicDatumPlaneFitTool](../src/OpenVisionLab.Vision3D/FeatureExtraction/GeometryConstruction/DeterministicDatumPlaneFitTool.cs),
수치 계산은 기존 plane-fit owner, 검증 입력은 [DatumPlaneFitSmokeSuite](../tests/OpenVisionLab.Inspection.Smoke/Suites/DatumPlaneFitSmokeSuite.cs)가 원본이다.

### 결정과 적용 조건

- 기존 local-median 필터는 grid의 이상점을 NaN으로 만든다. 기울기·불균일/결측
  이웃의 영향과 측정 결함 삭제 위험 때문에 공통 datum owner로 삼지 않는다.
- 전체 최소제곱 뒤 한 번 잔차를 제거하는 방법은 처음 오염된 평면에서 정상점까지
  제외할 수 있다. 유효한 평면 가설로 시작하는 제한된 합의 선별을 선택한다.
- `DeterministicDatumPlaneFitTool`은 선별만 소유하고, 후보와 최종 평면의
  수학은 기존 `LeastSquaresHeightFieldPlaneFitTool`에 위임한다. 기존 solver와
  local-origin 결과를 변경하지 않는다. 새 인터페이스·등록기·캐시를 만들지 않는다.

입력은 기존 불변 `HeightFieldPlaneFitSample`의 안정된 목록이다. X/Z는 수평축,
Y는 맞춤 높이이며 RawHeight는 독립적으로 보존한다. 선별 잔차는 **Y축 잔차**이고
threshold는 Y와 같은 단위다. 직교 거리나 자동 mm 변환을 사용하지 않는다.
양의 유한 threshold와 과반수보다 큰 최소 지지 비율은 호출자가 명시한다.
NaN/null/무한대는 선별할 이상점이 아니라 잘못된 입력이다. LevelSurface만 기존
정책대로 결측 grid 셀을 먼저 건너뛰고 유한한 기준점을 전달한다.

동일 입력 순서에 대해 양 끝을 포함한 최대 12개의 균등 인덱스를 가설점으로 고른다.
그 점들의 모든 삼중 조합(최대 220)을 평가한다. 비퇴화 가설마다 전체 기준점의
inclusive 높이 잔차 지지를 계산하고 최대 지지 집합을 선택한다. 같은 최대 지지
수를 가진 서로 다른 집합은 보수적으로 거부한다. 지지 개수/비율 게이트 뒤 기존
최소제곱으로 다시 맞추고 전체 기준점을 재분류하며, 같은 집합에 도달해야 성공한다.
10회 내 집합이 안정되지 않으면 거부한다. 취소는 입력/가설/분류/재맞춤 경계에서
관찰하며 OperationCanceledException을 전달한다.

성공 결과는 계산된 평면, 불변 선별 샘플, 사용/제외/가설 인덱스, 모든 입력의
높이 잔차, 적용 threshold·최소 개수/비율, 선별점 RMS와 RawHeight 평균,
실제 평가/재맞춤 횟수를 보존한다.
실패는 typed 사유와 메시지를 가지며 유효한 평면·측정 성공을 제공하지 않는다.
12점 탐색은 전체 입력의 모든 삼중 조합을 검사하지 않는다. 높은 지지는 물리적
기준면의 정체나 확률을 증명하지 않으며, 점 순서와 표본 위치가 결과에 영향을 준다.
다수 오염·선택된 가설점의 편향·threshold 부적합에 대한 범용 복원을 주장하지 않는다.

### 소유권과 실제 호출

solver는 GeometryConstruction owner, 선별은 datum owner, 계측 수식은 기존 Flatness/Volume/DualSurfaceThickness, grid 준비·평균·출력은
기존 LevelSurface다. 이 계산 경로는 Vision3D 내부 solver에 의존하며 UI/파일/전역 상태가 없다.

Caller → datum Execute → 기존 solver → 불변 결과 → 기존 계측의 ExecuteWithDatum
→ 실제 측정 루프. 별도 이름은 기존 Execute(null, ...) 호출의 overload 모호성을
피한다. 기존 public constructor/signature/default 경로는 유지한다. 새 계측 결과에는
선별 근거를 함께 남기고, 선별 실패를 tolerance failure로 처리하지 않는다.

LevelSurface → unique finite reference samples + 원본 grid 인덱스 → opt-in datum
→ 같은 선별 샘플로 평균/잔차/출력 reference fit → 원본 전체 grid의 detrending.
제외점과 측정 결함은 finite 출력으로 남고 NaN과 grid 위치는 보존한다. RegionEvidence는
기존 유한 입력 개수이며 선별점 수와 구별한다. null opt-in은 기존 동작이다.

writer/lifetime owner는 Execute의 지역 상태다. 입력/옵션은 caller가 실행 중 변경하지
않고 결과를 보관한다. 선별 목록/진단 배열은 읽기 전용 snapshot이고 native Dispose가
없다. 저장·종료·재시작과 calibration 승인은 Host 책임이며 이 변경이 실행이나 저장을
자동 유발하지 않는다. 읽기 순서는 datum Execute → 기존 plane fit → ExecuteWithDatum
소비자 → LevelSurface opt-in → DatumPlaneFitSmokeSuite다.

### 사용

호출자는 기준 영역과 독립 측정 영역, 동일한 단위/frame, 허용할 높이 잔차와
최소 지지를 정한다. 아래 값은 호출자가 정한 설정이며 생산 승인값을 추정하지 않는다.

```csharp
DatumPlaneFitOptions options = new DatumPlaneFitOptions
{
    MaximumAbsoluteHeightResidual = maximumDatumHeightResidual,
    MinimumInlierCount = minimumDatumSampleCount,
    MinimumInlierRatio = minimumDatumSupportRatio
};
DatumPlaneFitResult datum = new DeterministicDatumPlaneFitTool().Execute(referenceSamples, options, cancellationToken);
if (!datum.Success) throw new InvalidOperationException(datum.Message);
PlaneFlatnessInspectionResult flatness = PlaneFlatnessInspectionTool.ExecuteWithDatum(datum, measurementSamples, flatnessTolerance);
```

체적/두께는 각 기존 Tool의 static `ExecuteWithDatum`에 같은 datum을 전달한다.
LevelSurface는 자신의 options에 `DatumPlaneFitOptions`를 지정한다. 결과의 선별
인덱스는 전달한 기준점 목록을 가리키고, LevelSurface의 `ReferenceSampleIndices`는
그 목록을 원래 grid 셀로 연결한다. 실패 datum에는 계산 평면이 없다. 두께의 측정
입력/limit 오류 결과도 전달받은 datum 근거를 보존하며 오류를 측정 불합격으로 바꾸지 않는다.


## 연속 3D 표면 자세 보정

격자 후보 사이의 자세를 보정할 때 기존 coarse 결과 뒤에 point-to-point refinement를
선택적으로 적용한다. 전체 coverage와 범위를 다시 평가한 뒤 개선된 자세만 채택한다.
계약의 원본은 [DeterministicSurfacePoseRefinementTool](../src/OpenVisionLab.Vision3D/FeatureExtraction/SurfaceMatching/DeterministicSurfacePoseRefinementTool.cs)과
[SurfacePoseRefinementSmokeSuite](../tests/OpenVisionLab.Inspection.Smoke/Suites/SurfacePoseRefinementSmokeSuite.cs)다.

### 선택과 계약

격자를 더 촘촘하게 만드는 방법은 후보 곱과 비용이 증가하고 양자화가 남는다.
새 rigid solver는 완료된 수학을 중복한다. 기존 `ConstrainedBestFitRigidAlignmentTool`
과 `DeterministicSurfaceCoverageTool`을 재사용하는 작은 보정 owner를 선택한다.
일반 ICP의 반복/거리/중단 조건은 [PCL 공식 문서](https://pointclouds.org/documentation/classpcl_1_1_iterative_closest_point.html)를
참조한다. PCL을 의존성으로 추가하거나 동등한 성능·전역 수렴을 주장하지 않는다.

공통 PoseSearchOptions의 null RefinementOptions는 기존 경로 그대로다. opt-in은
유효한 coarse match 뒤에만 실행하며 기존 격자 후보 개수와 순위는 바꾸지 않는다.
직접 Execute는 사용자가 제공한 rigid seed와 같은 탐색 범위를 받는다. 미리 제공된
단위/frame 호환성과 실행 중 input/options의 안정성은 호출자 책임이다.
행렬은 `target = R * source + t`, 회전은 `Rz * Ry * Rx`, 길이 threshold는 XYZ 단위다.
옵션/한도의 원본은 `SurfacePoseRefinementOptions`와 `ValidateRefinementOptions`다.
`UseSpatiallyBalancedFit`는 기본 false이며 기존 반복/개선량 계약을 유지한다.
잘못된 opt-in은 coarse 탐색 전에 typed 실행 실패로 반환한다. 취소는 예외로 전달한다.

기본 경로는 coverage의 안정된 대응점 순서 양 끝을 포함한 균등 인덱스 최대64쌍을
선택한다. `UseSpatiallyBalancedFit`가 true이고 support>64이면 기존 key-point 위치
선택으로 최대64 고유 위치를 분산 선택한다. 비용 O(64*n), 첫 stable order/tie 규칙이므로
임의 permutation 불변이나 outlier 거부를 보장하지 않는다. 고유 위치<4이면 기존
ordered fit/실패 의미를 유지한다. 기존 solver의 4~64쌍/고유 좌표/비공선 조건을 그대로 적용한다. 3쌍 이하나
퇴화 대응점은 coarse 결과를 보존하고 사유를 남긴다. fit은 원본 모델→장면의 절대
자세이며 이미 변환한 점을 다시 합성하지 않는다. 각 trial은 **전체** 모델과 현재
사용 가능한 장면으로 coverage를 다시 계산한다. 대응점 수가 줄지 않고 RMSE가
설정한 최소 개선량보다 엄격히 감소할 때만 채택한다. 지지 수 증가만으로 잔차
악화를 허용하지 않는다. 미개선/solver 거부/범위 거부/반복 한도에서 마지막 채택
자세를 유지한다. 보정 불가와 입력/계산 실행 실패를 구별한다.

회전 범위는 canonical Euler만 검사하지 않는다. 일반 위치에서 두 등가 분해와
±180° 등가 표기를 검사한다. Y=±90°에서는 X−Z 또는 X+Z 관계와 호출자 범위의
교집합에서 등가 자세를 고른다. 역삼각함수의 경계 반올림은 1e-9° 이내에서 원래
경계로 정규화한 뒤 R을 재구성하고 대응점 centroid로 t를 다시 계산한다. 범위를
확장하지 않는다. translation은 기존 inclusive bounds를 그대로 검사한다.
이 방법은 unconstrained rigid fit 뒤 범위를 검사하며 범위 안의 최적 자세를 별도로
푸는 solver가 아니다. 잡음으로 고정축 밖의 회전이 필요하면 coarse를 유지한다.

결과는 초기/최종 pose·전체 coverage, 실제 fit/채택 횟수, typed 중단 사유·메시지,
마지막 fit에 사용한 최대64쌍의 식별자, 옵션/범위 snapshot을 가진다. 목록은 복사해
read-only로 보존한다. 마지막 fit은 거부될 수도 있으므로 최종 coverage와 구별한다.
20회·64쌍은 계산 상한이며 현장 takt 보장이나 전체 coverage 비용 상한은 아니다.

### 소유권과 실제 호출 경로

기존 owner: coarse Search/Multiple, greedy coverage, Registration paired solver.
SurfaceMatching 모듈의 `DeterministicSurfacePoseRefinementTool`이
국소 반복·fit 표본·범위/채택/근거를 소유한다. 별도 인터페이스·factory·registry·cache는 없다.
의존 방향: Search/Multiple → refinement → 기존 coverage/paired solver, 모두 Vision3D/BCL.

단일: bounded grid → coarse 최소 지지 → opt-in refinement → 채택 pose/coverage 반환.
복수: 기존 unclaimed scene 목록 → coarse 최소 지지 → 같은 available 목록만 보정
→ 초기/최종/fit 대응점의 원본 scene order 복원 → 최종 대응점 claim → 다음 instance.
보정이 앞 instance의 점을 재사용하지 않는다. SceneSampleCount는 기존 원본 scene
개수를 유지하며 evidence의 식별자는 항상 외부 입력의 order다.

mutable state는 각 Execute의 지역 pose/coverage/횟수와 기존 Multiple의 claim 배열에
있다. 결과·점·pose는 immutable, Tool은 공유 cache가 없다. 생성/해제는 caller와
managed 수명에 따르며 disposable/native/file/network/UI 자원을 만들지 않는다.
기존 public signature/default/constructor와 candidate count 의미는 보존하고
opt-in/result evidence만 추가한다. 짧은 읽기 순서: 두 Search Execute → refinement
Execute → coverage Execute → ConstrainedBestFit Execute → 대응 smoke.

### 사용

기존 유효한 search domain에 아래 옵션만 추가한다. 같은 PoseSearchOptions를
Multiple에 전달하면 각 coarse instance가 claim하기 전에 보정한다.

```csharp
options.RefinementOptions = new SurfacePoseRefinementOptions();
DeterministicRigidSurfacePoseSearchResult match =
    new DeterministicRigidSurfacePoseSearchTool().Execute(modelSamples, sceneSamples, options);
if (match.Success && match.Matched)
{
    SurfacePoseRefinementResult evidence = match.RefinementEvidence;
    // Use match.Pose and match.Coverage; review evidence.Improved and evidence.StopReason.
}
```

null option은 기존 grid path다. 보정 결과의 Success는 유효한 실행 여부이며
제품 합격이나 전역 수렴을 뜻하지 않는다. 직접 seed 보정은 지지가 부족해도
valid seed를 보존할 수 있으므로 Coverage와 StopReason을 함께 확인한다.
중단 후 마지막 fit 대응점은 거부된 trial의 입력일 수 있다. 최종 채택 대응점은
Coverage.Matches다. 복수 결과의 모든 scene order는 원본 입력 번호다.

64쌍의 표본과 전체 재평가는 전체점 최적 fit, 센서 정확도, 생산 Takt나 전역 수렴을
보장하지 않는다. 마지막 fit의 표본과 최종 coverage를 구분해 소비한다.

## 계산 비용과 협력적 취소

Host는 worker마다 입력·옵션·Tool을 분리하고 호출이 끝날 때까지 입력을 유지한다.
동기 호출에 token을 전달한 뒤 결과를 보존하거나 `OperationCanceledException`을
처리한다. 2D 결과와 Tool의 native 이미지는 각 생성 owner가 해제한다. 취소한
token은 다시 쓰지 않고, 정상 입력과 새 token 또는 `CancellationToken.None`으로
같은 Tool의 복구를 확인한다. 저장·종료·재시작은 이 메모리 내 호출의 책임이 아니다.

### 비용 판단

M은 모델 샘플, S는 장면 샘플, R은 회전 후보, I는 최대 instance, L은 보정 반복이다.
현재 구현의 비용 판단은 아래와 같다. 후보 예산은 후보 개수의 제한이며 각 후보의
샘플 탐색 시간이나 native 호출 시간을 제한하지 않는다.

| 경로 | 비용 판단 | 운영 판단에 필요한 이유 |
| --- | --- | --- |
| Coverage | 한 번의 전체 비교는 `O(M*S)`, 한 후보의 데이터/결과 공간은 `O(M+S)` | 장면만 커져도 내부 최근접 탐색이 길어진다. 입력 검증과 결과 보존 비용도 포함한다. |
| Single grid | `O(R*M*S)`에 입력 검증·centroid·축 배열이 추가 | R뿐 아니라 M/S를 함께 기록해야 입력 규모를 비교할 수 있다. |
| Multiple | 후보 상한 `I*R*M*S`, coverage 재실행의 보수적 상한 `O(I*R*M²*S²)` | translation 거부와 claim 뒤의 장면 감소가 실제 비용을 줄이므로 평가한 후보 수도 남긴다. |
| Local refinement | 전체 재평가는 `O((L+1)*M*S)` | fit의 64쌍 ceiling이 전체 support/residual 재평가의 비용을 없애지는 않는다. |
| 2D native/edge | 이미지 면적, 모델점, 검색·진단과 native kernel에 의존 | source copy, result clone, diagnostics를 포함한 실제 `Execute`를 측정한다. |

### 취소와 소유권 계약

managed 데이터 규모 반복은 index 0과 이후 최대 1,024회 간격, 반복
종료에서 취소를 확인한다. validation, 축 후보 생성, centroid, coverage의 내부
scene scan, available 구축, 대응점 mapping/claim, refinement 근거 복사가 해당한다.
기존 candidate/fit 단계의 확인도 유지한다. 마지막 유효 샘플 getter에서 취소가
발생해도 다음 단계나 성공 반환 전에 관찰한다. pre-cancel은 입력을 읽기 전에 OCE와
원래 token을 전달하며, token이 없는 invalid-input 결과는 기존 typed failure를 유지한다.
동시 malformed-input/취소의 우선순위나 반환 직후의 취소까지 원자적으로 보장하지 않는다.

이 기준은 반복 작업량의 관찰 간격이다. centroid는 반복당 getter 3회, available
구축은 2회이므로 getter 수를 반복 횟수와 구분한다. 배열 할당·GC, caller의
Count/indexer 내부 작업, OS scheduling, native OpenCV 실행은 millisecond 상한으로
제한하지 않는다. [Microsoft의 cooperative cancellation 계약](https://learn.microsoft.com/en-us/dotnet/standard/threading/cancellation-in-managed-threads)을 따르며
`Task.Run`이나 취소 timer를 강제 중단으로 해석하지 않는다.

취소 관찰은 기존 SurfaceMatching 모듈이 소유한다. token의 실제 경로는
`Search/Multiple -> shared validation/axis/centroid 또는 available -> Coverage ->
optional Refiner -> paired solver/전체 Coverage -> evidence snapshot/original ID ->
return`이다. claim 배열과 후보/list writer는 각 `Execute`의 local owner이며 token
요청은 caller의 책임이다. 3D 결과는 managed 수명이고, 2D Tool/Mat/result의 생성과
해제는 worker가 책임진다. mutable 입력/options를 공유하거나 같은 Tool의
Execute/Dispose를 겹쳐 호출하는 계약은 추가하지 않았다.

관련 호출은 [SdkExecutionSmokeSuite](../tests/OpenVisionLab.Inspection.Smoke/Suites/SdkExecutionSmokeSuite.cs)의
취소/복구 사례와 대응 Execute, shared validation/Coverage, refinement snapshot 순서로
읽을 수 있다. Vision3D는 netstandard2.0과 Compute에 의존하며 application Host, UI,
파일 또는 network adapter를 요구하지 않는다.

## Execution, failure and lifetime boundaries

| Call family | Failure / normal result | Recovery and lifetime |
| --- | --- | --- |
| `IThreeDInspectionTool.Execute(HeightMap3D)` | `MeasurementOutcome` distinguishes `Passed`, `OutOfTolerance`, `NotMeasured` | Preserve measured values on tolerance failure. For `NotMeasured`, inspect error code and correct units, ROI, missing coverage or configuration. |
| Geometry/construction Tools | Typed result; invalid/degenerate input may throw `ArgumentException`; arithmetic overflow may throw `InvalidOperationException` | Catch at the host operation boundary; discard this measurement. Do not reuse coefficients/results from an earlier call. |
| Typed dimensional inspections | `Passed=false` is a valid measurement outside tolerance; invalid inputs/calculations may throw | Retain finite metrics and the applied tolerance separately from execution failure. |
| Overloads with `CancellationToken` | Only the declared overload offers cooperative cancellation | Keep input alive until completion and handle `OperationCanceledException` where propagated; other Tools have no general abort contract. |

Numeric values embedded in failure `Message` text from `LineIntersectionTool`,
`FullXyzAffineSolveTool`, and `NominalActualMeshComparisonTool` use invariant decimal
and grouping separators so logs and evidence stay comparable across host cultures.
When a typed Tool validates a member of its `options` object, the message identifies
the invalid property and the argument metadata identifies the public `options`
parameter. This keeps the correction target visible without presenting a property as
a method parameter.
Use typed status and numeric properties for control flow; localize operator-facing
text in the host rather than parsing or rewriting these diagnostic messages.

These APIs are synchronous. The height-map interface and combined runner have no
cancellation or hard-timeout parameter. `Task.Run` does not add cancellation to an
algorithm. Pass stable inputs and options throughout a call; per-worker Tool
instances avoid relying on unspecified thread safety. `HeightMap3D` is immutable;
caller-supplied arrays/lists/options for other Tools must not change while in use.
Pure 3D result DTOs need no `Mat` disposal. Hosts own sensor/file handles and any
rendering buffers they create from results.

Example outcome handling after an existing height-map call:

```csharp
switch (result.MeasurementOutcome)
{
    case ThreeDMeasurementOutcome.Passed:
        Console.WriteLine("Measurement passed the configured limits.");
        break;
    case ThreeDMeasurementOutcome.OutOfTolerance:
        Console.WriteLine("Valid measurement outside tolerance; retain metrics.");
        break;
    case ThreeDMeasurementOutcome.NotMeasured:
        Console.Error.WriteLine($"{result.ErrorName}: {result.Message}");
        break;
}
```

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

Use the [source build and focused-test route](../README.md#clone-and-develop-on-another-pc).
`--filter "plane fit"` selects plane-fit scenarios; `--filter "Volume"` selects
volume scenarios. `--filter "3D analytic qualification"` selects the independent
plane/rigid-transform, coordinate-scale, tilted-measurement and recovery cases.
Their explicit numerical tolerances and input domain are owned by
`ThreeDSurfaceAndMetrologySmokeSuite.cs`; use the current source and execution
record rather than interpreting a synthetic tolerance as sensor accuracy.
Validate package metadata and its exact clean-source commit with [Verify-PackageProvenance](../eng/Verify-PackageProvenance.ps1).

The smoke executable uses only deterministic synthetic height maps. It verifies
legacy constructor compatibility, strict unit/frame rejection, missing-sample coverage,
analytic-plane fitting, tolerance failures that retain measurements, controlled input
errors, and that a 3D step still runs after a 2D step fails. It is not a substitute for
sensor data, calibrated artifacts, or production acceptance testing.

## Surface search 강건성과 비용 경계

옵션·DTO·기본값의 원본은
[단일 탐색 계약](../src/OpenVisionLab.Vision3D/FeatureExtraction/SurfaceMatching/DeterministicSurfaceMatchingContracts.cs)과
[다중 탐색 계약](../src/OpenVisionLab.Vision3D/FeatureExtraction/SurfaceMatching/DeterministicMultipleSurfaceMatchContracts.cs)이다.

- 단일 탐색의 기본 centroid 후보와 순서는 유지한다. 선택형 sample-pair anchor는 부분 표면/배경
  때문에 centroid가 이동하는 재현 입력을 보완한다. 모든 후보를 사전에 예산에 포함하고 초과하면
  grid를 잘라서 실행하지 않는다. 다중 탐색은 기존 anchor/expanded budget 계약을 유지한다.
- 자세 모호성은 raw Matched/selected pose와 별도 evidence다. 명시한 model-origin 위치 범위 안의
  다른 coarse 회전을 지지와 RMSE로 비교한다. 정책은 snapshot이며 연속 refinement와 별도다.
  모호하지 않다는 결과는 선언한 grid에 한정되고 전역 자세 유일성이나 scene object identity를 증명하지 않는다.
- `single/multiple/refiner -> ExecuteValidated -> coverage claim/RMSE -> conditional SurfaceSceneIndex`
  경로가 실제 사용된다. index는 변경되지 않는 scene 실행 동안만 재사용하며 multiple의 available
  scene이 바뀌면 새로 만든다. 원래 model 순서·unique claim·동률의 낮은 scene order·inclusive 거리와
  안정적 scaled RMSE를 보존한다. source/replay ID는 기존 원본 좌표의 order를 유지한다.
- 전수 coverage owner의 linear path는 적은 질의와 constant-X scene에서 남는다. X 정렬만으로
  충분히 줄일 수 없는 일반 분포는 선형 최악 비용을 가진다. 더 큰 tree/전역 cache는 이번에 넣지 않았다.
  인덱스 배열은 생성/정렬 뒤 읽기 전용이며 public result에 노출되지 않는다. managed 할당 증가와
  local 시간 분포를 함께 기록한다. CPU 작업량 예산은 시간 SLA가 아니다.

공개 입력은 caller가 Execute 동안 안정적으로 유지한다. scene index와 후보/선점 상태는 실행
지역이며 회전 비교/탐색은 기존 concrete owner를 재사용한다. code-reading 순서는 search Execute,
coverage ExecuteValidated, SurfaceSceneIndex, `SurfaceSearchRobustnessSmokeSuite`이다.
순서·지지·잔차와 취소/복구의 검증 입력은
[SurfaceSearchRobustnessSmokeSuite](../tests/OpenVisionLab.Inspection.Smoke/Suites/SurfaceSearchRobustnessSmokeSuite.cs)를 따른다.
후보·CPU 작업량 예산은 latency나 현장 성능의 상한을 보장하지 않는다.


## Reliability and challenge boundary

Point-pair/cross-section nonrepresentable derived arithmetic throws
InvalidOperationException before tolerance comparison. Adaptive indexing and
spatial-fit contracts are owned by the corresponding SurfaceMatching source and tests.

Use SurfaceChallengeEvaluationSmokeSuite/--surface-challenge for independent analytic
pose/physical IDs, development/held-out identities, repeated trials and limits.
The sampling option improves measured order-biased noise while preserving bounded
fit/full-model support/RMSE/pose checks. It does not reject all outliers or establish
global object identity. Near identical competing surfaces still permit a low-residual
fit to the wrong physical surface; the report preserves that failure. Exit0 means
evaluation completed, not every research truth gate passed.

Normals or RANSAC alone cannot identify identical XYZ support. Geometric match
evidence must remain separate from the host's physical-object acceptance policy.
