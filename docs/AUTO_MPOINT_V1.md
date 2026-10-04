# Auto MPoint V1 And Representative-Image Extension

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
