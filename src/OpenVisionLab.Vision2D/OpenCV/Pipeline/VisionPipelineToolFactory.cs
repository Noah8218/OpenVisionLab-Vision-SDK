using OpenVisionLab.Vision2D.Property;
using OpenVisionLab.Vision2D.Tool;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;

namespace OpenVisionLab.Vision2D.Pipeline
{
    /// <summary>Creates every Tool owned by OpenVisionLab.Vision2D from validated Pipeline data.</summary>
    public static class VisionPipelineToolFactory
    {
        public const string TemplateArtifactRole = "template";
        public const string EncodedImageArtifactFormat = "encoded-image";
        public const int EncodedImageArtifactFormatVersion = 1;

        /// <summary>Gets the immutable descriptors for all Tools owned by this package.</summary>
        public static IReadOnlyList<VisionPipelineToolDescriptor> Descriptors => VisionPipelineBuiltInDescriptors.All;

        /// <summary>Finds a descriptor by canonical Tool type or documented alias.</summary>
        public static bool TryGetDescriptor(string toolType, out VisionPipelineToolDescriptor descriptor)
        {
            string normalized = NormalizeToolType(toolType);
            foreach (VisionPipelineToolDescriptor candidate in Descriptors)
            {
                if (string.Equals(NormalizeToolType(candidate.ToolType), normalized, StringComparison.Ordinal))
                {
                    descriptor = candidate;
                    return true;
                }

                foreach (string alias in candidate.Aliases)
                {
                    if (string.Equals(NormalizeToolType(alias), normalized, StringComparison.Ordinal))
                    {
                        descriptor = candidate;
                        return true;
                    }
                }
            }

            descriptor = null;
            return false;
        }

        /// <summary>
        /// Creates a Tool that does not need an external artifact. Model-backed Tools require the resolver overload.
        /// </summary>
        public static IVisionTool Create(VisionPipelineStep step)
        {
            return Create(step, null);
        }

        /// <summary>
        /// Creates a Tool and resolves any required model from host-owned encoded bytes. The factory validates the
        /// artifact metadata and SHA-256 before decoding, clones the decoded image into the Tool, and releases its
        /// temporary Mat. The returned Tool is caller-owned.
        /// </summary>
        public static IVisionTool Create(
            VisionPipelineStep step,
            Func<VisionPipelineArtifactReference, byte[]> artifactResolver)
        {
            if (step == null)
            {
                throw new ArgumentNullException(nameof(step));
            }

            if (!TryGetDescriptor(step.ToolType, out VisionPipelineToolDescriptor descriptor))
            {
                throw new NotSupportedException($"Unsupported vision tool type '{step.ToolType}'.");
            }

            VisionPipelineArtifactReference templateArtifact = ValidateArtifacts(step, descriptor, artifactResolver);
            VisionPipelineParameterReader parameters = new VisionPipelineParameterReader(step.Parameters, descriptor);

            switch (NormalizeToolType(descriptor.ToolType))
            {
                case "threshold":
                    return CreateThresholdTool(parameters);
                case "morphology":
                    return CreateMorphologyTool(parameters);
                case "filter":
                    return CreateFilterTool(parameters);
                case "edgedetection":
                    return CreateEdgeDetectionTool(parameters);
                case "rotatescale":
                    return CreateRotateScaleTool(parameters);
                case "affinetransform":
                    return CreateAffineTransformTool(parameters);
                case "contour":
                    return CreateContourTool(parameters);
                case "corner":
                    return CreateCornerTool(parameters);
                case "matching":
                    return CreateMatchingTool(parameters, templateArtifact, artifactResolver);
                case "edgebasedtemplatematching":
                    return CreateEdgeBasedTemplateMatchingTool(parameters, templateArtifact, artifactResolver);
                case "autompoint":
                    return CreateAutoMPointTool(parameters);
                case "sift":
                    return CreateSiftTool(parameters, templateArtifact, artifactResolver);
                case "linegauge":
                    return CreateLineGaugeTool(parameters);
                case "mean":
                    return CreateMeanTool(parameters);
                default:
                    throw new NotSupportedException($"Unsupported vision tool type '{step.ToolType}'.");
            }
        }

        internal static string NormalizeToolType(string toolType)
        {
            string value = (toolType ?? string.Empty).Trim();
            if (value.EndsWith("Tool", StringComparison.OrdinalIgnoreCase))
            {
                value = value.Substring(0, value.Length - 4);
            }

            return value.Replace(" ", string.Empty).Replace("_", string.Empty).Replace("-", string.Empty).ToLowerInvariant();
        }

        private static ThresholdTool CreateThresholdTool(VisionPipelineParameterReader parameters)
        {
            ThresholdToolProperty property = new ThresholdToolProperty();
            property.Mode = parameters.GetEnum(nameof(property.Mode), property.Mode);
            property.Threshold = parameters.GetDouble(nameof(property.Threshold), property.Threshold);
            property.MaxValue = parameters.GetDouble(nameof(property.MaxValue), property.MaxValue);
            property.ThresholdType = parameters.GetEnum(nameof(property.ThresholdType), property.ThresholdType);
            property.RangeMin = parameters.GetInt(nameof(property.RangeMin), property.RangeMin);
            property.RangeMax = parameters.GetInt(nameof(property.RangeMax), property.RangeMax);
            property.Invert = parameters.GetBool(nameof(property.Invert), property.Invert);
            property.AdaptiveType = parameters.GetEnum(nameof(property.AdaptiveType), property.AdaptiveType);
            property.AdaptiveThresholdType = parameters.GetEnum(nameof(property.AdaptiveThresholdType), property.AdaptiveThresholdType);
            property.BlockSize = parameters.GetInt(nameof(property.BlockSize), property.BlockSize);
            property.Weight = parameters.GetInt(nameof(property.Weight), property.Weight);

            ThresholdTool tool = new ThresholdTool();
            tool.SetProperty(property);
            return tool;
        }

