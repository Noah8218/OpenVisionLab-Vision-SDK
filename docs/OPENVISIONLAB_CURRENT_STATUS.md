# OpenVisionLab Vision SDK Current Status

Updated: 2026-09-14
Project work item: `PL-0009`
Overall state: `resolved`

## Authority

This file is the single current human-readable authority for product identity,
ordered engineering priorities, completion criteria, and verification boundaries.
[`docs/README.md`](README.md) is the navigation index. The machine-readable
`.proofline/issues/PL-0009.json` records the completed correctness-focused analyzer
follow-up. `.proofline/issues/PL-0008.json` preserves the completed CA1305 review,
and `.proofline/issues/PL-0007.json` preserves the preceding culture-conversion
closure, and `.proofline/issues/PL-0006.json` preserves the preceding diagnostics, lifetime,
API-contract, and boundary-test closure. `PL-0005`'s SIFT diagnostic criterion was
reopened after a missed success path was found, then corrected and revalidated by
PL-0006. The other prior verification remains historical evidence, while
`.proofline/issues/PL-0004.json` preserves the preceding closure and
`.proofline/issues/PL-0003.json` and `.proofline/issues/PL-0002.json` preserve the
preceding closures. No ledger is a second design or release authority.

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

`PL-0002`, `PL-0003`, and `PL-0004` are resolved for their approved functional,
package-source traceability, and exact third-party technical-provenance scopes.
`PL-0004` completion does not make a legal determination or authorize commercial
redistribution; that separate clearance remains blocked by the prerequisites below.
`PL-0009` reviewed the 14 remaining exception-parameter, Dispose/finalizer, and
intentional-construction diagnostics. The implementation reports the
public `options` argument while retaining the invalid 3D option-property name,
preserves Pipeline `parameters`, suppresses finalization after owned-resource
release, and makes constructor-rejection test intent explicit. Focused smoke and the
analyzer pass; local build/smoke/coverage/API/documentation verification also passes.
Exact-commit package consumption and remote CI also pass.
`PL-0008` reviewed all 31 CA1305 sites remaining after PL-0007. The current
implementation makes 3D numeric failure messages and generated test evidence
culture invariant, aligns legacy `CVMean` standard-deviation rounding with the
modern owner, and makes the numeric reflection provider explicit. Focused tests and
the analyzer pass; local integrated verification, commit-fixed package consumption,
and remote CI also pass.
`PL-0007` corrected culture-dependent coordinate persistence in both the modern
and 3.x compatibility converters, documented the invariant contract, and removed
the associated 52 analyzer diagnostics. The remaining analyzer debt and its
no-regression boundary are recorded below.
`PL-0006` has completed the missed SIFT success diagnostic, preprocessing Mat release,
consumer API contracts, and numeric/success-path verification. `PL-0005`'s earlier
F7 closure is corrected below; the other audited changes retain their prior evidence.

## PL-0009 work contract

Status: `Complete` at implementation commit
`3117548b6ebada2eb144a0cae9b375ddf48447b8` on `origin/main`.

Implement now: review all five CA2208, three CA1816, and six CA1806 diagnostics by
owner and observable contract; correct only the public error/lifetime clarity that
can be preserved without an API change; make intentional constructor rejection
explicit; lower the three measured analyzer ceilings to zero; then run focused,
integrated, package-consumer, and remote verification.

Review later: public naming compatibility remains a separately versioned migration;
allocation/static/dispatch suggestions require a measured bottleneck; the existing
readability diagnostics require a separate owner review. Sensor-backed accuracy and
redistribution clearance still require the external prerequisites listed below.

Out of scope: sealing or restructuring public result types, new abstractions,
changing public signatures or result shapes, bulk analyzer cleanup, package
publication, consumer-repository mutation, sensor/calibration qualification, UI, or
native binary changes.

The four 3D Tool owners retain `Execute -> validation -> controlled failure`; their
option objects and results hold no mutable shared state or disposable lifetime. The
invalid property remains in the diagnostic message, while `options` is the actual
public argument name. `VisionPipelineToolFactory.Create -> Get* -> InvalidParameter`
retains the observable `parameters` name. `VisionToolResult`,
`VisionPipelineContext`, and `VisionPipelineRunResult` remain the write and release
owners of their existing Mats/tool results; `Dispose` releases those objects before
calling `GC.SuppressFinalize(this)`. The two smoke suites remain the owners of the
six rejection assertions and now discard constructed values explicitly. No module,
dependency direction, public binding, or owner moved.

Shortest review order: the four CA2208 3D validation sites, the Pipeline factory
helper, the three Dispose bodies, the two rejection-test methods, their focused
smoke assertions, then `eng/analyzer-baseline.json` and this section.

Acceptance: the four new 3D option-message assertions fail before correction and
pass afterward; Pipeline parameter naming and existing resource ownership/rejection
tests pass; CA2208, CA1816, and CA1806 are zero with no other analyzer increase;
public API remains exactly 3,295 entries; all local/package/remote gates pass.
Evidence is retained under
`D:\OpenVisionLab-TestData\OpenVisionLab-Vision-SDK\PL-0009`.

