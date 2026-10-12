using OpenCvSharp;
using OpenVisionLab.Vision2D.Property;
using OpenVisionLab.Vision2D.Tool;
using OpenVisionLab.Vision2D.Pipeline;
using OpenVisionLab.Inspection;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Threading;
using static OpenVisionLab.Inspection.Smoke.SmokeAssert;

namespace OpenVisionLab.Inspection.Smoke
{
    internal static class QrCodeSmokeSuite
    {
        internal static IEnumerable<SmokeCase> Cases()
        {
            yield return new SmokeCase("QR result attachment preserves completed failure and snapshot ownership", TestAttachmentOwnership);
            yield return new SmokeCase("QR result attachment releases exception and cancellation snapshots", TestAttachmentFailureCleanup);
            yield return new SmokeCase("QR direct RQ01 original ASCII UTF8 and whitespace text", TestText);
            yield return new SmokeCase("QR direct RQ02 RQ11 failure recovery and independent snapshots", TestRecovery);
            yield return new SmokeCase("QR direct RQ03 candidate counts and duplicate text", TestCounts);
            yield return new SmokeCase("QR direct RQ04 partial decode preserves candidate correspondence", TestPartial);
            yield return new SmokeCase("QR direct RQ05 RQ06 offset ROI and padded noncontinuous input", TestRoiCoordinates);
            yield return new SmokeCase("QR direct RQ07 RQ08 RQ09 input settings and ROI rejection", TestInvalid);
            yield return new SmokeCase("QR direct RQ10 cancellation and recovery", TestCancellation);
            yield return new SmokeCase("QR direct RQ12 separate instances and bounded repetitions", TestRepeated);
            yield return new SmokeCase("QR direct RQ15 backend classification and point contract", TestBackendContract);
            yield return new SmokeCase("QR pipeline RQ08 RQ13 settings round trip without execution", TestSettings);
            yield return new SmokeCase("QR pipeline RQ10 RQ14 typed Pipeline and Combined results", TestPipeline);
        }

        private static VisionPipeline QrPipeline()
        {
            VisionPipeline pipeline = new VisionPipeline { Name = "QR text" };
            VisionPipelineStep step = new VisionPipelineStep { Name = "Read QR", ToolType = "QrCodeTool", InputLayer = "Source", OutputLayer = "QR" };
            step.Parameters["NAME"] = "Original name";
            step.Parameters["USE_ROI"] = "true";
            step.Parameters["CvROI"] = "0,0,421,361";
            step.Parameters["ExpectedCodeCount"] = "1";
            pipeline.Steps.Add(step);
            return pipeline;
        }

        private static void TestSettings()
        {
            Require(VisionPipelineToolFactory.TryGetDescriptor("QrCodeTool", out VisionPipelineToolDescriptor descriptor) && descriptor.ToolType == "qrcode", "QR alias/canonical descriptor missing.");
            Require(descriptor.Parameters.Select(value => value.Name).SequenceEqual(new[] { "NAME", "USE_ROI", "CvROI", "ExpectedCodeCount" }) && descriptor.Artifacts.Count == 0, "QR metadata exposed unsupported settings or artifacts.");
            VisionPipeline pipeline = QrPipeline();
            string xml = VisionPipelineSerializer.Serialize(pipeline);
            VisionPipeline restored = VisionPipelineSerializer.Deserialize(xml);
            Require(VisionPipelineSerializer.Serialize(restored) == xml, "QR XML round trip changed settings.");
            using QrCodeTool loaded = (QrCodeTool)VisionPipelineToolFactory.Create(restored.Steps[0]);
            Require(loaded.LatestReading == null && loaded.imageSource.Empty() && loaded.imageResult.Empty() && loaded.property.NAME == "Original name" && loaded.property.CvROI == new Rect(0, 0, 421, 361) && loaded.property.ExpectedCodeCount == 1, "Restore ran inspection or changed settings.");
            foreach (var setting in new[] { ("ExpectedCodeCount", "0"), ("ExpectedCodeCount", "-1"), ("ExpectedCodeCount", "2147483648"), ("ExpectedCodeCount", "NaN"), ("USE_ROI", "maybe"), ("CvROI", "1,2,3"), ("CvROI", "0,0,0,10"), ("USE_THRESHOLD", "true"), ("unknown", "1") })
            {
                VisionPipelineStep invalid = VisionPipelineSerializer.Deserialize(xml).Steps[0];
                invalid.Parameters[setting.Item1] = setting.Item2;
                bool rejected = false;
                try { using QrCodeTool ignored = (QrCodeTool)VisionPipelineToolFactory.Create(invalid); }
                catch (ArgumentException) { rejected = true; }
                Require(rejected, "Invalid QR factory setting was accepted: " + setting);
            }
            VisionPipelineStep artifact = VisionPipelineSerializer.Deserialize(xml).Steps[0];
            artifact.Artifacts.Add(new VisionPipelineArtifactReference("template", "unused", "encoded-image", 1, new string('a', 64)));
            RequireThrows<ArgumentException>(() => VisionPipelineToolFactory.Create(artifact));
            using Mat source = QrCodeFixtures.Scene("a");
            using VisionToolResult result = loaded.Execute(source);
            RequireReading(result, "PART-A");
        }

