using OpenVisionLab.Vision3D.FeatureExtraction;
using System;

namespace OpenVisionLab.Vision3D.Inspection
{
    public sealed class PointPairDimensionsInspectionOptions
    {
        public double ExpectedDistance { get; set; }
        public double DistanceTolerance { get; set; }
        public double ExpectedPlanarWidth { get; set; }
        public double PlanarWidthTolerance { get; set; }
        public double ExpectedElevationAngleDegrees { get; set; }
        public double ElevationAngleToleranceDegrees { get; set; }
    }

    public sealed class PointPairDimensionsInspectionResult
    {
        public PointPairDimensionsInspectionResult(
            ThreeDPoint first,
            ThreeDPoint second,
            ThreeDPoint normalizedHeightAxis,
            ThreeDPoint delta,
            double distance,
            double planarWidth,
            double elevationAngleDegrees,
            double axialHeightDelta,
            double scalarHeightDelta,
            bool distancePassed,
            bool planarWidthPassed,
            bool elevationAnglePassed)
        {
            First = first;
            Second = second;
            NormalizedHeightAxis = normalizedHeightAxis;
            Delta = delta;
            Distance = distance;
            PlanarWidth = planarWidth;
            ElevationAngleDegrees = elevationAngleDegrees;
            AxialHeightDelta = axialHeightDelta;
            ScalarHeightDelta = scalarHeightDelta;
            DistancePassed = distancePassed;
            PlanarWidthPassed = planarWidthPassed;
            ElevationAnglePassed = elevationAnglePassed;
        }

        public ThreeDPoint First { get; }
        public ThreeDPoint Second { get; }
        public ThreeDPoint NormalizedHeightAxis { get; }
        public ThreeDPoint Delta { get; }
        public double Distance { get; }
        public double PlanarWidth { get; }
        public double ElevationAngleDegrees { get; }
        public double AxialHeightDelta { get; }
        public double ScalarHeightDelta { get; }
        public bool DistancePassed { get; }
        public bool PlanarWidthPassed { get; }
        public bool ElevationAnglePassed { get; }
        public bool Passed => DistancePassed && PlanarWidthPassed && ElevationAnglePassed;
    }

    /// <summary>
    /// Measures a full-XYZ point pair relative to a caller-owned height axis.
    /// Units, frame identity, source provenance, recipes, and overlays remain
    /// deliberately owned by the caller.
    /// </summary>
    public sealed class PointPairDimensionsInspectionTool
    {
        private const double MinimumLength = 1e-9;

