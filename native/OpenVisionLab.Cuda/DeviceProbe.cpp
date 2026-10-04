#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <windows.h>
#include <cstdint>
#include <cstring>
#include <new>

// Driver API ABI: CUDA handles/status/attributes are 32-bit, size_t is host-sized.
using CuInit = int (WINAPI*)(unsigned int);
using CuDriverVersion = int (WINAPI*)(int*);
using CuDeviceCount = int (WINAPI*)(int*);
using CuDeviceGet = int (WINAPI*)(int*, int);
using CuDeviceName = int (WINAPI*)(char*, int, int);
using CuDeviceAttribute = int (WINAPI*)(int*, int, int);
using CuDeviceMemory = int (WINAPI*)(size_t*, int);

struct Probe
{
    HMODULE driver = nullptr;
    CuDeviceGet get = nullptr;
    CuDeviceName name = nullptr;
    CuDeviceAttribute attribute = nullptr;
    CuDeviceMemory memory = nullptr;
    int count = 0;
    ~Probe() { if (driver) FreeLibrary(driver); }
};

struct DeviceInfo
{
    uint32_t size;
    int32_t ordinal;
    int32_t major;
    int32_t minor;
    uint64_t totalMemoryBytes;
    char name[128];
};
static_assert(sizeof(DeviceInfo) == 152, "Device probe ABI layout changed.");

extern "C" __declspec(dllexport) uint32_t __cdecl ovl_cuda_get_abi_version() { return 1; }

extern "C" __declspec(dllexport) int __cdecl ovl_cuda_create_probe(void** handle, int* version, int* count)
{
    if (!handle || !version || !count) return 5;
    *handle = nullptr; *version = 0; *count = 0;
    Probe* probe = new (std::nothrow) Probe();
    if (!probe) return 6;
    wchar_t path[MAX_PATH];
    UINT length = GetSystemDirectoryW(path, MAX_PATH);
    const wchar_t suffix[] = L"\\nvcuda.dll";
    if (!length || length + sizeof(suffix) / sizeof(wchar_t) > MAX_PATH) { delete probe; return 1; }
    memcpy(path + length, suffix, sizeof(suffix));
    probe->driver = LoadLibraryExW(path, nullptr, LOAD_LIBRARY_SEARCH_SYSTEM32);
    if (!probe->driver) { delete probe; return 1; }
    auto init = reinterpret_cast<CuInit>(GetProcAddress(probe->driver, "cuInit"));
    auto driverVersion = reinterpret_cast<CuDriverVersion>(GetProcAddress(probe->driver, "cuDriverGetVersion"));
    auto deviceCount = reinterpret_cast<CuDeviceCount>(GetProcAddress(probe->driver, "cuDeviceGetCount"));
    probe->get = reinterpret_cast<CuDeviceGet>(GetProcAddress(probe->driver, "cuDeviceGet"));
    probe->name = reinterpret_cast<CuDeviceName>(GetProcAddress(probe->driver, "cuDeviceGetName"));
    probe->attribute = reinterpret_cast<CuDeviceAttribute>(GetProcAddress(probe->driver, "cuDeviceGetAttribute"));
    probe->memory = reinterpret_cast<CuDeviceMemory>(GetProcAddress(probe->driver, "cuDeviceTotalMem_v2"));
    if (!init || !driverVersion || !deviceCount || !probe->get || !probe->name || !probe->attribute || !probe->memory)
    { delete probe; return 2; }
    int result = init(0);
    if (result == 100) { delete probe; return 7; }
    if (result != 0 || driverVersion(version) != 0) { delete probe; return 3; }
    if (deviceCount(&probe->count) != 0) { delete probe; return 4; }
    *count = probe->count;
    *handle = probe;
    return 0;
}

extern "C" __declspec(dllexport) int __cdecl ovl_cuda_query_device(void* handle, int ordinal, DeviceInfo* info)
{
    Probe* probe = static_cast<Probe*>(handle);
    if (!probe || !info || info->size != sizeof(DeviceInfo) || ordinal < 0 || ordinal >= probe->count) return 5;
    int device;
    size_t bytes = 0;
    memset(info, 0, sizeof(DeviceInfo));
    info->size = sizeof(DeviceInfo);
    info->ordinal = ordinal;
    // CU_DEVICE_ATTRIBUTE_COMPUTE_CAPABILITY_MAJOR/MINOR are 75/76 in the Driver API.
    if (probe->get(&device, ordinal) != 0 || probe->name(info->name, sizeof(info->name), device) != 0
        || probe->attribute(&info->major, 75, device) != 0 || probe->attribute(&info->minor, 76, device) != 0
        || probe->memory(&bytes, device) != 0) return 4;
    info->totalMemoryBytes = bytes;
    info->name[sizeof(info->name) - 1] = '\0';
    return 0;
}

extern "C" __declspec(dllexport) void __cdecl ovl_cuda_destroy_probe(void* handle)
{
    delete static_cast<Probe*>(handle);
}
