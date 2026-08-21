using OpenVisionLab.Vision2D.Property;
using OpenVisionLab.Vision2D.Result;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;

namespace OpenVisionLab.Vision2D.Tool
{
    public sealed class AutoMPointTool : OpenCvAlgorithmBase
    {
        public IAutoMPointToolProperty property;
        public List<AutoMPointCandidateResult> results = new List<AutoMPointCandidateResult>();
        public List<AutoMPointCandidateResult> candidates = new List<AutoMPointCandidateResult>();

        private Rect analysisRoi;
        private int generatedCandidateCount;
        private int prefilterPassedCount;
        private int exactEvaluatedCount;
        private double analysisElapsedMilliseconds;
        private IReadOnlyList<Mat> representativeImages = Array.Empty<Mat>();
        private readonly AutoMPointCandidateAnalyzer candidateAnalyzer;

        public AutoMPointTool()
        {
            candidateAnalyzer = new AutoMPointCandidateAnalyzer(this);
        }

        public void SetProperty(IAutoMPointToolProperty property) => this.property = property;

        public VisionToolResult Execute(Mat source, IReadOnlyList<Mat> samples)
        {
            representativeImages = samples ?? Array.Empty<Mat>();
            try
            {
                return base.Execute(source);
            }
            finally
            {
                representativeImages = Array.Empty<Mat>();
            }
        }

        protected override bool TryValidateBeforeRun(out VisionToolErrorCode errorCode, out string message)
        {
            if (!base.TryValidateBeforeRun(out errorCode, out message))
            {
                return false;
            }

            analysisRoi = property.UseAnalysisRoi
                ? property.AnalysisRoi
                : new Rect(0, 0, imageSource.Width, imageSource.Height);

            if (!IsRectInsideImage(analysisRoi, imageSource))
            {
                errorCode = VisionToolErrorCode.AutoMPointInvalidRoi;
                message = $"Auto MPoint analysis ROI must be inside the source image. ROI={FormatRect(analysisRoi)}, Image={imageSource.Width}x{imageSource.Height}.";
                return false;
            }

            if (!Enum.IsDefined(typeof(AutoMPointCandidateMode), property.CandidateMode)
                || property.PatternWidth <= 0
                || property.PatternHeight <= 0
                || property.CandidateStride <= 0
                || property.MaximumFinalists <= 0
                || property.MaximumResults <= 0
                || property.MaximumResults > property.MaximumFinalists)
            {
                errorCode = VisionToolErrorCode.AutoMPointInvalidParameter;
                message = "Auto MPoint candidate mode, pattern size, stride, finalist count, and result count must be positive and consistent.";
                return false;
            }

            bool usesGrid = property.CandidateMode != AutoMPointCandidateMode.WholeAnalysisRoi;
            if (usesGrid
                && (property.PatternWidth > analysisRoi.Width || property.PatternHeight > analysisRoi.Height))
            {
                errorCode = VisionToolErrorCode.AutoMPointInvalidPatternSize;
                message = $"Auto MPoint pattern must fit inside the analysis ROI. Pattern={property.PatternWidth}x{property.PatternHeight}, ROI={FormatRect(analysisRoi)}.";
                return false;
            }

            if (!AreUnitIntervalValuesValid(
                    property.MaximumCandidateOverlap,
                    property.MinimumEdgeDensity,
                    property.MinimumQuadrantBalance,
                    property.MinimumOrientationBalance,
                    property.MinimumFeatureQuality,
                    property.MatchingMinimumScore,
                    property.MinimumUniquenessMargin,
                    property.MinimumSyntheticSuccessRate,
                    property.MinimumRepresentativeSuccessRate)
                || !AreFiniteNonNegative(
                    property.MinimumContrastStdDev,
                    property.MaximumPositionErrorPixels,
                    property.MaximumAngleErrorDegrees,
                    property.MaximumScaleErrorRatio,
                    property.MaximumRuntimeMilliseconds)
                || property.CannyLow < 0
                || property.CannyHigh <= property.CannyLow
                || property.MaximumTemplatePoints <= 0
                || property.SearchStep <= 0
                || property.SyntheticTranslationPixels < 0
                || property.MinimumRepresentativeImageCount <= 0)
            {
                errorCode = VisionToolErrorCode.AutoMPointInvalidParameter;
                message = "Auto MPoint feature, matching, synthetic, and runtime gates are invalid.";
                return false;
            }

            if (representativeImages.Count > 0)
            {
                if (representativeImages.Count < property.MinimumRepresentativeImageCount)
                {
                    errorCode = VisionToolErrorCode.AutoMPointRepresentativeImageInvalid;
                    message = $"Auto MPoint representative validation requires at least {property.MinimumRepresentativeImageCount} images. Actual={representativeImages.Count}.";
                    return false;
                }

                for (int index = 0; index < representativeImages.Count; index++)
                {
                    Mat sample = representativeImages[index];
                    if (OpenCvHelper.IsImageEmpty(sample)
                        || sample.Width != imageSource.Width
                        || sample.Height != imageSource.Height)
                    {
                        errorCode = VisionToolErrorCode.AutoMPointRepresentativeImageInvalid;
                        message = $"Auto MPoint representative image {index + 1} must be loaded and match the reference size {imageSource.Width}x{imageSource.Height}.";
                        return false;
                    }
                }
            }

            if (property.UseAngleSearch
                && (property.AngleStep <= 0d
                    || !IsFinite(property.AngleStep)
                    || property.AngleMinimum > property.AngleMaximum
                    || property.AngleMinimum > 0
                    || property.AngleMaximum < 0))
            {
                errorCode = VisionToolErrorCode.AutoMPointInvalidParameter;
                message = "Auto MPoint angle search must include zero and use a positive finite step.";
                return false;
            }

            if (property.UseScaleSearch
                && (!AreFinitePositive(property.ScaleMinimum, property.ScaleMaximum, property.ScaleStep)
                    || property.ScaleMinimum > 1d
                    || property.ScaleMaximum < 1d
                    || property.ScaleMinimum > property.ScaleMaximum))
            {
                errorCode = VisionToolErrorCode.AutoMPointInvalidParameter;
                message = "Auto MPoint scale search must include 1.0 and use positive finite values.";
                return false;
            }

            errorCode = VisionToolErrorCode.None;
            message = string.Empty;
            return true;
        }

