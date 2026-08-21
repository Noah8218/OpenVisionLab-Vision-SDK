using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using OpenVisionLab.Core;
using OpenVisionLab.Vision2D.Property;
using OpenVisionLab.Vision2D.Result;
using OpenCvSharp;

namespace OpenVisionLab.Vision2D.Tool
{
    public class MatchingTool : OpenCvAlgorithmBase
    {
        public IOpenCVPropertyMatching property;
        public List<MatchingResult> results = new List<MatchingResult>();
        private Mat originalTemplate = new Mat();
        private readonly MatchingSearchEngine searchEngine;

        public MatchingTool()
        {
            searchEngine = new MatchingSearchEngine(this);
        }

        public void SetTemplateImage(Mat Image)
        {
            originalTemplate?.Dispose();
            imageTemplate?.Dispose();

            originalTemplate = OpenCvHelper.IsImageEmpty(Image) ? new Mat() : Image.Clone();
            imageTemplate = originalTemplate.Clone();
        }

        public void SetProperty(IOpenCVPropertyMatching property) => this.property = property;

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                originalTemplate?.Dispose();
                originalTemplate = null;
            }

            base.Dispose(disposing);
        }

        protected override bool TryValidateBeforeRun(out VisionToolErrorCode errorCode, out string message)
        {
            if (!base.TryValidateBeforeRun(out errorCode, out message))
            {
                return false;
            }

            Mat template = searchEngine.GetTemplateForRun();
            if (OpenCvHelper.IsImageEmpty(template))
            {
                errorCode = VisionToolErrorCode.MatchingTemplateMissing;
                message = string.IsNullOrWhiteSpace(property.PATTERN_PATH)
                    ? "Matching template image is not loaded."
                    : $"Matching template image is not loaded. Path={property.PATTERN_PATH}.";
                return false;
            }

            if (property.MAGNIFIATION <= 0)
            {
                errorCode = VisionToolErrorCode.MatchingInvalidScale;
                message = $"Matching magnification must be greater than 0. Magnification={property.MAGNIFIATION}.";
                return false;
            }

            if (property.USE_FIND_SCALE
                && (property.FIND_SCALE_MIN <= 0D || property.FIND_SCALE_MAX <= 0D))
            {
                errorCode = VisionToolErrorCode.MatchingInvalidScale;
                message = $"Matching scale range must be greater than 0 when scale search is enabled. Scale={property.FIND_SCALE_MIN:0.###}..{property.FIND_SCALE_MAX:0.###}.";
                return false;
            }

            if (property.USE_FIND_SCALE && property.FIND_SCALE_STEP <= 0D)
            {
                errorCode = VisionToolErrorCode.MatchingInvalidScale;
                message = $"Matching scale step must be greater than 0 when scale search is enabled. ScaleStep={property.FIND_SCALE_STEP:0.###}.";
                return false;
            }

            if (property.USE_FIND_ANGLE && property.FIND_ANGLE <= 0)
            {
                errorCode = VisionToolErrorCode.MatchingInvalidAngleStep;
                message = $"Matching angle step must be greater than 0 when angle search is enabled. AngleStep={property.FIND_ANGLE}.";
                return false;
            }

            if (property.USE_FIND_ANGLE
                && property.USE_COARSE_TO_FINE_ANGLE_SEARCH
                && property.COARSE_ANGLE_STEP <= 0)
            {
                errorCode = VisionToolErrorCode.MatchingInvalidAngleStep;
                message = $"Matching coarse angle step must be greater than 0 when coarse-to-fine angle search is enabled. CoarseAngleStep={property.COARSE_ANGLE_STEP}.";
                return false;
            }

            if (property.USE_FIND_ANGLE
                && property.USE_COARSE_TO_FINE_ANGLE_SEARCH
                && property.COARSE_ANGLE_TOP_K <= 0)
            {
                errorCode = VisionToolErrorCode.MatchingInvalidAngleStep;
                message = $"Matching coarse angle top K must be greater than 0 when coarse-to-fine angle search is enabled. CoarseAngleTopK={property.COARSE_ANGLE_TOP_K}.";
                return false;
            }

            if (!TryValidateAdaptiveThreshold(
                property,
                VisionToolErrorCode.MatchingInvalidAdaptiveBlockSize,
                out errorCode,
                out message))
            {
                return false;
            }

            if (!TryValidateRoiSet(
                property,
                property.USE_ROI,
                true,
                VisionToolErrorCode.MatchingRoiInvalid,
                "Matching",
                out errorCode,
                out message))
            {
                return false;
            }

            if (!TryValidateMatchingWorkingSize(template, out errorCode, out message))
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
                errorCode = VisionToolErrorCode.MatchingNoResult;
                message = $"Matching found no result. {searchEngine.FormatMatchingOptions()}";
                return false;
            }

            errorCode = VisionToolErrorCode.None;
            message = string.Empty;
            return true;
        }

        private bool TryValidateMatchingWorkingSize(Mat template, out VisionToolErrorCode errorCode, out string message)
        {
            foreach (Rect sourceBounds in GetMatchingSourceBounds())
            {
                int sourceWidth = (int)(sourceBounds.Width / property.MAGNIFIATION);
                int sourceHeight = (int)(sourceBounds.Height / property.MAGNIFIATION);

                if (sourceWidth <= 0 || sourceHeight <= 0)
                {
                    errorCode = VisionToolErrorCode.MatchingInvalidScale;
                    message = $"Matching magnification creates an empty working image. Magnification={property.MAGNIFIATION}.";
                    return false;
                }

                bool hasValidScale = false;
                foreach (double scale in searchEngine.CreateSearchScales())
                {
                    int templateWidth = (int)(Math.Round(template.Width * scale) / property.MAGNIFIATION);
                    int templateHeight = (int)(Math.Round(template.Height * scale) / property.MAGNIFIATION);
                    if (templateWidth > 0
                        && templateHeight > 0
                        && templateWidth <= sourceWidth
                        && templateHeight <= sourceHeight)
                    {
                        hasValidScale = true;
                        break;
                    }
                }

                if (!hasValidScale)
                {
                    errorCode = VisionToolErrorCode.MatchingTemplateInvalid;
                    message = $"Matching template scale range is larger than the working image. Template={template.Width}x{template.Height}, Source={sourceWidth}x{sourceHeight}, Scale={searchEngine.FormatScaleOptions()}.";
                    return false;
                }
            }

            errorCode = VisionToolErrorCode.None;
            message = string.Empty;
            return true;
        }

        private IEnumerable<Rect> GetMatchingSourceBounds()
        {
            Rect fullImage = new Rect(0, 0, imageSource.Width, imageSource.Height);

            if (property.USE_MULTI_ROI && property.CvROIS != null && property.CvROIS.Count > 0)
            {
                foreach (Rect roi in property.CvROIS)
                {
                    yield return roi.Width > 0 && roi.Height > 0 ? roi : fullImage;
                }

                yield break;
            }

            yield return property.USE_ROI && property.CvROI.Width > 0 && property.CvROI.Height > 0
                ? property.CvROI
                : fullImage;
        }

        protected override VisionToolErrorCode ResolveExecutionErrorCode(Exception exception)
        {
            VisionToolErrorCode baseCode = base.ResolveExecutionErrorCode(exception);
            if (baseCode == VisionToolErrorCode.OpenCvExecutionFailed
                && OpenCvHelper.IsImageEmpty(imageTemplate))
            {
                return VisionToolErrorCode.MatchingTemplateInvalid;
            }

            return baseCode;
        }

        public Mat Rotate(Mat src, double angle, bool PaddingWhite = false)
        {
            return searchEngine.Rotate(src, angle, PaddingWhite);
        }

        public override void Run()
        {
            searchEngine.Run();
        }

        public bool ImagePyramidsMultiRun()
        {
            return searchEngine.ImagePyramidsMultiRun();
        }

        public bool ImagePyramidsSingleRun()
        {
            return searchEngine.ImagePyramidsSingleRun();
        }

        private sealed class MatchingSearchEngine
    {
        private const double PyramidProposalScale = 0.5D;
        private readonly MatchingTool owner;

        public MatchingSearchEngine(MatchingTool owner)
        {
            this.owner = owner;
        }

        private IOpenCVPropertyMatching property => owner.property;

        private List<MatchingResult> results => owner.results;

        private Mat originalTemplate => owner.originalTemplate;

        private Mat imageTemplate => owner.imageTemplate;

        private Mat imageSource => owner.imageSource;

        private System.Diagnostics.Stopwatch swTaktTimems => owner.swTaktTimems;

        public Mat GetTemplateForRun()
        {
            return OpenCvHelper.IsImageEmpty(originalTemplate) ? imageTemplate : originalTemplate;
        }

        private Mat CreatePreparedTemplate()
        {
            Mat template = GetTemplateForRun().Clone();
            ApplyMatchingPreprocessing(template);
            return template;
        }

        private void ApplyMatchingPreprocessing(Mat image)
        {
            OpenCvHelper.SetImageChannel1(image);

            if (property.USE_THRESHOLD)
            {
                Cv2.Threshold(image, image, property.THRESHOLD, 255, property.THRESHOLD_TYPES);
            }
            else if (property.USE_ADAPTIVE_THRESHOLD)
            {
                Cv2.AdaptiveThreshold(
                    image,
                    image,
                    property.ADAPTIVE_THRESHOLD,
                    property.ADAPTIVE_THRESHOLD_ALGORITHM,
                    property.ADAPTIVE_THRESHOLD_TYPES,
                    property.BlockSize,
                    property.Weight);
            }

            if (property.USE_CANNY)
            {
                Cv2.GaussianBlur(image, image, new OpenCvSharp.Size(3, 3), 1, 1, BorderTypes.Default);
                Cv2.Canny(image, image, property.CANNY_LOW, property.CANNY_HIGH, 3, true);
            }
        }

        private Mat ResizeByMagnification(Mat image)
        {
            return image.Resize(new OpenCvSharp.Size(
                (int)(image.Width / property.MAGNIFIATION),
                (int)(image.Height / property.MAGNIFIATION)));
        }

        private static Mat ResizeTemplateForScale(Mat template, double scale)
        {
            double normalizedScale = NormalizeScale(scale);
            if (Math.Abs(normalizedScale - 1D) <= 0.000001D)
            {
                return template.Clone();
            }

            Mat resized = new Mat();
            Cv2.Resize(
                template,
                resized,
                new OpenCvSharp.Size(
                    Math.Max(1, (int)Math.Round(template.Width * normalizedScale)),
                    Math.Max(1, (int)Math.Round(template.Height * normalizedScale))),
                0D,
                0D,
                normalizedScale < 1D ? InterpolationFlags.Area : InterpolationFlags.Linear);
            return resized;
        }

        private void FindTemplate(Mat ImageSorce, Mat Template, ConcurrentBag<MatchingResult> Results_T, double angle, OpenCvSharp.Rect CvROI, bool applyRoiOffset)
        {
            FindTemplate(ImageSorce, Template, Results_T, angle, 1D, CvROI, applyRoiOffset, property.SCORE_MIN);
        }

        private void FindTemplate(Mat ImageSorce, Mat Template, ConcurrentBag<MatchingResult> Results_T, double angle, double scale, OpenCvSharp.Rect CvROI, bool applyRoiOffset, double minimumScore)
        {
            using (Mat imageMatching = new Mat())
            {
                Cv2.MatchTemplate(ImageSorce, Template, imageMatching, property.MATCH_MODE, null);

                for (int attempt = 0; attempt < 64; attempt++)
                {
                    if (!TryGetBestMatch(imageMatching, minimumScore, out OpenCvSharp.Point matchLocation, out double qualityScore))
                    {
                        break;
                    }

                    if (ShouldValidateCandidateByImageDifference(property.MATCH_MODE)
                        && !IsValidMatchingCandidate(ImageSorce, Template, matchLocation))
                    {
                        SuppressMatchingScore(imageMatching, Template, matchLocation, property.MATCH_MODE);
                        continue;
                    }

                    int ImageTplW = Template.Width;
                    int ImageTplH = Template.Height;

                    OpenCvSharp.Point ptStart = new OpenCvSharp.Point(matchLocation.X, matchLocation.Y);
                    OpenCvSharp.Point ptEnd = new OpenCvSharp.Point(matchLocation.X + (ImageTplW), matchLocation.Y + (ImageTplH));
                    double dScore = qualityScore * 100.0D;

                    Rect2f rtBounding = applyRoiOffset
                        ? new Rect2f(ptStart.X + CvROI.X, ptStart.Y + CvROI.Y, ptEnd.X - ptStart.X, ptEnd.Y - ptStart.Y)
                        : new Rect2f(ptStart.X, ptStart.Y, ptEnd.X - ptStart.X, ptEnd.Y - ptStart.Y);

                    OpenCvSharp.Point2f ptCenter = new OpenCvSharp.Point2f((rtBounding.X + (rtBounding.X + rtBounding.Width)) / 2, (rtBounding.Y + (rtBounding.Y + rtBounding.Height)) / 2);
                    Results_T.Add(new MatchingResult(0, dScore, ptCenter, rtBounding, angle, scale));
                    break;
                }
            }
        }

        private bool TryGetBestMatch(Mat imageMatching, out OpenCvSharp.Point matchLocation, out double qualityScore)
        {
            return TryGetBestMatch(imageMatching, property.SCORE_MIN, out matchLocation, out qualityScore);
        }

        private bool TryGetBestMatch(Mat imageMatching, double minimumScore, out OpenCvSharp.Point matchLocation, out double qualityScore)
        {
            Cv2.MinMaxLoc(
                imageMatching,
                out double minScore,
                out double maxScore,
                out OpenCvSharp.Point minLocation,
                out OpenCvSharp.Point maxLocation);

            bool lowerScoreIsBetter = IsLowerScoreBetter(property.MATCH_MODE);
            double rawScore = lowerScoreIsBetter ? minScore : maxScore;
            matchLocation = lowerScoreIsBetter ? minLocation : maxLocation;
            qualityScore = ConvertToQualityScore(rawScore, property.MATCH_MODE);
            return qualityScore > minimumScore;
        }

        private static bool IsLowerScoreBetter(TemplateMatchModes matchMode)
        {
            return matchMode == TemplateMatchModes.SqDiff
                || matchMode == TemplateMatchModes.SqDiffNormed;
        }

        private static double ConvertToQualityScore(double rawScore, TemplateMatchModes matchMode)
        {
            switch (matchMode)
            {
                case TemplateMatchModes.SqDiffNormed:
                    return 1.0 - Clamp01(rawScore);
                case TemplateMatchModes.SqDiff:
                    return 1.0 / (1.0 + Math.Max(0.0, rawScore));
                default:
                    return rawScore;
            }
        }

        private static double Clamp01(double value)
        {
            if (value < 0) { return 0; }
            if (value > 1) { return 1; }
            return value;
        }

        private static bool ShouldValidateCandidateByImageDifference(TemplateMatchModes matchMode)
        {
            return matchMode == TemplateMatchModes.SqDiff
                || matchMode == TemplateMatchModes.SqDiffNormed;
        }

        private static bool IsValidMatchingCandidate(Mat imageSource, Mat template, OpenCvSharp.Point location)
        {
            Rect candidateRect = new Rect(location, template.Size());
            if (candidateRect.X < 0
                || candidateRect.Y < 0
                || candidateRect.Right > imageSource.Width
                || candidateRect.Bottom > imageSource.Height)
            {
                return false;
            }

            using (Mat candidate = imageSource.SubMat(candidateRect))
            {
                Cv2.MeanStdDev(template, out _, out Scalar templateStdDev);
                Cv2.MeanStdDev(candidate, out _, out Scalar candidateStdDev);

                double templateDeviation = templateStdDev.Val0;
                double candidateDeviation = candidateStdDev.Val0;
                if (templateDeviation < 1.0)
                {
                    return true;
                }

                if (candidateDeviation < Math.Max(1.0, templateDeviation * 0.5))
                {
                    return false;
                }

                double meanAbsoluteDifference = GetMeanAbsoluteDifference(candidate, template);
                return meanAbsoluteDifference <= Math.Max(20.0, templateDeviation * 0.5);
            }
        }

        private static double GetMeanAbsoluteDifference(Mat candidate, Mat template)
        {
            using (Mat difference = new Mat())
            {
                Cv2.Absdiff(candidate, template, difference);
                Scalar mean = Cv2.Mean(difference);
                int channels = Math.Max(1, difference.Channels());
                double sum = mean.Val0;
                if (channels > 1)
                {
                    sum += mean.Val1;
                }

                if (channels > 2)
                {
                    sum += mean.Val2;
                }

                if (channels > 3)
                {
                    sum += mean.Val3;
                }

                return sum / channels;
            }
        }

        private static void SuppressMatchingScore(Mat imageMatching, Mat template, OpenCvSharp.Point location, TemplateMatchModes matchMode)
        {
            Rect suppressRect = ClampRectToImage(
                new Rect(
                    location.X - Math.Max(1, template.Width / 2),
                    location.Y - Math.Max(1, template.Height / 2),
                    Math.Max(3, template.Width),
                    Math.Max(3, template.Height)),
                imageMatching);
            if (suppressRect.Width > 0 && suppressRect.Height > 0)
            {
                Scalar suppressedScore = IsLowerScoreBetter(matchMode)
                    ? Scalar.All(double.MaxValue / 4)
                    : Scalar.All(-1);
                imageMatching.Rectangle(suppressRect, suppressedScore, -1);
            }
        }

        public Mat Rotate(Mat src, double angle, bool PaddingWhite = false)
        {
            return RotatedTemplateCache.Rotate(src, angle, PaddingWhite);
        }

        private RotatedTemplateLease GetOrCreateRotatedTemplate(Mat template, double angle, ulong templateHash, bool useCache)
        {
            return RotatedTemplateCache.GetOrCreate(
                template,
                angle,
                templateHash,
                useCache,
                property.USE_PADDING_COLOR_WHITE);
        }

        private sealed class RotatedTemplateCache
        {
            private const long ByteBudget = 128L * 1024L * 1024L;
            private const long MinimumBytes = 32L * 1024L;
            private const int EntryLimit = 4096;
            private static readonly object sync = new object();
            private static readonly Dictionary<RotatedTemplateCacheKey, RotatedTemplateCacheEntry> entries =
                new Dictionary<RotatedTemplateCacheKey, RotatedTemplateCacheEntry>();
            private static readonly LinkedList<RotatedTemplateCacheKey> lru =
                new LinkedList<RotatedTemplateCacheKey>();
            private static long cachedBytes;

            public static Mat Rotate(Mat src, double angle, bool paddingWhite)
            {
                Mat rotate = new Mat(src.Size(), src.Type());
                Mat matrix = Cv2.GetRotationMatrix2D(new Point2f(src.Width / 2, src.Height / 2), angle, 1);
                if (paddingWhite)
                {
                    Cv2.WarpAffine(src, rotate, matrix, src.Size(), InterpolationFlags.Linear, BorderTypes.Constant, new Scalar(255, 255, 255));
                }
                else
                {
                    Cv2.WarpAffine(src, rotate, matrix, src.Size(), InterpolationFlags.Linear, BorderTypes.Reflect);
                }

                return rotate;
            }

            public static RotatedTemplateLease GetOrCreate(
                Mat template,
                double angle,
                ulong templateHash,
                bool useCache,
                bool paddingWhite)
            {
                if (OpenCvHelper.IsImageEmpty(template))
                {
                    return RotatedTemplateLease.CreateUncached(new Mat());
                }

                double normalizedAngle = NormalizeCacheAngle(angle);
                if (!useCache)
                {
                    return RotatedTemplateLease.CreateUncached(Rotate(template, normalizedAngle, paddingWhite));
                }

                RotatedTemplateCacheKey key = new RotatedTemplateCacheKey(
                    templateHash,
                    template.Width,
                    template.Height,
                    template.Type(),
                    paddingWhite,
                    normalizedAngle);

                RotatedTemplateLease cached = TryUseCachedRotatedTemplate(key);
                if (cached != null)
                {
                    return cached;
                }

                Mat rotated = Rotate(template, normalizedAngle, paddingWhite);
                return StoreOrUseRotatedTemplate(key, rotated);
            }

            private static RotatedTemplateLease TryUseCachedRotatedTemplate(RotatedTemplateCacheKey key)
            {
                lock (sync)
                {
                    if (!entries.TryGetValue(key, out RotatedTemplateCacheEntry entry)
                        || OpenCvHelper.IsImageEmpty(entry.Image))
                    {
                        return null;
                    }

                    lru.Remove(entry.Node);
                    lru.AddFirst(entry.Node);
                    entry.UseCount++;

                    // MatchTemplate reads the template only. Lease the cached Mat directly and
                    // keep it protected from eviction until the caller disposes the lease.
                    return RotatedTemplateLease.CreateCached(entry);
                }
            }

            private static RotatedTemplateLease StoreOrUseRotatedTemplate(RotatedTemplateCacheKey key, Mat image)
            {
                if (OpenCvHelper.IsImageEmpty(image))
                {
                    return RotatedTemplateLease.CreateUncached(image);
                }

                long imageBytes = EstimateMatBytes(image);
                if (imageBytes <= 0 || imageBytes > ByteBudget / 2)
                {
                    return RotatedTemplateLease.CreateUncached(image);
                }

                lock (sync)
                {
                    if (entries.TryGetValue(key, out RotatedTemplateCacheEntry existing)
                        && !OpenCvHelper.IsImageEmpty(existing.Image))
                    {
                        image.Dispose();
                        lru.Remove(existing.Node);
                        lru.AddFirst(existing.Node);
                        existing.UseCount++;
                        return RotatedTemplateLease.CreateCached(existing);
                    }

                    LinkedListNode<RotatedTemplateCacheKey> node = lru.AddFirst(key);
                    RotatedTemplateCacheEntry entry = new RotatedTemplateCacheEntry(image, imageBytes, node);
                    entry.UseCount++;
                    entries[key] = entry;
                    cachedBytes += imageBytes;
                    TrimRotatedTemplateCache();
                    return RotatedTemplateLease.CreateCached(entry);
                }
            }

            private static void TrimRotatedTemplateCache()
            {
                int inspected = 0;
                while ((cachedBytes > ByteBudget
                        || entries.Count > EntryLimit)
                    && lru.Last != null
                    && inspected <= entries.Count)
                {
                    RotatedTemplateCacheKey oldestKey = lru.Last.Value;
                    lru.RemoveLast();

                    if (!entries.TryGetValue(oldestKey, out RotatedTemplateCacheEntry entry))
                    {
                        continue;
                    }

                    if (entry.UseCount > 0)
                    {
                        lru.AddFirst(entry.Node);
                        inspected++;
                        continue;
                    }

                    entries.Remove(oldestKey);
                    cachedBytes -= entry.Bytes;
                    entry.Image.Dispose();
                }
            }

            public static void Release(RotatedTemplateCacheEntry entry)
            {
                if (entry == null)
                {
                    return;
                }

                lock (sync)
                {
                    if (entry.UseCount > 0)
                    {
                        entry.UseCount--;
                    }

                    TrimRotatedTemplateCache();
                }
            }

            private static long EstimateMatBytes(Mat image)
            {
                if (OpenCvHelper.IsImageEmpty(image))
                {
                    return 0;
                }

                return (long)image.Rows * image.Cols * image.ElemSize();
            }

            public static bool ShouldUse(Mat image)
            {
                long imageBytes = EstimateMatBytes(image);
                return imageBytes >= MinimumBytes
                    && imageBytes <= ByteBudget / 2;
            }

            private static double NormalizeCacheAngle(double angle)
            {
                double normalized = Math.Round(angle, 6);
                return Math.Abs(normalized) < 0.000001D ? 0D : normalized;
            }

            public static unsafe ulong ComputeContentHash(Mat image)
            {
                if (OpenCvHelper.IsImageEmpty(image))
                {
                    return 0;
                }

                const ulong offset = 14695981039346656037UL;
                const ulong prime = 1099511628211UL;
                ulong hash = offset;
                void AddByte(byte value)
                {
                    hash ^= value;
                    hash *= prime;
                }

                long rowBytes = (long)image.Cols * image.ElemSize();
                if (rowBytes <= 0)
                {
                    return hash;
                }

                if (image.IsContinuous() && !image.IsSubmatrix())
                {
                    long length = image.DataEnd.ToInt64() - image.Data.ToInt64();
                    byte* data = (byte*)image.Data.ToPointer();
                    for (long i = 0; i < length; i++)
                    {
                        AddByte(data[i]);
                    }

                    return hash;
                }

                byte* row = (byte*)image.Data.ToPointer();
                long step = image.Step();
                for (int y = 0; y < image.Rows; y++)
                {
                    byte* current = row + step * y;
                    for (long x = 0; x < rowBytes; x++)
                    {
                        AddByte(current[x]);
                    }
                }

                return hash;
            }
        }

        public void Run()
        {
            if (property.USE_MULTI_ROI)
            {
                ImagePyramidsMultiRun();
            }
            else
            {
                ImagePyramidsSingleRun();
            }
        }

        public bool ImagePyramidsMultiRun()
        {
            swTaktTimems.Restart();
            results.Clear();

            if (OpenCvHelper.IsImageEmpty(imageSource))
            {
                return false;
            }

            using (Mat preparedSource = imageSource.Clone())
            using (Mat preparedTemplate = CreatePreparedTemplate())
            {
                ApplyMatchingPreprocessing(preparedSource);

                for (int i = 0; i < property.CvROIS.Count; i++)
                {
                    Rect roi = property.CvROIS[i];
                    if (roi.Width == 0 || roi.Height == 0)
                    {
                        roi = new Rect(0, 0, preparedSource.Width, preparedSource.Height);
                    }

                    if (!RunMatchingForSource(preparedSource, preparedTemplate, roi, true, true))
                    {
                        return false;
                    }
                }
            }
            swTaktTimems.Stop();


            return true;
        }

        public bool ImagePyramidsSingleRun()
        {
            swTaktTimems.Restart();
            results.Clear();

            if (OpenCvHelper.IsImageEmpty(imageSource))
            {
                return false;
            }

            using (Mat preparedSource = imageSource.Clone())
            {
                ApplyMatchingPreprocessing(preparedSource);
                Rect roi = property.USE_ROI && property.CvROI.Width > 0 && property.CvROI.Height > 0
                    ? property.CvROI
                    : new Rect(0, 0, preparedSource.Width, preparedSource.Height);

                using (Mat preparedTemplate = CreatePreparedTemplate())
                {
                    if (!RunMatchingForSource(preparedSource, preparedTemplate, roi, property.USE_ROI, property.USE_ROI))
                    {
                        return false;
                    }
                }
            }
            swTaktTimems.Stop();

            return true;
        }

        private bool RunMatchingForSource(Mat preparedSource, Mat preparedTemplate, Rect roi, bool useRoiCrop, bool applyRoiOffset)
        {
            if (OpenCvHelper.IsImageEmpty(preparedSource) || OpenCvHelper.IsImageEmpty(preparedTemplate))
            {
                return false;
            }

            using (Mat imageSrc = useRoiCrop ? preparedSource.SubMat(roi) : preparedSource.Clone())
            using (SearchTemplateCollection searchTemplates = CreateSearchTemplates(preparedTemplate, imageSrc))
            {
                if (searchTemplates.Count == 0)
                {
                    return false;
                }

                int maxCount = property.NUM_MATCH;
                int attempts = Math.Max(maxCount * 4, maxCount + 3);
                while (maxCount > 0 && attempts > 0)
                {
                    Thread.Sleep(0);
                    attempts--;

                    using (Mat imageSubMat = ResizeByMagnification(imageSrc))
                    {
                        MatchingResult highestScoreResult = FindBestMatchingCandidate(imageSubMat, searchTemplates.Items, roi, applyRoiOffset);
                        if (highestScoreResult == null)
                        {
                            break;
                        }

                        MatchingSearchTemplate selectedTemplate = searchTemplates.FindByScale(highestScoreResult.Scale);
                        if (selectedTemplate == null)
                        {
                            break;
                        }

                        maxCount--;
                        MatchingResult refinedResult = TryRefineMatchingResult(imageSrc, selectedTemplate.PreparedTemplate, roi, highestScoreResult, applyRoiOffset);
                        if (refinedResult != null && !AddFinalMatchingResult(refinedResult))
                        {
                            maxCount++;
                        }
                    }
                }
            }

            return true;
        }

        private SearchTemplateCollection CreateSearchTemplates(Mat preparedTemplate, Mat imageSrc)
        {
            List<MatchingSearchTemplate> templates = new List<MatchingSearchTemplate>();
            int sourceWidth = Math.Max(1, (int)(imageSrc.Width / property.MAGNIFIATION));
            int sourceHeight = Math.Max(1, (int)(imageSrc.Height / property.MAGNIFIATION));
            foreach (double scale in CreateSearchScales())
            {
                Mat scaledTemplate = ResizeTemplateForScale(preparedTemplate, scale);
                int workingWidth = (int)(scaledTemplate.Width / property.MAGNIFIATION);
                int workingHeight = (int)(scaledTemplate.Height / property.MAGNIFIATION);
                if (workingWidth <= 0 || workingHeight <= 0)
                {
                    scaledTemplate.Dispose();
                    continue;
                }

                Mat workingTemplate = ResizeByMagnification(scaledTemplate);
                if (workingTemplate.Width <= 0
                    || workingTemplate.Height <= 0
                    || workingTemplate.Width > sourceWidth
                    || workingTemplate.Height > sourceHeight)
                {
                    workingTemplate.Dispose();
                    scaledTemplate.Dispose();
                    continue;
                }

                templates.Add(new MatchingSearchTemplate(scale, scaledTemplate, workingTemplate));
            }

            return new SearchTemplateCollection(templates);
        }

        private MatchingResult FindBestMatchingCandidate(Mat imageSubMat, IReadOnlyList<MatchingSearchTemplate> searchTemplates, Rect roi, bool applyRoiOffset)
        {
            if (ShouldUsePyramidPositionProposal())
            {
                MatchingResult proposalCandidate = FindBestMatchingCandidatePyramidProposal(imageSubMat, searchTemplates, roi, applyRoiOffset);
                if (proposalCandidate != null)
                {
                    return proposalCandidate;
                }
            }

            ConcurrentBag<MatchingResult> candidates = new ConcurrentBag<MatchingResult>();
            foreach (MatchingSearchTemplate searchTemplate in searchTemplates)
            {
                MatchingResult candidate = FindBestMatchingCandidate(imageSubMat, searchTemplate.WorkingTemplate, searchTemplate.Scale, roi, applyRoiOffset);
                if (candidate != null)
                {
                    candidates.Add(candidate);
                }
            }

            return candidates.OrderByDescending(r => r.Score).FirstOrDefault();
        }

        private bool ShouldUsePyramidPositionProposal()
        {
            return property.USE_PYRAMID_POSITION_PROPOSAL
                && !property.USE_FIND_ANGLE
                && property.PYRAMID_POSITION_TOP_N > 0;
        }

        private MatchingResult FindBestMatchingCandidatePyramidProposal(
            Mat imageSubMat,
            IReadOnlyList<MatchingSearchTemplate> searchTemplates,
            Rect roi,
            bool applyRoiOffset)
        {
            if (OpenCvHelper.IsImageEmpty(imageSubMat) || searchTemplates == null || searchTemplates.Count == 0)
            {
                return null;
            }

            using (Mat sourcePyramid = ResizeForPyramidProposal(imageSubMat, PyramidProposalScale))
            {
                if (OpenCvHelper.IsImageEmpty(sourcePyramid))
                {
                    return null;
                }

                ConcurrentBag<MatchingResult> verifiedCandidates = new ConcurrentBag<MatchingResult>();
                Parallel.ForEach(searchTemplates, searchTemplate =>
                {
                    using (Mat templatePyramid = ResizeForPyramidProposal(searchTemplate.WorkingTemplate, PyramidProposalScale))
                    {
                        if (OpenCvHelper.IsImageEmpty(templatePyramid)
                            || templatePyramid.Width > sourcePyramid.Width
                            || templatePyramid.Height > sourcePyramid.Height)
                        {
                            return;
                        }

                        foreach (PyramidPositionProposal proposal in FindPyramidPositionProposals(sourcePyramid, templatePyramid))
                        {
                            MatchingResult verified = VerifyPyramidPositionProposal(
                                imageSubMat,
                                searchTemplate.WorkingTemplate,
                                searchTemplate.Scale,
                                proposal,
                                roi,
                                applyRoiOffset);
                            if (verified != null)
                            {
                                verifiedCandidates.Add(verified);
                            }
                        }
                    }
                });

                return verifiedCandidates.OrderByDescending(r => r.Score).FirstOrDefault();
            }
        }

        private List<PyramidPositionProposal> FindPyramidPositionProposals(Mat sourcePyramid, Mat templatePyramid)
        {
            List<PyramidPositionProposal> proposals = new List<PyramidPositionProposal>();
            int topN = Math.Max(1, property.PYRAMID_POSITION_TOP_N);
            double minScore = Clamp01(property.PYRAMID_POSITION_MIN_SCORE);
            using (Mat imageMatching = new Mat())
            {
                Cv2.MatchTemplate(sourcePyramid, templatePyramid, imageMatching, property.MATCH_MODE, null);
                for (int attempt = 0; attempt < topN; attempt++)
                {
                    if (!TryGetBestMatch(imageMatching, minScore, out OpenCvSharp.Point matchLocation, out double qualityScore))
                    {
                        break;
                    }

                    proposals.Add(new PyramidPositionProposal(matchLocation, qualityScore));
                    SuppressMatchingScore(imageMatching, templatePyramid, matchLocation, property.MATCH_MODE);
                }
            }

            return proposals;
        }

        private MatchingResult VerifyPyramidPositionProposal(
            Mat imageSubMat,
            Mat template,
            double scale,
            PyramidPositionProposal proposal,
            Rect roi,
            bool applyRoiOffset)
        {
            int guessX = (int)Math.Round(proposal.Location.X / PyramidProposalScale);
            int guessY = (int)Math.Round(proposal.Location.Y / PyramidProposalScale);
            Rect searchRect = CreateProposalVerificationRect(imageSubMat, template, guessX, guessY);
            if (searchRect.Width < template.Width || searchRect.Height < template.Height)
            {
                return null;
            }

            using (Mat searchImage = imageSubMat.SubMat(searchRect))
            using (Mat imageMatching = new Mat())
            {
                Cv2.MatchTemplate(searchImage, template, imageMatching, property.MATCH_MODE, null);
                if (!TryGetBestMatch(imageMatching, property.SCORE_MIN, out OpenCvSharp.Point matchLocation, out double qualityScore))
                {
                    return null;
                }

                OpenCvSharp.Point ptStart = new OpenCvSharp.Point(searchRect.X + matchLocation.X, searchRect.Y + matchLocation.Y);
                if (ShouldValidateCandidateByImageDifference(property.MATCH_MODE)
                    && !IsValidMatchingCandidate(imageSubMat, template, ptStart))
                {
                    return null;
                }

                OpenCvSharp.Point ptEnd = new OpenCvSharp.Point(ptStart.X + template.Width, ptStart.Y + template.Height);
                Rect2f bounding = applyRoiOffset
                    ? new Rect2f(ptStart.X + roi.X, ptStart.Y + roi.Y, ptEnd.X - ptStart.X, ptEnd.Y - ptStart.Y)
                    : new Rect2f(ptStart.X, ptStart.Y, ptEnd.X - ptStart.X, ptEnd.Y - ptStart.Y);
                OpenCvSharp.Point2f center = new OpenCvSharp.Point2f(
                    (bounding.X + (bounding.X + bounding.Width)) / 2,
                    (bounding.Y + (bounding.Y + bounding.Height)) / 2);

                return new MatchingResult(0, qualityScore * 100.0D, center, bounding, 0D, scale);
            }
        }

        private static Rect CreateProposalVerificationRect(Mat source, Mat template, int guessX, int guessY)
        {
            int paddingX = Math.Max(8, template.Width / 4);
            int paddingY = Math.Max(8, template.Height / 4);
            return ClampRectToImage(
                new Rect(
                    guessX - paddingX,
                    guessY - paddingY,
                    template.Width + (paddingX * 2),
                    template.Height + (paddingY * 2)),
                source);
        }

        private static Mat ResizeForPyramidProposal(Mat image, double scale)
        {
            if (OpenCvHelper.IsImageEmpty(image))
            {
                return new Mat();
            }

            Mat resized = new Mat();
            Cv2.Resize(
                image,
                resized,
                new OpenCvSharp.Size(
                    Math.Max(1, (int)Math.Round(image.Width * scale)),
                    Math.Max(1, (int)Math.Round(image.Height * scale))),
                0D,
                0D,
                InterpolationFlags.Area);
            return resized;
        }

        private MatchingResult FindBestMatchingCandidate(Mat imageSubMat, Mat imageTpl, double scale, Rect roi, bool applyRoiOffset)
        {
            if (property.USE_FIND_ANGLE
                && property.USE_COARSE_TO_FINE_ANGLE_SEARCH
                && property.COARSE_ANGLE_STEP > property.FIND_ANGLE)
            {
                return FindBestMatchingCandidateCoarseToFine(imageSubMat, imageTpl, scale, roi, applyRoiOffset);
            }

            return FindBestMatchingCandidateExhaustive(imageSubMat, imageTpl, scale, roi, applyRoiOffset);
        }

        private MatchingResult FindBestMatchingCandidateExhaustive(Mat imageSubMat, Mat imageTpl, double scale, Rect roi, bool applyRoiOffset)
        {
            ConcurrentBag<MatchingResult> candidates = new ConcurrentBag<MatchingResult>();
            double angleStep = property.FIND_ANGLE;
            bool useRotatedTemplateCache = RotatedTemplateCache.ShouldUse(imageTpl);
            ulong templateHash = useRotatedTemplateCache ? RotatedTemplateCache.ComputeContentHash(imageTpl) : 0UL;

            Task firstTask = Task.Run(() =>
            {
                FindTemplate(imageSubMat, imageTpl, candidates, 0, scale, roi, applyRoiOffset, property.SCORE_MIN);
            });

            if (property.USE_FIND_ANGLE)
            {
                Task plusTask = Task.Run(() =>
                {
                    Parallel.ForEach(CreatePositiveSearchAngles(angleStep), angle =>
                    {
                        using (RotatedTemplateLease rotated = GetOrCreateRotatedTemplate(imageTpl, angle, templateHash, useRotatedTemplateCache))
                        {
                            FindTemplate(imageSubMat, rotated.Image, candidates, angle, scale, roi, applyRoiOffset, property.SCORE_MIN);
                        }
                    });
                });

                Task minusTask = Task.Run(() =>
                {
                    Parallel.ForEach(CreateNegativeSearchAngles(angleStep), angle =>
                    {
                        using (RotatedTemplateLease rotated = GetOrCreateRotatedTemplate(imageTpl, angle, templateHash, useRotatedTemplateCache))
                        {
                            FindTemplate(imageSubMat, rotated.Image, candidates, angle, scale, roi, applyRoiOffset, property.SCORE_MIN);
                        }
                    });
                });

                Task.WaitAll(plusTask);
                Task.WaitAll(minusTask);
            }

            Task.WaitAll(firstTask);
            return candidates.OrderByDescending(r => r.Score).FirstOrDefault();
        }

        private MatchingResult FindBestMatchingCandidateCoarseToFine(Mat imageSubMat, Mat imageTpl, double scale, Rect roi, bool applyRoiOffset)
        {
            ConcurrentBag<MatchingResult> coarseCandidates = new ConcurrentBag<MatchingResult>();
            double coarseStep = Math.Max(property.FIND_ANGLE, property.COARSE_ANGLE_STEP);
            FindTemplatesForAngles(
                imageSubMat,
                imageTpl,
                scale,
                coarseCandidates,
                CreateSearchAngles(coarseStep),
                roi,
                applyRoiOffset,
                0D);

            MatchingResult[] coarseBest = coarseCandidates
                .OrderByDescending(r => r.Score)
                .Take(Math.Max(1, property.COARSE_ANGLE_TOP_K))
                .ToArray();

            if (coarseBest.Length == 0)
            {
                return null;
            }

            ConcurrentBag<MatchingResult> fineCandidates = new ConcurrentBag<MatchingResult>();
            HashSet<double> fineAngles = new HashSet<double>();
            foreach (MatchingResult candidate in coarseBest)
            {
                foreach (double angle in CreateSearchAnglesAround(candidate.Angle, coarseStep, property.FIND_ANGLE))
                {
                    fineAngles.Add(angle);
                }
            }

            FindTemplatesForAngles(
                imageSubMat,
                imageTpl,
                scale,
                fineCandidates,
                fineAngles,
                roi,
                applyRoiOffset,
                property.SCORE_MIN);

            return fineCandidates.OrderByDescending(r => r.Score).FirstOrDefault();
        }

        private void FindTemplatesForAngles(
            Mat imageSubMat,
            Mat imageTpl,
            double scale,
            ConcurrentBag<MatchingResult> candidates,
            IEnumerable<double> angles,
            Rect roi,
            bool applyRoiOffset,
            double minimumScore)
        {
            bool useRotatedTemplateCache = RotatedTemplateCache.ShouldUse(imageTpl);
            ulong templateHash = useRotatedTemplateCache ? RotatedTemplateCache.ComputeContentHash(imageTpl) : 0UL;
            Parallel.ForEach(angles, angle =>
            {
                if (Math.Abs(angle) < 0.000001D)
                {
                    FindTemplate(imageSubMat, imageTpl, candidates, 0D, scale, roi, applyRoiOffset, minimumScore);
                    return;
                }

                using (RotatedTemplateLease rotated = GetOrCreateRotatedTemplate(imageTpl, angle, templateHash, useRotatedTemplateCache))
                {
                    FindTemplate(imageSubMat, rotated.Image, candidates, angle, scale, roi, applyRoiOffset, minimumScore);
                }
            });
        }

        private IEnumerable<double> CreateSearchAngles(double angleStep)
        {
            HashSet<double> emitted = new HashSet<double>();
            foreach (double angle in CreateSearchAnglesInRange(property.FIND_ANGLE_MIN, property.FIND_ANGLE_MAX, angleStep))
            {
                if (emitted.Add(angle))
                {
                    yield return angle;
                }
            }

            if (property.FIND_ANGLE_MIN <= 0 && property.FIND_ANGLE_MAX >= 0 && emitted.Add(0D))
            {
                yield return 0D;
            }
        }

        private IEnumerable<double> CreateSearchAnglesAround(double centerAngle, double radius, double angleStep)
        {
            double minAngle = Math.Max(property.FIND_ANGLE_MIN, centerAngle - radius);
            double maxAngle = Math.Min(property.FIND_ANGLE_MAX, centerAngle + radius);
            foreach (double angle in CreateSearchAnglesInRange(minAngle, maxAngle, angleStep))
            {
                yield return angle;
            }
        }

        private static IEnumerable<double> CreateSearchAnglesInRange(double minAngle, double maxAngle, double angleStep)
        {
            if (angleStep <= 0)
            {
                yield break;
            }

            int start = (int)Math.Ceiling(minAngle / angleStep);
            int end = (int)Math.Floor(maxAngle / angleStep);
            for (int i = start; i <= end; i++)
            {
                double angle = Math.Round(angleStep * i, 6);
                yield return Math.Abs(angle) < 0.000001D ? 0D : angle;
            }
        }

        private IEnumerable<double> CreatePositiveSearchAngles(double angleStep)
        {
            int count = (int)(property.FIND_ANGLE_MAX / angleStep);
            for (int i = 1; i <= count; i++)
            {
                yield return angleStep * i;
            }
        }

        private IEnumerable<double> CreateNegativeSearchAngles(double angleStep)
        {
            int count = Math.Abs((int)(property.FIND_ANGLE_MIN / angleStep));
            for (int i = 1; i <= count; i++)
            {
                yield return angleStep * i * -1;
            }
        }

        public IEnumerable<double> CreateSearchScales()
        {
            if (!property.USE_FIND_SCALE)
            {
                yield return 1D;
                yield break;
            }

            foreach (double scale in CreateSearchScalesInRange(
                property.FIND_SCALE_MIN,
                property.FIND_SCALE_MAX,
                property.FIND_SCALE_STEP))
            {
                yield return scale;
            }
        }

        private static IEnumerable<double> CreateSearchScalesInRange(double minScale, double maxScale, double scaleStep)
        {
            if (scaleStep <= 0D)
            {
                yield break;
            }

            if (minScale > maxScale)
            {
                double temp = minScale;
                minScale = maxScale;
                maxScale = temp;
            }

            HashSet<double> emitted = new HashSet<double>();
            int start = (int)Math.Ceiling((minScale - 0.000000001D) / scaleStep);
            int end = (int)Math.Floor((maxScale + 0.000000001D) / scaleStep);
            for (int i = start; i <= end; i++)
            {
                double scale = NormalizeScale(i * scaleStep);
                if (scale > 0D && emitted.Add(scale))
                {
                    yield return scale;
                }
            }

            if (minScale <= 1D && maxScale >= 1D && emitted.Add(1D))
            {
                yield return 1D;
            }

            if (emitted.Count == 0)
            {
                yield return NormalizeScale(minScale);
            }
        }

        private static double NormalizeScale(double scale)
        {
            return scale <= 0D ? 1D : Math.Round(scale, 6);
        }

        private string FormatMatchingRoi()
        {
            if (property.USE_MULTI_ROI)
            {
                return $"Multi({property.CvROIS?.Count ?? 0})";
            }

            Rect roi = property.USE_ROI && property.CvROI.Width > 0 && property.CvROI.Height > 0
                ? property.CvROI
                : new Rect(0, 0, imageSource.Width, imageSource.Height);
            return $"{roi.X},{roi.Y},{roi.Width},{roi.Height}";
        }

        public string FormatMatchingOptions()
        {
            return $"Mode={property.MATCH_MODE}, ScoreMin={property.SCORE_MIN}, NumMatch={property.NUM_MATCH}, Magnification={property.MAGNIFIATION}, AngleSearch={property.USE_FIND_ANGLE}, Scale={FormatScaleOptions()}, Pyramid={FormatPyramidOptions()}, Threshold={property.USE_THRESHOLD}, Adaptive={property.USE_ADAPTIVE_THRESHOLD}, Canny={property.USE_CANNY}, ROI={FormatMatchingRoi()}";
        }

        public string FormatScaleOptions()
        {
            if (!property.USE_FIND_SCALE)
            {
                return "Off";
            }

            double minScale = Math.Min(property.FIND_SCALE_MIN, property.FIND_SCALE_MAX);
            double maxScale = Math.Max(property.FIND_SCALE_MIN, property.FIND_SCALE_MAX);
            return $"{minScale:0.###}..{maxScale:0.###}/Step={property.FIND_SCALE_STEP:0.###}";
        }

        private string FormatPyramidOptions()
        {
            if (!property.USE_PYRAMID_POSITION_PROPOSAL)
            {
                return "Off";
            }

            string availability = property.USE_FIND_ANGLE ? "/FallbackWhenAngleOn" : string.Empty;
            return $"Top={property.PYRAMID_POSITION_TOP_N}/Min={property.PYRAMID_POSITION_MIN_SCORE:0.###}{availability}";
        }

        private MatchingResult TryRefineMatchingResult(Mat imageSrc, Mat preparedTemplate, Rect roi, MatchingResult highestScoreResult, bool applyRoiOffset)
        {
            if (CanUseCoarseMatchAsFinalResult(highestScoreResult))
            {
                SuppressCoarseMatchRegion(imageSrc, preparedTemplate, roi, highestScoreResult, applyRoiOffset);
                return highestScoreResult;
            }

            int originalMagnification = 2;
            Rect refineRect = GetRefineSearchRect(imageSrc, preparedTemplate, roi, highestScoreResult, applyRoiOffset);
            using (Mat refineImage = imageSrc.SubMat(refineRect))
            using (Mat imageSubMatPy = refineImage.Resize(new OpenCvSharp.Size((int)(refineImage.Width / originalMagnification), (int)(refineImage.Height / originalMagnification))))
            using (Mat imageTplPy = preparedTemplate.Resize(new OpenCvSharp.Size((int)(preparedTemplate.Width / originalMagnification), (int)(preparedTemplate.Height / originalMagnification))))
            using (RotatedTemplateLease rotatedTemplate = CreateRefineTemplateLease(imageTplPy, highestScoreResult.Angle))
            using (Mat imageMatching = new Mat())
            {
                Cv2.MatchTemplate(imageSubMatPy, rotatedTemplate.Image, imageMatching, property.MATCH_MODE, null);
                if (!TryGetBestMatch(imageMatching, out OpenCvSharp.Point ptMaxLocation, out double qualityScore))
                {
                    return null;
                }

                OpenCvSharp.Point ptStart = new OpenCvSharp.Point(ptMaxLocation.X, ptMaxLocation.Y);
                OpenCvSharp.Point ptEnd = new OpenCvSharp.Point(ptMaxLocation.X + imageTplPy.Width, ptMaxLocation.Y + imageTplPy.Height);

                ptMaxLocation.X = (int)(ptMaxLocation.X * originalMagnification);
                ptMaxLocation.Y = (int)(ptMaxLocation.Y * originalMagnification);
                ptMaxLocation.X += refineRect.X;
                ptMaxLocation.Y += refineRect.Y;

                Rect maskRect = ClampRectToImage(
                    new Rect(
                        new OpenCvSharp.Point(ptMaxLocation.X - 5, ptMaxLocation.Y - 5),
                        new OpenCvSharp.Size(preparedTemplate.Width + 10, preparedTemplate.Height + 10)),
                    imageSrc);
                if (maskRect.Width > 0 && maskRect.Height > 0)
                {
                    SuppressMatchedRegion(imageSrc, maskRect);
                }

                ptStart.X *= originalMagnification;
                ptStart.Y *= originalMagnification;
                ptEnd.X *= originalMagnification;
                ptEnd.Y *= originalMagnification;
                ptStart.X += refineRect.X;
                ptStart.Y += refineRect.Y;
                ptEnd.X += refineRect.X;
                ptEnd.Y += refineRect.Y;

                if (ShouldValidateCandidateByImageDifference(property.MATCH_MODE)
                    && !IsValidMatchingCandidate(imageSrc, preparedTemplate, ptStart))
                {
                    Rect invalidRect = ClampRectToImage(
                        new Rect(ptStart, new OpenCvSharp.Size(preparedTemplate.Width, preparedTemplate.Height)),
                        imageSrc);
                    if (invalidRect.Width > 0 && invalidRect.Height > 0)
                    {
                        SuppressMatchedRegion(imageSrc, invalidRect);
                    }

                    return null;
                }

                double refinedScore = qualityScore * 100.0D;
                Rect2f bounding = applyRoiOffset
                    ? new Rect2f(ptStart.X + roi.X, ptStart.Y + roi.Y, ptEnd.X - ptStart.X, ptEnd.Y - ptStart.Y)
                    : new Rect2f(ptStart.X, ptStart.Y, ptEnd.X - ptStart.X, ptEnd.Y - ptStart.Y);
                OpenCvSharp.Point2f center = new OpenCvSharp.Point2f(
                    (bounding.X + (bounding.X + bounding.Width)) / 2,
                    (bounding.Y + (bounding.Y + bounding.Height)) / 2);

                return new MatchingResult(0, refinedScore, center, bounding, highestScoreResult.Angle, highestScoreResult.Scale);
            }
        }

        private RotatedTemplateLease CreateRefineTemplateLease(Mat template, double angle)
        {
            if (Math.Abs(angle) < 0.000001D)
            {
                return RotatedTemplateLease.CreateUncached(template.Clone());
            }

            bool useRotatedTemplateCache = RotatedTemplateCache.ShouldUse(template);
            ulong templateHash = useRotatedTemplateCache ? RotatedTemplateCache.ComputeContentHash(template) : 0UL;
            return GetOrCreateRotatedTemplate(template, angle, templateHash, useRotatedTemplateCache);
        }

        private bool CanUseCoarseMatchAsFinalResult(MatchingResult result)
        {
            return result != null
                && IsLowerScoreBetter(property.MATCH_MODE)
                && Math.Abs(property.MAGNIFIATION - 1.0) < 0.0001
                && Math.Abs(result.Angle) < 0.0001;
        }

        private static void SuppressCoarseMatchRegion(Mat imageSrc, Mat preparedTemplate, Rect roi, MatchingResult result, bool applyRoiOffset)
        {
            int x = (int)Math.Round(result.Bounding.X);
            int y = (int)Math.Round(result.Bounding.Y);
            if (applyRoiOffset)
            {
                x -= roi.X;
                y -= roi.Y;
            }

            Rect maskRect = ClampRectToImage(
                new Rect(
                    new OpenCvSharp.Point(x - 5, y - 5),
                    new OpenCvSharp.Size(preparedTemplate.Width + 10, preparedTemplate.Height + 10)),
                imageSrc);
            if (maskRect.Width > 0 && maskRect.Height > 0)
            {
                SuppressMatchedRegion(imageSrc, maskRect);
            }
        }

        private Rect GetRefineSearchRect(Mat imageSrc, Mat preparedTemplate, Rect roi, MatchingResult coarseResult, bool applyRoiOffset)
        {
            float coarseX = coarseResult.Bounding.X;
            float coarseY = coarseResult.Bounding.Y;
            if (applyRoiOffset)
            {
                coarseX -= roi.X;
                coarseY -= roi.Y;
            }

            int left = (int)Math.Round(coarseX * property.MAGNIFIATION) - preparedTemplate.Width / 2;
            int top = (int)Math.Round(coarseY * property.MAGNIFIATION) - preparedTemplate.Height / 2;
            Rect searchRect = new Rect(
                left,
                top,
                preparedTemplate.Width * 2,
                preparedTemplate.Height * 2);

            Rect clamped = ClampRectToImage(searchRect, imageSrc);
            if (clamped.Width >= preparedTemplate.Width && clamped.Height >= preparedTemplate.Height)
            {
                return clamped;
            }

            return new Rect(0, 0, imageSrc.Width, imageSrc.Height);
        }

        private static void SuppressMatchedRegion(Mat image, Rect maskRect)
        {
            if (image == null || image.Empty())
            {
                return;
            }

            using (Mat patch = image.SubMat(maskRect))
            {
                patch.SetTo(Cv2.Mean(image));

                for (int y = 0; y < patch.Height; y += 4)
                {
                    Scalar color = ((y / 4) % 2 == 0) ? Scalar.Black : Scalar.White;
                    Cv2.Line(patch, new OpenCvSharp.Point(0, y), new OpenCvSharp.Point(patch.Width - 1, y), color, 1);
                }

                for (int x = 0; x < patch.Width; x += 4)
                {
                    Scalar color = ((x / 4) % 2 == 0) ? Scalar.White : Scalar.Black;
                    Cv2.Line(patch, new OpenCvSharp.Point(x, 0), new OpenCvSharp.Point(x, patch.Height - 1), color, 1);
                }
            }
        }

        private static Rect ClampRectToImage(Rect rect, Mat image)
        {
            if (image == null || image.Empty())
            {
                return new Rect();
            }

            int left = Math.Max(0, rect.X);
            int top = Math.Max(0, rect.Y);
            int right = Math.Min(image.Width, rect.X + rect.Width);
            int bottom = Math.Min(image.Height, rect.Y + rect.Height);
            if (right <= left || bottom <= top)
            {
                return new Rect();
            }

            return new Rect(left, top, right - left, bottom - top);
        }

        private bool AddFinalMatchingResult(MatchingResult result)
        {
            if (result == null)
            {
                return false;
            }

            if (IsDuplicateMatchingResult(result))
            {
                return false;
            }

            results.Add(new MatchingResult(
                results.Count + 1,
                result.Score,
                result.Center,
                CommonConverter.RectangleToRect(result.Bounding),
                result.Angle,
                result.Scale));
            return true;
        }

        private bool IsDuplicateMatchingResult(MatchingResult result)
        {
            double threshold = Math.Max(4, Math.Min(result.Bounding.Width, result.Bounding.Height) * 0.5);
            foreach (MatchingResult existing in results)
            {
                double dx = existing.Center.X - result.Center.X;
                double dy = existing.Center.Y - result.Center.Y;
                if (Math.Sqrt(dx * dx + dy * dy) < threshold)
                {
                    return true;
                }
            }

            return false;
        }

        private sealed class MatchingSearchTemplate : IDisposable
        {
            public MatchingSearchTemplate(double scale, Mat preparedTemplate, Mat workingTemplate)
            {
                Scale = NormalizeScale(scale);
                PreparedTemplate = preparedTemplate;
                WorkingTemplate = workingTemplate;
            }

            public double Scale { get; }

            public Mat PreparedTemplate { get; }

            public Mat WorkingTemplate { get; }

            public void Dispose()
            {
                WorkingTemplate?.Dispose();
                PreparedTemplate?.Dispose();
            }
        }

        private sealed class SearchTemplateCollection : IDisposable
        {
            private readonly List<MatchingSearchTemplate> items;

            public SearchTemplateCollection(List<MatchingSearchTemplate> items)
            {
                this.items = items ?? new List<MatchingSearchTemplate>();
            }

            public IReadOnlyList<MatchingSearchTemplate> Items => items;

            public int Count => items.Count;

            public MatchingSearchTemplate FindByScale(double scale)
            {
                double normalizedScale = NormalizeScale(scale);
                return items
                    .OrderBy(item => Math.Abs(item.Scale - normalizedScale))
                    .FirstOrDefault();
            }

            public void Dispose()
            {
                foreach (MatchingSearchTemplate item in items)
                {
                    item.Dispose();
                }

                items.Clear();
            }
        }

        private sealed class PyramidPositionProposal
        {
            public PyramidPositionProposal(OpenCvSharp.Point location, double score)
            {
                Location = location;
                Score = score;
            }

            public OpenCvSharp.Point Location { get; }

            public double Score { get; }
        }

        private sealed class RotatedTemplateCacheEntry
        {
            public RotatedTemplateCacheEntry(Mat image, long bytes, LinkedListNode<RotatedTemplateCacheKey> node)
            {
                Image = image;
                Bytes = bytes;
                Node = node;
            }

            public Mat Image { get; }

            public long Bytes { get; }

            public LinkedListNode<RotatedTemplateCacheKey> Node { get; }

            public int UseCount { get; set; }
        }

        private sealed class RotatedTemplateLease : IDisposable
        {
            private readonly RotatedTemplateCacheEntry entry;
            private readonly bool disposeImage;
            private bool disposed;

            private RotatedTemplateLease(Mat image, RotatedTemplateCacheEntry entry, bool disposeImage)
            {
                Image = image;
                this.entry = entry;
                this.disposeImage = disposeImage;
            }

            public Mat Image { get; }

            public static RotatedTemplateLease CreateCached(RotatedTemplateCacheEntry entry)
            {
                return new RotatedTemplateLease(entry?.Image, entry, false);
            }

            public static RotatedTemplateLease CreateUncached(Mat image)
            {
                return new RotatedTemplateLease(image, null, true);
            }

            public void Dispose()
            {
                if (disposed)
                {
                    return;
                }

                disposed = true;
                if (disposeImage)
                {
                    Image?.Dispose();
                    return;
                }

                RotatedTemplateCache.Release(entry);
            }
        }

        private sealed class RotatedTemplateCacheKey : IEquatable<RotatedTemplateCacheKey>
        {
            public RotatedTemplateCacheKey(
                ulong templateHash,
                int width,
                int height,
                int type,
                bool paddingWhite,
                double angle)
            {
                TemplateHash = templateHash;
                Width = width;
                Height = height;
                Type = type;
                PaddingWhite = paddingWhite;
                Angle = angle;
            }

            public ulong TemplateHash { get; }

            public int Width { get; }

            public int Height { get; }

            public int Type { get; }

            public bool PaddingWhite { get; }

            public double Angle { get; }

            public bool Equals(RotatedTemplateCacheKey other)
            {
                return other != null
                    && TemplateHash == other.TemplateHash
                    && Width == other.Width
                    && Height == other.Height
                    && Type == other.Type
                    && PaddingWhite == other.PaddingWhite
                    && Angle.Equals(other.Angle);
            }

            public override bool Equals(object obj)
            {
                return Equals(obj as RotatedTemplateCacheKey);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = 17;
                    hash = hash * 31 + TemplateHash.GetHashCode();
                    hash = hash * 31 + Width;
                    hash = hash * 31 + Height;
                    hash = hash * 31 + Type;
                    hash = hash * 31 + PaddingWhite.GetHashCode();
                    hash = hash * 31 + Angle.GetHashCode();
                    return hash;
                }
            }
        }
        }
    }

}


