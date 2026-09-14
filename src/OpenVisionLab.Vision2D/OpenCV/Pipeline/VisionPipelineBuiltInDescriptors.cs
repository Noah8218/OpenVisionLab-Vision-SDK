using OpenVisionLab.Vision2D.Property;
using OpenVisionLab.Vision2D.Tool;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Drawing;
using System.Globalization;
using System.Linq;
using static OpenVisionLab.Vision2D.Pipeline.VisionPipelineDescriptorParameter;

namespace OpenVisionLab.Vision2D.Pipeline
{
    internal static class VisionPipelineBuiltInDescriptors
    {
        private const string PackageId = "OpenVisionLab.Vision2D";

        public static readonly IReadOnlyList<VisionPipelineToolDescriptor> All =
            new ReadOnlyCollection<VisionPipelineToolDescriptor>(new[]
            {
                CreateThresholdDescriptor(),
                CreateMorphologyDescriptor(),
                CreateFilterDescriptor(),
                CreateEdgeDetectionDescriptor(),
                CreateRotateScaleDescriptor(),
                CreateAffineTransformDescriptor(),
                CreateContourDescriptor(),
                CreateCornerDescriptor(),
                CreateMatchingDescriptor(),
                CreateEdgeBasedMatchingDescriptor(),
                CreateAutoMPointDescriptor(),
                CreateSiftDescriptor(),
                CreateLineGaugeDescriptor(),
                CreateMeanDescriptor()
            });

        private static VisionPipelineToolDescriptor CreateThresholdDescriptor()
        {
            ThresholdToolProperty value = new ThresholdToolProperty();
            return Tool(
                "threshold",
                typeof(ThresholdTool),
                typeof(ThresholdToolProperty),
                new List<string> { "ThresholdTool" },
                new[]
                {
                    Enum(nameof(value.Mode), value.Mode),
                    Number(nameof(value.Threshold), value.Threshold),
                    Number(nameof(value.MaxValue), value.MaxValue),
                    Enum(nameof(value.ThresholdType), value.ThresholdType),
                    Integer(nameof(value.RangeMin), value.RangeMin),
                    Integer(nameof(value.RangeMax), value.RangeMax),
                    Boolean(nameof(value.Invert), value.Invert),
                    Enum(nameof(value.AdaptiveType), value.AdaptiveType),
                    Enum(nameof(value.AdaptiveThresholdType), value.AdaptiveThresholdType),
                    Integer(nameof(value.BlockSize), value.BlockSize),
                    Integer(nameof(value.Weight), value.Weight)
                });
        }

        private static VisionPipelineToolDescriptor CreateMorphologyDescriptor()
        {
            MorphologyToolProperty value = new MorphologyToolProperty();
            return Tool(
                "morphology",
                typeof(MorphologyTool),
                typeof(MorphologyToolProperty),
                new List<string> { "MorphologyTool" },
                new[]
                {
                    Enum(nameof(value.Shape), value.Shape),
                    Enum(nameof(value.Operator), value.Operator),
                    Integer(nameof(value.KernelWidth), value.KernelWidth),
                    Integer(nameof(value.KernelHeight), value.KernelHeight),
                    Integer(nameof(value.Iterations), value.Iterations)
                });
        }

        private static VisionPipelineToolDescriptor CreateFilterDescriptor()
        {
            FilterToolProperty value = new FilterToolProperty();
            return Tool(
                "filter",
                typeof(FilterTool),
                typeof(FilterToolProperty),
                new List<string> { "FilterTool" },
                new[]
                {
                    Enum(nameof(value.FilterType), value.FilterType),
                    Integer(nameof(value.KernelWidth), value.KernelWidth),
                    Integer(nameof(value.KernelHeight), value.KernelHeight),
                    Integer(nameof(value.MedianKernelSize), value.MedianKernelSize),
                    Integer(nameof(value.Diameter), value.Diameter),
                    Integer(nameof(value.SigmaColor), value.SigmaColor),
                    Integer(nameof(value.SigmaSpace), value.SigmaSpace),
                    Enum(nameof(value.BorderType), value.BorderType)
                });
        }

