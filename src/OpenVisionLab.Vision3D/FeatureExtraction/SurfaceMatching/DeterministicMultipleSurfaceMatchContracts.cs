using System;
using System.Collections.Generic;

namespace OpenVisionLab.Vision3D.FeatureExtraction
{
    /// <summary>
    /// Source-neutral bounds for deterministic multiple-instance surface
    /// matching. Product identity, persistence, and acceptance stay with the
    /// caller.
    /// </summary>
    public sealed class DeterministicMultipleSurfaceMatchOptions
    {
        public DeterministicRigidSurfacePoseSearchOptions PoseSearchOptions
        {
            get;
            set;
        }

        public int MaximumMatchCount { get; set; }

        public int MaximumExpandedCandidateCount { get; set; }
    }

    public sealed class DeterministicSurfaceMatchInstance
    {
        public DeterministicSurfaceMatchInstance(
            int order,
            int evaluatedCandidateCount,
            RigidSurfacePose pose,
            DeterministicSurfaceCoverageResult coverage)
        {
            Order = order;
            EvaluatedCandidateCount = evaluatedCandidateCount;
            Pose = pose;
            Coverage = coverage;
        }

        internal DeterministicSurfaceMatchInstance(
            int order, int evaluatedCandidateCount, RigidSurfacePose pose,
            DeterministicSurfaceCoverageResult coverage, SurfacePoseRefinementResult refinementEvidence,
            SurfacePoseAmbiguityEvidence ambiguityEvidence = null,
            SurfacePoseProposalEvidence proposalEvidence = null,
            SurfacePoseAmbiguityEvidence finalAmbiguityEvidence = null)
            : this(order, evaluatedCandidateCount, pose, coverage)
        {
            RefinementEvidence = refinementEvidence;
            AmbiguityEvidence = ambiguityEvidence;
            ProposalEvidence = proposalEvidence;
            FinalAmbiguityEvidence = finalAmbiguityEvidence;
        }

        public int Order { get; }

        public int EvaluatedCandidateCount { get; }

        public RigidSurfacePose Pose { get; }

        public DeterministicSurfaceCoverageResult Coverage { get; }

        public SurfacePoseRefinementResult RefinementEvidence { get; }

        public SurfacePoseAmbiguityEvidence AmbiguityEvidence { get; }

        public SurfacePoseProposalEvidence ProposalEvidence { get; }

        public SurfacePoseAmbiguityEvidence FinalAmbiguityEvidence { get; }
    }

    public sealed class DeterministicMultipleSurfaceMatchResult
    {
        public const string Semantics =
            "ranked-greedy-disjoint-scene-sample-surface-matches-v1";

        private DeterministicMultipleSurfaceMatchResult(
            bool success,
            string message,
            int evaluatedCandidateCount,
            IReadOnlyList<DeterministicSurfaceMatchInstance> matches,
            string stopReason)
        {
            Success = success;
            Message = message ?? string.Empty;
            EvaluatedCandidateCount = evaluatedCandidateCount;
            Matches = matches ?? Array.Empty<DeterministicSurfaceMatchInstance>();
            StopReason = stopReason ?? string.Empty;
        }

        public bool Success { get; }

        public string Message { get; }

        public int EvaluatedCandidateCount { get; }

        public IReadOnlyList<DeterministicSurfaceMatchInstance> Matches
        {
            get;
        }

        public string StopReason { get; }

        internal static DeterministicMultipleSurfaceMatchResult Completed(
            int evaluatedCandidateCount,
            IReadOnlyList<DeterministicSurfaceMatchInstance> matches,
            string stopReason)
        {
            return new DeterministicMultipleSurfaceMatchResult(
                true,
                string.Empty,
                evaluatedCandidateCount,
                matches,
                stopReason);
        }

        internal static DeterministicMultipleSurfaceMatchResult Failed(
            string message)
        {
            return new DeterministicMultipleSurfaceMatchResult(
                false,
                message,
                0,
                Array.Empty<DeterministicSurfaceMatchInstance>(),
                string.Empty);
        }
    }
}
