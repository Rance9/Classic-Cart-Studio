/* CCS Console Workbench native host: included (-include) before every libpar2 source and par2_abi.cpp. Copyright (C) 2026 Rance9.
 * GPL-3.0-or-later.
 *
 * par2cmdline's libpar2internal.h (its _WIN32 branch, written for MSVC) defines the macros snprintf -> _snprintf_s and unlink -> _unlink
 * BEFORE it includes the C++ standard headers; with clang's libc++ (zig) that macro breaks libc++'s own use of snprintf inside <locale>.
 * Including the standard headers first, here, lets their include guards skip them later, so the unmodified par2cmdline sources compile.
 * Nothing in this file changes par2cmdline's behavior. */
#ifndef CCS_PAR2_PRELUDE_H
#define CCS_PAR2_PRELUDE_H
#ifdef __cplusplus
#include <algorithm>
#include <atomic>
#include <cassert>
#include <cctype>
#include <chrono>
#include <climits>
#include <cstdint>
#include <cstdio>
#include <cstdlib>
#include <cstring>
#include <cwchar>
#include <cwctype>
#include <fstream>
#include <functional>
#include <iomanip>
#include <iostream>
#include <limits>
#include <list>
#include <locale>
#include <map>
#include <memory>
#include <set>
#include <sstream>
#include <stdexcept>
#include <string>
#include <utility>
#include <vector>
#endif
#endif
