using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using OpenCvSharp;
using OpenVisionLab.Vision2D;
using OpenVisionLab.Vision2D.Pipeline;
using OpenVisionLab.Vision2D.Property;
using OpenVisionLab.Vision2D.Result;
using OpenVisionLab.Vision2D.Tool;
using static OpenVisionLab.Inspection.Smoke.SmokeAssert;

namespace OpenVisionLab.Inspection.Smoke
{
    internal static class EdgePoseRefinementSmokeSuite
    {
        internal static IEnumerable<SmokeCase> Cases()
        {
            yield return new SmokeCase("Pose refinement reduces independent off-grid position angle and scale errors", TestOffGrid);
            yield return new SmokeCase("Pose refinement respects bounds and recovers after invalid settings absence and cancellation", TestRecovery);
            yield return new SmokeCase("Pose refinement preserves polarity hybrid ROI scores and owned images", TestPolarityAndRoi);
            yield return new SmokeCase("Pose refinement preserves spatial and pose ambiguity and multiple-object identity", TestAmbiguityAndMultiple);
            yield return new SmokeCase("Pose refinement restores Tool options through existing XML teaching without executing", TestTeachingReplay);
            yield return new SmokeCase("Pose refinement observes cancellation at a bounded native attempt and recovers", TestNativeCancellation);
        }

        internal static Mat CreateTemplate()
        {
            Mat image = new Mat(64, 64, MatType.CV_8UC1, Scalar.All(17));
            Cv2.Rectangle(image, new Rect(8, 7, 42, 46), Scalar.All(225), -1);
            Cv2.Rectangle(image, new Rect(29, 7, 21, 16), Scalar.All(17), -1);
            Cv2.Circle(image, new Point(20, 37), 7, Scalar.All(17), -1);
            return image;
        }

        internal static Mat CreateSource(Mat template, double angle, double scale, double x = 43.25D, double y = 29.4D)
        {
            Mat source = new Mat(112, 144, MatType.CV_8UC1, Scalar.All(17));
            using Mat warp = Cv2.GetRotationMatrix2D(new Point2f(31.5F, 31.5F), angle, scale);
            warp.Set(0, 2, warp.At<double>(0, 2) + x);
            warp.Set(1, 2, warp.At<double>(1, 2) + y);
            Cv2.WarpAffine(template, source, warp, source.Size(), InterpolationFlags.Linear, BorderTypes.Constant, Scalar.All(17));
            return source;
        }

        internal static EdgeBasedTemplateMatchingTool CreateTool(Mat template, bool refine)
        {
            EdgeBasedTemplateMatchingTool tool = new EdgeBasedTemplateMatchingTool { UsePoseRefinement = refine };
            tool.SetProperty(new EdgeBasedTemplateMatchingToolProperty
            {
                SCORE_MIN = 0.6D, USE_DRAW_IMAGE = false, SEARCH_STEP = 2, USE_POSITION_REFINE = true,
                USE_SUBPIXEL_REFINE = true, USE_FIND_ANGLE = true, FIND_ANGLE_MIN = -5, FIND_ANGLE_MAX = 5, FIND_ANGLE = 5D,
                USE_FIND_SCALE = true, FIND_SCALE_MIN = 0.95D, FIND_SCALE_MAX = 1.05D, FIND_SCALE_STEP = 0.05D,
                MAX_TEMPLATE_POINTS = 400, MIN_GRADIENT_MAGNITUDE = 5D
            });
            tool.SetTemplateImage(template);
            return tool;
        }

        internal static Point2d ExpectedPoint(Point2d point, double angle, double scale, double x = 43.25D, double y = 29.4D)
        {
            double cos = Math.Cos(angle * Math.PI / 180D);
            double sin = Math.Sin(angle * Math.PI / 180D);
            double px = point.X - 31.5D;
            double py = point.Y - 31.5D;
            return new Point2d(31.5D + x + scale * (cos * px + sin * py), 31.5D + y + scale * (-sin * px + cos * py));
        }

