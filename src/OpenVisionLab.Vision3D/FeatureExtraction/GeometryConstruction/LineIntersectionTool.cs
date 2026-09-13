using System;
using System.Globalization;
using System.Threading;

namespace OpenVisionLab.Vision3D.FeatureExtraction
{
    /// <summary>
    /// Source-neutral infinite-line and finite-segment evidence for pure
    /// feature geometry. Source, unit, frame, recipe, and artifact identity
    /// remain the caller's responsibility.
    /// </summary>
    public sealed class ThreeDLineGeometry
    {
        public ThreeDLineGeometry(
            ThreeDPoint anchor,
            ThreeDPoint direction,
            ThreeDPoint segmentStart,
            ThreeDPoint segmentEnd)
        {
            Anchor = anchor;
            Direction = direction;
            SegmentStart = segmentStart;
            SegmentEnd = segmentEnd;
        }

        public ThreeDPoint Anchor { get; }

        public ThreeDPoint Direction { get; }

        public ThreeDPoint SegmentStart { get; }

        public ThreeDPoint SegmentEnd { get; }
    }

    public sealed class LineIntersectionOptions
    {
        public double MaximumClosestApproachDistance { get; set; }

        public double MinimumAcuteAngleDegrees { get; set; }

        public double MaximumSupportExtension { get; set; }
    }

    public sealed class LineIntersectionResult
    {
        private LineIntersectionResult(
            bool success,
            string message,
            ThreeDPoint cornerAnchor,
            ThreeDPoint firstClosestPoint,
            ThreeDPoint secondClosestPoint,
            double firstLineParameter,
            double secondLineParameter,
            double acuteAngleDegrees,
            double closestApproachDistance,
            double firstSupportMinimum,
            double firstSupportMaximum,
            double firstSupportExtension,
            double secondSupportMinimum,
            double secondSupportMaximum,
            double secondSupportExtension)
        {
            Success = success;
            Message = message ?? string.Empty;
            CornerAnchor = cornerAnchor;
            FirstClosestPoint = firstClosestPoint;
            SecondClosestPoint = secondClosestPoint;
            FirstLineParameter = firstLineParameter;
            SecondLineParameter = secondLineParameter;
            AcuteAngleDegrees = acuteAngleDegrees;
            ClosestApproachDistance = closestApproachDistance;
            FirstSupportMinimum = firstSupportMinimum;
            FirstSupportMaximum = firstSupportMaximum;
            FirstSupportExtension = firstSupportExtension;
            SecondSupportMinimum = secondSupportMinimum;
            SecondSupportMaximum = secondSupportMaximum;
            SecondSupportExtension = secondSupportExtension;
        }

        public bool Success { get; }

        public string Message { get; }

        public ThreeDPoint CornerAnchor { get; }

        public ThreeDPoint FirstClosestPoint { get; }

        public ThreeDPoint SecondClosestPoint { get; }

        public double FirstLineParameter { get; }

        public double SecondLineParameter { get; }

        public double AcuteAngleDegrees { get; }

        public double ClosestApproachDistance { get; }

        public double FirstSupportMinimum { get; }

        public double FirstSupportMaximum { get; }

        public double FirstSupportExtension { get; }

        public double SecondSupportMinimum { get; }

        public double SecondSupportMaximum { get; }

        public double SecondSupportExtension { get; }

        internal static LineIntersectionResult Completed(
            ThreeDPoint cornerAnchor,
            ThreeDPoint firstClosestPoint,
            ThreeDPoint secondClosestPoint,
            double firstLineParameter,
            double secondLineParameter,
            double acuteAngleDegrees,
            double closestApproachDistance,
            double firstSupportMinimum,
            double firstSupportMaximum,
            double firstSupportExtension,
            double secondSupportMinimum,
            double secondSupportMaximum,
            double secondSupportExtension)
        {
            return new LineIntersectionResult(
                true,
                "Completed full-XYZ closest-approach line intersection geometry.",
                cornerAnchor,
                firstClosestPoint,
                secondClosestPoint,
                firstLineParameter,
                secondLineParameter,
                acuteAngleDegrees,
                closestApproachDistance,
                firstSupportMinimum,
                firstSupportMaximum,
                firstSupportExtension,
                secondSupportMinimum,
                secondSupportMaximum,
                secondSupportExtension);
        }

