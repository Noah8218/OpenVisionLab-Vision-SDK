using System;
using System.Collections.Generic;

namespace OpenVisionLab.Vision3D.FeatureExtraction
{
    /// <summary>
    /// Source-neutral sample for fitting display Y as a function of display X/Z.
    /// RawHeight is carried independently for callers that retain sensor values.
    /// </summary>
    public sealed class HeightFieldPlaneFitSample
    {
        public HeightFieldPlaneFitSample(ThreeDPoint position, double rawHeight)
        {
            Position = position;
            RawHeight = rawHeight;
        }

        public ThreeDPoint Position { get; }

        public double RawHeight { get; }
    }

    public sealed class LeastSquaresHeightFieldPlaneFitResult
    {
        private readonly ThreeDPoint referenceOrigin;
        private readonly double localIntercept;

        public LeastSquaresHeightFieldPlaneFitResult(
            double slopeX,
            double slopeZ,
            double intercept,
            ThreeDPoint normal,
            double offset,
            int sampleCount,
            double rootMeanSquareDistance,
            ThreeDPoint target,
            ThreeDPoint targetProjection,
            double targetSignedDistance,
            double targetRawHeight,
            double targetRawReferenceHeight)
        {
            SlopeX = slopeX;
            SlopeZ = slopeZ;
            Intercept = intercept;
            Normal = normal;
            Offset = offset;
            SampleCount = sampleCount;
            RootMeanSquareDistance = rootMeanSquareDistance;
            Target = target;
            TargetProjection = targetProjection;
            TargetSignedDistance = targetSignedDistance;
            TargetRawHeight = targetRawHeight;
            TargetRawReferenceHeight = targetRawReferenceHeight;
        }

        internal LeastSquaresHeightFieldPlaneFitResult(
            double slopeX,
            double slopeZ,
            double intercept,
            ThreeDPoint normal,
            double offset,
            int sampleCount,
            double rootMeanSquareDistance,
            ThreeDPoint target,
            ThreeDPoint targetProjection,
            double targetSignedDistance,
            double targetRawHeight,
            double targetRawReferenceHeight,
            ThreeDPoint referenceOrigin,
            double localIntercept)
            : this(slopeX, slopeZ, intercept, normal, offset, sampleCount, rootMeanSquareDistance,
                target, targetProjection, targetSignedDistance, targetRawHeight, targetRawReferenceHeight)
        {
            this.referenceOrigin = referenceOrigin;
            this.localIntercept = localIntercept;
        }

        public double SlopeX { get; }
        public double SlopeZ { get; }
        public double Intercept { get; }
        public ThreeDPoint Normal { get; }
        public double Offset { get; }
        public int SampleCount { get; }
        public double RootMeanSquareDistance { get; }
        public ThreeDPoint Target { get; }
        public ThreeDPoint TargetProjection { get; }
        public double TargetSignedDistance { get; }
        public double TargetAbsoluteDistance => Math.Abs(TargetSignedDistance);
        public double TargetRawHeight { get; }
        public double TargetRawReferenceHeight { get; }
        public double TargetRawHeightDelta => TargetRawHeight - TargetRawReferenceHeight;

        public double EvaluateY(double x, double z)
        {
            return referenceOrigin == null
                ? (SlopeX * x) + (SlopeZ * z) + Intercept
                : referenceOrigin.Y + ((SlopeX * (x - referenceOrigin.X)) + (SlopeZ * (z - referenceOrigin.Z)) + localIntercept);
        }
        internal double SignedDistance(ThreeDPoint point)
        {
            return referenceOrigin == null
                ? LeastSquaresHeightFieldPlaneFitTool.SignedDistance(point, Normal, Offset)
                : LeastSquaresHeightFieldPlaneFitTool.SignedDistance(point, Normal, referenceOrigin, localIntercept);
        }
    }

