#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include <cstdint>
#include <cstring>
#include <new>
#include <cmath>
#include "KernelPtx.h"

using DevicePointer = unsigned long long;
using Cancel = int(__cdecl*)();
struct Timing
{
    uint32_t size;
    uint32_t reserved;
    double uploadMs;
    double kernelWaitMs;
    double downloadMs;
    uint64_t scratchBytes;
    int32_t driverError;
    int32_t launches;
};
static_assert(sizeof(Timing) == 48, "Execution timing ABI changed.");
static double Now()
{
    LARGE_INTEGER counter, frequency;
    QueryPerformanceCounter(&counter); QueryPerformanceFrequency(&frequency);
    return counter.QuadPart * 1000.0 / frequency.QuadPart;
}

// CUDA_MEMCPY2D_v2, used only through the dynamically loaded Driver API.
struct Copy2D
{
    size_t sourceX, sourceY;
    int sourceType;
    const void* sourceHost;
    DevicePointer sourceDevice;
    void* sourceArray;
    size_t sourcePitch;
    size_t destinationX, destinationY;
    int destinationType;
    void* destinationHost;
    DevicePointer destinationDevice;
    void* destinationArray;
    size_t destinationPitch;
    size_t widthBytes, height;
};
static_assert(sizeof(Copy2D) == 128, "CUDA 64-bit copy layout changed.");

struct Execution
{
    HMODULE driver = nullptr;
    void* context = nullptr;
    void* module = nullptr;
    void* stream = nullptr;
    void* edge = nullptr;
    void* edgeTile = nullptr;
    void* transform = nullptr;
    DevicePointer edgeWorkspace = 0;
    size_t edgeWorkspaceBytes = 0;
    int(WINAPI* create)(void**, unsigned, int) = nullptr;
    int(WINAPI* destroy)(void*) = nullptr;
    int(WINAPI* getCurrent)(void**) = nullptr;
    int(WINAPI* setCurrent)(void*) = nullptr;
    int(WINAPI* push)(void*) = nullptr;
    int(WINAPI* pop)(void**) = nullptr;
    int(WINAPI* load)(void**, const void*) = nullptr;
    int(WINAPI* unload)(void*) = nullptr;
    int(WINAPI* function)(void**, void*, const char*) = nullptr;
    int(WINAPI* streamCreate)(void**, unsigned) = nullptr;
    int(WINAPI* streamDestroy)(void*) = nullptr;
    int(WINAPI* synchronize)(void*) = nullptr;
    int(WINAPI* memoryInfo)(size_t*, size_t*) = nullptr;
    int(WINAPI* allocate)(DevicePointer*, size_t) = nullptr;
    int(WINAPI* release)(DevicePointer) = nullptr;
    int(WINAPI* upload)(DevicePointer, const void*, size_t) = nullptr;
    int(WINAPI* download)(void*, DevicePointer, size_t) = nullptr;
    int(WINAPI* copy2D)(const Copy2D*) = nullptr;
    int(WINAPI* launch)(void*, unsigned, unsigned, unsigned, unsigned, unsigned, unsigned,
        unsigned, void*, void**, void**) = nullptr;

    int ReleaseEdgeWorkspace()
    {
        if (!edgeWorkspace) return 0;
        int error = release(edgeWorkspace);
        if (!error) { edgeWorkspace = 0; edgeWorkspaceBytes = 0; }
        return error;
    }

    ~Execution()
    {
        if (context)
        {
            void* previous = nullptr;
            getCurrent(&previous);
            setCurrent(context);
            if (stream) { synchronize(stream); streamDestroy(stream); }
            ReleaseEdgeWorkspace();
            if (module) unload(module);
            destroy(context);
            if (previous != context) setCurrent(previous);
        }
        if (driver) FreeLibrary(driver);
    }
};