        private static MorphologyTool CreateMorphologyTool(VisionPipelineParameterReader parameters)
        {
            MorphologyToolProperty property = new MorphologyToolProperty();
            property.Shape = parameters.GetEnum(nameof(property.Shape), property.Shape);
            property.Operator = parameters.GetEnum(nameof(property.Operator), property.Operator);
            property.KernelWidth = parameters.GetInt(nameof(property.KernelWidth), property.KernelWidth);
            property.KernelHeight = parameters.GetInt(nameof(property.KernelHeight), property.KernelHeight);
            property.Iterations = parameters.GetInt(nameof(property.Iterations), property.Iterations);

            MorphologyTool tool = new MorphologyTool();
            tool.SetProperty(property);
            return tool;
        }

        private static FilterTool CreateFilterTool(VisionPipelineParameterReader parameters)
        {
            FilterToolProperty property = new FilterToolProperty();
            property.FilterType = parameters.GetEnum(nameof(property.FilterType), property.FilterType);
            property.KernelWidth = parameters.GetInt(nameof(property.KernelWidth), property.KernelWidth);
            property.KernelHeight = parameters.GetInt(nameof(property.KernelHeight), property.KernelHeight);
            property.MedianKernelSize = parameters.GetInt(nameof(property.MedianKernelSize), property.MedianKernelSize);
            property.Diameter = parameters.GetInt(nameof(property.Diameter), property.Diameter);
            property.SigmaColor = parameters.GetInt(nameof(property.SigmaColor), property.SigmaColor);
            property.SigmaSpace = parameters.GetInt(nameof(property.SigmaSpace), property.SigmaSpace);
            property.BorderType = parameters.GetEnum(nameof(property.BorderType), property.BorderType);

            FilterTool tool = new FilterTool();
            tool.SetProperty(property);
            return tool;
        }

        private static EdgeDetectionTool CreateEdgeDetectionTool(VisionPipelineParameterReader parameters)
        {
            EdgeDetectionToolProperty property = new EdgeDetectionToolProperty();
            property.EdgeType = parameters.GetEnum(nameof(property.EdgeType), property.EdgeType);
            property.CannyThresholdLow = parameters.GetInt(nameof(property.CannyThresholdLow), property.CannyThresholdLow);
            property.CannyThresholdHigh = parameters.GetInt(nameof(property.CannyThresholdHigh), property.CannyThresholdHigh);
            property.CannyApertureSize = parameters.GetInt(nameof(property.CannyApertureSize), property.CannyApertureSize);
            property.UseL2Gradient = parameters.GetBool(nameof(property.UseL2Gradient), property.UseL2Gradient);
            property.SobelDegreeX = parameters.GetInt(nameof(property.SobelDegreeX), property.SobelDegreeX);
            property.SobelDegreeY = parameters.GetInt(nameof(property.SobelDegreeY), property.SobelDegreeY);
            property.SobelKernelSize = parameters.GetInt(nameof(property.SobelKernelSize), property.SobelKernelSize);
            property.ScharrDegreeX = parameters.GetInt(nameof(property.ScharrDegreeX), property.ScharrDegreeX);
            property.ScharrDegreeY = parameters.GetInt(nameof(property.ScharrDegreeY), property.ScharrDegreeY);
            property.LaplacianKernelSize = parameters.GetInt(nameof(property.LaplacianKernelSize), property.LaplacianKernelSize);

            EdgeDetectionTool tool = new EdgeDetectionTool();
            tool.SetProperty(property);
            return tool;
        }

        private static RotateScaleTool CreateRotateScaleTool(VisionPipelineParameterReader parameters)
        {
            RotateScaleToolProperty property = new RotateScaleToolProperty();
            property.Angle = parameters.GetDouble(nameof(property.Angle), property.Angle);
            property.ScaleXPercent = parameters.GetDouble(nameof(property.ScaleXPercent), property.ScaleXPercent);
            property.ScaleYPercent = parameters.GetDouble(nameof(property.ScaleYPercent), property.ScaleYPercent);
            property.Interpolation = parameters.GetEnum(nameof(property.Interpolation), property.Interpolation);
            property.BorderType = parameters.GetEnum(nameof(property.BorderType), property.BorderType);

            RotateScaleTool tool = new RotateScaleTool();
            tool.SetProperty(property);
            return tool;
        }

