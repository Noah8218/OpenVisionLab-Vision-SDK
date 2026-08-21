using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using OpenVisionLab.Vision2D.Property;
using OpenVisionLab.Vision2D.Result;
using OpenCvSharp;

namespace OpenVisionLab.Vision2D.Tool
{
    public sealed class EdgeBasedTemplateMatchingTool : OpenCvAlgorithmBase
    {
        public IOpenCVPropertyEdgeBasedTemplateMatching property;
        public List<MatchingResult> results = new List<MatchingResult>();

        private const int PositionRefineSeedCount = 3;
        private const int ParallelModelThreshold = 2;
        private const int ParallelPositionThreshold = 4096;
        private const int HybridCandidateGridColumns = 4;
        private const int HybridCandidateGridRows = 4;
        private const int HybridCandidateGridTopK = 2;
        private const int HybridProposalDownscaleSourcePixels = 120000;
        private const int HybridProposalDownscaleTargetPixels = 80000;
        private const int HybridProposalMinScaledTemplateSize = 24;
        private const int HybridProposalScaledTopK = 2;
        private const int HybridProposalMinRefineRadius = 6;
        private const double HybridProposalFastPathMinImageScore = 0.985D;
        private const double HybridProposalFastPathMinEdgeScore = 0.70D;
        private const double HybridEdgeDescriptorWeight = 0.15D;
        private const double HybridDescriptorImageTieMargin = 0.02D;
        private const double PyramidPositionProposalScale = 0.5D;
        private const int PyramidPositionProposalMinTemplateSize = 16;
        private const int PyramidPositionProposalRefineRadius = 6;
        private const double PyramidPositionProposalWeakVerifiedMargin = 0.03D;
        private const double CandidateAmbiguityScoreGapThreshold = 0.03D;
        private const double CandidateAmbiguityDistanceFactor = 0.35D;
        private const int UniqueMatchMinimumInternalTopK = 8;
        private const int ModelSmallTemplateMinDimension = 24;
        private const int ModelLowEdgePointThreshold = 40;
        private const double ModelLowCoverageAreaThreshold = 0.15D;
        private const double ModelLowQuadrantBalanceThreshold = 0.20D;
        private const double ModelScaleSearchCoverageWarningThreshold = 0.35D;
        private const int ModelPyramidMaxDiagnosticLevels = 6;
        private const int ModelPyramidMinUsableDimension = 16;
        private readonly EdgeTemplateModelStore modelStore;
        private readonly EdgeCandidateSearch candidateSearch;
        private readonly EdgeHybridCandidateSearch hybridSearch;
        private readonly EdgeMatchDecision matchDecision;
        private readonly Dictionary<string, double> phaseElapsedMs = new Dictionary<string, double>(StringComparer.Ordinal);
        private readonly object phaseElapsedSync = new object();
        private CandidateDiagnostics candidateDiagnostics = new CandidateDiagnostics();
        private VisionToolErrorCode lastMatchingErrorCode = VisionToolErrorCode.MatchingNoResult;
        private string lastMatchingMessage = "Edge based template matching found no result.";

        public bool CollectPhaseTimings { get; set; }
        public IReadOnlyDictionary<string, double> LastPhaseElapsedMs => phaseElapsedMs;

        public EdgeBasedTemplateMatchingTool()
        {
            modelStore = new EdgeTemplateModelStore(this);
            candidateSearch = new EdgeCandidateSearch(this);
            hybridSearch = new EdgeHybridCandidateSearch(this);
            matchDecision = new EdgeMatchDecision(this);
        }

        public void SetProperty(IOpenCVPropertyEdgeBasedTemplateMatching propertyBase)
        {
            property = propertyBase;
            modelStore.Clear();
        }

        public void SetTemplateImage(Mat image)
        {
            imageTemplate?.Dispose();
            modelStore.SetTemplate(image);
            imageTemplate = modelStore.GetTemplateForRun().Clone();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                modelStore.Dispose();
            }

            base.Dispose(disposing);
        }

        public override VisionToolResult Execute(Mat source)
        {
            VisionToolResult result = base.Execute(source);
            if (result != null)
            {
                result.EdgeBasedMatchingDiagnostics = CreateDiagnosticEvidence(result);
            }

            return result;
        }

        protected override bool TryValidateBeforeRun(out VisionToolErrorCode errorCode, out string message)
        {
            if (!base.TryValidateBeforeRun(out errorCode, out message))
            {
                return false;
            }

            if (OpenCvHelper.IsImageEmpty(GetTemplateForRun()))
            {
                errorCode = VisionToolErrorCode.MatchingTemplateMissing;
                message = string.IsNullOrWhiteSpace(property.PATTERN_PATH)
                    ? "Edge based matching template image is not loaded."
                    : $"Edge based matching template image is not loaded. Path={property.PATTERN_PATH}.";
                return false;
            }

            if (property.SCORE_MIN < -1 || property.SCORE_MIN > 1)
            {
                errorCode = VisionToolErrorCode.InvalidParameter;
                message = $"Edge based matching SCORE_MIN must be between -1 and 1. SCORE_MIN={property.SCORE_MIN}.";
                return false;
            }

            if (property.NUM_MATCH <= 0)
            {
                errorCode = VisionToolErrorCode.InvalidParameter;
                message = $"Edge based matching NUM_MATCH must be greater than 0. NUM_MATCH={property.NUM_MATCH}.";
                return false;
            }

            if (property.USE_UNIQUE_MATCH_VALIDATION && property.NUM_MATCH != 1)
            {
                errorCode = VisionToolErrorCode.InvalidParameter;
                message = $"Unique edge matching requires NUM_MATCH=1. NUM_MATCH={property.NUM_MATCH}.";
                return false;
            }

            if (property.USE_UNIQUE_MATCH_VALIDATION && property.USE_MULTI_ROI)
            {
                errorCode = VisionToolErrorCode.InvalidParameter;
                message = "Unique edge matching requires one search region. USE_MULTI_ROI must be false.";
                return false;
            }

            if (property.USE_UNIQUE_MATCH_VALIDATION
                && (double.IsNaN(property.UNIQUE_MATCH_MIN_SCORE_MARGIN)
                    || double.IsInfinity(property.UNIQUE_MATCH_MIN_SCORE_MARGIN)
                    || property.UNIQUE_MATCH_MIN_SCORE_MARGIN < 0D
                    || property.UNIQUE_MATCH_MIN_SCORE_MARGIN > 1D))
            {
                errorCode = VisionToolErrorCode.InvalidParameter;
                message = $"Unique edge matching score margin must be between 0 and 1. Margin={property.UNIQUE_MATCH_MIN_SCORE_MARGIN}.";
                return false;
            }

            if (property.SEARCH_STEP <= 0)
            {
                errorCode = VisionToolErrorCode.InvalidParameter;
                message = $"Edge based matching SEARCH_STEP must be greater than 0. SEARCH_STEP={property.SEARCH_STEP}.";
                return false;
            }

            if (property.MAX_TEMPLATE_POINTS <= 0)
            {
                errorCode = VisionToolErrorCode.InvalidParameter;
                message = $"Edge based matching MAX_TEMPLATE_POINTS must be greater than 0. MAX_TEMPLATE_POINTS={property.MAX_TEMPLATE_POINTS}.";
                return false;
            }

            if (property.USE_PYRAMID_POSITION_PROPOSAL && property.PYRAMID_POSITION_TOP_N <= 0)
            {
                errorCode = VisionToolErrorCode.InvalidParameter;
                message = $"Edge based matching pyramid proposal top N must be greater than 0. TopN={property.PYRAMID_POSITION_TOP_N}.";
                return false;
            }

            if (property.USE_PYRAMID_POSITION_PROPOSAL
                && (property.PYRAMID_POSITION_MIN_SCORE < -1D || property.PYRAMID_POSITION_MIN_SCORE > 1D))
            {
                errorCode = VisionToolErrorCode.InvalidParameter;
                message = $"Edge based matching pyramid proposal min score must be between -1 and 1. MinScore={property.PYRAMID_POSITION_MIN_SCORE}.";
                return false;
            }

            if (property.USE_FIND_ANGLE && property.FIND_ANGLE <= 0)
            {
                errorCode = VisionToolErrorCode.MatchingInvalidAngleStep;
                message = $"Edge based matching angle step must be greater than 0 when angle search is enabled. AngleStep={property.FIND_ANGLE}.";
                return false;
            }

            if (property.USE_FIND_ANGLE
                && property.USE_COARSE_TO_FINE_ANGLE_SEARCH
                && property.COARSE_ANGLE_STEP <= 0)
            {
                errorCode = VisionToolErrorCode.MatchingInvalidAngleStep;
                message = $"Edge based matching coarse angle step must be greater than 0 when coarse-to-fine angle search is enabled. CoarseAngleStep={property.COARSE_ANGLE_STEP}.";
                return false;
            }

            if (property.USE_FIND_ANGLE
                && property.USE_COARSE_TO_FINE_ANGLE_SEARCH
                && property.COARSE_ANGLE_TOP_K <= 0)
            {
                errorCode = VisionToolErrorCode.MatchingInvalidAngleStep;
                message = $"Edge based matching coarse angle top K must be greater than 0 when coarse-to-fine angle search is enabled. CoarseAngleTopK={property.COARSE_ANGLE_TOP_K}.";
                return false;
            }

            if (property.USE_FIND_SCALE
                && (property.FIND_SCALE_MIN <= 0D || property.FIND_SCALE_MAX <= 0D))
            {
                errorCode = VisionToolErrorCode.MatchingInvalidScale;
                message = $"Edge based matching scale range must be greater than 0 when scale search is enabled. Scale={property.FIND_SCALE_MIN:0.###}..{property.FIND_SCALE_MAX:0.###}.";
                return false;
            }

            if (property.USE_FIND_SCALE && property.FIND_SCALE_STEP <= 0D)
            {
                errorCode = VisionToolErrorCode.MatchingInvalidScale;
                message = $"Edge based matching scale step must be greater than 0 when scale search is enabled. ScaleStep={property.FIND_SCALE_STEP:0.###}.";
                return false;
            }

            if (property.USE_HYBRID_VERIFY && property.HYBRID_VERIFY_TOP_N <= 0)
            {
                errorCode = VisionToolErrorCode.InvalidParameter;
                message = $"Edge based matching hybrid verify top N must be greater than 0. HybridTopN={property.HYBRID_VERIFY_TOP_N}.";
                return false;
            }

            if (property.USE_HYBRID_VERIFY
                && (property.HYBRID_VERIFY_IMAGE_WEIGHT < 0D || property.HYBRID_VERIFY_IMAGE_WEIGHT > 1D))
            {
                errorCode = VisionToolErrorCode.InvalidParameter;
                message = $"Edge based matching hybrid image weight must be between 0 and 1. HybridImageWeight={property.HYBRID_VERIFY_IMAGE_WEIGHT}.";
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
                "Edge based matching",
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
                errorCode = lastMatchingErrorCode;
                message = lastMatchingMessage;
                return false;
            }

            errorCode = VisionToolErrorCode.None;
            message = string.Empty;
            return true;
        }

        public override void Run()
        {
            swTaktTimems.Restart();
            results.Clear();
            ResetMatchingFailure();
            ResetPhaseTimings();
            ResetCandidateDiagnostics();

            if (OpenCvHelper.IsImageEmpty(imageSource))
            {
                SetMatchingFailure(VisionToolErrorCode.InputImageInvalid, "Source image is not loaded.");
                return;
            }

            long modelCacheStart = StartPhaseTiming();
            TemplateModelCache modelCache = GetTemplateModelCache();
            StopPhaseTiming("ModelCache", modelCacheStart);
            EdgeTemplateModel model = modelCache?.Model;
            if (model == null)
            {
                SetMatchingFailure(
                    VisionToolErrorCode.MatchingTemplateInvalid,
                    $"Edge based matching template model could not be created. {FormatMatchingOptions()}");
                return;
            }

            if (model.Points.Count == 0)
            {
                SetMatchingFailure(
                    VisionToolErrorCode.MatchingTemplateInvalid,
                    $"Edge based matching template has no usable edge points. {FormatMatchingOptions()}");
                return;
            }

            if (property.USE_MULTI_ROI)
            {
                RunMultiRoi(modelCache);
            }
            else
            {
                Rect roi = NormalizeRoi(property.CvROI);
                RunRoi(modelCache, roi, property.USE_ROI);
            }

            if (property.USE_DRAW_IMAGE || results.Count > 0)
            {
                long drawStart = StartPhaseTiming();
                DrawResultImage();
                StopPhaseTiming("DrawResult", drawStart);
            }

            swTaktTimems.Stop();
        }

        private void RunMultiRoi(TemplateModelCache modelCache)
        {
            if (property.CvROIS == null)
            {
                return;
            }

            foreach (Rect roi in property.CvROIS)
            {
                RunRoi(modelCache, NormalizeRoi(roi), true);
            }
        }

        private void RunRoi(TemplateModelCache modelCache, Rect roi, bool useRoi)
        {
            long preprocessStart = StartPhaseTiming();
            Mat source = CreatePreprocessedImage(roi, useRoi, property);
            StopPhaseTiming("Preprocess", preprocessStart);
            using (source)
            {
                long gradientStart = StartPhaseTiming();
                GradientImage gradients = GradientImage.Create(source, property.MIN_GRADIENT_MAGNITUDE);
                StopPhaseTiming("SourceGradient", gradientStart);
                using (gradients)
                {
                    EdgeTemplateModel model = modelCache.Model;
                    Mat template = modelCache.PreparedTemplate;
                    List<RotatedEdgeTemplateModel> searchModels = ShouldUseCoarseToFineAngleSearch()
                        ? null
                        : GetSearchModels(modelCache, property.FIND_ANGLE);
                    List<RectangleF> suppressedBounds = new List<RectangleF>();
                    int localMatchCount = property.USE_MULTI_ROI ? Math.Max(1, property.NUM_MATCH) : property.NUM_MATCH;

                    if (TryRunScaleSeedMultiMatch(
                        source,
                        gradients,
                        modelCache,
                        template,
                        searchModels,
                        roi,
                        useRoi,
                        suppressedBounds,
                        localMatchCount))
                    {
                        return;
                    }

                    for (int i = 0; i < localMatchCount; i++)
                    {
                        long proposalStart = StartPhaseTiming();
                        MatchCandidate imageProposal = CreateHybridImageProposalCandidate(
                            gradients,
                            source,
                            template,
                            modelCache,
                            suppressedBounds);
                        StopPhaseTiming("HybridImageProposal", proposalStart);
                        RecordImageProposalDiagnostics(imageProposal);

                        MatchCandidate candidate = imageProposal;
                        List<MatchCandidate> candidateSeeds = new List<MatchCandidate>();
                        bool useFastPath = TryUseHybridProposalFastPath(imageProposal, localMatchCount);
                        if (useFastPath)
                        {
                            candidateDiagnostics.FastPathCount++;
                            candidateDiagnostics.ImageProposalSelectedCount++;
                        }
                        else
                        {
                            if (ShouldUseHybridVerify())
                            {
                                candidateDiagnostics.FallbackSearchCount++;
                            }

                            long searchStart = StartPhaseTiming();
                            candidate = FindBestCandidate(
                                source,
                                gradients,
                                modelCache,
                                searchModels,
                                suppressedBounds,
                                out candidateSeeds);
                            StopPhaseTiming("SearchEdgeCandidate", searchStart);
                            RecordEdgeSearchDiagnostics(candidateSeeds, candidate);
                            long verifyStart = StartPhaseTiming();
                            candidate = ApplyHybridVerification(source, template, candidate, candidateSeeds, imageProposal);
                            StopPhaseTiming("HybridVerify", verifyStart);
                            if (!ShouldUseHybridVerify())
                            {
                                RecordCandidateAmbiguityDiagnostics(candidateSeeds, candidate, template);
                            }
                        }

                        RecordCandidateDisplayEvidence(
                            candidate,
                            (candidateSeeds ?? new List<MatchCandidate>())
                                .Concat(imageProposal == null
                                    ? Enumerable.Empty<MatchCandidate>()
                                    : new[] { imageProposal }),
                            template,
                            roi,
                            useRoi);

                        if (candidate == null || candidate.Score < property.SCORE_MIN)
                        {
                            RecordUniqueMatchNoMatch(candidate);
                            if (results.Count == 0)
                            {
                                SetMatchingFailure(
                                    VisionToolErrorCode.MatchingNoResult,
                                    $"Edge based matching found no result above score threshold. BestScore={(candidate?.Score * 100.0) ?? 0:0.###}, {FormatMatchingOptions()}");
                            }

                            break;
                        }

                        if (!TryAcceptUniqueMatch(candidate))
                        {
                            break;
                        }

                        candidate = ApplySubpixelRefinement(gradients, modelCache, candidate);
                        ApplyGlobalPolarityState(gradients, modelCache, candidate);
                        MatchingResult result = CreateResult(candidate, model, roi, useRoi);
                        if (!IsDuplicate(result))
                        {
                            result.Index = results.Count + 1;
                            results.Add(result);
                        }

                        suppressedBounds.Add(CreateExpandedBounds(candidate.Bounds, 0.35f));
                    }
                }
            }
        }

        private bool TryRunScaleSeedMultiMatch(
            Mat source,
            GradientImage gradients,
            TemplateModelCache modelCache,
            Mat template,
            List<RotatedEdgeTemplateModel> searchModels,
            Rect roi,
            bool useRoi,
            List<RectangleF> suppressedBounds,
            int localMatchCount)
        {
            if (!ShouldUseScaleSeedMultiMatch(localMatchCount)
                || searchModels == null
                || searchModels.Count == 0)
            {
                return false;
            }

            // Scale search scans every candidate position for every scale. For multi-match,
            // keep the first full-search seed pool and only fall back to full search if that
            // pool cannot provide enough non-overlapping matches.
            int targetResultCount = results.Count + localMatchCount;
            int seedCapacity = Math.Max(GetCandidateSeedCapacity(), localMatchCount);
            List<MatchCandidate> seedPool = new List<MatchCandidate>(seedCapacity);

            if (ShouldUseHybridVerify())
            {
                candidateDiagnostics.FallbackSearchCount++;
            }

            long searchStart = StartPhaseTiming();
            MatchCandidate firstCandidate = FindBestCandidate(
                source,
                gradients,
                modelCache,
                searchModels,
                suppressedBounds,
                out List<MatchCandidate> candidateSeeds);
            StopPhaseTiming("SearchEdgeCandidate", searchStart);
            RecordEdgeSearchDiagnostics(candidateSeeds, firstCandidate);
            MergeCandidateSeeds(seedPool, candidateSeeds, seedCapacity);
            TrackCandidateSeed(seedPool, firstCandidate, seedCapacity);

            while (results.Count < targetResultCount)
            {
                List<MatchCandidate> availableSeeds = CreateAvailableCandidateSeeds(seedPool, suppressedBounds);
                MatchCandidate fallback = availableSeeds.Count > 0 ? availableSeeds[0] : null;
                if (fallback == null)
                {
                    break;
                }

                long verifyStart = StartPhaseTiming();
                MatchCandidate candidate = ApplyHybridVerification(source, template, fallback, availableSeeds, null);
                StopPhaseTiming("HybridVerify", verifyStart);

                if (!TryAcceptCandidate(candidate, gradients, modelCache, roi, useRoi, suppressedBounds))
                {
                    return true;
                }
            }

            while (results.Count < targetResultCount)
            {
                if (ShouldUseHybridVerify())
                {
                    candidateDiagnostics.FallbackSearchCount++;
                }

                long fallbackSearchStart = StartPhaseTiming();
                MatchCandidate candidate = FindBestCandidate(
                    source,
                    gradients,
                    modelCache,
                    searchModels,
                    suppressedBounds,
                    out candidateSeeds);
                StopPhaseTiming("SearchEdgeCandidate", fallbackSearchStart);
                RecordEdgeSearchDiagnostics(candidateSeeds, candidate);

                long verifyStart = StartPhaseTiming();
                candidate = ApplyHybridVerification(source, template, candidate, candidateSeeds, null);
                StopPhaseTiming("HybridVerify", verifyStart);

                if (!TryAcceptCandidate(candidate, gradients, modelCache, roi, useRoi, suppressedBounds))
                {
                    return true;
                }
            }

            return true;
        }

        private bool ShouldUseScaleSeedMultiMatch(int localMatchCount)
        {
            return property.USE_FIND_SCALE
                && !property.USE_FIND_ANGLE
                && !property.USE_MULTI_ROI
                && ShouldUseHybridVerify()
                && localMatchCount > 1;
        }

        private bool TryAcceptCandidate(
            MatchCandidate candidate,
            GradientImage gradients,
            TemplateModelCache modelCache,
            Rect roi,
            bool useRoi,
            List<RectangleF> suppressedBounds)
        {
            if (candidate == null || candidate.Score < property.SCORE_MIN)
            {
                if (results.Count == 0)
                {
                    SetMatchingFailure(
                        VisionToolErrorCode.MatchingNoResult,
                        $"Edge based matching found no result above score threshold. BestScore={(candidate?.Score * 100.0) ?? 0:0.###}, {FormatMatchingOptions()}");
                }

                return false;
            }

            candidate = ApplySubpixelRefinement(gradients, modelCache, candidate);
            ApplyGlobalPolarityState(gradients, modelCache, candidate);
            MatchingResult result = CreateResult(candidate, modelCache.Model, roi, useRoi);
            if (!IsDuplicate(result))
            {
                result.Index = results.Count + 1;
                results.Add(result);
            }

            suppressedBounds.Add(CreateExpandedBounds(candidate.Bounds, 0.35f));
            return true;
        }

        private static List<MatchCandidate> CreateAvailableCandidateSeeds(
            IEnumerable<MatchCandidate> seedPool,
            List<RectangleF> suppressedBounds)
        {
            List<MatchCandidate> available = new List<MatchCandidate>();
            foreach (MatchCandidate candidate in seedPool ?? Enumerable.Empty<MatchCandidate>())
            {
                if (candidate == null || IsSuppressed(candidate.Bounds, suppressedBounds))
                {
                    continue;
                }

                if (available.Any(existing => IsSameCandidate(existing, candidate)))
                {
                    continue;
                }

                available.Add(candidate);
            }

            available.Sort((left, right) => right.Score.CompareTo(left.Score));
            return available;
        }

