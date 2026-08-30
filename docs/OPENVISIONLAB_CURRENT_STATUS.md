# OpenVisionLab Vision SDK Current Status

Updated: 2026-08-31
Project work item: `PL-0002`
Overall state: `resolved`

## Authority

This file is the single current human-readable authority for product identity,
ordered engineering priorities, completion criteria, and verification boundaries.
[`docs/README.md`](README.md) is the navigation index. The machine-readable
`.proofline/issues/PL-0002.json` ledger records live milestone state and evidence;
it is not a second design or release authority.

If a dated plan, completion record, benchmark count, command, version example, or
artifact path conflicts with this file, treat the dated material as historical
evidence. Source code and `Directory.Build.props` remain authoritative for the
implemented API and build metadata respectively. A source change that affects a
catalog or example requires this document set to be checked again.

## Current product identity and boundary

OpenVisionLab Vision SDK is a UI-independent C# `netstandard2.0` library for
OpenCvSharp-based 2D inspection and height-map/full-XYZ 3D computation. It ships
five packages: `OpenVisionLab.Core`, `OpenVisionLab.Vision2D`,
`OpenVisionLab.Vision2D.Blob`, `OpenVisionLab.Vision3D`, and
`OpenVisionLab.Inspection`.

- The SDK owns deterministic algorithms and explicit typed input, result, error,
  unit, frame, missing-sample, coverage, and numerical evidence contracts.
- The consuming host owns sensor acquisition, calibration and provenance, recipe
  tolerance, ROI teaching and rendering, Preview/Run, PLC/I/O, deployment, and the
  final product decision.
- Windows x64 is the current native runtime contract because Core packages
  `OpenCvSharpExtern.dll` for `runtimes/win-x64/native`.
- `Version=3.0.0` and `AssemblyVersion=3.0.0.0` are the API/assembly baseline.
  `PackageVersion=3.0.1-dev.1` is only the repository-local default. A package that
  can leave its build directory must use one new immutable prerelease version.
- Existing `C*`, `CV*`, and `LineGuage` compatibility types remain available
  throughout 3.x. Their removal is a separately gated 4.0 change.

## Current progress

`PL-0002` is `resolved`. All five milestones passed the final local integration
gates, and the verified implementation was committed as
`c066f16e9a6f38863b71e935d483483dc06618c6` and pushed to `origin/main`. This
delivery does not include package publication, consumer adoption, a tag, a release,
or deployment.

| Priority / milestone | State on 2026-08-31 | Immediate outcome |
| --- | --- | --- |
| 1 / M1 | complete; pushed | Contour mask evidence and Pipeline output routing are fixed and covered by direct regressions. |
| 2 / M2 | complete; pushed | Large-coordinate, finite-statistics, region-bound, and LineGauge depth boundaries are fail-closed and covered. |
| 3 / M3 | complete; pushed | Current authority/index, catalogs, versions, examples, provenance, and historical labels match the checked source tree. |
| 4 / M4 | complete; pushed | Direct 2D execution plus coverage, exact public-API, and analyzer no-regression gates pass locally and are wired into CI. |
| 5 / M5 | complete; pushed | Five packages and the isolated Windows x64 consumer pass with one native DLL at the consumer output root. |

## Current integrated evidence

The final candidate evidence is under
`D:\OpenVisionLab-TestData\OpenVisionLab-Vision-SDK\PL-0002\final-candidate-20260831-a1`.
The checked working tree was committed as
`c066f16e9a6f38863b71e935d483483dc06618c6` and pushed to `origin/main`.

- .NET SDK `8.0.423`; Release build: `0` warnings and `0` errors; full synthetic
  smoke: `220/220` passed.
