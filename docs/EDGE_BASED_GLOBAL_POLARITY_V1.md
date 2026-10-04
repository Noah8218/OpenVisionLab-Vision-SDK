# Edge-Based Global Polarity v1

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
