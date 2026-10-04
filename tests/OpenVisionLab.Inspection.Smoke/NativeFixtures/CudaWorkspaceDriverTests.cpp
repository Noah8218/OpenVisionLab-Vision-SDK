// Executes the production bridge with an owned deterministic Driver API adapter.
// Real CUDA arithmetic and deployment are checked separately by CudaKernelSmokeSuite.
#include "../../../native/OpenVisionLab.Cuda/KernelExecution.cpp"
#include <cstdlib>
#include <cstdio>
#include <map>
#include <vector>

static std::map<DevicePointer, size_t> allocations;
static size_t pool = 1024 * 1024, used = 0, peak = 0;
static int allocated = 0, freed = 0, copied = 0, uploaded = 0;
static int allocationError = 0, copyError = 0, launchError = 0, downloadError = 0, syncError = 0, releaseError = 0, pushError = 0;
static int cancelAt = 0, cancelCalls = 0, pushed = 0, popped = 0, destroyed = 0;
static void* currentContext = reinterpret_cast<void*>(2);
static void Require(bool condition, const char* message) { if (!condition) { printf("FAIL: %s\n", message); std::exit(1); } }
static int WINAPI Info(size_t* freeBytes, size_t* total) { *total = pool; *freeBytes = pool - used; return 0; }
static int WINAPI Allocate(DevicePointer* pointer, size_t bytes)
{
    if (allocationError) { int error = allocationError; allocationError = 0; return error; }
    void* data = std::calloc(1, bytes);
    if (!data || bytes > pool - used) { std::free(data); return 2; }
    *pointer = reinterpret_cast<DevicePointer>(data); allocations[*pointer] = bytes;
    used += bytes; if (used > peak) peak = used; ++allocated; return 0;
}
static int WINAPI Release(DevicePointer pointer)
{
    if (releaseError) { int error = releaseError; releaseError = 0; return error; }
    auto found = allocations.find(pointer); Require(found != allocations.end(), "No stale or duplicate release");
    used -= found->second; std::free(reinterpret_cast<void*>(pointer)); allocations.erase(found); ++freed; return 0;
}
static int WINAPI Upload(DevicePointer target, const void* source, size_t bytes) { memcpy(reinterpret_cast<void*>(target), source, bytes); ++uploaded; return 0; }
static int WINAPI Copy(const Copy2D* copy)
{
    if (copyError) { int error = copyError; copyError = 0; return error; }
    Require(copy->sourceType == 1 && copy->destinationType == 2, "Host to device copy types");
    for (size_t y = 0; y < copy->height; ++y)
        memcpy(reinterpret_cast<char*>(copy->destinationDevice) + y * copy->destinationPitch,
            static_cast<const char*>(copy->sourceHost) + (y + copy->sourceY) * copy->sourcePitch + copy->sourceX, copy->widthBytes);
    ++copied; return 0;
}
static int WINAPI Download(void* target, DevicePointer source, size_t bytes)
{
    if (downloadError) { int error = downloadError; downloadError = 0; return error; }
    memcpy(target, reinterpret_cast<void*>(source), bytes); return 0;
}
static int WINAPI Launch(void*, unsigned, unsigned, unsigned, unsigned, unsigned, unsigned, unsigned, void*, void** args, void**)
{
    if (launchError) { int error = launchError; launchError = 0; return error; }
    auto doubles = [&](int at) { return reinterpret_cast<double*>(*static_cast<DevicePointer*>(args[at])); };
    auto integer = [&](int at) { return *static_cast<int*>(args[at]); };
    Require(integer(7) == 1, "Fixture has one independently known model point");
    double* result = doubles(17);
    for (int i = 0; i < integer(16); ++i)
    {
        int position = integer(15) + i;
        int x = integer(10) + position % integer(12) * integer(13);
        int y = integer(11) + position / integer(12) * integer(13);
        result[i] = doubles(0)[y * integer(8) + x] * doubles(4)[0] + doubles(1)[y * integer(8) + x] * doubles(5)[0];
    }
    return 0;
}
static int WINAPI Synchronize(void*) { int error = syncError; syncError = 0; return error; }
static int WINAPI Push(void*) { if (pushError) return pushError; ++pushed; return 0; }
static int WINAPI Pop(void** value) { *value = nullptr; ++popped; return 0; }
static int WINAPI GetCurrent(void** value) { *value = currentContext; return 0; }
static int WINAPI SetCurrent(void* value) { currentContext = value; return 0; }
static int WINAPI Destroy(void*)
{
    ++destroyed;
    while (!allocations.empty()) { releaseError = 0; Release(allocations.begin()->first); }
    return 0;
}
static int __cdecl CancelCall() { ++cancelCalls; return cancelAt && cancelCalls >= cancelAt; }
static Execution* Create()
{
    Execution* value = new Execution(); value->context = reinterpret_cast<void*>(1);
    value->allocate = Allocate; value->release = Release; value->memoryInfo = Info;
    value->upload = Upload; value->copy2D = Copy; value->download = Download;
    value->launch = Launch; value->synchronize = Synchronize; value->push = Push; value->pop = Pop;
    value->getCurrent = GetCurrent; value->setCurrent = SetCurrent; value->destroy = Destroy;
    return value;
}
static Timing Call(Execution* value, int w, int h, double modelX, int expectedStatus = 0, int left = 2, int top = 1)
{
    double sx[35], sy[35], output[35] = {}, mx[] = { modelX }, my[] = { 1 - modelX }, limits[] = { -1000 };
    int offsets[] = { 0 };
    for (int y = 0; y < 5; ++y) for (int x = 0; x < 7; ++x) { sx[y * 7 + x] = 100 * y + x; sy[y * 7 + x] = -10 * y - x; }
    Timing timing = {}; timing.size = sizeof(Timing);
    int status = ovl_cuda_edge_tile_strided(value, sx, sy, 35, 7, 5, left, top, offsets, offsets, mx, my, limits, 1,
        w, h, 0, 0, w, h, 1, 0, output, 35, CancelCall, &timing);
    Require(status == expectedStatus, "Expected native status");
    if (!status) for (int y = 0; y < h; ++y) for (int x = 0; x < w; ++x)
        Require(output[y * w + x] == (modelX == 1 ? 100 * (y + top) + x + left : -10 * (y + top) - x - left), "Original pitch and changed model values");
    return timing;
}
static void End(Execution* value)
{
    int error = -1; Require(ovl_cuda_release_edge_workspace(value, &error) == 0 && error == 0 && used == 0, "Search end releases all workspace");
}
static int ActualGpu()
{
    void* handle = nullptr; int error = 0;
    Require(ovl_cuda_create_execution(0, &handle, &error) == 0 && handle, "Actual driver context creation");
    Execution* value = static_cast<Execution*>(handle);
    constexpr int width = 2049, height = 513;
    std::vector<double> sx((size_t)width * height, 0.5), sy(sx.size()), result(sx.size());
    int offsets[] = { 0 }; double mx[] = { 1 }, my[] = { 0 }, limits[] = { -2 };
    auto run = [&](int w, int h, int expected)
    {
        Timing timing = {}; timing.size = sizeof(Timing);
        int status = ovl_cuda_edge_tile_strided(handle, sx.data(), sy.data(), (int)sx.size(), width, height, 1, 1,
            offsets, offsets, mx, my, limits, 1, w, h, 0, 0, w, h, 1, 0, result.data(), (int)result.size(), CancelCall, &timing);
        Require(status == expected, "Actual GPU status");
        if (!status) for (int i = 0; i < w * h; ++i) Require(result[i] == 0.5, "Actual GPU independent constant score");
        Require(value->edgeWorkspaceBytes <= 256ULL * 1024 * 1024, "Actual allocation obeys hard ceiling");
    };
    auto end = [&]()
    {
        int code = -1;
        Require(ovl_cuda_release_edge_workspace(handle, &code) == 0 && code == 0
            && value->edgeWorkspace == 0 && value->edgeWorkspaceBytes == 0, "Actual cuMemFree succeeds and idle owner retains no allocation");
    };
    run(3, 2, 0); end(); // Kernel/context warm-up before shared-device memory observations.
    for (int cycle = 0; cycle < 3; ++cycle)
    {
        size_t before = 0, during = 0, after = 0, total = 0;
        { CurrentContext context(value); Require(context.status == 0 && value->memoryInfo(&before, &total) == 0, "Actual before memory query"); }
        run(2047, 511, 0);
        DevicePointer pointer = value->edgeWorkspace; size_t capacity = value->edgeWorkspaceBytes;
        run(1023, 257, 0);
        Require(value->edgeWorkspace == pointer && value->edgeWorkspaceBytes == capacity, "Actual smaller tile reuses the device allocation");
        { CurrentContext context(value); Require(context.status == 0 && value->memoryInfo(&during, &total) == 0, "Actual active memory query"); }
        end();
        { CurrentContext context(value); Require(context.status == 0 && value->memoryInfo(&after, &total) == 0, "Actual after memory query"); }
        printf("PASS G%d actual reuse/release capacity=%llu beforeFree=%llu activeFree=%llu afterFree=%llu\n", cycle + 1,
            (unsigned long long)capacity, (unsigned long long)before, (unsigned long long)during, (unsigned long long)after);
    }
    cancelCalls = 0; cancelAt = 3; run(2047, 511, 10); end(); cancelAt = 0;
    run(3, 2, 0); end();
    printf("PASS G4 actual after-launch cancellation release and recovery\n");
    run(2047, 511, 0); // Destruction is the final owner even if a borrower omits explicit release.
    ovl_cuda_destroy_execution(handle);
    handle = nullptr;
    Require(ovl_cuda_create_execution(0, &handle, &error) == 0 && handle, "New context after retained-workspace destruction");
    value = static_cast<Execution*>(handle); run(3, 2, 0); end(); ovl_cuda_destroy_execution(handle);
    printf("PASS G5 actual context destruction with retained workspace and new-context recovery\n");
    printf("PASS actual GPU workspace cases=5; shared free-memory observations are not process-exclusive VRAM measurements\n");
    return 0;
}