        private static void TestPipeline()
        {
            using Mat one = QrCodeFixtures.Scene("a");
            using Mat two = QrCodeFixtures.Scene("a", "b");
            using QrCodeTool tool = new QrCodeTool();
            VisionPipeline pipeline = QrPipeline();
            pipeline.Steps[0].Parameters["USE_ROI"] = "false";
            foreach (Mat source in new[] { one, two })
            {
                using VisionToolResult direct = tool.Execute(source);
                using VisionPipelineContext context = new VisionPipelineContext();
                context.SetLayer("Source", source);
                using VisionPipelineRunResult run = new VisionPipelineRuntime().Run(pipeline, context);
                VisionToolResult actual = run.StepResults.Single().ToolResult;
                Require(actual.ErrorCode == direct.ErrorCode && actual.QrCodeReading.Candidates.Select(value => value.Text).OrderBy(value => value, StringComparer.Ordinal).SequenceEqual(direct.QrCodeReading.Candidates.Select(value => value.Text).OrderBy(value => value, StringComparer.Ordinal)), "Pipeline lost QR text or failure meaning: " + actual.ErrorName + " / " + direct.ErrorName);
                using CombinedInspectionRunResult combined = new CombinedInspectionRunner().Run(new CombinedInspectionInput { Image = source }, new IVisionTool[] { tool }, null);
                Require(combined.Steps[0].VisionResult.ErrorCode == direct.ErrorCode && combined.Steps[0].VisionResult.QrCodeReading.DetectedCount == direct.QrCodeReading.DetectedCount, "Combined lost typed QR evidence.");
            }
            using VisionPipelineContext canceledContext = new VisionPipelineContext();
            canceledContext.SetLayer("Source", one);
            using CancellationTokenSource cancellation = new CancellationTokenSource();
            cancellation.Cancel();
            using VisionPipelineRunResult canceled = new VisionPipelineRuntime().RunWithFailureResults(pipeline, canceledContext, cancellation.Token);
            Require(canceled.StepResults.Single().ToolResult.ErrorCode == VisionToolErrorCode.StepCanceled && canceled.StepResults[0].ToolResult.QrCodeReading == null, "Pipeline cancellation published QR evidence.");
            using VisionPipelineRunResult recovered = new VisionPipelineRuntime().Run(pipeline, canceledContext);
            RequireReading(recovered.StepResults.Single().ToolResult, "PART-A");
        }

        private static void RequireReading(VisionToolResult result, params string[] expected)
        {
            Require(result.Success && result.QrCodeReading != null, "QR reading failed: " + result.ErrorName + ": " + result.Message);
            Require(result.QrCodeReading.DetectedCount == expected.Length && result.QrCodeReading.DecodedCount == expected.Length, "QR observed count differs.");
            Require(result.QrCodeReading.Candidates.Select(value => value.Text).OrderBy(value => value, StringComparer.Ordinal).SequenceEqual(expected.OrderBy(value => value, StringComparer.Ordinal)), "QR original text differs.");
            Require(result.QrCodeReading.Candidates.All(value => value.Decoded && value.Corners.Count == 4), "QR decoded candidate/corners missing.");
            Require(result.ResultImage.Size() == result.QrCodeReading.InputImageSize && result.Metrics["QrDetectedCount"] == expected.Length, "QR frame or numeric evidence differs.");
        }

