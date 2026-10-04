using OpenCvSharp;
using System;

namespace OpenVisionLab.Vision2D.Result
{
    /// <summary>Immutable similarity mapping from reference-image pixels to matched-image pixels.</summary>
    /// <remarks>Use poses from corresponding successful executions of the same template. Positive angles follow OpenCV's downward-Y image coordinates.</remarks>
    public sealed class MatchingCoordinateTransform
    {
        private readonly double cos;
        private readonly double sin;

        public Point2d ReferenceCenter { get; }
        public Point2d MatchedCenter { get; }
        public double AngleDegrees { get; }
        public double Scale { get; }

        /// <summary>Creates a mapping with an explicitly supplied relative angle and scale.</summary>
        public MatchingCoordinateTransform(Point2d referenceCenter, Point2d matchedCenter, double angleDegrees, double scale)
        {
            ValidatePoint(referenceCenter, nameof(referenceCenter));
            ValidatePoint(matchedCenter, nameof(matchedCenter));
            if (!IsFinite(angleDegrees)) throw new ArgumentOutOfRangeException(nameof(angleDegrees), "Angle must be finite.");
            if (!IsFinite(scale) || scale <= 0d || !IsFinite(1d / scale))
            {
                throw new ArgumentOutOfRangeException(nameof(scale), "Scale and its reciprocal must be finite and positive.");
            }

            ReferenceCenter = referenceCenter;
            MatchedCenter = matchedCenter;
            AngleDegrees = Math.IEEERemainder(angleDegrees, 360d);
            Scale = scale;
            double radians = AngleDegrees * Math.PI / 180d;
            cos = Math.Cos(radians);
            sin = Math.Sin(radians);
        }

        /// <summary>Snapshots two corresponding accepted poses; the caller checks each execution's success.</summary>
        public static MatchingCoordinateTransform FromMatches(MatchingResult referenceMatch, MatchingResult currentMatch)
        {
            if (referenceMatch == null) throw new ArgumentNullException(nameof(referenceMatch));
            if (currentMatch == null) throw new ArgumentNullException(nameof(currentMatch));
            return FromPoses(
                new Point2d(referenceMatch.Center.X, referenceMatch.Center.Y), referenceMatch.Angle, referenceMatch.Scale,
                new Point2d(currentMatch.Center.X, currentMatch.Center.Y), currentMatch.Angle, currentMatch.Scale);
        }

        /// <summary>Snapshots an accepted Teaching candidate's native pose and the current match.</summary>
        public static MatchingCoordinateTransform FromAutoMPoint(AutoMPointCandidateResult teaching, MatchingResult currentMatch)
        {
            if (teaching == null) throw new ArgumentNullException(nameof(teaching));
            if (currentMatch == null) throw new ArgumentNullException(nameof(currentMatch));
            if (!teaching.Accepted) throw new InvalidOperationException("Auto MPoint candidate must be accepted before creating a coordinate transform.");
            return FromPoses(
                new Point2d(teaching.NativeMatchCenter.X, teaching.NativeMatchCenter.Y), teaching.NativeMatchAngle, teaching.NativeMatchScale,
                new Point2d(currentMatch.Center.X, currentMatch.Center.Y), currentMatch.Angle, currentMatch.Scale);
        }

        /// <summary>Maps a reference-image point to matched-image pixels.</summary>
        public Point2d TransformPoint(Point2d referencePoint)
        {
            ValidatePoint(referencePoint, nameof(referencePoint));
            return MapOffset(referencePoint.X - ReferenceCenter.X, referencePoint.Y - ReferenceCenter.Y);
        }

        /// <summary>Maps an offset from the reference match center to a matched-image point, including translation.</summary>
        public Point2d TransformReferenceOffset(Point2d referenceOffset)
        {
            ValidatePoint(referenceOffset, nameof(referenceOffset));
            return MapOffset(referenceOffset.X, referenceOffset.Y);
        }

        /// <summary>Maps a matched-image point back to reference-image pixels.</summary>
        public Point2d InverseTransformPoint(Point2d matchedPoint)
        {
            ValidatePoint(matchedPoint, nameof(matchedPoint));
            double dx = (matchedPoint.X - MatchedCenter.X) / Scale;
            double dy = (matchedPoint.Y - MatchedCenter.Y) / Scale;
            return CheckedPoint(ReferenceCenter.X + (cos * dx - sin * dy), ReferenceCenter.Y + (sin * dx + cos * dy));
        }

        /// <summary>Maps continuous ROI boundaries in reference TL, TR, BR, BL order. No clipping, rounding or bounding-box conversion occurs.</summary>
        public Point2d[] TransformRoi(Rect2d referenceRoi)
        {
            double right = referenceRoi.X + referenceRoi.Width;
            double bottom = referenceRoi.Y + referenceRoi.Height;
            if (!IsFinite(referenceRoi.X) || !IsFinite(referenceRoi.Y)
                || !IsFinite(referenceRoi.Width) || !IsFinite(referenceRoi.Height)
                || referenceRoi.Width <= 0d || referenceRoi.Height <= 0d
                || !IsFinite(right) || !IsFinite(bottom) || right <= referenceRoi.X || bottom <= referenceRoi.Y)
            {
                throw new ArgumentOutOfRangeException(nameof(referenceRoi), "ROI must have finite, positive and representable continuous boundaries.");
            }

            return new[]
            {
                TransformPoint(new Point2d(referenceRoi.X, referenceRoi.Y)),
                TransformPoint(new Point2d(right, referenceRoi.Y)),
                TransformPoint(new Point2d(right, bottom)),
                TransformPoint(new Point2d(referenceRoi.X, bottom))
            };
        }

        private static MatchingCoordinateTransform FromPoses(
            Point2d referenceCenter, double referenceAngle, double referenceScale,
            Point2d currentCenter, double currentAngle, double currentScale)
        {
            if (!IsFinite(referenceAngle)) throw new ArgumentOutOfRangeException(nameof(referenceAngle), "Reference pose angle must be finite.");
            if (!IsFinite(currentAngle)) throw new ArgumentOutOfRangeException(nameof(currentAngle), "Current pose angle must be finite.");
            if (!IsFinite(referenceScale) || referenceScale <= 0d) throw new ArgumentOutOfRangeException(nameof(referenceScale), "Reference pose scale must be finite and positive.");
            if (!IsFinite(currentScale) || currentScale <= 0d) throw new ArgumentOutOfRangeException(nameof(currentScale), "Current pose scale must be finite and positive.");

            double angle = Math.IEEERemainder(currentAngle, 360d) - Math.IEEERemainder(referenceAngle, 360d);
            return new MatchingCoordinateTransform(referenceCenter, currentCenter, angle, currentScale / referenceScale);
        }

        private Point2d MapOffset(double dx, double dy)
        {
            if (!IsFinite(dx) || !IsFinite(dy)) throw new OverflowException("Reference offset is outside finite coordinate range.");
            return CheckedPoint(MatchedCenter.X + Scale * (cos * dx + sin * dy), MatchedCenter.Y + Scale * (-sin * dx + cos * dy));
        }

        private static void ValidatePoint(Point2d point, string parameterName)
        {
            if (!IsFinite(point.X) || !IsFinite(point.Y)) throw new ArgumentOutOfRangeException(parameterName, "Point coordinates must be finite.");
        }

        private static Point2d CheckedPoint(double x, double y)
        {
            if (!IsFinite(x) || !IsFinite(y)) throw new OverflowException("Mapped point is outside finite coordinate range.");
            return new Point2d(x, y);
        }

        private static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