struct CurrentContext
{
    Execution* owner;
    int status;
    explicit CurrentContext(Execution* value) : owner(value), status(value->push(value->context)) { }
    ~CurrentContext() { if (!status) { void* ignored = nullptr; owner->pop(&ignored); } }
};
struct Buffer
{
    Execution* owner;
    DevicePointer pointer = 0;
    explicit Buffer(Execution* value) : owner(value) { }
    ~Buffer() { if (pointer) owner->release(pointer); }
    int Allocate(size_t bytes) { return owner->allocate(&pointer, bytes); }
    int Upload(const void* data, size_t bytes) { return owner->upload(pointer, data, bytes); }
};
static int Fail(Timing* timing, int error, int status) { timing->driverError = error; return status; }
static bool Cancelled(Cancel cancel) { return cancel && cancel() != 0; }
static int Budget(Execution* owner, uint64_t bytes, Timing* timing)
{
    // Per-call scratch is bounded; no scene-by-model distance matrix or global GPU cache.
    constexpr uint64_t maximum = 64ULL * 1024 * 1024;
    if (bytes > maximum) return Fail(timing, 0, 8);
    size_t freeBytes = 0, total = 0;
    int status = owner->memoryInfo(&freeBytes, &total);
    if (status) return Fail(timing, status, 9);
    if (bytes > freeBytes / 2) return Fail(timing, 2, 8);
    timing->scratchBytes = bytes;
    return 0;
}

extern "C" __declspec(dllexport) uint32_t __cdecl ovl_cuda_get_execution_abi_version() { return 1; }
extern "C" __declspec(dllexport) int __cdecl ovl_cuda_create_execution(int ordinal, void** handle, int* driverError)
{
    if (!handle || !driverError || ordinal < 0) return 12;
    *handle = nullptr; *driverError = 0;
    Execution* value = new (std::nothrow) Execution();
    if (!value) return 8;
    wchar_t path[MAX_PATH];
    UINT length = GetSystemDirectoryW(path, MAX_PATH);
    const wchar_t suffix[] = L"\\nvcuda.dll";
    if (!length || length + sizeof(suffix) / sizeof(wchar_t) > MAX_PATH) { delete value; return 11; }
    memcpy(path + length, suffix, sizeof(suffix));
    value->driver = LoadLibraryExW(path, nullptr, LOAD_LIBRARY_SEARCH_SYSTEM32);
    if (!value->driver) { delete value; return 11; }
#define BIND(field, name) value->field = reinterpret_cast<decltype(value->field)>(GetProcAddress(value->driver, name)); if (!value->field) { delete value; return 11; }
    BIND(create, "cuCtxCreate_v2"); BIND(destroy, "cuCtxDestroy_v2");
    BIND(getCurrent, "cuCtxGetCurrent"); BIND(setCurrent, "cuCtxSetCurrent");
    BIND(push, "cuCtxPushCurrent_v2"); BIND(pop, "cuCtxPopCurrent_v2");
    BIND(load, "cuModuleLoadData"); BIND(unload, "cuModuleUnload"); BIND(function, "cuModuleGetFunction");
    BIND(streamCreate, "cuStreamCreate"); BIND(streamDestroy, "cuStreamDestroy_v2"); BIND(synchronize, "cuStreamSynchronize");
    BIND(memoryInfo, "cuMemGetInfo_v2"); BIND(allocate, "cuMemAlloc_v2"); BIND(release, "cuMemFree_v2");
    BIND(upload, "cuMemcpyHtoD_v2"); BIND(download, "cuMemcpyDtoH_v2"); BIND(launch, "cuLaunchKernel");
    BIND(copy2D, "cuMemcpy2D_v2");
#undef BIND
    auto init = reinterpret_cast<int(WINAPI*)(unsigned)>(GetProcAddress(value->driver, "cuInit"));
    auto get = reinterpret_cast<int(WINAPI*)(int*, int)>(GetProcAddress(value->driver, "cuDeviceGet"));
    if (!init || !get) { delete value; return 11; }
    int device = 0;
    void* previous = nullptr;
    int error = init(0);
    if (!error) error = get(&device, ordinal);
    if (!error) error = value->getCurrent(&previous);
    if (!error) error = value->create(&value->context, 0, device);
    if (!error) error = value->load(&value->module, KernelPtx);
    if (!error) error = value->function(&value->edge, value->module, "edge_grid");
    if (!error) error = value->function(&value->edgeTile, value->module, "edge_grid_tile");
    if (!error) error = value->function(&value->transform, value->module, "transform_points");
    if (!error) error = value->streamCreate(&value->stream, 1);
    int restore = value->setCurrent(previous);
    if (!error) error = restore;
    if (error) { *driverError = error; delete value; return error == 2 ? 8 : 11; }
    *handle = value;
    return 0;
}
extern "C" __declspec(dllexport) void __cdecl ovl_cuda_destroy_execution(void* handle)
{
    delete static_cast<Execution*>(handle);
}

