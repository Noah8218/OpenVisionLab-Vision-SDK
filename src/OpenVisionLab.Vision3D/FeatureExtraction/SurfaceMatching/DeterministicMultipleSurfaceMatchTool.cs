using System;
using System.Collections.Generic;
using System.Threading;

namespace OpenVisionLab.Vision3D.FeatureExtraction
{
    /// <summary>
    /// Finds a bounded collection of rigid surface instances. Each rotation,
    /// model anchor, and available scene anchor defines one translation
    /// candidate. The best candidate claims its scene samples before the next
    /// instance is searched, so returned instances never share evidence.
    /// </summary>
    public sealed class DeterministicMultipleSurfaceMatchTool
    {
        public const int AbsoluteMaximumMatchCount = 1000;
        public const int AbsoluteMaximumExpandedCandidateCount = 10000000;

        private readonly DeterministicRigidSurfacePoseSearchTool poseSearchTool = new DeterministicRigidSurfacePoseSearchTool();

        public DeterministicMultipleSurfaceMatchResult Execute(
            IReadOnlyList<SurfaceMatchSample> modelSamples,
            IReadOnlyList<SurfaceMatchSample> sceneSamples,
            DeterministicMultipleSurfaceMatchOptions options,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            try
            {
                SurfaceMatchingContractValidation.ValidateSamples(
                    modelSamples,
                    "Multiple surface match model input",
                    cancellationToken);
                SurfaceMatchingContractValidation.ValidateSamples(
                    sceneSamples,
                    "Multiple surface match scene input",
                    cancellationToken);
                ValidateOptions(options, modelSamples.Count, sceneSamples.Count);

                DeterministicRigidSurfacePoseSearchOptions poseOptions =
                    options.PoseSearchOptions;
                int rotationXCount =
                    DeterministicRigidSurfacePoseSearchTool.AxisCandidateCount(
                        poseOptions.MinimumRotationXDegrees,
                        poseOptions.MaximumRotationXDegrees,
                        poseOptions.RotationStepXDegrees,
                        "X");
                int rotationYCount =
                    DeterministicRigidSurfacePoseSearchTool.AxisCandidateCount(
                        poseOptions.MinimumRotationYDegrees,
                        poseOptions.MaximumRotationYDegrees,
                        poseOptions.RotationStepYDegrees,
                        "Y");
                int rotationZCount =
                    DeterministicRigidSurfacePoseSearchTool.AxisCandidateCount(
                        poseOptions.MinimumRotationZDegrees,
                        poseOptions.MaximumRotationZDegrees,
                        poseOptions.RotationStepZDegrees,
                        "Z");
                long rotationCandidateCount = checked(
                    (long)rotationXCount
                    * rotationYCount
                    * rotationZCount);
                if (rotationCandidateCount > poseOptions.MaximumCandidateCount)
                {
                    throw new ArgumentException(
                        "Multiple surface match rotation candidate count "
                        + rotationCandidateCount
                        + " exceeds the declared pose maximum "
                        + poseOptions.MaximumCandidateCount
                        + ".");
                }

                int modelAnchorLimit = poseOptions.ProposalOptions == null ? modelSamples.Count
                    : Math.Min(modelSamples.Count, poseOptions.ProposalOptions.MaximumModelAnchorCount);
                int sceneAnchorLimit = poseOptions.ProposalOptions == null ? sceneSamples.Count
                    : Math.Min(sceneSamples.Count, poseOptions.ProposalOptions.MaximumSceneAnchorCount);
                long expandedCandidateCount = checked(
                    rotationCandidateCount * modelAnchorLimit * sceneAnchorLimit * options.MaximumMatchCount);
                if (expandedCandidateCount
                    > options.MaximumExpandedCandidateCount)
                {
                    throw new ArgumentException(
                        "Multiple surface match expanded candidate count "
                        + expandedCandidateCount
                        + " exceeds the declared maximum "
                        + options.MaximumExpandedCandidateCount
                        + ".");
                }

                IReadOnlyList<SurfaceMatchSample> modelAnchors = poseOptions.ProposalOptions == null ? null
                    : DeterministicRigidSurfacePoseSearchTool.SelectProposalSamples(
                        modelSamples, poseOptions.ProposalOptions.MaximumModelAnchorCount, cancellationToken);
                double[] rotationX =
                    DeterministicRigidSurfacePoseSearchTool.AxisCandidates(
                        poseOptions.MinimumRotationXDegrees,
                        poseOptions.RotationStepXDegrees,
                        rotationXCount,
                        cancellationToken);
                double[] rotationY =
                    DeterministicRigidSurfacePoseSearchTool.AxisCandidates(
                        poseOptions.MinimumRotationYDegrees,
                        poseOptions.RotationStepYDegrees,
                        rotationYCount,
                        cancellationToken);
                double[] rotationZ =
                    DeterministicRigidSurfacePoseSearchTool.AxisCandidates(
                        poseOptions.MinimumRotationZDegrees,
                        poseOptions.RotationStepZDegrees,
                        rotationZCount,
                        cancellationToken);
                bool[] claimedSceneSamples = new bool[sceneSamples.Count];
                List<DeterministicSurfaceMatchInstance> matches =
                    new List<DeterministicSurfaceMatchInstance>();
                int totalEvaluatedCandidateCount = 0;
                string stopReason = string.Empty;
                while (matches.Count < options.MaximumMatchCount)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    List<int> sourceOrders = new List<int>();
                    List<SurfaceMatchSample> available =
                        new List<SurfaceMatchSample>();
                    for (int sceneIndex = 0;
                         sceneIndex < sceneSamples.Count;
                         sceneIndex++)
                    {
                        if ((sceneIndex & 1023) == 0) cancellationToken.ThrowIfCancellationRequested();
                        if (claimedSceneSamples[sceneIndex])
                        {
                            continue;
                        }

                        sourceOrders.Add(sceneSamples[sceneIndex].Order);
                        available.Add(new SurfaceMatchSample(
                            available.Count,
                            sceneSamples[sceneIndex].Position));
                    }

                    cancellationToken.ThrowIfCancellationRequested();
                    IReadOnlyList<SurfaceMatchSample> admissibleScene = poseOptions.ProposalOptions == null ? null
                        : DeterministicRigidSurfacePoseSearchTool.SelectAdmissibleSceneSamples(modelSamples, available, poseOptions, cancellationToken);
                    IReadOnlyList<SurfaceMatchSample> sceneAnchors = poseOptions.ProposalOptions == null ? null
                        : DeterministicRigidSurfacePoseSearchTool.SelectProposalSamples(
                            admissibleScene, poseOptions.ProposalOptions.MaximumSceneAnchorCount, cancellationToken);
                    SurfacePoseProposalEvidence proposals = modelAnchors == null ? null
                        : new SurfacePoseProposalEvidence(modelSamples.Count, sceneSamples.Count, available.Count, admissibleScene.Count,
                            modelAnchors, sceneAnchors, sourceOrders);
                    SurfaceSceneIndex indexedScene = new SurfaceSceneIndex(available,
                        (long)modelSamples.Count * rotationCandidateCount * (modelAnchors?.Count ?? modelSamples.Count)
                        * (sceneAnchors?.Count ?? available.Count), cancellationToken);
                    Candidate best = null;
                    int iterationEvaluatedCandidateCount = 0;
                    int enumerationOrder = 0;
                    for (int xIndex = 0; xIndex < rotationX.Length; xIndex++)
                    {
                        for (int yIndex = 0; yIndex < rotationY.Length; yIndex++)
                        {
                            for (int zIndex = 0; zIndex < rotationZ.Length; zIndex++)
                            {
                                DeterministicRigidSurfacePoseSearchTool.Rotation3 rotation =
                                    DeterministicRigidSurfacePoseSearchTool.Rotation(
                                        rotationX[xIndex],
                                        rotationY[yIndex],
                                        rotationZ[zIndex]);
                                for (int modelOffset = 0;
                                     modelOffset < (modelAnchors?.Count ?? modelSamples.Count);
                                     modelOffset++)
                                {
                                    cancellationToken.ThrowIfCancellationRequested();
                                    int modelIndex = modelAnchors == null ? modelOffset : modelAnchors[modelOffset].Order;
                                    ThreeDPoint rotatedModelAnchor =
                                        rotation.Transform(
                                            modelSamples[modelIndex].Position);
                                    for (int sceneOffset = 0;
                                         sceneOffset < (sceneAnchors?.Count ?? sceneSamples.Count);
                                         sceneOffset++)
                                    {
                                        cancellationToken.ThrowIfCancellationRequested();
                                        int sceneIndex = sceneAnchors == null ? sceneOffset : sourceOrders[sceneAnchors[sceneOffset].Order];
                                        if (claimedSceneSamples[sceneIndex])
                                        {
                                            enumerationOrder++;
                                            continue;
                                        }

                                        iterationEvaluatedCandidateCount++;
                                        ThreeDPoint sceneAnchor =
                                            sceneSamples[sceneIndex].Position;
                                        ThreeDPoint translation = new ThreeDPoint(
                                            sceneAnchor.X - rotatedModelAnchor.X,
                                            sceneAnchor.Y - rotatedModelAnchor.Y,
                                            sceneAnchor.Z - rotatedModelAnchor.Z);
                                        if (!DeterministicRigidSurfacePoseSearchTool
                                                .InsideTranslationBounds(
                                                    translation,
                                                    poseOptions))
                                        {
                                            enumerationOrder++;
                                            continue;
                                        }

                                        RigidSurfacePose pose = new RigidSurfacePose(
                                            rotation.M11,
                                            rotation.M12,
                                            rotation.M13,
                                            rotation.M21,
                                            rotation.M22,
                                            rotation.M23,
                                            rotation.M31,
                                            rotation.M32,
                                            rotation.M33,
                                            translation.X,
                                            translation.Y,
                                            translation.Z);
                                        DeterministicSurfaceCoverageResult coverage =
                                            Coverage(
                                                modelSamples,
                                                sceneSamples,
                                                sourceOrders,
                                                available,
                                                indexedScene,
                                                pose,
                                                poseOptions.MaximumCorrespondenceDistance,
                                                cancellationToken);
                                        Candidate candidate = new Candidate(
                                            enumerationOrder,
                                            pose,
                                            coverage);
                                        if (candidate.IsBetterThan(best))
                                        {
                                            best = candidate;
                                        }

                                        enumerationOrder++;
                                    }
                                }
                            }
                        }
                    }

                    cancellationToken.ThrowIfCancellationRequested();
                    totalEvaluatedCandidateCount = checked(
                        totalEvaluatedCandidateCount
                        + iterationEvaluatedCandidateCount);
                    if (best == null
                        || best.Coverage.MatchedModelSampleCount
                            < poseOptions.MinimumMatchedSampleCount)
                    {
                        stopReason = best == null
                            ? "No available scene sample produced a translation inside the declared bounds."
                            : "Best remaining candidate matched "
                                + best.Coverage.MatchedModelSampleCount
                                + " model samples, below the required "
                                + poseOptions.MinimumMatchedSampleCount
                                + ".";
                        break;
                    }

                    SurfacePoseAmbiguityEvidence ambiguity = poseOptions.AmbiguityOptions == null ? null
                        : poseSearchTool.EvaluateAmbiguity(
                            modelSamples, indexedScene, poseOptions, best.Pose,
                            DeterministicSurfaceCoverageTool.ExecuteValidated(modelSamples, indexedScene, best.Pose, poseOptions.MaximumCorrespondenceDistance, cancellationToken),
                            false, true, cancellationToken, modelAnchors, sceneAnchors).MapSceneOrders(sourceOrders, sceneSamples.Count, cancellationToken);

                    SurfacePoseAmbiguityEvidence finalAmbiguity = null;
                    SurfacePoseRefinementResult refinement = null;
                    if (poseOptions.RefinementOptions != null)
                    {
                        refinement = new DeterministicSurfacePoseRefinementTool().Execute(
                            modelSamples, available, best.Pose, poseOptions, cancellationToken);
                        if (!refinement.Success) throw new InvalidOperationException(refinement.Message);
                        if (poseOptions.AmbiguityOptions?.MaximumRefinedCandidateCount != null)
                            finalAmbiguity = poseSearchTool.EvaluateRefinedAmbiguity(modelSamples, indexedScene, poseOptions,
                                refinement.Pose, refinement.Coverage, false, true, cancellationToken, modelAnchors, sceneAnchors)
                                .MapSceneOrders(sourceOrders, sceneSamples.Count, cancellationToken);
                        refinement = refinement.MapSceneOrders(sourceOrders, sceneSamples.Count, cancellationToken);
                    }

                    RigidSurfacePose matchedPose = refinement?.Pose ?? best.Pose;
                    DeterministicSurfaceCoverageResult matchedCoverage = refinement?.Coverage ?? best.Coverage;
                    for (int matchIndex = 0; matchIndex < matchedCoverage.Matches.Count; matchIndex++)
                    {
                        if ((matchIndex & 1023) == 0) cancellationToken.ThrowIfCancellationRequested();
                        claimedSceneSamples[matchedCoverage.Matches[matchIndex].SceneSampleOrder] = true;
                    }

                    cancellationToken.ThrowIfCancellationRequested();

                    matches.Add(new DeterministicSurfaceMatchInstance(
                        matches.Count,
                        iterationEvaluatedCandidateCount,
                        matchedPose,
                        matchedCoverage,
                        refinement,
                        ambiguity,
                        proposals,
                        finalAmbiguity));
                }

                cancellationToken.ThrowIfCancellationRequested();
                return DeterministicMultipleSurfaceMatchResult.Completed(
                    totalEvaluatedCandidateCount,
                    matches,
                    stopReason);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                return DeterministicMultipleSurfaceMatchResult.Failed(
                    "Deterministic multiple surface match failed: "
                    + exception.Message);
            }
        }