        private static AffineTransformTool CreateAffineTransformTool(VisionPipelineParameterReader parameters)
        {
            AffineTransformToolProperty property = new AffineTransformToolProperty();
            property.SourcePoint1X = parameters.GetDouble(nameof(property.SourcePoint1X), property.SourcePoint1X);
            property.SourcePoint1Y = parameters.GetDouble(nameof(property.SourcePoint1Y), property.SourcePoint1Y);
            property.SourcePoint2X = parameters.GetDouble(nameof(property.SourcePoint2X), property.SourcePoint2X);
            property.SourcePoint2Y = parameters.GetDouble(nameof(property.SourcePoint2Y), property.SourcePoint2Y);
            property.SourcePoint3X = parameters.GetDouble(nameof(property.SourcePoint3X), property.SourcePoint3X);
            property.SourcePoint3Y = parameters.GetDouble(nameof(property.SourcePoint3Y), property.SourcePoint3Y);
            property.DestinationPoint1X = parameters.GetDouble(nameof(property.DestinationPoint1X), property.DestinationPoint1X);
            property.DestinationPoint1Y = parameters.GetDouble(nameof(property.DestinationPoint1Y), property.DestinationPoint1Y);
            property.DestinationPoint2X = parameters.GetDouble(nameof(property.DestinationPoint2X), property.DestinationPoint2X);
            property.DestinationPoint2Y = parameters.GetDouble(nameof(property.DestinationPoint2Y), property.DestinationPoint2Y);
            property.DestinationPoint3X = parameters.GetDouble(nameof(property.DestinationPoint3X), property.DestinationPoint3X);
            property.DestinationPoint3Y = parameters.GetDouble(nameof(property.DestinationPoint3Y), property.DestinationPoint3Y);
            property.OutputWidth = parameters.GetInt(nameof(property.OutputWidth), property.OutputWidth);
            property.OutputHeight = parameters.GetInt(nameof(property.OutputHeight), property.OutputHeight);
            property.Interpolation = parameters.GetEnum(nameof(property.Interpolation), property.Interpolation);
            property.BorderType = parameters.GetEnum(nameof(property.BorderType), property.BorderType);
            property.BorderValue = parameters.GetDouble(nameof(property.BorderValue), property.BorderValue);
            property.MinimumSourceTriangleArea = parameters.GetDouble(nameof(property.MinimumSourceTriangleArea), property.MinimumSourceTriangleArea);
            property.MinimumDestinationTriangleArea = parameters.GetDouble(nameof(property.MinimumDestinationTriangleArea), property.MinimumDestinationTriangleArea);
            property.MinimumValidPixelRatio = parameters.GetDouble(nameof(property.MinimumValidPixelRatio), property.MinimumValidPixelRatio);

            AffineTransformTool tool = new AffineTransformTool();
            tool.SetProperty(property);
            return tool;
        }

        private static ContourTool CreateContourTool(VisionPipelineParameterReader parameters)
        {
            ContourTool tool = new ContourTool();
            tool.SetProperty(CreateContourProperty(parameters));
            return tool;
        }

        private static CornerTool CreateCornerTool(VisionPipelineParameterReader parameters)
        {
            CornerTool tool = new CornerTool();
            tool.SetProperty(CreateContourProperty(parameters));
            return tool;
        }

        private static ContourToolProperty CreateContourProperty(VisionPipelineParameterReader parameters)
        {
            ContourToolProperty property = new ContourToolProperty();
            PopulateCommonProperty(property, parameters);
            property.USE_APPROXPOLYDP = parameters.GetBool(nameof(property.USE_APPROXPOLYDP), property.USE_APPROXPOLYDP);
            property.USE_DRAW_IMAGE = parameters.GetBool(nameof(property.USE_DRAW_IMAGE), property.USE_DRAW_IMAGE);
            property.ApproximationModes = parameters.GetEnum(nameof(property.ApproximationModes), property.ApproximationModes);
            property.DetectMode = parameters.GetEnum(nameof(property.DetectMode), property.DetectMode);
            property.EPSILON = parameters.GetDouble(nameof(property.EPSILON), property.EPSILON);
            property.MIN_AREA = parameters.GetInt(nameof(property.MIN_AREA), property.MIN_AREA);
            property.MAX_AREA = parameters.GetInt(nameof(property.MAX_AREA), property.MAX_AREA);
            property.MIN_WIDTH = parameters.GetInt(nameof(property.MIN_WIDTH), property.MIN_WIDTH);
            property.MAX_WIDTH = parameters.GetInt(nameof(property.MAX_WIDTH), property.MAX_WIDTH);
            property.MIN_HEIGHT = parameters.GetInt(nameof(property.MIN_HEIGHT), property.MIN_HEIGHT);
            property.MAX_HEIGHT = parameters.GetInt(nameof(property.MAX_HEIGHT), property.MAX_HEIGHT);
            property.DrawColor = parameters.GetColor(nameof(property.DrawColor), property.DrawColor);
            property.DrawThickness = parameters.GetInt(nameof(property.DrawThickness), property.DrawThickness);
            property.ClrGridHtml = parameters.GetString(nameof(property.ClrGridHtml), property.ClrGridHtml);
            return property;
        }

