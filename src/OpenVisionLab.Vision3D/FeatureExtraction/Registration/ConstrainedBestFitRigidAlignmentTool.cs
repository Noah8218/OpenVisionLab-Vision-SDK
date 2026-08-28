using System;
using System.Collections.Generic;
using System.Threading;

namespace OpenVisionLab.Vision3D.FeatureExtraction
{
    /// <summary>
    /// One ordered full-XYZ source-to-reference correspondence for the
    /// constrained best-fit rigid alignment route.
    /// </summary>
    public sealed class ConstrainedBestFitRigidCorrespondence
    {
        public ConstrainedBestFitRigidCorrespondence(ThreeDPoint source, ThreeDPoint reference)
        {
            Source = source;
            Reference = reference;
        }

        public ThreeDPoint Source { get; }

        public ThreeDPoint Reference { get; }
    }

    public sealed class ConstrainedBestFitRigidAlignmentOptions
    {
        /// <summary>
        /// Upper bound on the number of ordered pairs accepted by this
        /// bounded route. The implementation never accepts more than 64.
        /// </summary>
        public int MaximumCorrespondenceCount { get; set; } = 64;

        /// <summary>
        /// Minimum normalized distance from the farthest-pair line for at
        /// least one point. This is scale-independent and rejects collinear
        /// correspondence sets without requiring full 3D rank.
        /// </summary>
        public double MinimumNormalizedLineSpread { get; set; } = 1e-12;

        /// <summary>
        /// Diagnostic-only residual review threshold. It does not decide
        /// product acceptance.
        /// </summary>
        public double ArithmeticResidualWarning { get; set; } = 0.001;
    }

