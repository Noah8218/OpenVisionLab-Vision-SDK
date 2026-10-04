using OpenVisionLab.Compute;
using System;
using System.Collections.Generic;
using System.Threading;

namespace OpenVisionLab.Vision3D.FeatureExtraction
{
    /// <summary>
    /// One source-neutral ordered point used by deterministic surface
    /// matching. Unit, frame, source identity, and persistence remain owned
    /// by the caller.
    /// </summary>
    public sealed class SurfaceMatchSample
    {
        public SurfaceMatchSample(int order, ThreeDPoint position)
        {
            Order = order;
            Position = position;
        }

        public int Order { get; }

        public ThreeDPoint Position { get; }
    }

    /// <summary>
    /// Row-major rigid transform. Points are transformed as target =
    /// rotation * source + translation.
    /// </summary>
    public sealed class RigidSurfacePose
    {
        public RigidSurfacePose(
            double m11,
            double m12,
            double m13,
            double m21,
            double m22,
            double m23,
            double m31,
            double m32,
            double m33,
            double translationX,
            double translationY,
            double translationZ)
        {
            M11 = m11;
            M12 = m12;
            M13 = m13;
            M21 = m21;
            M22 = m22;
            M23 = m23;
            M31 = m31;
            M32 = m32;
            M33 = m33;
            TranslationX = translationX;
            TranslationY = translationY;
            TranslationZ = translationZ;
        }

        public double M11 { get; }
        public double M12 { get; }
        public double M13 { get; }
        public double M21 { get; }
        public double M22 { get; }
        public double M23 { get; }
        public double M31 { get; }
        public double M32 { get; }
        public double M33 { get; }
        public double TranslationX { get; }
        public double TranslationY { get; }
        public double TranslationZ { get; }

        public ThreeDPoint Transform(ThreeDPoint point)
        {
            if (point == null)
            {
                throw new ArgumentNullException(nameof(point));
            }

            return new ThreeDPoint(
                M11 * point.X + M12 * point.Y + M13 * point.Z + TranslationX,
                M21 * point.X + M22 * point.Y + M23 * point.Z + TranslationY,
                M31 * point.X + M32 * point.Y + M33 * point.Z + TranslationZ);
        }

        public bool IsRigid(double tolerance)
        {
            if (!SurfaceMatchingContractValidation.IsFinite(tolerance)
                || tolerance <= 0.0
                || !AllValuesFinite())
            {
                return false;
            }

            double firstLength = M11 * M11 + M12 * M12 + M13 * M13;
            double secondLength = M21 * M21 + M22 * M22 + M23 * M23;
            double thirdLength = M31 * M31 + M32 * M32 + M33 * M33;
            double firstSecond = M11 * M21 + M12 * M22 + M13 * M23;
            double firstThird = M11 * M31 + M12 * M32 + M13 * M33;
            double secondThird = M21 * M31 + M22 * M32 + M23 * M33;
            double determinant =
                M11 * (M22 * M33 - M23 * M32)
                - M12 * (M21 * M33 - M23 * M31)
                + M13 * (M21 * M32 - M22 * M31);
            return Math.Abs(firstLength - 1.0) <= tolerance
                && Math.Abs(secondLength - 1.0) <= tolerance
                && Math.Abs(thirdLength - 1.0) <= tolerance
                && Math.Abs(firstSecond) <= tolerance
                && Math.Abs(firstThird) <= tolerance
                && Math.Abs(secondThird) <= tolerance
                && Math.Abs(determinant - 1.0) <= tolerance;
        }

        private bool AllValuesFinite()
        {
            double[] values =
            {
                M11, M12, M13,
                M21, M22, M23,
                M31, M32, M33,
                TranslationX, TranslationY, TranslationZ
            };
            for (int index = 0; index < values.Length; index++)
            {
                if (!SurfaceMatchingContractValidation.IsFinite(values[index]))
                {
                    return false;
                }
            }

            return true;
        }
    }

    public sealed class SurfaceCoverageMatch
    {
        public SurfaceCoverageMatch(
            int modelSampleOrder,
            int sceneSampleOrder,
            double distance)
        {
            ModelSampleOrder = modelSampleOrder;
            SceneSampleOrder = sceneSampleOrder;
            Distance = distance;
        }

        public int ModelSampleOrder { get; }

        public int SceneSampleOrder { get; }

        public double Distance { get; }
    }

