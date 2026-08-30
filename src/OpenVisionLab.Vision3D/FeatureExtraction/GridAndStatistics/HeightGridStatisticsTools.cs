using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace OpenVisionLab.Vision3D.FeatureExtraction
{
    public sealed class HeightGridSummaryOptions
    {
        public bool ZeroIsMissing { get; set; } = true;
        public int DistributionBinCount { get; set; } = 32;
    }

    public sealed class HeightGridSummaryResult
    {
        internal HeightGridSummaryResult(
            bool success,
            string message,
            int sampleCount,
            int validSampleCount,
            int missingSampleCount,
            int zeroSampleCount,
            int nonFiniteSampleCount,
            double minimum,
            double maximum,
            double mean,
            IReadOnlyList<int> bins,
            IReadOnlyList<double> binLowerBounds,
            IReadOnlyList<double> binUpperBounds,
            int peakBinIndex)
        {
            Success = success;
            Message = message ?? string.Empty;
            SampleCount = sampleCount;
            ValidSampleCount = validSampleCount;
            MissingSampleCount = missingSampleCount;
            ZeroSampleCount = zeroSampleCount;
            NonFiniteSampleCount = nonFiniteSampleCount;
            Minimum = minimum;
            Maximum = maximum;
            Mean = mean;
            Bins = bins ?? Array.Empty<int>();
            BinLowerBounds = binLowerBounds ?? Array.Empty<double>();
            BinUpperBounds = binUpperBounds ?? Array.Empty<double>();
            PeakBinIndex = peakBinIndex;
        }

        public bool Success { get; }
        public string Message { get; }
        public int SampleCount { get; }
        public int ValidSampleCount { get; }
        public int MissingSampleCount { get; }
        public int ZeroSampleCount { get; }
        public int NonFiniteSampleCount { get; }
        public double Minimum { get; }
        public double Maximum { get; }
        public double Mean { get; }
        public IReadOnlyList<int> Bins { get; }
        public IReadOnlyList<double> BinLowerBounds { get; }
        public IReadOnlyList<double> BinUpperBounds { get; }
        public int PeakBinIndex { get; }
        public bool HasFiniteSamples => ValidSampleCount > 0;
        public bool IsConstant => HasFiniteSamples && Minimum == Maximum;
        public int PeakSampleCount => PeakBinIndex < 0 ? 0 : Bins[PeakBinIndex];
        public double PeakFraction => ValidSampleCount == 0
            ? double.NaN
            : PeakSampleCount / (double)ValidSampleCount;
        public double PeakLowerBound => PeakBinIndex < 0
            ? double.NaN
            : BinLowerBounds[PeakBinIndex];
        public double PeakUpperBound => PeakBinIndex < 0
            ? double.NaN
            : BinUpperBounds[PeakBinIndex];
        public double PeakCenter => PeakBinIndex < 0
            ? double.NaN
            : IsConstant
                ? Minimum
                : (PeakLowerBound + PeakUpperBound) * 0.5;
    }

    /// <summary>
    /// Computes deterministic full-grid statistics and a fixed-bin
    /// distribution from source-neutral single-precision samples. The caller
    /// explicitly selects whether zero is a missing value.
    /// </summary>
    public sealed class HeightGridSummaryTool
    {
        public HeightGridSummaryResult Execute(
            IReadOnlyList<float> samples,
            HeightGridSummaryOptions options,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            try
            {
                if (samples == null)
                {
                    throw new ArgumentNullException(nameof(samples));
                }

                if (options == null)
                {
                    throw new ArgumentNullException(nameof(options));
                }

                if (options.DistributionBinCount <= 0)
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(options.DistributionBinCount),
                        options.DistributionBinCount,
                        "Height-grid distribution bin count must be positive.");
                }

                int validCount = 0;
                int zeroCount = 0;
                int nonFiniteCount = 0;
                double minimum = double.PositiveInfinity;
                double maximum = double.NegativeInfinity;
                double sum = 0.0;
                for (int index = 0; index < samples.Count; index++)
                {
                    if ((index & 0x3fff) == 0)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                    }

                    float value = samples[index];
                    if (!IsFinite(value))
                    {
                        nonFiniteCount++;
                        continue;
                    }

                    if (options.ZeroIsMissing && value == 0.0f)
                    {
                        zeroCount++;
                        continue;
                    }

                    validCount++;
                    if (value < minimum)
                    {
                        minimum = value;
                    }

                    if (value > maximum)
                    {
                        maximum = value;
                    }

                    sum += value;
                }

                if (validCount == 0)
                {
                    return Failed(
                        "Height-grid summary requires at least one finite sample accepted by the missing-value policy.",
                        samples.Count,
                        zeroCount,
                        nonFiniteCount);
                }

                double mean = sum / validCount;
                int[] bins = new int[options.DistributionBinCount];
                double span = maximum - minimum;
                int observedValidCount = 0;
                for (int index = 0; index < samples.Count; index++)
                {
                    if ((index & 0x3fff) == 0)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                    }

                    float value = samples[index];
                    if (!IsFinite(value)
                        || (options.ZeroIsMissing && value == 0.0f))
                    {
                        continue;
                    }

                    observedValidCount++;
                    int binIndex = span == 0.0
                        ? 0
                        : Math.Min(
                            bins.Length - 1,
                            (int)(((double)value - minimum) / span * bins.Length));
                    bins[binIndex]++;
                }

                if (observedValidCount != validCount)
                {
                    throw new InvalidDataException(
                        "Height-grid summary changed between statistics and distribution passes.");
                }

                double[] lowerBounds;
                double[] upperBounds;
                int peakBinIndex;
                BuildDistributionEvidence(
                    minimum,
                    maximum,
                    bins,
                    out lowerBounds,
                    out upperBounds,
                    out peakBinIndex);
                return new HeightGridSummaryResult(
                    true,
                    "Completed deterministic height-grid summary.",
                    samples.Count,
                    validCount,
                    samples.Count - validCount,
                    zeroCount,
                    nonFiniteCount,
                    minimum,
                    maximum,
                    mean,
                    Array.AsReadOnly(bins),
                    Array.AsReadOnly(lowerBounds),
                    Array.AsReadOnly(upperBounds),
                    peakBinIndex);
            }
            catch (Exception exception) when (
                exception is ArgumentException
                || exception is InvalidDataException
                || exception is OverflowException)
            {
                return Failed(exception.Message, samples == null ? 0 : samples.Count, 0, 0);
            }
        }

        private static HeightGridSummaryResult Failed(
            string message,
            int sampleCount,
            int zeroSampleCount,
            int nonFiniteSampleCount)
        {
            return new HeightGridSummaryResult(
                false,
                message,
                sampleCount,
                0,
                sampleCount,
                zeroSampleCount,
                nonFiniteSampleCount,
                double.NaN,
                double.NaN,
                double.NaN,
                Array.Empty<int>(),
                Array.Empty<double>(),
                Array.Empty<double>(),
                -1);
        }

        internal static void BuildDistributionEvidence(
            double minimum,
            double maximum,
            IReadOnlyList<int> bins,
            out double[] lowerBounds,
            out double[] upperBounds,
            out int peakBinIndex)
        {
            lowerBounds = new double[bins.Count];
            upperBounds = new double[bins.Count];
            bool constant = minimum == maximum;
            peakBinIndex = 0;
            for (int index = 0; index < bins.Count; index++)
            {
                lowerBounds[index] = constant
                    ? minimum
                    : minimum + (maximum - minimum) * index / bins.Count;
                upperBounds[index] = constant
                    ? maximum
                    : minimum + (maximum - minimum) * (index + 1) / bins.Count;
                if (bins[index] > bins[peakBinIndex])
                {
                    peakBinIndex = index;
                }
            }
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }

    public sealed class HeightDistributionStatisticsOptions
    {
        public int BinCount { get; set; } = 32;
        public bool ZeroIsMissing { get; set; }
        public int? ExpectedValidSampleCount { get; set; }
    }

    public sealed class HeightDistributionStatisticsResult
    {
        internal HeightDistributionStatisticsResult(
            bool success,
            string message,
            int sampleCount,
            int validSampleCount,
            int missingSampleCount,
            double minimum,
            double maximum,
            double mean,
            IReadOnlyList<int> bins,
            int peakBinIndex)
        {
            Success = success;
            Message = message ?? string.Empty;
            SampleCount = sampleCount;
            ValidSampleCount = validSampleCount;
            MissingSampleCount = missingSampleCount;
            Minimum = minimum;
            Maximum = maximum;
            Mean = mean;
            Bins = bins ?? Array.Empty<int>();
            PeakBinIndex = peakBinIndex;
        }

        public bool Success { get; }
        public string Message { get; }
        public int SampleCount { get; }
        public int ValidSampleCount { get; }
        public int MissingSampleCount { get; }
        public double Minimum { get; }
        public double Maximum { get; }
        public double Mean { get; }
        public IReadOnlyList<int> Bins { get; }
        public int PeakBinIndex { get; }
        public bool HasFiniteSamples => ValidSampleCount > 0;
    }

    /// <summary>
    /// Computes deterministic finite-value statistics and bin counts for a
    /// source-neutral double-precision scalar sequence.
    /// </summary>
    public sealed class HeightDistributionStatisticsTool
    {
        public HeightDistributionStatisticsResult Execute(
            IReadOnlyList<double> values,
            HeightDistributionStatisticsOptions options,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            try
            {
                if (values == null)
                {
                    throw new ArgumentNullException(nameof(values));
                }

                if (options == null)
                {
                    throw new ArgumentNullException(nameof(options));
                }

                if (options.BinCount <= 0)
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(options.BinCount),
                        options.BinCount,
                        "Height-distribution bin count must be positive.");
                }

                int validCount = 0;
                double minimum = double.PositiveInfinity;
                double maximum = double.NegativeInfinity;
                double sum = 0.0;
                for (int index = 0; index < values.Count; index++)
                {
                    if ((index & 0x3fff) == 0)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                    }

                    double value = values[index];
                    if (!IsFinite(value)
                        || (options.ZeroIsMissing && value == 0.0))
                    {
                        continue;
                    }

                    validCount++;
                    if (value < minimum)
                    {
                        minimum = value;
                    }

                    if (value > maximum)
                    {
                        maximum = value;
                    }

                    sum += value;
                }

                if (options.ExpectedValidSampleCount.HasValue
                    && options.ExpectedValidSampleCount.Value != validCount)
                {
                    throw new InvalidDataException(
                        "Height-distribution valid-count mismatch: expected "
                        + options.ExpectedValidSampleCount.Value
                        + ", observed "
                        + validCount
                        + ".");
                }

                if (validCount == 0)
                {
                    return new HeightDistributionStatisticsResult(
                        true,
                        "No finite samples were available for height-distribution statistics.",
                        values.Count,
                        0,
                        values.Count,
                        double.NaN,
                        double.NaN,
                        double.NaN,
                        Array.AsReadOnly(new int[options.BinCount]),
                        -1);
                }

                double mean = sum / validCount;
                double span = maximum - minimum;
                if (!IsFinite(sum) || !IsFinite(mean) || !IsFinite(span))
                {
                    throw new InvalidDataException(
                        "Height-distribution statistics produced a non-finite value or overflow.");
                }

                int[] bins = new int[options.BinCount];
                for (int index = 0; index < values.Count; index++)
                {
                    if ((index & 0x3fff) == 0)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                    }

                    double value = values[index];
                    if (!IsFinite(value)
                        || (options.ZeroIsMissing && value == 0.0))
                    {
                        continue;
                    }

                    int binIndex = span == 0.0
                        ? 0
                        : Math.Min(
                            bins.Length - 1,
                            (int)((value - minimum) / span * bins.Length));
                    bins[binIndex]++;
                }

                int peakBinIndex = 0;
                for (int index = 1; index < bins.Length; index++)
                {
                    if (bins[index] > bins[peakBinIndex])
                    {
                        peakBinIndex = index;
                    }
                }

                return new HeightDistributionStatisticsResult(
                    true,
                    "Completed deterministic height-distribution statistics.",
                    values.Count,
                    validCount,
                    values.Count - validCount,
                    minimum,
                    maximum,
                    mean,
                    Array.AsReadOnly(bins),
                    peakBinIndex);
            }
            catch (Exception exception) when (
                exception is ArgumentException
                || exception is InvalidDataException
                || exception is OverflowException)
            {
                return new HeightDistributionStatisticsResult(
                    false,
                    exception.Message,
                    values == null ? 0 : values.Count,
                    0,
                    values == null ? 0 : values.Count,
                    double.NaN,
                    double.NaN,
                    double.NaN,
                    Array.Empty<int>(),
                    -1);
            }
        }

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }
    }
}
