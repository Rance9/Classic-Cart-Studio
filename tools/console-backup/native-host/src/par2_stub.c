/* CCS Console Workbench native host: a measurement-only stand-in for par2_abi.cpp (build_host.sh --measure links a host WITHOUT libpar2
 * so the size the PAR2 code adds can be reported). Never shipped. Copyright (C) 2026 Rance9. GPL-3.0-or-later. */
#include "ccs_host_abi.h"
#include "host_version.h"

static ccs_host_abi abi = { CCS_HOST_ABI_MAGIC, CCS_HOST_ABI_VERSION, (uint32_t)sizeof(ccs_host_abi), (uint32_t)sizeof(ccs_par2_report), 0,
                            CCS_HOST_VERSION_W, L"", L"none", 0, 0, 0 };

__declspec(dllexport) const ccs_host_abi *ccs_host_get_abi(void) { return &abi; }
