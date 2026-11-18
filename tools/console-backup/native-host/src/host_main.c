/* CCS Console Workbench native host. Copyright (C) 2026 Rance9. GPL-3.0-or-later.
 *
 * One executable: this native program carries the managed Workbench (a .NET Framework 4.8 WPF executable, itself carrying its managed
 * libraries) as a resource and runs it inside its own process, and carries par2cmdline's libpar2 linked in (par2_abi.cpp). Nothing is
 * extracted: the managed bytes go from the resource straight to the CLR (AppDomain.Load(byte[])), and the managed code reaches the PAR2
 * functions through a table of function pointers the host hands it (ccs_host_abi.h).
 *
 *   start      CoInitializeEx(STA) on the main thread (WPF needs a single-threaded apartment; reflection does not apply [STAThread]);
 *              check the payload against the SHA-256 recorded at build time; check .NET Framework 4.8 (4.8.1 for the native Arm64 build);
 *              mscoree!CLRCreateInstance -> ICLRMetaHost::GetRuntime("v4.0.30319") -> ICLRRuntimeInfo::IsLoadable/GetInterface
 *              (ICorRuntimeHost) -> Start -> GetDefaultDomain -> _AppDomain::SetData(CcsNativeHost.Abi, &table)
 *              -> _AppDomain::Load_3(bytes) -> _Assembly::get_EntryPoint -> _MethodInfo::Invoke_3(null, [string[] args])
 *   end        ExitProcess(Main's return value) (not the runtime's managed shutdown: see the comment at the end of run_managed).
 *   --host-info   prints what the host found (JSON, reads only) without starting the runtime's managed code.
 * The managed program relaunches this same executable for its controlled worker (--backup-worker); the host treats that like any other
 * command line. Exit codes of the host itself (the managed program's own codes are passed through):
 *   90 .NET Framework 4.8 missing or unusable   91 payload damaged or missing   92 runtime would not start   93 managed program would not
 *   load   94 managed program ended with an unhandled exception */
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <shellapi.h>
#include <bcrypt.h>
#include <stdio.h>
#include <stdarg.h>
#include <wchar.h>
#include "clr_hosting.h"
#include "ccs_host_abi.h"
#include "host_version.h"

#define IDR_MANAGED_PAYLOAD  101
#define IDR_PAYLOAD_SHA256   102

#define EXIT_NETFX   90
#define EXIT_PAYLOAD 91
#define EXIT_CLR     92
#define EXIT_LOAD    93
#define EXIT_THREW   94

#define NETFX_48_RELEASE   528040u   /* .NET Framework 4.8 */
#define NETFX_481_RELEASE  533320u   /* .NET Framework 4.8.1, the first with a native Arm64 runtime */

#if defined(__aarch64__) || defined(_M_ARM64)
#define HOST_ARCH L"arm64"
#define NETFX_MIN NETFX_481_RELEASE
#define NETFX_MIN_NAME L"4.8.1"
#elif defined(__x86_64__) || defined(_M_X64)
#define HOST_ARCH L"x64"
#define NETFX_MIN NETFX_48_RELEASE
#define NETFX_MIN_NAME L"4.8"
#else
#error "the host is built for x64 and Arm64 only"
#endif

const wchar_t *ccs_host_arch(void) { return HOST_ARCH; }

/* ---------- output: stderr/stdout when the parent gave us handles, a message box when started from Explorer ---------- */

static HANDLE std_handle(DWORD which)
{
    HANDLE h = GetStdHandle(which);
    return (h == NULL || h == INVALID_HANDLE_VALUE) ? NULL : h;
}

static void write_utf8(HANDLE h, const wchar_t *text)
{
    if (!h || !text) return;
    int n = WideCharToMultiByte(CP_UTF8, 0, text, -1, NULL, 0, NULL, NULL);
    if (n <= 1) return;
    char *b = (char *)HeapAlloc(GetProcessHeap(), 0, (SIZE_T)n);
    if (!b) return;
    WideCharToMultiByte(CP_UTF8, 0, text, -1, b, n, NULL, NULL);
    DWORD w; WriteFile(h, b, (DWORD)(n - 1), &w, NULL);
    HeapFree(GetProcessHeap(), 0, b);
}

static int g_worker_mode;   /* --backup-worker: the window reads JSON lines from our standard output */

