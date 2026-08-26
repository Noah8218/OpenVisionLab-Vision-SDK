# 2D Object Candidate Contract

`BlobTool` and `ContourTool` publish a `candidates` list after every execution.
The list is a single-pass source-coordinate evidence stream; it contains both
accepted and rejected candidates and does not replace the legacy `results`
list.

Each `VisionObjectCandidate` contains:

- deterministic `CandidateId`, `RegionIndex`, and native detector index;
- raw `Area`, `Bounding`, `Center`, and `Angle` geometry;
- `Accepted`, `RejectReasonCode`, and `RejectReasonText`;
- the exact `AppliedLimits` used for area and bounding-box evaluation;
- `Drawing` geometry, `GenerationStage`, and `CoordinateFrame`.

`results` remains area-filtered for compatibility with existing callers. A
consumer that owns additional dimension filtering can apply it to the
candidate evidence while preserving the original tool result and overlays.
OpenVisionLab's Pipeline consumes `candidates` and applies its existing
accepted-object presentation contract without executing a relaxed second Tool.

Candidate IDs are deterministic within a tool execution and repeated runs of
the same source/configuration. Multi-ROI IDs include the region index so that
native detector indexes cannot collide across regions.

The contract is additive. Legacy `BlobResult`, `ContourResult`, `CVBlob`, and
`CVContour` APIs remain available while parity evidence is collected.

## Verification

The SDK smoke suite verifies five-candidate Blob and Contour fixtures,
accepted/rejected dimension decisions, stable IDs, source coordinates, applied
limits, drawing geometry, and repeated execution. The package-only consumer
also instantiates both tools through packed assemblies and checks the public
candidate contract.
