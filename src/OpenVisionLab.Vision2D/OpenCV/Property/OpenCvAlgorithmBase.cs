using OpenVisionLab.Core;
using OpenVisionLab.Vision2D.Tool;
using OpenCvSharp;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Reflection;

namespace OpenVisionLab.Vision2D.Property
{
    public abstract class OpenCvAlgorithmBase : IVisionTool, IDisposable
    {
        public OpenCvAlgorithmBase() { }
        public Mat imageSource { get; set; } = new Mat();
        public Mat imageResult { get; set; } = new Mat();
        public Mat imageTemplate { get; set; } = new Mat();

        public Stopwatch swTaktTimems = new Stopwatch();

        public OpenCvSharp.Size size { get; set; } = new OpenCvSharp.Size();

        public virtual void SetSourceImage(Mat Image)
        {
            Image.CopyTo(imageSource);
            size = imageSource.Size();
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!disposing)
            {
                return;
            }

            imageSource?.Dispose();
            imageSource = null;

            imageResult?.Dispose();
            imageResult = null;

            imageTemplate?.Dispose();
            imageTemplate = null;
        }

        public abstract void Run();

        public virtual string Name => GetType().Name;

        public virtual VisionToolResult Execute(Mat source)
        {
            Stopwatch stopwatch = Stopwatch.StartNew();

            try
            {
                if (OpenCvHelper.IsImageEmpty(source))
                {
                    stopwatch.Stop();
                    return VisionToolResult.Failed(
                        VisionToolErrorCode.InputImageInvalid,
                        "Source image is not loaded.",
                        stopwatch.Elapsed);
                }

                SetSourceImage(source);
                if (!TryValidateBeforeRun(out VisionToolErrorCode validationErrorCode, out string validationMessage))
                {
                    stopwatch.Stop();
                    return VisionToolResult.Failed(
                        validationErrorCode,
                        validationMessage,
                        stopwatch.Elapsed);
                }

                Run();
                stopwatch.Stop();

                if (!TryValidateAfterRun(out VisionToolErrorCode runErrorCode, out string runMessage))
                {
                    VisionToolResult failedResult = VisionToolResult.Failed(
                        runErrorCode,
                        runMessage,
                        stopwatch.Elapsed);
                    AttachExecutionDetails(failedResult);
                    return failedResult;
                }

                return VisionToolResult.Passed(CreateResultImageSnapshot(), stopwatch.Elapsed, CollectMetrics(), CollectOverlays());
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                return VisionToolResult.Failed(
                    ResolveExecutionErrorCode(ex),
                    ex.GetBaseException().Message,
                    stopwatch.Elapsed,
                    ex);
            }
        }

        protected virtual bool TryValidateBeforeRun(out VisionToolErrorCode errorCode, out string message)
        {
            object toolProperty = GetToolProperty();
            if (toolProperty == null)
            {
                errorCode = VisionToolErrorCode.ToolPropertyMissing;
                message = $"{Name} property is not configured.";
                return false;
            }

            errorCode = VisionToolErrorCode.None;
            message = string.Empty;
            return true;
        }

        protected virtual bool TryValidateAfterRun(out VisionToolErrorCode errorCode, out string message)
        {
            errorCode = VisionToolErrorCode.None;
            message = string.Empty;
            return true;
        }

        private void AttachExecutionDetails(VisionToolResult result)
        {
            if (result == null)
            {
                return;
            }

            result.ResultImage = CreateResultImageSnapshot();
            foreach (KeyValuePair<string, double> metric in CollectMetrics())
            {
                result.Metrics[metric.Key] = metric.Value;
            }

            result.Overlays.AddRange(CollectOverlays().Where(overlay => overlay != null));
        }

        private Mat CreateResultImageSnapshot()
        {
            return OpenCvHelper.IsImageEmpty(imageResult)
                ? imageSource?.Clone()
                : imageResult.Clone();
        }

