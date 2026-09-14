# SDK priorities 1–4: implementation analysis and proof plan

Updated: 2026-09-15
Work item: `PL-0016`
Decision state: analysis complete; implementation is authorized and proceeds in the
order defined here.

## User goal and product boundary

The requested outcome is to analyze the four ordered SDK priorities in detail before
editing production code, then implement every boundary that can be proved with the
available repository and input evidence.

OpenVisionLab Vision SDK remains a UI-independent C# `netstandard2.0` rule-based 2D
and 3D algorithm kernel. It owns deterministic computation, typed contracts,
validation, error classification, result evidence, and declared cancellation. A host
continues to own acquisition, calibration approval, recipe persistence, Teaching UI,
Preview/Run, PLC/MES, final product acceptance, and process isolation.

The current evidence establishes a mature synthetic contract-regression SDK: Release
build, 237 smoke cases, coverage floors, exact public API and analyzer baselines,
third-party locks, and isolated `net8.0/win-x64` package consumption passed at the
preceding `PL-0015` closure. It does not establish real-sensor accuracy, Gauge R&R,
production false-accept/false-reject rates, Takt, another runtime identifier, or
long-running field stability.

Commercial lessons retained from Cognex VisionPro, MVTec HALCON, and Euresys Open
eVision are applied as SDK design criteria rather than copied product scope:

- keep each algorithm's typed teaching/settings/result evidence available;
- make orchestration opt-in and report execution status separately from inspection
  pass/fail;
- make cancellation capability explicit and never imply native-call preemption;
- preserve deterministic candidate order, coordinates, labels, and failure meaning;
- require calibrated representative data before physical-unit or production claims.

UI, camera control, commercial IDE emulation, PLC/MES, automatic calibration
approval, NuGet publication, consumer-repository changes, Linux/macOS support,
OpenCvSharp5/.NET 8 migration, and 4.0 breaking cleanup are out of scope.

## Repository topology and reading route

`OpenVisionLab.VisionSdk.sln` is a library solution with no application startup or
launch profile. `OpenVisionLab.Inspection.Smoke` is the executable contract-check
project; `OpenVisionLab.Vision3D.Benchmark` is a separate benchmark executable. The
package-only consumer is intentionally outside the solution.

Project dependency direction is:

```text
Vision2D --------> Core
Vision2D.Blob ---> Vision2D + Core
Inspection ------> Vision2D + Vision3D
Inspection.Smoke -> Inspection + Vision2D.Blob
Vision3D.Benchmark -> Vision3D
```

Library projects target `netstandard2.0`; executable checks target `net8.0`. Core
owns the vendored OpenCvSharp managed/native package assets. This graph prevents
putting 3D orchestration into Vision3D if it requires host-style reporting, prevents
Vision2D from depending on the Blob package, and makes Vision2D the correct shared
owner for an internal binary-component/contour compatibility engine.

## Analysis method and fixed evidence

The analysis inspected the solution and project references, every relevant public
execution contract, Pipeline runtime and error mapping, matching/search loop owners,
Blob/Contour call paths, package/provenance scripts, smoke tests, and current product
documents. Temporary probes live outside the source tree at:

`D:\OpenVisionLab-TestData\OpenVisionLab-Vision-SDK\PL-0016\analysis`.

The probes are evidence, not distributable product files:

| Evidence | Observation | SHA-256 |
| --- | --- | --- |
| `BlobContourProbe/baseline-4.4-managed-4.3-native.json` | Fixed current Blob/Contour examples | `B8CA3A3DAC7DE126C919EE4A493A3F58E12614D1665DE7FB7C405F603EAD9AEC` |
| `BlobContourProbe/contour-contract-baseline-4.4-managed-4.3-native.json` | Current contour retrieval/approximation contract | `E7544F9967D4BA2D8089105D79C51ACE5041A410E2A117557FDEED91F8DC742B` |
| `BlobContourProbe/native-compat-differential.json` | 1,600 exact contour-sequence comparisons; zero failures | `6F948CC7E7AF44000DFEAB954A262182B678921F373B085AE2C62AB414F92426` |
| `BlobContourProbe/connected-components-differential.json` | 500 exact component/candidate comparisons; zero failures | `8B661A008E0A7ED7163A0EF527F8F018FB8ADE83A6B82F562D241126AF1E9335` |

The differential probes use seeded generated binary images. They prove the examined
contract space and guide focused product tests; they are not sensor or production
qualification.

## Priority 1 — opt-in typed 3D execution adapters

### Current structure and gap

