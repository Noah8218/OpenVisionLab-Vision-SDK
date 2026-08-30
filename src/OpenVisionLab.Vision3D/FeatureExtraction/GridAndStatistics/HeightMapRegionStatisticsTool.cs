using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace OpenVisionLab.Vision3D.FeatureExtraction
{
    public sealed class HeightGridRegion
    {
        public HeightGridRegion(
            int row,
            int column,
            int rowCount,
            int columnCount)
        {
            Row = row;
            Column = column;
            RowCount = rowCount;
            ColumnCount = columnCount;
        }

        public int Row { get; }
        public int Column { get; }
        public int RowCount { get; }
        public int ColumnCount { get; }
    }

    public sealed class HeightMapRegionStatisticsResult
    {
        internal HeightMapRegionStatisticsResult(
            bool success,
            string message,
            int totalCellCount,
            int finiteCellCount,
            double sum,
            double minimum,
            double maximum)
        {
            Success = success;
            Message = message ?? string.Empty;
            TotalCellCount = totalCellCount;
            FiniteCellCount = finiteCellCount;
            Sum = sum;
            Minimum = minimum;
            Maximum = maximum;
        }

        public bool Success { get; }
        public string Message { get; }
        public int TotalCellCount { get; }
        public int FiniteCellCount { get; }
        public int MissingCellCount => TotalCellCount - FiniteCellCount;
        public bool HasFiniteSamples => FiniteCellCount > 0;
        public double Sum { get; }
        public double Mean => HasFiniteSamples ? Sum / FiniteCellCount : double.NaN;
        public double Minimum { get; }
        public double Maximum { get; }
        public double FiniteCoverageRatio => TotalCellCount == 0
            ? double.NaN
            : FiniteCellCount / (double)TotalCellCount;
    }

    /// <summary>
    /// Computes deterministic finite-value statistics for one row-major
    /// height-map region. Identity, unit, and acceptance remain caller policy.
    /// </summary>
    public sealed class HeightMapRegionStatisticsTool
    {
        public HeightMapRegionStatisticsResult Execute(
            int rowCount,
            int columnCount,
            IReadOnlyList<double> values,
            HeightGridRegion region,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            return ExecuteCore(
                rowCount,
                columnCount,
                values,
                region,
                null,
                false,
                cancellationToken);
        }

        /// <summary>
        /// Computes deterministic finite-value statistics for the foreground
        /// cells of an exact source-grid mask within one rectangular region.
        /// The mask must match the source-grid dimensions.
        /// </summary>
        public HeightMapRegionStatisticsResult ExecuteMasked(
            int rowCount,
            int columnCount,
            IReadOnlyList<double> values,
            HeightGridRegion region,
            HeightGridMask mask,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            return ExecuteCore(
                rowCount,
                columnCount,
                values,
                region,
                mask,
                true,
                cancellationToken);
        }

        private static HeightMapRegionStatisticsResult ExecuteCore(
            int rowCount,
            int columnCount,
            IReadOnlyList<double> values,
            HeightGridRegion region,
            HeightGridMask mask,
            bool requireMask,
            CancellationToken cancellationToken)
        {
            try
            {
                Validate(rowCount, columnCount, values, region);
                if (requireMask && mask == null)
                {
                    throw new ArgumentNullException(nameof(mask));
                }

                if (mask != null)
                {
                    ValidateMask(rowCount, columnCount, mask);
                }

                int finiteCount = 0;
                int totalCellCount = 0;
                double sum = 0.0;
                double minimum = double.PositiveInfinity;
                double maximum = double.NegativeInfinity;
                for (int row = region.Row;
                     row < region.Row + region.RowCount;
                     row++)
                {
                    for (int column = region.Column;
                         column < region.Column + region.ColumnCount;
                         column++)
                    {
                        int index = checked(row * columnCount + column);
                        if ((index & 0x3fff) == 0)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                        }

                        if (mask != null && !mask.Foreground[index])
                        {
                            continue;
                        }

                        totalCellCount++;

                        double value = values[index];
                        if (!IsFinite(value))
                        {
                            continue;
                        }

                        finiteCount++;
                        sum += value;
                        if (value < minimum)
                        {
                            minimum = value;
                        }

                        if (value > maximum)
                        {
                            maximum = value;
                        }
                    }
                }

                return new HeightMapRegionStatisticsResult(
                    true,
                    "Completed deterministic height-map region statistics.",
                    totalCellCount,
                    finiteCount,
                    sum,
                    finiteCount == 0 ? double.NaN : minimum,
                    finiteCount == 0 ? double.NaN : maximum);
            }
            catch (Exception exception) when (
                exception is ArgumentException
                || exception is InvalidDataException
                || exception is OverflowException)
            {
                return new HeightMapRegionStatisticsResult(
                    false,
                    exception.Message,
                    0,
                    0,
                    0.0,
                    double.NaN,
                    double.NaN);
            }
        }

        internal static void ValidateMask(
            int rowCount,
            int columnCount,
            HeightGridMask mask)
        {
            if (mask == null)
            {
                throw new ArgumentNullException(nameof(mask));
            }

            if (mask.RowCount != rowCount
                || mask.ColumnCount != columnCount)
            {
                throw new InvalidDataException(
                    "Height-map mask dimensions must match the source grid.");
            }

            int expectedCount = checked(rowCount * columnCount);
            if (mask.Foreground == null
                || mask.Foreground.Count != expectedCount)
            {
                throw new InvalidDataException(
                    "Height-map mask values must match the source-grid dimensions.");
            }
        }

        internal static void Validate(
            int rowCount,
            int columnCount,
            IReadOnlyList<double> values,
            HeightGridRegion region)
        {
            if (values == null)
            {
                throw new ArgumentNullException(nameof(values));
            }

            if (region == null)
            {
                throw new ArgumentNullException(nameof(region));
            }

            if (rowCount < 1
                || columnCount < 1
                || values.Count != checked(rowCount * columnCount))
            {
                throw new InvalidDataException(
                    "Height-map dimensions and row-major value count must agree.");
            }

            if (region.Row < 0
                || region.Column < 0
                || region.RowCount < 1
                || region.ColumnCount < 1
                || region.Row > rowCount - region.RowCount
                || region.Column > columnCount - region.ColumnCount)
            {
                throw new InvalidDataException(
                    "Height-map region is outside the source grid.");
            }
        }

        internal static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }
    }
}
