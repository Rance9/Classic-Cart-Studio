/* CCS Console Workbench native host. Copyright (C) 2026 Rance9. GPL-3.0-or-later.
 *
 * One executable: this native program carries the managed Workbench (a .NET Framework 4.8 WPF executable, itself carrying its managed
 * libraries) as a resource and runs it inside its own process, and carries par2cmdline's libpar2 linked in (par2_abi.cpp). Nothing is
 * extracted: the managed bytes go from the resource straight to the CLR (AppDomain.Load(byte[])), and the managed code reaches the PAR2
 * functions through a table of function pointers the host hands it (ccs_host_abi.h).
 *
 *   start      check the payload against the SHA-256 recorded at build time (a damaged copy is never taken for a missing part of Windows);
 *              check what this edition needs from the PC (below); CoInitializeEx(STA) on the main thread (WPF needs a single-threaded
 *              apartment; reflection does not apply [STAThread]);
 *              mscoree!CLRCreateInstance -> ICLRMetaHost::GetRuntime("v4.0.30319") -> ICLRRuntimeInfo::IsLoadable/GetInterface
 *              (ICorRuntimeHost) -> Start -> GetDefaultDomain -> _AppDomain::SetData(CcsNativeHost.Abi, &table)
 *              -> _AppDomain::Load_3(bytes) -> _Assembly::get_EntryPoint -> _MethodInfo::Invoke_3(null, [string[] args])
 *   end        ExitProcess(Main's return value) (not the runtime's managed shutdown: see the comment at the end of run_managed).
 *   --host-info   prints what the host found (JSON, reads only) without starting the runtime's managed code.
 * The managed program relaunches this same executable for its controlled worker (--backup-worker); the host treats that like any other
 * command line.
 *
 * What this edition needs from the PC (read, never changed): Windows 10 or 11, and Microsoft .NET Framework 4.8 or later for the x64
 * edition, 4.8.1 or later for the native Arm64 edition (the first with a native Arm64 runtime, which Microsoft makes for Windows 11), whose
 * runtime this process can load. The registry's Release value is read first; the runtime itself is checked only when that value is high
 * enough (so nothing of a missing or old runtime is held open while the person installs one).
 *   An interactive start (a window: no command below, output not redirected to a file or pipe) that finds something missing shows the
 *   setup window (host_setup.c): Download Installer opens Microsoft's page for the needed version in the person's browser, Continue
 *   checks again (registry and runtime) and, once everything is there, goes on with this same start and its arguments, Cancel ends it.
 *   It never downloads, installs, elevates, restarts or registers anything. Only one setup window at a time: a second start brings it
 *   forward and ends. A damaged copy and a start that fails later get the same window with accurate words instead of a message box.
 *   Worker, report and other commands (NONINTERACTIVE below) and redirected output never show a window or wait: the same words go to
 *   standard error (a worker also gets them as a JSON error event on standard output) and the exit code says what happened.
 * Exit codes of the host itself (the managed program's own codes are passed through):
 *   90 the .NET Framework this edition needs is missing or unusable (also: closed the setup window)   91 payload damaged or missing
 *   92 runtime would not start   93 managed program would not load   94 managed program ended with an unhandled exception
 *
 * Test hooks (they can only make the checks stricter or simulate a failure, never let a missing or unusable runtime through):
 *   CCS_HOST_SIMULATE_NETFX_RELEASE=<n>  the registry's Release value is taken as the LOWER of n and the real one
 *   CCS_HOST_SIMULATE_FILE=<file>        read again at every check (so a test can change it while the setup window is open); lines:
 *     netfx_release=<n>          as above
 *     os=<major>.<minor>.<build> Windows is taken to be that version when it is LOWER than the real one
 *     rules=arm64                an x64 build applies the Arm64 edition's (higher) requirements
 *     clr=missing | unusable     the runtime check fails at that step (mscoree.dll not found; the runtime cannot be opened)
 *     restart_pending=1          Windows is taken to have a restart waiting       internet=none   no internet connection
 *     browser=fail               opening the page fails                           start=fail      starting the runtime fails
 *   Every simulated value is listed under Technical details and in --host-info. */
#define WIN32_LEAN_AND_MEAN
#define COBJMACROS
#include <windows.h>
#include <shellapi.h>
#include <bcrypt.h>
#include <netlistmgr.h>
#include <stdio.h>
#include <stdarg.h>
#include <wchar.h>
#include "clr_hosting.h"
#include "ccs_host_abi.h"
#include "host_setup.h"
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

/* Microsoft's own version-specific download pages (each offers the Runtime, the Developer Pack and an offline Runtime installer), and
 * the short form a person can type on another PC (it leads to the same page) */
#define URL_NETFX48     L"https://dotnet.microsoft.com/en-us/download/dotnet-framework/net48"
#define URL_NETFX481    L"https://dotnet.microsoft.com/en-us/download/dotnet-framework/net481"
#define TYPED_NETFX48   L"dotnet.microsoft.com/download/dotnet-framework/net48"
#define TYPED_NETFX481  L"dotnet.microsoft.com/download/dotnet-framework/net481"
#define URL_WORKBENCH   L"https://classiccartstudio.com/app/"   /* the official download page (WorkbenchUpdates.DownloadPage) */

/* what each edition needs, from Microsoft's system requirements: 4.8 is made for Windows 10 version 1607 (build 14393) and later and is
 * part of Windows from Windows 10 version 1903 (build 18362); 4.8.1 is made for Windows 10 version 20H2 and later and Windows 11, is
 * part of Windows from Windows 11 version 22H2 (build 22621), and its native Arm64 runtime is for Windows 11 (build 22000) */
typedef struct {
    const wchar_t *edition;       /* this file's edition, in words */
    DWORD min_release;            /* the .NET Framework Release value it needs */
    const wchar_t *need;          /* that version, in words */
    const wchar_t *url, *typed;   /* Microsoft's page for that version */
    DWORD made_from;              /* the first Windows build Microsoft makes that version for (for this edition) */
    DWORD part_from;              /* the first Windows build that includes it */
} rule_t;
static const rule_t RULE_X64 __attribute__((unused)) = { L"x64", NETFX_48_RELEASE, L"4.8", URL_NETFX48, TYPED_NETFX48, 14393, 18362 };   /* (the Arm64 build has no use for it) */
static const rule_t RULE_ARM64 = { L"Arm64", NETFX_481_RELEASE, L"4.8.1", URL_NETFX481, TYPED_NETFX481, 22000, 22621 };
#if defined(__aarch64__) || defined(_M_ARM64)
#define RULE_OWN RULE_ARM64
#else
#define RULE_OWN RULE_X64
#endif

/* commands that never show a window and never wait for a person (Program.cs and the host's own --host-info) */
static const wchar_t *const NONINTERACTIVE[] = { L"--host-info", L"--backup-worker", L"--driver-action", L"--raw-disk", L"--scheduled-check",
    L"--version", L"--engine-info", L"--design-compile", L"--home-render-selftest", L"--parity-selftest" };
