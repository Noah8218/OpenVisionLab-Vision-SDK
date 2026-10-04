using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using OpenVisionLab.Compute.Infrastructure;

namespace OpenVisionLab.Compute
{
    /// <summary>Host-owned, lazy optional acceleration session. Tools borrow it; construction does not probe hardware.</summary>
    public sealed class VisionComputeSession : IDisposable
    {
        private const int MaximumResultBytes = 64 * 1024 * 1024;
        private readonly object sync = new object();
        private readonly bool useCuda;
        private readonly int? deviceOrdinal;
        private readonly long? edgeTileBudgetBytes;
        private readonly Func<CancellationToken, CudaSupportReport> probe;
        private CudaSupportReport support;
        private bool disposed;
        private CudaNativeExecution execution;
        private CudaFallbackReason? executionFailure;
        private int executionError;

        public VisionComputeSession() : this(new VisionComputeOptions()) { }

        public VisionComputeSession(VisionComputeOptions options) : this(options, CudaNativeProbe.Probe) { }

        internal VisionComputeSession(VisionComputeOptions options, Func<CancellationToken, CudaSupportReport> probe, bool evaluateUnqualifiedWorkloads = false, long? edgeTileBudgetBytes = null)
        {
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (options.DeviceOrdinal < 0) throw new ArgumentOutOfRangeException(nameof(options), "DeviceOrdinal must be nonnegative or null.");
            if (edgeTileBudgetBytes <= 0) throw new ArgumentOutOfRangeException(nameof(edgeTileBudgetBytes));
            this.edgeTileBudgetBytes = edgeTileBudgetBytes;
            this.probe = probe ?? throw new ArgumentNullException(nameof(probe));
            useCuda = options.UseCuda;
            deviceOrdinal = options.DeviceOrdinal;
            EvaluateUnqualifiedWorkloads = evaluateUnqualifiedWorkloads;
            support = new CudaSupportReport(useCuda ? CudaFallbackReason.NotProbed : CudaFallbackReason.Disabled);
        }

        public bool RequestedCuda => useCuda;
        internal bool EvaluateUnqualifiedWorkloads { get; }

        /// <summary>Returns the current immutable snapshot without initializing or executing anything.</summary>
        public CudaSupportReport Support
        {
            get { lock (sync) { ThrowIfDisposed(); return support; } }
        }

