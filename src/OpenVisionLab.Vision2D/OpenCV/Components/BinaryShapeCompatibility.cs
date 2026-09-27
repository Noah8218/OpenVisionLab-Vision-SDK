using System;
using System.Collections.Generic;
using System.Linq;
using OpenCvSharp;

namespace OpenVisionLab.Vision2D.Components
{
    internal sealed class BinaryComponent
    {
        internal BinaryComponent(int nativeIndex, int area, Point2d center, Rect bounds, double angle)
        {
            NativeIndex = nativeIndex;
            Area = area;
            Center = center;
            Bounds = bounds;
            Angle = angle;
        }

        internal int NativeIndex { get; }

        internal int Area { get; }

        internal Point2d Center { get; }

        internal Rect Bounds { get; }

        internal double Angle { get; }
    }

    internal static class BinaryShapeCompatibility
    {
        private static readonly int[] BoundaryMoveX = { 0, 1, 1, 1, 0, -1, -1, -1 };
        private static readonly int[] BoundaryMoveY = { -1, -1, 0, 1, 1, 1, 0, -1 };

        internal static IReadOnlyList<BinaryComponent> LabelComponents(Mat source)
        {
            ValidateBinarySource(source);

            using (Mat labels = new Mat())
            using (Mat stats = new Mat())
            using (Mat centroids = new Mat())
            {
                int labelCount = Cv2.ConnectedComponentsWithStats(
                    source,
                    labels,
                    stats,
                    centroids,
                    PixelConnectivity.Connectivity8,
                    MatType.CV_32S);
                ComponentMoments[] moments = new ComponentMoments[labelCount];
                List<int> labelsInSourceOrder = new List<int>(Math.Max(0, labelCount - 1));

                for (int y = 0; y < source.Rows; y++)
                {
                    for (int x = 0; x < source.Cols; x++)
                    {
                        int label = labels.At<int>(y, x);
                        if (label == 0)
                        {
                            continue;
                        }

                        ComponentMoments component = moments[label];
                        if (component == null)
                        {
                            component = new ComponentMoments(labelsInSourceOrder.Count + 1);
                            moments[label] = component;
                            labelsInSourceOrder.Add(label);
                        }
                        component.Add(x, y);
                    }
                }

                List<BinaryComponent> components = new List<BinaryComponent>(labelsInSourceOrder.Count);
                foreach (int label in labelsInSourceOrder)
                {
                    ComponentMoments component = moments[label];
                    int area = stats.At<int>(label, (int)ConnectedComponentsTypes.Area);
                    Rect bounds = new Rect(
                        stats.At<int>(label, (int)ConnectedComponentsTypes.Left),
                        stats.At<int>(label, (int)ConnectedComponentsTypes.Top),
                        stats.At<int>(label, (int)ConnectedComponentsTypes.Width),
                        stats.At<int>(label, (int)ConnectedComponentsTypes.Height));
                    components.Add(component.Create(area, bounds));
                }
                return components;
            }
        }

        internal static Point[][] FindContours(
            Mat source,
            RetrievalModes retrievalMode,
            ContourApproximationModes approximationMode)
        {
            ValidateBinarySource(source);

            Point[][] contours;
            HierarchyIndex[] hierarchy;
            using (Mat contourInput = source.Clone())
            {
                Cv2.FindContours(
                    contourInput,
                    out contours,
                    out hierarchy,
                    RetrievalModes.CComp,
                    ContourApproximationModes.ApproxNone);
            }

            List<ContourEntry> entries = new List<ContourEntry>();
            for (int index = 0; index < contours.Length; index++)
            {
                if (hierarchy[index].Parent >= 0)
                {
                    continue;
                }

                Point[] outer = ConvertBoundary(
                    TraceOuterBoundary(source, FindFirstPoint(contours[index])),
                    approximationMode);
                if (outer.Length == 0)
                {
                    continue;
                }

                List<Point[]> holes = new List<Point[]>();
                if (retrievalMode != RetrievalModes.External)
                {
                    int child = hierarchy[index].Child;
                    while (child >= 0)
                    {
                        Point[] hole = NormalizeHole(contours[child], approximationMode);
                        if (hole.Length > 0)
                        {
                            holes.Add(hole);
                        }
                        child = hierarchy[child].Next;
                    }
                    holes.Sort(CompareContours);
                }

                entries.Add(new ContourEntry(outer, holes));
            }

            entries.Sort((left, right) => CompareContours(left.Outer, right.Outer));
            List<Point[]> result = new List<Point[]>();
            foreach (ContourEntry entry in entries)
            {
                result.Add(entry.Outer);
                result.AddRange(entry.Holes);
            }
            return result.ToArray();
        }

