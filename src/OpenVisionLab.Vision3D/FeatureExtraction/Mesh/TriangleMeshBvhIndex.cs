using System;
using System.Collections.Generic;

namespace OpenVisionLab.Vision3D.FeatureExtraction
{
    internal sealed class TriangleMeshBvhIndex
    {
        private const int LeafTriangleCount = 8;

        private readonly TriangleEntry[] triangles;
        private readonly Node root;

        internal TriangleMeshBvhIndex(IReadOnlyList<MeshTriangle> source)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            if (source.Count == 0)
            {
                throw new ArgumentException(
                    "A distance index requires at least one triangle.",
                    nameof(source));
            }

            triangles = new TriangleEntry[source.Count];
            for (int index = 0; index < source.Count; index++)
            {
                triangles[index] = CreateEntry(source[index]);
            }

            root = BuildNode(0, triangles.Length);
        }

        internal int TriangleCount => triangles.Length;

        internal SearchResult FindNearest(Vector3 point)
        {
            SearchResult best = new SearchResult(
                double.PositiveInfinity,
                long.MaxValue,
                null,
                default(TriangleClosestPointKernel.ClosestPointResult));
            Search(root, point, ref best);
            return best;
        }

        internal void VisitBoundsCandidates(
            Vector3 point,
            double maximumDistanceSquared,
            Action<TriangleEntry> visit)
        {
            VisitBoundsCandidates(
                root,
                point,
                maximumDistanceSquared,
                visit);
        }

        private Node BuildNode(int start, int count)
        {
            Bounds bounds = CalculateBounds(start, count);
            if (count <= LeafTriangleCount)
            {
                return new Node(
                    bounds.Minimum,
                    bounds.Maximum,
                    start,
                    count,
                    null,
                    null);
            }

            Bounds centroidBounds = CalculateCentroidBounds(start, count);
            Vector3 span = centroidBounds.Maximum - centroidBounds.Minimum;
            int axis = span.X >= span.Y && span.X >= span.Z
                ? 0
                : span.Y >= span.Z
                    ? 1
                    : 2;
            Array.Sort(
                triangles,
                start,
                count,
                CentroidComparer.ForAxis(axis));

            int leftCount = count / 2;
            Node left = BuildNode(start, leftCount);
            Node right = BuildNode(
                start + leftCount,
                count - leftCount);
            return new Node(
                bounds.Minimum,
                bounds.Maximum,
                start,
                count,
                left,
                right);
        }

        private void Search(
            Node node,
            Vector3 point,
            ref SearchResult best)
        {
            if (DistanceSquaredToBounds(
                    point,
                    node.Minimum,
                    node.Maximum) > best.DistanceSquared)
            {
                return;
            }

            if (node.Left == null || node.Right == null)
            {
                int end = node.Start + node.Count;
                for (int index = node.Start; index < end; index++)
                {
                    TriangleEntry triangle = triangles[index];
                    TriangleClosestPointKernel.ClosestPointResult closest =
                        TriangleClosestPointKernel.Find(
                            point,
                            triangle.A,
                            triangle.B,
                            triangle.C);
                    double distanceSquared = Vector3.DistanceSquared(
                        point,
                        closest.Point);
                    if (distanceSquared < best.DistanceSquared
                        || distanceSquared == best.DistanceSquared
                        && triangle.Source.SourceTriangleIndex
                            < best.SourceTriangleIndex)
                    {
                        best = new SearchResult(
                            distanceSquared,
                            triangle.Source.SourceTriangleIndex,
                            triangle,
                            closest);
                    }
                }

                return;
            }

            double leftDistance = DistanceSquaredToBounds(
                point,
                node.Left.Minimum,
                node.Left.Maximum);
            double rightDistance = DistanceSquaredToBounds(
                point,
                node.Right.Minimum,
                node.Right.Maximum);
            if (leftDistance <= rightDistance)
            {
                Search(node.Left, point, ref best);
                Search(node.Right, point, ref best);
            }
            else
            {
                Search(node.Right, point, ref best);
                Search(node.Left, point, ref best);
            }
        }

