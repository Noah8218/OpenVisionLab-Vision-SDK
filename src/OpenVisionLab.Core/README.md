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
- a `.NET Framework`-only `buildTransitive` fallback that copies the native DLL to
  the consumer output.

`OpenCvSharpExtern.dll` currently makes Windows x64 the supported native runtime.
Modern SDK-style `win-x64` consumers resolve the runtime asset directly; the
`.NET Framework` fallback has been reviewed from source but has not been executed in
a runtime consumer. Keep all OpenVisionLab packages on the same version.

This package deliberately does not provide WinForms/WPF image conversion, serial-port,
system-time, drive-management, or other application-platform helpers. Consumers that
display a `Mat` should keep framework-specific conversion in their own UI adapter.

[Repository and full documentation](https://github.com/Noah8218/OpenVisionLab-Vision-SDK)