        private static VisionPipelineToolDescriptor CreateEdgeDetectionDescriptor()
        {
            EdgeDetectionToolProperty value = new EdgeDetectionToolProperty();
            return Tool(
                "edgeDetection",
                typeof(EdgeDetectionTool),
                typeof(EdgeDetectionToolProperty),
                new List<string> { "edge", "EdgeDetectionTool" },
                new[]
                {
                    Enum(nameof(value.EdgeType), value.EdgeType),
                    Integer(nameof(value.CannyThresholdLow), value.CannyThresholdLow),
                    Integer(nameof(value.CannyThresholdHigh), value.CannyThresholdHigh),
                    Integer(nameof(value.CannyApertureSize), value.CannyApertureSize),
                    Boolean(nameof(value.UseL2Gradient), value.UseL2Gradient),
                    Integer(nameof(value.SobelDegreeX), value.SobelDegreeX),
                    Integer(nameof(value.SobelDegreeY), value.SobelDegreeY),
                    Integer(nameof(value.SobelKernelSize), value.SobelKernelSize),
                    Integer(nameof(value.ScharrDegreeX), value.ScharrDegreeX),
                    Integer(nameof(value.ScharrDegreeY), value.ScharrDegreeY),
                    Integer(nameof(value.LaplacianKernelSize), value.LaplacianKernelSize)
                });
        }

        private static VisionPipelineToolDescriptor CreateRotateScaleDescriptor()
        {
            RotateScaleToolProperty value = new RotateScaleToolProperty();
            return Tool(
                "rotateScale",
                typeof(RotateScaleTool),
                typeof(RotateScaleToolProperty),
                new List<string> { "rotateAndScale", "RotateScaleTool" },
                new[]
                {
                    Number(nameof(value.Angle), value.Angle),
                    Number(nameof(value.ScaleXPercent), value.ScaleXPercent),
                    Number(nameof(value.ScaleYPercent), value.ScaleYPercent),
                    Enum(nameof(value.Interpolation), value.Interpolation),
                    Enum(nameof(value.BorderType), value.BorderType)
                });
        }

        private static VisionPipelineToolDescriptor CreateAffineTransformDescriptor()
        {
            AffineTransformToolProperty value = new AffineTransformToolProperty();
            return Tool(
                "affineTransform",
                typeof(AffineTransformTool),
                typeof(AffineTransformToolProperty),
                new List<string> { "affine", "affineMatrix", "AffineTransformTool" },
                new[]
                {
                    Number(nameof(value.SourcePoint1X), value.SourcePoint1X),
                    Number(nameof(value.SourcePoint1Y), value.SourcePoint1Y),
                    Number(nameof(value.SourcePoint2X), value.SourcePoint2X),
                    Number(nameof(value.SourcePoint2Y), value.SourcePoint2Y),
                    Number(nameof(value.SourcePoint3X), value.SourcePoint3X),
                    Number(nameof(value.SourcePoint3Y), value.SourcePoint3Y),
                    Number(nameof(value.DestinationPoint1X), value.DestinationPoint1X),
                    Number(nameof(value.DestinationPoint1Y), value.DestinationPoint1Y),
                    Number(nameof(value.DestinationPoint2X), value.DestinationPoint2X),
                    Number(nameof(value.DestinationPoint2Y), value.DestinationPoint2Y),
                    Number(nameof(value.DestinationPoint3X), value.DestinationPoint3X),
                    Number(nameof(value.DestinationPoint3Y), value.DestinationPoint3Y),
                    Integer(nameof(value.OutputWidth), value.OutputWidth),
                    Integer(nameof(value.OutputHeight), value.OutputHeight),
                    Enum(nameof(value.Interpolation), value.Interpolation),
                    Enum(nameof(value.BorderType), value.BorderType),
                    Number(nameof(value.BorderValue), value.BorderValue),
                    Number(nameof(value.MinimumSourceTriangleArea), value.MinimumSourceTriangleArea),
                    Number(nameof(value.MinimumDestinationTriangleArea), value.MinimumDestinationTriangleArea),
                    Number(nameof(value.MinimumValidPixelRatio), value.MinimumValidPixelRatio)
                });
        }

