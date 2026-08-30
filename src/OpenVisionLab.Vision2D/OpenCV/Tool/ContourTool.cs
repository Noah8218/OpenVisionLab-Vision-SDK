using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using OpenVisionLab.Core;
using OpenVisionLab.Vision2D.Property;
using OpenVisionLab.Vision2D.Result;
using OpenCvSharp;
using OpenCvSharp.Blob;

namespace OpenVisionLab.Vision2D.Tool
{
    public partial class ContourTool : OpenCvAlgorithmBase
    {
        public IOpenCVPropertyContour property;

        public List<ContourResult> results = new List<ContourResult>();

        /// <summary>All source-coordinate candidates produced by the last execution, including rejected candidates.</summary>
        public List<VisionObjectCandidate> candidates = new List<VisionObjectCandidate>();
        
        public ContourTool() { }

        public void SetProperty(IOpenCVPropertyContour propertyBase) => property = propertyBase;

        protected override bool TryValidateBeforeRun(out VisionToolErrorCode errorCode, out string message)
        {
            if (!base.TryValidateBeforeRun(out errorCode, out message))
            {
                return false;
            }

            if (!TryValidateAreaRange(
                property.MIN_AREA,
                property.MAX_AREA,
                VisionToolErrorCode.ContourInvalidAreaRange,
                "Contour",
                out errorCode,
                out message))
            {
                return false;
            }

            if (!TryValidateAdaptiveThreshold(
                property,
                VisionToolErrorCode.ContourInvalidAdaptiveBlockSize,
                out errorCode,
                out message))
            {
                return false;
            }

            if (!TryValidateRoiSet(
                property,
                property.USE_ROI,
                true,
                VisionToolErrorCode.ContourRoiInvalid,
                "Contour",
                out errorCode,
                out message))
            {
                return false;
            }

            errorCode = VisionToolErrorCode.None;
            message = string.Empty;
            return true;
        }

        protected override bool TryValidateAfterRun(out VisionToolErrorCode errorCode, out string message)
        {
            if (results == null || results.Count == 0)
            {
                errorCode = VisionToolErrorCode.ContourNoResult;
                message = $"Contour found no result. Area={property.MIN_AREA}..{property.MAX_AREA}, ROI={FormatContourRoi()}";
                return false;
            }

            errorCode = VisionToolErrorCode.None;
            message = string.Empty;
            return true;
        }

        public override void Run()
        {
            if (property.USE_MULTI_ROI)
            {
                MultiRun();
            }
            else
            {
                SingleRun();
            }
        }

        public bool SingleRun()
        {
            OpenCvSharp.Point[][] Contours;

            int MinArea = property.MIN_AREA;
            int MaxArea = property.MAX_AREA;

            results.Clear();
            candidates.Clear();

            if (OpenCvHelper.IsImageEmpty(imageSource))
            {

                return false;
            }

            Rect roi = NormalizeContourRoi(property.CvROI);

            if (property.USE_DRAW_IMAGE)
            {
                ReplaceResultImage(imageSource.Clone());
                OpenCvHelper.SetImageChannel3(imageResult);
            }

            using (Mat imageSrc = CreateWorkingContourImage(roi, property.USE_ROI))
            using (Mat contourInput = CreateFindContoursInput(imageSrc))
            {
                Contours = FindContours(
                    contourInput,
                    property.DetectMode,
                    property.ApproximationModes);
            }

            AddRoiToContourPoints(Contours, roi, property.USE_ROI);

            ConcurrentBag<ContourResult> filteredContours = new ConcurrentBag<ContourResult>();
            ConcurrentBag<OpenCvSharp.Point[]> drawContours = new ConcurrentBag<OpenCvSharp.Point[]>();
            ConcurrentBag<VisionObjectCandidate> detectedCandidates = new ConcurrentBag<VisionObjectCandidate>();
            VisionObjectCandidateLimits limits = ResolveCandidateLimits();

            Parallel.ForEach(Contours, (item, state, index) =>
            {
                if (TryCreateContourCandidate(
                    item,
                    index,
                    0,
                    MinArea,
                    MaxArea,
                    true,
                    limits,
                    out VisionObjectCandidate candidate,
                    out ContourResult result,
                    out OpenCvSharp.Point[] drawContour))
                {
                    detectedCandidates.Add(candidate);
                    if (result != null)
                    {
                        filteredContours.Add(result);
                        drawContours.Add(drawContour);
                    }
                }
            });

            if (property.USE_DRAW_IMAGE) { Cv2.DrawContours(imageResult, drawContours.ToArray(), -1, new Scalar(property.DrawColor.B, property.DrawColor.G, property.DrawColor.R, property.DrawColor.A), property.DrawThickness, LineTypes.Link4); }
            results = ReindexContours(filteredContours.OrderBy(c => c.Index)).ToList();
            candidates = OrderCandidates(detectedCandidates).ToList();

        
            return true;
        }

