#pragma once

#include <windows.h>

bool ClrStartPayload(const BYTE* core, DWORD coreSize, const BYTE* token, DWORD tokenSize, const BYTE* media, DWORD mediaSize);
