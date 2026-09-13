using OpenVisionLab.Vision2D.Property;
using OpenVisionLab.Vision2D.Tool;
using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Globalization;

namespace OpenVisionLab.Vision2D.Pipeline
{
    /// <summary>Creates the built-in 2D tools from validated pipeline parameters.</summary>
    public static class VisionPipelineToolFactory
    {
        /// <summary>
        /// Creates a built-in tool and rejects unknown parameter names or invalid supplied values.
        /// </summary>
        public static IVisionTool Create(VisionPipelineStep step)
        {
            if (step == null)
            {
                throw new ArgumentNullException(nameof(step));
            }

            string toolType = NormalizeToolType(step.ToolType);

            switch (toolType)
            {
                case "threshold":
                    return CreateThresholdTool(step.Parameters);
                case "morphology":
                    return CreateMorphologyTool(step.Parameters);
                case "filter":
                    return CreateFilterTool(step.Parameters);
                case "edgedetection":
                case "edge":
                    return CreateEdgeDetectionTool(step.Parameters);
                case "rotatescale":
                case "rotateandscale":
                    return CreateRotateScaleTool(step.Parameters);
                case "affine":
                case "affinematrix":
                case "affinetransform":
                    return CreateAffineTransformTool(step.Parameters);
                default:
                    throw new NotSupportedException($"Unsupported vision tool type '{step.ToolType}'.");
            }
        }

        private static IVisionTool CreateThresholdTool(IDictionary<string, string> parameters)
        {
            ValidateParameterKeys(parameters, typeof(ThresholdToolProperty));
            ThresholdToolProperty property = new ThresholdToolProperty
            {
                Mode = GetEnum(parameters, nameof(ThresholdToolProperty.Mode), ThresholdToolMode.Threshold),
                Threshold = GetDouble(parameters, nameof(ThresholdToolProperty.Threshold), 1),
                MaxValue = GetDouble(parameters, nameof(ThresholdToolProperty.MaxValue), 255),
                ThresholdType = GetEnum(parameters, nameof(ThresholdToolProperty.ThresholdType), ThresholdTypes.Binary),
                RangeMin = GetInt(parameters, nameof(ThresholdToolProperty.RangeMin), 1),
                RangeMax = GetInt(parameters, nameof(ThresholdToolProperty.RangeMax), 255),
                Invert = GetBool(parameters, nameof(ThresholdToolProperty.Invert), false),
                AdaptiveType = GetEnum(parameters, nameof(ThresholdToolProperty.AdaptiveType), AdaptiveThresholdTypes.MeanC),
                AdaptiveThresholdType = GetEnum(parameters, nameof(ThresholdToolProperty.AdaptiveThresholdType), ThresholdTypes.Binary),
                BlockSize = GetInt(parameters, nameof(ThresholdToolProperty.BlockSize), 25),
                Weight = GetInt(parameters, nameof(ThresholdToolProperty.Weight), 5)
            };

            ThresholdTool tool = new ThresholdTool();
            tool.SetProperty(property);
            return tool;
        }

        private static IVisionTool CreateMorphologyTool(IDictionary<string, string> parameters)
        {
            ValidateParameterKeys(parameters, typeof(MorphologyToolProperty));
            MorphologyToolProperty property = new MorphologyToolProperty
            {
                Shape = GetEnum(parameters, nameof(MorphologyToolProperty.Shape), MorphShapes.Rect),
                Operator = GetEnum(parameters, nameof(MorphologyToolProperty.Operator), MorphTypes.Erode),
                KernelWidth = GetInt(parameters, nameof(MorphologyToolProperty.KernelWidth), 3),
                KernelHeight = GetInt(parameters, nameof(MorphologyToolProperty.KernelHeight), 3),
                Iterations = GetInt(parameters, nameof(MorphologyToolProperty.Iterations), 1)
            };

            MorphologyTool tool = new MorphologyTool();
            tool.SetProperty(property);
            return tool;
        }

        private static IVisionTool CreateFilterTool(IDictionary<string, string> parameters)
        {
            ValidateParameterKeys(parameters, typeof(FilterToolProperty));
            FilterToolProperty property = new FilterToolProperty
            {
                FilterType = GetEnum(parameters, nameof(FilterToolProperty.FilterType), FilterToolType.Blur),
                KernelWidth = GetInt(parameters, nameof(FilterToolProperty.KernelWidth), 3),
                KernelHeight = GetInt(parameters, nameof(FilterToolProperty.KernelHeight), 3),
                MedianKernelSize = GetInt(parameters, nameof(FilterToolProperty.MedianKernelSize), 3),
                Diameter = GetInt(parameters, nameof(FilterToolProperty.Diameter), 3),
                SigmaColor = GetInt(parameters, nameof(FilterToolProperty.SigmaColor), 3),
                SigmaSpace = GetInt(parameters, nameof(FilterToolProperty.SigmaSpace), 3),
                BorderType = GetEnum(parameters, nameof(FilterToolProperty.BorderType), BorderTypes.Reflect101)
            };

            FilterTool tool = new FilterTool();
            tool.SetProperty(property);
            return tool;
        }

