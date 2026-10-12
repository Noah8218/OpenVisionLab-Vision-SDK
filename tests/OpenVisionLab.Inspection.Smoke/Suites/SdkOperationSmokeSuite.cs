using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using OpenCvSharp;
using OpenVisionLab.Compute;
using OpenVisionLab.Compute.Infrastructure;
using OpenVisionLab.Inspection;
using OpenVisionLab.Vision2D.Pipeline;
using OpenVisionLab.Vision2D.Property;
using OpenVisionLab.Vision2D.Tool;
using OpenVisionLab.Vision3D.FeatureExtraction;
using OpenVisionLab.Vision3D.Geometry;
using static OpenVisionLab.Inspection.Smoke.SmokeAssert;

namespace OpenVisionLab.Inspection.Smoke
{
    // Host scheduling/configuration belongs to these Dev-only scenarios, not to the SDK.
    internal static class SdkOperationSmokeSuite
    {
        private static readonly TimeSpan Guard = TimeSpan.FromSeconds(20);

        internal static IEnumerable<SmokeCase> Cases()
        {
            yield return new SmokeCase("SDK operation fresh contexts recover blank missing input and factory failure without stale layers", TestPipelineLifecycle);
            yield return new SmokeCase("SDK operation shutdown cancellation disposes a factory-created tool before restore and explicit run", TestPipelineCancellation);
            yield return new SmokeCase("SDK operation combined runs retain ordered fault evidence and older QR snapshots through reconfiguration", TestCombinedSequence);
            yield return new SmokeCase("SDK operation typed 3D fault and tokenless cancellation preserve completed values before recovery", TestThreeDSequence);
            yield return new SmokeCase("SDK operation pending settings and cancellation isolate a concurrent different-tool worker", TestPendingChange);
            yield return new SmokeCase("SDK operation two borrowed tools isolate cancellation and release their shared session last", TestSharedBorrowers);
            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("OVL_CUDA_EXECUTION_FIXTURES")))
            {
                yield return new SmokeCase("SDK operation native fault persists across borrowers and a new session explicitly retries", TestRememberedNativeFailure, "native-fixture");
                yield return new SmokeCase("SDK operation native shared borrowers cancel independently then release the Host session", TestSharedNativeCancellation, "native-fixture");
            }
        }

        internal static VisionPipeline QrProfile()
        {
            VisionPipeline pipeline = new VisionPipeline { Name = "Local operation QR" };
            VisionPipelineStep step = new VisionPipelineStep { Name = "Read QR", ToolType = "qrcode", InputLayer = "Source", OutputLayer = "CurrentQR" };
            step.Parameters["ExpectedCodeCount"] = "1";
            pipeline.Steps.Add(step);
            return pipeline;
        }

        private static void TestPipelineLifecycle()
        {
            using Mat a = QrCodeFixtures.Scene("a");
            using Mat b = QrCodeFixtures.Scene("b");
            using Mat blank = QrCodeFixtures.Scene();
            byte[] input = a.ToBytes(".png");
            VisionPipeline pipeline = QrProfile();
            using VisionPipelineContext firstContext = new VisionPipelineContext();
            firstContext.SetLayer("Source", a);
            using VisionPipelineRunResult first = new VisionPipelineRuntime().Run(pipeline, firstContext);
            RequireQr(first.StepResults.Single().ToolResult, "PART-A");
            byte[] retained = first.StepResults[0].ToolResult.ResultImage.ToBytes(".png");
            foreach (bool missing in new[] { false, true })
            {
                using VisionPipelineContext current = new VisionPipelineContext();
                if (!missing) current.SetLayer("Source", blank);
                using VisionPipelineRunResult failed = new VisionPipelineRuntime().RunWithFailureResults(pipeline, current);
                Require(!failed.Success && failed.StepResults.Count == 1 && failed.StepResults[0].ToolResult.ErrorCode ==
                    (missing ? VisionToolErrorCode.InputLayerMissing : VisionToolErrorCode.QrCodeNotDetected), "Current failure must retain its precise boundary.");
                using Mat absent = current.GetLayer("CurrentQR");
                Require(absent == null, "A fresh failed context must not publish an earlier successful output layer.");
            }
            using (VisionPipelineContext current = new VisionPipelineContext())
            {
                current.SetLayer("Source", b);
                using VisionPipelineRunResult failed = new VisionPipelineRuntime(_ => throw new InvalidOperationException("owned factory fault"))
                    .RunWithFailureResults(pipeline, current);
                Require(failed.StepResults.Single().ToolResult.ErrorCode == VisionToolErrorCode.ToolFactoryFailed, "Factory failure must be distinguished from algorithm rejection.");
                using Mat absent = current.GetLayer("CurrentQR");
                Require(absent == null, "Factory failure must not publish a layer.");
                using VisionPipelineRunResult recovered = new VisionPipelineRuntime().Run(pipeline, current);
                RequireQr(recovered.StepResults.Single().ToolResult, "PART-B");
            }
            Require(retained.SequenceEqual(first.StepResults[0].ToolResult.ResultImage.ToBytes(".png")) && input.SequenceEqual(a.ToBytes(".png")),
                "Recovery changed retained results or caller pixels.");
        }

        private static void TestPipelineCancellation()
        {
            using Mat source = QrCodeFixtures.Scene("b");
            using VisionPipelineContext context = new VisionPipelineContext();
            context.SetLayer("Source", source);
            using CancellationTokenSource cancellation = new CancellationTokenSource();
            QrCodeTool created = null;
            Mat createdInput = null;
            VisionPipelineRuntime runtime = new VisionPipelineRuntime(step =>
            {
                created = (QrCodeTool)VisionPipelineToolFactory.Create(step);
                createdInput = created.imageSource;
                cancellation.Cancel(); // Host stop requested at an existing managed boundary.
                return created;
            }, true);
            string settings = VisionPipelineSerializer.Serialize(QrProfile());
            using VisionPipelineRunResult stopped = runtime.RunWithFailureResults(QrProfile(), context, cancellation.Token);
            Require(stopped.StepResults.Single().ToolResult.ErrorCode == VisionToolErrorCode.StepCanceled && createdInput.IsDisposed && created.imageSource == null,
                "A canceled factory boundary must return one canceled step and release its owned Tool.");
            using Mat absent = context.GetLayer("CurrentQR");
            Require(absent == null && stopped.StepResults[0].ToolResult.QrCodeReading == null, "Stop must not publish stale QR values.");
            VisionPipeline restored = VisionPipelineSerializer.Deserialize(settings);
            using QrCodeTool loaded = (QrCodeTool)VisionPipelineToolFactory.Create(restored.Steps[0]);
            Require(loaded.LatestReading == null && loaded.imageSource.Empty(), "Restoring after stop must not execute.");
            using VisionPipelineRunResult recovered = new VisionPipelineRuntime().Run(restored, context);
            RequireQr(recovered.StepResults.Single().ToolResult, "PART-B");
        }

        private static void TestCombinedSequence()
        {
            using Mat a = QrCodeFixtures.Scene("a");
            using Mat two = QrCodeFixtures.Scene("a", "b");
            using QrCodeTool oneTool = new QrCodeTool();
            using QrCodeTool twoTool = new QrCodeTool();
            twoTool.SetProperty(new QrCodeToolProperty { ExpectedCodeCount = 2 });
            CombinedInspectionRunner runner = new CombinedInspectionRunner();
            using CombinedInspectionRunResult first = runner.Run(new CombinedInspectionInput { Image = a }, new IVisionTool[] { oneTool }, null);
            RequireQr(first.Steps.Single().VisionResult, "PART-A");
            byte[] retained = first.Steps[0].VisionResult.ResultImage.ToBytes(".png");
            using (CombinedInspectionRunResult failed = runner.Run(new CombinedInspectionInput { Image = two }, new IVisionTool[] { oneTool, new FaultTool(), twoTool }, null))
            {
                Require(!failed.Success && failed.Steps.Count == 3 && failed.Steps[0].VisionResult.ErrorCode == VisionToolErrorCode.QrCodeCountMismatch,
                    "Count mismatch must not suppress later independent Tools.");
                Require(!failed.Steps[1].Success, "Custom Tool fault must remain a failed step.");
                RequireQr(failed.Steps[2].VisionResult, "PART-A", "PART-B");
            }
            oneTool.SetProperty(new QrCodeToolProperty { ExpectedCodeCount = 2 }); // All earlier calls have returned.
            using CombinedInspectionRunResult recovered = runner.Run(new CombinedInspectionInput { Image = two }, new IVisionTool[] { oneTool }, null);
            RequireQr(recovered.Steps.Single().VisionResult, "PART-A", "PART-B");
            Require(retained.SequenceEqual(first.Steps[0].VisionResult.ResultImage.ToBytes(".png")) && !a.IsDisposed && !two.IsDisposed,
                "Reconfiguration or result release changed previous evidence or caller inputs.");
        }

        private static void TestThreeDSequence()
        {
            SurfaceMatchSample[] points = { new SurfaceMatchSample(0, new ThreeDPoint(0, 0, 0)), new SurfaceMatchSample(1, new ThreeDPoint(3, 0, 0)),
                new SurfaceMatchSample(2, new ThreeDPoint(0, 4, 0)), new SurfaceMatchSample(3, new ThreeDPoint(0, 0, 2)) };
            SurfaceMatchSample[] original = (SurfaceMatchSample[])points.Clone();
            RigidSurfacePose identity = new RigidSurfacePose(1, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0);
            DeterministicSurfaceCoverageTool tool = new DeterministicSurfaceCoverageTool();
            Func<DeterministicSurfaceCoverageResult> execute = () => tool.Execute(points, points, identity, 0.01);
            ThreeDToolAdapter<DeterministicSurfaceCoverageResult> good = new ThreeDToolAdapter<DeterministicSurfaceCoverageResult>("Exact authored points", execute);
            ThreeDToolExecutionReport faulted = ThreeDToolExecutionRunner.Run(new IThreeDToolAdapter[]
            {
                new ThreeDToolAdapter<DeterministicSurfaceCoverageResult>("owned fault", () => throw new InvalidDataException("owned input fault")), good
            });
            Require(faulted.Status == ThreeDToolExecutionStatus.Faulted && faulted.FaultedStepCount == 1 && faulted.CompletedStepCount == 1,
                "Independent typed 3D evidence must follow a fault.");
            Require(((DeterministicSurfaceCoverageResult)faulted.Steps[1].UntypedResult).MatchedModelSampleCount == 4, "Exact identity oracle differs.");
            using CancellationTokenSource cancellation = new CancellationTokenSource();
            ThreeDToolExecutionReport stopped = ThreeDToolExecutionRunner.Run(new IThreeDToolAdapter[]
            {
                new ThreeDToolAdapter<DeterministicSurfaceCoverageResult>("tokenless completed value", () => { var value = execute(); cancellation.Cancel(); return value; }), good
            }, cancellation.Token);
            Require(stopped.Status == ThreeDToolExecutionStatus.Canceled && stopped.Steps.Count == 1 && stopped.CompletedStepCount == 1,
                "A completed tokenless call must retain its value and stop later adapters.");
            var retained = (DeterministicSurfaceCoverageResult)stopped.Steps[0].UntypedResult;
            ThreeDToolExecutionReport recovered = ThreeDToolExecutionRunner.Run(new[] { good });
            Require(recovered.Status == ThreeDToolExecutionStatus.Completed && retained.MatchedModelSampleCount == 4 && points.SequenceEqual(original),
                "Recovery must preserve completed values and caller sample order.");
        }

        private static void TestPendingChange()
        {
            using Mat template = EdgePoseRefinementSmokeSuite.CreateTemplate();
            using Mat source = EdgeScene(template);
            using Mat qr = QrCodeFixtures.Scene("b");
            using ManualResetEventSlim entered = new ManualResetEventSlim();
            using ManualResetEventSlim release = new ManualResetEventSlim();
            using CancellationTokenSource cancellation = new CancellationTokenSource();
            using VisionComputeSession session = new VisionComputeSession(new VisionComputeOptions { UseCuda = true }, token =>
            {
                entered.Set();
                Require(release.Wait(Guard, CancellationToken.None), "Controlled probe release timed out.");
                token.ThrowIfCancellationRequested();
                return new CudaSupportReport(CudaFallbackReason.NativeModuleMissing);
            }, true);
            using EdgeBasedTemplateMatchingTool edge = EdgeTool(template, session);
            using QrCodeTool reader = new QrCodeTool();
            Task running = Task.Run(() => RequireCanceled(() => edge.Execute(source, cancellation.Token), cancellation.Token));
            try
            {
                Require(entered.Wait(Guard), "Worker A must reach its controlled probe.");
                EdgeBasedTemplateMatchingToolProperty pending = EdgeProperty();
                pending.NAME = "After cooperative stop";
                Task<VisionToolResult> other = Task.Run(() => reader.Execute(qr));
                Require(other.Wait(Guard), "Different worker B must complete while A is still active.");
                using VisionToolResult retained = other.Result;
                RequireQr(retained, "PART-B");
                Require(!running.IsCompleted && edge.property.NAME != pending.NAME, "Pending Host settings must not mutate the active instance.");
                cancellation.Cancel();
                release.Set();
                Require(running.Wait(Guard), "Cooperative cancellation must return before reconfiguration.");
                edge.SetProperty(pending);
                Require(edge.property.NAME == pending.NAME, "Finished execution must accept the pending Host configuration.");
                using VisionToolResult recovered = edge.Execute(source);
                RequireEdge(edge, recovered, template);
                edge.Dispose();
                RequireQr(retained, "PART-B");
                Require(!qr.IsDisposed && !source.IsDisposed, "Shutdown of one instance must not release another's caller inputs.");
            }
            finally { cancellation.Cancel(); release.Set(); running.Wait(Guard); }
        }

        private static void TestSharedBorrowers()
        {
            using Mat template = EdgePoseRefinementSmokeSuite.CreateTemplate();
            using Mat source = EdgeScene(template);
            using ManualResetEventSlim entered = new ManualResetEventSlim();
            using ManualResetEventSlim release = new ManualResetEventSlim();
            using ManualResetEventSlim secondStarted = new ManualResetEventSlim();
            using CancellationTokenSource waiting = new CancellationTokenSource();
            int probes = 0;
            using VisionComputeSession session = new VisionComputeSession(new VisionComputeOptions { UseCuda = true }, token =>
            {
                Interlocked.Increment(ref probes);
                entered.Set();
                Require(release.Wait(Guard, CancellationToken.None), "Shared probe release timed out.");
                return new CudaSupportReport(CudaFallbackReason.NativeModuleMissing);
            }, true);
            using EdgeBasedTemplateMatchingTool first = EdgeTool(template, session);
            using EdgeBasedTemplateMatchingTool second = EdgeTool(template, session);
            using Mat secondInput = source.Clone();
            Task<VisionToolResult> running = Task.Run(() => first.Execute(source));
            Task canceled = null;
            try
            {
                Require(entered.Wait(Guard), "First borrower must hold the shared session probe.");
                canceled = Task.Factory.StartNew(() =>
                {
                    secondStarted.Set();
                    RequireCanceled(() => second.Execute(secondInput, waiting.Token), waiting.Token);
                }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
                Require(secondStarted.Wait(Guard), "Second borrower must start.");
                waiting.Cancel();
                Require(canceled.Wait(Guard) && !running.IsCompleted, "The second cancellation must return while the first borrower remains active.");
                second.Dispose();
                release.Set();
                Require(running.Wait(Guard), "First borrower must complete after release.");
                using VisionToolResult retained = running.Result;
                RequireEdge(first, retained, template);
                using VisionToolResult recovered = first.Execute(source);
                RequireEdge(first, recovered, template);
                Require(probes == 1, "Releasing a borrower must not reset or dispose the Host-owned shared session.");
                first.Dispose();
                session.Dispose();
                RequireThrows<ObjectDisposedException>(() => session.ProbeCuda());
                Require(!retained.ResultImage.IsDisposed && !source.IsDisposed && !secondInput.IsDisposed, "Host shutdown violated result or input ownership.");
            }
            finally { waiting.Cancel(); release.Set(); canceled?.Wait(Guard); running.Wait(Guard); }
        }

        private static void TestRememberedNativeFailure()
        {
            string path = Path.Combine(Environment.GetEnvironmentVariable("OVL_CUDA_EXECUTION_FIXTURES"), "execution-3.dll");
            IntPtr module = CudaNativeProbe.LoadLibraryExW(path, IntPtr.Zero, CudaNativeProbe.SearchDllDirectoryAndSystem32);
            Require(module != IntPtr.Zero, "Owned failure fixture must load.");
            try
            {
                Counter live = CudaNativeProbe.GetExport<Counter>(module, "ovl_test_live_executions");
                int probes = 0;
                using Mat template = EdgePoseRefinementSmokeSuite.CreateTemplate();
                using Mat source = EdgeScene(template);
                Func<CancellationToken, CudaSupportReport> probe = token => { probes++; return CudaNativeProbe.Probe(path, token); };
                using VisionComputeSession session = new VisionComputeSession(new VisionComputeOptions { UseCuda = true }, probe, true);
                using EdgeBasedTemplateMatchingTool first = EdgeTool(template, session);
                using EdgeBasedTemplateMatchingTool second = EdgeTool(template, session);
                using VisionToolResult failedNative = first.Execute(source);
                RequireEdge(first, failedNative, template);
                Require(!failedNative.ComputeDiagnostics.AccelerationApplied && failedNative.ComputeDiagnostics.FallbackReason == CudaFallbackReason.GpuExecutionFailed && live() == 0,
                    "GPU failure must discard native work, release its execution handle and publish correct CPU results.");
                first.Dispose();
                using VisionToolResult remembered = second.Execute(source);
                RequireEdge(second, remembered, template);
                Require(remembered.ComputeDiagnostics.FallbackReason == CudaFallbackReason.GpuExecutionFailed && probes == 1 && live() == 0,
                    "Another borrower must retain the same session failure without silently re-probing.");
                second.Dispose();
                session.Dispose();
                using VisionComputeSession fresh = new VisionComputeSession(new VisionComputeOptions { UseCuda = true }, probe, true);
                using EdgeBasedTemplateMatchingTool recovered = EdgeTool(template, fresh);
                using VisionToolResult retry = recovered.Execute(source);
                RequireEdge(recovered, retry, template);
                Require(probes == 2 && retry.ComputeDiagnostics.FallbackReason == CudaFallbackReason.GpuExecutionFailed && live() == 0,
                    "Explicit new-session creation must retry discovery; a still-failing fixture must not become GPU success.");
            }
            finally { _ = CudaNativeProbe.FreeLibrary(module); }
        }

        private static void TestSharedNativeCancellation()
        {
            string path = Path.Combine(Environment.GetEnvironmentVariable("OVL_CUDA_EXECUTION_FIXTURES"), "execution-5.dll");
            IntPtr module = CudaNativeProbe.LoadLibraryExW(path, IntPtr.Zero, CudaNativeProbe.SearchDllDirectoryAndSystem32);
            Require(module != IntPtr.Zero, "Owned active-call fixture must load.");
            try
            {
                Counter active = CudaNativeProbe.GetExport<Counter>(module, "ovl_test_active_calls");
                Counter live = CudaNativeProbe.GetExport<Counter>(module, "ovl_test_live_executions");
                using Mat template = EdgePoseRefinementSmokeSuite.CreateTemplate();
                using Mat a = EdgeScene(template);
                using Mat b = a.Clone();
                using VisionComputeSession session = new VisionComputeSession(new VisionComputeOptions { UseCuda = true }, token => CudaNativeProbe.Probe(path, token), true);
                using EdgeBasedTemplateMatchingTool first = EdgeTool(template, session);
                using EdgeBasedTemplateMatchingTool second = EdgeTool(template, session);
                using CancellationTokenSource runningToken = new CancellationTokenSource();
                using CancellationTokenSource waitingToken = new CancellationTokenSource();
                Task running = Task.Run(() => RequireCanceled(() => first.Execute(a, runningToken.Token), runningToken.Token));
                Task waiting = null;
                try
                {
                    Require(SpinWait.SpinUntil(() => active() > 0, Guard), "First public Tool must submit native work.");
                    using ManualResetEventSlim entered = new ManualResetEventSlim();
                    Thread waitingThread = null;
                    waiting = Task.Factory.StartNew(() =>
                    {
                        waitingThread = Thread.CurrentThread;
                        entered.Set();
                        // The Host's explicit startup probe has no other blocking path:
                        // WaitSleepJoin here proves contention on ProbeCuda's session monitor.
                        RequireCanceled(() => session.ProbeCuda(waitingToken.Token), waitingToken.Token);
                    }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
                    Require(entered.Wait(Guard), "Second public Tool must start while native work is active.");
                    Require(SpinWait.SpinUntil(() => (waitingThread.ThreadState & System.Threading.ThreadState.WaitSleepJoin) != 0, Guard) && active() > 0,
                        "The second Host must actually wait in shared-session startup while native work is active.");
                    Stopwatch waitingCancel = Stopwatch.StartNew();
                    waitingToken.Cancel();
                    Require(waiting.Wait(Guard) && active() > 0 && !running.IsCompleted, "A waiting borrower must cancel without stopping the active borrower.");
                    waitingCancel.Stop();
                    Stopwatch runningCancel = Stopwatch.StartNew();
                    runningToken.Cancel();
                    Require(running.Wait(Guard) && active() == 0, "Cooperative native cancellation must finish before Tool release.");
                    runningCancel.Stop();
                    second.Dispose();
                    first.Dispose();
                    Require(session.TryScoreEdgeGrid(new[] { 1.0, -1.0, 0.0, 0.5 }, new double[4], new[] { 0 }, new[] { 1.0 }, new[] { 0.0 }, new[] { -2.0 },
                        4, 0, 0, 4, 1, 1, false, CancellationToken.None, out double[] _, out ComputeExecutionEvidence _), "Borrower release must leave the Host session usable after cancellation.");
                    Stopwatch shutdown = Stopwatch.StartNew();
                    session.Dispose();
                    shutdown.Stop();
                    Require(active() == 0 && live() == 0 && !a.IsDisposed && !b.IsDisposed, "Session shutdown must release native execution handles while retaining caller inputs.");
                    Console.WriteLine("OBSERVE | " + System.Text.Json.JsonSerializer.Serialize(new { Case = "SharedNativeBorrowers", WaitingCancellationMilliseconds = waitingCancel.Elapsed.TotalMilliseconds,
                        ActiveCancellationMilliseconds = runningCancel.Elapsed.TotalMilliseconds, SessionShutdownMilliseconds = shutdown.Elapsed.TotalMilliseconds, ActiveNativeCallsAfterShutdown = active(), LiveExecutionHandlesAfterShutdown = live(), WaitingEntryPoint = "Host ProbeCuda before second Tool Execute", Boundary = "Synthetic cooperative fixture; not real GPU loss, native hard interruption or approved latency." }));
                }
                finally { waitingToken.Cancel(); runningToken.Cancel(); waiting?.Wait(Guard); running.Wait(Guard); }
            }
            finally { _ = CudaNativeProbe.FreeLibrary(module); }
        }

        internal static Mat EdgeScene(Mat template)
        {
            Mat source = new Mat(193, 257, MatType.CV_8UC1, Scalar.All(17));
            using Mat roi = source.SubMat(new Rect(17, 23, template.Width, template.Height));
            template.CopyTo(roi);
            return source;
        }

        internal static EdgeBasedTemplateMatchingToolProperty EdgeProperty(int step = 1) => new EdgeBasedTemplateMatchingToolProperty
        {
            SCORE_MIN = 0.5, GREEDINESS = 0.8, SEARCH_STEP = step, MAX_TEMPLATE_POINTS = 500,
            USE_HYBRID_VERIFY = false, USE_FIND_ANGLE = false, USE_FIND_SCALE = false, USE_DRAW_IMAGE = false,
            USE_POSITION_REFINE = false, USE_SUBPIXEL_REFINE = false, CANNY_HIGH = 100, MIN_GRADIENT_MAGNITUDE = 5
        };

        internal static EdgeBasedTemplateMatchingTool EdgeTool(Mat template, VisionComputeSession session)
        {
            EdgeBasedTemplateMatchingTool tool = new EdgeBasedTemplateMatchingTool(session);
            tool.SetProperty(EdgeProperty());
            tool.SetTemplateImage(template);
            return tool;
        }

        internal static void RequireEdge(EdgeBasedTemplateMatchingTool tool, VisionToolResult result, Mat template)
        {
            RequirePreparedEdge(tool, result, tool.PrepareLearnedModel("operation-input-oracle"));
        }

        internal static void RequirePreparedEdge(EdgeBasedTemplateMatchingTool tool, VisionToolResult result, EdgeTemplateModelArtifact inputModel)
        {
            Require(result.Success && tool.results.Count == 1, "Authored edge match failed: " + result.ErrorName + ": " + result.Message);
            // Input model points define its centroid. SEARCH_STEP=1 quantizes that anchor,
            // then reports the visual center; no fixed metrology tolerance is assumed.
            double centerX = inputModel.Points.Average(point => point.X);
            double centerY = inputModel.Points.Average(point => point.Y);
            float expectedX = (float)(Math.Round(17 + centerX) + inputModel.Width / 2.0 - centerX);
            float expectedY = (float)(Math.Round(23 + centerY) + inputModel.Height / 2.0 - centerY);
            Require(tool.results[0].Center.X == expectedX && tool.results[0].Center.Y == expectedY && tool.results[0].Angle == 0 && tool.results[0].Scale == 1,
                "Authored placement and input-model centroid must give the exact unrefined grid pose.");
        }

        internal static void RequireQr(VisionToolResult result, params string[] text)
        {
            Require(result.Success && result.QrCodeReading != null && result.QrCodeReading.DecodedCount == text.Length &&
                result.QrCodeReading.Candidates.Select(value => value.Text).OrderBy(value => value, StringComparer.Ordinal)
                    .SequenceEqual(text.OrderBy(value => value, StringComparer.Ordinal)), "Authored QR texts differ: " + result.ErrorName);
        }

        private static void RequireCanceled(Action execute, CancellationToken token)
        {
            OperationCanceledException error = RequireThrows<OperationCanceledException>(execute);
            Require(error.CancellationToken == token, "Cancellation must retain the request token.");
        }

        private sealed class FaultTool : IVisionTool
        {
            public string Name => "Owned operation fault";
            public VisionToolResult Execute(Mat source) => throw new InvalidOperationException("Owned Tool fault; no input mutation.");
        }

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Counter();
    }
}
