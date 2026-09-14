#pragma warning disable CS0618

using OpenVisionLab.Core;
using OpenVisionLab.Core.Geometry2D;
using OpenVisionLab.Vision2D;
using OpenVisionLab.Vision2D.Blob;
using OpenVisionLab.Vision2D.Property;
using OpenVisionLab.Vision2D.Result;
using OpenVisionLab.Vision2D.Tool;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using DrawingPoint = System.Drawing.Point;
using DrawingPointF = System.Drawing.PointF;
using DrawingRectangle = System.Drawing.Rectangle;
using LegacyProjectionDirection = OpenVisionLab.Core.CFormula.PROJECTION_DIR;
using LegacyProjectionPolarity = OpenVisionLab.Core.CFormula.PROJECTION_POLARITY;
using ModernProjectionDirection = OpenVisionLab.Core.FormulaUtil.PROJECTION_DIR;
using ModernProjectionPolarity = OpenVisionLab.Core.FormulaUtil.PROJECTION_POLARITY;
using static OpenVisionLab.Inspection.Smoke.SmokeAssert;

namespace OpenVisionLab.Inspection.Smoke
{
    internal static class LegacyApiCompatibilitySmokeSuite
    {
        internal static IEnumerable<SmokeCase> Cases()
        {
            yield return new SmokeCase("Legacy Core conversion and formula results match modern APIs", TestCoreConversionAndFormulaParity);
            yield return new SmokeCase("Core coordinate strings remain culture invariant", TestCoreCoordinateStringCultureInvariance);
            yield return new SmokeCase("Legacy Core fitting and vertical geometry match modern APIs", TestCoreFittingAndVerticalParity);
            yield return new SmokeCase("Modern Core fitting remains stable for large coordinates and angles", TestModernCoreNumericalStability);
            yield return new SmokeCase("Modern Core fitting covers overloads and invalid geometry", TestModernCoreFittingBoundaries);
            yield return new SmokeCase("Legacy OpenCV helper and base preserve supported state parity", TestOpenCvHelperAndBaseParity);
            yield return new SmokeCase("Legacy OpenCV null handling divergence remains explicit", TestOpenCvHelperNullDivergence);
            yield return new SmokeCase("Legacy result DTOs preserve modern shared fields", TestResultDtoParity);
            yield return new SmokeCase("Legacy Blob execution matches BlobTool", TestBlobToolParity);
            yield return new SmokeCase("Legacy Contour execution matches ContourTool", TestContourToolParity);
            yield return new SmokeCase("Legacy Corner result-list divergence remains explicit", TestCornerToolResultDivergence);
            yield return new SmokeCase("Legacy Mean execution matches MeanTool", TestMeanToolParity);
            yield return new SmokeCase("Legacy Matching rotation and synthetic search match MatchingTool", TestMatchingRotationParity);
            yield return new SmokeCase("Legacy SIFT native failure and modern fallback remain explicit", TestSiftCompatibility);
            yield return new SmokeCase("Legacy LineGuage execution matches LineGaugeTool shared results", TestLineGaugeToolParity);
        }

        private static void TestCoreConversionAndFormulaParity()
        {
            Rect roi = new Rect(11, 13, 17, 19);
            DrawingPoint point = new DrawingPoint(23, 29);
            Require(CConverter.RoiToString(roi) == CommonConverter.RoiToString(roi),
                "ROI string conversion changed during the Core rename.");
            Require(CConverter.PointToString(point) == CommonConverter.PointToString(point),
                "Point string conversion changed during the Core rename.");
            Require(CConverter.StringToCVRect(CConverter.RoiToString(roi))
                == CommonConverter.StringToCVRect(CommonConverter.RoiToString(roi)),
                "ROI round trip changed during the Core rename.");

            Point lineStart = new Point(0, 10);
            Point lineEnd = new Point(100, 10);
            Point verticalStart = new Point(40, 0);
            Point verticalEnd = new Point(40, 100);
            Require(CFormula.CrossCheck(lineStart, lineEnd, verticalStart, verticalEnd)
                == FormulaUtil.CrossCheck(lineStart, lineEnd, verticalStart, verticalEnd),
                "CrossCheck changed during the Core rename.");
            RequireApproximately(
                CFormula.Angle(lineStart, lineEnd),
                FormulaUtil.Angle(lineStart, lineEnd),
                0d,
                "Line angle changed during the Core rename.");

            CLine legacyLine = new CLine(lineStart, lineEnd);
            CLine legacyVertical = new CLine(verticalStart, verticalEnd);
            LineSegment2D modernLine = new LineSegment2D(lineStart, lineEnd);
            LineSegment2D modernVertical = new LineSegment2D(verticalStart, verticalEnd);
            RequireApproximately(legacyLine.Distance(), modernLine.Distance(), 0d,
                "Line distance changed during the Core rename.");

            bool legacyFound = CFormula.FindIntersection(legacyLine, legacyVertical, out Point legacyIntersection);
            bool modernFound = FormulaUtil.FindIntersection(modernLine, modernVertical, out Point modernIntersection);
            Require(legacyFound == modernFound && legacyIntersection == modernIntersection,
                "Line intersection changed during the Core rename.");
        }

