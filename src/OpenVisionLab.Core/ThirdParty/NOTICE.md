# OpenVisionLab.Core third-party binary provenance and notice status

Updated: 2026-08-31

This is a technical provenance record, not legal advice or redistribution
clearance. `OpenVisionLab.Core` contains three vendored OpenCvSharp/OpenCV binary
files. The repository-wide MIT license covers OpenVisionLab-authored code; it does
not replace the terms that apply to these third-party files.

## Redistribution status: blocked

The exact upstream bytes are identified, but the minimum complete redistribution
notice set is not yet authoritative:

- the official `OpenCvSharp4 4.4.0.20200915` package and repository top-level
  license declare `BSD-3-Clause`, while the same source revision's
  `OpenCvSharp.Blob/ReadMe.txt` says that cvblob and OpenCvSharp use `LGPL` without
  naming an LGPL version or providing its complete text;
- the exact native build statically reports Intel IPP/IW 2020.0.0 and other
  third-party components, but the exact IPPICV archive does not contain an
  authoritative complete redistribution license; and
- the applicable ittnotify license choice has not been confirmed.

Do not use this provisional evidence set as approval to publish or commercially
redistribute the bundled binaries. Unblocking requires written clarification from
the applicable OpenCvSharp/cvblob rights holder for the Blob BSD/LGPL conflict and
LGPL version, authoritative Intel/OpenCV confirmation of redistribution rights for
the exact IPPICV 2020 archive, confirmation of the ittnotify license selection, and
approval of the final notice bundle by the project's distribution/legal owner.

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

The managed assemblies carry product version
`1.0.0+daa955c6e0263a7ba201404e5aa72f4c1bd144ae`. That revision is an official
OpenCvSharp commit, but it is not the `4.4.0.20200916` tag commit. The byte match to
the official NuGet entries is the release identity used here.

The native DLL reports OpenCV 4.3.0, core revision
`d40fe356e3ea77fd6b68c6e1ccac6d0a391775ba`, contrib revision
`0d92fd8041ae36d855ca40dd444b2102e754bfe3`, a static MSVC 1925 `/MT` build,
Intel IPP/IW 2020.0.0, and non-free algorithms enabled. Its exact official release
tag commit is `206eba074db5e85b09843ae1f9275ef192969e1c`.

## Preserved official license evidence

The package preserves these exact upstream texts while the conflict is unresolved:

- `licenses/OpenCvSharp-BSD-3-Clause.txt` from the managed source revision;
- `licenses/OpenCV-4.3-BSD-3-Clause.txt` from the exact OpenCV core revision;
- `licenses/OpenCV-Contrib-BSD-3-Clause.txt` from the exact contrib revision; and
- `evidence/OpenCvSharp.Blob-ReadMe.txt`, which preserves the conflicting LGPL
  statement without inventing a license version.

The native build also reports `ittnotify`, `libprotobuf`, `zlib`,
`libjpeg-turbo`, `libwebp`, `libpng`, `libtiff`, `libjasper`, `IlmImf`, `quirc`,
`ippiw`, and `ippicv`. Their official source notices were inspected during PL-0004,
but this package does not label that inspected collection as the final applicable
license set while the Blob, IPPICV, and ittnotify questions remain open.
