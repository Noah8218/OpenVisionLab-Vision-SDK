using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace OpenVisionLab.Vision3D.FeatureExtraction
{
    public enum DeterministicLineFitPositiveAxis
    {
        X,
        Z
    }

    /// <summary>
    /// Source-neutral ordered XYZ input. The caller owns source identity and
    /// supplies the stable input hash used only for deterministic scheduling.
    /// </summary>
    public sealed class DeterministicLineFitPoint
    {
        public DeterministicLineFitPoint(int scanlineIndex, ThreeDPoint point)
        {
            ScanlineIndex = scanlineIndex;
            Point = point;
        }

        public int ScanlineIndex { get; }

        public ThreeDPoint Point { get; }
    }

    public sealed class DeterministicLineFitOptions
    {
        public const int MaximumHypotheses = 256;
        public const int MaximumRefinementIterations = 10;

        public string InputHash { get; set; }

        public double MaximumOrthogonalResidual { get; set; }

        public int MinimumInlierCount { get; set; }

        public double MinimumInlierRatio { get; set; }

        public int MinimumInlierScanlineSpan { get; set; }

        public DeterministicLineFitPositiveAxis PositiveScanlineAxis { get; set; }
    }

    public sealed class DeterministicLineFitPointDiagnostic
    {
        public DeterministicLineFitPointDiagnostic(
            int inputPointIndex,
            int scanlineIndex,
            ThreeDPoint sourcePoint,
            ThreeDPoint projectedPoint,
            double orthogonalResidual,
            bool isInlier)
        {
            InputPointIndex = inputPointIndex;
            ScanlineIndex = scanlineIndex;
            SourcePoint = sourcePoint;
            ProjectedPoint = projectedPoint;
            OrthogonalResidual = orthogonalResidual;
            IsInlier = isInlier;
        }

        public int InputPointIndex { get; }

        public int ScanlineIndex { get; }

        public ThreeDPoint SourcePoint { get; }

        public ThreeDPoint ProjectedPoint { get; }

        public double OrthogonalResidual { get; }

        public bool IsInlier { get; }
    }

    public sealed class DeterministicLineFitDiagnostics
    {
        public DeterministicLineFitDiagnostics(
            int inputPointCount,
            int inlierCount,
            int outlierCount,
            double inlierRatio,
            int inlierScanlineMinimum,
            int inlierScanlineMaximum,
            int inlierScanlineSpan,
            double residualRms,
            double residualMaximum,
            double residualMedian,
            double projectedSegmentLength,
            int hypothesisCount,
            int refinementIterationCount)
        {
            InputPointCount = inputPointCount;
            InlierCount = inlierCount;
            OutlierCount = outlierCount;
            InlierRatio = inlierRatio;
            InlierScanlineMinimum = inlierScanlineMinimum;
            InlierScanlineMaximum = inlierScanlineMaximum;
            InlierScanlineSpan = inlierScanlineSpan;
            ResidualRms = residualRms;
            ResidualMaximum = residualMaximum;
            ResidualMedian = residualMedian;
            ProjectedSegmentLength = projectedSegmentLength;
            HypothesisCount = hypothesisCount;
            RefinementIterationCount = refinementIterationCount;
        }

        public int InputPointCount { get; }
        public int InlierCount { get; }
        public int OutlierCount { get; }
        public double InlierRatio { get; }
        public int InlierScanlineMinimum { get; }
        public int InlierScanlineMaximum { get; }
        public int InlierScanlineSpan { get; }
        public double ResidualRms { get; }
        public double ResidualMaximum { get; }
        public double ResidualMedian { get; }
        public double ProjectedSegmentLength { get; }
        public int HypothesisCount { get; }
        public int RefinementIterationCount { get; }
    }

    public sealed class DeterministicLineFitResult
    {
        private DeterministicLineFitResult(
            bool success,
            string message,
            ThreeDLineGeometry geometry,
            DeterministicLineFitDiagnostics diagnostics,
            IReadOnlyList<DeterministicLineFitPointDiagnostic> pointDiagnostics)
        {
            Success = success;
            Message = message ?? string.Empty;
            Geometry = geometry;
            Diagnostics = diagnostics;
            PointDiagnostics = pointDiagnostics ?? new DeterministicLineFitPointDiagnostic[0];
        }

        public bool Success { get; }

        public string Message { get; }

        public ThreeDLineGeometry Geometry { get; }

        public DeterministicLineFitDiagnostics Diagnostics { get; }

        public IReadOnlyList<DeterministicLineFitPointDiagnostic> PointDiagnostics { get; }

        internal static DeterministicLineFitResult Completed(
            ThreeDLineGeometry geometry,
            DeterministicLineFitDiagnostics diagnostics,
            IReadOnlyList<DeterministicLineFitPointDiagnostic> pointDiagnostics)
        {
            return new DeterministicLineFitResult(
                true,
                "Completed deterministic full-XYZ consensus/TLS line fit.",
                geometry,
                diagnostics,
                pointDiagnostics);
        }

        internal static DeterministicLineFitResult Failed(string message)
        {
            return new DeterministicLineFitResult(false, message, null, null, new DeterministicLineFitPointDiagnostic[0]);
        }
    }

    /// <summary>
    /// Pure deterministic full-XYZ consensus plus orthogonal-TLS line fitting.
    /// It owns no C3D, recipe, UI, calibration, or acceptance semantics.
    /// </summary>
    public sealed class DeterministicLineFitTool
    {
        private const double DirectionEpsilon = 1e-10;

        public DeterministicLineFitResult Execute(
            IReadOnlyList<DeterministicLineFitPoint> points,
            DeterministicLineFitOptions options,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            try
            {
                Validate(points, options);
                List<Pair> pairs = CreatePairs(options.InputHash, points, cancellationToken);
                Candidate winner = FindBestCandidate(points, pairs, options, cancellationToken);
                if (winner == null)
                {
                    return DeterministicLineFitResult.Failed("No non-degenerate Line Fit hypothesis satisfies the taught inlier support gates.");
                }

                bool[] membership = winner.Inliers;
                FittedLine fitted = default(FittedLine);
                int refinementIterations = 0;
                for (int iteration = 1; iteration <= DeterministicLineFitOptions.MaximumRefinementIterations; iteration++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    fitted = FitTls(points, membership, options.PositiveScanlineAxis);
                    bool[] reclassified = Classify(points, fitted, options.MaximumOrthogonalResidual, cancellationToken);
                    RequireSupport(points, reclassified, options, fitted);
                    refinementIterations = iteration;
                    if (SameMembership(membership, reclassified))
                    {
                        membership = reclassified;
                        break;
                    }

                    membership = reclassified;
                    if (iteration == DeterministicLineFitOptions.MaximumRefinementIterations)
                    {
                        return DeterministicLineFitResult.Failed("Line Fit TLS refinement did not stabilize within the fixed limit of 10 iterations.");
                    }
                }

                fitted = FitTls(points, membership, options.PositiveScanlineAxis);
                bool[] finalMembership = Classify(points, fitted, options.MaximumOrthogonalResidual, cancellationToken);
                RequireSupport(points, finalMembership, options, fitted);
                if (!SameMembership(membership, finalMembership))
                {
                    return DeterministicLineFitResult.Failed("Line Fit TLS refinement did not stabilize within the fixed limit of 10 iterations.");
                }

                double minimumProjection;
                double maximumProjection;
                DeterministicLineFitPointDiagnostic[] pointDiagnostics = CreateDiagnostics(
                    points,
                    fitted,
                    finalMembership,
                    cancellationToken,
                    out minimumProjection,
                    out maximumProjection);
                DeterministicLineFitPointDiagnostic[] inlierDiagnostics = pointDiagnostics.Where(point => point.IsInlier).ToArray();
                double[] residuals = inlierDiagnostics.Select(point => point.OrthogonalResidual).OrderBy(value => value).ToArray();
                ThreeDPoint segmentStart = fitted.At(minimumProjection);
                ThreeDPoint segmentEnd = fitted.At(maximumProjection);
                RequireFinite(segmentStart, "Line Fit segment start");
                RequireFinite(segmentEnd, "Line Fit segment end");
                int[] scanlines = inlierDiagnostics.Select(point => point.ScanlineIndex).ToArray();
                DeterministicLineFitDiagnostics diagnostics = new DeterministicLineFitDiagnostics(
                    points.Count,
                    inlierDiagnostics.Length,
                    points.Count - inlierDiagnostics.Length,
                    (double)inlierDiagnostics.Length / points.Count,
                    scanlines.Min(),
                    scanlines.Max(),
                    scanlines.Max() - scanlines.Min(),
                    Math.Sqrt(inlierDiagnostics.Average(point => point.OrthogonalResidual * point.OrthogonalResidual)),
                    residuals[residuals.Length - 1],
                    Median(residuals),
                    maximumProjection - minimumProjection,
                    pairs.Count,
                    refinementIterations);
                return DeterministicLineFitResult.Completed(
                    new ThreeDLineGeometry(fitted.Anchor, fitted.Direction, segmentStart, segmentEnd),
                    diagnostics,
                    pointDiagnostics);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception) when (exception is ArgumentException || exception is InvalidDataException || exception is OverflowException)
            {
                return DeterministicLineFitResult.Failed(exception.Message);
            }
        }

        private static void Validate(IReadOnlyList<DeterministicLineFitPoint> points, DeterministicLineFitOptions options)
        {
            if (points == null) throw new ArgumentNullException(nameof(points));
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (string.IsNullOrWhiteSpace(options.InputHash)) throw new InvalidDataException("Line Fit requires a stable non-empty input hash for deterministic pair scheduling.");
            if (points.Count < 3) throw new InvalidDataException("Line Fit requires at least three EdgePointSet points; received " + points.Count + ".");
            if (!IsFinite(options.MaximumOrthogonalResidual) || options.MaximumOrthogonalResidual <= 0.0)
            {
                throw new InvalidDataException("MaximumOrthogonalResidual must be an explicit finite number greater than zero.");
            }
            if (options.MinimumInlierCount < 3 || options.MinimumInlierCount > points.Count)
            {
                throw new InvalidDataException("MinimumInlierCount must be an integer from 3 through " + points.Count + ".");
            }
            if (!IsFinite(options.MinimumInlierRatio) || options.MinimumInlierRatio <= 0.0 || options.MinimumInlierRatio > 1.0)
            {
                throw new InvalidDataException("MinimumInlierRatio must be an explicit finite number greater than zero and no greater than one.");
            }
            if (options.MinimumInlierScanlineSpan < 2)
            {
                throw new InvalidDataException("MinimumInlierScanlineSpan must be an integer of at least two grid-index intervals.");
            }
            if (points[points.Count - 1].ScanlineIndex - points[0].ScanlineIndex < options.MinimumInlierScanlineSpan)
            {
                throw new InvalidDataException("MinimumInlierScanlineSpan cannot be reached by the available EdgePointSet points.");
            }

            int previousScanline = int.MinValue;
            for (int index = 0; index < points.Count; index++)
            {
                DeterministicLineFitPoint point = points[index];
                if (point == null || point.ScanlineIndex <= previousScanline)
                {
                    throw new InvalidDataException("Line Fit requires finite EdgePointSet points ordered by unique ascending ScanlineIndex.");
                }
                previousScanline = point.ScanlineIndex;
                if (point.Point == null || !point.Point.IsFinite)
                {
                    throw new InvalidDataException("Line Fit rejects non-finite EdgePointSet coordinates.");
                }
            }
        }

        private static List<Pair> CreatePairs(string inputHash, IReadOnlyList<DeterministicLineFitPoint> points, CancellationToken cancellationToken)
        {
            int pairCount = points.Count * (points.Count - 1) / 2;
            if (pairCount <= DeterministicLineFitOptions.MaximumHypotheses)
            {
                List<Pair> all = new List<Pair>(pairCount);
                for (int first = 0; first < points.Count - 1; first++)
                {
                    for (int second = first + 1; second < points.Count; second++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        if (!IsDegeneratePair(points[first], points[second])) all.Add(new Pair(first, second));
                    }
                }
                return all;
            }

            List<Pair> pairs = new List<Pair>(DeterministicLineFitOptions.MaximumHypotheses);
            HashSet<Pair> unique = new HashSet<Pair>();
            for (int attempt = 0; pairs.Count < DeterministicLineFitOptions.MaximumHypotheses && attempt < points.Count * points.Count * 32; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                byte[] bytes;
                using (SHA256 sha256 = SHA256.Create())
                {
                    bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(inputHash.ToUpperInvariant() + "|" + attempt));
                }
                int first = (int)(ReadUInt32BigEndian(bytes, 0) % (uint)points.Count);
                int second = (int)(ReadUInt32BigEndian(bytes, sizeof(uint)) % (uint)points.Count);
                if (first == second) continue;
                if (first > second)
                {
                    int temporary = first;
                    first = second;
                    second = temporary;
                }
                Pair pair = new Pair(first, second);
                if (unique.Add(pair) && !IsDegeneratePair(points[first], points[second])) pairs.Add(pair);
            }
            return pairs;
        }

        private static Candidate FindBestCandidate(
            IReadOnlyList<DeterministicLineFitPoint> points,
            IReadOnlyList<Pair> pairs,
            DeterministicLineFitOptions options,
            CancellationToken cancellationToken)
        {
            Candidate best = null;
            foreach (Pair pair in pairs)
            {
                cancellationToken.ThrowIfCancellationRequested();
                FittedLine line = FittedLine.FromPair(points[pair.First], points[pair.Second]);
                bool[] membership = Classify(points, line, options.MaximumOrthogonalResidual, cancellationToken);
                int count;
                double rms;
                int span;
                if (!HasSupport(points, membership, options, line, out count, out rms, out span)) continue;
                Candidate candidate = new Candidate(pair, membership, count, rms, span);
                if (best == null || candidate.IsBetterThan(best)) best = candidate;
            }
            return best;
        }

        private static bool[] Classify(
            IReadOnlyList<DeterministicLineFitPoint> points,
            FittedLine line,
            double maximumResidual,
            CancellationToken cancellationToken)
        {
            bool[] membership = new bool[points.Count];
            for (int index = 0; index < points.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                double residual = line.Residual(points[index]);
                if (!IsFinite(residual)) throw new InvalidDataException("Line Fit produced a non-finite orthogonal residual.");
                membership[index] = residual <= maximumResidual;
            }
            return membership;
        }

        private static void RequireSupport(
            IReadOnlyList<DeterministicLineFitPoint> points,
            bool[] membership,
            DeterministicLineFitOptions options,
            FittedLine line)
        {
            int count;
            double rms;
            int span;
            if (!HasSupport(points, membership, options, line, out count, out rms, out span))
            {
                throw new InvalidDataException("Line Fit final inliers do not satisfy the taught count, ratio, and scanline-span support gates.");
            }
        }

        private static bool HasSupport(
            IReadOnlyList<DeterministicLineFitPoint> points,
            bool[] membership,
            DeterministicLineFitOptions options,
            FittedLine line,
            out int count,
            out double rms,
            out int span)
        {
            int[] inliers = Enumerable.Range(0, points.Count).Where(index => membership[index]).ToArray();
            count = inliers.Length;
            if (count == 0)
            {
                rms = double.PositiveInfinity;
                span = 0;
                return false;
            }
            int first = inliers.Min(index => points[index].ScanlineIndex);
            int last = inliers.Max(index => points[index].ScanlineIndex);
            span = last - first;
            rms = Math.Sqrt(inliers.Average(index => Math.Pow(line.Residual(points[index]), 2)));
            if (!IsFinite(rms)) throw new InvalidDataException("Line Fit candidate residual RMS is non-finite.");
            return count >= options.MinimumInlierCount
                && (double)count / points.Count >= options.MinimumInlierRatio
                && span >= options.MinimumInlierScanlineSpan;
        }

        private static FittedLine FitTls(
            IReadOnlyList<DeterministicLineFitPoint> points,
            bool[] membership,
            DeterministicLineFitPositiveAxis axis)
        {
            int[] inliers = Enumerable.Range(0, points.Count).Where(index => membership[index]).ToArray();
            if (inliers.Length < 3) throw new InvalidDataException("Line Fit TLS requires at least three inliers.");
            Vector3d anchor = new Vector3d(
                inliers.Average(index => points[index].Point.X),
                inliers.Average(index => points[index].Point.Y),
                inliers.Average(index => points[index].Point.Z));
            double[,] covariance = new double[3, 3];
            foreach (int index in inliers)
            {
                ThreeDPoint point = points[index].Point;
                Vector3d delta = new Vector3d(point.X - anchor.X, point.Y - anchor.Y, point.Z - anchor.Z);
                covariance[0, 0] += delta.X * delta.X; covariance[0, 1] += delta.X * delta.Y; covariance[0, 2] += delta.X * delta.Z;
                covariance[1, 1] += delta.Y * delta.Y; covariance[1, 2] += delta.Y * delta.Z;
                covariance[2, 2] += delta.Z * delta.Z;
            }
            covariance[1, 0] = covariance[0, 1]; covariance[2, 0] = covariance[0, 2]; covariance[2, 1] = covariance[1, 2];
            SymmetricEigenResult eigen = SymmetricEigen.Decompose(covariance);
            if (!IsFinite(eigen.Values[0]) || eigen.Values[0] <= DirectionEpsilon || eigen.Values[0] - eigen.Values[1] <= Math.Max(1.0, eigen.Values[0]) * 1e-12)
            {
                throw new InvalidDataException("Line Fit TLS covariance is degenerate and has no stable dominant direction.");
            }
            Vector3d direction = eigen.Vectors[0].Normalize();
            double requiredComponent = axis == DeterministicLineFitPositiveAxis.Z ? direction.Z : direction.X;
            if (!IsFinite(requiredComponent) || Math.Abs(requiredComponent) <= DirectionEpsilon)
            {
                throw new InvalidDataException("Line Fit direction does not advance along the required source scanline axis.");
            }
            if (requiredComponent < 0.0) direction = direction.Negate();
            return new FittedLine(anchor, direction);
        }

        private static DeterministicLineFitPointDiagnostic[] CreateDiagnostics(
            IReadOnlyList<DeterministicLineFitPoint> points,
            FittedLine line,
            bool[] membership,
            CancellationToken cancellationToken,
            out double minimumProjection,
            out double maximumProjection)
        {
            minimumProjection = double.PositiveInfinity;
            maximumProjection = double.NegativeInfinity;
            DeterministicLineFitPointDiagnostic[] diagnostics = new DeterministicLineFitPointDiagnostic[points.Count];
            for (int index = 0; index < points.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                DeterministicLineFitPoint point = points[index];
                ThreeDPoint projection = line.Project(point);
                double residual = line.Residual(point);
                RequireFinite(projection, "Line Fit projection");
                if (!IsFinite(residual)) throw new InvalidDataException("Line Fit produced a non-finite final residual.");
                double scalar = line.ProjectionScalar(point);
                if (membership[index])
                {
                    minimumProjection = Math.Min(minimumProjection, scalar);
                    maximumProjection = Math.Max(maximumProjection, scalar);
                }
                diagnostics[index] = new DeterministicLineFitPointDiagnostic(index, point.ScanlineIndex, point.Point, projection, residual, membership[index]);
            }
            if (!IsFinite(minimumProjection) || !IsFinite(maximumProjection) || maximumProjection < minimumProjection)
            {
                throw new InvalidDataException("Line Fit could not determine finite inlier projection extents.");
            }
            return diagnostics;
        }

        private static double Median(double[] sorted)
        {
            return sorted.Length % 2 == 1
                ? sorted[sorted.Length / 2]
                : (sorted[sorted.Length / 2 - 1] + sorted[sorted.Length / 2]) / 2.0;
        }

        private static bool SameMembership(bool[] first, bool[] second)
        {
            return first.Length == second.Length && first.SequenceEqual(second);
        }

        private static bool IsDegeneratePair(DeterministicLineFitPoint first, DeterministicLineFitPoint second)
        {
            double dx = first.Point.X - second.Point.X;
            double dy = first.Point.Y - second.Point.Y;
            double dz = first.Point.Z - second.Point.Z;
            return (dx * dx) + (dy * dy) + (dz * dz) <= DirectionEpsilon * DirectionEpsilon;
        }

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }

        private static void RequireFinite(ThreeDPoint value, string label)
        {
            if (value == null || !value.IsFinite) throw new InvalidDataException(label + " is non-finite.");
        }

        private static uint ReadUInt32BigEndian(byte[] bytes, int offset)
        {
            return ((uint)bytes[offset] << 24)
                | ((uint)bytes[offset + 1] << 16)
                | ((uint)bytes[offset + 2] << 8)
                | bytes[offset + 3];
        }

        private sealed class Pair : IEquatable<Pair>
        {
            public Pair(int first, int second)
            {
                First = first;
                Second = second;
            }

            public int First { get; }
            public int Second { get; }

            public bool Equals(Pair other)
            {
                return other != null && First == other.First && Second == other.Second;
            }

            public override bool Equals(object obj)
            {
                return Equals(obj as Pair);
            }

            public override int GetHashCode()
            {
                return (First * 397) ^ Second;
            }
        }

        private sealed class Candidate
        {
            public Candidate(Pair pair, bool[] inliers, int count, double rms, int span)
            {
                Pair = pair;
                Inliers = inliers;
                Count = count;
                Rms = rms;
                Span = span;
            }

            public Pair Pair { get; }
            public bool[] Inliers { get; }
            public int Count { get; }
            public double Rms { get; }
            public int Span { get; }

            public bool IsBetterThan(Candidate other)
            {
                return Count != other.Count ? Count > other.Count
                    : Rms != other.Rms ? Rms < other.Rms
                    : Span != other.Span ? Span > other.Span
                    : Pair.First != other.Pair.First ? Pair.First < other.Pair.First
                    : Pair.Second < other.Pair.Second;
            }
        }

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

            public double Length { get { return Math.Sqrt((X * X) + (Y * Y) + (Z * Z)); } }

            public Vector3d Normalize()
            {
                double length = Length;
                if (!IsFinite(length) || length <= DirectionEpsilon) throw new InvalidDataException("Line Fit direction length is degenerate.");
                return new Vector3d(X / length, Y / length, Z / length);
            }

            public Vector3d Negate()
            {
                return new Vector3d(-X, -Y, -Z);
            }
        }

        private struct FittedLine
        {
            public FittedLine(Vector3d anchor, Vector3d direction)
            {
                Anchor = new ThreeDPoint(anchor.X, anchor.Y, anchor.Z);
                Direction = new ThreeDPoint(direction.X, direction.Y, direction.Z);
            }

            public ThreeDPoint Anchor { get; }
            public ThreeDPoint Direction { get; }

            public static FittedLine FromPair(DeterministicLineFitPoint first, DeterministicLineFitPoint second)
            {
                Vector3d direction = new Vector3d(
                    second.Point.X - first.Point.X,
                    second.Point.Y - first.Point.Y,
                    second.Point.Z - first.Point.Z).Normalize();
                return new FittedLine(new Vector3d(first.Point.X, first.Point.Y, first.Point.Z), direction);
            }

            public double ProjectionScalar(DeterministicLineFitPoint point)
            {
                return (point.Point.X - Anchor.X) * Direction.X
                    + (point.Point.Y - Anchor.Y) * Direction.Y
                    + (point.Point.Z - Anchor.Z) * Direction.Z;
            }

            public ThreeDPoint Project(DeterministicLineFitPoint point)
            {
                return At(ProjectionScalar(point));
            }

            public ThreeDPoint At(double scalar)
            {
                return new ThreeDPoint(
                    Anchor.X + scalar * Direction.X,
                    Anchor.Y + scalar * Direction.Y,
                    Anchor.Z + scalar * Direction.Z);
            }

            public double Residual(DeterministicLineFitPoint point)
            {
                ThreeDPoint projection = Project(point);
                double dx = point.Point.X - projection.X;
                double dy = point.Point.Y - projection.Y;
                double dz = point.Point.Z - projection.Z;
                return Math.Sqrt((dx * dx) + (dy * dy) + (dz * dz));
            }
        }

        private sealed class SymmetricEigenResult
        {
            public SymmetricEigenResult(double[] values, Vector3d[] vectors)
            {
                Values = values;
                Vectors = vectors;
            }

            public double[] Values { get; }
            public Vector3d[] Vectors { get; }
        }

        private static class SymmetricEigen
        {
            public static SymmetricEigenResult Decompose(double[,] source)
            {
                double[,] matrix = (double[,])source.Clone();
                double[,] vectors = new double[,] { { 1, 0, 0 }, { 0, 1, 0 }, { 0, 0, 1 } };
                for (int iteration = 0; iteration < 32; iteration++)
                {
                    int p;
                    int q;
                    LargestOffDiagonal(matrix, out p, out q);
                    if (Math.Abs(matrix[p, q]) <= 1e-14) break;
                    double angle = 0.5 * Math.Atan2(2 * matrix[p, q], matrix[q, q] - matrix[p, p]);
                    double cosine = Math.Cos(angle);
                    double sine = Math.Sin(angle);
                    for (int index = 0; index < 3; index++)
                    {
                        double mp = matrix[index, p];
                        double mq = matrix[index, q];
                        matrix[index, p] = cosine * mp - sine * mq;
                        matrix[index, q] = sine * mp + cosine * mq;
                    }
                    for (int index = 0; index < 3; index++)
                    {
                        double mp = matrix[p, index];
                        double mq = matrix[q, index];
                        matrix[p, index] = cosine * mp - sine * mq;
                        matrix[q, index] = sine * mp + cosine * mq;
                    }
                    for (int index = 0; index < 3; index++)
                    {
                        double vp = vectors[index, p];
                        double vq = vectors[index, q];
                        vectors[index, p] = cosine * vp - sine * vq;
                        vectors[index, q] = sine * vp + cosine * vq;
                    }
                }
                var result = Enumerable.Range(0, 3)
                    .Select(index => new EigenPair(matrix[index, index], new Vector3d(vectors[0, index], vectors[1, index], vectors[2, index]).Normalize(), index))
                    .OrderByDescending(item => item.Value)
                    .ThenBy(item => item.Index)
                    .ToArray();
                return new SymmetricEigenResult(result.Select(item => item.Value).ToArray(), result.Select(item => item.Vector).ToArray());
            }

            private static void LargestOffDiagonal(double[,] matrix, out int p, out int q)
            {
                p = 0;
                q = 1;
                double largest = Math.Abs(matrix[0, 1]);
                foreach (var candidate in new[] { new[] { 0, 2 }, new[] { 1, 2 } })
                {
                    double value = Math.Abs(matrix[candidate[0], candidate[1]]);
                    if (value > largest)
                    {
                        largest = value;
                        p = candidate[0];
                        q = candidate[1];
                    }
                }
            }

            private sealed class EigenPair
            {
                public EigenPair(double value, Vector3d vector, int index)
                {
                    Value = value;
                    Vector = vector;
                    Index = index;
                }

                public double Value { get; }
                public Vector3d Vector { get; }
                public int Index { get; }
            }
        }
    }
}