        /// <summary>Explicit read-only discovery, cached once per session. A cancelled probe is not cached.</summary>
        public CudaSupportReport ProbeCuda(CancellationToken cancellationToken = default(CancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            while (!Monitor.TryEnter(sync, 50)) cancellationToken.ThrowIfCancellationRequested();
            try
            {
                ThrowIfDisposed();
                cancellationToken.ThrowIfCancellationRequested();
                if (support.Reason != CudaFallbackReason.NotProbed) return support;
                CudaSupportReport discovered = probe(cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                if (discovered == null) discovered = new CudaSupportReport(CudaFallbackReason.InvalidNativeResult);
                if (discovered.DeviceDetected)
                {
                    int selected = deviceOrdinal ?? discovered.Devices[0].Ordinal;
                    bool found = false;
                    foreach (CudaDeviceInfo device in discovered.Devices) if (device.Ordinal == selected) { found = true; break; }
                    discovered = new CudaSupportReport(found ? discovered.Reason : CudaFallbackReason.RequestedDeviceUnavailable,
                        discovered.ModulePath, discovered.DriverApiVersion, discovered.Devices, found ? (int?)selected : null, discovered.NativeErrorCode);
                }
                support = discovered;
                return support;
            }
            finally { Monitor.Exit(sync); }
        }

        /// <summary>Describes the CPU path used by current tools. No GPU algorithm is executed by this method.</summary>
        public ComputeExecutionEvidence DescribeCpuExecution(CancellationToken cancellationToken = default(CancellationToken))
        {
            CudaSupportReport report = ProbeCuda(cancellationToken);
            return new ComputeExecutionEvidence(useCuda, report, reason: report.IsAccelerationAvailable ? CudaFallbackReason.KernelNotImplemented : report.Reason);
        }

        internal ComputeExecutionEvidence DescribeCpuExecution(CudaFallbackReason reason, CancellationToken token)
        {
            CudaSupportReport report = ProbeCuda(token);
            return new ComputeExecutionEvidence(useCuda, report, reason: report.Reason == CudaFallbackReason.None || report.Reason == CudaFallbackReason.KernelNotImplemented ? reason : report.Reason, driverError: report.NativeErrorCode);
        }

        internal bool TryScoreEdgeGrid(double[] sx, double[] sy, int[] offsets, double[] mx, double[] my, double[] thresholds,
            int width, int minX, int minY, int columns, int rows, int step, bool polarity, CancellationToken token,
            out double[] scores, out ComputeExecutionEvidence evidence)
        {
            scores = null;
            evidence = null;
            Enter(token);
            try
            {
                ThrowIfDisposed();
                if (!PrepareKernel(token, out double initializationMs))
                { evidence = new ComputeExecutionEvidence(useCuda, support, reason: executionFailure, initializationMs: initializationMs, driverError: executionError); return false; }
                if (!TryAllocateResult((long)columns * rows, initializationMs, out double[] values, out evidence))
                { token.ThrowIfCancellationRequested(); return false; }
                int status = execution.Score(sx, sy, offsets, mx, my, thresholds, width, minX, minY, columns, rows, step, polarity, values, token, out CudaNativeExecution.NativeTiming timing);
                bool success = FinishBatch(status, values, false, timing, initializationMs, out evidence);
                token.ThrowIfCancellationRequested();
                if (success) scores = values;
                return success;
            }
            finally { Monitor.Exit(sync); }
        }


        // The caller owns this buffer and consumes its valid prefix synchronously inside the session lock.
        internal sealed class EdgeScoreTile
        {
            internal double[] Scores;
            internal int MinX, MinY, Columns, Rows;
            internal bool CollectTimings;
            internal long LockWaitTicks, PreparationTicks, AllocationTicks, PackingTicks, NativeCallTicks, ValidationTicks, ConsumptionTicks;

            internal long StartTiming() => CollectTimings ? Stopwatch.GetTimestamp() : 0L;
            internal static long ElapsedTicks(long start) => start == 0L ? 0L : Stopwatch.GetTimestamp() - start;
            internal void ResetTimings() => LockWaitTicks = PreparationTicks = AllocationTicks = PackingTicks = NativeCallTicks = ValidationTicks = ConsumptionTicks = 0L;
        }

        internal static bool TryPlanEdgeTile(int columns, int rows, int step, int spanX, int spanY, int points,
            ulong budget, out int tileColumns, out int tileRows)
        {
            tileColumns = tileRows = 0;
            if (columns <= 0 || rows <= 0 || step <= 0 || spanX < 0 || spanY < 0 || points <= 0) return false;
            bool Fits(int c, int r)
            {
                long count = (long)c * r;
                if (count > MaximumResultBytes / sizeof(double)) return false;
                long w = (long)(c - 1) * step + spanX + 1;
                long h = (long)(r - 1) * step + spanY + 1;
                ulong overhead = (ulong)points * 32 + (ulong)count * 8;
                return w <= int.MaxValue && h <= int.MaxValue
                    && overhead <= budget && (ulong)w <= (budget - overhead) / 16 / (ulong)h;
            }
            int Largest(int maximum, Func<int, bool> fits)
            {
                int low = 1, high = maximum;
                while (low < high)
                {
                    int mid = low + (high - low + 1) / 2;
                    if (fits(mid)) low = mid; else high = mid - 1;
                }
                return low;
            }
            if (!Fits(1, 1)) return false;
            if (Fits(columns, 1))
            {
                tileColumns = columns;
                tileRows = Largest(rows, r => Fits(columns, r));
            }
            else
            {
                tileColumns = Largest(columns, c => Fits(c, 1));
                tileRows = 1;
            }
            return true;
        }

        internal bool TryScoreEdgeTiles(double[] sx, double[] sy, int[] offsetX, int[] offsetY,
            double[] mx, double[] my, double[] thresholds, int width, int minX, int minY,
            int columns, int rows, int step, bool polarity, EdgeScoreTile tile, Action consume,
            CancellationToken token, out ComputeExecutionEvidence evidence)
        {
            if (sx == null || sy == null || offsetX == null || offsetY == null || mx == null || my == null
                || thresholds == null || tile == null || consume == null) throw new ArgumentNullException(nameof(tile));
            tile.ResetTimings();
            int points = offsetX.Length;
            if (width <= 0 || sx.Length == 0 || sx.Length != sy.Length || sx.Length % width != 0 || points == 0
                || offsetY.Length != points || mx.Length != points || my.Length != points || thresholds.Length != points
                || columns <= 0 || rows <= 0 || step <= 0) throw new ArgumentException("Invalid edge tile grid dimensions.");
            int height = sx.Length / width;
            int minDx = offsetX[0], maxDx = offsetX[0], minDy = offsetY[0], maxDy = offsetY[0];
            for (int i = 1; i < points; i++)
            {
                minDx = Math.Min(minDx, offsetX[i]); maxDx = Math.Max(maxDx, offsetX[i]);
                minDy = Math.Min(minDy, offsetY[i]); maxDy = Math.Max(maxDy, offsetY[i]);
            }
            long started = tile.StartTiming();
            Enter(token);
            tile.LockWaitTicks += EdgeScoreTile.ElapsedTicks(started);
            try
            {
                ThrowIfDisposed();
                started = tile.StartTiming();
                bool prepared = PrepareKernel(token, out double initializationMs);
                tile.PreparationTicks += EdgeScoreTile.ElapsedTicks(started);
                if (!prepared)
                { evidence = new ComputeExecutionEvidence(useCuda, support, reason: executionFailure, initializationMs: initializationMs, driverError: executionError); return false; }
                ComputeExecutionEvidence accumulated = new ComputeExecutionEvidence(useCuda, support, initializationMs: initializationMs);
                ComputeExecutionEvidence Outcome(bool applied, CudaFallbackReason? reason = null) => new ComputeExecutionEvidence(useCuda, support, applied,
                    reason ?? accumulated.FallbackReason, accumulated.InitializationMilliseconds, accumulated.UploadMilliseconds,
                    accumulated.KernelWaitMilliseconds, accumulated.DownloadMilliseconds, accumulated.PeakScratchBytes,
                    accumulated.GpuBatchCount, accumulated.NativeExecutionErrorCode);
                if ((long)minX + minDx < 0 || (long)minY + minDy < 0
                    || (long)minX + (long)(columns - 1) * step + maxDx >= width
                    || (long)minY + (long)(rows - 1) * step + maxDy >= height)
                { evidence = Outcome(false, CudaFallbackReason.NumericalRangeUnsupported); return false; }
                if (!execution.SupportsEdgeTiles)
                {
                    long count = (long)columns * rows;
                    if (count > MaximumResultBytes / sizeof(double)
                        || (long)sx.Length * 16 + (long)points * 28 + Math.Min(16384L, count) * 8 > MaximumResultBytes)
                    { evidence = Outcome(false, CudaFallbackReason.KernelNotImplemented); return false; }
                    int[] offsets = new int[points];
                    for (int i = 0; i < points; i++) offsets[i] = checked(offsetY[i] * width + offsetX[i]);
                    started = tile.StartTiming();
                    bool applied = TryScoreEdgeGrid(sx, sy, offsets, mx, my, thresholds, width, minX, minY,
                        columns, rows, step, polarity, token, out double[] scores, out ComputeExecutionEvidence legacy);
                    tile.NativeCallTicks += EdgeScoreTile.ElapsedTicks(started);
                    accumulated = accumulated.Combine(legacy);
                    if (!applied) { evidence = Outcome(false); return false; }
                    tile.Scores = scores; tile.MinX = minX; tile.MinY = minY; tile.Columns = columns; tile.Rows = rows;
                    started = tile.StartTiming();
                    try { consume(); }
                    finally { tile.ConsumptionTicks += EdgeScoreTile.ElapsedTicks(started); }
                    token.ThrowIfCancellationRequested();
                    evidence = Outcome(true);
                    return true;
                }

                started = tile.StartTiming();
                int budgetStatus = execution.GetEdgeTileBudget(out ulong budget, out int budgetError);
                tile.PreparationTicks += EdgeScoreTile.ElapsedTicks(started);
                token.ThrowIfCancellationRequested();
                if (budgetStatus != 0)
                {
                    CudaNativeExecution.NativeTiming failedTiming = CudaNativeExecution.NativeTiming.Create();
                    failedTiming.DriverError = budgetError;
                    FinishBatch(budgetStatus, Array.Empty<double>(), false, failedTiming, 0, out ComputeExecutionEvidence failed);
                    accumulated = accumulated.Combine(failed);
                    evidence = Outcome(false);
                    return false;
                }
                budget = Math.Min(256UL * 1024 * 1024, budget);
                if (edgeTileBudgetBytes.HasValue) budget = Math.Min(budget, (ulong)edgeTileBudgetBytes.Value);
                long spanXLong = (long)maxDx - minDx, spanYLong = (long)maxDy - minDy;
                if (spanXLong > int.MaxValue || spanYLong > int.MaxValue)
                { evidence = Outcome(false, CudaFallbackReason.NumericalRangeUnsupported); return false; }
                int spanX = (int)spanXLong, spanY = (int)spanYLong;
                bool strided = execution.SupportsStridedEdgeTiles;
                // The existing grid transaction also owns the native workspace lifetime.
                bool RunTiles(out ComputeExecutionEvidence result)
                {
                    double[] packedX = null, packedY = null;
                    int retries = 0, completedTiles = 0, nextRow = 0, nextColumn = 0;
                    while (nextRow < rows)
                    {
                        token.ThrowIfCancellationRequested();
                        started = tile.StartTiming();
                        bool planned = TryPlanEdgeTile(columns, rows, step, spanX, spanY, points, budget, out int tileColumns, out int tileRows);
                        tile.PreparationTicks += EdgeScoreTile.ElapsedTicks(started);
                        if (!planned)
                        { result = Outcome(false, CudaFallbackReason.GpuMemoryUnavailable); return false; }
                        int c = Math.Min(tileColumns, columns - nextColumn), r = Math.Min(tileRows, rows - nextRow);
                        int packedWidth = checked((c - 1) * step + spanX + 1), packedHeight = checked((r - 1) * step + spanY + 1);
                        int packedCount = checked(packedWidth * packedHeight), count = checked(c * r);
                        started = tile.StartTiming();
                        try
                        {
                            if (!strided && (packedX == null || packedX.Length < packedCount)) { packedX = new double[packedCount]; packedY = new double[packedCount]; }
                            if (tile.Scores == null || tile.Scores.Length < count) tile.Scores = new double[count];
                        }
                        catch (OutOfMemoryException) { result = Outcome(false, CudaFallbackReason.GpuMemoryUnavailable); return false; }
                        finally { tile.AllocationTicks += EdgeScoreTile.ElapsedTicks(started); }
                        tile.MinX = checked(minX + nextColumn * step); tile.MinY = checked(minY + nextRow * step);
                        tile.Columns = c; tile.Rows = r;
                        int sourceX = checked(tile.MinX + minDx), sourceY = checked(tile.MinY + minDy);
                        started = tile.StartTiming();
                        if (!strided) for (int row = 0; row < packedHeight; row++)
                        {
                            token.ThrowIfCancellationRequested();
                            int from = checked((sourceY + row) * width + sourceX), to = row * packedWidth;
                            Array.Copy(sx, from, packedX, to, packedWidth); Array.Copy(sy, from, packedY, to, packedWidth);
                        }
                        if (!strided) tile.PackingTicks += EdgeScoreTile.ElapsedTicks(started);
                        started = tile.StartTiming();
                        CudaNativeExecution.NativeTiming timing;
                        int status = strided
                            ? execution.ScoreStridedTile(sx, sy, width, sourceX, sourceY, offsetX, offsetY, mx, my, thresholds,
                                packedWidth, packedHeight, -minDx, -minDy, c, r, step, polarity, tile.Scores, token, out timing)
                            : execution.ScoreTile(packedX, packedY, packedCount, offsetX, offsetY, mx, my, thresholds,
                                packedWidth, packedHeight, -minDx, -minDy, c, r, step, polarity, tile.Scores, token, out timing);
                        tile.NativeCallTicks += EdgeScoreTile.ElapsedTicks(started);
                        started = tile.StartTiming();
                        bool success = FinishBatch(status, tile.Scores, false, timing, 0, out ComputeExecutionEvidence batch, count, budget);
                        tile.ValidationTicks += EdgeScoreTile.ElapsedTicks(started);
                        accumulated = accumulated.Combine(batch);
                        token.ThrowIfCancellationRequested();
                        if (!success)
                        {
                            if (batch.FallbackReason == CudaFallbackReason.GpuMemoryUnavailable && completedTiles == 0 && retries < 2)
                            {
                                if (execution.ReleaseWorkspace(out int retryError) != 0)
                                {
                                    CudaNativeExecution.NativeTiming failed = CudaNativeExecution.NativeTiming.Create();
                                    failed.DriverError = retryError;
                                    FinishBatch(9, Array.Empty<double>(), false, failed, 0, out ComputeExecutionEvidence failure);
                                    accumulated = accumulated.Combine(failure);
                                    result = Outcome(false);
                                    return false;
                                }
                                budget /= 2; retries++; continue;
                            }
                            result = Outcome(false);
                            return false;
                        }
                        started = tile.StartTiming();
                        try { consume(); }
                        finally { tile.ConsumptionTicks += EdgeScoreTile.ElapsedTicks(started); }
                        token.ThrowIfCancellationRequested();
                        completedTiles++;
                        nextColumn += c;
                        if (nextColumn == columns) { nextColumn = 0; nextRow += r; }
                    }
                    result = Outcome(true, CudaFallbackReason.None);
                    return true;
                }

                int releaseStatus = 0, releaseError = 0;
                bool gridApplied;
                try { gridApplied = RunTiles(out evidence); }
                finally
                {
                    if (strided && execution != null)
                    {
                        started = tile.StartTiming();
                        releaseStatus = execution.ReleaseWorkspace(out releaseError);
                        tile.NativeCallTicks += EdgeScoreTile.ElapsedTicks(started);
                        if (releaseStatus != 0)
                        {
                            CudaNativeExecution.NativeTiming failed = CudaNativeExecution.NativeTiming.Create();
                            failed.DriverError = releaseError;
                            FinishBatch(releaseStatus, Array.Empty<double>(), false, failed, 0, out ComputeExecutionEvidence failure);
                            accumulated = accumulated.Combine(failure);
                        }
                    }
                }
                if (releaseStatus != 0) { evidence = Outcome(false); return false; }
                return gridApplied;
            }
            finally { Monitor.Exit(sync); }
        }

        internal bool TryTransformPoints(double[] points, double[] poses, CancellationToken token,
            out double[] transformed, out ComputeExecutionEvidence evidence)
        {
            transformed = null;
            evidence = null;
            Enter(token);
            try
            {
                ThrowIfDisposed();
                if (!PrepareKernel(token, out double initializationMs))
                { evidence = new ComputeExecutionEvidence(useCuda, support, reason: executionFailure, initializationMs: initializationMs, driverError: executionError); return false; }
                if (!TryAllocateResult((long)points.Length * (poses.Length / 12), initializationMs, out double[] values, out evidence))
                { token.ThrowIfCancellationRequested(); return false; }
                int status = execution.Transform(points, poses, values, token, out CudaNativeExecution.NativeTiming timing);
                bool success = FinishBatch(status, values, true, timing, initializationMs, out evidence);
                token.ThrowIfCancellationRequested();
                if (success) transformed = values;
                return success;
            }
            finally { Monitor.Exit(sync); }
        }

        private bool TryAllocateResult(long count, double initializationMs, out double[] values, out ComputeExecutionEvidence evidence)
        {
            values = null;
            evidence = null;
            if (count >= 0 && count <= MaximumResultBytes / sizeof(double))
            {
                try { values = new double[(int)count]; return true; }
                catch (OutOfMemoryException) { }
            }
            evidence = new ComputeExecutionEvidence(useCuda, support, reason: CudaFallbackReason.GpuMemoryUnavailable, initializationMs: initializationMs);
            return false;
        }

        private void Enter(CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            while (!Monitor.TryEnter(sync, 50)) token.ThrowIfCancellationRequested();
            if (token.IsCancellationRequested) { Monitor.Exit(sync); token.ThrowIfCancellationRequested(); }
        }

        private bool PrepareKernel(CancellationToken token, out double initializationMs)
        {
            initializationMs = 0;
            ProbeCuda(token);
            if (!useCuda || !support.SelectedDeviceOrdinal.HasValue || string.IsNullOrEmpty(support.ModulePath)) return false;
            if (execution != null) return true;
            if (executionFailure.HasValue) return false;
            Stopwatch timer = Stopwatch.StartNew();
            execution = CudaNativeExecution.Create(support.ModulePath, support.SelectedDeviceOrdinal.Value, token,
                out CudaFallbackReason reason, out executionError);
            initializationMs = timer.Elapsed.TotalMilliseconds;
            if (execution == null) { executionFailure = reason; return false; }
            support = new CudaSupportReport(CudaFallbackReason.None, support.ModulePath, support.DriverApiVersion,
                support.Devices, support.SelectedDeviceOrdinal, accelerationAvailable: true);
            return true;
        }

        private bool FinishBatch(int status, double[] values, bool transform, CudaNativeExecution.NativeTiming timing,
            double initializationMs, out ComputeExecutionEvidence evidence, int validCount = -1, ulong maximumScratchBytes = ulong.MaxValue)
        {
            CudaFallbackReason reason = status == 0 ? CudaFallbackReason.None
                : status == 8 ? CudaFallbackReason.GpuMemoryUnavailable : CudaFallbackReason.GpuExecutionFailed;
            if (status == 0 && (timing.Size != Marshal.SizeOf<CudaNativeExecution.NativeTiming>() || timing.Reserved != 0 || timing.Launches <= 0
                || timing.UploadMs < 0 || timing.KernelWaitMs < 0 || timing.DownloadMs < 0 || timing.ScratchBytes > maximumScratchBytes
                || double.IsNaN(timing.UploadMs + timing.KernelWaitMs + timing.DownloadMs)
                || double.IsInfinity(timing.UploadMs + timing.KernelWaitMs + timing.DownloadMs))) reason = CudaFallbackReason.InvalidKernelResult;
            if (reason == CudaFallbackReason.None)
            {
                int count = validCount < 0 ? values.Length : validCount;
                for (int i = 0; i < count; i++)
                    if (double.IsNaN(values[i]) || double.IsInfinity(values[i]))
                    { reason = transform ? CudaFallbackReason.NumericalRangeUnsupported : CudaFallbackReason.InvalidKernelResult; break; }
            }
            if (reason == CudaFallbackReason.GpuExecutionFailed || reason == CudaFallbackReason.InvalidKernelResult)
            {
                execution.Dispose();
                execution = null;
                executionFailure = reason;
                executionError = timing.DriverError;
                support = new CudaSupportReport(reason, support.ModulePath, support.DriverApiVersion,
                    support.Devices, support.SelectedDeviceOrdinal, timing.DriverError);
            }
            evidence = new ComputeExecutionEvidence(useCuda, support, reason == CudaFallbackReason.None, reason,
                initializationMs, timing.UploadMs, timing.KernelWaitMs, timing.DownloadMs, timing.ScratchBytes, timing.Launches, timing.DriverError);
            return reason == CudaFallbackReason.None;
        }

        public void Dispose()
        {
            lock (sync)
            {
                if (disposed) return;
                disposed = true;
                execution?.Dispose();
                execution = null;
            }
        }

        private void ThrowIfDisposed()
        {
            if (disposed) throw new ObjectDisposedException(nameof(VisionComputeSession));
        }
    }
}
