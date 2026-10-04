using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using OpenCvSharp;
using OpenVisionLab.Compute;
using OpenVisionLab.Compute.Infrastructure;
using OpenVisionLab.Vision2D.Property;
using OpenVisionLab.Vision2D.Tool;
using OpenVisionLab.Vision3D.FeatureExtraction;
using static OpenVisionLab.Inspection.Smoke.SmokeAssert;

namespace OpenVisionLab.Inspection.Smoke
{
    internal static class CudaKernelSmokeSuite
    {
        private static readonly int[] ZeroOffset = { 0 };
        private static readonly int[] TwoOffsets = { 0, 1 };
        private static readonly double[] One = { 1.0 };
        private static readonly double[] Zero = { 0.0 };
        private static readonly double[] MinusTwo = { -2.0 };
        private static readonly double[] TwoOnes = { 1.0, 1.0 };
        private static readonly double[] AlternatingHalves = { 0.5, -0.5, 0.5, -0.5 };
        private static readonly double[] TwoHalves = { 0.5, 0.25 };
        private static readonly double[] RecoveryPoint = { 1.0, 2.0, 3.0 };
        private static readonly JsonSerializerOptions ReportOptions = new JsonSerializerOptions { WriteIndented = true };
        private static string ModulePath => Environment.GetEnvironmentVariable("OVL_CUDA_TEST_MODULE");
        private static VisionComputeSession Session(string path = null, bool study = true, long? tileBytes = null) => new VisionComputeSession(new VisionComputeOptions { UseCuda = true }, token => CudaNativeProbe.Probe(path ?? ModulePath, token), study, tileBytes);