Vision3D currently has 61 public `*Tool` classes and 63 public `Execute` overloads.
Forty-three overloads accept `CancellationToken`; twenty do not. Only three
height-map tools implement `IThreeDInspectionTool` and run through
`CombinedInspectionRunner`: `ThicknessInspectionTool`, `WarpageInspectionTool`, and
`DatumPlaneRawHeightDeviationInspectionTool`.

`CombinedInspectionRunner` has a deliberately narrow contract. It accepts one
`HeightMap3D`, executes all configured 2D tools and then those three 3D tools,
continues after inspection failures, and owns disposal only for collected 2D
`VisionToolResult` instances. The other 3D tools accept heterogeneous typed inputs
and return heterogeneous results. Some results expose `Success`, some expose
`Passed`, and some are pure computed values. Treating those members as one meaning
would confuse execution completion with a domain decision.

### Alternatives considered

| Choice | Result |
| --- | --- |
| Extend `IThreeDInspectionTool` and `CombinedInspectionRunner` for every 3D shape | Rejected: it forces unrelated inputs/results into a false common DTO and reopens a completed narrow owner. |
| Reflection-based invocation and result inspection | Rejected: it moves type errors to runtime and makes cancellation/pass-fail semantics implicit. |
| Host-authored delegates with a typed adapter and an execution-only report | Selected: it preserves compile-time input/result construction while giving hosts one sequential report. |

### Intended owner and contract

`OpenVisionLab.Inspection` will own a new, separate `ThreeDToolExecutionRunner` and
adapter contract. The existing combined runner remains unchanged and canonical for
its current 2D plus three-height-map workflow.

The public surface will be additive:

- `IThreeDToolAdapter` exposes the normalized name, result type, real cancellation
  capability, and one execution entry point;
- `ThreeDToolAdapter<TResult>` wraps either `Func<TResult>` or
  `Func<CancellationToken, TResult>`; capability is derived from the constructor and
  cannot be asserted with a free-standing Boolean;
- `ThreeDToolExecutionStatus` has only `Completed`, `Canceled`, and `Faulted`;
- a non-generic execution-result base supports common reporting, while
  `ThreeDToolExecutionResult<TResult>` retains the exact typed `TResult`;
- `ThreeDToolExecutionReport` retains ordered per-step evidence and total elapsed
  time; `ThreeDToolExecutionRunner` executes sequentially.

Normal call path:

```text
host typed input/options/tool
  -> host creates ThreeDToolAdapter<TResult> delegate
  -> ThreeDToolExecutionRunner.Run(adapters, token)
  -> adapter invokes the typed Execute overload
  -> typed result remains available through ThreeDToolExecutionResult<TResult>
```

The runner reports an ordinary exception as `Faulted` and continues to the next
adapter. Cancellation records `Canceled`, stops later adapters, and is never
reported as a failed inspection. Non-cancellable delegates receive only pre/post
checkpoints; their call cannot be interrupted mid-execution. The report does not
dispose captured inputs or returned typed objects. The host remains their lifetime
owner, including any future disposable result.

### Acceptance and proof

- exact typed-result recovery without casts from unrelated DTO fields;
- ordered completion, fault-and-continue, cancel-and-stop, and non-cancellable
  post-call cancellation cases;
- capability derived from delegate shape;
- no new dependency from Vision3D to Inspection and no change to the combined
  runner's call path or disposal contract;
- package-only consumer example plus exact public-API review.

## Priority 2 — cooperative cancellation for long 2D search paths

### Current structure and gap

All current 2D tools implement `IVisionTool.Execute(Mat)`. That 3.x signature has no
token. `OpenCvAlgorithmBase.Execute` catches every `Exception` and converts it to a
`VisionToolResult`, so merely throwing `OperationCanceledException` inside a derived
tool would currently turn cancellation into a generic failure.

`VisionPipelineRuntime.Run` and `RunWithFailureResults` are synchronous and lack
cancellation overloads. `StepCanceled` (`301`) already maps to result status
`Canceled`, but no common runtime path produces it. `MaxElapsedMilliseconds` is a
post-execution acceptance check and cannot stop a running call.

The high-cost managed loop owners are `MatchingTool`,
`EdgeBasedTemplateMatchingTool`, `AutoMPointTool`, and `SiftTool`. OpenCV calls such
as `MatchTemplate`, feature extraction, descriptor matching, and homography are
native blocking calls. Cooperative cancellation can stop between those calls and
within managed candidate/angle/scale/ROI loops; it cannot preempt a native call that
is already executing. A host that needs forced termination still owns process
isolation.