        private static void TestCoreCoordinateStringCultureInvariance()
        {
            CultureInfo originalCulture = CultureInfo.CurrentCulture;
            Rect cvRect = new Rect(-11, 13, 17, 19);
            DrawingRectangle rectangle = new DrawingRectangle(-11, 13, 17, 19);
            DrawingPoint drawingPoint = new DrawingPoint(-23, 29);
            DrawingPointF drawingPointF = new DrawingPointF(1.5f, -2.25f);
            Point cvPoint = new Point(-23, 29);

            try
            {
                foreach (string cultureName in new[] { "en-US", "de-DE", "fr-FR", "ko-KR" })
                {
                    CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(cultureName);

                    Require(CommonConverter.RoiToString(cvRect) == "-11,13,17,19", "Modern OpenCV ROI serialization depends on the current culture.");
                    Require(CommonConverter.RoiToString(rectangle) == "-11,13,17,19", "Modern drawing ROI serialization depends on the current culture.");
                    Require(CommonConverter.RectToString(rectangle) == "-11,13,17,19", "Modern rectangle serialization depends on the current culture.");
                    Require(CommonConverter.PointToString(drawingPoint) == "-23,29", "Modern drawing-point serialization depends on the current culture.");
                    Require(CommonConverter.CVPointToString(cvPoint) == "-23,29", "Modern OpenCV-point serialization depends on the current culture.");
                    Require(CommonConverter.PointFToString(drawingPointF) == "1.5,-2.25", "Modern floating-point serialization depends on the current culture.");
                    Require(CommonConverter.StringToRectangle("-11,13,17,19") == rectangle, "Modern drawing ROI parsing depends on the current culture.");
                    Require(CommonConverter.StringToRect("-11,13,17,19") == cvRect, "Modern OpenCV ROI parsing depends on the current culture.");
                    Require(CommonConverter.StringToCVRect("-11,13,17,19") == cvRect, "Modern OpenCV rectangle parsing depends on the current culture.");
                    Require(CommonConverter.StringToPoint("-23,29") == drawingPoint, "Modern drawing-point parsing depends on the current culture.");
                    Require(CommonConverter.StringToCVPoint("-23,29") == cvPoint, "Modern OpenCV-point parsing depends on the current culture.");
                    Require(CommonConverter.StringToPointF("1.5,-2.25") == drawingPointF, "Modern floating-point parsing depends on the current culture.");

                    Require(CConverter.RoiToString(cvRect) == "-11,13,17,19", "Legacy OpenCV ROI serialization depends on the current culture.");
                    Require(CConverter.RoiToString(rectangle) == "-11,13,17,19", "Legacy drawing ROI serialization depends on the current culture.");
                    Require(CConverter.RectToString(rectangle) == "-11,13,17,19", "Legacy rectangle serialization depends on the current culture.");
                    Require(CConverter.PointToString(drawingPoint) == "-23,29", "Legacy drawing-point serialization depends on the current culture.");
                    Require(CConverter.CVPointToString(cvPoint) == "-23,29", "Legacy OpenCV-point serialization depends on the current culture.");
                    Require(CConverter.PointFToString(drawingPointF) == "1.5,-2.25", "Legacy floating-point serialization depends on the current culture.");
                    Require(CConverter.StringToRectangle("-11,13,17,19") == rectangle, "Legacy drawing ROI parsing depends on the current culture.");
                    Require(CConverter.StringToRect("-11,13,17,19") == cvRect, "Legacy OpenCV ROI parsing depends on the current culture.");
                    Require(CConverter.StringToCVRect("-11,13,17,19") == cvRect, "Legacy OpenCV rectangle parsing depends on the current culture.");
                    Require(CConverter.StringToPoint("-23,29") == drawingPoint, "Legacy drawing-point parsing depends on the current culture.");
                    Require(CConverter.StringToCVPoint("-23,29") == cvPoint, "Legacy OpenCV-point parsing depends on the current culture.");
                    Require(CConverter.StringToPointF("1.5,-2.25") == drawingPointF, "Legacy floating-point parsing depends on the current culture.");
                }
            }
            finally
            {
                CultureInfo.CurrentCulture = originalCulture;
            }

            Require(CommonConverter.StringToPointF("1,5,-2,25") == DrawingPointF.Empty, "Modern malformed token-count fallback changed.");
            Require(CConverter.StringToPointF("1,5,-2,25") == DrawingPointF.Empty, "Legacy malformed token-count fallback changed.");
            RequireThrows<FormatException>(() => CommonConverter.StringToPointF("invalid,2"));
            RequireThrows<FormatException>(() => CConverter.StringToPointF("invalid,2"));
            Require(CommonConverter.IntToByte(255) == byte.MaxValue, "Modern byte conversion changed.");
            Require(CConverter.IntToByte(255) == byte.MaxValue, "Legacy byte conversion changed.");
            RequireThrows<OverflowException>(() => CommonConverter.IntToByte(256));
            RequireThrows<OverflowException>(() => CConverter.IntToByte(256));
        }