    /// <summary>
    /// Raw, decision-free one-way coverage evidence. Each scene sample can be
    /// claimed once and the denominator is always the model sample count.
    /// </summary>
    public sealed class DeterministicSurfaceCoverageResult
    {
        public const string Semantics =
            "one-way-model-sample-greedy-unique-nearest-position-v1";

        private DeterministicSurfaceCoverageResult(
            bool success,
            string message,
            int modelSampleCount,
            int sceneSampleCount,
            int matchedModelSampleCount,
            double coverageRatio,
            bool hasInlierRmse,
            double inlierRmse,
            double maximumCorrespondenceDistance,
            IReadOnlyList<SurfaceCoverageMatch> matches)
        {
            Success = success;
            Message = message ?? string.Empty;
            ModelSampleCount = modelSampleCount;
            SceneSampleCount = sceneSampleCount;
            MatchedModelSampleCount = matchedModelSampleCount;
            CoverageRatio = coverageRatio;
            HasInlierRmse = hasInlierRmse;
            InlierRmse = inlierRmse;
            MaximumCorrespondenceDistance = maximumCorrespondenceDistance;
            Matches = matches ?? Array.Empty<SurfaceCoverageMatch>();
        }

        public bool Success { get; }
        public string Message { get; }
        public int ModelSampleCount { get; }
        public int SceneSampleCount { get; }
        public int MatchedModelSampleCount { get; }
        public int UnmatchedModelSampleCount =>
            ModelSampleCount - MatchedModelSampleCount;
        public double CoverageRatio { get; }
        public bool HasInlierRmse { get; }
        public double InlierRmse { get; }
        public double MaximumCorrespondenceDistance { get; }
        public IReadOnlyList<SurfaceCoverageMatch> Matches { get; }

        internal static DeterministicSurfaceCoverageResult Completed(
            int modelSampleCount,
            int sceneSampleCount,
            int matchedModelSampleCount,
            double coverageRatio,
            bool hasInlierRmse,
            double inlierRmse,
            double maximumCorrespondenceDistance,
            IReadOnlyList<SurfaceCoverageMatch> matches)
        {
            return new DeterministicSurfaceCoverageResult(
                true,
                string.Empty,
                modelSampleCount,
                sceneSampleCount,
                matchedModelSampleCount,
                coverageRatio,
                hasInlierRmse,
                inlierRmse,
                maximumCorrespondenceDistance,
                matches);
        }

        internal static DeterministicSurfaceCoverageResult Failed(
            string message)
        {
            return new DeterministicSurfaceCoverageResult(
                false,
                message,
                0,
                0,
                0,
                0.0,
                false,
                double.NaN,
                double.NaN,
                Array.Empty<SurfaceCoverageMatch>());
        }
    }

    /// <summary>
    /// Explicit Euler rotation grid, translation bounds, correspondence
    /// limit, match requirement, and candidate budget for one pose search.
    /// </summary>
    /// <summary>Opt-in farthest-position anchors. Selection only proposes poses; coverage always uses original samples.</summary>
    public sealed class SurfacePoseProposalOptions
    {
        public const int AbsoluteMaximumAnchorCount = 1024;
        public int MaximumModelAnchorCount { get; set; } = 32;
        public int MaximumSceneAnchorCount { get; set; } = 64;
    }

    /// <summary>Immutable identity of the reduced coarse proposal domain, not evidence of global pose uniqueness.</summary>
    public sealed class SurfacePoseProposalEvidence
    {
        internal SurfacePoseProposalEvidence(int modelSampleCount, int sceneSampleCount, int availableSceneSampleCount, int admissibleSceneSampleCount,
            IReadOnlyList<SurfaceMatchSample> modelAnchors, IReadOnlyList<SurfaceMatchSample> sceneAnchors,
            IReadOnlyList<int> sceneOrderMap = null)
        {
            ModelSampleCount = modelSampleCount;
            SceneSampleCount = sceneSampleCount;
            AvailableSceneSampleCount = availableSceneSampleCount;
            AdmissibleSceneSampleCount = admissibleSceneSampleCount;
            List<int> modelOrders = new List<int>(modelAnchors.Count);
            List<int> sceneOrders = new List<int>(sceneAnchors.Count);
            for (int index = 0; index < modelAnchors.Count; index++) modelOrders.Add(modelAnchors[index].Order);
            for (int index = 0; index < sceneAnchors.Count; index++)
                sceneOrders.Add(sceneOrderMap == null ? sceneAnchors[index].Order : sceneOrderMap[sceneAnchors[index].Order]);
            ModelSampleOrders = modelOrders.AsReadOnly();
            SceneSampleOrders = sceneOrders.AsReadOnly();
        }