        public bool MultiRun()
        {
            OpenCvSharp.Point[][] Contours;

            int MinArea = property.MIN_AREA;
            int MaxArea = property.MAX_AREA;

            results.Clear();
            candidates.Clear();
            List<OpenCvSharp.Point[]> drawContoursList = new List<OpenCvSharp.Point[]>();

            if (OpenCvHelper.IsImageEmpty(imageSource))
            {

                return false;
            }

            if (property.USE_DRAW_IMAGE)
            {
                ReplaceResultImage(imageSource.Clone());
                OpenCvHelper.SetImageChannel3(imageResult);
            }

            for (int i = 0; i < property.CvROIS.Count; i++)
            {
                Rect roi = NormalizeContourRoi(property.CvROIS[i]);

                using (Mat imageSrc = CreateWorkingContourImage(roi, true))
                using (Mat contourInput = CreateFindContoursInput(imageSrc))
                {
                    Contours = FindContours(
                        contourInput,
                        property.DetectMode,
                        property.ApproximationModes);
                }

                AddRoiToContourPoints(Contours, roi, true);

                ConcurrentBag<ContourResult> filteredContours = new ConcurrentBag<ContourResult>();
                ConcurrentBag<OpenCvSharp.Point[]> drawContours = new ConcurrentBag<OpenCvSharp.Point[]>();
                ConcurrentBag<VisionObjectCandidate> detectedCandidates = new ConcurrentBag<VisionObjectCandidate>();
                VisionObjectCandidateLimits limits = ResolveCandidateLimits();

                Parallel.ForEach(Contours, (item, state, index) =>
                {
                    if (TryCreateContourCandidate(
                        item,
                        index,
                        i,
                        MinArea,
                        MaxArea,
                        false,
                        limits,
                        out VisionObjectCandidate candidate,
                        out ContourResult result,
                        out OpenCvSharp.Point[] drawContour))
                    {
                        detectedCandidates.Add(candidate);
                        if (result != null)
                        {
                            filteredContours.Add(result);
                            drawContours.Add(drawContour);
                        }
                    }
                });

                results.AddRange(filteredContours.OrderBy(c => c.Index));
                drawContoursList.AddRange(drawContours);
                candidates.AddRange(detectedCandidates);
            }

            if (property.USE_DRAW_IMAGE) { Cv2.DrawContours(imageResult, drawContoursList.ToArray(), -1, new Scalar(property.DrawColor.B, property.DrawColor.G, property.DrawColor.R, property.DrawColor.A), property.DrawThickness, LineTypes.Link4); }
            results = ReindexContours(results).ToList();
            candidates = OrderCandidates(candidates).ToList();
            
        

            return true;
        }

        private Mat CreateWorkingContourImage(OpenCvSharp.Rect roi, bool useRoi)
        {
            return CreatePreprocessedImage(roi, useRoi, property);
        }

        private static Mat CreateFindContoursInput(Mat source)
        {
            if (OpenCvHelper.IsImageEmpty(source))
            {
                throw new InvalidOperationException("Contour preprocessing produced an empty image.");
            }

            if (source.Channels() != 1)
            {
                throw new InvalidOperationException(
                    $"Contour preprocessing must produce one channel. Actual={source.Channels()}.");
            }

            Mat input = new Mat();
            if (source.Type() == MatType.CV_8UC1)
            {
                source.CopyTo(input);
            }
            else
            {
                source.ConvertTo(input, MatType.CV_8UC1);
            }

            if (input.Empty() || !input.IsContinuous())
            {
                input.Dispose();
                throw new InvalidOperationException("Contour input must be a non-empty continuous CV_8UC1 image.");
            }

            return input;
        }