        internal static IEnumerable<SmokeCase> Cases()
        {
            yield return new SmokeCase("Cuda tile layout bounds large shapes and preserves row-major bands", TestTileLayout);
            if (string.IsNullOrEmpty(ModulePath)) yield break;
            yield return new SmokeCase("Cuda tile actual FP64 scores preserve signed XY negative anchors tails and row order", TestTiles);
            yield return new SmokeCase("Cuda tile workflow preserves seams suppress refinement ROI pyramid hybrid and pose", TestTileWorkflow);
            yield return new SmokeCase("Cuda phase timings preserve opt-in results reset recovery and callback ownership", TestPhaseTimings);
            yield return new SmokeCase("Cuda strided workspace preserves changing source model geometry and input ownership", TestStridedInputs);
            yield return new SmokeCase("Cuda kernel full edge grids preserve FP64 early exit polarity stride and ties", TestGrids);
            yield return new SmokeCase("Cuda kernel transforms preserve full FP64 coordinates and overflow fallback", TestTransforms);
            yield return new SmokeCase("Cuda kernel 2D workflow preserves result threshold ROI cancellation and recovery", TestTwoD);
            yield return new SmokeCase("Cuda kernel 3D workflow preserves ordered correspondence residuals and pose", TestThreeD);
            yield return new SmokeCase("Cuda kernel repeated native execution disposes buffers sessions and borrowed tools", TestLifetime);
            yield return new SmokeCase("Cuda kernel production policy keeps measured slow paths on CPU", TestPolicy);
            if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("OVL_CUDA_EXECUTION_FIXTURES")))
            {
                yield return new SmokeCase("Cuda tile late faults discard poisoned candidates and retain legacy recovery", TestTileFaults);
                yield return new SmokeCase("Cuda tile cancellation stops consumption and waits safely for disposal", TestTileCancellation);
                yield return new SmokeCase("Cuda kernel faults recover CPU rows and release partial native handles", TestFaults);
                yield return new SmokeCase("Cuda kernel cancels active work and waiting callers before safe disposal", TestCancellationAndDisposal);
                if (File.Exists(Path.Combine(Environment.GetEnvironmentVariable("OVL_CUDA_EXECUTION_FIXTURES"), "execution-16.dll")))
                {
                    yield return new SmokeCase("Cuda workspace lifetime releases success cancellation callback faults and incomplete extensions", TestWorkspaceLifetime);
                    yield return new SmokeCase("Cuda workspace active cancellation waits for disposal and recovers public matches",
                        () => TestTileCancellation(Path.Combine(Environment.GetEnvironmentVariable("OVL_CUDA_EXECUTION_FIXTURES"), "execution-20.dll")));
                }
            }
        }


        private static void TestPhaseTimings()
        {
            using Mat template = Template(48); using Mat source = Source(template, 512);
            using VisionComputeSession session = Session(tileBytes: 1024 * 1024);
            using EdgeBasedTemplateMatchingTool cpu = Tool(template, null);
            using EdgeBasedTemplateMatchingTool gpu = Tool(template, session);
            cpu.property.USE_PYRAMID_POSITION_PROPOSAL = gpu.property.USE_PYRAMID_POSITION_PROPOSAL = false;
            using VisionToolResult expected = cpu.Execute(source);
            using (VisionToolResult disabled = gpu.Execute(source)) Compare2D(cpu, gpu, expected, disabled);
            Require(gpu.LastPhaseElapsedMs.Count == 0, "Default execution must not record optional phase timings.");
            cpu.CollectPhaseTimings = gpu.CollectPhaseTimings = true;
            string[] keys = { "SearchCudaLockWait", "SearchCudaPreparation", "SearchCudaAllocation", "SearchCudaPacking",
                "SearchCudaNativeCall", "SearchCudaValidation", "SearchCudaCandidateConsumption" };
            for (int run = 0; run < 2; run++)
            {
                using VisionToolResult a = cpu.Execute(source); using VisionToolResult b = gpu.Execute(source);
                Compare2D(cpu, gpu, a, b);
                Require(b.ComputeDiagnostics.AccelerationApplied, "Timing validation must execute actual CUDA tiles.");
                Require(cpu.LastPhaseElapsedMs.ContainsKey("SearchCpuParallelPositions"), "CPU parallel search must have a wall-time scope.");
                foreach (string key in keys)
                    Require(gpu.LastPhaseElapsedMs.TryGetValue(key, out double value) && double.IsFinite(value) && value >= 0,
                        "Each optional CUDA scope must report a finite nonnegative duration: " + key);
                using CudaNativeExecution capability = CudaNativeExecution.Create(ModulePath, 0, CancellationToken.None, out _, out _);
                Require(capability.SupportsStridedEdgeTiles ? gpu.LastPhaseElapsedMs["SearchCudaPacking"] == 0 : gpu.LastPhaseElapsedMs["SearchCudaPacking"] > 0,
                    "Packing is absent only when both direct-transfer and release extensions are available.");
                Require(gpu.LastPhaseElapsedMs["SearchCudaNativeCall"] > 0
                    && gpu.LastPhaseElapsedMs["SearchCudaCandidateConsumption"] > 0, "Copy, actual native work and selection must be measured.");
                Require(keys.Sum(key => gpu.LastPhaseElapsedMs[key]) <= gpu.LastPhaseElapsedMs["SearchEdgeCandidate"] + 1,
                    "Nonoverlapping sequential CUDA durations must fit the enclosing search and reset on repeated Execute.");
            }
            double[] sx = Enumerable.Repeat(0.5, 129 * 7).ToArray(), sy = new double[sx.Length];
            VisionComputeSession.EdgeScoreTile tile = new VisionComputeSession.EdgeScoreTile();
            foreach (bool collect in new[] { true, false, true })
            {
                tile.CollectTimings = collect;
                Require(session.TryScoreEdgeTiles(sx, sy, ZeroOffset, ZeroOffset, One, Zero, MinusTwo, 129,
                    0, 0, 129, 7, 1, false, tile, () => Same(0.5, tile.Scores[0], "Timed tile score"), CancellationToken.None, out _),
                    "The same tile must recover across diagnostic mode changes.");
                long ticks = tile.LockWaitTicks + tile.PreparationTicks + tile.AllocationTicks + tile.PackingTicks
                    + tile.NativeCallTicks + tile.ValidationTicks + tile.ConsumptionTicks;
                Require(collect ? ticks > 0 : ticks == 0, "Internal timings must reset for each call and remain disabled when unrequested.");
            }
            InvalidOperationException callbackFailure = new InvalidOperationException("owned timing callback failure");
            bool propagated = false;
            try
            {
                session.TryScoreEdgeTiles(sx, sy, ZeroOffset, ZeroOffset, One, Zero, MinusTwo, 129,
                    0, 0, 129, 7, 1, false, tile, () => throw callbackFailure, CancellationToken.None, out _);
            }
            catch (InvalidOperationException error) { propagated = ReferenceEquals(error, callbackFailure); }
            Require(propagated, "Timing must preserve callback exception identity and release the session lock.");
            Require(session.TryScoreEdgeTiles(sx, sy, ZeroOffset, ZeroOffset, One, Zero, MinusTwo, 129,
                0, 0, 129, 7, 1, false, tile, () => Same(0.5, tile.Scores[0], "Callback recovery score"), CancellationToken.None, out _),
                "A valid call after callback failure must recover without stale tile state.");
        }

        private static void TestStridedInputs()
        {
            using VisionComputeSession session = Session(tileBytes: 8192);
            double[] sx = new double[73 * 19], sy = new double[sx.Length];
            int[] dx = { -2, 3 }, dy = { -1, 2 };
            double[] mx = { 1, 0 }, my = { 0, 1 }, limits = { -10000, -10000 };
            VisionComputeSession.EdgeScoreTile tile = new VisionComputeSession.EdgeScoreTile { CollectTimings = true };
            for (int run = 0; run < 4; run++)
            {
                for (int y = 0; y < 19; y++) for (int x = 0; x < 73; x++)
                { sx[y * 73 + x] = y * 8 + x * 0.25 + run; sy[y * 73 + x] = -y * 2 - x * 0.5 - run; }
                mx[0] = run % 2 == 0 ? 1 : -1;
                limits[0] = run == 2 ? 10000 : -10000;
                int step = run % 2 + 1, columns = run == 1 ? 7 : 31, rows = run == 1 ? 2 : 7;
                bool polarity = run == 3;
                double[] beforeX = (double[])sx.Clone(), beforeY = (double[])sy.Clone();
                int visited = 0;
                Require(session.TryScoreEdgeTiles(sx, sy, dx, dy, mx, my, limits, 73, 3, 2, columns, rows, step,
                    polarity, tile, () =>
                    {
                        for (int y = 0; y < tile.Rows; y++) for (int x = 0; x < tile.Columns; x++)
                        {
                            int gx = tile.MinX + x * step, gy = tile.MinY + y * step;
                            double first = ((gy - 1) * 8 + (gx - 2) * 0.25 + run) * mx[0];
                            double expected = !polarity && first < limits[0]
                                ? first : (first - (gy + 2) * 2 - (gx + 3) * 0.5 - run) / 2;
                            if (polarity && expected < 0) expected = -expected;
                            Same(expected, tile.Scores[y * tile.Columns + x], "Independent changed-input strided score");
                            visited++;
                        }
                    }, CancellationToken.None, out ComputeExecutionEvidence evidence), "Changed inputs must execute on CUDA: " + evidence?.FallbackReason);
                Require(visited == columns * rows && evidence.PeakScratchBytes <= 8192 && evidence.AccelerationApplied, "All bounded positions must be consumed once.");
                Require(sx.SequenceEqual(beforeX) && sy.SequenceEqual(beforeY), "Borrowed source planes must remain unchanged.");
            }
        }

        private static void TestWorkspaceLifetime()
        {
            string directory = Environment.GetEnvironmentVariable("OVL_CUDA_EXECUTION_FIXTURES");
            foreach (int kind in new[] { 16, 17, 18, 19, 20, 21, 22, 23 })
            {
                string path = Path.Combine(directory, "execution-" + kind + ".dll");
                IntPtr module = CudaNativeProbe.LoadLibraryExW(path, IntPtr.Zero, CudaNativeProbe.SearchDllDirectoryAndSystem32);
                Require(module != IntPtr.Zero, "Workspace fixture must load.");
                try
                {
                    Counter live = CudaNativeProbe.GetExport<Counter>(module, "ovl_test_live_workspaces");
                    Counter handles = CudaNativeProbe.GetExport<Counter>(module, "ovl_test_live_executions");
                    Counter releases = CudaNativeProbe.GetExport<Counter>(module, "ovl_test_workspace_releases");
                    using VisionComputeSession session = Session(path, tileBytes: 4096);
                    SetDelay delay = CudaNativeProbe.GetExport<SetDelay>(module, "ovl_test_set_tile_delay");
                    delay(0);
                    double[] source = Enumerable.Repeat(0.5, 257 * 5).ToArray();
                    VisionComputeSession.EdgeScoreTile tile = new VisionComputeSession.EdgeScoreTile { CollectTimings = true };
                    bool Run(Action consume, CancellationToken token) => session.TryScoreEdgeTiles(source, new double[source.Length], ZeroOffset, ZeroOffset,
                        One, Zero, MinusTwo, 257, 0, 0, 257, 5, 1, false, tile, consume, token, out _);
                    int consumed = 0;
                    bool applied = Run(() => { Same(kind == 18 || kind == 19 ? 2.0 : 0.5, tile.Scores[0], "Fixture score"); consumed += tile.Columns * tile.Rows; }, CancellationToken.None);
                    Require(applied == (kind == 16 || kind == 17 || kind == 20 || kind == 22 || kind == 23), "Only recoverable or complete extensions may finish.");
                    Require(live() == 0, "Workspace must be released before returning from every outcome.");
                    if (applied)
                    {
                        Require(consumed == source.Length, "Allocation retry must not omit or duplicate scores.");
                        Require(kind >= 22 ? tile.PackingTicks > 0 : tile.PackingTicks == 0, "Incomplete extension pairs must select the packed path.");
                        InvalidOperationException expected = new InvalidOperationException("owned workspace callback");
                        try { Run(() => throw expected, CancellationToken.None); throw new InvalidOperationException("Callback exception missing"); }
                        catch (InvalidOperationException actual) { Require(ReferenceEquals(actual, expected), "Callback exception identity must survive release."); }
                        Require(live() == 0, "Callback failure must release before propagation.");
                        using CancellationTokenSource cancel = new CancellationTokenSource();
                        RequireThrows<OperationCanceledException>(() => Run(() => cancel.Cancel(), cancel.Token));
                        Require(live() == 0 && Run(() => Same(0.5, tile.Scores[0], "Workspace recovery"), CancellationToken.None), "Cancellation must release and permit valid recovery.");
                    }
                    else Require(!Run(() => { }, CancellationToken.None) && handles() == 0, "Fatal native or release failure must poison and destroy the handle.");
                    Require(kind >= 22 ? releases() == 0 : releases() >= (kind == 18 || kind == 19 ? 1 : 2),
                        "Complete extension pairs release capability/grid workspaces; poisoned handles release through destruction.");
                    session.Dispose(); Require(live() == 0 && handles() == 0, "Dispose must leave no native workspace or handle.");
                    if (kind == 18 || kind == 19 || kind == 21)
                    {
                        using VisionComputeSession failed = Session(path, tileBytes: 64 * 1024);
                        using Mat template = Template(48); using Mat image = Source(template, 224);
                        using EdgeBasedTemplateMatchingTool cpu = Tool(template, null); using EdgeBasedTemplateMatchingTool gpu = Tool(template, failed);
                        cpu.property.USE_PYRAMID_POSITION_PROPOSAL = gpu.property.USE_PYRAMID_POSITION_PROPOSAL = false;
                        using VisionToolResult expected = cpu.Execute(image); using VisionToolResult actual = gpu.Execute(image);
                        Compare2D(cpu, gpu, expected, actual);
                        Require(!actual.ComputeDiagnostics.AccelerationApplied && live() == 0 && handles() == 0,
                            "Late invalid/kernel/release failure must discard staged matches and finish with the complete CPU result.");
                    }
                }
                finally { CudaNativeProbe.FreeLibrary(module); }
            }
        }

        private static void TestTileLayout()
        {
            foreach (int side in new[] { 5000, 10000, 15000 })
            foreach (ulong budget in new[] { 64UL * 1024 * 1024, 256UL * 1024 * 1024 })
            {
                int columns = (side - 192) / 2 + 1;
                Require(VisionComputeSession.TryPlanEdgeTile(columns, columns, 2, 191, 191, 500, budget, out int c, out int r), "Large full-image stencil must fit a bounded tile.");
                long w = (c - 1L) * 2 + 192, h = (r - 1L) * 2 + 192;
                Require(c == columns && r > 0 && 16 * w * h + 16000 + 8L * c * r <= (long)budget
                    && 8L * c * r <= 64L * 1024 * 1024, "Input/model/output bytes and full-width order must remain bounded.");
            }
            Require(!VisionComputeSession.TryPlanEdgeTile(0, 1, 1, 9, 9, 7, 4096, out _, out _), "Empty grid must terminate.");
            Require(!VisionComputeSession.TryPlanEdgeTile(10, 10, 1, 9, 9, 7, 100, out _, out _), "Minimum stencil cannot be omitted to fit memory.");
            Require(VisionComputeSession.TryPlanEdgeTile(int.MaxValue, int.MaxValue, int.MaxValue, 0, 0, 1, 4096, out int one, out int row)
                && one == 1 && row == 1, "Extreme arithmetic must select a representable bounded candidate.");
        }

        private static void TestTiles()
        {
            const int width = 129, height = 131;
            Random random = new Random(42001);
            double[] sx = new double[width * height], sy = new double[sx.Length];
            for (int i = 0; i < sx.Length; i++) { double a = random.NextDouble() * 6; sx[i] = Math.Cos(a); sy[i] = Math.Sin(a); }
            double[] originalX = (double[])sx.Clone(), originalY = (double[])sy.Clone();
            int[] baseX = { -4, 5, -4, 5, 0, 1, -2 }, baseY = { -3, -3, 6, 6, 0, -2, 1 };
            long compared = 0;
            foreach (int points in new[] { 1, 7, 17, 257 })
            foreach (int shift in new[] { 0, 8 })
            foreach (int step in new[] { 1, 3 })
            foreach (bool polarity in new[] { false, true })
            foreach (int budget in new[] { 64 * 1024, 4 * 1024 })
            {
                if (points == 257 && budget == 4 * 1024) continue;
                using VisionComputeSession session = Session(tileBytes: budget);
                int[] dx = new int[points], dy = new int[points];
                double[] mx = new double[points], my = new double[points], limits = new double[points];
                for (int i = 0; i < points; i++)
                {
                    dx[i] = baseX[i % 7] + shift; dy[i] = baseY[i % 7] + shift;
                    double a = random.NextDouble() * 6; mx[i] = Math.Cos(a); my[i] = Math.Sin(a);
                    double processed = i + 1, min = 0.5, greed = 0.8;
                    limits[i] = Math.Min(min - 1 + (1 - greed * min) / (1 - greed) / points * processed, min / points * processed) * processed;
                }
                int minX = -dx.Min(), minY = -dy.Min();
                int columns = (width - 1 - dx.Max() - minX) / step + 1, rows = (height - 1 - dy.Max() - minY) / step + 1;
                VisionComputeSession.EdgeScoreTile tile = new VisionComputeSession.EdgeScoreTile { Scores = Enumerable.Repeat(double.NaN, 10000).ToArray() };
                int visited = 0, tiles = 0;
                Require(session.TryScoreEdgeTiles(sx, sy, dx, dy, mx, my, limits, width, minX, minY, columns, rows, step, polarity, tile, () =>
                {
                    tiles++;
                    for (int y = 0; y < tile.Rows; y++)
                    for (int x = 0; x < tile.Columns; x++)
                    {
                        int gx = tile.MinX + x * step, gy = tile.MinY + y * step;
                        int index = ((gy - minY) / step) * columns + (gx - minX) / step;
                        Require(index == visited++, "Every candidate must be visited once in original row-major order.");
                        double sum = 0, expected = 0;
                        for (int i = 0; i < points; i++)
                        {
                            int at = (gy + dy[i]) * width + gx + dx[i];
                            sum += originalX[at] * mx[i] + originalY[at] * my[i]; expected = sum / (i + 1);
                            if (!polarity && sum < limits[i]) break;
                            if (i == points - 1 && polarity) expected = Math.Abs(sum) / points;
                        }
                        Same(expected, tile.Scores[y * tile.Columns + x], "Original samples versus actual GPU packed tile");
                        compared++;
                    }
                    Array.Fill(tile.Scores, double.NaN); // The next call must validate only its freshly written prefix.
                }, CancellationToken.None, out ComputeExecutionEvidence evidence), "Tiled actual GPU must execute: " + evidence?.FallbackReason);
                Require(visited == columns * rows && tiles > 1 && evidence.AccelerationApplied && evidence.PeakScratchBytes <= (ulong)budget,
                    "Tail/halo/native buffers must remain bounded, complete and actually accelerated.");
            }
            foreach (double limit in new[] { Math.BitDecrement(0.5), 0.5, Math.BitIncrement(0.5) })
            {
                using VisionComputeSession session = Session(tileBytes: 1024);
                VisionComputeSession.EdgeScoreTile tile = new VisionComputeSession.EdgeScoreTile();
                Require(session.TryScoreEdgeTiles(TwoHalves, new double[2], TwoOffsets, new int[2], TwoOnes, new double[2], new[] { limit, -2.0 },
                    2, 0, 0, 1, 1, 1, false, tile, () => Same(limit > 0.5 ? 0.5 : 0.375, tile.Scores[0], "Tiled one-ULP early exit"),
                    CancellationToken.None, out _), "One-ULP tile must execute.");
            }
            using (CudaNativeExecution native = CudaNativeExecution.Create(ModulePath, 0, CancellationToken.None, out _, out _))
            {
                double[] source = { 1, -1, 0, 0.5 }, output = new double[1];
                int[] invalidX = { 4 }, invalidY = { -1 };
                int status = native.ScoreTile(source, new double[4], 4, invalidX, invalidY, One, Zero, MinusTwo,
                    4, 1, 0, 0, 1, 1, 1, false, output, CancellationToken.None, out _);
                Require(status == 0 && double.IsNaN(output[0]), "A linearly valid index must not hide out-of-range signed XY.");
                status = native.ScoreTile(source, new double[4], 4, ZeroOffset, ZeroOffset, One, Zero, MinusTwo,
                    4, 1, 0, 0, 2, 1, 1, false, output, CancellationToken.None, out _);
                Require(status == 12, "Native output capacity must be checked before any write.");
            }
            for (int i = 0; i < sx.Length; i++) { Same(originalX[i], sx[i], "Source X must stay unchanged"); Same(originalY[i], sy[i], "Source Y must stay unchanged"); }
            Console.WriteLine("CUDA tile independently compared positions=" + compared + "; signed XY/negative anchors/order/tails all bitwise equal");
        }

        private static void TestTileWorkflow()
        {
            using Mat template = Template(48);
            using Mat source = Source(template, 224, true);
            using VisionComputeSession session = Session(tileBytes: 256 * 1024);
            using EdgeBasedTemplateMatchingTool cpu = Tool(template, null, 2);
            using EdgeBasedTemplateMatchingTool gpu = Tool(template, session, 2);
            foreach (bool pyramid in new[] { false, true })
            foreach (bool hybrid in new[] { false, true })
            foreach (bool unique in new[] { false, true })
            {
                cpu.property.USE_PYRAMID_POSITION_PROPOSAL = gpu.property.USE_PYRAMID_POSITION_PROPOSAL = pyramid;
                cpu.property.USE_HYBRID_VERIFY = gpu.property.USE_HYBRID_VERIFY = hybrid;
                cpu.property.USE_UNIQUE_MATCH_VALIDATION = gpu.property.USE_UNIQUE_MATCH_VALIDATION = unique;
                cpu.property.NUM_MATCH = gpu.property.NUM_MATCH = unique ? 1 : 2;
                Console.WriteLine($"Tile workflow: pyramid={pyramid}, hybrid={hybrid}, unique={unique}, matches={cpu.property.NUM_MATCH}");
                using VisionToolResult a = cpu.Execute(source); using VisionToolResult b = gpu.Execute(source);
                Compare2D(cpu, gpu, a, b);
                if (!unique)
                    Require(a.Success && cpu.results.Count == 2 && cpu.results[0].Bounding.X > 16 && cpu.results[0].Bounding.X < 19
                        && cpu.results[0].Bounding.Y > 22 && cpu.results[0].Bounding.Y < 25
                        && cpu.results[1].Bounding.X > 162 && cpu.results[1].Bounding.X < 165
                        && cpu.results[1].Bounding.Y > 160 && cpu.results[1].Bounding.Y < 163,
                        "Independent equal-copy coordinates must retain upper/left result before lower/right.");
                Require(b.ComputeDiagnostics.AccelerationApplied && b.ComputeDiagnostics.GpuBatchCount > 1
                    && b.ComputeDiagnostics.PeakScratchBytes <= 256UL * 1024, "Workflow must cross multiple real GPU tiles.");
                if (pyramid) Require(b.Metrics["Candidate.PyramidProposalAttemptCount"] > 0, "Top-candidate production caller must actually execute.");
            }
            cpu.property.USE_PYRAMID_POSITION_PROPOSAL = gpu.property.USE_PYRAMID_POSITION_PROPOSAL = false;
            cpu.property.USE_HYBRID_VERIFY = gpu.property.USE_HYBRID_VERIFY = false;
            cpu.property.USE_UNIQUE_MATCH_VALIDATION = gpu.property.USE_UNIQUE_MATCH_VALIDATION = false;
            cpu.property.NUM_MATCH = gpu.property.NUM_MATCH = 1;
            cpu.property.USE_ROI = gpu.property.USE_ROI = true;
            cpu.property.CvROI = gpu.property.CvROI = new Rect(5, 7, 201, 203);
            using (Mat padded = new Mat(240, 247, MatType.CV_8UC1, Scalar.All(0)))
            using (Mat view = new Mat(padded, new Rect(3, 4, 224, 224)))
            {
                source.CopyTo(view);
                using VisionToolResult a = cpu.Execute(view); using VisionToolResult b = gpu.Execute(view);
                Compare2D(cpu, gpu, a, b);
                Require(b.ComputeDiagnostics.AccelerationApplied, "Padded/odd ROI source must use the tile path.");
            }
            using Mat baseTemplate = EdgePoseRefinementSmokeSuite.CreateTemplate();
            using Mat rotated = EdgePoseRefinementSmokeSuite.CreateSource(baseTemplate, 2.5, 1.025);
            using EdgeBasedTemplateMatchingTool poseCpu = Tool(baseTemplate, null);
            using EdgeBasedTemplateMatchingTool poseGpu = Tool(baseTemplate, session);
            poseCpu.property.USE_FIND_ANGLE = poseGpu.property.USE_FIND_ANGLE = true;
            poseCpu.property.FIND_ANGLE_MIN = poseGpu.property.FIND_ANGLE_MIN = -5;
            poseCpu.property.FIND_ANGLE_MAX = poseGpu.property.FIND_ANGLE_MAX = 5;
            poseCpu.property.FIND_ANGLE = poseGpu.property.FIND_ANGLE = 2.5;
            poseCpu.property.USE_FIND_SCALE = poseGpu.property.USE_FIND_SCALE = true;
            poseCpu.property.FIND_SCALE_MIN = poseGpu.property.FIND_SCALE_MIN = 1;
            poseCpu.property.FIND_SCALE_MAX = poseGpu.property.FIND_SCALE_MAX = 1.05;
            poseCpu.property.FIND_SCALE_STEP = poseGpu.property.FIND_SCALE_STEP = 0.025;
            poseCpu.UsePoseRefinement = poseGpu.UsePoseRefinement = true;
            using VisionToolResult pa = poseCpu.Execute(rotated); using VisionToolResult pb = poseGpu.Execute(rotated);
            Compare2D(poseCpu, poseGpu, pa, pb);
            Require(pb.ComputeDiagnostics.AccelerationApplied, "Rotated/scaled model offsets must use real tiles.");
        }

        private static void TestTileFaults()
        {
            string directory = Environment.GetEnvironmentVariable("OVL_CUDA_EXECUTION_FIXTURES");
            using Mat template = Template(48); using Mat source = Source(template, 224);
            foreach (int kind in new[] { 9, 10, 11, 15 })
            foreach (bool pyramid in new[] { false, true })
            {
                string path = Path.Combine(directory, "execution-" + kind + ".dll");
                IntPtr module = CudaNativeProbe.LoadLibraryExW(path, IntPtr.Zero, CudaNativeProbe.SearchDllDirectoryAndSystem32);
                Require(module != IntPtr.Zero, "Tile fault fixture must load.");
                try
                {
                    Counter live = CudaNativeProbe.GetExport<Counter>(module, "ovl_test_live_executions");
                    using VisionComputeSession session = Session(path, tileBytes: 64 * 1024);
                    using EdgeBasedTemplateMatchingTool cpu = Tool(template, null); using EdgeBasedTemplateMatchingTool gpu = Tool(template, session);
                    gpu.CollectPhaseTimings = true;
                    cpu.property.USE_PYRAMID_POSITION_PROPOSAL = gpu.property.USE_PYRAMID_POSITION_PROPOSAL = pyramid;
                    using VisionToolResult a = cpu.Execute(source); using VisionToolResult b = gpu.Execute(source);
                    Compare2D(cpu, gpu, a, b);
                    Require(b.ComputeDiagnostics.GpuBatchCount >= 2, "Fault must occur after an accepted local tile, not during discovery.");
                    Require(b.ComputeDiagnostics.UploadMilliseconds >= 2 && b.ComputeDiagnostics.KernelWaitMilliseconds >= 1
                        && b.ComputeDiagnostics.DownloadMilliseconds >= 0.5, "Discarded work must retain injected phase costs, separately from performance evidence.");
                    if (!pyramid || kind != 9)
                    {
                        CudaFallbackReason expected = kind == 9 ? CudaFallbackReason.GpuMemoryUnavailable : kind == 10 ? CudaFallbackReason.GpuExecutionFailed : CudaFallbackReason.InvalidKernelResult;
                        Require(!b.ComputeDiagnostics.AccelerationApplied && b.ComputeDiagnostics.FallbackReason == expected,
                            "Discarded partial GPU work must not be reported as applied.");
                    }
                    using (VisionToolResult recovered = gpu.Execute(source)) Compare2D(cpu, gpu, a, recovered);
                    session.Dispose(); Require(live() == 0, "Late faults and recovery must release all owned handles.");
                }
                finally { _ = CudaNativeProbe.FreeLibrary(module); }
            }
            using (VisionComputeSession session = Session(Path.Combine(directory, "execution-10.dll"), study: false, tileBytes: 64 * 1024))
            using (Mat large = Source(template, 512))
            using (EdgeBasedTemplateMatchingTool cpu = Tool(template, null))
            using (EdgeBasedTemplateMatchingTool gpu = Tool(template, session))
            {
                foreach (EdgeBasedTemplateMatchingTool tool in new[] { cpu, gpu })
                {
                    tool.property.USE_MULTI_ROI = true;
                    tool.property.CvROIS.Add(new Rect(0, 0, 384, 384));
                    tool.property.CvROIS.Add(new Rect(0, 0, 200, 200));
                }
                using VisionToolResult a = cpu.Execute(large); using VisionToolResult b = gpu.Execute(large);
                Compare2D(cpu, gpu, a, b);
                Console.WriteLine("Large-fault/small-ROI diagnostics=" + JsonSerializer.Serialize(b.ComputeDiagnostics));
                Require(!b.ComputeDiagnostics.AccelerationApplied && b.ComputeDiagnostics.FallbackReason == CudaFallbackReason.GpuExecutionFailed
                    && b.ComputeDiagnostics.NativeExecutionErrorCode == 719
                    && b.ComputeDiagnostics.GpuBatchCount == 2 && b.ComputeDiagnostics.UploadMilliseconds == 2
                    && b.ComputeDiagnostics.KernelWaitMilliseconds == 1 && b.ComputeDiagnostics.DownloadMilliseconds == 0.5,
                    "A later small ROI must retain all discarded GPU-grid costs without marking that grid applied.");
            }
            foreach (int kind in new[] { 8, 13, 14 })
            {
                using VisionComputeSession session = Session(Path.Combine(directory, "execution-" + kind + ".dll"), tileBytes: 4096);
                double[] values = Enumerable.Repeat(0.5, 4097).ToArray();
                VisionComputeSession.EdgeScoreTile tile = new VisionComputeSession.EdgeScoreTile { CollectTimings = true };
                int consumed = 0;
                bool success = session.TryScoreEdgeTiles(values, new double[values.Length], ZeroOffset, ZeroOffset, One, Zero, MinusTwo,
                    values.Length, 0, 0, values.Length, 1, 1, false, tile, () => consumed += tile.Columns * tile.Rows,
                    CancellationToken.None, out ComputeExecutionEvidence evidence);
                Require(success == (kind == 13), "Only one-time allocation rejection should recover through smaller tiles.");
                Require(kind == 13 ? consumed == values.Length && evidence.AccelerationApplied : consumed == 0 && !evidence.AccelerationApplied,
                    "Initial budget failure must not consume output or loop indefinitely.");
                Require(evidence.GpuBatchCount <= (kind == 13 ? 100 : 3), "Allocation retries must be finite.");
            }
            using (VisionComputeSession legacy = Session(Path.Combine(directory, "execution-0.dll")))
            {
                using EdgeBasedTemplateMatchingTool cpu = Tool(template, null); using EdgeBasedTemplateMatchingTool gpu = Tool(template, legacy);
                using VisionToolResult a = cpu.Execute(source); using VisionToolResult b = gpu.Execute(source);
                Compare2D(cpu, gpu, a, b); Require(b.ComputeDiagnostics.AccelerationApplied, "Previous ABI1 module must retain small GPU grids.");
                double[] values = new double[4097 * 1024];
                VisionComputeSession.EdgeScoreTile tile = new VisionComputeSession.EdgeScoreTile { CollectTimings = true };
                Require(!legacy.TryScoreEdgeTiles(values, values, ZeroOffset, ZeroOffset, One, Zero, MinusTwo, 4097,
                    0, 0, 4097, 1024, 1, false, tile, () => throw new InvalidOperationException("No legacy large result may be consumed"), CancellationToken.None, out ComputeExecutionEvidence reason)
                    && reason.FallbackReason == CudaFallbackReason.KernelNotImplemented, "Old native without tile extension must safely select CPU for large source.");
            }
        }

        private static void TestTileCancellation()
        {
            string path = Path.Combine(Environment.GetEnvironmentVariable("OVL_CUDA_EXECUTION_FIXTURES"), "execution-12.dll");
            TestTileCancellation(path);
        }

        private static void TestTileCancellation(string path)
        {
            IntPtr module = CudaNativeProbe.LoadLibraryExW(path, IntPtr.Zero, CudaNativeProbe.SearchDllDirectoryAndSystem32);
            try
            {
                Counter active = CudaNativeProbe.GetExport<Counter>(module, "ovl_test_active_calls");
                Counter live = CudaNativeProbe.GetExport<Counter>(module, "ovl_test_live_executions");
                Counter workspaces = CudaNativeProbe.GetExport<Counter>(module, "ovl_test_live_workspaces");
                using VisionComputeSession session = Session(path, tileBytes: 4096);
                double[] source = Enumerable.Repeat(0.5, 4097).ToArray();
                void Run(CancellationToken token) => session.TryScoreEdgeTiles(source, new double[source.Length], ZeroOffset, ZeroOffset, One, Zero, MinusTwo,
                    source.Length, 0, 0, source.Length, 1, 1, false, new VisionComputeSession.EdgeScoreTile { CollectTimings = true }, () => { }, token, out _);
                using CancellationTokenSource running = new CancellationTokenSource();
                using CancellationTokenSource waiting = new CancellationTokenSource();
                Task work = Task.Run(() => RequireThrows<OperationCanceledException>(() => Run(running.Token)));
                Require(SpinWait.SpinUntil(() => active() > 0, 5000), "Tile operation must reach native execution.");
                Task queued = Task.Run(() => RequireThrows<OperationCanceledException>(() => Run(waiting.Token)));
                waiting.Cancel(); Require(queued.Wait(2000), "Waiting tile cancellation must remain responsive.");
                using ManualResetEventSlim disposingEntered = new ManualResetEventSlim();
                Task disposing = Task.Run(() => { disposingEntered.Set(); session.Dispose(); });
                Require(disposingEntered.Wait(2000) && !disposing.Wait(50) && active() > 0, "Dispose must wait while the native tile is active.");
                running.Cancel(); Require(work.Wait(2000) && disposing.Wait(2000) && active() == 0 && live() == 0,
                    "Cooperative cancellation must release calls and execution before disposal returns.");
                using VisionComputeSession recovered = Session(tileBytes: 4096);
                using CancellationTokenSource mid = new CancellationTokenSource();
                VisionComputeSession.EdgeScoreTile tile = new VisionComputeSession.EdgeScoreTile { CollectTimings = true };
                int consumed = 0;
                RequireThrows<OperationCanceledException>(() => recovered.TryScoreEdgeTiles(source, new double[source.Length], ZeroOffset, ZeroOffset, One, Zero, MinusTwo,
                    source.Length, 0, 0, source.Length, 1, 1, false, tile, () => { consumed++; mid.Cancel(); }, mid.Token, out _));
                Require(consumed == 1, "Cancellation after a consumed local tile must not run another tile or CPU grid.");
                Score(recovered, new[] { 1.0, -1.0, 0.0, 0.5 }, CancellationToken.None);
                SetDelay delay = CudaNativeProbe.GetExport<SetDelay>(module, "ovl_test_set_tile_delay");
                using Mat template = Template(48); using Mat image = Source(template, 224);
                foreach (bool pyramid in new[] { false, true })
                {
                    delay(0);
                    using VisionComputeSession toolSession = Session(path, tileBytes: 64 * 1024);
                    using EdgeBasedTemplateMatchingTool cpu = Tool(template, null); using EdgeBasedTemplateMatchingTool gpu = Tool(template, toolSession);
                    cpu.property.USE_PYRAMID_POSITION_PROPOSAL = gpu.property.USE_PYRAMID_POSITION_PROPOSAL = pyramid;
                    using VisionToolResult expected = cpu.Execute(image);
                    using (VisionToolResult seeded = gpu.Execute(image)) Compare2D(cpu, gpu, expected, seeded);
                    Require(gpu.results.Count > 0, "Cancellation must begin with preceding public match rows.");
                    delay(1);
                    using CancellationTokenSource cancelTool = new CancellationTokenSource();
                    Task execution = Task.Run(() => RequireThrows<OperationCanceledException>(() => gpu.Execute(image, cancelTool.Token)));
                    Require(SpinWait.SpinUntil(() => active() > 0, 5000), "Production search must reach the delayed native tile.");
                    cancelTool.Cancel(); Require(execution.Wait(2000) && gpu.results.Count == 0, "Active tile cancellation must clear public matches without CPU restart.");
                    delay(0);
                    using (VisionToolResult retried = gpu.Execute(image)) Compare2D(cpu, gpu, expected, retried);
                }
                Require(active() == 0 && live() == 0, "Cancelled production search must recover and release its execution handles.");
                Require(workspaces == null || workspaces() == 0, "Active cancellation and public recovery must leave no retained workspace.");
            }
            finally { _ = CudaNativeProbe.FreeLibrary(module); }
        }

        private static void Same(double expected, double actual, string description)
        {
            Require(BitConverter.DoubleToInt64Bits(expected) == BitConverter.DoubleToInt64Bits(actual), description + ": " + expected.ToString("R", CultureInfo.InvariantCulture) + " != " + actual.ToString("R", CultureInfo.InvariantCulture));
        }

        private static void TestGrids()
        {
            using VisionComputeSession session = Session();
            Random random = new Random(39001);
            const int width = 129, height = 131;
            double[] sx = new double[width * height], sy = new double[sx.Length];
            for (int i = 0; i < sx.Length; i++) { double a = random.NextDouble() * 6.0; sx[i] = Math.Cos(a); sy[i] = Math.Sin(a); }
            long compared = 0;
            foreach (int points in new[] { 1, 3, 17, 255, 256, 257 })
            foreach (bool polarity in new[] { false, true })
            foreach (int step in new[] { 1, 3 })
            {
                int[] offsets = new int[points];
                double[] mx = new double[points], my = new double[points], thresholds = new double[points];
                for (int i = 0; i < points; i++)
                {
                    offsets[i] = random.Next(-1, 2) * width + random.Next(-1, 2);
                    double a = random.NextDouble() * 6.0; mx[i] = Math.Cos(a); my[i] = Math.Sin(a);
                    double min = points % 2 == 0 ? 0.85 : 0.25, greed = points < 17 ? 0 : 0.8;
                    double processed = i + 1;
                    thresholds[i] = Math.Min(min - 1 + (1 - greed * min) / (1 - greed) / points * processed, min / points * processed) * processed;
                }
                int columns = (width - 5) / step + 1, rows = (height - 5) / step + 1;
                Require(session.TryScoreEdgeGrid(sx, sy, offsets, mx, my, thresholds, width, 2, 2, columns, rows, step, polarity,
                    CancellationToken.None, out double[] actual, out ComputeExecutionEvidence evidence), "Actual edge kernel must execute: " + evidence?.FallbackReason);
                Require(evidence.AccelerationApplied && session.Support.IsAccelerationAvailable, "Discovery alone must not satisfy execution capability.");
                for (int row = 0; row < rows; row++)
                for (int column = 0; column < columns; column++)
                {
                    int center = (2 + row * step) * width + 2 + column * step;
                    double sum = 0, expected = 0;
                    for (int i = 0; i < points; i++)
                    {
                        sum += sx[center + offsets[i]] * mx[i] + sy[center + offsets[i]] * my[i];
                        expected = sum / (i + 1);
                        if (!polarity && sum < thresholds[i]) break;
                        if (i == points - 1 && polarity) expected = Math.Abs(sum) / points;
                    }
                    Same(expected, actual[row * columns + column], "Full authored candidate grid");
                    compared++;
                }
            }
            foreach (double threshold in new[] { Math.BitDecrement(0.5), 0.5, Math.BitIncrement(0.5) })
            {
                Require(session.TryScoreEdgeGrid(AlternatingHalves, new double[4], ZeroOffset, One, Zero, new[] { threshold },
                    4, 0, 0, 4, 1, 1, false, CancellationToken.None, out double[] scores, out ComputeExecutionEvidence _), "Boundary kernel must execute.");
                Same(0.5, scores[0], "Inclusive score boundary"); Same(scores[0], scores[2], "Stable equal scores");
                Require(session.TryScoreEdgeGrid(TwoHalves, new double[2], TwoOffsets, TwoOnes, new double[2], new[] { threshold, -2.0 },
                    2, 0, 0, 1, 1, 1, false, CancellationToken.None, out double[] boundary, out ComputeExecutionEvidence _), "Early-exit threshold must execute.");
                Same(threshold > 0.5 ? 0.5 : 0.375, boundary[0], "One-ULP early-exit boundary");
            }
            Console.WriteLine("CUDA grid compared positions=" + compared + "; all bitwise equal");
        }

        private static void TestTransforms()
        {
            using VisionComputeSession session = Session();
            Random random = new Random(39002);
            foreach (int count in new[] { 1, 17, 257 })
            foreach (double scale in new[] { 1.0, 1e150 })
            {
                double[] points = Enumerable.Range(0, count * 3).Select(_ => (random.NextDouble() - 0.5) * scale).ToArray();
                double[] poses = { 0, -1, 0, 1, 0, 0, 0, 0, 1, 4, 5, 6,
                    Math.Cos(0.37), -Math.Sin(0.37), 0, Math.Sin(0.37), Math.Cos(0.37), 0, 0, 0, 1, -2, 3, -4 };
                Require(session.TryTransformPoints(points, poses, CancellationToken.None, out double[] actual, out ComputeExecutionEvidence evidence), "Actual transform kernel must execute: " + evidence?.FallbackReason);
                for (int pose = 0; pose < 2; pose++)
                for (int i = 0; i < count; i++)
                for (int axis = 0; axis < 3; axis++)
                {
                    int m = pose * 12 + axis * 3, p = i * 3;
                    double expected = poses[m] * points[p] + poses[m + 1] * points[p + 1] + poses[m + 2] * points[p + 2] + poses[pose * 12 + 9 + axis];
                    Same(expected, actual[(pose * count + i) * 3 + axis], "Transform coordinate");
                }
            }
            double[] extreme = { double.MaxValue, double.MaxValue, double.MaxValue };
            double[] overflow = { 1, 1, 1, 0, 1, 0, 0, 0, 1, 0, 0, 0 };
            Require(!session.TryTransformPoints(extreme, overflow, CancellationToken.None, out double[] _, out ComputeExecutionEvidence failure)
                && failure.FallbackReason == CudaFallbackReason.NumericalRangeUnsupported, "Non-finite transform must select the existing CPU path.");
            double[] identity = { 1, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0 };
            Require(session.TryTransformPoints(RecoveryPoint, identity, CancellationToken.None, out double[] recovered, out ComputeExecutionEvidence _), "Valid transform must recover after unsupported numerical range.");
            Same(3, recovered[2], "Transform recovery");
        }

        private static Mat Template(int side)
        {
            using Mat original = EdgePoseRefinementSmokeSuite.CreateTemplate();
            Mat result = new Mat();
            Cv2.Resize(original, result, new Size(side, side), 0, 0, InterpolationFlags.Nearest);
            return result;
        }

        private static Mat Source(Mat template, int side, bool duplicate = false)
        {
            Mat source = new Mat(side, side, MatType.CV_8UC1, Scalar.All(0));
            using (Mat roi = new Mat(source, new Rect(17, 23, template.Width, template.Height))) template.CopyTo(roi);
            if (duplicate) using (Mat roi = new Mat(source, new Rect(side - template.Width - 13, side - template.Height - 15, template.Width, template.Height))) template.CopyTo(roi);
            return source;
        }

        private static EdgeBasedTemplateMatchingTool Tool(Mat template, VisionComputeSession session, int step = 1)
        {
            EdgeBasedTemplateMatchingTool tool = new EdgeBasedTemplateMatchingTool(session);
            tool.SetProperty(new EdgeBasedTemplateMatchingToolProperty
            {
                SCORE_MIN = 0.5, GREEDINESS = 0.8, SEARCH_STEP = step, MAX_TEMPLATE_POINTS = 500,
                USE_HYBRID_VERIFY = false, USE_FIND_ANGLE = false, USE_FIND_SCALE = false,
                USE_DRAW_IMAGE = false, USE_POSITION_REFINE = true, USE_SUBPIXEL_REFINE = true,
                CANNY_HIGH = 100, MIN_GRADIENT_MAGNITUDE = 5
            });
            tool.SetTemplateImage(template);
            return tool;
        }

        private static void Compare2D(EdgeBasedTemplateMatchingTool cpu, EdgeBasedTemplateMatchingTool gpu, VisionToolResult first, VisionToolResult second)
        {
            Require(first.Success == second.Success && first.ErrorCode == second.ErrorCode && cpu.results.Count == gpu.results.Count, "2D success/rejection/count must match.");
            var firstMetrics = first.Metrics.Where(pair => !pair.Key.StartsWith("Phase.", StringComparison.Ordinal)).ToArray();
            var secondMetrics = second.Metrics.Where(pair => !pair.Key.StartsWith("Phase.", StringComparison.Ordinal)).ToArray();
            Require(firstMetrics.Length == secondMetrics.Length && firstMetrics.All(pair => second.Metrics.TryGetValue(pair.Key, out double v) && pair.Value.Equals(v)), "All non-time typed 2D metrics must match: " + string.Join("; ", first.Metrics.Where(pair => !second.Metrics.TryGetValue(pair.Key, out double value) || !pair.Value.Equals(value)).Select(pair => pair.Key + " CPU=" + pair.Value.ToString("R", CultureInfo.InvariantCulture) + " GPU=" + (second.Metrics.TryGetValue(pair.Key, out double v) ? v.ToString("R", CultureInfo.InvariantCulture) : "missing"))));
            for (int i = 0; i < cpu.results.Count; i++)
            {
                var a = cpu.results[i]; var b = gpu.results[i];
                Same(a.Score, b.Score, "Final score"); Same(a.EdgeScore, b.EdgeScore, "Edge score");
                Require(a.Center == b.Center && a.Angle == b.Angle && a.Scale == b.Scale && a.Bounding == b.Bounding && a.PolarityReversed == b.PolarityReversed, $"Final row {i} coordinates/pose/polarity must match: CPU center={a.Center} angle={a.Angle:R} scale={a.Scale:R} bounds={a.Bounding} polarity={a.PolarityReversed}; GPU center={b.Center} angle={b.Angle:R} scale={b.Scale:R} bounds={b.Bounding} polarity={b.PolarityReversed}.");
            }
            if (first.ResultImage != null && second.ResultImage != null)
                Require(first.ResultImage.ToBytes(".png").SequenceEqual(second.ResultImage.ToBytes(".png")), "Final image pixels must match.");
        }

        private static void TestTwoD()
        {
            using Mat template = Template(48);
            using Mat source = Source(template, 224);
            using VisionComputeSession session = Session();
            using EdgeBasedTemplateMatchingTool cpu = Tool(template, null);
            using EdgeBasedTemplateMatchingTool gpu = Tool(template, session);
            foreach (bool polarity in new[] { false, true })
            foreach (int step in new[] { 1, 3 })
            {
                Console.WriteLine($"Existing workflow: polarity={polarity}, step={step}");
                cpu.property.ALLOW_GLOBAL_POLARITY_REVERSAL = gpu.property.ALLOW_GLOBAL_POLARITY_REVERSAL = polarity;
                cpu.property.SEARCH_STEP = gpu.property.SEARCH_STEP = step;
                using VisionToolResult expected = cpu.Execute(source);
                using VisionToolResult actual = gpu.Execute(source);
                Compare2D(cpu, gpu, expected, actual);
                Require(actual.ComputeDiagnostics.AccelerationApplied, "Workflow must exercise an actual GPU score batch.");
            }
            foreach (bool duplicates in new[] { false, true })
            {
                using Mat variant = duplicates ? Source(template, 224, true) : new Mat(224, 224, MatType.CV_8UC1, Scalar.All(0));
                using VisionToolResult expected = cpu.Execute(variant);
                using VisionToolResult actual = gpu.Execute(variant);
                Compare2D(cpu, gpu, expected, actual);
            }
            cpu.property.USE_ROI = gpu.property.USE_ROI = true;
            cpu.property.CvROI = gpu.property.CvROI = new Rect(5, 7, 200, 200);
            using (VisionToolResult expected = cpu.Execute(source)) using (VisionToolResult actual = gpu.Execute(source)) Compare2D(cpu, gpu, expected, actual);
            cpu.property.USE_ROI = gpu.property.USE_ROI = false;
            using (Mat inverted = new Mat())
            {
                Cv2.BitwiseNot(source, inverted);
                using VisionToolResult expected = cpu.Execute(inverted); using VisionToolResult actual = gpu.Execute(inverted);
                Compare2D(cpu, gpu, expected, actual);
            }
            using (Mat baseTemplate = EdgePoseRefinementSmokeSuite.CreateTemplate())
            using (Mat transformedSource = EdgePoseRefinementSmokeSuite.CreateSource(baseTemplate, 2.5, 1.025))
            using (EdgeBasedTemplateMatchingTool poseCpu = Tool(baseTemplate, null))
            using (EdgeBasedTemplateMatchingTool poseGpu = Tool(baseTemplate, session))
            {
                poseCpu.property.USE_FIND_ANGLE = poseGpu.property.USE_FIND_ANGLE = true;
                poseCpu.property.FIND_ANGLE_MIN = poseGpu.property.FIND_ANGLE_MIN = -5;
                poseCpu.property.FIND_ANGLE_MAX = poseGpu.property.FIND_ANGLE_MAX = 5;
                poseCpu.property.FIND_ANGLE = poseGpu.property.FIND_ANGLE = 2.5;
                poseCpu.property.USE_FIND_SCALE = poseGpu.property.USE_FIND_SCALE = true;
                poseCpu.property.FIND_SCALE_MIN = poseGpu.property.FIND_SCALE_MIN = 1;
                poseCpu.property.FIND_SCALE_MAX = poseGpu.property.FIND_SCALE_MAX = 1.05;
                poseCpu.property.FIND_SCALE_STEP = poseGpu.property.FIND_SCALE_STEP = 0.025;
                poseCpu.UsePoseRefinement = poseGpu.UsePoseRefinement = true;
                using VisionToolResult expected = poseCpu.Execute(transformedSource); using VisionToolResult actual = poseGpu.Execute(transformedSource);
                Compare2D(poseCpu, poseGpu, expected, actual);
            }
            using CancellationTokenSource cancelled = new CancellationTokenSource();
            cancelled.Cancel();
            RequireThrows<OperationCanceledException>(() => gpu.Execute(source, cancelled.Token));
            Require(gpu.results.Count == 0, "Cancel must clear preceding match rows.");
            using (VisionToolResult invalid = gpu.Execute(null)) Require(!invalid.Success && invalid.ComputeDiagnostics == null, "Invalid input must not retain compute evidence.");
            using (VisionToolResult expected = cpu.Execute(source))
            using (VisionToolResult recovered = gpu.Execute(source)) Compare2D(cpu, gpu, expected, recovered);
        }

        private static SurfaceMatchSample[] Model(int count)
        {
            int side = (int)Math.Ceiling(Math.Sqrt(count));
            return Enumerable.Range(0, count).Select(i =>
            {
                double x = (i % side) * 0.12, y = (i / side) * 0.09;
                return new SurfaceMatchSample(i, new ThreeDPoint(x, y, 0.013 * x * x + 0.005 * x * y + 0.007 * Math.Sin(x * 3)));
            }).ToArray();
        }

        private static SurfaceMatchSample[] Scene(SurfaceMatchSample[] model)
        {
            double angle = 15 * Math.PI / 180.0;
            RigidSurfacePose truth = new RigidSurfacePose(Math.Cos(angle), -Math.Sin(angle), 0, Math.Sin(angle), Math.Cos(angle), 0, 0, 0, 1, 0.77, -0.31, 0.12);
            return model.Select(sample => new SurfaceMatchSample(sample.Order, truth.Transform(sample.Position))).ToArray();
        }

        private static DeterministicRigidSurfacePoseSearchOptions Options(int count)
        {
            var options = SurfaceSearchRobustnessSmokeSuite.Options();
            options.MinimumRotationZDegrees = -90; options.MaximumRotationZDegrees = 90; options.RotationStepZDegrees = 5;
            options.MinimumMatchedSampleCount = count / 2; options.MaximumCorrespondenceDistance = 0.04;
            return options;
        }

        private static void Compare3D(DeterministicRigidSurfacePoseSearchResult expected, DeterministicRigidSurfacePoseSearchResult actual)
        {
            Require(expected.Success == actual.Success && expected.Matched == actual.Matched && expected.EvaluatedCandidateCount == actual.EvaluatedCandidateCount, "3D candidate count/success/decision must match.");
            Require(expected.Coverage.Matches.Count == actual.Coverage.Matches.Count, "Ordered coverage count must match.");
            Same(expected.Coverage.InlierRmse, actual.Coverage.InlierRmse, "Coverage RMSE");
            for (int i = 0; i < expected.Coverage.Matches.Count; i++)
            {
                var a = expected.Coverage.Matches[i]; var b = actual.Coverage.Matches[i];
                Require(a.ModelSampleOrder == b.ModelSampleOrder && a.SceneSampleOrder == b.SceneSampleOrder, "Ordered claim identities must match.");
                Same(a.Distance, b.Distance, "Correspondence distance");
            }
            Require((expected.Pose == null) == (actual.Pose == null), "Selected pose presence must match.");
            if (expected.Pose != null)
            {
                var a = expected.Pose; var b = actual.Pose;
                Same(a.TranslationX, b.TranslationX, "Pose X"); Same(a.TranslationY, b.TranslationY, "Pose Y"); Same(a.TranslationZ, b.TranslationZ, "Pose Z");
                Same(a.M11, b.M11, "Pose M11"); Same(a.M12, b.M12, "Pose M12"); Same(a.M13, b.M13, "Pose M13");
                Same(a.M21, b.M21, "Pose M21"); Same(a.M22, b.M22, "Pose M22"); Same(a.M23, b.M23, "Pose M23");
                Same(a.M31, b.M31, "Pose M31"); Same(a.M32, b.M32, "Pose M32"); Same(a.M33, b.M33, "Pose M33");
            }
        }

        private static void TestThreeD()
        {
            using VisionComputeSession session = Session();
            var cpu = new DeterministicRigidSurfacePoseSearchTool();
            var gpu = new DeterministicRigidSurfacePoseSearchTool(session);
            foreach (int count in new[] { 257, 512 })
            {
                var model = Model(count); var scene = Scene(model); var options = Options(count);
                var expected = cpu.Execute(model, scene, options); var actual = gpu.Execute(model, scene, options);
                Compare3D(expected, actual);
                Require(actual.ComputeDiagnostics.AccelerationApplied, "3D workflow must exercise GPU transforms while preserving CPU claims.");
                using CancellationTokenSource cancelled = new CancellationTokenSource(); cancelled.Cancel();
                RequireThrows<OperationCanceledException>(() => gpu.Execute(model, scene, options, cancelled.Token));
                Compare3D(expected, gpu.Execute(model, scene, options));
            }
        }

        private static void TestLifetime()
        {
            for (int cycle = 0; cycle < 3; cycle++)
            {
                using VisionComputeSession session = Session();
                double[] values = Enumerable.Repeat(0.5, 4097).ToArray();
                for (int run = 0; run < 12; run++)
                {
                    Require(session.TryScoreEdgeGrid(values, new double[values.Length], ZeroOffset, One, Zero, Zero,
                        values.Length, 0, 0, values.Length, 1, 1, false, CancellationToken.None, out double[] scores, out ComputeExecutionEvidence evidence), "Repeated actual CUDA execution must recover buffers.");
                    Same(0.5, scores[4096], "Odd tail candidate"); Require(evidence.PeakScratchBytes < 64UL * 1024 * 1024, "Scratch budget must remain bounded.");
                }
                using CancellationTokenSource cancelled = new CancellationTokenSource(); cancelled.Cancel();
                RequireThrows<OperationCanceledException>(() => session.TryTransformPoints(RecoveryPoint, new double[12], cancelled.Token, out double[] _, out ComputeExecutionEvidence _));
                Require(!session.TryScoreEdgeGrid(values, values, ZeroOffset, One, Zero, Zero, values.Length,
                    0, 0, int.MaxValue, int.MaxValue, 1, false, CancellationToken.None, out double[] rejected, out ComputeExecutionEvidence rejectedEvidence)
                    && rejected == null && rejectedEvidence.FallbackReason == CudaFallbackReason.GpuMemoryUnavailable,
                    "An oversized GPU result must select CPU before allocating a candidate matrix.");
                Require(session.TryScoreEdgeGrid(values, values, ZeroOffset, One, Zero, Zero, values.Length,
                    0, 0, values.Length, 1, 1, false, CancellationToken.None, out double[] recovered, out ComputeExecutionEvidence _), "Budget rejection must preserve session recovery.");
                Same(0.5, recovered[4096], "Budget recovery");
                session.Dispose();
                RequireThrows<ObjectDisposedException>(() => session.ProbeCuda());
            }
        }

        private static void TestPolicy()
        {
            using VisionComputeSession session = Session(study: false);
            using Mat template = Template(48);
            foreach (int size in new[] { 224, 512 })
            {
                using Mat source = Source(template, size);
                using EdgeBasedTemplateMatchingTool cpu = Tool(template, null, 2);
                using EdgeBasedTemplateMatchingTool gpu = Tool(template, session, 2);
                using VisionToolResult a = cpu.Execute(source); using VisionToolResult b = gpu.Execute(source);
                Compare2D(cpu, gpu, a, b);
                Require(b.ComputeDiagnostics.AccelerationApplied == (size == 512), "Measured grid policy must select the intended backend.");
                if (size == 224) Require(b.ComputeDiagnostics.FallbackReason == CudaFallbackReason.WorkloadTooSmall && !session.Support.IsAccelerationAvailable, "Small inputs must not create a GPU execution context.");
            }
            var model = Model(1024); var scene = Scene(model); var options = Options(1024);
            var expected = new DeterministicRigidSurfacePoseSearchTool().Execute(model, scene, options);
            var actual = new DeterministicRigidSurfacePoseSearchTool(session).Execute(model, scene, options);
            Compare3D(expected, actual);
            Require(!actual.ComputeDiagnostics.AccelerationApplied && actual.ComputeDiagnostics.FallbackReason == CudaFallbackReason.NoMeasuredBenefit, "A qualified 2D session must keep the measured slower 3D path on CPU.");
        }


        internal static int LargeImageBenchmark(string outputPath, bool useCuda, int imageSide)
        {
            if (imageSide != 5000 && imageSide != 10000 && imageSide != 15000)
                throw new ArgumentOutOfRangeException(nameof(imageSide), "Large-image dimensions must be 5000, 10000 or 15000 pixels per side.");
            if (useCuda && string.IsNullOrEmpty(ModulePath)) throw new InvalidOperationException("OVL_CUDA_TEST_MODULE must name the tested native kernel module.");

            const int templateSide = 192, step = 2, repeats = 3;
            using Mat template = Template(templateSide);
            using Mat source = Source(template, imageSide);
            using VisionComputeSession session = useCuda ? Session(study: false) : null;
            using EdgeBasedTemplateMatchingTool tool = Tool(template, session, step);
            using Process process = Process.GetCurrentProcess();
            tool.CollectPhaseTimings = true;
            string inputPixelsSha256 = PixelDigest(source);
            string expectedFingerprint = null;
            List<object> samples = new List<object>();
            List<double> warmTimes = new List<double>();
            int validatedSamples = 0;
            bool anyAccelerationApplied = false;
            long pixels = (long)imageSide * imageSide;
            Type gradientType = typeof(EdgeBasedTemplateMatchingTool).GetNestedType("GradientImage", System.Reflection.BindingFlags.NonPublic);
            int scratchBudget = (int)gradientType.GetField("MagnitudeBatchBytes", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static).GetRawConstantValue();
            long scratchRows = Math.Max(1, Math.Min(imageSide, (scratchBudget / sizeof(double)) / imageSide));
            long gradientNativeTemporaryBytes = scratchRows * imageSide * sizeof(double);

            void WriteReport()
            {
                File.WriteAllText(outputPath, JsonSerializer.Serialize(new
                {
                    scope = "Full Execute: preprocessing, gradients, full search, transfer, decision, refinement, drawing and result clone. Input/template setup, between-call GC and pixel validation excluded.",
                    input = new { width = imageSide, height = imageSide, pixels, inputPixelsSha256, pixelType = "CV_8UC1", templateSide, templateLocationX = 17, templateLocationY = 23,
                        background = "synthetic flat zero", searchStep = step, maximumTemplatePoints = 500, useRoi = false, resizeInput = false,
                        sourceBytes = pixels, gradientArrayBytes = pixels * 16, gradientNativeTemporaryBytes },
                    requestedBackend = useCuda ? "CUDA" : "CPU", repeats, validatedSamples, complete = validatedSamples == repeats + 1,
                    firstCall = "First Execute in this process/session; CUDA Driver cache initially empty, controlled by caller; OS caches uncontrolled",
                    warmPolicy = "Retained tool/session; explicit full GC outside each timer to bound large-image memory; not production soak performance",
                    warmMedianMilliseconds = warmTimes.Count == repeats ? (double?)Median(warmTimes.ToArray()) : null,
                    anyAccelerationApplied, resultFingerprint = expectedFingerprint,
                    driverCache = Environment.GetEnvironmentVariable("CUDA_CACHE_PATH"), nativeModule = useCuda ? ModulePath : null,
                    processId = process.Id, samples
                }, ReportOptions));
            }

            for (int run = 0; run <= repeats; run++)
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
                long allocatedBefore = GC.GetTotalAllocatedBytes(true);
                TimeSpan cpuBefore = process.TotalProcessorTime;
                Console.WriteLine("BEGIN large image=" + imageSide + " requested=" + (useCuda ? "CUDA" : "CPU") + " run=" + run);
                Stopwatch timer = Stopwatch.StartNew();
                using VisionToolResult result = tool.Execute(source);
                timer.Stop();
                double elapsed = timer.Elapsed.TotalMilliseconds;
                process.Refresh();
                long allocatedBytes = GC.GetTotalAllocatedBytes(true) - allocatedBefore;
                double processCpuMilliseconds = (process.TotalProcessorTime - cpuBefore).TotalMilliseconds;
                long workingSetBytes = process.WorkingSet64, privateBytes = process.PrivateMemorySize64;
                long processPeakWorkingSetBytes = process.PeakWorkingSet64, processPeakPagedMemoryBytes = process.PeakPagedMemorySize64;
                var phases = tool.LastPhaseElapsedMs.ToDictionary(pair => pair.Key, pair => pair.Value);
                var matches = tool.results.Select(match => new
                {
                    match.Index, scoreBits = BitConverter.DoubleToInt64Bits(match.Score), edgeScoreBits = BitConverter.DoubleToInt64Bits(match.EdgeScore),
                    imageScoreBits = BitConverter.DoubleToInt64Bits(match.ImageScore), finalScoreBits = BitConverter.DoubleToInt64Bits(match.FinalScore),
                    scoreMarginBits = BitConverter.DoubleToInt64Bits(match.ScoreMargin), match.PolarityReversed,
                    angleBits = BitConverter.DoubleToInt64Bits(match.Angle), scaleBits = BitConverter.DoubleToInt64Bits(match.Scale),
                    centerXBits = BitConverter.SingleToInt32Bits(match.Center.X), centerYBits = BitConverter.SingleToInt32Bits(match.Center.Y),
                    boundsXBits = BitConverter.SingleToInt32Bits(match.Bounding.X), boundsYBits = BitConverter.SingleToInt32Bits(match.Bounding.Y),
                    boundsWidthBits = BitConverter.SingleToInt32Bits(match.Bounding.Width), boundsHeightBits = BitConverter.SingleToInt32Bits(match.Bounding.Height)
                }).ToArray();
                var metrics = result.Metrics.Where(pair => !pair.Key.StartsWith("Phase.", StringComparison.Ordinal)).OrderBy(pair => pair.Key, StringComparer.Ordinal)
                    .ToDictionary(pair => pair.Key, pair => BitConverter.DoubleToInt64Bits(pair.Value));
                bool fullOutputImage = result.ResultImage != null && result.ResultImage.Width == imageSide && result.ResultImage.Height == imageSide;
                string outputPixelsSha256 = fullOutputImage ? PixelDigest(result.ResultImage) : null;
                var snapshot = new { result.Success, result.ErrorCode, metrics, matches, outputPixelsSha256,
                    outputWidth = result.ResultImage?.Width, outputHeight = result.ResultImage?.Height, outputType = result.ResultImage?.Type().ToString() };
                string fingerprint = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(JsonSerializer.Serialize(snapshot))));
                bool truthMatches = result.Success && tool.results.Count == 1 && fullOutputImage
                    && Math.Abs(tool.results[0].Bounding.X - 17) <= 2 && Math.Abs(tool.results[0].Bounding.Y - 23) <= 2
                    && tool.results[0].Bounding.Width == templateSide && tool.results[0].Bounding.Height == templateSide
                    && tool.results[0].Score >= tool.property.SCORE_MIN && tool.results[0].Angle == 0 && tool.results[0].Scale == 1;
                bool stableResult = expectedFingerprint == null || expectedFingerprint == fingerprint;
                expectedFingerprint ??= fingerprint;
                anyAccelerationApplied |= result.ComputeDiagnostics?.AccelerationApplied == true;
                samples.Add(new { run, cold = run == 0, elapsedMilliseconds = elapsed, processCpuMilliseconds, allocatedBytes,
                    workingSetBytes, privateBytes, processPeakWorkingSetBytes, processPeakPagedMemoryBytes,
                    memoryScope = "Process lifetime high-water marks include setup and earlier calls; endpoint values sampled before pixel validation", phases,
                    actualBackend = result.ComputeDiagnostics?.AccelerationApplied == true ? "CUDA" : "CPU", compute = result.ComputeDiagnostics,
                    truthMatches, stableResult, resultFingerprint = fingerprint, snapshot });
                if (run > 0) warmTimes.Add(elapsed);
                if (truthMatches && stableResult) validatedSamples++;
                WriteReport();
                Console.WriteLine("END large image=" + imageSide + " run=" + run + " ms=" + elapsed.ToString("F2", CultureInfo.InvariantCulture)
                    + " applied=" + (result.ComputeDiagnostics?.AccelerationApplied == true) + " fallback=" + result.ComputeDiagnostics?.FallbackReason
                    + " processPeakBytes=" + processPeakWorkingSetBytes);
                Require(truthMatches && stableResult, "Large-image independent position, full output and repeated result must agree.");
            }
            return 0;
        }

        private static string PixelDigest(Mat image)
        {
            using var hash = System.Security.Cryptography.IncrementalHash.CreateHash(System.Security.Cryptography.HashAlgorithmName.SHA256);
            byte[] row = new byte[checked(image.Width * (int)image.ElemSize())];
            for (int y = 0; y < image.Height; y++)
            {
                Marshal.Copy(image.Ptr(y), row, 0, row.Length);
                hash.AppendData(row);
            }
            return Convert.ToHexString(hash.GetHashAndReset());
        }

        internal static int ColdBenchmark(string outputPath, bool useCuda)
        {
            using Mat template = Template(96); using Mat source = Source(template, 512);
            using VisionComputeSession session = useCuda ? Session(study: false) : null;
            using EdgeBasedTemplateMatchingTool tool = Tool(template, session, 2);
            Stopwatch timer = Stopwatch.StartNew();
            using VisionToolResult result = tool.Execute(source);
            double elapsed = timer.Elapsed.TotalMilliseconds;
            Require(result.Success && tool.results.Count > 0, "Fresh-process measurement must produce a match.");
            if (useCuda) Require(result.ComputeDiagnostics?.AccelerationApplied == true, "Fresh-process CUDA measurement must execute the kernel.");
            File.WriteAllText(outputPath, JsonSerializer.Serialize(new { scope = "First complete Execute of a fresh process; input/template setup excluded; owned empty Driver JIT cache for CUDA",
                useCuda, elapsedMs = elapsed, result.Success, result.ComputeDiagnostics, driverCache = Environment.GetEnvironmentVariable("CUDA_CACHE_PATH") }, ReportOptions));
            return 0;
        }

        internal static int Benchmark(string outputPath, bool study = false)
        {
            if (string.IsNullOrEmpty(ModulePath)) throw new InvalidOperationException("OVL_CUDA_TEST_MODULE must name the tested native kernel module.");
            List<object> rows = new List<object>();
            foreach (var input in new[] { (image: 224, template: 48), (image: 512, template: 96), (image: 1024, template: 192) })
            {
                using Mat template = Template(input.template); using Mat source = Source(template, input.image);
                using VisionComputeSession session = Session(study: study);
                using EdgeBasedTemplateMatchingTool cpu = Tool(template, null, 2); using EdgeBasedTemplateMatchingTool gpu = Tool(template, session, 2);
                Stopwatch timer = Stopwatch.StartNew(); using (VisionToolResult _ = cpu.Execute(source)) { }
                double cpuCold = timer.Elapsed.TotalMilliseconds;
                ComputeExecutionEvidence coldEvidence;
                timer.Restart(); using (VisionToolResult result = gpu.Execute(source)) { coldEvidence = result.ComputeDiagnostics; Require(coldEvidence != null, "Benchmark must report the actual selected backend."); }
                double gpuCold = timer.Elapsed.TotalMilliseconds;
                double[] cpuTimes = new double[9], gpuTimes = new double[9]; ComputeExecutionEvidence evidence = null;
                for (int run = 0; run < 9; run++)
                {
                    for (int slot = 0; slot < 2; slot++)
                    {
                        bool useGpu = (run + slot) % 2 == 1;
                        timer.Restart(); using VisionToolResult result = (useGpu ? gpu : cpu).Execute(source);
                        if (useGpu) { gpuTimes[run] = timer.Elapsed.TotalMilliseconds; evidence = result.ComputeDiagnostics; } else cpuTimes[run] = timer.Elapsed.TotalMilliseconds;
                    }
                }
                using (VisionToolResult a = cpu.Execute(source)) using (VisionToolResult b = gpu.Execute(source)) Compare2D(cpu, gpu, a, b);
                rows.Add(new { kind = "2D Edge complete", input.image, input.template, repeats = 9, cpuColdMs = cpuCold, gpuColdMs = gpuCold,
                    cpuMedianMs = Median(cpuTimes), gpuMedianMs = Median(gpuTimes), speedup = Median(cpuTimes) / Median(gpuTimes), cpuTimes, gpuTimes, coldEvidence, evidence });
                Console.WriteLine("2D " + input.image + " CPU=" + Median(cpuTimes) + " CUDA=" + Median(gpuTimes));
            }
            foreach (int count in new[] { 257, 1024, 4096 })
            {
                var model = Model(count); var scene = Scene(model); var options = Options(count);
                using VisionComputeSession session = Session(study: study); var cpu = new DeterministicRigidSurfacePoseSearchTool(); var gpu = new DeterministicRigidSurfacePoseSearchTool(session);
                Stopwatch timer = Stopwatch.StartNew(); var a = cpu.Execute(model, scene, options); double cpuCold = timer.Elapsed.TotalMilliseconds;
                timer.Restart(); var b = gpu.Execute(model, scene, options); double gpuCold = timer.Elapsed.TotalMilliseconds; Compare3D(a, b);
                double[] cpuTimes = new double[9], gpuTimes = new double[9]; ComputeExecutionEvidence evidence = null;
                for (int run = 0; run < 9; run++)
                for (int slot = 0; slot < 2; slot++)
                {
                    bool useGpu = (run + slot) % 2 == 1;
                    timer.Restart(); var result = (useGpu ? gpu : cpu).Execute(model, scene, options);
                    if (useGpu) { gpuTimes[run] = timer.Elapsed.TotalMilliseconds; evidence = result.ComputeDiagnostics; } else cpuTimes[run] = timer.Elapsed.TotalMilliseconds;
                }
                rows.Add(new { kind = "3D Pose complete", count, repeats = 9, cpuColdMs = cpuCold, gpuColdMs = gpuCold, cpuMedianMs = Median(cpuTimes),
                    gpuMedianMs = Median(gpuTimes), speedup = Median(cpuTimes) / Median(gpuTimes), cpuTimes, gpuTimes, coldEvidence = b.ComputeDiagnostics, evidence });
                Console.WriteLine("3D " + count + " CPU=" + Median(cpuTimes) + " CUDA=" + Median(gpuTimes));
            }
            var report = new { source = "PL-0039 local Dev", study, scope = "synthetic inputs, full invocation including preprocessing/transfers/CPU decision; cold means first call of a session; driver JIT cache uncontrolled", rows };
            File.WriteAllText(outputPath, JsonSerializer.Serialize(report, ReportOptions));
            return 0;
        }

        private static void TestFaults()
        {
            string directory = Environment.GetEnvironmentVariable("OVL_CUDA_EXECUTION_FIXTURES");
            CudaFallbackReason[] expectedReasons = { CudaFallbackReason.None, CudaFallbackReason.KernelInitializationFailed,
                CudaFallbackReason.GpuMemoryUnavailable, CudaFallbackReason.GpuExecutionFailed, CudaFallbackReason.InvalidKernelResult, CudaFallbackReason.None, CudaFallbackReason.NativeAbiMismatch, CudaFallbackReason.InvalidKernelResult };
            using Mat template = Template(48); using Mat source = Source(template, 224);
            for (int kind = 1; kind <= 7; kind++)
            {
                if (kind == 5) continue;
                string path = Path.Combine(directory, "execution-" + kind + ".dll");
                IntPtr module = CudaNativeProbe.LoadLibraryExW(path, IntPtr.Zero, CudaNativeProbe.SearchDllDirectoryAndSystem32);
                Require(module != IntPtr.Zero, "Fault fixture must load.");
                try
                {
                    Counter live = CudaNativeProbe.GetExport<Counter>(module, "ovl_test_live_executions");
                    using VisionComputeSession session = Session(path);
                    using EdgeBasedTemplateMatchingTool cpu = Tool(template, null); using EdgeBasedTemplateMatchingTool gpu = Tool(template, session);
                    using VisionToolResult a = cpu.Execute(source); using VisionToolResult b = gpu.Execute(source);
                    Compare2D(cpu, gpu, a, b);
                    Require(!b.ComputeDiagnostics.AccelerationApplied && b.ComputeDiagnostics.FallbackReason == expectedReasons[kind], "Fault must publish the precise CPU fallback reason: " + kind);
                    using (VisionToolResult recovered = gpu.Execute(source)) Compare2D(cpu, gpu, a, recovered);
                    if (kind == 2) Score(session, new[] { 1.0, -1.0, 0.0, 0.5 }, CancellationToken.None);
                    session.Dispose(); Require(live() == 0, "Partial/failed execution handles must be released.");
                }
                finally { _ = CudaNativeProbe.FreeLibrary(module); }
            }
        }

        private static void TestCancellationAndDisposal()
        {
            string path = Path.Combine(Environment.GetEnvironmentVariable("OVL_CUDA_EXECUTION_FIXTURES"), "execution-5.dll");
            IntPtr module = CudaNativeProbe.LoadLibraryExW(path, IntPtr.Zero, CudaNativeProbe.SearchDllDirectoryAndSystem32);
            try
            {
                Counter active = CudaNativeProbe.GetExport<Counter>(module, "ovl_test_active_calls");
                Counter live = CudaNativeProbe.GetExport<Counter>(module, "ovl_test_live_executions");
                using VisionComputeSession session = Session(path);
                using CancellationTokenSource running = new CancellationTokenSource();
                using CancellationTokenSource waiting = new CancellationTokenSource();
                double[] source = Enumerable.Repeat(0.5, 4097).ToArray();
                using (CancellationTokenSource retry = new CancellationTokenSource())
                {
                    Task cancelledWork = Task.Run(() => RequireThrows<OperationCanceledException>(() => Score(session, source, retry.Token)));
                    Require(SpinWait.SpinUntil(() => active() > 0, 5000), "First fixture call must be submitted before cancellation.");
                    retry.Cancel(); Require(cancelledWork.Wait(2000), "Submitted cancellation must return.");
                    Score(session, new[] { 1.0, -1.0, 0.0, 0.5 }, CancellationToken.None);
                }
                Task work = Task.Run(() => RequireThrows<OperationCanceledException>(() => Score(session, source, running.Token)));
                Require(SpinWait.SpinUntil(() => active() > 0, 5000), "Fixture must reach an active submitted operation.");
                Task queued = Task.Run(() => RequireThrows<OperationCanceledException>(() => Score(session, source, waiting.Token)));
                waiting.Cancel(); Require(queued.Wait(2000), "Waiting cancellation must not wait for the native operation.");
                Task disposing = Task.Run(() => session.Dispose());
                Require(!disposing.IsCompleted, "Disposal must not free the active native handle.");
                running.Cancel(); Require(work.Wait(2000) && disposing.Wait(2000), "Native cooperative cancellation must complete before disposal.");
                Require(active() == 0 && live() == 0, "Disposal must leave no fixture operation or execution handle.");
                using VisionComputeSession recovered = Session(path);
                Score(recovered, new[] { 1.0, -1.0, 0.0, 0.5 }, CancellationToken.None);
            }
            finally { _ = CudaNativeProbe.FreeLibrary(module); }
        }

        private static void Score(VisionComputeSession session, double[] source, CancellationToken token)
        {
            Require(session.TryScoreEdgeGrid(source, new double[source.Length], ZeroOffset, One, Zero, MinusTwo, source.Length,
                0, 0, source.Length, 1, 1, false, token, out double[] _, out ComputeExecutionEvidence _), "Valid fixture operation must succeed.");
        }

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Counter();
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int SetDelay(int enabled);
        private static double Median(double[] samples) => samples.OrderBy(value => value).ElementAt(samples.Length / 2);
    }
}
