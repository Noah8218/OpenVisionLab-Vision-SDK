using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;

namespace OpenVisionLab.Vision3D.FeatureExtraction
{
    /// <summary>Opt-in local point-to-point refinement; length thresholds use the input XYZ unit.</summary>
    public sealed class SurfacePoseRefinementOptions
    {
        public int MaximumIterations { get; set; } = 10;

        /// <summary>A trial must reduce the selected objective (full-coverage RMSE, or fixed-count trimmed RMSE when enabled) by strictly more than this finite nonnegative amount.</summary>
        public double MinimumRmseImprovement { get; set; }

        /// <summary>Opt-in farthest-point selection of at most 64 distinct model positions when support exceeds 64. Stable first-order seed; not permutation-invariant or outlier rejection.</summary>
        public bool UseSpatiallyBalancedFit { get; set; }

        /// <summary>Null preserves full-coverage fitting and acceptance. A finite fraction in (0.5,1) retains the closest fixed-count residuals for spatial fitting and the objective; full coverage and support are preserved, but raw full-coverage RMSE may increase. Not object identification or a guarantee against outliers.</summary>
        public double? RetainedCorrespondenceFraction { get; set; }

        /// <summary>Optional independent trimmed-fit floor, at least 4 and no greater than model count. Requires a retained fraction. Null preserves the MinimumMatchedSampleCount floor; full-coverage support acceptance never changes.</summary>
        public int? MinimumRetainedCorrespondenceCount { get; set; }
    }

    public enum SurfacePoseRefinementStopReason
    {
        ExecutionFailed = 0,
        InsufficientCorrespondences = 1,
        AlignmentRejected = 2,
        BoundsRejected = 3,
        NoImprovement = 4,
        IterationLimit = 5
    }

    /// <summary>
    /// Immutable local refinement evidence. Success means the execution was valid,
    /// including a valid seed retained when a trial could not improve it.
    /// </summary>
    public sealed class SurfacePoseRefinementResult
    {
        internal SurfacePoseRefinementResult(
            bool success, string message, RigidSurfacePose initialPose,
            DeterministicSurfaceCoverageResult initialCoverage, RigidSurfacePose pose,
            DeterministicSurfaceCoverageResult coverage, int iterationCount, int acceptedIterationCount,
            SurfacePoseRefinementStopReason stopReason, IReadOnlyList<SurfaceCoverageMatch> lastFitCorrespondences,
            DeterministicRigidSurfacePoseSearchOptions options, int retainedCorrespondenceCount = 0,
            double? initialTrimmedRmse = null, double? trimmedRmse = null,
            IReadOnlyList<SurfaceCoverageMatch> lastRetainedCorrespondences = null, CancellationToken cancellationToken = default(CancellationToken))
        {
            Success = success;
            Message = message ?? string.Empty;
            InitialPose = initialPose;
            InitialCoverage = SnapshotCoverage(initialCoverage, cancellationToken);
            Pose = pose;
            Coverage = SnapshotCoverage(coverage, cancellationToken);
            IterationCount = iterationCount;
            AcceptedIterationCount = acceptedIterationCount;
            StopReason = stopReason;
            LastFitCorrespondences = SnapshotMatches(lastFitCorrespondences, cancellationToken);
            LastRetainedCorrespondences = SnapshotMatches(lastRetainedCorrespondences, cancellationToken);
            MaximumIterations = options?.RefinementOptions.MaximumIterations ?? 0;
            MinimumRmseImprovement = options?.RefinementOptions.MinimumRmseImprovement ?? double.NaN;
            UseSpatiallyBalancedFit = options?.RefinementOptions.UseSpatiallyBalancedFit ?? false;
            RetainedCorrespondenceFraction = options?.RefinementOptions.RetainedCorrespondenceFraction;
            MinimumRetainedCorrespondenceCount = options?.RefinementOptions.MinimumRetainedCorrespondenceCount;
            RetainedCorrespondenceCount = retainedCorrespondenceCount;
            InitialTrimmedRmse = initialTrimmedRmse;
            TrimmedRmse = trimmedRmse;
            MinimumMatchedSampleCount = options?.MinimumMatchedSampleCount ?? 0;
            RotationBoundsDegrees = Array.AsReadOnly(options == null ? Array.Empty<double>() : new[]
            {
                options.MinimumRotationXDegrees, options.MaximumRotationXDegrees,
                options.MinimumRotationYDegrees, options.MaximumRotationYDegrees,
                options.MinimumRotationZDegrees, options.MaximumRotationZDegrees
            });
            TranslationBounds = Array.AsReadOnly(options == null ? Array.Empty<double>() : new[]
            {
                options.MinimumTranslationX, options.MaximumTranslationX,
                options.MinimumTranslationY, options.MaximumTranslationY,
                options.MinimumTranslationZ, options.MaximumTranslationZ
            });
            cancellationToken.ThrowIfCancellationRequested();
        }