        private static MatchingTool CreateMatchingTool(
            VisionPipelineParameterReader parameters,
            VisionPipelineArtifactReference artifact,
            Func<VisionPipelineArtifactReference, byte[]> artifactResolver)
        {
            MatchingToolProperty property = new MatchingToolProperty();
            PopulateCommonProperty(property, parameters);
            property.MATCH_MODE = parameters.GetEnum(nameof(property.MATCH_MODE), property.MATCH_MODE);
            property.SCORE_MIN = parameters.GetDouble(nameof(property.SCORE_MIN), property.SCORE_MIN);
            property.MAGNIFIATION = parameters.GetDouble(nameof(property.MAGNIFIATION), property.MAGNIFIATION);
            property.NUM_MATCH = parameters.GetInt(nameof(property.NUM_MATCH), property.NUM_MATCH);
            property.USE_FIND_SCALE = parameters.GetBool(nameof(property.USE_FIND_SCALE), property.USE_FIND_SCALE);
            property.FIND_SCALE_MIN = parameters.GetDouble(nameof(property.FIND_SCALE_MIN), property.FIND_SCALE_MIN);
            property.FIND_SCALE_MAX = parameters.GetDouble(nameof(property.FIND_SCALE_MAX), property.FIND_SCALE_MAX);
            property.FIND_SCALE_STEP = parameters.GetDouble(nameof(property.FIND_SCALE_STEP), property.FIND_SCALE_STEP);
            property.USE_FIND_ANGLE = parameters.GetBool(nameof(property.USE_FIND_ANGLE), property.USE_FIND_ANGLE);
            property.FIND_ANGLE = parameters.GetDouble(nameof(property.FIND_ANGLE), property.FIND_ANGLE);
            property.FIND_ANGLE_MAX = parameters.GetInt(nameof(property.FIND_ANGLE_MAX), property.FIND_ANGLE_MAX);
            property.FIND_ANGLE_MIN = parameters.GetInt(nameof(property.FIND_ANGLE_MIN), property.FIND_ANGLE_MIN);
            property.USE_COARSE_TO_FINE_ANGLE_SEARCH = parameters.GetBool(nameof(property.USE_COARSE_TO_FINE_ANGLE_SEARCH), property.USE_COARSE_TO_FINE_ANGLE_SEARCH);
            property.COARSE_ANGLE_STEP = parameters.GetDouble(nameof(property.COARSE_ANGLE_STEP), property.COARSE_ANGLE_STEP);
            property.COARSE_ANGLE_TOP_K = parameters.GetInt(nameof(property.COARSE_ANGLE_TOP_K), property.COARSE_ANGLE_TOP_K);
            property.USE_PYRAMID_POSITION_PROPOSAL = parameters.GetBool(nameof(property.USE_PYRAMID_POSITION_PROPOSAL), property.USE_PYRAMID_POSITION_PROPOSAL);
            property.PYRAMID_POSITION_TOP_N = parameters.GetInt(nameof(property.PYRAMID_POSITION_TOP_N), property.PYRAMID_POSITION_TOP_N);
            property.PYRAMID_POSITION_MIN_SCORE = parameters.GetDouble(nameof(property.PYRAMID_POSITION_MIN_SCORE), property.PYRAMID_POSITION_MIN_SCORE);
            property.USE_CANNY = parameters.GetBool(nameof(property.USE_CANNY), property.USE_CANNY);
            property.CANNY_HIGH = parameters.GetInt(nameof(property.CANNY_HIGH), property.CANNY_HIGH);
            property.CANNY_LOW = parameters.GetInt(nameof(property.CANNY_LOW), property.CANNY_LOW);
            property.USE_PADDING_COLOR_WHITE = parameters.GetBool(nameof(property.USE_PADDING_COLOR_WHITE), property.USE_PADDING_COLOR_WHITE);

            using (Mat template = ResolveTemplate(artifact, artifactResolver))
            {
                MatchingTool tool = new MatchingTool();
                try
                {
                    tool.SetProperty(property);
                    tool.SetTemplateImage(template);
                    return tool;
                }
                catch
                {
                    tool.Dispose();
                    throw;
                }
            }
        }