    public sealed class ConstrainedBestFitRigidAlignmentPose
    {
        public ConstrainedBestFitRigidAlignmentPose(
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

    public sealed class ConstrainedBestFitRigidAlignmentResidual
    {
        public ConstrainedBestFitRigidAlignmentResidual(
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

    public sealed class ConstrainedBestFitRigidAlignmentResult
    {
        private ConstrainedBestFitRigidAlignmentResult(
            bool success,
            string message,
            ConstrainedBestFitRigidAlignmentPose pose,
            int pairCount,
            int maximumCorrespondenceCount,
            double minimumNormalizedLineSpread,
            double sourceNormalizedLineSpread,
            double referenceNormalizedLineSpread,
            ThreeDPoint sourceCentroid,
            ThreeDPoint referenceCentroid,
            double arithmeticResidualWarning,
            double rmsResidual,
            double maximumResidual,
            bool arithmeticResidualWarningExceeded,
            IReadOnlyList<ConstrainedBestFitRigidAlignmentResidual> residuals)
        {
            Success = success;
            Message = message ?? string.Empty;
            Pose = pose;
            PairCount = pairCount;
            MaximumCorrespondenceCount = maximumCorrespondenceCount;
            MinimumNormalizedLineSpread = minimumNormalizedLineSpread;
            SourceNormalizedLineSpread = sourceNormalizedLineSpread;
            ReferenceNormalizedLineSpread = referenceNormalizedLineSpread;
            SourceCentroid = sourceCentroid;
            ReferenceCentroid = referenceCentroid;
            ArithmeticResidualWarning = arithmeticResidualWarning;
            RmsResidual = rmsResidual;
            MaximumResidual = maximumResidual;
            ArithmeticResidualWarningExceeded = arithmeticResidualWarningExceeded;
            Residuals = residuals ?? new ConstrainedBestFitRigidAlignmentResidual[0];
        }

        public bool Success { get; }
        public string Message { get; }
        public ConstrainedBestFitRigidAlignmentPose Pose { get; }
        public int PairCount { get; }
        public int MaximumCorrespondenceCount { get; }
        public bool UsedAllCorrespondences => Success && PairCount == Residuals.Count;
        public double MinimumNormalizedLineSpread { get; }
        public double SourceNormalizedLineSpread { get; }
        public double ReferenceNormalizedLineSpread { get; }
        public ThreeDPoint SourceCentroid { get; }
        public ThreeDPoint ReferenceCentroid { get; }
        public double ArithmeticResidualWarning { get; }
        public double RmsResidual { get; }
        public double MaximumResidual { get; }
        public bool ArithmeticResidualWarningExceeded { get; }
        public IReadOnlyList<ConstrainedBestFitRigidAlignmentResidual> Residuals { get; }

        internal static ConstrainedBestFitRigidAlignmentResult Completed(
            ConstrainedBestFitRigidAlignmentPose pose,
            int pairCount,
            int maximumCorrespondenceCount,
            double minimumNormalizedLineSpread,
            double sourceNormalizedLineSpread,
            double referenceNormalizedLineSpread,
            ThreeDPoint sourceCentroid,
            ThreeDPoint referenceCentroid,
            double arithmeticResidualWarning,
            double rmsResidual,
            double maximumResidual,
            bool arithmeticResidualWarningExceeded,
            IReadOnlyList<ConstrainedBestFitRigidAlignmentResidual> residuals)
        {
            return new ConstrainedBestFitRigidAlignmentResult(
                true,
                "Completed constrained all-correspondence proper-rigid best-fit alignment.",
                pose,
                pairCount,
                maximumCorrespondenceCount,
                minimumNormalizedLineSpread,
                sourceNormalizedLineSpread,
                referenceNormalizedLineSpread,
                sourceCentroid,
                referenceCentroid,
                arithmeticResidualWarning,
                rmsResidual,
                maximumResidual,
                arithmeticResidualWarningExceeded,
                residuals);
        }

        internal static ConstrainedBestFitRigidAlignmentResult Failed(string message)
        {
            return new ConstrainedBestFitRigidAlignmentResult(
                false,
                message,
                null,
                0,
                0,
                double.NaN,
                double.NaN,
                double.NaN,
                null,
                null,
                double.NaN,
                double.NaN,
                double.NaN,
                false,
                new ConstrainedBestFitRigidAlignmentResidual[0]);
        }
    }

    /// <summary>
    /// Deterministic all-correspondence proper-rigid least-squares alignment.
    /// The pose domain is constrained to rotation plus translation: no scale,
    /// shear, reflection, weighting, or automatic outlier rejection is used.
    /// Four to sixty-four ordered pairs are accepted and every pair contributes
    /// to one Horn quaternion solution. Units, identity, frame endpoints,
    /// acceptance, and cloud lifecycle remain caller-owned.
    /// </summary>
    public sealed class ConstrainedBestFitRigidAlignmentTool
    {
        private const int MinimumCorrespondenceCount = 4;
        private const int MaximumCorrespondenceCount = 64;
        private const int MaximumJacobiIterations = 64;
        private const double JacobiRelativeTolerance = 1e-15;

        public ConstrainedBestFitRigidAlignmentResult Execute(
            IReadOnlyList<ConstrainedBestFitRigidCorrespondence> correspondences,
            ConstrainedBestFitRigidAlignmentOptions options,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            try
            {
                Validate(correspondences, options);
                cancellationToken.ThrowIfCancellationRequested();

                ThreeDPoint sourceCentroid = Centroid(correspondences, true, cancellationToken);
                ThreeDPoint referenceCentroid = Centroid(correspondences, false, cancellationToken);
                double sourceSpread = NormalizedLineSpread(correspondences, true, cancellationToken);
                double referenceSpread = NormalizedLineSpread(correspondences, false, cancellationToken);
                if (!IsFinite(sourceSpread) || sourceSpread <= options.MinimumNormalizedLineSpread)
                {
                    return ConstrainedBestFitRigidAlignmentResult.Failed(
                        "Source correspondence set is collinear or below the minimum normalized line-spread gate.");
                }
                if (!IsFinite(referenceSpread) || referenceSpread <= options.MinimumNormalizedLineSpread)
                {
                    return ConstrainedBestFitRigidAlignmentResult.Failed(
                        "Reference correspondence set is collinear or below the minimum normalized line-spread gate.");
                }

                double[,] covariance = BuildCovariance(
                    correspondences,
                    sourceCentroid,
                    referenceCentroid,
                    cancellationToken);
                double[,] horn = BuildHornMatrix(covariance);
                double[] quaternion = LargestEigenvector(horn, cancellationToken);
                ConstrainedBestFitRigidAlignmentPose pose = CreatePose(
                    quaternion,
                    sourceCentroid,
                    referenceCentroid);
                EnsureFinite(pose);

                List<ConstrainedBestFitRigidAlignmentResidual> residuals =
                    new List<ConstrainedBestFitRigidAlignmentResidual>(correspondences.Count);
                double squaredResidualSum = 0.0;
                double maximumResidual = 0.0;
                for (int index = 0; index < correspondences.Count; index++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    ConstrainedBestFitRigidCorrespondence pair = correspondences[index];
                    ThreeDPoint transformed = pose.Transform(pair.Source);
                    ThreeDPoint residual = new ThreeDPoint(
                        pair.Reference.X - transformed.X,
                        pair.Reference.Y - transformed.Y,
                        pair.Reference.Z - transformed.Z);
                    double norm = Length(residual);
                    if (!transformed.IsFinite || !residual.IsFinite || !IsFinite(norm))
                    {
                        return ConstrainedBestFitRigidAlignmentResult.Failed(
                            "Constrained best-fit rigid alignment produced non-finite residual evidence.");
                    }
                    residuals.Add(new ConstrainedBestFitRigidAlignmentResidual(
                        index,
                        pair.Source,
                        pair.Reference,
                        transformed,
                        residual,
                        norm));
                    squaredResidualSum += norm * norm;
                    maximumResidual = Math.Max(maximumResidual, norm);
                }

                double rmsResidual = Math.Sqrt(squaredResidualSum / correspondences.Count);
                if (!IsFinite(rmsResidual) || !IsFinite(maximumResidual))
                {
                    return ConstrainedBestFitRigidAlignmentResult.Failed(
                        "Constrained best-fit rigid alignment produced non-finite residual metrics.");
                }

                return ConstrainedBestFitRigidAlignmentResult.Completed(
                    pose,
                    correspondences.Count,
                    options.MaximumCorrespondenceCount,
                    options.MinimumNormalizedLineSpread,
                    sourceSpread,
                    referenceSpread,
                    sourceCentroid,
                    referenceCentroid,
                    options.ArithmeticResidualWarning,
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
                return ConstrainedBestFitRigidAlignmentResult.Failed(
                    "Constrained best-fit rigid alignment failed: " + exception.Message);
            }
        }

        private static void Validate(
            IReadOnlyList<ConstrainedBestFitRigidCorrespondence> correspondences,
            ConstrainedBestFitRigidAlignmentOptions options)
        {
            if (correspondences == null
                || correspondences.Count < MinimumCorrespondenceCount
                || correspondences.Count > MaximumCorrespondenceCount)
            {
                throw new ArgumentException(
                    "Constrained best-fit rigid alignment requires four to sixty-four ordered correspondence pairs.");
            }
            if (options == null
                || options.MaximumCorrespondenceCount < MinimumCorrespondenceCount
                || options.MaximumCorrespondenceCount > MaximumCorrespondenceCount)
            {
                throw new ArgumentException(
                    "MaximumCorrespondenceCount must be between four and sixty-four.");
            }
            if (correspondences.Count > options.MaximumCorrespondenceCount)
            {
                throw new ArgumentException(
                    "The correspondence count exceeds the authored maximum correspondence-count gate.");
            }
            if (!IsFinite(options.MinimumNormalizedLineSpread)
                || options.MinimumNormalizedLineSpread <= 0.0
                || options.MinimumNormalizedLineSpread >= 1.0)
            {
                throw new ArgumentException(
                    "MinimumNormalizedLineSpread must be finite, greater than zero, and less than one.");
            }
            if (!IsFinite(options.ArithmeticResidualWarning)
                || options.ArithmeticResidualWarning < 0.0)
            {
                throw new ArgumentException(
                    "ArithmeticResidualWarning must be a finite non-negative number.");
            }

            for (int index = 0; index < correspondences.Count; index++)
            {
                ConstrainedBestFitRigidCorrespondence pair = correspondences[index];
                if (pair == null
                    || pair.Source == null
                    || pair.Reference == null
                    || !pair.Source.IsFinite
                    || !pair.Reference.IsFinite)
                {
                    throw new ArgumentException(
                        "Constrained best-fit rigid alignment requires finite source/reference coordinates.");
                }
                for (int previous = 0; previous < index; previous++)
                {
                    ConstrainedBestFitRigidCorrespondence prior = correspondences[previous];
                    if (SamePoint(pair.Source, prior.Source)
                        || SamePoint(pair.Reference, prior.Reference))
                    {
                        throw new ArgumentException(
                            "Constrained best-fit rigid alignment requires unique source and reference coordinates.");
                    }
                }
            }
        }

        private static ThreeDPoint Centroid(
            IReadOnlyList<ConstrainedBestFitRigidCorrespondence> correspondences,
            bool source,
            CancellationToken cancellationToken)
        {
            double x = 0.0;
            double y = 0.0;
            double z = 0.0;
            for (int index = 0; index < correspondences.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                ThreeDPoint point = source ? correspondences[index].Source : correspondences[index].Reference;
                x += point.X;
                y += point.Y;
                z += point.Z;
            }
            x /= correspondences.Count;
            y /= correspondences.Count;
            z /= correspondences.Count;
            ThreeDPoint centroid = new ThreeDPoint(x, y, z);
            if (!centroid.IsFinite)
            {
                throw new ArgumentException("Correspondence centroid is non-finite.");
            }
            return centroid;
        }

        private static double[,] BuildCovariance(
            IReadOnlyList<ConstrainedBestFitRigidCorrespondence> correspondences,
            ThreeDPoint sourceCentroid,
            ThreeDPoint referenceCentroid,
            CancellationToken cancellationToken)
        {
            double[,] covariance = new double[3, 3];
            for (int index = 0; index < correspondences.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                ThreeDPoint source = correspondences[index].Source;
                ThreeDPoint reference = correspondences[index].Reference;
                double sx = source.X - sourceCentroid.X;
                double sy = source.Y - sourceCentroid.Y;
                double sz = source.Z - sourceCentroid.Z;
                double rx = reference.X - referenceCentroid.X;
                double ry = reference.Y - referenceCentroid.Y;
                double rz = reference.Z - referenceCentroid.Z;
                covariance[0, 0] += sx * rx;
                covariance[0, 1] += sx * ry;
                covariance[0, 2] += sx * rz;
                covariance[1, 0] += sy * rx;
                covariance[1, 1] += sy * ry;
                covariance[1, 2] += sy * rz;
                covariance[2, 0] += sz * rx;
                covariance[2, 1] += sz * ry;
                covariance[2, 2] += sz * rz;
            }
            for (int row = 0; row < 3; row++)
            {
                for (int column = 0; column < 3; column++)
                {
                    if (!IsFinite(covariance[row, column]))
                    {
                        throw new ArgumentException("Correspondence covariance is non-finite.");
                    }
                }
            }
            return covariance;
        }

        private static double[,] BuildHornMatrix(double[,] covariance)
        {
            double sxx = covariance[0, 0];
            double sxy = covariance[0, 1];
            double sxz = covariance[0, 2];
            double syx = covariance[1, 0];
            double syy = covariance[1, 1];
            double syz = covariance[1, 2];
            double szx = covariance[2, 0];
            double szy = covariance[2, 1];
            double szz = covariance[2, 2];
            double[,] matrix =
            {
                {
                    sxx + syy + szz,
                    syz - szy,
                    szx - sxz,
                    sxy - syx
                },
                {
                    syz - szy,
                    sxx - syy - szz,
                    sxy + syx,
                    szx + sxz
                },
                {
                    szx - sxz,
                    sxy + syx,
                    -sxx + syy - szz,
                    syz + szy
                },
                {
                    sxy - syx,
                    szx + sxz,
                    syz + szy,
                    -sxx - syy + szz
                }
            };
            for (int row = 0; row < 4; row++)
            {
                for (int column = 0; column < 4; column++)
                {
                    if (!IsFinite(matrix[row, column]))
                    {
                        throw new ArgumentException("Horn quaternion matrix is non-finite.");
                    }
                }
            }
            return matrix;
        }

        private static double[] LargestEigenvector(double[,] source, CancellationToken cancellationToken)
        {
            double[,] matrix = new double[4, 4];
            double[,] vectors = new double[4, 4];
            for (int row = 0; row < 4; row++)
            {
                vectors[row, row] = 1.0;
                for (int column = 0; column < 4; column++) matrix[row, column] = source[row, column];
            }

            for (int iteration = 0; iteration < MaximumJacobiIterations; iteration++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int first = 0;
                int second = 1;
                double maximumOffDiagonal = Math.Abs(matrix[first, second]);
                for (int row = 0; row < 4; row++)
                {
                    for (int column = row + 1; column < 4; column++)
                    {
                        double candidate = Math.Abs(matrix[row, column]);
                        if (candidate > maximumOffDiagonal)
                        {
                            maximumOffDiagonal = candidate;
                            first = row;
                            second = column;
                        }
                    }
                }
                double scale = 1.0;
                for (int diagonal = 0; diagonal < 4; diagonal++)
                {
                    scale = Math.Max(scale, Math.Abs(matrix[diagonal, diagonal]));
                }
                if (!IsFinite(maximumOffDiagonal) || maximumOffDiagonal <= JacobiRelativeTolerance * scale)
                {
                    break;
                }

                double app = matrix[first, first];
                double aqq = matrix[second, second];
                double apq = matrix[first, second];
                double tau = (aqq - app) / (2.0 * apq);
                double t = tau >= 0.0
                    ? 1.0 / (tau + Math.Sqrt(1.0 + (tau * tau)))
                    : -1.0 / (-tau + Math.Sqrt(1.0 + (tau * tau)));
                double cosine = 1.0 / Math.Sqrt(1.0 + (t * t));
                double sine = t * cosine;
                for (int index = 0; index < 4; index++)
                {
                    if (index == first || index == second) continue;
                    double aip = matrix[index, first];
                    double aiq = matrix[index, second];
                    matrix[index, first] = (cosine * aip) - (sine * aiq);
                    matrix[first, index] = matrix[index, first];
                    matrix[index, second] = (sine * aip) + (cosine * aiq);
                    matrix[second, index] = matrix[index, second];
                }
                matrix[first, first] =
                    (cosine * cosine * app)
                    - (2.0 * sine * cosine * apq)
                    + (sine * sine * aqq);
                matrix[second, second] =
                    (sine * sine * app)
                    + (2.0 * sine * cosine * apq)
                    + (cosine * cosine * aqq);
                matrix[first, second] = 0.0;
                matrix[second, first] = 0.0;
                for (int row = 0; row < 4; row++)
                {
                    double vip = vectors[row, first];
                    double viq = vectors[row, second];
                    vectors[row, first] = (cosine * vip) - (sine * viq);
                    vectors[row, second] = (sine * vip) + (cosine * viq);
                }
            }

            int selected = 0;
            for (int index = 1; index < 4; index++)
            {
                if (matrix[index, index] > matrix[selected, selected]) selected = index;
            }
            double[] result = new double[4];
            double norm = 0.0;
            for (int row = 0; row < 4; row++)
            {
                result[row] = vectors[row, selected];
                norm += result[row] * result[row];
            }
            norm = Math.Sqrt(norm);
            if (!IsFinite(norm) || norm <= 0.0)
            {
                throw new ArgumentException("Horn quaternion eigensolver returned a degenerate eigenvector.");
            }
            for (int row = 0; row < 4; row++) result[row] /= norm;
            int signIndex = 0;
            while (signIndex < result.Length && result[signIndex] == 0.0) signIndex++;
            if (signIndex < result.Length && result[signIndex] < 0.0)
            {
                for (int row = 0; row < 4; row++) result[row] = -result[row];
            }
            return result;
        }

        private static ConstrainedBestFitRigidAlignmentPose CreatePose(
            double[] quaternion,
            ThreeDPoint sourceCentroid,
            ThreeDPoint referenceCentroid)
        {
            double w = quaternion[0];
            double x = quaternion[1];
            double y = quaternion[2];
            double z = quaternion[3];
            double m11 = 1.0 - (2.0 * ((y * y) + (z * z)));
            double m12 = 2.0 * ((x * y) - (w * z));
            double m13 = 2.0 * ((x * z) + (w * y));
            double m21 = 2.0 * ((x * y) + (w * z));
            double m22 = 1.0 - (2.0 * ((x * x) + (z * z)));
            double m23 = 2.0 * ((y * z) - (w * x));
            double m31 = 2.0 * ((x * z) - (w * y));
            double m32 = 2.0 * ((y * z) + (w * x));
            double m33 = 1.0 - (2.0 * ((x * x) + (y * y)));
            return new ConstrainedBestFitRigidAlignmentPose(
                m11, m12, m13,
                m21, m22, m23,
                m31, m32, m33,
                referenceCentroid.X - ((m11 * sourceCentroid.X) + (m12 * sourceCentroid.Y) + (m13 * sourceCentroid.Z)),
                referenceCentroid.Y - ((m21 * sourceCentroid.X) + (m22 * sourceCentroid.Y) + (m23 * sourceCentroid.Z)),
                referenceCentroid.Z - ((m31 * sourceCentroid.X) + (m32 * sourceCentroid.Y) + (m33 * sourceCentroid.Z)));
        }

        private static double NormalizedLineSpread(
            IReadOnlyList<ConstrainedBestFitRigidCorrespondence> correspondences,
            bool source,
            CancellationToken cancellationToken)
        {
            int first = 0;
            int second = 1;
            double maximumDistance = Distance(
                source ? correspondences[first].Source : correspondences[first].Reference,
                source ? correspondences[second].Source : correspondences[second].Reference);
            for (int left = 0; left < correspondences.Count; left++)
            {
                for (int right = left + 1; right < correspondences.Count; right++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    double candidate = Distance(
                        source ? correspondences[left].Source : correspondences[left].Reference,
                        source ? correspondences[right].Source : correspondences[right].Reference);
                    if (!IsFinite(candidate)) return double.NaN;
                    if (candidate > maximumDistance)
                    {
                        maximumDistance = candidate;
                        first = left;
                        second = right;
                    }
                }
            }
            if (!IsFinite(maximumDistance) || maximumDistance <= 0.0) return 0.0;
            ThreeDPoint anchor = source ? correspondences[first].Source : correspondences[first].Reference;
            ThreeDPoint far = source ? correspondences[second].Source : correspondences[second].Reference;
            Vector3d edge = Subtract(far, anchor);
            double maximumCross = 0.0;
            for (int index = 0; index < correspondences.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                ThreeDPoint point = source ? correspondences[index].Source : correspondences[index].Reference;
                maximumCross = Math.Max(maximumCross, Length(Cross(edge, Subtract(point, anchor))));
            }
            return maximumCross / (maximumDistance * maximumDistance);
        }

        private static double Distance(ThreeDPoint first, ThreeDPoint second) =>
            Length(Subtract(second, first));

        private static Vector3d Subtract(ThreeDPoint left, ThreeDPoint right) =>
            new Vector3d(left.X - right.X, left.Y - right.Y, left.Z - right.Z);

        private static Vector3d Cross(Vector3d left, Vector3d right) =>
            new Vector3d(
                (left.Y * right.Z) - (left.Z * right.Y),
                (left.Z * right.X) - (left.X * right.Z),
                (left.X * right.Y) - (left.Y * right.X));

        private static double Length(Vector3d value) =>
            Math.Sqrt((value.X * value.X) + (value.Y * value.Y) + (value.Z * value.Z));

        private static double Length(ThreeDPoint value) =>
            Math.Sqrt((value.X * value.X) + (value.Y * value.Y) + (value.Z * value.Z));

        private static bool SamePoint(ThreeDPoint first, ThreeDPoint second) =>
            first.X == second.X && first.Y == second.Y && first.Z == second.Z;

        private static void EnsureFinite(ConstrainedBestFitRigidAlignmentPose pose)
        {
            double[] values =
            {
                pose.M11, pose.M12, pose.M13,
                pose.M21, pose.M22, pose.M23,
                pose.M31, pose.M32, pose.M33,
                pose.TranslationX, pose.TranslationY, pose.TranslationZ
            };
            for (int index = 0; index < values.Length; index++)
            {
                if (!IsFinite(values[index]))
                {
                    throw new ArgumentException("Constrained best-fit rigid pose contains a non-finite value.");
                }
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
        }
    }
}