        protected virtual VisionToolErrorCode ResolveExecutionErrorCode(Exception exception)
        {
            Exception baseException = exception?.GetBaseException() ?? exception;
            string message = baseException?.Message ?? string.Empty;

            if (baseException is NullReferenceException && GetToolProperty() == null)
            {
                return VisionToolErrorCode.ToolPropertyMissing;
            }

            if (message.IndexOf("property", StringComparison.OrdinalIgnoreCase) >= 0
                && (message.IndexOf("not configured", StringComparison.OrdinalIgnoreCase) >= 0
                    || message.IndexOf("not set", StringComparison.OrdinalIgnoreCase) >= 0))
            {
                return VisionToolErrorCode.ToolPropertyMissing;
            }

            if (message.IndexOf("Source image", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return VisionToolErrorCode.InputImageInvalid;
            }

            if (baseException is OpenCVException || baseException is OpenCvSharpException)
            {
                return VisionToolErrorCode.OpenCvExecutionFailed;
            }

            if (ContainsMessageToken(message, "ROI")
                || ContainsMessageToken(message, "SubMat")
                || ContainsMessageToken(message, "Rect"))
            {
                return VisionToolErrorCode.InvalidRoi;
            }

            if (message.IndexOf("OpenCV", StringComparison.OrdinalIgnoreCase) >= 0
                || message.IndexOf("Bad argument", StringComparison.OrdinalIgnoreCase) >= 0
                || message.IndexOf("Assertion failed", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return VisionToolErrorCode.OpenCvExecutionFailed;
            }

            return VisionToolErrorCode.ToolExecutionException;
        }

        private static bool ContainsMessageToken(string message, string token)
        {
            if (string.IsNullOrEmpty(message) || string.IsNullOrEmpty(token))
            {
                return false;
            }

            int start = 0;
#pragma warning disable CA2249 // The start offset is required to inspect every token boundary.
            while (start < message.Length)
            {
                int index = message.IndexOf(token, start, StringComparison.OrdinalIgnoreCase);
                if (index < 0)
                {
                    return false;
                }

                bool hasWordBefore = index > 0 && char.IsLetter(message[index - 1]);
                int end = index + token.Length;
                bool hasWordAfter = end < message.Length && char.IsLetter(message[end]);
                if (!hasWordBefore && !hasWordAfter)
                {
                    return true;
                }

                start = index + token.Length;
            }
#pragma warning restore CA2249

            return false;
        }

        protected bool TryValidateRoi(Rect roi, bool allowWholeImageFallback, VisionToolErrorCode invalidCode, out VisionToolErrorCode errorCode, out string message)
        {
            if (roi.Width == 0 || roi.Height == 0)
            {
                if (allowWholeImageFallback)
                {
                    errorCode = VisionToolErrorCode.None;
                    message = string.Empty;
                    return true;
                }

                errorCode = invalidCode;
                message = "ROI is required for this tool.";
                return false;
            }

            if (roi.X < 0 || roi.Y < 0 || roi.Width < 0 || roi.Height < 0
                || roi.X + roi.Width > imageSource.Width
                || roi.Y + roi.Height > imageSource.Height)
            {
                errorCode = invalidCode;
                message = $"ROI is outside the source image. ROI=({roi.X},{roi.Y},{roi.Width},{roi.Height}), Image=({imageSource.Width},{imageSource.Height}).";
                return false;
            }

            errorCode = VisionToolErrorCode.None;
            message = string.Empty;
            return true;
        }

        protected static bool TryValidateAreaRange(
            int minArea,
            int maxArea,
            VisionToolErrorCode invalidCode,
            string toolName,
            out VisionToolErrorCode errorCode,
            out string message)
        {
            if (minArea > maxArea)
            {
                errorCode = invalidCode;
                message = $"{toolName} area range is invalid. Min={minArea}, Max={maxArea}.";
                return false;
            }

            errorCode = VisionToolErrorCode.None;
            message = string.Empty;
            return true;
        }

        protected bool TryValidateRoiSet(
            IOpenCVPropertyBase property,
            bool validateSingleRoi,
            bool allowWholeImageFallback,
            VisionToolErrorCode invalidCode,
            string toolName,
            out VisionToolErrorCode errorCode,
            out string message)
        {
            if (property == null)
            {
                errorCode = VisionToolErrorCode.ToolPropertyMissing;
                message = $"{toolName} property is not configured.";
                return false;
            }

            if (property.USE_MULTI_ROI)
            {
                if (property.CvROIS == null || property.CvROIS.Count == 0)
                {
                    errorCode = invalidCode;
                    message = $"{toolName} multi ROI is enabled, but ROI list is empty.";
                    return false;
                }

                for (int i = 0; i < property.CvROIS.Count; i++)
                {
                    if (!TryValidateRoi(property.CvROIS[i], allowWholeImageFallback, invalidCode, out errorCode, out message))
                    {
                        message = $"{toolName} ROI #{i + 1}: {message}";
                        return false;
                    }
                }
            }
            else if (validateSingleRoi
                && !TryValidateRoi(property.CvROI, allowWholeImageFallback, invalidCode, out errorCode, out message))
            {
                message = $"{toolName} ROI: {message}";
                return false;
            }

            errorCode = VisionToolErrorCode.None;
            message = string.Empty;
            return true;
        }

        protected static bool IsValidAdaptiveBlockSize(int blockSize)
        {
            return blockSize > 1 && blockSize % 2 == 1;
        }

        protected static bool TryValidateAdaptiveThreshold(
            IOpenCVPropertyBase property,
            VisionToolErrorCode invalidCode,
            out VisionToolErrorCode errorCode,
            out string message)
        {
            if (property != null
                && property.USE_ADAPTIVE_THRESHOLD
                && !IsValidAdaptiveBlockSize(property.BlockSize))
            {
                errorCode = invalidCode;
                message = $"Adaptive threshold BlockSize must be an odd number greater than 1. BlockSize={property.BlockSize}.";
                return false;
            }

            errorCode = VisionToolErrorCode.None;
            message = string.Empty;
            return true;
        }

        protected void ReplaceResultImage(Mat result)
        {
            imageResult?.Dispose();
            imageResult = result ?? new Mat();
        }

        protected Mat CreatePreprocessedImage(Rect roi, bool useRoi, IOpenCVPropertyBase property)
        {
            Mat image = useRoi ? imageSource.SubMat(roi).Clone() : imageSource.Clone();
            ApplyCommonPreprocessing(image, property);
            return image;
        }

        protected static void ApplyCommonPreprocessing(Mat image, IOpenCVPropertyBase property)
        {
            if (image == null || image.Empty() || property == null)
            {
                return;
            }

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

            if (property.USE_BITWISENOT)
            {
                Cv2.BitwiseNot(image, image);
            }
        }

        private object GetToolProperty()
        {
            return GetType().GetField("property", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(this)
                ?? GetType().GetProperty("property", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(this);
        }

        protected virtual IDictionary<string, double> CollectMetrics()
        {
            Dictionary<string, double> metrics = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            AddImageMetrics(metrics, "SourceImage", imageSource);
            AddImageMetrics(metrics, "ResultImage", OpenCvHelper.IsImageEmpty(imageResult) ? imageSource : imageResult);
            AddResultListMetrics(metrics, "results");
            AddResultListMetrics(metrics, "resultList");
            return metrics;
        }

        private static void AddImageMetrics(Dictionary<string, double> metrics, string prefix, Mat image)
        {
            if (metrics == null || OpenCvHelper.IsImageEmpty(image))
            {
                return;
            }

            metrics[$"{prefix}Width"] = image.Width;
            metrics[$"{prefix}Height"] = image.Height;
            metrics[$"{prefix}Channels"] = image.Channels();
        }

        protected virtual IEnumerable<VisionToolOverlay> CollectOverlays()
        {
            List<VisionToolOverlay> overlays = new List<VisionToolOverlay>();
            AddResultListOverlays(overlays, "results");
            AddResultListOverlays(overlays, "resultList");
            return overlays;
        }

        private void AddResultListMetrics(Dictionary<string, double> metrics, string memberName)
        {
            object value = GetType().GetField(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(this)
                ?? GetType().GetProperty(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(this);

            if (!(value is IEnumerable enumerable) || value is string)
            {
                return;
            }

            List<object> items = enumerable.Cast<object>().Where(item => item != null).ToList();
            metrics["ResultCount"] = items.Count;
            AddNumericAggregate(metrics, items, "Area", "Area");
            AddNumericAggregate(metrics, items, "Score", "Score");
            AddNumericAggregate(metrics, items, "Angle", "Angle");
            AddNumericAggregate(metrics, items, "meanValue", "MeanValue");
            AddNumericAggregate(metrics, items, "EdgeCount", "EdgeCount");
            AddNumericAggregate(metrics, items, "EdgePointCount", "EdgePointCount");
            AddNestedCount(metrics, items, "Results_List", "EdgeCount");
            AddNestedCount(metrics, items, "edgeList", "EdgePointCount");
        }

        private static void AddNumericAggregate(Dictionary<string, double> metrics, IList<object> items, string propertyName, string metricName)
        {
            List<double> values = items
                .Select(item => ReadDoubleProperty(item, propertyName))
                .Where(value => value.HasValue)
                .Select(value => value.Value)
                .ToList();

            if (values.Count == 0)
            {
                return;
            }

            metrics[$"{metricName}Min"] = values.Min();
            metrics[$"{metricName}Max"] = values.Max();
            metrics[$"{metricName}Avg"] = values.Average();
        }

        private static void AddNestedCount(Dictionary<string, double> metrics, IList<object> items, string propertyName, string metricName)
        {
            int count = 0;
            foreach (object item in items)
            {
                object value = item.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(item);
                if (value is IEnumerable enumerable && !(value is string))
                {
                    count += enumerable.Cast<object>().Count();
                }
            }

            if (count > 0)
            {
                metrics[metricName] = count;
            }
        }

        private static double? ReadDoubleProperty(object item, string propertyName)
        {
            object value = item.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(item);
            if (value == null)
            {
                return null;
            }

            try
            {
                return Convert.ToDouble(value);
            }
            catch
            {
                return null;
            }
        }

        private void AddResultListOverlays(List<VisionToolOverlay> overlays, string memberName)
        {
            object value = GetType().GetField(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(this)
                ?? GetType().GetProperty(memberName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(this);

            if (!(value is IEnumerable enumerable) || value is string)
            {
                return;
            }

            int fallbackIndex = 1;
            foreach (object item in enumerable.Cast<object>().Where(item => item != null))
            {
                int index = ReadIntProperty(item, "Index") ?? ReadIntProperty(item, "index") ?? fallbackIndex;
                string label = CreateOverlayLabel(index, item);
                RectangleF? bounds = ReadRectangleProperty(item, "Bounding");
                PointF? center = ReadPointProperty(item, "Center");
                double angle = ReadDoubleProperty(item, "Angle") ?? 0d;

                if (bounds.HasValue)
                {
                    overlays.Add(new VisionToolOverlay
                    {
                        Kind = VisionToolOverlayKind.Rectangle,
                        Label = label,
                        Bounds = bounds.Value,
                        Center = center ?? CenterOf(bounds.Value),
                        Angle = angle
                    });
                }
                else if (center.HasValue)
                {
                    overlays.Add(new VisionToolOverlay
                    {
                        Kind = VisionToolOverlayKind.Point,
                        Label = label,
                        Center = center.Value,
                        Angle = angle
                    });
                }

                List<PointF> points = ReadPointListProperty(item, "edgeList");
                if (points.Count > 0)
                {
                    VisionToolOverlay pointOverlay = new VisionToolOverlay
                    {
                        Kind = VisionToolOverlayKind.Points,
                        Label = string.IsNullOrWhiteSpace(label) ? "Edges" : label
                    };
                    pointOverlay.Points.AddRange(points);
                    overlays.Add(pointOverlay);
                }

                LineOverlayGeometry? line = ReadLineProperty(item, "FitLine");
                if (line.HasValue)
                {
                    overlays.Add(new VisionToolOverlay
                    {
                        Kind = VisionToolOverlayKind.Line,
                        Label = CreateLineOverlayLabel(index, item),
                        Start = line.Value.Start,
                        End = line.Value.End,
                        Center = CenterOf(line.Value.Start, line.Value.End)
                    });
                }

                fallbackIndex++;
            }
        }

        private static string CreateOverlayLabel(int index, object item)
        {
            List<string> parts = new List<string> { $"#{index}" };
            double? area = ReadDoubleProperty(item, "Area");
            double? score = ReadDoubleProperty(item, "Score");
            double? mean = ReadDoubleProperty(item, "meanValue");
            double? angle = ReadDoubleProperty(item, "Angle");
            PointF? center = ReadPointProperty(item, "Center");
            int? edgeCount = ReadEnumerableCount(item, "Results_List") ?? ReadEnumerableCount(item, "edgeList");

            if (score.HasValue)
            {
                parts.Add($"S:{score.Value:0.#}");
            }

            if (area.HasValue)
            {
                parts.Add($"A:{area.Value:0}");
            }

            if (mean.HasValue)
            {
                parts.Add($"M:{mean.Value:0.#}");
            }

            if (angle.HasValue && Math.Abs(angle.Value) > 0.000001)
            {
                parts.Add($"Ang:{angle.Value:0.#}");
            }

            if (edgeCount.HasValue && edgeCount.Value > 0)
            {
                parts.Add($"E:{edgeCount.Value}");
            }

            if (center.HasValue)
            {
                parts.Add($"C:{center.Value.X:0},{center.Value.Y:0}");
            }

            return string.Join(" ", parts);
        }

        private static string CreateLineOverlayLabel(int index, object item)
        {
            int? edgeCount = ReadEnumerableCount(item, "Results_List") ?? ReadEnumerableCount(item, "edgeList");
            List<string> parts = new List<string> { $"#{index}", "Fit" };
            if (edgeCount.HasValue && edgeCount.Value > 0)
            {
                parts.Add($"E:{edgeCount.Value}");
            }

            return string.Join(" ", parts);
        }

        private static RectangleF? ReadRectangleProperty(object item, string propertyName)
        {
            object value = ReadPropertyValue(item, propertyName);
            if (value == null)
            {
                return null;
            }

            float? x = ReadFloatProperty(value, "X");
            float? y = ReadFloatProperty(value, "Y");
            float? width = ReadFloatProperty(value, "Width");
            float? height = ReadFloatProperty(value, "Height");
            if (!x.HasValue || !y.HasValue || !width.HasValue || !height.HasValue)
            {
                return null;
            }

            if (width.Value <= 0 || height.Value <= 0)
            {
                return null;
            }

            return new RectangleF(x.Value, y.Value, width.Value, height.Value);
        }

        private static PointF? ReadPointProperty(object item, string propertyName)
        {
            object value = ReadPropertyValue(item, propertyName);
            if (value == null)
            {
                return null;
            }

            return ReadPointValue(value);
        }

        private static List<PointF> ReadPointListProperty(object item, string propertyName)
        {
            object value = ReadPropertyValue(item, propertyName);
            if (!(value is IEnumerable enumerable) || value is string)
            {
                return new List<PointF>();
            }

            return enumerable
                .Cast<object>()
                .Select(ReadPointValue)
                .Where(point => point.HasValue)
                .Select(point => point.Value)
                .ToList();
        }

        private static PointF? ReadPointValue(object value)
        {
            float? x = ReadFloatProperty(value, "X");
            float? y = ReadFloatProperty(value, "Y");
            if (!x.HasValue || !y.HasValue)
            {
                return null;
            }

            return new PointF(x.Value, y.Value);
        }

        private static object ReadPropertyValue(object item, string propertyName)
        {
            if (item == null || string.IsNullOrWhiteSpace(propertyName))
            {
                return null;
            }

            Type type = item.GetType();
            PropertyInfo property = type.GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (property != null)
            {
                return property.GetValue(item);
            }

            return type.GetField(propertyName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(item);
        }

        private static int? ReadIntProperty(object item, string propertyName)
        {
            object value = ReadPropertyValue(item, propertyName);
            if (value == null)
            {
                return null;
            }

            try
            {
                return Convert.ToInt32(value);
            }
            catch
            {
                return null;
            }
        }

        private static float? ReadFloatProperty(object item, string propertyName)
        {
            object value = ReadPropertyValue(item, propertyName);
            if (value == null)
            {
                return null;
            }

            try
            {
                return Convert.ToSingle(value);
            }
            catch
            {
                return null;
            }
        }

        private static int? ReadEnumerableCount(object item, string propertyName)
        {
            object value = ReadPropertyValue(item, propertyName);
            if (!(value is IEnumerable enumerable) || value is string)
            {
                return null;
            }

            return enumerable.Cast<object>().Count();
        }

        private static LineOverlayGeometry? ReadLineProperty(object item, string propertyName)
        {
            object value = ReadPropertyValue(item, propertyName);
            if (value == null)
            {
                return null;
            }

            if (TryReadLinePoints(value, "P1", "P2", out LineOverlayGeometry line)
                || TryReadLinePoints(value, "Point1", "Point2", out line)
                || TryReadLinePoints(value, "Start", "End", out line)
                || TryReadLinePoints(value, "StartPoint", "EndPoint", out line)
                || TryReadLinePoints(value, "Begin", "End", out line))
            {
                return line;
            }

            return null;
        }

        private static bool TryReadLinePoints(object value, string startMember, string endMember, out LineOverlayGeometry line)
        {
            line = default;
            object start = ReadPropertyValue(value, startMember);
            object end = ReadPropertyValue(value, endMember);
            PointF? startPoint = ReadPointValue(start);
            PointF? endPoint = ReadPointValue(end);
            if (!startPoint.HasValue || !endPoint.HasValue)
            {
                return false;
            }

            if (Math.Abs(startPoint.Value.X - endPoint.Value.X) < 0.000001
                && Math.Abs(startPoint.Value.Y - endPoint.Value.Y) < 0.000001)
            {
                return false;
            }

            line = new LineOverlayGeometry
            {
                Start = startPoint.Value,
                End = endPoint.Value
            };
            return true;
        }

        private static PointF CenterOf(RectangleF bounds)
        {
            return new PointF(bounds.X + bounds.Width / 2f, bounds.Y + bounds.Height / 2f);
        }

        private static PointF CenterOf(PointF start, PointF end)
        {
            return new PointF((start.X + end.X) / 2f, (start.Y + end.Y) / 2f);
        }

        private struct LineOverlayGeometry
        {
            public PointF Start { get; set; }
            public PointF End { get; set; }
        }

    }
}