        private void VisitBoundsCandidates(
            Node node,
            Vector3 point,
            double maximumDistanceSquared,
            Action<TriangleEntry> visit)
        {
            if (DistanceSquaredToBounds(
                    point,
                    node.Minimum,
                    node.Maximum) > maximumDistanceSquared)
            {
                return;
            }

            if (node.Left == null || node.Right == null)
            {
                int end = node.Start + node.Count;
                for (int index = node.Start; index < end; index++)
                {
                    visit(triangles[index]);
                }

                return;
            }

            VisitBoundsCandidates(
                node.Left,
                point,
                maximumDistanceSquared,
                visit);
            VisitBoundsCandidates(
                node.Right,
                point,
                maximumDistanceSquared,
                visit);
        }

        private Bounds CalculateBounds(int start, int count)
        {
            Vector3 minimum = new Vector3(double.PositiveInfinity);
            Vector3 maximum = new Vector3(double.NegativeInfinity);
            int end = start + count;
            for (int index = start; index < end; index++)
            {
                minimum = Vector3.Min(minimum, triangles[index].Minimum);
                maximum = Vector3.Max(maximum, triangles[index].Maximum);
            }

            return new Bounds(minimum, maximum);
        }

        private Bounds CalculateCentroidBounds(int start, int count)
        {
            Vector3 minimum = new Vector3(double.PositiveInfinity);
            Vector3 maximum = new Vector3(double.NegativeInfinity);
            int end = start + count;
            for (int index = start; index < end; index++)
            {
                minimum = Vector3.Min(minimum, triangles[index].Centroid);
                maximum = Vector3.Max(maximum, triangles[index].Centroid);
            }

            return new Bounds(minimum, maximum);
        }

        private static TriangleEntry CreateEntry(MeshTriangle triangle)
        {
            if (triangle == null)
            {
                throw new ArgumentNullException(nameof(triangle));
            }

            if (!IsFinite(triangle.A)
                || !IsFinite(triangle.B)
                || !IsFinite(triangle.C))
            {
                throw new ArgumentException(
                    "Triangle "
                    + triangle.SourceTriangleIndex
                    + " contains a non-finite coordinate.",
                    nameof(triangle));
            }

            Vector3 a = ToVector(
                triangle.A,
                nameof(triangle));
            Vector3 b = ToVector(
                triangle.B,
                nameof(triangle));
            Vector3 c = ToVector(
                triangle.C,
                nameof(triangle));
            Vector3 cross = Vector3.Cross(b - a, c - a);
            double crossLengthSquared = Vector3.Dot(cross, cross);
            if (!IsFinite(crossLengthSquared)
                || crossLengthSquared <= 0.0)
            {
                throw new ArgumentException(
                    "Triangle "
                    + triangle.SourceTriangleIndex
                    + " is degenerate.",
                    nameof(triangle));
            }

            Vector3 normal = cross / Math.Sqrt(crossLengthSquared);
            Vector3 minimum = Vector3.Min(a, Vector3.Min(b, c));
            Vector3 maximum = Vector3.Max(a, Vector3.Max(b, c));
            Vector3 centroid = new Vector3(
                (a.X + b.X + c.X) / 3.0,
                (a.Y + b.Y + c.Y) / 3.0,
                (a.Z + b.Z + c.Z) / 3.0);
            return new TriangleEntry(
                triangle,
                a,
                b,
                c,
                minimum,
                maximum,
                centroid,
                normal);
        }

        private static double DistanceSquaredToBounds(
            Vector3 point,
            Vector3 minimum,
            Vector3 maximum)
        {
            double x = AxisDistance(point.X, minimum.X, maximum.X);
            double y = AxisDistance(point.Y, minimum.Y, maximum.Y);
            double z = AxisDistance(point.Z, minimum.Z, maximum.Z);
            return x * x + y * y + z * z;
        }

