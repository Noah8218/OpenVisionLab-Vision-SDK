using System;
using System.Threading;
using OpenCvSharp;
using OpenVisionLab.Inspection;
using OpenVisionLab.Vision2D;
using OpenVisionLab.Vision2D.Property;
using OpenVisionLab.Vision2D.Result;
using OpenVisionLab.Vision2D.Tool;
using OpenVisionLab.Vision3D.FeatureExtraction;
using OpenVisionLab.Vision3D.Inspection;

namespace OpenVisionLab.PackageConsumer
{
    // Shared C# 7.3 package-only checks. No SDK source or ProjectReference.
    internal static class HardeningChecks
    {
        internal static void Run()
        {
            using (Mat color = new Mat(2, 3, MatType.CV_8UC3, new Scalar(255, 0, 0)))
            using (Mat gray = OpenCvHelper.CreateGray8(color, ImageChannelOrder.Rgb))
                Require(gray.At<byte>(0, 0) == 76 && color.Channels() == 3, "Explicit RGB snapshot contract failed.");

            using (Mat template = new Mat(48, 48, MatType.CV_8UC1, Scalar.All(17)))
            using (Mat source = new Mat(112, 144, MatType.CV_8UC1, Scalar.All(17)))
            using (Mat absent = new Mat(source.Size(), MatType.CV_8UC1, Scalar.All(33)))
            using (EdgeBasedTemplateMatchingTool tool = new EdgeBasedTemplateMatchingTool())
            {
                Cv2.Rectangle(template, new Rect(7, 6, 31, 34), Scalar.All(225), -1);
                Cv2.Rectangle(template, new Rect(22, 6, 16, 13), Scalar.All(17), -1);
                using (Mat region = new Mat(source, new Rect(43, 29, 48, 48))) template.CopyTo(region);
                source.Set(0, 0, (byte)21);
                tool.SetProperty(new EdgeBasedTemplateMatchingToolProperty { SCORE_MIN = 0.6, USE_DRAW_IMAGE = false, USE_FIND_ANGLE = false, SEARCH_STEP = 2, MAX_TEMPLATE_POINTS = 200 });
                tool.SetTemplateImage(template);
                using (VisionToolResult a = tool.Execute(source))
                using (VisionToolResult b = tool.Execute(absent))
                using (VisionToolResult c = tool.Execute(source, CancellationToken.None))
                {
                    Require(a.Success && !b.Success && b.ErrorCode == VisionToolErrorCode.MatchingNoResult && c.Success, "Success/NoMatch/recovery changed.");
                    Require(a.ResultImage.At<byte>(0, 0) == 21 && b.ResultImage.At<byte>(0, 0) == 33, "Result frames were mixed.");
                    tool.Dispose();
                    Require(!a.ResultImage.IsDisposed && a.ResultImage.At<byte>(0, 0) == 21, "Snapshot must outlive Tool.");
                }
            }

            HeightFieldPlaneFitSample[] reference = {
                new HeightFieldPlaneFitSample(new ThreeDPoint(0, 0, 0), 100),
                new HeightFieldPlaneFitSample(new ThreeDPoint(1, 0, 0), 102),
                new HeightFieldPlaneFitSample(new ThreeDPoint(0, 0, 1), 103)
            };
            var measured = new[] { new HeightFieldPlaneFitSample(new ThreeDPoint(0.5, 700, 0.5), 107.5) };
            DualSurfaceThicknessInspectionResult thickness = new DualSurfaceThicknessInspectionTool().Execute(reference, measured, 4, 6, 1, CancellationToken.None);
            Require(thickness.Success && thickness.Decision == DualSurfaceThicknessDecision.Pass && Math.Abs(thickness.Mean - 5) < 1e-12, "Raw reference plane was mixed with display Y.");
            using (CancellationTokenSource cancel = new CancellationTokenSource())
            {
                cancel.Cancel();
                bool observed = false;
                try { new CombinedInspectionRunner().Run(null, null, null, cancel.Token); }
                catch (OperationCanceledException) { observed = true; }
                Require(observed, "Combined pre-cancellation contract failed.");
                ICancellableThreeDInspectionTool supported = new ThicknessInspectionTool();
                observed = false;
                try { supported.Execute(null, cancel.Token); }
                catch (OperationCanceledException) { observed = true; }
                Require(observed, "Optional 3D cancellation contract failed.");
            }
            using (Mat texture = new Mat(256, 256, MatType.CV_8UC1))
            using (SiftTool feature = new SiftTool())
            {
                Random random = new Random(8218);
                for (int y = 0; y < texture.Height; y++)
                for (int x = 0; x < texture.Width; x++) texture.Set(y, x, (byte)random.Next(256));
                feature.SetProperty(new SiftToolProperty());
                feature.SetTemplateImage(texture);
                using (VisionToolResult result = feature.Execute(texture))
                {
                    double sift, orb;
                    Require(result.Metrics.TryGetValue("FeatureDetector.Sift", out sift) && result.Metrics.TryGetValue("FeatureDetector.OrbFallback", out orb) && sift + orb == 1, "Actual detector must be explicit.");
                    Require(result.Success, "Textured self-match must recover with the supported detector: " + result.Message);
                    Console.WriteLine("DETECTOR|" + (sift == 1 ? "SIFT|L2" : "ORB fallback|Hamming") + "|OpenCV=" + Cv2.GetVersionString());
                    // A Host requiring SIFT must reject orb == 1; the SDK keeps its legacy fallback default.
                }
            }
            CheckCaliper();
            Console.WriteLine("PASS|hardening-package-consumer");
        }