        private void ResetPhaseTimings()
        {
            if (CollectPhaseTimings)
            {
                lock (phaseElapsedSync)
                {
                    phaseElapsedMs.Clear();
                }
            }
        }

        private void ResetCandidateDiagnostics()
        {
            candidateDiagnostics = new CandidateDiagnostics();
        }

        private long StartPhaseTiming()
        {
            return CollectPhaseTimings ? Stopwatch.GetTimestamp() : 0L;
        }

        private void StopPhaseTiming(string phaseName, long startTimestamp)
        {
            if (!CollectPhaseTimings || startTimestamp == 0L || string.IsNullOrWhiteSpace(phaseName))
            {
                return;
            }

            double elapsedMs = (Stopwatch.GetTimestamp() - startTimestamp) * 1000D / Stopwatch.Frequency;
            lock (phaseElapsedSync)
            {
                if (phaseElapsedMs.TryGetValue(phaseName, out double currentMs))
                {
                    phaseElapsedMs[phaseName] = currentMs + elapsedMs;
                    return;
                }

                phaseElapsedMs[phaseName] = elapsedMs;
            }
        }

        protected override IDictionary<string, double> CollectMetrics()
        {
            IDictionary<string, double> metrics = base.CollectMetrics();
            AddModelQualityMetrics(metrics, modelStore.Current?.Model);
            AddModelPyramidDiagnostics(metrics, modelStore.Current?.Model);
            AddSearchSpaceDiagnostics(metrics, modelStore.Current?.Model);
            AddCandidateDiagnostics(metrics, candidateDiagnostics, property?.USE_FIND_SCALE == true);
            AddUniqueMatchDiagnostics(metrics);
            AddGlobalPolarityDiagnostics(metrics);
            lock (phaseElapsedSync)
            {
                foreach (KeyValuePair<string, double> phase in phaseElapsedMs)
                {
                    metrics[$"Phase.{phase.Key}.Ms"] = phase.Value;
                }
            }

            return metrics;
        }

        private void AddGlobalPolarityDiagnostics(IDictionary<string, double> metrics)
        {
            if (metrics == null || property == null)
            {
                return;
            }

            metrics["GlobalPolarity.AllowReversal"] = property.ALLOW_GLOBAL_POLARITY_REVERSAL ? 1D : 0D;
            metrics["GlobalPolarity.Reversed"] = results.Count == 1 && results[0].PolarityReversed ? 1D : 0D;
            metrics["GlobalPolarity.SameCount"] = results.Count(result => !result.PolarityReversed);
            metrics["GlobalPolarity.ReversedCount"] = results.Count(result => result.PolarityReversed);
        }

        private void AddUniqueMatchDiagnostics(IDictionary<string, double> metrics)
        {
            if (metrics == null || property == null)
            {
                return;
            }

            bool enabled = property.USE_UNIQUE_MATCH_VALIDATION;
            metrics["UniqueMatch.Enabled"] = enabled ? 1D : 0D;
            metrics["UniqueMatch.State"] = enabled ? (double)candidateDiagnostics.UniqueMatchState : 0D;
            metrics["UniqueMatch.MinimumInternalTopK"] = enabled ? UniqueMatchMinimumInternalTopK : 0D;
            metrics["UniqueMatch.PlausibleAlternativeCount"] = candidateDiagnostics.UniqueMatchPlausibleAlternativeCount;
            metrics["UniqueMatch.SelectedScore"] = NormalizeDiagnosticScore(candidateDiagnostics.UniqueMatchSelectedScore);
            metrics["UniqueMatch.StrongestAlternativeScore"] = NormalizeDiagnosticScore(candidateDiagnostics.UniqueMatchStrongestAlternativeScore);
            metrics["UniqueMatch.ScoreMargin"] = NormalizeDiagnosticScore(candidateDiagnostics.UniqueMatchScoreMargin);
            metrics["UniqueMatch.MinimumScoreMargin"] = enabled
                ? property.UNIQUE_MATCH_MIN_SCORE_MARGIN
                : 0D;
            metrics["UniqueMatch.DistanceThresholdPx"] = NormalizeDiagnosticScore(candidateDiagnostics.UniqueMatchDistanceThreshold);
        }

        private static void AddCandidateDiagnostics(
            IDictionary<string, double> metrics,
            CandidateDiagnostics diagnostics,
            bool scaleSearchEnabled)
        {
            if (metrics == null || diagnostics == null)
            {
                return;
            }

            metrics["Candidate.ScaleSearchEnabled"] = scaleSearchEnabled ? 1D : 0D;
            metrics["Candidate.ImageProposalCount"] = diagnostics.ImageProposalCount;
            metrics["Candidate.ImageProposalMissingCount"] = diagnostics.ImageProposalMissingCount;
            metrics["Candidate.FastPathCount"] = diagnostics.FastPathCount;
            metrics["Candidate.FallbackSearchCount"] = diagnostics.FallbackSearchCount;
            metrics["Candidate.EdgeSeedCount"] = diagnostics.EdgeSeedCount;
            metrics["Candidate.EdgeSearchBestCount"] = diagnostics.EdgeSearchBestCount;
            metrics["Candidate.HybridCandidateCount"] = diagnostics.HybridCandidateCount;
            metrics["Candidate.HybridVerifiedCount"] = diagnostics.HybridVerifiedCount;
            metrics["Candidate.ImageProposalInVerificationCount"] = diagnostics.ImageProposalInVerificationCount;
            metrics["Candidate.ImageProposalSelectedCount"] = diagnostics.ImageProposalSelectedCount;
            metrics["Candidate.FallbackSelectedCount"] = diagnostics.FallbackSelectedCount;
            metrics["Candidate.MaxImageProposalEdgeScore"] = NormalizeDiagnosticScore(diagnostics.MaxImageProposalEdgeScore);
            metrics["Candidate.MaxImageProposalImageScore"] = NormalizeDiagnosticScore(diagnostics.MaxImageProposalImageScore);
            metrics["Candidate.MaxEdgeSearchScore"] = NormalizeDiagnosticScore(diagnostics.MaxEdgeSearchScore);
            metrics["Candidate.PyramidProposalScale"] = PyramidPositionProposalScale;
            metrics["Candidate.PyramidProposalAttemptCount"] = diagnostics.PyramidProposalAttemptCount;
            metrics["Candidate.PyramidProposalAcceptedCount"] = diagnostics.PyramidProposalAcceptedCount;
            metrics["Candidate.PyramidProposalFallbackCount"] = diagnostics.PyramidProposalFallbackCount;
            metrics["Candidate.PyramidProposalCandidateCount"] = diagnostics.PyramidProposalCandidateCount;
            metrics["Candidate.PyramidProposalVerifiedCount"] = diagnostics.PyramidProposalVerifiedCount;
            metrics["Candidate.MaxPyramidProposalScore"] = NormalizeDiagnosticScore(diagnostics.MaxPyramidProposalScore);
            metrics["Candidate.MaxPyramidVerifiedScore"] = NormalizeDiagnosticScore(diagnostics.MaxPyramidVerifiedScore);
            metrics["Candidate.AmbiguousSelectionCount"] = diagnostics.AmbiguousSelectionCount;
            metrics["Candidate.AmbiguousAlternativeCount"] = diagnostics.AmbiguousAlternativeCount;
            metrics["Candidate.SameScaleAmbiguousAlternativeCount"] = diagnostics.SameScaleAmbiguousAlternativeCount;
            metrics["Candidate.DifferentScaleAmbiguousAlternativeCount"] = diagnostics.DifferentScaleAmbiguousAlternativeCount;
            metrics["Candidate.MaxAmbiguousAlternativeScore"] = NormalizeDiagnosticScore(diagnostics.MaxAmbiguousAlternativeScore);
            metrics["Candidate.MinAmbiguousScoreGap"] = NormalizeDiagnosticScore(diagnostics.MinAmbiguousScoreGap);
            metrics["Candidate.MaxAmbiguousDistance"] = NormalizeDiagnosticScore(diagnostics.MaxAmbiguousDistance);
            metrics["Candidate.MaxAmbiguousScaleDelta"] = NormalizeDiagnosticScore(diagnostics.MaxAmbiguousScaleDelta);
            metrics["Candidate.ScaleAmbiguityRisk"] = scaleSearchEnabled
                && diagnostics.AmbiguousSelectionCount > 0
                ? 1D
                : 0D;
        }

        private static double NormalizeDiagnosticScore(double value)
        {
            return double.IsNaN(value) || double.IsInfinity(value) ? 0D : value;
        }

        private void RecordImageProposalDiagnostics(MatchCandidate imageProposal)
        {
            if (!ShouldUseHybridVerify())
            {
                return;
            }

            if (imageProposal == null)
            {
                candidateDiagnostics.ImageProposalMissingCount++;
                return;
            }

            candidateDiagnostics.ImageProposalCount++;
            candidateDiagnostics.MaxImageProposalEdgeScore = Math.Max(
                candidateDiagnostics.MaxImageProposalEdgeScore,
                imageProposal.Score);
            if (!double.IsNaN(imageProposal.ImageVerifyScore))
            {
                candidateDiagnostics.MaxImageProposalImageScore = Math.Max(
                    candidateDiagnostics.MaxImageProposalImageScore,
                    imageProposal.ImageVerifyScore);
            }
        }

        private void RecordEdgeSearchDiagnostics(IEnumerable<MatchCandidate> candidateSeeds, MatchCandidate best)
        {
            if (!ShouldUseHybridVerify())
            {
                return;
            }

            candidateDiagnostics.EdgeSeedCount += candidateSeeds?.Count() ?? 0;
            if (best == null)
            {
                return;
            }

            candidateDiagnostics.EdgeSearchBestCount++;
            candidateDiagnostics.MaxEdgeSearchScore = Math.Max(candidateDiagnostics.MaxEdgeSearchScore, best.Score);
        }

        private void RecordCandidateAmbiguityDiagnostics(
            IEnumerable<MatchCandidate> candidates,
            MatchCandidate selected,
            Mat template)
        {
            EvaluateUniqueMatch(candidates, selected, template);

            if (candidates == null || selected == null || OpenCvHelper.IsImageEmpty(template))
            {
                return;
            }

            double distanceThreshold = Math.Max(8D, Math.Min(template.Width, template.Height) * CandidateAmbiguityDistanceFactor);
            int ambiguousAlternativeCount = 0;
            int sameScaleAmbiguousAlternativeCount = 0;
            int differentScaleAmbiguousAlternativeCount = 0;
            double maxAlternativeScore = double.NegativeInfinity;
            double minScoreGap = double.PositiveInfinity;
            double maxDistance = double.NegativeInfinity;
            double maxScaleDelta = double.NegativeInfinity;

            foreach (MatchCandidate candidate in candidates)
            {
                if (candidate == null || IsSameCandidate(candidate, selected))
                {
                    continue;
                }

                double dx = candidate.TemplateCenter.X - selected.TemplateCenter.X;
                double dy = candidate.TemplateCenter.Y - selected.TemplateCenter.Y;
                double distance = Math.Sqrt((dx * dx) + (dy * dy));
                if (distance < distanceThreshold)
                {
                    continue;
                }

                double scoreGap = selected.Score - candidate.Score;
                if (scoreGap > CandidateAmbiguityScoreGapThreshold)
                {
                    continue;
                }

                ambiguousAlternativeCount++;
                double scaleDelta = Math.Abs(candidate.Scale - selected.Scale);
                if (scaleDelta <= 0.0001D)
                {
                    sameScaleAmbiguousAlternativeCount++;
                }
                else
                {
                    differentScaleAmbiguousAlternativeCount++;
                }

                maxAlternativeScore = Math.Max(maxAlternativeScore, candidate.Score);
                minScoreGap = Math.Min(minScoreGap, scoreGap);
                maxDistance = Math.Max(maxDistance, distance);
                maxScaleDelta = Math.Max(maxScaleDelta, scaleDelta);
            }

            if (ambiguousAlternativeCount <= 0)
            {
                return;
            }

            candidateDiagnostics.AmbiguousSelectionCount++;
            candidateDiagnostics.AmbiguousAlternativeCount += ambiguousAlternativeCount;
            candidateDiagnostics.SameScaleAmbiguousAlternativeCount += sameScaleAmbiguousAlternativeCount;
            candidateDiagnostics.DifferentScaleAmbiguousAlternativeCount += differentScaleAmbiguousAlternativeCount;
            candidateDiagnostics.MaxAmbiguousAlternativeScore = Math.Max(
                candidateDiagnostics.MaxAmbiguousAlternativeScore,
                maxAlternativeScore);
            candidateDiagnostics.MinAmbiguousScoreGap = Math.Min(
                candidateDiagnostics.MinAmbiguousScoreGap,
                minScoreGap);
            candidateDiagnostics.MaxAmbiguousDistance = Math.Max(
                candidateDiagnostics.MaxAmbiguousDistance,
                maxDistance);
            candidateDiagnostics.MaxAmbiguousScaleDelta = Math.Max(
                candidateDiagnostics.MaxAmbiguousScaleDelta,
                maxScaleDelta);
        }

        private void RecordCandidateDisplayEvidence(
            MatchCandidate selected,
            IEnumerable<MatchCandidate> candidates,
            Mat template,
            Rect roi,
            bool useRoi)
        {
            if (selected == null)
            {
                return;
            }

            if (candidateDiagnostics.SelectedCandidate == null)
            {
                candidateDiagnostics.SelectedCandidate = CreateCandidateDiagnostic(
                    selected,
                    roi,
                    useRoi);
            }

            if (candidateDiagnostics.StrongestSpatialAlternative != null
                || OpenCvHelper.IsImageEmpty(template))
            {
                return;
            }

            double distanceThreshold = Math.Max(
                8D,
                Math.Min(template.Width, template.Height) * CandidateAmbiguityDistanceFactor);
            MatchCandidate strongest = null;
            foreach (MatchCandidate candidate in candidates ?? Enumerable.Empty<MatchCandidate>())
            {
                if (candidate == null || IsSameCandidate(candidate, selected))
                {
                    continue;
                }

                double dx = candidate.TemplateCenter.X - selected.TemplateCenter.X;
                double dy = candidate.TemplateCenter.Y - selected.TemplateCenter.Y;
                if (Math.Sqrt((dx * dx) + (dy * dy)) < distanceThreshold)
                {
                    continue;
                }

                if (strongest == null
                    || GetUniqueMatchSelectionScore(candidate) > GetUniqueMatchSelectionScore(strongest))
                {
                    strongest = candidate;
                }
            }

            if (strongest != null)
            {
                candidateDiagnostics.StrongestSpatialAlternative = CreateCandidateDiagnostic(
                    strongest,
                    roi,
                    useRoi);
            }
        }

        private static EdgeBasedMatchingCandidateDiagnostic CreateCandidateDiagnostic(
            MatchCandidate candidate,
            Rect roi,
            bool useRoi)
        {
            if (candidate == null)
            {
                return null;
            }

            float offsetX = useRoi ? roi.X : 0F;
            float offsetY = useRoi ? roi.Y : 0F;
            return new EdgeBasedMatchingCandidateDiagnostic
            {
                Score = candidate.Score,
                Angle = candidate.Angle,
                Scale = NormalizeScale(candidate.Scale),
                Center = new PointF(
                    (float)candidate.TemplateCenter.X + offsetX,
                    (float)candidate.TemplateCenter.Y + offsetY),
                Bounds = new RectangleF(
                    candidate.Bounds.X + offsetX,
                    candidate.Bounds.Y + offsetY,
                    candidate.Bounds.Width,
                    candidate.Bounds.Height)
            };
        }

        private void EvaluateUniqueMatch(
            IEnumerable<MatchCandidate> candidates,
            MatchCandidate selected,
            Mat template)
        {
            matchDecision.EvaluateUniqueMatch(candidates, selected, template);
        }

        private double GetUniqueMatchSelectionScore(MatchCandidate candidate)
        {
            return matchDecision.GetUniqueMatchSelectionScore(candidate);
        }

        private void RecordUniqueMatchNoMatch(MatchCandidate candidate)
        {
            matchDecision.RecordUniqueMatchNoMatch(candidate);
        }

        private bool TryAcceptUniqueMatch(MatchCandidate candidate)
        {
            return matchDecision.TryAcceptUniqueMatch(candidate);
        }

        private sealed class EdgeMatchDecision
    {
        private readonly EdgeBasedTemplateMatchingTool owner;

        public EdgeMatchDecision(EdgeBasedTemplateMatchingTool owner)
        {
            this.owner = owner;
        }

        private IOpenCVPropertyEdgeBasedTemplateMatching property => owner.property;

        private CandidateDiagnostics candidateDiagnostics => owner.candidateDiagnostics;

        private List<MatchingResult> results => owner.results;

        private void SetMatchingFailure(VisionToolErrorCode errorCode, string message)
            => owner.SetMatchingFailure(errorCode, message);

        private string FormatMatchingOptions() => owner.FormatMatchingOptions();

        private bool ShouldUseHybridVerify() => owner.ShouldUseHybridVerify();

        public void EvaluateUniqueMatch(
            IEnumerable<MatchCandidate> candidates,
            MatchCandidate selected,
            Mat template)
        {
            if (!property.USE_UNIQUE_MATCH_VALIDATION
                || selected == null
                || OpenCvHelper.IsImageEmpty(template))
            {
                return;
            }

            double distanceThreshold = Math.Max(
                8D,
                Math.Min(template.Width, template.Height) * CandidateAmbiguityDistanceFactor);
            double selectedScore = GetUniqueMatchSelectionScore(selected);
            double strongestAlternativeScore = double.NegativeInfinity;
            int eligibleAlternativeCount = 0;
            int plausibleAlternativeCount = 0;

            foreach (MatchCandidate candidate in candidates ?? Enumerable.Empty<MatchCandidate>())
            {
                if (candidate == null
                    || IsSameCandidate(candidate, selected)
                    || candidate.Score < property.SCORE_MIN)
                {
                    continue;
                }

                double dx = candidate.TemplateCenter.X - selected.TemplateCenter.X;
                double dy = candidate.TemplateCenter.Y - selected.TemplateCenter.Y;
                if (Math.Sqrt((dx * dx) + (dy * dy)) < distanceThreshold)
                {
                    continue;
                }

                eligibleAlternativeCount++;
                double candidateSelectionScore = GetUniqueMatchSelectionScore(candidate);
                strongestAlternativeScore = Math.Max(
                    strongestAlternativeScore,
                    candidateSelectionScore);
                if (selectedScore - candidateSelectionScore < property.UNIQUE_MATCH_MIN_SCORE_MARGIN)
                {
                    plausibleAlternativeCount++;
                }
            }

            double alternativeScore = eligibleAlternativeCount > 0
                ? strongestAlternativeScore
                : 0D;
            double scoreMargin = selectedScore - alternativeScore;

            candidateDiagnostics.UniqueMatchState = plausibleAlternativeCount > 0
                ? UniqueMatchState.Ambiguous
                : UniqueMatchState.Success;
            candidateDiagnostics.UniqueMatchPlausibleAlternativeCount = plausibleAlternativeCount;
            candidateDiagnostics.UniqueMatchSelectedScore = selectedScore;
            candidateDiagnostics.UniqueMatchStrongestAlternativeScore = alternativeScore;
            candidateDiagnostics.UniqueMatchScoreMargin = scoreMargin;
            candidateDiagnostics.UniqueMatchDistanceThreshold = distanceThreshold;
        }

        public double GetUniqueMatchSelectionScore(MatchCandidate candidate)
        {
            return ShouldUseHybridVerify() && !double.IsNaN(candidate.HybridScore)
                ? candidate.HybridScore
                : candidate.Score;
        }

        public void RecordUniqueMatchNoMatch(MatchCandidate candidate)
        {
            if (!property.USE_UNIQUE_MATCH_VALIDATION)
            {
                return;
            }

            candidateDiagnostics.UniqueMatchState = UniqueMatchState.NoMatch;
            candidateDiagnostics.UniqueMatchSelectedScore = candidate?.Score ?? 0D;
            candidateDiagnostics.UniqueMatchStrongestAlternativeScore = 0D;
            candidateDiagnostics.UniqueMatchScoreMargin = 0D;
        }

        public bool TryAcceptUniqueMatch(MatchCandidate candidate)
        {
            if (!property.USE_UNIQUE_MATCH_VALIDATION)
            {
                return true;
            }

            if (candidateDiagnostics.UniqueMatchState == UniqueMatchState.Ambiguous)
            {
                SetMatchingFailure(
                    VisionToolErrorCode.MatchingAmbiguous,
                    $"Edge based matching rejected an ambiguous unique match. "
                    + $"BestScore={candidateDiagnostics.UniqueMatchSelectedScore * 100D:0.###}, "
                    + $"AlternativeScore={candidateDiagnostics.UniqueMatchStrongestAlternativeScore * 100D:0.###}, "
                    + $"ScoreMargin={candidateDiagnostics.UniqueMatchScoreMargin * 100D:0.###}, "
                    + $"RequiredMargin={property.UNIQUE_MATCH_MIN_SCORE_MARGIN * 100D:0.###}, "
                    + $"PlausibleAlternatives={candidateDiagnostics.UniqueMatchPlausibleAlternativeCount}, "
                    + FormatMatchingOptions());
                return false;
            }

            if (candidateDiagnostics.UniqueMatchState == UniqueMatchState.Disabled)
            {
                candidateDiagnostics.UniqueMatchState = UniqueMatchState.Success;
                candidateDiagnostics.UniqueMatchSelectedScore = GetUniqueMatchSelectionScore(candidate);
                candidateDiagnostics.UniqueMatchStrongestAlternativeScore = 0D;
                candidateDiagnostics.UniqueMatchScoreMargin = candidateDiagnostics.UniqueMatchSelectedScore;
            }

            return true;
        }

    }