        private static OpenCvSharp.Point[][] FindContours(
            Mat input,
            RetrievalModes retrievalMode,
            ContourApproximationModes approximationMode)
        {
            CvBlobs blobs = new CvBlobs();
            blobs.Label(input);
            List<OpenCvSharp.Point[]> contours = new List<OpenCvSharp.Point[]>();
            foreach (KeyValuePair<int, CvBlob> item in blobs.OrderBy(pair => pair.Key))
            {
                AddContourChain(contours, item.Value?.Contour, approximationMode);
                if (retrievalMode == RetrievalModes.External)
                {
                    continue;
                }

                foreach (CvContourChainCode internalContour in item.Value?.InternalContours
                    ?? new List<CvContourChainCode>())
                {
                    AddContourChain(contours, internalContour, approximationMode);
                }
            }

            return contours.ToArray();
        }

        private static void AddContourChain(
            ICollection<OpenCvSharp.Point[]> destination,
            CvContourChainCode chain,
            ContourApproximationModes approximationMode)
        {
            if (destination == null || chain == null)
            {
                return;
            }

            CvContourPolygon polygon = chain.ConvertToPolygon();
            if (polygon == null || polygon.Count == 0)
            {
                return;
            }

            if (approximationMode != ContourApproximationModes.ApproxNone)
            {
                polygon = polygon.Simplify();
            }

            OpenCvSharp.Point[] points = polygon?.ToArray() ?? Array.Empty<OpenCvSharp.Point>();
            if (points.Length > 0)
            {
                destination.Add(points);
            }
        }

        private bool TryCreateContourCandidate(
            OpenCvSharp.Point[] contour,
            long sourceIndex,
            int regionIndex,
            int minArea,
            int maxArea,
            bool useDrawContourAsResult,
            VisionObjectCandidateLimits limits,
            out VisionObjectCandidate candidate,
            out ContourResult result,
            out OpenCvSharp.Point[] drawContour)
        {
            candidate = null;
            result = null;
            drawContour = null;

            if (contour == null || contour.Length == 0)
            {
                return false;
            }

            double contourArea = Cv2.ContourArea(contour, false);

            OpenCvSharp.Point[] contourForCalc;
            if (property.USE_APPROXPOLYDP)
            {
                double peri = Cv2.ArcLength(contour, true);
                OpenCvSharp.Point[] approxPoints = Cv2.ApproxPolyDP(contour, property.EPSILON * peri, true);
                contourForCalc = approxPoints;
                drawContour = approxPoints;
            }
            else
            {
                contourForCalc = contour;
                drawContour = contour;
            }

            Rect bounds = Cv2.BoundingRect(contourForCalc);
            RotatedRect rotatedRect = Cv2.MinAreaRect(contourForCalc);
            OpenCvSharp.Point center = new OpenCvSharp.Point(
                bounds.X + bounds.Width / 2,
                bounds.Y + bounds.Height / 2);
            OpenCvSharp.Point[] resultContour = useDrawContourAsResult ? drawContour : contour;

            bool masked = IsMasked(bounds);
            VisionObjectCandidateDecision decision = masked
                ? new VisionObjectCandidateDecision(
                    VisionObjectCandidateRejectReasonCode.Masked,
                    "Candidate is inside a configured mask.")
                : VisionObjectCandidateEvaluator.Evaluate(
                    contourArea,
                    bounds.Width,
                    bounds.Height,
                    limits);
            candidate = new VisionObjectCandidate
            {
                CandidateId = VisionObjectCandidate.CreateCandidateId(
                    VisionObjectCandidateGenerationStage.ContourExtraction,
                    regionIndex,
                    (int)sourceIndex),
                RegionIndex = regionIndex,
                NativeIndex = (int)sourceIndex,
                Area = contourArea,
                Center = new Point2d(center.X, center.Y),
                Bounding = new Rectangle(bounds.X, bounds.Y, bounds.Width, bounds.Height),
                Angle = Math.Round(rotatedRect.Angle, 1),
                Accepted = decision.Accepted,
                RejectReasonCode = decision.Code,
                RejectReasonText = decision.Text,
                AppliedLimits = limits,
                Drawing = CreateContourCandidateDrawing(
                    resultContour,
                    bounds,
                    center,
                    Math.Round(rotatedRect.Angle, 1)),
                GenerationStage = VisionObjectCandidateGenerationStage.ContourExtraction,
                CoordinateFrame = VisionObjectCandidateCoordinateFrame.SourceImage
            };

            // Keep the legacy results list area-filtered. The application applies
            // its existing dimension filter after consuming candidates.
            if (masked || contourArea < minArea || contourArea > maxArea)
            {
                return true;
            }

            result = new ContourResult(
                (int)sourceIndex,
                contourArea,
                center,
                bounds,
                resultContour,
                Math.Round(rotatedRect.Angle, 1));
            return true;
        }

