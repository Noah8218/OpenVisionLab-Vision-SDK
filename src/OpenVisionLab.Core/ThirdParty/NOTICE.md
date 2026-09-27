# OpenVisionLab.Core third-party binary provenance and notice status

Updated: 2026-09-15

This is a technical provenance record, not legal advice or redistribution
clearance. `OpenVisionLab.Core` contains two vendored OpenCvSharp/OpenCV binary
files. The repository-wide MIT license covers OpenVisionLab-authored code; it does
not replace the terms that apply to these third-party files.

## Redistribution status: blocked pending distribution-owner approval

Exact official evidence identifies the IPPICV redistribution terms and ittnotify
license choice. Commercial redistribution is still blocked pending one
authoritative human decision: the project's distribution/legal owner must approve
the final notice bundle, Intel notice conditions, and distribution workflow for the
two remaining vendored binaries.

Use the [repository clearance checklist](https://github.com/Noah8218/OpenVisionLab-Vision-SDK/blob/main/docs/THIRD_PARTY_REDISTRIBUTION_CLEARANCE_CHECKLIST.md)
to request and retain that decision against the exact binaries and source commits.

Do not use this evidence set as approval to publish or commercially redistribute
the bundled binaries. Changing `redistributionClearance` from `blocked` requires a
separate reviewed decision after the distribution-owner approval is retained by the
project.

## Questions resolved by exact official evidence

### Intel IPPICV/IW 2020

Exact OpenCV core commit `d40fe356e3ea77fd6b68c6e1ccac6d0a391775ba`
pins OpenCV third-party commit `a56b6ac6f030c312b2dce17430eef13aed9af274`
and MD5 `879741A7946B814455EEE6C6FFDE2984` for
`ippicv_2020_win_intel64_20191018_general.zip`. A fresh download from that
official URL is byte-identical to the PL-0004 archive and has SHA-256
`E64E09F8A2E121D4FFF440FB12B1298BC0760F1391770AEFE5D1DEB6630352B7`.
The archive contains the Intel Simplified Software License, version April 2018,
which permits unmodified redistribution subject to its stated conditions. It also
contains `third-party-programs.txt`, which reports no separately licensed Third
Party Programs for that IPPICV package. Both exact files are preserved here.

### ittnotify

The exact OpenCV 4.3 source header `3rdparty/ittnotify/include/ittnotify.h` has
SHA-256 `5F6D683FCC91D23FEFCB7BC382DA1DB8292D1FE696B8F7664AC0B163ED601F80`
and explicitly permits selection of either BSD or GPLv2. This evidence bundle
selects the BSD-3-Clause terms and preserves both the selection header and the
exact BSD license text.

## Exact binary inventory

| Repository file | Exact official artifact and entry | Bytes | SHA-256 |
| --- | --- | ---: | --- |
| `DLL/OpenCvSharp.dll` | NuGet `OpenCvSharp4 4.4.0.20200915`, `lib/netstandard2.0/OpenCvSharp.dll` | 862,208 | `A5C477750EB4321B608F4B9183949915D4A42FE0B5D80CFB8376F5A326FA5F24` |
| `DLL/OpenCvSharpExtern.dll` | GitHub release `4.3.0.20200708`, `NativeLib/win/x64/OpenCvSharpExtern.dll` | 53,231,104 | `C9E02A255DD83C9B06CA56EC6F435F15B53A863435238FCC5D8B9082B035F249` |

The managed and native files intentionally come from different official upstream
artifacts. Do not describe the bundled set as one OpenCvSharp version.

Official containers:

- Managed NuGet package:
  <https://api.nuget.org/v3-flatcontainer/opencvsharp4/4.4.0.20200915/opencvsharp4.4.4.0.20200915.nupkg>
  (2,682,577 bytes; observed SHA-256
  `D6F6C98D45C84D0FFA0C9154400BFAAA65FF3957E290349BE9C9B1190E807BF1`;
  NuGet.org repository signature verified during PL-0004 research).
- Native release ZIP:
  <https://github.com/shimat/opencvsharp/releases/download/4.3.0.20200708/OpenCvSharp-4.3.0-20200708.zip>
  (79,315,607 bytes; observed SHA-256
  `1639AF0E08245F7A50D3A299636EF36ACC527CA5CCFFB1F97CEC861C774D97EB`).
  GitHub exposed no publisher digest for asset ID `22677192`, so this container
  hash is an observed download hash, not an upstream checksum or signature.
- Exact IPPICV archive:
  <https://raw.githubusercontent.com/opencv/opencv_3rdparty/a56b6ac6f030c312b2dce17430eef13aed9af274/ippicv/ippicv_2020_win_intel64_20191018_general.zip>
  (35,798,082 bytes; OpenCV-pinned MD5
  `879741A7946B814455EEE6C6FFDE2984`; observed SHA-256
  `E64E09F8A2E121D4FFF440FB12B1298BC0760F1391770AEFE5D1DEB6630352B7`).

The managed assemblies carry product version
`1.0.0+daa955c6e0263a7ba201404e5aa72f4c1bd144ae`. That revision is an official
OpenCvSharp commit, but it is not the `4.4.0.20200916` tag commit. The byte match to
the official NuGet entries is the release identity used here.

The native DLL reports OpenCV 4.3.0, core revision
`d40fe356e3ea77fd6b68c6e1ccac6d0a391775ba`, contrib revision
`0d92fd8041ae36d855ca40dd444b2102e754bfe3`, a static MSVC 1925 `/MT` build,
Intel IPP/IW 2020.0.0, and non-free algorithms enabled. Its exact official release
tag commit is `206eba074db5e85b09843ae1f9275ef192969e1c`.

## Preserved official evidence

The Core package carries every file below under `third-party/`:

- OpenCvSharp and OpenCV core/contrib BSD license texts from the exact source
  revisions;
- the exact IPPICV 2020 EULA and its `third-party-programs.txt` declaration;
- the exact ittnotify dual-license header and selected BSD license; and
- the exact OpenCV 4.3 source notices for DNN Torch import, Jasper,
  libjpeg-turbo/IJG, libpng, libtiff, libwebp, OpenEXR/IlmImf, protobuf, quirc,
  SoftFloat, and zlib.

The native build reports `ittnotify`, `libprotobuf`, `zlib`, `libjpeg-turbo`,
`libwebp`, `libpng`, `libtiff`, `libjasper`, `IlmImf`, `quirc`, `ippiw`, and
`ippicv`. The manifest records the exact source URL, size, and SHA-256 for each
preserved document. Inclusion records provenance and terms; it is not a legal
determination that the final commercial distribution method satisfies them.

The removed `OpenCvSharp.Blob` binary and its LGPL evidence remain only in Git
history and dated PL-0013 records as historical provenance. They are not current
Core package files, manifest entries, or redistribution dependencies.

## Distribution-owner approval checklist

- Confirm the Intel notice and no-modification conditions against the bytes being
  released.
- Review the final `third-party/` contents and package hashes.
- Record explicit distribution/legal-owner approval before changing the blocked
  manifest state or publishing a package.
