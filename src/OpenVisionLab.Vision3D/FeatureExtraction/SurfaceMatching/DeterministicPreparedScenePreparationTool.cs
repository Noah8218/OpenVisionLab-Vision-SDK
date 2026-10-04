using System;
using System.Collections.Generic;
using System.Threading;

namespace OpenVisionLab.Vision3D.FeatureExtraction
{
    public sealed class PreparedScenePointSample
    {
        public PreparedScenePointSample(
            int order,
            int sourcePointIndex,
            ThreeDPoint position)
        {
            Order = order;
            SourcePointIndex = sourcePointIndex;
            Position = position;
        }

        public int Order { get; }
        public int SourcePointIndex { get; }
        public ThreeDPoint Position { get; }
    }

    public sealed class DeterministicPreparedScenePreparationOptions
    {
        public int MaximumSampleCount { get; set; }
        /// <summary>False retains even-index selection. True sorts by XYZ and reuses farthest-position selection; duplicate positions may produce fewer samples.</summary>
        public bool UseSpatiallyBalancedSampling { get; set; }
        /// <summary>Required positive n*min(n,MaximumSampleCount) distance-comparison budget in spatial mode; null in default mode. Sorting costs are additional.</summary>
        public long? MaximumSpatialComparisonCount { get; set; }
    }

    public sealed class DeterministicPreparedScenePreparationResult
    {
        private DeterministicPreparedScenePreparationResult(
            bool success,
            string message,
            IReadOnlyList<PreparedScenePointSample> samples,
            string samplingSemantics = "")
        {
            Success = success;
            Message = message ?? string.Empty;
            Samples = samples ?? Array.Empty<PreparedScenePointSample>();
            SamplingSemantics = samplingSemantics;
        }

        public bool Success { get; }
        public string Message { get; }
        public IReadOnlyList<PreparedScenePointSample> Samples { get; }
        public string SamplingSemantics { get; }

        internal static DeterministicPreparedScenePreparationResult Completed(
            IReadOnlyList<PreparedScenePointSample> samples,
            string samplingSemantics = DeterministicPreparedScenePreparationTool.Semantics)
        {
            return new DeterministicPreparedScenePreparationResult(
                true,
                string.Empty,
                samples,
                samplingSemantics);
        }

        internal static DeterministicPreparedScenePreparationResult Failed(
            string message)
        {
            return new DeterministicPreparedScenePreparationResult(
                false,
                message,
                Array.Empty<PreparedScenePointSample>());
        }
    }

    /// <summary>
    /// Selects a deterministic even-index subset from an already-admitted
    /// finite scene. Source quality, identity, unit, frame, and persistence
    /// remain with the caller.
    /// </summary>
    public sealed class DeterministicPreparedScenePreparationTool
    {
        public const string Semantics =
            "deterministic-even-point-index-v1";
        public const string SpatialSamplingSemantics = "lexicographic-farthest-position-v1";

        public DeterministicPreparedScenePreparationResult Execute(
            IReadOnlyList<ThreeDPoint> finitePoints,
            DeterministicPreparedScenePreparationOptions options,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            try
            {
                if (finitePoints == null || finitePoints.Count == 0)
                {
                    throw new ArgumentException(
                        "Prepared-scene preparation requires finite source points.");
                }

                if (options == null || options.MaximumSampleCount <= 0)
                {
                    throw new ArgumentException(
                        "Prepared-scene preparation requires a positive maximum sample count.");
                }

                for (int pointIndex = 0;
                     pointIndex < finitePoints.Count;
                     pointIndex++)
                {
                    if ((pointIndex & 1023) == 0) cancellationToken.ThrowIfCancellationRequested();
                    if (finitePoints[pointIndex] == null
                        || !finitePoints[pointIndex].IsFinite)
                    {
                        throw new ArgumentException(
                            "Prepared-scene preparation requires finite XYZ points.");
                    }
                }

                int sampleCount = Math.Min(
                    options.MaximumSampleCount,
                    finitePoints.Count);
                if (options.UseSpatiallyBalancedSampling)
                {
                    if (!options.MaximumSpatialComparisonCount.HasValue || options.MaximumSpatialComparisonCount.Value < 1
                        || (long)finitePoints.Count * sampleCount > options.MaximumSpatialComparisonCount.Value)
                        throw new ArgumentException("Spatial scene preparation exceeds or lacks its declared distance-comparison budget.");
                    List<int> sourceIndices = new List<int>(finitePoints.Count);
                    for (int index = 0; index < finitePoints.Count; index++)
                    {
                        if ((index & 1023) == 0) cancellationToken.ThrowIfCancellationRequested();
                        sourceIndices.Add(index);
                    }
                    int comparisons = 0;
                    sourceIndices.Sort((first, second) =>
                    {
                        if ((comparisons++ & 1023) == 0) cancellationToken.ThrowIfCancellationRequested();
                        ThreeDPoint a = finitePoints[first], b = finitePoints[second];
                        int order = a.X.CompareTo(b.X);
                        if (order == 0) order = a.Y.CompareTo(b.Y);
                        if (order == 0) order = a.Z.CompareTo(b.Z);
                        return order == 0 ? first.CompareTo(second) : order;
                    });
                    List<ThreeDPoint> positions = new List<ThreeDPoint>(finitePoints.Count);
                    for (int index = 0; index < sourceIndices.Count; index++)
                    {
                        if ((index & 1023) == 0) cancellationToken.ThrowIfCancellationRequested();
                        positions.Add(finitePoints[sourceIndices[index]]);
                    }
                    List<KeyValuePair<int, double>> selected = DeterministicModelKeyPointExtractionTool.SelectPositionIndices(
                        positions, sampleCount, 0, cancellationToken);
                    PreparedScenePointSample[] spatial = new PreparedScenePointSample[selected.Count];
                    for (int index = 0; index < spatial.Length; index++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        int sourceIndex = sourceIndices[selected[index].Key];
                        spatial[index] = new PreparedScenePointSample(index, sourceIndex, finitePoints[sourceIndex]);
                    }
                    cancellationToken.ThrowIfCancellationRequested();
                    return DeterministicPreparedScenePreparationResult.Completed(Array.AsReadOnly(spatial), SpatialSamplingSemantics);
                }
                if (options.MaximumSpatialComparisonCount.HasValue)
                    throw new ArgumentException("The spatial comparison budget requires explicit spatial scene sampling.");
                PreparedScenePointSample[] samples =
                    new PreparedScenePointSample[sampleCount];
                for (int sampleOrder = 0;
                     sampleOrder < sampleCount;
                     sampleOrder++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    int sourcePointIndex = checked((int)(
                        ((long)sampleOrder * 2L + 1L)
                        * finitePoints.Count
                        / (sampleCount * 2L)));
                    samples[sampleOrder] = new PreparedScenePointSample(
                        sampleOrder,
                        sourcePointIndex,
                        finitePoints[sourcePointIndex]);
                }

                return DeterministicPreparedScenePreparationResult.Completed(
                    samples);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                cancellationToken.ThrowIfCancellationRequested();
                return DeterministicPreparedScenePreparationResult.Failed(
                    "Deterministic prepared-scene preparation failed: "
                    + exception.Message);
            }
        }
    }
}