        public int ModelSampleCount { get; }
        public int SceneSampleCount { get; }
        public int AvailableSceneSampleCount { get; }
        public int AdmissibleSceneSampleCount { get; }
        public IReadOnlyList<int> ModelSampleOrders { get; }
        public IReadOnlyList<int> SceneSampleOrders { get; }
    }

    public sealed class DeterministicRigidSurfacePoseSearchOptions
    {
        public double MinimumRotationXDegrees { get; set; }
        public double MaximumRotationXDegrees { get; set; }
        public double RotationStepXDegrees { get; set; }
        public double MinimumRotationYDegrees { get; set; }
        public double MaximumRotationYDegrees { get; set; }
        public double RotationStepYDegrees { get; set; }
        public double MinimumRotationZDegrees { get; set; }
        public double MaximumRotationZDegrees { get; set; }
        public double RotationStepZDegrees { get; set; }
        public double MinimumTranslationX { get; set; }
        public double MaximumTranslationX { get; set; }
        public double MinimumTranslationY { get; set; }
        public double MaximumTranslationY { get; set; }
        public double MinimumTranslationZ { get; set; }
        public double MaximumTranslationZ { get; set; }
        public double MaximumCorrespondenceDistance { get; set; }
        public int MinimumMatchedSampleCount { get; set; }
        public int MaximumCandidateCount { get; set; }

        /// <summary>Single search: false preserves centroid-only translation; true adds every sample-pair anchor within MaximumCandidateCount. Multiple search always uses sample-pair anchors.</summary>
        public bool UseSamplePairTranslationCandidates { get; set; }

        /// <summary>Null keeps the full anchor domain. Single search requires sample-pair candidates; multiple search always uses anchors.</summary>
        public SurfacePoseProposalOptions ProposalOptions { get; set; }

        /// <summary>Null skips competing-pose comparison. Evidence covers the declared coarse candidate domain, separately from local refinement.</summary>
        public SurfacePoseAmbiguityOptions AmbiguityOptions { get; set; }

        /// <summary>Null preserves the coarse Euler-grid path. Set explicitly to refine a matched pose locally.</summary>
        public SurfacePoseRefinementOptions RefinementOptions { get; set; }
    }

    /// <summary>
    /// Pose-search execution state plus the selected pose and raw coverage
    /// evidence. Matched is independent of application acceptance policy.
    /// </summary>
    public sealed class DeterministicRigidSurfacePoseSearchResult
    {
        private DeterministicRigidSurfacePoseSearchResult(
            bool success,
            string message,
            bool matched,
            int evaluatedCandidateCount,
            RigidSurfacePose pose,
            DeterministicSurfaceCoverageResult coverage,
            string rejectionReason,
            SurfacePoseRefinementResult refinementEvidence = null,
            SurfacePoseAmbiguityEvidence ambiguityEvidence = null,
            SurfacePoseProposalEvidence proposalEvidence = null,
            SurfacePoseAmbiguityEvidence finalAmbiguityEvidence = null,
            ComputeExecutionEvidence computeDiagnostics = null)
        {
            ComputeDiagnostics = computeDiagnostics;
            Success = success;
            Message = message ?? string.Empty;
            Matched = matched;
            EvaluatedCandidateCount = evaluatedCandidateCount;
            Pose = pose;
            Coverage = coverage;
            RejectionReason = rejectionReason ?? string.Empty;
            RefinementEvidence = refinementEvidence;
            AmbiguityEvidence = ambiguityEvidence;
            ProposalEvidence = proposalEvidence;
            FinalAmbiguityEvidence = finalAmbiguityEvidence;
        }

        public bool Success { get; }
        public string Message { get; }
        public bool Matched { get; }
        public int EvaluatedCandidateCount { get; }
        public RigidSurfacePose Pose { get; }
        public DeterministicSurfaceCoverageResult Coverage { get; }
        public string RejectionReason { get; }
        public SurfacePoseRefinementResult RefinementEvidence { get; }
        public SurfacePoseAmbiguityEvidence AmbiguityEvidence { get; }
        public SurfacePoseProposalEvidence ProposalEvidence { get; }
        /// <summary>Opt-in refined-candidate comparison against Pose/Coverage. Require ComparisonComplete before interpreting a non-ambiguous result.</summary>
        public SurfacePoseAmbiguityEvidence FinalAmbiguityEvidence { get; }
        /// <summary>Immutable compute snapshot; null when validation failed before compute selection.</summary>
        public ComputeExecutionEvidence ComputeDiagnostics { get; }

