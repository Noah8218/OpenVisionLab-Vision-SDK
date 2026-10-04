#include "CudaProbeFixture.cpp"
#include <limits>
#include <vector>

#ifndef OVL_EXECUTION_KIND
#define OVL_EXECUTION_KIND 0
#endif
struct Timing
{
    uint32_t size, reserved;
    double uploadMs, kernelWaitMs, downloadMs;
    uint64_t scratchBytes;
    int32_t driverError, launches;
};
using Cancel = int(__cdecl*)();
static LONG liveExecutions = 0;
static LONG activeCalls = 0;
struct FixtureExecution { int calls = 0; bool workspace = false; };
static LONG liveWorkspaces = 0;
static LONG workspaceReleases = 0;
extern "C" __declspec(dllexport) LONG __cdecl ovl_test_live_workspaces() { return liveWorkspaces; }
extern "C" __declspec(dllexport) LONG __cdecl ovl_test_workspace_releases() { return workspaceReleases; }
extern "C" __declspec(dllexport) LONG __cdecl ovl_test_live_executions() { return liveExecutions; }
extern "C" __declspec(dllexport) LONG __cdecl ovl_test_active_calls() { return activeCalls; }
extern "C" __declspec(dllexport) uint32_t __cdecl ovl_cuda_get_execution_abi_version() { return OVL_EXECUTION_KIND == 6 ? 2 : 1; }
extern "C" __declspec(dllexport) int __cdecl ovl_cuda_create_execution(int, void** handle, int* error)
{
    *handle = new (std::nothrow) FixtureExecution(); *error = 0;
    if (!*handle) return 8;
    InterlockedIncrement(&liveExecutions);
    if (OVL_EXECUTION_KIND == 1) { *error = 209; return 11; }
    return 0;
}
extern "C" __declspec(dllexport) void __cdecl ovl_cuda_destroy_execution(void* handle)
{
    if (handle)
    {
        auto value = static_cast<FixtureExecution*>(handle);
        if (value->workspace) InterlockedDecrement(&liveWorkspaces);
        delete value; InterlockedDecrement(&liveExecutions);
    }
}
extern "C" __declspec(dllexport) int __cdecl ovl_cuda_edge_grid(void*, const double* sx, const double* sy, int sourceCount,
    const int* offsets, const double* mx, const double* my, const double* thresholds, int points,
    int width, int minX, int minY, int columns, int rows, int step, int polarity, double* output, Cancel cancel, Timing* timing)
{
    timing->driverError = 0; timing->launches = 1;
    if (sourceCount > 4)
    {
        if (OVL_EXECUTION_KIND == 2) { timing->driverError = 2; return 8; }
        if (OVL_EXECUTION_KIND == 3) { timing->driverError = 719; return 9; }
        if (OVL_EXECUTION_KIND == 4) { output[0] = std::numeric_limits<double>::quiet_NaN(); return 0; }
        if (OVL_EXECUTION_KIND == 7) timing->size = 0;
        if (OVL_EXECUTION_KIND == 5)
        {
            InterlockedIncrement(&activeCalls);
            for (int i = 0; i < 200; i++)
            {
                if (cancel && cancel()) { InterlockedDecrement(&activeCalls); return 10; }
                Sleep(5);
            }
            InterlockedDecrement(&activeCalls);
        }
    }
    for (int row = 0; row < rows; row++) for (int col = 0; col < columns; col++)
    {
        double sum = 0;
        int center = (minY + row * step) * width + minX + col * step;
        int processed = 0;
        for (int i = 0; i < points; i++)
        {
            int at = center + offsets[i]; sum += sx[at] * mx[i] + sy[at] * my[i]; processed = i + 1;
            if (!polarity && sum < thresholds[i]) break;
        }
        output[row * columns + col] = (polarity && sum < 0 ? -sum : sum) / processed;
    }
    return cancel && cancel() ? 10 : 0;
}
extern "C" __declspec(dllexport) int __cdecl ovl_cuda_transform_points(void*, const double* points, int count,
    const double* poses, int poseCount, double* output, Cancel cancel, Timing* timing)
{
    timing->driverError = 0; timing->launches = 1;
    for (int p = 0; p < poseCount; p++) for (int i = 0; i < count; i++) for (int axis = 0; axis < 3; axis++)
    {
        int m = p * 12 + axis * 3, at = i * 3;
        output[(p * count + i) * 3 + axis] = poses[m] * points[at] + poses[m + 1] * points[at + 1] + poses[m + 2] * points[at + 2] + poses[p * 12 + 9 + axis];
    }
    return cancel && cancel() ? 10 : 0;
}

