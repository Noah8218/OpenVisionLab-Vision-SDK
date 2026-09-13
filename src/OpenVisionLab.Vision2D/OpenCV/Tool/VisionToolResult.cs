using OpenCvSharp;
using OpenVisionLab.Vision2D.Result;
using System;
using System.Collections.Generic;
using System.Linq;

namespace OpenVisionLab.Vision2D.Tool
{
    public enum VisionToolErrorCode
    {
        None = 0,
        Unknown = 1,
        InputImageInvalid = 100,
        InputLayerMissing = 101,
        InvalidRoi = 110,
        InvalidParameter = 120,
        ToolPropertyMissing = 121,
        TemplateImageMissing = 130,
        TemplateImageInvalid = 131,
        ToolFactoryFailed = 200,
        ToolExecutionException = 210,
        OpenCvExecutionFailed = 220,
        StepTimeout = 300,
        StepCanceled = 301,
        ThresholdInvalidRange = 350,
        ThresholdInvalidMaxValue = 351,
        ThresholdInvalidAdaptiveBlockSize = 352,
        MorphologyInvalidKernel = 360,
        MorphologyInvalidIterations = 361,
        FilterInvalidKernel = 370,
        FilterInvalidSigma = 371,
        EdgeDetectionInvalidThreshold = 380,
        EdgeDetectionInvalidKernel = 381,
        EdgeDetectionInvalidDerivative = 382,
        ContourInvalidAreaRange = 400,
        ContourRoiInvalid = 401,
        ContourInvalidAdaptiveBlockSize = 402,
        ContourNoResult = 403,
        BlobInvalidAreaRange = 500,
        BlobRoiInvalid = 501,
        BlobLabelingFailed = 502,
        BlobInvalidAdaptiveBlockSize = 503,
        BlobNoResult = 504,
        MatchingTemplateMissing = 600,
        MatchingTemplateInvalid = 601,
        MatchingRoiInvalid = 602,
        MatchingInvalidScale = 603,
        MatchingInvalidAngleStep = 604,
        MatchingInvalidAdaptiveBlockSize = 605,
        MatchingNoResult = 606,
        MatchingAmbiguous = 607,
        LineGaugeRoiInvalid = 700,
        LineGaugeInvalidSampling = 701,
        LineGaugeInvalidAdaptiveBlockSize = 702,
        LineGaugeEdgeNotFound = 703,
        LineGaugeFitFailed = 704,
        MeanRoiInvalid = 800,
        MeanInvalidAdaptiveBlockSize = 801,
        CornerRoiInvalid = 850,
        CornerInvalidAdaptiveBlockSize = 851,
        CornerNoResult = 852,
        FeatureTemplateMissing = 900,
        FeatureRoiInvalid = 901,
        FeatureTemplateInvalid = 902,
        FeatureInvalidAdaptiveBlockSize = 903,
        FeatureNoKeypoints = 904,
        FeatureNotEnoughMatches = 905,
        FeatureHomographyFailed = 906,
        FeatureNoResult = 907,
        RotateScaleInvalidScale = 1000,
        AffineInvalidPoint = 1010,
        AffineDegenerateSource = 1011,
        AffineDegenerateDestination = 1012,
        AffineInvalidOutputSize = 1013,
        AffineInvalidSampling = 1014,
        AffineInvalidGate = 1015,
        AffineInsufficientCoverage = 1016,
        AutoMPointInvalidRoi = 1020,
        AutoMPointInvalidPatternSize = 1021,
        AutoMPointInvalidParameter = 1022,
        AutoMPointNoCandidate = 1023,
        AutoMPointRepresentativeImageInvalid = 1024
    }

    public enum VisionToolResultStatus
    {
        Passed,
        Failed,
        InvalidInput,
        InvalidParameter,
        InvalidRoi,
        ConfigurationError,
        Timeout,
        Canceled,
        Exception
    }

