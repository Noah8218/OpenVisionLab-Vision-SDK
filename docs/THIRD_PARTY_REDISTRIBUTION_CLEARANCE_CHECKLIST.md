# Third-party redistribution clearance checklist

Updated: 2026-09-15

Use this checklist for the two current vendored binaries recorded in
`src/OpenVisionLab.Core/ThirdParty/provenance.json`. A binary replacement,
rebuild, or version change requires a new provenance review. This document
prepares the remaining human approval; it is not legal advice or approval to
distribute.

## 1. Project distribution/legal-owner approval

Complete this record only after the candidate packages have an immutable version,
source commit, and hashes.

```text
Decision: Approved | Rejected | More evidence required
Reviewer name and role:
Decision date and jurisdiction/scope:
Exact source commit:
Exact package version:
Five package SHA-256 values:
OpenCvSharp/OpenCV provenance evidence location:
Intel IPPICV unmodified-byte condition verified:
Intel and all other third-party notices included:
Distribution channel and audience:
Additional conditions:
Approval signature or immutable record location:
```

The reviewer must check the candidate against these exact technical facts:

- `OpenCvSharp.dll` matches the official OpenCvSharp4
  `4.4.0.20200915` `netstandard2.0` entry.
- `OpenCvSharpExtern.dll` matches official OpenCvSharp release
  `4.3.0.20200708` and reports OpenCV 4.3.0 with IPP/IW 2020.0.0.
- The Core package contains exactly the manifest-owned `third-party/` files and
  two vendored binaries; the other four OpenVisionLab packages contain neither.
- The selected candidate passes `eng/Verify-PackageProvenance.ps1` with a clean
  worktree and exact commit.
- The release evidence retains this decision record, the package manifest, and
  package hashes.

## 2. Historical removed dependency

The former `OpenCvSharp.Blob.dll` dependency, its exact LGPL evidence, and the
related rights-scope questions were resolved as a removal boundary, not as a
current redistribution dependency. The binary is deleted from the source and
Core package; its prior evidence remains traceable in Git history and the dated
PL-0013 record. Do not reintroduce those files into the current manifest or
package without a new review.

## 3. Repository update after approval

After the distribution-owner approval exists, open a separate reviewed work item.
Attach the immutable evidence locations, update the manifest and notices, run all
package provenance and consumer gates, and follow the repository's versioned
release policy. Approval of this checklist does not itself authorize package
publication, tag creation, release, deployment, or consumer-repository changes.