        protected override bool TryValidateAfterRun(out VisionToolErrorCode errorCode, out string message)
        {
            if (results.Count == 0)
            {
                errorCode = VisionToolErrorCode.AutoMPointNoCandidate;
                string strongestReason = candidates
                    .OrderByDescending(candidate => candidate.FeatureQuality)
                    .Select(candidate => candidate.RejectReason)
                    .FirstOrDefault(reason => !string.IsNullOrWhiteSpace(reason))
                    ?? "No candidate passed the configured gates.";
                message = $"Auto MPoint found no accepted suggestion. Generated={generatedCandidateCount}, PrefilterPassed={prefilterPassedCount}, ExactEvaluated={exactEvaluatedCount}. {strongestReason}";
                return false;
            }

            errorCode = VisionToolErrorCode.None;
            message = string.Empty;
            return true;
        }

        public override void Run()
        {
            candidateAnalyzer.Run();
        }

        protected override IDictionary<string, double> CollectMetrics()
        {
            IDictionary<string, double> metrics = base.CollectMetrics();
            metrics["AutoMPoint.GeneratedCandidateCount"] = generatedCandidateCount;
            metrics["AutoMPoint.PrefilterPassedCount"] = prefilterPassedCount;
            metrics["AutoMPoint.ExactEvaluatedCount"] = exactEvaluatedCount;
            metrics["AutoMPoint.AcceptedCandidateCount"] = results.Count;
            metrics["AutoMPoint.AnalysisElapsedMs"] = analysisElapsedMilliseconds;
            metrics["AutoMPoint.RepresentativeImageCount"] = representativeImages.Count;

            AutoMPointCandidateResult best = results.FirstOrDefault();
            if (best != null)
            {
                metrics["AutoMPoint.BestScore"] = best.Score;
                metrics["AutoMPoint.BestUniquenessMargin"] = best.UniquenessMargin;
                metrics["AutoMPoint.BestPositionErrorMaxPx"] = best.PositionErrorMaxPixels;
                metrics["AutoMPoint.BestRuntimeP95Ms"] = best.RuntimeP95Milliseconds;
                metrics["AutoMPoint.BestRepresentativeSuccessRate"] = best.RepresentativeSuccessRate;
                metrics["AutoMPoint.BestRepresentativeMeanScore"] = best.RepresentativeMeanScore;
                metrics["AutoMPoint.BestRepresentativeMinimumUniquenessMargin"] =
                    best.RepresentativeMinimumUniquenessMargin;
            }

            return metrics;
        }

        protected override IEnumerable<VisionToolOverlay> CollectOverlays()
        {
            List<VisionToolOverlay> overlays = new List<VisionToolOverlay>();
            foreach (AutoMPointCandidateResult candidate in results)
            {
                overlays.Add(new VisionToolOverlay
                {
                    Kind = VisionToolOverlayKind.Rectangle,
                    Bounds = candidate.Bounding,
                    Label = candidate.RepresentativeImageCount > 0
                        ? $"Auto MPoint #{candidate.Rank} R:{candidate.RepresentativeSuccessCount}/{candidate.RepresentativeImageCount} S:{candidate.RepresentativeMeanScore:0.0}"
                        : $"Auto MPoint #{candidate.Rank} S:{candidate.Score:0.0} U:{candidate.UniquenessMargin:0.000}"
                });
                overlays.Add(new VisionToolOverlay
                {
                    Kind = VisionToolOverlayKind.Point,
                    Center = new PointF(candidate.PatternCenter.X, candidate.PatternCenter.Y),
                    Label = $"MPoint #{candidate.Rank}"
                });
            }

            return overlays;
        }

        private sealed class AutoMPointCandidateAnalyzer
    {
        private readonly AutoMPointTool owner;

        public AutoMPointCandidateAnalyzer(AutoMPointTool owner)
        {
            this.owner = owner;
        }

        private IAutoMPointToolProperty property => owner.property;

        private List<AutoMPointCandidateResult> results => owner.results;

        private List<AutoMPointCandidateResult> candidates => owner.candidates;

        private Rect analysisRoi => owner.analysisRoi;

        private int generatedCandidateCount { get => owner.generatedCandidateCount; set => owner.generatedCandidateCount = value; }

        private int prefilterPassedCount { get => owner.prefilterPassedCount; set => owner.prefilterPassedCount = value; }

        private int exactEvaluatedCount { get => owner.exactEvaluatedCount; set => owner.exactEvaluatedCount = value; }

        private double analysisElapsedMilliseconds { get => owner.analysisElapsedMilliseconds; set => owner.analysisElapsedMilliseconds = value; }

        private IReadOnlyList<Mat> representativeImages => owner.representativeImages;

        private Mat imageSource => owner.imageSource;

        private Mat imageResult => owner.imageResult;

        private Stopwatch swTaktTimems => owner.swTaktTimems;

        private void ReplaceResultImage(Mat result) => owner.ReplaceResultImage(result);