extern "C" __declspec(dllexport) int __cdecl ovl_cuda_edge_grid(void* handle,
    const double* sourceX, const double* sourceY, int sourceCount, const int* offsets,
    const double* modelX, const double* modelY, const double* thresholds, int points,
    int width, int minX, int minY, int columns, int rows, int step, int polarity,
    double* output, Cancel cancel, Timing* timing)
{
    Execution* value = static_cast<Execution*>(handle);
    if (!value || !sourceX || !sourceY || !offsets || !modelX || !modelY || !thresholds || !output
        || !timing || timing->size != sizeof(Timing) || sourceCount <= 0 || points <= 0 || width <= 0
        || columns <= 0 || rows <= 0 || step <= 0 || (int64_t)columns * rows > INT32_MAX) return 12;
    uint32_t size = timing->size; memset(timing, 0, sizeof(Timing)); timing->size = size;
    if (Cancelled(cancel)) return 10;
    CurrentContext current(value);
    if (current.status) return Fail(timing, current.status, 9);
    int count = columns * rows;
    int tile = count < 16384 ? count : 16384;
    uint64_t bytes = (uint64_t)sourceCount * 16 + (uint64_t)points * 28 + (uint64_t)tile * 8;
    int status = Budget(value, bytes, timing);
    if (status) return status;
    Buffer sx(value), sy(value), off(value), mx(value), my(value), limits(value), scores(value);
    double start = Now();
    int error = sx.Allocate((size_t)sourceCount * 8);
    if (!error) error = sy.Allocate((size_t)sourceCount * 8);
    if (!error) error = off.Allocate((size_t)points * 4);
    if (!error) error = mx.Allocate((size_t)points * 8);
    if (!error) error = my.Allocate((size_t)points * 8);
    if (!error) error = limits.Allocate((size_t)points * 8);
    if (!error) error = scores.Allocate((size_t)tile * 8);
    if (error) return Fail(timing, error, 8);
    error = sx.Upload(sourceX, (size_t)sourceCount * 8);
    if (!error) error = sy.Upload(sourceY, (size_t)sourceCount * 8);
    if (!error) error = off.Upload(offsets, (size_t)points * 4);
    if (!error) error = mx.Upload(modelX, (size_t)points * 8);
    if (!error) error = my.Upload(modelY, (size_t)points * 8);
    if (!error) error = limits.Upload(thresholds, (size_t)points * 8);
    timing->uploadMs = Now() - start;
    if (error) return Fail(timing, error, 9);
    for (int first = 0; first < count; first += tile)
    {
        if (Cancelled(cancel)) return 10;
        int sizeNow = count - first < tile ? count - first : tile;
        void* arguments[] = { &sx.pointer, &sy.pointer, &sourceCount, &off.pointer, &mx.pointer, &my.pointer,
            &limits.pointer, &points, &width, &minX, &minY, &columns, &step, &polarity, &first, &sizeNow, &scores.pointer };
        start = Now();
        error = value->launch(value->edge, (sizeNow + 127) / 128, 1, 1, 128, 1, 1, 0, value->stream, arguments, nullptr);
        if (!error) error = value->synchronize(value->stream);
        timing->kernelWaitMs += Now() - start;
        ++timing->launches;
        if (error) return Fail(timing, error, 9);
        if (Cancelled(cancel)) return 10;
        start = Now();
        error = value->download(output + first, scores.pointer, (size_t)sizeNow * 8);
        timing->downloadMs += Now() - start;
        if (error) return Fail(timing, error, 9);
    }
    return Cancelled(cancel) ? 10 : 0;
}

