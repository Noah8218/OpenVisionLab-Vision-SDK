#include <cstdint>
#include <cstring>
#include <new>
#include <windows.h>

#ifndef OVL_FIXTURE_KIND
#define OVL_FIXTURE_KIND 0
#endif

struct DeviceInfo
{
    uint32_t size;
    int32_t ordinal;
    int32_t major;
    int32_t minor;
    uint64_t totalMemoryBytes;
    char name[128];
};
static_assert(sizeof(DeviceInfo) == 152, "Fixture ABI layout changed.");
static int liveProbes = 0;
extern "C" __declspec(dllexport) int __cdecl ovl_test_live_probes() { return liveProbes; }

#if OVL_FIXTURE_KIND != 2
#if OVL_FIXTURE_KIND == 22
extern "C" __declspec(dllimport) int __cdecl ovl_fixture_dependency();
extern "C" __declspec(dllexport) uint32_t __cdecl ovl_cuda_get_abi_version() { return 1 + ovl_fixture_dependency(); }
#else
extern "C" __declspec(dllexport) uint32_t __cdecl ovl_cuda_get_abi_version() { return OVL_FIXTURE_KIND == 1 ? 99 : 1; }
#endif
#endif
extern "C" __declspec(dllexport) int __cdecl ovl_cuda_create_probe(void** handle, int* version, int* count)
{
    *handle = nullptr; *version = 12090; *count = 3;
    if (OVL_FIXTURE_KIND >= 3 && OVL_FIXTURE_KIND <= 5) return OVL_FIXTURE_KIND - 2;
    if (OVL_FIXTURE_KIND == 17) return 7;
    if (OVL_FIXTURE_KIND == 13) return 0;
    auto probe = new (std::nothrow) int(1);
    if (!probe) return 6;
    *handle = probe;
    ++liveProbes;
    if (OVL_FIXTURE_KIND == 6) return 4;
    if (OVL_FIXTURE_KIND == 7) *count = 0;
    if (OVL_FIXTURE_KIND == 8) *count = 1025;
    if (OVL_FIXTURE_KIND == 14) *version = 0;
    if (OVL_FIXTURE_KIND == 15) *count = -1;
    return 0;
}
extern "C" __declspec(dllexport) int __cdecl ovl_cuda_query_device(void*, int ordinal, DeviceInfo* info)
{
    if (OVL_FIXTURE_KIND == 11 && ordinal == 1) return 4;
    if (OVL_FIXTURE_KIND == 12) Sleep(200);
    memset(info, 0, sizeof(DeviceInfo));
    info->size = sizeof(DeviceInfo);
    info->ordinal = ordinal;
    const int major[] = {6, 8, 12};
    const int minor[] = {1, 6, 0};
    info->major = major[ordinal]; info->minor = minor[ordinal];
    info->totalMemoryBytes = (3ULL + ordinal) * 1024 * 1024 * 1024;
    const char name[] = "Synthetic CUDA fixture";
    memcpy(info->name, name, sizeof(name));
    if (OVL_FIXTURE_KIND == 9) memset(info->name, 'x', sizeof(info->name));
    if (OVL_FIXTURE_KIND == 10) info->ordinal = 999;
    if (OVL_FIXTURE_KIND == 16) info->size = 1;
    if (OVL_FIXTURE_KIND == 18) info->major = 0;
    if (OVL_FIXTURE_KIND == 19) info->totalMemoryBytes = 0;
    if (OVL_FIXTURE_KIND == 21) info->name[0] = '\0';
    return 0;
}
#if OVL_FIXTURE_KIND != 20
extern "C" __declspec(dllexport) void __cdecl ovl_cuda_destroy_probe(void* handle)
{
    delete static_cast<int*>(handle);
    --liveProbes;
}
#endif