        private static void TestCoreFittingAndVerticalParity()
        {
            List<DrawingPointF> fitPoints = new List<DrawingPointF>
            {
                new DrawingPointF(0f, 3f),
                new DrawingPointF(10f, 23f),
                new DrawingPointF(20f, 43f),
                new DrawingPointF(30f, 63f)
            };
            CLineCalculatorFitting legacyCalculator = new CLineCalculatorFitting();
            LineFittingCalculator modernCalculator = new LineFittingCalculator();
            (DrawingPointF legacyFitStart, DrawingPointF legacyFitEnd) = legacyCalculator.LineFit(fitPoints);
            (DrawingPointF modernFitStart, DrawingPointF modernFitEnd) = modernCalculator.LineFit(fitPoints);
            Require(legacyFitStart == modernFitStart && legacyFitEnd == modernFitEnd,
                "Least-squares line endpoints changed during the Core rename.");
            RequireApproximately(legacyCalculator.Slope, modernCalculator.Slope, 0d,
                "Least-squares slope changed during the Core rename.");
            RequireApproximately(legacyCalculator.Intercept, modernCalculator.Intercept, 0d,
                "Least-squares intercept changed during the Core rename.");

            List<Point> edges = fitPoints.Select(point => new Point(point.X, point.Y)).ToList();
            CLine legacyFit = CLineFitting.GetFitLine(edges, LegacyProjectionDirection.X_LTOR);
            LineSegment2D modernFit = LineFitting.GetFitLine(edges, ModernProjectionDirection.X_LTOR);
            Require(legacyFit.Start == modernFit.Start && legacyFit.End == modernFit.End,
                "Fitted line changed during the Core rename.");

            CLine emptyLegacyFit = CLineFitting.GetFitLine(new List<Point>(), LegacyProjectionDirection.X_LTOR);
            LineSegment2D emptyModernFit = LineFitting.GetFitLine(new List<Point>(), ModernProjectionDirection.X_LTOR);
            Require(emptyLegacyFit.Start == emptyModernFit.Start && emptyLegacyFit.End == emptyModernFit.End,
                "Empty-edge fitting fallback changed during the Core rename.");

            Point horizontalStart = new Point(10, 10);
            Point horizontalEnd = new Point(90, 10);
            Point basePoint = new Point(50, 50);
            Point imageSize = new Point(100, 100);
            CLineVertical.GetLineCoef(horizontalStart, horizontalEnd, basePoint, imageSize, out List<Point> legacyCandidates);
            VerticalLineCalculator.GetLineCoef(horizontalStart, horizontalEnd, basePoint, imageSize, out List<Point> modernCandidates);
            Require(legacyCandidates.SequenceEqual(modernCandidates),
                "Vertical line candidates changed during the Core rename.");
        }

        private static void TestModernCoreNumericalStability()
        {
            DrawingPointF[] translated =
            {
                new DrawingPointF(100000f, 200003f),
                new DrawingPointF(100001f, 200005f),
                new DrawingPointF(100002f, 200007f)
            };
            double error = LineFittingCalculator.FindLinearLeastSquaresFit(translated, out double slope, out double intercept);

            RequireApproximately(slope, 2.0, 1e-9, "Large-coordinate line fitting lost the expected slope.");
            RequireApproximately(intercept, 3.0, 1e-6, "Large-coordinate line fitting lost the expected intercept.");
            RequireApproximately(error, 0.0, 1e-6, "Large-coordinate line fitting produced residual error for an exact line.");

            double angle = FormulaUtil.threePointAngle(
                new Point(0, 0),
                new Point(50000, 0),
                new Point(50000, 0));
            RequireApproximately(angle, 0.0, 1e-12,
                "Same-direction vectors must produce a zero three-point angle.");

            bool degenerateRejected = false;
            try
            {
                LineFittingCalculator.FindLinearLeastSquaresFit(
                    new[]
                    {
                        new DrawingPointF(1f, 0f),
                        new DrawingPointF(1f, 1f),
                        new DrawingPointF(1f, 2f)
                    },
                    out _,
                    out _);
            }
            catch (ArgumentException exception)
            {
                degenerateRejected = exception.Message.Contains("distinct", StringComparison.OrdinalIgnoreCase);
            }

            Require(degenerateRejected,
                "A vertical y=f(x) fit must reject its zero-variance X coordinates explicitly.");
        }