        private VisionObjectCandidateLimits ResolveCandidateLimits()
        {
            IVisionObjectFilterProperty filter = property as IVisionObjectFilterProperty;
            return new VisionObjectCandidateLimits(
                property.MIN_AREA,
                property.MAX_AREA,
                filter?.MIN_WIDTH ?? 0,
                filter?.MAX_WIDTH ?? int.MaxValue,
                filter?.MIN_HEIGHT ?? 0,
                filter?.MAX_HEIGHT ?? int.MaxValue);
        }

        private static VisionToolOverlay CreateContourCandidateDrawing(
            OpenCvSharp.Point[] points,
            OpenCvSharp.Rect bounds,
            OpenCvSharp.Point center,
            double angle)
        {
            VisionToolOverlay drawing = new VisionToolOverlay
            {
                Kind = VisionToolOverlayKind.Points,
                Label = "Contour candidate",
                Bounds = new RectangleF(bounds.X, bounds.Y, bounds.Width, bounds.Height),
                Center = new PointF(center.X, center.Y),
                Angle = angle
            };

            foreach (OpenCvSharp.Point point in points ?? Array.Empty<OpenCvSharp.Point>())
            {
                drawing.Points.Add(new PointF(point.X, point.Y));
            }

            return drawing;
        }

        private static IEnumerable<VisionObjectCandidate> OrderCandidates(
            IEnumerable<VisionObjectCandidate> source)
        {
            return (source ?? Enumerable.Empty<VisionObjectCandidate>())
                .Where(candidate => candidate != null)
                .OrderBy(candidate => candidate.RegionIndex)
                .ThenBy(candidate => candidate.NativeIndex);
        }

        private Rect NormalizeContourRoi(Rect roi)
        {
            return roi.Width == 0 || roi.Height == 0
                ? new Rect(0, 0, imageSource.Width, imageSource.Height)
                : roi;
        }

        private string FormatContourRoi()
        {
            if (property.USE_MULTI_ROI)
            {
                return $"Multi({property.CvROIS?.Count ?? 0})";
            }

            Rect roi = NormalizeContourRoi(property.CvROI);
            return $"{roi.X},{roi.Y},{roi.Width},{roi.Height}";
        }

        private static IEnumerable<ContourResult> ReindexContours(IEnumerable<ContourResult> source)
        {
            int index = 1;
            foreach (ContourResult result in source)
            {
                result.Index = index++;
                yield return result;
            }
        }

        private void AddRoiToContourPoints(OpenCvSharp.Point[][] Contours, OpenCvSharp.Rect CvROI, bool applyOffset)
        {
            if (applyOffset)
            {
                for (int i = 0; i < Contours.Length; i++)
                {
                    for (int j = 0; j < Contours[i].Length; j++)
                    {
                        Contours[i][j].X = Contours[i][j].X + CvROI.X;
                        Contours[i][j].Y = Contours[i][j].Y + CvROI.Y;
                    }
                }
            }
        }

