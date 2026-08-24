using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace OpenVisionLab.Vision3D.FeatureExtraction
{
    public enum ConnectedRegionPresenceDecision
    {
        NotEvaluated,
        Present,
        Missing
    }

    public enum ConnectedRegionPresenceCoverageDisposition
    {
        NotEvaluated,
        Accepted,
        BelowMinimum
    }

    public enum ConnectedRegionPresenceHeightDisposition
    {
        NotEvaluated,
        Accepted,
        Missing,
        BelowMinimum,
        AboveMaximum
    }

    /// <summary>
    /// Defines explicit finite-coverage and optional mean-height gates for a
    /// connected-region presence check. Height values remain source-neutral;
    /// units and calibration are caller policy.
    /// </summary>
    public sealed class ConnectedRegionPresenceOptions
    {
        public double MinimumFiniteCoverageRatio { get; set; } = 1.0;
        public double? MinimumMeanHeight { get; set; }
        public double? MaximumMeanHeight { get; set; }
    }

    public sealed class ConnectedRegionPresenceFeature
    {
        internal ConnectedRegionPresenceFeature(
            int index,
            int totalCellCount,
            int finiteCellCount,
            double finiteCoverageRatio,
            double? meanHeight,
            ConnectedRegionPresenceCoverageDisposition coverageDisposition,
            ConnectedRegionPresenceHeightDisposition heightDisposition,
            ConnectedRegionPresenceDecision decision)
        {
            Index = index;
            TotalCellCount = totalCellCount;
            FiniteCellCount = finiteCellCount;
            FiniteCoverageRatio = finiteCoverageRatio;
            MeanHeight = meanHeight;
            CoverageDisposition = coverageDisposition;
            HeightDisposition = heightDisposition;
            Decision = decision;
        }

        public int Index { get; }
        public int TotalCellCount { get; }
        public int FiniteCellCount { get; }
        public int MissingCellCount => TotalCellCount - FiniteCellCount;
        public double FiniteCoverageRatio { get; }
        public double? MeanHeight { get; }
        public ConnectedRegionPresenceCoverageDisposition CoverageDisposition { get; }
        public ConnectedRegionPresenceHeightDisposition HeightDisposition { get; }
        public ConnectedRegionPresenceDecision Decision { get; }
    }

    public sealed class ConnectedRegionPresenceResult
    {
        private ConnectedRegionPresenceResult(
            bool success,
            string message,
            IReadOnlyList<ConnectedRegionPresenceFeature> regions,
            int presentRegionCount,
            int missingRegionCount,
            ConnectedRegionPresenceDecision aggregateDecision)
        {
            Success = success;
            Message = message ?? string.Empty;
            Regions = regions ?? new ConnectedRegionPresenceFeature[0];
            PresentRegionCount = presentRegionCount;
            MissingRegionCount = missingRegionCount;
            AggregateDecision = aggregateDecision;
        }

        public bool Success { get; }
        public string Message { get; }
        public IReadOnlyList<ConnectedRegionPresenceFeature> Regions { get; }
        public int RegionCount => Regions.Count;
        public int PresentRegionCount { get; }
        public int MissingRegionCount { get; }
        public ConnectedRegionPresenceDecision AggregateDecision { get; }

        internal static ConnectedRegionPresenceResult Completed(
            IReadOnlyList<ConnectedRegionPresenceFeature> regions,
            int presentRegionCount,
            int missingRegionCount,
            ConnectedRegionPresenceDecision aggregateDecision)
        {
            return new ConnectedRegionPresenceResult(
                true,
                "Completed deterministic connected-region presence check.",
                regions,
                presentRegionCount,
                missingRegionCount,
                aggregateDecision);
        }

        internal static ConnectedRegionPresenceResult Failed(string message)
        {
            return new ConnectedRegionPresenceResult(
                false,
                message,
                new ConnectedRegionPresenceFeature[0],
                0,
                0,
                ConnectedRegionPresenceDecision.NotEvaluated);
        }
    }

    /// <summary>
    /// Evaluates finite height coverage and optional mean-height bounds for
    /// each existing connected region. AggregateDecision is Present when at
    /// least one region is Present; all-region acceptance remains a caller
    /// contract for a later inspection slice.
    /// </summary>
    public sealed class ConnectedRegionPresenceTool
    {
        public ConnectedRegionPresenceResult Execute(
            ConnectedRegionResult connectedRegions,
            int rowCount,
            int columnCount,
            IReadOnlyList<double> values,
            ConnectedRegionPresenceOptions options = null,
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
                ConnectedRegionPresenceOptions resolvedOptions =
                    options ?? new ConnectedRegionPresenceOptions();
                List<ConnectedRegionPresenceFeature> features =
                    new List<ConnectedRegionPresenceFeature>(
                        connectedRegions.Regions.Count);
                int presentRegionCount = 0;
                int missingRegionCount = 0;

                for (int index = 0; index < connectedRegions.Regions.Count; index++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    ConnectedRegionPresenceFeature feature = Calculate(
                        connectedRegions.Regions[index],
                        rowCount,
                        columnCount,
                        values,
                        resolvedOptions,
                        cancellationToken);
                    features.Add(feature);
                    if (feature.Decision == ConnectedRegionPresenceDecision.Present)
                    {
                        presentRegionCount++;
                    }
                    else
                    {
                        missingRegionCount++;
                    }
                }

                ConnectedRegionPresenceDecision aggregateDecision =
                    presentRegionCount > 0
                        ? ConnectedRegionPresenceDecision.Present
                        : ConnectedRegionPresenceDecision.Missing;
                return ConnectedRegionPresenceResult.Completed(
                    Array.AsReadOnly(features.ToArray()),
                    presentRegionCount,
                    missingRegionCount,
                    aggregateDecision);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (ArgumentException exception)
            {
                return ConnectedRegionPresenceResult.Failed(exception.Message);
            }
            catch (InvalidDataException exception)
            {
                return ConnectedRegionPresenceResult.Failed(exception.Message);
            }
            catch (OverflowException exception)
            {
                return ConnectedRegionPresenceResult.Failed(exception.Message);
            }
        }

        private static ConnectedRegionPresenceFeature Calculate(
            ConnectedRegion region,
            int rowCount,
            int columnCount,
            IReadOnlyList<double> values,
            ConnectedRegionPresenceOptions options,
            CancellationToken cancellationToken)
        {
            ValidateRegion(region, rowCount, columnCount);
            int totalCellCount = region.Cells.Count;
            int finiteCellCount = 0;
            double sum = 0.0;
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

                finiteCellCount++;
                sum += value;
                if (!IsFinite(sum))
                {
                    throw new InvalidDataException(
                        "Connected-region presence height sum is not finite.");
                }
            }

            double coverageRatio = finiteCellCount / (double)totalCellCount;
            double? meanHeight = finiteCellCount == 0
                ? (double?)null
                : sum / finiteCellCount;
            if (meanHeight.HasValue && !IsFinite(meanHeight.Value))
            {
                throw new InvalidDataException(
                    "Connected-region presence mean height is not finite.");
            }

            ConnectedRegionPresenceCoverageDisposition coverageDisposition =
                coverageRatio >= options.MinimumFiniteCoverageRatio
                    ? ConnectedRegionPresenceCoverageDisposition.Accepted
                    : ConnectedRegionPresenceCoverageDisposition.BelowMinimum;
            ConnectedRegionPresenceHeightDisposition heightDisposition;
            if (!meanHeight.HasValue)
            {
                heightDisposition =
                    ConnectedRegionPresenceHeightDisposition.Missing;
            }
            else if (options.MinimumMeanHeight.HasValue
                && meanHeight.Value < options.MinimumMeanHeight.Value)
            {
                heightDisposition =
                    ConnectedRegionPresenceHeightDisposition.BelowMinimum;
            }
            else if (options.MaximumMeanHeight.HasValue
                && meanHeight.Value > options.MaximumMeanHeight.Value)
            {
                heightDisposition =
                    ConnectedRegionPresenceHeightDisposition.AboveMaximum;
            }
            else
            {
                heightDisposition =
                    ConnectedRegionPresenceHeightDisposition.Accepted;
            }

            ConnectedRegionPresenceDecision decision =
                coverageDisposition == ConnectedRegionPresenceCoverageDisposition.Accepted
                && heightDisposition == ConnectedRegionPresenceHeightDisposition.Accepted
                    ? ConnectedRegionPresenceDecision.Present
                    : ConnectedRegionPresenceDecision.Missing;
            return new ConnectedRegionPresenceFeature(
                region.Index,
                totalCellCount,
                finiteCellCount,
                coverageRatio,
                meanHeight,
                coverageDisposition,
                heightDisposition,
                decision);
        }

        private static void Validate(
            ConnectedRegionResult connectedRegions,
            int rowCount,
            int columnCount,
            IReadOnlyList<double> values,
            ConnectedRegionPresenceOptions options)
        {
            if (connectedRegions == null)
            {
                throw new ArgumentNullException(nameof(connectedRegions));
            }

            if (!connectedRegions.Success)
            {
                throw new InvalidDataException(
                    "Connected-region presence requires a successful labeling result.");
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
                    "Connected-region presence dimensions and row-major values must agree.");
            }

            ConnectedRegionPresenceOptions resolvedOptions =
                options ?? new ConnectedRegionPresenceOptions();
            if (!IsFinite(resolvedOptions.MinimumFiniteCoverageRatio)
                || resolvedOptions.MinimumFiniteCoverageRatio < 0.0
                || resolvedOptions.MinimumFiniteCoverageRatio > 1.0)
            {
                throw new InvalidDataException(
                    "Connected-region presence coverage threshold must be finite and within [0, 1].");
            }

            if (resolvedOptions.MinimumMeanHeight.HasValue
                && !IsFinite(resolvedOptions.MinimumMeanHeight.Value))
            {
                throw new InvalidDataException(
                    "Connected-region presence minimum mean height must be finite.");
            }

            if (resolvedOptions.MaximumMeanHeight.HasValue
                && !IsFinite(resolvedOptions.MaximumMeanHeight.Value))
            {
                throw new InvalidDataException(
                    "Connected-region presence maximum mean height must be finite.");
            }

            if (resolvedOptions.MinimumMeanHeight.HasValue
                && resolvedOptions.MaximumMeanHeight.HasValue
                && resolvedOptions.MinimumMeanHeight.Value
                    > resolvedOptions.MaximumMeanHeight.Value)
            {
                throw new InvalidDataException(
                    "Connected-region presence height thresholds must be ordered.");
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
                    "Connected-region presence requires a non-empty region.");
            }

            if (region.MinimumRow < 0
                || region.MinimumColumn < 0
                || region.MaximumRow < region.MinimumRow
                || region.MaximumColumn < region.MinimumColumn
                || region.MaximumRow >= rowCount
                || region.MaximumColumn >= columnCount)
            {
                throw new InvalidDataException(
                    "Connected-region presence bounds are outside the source grid.");
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
                        "Connected-region presence cell is outside its declared bounds.");
                }
            }
        }

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }
    }
}