        public void Run()
        {
            Stopwatch analysisStopwatch = Stopwatch.StartNew();
            results.Clear();
            candidates.Clear();
            generatedCandidateCount = 0;
            prefilterPassedCount = 0;
            exactEvaluatedCount = 0;

            using (Mat gray = CreateGrayImage(imageSource))
            using (Mat edges = new Mat())
            using (Mat gradientX = new Mat())
            using (Mat gradientY = new Mat())
            using (Mat absoluteGradientX = new Mat())
            using (Mat absoluteGradientY = new Mat())
            {
                Cv2.Canny(gray, edges, property.CannyLow, property.CannyHigh, 3, true);
                Cv2.Sobel(gray, gradientX, MatType.CV_32FC1, 1, 0, 3);
                Cv2.Sobel(gray, gradientY, MatType.CV_32FC1, 0, 1, 3);
                Cv2.ConvertScaleAbs(gradientX, absoluteGradientX);
                Cv2.ConvertScaleAbs(gradientY, absoluteGradientY);

                List<FeatureCandidate> scored = GenerateCandidateRects()
                    .Select(candidate => ScoreFeatureCandidate(
                        candidate.Rect,
                        candidate.IsWholeAnalysisRoi,
                        gray,
                        edges,
                        absoluteGradientX,
                        absoluteGradientY))
                    .ToList();
                generatedCandidateCount = scored.Count;

                List<FeatureCandidate> passed = scored
                    .Where(candidate => string.IsNullOrWhiteSpace(candidate.RejectReason))
                    .OrderByDescending(candidate => candidate.FeatureQuality)
                    .ThenBy(candidate => candidate.Rect.Y)
                    .ThenBy(candidate => candidate.Rect.X)
                    .ToList();
                prefilterPassedCount = passed.Count;

                List<FeatureCandidate> finalists = SelectNonOverlappingFinalists(passed);
                EdgeBasedTemplateMatchingTool matcher = CreateMatcherLease();
                try
                {
                    foreach (FeatureCandidate finalist in finalists)
                    {
                        AutoMPointCandidateResult candidate = EvaluateCandidate(gray, finalist, matcher);
                        candidates.Add(candidate);
                    }

                    if (representativeImages.Count > 0)
                    {
                        foreach (AutoMPointCandidateResult candidate in candidates.Where(item => item.Accepted))
                        {
                            EvaluateRepresentativeImages(gray, candidate, matcher);
                        }
                    }
                }
                finally
                {
                    ReleaseMatcher(matcher);
                }

                exactEvaluatedCount = candidates.Count;
                List<AutoMPointCandidateResult> accepted = candidates
                    .Where(candidate => candidate.Accepted)
                    .OrderByDescending(candidate => representativeImages.Count > 0
                        ? candidate.RepresentativeSuccessRate
                        : candidate.Score)
                    .ThenByDescending(candidate => candidate.RepresentativeMinimumUniquenessMargin)
                    .ThenByDescending(candidate => candidate.RepresentativeMeanScore)
                    .ThenByDescending(candidate => candidate.Score)
                    .ThenBy(candidate => candidate.PatternRoi.Y)
                    .ThenBy(candidate => candidate.PatternRoi.X)
                    .Take(property.MaximumResults)
                    .ToList();

                for (int index = 0; index < accepted.Count; index++)
                {
                    accepted[index].Rank = index + 1;
                    accepted[index].Index = index + 1;
                }

                results.AddRange(accepted);
                DrawResult();
            }

            analysisStopwatch.Stop();
            analysisElapsedMilliseconds = analysisStopwatch.Elapsed.TotalMilliseconds;
            swTaktTimems.Restart();
            swTaktTimems.Stop();
        }

        private List<CandidateRect> GenerateCandidateRects()
        {
            List<CandidateRect> output = new List<CandidateRect>();
            if (property.CandidateMode != AutoMPointCandidateMode.WholeAnalysisRoi)
            {
                List<int> xs = CreateAxisPositions(
                    analysisRoi.X,
                    analysisRoi.Right - property.PatternWidth,
                    property.CandidateStride);
                List<int> ys = CreateAxisPositions(
                    analysisRoi.Y,
                    analysisRoi.Bottom - property.PatternHeight,
                    property.CandidateStride);
                foreach (int y in ys)
                {
                    foreach (int x in xs)
                    {
                        output.Add(new CandidateRect(
                            new Rect(x, y, property.PatternWidth, property.PatternHeight),
                            false));
                    }
                }
            }

            if (property.CandidateMode != AutoMPointCandidateMode.Grid)
            {
                output.Add(new CandidateRect(analysisRoi, true));
            }

            return output;
        }

        private FeatureCandidate ScoreFeatureCandidate(
            Rect rect,
            bool isWholeAnalysisRoi,
            Mat gray,
            Mat edges,
            Mat absoluteGradientX,
            Mat absoluteGradientY)
        {
            FeatureCandidate candidate = new FeatureCandidate(rect, isWholeAnalysisRoi);
            using (Mat grayRoi = gray.SubMat(rect))
            using (Mat edgeRoi = edges.SubMat(rect))
            using (Mat gradientXRoi = absoluteGradientX.SubMat(rect))
            using (Mat gradientYRoi = absoluteGradientY.SubMat(rect))
            {
                Cv2.MeanStdDev(grayRoi, out _, out Scalar deviation);
                candidate.ContrastStdDev = deviation.Val0;
                candidate.EdgeDensity = Cv2.CountNonZero(edgeRoi) / (double)(rect.Width * rect.Height);
                candidate.QuadrantBalance = CalculateQuadrantBalance(edgeRoi);

                double gradientXSum = Cv2.Sum(gradientXRoi).Val0;
                double gradientYSum = Cv2.Sum(gradientYRoi).Val0;
                double maximumGradient = Math.Max(gradientXSum, gradientYSum);
                candidate.OrientationBalance = maximumGradient > 0d
                    ? Math.Min(gradientXSum, gradientYSum) / maximumGradient
                    : 0d;
            }

            double contrastScore = Clamp01(candidate.ContrastStdDev / 48d);
            double edgeScore = Clamp01(candidate.EdgeDensity / 0.08d);
            candidate.FeatureQuality = GeometricMean(
                contrastScore,
                edgeScore,
                Clamp01(candidate.QuadrantBalance),
                Clamp01(candidate.OrientationBalance));

            if (candidate.ContrastStdDev < property.MinimumContrastStdDev)
            {
                candidate.RejectReason = $"ContrastStdDev {candidate.ContrastStdDev:0.###} < {property.MinimumContrastStdDev:0.###}.";
            }
            else if (candidate.EdgeDensity < property.MinimumEdgeDensity)
            {
                candidate.RejectReason = $"EdgeDensity {candidate.EdgeDensity:0.####} < {property.MinimumEdgeDensity:0.####}.";
            }
            else if (candidate.QuadrantBalance < property.MinimumQuadrantBalance)
            {
                candidate.RejectReason = $"QuadrantBalance {candidate.QuadrantBalance:0.####} < {property.MinimumQuadrantBalance:0.####}.";
            }
            else if (candidate.OrientationBalance < property.MinimumOrientationBalance)
            {
                candidate.RejectReason = $"OrientationBalance {candidate.OrientationBalance:0.####} < {property.MinimumOrientationBalance:0.####}.";
            }
            else if (candidate.FeatureQuality < property.MinimumFeatureQuality)
            {
                candidate.RejectReason = $"FeatureQuality {candidate.FeatureQuality:0.####} < {property.MinimumFeatureQuality:0.####}.";
            }

            return candidate;
        }

