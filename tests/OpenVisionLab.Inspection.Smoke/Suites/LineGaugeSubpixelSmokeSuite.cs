using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Xml;
using System.Xml.Serialization;
using OpenCvSharp;
using OpenVisionLab.Core.Geometry2D;
using OpenVisionLab.Vision2D.Pipeline;
using OpenVisionLab.Vision2D.Property;
using OpenVisionLab.Vision2D.Result;
using OpenVisionLab.Vision2D.Tool;
using static OpenVisionLab.Core.FormulaUtil;
using static OpenVisionLab.Inspection.Smoke.SmokeAssert;

namespace OpenVisionLab.Inspection.Smoke
{
    internal static class LineGaugeSubpixelSmokeSuite
    {
        internal static IEnumerable<SmokeCase> Cases()
        {
            yield return new SmokeCase("LineGauge subpixel recovers independent fractional edges in all directions and polarities", TestProfiles);
            yield return new SmokeCase("LineGauge subpixel retains original gray evidence through ROI threshold inversion and color", TestPreprocessing);
            yield return new SmokeCase("LineGauge subpixel restores XML settings and exposes acceptance metrics without executing restoration", TestRestore);
            yield return new SmokeCase("LineGauge subpixel rejects border profiles invalid settings and cancellation with recovery", TestRecovery);
            yield return new SmokeCase("LineGauge subpixel evidence remains immutable after source result and tool lifetime changes", TestSnapshot);
            yield return new SmokeCase("LineGauge subpixel clears stale and cancelled evidence at execution boundaries with recovery", TestExecutionBoundaries);
            yield return new SmokeCase("LineGauge double fit preserves analytic precision and rejects nonfinite degenerate inputs", TestDoubleFit);
        }

