using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace OpenVisionLab.Vision3D.FeatureExtraction
{
    public sealed class DeterministicMedianFilterOptions
    {
        public int KernelSize { get; set; }
    }

    public sealed class DeterministicMedianFilterResult
    {
        private DeterministicMedianFilterResult(bool success, string message, IReadOnlyList<double> values, int changedCount)
        {
            Success = success;
            Message = message ?? string.Empty;
            Values = values ?? Array.Empty<double>();
            ChangedCount = changedCount;
        }

        public bool Success { get; }
        public string Message { get; }
        public IReadOnlyList<double> Values { get; }
        public int ChangedCount { get; }

        internal static DeterministicMedianFilterResult Completed(IReadOnlyList<double> values, int changedCount)
        {
            return new DeterministicMedianFilterResult(
                true,
                "Completed deterministic median filtering.",
                values,
                changedCount);
        }

        internal static DeterministicMedianFilterResult Failed(string message)
        {
            return new DeterministicMedianFilterResult(false, message, Array.Empty<double>(), 0);
        }
    }

    /// <summary>
    /// Pure row-major finite/NaN median filtering. The caller owns source
    /// identity, scalar meaning, and any format-specific missing-value rules.
    /// </summary>
    public sealed class DeterministicMedianFilterTool
    {
        public DeterministicMedianFilterResult Execute(
            int rowCount,
            int columnCount,
            IReadOnlyList<double> values,
            DeterministicMedianFilterOptions options,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            try
            {
                Validate(rowCount, columnCount, values, options);
                double[] output = new double[values.Count];
                double[] neighbors = new double[49];
                int radius = options.KernelSize / 2;
                int changedCount = 0;

                for (int row = 0; row < rowCount; row++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    for (int column = 0; column < columnCount; column++)
                    {
                        int sourceIndex = (row * columnCount) + column;
                        if (!IsFinite(values[sourceIndex]))
                        {
                            output[sourceIndex] = double.NaN;
                            continue;
                        }

                        int count = 0;
                        for (int neighborRow = Math.Max(0, row - radius);
                             neighborRow <= Math.Min(rowCount - 1, row + radius);
                             neighborRow++)
                        {
                            for (int neighborColumn = Math.Max(0, column - radius);
                                 neighborColumn <= Math.Min(columnCount - 1, column + radius);
                                 neighborColumn++)
                            {
                                double value = values[(neighborRow * columnCount) + neighborColumn];
                                if (IsFinite(value))
                                {
                                    neighbors[count++] = value;
                                }
                            }
                        }

                        Array.Sort(neighbors, 0, count);
                        double median = (count & 1) == 1
                            ? neighbors[count / 2]
                            : (neighbors[(count / 2) - 1] + neighbors[count / 2]) / 2.0;
                        output[sourceIndex] = median;
                        if (median != values[sourceIndex])
                        {
                            changedCount++;
                        }
                    }
                }

                return DeterministicMedianFilterResult.Completed(output, changedCount);
            }
            catch (ArgumentException exception)
            {
                return DeterministicMedianFilterResult.Failed(exception.Message);
            }
            catch (InvalidDataException exception)
            {
                return DeterministicMedianFilterResult.Failed(exception.Message);
            }
            catch (OverflowException exception)
            {
                return DeterministicMedianFilterResult.Failed(exception.Message);
            }
        }

        private static void Validate(
            int rowCount,
            int columnCount,
            IReadOnlyList<double> values,
            DeterministicMedianFilterOptions options)
        {
            if (rowCount <= 0 || columnCount <= 0)
            {
                throw new InvalidDataException("Median Filter grid dimensions must be positive.");
            }

            if (values == null || values.Count != checked(rowCount * columnCount))
            {
                throw new InvalidDataException("Median Filter grid values must match the declared dimensions.");
            }

            if (options == null || options.KernelSize != 3 && options.KernelSize != 5 && options.KernelSize != 7)
            {
                throw new InvalidDataException("Median Filter KernelSize must be 3, 5, or 7.");
            }

            for (int index = 0; index < values.Count; index++)
            {
                if (IsFinite(values[index]))
                {
                    return;
                }
            }

            throw new InvalidDataException("Median Filter grid contains no finite samples.");
        }

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }
    }
}