        private List<FeatureCandidate> SelectNonOverlappingFinalists(List<FeatureCandidate> passed)
        {
            List<FeatureCandidate> selected = new List<FeatureCandidate>();
            foreach (FeatureCandidate candidate in passed)
            {
                bool overlaps = selected.Any(existing =>
                    candidate.IsWholeAnalysisRoi == existing.IsWholeAnalysisRoi
                    && CalculateIntersectionOverUnion(candidate.Rect, existing.Rect) > property.MaximumCandidateOverlap);
                if (overlaps)
                {
                    continue;
                }

                selected.Add(candidate);
                if (selected.Count >= property.MaximumFinalists)
                {
                    break;
                }
            }

            return selected;
        }

        private AutoMPointCandidateResult EvaluateCandidate(
            Mat gray,
            FeatureCandidate feature,
            EdgeBasedTemplateMatchingTool matcher)
        {
            AutoMPointCandidateResult candidate = new AutoMPointCandidateResult
            {
                PatternRoi = feature.Rect,
                Bounding = new RectangleF(feature.Rect.X, feature.Rect.Y, feature.Rect.Width, feature.Rect.Height),
                PatternCenter = RectCenter(feature.Rect),
                Center = RectCenter(feature.Rect),
                ContrastStdDev = feature.ContrastStdDev,
                EdgeDensity = feature.EdgeDensity,
                QuadrantBalance = feature.QuadrantBalance,
                OrientationBalance = feature.OrientationBalance,
                FeatureQuality = feature.FeatureQuality
            };

            List<double> runtimes = new List<double>();
            using (Mat template = gray.SubMat(feature.Rect).Clone())
            {
                matcher.SetProperty(CreateMatcherProperty());
                matcher.SetTemplateImage(template);

                VisionToolResult selfExecution = matcher.Execute(gray);
                try
                {
                    runtimes.Add(selfExecution.Elapsed.TotalMilliseconds);
                    CopyModelDiagnostics(selfExecution, candidate);
                    MatchingResult self = SelectSelfResult(matcher.results, feature.Rect);
                    if (!selfExecution.Success || self == null)
                    {
                        return Reject(candidate, $"Self matching failed. {selfExecution.ErrorName}: {selfExecution.Message}", runtimes);
                    }

                    candidate.NativeMatchCenter = self.Center;
                    candidate.NativeToPatternOffsetX = candidate.PatternCenter.X - self.Center.X;
                    candidate.NativeToPatternOffsetY = candidate.PatternCenter.Y - self.Center.Y;
                    candidate.SelfMatchScore = self.Score;

                    double alternativeDistance = Math.Max(8d, Math.Min(feature.Rect.Width, feature.Rect.Height) * 0.35d);
                    MatchingResult alternative = matcher.results
                        .Where(result => Distance(result.Center, self.Center) >= alternativeDistance)
                        .OrderByDescending(result => result.Score)
                        .FirstOrDefault();
                    candidate.AlternativeMatchScore = alternative?.Score ?? 0d;
                    candidate.UniquenessMargin =
                        Math.Max(0d, candidate.SelfMatchScore - candidate.AlternativeMatchScore) / 100d;
                }
                finally
                {
                    selfExecution.Dispose();
                }

                if (candidate.UniquenessMargin < property.MinimumUniquenessMargin)
                {
                    return Reject(
                        candidate,
                        $"UniquenessMargin {candidate.UniquenessMargin:0.####} < {property.MinimumUniquenessMargin:0.####}.",
                        runtimes);
                }

                List<double> positionErrors = new List<double>();
                List<double> angleErrors = new List<double>();
                List<double> scaleErrors = new List<double>();
                int successCount = 0;
                List<SyntheticCase> syntheticCases = CreateSyntheticCases();
                foreach (SyntheticCase syntheticCase in syntheticCases)
                {
                    using (Mat matrix = CreateSyntheticMatrix(gray.Size(), syntheticCase))
                    using (Mat transformed = new Mat())
                    {
                        Cv2.WarpAffine(
                            gray,
                            transformed,
                            matrix,
                            gray.Size(),
                            InterpolationFlags.Linear,
                            BorderTypes.Reflect101);
                        if (Math.Abs(syntheticCase.Contrast - 1d) > 0.000001d
                            || Math.Abs(syntheticCase.Brightness) > 0.000001d)
                        {
                            transformed.ConvertTo(
                                transformed,
                                transformed.Type(),
                                syntheticCase.Contrast,
                                syntheticCase.Brightness);
                        }

                        Point2f expectedCenter = TransformPoint(candidate.NativeMatchCenter, matrix);
                        VisionToolResult execution = matcher.Execute(transformed);
                        try
                        {
                            runtimes.Add(execution.Elapsed.TotalMilliseconds);
                            MatchingResult actual = matcher.results
                                .OrderByDescending(result => result.Score)
                                .FirstOrDefault();
                            if (!execution.Success || actual == null)
                            {
                                continue;
                            }

                            successCount++;
                            positionErrors.Add(Distance(actual.Center, expectedCenter));
                            angleErrors.Add(AngleDifference(actual.Angle, syntheticCase.Angle));
                            scaleErrors.Add(Math.Abs(actual.Scale - syntheticCase.Scale));
                        }
                        finally
                        {
                            execution.Dispose();
                        }
                    }
                }

                candidate.SyntheticSuccessRate = syntheticCases.Count > 0
                    ? successCount / (double)syntheticCases.Count
                    : 1d;
                candidate.PositionErrorMeanPixels = positionErrors.Count > 0 ? positionErrors.Average() : double.PositiveInfinity;
                candidate.PositionErrorMaxPixels = positionErrors.Count > 0 ? positionErrors.Max() : double.PositiveInfinity;
                candidate.AngleErrorMaxDegrees = angleErrors.Count > 0 ? angleErrors.Max() : double.PositiveInfinity;
                candidate.ScaleErrorMaxRatio = scaleErrors.Count > 0 ? scaleErrors.Max() : double.PositiveInfinity;
                candidate.RuntimeMedianMilliseconds = Percentile(runtimes, 0.5d);
                candidate.RuntimeP95Milliseconds = Percentile(runtimes, 0.95d);

                if (candidate.SyntheticSuccessRate < property.MinimumSyntheticSuccessRate)
                {
                    return Reject(
                        candidate,
                        $"SyntheticSuccessRate {candidate.SyntheticSuccessRate:0.###} < {property.MinimumSyntheticSuccessRate:0.###}.",
                        runtimes);
                }

                if (candidate.PositionErrorMaxPixels > property.MaximumPositionErrorPixels)
                {
                    return Reject(
                        candidate,
                        $"PositionErrorMax {candidate.PositionErrorMaxPixels:0.###}px > {property.MaximumPositionErrorPixels:0.###}px.",
                        runtimes);
                }

                if (candidate.AngleErrorMaxDegrees > property.MaximumAngleErrorDegrees)
                {
                    return Reject(
                        candidate,
                        $"AngleErrorMax {candidate.AngleErrorMaxDegrees:0.###}deg > {property.MaximumAngleErrorDegrees:0.###}deg.",
                        runtimes);
                }

                if (candidate.ScaleErrorMaxRatio > property.MaximumScaleErrorRatio)
                {
                    return Reject(
                        candidate,
                        $"ScaleErrorMax {candidate.ScaleErrorMaxRatio:0.####} > {property.MaximumScaleErrorRatio:0.####}.",
                        runtimes);
                }

                if (property.MaximumRuntimeMilliseconds > 0d
                    && candidate.RuntimeP95Milliseconds > property.MaximumRuntimeMilliseconds)
                {
                    return Reject(
                        candidate,
                        $"RuntimeP95 {candidate.RuntimeP95Milliseconds:0.###}ms > {property.MaximumRuntimeMilliseconds:0.###}ms.",
                        runtimes);
                }
            }

            candidate.Accepted = true;
            candidate.RejectReason = string.Empty;
            candidate.Score = CalculateOverallScore(candidate) * 100d;
            return candidate;
        }