        internal static Mat Profile(int width, int height, bool projectsX, double intercept, double slope = 0.03, double blur = 1.6, bool increasing = true, int seed = 0)
        {
            Mat image = new Mat(height, width, MatType.CV_8UC1);
            Random random = new Random(seed);
            for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                double scan = projectsX ? y : x, axis = projectsX ? x : y;
                double value = 120 + (increasing ? 80 : -80) * Math.Tanh((axis - (intercept + slope * scan)) / blur);
                if (seed != 0) value += (random.NextDouble() - 0.5) * 0.8;
                image.Set(y, x, (byte)Math.Max(0, Math.Min(255, Math.Round(value))));
            }
            return image;
        }
        internal static LineGaugeToolProperty Settings(PROJECTION_DIR direction = PROJECTION_DIR.X_LTOR) => new LineGaugeToolProperty
        {
            CvROI = new Rect(0, 0, 149, 121), PRJ_DIR = direction, PRJ_PORALITY = PROJECTION_POLARITY.ALL,
            CONTRAST = 4, THICKNESS = 2, SAMPLING_STEP = 1, UseSubpixelMeasurement = true, SubpixelProfileRadius = 6
        };
        private static bool ProjectsX(PROJECTION_DIR direction) => direction == PROJECTION_DIR.X_LTOR || direction == PROJECTION_DIR.X_RTOL;
        private static bool Forward(PROJECTION_DIR direction) => direction == PROJECTION_DIR.X_LTOR || direction == PROJECTION_DIR.Y_TTOB;
        private static void Check(LineGaugeSubpixelEvidence evidence, double intercept, double slope)
        {
            Require(evidence != null && evidence.HasFit && evidence.SampleCount >= 3, "Subpixel fit is missing.");
            double maximum = evidence.Points.Max(point => Math.Abs(evidence.ProjectsX
                ? point.X - intercept - slope * point.Y : point.Y - intercept - slope * point.X));
            // Quantized tanh profiles with the authored blur/radius have a 1/8-pixel analytic interpolation gate; this is not a sensor tolerance.
            Require(maximum <= 0.125, "Fractional edge error exceeds the independent synthetic gate: " + maximum);
            Require(evidence.OrthogonalRmse <= 0.125, "Fit residual is inconsistent with the analytic line.");
            RequireApproximately(evidence.Slope, slope, 0.002, "Double fit lost the slope.");
        }
        private static void TestProfiles()
        {
            foreach (PROJECTION_DIR direction in new[] { PROJECTION_DIR.X_LTOR, PROJECTION_DIR.X_RTOL, PROJECTION_DIR.Y_TTOB, PROJECTION_DIR.Y_BTOT })
            foreach (PROJECTION_POLARITY polarity in new[] { PROJECTION_POLARITY.ALL, PROJECTION_POLARITY.BTOW, PROJECTION_POLARITY.WTOB })
            foreach (double blur in new[] { 0.8, 1.6, 2.2 })
            foreach (double intercept in new[] { 23.125, 47.25, 73.875 })
            {
                bool increasing = polarity == PROJECTION_POLARITY.ALL || (polarity == PROJECTION_POLARITY.BTOW) == Forward(direction);
                using Mat source = Profile(149, 121, ProjectsX(direction), intercept, blur: blur, increasing: increasing);
                var property = Settings(direction); property.PRJ_PORALITY = polarity;
                using var tool = new LineGaugeTool(); tool.SetProperty(property);
                using VisionToolResult result = tool.Execute(source);
                Require(result.Success, "Profile failed: " + direction + "/" + polarity + "/" + blur + ": " + result.Message);
                Check(tool.resultList.Single().SubpixelEvidence, intercept, 0.03);
                var integerPoints = tool.resultList.Single().edgeList.ToArray(); var integerLine = tool.resultList.Single().FitLine;
                property.UseSubpixelMeasurement = false;
                using VisionToolResult baseline = tool.Execute(source);
                Require(baseline.Success && tool.resultList.Single().SubpixelEvidence == null
                    && tool.resultList.Single().edgeList.SequenceEqual(integerPoints)
                    && tool.resultList.Single().FitLine.Start == integerLine.Start && tool.resultList.Single().FitLine.End == integerLine.End,
                    "Opt-in evidence changed the legacy integer path.");
            }
        }
        private static void TestPreprocessing()
        {
            using Mat source = Profile(149, 121, true, 47.25, seed: 317);
            using Mat color = new Mat(); Cv2.CvtColor(source, color, ColorConversionCodes.GRAY2BGR);
            foreach (Mat image in new[] { source, color })
            foreach (bool invert in new[] { false, true })
            {
                var settings = Settings(); settings.USE_ROI = true; settings.CvROI = new Rect(11, 13, 113, 91);
                settings.USE_THRESHOLD = true; settings.THRESHOLD = 128; settings.USE_BITWISENOT = invert;
                using var tool = new LineGaugeTool(); tool.SetProperty(settings);
                using var result = tool.Execute(image);
                Require(result.Success, "Gray/color/ROI threshold path failed: " + result.Message);
                Check(tool.resultList.Single().SubpixelEvidence, 47.25, 0.03);
                Require(tool.resultList.Single().SubpixelEvidence.Points.All(point => point.Y >= 13 && point.Y < 104), "ROI offset was lost.");
            }
            using Mat padded = source.SubMat(new Rect(11, 13, 113, 91));
            var local = Settings(); local.CvROI = new Rect(0, 0, padded.Cols, padded.Rows);
            using var localTool = new LineGaugeTool(); localTool.SetProperty(local);
            using var localResult = localTool.Execute(padded);
            Require(localResult.Success, "Non-contiguous source failed.");
            Check(localTool.resultList.Single().SubpixelEvidence, 47.25 + 0.03 * 13 - 11, 0.03);
            var multi = Settings(); multi.USE_MULTI_ROI = true; multi.CvROIS = new List<Rect> { new Rect(11, 13, 113, 40), new Rect(11, 63, 113, 40) };
            using var multiTool = new LineGaugeTool(); multiTool.SetProperty(multi);
            using var multiResult = multiTool.Execute(source);
            Require(multiResult.Success && multiTool.resultList.Count == 2, "Multiple ROI evidence failed.");
            foreach (var result in multiTool.resultList) Check(result.SubpixelEvidence, 47.25, 0.03);
        }
        private static void TestRestore()
        {
            var step = new VisionPipelineStep { ToolType = "lineGauge" };
            step.Parameters[nameof(LineGaugeToolProperty.CvROI)] = "0,0,149,121";
            step.Parameters[nameof(LineGaugeToolProperty.UseSubpixelMeasurement)] = "True";
            step.Parameters[nameof(LineGaugeToolProperty.SubpixelProfileRadius)] = "6";
            step.Parameters[nameof(LineGaugeToolProperty.CONTRAST)] = "4";
            step.Parameters[nameof(LineGaugeToolProperty.THICKNESS)] = "2";
            step.Parameters[nameof(LineGaugeToolProperty.SAMPLING_STEP)] = "1";
            var serializer = new XmlSerializer(typeof(VisionPipelineStep));
            using var writer = new StringWriter(); serializer.Serialize(writer, step);
            using var reader = new StringReader(writer.ToString());
            using var xml = XmlReader.Create(reader, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
            var restored = (VisionPipelineStep)serializer.Deserialize(xml);
            using var tool = (LineGaugeTool)VisionPipelineToolFactory.Create(restored);
            Require(tool.resultList.Count == 0 && ((LineGaugeToolProperty)tool.property).UseSubpixelMeasurement, "Restore executed or lost opt-in configuration.");
            using Mat source = Profile(149, 121, true, 47.25);
            using var result = tool.Execute(source);
            Require(result.Success && result.Metrics["LineGauge.SubpixelSampleCount"] > 3, "Factory did not connect subpixel execution/metrics.");
            restored.UseAcceptance = true; restored.AcceptanceMetricName = "LineGauge.SubpixelMaximumOrthogonalRmse";
            restored.UseAcceptanceMetricMaximum = true; restored.AcceptanceMetricMaximum = 0.125;
            Require(VisionPipelineAcceptanceEvaluator.Evaluate(restored, result).Passed, "Produced metric was not usable by existing acceptance.");
        }
        private static void TestRecovery()
        {
            using var tool = new LineGaugeTool(); var settings = Settings(); tool.SetProperty(settings);
            using Mat good = Profile(149, 121, true, 47.25), border = Profile(149, 121, true, 4, slope: 0);
            using (var bad = tool.Execute(border))
                Require(!bad.Success && tool.resultList.Single().SubpixelEvidence.RejectedPointCount > 0, "A truncated border window was silently admitted.");
            settings.SubpixelProfileRadius = 1;
            using (var bad = tool.Execute(good)) Require(!bad.Success, "Invalid radius was accepted.");
            settings.SubpixelProfileRadius = 6; settings.CONTRAST = double.NaN;
            using (var bad = tool.Execute(good)) Require(!bad.Success, "Non-finite gray contrast was accepted.");
            settings.CONTRAST = 4;
            using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
            RequireThrows<OperationCanceledException>(() => tool.Execute(good, cancellation.Token));
            using var valid = tool.Execute(good, CancellationToken.None);
            Require(valid.Success, "Recovery failed."); Check(tool.resultList.Single().SubpixelEvidence, 47.25, 0.03);
        }
        private sealed class CancelAtRunLineGauge : LineGaugeTool
        {
            internal CancellationTokenSource Cancellation;
            internal bool BeforeRun, RunEntered;
            public override void Run()
            {
                RunEntered = true;
                if (BeforeRun) Cancellation?.Cancel();
                base.Run();
                if (!BeforeRun) Cancellation?.Cancel();
            }
        }
        private static void TestExecutionBoundaries()
        {
            using Mat source = Profile(149, 121, true, 47.25);
            using var tool = new CancelAtRunLineGauge(); var settings = Settings(); tool.SetProperty(settings);
            using (var good = tool.Execute(source)) Require(good.Success, "Boundary control failed.");
            var saved = tool.resultList.Single().SubpixelEvidence;
            using (var invalid = tool.Execute(null))
                Require(!invalid.Success && tool.resultList.Count == 0 && saved.HasFit, "Invalid input exposed stale evidence or damaged an earlier snapshot.");
            foreach (bool before in new[] { true, false })
            {
                using var cancellation = new CancellationTokenSource();
                tool.Cancellation = cancellation; tool.BeforeRun = before; tool.RunEntered = false;
                RequireThrows<OperationCanceledException>(() => tool.Execute(source, cancellation.Token));
                Require(tool.RunEntered && tool.resultList.Count == 0, "Cancellation at Run or before publication exposed output.");
                tool.Cancellation = null;
                using var recovered = tool.Execute(source, CancellationToken.None);
                Require(recovered.Success, "Run cancellation recovery failed."); Check(tool.resultList.Single().SubpixelEvidence, 47.25, 0.03);
            }
            using (var cancellation = new CancellationTokenSource())
            {
                cancellation.Cancel(); RequireThrows<OperationCanceledException>(() => tool.Execute(source, cancellation.Token));
                Require(tool.resultList.Count == 0, "Pre-cancel left a previous accepted result.");
            }
            using (var good = tool.Execute(source)) Require(good.Success, "Pre-cancel recovery failed.");
            settings.SubpixelProfileRadius = 1;
            using (var invalid = tool.Execute(source)) Require(!invalid.Success && tool.resultList.Count == 0, "Validation failure left old evidence.");
            settings.SubpixelProfileRadius = 6;
            using var final = tool.Execute(source); Require(final.Success && saved.HasFit, "Final valid recovery or immutable snapshot failed.");
        }
        private static void TestSnapshot()
        {
            using Mat source = Profile(149, 121, true, 47.25); var tool = new LineGaugeTool(); tool.SetProperty(Settings());
            using var result = tool.Execute(source); Require(result.Success, "Snapshot control failed.");
            var evidence = tool.resultList.Single().SubpixelEvidence; var original = evidence.Points[0];
            RequireThrows<NotSupportedException>(() => ((IList<Point2d>)evidence.Points)[0] = new Point2d(0, 0));
            tool.resultList.Single().edgeList.Clear(); source.SetTo(Scalar.All(0)); tool.Dispose();
            Require(evidence.Points[0] == original && evidence.HasFit && evidence.SampleCount > 3, "Evidence depended on mutable integer output/image/tool lifetime.");
        }
        private static void TestDoubleFit()
        {
            Point2d[] values = Enumerable.Range(0, 8).Select(i => new Point2d(1000000.0 + i * 0.25, 3.25 * (1000000.0 + i * 0.25) + 0.125)).ToArray();
            double norm = LineFittingCalculator.FindDoublePrecisionLinearLeastSquaresFit(values, out double slope, out double intercept);
            RequireApproximately(slope, 3.25, 1e-12, "Double precision slope changed.");
            RequireApproximately(intercept, 0.125, 1e-8, "Fractional intercept was truncated.");
            Require(norm <= 1e-8, "Exact affine points gained residuals.");
            RequireThrows<ArgumentException>(() => LineFittingCalculator.FindDoublePrecisionLinearLeastSquaresFit(new[] { new Point2d(1,1), new Point2d(1,2) }, out _, out _));
            RequireThrows<ArgumentException>(() => LineFittingCalculator.FindDoublePrecisionLinearLeastSquaresFit(new[] { new Point2d(1,1), new Point2d(double.NaN,2) }, out _, out _));
        }
    }
}
