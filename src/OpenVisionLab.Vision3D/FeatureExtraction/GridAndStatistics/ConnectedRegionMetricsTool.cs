using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace OpenVisionLab.Vision3D.FeatureExtraction
{
    /// <summary>
    /// Declares the source-grid geometry used to turn connected mask cells into
    /// deterministic planar metrics. Origin coordinates identify the first
    /// cell center; pitches must be positive.
    /// </summary>
    public sealed class ConnectedRegionMetricsOptions
    {
        public double OriginX { get; set; }
        public double OriginY { get; set; }
        public double ColumnPitch { get; set; } = 1.0;
        public double RowPitch { get; set; } = 1.0;
    }

    /// <summary>
    /// Geometry-only bounding output around the occupied cell footprints. It
    /// is not a persisted recipe or downstream editable artifact.
    /// </summary>
    public sealed class ConnectedRegionBoundingArtifact
    {
        internal ConnectedRegionBoundingArtifact(
            int minimumRow,
            int minimumColumn,
            int maximumRow,
            int maximumColumn,
            double minimumX,
            double minimumY,
            double maximumX,
            double maximumY)
        {
            MinimumRow = minimumRow;
            MinimumColumn = minimumColumn;
            MaximumRow = maximumRow;
            MaximumColumn = maximumColumn;
            MinimumX = minimumX;
            MinimumY = minimumY;
            MaximumX = maximumX;
            MaximumY = maximumY;
        }

        public int MinimumRow { get; }
        public int MinimumColumn { get; }
        public int MaximumRow { get; }
        public int MaximumColumn { get; }
        public double MinimumX { get; }
        public double MinimumY { get; }
        public double MaximumX { get; }
        public double MaximumY { get; }
        public double Width => MaximumX - MinimumX;
        public double Height => MaximumY - MinimumY;
        public string CoordinateConvention => "GridXGridYCellCenterFootprint";
    }

    public sealed class ConnectedRegionMetric
    {
        internal ConnectedRegionMetric(
            int index,
            int cellCount,
            double area,
            double centerX,
            double centerY,
            bool hasOrientation,
            double orientationDegrees,
            ConnectedRegionBoundingArtifact bounding)
        {
            Index = index;
            CellCount = cellCount;
            Area = area;
            CenterX = centerX;
            CenterY = centerY;
            HasOrientation = hasOrientation;
            OrientationDegrees = orientationDegrees;
            Bounding = bounding;
        }

        public int Index { get; }
        public int CellCount { get; }
        public double Area { get; }
        public double CenterX { get; }
        public double CenterY { get; }
        public bool HasOrientation { get; }
        public double OrientationDegrees { get; }
        public ConnectedRegionBoundingArtifact Bounding { get; }
    }

    public sealed class ConnectedRegionMetricsResult
    {
        private ConnectedRegionMetricsResult(
            bool success,
            string message,
            IReadOnlyList<ConnectedRegionMetric> regions,
            double totalArea)
        {
            Success = success;
            Message = message ?? string.Empty;
            Regions = regions ?? new ConnectedRegionMetric[0];
            TotalArea = totalArea;
        }

        public bool Success { get; }
        public string Message { get; }
        public IReadOnlyList<ConnectedRegionMetric> Regions { get; }
        public int RegionCount => Regions.Count;
        public double TotalArea { get; }

        internal static ConnectedRegionMetricsResult Completed(
            IReadOnlyList<ConnectedRegionMetric> regions,
            double totalArea)
        {
            return new ConnectedRegionMetricsResult(
                true,
                "Completed deterministic connected-region metrics.",
                regions,
                totalArea);
        }

        internal static ConnectedRegionMetricsResult Failed(string message)
        {
            return new ConnectedRegionMetricsResult(
                false,
                message,
                new ConnectedRegionMetric[0],
                0.0);
        }
    }

    /// <summary>
    /// Computes deterministic count, area, centroid, principal orientation,
    /// and cell-footprint bounds for an existing connected-region result.
    /// Orientation is undefined for a point or an isotropic region and is
    /// reported as HasOrientation=false with NaN degrees.
    /// </summary>
    public sealed class ConnectedRegionMetricsTool
    {
        private const double OrientationTolerance = 1e-12;

        public ConnectedRegionMetricsResult Execute(
            ConnectedRegionResult connectedRegions,
            ConnectedRegionMetricsOptions options = null,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            try
            {
                Validate(connectedRegions, options);
                ConnectedRegionMetricsOptions resolvedOptions =
                    options ?? new ConnectedRegionMetricsOptions();
                List<ConnectedRegionMetric> metrics =
                    new List<ConnectedRegionMetric>(connectedRegions.Regions.Count);
                double totalArea = 0.0;

                for (int index = 0; index < connectedRegions.Regions.Count; index++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    ConnectedRegionMetric metric = Calculate(
                        connectedRegions.Regions[index],
                        resolvedOptions,
                        cancellationToken);
                    metrics.Add(metric);
                    totalArea += metric.Area;
                    if (!IsFinite(totalArea))
                    {
                        throw new InvalidDataException(
                            "Connected-region total area is not finite.");
                    }
                }

                return ConnectedRegionMetricsResult.Completed(
                    Array.AsReadOnly(metrics.ToArray()),
                    totalArea);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (ArgumentException exception)
            {
                return ConnectedRegionMetricsResult.Failed(exception.Message);
            }
            catch (InvalidDataException exception)
            {
                return ConnectedRegionMetricsResult.Failed(exception.Message);
            }
            catch (OverflowException exception)
            {
                return ConnectedRegionMetricsResult.Failed(exception.Message);
            }
        }

        private static ConnectedRegionMetric Calculate(
            ConnectedRegion region,
            ConnectedRegionMetricsOptions options,
            CancellationToken cancellationToken)
        {
            if (region == null || region.Cells == null || region.Cells.Count == 0)
            {
                throw new InvalidDataException(
                    "Connected-region metrics require a non-empty region.");
            }

            double baseX = options.OriginX
                + (region.MinimumColumn * options.ColumnPitch);
            double baseY = options.OriginY
                + (region.MinimumRow * options.RowPitch);
            double minimumX = baseX - (options.ColumnPitch * 0.5);
            double minimumY = baseY - (options.RowPitch * 0.5);
            double maximumX = options.OriginX
                + ((region.MaximumColumn + 0.5) * options.ColumnPitch);
            double maximumY = options.OriginY
                + ((region.MaximumRow + 0.5) * options.RowPitch);
            if (!IsFinite(baseX)
                || !IsFinite(baseY)
                || !IsFinite(minimumX)
                || !IsFinite(minimumY)
                || !IsFinite(maximumX)
                || !IsFinite(maximumY))
            {
                throw new InvalidDataException(
                    "Connected-region bounds are not finite.");
            }

            double area = region.CellCount
                * options.ColumnPitch
                * options.RowPitch;
            if (!IsFinite(area))
            {
                throw new InvalidDataException(
                    "Connected-region area is not finite.");
            }

            double meanX = 0.0;
            double meanY = 0.0;
            for (int index = 0; index < region.Cells.Count; index++)
            {
                if ((index & 0x3fff) == 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                }

                HeightGridCell cell = region.Cells[index];
                ValidateCell(region, cell);
                double x = (cell.Column - region.MinimumColumn)
                    * options.ColumnPitch;
                double y = (cell.Row - region.MinimumRow)
                    * options.RowPitch;
                meanX += (x - meanX) / (index + 1);
                meanY += (y - meanY) / (index + 1);
            }

            double centerX = baseX + meanX;
            double centerY = baseY + meanY;
            if (!IsFinite(centerX) || !IsFinite(centerY))
            {
                throw new InvalidDataException(
                    "Connected-region center is not finite.");
            }

            double xx = 0.0;
            double yy = 0.0;
            double xy = 0.0;
            for (int index = 0; index < region.Cells.Count; index++)
            {
                if ((index & 0x3fff) == 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                }

                HeightGridCell cell = region.Cells[index];
                double x = (cell.Column - region.MinimumColumn)
                    * options.ColumnPitch;
                double y = (cell.Row - region.MinimumRow)
                    * options.RowPitch;
                double deltaX = x - meanX;
                double deltaY = y - meanY;
                xx += deltaX * deltaX;
                yy += deltaY * deltaY;
                xy += deltaX * deltaY;
            }

            if (!IsFinite(xx) || !IsFinite(yy) || !IsFinite(xy))
            {
                throw new InvalidDataException(
                    "Connected-region orientation statistics are not finite.");
            }

            bool hasOrientation = false;
            double orientationDegrees = double.NaN;
            double trace = xx + yy;
            double discriminantSquared = ((xx - yy) * (xx - yy))
                + (4.0 * xy * xy);
            if (!IsFinite(trace) || !IsFinite(discriminantSquared))
            {
                throw new InvalidDataException(
                    "Connected-region orientation eigenvalues are not finite.");
            }

            double discriminant = Math.Sqrt(Math.Max(0.0, discriminantSquared));
            double majorEigenvalue = (trace + discriminant) * 0.5;
            double minorEigenvalue = (trace - discriminant) * 0.5;
            double eigenvalueTolerance = OrientationTolerance
                * Math.Max(1.0, trace);
            if (majorEigenvalue > eigenvalueTolerance
                && majorEigenvalue - minorEigenvalue > eigenvalueTolerance)
            {
                orientationDegrees = Math.Atan2(2.0 * xy, xx - yy)
                    * 90.0
                    / Math.PI;
                while (orientationDegrees < 0.0)
                {
                    orientationDegrees += 180.0;
                }

                while (orientationDegrees >= 180.0)
                {
                    orientationDegrees -= 180.0;
                }

                hasOrientation = true;
            }

            return new ConnectedRegionMetric(
                region.Index,
                region.CellCount,
                area,
                centerX,
                centerY,
                hasOrientation,
                orientationDegrees,
                new ConnectedRegionBoundingArtifact(
                    region.MinimumRow,
                    region.MinimumColumn,
                    region.MaximumRow,
                    region.MaximumColumn,
                    minimumX,
                    minimumY,
                    maximumX,
                    maximumY));
        }

        private static void Validate(
            ConnectedRegionResult connectedRegions,
            ConnectedRegionMetricsOptions options)
        {
            if (connectedRegions == null)
            {
                throw new ArgumentNullException(nameof(connectedRegions));
            }

            if (!connectedRegions.Success)
            {
                throw new InvalidDataException(
                    "Connected-region metrics require a successful labeling result.");
            }

            ConnectedRegionMetricsOptions resolvedOptions =
                options ?? new ConnectedRegionMetricsOptions();
            if (!IsFinite(resolvedOptions.OriginX)
                || !IsFinite(resolvedOptions.OriginY)
                || !IsFinite(resolvedOptions.ColumnPitch)
                || resolvedOptions.ColumnPitch <= 0.0
                || !IsFinite(resolvedOptions.RowPitch)
                || resolvedOptions.RowPitch <= 0.0)
            {
                throw new InvalidDataException(
                    "Connected-region metric origin and positive pitches must be finite.");
            }
        }

        private static void ValidateCell(ConnectedRegion region, HeightGridCell cell)
        {
            if (cell.Row < region.MinimumRow
                || cell.Row > region.MaximumRow
                || cell.Column < region.MinimumColumn
                || cell.Column > region.MaximumColumn)
            {
                throw new InvalidDataException(
                    "Connected-region cell is outside its declared bounds.");
            }
        }

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }
    }
}
