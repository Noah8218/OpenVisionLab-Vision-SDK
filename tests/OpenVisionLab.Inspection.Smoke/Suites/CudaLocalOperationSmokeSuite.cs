using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Threading;
using OpenCvSharp;
using OpenVisionLab.Compute;
using OpenVisionLab.Compute.Infrastructure;
using OpenVisionLab.Vision2D;
using OpenVisionLab.Vision2D.Pipeline;
using OpenVisionLab.Vision2D.Property;
using OpenVisionLab.Vision2D.Tool;
using OpenVisionLab.Vision3D.FeatureExtraction;
using OpenVisionLab.Vision3D.Geometry;
using static OpenVisionLab.Inspection.Smoke.SmokeAssert;

namespace OpenVisionLab.Inspection.Smoke
{
    internal static class CudaLocalOperationSmokeSuite
    {
        private static string EvidenceDirectory => Environment.GetEnvironmentVariable("OVL_CUDA_LOCAL_REPORTS");
        private static string FixtureDirectory => Environment.GetEnvironmentVariable("OVL_CUDA_LOCAL_FIXTURES");
        private static readonly List<object> Observations = new List<object>();
        private static readonly JsonSerializerOptions Json = CreateJsonOptions();

        internal static IEnumerable<SmokeCase> Cases()
        {
            yield return new SmokeCase("Cuda local defaults validation lazy construction and cancellation", Defaults);
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("OVL_CUDA_TEST_MODULE"))) yield break;
            yield return new SmokeCase("Cuda local median and local-median full numerical boundaries", Filters, "cuda-device");
            yield return new SmokeCase("Cuda local normal pitches missing derivatives validation and ordered samples", Normals, "cuda-device");
            yield return new SmokeCase("Cuda local Scharr full pixels strided ROI channels and validation recovery", Scharr, "cuda-device");
            yield return new SmokeCase("Cuda local independent median local-median plane and Scharr oracles", Oracles, "cuda-device");
            yield return new SmokeCase("Cuda local shared host session typed consumers factory and safe disposal", Consumers, "cuda-device");
            yield return new SmokeCase("Cuda local tiled median full numerical boundaries", MedianTiles, "cuda-device");
            yield return new SmokeCase("Cuda local tiled median budget boundaries independent oracle", MedianTileBudget, "cuda-device");
            yield return new SmokeCase("Cuda local actual scratch ceiling returns complete CPU result", MemoryCeiling, "cuda-device");
            if (!string.IsNullOrEmpty(FixtureDirectory))
            {
                yield return new SmokeCase("Cuda local adaptive VRAM tiles failure cancellation and recovery", AdaptiveBudget, "native-fixture");
                yield return new SmokeCase("Cuda local tiled median post-download failures cancellation and recovery", MedianTileFaults, "native-fixture");
                yield return new SmokeCase("Cuda local old missing discovery-only modules preserve CPU and old Edge", OldModules, "cuda-device");
                yield return new SmokeCase("Cuda local per-primitive post-launch failures discard outputs and recover", Faults, "native-fixture");
                yield return new SmokeCase("Cuda local per-primitive post-launch cancellation frees buffers and recovers", ActiveCancellation, "native-fixture");
            }
        }

        private static VisionComputeSession Session(string path = null, bool study = false) => new VisionComputeSession(
            new VisionComputeOptions { UseCuda = true }, token => path == null ? CudaNativeProbe.Probe(token) : CudaNativeProbe.Probe(path, token), study);

        private static void Defaults()
        {
            int probes = 0;
            var options = new VisionComputeOptions();
            using var session = new VisionComputeSession(options, _ => { probes++; throw new InvalidOperationException("Disabled session must not probe."); });
            options.UseCuda = true;
            var median = new DeterministicMedianFilterTool(session);
            var local = new DeterministicLocalMedianOutlierFilterTool(session);
            var normal = new HeightMapNormalPreparationTool(session);
            using var edge = Edge(session);
            Require(probes == 0 && session.Support.Reason == CudaFallbackReason.Disabled, "Construction must remain lazy and snapshot options.");
            double[] values = Enumerable.Repeat(1.0, 9).ToArray();
            CheckFilter("disabled", session, 3, 3, values, 3, false);
            CheckFilter("disabled-local", session, 3, 3, values, 3, false, true);
            CompareNormal("disabled-normal", new HeightMapNormalPreparationTool().Execute(Map(3, 3, values)), normal.Execute(Map(3, 3, values)), false);
            using Mat image = Image(3, 3, 1);
            using VisionToolResult result = edge.Execute(image);
            Require(result.Success && result.ComputeDiagnostics.FallbackReason == CudaFallbackReason.Disabled && probes == 0, "Disabled tools must run CPU without native discovery.");
            Require(!median.Execute(0, 3, values, new DeterministicMedianFilterOptions { KernelSize = 3 }).Success, "Bad dimensions must fail before GPU.");
            Require(!local.Execute(3, 3, values, new DeterministicLocalMedianOutlierFilterOptions { WindowSize = 3, MinimumValidNeighbors = 9, MaximumAbsoluteDeviation = 1 }).Success, "Bad neighbor capacity must fail before GPU.");
            Require(!normal.Execute(null).Success, "Null map must retain a controlled failure.");
            using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
            Cancelled(() => median.Execute(3, 3, values, new DeterministicMedianFilterOptions { KernelSize = 3 }, cancelled.Token));
            Cancelled(() => local.Execute(3, 3, values, LocalOptions(3), cancelled.Token));
            Cancelled(() => normal.Execute(Map(3, 3, values), cancellationToken: cancelled.Token));
            Cancelled(() => edge.Execute(image, cancelled.Token));
            Record("defaults-validation-precancel", new { probes, session.Support, success = result.Success });
        }

        private static void Filters()
        {
            using var session = Session();
            foreach (int kernel in new[] { 3, 5, 7 })
            foreach (var size in new[] { (1, 1), (2, 3), (7, 9), (17, 23) })
            {
                double[] values = Values(size.Item1, size.Item2);
                CheckFilter($"median-k{kernel}-{size}", session, size.Item1, size.Item2, values, kernel, true);
                CheckFilter($"local-k{kernel}-{size}", session, size.Item1, size.Item2, values, kernel, true, true);
            }
            double negativeZero = BitConverter.Int64BitsToDouble(long.MinValue);
            double[] zeroTies = { 0, negativeZero, -1, 1, 0, 0, -2, 2, 0 };
            CheckFilter("median-negative-zero-cpu", session, 3, 3, zeroTies, 7, false);
            CheckFilter("local-negative-zero-gpu", session, 3, 3, zeroTies, 7, true, true);
            foreach (double value in new[] { double.MaxValue, -double.MaxValue, double.Epsilon })
            {
                double[] extreme = Enumerable.Repeat(value, 6).ToArray();
                CheckFilter("median-extreme-" + value, session, 2, 3, extreme, 3, true);
                CheckFilter("local-extreme-" + value, session, 2, 3, extreme, 3, true, true);
            }
            double[] mixed = { double.NaN, double.PositiveInfinity, 2, double.NegativeInfinity, 7, 3, 4, 5, 6 };
            CheckFilter("median-missing-infinity", session, 3, 3, mixed, 3, true);
            CheckFilter("local-preserved-infinity", session, 3, 3, mixed, 3, true, true);
            double[] threshold = { 1, 1, 1, 1, 6, 1, 1, 1, 1 };
            CheckFilter("local-equality-threshold", session, 3, 3, threshold, 3, true, true, 5, 8);
            CheckFilter("local-insufficient-clipped-neighbors", session, 3, 3, threshold, 7, true, true, 5, 48);
            foreach (int invalidKernel in new[] { 0, 2, 9 })
            {
                var a = new DeterministicMedianFilterTool().Execute(3, 3, threshold, new DeterministicMedianFilterOptions { KernelSize = invalidKernel });
                var b = new DeterministicMedianFilterTool(session).Execute(3, 3, threshold, new DeterministicMedianFilterOptions { KernelSize = invalidKernel });
                EqualResult(a, b); Require(!b.Success && b.ComputeDiagnostics == null, "Invalid kernel must not submit GPU work.");
                Record("invalid-median-" + invalidKernel, new { a.Success, a.Message });
            }
            var allMissing = new DeterministicLocalMedianOutlierFilterTool(session).Execute(1, 1, new[] { double.NaN }, LocalOptions(3));
            Require(!allMissing.Success && allMissing.ComputeDiagnostics == null, "All-missing filter must fail validation before probing.");
            CheckFilter("filter-valid-after-invalid", session, 3, 3, threshold, 3, true);
            CheckFilter("median-multi-batch-tail", session, 129, 137, Values(129, 137), 7, true);
            CheckFilter("local-multi-batch-tail", session, 129, 137, Values(129, 137), 5, true, true);
        }

        private static void Normals()
        {
            using var session = Session(study: true);
            foreach (var size in new[] { (1, 1), (1, 9), (9, 1), (2, 3), (9, 11) })
            foreach (var pitch in new[] { (1.0, 1.0), (0.25, 2.5) })
            {
                var map = Map(size.Item1, size.Item2, Values(size.Item1, size.Item2), pitch.Item1, pitch.Item2);
                var options = new HeightMapNormalPreparationOptions { ExpectedNormal = new ThreeDPoint(0, 1, 0), MinimumAlignmentCosine = 0.7 };
                CompareNormal($"normal-{size}-pitch{pitch}", new HeightMapNormalPreparationTool().Execute(map, options), new HeightMapNormalPreparationTool(session).Execute(map, options), true);
            }
            foreach (double pitch in new[] { 1e-300, 1e100 })
            {
                var map = Map(2, 3, new[] { double.MaxValue, -double.MaxValue, 0, 2, 3, 4 }, pitch, pitch);
                CompareNormal("normal-extreme-pitch-" + pitch, new HeightMapNormalPreparationTool().Execute(map), new HeightMapNormalPreparationTool(session).Execute(map), true);
            }
            var missing = Map(3, 3, Enumerable.Repeat(double.NaN, 9).ToArray());
            CompareNormal("normal-all-missing", new HeightMapNormalPreparationTool().Execute(missing), new HeightMapNormalPreparationTool(session).Execute(missing), true);
            var invalid = new HeightMapNormalPreparationOptions { ExpectedNormal = new ThreeDPoint(0, 0, 0) };
            var mapValid = Map(3, 3, Enumerable.Repeat(2.0, 9).ToArray());
            var failed = new HeightMapNormalPreparationTool(session).Execute(mapValid, invalid);
            EqualResult(new HeightMapNormalPreparationTool().Execute(mapValid, invalid), failed);
            Require(!failed.Success && failed.ComputeDiagnostics == null, "Invalid expected normal must fail before CUDA.");
            CompareNormal("normal-valid-after-invalid", new HeightMapNormalPreparationTool().Execute(mapValid), new HeightMapNormalPreparationTool(session).Execute(mapValid), true);
            var tail = Map(129, 137, Values(129, 137));
            CompareNormal("normal-multi-batch-tail", new HeightMapNormalPreparationTool().Execute(tail), new HeightMapNormalPreparationTool(session).Execute(tail), true);
        }

        private static void Scharr()
        {
            using var session = Session();
            using var study = Session(study: true);
            foreach (var size in new[] { (1, 1), (1, 7), (7, 1), (9, 11), (17, 23) })
            foreach (int channels in new[] { 1, 3 })
            foreach (int axis in new[] { 0, 1 })
            {
                using Mat parent = Image(size.Item1 + 4, size.Item2 + 6, channels);
                using Mat roi = parent.SubMat(new Rect(2, 2, size.Item2, size.Item1));
                Require(CheckEdge($"scharr-{size}-c{channels}-axis{axis}-roi", session, roi, axis, false).FallbackReason == CudaFallbackReason.NoMeasuredBenefit, "Public Scharr must retain measured CPU policy.");
                CheckEdge($"scharr-study-{size}-c{channels}-axis{axis}-roi", study, roi, axis, true);
            }
            using Mat four = Image(9, 11, 4);
            CheckEdge("scharr-four-channel-cpu", session, four, 0, false);
            using Mat sixteen = new Mat(9, 11, MatType.CV_16UC1, new Scalar(4));
            CheckEdge("scharr-16u-cpu-contract", session, sixteen, 1, false);
            foreach (MatType depth in new[] { MatType.CV_16UC1, MatType.CV_32FC1, MatType.CV_64FC1 })
            {
                using Mat constant = new Mat(9, 11, depth, new Scalar(4));
                CheckEdge("scharr-distinct-depth-" + depth, session, constant, 1, false);
                using var cpuDepth = Edge(null, 1); using var actualDepth = cpuDepth.Execute(constant);
                Require(actualDepth.Success && Pixels(actualDepth.ResultImage).All(p => p == 0), "Distinct-depth constant Scharr must preserve stable zero pixels.");
            }
            using Mat source = Image(9, 11, 1);
            using var tool = Edge(session, 1);
            foreach (var derivative in new[] { (0, 0), (1, 1), (2, 0) })
            {
                tool.property.ScharrDegreeX = derivative.Item1; tool.property.ScharrDegreeY = derivative.Item2;
                using var invalid = tool.Execute(source);
                Require(!invalid.Success && invalid.ErrorCode == VisionToolErrorCode.EdgeDetectionInvalidDerivative && invalid.ComputeDiagnostics == null, "Invalid derivative must retain validation and no GPU evidence.");
                Record("invalid-scharr-" + derivative, new { invalid.Success, invalid.ErrorCode, invalid.Message });
            }
            tool.property.ScharrDegreeX = 0; tool.property.ScharrDegreeY = 1;
            using var recovered = tool.Execute(source);
            Require(recovered.Success && !recovered.ComputeDiagnostics.AccelerationApplied && recovered.ComputeDiagnostics.FallbackReason == CudaFallbackReason.NoMeasuredBenefit, "Same public tool must recover on CPU after invalid inputs.");
            foreach (var kind in new[] { EdgeDetectionToolType.Sobel, EdgeDetectionToolType.Canny, EdgeDetectionToolType.Laplacian })
                CheckEdge("other-edge-cpu-" + kind, session, source, 0, false, kind);
            using Mat tail = Image(129, 137, 1);
            CheckEdge("scharr-public-multi-batch-tail", session, tail, 1, false);
            CheckEdge("scharr-study-multi-batch-tail", study, tail, 1, true);
        }

        private static void Oracles()
        {
            using var session = Session(study: true);
            double[] impulse = { 1, 1, 1, 1, 99, 1, 1, 1, 1 };
            var median = new DeterministicMedianFilterTool(session).Execute(3, 3, impulse, new DeterministicMedianFilterOptions { KernelSize = 3 });
            Require(median.Values.All(v => v == 1) && median.ChangedCount == 1, "Independent median impulse oracle.");
            var local = new DeterministicLocalMedianOutlierFilterTool(session).Execute(3, 3, impulse, LocalOptions(3));
            Require(local.OutlierIndices.SequenceEqual(new[] { 4 }) && double.IsNaN(local.Values[4]) && local.Values.Where((v, i) => i != 4).All(v => v == 1), "Independent center-excluded local impulse oracle.");
            var normal = new HeightMapNormalPreparationTool(session).Execute(Map(3, 3, new double[9], 2, 4));
            Require(normal.CalculatedNormalCount == 9 && normal.CentralDerivativeCount == 6 && normal.OneSidedDerivativeCount == 12 && normal.MissingDerivativeCount == 0, "Independent flat plane counts.");
            Require(normal.Samples.All(s => Bits(s.Normal.X) == long.MinValue && s.Normal.Y == 1 && Bits(s.Normal.Z) == long.MinValue), "Flat normal must preserve negative zero.");
            using Mat ramp = new Mat(3, 3, MatType.CV_8UC1);
            for (int y = 0; y < 3; y++) for (int x = 0; x < 3; x++) ramp.Set(y, x, (byte)x);
            using var tool = Edge(session);
            using var edge = tool.Execute(ramp);
            byte[] expected = { 0, 32, 0, 0, 32, 0, 0, 32, 0 };
            Require(Pixels(edge.ResultImage).SequenceEqual(expected), "Independent Reflect101 Scharr ramp oracle.");
            Record("independent-oracles", new { median.ChangedCount, local.OutlierIndices, normal.CalculatedNormalCount, pixels = Convert.ToHexString(expected) });
        }

        private static void Consumers()
        {
            var options = new VisionComputeOptions { UseCuda = true };
            using var session = new VisionComputeSession(options);
            options.UseCuda = false;
            var median = new DeterministicMedianFilterTool(session);
            var local = new DeterministicLocalMedianOutlierFilterTool(session);
            var normal = new HeightMapNormalPreparationTool(session);
            var step = new VisionPipelineStep { ToolType = "edgeDetection" };
            step.Parameters[nameof(EdgeDetectionToolProperty.EdgeType)] = "Scharr";
            step.Parameters[nameof(EdgeDetectionToolProperty.ScharrDegreeX)] = "0";
            step.Parameters[nameof(EdgeDetectionToolProperty.ScharrDegreeY)] = "1";
            using var edge = (EdgeDetectionTool)VisionPipelineToolFactory.Create(step, null, session);
            Require(session.Support.Reason == CudaFallbackReason.NotProbed, "Factory construction/restoration must not probe or execute.");
            var first = median.Execute(9, 11, Values(9, 11), new DeterministicMedianFilterOptions { KernelSize = 3 });
            var second = local.Execute(9, 11, first.Values, LocalOptions(3));
            var third = normal.Execute(Map(9, 11, second.Values.ToArray()));
            foreach (var evidence in new[] { first.ComputeDiagnostics, second.ComputeDiagnostics }) Applied(evidence, true);
            Applied(third.ComputeDiagnostics, false);
            Require(third.ComputeDiagnostics.FallbackReason == CudaFallbackReason.NoMeasuredBenefit, "Public normal must retain CPU after the measured adoption decision.");
            EqualResult(new HeightMapNormalPreparationTool().Execute(Map(9, 11, second.Values.ToArray())), third);
            Require(second.ComputeDiagnostics.InitializationMilliseconds < first.ComputeDiagnostics.InitializationMilliseconds && third.ComputeDiagnostics.Support.IsAccelerationAvailable, "Serial consumers must reuse their host session.");
            var adapter = new ThreeDToolAdapter<HeightMapNormalPreparationResult>("Normal", token => normal.Execute(Map(9, 11, second.Values.ToArray()), cancellationToken: token));
            var report = ThreeDToolExecutionRunner.Run(new IThreeDToolAdapter[] { adapter });
            Require(report.CompletedStepCount == 1, "Existing typed runner must consume the integrated normal tool.");
            using Mat image = Image(9, 11, 1);
            using var result = edge.Execute(image);
            Applied(result.ComputeDiagnostics, false);
            Require(result.ComputeDiagnostics.FallbackReason == CudaFallbackReason.NoMeasuredBenefit, "Factory must preserve public Scharr adoption policy.");
            edge.Dispose();
            CheckFilter("after-borrowed-tool-dispose", session, 9, 11, Values(9, 11), 3, true);
            session.Dispose();
            try { median.Execute(9, 11, Values(9, 11), new DeterministicMedianFilterOptions { KernelSize = 3 }); throw new InvalidOperationException("Disposed session must be rejected."); }
            catch (ObjectDisposedException) { }
            Record("serial-consumers-factory-lifetime", new { sessionWasLazy = true, typedSteps = report.CompletedStepCount, first = first.ComputeDiagnostics, second = second.ComputeDiagnostics, third = third.ComputeDiagnostics, edge = result.ComputeDiagnostics });
        }

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void SetBudget(ulong freeBytes, int mode);

        private static void AdaptiveBudget()
        {
            string path = Path.Combine(FixtureDirectory, "adaptive-budget/OpenVisionLab.Cuda.Native.dll");
            IntPtr module = NativeLibrary.Load(path);
            var budget = Marshal.GetDelegateForFunctionPointer<SetBudget>(NativeLibrary.GetExport(module, "ovl_test_budget"));
            var buffers = Marshal.GetDelegateForFunctionPointer<Count>(NativeLibrary.GetExport(module, "ovl_test_buffers"));
            var downloads = Marshal.GetDelegateForFunctionPointer<Count>(NativeLibrary.GetExport(module, "ovl_test_downloads"));
            var callback = Marshal.GetDelegateForFunctionPointer<SetAfterLaunch>(NativeLibrary.GetExport(module, "ovl_test_callback"));
            const int rows = 121, columns = 101;
            double[] input = Values(rows, columns);
            input[34 * columns + 7] = 99; input[35 * columns + 8] = double.NaN; input[36 * columns + 9] = double.PositiveInfinity;
            try
            {
                foreach (bool local in new[] { false, true })
                foreach (int kernel in new[] { 3, 5, 7 })
                {
                    budget(128 * 1024, 0);
                    using var session = Session(path);
                    var evidence = CheckFilter("adaptive-" + local + "-" + kernel, session, rows, columns, input, kernel, true, local);
                    Require(evidence.PeakScratchBytes <= 64UL * 1024 && downloads() > 1 && buffers() == 0, "Reduced budget must actually tile and release every buffer.");
                }
                foreach (int mode in new[] { 0, 1, 2, 3 })
                {
                    budget(128 * 1024, 0);
                    using var session = Session(path);
                    CheckFilter("adaptive-warmup-" + mode, session, 3, 3, Enumerable.Repeat(1.0, 9).ToArray(), 3, true);
                    budget(mode == 0 ? 512UL : 128UL * 1024, mode);
                    var failure = CheckFilter("adaptive-failure-" + mode, session, rows, columns, input, 3, false);
                    Require(failure.FallbackReason == (mode == 0 || mode == 2 ? CudaFallbackReason.GpuMemoryUnavailable : CudaFallbackReason.GpuExecutionFailed) && buffers() == 0, "Budget/driver/allocation/download failure must recover fully on CPU and free all buffers.");
                    budget(128 * 1024, 0);
                    using var fresh = Session(path);
                    CheckFilter("adaptive-fresh-" + mode, fresh, rows, columns, input, 3, true);
                }
                budget(128 * 1024, 0);
                using (var session = Session(path))
                using (var cancellation = new CancellationTokenSource())
                {
                    CheckFilter("adaptive-cancel-warmup", session, 3, 3, Enumerable.Repeat(1.0, 9).ToArray(), 3, true);
                    budget(128 * 1024, 0);
                    AfterLaunch cancel = () => cancellation.Cancel();
                    callback(cancel);
                    try { Cancelled(() => new DeterministicMedianFilterTool(session).Execute(rows, columns, input, new DeterministicMedianFilterOptions { KernelSize = 3 }, cancellation.Token)); }
                    finally { callback(null); GC.KeepAlive(cancel); }
                    Require(downloads() == 1 && buffers() == 0, "Cancellation after the first downloaded tile must publish no partial result and release buffers.");
                    budget(128 * 1024, 0);
                    CheckFilter("adaptive-cancel-retry", session, rows, columns, input, 3, true);
                }
                Record("adaptive-budget-proof", new { reportedFreeBytes = 128 * 1024, maximumScratchBytes = 64 * 1024, buffers = buffers(), boundary = "Owned reporting/fault fixture; actual NVIDIA allocations and kernels, serialized calls" });
            }
            finally { callback(null); budget(ulong.MaxValue, 0); NativeLibrary.Free(module); }
        }

        private static void MedianTiles()
        {
            using var session = Session();
            foreach (int kernel in new[] { 3, 5, 7 })
            foreach (bool local in new[] { false, true })
            {
                int columns = 2051, rows = 2047;
                double[] input = Values(rows, columns);
                int boundary = 64 * 1024 * 1024 / 16 / columns - kernel + 1;
                for (int row = boundary - 3; row <= boundary + 3; row++)
                {
                    input[row * columns + 7] = 99;
                    input[row * columns + 8] = double.NaN;
                    input[row * columns + 9] = double.PositiveInfinity;
                }
                input[0] = 99; input[input.Length - 1] = double.NaN;
                var clock = Stopwatch.StartNew();
                var medianOptions = new DeterministicMedianFilterOptions { KernelSize = kernel };
                var localOptions = LocalOptions(kernel);
                var cpuMedian = local ? null : new DeterministicMedianFilterTool().Execute(rows, columns, input, medianOptions);
                var cpuLocal = local ? new DeterministicLocalMedianOutlierFilterTool().Execute(rows, columns, input, localOptions) : null;
                double cpuMs = clock.Elapsed.TotalMilliseconds;
                clock.Restart();
                var gpuMedian = local ? null : new DeterministicMedianFilterTool(session).Execute(rows, columns, input, medianOptions);
                var gpuLocal = local ? new DeterministicLocalMedianOutlierFilterTool(session).Execute(rows, columns, input, localOptions) : null;
                double gpuMs = clock.Elapsed.TotalMilliseconds;
                IReadOnlyList<double> expected = local ? cpuLocal.Values : cpuMedian.Values;
                IReadOnlyList<double> actual = local ? gpuLocal.Values : gpuMedian.Values;
                Require(expected.Count == input.Length && actual.Count == input.Length, "Tiled output must cover every original cell.");
                for (int i = 0; i < expected.Count; i++) Require(double.IsNaN(expected[i]) && double.IsNaN(actual[i]) || Bits(expected[i]) == Bits(actual[i]), "Tiled output must preserve every CPU bit.");
                Require(local ? cpuLocal.OutlierIndices.SequenceEqual(gpuLocal.OutlierIndices) : cpuMedian.ChangedCount == gpuMedian.ChangedCount, "Tiled counts and original indices must match.");
                var evidence = local ? gpuLocal.ComputeDiagnostics : gpuMedian.ComputeDiagnostics;
                Applied(evidence, true);
                Require(evidence.PeakScratchBytes <= 64UL * 1024 * 1024, "Tiling must retain the scratch cap.");
                Record("tiled-" + kernel + "-" + local, new { rows, columns, boundary, kernel, local, cells = actual.Count, cpuMs, gpuMs, evidence });
            }
        }

        private static void MedianTileBudget()
        {
            using var session = Session();
            foreach (var size in new[] { (rows: 2048, columns: 2048), (rows: 4091, columns: 2049) })
            foreach (bool local in new[] { false, true })
            {
                double[] input = Enumerable.Repeat(1.0, checked(size.rows * size.columns)).ToArray();
                var median = local ? null : new DeterministicMedianFilterTool(session).Execute(size.rows, size.columns, input, new DeterministicMedianFilterOptions { KernelSize = 3 });
                var outliers = local ? new DeterministicLocalMedianOutlierFilterTool(session).Execute(size.rows, size.columns, input, LocalOptions(7)) : null;
                IReadOnlyList<double> values = local ? outliers.Values : median.Values;
                var evidence = local ? outliers.ComputeDiagnostics : median.ComputeDiagnostics;
                Require(values.Count == input.Length && values.All(value => Bits(value) == Bits(1.0)) && input.All(value => Bits(value) == Bits(1.0)), "An independent constant neighborhood oracle must preserve every output and input bit.");
                Require(local ? outliers.OutlierIndices.Count == 0 : median.ChangedCount == 0, "A constant grid has no changed cells or outliers.");
                Applied(evidence, true);
                Require(evidence.PeakScratchBytes <= 64UL * 1024 * 1024, "At-cap and three-tile calls must retain the same scratch cap.");
                Record("tile-budget-" + size.rows + "-" + local, new { size.rows, size.columns, local, cells = values.Count, oracle = "constant finite neighborhood = 1; zero changes/outliers", evidence });
            }
        }

        private static void MedianTileFaults()
        {
            string path = Path.Combine(FixtureDirectory, "tile-fault/OpenVisionLab.Cuda.Native.dll");
            IntPtr module = CudaNativeProbe.LoadLibraryExW(path, IntPtr.Zero, CudaNativeProbe.SearchDllDirectoryAndSystem32);
            Require(module != IntPtr.Zero, "Owned tile-fault module must load.");
            try
            {
                var fault = CudaNativeProbe.GetExport<SetFault>(module, "ovl_test_tile_fault");
                var callback = CudaNativeProbe.GetExport<SetAfterLaunch>(module, "ovl_test_tile_callback");
                var completed = CudaNativeProbe.GetExport<Count>(module, "ovl_test_completed_tiles");
                var buffers = CudaNativeProbe.GetExport<Count>(module, "ovl_test_live_buffers");
                foreach (bool local in new[] { false, true })
                {
                    using var session = Session(path);
                    int rows = 2047, columns = 2051;
                    double[] input = Values(rows, columns);
                    fault(9);
                    var evidence = CheckTileFaultFilter(session, input, rows, columns, local, false);
                    Require(evidence.FallbackReason == CudaFallbackReason.GpuExecutionFailed && evidence.NativeExecutionErrorCode == 719 && completed() == 1 && buffers() == 0, "A failure after the first output tile must discard all partial output and free buffers.");
                    fault(0);
                    CheckFilter("same-session-after-tile-fault", session, 3, 3, Enumerable.Repeat(1.0, 9).ToArray(), 3, false, local);
                    using var fresh = Session(path);
                    CheckFilter("fresh-after-tile-fault", fresh, 3, 3, Enumerable.Repeat(1.0, 9).ToArray(), 3, true, local);
                    using var cancellation = new CancellationTokenSource();
                    AfterLaunch cancel = () => cancellation.Cancel();
                    callback(cancel);
                    try { Cancelled(() => CheckTileFaultFilter(fresh, input, rows, columns, local, true, cancellation.Token)); }
                    finally { callback(null); GC.KeepAlive(cancel); }
                    Require(completed() == 1 && buffers() == 0, "Cancellation after a downloaded tile must release buffers.");
                    CheckFilter("after-tile-cancel-retry", fresh, 3, 3, Enumerable.Repeat(1.0, 9).ToArray(), 3, true, local);
                    Record("tile-failure-recovery-" + local, new { local, evidence, remainingBuffers = buffers() });
                }
            }
            finally { CudaNativeProbe.FreeLibrary(module); }
        }

        private static ComputeExecutionEvidence CheckTileFaultFilter(VisionComputeSession session, double[] input, int rows, int columns, bool local, bool applied, CancellationToken token = default)
        {
            if (local)
            {
                var expected = new DeterministicLocalMedianOutlierFilterTool().Execute(rows, columns, input, LocalOptions(3), CancellationToken.None);
                var actual = new DeterministicLocalMedianOutlierFilterTool(session).Execute(rows, columns, input, LocalOptions(3), token);
                Require(expected.OutlierIndices.SequenceEqual(actual.OutlierIndices) && expected.Values.Count == actual.Values.Count, "Failed tile local result must retain all indices.");
                for (int i = 0; i < expected.Values.Count; i++) Require(double.IsNaN(expected.Values[i]) && double.IsNaN(actual.Values[i]) || Bits(expected.Values[i]) == Bits(actual.Values[i]), "CPU recovery must replace every partial tile cell.");
                Applied(actual.ComputeDiagnostics, applied); return actual.ComputeDiagnostics;
            }
            var options = new DeterministicMedianFilterOptions { KernelSize = 3 };
            var a = new DeterministicMedianFilterTool().Execute(rows, columns, input, options, CancellationToken.None);
            var b = new DeterministicMedianFilterTool(session).Execute(rows, columns, input, options, token);
            Require(a.ChangedCount == b.ChangedCount && a.Values.Count == b.Values.Count, "Failed tile result must retain the full count.");
            for (int i = 0; i < a.Values.Count; i++) Require(double.IsNaN(a.Values[i]) && double.IsNaN(b.Values[i]) || Bits(a.Values[i]) == Bits(b.Values[i]), "CPU recovery must replace every partial tile cell.");
            Applied(b.ComputeDiagnostics, applied); return b.ComputeDiagnostics;
        }

        private static void MemoryCeiling()
        {
            using var session = Session();
            // 64 MiB native scratch / 16 bytes per cell, plus one cell: derived from the declared cap.
            double[] input = new double[64 * 1024 * 1024 / 16 + 1];
            var result = new DeterministicMedianFilterTool(session).Execute(1, input.Length, input, new DeterministicMedianFilterOptions { KernelSize = 3 });
            Require(result.Success && result.ChangedCount == 0 && result.Values.Count == input.Length && result.Values.All(v => Bits(v) == 0), "Memory fallback must produce the entire CPU output.");
            Require(!result.ComputeDiagnostics.AccelerationApplied && result.ComputeDiagnostics.FallbackReason == CudaFallbackReason.GpuMemoryUnavailable, "Real scratch ceiling must report memory fallback.");
            CheckFilter("after-actual-memory-fallback", session, 3, 3, Enumerable.Repeat(1.0, 9).ToArray(), 3, true);
            Record("actual-scratch-cap", new { cells = input.Length, result.Success, result.ChangedCount, evidence = result.ComputeDiagnostics });
        }

        private static void OldModules()
        {
            foreach (string name in new[] { "absent.dll", "discovery/OpenVisionLab.Cuda.Native.dll", "old/OpenVisionLab.Cuda.Native.dll" })
            {
                using var session = Session(Path.Combine(FixtureDirectory, name), study: true);
                foreach (string operation in new[] { "median", "local", "normal", "scharr" }) RunOperation(operation, session, false, "module-" + name);
                if (name.StartsWith("old/", StringComparison.Ordinal))
                    Require(session.TryScoreEdgeGrid(new[] { 1.0, -1, 0, 0.5 }, new double[4], new[] { 0 }, new[] { 1.0 }, new[] { 0.0 }, new[] { -2.0 }, 4, 0, 0, 4, 1, 1, false, CancellationToken.None, out var scores, out var evidence)
                        && scores.SequenceEqual(new[] { 1.0, -1, 0, 0.5 }) && evidence.AccelerationApplied, "Missing additive exports must preserve old Edge CUDA capability.");
            }
            using var fresh = Session(study: true);
            RunOperation("normal", fresh, true, "fresh-production-after-old");
        }

        private static void Faults()
        {
            string path = Path.Combine(FixtureDirectory, "fault/OpenVisionLab.Cuda.Native.dll");
            IntPtr module = CudaNativeProbe.LoadLibraryExW(path, IntPtr.Zero, CudaNativeProbe.SearchDllDirectoryAndSystem32);
            Require(module != IntPtr.Zero, "Owned real-driver fault module must load.");
            try
            {
                var fault = CudaNativeProbe.GetExport<SetFault>(module, "ovl_test_set_fault");
                var buffers = CudaNativeProbe.GetExport<Count>(module, "ovl_test_live_buffers");
                var live = CudaNativeProbe.GetExport<Count>(module, "ovl_test_live_executions");
                foreach (string operation in new[] { "median", "local", "normal", "scharr" })
                {
                    using (var session = Session(path, study: true))
                    {
                        fault(20);
                        var rejected = RunOperation(operation, session, false, "first-capability-malformed-timing-" + operation);
                        Require(rejected.FallbackReason == CudaFallbackReason.InvalidKernelResult && live() == 0 && buffers() == 0,
                            "An optional capability check with malformed timing must be rejected and released before application.");
                    }
                    using (var session = Session(path, study: true)) RunOperation(operation, session, true, "fresh-after-capability-rejection-" + operation);
                }
                foreach (string operation in new[] { "median", "local", "normal", "scharr" })
                foreach (int mode in new[] { 8, 9, 20, 21 })
                {
                    if (mode == 21 && operation == "scharr") continue;
                    using (var session = Session(path, study: true))
                    {
                        RunOperation(operation, session, true, "qualify-before-fault");
                        fault(mode);
                        ComputeExecutionEvidence evidence = RunOperation(operation, session, false, $"post-launch-{operation}-{mode}");
                        var expected = mode == 8 ? CudaFallbackReason.GpuMemoryUnavailable : mode == 9 ? CudaFallbackReason.GpuExecutionFailed : CudaFallbackReason.InvalidKernelResult;
                        Require(evidence.FallbackReason == expected && evidence.GpuBatchCount > 0 && buffers() == 0, "Fault must follow actual launch, discard output, free all call buffers and preserve precise CPU evidence.");
                        if (mode == 9) Require(evidence.NativeExecutionErrorCode == 719, "Native fault code must remain visible.");
                        RunOperation(operation, session, mode == 8, "same-session-recovery-" + mode);
                    }
                    Require(live() == 0 && buffers() == 0, "Session disposal must release every native handle/buffer.");
                    using var recovered = Session(path, study: true);
                    RunOperation(operation, recovered, true, "fresh-session-recovery-" + mode);
                }
            }
            finally { CudaNativeProbe.FreeLibrary(module); }
        }

        private static void ActiveCancellation()
        {
            string path = Path.Combine(FixtureDirectory, "fault/OpenVisionLab.Cuda.Native.dll");
            IntPtr module = CudaNativeProbe.LoadLibraryExW(path, IntPtr.Zero, CudaNativeProbe.SearchDllDirectoryAndSystem32);
            Require(module != IntPtr.Zero, "Cancellation fixture must load.");
            try
            {
                var afterLaunch = CudaNativeProbe.GetExport<SetAfterLaunch>(module, "ovl_test_after_launch");
                var buffers = CudaNativeProbe.GetExport<Count>(module, "ovl_test_live_buffers");
                var live = CudaNativeProbe.GetExport<Count>(module, "ovl_test_live_executions");
                foreach (string operation in new[] { "median", "local", "normal", "scharr" })
                {
                    using (var session = Session(path, study: true))
                    {
                        RunOperation(operation, session, true, "qualify-before-cancel");
                        using var cancellation = new CancellationTokenSource();
                        AfterLaunch callback = () => cancellation.Cancel();
                        afterLaunch(callback);
                        try { Cancelled(() => RunOperation(operation, session, false, "active-cancel", cancellation.Token)); }
                        finally { afterLaunch(null); GC.KeepAlive(callback); }
                        Require(buffers() == 0, "Actual launched cancellation must release call buffers before returning.");
                        RunOperation(operation, session, true, "same-session-after-active-cancel");
                    }
                    Require(live() == 0 && buffers() == 0, "Cancellation and disposal must leave no live native resources.");
                    Record("active-cancel-" + operation, new { launched = true, propagated = true, liveBuffers = buffers(), liveExecutions = live() });
                }
            }
            finally { CudaNativeProbe.FreeLibrary(module); }
        }

        private static ComputeExecutionEvidence RunOperation(string operation, VisionComputeSession session, bool applied, string label, CancellationToken token = default)
        {
            double[] input = Values(17, 23);
            if (operation == "median") return CheckFilter(label + "-median", session, 17, 23, input, 3, applied, token: token);
            if (operation == "local") return CheckFilter(label + "-local", session, 17, 23, input, 3, applied, true, token: token);
            if (operation == "normal")
            {
                var map = Map(17, 23, input);
                var result = new HeightMapNormalPreparationTool(session).Execute(map, cancellationToken: token);
                CompareNormal(label, new HeightMapNormalPreparationTool().Execute(map, cancellationToken: CancellationToken.None), result, applied);
                return result.ComputeDiagnostics;
            }
            using Mat image = Image(17, 23, 3);
            return CheckEdge(label, session, image, 0, applied, token: token);
        }

        private static ComputeExecutionEvidence CheckFilter(string label, VisionComputeSession session, int rows, int columns, double[] input,
            int kernel, bool applied, bool local = false, double limit = 5, int minimum = 1, CancellationToken token = default)
        {
            double[] unchanged = (double[])input.Clone();
            object a, b;
            ComputeExecutionEvidence evidence;
            if (local)
            {
                var options = new DeterministicLocalMedianOutlierFilterOptions { WindowSize = kernel, MinimumValidNeighbors = minimum, MaximumAbsoluteDeviation = limit };
                a = new DeterministicLocalMedianOutlierFilterTool().Execute(rows, columns, input, options, CancellationToken.None);
                var result = new DeterministicLocalMedianOutlierFilterTool(session).Execute(rows, columns, input, options, token);
                b = result; evidence = result.ComputeDiagnostics;
            }
            else
            {
                var options = new DeterministicMedianFilterOptions { KernelSize = kernel };
                a = new DeterministicMedianFilterTool().Execute(rows, columns, input, options, CancellationToken.None);
                var result = new DeterministicMedianFilterTool(session).Execute(rows, columns, input, options, token);
                b = result; evidence = result.ComputeDiagnostics;
            }
            EqualResult(a, b); Applied(evidence, applied);
            Require(input.Select(Bits).SequenceEqual(unchanged.Select(Bits)), "Tool must not mutate caller input.");
            Record(label, new { rows, columns, kernel, local, limit, minimum, input = Canonical(input), expected = Canonical(a), actual = Canonical(b), evidence });
            return evidence;
        }

        private static void CompareNormal(string label, HeightMapNormalPreparationResult a, HeightMapNormalPreparationResult b, bool applied)
        {
            EqualResult(a, b); Applied(b.ComputeDiagnostics, applied);
            Record(label, new { expected = Canonical(a), actual = Canonical(b), evidence = b.ComputeDiagnostics });
        }

        private static ComputeExecutionEvidence CheckEdge(string label, VisionComputeSession session, Mat input, int axis, bool applied,
            EdgeDetectionToolType kind = EdgeDetectionToolType.Scharr, CancellationToken token = default)
        {
            byte[] unchanged = Pixels(input);
            using var cpu = Edge(null, axis, kind); using var gpu = Edge(session, axis, kind);
            using var a = cpu.Execute(input); using var b = gpu.Execute(input, token);
            Require(a.Success == b.Success && a.ErrorCode == b.ErrorCode && a.Message == b.Message, "Edge status and failure contract must match CPU.");
            Require(Canonical(a.Metrics).ToJsonString() == Canonical(b.Metrics).ToJsonString()
                && Canonical(a.Overlays).ToJsonString() == Canonical(b.Overlays).ToJsonString(), "Edge metrics and overlays must retain their public contracts.");
            byte[] expectedPixels = Pixels(a.ResultImage), actualPixels = Pixels(b.ResultImage);
            if (!expectedPixels.SequenceEqual(actualPixels))
            {
                Record("FAILED-" + label, new { expected = Convert.ToHexString(expectedPixels), actual = Convert.ToHexString(actualPixels), evidence = b.ComputeDiagnostics });
                throw new InvalidOperationException("Every output pixel must match CPU: " + label);
            }
            Require(Pixels(input).SequenceEqual(unchanged), "Edge input must remain owned by caller.");
            if (a.Success) Applied(b.ComputeDiagnostics, applied); else Require(!applied, "Required GPU consumer must succeed.");
            Record(label, new { rows = input.Rows, columns = input.Cols, type = input.Type().ToString(), expected = PixelHash(a.ResultImage), actual = PixelHash(b.ResultImage), a.Success, a.ErrorCode, a.Message, evidence = b.ComputeDiagnostics });
            return b.ComputeDiagnostics;
        }

        private static EdgeDetectionTool Edge(VisionComputeSession session, int axis = 0, EdgeDetectionToolType kind = EdgeDetectionToolType.Scharr)
        {
            var tool = new EdgeDetectionTool(session);
            tool.SetProperty(new EdgeDetectionToolProperty { EdgeType = kind, ScharrDegreeX = axis == 0 ? 1 : 0, ScharrDegreeY = axis == 1 ? 1 : 0 });
            return tool;
        }

        private static HeightMap3D Map(int rows, int columns, double[] values, double columnPitch = 0.5, double rowPitch = 2) => new HeightMap3D(rows, columns, -3, 7, columnPitch, rowPitch, values, "mm", "fixture-frame", "fixture-source");
        private static DeterministicLocalMedianOutlierFilterOptions LocalOptions(int kernel) => new DeterministicLocalMedianOutlierFilterOptions { WindowSize = kernel, MaximumAbsoluteDeviation = 5, MinimumValidNeighbors = 1 };
        private static double[] Values(int rows, int columns)
        {
            double[] values = Enumerable.Range(0, rows * columns).Select(i => (i * 17 % 97 - 48) * 0.25 + 0.125).ToArray();
            if (values.Length > 8) { values[2] = double.NaN; values[values.Length / 2] = 99; }
            return values;
        }
        private static Mat Image(int rows, int columns, int channels)
        {
            var mat = new Mat(rows, columns, MatType.CV_8UC(channels));
            byte[] pixels = Enumerable.Range(0, rows * columns * channels).Select(i => (byte)((i * 37 + i / 11 * 17) % 256)).ToArray();
            Marshal.Copy(pixels, 0, mat.Data, pixels.Length);
            return mat;
        }
        private static byte[] Pixels(Mat image)
        {
            if (image == null || image.Empty()) return Array.Empty<byte>();
            int rowBytes = checked(image.Cols * (int)image.ElemSize());
            byte[] pixels = new byte[checked(image.Rows * rowBytes)];
            for (int row = 0; row < image.Rows; row++) Marshal.Copy(image.Ptr(row), pixels, row * rowBytes, rowBytes);
            return pixels;
        }
        private static string PixelHash(Mat image) => Convert.ToHexString(SHA256.HashData(Pixels(image)));
        private static long Bits(double value) => BitConverter.DoubleToInt64Bits(value);
        private static void Applied(ComputeExecutionEvidence evidence, bool expected) => Require(evidence != null && evidence.AccelerationApplied == expected && (!expected || evidence.FallbackReason == CudaFallbackReason.None && evidence.GpuBatchCount > 0), "Actual CUDA application/fallback evidence must match the required path.");
        private static void Cancelled(Action action)
        {
            try { action(); throw new InvalidOperationException("Cancellation must propagate."); }
            catch (OperationCanceledException) { }
        }
        private static JsonNode Canonical(object result)
        {
            JsonNode node = JsonSerializer.SerializeToNode(result, Json);
            if (node is JsonObject obj) obj.Remove("ComputeDiagnostics");
            return node;
        }
        private static void EqualResult(object expected, object actual) => Require(Canonical(expected).ToJsonString() == Canonical(actual).ToJsonString(), "Entire non-timing result contract must match CPU, including exact finite and signed-zero bits.");
        private static void Record(string id, object result)
        {
            Observations.Add(new { id, result });
            if (string.IsNullOrEmpty(EvidenceDirectory)) return;
            Directory.CreateDirectory(EvidenceDirectory);
            File.WriteAllText(Path.Combine(EvidenceDirectory, "local-cases.json"), JsonSerializer.Serialize(Observations, Json));
        }
        private static JsonSerializerOptions CreateJsonOptions()
        {
            var options = new JsonSerializerOptions { WriteIndented = true };
            options.Converters.Add(new DoubleBitsConverter());
            return options;
        }
        private sealed class DoubleBitsConverter : JsonConverter<double>
        {
            public override double Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options) => throw new NotSupportedException();
            public override void Write(Utf8JsonWriter writer, double value, JsonSerializerOptions options) => writer.WriteStringValue(double.IsNaN(value) ? "NaN" : BitConverter.DoubleToInt64Bits(value).ToString("X16", System.Globalization.CultureInfo.InvariantCulture));
        }
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void SetFault(int mode);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void AfterLaunch();
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void SetAfterLaunch(AfterLaunch callback);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Count();
    }
}