        private static VisionPipelineToolDescriptor CreateContourDescriptor()
        {
            ContourToolProperty value = new ContourToolProperty();
            return Tool(
                "contour",
                typeof(ContourTool),
                typeof(ContourToolProperty),
                new List<string> { "ContourTool" },
                Common(value).Concat(new[]
                {
                    Boolean(nameof(value.USE_APPROXPOLYDP), value.USE_APPROXPOLYDP),
                    Boolean(nameof(value.USE_DRAW_IMAGE), value.USE_DRAW_IMAGE),
                    Enum(nameof(value.ApproximationModes), value.ApproximationModes),
                    Enum(nameof(value.DetectMode), value.DetectMode),
                    Number(nameof(value.EPSILON), value.EPSILON),
                    Integer(nameof(value.MIN_AREA), value.MIN_AREA),
                    Integer(nameof(value.MAX_AREA), value.MAX_AREA),
                    Integer(nameof(value.MIN_WIDTH), value.MIN_WIDTH),
                    Integer(nameof(value.MAX_WIDTH), value.MAX_WIDTH),
                    Integer(nameof(value.MIN_HEIGHT), value.MIN_HEIGHT),
                    Integer(nameof(value.MAX_HEIGHT), value.MAX_HEIGHT),
                    Color(nameof(value.DrawColor), FormatColor(value.DrawColor)),
                    Integer(nameof(value.DrawThickness), value.DrawThickness),
                    String(nameof(value.ClrGridHtml), value.ClrGridHtml)
                }));
        }

        private static VisionPipelineToolDescriptor CreateCornerDescriptor()
        {
            ContourToolProperty value = new ContourToolProperty();
            return Tool(
                "corner",
                typeof(CornerTool),
                typeof(ContourToolProperty),
                new List<string> { "CornerTool" },
                Common(value).Concat(new[]
                {
                    Boolean(nameof(value.USE_APPROXPOLYDP), value.USE_APPROXPOLYDP),
                    Boolean(nameof(value.USE_DRAW_IMAGE), value.USE_DRAW_IMAGE),
                    Enum(nameof(value.ApproximationModes), value.ApproximationModes),
                    Enum(nameof(value.DetectMode), value.DetectMode),
                    Number(nameof(value.EPSILON), value.EPSILON),
                    Integer(nameof(value.MIN_AREA), value.MIN_AREA),
                    Integer(nameof(value.MAX_AREA), value.MAX_AREA),
                    Integer(nameof(value.MIN_WIDTH), value.MIN_WIDTH),
                    Integer(nameof(value.MAX_WIDTH), value.MAX_WIDTH),
                    Integer(nameof(value.MIN_HEIGHT), value.MIN_HEIGHT),
                    Integer(nameof(value.MAX_HEIGHT), value.MAX_HEIGHT),
                    Color(nameof(value.DrawColor), FormatColor(value.DrawColor)),
                    Integer(nameof(value.DrawThickness), value.DrawThickness),
                    String(nameof(value.ClrGridHtml), value.ClrGridHtml)
                }));
        }