static void json_escape(const wchar_t *s, wchar_t *out, size_t cap)
{
    size_t o = 0;
    for (; *s && o + 7 < cap; s++)
    {
        wchar_t c = *s;
        if (c == L'"' || c == L'\\') { out[o++] = L'\\'; out[o++] = c; }
        else if (c < 0x20) { o += (size_t)swprintf(out + o, cap - o, L"\\u%04x", (unsigned)c); }
        else out[o++] = c;
    }
    out[o] = 0;
}

/* tell the person (or the window that started a worker) why the Workbench cannot start; returns code */
static int fail(int code, const wchar_t *fmt, ...)
{
    wchar_t text[2048];
    va_list ap; va_start(ap, fmt); vswprintf(text, sizeof text / sizeof text[0], fmt, ap); va_end(ap);
    HANDLE err = std_handle(STD_ERROR_HANDLE), out = std_handle(STD_OUTPUT_HANDLE);
    if (g_worker_mode && out)
    {
        wchar_t esc[4200], line[4400];
        json_escape(text, esc, sizeof esc / sizeof esc[0]);
        swprintf(line, sizeof line / sizeof line[0], L"{\"event\":\"error\",\"text\":\"%ls\"}\n", esc);
        write_utf8(out, line);
    }
    if (err) { write_utf8(err, text); write_utf8(err, L"\n"); }
    else if (!g_worker_mode) MessageBoxW(NULL, text, L"CCS Console Workbench", MB_OK | MB_ICONERROR | MB_SETFOREGROUND);
    return code;
}

/* ---------- the payload ---------- */

typedef struct { const BYTE *bytes; DWORD size; wchar_t expected_sha256[65]; wchar_t actual_sha256[65]; int ok; } payload_t;

static int sha256_hex(const BYTE *data, DWORD size, wchar_t hex[65])
{
    BCRYPT_ALG_HANDLE alg = NULL; BCRYPT_HASH_HANDLE h = NULL; UCHAR digest[32]; int ok = 0;
    if (BCryptOpenAlgorithmProvider(&alg, BCRYPT_SHA256_ALGORITHM, NULL, 0) != 0) return 0;
    if (BCryptCreateHash(alg, &h, NULL, 0, NULL, 0, 0) == 0 &&
        BCryptHashData(h, (PUCHAR)data, size, 0) == 0 &&
        BCryptFinishHash(h, digest, sizeof digest, 0) == 0)
    {
        static const wchar_t digits[] = L"0123456789abcdef";
        for (int i = 0; i < 32; i++) { hex[i * 2] = digits[digest[i] >> 4]; hex[i * 2 + 1] = digits[digest[i] & 15]; }
        hex[64] = 0; ok = 1;
    }
    if (h) BCryptDestroyHash(h);
    BCryptCloseAlgorithmProvider(alg, 0);
    return ok;
}

static const BYTE *resource(int id, DWORD *size)
{
    HRSRC r = FindResourceW(NULL, MAKEINTRESOURCEW(id), MAKEINTRESOURCEW(10) /* RT_RCDATA */);
    if (!r) return NULL;
    HGLOBAL g = LoadResource(NULL, r);
    if (!g) return NULL;
    *size = SizeofResource(NULL, r);
    return (const BYTE *)LockResource(g);
}

static void load_payload(payload_t *p)
{
    ZeroMemory(p, sizeof *p);
    DWORD hs = 0;
    const BYTE *h = resource(IDR_PAYLOAD_SHA256, &hs);
    if (h && hs >= 64) { for (int i = 0; i < 64; i++) p->expected_sha256[i] = (wchar_t)h[i]; p->expected_sha256[64] = 0; }
    p->bytes = resource(IDR_MANAGED_PAYLOAD, &p->size);
    if (p->bytes && p->size > 0 && sha256_hex(p->bytes, p->size, p->actual_sha256))
        p->ok = p->expected_sha256[0] && _wcsicmp(p->expected_sha256, p->actual_sha256) == 0;
}

/* ---------- .NET Framework detection (reads only; never installs) ---------- */

