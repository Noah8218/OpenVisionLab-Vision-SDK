using System;
using System.Collections.Generic;
using System.Threading;

namespace OpenVisionLab.Vision3D.FeatureExtraction
{
    public sealed class FullXyzAffineCorrespondence
    {
        public FullXyzAffineCorrespondence(ThreeDPoint source, ThreeDPoint reference)
        {
            Source = source;
            Reference = reference;
        }

        public ThreeDPoint Source { get; }

        public ThreeDPoint Reference { get; }
    }

    public sealed class FullXyzAffineSolveOptions
    {
        public double MaximumConditionEstimate { get; set; } = 1000000.0;

        public double ArithmeticResidualWarning { get; set; } = 0.001;
    }

    public sealed class FullXyzAffineMatrix
    {
        public FullXyzAffineMatrix(
            double m11, double m12, double m13, double m14,
            double m21, double m22, double m23, double m24,
            double m31, double m32, double m33, double m34)
        {
            M11 = m11; M12 = m12; M13 = m13; M14 = m14;
            M21 = m21; M22 = m22; M23 = m23; M24 = m24;
            M31 = m31; M32 = m32; M33 = m33; M34 = m34;
        }

        public double M11 { get; } public double M12 { get; } public double M13 { get; } public double M14 { get; }
        public double M21 { get; } public double M22 { get; } public double M23 { get; } public double M24 { get; }
        public double M31 { get; } public double M32 { get; } public double M33 { get; } public double M34 { get; }

        public ThreeDPoint Transform(ThreeDPoint point)
        {
            if (point == null) throw new ArgumentNullException(nameof(point));
            TransformCoordinates(point.X, point.Y, point.Z, out double x, out double y, out double z);
            return new ThreeDPoint(x, y, z);
        }

        /// <summary>
        /// Applies this matrix without allocating an intermediate point object.
        /// High-volume callers can retain their own value representation.
        /// </summary>
        public void TransformCoordinates(
            double x,
            double y,
            double z,
            out double transformedX,
            out double transformedY,
            out double transformedZ)
        {
            transformedX = (M11 * x) + (M12 * y) + (M13 * z) + M14;
            transformedY = (M21 * x) + (M22 * y) + (M23 * z) + M24;
            transformedZ = (M31 * x) + (M32 * y) + (M33 * z) + M34;
        }
    }

    public sealed class FullXyzAffineResidual
    {
        public FullXyzAffineResidual(ThreeDPoint source, ThreeDPoint reference, ThreeDPoint transformed, ThreeDPoint residual, double residualNorm)
        {
            Source = source;
            Reference = reference;
            Transformed = transformed;
            Residual = residual;
            ResidualNorm = residualNorm;
        }

        public ThreeDPoint Source { get; }
        public ThreeDPoint Reference { get; }
        public ThreeDPoint Transformed { get; }
        public ThreeDPoint Residual { get; }
        public double ResidualNorm { get; }
    }

    public sealed class FullXyzAffineSolveResult
    {
        private FullXyzAffineSolveResult(
            bool success,
            string message,
            FullXyzAffineMatrix matrix,
            double sourceAugmentedDeterminant,
            double linearDeterminantAbsolute,
            double conditionEstimate,
            double arithmeticRmsResidual,
            double arithmeticMaximumResidual,
            bool arithmeticResidualWarningExceeded,
            IReadOnlyList<FullXyzAffineResidual> residuals)
        {
            Success = success;
            Message = message ?? string.Empty;
            Matrix = matrix;
            SourceAugmentedDeterminant = sourceAugmentedDeterminant;
            LinearDeterminantAbsolute = linearDeterminantAbsolute;
            ConditionEstimate = conditionEstimate;
            ArithmeticRmsResidual = arithmeticRmsResidual;
            ArithmeticMaximumResidual = arithmeticMaximumResidual;
            ArithmeticResidualWarningExceeded = arithmeticResidualWarningExceeded;
            Residuals = residuals ?? new FullXyzAffineResidual[0];
        }

        public bool Success { get; }
        public string Message { get; }
        public FullXyzAffineMatrix Matrix { get; }
        public double SourceAugmentedDeterminant { get; }
        public double LinearDeterminantAbsolute { get; }
        public double ConditionEstimate { get; }
        public double ArithmeticRmsResidual { get; }
        public double ArithmeticMaximumResidual { get; }
        public bool ArithmeticResidualWarningExceeded { get; }
        public IReadOnlyList<FullXyzAffineResidual> Residuals { get; }