    /// <summary>
    /// Owns ResultImage. Dispose the result after its image is no longer needed.
    /// </summary>
    public class VisionToolResult : IDisposable
    {
        /// <summary>Tool success; application or pipeline acceptance may impose additional limits.</summary>
        public bool Success { get; set; }
        /// <summary>Diagnostic text for people. Use ErrorCode for programmatic classification.</summary>
        public string Message { get; set; } = string.Empty;
        /// <summary>Owned image snapshot, possibly null on failure. Clone before retaining it beyond this result's lifetime.</summary>
        public Mat ResultImage { get; set; }
        public TimeSpan Elapsed { get; set; }
        /// <summary>Captured execution exception when present; ordinary validation or no-result failures need not have one.</summary>
        public Exception Exception { get; set; }
        public VisionToolErrorCode ErrorCode { get; set; } = VisionToolErrorCode.None;
        public VisionToolResultStatus ResultStatus { get; set; } = VisionToolResultStatus.Passed;
        public string ResultStatusName => ResultStatus.ToString();
        public int ErrorCodeValue => (int)ErrorCode;
        public string ErrorName => ErrorCode.ToString();
        public bool HasError => ErrorCode != VisionToolErrorCode.None;
        /// <summary>Case-insensitive, tool-specific measurements. Missing keys are not zero; use TryGetValue.</summary>
        public Dictionary<string, double> Metrics { get; } = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
        public List<VisionToolOverlay> Overlays { get; } = new List<VisionToolOverlay>();
        public EdgeBasedMatchingDiagnosticEvidence EdgeBasedMatchingDiagnostics { get; set; }

        public static VisionToolResult Passed(
            Mat resultImage,
            TimeSpan elapsed,
            IDictionary<string, double> metrics = null,
            IEnumerable<VisionToolOverlay> overlays = null)
        {
            VisionToolResult result = new VisionToolResult
            {
                Success = true,
                ResultImage = resultImage,
                Elapsed = elapsed,
                ResultStatus = VisionToolResultStatus.Passed
            };

            if (metrics != null)
            {
                foreach (KeyValuePair<string, double> metric in metrics)
                {
                    if (string.IsNullOrWhiteSpace(metric.Key)) { continue; }
                    result.Metrics[metric.Key] = metric.Value;
                }
            }

            if (overlays != null)
            {
                result.Overlays.AddRange(overlays.Where(overlay => overlay != null));
            }

            return result;
        }

        public static VisionToolResult Failed(string message, TimeSpan elapsed, Exception exception = null)
        {
            return Failed(VisionToolErrorCode.Unknown, message, elapsed, exception);
        }

        public static VisionToolResult Failed(
            VisionToolErrorCode errorCode,
            string message,
            TimeSpan elapsed,
            Exception exception = null)
        {
            VisionToolErrorCode resolvedErrorCode = errorCode == VisionToolErrorCode.None
                ? VisionToolErrorCode.Unknown
                : errorCode;

            return new VisionToolResult
            {
                Success = false,
                Message = message ?? string.Empty,
                Elapsed = elapsed,
                Exception = exception,
                ErrorCode = resolvedErrorCode,
                ResultStatus = ResolveStatus(resolvedErrorCode)
            };
        }

