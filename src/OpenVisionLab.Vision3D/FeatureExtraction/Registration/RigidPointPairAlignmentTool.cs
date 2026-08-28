using System;
using System.Collections.Generic;
using System.Threading;

namespace OpenVisionLab.Vision3D.FeatureExtraction
{
    /// <summary>
    /// One ordered source-to-reference 3D correspondence for the deterministic
    /// three-pair rigid alignment route.
    /// </summary>
    public sealed class RigidPointPairCorrespondence
    {
        public RigidPointPairCorrespondence(ThreeDPoint source, ThreeDPoint reference)
        {
            Source = source;
            Reference = reference;
        }

        public ThreeDPoint Source { get; }

        public ThreeDPoint Reference { get; }
    }

    public sealed class RigidPointPairAlignmentOptions
    {
        /// <summary>
        /// Maximum absolute difference between each source/reference pair
        /// length. The caller owns the unit and acceptance meaning.
        /// </summary>
        public double MaximumPairLengthError { get; set; } = 1e-9;

        /// <summary>
        /// Minimum normalized magnitude of the first two triangle edges'
        /// cross product. The value is divided by the squared maximum pair
        /// span, so the gate is scale-independent.
        /// </summary>
        public double MinimumNormalizedCrossMagnitude { get; set; } = 1e-12;
    }

    public sealed class RigidPointPairAlignmentPose
    {
        public RigidPointPairAlignmentPose(
            double m11, double m12, double m13, double m21, double m22,
            double m23, double m31, double m32, double m33,
            double translationX, double translationY, double translationZ)
        {
            M11 = m11;
            M12 = m12;
            M13 = m13;
            M21 = m21;
            M22 = m22;
            M23 = m23;
            M31 = m31;
            M32 = m32;
            M33 = m33;
            TranslationX = translationX;
            TranslationY = translationY;
            TranslationZ = translationZ;
        }

        public double M11 { get; }
        public double M12 { get; }
        public double M13 { get; }
        public double M21 { get; }
        public double M22 { get; }
        public double M23 { get; }
        public double M31 { get; }
        public double M32 { get; }
        public double M33 { get; }
        public double TranslationX { get; }
        public double TranslationY { get; }
        public double TranslationZ { get; }

        public ThreeDPoint Transform(ThreeDPoint point)
        {
            if (point == null) throw new ArgumentNullException(nameof(point));
            return new ThreeDPoint(
                (M11 * point.X) + (M12 * point.Y) + (M13 * point.Z) + TranslationX,
                (M21 * point.X) + (M22 * point.Y) + (M23 * point.Z) + TranslationY,
                (M31 * point.X) + (M32 * point.Y) + (M33 * point.Z) + TranslationZ);
        }

        public IReadOnlyList<double> ToRowMajor4X4()
        {
            return new[]
            {
                M11, M12, M13, TranslationX,
                M21, M22, M23, TranslationY,
                M31, M32, M33, TranslationZ,
                0.0, 0.0, 0.0, 1.0
            };
        }
    }

    public sealed class RigidPointPairAlignmentResidual
    {
        public RigidPointPairAlignmentResidual(
            int pairIndex,
            ThreeDPoint source,
            ThreeDPoint reference,
            ThreeDPoint transformed,
            ThreeDPoint residual,
            double residualNorm)
        {
            PairIndex = pairIndex;
            Source = source;
            Reference = reference;
            Transformed = transformed;
            Residual = residual;
            ResidualNorm = residualNorm;
        }

        public int PairIndex { get; }
        public ThreeDPoint Source { get; }
        public ThreeDPoint Reference { get; }
        public ThreeDPoint Transformed { get; }
        public ThreeDPoint Residual { get; }
        public double ResidualNorm { get; }
    }

