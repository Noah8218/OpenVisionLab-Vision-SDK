using System;
using System.Collections.Generic;
using System.Threading;

namespace OpenVisionLab.Vision3D.FeatureExtraction
{
    /// <summary>Execution-local ordered interval index on the widest coordinate axis. Samples must remain unchanged until the owning execution returns.</summary>
    internal sealed class SurfaceSceneIndex
    {
        private readonly SurfaceMatchSample[] sorted;
        private readonly int axis;
        internal IReadOnlyList<SurfaceMatchSample> Samples { get; }
        internal bool IsIndexed => sorted != null;

        internal SurfaceSceneIndex(IReadOnlyList<SurfaceMatchSample> samples, long expectedQueryCount, CancellationToken cancellationToken)
        {
            Samples = samples;
            cancellationToken.ThrowIfCancellationRequested();
            // ponytail: widest-axis interval pruning still has a linear worst case; add a 3D tree only for measured multi-axis concentration.
            if (samples.Count < 64 || expectedQueryCount < 32) return;
            ThreeDPoint first = samples[0].Position;
            double minX = first.X, maxX = first.X, minY = first.Y, maxY = first.Y, minZ = first.Z, maxZ = first.Z;
            for (int index = 1; index < samples.Count; index++)
            {
                if ((index & 1023) == 0) cancellationToken.ThrowIfCancellationRequested();
                ThreeDPoint point = samples[index].Position;
                minX = Math.Min(minX, point.X); maxX = Math.Max(maxX, point.X);
                minY = Math.Min(minY, point.Y); maxY = Math.Max(maxY, point.Y);
                minZ = Math.Min(minZ, point.Z); maxZ = Math.Max(maxZ, point.Z);
            }
            // Half-spans preserve ordering without overflowing a finite coordinate range.
            double spanX = maxX * 0.5 - minX * 0.5;
            double spanY = maxY * 0.5 - minY * 0.5;
            double spanZ = maxZ * 0.5 - minZ * 0.5;
            if (spanX == 0.0 && spanY == 0.0 && spanZ == 0.0) return;
            axis = spanY > spanX ? 1 : 0;
            if (spanZ > (axis == 0 ? spanX : spanY)) axis = 2;
            sorted = new SurfaceMatchSample[samples.Count];
            for (int index = 0; index < sorted.Length; index++)
            {
                if ((index & 1023) == 0) cancellationToken.ThrowIfCancellationRequested();
                sorted[index] = samples[index];
            }

            Sort(sorted, cancellationToken);
        }

        private void Sort(SurfaceMatchSample[] sorted, CancellationToken cancellationToken)
        {
            int comparisons = 0;
            try
            {
                Array.Sort(sorted, (first, second) =>
                {
                    if ((comparisons++ & 1023) == 0) cancellationToken.ThrowIfCancellationRequested();
                    int order = Coordinate(first.Position).CompareTo(Coordinate(second.Position));
                    return order == 0 ? first.Order.CompareTo(second.Order) : order;
                });
            }
            catch (InvalidOperationException exception) when (exception.InnerException is OperationCanceledException)
            {
                cancellationToken.ThrowIfCancellationRequested();
                throw;
            }

            cancellationToken.ThrowIfCancellationRequested();
        }

        private double Coordinate(ThreeDPoint point) => axis == 0 ? point.X : axis == 1 ? point.Y : point.Z;

        internal int FindNearest(ThreeDPoint point, bool[] claimed, double maximumDistance,
            CancellationToken cancellationToken, out double bestDistance)
        {
            bestDistance = double.PositiveInfinity;
            if (!point.IsFinite) return -1;
            int bestOrder = -1;
            double queryCoordinate = Coordinate(point);
            int start = 0;
            {
                int end = sorted.Length;
                while (start < end)
                {
                    int middle = start + (end - start) / 2;
                    // Subtract directly instead of coordinate-radius endpoints: this preserves inclusive limits even at extreme magnitudes.
                    double delta = Coordinate(sorted[middle].Position) - queryCoordinate;
                    if (delta < 0 && Math.Abs(delta) > maximumDistance) start = middle + 1;
                    else end = middle;
                }
            }

            for (int index = start; index < sorted.Length; index++)
            {
                if ((index & 1023) == 0) cancellationToken.ThrowIfCancellationRequested();
                SurfaceMatchSample sample = sorted[index];
                if (Coordinate(sample.Position) - queryCoordinate > maximumDistance) break;
                if (claimed[sample.Order]) continue;
                double distance = Distance(point, sample.Position);
                if (distance < bestDistance || distance == bestDistance && sample.Order < bestOrder)
                {
                    bestDistance = distance;
                    bestOrder = sample.Order;
                }
            }

            cancellationToken.ThrowIfCancellationRequested();
            return bestOrder;
        }

        internal static double Distance(ThreeDPoint first, ThreeDPoint second)
        {
            double x = Math.Abs(first.X - second.X);
            double y = Math.Abs(first.Y - second.Y);
            double z = Math.Abs(first.Z - second.Z);
            double scale = Math.Max(x, Math.Max(y, z));
            if (!SurfaceMatchingContractValidation.IsFinite(scale)) return double.PositiveInfinity;
            if (scale == 0.0) return 0.0;
            x /= scale;
            y /= scale;
            z /= scale;
            return scale * Math.Sqrt(x * x + y * y + z * z);
        }
    }
}