        private void EvaluateRepresentativeImages(
            Mat referenceGray,
            AutoMPointCandidateResult candidate,
            EdgeBasedTemplateMatchingTool matcher)
        {
            List<double> scores = new List<double>();
            List<double> uniquenessMargins = new List<double>();
            List<double> runtimes = new List<double>();
            candidate.RepresentativeMatches.Clear();

            using (Mat template = referenceGray.SubMat(candidate.PatternRoi).Clone())
            {
                matcher.SetProperty(CreateMatcherProperty());
                matcher.SetTemplateImage(template);
                for (int index = 0; index < representativeImages.Count; index++)
                {
                    using (Mat sampleGray = CreateGrayImage(representativeImages[index]))
                    {
                        VisionToolResult execution = matcher.Execute(sampleGray);
                        try
                        {
                            MatchingResult best = matcher.results
                                .OrderByDescending(result => result.Score)
                                .FirstOrDefault();
                            double score = best?.Score ?? 0d;
                            double alternativeDistance = Math.Max(
                                8d,
                                Math.Min(candidate.PatternRoi.Width, candidate.PatternRoi.Height) * 0.35d);
                            MatchingResult alternative = best == null
                                ? null
                                : matcher.results
                                    .Where(result => Distance(result.Center, best.Center) >= alternativeDistance)
                                    .OrderByDescending(result => result.Score)
                                    .FirstOrDefault();
                            double uniquenessMargin = best == null
                                ? 0d
                                : Math.Max(0d, best.Score - (alternative?.Score ?? 0d)) / 100d;
                            bool success = execution.Success
                                && best != null
                                && uniquenessMargin >= property.MinimumUniquenessMargin;
                            string outcome = success
                                ? "Success"
                                : best != null && uniquenessMargin < property.MinimumUniquenessMargin
                                    ? "Ambiguous"
                                    : "NoMatch";
                            string resultMessage = success
                                ? string.Empty
                                : outcome == "Ambiguous"
                                    ? $"UniquenessMargin {uniquenessMargin:0.####} < {property.MinimumUniquenessMargin:0.####}."
                                    : $"{execution.ErrorName}: {execution.Message}";

                            scores.Add(score);
                            uniquenessMargins.Add(uniquenessMargin);
                            runtimes.Add(execution.Elapsed.TotalMilliseconds);
                            candidate.RepresentativeMatches.Add(new AutoMPointRepresentativeMatchResult
                            {
                                ImageIndex = index + 1,
                                Success = success,
                                Outcome = outcome,
                                Message = resultMessage,
                                Center = best?.Center ?? new Point2f(),
                                Score = score,
                                UniquenessMargin = uniquenessMargin,
                                Angle = best?.Angle ?? 0d,
                                Scale = best?.Scale ?? 0d,
                                RuntimeMilliseconds = execution.Elapsed.TotalMilliseconds
                            });
                        }
                        finally
                        {
                            execution.Dispose();
                        }
                    }
                }
            }

            candidate.RepresentativeImageCount = candidate.RepresentativeMatches.Count;
            candidate.RepresentativeSuccessCount =
                candidate.RepresentativeMatches.Count(match => match.Success);
            candidate.RepresentativeSuccessRate = candidate.RepresentativeImageCount > 0
                ? candidate.RepresentativeSuccessCount / (double)candidate.RepresentativeImageCount
                : 0d;
            candidate.RepresentativeMeanScore = scores.Count > 0 ? scores.Average() : 0d;
            candidate.RepresentativeMinimumScore = scores.Count > 0 ? scores.Min() : 0d;
            candidate.RepresentativeMeanUniquenessMargin =
                uniquenessMargins.Count > 0 ? uniquenessMargins.Average() : 0d;
            candidate.RepresentativeMinimumUniquenessMargin =
                uniquenessMargins.Count > 0 ? uniquenessMargins.Min() : 0d;
            candidate.RepresentativeRuntimeP95Milliseconds = Percentile(runtimes, 0.95d);
            candidate.Score = CalculateOverallScore(candidate) * 100d;

            if (candidate.RepresentativeSuccessRate < property.MinimumRepresentativeSuccessRate)
            {
                candidate.Accepted = false;
                candidate.RejectReason =
                    $"RepresentativeSuccessRate {candidate.RepresentativeSuccessRate:0.###} < {property.MinimumRepresentativeSuccessRate:0.###} "
                    + $"({candidate.RepresentativeSuccessCount}/{candidate.RepresentativeImageCount}).";
            }
        }