    public sealed class RigidPointPairAlignmentResult
    {
        private RigidPointPairAlignmentResult(
            bool success,
            string message,
            RigidPointPairAlignmentPose pose,
            double sourceNormalizedCrossMagnitude,
            double referenceNormalizedCrossMagnitude,
            double maximumPairLengthError,
            double maximumObservedPairLengthError,
            double rmsResidual,
            double maximumResidual,
            IReadOnlyList<RigidPointPairAlignmentResidual> residuals)
        {
            Success = success;
            Message = message ?? string.Empty;
            Pose = pose;
            SourceNormalizedCrossMagnitude = sourceNormalizedCrossMagnitude;
            ReferenceNormalizedCrossMagnitude = referenceNormalizedCrossMagnitude;
            MaximumPairLengthError = maximumPairLengthError;
            MaximumObservedPairLengthError = maximumObservedPairLengthError;
            RmsResidual = rmsResidual;
            MaximumResidual = maximumResidual;
            Residuals = residuals ?? new RigidPointPairAlignmentResidual[0];
        }

        public bool Success { get; }
        public string Message { get; }
        public RigidPointPairAlignmentPose Pose { get; }
        public double SourceNormalizedCrossMagnitude { get; }
        public double ReferenceNormalizedCrossMagnitude { get; }
        public double MaximumPairLengthError { get; }
        public double MaximumObservedPairLengthError { get; }
        public double RmsResidual { get; }
        public double MaximumResidual { get; }
        public IReadOnlyList<RigidPointPairAlignmentResidual> Residuals { get; }

        internal static RigidPointPairAlignmentResult Completed(
            RigidPointPairAlignmentPose pose,
            double sourceNormalizedCrossMagnitude,
            double referenceNormalizedCrossMagnitude,
            double maximumPairLengthError,
            double maximumObservedPairLengthError,
            double rmsResidual,
            double maximumResidual,
            IReadOnlyList<RigidPointPairAlignmentResidual> residuals)
        {
            return new RigidPointPairAlignmentResult(
                true,
                "Completed deterministic rigid alignment from exactly three ordered point pairs.",
                pose,
                sourceNormalizedCrossMagnitude,
                referenceNormalizedCrossMagnitude,
                maximumPairLengthError,
                maximumObservedPairLengthError,
                rmsResidual,
                maximumResidual,
                residuals);
        }

        internal static RigidPointPairAlignmentResult Failed(string message)
        {
            return new RigidPointPairAlignmentResult(
                false,
                message,
                null,
                double.NaN,
                double.NaN,
                double.NaN,
                double.NaN,
                double.NaN,
                double.NaN,
                new RigidPointPairAlignmentResidual[0]);
        }
    }

    /// <summary>
    /// Constructs a proper source-to-reference rigid transform from exactly
    /// three ordered non-collinear point pairs. The first two edge vectors and
    /// their oriented cross product form each frame; no least-squares or
    /// best-fit policy is applied. Units, identities, acceptance, and source
    /// lifecycle remain caller-owned.
    /// </summary>
    public sealed class RigidPointPairAlignmentTool
    {
        private const int RequiredPairCount = 3;

