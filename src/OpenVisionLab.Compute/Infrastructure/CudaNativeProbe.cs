using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace OpenVisionLab.Compute.Infrastructure
{
    internal static class CudaNativeProbe
    {
        internal const string ModuleFileName = "OpenVisionLab.Cuda.Native.dll";
        private const uint AbiVersion = 1;
        internal const uint SearchDllDirectoryAndSystem32 = 0x00000100 | 0x00000800;
        private const uint SuppressLoaderDialogs = 0x00000001 | 0x00008000;

        internal static CudaSupportReport Probe(CancellationToken cancellationToken)
        {
            string location = typeof(CudaNativeProbe).Assembly.Location;
            string directory = string.IsNullOrEmpty(location) ? AppContext.BaseDirectory : Path.GetDirectoryName(location);
            string adjacent = Path.Combine(directory, ModuleFileName);
            string runtime = Path.Combine(directory, "runtimes", "win-x64", "native", ModuleFileName);
            return Probe(File.Exists(adjacent) ? adjacent : runtime, cancellationToken);
        }

        internal static CudaSupportReport Probe(string modulePath, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows) || RuntimeInformation.ProcessArchitecture != Architecture.X64)
                return new CudaSupportReport(CudaFallbackReason.PlatformUnsupported);
            string fullPath = Path.GetFullPath(modulePath);
            if (!File.Exists(fullPath)) return new CudaSupportReport(CudaFallbackReason.NativeModuleMissing, fullPath);
            uint previousMode;
            try
            {
                if (!SetThreadErrorMode(GetThreadErrorMode() | SuppressLoaderDialogs, out previousMode))
                    return new CudaSupportReport(CudaFallbackReason.NativeModuleLoadFailed, fullPath, nativeErrorCode: Marshal.GetLastWin32Error());
            }
            catch (EntryPointNotFoundException) { return new CudaSupportReport(CudaFallbackReason.PlatformUnsupported, fullPath); }

            IntPtr module = IntPtr.Zero;
            IntPtr handle = IntPtr.Zero;
            DestroyProbe destroy = null;
            try
            {
                module = LoadLibraryExW(fullPath, IntPtr.Zero, SearchDllDirectoryAndSystem32);
                if (module == IntPtr.Zero)
                {
                    int error = Marshal.GetLastWin32Error();
                    return new CudaSupportReport(error == 193 ? CudaFallbackReason.NativeModuleInvalidImage : CudaFallbackReason.NativeModuleLoadFailed,
                        fullPath, nativeErrorCode: error);
                }
                GetAbi getAbi = GetExport<GetAbi>(module, "ovl_cuda_get_abi_version");
                if (getAbi == null) return new CudaSupportReport(CudaFallbackReason.NativeExportMissing, fullPath);
                if (getAbi() != AbiVersion) return new CudaSupportReport(CudaFallbackReason.NativeAbiMismatch, fullPath);
                CreateProbe create = GetExport<CreateProbe>(module, "ovl_cuda_create_probe");
                QueryDevice query = GetExport<QueryDevice>(module, "ovl_cuda_query_device");
                destroy = GetExport<DestroyProbe>(module, "ovl_cuda_destroy_probe");
                if (create == null || query == null || destroy == null) return new CudaSupportReport(CudaFallbackReason.NativeExportMissing, fullPath);
                cancellationToken.ThrowIfCancellationRequested();
                int status = create(out handle, out int driverVersion, out int count);
                if (status != 0) return FromNativeFailure(status, fullPath);
                if (handle == IntPtr.Zero || count < 0 || count > 1024 || driverVersion <= 0)
                    return new CudaSupportReport(CudaFallbackReason.InvalidNativeResult, fullPath);
                if (count == 0) return new CudaSupportReport(CudaFallbackReason.NoCudaDevice, fullPath, driverVersion);
                List<CudaDeviceInfo> devices = new List<CudaDeviceInfo>(count);
                for (int ordinal = 0; ordinal < count; ordinal++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    NativeDevice info = new NativeDevice { Size = (uint)Marshal.SizeOf<NativeDevice>(), Name = new byte[128] };
                    status = query(handle, ordinal, ref info);
                    if (status != 0) return FromNativeFailure(status, fullPath);
                    int terminator = Array.IndexOf(info.Name, (byte)0);
                    if (info.Size != Marshal.SizeOf<NativeDevice>() || info.Ordinal != ordinal || info.Major <= 0 || info.Minor < 0
                        || info.TotalMemoryBytes == 0 || terminator <= 0)
                        return new CudaSupportReport(CudaFallbackReason.InvalidNativeResult, fullPath);
                    devices.Add(new CudaDeviceInfo(ordinal, Encoding.UTF8.GetString(info.Name, 0, terminator), info.Major, info.Minor, info.TotalMemoryBytes));
                }
                cancellationToken.ThrowIfCancellationRequested();
                return new CudaSupportReport(CudaFallbackReason.None, fullPath, driverVersion, devices);
            }
            finally
            {
                try { if (handle != IntPtr.Zero && destroy != null) destroy(handle); }
                finally
                {
                    if (module != IntPtr.Zero) _ = FreeLibrary(module);
                    _ = SetThreadErrorMode(previousMode, out uint ignored);
                }
            }
        }

        private static CudaSupportReport FromNativeFailure(int status, string path)
        {
            CudaFallbackReason reason;
            switch (status)
            {
                case 1: reason = CudaFallbackReason.DriverMissing; break;
                case 2: reason = CudaFallbackReason.DriverApiMissing; break;
                case 3: reason = CudaFallbackReason.DriverInitializationFailed; break;
                case 4: reason = CudaFallbackReason.DeviceQueryFailed; break;
                case 7: reason = CudaFallbackReason.NoCudaDevice; break;
                default: reason = CudaFallbackReason.InvalidNativeResult; break;
            }
            return new CudaSupportReport(reason, path, nativeErrorCode: status);
        }

        internal static T GetExport<T>(IntPtr module, string name) where T : class
        {
            IntPtr address = GetProcAddress(module, name);
            return address == IntPtr.Zero ? null : Marshal.GetDelegateForFunctionPointer(address, typeof(T)) as T;
        }

        [StructLayout(LayoutKind.Sequential, Pack = 8)]
        private struct NativeDevice
        {
            public uint Size;
            public int Ordinal;
            public int Major;
            public int Minor;
            public ulong TotalMemoryBytes;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 128)] public byte[] Name;
        }

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate uint GetAbi();
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int CreateProbe(out IntPtr handle, out int driverVersion, out int count);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate int QueryDevice(IntPtr handle, int ordinal, ref NativeDevice device);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] private delegate void DestroyProbe(IntPtr handle);

        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)] internal static extern IntPtr LoadLibraryExW(string path, IntPtr reserved, uint flags);
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [DllImport("kernel32.dll", CharSet = CharSet.Ansi, ExactSpelling = true, BestFitMapping = false, ThrowOnUnmappableChar = true)] private static extern IntPtr GetProcAddress(IntPtr module, string name);
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [DllImport("kernel32.dll", ExactSpelling = true)] [return: MarshalAs(UnmanagedType.Bool)] internal static extern bool FreeLibrary(IntPtr module);
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [DllImport("kernel32.dll", ExactSpelling = true)] private static extern uint GetThreadErrorMode();
        [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
        [DllImport("kernel32.dll", ExactSpelling = true, SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool SetThreadErrorMode(uint mode, out uint previous);
    }
}