        internal static double Distance(Point2d first, Point2d second) => Math.Sqrt(Math.Pow(first.X - second.X, 2) + Math.Pow(first.Y - second.Y, 2));

        private static void TestOffGrid()
        {
            using Mat template = CreateTemplate();
            foreach ((double angle, double scale) in new[] { (2.5D, 1.025D), (-2.5D, 0.975D) })
            {
                using Mat source = CreateSource(template, angle, scale);
                using EdgeBasedTemplateMatchingTool coarse = CreateTool(template, false);
                using EdgeBasedTemplateMatchingTool fine = CreateTool(template, true);
                using VisionToolResult before = coarse.Execute(source);
                using VisionToolResult after = fine.Execute(source);
                Require(before.Success && after.Success, "Authored off-grid target must match: " + before.Message + " / " + after.Message);
                MatchingResult first = coarse.results.Single();
                MatchingResult last = fine.results.Single();
                VerifyFinalEdgeScore(template, source, last, after.EdgeBasedMatchingDiagnostics);
                Point2d expected = ExpectedPoint(new Point2d(32, 32), angle, scale);
                double oldPosition = Distance(new Point2d(first.Center.X, first.Center.Y), expected);
                double newPosition = Distance(new Point2d(last.Center.X, last.Center.Y), expected);
                Console.WriteLine(FormattableString.Invariant($"off-grid {angle}/{scale}: coarse {first.Angle}/{first.Scale} XY={oldPosition}; fine {last.Angle}/{last.Scale} XY={newPosition}; attempts={after.Metrics["PoseRefinement.AttemptCount"]} accepted={after.Metrics["PoseRefinement.AcceptedCount"]}"));
                Require(Math.Abs(last.Angle - angle) < 0.15D && Math.Abs(last.Angle - angle) < Math.Abs(first.Angle - angle), "Continuous angle must improve against the independent authored angle.");
                Require(Math.Abs(last.Scale - scale) < 0.003D && Math.Abs(last.Scale - scale) < Math.Abs(first.Scale - scale), "Continuous scale must improve against the independent authored scale.");
                Require(newPosition < 0.25D && newPosition < oldPosition, "Continuous position must improve against the independent authored center.");
                Require(after.Metrics["PoseRefinement.AcceptedCount"] >= 1 && last.Score == last.EdgeScore && last.FinalScore == last.EdgeScore && double.IsNaN(last.ImageScore), "Published scores must use the final continuous edge pose.");
                MatchingCoordinateTransform transform = new MatchingCoordinateTransform(new Point2d(32, 32), new Point2d(last.Center.X, last.Center.Y), last.Angle, last.Scale);
                Point2d expectedMark = ExpectedPoint(new Point2d(20, 37), angle, scale);
                Require(Distance(transform.TransformPoint(new Point2d(20, 37)), expectedMark) < 0.3D, "A downstream ROI mark must follow the authored transformation.");
                fine.UsePoseUniquenessValidation = true;
                fine.property.USE_COARSE_TO_FINE_ANGLE_SEARCH = true;
                fine.property.USE_PYRAMID_POSITION_PROPOSAL = true;
                using VisionToolResult repeated = fine.Execute(source);
                Require(repeated.Success && fine.results.Single().Angle == last.Angle && fine.results.Single().Scale == last.Scale && fine.results.Single().Score == last.Score, "Repeated native refinement must preserve the selected pose and score.");
                RequireApproximately(repeated.EdgeBasedMatchingDiagnostics.SelectedCandidate.Angle, fine.results.Single().Angle, 0, "Pose diagnostics must refer to the published selected candidate.");
            }
        }

