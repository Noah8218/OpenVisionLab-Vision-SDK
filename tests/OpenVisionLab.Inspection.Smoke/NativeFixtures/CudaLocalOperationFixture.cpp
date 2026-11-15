// Test-only real-driver adapter. Production kernels/bridge remain authoritative.
// Calls are serialized by CudaLocalOperationSmokeSuite; no production build includes this file.
#define ovl_cuda_create_execution fixture_create
#define ovl_cuda_destroy_execution fixture_destroy
#define ovl_cuda_median3d fixture_median
#define ovl_cuda_normals3d fixture_normals
#define ovl_cuda_scharr8u fixture_scharr
#ifdef OVL_KERNEL_EXECUTION_SOURCE
#include OVL_KERNEL_EXECUTION_SOURCE
#else
#include "../../../native/OpenVisionLab.Cuda/KernelExecution.cpp"
#endif
#undef ovl_cuda_create_execution
#undef ovl_cuda_destroy_execution
#undef ovl_cuda_median3d
#undef ovl_cuda_normals3d
#undef ovl_cuda_scharr8u
#include <limits>

static decltype(Execution::memoryInfo) realInfo;
static decltype(Execution::allocate) realAllocate;
static decltype(Execution::release) realRelease;
static decltype(Execution::download) realDownload;
static decltype(Execution::launch) realLaunch;
static int liveBuffers = 0, liveExecutions = 0, downloads = 0, faultMode = 0, budgetMode = 0;
static uint64_t reportedFree = UINT64_MAX;
static void(__cdecl* afterDownload)() = nullptr;
static void(__cdecl* afterLaunch)() = nullptr;
static bool largeTileCall = false;
static int WINAPI Info(size_t* available, size_t* total)
{
    int status = realInfo(available, total);
    if (!status && budgetMode == 1) return 719;
    if (!status && reportedFree < *available) *available = static_cast<size_t>(reportedFree);
    return status;
}
static int WINAPI Allocate(DevicePointer* pointer, size_t bytes)
{
    if (budgetMode == 2) return 2;
    int status = realAllocate(pointer, bytes);
    if (!status) ++liveBuffers;
    return status;
}
static int WINAPI Release(DevicePointer pointer)
{
    int status = realRelease(pointer);
    if (!status) --liveBuffers;
    return status;
}
static int WINAPI Download(void* target, DevicePointer source, size_t bytes)
{
    int status = realDownload(target, source, bytes);
    if (!status && (OVL_LOCAL_FIXTURE_KIND != 2 || largeTileCall))
    {
        ++downloads;
        if (afterDownload) afterDownload();
        if (budgetMode == 3 || (OVL_LOCAL_FIXTURE_KIND == 2 && faultMode == 9)) return 719;
    }
    return status;
}
static int WINAPI Launch(void* function, unsigned gx, unsigned gy, unsigned gz, unsigned bx, unsigned by, unsigned bz,
    unsigned shared, void* stream, void** arguments, void** extra)
{
    int status = realLaunch(function, gx, gy, gz, bx, by, bz, shared, stream, arguments, extra);
    if (!status && afterLaunch)
    {
        auto callback = afterLaunch; afterLaunch = nullptr;
        callback();
    }
    return status;
}
extern "C" __declspec(dllexport) int __cdecl ovl_cuda_create_execution(int ordinal, void** handle, int* error)
{
    int status = fixture_create(ordinal, handle, error);
    if (status) return status;
    Execution* value = static_cast<Execution*>(*handle);
    realInfo = value->memoryInfo; realAllocate = value->allocate; realRelease = value->release; realDownload = value->download; realLaunch = value->launch;
    value->memoryInfo = Info; value->allocate = Allocate; value->release = Release; value->download = Download; value->launch = Launch;
    ++liveExecutions;
    return 0;
}
extern "C" __declspec(dllexport) void __cdecl ovl_cuda_destroy_execution(void* handle)
{
    fixture_destroy(handle);
    if (handle) --liveExecutions;
}
extern "C" __declspec(dllexport) int __cdecl ovl_test_live_buffers() { return liveBuffers; }
extern "C" __declspec(dllexport) int __cdecl ovl_test_buffers() { return liveBuffers; }
extern "C" __declspec(dllexport) int __cdecl ovl_test_live_executions() { return liveExecutions; }
extern "C" __declspec(dllexport) int __cdecl ovl_test_downloads() { return downloads; }
extern "C" __declspec(dllexport) int __cdecl ovl_test_completed_tiles() { return downloads; }
extern "C" __declspec(dllexport) void __cdecl ovl_test_budget(uint64_t bytes, int mode) { reportedFree = bytes; budgetMode = mode; downloads = 0; }
extern "C" __declspec(dllexport) void __cdecl ovl_test_set_fault(int mode) { faultMode = mode; downloads = 0; }
extern "C" __declspec(dllexport) void __cdecl ovl_test_tile_fault(int mode) { faultMode = mode; downloads = 0; }
extern "C" __declspec(dllexport) void __cdecl ovl_test_callback(void(__cdecl* callback)()) { afterDownload = callback; if (callback) downloads = 0; }
extern "C" __declspec(dllexport) void __cdecl ovl_test_tile_callback(void(__cdecl* callback)()) { afterDownload = callback; if (callback) downloads = 0; }
extern "C" __declspec(dllexport) void __cdecl ovl_test_after_launch(void(__cdecl* callback)()) { afterLaunch = callback; }

#if OVL_LOCAL_FIXTURE_KIND != 4
static int Finish(int status, Timing* timing, double* output)
{
    if (OVL_LOCAL_FIXTURE_KIND != 3 || status) return status;
    int mode = faultMode; faultMode = 0;
    // Synthetic status injection after a real completed primitive, not a real driver failure.
    if (mode == 8 || mode == 9) return Fail(timing, mode == 9 ? 719 : 2, mode);
    if (mode == 20) timing->kernelWaitMs = std::numeric_limits<double>::quiet_NaN();
    if (mode == 21 && output) output[0] = std::numeric_limits<double>::infinity();
    return status;
}
extern "C" __declspec(dllexport) int __cdecl ovl_cuda_median3d(void* handle, const double* source, int width, int rows,
    int kernel, int local, int minimum, double limit, double* output, Cancel cancel, Timing* timing)
{
    largeTileCall = static_cast<int64_t>(width) * rows > 1024 * 1024;
    if (OVL_LOCAL_FIXTURE_KIND == 1 || (OVL_LOCAL_FIXTURE_KIND == 2 && largeTileCall)) downloads = 0;
    int status = fixture_median(handle, source, width, rows, kernel, local, minimum, limit, output, cancel, timing);
    largeTileCall = false;
    return Finish(status, timing, output);
}
extern "C" __declspec(dllexport) int __cdecl ovl_cuda_normals3d(void* handle, const double* source, int width, int rows,
    double columnPitch, double rowPitch, double* output, Cancel cancel, Timing* timing)
{
    return Finish(fixture_normals(handle, source, width, rows, columnPitch, rowPitch, output, cancel, timing), timing, output);
}
extern "C" __declspec(dllexport) int __cdecl ovl_cuda_scharr8u(void* handle, const unsigned char* source, int width, int rows,
    int channels, int axis, unsigned char* output, Cancel cancel, Timing* timing)
{
    return Finish(fixture_scharr(handle, source, width, rows, channels, axis, output, cancel, timing), timing, nullptr);
}
#endif
