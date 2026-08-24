using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace OpenVisionLab.Vision3D.FeatureExtraction
{
    public enum ConnectedRegionFillHeightDecision
    {
        NotEvaluated,
        Accepted,
        Rejected
    }

    public enum ConnectedRegionFillHeightCoverageDisposition
    {
        NotEvaluated,
        Accepted,
        BelowMinimum
    }

    public enum ConnectedRegionFillHeightDisposition
    {
        NotEvaluated,
        Accepted,
        Missing,
        BelowMinimum,
        AboveMaximum
    }

    /// <summary>
    /// Explicit source-grid reference surface expressed as
    /// height = slopeX * column + slopeZ * row + intercept.
    /// </summary>
    public sealed class ConnectedRegionFillHeightReferenceSurface
    {
        public double SlopeX { get; set; }
        public double SlopeZ { get; set; }
        public double Intercept { get; set; }

        internal double Evaluate(int row, int column)
        {
            return SlopeX * column + SlopeZ * row + Intercept;
        }
    }

    /// <summary>
    /// Defines explicit finite-coverage and mean signed fill-height gates.
    /// Positive fill height is raw height minus reference-surface height.
    /// </summary>
    public sealed class ConnectedRegionFillHeightOptions
    {
        public ConnectedRegionFillHeightReferenceSurface ReferenceSurface { get; set; }
        public double MinimumFiniteCoverageRatio { get; set; } = 1.0;
        public double? MinimumMeanFillHeight { get; set; }
        public double? MaximumMeanFillHeight { get; set; }
    }

    public sealed class ConnectedRegionFillHeightFeature
    {
        internal ConnectedRegionFillHeightFeature(
            int index,
            int totalCellCount,
            int finiteCellCount,
            double finiteCoverageRatio,
            double? meanFillHeight,
            double? minimumFillHeight,
            double? maximumFillHeight,
            ConnectedRegionFillHeightCoverageDisposition coverageDisposition,
            ConnectedRegionFillHeightDisposition fillHeightDisposition,
            ConnectedRegionFillHeightDecision decision)
        {
            Index = index;
            TotalCellCount = totalCellCount;
            FiniteCellCount = finiteCellCount;
            FiniteCoverageRatio = finiteCoverageRatio;
            MeanFillHeight = meanFillHeight;
            MinimumFillHeight = minimumFillHeight;
            MaximumFillHeight = maximumFillHeight;
            CoverageDisposition = coverageDisposition;
            FillHeightDisposition = fillHeightDisposition;
            Decision = decision;
        }

        public int Index { get; }
        public int TotalCellCount { get; }
        public int FiniteCellCount { get; }
        public int MissingCellCount => TotalCellCount - FiniteCellCount;
        public double FiniteCoverageRatio { get; }
        public double? MeanFillHeight { get; }
        public double? MinimumFillHeight { get; }
        public double? MaximumFillHeight { get; }
        public ConnectedRegionFillHeightCoverageDisposition CoverageDisposition { get; }
        public ConnectedRegionFillHeightDisposition FillHeightDisposition { get; }
        public ConnectedRegionFillHeightDecision Decision { get; }
    }

    public sealed class ConnectedRegionFillHeightResult
    {
        private ConnectedRegionFillHeightResult(
            bool success,
            string message,
            IReadOnlyList<ConnectedRegionFillHeightFeature> regions,
            int acceptedRegionCount,
            int rejectedRegionCount)
        {
            Success = success;
            Message = message ?? string.Empty;
            Regions = regions ?? new ConnectedRegionFillHeightFeature[0];
            AcceptedRegionCount = acceptedRegionCount;
            RejectedRegionCount = rejectedRegionCount;
        }

        public bool Success { get; }
        public string Message { get; }
        public IReadOnlyList<ConnectedRegionFillHeightFeature> Regions { get; }
        public int RegionCount => Regions.Count;
        public int AcceptedRegionCount { get; }
        public int RejectedRegionCount { get; }

        internal static ConnectedRegionFillHeightResult Completed(
            IReadOnlyList<ConnectedRegionFillHeightFeature> regions,
            int acceptedRegionCount,
            int rejectedRegionCount)
        {
            return new ConnectedRegionFillHeightResult(
                true,
                "Completed deterministic connected-region fill-height analysis.",
                regions,
                acceptedRegionCount,
                rejectedRegionCount);
        }

        internal static ConnectedRegionFillHeightResult Failed(string message)
        {
            return new ConnectedRegionFillHeightResult(
                false,
                message,
                new ConnectedRegionFillHeightFeature[0],
                0,
                0);
        }
    }

    /// <summary>
    /// Evaluates finite height samples from each existing connected region
    /// against one explicit source-grid reference surface. It does not fit,
    /// mutate, or identify the reference surface.
    /// </summary>
    public sealed class ConnectedRegionFillHeightTool
    {
        public ConnectedRegionFillHeightResult Execute(
            ConnectedRegionResult connectedRegions,
            int rowCount,
            int columnCount,
            IReadOnlyList<double> values,
            ConnectedRegionFillHeightOptions options = null,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            try
            {
                Validate(
                    connectedRegions,
                    rowCount,
                    columnCount,
                    values,
                    options);
                ConnectedRegionFillHeightOptions resolvedOptions =
                    options ?? new ConnectedRegionFillHeightOptions();
                List<ConnectedRegionFillHeightFeature> features =
                    new List<ConnectedRegionFillHeightFeature>(
                        connectedRegions.Regions.Count);
                int acceptedRegionCount = 0;
                int rejectedRegionCount = 0;

                for (int index = 0; index < connectedRegions.Regions.Count; index++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    ConnectedRegionFillHeightFeature feature = Calculate(
                        connectedRegions.Regions[index],
                        rowCount,
                        columnCount,
                        values,
                        resolvedOptions,
                        cancellationToken);
                    features.Add(feature);
                    if (feature.Decision == ConnectedRegionFillHeightDecision.Accepted)
                    {
                        acceptedRegionCount++;
                    }
                    else
                    {
                        rejectedRegionCount++;
                    }
                }

                return ConnectedRegionFillHeightResult.Completed(
                    Array.AsReadOnly(features.ToArray()),
                    acceptedRegionCount,
                    rejectedRegionCount);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (ArgumentException exception)
            {
                return ConnectedRegionFillHeightResult.Failed(exception.Message);
            }
            catch (InvalidDataException exception)
            {
                return ConnectedRegionFillHeightResult.Failed(exception.Message);
            }
            catch (OverflowException exception)
            {
                return ConnectedRegionFillHeightResult.Failed(exception.Message);
            }
        }

        private static ConnectedRegionFillHeightFeature Calculate(
            ConnectedRegion region,
            int rowCount,
            int columnCount,
            IReadOnlyList<double> values,
            ConnectedRegionFillHeightOptions options,
            CancellationToken cancellationToken)
        {
            ValidateRegion(region, rowCount, columnCount);
            int totalCellCount = region.Cells.Count;
            int finiteCellCount = 0;
            double sum = 0.0;
            double minimum = double.PositiveInfinity;
            double maximum = double.NegativeInfinity;
            for (int index = 0; index < totalCellCount; index++)
            {
                if ((index & 0x3fff) == 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                }

                HeightGridCell cell = region.Cells[index];
                double value = values[checked(cell.Row * columnCount + cell.Column)];
                if (!IsFinite(value))
                {
                    continue;
                }

                double referenceHeight = options.ReferenceSurface.Evaluate(
                    cell.Row,
                    cell.Column);
                double fillHeight = value - referenceHeight;
                if (!IsFinite(fillHeight))
                {
                    throw new InvalidDataException(
                        "Connected-region fill height is not finite.");
                }

                finiteCellCount++;
                sum += fillHeight;
                minimum = Math.Min(minimum, fillHeight);
                maximum = Math.Max(maximum, fillHeight);
                if (!IsFinite(sum))
                {
                    throw new InvalidDataException(
                        "Connected-region fill-height sum is not finite.");
                }
            }

            double coverageRatio = finiteCellCount / (double)totalCellCount;
            double? meanFillHeight = finiteCellCount == 0
                ? (double?)null
                : sum / finiteCellCount;
            double? minimumFillHeight = finiteCellCount == 0
                ? (double?)null
                : minimum;
            double? maximumFillHeight = finiteCellCount == 0
                ? (double?)null
                : maximum;
            if (meanFillHeight.HasValue && !IsFinite(meanFillHeight.Value))
            {
                throw new InvalidDataException(
                    "Connected-region mean fill height is not finite.");
            }

            ConnectedRegionFillHeightCoverageDisposition coverageDisposition =
                coverageRatio >= options.MinimumFiniteCoverageRatio
                    ? ConnectedRegionFillHeightCoverageDisposition.Accepted
                    : ConnectedRegionFillHeightCoverageDisposition.BelowMinimum;
            ConnectedRegionFillHeightDisposition fillHeightDisposition;
            if (!meanFillHeight.HasValue)
            {
                fillHeightDisposition = ConnectedRegionFillHeightDisposition.Missing;
            }
            else if (options.MinimumMeanFillHeight.HasValue
                && meanFillHeight.Value < options.MinimumMeanFillHeight.Value)
            {
                fillHeightDisposition = ConnectedRegionFillHeightDisposition.BelowMinimum;
            }
            else if (options.MaximumMeanFillHeight.HasValue
                && meanFillHeight.Value > options.MaximumMeanFillHeight.Value)
            {
                fillHeightDisposition = ConnectedRegionFillHeightDisposition.AboveMaximum;
            }
            else
            {
                fillHeightDisposition = ConnectedRegionFillHeightDisposition.Accepted;
            }

            ConnectedRegionFillHeightDecision decision =
                coverageDisposition == ConnectedRegionFillHeightCoverageDisposition.Accepted
                && fillHeightDisposition == ConnectedRegionFillHeightDisposition.Accepted
                    ? ConnectedRegionFillHeightDecision.Accepted
                    : ConnectedRegionFillHeightDecision.Rejected;
            return new ConnectedRegionFillHeightFeature(
                region.Index,
                totalCellCount,
                finiteCellCount,
                coverageRatio,
                meanFillHeight,
                minimumFillHeight,
                maximumFillHeight,
                coverageDisposition,
                fillHeightDisposition,
                decision);
        }

        private static void Validate(
            ConnectedRegionResult connectedRegions,
            int rowCount,
            int columnCount,
            IReadOnlyList<double> values,
            ConnectedRegionFillHeightOptions options)
        {
            if (connectedRegions == null)
            {
                throw new ArgumentNullException(nameof(connectedRegions));
            }

            if (!connectedRegions.Success)
            {
                throw new InvalidDataException(
                    "Connected-region fill height requires a successful labeling result.");
            }

            if (values == null)
            {
                throw new ArgumentNullException(nameof(values));
            }

            if (rowCount <= 0
                || columnCount <= 0
                || values.Count != checked(rowCount * columnCount))
            {
                throw new InvalidDataException(
                    "Connected-region fill-height dimensions and row-major values must agree.");
            }

            ConnectedRegionFillHeightOptions resolvedOptions =
                options ?? new ConnectedRegionFillHeightOptions();
            if (resolvedOptions.ReferenceSurface == null
                || !IsFinite(resolvedOptions.ReferenceSurface.SlopeX)
                || !IsFinite(resolvedOptions.ReferenceSurface.SlopeZ)
                || !IsFinite(resolvedOptions.ReferenceSurface.Intercept))
            {
                throw new InvalidDataException(
                    "Connected-region fill-height reference surface coefficients must be finite.");
            }

            if (!IsFinite(resolvedOptions.MinimumFiniteCoverageRatio)
                || resolvedOptions.MinimumFiniteCoverageRatio < 0.0
                || resolvedOptions.MinimumFiniteCoverageRatio > 1.0)
            {
                throw new InvalidDataException(
                    "Connected-region fill-height coverage threshold must be finite and within [0, 1].");
            }

            if (resolvedOptions.MinimumMeanFillHeight.HasValue
                && !IsFinite(resolvedOptions.MinimumMeanFillHeight.Value))
            {
                throw new InvalidDataException(
                    "Connected-region minimum mean fill height must be finite.");
            }

            if (resolvedOptions.MaximumMeanFillHeight.HasValue
                && !IsFinite(resolvedOptions.MaximumMeanFillHeight.Value))
            {
                throw new InvalidDataException(
                    "Connected-region maximum mean fill height must be finite.");
            }

            if (resolvedOptions.MinimumMeanFillHeight.HasValue
                && resolvedOptions.MaximumMeanFillHeight.HasValue
                && resolvedOptions.MinimumMeanFillHeight.Value
                    > resolvedOptions.MaximumMeanFillHeight.Value)
            {
                throw new InvalidDataException(
                    "Connected-region fill-height thresholds must be ordered.");
            }
        }

        private static void ValidateRegion(
            ConnectedRegion region,
            int rowCount,
            int columnCount)
        {
            if (region == null || region.Cells == null || region.Cells.Count == 0)
            {
                throw new InvalidDataException(
                    "Connected-region fill height requires a non-empty region.");
            }

            if (region.MinimumRow < 0
                || region.MinimumColumn < 0
                || region.MaximumRow < region.MinimumRow
                || region.MaximumColumn < region.MinimumColumn
                || region.MaximumRow >= rowCount
                || region.MaximumColumn >= columnCount)
            {
                throw new InvalidDataException(
                    "Connected-region fill-height bounds are outside the source grid.");
            }

            for (int index = 0; index < region.Cells.Count; index++)
            {
                HeightGridCell cell = region.Cells[index];
                if (cell.Row < region.MinimumRow
                    || cell.Row > region.MaximumRow
                    || cell.Column < region.MinimumColumn
                    || cell.Column > region.MaximumColumn)
                {
                    throw new InvalidDataException(
                        "Connected-region fill-height cell is outside its declared bounds.");
                }
            }
        }

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }
    }
}