        private static IVisionTool CreateEdgeDetectionTool(IDictionary<string, string> parameters)
        {
            ValidateParameterKeys(parameters, typeof(EdgeDetectionToolProperty));
            EdgeDetectionToolProperty property = new EdgeDetectionToolProperty
            {
                EdgeType = GetEnum(parameters, nameof(EdgeDetectionToolProperty.EdgeType), EdgeDetectionToolType.Canny),
                CannyThresholdLow = GetInt(parameters, nameof(EdgeDetectionToolProperty.CannyThresholdLow), 100),
                CannyThresholdHigh = GetInt(parameters, nameof(EdgeDetectionToolProperty.CannyThresholdHigh), 200),
                CannyApertureSize = GetInt(parameters, nameof(EdgeDetectionToolProperty.CannyApertureSize), 3),
                UseL2Gradient = GetBool(parameters, nameof(EdgeDetectionToolProperty.UseL2Gradient), true),
                SobelDegreeX = GetInt(parameters, nameof(EdgeDetectionToolProperty.SobelDegreeX), 0),
                SobelDegreeY = GetInt(parameters, nameof(EdgeDetectionToolProperty.SobelDegreeY), 0),
                SobelKernelSize = GetInt(parameters, nameof(EdgeDetectionToolProperty.SobelKernelSize), 1),
                ScharrDegreeX = GetInt(parameters, nameof(EdgeDetectionToolProperty.ScharrDegreeX), 0),
                ScharrDegreeY = GetInt(parameters, nameof(EdgeDetectionToolProperty.ScharrDegreeY), 0),
                LaplacianKernelSize = GetInt(parameters, nameof(EdgeDetectionToolProperty.LaplacianKernelSize), 1)
            };

            EdgeDetectionTool tool = new EdgeDetectionTool();
            tool.SetProperty(property);
            return tool;
        }

        private static IVisionTool CreateRotateScaleTool(IDictionary<string, string> parameters)
        {
            ValidateParameterKeys(parameters, typeof(RotateScaleToolProperty));
            RotateScaleToolProperty property = new RotateScaleToolProperty
            {
                Angle = GetDouble(parameters, nameof(RotateScaleToolProperty.Angle), 0d),
                ScaleXPercent = GetDouble(parameters, nameof(RotateScaleToolProperty.ScaleXPercent), 100d),
                ScaleYPercent = GetDouble(parameters, nameof(RotateScaleToolProperty.ScaleYPercent), 100d),
                Interpolation = GetEnum(parameters, nameof(RotateScaleToolProperty.Interpolation), InterpolationFlags.Linear),
                BorderType = GetEnum(parameters, nameof(RotateScaleToolProperty.BorderType), BorderTypes.Constant)
            };

            RotateScaleTool tool = new RotateScaleTool();
            tool.SetProperty(property);
            return tool;
        }

        private static IVisionTool CreateAffineTransformTool(IDictionary<string, string> parameters)
        {
            ValidateParameterKeys(parameters, typeof(AffineTransformToolProperty));
            AffineTransformToolProperty property = new AffineTransformToolProperty
            {
                SourcePoint1X = GetDouble(parameters, nameof(AffineTransformToolProperty.SourcePoint1X), 0d),
                SourcePoint1Y = GetDouble(parameters, nameof(AffineTransformToolProperty.SourcePoint1Y), 0d),
                SourcePoint2X = GetDouble(parameters, nameof(AffineTransformToolProperty.SourcePoint2X), 100d),
                SourcePoint2Y = GetDouble(parameters, nameof(AffineTransformToolProperty.SourcePoint2Y), 0d),
                SourcePoint3X = GetDouble(parameters, nameof(AffineTransformToolProperty.SourcePoint3X), 0d),
                SourcePoint3Y = GetDouble(parameters, nameof(AffineTransformToolProperty.SourcePoint3Y), 100d),
                DestinationPoint1X = GetDouble(parameters, nameof(AffineTransformToolProperty.DestinationPoint1X), 0d),
                DestinationPoint1Y = GetDouble(parameters, nameof(AffineTransformToolProperty.DestinationPoint1Y), 0d),
                DestinationPoint2X = GetDouble(parameters, nameof(AffineTransformToolProperty.DestinationPoint2X), 100d),
                DestinationPoint2Y = GetDouble(parameters, nameof(AffineTransformToolProperty.DestinationPoint2Y), 0d),
                DestinationPoint3X = GetDouble(parameters, nameof(AffineTransformToolProperty.DestinationPoint3X), 0d),
                DestinationPoint3Y = GetDouble(parameters, nameof(AffineTransformToolProperty.DestinationPoint3Y), 100d),
                OutputWidth = GetInt(parameters, nameof(AffineTransformToolProperty.OutputWidth), 0),
                OutputHeight = GetInt(parameters, nameof(AffineTransformToolProperty.OutputHeight), 0),
                Interpolation = GetEnum(parameters, nameof(AffineTransformToolProperty.Interpolation), InterpolationFlags.Linear),
                BorderType = GetEnum(parameters, nameof(AffineTransformToolProperty.BorderType), BorderTypes.Constant),
                BorderValue = GetDouble(parameters, nameof(AffineTransformToolProperty.BorderValue), 0d),
                MinimumSourceTriangleArea = GetDouble(parameters, nameof(AffineTransformToolProperty.MinimumSourceTriangleArea), 1d),
                MinimumDestinationTriangleArea = GetDouble(parameters, nameof(AffineTransformToolProperty.MinimumDestinationTriangleArea), 1d),
                MinimumValidPixelRatio = GetDouble(parameters, nameof(AffineTransformToolProperty.MinimumValidPixelRatio), 0d)
            };

            AffineTransformTool tool = new AffineTransformTool();
            tool.SetProperty(property);
            return tool;
        }