extern "C" __declspec(dllexport) int __cdecl ovl_cuda_transform_points(void* handle,
    const double* points, int pointCount, const double* poses, int poseCount,
    double* output, Cancel cancel, Timing* timing)
{
    Execution* value = static_cast<Execution*>(handle);
    if (!value || !points || !poses || !output || !timing || timing->size != sizeof(Timing)
        || pointCount <= 0 || poseCount <= 0 || (int64_t)pointCount * poseCount > INT32_MAX / 3) return 12;
    uint32_t size = timing->size; memset(timing, 0, sizeof(Timing)); timing->size = size;
    if (Cancelled(cancel)) return 10;
    CurrentContext current(value);
    if (current.status) return Fail(timing, current.status, 9);
    int count = pointCount * poseCount;
    int tile = count < 16384 ? count : 16384;
    uint64_t bytes = (uint64_t)pointCount * 24 + (uint64_t)poseCount * 96 + (uint64_t)tile * 24;
    int status = Budget(value, bytes, timing);
    if (status) return status;
    Buffer input(value), matrices(value), transformed(value);
    double start = Now();
    int error = input.Allocate((size_t)pointCount * 24);
    if (!error) error = matrices.Allocate((size_t)poseCount * 96);
    if (!error) error = transformed.Allocate((size_t)tile * 24);
    if (error) return Fail(timing, error, 8);
    error = input.Upload(points, (size_t)pointCount * 24);
    if (!error) error = matrices.Upload(poses, (size_t)poseCount * 96);
    timing->uploadMs = Now() - start;
    if (error) return Fail(timing, error, 9);
    for (int first = 0; first < count; first += tile)
    {
        if (Cancelled(cancel)) return 10;
        int sizeNow = count - first < tile ? count - first : tile;
        void* arguments[] = { &input.pointer, &pointCount, &matrices.pointer, &first, &sizeNow, &transformed.pointer };
        start = Now();
        error = value->launch(value->transform, (sizeNow + 127) / 128, 1, 1, 128, 1, 1, 0, value->stream, arguments, nullptr);
        if (!error) error = value->synchronize(value->stream);
        timing->kernelWaitMs += Now() - start;
        ++timing->launches;
        if (error) return Fail(timing, error, 9);
        if (Cancelled(cancel)) return 10;
        start = Now();
        error = value->download(output + first * 3, transformed.pointer, (size_t)sizeNow * 24);
        timing->downloadMs += Now() - start;
        if (error) return Fail(timing, error, 9);
    }
    return Cancelled(cancel) ? 10 : 0;
}

extern "C" __declspec(dllexport) int __cdecl ovl_cuda_get_edge_tile_budget(void* handle, uint64_t* bytes, int32_t* driverError)
{
    Execution* value = static_cast<Execution*>(handle);
    if (!value || !bytes || !driverError) return 12;
    *bytes = 0; *driverError = 0;
    CurrentContext current(value);
    if (current.status) { *driverError = current.status; return 9; }
    size_t freeBytes = 0, total = 0;
    int error = value->memoryInfo(&freeBytes, &total);
    if (error) { *driverError = error; return 9; }
    constexpr uint64_t maximum = 256ULL * 1024 * 1024;
    *bytes = freeBytes / 4 < maximum ? freeBytes / 4 : maximum;
    return 0;
}