- Reproducible line-coverage gate passed each reviewed baseline:

  | Assembly | Observed | Required minimum |
  | --- | ---: | ---: |
  | `OpenVisionLab.Core` | 21.68% | 20.00% |
  | `OpenVisionLab.Inspection` | 69.92% | 68.00% |
  | `OpenVisionLab.Vision2D` | 72.32% | 68.00% |
  | `OpenVisionLab.Vision2D.Blob` | 69.17% | 68.00% |
  | `OpenVisionLab.Vision3D` | 90.68% | 89.00% |

- The exact public-API gate passed `3,295/3,295` baseline/current entries across
  five assemblies. The strengthened gate records 415 exported types, parameter
  names and modifiers, enum underlying types, property/event modifiers, and rejects
  unreviewed additions as well as removals or changes. Missing-entry and renamed-
  parameter negative checks failed as intended.
- The `latest-recommended`/`All` analyzer gate reported 596 existing `CA*`
  diagnostics at or below their per-code baseline. A zero-diagnostic baseline update
  and an analysis-mode mismatch failed as intended. The gate uses
  `--no-incremental`; two consecutive runs against the same artifacts path each
  reproduced all 596 diagnostics and passed.
- All five packages were created as `3.0.1-verification.20260831.1`; an isolated-cache
  `net8.0`/`win-x64` package-only consumer passed its 2D native and 3D managed
  checks. Its 13-file output totals `55,121,250` bytes and contains exactly one
  `OpenCvSharpExtern.dll`, directly at the output root.
- Package SHA-256 values are: Core
  `DD20A789D2FB2CEEB0396A47144DD4C2BEDFB7DBAD6DEBE66D001D9DC6CC667E`;
  Inspection
  `82EE6F92BB29B7B8CE3C70527C5A0D1301DCC20653A4B87145D47900212E3694`;
  Vision2D
  `170A87137EA3C801C61CCA7E425BDDB42B0D1B494ACE4B79C0FEA552FBD48436`;
  Vision2D.Blob
  `95C4D0A3CBC85697263A4DDA73FB188E6F4E3697CC54326E3EEF895401193727`;
  Vision3D
  `A42892EC4BACB76AB2869BEDC18725B0AC61423144820D8F2FEEF0795FF2745F`.

The `buildTransitive/OpenVisionLab.Core.targets` fallback for
`TargetFrameworkIdentifier == .NETFramework` was reviewed from source only. No
.NET Framework runtime consumer was executed, so that fallback remains unverified.
No package was published, no consumer repository was changed, and no tag, release,
or deployment was performed.

## Priority 1 — 2D result-contract correctness

Cause: the audited Contour path exposes `CvMASKS` but does not apply the mask to
accepted Contour results, while the Pipeline output condition can attempt to write an
empty `OutputLayer` or a missing `ResultImage`. Both defects cross a public contract
boundary rather than being display-only issues.

Scope:

- apply source-coordinate `CvMASKS` consistently to Contour single-ROI,
  multi-ROI, and square-result paths: keep the source-coordinate candidate with a
  `Masked` decision, but exclude it from accepted `results` when its bounding box is
  fully contained by a mask;
- create a Pipeline layer only when the step declares a non-empty output name and
  the tool produced a non-null result image; a blank name is a no-op and a null
  image preserves any existing layer;
- preserve fail-closed acceptance, step order, caller-owned inputs, and explicit
  Preview/Run ownership.

Completion criteria:

- direct regressions prove masked Contours remain auditable as `Masked` candidates
  but are excluded from accepted whole-image, ROI, multi-ROI, and square results;
- direct regressions prove blank output names and absent result images do not create
  layers, replace an existing layer, or throw an accidental layer-name/null-image
  error;
- existing 2D behavior and the full smoke runner pass.

Verification boundary: synthetic Mats prove library behavior only. They do not prove
operator workflow, recipe migration, or field-image suitability.

Observed integration result: the Contour regressions preserve fully masked
source-coordinate candidates as `Masked` evidence while excluding them from accepted
single, multi, and square results. Pipeline regressions prove a blank output name is
a no-op and a null result image preserves an existing layer. The full `220/220`
smoke suite passed.

