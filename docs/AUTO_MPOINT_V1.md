# Auto MPoint V1 And Representative-Image Extension

Status: Current contract; the completion evidence below is a historical
Library-Noah snapshot from 2026-07-24.

Use [`OPENVISIONLAB_CURRENT_STATUS.md`](OPENVISIONLAB_CURRENT_STATUS.md) for current
project state. Historical commands, case counts, binary versions, hashes, and
artifact paths in this file do not prove the current source. Current verification
uses:

```powershell
dotnet build OpenVisionLab.VisionSdk.sln -c Release
dotnet run --project tests\OpenVisionLab.Inspection.Smoke\OpenVisionLab.Inspection.Smoke.csproj -c Release --no-build
```

## Purpose

`AutoMPointTool` is a training-time pattern suggestion tool. It examines one
reference image, ranks fixed-size pattern windows, and returns operator-reviewable
matching points with feature, uniqueness, synthetic stability, precision, and
runtime evidence.

It does not run as an inspection Pipeline Step. The selected pattern is used later
by the existing `EdgeBasedTemplateMatchingTool`.

The optional representative-image overload evaluates the same shortlisted
templates on multiple same-size images and automatically ranks the strongest
repeatable candidate. The original one-image API and behavior remain available.

## Approved V1 Boundary

- Target matcher: existing `EdgeBasedTemplateMatchingTool` with hybrid verification.
- Analysis area: the whole input image or one explicit rectangular ROI.
- Candidate size: operator-supplied fixed width and height.
- Candidate modes: grid search, whole analysis ROI, or both.
- Result: up to five non-overlapping accepted candidates.
- Exact checks: self-location, strongest alternative location, three known synthetic
  image variations, and measured matching elapsed time.
- Output state: `Suggested`, never production-qualified from one image.
- Apply behavior: no automatic template save, recipe mutation, Preview, or Run.

V1 does not provide automatic model-size selection, SIFT/ORB candidate generation,
semantic object/background segmentation, production-image qualification, automatic
three-point affine grouping, homography, or field-robustness claims.

## Representative-Image Ranking

`Execute(reference, representativeImages)` adds one bounded selection stage after
the existing one-image feature, uniqueness, and synthetic gates:

1. Crop every accepted finalist from the reference image.
2. Run the existing edge matcher on every representative image.
3. Record each image's `Success`, `Ambiguous`, or `NoMatch` result, score,
   uniqueness margin, pose, and runtime.
4. Reject a finalist below `MinimumRepresentativeSuccessRate`.
5. Rank survivors by representative success rate, minimum uniqueness margin,
   mean match score, then the original one-image score.

This ordering deliberately favors the candidate with the strongest worst-case
separation when multiple candidates have the same success rate. It does not infer
which physical feature the operator intended.

## Commercial Design Basis

Cognex Auto-Select accepts an image, a model size, and target pattern-location
tool, then returns suggested training windows. Its documented scores include
symmetry, orthogonality, and uniqueness. Uniqueness is evaluated by actually
training a candidate and comparing its own match with the strongest other match
in the image.

HALCON similarly exposes model-region, significant-feature, and image-pyramid
diagnostics and describes automatically determined model settings as interactive
suggestions that an operator may revise.

References:

- <https://docs.cognex.com/cvl_900/web/en/cvl_vision_tools/Content/Topics/VisionTools/Auto_Select_Tool_Overvie.htm>
- <https://docs.cognex.com/cvl_900/web/en/cvl_vision_tools/Content/Topics/VisionTools/Uniqueness_Score.htm>
- <https://docs.cognex.com/cvl_900/web/EN/cvl_vision_tools/Content/Topics/VisionTools/Score_Values.htm>
- <https://www.mvtec.com/doc/halcon/2111/en/inspect_shape_model.html>
- <https://www.mvtec.com/doc/halcon/2405/en/determine_shape_model_params.html>

OpenVisionLab Vision SDK follows the same staged principle without copying proprietary
implementation details:

1. Score all windows with inexpensive contrast and edge-distribution measures.
2. Remove heavily overlapping windows.
3. Run the existing target matcher only on the strongest finalists.
4. Fail candidates with weak self-location, a strong distant alternative, unstable
   known-transform replay, excessive position error, or an enabled runtime gate.
5. Rank only candidates that pass all enabled gates.

## Input Contract

`AutoMPointToolProperty` defines:

- `AnalysisRoi` and `UseAnalysisRoi`.
- `CandidateMode`.
- `PatternWidth`, `PatternHeight`, and `CandidateStride`.
- `MaximumFinalists` and `MaximumResults`.
- Pre-filter gates for contrast, edge density, quadrant balance, orientation balance,
  and combined feature quality.
- Existing edge matcher angle, scale, score, Canny, and search settings.
- Minimum uniqueness margin.
- Minimum synthetic success rate and maximum position, angle, scale, and runtime
  errors.
- `MinimumRepresentativeImageCount` and
  `MinimumRepresentativeSuccessRate` for the optional multi-image overload.

Invalid dimensions, ROI, ranges, counts, and non-finite gates fail before analysis.

## Candidate Generation And Feature Score

The input image is converted to grayscale once. Canny edges and absolute Sobel X/Y
responses are computed once for the source image and sampled only through candidate
windows inside the analysis area.