        private static VisionPipelineToolDescriptor CreateMatchingDescriptor()
        {
            MatchingToolProperty value = new MatchingToolProperty();
            return Tool(
                "matching",
                typeof(MatchingTool),
                typeof(MatchingToolProperty),
                new List<string> { "MatchingTool" },
                Common(value).Concat(new[]
                {
                    Enum(nameof(value.MATCH_MODE), value.MATCH_MODE),
                    Number(nameof(value.SCORE_MIN), value.SCORE_MIN),
                    Number(nameof(value.MAGNIFIATION), value.MAGNIFIATION),
                    Integer(nameof(value.NUM_MATCH), value.NUM_MATCH),
                    Boolean(nameof(value.USE_FIND_SCALE), value.USE_FIND_SCALE),
                    Number(nameof(value.FIND_SCALE_MIN), value.FIND_SCALE_MIN),
                    Number(nameof(value.FIND_SCALE_MAX), value.FIND_SCALE_MAX),
                    Number(nameof(value.FIND_SCALE_STEP), value.FIND_SCALE_STEP),
                    Boolean(nameof(value.USE_FIND_ANGLE), value.USE_FIND_ANGLE),
                    Number(nameof(value.FIND_ANGLE), value.FIND_ANGLE),
                    Integer(nameof(value.FIND_ANGLE_MAX), value.FIND_ANGLE_MAX),
                    Integer(nameof(value.FIND_ANGLE_MIN), value.FIND_ANGLE_MIN),
                    Boolean(nameof(value.USE_COARSE_TO_FINE_ANGLE_SEARCH), value.USE_COARSE_TO_FINE_ANGLE_SEARCH),
                    Number(nameof(value.COARSE_ANGLE_STEP), value.COARSE_ANGLE_STEP),
                    Integer(nameof(value.COARSE_ANGLE_TOP_K), value.COARSE_ANGLE_TOP_K),
                    Boolean(nameof(value.USE_PYRAMID_POSITION_PROPOSAL), value.USE_PYRAMID_POSITION_PROPOSAL),
                    Integer(nameof(value.PYRAMID_POSITION_TOP_N), value.PYRAMID_POSITION_TOP_N),
                    Number(nameof(value.PYRAMID_POSITION_MIN_SCORE), value.PYRAMID_POSITION_MIN_SCORE),
                    Boolean(nameof(value.USE_CANNY), value.USE_CANNY),
                    Integer(nameof(value.CANNY_HIGH), value.CANNY_HIGH),
                    Integer(nameof(value.CANNY_LOW), value.CANNY_LOW),
                    Boolean(nameof(value.USE_PADDING_COLOR_WHITE), value.USE_PADDING_COLOR_WHITE)
                }),
                TemplateArtifact());
        }

        private static VisionPipelineToolDescriptor CreateEdgeBasedMatchingDescriptor()
        {
            EdgeBasedTemplateMatchingToolProperty value = new EdgeBasedTemplateMatchingToolProperty();
            return Tool(
                "edgeBasedTemplateMatching",
                typeof(EdgeBasedTemplateMatchingTool),
                typeof(EdgeBasedTemplateMatchingToolProperty),
                new List<string> { "edgeBasedMatching", "EdgeBasedTemplateMatchingTool" },
                Common(value).Concat(new[]
                {
                    Number(nameof(value.SCORE_MIN), value.SCORE_MIN),
                    Integer(nameof(value.NUM_MATCH), value.NUM_MATCH),
                    Boolean(nameof(value.USE_UNIQUE_MATCH_VALIDATION), value.USE_UNIQUE_MATCH_VALIDATION),
                    Number(nameof(value.UNIQUE_MATCH_MIN_SCORE_MARGIN), value.UNIQUE_MATCH_MIN_SCORE_MARGIN),
                    Boolean(nameof(value.ALLOW_GLOBAL_POLARITY_REVERSAL), value.ALLOW_GLOBAL_POLARITY_REVERSAL),
                    Integer(nameof(value.CANNY_LOW), value.CANNY_LOW),
                    Integer(nameof(value.CANNY_HIGH), value.CANNY_HIGH),
                    Integer(nameof(value.CANNY_APERTURE_SIZE), value.CANNY_APERTURE_SIZE),
                    Boolean(nameof(value.USE_L2_GRADIENT), value.USE_L2_GRADIENT),
                    Enum(nameof(value.CONTOUR_RETRIEVAL_MODE), value.CONTOUR_RETRIEVAL_MODE),
                    Enum(nameof(value.CONTOUR_APPROXIMATION_MODE), value.CONTOUR_APPROXIMATION_MODE),
                    Boolean(nameof(value.USE_FIND_ANGLE), value.USE_FIND_ANGLE),
                    Number(nameof(value.FIND_ANGLE), value.FIND_ANGLE),
                    Integer(nameof(value.FIND_ANGLE_MAX), value.FIND_ANGLE_MAX),
                    Integer(nameof(value.FIND_ANGLE_MIN), value.FIND_ANGLE_MIN),
                    Boolean(nameof(value.USE_COARSE_TO_FINE_ANGLE_SEARCH), value.USE_COARSE_TO_FINE_ANGLE_SEARCH),
                    Number(nameof(value.COARSE_ANGLE_STEP), value.COARSE_ANGLE_STEP),
                    Integer(nameof(value.COARSE_ANGLE_TOP_K), value.COARSE_ANGLE_TOP_K),
                    Boolean(nameof(value.USE_FIND_SCALE), value.USE_FIND_SCALE),
                    Number(nameof(value.FIND_SCALE_MIN), value.FIND_SCALE_MIN),
                    Number(nameof(value.FIND_SCALE_MAX), value.FIND_SCALE_MAX),
                    Number(nameof(value.FIND_SCALE_STEP), value.FIND_SCALE_STEP),
                    Number(nameof(value.GREEDINESS), value.GREEDINESS),
                    Integer(nameof(value.SEARCH_STEP), value.SEARCH_STEP),
                    Boolean(nameof(value.USE_POSITION_REFINE), value.USE_POSITION_REFINE),
                    Boolean(nameof(value.USE_SUBPIXEL_REFINE), value.USE_SUBPIXEL_REFINE),
                    Boolean(nameof(value.USE_PYRAMID_POSITION_PROPOSAL), value.USE_PYRAMID_POSITION_PROPOSAL),
                    Integer(nameof(value.PYRAMID_POSITION_TOP_N), value.PYRAMID_POSITION_TOP_N),
                    Number(nameof(value.PYRAMID_POSITION_MIN_SCORE), value.PYRAMID_POSITION_MIN_SCORE),
                    Boolean(nameof(value.USE_HYBRID_VERIFY), value.USE_HYBRID_VERIFY),
                    Integer(nameof(value.HYBRID_VERIFY_TOP_N), value.HYBRID_VERIFY_TOP_N),
                    Number(nameof(value.HYBRID_VERIFY_IMAGE_WEIGHT), value.HYBRID_VERIFY_IMAGE_WEIGHT),
                    Integer(nameof(value.MAX_TEMPLATE_POINTS), value.MAX_TEMPLATE_POINTS),
                    Number(nameof(value.MIN_GRADIENT_MAGNITUDE), value.MIN_GRADIENT_MAGNITUDE),
                    Boolean(nameof(value.USE_DRAW_IMAGE), value.USE_DRAW_IMAGE)
                }),
                TemplateArtifact());
        }