        public RigidPointPairAlignmentResult Execute(
            IReadOnlyList<RigidPointPairCorrespondence> correspondences,
            RigidPointPairAlignmentOptions options,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            try
            {
                Validate(correspondences, options);
                cancellationToken.ThrowIfCancellationRequested();

                double sourceCross = NormalizedCrossMagnitude(
                    correspondences[0].Source,
                    correspondences[1].Source,
                    correspondences[2].Source);
                double referenceCross = NormalizedCrossMagnitude(
                    correspondences[0].Reference,
                    correspondences[1].Reference,
                    correspondences[2].Reference);
                if (!IsFinite(sourceCross) || sourceCross <= options.MinimumNormalizedCrossMagnitude)
                {
                    return RigidPointPairAlignmentResult.Failed(
                        "Source point triangle is collinear or below the minimum normalized cross-magnitude gate.");
                }
                if (!IsFinite(referenceCross) || referenceCross <= options.MinimumNormalizedCrossMagnitude)
                {
                    return RigidPointPairAlignmentResult.Failed(
                        "Reference point triangle is collinear or below the minimum normalized cross-magnitude gate.");
                }

                double maximumLengthError = 0.0;
                for (int first = 0; first < RequiredPairCount; first++)
                {
                    for (int second = first + 1; second < RequiredPairCount; second++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        double sourceLength = Distance(correspondences[first].Source, correspondences[second].Source);
                        double referenceLength = Distance(correspondences[first].Reference, correspondences[second].Reference);
                        double error = Math.Abs(sourceLength - referenceLength);
                        if (!IsFinite(error))
                        {
                            return RigidPointPairAlignmentResult.Failed("Rigid point-pair alignment produced a non-finite pair-length error.");
                        }
                        maximumLengthError = Math.Max(maximumLengthError, error);
                    }
                }
                if (maximumLengthError > options.MaximumPairLengthError)
                {
                    return RigidPointPairAlignmentResult.Failed(
                        "Source/reference point-pair lengths differ by "
                        + maximumLengthError.ToString("G8", System.Globalization.CultureInfo.InvariantCulture)
                        + "; the authored maximum is "
                        + options.MaximumPairLengthError.ToString("G8", System.Globalization.CultureInfo.InvariantCulture)
                        + ".");
                }

                Frame sourceFrame = BuildFrame(
                    correspondences[0].Source,
                    correspondences[1].Source,
                    correspondences[2].Source,
                    sourceCross,
                    options.MinimumNormalizedCrossMagnitude);
                Frame referenceFrame = BuildFrame(
                    correspondences[0].Reference,
                    correspondences[1].Reference,
                    correspondences[2].Reference,
                    referenceCross,
                    options.MinimumNormalizedCrossMagnitude);
                RigidPointPairAlignmentPose pose = ComposePose(
                    sourceFrame,
                    referenceFrame,
                    correspondences[0].Source,
                    correspondences[0].Reference);
                EnsureFinite(pose);

                List<RigidPointPairAlignmentResidual> residuals = new List<RigidPointPairAlignmentResidual>(RequiredPairCount);
                double squaredResidualSum = 0.0;
                double maximumResidual = 0.0;
                for (int index = 0; index < RequiredPairCount; index++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    RigidPointPairCorrespondence pair = correspondences[index];
                    ThreeDPoint transformed = pose.Transform(pair.Source);
                    ThreeDPoint residual = new ThreeDPoint(
                        pair.Reference.X - transformed.X,
                        pair.Reference.Y - transformed.Y,
                        pair.Reference.Z - transformed.Z);
                    double norm = Length(residual);
                    if (!IsFinite(norm) || !transformed.IsFinite || !residual.IsFinite)
                    {
                        return RigidPointPairAlignmentResult.Failed("Rigid point-pair alignment produced non-finite residual evidence.");
                    }
                    residuals.Add(new RigidPointPairAlignmentResidual(index, pair.Source, pair.Reference, transformed, residual, norm));
                    squaredResidualSum += norm * norm;
                    maximumResidual = Math.Max(maximumResidual, norm);
                }

                double rmsResidual = Math.Sqrt(squaredResidualSum / RequiredPairCount);
                if (!IsFinite(rmsResidual) || !IsFinite(maximumResidual))
                {
                    return RigidPointPairAlignmentResult.Failed("Rigid point-pair alignment produced non-finite residual metrics.");
                }
                return RigidPointPairAlignmentResult.Completed(
                    pose,
                    sourceCross,
                    referenceCross,
                    options.MaximumPairLengthError,
                    maximumLengthError,
                    rmsResidual,
                    maximumResidual,
                    residuals);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                return RigidPointPairAlignmentResult.Failed("Rigid point-pair alignment failed: " + exception.Message);
            }
        }