        private static void TestText()
        {
            using QrCodeTool tool = new QrCodeTool();
            foreach (string key in new[] { "ascii", "utf8", "spaces", "only-spaces" })
            {
                using Mat source = QrCodeFixtures.Scene(key);
                using Mat original = source.Clone();
                using VisionToolResult result = tool.Execute(source);
                RequireReading(result, QrCodeFixtures.Text(key));
                Require(Cv2.Norm(source, original) == 0, "QR changed caller pixels.");
                Require(ReferenceEquals(result.QrCodeReading, tool.LatestReading), "QR typed evidence differs from completed observation.");
            }
        }

        private static void TestRecovery()
        {
            using Mat source = QrCodeFixtures.Scene("a");
            using Mat blank = QrCodeFixtures.Scene();
            using QrCodeTool tool = new QrCodeTool();
            using VisionToolResult first = tool.Execute(source);
            RequireReading(first, "PART-A");
            var retained = first.QrCodeReading;
            byte[] original = first.ResultImage.ToBytes(".pgm");
            using VisionToolResult invalid = tool.Execute(null);
            Require(invalid.ErrorCode == VisionToolErrorCode.InputImageInvalid && tool.LatestReading == null && tool.imageResult == null, "Invalid input retained stale QR state.");
            using VisionToolResult notDetected = tool.Execute(blank);
            Require(notDetected.ErrorCode == VisionToolErrorCode.QrCodeNotDetected && notDetected.QrCodeReading.DetectedCount == 0, "Blank input did not report its current empty observation.");
            Require(original.SequenceEqual(first.ResultImage.ToBytes(".pgm")), "Later QR executions changed earlier caller snapshot.");
            using VisionToolResult recovered = tool.Execute(source);
            RequireReading(recovered, "PART-A");
            first.Dispose();
            tool.Dispose();
            Require(retained.Candidates[0].Text == "PART-A" && retained.Candidates[0].Corners.Count == 4 && !source.IsDisposed, "Managed evidence depends on native lifetime.");
            RequireThrows<NotSupportedException>(() => ((IList<OpenVisionLab.Vision2D.Result.QrCodeCandidate>)retained.Candidates).Clear());
            RequireThrows<NotSupportedException>(() => ((IList<Point2f>)retained.Candidates[0].Corners)[0] = new Point2f());
        }

        private static void TestCounts()
        {
            using Mat source = QrCodeFixtures.Scene("a", "b");
            using QrCodeTool tool = new QrCodeTool();
            using VisionToolResult mismatch = tool.Execute(source);
            Require(mismatch.ErrorCode == VisionToolErrorCode.QrCodeCountMismatch && mismatch.QrCodeReading.DetectedCount == 2 && mismatch.QrCodeReading.DecodedCount == 2, "Expected one must reject two codes while preserving observations.");
            tool.property.ExpectedCodeCount = 2;
            using VisionToolResult accepted = tool.Execute(source);
            RequireReading(accepted, "PART-A", "PART-B");
            foreach (var candidate in accepted.QrCodeReading.Candidates)
                Require((candidate.Text == "PART-A") == (candidate.Corners.Average(value => value.X) < 300), "Candidate text lost its physical corner correspondence.");
            using Mat duplicate = QrCodeFixtures.Scene("a", "a");
            using VisionToolResult same = tool.Execute(duplicate);
            RequireReading(same, "PART-A", "PART-A");
        }

        private static void TestPartial()
        {
            using Mat source = QrCodeFixtures.Partial();
            using QrCodeTool tool = new QrCodeTool();
            tool.property.ExpectedCodeCount = 2;
            using VisionToolResult result = tool.Execute(source);
            Require(result.ErrorCode == VisionToolErrorCode.QrCodeDecodeFailed && result.QrCodeReading.DetectedCount == 2 && result.QrCodeReading.DecodedCount == 1, "Partial decoding was lost or accepted.");
            foreach (var candidate in result.QrCodeReading.Candidates)
                Require(candidate.Corners.Count == 4 && (candidate.Text == "PART-A") == (candidate.Corners.Average(value => value.X) < 300), "Partial text and failed candidate correspondence differs.");
            tool.property.ExpectedCodeCount = 1;
            using VisionToolResult mismatch = tool.Execute(source);
            Require(mismatch.ErrorCode == VisionToolErrorCode.QrCodeCountMismatch && mismatch.QrCodeReading.DecodedCount == 1, "Count mismatch must precede decode failure.");
            tool.property.ExpectedCodeCount = 2;
            using Mat valid = QrCodeFixtures.Scene("a", "b");
            using VisionToolResult recovered = tool.Execute(valid);
            RequireReading(recovered, "PART-A", "PART-B");
        }