        public static VisionToolResultStatus ResolveStatus(VisionToolErrorCode errorCode)
        {
            switch (errorCode)
            {
                case VisionToolErrorCode.None:
                    return VisionToolResultStatus.Passed;
                case VisionToolErrorCode.InputImageInvalid:
                case VisionToolErrorCode.InputLayerMissing:
                case VisionToolErrorCode.AutoMPointRepresentativeImageInvalid:
                    return VisionToolResultStatus.InvalidInput;
                case VisionToolErrorCode.InvalidRoi:
                case VisionToolErrorCode.ContourRoiInvalid:
                case VisionToolErrorCode.BlobRoiInvalid:
                case VisionToolErrorCode.MatchingRoiInvalid:
                case VisionToolErrorCode.LineGaugeRoiInvalid:
                case VisionToolErrorCode.MeanRoiInvalid:
                case VisionToolErrorCode.CornerRoiInvalid:
                case VisionToolErrorCode.FeatureRoiInvalid:
                case VisionToolErrorCode.AutoMPointInvalidRoi:
                    return VisionToolResultStatus.InvalidRoi;
                case VisionToolErrorCode.InvalidParameter:
                case VisionToolErrorCode.ThresholdInvalidRange:
                case VisionToolErrorCode.ThresholdInvalidMaxValue:
                case VisionToolErrorCode.ThresholdInvalidAdaptiveBlockSize:
                case VisionToolErrorCode.MorphologyInvalidKernel:
                case VisionToolErrorCode.MorphologyInvalidIterations:
                case VisionToolErrorCode.FilterInvalidKernel:
                case VisionToolErrorCode.FilterInvalidSigma:
                case VisionToolErrorCode.EdgeDetectionInvalidThreshold:
                case VisionToolErrorCode.EdgeDetectionInvalidKernel:
                case VisionToolErrorCode.EdgeDetectionInvalidDerivative:
                case VisionToolErrorCode.ContourInvalidAreaRange:
                case VisionToolErrorCode.ContourInvalidAdaptiveBlockSize:
                case VisionToolErrorCode.BlobInvalidAreaRange:
                case VisionToolErrorCode.BlobInvalidAdaptiveBlockSize:
                case VisionToolErrorCode.MatchingInvalidScale:
                case VisionToolErrorCode.MatchingInvalidAngleStep:
                case VisionToolErrorCode.MatchingInvalidAdaptiveBlockSize:
                case VisionToolErrorCode.LineGaugeInvalidSampling:
                case VisionToolErrorCode.LineGaugeInvalidAdaptiveBlockSize:
                case VisionToolErrorCode.MeanInvalidAdaptiveBlockSize:
                case VisionToolErrorCode.CornerInvalidAdaptiveBlockSize:
                case VisionToolErrorCode.FeatureInvalidAdaptiveBlockSize:
                case VisionToolErrorCode.RotateScaleInvalidScale:
                case VisionToolErrorCode.AffineInvalidPoint:
                case VisionToolErrorCode.AffineDegenerateSource:
                case VisionToolErrorCode.AffineDegenerateDestination:
                case VisionToolErrorCode.AffineInvalidOutputSize:
                case VisionToolErrorCode.AffineInvalidSampling:
                case VisionToolErrorCode.AffineInvalidGate:
                case VisionToolErrorCode.AutoMPointInvalidPatternSize:
                case VisionToolErrorCode.AutoMPointInvalidParameter:
                    return VisionToolResultStatus.InvalidParameter;
                case VisionToolErrorCode.ToolPropertyMissing:
                case VisionToolErrorCode.TemplateImageMissing:
                case VisionToolErrorCode.TemplateImageInvalid:
                case VisionToolErrorCode.ToolFactoryFailed:
                case VisionToolErrorCode.MatchingTemplateMissing:
                case VisionToolErrorCode.MatchingTemplateInvalid:
                case VisionToolErrorCode.FeatureTemplateMissing:
                case VisionToolErrorCode.FeatureTemplateInvalid:
                    return VisionToolResultStatus.ConfigurationError;
                case VisionToolErrorCode.StepTimeout:
                    return VisionToolResultStatus.Timeout;
                case VisionToolErrorCode.StepCanceled:
                    return VisionToolResultStatus.Canceled;
                case VisionToolErrorCode.ToolExecutionException:
                case VisionToolErrorCode.OpenCvExecutionFailed:
                case VisionToolErrorCode.BlobLabelingFailed:
                    return VisionToolResultStatus.Exception;
                default:
                    return VisionToolResultStatus.Failed;
            }
        }

        public void Dispose()
        {
            ResultImage?.Dispose();
            ResultImage = null;
            GC.SuppressFinalize(this);
        }
    }
}