        private static VisionPipelineToolDescriptor CreateAutoMPointDescriptor()
        {
            AutoMPointToolProperty value = new AutoMPointToolProperty();
            return Tool(
                "autoMPoint",
                typeof(AutoMPointTool),
                typeof(AutoMPointToolProperty),
                new List<string> { "AutoMPointTool" },
                new[]
                {
                    Boolean(nameof(value.UseAnalysisRoi), value.UseAnalysisRoi),
                    Rectangle(nameof(value.AnalysisRoi), FormatRectangle(value.AnalysisRoi)),
                    Enum(nameof(value.CandidateMode), value.CandidateMode),
                    Integer(nameof(value.PatternWidth), value.PatternWidth),
                    Integer(nameof(value.PatternHeight), value.PatternHeight),
                    Integer(nameof(value.CandidateStride), value.CandidateStride),
                    Integer(nameof(value.MaximumFinalists), value.MaximumFinalists),
                    Integer(nameof(value.MaximumResults), value.MaximumResults),
                    Number(nameof(value.MaximumCandidateOverlap), value.MaximumCandidateOverlap),
                    Number(nameof(value.MinimumContrastStdDev), value.MinimumContrastStdDev),
                    Number(nameof(value.MinimumEdgeDensity), value.MinimumEdgeDensity),
                    Number(nameof(value.MinimumQuadrantBalance), value.MinimumQuadrantBalance),
                    Number(nameof(value.MinimumOrientationBalance), value.MinimumOrientationBalance),
                    Number(nameof(value.MinimumFeatureQuality), value.MinimumFeatureQuality),
                    Integer(nameof(value.CannyLow), value.CannyLow),
                    Integer(nameof(value.CannyHigh), value.CannyHigh),
                    Number(nameof(value.MatchingMinimumScore), value.MatchingMinimumScore),
                    Number(nameof(value.MinimumUniquenessMargin), value.MinimumUniquenessMargin),
                    Integer(nameof(value.MaximumTemplatePoints), value.MaximumTemplatePoints),
                    Integer(nameof(value.SearchStep), value.SearchStep),
                    Boolean(nameof(value.UsePositionRefine), value.UsePositionRefine),
                    Boolean(nameof(value.UseSubpixelRefine), value.UseSubpixelRefine),
                    Boolean(nameof(value.UsePyramidPositionProposal), value.UsePyramidPositionProposal),
                    Boolean(nameof(value.UseHybridVerify), value.UseHybridVerify),
                    Boolean(nameof(value.UseAngleSearch), value.UseAngleSearch),
                    Integer(nameof(value.AngleMinimum), value.AngleMinimum),
                    Integer(nameof(value.AngleMaximum), value.AngleMaximum),
                    Number(nameof(value.AngleStep), value.AngleStep),
                    Boolean(nameof(value.UseScaleSearch), value.UseScaleSearch),
                    Number(nameof(value.ScaleMinimum), value.ScaleMinimum),
                    Number(nameof(value.ScaleMaximum), value.ScaleMaximum),
                    Number(nameof(value.ScaleStep), value.ScaleStep),
                    Integer(nameof(value.SyntheticTranslationPixels), value.SyntheticTranslationPixels),
                    Number(nameof(value.SyntheticRotationDegrees), value.SyntheticRotationDegrees),
                    Number(nameof(value.SyntheticScaleRatio), value.SyntheticScaleRatio),
                    Number(nameof(value.MinimumSyntheticSuccessRate), value.MinimumSyntheticSuccessRate),
                    Number(nameof(value.MaximumPositionErrorPixels), value.MaximumPositionErrorPixels),
                    Number(nameof(value.MaximumAngleErrorDegrees), value.MaximumAngleErrorDegrees),
                    Number(nameof(value.MaximumScaleErrorRatio), value.MaximumScaleErrorRatio),
                    Number(nameof(value.MaximumRuntimeMilliseconds), value.MaximumRuntimeMilliseconds),
                    Integer(nameof(value.MinimumRepresentativeImageCount), value.MinimumRepresentativeImageCount),
                    Number(nameof(value.MinimumRepresentativeSuccessRate), value.MinimumRepresentativeSuccessRate)
                });
        }

