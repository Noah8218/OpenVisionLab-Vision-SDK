using OpenVisionLab.Vision2D.Property;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Drawing;

namespace OpenVisionLab.Vision2D.Tool
{
    public class AffineTransformTool : OpenCvAlgorithmBase
    {
        private const int MaximumOutputDimension = 32768;
        private const double DegenerateTriangleAreaEpsilon = 1e-9d;

        public IAffineTransformToolProperty property;

        private double[] matrixValues = new double[6];
        private double sourceTriangleArea;
        private double destinationTriangleArea;
        private double validPixelRatio;
        private Point2f[] destinationPoints = Array.Empty<Point2f>();
        private Point2f[] transformedFramePoints = Array.Empty<Point2f>();

        public void SetProperty(IAffineTransformToolProperty property) => this.property = property;

        protected override bool TryValidateBeforeRun(out VisionToolErrorCode errorCode, out string message)
        {
            if (!base.TryValidateBeforeRun(out errorCode, out message))
            {
                return false;
            }

            if (!TryCreatePoints(
                property.SourcePoint1X,
                property.SourcePoint1Y,
                property.SourcePoint2X,
                property.SourcePoint2Y,
                property.SourcePoint3X,
                property.SourcePoint3Y,
                out Point2f[] sourcePoints)
                || !TryCreatePoints(
                    property.DestinationPoint1X,
                    property.DestinationPoint1Y,
                    property.DestinationPoint2X,
                    property.DestinationPoint2Y,
                    property.DestinationPoint3X,
                    property.DestinationPoint3Y,
                    out Point2f[] targetPoints))
            {
                errorCode = VisionToolErrorCode.AffineInvalidPoint;
                message = "Affine source and destination point coordinates must be finite.";
                return false;
            }

            if (!IsFinite(property.BorderValue)
                || !IsFinite(property.MinimumSourceTriangleArea)
                || !IsFinite(property.MinimumDestinationTriangleArea)
                || !IsFinite(property.MinimumValidPixelRatio)
                || property.MinimumSourceTriangleArea < 0d
                || property.MinimumDestinationTriangleArea < 0d
                || property.MinimumValidPixelRatio < 0d
                || property.MinimumValidPixelRatio > 1d)
            {
                errorCode = VisionToolErrorCode.AffineInvalidGate;
                message = "Affine border value and gates must be finite; triangle areas must be non-negative and valid-pixel ratio must be between 0 and 1.";
                return false;
            }

            sourceTriangleArea = TriangleArea(sourcePoints);
            destinationTriangleArea = TriangleArea(targetPoints);

            if (sourceTriangleArea <= DegenerateTriangleAreaEpsilon
                || sourceTriangleArea < property.MinimumSourceTriangleArea)
            {
                errorCode = VisionToolErrorCode.AffineDegenerateSource;
                message = $"Affine source triangle area is below the configured minimum. Area={sourceTriangleArea:0.######}, Minimum={property.MinimumSourceTriangleArea:0.######}.";
                return false;
            }

            if (destinationTriangleArea <= DegenerateTriangleAreaEpsilon
                || destinationTriangleArea < property.MinimumDestinationTriangleArea)
            {
                errorCode = VisionToolErrorCode.AffineDegenerateDestination;
                message = $"Affine destination triangle area is below the configured minimum. Area={destinationTriangleArea:0.######}, Minimum={property.MinimumDestinationTriangleArea:0.######}.";
                return false;
            }

            if (property.OutputWidth < 0
                || property.OutputHeight < 0
                || property.OutputWidth > MaximumOutputDimension
                || property.OutputHeight > MaximumOutputDimension)
            {
                errorCode = VisionToolErrorCode.AffineInvalidOutputSize;
                message = $"Affine output dimensions must be 0 or between 1 and {MaximumOutputDimension}. Width={property.OutputWidth}, Height={property.OutputHeight}.";
                return false;
            }

            if (!IsSupportedInterpolation(property.Interpolation)
                || !IsSupportedBorderType(property.BorderType))
            {
                errorCode = VisionToolErrorCode.AffineInvalidSampling;
                message = $"Affine interpolation or border policy is not supported. Interpolation={property.Interpolation}, BorderType={property.BorderType}.";
                return false;
            }

            errorCode = VisionToolErrorCode.None;
            message = string.Empty;
            return true;
        }

