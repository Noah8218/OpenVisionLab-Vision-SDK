using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace OpenVisionLab.Compute.Infrastructure
{
    /// <summary>Session-owned execution handle. The session serializes every call and disposal.</summary>
    internal sealed class CudaNativeExecution : IDisposable
    {
        private IntPtr module;
        private IntPtr handle;
        private Destroy destroy;
        private EdgeGrid edge;
        private EdgeTile edgeTile;
        private EdgeTileBudget tileBudget;
        private EdgeTileStrided edgeTileStrided;
        private ReleaseEdgeWorkspace releaseEdgeWorkspace;
        private TransformPoints transform;

        private CudaNativeExecution() { }

        internal static CudaNativeExecution Create(string path, int ordinal, CancellationToken token, out CudaFallbackReason reason, out int error)
        {
            token.ThrowIfCancellationRequested();
            reason = CudaFallbackReason.KernelNotImplemented;
            error = 0;
            CudaNativeExecution value = new CudaNativeExecution();
            try
            {
                value.module = CudaNativeProbe.LoadLibraryExW(path, IntPtr.Zero, CudaNativeProbe.SearchDllDirectoryAndSystem32);
                if (value.module == IntPtr.Zero) { reason = CudaFallbackReason.NativeModuleLoadFailed; error = Marshal.GetLastWin32Error(); return null; }
                GetAbi abi = CudaNativeProbe.GetExport<GetAbi>(value.module, "ovl_cuda_get_execution_abi_version");
                if (abi == null) return null;
                if (abi() != 1) { reason = CudaFallbackReason.NativeAbiMismatch; return null; }
                CreateExecution create = CudaNativeProbe.GetExport<CreateExecution>(value.module, "ovl_cuda_create_execution");
                value.destroy = CudaNativeProbe.GetExport<Destroy>(value.module, "ovl_cuda_destroy_execution");
                value.edge = CudaNativeProbe.GetExport<EdgeGrid>(value.module, "ovl_cuda_edge_grid");
                value.transform = CudaNativeProbe.GetExport<TransformPoints>(value.module, "ovl_cuda_transform_points");
                if (create == null || value.destroy == null || value.edge == null || value.transform == null)
                { reason = CudaFallbackReason.NativeExportMissing; return null; }
                value.edgeTile = CudaNativeProbe.GetExport<EdgeTile>(value.module, "ovl_cuda_edge_tile");
                value.tileBudget = CudaNativeProbe.GetExport<EdgeTileBudget>(value.module, "ovl_cuda_get_edge_tile_budget");
                value.edgeTileStrided = CudaNativeProbe.GetExport<EdgeTileStrided>(value.module, "ovl_cuda_edge_tile_strided");
                value.releaseEdgeWorkspace = CudaNativeProbe.GetExport<ReleaseEdgeWorkspace>(value.module, "ovl_cuda_release_edge_workspace");
                int status = create(ordinal, out value.handle, out error);
                token.ThrowIfCancellationRequested();
                if (status != 0 || value.handle == IntPtr.Zero)
                { reason = status == 8 ? CudaFallbackReason.GpuMemoryUnavailable : CudaFallbackReason.KernelInitializationFailed; return null; }
                if (!value.CheckCapability(token) || (value.SupportsEdgeTiles && !value.CheckTileCapability(token))) { reason = CudaFallbackReason.InvalidKernelResult; return null; }
                reason = CudaFallbackReason.None;
                return value;
            }
            finally { if (reason != CudaFallbackReason.None || token.IsCancellationRequested) value.Dispose(); }
        }

        private bool CheckCapability(CancellationToken token)
        {
            double[] source = { 1, -1, 0, 0.5 };
            double[] scores = new double[4];
            NativeTiming timing = NativeTiming.Create();
            Cancel cancel = () => token.IsCancellationRequested ? 1 : 0;
            int[] offsets = { 0 };
            double[] modelX = { 1.0 }, modelY = { 0.0 }, thresholds = { -2.0 };
            int status = edge(handle, source, new double[4], 4, offsets, modelX, modelY, thresholds,
                1, 4, 0, 0, 4, 1, 1, 0, scores, cancel, ref timing);
            GC.KeepAlive(cancel);
            token.ThrowIfCancellationRequested();
            if (status != 0) return false;
            for (int i = 0; i < 4; i++) if (BitConverter.DoubleToInt64Bits(source[i]) != BitConverter.DoubleToInt64Bits(scores[i])) return false;
            double[] points = { 1, 2, 3, -1, 0, 4 };
            double[] pose = { 0, -1, 0, 1, 0, 0, 0, 0, 1, 4, 5, 6 };
            double[] result = new double[6];
            timing = NativeTiming.Create();
            status = transform(handle, points, 2, pose, 1, result, cancel, ref timing);
            GC.KeepAlive(cancel);
            token.ThrowIfCancellationRequested();
            double[] expected = { 2, 6, 9, 4, 4, 10 };
            if (status != 0) return false;
            for (int i = 0; i < 6; i++) if (result[i] != expected[i]) return false;
            return true;
        }


        internal bool SupportsEdgeTiles => edgeTile != null && tileBudget != null;
        internal bool SupportsStridedEdgeTiles => SupportsEdgeTiles && edgeTileStrided != null && releaseEdgeWorkspace != null;

        private bool CheckTileCapability(CancellationToken token)
        {
            double[] source = { 1, -1, 0, 0.5 }, result = new double[4];
            int[] dx = { 1 }, dy = { 0 };
            double[] modelX = { 1.0 }, modelY = { 0.0 }, thresholds = { -2.0 };
            int status;
            NativeTiming timing;
            if (SupportsStridedEdgeTiles)
            {
                bool released = false;
                try
                {
                    status = ScoreStridedTile(new[] { 99.0, 1, -1, 0, 0.5, 99 }, new double[6], 6, 1, 0,
                        dx, dy, modelX, modelY, thresholds, 4, 1, -1, 0, 4, 1, 1, false, result, token, out timing);
                }
                finally { released = ReleaseWorkspace(out _) == 0; }
                if (!released) return false;
            }
            else status = ScoreTile(source, new double[4], 4, dx, dy, modelX, modelY, thresholds,
                4, 1, -1, 0, 4, 1, 1, false, result, token, out timing);
            if (status != 0 || timing.Size != Marshal.SizeOf<NativeTiming>() || timing.Reserved != 0 || timing.Launches <= 0) return false;
            for (int i = 0; i < 4; i++) if (BitConverter.DoubleToInt64Bits(source[i]) != BitConverter.DoubleToInt64Bits(result[i])) return false;
            return true;
        }

        internal int GetEdgeTileBudget(out ulong bytes, out int error) => tileBudget(handle, out bytes, out error);

        internal int ReleaseWorkspace(out int error)
        {
            error = 0;
            return SupportsStridedEdgeTiles && handle != IntPtr.Zero ? releaseEdgeWorkspace(handle, out error) : 0;
        }

        internal int ScoreStridedTile(double[] sx, double[] sy, int sourceWidth, int sourceLeft, int sourceTop,
            int[] dx, int[] dy, double[] mx, double[] my, double[] thresholds, int width, int height, int minX, int minY,
            int columns, int rows, int step, bool polarity, double[] result, CancellationToken token, out NativeTiming timing)
        {
            timing = NativeTiming.Create();
            Cancel cancel = () => token.IsCancellationRequested ? 1 : 0;
            int status = edgeTileStrided(handle, sx, sy, sx.Length, sourceWidth, sourceWidth > 0 ? sx.Length / sourceWidth : 0, sourceLeft, sourceTop,
                dx, dy, mx, my, thresholds, dx.Length, width, height, minX, minY, columns, rows, step,
                polarity ? 1 : 0, result, result.Length, cancel, ref timing);
            GC.KeepAlive(cancel);
            token.ThrowIfCancellationRequested();
            return status;
        }

        internal int ScoreTile(double[] sx, double[] sy, int sourceCount, int[] dx, int[] dy,
            double[] mx, double[] my, double[] thresholds, int width, int height, int minX, int minY,
            int columns, int rows, int step, bool polarity, double[] result, CancellationToken token, out NativeTiming timing)
        {
            timing = NativeTiming.Create();
            Cancel cancel = () => token.IsCancellationRequested ? 1 : 0;
            int status = edgeTile(handle, sx, sy, sourceCount, dx, dy, mx, my, thresholds, dx.Length,
                width, height, minX, minY, columns, rows, step, polarity ? 1 : 0, result, result.Length, cancel, ref timing);
            GC.KeepAlive(cancel);
            token.ThrowIfCancellationRequested();
            return status;
        }

        internal int Score(double[] sx, double[] sy, int[] offsets, double[] mx, double[] my, double[] thresholds,
            int width, int minX, int minY, int columns, int rows, int step, bool polarity,
            double[] result, CancellationToken token, out NativeTiming timing)
        {
            timing = NativeTiming.Create();
            Cancel cancel = () => token.IsCancellationRequested ? 1 : 0;
            int status = edge(handle, sx, sy, sx.Length, offsets, mx, my, thresholds, offsets.Length,
                width, minX, minY, columns, rows, step, polarity ? 1 : 0, result, cancel, ref timing);
            GC.KeepAlive(cancel);
            token.ThrowIfCancellationRequested();
            return status;
        }

        internal int Transform(double[] points, double[] poses, double[] result, CancellationToken token, out NativeTiming timing)
        {
            timing = NativeTiming.Create();
            Cancel cancel = () => token.IsCancellationRequested ? 1 : 0;
            int status = transform(handle, points, points.Length / 3, poses, poses.Length / 12, result, cancel, ref timing);
            GC.KeepAlive(cancel);
            token.ThrowIfCancellationRequested();
            return status;
        }

        public void Dispose()
        {
            try { if (handle != IntPtr.Zero && destroy != null) destroy(handle); }
            finally
            {
                handle = IntPtr.Zero;
                if (module != IntPtr.Zero) { _ = CudaNativeProbe.FreeLibrary(module); module = IntPtr.Zero; }
            }
        }

        [StructLayout(LayoutKind.Sequential, Pack = 8)]
        internal struct NativeTiming
        {
            public uint Size;
            public uint Reserved;
            public double UploadMs;
            public double KernelWaitMs;
            public double DownloadMs;
            public ulong ScratchBytes;
            public int DriverError;
            public int Launches;
            internal static NativeTiming Create() => new NativeTiming { Size = (uint)Marshal.SizeOf<NativeTiming>() };
        }
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint GetAbi();
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int CreateExecution(int ordinal, out IntPtr handle, out int error);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void Destroy(IntPtr handle);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Cancel();
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int EdgeGrid(IntPtr handle, [In] double[] sx, [In] double[] sy, int count,
            [In] int[] offsets, [In] double[] mx, [In] double[] my, [In] double[] thresholds, int points,
            int width, int minX, int minY, int columns, int rows, int step, int polarity, [Out] double[] result, Cancel cancel, ref NativeTiming timing);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int EdgeTileBudget(IntPtr handle, out ulong bytes, out int error);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int ReleaseEdgeWorkspace(IntPtr handle, out int error);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int EdgeTileStrided(IntPtr handle, [In] double[] sx, [In] double[] sy, int count,
            int sourceWidth, int sourceHeight, int sourceLeft, int sourceTop, [In] int[] dx, [In] int[] dy,
            [In] double[] mx, [In] double[] my, [In] double[] thresholds, int points,
            int width, int height, int minX, int minY, int columns, int rows, int step, int polarity,
            [Out] double[] result, int capacity, Cancel cancel, ref NativeTiming timing);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int EdgeTile(IntPtr handle, [In] double[] sx, [In] double[] sy, int count,
            [In] int[] dx, [In] int[] dy, [In] double[] mx, [In] double[] my, [In] double[] thresholds, int points,
            int width, int height, int minX, int minY, int columns, int rows, int step, int polarity,
            [Out] double[] result, int capacity, Cancel cancel, ref NativeTiming timing);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int TransformPoints(IntPtr handle, [In] double[] points, int count,
            [In] double[] poses, int poseCount, [Out] double[] result, Cancel cancel, ref NativeTiming timing);
    }
}