static DWORD netfx_release(void)
{
    /* test hook: CCS_HOST_SIMULATE_NETFX_RELEASE=<n> makes the host believe the registry said n (it can only make the check stricter
     * in practice: the runtime itself is still checked below); test_host.py uses it to show the "missing .NET" message path */
    wchar_t sim[16];
    DWORD sn = GetEnvironmentVariableW(L"CCS_HOST_SIMULATE_NETFX_RELEASE", sim, 16);
    if (sn > 0 && sn < 16) return (DWORD)wcstoul(sim, NULL, 10);
    DWORD v = 0, cb = sizeof v;
    if (RegGetValueW(HKEY_LOCAL_MACHINE, L"SOFTWARE\\Microsoft\\NET Framework Setup\\NDP\\v4\\Full", L"Release",
                     RRF_RT_REG_DWORD, NULL, &v, &cb) != ERROR_SUCCESS) return 0;
    return v;
}

static const wchar_t *netfx_name(DWORD r)
{
    if (r >= 533320) return L"4.8.1 or later";
    if (r >= 528040) return L"4.8";
    if (r >= 461808) return L"4.7.2";
    if (r >= 461308) return L"4.7.1";
    if (r >= 460798) return L"4.7";
    if (r >= 394802) return L"4.6.2";
    if (r >= 394254) return L"4.6.1";
    if (r >= 393295) return L"4.6";
    if (r >= 378389) return L"4.5.x";
    if (r > 0) return L"an earlier 4.x";
    return L"none";
}

/* the PC's own processor and whether this host runs under emulation (an x64 host on an Arm64 PC) */
static void machine_facts(const wchar_t **native, int *emulated)
{
    typedef BOOL (WINAPI *PFN_IsWow64Process2)(HANDLE, USHORT *, USHORT *);
    PFN_IsWow64Process2 f = (PFN_IsWow64Process2)(void *)GetProcAddress(GetModuleHandleW(L"kernel32.dll"), "IsWow64Process2");
    USHORT proc = 0, nat = 0;
    *native = L"unknown"; *emulated = 0;
    if (f && f(GetCurrentProcess(), &proc, &nat))
    {
        *native = nat == IMAGE_FILE_MACHINE_AMD64 ? L"x64" : nat == 0xAA64 /* ARM64 */ ? L"arm64" : nat == IMAGE_FILE_MACHINE_I386 ? L"x86" : L"other";
#if defined(__x86_64__)
        *emulated = nat == 0xAA64;
#endif
    }
}

/* ---------- the runtime ---------- */

typedef struct { HMODULE mscoree; void *meta; void *info; void *cor; wchar_t version[64]; wchar_t dir[MAX_PATH]; } clr_t;

static const wchar_t *hr_text(HRESULT hr, wchar_t *buf, size_t cap)
{
    buf[0] = 0;
    FormatMessageW(FORMAT_MESSAGE_FROM_SYSTEM | FORMAT_MESSAGE_IGNORE_INSERTS, NULL, (DWORD)hr, 0, buf, (DWORD)cap, NULL);
    size_t n = wcslen(buf); while (n && (buf[n - 1] == L'\n' || buf[n - 1] == L'\r' || buf[n - 1] == L' ')) buf[--n] = 0;
    return buf;
}

