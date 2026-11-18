/* CCS Console Workbench's parity library for Classic Cart Studio for Windows: the same par2_abi.cpp and libpar2 objects as the single-file
 * host, linked as ccs-par2.dll and installed beside the app (never extracted). The app's engine loads it from its own folder by full path
 * and reads the same ABI table through the same export, ccs_host_get_abi. Copyright (C) 2026 Rance9. GPL-3.0-or-later. */
#include <wchar.h>
#if defined(__aarch64__)
#define CCS_DLL_ARCH L"arm64"
#else
#define CCS_DLL_ARCH L"x64"
#endif
const wchar_t *ccs_host_arch(void) { return CCS_DLL_ARCH L" (ccs-par2.dll)"; }
