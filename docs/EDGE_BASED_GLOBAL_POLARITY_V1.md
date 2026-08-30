# Edge-Based Global Polarity v1

Updated: 2026-07-28

Status: Current polarity contract with historical verification evidence. Use
[`OPENVISIONLAB_CURRENT_STATUS.md`](OPENVISIONLAB_CURRENT_STATUS.md) for project
state and current verification boundaries.

`IOpenCVPropertyEdgeBasedTemplateMatching.ALLOW_GLOBAL_POLARITY_REVERSAL`
enables one optional whole-candidate contrast reversal.

- `false` is the legacy/default behavior and keeps signed edge-direction
  scoring.
- `true` evaluates the complete candidate under Same or one globally reversed
  direction.
- It does not ignore polarity independently at each edge.
- Successful `MatchingResult` objects publish `PolarityReversed`.
- Metrics publish `GlobalPolarity.AllowReversal`, single-result
  `GlobalPolarity.Reversed`, and exact Same/Reversed result counts.
- Existing score, unique-match, search, angle, scale, suppression, and count
  gates remain active.

Historical verification: the former `Lib.Inspection.Smoke` runner recorded 67/67,
including legacy reversed rejection, opt-in Same, opt-in Reversed, and no-target
rejection. That count is not a current-source result. Verify the current source with:

```powershell
dotnet build OpenVisionLab.VisionSdk.sln -c Release
dotnet run --project tests\OpenVisionLab.Inspection.Smoke\OpenVisionLab.Inspection.Smoke.csproj -c Release --no-build
```

This is deterministic synthetic core evidence, not physical-feature or field
qualification.