### Alternatives considered

| Choice | Result |
| --- | --- |
| Change `IVisionTool.Execute(Mat)` | Rejected for 3.x binary/source compatibility. |
| Run every tool on `Task` and abandon it on cancellation | Rejected: work and native resources continue without a deterministic owner. |
| Add a marker plus token overload only to cooperative tools | Selected: capability is queryable and legacy behavior remains exact. |

### Intended owner and semantics

`OpenVisionLab.Vision2D.Tool` will own additive
`ICancellableVisionTool : IVisionTool` with
`Execute(Mat, CancellationToken)`. `OpenCvAlgorithmBase` will retain legacy
`Execute(Mat)` behavior and provide a cancellable protected execution path that
checks before/after input cloning, validation, and algorithm execution. The token
overload propagates `OperationCanceledException`; the legacy overload continues to
return a normal failed result for exceptions as it does today.

The four matching/search owners will implement the new interface and add
checkpoints at their managed ROI, candidate, angle, scale, model, finalist, and
representative loops. `ParallelOptions.CancellationToken` will be used where the
existing search is parallel. Each native call is bracketed by checkpoints.

`VisionPipelineRuntime` will add token overloads without changing existing ones:

- cancellable `Run` propagates `OperationCanceledException` and disposes already
  completed owned results through the existing failure cleanup;
- cancellable `RunWithFailureResults` appends one `StepCanceled` result and stops;
- for a legacy/non-cooperative tool, the runtime checks immediately before and after
  execution; if cancellation is observed after a returned result, it disposes that
  result before recording cancellation;
- neither overload starts background work, and no timeout claim is added.

### Acceptance and proof

- pre-cancel, managed-loop cancel, native-call boundary cancel, and cancel-after-
  noncooperative-call tests;
- no later Pipeline step executes after cancellation;
- exactly one `StepCanceled` result on the failure-result path and propagated OCE on
  the throwing path;
- completed result/image ownership remains leak-free;
- legacy overload tests and public API remain compatible.

### Implemented M3 checkpoint

Implementation commit `6d92357a7c3c8a072a605105cadc1c08892f9d04` adds the
optional `ICancellableVisionTool` contract without changing `IVisionTool`, adds both
Pipeline token overloads, and places cooperative checkpoints in Matching,
EdgeBasedTemplateMatching, AutoMPoint, and SIFT. `StepCanceled` is emitted only when
the caller token is actually requested; an unrequested cancellation-shaped custom
Tool exception retains `ToolExecutionException`, while cancellation raised during
factory execution is classified from the requested caller token.

The M3 candidate passed Release build with 0 warnings/errors, all 5 focused
cancellation/error/lifetime cases, the exact 3,398-entry public API, and the
unchanged 411-diagnostic analyzer gate with 186 exact compatibility and 225 exact
performance identities. Five clean commit-fixed packages at
`3.0.1-pl0016.m3.6d92357.1789407712613` passed provenance and isolated package-only
consumption, with exactly one root `OpenCvSharpExtern.dll`. Reusable evidence is
under
`D:\OpenVisionLab-TestData\OpenVisionLab-Vision-SDK\PL-0016\M3\classification-recheck-1789407595483`
and `M3\final-6d92357-1789407712613`. The final PL-0016 candidate still owns the
full smoke and coverage gate.

## Priority 3 — remove OpenCvSharp.Blob and qualify OpenCvSharp4 4.13

### Current binary and dependency boundary

The SDK currently vendors managed `OpenCvSharp.dll` and
`OpenCvSharp.Blob.dll` from OpenCvSharp4 `4.4.0.20200915` source commit
`daa955...`, plus a Windows x64 `OpenCvSharpExtern.dll` built with OpenCV 4.3.0.
Only `BlobTool`, legacy `CVBlob`, `ContourTool`, their project references, and
package/provenance checks use `OpenCvSharp.Blob`.

No public SDK signature intentionally exposes an `OpenCvSharp.Blob` type. The
dependency can therefore be removed without changing the current typed consumer
surface, provided observable labels, ordering, geometry, retrieval, approximation,
ROI/mask coordinates, and candidate evidence remain exact.

The upstream cvBlob-derived component is offered under
`LGPL-3.0-or-later`. Removing it from shipped code and packages removes that
component-specific redistribution question. It does not constitute legal clearance
for the remaining managed/native binary bundle; distribution-owner review remains
an external decision.

### Characterized current behavior

Blob behavior:

- 8-neighbor connectivity;
- stable row-major component identity, exposed as one-based labels;
- pixel-count area and pixel moments;
- inclusive left/top/right/bottom bounds;
- centroid and central-moment angle in radians;
- source-coordinate ROI/mask handling and stable `NativeIndex` identity.

Contour behavior:

- connected-component order is row-major; each outer contour is followed by its
  holes;
- `External` suppresses holes; the other currently accepted retrieval modes expose
  the same outer-plus-hole set;
- `ApproxNone` uses direction-change chain vertices; every other accepted
  approximation mode applies closed-loop Ramer-Douglas-Peucker with delta `1`;
- start point, winding, duplicate-start selection, candidate order, and source
  coordinate translation are observable contract details;
- area, bounding rectangle, and minimum-area rectangle are computed from the final
  published polygon.

A direct `Cv2.FindContours` substitution failed exact comparison because OpenCV's
component order, winding, start point, point count, and asymmetric-shape area differ.
The selected compatibility algorithm uses `Cv2.FindContours` with `CComp` and
`ApproxNone`, normalizes outer/hole winding and top-left start, resolves duplicate
starts by clockwise direction priority, compresses direction changes, applies the
closed-loop delta-1 simplification when requested, sorts outer contours row-major,
and emits each outer followed by its child holes. It passed 1,600 exact point-
sequence comparisons across 200 seeded images, four retrieval modes, and two
approximation policies.

The selected Blob implementation uses `Cv2.ConnectedComponentsWithStats`, one scan
of the label map for first-pixel order and moments, and the existing central-moment
formula. Stable relabeling by first foreground pixel is required because native label
identity is not the public SDK identity. It passed 500 exact seeded comparisons.

### Intended owner and removal proof

Vision2D will own internal binary-component and contour compatibility code so both
`ContourTool` and the friend `Vision2D.Blob` assembly reuse one algorithm. The Blob
package will retain public `BlobTool` and legacy `CVBlob`; it will no longer own or
ship a third-party labeling engine.

Completion requires:

- exact curated Blob/Contour fixtures plus the fixed differential evidence;
- zero source/project/package references to `OpenCvSharp.Blob`;
- deletion of the shipped `OpenCvSharp.Blob.dll` and removal from package contents;
- provenance/NOTICE/checklist updates that preserve history but describe the current
  two-binary distribution accurately;
- unchanged public Blob/Contour result contracts and package-only consumption.

### OpenCvSharp4 4.13 qualification result and migration decision

Official signed packages `OpenCvSharp4`, `OpenCvSharp4.runtime.win`, and
`OpenCvSharp4.runtime.win.slim` version `4.13.0.20260627` were inspected in an
isolated D-drive repository copy:

| Package | SHA-256 |
| --- | --- |
| `opencvsharp4.4.13.0.20260627.nupkg` | `8ACEE778364E5EEE6495D923732CACD8D895C7F683D2144F622B54418623D12C` |
| `opencvsharp4.runtime.win.4.13.0.20260627.nupkg` | `907FC3F682B1D430EF47F6782FB2C78A8337AC9BED2E5E7F8F01B9585C42C830` |
| `opencvsharp4.runtime.win.slim.4.13.0.20260627.nupkg` | `281551A6C032D1AAB316DB9C1817BCDED5A85188B24B2EFD12C02665E7233817` |

The package signatures verified. The `netstandard2.0` managed DLL hash is
`B50D714475DAB669BE646FC33676F417540B38487B8B93DD9983EEAF040D429D`;
the slim Windows x64 native DLL hash is
`1FA122BDB8E94175E7719FB8AA8F2AB211268A756F5D0C7A13C710ED79AE30CD`.

One compile change is required: `Mat.Type()` now yields `MatType`, so
`MatchingTool`'s template cache key must store `MatType` and hash it explicitly.
With that isolated change the solution builds with zero warnings/errors. Running
the executable from its output directory passes all 192 cases before the first Blob
case; the first Blob case then fails because the old 4.4 Blob assembly is not a
compatible 4.13 extension. A focused SIFT native execution passes. Running through
`dotnet run` from the repository root also exposed a changed native loader search
assumption; direct execution from the output directory succeeds. These observations
mean 4.13 cannot replace production bytes before Blob removal and package-layout
verification.

