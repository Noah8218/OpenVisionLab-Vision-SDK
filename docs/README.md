# OpenVisionLab Vision SDK Documentation Index

Start with the [current status and work contract](OPENVISIONLAB_CURRENT_STATUS.md).
It is the current human-readable authority for product identity, priority order,
completion criteria, and verification boundaries. The repository
[`README.md`](../README.md) is the consumer overview and quick start.

## Current contracts and guides

| Document | Role |
| --- | --- |
| [Current status and work contract](OPENVISIONLAB_CURRENT_STATUS.md) | PL-0010 readability analyzer contract review, prior correctness/culture/API evidence, and blocked redistribution-clearance prerequisite |
| [Core third-party provenance and notice status](../src/OpenVisionLab.Core/ThirdParty/NOTICE.md) | Exact mixed OpenCvSharp/OpenCV binary origin, preserved license evidence, and unresolved redistribution blockers |
| [3D inspection contract](three-d-inspection.md) | Complete public 3D Tool catalog, input layers, units, frames, missing samples, outcomes, and verification limits |
| [2.9.1 to OpenVisionLab 3.0 migration](MIGRATING_LIB_2_9_1_TO_OPENVISIONLAB_3_0.md) | Package/namespace migration; `3.0.0` is the API migration baseline, not a current package-install promise |
| [Affine Transform 2D](AFFINE_TRANSFORM_2D.md) | Current 2D affine Tool and Pipeline contract |
| [Object candidate contract](OBJECT_CANDIDATE_CONTRACT.md) | Additive Blob/Contour candidate evidence contract |
| [Edge-based unique match](EDGE_BASED_UNIQUE_MATCH_V1.md) | Current fail-closed unique-result contract |
| [Auto MPoint V1](AUTO_MPOINT_V1.md) | Current teaching-time contract with a historical completion-evidence section |
| [Edge-based global polarity V1](EDGE_BASED_GLOBAL_POLARITY_V1.md) | Current opt-in polarity contract with a historical verification count |
| [Matching responsibility and production baseline](MATCHING_RESPONSIBILITY_AND_PRODUCTION_BASELINE_PLAN_20260821.md) | Active SDK/host responsibility boundary and missing sensor-backed production prerequisites |
| [Legacy C/CV/LineGuage 4.0 removal plan](LEGACY_C_CV_LINEGUAGE_V4_REMOVAL_PLAN_20260805.md) | Active 3.x compatibility policy and separately gated 4.0 removal criteria |

## Historical records

These files preserve dated decisions and evidence. Their versions, commands, test
counts, paths, percentages, and “next” statements are not current status.

| Document | Historical scope |
| --- | --- |
| [Vision SDK identity and v3 migration ledger](OPENVISIONLAB_VISION_SDK_IDENTITY_AND_V3_MIGRATION_PLAN_20260805.md) | 2026-08 migration chronology, completion records, and benchmark attempts |
| [3D consumer API usability and release plan](THREED_CONSUMER_API_USABILITY_AND_RELEASE_PLAN_20260804.md) | 2026-08-04 Library-Noah/2.9 package-integration snapshot |

## Package-specific quick starts

- [`OpenVisionLab.Core`](../src/OpenVisionLab.Core/README.md)
- [`OpenVisionLab.Vision2D`](../src/OpenVisionLab.Vision2D/README.md)
- [`OpenVisionLab.Vision2D.Blob`](../src/OpenVisionLab.Vision2D.Blob/README.md)
- [`OpenVisionLab.Vision3D`](../src/OpenVisionLab.Vision3D/README.md)
- [`OpenVisionLab.Inspection`](../src/OpenVisionLab.Inspection/README.md)

Package quick starts name `3.0.1-dev.1` only as the repository-local default and
tell the consumer to replace it with the exact version from the selected source. The
API/assembly baseline is `3.0.0`; shared, consumed, or published bytes require a
separately recorded immutable version.

## Images and provenance