        private static VisionPipelineToolDescriptor CreateSiftDescriptor()
        {
            SiftToolProperty value = new SiftToolProperty();
            return Tool(
                "sift",
                typeof(SiftTool),
                typeof(SiftToolProperty),
                new List<string> { "SiftTool" },
                Common(value).Concat(new[]
                {
                    Number(nameof(value.RANSAC_REPROJ_THRESHOLD), value.RANSAC_REPROJ_THRESHOLD),
                    Number(nameof(value.SCORE_MIN), value.SCORE_MIN)
                }),
                TemplateArtifact());
        }

        private static VisionPipelineToolDescriptor CreateLineGaugeDescriptor()
        {
            LineGaugeToolProperty value = new LineGaugeToolProperty();
            IEnumerable<VisionPipelineParameterDescriptor> common = Common(value)
                .Select(parameter => parameter.Name == nameof(value.CvROI)
                    ? Rectangle(parameter.Name, parameter.DefaultValue, true)
                    : parameter);
            return Tool(
                "lineGauge",
                typeof(LineGaugeTool),
                typeof(LineGaugeToolProperty),
                new List<string> { "lineGuage", "LineGaugeTool" },
                common.Concat(new[]
                {
                    Enum(nameof(value.PRJ_PORALITY), value.PRJ_PORALITY),
                    Enum(nameof(value.PRJ_DIR), value.PRJ_DIR),
                    Number(nameof(value.CONTRAST), value.CONTRAST),
                    Number(nameof(value.THICKNESS), value.THICKNESS),
                    Number(nameof(value.SAMPLING_STEP), value.SAMPLING_STEP),
                    Enum(nameof(value.VER_PRJ_DIR), value.VER_PRJ_DIR),
                    Integer(nameof(value.POINT_RANGE), value.POINT_RANGE),
                    Boolean(nameof(value.USE_MANUAL_ANGLE), value.USE_MANUAL_ANGLE),
                    Number(nameof(value.MANUAL_ANGLE_VALUE), value.MANUAL_ANGLE_VALUE),
                    Boolean(nameof(value.USE_EXTEND_FIT_LINE), value.USE_EXTEND_FIT_LINE),
                    Integer(nameof(value.EXTEND_FIT_LINE_VALUE), value.EXTEND_FIT_LINE_VALUE),
                    Boolean(nameof(value.SHOW_VERTICAL_LINE), value.SHOW_VERTICAL_LINE),
                    Boolean(nameof(value.SHOW_EDGE), value.SHOW_EDGE),
                    Boolean(nameof(value.SHOW_CONTOUR), value.SHOW_CONTOUR),
                    Boolean(nameof(value.SHOW_FITLINE), value.SHOW_FITLINE)
                }));
        }