        internal static LineIntersectionResult Failed(string message)
        {
            return new LineIntersectionResult(
                false,
                message,
                null,
                null,
                null,
                double.NaN,
                double.NaN,
                double.NaN,
                double.NaN,
                double.NaN,
                double.NaN,
                double.NaN,
                double.NaN,
                double.NaN,
                double.NaN);
        }
    }

    /// <summary>
    /// Pure full-XYZ closest-approach, angle, and finite-support evaluation
    /// for two normalized source-neutral line geometries. It creates no
    /// recipe artifact and makes no physical or metrology claim.
    /// </summary>
    public sealed class LineIntersectionTool
    {
        private const double DirectionTolerance = 1e-8;
        private const double ParallelDenominatorEpsilon = 1e-12;

        public LineIntersectionResult Execute(
            ThreeDLineGeometry firstLine,
            ThreeDLineGeometry secondLine,
            LineIntersectionOptions options,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            try
            {
                Validate(firstLine, secondLine, options);
                cancellationToken.ThrowIfCancellationRequested();

                ThreeDPoint a = firstLine.Anchor;
                ThreeDPoint b = secondLine.Anchor;
                ThreeDPoint u = firstLine.Direction;
                ThreeDPoint v = secondLine.Direction;
                double dot = Clamp(Dot(u, v), -1.0, 1.0);
                double acuteAngleDegrees = Math.Acos(Math.Abs(dot)) * 180.0 / Math.PI;
                if (!IsFinite(acuteAngleDegrees) || acuteAngleDegrees < options.MinimumAcuteAngleDegrees)
                {
                    return LineIntersectionResult.Failed(
                        "Line acute angle "
                        + acuteAngleDegrees.ToString("G8", CultureInfo.InvariantCulture)
                        + " degrees is below taught minimum "
                        + options.MinimumAcuteAngleDegrees.ToString("G8", CultureInfo.InvariantCulture)
                        + " degrees.");
                }

                double denominator = 1.0 - (dot * dot);
                if (!IsFinite(denominator) || denominator <= ParallelDenominatorEpsilon)
                {
                    return LineIntersectionResult.Failed("Line intersection rejects parallel or numerically near-parallel lines.");
                }

                ThreeDPoint w = Subtract(a, b);
                double d = Dot(u, w);
                double e = Dot(v, w);
                double firstParameter = ((dot * e) - d) / denominator;
                double secondParameter = (e - (dot * d)) / denominator;
                ThreeDPoint firstClosest = Add(a, Scale(u, firstParameter));
                ThreeDPoint secondClosest = Add(b, Scale(v, secondParameter));
                double gap = Length(Subtract(firstClosest, secondClosest));
                RequireFinite(firstParameter, "First line closest parameter");
                RequireFinite(secondParameter, "Second line closest parameter");
                RequireFinite(firstClosest, "First line closest point");
                RequireFinite(secondClosest, "Second line closest point");
                if (!IsFinite(gap) || gap > options.MaximumClosestApproachDistance)
                {
                    return LineIntersectionResult.Failed(
                        "Line closest-approach gap "
                        + gap.ToString("G8", CultureInfo.InvariantCulture)
                        + " source-coordinate exceeds taught maximum "
                        + options.MaximumClosestApproachDistance.ToString("G8", CultureInfo.InvariantCulture)
                        + ".");
                }

                Support firstSupport = GetSupport(firstLine, firstParameter);
                Support secondSupport = GetSupport(secondLine, secondParameter);
                if (firstSupport.Extension > options.MaximumSupportExtension || secondSupport.Extension > options.MaximumSupportExtension)
                {
                    return LineIntersectionResult.Failed(
                        "Line closest approach is outside taught inlier support extension "
                        + options.MaximumSupportExtension.ToString("G8", CultureInfo.InvariantCulture)
                        + " source-coordinate.");
                }

                ThreeDPoint corner = Scale(Add(firstClosest, secondClosest), 0.5);
                RequireFinite(corner, "Corner anchor");
                return LineIntersectionResult.Completed(
                    corner,
                    firstClosest,
                    secondClosest,
                    firstParameter,
                    secondParameter,
                    acuteAngleDegrees,
                    gap,
                    firstSupport.Minimum,
                    firstSupport.Maximum,
                    firstSupport.Extension,
                    secondSupport.Minimum,
                    secondSupport.Maximum,
                    secondSupport.Extension);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                return LineIntersectionResult.Failed("Line intersection failed: " + exception.Message);
            }
        }

