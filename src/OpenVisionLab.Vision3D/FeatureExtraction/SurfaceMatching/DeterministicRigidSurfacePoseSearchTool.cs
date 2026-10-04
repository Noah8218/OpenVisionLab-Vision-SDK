using OpenVisionLab.Compute;
using System;
using System.Collections.Generic;
using System.Threading;

namespace OpenVisionLab.Vision3D.FeatureExtraction
{
    /// <summary>
    /// Deterministic bounded Euler-grid pose search. It derives translation
    /// from centroids with optional bounded sample-pair anchors and ranks candidates by matched model
    /// count, RMSE, then enumeration order. It has no product acceptance or
    /// source identity policy.
    /// </summary>
    public sealed class DeterministicRigidSurfacePoseSearchTool
    {
        public const int AbsoluteMaximumCandidateCount = 1000000;

        private readonly RigidPoseSymmetryEquivalenceTool rotationComparison = new RigidPoseSymmetryEquivalenceTool();
        private readonly VisionComputeSession computeSession;

        public DeterministicRigidSurfacePoseSearchTool() : this(null) { }

        /// <summary>Borrows a host-owned session without probing. The caller owns session disposal.</summary>
        public DeterministicRigidSurfacePoseSearchTool(VisionComputeSession computeSession)
        {
            this.computeSession = computeSession;
        }

