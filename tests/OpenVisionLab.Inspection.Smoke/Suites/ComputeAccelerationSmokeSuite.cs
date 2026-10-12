using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using OpenCvSharp;
using OpenVisionLab.Compute;
using OpenVisionLab.Compute.Infrastructure;
using OpenVisionLab.Vision2D.Pipeline;
using OpenVisionLab.Vision2D.Tool;
using OpenVisionLab.Vision3D.FeatureExtraction;
using static OpenVisionLab.Inspection.Smoke.SmokeAssert;

namespace OpenVisionLab.Inspection.Smoke
{
    internal static class ComputeAccelerationSmokeSuite
    {
        internal static IEnumerable<SmokeCase> Cases()
        {
            yield return new SmokeCase("Compute defaults snapshot options without loading CUDA", TestDefaults);
            yield return new SmokeCase("Compute selects device ordinals and retains immutable installation reports", TestSelection);
            yield return new SmokeCase("Compute shares one probe across concurrent callers and cancels waiting callers", TestConcurrency);
            yield return new SmokeCase("Compute cancels discovery without caching and recovers on the same session", TestCancellation);
            yield return new SmokeCase("Compute closes the host-owned lifetime after discovery", TestDisposal);
            yield return new SmokeCase("Compute ignores working-directory modules and runs with an absent optional DLL", TestMissingModule);
            yield return new SmokeCase("Compute preserves 2D factory CPU scores images validation cancellation and recovery", TestTwoD);
            yield return new SmokeCase("Compute preserves 3D CPU coverage poses no-match validation cancellation and recovery", TestThreeD);
            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("OVL_COMPUTE_TEST_MODULES")))
            {
                yield return new SmokeCase("Compute first-use detection-only module reports execution fallback and fresh-session recovery", () => TestFirstUse(Fixture(0), false), "native-fixture");
                yield return new SmokeCase("Compute native ABI device installation and malformed-output failure matrix", TestNativeFixtures, "native-fixture");
                yield return new SmokeCase("Compute releases a cancelled native probe and recovers without stale discovery", TestNativeCancellation, "native-fixture");
            }
            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("OVL_COMPUTE_REAL_PROBE")))
                yield return new SmokeCase("Compute actual Driver API device discovery keeps inspection on CPU", TestActualDevice, "cuda-probe");
            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("OVL_CUDA_TEST_MODULE")))
                yield return new SmokeCase("Compute first-use actual CUDA separates discovery initialization and application", () => TestFirstUse(Environment.GetEnvironmentVariable("OVL_CUDA_TEST_MODULE"), true), "cuda-device");
            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("OVL_CUDA_TEST_MODULE")) && !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("OVL_CUDA_EXECUTION_FIXTURES")))
                yield return new SmokeCase("Compute first-use initialization and execution failures retain precise support and CPU recovery", TestExecutionFailures, "native-fixture");
        }

        private static CudaSupportReport Devices()
        {
            return new CudaSupportReport(CudaFallbackReason.None, driverApiVersion: 12090,
                devices: new[] { new CudaDeviceInfo(0, "Synthetic 6.1", 6, 1, 3UL << 30), new CudaDeviceInfo(2, "Synthetic 8.6", 8, 6, 8UL << 30) });
        }

        private static void TestDefaults()
        {
            int calls = 0;
            VisionComputeOptions options = new VisionComputeOptions();
            using VisionComputeSession session = new VisionComputeSession(options, _ => { calls++; return Devices(); });
            options.UseCuda = true;
            options.DeviceOrdinal = 2;
            Require(!session.RequestedCuda && session.Support.Reason == CudaFallbackReason.Disabled && calls == 0, "Construction and option edits must not probe or change a session snapshot.");
            Require(!session.DescribeCpuExecution().AccelerationApplied && session.ProbeCuda().InstallationAction == CudaInstallationAction.None && calls == 0, "Disabled execution must never load a module.");
            RequireThrows<ArgumentNullException>(() => { using VisionComputeSession invalid = new VisionComputeSession(null); });
            RequireThrows<ArgumentOutOfRangeException>(() => { using VisionComputeSession invalid = new VisionComputeSession(new VisionComputeOptions { DeviceOrdinal = -1 }); });
        }

        private static void TestSelection()
        {
            int calls = 0;
            VisionComputeOptions options = new VisionComputeOptions { UseCuda = true, DeviceOrdinal = 2 };
            using VisionComputeSession session = new VisionComputeSession(options, _ => { calls++; return Devices(); });
            CudaSupportReport before = session.Support;
            options.DeviceOrdinal = 0;
            ComputeExecutionEvidence first = session.DescribeCpuExecution();
            Require(first.RequestedCuda && !first.AccelerationApplied && !first.Support.IsAccelerationAvailable && first.Support.SelectedDeviceOrdinal == 2,
                "Device discovery must retain the requested ordinal and never claim a kernel was applied.");
            Require(first.Support.Reason == CudaFallbackReason.None && first.Support.InstallationAction == CudaInstallationAction.None
                && first.FallbackReason == CudaFallbackReason.KernelNotImplemented, "Untested discovery must not demand an update; unsupported CPU work retains its separate fallback reason.");
            Require(ReferenceEquals(first.Support, session.ProbeCuda()) && calls == 1 && before.Reason == CudaFallbackReason.NotProbed, "Discovery must be cached while older snapshots stay unchanged.");
            Require(first.Support.Devices[1].ComputeCapabilityMajor == 8 && first.Support.Devices[1].TotalMemoryBytes == 8UL << 30, "Device capability and total memory must survive selection.");
            RequireThrows<NotSupportedException>(() => ((IList<CudaDeviceInfo>)first.Support.Devices).Clear());
            using VisionComputeSession unavailable = new VisionComputeSession(new VisionComputeOptions { UseCuda = true, DeviceOrdinal = 1 }, _ => Devices());
            Require(unavailable.ProbeCuda().Reason == CudaFallbackReason.RequestedDeviceUnavailable && unavailable.Support.SelectedDeviceOrdinal == null
                && unavailable.Support.Devices.Count == 2 && unavailable.Support.InstallationAction == CudaInstallationAction.SelectAvailableDeviceOrUseCpu, "Ordinal gaps must not silently select another GPU.");
            using VisionComputeSession automatic = new VisionComputeSession(new VisionComputeOptions { UseCuda = true }, _ => Devices());
            Require(automatic.ProbeCuda().SelectedDeviceOrdinal == 0, "Automatic selection must choose the first detected ordinal.");
        }

        private static void TestConcurrency()
        {
            int calls = 0;
            using ManualResetEventSlim entered = new ManualResetEventSlim();
            using ManualResetEventSlim release = new ManualResetEventSlim();
            using VisionComputeSession session = new VisionComputeSession(new VisionComputeOptions { UseCuda = true }, token =>
            {
                Interlocked.Increment(ref calls);
                entered.Set();
                Require(release.Wait(TimeSpan.FromSeconds(5), token), "Probe test gate timed out.");
                return Devices();
            });
            Task<CudaSupportReport> owner = Task.Run(() => session.ProbeCuda());
            Require(entered.Wait(TimeSpan.FromSeconds(5)), "The first probe did not start.");
            try
            {
                using CancellationTokenSource cancel = new CancellationTokenSource(100);
                Task waiter = Task.Run(() => RequireThrows<OperationCanceledException>(() => session.ProbeCuda(cancel.Token)));
                Require(waiter.Wait(TimeSpan.FromSeconds(5)), "A waiting caller must observe cancellation without waiting for the owner.");
                Task<CudaSupportReport>[] others = Enumerable.Range(0, 6).Select(_ => Task.Run(() => session.ProbeCuda())).ToArray();
                release.Set();
                Require(Task.WaitAll(others, TimeSpan.FromSeconds(5)) && owner.Wait(TimeSpan.FromSeconds(5)), "Concurrent discovery did not complete.");
                Require(calls == 1 && others.All(task => ReferenceEquals(task.Result, owner.Result)), "Concurrent callers must share exactly one successful discovery snapshot.");
            }
            finally { release.Set(); owner.GetAwaiter().GetResult(); }
        }

        private static void TestCancellation()
        {
            int calls = 0;
            using CancellationTokenSource cancel = new CancellationTokenSource();
            using VisionComputeSession session = new VisionComputeSession(new VisionComputeOptions { UseCuda = true }, _ =>
            {
                if (++calls == 1) cancel.Cancel();
                return Devices();
            });
            RequireThrows<OperationCanceledException>(() => session.ProbeCuda(cancel.Token));
            Require(session.Support.Reason == CudaFallbackReason.NotProbed && calls == 1, "Cancelled discovery must leave no cached result.");
            Require(session.ProbeCuda().DeviceDetected && calls == 2, "A valid retry must discover once after cancellation.");
            RequireThrows<OperationCanceledException>(() => session.DescribeCpuExecution(cancel.Token));
            Require(calls == 2, "Pre-cancelled execution must not re-enter discovery.");
        }

        private static void TestDisposal()
        {
            using ManualResetEventSlim entered = new ManualResetEventSlim();
            using ManualResetEventSlim release = new ManualResetEventSlim();
            VisionComputeSession session = new VisionComputeSession(new VisionComputeOptions { UseCuda = true }, token =>
            {
                entered.Set();
                Require(release.Wait(TimeSpan.FromSeconds(5), token), "Lifetime test gate timed out.");
                return Devices();
            });
            Task probe = Task.Run(() => session.ProbeCuda());
            Require(entered.Wait(TimeSpan.FromSeconds(5)), "Lifetime probe did not enter.");
            Task dispose = Task.Run(session.Dispose);
            try
            {
                Require(!dispose.Wait(100), "Dispose must wait for an owned discovery to finish.");
                release.Set();
                Require(Task.WaitAll(new[] { probe, dispose }, TimeSpan.FromSeconds(5)), "Session did not close after discovery.");
                RequireThrows<ObjectDisposedException>(() => session.ProbeCuda());
                RequireThrows<ObjectDisposedException>(() => { _ = session.Support; });
                session.Dispose();
            }
            finally { release.Set(); Task.WaitAll(probe, dispose); session.Dispose(); }
        }

        private static void TestMissingModule()
        {
            string directory = Path.GetDirectoryName(typeof(VisionComputeSession).Assembly.Location);
            Require(!File.Exists(Path.Combine(directory, CudaNativeProbe.ModuleFileName)), "This case requires an isolated CPU output directory.");
            string previous = Environment.CurrentDirectory;
            string isolated = Path.Combine(Path.GetTempPath(), "ovl-cwd-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(isolated);
            string impostor = Path.Combine(isolated, CudaNativeProbe.ModuleFileName);
            File.WriteAllText(impostor, "working-directory DLL must not be loaded");
            try
            {
                Environment.CurrentDirectory = isolated;
                using VisionComputeSession session = new VisionComputeSession(new VisionComputeOptions { UseCuda = true });
                CudaSupportReport report = session.ProbeCuda();
                Require(report.Reason == (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) && RuntimeInformation.ProcessArchitecture == Architecture.X64
                    ? CudaFallbackReason.NativeModuleMissing : CudaFallbackReason.PlatformUnsupported), "Missing CUDA must return a report rather than a loader exception.");
                if (report.ModulePath != null) Require(report.ModulePath.StartsWith(directory, StringComparison.OrdinalIgnoreCase), "Discovery must not follow the working directory.");
                Require(!session.DescribeCpuExecution().AccelerationApplied, "Missing CUDA must retain a usable CPU session.");
                DeterministicRigidSurfacePoseSearchResult result = new DeterministicRigidSurfacePoseSearchTool(session).Execute(Model(), Model(), SurfaceSearchRobustnessSmokeSuite.Options());
                Require(result.Success && result.Matched && result.ComputeDiagnostics.Support == report && !result.ComputeDiagnostics.AccelerationApplied, "A deployed CPU caller must work without the optional native module.");
            }
            finally { Environment.CurrentDirectory = previous; File.Delete(impostor); Directory.Delete(isolated); }
        }

        private static VisionPipelineStep EdgeStep(Mat template, out byte[] encoded)
        {
            encoded = template.ToBytes(".png");
            VisionPipelineStep step = new VisionPipelineStep { Name = "Compute CPU parity", ToolType = "EdgeBasedTemplateMatching" };
            step.Parameters["SCORE_MIN"] = "0.6";
            step.Parameters["USE_DRAW_IMAGE"] = "false";
            step.Parameters["SEARCH_STEP"] = "2";
            step.Parameters["MAX_TEMPLATE_POINTS"] = "400";
            step.Artifacts.Add(new VisionPipelineArtifactReference(VisionPipelineToolFactory.TemplateArtifactRole, "fixture/edge", VisionPipelineToolFactory.EncodedImageArtifactFormat,
                VisionPipelineToolFactory.EncodedImageArtifactFormatVersion, Convert.ToHexString(SHA256.HashData(encoded))));
            return step;
        }

        private static void TestTwoD()
        {
            using Mat template = EdgePoseRefinementSmokeSuite.CreateTemplate();
            using Mat source = EdgePoseRefinementSmokeSuite.CreateSource(template, 0, 1, 43, 29);
            VisionPipelineStep step = EdgeStep(template, out byte[] encoded);
            int calls = 0;
            using VisionComputeSession session = new VisionComputeSession(new VisionComputeOptions { UseCuda = true }, _ => { calls++; return new CudaSupportReport(CudaFallbackReason.DriverMissing); });
            using EdgeBasedTemplateMatchingTool baseline = (EdgeBasedTemplateMatchingTool)VisionPipelineToolFactory.Create(step, _ => encoded);
            using EdgeBasedTemplateMatchingTool tool = (EdgeBasedTemplateMatchingTool)VisionPipelineToolFactory.Create(step, _ => encoded, session);
            Require(calls == 0 && tool.results.Count == 0, "Factory construction must not discover devices or run inspection.");
            using (VisionToolResult invalid = tool.Execute(null)) Require(!invalid.Success && invalid.ComputeDiagnostics == null && calls == 0, "Invalid input must not probe CUDA.");
            using CancellationTokenSource cancel = new CancellationTokenSource();
            cancel.Cancel();
            RequireThrows<OperationCanceledException>(() => tool.Execute(source, cancel.Token));
            Require(calls == 0, "Cancelled input must not discover devices.");
            using VisionToolResult expected = baseline.Execute(source);
            using VisionToolResult actual = tool.Execute(source);
            Require(expected.Success && actual.Success && expected.ComputeDiagnostics == null && actual.ComputeDiagnostics.FallbackReason == CudaFallbackReason.DriverMissing
                && !actual.ComputeDiagnostics.AccelerationApplied && calls == 1, "A missing driver must preserve successful CPU inspection and explicit fallback evidence.");
            Require(expected.Metrics.Count == actual.Metrics.Count && expected.Metrics.All(pair => actual.Metrics.TryGetValue(pair.Key, out double value) && value.Equals(pair.Value)), "CPU result metrics must remain identical.");
            Require(expected.ResultImage.ToBytes(".png").SequenceEqual(actual.ResultImage.ToBytes(".png")), "CPU result pixels must remain identical.");
            RequireThrows<OperationCanceledException>(() => tool.Execute(source, cancel.Token));
            Require(tool.results.Count == 0, "Pre-cancelled execution after a valid match must clear stale published rows.");
            using (VisionToolResult invalid = tool.Execute(null)) Require(!invalid.Success && invalid.ComputeDiagnostics == null, "Failed input must not retain the previous execution snapshot.");
            using (VisionToolResult recovered = tool.Execute(source)) Require(recovered.Success && ReferenceEquals(recovered.ComputeDiagnostics.Support, actual.ComputeDiagnostics.Support) && calls == 1, "The same Tool must recover using the cached session report.");
            tool.Dispose();
            Require(session.ProbeCuda().Reason == CudaFallbackReason.DriverMissing, "Tool disposal must not close a borrowed session.");
            using EdgeBasedTemplateMatchingTool legacy = new EdgeBasedTemplateMatchingTool();
            RequireThrows<InvalidOperationException>(() => VisionPipelineToolFactory.Create(step, null));
        }

        private static SurfaceMatchSample[] Model()
        {
            return SurfaceSearchRobustnessSmokeSuite.Points(new[] { 0.0, 0, 0 }, new[] { 3.0, 0, 0 }, new[] { 0.0, 4, 0 }, new[] { 0.0, 0, 5 });
        }

        private static void TestThreeD()
        {
            SurfaceMatchSample[] model = Model();
            SurfaceMatchSample[] scene = SurfaceSearchRobustnessSmokeSuite.Points(new[] { 10.0, -4, 2 }, new[] { 13.0, -4, 2 }, new[] { 10.0, 0, 2 }, new[] { 10.0, -4, 7 });
            DeterministicRigidSurfacePoseSearchOptions options = SurfaceSearchRobustnessSmokeSuite.Options();
            int calls = 0;
            using VisionComputeSession session = new VisionComputeSession(new VisionComputeOptions { UseCuda = true }, _ => { calls++; return Devices(); });
            DeterministicRigidSurfacePoseSearchTool tool = new DeterministicRigidSurfacePoseSearchTool(session);
            Require(!tool.Execute(null, scene, options).Success && calls == 0, "Invalid 3D input must not probe.");
            options.MaximumCandidateCount = 0;
            Require(!tool.Execute(model, scene, options).Success && calls == 0, "Invalid budgets must fail before discovery.");
            options.MaximumCandidateCount = 10000;
            using CancellationTokenSource cancel = new CancellationTokenSource();
            cancel.Cancel();
            RequireThrows<OperationCanceledException>(() => tool.Execute(model, scene, options, cancel.Token));
            Require(calls == 0, "Cancelled 3D input must not discover devices.");
            DeterministicRigidSurfacePoseSearchResult expected = new DeterministicRigidSurfacePoseSearchTool().Execute(model, scene, options);
            DeterministicRigidSurfacePoseSearchResult actual = tool.Execute(model, scene, options);
            Require(expected.Success && expected.Matched && actual.Success && actual.Matched && actual.Coverage.MatchedModelSampleCount == 4
                && actual.Coverage.InlierRmse == 0 && expected.EvaluatedCandidateCount == actual.EvaluatedCandidateCount, "CPU coverage and candidate ranking must remain unchanged.");
            RequireApproximately(actual.Pose.TranslationX, 10, 0, "Independent X translation changed.");
            RequireApproximately(actual.Pose.TranslationY, -4, 0, "Independent Y translation changed.");
            RequireApproximately(actual.Pose.TranslationZ, 2, 0, "Independent Z translation changed.");
            Require(expected.Coverage.Matches.Select(match => (match.ModelSampleOrder, match.SceneSampleOrder, match.Distance))
                .SequenceEqual(actual.Coverage.Matches.Select(match => (match.ModelSampleOrder, match.SceneSampleOrder, match.Distance))), "CPU correspondences must retain the original sample identities and residuals.");
            Require(actual.ComputeDiagnostics.RequestedCuda && !actual.ComputeDiagnostics.AccelerationApplied && actual.ComputeDiagnostics.Support.DeviceDetected && calls == 1, "GPU discovery must still identify the executed CPU path.");
            options.MinimumTranslationX = 0;
            options.MaximumTranslationX = 1;
            DeterministicRigidSurfacePoseSearchResult noMatch = tool.Execute(model, scene, options);
            Require(noMatch.Success && !noMatch.Matched && ReferenceEquals(noMatch.ComputeDiagnostics.Support, actual.ComputeDiagnostics.Support), "CPU no-match evidence must preserve the shared report.");
            options.MaximumTranslationX = 100;
            Require(tool.Execute(model, scene, options).Matched && calls == 1, "A valid recovery must retain one discovery per session.");
            using EdgeBasedTemplateMatchingTool borrowed = new EdgeBasedTemplateMatchingTool(session);
            borrowed.Dispose();
            Require(session.ProbeCuda().DeviceDetected, "2D and 3D borrowers must share the host lifetime.");
        }

        private static void TestFirstUse(string modulePath, bool executeCuda)
        {
            using Mat template = EdgePoseRefinementSmokeSuite.CreateTemplate();
            using Mat small = EdgePoseRefinementSmokeSuite.CreateSource(template, 0, 1, 43, 29);
            VisionPipelineStep step = EdgeStep(template, out byte[] encoded);
            int probes = 0;
            VisionComputeOptions options = new VisionComputeOptions { UseCuda = true };
            using VisionComputeSession session = new VisionComputeSession(options, token => { probes++; return CudaNativeProbe.Probe(modulePath, token); });
            options.UseCuda = false;
            using EdgeBasedTemplateMatchingTool tool = (EdgeBasedTemplateMatchingTool)VisionPipelineToolFactory.Create(step, _ => encoded, session);
            using EdgeBasedTemplateMatchingTool cpu = (EdgeBasedTemplateMatchingTool)VisionPipelineToolFactory.Create(step, _ => encoded);
            Require(session.RequestedCuda && session.Support.Reason == CudaFallbackReason.NotProbed && probes == 0 && tool.results.Count == 0,
                "Copied options and restored factory state must not probe or inspect.");
            CudaSupportReport discovered = session.ProbeCuda();
            Require(discovered.DeviceDetected && discovered.Reason == CudaFallbackReason.None && !discovered.IsAccelerationAvailable
                && discovered.InstallationAction == CudaInstallationAction.None && probes == 1, "Successful discovery must remain unqualified without an update instruction.");
            Require(session.DescribeCpuExecution().FallbackReason == CudaFallbackReason.KernelNotImplemented && ReferenceEquals(session.Support, discovered),
                "An unsupported CPU algorithm must not convert untested discovery into a module failure.");
            using VisionToolResult expectedSmall = cpu.Execute(small);
            using (VisionToolResult actualSmall = tool.Execute(small))
            {
                RequireCpuParity(expectedSmall, actualSmall);
                Require(actualSmall.ComputeDiagnostics.FallbackReason == CudaFallbackReason.WorkloadTooSmall
                    && !session.Support.IsAccelerationAvailable && ReferenceEquals(session.Support, discovered), "A small grid must not initialize or qualify kernels.");
            }

            double[] sourceX = { 1, -1, 0, 0.5 };
            bool applied = session.TryScoreEdgeGrid(sourceX, new double[4], new[] { 0 }, new[] { 1.0 }, new[] { 0.0 }, new[] { -2.0 },
                4, 0, 0, 4, 1, 1, false, CancellationToken.None, out double[] scores, out ComputeExecutionEvidence evidence);
            Require(applied == executeCuda && evidence.AccelerationApplied == executeCuda, "Only a completed kernel batch may report applied acceleration.");
            Require(discovered.Reason == CudaFallbackReason.None && !discovered.IsAccelerationAvailable && discovered.InstallationAction == CudaInstallationAction.None,
                "An earlier discovery snapshot must remain immutable after initialization.");
            if (!executeCuda)
            {
                Require(scores == null && evidence.FallbackReason == CudaFallbackReason.KernelNotImplemented
                    && session.Support.Reason == CudaFallbackReason.KernelNotImplemented && !session.Support.IsAccelerationAvailable
                    && session.Support.InstallationAction == CudaInstallationAction.UpdateAccelerationModule, "A detection-only DLL needs an execution module only after explicit work is attempted.");
                using VisionToolResult recovered = tool.Execute(small);
                RequireCpuParity(expectedSmall, recovered);
                Require(recovered.ComputeDiagnostics.FallbackReason == CudaFallbackReason.KernelNotImplemented,
                    "Small-grid CPU recovery must retain the remembered execution-module failure.");
                using VisionComputeSession retry = new VisionComputeSession(new VisionComputeOptions { UseCuda = true }, token => CudaNativeProbe.Probe(modulePath, token));
                Require(retry.Support.Reason == CudaFallbackReason.NotProbed && retry.ProbeCuda().Reason == CudaFallbackReason.None
                    && retry.Support.InstallationAction == CudaInstallationAction.None && !retry.Support.IsAccelerationAvailable, "A new session must clear remembered failure without running kernels.");
            }
            else
            {
                Require(evidence.FallbackReason == CudaFallbackReason.None && session.Support.IsAccelerationAvailable
                    && session.Support.Reason == CudaFallbackReason.None && session.Support.InstallationAction == CudaInstallationAction.None
                    && scores.SequenceEqual(sourceX), "Actual CUDA initialization and independent FP64 results must qualify capability separately from discovery.");
                using Mat large = new Mat(512, 512, MatType.CV_8UC1, Scalar.All(17));
                using (Mat patch = new Mat(large, new Rect(43, 29, template.Width, template.Height))) template.CopyTo(patch);
                using VisionToolResult expected = cpu.Execute(large);
                using VisionToolResult actual = tool.Execute(large);
                Require(expected.Success && actual.Success && actual.ComputeDiagnostics.AccelerationApplied
                    && actual.ComputeDiagnostics.FallbackReason == CudaFallbackReason.None, "The eligible public Edge grid must report its own actual CUDA application.");
                Require(expected.Metrics.Count == actual.Metrics.Count && expected.Metrics.All(pair => actual.Metrics.TryGetValue(pair.Key, out double value) && value.Equals(pair.Value)),
                    "Public CPU/CUDA non-time metrics must remain equal.");
                Require(expected.ResultImage.ToBytes(".png").SequenceEqual(actual.ResultImage.ToBytes(".png")), "Public CPU/CUDA result pixels must remain equal.");
                DeterministicRigidSurfacePoseSearchResult threeD = new DeterministicRigidSurfacePoseSearchTool(session).Execute(Model(), Model(), SurfaceSearchRobustnessSmokeSuite.Options());
                Require(threeD.Success && threeD.Matched && !threeD.ComputeDiagnostics.AccelerationApplied
                    && threeD.ComputeDiagnostics.FallbackReason == CudaFallbackReason.NoMeasuredBenefit && session.Support.IsAccelerationAvailable,
                    "Qualified CUDA capability must not imply public 3D acceleration.");
            }
            Require(probes == 1, "Execution and CPU recovery must retain the session's cached discovery.");
            Console.WriteLine("CUDA first-use | actualDevice=" + executeCuda + " | module=" + modulePath + " | SHA256=" + Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(modulePath)))
                + " | discovery=None/available:false/action:None | execution=" + evidence.FallbackReason + " | applied=" + evidence.AccelerationApplied);
        }

        private static void TestExecutionFailures()
        {
            string directory = Environment.GetEnvironmentVariable("OVL_CUDA_EXECUTION_FIXTURES");
            using Mat template = EdgePoseRefinementSmokeSuite.CreateTemplate();
            using Mat source = EdgePoseRefinementSmokeSuite.CreateSource(template, 0, 1, 43, 29);
            VisionPipelineStep step = EdgeStep(template, out byte[] encoded);
            using EdgeBasedTemplateMatchingTool cpu = (EdgeBasedTemplateMatchingTool)VisionPipelineToolFactory.Create(step, _ => encoded);
            using VisionToolResult expected = cpu.Execute(source);
            foreach ((int kind, CudaFallbackReason reason, CudaInstallationAction action) in new[] {
                (1, CudaFallbackReason.KernelInitializationFailed, CudaInstallationAction.UseCpu),
                (6, CudaFallbackReason.NativeAbiMismatch, CudaInstallationAction.UseCompatibleAccelerationModule),
                (3, CudaFallbackReason.GpuExecutionFailed, CudaInstallationAction.UseCpu),
                (4, CudaFallbackReason.InvalidKernelResult, CudaInstallationAction.UseCpu),
                (7, CudaFallbackReason.InvalidKernelResult, CudaInstallationAction.UseCpu) })
            {
                string path = Path.Combine(directory, "execution-" + kind + ".dll");
                IntPtr module = NativeLibrary.Load(path);
                try
                {
                    LiveProbes live = Marshal.GetDelegateForFunctionPointer<LiveProbes>(NativeLibrary.GetExport(module, "ovl_test_live_executions"));
                    using VisionComputeSession session = new VisionComputeSession(new VisionComputeOptions { UseCuda = true }, token => CudaNativeProbe.Probe(path, token));
                    CudaSupportReport discovered = session.ProbeCuda();
                    Require(discovered.Reason == CudaFallbackReason.None && discovered.InstallationAction == CudaInstallationAction.None, "Failure fixtures must begin with successful untested discovery.");
                    bool applied = session.TryScoreEdgeGrid(new[] { 1.0, -1.0, 0.0, 0.5, 1.0 }, new double[5], new[] { 0 }, new[] { 1.0 }, new[] { 0.0 }, new[] { -2.0 },
                        5, 0, 0, 5, 1, 1, false, CancellationToken.None, out _, out ComputeExecutionEvidence evidence);
                    Require(!applied && !evidence.AccelerationApplied && evidence.FallbackReason == reason && session.Support.Reason == reason
                        && session.Support.InstallationAction == action && session.Support.NativeErrorCode == evidence.NativeExecutionErrorCode
                        && !session.Support.IsAccelerationAvailable && live() == 0, "Initialization or execution failure must publish its actual condition and release partial handles: " + kind);
                    Require(session.DescribeCpuExecution().NativeExecutionErrorCode == evidence.NativeExecutionErrorCode, "General CPU diagnostics must retain the remembered native error: " + kind);
                    using EdgeBasedTemplateMatchingTool tool = (EdgeBasedTemplateMatchingTool)VisionPipelineToolFactory.Create(step, _ => encoded, session);
                    using VisionToolResult recovered = tool.Execute(source);
                    RequireCpuParity(expected, recovered);
                    Require(recovered.ComputeDiagnostics.FallbackReason == reason && ReferenceEquals(recovered.ComputeDiagnostics.Support, session.Support)
                        && discovered.Reason == CudaFallbackReason.None, "CPU recovery must retain failure diagnostics and immutable earlier discovery: " + kind);
                    using VisionComputeSession retry = new VisionComputeSession(new VisionComputeOptions { UseCuda = true }, token => CudaNativeProbe.Probe(path, token));
                    Require(retry.Support.Reason == CudaFallbackReason.NotProbed && retry.ProbeCuda().Reason == CudaFallbackReason.None
                        && !retry.Support.IsAccelerationAvailable && live() == 0, "A new session must reset failure without starting execution: " + kind);
                    Console.WriteLine("CUDA first-use fixture | kind=" + kind + " | fallback=" + reason + " | action=" + action + " | error=" + evidence.NativeExecutionErrorCode + " | live=" + live());
                }
                finally { NativeLibrary.Free(module); }
            }
        }

        private static void RequireCpuParity(VisionToolResult expected, VisionToolResult actual)
        {
            Require(expected.Success && actual.Success && !actual.ComputeDiagnostics.AccelerationApplied, "CPU fallback must preserve a successful inspection.");
            Require(expected.Metrics.Count == actual.Metrics.Count && expected.Metrics.All(pair => actual.Metrics.TryGetValue(pair.Key, out double value) && value.Equals(pair.Value)), "CPU fallback metrics must remain equal.");
            Require(expected.ResultImage.ToBytes(".png").SequenceEqual(actual.ResultImage.ToBytes(".png")), "CPU fallback pixels must remain equal.");
        }

        private static string Fixture(int kind) => Path.Combine(Environment.GetEnvironmentVariable("OVL_COMPUTE_TEST_MODULES"), "fixture-" + kind + ".dll");

        private static void TestNativeFixtures()
        {
            CudaFallbackReason[] expected = {
                CudaFallbackReason.None, CudaFallbackReason.NativeAbiMismatch, CudaFallbackReason.NativeExportMissing,
                CudaFallbackReason.DriverMissing, CudaFallbackReason.DriverApiMissing, CudaFallbackReason.DriverInitializationFailed,
                CudaFallbackReason.DeviceQueryFailed, CudaFallbackReason.NoCudaDevice, CudaFallbackReason.InvalidNativeResult,
                CudaFallbackReason.InvalidNativeResult, CudaFallbackReason.InvalidNativeResult, CudaFallbackReason.DeviceQueryFailed,
                CudaFallbackReason.None, CudaFallbackReason.InvalidNativeResult, CudaFallbackReason.InvalidNativeResult,
                CudaFallbackReason.InvalidNativeResult, CudaFallbackReason.InvalidNativeResult, CudaFallbackReason.NoCudaDevice,
                CudaFallbackReason.InvalidNativeResult, CudaFallbackReason.InvalidNativeResult, CudaFallbackReason.NativeExportMissing,
                CudaFallbackReason.InvalidNativeResult
            };
            for (int kind = 0; kind < expected.Length; kind++)
            {
                if (kind == 12) continue;
                string path = Fixture(kind);
                IntPtr reference = NativeLibrary.Load(path);
                try
                {
                    CudaSupportReport report = CudaNativeProbe.Probe(path, CancellationToken.None);
                    Require(report.Reason == expected[kind] && !report.IsAccelerationAvailable, "Native fixture " + kind + " returned " + report.Reason + ".");
                    Require(Marshal.GetDelegateForFunctionPointer<LiveProbes>(NativeLibrary.GetExport(reference, "ovl_test_live_probes"))() == 0, "Native probe leaked a handle on fixture " + kind + ".");
                    if (kind == 0) Require(report.Devices.Count == 3 && report.Devices[0].ComputeCapabilityMajor == 6 && report.Devices[2].ComputeCapabilityMajor == 12
                        && report.DriverApiVersion == 12090 && report.InstallationAction == CudaInstallationAction.None, "Untested synthetic devices must retain their identity without an update instruction.");
                    if (kind >= 3 && kind <= 5) Require(report.InstallationAction == CudaInstallationAction.InstallOrUpdateNvidiaDriver, "Driver failures must return an installation action.");
                }
                finally { NativeLibrary.Free(reference); }
                Require(GetModuleHandleW(path) == IntPtr.Zero, "Native fixture must be unloaded after all owned references close.");
            }
            CudaSupportReport missingDependency = CudaNativeProbe.Probe(Fixture(22), CancellationToken.None);
            Require(missingDependency.Reason == CudaFallbackReason.NativeModuleLoadFailed && missingDependency.NativeErrorCode == 126,
                "A missing dependency of an optional DLL must return a classified loader failure.");
            string malformed = Path.Combine(Path.GetTempPath(), "ovl-invalid-" + Guid.NewGuid().ToString("N") + ".dll");
            try
            {
                File.WriteAllText(malformed, "not a PE image");
                Require(CudaNativeProbe.Probe(malformed, CancellationToken.None).Reason == CudaFallbackReason.NativeModuleInvalidImage, "Malformed native images must return a safe loader failure.");
            }
            finally { File.Delete(malformed); }
        }

        private static void TestNativeCancellation()
        {
            string path = Fixture(12);
            IntPtr reference = NativeLibrary.Load(path);
            try
            {
                using VisionComputeSession session = new VisionComputeSession(new VisionComputeOptions { UseCuda = true }, token => CudaNativeProbe.Probe(path, token));
                using CancellationTokenSource cancel = new CancellationTokenSource(50);
                RequireThrows<OperationCanceledException>(() => session.ProbeCuda(cancel.Token));
                Require(session.Support.Reason == CudaFallbackReason.NotProbed, "Cancelled native queries must not leave discovery cached.");
                Require(Marshal.GetDelegateForFunctionPointer<LiveProbes>(NativeLibrary.GetExport(reference, "ovl_test_live_probes"))() == 0, "Cancelled native queries must release their probe.");
                Require(session.ProbeCuda().Devices.Count == 3, "The same session must recover after a cancelled native query.");
            }
            finally { NativeLibrary.Free(reference); }
            Require(GetModuleHandleW(path) == IntPtr.Zero, "Cancelled and recovered native module references must be released.");
        }

        private static void TestActualDevice()
        {
            string sourceModule = Environment.GetEnvironmentVariable("OVL_COMPUTE_REAL_PROBE");
            string directory = Path.GetDirectoryName(typeof(VisionComputeSession).Assembly.Location);
            Require(directory.StartsWith(@"D:\OpenVisionLab-TestData\", StringComparison.OrdinalIgnoreCase), "Opt-in deployment artifacts must stay in the physical D: test root.");
            foreach (string deployedModule in new[] { Path.Combine(directory, CudaNativeProbe.ModuleFileName), Path.Combine(directory, "runtimes", "win-x64", "native", CudaNativeProbe.ModuleFileName) })
            {
                Require(!File.Exists(deployedModule), "The deployment case must not replace an existing native module.");
                Directory.CreateDirectory(Path.GetDirectoryName(deployedModule));
                File.Copy(sourceModule, deployedModule);
                try
                {
                    using VisionComputeSession session = new VisionComputeSession(new VisionComputeOptions { UseCuda = true });
                    Require(session.Support.Reason == CudaFallbackReason.NotProbed, "Public construction must stay lazy even with an installed module.");
                    DeterministicRigidSurfacePoseSearchResult result = new DeterministicRigidSurfacePoseSearchTool(session).Execute(Model(), Model(), SurfaceSearchRobustnessSmokeSuite.Options());
                    Require(result.Success && result.Matched && !result.ComputeDiagnostics.AccelerationApplied && session.Support.DeviceDetected
                        && session.Support.Reason == CudaFallbackReason.None && session.Support.ModulePath == deployedModule
                        && !session.Support.IsAccelerationAvailable && session.Support.InstallationAction == CudaInstallationAction.None,
                        "Public device discovery must preserve CPU inspection without claiming kernel availability or an update requirement.");
                    using Mat template = EdgePoseRefinementSmokeSuite.CreateTemplate();
                    using Mat source = EdgePoseRefinementSmokeSuite.CreateSource(template, 0, 1, 43, 29);
                    VisionPipelineStep step = EdgeStep(template, out byte[] encoded);
                    using EdgeBasedTemplateMatchingTool tool = (EdgeBasedTemplateMatchingTool)VisionPipelineToolFactory.Create(step, _ => encoded, session);
                    using VisionToolResult twoD = tool.Execute(source);
                    Require(twoD.Success && ReferenceEquals(twoD.ComputeDiagnostics.Support, result.ComputeDiagnostics.Support), "The public 2D factory and 3D search must borrow the same cached discovery.");
                    Console.WriteLine("CUDA deployment | " + deployedModule);
                    Console.WriteLine("CUDA discovery | Driver API=" + session.Support.DriverApiVersion);
                    foreach (CudaDeviceInfo device in session.Support.Devices)
                        Console.WriteLine("CUDA discovery | ordinal=" + device.Ordinal + " | " + device.Name + " | CC=" + device.ComputeCapabilityMajor + "." + device.ComputeCapabilityMinor + " | totalBytes=" + device.TotalMemoryBytes);
                }
                finally { File.Delete(deployedModule); }
            }
        }

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int LiveProbes();
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)] private static extern IntPtr GetModuleHandleW(string name);
    }
}