        private static void VerifyFinalEdgeScore(Mat template, Mat source, MatchingResult match, EdgeBasedMatchingDiagnosticEvidence evidence)
        {
            using Mat templateDx = new Mat();
            using Mat templateDy = new Mat();
            using Mat sourceDx = new Mat();
            using Mat sourceDy = new Mat();
            Cv2.Sobel(template, templateDx, MatType.CV_32F, 1, 0, 3);
            Cv2.Sobel(template, templateDy, MatType.CV_32F, 0, 1, 3);
            Cv2.Sobel(source, sourceDx, MatType.CV_32F, 1, 0, 3);
            Cv2.Sobel(source, sourceDy, MatType.CV_32F, 0, 1, 3);
            using Mat unitDx = new Mat(source.Size(), MatType.CV_32FC1);
            using Mat unitDy = new Mat(source.Size(), MatType.CV_32FC1);
            for (int y = 0; y < source.Height; y++)
            for (int x = 0; x < source.Width; x++)
            {
                double dx = sourceDx.At<float>(y, x);
                double dy = sourceDy.At<float>(y, x);
                double magnitude = Math.Sqrt(dx * dx + dy * dy);
                unitDx.Set(y, x, magnitude >= 5D ? (float)(dx / magnitude) : 0F);
                unitDy.Set(y, x, magnitude >= 5D ? (float)(dy / magnitude) : 0F);
            }
            // Independent native bilinear sampler and published model points verify the score contract.
            using Mat sampledDx = new Mat();
            using Mat sampledDy = new Mat();
            double cos = Math.Cos(match.Angle * Math.PI / 180D);
            double sin = Math.Sin(match.Angle * Math.PI / 180D);
            double sum = 0D;
            foreach (System.Drawing.PointF point in evidence.ModelPoints)
            {
                double px = point.X - 32D;
                double py = point.Y - 32D;
                Point2f position = new Point2f((float)(match.Center.X + match.Scale * (px * cos + py * sin)),
                    (float)(match.Center.Y + match.Scale * (-px * sin + py * cos)));
                Cv2.GetRectSubPix(unitDx, new Size(1, 1), position, sampledDx);
                Cv2.GetRectSubPix(unitDy, new Size(1, 1), position, sampledDy);
                double sx = sampledDx.At<float>(0, 0);
                double sy = sampledDy.At<float>(0, 0);
                double tx = templateDx.At<float>((int)point.Y, (int)point.X);
                double ty = templateDy.At<float>((int)point.Y, (int)point.X);
                double length = Math.Sqrt((sx * sx + sy * sy) * (tx * tx + ty * ty));
                if (length > 0D) sum += (sx * (tx * cos + ty * sin) + sy * (-tx * sin + ty * cos)) / length;
            }
            double signed = sum / evidence.ModelPoints.Count;
            RequireApproximately(match.EdgeScore, Math.Round(Math.Abs(signed) * 100D, 3), 0.002D, "EdgeScore must be sampled at the exact final position, angle and scale.");
            Require(match.PolarityReversed == (signed < 0D), "Global polarity must agree with the independently sampled final-pose sign.");
        }