int main(int argc, char** argv)
{
    if (argc == 2 && strcmp(argv[1], "--actual") == 0) return ActualGpu();
    Execution* value = Create();
    double source[] = { 1, -1, 0, 0.5 }, zero[4] = {}, model[] = { 1 }, limit[] = { -2 }, output[4] = {};
    int offsets[] = { 0 }; Timing timing = {}; timing.size = sizeof(Timing);
    Require(ovl_cuda_edge_tile(value, source, zero, 4, offsets, offsets, model, zero, limit, 1, 4, 1, 0, 0, 4, 1, 1, 0, output, 4, nullptr, &timing) == 0,
        "Old packed ABI succeeds");
    Require(allocated == 1 && freed == 1 && used == 0 && memcmp(output, source, sizeof(source)) == 0, "Old packed call is one local allocation with exact results");
    printf("PASS N01 packed ABI local lifetime\n");

    timing = Call(value, 3, 2, 1); size_t capacity = value->edgeWorkspaceBytes;
    Call(value, 3, 2, 0); Call(value, 1, 1, 1);
    Require(allocated == 2 && freed == 1 && copied == 6 && uploaded == 22 && used == capacity,
        "Three tiles reuse one allocation while reuploading every source and model");
    Require(timing.scratchBytes == capacity && capacity <= pool / 4, "Actual retained capacity is budgeted");
    printf("PASS N02 pitched transfer changed model tail and reuse counts\n");
    peak = used; Call(value, 4, 3, 1);
    Require(allocated == 3 && freed == 2 && peak == used, "Growth frees old allocation before creating new");
    printf("PASS N03 growth has no double allocation peak\n");
    pool = used * 2; timing = Call(value, 1, 1, 1);
    Require(timing.scratchBytes <= pool / 4 && allocated == 4 && freed == 3, "Pressure shrinks capacity to actual allowance");
    End(value); pool = 128; Call(value, 1, 1, 1, 8); Require(used == 0, "Rejected budget retains nothing"); pool = 1024 * 1024;
    printf("PASS N04 pressure shrink and budget refusal\n");
    allocationError = 2; Call(value, 3, 2, 1, 8); Require(used == 0, "Allocation failure retains nothing");
    Call(value, 1, 1, 1); End(value);
    printf("PASS N05 allocation failure and valid recovery\n");
    for (int* failure : { &copyError, &launchError, &syncError, &downloadError })
    {
        *failure = 719; Call(value, 3, 2, 1, 9); End(value); Call(value, 1, 1, 0); End(value);
    }
    printf("PASS N06 transfer launch wait download failures and recovery\n");
    cancelCalls = 0; cancelAt = 1; Call(value, 3, 2, 1, 10); Require(used == 0, "Pre-cancel cannot allocate");
    cancelCalls = 0; cancelAt = 3; Call(value, 3, 2, 1, 10); End(value);
    cancelAt = 0; Call(value, 1, 1, 1); End(value);
    printf("PASS N07 pre-cancel and after-launch cancellation release\n");
    Call(value, 3, 2, 1); int error = 0;
    pushError = 201; Require(ovl_cuda_release_edge_workspace(value, &error) == 9 && error == 201 && used > 0, "Context failure is surfaced"); pushError = 0;
    releaseError = 719; Require(ovl_cuda_release_edge_workspace(value, &error) == 9 && error == 719 && used > 0, "Failed free retains ownership for destruction"); End(value);
    printf("PASS N08 cleanup errors preserve ownership and recover\n");
    int before = allocated;
    Call(value, 3, 2, 1, 12, -1, 1); Call(value, 3, 2, 1, 12, 6, 1); Call(value, 3, 2, 1, 12, 2, 4);
    Require(allocated == before && used == 0, "Malformed rectangles are rejected before allocation");
    printf("PASS N09 input bounds\n");
    Call(value, 3, 2, 1); releaseError = 719;
    ovl_cuda_destroy_execution(value);
    Require(used == 0 && allocations.empty() && destroyed == 1 && pushed == popped && currentContext == reinterpret_cast<void*>(2),
        "Context destruction clears retained allocation and restores previous context");
    printf("PASS N10 destruction after failed release; context balance\n");
    printf("PASS native workspace production-code driver cases=10 allocated=%d freed=%d\n", allocated, freed);
    return 0;
}
