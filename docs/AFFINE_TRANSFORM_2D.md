# AffineTransform 2D

`OpenVisionLab.Vision2D.Tool.AffineTransformTool` provides an additive, deterministic
three-point 2D affine transform.

## Contract

- Teach three non-collinear source points and the three corresponding destination points.
- Calculate the matrix with OpenCV `GetAffineTransform`.
- Execute the image mapping with OpenCV `WarpAffine`.
- Configure output size, interpolation, border policy, and minimum valid-pixel ratio.
- Review the six matrix coefficients, determinant, scale, rotation, shear, translation,
  triangle-area, and valid-pixel metrics.
- Review the destination triangle and transformed source-frame overlays.
- Fail closed on invalid points, degenerate triangles, invalid output/sampling/gates,
  or insufficient source coverage.

Canonical factory name: `AffineTransform`.

Compatibility aliases: `Affine`, `AffineMatrix`.

## Version and compatibility

The API baseline is `3.0.0`, the assembly/file version is `3.0.0.0`, and the
repository-local package default is `3.0.1-dev.1`. Install the exact immutable
package version produced or published for the selected package source; do not infer
an installable package version from the API baseline.
The `AffineTransformTool` type/member and numerical contract is preserved from
`Lib.OpenCV 2.9.1`; only the package, assembly and namespace identity changes.

Consumers should record the vendored DLL SHA-256 and file version. Do not replace
the full dependency set unless the consumer has verified that its legacy APIs are
still present.

## Verification

```powershell
dotnet build OpenVisionLab.VisionSdk.sln -c Debug -p:Platform="Any CPU"
dotnet run --project tests\OpenVisionLab.Inspection.Smoke\OpenVisionLab.Inspection.Smoke.csproj -c Debug --no-build
```

The smoke suite includes a known six-coefficient matrix, zero-gate collinear-source
rejection, and insufficient-coverage evidence retention.
