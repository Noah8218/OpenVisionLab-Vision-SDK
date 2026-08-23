using System;
using System.Collections.Generic;

namespace OpenVisionLab.Vision3D.FeatureExtraction
{
    /// <summary>
    /// Source-neutral triangle with a stable caller-owned source index.
    /// </summary>
    public sealed class MeshTriangle
    {
        public MeshTriangle(
            long sourceTriangleIndex,
            ThreeDPoint a,
            ThreeDPoint b,
            ThreeDPoint c)
        {
            SourceTriangleIndex = sourceTriangleIndex;
            A = a;
            B = b;
            C = c;
        }

        public long SourceTriangleIndex { get; }

        public ThreeDPoint A { get; }

        public ThreeDPoint B { get; }

        public ThreeDPoint C { get; }
    }

    public enum MeshClosestFeature
    {
        FaceInterior,
        Edge,
        Vertex
    }

    /// <summary>
    /// Deterministic closest-point evidence. Boundary signs remain unresolved
    /// until the caller explicitly requests robust sign recovery.
    /// </summary>
    public sealed class PointMeshDistance
    {
        public PointMeshDistance(
            long sourceTriangleIndex,
            ThreeDPoint closestPoint,
            ThreeDPoint triangleNormal,
            MeshClosestFeature closestFeature,
            double unsignedDistance,
            double? signedDistance,
            bool signResolved)
        {
            SourceTriangleIndex = sourceTriangleIndex;
            ClosestPoint = closestPoint;
            TriangleNormal = triangleNormal;
            ClosestFeature = closestFeature;
            UnsignedDistance = unsignedDistance;
            SignedDistance = signedDistance;
            SignResolved = signResolved;
        }

        public long SourceTriangleIndex { get; }

        public ThreeDPoint ClosestPoint { get; }

        public ThreeDPoint TriangleNormal { get; }

        public MeshClosestFeature ClosestFeature { get; }

        public double UnsignedDistance { get; }

        public double? SignedDistance { get; }

        public bool SignResolved { get; }
    }

    /// <summary>
    /// Builds a deterministic BVH once, then executes closest-point and robust
    /// sign queries without owning source identity, units, frames, or product
    /// acceptance policy.
    /// </summary>
    public sealed class TriangleMeshDistanceTool
    {
        public const double RobustSignDistanceEpsilon =
            1.1920928955078125e-7;

        private readonly TriangleMeshBvhIndex index;

        public TriangleMeshDistanceTool(IReadOnlyList<MeshTriangle> source)
        {
            index = new TriangleMeshBvhIndex(source);
        }

        public int TriangleCount => index.TriangleCount;

        public PointMeshDistance Execute(ThreeDPoint point)
        {
            Vector3 query = ToVector(
                point,
                nameof(point));
            TriangleMeshBvhIndex.SearchResult best =
                index.FindNearest(query);
            double unsignedDistance = Math.Sqrt(
                Math.Max(0.0, best.DistanceSquared));
            TriangleMeshSignResolver.SignResolution sign =
                TriangleMeshSignResolver.ResolveDirect(
                    query,
                    best.Triangle,
                    best.Closest,
                    unsignedDistance);

            return CreateDistance(
                best.Triangle,
                best.Closest,
                unsignedDistance,
                sign.SignedDistance,
                sign.SignResolved);
        }

        public PointMeshDistance ExecuteRobustSign(
            ThreeDPoint point,
            double nearestUnsignedDistance)
        {
            Vector3 query = ToVector(
                point,
                nameof(point));
            if (!IsFinite(nearestUnsignedDistance)
                || nearestUnsignedDistance < 0.0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(nearestUnsignedDistance),
                    "The nearest unsigned distance must be finite and non-negative.");
            }

            TriangleMeshSignResolver.RobustResolution resolved =
                TriangleMeshSignResolver.ResolveRobust(
                    index,
                    query,
                    nearestUnsignedDistance,
                    RobustSignDistanceEpsilon);
            return CreateDistance(
                resolved.Triangle,
                resolved.Closest,
                resolved.Distance,
                resolved.SignedDistance,
                true);
        }

        private static PointMeshDistance CreateDistance(
            TriangleMeshBvhIndex.TriangleEntry triangle,
            TriangleClosestPointKernel.ClosestPointResult closest,
            double unsignedDistance,
            double? signedDistance,
            bool signResolved)
        {
            return new PointMeshDistance(
                triangle.Source.SourceTriangleIndex,
                ToPoint(closest.Point),
                ToPoint(triangle.Normal),
                closest.Feature,
                unsignedDistance,
                signedDistance,
                signResolved);
        }

        private static Vector3 ToVector(ThreeDPoint point, string name)
        {
            if (!IsFinite(point))
            {
                throw new ArgumentException(
                    name == "point"
                        ? "The query point must contain finite coordinates."
                        : "A triangle contains a non-finite coordinate.",
                    name);
            }

            Vector3 value = new Vector3(
                point.X,
                point.Y,
                point.Z);
            if (!Vector3.IsFinite(value))
            {
                throw new ArgumentException(
                    name == "point"
                        ? "The query point must contain finite coordinates."
                        : "A triangle contains a non-finite coordinate.",
                    name);
            }

            return value;
        }

        private static ThreeDPoint ToPoint(Vector3 value)
        {
            return new ThreeDPoint(value.X, value.Y, value.Z);
        }

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }

        private static bool IsFinite(ThreeDPoint value)
        {
            return value != null
                && value.IsFinite
                && Math.Abs(value.X) <= float.MaxValue
                && Math.Abs(value.Y) <= float.MaxValue
                && Math.Abs(value.Z) <= float.MaxValue;
        }
    }
}