        private static void TestModernCoreFittingBoundaries()
        {
            LineFittingCalculator calculator = new LineFittingCalculator();
            DrawingPointF[] noisy = { new DrawingPointF(0, 1), new DrawingPointF(1, 1), new DrawingPointF(2, 3) };
            double error = LineFittingCalculator.FindLinearLeastSquaresFit(noisy, out double slope, out double intercept);
            RequireApproximately(slope, 1.0, 1e-12, "Noisy fit slope changed.");
            RequireApproximately(intercept, 2.0 / 3.0, 1e-12, "Noisy fit intercept changed.");
            RequireApproximately(error, Math.Sqrt(2.0 / 3.0), 1e-12, "Fit error must be root sum of squared vertical residuals, not RMS.");
            RequireApproximately(LineFittingCalculator.ErrorSquared(noisy, slope, intercept), error * error, 1e-12, "Squared error disagrees with the returned norm.");

            var floatFit = calculator.LineFit(new[] { new DrawingPointF(2, 7), new DrawingPointF(0, 3) });
            var integerFit = calculator.LineFit(new[] { new DrawingPoint(2, 7), new DrawingPoint(0, 3) });
            Require(floatFit == integerFit && floatFit.Item1 == new DrawingPointF(0, 3) && floatFit.Item2 == new DrawingPointF(2, 7),
                "Drawing-point overloads must return endpoints in increasing X order.");

            int enumerationCount = 0;
            IEnumerable<Point> EnumerateOnce()
            {
                Require(++enumerationCount == 1, "Line fitting enumerated the input more than once.");
                yield return new Point(1000000000, 1000000003);
                yield return new Point(1000000001, 1000000004);
                yield return new Point(1000000002, 1000000005);
            }
            var largeFit = calculator.LineFitX(EnumerateOnce());
            Require(largeFit.Item1 == new DrawingPoint(1000000000, 1000000003)
                && largeFit.Item2 == new DrawingPoint(1000000002, 1000000005), "Integer fit lost adjacent coordinates at a large origin.");
            var verticalFit = calculator.LineFitY(new[] { new Point(7, 2), new Point(7, -2) });
            Require(verticalFit.Item1 == new DrawingPoint(7, -2) && verticalFit.Item2 == new DrawingPoint(7, 2),
                "LineFitY must fit x=f(y), including a vertical line.");
            var extended = calculator.LinearLeastSquaresFit(new List<Point> { new Point(0, 3), new Point(2, 7) }, new System.Drawing.Size(10, 20));
            Require(extended.Item1 == new DrawingPoint(0, 3) && extended.Item2 == new DrawingPoint(10, 23),
                "Compatibility extent overload must extrapolate to Width without clipping to Height.");

            RequireThrows<ArgumentNullException>(() => calculator.LineFit((IEnumerable<DrawingPointF>)null));
            RequireThrows<ArgumentException>(() => calculator.LineFit(Array.Empty<DrawingPointF>()));
            RequireThrows<ArgumentException>(() => calculator.LineFit(new[] { new DrawingPointF(1, 2) }));
            RequireThrows<ArgumentException>(() => calculator.LineFitX(new[] { new Point(1, 0), new Point(1, 2) }));
            RequireThrows<ArgumentException>(() => calculator.LineFitY(new[] { new Point(0, 1), new Point(2, 1) }));
            foreach (float invalid in new[] { float.NaN, float.PositiveInfinity, float.NegativeInfinity })
            {
                RequireThrows<ArgumentException>(() => calculator.LineFit(new[] { new DrawingPointF(invalid, 0), new DrawingPointF(1, 2) }));
                RequireThrows<ArgumentException>(() => calculator.LineFit(new[] { new DrawingPointF(0, invalid), new DrawingPointF(1, 2) }));
            }

            Point extremeBase = new Point(int.MinValue, int.MinValue);
            RequireApproximately(FormulaUtil.threePointAngle(extremeBase, new Point(int.MaxValue, int.MinValue), new Point(int.MinValue, int.MaxValue)),
                90.0, 1e-12, "Angle differences must not overflow integer coordinates.");
            RequireApproximately(FormulaUtil.threePointAngle(new Point(10, 10), new Point(0, 10), new Point(20, 10)),
                180.0, 1e-12, "Opposite vectors must produce 180 degrees.");
            Require(double.IsNaN(FormulaUtil.threePointAngle(extremeBase, extremeBase, new Point(0, 0))),
                "A zero-length angle vector must remain explicitly undefined.");
        }

        private static void TestOpenCvHelperAndBaseParity()
        {
            using (Mat source = new Mat(new Size(24, 16), MatType.CV_8UC1, Scalar.All(37)))
            using (Mat empty = new Mat())
            {
                Require(COpenCVHelper.IsImageEmpty(source) == OpenCvHelper.IsImageEmpty(source),
                    "Valid-image state changed in OpenCvHelper.");
                Require(COpenCVHelper.IsImageEmpty(empty) == OpenCvHelper.IsImageEmpty(empty),
                    "Empty-image state changed in OpenCvHelper.");
                Require(COpenCVHelper.IsRectEmpty(new Rect()) == OpenCvHelper.IsRectEmpty(new Rect()),
                    "Empty-rectangle state changed in OpenCvHelper.");

                LegacyNoOpTool legacy = new LegacyNoOpTool();
                using (ModernNoOpTool modern = new ModernNoOpTool())
                {
                    try
                    {
                        legacy.SetSourceImage(source);
                        modern.SetSourceImage(source);
                        legacy.Run();
                        modern.Run();
                        Require(legacy.size == modern.size,
                            "Base tool source size changed during the base-class rename.");
                        Require(Cv2.Norm(legacy.imageSource, modern.imageSource, NormTypes.L1) == 0d,
                            "Base tool source copy changed during the base-class rename.");
                        Require(Cv2.Norm(legacy.imageResult, modern.imageResult, NormTypes.L1) == 0d,
                            "Base tool result state changed during the base-class rename.");
                    }
                    finally
                    {
                        DisposeLegacyTool(legacy);
                    }
                }
            }
        }

        private static void TestOpenCvHelperNullDivergence()
        {
            TextWriter originalError = Console.Error;
            bool legacyResult;
            using (StringWriter legacyError = new StringWriter())
            {
                Console.SetError(legacyError);
                try
                {
                    legacyResult = COpenCVHelper.IsMatEmpty(null);
                }
                finally
                {
                    Console.SetError(originalError);
                }

                Require(!legacyResult
                    && legacyError.ToString().Contains("[FAILED]", StringComparison.OrdinalIgnoreCase),
                    "COpenCVHelper null behavior changed; review the documented migration asymmetry.");
            }

            bool modernThrows = false;
            try
            {
                OpenCvHelper.IsMatEmpty(null);
            }
            catch (NullReferenceException)
            {
                modernThrows = true;
            }

            Require(modernThrows,
                "OpenCvHelper null behavior changed; review the documented migration asymmetry.");
        }