/* finds and checks the v4 runtime without starting it; returns 0 or an exit code (after telling the person) */
static int clr_find(clr_t *c, int quiet)
{
    ZeroMemory(c, sizeof *c);
    wchar_t e[512];
    c->mscoree = LoadLibraryExW(L"mscoree.dll", NULL, LOAD_LIBRARY_SEARCH_SYSTEM32);
    if (!c->mscoree)
        return quiet ? EXIT_NETFX : fail(EXIT_NETFX, L"CCS Console Workbench needs Microsoft .NET Framework %ls, which is part of Windows 10 and 11. "
                                     L"This PC does not have it (mscoree.dll is missing). Nothing was installed or changed.", NETFX_MIN_NAME);
    PFN_CLRCreateInstance create = (PFN_CLRCreateInstance)(void *)GetProcAddress(c->mscoree, "CLRCreateInstance");
    HRESULT hr = create ? create(&CCS_CLSID_CLRMetaHost, &CCS_IID_ICLRMetaHost, &c->meta) : E_NOTIMPL;
    if (FAILED(hr))
        return quiet ? EXIT_NETFX : fail(EXIT_NETFX, L"CCS Console Workbench could not reach Microsoft .NET Framework on this PC (0x%08lX: %ls). Nothing was installed or changed.",
                                     (unsigned long)hr, hr_text(hr, e, 512));
    hr = ((PFN_MetaHost_GetRuntime)CCS_VFN(c->meta, SLOT_ICLRMetaHost_GetRuntime))(c->meta, L"v4.0.30319", &CCS_IID_ICLRRuntimeInfo, &c->info);
    if (FAILED(hr))
        return quiet ? EXIT_NETFX : fail(EXIT_NETFX, L"CCS Console Workbench needs Microsoft .NET Framework %ls. This PC's .NET Framework 4 runtime is missing or turned off "
                                     L"(0x%08lX: %ls). Nothing was installed or changed.", NETFX_MIN_NAME, (unsigned long)hr, hr_text(hr, e, 512));
    DWORD n = 64; ((PFN_RuntimeInfo_GetString)CCS_VFN(c->info, SLOT_ICLRRuntimeInfo_GetVersionString))(c->info, c->version, &n);
    n = MAX_PATH; ((PFN_RuntimeInfo_GetString)CCS_VFN(c->info, SLOT_ICLRRuntimeInfo_GetRuntimeDirectory))(c->info, c->dir, &n);
    BOOL loadable = FALSE;
    hr = ((PFN_RuntimeInfo_IsLoadable)CCS_VFN(c->info, SLOT_ICLRRuntimeInfo_IsLoadable))(c->info, &loadable);
    if (FAILED(hr) || !loadable)
        return quiet ? EXIT_NETFX : fail(EXIT_NETFX, L"CCS Console Workbench cannot use this PC's .NET Framework %ls runtime in its process (0x%08lX). Nothing was installed or changed.",
                                     c->version, (unsigned long)hr);
    return 0;
}

/* ---------- --host-info (reads only) ---------- */

static void print_host_info(const payload_t *p)
{
    wchar_t exe[MAX_PATH * 2]; GetModuleFileNameW(NULL, exe, MAX_PATH * 2);
    wchar_t exe_j[MAX_PATH * 4]; json_escape(exe, exe_j, MAX_PATH * 4);
    const wchar_t *native; int emulated; machine_facts(&native, &emulated);
    DWORD rel = netfx_release();
    clr_t c; int clr_rc = clr_find(&c, 1);
    wchar_t dir_j[MAX_PATH * 2]; json_escape(c.dir, dir_j, MAX_PATH * 2);
    wchar_t text[8192];
    swprintf(text, sizeof text / sizeof text[0],
             L"{\"schema\":\"ccs-native-host-info/1\",\"host_version\":\"%ls\",\"host_arch\":\"%ls\",\"pc_arch\":\"%ls\",\"emulated\":%ls,"
             L"\"exe\":\"%ls\",\"payload_bytes\":%lu,\"payload_sha256\":\"%ls\",\"payload_expected_sha256\":\"%ls\",\"payload_ok\":%ls,"
             L"\"netfx_release\":%lu,\"netfx\":\"%ls\",\"netfx_required\":\"%ls\",\"netfx_ok\":%ls,\"clr_version\":\"%ls\",\"clr_dir\":\"%ls\",\"clr_loadable\":%ls,"
             L"\"par2\":\"%ls\",\"abi_version\":%u}\n",
             CCS_HOST_VERSION_W, HOST_ARCH, native, emulated ? L"true" : L"false", exe_j, (unsigned long)p->size, p->actual_sha256, p->expected_sha256,
             p->ok ? L"true" : L"false", (unsigned long)rel, netfx_name(rel), NETFX_MIN_NAME, rel >= NETFX_MIN ? L"true" : L"false", c.version, dir_j,
             clr_rc == 0 ? L"true" : L"false", ccs_host_get_abi()->par2_version, CCS_HOST_ABI_VERSION);
    write_utf8(std_handle(STD_OUTPUT_HANDLE), text);
}

/* ---------- run the managed program ---------- */

