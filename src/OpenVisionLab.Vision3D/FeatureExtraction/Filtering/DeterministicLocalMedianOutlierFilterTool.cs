using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace OpenVisionLab.Vision3D.FeatureExtraction
{
    public sealed class DeterministicLocalMedianOutlierFilterOptions
    {
        public int WindowSize { get; set; }
        public double MaximumAbsoluteDeviation { get; set; }
        public int MinimumValidNeighbors { get; set; }
    }

    public sealed class DeterministicLocalMedianOutlierFilterResult
    {
        private DeterministicLocalMedianOutlierFilterResult(
            bool success,
            string message,
            IReadOnlyList<double> values,
            IReadOnlyList<int> outlierIndices)
        {
            Success = success;
            Message = message ?? string.Empty;
            Values = values ?? Array.Empty<double>();
            OutlierIndices = outlierIndices ?? Array.Empty<int>();
        }

        public bool Success { get; }
        public string Message { get; }
        public IReadOnlyList<double> Values { get; }
        public IReadOnlyList<int> OutlierIndices { get; }

        internal static DeterministicLocalMedianOutlierFilterResult Completed(
            IReadOnlyList<double> values,
            IReadOnlyList<int> outlierIndices)
        {
            return new DeterministicLocalMedianOutlierFilterResult(
                true,
                "Completed deterministic local-median outlier filtering.",
                values,
                outlierIndices);
        }

        internal static DeterministicLocalMedianOutlierFilterResult Failed(
            string message)
        {
            return new DeterministicLocalMedianOutlierFilterResult(
                false,
                message,
                Array.Empty<double>(),
                Array.Empty<int>());
        }
    }

    /// <summary>
    /// Pure row-major local-median absolute-deviation filter. The finite
    /// center is excluded from its neighborhood, borders use available
    /// neighbors, and accepted outliers become NaN in a new output array.
    /// Source identity, units, persistence, and acceptance presentation stay
    /// with the caller.
    /// </summary>
    public sealed class DeterministicLocalMedianOutlierFilterTool
    {
        public const string Semantics =
            "local-median-absolute-deviation-center-excluded-v1";

        public DeterministicLocalMedianOutlierFilterResult Execute(
            int rowCount,
            int columnCount,
            IReadOnlyList<double> values,
            DeterministicLocalMedianOutlierFilterOptions options,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            try
            {
                Validate(rowCount, columnCount, values, options);
                double[] output = new double[values.Count];
                for (int index = 0; index < values.Count; index++)
                {
                    output[index] = values[index];
                }

                List<int> outlierIndices = new List<int>();
                double[] neighbors = new double[checked(
                    options.WindowSize * options.WindowSize - 1)];
                int radius = options.WindowSize / 2;
                for (int row = 0; row < rowCount; row++)
                {
                    for (int column = 0; column < columnCount; column++)
                    {
                        int index = checked(row * columnCount + column);
                        if ((index & 0x3fff) == 0)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                        }

                        double center = values[index];
                        if (!IsFinite(center))
                        {
                            continue;
                        }

                        int neighborCount = 0;
                        int firstRow = Math.Max(0, row - radius);
                        int lastRow = Math.Min(rowCount - 1, row + radius);
                        int firstColumn = Math.Max(0, column - radius);
                        int lastColumn = Math.Min(columnCount - 1, column + radius);
                        for (int neighborRow = firstRow;
                             neighborRow <= lastRow;
                             neighborRow++)
                        {
                            for (int neighborColumn = firstColumn;
                                 neighborColumn <= lastColumn;
                                 neighborColumn++)
                            {
                                if (neighborRow == row
                                    && neighborColumn == column)
                                {
                                    continue;
                                }

                                double value = values[checked(
                                    neighborRow * columnCount + neighborColumn)];
                                if (IsFinite(value))
                                {
                                    neighbors[neighborCount++] = value;
                                }
                            }
                        }

                        if (neighborCount < options.MinimumValidNeighbors)
                        {
                            continue;
                        }

                        Array.Sort(neighbors, 0, neighborCount);
                        int middle = neighborCount / 2;
                        double median = (neighborCount & 1) == 0
                            ? (neighbors[middle - 1] + neighbors[middle]) / 2.0
                            : neighbors[middle];
                        if (!IsFinite(median))
                        {
                            // Preserve ordinary midpoint rounding; halve first only when the sum overflows.
                            median = neighbors[middle - 1] / 2.0 + neighbors[middle] / 2.0;
                        }

                        if (Math.Abs(center - median)
                            > options.MaximumAbsoluteDeviation)
                        {
                            output[index] = double.NaN;
                            outlierIndices.Add(index);
                        }
                    }
                }

                return DeterministicLocalMedianOutlierFilterResult.Completed(
                    output,
                    outlierIndices);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (ArgumentException exception)
            {
                return DeterministicLocalMedianOutlierFilterResult.Failed(
                    exception.Message);
            }
            catch (InvalidDataException exception)
            {
                return DeterministicLocalMedianOutlierFilterResult.Failed(
                    exception.Message);
            }
            catch (OverflowException exception)
            {
                return DeterministicLocalMedianOutlierFilterResult.Failed(
                    exception.Message);
            }
        }

        private static void Validate(
            int rowCount,
            int columnCount,
            IReadOnlyList<double> values,
            DeterministicLocalMedianOutlierFilterOptions options)
        {
            if (rowCount <= 0 || columnCount <= 0)
            {
                throw new InvalidDataException(
                    "Local Median Outlier Filter grid dimensions must be positive.");
            }

            if (values == null
                || values.Count != checked(rowCount * columnCount))
            {
                throw new InvalidDataException(
                    "Local Median Outlier Filter values must match the declared grid dimensions.");
            }

            if (options == null
                || options.WindowSize != 3
                    && options.WindowSize != 5
                    && options.WindowSize != 7)
            {
                throw new InvalidDataException(
                    "Local Median Outlier Filter WindowSize must be 3, 5, or 7.");
            }

            int maximumNeighbors = checked(
                options.WindowSize * options.WindowSize - 1);
            if (options.MinimumValidNeighbors < 1
                || options.MinimumValidNeighbors > maximumNeighbors)
            {
                throw new InvalidDataException(
                    "Local Median Outlier Filter MinimumValidNeighbors is outside the kernel capacity.");
            }

            if (!IsFinite(options.MaximumAbsoluteDeviation)
                || options.MaximumAbsoluteDeviation <= 0.0)
            {
                throw new InvalidDataException(
                    "Local Median Outlier Filter MaximumAbsoluteDeviation must be finite and greater than zero.");
            }

            for (int index = 0; index < values.Count; index++)
            {
                if (IsFinite(values[index]))
                {
                    return;
                }
            }

            throw new InvalidDataException(
                "Local Median Outlier Filter contains no finite samples.");
        }

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }
    }
}