        private AutoMPointCandidateResult Reject(
            AutoMPointCandidateResult candidate,
            string reason,
            List<double> runtimes)
        {
            candidate.Accepted = false;
            candidate.RejectReason = reason ?? "Rejected.";
            if (runtimes != null && runtimes.Count > 0)
            {
                candidate.RuntimeMedianMilliseconds = Percentile(runtimes, 0.5d);
                candidate.RuntimeP95Milliseconds = Percentile(runtimes, 0.95d);
            }

            candidate.Score = CalculateOverallScore(candidate) * 100d;
            return candidate;
        }

        private EdgeBasedTemplateMatchingTool CreateMatcherLease()
        {
            return new EdgeBasedTemplateMatchingTool();
        }

        private static void ReleaseMatcher(EdgeBasedTemplateMatchingTool matcher)
        {
            matcher?.Dispose();
        }

        private EdgeBasedTemplateMatchingToolProperty CreateMatcherProperty()
        {
            return new EdgeBasedTemplateMatchingToolProperty
            {
                NAME = "Auto MPoint verifier",
                SCORE_MIN = property.MatchingMinimumScore,
                NUM_MATCH = 2,
                CANNY_LOW = property.CannyLow,
                CANNY_HIGH = property.CannyHigh,
                MAX_TEMPLATE_POINTS = property.MaximumTemplatePoints,
                SEARCH_STEP = property.SearchStep,
                USE_POSITION_REFINE = property.UsePositionRefine,
                USE_SUBPIXEL_REFINE = property.UseSubpixelRefine,
                USE_PYRAMID_POSITION_PROPOSAL = property.UsePyramidPositionProposal,
                PYRAMID_POSITION_MIN_SCORE = Math.Min(0.7d, property.MatchingMinimumScore),
                USE_HYBRID_VERIFY = property.UseHybridVerify,
                USE_DRAW_IMAGE = false,
                USE_FIND_ANGLE = property.UseAngleSearch,
                FIND_ANGLE_MIN = property.AngleMinimum,
                FIND_ANGLE_MAX = property.AngleMaximum,
                FIND_ANGLE = property.AngleStep,
                USE_COARSE_TO_FINE_ANGLE_SEARCH = true,
                COARSE_ANGLE_STEP = 4d,
                USE_FIND_SCALE = property.UseScaleSearch,
                FIND_SCALE_MIN = property.ScaleMinimum,
                FIND_SCALE_MAX = property.ScaleMaximum,
                FIND_SCALE_STEP = property.ScaleStep,
                HYBRID_VERIFY_TOP_N = 6,
                USE_ROI = true,
                CvROI = analysisRoi
            };
        }

        private void DrawResult()
        {
            Mat drawing = imageSource.Clone();
            OpenCvHelper.SetImageChannel3(drawing);
            Cv2.Rectangle(drawing, analysisRoi, new Scalar(255, 255, 0), 1);

            foreach (AutoMPointCandidateResult candidate in candidates)
            {
                if (candidate.Accepted)
                {
                    continue;
                }

                Scalar color = new Scalar(0, 0, 180);
                Cv2.Rectangle(drawing, candidate.PatternRoi, color, 1);
                OpenCvSharp.Point patternPoint = new OpenCvSharp.Point(
                    (int)Math.Round(candidate.PatternCenter.X),
                    (int)Math.Round(candidate.PatternCenter.Y));
                Cv2.Circle(drawing, patternPoint, 4, color, -1, LineTypes.AntiAlias);
                Cv2.PutText(
                    drawing,
                    "REJECT",
                    new OpenCvSharp.Point(candidate.PatternRoi.X + 3, candidate.PatternRoi.Y + 18),
                    HersheyFonts.HersheySimplex,
                    0.4,
                    color,
                    1,
                    LineTypes.AntiAlias);
            }

            foreach (AutoMPointCandidateResult candidate in results)
            {
                Scalar color = candidate.Rank == 1 ? new Scalar(0, 255, 255) : new Scalar(0, 255, 0);
                Cv2.Rectangle(drawing, candidate.PatternRoi, color, candidate.Rank == 1 ? 3 : 2);
                OpenCvSharp.Point patternPoint = new OpenCvSharp.Point(
                    (int)Math.Round(candidate.PatternCenter.X),
                    (int)Math.Round(candidate.PatternCenter.Y));
                Cv2.Circle(drawing, patternPoint, 5, color, 2, LineTypes.AntiAlias);
                Cv2.PutText(
                    drawing,
                    candidate.RepresentativeImageCount > 0
                        ? $"#{candidate.Rank} {candidate.RepresentativeSuccessCount}/{candidate.RepresentativeImageCount}"
                        : $"#{candidate.Rank}",
                    new OpenCvSharp.Point(candidate.PatternRoi.X + 3, candidate.PatternRoi.Y + 15),
                    HersheyFonts.HersheySimplex,
                    0.45,
                    color,
                    1,
                    LineTypes.AntiAlias);
            }

            ReplaceResultImage(drawing);
        }