After the Blob replacement, the full source/smoke/package-consumer matrix will be
rerun against 4.13 in isolation. Adoption requires every current gate, a reviewed
native module list, exact provenance, and one-root-native-DLL package output. If any
gate remains unresolved, production stays on the current bytes and the exact failed
condition is recorded. `PackageReference` migration is not included because it would
change the SDK's current self-contained/offline package contract. OpenCvSharp5
requires .NET 8+ and remains a separately versioned 4.0 decision.

Official references checked on 2026-09-15:

- <https://www.nuget.org/packages/OpenCvSharp4/>
- <https://www.nuget.org/packages/OpenCvSharp4.runtime.win>
- <https://github.com/shimat/opencvsharp/blob/main/docs/docfx/articles/getting-started/package-selection.md>
- <https://github.com/shimat/opencvsharp/blob/main/docs/migration-4-to-5.md>
- <https://github.com/shimat/opencvsharp/releases>

## Priority 4 — calibrated 2D metrology and comparison gates

### Current evidence gap

The repository has a legacy demonstration input and six synthetic documentation
images. They have no complete sensor, calibration, source-image, parameter,
generator, commit, or checksum manifest and are explicitly illustrative. Current
smoke fixtures prove deterministic software contracts only. `LineGaugeTool` reports
pixel-space evidence; it is not a calibrated physical-unit metrology contract.
There is no current golden-comparison or color-inspection public Tool.

No repository input currently supplies all of:

- sensor model/serial/firmware, lens, exposure, gain, lighting, trigger and
  acquisition settings;
- calibration ID/hash, coordinate frame, unit, validity range and approval;
- part/lot, fixture, recipe, operator/environment, normal and labeled defect samples;
- independent ground-truth method, unit, uncertainty and repeat measurements;
- LSL/USL plus allowed false-accept/false-reject policy;
- target hardware, cold/warm timing distribution, memory, and Takt limit.

Building a physical-unit fixture, gauge, golden, or color algorithm before those
contracts would create an API whose accuracy and acceptance meaning cannot be
verified. That would violate the SDK's fail-closed rule and the explicit “after the
contract and representative-data gates exist” condition in priority 4.

### Implement-now boundary

The SDK will add an executable representative-data manifest schema, a documented
template, and `eng/Verify-Calibrated2DBaseline.ps1`. The verifier will fail closed
for missing fields, files, malformed hashes, unit/frame mismatches, missing labels or
ground truth, absent tolerance/error policy, and absent performance context. It will
write a deterministic validation report to the caller-selected D-drive output path
and will not execute algorithms, approve calibration, or copy user data.

The canonical physical data root is:

`D:\OpenVisionLab-TestData\OpenVisionLab-Vision-SDK\production-baseline`.

Once a real manifest and data set pass, the next design review must define each
algorithm independently: calibrated fixture/frame transform, physical-unit gauge
primitives, registered golden comparison, and color-space/illumination contract.
Each needs normal/defect acceptance cases, uncertainty and tolerance boundaries,
false-accept/false-reject evaluation, and Takt evidence. Until then those public
algorithm APIs are blocked by external data rather than by more source inspection.

### Acceptance and proof

- a complete synthetic contract fixture passes the manifest verifier without being
  described as production evidence;
- missing/modified source files and every required contract group fail closed;
- validation has no Preview/Run, calibration approval, or data-copy side effect;
- the current status names the exact missing real-data prerequisite and does not
  claim physical accuracy or production readiness.

## Implementation order and stop gates

The following order minimizes rework and preserves reviewable boundaries:

1. Add the independent 3D adapter/report owner and focused/package-consumer tests.
2. Add the cancellable 2D interface, base/runtime overloads, four search-tool
   checkpoints, and lifetime/error tests.
3. Replace Blob/Contour internals, remove the dependency and shipped asset, then run
   the isolated OpenCvSharp4 4.13 qualification against the replacement.
4. Add and test the calibrated-data contract gate. Stop public metrology/comparison
   API work if no real approved data set passes it.

Each implementation milestone must pass focused smoke, Release build, exact public
API and analyzer checks appropriate to its changed dependency graph. The final
source candidate must additionally pass full smoke/coverage, documentation and
third-party gates, fresh commit-fixed packaging, isolated package consumption, and
native-output inspection. Publishing, tagging, releasing, deployment, and consumer
repository mutation remain separate authorization boundaries.

## Closure rule

`PL-0016` can be recorded `Complete` only when all four implementation boundaries
above have the required evidence. If priorities 1–3 and the priority-4 gate pass but
no approved representative data exists, the work remains `Blocked` at the calibrated
algorithm boundary and names the exact manifest/data prerequisite. The blocked state
must never be described as SDK metrology completion.