        protected override bool TryValidateAfterRun(out VisionToolErrorCode errorCode, out string message)
        {
            if (validPixelRatio + 1e-12d < property.MinimumValidPixelRatio)
            {
                errorCode = VisionToolErrorCode.AffineInsufficientCoverage;
                message = $"Affine valid-pixel ratio is below the configured minimum. Ratio={validPixelRatio:0.######}, Minimum={property.MinimumValidPixelRatio:0.######}.";
                return false;
            }

            errorCode = VisionToolErrorCode.None;
            message = string.Empty;
            return true;
        }

        public override void Run()
        {
            if (property == null)
            {
                throw new InvalidOperationException("AffineTransform property is not configured.");
            }

            Point2f[] sourcePoints =
            {
                new Point2f((float)property.SourcePoint1X, (float)property.SourcePoint1Y),
                new Point2f((float)property.SourcePoint2X, (float)property.SourcePoint2Y),
                new Point2f((float)property.SourcePoint3X, (float)property.SourcePoint3Y)
            };
            destinationPoints = new[]
            {
                new Point2f((float)property.DestinationPoint1X, (float)property.DestinationPoint1Y),
                new Point2f((float)property.DestinationPoint2X, (float)property.DestinationPoint2Y),
                new Point2f((float)property.DestinationPoint3X, (float)property.DestinationPoint3Y)
            };

            int outputWidth = property.OutputWidth == 0 ? imageSource.Width : property.OutputWidth;
            int outputHeight = property.OutputHeight == 0 ? imageSource.Height : property.OutputHeight;
            OpenCvSharp.Size outputSize = new OpenCvSharp.Size(outputWidth, outputHeight);

            using (Mat matrix = Cv2.GetAffineTransform(sourcePoints, destinationPoints))
            {
                ReadMatrix(matrix, matrixValues);

                Mat transformed = new Mat();
                Cv2.WarpAffine(
                    imageSource,
                    transformed,
                    matrix,
                    outputSize,
                    property.Interpolation,
                    property.BorderType,
                    Scalar.All(property.BorderValue));
                ReplaceResultImage(transformed);

                using (Mat sourceMask = new Mat(imageSource.Size(), MatType.CV_8UC1, Scalar.All(255d)))
                using (Mat transformedMask = new Mat())
                {
                    Cv2.WarpAffine(
                        sourceMask,
                        transformedMask,
                        matrix,
                        outputSize,
                        InterpolationFlags.Nearest,
                        BorderTypes.Constant,
                        Scalar.All(0d));
                    validPixelRatio = (double)Cv2.CountNonZero(transformedMask) / (outputWidth * (double)outputHeight);
                }

                transformedFramePoints = TransformPoints(
                    matrixValues,
                    new[]
                    {
                        new Point2f(0f, 0f),
                        new Point2f(imageSource.Width - 1f, 0f),
                        new Point2f(imageSource.Width - 1f, imageSource.Height - 1f),
                        new Point2f(0f, imageSource.Height - 1f)
                    });
            }
        }

        protected override IDictionary<string, double> CollectMetrics()
        {
            IDictionary<string, double> metrics = base.CollectMetrics();
            double a = matrixValues[0];
            double b = matrixValues[1];
            double c = matrixValues[3];
            double d = matrixValues[4];
            double scaleX = Math.Sqrt((a * a) + (c * c));
            double scaleY = Math.Sqrt((b * b) + (d * d));
            double shearCosine = scaleX > 0d && scaleY > 0d
                ? ((a * b) + (c * d)) / (scaleX * scaleY)
                : 0d;

            metrics["AffineM11"] = a;
            metrics["AffineM12"] = b;
            metrics["AffineM13"] = matrixValues[2];
            metrics["AffineM21"] = c;
            metrics["AffineM22"] = d;
            metrics["AffineM23"] = matrixValues[5];
            metrics["AffineDeterminant"] = (a * d) - (b * c);
            metrics["AffineScaleX"] = scaleX;
            metrics["AffineScaleY"] = scaleY;
            metrics["AffineRotationDeg"] = Math.Atan2(c, a) * 180d / Math.PI;
            metrics["AffineShearCosine"] = shearCosine;
            metrics["AffineTranslationX"] = matrixValues[2];
            metrics["AffineTranslationY"] = matrixValues[5];
            metrics["AffineSourceTriangleArea"] = sourceTriangleArea;
            metrics["AffineDestinationTriangleArea"] = destinationTriangleArea;
            metrics["AffineValidPixelRatio"] = validPixelRatio;
            return metrics;
        }

