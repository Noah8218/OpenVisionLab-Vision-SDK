using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using OpenVisionLab.Vision3D.Geometry;

namespace OpenVisionLab.Vision3D.FeatureExtraction
{
    /// <summary>
    /// Describes the finite-difference sources used for one calculated normal.
    /// A central derivative is used when both neighbors are finite; a one-sided
    /// derivative is used at a finite boundary. Units and frame remain caller
    /// responsibilities.
    /// </summary>
    public sealed class HeightMapNormalSample
    {
        internal HeightMapNormalSample(
            int row,
            int column,
            ThreeDPoint position,
            ThreeDPoint normal,
            bool centralColumnDerivative,
            bool centralRowDerivative)
        {
            Row = row;
            Column = column;
            Position = position;
            Normal = normal;
            CentralColumnDerivative = centralColumnDerivative;
            CentralRowDerivative = centralRowDerivative;
        }

        public int Row { get; }

        public int Column { get; }

        /// <summary>
        /// Position uses the Studio-compatible convention (grid X, scalar
        /// height Y, grid row Z).
        /// </summary>
        public ThreeDPoint Position { get; }

        /// <summary>
        /// Unit normal with a positive scalar-height component.
        /// </summary>
        public ThreeDPoint Normal { get; }

        public bool CentralColumnDerivative { get; }

        public bool CentralRowDerivative { get; }

        public bool UsesOneSidedDerivative =>
            !CentralColumnDerivative || !CentralRowDerivative;
    }

    public enum HeightMapNormalValidationState
    {
        NotRequested = 0,
        Passed = 1,
        Failed = 2
    }

    /// <summary>
    /// Optional validation policy for calculated normals. ExpectedNormal is
    /// intentionally caller-supplied; the tool does not infer orientation.
    /// </summary>
    public sealed class HeightMapNormalPreparationOptions
    {
        public ThreeDPoint ExpectedNormal { get; set; }

        public double MinimumAlignmentCosine { get; set; } = 0.999;
    }

    /// <summary>
    /// Controlled output for deterministic regular-height-map normal
    /// preparation. Missing source cells and cells without a finite neighbor
    /// on either planar axis are not interpolated and have no calculated
    /// normal.
    /// </summary>
    public sealed class HeightMapNormalPreparationResult
    {
        internal HeightMapNormalPreparationResult(
            bool success,
            string message,
            int rowCount,
            int columnCount,
            int inputFiniteSampleCount,
            int calculatedNormalCount,
            int unavailableNormalCount,
            int centralDerivativeCount,
            int oneSidedDerivativeCount,
            int missingDerivativeCount,
            IReadOnlyList<HeightMapNormalSample> samples,
            HeightMapNormalValidationState validationState,
            int validatedNormalCount,
            int consistentNormalCount,
            int reversedNormalCount,
            double minimumAlignment,
            double meanAlignment,
            double maximumAngularErrorDegrees)
        {
            Success = success;
            Message = message ?? string.Empty;
            RowCount = rowCount;
            ColumnCount = columnCount;
            InputFiniteSampleCount = inputFiniteSampleCount;
            CalculatedNormalCount = calculatedNormalCount;
            UnavailableNormalCount = unavailableNormalCount;
            CentralDerivativeCount = centralDerivativeCount;
            OneSidedDerivativeCount = oneSidedDerivativeCount;
            MissingDerivativeCount = missingDerivativeCount;
            Samples = samples ?? Array.Empty<HeightMapNormalSample>();
            ValidationState = validationState;
            ValidatedNormalCount = validatedNormalCount;
            ConsistentNormalCount = consistentNormalCount;
            ReversedNormalCount = reversedNormalCount;
            MinimumAlignment = minimumAlignment;
            MeanAlignment = meanAlignment;
            MaximumAngularErrorDegrees = maximumAngularErrorDegrees;
        }

        public bool Success { get; }

        public string Message { get; }

        public int RowCount { get; }

        public int ColumnCount { get; }