        private SurfacePoseRefinementResult(SurfacePoseRefinementResult source, IReadOnlyList<int> sceneOrders, int sceneSampleCount, CancellationToken cancellationToken)
        {
            Success = source.Success;
            Message = source.Message;
            InitialPose = source.InitialPose;
            InitialCoverage = SnapshotCoverage(source.InitialCoverage, cancellationToken, sceneOrders, sceneSampleCount);
            Pose = source.Pose;
            Coverage = SnapshotCoverage(source.Coverage, cancellationToken, sceneOrders, sceneSampleCount);
            IterationCount = source.IterationCount;
            AcceptedIterationCount = source.AcceptedIterationCount;
            StopReason = source.StopReason;
            LastFitCorrespondences = SnapshotMatches(source.LastFitCorrespondences, cancellationToken, sceneOrders);
            LastRetainedCorrespondences = SnapshotMatches(source.LastRetainedCorrespondences, cancellationToken, sceneOrders);
            MaximumIterations = source.MaximumIterations;
            MinimumRmseImprovement = source.MinimumRmseImprovement;
            UseSpatiallyBalancedFit = source.UseSpatiallyBalancedFit;
            RetainedCorrespondenceFraction = source.RetainedCorrespondenceFraction;
            MinimumRetainedCorrespondenceCount = source.MinimumRetainedCorrespondenceCount;
            RetainedCorrespondenceCount = source.RetainedCorrespondenceCount;
            InitialTrimmedRmse = source.InitialTrimmedRmse;
            TrimmedRmse = source.TrimmedRmse;
            MinimumMatchedSampleCount = source.MinimumMatchedSampleCount;
            RotationBoundsDegrees = source.RotationBoundsDegrees;
            TranslationBounds = source.TranslationBounds;
            cancellationToken.ThrowIfCancellationRequested();
        }

        public bool Success { get; }
        public string Message { get; }
        public RigidSurfacePose InitialPose { get; }
        public DeterministicSurfaceCoverageResult InitialCoverage { get; }
        public RigidSurfacePose Pose { get; }
        public DeterministicSurfaceCoverageResult Coverage { get; }
        /// <summary>Number of attempted paired fits, separate from coarse grid candidates.</summary>
        public int IterationCount { get; }
        public int AcceptedIterationCount { get; }
        public bool Improved => AcceptedIterationCount > 0;
        public SurfacePoseRefinementStopReason StopReason { get; }
        /// <summary>Seed-coverage pairs used for the last attempted fit, which may have been rejected.</summary>
        public IReadOnlyList<SurfaceCoverageMatch> LastFitCorrespondences { get; }
        public int MaximumIterations { get; }
        public double MinimumRmseImprovement { get; }
        public bool UseSpatiallyBalancedFit { get; }
        public double? RetainedCorrespondenceFraction { get; }

        public int? MinimumRetainedCorrespondenceCount { get; }
        /// <summary>Fixed objective support: max(4, declared minimum support, ceil(fraction * initial matched count)); zero when trimming is disabled.</summary>
        public int RetainedCorrespondenceCount { get; }
        /// <summary>Null when disabled or initial support is insufficient for the fixed retained count.</summary>
        public double? InitialTrimmedRmse { get; }
        /// <summary>Fixed-count trimmed RMSE at the last accepted pose, separate from the raw full-coverage RMSE.</summary>
        public double? TrimmedRmse { get; }
        /// <summary>Closest fixed-count seed-coverage pairs considered for the last attempted fit, which may have been rejected. Empty when trimming is disabled or no fit was attempted.</summary>
        public IReadOnlyList<SurfaceCoverageMatch> LastRetainedCorrespondences { get; }
        public int MinimumMatchedSampleCount { get; }
        /// <summary>Snapshot ordered X min/max, Y min/max, Z min/max for Rz * Ry * Rx.</summary>
        public IReadOnlyList<double> RotationBoundsDegrees { get; }
        /// <summary>Snapshot ordered X min/max, Y min/max, Z min/max in the input XYZ unit.</summary>
        public IReadOnlyList<double> TranslationBounds { get; }