        internal static FullXyzAffineSolveResult Completed(
            FullXyzAffineMatrix matrix,
            double sourceAugmentedDeterminant,
            double linearDeterminantAbsolute,
            double conditionEstimate,
            double arithmeticRmsResidual,
            double arithmeticMaximumResidual,
            bool arithmeticResidualWarningExceeded,
            IReadOnlyList<FullXyzAffineResidual> residuals)
        {
            return new FullXyzAffineSolveResult(
                true,
                "Completed exact-four full-XYZ affine solve.",
                matrix,
                sourceAugmentedDeterminant,
                linearDeterminantAbsolute,
                conditionEstimate,
                arithmeticRmsResidual,
                arithmeticMaximumResidual,
                arithmeticResidualWarningExceeded,
                residuals);
        }

        internal static FullXyzAffineSolveResult Failed(string message)
        {
            return new FullXyzAffineSolveResult(false, message, null, double.NaN, double.NaN, double.NaN, double.NaN, double.NaN, false, new FullXyzAffineResidual[0]);
        }
    }

    /// <summary>
    /// Deterministic source-to-reference affine solver for exactly four
    /// independent XYZ pairs. It uses scaled partial pivoting, never normal
    /// equations or a planar fallback, and does not transform a point cloud.
    /// </summary>
    public sealed class FullXyzAffineSolveTool
    {
        private const int RequiredPairCount = 4;
        private const double PivotRelativeTolerance = 1e-12;

        public FullXyzAffineSolveResult Execute(
            IReadOnlyList<FullXyzAffineCorrespondence> correspondences,
            FullXyzAffineSolveOptions options,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            try
            {
                Validate(correspondences, options);
                cancellationToken.ThrowIfCancellationRequested();
                double[][] source = new double[RequiredPairCount][];
                ThreeDPoint sourceOrigin = correspondences[0].Source;
                ThreeDPoint referenceOrigin = correspondences[0].Reference;
                for (int index = 0; index < RequiredPairCount; index++)
                {
                    FullXyzAffineCorrespondence pair = correspondences[index];
                    source[index] = new[]
                    {
                        pair.Source.X - sourceOrigin.X,
                        pair.Source.Y - sourceOrigin.Y,
                        pair.Source.Z - sourceOrigin.Z,
                        1.0
                    };
                }

                double[,] inverse = InvertScaledPartialPivot(source, cancellationToken);
                double determinant = DeterminantScaledPartialPivot(source, cancellationToken);
                double condition = InfinityNorm(source) * InfinityNorm(inverse);
                if (!IsFinite(condition) || condition > options.MaximumConditionEstimate)
                {
                    return FullXyzAffineSolveResult.Failed("Full XYZ affine solve rejected source correspondence condition estimate " + condition.ToString("G8") + "; taught maximum is " + options.MaximumConditionEstimate.ToString("G8") + ".");
                }

                double[,] coefficients = new double[RequiredPairCount, 3];
                for (int row = 0; row < RequiredPairCount; row++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    for (int coordinate = 0; coordinate < 3; coordinate++)
                    {
                        for (int index = 0; index < RequiredPairCount; index++)
                        {
                            coefficients[row, coordinate] += inverse[row, index]
                                * (Reference(correspondences[index].Reference, coordinate)
                                    - Reference(referenceOrigin, coordinate));
                        }
                    }
                }

                coefficients[3, 0] += referenceOrigin.X
                    - (coefficients[0, 0] * sourceOrigin.X)
                    - (coefficients[1, 0] * sourceOrigin.Y)
                    - (coefficients[2, 0] * sourceOrigin.Z);
                coefficients[3, 1] += referenceOrigin.Y
                    - (coefficients[0, 1] * sourceOrigin.X)
                    - (coefficients[1, 1] * sourceOrigin.Y)
                    - (coefficients[2, 1] * sourceOrigin.Z);
                coefficients[3, 2] += referenceOrigin.Z
                    - (coefficients[0, 2] * sourceOrigin.X)
                    - (coefficients[1, 2] * sourceOrigin.Y)
                    - (coefficients[2, 2] * sourceOrigin.Z);

                FullXyzAffineMatrix matrix = new FullXyzAffineMatrix(
                    coefficients[0, 0], coefficients[1, 0], coefficients[2, 0], coefficients[3, 0],
                    coefficients[0, 1], coefficients[1, 1], coefficients[2, 1], coefficients[3, 1],
                    coefficients[0, 2], coefficients[1, 2], coefficients[2, 2], coefficients[3, 2]);
                EnsureFinite(matrix);
                double linearDeterminantAbsolute = Math.Abs(Determinant3x3(matrix));
                List<FullXyzAffineResidual> residuals = new List<FullXyzAffineResidual>();
                double maximumResidual = 0.0;
                double squaredResidualSum = 0.0;
                for (int index = 0; index < RequiredPairCount; index++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    FullXyzAffineCorrespondence pair = correspondences[index];
                    ThreeDPoint transformed = matrix.Transform(pair.Source);
                    ThreeDPoint residual = new ThreeDPoint(
                        pair.Reference.X - transformed.X,
                        pair.Reference.Y - transformed.Y,
                        pair.Reference.Z - transformed.Z);
                    double norm = Math.Sqrt((residual.X * residual.X) + (residual.Y * residual.Y) + (residual.Z * residual.Z));
                    if (!residual.IsFinite || !IsFinite(norm))
                    {
                        return FullXyzAffineSolveResult.Failed("Full XYZ affine solve produced non-finite residual evidence.");
                    }
                    residuals.Add(new FullXyzAffineResidual(pair.Source, pair.Reference, transformed, residual, norm));
                    maximumResidual = Math.Max(maximumResidual, norm);
                    squaredResidualSum += norm * norm;
                }

                double rmsResidual = Math.Sqrt(squaredResidualSum / RequiredPairCount);
                if (!IsFinite(determinant) || !IsFinite(linearDeterminantAbsolute) || !IsFinite(rmsResidual) || !IsFinite(maximumResidual))
                {
                    return FullXyzAffineSolveResult.Failed("Full XYZ affine solve produced non-finite matrix evidence.");
                }
                return FullXyzAffineSolveResult.Completed(
                    matrix,
                    determinant,
                    linearDeterminantAbsolute,
                    condition,
                    rmsResidual,
                    maximumResidual,
                    maximumResidual > options.ArithmeticResidualWarning,
                    residuals);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                return FullXyzAffineSolveResult.Failed("Full XYZ affine solve failed: " + exception.Message);
            }
        }