Recommended model: `gpt-5.6-terra` | Reasoning effort: `medium`

## Priority 2 — numerical and input-boundary reliability

Cause: the audit identified large-coordinate landmark/affine sensitivity,
non-finite or overflowing statistics, region-bound arithmetic overflow, and an
implicit `LineGaugeTool` image-depth assumption without a complete direct boundary
test matrix.

Scope:

- stabilize the approved landmark/affine calculations for finite large-coordinate
  inputs without changing the public transform meaning;
- fail closed when statistics or region arithmetic would become non-finite or
  overflow;
- accept only unsigned 8-bit (`CV_8U`) LineGauge input depth; convert supported
  multi-channel `CV_8U` input to grayscale single-channel data before execution,
  and return `InputImageInvalid` for other depths while preserving ROI, direction,
  and legacy compatibility contracts.

Completion criteria:

- analytic large-coordinate cases recover the expected transform within an authored
  tolerance, and degenerate/non-finite cases remain controlled failures;
- finite-statistics and region-bound limit tests cover the audited overflow paths;
- `CV_8U` LineGauge input executes after any required grayscale conversion, and
  every other depth fails with a stable public error;
- focused boundary checks and the full smoke runner pass.

Verification boundary: numerical/synthetic checks do not establish sensor accuracy,
calibration validity, Gauge R&R, or production tolerance capability.

Observed integration result: `+1e12` common-translation landmark/affine cases,
finite-aggregation failure, overflow-safe region bounds, and non-`CV_8U`
LineGauge rejection pass in the full `220/220` smoke suite. This does not establish
useful precision near `double.MaxValue` or for geometry already lost to input ULP.

Recommended model: `gpt-5.6-sol` | Reasoning effort: `high`

## Priority 3 — current documentation authority and API discovery

Cause: dated completion ledgers had become de facto status documents, public 2D/3D
catalogs lagged source, `3.0.0` API baseline text was used as an install version,
examples contradicted native-resource ownership, and old commands/counts/artifact
paths were not marked historical.

Scope:

- maintain this current authority and the document index;
- keep the complete public 2D and 3D Tool catalogs aligned with source;
- distinguish the `3.0.0` API/assembly baseline from the `3.0.1-dev.1` local package
  default and from separately approved immutable package versions;
- use current solution/test entry points, dispose `OpenCvAlgorithmBase` tools and
  `VisionToolResult` instances in examples, and state image provenance limits;
- retain dated records with an explicit historical label instead of deleting them.

Completion criteria:

- every active document is reachable from `docs/README.md` and the root README links
  to the current authority;
- the public catalogs match current public `*Tool` declarations;
- active installation examples do not present `3.0.0` as the current package to
  install;
- active commands name existing projects, code examples follow the documented
  ownership contract, and stale counts/paths are visibly historical;
- all local Markdown/HTML links resolve and the issue ledger remains `doing` until
  the checked working tree has an exact source commit and approved delivery record.

Verification boundary: source/link searches prove documentation consistency at the
checked revision. They do not prove snippets compile, packages were published, or
the remaining `PL-0002` milestones passed.

Observed integration result: source/catalog comparison found all `15/15` public
non-legacy 2D Tools and `62/62` public 3D Tools. All 22 Markdown documents are
indexed or reachable as classified, all 52 checked Markdown/HTML local links and
images resolve,
active install examples distinguish `3.0.0` from an installable package version,
and active native-owner examples dispose Tools/results.

Recommended model: `gpt-5.6-luna` | Reasoning effort: `low`

## Priority 4 — reproducible quality gates

Cause: not every public non-legacy 2D Tool had a direct execution check, existing
coverage was not a reproducible release gate, and CI did not prevent accidental
public API removal or analyzer-regression growth.

Scope:

- directly execute each current non-legacy 2D Tool through its public contract;
- record reproducible coverage from a declared command and source revision;
- compare the public API to a reviewed compatibility baseline;
- prevent new analyzer warnings without requiring unrelated legacy debt to be fixed
  in the same change.