        private static void CheckCaliper()
        {
            using (Mat image = new Mat(96, 96, MatType.CV_8UC1))
            using (RotatedCaliperTool tool = new RotatedCaliperTool())
            {
                byte[] transition = { 10, 26, 56, 92, 126, 150 };
                for (int y = 0; y < image.Height; y++)
                for (int x = 0; x < image.Width; x++) image.Set(y, x, transition[Math.Max(0, Math.Min(5, y - 40))]);
                RotatedCaliperToolProperty settings = new RotatedCaliperToolProperty
                {
                    CenterX = 48, CenterY = 42.5, StationCount = 9, StationPitch = 3,
                    ScanLength = 5, ScanSampleCount = 6, TangentWidth = 0, TangentSampleCount = 1,
                    Polarity = CaliperPolarity.Rising, QualityGateEnabled = false
                };
                tool.SetProperty(settings);
                using (VisionToolResult result = tool.Execute(image))
                {
                    CaliperMeasurementEvidence retained = result.CaliperMeasurement;
                    Require(result.Success && retained != null && retained.MeasurementEstablished && retained.Acceptance == CaliperAcceptance.NotEvaluated,
                        "Caliper geometry must not become an unconfigured quality Pass.");
                    Require(Math.Abs(retained.Line.CenterY - 42.75) < 1e-9 && retained.UsedPointIds.Count == 9,
                        "Caliper forward-gradient subpixel oracle changed.");
                    settings.QualityGateEnabled = true; settings.MinimumPointCount = 10;
                    tool.SetProperty(settings);
                    using (VisionToolResult rejected = tool.Execute(image))
                        Require(rejected.Success && rejected.CaliperMeasurement.MeasurementEstablished && rejected.CaliperMeasurement.Acceptance == CaliperAcceptance.Rejected,
                            "Caliper explicit quality rejection must remain distinct from geometric failure.");
                    using (Mat blank = new Mat(image.Size(), MatType.CV_8UC1, Scalar.All(10)))
                    using (VisionToolResult absent = tool.Execute(blank)) Require(!absent.Success, "Caliper absence reused a fit.");
                    tool.Dispose();
                    Require(retained.Line.CenterY == result.CaliperMeasurement.Line.CenterY && retained.Stations.Count == 9 && result.ResultImage.At<byte>(42, 48) == 56,
                        "Caliper evidence/image snapshot did not survive Tool disposal and later frames.");
                }
                IVisionTool external = new ExternalLegacyTool();
                Require(!(external is ICancellableVisionTool), "An old external IVisionTool implementation acquired a required interface member.");
                using (VisionToolResult externalResult = external.Execute(image)) Require(externalResult.Success, "Old external Tool did not execute.");
            }
            using (Mat image = new Mat(96, 96, MatType.CV_8UC1))
            using (RotatedCaliperTool circle = new RotatedCaliperTool())
            {
                for (int y = 0; y < image.Height; y++)
                for (int x = 0; x < image.Width; x++)
                {
                    double radius = Math.Sqrt((x - 48.0) * (x - 48.0) + (y - 48.0) * (y - 48.0));
                    image.Set(y, x, (byte)Math.Round(20 + 100 * (1 + Math.Tanh((radius - 20) / 1.2))));
                }
                circle.SetProperty(new RotatedCaliperToolProperty
                {
                    Shape = CaliperShape.Circle, CenterX = 48, CenterY = 48, Radius = 19.7,
                    StationCount = 24, AngularPitchDegrees = 15, ScanLength = 10, ScanSampleCount = 20,
                    TangentWidth = 0, TangentSampleCount = 1, Polarity = CaliperPolarity.Rising
                });
                using (VisionToolResult result = circle.Execute(image))
                    Require(result.Success && result.CaliperMeasurement.Circle != null && Math.Abs(result.CaliperMeasurement.Circle.Radius - 20) < 0.5
                        && result.CaliperMeasurement.Acceptance == CaliperAcceptance.NotEvaluated, "Package circle geometry or quality contract failed.");
            }
            Console.WriteLine("PASS|caliper-package-consumer|CSharp7.3-compatible");
        }

        // Implements only the pre-existing two-member interface; compiles under the net48 C# 7.3 gate.
        private sealed class ExternalLegacyTool : IVisionTool
        {
            public string Name { get { return "External legacy Tool"; } }
            public VisionToolResult Execute(Mat source) { return VisionToolResult.Passed(source.Clone(), TimeSpan.Zero); }
        }

        private static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }
    }
}
