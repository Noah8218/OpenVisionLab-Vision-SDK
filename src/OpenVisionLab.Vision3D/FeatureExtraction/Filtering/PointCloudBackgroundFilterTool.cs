using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace OpenVisionLab.Vision3D.FeatureExtraction
{
    public enum PointCloudBackgroundFilterMode
    {
        RemoveAtOrBelowDistance
    }

    public sealed class PointCloudBackgroundFilterOptions
    {
        public PointCloudBackgroundFilterMode Mode { get; set; } =
            PointCloudBackgroundFilterMode.RemoveAtOrBelowDistance;

        public double MaximumBackgroundDistance { get; set; }
    }

    /// <summary>
    /// One retained current point and its nearest saved-background distance.
    /// </summary>
    public sealed class PointCloudBackgroundFilterPoint
    {
        internal PointCloudBackgroundFilterPoint(
            int sourceIndex,
            ThreeDPoint point,
            double nearestBackgroundDistance)
        {
            SourceIndex = sourceIndex;
            Point = point;
            NearestBackgroundDistance = nearestBackgroundDistance;
        }

        public int SourceIndex { get; }

        public ThreeDPoint Point { get; }

        public double NearestBackgroundDistance { get; }
    }

    public sealed class PointCloudBackgroundFilterResult
    {
        internal PointCloudBackgroundFilterResult(
            bool success,
            string message,
            IReadOnlyList<PointCloudBackgroundFilterPoint> retainedPoints,
            PointCloudBackgroundFilterMode mode,
            double maximumBackgroundDistance,
            int inputPointCount,
            int backgroundPointCount,
            int retainedPointCount,
            int removedPointCount,
            double minimumNearestBackgroundDistance,
            double maximumNearestBackgroundDistance,
            double meanNearestBackgroundDistance)
        {
            Success = success;
            Message = message ?? string.Empty;
            RetainedPoints = retainedPoints ?? new PointCloudBackgroundFilterPoint[0];
            Mode = mode;
            MaximumBackgroundDistance = maximumBackgroundDistance;
            InputPointCount = inputPointCount;
            BackgroundPointCount = backgroundPointCount;
            RetainedPointCount = retainedPointCount;
            RemovedPointCount = removedPointCount;
            MinimumNearestBackgroundDistance = minimumNearestBackgroundDistance;
            MaximumNearestBackgroundDistance = maximumNearestBackgroundDistance;
            MeanNearestBackgroundDistance = meanNearestBackgroundDistance;
        }

        public bool Success { get; }

        public string Message { get; }

        public IReadOnlyList<PointCloudBackgroundFilterPoint> RetainedPoints { get; }

        public PointCloudBackgroundFilterMode Mode { get; }

        public double MaximumBackgroundDistance { get; }

        public int InputPointCount { get; }

        public int BackgroundPointCount { get; }

        public int RetainedPointCount { get; }

        public int RemovedPointCount { get; }

        public double MinimumNearestBackgroundDistance { get; }

        public double MaximumNearestBackgroundDistance { get; }

        public double MeanNearestBackgroundDistance { get; }
    }

    /// <summary>
    /// Removes current points that are at or below an explicit Euclidean
    /// distance from the saved background. The retained order and coordinates
    /// are unchanged; no alignment, interpolation, or missing-point policy is
    /// inferred here.
    /// </summary>
    public sealed class PointCloudBackgroundFilterTool
    {
        public PointCloudBackgroundFilterResult Execute(
            IReadOnlyList<ThreeDPoint> current,
            IReadOnlyList<ThreeDPoint> savedBackground,
            PointCloudBackgroundFilterOptions options,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            try
            {
                Validate(current, savedBackground, options);
                cancellationToken.ThrowIfCancellationRequested();

                var retained = new List<PointCloudBackgroundFilterPoint>(current.Count);
                var minimumDistance = double.PositiveInfinity;
                var maximumDistance = double.NegativeInfinity;
                var distanceSum = 0.0;
                var removedCount = 0;
                for (var currentIndex = 0; currentIndex < current.Count; currentIndex++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var currentPoint = current[currentIndex];
                    var minimumSquaredDistance = double.PositiveInfinity;
                    // ponytail: O(N x M) is the smallest deterministic implementation; add a spatial index only when measured cloud sizes require it.
                    for (var backgroundIndex = 0; backgroundIndex < savedBackground.Count; backgroundIndex++)
                    {
                        if ((backgroundIndex & 0x3ff) == 0)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                        }

                        var backgroundPoint = savedBackground[backgroundIndex];
                        var deltaX = currentPoint.X - backgroundPoint.X;
                        var deltaY = currentPoint.Y - backgroundPoint.Y;
                        var deltaZ = currentPoint.Z - backgroundPoint.Z;
                        var squaredDistance = (deltaX * deltaX)
                            + (deltaY * deltaY)
                            + (deltaZ * deltaZ);
                        if (double.IsNaN(squaredDistance) || double.IsInfinity(squaredDistance))
                        {
                            throw new InvalidDataException(
                                "Point-cloud background distance overflowed or became non-finite.");
                        }

                        if (squaredDistance < minimumSquaredDistance)
                        {
                            minimumSquaredDistance = squaredDistance;
                        }
                    }

                    var nearestDistance = Math.Sqrt(minimumSquaredDistance);
                    if (double.IsNaN(nearestDistance) || double.IsInfinity(nearestDistance))
                    {
                        throw new InvalidDataException(
                            "Point-cloud background nearest distance is non-finite.");
                    }

                    minimumDistance = Math.Min(minimumDistance, nearestDistance);
                    maximumDistance = Math.Max(maximumDistance, nearestDistance);
                    distanceSum += nearestDistance;
                    if (options.Mode == PointCloudBackgroundFilterMode.RemoveAtOrBelowDistance
                        && nearestDistance <= options.MaximumBackgroundDistance)
                    {
                        removedCount++;
                        continue;
                    }

                    retained.Add(
                        new PointCloudBackgroundFilterPoint(
                            currentIndex,
                            currentPoint,
                            nearestDistance));
                }

                return new PointCloudBackgroundFilterResult(
                    true,
                    "Completed deterministic Euclidean point-cloud background filtering.",
                    retained.ToArray(),
                    options.Mode,
                    options.MaximumBackgroundDistance,
                    current.Count,
                    savedBackground.Count,
                    retained.Count,
                    removedCount,
                    minimumDistance,
                    maximumDistance,
                    distanceSum / current.Count);
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
            IReadOnlyList<ThreeDPoint> current,
            IReadOnlyList<ThreeDPoint> savedBackground,
            PointCloudBackgroundFilterOptions options)
        {
            if (current == null)
            {
                throw new ArgumentNullException(nameof(current));
            }

            if (savedBackground == null)
            {
                throw new ArgumentNullException(nameof(savedBackground));
            }

            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            if (current.Count == 0)
            {
                throw new InvalidDataException("Point-cloud background filtering requires at least one current point.");
            }

            if (savedBackground.Count == 0)
            {
                throw new InvalidDataException("Point-cloud background filtering requires at least one saved-background point.");
            }

            if (!Enum.IsDefined(typeof(PointCloudBackgroundFilterMode), options.Mode))
            {
                throw new ArgumentException("Point-cloud background filter mode is invalid.");
            }

            if (double.IsNaN(options.MaximumBackgroundDistance)
                || double.IsInfinity(options.MaximumBackgroundDistance)
                || options.MaximumBackgroundDistance < 0.0)
            {
                throw new ArgumentException(
                    "Maximum point-cloud background distance must be finite and non-negative.");
            }

            ValidatePoints(current, "Current");
            ValidatePoints(savedBackground, "Saved background");
        }

        private static void ValidatePoints(
            IReadOnlyList<ThreeDPoint> points,
            string label)
        {
            for (var index = 0; index < points.Count; index++)
            {
                var point = points[index];
                if (point == null
                    || double.IsNaN(point.X) || double.IsInfinity(point.X)
                    || double.IsNaN(point.Y) || double.IsInfinity(point.Y)
                    || double.IsNaN(point.Z) || double.IsInfinity(point.Z))
                {
                    throw new InvalidDataException(
                        $"{label} point {index} must contain finite XYZ coordinates.");
                }
            }
        }

        private static PointCloudBackgroundFilterResult Error(string message) =>
            new PointCloudBackgroundFilterResult(
                false,
                message,
                new PointCloudBackgroundFilterPoint[0],
                default(PointCloudBackgroundFilterMode),
                double.NaN,
                0,
                0,
                0,
                0,
                double.NaN,
                double.NaN,
                double.NaN);
    }
}