        private static void AddModelQualityMetrics(IDictionary<string, double> metrics, EdgeTemplateModel model)
        {
            if (metrics == null || model == null)
            {
                return;
            }

            int width = Math.Max(0, model.Width);
            int height = Math.Max(0, model.Height);
            List<TemplateEdgePoint> points = model.Points ?? new List<TemplateEdgePoint>();
            int sampledPointCount = points.Count;
            int rawPointCount = Math.Max(model.RawPointCount, sampledPointCount);
            double templateArea = width > 0 && height > 0
                ? (double)width * height
                : 0D;

            metrics["Model.TemplateWidth"] = width;
            metrics["Model.TemplateHeight"] = height;
            metrics["Model.RawEdgePointCount"] = rawPointCount;
            metrics["Model.EdgePointCount"] = sampledPointCount;
            metrics["Model.EdgeDensity"] = templateArea > 0D
                ? sampledPointCount / templateArea
                : 0D;
            metrics["Model.PointSampleRatio"] = rawPointCount > 0
                ? (double)sampledPointCount / rawPointCount
                : 0D;
            metrics["Model.PointLimitHit"] = rawPointCount > sampledPointCount ? 1D : 0D;

            double coverageX = 0D;
            double coverageY = 0D;
            double quadrantBalance = 0D;
            if (sampledPointCount > 0 && width > 0 && height > 0)
            {
                int minX = points.Min(point => point.X);
                int maxX = points.Max(point => point.X);
                int minY = points.Min(point => point.Y);
                int maxY = points.Max(point => point.Y);
                coverageX = (maxX - minX + 1D) / width;
                coverageY = (maxY - minY + 1D) / height;

                int[] quadrantCounts = new int[4];
                foreach (TemplateEdgePoint point in points)
                {
                    bool right = point.X >= model.Center.X;
                    bool bottom = point.Y >= model.Center.Y;
                    int index = (right ? 1 : 0) + (bottom ? 2 : 0);
                    quadrantCounts[index]++;
                }

                int maxQuadrant = quadrantCounts.Max();
                int minQuadrant = quadrantCounts.Min();
                quadrantBalance = maxQuadrant > 0
                    ? (double)minQuadrant / maxQuadrant
                    : 0D;
            }

            double coverageArea = coverageX * coverageY;
            double smallTemplateRisk = Math.Min(width, height) < ModelSmallTemplateMinDimension ? 1D : 0D;
            double lowEdgePointRisk = sampledPointCount < ModelLowEdgePointThreshold ? 1D : 0D;
            double lowCoverageRisk = coverageArea < ModelLowCoverageAreaThreshold ? 1D : 0D;
            double scaleCoverageWarningRisk = coverageArea < ModelScaleSearchCoverageWarningThreshold ? 1D : 0D;
            double lowQuadrantBalanceRisk = quadrantBalance < ModelLowQuadrantBalanceThreshold ? 1D : 0D;
            metrics["Model.EdgeCoverageX"] = coverageX;
            metrics["Model.EdgeCoverageY"] = coverageY;
            metrics["Model.EdgeCoverageArea"] = coverageArea;
            metrics["Model.QuadrantBalance"] = quadrantBalance;
            metrics["Model.SmallTemplateRisk"] = smallTemplateRisk;
            metrics["Model.LowEdgePointRisk"] = lowEdgePointRisk;
            metrics["Model.LowCoverageRisk"] = lowCoverageRisk;
            metrics["Model.LowQuadrantBalanceRisk"] = lowQuadrantBalanceRisk;
            metrics["Model.ScaleCoverageWarningRisk"] = scaleCoverageWarningRisk;
            metrics["Model.ScaleSearchRisk"] = Math.Max(
                Math.Max(smallTemplateRisk, lowEdgePointRisk),
                Math.Max(scaleCoverageWarningRisk, lowQuadrantBalanceRisk));
        }

        private static void AddModelPyramidDiagnostics(IDictionary<string, double> metrics, EdgeTemplateModel model)
        {
            if (metrics == null || model == null)
            {
                return;
            }

            List<TemplateEdgePoint> points = model.Points ?? new List<TemplateEdgePoint>();
            int highestUsableLevel = 0;
            int emittedLevelCount = 0;
            for (int level = 0; level < ModelPyramidMaxDiagnosticLevels; level++)
            {
                int divisor = 1 << level;
                int width = Math.Max(1, (int)Math.Ceiling(model.Width / (double)divisor));
                int height = Math.Max(1, (int)Math.Ceiling(model.Height / (double)divisor));
                if (width < 1 || height < 1)
                {
                    break;
                }

                PyramidLevelDiagnostics levelDiagnostics = CreatePyramidLevelDiagnostics(points, width, height, divisor);
                bool usable = width >= ModelPyramidMinUsableDimension
                    && height >= ModelPyramidMinUsableDimension
                    && levelDiagnostics.EdgePointCount >= ModelLowEdgePointThreshold
                    && levelDiagnostics.CoverageArea >= ModelLowCoverageAreaThreshold;
                if (usable)
                {
                    highestUsableLevel = level;
                }

                string prefix = string.Format(CultureInfo.InvariantCulture, "Model.Pyramid.Level{0}", level);
                metrics[$"{prefix}.Width"] = width;
                metrics[$"{prefix}.Height"] = height;
                metrics[$"{prefix}.EdgePointCount"] = levelDiagnostics.EdgePointCount;
                metrics[$"{prefix}.EdgeDensity"] = width > 0 && height > 0
                    ? levelDiagnostics.EdgePointCount / ((double)width * height)
                    : 0D;
                metrics[$"{prefix}.CoverageArea"] = levelDiagnostics.CoverageArea;
                metrics[$"{prefix}.QuadrantBalance"] = levelDiagnostics.QuadrantBalance;
                metrics[$"{prefix}.Usable"] = usable ? 1D : 0D;
                emittedLevelCount++;

                if (width <= 1 && height <= 1)
                {
                    break;
                }
            }

            // Diagnostic only: do not let pyramid estimates change search behavior without a survival audit.
            metrics["Model.Pyramid.LevelCountEstimate"] = emittedLevelCount;
            metrics["Model.Pyramid.HighestUsableLevel"] = highestUsableLevel;
        }

        private static PyramidLevelDiagnostics CreatePyramidLevelDiagnostics(
            IEnumerable<TemplateEdgePoint> points,
            int width,
            int height,
            int divisor)
        {
            HashSet<long> scaledPoints = new HashSet<long>();
            foreach (TemplateEdgePoint point in points ?? Enumerable.Empty<TemplateEdgePoint>())
            {
                int x = Clamp((int)Math.Floor(point.X / (double)divisor), 0, width - 1);
                int y = Clamp((int)Math.Floor(point.Y / (double)divisor), 0, height - 1);
                scaledPoints.Add(((long)y << 32) | (uint)x);
            }

            if (scaledPoints.Count == 0 || width <= 0 || height <= 0)
            {
                return new PyramidLevelDiagnostics(0, 0D, 0D);
            }

            int minX = width;
            int maxX = 0;
            int minY = height;
            int maxY = 0;
            int[] quadrantCounts = new int[4];
            double centerX = (width - 1D) / 2D;
            double centerY = (height - 1D) / 2D;
            foreach (long key in scaledPoints)
            {
                int x = (int)(key & 0xFFFFFFFF);
                int y = (int)(key >> 32);
                minX = Math.Min(minX, x);
                maxX = Math.Max(maxX, x);
                minY = Math.Min(minY, y);
                maxY = Math.Max(maxY, y);

                bool right = x >= centerX;
                bool bottom = y >= centerY;
                int index = (right ? 1 : 0) + (bottom ? 2 : 0);
                quadrantCounts[index]++;
            }

            double coverageX = (maxX - minX + 1D) / width;
            double coverageY = (maxY - minY + 1D) / height;
            int maxQuadrant = quadrantCounts.Max();
            int minQuadrant = quadrantCounts.Min();
            double quadrantBalance = maxQuadrant > 0
                ? (double)minQuadrant / maxQuadrant
                : 0D;
            return new PyramidLevelDiagnostics(scaledPoints.Count, coverageX * coverageY, quadrantBalance);
        }

        private void AddSearchSpaceDiagnostics(IDictionary<string, double> metrics, EdgeTemplateModel model)
        {
            if (metrics == null || model == null)
            {
                return;
            }

            Rect roi = NormalizeRoi(property?.USE_ROI == true ? property.CvROI : new Rect());
            int searchWidth = Math.Max(0, roi.Width);
            int searchHeight = Math.Max(0, roi.Height);
            int positionsX = Math.Max(0, searchWidth - model.Width + 1);
            int positionsY = Math.Max(0, searchHeight - model.Height + 1);
            double positionsPerAngle = (double)positionsX * positionsY;
            int angleCount = CreateSearchAngles().Count();
            int coarseAngleCount = ShouldUseCoarseToFineAngleSearch()
                ? CreateSearchAngles(property.COARSE_ANGLE_STEP).Count()
                : angleCount;
            int scaleCount = CreateSearchScales().Count();

            metrics["Candidate.SearchAreaWidth"] = searchWidth;
            metrics["Candidate.SearchAreaHeight"] = searchHeight;
            metrics["Candidate.SearchAngleCount"] = angleCount;
            metrics["Candidate.CoarseAngleCount"] = coarseAngleCount;
            metrics["Candidate.SearchScaleCount"] = scaleCount;
            metrics["Candidate.EstimatedPositionsPerAngle"] = positionsPerAngle;
            metrics["Candidate.EstimatedRawSearchPositions"] = positionsPerAngle * angleCount * scaleCount;
            metrics["Candidate.EstimatedCoarseSearchPositions"] = positionsPerAngle * coarseAngleCount * scaleCount;
        }

        private MatchCandidate FindBestCandidate(
            Mat source,
            GradientImage gradients,
            TemplateModelCache modelCache,
            List<RotatedEdgeTemplateModel> searchModels,
            List<RectangleF> suppressedBounds,
            out List<MatchCandidate> candidateSeeds)
        {
            return candidateSearch.FindBestCandidate(
                source,
                gradients,
                modelCache,
                searchModels,
                suppressedBounds,
                out candidateSeeds);
        }

        private MatchCandidate ApplySubpixelRefinement(
            GradientImage gradients,
            TemplateModelCache modelCache,
            MatchCandidate candidate)
        {
            return candidateSearch.ApplySubpixelRefinement(gradients, modelCache, candidate);
        }

        private void ApplyGlobalPolarityState(
            GradientImage gradients,
            TemplateModelCache modelCache,
            MatchCandidate candidate)
        {
            candidateSearch.ApplyGlobalPolarityState(gradients, modelCache, candidate);
        }

        private List<RotatedEdgeTemplateModel> GetSearchModels(TemplateModelCache modelCache, double angleStep)
        {
            return candidateSearch.GetSearchModels(modelCache, angleStep);
        }

        private IEnumerable<double> CreateSearchAngles()
        {
            return candidateSearch.CreateSearchAngles();
        }

        private IEnumerable<double> CreateSearchAngles(double angleStep)
        {
            return candidateSearch.CreateSearchAngles(angleStep);
        }

        private IEnumerable<double> CreateSearchAnglesAround(double centerAngle, double radius, double angleStep)
        {
            return candidateSearch.CreateSearchAnglesAround(centerAngle, radius, angleStep);
        }

        private IEnumerable<double> CreateSearchScales()
        {
            return candidateSearch.CreateSearchScales();
        }

        private bool ShouldUseCoarseToFineAngleSearch()
        {
            return candidateSearch.ShouldUseCoarseToFineAngleSearch();
        }

        private static bool ShouldTrackCandidateSeed(List<MatchCandidate> seeds, double score, int capacity)
        {
            return EdgeCandidateSearch.ShouldTrackCandidateSeed(seeds, score, capacity);
        }

        private static bool IsBetterCandidate(MatchCandidate candidate, MatchCandidate currentBest)
        {
            return EdgeCandidateSearch.IsBetterCandidate(candidate, currentBest);
        }

        private static bool IsBetterScore(double score, double centerX, double centerY, MatchCandidate currentBest)
        {
            return EdgeCandidateSearch.IsBetterScore(score, centerX, centerY, currentBest);
        }

        private static void TrackCandidateSeed(List<MatchCandidate> seeds, MatchCandidate candidate, int capacity)
        {
            EdgeCandidateSearch.TrackCandidateSeed(seeds, candidate, capacity);
        }

        private static void MergeCandidateSeeds(List<MatchCandidate> target, IEnumerable<MatchCandidate> source, int capacity)
        {
            EdgeCandidateSearch.MergeCandidateSeeds(target, source, capacity);
        }

        private static bool IsSameCandidate(MatchCandidate left, MatchCandidate right)
        {
            return EdgeCandidateSearch.IsSameCandidate(left, right);
        }

        private static MatchCandidate CreateMatchCandidate(
            RotatedEdgeTemplateModel model,
            int centerX,
            int centerY,
            RectangleF bounds,
            double score)
        {
            return EdgeCandidateSearch.CreateMatchCandidate(model, centerX, centerY, bounds, score);
        }

        private double ScoreCandidate(GradientImage gradients, RotatedEdgeTemplateModel model, int centerX, int centerY)
        {
            return candidateSearch.ScoreCandidate(gradients, model, centerX, centerY);
        }

        private static RotatedEdgeTemplateModel CreateRotatedModel(EdgeTemplateModel model, double angle)
        {
            return EdgeCandidateSearch.CreateRotatedModel(model, angle);
        }

        private sealed class EdgeCandidateSearch
    {
        private readonly EdgeBasedTemplateMatchingTool owner;

        public EdgeCandidateSearch(EdgeBasedTemplateMatchingTool owner)
        {
            this.owner = owner;
        }

        private IOpenCVPropertyEdgeBasedTemplateMatching property => owner.property;

        private CandidateDiagnostics candidateDiagnostics => owner.candidateDiagnostics;

        private EdgeTemplateModelStore modelStore => owner.modelStore;

        private long StartPhaseTiming() => owner.StartPhaseTiming();

        private void StopPhaseTiming(string phaseName, long startTimestamp) => owner.StopPhaseTiming(phaseName, startTimestamp);

        private int GetCandidateSeedCapacity() => owner.GetCandidateSeedCapacity();

        private bool ShouldUseHybridSpatialSeeds() => owner.ShouldUseHybridSpatialSeeds();

        public MatchCandidate FindBestCandidate(
            Mat source,
            GradientImage gradients,
            TemplateModelCache modelCache,
            List<RotatedEdgeTemplateModel> searchModels,
            List<RectangleF> suppressedBounds,
            out List<MatchCandidate> candidateSeeds)
        {
            if (TryFindBestCandidateFromPyramidPositionProposal(
                source,
                gradients,
                modelCache,
                suppressedBounds,
                out MatchCandidate proposalCandidate,
                out candidateSeeds))
            {
                return proposalCandidate;
            }

            if (ShouldUseCoarseToFineAngleSearch())
            {
                return FindBestCandidateCoarseToFine(gradients, modelCache, suppressedBounds, out candidateSeeds);
            }

            return FindBestCandidateFromModels(gradients, searchModels, suppressedBounds, out candidateSeeds);
        }

        private bool TryFindBestCandidateFromPyramidPositionProposal(
            Mat source,
            GradientImage gradients,
            TemplateModelCache modelCache,
            List<RectangleF> suppressedBounds,
            out MatchCandidate candidate,
            out List<MatchCandidate> candidateSeeds)
        {
            candidate = null;
            candidateSeeds = null;
            if (!ShouldUsePyramidPositionProposal(source, modelCache?.Model))
            {
                return false;
            }

            candidateDiagnostics.PyramidProposalAttemptCount++;
            long proposalStart = StartPhaseTiming();
            List<MatchCandidate> proposals = CreateHalfScalePositionProposals(source, modelCache);
            StopPhaseTiming("PyramidProposal.Search", proposalStart);
            candidateDiagnostics.PyramidProposalCandidateCount += proposals.Count;
            if (proposals.Count == 0)
            {
                candidateDiagnostics.PyramidProposalFallbackCount++;
                return false;
            }

            int seedCapacity = Math.Max(GetCandidateSeedCapacity(), Math.Max(1, property.PYRAMID_POSITION_TOP_N));
            candidateSeeds = new List<MatchCandidate>(seedCapacity);
            MatchCandidate best = null;
            long verifyStart = StartPhaseTiming();
            foreach (MatchCandidate proposal in proposals)
            {
                candidateDiagnostics.MaxPyramidProposalScore = Math.Max(
                    candidateDiagnostics.MaxPyramidProposalScore,
                    proposal.Score);

                double proposedTemplateCenterX = proposal.TemplateCenter.X / PyramidPositionProposalScale;
                double proposedTemplateCenterY = proposal.TemplateCenter.Y / PyramidPositionProposalScale;
                foreach (double angle in CreatePyramidProposalRefineAngles(proposal.Angle))
                {
                    RotatedEdgeTemplateModel model = modelCache.GetRotatedModel(angle);
                    // The proposal is produced by a separately scaled template model.
                    // Convert through the visual template center, then re-derive the full-resolution
                    // model origin for each refine angle so rotated offset differences do not shift
                    // the verification window away from the true candidate.
                    int centerX = (int)Math.Round(proposedTemplateCenterX - model.TemplateCenterOffsetX);
                    int centerY = (int)Math.Round(proposedTemplateCenterY - model.TemplateCenterOffsetY);
                    MatchCandidate verified = FindBestCandidateNear(
                        gradients,
                        model,
                        suppressedBounds,
                        centerX,
                        centerY,
                        Math.Max(PyramidPositionProposalRefineRadius, Math.Max(1, property.SEARCH_STEP) * 2));
                    if (verified == null)
                    {
                        continue;
                    }

                    candidateDiagnostics.PyramidProposalVerifiedCount++;
                    candidateDiagnostics.MaxPyramidVerifiedScore = Math.Max(
                        candidateDiagnostics.MaxPyramidVerifiedScore,
                        verified.Score);
                    TrackCandidateSeed(candidateSeeds, verified, seedCapacity);
                    if (IsBetterCandidate(verified, best))
                    {
                        best = verified;
                    }
                }
            }

            StopPhaseTiming("PyramidProposal.Verify", verifyStart);
            if (best == null)
            {
                candidateDiagnostics.PyramidProposalFallbackCount++;
                return false;
            }

            double acceptScore = Math.Max(property.SCORE_MIN, property.PYRAMID_POSITION_MIN_SCORE);
            double weakVerifiedScore = acceptScore + PyramidPositionProposalWeakVerifiedMargin;
            // Proposal verification is a speed path, not the final fallback.
            // If the full-resolution verified score only barely clears the operator threshold,
            // use the existing full search so weak/downscaled proposals do not lock in a bad candidate.
            if (best.Score < weakVerifiedScore)
            {
                candidateDiagnostics.PyramidProposalFallbackCount++;
                return false;
            }

            candidateDiagnostics.PyramidProposalAcceptedCount++;
            candidate = best;
            TrackCandidateSeed(candidateSeeds, candidate, seedCapacity);
            return true;
        }

        private bool ShouldUsePyramidPositionProposal(Mat source, EdgeTemplateModel model)
        {
            if (property?.USE_PYRAMID_POSITION_PROPOSAL != true
                || property.USE_FIND_SCALE
                || OpenCvHelper.IsImageEmpty(source)
                || model == null
                || property.PYRAMID_POSITION_TOP_N <= 0)
            {
                return false;
            }

            int scaledTemplateWidth = (int)Math.Round(model.Width * PyramidPositionProposalScale);
            int scaledTemplateHeight = (int)Math.Round(model.Height * PyramidPositionProposalScale);
            return scaledTemplateWidth >= PyramidPositionProposalMinTemplateSize
                && scaledTemplateHeight >= PyramidPositionProposalMinTemplateSize
                && source.Width > model.Width
                && source.Height > model.Height;
        }

        private List<MatchCandidate> CreateHalfScalePositionProposals(Mat source, TemplateModelCache modelCache)
        {
            using (Mat scaledSource = ResizeForPyramidProposal(source, PyramidPositionProposalScale))
            using (Mat scaledTemplate = ResizeForPyramidProposal(modelCache.PreparedTemplate, PyramidPositionProposalScale))
            using (GradientImage scaledGradients = GradientImage.Create(scaledSource, property.MIN_GRADIENT_MAGNITUDE))
            {
                EdgeTemplateModel scaledModel = modelStore.CreateModel(scaledTemplate);
                if (scaledModel.Points.Count < ModelLowEdgePointThreshold)
                {
                    return new List<MatchCandidate>();
                }

                RotatedEdgeTemplateModel[] proposalModels = CreatePyramidProposalAngles()
                    .Select(angle => CreateRotatedModel(scaledModel, angle))
                    .ToArray();
                int proposalStep = Math.Max(
                    1,
                    (int)Math.Round(Math.Max(1, property.SEARCH_STEP) * PyramidPositionProposalScale));
                return FindTopCandidatesFromModels(
                    scaledGradients,
                    proposalModels,
                    Math.Max(1, property.PYRAMID_POSITION_TOP_N),
                    proposalStep);
            }
        }

        private IEnumerable<double> CreatePyramidProposalAngles()
        {
            if (!property.USE_FIND_ANGLE)
            {
                yield return 0D;
                yield break;
            }

            double step = ShouldUseCoarseToFineAngleSearch()
                ? Math.Max(property.FIND_ANGLE, property.COARSE_ANGLE_STEP)
                : Math.Max(property.FIND_ANGLE, property.COARSE_ANGLE_STEP);
            foreach (double angle in CreateSearchAnglesInRange(property.FIND_ANGLE_MIN, property.FIND_ANGLE_MAX, step))
            {
                yield return angle;
            }
        }

        private IEnumerable<double> CreatePyramidProposalRefineAngles(double proposalAngle)
        {
            if (!property.USE_FIND_ANGLE)
            {
                yield return 0D;
                yield break;
            }

            double proposalStep = ShouldUseCoarseToFineAngleSearch()
                ? Math.Max(property.FIND_ANGLE, property.COARSE_ANGLE_STEP)
                : Math.Max(property.FIND_ANGLE, property.COARSE_ANGLE_STEP);
            double radius = Math.Max(property.FIND_ANGLE, proposalStep / 2D);
            foreach (double angle in CreateSearchAnglesAround(proposalAngle, radius, property.FIND_ANGLE))
            {
                yield return angle;
            }
        }

        private List<MatchCandidate> FindTopCandidatesFromModels(
            GradientImage gradients,
            IEnumerable<RotatedEdgeTemplateModel> models,
            int capacity,
            int step)
        {
            List<MatchCandidate> candidates = new List<MatchCandidate>(capacity);
            foreach (RotatedEdgeTemplateModel model in models ?? Enumerable.Empty<RotatedEdgeTemplateModel>())
            {
                FindTopCandidates(gradients, model, candidates, capacity, step);
            }

            candidates.Sort((left, right) => right.Score.CompareTo(left.Score));
            return candidates.Take(capacity).ToList();
        }