        public DeterministicRigidSurfacePoseSearchResult Execute(
            IReadOnlyList<SurfaceMatchSample> modelSamples,
            IReadOnlyList<SurfaceMatchSample> sceneSamples,
            DeterministicRigidSurfacePoseSearchOptions options,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            ComputeExecutionEvidence computeEvidence = null;
            try
            {
                SurfaceMatchingContractValidation.ValidateSamples(
                    modelSamples,
                    "Rigid surface pose model input",
                    cancellationToken);
                SurfaceMatchingContractValidation.ValidateSamples(
                    sceneSamples,
                    "Rigid surface pose scene input",
                    cancellationToken);
                ValidateOptions(options, modelSamples.Count, sceneSamples.Count);

                int rotationXCount = AxisCandidateCount(
                    options.MinimumRotationXDegrees,
                    options.MaximumRotationXDegrees,
                    options.RotationStepXDegrees,
                    "X");
                int rotationYCount = AxisCandidateCount(
                    options.MinimumRotationYDegrees,
                    options.MaximumRotationYDegrees,
                    options.RotationStepYDegrees,
                    "Y");
                int rotationZCount = AxisCandidateCount(
                    options.MinimumRotationZDegrees,
                    options.MaximumRotationZDegrees,
                    options.RotationStepZDegrees,
                    "Z");
                long rotationCount = checked((long)rotationXCount * rotationYCount * rotationZCount);
                if (options.ProposalOptions != null && !options.UseSamplePairTranslationCandidates)
                    throw new ArgumentException("Reduced proposals require explicit sample-pair translation candidates.");
                int modelAnchorLimit = options.ProposalOptions == null ? modelSamples.Count
                    : Math.Min(modelSamples.Count, options.ProposalOptions.MaximumModelAnchorCount);
                int sceneAnchorLimit = options.ProposalOptions == null ? sceneSamples.Count
                    : Math.Min(sceneSamples.Count, options.ProposalOptions.MaximumSceneAnchorCount);
                long translationsPerRotation = options.UseSamplePairTranslationCandidates
                    ? checked(1L + (long)modelAnchorLimit * sceneAnchorLimit) : 1L;
                if (rotationCount > options.MaximumCandidateCount / translationsPerRotation)
                    throw new ArgumentException("Rigid pose search expanded candidate count exceeds the declared maximum " + options.MaximumCandidateCount + ".");

                computeEvidence = computeSession?.DescribeCpuExecution(CudaFallbackReason.NoMeasuredBenefit, cancellationToken);
                IReadOnlyList<SurfaceMatchSample> modelAnchors = options.ProposalOptions == null ? null
                    : SelectProposalSamples(modelSamples, options.ProposalOptions.MaximumModelAnchorCount, cancellationToken);
                IReadOnlyList<SurfaceMatchSample> admissibleScene = options.ProposalOptions == null ? null
                    : SelectAdmissibleSceneSamples(modelSamples, sceneSamples, options, cancellationToken);
                IReadOnlyList<SurfaceMatchSample> sceneAnchors = options.ProposalOptions == null ? null
                    : SelectProposalSamples(admissibleScene, options.ProposalOptions.MaximumSceneAnchorCount, cancellationToken);
                SurfacePoseProposalEvidence proposals = modelAnchors == null ? null
                    : new SurfacePoseProposalEvidence(modelSamples.Count, sceneSamples.Count, sceneSamples.Count, admissibleScene.Count, modelAnchors, sceneAnchors);
                SurfaceSceneIndex sceneIndex = new SurfaceSceneIndex(sceneSamples, (long)modelSamples.Count * rotationCount * translationsPerRotation, cancellationToken);
                Candidate best = null;
                int evaluatedCandidateCount = 0;
                IEnumerable<RigidSurfacePose> poses = EnumeratePoses(modelSamples, sceneSamples, options,
                    true, options.UseSamplePairTranslationCandidates, cancellationToken, modelAnchors, sceneAnchors);
                if (computeSession?.RequestedCuda == true && computeSession.EvaluateUnqualifiedWorkloads && modelSamples.Count >= 256 && rotationCount >= 32)
                {
                    double[] positions = new double[checked(modelSamples.Count * 3)];
                    for (int i = 0; i < modelSamples.Count; i++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        ThreeDPoint point = modelSamples[i].Position;
                        positions[i * 3] = point.X; positions[i * 3 + 1] = point.Y; positions[i * 3 + 2] = point.Z;
                    }
                    foreach (List<RigidSurfacePose> batch in PoseBatches(poses, cancellationToken))
                    {
                        double[] matrices = new double[batch.Count * 12];
                        for (int i = 0; i < batch.Count; i++)
                        {
                            RigidSurfacePose pose = batch[i];
                            if (pose == null) continue;
                            int at = i * 12;
                            matrices[at] = pose.M11; matrices[at + 1] = pose.M12; matrices[at + 2] = pose.M13;
                            matrices[at + 3] = pose.M21; matrices[at + 4] = pose.M22; matrices[at + 5] = pose.M23;
                            matrices[at + 6] = pose.M31; matrices[at + 7] = pose.M32; matrices[at + 8] = pose.M33;
                            matrices[at + 9] = pose.TranslationX; matrices[at + 10] = pose.TranslationY; matrices[at + 11] = pose.TranslationZ;
                        }
                        bool applied = computeSession.TryTransformPoints(positions, matrices, cancellationToken,
                            out double[] transformed, out ComputeExecutionEvidence evidence);
                        computeEvidence = computeEvidence.Combine(evidence);
                        for (int i = 0; i < batch.Count; i++)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            int enumerationOrder = evaluatedCandidateCount++;
                            RigidSurfacePose pose = batch[i];
                            if (pose == null) continue;
                            DeterministicSurfaceCoverageResult coverage = DeterministicSurfaceCoverageTool.ExecuteValidated(
                                modelSamples, sceneIndex, pose, options.MaximumCorrespondenceDistance,
                                applied ? transformed : null, i * modelSamples.Count * 3, cancellationToken);
                            Candidate candidate = new Candidate(enumerationOrder, pose, coverage);
                            if (candidate.IsBetterThan(best)) best = candidate;
                        }
                    }
                }
                else foreach (RigidSurfacePose pose in poses)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    int enumerationOrder = evaluatedCandidateCount++;
                    if (pose == null) continue;
                    DeterministicSurfaceCoverageResult coverage = DeterministicSurfaceCoverageTool.ExecuteValidated(
                        modelSamples, sceneIndex, pose, options.MaximumCorrespondenceDistance, cancellationToken);
                    Candidate candidate = new Candidate(enumerationOrder, pose, coverage);
                    if (candidate.IsBetterThan(best)) best = candidate;
                }

                cancellationToken.ThrowIfCancellationRequested();
                if (best == null
                    || best.Coverage.MatchedModelSampleCount
                        < options.MinimumMatchedSampleCount)
                {
                    DeterministicSurfaceCoverageResult emptyCoverage =
                        best == null
                        ? DeterministicSurfaceCoverageResult.Completed(
                            modelSamples.Count,
                            sceneSamples.Count,
                            0,
                            0.0,
                            false,
                            double.NaN,
                            options.MaximumCorrespondenceDistance,
                            Array.Empty<SurfaceCoverageMatch>())
                        : best.Coverage;
                    string reason = best == null
                        ? "No rotation candidate produced a translation inside the declared bounds."
                        : "Best candidate matched "
                            + best.Coverage.MatchedModelSampleCount
                            + " model samples, below the required "
                            + options.MinimumMatchedSampleCount
                            + ".";
                    return DeterministicRigidSurfacePoseSearchResult.Completed(
                        false,
                        evaluatedCandidateCount,
                        null,
                        emptyCoverage,
                        reason, proposalEvidence: proposals, computeDiagnostics: computeEvidence);
                }