        private static void Validate(
            IReadOnlyList<RigidPointPairCorrespondence> correspondences,
            RigidPointPairAlignmentOptions options)
        {
            if (correspondences == null || correspondences.Count != RequiredPairCount)
            {
                throw new ArgumentException("Rigid point-pair alignment requires exactly three ordered source/reference pairs.");
            }
            if (options == null
                || !IsFinite(options.MaximumPairLengthError)
                || options.MaximumPairLengthError < 0.0)
            {
                throw new ArgumentException("MaximumPairLengthError must be a finite non-negative number.");
            }
            if (!IsFinite(options.MinimumNormalizedCrossMagnitude)
                || options.MinimumNormalizedCrossMagnitude <= 0.0
                || options.MinimumNormalizedCrossMagnitude >= 1.0)
            {
                throw new ArgumentException("MinimumNormalizedCrossMagnitude must be finite, greater than zero, and less than one.");
            }
            for (int index = 0; index < RequiredPairCount; index++)
            {
                RigidPointPairCorrespondence pair = correspondences[index];
                if (pair == null || pair.Source == null || pair.Reference == null
                    || !pair.Source.IsFinite || !pair.Reference.IsFinite)
                {
                    throw new ArgumentException("Rigid point-pair alignment requires finite source/reference coordinates.");
                }
            }
        }

        private static Frame BuildFrame(
            ThreeDPoint first,
            ThreeDPoint second,
            ThreeDPoint third,
            double normalizedCrossMagnitude,
            double minimumNormalizedCrossMagnitude)
        {
            Vector3d edgeA = Subtract(second, first);
            Vector3d edgeB = Subtract(third, first);
            double span = Math.Max(Distance(first, second), Math.Max(Distance(first, third), Distance(second, third)));
            Vector3d axisX = Normalize(edgeA);
            Vector3d orthogonal = edgeB - (axisX * Dot(edgeB, axisX));
            Vector3d axisY = Normalize(orthogonal);
            Vector3d axisZ = Normalize(Cross(axisX, axisY));
            if (!IsFinite(span)
                || span <= 0.0
                || !IsFinite(normalizedCrossMagnitude)
                || normalizedCrossMagnitude <= minimumNormalizedCrossMagnitude
                || !axisX.IsFinite
                || !axisY.IsFinite
                || !axisZ.IsFinite)
            {
                throw new ArgumentException("Rigid point-pair triangle frame is degenerate.");
            }

            return new Frame(axisX, axisY, axisZ);
        }

        private static RigidPointPairAlignmentPose ComposePose(
            Frame source,
            Frame reference,
            ThreeDPoint sourceAnchor,
            ThreeDPoint referenceAnchor)
        {
            double m11 = Entry(reference, source, 0, 0);
            double m12 = Entry(reference, source, 0, 1);
            double m13 = Entry(reference, source, 0, 2);
            double m21 = Entry(reference, source, 1, 0);
            double m22 = Entry(reference, source, 1, 1);
            double m23 = Entry(reference, source, 1, 2);
            double m31 = Entry(reference, source, 2, 0);
            double m32 = Entry(reference, source, 2, 1);
            double m33 = Entry(reference, source, 2, 2);
            return new RigidPointPairAlignmentPose(
                m11,
                m12,
                m13,
                m21,
                m22,
                m23,
                m31,
                m32,
                m33,
                referenceAnchor.X - ((m11 * sourceAnchor.X) + (m12 * sourceAnchor.Y) + (m13 * sourceAnchor.Z)),
                referenceAnchor.Y - ((m21 * sourceAnchor.X) + (m22 * sourceAnchor.Y) + (m23 * sourceAnchor.Z)),
                referenceAnchor.Z - ((m31 * sourceAnchor.X) + (m32 * sourceAnchor.Y) + (m33 * sourceAnchor.Z)));
        }

