# OpenVisionLab.Core

Shared runtime and geometry support for OpenVisionLab Vision SDK 3.0.

Most applications should install `OpenVisionLab.Vision2D`, `OpenVisionLab.Vision2D.Blob`, or `OpenVisionLab.Inspection` and receive this package transitively.

`3.0.0` is the API/assembly baseline, not the install version. Use the exact
immutable version from your package source; `3.0.1-dev.1` is only the current
repository-local default.

```powershell
$packageVersion = "3.0.1-dev.1" # Replace when pack or your feed uses another version.
dotnet add package OpenVisionLab.Core --version $packageVersion
```

The package contains:

- UI-independent numeric, coordinate, ROI, and 2D geometry utilities;
- managed OpenCvSharp assemblies used by the SDK;
- `runtimes/win-x64/native/OpenCvSharpExtern.dll`;
- `third-party/provenance.json` plus the provisional notice and preserved official
  license/conflict evidence under `third-party/`;
- a `.NET Framework`-only `buildTransitive` fallback that copies the native DLL to
  the consumer output.

`OpenCvSharpExtern.dll` currently makes Windows x64 the supported native runtime.
Modern SDK-style `win-x64` consumers resolve the runtime asset directly; the
`.NET Framework` fallback has been reviewed from source but has not been executed in
a runtime consumer. Keep all OpenVisionLab packages on the same version.

The managed DLLs match official `OpenCvSharp4 4.4.0.20200915`; the native DLL
matches the official OpenCvSharp `4.3.0.20200708` release ZIP. Do not collapse this
mixed provenance into one upstream version. The exact bytes are locked, but
redistribution clearance remains blocked because official OpenCvSharp evidence
conflicts between BSD-3-Clause and an unspecified LGPL statement for Blob, and the
native IPPICV/ittnotify terms are incomplete. Read `third-party/NOTICE.md` in this
package before any redistribution decision; it is technical evidence, not legal
advice or publication approval.

This package deliberately does not provide WinForms/WPF image conversion, serial-port,
system-time, drive-management, or other application-platform helpers. Consumers that
display a `Mat` should keep framework-specific conversion in their own UI adapter.

## Numerical geometry contracts

`Geometry2D.LineFittingCalculator` fits `y = Slope*x + Intercept`. Use `LineFitY`
for `x = Slope*y + Intercept` when fitting a near-vertical line. At least two finite
points with distinct independent-axis coordinates are required; empty, singleton,
non-finite and degenerate inputs throw `ArgumentException` (null sequences throw
`ArgumentNullException`). Inputs are enumerated once and then materialized. A
calculator stores its last successful coefficients; do not read them as a new
measurement after a failed fit or share the instance between concurrent calls.

`FindLinearLeastSquaresFit` returns `sqrt(sum(residualY²))`, not RMS. `ErrorSquared`
is a raw arithmetic helper; it does not validate inputs or promise finite output.
Coefficients use double arithmetic, but `PointF` has float precision, `LineFit`
returns float endpoints, and `LineFitX/Y` return truncated integer endpoints.
`LinearLeastSquaresFit(points, size)` extrapolates to X=0 and X=`size.Width`; it
does not clip Y to `size.Height` and converts through `PointF`. Choose the overload
according to coordinate magnitude and required resolution.

```csharp
using System;
using System.Drawing;
using OpenVisionLab.Core.Geometry2D;

PointF[] points = { new PointF(0, 3), new PointF(1, 5), new PointF(2, 7) };
double error = LineFittingCalculator.FindLinearLeastSquaresFit(points, out double slope, out double intercept);
Console.WriteLine($"y={slope}*x+{intercept}; residual norm={error}"); // 2, 3, 0
```

`FormulaUtil.threePointAngle` returns degrees in [0, 180]; a coincident vector
endpoint produces `double.NaN`, which callers must reject before applying a limit.
Finite arithmetic and synthetic tests do not certify arbitrary coordinate scales
or calibrated measurement accuracy. Legacy `C*` types retain the separately
documented [migration differences](../../docs/MIGRATING_LIB_2_9_1_TO_OPENVISIONLAB_3_0.md).

[Repository and full documentation](https://github.com/Noah8218/OpenVisionLab-Vision-SDK)