        internal SurfacePoseRefinementResult MapSceneOrders(IReadOnlyList<int> sceneOrders, int sceneSampleCount, CancellationToken cancellationToken)
        {
            return new SurfacePoseRefinementResult(this, sceneOrders, sceneSampleCount, cancellationToken);
        }

        private static DeterministicSurfaceCoverageResult SnapshotCoverage(
            DeterministicSurfaceCoverageResult coverage, CancellationToken cancellationToken, IReadOnlyList<int> sceneOrders = null, int sceneSampleCount = 0)
        {
            if (coverage == null) return null;
            return DeterministicSurfaceCoverageResult.Completed(
                coverage.ModelSampleCount, sceneOrders == null ? coverage.SceneSampleCount : sceneSampleCount,
                coverage.MatchedModelSampleCount, coverage.CoverageRatio, coverage.HasInlierRmse,
                coverage.InlierRmse, coverage.MaximumCorrespondenceDistance, SnapshotMatches(coverage.Matches, cancellationToken, sceneOrders));
        }

        private static ReadOnlyCollection<SurfaceCoverageMatch> SnapshotMatches(
            IReadOnlyList<SurfaceCoverageMatch> matches, CancellationToken cancellationToken, IReadOnlyList<int> sceneOrders = null)
        {
            SurfaceCoverageMatch[] copy = new SurfaceCoverageMatch[matches?.Count ?? 0];
            for (int index = 0; index < copy.Length; index++)
            {
                if ((index & 1023) == 0) cancellationToken.ThrowIfCancellationRequested();
                SurfaceCoverageMatch match = matches[index];
                copy[index] = sceneOrders == null ? match : new SurfaceCoverageMatch(
                    match.ModelSampleOrder, sceneOrders[match.SceneSampleOrder], match.Distance);
            }

            cancellationToken.ThrowIfCancellationRequested();
            return Array.AsReadOnly(copy);
        }
    }

    /// <summary>
    /// Bounded local proper-rigid refinement using the existing greedy coverage
    /// and paired least-squares solver. It preserves support and strictly reduces
    /// the selected objective; opt-in trimming can increase raw full-coverage RMSE.
    /// It does not identify symmetry or guarantee global convergence.
    /// Inputs and mutable options must remain stable until Execute returns.
    /// </summary>
    public sealed class DeterministicSurfacePoseRefinementTool
    {
        public const int AbsoluteMaximumIterations = 20;
        public const int MaximumFitPairCount = 64;
        private const double AngularRoundoffDegrees = 1e-9;

        private readonly ConstrainedBestFitRigidAlignmentTool alignmentTool = new ConstrainedBestFitRigidAlignmentTool();