        private List<SyntheticCase> CreateSyntheticCases()
        {
            int translation = property.SyntheticTranslationPixels;
            double angle = SelectSyntheticAngle();
            double scale = SelectSyntheticScale();
            return new List<SyntheticCase>
        {
            new SyntheticCase(translation, -translation, 0d, 1d, 1d, 0d),
            new SyntheticCase(-translation, translation, angle, 1d, 1d, 0d),
            new SyntheticCase(translation, translation, 0d, scale, 1.08d, 4d)
        };
        }

        private double SelectSyntheticAngle()
        {
            if (!property.UseAngleSearch)
            {
                return 0d;
            }

            double magnitude = Math.Abs(property.SyntheticRotationDegrees);
            if (property.AngleMaximum >= magnitude)
            {
                return magnitude;
            }

            if (property.AngleMinimum <= -magnitude)
            {
                return -magnitude;
            }

            return Math.Abs(property.AngleMaximum) >= Math.Abs(property.AngleMinimum)
                ? property.AngleMaximum
                : property.AngleMinimum;
        }

        private double SelectSyntheticScale()
        {
            if (!property.UseScaleSearch)
            {
                return 1d;
            }

            double requested = property.SyntheticScaleRatio;
            if (!IsFinite(requested) || requested <= 0d)
            {
                requested = 1d;
            }

            return Math.Max(property.ScaleMinimum, Math.Min(property.ScaleMaximum, requested));
        }

        private static Mat CreateSyntheticMatrix(OpenCvSharp.Size imageSize, SyntheticCase syntheticCase)
        {
            Mat matrix = Cv2.GetRotationMatrix2D(
                new Point2f((imageSize.Width - 1) / 2f, (imageSize.Height - 1) / 2f),
                syntheticCase.Angle,
                syntheticCase.Scale);
            matrix.Set(0, 2, matrix.At<double>(0, 2) + syntheticCase.OffsetX);
            matrix.Set(1, 2, matrix.At<double>(1, 2) + syntheticCase.OffsetY);
            return matrix;
        }

        private static Point2f TransformPoint(Point2f point, Mat matrix)
        {
            return new Point2f(
                (float)((matrix.At<double>(0, 0) * point.X)
                    + (matrix.At<double>(0, 1) * point.Y)
                    + matrix.At<double>(0, 2)),
                (float)((matrix.At<double>(1, 0) * point.X)
                    + (matrix.At<double>(1, 1) * point.Y)
                    + matrix.At<double>(1, 2)));
        }

        private MatchingResult SelectSelfResult(IEnumerable<MatchingResult> matches, Rect taughtRoi)
        {
            Point2f taughtCenter = RectCenter(taughtRoi);
            MatchingResult nearest = (matches ?? Enumerable.Empty<MatchingResult>())
                .OrderBy(result => Distance(result.Center, taughtCenter))
                .ThenByDescending(result => result.Score)
                .FirstOrDefault();
            double tolerance = Math.Max(8d, Math.Max(taughtRoi.Width, taughtRoi.Height) * 0.55d);
            return nearest != null && Distance(nearest.Center, taughtCenter) <= tolerance ? nearest : null;
        }

        private static void CopyModelDiagnostics(
            VisionToolResult execution,
            AutoMPointCandidateResult candidate)
        {
            candidate.ModelEdgePointCount = ReadMetric(execution, "Model.EdgePointCount");
            candidate.ModelEdgeCoverageArea = ReadMetric(execution, "Model.EdgeCoverageArea");
            candidate.ModelQuadrantBalance = ReadMetric(execution, "Model.QuadrantBalance");
            candidate.ModelHighestUsablePyramidLevel = ReadMetric(execution, "Model.Pyramid.HighestUsableLevel");
        }

        private static double ReadMetric(VisionToolResult result, string name)
        {
            return result != null
                && result.Metrics.TryGetValue(name, out double value)
                && IsFinite(value)
                    ? value
                    : 0d;
        }

        private double CalculateOverallScore(AutoMPointCandidateResult candidate)
        {
            double uniquenessScore = Clamp01(candidate.UniquenessMargin / Math.Max(0.000001d, property.MinimumUniquenessMargin * 3d));
            double stabilityScore = Clamp01(candidate.SyntheticSuccessRate);
            double precisionScore = IsFinite(candidate.PositionErrorMaxPixels)
                ? Clamp01(1d - (candidate.PositionErrorMaxPixels / Math.Max(0.000001d, property.MaximumPositionErrorPixels * 2d)))
                : 0d;
            double runtimeScore = property.MaximumRuntimeMilliseconds > 0d
                ? Clamp01(1d - (candidate.RuntimeP95Milliseconds / (property.MaximumRuntimeMilliseconds * 2d)))
                : 1d;
            double singleImageScore = GeometricMean(
                Clamp01(candidate.FeatureQuality),
                uniquenessScore,
                stabilityScore,
                precisionScore,
                runtimeScore);
            if (candidate.RepresentativeImageCount <= 0)
            {
                return singleImageScore;
            }

            double representativeUniquenessScore = Clamp01(
                candidate.RepresentativeMinimumUniquenessMargin
                / Math.Max(0.000001d, property.MinimumUniquenessMargin * 3d));
            return GeometricMean(
                singleImageScore,
                candidate.RepresentativeSuccessRate,
                Clamp01(candidate.RepresentativeMeanScore / 100d),
                representativeUniquenessScore);
        }

