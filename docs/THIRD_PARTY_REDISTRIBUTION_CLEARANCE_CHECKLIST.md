# Third-party redistribution clearance checklist

Updated: 2026-09-14

Use this checklist only for the three unchanged binaries recorded in
`src/OpenVisionLab.Core/ThirdParty/provenance.json`. A binary replacement, rebuild,
or version change requires a new provenance review. This document prepares the two
remaining human approvals; it is not legal advice or approval to distribute.

## 1. OpenCvSharp/cvBlob rights-holder clarification

Send the request to a person who can authoritatively speak for the applicable
OpenCvSharp and cvBlob rights. Retain the complete response, sender identity, date,
and stable URL or message export with the release evidence.

### Request status

- Submitted to the active OpenCvSharp repository as
  [issue #2136](https://github.com/shimat/opencvsharp/issues/2136) on
  2026-09-14. The request identifies the exact package, source commit, binary hash,
  conflicting declarations, and all five questions below.
- Earlier [issue #1200](https://github.com/shimat/opencvsharp/issues/1200) records
  the maintainer's decision to remove Blob after its license difference was raised;
  merged [PR #1201](https://github.com/shimat/opencvsharp/pull/1201) performed that
  removal. Neither record defines the license scope or fulfillment conditions for
  the exact 2020 assembly, so it is supporting context rather than clearance.
- As of 2026-09-14, issue #2136 is open with no response. Submission evidence is
  preserved under
  `D:\OpenVisionLab-TestData\OpenVisionLab-Vision-SDK\PL-0014` and tracked by
  `.proofline/issues/PL-0014.json`.

### Facts to include

- NuGet package: `OpenCvSharp4 4.4.0.20200915`.
- Exact managed source commit:
  `daa955c6e0263a7ba201404e5aa72f4c1bd144ae`.
- Exact assembly: `OpenCvSharp.Blob.dll`, 40,960 bytes, SHA-256
  `E03FE75D2C9D88886384EDBC445C63DA051EE3450286C8D0982FCD9F4BC24D54`.
- The NuGet metadata and repository top-level license declare `BSD-3-Clause`.
- `src/OpenCvSharp.Blob/ReadMe.txt` broadly states LGPL for cvBlob and
  OpenCvSharp.
- Seven cvBlob-derived source files carry `LGPL-3.0-or-later` headers:
  `BlobRenderer.cs`, `CvBlobs.cs`, `CvContourChainCode.cs`,
  `CvContourPolygon.cs`, `CvTrack.cs`, `CvTracks.cs`, and `Labeller.cs`.

### Questions requiring an explicit answer

1. Which license governs each part of `OpenCvSharp.Blob.dll` at the exact commit?
2. Does `LGPL-3.0-or-later` apply only to the seven named cvBlob-derived files, to
   the complete Blob assembly, or to another defined scope?
3. Does the Blob ReadMe's statement that OpenCvSharp uses LGPL override or qualify
   the top-level and NuGet `BSD-3-Clause` declaration? If so, how?
4. May the exact unmodified Blob assembly be redistributed in a commercial NuGet
   package, and what notices, corresponding source, replacement/relinking path, or
   other conditions does the rights holder require?
5. May the response be retained and cited as the authoritative clarification for
   this exact binary and source commit?

A response that only links to the current OpenCvSharp license, discusses a different
version, or says to consult the repository does not resolve the exact scope question.

## 2. Project distribution/legal-owner approval

Complete this record only after the rights-holder response above is attached and the
candidate packages have immutable version, commit, and hashes.

```text
Decision: Approved | Rejected | More evidence required
Reviewer name and role:
Decision date and jurisdiction/scope:
Exact source commit:
Exact package version:
Five package SHA-256 values:
OpenCvSharp/cvBlob clarification evidence location:
Selected LGPL version and fulfillment method:
Corresponding-source location and retention period:
Library replacement/relinking method verified:
Intel IPPICV unmodified-byte condition verified:
Intel and all other third-party notices included:
Distribution channel and audience:
Additional conditions:
Approval signature or immutable record location:
```

The reviewer must check the candidate against the following exact technical facts:

- `OpenCvSharp.dll` and `OpenCvSharp.Blob.dll` match official OpenCvSharp
  `4.4.0.20200915` managed entries.
- `OpenCvSharpExtern.dll` matches official OpenCvSharp release
  `4.3.0.20200708` and reports OpenCV 4.3.0 with IPP/IW 2020.0.0.
- The Core package contains exactly the manifest-owned `third-party/` files; the
  other four OpenVisionLab packages contain none of them.
- The selected candidate passes `eng/Verify-PackageProvenance.ps1` with a clean
  worktree and exact commit.
- The release evidence retains the rights-holder response, this decision record,
  package manifest, and package hashes.

## 3. Repository update after approval

After both approvals exist, open a separate reviewed work item. Attach the immutable
evidence locations, update the manifest and notices, run all package provenance and
consumer gates, and follow the repository's versioned release policy. Approval of
this checklist does not itself authorize package publication, tag creation, release,
deployment, or consumer-repository changes.