        private static string NormalizeToolType(string toolType)
        {
            string value = (toolType ?? string.Empty).Trim();
            if (value.EndsWith("Tool", StringComparison.OrdinalIgnoreCase))
            {
                value = value.Substring(0, value.Length - 4);
            }

            return value.Replace(" ", string.Empty).Replace("_", string.Empty).ToLowerInvariant();
        }

        private static void ValidateParameterKeys(IDictionary<string, string> parameters, Type propertyType)
        {
            if (parameters == null)
            {
                return;
            }

            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (KeyValuePair<string, string> item in parameters)
            {
                if (string.IsNullOrWhiteSpace(item.Key))
                {
                    throw new ArgumentException("Vision pipeline parameter names cannot be empty.", nameof(parameters));
                }

                if (!seen.Add(item.Key))
                {
                    throw new ArgumentException($"Vision pipeline parameter '{item.Key}' is duplicated.", nameof(parameters));
                }

                bool known = false;
                foreach (var property in propertyType.GetProperties())
                {
                    if (property.CanWrite && string.Equals(property.Name, item.Key, StringComparison.OrdinalIgnoreCase))
                    {
                        known = true;
                        break;
                    }
                }

                if (!known)
                {
                    throw new ArgumentException(
                        $"Vision pipeline parameter '{item.Key}' is not supported by {propertyType.Name}.",
                        nameof(parameters));
                }
            }
        }

        private static bool TryGetValue(IDictionary<string, string> parameters, string key, out string value)
        {
            value = null;
            if (parameters == null || string.IsNullOrWhiteSpace(key))
            {
                return false;
            }

            foreach (KeyValuePair<string, string> item in parameters)
            {
                if (string.Equals(item.Key, key, StringComparison.OrdinalIgnoreCase))
                {
                    value = item.Value;
                    return true;
                }
            }

            return false;
        }

        private static int GetInt(IDictionary<string, string> parameters, string key, int defaultValue)
        {
            if (!TryGetValue(parameters, key, out string value))
            {
                return defaultValue;
            }

            if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int result))
            {
                throw InvalidParameter(key, value, "an integer", nameof(parameters));
            }

            return result;
        }

        private static double GetDouble(IDictionary<string, string> parameters, string key, double defaultValue)
        {
            if (!TryGetValue(parameters, key, out string value))
            {
                return defaultValue;
            }

            if (!double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double result)
                || double.IsNaN(result)
                || double.IsInfinity(result))
            {
                throw InvalidParameter(key, value, "a finite number", nameof(parameters));
            }

            return result;
        }

        private static bool GetBool(IDictionary<string, string> parameters, string key, bool defaultValue)
        {
            if (!TryGetValue(parameters, key, out string value))
            {
                return defaultValue;
            }

            if (!bool.TryParse(value, out bool result))
            {
                throw InvalidParameter(key, value, "true or false", nameof(parameters));
            }

            return result;
        }

        private static TEnum GetEnum<TEnum>(IDictionary<string, string> parameters, string key, TEnum defaultValue)
            where TEnum : struct
        {
            if (!TryGetValue(parameters, key, out string value))
            {
                return defaultValue;
            }

            if (!Enum.TryParse(value, true, out TEnum result) || !IsSupportedEnumValue(result))
            {
                throw InvalidParameter(key, value, typeof(TEnum).Name, nameof(parameters));
            }

            return result;
        }

        private static bool IsSupportedEnumValue<TEnum>(TEnum value)
            where TEnum : struct
        {
            Type enumType = typeof(TEnum);
            if (Enum.IsDefined(enumType, value))
            {
                return true;
            }

            if (!enumType.IsDefined(typeof(FlagsAttribute), false))
            {
                return false;
            }

            try
            {
                ulong allowedBits = 0;
                foreach (object definedValue in Enum.GetValues(enumType))
                {
                    allowedBits |= Convert.ToUInt64(definedValue, CultureInfo.InvariantCulture);
                }

                ulong actualBits = Convert.ToUInt64(value, CultureInfo.InvariantCulture);
                return (actualBits & ~allowedBits) == 0;
            }
            catch (OverflowException)
            {
                return false;
            }
        }

        private static ArgumentException InvalidParameter(string key, string value, string expected, string parameterName)
        {
            return new ArgumentException(
                $"Vision pipeline parameter '{key}' must be {expected}. Value='{value ?? "<null>"}'.",
                parameterName);
        }
    }
}