        private static EdgeBasedTemplateMatchingTool CreateEdgeBasedTemplateMatchingTool(
            VisionPipelineParameterReader parameters,
            VisionPipelineArtifactReference artifact,
            Func<VisionPipelineArtifactReference, byte[]> artifactResolver)
        {
            EdgeBasedTemplateMatchingToolProperty property = new EdgeBasedTemplateMatchingToolProperty();
            PopulateCommonProperty(property, parameters);
            property.SCORE_MIN = parameters.GetDouble(nameof(property.SCORE_MIN), property.SCORE_MIN);
            property.NUM_MATCH = parameters.GetInt(nameof(property.NUM_MATCH), property.NUM_MATCH);
            property.USE_UNIQUE_MATCH_VALIDATION = parameters.GetBool(nameof(property.USE_UNIQUE_MATCH_VALIDATION), property.USE_UNIQUE_MATCH_VALIDATION);
            property.UNIQUE_MATCH_MIN_SCORE_MARGIN = parameters.GetDouble(nameof(property.UNIQUE_MATCH_MIN_SCORE_MARGIN), property.UNIQUE_MATCH_MIN_SCORE_MARGIN);
            property.ALLOW_GLOBAL_POLARITY_REVERSAL = parameters.GetBool(nameof(property.ALLOW_GLOBAL_POLARITY_REVERSAL), property.ALLOW_GLOBAL_POLARITY_REVERSAL);
            property.CANNY_LOW = parameters.GetInt(nameof(property.CANNY_LOW), property.CANNY_LOW);
            property.CANNY_HIGH = parameters.GetInt(nameof(property.CANNY_HIGH), property.CANNY_HIGH);
            property.CANNY_APERTURE_SIZE = parameters.GetInt(nameof(property.CANNY_APERTURE_SIZE), property.CANNY_APERTURE_SIZE);
            property.USE_L2_GRADIENT = parameters.GetBool(nameof(property.USE_L2_GRADIENT), property.USE_L2_GRADIENT);
            property.CONTOUR_RETRIEVAL_MODE = parameters.GetEnum(nameof(property.CONTOUR_RETRIEVAL_MODE), property.CONTOUR_RETRIEVAL_MODE);
            property.CONTOUR_APPROXIMATION_MODE = parameters.GetEnum(nameof(property.CONTOUR_APPROXIMATION_MODE), property.CONTOUR_APPROXIMATION_MODE);
            property.USE_FIND_ANGLE = parameters.GetBool(nameof(property.USE_FIND_ANGLE), property.USE_FIND_ANGLE);
            property.FIND_ANGLE = parameters.GetDouble(nameof(property.FIND_ANGLE), property.FIND_ANGLE);
            property.FIND_ANGLE_MAX = parameters.GetInt(nameof(property.FIND_ANGLE_MAX), property.FIND_ANGLE_MAX);
            property.FIND_ANGLE_MIN = parameters.GetInt(nameof(property.FIND_ANGLE_MIN), property.FIND_ANGLE_MIN);
            property.USE_COARSE_TO_FINE_ANGLE_SEARCH = parameters.GetBool(nameof(property.USE_COARSE_TO_FINE_ANGLE_SEARCH), property.USE_COARSE_TO_FINE_ANGLE_SEARCH);
            property.COARSE_ANGLE_STEP = parameters.GetDouble(nameof(property.COARSE_ANGLE_STEP), property.COARSE_ANGLE_STEP);
            property.COARSE_ANGLE_TOP_K = parameters.GetInt(nameof(property.COARSE_ANGLE_TOP_K), property.COARSE_ANGLE_TOP_K);
            property.USE_FIND_SCALE = parameters.GetBool(nameof(property.USE_FIND_SCALE), property.USE_FIND_SCALE);
            property.FIND_SCALE_MIN = parameters.GetDouble(nameof(property.FIND_SCALE_MIN), property.FIND_SCALE_MIN);
            property.FIND_SCALE_MAX = parameters.GetDouble(nameof(property.FIND_SCALE_MAX), property.FIND_SCALE_MAX);
            property.FIND_SCALE_STEP = parameters.GetDouble(nameof(property.FIND_SCALE_STEP), property.FIND_SCALE_STEP);
            property.GREEDINESS = parameters.GetDouble(nameof(property.GREEDINESS), property.GREEDINESS);
            property.SEARCH_STEP = parameters.GetInt(nameof(property.SEARCH_STEP), property.SEARCH_STEP);
            property.USE_POSITION_REFINE = parameters.GetBool(nameof(property.USE_POSITION_REFINE), property.USE_POSITION_REFINE);
            property.USE_SUBPIXEL_REFINE = parameters.GetBool(nameof(property.USE_SUBPIXEL_REFINE), property.USE_SUBPIXEL_REFINE);
            property.USE_PYRAMID_POSITION_PROPOSAL = parameters.GetBool(nameof(property.USE_PYRAMID_POSITION_PROPOSAL), property.USE_PYRAMID_POSITION_PROPOSAL);
            property.PYRAMID_POSITION_TOP_N = parameters.GetInt(nameof(property.PYRAMID_POSITION_TOP_N), property.PYRAMID_POSITION_TOP_N);
            property.PYRAMID_POSITION_MIN_SCORE = parameters.GetDouble(nameof(property.PYRAMID_POSITION_MIN_SCORE), property.PYRAMID_POSITION_MIN_SCORE);
            property.USE_HYBRID_VERIFY = parameters.GetBool(nameof(property.USE_HYBRID_VERIFY), property.USE_HYBRID_VERIFY);
            property.HYBRID_VERIFY_TOP_N = parameters.GetInt(nameof(property.HYBRID_VERIFY_TOP_N), property.HYBRID_VERIFY_TOP_N);
            property.HYBRID_VERIFY_IMAGE_WEIGHT = parameters.GetDouble(nameof(property.HYBRID_VERIFY_IMAGE_WEIGHT), property.HYBRID_VERIFY_IMAGE_WEIGHT);
            property.MAX_TEMPLATE_POINTS = parameters.GetInt(nameof(property.MAX_TEMPLATE_POINTS), property.MAX_TEMPLATE_POINTS);
            property.MIN_GRADIENT_MAGNITUDE = parameters.GetDouble(nameof(property.MIN_GRADIENT_MAGNITUDE), property.MIN_GRADIENT_MAGNITUDE);
            property.USE_DRAW_IMAGE = parameters.GetBool(nameof(property.USE_DRAW_IMAGE), property.USE_DRAW_IMAGE);

            using (Mat template = ResolveTemplate(artifact, artifactResolver))
            {
                EdgeBasedTemplateMatchingTool tool = new EdgeBasedTemplateMatchingTool();
                try
                {
                    tool.SetProperty(property);
                    tool.SetTemplateImage(template);
                    return tool;
                }
                catch
                {
                    tool.Dispose();
                    throw;
                }
            }
        }