        private static void ValidateBinarySource(Mat source)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }
            if (source.Empty() || source.Type() != MatType.CV_8UC1)
            {
                throw new ArgumentException("Binary shape input must be a non-empty CV_8UC1 image.", nameof(source));
            }
        }

        private static Point FindFirstPoint(IEnumerable<Point> points)
        {
            return points.OrderBy(point => point.Y).ThenBy(point => point.X).First();
        }

        private static Point[] TraceOuterBoundary(Mat source, Point start)
        {
            List<Point> points = new List<Point> { start };
            Point current = start;
            int sector = 1;
            bool completed = false;
            long iterations = 0;
            long maximumIterations = ((long)source.Rows * source.Cols * 8L) + 8L;

            do
            {
                if (++iterations > maximumIterations)
                {
                    throw new InvalidOperationException("Binary shape boundary tracing did not converge.");
                }

                for (int attempt = 0; attempt < 3; attempt++)
                {
                    bool moved = false;
                    int firstDirection = ((2 * sector) + 7) % 8;
                    for (int offset = 0; offset < 3; offset++)
                    {
                        int direction = (firstDirection + offset) % 8;
                        int x = current.X + BoundaryMoveX[direction];
                        int y = current.Y + BoundaryMoveY[direction];
                        if (x < 0 || x >= source.Cols || y < 0 || y >= source.Rows
                            || source.At<byte>(y, x) == 0)
                        {
                            continue;
                        }

                        current = new Point(x, y);
                        sector = direction / 2;
                        points.Add(current);
                        moved = true;
                        break;
                    }

                    if (moved)
                    {
                        break;
                    }

                    sector = (sector + 1) % 4;
                    completed = current == start && sector == 1;
                    if (completed)
                    {
                        break;
                    }
                }
            } while (!completed);

            if (points.Count > 1 && points[points.Count - 1] == start)
            {
                points.RemoveAt(points.Count - 1);
            }
            return points.ToArray();
        }

        private static Point[] ConvertBoundary(
            Point[] source,
            ContourApproximationModes approximationMode)
        {
            Point[] compressed = CompressDirectionChanges(source);
            if (approximationMode == ContourApproximationModes.ApproxNone || compressed.Length <= 2)
            {
                return compressed;
            }
            return Simplify(compressed, 1D);
        }

        private static Point[] NormalizeHole(
            Point[] source,
            ContourApproximationModes approximationMode)
        {
            Point[] oriented = OrientAndRotate(source);
            Point[] compressed = OrientAndRotate(CompressDirectionChanges(oriented));
            if (approximationMode == ContourApproximationModes.ApproxNone || compressed.Length <= 2)
            {
                return compressed;
            }
            return Simplify(compressed, 1D);
        }

        private static Point[] CompressDirectionChanges(Point[] source)
        {
            if (source == null || source.Length <= 2)
            {
                return source?.ToArray() ?? Array.Empty<Point>();
            }

            List<Point> result = new List<Point> { source[0] };
            int previousDirection = Direction(source[0], source[1]);
            for (int index = 1; index < source.Length; index++)
            {
                int direction = Direction(source[index], source[(index + 1) % source.Length]);
                if (direction != previousDirection)
                {
                    result.Add(source[index]);
                    previousDirection = direction;
                }
            }
            return result.ToArray();
        }

        private static Point[] OrientAndRotate(Point[] source)
        {
            if (source == null || source.Length == 0)
            {
                return Array.Empty<Point>();
            }

            Point[] points = source.ToArray();
            double area = SignedArea(points);
            if (area < 0D)
            {
                Array.Reverse(points);
            }

            Point[] forward = BestRotation(points);
            if (area != 0D)
            {
                return forward;
            }

            Point[] reverse = BestRotation(points.Reverse().ToArray());
            return CompareSequences(forward, reverse) <= 0 ? forward : reverse;
        }

        private static Point[] BestRotation(Point[] points)
        {
            Point minimum = points[0];
            for (int index = 1; index < points.Length; index++)
            {
                if (ComparePoint(points[index], minimum) < 0)
                {
                    minimum = points[index];
                }
            }

            int best = 0;
            while (ComparePoint(points[best], minimum) != 0)
            {
                best++;
            }
            for (int index = best + 1; index < points.Length; index++)
            {
                if (ComparePoint(points[index], minimum) == 0
                    && CompareRotations(points, index, best) < 0)
                {
                    best = index;
                }
            }

            if (best == 0)
            {
                return points;
            }

            Point[] rotated = new Point[points.Length];
            for (int index = 0; index < points.Length; index++)
            {
                rotated[index] = points[(best + index) % points.Length];
            }
            return rotated;
        }

        private static int CompareRotations(Point[] points, int leftStart, int rightStart)
        {
            for (int offset = 0; offset < points.Length; offset++)
            {
                int left = (leftStart + offset) % points.Length;
                int right = (rightStart + offset) % points.Length;
                int comparison = DirectionPriority(points[left], points[(left + 1) % points.Length])
                    .CompareTo(DirectionPriority(points[right], points[(right + 1) % points.Length]));
                if (comparison != 0)
                {
                    return comparison;
                }
            }
            return 0;
        }

        private static int CompareSequences(Point[] left, Point[] right)
        {
            for (int index = 0; index < left.Length; index++)
            {
                int comparison = DirectionPriority(left[index], left[(index + 1) % left.Length])
                    .CompareTo(DirectionPriority(right[index], right[(index + 1) % right.Length]));
                if (comparison != 0)
                {
                    return comparison;
                }
            }
            return 0;
        }

        private static int CompareContours(Point[] left, Point[] right)
        {
            return ComparePoint(left[0], right[0]);
        }

        private static int ComparePoint(Point left, Point right)
        {
            int y = left.Y.CompareTo(right.Y);
            return y != 0 ? y : left.X.CompareTo(right.X);
        }

        private static double SignedArea(Point[] points)
        {
            double area = 0D;
            for (int index = 0; index < points.Length; index++)
            {
                Point current = points[index];
                Point next = points[(index + 1) % points.Length];
                area += ((double)current.X * next.Y) - ((double)next.X * current.Y);
            }
            return area * 0.5D;
        }

        private static int Direction(Point from, Point to)
        {
            int dx = Math.Sign(to.X - from.X);
            int dy = Math.Sign(to.Y - from.Y);
            return ((dy + 1) * 3) + dx + 1;
        }

        private static int DirectionPriority(Point from, Point to)
        {
            int dx = Math.Sign(to.X - from.X);
            int dy = Math.Sign(to.Y - from.Y);
            if (dx > 0 && dy == 0) return 0;
            if (dx > 0 && dy > 0) return 1;
            if (dx == 0 && dy > 0) return 2;
            if (dx < 0 && dy > 0) return 3;
            if (dx < 0 && dy == 0) return 4;
            if (dx < 0 && dy < 0) return 5;
            if (dx == 0 && dy < 0) return 6;
            if (dx > 0 && dy < 0) return 7;
            return 8;
        }

        private static Point[] Simplify(Point[] points, double delta)
        {
            double furthestDistance = 0D;
            int furthestIndex = 0;
            for (int index = 1; index < points.Length; index++)
            {
                double distance = Distance(points[index], points[0]);
                if (distance > furthestDistance)
                {
                    furthestDistance = distance;
                    furthestIndex = index;
                }
            }

            if (furthestDistance < delta)
            {
                return new[] { points[0] };
            }

            bool[] retained = new bool[points.Length];
            retained[0] = true;
            retained[furthestIndex] = true;
            SimplifySegment(points, 0, furthestIndex, retained, delta);
            SimplifySegment(points, furthestIndex, -1, retained, delta);
            return points.Where((point, index) => retained[index]).ToArray();
        }

        private static void SimplifySegment(
            Point[] points,
            int firstIndex,
            int lastIndex,
            bool[] retained,
            double delta)
        {
            int endIndex = lastIndex < 0 ? points.Length : lastIndex;
            if (Math.Abs(firstIndex - endIndex) <= 1)
            {
                return;
            }

            Point first = points[firstIndex];
            Point last = lastIndex < 0 ? points[0] : points[lastIndex];
            double furthestDistance = 0D;
            int furthestIndex = 0;
            for (int index = firstIndex + 1; index < endIndex; index++)
            {
                double distance = DistanceToSegment(first, last, points[index]);
                if (distance >= delta && distance > furthestDistance)
                {
                    furthestDistance = distance;
                    furthestIndex = index;
                }
            }

            if (furthestIndex == 0)
            {
                return;
            }

            retained[furthestIndex] = true;
            SimplifySegment(points, firstIndex, furthestIndex, retained, delta);
            SimplifySegment(points, furthestIndex, lastIndex, retained, delta);
        }

        private static double DistanceToSegment(Point first, Point last, Point point)
        {
            double firstToLastX = last.X - first.X;
            double firstToLastY = last.Y - first.Y;
            double lastToPointX = point.X - last.X;
            double lastToPointY = point.Y - last.Y;
            if ((firstToLastX * lastToPointX) + (firstToLastY * lastToPointY) > 0D)
            {
                return Distance(last, point);
            }

            double lastToFirstX = first.X - last.X;
            double lastToFirstY = first.Y - last.Y;
            double firstToPointX = point.X - first.X;
            double firstToPointY = point.Y - first.Y;
            if ((lastToFirstX * firstToPointX) + (lastToFirstY * firstToPointY) > 0D)
            {
                return Distance(first, point);
            }

            double length = Distance(first, last);
            if (length == 0D)
            {
                return Distance(first, point);
            }

            double cross = (firstToLastX * (point.Y - first.Y))
                - (firstToLastY * (point.X - first.X));
            return Math.Abs(cross / length);
        }

        private static double Distance(Point left, Point right)
        {
            double x = left.X - right.X;
            double y = left.Y - right.Y;
            return Math.Sqrt((x * x) + (y * y));
        }

        private sealed class ComponentMoments
        {
            private double m01;
            private double m02;
            private double m10;
            private double m11;
            private double m20;

            internal ComponentMoments(int nativeIndex)
            {
                NativeIndex = nativeIndex;
            }

            private int NativeIndex { get; }

            internal void Add(int x, int y)
            {
                m10 += x;
                m01 += y;
                m11 += (double)x * y;
                m20 += (double)x * x;
                m02 += (double)y * y;
            }

            internal BinaryComponent Create(int area, Rect bounds)
            {
                Point2d center = new Point2d(m10 / area, m01 / area);
                double u11 = m11 - ((m10 * m01) / area);
                double u20 = m20 - ((m10 * m10) / area);
                double u02 = m02 - ((m01 * m01) / area);
                double angle = 0.5D * Math.Atan2(2D * u11, u20 - u02);
                return new BinaryComponent(NativeIndex, area, center, bounds, angle);
            }
        }

        private sealed class ContourEntry
        {
            internal ContourEntry(Point[] outer, List<Point[]> holes)
            {
                Outer = outer;
                Holes = holes;
            }

            internal Point[] Outer { get; }

            internal List<Point[]> Holes { get; }
        }
    }
}