        private static DeterministicSurfaceCoverageResult Coverage(
            IReadOnlyList<SurfaceMatchSample> modelSamples,
            IReadOnlyList<SurfaceMatchSample> sceneSamples,
            IReadOnlyList<int> sourceOrders,
            IReadOnlyList<SurfaceMatchSample> available,
            SurfaceSceneIndex sceneIndex,
            RigidSurfacePose pose,
            double maximumCorrespondenceDistance,
            CancellationToken cancellationToken)
        {
            if (available.Count == 0)
            {
                return DeterministicSurfaceCoverageResult.Completed(
                    modelSamples.Count,
                    sceneSamples.Count,
                    0,
                    0.0,
                    false,
                    double.NaN,
                    maximumCorrespondenceDistance,
                    Array.Empty<SurfaceCoverageMatch>());
            }

            DeterministicSurfaceCoverageResult result = DeterministicSurfaceCoverageTool.ExecuteValidated(
                modelSamples,
                sceneIndex,
                pose,
                maximumCorrespondenceDistance,
                cancellationToken);
            if (!result.Success)
            {
                throw new InvalidOperationException(result.Message);
            }

            SurfaceCoverageMatch[] mapped = new SurfaceCoverageMatch[result.Matches.Count];
            for (int index = 0; index < mapped.Length; index++)
            {
                if ((index & 1023) == 0) cancellationToken.ThrowIfCancellationRequested();
                SurfaceCoverageMatch match = result.Matches[index];
                mapped[index] = new SurfaceCoverageMatch(
                    match.ModelSampleOrder, sourceOrders[match.SceneSampleOrder], match.Distance);
            }

            cancellationToken.ThrowIfCancellationRequested();
            return DeterministicSurfaceCoverageResult.Completed(
                modelSamples.Count,
                sceneSamples.Count,
                result.MatchedModelSampleCount,
                result.CoverageRatio,
                result.HasInlierRmse,
                result.InlierRmse,
                result.MaximumCorrespondenceDistance,
                mapped);
        }

