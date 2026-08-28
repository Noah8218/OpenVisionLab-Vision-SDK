using System;
using System.IO;
using System.Threading;
using OpenVisionLab.Vision3D.Geometry;

namespace OpenVisionLab.Vision3D.FeatureExtraction
{
    /// <summary>
    /// Declares the signed direction for one saved-background subtraction.
    /// </summary>
    public enum HeightMapBackgroundSubtractionMode
    {
        CurrentMinusSavedBackground
    }

    public sealed class HeightMapBackgroundSubtractionOptions
    {
        public HeightMapBackgroundSubtractionMode Mode { get; set; } =
            HeightMapBackgroundSubtractionMode.CurrentMinusSavedBackground;
    }

    /// <summary>
    /// Controlled same-grid signed difference result. Missing cells are those
    /// where either input is missing; no missing value is treated as zero.
    /// </summary>
    public sealed class HeightMapBackgroundSubtractionResult
    {
        internal HeightMapBackgroundSubtractionResult(
            bool success,
            string message,
            HeightMap3D output,
            HeightMapBackgroundSubtractionMode mode,
            int currentValidSampleCount,
            int backgroundValidSampleCount,
            int pairedValidSampleCount,
            int missingEitherSampleCount,
            int zeroDeltaSampleCount,
            int positiveDeltaSampleCount,
            int negativeDeltaSampleCount)
        {
            Success = success;
            Message = message ?? string.Empty;
            Output = output;
            Mode = mode;
            CurrentValidSampleCount = currentValidSampleCount;
            BackgroundValidSampleCount = backgroundValidSampleCount;
            PairedValidSampleCount = pairedValidSampleCount;
            MissingEitherSampleCount = missingEitherSampleCount;
            ZeroDeltaSampleCount = zeroDeltaSampleCount;
            PositiveDeltaSampleCount = positiveDeltaSampleCount;
            NegativeDeltaSampleCount = negativeDeltaSampleCount;
        }

        public bool Success { get; }

        public string Message { get; }

        public HeightMap3D Output { get; }

        public HeightMapBackgroundSubtractionMode Mode { get; }

        public int CurrentValidSampleCount { get; }

        public int BackgroundValidSampleCount { get; }

        public int PairedValidSampleCount { get; }

        public int MissingEitherSampleCount { get; }

        public int ZeroDeltaSampleCount { get; }

        public int PositiveDeltaSampleCount { get; }

        public int NegativeDeltaSampleCount { get; }

        public int OutputValidSampleCount => PairedValidSampleCount;

        public int OutputMissingSampleCount => MissingEitherSampleCount;
    }

    /// <summary>
    /// Computes one deterministic signed current-minus-saved-background
    /// difference over two identically aligned immutable regular height maps.
    /// No alignment, interpolation, resampling, tolerance, or missing-value
    /// substitution is performed.
    /// </summary>
    public sealed class HeightMapBackgroundSubtractionTool
    {
        public HeightMapBackgroundSubtractionResult Execute(
            HeightMap3D current,
            HeightMap3D savedBackground,
            HeightMapBackgroundSubtractionOptions options,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            try
            {
                Validate(current, savedBackground, options);
                cancellationToken.ThrowIfCancellationRequested();

                double[] currentValues = current.CopyValues();
                double[] backgroundValues = savedBackground.CopyValues();
                double[] outputValues = new double[currentValues.Length];
                int currentValidSampleCount = 0;
                int backgroundValidSampleCount = 0;
                int pairedValidSampleCount = 0;
                int missingEitherSampleCount = 0;
                int zeroDeltaSampleCount = 0;
                int positiveDeltaSampleCount = 0;
                int negativeDeltaSampleCount = 0;
                for (int index = 0; index < currentValues.Length; index++)
                {
                    if ((index & 0x3fff) == 0)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                    }

                    double currentValue = currentValues[index];
                    double backgroundValue = backgroundValues[index];
                    bool currentMissing = double.IsNaN(currentValue);
                    bool backgroundMissing = double.IsNaN(backgroundValue);
                    if (!currentMissing)
                    {
                        currentValidSampleCount++;
                    }

                    if (!backgroundMissing)
                    {
                        backgroundValidSampleCount++;
                    }

                    if (currentMissing || backgroundMissing)
                    {
                        missingEitherSampleCount++;
                        outputValues[index] = double.NaN;
                        continue;
                    }

                    double delta = options.Mode == HeightMapBackgroundSubtractionMode.CurrentMinusSavedBackground
                        ? currentValue - backgroundValue
                        : double.NaN;
                    if (double.IsInfinity(delta))
                    {
                        throw new InvalidDataException("Background subtraction produced a non-finite delta.");
                    }

                    pairedValidSampleCount++;
                    outputValues[index] = delta;
                    if (delta == 0.0)
                    {
                        zeroDeltaSampleCount++;
                    }
                    else if (delta > 0.0)
                    {
                        positiveDeltaSampleCount++;
                    }
                    else
                    {
                        negativeDeltaSampleCount++;
                    }
                }

                if (pairedValidSampleCount == 0)
                {
                    throw new InvalidDataException(
                        "Background subtraction requires at least one finite pair shared by current and saved-background grids.");
                }

                HeightMap3D output = new HeightMap3D(
                    current.Rows,
                    current.Columns,
                    current.OriginX,
                    current.OriginY,
                    current.ColumnPitch,
                    current.RowPitch,
                    outputValues,
                    current.PlanarUnit,
                    current.HeightUnit,
                    current.FrameId,
                    current.SourceId);
                return new HeightMapBackgroundSubtractionResult(
                    true,
                    "Completed current-minus-saved-background subtraction on an aligned grid without source mutation.",
                    output,
                    options.Mode,
                    currentValidSampleCount,
                    backgroundValidSampleCount,
                    pairedValidSampleCount,
                    missingEitherSampleCount,
                    zeroDeltaSampleCount,
                    positiveDeltaSampleCount,
                    negativeDeltaSampleCount);
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
            HeightMap3D current,
            HeightMap3D savedBackground,
            HeightMapBackgroundSubtractionOptions options)
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

            if (!Enum.IsDefined(typeof(HeightMapBackgroundSubtractionMode), options.Mode))
            {
                throw new ArgumentException("Background subtraction mode is invalid.");
            }

            if (current.Rows != savedBackground.Rows
                || current.Columns != savedBackground.Columns
                || current.OriginX != savedBackground.OriginX
                || current.OriginY != savedBackground.OriginY
                || current.ColumnPitch != savedBackground.ColumnPitch
                || current.RowPitch != savedBackground.RowPitch
                || !string.Equals(current.PlanarUnit, savedBackground.PlanarUnit, StringComparison.Ordinal)
                || !string.Equals(current.HeightUnit, savedBackground.HeightUnit, StringComparison.Ordinal)
                || !string.Equals(current.FrameId, savedBackground.FrameId, StringComparison.Ordinal))
            {
                throw new InvalidDataException(
                    "Current and saved-background height maps must have identical dimensions, origin, pitches, units, and frame.");
            }
        }

        private static HeightMapBackgroundSubtractionResult Error(string message)
        {
            return new HeightMapBackgroundSubtractionResult(
                false,
                message,
                null,
                default(HeightMapBackgroundSubtractionMode),
                0,
                0,
                0,
                0,
                0,
                0,
                0);
        }
    }
}