        private static void TestResultDtoParity()
        {
            Point2d center = new Point2d(12.345, 67.891);
            Rect bounds = new Rect(3, 5, 17, 19);
            CResultBlob legacyBlob = new CResultBlob(2, 323d, center, bounds, 7.654d);
            BlobResult modernBlob = new BlobResult(2, 323d, center, bounds, 7.654d);
            Require(legacyBlob.Index == modernBlob.Index
                && legacyBlob.Area == modernBlob.Area
                && legacyBlob.Center == modernBlob.Center
                && legacyBlob.Bounding == modernBlob.Bounding
                && legacyBlob.Angle == modernBlob.Angle,
                "Blob result shared fields changed.");

            Point[] contour = { new Point(3, 5), new Point(20, 5), new Point(20, 24), new Point(3, 24) };
            CResultContour legacyContour = new CResultContour(4, 323d, center, bounds, contour, 8d);
            ContourResult modernContour = new ContourResult(4, 323d, center, bounds, contour, 8d);
            Require(legacyContour.Index == modernContour.Index
                && legacyContour.Area == modernContour.Area
                && legacyContour.Center == modernContour.Center
                && legacyContour.Bounding == modernContour.Bounding
                && legacyContour.Contours.SequenceEqual(modernContour.Contours),
                "Contour result shared fields changed.");

            CResultCorner legacyCorner = new CResultCorner(0d, center, bounds, 0d);
            CornerResult modernCorner = new CornerResult(0d, center, bounds, 0d);
            Require(legacyCorner.Center == modernCorner.Center
                && legacyCorner.Bounding == modernCorner.Bounding,
                "Corner result shared fields changed.");

            Point2f matchingCenter = new Point2f(15.5f, 16.5f);
            Rect2f matchingBounds = new Rect2f(6f, 7f, 18f, 19f);
            CResultMatching legacyMatching = new CResultMatching(5, 91d, matchingCenter, matchingBounds, 12d);
            MatchingResult modernMatching = new MatchingResult(5, 91d, matchingCenter, matchingBounds, 12d);
            Require(legacyMatching.Index == modernMatching.Index
                && legacyMatching.Score == modernMatching.Score
                && legacyMatching.Center == modernMatching.Center
                && legacyMatching.Bounding == modernMatching.Bounding
                && legacyMatching.Angle == modernMatching.Angle
                && modernMatching.Scale == 1d,
                "Matching result shared fields or default scale changed.");

            DrawingRectangle meanBounds = new DrawingRectangle(4, 6, 20, 10);
            CResultMean legacyMean = new CResultMean(6, 137.5d, meanBounds);
            MeanResult modernMean = new MeanResult(6, 137.5d, meanBounds);
            Require(legacyMean.index == modernMean.index
                && legacyMean.meanValue == modernMean.meanValue
                && legacyMean.Bounding == modernMean.Bounding
                && legacyMean.Center == modernMean.Center,
                "Mean result shared fields changed.");

            List<CVLineGuage_Edge> legacyEdges = new List<CVLineGuage_Edge>
            {
                new CVLineGuage_Edge(1, new Point(20, 10)),
                new CVLineGuage_Edge(2, new Point(20, 20))
            };
            List<LineGaugeEdge> modernEdges = new List<LineGaugeEdge>
            {
                new LineGaugeEdge(1, new Point(20, 10)),
                new LineGaugeEdge(2, new Point(20, 20))
            };
            CVLineGuage_Result legacyLineResult = new CVLineGuage_Result(
                legacyEdges,
                new CLine(new Point(20, 10), new Point(20, 20)));
            LineGaugeResult modernLineResult = new LineGaugeResult(
                modernEdges,
                new LineSegment2D(new Point(20, 10), new Point(20, 20)));
            Require(legacyLineResult.edgeList.SequenceEqual(modernLineResult.edgeList)
                && legacyLineResult.FitLine.Start == modernLineResult.FitLine.Start
                && legacyLineResult.FitLine.End == modernLineResult.FitLine.End
                && modernLineResult.EdgeCount == legacyLineResult.Results_List.Count,
                "Line Gauge result shared fields changed.");

            CVLineGuage_VerticalLines legacyVertical = new CVLineGuage_VerticalLines
            {
                index = 3,
                intersectionLengths = new List<double> { 4d },
                cLines = new List<CLine> { new CLine(new Point(1, 2), new Point(3, 4)) }
            };
            LineGaugeVerticalLines modernVertical = new LineGaugeVerticalLines
            {
                index = 3,
                intersectionLengths = new List<double> { 4d },
                cLines = new List<LineSegment2D> { new LineSegment2D(new Point(1, 2), new Point(3, 4)) }
            };
            Require(legacyVertical.index == modernVertical.index
                && legacyVertical.intersectionLengths.SequenceEqual(modernVertical.intersectionLengths)
                && legacyVertical.cLines[0].Start == modernVertical.cLines[0].Start
                && legacyVertical.cLines[0].End == modernVertical.cLines[0].End,
                "Line Gauge vertical-line shared fields changed.");
        }

        private static void TestBlobToolParity()
        {
            using (Mat source = CreateBinaryShapeSource())
            using (BlobTool modern = new BlobTool())
            {
                CVBlob legacy = new CVBlob();
                try
                {
                    legacy.SetProperty(CreateBlobProperty());
                    modern.SetProperty(CreateBlobProperty());
                    legacy.SetSourceImage(source);
                    modern.SetSourceImage(source);
                    legacy.Run();
                    modern.Run();

                    List<CResultBlob> legacyResults = legacy.results.OrderBy(result => result.Bounding.X).ToList();
                    List<BlobResult> modernResults = modern.results.OrderBy(result => result.Bounding.X).ToList();
                    Require(legacyResults.Count == modernResults.Count && legacyResults.Count == 1,
                        "Blob result count changed between legacy and modern tools.");
                    RequireApproximately(legacyResults[0].Area, modernResults[0].Area, 0d,
                        "Blob area changed between legacy and modern tools.");
                    Require(legacyResults[0].Center == modernResults[0].Center
                        && legacyResults[0].Bounding == modernResults[0].Bounding,
                        "Blob geometry changed between legacy and modern tools.");
                }
                finally
                {
                    DisposeLegacyTool(legacy);
                }
            }
        }