static int ScoreEdgeTile(Execution* value,
    const double* sourceX, const double* sourceY, int sourceCount, const int* offsetX, const int* offsetY,
    const double* modelX, const double* modelY, const double* thresholds, int points,
    int width, int height, int minX, int minY, int columns, int rows, int step, int polarity,
    double* output, int outputCapacity, Cancel cancel, Timing* timing,
    int sourceWidth, int sourceHeight, int sourceLeft, int sourceTop, bool reuseWorkspace)
{
    if (!value || !sourceX || !sourceY || !offsetX || !offsetY || !modelX || !modelY || !thresholds || !output
        || !timing || timing->size != sizeof(Timing) || sourceCount <= 0 || points <= 0 || width <= 0 || height <= 0
        || sourceWidth <= 0 || sourceHeight <= 0 || (int64_t)sourceWidth * sourceHeight != sourceCount
        || sourceLeft < 0 || sourceTop < 0 || (int64_t)sourceLeft + width > sourceWidth || (int64_t)sourceTop + height > sourceHeight
        || columns <= 0 || rows <= 0 || step <= 0
        || (int64_t)columns * rows > outputCapacity || (int64_t)columns * rows > 64LL * 1024 * 1024 / 8) return 12;
    uint32_t size = timing->size; memset(timing, 0, sizeof(Timing)); timing->size = size;
    if (Cancelled(cancel)) return 10;
    CurrentContext current(value);
    if (current.status) return Fail(timing, current.status, 9);
    int count = columns * rows;
    size_t samples = (size_t)width * height;
    uint64_t bytes = (uint64_t)samples * 16 + (uint64_t)points * 32 + (uint64_t)count * 8;
    constexpr uint64_t maximum = 256ULL * 1024 * 1024;
    size_t freeBytes = 0, total = 0;
    int error = value->memoryInfo(&freeBytes, &total);
    if (error) return Fail(timing, error, 9);
    // Count this search's retained allocation as available again, without changing the 1/4 reserve.
    uint64_t available = (uint64_t)freeBytes + (reuseWorkspace ? value->edgeWorkspaceBytes : 0);
    uint64_t budget = available / 4 < maximum ? available / 4 : maximum;
    if (bytes > budget) return Fail(timing, 2, 8);
    Buffer scratch(value);
    double start = Now();
    if (reuseWorkspace)
    {
        if (value->edgeWorkspaceBytes < bytes || value->edgeWorkspaceBytes > budget)
        {
            error = value->ReleaseEdgeWorkspace();
            if (error) return Fail(timing, error, 9);
            error = value->allocate(&value->edgeWorkspace, (size_t)bytes);
            if (!error) value->edgeWorkspaceBytes = (size_t)bytes;
        }
        timing->scratchBytes = value->edgeWorkspaceBytes;
    }
    else { error = scratch.Allocate((size_t)bytes); timing->scratchBytes = bytes; }
    if (error) { timing->uploadMs = Now() - start; return Fail(timing, error, 8); }
    DevicePointer sx = reuseWorkspace ? value->edgeWorkspace : scratch.pointer;
    DevicePointer sy = sx + samples * 8, ox = sy + samples * 8, oy = ox + (size_t)points * 4;
    DevicePointer mx = oy + (size_t)points * 4, my = mx + (size_t)points * 8;
    DevicePointer limits = my + (size_t)points * 8, scores = limits + (size_t)points * 8;
    if (reuseWorkspace)
    {
        Copy2D copy = {};
        copy.sourceX = (size_t)sourceLeft * 8; copy.sourceY = sourceTop;
        copy.sourceType = 1; copy.sourceHost = sourceX; copy.sourcePitch = (size_t)sourceWidth * 8;
        copy.destinationType = 2; copy.destinationDevice = sx; copy.destinationPitch = (size_t)width * 8;
        copy.widthBytes = (size_t)width * 8; copy.height = height;
        error = value->copy2D(&copy);
        if (!error) { copy.sourceHost = sourceY; copy.destinationDevice = sy; error = value->copy2D(&copy); }
    }
    else
    {
        error = value->upload(sx, sourceX, samples * 8);
        if (!error) error = value->upload(sy, sourceY, samples * 8);
    }
    if (!error) error = value->upload(ox, offsetX, (size_t)points * 4);
    if (!error) error = value->upload(oy, offsetY, (size_t)points * 4);
    if (!error) error = value->upload(mx, modelX, (size_t)points * 8);
    if (!error) error = value->upload(my, modelY, (size_t)points * 8);
    if (!error) error = value->upload(limits, thresholds, (size_t)points * 8);
    timing->uploadMs = Now() - start;
    if (error) return Fail(timing, error, 9);
    for (int first = 0; first < count; first += 16384)
    {
        if (Cancelled(cancel)) return 10;
        int sizeNow = count - first < 16384 ? count - first : 16384;
        DevicePointer destination = scores + (uint64_t)first * 8;
        void* arguments[] = { &sx, &sy, &ox, &oy, &mx, &my,
            &limits, &points, &width, &height, &minX, &minY, &columns, &step, &polarity, &first, &sizeNow, &destination };
        start = Now();
        error = value->launch(value->edgeTile, (sizeNow + 127) / 128, 1, 1, 128, 1, 1, 0, value->stream, arguments, nullptr);
        if (!error) error = value->synchronize(value->stream);
        timing->kernelWaitMs += Now() - start;
        ++timing->launches;
        if (error) return Fail(timing, error, 9);
        if (Cancelled(cancel)) return 10;
        start = Now();
        error = value->download(output + first, destination, (size_t)sizeNow * 8);
        timing->downloadMs += Now() - start;
        if (error) return Fail(timing, error, 9);
    }
    return Cancelled(cancel) ? 10 : 0;
}

