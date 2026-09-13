using System;
using System.Collections.Generic;
using System.Linq;
using OpenVisionLab.Core;
using OpenVisionLab.Vision2D.Property;
using OpenVisionLab.Vision2D.Result;
using OpenCvSharp;
using OpenCvSharp.Features2D;

namespace OpenVisionLab.Vision2D.Tool
{
    /// <summary>
    /// Performs feature matching with SIFT and falls back to ORB when the native
    /// runtime does not expose SIFT. The selected detector is reported in the
    /// result metrics.
    /// </summary>
    public partial class SiftTool : OpenCvAlgorithmBase
    {
        public IOpenCVPropertyFeatureSIFT property;
        public List<MatchingResult> results = new List<MatchingResult>();
        private VisionToolErrorCode lastFeatureErrorCode = VisionToolErrorCode.FeatureNoResult;
        private string lastFeatureMessage = "Feature matching found no result.";
        private string lastFeatureDetector = "Unknown";

        public SiftTool() { }

        public void SetProperty(IOpenCVPropertyFeatureSIFT propertyBase) => property = propertyBase;

        protected override IDictionary<string, double> CollectMetrics()
        {
            IDictionary<string, double> metrics = base.CollectMetrics();
            metrics["FeatureDetector.Sift"] = string.Equals(lastFeatureDetector, "SIFT", StringComparison.Ordinal)
                ? 1.0
                : 0.0;
            metrics["FeatureDetector.OrbFallback"] = string.Equals(lastFeatureDetector, "ORB fallback", StringComparison.Ordinal)
                ? 1.0
                : 0.0;
            return metrics;
        }

        public void SetTemplateImage(Mat Image)
        {
            imageTemplate?.Dispose();
            imageTemplate = OpenCvHelper.IsImageEmpty(Image) ? new Mat() : Image.Clone();
        }