static int run_managed(const payload_t *p, int argc, wchar_t **argv)
{
    clr_t c;
    int rc = clr_find(&c, 0);
    if (rc) return rc;
    wchar_t e[512];
    HRESULT hr = ((PFN_RuntimeInfo_GetInterface)CCS_VFN(c.info, SLOT_ICLRRuntimeInfo_GetInterface))(c.info, &CCS_CLSID_CorRuntimeHost, &CCS_IID_ICorRuntimeHost, &c.cor);
    if (SUCCEEDED(hr)) hr = ((PFN_CorHost_Start)CCS_VFN(c.cor, SLOT_ICorRuntimeHost_Start))(c.cor);
    if (FAILED(hr)) return fail(EXIT_CLR, L"CCS Console Workbench could not start .NET Framework %ls (0x%08lX: %ls).", c.version, (unsigned long)hr, hr_text(hr, e, 512));

    IUnknown *unk = NULL; void *domain = NULL;
    hr = ((PFN_CorHost_GetDefaultDomain)CCS_VFN(c.cor, SLOT_ICorRuntimeHost_GetDefaultDomain))(c.cor, &unk);
    if (SUCCEEDED(hr)) hr = ccs_query(unk, &CCS_IID__AppDomain, &domain);
    ccs_release(unk);
    if (FAILED(hr)) return fail(EXIT_CLR, L"CCS Console Workbench could not open the .NET application domain (0x%08lX: %ls).", (unsigned long)hr, hr_text(hr, e, 512));

    /* the one pointer the managed side receives: the ABI table, as AppDomain data (a boxed Int64; process-local, never inherited) */
    VARIANT abi; VariantInit(&abi); abi.vt = VT_I8; abi.llVal = (LONGLONG)(INT_PTR)ccs_host_get_abi();
    BSTR abi_name = SysAllocString(CCS_HOST_ABI_DATA_NAME);
    hr = ((PFN_AppDomain_SetData)CCS_VFN(domain, SLOT__AppDomain_SetData))(domain, abi_name, abi);
    SysFreeString(abi_name);
    if (FAILED(hr)) return fail(EXIT_CLR, L"CCS Console Workbench could not pass its parity functions to the managed program (0x%08lX).", (unsigned long)hr);

    /* the managed executable's bytes, from the resource straight into a SAFEARRAY for Load(byte[]) (nothing on disk) */
    SAFEARRAY *raw = SafeArrayCreateVector(VT_UI1, 0, p->size);
    if (!raw) return fail(EXIT_LOAD, L"CCS Console Workbench ran out of memory while starting.");
    void *dst = NULL;
    SafeArrayAccessData(raw, &dst); memcpy(dst, p->bytes, p->size); SafeArrayUnaccessData(raw);
    void *assembly = NULL;
    hr = ((PFN_AppDomain_Load_3)CCS_VFN(domain, SLOT__AppDomain_Load_3))(domain, raw, &assembly);
    SafeArrayDestroy(raw);
    if (FAILED(hr)) return fail(EXIT_LOAD, L"CCS Console Workbench could not load its program (0x%08lX: %ls).", (unsigned long)hr, hr_text(hr, e, 512));
    void *entry = NULL;
    hr = ((PFN_Assembly_get_EntryPoint)CCS_VFN(assembly, SLOT__Assembly_get_EntryPoint))(assembly, &entry);
    if (FAILED(hr) || !entry) return fail(EXIT_LOAD, L"CCS Console Workbench's program has no entry point (0x%08lX).", (unsigned long)hr);

    /* Main(string[] args): args without the executable's own name, split as Windows splits them (CommandLineToArgvW) */
    SAFEARRAY *sargs = SafeArrayCreateVector(VT_BSTR, 0, (ULONG)(argc > 1 ? argc - 1 : 0));
    for (LONG i = 1; i < argc; i++) { BSTR b = SysAllocString(argv[i]); LONG ix = i - 1; SafeArrayPutElement(sargs, &ix, b); SysFreeString(b); }
    SAFEARRAY *params = SafeArrayCreateVector(VT_VARIANT, 0, 1);
    VARIANT va; VariantInit(&va); va.vt = VT_ARRAY | VT_BSTR; va.parray = sargs;
    LONG zero = 0; SafeArrayPutElement(params, &zero, &va);   /* copies the array */
    SafeArrayDestroy(sargs);
    VARIANT target; VariantInit(&target); target.vt = VT_NULL;   /* a static method */
    VARIANT ret; VariantInit(&ret);
    hr = ((PFN_MethodInfo_Invoke_3)CCS_VFN(entry, SLOT__MethodInfo_Invoke_3))(entry, target, params, &ret);
    SafeArrayDestroy(params);
    int code;
    if (FAILED(hr))
    {
        /* an exception the managed program did not handle: reflection returns it as TargetInvocationException */
        wchar_t desc[1024] = L"";
        IErrorInfo *ei = NULL;
        if (GetErrorInfo(0, &ei) == S_OK && ei)
        {
            BSTR d = NULL;
            if (SUCCEEDED(ei->lpVtbl->GetDescription(ei, &d)) && d) { lstrcpynW(desc, d, 1024); SysFreeString(d); }
            ei->lpVtbl->Release(ei);
        }
        code = fail(EXIT_THREW, L"CCS Console Workbench stopped unexpectedly (0x%08lX). %ls", (unsigned long)hr, desc);
    }
    else code = ret.vt == VT_I4 ? (int)ret.lVal : 0;
    VariantClear(&ret);
    ccs_release(entry); ccs_release(assembly); ccs_release(domain);
    /* End the process with Main's code through the operating system's ExitProcess; the runtime then only sees DLL_PROCESS_DETACH.
     * Measured (NATIVE-HOST.md, "Ending the process"): the runtime's own managed shutdown, entered from this host through
     * ICLRMetaHost::ExitProcess or mscoree/clr CorExitProcess, replaced Main's code with HOST_E_EXITPROCESS_TIMEOUT (0x80131029) in
     * most runs on a fully loaded CPU (also with ICLRPolicyManager set to take no action on finalizer timeouts), while the same managed
     * program started directly never did; this path kept the code in every run. What it skips: AppDomain.ProcessExit handlers and the
     * finalizers .NET Framework runs at shutdown. The Workbench registers no ProcessExit handler and closes and flushes its files itself. */
    ExitProcess((UINT)code);
    return code;   /* not reached */
}