        private static double Entry(Frame reference, Frame source, int row, int column) =>
            (Component(reference.X, row) * Component(source.X, column))
            + (Component(reference.Y, row) * Component(source.Y, column))
            + (Component(reference.Z, row) * Component(source.Z, column));

        private static double Component(Vector3d value, int index) =>
            index == 0 ? value.X : index == 1 ? value.Y : value.Z;

        private static double NormalizedCrossMagnitude(ThreeDPoint first, ThreeDPoint second, ThreeDPoint third)
        {
            Vector3d edgeA = Subtract(second, first);
            Vector3d edgeB = Subtract(third, first);
            double span = Math.Max(Distance(first, second), Math.Max(Distance(first, third), Distance(second, third)));
            if (!IsFinite(span) || span <= 0.0) return 0.0;
            return Length(Cross(edgeA, edgeB)) / (span * span);
        }

        private static double Distance(ThreeDPoint first, ThreeDPoint second) =>
            Length(Subtract(second, first));

        private static Vector3d Subtract(ThreeDPoint left, ThreeDPoint right) =>
            new Vector3d(left.X - right.X, left.Y - right.Y, left.Z - right.Z);

        private static double Dot(Vector3d left, Vector3d right) =>
            (left.X * right.X) + (left.Y * right.Y) + (left.Z * right.Z);

        private static Vector3d Cross(Vector3d left, Vector3d right) =>
            new Vector3d(
                (left.Y * right.Z) - (left.Z * right.Y),
                (left.Z * right.X) - (left.X * right.Z),
                (left.X * right.Y) - (left.Y * right.X));

        private static Vector3d Normalize(Vector3d value)
        {
            double length = Length(value);
            if (!IsFinite(length) || length <= 0.0)
            {
                throw new ArgumentException("Rigid point-pair triangle contains a zero-length edge.");
            }
            return value / length;
        }

        private static double Length(Vector3d value) =>
            Math.Sqrt((value.X * value.X) + (value.Y * value.Y) + (value.Z * value.Z));

        private static double Length(ThreeDPoint value) =>
            Math.Sqrt((value.X * value.X) + (value.Y * value.Y) + (value.Z * value.Z));

        private static void EnsureFinite(RigidPointPairAlignmentPose pose)
        {
            double[] values =
            {
                pose.M11, pose.M12, pose.M13, pose.M21, pose.M22, pose.M23,
                pose.M31, pose.M32, pose.M33, pose.TranslationX,
                pose.TranslationY, pose.TranslationZ
            };
            for (int index = 0; index < values.Length; index++)
            {
                if (!IsFinite(values[index])) throw new ArgumentException("Rigid point-pair pose contains a non-finite value.");
            }
        }

        private static bool IsFinite(double value) =>
            !double.IsNaN(value) && !double.IsInfinity(value);

        private struct Vector3d
        {
            public Vector3d(double x, double y, double z)
            {
                X = x;
                Y = y;
                Z = z;
            }

            public double X { get; }
            public double Y { get; }
            public double Z { get; }
            public bool IsFinite => IsFinite(X) && IsFinite(Y) && IsFinite(Z);

            public static Vector3d operator *(Vector3d value, double scalar) =>
                new Vector3d(value.X * scalar, value.Y * scalar, value.Z * scalar);

            public static Vector3d operator /(Vector3d value, double scalar) =>
                new Vector3d(value.X / scalar, value.Y / scalar, value.Z / scalar);

            public static Vector3d operator -(Vector3d left, Vector3d right) =>
                new Vector3d(left.X - right.X, left.Y - right.Y, left.Z - right.Z);
        }

        private sealed class Frame
        {
            public Frame(Vector3d x, Vector3d y, Vector3d z)
            {
                X = x;
                Y = y;
                Z = z;
            }

            public Vector3d X { get; }
            public Vector3d Y { get; }
            public Vector3d Z { get; }
        }
    }
}
