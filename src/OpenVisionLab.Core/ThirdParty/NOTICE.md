# OpenVisionLab.Core third-party binary provenance and notice status

Updated: 2026-09-14

This is a technical provenance record, not legal advice or redistribution
clearance. `OpenVisionLab.Core` contains three vendored OpenCvSharp/OpenCV binary
files. The repository-wide MIT license covers OpenVisionLab-authored code; it does
not replace the terms that apply to these third-party files.

## Redistribution status: blocked pending two approvals

Exact official evidence now identifies the previously uncertain LGPL version,
IPPICV redistribution terms, and ittnotify license choice. Commercial
redistribution is still blocked because two decisions require authoritative human
approval:

1. Obtain written OpenCvSharp/cvBlob rights-holder clarification of the scope
   relationship between the NuGet/top-level `BSD-3-Clause` declaration, the Blob
   ReadMe's broad LGPL statement, and the seven cvBlob-derived source files marked
   `LGPL-3.0-or-later`.
2. Have the project's distribution/legal owner approve the final notice bundle and
   the chosen LGPL source/relinking fulfillment method, Intel notice conditions,
   and distribution workflow.

Use the [repository clearance checklist](https://github.com/Noah8218/OpenVisionLab-Vision-SDK/blob/main/docs/THIRD_PARTY_REDISTRIBUTION_CLEARANCE_CHECKLIST.md)
to request and retain both decisions against the exact binaries and source commits.

Do not use this evidence set as approval to publish or commercially redistribute
the bundled binaries. Changing `redistributionClearance` from `blocked` requires a
separate reviewed decision after both approvals are retained by the project.

## Questions resolved by exact official evidence

### OpenCvSharp.Blob LGPL version

The two managed assemblies exactly match official NuGet package `OpenCvSharp4
4.4.0.20200915` and report source commit
`daa955c6e0263a7ba201404e5aa72f4c1bd144ae`. At that exact commit,
`BlobRenderer.cs`, `CvBlobs.cs`, `CvContourChainCode.cs`,
`CvContourPolygon.cs`, `CvTrack.cs`, `CvTracks.cs`, and `Labeller.cs` each state
that their cvBlob-derived code is offered under LGPL version 3 or any later
version. The package now preserves that header, LGPL 3.0, and the incorporated GPL
3.0 text. This resolves the missing-version question; it does not resolve the
rights-scope question named above.

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
selects the BSD terms and preserves both the selection header and the exact BSD
license text.

## Exact binary inventory

| Repository file | Exact official artifact and entry | Bytes | SHA-256 |
| --- | --- | ---: | --- |
| `DLL/OpenCvSharp.dll` | NuGet `OpenCvSharp4 4.4.0.20200915`, `lib/netstandard2.0/OpenCvSharp.dll` | 862,208 | `A5C477750EB4321B608F4B9183949915D4A42FE0B5D80CFB8376F5A326FA5F24` |
| `DLL/OpenCvSharp.Blob.dll` | NuGet `OpenCvSharp4 4.4.0.20200915`, `lib/netstandard2.0/OpenCvSharp.Blob.dll` | 40,960 | `E03FE75D2C9D88886384EDBC445C63DA051EE3450286C8D0982FCD9F4BC24D54` |
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
- the exact Blob ReadMe, representative `LGPL-3.0-or-later` source header, and
  complete LGPL 3.0 plus GPL 3.0 texts;
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

## Distribution-owner approval checklist

- Retain the written OpenCvSharp/cvBlob scope clarification with the release
  evidence.
- Select and document the LGPL source/relinking fulfillment method for the exact
  unmodified Blob assembly.
- Confirm the Intel notice and no-modification conditions against the bytes being
  released.
- Review the final `third-party/` contents and package hashes.
- Record explicit distribution/legal-owner approval before changing the blocked
  manifest state or publishing a package.