        private void FindTopCandidates(
            GradientImage gradients,
            RotatedEdgeTemplateModel model,
            List<MatchCandidate> candidates,
            int capacity,
            int step)
        {
            int minCenterX = -model.MinOffsetX;
            int minCenterY = -model.MinOffsetY;
            int maxCenterX = gradients.Width - 1 - model.MaxOffsetX;
            int maxCenterY = gradients.Height - 1 - model.MaxOffsetY;
            if (maxCenterX < minCenterX || maxCenterY < minCenterY)
            {
                return;
            }

            CandidateScoreContext scoreContext = CreateScoreContext(gradients, model);
            step = Math.Max(1, step);
            for (int y = minCenterY; y <= maxCenterY; y += step)
            {
                for (int x = minCenterX; x <= maxCenterX; x += step)
                {
                    double score = ScoreCandidate(scoreContext, x, y);
                    if (!ShouldTrackCandidateSeed(candidates, score, capacity))
                    {
                        continue;
                    }

                    RectangleF bounds = CreateLocalBounds(x, y, model);
                    TrackCandidateSeed(candidates, CreateMatchCandidate(model, x, y, bounds, score), capacity);
                }
            }
        }

        private MatchCandidate FindBestCandidateNear(
            GradientImage gradients,
            RotatedEdgeTemplateModel model,
            List<RectangleF> suppressedBounds,
            int centerX,
            int centerY,
            int radius)
        {
            int minCenterX = Math.Max(-model.MinOffsetX, centerX - radius);
            int minCenterY = Math.Max(-model.MinOffsetY, centerY - radius);
            int maxCenterX = Math.Min(gradients.Width - 1 - model.MaxOffsetX, centerX + radius);
            int maxCenterY = Math.Min(gradients.Height - 1 - model.MaxOffsetY, centerY + radius);
            if (maxCenterX < minCenterX || maxCenterY < minCenterY)
            {
                return null;
            }

            bool hasSuppressedBounds = HasSuppressedBounds(suppressedBounds);
            CandidateScoreContext scoreContext = CreateScoreContext(gradients, model);
            MatchCandidate best = null;
            for (int y = minCenterY; y <= maxCenterY; y++)
            {
                for (int x = minCenterX; x <= maxCenterX; x++)
                {
                    RectangleF bounds = CreateLocalBounds(x, y, model);
                    if (hasSuppressedBounds && IsSuppressed(bounds, suppressedBounds))
                    {
                        continue;
                    }

                    double score = ScoreCandidate(scoreContext, x, y);
                    if (IsBetterScore(score, x, y, best))
                    {
                        best = CreateMatchCandidate(model, x, y, bounds, score);
                    }
                }
            }

            return best;
        }

        private static Mat ResizeForPyramidProposal(Mat source, double scale)
        {
            Mat resized = new Mat();
            Cv2.Resize(
                source,
                resized,
                new OpenCvSharp.Size(
                    Math.Max(1, (int)Math.Round(source.Width * scale)),
                    Math.Max(1, (int)Math.Round(source.Height * scale))),
                0D,
                0D,
                InterpolationFlags.Area);
            return resized;
        }

        private MatchCandidate FindBestCandidateFromModels(
            GradientImage gradients,
            IEnumerable<RotatedEdgeTemplateModel> searchModels,
            List<RectangleF> suppressedBounds,
            out List<MatchCandidate> candidateSeeds)
        {
            RotatedEdgeTemplateModel[] models = (searchModels ?? Enumerable.Empty<RotatedEdgeTemplateModel>()).ToArray();
            if (models.Length == 0)
            {
                candidateSeeds = null;
                return null;
            }

            int seedCapacity = GetCandidateSeedCapacity();
            candidateSeeds = seedCapacity > 0 ? new List<MatchCandidate>(seedCapacity) : null;
            if (ShouldUseParallelModelSearch(models.Length))
            {
                object sync = new object();
                List<MatchCandidate> mergedSeeds = candidateSeeds;
                MatchCandidate parallelBest = null;
                Parallel.ForEach(models, searchModel =>
                {
                    List<MatchCandidate> modelSeeds;
                    MatchCandidate candidate = FindBestCandidate(
                        gradients,
                        searchModel,
                        suppressedBounds,
                        false,
                        out modelSeeds);
                    if (candidate == null)
                    {
                        return;
                    }

                    lock (sync)
                    {
                        if (IsBetterCandidate(candidate, parallelBest))
                        {
                            parallelBest = candidate;
                        }

                        MergeCandidateSeeds(mergedSeeds, modelSeeds, seedCapacity);
                        TrackCandidateSeed(mergedSeeds, candidate, seedCapacity);
                    }
                });

                candidateSeeds = mergedSeeds;
                return parallelBest;
            }

            MatchCandidate best = null;
            foreach (RotatedEdgeTemplateModel searchModel in models)
            {
                List<MatchCandidate> modelSeeds;
                MatchCandidate candidate = FindBestCandidate(
                    gradients,
                    searchModel,
                    suppressedBounds,
                    true,
                    out modelSeeds);
                if (IsBetterCandidate(candidate, best))
                {
                    best = candidate;
                }

                MergeCandidateSeeds(candidateSeeds, modelSeeds, seedCapacity);
                TrackCandidateSeed(candidateSeeds, candidate, seedCapacity);
            }

            return best;
        }

        private List<MatchCandidate> FindCandidatesFromModels(
            GradientImage gradients,
            IEnumerable<RotatedEdgeTemplateModel> searchModels,
            List<RectangleF> suppressedBounds)
        {
            RotatedEdgeTemplateModel[] models = (searchModels ?? Enumerable.Empty<RotatedEdgeTemplateModel>()).ToArray();
            if (models.Length == 0)
            {
                return new List<MatchCandidate>();
            }

            List<MatchCandidate> candidates = new List<MatchCandidate>(models.Length);
            if (ShouldUseParallelModelSearch(models.Length))
            {
                object sync = new object();
                Parallel.ForEach(models, searchModel =>
                {
                    MatchCandidate candidate = FindBestCandidate(gradients, searchModel, suppressedBounds, false);
                    if (candidate == null)
                    {
                        return;
                    }

                    lock (sync)
                    {
                        candidates.Add(candidate);
                    }
                });

                return candidates;
            }

            foreach (RotatedEdgeTemplateModel model in models)
            {
                MatchCandidate candidate = FindBestCandidate(gradients, model, suppressedBounds, true);
                if (candidate != null)
                {
                    candidates.Add(candidate);
                }
            }

            return candidates;
        }

        private MatchCandidate FindBestCandidateCoarseToFine(
            GradientImage gradients,
            TemplateModelCache modelCache,
            List<RectangleF> suppressedBounds,
            out List<MatchCandidate> candidateSeeds)
        {
            // Wide angle ranges are expensive because each angle scans all candidate centers.
            // Coarse-to-fine keeps accuracy by refining only around the best coarse angles.
            double coarseStep = Math.Max(property.FIND_ANGLE, property.COARSE_ANGLE_STEP);
            List<MatchCandidate> coarseCandidates = FindCandidatesFromModels(
                gradients,
                GetSearchModels(modelCache, coarseStep),
                suppressedBounds);

            MatchCandidate[] coarseBest = coarseCandidates
                .OrderByDescending(candidate => candidate.Score)
                .Take(Math.Max(1, property.COARSE_ANGLE_TOP_K))
                .ToArray();
            if (coarseBest.Length == 0)
            {
                candidateSeeds = null;
                return null;
            }

            Dictionary<string, RotatedEdgeTemplateModel> fineModels = new Dictionary<string, RotatedEdgeTemplateModel>(StringComparer.Ordinal);
            double fineRadius = coarseStep / 2D;
            foreach (MatchCandidate coarseCandidate in coarseBest)
            {
                foreach (double angle in CreateSearchAnglesAround(coarseCandidate.Angle, fineRadius, property.FIND_ANGLE))
                {
                    double normalizedAngle = Math.Round(angle, 6);
                    double normalizedScale = NormalizeScale(coarseCandidate.Scale);
                    string modelKey = CreateAngleScaleKey(normalizedAngle, normalizedScale);
                    if (!fineModels.ContainsKey(modelKey))
                    {
                        fineModels[modelKey] = modelCache.GetRotatedModel(normalizedAngle, normalizedScale);
                    }
                }
            }

            MatchCandidate fineBest = FindBestCandidateFromModels(
                gradients,
                fineModels.Values,
                suppressedBounds,
                out candidateSeeds);
            if (candidateSeeds == null || candidateSeeds.Count == 0)
            {
                int seedCapacity = GetCandidateSeedCapacity();
                candidateSeeds = seedCapacity > 0 ? new List<MatchCandidate>(seedCapacity) : null;
                foreach (MatchCandidate coarseCandidate in coarseBest)
                {
                    TrackCandidateSeed(candidateSeeds, coarseCandidate, seedCapacity);
                }
            }

            return fineBest ?? coarseBest[0];
        }

        private MatchCandidate FindBestCandidate(
            GradientImage gradients,
            RotatedEdgeTemplateModel model,
            List<RectangleF> suppressedBounds)
        {
            return FindBestCandidate(gradients, model, suppressedBounds, true);
        }

        private MatchCandidate FindBestCandidate(
            GradientImage gradients,
            RotatedEdgeTemplateModel model,
            List<RectangleF> suppressedBounds,
            bool allowPositionParallel)
        {
            List<MatchCandidate> candidateSeeds;
            return FindBestCandidate(gradients, model, suppressedBounds, allowPositionParallel, out candidateSeeds);
        }

        private MatchCandidate FindBestCandidate(
            GradientImage gradients,
            RotatedEdgeTemplateModel model,
            List<RectangleF> suppressedBounds,
            bool allowPositionParallel,
            out List<MatchCandidate> candidateSeeds)
        {
            int minCenterX = -model.MinOffsetX;
            int minCenterY = -model.MinOffsetY;
            int maxCenterX = gradients.Width - 1 - model.MaxOffsetX;
            int maxCenterY = gradients.Height - 1 - model.MaxOffsetY;

            if (maxCenterX < minCenterX || maxCenterY < minCenterY)
            {
                candidateSeeds = null;
                return null;
            }

            MatchCandidate best = null;
            int step = Math.Max(1, property.SEARCH_STEP);
            bool usePositionRefine = property.USE_POSITION_REFINE && step > 1;
            int seedCapacity = GetCandidateSeedCapacity();
            bool useSpatialSeeds = ShouldUseHybridSpatialSeeds();
            bool hasSuppressedBounds = HasSuppressedBounds(suppressedBounds);
            CandidateScoreContext scoreContext = CreateScoreContext(gradients, model);
            if (allowPositionParallel && ShouldUseParallelPositionSearch(minCenterX, minCenterY, maxCenterX, maxCenterY, step))
            {
                best = FindBestCandidateParallelPositions(
                    gradients,
                    model,
                    scoreContext,
                    suppressedBounds,
                    minCenterX,
                    minCenterY,
                    maxCenterX,
                    maxCenterY,
                    step,
                    seedCapacity,
                    useSpatialSeeds,
                    hasSuppressedBounds,
                    out candidateSeeds);
                if (usePositionRefine && best != null)
                {
                    best = RefineBestCandidate(
                        gradients,
                        model,
                        scoreContext,
                        suppressedBounds,
                        candidateSeeds,
                        minCenterX,
                        minCenterY,
                        maxCenterX,
                        maxCenterY,
                        step);
                }

                TrackCandidateSeed(candidateSeeds, best, seedCapacity);
                return best;
            }

            candidateSeeds = seedCapacity > 0
                ? new List<MatchCandidate>(seedCapacity)
                : null;
            SpatialCandidateSeedTracker spatialSeeds = useSpatialSeeds
                ? new SpatialCandidateSeedTracker(
                    minCenterX,
                    minCenterY,
                    maxCenterX,
                    maxCenterY,
                    HybridCandidateGridColumns,
                    HybridCandidateGridRows,
                    HybridCandidateGridTopK)
                : null;

            for (int y = minCenterY; y <= maxCenterY; y += step)
            {
                for (int x = minCenterX; x <= maxCenterX; x += step)
                {
                    RectangleF bounds = default;
                    bool boundsCreated = false;
                    if (hasSuppressedBounds)
                    {
                        bounds = CreateLocalBounds(x, y, model);
                        boundsCreated = true;
                        if (IsSuppressed(bounds, suppressedBounds))
                        {
                            continue;
                        }
                    }

                    double score = ScoreCandidate(scoreContext, x, y);
                    MatchCandidate candidate = null;
                    bool shouldTrackSeed = ShouldTrackCandidateSeed(candidateSeeds, score, seedCapacity);
                    bool shouldTrackSpatialSeed = spatialSeeds?.ShouldTrack(x, y, score) ?? false;
                    if (IsBetterScore(score, x, y, best))
                    {
                        candidate = CreateMatchCandidate(
                            model,
                            x,
                            y,
                            EnsureBounds(model, x, y, ref bounds, ref boundsCreated),
                            score);
                        best = candidate;
                    }

                    if (shouldTrackSeed)
                    {
                        candidate = candidate ?? CreateMatchCandidate(
                            model,
                            x,
                            y,
                            EnsureBounds(model, x, y, ref bounds, ref boundsCreated),
                            score);
                        TrackCandidateSeed(candidateSeeds, candidate, seedCapacity);
                    }

                    if (shouldTrackSpatialSeed)
                    {
                        candidate = candidate ?? CreateMatchCandidate(
                            model,
                            x,
                            y,
                            EnsureBounds(model, x, y, ref bounds, ref boundsCreated),
                            score);
                        spatialSeeds.Track(candidate);
                    }
                }
            }

            if (usePositionRefine && best != null)
            {
                best = RefineBestCandidate(
                    gradients,
                    model,
                    scoreContext,
                    suppressedBounds,
                    candidateSeeds,
                    minCenterX,
                    minCenterY,
                    maxCenterX,
                    maxCenterY,
                    step);
            }

            MergeCandidateSeeds(candidateSeeds, spatialSeeds?.GetCandidates(), seedCapacity);
            TrackCandidateSeed(candidateSeeds, best, seedCapacity);
            return best;
        }

        private MatchCandidate FindBestCandidateParallelPositions(
            GradientImage gradients,
            RotatedEdgeTemplateModel model,
            CandidateScoreContext scoreContext,
            List<RectangleF> suppressedBounds,
            int minCenterX,
            int minCenterY,
            int maxCenterX,
            int maxCenterY,
            int step,
            int seedCapacity,
            bool useSpatialSeeds,
            bool hasSuppressedBounds,
            out List<MatchCandidate> candidateSeeds)
        {
            int rowCount = ((maxCenterY - minCenterY) / step) + 1;
            object sync = new object();
            CandidateSearchState globalState = new CandidateSearchState(
                seedCapacity,
                useSpatialSeeds,
                minCenterX,
                minCenterY,
                maxCenterX,
                maxCenterY);

            Parallel.For(
                0,
                rowCount,
                () => new CandidateSearchState(
                    seedCapacity,
                    useSpatialSeeds,
                    minCenterX,
                    minCenterY,
                    maxCenterX,
                    maxCenterY),
                (rowIndex, loopState, localState) =>
                {
                    int y = minCenterY + (rowIndex * step);
                    for (int x = minCenterX; x <= maxCenterX; x += step)
                    {
                        if (hasSuppressedBounds
                            && IsSuppressed(CreateLocalBounds(x, y, model), suppressedBounds))
                        {
                            continue;
                        }

                        double score = ScoreCandidate(scoreContext, x, y);
                        localState.Consider(model, x, y, score);
                    }

                    return localState;
                },
                localState =>
                {
                    lock (sync)
                    {
                        globalState.Merge(localState);
                    }
                });

            candidateSeeds = globalState.CreateMergedCandidateSeeds();
            return globalState.Best;
        }

        private MatchCandidate RefineBestCandidate(
            GradientImage gradients,
            RotatedEdgeTemplateModel model,
            CandidateScoreContext scoreContext,
            List<RectangleF> suppressedBounds,
            List<MatchCandidate> coarseSeeds,
            int minCenterX,
            int minCenterY,
            int maxCenterX,
            int maxCenterY,
            int radius)
        {
            if (coarseSeeds == null || coarseSeeds.Count == 0)
            {
                return null;
            }

            MatchCandidate best = coarseSeeds.OrderByDescending(seed => seed.Score).First();
            bool hasSuppressedBounds = HasSuppressedBounds(suppressedBounds);
            foreach (MatchCandidate seed in coarseSeeds)
            {
                int coarseCenterX = (int)Math.Round(seed.Center.X);
                int coarseCenterY = (int)Math.Round(seed.Center.Y);
                int refineMinX = Math.Max(minCenterX, coarseCenterX - radius);
                int refineMaxX = Math.Min(maxCenterX, coarseCenterX + radius);
                int refineMinY = Math.Max(minCenterY, coarseCenterY - radius);
                int refineMaxY = Math.Min(maxCenterY, coarseCenterY + radius);

                for (int y = refineMinY; y <= refineMaxY; y++)
                {
                    for (int x = refineMinX; x <= refineMaxX; x++)
                    {
                        RectangleF bounds = default;
                        bool boundsCreated = false;
                        if (hasSuppressedBounds)
                        {
                            bounds = CreateLocalBounds(x, y, model);
                            boundsCreated = true;
                            if (IsSuppressed(bounds, suppressedBounds))
                            {
                                continue;
                            }
                        }

                        double score = ScoreCandidate(scoreContext, x, y);
                        if (score > best.Score)
                        {
                            best = CreateMatchCandidate(
                                model,
                                x,
                                y,
                                EnsureBounds(model, x, y, ref bounds, ref boundsCreated),
                                score);
                        }
                    }
                }
            }

            return best;
        }

        public MatchCandidate ApplySubpixelRefinement(
            GradientImage gradients,
            TemplateModelCache modelCache,
            MatchCandidate candidate)
        {
            if (property?.USE_SUBPIXEL_REFINE != true
                || candidate == null
                || gradients == null
                || modelCache == null)
            {
                return candidate;
            }

            RotatedEdgeTemplateModel model = modelCache.GetRotatedModel(candidate.Angle, candidate.Scale);
            int centerX = (int)Math.Round(candidate.Center.X);
            int centerY = (int)Math.Round(candidate.Center.Y);
            if (!CanScoreSubpixelNeighborhood(gradients, model, centerX, centerY))
            {
                return candidate;
            }

            CandidateScoreContext scoreContext = CreateScoreContext(gradients, model);
            double centerScore = ScoreCandidate(scoreContext, centerX, centerY);
            double leftScore = ScoreCandidate(scoreContext, centerX - 1, centerY);
            double rightScore = ScoreCandidate(scoreContext, centerX + 1, centerY);
            double topScore = ScoreCandidate(scoreContext, centerX, centerY - 1);
            double bottomScore = ScoreCandidate(scoreContext, centerX, centerY + 1);
            double offsetX = EstimateSubpixelOffset(leftScore, centerScore, rightScore);
            double offsetY = EstimateSubpixelOffset(topScore, centerScore, bottomScore);
            if (Math.Abs(offsetX) <= 0.000001D && Math.Abs(offsetY) <= 0.000001D)
            {
                return candidate;
            }

            return CreateSubpixelCandidate(candidate, model, centerX + offsetX, centerY + offsetY);
        }

        private static bool CanScoreSubpixelNeighborhood(
            GradientImage gradients,
            RotatedEdgeTemplateModel model,
            int centerX,
            int centerY)
        {
            return centerX - 1 + model.MinOffsetX >= 0
                && centerY - 1 + model.MinOffsetY >= 0
                && centerX + 1 + model.MaxOffsetX < gradients.Width
                && centerY + 1 + model.MaxOffsetY < gradients.Height;
        }

        private static double EstimateSubpixelOffset(double previousScore, double centerScore, double nextScore)
        {
            double denominator = previousScore - (2D * centerScore) + nextScore;
            if (Math.Abs(denominator) <= 0.0000001D)
            {
                return 0D;
            }

            double offset = 0.5D * (previousScore - nextScore) / denominator;
            return Clamp(offset, -0.5D, 0.5D);
        }

        private static MatchCandidate CreateSubpixelCandidate(
            MatchCandidate candidate,
            RotatedEdgeTemplateModel model,
            double refinedCenterX,
            double refinedCenterY)
        {
            double dx = refinedCenterX - candidate.Center.X;
            double dy = refinedCenterY - candidate.Center.Y;
            return new MatchCandidate
            {
                Center = new Point2d(refinedCenterX, refinedCenterY),
                TemplateCenter = new Point2d(
                    refinedCenterX + model.TemplateCenterOffsetX,
                    refinedCenterY + model.TemplateCenterOffsetY),
                Bounds = new RectangleF(
                    (float)(candidate.Bounds.X + dx),
                    (float)(candidate.Bounds.Y + dy),
                    candidate.Bounds.Width,
                    candidate.Bounds.Height),
                Score = candidate.Score,
                ImageVerifyScore = candidate.ImageVerifyScore,
                DescriptorVerifyScore = candidate.DescriptorVerifyScore,
                HybridScore = candidate.HybridScore,
                PolarityReversed = candidate.PolarityReversed,
                Angle = candidate.Angle,
                Scale = candidate.Scale,
                Width = candidate.Width,
                Height = candidate.Height
            };
        }

        private static bool HasSuppressedBounds(List<RectangleF> suppressedBounds)
        {
            return suppressedBounds != null && suppressedBounds.Count > 0;
        }

        private static RectangleF EnsureBounds(
            RotatedEdgeTemplateModel model,
            int centerX,
            int centerY,
            ref RectangleF bounds,
            ref bool boundsCreated)
        {
            if (!boundsCreated)
            {
                bounds = CreateLocalBounds(centerX, centerY, model);
                boundsCreated = true;
            }

            return bounds;
        }

        public static bool ShouldTrackCandidateSeed(List<MatchCandidate> seeds, double score, int capacity)
        {
            return seeds != null
                && capacity > 0
                && (seeds.Count < capacity
                || score > seeds[seeds.Count - 1].Score);
        }

        private static bool ShouldUseParallelModelSearch(int modelCount)
        {
            return Environment.ProcessorCount > 1 && modelCount >= ParallelModelThreshold;
        }