- `samples/vision_sample.png` is a legacy-branded demonstration input retained for
  source examples. It is not a calibration or production artifact.
- `images/*.png` contains synthetic visual reference captures for the named tools.
  They were not generated from `vision_sample.png`.
- The repository currently has no tracked generator, exact source image, parameter
  manifest, commit, or checksum record for those six captures. Treat them as
  illustrations, not reproducible test or release evidence.

## Start Here

Open `OpenVisionLab.VisionSdk.sln`. This is a library solution; no application host
or repository-defined startup/launch profile exists. Choose
`OpenVisionLab.Inspection.Smoke` as the console startup project for contract checks.
`OpenVisionLab.Vision3D.Benchmark` is a separate console benchmark, not the SDK entry
point. The package-only consumer is outside the solution.

Read the root consumer quick start, the selected package README's input/result
contract, and then the matching Tool's `Execute`/`Run` plus its smoke case. Search
for the Tool name in `tests/OpenVisionLab.Inspection.Smoke/Suites` to reach both
normal and failure examples. This is the single developer onboarding route.

Project references (arrow means depends on): `Vision2D -> Core`;
`Vision2D.Blob -> Vision2D + Core`; `Inspection -> Vision2D + Vision3D`;
`Inspection.Smoke -> Inspection + Vision2D.Blob`; `Vision3D.Benchmark -> Vision3D`.
Core and Vision3D have no project references; Core supplies the native OpenCV
dependency. Library projects target `netstandard2.0`; console checks target `net8.0`.

## Current verification entry points

Run from the repository root. Do not infer success from a historical case count.

```powershell
dotnet tool restore
$testRoot = "D:\OpenVisionLab-TestData\OpenVisionLab-Vision-SDK\local-check"
New-Item -ItemType Directory -Force -Path $testRoot | Out-Null
$env:TEMP = $testRoot
$env:TMP = $testRoot
dotnet build OpenVisionLab.VisionSdk.sln -c Release --artifacts-path "$testRoot\build"
$assemblyDirectory = "$testRoot\build\bin\OpenVisionLab.Inspection.Smoke\release"
$smokeAssembly = "$assemblyDirectory\OpenVisionLab.Inspection.Smoke.dll"
dotnet $smokeAssembly --list
dotnet $smokeAssembly --filter "SIFT" # Case-insensitive name substring; no match fails.
# For a full regression run: dotnet $smokeAssembly
./eng/Verify-Coverage.ps1 `
  -SmokeAssembly $smokeAssembly `
  -OutputPath "$testRoot\coverage.cobertura.xml"
./eng/Verify-PublicApi.ps1 `
  -AssemblyDirectory $assemblyDirectory
./eng/Verify-AnalyzerBaseline.ps1 `
  -SolutionPath OpenVisionLab.VisionSdk.sln `
  -ArtifactsPath "$testRoot\analyzer"
```

`Verify-Coverage.ps1` executes the full smoke assembly while collecting coverage.
The API baseline is an exact reviewed set: an addition, removal, signature change,
or recorded parameter-name change requires an explicit compatibility decision.
Analyzer results are compared by diagnostic code so existing debt cannot grow
silently.

The package-provenance entry point is `eng/Verify-PackageProvenance.ps1`; its
minimal fail-closed regression check is
`eng/Test-PackageProvenanceNegative.ps1`. See the
repository [`README.md`](../README.md#commit-fixed-package-and-isolated-consumer-verification)
for its runnable fresh D-drive pack, manifest, and isolated-consumer sequence. The
gate requires a clean committed worktree and proves package metadata, required
contents, assembly commit, and internal dependency declaration consistency; it does
not publish packages or make the dependency declarations exact pins.

The solution also contains
`tests/OpenVisionLab.Vision3D.Benchmark/OpenVisionLab.Vision3D.Benchmark.csproj`.
`tests/OpenVisionLab.PackageConsumer.Smoke` is intentionally outside the solution and
must be restored against a freshly packed, isolated NuGet source/cache when package
behavior is being verified.
