using System;
using System.Collections.Generic;
using System.Threading;

namespace OpenVisionLab.Vision3D.FeatureExtraction
{
    public sealed class LandmarkCorrespondenceValidationResult
    {
        internal LandmarkCorrespondenceValidationResult(
            bool success,
            string message,
            int sourceRank,
            int referenceRank,
            double sourceNormalizedTetrahedronVolume,
            double referenceNormalizedTetrahedronVolume)
        {
            Success = success;
            Message = message ?? string.Empty;
            SourceRank = sourceRank;
            ReferenceRank = referenceRank;
            SourceNormalizedTetrahedronVolume = sourceNormalizedTetrahedronVolume;
            ReferenceNormalizedTetrahedronVolume = referenceNormalizedTetrahedronVolume;
        }

        public bool Success { get; }
        public string Message { get; }
        public int SourceRank { get; }
        public int ReferenceRank { get; }
        public double SourceNormalizedTetrahedronVolume { get; }
        public double ReferenceNormalizedTetrahedronVolume { get; }
    }

    /// <summary>
    /// Verifies that exactly four source/reference landmarks form independent
    /// tetrahedra. Landmark identity, pairing order, units, frames, recipe
    /// lifecycle, and affine solving remain caller-owned.
    /// </summary>
    public sealed class LandmarkCorrespondenceValidationTool
    {
        private const int RequiredPairCount = 4;
        private const double RankRelativeTolerance = 1e-12;

        public LandmarkCorrespondenceValidationResult Execute(
            IReadOnlyList<ThreeDPoint> sourceLandmarks,
            IReadOnlyList<ThreeDPoint> referenceLandmarks,
            double minimumNormalizedTetrahedronVolume,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            Validate(sourceLandmarks, referenceLandmarks, minimumNormalizedTetrahedronVolume);
            cancellationToken.ThrowIfCancellationRequested();
            int sourceRank = GetAugmentedRank(sourceLandmarks, cancellationToken);
            int referenceRank = GetAugmentedRank(referenceLandmarks, cancellationToken);
            double sourceVolume = GetNormalizedTetrahedronVolume(sourceLandmarks);
            double referenceVolume = GetNormalizedTetrahedronVolume(referenceLandmarks);
            if (sourceRank < RequiredPairCount
                || sourceVolume <= minimumNormalizedTetrahedronVolume)
            {
                return new LandmarkCorrespondenceValidationResult(
                    false,
                    string.Format(
                        System.Globalization.CultureInfo.InvariantCulture,
                        "Source landmark tetrahedron is not affine-independent (rank {0}/4, normalized volume {1:G8}, taught minimum {2:G8}).",
                        sourceRank,
                        sourceVolume,
                        minimumNormalizedTetrahedronVolume),
                    sourceRank,
                    referenceRank,
                    sourceVolume,
                    referenceVolume);
            }
            if (referenceRank < RequiredPairCount
                || referenceVolume <= minimumNormalizedTetrahedronVolume)
            {
                return new LandmarkCorrespondenceValidationResult(
                    false,
                    string.Format(
                        System.Globalization.CultureInfo.InvariantCulture,
                        "Reference landmark tetrahedron is not affine-independent (rank {0}/4, normalized volume {1:G8}, taught minimum {2:G8}).",
                        referenceRank,
                        referenceVolume,
                        minimumNormalizedTetrahedronVolume),
                    sourceRank,
                    referenceRank,
                    sourceVolume,
                    referenceVolume);
            }

            return new LandmarkCorrespondenceValidationResult(
                true,
                "Source and reference landmark tetrahedra satisfy the taught independence gate.",
                sourceRank,
                referenceRank,
                sourceVolume,
                referenceVolume);
        }