        private static void TestContourToolParity()
        {
            using (Mat source = CreateBinaryShapeSource())
            using (ContourTool modern = new ContourTool())
            {
                CVContour legacy = new CVContour();
                try
                {
                    legacy.SetProperty(CreateContourProperty());
                    modern.SetProperty(CreateContourProperty());
                    legacy.SetSourceImage(source);
                    modern.SetSourceImage(source);
                    legacy.Run();
                    modern.Run();
                    Require(legacy.results.Count == modern.results.Count && legacy.results.Count == 1,
                        "Contour result count changed between legacy and modern tools.");
                    RequireApproximately(legacy.results[0].Area, modern.results[0].Area, 0d,
                        "Contour area changed between legacy and modern tools.");
                    Require(legacy.results[0].Center == modern.results[0].Center
                        && legacy.results[0].Bounding == modern.results[0].Bounding
                        && legacy.results[0].Contours.SequenceEqual(modern.results[0].Contours),
                        "Contour geometry changed between legacy and modern tools.");
                }
                finally
                {
                    DisposeLegacyTool(legacy);
                }
            }
        }

        private static void TestCornerToolResultDivergence()
        {
            using (Mat source = CreateBinaryShapeSource())
            using (CornerTool modern = new CornerTool())
            {
                CVCorner legacy = new CVCorner();
                try
                {
                    legacy.SetProperty(CreateContourProperty());
                    modern.SetProperty(CreateContourProperty());
                    legacy.SetSourceImage(source);
                    modern.SetSourceImage(source);
                    legacy.Run();
                    modern.Run();
                    Require(legacy.results.Count == 0,
                        "Legacy CVCorner unexpectedly began publishing result DTOs; review the migration contract.");
                    Require(modern.results.Count >= 4,
                        "CornerTool must publish detected corner DTOs for the synthetic rectangle.");
                    Require(!COpenCVHelper.IsImageEmpty(legacy.imageResult)
                        && !OpenCvHelper.IsImageEmpty(modern.imageResult),
                        "Both corner implementations must retain their rendered result image.");
                }
                finally
                {
                    DisposeLegacyTool(legacy);
                }
            }
        }

        private static void TestMeanToolParity()
        {
            CultureInfo originalCulture = CultureInfo.CurrentCulture;
            using (Mat source = new Mat(new Size(64, 48), MatType.CV_8UC1, Scalar.All(100)))
            using (MeanTool modern = new MeanTool())
            {
                Cv2.Rectangle(
                    source,
                    new Rect(0, 0, 32, 48),
                    Scalar.All(150),
                    Cv2.FILLED);

                try
                {
                    CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-DE");
                    foreach (bool useMultipleRois in new[] { false, true })
                    {
                        MeanToolProperty legacyProperty = CreateMeanProperty();
                        MeanToolProperty modernProperty = CreateMeanProperty();
                        legacyProperty.MEAN_TYPES = MeanType.MeanStdDev;
                        modernProperty.MEAN_TYPES = MeanType.MeanStdDev;
                        legacyProperty.USE_MULTI_ROI = useMultipleRois;
                        modernProperty.USE_MULTI_ROI = useMultipleRois;
                        if (useMultipleRois)
                        {
                            legacyProperty.CvROIS.Add(legacyProperty.CvROI);
                            modernProperty.CvROIS.Add(modernProperty.CvROI);
                        }

                        CVMean legacy = new CVMean();
                        try
                        {
                            legacy.SetProperty(legacyProperty);
                            modern.SetProperty(modernProperty);
                            legacy.SetSourceImage(source);
                            legacy.Run();
                            using (VisionToolResult modernResult = modern.Execute(source))
                            {
                                Require(modernResult.Success && legacy.results.Count == 1 && modern.results.Count == 1,
                                    "Mean result count or execution status changed under de-DE.");
                                RequireApproximately(legacy.results[0].meanValue, 21.7, 0d,
                                    "Legacy standard deviation depends on the current culture.");
                                RequireApproximately(legacy.results[0].meanValue, modern.results[0].meanValue, 0d,
                                    "Mean value changed between legacy and modern tools.");
                                Require(legacy.results[0].Bounding == modern.results[0].Bounding
                                    && legacy.results[0].Center == modern.results[0].Center,
                                    "Mean result geometry changed between legacy and modern tools.");
                                Require(modernResult.Metrics.TryGetValue("MeanValueAvg", out double metric)
                                    && metric == 21.7
                                    && modernResult.Overlays.Count == 1
                                    && modernResult.Overlays[0].Label.StartsWith("#0 ", StringComparison.Ordinal),
                                    "Mean metric or overlay numeric extraction changed under de-DE.");
                            }
                        }
                        finally
                        {
                            DisposeLegacyTool(legacy);
                        }
                    }
                }
                finally
                {
                    CultureInfo.CurrentCulture = originalCulture;
                }
            }
        }

