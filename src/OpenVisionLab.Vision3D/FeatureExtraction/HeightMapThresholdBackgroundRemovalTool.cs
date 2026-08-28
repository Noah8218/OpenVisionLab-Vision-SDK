using System;
using System.IO;
using System.Threading;
using OpenVisionLab.Vision3D.Geometry;

namespace OpenVisionLab.Vision3D.FeatureExtraction
{
    /// <summary>
    /// Declares which side of an inclusive height threshold is foreground.
    /// </summary>
    public enum HeightThresholdBackgroundRemovalMode
    {
        KeepAtOrAboveThreshold,
        KeepAtOrBelowThreshold
    }

    public sealed class HeightMapThresholdBackgroundRemovalOptions
    {
        public double Threshold { get; set; }

        public HeightThresholdBackgroundRemovalMode Mode { get; set; } =
            HeightThresholdBackgroundRemovalMode.KeepAtOrAboveThreshold;
    }

    /// <summary>
    /// Controlled same-grid output from an explicit height-threshold
    /// foreground predicate. Existing missing cells remain missing.
    /// </summary>
    public sealed class HeightMapThresholdBackgroundRemovalResult
    {
        internal HeightMapThresholdBackgroundRemovalResult(
            bool success,
            string message,
            HeightMap3D output,
            double threshold,
            HeightThresholdBackgroundRemovalMode mode,
            int inputValidSampleCount,
            int inputMissingSampleCount,
            int retainedValidSampleCount,
            int removedBackgroundSampleCount)
        {
            Success = success;
            Message = message ?? string.Empty;
            Output = output;
            Threshold = threshold;
            Mode = mode;
            InputValidSampleCount = inputValidSampleCount;
            InputMissingSampleCount = inputMissingSampleCount;
            RetainedValidSampleCount = retainedValidSampleCount;
            RemovedBackgroundSampleCount = removedBackgroundSampleCount;
        }

        public bool Success { get; }

        public string Message { get; }

        public HeightMap3D Output { get; }

        public double Threshold { get; }

        public HeightThresholdBackgroundRemovalMode Mode { get; }

        public int InputValidSampleCount { get; }

        public int InputMissingSampleCount { get; }

        public int RetainedValidSampleCount { get; }

        public int RemovedBackgroundSampleCount { get; }

        public int OutputMissingSampleCount =>
            InputMissingSampleCount + RemovedBackgroundSampleCount;

        public bool HasForeground => RetainedValidSampleCount > 0;
    }

    /// <summary>
    /// Applies one explicit inclusive height predicate to an immutable regular
    /// height map. Values outside the predicate become NaN; no interpolation,
    /// morphology, region inference, or product acceptance is performed.
    /// </summary>
    public sealed class HeightMapThresholdBackgroundRemovalTool
    {
        public HeightMapThresholdBackgroundRemovalResult Execute(
            HeightMap3D source,
            HeightMapThresholdBackgroundRemovalOptions options,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            try
            {
                Validate(source, options);
                cancellationToken.ThrowIfCancellationRequested();

                double[] sourceValues = source.CopyValues();
                double[] outputValues = new double[sourceValues.Length];
                int inputValidSampleCount = 0;
                int inputMissingSampleCount = 0;
                int retainedValidSampleCount = 0;
                int removedBackgroundSampleCount = 0;
                for (int index = 0; index < sourceValues.Length; index++)
                {
                    if ((index & 0x3fff) == 0)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                    }

                    double value = sourceValues[index];
                    if (double.IsNaN(value))
                    {
                        inputMissingSampleCount++;
                        outputValues[index] = double.NaN;
                        continue;
                    }

                    inputValidSampleCount++;
                    bool keep = options.Mode == HeightThresholdBackgroundRemovalMode.KeepAtOrAboveThreshold
                        ? value >= options.Threshold
                        : value <= options.Threshold;
                    if (keep)
                    {
                        retainedValidSampleCount++;
                        outputValues[index] = value;
                    }
                    else
                    {
                        removedBackgroundSampleCount++;
                        outputValues[index] = double.NaN;
                    }
                }

                HeightMap3D output = new HeightMap3D(
                    source.Rows,
                    source.Columns,
                    source.OriginX,
                    source.OriginY,
                    source.ColumnPitch,
                    source.RowPitch,
                    outputValues,
                    source.PlanarUnit,
                    source.HeightUnit,
                    source.FrameId,
                    source.SourceId);
                string message = retainedValidSampleCount == 0
                    ? "No finite samples met the inclusive height predicate; the same-grid output contains only missing samples."
                    : "Completed inclusive height-threshold background removal without interpolation or source mutation.";
                return new HeightMapThresholdBackgroundRemovalResult(
                    true,
                    message,
                    output,
                    options.Threshold,
                    options.Mode,
                    inputValidSampleCount,
                    inputMissingSampleCount,
                    retainedValidSampleCount,
                    removedBackgroundSampleCount);
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
            HeightMap3D source,
            HeightMapThresholdBackgroundRemovalOptions options)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            if (options == null)
            {
                throw new ArgumentNullException(nameof(options));
            }

            if (double.IsNaN(options.Threshold) || double.IsInfinity(options.Threshold))
            {
                throw new ArgumentException("Height threshold must be finite.");
            }

            if (!Enum.IsDefined(typeof(HeightThresholdBackgroundRemovalMode), options.Mode))
            {
                throw new ArgumentException("Height threshold background-removal mode is invalid.");
            }

            double[] values = source.CopyValues();
            bool hasFiniteSample = false;
            for (int index = 0; index < values.Length; index++)
            {
                if (!double.IsNaN(values[index]))
                {
                    hasFiniteSample = true;
                    break;
                }
            }

            if (!hasFiniteSample)
            {
                throw new InvalidDataException("Height threshold background removal requires at least one finite source sample.");
            }
        }

        private static HeightMapThresholdBackgroundRemovalResult Error(string message)
        {
            return new HeightMapThresholdBackgroundRemovalResult(
                false,
                message,
                null,
                double.NaN,
                default(HeightThresholdBackgroundRemovalMode),
                0,
                0,
                0,
                0);
        }
    }
}