        public int InputFiniteSampleCount { get; }

        public int CalculatedNormalCount { get; }

        public int UnavailableNormalCount { get; }

        /// <summary>
        /// Number of finite derivative axes resolved by central differences.
        /// </summary>
        public int CentralDerivativeCount { get; }

        /// <summary>
        /// Number of finite derivative axes resolved by one-sided differences.
        /// </summary>
        public int OneSidedDerivativeCount { get; }

        /// <summary>
        /// Number of derivative axes for which no finite neighbor was present.
        /// </summary>
        public int MissingDerivativeCount { get; }

        public IReadOnlyList<HeightMapNormalSample> Samples { get; }

        public HeightMapNormalValidationState ValidationState { get; }

        public int ValidatedNormalCount { get; }

        public int ConsistentNormalCount { get; }

        public int ReversedNormalCount { get; }

        public double MinimumAlignment { get; }

        public double MeanAlignment { get; }

        public double MaximumAngularErrorDegrees { get; }
    }

    /// <summary>
    /// Calculates one deterministic normal for each finite regular-grid cell
    /// whose X and Z derivatives can both be resolved. Interior cells use
    /// central differences; boundaries use one-sided differences. A missing
    /// neighbor never gets replaced by interpolation. An optional expected
    /// normal produces validation evidence but does not change the calculated
    /// samples.
    /// </summary>
    public sealed class HeightMapNormalPreparationTool
    {
        public const string Semantics =
            "height-map-normal-finite-difference-with-explicit-validation-v1";