        private static bool ShouldUseParallelPositionSearch(
            int minCenterX,
            int minCenterY,
            int maxCenterX,
            int maxCenterY,
            int step)
        {
            if (Environment.ProcessorCount <= 1)
            {
                return false;
            }

            int columnCount = ((maxCenterX - minCenterX) / step) + 1;
            int rowCount = ((maxCenterY - minCenterY) / step) + 1;
            return columnCount * rowCount >= ParallelPositionThreshold;
        }

        public static bool IsBetterCandidate(MatchCandidate candidate, MatchCandidate currentBest)
        {
            if (candidate == null)
            {
                return false;
            }

            return IsBetterScore(candidate.Score, candidate.Center.X, candidate.Center.Y, currentBest);
        }

        public static bool IsBetterScore(double score, double centerX, double centerY, MatchCandidate currentBest)
        {
            if (currentBest == null)
            {
                return true;
            }

            const double epsilon = 0.0000001D;
            if (score > currentBest.Score + epsilon)
            {
                return true;
            }

            if (Math.Abs(score - currentBest.Score) > epsilon)
            {
                return false;
            }

            if (centerY < currentBest.Center.Y)
            {
                return true;
            }

            return Math.Abs(centerY - currentBest.Center.Y) <= epsilon
                && centerX < currentBest.Center.X;
        }

        public static void TrackCandidateSeed(List<MatchCandidate> seeds, MatchCandidate candidate, int capacity)
        {
            if (seeds == null || candidate == null || capacity <= 0)
            {
                return;
            }

            for (int i = 0; i < seeds.Count; i++)
            {
                if (IsSameCandidate(seeds[i], candidate))
                {
                    if (candidate.Score > seeds[i].Score)
                    {
                        seeds[i] = candidate;
                    }

                    seeds.Sort((left, right) => right.Score.CompareTo(left.Score));
                    return;
                }
            }

            seeds.Add(candidate);
            seeds.Sort((left, right) => right.Score.CompareTo(left.Score));
            if (seeds.Count > capacity)
            {
                seeds.RemoveAt(seeds.Count - 1);
            }
        }

        public static void MergeCandidateSeeds(List<MatchCandidate> target, IEnumerable<MatchCandidate> source, int capacity)
        {
            if (target == null || source == null || capacity <= 0)
            {
                return;
            }

            foreach (MatchCandidate candidate in source)
            {
                TrackCandidateSeed(target, candidate, capacity);
            }
        }

        public static bool IsSameCandidate(MatchCandidate left, MatchCandidate right)
        {
            return left != null
                && right != null
                && Math.Abs(left.TemplateCenter.X - right.TemplateCenter.X) < 0.5D
                && Math.Abs(left.TemplateCenter.Y - right.TemplateCenter.Y) < 0.5D
                && Math.Abs(left.Angle - right.Angle) < 0.0001D
                && Math.Abs(left.Scale - right.Scale) < 0.0001D;
        }

        public static MatchCandidate CreateMatchCandidate(
            RotatedEdgeTemplateModel model,
            int centerX,
            int centerY,
            RectangleF bounds,
            double score)
        {
            return new MatchCandidate
            {
                Center = new Point2d(centerX, centerY),
                TemplateCenter = new Point2d(centerX + model.TemplateCenterOffsetX, centerY + model.TemplateCenterOffsetY),
                Bounds = bounds,
                Score = score,
                Angle = model.Angle,
                Scale = model.Scale,
                Width = model.TemplateWidth,
                Height = model.TemplateHeight
            };
        }

        public double ScoreCandidate(GradientImage gradients, RotatedEdgeTemplateModel model, int centerX, int centerY)
        {
            return ScoreCandidate(CreateScoreContext(gradients, model), centerX, centerY);
        }

        private CandidateScoreContext CreateScoreContext(GradientImage gradients, RotatedEdgeTemplateModel model)
        {
            return new CandidateScoreContext(
                gradients.Width,
                model.PointCount,
                model.GetIndexOffsets(gradients.Width),
                model.UnitDxValues,
                model.UnitDyValues,
                gradients.UnitDxValues,
                gradients.UnitDyValues,
                CreateBreakSumThresholds(model.PointCount),
                property.ALLOW_GLOBAL_POLARITY_REVERSAL);
        }

        private double[] CreateBreakSumThresholds(int pointCount)
        {
            double[] thresholds = new double[Math.Max(0, pointCount)];
            if (pointCount <= 0)
            {
                return thresholds;
            }

            double minScore = property.SCORE_MIN;
            double greediness = Clamp(property.GREEDINESS, 0d, 0.999d);
            double normMinScore = minScore / pointCount;
            double normGreediness = (1d - greediness * minScore) / (1d - greediness) / pointCount;
            double minScoreMinusOne = minScore - 1d;
            for (int i = 0; i < pointCount; i++)
            {
                int processed = i + 1;
                double breakScore = Math.Min(
                    minScoreMinusOne + normGreediness * processed,
                    normMinScore * processed);
                thresholds[i] = breakScore * processed;
            }

            return thresholds;
        }

        private static double ScoreCandidate(CandidateScoreContext context, int centerX, int centerY)
        {
            double partialSum = 0d;
            int pointCount = context.PointCount;
            if (pointCount <= 0)
            {
                return 0D;
            }

            int[] indexOffsets = context.IndexOffsets;
            double[] templateUnitDxValues = context.TemplateUnitDxValues;
            double[] templateUnitDyValues = context.TemplateUnitDyValues;
            double[] sourceUnitDxValues = context.SourceUnitDxValues;
            double[] sourceUnitDyValues = context.SourceUnitDyValues;
            double[] breakSumThresholds = context.BreakSumThresholds;
            int centerIndex = (centerY * context.ImageWidth) + centerX;

            for (int i = 0; i < pointCount; i++)
            {
                int index = centerIndex + indexOffsets[i];
                partialSum += (sourceUnitDxValues[index] * templateUnitDxValues[i])
                    + (sourceUnitDyValues[index] * templateUnitDyValues[i]);

                if (!context.AllowGlobalPolarityReversal
                    && partialSum < breakSumThresholds[i])
                {
                    return partialSum / (i + 1);
                }
            }

            return context.AllowGlobalPolarityReversal
                ? Math.Abs(partialSum) / pointCount
                : partialSum / pointCount;
        }

        public void ApplyGlobalPolarityState(
            GradientImage gradients,
            TemplateModelCache modelCache,
            MatchCandidate candidate)
        {
            if (candidate == null)
            {
                return;
            }

            candidate.PolarityReversed = false;
            if (!property.ALLOW_GLOBAL_POLARITY_REVERSAL)
            {
                return;
            }

            RotatedEdgeTemplateModel model = modelCache.GetRotatedModel(candidate.Angle, candidate.Scale);
            CandidateScoreContext context = CreateScoreContext(gradients, model);
            candidate.PolarityReversed = ScoreCandidateSigned(
                context,
                (int)Math.Round(candidate.Center.X),
                (int)Math.Round(candidate.Center.Y)) < 0D;
        }

        private static double ScoreCandidateSigned(
            CandidateScoreContext context,
            int centerX,
            int centerY)
        {
            if (context.PointCount <= 0)
            {
                return 0D;
            }

            double sum = 0D;
            int centerIndex = (centerY * context.ImageWidth) + centerX;
            for (int i = 0; i < context.PointCount; i++)
            {
                int index = centerIndex + context.IndexOffsets[i];
                sum += (context.SourceUnitDxValues[index] * context.TemplateUnitDxValues[i])
                    + (context.SourceUnitDyValues[index] * context.TemplateUnitDyValues[i]);
            }

            return sum / context.PointCount;
        }

        public List<RotatedEdgeTemplateModel> GetSearchModels(TemplateModelCache modelCache, double angleStep)
        {
            return modelCache.GetSearchModels(
                CreateSearchModelCacheKey(angleStep),
                CreateSearchAngles(angleStep),
                CreateSearchScales());
        }