        public SurfacePoseRefinementResult Execute(
            IReadOnlyList<SurfaceMatchSample> modelSamples, IReadOnlyList<SurfaceMatchSample> sceneSamples,
            RigidSurfacePose initialPose, DeterministicRigidSurfacePoseSearchOptions options,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                SurfaceMatchingContractValidation.ValidateSamples(modelSamples, "Surface refinement model input", cancellationToken);
                SurfaceMatchingContractValidation.ValidateSamples(sceneSamples, "Surface refinement scene input", cancellationToken);
                DeterministicRigidSurfacePoseSearchTool.ValidateOptions(options, modelSamples.Count, sceneSamples.Count);
                if (options.RefinementOptions == null) throw new ArgumentException("Surface refinement requires explicit refinement options.");
                ValidateRotationDomain(options);
                if (initialPose == null || !initialPose.IsRigid(1e-9)
                    || !TryBoundedRotation(initialPose.M11, initialPose.M12, initialPose.M13,
                        initialPose.M21, initialPose.M31, initialPose.M32, initialPose.M33, options, out _)
                    || !DeterministicRigidSurfacePoseSearchTool.InsideTranslationBounds(
                        new ThreeDPoint(initialPose.TranslationX, initialPose.TranslationY, initialPose.TranslationZ), options))
                {
                    throw new ArgumentException("Surface refinement requires a proper-rigid seed inside the declared bounds.");
                }

                SurfaceSceneIndex sceneIndex = new SurfaceSceneIndex(sceneSamples, (long)modelSamples.Count * (options.RefinementOptions.MaximumIterations + 1), cancellationToken);
                DeterministicSurfaceCoverageResult initialCoverage = Coverage(modelSamples, sceneIndex, initialPose, options, cancellationToken);
                RigidSurfacePose pose = initialPose;
                DeterministicSurfaceCoverageResult coverage = initialCoverage;
                IReadOnlyList<SurfaceCoverageMatch> lastFit = Array.Empty<SurfaceCoverageMatch>();
                IReadOnlyList<SurfaceCoverageMatch> lastRetained = Array.Empty<SurfaceCoverageMatch>();
                bool trimmed = options.RefinementOptions.RetainedCorrespondenceFraction.HasValue;
                int retainedCount = trimmed ? Math.Max(Math.Max(4, options.RefinementOptions.MinimumRetainedCorrespondenceCount ?? options.MinimumMatchedSampleCount),
                    (int)Math.Ceiling(options.RefinementOptions.RetainedCorrespondenceFraction.Value * initialCoverage.Matches.Count)) : 0;
                SurfaceCoverageMatch[] retained = trimmed && initialCoverage.Matches.Count >= retainedCount
                    ? SelectRetained(initialCoverage.Matches, retainedCount, cancellationToken) : null;
                double? initialTrimmedRmse = retained == null ? (double?)null : TrimmedRmse(retained, cancellationToken);
                double? trimmedRmse = initialTrimmedRmse;
                int iterations = 0;
                int accepted = 0;
                SurfacePoseRefinementStopReason stopReason = SurfacePoseRefinementStopReason.IterationLimit;
                string message = "The declared refinement iteration limit was reached; the last accepted pose was retained.";
                for (int iteration = 0; iteration < options.RefinementOptions.MaximumIterations; iteration++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (coverage.MatchedModelSampleCount < Math.Max(Math.Max(4, options.MinimumMatchedSampleCount), retainedCount))
                    {
                        stopReason = SurfacePoseRefinementStopReason.InsufficientCorrespondences;
                        message = "Refinement requires at least four correspondences and the declared minimum support; the seed was retained.";
                        break;
                    }

                    IReadOnlyList<SurfaceCoverageMatch> fitCandidates = trimmed ? retained : coverage.Matches;
                    if (trimmed) lastRetained = retained;
                    // ponytail: bounded 64-pair fitting and O(64*n) spatial selection; residual trimming remains a local policy, not global capture or object identification.
                    int pairCount = Math.Min(MaximumFitPairCount, fitCandidates.Count);
                    List<KeyValuePair<int, double>> selected = null;
                    if ((trimmed || options.RefinementOptions.UseSpatiallyBalancedFit) && fitCandidates.Count > MaximumFitPairCount)
                    {
                        ThreeDPoint[] positions = new ThreeDPoint[fitCandidates.Count];
                        for (int index = 0; index < positions.Length; index++)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            positions[index] = modelSamples[fitCandidates[index].ModelSampleOrder].Position;
                        }
                        selected = DeterministicModelKeyPointExtractionTool.SelectPositionIndices(positions, MaximumFitPairCount, 0D, cancellationToken);
                        // Keep the established paired-fit failure semantics when fewer than four distinct positions are available.
                        if (selected.Count < 4) selected = null;
                        else pairCount = selected.Count;
                    }
                    SurfaceCoverageMatch[] fitMatches = new SurfaceCoverageMatch[pairCount];
                    ConstrainedBestFitRigidCorrespondence[] pairs = new ConstrainedBestFitRigidCorrespondence[pairCount];
                    for (int index = 0; index < pairCount; index++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        int matchIndex = selected == null ? (int)((long)index * (fitCandidates.Count - 1) / (pairCount - 1)) : selected[index].Key;
                        SurfaceCoverageMatch match = fitCandidates[matchIndex];
                        fitMatches[index] = match;
                        pairs[index] = new ConstrainedBestFitRigidCorrespondence(
                            modelSamples[match.ModelSampleOrder].Position, sceneSamples[match.SceneSampleOrder].Position);
                    }

                    lastFit = fitMatches;
                    iterations++;
                    ConstrainedBestFitRigidAlignmentResult fit = alignmentTool.Execute(
                        pairs, new ConstrainedBestFitRigidAlignmentOptions(), cancellationToken);
                    if (!fit.Success)
                    {
                        stopReason = SurfacePoseRefinementStopReason.AlignmentRejected;
                        message = fit.Message;
                        break;
                    }

                    if (!TryCreateBoundedPose(fit, options, out RigidSurfacePose trialPose))
                    {
                        stopReason = SurfacePoseRefinementStopReason.BoundsRejected;
                        message = "The fitted proper-rigid pose is outside the declared rotation or translation bounds; the last accepted pose was retained.";
                        break;
                    }

                    DeterministicSurfaceCoverageResult trialCoverage = Coverage(modelSamples, sceneIndex, trialPose, options, cancellationToken);
                    SurfaceCoverageMatch[] trialRetained = trimmed && trialCoverage.Matches.Count >= retainedCount
                        ? SelectRetained(trialCoverage.Matches, retainedCount, cancellationToken) : null;
                    double? trialTrimmedRmse = trialRetained == null ? (double?)null : TrimmedRmse(trialRetained, cancellationToken);
                    double previousObjective = trimmed ? trimmedRmse.GetValueOrDefault(double.NaN) : coverage.InlierRmse;
                    double trialObjective = trimmed ? trialTrimmedRmse.GetValueOrDefault(double.NaN) : trialCoverage.InlierRmse;
                    if (trialCoverage.MatchedModelSampleCount < coverage.MatchedModelSampleCount
                        || !trialCoverage.HasInlierRmse
                        || !(previousObjective - trialObjective > options.RefinementOptions.MinimumRmseImprovement))
                    {
                        stopReason = SurfacePoseRefinementStopReason.NoImprovement;
                        message = trimmed
                            ? "The trial did not preserve support and strictly improve fixed-count trimmed RMSE by the declared amount; the last accepted pose was retained."
                            : "The trial did not preserve support and strictly improve full-coverage RMSE by the declared amount; the last accepted pose was retained.";
                        break;
                    }

                    pose = trialPose;
                    coverage = trialCoverage;
                    retained = trialRetained;
                    trimmedRmse = trialTrimmedRmse;
                    accepted++;
                }