        public bool SquareRun()
        {
            results.Clear();

            if (OpenCvHelper.IsImageEmpty(imageSource))
            {
                return false;
            }

            Rect roi = NormalizeContourRoi(property.CvROI);

            using (Mat imageSrc = imageSource.Clone())
            {
                if (OpenCvHelper.IsImageEmpty(imageSource)) return false;
                ReplaceResultImage(imageSrc.Clone());

                if (imageSrc.Channels() == 4) Cv2.CvtColor(imageSrc, imageSrc, ColorConversionCodes.RGBA2GRAY);
                if (imageSrc.Channels() == 3) Cv2.CvtColor(imageSrc, imageSrc, ColorConversionCodes.RGB2GRAY);
                if (imageResult.Channels() == 1) Cv2.CvtColor(imageResult, imageResult, ColorConversionCodes.GRAY2RGB);

                using (Mat imageContour = CreateWorkingContourImage(roi, property.USE_ROI))
                using (Mat contourInput = CreateFindContoursInput(imageContour))
                {
                    OpenCvSharp.Point[][] contours = FindContours(
                        contourInput,
                        property.DetectMode,
                        property.ApproximationModes);
                    AddRoiToContourPoints(contours, roi, property.USE_ROI);

                    List<ContourResult> squareResults = new List<ContourResult>();
                    for (int i = 0; i < contours.Length; i++)
                    {
                        if (TryCreateSquareContourResult(
                            contours[i],
                            i,
                            property.MIN_AREA,
                            property.MAX_AREA,
                            out ContourResult result,
                            out OpenCvSharp.Point[] squarePoints))
                        {
                            squareResults.Add(result);
                            for (int j = 0; j < squarePoints.Length; j++)
                            {
                                Cv2.Circle(imageResult, squarePoints[j], 5, Scalar.Yellow, Cv2.FILLED);
                            }

                            Cv2.Polylines(imageResult, new[] { squarePoints }, true, Scalar.Yellow, 1, LineTypes.AntiAlias, 0);
                        }
                    }

                    results = ReindexContours(squareResults.OrderBy(result => result.Index)).ToList();
                }
            }

            return true;
        }

        private bool TryCreateSquareContourResult(
            OpenCvSharp.Point[] contour,
            long sourceIndex,
            int minArea,
            int maxArea,
            out ContourResult result,
            out OpenCvSharp.Point[] squarePoints)
        {
            result = null;
            squarePoints = null;

            double contourArea = Cv2.ContourArea(contour, false);
            if (contourArea < minArea || contourArea > maxArea)
            {
                return false;
            }

            double peri = Cv2.ArcLength(contour, true);
            OpenCvSharp.Point[] approxPoints = Cv2.ApproxPolyDP(contour, property.EPSILON * peri, true);
            if (approxPoints.Length != 4 || !Cv2.IsContourConvex(approxPoints) || !HasNearRightAngle(approxPoints))
            {
                return false;
            }

            Rect bounds = Cv2.BoundingRect(approxPoints);
            if (IsMasked(bounds))
            {
                return false;
            }

            RotatedRect rotatedRect = Cv2.MinAreaRect(approxPoints);
            OpenCvSharp.Point center = new OpenCvSharp.Point(
                bounds.X + bounds.Width / 2,
                bounds.Y + bounds.Height / 2);

            squarePoints = approxPoints;
            result = new ContourResult(
                (int)sourceIndex,
                contourArea,
                center,
                bounds,
                squarePoints,
                Math.Round(rotatedRect.Angle, 1));
            return true;
        }

        private bool IsMasked(Rect bounds)
        {
            if (property.CvMASKS == null || property.CvMASKS.Count == 0)
            {
                return false;
            }

            for (int i = 0; i < property.CvMASKS.Count; i++)
            {
                if (property.CvMASKS[i].Contains(bounds))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool HasNearRightAngle(OpenCvSharp.Point[] points)
        {
            for (int i = 0; i < points.Length; i++)
            {
                double angle = FormulaUtil.threePointAngle(
                    points[i],
                    points[(i + points.Length - 1) % points.Length],
                    points[(i + 1) % points.Length]);
                if (Math.Abs(angle - 90d) > 5d)
                {
                    return false;
                }
            }

            return true;
        }
    }
}