        private static void TestRoiCoordinates()
        {
            using Mat local = QrCodeFixtures.Scene("a");
            using QrCodeTool tool = new QrCodeTool();
            using VisionToolResult baseline = tool.Execute(local);
            RequireReading(baseline, "PART-A");
            using Mat padded = new Mat(459, 641, MatType.CV_8UC1, Scalar.All(255));
            using (Mat target = new Mat(padded, new Rect(31, 29, local.Width, local.Height))) local.CopyTo(target);
            using Mat view = new Mat(padded, new Rect(7, 9, 583, 437));
            Require(!view.IsContinuous(), "Padded subview fixture must be noncontinuous.");
            using Mat original = padded.Clone();
            tool.property.USE_ROI = true;
            tool.property.CvROI = new Rect(24, 20, local.Width, local.Height);
            using VisionToolResult result = tool.Execute(view);
            RequireReading(result, "PART-A");
            for (int corner = 0; corner < 4; corner++)
            {
                Point2f before = baseline.QrCodeReading.Candidates[0].Corners[corner], after = result.QrCodeReading.Candidates[0].Corners[corner];
                Require(after.X == before.X + 24 && after.Y == before.Y + 20, "ROI origin was not applied exactly once in current input coordinates.");
            }
            Require(Cv2.Norm(padded, original) == 0 && result.QrCodeReading.InputImageSize == view.Size(), "Subview/input frame was changed.");
            using Mat two = QrCodeFixtures.Scene("a", "b");
            tool.property.CvROI = new Rect(0, 0, 280, 361);
            using VisionToolResult selected = tool.Execute(two);
            RequireReading(selected, "PART-A");
            tool.property.CvROI = new Rect(0, 0, two.Width, two.Height);
            using VisionToolResult widened = tool.Execute(two);
            Require(widened.ErrorCode == VisionToolErrorCode.QrCodeCountMismatch, "Widened ROI silently selected a first code.");
        }

        private static void TestInvalid()
        {
            using QrCodeTool tool = new QrCodeTool();
            using Mat source = QrCodeFixtures.Scene("a");
            using Mat bgr = new Mat();
            Cv2.CvtColor(source, bgr, ColorConversionCodes.GRAY2BGR);
            using (VisionToolResult result = tool.Execute(bgr)) RequireReading(result, "PART-A");
            using Mat empty = new Mat();
            using Mat depth = new Mat(361, 421, MatType.CV_16UC1, Scalar.All(0));
            using Mat bgra = new Mat(361, 421, MatType.CV_8UC4, Scalar.All(0));
            foreach (Mat invalid in new[] { null, empty, depth, bgra })
            {
                using VisionToolResult result = tool.Execute(invalid);
                Require(result.ErrorCode == VisionToolErrorCode.InputImageInvalid && result.QrCodeReading == null && tool.LatestReading == null, "Unsupported input was decoded.");
            }
            foreach (Action<QrCodeToolProperty> change in new Action<QrCodeToolProperty>[] { value => value.ExpectedCodeCount = 0, value => value.ExpectedCodeCount = -1, value => value.USE_THRESHOLD = true, value => value.USE_ADAPTIVE_THRESHOLD = true, value => value.USE_BITWISENOT = true, value => value.USE_MULTI_ROI = true, value => value.CvMASKS.Add(new Rect(0, 0, 1, 1)) })
            {
                tool.property = new QrCodeToolProperty();
                change(tool.property);
                using VisionToolResult result = tool.Execute(source);
                Require(result.ErrorCode == VisionToolErrorCode.InvalidParameter && result.QrCodeReading == null, "Unsupported QR setting was silently ignored.");
            }
            tool.property = new QrCodeToolProperty { USE_ROI = true };
            foreach (Rect roi in new[] { new Rect(-1, 0, 2, 2), new Rect(0, 0, 0, 2), new Rect(0, 0, 422, 361), new Rect(int.MaxValue, 0, int.MaxValue, 1), new Rect(0, int.MaxValue, 1, int.MaxValue) })
            {
                tool.property.CvROI = roi;
                using VisionToolResult result = tool.Execute(source);
                Require(result.ErrorCode == VisionToolErrorCode.InvalidRoi && result.QrCodeReading == null, "Invalid/overflow QR ROI was accepted or clamped.");
            }
            tool.property = new QrCodeToolProperty();
            using VisionToolResult recovered = tool.Execute(source);
            RequireReading(recovered, "PART-A");
            tool.property = null;
            using VisionToolResult missing = tool.Execute(source);
            Require(missing.ErrorCode == VisionToolErrorCode.ToolPropertyMissing, "Missing QR property classification changed.");
            tool.property = new QrCodeToolProperty();
            tool.SetSourceImage(source);
            tool.Run();
            Require(tool.LatestReading.DecodedCount == 1, "Legacy direct Run did not publish current evidence.");
            tool.property.ExpectedCodeCount = 0;
            RequireThrows<InvalidOperationException>(() => tool.Run());
            Require(tool.LatestReading == null && tool.imageResult == null, "Invalid legacy Run retained stale state.");
        }

