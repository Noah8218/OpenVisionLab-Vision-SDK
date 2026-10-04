#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <fstream>
#include <iterator>
#include <iostream>
#include <string>
#include <vector>

// Build-time compiler only. Neither this executable nor NVRTC is a runtime SDK dependency.
int wmain(int argc, wchar_t** argv)
{
    if (argc != 4) return 2;
    HMODULE library = LoadLibraryExW(argv[2], nullptr, LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR | LOAD_LIBRARY_SEARCH_SYSTEM32);
    if (!library) return 3;
    auto create = reinterpret_cast<int(__cdecl*)(void**, const char*, const char*, int, const char* const*, const char* const*)>(GetProcAddress(library, "nvrtcCreateProgram"));
    auto compile = reinterpret_cast<int(__cdecl*)(void*, int, const char* const*)>(GetProcAddress(library, "nvrtcCompileProgram"));
    auto logSize = reinterpret_cast<int(__cdecl*)(void*, size_t*)>(GetProcAddress(library, "nvrtcGetProgramLogSize"));
    auto getLog = reinterpret_cast<int(__cdecl*)(void*, char*)>(GetProcAddress(library, "nvrtcGetProgramLog"));
    auto ptxSize = reinterpret_cast<int(__cdecl*)(void*, size_t*)>(GetProcAddress(library, "nvrtcGetPTXSize"));
    auto getPtx = reinterpret_cast<int(__cdecl*)(void*, char*)>(GetProcAddress(library, "nvrtcGetPTX"));
    auto destroy = reinterpret_cast<int(__cdecl*)(void**)>(GetProcAddress(library, "nvrtcDestroyProgram"));
    if (!create || !compile || !logSize || !getLog || !ptxSize || !getPtx || !destroy) { FreeLibrary(library); return 4; }
    std::ifstream sourceFile(argv[1], std::ios::binary);
    std::string source((std::istreambuf_iterator<char>(sourceFile)), std::istreambuf_iterator<char>());
    void* program = nullptr;
    int status = source.empty() ? 5 : create(&program, source.c_str(), "Kernels.cu", 0, nullptr, nullptr);
    const char* options[] = { "--gpu-architecture=compute_61", "--fmad=false", "--std=c++14" };
    if (!status) status = compile(program, 3, options);
    size_t size = 0;
    if (program && !logSize(program, &size) && size)
    {
        std::vector<char> log(size);
        if (!getLog(program, log.data())) std::cout << log.data();
    }
    if (!status) status = ptxSize(program, &size);
    if (!status)
    {
        std::vector<char> ptx(size);
        status = getPtx(program, ptx.data());
        if (!status)
        {
            std::ofstream output(argv[3], std::ios::binary);
            output << "static const char KernelPtx[] = R\"OVLPTX(" << ptx.data() << ")OVLPTX\";\n";
            if (!output) status = 6;
        }
    }
    if (program) destroy(&program);
    FreeLibrary(library);
    return status;
}