        public HeightMapNormalPreparationResult Execute(
            HeightMap3D source,
            HeightMapNormalPreparationOptions options = null,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            try
            {
                Validate(source, options);
                cancellationToken.ThrowIfCancellationRequested();

                double[] values = source.CopyValues();
                List<HeightMapNormalSample> samples =
                    new List<HeightMapNormalSample>();
                int inputFiniteSampleCount = 0;
                int unavailableNormalCount = 0;
                int centralDerivativeCount = 0;
                int oneSidedDerivativeCount = 0;
                int missingDerivativeCount = 0;

                for (int row = 0; row < source.Rows; row++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    for (int column = 0; column < source.Columns; column++)
                    {
                        if (((row * source.Columns) + column & 0x3fff) == 0)
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                        }

                        int index = (row * source.Columns) + column;
                        double height = values[index];
                        if (!IsFinite(height))
                        {
                            continue;
                        }

                        inputFiniteSampleCount++;
                        double columnDerivative;
                        double rowDerivative;
                        bool centralColumn;
                        bool centralRow;
                        bool hasColumnDerivative = TryDerivative(
                            values,
                            source.Rows,
                            source.Columns,
                            row,
                            column,
                            true,
                            source.ColumnPitch,
                            out columnDerivative,
                            out centralColumn);
                        bool hasRowDerivative = TryDerivative(
                            values,
                            source.Rows,
                            source.Columns,
                            row,
                            column,
                            false,
                            source.RowPitch,
                            out rowDerivative,
                            out centralRow);
                        if (!hasColumnDerivative)
                        {
                            missingDerivativeCount++;
                        }
                        else if (centralColumn)
                        {
                            centralDerivativeCount++;
                        }
                        else
                        {
                            oneSidedDerivativeCount++;
                        }
                        if (!hasRowDerivative)
                        {
                            missingDerivativeCount++;
                        }
                        else if (centralRow)
                        {
                            centralDerivativeCount++;
                        }
                        else
                        {
                            oneSidedDerivativeCount++;
                        }

                        if (!hasColumnDerivative || !hasRowDerivative)
                        {
                            unavailableNormalCount++;
                            continue;
                        }

                        double normalX = -columnDerivative;
                        double normalY = 1.0;
                        double normalZ = -rowDerivative;
                        double normalLength = Math.Sqrt(
                            (normalX * normalX)
                            + (normalY * normalY)
                            + (normalZ * normalZ));
                        if (!IsFinitePositive(normalLength))
                        {
                            unavailableNormalCount++;
                            continue;
                        }

                        samples.Add(new HeightMapNormalSample(
                            row,
                            column,
                            new ThreeDPoint(
                                source.GetX(column),
                                height,
                                source.GetY(row)),
                            new ThreeDPoint(
                                normalX / normalLength,
                                normalY / normalLength,
                                normalZ / normalLength),
                            centralColumn,
                            centralRow));
                    }
                }

                if (inputFiniteSampleCount == 0)
                {
                    return Failed(
                        source,
                        "Normal preparation requires at least one finite source height.",
                        inputFiniteSampleCount,
                        unavailableNormalCount,
                        centralDerivativeCount,
                        oneSidedDerivativeCount,
                        missingDerivativeCount);
                }
                if (samples.Count == 0)
                {
                    return Failed(
                        source,
                        "Normal preparation requires one finite cell with resolvable X and Z neighbors.",
                        inputFiniteSampleCount,
                        unavailableNormalCount,
                        centralDerivativeCount,
                        oneSidedDerivativeCount,
                        missingDerivativeCount);
                }

                HeightMapNormalValidationState validationState =
                    HeightMapNormalValidationState.NotRequested;
                int validatedNormalCount = 0;
                int consistentNormalCount = 0;
                int reversedNormalCount = 0;
                double minimumAlignment = double.NaN;
                double meanAlignment = double.NaN;
                double maximumAngularErrorDegrees = double.NaN;
                if (options != null && options.ExpectedNormal != null)
                {
                    ThreeDPoint expected = Normalize(options.ExpectedNormal);
                    validationState = HeightMapNormalValidationState.Passed;
                    double alignmentSum = 0.0;
                    minimumAlignment = double.PositiveInfinity;
                    double maximumAngularError = 0.0;
                    foreach (HeightMapNormalSample sample in samples)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        double alignment = Dot(sample.Normal, expected);
                        alignment = Clamp(alignment, -1.0, 1.0);
                        validatedNormalCount++;
                        alignmentSum += alignment;
                        minimumAlignment = Math.Min(minimumAlignment, alignment);
                        if (alignment >= options.MinimumAlignmentCosine)
                        {
                            consistentNormalCount++;
                        }
                        if (alignment < 0.0)
                        {
                            reversedNormalCount++;
                        }
                        maximumAngularError = Math.Max(
                            maximumAngularError,
                            Math.Acos(alignment) * 180.0 / Math.PI);
                    }

                    meanAlignment = alignmentSum / validatedNormalCount;
                    maximumAngularErrorDegrees = maximumAngularError;
                    if (consistentNormalCount != validatedNormalCount)
                    {
                        validationState = HeightMapNormalValidationState.Failed;
                    }
                }

                return new HeightMapNormalPreparationResult(
                    true,
                    "Completed deterministic regular-height-map normal preparation without source mutation.",
                    source.Rows,
                    source.Columns,
                    inputFiniteSampleCount,
                    samples.Count,
                    unavailableNormalCount,
                    centralDerivativeCount,
                    oneSidedDerivativeCount,
                    missingDerivativeCount,
                    Array.AsReadOnly(samples.ToArray()),
                    validationState,
                    validatedNormalCount,
                    consistentNormalCount,
                    reversedNormalCount,
                    minimumAlignment,
                    meanAlignment,
                    maximumAngularErrorDegrees);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (ArgumentException exception)
            {
                return Failed(exception.Message);
            }
            catch (InvalidDataException exception)
            {
                return Failed(exception.Message);
            }
            catch (OverflowException exception)
            {
                return Failed(exception.Message);
            }
        }