### PL-0009 verification and closure

- Before the product change, all four updated 3D option-contract cases built and
  failed at the new public-parameter assertion.
- After the change, eight focused 2D/3D option, factory, rejection, and resource
  ownership cases pass. The analyzer reports **499 diagnostics in 12 emitted
  codes**, omits CA2208, CA1816, and CA1806, and leaves every other count unchanged.
- The Release solution build reports 0 warnings/errors. Direct and instrumented full
  smoke each pass **233/233** cases. Coverage is Core 37.35%, Inspection 69.92%,
  Vision2D 74.35%, Vision2D.Blob 69.17%, and Vision3D 90.97%; all floors pass. The
  public API matches all **3,295** entries and all 80 local Markdown links resolve.
- Five exact-commit packages at `3.0.1-dev.1789343484394` pass provenance,
  documentation and binary hashes, three fail-closed mutation probes, isolated
  `net8.0/win-x64` consumption, and the one-native-copy check. The packages were not
  published and no consumer repository was changed.
- GitHub Actions [Build run 34790849877](https://github.com/Noah8218/OpenVisionLab-Vision-SDK/actions/runs/34790849877)
  completed successfully for the exact implementation commit, including build,
  smoke/coverage, API, analyzer, package, provenance, consumer, and native gates.

Reusable exact-commit evidence is under
`D:\OpenVisionLab-TestData\OpenVisionLab-Vision-SDK\PL-0009\final-3117548`.
`integrated-verification-summary.json` has SHA-256
`1C84877F2B659D42656740575F8479FD1E2FFCD62DA4FB1377FEEFADB16C07B2`.
Only console DLLs were executed. This does not prove real-sensor accuracy,
calibration, false accept/reject rates, long-running production performance,
non-Windows-x64 runtimes, or commercial redistribution clearance.

## PL-0008 work contract

Status: `Complete` at implementation commit
`fac71d5e4949938ded375929cfd02aaea0865d93` on `origin/main`.

Implement now: review every remaining CA1305 site by owner and observable contract;
make machine-readable evidence and the affected 3D failure messages culture
invariant; remove the legacy numeric string round trip; preserve result shapes,
exception flow, public API, and analyzer counts outside CA1305; then run the local
and remote repository gates.

Review later: public field/name compatibility diagnostics and optimization
suggestions still require a separately approved compatibility migration or measured
hot-path evidence. Sensor-backed accuracy and redistribution clearance require the
external prerequisites below.

Out of scope: renaming 3.x public members, speculative performance rewrites, a new
localization framework, package publication, consumer-repository changes, sensor or
calibration qualification, and native binary replacement.

The current owners stay unchanged. `LineIntersectionTool`,
`FullXyzAffineSolveTool`, and `NominalActualMeshComparisonTool` own their diagnostic
messages. `CVMean` owns legacy standard-deviation output. `OpenCvAlgorithmBase`
owns result metric/overlay extraction, and `Vision2DSmokeSuite` owns its generated
evidence text. No mutable state, lifetime, dependency direction, or public contract
moved. The shortest review route is those five product files, the two culture/legacy
smoke cases, `eng/analyzer-baseline.json`, and this section.

Acceptance: the pre-fix 3D culture regression fails and the corrected version passes
for `en-US`, `de-DE`, `fr-FR`, and `ko-KR`; legacy and modern mean/metric/overlay
results agree under `de-DE`; CA1305 is zero with no other analyzer-count increase;
the exact public API and all local/package/remote gates pass. Evidence is retained
under `D:\OpenVisionLab-TestData\OpenVisionLab-Vision-SDK\PL-0008`.

### PL-0008 verification and closure

- The new 3D culture regression built with 0 warnings/errors and failed before the
  correction at `de-DE`; it passes after correction for all four declared cultures.
  The legacy/modern standard-deviation, metric, and overlay check passes under
  `de-DE` through both single- and multi-ROI routes.
- The Release solution build reports 0 warnings and 0 errors. Full smoke and the
  instrumented coverage run each pass **233/233** cases. Coverage is Core 37.35%,
  Inspection 69.92%, Vision2D 74.34%, Vision2D.Blob 69.17%, and Vision3D 90.70%;
  every configured floor passes.
- The public API matches all **3,295** baseline entries exactly. The analyzer gate
  passes at **513 diagnostics in 15 emitted codes**, CA1305 is absent, and every
  other code count is unchanged. The CA1305 ceiling is fixed at zero. All 80 local
  Markdown links resolve.
- Five exact-commit packages at `3.0.1-dev.1789338223890` pass package provenance,
  documentation and binary hashes, three fail-closed mutation probes, isolated
  `net8.0/win-x64` consumption, and the one-native-copy check. The packages were
  not published and no consumer repository was changed.
- GitHub Actions [Build run 34786623362](https://github.com/Noah8218/OpenVisionLab-Vision-SDK/actions/runs/34786623362)
  completed successfully for the exact implementation commit, including the same
  build, smoke/coverage, API, analyzer, package, provenance, consumer, and native
  runtime gates.

Reusable exact-commit evidence is under
`D:\OpenVisionLab-TestData\OpenVisionLab-Vision-SDK\PL-0008\final-fac71d5`.
`integrated-verification-summary.json` has SHA-256
`2B9849F1ECAC6CC9CC036FCBBDBC52869D9E186D160EBA263130A0B12F971845`;
the package hashes are in `package-provenance.json`. Only console DLLs were
executed. This does not prove real-sensor accuracy, calibration, false accept/reject
rates, long-running production performance, non-Windows-x64 runtimes, or commercial
redistribution clearance.

## PL-0007 work contract

Status: `Complete` at implementation commit
`00a3fb2ede8a85708ae00d3fd23be14482aa1c66` on `origin/main`.

Completed scope: make `CommonConverter` and 3.x compatibility `CConverter`
coordinate/ROI serialization and parsing independent of `CurrentCulture`; preserve
their public signatures, token-count fallback, and numeric exception behavior; add
an `en-US`, `de-DE`, `fr-FR`, and `ko-KR` regression; document the persistence/UI
formatting boundary; and lower the measured CA1305 ceiling from 83 to 31.

Review later: the remaining 544 analyzer diagnostics require owner-specific
compatibility, correctness, or measured-performance evidence. Sensor-backed
accuracy and redistribution clearance still require the external prerequisites
listed below.

Out of scope: a new serializer or public API, automatic recovery of already
ambiguous decimal-comma strings, unrelated analyzer rewrites, package publication,
sensor/calibration qualification, UI localization, or native binary changes.

The existing Core converters remain the owners. The call path is consumer numeric
state -> `PointFToString`/other `*ToString` -> persisted invariant text -> matching
`StringTo*` method. The converters hold no mutable state or disposable lifetime.
Localized display text remains a host concern. No module, dependency direction, or
public binding moved; the shortest review route is the two Converter files, the
`Core coordinate strings remain culture invariant` smoke case, then the Core README.

### PL-0007 verification and closure

- The new culture smoke failed before correction at the `de-DE` floating-point
  serialization assertion and passed afterward for all four cultures. Malformed
  token-count fallback, `FormatException`, `OverflowException`, modern/legacy
  parity, and byte conversion behavior are covered.
- .NET SDK 8.0.423 Release build reported 0 warnings and 0 errors. Full smoke and
  the instrumented coverage run each passed **232/232**; coverage was Core 37.35%,
  Inspection 69.92%, Vision2D 74.10%, Vision2D.Blob 69.17%, and Vision3D 90.67%.
  The public API matched all **3,295** baseline entries exactly.
- The fixed analyzer run reports **544 diagnostics in 16 codes**. CA1305 fell from
  83 to 31, all other code counts stayed unchanged, and both Converter files now
  have zero diagnostics. The baseline was tightened to the observed counts.
- The Core README example built with 0 warnings/errors and produced
  `1.5,-2.25`. Five commit-fixed packages at
  `3.0.1-dev.1789315533708` passed provenance, three fail-closed mutation probes,
  isolated `net8.0/win-x64` consumption, and the one-native-copy check. They were
  not published.
- GitHub Actions [Build run 34767469759](https://github.com/Noah8218/OpenVisionLab-Vision-SDK/actions/runs/34767469759)
  passed all 15 verification steps for the exact implementation commit.

Reusable evidence is under
`D:\OpenVisionLab-TestData\OpenVisionLab-Vision-SDK\PL-0007`; the integrated
summary is `final-00a3fb2/integrated-verification-summary.json` with SHA-256
`7FD1169A8534E5151AAF220E9F0B213B0D2241A72A9FD6BB8A5CBE6884A86704`.
This proves the declared Windows x64 synthetic/package contract. It does not prove
real-sensor accuracy, calibration, Gauge R&R, long-running workloads, another RID,
.NET Framework runtime behavior, or commercial redistribution clearance.

## PL-0006 work contract

Status: `Complete` for the four approved scopes below, verified at implementation
commit `34d99f8909c04dc28cd90e3d085c5d655a787aa2` on `origin/main`.

Completed scope: preserve SIFT/ORB diagnostics through success, failure, and repeated
single/multi-ROI execution; release cloned preprocessing images on exceptions in
the common helper and both template paths; document per-tool input/options/ROI and
failure/recovery contracts plus concurrency, cancellation, timing, and lifetime;
strengthen numeric assertions and Core/3D/acceptance boundary tests; review the
existing analyzer diagnostics by their actual cause.

Review later: sensor-backed accuracy, false accept/reject, throughput, and long-run
production workloads require the dataset, calibration, tolerances, and operational
targets. Redistribution clearance still requires the written evidence below.

Out of scope: new UI/acquisition/PLC services, native binary replacement, new public
interfaces, legacy behavior changes, package publication, or a claim that every
algorithm/parameter combination has been certified.

Owners remain `SiftTool` (detector state), `OpenCvAlgorithmBase` (source clone and
preprocessing), and the existing SIFT/Edge template preparation methods (template
clones). `Execute -> Run -> preprocessing -> CollectMetrics` is the normal call
path. The callee releases a clone if preprocessing throws; the caller releases a
successfully returned clone. `SmokeAssert` owns numerical test comparisons. No
module, public binding, or dependency boundary is moved.

Acceptance: each of the four scopes above has focused executable evidence; the
shared numeric assertion rejects non-finite inputs; Release, full smoke/coverage,
exact public API, analyzer, clean package/consumer, and remote main gates pass.
Evidence is retained under
`D:\OpenVisionLab-TestData\OpenVisionLab-Vision-SDK\PL-0006` and in the ledger.

### PL-0006 verification and closure

- **C1 — Pass:** the new SIFT success diagnostic test failed before correction and
  passed afterward. Single ROI success/failure/recovery, multiple ROI success,
  source-coordinate center, and missing-template evidence are covered. The bundled
  runtime executed ORB fallback; native SIFT execution remains unverified.
- **C2 — Pass:** common full-image/ROI preprocessing and both template preparation
  paths preserve input ownership and recover after an invalid native threshold
  option. Coverage records execution of all three exception `Dispose`/rethrow paths
  and the ROI view's `using` scope. This proves those cleanup paths were exercised;
  it is not a long-running native memory-leak qualification.
- **C3 — Pass:** all 14 modern 2D Tools plus the separate Blob Tool have an input,
  ROI, option/default reference and failure/recovery route. Existing Core, 3D and
  Inspection guides explain result/exception/tolerance, precision and lifetime.
  Five package README examples were extracted unchanged, built and executed;
  72 local documentation link targets passed. `docs/README.md#start-here` is the
  single solution/project-reference and developer reading route.
- **C4 — Pass:** the former numerical assertion accepted NaN; its new regression
  failed before the fix. Finite/inclusive acceptance, translated/noisy/degenerate
  line and plane fits, coordinate overflow, independent raw-height fit, and volume
  sample/accumulation/acceptance-subtraction boundaries pass. Analyzer triage is
  recorded below with explicit remaining debt.
- **C5 — Pass:** .NET SDK 8.0.423 Release build and isolated consumer build each
  reported 0 warnings/0 errors. Full smoke and the instrumented coverage run each
  passed **231/231**. Public API matched **3,295** entries exactly. The fixed-commit
  analyzer run retained **596** diagnostics without changing the baseline.

| Assembly | Observed line coverage | Required minimum |
| --- | ---: | ---: |
| Core | 27.87% | 20.00% |
| Inspection | 69.92% | 68.00% |
| Vision2D | 74.10% | 68.00% |
| Vision2D.Blob | 69.17% | 68.00% |
| Vision3D | 90.67% | 89.00% |

Local immutable verification packages use `3.0.1-dev.1789310781178` and embed
`34d99f8909c04dc28cd90e3d085c5d655a787aa2`. Five-package provenance, three negative
provenance probes, the isolated `net8.0/win-x64` package consumer and exactly one
native DLL at the consumer output root passed. Package hashes are recorded in
`final-34d99f8/package-provenance.json`. The same source passed all 18 remote Build
steps in [run 34763703160](https://github.com/Noah8218/OpenVisionLab-Vision-SDK/actions/runs/34763703160)
(1m59s); the actions Node.js 20 deprecation annotation was non-blocking.

Reusable evidence: `PL-0006/final-34d99f8/verification-summary.json`, build/smoke/API
and coverage logs, `preprocessing-lifetime-coverage.csv`, package manifest, negative
probe summary, consumer logs/native hash and `remote-ci.json`. The preceding
`red`, `green`, `boundaries`, `readme-examples`, `precommit` and
`analyzer-34d99f8` directories retain focused proof. CLI verification listed 231
cases; an unmatched filter exits 1 and invalid arguments exit 2. Test artifacts,
TEMP and TMP remained on D:. Only console DLLs were executed; no UI was reviewed.

The final closure commit changes documentation/ledger evidence only. These packages
remain tied to the implementation commit above. No package was published and no
consumer repository was changed. Real sensor accuracy, calibration, false accept/
reject rates, representative workloads, long-running memory/latency, other native
runtimes and redistribution clearance remain outside this completed scope.

### Current analyzer triage boundary

The PL-0006 analyzer run historically retained **596 diagnostics in 16 codes**.
PL-0007 and PL-0008 removed all 83 CA1305 diagnostics. PL-0009 removed the 14
CA1806, CA1816, and CA2208 diagnostics. The exact implementation commit reports
**499 diagnostics in 12 emitted codes**, with every other code count unchanged.
Current counts are recorded in
`PL-0009/final-3117548/integrated-verification-summary.json` under the D-drive
evidence root and in remote Build run 34790849877. This remains a no-regression gate
and categorized debt review, not a zero-warning claim.

| Category | Codes / count | Review decision |
| --- | --- | --- |
| Public field and naming compatibility | CA1051, CA1707, CA1716 / 186 | Preserve 3.x public names and fields; broad renaming is a separate compatibility migration. |
| Culture-sensitive formatting | CA1305 / 0 | All 31 remaining sites were reviewed by owner. Numeric diagnostics, legacy rounding, reflection conversion, and generated evidence now use explicit culture-independent behavior; the baseline ceiling is zero. |
| Allocation, static and dispatch suggestions | CA1805, CA1822, CA1825, CA1843, CA1859, CA1861, CA1869 / 272 | No measured bottleneck justifies a bulk rewrite. Benchmark the affected call path before promoting performance suggestions. |
| Readability | CA1507, CA2249 / 41 | Defer unrelated nameof/Contains rewrites. |
| Ignored constructed result | CA1806 / 0 | Five TriangleMeshDistance and one Pipeline rejection assertion explicitly discard the constructed value without changing the expected exception. The ceiling is zero. |
| Dispose/finalizer extensibility | CA1816 / 0 | VisionToolResult, VisionPipelineContext and VisionPipelineRunResult release their existing owned Mats/results, then suppress finalization for derived instances. The ceiling is zero. |
| Exception parameter naming | CA2208 / 0 | Four 3D messages retain the invalid option-property name while reporting the actual public `options` argument; the Pipeline helper still reports `parameters`. The ceiling is zero. |

The CA1305 review covered every prior location:

| Owner | Prior count | Decision |
| --- | ---: | --- |
| `LineIntersectionTool` | 5 | Keep the existing controlled-result messages and format measured/taught decimal values with `InvariantCulture`. |
| `FullXyzAffineSolveTool` | 2 | Keep the existing rejected-condition result and make actual/maximum condition text deterministic. |
| `NominalActualMeshComparisonTool` | 2 | Preserve the defensive unresolved-sign failure shape and make grouped point counts invariant. |
| Legacy `CVMean` | 4 | Replace two format/parse pairs with the modern owner's direct `Math.Round(value, 1)` behavior. |
| `OpenCvAlgorithmBase` | 3 | Keep reflection over the existing strongly typed numeric result members and pass `InvariantCulture` to `Convert`. |
| `Vision2DSmokeSuite` evidence writers | 15 | Use invariant decimal text for Auto MPoint and unique-match reproducibility files. |

PL-0009 re-reviewed and corrected the six CA1806 sites, all three Dispose bodies,
and all five CA2208 call sites after the user requested the next analyzer priority.
The other remaining groups are still classified by diagnostics; this is not an
individual correctness certification of all 499 current locations. The CA1305,
CA1806, CA1816, and CA2208 ceilings are now zero; no other ceiling or coverage
minimum was relaxed.

### Historical PL-0002 milestone snapshot

| Priority / milestone | State on 2026-08-31 | Immediate outcome |
| --- | --- | --- |
| 1 / M1 | complete; pushed | Contour mask evidence and Pipeline output routing are fixed and covered by direct regressions. |
| 2 / M2 | complete; pushed | Large-coordinate, finite-statistics, region-bound, and LineGauge depth boundaries are fail-closed and covered. |
| 3 / M3 | complete; pushed | Current authority/index, catalogs, versions, examples, provenance, and historical labels match the checked source tree. |
| 4 / M4 | complete; pushed | Direct 2D execution plus coverage, exact public-API, and analyzer no-regression gates pass locally and are wired into CI. |
| 5 / M5 | complete; pushed | Five packages and the isolated Windows x64 consumer pass with one native DLL at the consumer output root. |

## Prior PL-0002 integrated evidence

The final candidate evidence is under
`D:\OpenVisionLab-TestData\OpenVisionLab-Vision-SDK\PL-0002\final-candidate-20260831-a1`.
The checked working tree was committed as
`c066f16e9a6f38863b71e935d483483dc06618c6` and pushed to `origin/main`.
The packages were created before that commit and therefore record their build-time
HEAD `6da3bcf521efb88681a17e4a7b23a091e7fcbacf`; their hashes below identify
functional verification artifacts, not source-commit-fixed candidates.

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

## PL-0003 completion record

Status: `Complete`

Scope: added `eng/Verify-PackageProvenance.ps1` as the single local/CI gate for a
clean exact HEAD, five expected package IDs and filenames, package version,
repository type/URL/full commit, required package entries, assembly product-version
commit, reviewed internal dependency declarations, SHA-256 output, and an optional
JSON manifest. The CI workflow now invokes this gate before its isolated consumer.

Acceptance criteria:

- C1 — Pass. The shared verifier passed locally and as GitHub Actions step
  `Verify package provenance and documentation` for the exact five-package graph.
- C2 — Pass. Untracked dirty worktree, expected HEAD, `.nuspec` commit, `.nuspec`
  version, stale `pack --no-build` assembly, internal dependency version, and
  duplicate required-entry probes all failed for their intended reasons.
- C3 — Pass. A fresh D-drive build and integrated pack from clean commit
  `89a6421cf54e24478afc32fcd2a539d6824bf519` produced version
  `3.0.1-provenance.20260831.1`; all five repository commits and assembly product
  versions equal that full commit.
- C4 — Pass. Release build, `220/220` smoke and all five coverage floors, exact
  `3295/3295` public API, 596-diagnostic analyzer baseline, isolated
  `net8.0`/`win-x64` package consumer, one native runtime DLL, and remote `main` CI
  passed.
- C5 — Pass. This authority document, the documentation index, consumer README,
  workflow, JSON manifest, verification summary, and `PL-0003` ledger record the
  exact provenance and exclusions.

Verification:

- .NET SDK `8.0.423`; fresh Release build: `0` warnings, `0` errors; smoke:
  `220/220`.
- Coverage: Core `21.68% >= 20.00%`; Inspection `69.92% >= 68.00%`; Vision2D
  `72.32% >= 68.00%`; Vision2D.Blob `69.17% >= 68.00%`; Vision3D
  `90.68% >= 89.00%`.
- Public API: exact `3295/3295`; analyzer: 596 diagnostics at or below baseline.
- Isolated consumer: 13 top-level files, `55,121,246` bytes, exactly one
  `OpenCvSharpExtern.dll` at the output root.
- GitHub Actions: run
  [`33344346670`](https://github.com/Noah8218/OpenVisionLab-Vision-SDK/actions/runs/33344346670)
  passed in `2m2s` for implementation commit `89a6421cf54e24478afc32fcd2a539d6824bf519`.

| Package | SHA-256 |
| --- | --- |
| `OpenVisionLab.Core` | `A9DC520ED2A9DB10470872D24A8D91039B8B8D8BB66B0577348630430C22A45D` |
| `OpenVisionLab.Inspection` | `F7907CF088BCC73B751D10BD13BABE8C3DB243DF228A9DA4E1BAC2ED55C35645` |
| `OpenVisionLab.Vision2D` | `EE9FCF85B98A858FB24042C106E4053C0706DFA3312C8BB045074EDF2B6E559A` |
| `OpenVisionLab.Vision2D.Blob` | `EB06B7A0D01B1872A72F3E75A409B75CFE60198009DD08602F8CB46797628056` |
| `OpenVisionLab.Vision3D` | `3D1CB5A262D92916AEA2405936880B5A1C3599B395B63DA6C98E3DD5970AF294` |

Evidence:

- Implementation commit: `89a6421cf54e24478afc32fcd2a539d6824bf519`.
- Reusable local root:
  `D:\OpenVisionLab-TestData\OpenVisionLab-Vision-SDK\PL-0003\final-candidate-20260831-a1`.
- Package manifest: `package-provenance.json`; combined local/CI record:
  `verification-summary.json`; complete command output: `logs\` under that root.
- Fail-closed probe results are retained in sibling `negative-*` directories under
  `D:\OpenVisionLab-TestData\OpenVisionLab-Vision-SDK\PL-0003`.

Boundary / next dependency: these are commit-fixed development packages, not
published or legally cleared release artifacts. No package publication, version
release, consumer-repository change, tag, release, deployment, other RID, .NET
Framework runtime, real sensor, calibration, or legal-clearance check was performed.
The internal dependency strings prove consistent minimum floors, not exact NuGet
resolution pins. Remote CI also emitted a non-failing annotation that v4 checkout and
setup-dotnet actions still declare Node.js 20 and were forced onto Node.js 24.

## PL-0004 completion record

Status: `Complete`

Scope: established exact official technical provenance for the three existing
vendored OpenCvSharp/OpenCV DLLs; packaged a machine-readable lock, provisional
notice, and preserved license/conflict evidence; and added shared positive and
fail-closed local/CI verification. No binary was replaced or upgraded, and no legal
clearance was claimed.

Exact provenance:

- `OpenCvSharp.dll` (`862,208` bytes,
  `A5C477750EB4321B608F4B9183949915D4A42FE0B5D80CFB8376F5A326FA5F24`) and
  `OpenCvSharp.Blob.dll` (`40,960` bytes,
  `E03FE75D2C9D88886384EDBC445C63DA051EE3450286C8D0982FCD9F4BC24D54`) are
  byte-identical to the official repository-signed NuGet package
  `OpenCvSharp4 4.4.0.20200915` `netstandard2.0` entries.
- `OpenCvSharpExtern.dll` (`53,231,104` bytes,
  `C9E02A255DD83C9B06CA56EC6F435F15B53A863435238FCC5D8B9082B035F249`) is
  byte-identical to the official GitHub release `4.3.0.20200708`
  `NativeLib/win/x64/OpenCvSharpExtern.dll` entry.
- The reviewed set is therefore deliberately recorded as mixed `4.4` managed / `4.3`
  native, not as one inferred version.

Acceptance criteria:

- C1/C2 — Pass. `src/OpenVisionLab.Core/ThirdParty/provenance.json` fixes each local
  byte, Git blob, managed/native identity, official container/entry, and exact-match
  evidence; unknown license terms remain explicitly unknown.
- C3 — Pass. Root/Core notices and consumer documentation distinguish the project MIT
  scope, third-party evidence, mixed upstream identity, and non-legal-advice boundary.
  Core packages the manifest, notice, three official BSD texts, and Blob conflict
  ReadMe.
- C4 — Pass. Shared verifiers lock the canonical manifest, source binaries, Core
  package paths and bytes, non-Core absence, evidence files, duplicate/unsafe ZIP
  paths, and renamed exact binary bytes. The CI regression harness rejects all three
  representative mutations with their exact diagnostics.
- C5 — Pass. Fresh D-drive Release, smoke/coverage, public API, analyzer, five-package
  provenance, isolated `net8.0`/`win-x64` consumer, native-copy, negative probes, and
  remote `main` CI passed.
- C6 — Pass. This authority, navigation documents, notices, final validation summary,
  and `.proofline/issues/PL-0004.json` record the exact evidence and exclusions.

Verification:

- Verified implementation commit: `c5d46d846de9fde574d6ff0caf1d6eb1bcd386b1`, pushed to
  `origin/main`; worktree clean.
- .NET SDK `8.0.423`; Release build `0` warnings/`0` errors; smoke `220/220`; all
  five coverage floors passed; public API exactly `3,295`; analyzer `596` diagnostics
  at or below baseline.
- Local package version `3.0.1-provenance.c5d46d84.1` passed exact clean-commit
  provenance. Package SHA-256 values:

  | Package | SHA-256 |
  | --- | --- |
  | `OpenVisionLab.Core` | `0BBF72D23410B226E47D93172E59F65A2E069285578F99E2DF72B34C16316948` |
  | `OpenVisionLab.Inspection` | `E9BF5B22EB33ABB3D4E8E43AB3CAB9AA4DF4C3A6635E46942BE8203ACF64354D` |
  | `OpenVisionLab.Vision2D` | `0BD5C531E7D3877089ED90BB6828A48722F66037E8D16CABA2D3266578053F13` |
  | `OpenVisionLab.Vision2D.Blob` | `21A397910A7B10DDD7B611B85FA6BE39A7306C43BE26CC92A8D73D1760054584` |
  | `OpenVisionLab.Vision3D` | `5BCCE35611B90D865AD1F69A2C525E64F9A62E6883A7606B8463E8CECF00460D` |

- The isolated package-only consumer passed and contained exactly one native DLL at
  its output root with the reviewed native SHA-256. Representative fail-closed
  regression was `3/3`, and the harness returned process exit `0`.
- GitHub Actions
  [`33348179667`](https://github.com/Noah8218/OpenVisionLab-Vision-SDK/actions/runs/33348179667)
  passed all 18 steps for the final commit. Initial run `33347973409` exposed a
  harness exit-code propagation defect after all probes passed; final commit
  `c5d46d8` fixed the success exit and the full rerun passed. The only non-blocking
  annotation was the v4 actions Node.js 20 declaration being forced onto Node.js 24.

Evidence:

- Reusable local root:
  `D:\OpenVisionLab-TestData\OpenVisionLab-Vision-SDK\PL-0004\final-c5d46d84-20260831-a1`.
- `validation-summary.json` SHA-256:
  `3DE1897AEBF6180D1652648E8EB77EA844FA5CB6A0262F175360C255C10B4D91`;
  package manifest, remote-CI JSON, logs, negative-probe results, coverage, packages,
  and isolated consumer output are under the same root.

Boundary / next dependency: no package publication, stable version, tag, release,
deployment, consumer-repository change, other RID, .NET Framework runtime, real
sensor, calibration, or production-metrology qualification was performed.
Redistribution clearance remains blocked by the Blob BSD/LGPL conflict and missing
LGPL version/full text, authoritative redistribution terms for the exact IPPICV 2020
archive, the ittnotify license selection, and final project distribution/legal-owner
approval.

## PL-0005 — audit finding remediation

Status: `Complete` after PL-0006 corrected and revalidated F7/C4. The original
success branch reset `lastFeatureDetector` to `Unknown`; the blank-image test had
missed that branch. PL-0006's failing-before/passing-after success regression and
full verification above replace the earlier F7 completion inference. Original implementation commit
`a872ed11b88b0371a3a39b3a9a7a9a46db4d1f5b` is on `origin/main`; the package
provenance and consumer evidence below is fixed to that same commit.

Scope: fix the reproduced acceptance, numerical, filter, threshold, and error-code
failures; document the actual SIFT/ORB runtime choice; and make the source-reference
quick start include the managed and native OpenCvSharp assets required by a direct
checkout consumer. The public API signatures, package version defaults, native DLL
bytes, legacy compatibility types, host-owned calibration/recipe/UI boundaries, and
redistribution-clearance decision remain unchanged.

Implementation decisions:

- Acceptance metrics, active metric limits, and elapsed limits are finite-only and
  fail closed. A configured metric limit also requires a metric name.
- Modern Core line fitting uses centered double-precision sums and rejects fewer
  than two points, non-finite coordinates, or zero X variance with an explicit
  `ArgumentException`. Legacy `C*` fitting remains a compatibility path.
- Modern 3D height-field plane distances and projections use double precision. The
  change deliberately removes the prior translation-dependent float rounding from
  the modern result contract; legacy 3.x type names and signatures are unchanged.
- Volume and plane-distance accumulation throw `InvalidOperationException` when a
  finite input would produce a non-finite intermediate or output, keeping numerical
  calculation failure separate from an ordinary `Passed == false` tolerance result.
- `SiftTool` still attempts SIFT, falls back to ORB when the bundled native entry
  point is unavailable, and reports the selected detector through result metrics.
  Replacing the bundled binary or claiming SIFT-equivalent behavior remains out of
  scope.

Completion criteria:

- F1-F6 and F9 have direct smoke regressions for the reproduced failure and its
  controlled result or exception contract.
- F2's large-origin flatness case and F3's translated line/angle cases preserve the
  expected numeric result; the full smoke runner passes.
- Root and package documentation describe the SIFT fallback, finite acceptance
  contract, volume overflow behavior, and working direct-source reference route.
- Release build, coverage, exact public API, analyzer no-regression, package
  provenance, and isolated package-only consumer checks pass for one clean commit.

Verification and evidence:

- .NET SDK `8.0.423`; clean-commit Release build: `0` warnings and `0` errors;
  full synthetic smoke: `224/224` passed. Evidence:
  `D:\OpenVisionLab-TestData\OpenVisionLab-Vision-SDK\PL-0005\postcommit-a872ed1\build.log`,
  `smoke.log`.
- Coverage gate passed: Core `24.36% >= 20.00%`, Inspection
  `69.92% >= 68.00%`, Vision2D `72.81% >= 68.00%`, Vision2D.Blob
  `69.17% >= 68.00%`, Vision3D `90.58% >= 89.00%`.
- Exact public API gate passed `3,295/3,295`; analyzer no-regression gate passed
  `596` diagnostics at or below the existing per-code baseline.
- Five local packages were created as `3.0.1-audit.20260913.1`. The provenance
  verifier passed with a clean worktree and exact commit
  `a872ed11b88b0371a3a39b3a9a7a9a46db4d1f5b`. Manifest and logs:
  `D:\OpenVisionLab-TestData\OpenVisionLab-Vision-SDK\PL-0005\package-20260913-a872ed1\package-provenance.json`.

  | Package | SHA-256 |
  | --- | --- |
  | `OpenVisionLab.Core` | `1A63E33B945F82620920C9FB5F6E424BA7BCE818ECD6A274E726637F6B09BDA5` |
  | `OpenVisionLab.Inspection` | `0B4E518B79555F29EE41583B1F5B4A42295945547793E4CEF6D370B2FBF57C54` |
  | `OpenVisionLab.Vision2D` | `0D26CA93DE1CDD7FCAE0090409A542076497B4E31713225D823D9F4F65737D42` |
  | `OpenVisionLab.Vision2D.Blob` | `E0D7C2F5CBFD8166B1F82AF075F70DA1EF6A3DD53E5A09BAE704741D8EEB8A0A` |
  | `OpenVisionLab.Vision3D` | `B2FBD34F5345E70FC44324112FDF3B34652E36025CBF0159D2E6F358B4280A14` |

- Isolated `net8.0/win-x64` package consumer restore/build/run passed. Its output
  contains `13` top-level files and exactly one `OpenCvSharpExtern.dll` at the
  output root. Evidence:
  `D:\OpenVisionLab-TestData\OpenVisionLab-Vision-SDK\PL-0005\package-20260913-a872ed1\consumer`.
- `git push origin main` delivered the implementation commit. GitHub Actions Build
  run [`34747318509`](https://github.com/Noah8218/OpenVisionLab-Vision-SDK/actions/runs/34747318509)
  passed in `1m55s`, including the repository's package provenance guards and
  package-only consumer. The runner emitted only the existing Node.js 20
  deprecation annotation for actions forced to Node.js 24.

Boundary: this closes the reproduced F1-F9 findings and the documented first-use
source-reference gap. It does not replace the bundled native bytes, provide a
native SIFT entry point, publish a package, create a release/tag, deploy, validate
another RID or .NET Framework runtime, run real sensors/calibration/Gauge R&R, or
establish commercial redistribution clearance. The latter remains blocked by the
prerequisites recorded above.

## Next priority — external redistribution-clearance evidence

Prerequisite: obtain the four written items named above from authoritative rights
holders and the project's distribution/legal owner. Until those materials exist,
another implementation or model run cannot establish redistribution clearance, so
no model-token recommendation is made.

## Historical PL-0002 priority 1 — 2D result-contract correctness

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

## Historical PL-0002 priority 2 — numerical and input-boundary reliability

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

## Historical PL-0002 priority 3 — current documentation authority and API discovery

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

## Historical PL-0002 priority 4 — reproducible quality gates

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

## Historical PL-0002 priority 5 — native/RID package closure

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
- Vendored OpenCvSharp/OpenCV binary bytes now have exact official artifact
  provenance and provisional notice evidence. Commercial redistribution is still
  blocked by the unresolved Blob BSD/LGPL conflict, unspecified LGPL version,
  incomplete exact IPPICV redistribution terms, unconfirmed ittnotify license
  selection, and the absence of project distribution/legal-owner approval for the
  final notice bundle. This repository audit did not make a legal determination.

## PL-0002 completion record

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