        private static AutoMPointTool CreateAutoMPointTool(VisionPipelineParameterReader parameters)
        {
            AutoMPointToolProperty property = new AutoMPointToolProperty();
            property.UseAnalysisRoi = parameters.GetBool(nameof(property.UseAnalysisRoi), property.UseAnalysisRoi);
            property.AnalysisRoi = parameters.GetRectangle(nameof(property.AnalysisRoi), property.AnalysisRoi);
            property.CandidateMode = parameters.GetEnum(nameof(property.CandidateMode), property.CandidateMode);
            property.PatternWidth = parameters.GetInt(nameof(property.PatternWidth), property.PatternWidth);
            property.PatternHeight = parameters.GetInt(nameof(property.PatternHeight), property.PatternHeight);
            property.CandidateStride = parameters.GetInt(nameof(property.CandidateStride), property.CandidateStride);
            property.MaximumFinalists = parameters.GetInt(nameof(property.MaximumFinalists), property.MaximumFinalists);
            property.MaximumResults = parameters.GetInt(nameof(property.MaximumResults), property.MaximumResults);
            property.MaximumCandidateOverlap = parameters.GetDouble(nameof(property.MaximumCandidateOverlap), property.MaximumCandidateOverlap);
            property.MinimumContrastStdDev = parameters.GetDouble(nameof(property.MinimumContrastStdDev), property.MinimumContrastStdDev);
            property.MinimumEdgeDensity = parameters.GetDouble(nameof(property.MinimumEdgeDensity), property.MinimumEdgeDensity);
            property.MinimumQuadrantBalance = parameters.GetDouble(nameof(property.MinimumQuadrantBalance), property.MinimumQuadrantBalance);
            property.MinimumOrientationBalance = parameters.GetDouble(nameof(property.MinimumOrientationBalance), property.MinimumOrientationBalance);
            property.MinimumFeatureQuality = parameters.GetDouble(nameof(property.MinimumFeatureQuality), property.MinimumFeatureQuality);
            property.CannyLow = parameters.GetInt(nameof(property.CannyLow), property.CannyLow);
            property.CannyHigh = parameters.GetInt(nameof(property.CannyHigh), property.CannyHigh);
            property.MatchingMinimumScore = parameters.GetDouble(nameof(property.MatchingMinimumScore), property.MatchingMinimumScore);
            property.MinimumUniquenessMargin = parameters.GetDouble(nameof(property.MinimumUniquenessMargin), property.MinimumUniquenessMargin);
            property.MaximumTemplatePoints = parameters.GetInt(nameof(property.MaximumTemplatePoints), property.MaximumTemplatePoints);
            property.SearchStep = parameters.GetInt(nameof(property.SearchStep), property.SearchStep);
            property.UsePositionRefine = parameters.GetBool(nameof(property.UsePositionRefine), property.UsePositionRefine);
            property.UseSubpixelRefine = parameters.GetBool(nameof(property.UseSubpixelRefine), property.UseSubpixelRefine);
            property.UsePyramidPositionProposal = parameters.GetBool(nameof(property.UsePyramidPositionProposal), property.UsePyramidPositionProposal);
            property.UseHybridVerify = parameters.GetBool(nameof(property.UseHybridVerify), property.UseHybridVerify);
            property.UseAngleSearch = parameters.GetBool(nameof(property.UseAngleSearch), property.UseAngleSearch);
            property.AngleMinimum = parameters.GetInt(nameof(property.AngleMinimum), property.AngleMinimum);
            property.AngleMaximum = parameters.GetInt(nameof(property.AngleMaximum), property.AngleMaximum);
            property.AngleStep = parameters.GetDouble(nameof(property.AngleStep), property.AngleStep);
            property.UseScaleSearch = parameters.GetBool(nameof(property.UseScaleSearch), property.UseScaleSearch);
            property.ScaleMinimum = parameters.GetDouble(nameof(property.ScaleMinimum), property.ScaleMinimum);
            property.ScaleMaximum = parameters.GetDouble(nameof(property.ScaleMaximum), property.ScaleMaximum);
            property.ScaleStep = parameters.GetDouble(nameof(property.ScaleStep), property.ScaleStep);
            property.SyntheticTranslationPixels = parameters.GetInt(nameof(property.SyntheticTranslationPixels), property.SyntheticTranslationPixels);
            property.SyntheticRotationDegrees = parameters.GetDouble(nameof(property.SyntheticRotationDegrees), property.SyntheticRotationDegrees);
            property.SyntheticScaleRatio = parameters.GetDouble(nameof(property.SyntheticScaleRatio), property.SyntheticScaleRatio);
            property.MinimumSyntheticSuccessRate = parameters.GetDouble(nameof(property.MinimumSyntheticSuccessRate), property.MinimumSyntheticSuccessRate);
            property.MaximumPositionErrorPixels = parameters.GetDouble(nameof(property.MaximumPositionErrorPixels), property.MaximumPositionErrorPixels);
            property.MaximumAngleErrorDegrees = parameters.GetDouble(nameof(property.MaximumAngleErrorDegrees), property.MaximumAngleErrorDegrees);
            property.MaximumScaleErrorRatio = parameters.GetDouble(nameof(property.MaximumScaleErrorRatio), property.MaximumScaleErrorRatio);
            property.MaximumRuntimeMilliseconds = parameters.GetDouble(nameof(property.MaximumRuntimeMilliseconds), property.MaximumRuntimeMilliseconds);
            property.MinimumRepresentativeImageCount = parameters.GetInt(nameof(property.MinimumRepresentativeImageCount), property.MinimumRepresentativeImageCount);
            property.MinimumRepresentativeSuccessRate = parameters.GetDouble(nameof(property.MinimumRepresentativeSuccessRate), property.MinimumRepresentativeSuccessRate);

            AutoMPointTool tool = new AutoMPointTool();
            tool.SetProperty(property);
            return tool;
        }