        protected override IEnumerable<VisionToolOverlay> CollectOverlays()
        {
            List<VisionToolOverlay> overlays = new List<VisionToolOverlay>(base.CollectOverlays());

            for (int index = 0; index < destinationPoints.Length; index++)
            {
                overlays.Add(new VisionToolOverlay
                {
                    Kind = VisionToolOverlayKind.Point,
                    Label = "Affine Destination " + (index + 1),
                    Center = ToPointF(destinationPoints[index])
                });
                overlays.Add(CreateLine(
                    "Affine Destination Triangle",
                    destinationPoints[index],
                    destinationPoints[(index + 1) % destinationPoints.Length]));
            }

            for (int index = 0; index < transformedFramePoints.Length; index++)
            {
                overlays.Add(CreateLine(
                    "Affine Source Frame",
                    transformedFramePoints[index],
                    transformedFramePoints[(index + 1) % transformedFramePoints.Length]));
            }

            return overlays;
        }

        private static VisionToolOverlay CreateLine(string label, Point2f start, Point2f end)
        {
            return new VisionToolOverlay
            {
                Kind = VisionToolOverlayKind.Line,
                Label = label,
                Start = ToPointF(start),
                End = ToPointF(end)
            };
        }

        private static PointF ToPointF(Point2f point) => new PointF(point.X, point.Y);

        private static bool TryCreatePoints(
            double x1,
            double y1,
            double x2,
            double y2,
            double x3,
            double y3,
            out Point2f[] points)
        {
            points = Array.Empty<Point2f>();
            if (!IsFinite(x1) || !IsFinite(y1)
                || !IsFinite(x2) || !IsFinite(y2)
                || !IsFinite(x3) || !IsFinite(y3))
            {
                return false;
            }

            points = new[]
            {
                new Point2f((float)x1, (float)y1),
                new Point2f((float)x2, (float)y2),
                new Point2f((float)x3, (float)y3)
            };
            return true;
        }

        private static double TriangleArea(IReadOnlyList<Point2f> points)
        {
            return Math.Abs(
                ((points[1].X - points[0].X) * (points[2].Y - points[0].Y))
                - ((points[1].Y - points[0].Y) * (points[2].X - points[0].X))) * 0.5d;
        }

        private static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);

        private static bool IsSupportedInterpolation(InterpolationFlags interpolation)
        {
            return interpolation == InterpolationFlags.Nearest
                || interpolation == InterpolationFlags.Linear
                || interpolation == InterpolationFlags.Cubic
                || interpolation == InterpolationFlags.Lanczos4;
        }

        private static bool IsSupportedBorderType(BorderTypes borderType)
        {
            return borderType == BorderTypes.Constant
                || borderType == BorderTypes.Replicate
                || borderType == BorderTypes.Reflect
                || borderType == BorderTypes.Wrap
                || borderType == BorderTypes.Reflect101;
        }

        private static void ReadMatrix(Mat matrix, double[] values)
        {
            for (int row = 0; row < 2; row++)
            {
                for (int column = 0; column < 3; column++)
                {
                    values[(row * 3) + column] = matrix.At<double>(row, column);
                }
            }
        }

        private static Point2f[] TransformPoints(IReadOnlyList<double> matrix, IReadOnlyList<Point2f> points)
        {
            Point2f[] transformed = new Point2f[points.Count];
            for (int index = 0; index < points.Count; index++)
            {
                Point2f point = points[index];
                transformed[index] = new Point2f(
                    (float)((matrix[0] * point.X) + (matrix[1] * point.Y) + matrix[2]),
                    (float)((matrix[3] * point.X) + (matrix[4] * point.Y) + matrix[5]));
            }

            return transformed;
        }
    }
}
