using System;
using System.Collections.Generic;
using System.Threading;

namespace OpenVisionLab.Vision3D.FeatureExtraction
{
    public sealed class ModelKeyPointInput
    {
        public ModelKeyPointInput(
            int sourceSampleOrder,
            ThreeDPoint position,
            ThreeDPoint normal)
        {
            SourceSampleOrder = sourceSampleOrder;
            Position = position;
            Normal = normal;
        }

        public int SourceSampleOrder { get; }
        public ThreeDPoint Position { get; }
        public ThreeDPoint Normal { get; }
    }

    public sealed class ExtractedModelKeyPoint
    {
        public ExtractedModelKeyPoint(
            int order,
            int sourceSampleOrder,
            ThreeDPoint position,
            ThreeDPoint normal,
            double nearestSelectedDistance)
        {
            Order = order;
            SourceSampleOrder = sourceSampleOrder;
            Position = position;
            Normal = normal;
            NearestSelectedDistance = nearestSelectedDistance;
        }

        public int Order { get; }
        public int SourceSampleOrder { get; }
        public ThreeDPoint Position { get; }
        public ThreeDPoint Normal { get; }
        public double NearestSelectedDistance { get; }
    }

    public sealed class DeterministicModelKeyPointExtractionOptions
    {
        public int MaximumKeyPointCount { get; set; }
        public double MinimumSeparation { get; set; }
    }

    public sealed class DeterministicModelKeyPointExtractionResult
    {
        private DeterministicModelKeyPointExtractionResult(
            bool success,
            string message,
            IReadOnlyList<ExtractedModelKeyPoint> keyPoints)
        {
            Success = success;
            Message = message ?? string.Empty;
            KeyPoints = keyPoints ?? Array.Empty<ExtractedModelKeyPoint>();
        }

        public bool Success { get; }
        public string Message { get; }
        public IReadOnlyList<ExtractedModelKeyPoint> KeyPoints { get; }

        internal static DeterministicModelKeyPointExtractionResult Completed(
            IReadOnlyList<ExtractedModelKeyPoint> keyPoints)
        {
            return new DeterministicModelKeyPointExtractionResult(
                true,
                string.Empty,
                keyPoints);
        }

        internal static DeterministicModelKeyPointExtractionResult Failed(
            string message)
        {
            return new DeterministicModelKeyPointExtractionResult(
                false,
                message,
                Array.Empty<ExtractedModelKeyPoint>());
        }
    }

    /// <summary>
    /// Selects a deterministic spatially distributed subset from prepared
    /// model samples. The lowest source order is the seed; each later point
    /// maximizes distance to its nearest selected point, with source order as
    /// the exact-tie breaker. The Tool does not execute or alter matching.
    /// </summary>
    public sealed class DeterministicModelKeyPointExtractionTool
    {
        public const string Semantics =
            "deterministic-farthest-model-sample-v1";

        public DeterministicModelKeyPointExtractionResult Execute(
            IReadOnlyList<ModelKeyPointInput> samples,
            DeterministicModelKeyPointExtractionOptions options,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                Validate(samples, options, cancellationToken);
                List<ModelKeyPointInput> candidates = new List<ModelKeyPointInput>(samples.Count);
                for (int index = 0; index < samples.Count; index++)
                {
                    if ((index & 1023) == 0) cancellationToken.ThrowIfCancellationRequested();
                    candidates.Add(samples[index]);
                }

                cancellationToken.ThrowIfCancellationRequested();
                int comparisons = 0;
                candidates.Sort((first, second) =>
                {
                    if ((comparisons++ & 1023) == 0) cancellationToken.ThrowIfCancellationRequested();
                    return first.SourceSampleOrder.CompareTo(second.SourceSampleOrder);
                });
                cancellationToken.ThrowIfCancellationRequested();

                List<ThreeDPoint> positions = new List<ThreeDPoint>(candidates.Count);
                for (int index = 0; index < candidates.Count; index++)
                {
                    if ((index & 1023) == 0) cancellationToken.ThrowIfCancellationRequested();
                    positions.Add(candidates[index].Position);
                }

                List<KeyValuePair<int, double>> selected = SelectPositionIndices(
                    positions, options.MaximumKeyPointCount, options.MinimumSeparation, cancellationToken);
                List<ExtractedModelKeyPoint> keyPoints = new List<ExtractedModelKeyPoint>(selected.Count);
                foreach (KeyValuePair<int, double> selection in selected)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    ModelKeyPointInput sample = candidates[selection.Key];
                    keyPoints.Add(new ExtractedModelKeyPoint(keyPoints.Count, sample.SourceSampleOrder,
                        sample.Position, sample.Normal, selection.Value));
                }