        private static SiftTool CreateSiftTool(
            VisionPipelineParameterReader parameters,
            VisionPipelineArtifactReference artifact,
            Func<VisionPipelineArtifactReference, byte[]> artifactResolver)
        {
            SiftToolProperty property = new SiftToolProperty();
            PopulateCommonProperty(property, parameters);
            property.RANSAC_REPROJ_THRESHOLD = parameters.GetDouble(nameof(property.RANSAC_REPROJ_THRESHOLD), property.RANSAC_REPROJ_THRESHOLD);
            property.SCORE_MIN = parameters.GetDouble(nameof(property.SCORE_MIN), property.SCORE_MIN);

            using (Mat template = ResolveTemplate(artifact, artifactResolver))
            {
                SiftTool tool = new SiftTool();
                try
                {
                    tool.SetProperty(property);
                    tool.SetTemplateImage(template);
                    return tool;
                }
                catch
                {
                    tool.Dispose();
                    throw;
                }
            }
        }

        private static LineGaugeTool CreateLineGaugeTool(VisionPipelineParameterReader parameters)
        {
            LineGaugeToolProperty property = new LineGaugeToolProperty();
            PopulateCommonProperty(property, parameters);
            property.PRJ_PORALITY = parameters.GetEnum(nameof(property.PRJ_PORALITY), property.PRJ_PORALITY);
            property.PRJ_DIR = parameters.GetEnum(nameof(property.PRJ_DIR), property.PRJ_DIR);
            property.CONTRAST = parameters.GetDouble(nameof(property.CONTRAST), property.CONTRAST);
            property.THICKNESS = parameters.GetDouble(nameof(property.THICKNESS), property.THICKNESS);
            property.SAMPLING_STEP = parameters.GetDouble(nameof(property.SAMPLING_STEP), property.SAMPLING_STEP);
            property.VER_PRJ_DIR = parameters.GetEnum(nameof(property.VER_PRJ_DIR), property.VER_PRJ_DIR);
            property.POINT_RANGE = parameters.GetInt(nameof(property.POINT_RANGE), property.POINT_RANGE);
            property.USE_MANUAL_ANGLE = parameters.GetBool(nameof(property.USE_MANUAL_ANGLE), property.USE_MANUAL_ANGLE);
            property.MANUAL_ANGLE_VALUE = parameters.GetDouble(nameof(property.MANUAL_ANGLE_VALUE), property.MANUAL_ANGLE_VALUE);
            property.USE_EXTEND_FIT_LINE = parameters.GetBool(nameof(property.USE_EXTEND_FIT_LINE), property.USE_EXTEND_FIT_LINE);
            property.EXTEND_FIT_LINE_VALUE = parameters.GetInt(nameof(property.EXTEND_FIT_LINE_VALUE), property.EXTEND_FIT_LINE_VALUE);
            property.SHOW_VERTICAL_LINE = parameters.GetBool(nameof(property.SHOW_VERTICAL_LINE), property.SHOW_VERTICAL_LINE);
            property.SHOW_EDGE = parameters.GetBool(nameof(property.SHOW_EDGE), property.SHOW_EDGE);
            property.SHOW_CONTOUR = parameters.GetBool(nameof(property.SHOW_CONTOUR), property.SHOW_CONTOUR);
            property.SHOW_FITLINE = parameters.GetBool(nameof(property.SHOW_FITLINE), property.SHOW_FITLINE);

            LineGaugeTool tool = new LineGaugeTool();
            tool.SetProperty(property);
            return tool;
        }

        private static MeanTool CreateMeanTool(VisionPipelineParameterReader parameters)
        {
            MeanToolProperty property = new MeanToolProperty();
            PopulateCommonProperty(property, parameters);
            property.MEAN_MAX = parameters.GetInt(nameof(property.MEAN_MAX), property.MEAN_MAX);
            property.MEAN_MIN = parameters.GetInt(nameof(property.MEAN_MIN), property.MEAN_MIN);
            property.MEAN_TYPES = parameters.GetEnum(nameof(property.MEAN_TYPES), property.MEAN_TYPES);

            MeanTool tool = new MeanTool();
            tool.SetProperty(property);
            return tool;
        }