                SurfacePoseAmbiguityEvidence ambiguity = options.AmbiguityOptions == null ? null
                    : EvaluateAmbiguity(modelSamples, sceneIndex, options, best.Pose, best.Coverage,
                        true, options.UseSamplePairTranslationCandidates, cancellationToken, modelAnchors, sceneAnchors);

                SurfacePoseRefinementResult refinement = null;
                if (options.RefinementOptions != null)
                {
                    refinement = new DeterministicSurfacePoseRefinementTool().Execute(
                        modelSamples, sceneSamples, best.Pose, options, cancellationToken);
                    if (!refinement.Success) throw new InvalidOperationException(refinement.Message);
                }

                SurfacePoseAmbiguityEvidence finalAmbiguity = options.AmbiguityOptions?.MaximumRefinedCandidateCount == null ? null
                    : EvaluateRefinedAmbiguity(modelSamples, sceneIndex, options, refinement.Pose, refinement.Coverage,
                        true, options.UseSamplePairTranslationCandidates, cancellationToken, modelAnchors, sceneAnchors);
                cancellationToken.ThrowIfCancellationRequested();
                return DeterministicRigidSurfacePoseSearchResult.Completed(
                    true,
                    evaluatedCandidateCount,
                    refinement?.Pose ?? best.Pose,
                    refinement?.Coverage ?? best.Coverage,
                    string.Empty,
                    refinement,
                    ambiguity,
                    proposals,
                    finalAmbiguity, computeDiagnostics: computeEvidence);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                return DeterministicRigidSurfacePoseSearchResult.Failed(
                    "Deterministic rigid surface pose search failed: "
                    + exception.Message, computeEvidence);
            }
        }

        internal static void ValidateOptions(
            DeterministicRigidSurfacePoseSearchOptions options,
            int modelSampleCount,
            int sceneSampleCount)
        {
            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            if (options.ProposalOptions != null)
            {
                SurfacePoseProposalOptions proposals = options.ProposalOptions;
                if (proposals.MaximumModelAnchorCount < 1 || proposals.MaximumSceneAnchorCount < 1
                    || proposals.MaximumModelAnchorCount > SurfacePoseProposalOptions.AbsoluteMaximumAnchorCount
                    || proposals.MaximumSceneAnchorCount > SurfacePoseProposalOptions.AbsoluteMaximumAnchorCount)
                    throw new ArgumentException("Reduced proposal anchor counts must be from 1 through 1024.");
            }

            if (options.RefinementOptions != null)
            {
                DeterministicSurfacePoseRefinementTool.ValidateRefinementOptions(options.RefinementOptions);
                if (options.RefinementOptions.MinimumRetainedCorrespondenceCount > modelSampleCount)
                    throw new ArgumentException("The retained-fit minimum cannot exceed the model sample count.");
            }

            if (options.AmbiguityOptions != null)
            {
                SurfacePoseAmbiguityOptions ambiguity = options.AmbiguityOptions;
                if (ambiguity.MaximumRefinedCandidateCount.HasValue
                    && (options.RefinementOptions == null || ambiguity.MaximumRefinedCandidateCount.Value < 1
                        || ambiguity.MaximumRefinedCandidateCount.Value > AbsoluteMaximumCandidateCount))
                    throw new ArgumentException("Final ambiguity requires refinement and an explicit refinement budget from 1 through 1000000.");
                if (!SurfaceMatchingContractValidation.IsFinite(ambiguity.MinimumRotationSeparationDegrees)
                    || ambiguity.MinimumRotationSeparationDegrees <= 0.0 || ambiguity.MinimumRotationSeparationDegrees > 180.0
                    || !SurfaceMatchingContractValidation.IsFinite(ambiguity.MaximumTranslationSeparation) || ambiguity.MaximumTranslationSeparation < 0.0
                    || (ambiguity.MinimumTranslationSeparation.HasValue
                        && (!SurfaceMatchingContractValidation.IsFinite(ambiguity.MinimumTranslationSeparation.Value)
                            || ambiguity.MinimumTranslationSeparation.Value <= 0.0
                            || ambiguity.MinimumTranslationSeparation.Value > ambiguity.MaximumTranslationSeparation))
                    || ambiguity.MaximumMatchedSampleCountDifference < 0 || ambiguity.MaximumMatchedSampleCountDifference > modelSampleCount
                    || !SurfaceMatchingContractValidation.IsFinite(ambiguity.MaximumRmseDifference) || ambiguity.MaximumRmseDifference < 0.0)
                    throw new ArgumentException("Surface pose ambiguity requires a rotation separation in (0,180], a finite nonnegative maximum translation separation, an optional finite positive minimum translation separation no greater than the maximum, a nonnegative bounded support difference, and a finite nonnegative RMSE difference.");
            }

            double[] translation =
            {
                options.MinimumTranslationX,
                options.MaximumTranslationX,
                options.MinimumTranslationY,
                options.MaximumTranslationY,
                options.MinimumTranslationZ,
                options.MaximumTranslationZ
            };
            for (int index = 0; index < translation.Length; index++)
            {
                if (!SurfaceMatchingContractValidation.IsFinite(
                        translation[index]))
                {
                    throw new ArgumentException(
                        "Rigid pose translation bounds must be finite and ordered.");
                }
            }

            if (options.MinimumTranslationX > options.MaximumTranslationX
                || options.MinimumTranslationY > options.MaximumTranslationY
                || options.MinimumTranslationZ > options.MaximumTranslationZ)
            {
                throw new ArgumentException(
                    "Rigid pose translation bounds must be finite and ordered.");
            }

            if (!SurfaceMatchingContractValidation.IsFinite(
                    options.MaximumCorrespondenceDistance)
                || options.MaximumCorrespondenceDistance <= 0.0)
            {
                throw new ArgumentException(
                    "Maximum correspondence distance must be finite and positive.");
            }

            if (options.MinimumMatchedSampleCount < 3
                || options.MinimumMatchedSampleCount > modelSampleCount
                || options.MinimumMatchedSampleCount > sceneSampleCount)
            {
                throw new ArgumentException(
                    "Rigid pose search minimum matched sample count must be at least three and cannot exceed available samples.");
            }

            if (options.MaximumCandidateCount <= 0
                || options.MaximumCandidateCount
                    > AbsoluteMaximumCandidateCount)
            {
                throw new ArgumentException(
                    "Maximum candidate count must be from 1 through "
                    + AbsoluteMaximumCandidateCount
                    + ".");
            }
        }

        internal SurfacePoseAmbiguityEvidence EvaluateAmbiguity(IReadOnlyList<SurfaceMatchSample> modelSamples,
            SurfaceSceneIndex sceneIndex, DeterministicRigidSurfacePoseSearchOptions options,
            RigidSurfacePose referencePose, DeterministicSurfaceCoverageResult referenceCoverage,
            bool includeCentroid, bool includeAnchors, CancellationToken cancellationToken,
            IReadOnlyList<SurfaceMatchSample> modelAnchors = null, IReadOnlyList<SurfaceMatchSample> sceneAnchors = null)
        {
            SurfacePoseAmbiguityOptions policy = options.AmbiguityOptions;
            RigidPoseSymmetryEquivalenceOptions comparison = new RigidPoseSymmetryEquivalenceOptions
            {
                Symmetry = new RigidPoseSymmetry(RigidPoseSymmetryKind.None, RigidPoseSymmetryAxis.None, 1),
                MaximumTranslationDifference = double.MaxValue, MaximumRotationDifferenceDegrees = 180, RigidTransformTolerance = 1e-9
            };
            Candidate alternative = null;
            double alternativeTranslationSeparation = double.NaN;
            int evaluated = 0;
            foreach (RigidSurfacePose pose in EnumeratePoses(modelSamples, sceneIndex.Samples, options,
                includeCentroid, includeAnchors, cancellationToken, modelAnchors, sceneAnchors))
            {
                cancellationToken.ThrowIfCancellationRequested();
                int order = evaluated++;
                if (pose == null) continue;
                double translationSeparation = SurfaceSceneIndex.Distance(
                    new ThreeDPoint(referencePose.TranslationX, referencePose.TranslationY, referencePose.TranslationZ),
                    new ThreeDPoint(pose.TranslationX, pose.TranslationY, pose.TranslationZ));
                if (translationSeparation > policy.MaximumTranslationSeparation) continue;
                double rotationSeparation = rotationComparison.Execute(referencePose, pose, comparison).RotationDifferenceDegrees;
                if (rotationSeparation < policy.MinimumRotationSeparationDegrees
                    && (!policy.MinimumTranslationSeparation.HasValue || translationSeparation < policy.MinimumTranslationSeparation.Value)) continue;
                DeterministicSurfaceCoverageResult coverage = DeterministicSurfaceCoverageTool.ExecuteValidated(
                    modelSamples, sceneIndex, pose, options.MaximumCorrespondenceDistance, cancellationToken);
                // Translation mode reports a qualifying competitor rather than hiding one behind a stronger diagnostic candidate outside the RMSE gate.
                if (policy.MinimumTranslationSeparation.HasValue
                    && (!coverage.HasInlierRmse || coverage.MatchedModelSampleCount < options.MinimumMatchedSampleCount
                        || referenceCoverage.MatchedModelSampleCount - coverage.MatchedModelSampleCount > policy.MaximumMatchedSampleCountDifference
                        || Math.Abs(referenceCoverage.InlierRmse - coverage.InlierRmse) > policy.MaximumRmseDifference)) continue;
                Candidate candidate = new Candidate(order, pose, coverage);
                if (candidate.IsBetterThan(alternative))
                {
                    alternative = candidate;
                    alternativeTranslationSeparation = translationSeparation;
                }
            }

            cancellationToken.ThrowIfCancellationRequested();
            bool ambiguous = alternative != null && alternative.Coverage.HasInlierRmse
                && alternative.Coverage.MatchedModelSampleCount >= options.MinimumMatchedSampleCount
                && referenceCoverage.MatchedModelSampleCount - alternative.Coverage.MatchedModelSampleCount <= policy.MaximumMatchedSampleCountDifference
                && Math.Abs(referenceCoverage.InlierRmse - alternative.Coverage.InlierRmse) <= policy.MaximumRmseDifference;
            return new SurfacePoseAmbiguityEvidence(referencePose, referenceCoverage, alternative?.Pose, alternative?.Coverage,
                evaluated, alternative == null ? double.NaN : rotationComparison.Execute(referencePose, alternative.Pose, comparison).RotationDifferenceDegrees,
                ambiguous, policy, alternativeTranslationSeparation);
        }

        internal SurfacePoseAmbiguityEvidence EvaluateRefinedAmbiguity(IReadOnlyList<SurfaceMatchSample> modelSamples,
            SurfaceSceneIndex sceneIndex, DeterministicRigidSurfacePoseSearchOptions options,
            RigidSurfacePose referencePose, DeterministicSurfaceCoverageResult referenceCoverage,
            bool includeCentroid, bool includeAnchors, CancellationToken cancellationToken,
            IReadOnlyList<SurfaceMatchSample> modelAnchors = null, IReadOnlyList<SurfaceMatchSample> sceneAnchors = null)
        {
            SurfacePoseAmbiguityOptions policy = options.AmbiguityOptions;
            RigidPoseSymmetryEquivalenceOptions comparison = new RigidPoseSymmetryEquivalenceOptions
            {
                Symmetry = new RigidPoseSymmetry(RigidPoseSymmetryKind.None, RigidPoseSymmetryAxis.None, 1),
                MaximumTranslationDifference = double.MaxValue, MaximumRotationDifferenceDegrees = 180, RigidTransformTolerance = 1e-9
            };
            Candidate alternative = null;
            double alternativeTranslation = double.NaN;
            int evaluated = 0, refined = 0;
            bool complete = true;
            foreach (RigidSurfacePose seed in EnumeratePoses(modelSamples, sceneIndex.Samples, options,
                includeCentroid, includeAnchors, cancellationToken, modelAnchors, sceneAnchors))
            {
                cancellationToken.ThrowIfCancellationRequested();
                int order = evaluated++;
                if (seed == null) continue;
                DeterministicSurfaceCoverageResult seedCoverage = DeterministicSurfaceCoverageTool.ExecuteValidated(
                    modelSamples, sceneIndex, seed, options.MaximumCorrespondenceDistance, cancellationToken);
                if (seedCoverage.MatchedModelSampleCount < options.MinimumMatchedSampleCount) continue;
                if (refined == policy.MaximumRefinedCandidateCount.Value) { complete = false; break; }
                refined++;
                SurfacePoseRefinementResult result = new DeterministicSurfacePoseRefinementTool().Execute(
                    modelSamples, sceneIndex.Samples, seed, options, cancellationToken);
                if (!result.Success) throw new InvalidOperationException(result.Message);
                RigidSurfacePose pose = result.Pose;
                DeterministicSurfaceCoverageResult coverage = result.Coverage;
                double translation = SurfaceSceneIndex.Distance(
                    new ThreeDPoint(referencePose.TranslationX, referencePose.TranslationY, referencePose.TranslationZ),
                    new ThreeDPoint(pose.TranslationX, pose.TranslationY, pose.TranslationZ));
                double rotation = rotationComparison.Execute(referencePose, pose, comparison).RotationDifferenceDegrees;
                if (translation > policy.MaximumTranslationSeparation
                    || (rotation < policy.MinimumRotationSeparationDegrees
                        && (!policy.MinimumTranslationSeparation.HasValue || translation < policy.MinimumTranslationSeparation.Value))
                    || !coverage.HasInlierRmse || coverage.MatchedModelSampleCount < options.MinimumMatchedSampleCount
                    || referenceCoverage.MatchedModelSampleCount - coverage.MatchedModelSampleCount > policy.MaximumMatchedSampleCountDifference
                    || Math.Abs(referenceCoverage.InlierRmse - coverage.InlierRmse) > policy.MaximumRmseDifference) continue;
                Candidate candidate = new Candidate(order, pose, coverage);
                if (candidate.IsBetterThan(alternative)) { alternative = candidate; alternativeTranslation = translation; }
            }
            cancellationToken.ThrowIfCancellationRequested();
            return new SurfacePoseAmbiguityEvidence(referencePose, referenceCoverage, alternative?.Pose, alternative?.Coverage,
                evaluated, alternative == null ? double.NaN : rotationComparison.Execute(referencePose, alternative.Pose, comparison).RotationDifferenceDegrees,
                alternative != null, policy, alternativeTranslation, true, complete, refined);
        }

        private static IEnumerable<RigidSurfacePose> EnumeratePoses(IReadOnlyList<SurfaceMatchSample> modelSamples,
            IReadOnlyList<SurfaceMatchSample> sceneSamples, DeterministicRigidSurfacePoseSearchOptions options,
            bool includeCentroid, bool includeAnchors, CancellationToken cancellationToken,
            IReadOnlyList<SurfaceMatchSample> modelAnchors = null, IReadOnlyList<SurfaceMatchSample> sceneAnchors = null)
        {
            double[] x = AxisCandidates(options.MinimumRotationXDegrees, options.RotationStepXDegrees,
                AxisCandidateCount(options.MinimumRotationXDegrees, options.MaximumRotationXDegrees, options.RotationStepXDegrees, "X"), cancellationToken);
            double[] y = AxisCandidates(options.MinimumRotationYDegrees, options.RotationStepYDegrees,
                AxisCandidateCount(options.MinimumRotationYDegrees, options.MaximumRotationYDegrees, options.RotationStepYDegrees, "Y"), cancellationToken);
            double[] z = AxisCandidates(options.MinimumRotationZDegrees, options.RotationStepZDegrees,
                AxisCandidateCount(options.MinimumRotationZDegrees, options.MaximumRotationZDegrees, options.RotationStepZDegrees, "Z"), cancellationToken);
            ThreeDPoint modelCentroid = includeCentroid ? Centroid(modelSamples, cancellationToken) : null;
            ThreeDPoint sceneCentroid = includeCentroid ? Centroid(sceneSamples, cancellationToken) : null;
            foreach (double rx in x)
            foreach (double ry in y)
            foreach (double rz in z)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Rotation3 rotation = Rotation(rx, ry, rz);
                if (includeCentroid) yield return AnchorPose(rotation, modelCentroid, sceneCentroid, options);
                if (!includeAnchors) continue;
                IReadOnlyList<SurfaceMatchSample> selectedModel = modelAnchors ?? modelSamples;
                IReadOnlyList<SurfaceMatchSample> selectedScene = sceneAnchors ?? sceneSamples;
                for (int modelIndex = 0; modelIndex < selectedModel.Count; modelIndex++)
                for (int sceneIndex = 0; sceneIndex < selectedScene.Count; sceneIndex++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    yield return AnchorPose(rotation, selectedModel[modelIndex].Position, selectedScene[sceneIndex].Position, options);
                }
            }
        }

        internal static IReadOnlyList<SurfaceMatchSample> SelectAdmissibleSceneSamples(IReadOnlyList<SurfaceMatchSample> modelSamples,
            IReadOnlyList<SurfaceMatchSample> sceneSamples, DeterministicRigidSurfacePoseSearchOptions options, CancellationToken cancellationToken)
        {
            double radius = 0D;
            for (int i = 0; i < modelSamples.Count; i++)
            {
                if ((i & 1023) == 0) cancellationToken.ThrowIfCancellationRequested();
                ThreeDPoint point = modelSamples[i].Position;
                double scale = Math.Max(Math.Abs(point.X), Math.Max(Math.Abs(point.Y), Math.Abs(point.Z)));
                if (scale > 0D)
                    radius = Math.Max(radius, scale * Math.Sqrt(Math.Pow(point.X / scale, 2) + Math.Pow(point.Y / scale, 2) + Math.Pow(point.Z / scale, 2)));
            }
            // Any rigid rotation lies in this conservative source-origin sphere. Overflow widens admission to all finite points.
            radius = radius * (1D + 1e-12D) + options.MaximumCorrespondenceDistance;
            List<SurfaceMatchSample> admitted = new List<SurfaceMatchSample>();
            for (int i = 0; i < sceneSamples.Count; i++)
            {
                if ((i & 1023) == 0) cancellationToken.ThrowIfCancellationRequested();
                SurfaceMatchSample sample = sceneSamples[i];
                ThreeDPoint point = sample.Position;
                if (point.X >= options.MinimumTranslationX - radius && point.X <= options.MaximumTranslationX + radius
                    && point.Y >= options.MinimumTranslationY - radius && point.Y <= options.MaximumTranslationY + radius
                    && point.Z >= options.MinimumTranslationZ - radius && point.Z <= options.MaximumTranslationZ + radius) admitted.Add(sample);
            }
            cancellationToken.ThrowIfCancellationRequested();
            return admitted;
        }

        internal static IReadOnlyList<SurfaceMatchSample> SelectProposalSamples(
            IReadOnlyList<SurfaceMatchSample> samples, int maximumCount, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (samples.Count <= maximumCount) return samples;
            List<ThreeDPoint> positions = new List<ThreeDPoint>(samples.Count);
            for (int index = 0; index < samples.Count; index++)
            {
                if ((index & 1023) == 0) cancellationToken.ThrowIfCancellationRequested();
                positions.Add(samples[index].Position);
            }

            List<KeyValuePair<int, double>> selected = DeterministicModelKeyPointExtractionTool.SelectPositionIndices(
                positions, maximumCount, 0, cancellationToken);
            selected.Sort((first, second) => first.Key.CompareTo(second.Key));
            List<SurfaceMatchSample> result = new List<SurfaceMatchSample>(selected.Count);
            for (int index = 0; index < selected.Count; index++)
            {
                if ((index & 1023) == 0) cancellationToken.ThrowIfCancellationRequested();
                result.Add(samples[selected[index].Key]);
            }

            cancellationToken.ThrowIfCancellationRequested();
            return result;
        }

        private static RigidSurfacePose AnchorPose(Rotation3 rotation, ThreeDPoint model, ThreeDPoint scene,
            DeterministicRigidSurfacePoseSearchOptions options)
        {
            ThreeDPoint rotated = rotation.Transform(model);
            ThreeDPoint translation = new ThreeDPoint(scene.X - rotated.X, scene.Y - rotated.Y, scene.Z - rotated.Z);
            if (!InsideTranslationBounds(translation, options)) return null;
            return new RigidSurfacePose(rotation.M11, rotation.M12, rotation.M13, rotation.M21, rotation.M22,
                rotation.M23, rotation.M31, rotation.M32, rotation.M33, translation.X, translation.Y, translation.Z);
        }

        internal static int AxisCandidateCount(
            double minimum,
            double maximum,
            double step,
            string axis)
        {
            if (!SurfaceMatchingContractValidation.IsFinite(minimum)
                || !SurfaceMatchingContractValidation.IsFinite(maximum)
                || !SurfaceMatchingContractValidation.IsFinite(step)
                || minimum > maximum
                || step <= 0.0
                || minimum < -180.0
                || maximum > 180.0)
            {
                throw new ArgumentException(
                    "Rigid pose "
                    + axis
                    + "-rotation range requires finite ordered bounds in [-180,180] and a positive step.");
            }

            double candidateCount = Math.Floor(
                (maximum - minimum) / step + 1e-12) + 1.0;
            if (!SurfaceMatchingContractValidation.IsFinite(candidateCount)
                || candidateCount < 1.0)
            {
                throw new ArgumentException(
                    "Rigid pose " + axis + "-rotation range has no candidate.");
            }

            if (candidateCount > AbsoluteMaximumCandidateCount)
            {
                throw new ArgumentException(
                    "Rigid pose "
                    + axis
                    + "-rotation candidate count exceeds the supported limit "
                    + AbsoluteMaximumCandidateCount
                    + ".");
            }

            return checked((int)candidateCount);
        }

        internal static double[] AxisCandidates(
            double minimum,
            double step,
            int count,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            double[] values = new double[count];
            for (int index = 0; index < count; index++)
            {
                if ((index & 1023) == 0) cancellationToken.ThrowIfCancellationRequested();
                values[index] = minimum + index * step;
            }

            cancellationToken.ThrowIfCancellationRequested();
            return values;
        }

        private static ThreeDPoint Centroid(
            IReadOnlyList<SurfaceMatchSample> samples,
            CancellationToken cancellationToken)
        {
            double x = 0.0;
            double y = 0.0;
            double z = 0.0;
            for (int index = 0; index < samples.Count; index++)
            {
                if ((index & 1023) == 0) cancellationToken.ThrowIfCancellationRequested();
                x += samples[index].Position.X;
                y += samples[index].Position.Y;
                z += samples[index].Position.Z;
            }

            cancellationToken.ThrowIfCancellationRequested();
            return new ThreeDPoint(
                x / samples.Count,
                y / samples.Count,
                z / samples.Count);
        }

        internal static Rotation3 Rotation(
            double xDegrees,
            double yDegrees,
            double zDegrees)
        {
            double x = xDegrees * Math.PI / 180.0;
            double y = yDegrees * Math.PI / 180.0;
            double z = zDegrees * Math.PI / 180.0;
            double cx = Math.Cos(x);
            double sx = Math.Sin(x);
            double cy = Math.Cos(y);
            double sy = Math.Sin(y);
            double cz = Math.Cos(z);
            double sz = Math.Sin(z);
            return new Rotation3(
                cz * cy,
                cz * sy * sx - sz * cx,
                cz * sy * cx + sz * sx,
                sz * cy,
                sz * sy * sx + cz * cx,
                sz * sy * cx - cz * sx,
                -sy,
                cy * sx,
                cy * cx);
        }

        internal static bool InsideTranslationBounds(
            ThreeDPoint translation,
            DeterministicRigidSurfacePoseSearchOptions options)
        {
            return translation.X >= options.MinimumTranslationX
                && translation.X <= options.MaximumTranslationX
                && translation.Y >= options.MinimumTranslationY
                && translation.Y <= options.MaximumTranslationY
                && translation.Z >= options.MinimumTranslationZ
                && translation.Z <= options.MaximumTranslationZ;
        }

        private static IEnumerable<List<RigidSurfacePose>> PoseBatches(IEnumerable<RigidSurfacePose> poses, CancellationToken token)
        {
            List<RigidSurfacePose> batch = new List<RigidSurfacePose>(32);
            foreach (RigidSurfacePose pose in poses)
            {
                token.ThrowIfCancellationRequested();
                batch.Add(pose);
                if (batch.Count != 32) continue;
                yield return batch;
                batch = new List<RigidSurfacePose>(32);
            }
            if (batch.Count > 0) yield return batch;
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

        internal sealed class Rotation3
        {
            public Rotation3(
                double m11,
                double m12,
                double m13,
                double m21,
                double m22,
                double m23,
                double m31,
                double m32,
                double m33)
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

            public ThreeDPoint Transform(ThreeDPoint point)
            {
                return new ThreeDPoint(
                    M11 * point.X + M12 * point.Y + M13 * point.Z,
                    M21 * point.X + M22 * point.Y + M23 * point.Z,
                    M31 * point.X + M32 * point.Y + M33 * point.Z);
            }
        }
    }
}