Completion criteria:

- the direct-execution matrix, coverage command and threshold, public API comparison,
  and analyzer no-regression check run locally and in CI;
- failures are actionable and do not depend on a developer machine's global package
  cache;
- existing Release build, smoke, pack, and isolated consumer gates still pass.

Verification boundary: these gates cover the declared managed/native build surface;
they do not substitute for fuzzing, security review, real sensors, or consumer UI
tests.

Observed integration result: the direct Morphology, Filter, Edge Detection, and
Rotate/Scale executions are in the smoke matrix; the coverage, exact API, and
analyzer gates above pass and are invoked by `.github/workflows/build.yml`.

Recommended model: `gpt-5.6-terra` | Reasoning effort: `medium`

## Priority 5 — native/RID package closure

Cause: the Windows x64 native runtime is packaged through Core and transitive build
targets, but the support boundary and the necessary consumer output location were
not represented as one checked contract, allowing unnecessary duplicate assets to
remain unnoticed.

Scope:

- document Windows x64 as the supported RID without implying unverified cross-RID
  support;
- retain exactly the managed/native assets and output copies required by a
  package-only consumer;
- leave package publication, consumer-repository upgrades, and other RIDs outside
  this change unless separately approved.

Completion criteria:

- all five packages are created with one unique immutable prerelease version;
- package inspection and an isolated-cache package-only consumer prove 2D native and
  3D managed execution with no unnecessary native-output duplication;
- Release build, full smoke, API/analyzer/coverage gates, and package checks pass;
- no package is published and no consumer repository is changed under `PL-0002`.

Verification boundary: package-only Windows x64 execution does not establish another
RID, installation/deployment behavior, or a published release. The `.NET Framework`
fallback target is source-reviewed but runtime-unverified.

Observed integration result: both the primary integration snapshot and the
post-gate review pack five same-version packages and run an isolated `net8.0`/
`win-x64` consumer with exactly one native DLL directly in the output root.

Recommended model: `gpt-5.6-terra` | Reasoning effort: `high`

## Commercialization gates outside PL-0002

Do not describe `PL-0002` completion as production metrology qualification.

- Sensor-backed production evidence requires the sensor model/settings, calibration
  ID/hash, part and recipe identity, independent ground truth with uncertainty,
  LSL/USL, false-accept/false-reject policy, and takt targets.
- Another official performance-baseline attempt requires a dedicated isolated
  performance host. Historical benchmark sessions do not satisfy that prerequisite.
- NuGet publication, a stable `3.0.1` release, consumer package/hash updates, tag
  creation, and deployment remain separate authorization boundaries.
- Vendored OpenCvSharp/OpenCV binaries require exact source/version/license
  provenance and any required third-party notices before commercial redistribution
  can be claimed ready. The current `NOTICE` contains project attribution only; it
  does not identify those third-party binaries, their exact versions, or their
  license notices. This repository audit did not make a legal determination.

## Completion record

Status: `Complete`

Scope: audit priorities 1–5 only; 2D contracts, numerical/input boundaries,
documentation authority, reproducible quality gates, and Windows x64 native package
closure.

Acceptance criteria: C1–C7 and M1–M5 passed with evidence recorded in
`.proofline/issues/PL-0002.json`.

Verification: .NET SDK `8.0.423`; Release `0` warnings/`0` errors; smoke `220/220`;
five coverage thresholds; 415 exported types and `3,295/3,295` exact API entries;
596 analyzer diagnostics at or below baseline; five same-version packages; isolated
`net8.0`/`win-x64` consumer pass; one native DLL at the output root.

Evidence: implementation commit
`c066f16e9a6f38863b71e935d483483dc06618c6`, `origin/main`, and the final candidate
directory above.

Boundary / next dependency: no NuGet publication, tag, release, deployment, consumer
repository change, real-sensor/metrology qualification, official performance
baseline, other RID, or .NET Framework runtime verification was performed.
