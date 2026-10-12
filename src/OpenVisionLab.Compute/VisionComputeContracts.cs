using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace OpenVisionLab.Compute
{
    /// <summary>Host-owned preferences. A session copies these values; it never persists or installs anything.</summary>
    public sealed class VisionComputeOptions
    {
        public bool UseCuda { get; set; }
        /// <summary>Null selects the first detected device. This does not distribute work across devices.</summary>
        public int? DeviceOrdinal { get; set; }
    }

    public enum CudaFallbackReason
    {
        Disabled,
        NotProbed,
        PlatformUnsupported,
        NativeModuleMissing,
        NativeModuleLoadFailed,
        NativeModuleInvalidImage,
        NativeAbiMismatch,
        NativeExportMissing,
        DriverMissing,
        DriverApiMissing,
        DriverInitializationFailed,
        NoCudaDevice,
        DeviceQueryFailed,
        RequestedDeviceUnavailable,
        InvalidNativeResult,
        KernelNotImplemented,
        None,
        WorkloadTooSmall,
        GpuMemoryUnavailable,
        GpuExecutionFailed,
        KernelInitializationFailed,
        InvalidKernelResult,
        NumericalRangeUnsupported,
        NoMeasuredBenefit
    }

    public enum CudaInstallationAction
    {
        None,
        InstallAccelerationModule,
        UseCompatibleAccelerationModule,
        InstallOrUpdateNvidiaDriver,
        SelectAvailableDeviceOrUseCpu,
        UseCpu,
        UpdateAccelerationModule
    }

    /// <summary>Immutable device discovery, not proof that an algorithm kernel has been qualified.</summary>
    public sealed class CudaDeviceInfo
    {
        internal CudaDeviceInfo(int ordinal, string name, int major, int minor, ulong totalMemoryBytes)
        {
            Ordinal = ordinal;
            Name = name;
            ComputeCapabilityMajor = major;
            ComputeCapabilityMinor = minor;
            TotalMemoryBytes = totalMemoryBytes;
        }

        public int Ordinal { get; }
        public string Name { get; }
        public int ComputeCapabilityMajor { get; }
        public int ComputeCapabilityMinor { get; }
        /// <summary>Total device memory, not free memory or an approved allocation budget.</summary>
        public ulong TotalMemoryBytes { get; }
    }

    /// <summary>Immutable installation/discovery report. DriverApiVersion is CUDA's API version, not the display-driver release number.</summary>
    public sealed class CudaSupportReport
    {
        internal CudaSupportReport(CudaFallbackReason reason, string modulePath = null, int? driverApiVersion = null,
            IEnumerable<CudaDeviceInfo> devices = null, int? selectedDeviceOrdinal = null, int nativeErrorCode = 0, bool accelerationAvailable = false)
        {
            Reason = reason;
            ModulePath = modulePath;
            DriverApiVersion = driverApiVersion;
            Devices = new ReadOnlyCollection<CudaDeviceInfo>(devices == null ? new List<CudaDeviceInfo>() : new List<CudaDeviceInfo>(devices));
            SelectedDeviceOrdinal = selectedDeviceOrdinal;
            NativeErrorCode = nativeErrorCode;
            IsAccelerationAvailable = accelerationAvailable;
        }

        /// <summary>Current discovery or initialization condition. None alone does not establish kernel availability or application.</summary>
        public CudaFallbackReason Reason { get; }
        public string ModulePath { get; }
        public int? DriverApiVersion { get; }
        public IReadOnlyList<CudaDeviceInfo> Devices { get; }
        public int? SelectedDeviceOrdinal { get; }
        public int NativeErrorCode { get; }
        public bool DeviceDetected => Devices.Count > 0;
        /// <summary>True only after execution initialization and the numerical capability check pass.</summary>
        public bool IsAccelerationAvailable { get; }
        public CudaInstallationAction InstallationAction
        {
            get
            {
                switch (Reason)
                {
                    case CudaFallbackReason.Disabled:
                    case CudaFallbackReason.NotProbed: return CudaInstallationAction.None;
                    case CudaFallbackReason.None: return CudaInstallationAction.None;
                    case CudaFallbackReason.NativeModuleMissing: return CudaInstallationAction.InstallAccelerationModule;
                    case CudaFallbackReason.NativeAbiMismatch:
                    case CudaFallbackReason.NativeModuleInvalidImage:
                    case CudaFallbackReason.NativeExportMissing:
                    case CudaFallbackReason.NativeModuleLoadFailed:
                    case CudaFallbackReason.InvalidNativeResult: return CudaInstallationAction.UseCompatibleAccelerationModule;
                    case CudaFallbackReason.DriverMissing:
                    case CudaFallbackReason.DriverApiMissing:
                    case CudaFallbackReason.DriverInitializationFailed: return CudaInstallationAction.InstallOrUpdateNvidiaDriver;
                    case CudaFallbackReason.RequestedDeviceUnavailable:
                    case CudaFallbackReason.NoCudaDevice: return CudaInstallationAction.SelectAvailableDeviceOrUseCpu;
                    case CudaFallbackReason.KernelNotImplemented: return CudaInstallationAction.UpdateAccelerationModule;
                    default: return CudaInstallationAction.UseCpu;
                }
            }
        }
    }

    /// <summary>Per-execution snapshot; failed, cancelled or unimplemented GPU work is never reported as applied acceleration.</summary>
    public sealed class ComputeExecutionEvidence
    {
        internal ComputeExecutionEvidence(bool requestedCuda, CudaSupportReport support, bool applied = false,
            CudaFallbackReason? reason = null, double initializationMs = 0, double uploadMs = 0,
            double kernelWaitMs = 0, double downloadMs = 0, ulong scratchBytes = 0, int batches = 0, int driverError = 0)
        {
            RequestedCuda = requestedCuda;
            Support = support;
            AccelerationApplied = applied;
            FallbackReason = reason ?? support.Reason;
            InitializationMilliseconds = initializationMs;
            UploadMilliseconds = uploadMs;
            KernelWaitMilliseconds = kernelWaitMs;
            DownloadMilliseconds = downloadMs;
            PeakScratchBytes = scratchBytes;
            GpuBatchCount = batches;
            NativeExecutionErrorCode = driverError;
        }

        public bool RequestedCuda { get; }
        public bool AccelerationApplied { get; }
        public CudaSupportReport Support { get; }
        public CudaFallbackReason FallbackReason { get; }
        public double InitializationMilliseconds { get; }
        public double UploadMilliseconds { get; }
        /// <summary>Host elapsed launch plus synchronization time; not a device event timer.</summary>
        public double KernelWaitMilliseconds { get; }
        public double DownloadMilliseconds { get; }
        public ulong PeakScratchBytes { get; }
        public int GpuBatchCount { get; }
        public int NativeExecutionErrorCode { get; }

        internal ComputeExecutionEvidence Combine(ComputeExecutionEvidence next)
        {
            return new ComputeExecutionEvidence(RequestedCuda, next.Support, AccelerationApplied || next.AccelerationApplied,
                next.FallbackReason, InitializationMilliseconds + next.InitializationMilliseconds,
                UploadMilliseconds + next.UploadMilliseconds, KernelWaitMilliseconds + next.KernelWaitMilliseconds,
                DownloadMilliseconds + next.DownloadMilliseconds, Math.Max(PeakScratchBytes, next.PeakScratchBytes),
                GpuBatchCount + next.GpuBatchCount, next.NativeExecutionErrorCode);
        }
    }
}