        public PointPairDimensionsInspectionResult Execute(
            ThreeDPoint first,
            ThreeDPoint second,
            ThreeDPoint heightAxis,
            double firstScalarHeight,
            double secondScalarHeight,
            PointPairDimensionsInspectionOptions options)
        {
            if (!IsFinite(first) || !IsFinite(second) || !IsFinite(heightAxis))
            {
                throw new ArgumentException("Point-pair coordinates and height axis must be finite.");
            }

            if (!IsFinite(firstScalarHeight) || !IsFinite(secondScalarHeight))
            {
                throw new ArgumentException("Point-pair scalar heights must be finite.");
            }

            Validate(options);
            double axisLength = Length(heightAxis.X, heightAxis.Y, heightAxis.Z);
            if (axisLength <= MinimumLength)
            {
                throw new ArgumentException("Height axis must have non-zero length.", nameof(heightAxis));
            }

            double axisScale = Math.Max(Math.Abs(heightAxis.X), Math.Max(Math.Abs(heightAxis.Y), Math.Abs(heightAxis.Z)));
            double scaledAxisLength = Length(heightAxis.X / axisScale, heightAxis.Y / axisScale, heightAxis.Z / axisScale);
            ThreeDPoint normalizedAxis = IsFinite(axisLength)
                ? new ThreeDPoint(heightAxis.X / axisLength, heightAxis.Y / axisLength, heightAxis.Z / axisLength)
                : new ThreeDPoint((heightAxis.X / axisScale) / scaledAxisLength,
                    (heightAxis.Y / axisScale) / scaledAxisLength, (heightAxis.Z / axisScale) / scaledAxisLength);
            ThreeDPoint delta = new ThreeDPoint(
                second.X - first.X,
                second.Y - first.Y,
                second.Z - first.Z);
            double distance = Length(delta.X, delta.Y, delta.Z);
            if (!IsFinite(delta) || !IsFinite(distance))
            {
                throw new InvalidOperationException("Point-pair distance cannot be represented as a finite value.");
            }
            if (distance <= MinimumLength)
            {
                throw new ArgumentException("Point-pair positions must be distinct.");
            }

            double axialHeightDelta = Dot(delta, normalizedAxis);
            double planarX = delta.X - (axialHeightDelta * normalizedAxis.X);
            double planarY = delta.Y - (axialHeightDelta * normalizedAxis.Y);
            double planarZ = delta.Z - (axialHeightDelta * normalizedAxis.Z);
            double planarWidth = Length(planarX, planarY, planarZ);
            double elevationAngle = Math.Atan2(axialHeightDelta, planarWidth) * 180.0 / Math.PI;
            double scalarHeightDelta = secondScalarHeight - firstScalarHeight;
            if (!IsFinite(normalizedAxis) || !IsFinite(axialHeightDelta) || !IsFinite(planarWidth)
                || !IsFinite(elevationAngle) || !IsFinite(scalarHeightDelta))
            {
                throw new InvalidOperationException("Point-pair measurements cannot be represented as finite values.");
            }

            return new PointPairDimensionsInspectionResult(
                first,
                second,
                normalizedAxis,
                delta,
                distance,
                planarWidth,
                elevationAngle,
                axialHeightDelta,
                scalarHeightDelta,
                Within(distance, options.ExpectedDistance, options.DistanceTolerance),
                Within(planarWidth, options.ExpectedPlanarWidth, options.PlanarWidthTolerance),
                Within(elevationAngle, options.ExpectedElevationAngleDegrees, options.ElevationAngleToleranceDegrees));
        }

        private static void Validate(PointPairDimensionsInspectionOptions options)
        {
            if (options == null
                || !NonNegative(options.ExpectedDistance)
                || !NonNegative(options.DistanceTolerance)
                || !NonNegative(options.ExpectedPlanarWidth)
                || !NonNegative(options.PlanarWidthTolerance)
                || !IsFinite(options.ExpectedElevationAngleDegrees)
                || options.ExpectedElevationAngleDegrees < -90.0
                || options.ExpectedElevationAngleDegrees > 90.0
                || !NonNegative(options.ElevationAngleToleranceDegrees))
            {
                throw new ArgumentException("Expected point-pair values and tolerances are invalid.", nameof(options));
            }
        }

        private static bool Within(double actual, double expected, double tolerance) =>
            Math.Abs(actual - expected) <= tolerance;

        private static double Dot(ThreeDPoint first, ThreeDPoint second) =>
            (first.X * second.X) + (first.Y * second.Y) + (first.Z * second.Z);

        private static double Length(double x, double y, double z)
        {
            double squared = (x * x) + (y * y) + (z * z);
            if (IsFinite(squared) && squared > 0.0) return Math.Sqrt(squared);
            double scale = Math.Max(Math.Abs(x), Math.Max(Math.Abs(y), Math.Abs(z)));
            if (scale == 0.0 || !IsFinite(scale)) return scale;
            x /= scale;
            y /= scale;
            z /= scale;
            return scale * Math.Sqrt(x * x + y * y + z * z);
        }

        private static bool NonNegative(double value) => IsFinite(value) && value >= 0.0;

        private static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);

        private static bool IsFinite(ThreeDPoint point) =>
            point != null && IsFinite(point.X) && IsFinite(point.Y) && IsFinite(point.Z);
    }
}