        private static double CalculateQuadrantBalance(Mat edgeRoi)
        {
            int leftWidth = edgeRoi.Width / 2;
            int rightWidth = edgeRoi.Width - leftWidth;
            int topHeight = edgeRoi.Height / 2;
            int bottomHeight = edgeRoi.Height - topHeight;
            if (leftWidth <= 0 || rightWidth <= 0 || topHeight <= 0 || bottomHeight <= 0)
            {
                return 0d;
            }

            int[] counts =
            {
            CountNonZero(edgeRoi, new Rect(0, 0, leftWidth, topHeight)),
            CountNonZero(edgeRoi, new Rect(leftWidth, 0, rightWidth, topHeight)),
            CountNonZero(edgeRoi, new Rect(0, topHeight, leftWidth, bottomHeight)),
            CountNonZero(edgeRoi, new Rect(leftWidth, topHeight, rightWidth, bottomHeight))
        };
            int maximum = counts.Max();
            return maximum > 0 ? counts.Min() / (double)maximum : 0d;
        }

        private static int CountNonZero(Mat source, Rect roi)
        {
            using (Mat sub = source.SubMat(roi))
            {
                return Cv2.CountNonZero(sub);
            }
        }

        private static Mat CreateGrayImage(Mat source)
        {
            Mat gray = source.Clone();
            OpenCvHelper.SetImageChannel1(gray);
            return gray;
        }

        private static List<int> CreateAxisPositions(int first, int last, int stride)
        {
            List<int> values = new List<int>();
            for (int value = first; value <= last; value += stride)
            {
                values.Add(value);
            }

            if (values.Count == 0 || values[values.Count - 1] != last)
            {
                values.Add(last);
            }

            return values;
        }

    }

        private static double CalculateIntersectionOverUnion(Rect left, Rect right)
        {
            int intersectionLeft = Math.Max(left.Left, right.Left);
            int intersectionTop = Math.Max(left.Top, right.Top);
            int intersectionRight = Math.Min(left.Right, right.Right);
            int intersectionBottom = Math.Min(left.Bottom, right.Bottom);
            int intersectionWidth = Math.Max(0, intersectionRight - intersectionLeft);
            int intersectionHeight = Math.Max(0, intersectionBottom - intersectionTop);
            double intersection = intersectionWidth * intersectionHeight;
            double union = (left.Width * left.Height) + (right.Width * right.Height) - intersection;
            return union > 0d ? intersection / union : 0d;
        }

        private static Point2f RectCenter(Rect rect)
        {
            return new Point2f(rect.X + (rect.Width / 2f), rect.Y + (rect.Height / 2f));
        }

        private static double Distance(Point2f left, Point2f right)
        {
            double dx = left.X - right.X;
            double dy = left.Y - right.Y;
            return Math.Sqrt((dx * dx) + (dy * dy));
        }

        private static double AngleDifference(double left, double right)
        {
            double difference = Math.Abs(left - right) % 360d;
            return difference > 180d ? 360d - difference : difference;
        }

        private static double Percentile(List<double> values, double percentile)
        {
            if (values == null || values.Count == 0)
            {
                return 0d;
            }

            double[] ordered = values.Where(IsFinite).OrderBy(value => value).ToArray();
            if (ordered.Length == 0)
            {
                return 0d;
            }

            int index = (int)Math.Ceiling(Clamp01(percentile) * ordered.Length) - 1;
            return ordered[Math.Max(0, Math.Min(ordered.Length - 1, index))];
        }

        private static double GeometricMean(params double[] values)
        {
            if (values == null || values.Length == 0)
            {
                return 0d;
            }

            double product = 1d;
            foreach (double value in values)
            {
                product *= Clamp01(value);
            }

            return Math.Pow(product, 1d / values.Length);
        }

        private static double Clamp01(double value)
        {
            if (!IsFinite(value) || value <= 0d) { return 0d; }
            if (value >= 1d) { return 1d; }
            return value;
        }

        private static bool AreUnitIntervalValuesValid(params double[] values)
        {
            return values != null && values.All(value => IsFinite(value) && value >= 0d && value <= 1d);
        }

        private static bool AreFiniteNonNegative(params double[] values)
        {
            return values != null && values.All(value => IsFinite(value) && value >= 0d);
        }

        private static bool AreFinitePositive(params double[] values)
        {
            return values != null && values.All(value => IsFinite(value) && value > 0d);
        }

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }

        private static bool IsRectInsideImage(Rect rect, Mat image)
        {
            return rect.Width > 0
                && rect.Height > 0
                && rect.X >= 0
                && rect.Y >= 0
                && rect.Right <= image.Width
                && rect.Bottom <= image.Height;
        }

        private static string FormatRect(Rect rect)
        {
            return $"{rect.X},{rect.Y},{rect.Width},{rect.Height}";
        }

        private sealed class CandidateRect
        {
            public CandidateRect(Rect rect, bool isWholeAnalysisRoi)
            {
                Rect = rect;
                IsWholeAnalysisRoi = isWholeAnalysisRoi;
            }

            public Rect Rect { get; }
            public bool IsWholeAnalysisRoi { get; }
        }

        private sealed class FeatureCandidate
        {
            public FeatureCandidate(Rect rect, bool isWholeAnalysisRoi)
            {
                Rect = rect;
                IsWholeAnalysisRoi = isWholeAnalysisRoi;
            }

            public Rect Rect { get; }
            public bool IsWholeAnalysisRoi { get; }
            public double ContrastStdDev { get; set; }
            public double EdgeDensity { get; set; }
            public double QuadrantBalance { get; set; }
            public double OrientationBalance { get; set; }
            public double FeatureQuality { get; set; }
            public string RejectReason { get; set; } = string.Empty;
        }

        private sealed class SyntheticCase
        {
            public SyntheticCase(
                int offsetX,
                int offsetY,
                double angle,
                double scale,
                double contrast,
                double brightness)
            {
                OffsetX = offsetX;
                OffsetY = offsetY;
                Angle = angle;
                Scale = scale;
                Contrast = contrast;
                Brightness = brightness;
            }

            public int OffsetX { get; }
            public int OffsetY { get; }
            public double Angle { get; }
            public double Scale { get; }
            public double Contrast { get; }
            public double Brightness { get; }
        }

    }

}