    /// <summary>
    /// Fits Y = slopeX * X + slopeZ * Z + intercept by least squares.
    /// Uses centered sums and double-precision distances, with explicit finite/degenerate guards.
    /// Coordinate magnitude and conditioning still limit representable precision.
    /// </summary>
    public sealed class LeastSquaresHeightFieldPlaneFitTool
    {
        public LeastSquaresHeightFieldPlaneFitResult Execute(IReadOnlyList<HeightFieldPlaneFitSample> samples)
        {
            if (samples == null)
            {
                throw new ArgumentNullException(nameof(samples));
            }

            if (samples.Count < 3)
            {
                throw new ArgumentException("Plane fitting requires at least three samples.", nameof(samples));
            }

            HeightFieldPlaneFitSample origin = samples[0];
            if (!IsFinite(origin))
            {
                throw new ArgumentException("Plane fitting samples must contain finite coordinates and raw heights.", nameof(samples));
            }

            double meanX = 0.0;
            double meanY = 0.0;
            double meanZ = 0.0;
            double meanRaw = 0.0;
            foreach (HeightFieldPlaneFitSample sample in samples)
            {
                if (!IsFinite(sample))
                {
                    throw new ArgumentException("Plane fitting samples must contain finite coordinates and raw heights.", nameof(samples));
                }

                meanX += sample.Position.X - origin.Position.X;
                meanY += sample.Position.Y - origin.Position.Y;
                meanZ += sample.Position.Z - origin.Position.Z;
                meanRaw += sample.RawHeight - origin.RawHeight;
            }

            meanX /= samples.Count;
            meanY /= samples.Count;
            meanZ /= samples.Count;
            meanRaw /= samples.Count;
            if (!IsFinite(meanX) || !IsFinite(meanY) || !IsFinite(meanZ) || !IsFinite(meanRaw))
            {
                throw new InvalidOperationException("Plane fitting produced a non-finite sample mean.");
            }

            double sumXX = 0.0;
            double sumXZ = 0.0;
            double sumZZ = 0.0;
            double sumXY = 0.0;
            double sumZY = 0.0;
            double sumXRaw = 0.0;
            double sumZRaw = 0.0;
            foreach (HeightFieldPlaneFitSample sample in samples)
            {
                double x = (sample.Position.X - origin.Position.X) - meanX;
                double y = (sample.Position.Y - origin.Position.Y) - meanY;
                double z = (sample.Position.Z - origin.Position.Z) - meanZ;
                double raw = (sample.RawHeight - origin.RawHeight) - meanRaw;
                sumXX += x * x;
                sumXZ += x * z;
                sumZZ += z * z;
                sumXY += x * y;
                sumZY += z * y;
                sumXRaw += x * raw;
                sumZRaw += z * raw;
            }

            if (!IsFinite(sumXX) || !IsFinite(sumXZ) || !IsFinite(sumZZ)
                || !IsFinite(sumXY) || !IsFinite(sumZY)
                || !IsFinite(sumXRaw) || !IsFinite(sumZRaw))
            {
                throw new InvalidOperationException("Plane fitting overflowed its centered sums.");
            }

            double determinant = (sumXX * sumZZ) - (sumXZ * sumXZ);
            // Both quantities scale with the fourth power of the coordinate unit.
            double determinantScale = sumXX * sumZZ;
            if (!IsFinite(determinant) || !IsFinite(determinantScale))
            {
                throw new InvalidOperationException("Plane fitting produced a non-finite determinant.");
            }

            if (determinantScale <= 0.0 || Math.Abs(determinant) <= determinantScale * 1e-12)
            {
                throw new ArgumentException("Plane fitting samples must span two horizontal axes.", nameof(samples));
            }

            double slopeX = ((sumXY * sumZZ) - (sumZY * sumXZ)) / determinant;
            double slopeZ = ((sumZY * sumXX) - (sumXY * sumXZ)) / determinant;
            double localIntercept = meanY - (slopeX * meanX) - (slopeZ * meanZ);
            double intercept = origin.Position.Y - (slopeX * origin.Position.X) - (slopeZ * origin.Position.Z) + localIntercept;
            double rawSlopeX = ((sumXRaw * sumZZ) - (sumZRaw * sumXZ)) / determinant;
            double rawSlopeZ = ((sumZRaw * sumXX) - (sumXRaw * sumXZ)) / determinant;
            double rawIntercept = meanRaw - (rawSlopeX * meanX) - (rawSlopeZ * meanZ);
            if (!IsFinite(slopeX) || !IsFinite(slopeZ) || !IsFinite(intercept)
                || !IsFinite(localIntercept)
                || !IsFinite(rawSlopeX) || !IsFinite(rawSlopeZ) || !IsFinite(rawIntercept))
            {
                throw new InvalidOperationException("Plane fitting produced a non-finite coefficient.");
            }

            double normalLength = Math.Sqrt((slopeX * slopeX) + 1.0 + (slopeZ * slopeZ));
            if (!IsFinite(normalLength) || normalLength <= 0.0)
            {
                throw new InvalidOperationException("Plane fitting produced a non-finite normal.");
            }

            ThreeDPoint normal = new ThreeDPoint(
                -slopeX / normalLength,
                1.0 / normalLength,
                -slopeZ / normalLength);
            double offset = -intercept / normalLength;

            HeightFieldPlaneFitSample target = samples[0];
            double targetSignedDistance = SignedDistance(target.Position, normal, origin.Position, localIntercept);
            double squaredDistanceSum = targetSignedDistance * targetSignedDistance;
            if (!IsFinite(targetSignedDistance) || !IsFinite(squaredDistanceSum))
            {
                throw new InvalidOperationException("Plane fitting produced a non-finite distance.");
            }

            for (int index = 1; index < samples.Count; index++)
            {
                double signedDistance = SignedDistance(samples[index].Position, normal, origin.Position, localIntercept);
                double squaredDistance = signedDistance * signedDistance;
                if (!IsFinite(signedDistance) || !IsFinite(squaredDistance))
                {
                    throw new InvalidOperationException("Plane fitting produced a non-finite distance.");
                }

                squaredDistanceSum += squaredDistance;
                if (!IsFinite(squaredDistanceSum))
                {
                    throw new InvalidOperationException("Plane fitting overflowed its distance accumulation range.");
                }

                if (Math.Abs(signedDistance) > Math.Abs(targetSignedDistance))
                {
                    target = samples[index];
                    targetSignedDistance = signedDistance;
                }
            }

            ThreeDPoint projection = Project(target.Position, normal, targetSignedDistance);
            double rawReference = origin.RawHeight + ((rawSlopeX * (target.Position.X - origin.Position.X))
                + (rawSlopeZ * (target.Position.Z - origin.Position.Z)) + rawIntercept);
            return new LeastSquaresHeightFieldPlaneFitResult(
                slopeX,
                slopeZ,
                intercept,
                normal,
                offset,
                samples.Count,
                Math.Sqrt(squaredDistanceSum / samples.Count),
                target.Position,
                projection,
                targetSignedDistance,
                target.RawHeight,
                rawReference,
                origin.Position,
                localIntercept);
        }

        internal static double SignedDistance(ThreeDPoint point, ThreeDPoint normal, double offset)
        {
            double dot = (normal.X * point.X)
                + (normal.Y * point.Y)
                + (normal.Z * point.Z);
            return dot + offset;
        }

        internal static double SignedDistance(ThreeDPoint point, ThreeDPoint normal, ThreeDPoint referenceOrigin, double localIntercept)
        {
            return (normal.X * (point.X - referenceOrigin.X))
                + (normal.Y * ((point.Y - referenceOrigin.Y) - localIntercept))
                + (normal.Z * (point.Z - referenceOrigin.Z));
        }

        internal static ThreeDPoint Project(ThreeDPoint point, ThreeDPoint normal, double signedDistance)
        {
            return new ThreeDPoint(
                point.X - (normal.X * signedDistance),
                point.Y - (normal.Y * signedDistance),
                point.Z - (normal.Z * signedDistance));
        }

        private static bool IsFinite(HeightFieldPlaneFitSample sample)
        {
            return sample != null
                && sample.Position != null
                && sample.Position.IsFinite
                && IsFinite(sample.RawHeight);
        }

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }
    }
}