        private static VisionPipelineToolDescriptor CreateMeanDescriptor()
        {
            MeanToolProperty value = new MeanToolProperty();
            return Tool(
                "mean",
                typeof(MeanTool),
                typeof(MeanToolProperty),
                new List<string> { "MeanTool" },
                Common(value).Concat(new[]
                {
                    Integer(nameof(value.MEAN_MAX), value.MEAN_MAX),
                    Integer(nameof(value.MEAN_MIN), value.MEAN_MIN),
                    Enum(nameof(value.MEAN_TYPES), value.MEAN_TYPES)
                }));
        }

        private static VisionPipelineToolDescriptor Tool(
            string toolType,
            Type toolTypeValue,
            Type propertyType,
            IEnumerable<string> aliases,
            IEnumerable<VisionPipelineParameterDescriptor> parameters,
            params VisionPipelineArtifactDescriptor[] artifacts)
        {
            return new VisionPipelineToolDescriptor(
                toolType,
                toolTypeValue.FullName,
                propertyType.FullName,
                PackageId,
                aliases,
                parameters,
                artifacts);
        }

        private static VisionPipelineParameterDescriptor[] Common(OpenCvToolPropertyBase value)
        {
            return new[]
            {
                String(nameof(value.NAME), value.NAME),
                Number(nameof(value.PIXELPERMM), value.PIXELPERMM),
                Boolean(nameof(value.USE_THRESHOLD), value.USE_THRESHOLD),
                Boolean(nameof(value.USE_BITWISENOT), value.USE_BITWISENOT),
                Enum(nameof(value.THRESHOLD_TYPES), value.THRESHOLD_TYPES),
                Number(nameof(value.THRESHOLD), value.THRESHOLD),
                Boolean(nameof(value.USE_ADAPTIVE_THRESHOLD), value.USE_ADAPTIVE_THRESHOLD),
                Number(nameof(value.ADAPTIVE_THRESHOLD), value.ADAPTIVE_THRESHOLD),
                Enum(nameof(value.ADAPTIVE_THRESHOLD_TYPES), value.ADAPTIVE_THRESHOLD_TYPES),
                Enum(nameof(value.ADAPTIVE_THRESHOLD_ALGORITHM), value.ADAPTIVE_THRESHOLD_ALGORITHM),
                Integer(nameof(value.BlockSize), value.BlockSize),
                Integer(nameof(value.Weight), value.Weight),
                Boolean(nameof(value.USE_ROI), value.USE_ROI),
                Boolean(nameof(value.USE_MULTI_ROI), value.USE_MULTI_ROI),
                Rectangle(nameof(value.CvROI), FormatRectangle(value.CvROI)),
                RectangleList(nameof(value.CvROIS)),
                RectangleList(nameof(value.CvMASKS))
            };
        }

        private static VisionPipelineArtifactDescriptor TemplateArtifact()
        {
            return new VisionPipelineArtifactDescriptor(
                VisionPipelineToolFactory.TemplateArtifactRole,
                VisionPipelineToolFactory.EncodedImageArtifactFormat,
                VisionPipelineToolFactory.EncodedImageArtifactFormatVersion,
                true);
        }

        private static string FormatRectangle(OpenCvSharp.Rect value)
        {
            return string.Join(",", new[]
            {
                value.X.ToString(CultureInfo.InvariantCulture),
                value.Y.ToString(CultureInfo.InvariantCulture),
                value.Width.ToString(CultureInfo.InvariantCulture),
                value.Height.ToString(CultureInfo.InvariantCulture)
            });
        }

        private static string FormatColor(Color value)
        {
            return value.A == byte.MaxValue
                ? $"#{value.R:X2}{value.G:X2}{value.B:X2}"
                : $"#{value.A:X2}{value.R:X2}{value.G:X2}{value.B:X2}";
        }
    }
}