        private static void Validate(ThreeDLineGeometry firstLine, ThreeDLineGeometry secondLine, LineIntersectionOptions options)
        {
            if (firstLine == null || secondLine == null)
            {
                throw new ArgumentException("Line intersection requires two explicit line geometries.");
            }
            if (options == null || !IsFinite(options.MaximumClosestApproachDistance) || options.MaximumClosestApproachDistance <= 0.0)
            {
                throw new ArgumentException("MaximumClosestApproachDistance must be a finite number greater than zero.");
            }
            if (!IsFinite(options.MinimumAcuteAngleDegrees) || options.MinimumAcuteAngleDegrees <= 0.0 || options.MinimumAcuteAngleDegrees > 90.0)
            {
                throw new ArgumentException("MinimumAcuteAngleDegrees must be a finite number greater than zero and no greater than 90.");
            }
            if (!IsFinite(options.MaximumSupportExtension) || options.MaximumSupportExtension < 0.0)
            {
                throw new ArgumentException("MaximumSupportExtension must be a finite number no less than zero.");
            }
            ValidateLine(firstLine, "First");
            ValidateLine(secondLine, "Second");
        }

        private static void ValidateLine(ThreeDLineGeometry line, string label)
        {
            if (line.Anchor == null || line.Direction == null || line.SegmentStart == null || line.SegmentEnd == null
                || !line.Anchor.IsFinite || !line.Direction.IsFinite || !line.SegmentStart.IsFinite || !line.SegmentEnd.IsFinite)
            {
                throw new ArgumentException(label + " line geometry contains non-finite coordinates.");
            }
            double length = Length(line.Direction);
            if (!IsFinite(length) || Math.Abs(length - 1.0) > DirectionTolerance)
            {
                throw new ArgumentException(label + " line direction must be finite and normalized.");
            }
        }

        private static Support GetSupport(ThreeDLineGeometry line, double parameter)
        {
            double start = Dot(Subtract(line.SegmentStart, line.Anchor), line.Direction);
            double end = Dot(Subtract(line.SegmentEnd, line.Anchor), line.Direction);
            double minimum = Math.Min(start, end);
            double maximum = Math.Max(start, end);
            RequireFinite(minimum, "Line support minimum");
            RequireFinite(maximum, "Line support maximum");
            double extension = parameter < minimum ? minimum - parameter : parameter > maximum ? parameter - maximum : 0.0;
            return new Support(minimum, maximum, extension);
        }

        private static double Clamp(double value, double minimum, double maximum)
        {
            return value < minimum ? minimum : value > maximum ? maximum : value;
        }

        private static double Dot(ThreeDPoint left, ThreeDPoint right)
        {
            return (left.X * right.X) + (left.Y * right.Y) + (left.Z * right.Z);
        }

        private static ThreeDPoint Add(ThreeDPoint left, ThreeDPoint right)
        {
            return new ThreeDPoint(left.X + right.X, left.Y + right.Y, left.Z + right.Z);
        }

        private static ThreeDPoint Subtract(ThreeDPoint left, ThreeDPoint right)
        {
            return new ThreeDPoint(left.X - right.X, left.Y - right.Y, left.Z - right.Z);
        }

        private static ThreeDPoint Scale(ThreeDPoint point, double scale)
        {
            return new ThreeDPoint(point.X * scale, point.Y * scale, point.Z * scale);
        }

        private static double Length(ThreeDPoint point)
        {
            return Math.Sqrt(Dot(point, point));
        }

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }

        private static void RequireFinite(double value, string label)
        {
            if (!IsFinite(value)) throw new ArgumentException(label + " is non-finite.");
        }

        private static void RequireFinite(ThreeDPoint point, string label)
        {
            if (point == null || !point.IsFinite) throw new ArgumentException(label + " is non-finite.");
        }

        private sealed class Support
        {
            public Support(double minimum, double maximum, double extension)
            {
                Minimum = minimum;
                Maximum = maximum;
                Extension = extension;
            }

            public double Minimum { get; }

            public double Maximum { get; }

            public double Extension { get; }
        }
    }
}
