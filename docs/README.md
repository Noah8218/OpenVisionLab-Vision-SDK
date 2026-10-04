# OpenVisionLab Vision SDK Source And Contracts

The repository [README](../README.md) provides the consumer overview. Source,
project configuration and executable checks define current behavior.

## Public contracts

| Guide | Contract |
| --- | --- |
| [3D inspection](three-d-inspection.md) | Input layers, units, frames, missing samples, outcomes and lifetime |
| [2.9.1 to OpenVisionLab 3.0 migration](MIGRATING_LIB_2_9_1_TO_OPENVISIONLAB_3_0.md) | Package and namespace compatibility |
| [Affine Transform 2D](AFFINE_TRANSFORM_2D.md) | Affine tool and Pipeline behavior |
| [Object candidates](OBJECT_CANDIDATE_CONTRACT.md) | Blob/Contour evidence |
| [Edge-based unique match](EDGE_BASED_UNIQUE_MATCH_V1.md) | Fail-closed unique-result behavior |
| [Auto MPoint](AUTO_MPOINT_V1.md) | Teaching-time suggestions and representative-image input |
| [Global polarity](EDGE_BASED_GLOBAL_POLARITY_V1.md) | Optional whole-candidate contrast reversal |
| [Third-party clearance checklist](THIRD_PARTY_REDISTRIBUTION_CLEARANCE_CHECKLIST.md) | Approval against exact binary provenance |

Package guides: [Core](../src/OpenVisionLab.Core/README.md),
[Vision2D](../src/OpenVisionLab.Vision2D/README.md),
[Vision2D.Blob](../src/OpenVisionLab.Vision2D.Blob/README.md),
[Vision3D](../src/OpenVisionLab.Vision3D/README.md),
[Compute](../src/OpenVisionLab.Compute/README.md),
[Inspection](../src/OpenVisionLab.Inspection/README.md).
Keep the [Core notices](../src/OpenVisionLab.Core/ThirdParty/NOTICE.md),
[binary manifest](../src/OpenVisionLab.Core/ThirdParty/provenance.json),
[LICENSE](../LICENSE) and [NOTICE](../NOTICE) with source and applicable artifacts.

## Start Here

Open `OpenVisionLab.VisionSdk.sln`. It contains libraries and console checks; there
is no application host or repository-defined startup/launch profile. Choose
`OpenVisionLab.Inspection.Smoke` for executable contract checks.
`OpenVisionLab.Vision3D.Benchmark` is a separate console benchmark. The package-only
consumer is outside the solution.

Read the root README, the selected package's input/result contract, then the Tool's
`Execute`/`Run`. Search its name in `tests/OpenVisionLab.Inspection.Smoke/Suites`
for normal and failure cases. This is the source navigation route.

Project references (arrow means depends on): `Vision2D -> Core + Compute`;
`Vision2D.Blob -> Vision2D + Core`; `Inspection -> Vision2D + Vision3D`;
`Inspection.Smoke -> Inspection + Vision2D.Blob`; `Vision3D.Benchmark -> Vision3D`.
Vision3D depends on Compute. Core and Compute have no project references. Project files own target frameworks
and required runtime dependencies.

## Current verification entry points

```powershell
$testRoot = "D:\OpenVisionLab-TestData\OpenVisionLab-Vision-SDK\local-check"
New-Item -ItemType Directory -Force -Path $testRoot | Out-Null
$env:TEMP = $testRoot
$env:TMP = $testRoot
dotnet build OpenVisionLab.VisionSdk.sln -c Release --artifacts-path "$testRoot\build"
$assemblyDirectory = "$testRoot\build\bin\OpenVisionLab.Inspection.Smoke\release"
$smokeAssembly = "$assemblyDirectory\OpenVisionLab.Inspection.Smoke.dll"
dotnet $smokeAssembly --list
dotnet $smokeAssembly --filter "SIFT"
./eng/Verify-PublicApi.ps1 -AssemblyDirectory $assemblyDirectory
```

The filter selects case names by substring and fails when no case matches. Select
checks according to the changed source; run the full smoke assembly only when the
regression scope requires it. Synthetic checks establish the exercised library
contract. Real sensor accuracy and production performance require independent data.
Use available storage when the machine has no D: drive.
