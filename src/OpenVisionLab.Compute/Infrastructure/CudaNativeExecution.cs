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
        private Median3D median;
        private Normals3D normals;
        private Scharr8U scharr;
        private bool medianQualified, localMedianQualified, normalsQualified, scharrQualified;

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
                value.median = CudaNativeProbe.GetExport<Median3D>(value.module, "ovl_cuda_median3d");
                value.normals = CudaNativeProbe.GetExport<Normals3D>(value.module, "ovl_cuda_normals3d");
                value.scharr = CudaNativeProbe.GetExport<Scharr8U>(value.module, "ovl_cuda_scharr8u");
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


        internal bool SupportsMedian => median != null;
        internal bool SupportsNormals => normals != null;
        internal bool SupportsScharr => scharr != null;

        internal int Median(double[] source, int width, int rows, int kernel, bool local, int minimum, double limit,
            double[] result, CancellationToken token, out NativeTiming timing)
        {
            timing = NativeTiming.Create();
            Cancel cancel = () => token.IsCancellationRequested ? 1 : 0;
            int status = median(handle, source, width, rows, kernel, local ? 1 : 0, minimum, limit, result, cancel, ref timing);
            GC.KeepAlive(cancel);
            token.ThrowIfCancellationRequested();
            return status;
        }

        internal int Normals(double[] source, int width, int rows, double columnPitch, double rowPitch,
            double[] result, CancellationToken token, out NativeTiming timing)
        {
            timing = NativeTiming.Create();
            Cancel cancel = () => token.IsCancellationRequested ? 1 : 0;
            int status = normals(handle, source, width, rows, columnPitch, rowPitch, result, cancel, ref timing);
            GC.KeepAlive(cancel);
            token.ThrowIfCancellationRequested();
            return status;
        }

        internal int Scharr(byte[] source, int width, int rows, int channels, int axis,
            byte[] result, CancellationToken token, out NativeTiming timing)
        {
            timing = NativeTiming.Create();
            Cancel cancel = () => token.IsCancellationRequested ? 1 : 0;
            int status = scharr(handle, source, width, rows, channels, axis, result, cancel, ref timing);
            GC.KeepAlive(cancel);
            token.ThrowIfCancellationRequested();
            return status;
        }

        internal int CheckMedian(bool local, CancellationToken token, out NativeTiming timing)
        {
            timing = NativeTiming.Create();
            if (local ? localMedianQualified : medianQualified) return 0;
            double[] source = { 1, 1, 1, 1, 99, 1, 1, 1, 1 }, result = new double[9];
            int status = Median(source, 3, 3, 3, local, 3, 5, result, token, out timing);
            if (status != 0) return status;
            if (!timing.IsValid()) return 13;
            for (int i = 0; i < result.Length; i++)
                if (local && i == 4 ? !double.IsNaN(result[i]) : result[i] != 1.0) return 13;
            if (local) localMedianQualified = true; else medianQualified = true;
            return 0;
        }

        internal int CheckNormals(CancellationToken token, out NativeTiming timing)
        {
            timing = NativeTiming.Create();
            if (normalsQualified) return 0;
            double[] result = new double[54];
            int status = Normals(new double[9], 3, 3, 2, 4, result, token, out timing);
            if (status != 0) return status;
            if (!timing.IsValid()) return 13;
            for (int i = 0; i < 9; i++)
            {
                int flags = 19 | (i % 3 == 1 ? 4 : 0) | (i / 3 == 1 ? 8 : 0);
                if (result[i * 6] != 0 || result[i * 6 + 1] != 0 || result[i * 6 + 2] != 0
                    || result[i * 6 + 3] != 1 || result[i * 6 + 4] != 0 || result[i * 6 + 5] != flags) return 13;
            }
            normalsQualified = true;
            return 0;
        }

        internal int CheckScharr(CancellationToken token, out NativeTiming timing)
        {
            timing = NativeTiming.Create();
            if (scharrQualified) return 0;
            byte[] source = { 0, 1, 2, 0, 1, 2, 0, 1, 2 }, result = new byte[9];
            for (int axis = 0; axis < 2; axis++)
            {
                int status = Scharr(source, 3, 3, 1, axis, result, token, out timing);
                if (status != 0) return status;
                if (!timing.IsValid()) return 13;
                for (int i = 0; i < result.Length; i++) if (result[i] != (axis == 0 && i % 3 == 1 ? 32 : 0)) return 13;
            }
            scharrQualified = true;
            return 0;
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
            internal bool IsValid(ulong maximumScratchBytes = ulong.MaxValue) => Size == Marshal.SizeOf<NativeTiming>() && Reserved == 0 && Launches > 0
                && UploadMs >= 0 && KernelWaitMs >= 0 && DownloadMs >= 0 && ScratchBytes <= maximumScratchBytes
                && !double.IsNaN(UploadMs + KernelWaitMs + DownloadMs) && !double.IsInfinity(UploadMs + KernelWaitMs + DownloadMs);
            internal static NativeTiming Create() => new NativeTiming { Size = (uint)Marshal.SizeOf<NativeTiming>() };
        }
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Median3D(IntPtr handle, [In] double[] source,
            int width, int rows, int kernel, int local, int minimum, double limit, [Out] double[] result, Cancel cancel, ref NativeTiming timing);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Normals3D(IntPtr handle, [In] double[] source,
            int width, int rows, double columnPitch, double rowPitch, [Out] double[] result, Cancel cancel, ref NativeTiming timing);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int Scharr8U(IntPtr handle, [In] byte[] source,
            int width, int rows, int channels, int axis, [Out] byte[] result, Cancel cancel, ref NativeTiming timing);
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