        private static void ValidateOptions(
            DeterministicMultipleSurfaceMatchOptions options,
            int modelSampleCount,
            int sceneSampleCount)
        {
            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            if (options.PoseSearchOptions == null)
            {
                throw new ArgumentException(
                    "Multiple surface match requires pose search options.");
            }

            if (options.MaximumMatchCount <= 0
                || options.MaximumMatchCount > AbsoluteMaximumMatchCount)
            {
                throw new ArgumentException(
                    "Maximum match count must be from 1 through "
                    + AbsoluteMaximumMatchCount
                    + ".");
            }

            if (options.MaximumExpandedCandidateCount <= 0
                || options.MaximumExpandedCandidateCount
                    > AbsoluteMaximumExpandedCandidateCount)
            {
                throw new ArgumentException(
                    "Maximum expanded candidate count must be from 1 through "
                    + AbsoluteMaximumExpandedCandidateCount
                    + ".");
            }

            DeterministicRigidSurfacePoseSearchTool.ValidateOptions(
                options.PoseSearchOptions,
                modelSampleCount,
                sceneSampleCount);
        }

        private sealed class Candidate
        {
            public Candidate(
                int enumerationOrder,
                RigidSurfacePose pose,
                DeterministicSurfaceCoverageResult coverage)
            {
                EnumerationOrder = enumerationOrder;
                Pose = pose;
                Coverage = coverage;
            }

            public int EnumerationOrder { get; }

            public RigidSurfacePose Pose { get; }

            public DeterministicSurfaceCoverageResult Coverage { get; }

            public bool IsBetterThan(Candidate current)
            {
                if (current == null)
                {
                    return true;
                }

                if (Coverage.MatchedModelSampleCount
                    != current.Coverage.MatchedModelSampleCount)
                {
                    return Coverage.MatchedModelSampleCount
                        > current.Coverage.MatchedModelSampleCount;
                }

                double candidateRmse = Coverage.HasInlierRmse
                    ? Coverage.InlierRmse
                    : double.PositiveInfinity;
                double currentRmse = current.Coverage.HasInlierRmse
                    ? current.Coverage.InlierRmse
                    : double.PositiveInfinity;
                if (candidateRmse != currentRmse)
                {
                    return candidateRmse < currentRmse;
                }

                return EnumerationOrder < current.EnumerationOrder;
            }
        }
    }
}