        private static void TestCancellation()
        {
            using QrCodeTool tool = new QrCodeTool();
            using Mat source = QrCodeFixtures.Scene("a");
            using VisionToolResult earlier = tool.Execute(source);
            using CancellationTokenSource pre = new CancellationTokenSource();
            pre.Cancel();
            RequireThrows<OperationCanceledException>(() => tool.Execute(source, pre.Token));
            Require(tool.LatestReading == null && tool.imageResult == null && earlier.QrCodeReading.Candidates[0].Text == "PART-A", "Pre-cancellation retained current observation or damaged previous result.");
            // Timing cancellation demonstrates eventual observation, not a precise native instruction or hard timeout.
            using Mat large = new Mat(2401, 3201, MatType.CV_8UC1, Scalar.All(255));
            using (Mat target = new Mat(large, new Rect(700, 600, source.Width, source.Height))) source.CopyTo(target);
            using CancellationTokenSource during = new CancellationTokenSource();
            during.CancelAfter(1);
            RequireThrows<OperationCanceledException>(() => tool.Execute(large, during.Token));
            Require(tool.LatestReading == null && tool.imageResult == null, "Timed cancellation published stale evidence.");
            using VisionToolResult recovered = tool.Execute(source);
            RequireReading(recovered, "PART-A");
        }

        private static void TestRepeated()
        {
            Parallel.For(0, 2, _ =>
            {
                using Mat source = QrCodeFixtures.Scene("a");
                using QrCodeTool tool = new QrCodeTool();
                for (int iteration = 0; iteration < 20; iteration++)
                {
                    using VisionToolResult result = tool.Execute(source);
                    RequireReading(result, "PART-A");
                    Mat image = result.ResultImage;
                    result.Dispose();
                    Require(image.IsDisposed && !source.IsDisposed, "Repeated QR ownership leaked result or released source.");
                }
            });
        }

        private static void TestBackendContract()
        {
            using QrCodeTool tool = new QrCodeTool();
            MethodInfo classify = typeof(QrCodeTool).GetMethod("ResolveExecutionErrorCode", BindingFlags.Instance | BindingFlags.NonPublic);
            foreach (Exception exception in new Exception[] { new DllNotFoundException(), new EntryPointNotFoundException(), new BadImageFormatException() })
                Require((VisionToolErrorCode)classify.Invoke(tool, new object[] { exception }) == VisionToolErrorCode.QrCodeBackendUnavailable, "Backend absence was classified as QR absence.");
            MethodInfo validate = typeof(QrCodeTool).GetMethod("ValidatePoints", BindingFlags.Static | BindingFlags.NonPublic);
            foreach (Point2f[] points in new[] { Array.Empty<Point2f>(), new[] { new Point2f() }, new[] { new Point2f(), new Point2f(), new Point2f(), new Point2f() }, new[] { new Point2f(float.NaN, 0), new Point2f(1, 0), new Point2f(1, 1), new Point2f(0, 1) } })
            {
                TargetInvocationException failure = RequireThrows<TargetInvocationException>(() => validate.Invoke(null, new object[] { points }));
                Require(failure.InnerException is InvalidOperationException, "Invalid native geometry was not rejected before decode correspondence.");
            }
        }

