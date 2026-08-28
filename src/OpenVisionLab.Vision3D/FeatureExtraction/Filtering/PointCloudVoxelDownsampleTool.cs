using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace OpenVisionLab.Vision3D.FeatureExtraction
{
    /// <summary>
    /// Explicit options for deterministic point-cloud voxel reduction. The
    /// caller owns the coordinate unit and frame; this tool only applies the
    /// authored origin and positive edge length.
    /// </summary>
    public sealed class PointCloudVoxelDownsampleOptions
    {
        public double VoxelEdgeLength { get; set; }

        public double OriginX { get; set; }

        public double OriginY { get; set; }

        public double OriginZ { get; set; }
    }

    /// <summary>
    /// One representative point retained for an occupied voxel.
    /// </summary>
    public sealed class PointCloudVoxelDownsamplePoint
    {
        internal PointCloudVoxelDownsamplePoint(
            int sourceIndex,
            ThreeDPoint point,
            long voxelX,
            long voxelY,
            long voxelZ)
        {
            SourceIndex = sourceIndex;
            Point = point;
            VoxelX = voxelX;
            VoxelY = voxelY;
            VoxelZ = voxelZ;
        }

        public int SourceIndex { get; }

        public ThreeDPoint Point { get; }

        public long VoxelX { get; }

        public long VoxelY { get; }

        public long VoxelZ { get; }
    }

    /// <summary>
    /// Inclusive XYZ bounds for a finite point-cloud sequence.
    /// </summary>
    public sealed class PointCloudVoxelDownsampleBounds
    {
        internal PointCloudVoxelDownsampleBounds(
            double minimumX,
            double minimumY,
            double minimumZ,
            double maximumX,
            double maximumY,
            double maximumZ)
        {
            MinimumX = minimumX;
            MinimumY = minimumY;
            MinimumZ = minimumZ;
            MaximumX = maximumX;
            MaximumY = maximumY;
            MaximumZ = maximumZ;
        }

        public double MinimumX { get; }

        public double MinimumY { get; }

        public double MinimumZ { get; }

        public double MaximumX { get; }

        public double MaximumY { get; }

        public double MaximumZ { get; }
    }

    public sealed class PointCloudVoxelDownsampleResult
    {
        internal PointCloudVoxelDownsampleResult(
            bool success,
            string message,
            IReadOnlyList<PointCloudVoxelDownsamplePoint> representatives,
            double voxelEdgeLength,
            double originX,
            double originY,
            double originZ,
            int inputPointCount,
            int outputPointCount,
            PointCloudVoxelDownsampleBounds inputBounds,
            PointCloudVoxelDownsampleBounds outputBounds)
        {
            Success = success;
            Message = message ?? string.Empty;
            Representatives = representatives ?? new PointCloudVoxelDownsamplePoint[0];
            VoxelEdgeLength = voxelEdgeLength;
            OriginX = originX;
            OriginY = originY;
            OriginZ = originZ;
            InputPointCount = inputPointCount;
            OutputPointCount = outputPointCount;
            ReducedPointCount = inputPointCount - outputPointCount;
            InputBounds = inputBounds;
            OutputBounds = outputBounds;
        }

        public bool Success { get; }

        public string Message { get; }

        public IReadOnlyList<PointCloudVoxelDownsamplePoint> Representatives { get; }

        public double VoxelEdgeLength { get; }

        public double OriginX { get; }

        public double OriginY { get; }

        public double OriginZ { get; }

        public int InputPointCount { get; }

        public int OutputPointCount { get; }

        public int ReducedPointCount { get; }

        public PointCloudVoxelDownsampleBounds InputBounds { get; }

        public PointCloudVoxelDownsampleBounds OutputBounds { get; }
    }

    /// <summary>
    /// Reduces finite XYZ points to one first-source representative per
    /// occupied voxel. Voxel indices are floor((coordinate - origin) /
    /// edge), the origin is never inferred, and representative order follows
    /// first source appearance. No interpolation, averaging, alignment, or
    /// source mutation is performed.
    /// </summary>
    public sealed class PointCloudVoxelDownsampleTool
    {
        public const string VoxelIndexPolicyName = "FloorFromExplicitOrigin";

        public const string RepresentativePolicyName = "FirstSourcePoint";

        public const string OutputOrderPolicyName = "FirstSourceAppearance";

        public PointCloudVoxelDownsampleResult Execute(
            IReadOnlyList<ThreeDPoint> points,
            PointCloudVoxelDownsampleOptions options,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            try
            {
                Validate(points, options);
                cancellationToken.ThrowIfCancellationRequested();

                var representatives = new List<PointCloudVoxelDownsamplePoint>(points.Count);
                var occupied = new Dictionary<VoxelIndex, int>();
                var inputBounds = BoundsAccumulator.Start(points[0]);
                var outputBounds = BoundsAccumulator.Start(points[0]);
                for (var sourceIndex = 0; sourceIndex < points.Count; sourceIndex++)
                {
                    if ((sourceIndex & 0x3ff) == 0)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                    }

                    var point = points[sourceIndex];
                    inputBounds.Include(point);
                    var voxel = new VoxelIndex(
                        CalculateVoxelIndex(point.X, options.OriginX, options.VoxelEdgeLength, "X"),
                        CalculateVoxelIndex(point.Y, options.OriginY, options.VoxelEdgeLength, "Y"),
                        CalculateVoxelIndex(point.Z, options.OriginZ, options.VoxelEdgeLength, "Z"));
                    if (occupied.ContainsKey(voxel))
                    {
                        continue;
                    }

                    occupied.Add(voxel, sourceIndex);
                    representatives.Add(
                        new PointCloudVoxelDownsamplePoint(
                            sourceIndex,
                            point,
                            voxel.X,
                            voxel.Y,
                            voxel.Z));
                    outputBounds.Include(point);
                }

                return new PointCloudVoxelDownsampleResult(
                    true,
                    "Completed deterministic point-cloud voxel reduction using first-source representatives.",
                    representatives.ToArray(),
                    options.VoxelEdgeLength,
                    options.OriginX,
                    options.OriginY,
                    options.OriginZ,
                    points.Count,
                    representatives.Count,
                    inputBounds.ToBounds(),
                    outputBounds.ToBounds());
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (ArgumentException exception)
            {
                return Error(exception.Message);
            }
            catch (InvalidDataException exception)
            {
                return Error(exception.Message);
            }
            catch (OverflowException exception)
            {
                return Error(exception.Message);
            }
        }

        private static void Validate(
            IReadOnlyList<ThreeDPoint> points,
            PointCloudVoxelDownsampleOptions options)
        {
            if (points == null)
            {
                throw new ArgumentNullException(nameof(points));
            }

            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            if (points.Count == 0)
            {
                throw new InvalidDataException("Point-cloud voxel reduction requires at least one point.");
            }

            if (double.IsNaN(options.VoxelEdgeLength)
                || double.IsInfinity(options.VoxelEdgeLength)
                || options.VoxelEdgeLength <= 0.0)
            {
                throw new ArgumentException("Point-cloud voxel edge length must be finite and positive.");
            }

            if (double.IsNaN(options.OriginX)
                || double.IsInfinity(options.OriginX)
                || double.IsNaN(options.OriginY)
                || double.IsInfinity(options.OriginY)
                || double.IsNaN(options.OriginZ)
                || double.IsInfinity(options.OriginZ))
            {
                throw new ArgumentException("Point-cloud voxel origin must contain finite XYZ coordinates.");
            }

            for (var index = 0; index < points.Count; index++)
            {
                var point = points[index];
                if (point == null || !point.IsFinite)
                {
                    throw new InvalidDataException(
                        $"Point-cloud point {index} must contain finite XYZ coordinates.");
                }
            }
        }

        private static long CalculateVoxelIndex(
            double coordinate,
            double origin,
            double edge,
            string axis)
        {
            var quotient = (coordinate - origin) / edge;
            if (double.IsNaN(quotient) || double.IsInfinity(quotient))
            {
                throw new InvalidDataException(
                    $"Point-cloud voxel {axis}-index calculation became non-finite.");
            }

            var floored = Math.Floor(quotient);
            const double minimumLong = -9223372036854775808d;
            const double maximumLongExclusive = 9223372036854775808d;
            if (floored < minimumLong || floored >= maximumLongExclusive)
            {
                throw new InvalidDataException(
                    $"Point-cloud voxel {axis}-index is outside the supported Int64 range.");
            }

            return checked((long)floored);
        }

        private static PointCloudVoxelDownsampleResult Error(string message) =>
            new PointCloudVoxelDownsampleResult(
                false,
                message,
                new PointCloudVoxelDownsamplePoint[0],
                double.NaN,
                double.NaN,
                double.NaN,
                double.NaN,
                0,
                0,
                null,
                null);

        private readonly struct VoxelIndex : IEquatable<VoxelIndex>
        {
            internal VoxelIndex(long x, long y, long z)
            {
                X = x;
                Y = y;
                Z = z;
            }

            internal long X { get; }

            internal long Y { get; }

            internal long Z { get; }

            public bool Equals(VoxelIndex other) => X == other.X && Y == other.Y && Z == other.Z;

            public override bool Equals(object obj) => obj is VoxelIndex && Equals((VoxelIndex)obj);

            public override int GetHashCode()
            {
                unchecked
                {
                    var hash = X.GetHashCode();
                    hash = (hash * 397) ^ Y.GetHashCode();
                    return (hash * 397) ^ Z.GetHashCode();
                }
            }
        }

        private struct BoundsAccumulator
        {
            private double minimumX;
            private double minimumY;
            private double minimumZ;
            private double maximumX;
            private double maximumY;
            private double maximumZ;

            internal static BoundsAccumulator Start(ThreeDPoint point)
            {
                return new BoundsAccumulator
                {
                    minimumX = point.X,
                    minimumY = point.Y,
                    minimumZ = point.Z,
                    maximumX = point.X,
                    maximumY = point.Y,
                    maximumZ = point.Z
                };
            }

            internal void Include(ThreeDPoint point)
            {
                minimumX = Math.Min(minimumX, point.X);
                minimumY = Math.Min(minimumY, point.Y);
                minimumZ = Math.Min(minimumZ, point.Z);
                maximumX = Math.Max(maximumX, point.X);
                maximumY = Math.Max(maximumY, point.Y);
                maximumZ = Math.Max(maximumZ, point.Z);
            }

            internal PointCloudVoxelDownsampleBounds ToBounds() =>
                new PointCloudVoxelDownsampleBounds(
                    minimumX,
                    minimumY,
                    minimumZ,
                    maximumX,
                    maximumY,
                    maximumZ);
        }
    }
}