        private static void TestMatchingRotationParity()
        {
            using (Mat source = CreateFeaturePattern(48, 48))
            using (Mat template = source.SubMat(new Rect(6, 6, 30, 28)).Clone())
            using (MatchingTool modern = new MatchingTool())
            {
                CVMatching legacy = new CVMatching();
                try
                {
                    legacy.SetProperty(CreateMatchingProperty());
                    modern.SetProperty(CreateMatchingProperty());
                    using (Mat legacyRotated = legacy.Rotate(source, 17d, false))
                    using (Mat modernRotated = modern.Rotate(source, 17d, false))
                    {
                        Require(legacyRotated.Size() == modernRotated.Size()
                            && legacyRotated.Type() == modernRotated.Type(),
                            "Matching rotation output shape changed.");
                        Require(Cv2.Norm(legacyRotated, modernRotated, NormTypes.L1) == 0d,
                            "Matching rotation pixels changed.");
                    }

                    legacy.SetTemplateImage(template);
                    modern.SetTemplateImage(template);
                    legacy.SetSourceImage(source);
                    modern.SetSourceImage(source);
                    legacy.Run();
                    modern.Run();
                    Require(legacy.results.Count > 0 && modern.results.Count > 0,
                        "Both matching implementations must find the embedded synthetic template.");
                    RequireApproximately(legacy.results[0].Center.X, modern.results[0].Center.X, 1d,
                        "Matching center X changed between legacy and modern tools.");
                    RequireApproximately(legacy.results[0].Center.Y, modern.results[0].Center.Y, 1d,
                        "Matching center Y changed between legacy and modern tools.");
                    RequireApproximately(legacy.results[0].Score, modern.results[0].Score, 0.1d,
                        "Matching score changed between legacy and modern tools.");
                }
                finally
                {
                    DisposeLegacyTool(legacy);
                }
            }
        }

        private static void TestSiftCompatibility()
        {
            CVSIFT legacy = new CVSIFT();
            using (SiftTool modern = new SiftTool())
            using (Mat blank = new Mat(new Size(64, 64), MatType.CV_8UC1, Scalar.All(0)))
            {
                try
                {
                    Point2f[] points = { new Point2f(1.25f, 2.5f), new Point2f(3.75f, 4.5f) };
                    Require(legacy.ConvertPoint2fToPoint2d(points)
                        .SequenceEqual(modern.ConvertPoint2fToPoint2d(points)),
                        "SIFT point conversion changed.");

                    legacy.SetProperty(new SiftToolProperty());
                    modern.SetProperty(new SiftToolProperty());
                    legacy.SetSourceImage(blank);
                    legacy.SetTemplateImage(blank.Clone());
                    modern.SetTemplateImage(blank);

                    TextWriter originalError = Console.Error;
                    string legacyFailure;
                    using (StringWriter legacyError = new StringWriter())
                    {
                        Console.SetError(legacyError);
                        try
                        {
                            legacy.Run();
                        }
                        finally
                        {
                            Console.SetError(originalError);
                        }

                        legacyFailure = legacyError.ToString();
                    }

                    Require(legacy.results.Count == 0
                        && legacyFailure.Contains("features2d_SIFT_create", StringComparison.OrdinalIgnoreCase),
                        "CVSIFT native entry-point failure changed; review the migration contract.");

                    VisionToolResult modernOutcome = modern.Execute(blank);
                    try
                    {
                        Require(!modernOutcome.Success
                            && modernOutcome.ErrorCode == VisionToolErrorCode.FeatureNoKeypoints,
                            "SiftTool must fall back and report FeatureNoKeypoints for a blank image.");
                        Require(modernOutcome.Metrics.ContainsKey("FeatureDetector.Sift")
                            && modernOutcome.Metrics.ContainsKey("FeatureDetector.OrbFallback")
                            && (modernOutcome.Metrics["FeatureDetector.Sift"] == 1.0
                                || modernOutcome.Metrics["FeatureDetector.OrbFallback"] == 1.0),
                            "SiftTool must report the detector selected by the native runtime.");
                    }
                    finally
                    {
                        modernOutcome.ResultImage?.Dispose();
                    }
                }
                finally
                {
                    DisposeLegacyTool(legacy);
                }
            }
        }

        private static void TestLineGaugeToolParity()
        {
            using (Mat source = new Mat(new Size(128, 64), MatType.CV_8UC1, Scalar.All(0)))
            using (LineGaugeTool modern = new LineGaugeTool())
            {
                Cv2.Rectangle(source, new Rect(64, 0, 64, 64), Scalar.All(255), Cv2.FILLED);
                CVLineGuage legacy = new CVLineGuage();
                try
                {
                    legacy.SetProperty(CreateLegacyLineGaugeProperty());
                    modern.SetProperty(CreateModernLineGaugeProperty());
                    legacy.SetSourceImage(source);
                    modern.SetSourceImage(source);
                    legacy.Run();
                    modern.Run();
                    Require(legacy.resultList.Count == modern.resultList.Count && legacy.resultList.Count == 1,
                        "Line Gauge result count changed between legacy and modern tools.");

                    List<Point> legacyEdges = legacy.resultList[0].edgeList.OrderBy(point => point.Y).ToList();
                    List<Point> modernEdges = modern.resultList[0].edgeList.OrderBy(point => point.Y).ToList();
                    Require(legacyEdges.Count > 2 && legacyEdges.SequenceEqual(modernEdges),
                        "Line Gauge edge positions changed between legacy and modern tools.");
                    Require(legacy.resultList[0].FitLine.Start == modern.resultList[0].FitLine.Start
                        && legacy.resultList[0].FitLine.End == modern.resultList[0].FitLine.End,
                        "Line Gauge fitted line changed between legacy and modern tools.");
                }
                finally
                {
                    DisposeLegacyTool(legacy);
                }
            }
        }