int WINAPI wWinMain(HINSTANCE instance, HINSTANCE previous, LPWSTR command_line, int show)
{
    (void)instance; (void)previous; (void)command_line; (void)show;
    /* no DLL is ever looked for in the current folder (mscoree.dll itself is loaded from System32 only, below). The process-wide
     * SetDefaultDllDirectories(System32) is NOT used: the runtime and WPF load their own DLLs in ways that setting would break. */
    SetDllDirectoryW(L"");
    int argc = 0;
    wchar_t **argv = CommandLineToArgvW(GetCommandLineW(), &argc);
    if (!argv) return fail(EXIT_PAYLOAD, L"CCS Console Workbench could not read its command line.");
    for (int i = 1; i < argc; i++) if (wcscmp(argv[i], L"--backup-worker") == 0) g_worker_mode = 1;

    payload_t p; load_payload(&p);
    if (argc > 1 && wcscmp(argv[1], L"--host-info") == 0) { print_host_info(&p); return p.ok ? 0 : EXIT_PAYLOAD; }
    if (!p.bytes || !p.size) return fail(EXIT_PAYLOAD, L"This copy of CCS Console Workbench is incomplete (its program is missing). Please download it again.");
    if (!p.ok) return fail(EXIT_PAYLOAD, L"This copy of CCS Console Workbench is damaged (its program does not match its recorded fingerprint). Please download it again.");

    DWORD rel = netfx_release();
    if (rel < NETFX_MIN)
    {
        const wchar_t *native; int emulated; machine_facts(&native, &emulated);
#if defined(__aarch64__)
        return fail(EXIT_NETFX, L"This Arm64 edition of CCS Console Workbench needs Microsoft .NET Framework 4.8.1 or later (included with Windows 11 2022 Update and later). "
                                L"This PC has: %ls. The x64 edition can run on this PC with .NET Framework 4.8. Nothing was installed or changed.", netfx_name(rel));
#else
        return fail(EXIT_NETFX, L"CCS Console Workbench needs Microsoft .NET Framework 4.8 or later (included with Windows 10 May 2019 Update and later, and Windows 11). "
                                L"This PC has: %ls. Nothing was installed or changed.", netfx_name(rel));
#endif
    }
    /* WPF needs a single-threaded apartment; reflection's Invoke does not apply Main's [STAThread], so the host sets it here */
    HRESULT hr = CoInitializeEx(NULL, COINIT_APARTMENTTHREADED);
    if (FAILED(hr)) return fail(EXIT_CLR, L"CCS Console Workbench could not prepare its window thread (0x%08lX).", (unsigned long)hr);
    return run_managed(&p, argc, argv);
}
