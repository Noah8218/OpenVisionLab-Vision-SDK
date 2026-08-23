using System;

namespace OpenVisionLab.Vision3D.FeatureExtraction
{
    internal static class TriangleClosestPointKernel
    {
        internal static ClosestPointResult Find(
            Vector3 point,
            Vector3 a,
            Vector3 b,
            Vector3 c)
        {
            Vector3 ab = b - a;
            Vector3 ac = c - a;
            Vector3 ap = point - a;
            double d1 = Vector3.Dot(ab, ap);
            double d2 = Vector3.Dot(ac, ap);
            if (d1 <= 0.0 && d2 <= 0.0)
            {
                return new ClosestPointResult(
                    a,
                    MeshClosestFeature.Vertex);
            }

            Vector3 bp = point - b;
            double d3 = Vector3.Dot(ab, bp);
            double d4 = Vector3.Dot(ac, bp);
            if (d3 >= 0.0 && d4 <= d3)
            {
                return new ClosestPointResult(
                    b,
                    MeshClosestFeature.Vertex);
            }

            double vc = d1 * d4 - d3 * d2;
            if (vc <= 0.0 && d1 >= 0.0 && d3 <= 0.0)
            {
                double scale = d1 / (d1 - d3);
                return new ClosestPointResult(
                    a + scale * ab,
                    MeshClosestFeature.Edge);
            }

            Vector3 cp = point - c;
            double d5 = Vector3.Dot(ab, cp);
            double d6 = Vector3.Dot(ac, cp);
            if (d6 >= 0.0 && d5 <= d6)
            {
                return new ClosestPointResult(
                    c,
                    MeshClosestFeature.Vertex);
            }

            double vb = d5 * d2 - d1 * d6;
            if (vb <= 0.0 && d2 >= 0.0 && d6 <= 0.0)
            {
                double scale = d2 / (d2 - d6);
                return new ClosestPointResult(
                    a + scale * ac,
                    MeshClosestFeature.Edge);
            }

            double va = d3 * d6 - d5 * d4;
            if (va <= 0.0 && d4 - d3 >= 0.0 && d5 - d6 >= 0.0)
            {
                double scale = (d4 - d3)
                    / ((d4 - d3) + (d5 - d6));
                return new ClosestPointResult(
                    b + scale * (c - b),
                    MeshClosestFeature.Edge);
            }

            double denominator = 1.0 / (va + vb + vc);
            double v = vb * denominator;
            double w = vc * denominator;
            return new ClosestPointResult(
                a + v * ab + w * ac,
                MeshClosestFeature.FaceInterior);
        }

        internal struct ClosestPointResult
        {
            internal ClosestPointResult(
                Vector3 point,
                MeshClosestFeature feature)
            {
                Point = point;
                Feature = feature;
            }

            internal Vector3 Point { get; }
            internal MeshClosestFeature Feature { get; }
        }
    }

    internal struct Vector3
    {
        internal Vector3(double value)
            : this(value, value, value)
        {
        }

        internal Vector3(double x, double y, double z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        internal double X { get; }
        internal double Y { get; }
        internal double Z { get; }

        internal static Vector3 Min(Vector3 first, Vector3 second)
        {
            return new Vector3(
                Math.Min(first.X, second.X),
                Math.Min(first.Y, second.Y),
                Math.Min(first.Z, second.Z));
        }

        internal static Vector3 Max(Vector3 first, Vector3 second)
        {
            return new Vector3(
                Math.Max(first.X, second.X),
                Math.Max(first.Y, second.Y),
                Math.Max(first.Z, second.Z));
        }

        internal static Vector3 Cross(Vector3 first, Vector3 second)
        {
            return new Vector3(
                first.Y * second.Z - first.Z * second.Y,
                first.Z * second.X - first.X * second.Z,
                first.X * second.Y - first.Y * second.X);
        }

        internal static double Dot(Vector3 first, Vector3 second)
        {
            return first.X * second.X
                + first.Y * second.Y
                + first.Z * second.Z;
        }

        internal static double DistanceSquared(
            Vector3 first,
            Vector3 second)
        {
            double x = (double)first.X - second.X;
            double y = (double)first.Y - second.Y;
            double z = (double)first.Z - second.Z;
            return x * x + y * y + z * z;
        }

        internal static bool IsFinite(Vector3 value)
        {
            return !double.IsNaN(value.X) && !double.IsInfinity(value.X)
                && !double.IsNaN(value.Y) && !double.IsInfinity(value.Y)
                && !double.IsNaN(value.Z) && !double.IsInfinity(value.Z);
        }

        public static Vector3 operator +(Vector3 first, Vector3 second)
        {
            return new Vector3(
                first.X + second.X,
                first.Y + second.Y,
                first.Z + second.Z);
        }

        public static Vector3 operator -(Vector3 first, Vector3 second)
        {
            return new Vector3(
                first.X - second.X,
                first.Y - second.Y,
                first.Z - second.Z);
        }

        public static Vector3 operator *(double scale, Vector3 value)
        {
            return new Vector3(
                scale * value.X,
                scale * value.Y,
                scale * value.Z);
        }

        public static Vector3 operator /(Vector3 value, double scale)
        {
            return new Vector3(
                value.X / scale,
                value.Y / scale,
                value.Z / scale);
        }
    }
}
