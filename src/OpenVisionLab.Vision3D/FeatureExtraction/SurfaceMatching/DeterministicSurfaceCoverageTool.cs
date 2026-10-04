using System;
using System.Collections.Generic;
using System.Threading;

namespace OpenVisionLab.Vision3D.FeatureExtraction
{
    /// <summary>
    /// Computes deterministic one-way model-to-scene coverage. Model samples
    /// are visited in stable order and claim the nearest unclaimed scene
    /// sample within the inclusive distance limit.
    /// </summary>
    public sealed class DeterministicSurfaceCoverageTool
    {
        public DeterministicSurfaceCoverageResult Execute(
            IReadOnlyList<SurfaceMatchSample> modelSamples,
            IReadOnlyList<SurfaceMatchSample> sceneSamples,
            RigidSurfacePose pose,
            double maximumCorrespondenceDistance,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            try
            {
                SurfaceMatchingContractValidation.ValidateSamples(
                    modelSamples,
                    "Surface coverage model input",
                    cancellationToken);
                SurfaceMatchingContractValidation.ValidateSamples(
                    sceneSamples,
                    "Surface coverage scene input",
                    cancellationToken);
                if (pose == null || !pose.IsRigid(1e-9))
                {
                    throw new ArgumentException(
                        "Surface coverage requires a finite rigid pose.");
                }

                if (!SurfaceMatchingContractValidation.IsFinite(
                        maximumCorrespondenceDistance)
                    || maximumCorrespondenceDistance <= 0.0)
                {
                    throw new ArgumentException(
                        "Surface coverage distance must be finite and positive.");
                }

                return ExecuteValidated(modelSamples, new SurfaceSceneIndex(sceneSamples, modelSamples.Count, cancellationToken),
                    pose, maximumCorrespondenceDistance, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                return DeterministicSurfaceCoverageResult.Failed(
                    "Deterministic surface coverage failed: "
                    + exception.Message);
            }
        }

        internal static DeterministicSurfaceCoverageResult ExecuteValidated(
            IReadOnlyList<SurfaceMatchSample> modelSamples, SurfaceSceneIndex sceneIndex,
            RigidSurfacePose pose, double maximumCorrespondenceDistance, CancellationToken cancellationToken)
        {
            return ExecuteValidated(modelSamples, sceneIndex, pose, maximumCorrespondenceDistance, null, 0, cancellationToken);
        }

        internal static DeterministicSurfaceCoverageResult ExecuteValidated(
            IReadOnlyList<SurfaceMatchSample> modelSamples, SurfaceSceneIndex sceneIndex,
            RigidSurfacePose pose, double maximumCorrespondenceDistance,
            double[] transformedPositions, int transformedOffset, CancellationToken cancellationToken)
        {
            IReadOnlyList<SurfaceMatchSample> sceneSamples = sceneIndex.Samples;
            bool[] claimedSceneSamples = new bool[sceneSamples.Count];
            List<SurfaceCoverageMatch> matches =
                new List<SurfaceCoverageMatch>(
                    Math.Min(modelSamples.Count, sceneSamples.Count));
            double rmseScale = 0.0;
            double rmseScaledSquareSum = 0.0;
            for (int modelIndex = 0;
                 modelIndex < modelSamples.Count;
                 modelIndex++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                SurfaceMatchSample modelSample = modelSamples[modelIndex];
                int transformedIndex = transformedOffset + modelIndex * 3;
                ThreeDPoint transformed = transformedPositions == null ? pose.Transform(modelSample.Position)
                    : new ThreeDPoint(transformedPositions[transformedIndex], transformedPositions[transformedIndex + 1], transformedPositions[transformedIndex + 2]);
                int bestSceneOrder = -1;
                double bestDistance = double.PositiveInfinity;
                if (sceneIndex.IsIndexed)
                {
                    bestSceneOrder = sceneIndex.FindNearest(transformed, claimedSceneSamples,
                        maximumCorrespondenceDistance, cancellationToken, out bestDistance);
                }
                else
                {
                    for (int sceneOrder = 0; sceneOrder < sceneSamples.Count; sceneOrder++)
                    {
                        if ((sceneOrder & 1023) == 0) cancellationToken.ThrowIfCancellationRequested();
                        SurfaceMatchSample sample = sceneSamples[sceneOrder];
                        if (claimedSceneSamples[sample.Order]) continue;
                        double distance = SurfaceSceneIndex.Distance(transformed, sample.Position);
                        if (distance < bestDistance || distance == bestDistance && sample.Order < bestSceneOrder)
                        {
                            bestDistance = distance;
                            bestSceneOrder = sample.Order;
                        }
                    }
                }

                cancellationToken.ThrowIfCancellationRequested();
                if (bestSceneOrder < 0
                    || bestDistance > maximumCorrespondenceDistance)
                {
                    continue;
                }

                claimedSceneSamples[bestSceneOrder] = true;
                AccumulateScaledSquare(
                    bestDistance,
                    ref rmseScale,
                    ref rmseScaledSquareSum);
                matches.Add(
                    new SurfaceCoverageMatch(
                        modelSample.Order,
                        bestSceneOrder,
                        bestDistance));
            }

            cancellationToken.ThrowIfCancellationRequested();
            int matchedCount = matches.Count;
            double coverageRatio =
                matchedCount / (double)modelSamples.Count;
            bool hasInlierRmse = matchedCount > 0;
            double inlierRmse = !hasInlierRmse
                ? double.NaN
                : rmseScale == 0.0
                    ? 0.0
                    : rmseScale * Math.Min(
                        1.0,
                        Math.Sqrt(
                            rmseScaledSquareSum / matchedCount));
            return DeterministicSurfaceCoverageResult.Completed(
                modelSamples.Count,
                sceneSamples.Count,
                matchedCount,
                coverageRatio,
                hasInlierRmse,
                inlierRmse,
                maximumCorrespondenceDistance,
                matches);
        }

        private static void AccumulateScaledSquare(
            double value,
            ref double scale,
            ref double scaledSquareSum)
        {
            if (value == 0.0)
            {
                return;
            }

            if (scale < value)
            {
                double ratio = scale / value;
                scaledSquareSum = 1.0
                    + scaledSquareSum * ratio * ratio;
                scale = value;
            }
            else
            {
                double ratio = value / scale;
                scaledSquareSum += ratio * ratio;
            }
        }
    }
}