        internal static DeterministicRigidSurfacePoseSearchResult Completed(
            bool matched,
            int evaluatedCandidateCount,
            RigidSurfacePose pose,
            DeterministicSurfaceCoverageResult coverage,
            string rejectionReason,
            SurfacePoseRefinementResult refinementEvidence = null,
            SurfacePoseAmbiguityEvidence ambiguityEvidence = null,
            SurfacePoseProposalEvidence proposalEvidence = null,
            SurfacePoseAmbiguityEvidence finalAmbiguityEvidence = null,
            ComputeExecutionEvidence computeDiagnostics = null)
        {
            return new DeterministicRigidSurfacePoseSearchResult(
                true,
                string.Empty,
                matched,
                evaluatedCandidateCount,
                pose,
                coverage,
                rejectionReason,
                refinementEvidence,
                ambiguityEvidence,
                proposalEvidence,
                finalAmbiguityEvidence,
                computeDiagnostics);
        }

        internal static DeterministicRigidSurfacePoseSearchResult Failed(
            string message, ComputeExecutionEvidence computeDiagnostics = null)
        {
            return new DeterministicRigidSurfacePoseSearchResult(
                false,
                message,
                false,
                0,
                null,
                null,
                string.Empty, computeDiagnostics: computeDiagnostics);
        }
    }

    /// <summary>Explicit competing-pose policy; refinement comparison is opt-in. Distances and RMSE use the input XYZ unit.</summary>
    public sealed class SurfacePoseAmbiguityOptions
    {
        public double MinimumRotationSeparationDegrees { get; set; }
        /// <summary>Null preserves orientation-only comparison. A finite positive value also distinguishes model-origin translations at least this far apart, within MaximumTranslationSeparation.</summary>
        public double? MinimumTranslationSeparation { get; set; }
        /// <summary>Maximum separation of model origins in scene coordinates for alternatives belonging to this instance.</summary>
        public double MaximumTranslationSeparation { get; set; }
        public int MaximumMatchedSampleCountDifference { get; set; }
        public double MaximumRmseDifference { get; set; }
        /// <summary>Null preserves coarse-only evidence. A positive budget also compares refined seeds that meet MinimumMatchedSampleCount; requires RefinementOptions. Exhaustion is incomplete evidence, not uniqueness.</summary>
        public int? MaximumRefinedCandidateCount { get; set; }
    }

    /// <summary>Competing-pose evidence within the authored coarse seed domain. Refined comparison admits only seeds meeting the coarse support minimum and compares their refined or retained poses against the returned pose. Translation mode selects the strongest qualifying alternative; null preserves the strongest rotated coarse diagnostic alternative. Absence of ambiguity does not prove global pose uniqueness.</summary>
    public sealed class SurfacePoseAmbiguityEvidence
    {
        internal SurfacePoseAmbiguityEvidence(RigidSurfacePose referencePose, DeterministicSurfaceCoverageResult referenceCoverage,
            RigidSurfacePose alternativePose, DeterministicSurfaceCoverageResult alternativeCoverage, int evaluatedCandidateCount,
            double rotationSeparationDegrees, bool isAmbiguous, SurfacePoseAmbiguityOptions options, double translationSeparation = double.NaN,
            bool isRefinedComparison = false, bool comparisonComplete = true, int refinedCandidateCount = 0)
        {
            ReferencePose = referencePose;
            ReferenceCoverage = referenceCoverage;
            AlternativePose = alternativePose;
            AlternativeCoverage = alternativeCoverage;
            EvaluatedCandidateCount = evaluatedCandidateCount;
            RotationSeparationDegrees = rotationSeparationDegrees;
            TranslationSeparation = translationSeparation;
            IsAmbiguous = isAmbiguous;
            IsRefinedComparison = isRefinedComparison;
            ComparisonComplete = comparisonComplete;
            RefinedCandidateCount = refinedCandidateCount;
            MaximumRefinedCandidateCount = options.MaximumRefinedCandidateCount;
            MinimumRotationSeparationDegrees = options.MinimumRotationSeparationDegrees;
            MinimumTranslationSeparation = options.MinimumTranslationSeparation;
            MaximumMatchedSampleCountDifference = options.MaximumMatchedSampleCountDifference;
            MaximumRmseDifference = options.MaximumRmseDifference;
            MaximumTranslationSeparation = options.MaximumTranslationSeparation;
        }