        private static void TestRecovery()
        {
            using Mat template = CreateTemplate();
            using Mat source = CreateSource(template, 2.5D, 1.025D);
            using Mat absent = new Mat(source.Size(), source.Type(), Scalar.All(17));
            using EdgeBasedTemplateMatchingTool tool = CreateTool(template, true);
            foreach (int count in new[] { 0, 101 })
            {
                tool.PoseRefinementMaximumIterations = count;
                using VisionToolResult rejected = tool.Execute(source);
                Require(!rejected.Success && rejected.ErrorCode == VisionToolErrorCode.InvalidParameter && tool.results.Count == 0, "Invalid iteration budget must be rejected without stale matches.");
            }
            tool.UsePoseRefinement = false;
            tool.PoseRefinementEpsilon = double.NaN;
            using (VisionToolResult legacy = tool.Execute(source)) Require(legacy.Success && legacy.Metrics["PoseRefinement.AttemptCount"] == 0, "Disabled refinement must preserve legacy execution even when unused refinement settings are invalid.");
            tool.UsePoseRefinement = true;
            tool.PoseRefinementMaximumIterations = 40;
            foreach (double epsilon in new[] { 0D, -1D, double.NaN, double.PositiveInfinity })
            {
                tool.PoseRefinementEpsilon = epsilon;
                using VisionToolResult rejected = tool.Execute(source);
                Require(!rejected.Success && rejected.ErrorCode == VisionToolErrorCode.InvalidParameter, "Invalid epsilon must be rejected.");
            }
            tool.PoseRefinementEpsilon = 0.000001D;
            tool.property.USE_COARSE_TO_FINE_ANGLE_SEARCH = true;
            foreach (double step in new[] { double.NaN, double.PositiveInfinity, 0.0000001D })
            {
                tool.property.COARSE_ANGLE_STEP = step;
                using VisionToolResult rejected = tool.Execute(source);
                Require(!rejected.Success && rejected.ErrorCode == VisionToolErrorCode.InvalidParameter, "An active unrepresentable coarse grid must fail before refinement.");
            }
            tool.property.COARSE_ANGLE_STEP = 5D;
            tool.property.USE_COARSE_TO_FINE_ANGLE_SEARCH = false;
            using (VisionToolResult blank = tool.Execute(absent)) Require(!blank.Success && tool.results.Count == 0, "Absence must not retain a refined target.");
            using CancellationTokenSource canceled = new CancellationTokenSource();
            canceled.Cancel();
            OperationCanceledException exception = RequireThrows<OperationCanceledException>(() => { using VisionToolResult unused = tool.Execute(source, canceled.Token); });
            Require(exception.CancellationToken == canceled.Token && tool.results.Count == 0, "Refinement must retain the caller cancellation contract.");
            tool.property.FIND_ANGLE_MIN = 0;
            tool.property.FIND_ANGLE_MAX = 0;
            tool.property.FIND_SCALE_MIN = 1D;
            tool.property.FIND_SCALE_MAX = 1D;
            using VisionToolResult limited = tool.Execute(source);
            Require(limited.Success && tool.results.Single().Angle == 0D && tool.results.Single().Scale == 1D, "Refinement must not leave the declared single-pose range.");
            tool.property.FIND_ANGLE_MIN = -5;
            tool.property.FIND_ANGLE_MAX = 5;
            tool.property.FIND_SCALE_MIN = 0.95D;
            tool.property.FIND_SCALE_MAX = 1.05D;
            using VisionToolResult recovered = tool.Execute(source);
            Require(recovered.Success && Math.Abs(tool.results.Single().Angle - 2.5D) < 0.15D, "The same instance must recover after bounds, rejection and cancellation.");
        }

        private static void TestPolarityAndRoi()
        {
            using Mat template = CreateTemplate();
            using Mat source = CreateSource(template, -2.5D, 0.975D);
            using Mat reversed = new Mat();
            Cv2.BitwiseNot(source, reversed);
            using EdgeBasedTemplateMatchingTool tool = CreateTool(template, true);
            tool.property.ALLOW_GLOBAL_POLARITY_REVERSAL = true;
            tool.property.USE_HYBRID_VERIFY = true;
            tool.property.USE_ROI = true;
            tool.property.CvROI = new Rect(16, 12, 120, 96);
            using VisionToolResult same = tool.Execute(source);
            Require(same.Success && !tool.results.Single().PolarityReversed, "Same-polarity refined ROI must match: " + same.Message);
            MatchingResult retained = tool.results.Single();
            using VisionToolResult opposite = tool.Execute(reversed);
            Require(opposite.Success && tool.results.Single().PolarityReversed, "Global reversed-polarity refined ROI must match: " + opposite.Message);
            MatchingResult match = tool.results.Single();
            VerifyFinalEdgeScore(template, reversed, match, opposite.EdgeBasedMatchingDiagnostics);
            Require(Math.Abs(match.Angle + 2.5D) < 0.15D && Math.Abs(match.Scale - 0.975D) < 0.003D, "Polarity must be evaluated at the continuous pose.");
            RequireApproximately(match.EdgeScore, retained.EdgeScore, 0.001D, "Reversal must retain the same continuous edge evidence.");
            Require(!double.IsNaN(match.ImageScore) && match.FinalScore == Math.Round(match.EdgeScore * 0.65D + match.ImageScore * 0.35D, 3), "Hybrid scores must be recomputed for the final candidate.");
            Point2d expected = ExpectedPoint(new Point2d(32, 32), -2.5D, 0.975D);
            Require(Distance(new Point2d(match.Center.X, match.Center.Y), expected) < 0.25D, "ROI offset must be applied once after refinement.");
            Require(!source.IsDisposed && !reversed.IsDisposed && !template.IsDisposed && same.ResultImage != null && !same.ResultImage.IsDisposed, "Native refinement must preserve caller and retained result lifetimes.");
        }