#if OVL_EXECUTION_KIND >= 8
static LONG tileDelay = 1;
extern "C" __declspec(dllexport) int __cdecl ovl_test_set_tile_delay(int enabled) { return InterlockedExchange(&tileDelay, enabled); }
extern "C" __declspec(dllexport) int __cdecl ovl_cuda_get_edge_tile_budget(void*, uint64_t* bytes, int32_t* error)
{
    *bytes = OVL_EXECUTION_KIND == 14 ? 0 : 1024 * 1024; *error = 0;
    return 0;
}
extern "C" __declspec(dllexport) int __cdecl ovl_cuda_edge_tile(void* handle, const double* sx, const double* sy, int sourceCount,
    const int* dx, const int* dy, const double* mx, const double* my, const double* thresholds, int points,
    int width, int height, int minX, int minY, int columns, int rows, int step, int polarity,
    double* output, int capacity, Cancel cancel, Timing* timing)
{
    if (width <= 0 || height <= 0 || (int64_t)width * height != sourceCount || (int64_t)columns * rows > capacity) return 12;
    timing->driverError = 0; timing->launches = 1;
    int call = sourceCount > 4 ? ++static_cast<FixtureExecution*>(handle)->calls : 0;
    if (call > 0) { timing->uploadMs = 1; timing->kernelWaitMs = 0.5; timing->downloadMs = 0.25; }
    if ((OVL_EXECUTION_KIND == 8 && call > 0) || ((OVL_EXECUTION_KIND == 13 || OVL_EXECUTION_KIND == 17) && call == 1)
        || (OVL_EXECUTION_KIND == 9 && call == 2)) { timing->driverError = 2; return 8; }
    if ((OVL_EXECUTION_KIND == 10 || OVL_EXECUTION_KIND == 18) && call == 2) { timing->driverError = 719; return 9; }
    if ((OVL_EXECUTION_KIND == 11 || OVL_EXECUTION_KIND == 19) && call == 2) { output[0] = std::numeric_limits<double>::quiet_NaN(); return 0; }
    if (OVL_EXECUTION_KIND == 15 && call == 2) timing->size = 0;
    if ((OVL_EXECUTION_KIND == 12 || OVL_EXECUTION_KIND == 20) && call > 0 && InterlockedCompareExchange(&tileDelay, 0, 0))
    {
        InterlockedIncrement(&activeCalls);
        for (int i = 0; i < 200; i++)
        {
            if (cancel && cancel()) { InterlockedDecrement(&activeCalls); return 10; }
            Sleep(5);
        }
        InterlockedDecrement(&activeCalls);
    }
    for (int row = 0; row < rows; row++) for (int col = 0; col < columns; col++)
    {
        double sum = 0;
        int processed = 0;
        for (int i = 0; i < points; i++)
        {
            int x = minX + col * step + dx[i], y = minY + row * step + dy[i];
            if (x < 0 || x >= width || y < 0 || y >= height) { sum = std::numeric_limits<double>::quiet_NaN(); break; }
            int at = y * width + x; sum += sx[at] * mx[i] + sy[at] * my[i]; processed = i + 1;
            if (!polarity && sum < thresholds[i]) break;
        }
        output[row * columns + col] = (polarity && sum < 0 ? -sum : sum) / processed;
        if (call == 1 && (OVL_EXECUTION_KIND == 9 || OVL_EXECUTION_KIND == 10 || OVL_EXECUTION_KIND == 11 || OVL_EXECUTION_KIND == 15
            || OVL_EXECUTION_KIND == 18 || OVL_EXECUTION_KIND == 19))
            output[row * columns + col] = 2.0; // Deliberately poison the local first tile to prove rollback.
    }
    return cancel && cancel() ? 10 : 0;
}
#endif

#if OVL_EXECUTION_KIND >= 16 && OVL_EXECUTION_KIND != 23
extern "C" __declspec(dllexport) int __cdecl ovl_cuda_edge_tile_strided(void* handle, const double* sx, const double* sy, int sourceCount,
    int sourceWidth, int sourceHeight, int left, int top, const int* dx, const int* dy,
    const double* mx, const double* my, const double* thresholds, int points,
    int width, int height, int minX, int minY, int columns, int rows, int step, int polarity,
    double* output, int capacity, Cancel cancel, Timing* timing)
{
    if ((int64_t)sourceWidth * sourceHeight != sourceCount || left < 0 || top < 0
        || (int64_t)left + width > sourceWidth || (int64_t)top + height > sourceHeight) return 12;
    auto value = static_cast<FixtureExecution*>(handle);
    if (!value->workspace) { value->workspace = true; InterlockedIncrement(&liveWorkspaces); }
    std::vector<double> px((size_t)width * height), py(px.size());
    for (int y = 0; y < height; ++y)
    {
        memcpy(px.data() + (size_t)y * width, sx + (size_t)(top + y) * sourceWidth + left, (size_t)width * 8);
        memcpy(py.data() + (size_t)y * width, sy + (size_t)(top + y) * sourceWidth + left, (size_t)width * 8);
    }
    return ovl_cuda_edge_tile(handle, px.data(), py.data(), (int)px.size(), dx, dy, mx, my, thresholds, points,
        width, height, minX, minY, columns, rows, step, polarity, output, capacity, cancel, timing);
}
#endif
#if OVL_EXECUTION_KIND >= 16 && OVL_EXECUTION_KIND != 22
extern "C" __declspec(dllexport) int __cdecl ovl_cuda_release_edge_workspace(void* handle, int* error)
{
    auto value = static_cast<FixtureExecution*>(handle);
    *error = 0; InterlockedIncrement(&workspaceReleases);
    if (OVL_EXECUTION_KIND == 21 && value->calls > 0) { *error = 719; return 9; }
    if (value->workspace) { value->workspace = false; InterlockedDecrement(&liveWorkspaces); }
    return 0;
}
#endif