        private static void PopulateCommonProperty(OpenCvToolPropertyBase property, VisionPipelineParameterReader parameters)
        {
            property.NAME = parameters.GetString(nameof(property.NAME), property.NAME);
            property.PIXELPERMM = parameters.GetDouble(nameof(property.PIXELPERMM), property.PIXELPERMM);
            property.USE_THRESHOLD = parameters.GetBool(nameof(property.USE_THRESHOLD), property.USE_THRESHOLD);
            property.USE_BITWISENOT = parameters.GetBool(nameof(property.USE_BITWISENOT), property.USE_BITWISENOT);
            property.THRESHOLD_TYPES = parameters.GetEnum(nameof(property.THRESHOLD_TYPES), property.THRESHOLD_TYPES);
            property.THRESHOLD = parameters.GetDouble(nameof(property.THRESHOLD), property.THRESHOLD);
            property.USE_ADAPTIVE_THRESHOLD = parameters.GetBool(nameof(property.USE_ADAPTIVE_THRESHOLD), property.USE_ADAPTIVE_THRESHOLD);
            property.ADAPTIVE_THRESHOLD = parameters.GetDouble(nameof(property.ADAPTIVE_THRESHOLD), property.ADAPTIVE_THRESHOLD);
            property.ADAPTIVE_THRESHOLD_TYPES = parameters.GetEnum(nameof(property.ADAPTIVE_THRESHOLD_TYPES), property.ADAPTIVE_THRESHOLD_TYPES);
            property.ADAPTIVE_THRESHOLD_ALGORITHM = parameters.GetEnum(nameof(property.ADAPTIVE_THRESHOLD_ALGORITHM), property.ADAPTIVE_THRESHOLD_ALGORITHM);
            property.BlockSize = parameters.GetInt(nameof(property.BlockSize), property.BlockSize);
            property.Weight = parameters.GetInt(nameof(property.Weight), property.Weight);
            property.USE_ROI = parameters.GetBool(nameof(property.USE_ROI), property.USE_ROI);
            property.USE_MULTI_ROI = parameters.GetBool(nameof(property.USE_MULTI_ROI), property.USE_MULTI_ROI);
            property.CvROI = parameters.GetRectangle(nameof(property.CvROI), property.CvROI);
            property.CvROIS = parameters.GetRectangleList(nameof(property.CvROIS), property.CvROIS);
            property.CvMASKS = parameters.GetRectangleList(nameof(property.CvMASKS), property.CvMASKS);
        }

        private static VisionPipelineArtifactReference ValidateArtifacts(
            VisionPipelineStep step,
            VisionPipelineToolDescriptor descriptor,
            Func<VisionPipelineArtifactReference, byte[]> artifactResolver)
        {
            if (descriptor.Artifacts.Count == 0)
            {
                if (step.Artifacts.Count > 0)
                {
                    throw new ArgumentException(
                        $"Vision pipeline Tool '{descriptor.ToolType}' does not accept artifacts.",
                        nameof(step));
                }

                return null;
            }

            VisionPipelineArtifactDescriptor requirement = descriptor.Artifacts[0];
            if (step.Artifacts.Count != 1)
            {
                throw new ArgumentException(
                    $"Vision pipeline Tool '{descriptor.ToolType}' requires exactly one '{requirement.Role}' artifact.",
                    nameof(step));
            }

            VisionPipelineArtifactReference artifact = step.Artifacts[0];
            VisionPipelineArtifactValidation.ValidateReference(artifact, nameof(step));
            if (!string.Equals(artifact.Role, requirement.Role, StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException(
                    $"Vision pipeline Tool '{descriptor.ToolType}' requires artifact role '{requirement.Role}'.",
                    nameof(step));
            }

            if (!string.Equals(artifact.Format, requirement.Format, StringComparison.OrdinalIgnoreCase)
                || artifact.FormatVersion != requirement.FormatVersion)
            {
                throw new NotSupportedException(
                    $"Vision pipeline artifact '{artifact.Id}' uses unsupported format '{artifact.Format}' version {artifact.FormatVersion}. "
                    + $"Required: '{requirement.Format}' version {requirement.FormatVersion}.");
            }

            if (artifactResolver == null)
            {
                throw new InvalidOperationException(
                    $"Vision pipeline Tool '{descriptor.ToolType}' requires a host artifact resolver for '{artifact.Id}'.");
            }

            return artifact;
        }

        private static Mat ResolveTemplate(
            VisionPipelineArtifactReference artifact,
            Func<VisionPipelineArtifactReference, byte[]> artifactResolver)
        {
            byte[] encoded = artifactResolver(artifact);
            if (encoded == null || encoded.Length == 0)
            {
                throw new InvalidOperationException($"Vision pipeline artifact resolver returned no bytes for '{artifact.Id}'.");
            }

            string actualHash;
            using (SHA256 sha256 = SHA256.Create())
            {
                actualHash = BitConverter.ToString(sha256.ComputeHash(encoded)).Replace("-", string.Empty);
            }

            if (!string.Equals(actualHash, artifact.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Vision pipeline artifact SHA-256 mismatch for '{artifact.Id}'. Expected={artifact.Sha256}, Actual={actualHash}.");
            }

            Mat template;
            try
            {
                using (MemoryStream stream = new MemoryStream(encoded, false))
                {
                    template = Mat.FromStream(stream, ImreadModes.Unchanged);
                }
            }
            catch (Exception exception)
            {
                throw new InvalidOperationException(
                    $"Vision pipeline artifact '{artifact.Id}' could not be decoded as an image.",
                    exception);
            }

            if (template == null || template.Empty())
            {
                template?.Dispose();
                throw new InvalidOperationException($"Vision pipeline artifact '{artifact.Id}' decoded to an empty image.");
            }

            return template;
        }
    }
}