        private static void TestAmbiguityAndMultiple()
        {
            using Mat template = CreateTemplate();
            using Mat one = CreateSource(template, 0D, 1D, 12D, 22D);
            using Mat source = new Mat(112, 280, MatType.CV_8UC1, Scalar.All(17));
            using (Mat first = new Mat(source, new Rect(0, 0, 144, 112))) one.CopyTo(first);
            using (Mat second = new Mat(source, new Rect(164, 22, 64, 64))) template.CopyTo(second);
            using EdgeBasedTemplateMatchingTool tool = CreateTool(template, true);
            tool.property.USE_FIND_ANGLE = false;
            tool.property.USE_FIND_SCALE = false;
            tool.property.USE_UNIQUE_MATCH_VALIDATION = true;
            using (VisionToolResult ambiguous = tool.Execute(source)) Require(!ambiguous.Success && ambiguous.ErrorCode == VisionToolErrorCode.MatchingAmbiguous && tool.results.Count == 0, "Refining two identical objects must preserve spatial ambiguity.");
            tool.property.USE_UNIQUE_MATCH_VALIDATION = false;
            tool.property.USE_FIND_SCALE = true;
            tool.property.USE_HYBRID_VERIFY = true;
            tool.property.NUM_MATCH = 2;
            using (VisionToolResult multiple = tool.Execute(source)) Require(multiple.Success && tool.results.Count == 2 && Math.Abs(tool.results[0].Center.X - tool.results[1].Center.X) > 100D, "Multiple refinement must preserve disjoint object identity.");
            using Mat square = new Mat(64, 64, MatType.CV_8UC1, Scalar.All(17));
            Cv2.Rectangle(square, new Rect(12, 12, 40, 40), Scalar.All(225), -1);
            using Mat squareSource = CreateSource(square, 0D, 1D);
            using EdgeBasedTemplateMatchingTool symmetric = CreateTool(square, true);
            symmetric.property.FIND_ANGLE_MIN = 0;
            symmetric.property.FIND_ANGLE_MAX = 270;
            symmetric.property.FIND_ANGLE = 90;
            symmetric.property.USE_FIND_SCALE = false;
            symmetric.UsePoseUniquenessValidation = true;
            using VisionToolResult refused = symmetric.Execute(squareSource);
            Require(!refused.Success && refused.ErrorCode == VisionToolErrorCode.MatchingAmbiguous, "Continuous refinement must preserve same-position symmetric pose ambiguity.");
        }