extern "C" __declspec(dllexport) int __cdecl ovl_cuda_edge_tile(void* handle,
    const double* sourceX, const double* sourceY, int sourceCount, const int* offsetX, const int* offsetY,
    const double* modelX, const double* modelY, const double* thresholds, int points,
    int width, int height, int minX, int minY, int columns, int rows, int step, int polarity,
    double* output, int outputCapacity, Cancel cancel, Timing* timing)
{
    return ScoreEdgeTile(static_cast<Execution*>(handle), sourceX, sourceY, sourceCount, offsetX, offsetY,
        modelX, modelY, thresholds, points, width, height, minX, minY, columns, rows, step, polarity,
        output, outputCapacity, cancel, timing, width, height, 0, 0, false);
}

extern "C" __declspec(dllexport) int __cdecl ovl_cuda_edge_tile_strided(void* handle,
    const double* sourceX, const double* sourceY, int sourceCount, int sourceWidth, int sourceHeight, int sourceLeft, int sourceTop,
    const int* offsetX, const int* offsetY, const double* modelX, const double* modelY, const double* thresholds, int points,
    int width, int height, int minX, int minY, int columns, int rows, int step, int polarity,
    double* output, int outputCapacity, Cancel cancel, Timing* timing)
{
    return ScoreEdgeTile(static_cast<Execution*>(handle), sourceX, sourceY, sourceCount, offsetX, offsetY,
        modelX, modelY, thresholds, points, width, height, minX, minY, columns, rows, step, polarity,
        output, outputCapacity, cancel, timing, sourceWidth, sourceHeight, sourceLeft, sourceTop, true);
}

extern "C" __declspec(dllexport) int __cdecl ovl_cuda_release_edge_workspace(void* handle, int* driverError)
{
    Execution* value = static_cast<Execution*>(handle);
    if (!value || !driverError) return 12;
    *driverError = 0;
    CurrentContext current(value);
    if (current.status) { *driverError = current.status; return 9; }
    *driverError = value->ReleaseEdgeWorkspace();
    return *driverError ? 9 : 0;
}