        public RigidSurfacePose ReferencePose { get; }
        public DeterministicSurfaceCoverageResult ReferenceCoverage { get; }
        public RigidSurfacePose AlternativePose { get; }
        public DeterministicSurfaceCoverageResult AlternativeCoverage { get; }
        public int EvaluatedCandidateCount { get; }
        public double RotationSeparationDegrees { get; }
        /// <summary>Separation of model origins in scene coordinates, in the input XYZ unit; NaN when no alternative exists.</summary>
        public double TranslationSeparation { get; }
        public bool IsAmbiguous { get; }
        public bool IsRefinedComparison { get; }
        /// <summary>Whether all coarse-support-admitted seeds were compared within the authored budget. This does not cover all possible final poses.</summary>
        public bool ComparisonComplete { get; }
        /// <summary>Number of seed refinement attempts, including valid seeds retained without an accepted update.</summary>
        public int RefinedCandidateCount { get; }
        public int? MaximumRefinedCandidateCount { get; }
        public double MinimumRotationSeparationDegrees { get; }
        public double? MinimumTranslationSeparation { get; }
        public double MaximumTranslationSeparation { get; }
        public int MaximumMatchedSampleCountDifference { get; }
        public double MaximumRmseDifference { get; }

        internal SurfacePoseAmbiguityEvidence MapSceneOrders(IReadOnlyList<int> sourceOrders, int sceneCount, CancellationToken cancellationToken)
        {
            return new SurfacePoseAmbiguityEvidence(ReferencePose, MapCoverage(ReferenceCoverage, sourceOrders, sceneCount, cancellationToken),
                AlternativePose, MapCoverage(AlternativeCoverage, sourceOrders, sceneCount, cancellationToken), EvaluatedCandidateCount,
                RotationSeparationDegrees, IsAmbiguous, new SurfacePoseAmbiguityOptions
                {
                    MinimumRotationSeparationDegrees = MinimumRotationSeparationDegrees,
                    MinimumTranslationSeparation = MinimumTranslationSeparation,
                    MaximumMatchedSampleCountDifference = MaximumMatchedSampleCountDifference,
                    MaximumTranslationSeparation = MaximumTranslationSeparation,
                    MaximumRmseDifference = MaximumRmseDifference,
                    MaximumRefinedCandidateCount = MaximumRefinedCandidateCount
                }, TranslationSeparation, IsRefinedComparison, ComparisonComplete, RefinedCandidateCount);
        }

        private static DeterministicSurfaceCoverageResult MapCoverage(DeterministicSurfaceCoverageResult coverage,
            IReadOnlyList<int> sourceOrders, int sceneCount, CancellationToken cancellationToken)
        {
            if (coverage == null) return null;
            SurfaceCoverageMatch[] mapped = new SurfaceCoverageMatch[coverage.Matches.Count];
            for (int index = 0; index < mapped.Length; index++)
            {
                if ((index & 1023) == 0) cancellationToken.ThrowIfCancellationRequested();
                SurfaceCoverageMatch match = coverage.Matches[index];
                mapped[index] = new SurfaceCoverageMatch(match.ModelSampleOrder, sourceOrders[match.SceneSampleOrder], match.Distance);
            }

            cancellationToken.ThrowIfCancellationRequested();
            return DeterministicSurfaceCoverageResult.Completed(coverage.ModelSampleCount, sceneCount,
                coverage.MatchedModelSampleCount, coverage.CoverageRatio, coverage.HasInlierRmse, coverage.InlierRmse,
                coverage.MaximumCorrespondenceDistance, mapped);
        }
    }

    internal static class SurfaceMatchingContractValidation
    {
        public static void ValidateSamples(
            IReadOnlyList<SurfaceMatchSample> samples,
            string label,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (samples == null || samples.Count == 0)
            {
                throw new ArgumentException(
                    label + " requires at least one ordered sample.");
            }

            for (int index = 0; index < samples.Count; index++)
            {
                if ((index & 1023) == 0) cancellationToken.ThrowIfCancellationRequested();
                SurfaceMatchSample sample = samples[index];
                if (sample == null
                    || sample.Order != index
                    || sample.Position == null
                    || !sample.Position.IsFinite)
                {
                    throw new ArgumentException(
                        label + " requires contiguous zero-based orders and finite XYZ positions.");
                }
            }

            cancellationToken.ThrowIfCancellationRequested();
        }

        public static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }
    }
}