        private static Mat CreateBinaryShapeSource()
        {
            Mat source = new Mat(new Size(96, 80), MatType.CV_8UC1, Scalar.All(0));
            Cv2.Rectangle(source, new Rect(24, 18, 40, 32), Scalar.All(255), Cv2.FILLED);
            return source;
        }

        private static Mat CreateFeaturePattern(int width, int height)
        {
            Mat source = new Mat(new Size(width, height), MatType.CV_8UC1, Scalar.All(0));
            Cv2.Rectangle(source, new Rect(7, 9, 19, 13), Scalar.All(220), Cv2.FILLED);
            Cv2.Line(source, new Point(3, height - 7), new Point(width - 5, 4), Scalar.All(130), 2);
            Cv2.Circle(source, new Point(width - 12, height - 11), 5, Scalar.All(255), Cv2.FILLED);
            return source;
        }

        private static BlobToolProperty CreateBlobProperty()
        {
            return new BlobToolProperty
            {
                THRESHOLD = 100d,
                MIN_AREA = 50,
                MAX_AREA = 10000
            };
        }

        private static ContourToolProperty CreateContourProperty()
        {
            return new ContourToolProperty
            {
                USE_THRESHOLD = true,
                THRESHOLD = 100d,
                MIN_AREA = 50,
                MAX_AREA = 10000,
                USE_ROI = true,
                CvROI = new Rect(0, 0, 96, 80)
            };
        }

        private static MeanToolProperty CreateMeanProperty()
        {
            return new MeanToolProperty
            {
                USE_ROI = true,
                CvROI = new Rect(8, 6, 32, 24),
                MEAN_TYPES = MeanType.Mean
            };
        }

        private static MatchingToolProperty CreateMatchingProperty()
        {
            return new MatchingToolProperty
            {
                USE_FIND_ANGLE = false,
                NUM_MATCH = 1,
                SCORE_MIN = 0.5d,
                USE_ROI = true,
                CvROI = new Rect(0, 0, 48, 48)
            };
        }

        private static LegacyLineGaugeProperty CreateLegacyLineGaugeProperty()
        {
            return new LegacyLineGaugeProperty
            {
                USE_ROI = true,
                CvROI = new Rect(0, 0, 128, 64),
                PRJ_DIR = LegacyProjectionDirection.X_LTOR,
                VER_PRJ_DIR = LegacyProjectionDirection.X_LTOR,
                PRJ_PORALITY = LegacyProjectionPolarity.BTOW,
                CONTRAST = 30d,
                THICKNESS = 3d,
                SAMPLING_STEP = 8d
            };
        }

        private static LineGaugeToolProperty CreateModernLineGaugeProperty()
        {
            return new LineGaugeToolProperty
            {
                USE_ROI = true,
                CvROI = new Rect(0, 0, 128, 64),
                PRJ_DIR = ModernProjectionDirection.X_LTOR,
                VER_PRJ_DIR = ModernProjectionDirection.X_LTOR,
                PRJ_PORALITY = ModernProjectionPolarity.BTOW,
                CONTRAST = 30d,
                THICKNESS = 3d,
                SAMPLING_STEP = 8d
            };
        }

        private static void DisposeLegacyTool(COpenCVAlgorithmBase tool)
        {
            Mat source = tool.imageSource;
            Mat result = tool.imageResult;
            Mat template = tool.imageTemplate;
            source?.Dispose();
            if (!ReferenceEquals(result, source)) { result?.Dispose(); }
            if (!ReferenceEquals(template, source) && !ReferenceEquals(template, result)) { template?.Dispose(); }
        }

        private sealed class LegacyNoOpTool : COpenCVAlgorithmBase
        {
            public override void Run()
            {
                imageResult?.Dispose();
                imageResult = imageSource.Clone();
            }
        }

        private sealed class ModernNoOpTool : OpenCvAlgorithmBase
        {
            public override void Run()
            {
                imageResult?.Dispose();
                imageResult = imageSource.Clone();
            }
        }

        private sealed class LegacyLineGaugeProperty : OpenCvToolPropertyBase, IOpenCVPropertyLineGuage
        {
            internal LegacyLineGaugeProperty() : base("Legacy line gauge") { }

            public LegacyProjectionPolarity PRJ_PORALITY { get; set; } = LegacyProjectionPolarity.BTOW;
            public LegacyProjectionDirection PRJ_DIR { get; set; } = LegacyProjectionDirection.X_LTOR;
            public double CONTRAST { get; set; } = 30d;
            public double THICKNESS { get; set; } = 5d;
            public double SAMPLING_STEP { get; set; } = 10d;
            public LegacyProjectionDirection VER_PRJ_DIR { get; set; } = LegacyProjectionDirection.X_LTOR;
            public int POINT_RANGE { get; set; } = 10;
            public bool USE_MANUAL_ANGLE { get; set; }
            public double MANUAL_ANGLE_VALUE { get; set; }
            public bool USE_EXTEND_FIT_LINE { get; set; }
            public int EXTEND_FIT_LINE_VALUE { get; set; } = 100;
            public bool SHOW_VERTICAL_LINE { get; set; } = true;
            public bool SHOW_EDGE { get; set; } = true;
            public bool SHOW_CONTOUR { get; set; } = true;
            public bool SHOW_FITLINE { get; set; } = true;
        }
    }
}

#pragma warning restore CS0618