                cancellationToken.ThrowIfCancellationRequested();
                return DeterministicModelKeyPointExtractionResult.Completed(keyPoints);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return DeterministicModelKeyPointExtractionResult.Failed(
                    "Deterministic model key-point extraction failed: "
                    + exception.Message);
            }
        }

        // Shared geometry selection: the caller validates positions and supplies a stable order.
        internal static List<KeyValuePair<int, double>> SelectPositionIndices(
            IReadOnlyList<ThreeDPoint> positions, int maximumCount, double minimumSeparation,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int targetCount = Math.Min(maximumCount, positions.Count);
            List<KeyValuePair<int, double>> result = new List<KeyValuePair<int, double>>(targetCount);
            if (targetCount == 0) return result;
            bool[] selected = new bool[positions.Count];
            double[] nearest = new double[positions.Count];
            for (int index = 0; index < nearest.Length; index++)
            {
                if ((index & 1023) == 0) cancellationToken.ThrowIfCancellationRequested();
                nearest[index] = double.PositiveInfinity;
            }

            int nextIndex = 0;
            double nextDistance = 0;
            while (result.Count < targetCount)
            {
                cancellationToken.ThrowIfCancellationRequested();
                selected[nextIndex] = true;
                result.Add(new KeyValuePair<int, double>(nextIndex, nextDistance));
                int bestIndex = -1;
                double bestDistance = -1;
                for (int index = 0; index < positions.Count; index++)
                {
                    if ((index & 1023) == 0) cancellationToken.ThrowIfCancellationRequested();
                    if (selected[index]) continue;
                    double distance = Distance(positions[nextIndex], positions[index]);
                    if (!SurfaceMatchingContractValidation.IsFinite(distance))
                        throw new ArgumentException("Model key-point distance must be finite.");
                    if (distance < nearest[index]) nearest[index] = distance;
                    if (nearest[index] > bestDistance)
                    {
                        bestIndex = index;
                        bestDistance = nearest[index];
                    }
                }

                if (bestIndex < 0 || bestDistance <= minimumSeparation) break;
                nextIndex = bestIndex;
                nextDistance = bestDistance;
            }

            cancellationToken.ThrowIfCancellationRequested();
            return result;
        }

        private static void Validate(
            IReadOnlyList<ModelKeyPointInput> samples,
            DeterministicModelKeyPointExtractionOptions options,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (samples == null || samples.Count == 0)
            {
                throw new ArgumentException(
                    "Model key-point extraction requires prepared model samples.");
            }

            if (options == null
                || options.MaximumKeyPointCount <= 0
                || !SurfaceMatchingContractValidation.IsFinite(
                    options.MinimumSeparation)
                || options.MinimumSeparation < 0.0)
            {
                throw new ArgumentException(
                    "Model key-point extraction options are invalid.");
            }

            HashSet<int> sourceOrders = new HashSet<int>();
            for (int index = 0; index < samples.Count; index++)
            {
                if ((index & 1023) == 0) cancellationToken.ThrowIfCancellationRequested();
                ModelKeyPointInput sample = samples[index];
                if (sample == null
                    || sample.SourceSampleOrder < 0
                    || !sourceOrders.Add(sample.SourceSampleOrder)
                    || sample.Position == null
                    || !sample.Position.IsFinite
                    || sample.Normal == null
                    || !sample.Normal.IsFinite
                    || !IsUnit(sample.Normal))
                {
                    throw new ArgumentException(
                        "Model key-point samples require unique non-negative orders, finite positions, and unit normals.");
                }
            }

            cancellationToken.ThrowIfCancellationRequested();
        }

        private static bool IsUnit(ThreeDPoint value)
        {
            double length = Math.Sqrt(
                value.X * value.X
                + value.Y * value.Y
                + value.Z * value.Z);
            return SurfaceMatchingContractValidation.IsFinite(length)
                && Math.Abs(length - 1.0) <= 1e-6;
        }

        private static double Distance(ThreeDPoint first, ThreeDPoint second)
        {
            double x = Math.Abs(first.X - second.X);
            double y = Math.Abs(first.Y - second.Y);
            double z = Math.Abs(first.Z - second.Z);
            double scale = Math.Max(x, Math.Max(y, z));
            if (scale == 0.0)
            {
                return 0.0;
            }

            x /= scale;
            y /= scale;
            z /= scale;
            return scale * Math.Sqrt(x * x + y * y + z * z);
        }
    }
}