                return new SurfacePoseRefinementResult(
                    true, message, initialPose, initialCoverage, pose, coverage,
                    iterations, accepted, stopReason, lastFit, options,
                    retainedCount, initialTrimmedRmse, trimmedRmse, lastRetained, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                return new SurfacePoseRefinementResult(
                    false, "Deterministic surface pose refinement failed: " + exception.Message,
                    null, null, null, null, 0, 0, SurfacePoseRefinementStopReason.ExecutionFailed, null, null);
            }
        }

        internal static void ValidateRefinementOptions(SurfacePoseRefinementOptions options)
        {
            if (options.MaximumIterations < 1 || options.MaximumIterations > AbsoluteMaximumIterations
                || !SurfaceMatchingContractValidation.IsFinite(options.MinimumRmseImprovement) || options.MinimumRmseImprovement < 0.0)
            {
                throw new ArgumentException("Surface refinement requires 1 through 20 iterations and a finite nonnegative minimum RMSE improvement.");
            }
            if (options.MinimumRetainedCorrespondenceCount.HasValue
                && (!options.RetainedCorrespondenceFraction.HasValue || options.MinimumRetainedCorrespondenceCount.Value < 4))
                throw new ArgumentException("An independent retained-fit minimum requires trimming and at least four correspondences.");
            if (options.RetainedCorrespondenceFraction.HasValue
                && (!SurfaceMatchingContractValidation.IsFinite(options.RetainedCorrespondenceFraction.Value)
                    || options.RetainedCorrespondenceFraction.Value <= 0.5 || options.RetainedCorrespondenceFraction.Value >= 1.0))
            {
                throw new ArgumentException("The retained correspondence fraction must be finite and strictly between 0.5 and 1, or null to preserve full-coverage refinement.");
            }
        }