#define NONINTERACTIVE_COUNT (sizeof NONINTERACTIVE / sizeof NONINTERACTIVE[0])

/* ---------- output: stderr/stdout when the parent gave us handles; the setup window for an interactive start ---------- */

static HANDLE std_handle(DWORD which)
{
    HANDLE h = GetStdHandle(which);
    return (h == NULL || h == INVALID_HANDLE_VALUE) ? NULL : h;
}

/* a file or a pipe: someone (a program, a script) reads the output; a console or nothing: a person */
static int redirected(DWORD which)
{
    HANDLE h = std_handle(which);
    if (!h) return 0;
    DWORD t = GetFileType(h);
    return t == FILE_TYPE_DISK || t == FILE_TYPE_PIPE;
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

static int g_worker_mode;     /* --backup-worker: the window reads JSON lines from our standard output */
static int g_noninteractive;  /* no window, no waiting (NONINTERACTIVE, or redirected output) */
static int g_studio_launch;   /* the copy Classic Cart Studio installed, started by Studio (--studio-launch) */

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

static void say(const wchar_t *text)   /* the noninteractive output: a worker's JSON error event, and standard error */
{
    HANDLE err = std_handle(STD_ERROR_HANDLE), out = std_handle(STD_OUTPUT_HANDLE);
    if (g_worker_mode && out)
    {
        wchar_t esc[4200], line[4400];
        json_escape(text, esc, sizeof esc / sizeof esc[0]);
        swprintf(line, sizeof line / sizeof line[0], L"{\"event\":\"error\",\"text\":\"%ls\"}\n", esc);
        write_utf8(out, line);
    }
    if (err) { write_utf8(err, text); write_utf8(err, L"\n"); }
}

static int show_window(setup_view *v, setup_recheck_fn recheck, void *ctx);
static void start_problem_view(setup_view *v, int code, const wchar_t *text);

/* tell the person (or the window that started a worker) why the Workbench cannot start; returns code */
static int fail(int code, const wchar_t *fmt, ...)
{
    wchar_t text[2048];
    va_list ap; va_start(ap, fmt); vswprintf(text, sizeof text / sizeof text[0], fmt, ap); va_end(ap);
    say(text);
    if (!g_noninteractive)
    {
        static setup_view v;
        start_problem_view(&v, code, text);
        show_window(&v, NULL, NULL);
    }
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

/* ---------- test hooks (stricter only; see the top of this file) ---------- */

typedef struct {
    wchar_t list[480];                      /* what was simulated, for Technical details and --host-info */
    int has_release; DWORD release;
    int has_os; DWORD major, minor, build;
    int arm64_rules, clr_missing, clr_unusable, restart, offline, browser_fail, start_fail;
} sim_t;

static void sim_note(sim_t *s, const wchar_t *what)
{
    size_t n = wcslen(s->list);
    if (n + wcslen(what) + 3 >= sizeof s->list / sizeof s->list[0]) return;
    if (n) wcscat(s->list, L"; ");
    wcscat(s->list, what);
}

static void sim_read(sim_t *s)
{
    ZeroMemory(s, sizeof *s);
    wchar_t v[64];
    DWORD n = GetEnvironmentVariableW(L"CCS_HOST_SIMULATE_NETFX_RELEASE", v, 64);
    if (n > 0 && n < 64) { s->has_release = 1; s->release = (DWORD)wcstoul(v, NULL, 10); swprintf(v, 64, L"netfx_release=%lu", (unsigned long)s->release); sim_note(s, v); }
    wchar_t path[MAX_PATH * 2];
    n = GetEnvironmentVariableW(L"CCS_HOST_SIMULATE_FILE", path, MAX_PATH * 2);
    if (n == 0 || n >= MAX_PATH * 2) return;
    /* read again at every check; a missing or unreadable file simulates nothing (the real checks apply) */
    HANDLE f = CreateFileW(path, GENERIC_READ, FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE, NULL, OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, NULL);
    if (f == INVALID_HANDLE_VALUE) return;
    char buf[4097]; DWORD got = 0;
    BOOL ok = ReadFile(f, buf, 4096, &got, NULL);
    CloseHandle(f);
    if (!ok) return;
    buf[got] = 0;
    for (char *line = buf; line && *line; )
    {
        char *next = strpbrk(line, "\r\n");
        if (next) { *next++ = 0; while (*next == '\r' || *next == '\n') next++; }
        char *eq = strchr(line, '=');
        if (eq)
        {
            *eq = 0; const char *k = line, *val = eq + 1;
            wchar_t note[128] = L"";
            if (!strcmp(k, "netfx_release")) { DWORD r = (DWORD)strtoul(val, NULL, 10); if (!s->has_release || r < s->release) s->release = r; s->has_release = 1; swprintf(note, 128, L"netfx_release=%lu", (unsigned long)r); }
            else if (!strcmp(k, "os")) { unsigned a = 0, b = 0, c = 0; if (sscanf(val, "%u.%u.%u", &a, &b, &c) == 3) { s->has_os = 1; s->major = a; s->minor = b; s->build = c; swprintf(note, 128, L"os=%u.%u.%u", a, b, c); } }
            else if (!strcmp(k, "rules") && !strcmp(val, "arm64")) { s->arm64_rules = 1; wcscpy(note, L"rules=arm64"); }
            else if (!strcmp(k, "clr") && !strcmp(val, "missing")) { s->clr_missing = 1; wcscpy(note, L"clr=missing"); }
            else if (!strcmp(k, "clr") && !strcmp(val, "unusable")) { s->clr_unusable = 1; wcscpy(note, L"clr=unusable"); }
            else if (!strcmp(k, "restart_pending") && !strcmp(val, "1")) { s->restart = 1; wcscpy(note, L"restart_pending=1"); }
            else if (!strcmp(k, "internet") && !strcmp(val, "none")) { s->offline = 1; wcscpy(note, L"internet=none"); }
            else if (!strcmp(k, "browser") && !strcmp(val, "fail")) { s->browser_fail = 1; wcscpy(note, L"browser=fail"); }
            else if (!strcmp(k, "start") && !strcmp(val, "fail")) { s->start_fail = 1; wcscpy(note, L"start=fail"); }
            if (note[0]) sim_note(s, note);
        }
        line = next;
    }
}

/* ---------- what this PC has (reads only; never installs) ---------- */

static DWORD netfx_registry(int *found)
{
    DWORD v = 0, cb = sizeof v;
    *found = RegGetValueW(HKEY_LOCAL_MACHINE, L"SOFTWARE\\Microsoft\\NET Framework Setup\\NDP\\v4\\Full", L"Release",
                          RRF_RT_REG_DWORD, NULL, &v, &cb) == ERROR_SUCCESS;
    return *found ? v : 0;
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

/* Windows' own version (ntdll's RtlGetVersion: not affected by compatibility settings) */
static void windows_version(DWORD *major, DWORD *minor, DWORD *build)
{
    typedef LONG (WINAPI *PFN_RtlGetVersion)(OSVERSIONINFOW *);
    PFN_RtlGetVersion f = (PFN_RtlGetVersion)(void *)GetProcAddress(GetModuleHandleW(L"ntdll.dll"), "RtlGetVersion");
    OSVERSIONINFOW v; ZeroMemory(&v, sizeof v); v.dwOSVersionInfoSize = sizeof v;
    *major = *minor = *build = 0;
    if (f && f(&v) == 0) { *major = v.dwMajorVersion; *minor = v.dwMinorVersion; *build = v.dwBuildNumber; }
}

/* a restart Windows is waiting for (any of Windows' own markers; reads only) */
static int restart_waiting(wchar_t *why, size_t cap)
{
    why[0] = 0;
    HKEY k;
    if (RegOpenKeyExW(HKEY_LOCAL_MACHINE, L"SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Component Based Servicing\\RebootPending", 0, KEY_READ | KEY_WOW64_64KEY, &k) == ERROR_SUCCESS)
    { RegCloseKey(k); wcsncat(why, L"Windows servicing; ", cap - wcslen(why) - 1); }
    if (RegOpenKeyExW(HKEY_LOCAL_MACHINE, L"SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\WindowsUpdate\\Auto Update\\RebootRequired", 0, KEY_READ | KEY_WOW64_64KEY, &k) == ERROR_SUCCESS)
    { RegCloseKey(k); wcsncat(why, L"Windows Update; ", cap - wcslen(why) - 1); }
    DWORD cb = 0;
    if (RegGetValueW(HKEY_LOCAL_MACHINE, L"SYSTEM\\CurrentControlSet\\Control\\Session Manager", L"PendingFileRenameOperations", RRF_RT_REG_MULTI_SZ, NULL, NULL, &cb) == ERROR_SUCCESS && cb > 4)
        wcsncat(why, L"files waiting to be replaced; ", cap - wcslen(why) - 1);
    size_t n = wcslen(why); if (n >= 2) why[n - 2] = 0;
    return why[0] != 0;
}

/* ---------- the runtime ---------- */

typedef struct { HMODULE mscoree; void *meta; void *info; void *cor; wchar_t version[64]; wchar_t dir[MAX_PATH]; int step; HRESULT hr; } clr_t;

static const wchar_t *hr_text(HRESULT hr, wchar_t *buf, size_t cap)
{
    buf[0] = 0;
    FormatMessageW(FORMAT_MESSAGE_FROM_SYSTEM | FORMAT_MESSAGE_IGNORE_INSERTS, NULL, (DWORD)hr, 0, buf, (DWORD)cap, NULL);
    size_t n = wcslen(buf); while (n && (buf[n - 1] == L'\n' || buf[n - 1] == L'\r' || buf[n - 1] == L' ' || buf[n - 1] == L'.')) buf[--n] = 0;
    return buf;
}

/* an HRESULT as "0x80070005: Access is denied", or only the code when Windows has no words for it */
static const wchar_t *hr_words(HRESULT hr, wchar_t *buf, size_t cap)
{
    wchar_t t[400]; hr_text(hr, t, 400);
    if (t[0]) swprintf(buf, cap, L"0x%08lX: %ls", (unsigned long)hr, t); else swprintf(buf, cap, L"0x%08lX", (unsigned long)hr);
    return buf;
}

/* the step at which the runtime check failed, in words */
static const wchar_t *clr_step_words(int step)
{
    switch (step)
    {
    case 1: return L"mscoree.dll, the part of Windows that starts .NET Framework, was not found";
    case 2: return L"Windows could not open its list of .NET Framework runtimes";
    case 3: return L"the .NET Framework 4 runtime (v4.0.30319) is missing or turned off";
    case 4: return L"the .NET Framework 4 runtime cannot be loaded into this program";
    default: return L"the .NET Framework 4 runtime is ready";
    }
}

/* finds and checks the v4 runtime without starting it: 0, or the step that failed (1 mscoree.dll, 2 CLRCreateInstance, 3 GetRuntime,
 * 4 IsLoadable) with its HRESULT in c->hr */
static int clr_find(clr_t *c)
{
    ZeroMemory(c, sizeof *c);
    c->mscoree = LoadLibraryExW(L"mscoree.dll", NULL, LOAD_LIBRARY_SEARCH_SYSTEM32);
    if (!c->mscoree) { c->hr = HRESULT_FROM_WIN32(GetLastError()); return c->step = 1; }
    PFN_CLRCreateInstance create = (PFN_CLRCreateInstance)(void *)GetProcAddress(c->mscoree, "CLRCreateInstance");
    HRESULT hr = create ? create(&CCS_CLSID_CLRMetaHost, &CCS_IID_ICLRMetaHost, &c->meta) : E_NOTIMPL;
    if (FAILED(hr)) { c->hr = hr; return c->step = 2; }
    hr = ((PFN_MetaHost_GetRuntime)CCS_VFN(c->meta, SLOT_ICLRMetaHost_GetRuntime))(c->meta, L"v4.0.30319", &CCS_IID_ICLRRuntimeInfo, &c->info);
    if (FAILED(hr)) { c->hr = hr; return c->step = 3; }
    DWORD n = 64; ((PFN_RuntimeInfo_GetString)CCS_VFN(c->info, SLOT_ICLRRuntimeInfo_GetVersionString))(c->info, c->version, &n);
    n = MAX_PATH; ((PFN_RuntimeInfo_GetString)CCS_VFN(c->info, SLOT_ICLRRuntimeInfo_GetRuntimeDirectory))(c->info, c->dir, &n);
    BOOL loadable = FALSE;
    hr = ((PFN_RuntimeInfo_IsLoadable)CCS_VFN(c->info, SLOT_ICLRRuntimeInfo_IsLoadable))(c->info, &loadable);
    if (FAILED(hr) || !loadable) { c->hr = FAILED(hr) ? hr : S_FALSE; return c->step = 4; }
    return 0;
}

typedef struct { int probed, step, simulated; HRESULT hr; wchar_t version[64]; wchar_t dir[MAX_PATH]; } clr_report;

/* the runtime check as a report; nothing stays held after a failure (so a repair or an installation is not kept waiting on it) */
static void clr_probe(clr_report *r, const sim_t *sim)
{
    ZeroMemory(r, sizeof *r);
    r->probed = 1;
    if (sim->clr_missing) { r->step = 1; r->hr = HRESULT_FROM_WIN32(ERROR_MOD_NOT_FOUND); r->simulated = 1; return; }
    if (sim->clr_unusable) { r->step = 3; r->hr = (HRESULT)0x80131700; r->simulated = 1; return; }
    clr_t c;
    r->step = clr_find(&c); r->hr = c.hr;
    lstrcpynW(r->version, c.version, 64); lstrcpynW(r->dir, c.dir, MAX_PATH);
    ccs_release(c.info); ccs_release(c.meta);
    if (r->step && c.mscoree) FreeLibrary(c.mscoree);
}

/* ---------- the facts and what they mean ---------- */

typedef struct {
    const rule_t *rule;
    DWORD major, minor, build, real_major, real_minor, real_build, ubr; wchar_t display[32];
    DWORD release, real_release; int release_found;
    const wchar_t *native; int emulated;
    clr_report clr;
    int restart; wchar_t restart_why[160];
    int internet;   /* 1 connected, 0 not connected, -1 not known, -2 not asked */
    sim_t sim;
} facts_t;

enum { PRE_READY, PRE_MISSING, PRE_NOT_FOUND, PRE_UNUSABLE, PRE_WINDOWS };
static const wchar_t *const PRE_NAMES[] = { L"ready", L"netfx-missing", L"netfx-not-found", L"runtime-unusable", L"windows-unsupported" };

/* probe: 0 no runtime check, 1 the runtime check when the registry value is high enough (Windows 10 or later), 2 always (--host-info) */
static void gather(facts_t *f, int probe)
{
    ZeroMemory(f, sizeof *f);
    sim_read(&f->sim);
    f->rule = f->sim.arm64_rules ? &RULE_ARM64 : &RULE_OWN;
    windows_version(&f->real_major, &f->real_minor, &f->real_build);
    f->major = f->real_major; f->minor = f->real_minor; f->build = f->real_build;
    if (f->sim.has_os && (f->sim.major < f->major || (f->sim.major == f->major && (f->sim.minor < f->minor || (f->sim.minor == f->minor && f->sim.build < f->build)))))
    { f->major = f->sim.major; f->minor = f->sim.minor; f->build = f->sim.build; }
    DWORD cb = sizeof f->ubr;
    RegGetValueW(HKEY_LOCAL_MACHINE, L"SOFTWARE\\Microsoft\\Windows NT\\CurrentVersion", L"UBR", RRF_RT_REG_DWORD, NULL, &f->ubr, &cb);
    cb = sizeof f->display;
    if (RegGetValueW(HKEY_LOCAL_MACHINE, L"SOFTWARE\\Microsoft\\Windows NT\\CurrentVersion", L"DisplayVersion", RRF_RT_REG_SZ, NULL, f->display, &cb) != ERROR_SUCCESS)
    { cb = sizeof f->display; RegGetValueW(HKEY_LOCAL_MACHINE, L"SOFTWARE\\Microsoft\\Windows NT\\CurrentVersion", L"ReleaseId", RRF_RT_REG_SZ, NULL, f->display, &cb); }
    f->real_release = f->release = netfx_registry(&f->release_found);
    if (f->sim.has_release && f->sim.release < f->release) f->release = f->sim.release;   /* only ever lower */
    machine_facts(&f->native, &f->emulated);
    f->restart = restart_waiting(f->restart_why, sizeof f->restart_why / sizeof f->restart_why[0]);
    if (f->sim.restart && !f->restart) { f->restart = 1; lstrcpynW(f->restart_why, L"simulated", 160); }
    f->internet = -2;
    if (probe == 2 || (probe == 1 && f->major >= 10 && f->release >= f->rule->min_release)) clr_probe(&f->clr, &f->sim);
}

static int decide(const facts_t *f)
{
    if (f->major < 10) return PRE_WINDOWS;
    if (f->release < f->rule->min_release)
    {
        if (f->build < f->rule->made_from) return PRE_WINDOWS;      /* Microsoft does not make the needed version for this Windows */
        if (f->build >= f->rule->part_from) return PRE_NOT_FOUND;   /* part of this Windows, yet not found */
        return PRE_MISSING;                                          /* installable from Microsoft's page */
    }
    if (f->clr.probed && f->clr.step) return PRE_UNUSABLE;
    return PRE_READY;
}

/* whether this PC is connected to the internet, as Windows' network list sees it (COM; the setup window only) */
static int internet_state(const sim_t *sim)
{
    if (sim->offline) return 0;
    static const GUID CLSID_NLM = { 0xdcb00c01, 0x570f, 0x4a9b, { 0x8d, 0x69, 0x19, 0x9f, 0xdb, 0xa5, 0x72, 0x3b } };
    static const GUID IID_NLM = { 0xdcb00000, 0x570f, 0x4a9b, { 0x8d, 0x69, 0x19, 0x9f, 0xdb, 0xa5, 0x72, 0x3b } };
    INetworkListManager *m = NULL;
    if (FAILED(CoCreateInstance(&CLSID_NLM, NULL, CLSCTX_ALL, &IID_NLM, (void **)&m)) || !m) return -1;
    NLM_CONNECTIVITY c = NLM_CONNECTIVITY_DISCONNECTED;
    HRESULT hr = INetworkListManager_GetConnectivity(m, &c);
    INetworkListManager_Release(m);
    if (FAILED(hr)) return -1;
    return (c & (NLM_CONNECTIVITY_IPV4_INTERNET | NLM_CONNECTIVITY_IPV6_INTERNET)) ? 1 : 0;
}

/* ---------- words ---------- */

static const struct { DWORD build; const wchar_t *name; } WINDOWS_NAMES[] = {
    { 10240, L"1507" }, { 10586, L"1511" }, { 14393, L"1607" }, { 15063, L"1703" }, { 16299, L"1709" }, { 17134, L"1803" }, { 17763, L"1809" },
    { 18362, L"1903" }, { 18363, L"1909" }, { 19041, L"2004" }, { 19042, L"20H2" }, { 19043, L"21H1" }, { 19044, L"21H2" }, { 19045, L"22H2" },
    { 22000, L"21H2" }, { 22621, L"22H2" }, { 22631, L"23H2" }, { 26100, L"24H2" }, { 26200, L"25H2" } };

/* "Windows 10 version 1809 (build 17763)", "Windows 11 version 24H2 (build 26100)", "Windows 8.1" */
static void windows_words(const facts_t *f, wchar_t *b, size_t cap)
{
    if (f->major < 10)
    {
        const wchar_t *n = f->major == 6 && f->minor == 3 ? L"Windows 8.1" : f->major == 6 && f->minor == 2 ? L"Windows 8" : f->major == 6 && f->minor == 1 ? L"Windows 7" : NULL;
        if (n) lstrcpynW(b, n, (int)cap); else swprintf(b, cap, L"Windows %lu.%lu", (unsigned long)f->major, (unsigned long)f->minor);
        return;
    }
    const wchar_t *product = f->build >= 22000 ? L"Windows 11" : L"Windows 10", *name = NULL;
    for (size_t i = 0; i < sizeof WINDOWS_NAMES / sizeof WINDOWS_NAMES[0]; i++) if (WINDOWS_NAMES[i].build == f->build) name = WINDOWS_NAMES[i].name;
    if (name) swprintf(b, cap, L"%ls version %ls (build %lu)", product, name, (unsigned long)f->build);
    else swprintf(b, cap, L"%ls (build %lu)", product, (unsigned long)f->build);
}

static void have_words(const facts_t *f, wchar_t *b, size_t cap)
{
    if (f->release == 0) lstrcpynW(b, L"This PC has no .NET Framework 4.", (int)cap);
    else swprintf(b, cap, L"This PC has .NET Framework %ls.", netfx_name(f->release));
}

/* "Workbench needs" or "This Arm64 edition of Workbench needs" */
static const wchar_t *who_needs(const facts_t *f) { return f->rule == &RULE_ARM64 ? L"This Arm64 edition of Workbench needs" : L"Workbench needs"; }

static const wchar_t RESTART_NOTE[] = L" Windows has a restart waiting. If the .NET Framework installer asked you to restart, restart this PC, then open Workbench again.";

static void details_text(const facts_t *f, const wchar_t *result, wchar_t *b, size_t cap)
{
    wchar_t exe[MAX_PATH * 2] = L""; GetModuleFileNameW(NULL, exe, MAX_PATH * 2);
    wchar_t e[300], win[96]; windows_words(f, win, 96);
    wchar_t clr[700];
    if (!f->clr.probed) lstrcpynW(clr, L"not checked (the registry value is too low, or this Windows is not supported)", 700);
    else if (!f->clr.step) swprintf(clr, 700, L"ready: %ls in %ls", f->clr.version, f->clr.dir);
    else swprintf(clr, 700, L"%ls (step %d, %ls)%ls", clr_step_words(f->clr.step), f->clr.step, hr_words(f->clr.hr,
                  e, 300), f->clr.simulated ? L" [simulated]" : L"");
    swprintf(b, cap,
             L"Result: %ls\n"
             L"Workbench %ls, %ls file; requirements applied: the %ls edition's (.NET Framework %ls, Release %lu or later)\n"
             L"Processor: %ls%ls\n"
             L"Windows: %ls; reported %lu.%lu.%lu.%lu %ls\n"
             L".NET Framework Release value: %lu (%ls); in the registry: %ls %lu\n"
             L"Runtime check: %ls\n"
             L"Restart waiting: %ls%ls%ls\n"
             L"Internet: %ls\n"
             L"Microsoft's page for .NET Framework %ls: %ls\n"
             L"This file: %ls\n"
             L"Simulated for testing: %ls",
             result, CCS_HOST_VERSION_W, HOST_ARCH, f->rule->edition, f->rule->need, (unsigned long)f->rule->min_release,
             f->native, f->emulated ? L" (this x64 file runs under emulation)" : L"",
             win, (unsigned long)f->real_major, (unsigned long)f->real_minor, (unsigned long)f->real_build, (unsigned long)f->ubr, f->display,
             (unsigned long)f->release, netfx_name(f->release), f->release_found ? L"Release =" : L"not found", (unsigned long)f->real_release,
             clr, f->restart ? L"yes (" : L"no", f->restart ? f->restart_why : L"", f->restart ? L")" : L"",
             f->internet == 1 ? L"connected" : f->internet == 0 ? L"not connected" : L"not known",
             f->rule->need, f->rule->url, exe, f->sim.list[0] ? f->sim.list : L"nothing");
}

static void view_common(setup_view *v)
{
    ZeroMemory(v, sizeof *v);
    lstrcpynW(v->title, L"CCS Console Workbench", 96);
    lstrcpynW(v->close_label, L"Cancel", 32);
}

/* the setup window's words for a missing or unusable .NET Framework or an unsupported Windows; again: after Continue */
static void prereq_view(setup_view *v, const facts_t *f, int kind, int again)
{
    view_common(v);
    const rule_t *r = f->rule;
    wchar_t have[96]; have_words(f, have, 96);
    wchar_t e[300];
    lstrcpynW(v->heading, L"Before Workbench can open", 200);
    switch (kind)
    {
    case PRE_MISSING:
        swprintf(v->body, SETUP_TEXT, L"%ls Microsoft .NET Framework %ls, a Windows component. Download and install it, then choose Continue.", who_needs(f), r->need);
        swprintf(v->steps, SETUP_TEXT, L"Download Installer opens Microsoft's .NET Framework %ls page in your web browser. There, choose Download .NET Framework %ls "
                 L"Runtime, not the Developer Pack. Run the file it downloads; Windows asks for an administrator's permission to install it. Then come back here "
                 L"and choose Continue.", r->need, r->need);
        swprintf(v->offline, SETUP_TEXT, L"No internet on this PC? On a PC that has internet, open %ls. Under Advanced downloads, in the Run apps - Runtime row, "
                 L"choose Runtime under Offline installer. Copy that file to this PC, for example on a USB drive, run it here, then choose Continue.", r->typed);
        if (again) swprintf(v->status, SETUP_TEXT, L"Still missing: .NET Framework %ls. %ls", r->need, have);
        else lstrcpynW(v->status, have, SETUP_TEXT);
        v->status_warn = again;
        if (f->internet == 0) wcsncat(v->status, L" This PC does not seem to be connected to the internet: follow No internet on this PC? above.", SETUP_TEXT - wcslen(v->status) - 1);
        lstrcpynW(v->open_url, r->url, 300);
        lstrcpynW(v->open_label, L"&Download Installer", 64);
        swprintf(v->open_done, SETUP_TEXT, L"Your web browser was asked to open Microsoft's .NET Framework %ls page. Choose Download .NET Framework %ls Runtime there, "
                 L"run the file it downloads, then come back and choose Continue.", r->need, r->need);
        v->can_continue = 1; v->primary = SETUP_PRIMARY_OPEN;
        break;
    case PRE_NOT_FOUND:
        swprintf(v->body, SETUP_TEXT, L"%ls Microsoft .NET Framework %ls, which is part of this version of Windows, but Workbench could not find it on this PC.", who_needs(f), r->need);
        lstrcpynW(v->steps, L"Install the latest updates in Windows Update, restart this PC, then choose Continue or open Workbench again. Technical details "
                  L"shows what Workbench found.", SETUP_TEXT);
        if (again) swprintf(v->status, SETUP_TEXT, L"Still not found: .NET Framework %ls. %ls", r->need, have);
        else lstrcpynW(v->status, have, SETUP_TEXT);
        v->status_warn = again;
        v->can_continue = 1; v->primary = SETUP_PRIMARY_CONTINUE;
        break;
    case PRE_UNUSABLE:
        swprintf(v->body, SETUP_TEXT, L"This PC has Microsoft .NET Framework %ls, but Windows could not start it for Workbench.", netfx_name(f->release));
        lstrcpynW(v->steps, L"Restart this PC, then choose Continue or open Workbench again. If it still cannot start, install the latest updates in Windows "
                  L"Update. Technical details shows what Windows reported.", SETUP_TEXT);
        swprintf(v->status, SETUP_TEXT, L"%lsWindows reported that %ls (%ls).", again ? L"Still not ready. " : L"", clr_step_words(f->clr.step),
                 hr_words(f->clr.hr, e, 300));
        v->status_warn = 1;
        v->can_continue = 1; v->primary = SETUP_PRIMARY_CONTINUE;
        break;
    case PRE_WINDOWS:
    default:
    {
        wchar_t win[96]; windows_words(f, win, 96);
        lstrcpynW(v->heading, L"Workbench cannot open on this version of Windows", 200);
        if (f->major < 10)
        {
            swprintf(v->body, SETUP_TEXT, L"Workbench needs Windows 10 or Windows 11. This PC runs %ls.", win);
            lstrcpynW(v->steps, L"Workbench can open on this PC once it runs Windows 10 or Windows 11. Nothing was installed or changed.", SETUP_TEXT);
        }
        else if (r == &RULE_ARM64)
        {
            swprintf(v->body, SETUP_TEXT, L"This Arm64 edition of Workbench needs Microsoft .NET Framework 4.8.1, which Microsoft makes for Arm-based PCs with "
                     L"Windows 11. This PC runs %ls.", win);
            lstrcpynW(v->steps, L"Workbench can open on this PC once it runs Windows 11. Nothing was installed or changed.", SETUP_TEXT);
        }
        else
        {
            swprintf(v->body, SETUP_TEXT, L"Workbench needs Microsoft .NET Framework 4.8, which Microsoft makes for Windows 10 version 1607 and later and for "
                     L"Windows 11. This PC runs %ls.", win);
            lstrcpynW(v->steps, L"Update Windows to a current version of Windows 10 or Windows 11, then open Workbench again. Nothing was installed or changed.", SETUP_TEXT);
        }
        v->can_continue = 0; lstrcpynW(v->close_label, L"Close", 32); v->primary = SETUP_PRIMARY_CLOSE;
        break;
    }
    }
    if (f->restart && kind != PRE_WINDOWS) wcsncat(v->status, RESTART_NOTE, SETUP_TEXT - wcslen(v->status) - 1);
    details_text(f, PRE_NAMES[kind], v->details, SETUP_DETAILS);
}

/* everything is there now, but Windows has a restart waiting: say so once before going on */
static void ready_restart_view(setup_view *v, const facts_t *f)
{
    view_common(v);
    lstrcpynW(v->heading, L"Before Workbench can open", 200);
    swprintf(v->body, SETUP_TEXT, L"This PC now has Microsoft .NET Framework %ls, which Workbench needs.", netfx_name(f->release));
    lstrcpynW(v->steps, L"If the .NET Framework installer asked you to restart, restart this PC first, then open Workbench again. To open Workbench now, "
              L"choose Continue.", SETUP_TEXT);
    swprintf(v->status, SETUP_TEXT, L"Windows has a restart waiting (%ls).", f->restart_why);
    v->status_warn = 1;
    v->can_continue = 1; v->primary = SETUP_PRIMARY_CONTINUE;
    details_text(f, L"ready, with a restart waiting", v->details, SETUP_DETAILS);
}

/* the plain words for noninteractive output (no window) */
static void prereq_plain(const facts_t *f, int kind, wchar_t *b, size_t cap)
{
    const rule_t *r = f->rule;
    wchar_t win[96], e[300]; windows_words(f, win, 96);
    const wchar_t *ed = r == &RULE_ARM64 ? L"This Arm64 edition of CCS Console Workbench" : L"CCS Console Workbench";
    switch (kind)
    {
    case PRE_MISSING:
        swprintf(b, cap, L"%ls needs Microsoft .NET Framework %ls or later, a Windows component. This PC has: %ls. Install the .NET Framework %ls Runtime "
                 L"from %ls, then start Workbench again. Nothing was installed or changed.", ed, r->need, netfx_name(f->release), r->need, r->url);
        break;
    case PRE_NOT_FOUND:
        swprintf(b, cap, L"%ls needs Microsoft .NET Framework %ls or later, which is part of this version of Windows (%ls), but it was not found (this PC "
                 L"reports: %ls). Install the latest Windows updates and restart this PC. Nothing was installed or changed.", ed, r->need, win, netfx_name(f->release));
        break;
    case PRE_UNUSABLE:
        swprintf(b, cap, L"%ls cannot start this PC's Microsoft .NET Framework %ls: %ls (%ls). Restart this PC; if that does not help, install the "
                 L"latest Windows updates. Nothing was installed or changed.", ed, netfx_name(f->release), clr_step_words(f->clr.step), hr_words(f->clr.hr,
                 e, 300));
        break;
    default:
        if (f->major < 10) swprintf(b, cap, L"%ls needs Windows 10 or Windows 11; this PC runs %ls. Nothing was installed or changed.", ed, win);
        else if (r == &RULE_ARM64) swprintf(b, cap, L"%ls needs Microsoft .NET Framework 4.8.1, which Microsoft makes for Arm-based PCs with Windows 11; this "
                                            L"PC runs %ls. Nothing was installed or changed.", ed, win);
        else swprintf(b, cap, L"%ls needs Microsoft .NET Framework 4.8, which Microsoft makes for Windows 10 version 1607 and later and for Windows 11; "
                      L"this PC runs %ls. Nothing was installed or changed.", ed, win);
        break;
    }
    if (f->restart && kind != PRE_WINDOWS) { wcsncat(b, L" Windows has a restart waiting (", cap - wcslen(b) - 1); wcsncat(b, f->restart_why, cap - wcslen(b) - 1); wcsncat(b, L").", cap - wcslen(b) - 1); }
    if (f->sim.list[0]) { wcsncat(b, L" [simulated for testing: ", cap - wcslen(b) - 1); wcsncat(b, f->sim.list, cap - wcslen(b) - 1); wcsncat(b, L"]", cap - wcslen(b) - 1); }
}

/* a damaged or incomplete copy: never a missing part of Windows */
static void payload_view(setup_view *v, const payload_t *p)
{
    view_common(v);
    int missing = !p->bytes || !p->size;
    lstrcpynW(v->heading, missing ? L"This copy of Workbench is incomplete" : L"This copy of Workbench is damaged", 200);
    lstrcpynW(v->body, missing ? L"Its program is missing from the file, so it cannot start. This is not a missing part of Windows: installing .NET Framework "
                                 L"would not fix it."
                               : L"Its program does not match the fingerprint recorded when it was made, so it was not started. This is not a missing part of "
                                 L"Windows: installing .NET Framework would not fix it.", SETUP_TEXT);
    lstrcpynW(v->steps, g_studio_launch
              ? L"Run the Classic Cart Studio installer again to repair it; your collections, backups and settings are kept. Open Download Page opens "
                L"ClassicCartStudio.com/app/, where the installer is."
              : L"Download CCS Console Workbench again from ClassicCartStudio.com/app/ and use the new copy. Open Download Page opens that page in your "
                L"web browser.", SETUP_TEXT);
    lstrcpynW(v->status, L"Nothing was changed on this PC.", SETUP_TEXT);
    lstrcpynW(v->open_url, URL_WORKBENCH, 300);
    lstrcpynW(v->open_label, L"&Open Download Page", 64);
    lstrcpynW(v->open_done, L"Your web browser was asked to open ClassicCartStudio.com/app/.", SETUP_TEXT);
    lstrcpynW(v->close_label, L"Close", 32);
    v->primary = SETUP_PRIMARY_OPEN;
    wchar_t exe[MAX_PATH * 2] = L""; GetModuleFileNameW(NULL, exe, MAX_PATH * 2);
    swprintf(v->details, SETUP_DETAILS, L"Result: %ls\nWorkbench %ls, %ls file\nProgram inside this file: %lu bytes\nIts SHA-256 now: %ls\nIts SHA-256 "
             L"recorded when it was made: %ls\nThis file: %ls", missing ? L"payload missing" : L"payload damaged", CCS_HOST_VERSION_W, HOST_ARCH,
             (unsigned long)p->size, p->actual_sha256[0] ? p->actual_sha256 : L"(none)", p->expected_sha256[0] ? p->expected_sha256 : L"(none)", exe);
}

/* a start that failed after the checks (the runtime would not start, the program would not load, or it stopped unexpectedly) */
static void start_problem_view(setup_view *v, int code, const wchar_t *text)
{
    view_common(v);
    lstrcpynW(v->close_label, L"Close", 32); v->primary = SETUP_PRIMARY_CLOSE;
    switch (code)
    {
    case EXIT_NETFX:
    case EXIT_CLR:
        lstrcpynW(v->heading, L"Workbench could not start", 200);
        lstrcpynW(v->body, L"Windows could not start Microsoft .NET Framework for Workbench.", SETUP_TEXT);
        lstrcpynW(v->steps, L"Restart this PC, then open Workbench again. If it still cannot start, install the latest updates in Windows Update.", SETUP_TEXT);
        break;
    case EXIT_LOAD:
        lstrcpynW(v->heading, L"Workbench could not start", 200);
        lstrcpynW(v->body, L"Workbench could not load its program.", SETUP_TEXT);
        lstrcpynW(v->steps, g_studio_launch ? L"Restart this PC and open Workbench again. If it still does not open, run the Classic Cart Studio installer again "
                                              L"to repair it; your collections, backups and settings are kept."
                                            : L"Restart this PC and open Workbench again. If it still does not open, download CCS Console Workbench again from "
                                              L"ClassicCartStudio.com/app/.", SETUP_TEXT);
        lstrcpynW(v->open_url, URL_WORKBENCH, 300);
        lstrcpynW(v->open_label, L"&Open Download Page", 64);
        lstrcpynW(v->open_done, L"Your web browser was asked to open ClassicCartStudio.com/app/.", SETUP_TEXT);
        break;
    case EXIT_THREW:
        lstrcpynW(v->heading, L"Workbench stopped unexpectedly", 200);
        lstrcpynW(v->body, L"Workbench ran into a problem it could not handle, and closed.", SETUP_TEXT);
        lstrcpynW(v->steps, L"Open Workbench again. Technical details shows what was reported; Copy Details copies it.", SETUP_TEXT);
        break;
    default:
        lstrcpynW(v->heading, L"Workbench could not start", 200);
        lstrcpynW(v->body, text, SETUP_TEXT);
        break;
    }
    swprintf(v->details, SETUP_DETAILS, L"Result: exit code %d\nWorkbench %ls, %ls file\nReported: %ls", code, CCS_HOST_VERSION_W, HOST_ARCH, text);
}

/* ---------- the setup window: one at a time ---------- */

static int open_page(const wchar_t *url, wchar_t *why, size_t cap, void *ctx)
{
    (void)ctx;
    sim_t sim; sim_read(&sim);
    if (sim.browser_fail) { lstrcpynW(why, L"simulated for testing", (int)cap); return 0; }
    SHELLEXECUTEINFOW sei; ZeroMemory(&sei, sizeof sei);
    sei.cbSize = sizeof sei; sei.fMask = SEE_MASK_NOASYNC | SEE_MASK_FLAG_NO_UI; sei.lpVerb = L"open"; sei.lpFile = url; sei.nShow = SW_SHOWNORMAL;
    if (ShellExecuteExW(&sei)) return 1;
    DWORD e = GetLastError();
    wchar_t t[256]; hr_text(HRESULT_FROM_WIN32(e), t, 256);
    swprintf(why, cap, L"%ls, error %lu", t[0] ? t : L"Windows gave no reason", (unsigned long)e);
    return 0;
}

static int show_window(setup_view *v, setup_recheck_fn recheck, void *ctx)
{
    /* one setup window at a time: a second start brings the open one forward and ends (no second window, no second launch) */
    HANDLE m = CreateMutexW(NULL, FALSE, L"Local\\CCS.ConsoleWorkbench.Setup");
    if (m && GetLastError() == ERROR_ALREADY_EXISTS) { setup_window_bring_forward(); CloseHandle(m); return SETUP_CLOSED; }
    int r = setup_window_run(v, recheck, open_page, ctx);
    if (m) CloseHandle(m);
    if (r < 0)
    {   /* Windows could not make the window: the same words in Windows' own message box rather than nothing */
        wchar_t t[SETUP_TEXT * 3];
        swprintf(t, sizeof t / sizeof t[0], L"%ls\n\n%ls\n\n%ls", v->heading, v->body, v->steps);
        MessageBoxW(NULL, t, v->title, MB_OK | MB_ICONERROR | MB_SETFOREGROUND);
        return SETUP_CLOSED;
    }
    return r;
}

typedef struct { facts_t f; int kind; int restart_seen; } flow_t;

static int flow_recheck(setup_view *v, void *ctx)
{
    flow_t *fl = (flow_t *)ctx;
    facts_t nf; gather(&nf, 1);
    nf.internet = internet_state(&nf.sim);
    int kind = decide(&nf);
    fl->f = nf; fl->kind = kind;
    if (kind == PRE_READY)
    {
        if (nf.restart && !fl->restart_seen) { fl->restart_seen = 1; ready_restart_view(v, &nf); return 0; }
        return 1;
    }
    fl->restart_seen = 0;
    prereq_view(v, &nf, kind, 1);
    return 0;
}

/* ---------- --host-info (reads only) ---------- */

static void print_host_info(const payload_t *p)
{
    wchar_t exe[MAX_PATH * 2]; GetModuleFileNameW(NULL, exe, MAX_PATH * 2);
    wchar_t exe_j[MAX_PATH * 4]; json_escape(exe, exe_j, MAX_PATH * 4);
    facts_t f; gather(&f, 2);
    int kind = decide(&f);
    wchar_t dir_j[MAX_PATH * 2]; json_escape(f.clr.dir, dir_j, MAX_PATH * 2);
    wchar_t sim_j[1000]; json_escape(f.sim.list, sim_j, 1000);
    wchar_t why_j[400]; json_escape(f.restart_why, why_j, 400);
    wchar_t cmds[600] = L"";
    for (size_t i = 0; i < NONINTERACTIVE_COUNT; i++) { if (i) wcscat(cmds, L","); wcscat(cmds, L"\""); wcscat(cmds, NONINTERACTIVE[i]); wcscat(cmds, L"\""); }
    wchar_t text[12000];
    swprintf(text, sizeof text / sizeof text[0],
             L"{\"schema\":\"ccs-native-host-info/1\",\"host_version\":\"%ls\",\"host_arch\":\"%ls\",\"pc_arch\":\"%ls\",\"emulated\":%ls,"
             L"\"exe\":\"%ls\",\"payload_bytes\":%lu,\"payload_sha256\":\"%ls\",\"payload_expected_sha256\":\"%ls\",\"payload_ok\":%ls,"
             L"\"netfx_release\":%lu,\"netfx\":\"%ls\",\"netfx_required\":\"%ls\",\"netfx_ok\":%ls,\"clr_version\":\"%ls\",\"clr_dir\":\"%ls\",\"clr_loadable\":%ls,"
             L"\"prerequisite\":\"%ls\",\"prerequisite_rules\":\"%ls\",\"netfx_download\":\"%ls\",\"os\":\"%lu.%lu.%lu\",\"os_ubr\":%lu,"
             L"\"restart_pending\":%ls,\"restart_reason\":\"%ls\",\"clr_step\":%d,\"clr_hresult\":%ld,\"noninteractive_commands\":[%ls],\"simulated\":\"%ls\","
             L"\"par2\":\"%ls\",\"abi_version\":%u}\n",
             CCS_HOST_VERSION_W, HOST_ARCH, f.native, f.emulated ? L"true" : L"false", exe_j, (unsigned long)p->size, p->actual_sha256, p->expected_sha256,
             p->ok ? L"true" : L"false", (unsigned long)f.release, netfx_name(f.release), f.rule->need, f.release >= f.rule->min_release ? L"true" : L"false",
             f.clr.version, dir_j, f.clr.probed && !f.clr.step ? L"true" : L"false",
             PRE_NAMES[kind], f.rule->edition, f.rule->url, (unsigned long)f.major, (unsigned long)f.minor, (unsigned long)f.build, (unsigned long)f.ubr,
             f.restart ? L"true" : L"false", why_j, f.clr.step, (long)f.clr.hr, cmds, sim_j, ccs_host_get_abi()->par2_version, CCS_HOST_ABI_VERSION);
    write_utf8(std_handle(STD_OUTPUT_HANDLE), text);
}

/* ---------- run the managed program ---------- */

static int run_managed(const payload_t *p, int argc, wchar_t **argv, int simulate_start_failure)
{
    clr_t c;
    wchar_t e[512];
    int step = clr_find(&c);
    if (step) return fail(EXIT_NETFX, L"CCS Console Workbench cannot start this PC's .NET Framework runtime: %ls (%ls). Nothing was installed or changed.",
                          clr_step_words(step), hr_words(c.hr, e, 512));
    HRESULT hr = simulate_start_failure ? (HRESULT)0x80131700 : S_OK;   /* the test hook start=fail: a failure before the runtime is bound */
    if (SUCCEEDED(hr)) hr = ((PFN_RuntimeInfo_GetInterface)CCS_VFN(c.info, SLOT_ICLRRuntimeInfo_GetInterface))(c.info, &CCS_CLSID_CorRuntimeHost, &CCS_IID_ICorRuntimeHost, &c.cor);
    if (SUCCEEDED(hr)) hr = ((PFN_CorHost_Start)CCS_VFN(c.cor, SLOT_ICorRuntimeHost_Start))(c.cor);
    if (FAILED(hr)) return fail(EXIT_CLR, L"CCS Console Workbench could not start .NET Framework %ls (%ls)%ls.", c.version, hr_words(hr, e, 512),
                                simulate_start_failure ? L" [simulated for testing: start=fail]" : L"");

    IUnknown *unk = NULL; void *domain = NULL;
    hr = ((PFN_CorHost_GetDefaultDomain)CCS_VFN(c.cor, SLOT_ICorRuntimeHost_GetDefaultDomain))(c.cor, &unk);
    if (SUCCEEDED(hr)) hr = ccs_query(unk, &CCS_IID__AppDomain, &domain);
    ccs_release(unk);
    if (FAILED(hr)) return fail(EXIT_CLR, L"CCS Console Workbench could not open the .NET application domain (%ls).", hr_words(hr, e, 512));

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
    if (FAILED(hr)) return fail(EXIT_LOAD, L"CCS Console Workbench could not load its program (%ls).", hr_words(hr, e, 512));
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
     * Measured: the runtime's own managed shutdown, entered from this host through
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
    for (int i = 1; i < argc; i++)
    {
        if (wcscmp(argv[i], L"--backup-worker") == 0) g_worker_mode = 1;
        if (wcscmp(argv[i], L"--studio-launch") == 0) g_studio_launch = 1;
        for (size_t k = 0; k < NONINTERACTIVE_COUNT; k++) if (wcscmp(argv[i], NONINTERACTIVE[k]) == 0) g_noninteractive = 1;
    }
    if (redirected(STD_OUTPUT_HANDLE) || redirected(STD_ERROR_HANDLE)) g_noninteractive = 1;
    /* WPF needs a single-threaded apartment; reflection's Invoke does not apply Main's [STAThread], so the host sets it here (before the
     * setup window too, which uses COM for its live region and the network check) */
    HRESULT hr = CoInitializeEx(NULL, COINIT_APARTMENTTHREADED);

    payload_t p; load_payload(&p);
    if (argc > 1 && wcscmp(argv[1], L"--host-info") == 0) { print_host_info(&p); return p.ok ? 0 : EXIT_PAYLOAD; }
    if (!p.bytes || !p.size || !p.ok)
    {
        if (g_noninteractive)
            return fail(EXIT_PAYLOAD, !p.bytes || !p.size ? L"This copy of CCS Console Workbench is incomplete (its program is missing). Please download it again."
                                                         : L"This copy of CCS Console Workbench is damaged (its program does not match its recorded fingerprint). Please download it again.");
        static setup_view pv;
        payload_view(&pv, &p);
        show_window(&pv, NULL, NULL);
        return EXIT_PAYLOAD;
    }

    static flow_t fl;
    gather(&fl.f, 1);
    fl.kind = decide(&fl.f);
    if (fl.kind != PRE_READY)
    {
        if (g_noninteractive) { wchar_t t[2048]; prereq_plain(&fl.f, fl.kind, t, 2048); return fail(EXIT_NETFX, L"%ls", t); }
        fl.f.internet = internet_state(&fl.f.sim);
        static setup_view v;
        prereq_view(&v, &fl.f, fl.kind, 0);
        if (show_window(&v, flow_recheck, &fl) != SETUP_READY) return EXIT_NETFX;   /* Cancel, Close, or another start's window came forward */
    }
    if (FAILED(hr)) return fail(EXIT_CLR, L"CCS Console Workbench could not prepare its window thread (0x%08lX).", (unsigned long)hr);
    return run_managed(&p, argc, argv, fl.f.sim.start_fail);
}
