using System;
using System.Collections.Generic;
using System.Threading;

namespace OpenVisionLab.Vision3D.FeatureExtraction
{
    /// <summary>
    /// Source-neutral height-plane parameters used to construct a deterministic
    /// software level frame. Coordinates use the caller's X/Y/Z convention;
    /// units and physical calibration remain caller responsibilities.
    /// </summary>
    public sealed class LevelFramePlane
    {
        public LevelFramePlane(
            double slopeX,
            double slopeZ,
            double intercept,
            double originX = 0.0,
            double originZ = 0.0)
        {
            SlopeX = slopeX;
            SlopeZ = slopeZ;
            Intercept = intercept;
            OriginX = originX;
            OriginZ = originZ;
        }

        public double SlopeX { get; }
        public double SlopeZ { get; }
        public double Intercept { get; }
        public double OriginX { get; }
        public double OriginZ { get; }
    }

    /// <summary>
    /// Deterministic source-to-level-frame geometry. The 12 values are the
    /// row-major 3x4 affine rows [U; V; H], followed by translation terms.
    /// </summary>
    public sealed class LevelFrameResult
    {
        private LevelFrameResult(
            bool success,
            string message,
            ThreeDPoint origin,
            ThreeDPoint uAxis,
            ThreeDPoint vAxis,
            ThreeDPoint hAxis,
            IReadOnlyList<double> sourceToFrameValues,
            double linearDeterminant)
        {
            Success = success;
            Message = message ?? string.Empty;
            Origin = origin;
            UAxis = uAxis;
            VAxis = vAxis;
            HAxis = hAxis;
            SourceToFrameValues = sourceToFrameValues ?? Array.Empty<double>();
            LinearDeterminant = linearDeterminant;
        }

        public bool Success { get; }
        public string Message { get; }
        public ThreeDPoint Origin { get; }
        public ThreeDPoint UAxis { get; }
        public ThreeDPoint VAxis { get; }
        public ThreeDPoint HAxis { get; }
        public IReadOnlyList<double> SourceToFrameValues { get; }
        public double LinearDeterminant { get; }

        internal static LevelFrameResult Completed(
            ThreeDPoint origin,
            ThreeDPoint uAxis,
            ThreeDPoint vAxis,
            ThreeDPoint hAxis,
            IReadOnlyList<double> sourceToFrameValues,
            double linearDeterminant)
        {
            return new LevelFrameResult(
                true,
                "Completed deterministic level-frame construction.",
                origin,
                uAxis,
                vAxis,
                hAxis,
                sourceToFrameValues,
                linearDeterminant);
        }

        internal static LevelFrameResult Failed(string message)
        {
            return new LevelFrameResult(
                false,
                message,
                new ThreeDPoint(double.NaN, double.NaN, double.NaN),
                new ThreeDPoint(double.NaN, double.NaN, double.NaN),
                new ThreeDPoint(double.NaN, double.NaN, double.NaN),
                new ThreeDPoint(double.NaN, double.NaN, double.NaN),
                Array.Empty<double>(),
                double.NaN);
        }
    }

    /// <summary>
    /// Converts a fitted height plane into a deterministic right-handed level
    /// frame. H is the positive-Y plane normal, U is the projection of +X on
    /// the plane (with +Z fallback), and V = H x U. Thus U x V = H.
    /// </summary>
    public sealed class LevelFrameTool
    {
        public const string Semantics = "height-plane-to-right-handed-level-frame-v1";

        public LevelFrameResult Execute(
            LevelFramePlane plane,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            try
            {
                if (plane == null)
                {
                    return LevelFrameResult.Failed(
                        "Level-frame plane parameters are required.");
                }

                cancellationToken.ThrowIfCancellationRequested();
                if (!IsFinite(plane.SlopeX)
                    || !IsFinite(plane.SlopeZ)
                    || !IsFinite(plane.Intercept)
                    || !IsFinite(plane.OriginX)
                    || !IsFinite(plane.OriginZ))
                {
                    return LevelFrameResult.Failed(
                        "Level-frame plane parameters must be finite.");
                }

                Vector h = Normalize(new Vector(-plane.SlopeX, 1.0, -plane.SlopeZ));
                Vector u = ProjectOntoPlane(new Vector(1.0, 0.0, 0.0), h);
                if (!TryNormalize(u, out u))
                {
                    u = ProjectOntoPlane(new Vector(0.0, 0.0, 1.0), h);
                    if (!TryNormalize(u, out u))
                    {
                        return LevelFrameResult.Failed(
                            "Level-frame plane normal is degenerate.");
                    }
                }

                Vector v = Cross(h, u);
                if (!TryNormalize(v, out v))
                {
                    return LevelFrameResult.Failed(
                        "Level-frame axis construction is degenerate.");
                }

                cancellationToken.ThrowIfCancellationRequested();
                double originY = (plane.SlopeX * plane.OriginX)
                    + (plane.SlopeZ * plane.OriginZ)
                    + plane.Intercept;
                Vector origin = new Vector(plane.OriginX, originY, plane.OriginZ);
                double[] values =
                {
                    u.X, u.Y, u.Z, -Dot(u, origin),
                    v.X, v.Y, v.Z, -Dot(v, origin),
                    h.X, h.Y, h.Z, -Dot(h, origin)
                };
                double determinant = Dot(Cross(u, v), h);
                if (!IsFinite(determinant) || Math.Abs(determinant - 1.0) > 1e-10)
                {
                    return LevelFrameResult.Failed(
                        "Level-frame axes did not produce a right-handed orthonormal basis.");
                }

                return LevelFrameResult.Completed(
                    new ThreeDPoint(origin.X, origin.Y, origin.Z),
                    new ThreeDPoint(u.X, u.Y, u.Z),
                    new ThreeDPoint(v.X, v.Y, v.Z),
                    new ThreeDPoint(h.X, h.Y, h.Z),
                    values,
                    determinant);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (ArgumentException exception)
            {
                return LevelFrameResult.Failed(exception.Message);
            }
            catch (OverflowException exception)
            {
                return LevelFrameResult.Failed(exception.Message);
            }
        }

        private static Vector ProjectOntoPlane(Vector value, Vector normal)
        {
            double projection = Dot(value, normal);
            return new Vector(
                value.X - (projection * normal.X),
                value.Y - (projection * normal.Y),
                value.Z - (projection * normal.Z));
        }

        private static Vector Normalize(Vector value)
        {
            double length = Math.Sqrt(Dot(value, value));
            if (!IsFinite(length) || length <= 1e-15)
            {
                throw new ArgumentException("Level-frame normal must have a positive finite length.");
            }

            return new Vector(value.X / length, value.Y / length, value.Z / length);
        }

        private static bool TryNormalize(Vector value, out Vector normalized)
        {
            double length = Math.Sqrt(Dot(value, value));
            if (!IsFinite(length) || length <= 1e-15)
            {
                normalized = default(Vector);
                return false;
            }

            normalized = new Vector(value.X / length, value.Y / length, value.Z / length);
            return true;
        }

        private static Vector Cross(Vector left, Vector right) =>
            new Vector(
                (left.Y * right.Z) - (left.Z * right.Y),
                (left.Z * right.X) - (left.X * right.Z),
                (left.X * right.Y) - (left.Y * right.X));

        private static double Dot(Vector left, Vector right) =>
            (left.X * right.X) + (left.Y * right.Y) + (left.Z * right.Z);

        private static bool IsFinite(double value) =>
            !double.IsNaN(value) && !double.IsInfinity(value);

        private struct Vector
        {
            public Vector(double x, double y, double z)
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