        protected override bool TryValidateBeforeRun(out VisionToolErrorCode errorCode, out string message)
        {
            if (!base.TryValidateBeforeRun(out errorCode, out message))
            {
                return false;
            }

            if (OpenCvHelper.IsImageEmpty(imageTemplate))
            {
                errorCode = VisionToolErrorCode.FeatureTemplateMissing;
                message = string.IsNullOrWhiteSpace(property.PATTERN_PATH)
                    ? "Feature matching template image is not loaded."
                    : $"Feature matching template image is not loaded. Path={property.PATTERN_PATH}.";
                return false;
            }

            if (!TryValidateAdaptiveThreshold(
                property,
                VisionToolErrorCode.FeatureInvalidAdaptiveBlockSize,
                out errorCode,
                out message))
            {
                return false;
            }

            if (!TryValidateRoiSet(
                property,
                property.USE_ROI,
                true,
                VisionToolErrorCode.FeatureRoiInvalid,
                "Feature matching",
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
                errorCode = lastFeatureErrorCode == VisionToolErrorCode.None
                    ? VisionToolErrorCode.FeatureNoResult
                    : lastFeatureErrorCode;
                message = string.IsNullOrWhiteSpace(lastFeatureMessage)
                    ? "Feature matching found no result."
                    : lastFeatureMessage;
                return false;
            }

            errorCode = VisionToolErrorCode.None;
            message = string.Empty;
            return true;
        }

        public override void Run()
        {
            if(property.USE_MULTI_ROI)
            {
                MultiRun();
            }
            else
            {
                SingleRun();
            }            
        }

        protected bool MultiRun()
        {
            swTaktTimems.Restart();
            results.Clear();
            ResetFeatureFailure();

            if (OpenCvHelper.IsImageEmpty(imageSource)) { return false; }

            using (Mat imageTemplate = CreatePreparedFeatureTemplate())
            {
                for (int i = 0; i < property.CvROIS.Count; i++)
                {
                    Rect roi = NormalizeFeatureRoi(property.CvROIS[i]);
                    RunFeatureMatchingForRoi(imageTemplate, roi, true);
                }
            }

            swTaktTimems.Stop();
            return true;
        }

        protected bool SingleRun()
        {
            swTaktTimems.Restart();
            results.Clear();
            ResetFeatureFailure();

            if (OpenCvHelper.IsImageEmpty(imageSource)) { return false; }

            Rect roi = NormalizeFeatureRoi(property.CvROI);
            using (Mat imageTemplate = CreatePreparedFeatureTemplate())
            {
                RunFeatureMatchingForRoi(imageTemplate, roi, property.USE_ROI);
            }

            swTaktTimems.Stop();
            return true;
        }

        private Mat CreatePreparedFeatureTemplate()
        {
            Mat preparedTemplate = imageTemplate.Clone();
            ApplyCommonPreprocessing(preparedTemplate, property);
            return preparedTemplate;
        }

        private Rect NormalizeFeatureRoi(Rect roi)
        {
            return roi.Width == 0 || roi.Height == 0
                ? new Rect(0, 0, imageSource.Width, imageSource.Height)
                : roi;
        }

        private void RunFeatureMatchingForRoi(Mat imageTemplate, Rect roi, bool useRoi)
        {
            using (Mat imageSift = CreatePreprocessedImage(roi, useRoi, property))
            {
                AddFeatureMatchResult(imageSift, imageTemplate, useRoi, roi);
            }
        }

        private void AddFeatureMatchResult(Mat imageSift, Mat imageTemplate, bool useRoi, Rect roi)
        {
            if (OpenCvHelper.IsImageEmpty(imageSift) || OpenCvHelper.IsImageEmpty(imageTemplate))
            {
                SetFeatureFailure(VisionToolErrorCode.FeatureNoResult, $"Feature matching source or template image is empty after preprocessing. {FormatFeatureOptions(roi)}");
                return;
            }

            using (FeatureDetectorRuntime detector = CreateFeatureDetectorRuntime())
            using (Mat descriptorsTemplate = new Mat())
            using (Mat descriptorsSource = new Mat())
            {
                lastFeatureDetector = detector.Name;
                detector.Detector.DetectAndCompute(imageTemplate, null, out KeyPoint[] keypointsTemplate, descriptorsTemplate);
                detector.Detector.DetectAndCompute(imageSift, null, out KeyPoint[] keypointsSource, descriptorsSource);

                if (descriptorsTemplate.Empty() || descriptorsSource.Empty()
                    || keypointsTemplate.Length == 0 || keypointsSource.Length == 0)
                {
                    SetFeatureFailure(
                        VisionToolErrorCode.FeatureNoKeypoints,
                        $"Feature matching could not find keypoints. Template={keypointsTemplate.Length}, Source={keypointsSource.Length}, {FormatFeatureOptions(roi)}");
                    return;
                }

                using (BFMatcher matcher = new BFMatcher(detector.NormType))
                {
                    DMatch[][] knnMatches = matcher.KnnMatch(descriptorsTemplate, descriptorsSource, 2);
                    float ratioThreshold = (float)property.SCORE_MIN;
                    List<DMatch> goodMatches = new List<DMatch>();

                    foreach (DMatch[] match in knnMatches)
                    {
                        if (match.Length >= 2 && match[0].Distance < ratioThreshold * match[1].Distance)
                        {
                            goodMatches.Add(match[0]);
                        }
                    }

                    if (goodMatches.Count < 4)
                    {
                        SetFeatureFailure(
                            VisionToolErrorCode.FeatureNotEnoughMatches,
                            $"Feature matching found too few good matches. GoodMatches={goodMatches.Count}, Required=4, TemplateKeypoints={keypointsTemplate.Length}, SourceKeypoints={keypointsSource.Length}, {FormatFeatureOptions(roi)}");
                        return;
                    }

                    Point2f[] srcPts = goodMatches.Select(m => keypointsTemplate[m.QueryIdx].Pt).ToArray();
                    Point2f[] dstPts = goodMatches.Select(m => keypointsSource[m.TrainIdx].Pt).ToArray();
                    Point2d[] srcPtsD = ConvertPoint2fToPoint2d(srcPts);
                    Point2d[] dstPtsD = ConvertPoint2fToPoint2d(dstPts);

                    using (Mat inlierMask = new Mat())
                    using (Mat homography = Cv2.FindHomography(srcPtsD, dstPtsD, HomographyMethods.Ransac, property.RANSAC_REPROJ_THRESHOLD, inlierMask))
                    {
                        if (homography == null || homography.Empty())
                        {
                            SetFeatureFailure(
                                VisionToolErrorCode.FeatureHomographyFailed,
                                $"Feature matching homography failed. GoodMatches={goodMatches.Count}, {FormatFeatureOptions(roi)}");
                            return;
                        }

                        Size size = imageTemplate.Size();
                        Point2f[] pts =
                        {
                            new Point2f(0, 0),
                            new Point2f(0, size.Height - 1),
                            new Point2f(size.Width - 1, size.Height - 1),
                            new Point2f(size.Width - 1, 0)
                        };

                        Point2f[] dst = Cv2.PerspectiveTransform(pts, homography);
                        if (dst.Length != 4)
                        {
                            SetFeatureFailure(VisionToolErrorCode.FeatureHomographyFailed, $"Feature matching homography returned invalid corner count. Count={dst.Length}, {FormatFeatureOptions(roi)}");
                            return;
                        }

                        if (useRoi)
                        {
                            for (int i = 0; i < dst.Length; i++)
                            {
                                dst[i].X += roi.X;
                                dst[i].Y += roi.Y;
                            }
                        }

                        Point2f center = new Point2f(
                            (dst[0].X + dst[2].X) / 2,
                            (dst[0].Y + dst[2].Y) / 2);
                        Size2f size2f = new Size2f(
                            (float)Math.Sqrt(Math.Pow(dst[1].X - dst[0].X, 2) + Math.Pow(dst[1].Y - dst[0].Y, 2)),
                            (float)Math.Sqrt(Math.Pow(dst[3].X - dst[0].X, 2) + Math.Pow(dst[3].Y - dst[0].Y, 2)));
                        float angle = (float)(Math.Atan2(dst[1].Y - dst[0].Y, dst[1].X - dst[0].X) * 180 / Math.PI);
                        RotatedRect rect = new RotatedRect(center, size2f, angle);
                        Rect2f bounding = CreateBoundingRect(dst);
                        double score = CalculateFeatureScore(inlierMask, goodMatches.Count);

                        results.Add(new MatchingResult(results.Count + 1, score, center, bounding, rect.Angle));
                        ResetFeatureFailure();
                    }
                }
            }
        }

        private void ResetFeatureFailure()
        {
            lastFeatureErrorCode = VisionToolErrorCode.FeatureNoResult;
            lastFeatureMessage = "Feature matching found no result.";
            lastFeatureDetector = "Unknown";
        }

        private void SetFeatureFailure(VisionToolErrorCode errorCode, string message)
        {
            if (results.Count > 0)
            {
                return;
            }

            if (GetFeatureFailurePriority(errorCode) < GetFeatureFailurePriority(lastFeatureErrorCode))
            {
                return;
            }

            lastFeatureErrorCode = errorCode;
            lastFeatureMessage = message;
        }

        private static int GetFeatureFailurePriority(VisionToolErrorCode errorCode)
        {
            switch (errorCode)
            {
                case VisionToolErrorCode.FeatureHomographyFailed:
                    return 4;
                case VisionToolErrorCode.FeatureNotEnoughMatches:
                    return 3;
                case VisionToolErrorCode.FeatureNoKeypoints:
                    return 2;
                case VisionToolErrorCode.FeatureNoResult:
                    return 1;
                default:
                    return 0;
            }
        }

        private string FormatFeatureOptions(Rect roi)
        {
            return $"ScoreMin={property.SCORE_MIN}, RansacThreshold={property.RANSAC_REPROJ_THRESHOLD}, Threshold={property.USE_THRESHOLD}, Adaptive={property.USE_ADAPTIVE_THRESHOLD}, ROI={FormatFeatureRoi(roi)}";
        }

        private string FormatFeatureRoi(Rect roi)
        {
            if (property.USE_MULTI_ROI)
            {
                return $"Multi({property.CvROIS?.Count ?? 0}) current={roi.X},{roi.Y},{roi.Width},{roi.Height}";
            }

            Rect actualRoi = property.USE_ROI ? roi : new Rect(0, 0, imageSource.Width, imageSource.Height);
            return $"{actualRoi.X},{actualRoi.Y},{actualRoi.Width},{actualRoi.Height}";
        }

        private static Rect2f CreateBoundingRect(Point2f[] points)
        {
            float minX = points.Min(point => point.X);
            float minY = points.Min(point => point.Y);
            float maxX = points.Max(point => point.X);
            float maxY = points.Max(point => point.Y);

            return new Rect2f(minX, minY, maxX - minX, maxY - minY);
        }

        private static FeatureDetectorRuntime CreateFeatureDetectorRuntime()
        {
            try
            {
                return new FeatureDetectorRuntime(SIFT.Create(), NormTypes.L2, "SIFT");
            }
            catch (Exception ex) when (IsSiftUnavailable(ex))
            {
                return new FeatureDetectorRuntime(ORB.Create(1000), NormTypes.Hamming, "ORB fallback");
            }
        }

        private static bool IsSiftUnavailable(Exception exception)
        {
            Exception baseException = exception.GetBaseException();
            return baseException is EntryPointNotFoundException
                || baseException.Message.IndexOf("features2d_SIFT_create", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private sealed class FeatureDetectorRuntime : IDisposable
        {
            public FeatureDetectorRuntime(Feature2D detector, NormTypes normType, string name)
            {
                Detector = detector;
                NormType = normType;
                Name = name;
            }

            public Feature2D Detector { get; }
            public NormTypes NormType { get; }
            public string Name { get; }

            public void Dispose()
            {
                Detector?.Dispose();
            }
        }

        private static double CalculateFeatureScore(Mat inlierMask, int matchCount)
        {
            if (matchCount <= 0 || inlierMask == null || inlierMask.Empty())
            {
                return 0;
            }

            return Math.Round((double)Cv2.CountNonZero(inlierMask) / matchCount * 100.0D, 1);
        }

        public OpenCvSharp.Point2d ConvertPoint2fToPoint2d(OpenCvSharp.Point2f point)
        {
            return new OpenCvSharp.Point2d((double)point.X, (double)point.Y);
        }

        public OpenCvSharp.Point2d[] ConvertPoint2fToPoint2d(OpenCvSharp.Point2f[] points)
        {

            OpenCvSharp.Point2d[] point2fs = new OpenCvSharp.Point2d[points.Length];

            for (int i = 0; i < points.Length; i++)
            {
                point2fs[i] = new OpenCvSharp.Point2d(points[i].X, points[i].Y);
            }

            return point2fs;
        }
    }
}