        private static void Validate(
            IReadOnlyList<ThreeDPoint> sourceLandmarks,
            IReadOnlyList<ThreeDPoint> referenceLandmarks,
            double minimumNormalizedTetrahedronVolume)
        {
            if (sourceLandmarks == null) throw new ArgumentNullException(nameof(sourceLandmarks));
            if (referenceLandmarks == null) throw new ArgumentNullException(nameof(referenceLandmarks));
            if (sourceLandmarks.Count != RequiredPairCount
                || referenceLandmarks.Count != RequiredPairCount)
            {
                throw new ArgumentException("Landmark correspondence validation requires exactly four source/reference pairs.");
            }
            if (!IsFinite(minimumNormalizedTetrahedronVolume)
                || minimumNormalizedTetrahedronVolume <= 0.0
                || minimumNormalizedTetrahedronVolume >= 1.0)
            {
                throw new ArgumentOutOfRangeException(nameof(minimumNormalizedTetrahedronVolume));
            }
            for (int index = 0; index < RequiredPairCount; index++)
            {
                if (!IsFinite(sourceLandmarks[index]) || !IsFinite(referenceLandmarks[index]))
                {
                    throw new ArgumentException("Landmark correspondence validation requires finite coordinates.");
                }
            }
        }

        private static int GetAugmentedRank(
            IReadOnlyList<ThreeDPoint> points,
            CancellationToken cancellationToken)
        {
            double[][] matrix = new double[RequiredPairCount][];
            double maximum = 0.0;
            ThreeDPoint origin = points[0];
            for (int row = 0; row < RequiredPairCount; row++)
            {
                matrix[row] = new[]
                {
                    points[row].X - origin.X,
                    points[row].Y - origin.Y,
                    points[row].Z - origin.Z,
                    1.0
                };
                for (int column = 0; column < RequiredPairCount; column++)
                {
                    maximum = Math.Max(maximum, Math.Abs(matrix[row][column]));
                }
            }
            double tolerance = Math.Max(1.0, maximum) * RankRelativeTolerance;
            int rank = 0;
            for (int column = 0; column < RequiredPairCount && rank < matrix.Length; column++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int pivotRow = rank;
                double pivotAbsolute = Math.Abs(matrix[pivotRow][column]);
                for (int row = rank + 1; row < matrix.Length; row++)
                {
                    double candidate = Math.Abs(matrix[row][column]);
                    if (candidate > pivotAbsolute)
                    {
                        pivotRow = row;
                        pivotAbsolute = candidate;
                    }
                }
                if (pivotAbsolute <= tolerance) continue;
                double[] swap = matrix[rank];
                matrix[rank] = matrix[pivotRow];
                matrix[pivotRow] = swap;
                double divisor = matrix[rank][column];
                for (int target = rank + 1; target < matrix.Length; target++)
                {
                    double factor = matrix[target][column] / divisor;
                    for (int entry = column; entry < RequiredPairCount; entry++)
                    {
                        matrix[target][entry] -= factor * matrix[rank][entry];
                    }
                }
                rank++;
            }
            return rank;
        }

        private static double GetNormalizedTetrahedronVolume(IReadOnlyList<ThreeDPoint> points)
        {
            ThreeDPoint a = Subtract(points[1], points[0]);
            ThreeDPoint b = Subtract(points[2], points[0]);
            ThreeDPoint c = Subtract(points[3], points[0]);
            double volume6 = Math.Abs(Dot(a, Cross(b, c)));
            double span = 0.0;
            for (int first = 0; first < points.Count; first++)
            {
                for (int second = first + 1; second < points.Count; second++)
                {
                    span = Math.Max(span, Math.Sqrt(LengthSquared(Subtract(points[second], points[first]))));
                }
            }
            return span <= 0.0 || !IsFinite(span)
                ? 0.0
                : volume6 / (span * span * span);
        }

        private static bool IsFinite(ThreeDPoint point)
        {
            return point != null && IsFinite(point.X) && IsFinite(point.Y) && IsFinite(point.Z);
        }

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }

        private static ThreeDPoint Subtract(ThreeDPoint left, ThreeDPoint right)
        {
            return new ThreeDPoint(left.X - right.X, left.Y - right.Y, left.Z - right.Z);
        }

        private static ThreeDPoint Cross(ThreeDPoint left, ThreeDPoint right)
        {
            return new ThreeDPoint(
                left.Y * right.Z - left.Z * right.Y,
                left.Z * right.X - left.X * right.Z,
                left.X * right.Y - left.Y * right.X);
        }

        private static double Dot(ThreeDPoint left, ThreeDPoint right)
        {
            return left.X * right.X + left.Y * right.Y + left.Z * right.Z;
        }

        private static double LengthSquared(ThreeDPoint point)
        {
            return Dot(point, point);
        }
    }
}