        private static double AxisDistance(
            double value,
            double minimum,
            double maximum)
        {
            return value < minimum
                ? minimum - (double)value
                : value > maximum
                    ? value - (double)maximum
                    : 0.0;
        }

        private static Vector3 ToVector(ThreeDPoint point, string name)
        {
            if (!IsFinite(point))
            {
                throw new ArgumentException(
                    "A triangle contains a non-finite coordinate.",
                    name);
            }

            Vector3 value = new Vector3(
                point.X,
                point.Y,
                point.Z);
            if (!Vector3.IsFinite(value))
            {
                throw new ArgumentException(
                    "A triangle contains a non-finite coordinate.",
                    name);
            }

            return value;
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

        internal sealed class TriangleEntry
        {
            internal TriangleEntry(
                MeshTriangle source,
                Vector3 a,
                Vector3 b,
                Vector3 c,
                Vector3 minimum,
                Vector3 maximum,
                Vector3 centroid,
                Vector3 normal)
            {
                Source = source;
                A = a;
                B = b;
                C = c;
                Minimum = minimum;
                Maximum = maximum;
                Centroid = centroid;
                Normal = normal;
            }

            internal MeshTriangle Source { get; }
            internal Vector3 A { get; }
            internal Vector3 B { get; }
            internal Vector3 C { get; }
            internal Vector3 Minimum { get; }
            internal Vector3 Maximum { get; }
            internal Vector3 Centroid { get; }
            internal Vector3 Normal { get; }
        }

        internal struct SearchResult
        {
            internal SearchResult(
                double distanceSquared,
                long sourceTriangleIndex,
                TriangleEntry triangle,
                TriangleClosestPointKernel.ClosestPointResult closest)
            {
                DistanceSquared = distanceSquared;
                SourceTriangleIndex = sourceTriangleIndex;
                Triangle = triangle;
                Closest = closest;
            }

            internal double DistanceSquared { get; }
            internal long SourceTriangleIndex { get; }
            internal TriangleEntry Triangle { get; }
            internal TriangleClosestPointKernel.ClosestPointResult Closest { get; }
        }

        private sealed class Node
        {
            internal Node(
                Vector3 minimum,
                Vector3 maximum,
                int start,
                int count,
                Node left,
                Node right)
            {
                Minimum = minimum;
                Maximum = maximum;
                Start = start;
                Count = count;
                Left = left;
                Right = right;
            }

            internal Vector3 Minimum { get; }
            internal Vector3 Maximum { get; }
            internal int Start { get; }
            internal int Count { get; }
            internal Node Left { get; }
            internal Node Right { get; }
        }

        private sealed class CentroidComparer : IComparer<TriangleEntry>
        {
            private static readonly CentroidComparer X =
                new CentroidComparer(0);
            private static readonly CentroidComparer Y =
                new CentroidComparer(1);
            private static readonly CentroidComparer Z =
                new CentroidComparer(2);

            private readonly int axis;

            private CentroidComparer(int axis)
            {
                this.axis = axis;
            }

            internal static CentroidComparer ForAxis(int axis)
            {
                return axis == 0 ? X : axis == 1 ? Y : Z;
            }

            public int Compare(TriangleEntry first, TriangleEntry second)
            {
                int comparison = GetAxis(first.Centroid)
                    .CompareTo(GetAxis(second.Centroid));
                return comparison != 0
                    ? comparison
                    : first.Source.SourceTriangleIndex.CompareTo(
                        second.Source.SourceTriangleIndex);
            }

            private double GetAxis(Vector3 value)
            {
                return axis == 0
                    ? value.X
                    : axis == 1
                        ? value.Y
                        : value.Z;
            }
        }

        private struct Bounds
        {
            internal Bounds(Vector3 minimum, Vector3 maximum)
            {
                Minimum = minimum;
                Maximum = maximum;
            }

            internal Vector3 Minimum { get; }
            internal Vector3 Maximum { get; }
        }
    }
}