        private static bool TryDerivative(
            IReadOnlyList<double> values,
            int rows,
            int columns,
            int row,
            int column,
            bool columnAxis,
            double pitch,
            out double derivative,
            out bool central)
        {
            derivative = double.NaN;
            central = false;
            bool hasLower = columnAxis
                ? column > 0 && IsFinite(values[(row * columns) + column - 1])
                : row > 0 && IsFinite(values[((row - 1) * columns) + column]);
            bool hasUpper = columnAxis
                ? column + 1 < columns && IsFinite(values[(row * columns) + column + 1])
                : row + 1 < rows && IsFinite(values[((row + 1) * columns) + column]);
            double current = values[(row * columns) + column];
            if (hasLower && hasUpper)
            {
                double lower = columnAxis
                    ? values[(row * columns) + column - 1]
                    : values[((row - 1) * columns) + column];
                double upper = columnAxis
                    ? values[(row * columns) + column + 1]
                    : values[((row + 1) * columns) + column];
                derivative = (upper - lower) / (2.0 * pitch);
                central = true;
            }
            else if (hasUpper)
            {
                double upper = columnAxis
                    ? values[(row * columns) + column + 1]
                    : values[((row + 1) * columns) + column];
                derivative = (upper - current) / pitch;
            }
            else if (hasLower)
            {
                double lower = columnAxis
                    ? values[(row * columns) + column - 1]
                    : values[((row - 1) * columns) + column];
                derivative = (current - lower) / pitch;
            }

            return IsFinite(derivative);
        }

        private static void Validate(
            HeightMap3D source,
            HeightMapNormalPreparationOptions options)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }
            if (options == null)
            {
                return;
            }
            if (!IsFinite(options.MinimumAlignmentCosine)
                || options.MinimumAlignmentCosine < -1.0
                || options.MinimumAlignmentCosine > 1.0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(options.MinimumAlignmentCosine));
            }
            if (options.ExpectedNormal != null)
            {
                Normalize(options.ExpectedNormal);
            }
        }

        private static ThreeDPoint Normalize(ThreeDPoint value)
        {
            if (value == null || !value.IsFinite)
            {
                throw new ArgumentException(
                    "Expected normal must contain finite coordinates.");
            }
            double length = Math.Sqrt(
                (value.X * value.X)
                + (value.Y * value.Y)
                + (value.Z * value.Z));
            if (!IsFinitePositive(length))
            {
                throw new ArgumentException(
                    "Expected normal must have a positive finite length.");
            }
            return new ThreeDPoint(
                value.X / length,
                value.Y / length,
                value.Z / length);
        }

        private static double Dot(ThreeDPoint left, ThreeDPoint right) =>
            (left.X * right.X) + (left.Y * right.Y) + (left.Z * right.Z);

        private static double Clamp(double value, double minimum, double maximum) =>
            value < minimum ? minimum : value > maximum ? maximum : value;

        private static HeightMapNormalPreparationResult Failed(
            string message)
        {
            return new HeightMapNormalPreparationResult(
                false,
                message,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                0,
                Array.Empty<HeightMapNormalSample>(),
                HeightMapNormalValidationState.NotRequested,
                0,
                0,
                0,
                double.NaN,
                double.NaN,
                double.NaN);
        }

        private static HeightMapNormalPreparationResult Failed(
            HeightMap3D source,
            string message,
            int inputFiniteSampleCount,
            int unavailableNormalCount,
            int centralDerivativeCount,
            int oneSidedDerivativeCount,
            int missingDerivativeCount)
        {
            return new HeightMapNormalPreparationResult(
                false,
                message,
                source.Rows,
                source.Columns,
                inputFiniteSampleCount,
                0,
                unavailableNormalCount,
                centralDerivativeCount,
                oneSidedDerivativeCount,
                missingDerivativeCount,
                Array.Empty<HeightMapNormalSample>(),
                HeightMapNormalValidationState.NotRequested,
                0,
                0,
                0,
                double.NaN,
                double.NaN,
                double.NaN);
        }

        private static bool IsFinite(double value) =>
            !double.IsNaN(value) && !double.IsInfinity(value);

        private static bool IsFinitePositive(double value) =>
            IsFinite(value) && value > 0.0;
    }
}