        private string CreateSearchModelCacheKey(double angleStep)
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "Angle:{0}:{1}:{2}:{3}|Scale:{4}:{5}:{6}:{7}",
                property.USE_FIND_ANGLE,
                property.FIND_ANGLE_MIN,
                property.FIND_ANGLE_MAX,
                Math.Round(angleStep, 6),
                property.USE_FIND_SCALE,
                Math.Round(property.FIND_SCALE_MIN, 6),
                Math.Round(property.FIND_SCALE_MAX, 6),
                Math.Round(property.FIND_SCALE_STEP, 6));
        }

        public IEnumerable<double> CreateSearchAngles()
        {
            return CreateSearchAngles(property.FIND_ANGLE);
        }

        public IEnumerable<double> CreateSearchAngles(double angleStep)
        {
            if (!property.USE_FIND_ANGLE)
            {
                yield return 0D;
                yield break;
            }

            foreach (double angle in CreateSearchAnglesInRange(property.FIND_ANGLE_MIN, property.FIND_ANGLE_MAX, angleStep))
            {
                yield return angle;
            }
        }

        public IEnumerable<double> CreateSearchAnglesAround(double centerAngle, double radius, double angleStep)
        {
            double minAngle = Math.Max(Math.Min(property.FIND_ANGLE_MIN, property.FIND_ANGLE_MAX), centerAngle - radius);
            double maxAngle = Math.Min(Math.Max(property.FIND_ANGLE_MIN, property.FIND_ANGLE_MAX), centerAngle + radius);
            foreach (double angle in CreateSearchAnglesInRange(minAngle, maxAngle, angleStep))
            {
                yield return angle;
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
            int start = (int)Math.Ceiling(minScale / scaleStep);
            int end = (int)Math.Floor(maxScale / scaleStep);
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
        }

        public bool ShouldUseCoarseToFineAngleSearch()
        {
            return property.USE_FIND_ANGLE
                && property.USE_COARSE_TO_FINE_ANGLE_SEARCH
                && property.COARSE_ANGLE_STEP > property.FIND_ANGLE;
        }

        private static IEnumerable<double> CreateSearchAnglesInRange(double minAngle, double maxAngle, double angleStep)
        {
            if (angleStep <= 0D)
            {
                yield break;
            }

            if (minAngle > maxAngle)
            {
                double temp = minAngle;
                minAngle = maxAngle;
                maxAngle = temp;
            }

            HashSet<double> emitted = new HashSet<double>();
            int start = (int)Math.Ceiling(minAngle / angleStep);
            int end = (int)Math.Floor(maxAngle / angleStep);
            for (int i = start; i <= end; i++)
            {
                double angle = Math.Round(i * angleStep, 6);
                if (emitted.Add(angle))
                {
                    yield return angle;
                }
            }

            if (minAngle <= 0D && maxAngle >= 0D && emitted.Add(0D))
            {
                yield return 0D;
            }
        }

        public static RotatedEdgeTemplateModel CreateRotatedModel(EdgeTemplateModel model, double angle)
        {
            double radians = angle * Math.PI / 180D;
            double cos = Math.Cos(radians);
            double sin = Math.Sin(radians);
            List<RotatedTemplateEdgePoint> points = new List<RotatedTemplateEdgePoint>(model.Points.Count);

            foreach (TemplateEdgePoint point in model.Points)
            {
                // OpenCV image coordinates use a downward Y axis. Match GetRotationMatrix2D
                // angle semantics so EdgeBasedMatching reports the same sign as image matching.
                int offsetX = (int)Math.Round(point.OffsetX * cos + point.OffsetY * sin);
                int offsetY = (int)Math.Round(-point.OffsetX * sin + point.OffsetY * cos);
                points.Add(new RotatedTemplateEdgePoint
                {
                    OffsetX = offsetX,
                    OffsetY = offsetY,
                    UnitDx = point.UnitDx * cos + point.UnitDy * sin,
                    UnitDy = -point.UnitDx * sin + point.UnitDy * cos
                });
            }

            int minOffsetX = points.Count == 0 ? 0 : points.Min(point => point.OffsetX);
            int maxOffsetX = points.Count == 0 ? 0 : points.Max(point => point.OffsetX);
            int minOffsetY = points.Count == 0 ? 0 : points.Min(point => point.OffsetY);
            int maxOffsetY = points.Count == 0 ? 0 : points.Max(point => point.OffsetY);
            RectangleF localBounds = CreateRotatedTemplateBounds(model, cos, sin);

            double templateCenterOffsetX = ((model.Width / 2D) - model.Center.X) * cos + ((model.Height / 2D) - model.Center.Y) * sin;
            double templateCenterOffsetY = -((model.Width / 2D) - model.Center.X) * sin + ((model.Height / 2D) - model.Center.Y) * cos;

            return new RotatedEdgeTemplateModel(
                model.Width,
                model.Height,
                Math.Round(angle, 6),
                points,
                minOffsetX,
                maxOffsetX,
                minOffsetY,
                maxOffsetY,
                localBounds,
                templateCenterOffsetX,
                templateCenterOffsetY,
                model.Scale);
        }

        private static RectangleF CreateRotatedTemplateBounds(EdgeTemplateModel model, double cos, double sin)
        {
            Point2d[] corners =
            {
            new Point2d(-model.Center.X, -model.Center.Y),
            new Point2d(model.Width - model.Center.X, -model.Center.Y),
            new Point2d(model.Width - model.Center.X, model.Height - model.Center.Y),
            new Point2d(-model.Center.X, model.Height - model.Center.Y)
        };

            double minX = double.MaxValue;
            double minY = double.MaxValue;
            double maxX = double.MinValue;
            double maxY = double.MinValue;
            foreach (Point2d corner in corners)
            {
                double x = corner.X * cos + corner.Y * sin;
                double y = -corner.X * sin + corner.Y * cos;
                minX = Math.Min(minX, x);
                minY = Math.Min(minY, y);
                maxX = Math.Max(maxX, x);
                maxY = Math.Max(maxY, y);
            }

            return new RectangleF(
                (float)minX,
                (float)minY,
                (float)(maxX - minX),
                (float)(maxY - minY));
        }

    }

        private TemplateModelCache GetTemplateModelCache()
        {
            return modelStore.GetTemplateModelCache();
        }

        private void ClearTemplateModelCache()
        {
            modelStore.Clear();
        }

        private Mat GetTemplateForRun()
        {
            return modelStore.GetTemplateForRun();
        }

        private Mat CreatePreparedGrayImage(Mat source)
        {
            Mat image = source.Clone();
            ApplyCommonPreprocessing(image, property);
            return image;
        }

        private sealed class EdgeTemplateModelStore : IDisposable
    {
        private readonly EdgeBasedTemplateMatchingTool owner;
        private Mat originalTemplate = new Mat();
        private TemplateModelCache templateModelCache;
        private long templateRevision;

        public EdgeTemplateModelStore(EdgeBasedTemplateMatchingTool owner)
        {
            this.owner = owner;
        }

        private IOpenCVPropertyEdgeBasedTemplateMatching property => owner.property;

        public TemplateModelCache Current => templateModelCache;

        public void SetTemplate(Mat image)
        {
            Clear();
            originalTemplate?.Dispose();
            originalTemplate = OpenCvHelper.IsImageEmpty(image) ? new Mat() : image.Clone();
            templateRevision++;
        }

        public Mat GetTemplateForRun()
        {
            return OpenCvHelper.IsImageEmpty(originalTemplate) ? owner.imageTemplate : originalTemplate;
        }

        public TemplateModelCache GetTemplateModelCache()
        {
            Mat template = GetTemplateForRun();
            string key = CreateTemplateModelCacheKey(template);
            if (templateModelCache != null && string.Equals(templateModelCache.Key, key, StringComparison.Ordinal))
            {
                return templateModelCache;
            }

            Clear();
            Mat preparedTemplate = owner.CreatePreparedGrayImage(template);
            EdgeTemplateModel model = CreateModel(preparedTemplate);
            templateModelCache = new TemplateModelCache(key, preparedTemplate, model, CreateTemplateModel);
            return templateModelCache;
        }

        public void Clear()
        {
            templateModelCache?.Dispose();
            templateModelCache = null;
        }

        private string CreateTemplateModelCacheKey(Mat template)
        {
            if (OpenCvHelper.IsImageEmpty(template))
            {
                return "empty";
            }

            string path = property?.PATTERN_PATH ?? string.Empty;
            long fileTicks = 0L;
            if (!string.IsNullOrWhiteSpace(path) && System.IO.File.Exists(path))
            {
                fileTicks = System.IO.File.GetLastWriteTimeUtc(path).Ticks;
            }

            long dataPointer = template.Data.ToInt64();
            return string.Format(
                CultureInfo.InvariantCulture,
                "Tpl:{0}:{1}:{2}:{3}:{4}|Path:{5}:{6}|Prep:{7}:{8}:{9:0.###}:{10}:{11}:{12:0.###}:{13}:{14}:{15}:{16}|Edge:{17}:{18}:{19}:{20}:{21}:{22}:{23}:{24:0.###}",
                template.Width,
                template.Height,
                template.Type(),
                dataPointer,
                templateRevision,
                path,
                fileTicks,
                property.USE_THRESHOLD,
                property.THRESHOLD_TYPES,
                property.THRESHOLD,
                property.USE_ADAPTIVE_THRESHOLD,
                property.ADAPTIVE_THRESHOLD_TYPES,
                property.ADAPTIVE_THRESHOLD,
                property.ADAPTIVE_THRESHOLD_ALGORITHM,
                property.BlockSize,
                property.Weight,
                property.USE_BITWISENOT,
                property.CANNY_LOW,
                property.CANNY_HIGH,
                property.CANNY_APERTURE_SIZE,
                property.USE_L2_GRADIENT,
                property.CONTOUR_RETRIEVAL_MODE,
                property.CONTOUR_APPROXIMATION_MODE,
                property.MAX_TEMPLATE_POINTS,
                property.MIN_GRADIENT_MAGNITUDE);
        }

        public EdgeTemplateModel CreateModel(Mat template)
        {
            return CreateTemplateModel(template, 1D);
        }

        private EdgeTemplateModel CreateTemplateModel(Mat template, double scale)
        {
            using (Mat edges = new Mat())
            using (GradientImage gradients = GradientImage.Create(template, property.MIN_GRADIENT_MAGNITUDE))
            {
                Cv2.Canny(
                    template,
                    edges,
                    property.CANNY_LOW,
                    property.CANNY_HIGH,
                    NormalizeCannyAperture(property.CANNY_APERTURE_SIZE),
                    property.USE_L2_GRADIENT);

                Cv2.FindContours(
                    edges,
                    out OpenCvSharp.Point[][] contours,
                    out _,
                    property.CONTOUR_RETRIEVAL_MODE,
                    property.CONTOUR_APPROXIMATION_MODE);

                List<TemplateEdgePoint> points = new List<TemplateEdgePoint>();
                HashSet<long> seen = new HashSet<long>();
                foreach (OpenCvSharp.Point[] contour in contours)
                {
                    foreach (OpenCvSharp.Point point in contour)
                    {
                        long key = ((long)point.Y << 32) | (uint)point.X;
                        if (!seen.Add(key))
                        {
                            continue;
                        }

                        double magnitude = gradients.GetMagnitude(point.X, point.Y);
                        if (magnitude <= property.MIN_GRADIENT_MAGNITUDE)
                        {
                            continue;
                        }

                        points.Add(new TemplateEdgePoint
                        {
                            X = point.X,
                            Y = point.Y,
                            UnitDx = gradients.GetUnitDx(point.X, point.Y),
                            UnitDy = gradients.GetUnitDy(point.X, point.Y),
                            InvMagnitude = 1d / magnitude
                        });
                    }
                }

                int rawPointCount = points.Count;
                points = Downsample(points, property.MAX_TEMPLATE_POINTS);
                if (points.Count == 0)
                {
                    return new EdgeTemplateModel(template.Width, template.Height, new Point2d(), points, rawPointCount, scale);
                }

                Point2d center = new Point2d(points.Average(point => point.X), points.Average(point => point.Y));
                foreach (TemplateEdgePoint point in points)
                {
                    point.OffsetX = (int)Math.Round(point.X - center.X);
                    point.OffsetY = (int)Math.Round(point.Y - center.Y);
                }

                return new EdgeTemplateModel(template.Width, template.Height, center, points, rawPointCount, scale);
            }
        }

        public void Dispose()
        {
            Clear();
            originalTemplate?.Dispose();
            originalTemplate = null;
        }
    }

        private Rect NormalizeRoi(Rect roi)
        {
            return roi.Width == 0 || roi.Height == 0
                ? new Rect(0, 0, imageSource.Width, imageSource.Height)
                : roi;
        }

        private MatchingResult CreateResult(MatchCandidate candidate, EdgeTemplateModel model, Rect roi, bool applyRoiOffset)
        {
            double templateCenterX = candidate.TemplateCenter.X;
            double templateCenterY = candidate.TemplateCenter.Y;
            if (applyRoiOffset)
            {
                templateCenterX += roi.X;
                templateCenterY += roi.Y;
            }

            Rect2f bounding = new Rect2f(
                (float)(templateCenterX - (candidate.Width / 2D)),
                (float)(templateCenterY - (candidate.Height / 2D)),
                candidate.Width,
                candidate.Height);
            Point2f center = new Point2f((float)templateCenterX, (float)templateCenterY);
            double edgeScore = Math.Round(candidate.Score * 100D, 3);
            MatchingResult result = new MatchingResult(
                0,
                edgeScore,
                center,
                bounding,
                candidate.Angle,
                candidate.Scale)
            {
                EdgeScore = edgeScore,
                ImageScore = double.IsNaN(candidate.ImageVerifyScore)
                    ? double.NaN
                    : Math.Round(candidate.ImageVerifyScore * 100D, 3),
                FinalScore = Math.Round(GetUniqueMatchSelectionScore(candidate) * 100D, 3),
                ScoreMargin = property.USE_UNIQUE_MATCH_VALIDATION
                    ? Math.Round(candidateDiagnostics.UniqueMatchScoreMargin * 100D, 3)
                    : double.NaN,
                PolarityReversed = candidate.PolarityReversed
            };
            return result;
        }

        private static RectangleF CreateLocalBounds(int centerX, int centerY, RotatedEdgeTemplateModel model)
        {
            return new RectangleF(
                centerX + model.LocalBounds.X,
                centerY + model.LocalBounds.Y,
                model.LocalBounds.Width,
                model.LocalBounds.Height);
        }

        private bool IsDuplicate(MatchingResult result)
        {
            double threshold = Math.Max(4d, Math.Min(result.Bounding.Width, result.Bounding.Height) * 0.35d);
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

        private static bool IsSuppressed(RectangleF bounds, List<RectangleF> suppressedBounds)
        {
            if (suppressedBounds == null || suppressedBounds.Count == 0)
            {
                return false;
            }

            PointF center = new PointF(bounds.X + bounds.Width / 2f, bounds.Y + bounds.Height / 2f);
            return suppressedBounds.Any(rect => rect.Contains(center));
        }

        private static RectangleF CreateExpandedBounds(RectangleF bounds, float expansionRatio)
        {
            float expandX = bounds.Width * expansionRatio;
            float expandY = bounds.Height * expansionRatio;
            return new RectangleF(
                bounds.X - expandX,
                bounds.Y - expandY,
                bounds.Width + expandX * 2,
                bounds.Height + expandY * 2);
        }

        private bool TryUseHybridProposalFastPath(MatchCandidate imageProposal, int localMatchCount)
        {
            return hybridSearch.TryUseHybridProposalFastPath(imageProposal, localMatchCount);
        }

        private MatchCandidate CreateHybridImageProposalCandidate(
            GradientImage gradients,
            Mat source,
            Mat template,
            TemplateModelCache modelCache,
            List<RectangleF> suppressedBounds)
        {
            return hybridSearch.CreateHybridImageProposalCandidate(
                gradients,
                source,
                template,
                modelCache,
                suppressedBounds);
        }

        private MatchCandidate ApplyHybridVerification(
            Mat source,
            Mat template,
            MatchCandidate fallback,
            IEnumerable<MatchCandidate> candidateSeeds,
            MatchCandidate imageProposal)
        {
            return hybridSearch.ApplyHybridVerification(source, template, fallback, candidateSeeds, imageProposal);
        }

        private bool ShouldUseHybridVerify()
        {
            return hybridSearch.ShouldUseHybridVerify();
        }

        private int GetCandidateSeedCapacity()
        {
            return hybridSearch.GetCandidateSeedCapacity();
        }

        private bool ShouldUseHybridSpatialSeeds()
        {
            return hybridSearch.ShouldUseHybridSpatialSeeds();
        }

        private sealed class EdgeHybridCandidateSearch
    {
        private readonly EdgeBasedTemplateMatchingTool owner;

        public EdgeHybridCandidateSearch(EdgeBasedTemplateMatchingTool owner)
        {
            this.owner = owner;
        }

        private IOpenCVPropertyEdgeBasedTemplateMatching property => owner.property;

        private CandidateDiagnostics candidateDiagnostics => owner.candidateDiagnostics;

        private double ScoreCandidate(GradientImage gradients, RotatedEdgeTemplateModel model, int centerX, int centerY)
            => owner.ScoreCandidate(gradients, model, centerX, centerY);

        private bool ShouldUseCoarseToFineAngleSearch() => owner.ShouldUseCoarseToFineAngleSearch();

        private IEnumerable<double> CreateSearchAngles(double angleStep) => owner.CreateSearchAngles(angleStep);

        private IEnumerable<double> CreateSearchAnglesAround(double centerAngle, double radius, double angleStep)
            => owner.CreateSearchAnglesAround(centerAngle, radius, angleStep);

        private long StartPhaseTiming() => owner.StartPhaseTiming();

        private void StopPhaseTiming(string phaseName, long startTimestamp) => owner.StopPhaseTiming(phaseName, startTimestamp);

        private void RecordCandidateAmbiguityDiagnostics(
            IEnumerable<MatchCandidate> candidates,
            MatchCandidate selected,
            Mat template)
            => owner.RecordCandidateAmbiguityDiagnostics(candidates, selected, template);

        public bool TryUseHybridProposalFastPath(MatchCandidate imageProposal, int localMatchCount)
        {
            if (property.USE_UNIQUE_MATCH_VALIDATION
                || !ShouldUseHybridVerify()
                || localMatchCount != 1
                || imageProposal == null
                || double.IsNaN(imageProposal.ImageVerifyScore))
            {
                return false;
            }

            double requiredEdgeScore = Math.Max(property.SCORE_MIN, HybridProposalFastPathMinEdgeScore);
            return imageProposal.ImageVerifyScore >= HybridProposalFastPathMinImageScore
                && imageProposal.Score >= requiredEdgeScore;
        }

        public MatchCandidate CreateHybridImageProposalCandidate(
            GradientImage gradients,
            Mat source,
            Mat template,
            TemplateModelCache modelCache,
            List<RectangleF> suppressedBounds)
        {
            if (!ShouldUseHybridVerify()
                || property.USE_FIND_SCALE
                || OpenCvHelper.IsImageEmpty(source)
                || OpenCvHelper.IsImageEmpty(template)
                || template.Width > source.Width
                || template.Height > source.Height)
            {
                return null;
            }

            if (property.USE_FIND_ANGLE)
            {
                return CreateHybridAngleImageProposalCandidate(
                    gradients,
                    source,
                    template,
                    modelCache,
                    suppressedBounds);
            }

            EdgeTemplateModel baseModel = modelCache.Model;
            using (Mat result = new Mat())
            {
                Cv2.MatchTemplate(source, template, result, TemplateMatchModes.CCoeffNormed);
                Cv2.MinMaxLoc(result, out _, out double maxValue, out _, out OpenCvSharp.Point maxLocation);

                RotatedEdgeTemplateModel model = CreateRotatedModel(baseModel, 0D);
                int centerX = (int)Math.Round(maxLocation.X + baseModel.Center.X);
                int centerY = (int)Math.Round(maxLocation.Y + baseModel.Center.Y);
                RectangleF bounds = CreateLocalBounds(centerX, centerY, model);
                if (IsSuppressed(bounds, suppressedBounds))
                {
                    return null;
                }

                double score = ScoreCandidate(gradients, model, centerX, centerY);
                MatchCandidate candidate = CreateMatchCandidate(model, centerX, centerY, bounds, score);
                candidate.ImageVerifyScore = NormalizeTemplateMatchScore(maxValue);
                return candidate;
            }
        }

        private MatchCandidate CreateHybridAngleImageProposalCandidate(
            GradientImage gradients,
            Mat source,
            Mat template,
            TemplateModelCache modelCache,
            List<RectangleF> suppressedBounds)
        {
            MatchCandidate coarseBest = CreateBestHybridImageProposalFromAngles(
                gradients,
                source,
                template,
                modelCache,
                ShouldUseCoarseToFineAngleSearch() ? property.COARSE_ANGLE_STEP : property.FIND_ANGLE,
                suppressedBounds);
            if (coarseBest == null || !ShouldUseCoarseToFineAngleSearch())
            {
                return coarseBest;
            }

            double refineRadius = Math.Max(property.FIND_ANGLE, property.COARSE_ANGLE_STEP / 2D);
            return CreateBestHybridImageProposalFromAngles(
                gradients,
                source,
                template,
                modelCache,
                CreateSearchAnglesAround(coarseBest.Angle, refineRadius, property.FIND_ANGLE),
                suppressedBounds) ?? coarseBest;
        }

        private MatchCandidate CreateBestHybridImageProposalFromAngles(
            GradientImage gradients,
            Mat source,
            Mat template,
            TemplateModelCache modelCache,
            double angleStep,
            List<RectangleF> suppressedBounds)
        {
            return CreateBestHybridImageProposalFromAngles(
                gradients,
                source,
                template,
                modelCache,
                CreateSearchAngles(angleStep),
                suppressedBounds);
        }

        private MatchCandidate CreateBestHybridImageProposalFromAngles(
            GradientImage gradients,
            Mat source,
            Mat template,
            TemplateModelCache modelCache,
            IEnumerable<double> angles,
            List<RectangleF> suppressedBounds)
        {
            MatchCandidate best = null;
            double bestImageScore = double.MinValue;
            bool useScaledProposal = TryGetHybridProposalScale(source, template, out double proposalScale);
            double[] angleArray = (angles ?? Enumerable.Empty<double>()).ToArray();
            using (Mat scaledSource = useScaledProposal ? new Mat() : null)
            {
                // The source image is the same for every angle in this proposal batch.
                // Keep this resize outside the angle loop; only rotated templates change per angle.
                if (useScaledProposal)
                {
                    long scaleSourceStart = StartPhaseTiming();
                    Cv2.Resize(source, scaledSource, new OpenCvSharp.Size(), proposalScale, proposalScale, InterpolationFlags.Area);
                    StopPhaseTiming("HybridProposal.ScaleSource", scaleSourceStart);
                }

                if (ShouldUseParallelHybridProposal(angleArray.Length))
                {
                    object sync = new object();
                    Parallel.ForEach(angleArray, angle =>
                    {
                        MatchCandidate candidate = CreateHybridImageProposalCandidateForAngle(
                            gradients,
                            source,
                            template,
                            modelCache,
                            angle,
                            useScaledProposal ? scaledSource : null,
                            proposalScale,
                            suppressedBounds,
                            out double imageScore);
                        if (candidate == null)
                        {
                            return;
                        }

                        lock (sync)
                        {
                            if (best == null
                                || imageScore > bestImageScore
                                || (Math.Abs(imageScore - bestImageScore) <= 0.0000001D && IsBetterCandidate(candidate, best)))
                            {
                                best = candidate;
                                bestImageScore = imageScore;
                            }
                        }
                    });
                }
                else
                {
                    foreach (double angle in angleArray)
                    {
                        MatchCandidate candidate = CreateHybridImageProposalCandidateForAngle(
                            gradients,
                            source,
                            template,
                            modelCache,
                            angle,
                            useScaledProposal ? scaledSource : null,
                            proposalScale,
                            suppressedBounds,
                            out double imageScore);
                        if (candidate == null)
                        {
                            continue;
                        }

                        if (best == null
                            || imageScore > bestImageScore
                            || (Math.Abs(imageScore - bestImageScore) <= 0.0000001D && IsBetterCandidate(candidate, best)))
                        {
                            best = candidate;
                            bestImageScore = imageScore;
                        }
                    }
                }
            }

            return best;
        }

        private static bool ShouldUseParallelHybridProposal(int angleCount)
        {
            return Environment.ProcessorCount > 1 && angleCount >= 4;
        }

        private MatchCandidate CreateHybridImageProposalCandidateForAngle(
            GradientImage gradients,
            Mat source,
            Mat template,
            TemplateModelCache modelCache,
            double angle,
            Mat scaledSource,
            double proposalScale,
            List<RectangleF> suppressedBounds,
            out double imageScore)
        {
            imageScore = double.NaN;
            long rotateStart = StartPhaseTiming();
            using (Mat verifyTemplate = CreateHybridVerifyTemplate(template, angle))
            {
                StopPhaseTiming("HybridProposal.RotateTemplate", rotateStart);
                if (verifyTemplate.Width > source.Width || verifyTemplate.Height > source.Height)
                {
                    return null;
                }

                if (TryCreateScaledHybridImageProposalCandidateForAngle(
                    gradients,
                    source,
                    verifyTemplate,
                    modelCache,
                    angle,
                    scaledSource,
                    proposalScale,
                    suppressedBounds,
                    out MatchCandidate scaledCandidate,
                    out double scaledImageScore))
                {
                    imageScore = scaledImageScore;
                    return scaledCandidate;
                }

                return CreateFullResolutionHybridImageProposalCandidateForAngle(
                    gradients,
                    source,
                    verifyTemplate,
                    modelCache,
                    angle,
                    suppressedBounds,
                    out imageScore);
            }
        }

        private MatchCandidate CreateFullResolutionHybridImageProposalCandidateForAngle(
            GradientImage gradients,
            Mat source,
            Mat verifyTemplate,
            TemplateModelCache modelCache,
            double angle,
            List<RectangleF> suppressedBounds,
            out double imageScore)
        {
            imageScore = double.NaN;
            using (Mat result = new Mat())
            {
                long fullMatchStart = StartPhaseTiming();
                Cv2.MatchTemplate(source, verifyTemplate, result, TemplateMatchModes.CCoeffNormed);
                Cv2.MinMaxLoc(result, out _, out double maxValue, out _, out OpenCvSharp.Point maxLocation);
                StopPhaseTiming("HybridProposal.FullMatch", fullMatchStart);
                imageScore = NormalizeTemplateMatchScore(maxValue);
                return CreateHybridImageProposalCandidateFromLocation(
                    gradients,
                    modelCache,
                    angle,
                    verifyTemplate.Width,
                    verifyTemplate.Height,
                    maxLocation,
                    imageScore,
                    suppressedBounds);
            }
        }

        private bool TryCreateScaledHybridImageProposalCandidateForAngle(
            GradientImage gradients,
            Mat source,
            Mat verifyTemplate,
            TemplateModelCache modelCache,
            double angle,
            Mat scaledSource,
            double scale,
            List<RectangleF> suppressedBounds,
            out MatchCandidate candidate,
            out double imageScore)
        {
            candidate = null;
            imageScore = double.NaN;
            if (OpenCvHelper.IsImageEmpty(scaledSource) || scale <= 0D)
            {
                return false;
            }

            using (Mat scaledTemplate = new Mat())
            using (Mat result = new Mat())
            {
                long scaleTemplateStart = StartPhaseTiming();
                Cv2.Resize(verifyTemplate, scaledTemplate, new OpenCvSharp.Size(), scale, scale, InterpolationFlags.Area);
                StopPhaseTiming("HybridProposal.ScaleTemplate", scaleTemplateStart);
                if (scaledTemplate.Width > scaledSource.Width || scaledTemplate.Height > scaledSource.Height)
                {
                    return false;
                }

                long scaledMatchStart = StartPhaseTiming();
                Cv2.MatchTemplate(scaledSource, scaledTemplate, result, TemplateMatchModes.CCoeffNormed);
                List<TemplateMatchLocation> locations = FindTopTemplateMatchLocations(
                    result,
                    HybridProposalScaledTopK,
                    GetHybridProposalSuppressionRadius(scaledTemplate));
                StopPhaseTiming("HybridProposal.ScaledMatch", scaledMatchStart);

                MatchCandidate best = null;
                double bestImageScore = double.MinValue;
                foreach (TemplateMatchLocation location in locations)
                {
                    double predictedX = location.Location.X / scale;
                    double predictedY = location.Location.Y / scale;
                    if (!TryRefineHybridTemplateLocation(
                        source,
                        verifyTemplate,
                        predictedX,
                        predictedY,
                        scale,
                        out OpenCvSharp.Point refinedLocation,
                        out double refinedImageScore))
                    {
                        continue;
                    }

                    MatchCandidate refinedCandidate = CreateHybridImageProposalCandidateFromLocation(
                        gradients,
                        modelCache,
                        angle,
                        verifyTemplate.Width,
                        verifyTemplate.Height,
                        refinedLocation,
                        refinedImageScore,
                        suppressedBounds);
                    if (refinedCandidate == null)
                    {
                        continue;
                    }

                    if (best == null
                        || refinedImageScore > bestImageScore
                        || (Math.Abs(refinedImageScore - bestImageScore) <= 0.0000001D
                            && IsBetterCandidate(refinedCandidate, best)))
                    {
                        best = refinedCandidate;
                        bestImageScore = refinedImageScore;
                    }
                }

                if (best == null)
                {
                    return false;
                }

                candidate = best;
                imageScore = bestImageScore;
                return true;
            }
        }

        private MatchCandidate CreateHybridImageProposalCandidateFromLocation(
            GradientImage gradients,
            TemplateModelCache modelCache,
            double angle,
            int templateWidth,
            int templateHeight,
            OpenCvSharp.Point topLeft,
            double imageScore,
            List<RectangleF> suppressedBounds)
        {
            RotatedEdgeTemplateModel model = modelCache.GetRotatedModel(angle);
            double templateCenterX = topLeft.X + (templateWidth / 2D);
            double templateCenterY = topLeft.Y + (templateHeight / 2D);
            int centerX = (int)Math.Round(templateCenterX - model.TemplateCenterOffsetX);
            int centerY = (int)Math.Round(templateCenterY - model.TemplateCenterOffsetY);
            RectangleF bounds = CreateLocalBounds(centerX, centerY, model);
            if (IsSuppressed(bounds, suppressedBounds))
            {
                return null;
            }

            if (!IsCandidateInsideImage(gradients, model, centerX, centerY))
            {
                return null;
            }

            double edgeScore = ScoreCandidate(gradients, model, centerX, centerY);
            MatchCandidate candidate = CreateMatchCandidate(model, centerX, centerY, bounds, edgeScore);
            candidate.ImageVerifyScore = imageScore;
            return candidate;
        }

        private static bool IsCandidateInsideImage(
            GradientImage gradients,
            RotatedEdgeTemplateModel model,
            int centerX,
            int centerY)
        {
            return centerX + model.MinOffsetX >= 0
                && centerY + model.MinOffsetY >= 0
                && centerX + model.MaxOffsetX < gradients.Width
                && centerY + model.MaxOffsetY < gradients.Height;
        }

        private static bool TryGetHybridProposalScale(Mat source, Mat template, out double scale)
        {
            scale = 1D;
            int sourcePixels = source.Width * source.Height;
            int minTemplateSize = Math.Min(template.Width, template.Height);
            if (sourcePixels < HybridProposalDownscaleSourcePixels
                || minTemplateSize < HybridProposalMinScaledTemplateSize)
            {
                return false;
            }

            double targetScale = Math.Sqrt((double)HybridProposalDownscaleTargetPixels / sourcePixels);
            double templateScale = (double)HybridProposalMinScaledTemplateSize / minTemplateSize;
            scale = Clamp(Math.Max(targetScale, templateScale), 0.25D, 0.75D);
            int scaledTemplateWidth = Math.Max(1, (int)Math.Round(template.Width * scale));
            int scaledTemplateHeight = Math.Max(1, (int)Math.Round(template.Height * scale));
            return scale < 0.95D
                && scaledTemplateWidth >= HybridProposalMinScaledTemplateSize
                && scaledTemplateHeight >= HybridProposalMinScaledTemplateSize;
        }

        private static List<TemplateMatchLocation> FindTopTemplateMatchLocations(
            Mat result,
            int count,
            int suppressionRadius)
        {
            List<TemplateMatchLocation> locations = new List<TemplateMatchLocation>(Math.Max(1, count));
            using (Mat work = result.Clone())
            {
                int limit = Math.Max(1, count);
                for (int i = 0; i < limit; i++)
                {
                    Cv2.MinMaxLoc(work, out _, out double maxValue, out _, out OpenCvSharp.Point maxLocation);
                    locations.Add(new TemplateMatchLocation(maxLocation, maxValue));

                    int left = Math.Max(0, maxLocation.X - suppressionRadius);
                    int top = Math.Max(0, maxLocation.Y - suppressionRadius);
                    int right = Math.Min(work.Width - 1, maxLocation.X + suppressionRadius);
                    int bottom = Math.Min(work.Height - 1, maxLocation.Y + suppressionRadius);
                    if (right < left || bottom < top)
                    {
                        continue;
                    }

                    using (Mat suppressed = new Mat(work, new Rect(left, top, right - left + 1, bottom - top + 1)))
                    {
                        suppressed.SetTo(new Scalar(-1D));
                    }
                }
            }

            return locations;
        }

        private static int GetHybridProposalSuppressionRadius(Mat scaledTemplate)
        {
            return Math.Max(4, Math.Min(scaledTemplate.Width, scaledTemplate.Height) / 3);
        }

        private bool TryRefineHybridTemplateLocation(
            Mat source,
            Mat verifyTemplate,
            double predictedX,
            double predictedY,
            double scale,
            out OpenCvSharp.Point location,
            out double score)
        {
            location = new OpenCvSharp.Point();
            score = double.NaN;
            int maxTopLeftX = source.Width - verifyTemplate.Width;
            int maxTopLeftY = source.Height - verifyTemplate.Height;
            if (maxTopLeftX < 0 || maxTopLeftY < 0)
            {
                return false;
            }

            int radius = (int)Math.Ceiling(Math.Max(HybridProposalMinRefineRadius, 2D / scale));
            int centerX = ClampInt((int)Math.Round(predictedX), 0, maxTopLeftX);
            int centerY = ClampInt((int)Math.Round(predictedY), 0, maxTopLeftY);
            int minX = ClampInt(centerX - radius, 0, maxTopLeftX);
            int maxX = ClampInt(centerX + radius, 0, maxTopLeftX);
            int minY = ClampInt(centerY - radius, 0, maxTopLeftY);
            int maxY = ClampInt(centerY + radius, 0, maxTopLeftY);
            if (maxX < minX || maxY < minY)
            {
                return false;
            }

            Rect patchRect = new Rect(
                minX,
                minY,
                (maxX - minX) + verifyTemplate.Width,
                (maxY - minY) + verifyTemplate.Height);
            using (Mat sourcePatch = new Mat(source, patchRect))
            using (Mat result = new Mat())
            {
                long refineStart = StartPhaseTiming();
                Cv2.MatchTemplate(sourcePatch, verifyTemplate, result, TemplateMatchModes.CCoeffNormed);
                Cv2.MinMaxLoc(result, out _, out double maxValue, out _, out OpenCvSharp.Point localLocation);
                StopPhaseTiming("HybridProposal.RefineMatch", refineStart);
                location = new OpenCvSharp.Point(patchRect.X + localLocation.X, patchRect.Y + localLocation.Y);
                score = NormalizeTemplateMatchScore(maxValue);
                return true;
            }
        }

        private static double NormalizeTemplateMatchScore(double value)
        {
            return Clamp((value + 1D) / 2D, 0D, 1D);
        }

        private static int ClampInt(int value, int min, int max)
        {
            if (value < min)
            {
                return min;
            }

            if (value > max)
            {
                return max;
            }

            return value;
        }

        public MatchCandidate ApplyHybridVerification(
            Mat source,
            Mat template,
            MatchCandidate fallback,
            IEnumerable<MatchCandidate> candidateSeeds,
            MatchCandidate imageProposal)
        {
            if (!ShouldUseHybridVerify() || fallback == null)
            {
                return fallback;
            }

            int topN = Math.Max(1, property.HYBRID_VERIFY_TOP_N);
            double imageWeight = Clamp(property.HYBRID_VERIFY_IMAGE_WEIGHT, 0D, 1D);
            double edgeWeight = 1D - imageWeight;
            int verifyCapacity = Math.Max(topN, GetCandidateSeedCapacity());
            List<MatchCandidate> candidates = new List<MatchCandidate>(verifyCapacity + 1);
            TrackCandidateSeed(candidates, fallback, verifyCapacity);
            MergeCandidateSeeds(candidates, candidateSeeds, verifyCapacity);
            AddCandidateSeed(candidates, imageProposal);
            bool containsImageProposal = imageProposal != null
                && candidates.Any(candidate => IsSameCandidate(candidate, imageProposal));

            List<HybridVerificationScore> verifiedScores = new List<HybridVerificationScore>(candidates.Count);
            double bestImageScore = double.MinValue;
            MatchCandidate bestImageCandidate = null;
            foreach (MatchCandidate candidate in candidates)
            {
                double imageScore = candidate.ImageVerifyScore;
                if (double.IsNaN(imageScore)
                    && !TryComputeHybridImageScore(source, template, candidate, out imageScore))
                {
                    continue;
                }

                verifiedScores.Add(new HybridVerificationScore(candidate, imageScore));
                if (imageScore > bestImageScore)
                {
                    bestImageScore = imageScore;
                    bestImageCandidate = candidate;
                }
            }

            MatchCandidate best = fallback;
            foreach (HybridVerificationScore verifiedScore in verifiedScores)
            {
                MatchCandidate candidate = verifiedScore.Candidate;
                double imageScore = verifiedScore.ImageScore;
                double descriptorScore = double.NaN;
                double finalVerificationScore = imageScore;

                // Edge-map descriptors are only a near-tie resolver. They must not let a weak
                // image proposal beat the clearly better image location in repeated patterns.
                if (imageScore >= bestImageScore - HybridDescriptorImageTieMargin
                    && IsNearHybridBestImageCandidate(candidate, bestImageCandidate, template)
                    && TryComputeHybridEdgeDescriptorScore(source, template, candidate, out descriptorScore))
                {
                    finalVerificationScore = BlendHybridVerificationScore(imageScore, descriptorScore);
                }

                candidate.ImageVerifyScore = imageScore;
                candidate.DescriptorVerifyScore = descriptorScore;
                candidate.HybridScore = (candidate.Score * edgeWeight) + (finalVerificationScore * imageWeight);
                if (IsBetterHybridCandidate(candidate, best))
                {
                    best = candidate;
                }
            }

            RecordHybridVerificationDiagnostics(
                candidates.Count,
                verifiedScores.Count,
                containsImageProposal,
                imageProposal,
                fallback,
                best);
            RecordCandidateAmbiguityDiagnostics(candidates, best, template);
            return best;
        }

        private void RecordHybridVerificationDiagnostics(
            int candidateCount,
            int verifiedCount,
            bool containsImageProposal,
            MatchCandidate imageProposal,
            MatchCandidate fallback,
            MatchCandidate selected)
        {
            if (!ShouldUseHybridVerify())
            {
                return;
            }

            candidateDiagnostics.HybridCandidateCount += Math.Max(0, candidateCount);
            candidateDiagnostics.HybridVerifiedCount += Math.Max(0, verifiedCount);
            if (containsImageProposal)
            {
                candidateDiagnostics.ImageProposalInVerificationCount++;
            }

            if (IsSameCandidate(selected, imageProposal))
            {
                candidateDiagnostics.ImageProposalSelectedCount++;
            }

            if (IsSameCandidate(selected, fallback))
            {
                candidateDiagnostics.FallbackSelectedCount++;
            }
        }

        private static void AddCandidateSeed(List<MatchCandidate> seeds, MatchCandidate candidate)
        {
            if (seeds == null || candidate == null)
            {
                return;
            }

            foreach (MatchCandidate seed in seeds)
            {
                if (IsSameCandidate(seed, candidate))
                {
                    return;
                }
            }

            seeds.Add(candidate);
            seeds.Sort((left, right) => right.Score.CompareTo(left.Score));
        }

        private bool TryComputeHybridImageScore(
            Mat source,
            Mat template,
            MatchCandidate candidate,
            out double imageScore)
        {
            imageScore = 0D;
            if (OpenCvHelper.IsImageEmpty(source)
                || OpenCvHelper.IsImageEmpty(template)
                || candidate == null)
            {
                return false;
            }

            using (Mat verifyTemplate = CreateHybridVerifyTemplate(template, candidate.Angle, candidate.Scale))
            using (Mat result = new Mat())
            {
                Rect patchRect = CreateTemplatePatchRect(source, verifyTemplate, candidate);
                if (patchRect.Width <= 0 || patchRect.Height <= 0)
                {
                    return false;
                }

                using (Mat sourcePatch = new Mat(source, patchRect))
                {
                    long imageScoreStart = StartPhaseTiming();
                    Cv2.MatchTemplate(sourcePatch, verifyTemplate, result, TemplateMatchModes.CCoeffNormed);
                    Cv2.MinMaxLoc(result, out _, out double maxValue, out _, out _);
                    StopPhaseTiming("HybridVerify.ImageScore", imageScoreStart);
                    imageScore = NormalizeTemplateMatchScore(maxValue);
                    return true;
                }
            }
        }

        private static bool IsNearHybridBestImageCandidate(
            MatchCandidate candidate,
            MatchCandidate bestImageCandidate,
            Mat template)
        {
            if (candidate == null || bestImageCandidate == null)
            {
                return false;
            }

            double dx = candidate.TemplateCenter.X - bestImageCandidate.TemplateCenter.X;
            double dy = candidate.TemplateCenter.Y - bestImageCandidate.TemplateCenter.Y;
            double distance = Math.Sqrt((dx * dx) + (dy * dy));
            double candidateScale = NormalizeScale(candidate.Scale);
            double threshold = Math.Max(6D, Math.Min(template.Width, template.Height) * candidateScale * 0.35D);
            return distance <= threshold;
        }

        private bool TryComputeHybridEdgeDescriptorScore(
            Mat source,
            Mat template,
            MatchCandidate candidate,
            out double score)
        {
            score = double.NaN;
            using (Mat verifyTemplate = CreateHybridVerifyTemplate(template, candidate.Angle, candidate.Scale))
            using (Mat templateEdges = new Mat())
            {
                Rect patchRect = CreateTemplatePatchRect(source, verifyTemplate, candidate);
                if (patchRect.Width <= 0 || patchRect.Height <= 0)
                {
                    return false;
                }

                using (Mat sourcePatch = new Mat(source, patchRect))
                {
                    long descriptorTemplateStart = StartPhaseTiming();
                    Cv2.Canny(
                        verifyTemplate,
                        templateEdges,
                        property.CANNY_LOW,
                        property.CANNY_HIGH,
                        NormalizeCannyAperture(property.CANNY_APERTURE_SIZE),
                        property.USE_L2_GRADIENT);
                    StopPhaseTiming("HybridVerify.DescriptorTemplateCanny", descriptorTemplateStart);
                    return TryComputeHybridEdgeDescriptorScore(sourcePatch, templateEdges, out score);
                }
            }
        }

        private bool TryComputeHybridEdgeDescriptorScore(Mat sourcePatch, Mat templateEdges, out double score)
        {
            score = double.NaN;
            if (OpenCvHelper.IsImageEmpty(sourcePatch)
                || OpenCvHelper.IsImageEmpty(templateEdges)
                || sourcePatch.Width != templateEdges.Width
                || sourcePatch.Height != templateEdges.Height)
            {
                return false;
            }

            using (Mat sourceEdges = new Mat())
            using (Mat result = new Mat())
            {
                long descriptorSourceStart = StartPhaseTiming();
                Cv2.Canny(
                    sourcePatch,
                    sourceEdges,
                    property.CANNY_LOW,
                    property.CANNY_HIGH,
                    NormalizeCannyAperture(property.CANNY_APERTURE_SIZE),
                    property.USE_L2_GRADIENT);
                StopPhaseTiming("HybridVerify.DescriptorSourceCanny", descriptorSourceStart);

                if (Cv2.CountNonZero(sourceEdges) == 0 || Cv2.CountNonZero(templateEdges) == 0)
                {
                    return false;
                }

                long descriptorMatchStart = StartPhaseTiming();
                Cv2.MatchTemplate(sourceEdges, templateEdges, result, TemplateMatchModes.CCoeffNormed);
                Cv2.MinMaxLoc(result, out _, out double maxValue, out _, out _);
                StopPhaseTiming("HybridVerify.DescriptorMatch", descriptorMatchStart);
                score = NormalizeTemplateMatchScore(maxValue);
                return true;
            }
        }

        private static double BlendHybridVerificationScore(double imageScore, double descriptorScore)
        {
            double descriptorWeight = Clamp(HybridEdgeDescriptorWeight, 0D, 1D);
            return (imageScore * (1D - descriptorWeight)) + (descriptorScore * descriptorWeight);
        }

        private static Rect CreateTemplatePatchRect(Mat source, Mat template, MatchCandidate candidate)
        {
            int x = (int)Math.Round(candidate.TemplateCenter.X - (template.Width / 2D));
            int y = (int)Math.Round(candidate.TemplateCenter.Y - (template.Height / 2D));
            if (x < 0 || y < 0 || x + template.Width > source.Width || y + template.Height > source.Height)
            {
                return new Rect();
            }

            return new Rect(x, y, template.Width, template.Height);
        }

        private static Mat CreateHybridVerifyTemplate(Mat template, double angle)
        {
            if (Math.Abs(angle) <= 0.0001D)
            {
                return template.Clone();
            }

            Mat rotated = new Mat();
            Point2f center = new Point2f((template.Width - 1) / 2f, (template.Height - 1) / 2f);
            using (Mat rotation = Cv2.GetRotationMatrix2D(center, angle, 1D))
            {
                Cv2.WarpAffine(
                    template,
                    rotated,
                    rotation,
                    template.Size(),
                    InterpolationFlags.Linear,
                    BorderTypes.Constant,
                    Scalar.Black);
            }

            return rotated;
        }

        private static Mat CreateHybridVerifyTemplate(Mat template, double angle, double scale)
        {
            using (Mat scaledTemplate = ResizeTemplateForScale(template, scale))
            {
                return CreateHybridVerifyTemplate(scaledTemplate, angle);
            }
        }

        public bool ShouldUseHybridVerify()
        {
            return property.USE_HYBRID_VERIFY
                && property.HYBRID_VERIFY_TOP_N > 0
                && property.HYBRID_VERIFY_IMAGE_WEIGHT > 0D;
        }

        public int GetCandidateSeedCapacity()
        {
            int capacity = 0;
            if (property.USE_POSITION_REFINE && property.SEARCH_STEP > 1)
            {
                capacity = Math.Max(capacity, PositionRefineSeedCount);
            }

            if (ShouldUseHybridVerify())
            {
                // Hybrid verification needs more than a global top-N. A spatial grid keeps
                // candidates from other image regions alive when distractors dominate one area.
                int hybridCapacity = property.HYBRID_VERIFY_TOP_N
                    + (HybridCandidateGridColumns * HybridCandidateGridRows * HybridCandidateGridTopK);
                capacity = Math.Max(capacity, hybridCapacity);
            }

            if (property.USE_UNIQUE_MATCH_VALIDATION)
            {
                capacity = Math.Max(capacity, UniqueMatchMinimumInternalTopK);
            }

            return capacity;
        }

        public bool ShouldUseHybridSpatialSeeds()
        {
            return ShouldUseHybridVerify() || property.USE_UNIQUE_MATCH_VALIDATION;
        }

        private static bool IsBetterHybridCandidate(MatchCandidate candidate, MatchCandidate currentBest)
        {
            if (candidate == null || double.IsNaN(candidate.HybridScore))
            {
                return false;
            }

            if (currentBest == null || double.IsNaN(currentBest.HybridScore))
            {
                return true;
            }

            const double epsilon = 0.0000001D;
            if (candidate.HybridScore > currentBest.HybridScore + epsilon)
            {
                return true;
            }

            if (Math.Abs(candidate.HybridScore - currentBest.HybridScore) > epsilon)
            {
                return false;
            }

            return IsBetterCandidate(candidate, currentBest);
        }

    }

        private void DrawResultImage()
        {
            ReplaceResultImage(imageSource.Clone());
            OpenCvHelper.SetImageChannel3(imageResult);
            TemplateModelCache modelCache = modelStore.Current;

            foreach (MatchingResult result in results)
            {
                Rect rect = new Rect(
                    (int)Math.Round(result.Bounding.X),
                    (int)Math.Round(result.Bounding.Y),
                    (int)Math.Round(result.Bounding.Width),
                    (int)Math.Round(result.Bounding.Height));
                if (!DrawEdgeModelOutline(modelCache, result, new Scalar(50, 205, 50)))
                {
                    // Fallback only: EdgeBasedMatching should normally draw the taught edge model, not a box.
                    Cv2.Rectangle(imageResult, rect, new Scalar(50, 205, 50), 2);
                }

                Cv2.Circle(
                    imageResult,
                    new OpenCvSharp.Point((int)Math.Round(result.Center.X), (int)Math.Round(result.Center.Y)),
                    4,
                    Scalar.Red,
                    -1);
                Cv2.PutText(
                    imageResult,
                    $"#{result.Index} {result.Score:0.0} {(result.PolarityReversed ? "Reversed" : "Same")}",
                    new OpenCvSharp.Point(rect.X, Math.Max(14, rect.Y - 5)),
                    HersheyFonts.HersheySimplex,
                    0.45,
                    Scalar.Yellow,
                    1,
                    LineTypes.AntiAlias);
            }
        }

        private EdgeBasedMatchingDiagnosticEvidence CreateDiagnosticEvidence(VisionToolResult result)
        {
            EdgeTemplateModel model = modelStore.Current?.Model;
            Rect searchRoi = property?.USE_ROI == true
                ? NormalizeRoi(property.CvROI)
                : new Rect(0, 0, Math.Max(0, imageSource?.Width ?? 0), Math.Max(0, imageSource?.Height ?? 0));
            EdgeBasedMatchingDiagnosticEvidence evidence = new EdgeBasedMatchingDiagnosticEvidence
            {
                State = ResolveDiagnosticState(result),
                ErrorCode = result?.ErrorName ?? string.Empty,
                SearchRoi = new RectangleF(searchRoi.X, searchRoi.Y, searchRoi.Width, searchRoi.Height),
                TemplateWidth = Math.Max(0, model?.Width ?? 0),
                TemplateHeight = Math.Max(0, model?.Height ?? 0),
                ModelCenter = new PointF(
                    (float)(model?.Center.X ?? 0D),
                    (float)(model?.Center.Y ?? 0D)),
                SelectedCandidate = candidateDiagnostics.SelectedCandidate?.Clone(),
                StrongestSpatialAlternative = candidateDiagnostics.StrongestSpatialAlternative?.Clone()
            };
            if (model?.Points != null)
            {
                evidence.ModelPoints.AddRange(model.Points.Select(point => new PointF(point.X, point.Y)));
            }

            evidence.Reason = ResolveDiagnosticReason(result, evidence);
            return evidence;
        }

        private string ResolveDiagnosticReason(
            VisionToolResult result,
            EdgeBasedMatchingDiagnosticEvidence evidence)
        {
            if (!string.IsNullOrWhiteSpace(result?.Message))
            {
                return result.Message;
            }

            EdgeBasedMatchingCandidateDiagnostic selected = evidence?.SelectedCandidate;
            if (result?.Success == true && selected != null)
            {
                EdgeBasedMatchingCandidateDiagnostic alternative = evidence.StrongestSpatialAlternative;
                string alternativeText = alternative == null
                    ? "StrongestSpatialAlternative=None"
                    : string.Format(
                        CultureInfo.InvariantCulture,
                        "StrongestSpatialAlternative={0:0.###}, ScoreMargin={1:0.###}",
                        alternative.Score,
                        selected.Score - alternative.Score);
                return string.Format(
                    CultureInfo.InvariantCulture,
                    "Success: selected score {0:0.###} >= SCORE_MIN {1:0.###}; {2}.",
                    selected.Score,
                    property?.SCORE_MIN ?? 0D,
                    alternativeText);
            }

            return "Edge based matching did not retain a candidate decision.";
        }

        private static string ResolveDiagnosticState(VisionToolResult result)
        {
            if (result?.Success == true)
            {
                return "Success";
            }

            switch (result?.ErrorCode)
            {
                case VisionToolErrorCode.MatchingNoResult:
                    return "NoMatch";
                case VisionToolErrorCode.MatchingAmbiguous:
                    return "Ambiguous";
                default:
                    return "Error";
            }
        }

        private bool DrawEdgeModelOutline(TemplateModelCache modelCache, MatchingResult result, Scalar color)
        {
            if (modelCache?.Model == null || modelCache.Model.Points.Count == 0)
            {
                return false;
            }

            RotatedEdgeTemplateModel rotatedModel = modelCache.GetRotatedModel(result.Angle, result.Scale);
            if (rotatedModel.PointCount == 0)
            {
                return false;
            }

            double modelCenterX = result.Center.X - rotatedModel.TemplateCenterOffsetX;
            double modelCenterY = result.Center.Y - rotatedModel.TemplateCenterOffsetY;
            int drawnPointCount = 0;
            foreach (RotatedTemplateEdgePoint point in rotatedModel.Points)
            {
                int x = (int)Math.Round(modelCenterX + point.OffsetX);
                int y = (int)Math.Round(modelCenterY + point.OffsetY);
                if (x < 0 || y < 0 || x >= imageResult.Width || y >= imageResult.Height)
                {
                    continue;
                }

                Cv2.Circle(
                    imageResult,
                    new OpenCvSharp.Point(x, y),
                    1,
                    color,
                    -1,
                    LineTypes.AntiAlias);
                drawnPointCount++;
            }

            return drawnPointCount > 0;
        }

        private void ResetMatchingFailure()
        {
            lastMatchingErrorCode = VisionToolErrorCode.MatchingNoResult;
            lastMatchingMessage = "Edge based template matching found no result.";
        }

        private void SetMatchingFailure(VisionToolErrorCode errorCode, string message)
        {
            if (results.Count > 0)
            {
                return;
            }

            lastMatchingErrorCode = errorCode;
            lastMatchingMessage = message ?? string.Empty;
        }

        private string FormatMatchingOptions()
        {
            string roi = property.USE_MULTI_ROI
                ? $"Multi({property.CvROIS?.Count ?? 0})"
                : FormatRoi(NormalizeRoi(property.CvROI));
            string angle = property.USE_FIND_ANGLE
                ? $"Angle={property.FIND_ANGLE_MIN}..{property.FIND_ANGLE_MAX}/Step={property.FIND_ANGLE}"
                : "Angle=Off";
            if (property.USE_FIND_ANGLE && ShouldUseCoarseToFineAngleSearch())
            {
                angle += $"/Coarse={property.COARSE_ANGLE_STEP}/TopK={property.COARSE_ANGLE_TOP_K}";
            }

            string position = property.USE_POSITION_REFINE && property.SEARCH_STEP > 1
                ? $"SearchStep={property.SEARCH_STEP}+Refine"
                : $"SearchStep={property.SEARCH_STEP}";
            if (property.USE_SUBPIXEL_REFINE)
            {
                position += "+Subpixel";
            }

            string scale = property.USE_FIND_SCALE
                ? $"Scale={property.FIND_SCALE_MIN:0.###}..{property.FIND_SCALE_MAX:0.###}/Step={property.FIND_SCALE_STEP:0.###}"
                : "Scale=Off";
            string pyramid = property.USE_PYRAMID_POSITION_PROPOSAL
                ? $", PyramidProposal=Top{property.PYRAMID_POSITION_TOP_N}/Min{property.PYRAMID_POSITION_MIN_SCORE:0.###}"
                : string.Empty;
            string hybrid = ShouldUseHybridVerify()
                ? $", Hybrid=Top{property.HYBRID_VERIFY_TOP_N}/W{property.HYBRID_VERIFY_IMAGE_WEIGHT:0.###}"
                : string.Empty;
            string unique = property.USE_UNIQUE_MATCH_VALIDATION
                ? $", Unique=Margin{property.UNIQUE_MATCH_MIN_SCORE_MARGIN:0.###}"
                : string.Empty;
            string polarity = property.ALLOW_GLOBAL_POLARITY_REVERSAL
                ? ", Polarity=SameOrGloballyReversed"
                : ", Polarity=Same";
            return $"ScoreMin={property.SCORE_MIN}, MatchCount={property.NUM_MATCH}, Canny={property.CANNY_LOW}..{property.CANNY_HIGH}, {angle}, {scale}, {position}{pyramid}{hybrid}{unique}{polarity}, MaxPoints={property.MAX_TEMPLATE_POINTS}, ROI={roi}";
        }

        private static string FormatRoi(Rect roi)
        {
            return $"{roi.X},{roi.Y},{roi.Width},{roi.Height}";
        }

        private static int NormalizeCannyAperture(int value)
        {
            if (value <= 3)
            {
                return 3;
            }

            if (value <= 5)
            {
                return 5;
            }

            return 7;
        }

        private static double Clamp(double value, double min, double max)
        {
            if (value < min) { return min; }
            if (value > max) { return max; }
            return value;
        }

        private static double NormalizeScale(double scale)
        {
            return scale <= 0D ? 1D : Math.Round(scale, 6);
        }

        private static string CreateAngleScaleKey(double angle, double scale)
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "{0:0.######}:{1:0.######}",
                Math.Round(angle, 6),
                NormalizeScale(scale));
        }

        private static Mat ResizeTemplateForScale(Mat template, double scale)
        {
            if (OpenCvHelper.IsImageEmpty(template))
            {
                return new Mat();
            }

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

        private static int Clamp(int value, int min, int max)
        {
            if (value < min) { return min; }
            if (value > max) { return max; }
            return value;
        }

        private static List<TemplateEdgePoint> Downsample(List<TemplateEdgePoint> points, int maxPoints)
        {
            if (points == null || points.Count <= maxPoints)
            {
                return points ?? new List<TemplateEdgePoint>();
            }

            List<TemplateEdgePoint> sampled = new List<TemplateEdgePoint>(maxPoints);
            double step = (double)points.Count / maxPoints;
            for (int i = 0; i < maxPoints; i++)
            {
                sampled.Add(points[(int)Math.Floor(i * step)]);
            }

            return sampled;
        }

        private sealed class GradientImage : IDisposable
        {
            private GradientImage(
                int width,
                int height,
                double[] dxValues,
                double[] dyValues,
                double[] magnitudeValues,
                double[] unitDxValues,
                double[] unitDyValues)
            {
                Width = width;
                Height = height;
                DxValues = dxValues ?? Array.Empty<double>();
                DyValues = dyValues ?? Array.Empty<double>();
                MagnitudeValues = magnitudeValues ?? Array.Empty<double>();
                UnitDxValues = unitDxValues ?? Array.Empty<double>();
                UnitDyValues = unitDyValues ?? Array.Empty<double>();
            }

            public int Width { get; }
            public int Height { get; }
            private double[] DxValues { get; }
            private double[] DyValues { get; }
            private double[] MagnitudeValues { get; }
            public double[] UnitDxValues { get; }
            public double[] UnitDyValues { get; }

            public static GradientImage Create(Mat image, double unitMagnitudeThreshold)
            {
                using (Mat dx = new Mat())
                using (Mat dy = new Mat())
                using (Mat magnitude = new Mat())
                {
                    Cv2.Sobel(image, dx, MatType.CV_64F, 1, 0, 3);
                    Cv2.Sobel(image, dy, MatType.CV_64F, 0, 1, 3);
                    Cv2.Magnitude(dx, dy, magnitude);

                    // Scoring visits gradient values for every candidate center and template point.
                    // Cache compact arrays once so the hot loop avoids repeated Mat.At<T>() calls.
                    double[] dxValues = CopyMatToArray(dx);
                    double[] dyValues = CopyMatToArray(dy);
                    double[] magnitudeValues = CopyMatToArray(magnitude);
                    CreateUnitGradientArrays(
                        dxValues,
                        dyValues,
                        magnitudeValues,
                        unitMagnitudeThreshold,
                        out double[] unitDxValues,
                        out double[] unitDyValues);

                    return new GradientImage(
                        image.Width,
                        image.Height,
                        dxValues,
                        dyValues,
                        magnitudeValues,
                        unitDxValues,
                        unitDyValues);
                }
            }

            public double GetDx(int x, int y)
            {
                return DxValues[GetIndex(x, y)];
            }

            public double GetDy(int x, int y)
            {
                return DyValues[GetIndex(x, y)];
            }

            public double GetMagnitude(int x, int y)
            {
                return MagnitudeValues[GetIndex(x, y)];
            }

            public double GetUnitDx(int x, int y)
            {
                return UnitDxValues[GetIndex(x, y)];
            }

            public double GetUnitDy(int x, int y)
            {
                return UnitDyValues[GetIndex(x, y)];
            }

            private int GetIndex(int x, int y)
            {
                return (y * Width) + x;
            }

            private static double[] CopyMatToArray(Mat mat)
            {
                int width = mat.Width;
                int height = mat.Height;
                double[] values = new double[width * height];
                int index = 0;
                for (int y = 0; y < height; y++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        values[index++] = mat.At<double>(y, x);
                    }
                }

                return values;
            }

            private static void CreateUnitGradientArrays(
                double[] dxValues,
                double[] dyValues,
                double[] magnitudeValues,
                double magnitudeThreshold,
                out double[] unitDxValues,
                out double[] unitDyValues)
            {
                int length = magnitudeValues?.Length ?? 0;
                unitDxValues = new double[length];
                unitDyValues = new double[length];
                for (int i = 0; i < length; i++)
                {
                    double magnitude = magnitudeValues[i];
                    if (magnitude <= magnitudeThreshold)
                    {
                        continue;
                    }

                    double inverseMagnitude = 1D / magnitude;
                    unitDxValues[i] = dxValues[i] * inverseMagnitude;
                    unitDyValues[i] = dyValues[i] * inverseMagnitude;
                }
            }

            public void Dispose()
            {
            }
        }

        private sealed class TemplateModelCache : IDisposable
        {
            private readonly object sync = new object();
            private readonly Func<Mat, double, EdgeTemplateModel> modelFactory;
            private readonly Dictionary<string, List<RotatedEdgeTemplateModel>> searchModels
                = new Dictionary<string, List<RotatedEdgeTemplateModel>>(StringComparer.Ordinal);
            private readonly Dictionary<double, EdgeTemplateModel> scaledModels
                = new Dictionary<double, EdgeTemplateModel>();
            private readonly Dictionary<string, RotatedEdgeTemplateModel> rotatedModels
                = new Dictionary<string, RotatedEdgeTemplateModel>(StringComparer.Ordinal);

            public TemplateModelCache(
                string key,
                Mat preparedTemplate,
                EdgeTemplateModel model,
                Func<Mat, double, EdgeTemplateModel> modelFactory)
            {
                Key = key ?? string.Empty;
                PreparedTemplate = preparedTemplate ?? new Mat();
                Model = model;
                this.modelFactory = modelFactory ?? throw new ArgumentNullException(nameof(modelFactory));
            }

            public string Key { get; }
            public Mat PreparedTemplate { get; }
            public EdgeTemplateModel Model { get; }

            public List<RotatedEdgeTemplateModel> GetSearchModels(
                string key,
                IEnumerable<double> angles,
                IEnumerable<double> scales)
            {
                key = key ?? string.Empty;
                lock (sync)
                {
                    if (searchModels.TryGetValue(key, out List<RotatedEdgeTemplateModel> cached))
                    {
                        return cached;
                    }

                    List<RotatedEdgeTemplateModel> created = new List<RotatedEdgeTemplateModel>();
                    foreach (double scale in scales ?? new[] { 1D })
                    {
                        foreach (double angle in angles ?? Enumerable.Empty<double>())
                        {
                            created.Add(GetRotatedModelCore(angle, scale));
                        }
                    }

                    searchModels[key] = created;
                    return created;
                }
            }

            public RotatedEdgeTemplateModel GetRotatedModel(double angle)
            {
                return GetRotatedModel(angle, 1D);
            }

            public RotatedEdgeTemplateModel GetRotatedModel(double angle, double scale)
            {
                lock (sync)
                {
                    return GetRotatedModelCore(angle, scale);
                }
            }

            private RotatedEdgeTemplateModel GetRotatedModelCore(double angle, double scale)
            {
                double normalizedAngle = Math.Round(angle, 6);
                double normalizedScale = NormalizeScale(scale);
                string key = CreateAngleScaleKey(normalizedAngle, normalizedScale);
                if (!rotatedModels.TryGetValue(key, out RotatedEdgeTemplateModel model))
                {
                    model = CreateRotatedModel(GetScaledModelCore(normalizedScale), normalizedAngle);
                    rotatedModels[key] = model;
                }

                return model;
            }

            private EdgeTemplateModel GetScaledModelCore(double scale)
            {
                double normalizedScale = NormalizeScale(scale);
                if (Math.Abs(normalizedScale - 1D) <= 0.000001D)
                {
                    return Model;
                }

                if (!scaledModels.TryGetValue(normalizedScale, out EdgeTemplateModel model))
                {
                    using (Mat scaledTemplate = ResizeTemplateForScale(PreparedTemplate, normalizedScale))
                    {
                        model = modelFactory(scaledTemplate, normalizedScale);
                    }

                    scaledModels[normalizedScale] = model;
                }

                return model;
            }

            public void Dispose()
            {
                lock (sync)
                {
                    PreparedTemplate?.Dispose();
                    searchModels.Clear();
                    scaledModels.Clear();
                    rotatedModels.Clear();
                }
            }
        }

        private sealed class EdgeTemplateModel
        {
            public EdgeTemplateModel(
                int width,
                int height,
                Point2d center,
                List<TemplateEdgePoint> points,
                int rawPointCount,
                double scale)
            {
                Width = width;
                Height = height;
                Center = center;
                Points = points ?? new List<TemplateEdgePoint>();
                RawPointCount = rawPointCount < 0 ? Points.Count : rawPointCount;
                Scale = NormalizeScale(scale);
            }

            public int Width { get; }
            public int Height { get; }
            public Point2d Center { get; }
            public List<TemplateEdgePoint> Points { get; }
            public int RawPointCount { get; }
            public double Scale { get; }
        }

        private sealed class PyramidLevelDiagnostics
        {
            public PyramidLevelDiagnostics(int edgePointCount, double coverageArea, double quadrantBalance)
            {
                EdgePointCount = edgePointCount;
                CoverageArea = coverageArea;
                QuadrantBalance = quadrantBalance;
            }

            public int EdgePointCount { get; }
            public double CoverageArea { get; }
            public double QuadrantBalance { get; }
        }

        private sealed class RotatedEdgeTemplateModel
        {
            private readonly object indexOffsetSync = new object();
            private readonly Dictionary<int, int[]> indexOffsetsByImageWidth = new Dictionary<int, int[]>();

            public RotatedEdgeTemplateModel(
                int templateWidth,
                int templateHeight,
                double angle,
                List<RotatedTemplateEdgePoint> points,
                int minOffsetX,
                int maxOffsetX,
                int minOffsetY,
                int maxOffsetY,
                RectangleF localBounds,
                double templateCenterOffsetX,
                double templateCenterOffsetY,
                double scale)
            {
                TemplateWidth = templateWidth;
                TemplateHeight = templateHeight;
                Angle = angle;
                Scale = NormalizeScale(scale);
                Points = points ?? new List<RotatedTemplateEdgePoint>();
                MinOffsetX = minOffsetX;
                MaxOffsetX = maxOffsetX;
                MinOffsetY = minOffsetY;
                MaxOffsetY = maxOffsetY;
                LocalBounds = localBounds;
                TemplateCenterOffsetX = templateCenterOffsetX;
                TemplateCenterOffsetY = templateCenterOffsetY;
                PointCount = Points.Count;
                OffsetXValues = new int[PointCount];
                OffsetYValues = new int[PointCount];
                UnitDxValues = new double[PointCount];
                UnitDyValues = new double[PointCount];
                for (int i = 0; i < PointCount; i++)
                {
                    RotatedTemplateEdgePoint point = Points[i];
                    OffsetXValues[i] = point.OffsetX;
                    OffsetYValues[i] = point.OffsetY;
                    UnitDxValues[i] = point.UnitDx;
                    UnitDyValues[i] = point.UnitDy;
                }
            }

            public int TemplateWidth { get; }
            public int TemplateHeight { get; }
            public double Angle { get; }
            public double Scale { get; }
            public List<RotatedTemplateEdgePoint> Points { get; }
            public int PointCount { get; }
            public int[] OffsetXValues { get; }
            public int[] OffsetYValues { get; }
            public double[] UnitDxValues { get; }
            public double[] UnitDyValues { get; }
            public int MinOffsetX { get; }
            public int MaxOffsetX { get; }
            public int MinOffsetY { get; }
            public int MaxOffsetY { get; }
            public RectangleF LocalBounds { get; }
            public double TemplateCenterOffsetX { get; }
            public double TemplateCenterOffsetY { get; }

            public int[] GetIndexOffsets(int imageWidth)
            {
                lock (indexOffsetSync)
                {
                    if (indexOffsetsByImageWidth.TryGetValue(imageWidth, out int[] cached))
                    {
                        return cached;
                    }

                    int[] created = new int[PointCount];
                    for (int i = 0; i < PointCount; i++)
                    {
                        created[i] = (OffsetYValues[i] * imageWidth) + OffsetXValues[i];
                    }

                    indexOffsetsByImageWidth[imageWidth] = created;
                    return created;
                }
            }
        }

        private sealed class TemplateEdgePoint
        {
            public int X { get; set; }
            public int Y { get; set; }
            public int OffsetX { get; set; }
            public int OffsetY { get; set; }
            public double UnitDx { get; set; }
            public double UnitDy { get; set; }
            public double InvMagnitude { get; set; }
        }

        private sealed class RotatedTemplateEdgePoint
        {
            public int OffsetX { get; set; }
            public int OffsetY { get; set; }
            public double UnitDx { get; set; }
            public double UnitDy { get; set; }
        }

        private sealed class CandidateScoreContext
        {
            public CandidateScoreContext(
                int imageWidth,
                int pointCount,
                int[] indexOffsets,
                double[] templateUnitDxValues,
                double[] templateUnitDyValues,
                double[] sourceUnitDxValues,
                double[] sourceUnitDyValues,
                double[] breakSumThresholds,
                bool allowGlobalPolarityReversal)
            {
                ImageWidth = imageWidth;
                PointCount = pointCount;
                IndexOffsets = indexOffsets ?? Array.Empty<int>();
                TemplateUnitDxValues = templateUnitDxValues ?? Array.Empty<double>();
                TemplateUnitDyValues = templateUnitDyValues ?? Array.Empty<double>();
                SourceUnitDxValues = sourceUnitDxValues ?? Array.Empty<double>();
                SourceUnitDyValues = sourceUnitDyValues ?? Array.Empty<double>();
                BreakSumThresholds = breakSumThresholds ?? Array.Empty<double>();
                AllowGlobalPolarityReversal = allowGlobalPolarityReversal;
            }

            public int ImageWidth { get; }
            public int PointCount { get; }
            public int[] IndexOffsets { get; }
            public double[] TemplateUnitDxValues { get; }
            public double[] TemplateUnitDyValues { get; }
            public double[] SourceUnitDxValues { get; }
            public double[] SourceUnitDyValues { get; }
            public double[] BreakSumThresholds { get; }
            public bool AllowGlobalPolarityReversal { get; }
        }

        private sealed class CandidateSearchState
        {
            private readonly int seedCapacity;
            private readonly SpatialCandidateSeedTracker spatialSeeds;

            public CandidateSearchState(
                int seedCapacity,
                bool useSpatialSeeds,
                int minCenterX,
                int minCenterY,
                int maxCenterX,
                int maxCenterY)
            {
                this.seedCapacity = Math.Max(0, seedCapacity);
                CandidateSeeds = this.seedCapacity > 0
                    ? new List<MatchCandidate>(this.seedCapacity)
                    : null;
                spatialSeeds = useSpatialSeeds
                    ? new SpatialCandidateSeedTracker(
                        minCenterX,
                        minCenterY,
                        maxCenterX,
                        maxCenterY,
                        HybridCandidateGridColumns,
                        HybridCandidateGridRows,
                        HybridCandidateGridTopK)
                    : null;
            }

            public MatchCandidate Best { get; private set; }
            public List<MatchCandidate> CandidateSeeds { get; }

            public void Consider(
                RotatedEdgeTemplateModel model,
                int centerX,
                int centerY,
                double score)
            {
                MatchCandidate candidate = null;
                if (IsBetterScore(score, centerX, centerY, Best))
                {
                    candidate = CreateMatchCandidate(
                        model,
                        centerX,
                        centerY,
                        CreateLocalBounds(centerX, centerY, model),
                        score);
                    Best = candidate;
                }

                bool shouldTrackSeed = ShouldTrackCandidateSeed(CandidateSeeds, score, seedCapacity);
                bool shouldTrackSpatialSeed = spatialSeeds?.ShouldTrack(centerX, centerY, score) ?? false;
                if (shouldTrackSeed)
                {
                    candidate = candidate ?? CreateMatchCandidate(
                        model,
                        centerX,
                        centerY,
                        CreateLocalBounds(centerX, centerY, model),
                        score);
                    TrackCandidateSeed(CandidateSeeds, candidate, seedCapacity);
                }

                if (shouldTrackSpatialSeed)
                {
                    candidate = candidate ?? CreateMatchCandidate(
                        model,
                        centerX,
                        centerY,
                        CreateLocalBounds(centerX, centerY, model),
                        score);
                    spatialSeeds.Track(candidate);
                }
            }

            public void Merge(CandidateSearchState other)
            {
                if (other == null)
                {
                    return;
                }

                if (IsBetterCandidate(other.Best, Best))
                {
                    Best = other.Best;
                }

                if (CandidateSeeds == null || other.CandidateSeeds == null)
                {
                    return;
                }

                foreach (MatchCandidate seed in other.CandidateSeeds)
                {
                    TrackCandidateSeed(CandidateSeeds, seed, seedCapacity);
                }

                spatialSeeds?.Merge(other.spatialSeeds);
            }

            public List<MatchCandidate> CreateMergedCandidateSeeds()
            {
                if (CandidateSeeds == null)
                {
                    return null;
                }

                MergeCandidateSeeds(CandidateSeeds, spatialSeeds?.GetCandidates(), seedCapacity);
                return CandidateSeeds;
            }
        }

        private sealed class SpatialCandidateSeedTracker
        {
            private readonly int minCenterX;
            private readonly int minCenterY;
            private readonly double width;
            private readonly double height;
            private readonly int columns;
            private readonly int rows;
            private readonly int cellCapacity;
            private readonly List<MatchCandidate>[] candidates;

            public SpatialCandidateSeedTracker(
                int minCenterX,
                int minCenterY,
                int maxCenterX,
                int maxCenterY,
                int columns,
                int rows,
                int cellCapacity)
            {
                this.minCenterX = minCenterX;
                this.minCenterY = minCenterY;
                this.columns = Math.Max(1, columns);
                this.rows = Math.Max(1, rows);
                this.cellCapacity = Math.Max(1, cellCapacity);
                width = Math.Max(1D, maxCenterX - minCenterX + 1D);
                height = Math.Max(1D, maxCenterY - minCenterY + 1D);
                candidates = new List<MatchCandidate>[this.columns * this.rows];
            }

            public bool ShouldTrack(int centerX, int centerY, double score)
            {
                List<MatchCandidate> cell = candidates[GetIndex(centerX, centerY)];
                return cell == null
                    || cell.Count < cellCapacity
                    || score > cell[cell.Count - 1].Score;
            }

            public void Track(MatchCandidate candidate)
            {
                if (candidate == null)
                {
                    return;
                }

                int index = GetIndex(candidate.Center.X, candidate.Center.Y);
                if (candidates[index] == null)
                {
                    candidates[index] = new List<MatchCandidate>(cellCapacity);
                }

                TrackCandidateSeed(candidates[index], candidate, cellCapacity);
            }

            public void Merge(SpatialCandidateSeedTracker other)
            {
                if (other == null)
                {
                    return;
                }

                foreach (MatchCandidate candidate in other.GetCandidates())
                {
                    Track(candidate);
                }
            }

            public IEnumerable<MatchCandidate> GetCandidates()
            {
                foreach (List<MatchCandidate> cell in candidates)
                {
                    if (cell == null)
                    {
                        continue;
                    }

                    foreach (MatchCandidate candidate in cell)
                    {
                        yield return candidate;
                    }
                }
            }

            private int GetIndex(double centerX, double centerY)
            {
                int column = ClampIndex((int)Math.Floor(((centerX - minCenterX) / width) * columns), columns);
                int row = ClampIndex((int)Math.Floor(((centerY - minCenterY) / height) * rows), rows);
                return (row * columns) + column;
            }

            private static int ClampIndex(int value, int count)
            {
                if (value < 0)
                {
                    return 0;
                }

                if (value >= count)
                {
                    return count - 1;
                }

                return value;
            }
        }

        private sealed class MatchCandidate
        {
            public Point2d Center { get; set; }
            public Point2d TemplateCenter { get; set; }
            public RectangleF Bounds { get; set; }
            public double Score { get; set; }
            public double ImageVerifyScore { get; set; } = double.NaN;
            public double DescriptorVerifyScore { get; set; } = double.NaN;
            public double HybridScore { get; set; } = double.NaN;
            public bool PolarityReversed { get; set; }
            public double Angle { get; set; }
            public double Scale { get; set; } = 1D;
            public int Width { get; set; }
            public int Height { get; set; }
        }

        private sealed class CandidateDiagnostics
        {
            public EdgeBasedMatchingCandidateDiagnostic SelectedCandidate { get; set; }
            public EdgeBasedMatchingCandidateDiagnostic StrongestSpatialAlternative { get; set; }
            public UniqueMatchState UniqueMatchState { get; set; }
            public int UniqueMatchPlausibleAlternativeCount { get; set; }
            public double UniqueMatchSelectedScore { get; set; } = double.NegativeInfinity;
            public double UniqueMatchStrongestAlternativeScore { get; set; } = double.NegativeInfinity;
            public double UniqueMatchScoreMargin { get; set; } = double.NegativeInfinity;
            public double UniqueMatchDistanceThreshold { get; set; } = double.NegativeInfinity;
            public int ImageProposalCount { get; set; }
            public int ImageProposalMissingCount { get; set; }
            public int FastPathCount { get; set; }
            public int FallbackSearchCount { get; set; }
            public int EdgeSeedCount { get; set; }
            public int EdgeSearchBestCount { get; set; }
            public int HybridCandidateCount { get; set; }
            public int HybridVerifiedCount { get; set; }
            public int ImageProposalInVerificationCount { get; set; }
            public int ImageProposalSelectedCount { get; set; }
            public int FallbackSelectedCount { get; set; }
            public int PyramidProposalAttemptCount { get; set; }
            public int PyramidProposalAcceptedCount { get; set; }
            public int PyramidProposalFallbackCount { get; set; }
            public int PyramidProposalCandidateCount { get; set; }
            public int PyramidProposalVerifiedCount { get; set; }
            public int AmbiguousSelectionCount { get; set; }
            public int AmbiguousAlternativeCount { get; set; }
            public int SameScaleAmbiguousAlternativeCount { get; set; }
            public int DifferentScaleAmbiguousAlternativeCount { get; set; }
            public double MaxImageProposalEdgeScore { get; set; } = double.NegativeInfinity;
            public double MaxImageProposalImageScore { get; set; } = double.NegativeInfinity;
            public double MaxEdgeSearchScore { get; set; } = double.NegativeInfinity;
            public double MaxPyramidProposalScore { get; set; } = double.NegativeInfinity;
            public double MaxPyramidVerifiedScore { get; set; } = double.NegativeInfinity;
            public double MaxAmbiguousAlternativeScore { get; set; } = double.NegativeInfinity;
            public double MinAmbiguousScoreGap { get; set; } = double.PositiveInfinity;
            public double MaxAmbiguousDistance { get; set; } = double.NegativeInfinity;
            public double MaxAmbiguousScaleDelta { get; set; } = double.NegativeInfinity;
        }

        private enum UniqueMatchState
        {
            Disabled = 0,
            NoMatch = 1,
            Success = 2,
            Ambiguous = 3
        }

        private sealed class HybridVerificationScore
        {
            public HybridVerificationScore(MatchCandidate candidate, double imageScore)
            {
                Candidate = candidate;
                ImageScore = imageScore;
            }

            public MatchCandidate Candidate { get; }
            public double ImageScore { get; }
        }

        private sealed class TemplateMatchLocation
        {
            public TemplateMatchLocation(OpenCvSharp.Point location, double score)
            {
                Location = location;
                Score = score;
            }

            public OpenCvSharp.Point Location { get; }
            public double Score { get; }
        }
    }
}