        private static void TestAttachmentOwnership()
        {
            using Mat source = new Mat(12, 16, MatType.CV_8UC1, Scalar.All(37));
            using AttachmentTool tool = new AttachmentTool();
            using VisionToolResult first = tool.Execute(source);
            Require(first.Success && first.Metrics["Attachment"] == 17 && first.QrCodeReading == null, "Default attachment changed a non-QR result.");
            Mat firstImage = first.ResultImage;
            tool.FailAfterRun = true;
            using VisionToolResult failed = tool.Execute(source);
            Require(!failed.Success && failed.ErrorCode == VisionToolErrorCode.ContourNoResult && failed.Metrics["Attachment"] == 17, "Completed failure lost attachment.");
            Require(firstImage.At<byte>(0, 0) == 37 && source.At<byte>(0, 0) == 37, "Later execution changed the caller's input or snapshot.");
            tool.HookCalls = 0;
            using VisionToolResult invalid = tool.Execute(null);
            Require(invalid.ErrorCode == VisionToolErrorCode.InputImageInvalid && tool.HookCalls == 0 && invalid.ResultImage == null, "Pre-run rejection invoked attachment.");
        }

        private static void TestAttachmentFailureCleanup()
        {
            using Mat source = new Mat(12, 16, MatType.CV_8UC1, Scalar.All(37));
            using AttachmentTool tool = new AttachmentTool { ThrowInHook = true };
            foreach (bool failAfterRun in new[] { false, true })
            {
                tool.FailAfterRun = failAfterRun;
                using VisionToolResult failure = tool.Execute(source);
                Require(failure.ErrorCode == VisionToolErrorCode.ToolExecutionException && failure.ResultImage == null && tool.AttachedImage.IsDisposed, "Hook exception leaked its assembled snapshot.");
            }
            tool.ThrowInHook = false;
            foreach (bool failAfterRun in new[] { false, true })
            {
                tool.FailAfterRun = failAfterRun;
                using CancellationTokenSource cancellation = new CancellationTokenSource();
                tool.CancelInHook = cancellation;
                bool canceled = false;
                try { using VisionToolResult ignored = tool.Execute(source, cancellation.Token); }
                catch (OperationCanceledException) { canceled = true; }
                Require(canceled && tool.AttachedImage.IsDisposed, "Hook cancellation leaked or published a result.");
            }
            tool.CancelInHook = null;
            tool.FailAfterRun = false;
            using VisionToolResult recovered = tool.Execute(source);
            Require(recovered.Success && source.At<byte>(0, 0) == 37, "Hook failure did not recover or changed caller input.");
        }

        private sealed class AttachmentTool : OpenCvAlgorithmBase, ICancellableVisionTool
        {
            public MeanToolProperty property = new MeanToolProperty();
            internal bool FailAfterRun, ThrowInHook;
            internal int HookCalls;
            internal Mat AttachedImage;
            internal CancellationTokenSource CancelInHook;

            public VisionToolResult Execute(Mat source, CancellationToken token) => ExecuteWithCancellation(source, token);
            public override void Run() => ReplaceResultImage(imageSource.Clone());
            protected override bool TryValidateAfterRun(out VisionToolErrorCode code, out string message)
            {
                code = FailAfterRun ? VisionToolErrorCode.ContourNoResult : VisionToolErrorCode.None;
                message = FailAfterRun ? "Deliberate completed failure." : string.Empty;
                return !FailAfterRun;
            }
            protected override void AttachResultEvidence(VisionToolResult result)
            {
                HookCalls++;
                AttachedImage = result.ResultImage;
                result.Metrics["Attachment"] = 17;
                if (ThrowInHook) throw new InvalidOperationException("Deliberate attachment failure.");
                CancelInHook?.Cancel();
            }
        }
    }
}