        private static SurfaceCoverageMatch[] SelectRetained(IReadOnlyList<SurfaceCoverageMatch> matches, int count, CancellationToken cancellationToken)
        {
            SurfaceCoverageMatch[] ranked = new SurfaceCoverageMatch[matches.Count];
            for (int index = 0; index < ranked.Length; index++)
            {
                if ((index & 1023) == 0) cancellationToken.ThrowIfCancellationRequested();
                ranked[index] = matches[index];
            }
            int comparisons = 0;
            try
            {
                Array.Sort(ranked, (first, second) =>
                {
                    if ((comparisons++ & 1023) == 0) cancellationToken.ThrowIfCancellationRequested();
                    int order = first.Distance.CompareTo(second.Distance);
                    if (order == 0) order = first.ModelSampleOrder.CompareTo(second.ModelSampleOrder);
                    return order == 0 ? first.SceneSampleOrder.CompareTo(second.SceneSampleOrder) : order;
                });
                SurfaceCoverageMatch[] retained = new SurfaceCoverageMatch[count];
                for (int index = 0; index < count; index++)
                {
                    if ((index & 1023) == 0) cancellationToken.ThrowIfCancellationRequested();
                    retained[index] = ranked[index];
                }
                Array.Sort(retained, (first, second) =>
                {
                    if ((comparisons++ & 1023) == 0) cancellationToken.ThrowIfCancellationRequested();
                    int order = first.ModelSampleOrder.CompareTo(second.ModelSampleOrder);
                    return order == 0 ? first.SceneSampleOrder.CompareTo(second.SceneSampleOrder) : order;
                });
                cancellationToken.ThrowIfCancellationRequested();
                return retained;
            }
            catch (InvalidOperationException exception) when (exception.InnerException is OperationCanceledException)
            {
                cancellationToken.ThrowIfCancellationRequested();
                throw;
            }
        }

        private static double TrimmedRmse(IReadOnlyList<SurfaceCoverageMatch> retained, CancellationToken cancellationToken)
        {
            double scale = 0D;
            for (int index = 0; index < retained.Count; index++)
            {
                if ((index & 1023) == 0) cancellationToken.ThrowIfCancellationRequested();
                scale = Math.Max(scale, retained[index].Distance);
            }
            double sum = 0D;
            if (scale > 0D)
            {
                for (int index = 0; index < retained.Count; index++)
                {
                    if ((index & 1023) == 0) cancellationToken.ThrowIfCancellationRequested();
                    double normalized = retained[index].Distance / scale;
                    sum += normalized * normalized;
                }
            }
            cancellationToken.ThrowIfCancellationRequested();
            return scale == 0D ? 0D : scale * Math.Min(1D, Math.Sqrt(sum / retained.Count));
        }

        private static DeterministicSurfaceCoverageResult Coverage(
            IReadOnlyList<SurfaceMatchSample> modelSamples, SurfaceSceneIndex sceneIndex,
            RigidSurfacePose pose, DeterministicRigidSurfacePoseSearchOptions options, CancellationToken cancellationToken)
        {
            DeterministicSurfaceCoverageResult result = DeterministicSurfaceCoverageTool.ExecuteValidated(
                modelSamples, sceneIndex, pose, options.MaximumCorrespondenceDistance, cancellationToken);
            if (!result.Success) throw new InvalidOperationException(result.Message);
            return result;
        }

        private static void ValidateRotationDomain(DeterministicRigidSurfacePoseSearchOptions options)
        {
            long count = (long)DeterministicRigidSurfacePoseSearchTool.AxisCandidateCount(
                options.MinimumRotationXDegrees, options.MaximumRotationXDegrees, options.RotationStepXDegrees, "X")
                * DeterministicRigidSurfacePoseSearchTool.AxisCandidateCount(
                    options.MinimumRotationYDegrees, options.MaximumRotationYDegrees, options.RotationStepYDegrees, "Y")
                * DeterministicRigidSurfacePoseSearchTool.AxisCandidateCount(
                    options.MinimumRotationZDegrees, options.MaximumRotationZDegrees, options.RotationStepZDegrees, "Z");
            if (count > options.MaximumCandidateCount) throw new ArgumentException("The declared rotation domain exceeds the candidate budget.");
        }