        private static void Validate(IReadOnlyList<FullXyzAffineCorrespondence> correspondences, FullXyzAffineSolveOptions options)
        {
            if (correspondences == null || correspondences.Count != RequiredPairCount)
            {
                throw new ArgumentException("Full XYZ affine solve requires exactly four correspondence pairs.");
            }
            if (options == null || !IsFinite(options.MaximumConditionEstimate) || options.MaximumConditionEstimate <= 0.0)
            {
                throw new ArgumentException("MaximumConditionEstimate must be a finite positive number.");
            }
            if (!IsFinite(options.ArithmeticResidualWarning) || options.ArithmeticResidualWarning < 0.0)
            {
                throw new ArgumentException("ArithmeticResidualWarning must be a finite non-negative number.");
            }
            for (int index = 0; index < correspondences.Count; index++)
            {
                if (correspondences[index] == null || correspondences[index].Source == null || correspondences[index].Reference == null
                    || !correspondences[index].Source.IsFinite || !correspondences[index].Reference.IsFinite)
                {
                    throw new ArgumentException("Full XYZ affine solve requires finite source/reference coordinates.");
                }
            }
        }

        private static double[,] InvertScaledPartialPivot(double[][] source, CancellationToken cancellationToken)
        {
            double[,] augmented = new double[RequiredPairCount, RequiredPairCount * 2];
            double[] scales = new double[RequiredPairCount];
            int row;
            int column;
            for (row = 0; row < RequiredPairCount; row++)
            {
                for (column = 0; column < RequiredPairCount; column++)
                {
                    augmented[row, column] = source[row][column];
                    augmented[row, RequiredPairCount + column] = row == column ? 1.0 : 0.0;
                }
                scales[row] = MaximumAbsolute(source[row]);
                if (scales[row] <= 0.0 || !IsFinite(scales[row])) throw new ArgumentException("Full XYZ affine source matrix row is singular.");
            }
            for (column = 0; column < RequiredPairCount; column++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int pivotRow = SelectPivotRow(augmented, scales, column);
                double pivot = augmented[pivotRow, column];
                if (Math.Abs(pivot) <= scales[pivotRow] * PivotRelativeTolerance)
                {
                    throw new ArgumentException("Full XYZ affine source matrix failed the scaled partial-pivot independence gate.");
                }
                SwapRows(augmented, column, pivotRow);
                double scale = scales[column]; scales[column] = scales[pivotRow]; scales[pivotRow] = scale;
                pivot = augmented[column, column];
                for (int entry = 0; entry < RequiredPairCount * 2; entry++) augmented[column, entry] /= pivot;
                for (row = 0; row < RequiredPairCount; row++)
                {
                    if (row == column) continue;
                    double factor = augmented[row, column];
                    for (int entry = 0; entry < RequiredPairCount * 2; entry++) augmented[row, entry] -= factor * augmented[column, entry];
                }
            }
            double[,] inverse = new double[RequiredPairCount, RequiredPairCount];
            for (row = 0; row < RequiredPairCount; row++)
            {
                for (column = 0; column < RequiredPairCount; column++) inverse[row, column] = augmented[row, RequiredPairCount + column];
            }
            return inverse;
        }