        private static void TestNativeCancellation()
        {
            using Mat template = CreateTemplate();
            using Mat source = CreateSource(template, 2.5D, 1.025D);
            using EdgeBasedTemplateMatchingTool tool = CreateTool(template, true);
            using CancellationTokenSource cancellation = new CancellationTokenSource();
            using CancellationTokenSource stopObserver = new CancellationTokenSource();
            using ManualResetEventSlim ready = new ManualResetEventSlim(false);
            System.Reflection.FieldInfo diagnosticField = typeof(EdgeBasedTemplateMatchingTool).GetField("candidateDiagnostics", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            System.Reflection.PropertyInfo attemptProperty = diagnosticField.FieldType.GetProperty("PoseRefinementAttemptCount");
            // Observe the native attempt boundary during public Execute; no private execution or state mutation is used.
            Task observer = Task.Run(() =>
            {
                ready.Set();
                while (!stopObserver.IsCancellationRequested)
                {
                    object diagnostics = diagnosticField.GetValue(tool);
                    if ((int)attemptProperty.GetValue(diagnostics) > 0)
                    {
                        cancellation.Cancel();
                        return;
                    }
                    Thread.Yield();
                }
            });
            OperationCanceledException canceled;
            try
            {
                Require(ready.Wait(TimeSpan.FromSeconds(5)), "The isolated cancellation observer did not start.");
                canceled = RequireThrows<OperationCanceledException>(() => { using VisionToolResult unused = tool.Execute(source, cancellation.Token); });
            }
            finally
            {
                stopObserver.Cancel();
                observer.GetAwaiter().GetResult();
            }
            Require(canceled.CancellationToken == cancellation.Token && tool.results.Count == 0 && !source.IsDisposed && !template.IsDisposed,
                "Cancellation at a native attempt must preserve the caller token and images without publishing partial rows.");
            using VisionToolResult recovered = tool.Execute(source);
            Require(recovered.Success && Math.Abs(tool.results.Single().Angle - 2.5D) < 0.15D, "The native cancellation path must release temporary matrices and recover on the same instance.");
        }

        private static void TestTeachingReplay()
        {
            using Mat template = CreateTemplate();
            using Mat source = CreateSource(template, 2.5D, 1.025D);
            byte[] encoded = template.ToBytes(".png");
            VisionPipelineToolDescriptor descriptor = VisionPipelineToolFactory.Descriptors.Single(item => item.ToolTypeName == typeof(EdgeBasedTemplateMatchingTool).FullName);
            VisionPipelineStep step = new VisionPipelineStep { Name = "Continuous pose", ToolType = descriptor.ToolType };
            foreach (VisionPipelineParameterDescriptor parameter in descriptor.Parameters) step.Parameters[parameter.Name] = parameter.DefaultValue;
            step.Parameters[nameof(EdgeBasedTemplateMatchingTool.UsePoseRefinement)] = "true";
            step.Parameters[nameof(EdgeBasedTemplateMatchingTool.PoseRefinementMaximumIterations)] = "35";
            step.Parameters[nameof(EdgeBasedTemplateMatchingTool.PoseRefinementEpsilon)] = "0.000002";
            step.Artifacts.Add(new VisionPipelineArtifactReference(VisionPipelineToolFactory.TemplateArtifactRole, "teaching/continuous", VisionPipelineToolFactory.EncodedImageArtifactFormat, VisionPipelineToolFactory.EncodedImageArtifactFormatVersion, Convert.ToHexString(SHA256.HashData(encoded))));
            VisionPipeline pipeline = new VisionPipeline { Name = "Continuous pose replay" };
            pipeline.Steps.Add(step);
            string xml = VisionPipelineSerializer.Serialize(pipeline);
            VisionPipeline restored = VisionPipelineSerializer.Deserialize(xml);
            using EdgeBasedTemplateMatchingTool tool = (EdgeBasedTemplateMatchingTool)VisionPipelineToolFactory.Create(restored.Steps.Single(), artifact => encoded);
            Require(tool.UsePoseRefinement && tool.PoseRefinementMaximumIterations == 35 && tool.PoseRefinementEpsilon == 0.000002D && tool.results.Count == 0, "Factory must restore all Tool options without executing.");
            Require(VisionPipelineSerializer.Serialize(restored) == xml, "Existing XML round trip must retain continuous settings and teaching identity.");
            step.Parameters.Remove(nameof(EdgeBasedTemplateMatchingTool.UsePoseRefinement));
            using EdgeBasedTemplateMatchingTool legacy = (EdgeBasedTemplateMatchingTool)VisionPipelineToolFactory.Create(step, artifact => encoded);
            Require(!legacy.UsePoseRefinement && legacy.results.Count == 0, "Missing opt-in XML field must preserve the disabled default.");
        }
    }
}