Each window publishes:

- grayscale standard deviation;
- edge density;
- four-quadrant balance;
- X/Y gradient orientation balance;
- geometric-mean feature quality.

The geometric mean prevents one strong component from hiding a missing component.
Windows below any configured gate do not enter exact matching.

Finalists are selected by descending feature quality with deterministic coordinate
tie-breaking and overlap suppression.

## Exact Matching And Uniqueness

Each finalist is cropped as a template and evaluated with one
`EdgeBasedTemplateMatchingTool` instance configured for two results.

The self match is the result nearest the taught window. A distant alternative is
the strongest result outside the self-location tolerance.

```text
UniquenessMargin = (SelfMatchScore - AlternativeScore) / 100
```

No self result or a margin below the configured minimum rejects the candidate.

The authored MPoint is the geometric center of the candidate ROI. The existing edge
matcher reports its native edge-model center, so each result retains both points and
their reference offset. A future OpenVisionLab adapter can transform this fixed
offset with the reported pose without redefining the taught MPoint.

## Synthetic Stability And Precision

The complete reference image is replayed under three known variations:

1. translation;
2. translation plus a small permitted rotation;
3. translation plus a small permitted scale and photometric change.

The known transform is applied to the self-match center. The highest returned match
is compared with that expected point. The tool records:

- replay success rate;
- mean and maximum position error in pixels;
- maximum angle error in degrees;
- maximum scale error as a ratio;
- median and P95 matching elapsed milliseconds.

These measurements prove only deterministic one-image synthetic behavior. Actual
production variation requires an independently supplied N-image verification set.

## Output Contract

`AutoMPointCandidateResult` retains:

- rank, candidate ROI, authored MPoint, and native match center;
- accepted/rejected state and exact reject reason;
- feature component scores;
- self, alternative, and uniqueness scores;
- synthetic success and pose error metrics;
- median and P95 runtime;
- representative-image count, success count/rate, mean/minimum score,
  mean/minimum uniqueness, P95 runtime, and per-image outcome evidence;
- combined ranking score.

The result image draws evaluated finalists, accepted suggestions, ranks, MPoints,
and rejection marks. Public overlays retain the selected candidate rectangles,
MPoints, and rank/score labels.

## Acceptance Criteria

V1 is complete only when current-source smoke evidence proves:

1. a unique asymmetric pattern is suggested with finite feature, uniqueness,
   precision, and runtime metrics;
2. a repeated pattern fails the uniqueness gate;
3. an invalid ROI or pattern size fails closed with a stable error;
4. candidate ranking and drawings are deterministic for identical inputs;
5. the complete OpenVisionLab Vision SDK solution builds and the full inspection smoke runner
   passes.
6. multiple representative images select the candidate with the strongest
   cross-image evidence rather than the highest reference-image score alone;
7. missing, undersized, empty, or size-mismatched representative sets fail closed.

## Product Integration Boundary

OpenVisionLab integration is a separate step after this library contract passes.
The PropertyGrid UI will expose explicit `Analyze candidates` and `Use this pattern`
actions. Merely selecting a candidate must not save a template, mutate a recipe,
change a layer route, Preview, or Run.

## Historical completion evidence

The following commands and counts were recorded against the former Library-Noah
layout on 2026-07-24. The named solution and runner no longer exist in this checkout.

```text
dotnet build Lib.Common.sln -c Release
Build succeeded: 0 warnings, 0 errors

dotnet run --no-build --project Lib.Inspection.Smoke\Lib.Inspection.Smoke.csproj -c Release
Lib.Inspection.Smoke | 60/60 passed
```

The Auto MPoint smoke matrix proves:

- the unique asymmetric pattern is ranked first at ROI `64,64,64,64`;
- the result is deterministic across two executions, including the drawing pixels;
- two identical patterns both fail with `UniquenessMargin 0 < 0.1`;
- out-of-image analysis ROI and oversized pattern definitions fail with stable
  Auto MPoint errors.

Historical evidence path recorded at the time:
`artifacts\auto_mpoint_v1_20260724`. It is not present in the current checkout.

Representative-image extension verification on 2026-07-24:

```text
dotnet build Lib.Common.sln -c Release
Build succeeded: 0 warnings, 0 errors

dotnet run --no-build --project Lib.Inspection.Smoke\Lib.Inspection.Smoke.csproj -c Release
Lib.Inspection.Smoke | 66/66 passed
```

The added matrix proves that actual representative-image results can reverse a
reference-only candidate ordering and that invalid representative sets fail with
`AutoMPointRepresentativeImageInvalid`.

Historical evidence path recorded at the time:
`artifacts\auto_mpoint_representative_v2_20260724`. It is not present in the
current checkout.

Historical Release output provenance:

```text
Lib.OpenCV.dll assembly version: 2.1.0.0
Lib.OpenCV.dll file version: 2.8.0.0
SHA-256: B456BE7AFC002BA1535A5892092B746FB44560300961BD71342AAC0E7741B180
```

This is deterministic synthetic and library-API evidence. The OpenVisionLab P229
pilot separately supplies bounded same-source multi-image evidence. Automatic
pattern-size selection, semantic feature identity, production qualification, and
field qualification remain outside this completed slice.