        private static double DeterminantScaledPartialPivot(double[][] source, CancellationToken cancellationToken)
        {
            double[][] matrix = new double[RequiredPairCount][];
            double[] scales = new double[RequiredPairCount];
            for (int row = 0; row < RequiredPairCount; row++)
            {
                matrix[row] = (double[])source[row].Clone();
                scales[row] = MaximumAbsolute(matrix[row]);
            }
            double sign = 1.0;
            double determinant = 1.0;
            for (int column = 0; column < RequiredPairCount; column++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int pivotRow = SelectPivotRow(matrix, scales, column);
                double pivot = matrix[pivotRow][column];
                if (Math.Abs(pivot) <= scales[pivotRow] * PivotRelativeTolerance)
                {
                    throw new ArgumentException("Full XYZ affine source determinant is singular.");
                }
                if (pivotRow != column)
                {
                    double[] row = matrix[column]; matrix[column] = matrix[pivotRow]; matrix[pivotRow] = row;
                    double scale = scales[column]; scales[column] = scales[pivotRow]; scales[pivotRow] = scale;
                    sign = -sign;
                }
                pivot = matrix[column][column];
                determinant *= pivot;
                for (int row = column + 1; row < RequiredPairCount; row++)
                {
                    double factor = matrix[row][column] / pivot;
                    for (int entry = column + 1; entry < RequiredPairCount; entry++) matrix[row][entry] -= factor * matrix[column][entry];
                }
            }
            return sign * determinant;
        }

        private static int SelectPivotRow(double[,] matrix, double[] scales, int column)
        {
            int pivotRow = column;
            double best = -1.0;
            for (int row = column; row < RequiredPairCount; row++)
            {
                double candidate = Math.Abs(matrix[row, column]) / scales[row];
                if (candidate > best)
                {
                    best = candidate;
                    pivotRow = row;
                }
            }
            return pivotRow;
        }

        private static int SelectPivotRow(double[][] matrix, double[] scales, int column)
        {
            int pivotRow = column;
            double best = -1.0;
            for (int row = column; row < RequiredPairCount; row++)
            {
                double candidate = Math.Abs(matrix[row][column]) / scales[row];
                if (candidate > best)
                {
                    best = candidate;
                    pivotRow = row;
                }
            }
            return pivotRow;
        }

        private static void SwapRows(double[,] matrix, int first, int second)
        {
            if (first == second) return;
            for (int column = 0; column < matrix.GetLength(1); column++)
            {
                double value = matrix[first, column];
                matrix[first, column] = matrix[second, column];
                matrix[second, column] = value;
            }
        }

        private static double Reference(ThreeDPoint point, int coordinate)
        {
            return coordinate == 0 ? point.X : coordinate == 1 ? point.Y : point.Z;
        }

        private static double MaximumAbsolute(double[] values)
        {
            double maximum = 0.0;
            for (int index = 0; index < values.Length; index++) maximum = Math.Max(maximum, Math.Abs(values[index]));
            return maximum;
        }

        private static double InfinityNorm(double[][] matrix)
        {
            double maximum = 0.0;
            for (int row = 0; row < RequiredPairCount; row++)
            {
                double sum = 0.0;
                for (int column = 0; column < RequiredPairCount; column++) sum += Math.Abs(matrix[row][column]);
                maximum = Math.Max(maximum, sum);
            }
            return maximum;
        }

        private static double InfinityNorm(double[,] matrix)
        {
            double maximum = 0.0;
            for (int row = 0; row < RequiredPairCount; row++)
            {
                double sum = 0.0;
                for (int column = 0; column < RequiredPairCount; column++) sum += Math.Abs(matrix[row, column]);
                maximum = Math.Max(maximum, sum);
            }
            return maximum;
        }

        private static double Determinant3x3(FullXyzAffineMatrix matrix)
        {
            return matrix.M11 * ((matrix.M22 * matrix.M33) - (matrix.M23 * matrix.M32))
                - matrix.M12 * ((matrix.M21 * matrix.M33) - (matrix.M23 * matrix.M31))
                + matrix.M13 * ((matrix.M21 * matrix.M32) - (matrix.M22 * matrix.M31));
        }

        private static void EnsureFinite(FullXyzAffineMatrix matrix)
        {
            double[] values =
            {
                matrix.M11, matrix.M12, matrix.M13, matrix.M14,
                matrix.M21, matrix.M22, matrix.M23, matrix.M24,
                matrix.M31, matrix.M32, matrix.M33, matrix.M34
            };
            for (int index = 0; index < values.Length; index++)
            {
                if (!IsFinite(values[index])) throw new ArgumentException("Full XYZ affine matrix contains a non-finite value.");
            }
        }

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }
    }
}