        private static bool TryCreateBoundedPose(
            ConstrainedBestFitRigidAlignmentResult fit, DeterministicRigidSurfacePoseSearchOptions options, out RigidSurfacePose pose)
        {
            pose = null;
            ConstrainedBestFitRigidAlignmentPose matrix = fit.Pose;
            if (!TryBoundedRotation(matrix.M11, matrix.M12, matrix.M13, matrix.M21,
                matrix.M31, matrix.M32, matrix.M33, options, out DeterministicRigidSurfacePoseSearchTool.Rotation3 rotation)) return false;
            ThreeDPoint rotatedCentroid = rotation.Transform(fit.SourceCentroid);
            ThreeDPoint translation = new ThreeDPoint(
                fit.ReferenceCentroid.X - rotatedCentroid.X,
                fit.ReferenceCentroid.Y - rotatedCentroid.Y,
                fit.ReferenceCentroid.Z - rotatedCentroid.Z);
            if (!translation.IsFinite || !DeterministicRigidSurfacePoseSearchTool.InsideTranslationBounds(translation, options)) return false;
            pose = new RigidSurfacePose(
                rotation.M11, rotation.M12, rotation.M13, rotation.M21, rotation.M22,
                rotation.M23, rotation.M31, rotation.M32, rotation.M33, translation.X, translation.Y, translation.Z);
            return pose.IsRigid(1e-9);
        }

        private static bool TryBoundedRotation(
            double m11, double m12, double m13, double m21, double m31, double m32, double m33,
            DeterministicRigidSurfacePoseSearchOptions options, out DeterministicRigidSurfacePoseSearchTool.Rotation3 rotation)
        {
            rotation = null;
            double horizontal = Math.Sqrt(m32 * m32 + m33 * m33);
            double y = Math.Atan2(-m31, horizontal) * 180.0 / Math.PI;
            if (horizontal <= 1e-12)
            {
                if (!ClampBound(ref y, options.MinimumRotationYDegrees, options.MaximumRotationYDegrees)) return false;
                double relation = Math.Atan2(m31 < 0.0 ? m12 : -m12, m31 < 0.0 ? m13 : -m13) * 180.0 / Math.PI;
                for (int turn = -1; turn <= 1; turn++)
                {
                    double d = relation + 360.0 * turn;
                    double zMin = Math.Max(options.MinimumRotationZDegrees,
                        m31 < 0.0 ? options.MinimumRotationXDegrees - d : d - options.MaximumRotationXDegrees);
                    double zMax = Math.Min(options.MaximumRotationZDegrees,
                        m31 < 0.0 ? options.MaximumRotationXDegrees - d : d - options.MinimumRotationXDegrees);
                    if (zMin > zMax + AngularRoundoffDegrees) continue;
                    double z = Math.Max(zMin, Math.Min(0.0, zMax));
                    double x = m31 < 0.0 ? d + z : d - z;
                    if (TryAngles(x, y, z, options, out rotation)) return true;
                }

                return false;
            }

            double regularX = Math.Atan2(m32, m33) * 180.0 / Math.PI;
            double regularZ = Math.Atan2(m21, m11) * 180.0 / Math.PI;
            if (TryAngles(regularX, y, regularZ, options, out rotation)) return true;
            double alternativeY = y >= 0.0 ? 180.0 - y : -180.0 - y;
            return TryAngles(regularX + 180.0, alternativeY, regularZ + 180.0, options, out rotation);
        }

        private static bool TryAngles(
            double x, double y, double z, DeterministicRigidSurfacePoseSearchOptions options,
            out DeterministicRigidSurfacePoseSearchTool.Rotation3 rotation)
        {
            rotation = null;
            if (!TryAxis(x, options.MinimumRotationXDegrees, options.MaximumRotationXDegrees, out x)
                || !TryAxis(y, options.MinimumRotationYDegrees, options.MaximumRotationYDegrees, out y)
                || !TryAxis(z, options.MinimumRotationZDegrees, options.MaximumRotationZDegrees, out z)) return false;
            rotation = DeterministicRigidSurfacePoseSearchTool.Rotation(x, y, z);
            return true;
        }

        private static bool TryAxis(double degrees, double minimum, double maximum, out double bounded)
        {
            for (int turn = -1; turn <= 1; turn++)
            {
                bounded = degrees + turn * 360.0;
                if (ClampBound(ref bounded, minimum, maximum)) return true;
            }

            bounded = double.NaN;
            return false;
        }

        private static bool ClampBound(ref double value, double minimum, double maximum)
        {
            if (value < minimum - AngularRoundoffDegrees || value > maximum + AngularRoundoffDegrees) return false;
            value = Math.Max(minimum, Math.Min(value, maximum));
            return true;
        }
    }
}
