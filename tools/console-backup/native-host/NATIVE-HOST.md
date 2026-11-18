# CCS Console Workbench: the native single-file host

Status: built and measured on 2026-11-15 in branch `wb-host`. What follows separates what was **verified** (run here, with the
evidence named), what is **believed** (from documentation or code reading, not run) and what is **unknown**.

## 1. Architecture

One executable, `CCS-Console-Workbench.exe`, per architecture (x64 and Arm64). It is a native Windows program (C and C++, built with
zig 0.17.0 for `x86_64-windows-gnu` and `aarch64-windows-gnu`) that carries:

| Part | How it is carried | How it runs |
|---|---|---|
| the managed Workbench (the .NET Framework 4.8 WPF executable built from `ccs-backup-utility`, which itself carries its managed DLLs as resources) | Win32 resource `RCDATA 101`, plus its SHA-256 as `RCDATA 102` | the host checks the SHA-256, starts the .NET Framework 4 runtime in its own process and loads the bytes with `AppDomain.Load(byte[])`; the managed program's existing `AssemblyResolve` handler then loads its DLLs from its own resources, as before |
| par2cmdline 1.4.0's library (libpar2), unmodified | compiled from the pinned release tarball and linked into the host | called by the managed side through the host's ABI table (section 3) |
| the C++ runtime (libc++, libc++abi, libunwind), mingw-w64 start-up code, compiler-rt | linked in statically by zig | - |
| the C runtime | **not** carried: Windows' Universal CRT (`ucrtbase.dll`, through the `api-ms-win-crt-*` API sets), an operating-system component of every Windows 10 and 11 | imported |

Nothing is written to disk to run: no helper EXE or DLL, no extraction to `%TEMP%`, `%APPDATA%`, `%LOCALAPPDATA%`, a cache or beside the
executable, and no .NET "single-file" self-extraction setting (that is a .NET Core feature in any case; this is .NET Framework). The
controlled worker is the same executable started again with `--backup-worker` (the managed code already starts
`Process.GetCurrentProcess().MainModule.FileName`, which is now the host).

### Start-up sequence (`src/host_main.c`)

1. `SetDllDirectoryW(L"")` (no DLL search in the current folder); `CommandLineToArgvW(GetCommandLineW())`.
2. `--host-info`: print what the host found (JSON) and stop, without running managed code.
3. Payload: `FindResourceW/LoadResource/LockResource` (`RCDATA 101`), SHA-256 with `BCrypt*` compared with `RCDATA 102`. A mismatch stops
   with "This copy of CCS Console Workbench is damaged ..." (exit 91).
4. .NET Framework: registry `HKLM\SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full\Release` must be at least 528040 (4.8) for the x64
   host and 533320 (4.8.1, the first .NET Framework with a native Arm64 runtime) for the Arm64 host. Otherwise a plain sentence, naming
   what the PC has, ending "Nothing was installed or changed." (exit 90). The host never installs or enables anything.
5. `CoInitializeEx(NULL, COINIT_APARTMENTTHREADED)` on the main thread: WPF needs a single-threaded apartment and reflection's
   `Invoke` does not apply `Main`'s `[STAThread]`.
6. `LoadLibraryExW("mscoree.dll", LOAD_LIBRARY_SEARCH_SYSTEM32)` -> `CLRCreateInstance(CLSID_CLRMetaHost, IID_ICLRMetaHost)` ->
   `ICLRMetaHost::GetRuntime("v4.0.30319")` -> `ICLRRuntimeInfo::IsLoadable` -> `ICLRRuntimeInfo::GetInterface(CLSID_CorRuntimeHost,
   IID_ICorRuntimeHost)` -> `ICorRuntimeHost::Start` -> `GetDefaultDomain` -> `QueryInterface(IID__AppDomain)`.
7. `_AppDomain::SetData("CcsNativeHost.Abi", VT_I8 address of the ABI table)`.
8. `SafeArrayCreateVector(VT_UI1)` + copy of the resource bytes -> `_AppDomain::Load_3` (`Load(byte[])`) -> `_Assembly::get_EntryPoint`
   -> `_MethodInfo::Invoke_3(null, [string[] args])` with the arguments after the executable's name.
9. The managed `Main` first gives the default domain its target framework (`Program.HostedTargetFramework`, section 5.2), then runs as
   before.
10. When `Main` returns, `ExitProcess(Main's return value)` (kernel32). Not the runtime's managed shutdown: section 5.1 shows why.

Unhandled managed exceptions come back from `Invoke_3` as `TargetInvocationException` (0x80131604); the host reports "stopped
unexpectedly" with the `IErrorInfo` description and exits 94. Runtime start failures exit 92, load failures 93.

### Interfaces and headers

zig's mingw-w64 headers have `mscoree.h` (with `ICorRuntimeHost`) but no `metahost.h` and nothing from `mscorlib.tlb`, so
`src/clr_hosting.h` declares every GUID and calls every method by vtable slot:

| Interface | IID / CLSID | Slots used | Source of the slot numbers |
|---|---|---|---|
| `ICLRMetaHost` | `{D332DB9E-B9B3-4125-8207-A14884F53216}`, CLSID `{9280188D-0E8E-4867-B30C-7FA83884E8DE}` | GetRuntime 3 (ExitProcess 9 was tried, section 5.1) | Microsoft `metahost.h` order, cross-checked with Wine `include/metahost.idl` |
| `ICLRRuntimeInfo` | `{BD39D1D2-BA2F-486A-89B0-B4B0CB466891}` | GetVersionString 3, GetRuntimeDirectory 4, GetInterface 9, IsLoadable 10 | same |
| `ICorRuntimeHost` | `{CB2F6722-AB3A-11D2-9C40-00C04FA30A3E}`, CLSID `{CB2F6723-AB3A-11D2-9C40-00C04FA30A3E}` | Start 10, GetDefaultDomain 13 | zig's mingw-w64 `mscoree.h` (`ICorRuntimeHostVtbl`) |
| `_AppDomain` | `{05F696DC-2B29-3663-AD8B-C4389CF2A713}` | Load_3 45, SetData 63 | this PC's `mscorlib.tlb`, `tools/dump_vtables.ps1` |
| `_Assembly` (dual) | `{17156360-2F1A-384A-BC52-FDE93C215C5B}` | get_EntryPoint 16 | same (vtable view of the dual interface) |
| `_MethodInfo` | `{FFCC1B5D-ECB8-38DD-9B01-3DC8ABC2AA5F}` | Invoke_3 37 | same |

`test_host.py` re-reads the type library on every run and fails if a constant in `clr_hosting.h` differs.

## 2. Build (`build_host.sh`, WSL)

```
wsl -d Ubuntu -- bash tools/console-backup/native-host/build_host.sh --payload <managed exe> --out <dir> [--arch all] [--measure]
```

Pinned: zig `0.17.0` (checked); par2cmdline `1.4.0`, `https://github.com/Parchive/par2cmdline/releases/download/v1.4.0/par2cmdline-1.4.0.tar.gz`,
SHA-256 `f738242adf3bbfe0b041b3263e5be4d76e4b16a4e938952a38265efd94c7869d` (checked every build; the same value GitHub records as the
asset digest, and the tarball's SHA-512 matches the release's `checksums.sha512`), tag `v1.4.0` = commit
`3a27718ac77e638bd4ad5c2f14a2349759fcec15` (annotated tag object `5d66a821f3ebb8df92b6edcddebc0d05cb5cd13d`). The release's detached
GPG signature (`.sig`) was not checked (no key was imported on this PC).

Only the files par2cmdline's `Makefile.am` lists for `libpar2.a` are compiled (24 `.cpp`; only `src/`, `Makefile.am`, `COPYING` and `AUTHORS` are extracted from the tarball), with `-O2 -std=c++14 -DNDEBUG
-DPACKAGE="par2cmdline" -DVERSION="1.4.0"`, no OpenMP. One build-only adaptation, no source change: `src/par2_prelude.h` is
force-included so the C++ standard headers are parsed before `libpar2internal.h`'s MSVC-oriented `#define snprintf _snprintf_s` (which
otherwise breaks libc++'s `<locale>`). Link: `-municode -Wl,--subsystem,windows -Wl,--gc-sections -s` with `ole32 oleaut32 shell32
advapi32 bcrypt user32`. Outputs `<out>/x64/<name>.exe`, `<out>/arm64/<name>.exe` and `<out>/build-record.json` (inputs and outputs with
SHA-256). The PE header's link time stamp, the only byte difference lld leaves between two links of the same inputs, is replaced by
the first four bytes of the SHA-256 of the file with that field zeroed (the idea of MSVC's `/Brepro`), so builds are byte-for-byte
reproducible. The work folder defaults to WSL's own file system (`~/.cache/ccs-native-host/work`; compiling on `/mnt/c` is several times
slower). `--measure` also links a host without libpar2 (`src/par2_stub.c`, never shipped) to measure what the PAR2 code adds.

Test (Windows, Python 3.13): `python tools/console-backup/native-host/test_host.py --work <new folder> [--big-mb 128]` builds the managed
standalone and both hosts (or takes `--managed` and `--host-out`), then runs everything in section 4. `tools/dump_vtables.ps1` prints the
mscorlib vtable slots. The host has one test hook: `CCS_HOST_SIMULATE_NETFX_RELEASE=<n>` makes it believe the registry reported `n`
(used to show the missing-.NET message; it changes nothing else, and the runtime itself is still checked).

## 3. The ABI (`src/ccs_host_abi.h`)

### How the managed side finds it

The host passes **one pointer**: before the managed entry point runs it calls `_AppDomain::SetData("CcsNativeHost.Abi", <boxed Int64:
address of a static ccs_host_abi table>)`. `CcsBackup.Repair.Par2Native` reads `AppDomain.CurrentDomain.GetData("CcsNativeHost.Abi")`,
checks the table's magic (`"CCSHOST1"`), version (1 or later), table size and report size, then turns the three function pointers into
delegates (`Marshal.GetDelegateForFunctionPointer`). AppDomain data is process-local and cannot be inherited by a child process (unlike
an environment variable), and it is absent whenever the managed executable runs any other way. Classic Cart Studio for Windows ships the
same parity code as `ccs-par2.dll` beside its program; without the AppDomain data `Par2Native` loads that library from the program's own
folder, by full path (`LoadLibraryExW`, dependencies searched only in that folder and System32), and reads the same table from its export. When
neither is present it returns `Par2Status.NotHosted` with "Parity protection (PAR2) needs the standalone CCS Console Workbench or Classic
Cart Studio for Windows with its parity library (ccs-par2.dll)." and never throws.

Also verified, as a cross-check only: the host exports `ccs_host_get_abi` from the executable, and `GetProcAddress(GetModuleHandleW(NULL),
"ccs_host_get_abi")` from managed code returns the same table address. Not used: `[DllImport("CCS-Console-Workbench.exe")]` by name,
because it depends on the file keeping its name and on the DLL search order (a renamed copy could make the loader look for, and load,
a different file of that name).

### Functions (all synchronous, on the caller's thread; one call at a time per process, a second concurrent call gets `BUSY`)

```c
int32_t par2_create(const wchar_t *data_path, const wchar_t *par2_path, uint32_t redundancy_pct /*1..100*/, uint32_t volume_count /*0..32*/,
                    uint64_t memory_limit /*0 = 256 MB*/, ccs_par2_progress_fn progress, void *context, volatile int32_t *cancel,
                    ccs_par2_report *report);
int32_t par2_verify(const wchar_t *par2_path, const wchar_t *data_path, uint64_t memory_limit,
                    ccs_par2_progress_fn progress, void *context, volatile int32_t *cancel, ccs_par2_report *report);
int32_t par2_repair(const wchar_t *par2_path, const wchar_t *data_path, const wchar_t *output_path, uint64_t memory_limit,
                    ccs_par2_progress_fn progress, void *context, volatile int32_t *cancel, ccs_par2_report *report);
typedef int32_t (*ccs_par2_progress_fn)(void *context, int32_t stage, int32_t permille /*0..1000 or -1*/);   /* nonzero = stop */
```

Status codes: 0 OK, 1 DAMAGED_REPAIRABLE, 2 DAMAGED_NOT_REPAIRABLE, 3 PARITY_UNUSABLE, 4 REPAIR_FAILED, 5 CANCELED, 6 BAD_ARGUMENT,
7 IO_ERROR, 8 OUT_OF_MEMORY, 9 INTERNAL_ERROR, 10 NOT_NEEDED (repair of intact data), 11 BUSY. Stages: 1 reading data, 2 computing,
3 writing, 4 loading parity, 5 scanning data, 6 repairing, 7 checking output.

`ccs_par2_report` (2,104 bytes; the caller sets `size`): status, libpar2's own `Result`, block size, source blocks, recovery blocks
(created, or intact ones found), recovery volume files, damaged blocks, intact blocks, protected-file bytes, parity bytes, and a
plain-text `message[1024]`. The counts come from libpar2's own fields (`Par2Creator`/`Par2Repairer` subclasses read the protected
members after `Process`), never from parsing its text.

Behavior that matters for backups:

- **create**: one protected file; the parity goes beside it as `<name>.par2` (index) plus `volume_count` uniform volumes
  `<name>.volNNN+MMM.par2`, in the protected file's own folder (enforced). Block size and counts follow par2cmdline's own defaults (about 2,000 source blocks; recovery blocks =
  (source blocks x pct + 50) / 100, at least 1). Refuses if any `<name>.par2`/`<name>.vol*.par2` exists. The protected file is held open
  read-only with read-sharing only for the whole call (nobody can change, rename or delete it meanwhile). On failure or cancel every
  parity file this call made is deleted. libpar2 itself creates and deletes a `<name>.check.par2` probe in that folder at the start.
- **verify**: reads only; the data file and every parity file are held as above; full-file MD5 check on (`--full-hash`); refuses a parity
  set that protects more than one file or a data file whose name differs from the one recorded (the PAR2 format stores the name).
- **repair**: never writes the damaged file and never renames it. It first verifies (intact -> `NOT_NEEDED`, nothing written); otherwise it
  makes a new hidden folder `.<output name>.ccs-rebuild-<pid>-<tick>` beside `output_path`, runs libpar2 with that folder as the base path
  and the damaged file only as an "extra file" to read blocks from, re-verifies the rebuilt file in a separate libpar2 pass, and only then
  renames it to `output_path` (no replace) and removes the folder. Any failure removes the folder and leaves nothing. (libpar2 on its own
  repairs in place and renames the damaged file to `<name>.1`; this layer is what prevents that.)
- **progress/cancel**: libpar2 writes progress as text (`"Processing: 12.3%\r"`, at most every 50 ms); the host's `std::streambuf` turns
  it into `(stage, permille)` calls and checks the cancel flag on every write, throwing a private exception out of the stream (the stream
  rethrows), so the library's objects unwind and close their files. Cancel latency is the time to libpar2's next output; a few steps are
  silent (building the Reed-Solomon matrix of a large repair).
- Paths are UTF-16 in, converted to UTF-8 for libpar2, whose Windows layer converts back and uses the wide Win32 API with `\\?\` prefixes
  for long paths (the test runs the round trip in a folder named with non-ASCII letters).

### The managed wrapper (`CcsBackup/Repair/Par2Native.cs`)

`Par2Native.Available`, `UnavailableReason`, `HostVersion`, `HostArchitecture`, `LibraryVersion`; `Create(dataPath, par2Path,
redundancyPercent, volumes = 1, memoryLimit = 0, Action<Par2Stage,int> progress = null, CancellationToken cancel = default)`,
`Verify(par2Path, dataPath, ...)`, `Repair(par2Path, dataPath, outputPath, ...)` -> `Par2Report`. The cancel flag lives in unmanaged
memory (`AllocHGlobal`); the token's registration is disposed before it is freed; the progress delegate is kept alive for the call and
swallows exceptions (returning "stop") so nothing crosses into native frames. `SelfTest(folder, output)` is the round trip below, reached
through the Workbench's `--parity-selftest FOLDER [--parity-selftest-bytes N]`.

## 4. Results (2026-11-15, this PC)

`python tools/console-backup/native-host/test_host.py --work <folder> --big-mb 128`: **68 checks passed, none failed** (managed build,
both host builds, static checks, all runs). Rerun without `--big-mb` on the committed sources (the last edit only renamed strings):
**67 passed, none failed**; the hashes below are from that run, the measurements from either. The PC: x64, Windows 11 Pro Insider 26310, .NET Framework release 534296
(4.8.1), shared with other build jobs during the measurements (timings vary with that load).

### 4.1 Builds

| File | Bytes | SHA-256 |
|---|---|---|
| managed payload `CCS-Console-Backup-Restore-Utility.exe` (this branch) | 11,546,624 | `af4b1ac4109ad30ea87872fb3d6b372c9d03bfe8fc2be8f307a91f36e700c8b5` |
| `x64/CCS-Console-Workbench.exe` | 12,261,376 | `ef82ab06dbcd29f6280103a89c293d7a09be8d6e45b4f7dcfa3d5744407dbfdd` |
| `arm64/CCS-Console-Workbench.exe` | 12,165,632 | `b076be2d3905216f443c6e69e97719c0abf23ad0539f0d85301dda340686c086` |

The payload is the managed standalone as named at this branch's base commit ("CCS Console Backup & Restore Utility"); the host is
payload-agnostic, so the Workbench-named build from the other branches embeds the same way.

Reproducible: two separate builds (separate work folders, the managed payload rebuilt in between) of the previous revision gave the
identical x64 SHA-256 `7e64b87d...` (the payload's .NET build is deterministic and `build_host.sh` normalizes the PE time stamp, the
only difference lld leaves). Full build of both architectures with zig's caches warm: 197 to 291 s here (the first build on a PC also compiles libc++ and the mingw-w64 libraries into zig's cache, about 20 to 40 minutes here).

Static checks of both executables: machine x64 `0x8664` / Arm64 `0xAA64`; GUI subsystem; imports only `KERNEL32, USER32, ADVAPI32,
SHELL32, ole32, OLEAUT32, bcrypt` and the `api-ms-win-crt-*` API sets of the Universal CRT; export `ccs_host_get_abi`; `RCDATA 101` exactly
the payload's size; manifest present; the four mscorlib vtable slots equal this PC's `mscorlib.tlb`.

### 4.2 Sizes (decimal units)

| Component | Bytes | Notes |
|---|---|---|
| managed payload | 11,546,624 | stored uncompressed in the host |
| ... `CcsBackup.dll` (engine) | 11,153,920 | of which the recovery images (ccs-backup, ccs-restore at this commit): 10,891,289 |
| ... `CcsBackupUi.dll` (window) | 144,896 | |
| ... `LibUsbDotNet.LibUsbDotNet.dll` | 156,672 | |
| ... `CcsConsole.dll` (FEL loader) | 28,160 | |
| ... the executable's own code and resources | 62,976 | |
| x64 host without libpar2 (measurement build) | 11,688,448 | |
| **x64: libpar2 + the C++ runtime it needs** | **572,928** | full minus the measurement build |
| x64: all native code and resources besides the payload | 714,752 | host, libpar2, C++ runtime, CRT start-up, icon (51 KB), manifest, version |
| Arm64: libpar2 + C++ runtime / everything besides the payload | 536,064 / 619,008 | |
| **final executable** | **x64 12,261,376 (12.3 MB); Arm64 12,165,632 (12.2 MB)** | |

Compression and duplication (measured, **not applied**): the payload compresses to 10,226,113 bytes with zlib level 9 and 5,178,820 with
xz level 9; the recovery images alone to 4,951,352 with xz. `uboot-sd.bin` (761,856) and `fes1.bin` (12,656) are byte-identical in every
recovery-image variant: 774,512 duplicate bytes in this build's two variants, 3,098,048 if all five variants are embedded. The managed
assemblies are carried once (inside the payload); the host carries the payload once. Applying compression would need a decompressor in
the host (in-memory only) and would not lower peak memory; deduplicating the images is a change to how the engine embeds them.

### 4.3 Peak working set and timing

Peak working set read from each process's handle after it ended (`GetProcessMemoryInfo`):

| Run | Hosted (native host) | Managed executable started directly |
|---|---|---|
| `--host-info` (no runtime) | 22.7 MB | - |
| `--version` | 74.9 to 86.4 MB (1.9 s) | 22.4 to 22.5 MB (1.5 s) |
| `--engine-info` | 86.0 to 87.3 MB (5.6 s) | 48.7 to 48.8 MB (3.5 s) |
| worker, one request | 85.9 to 87.3 MB | - |
| the window (open, render, close) | 183.1 to 183.5 MB (shown after 5.9 s) | 143.2 to 144.5 MB (shown after 6.8 s) |
| `--parity-selftest` 8 MB | 84.4 MB (15.0 s) | - |
| `--parity-selftest` 128 MB | 107.8 MB (80.3 s) | - |
| `--parity-selftest` 512 MB (earlier build, same libpar2 code) | 174.0 to 184.9 MB (606 to 824 s) | - |

The hosted runs cost about 37 to 64 MB more: the payload's 11.5 MB is touched once by the SHA-256 check (file-backed pages), copied into
the `SAFEARRAY` for `Load_3` (freed right after the load) and copied again by the runtime. Possible reductions, not done: a `SAFEARRAY`
descriptor over the resource memory instead of a copy (-11.5 MB at the peak), trimming the hashed pages afterwards.

PAR2 (10% redundancy, about 2,000 source blocks, one volume, single thread):

| Protected file | create | verify (intact) | repair (3 damaged blocks, includes its two verifications) | parity bytes |
|---|---|---|---|---|
| 8 MB | 2.7 s | 0.8 s | 3.1 s | 1,176,580 |
| 128 MB | 23.1 s | 1.8 s | 7.4 s | 13,176,580 |
| 512 MB | 189 to 206 s | 10 to 21 s | 60 to 72 s | 51,576,580 |

Verifying a fully damaged file (the "not repairable" check) took several minutes at 512 MB (libpar2 searches every byte offset for
displaced blocks).

### 4.4 Nothing written outside the chosen folders

Method: `ReadDirectoryChangesW` on whole trees, running for the whole sequence of runs (host-info, the simulated missing .NET, version,
engine-info, both worker requests, the 8 MB and 128 MB self-tests, both windows, the CPU-load runs), every event time-stamped:

| Watched | Events | Named after this product or its libraries | `.exe/.dll/.sys/.winmd` created or changed |
|---|---|---|---|
| beside the executable (a new folder holding only it) | 0 | 0 | 0 |
| TEMP: this session's `C:\WINDOWS\TEMP` cannot be watched without administrator rights, so every program ran with `TEMP`/`TMP` pointing at a new watched folder (`GetTempPathW` and .NET's `Path.GetTempPath` read them) | 0 | 0 | 0 |
| `%APPDATA%` | 407 (other programs' caches) | 0 | 0 |
| `%LOCALAPPDATA%` (includes the profile's own `Temp`) | 4,408 (other programs' caches and logs) | 0 | 0 |

Windows cannot attribute a change to a process without administrator tracing, so the method is exclusion: product-named paths and
executable files would have failed the test; the first 15 other paths of each tree are kept in `results.json` for review. The watch sees
creations and deletions, so a file extracted and deleted within the runs would also have been seen. No overflow of the watch buffers.

## 5. Two hosting differences found and fixed

### 5.1 Ending the process: the exit code

The first host ended with `ICLRMetaHost::ExitProcess(code)` (the documented "shut the runtimes down gracefully, then end the process").
Runs without heavy load kept the exit code, but a 512 MB parity self-test run while the PC was busy building ended with exit code `0x80131029`
(`HOST_E_EXITPROCESS_TIMEOUT` in `corerror.h`) after printing `"selftest":"passed"`. Reproduced with every core busy (twice as many
busy-looping processes as logical processors) by running `--parity-selftest` (8 MB) and `--engine-info` repeatedly:

| How the host ended the process | Stressed runs | Exit code kept (0) | `0x80131029` |
|---|---|---|---|
| `ICLRMetaHost::ExitProcess` | 12 | 4 | 8 |
| the runtime's own `CorExitProcess` (via `ICLRRuntimeInfo::GetProcAddress`) | 17 | 7 | 10 |
| `CorExitProcess` after `ICLRPolicyManager::SetActionOnTimeout(OPR_FinalizerRun, eNoAction)` (runtime started through `ICLRRuntimeHost`) | 6 | 2 | 4 |
| **kernel32 `ExitProcess` (the final host)** | 23 | 23 | 0 |
| the managed executable started directly (reference) | 12 | 12 | 0 |

The failing exits took 0.6 to 2.3 s after the last output, so this is not the 40-second process-exit limit; the exact trigger inside
the runtime's shutdown when it is entered from a host thread was not identified. The final host therefore ends with the operating
system's `ExitProcess`, after which the runtime only receives `DLL_PROCESS_DETACH`. Consequence: `AppDomain.ProcessExit` handlers and the
finalizers .NET Framework runs at shutdown do not run in the standalone. The CCS code registers no `ProcessExit` handler (searched:
`ProcessExit`, `DomainUnload`, finalizers) and disposes and flushes its files itself (`Durable`); operating-system handles (files, USB) are
closed by Windows when the process ends either way. `test_host.py` checks the exit code under full CPU load on every run.

### 5.2 The target framework of a program loaded from memory

`Assembly.Load(byte[])` gives the default domain no entry assembly, so .NET Framework cannot read the program's
`[TargetFramework(".NETFramework,Version=v4.8")]` and, as for any process without one, applies .NET Framework **4.0** compatibility
defaults. Measured in the hosted self-test before the fix: `TargetFrameworkName` none, `Switch.System.IO.UseLegacyPathHandling` **true**,
`Switch.System.IO.BlockLongPaths` **true** (paths over 260 characters refused even though the manifest declares long-path awareness);
WPF's and networking's compatibility switches are keyed to the same value (not measured individually). The fix is in the managed program, first line of `Main` (`Program.HostedTargetFramework`):
when there is no entry assembly and no target framework yet, it passes the program's own `TargetFrameworkAttribute` to the runtime's
private `AppDomain.SetTargetFrameworkName`, the method the runtime itself calls at start-up. After the fix the hosted values equal a direct
start's (`.NETFramework,Version=v4.8`, both switches unset). This is private reflection into mscorlib (stable in the frozen .NET
Framework 4.8.x, but not a documented API); the self-test prints the values and `test_host.py` fails if they regress. A documented
alternative needs a `<exe>.config` file on disk (`supportedRuntime sku`), which would break the single file.

Other differences that remain, each checked: `Assembly.GetEntryAssembly()` is `null` (the Workbench does not use it; WPF resources are
addressed by `;component` URIs and load, seen in the window capture); the AppDomain's friendly name is `DefaultDomain` instead of the
executable's name (visible only in WPF's internal window class name `HwndWrapper[DefaultDomain;...]`); there is no application `.config`
file (the managed program's own only declared `supportedRuntime`, which the host does explicitly); an unhandled exception that escapes
`Main` is reported by the host ("stopped unexpectedly", exit 94) instead of Windows Error Reporting.

## 6. Verified, believed, unknown

**Verified on this PC** (x64, Windows 11 Pro Insider 26310, .NET Framework release 534296 = 4.8.1, corporate-managed with Defender and
WDAC; the evidence is `test_host.py`'s `results.json` and the runs named here):

- The single native executable starts the .NET Framework runtime in-process and runs the real managed Workbench from its resource
  bytes: `--version` and `--engine-info` give byte-identical output to the managed executable started directly; the controlled worker
  mode (`--backup-worker`) answers two console-free requests through the host (unknown request, exit 2; preparation not offered, exit 4).
- WPF works natively hosted: the real Workbench window (with `--simulated-console` and test settings) opens, renders its theme, fonts
  and wordmark (captured with `PrintWindow`), is per-monitor-v2 DPI aware like the direct start, and closes with exit 0 on `WM_CLOSE`.
- The main thread is STA; `Process.GetCurrentProcess().MainModule.FileName` is the host, which is what the window passes to its worker
  client as `WorkerExe`. The worker mode was run directly; starting a worker by clicking through the window was not driven in the test.
- The ABI reaches the managed code through AppDomain data; the export cross-check returns the same table; the managed executable started
  directly gets `NotHosted` with the plain sentence and writes nothing.
- PAR2 round trip through managed wrapper -> host ABI -> libpar2 in a folder with a non-ASCII name: create (10%), verify intact, repair of
  intact data writes nothing, damage detected (3 blocks), rebuilt into a new file whose SHA-256 equals the original, the damaged input
  unchanged (hash and time), no leftovers, refusals (existing parity, existing output, wrong file name), not-repairable damage reported
  with nothing written, cancel during create (no parity left) and before verify.
- No file is created or changed beside the executable, and nothing with this product's or its libraries' names and no `.exe/.dll/.sys`
  appears in `%TEMP%` (a watched sentinel TEMP, see 4.4), `%APPDATA%` or `%LOCALAPPDATA%` during all of the runs above.
- The missing-.NET path (simulated through the host's test hook) prints the plain sentence on standard error, or a JSON error event in
  worker mode, exits 90 and loads no runtime.
- The Arm64 executable builds, is an Arm64 GUI PE with the same imports, export and payload resource. It was **not executed**: Windows on
  this x64 PC refuses to start it (WinError 216).
- Two builds of the same inputs are byte-identical after the PE time stamp normalization (before it, two links differed only inside
  that 4-byte field).
- The exit code survives a fully loaded CPU (6 of 6 runs in each final test; 23 of 23 overall with the final exit path, section 5.1).

**Believed (documentation or code reading, not run here):**

- The Arm64 host runs on Windows 11 on Arm with .NET Framework 4.8.1 (native Arm64 CLR, WPF supported there by Microsoft); it refuses
  older .NET Framework with the plain sentence. The x64 host also runs on Windows 11 on Arm under x64 emulation with the x64 .NET
  Framework. Windows 10 on Arm has no x64 emulation, so only the Arm64 host (with 4.8.1) could run there.
- Authenticode-signing the host covers the whole file including the managed payload; the CLR does not check a signature on bytes given
  to `Load(byte[])`, so the payload's integrity rests on the outer signature and the host's own SHA-256 check.
- AMSI: .NET Framework 4.8 passes assemblies loaded from bytes to AMSI (Defender here); that scan did not block any run on this PC.

**Unknown:**

- Arm64 runtime behavior of everything above (host start, WPF, the worker, libpar2), until run on an Arm64 PC.
- SmartScreen reputation and third-party antivirus/EDR reactions: a native process that loads the CLR and an assembly from memory is a
  pattern some security products flag. Not observable here beyond Defender on this PC.
- Behavior on PCs with .NET Framework 4.8 but not 4.8.1, and on Windows 10 (only this Windows 11 Insider PC was used).
- The exact cause of `HOST_E_EXITPROCESS_TIMEOUT` in the runtime's own shutdown when entered from the host (worked around, 5.1).

## 7. Unresolved issues (exact)

1. **Arm64 not executed.** Needs an Arm64 Windows 11 PC: run `CCS-Console-Workbench.exe --host-info`, `--version`, `--engine-info`,
   `--parity-selftest <new folder>`, and open the window; also run the x64 build there under emulation. Nothing here claims it works.
2. **Private reflection for the target framework** (5.2). Works on .NET Framework 4.8/4.8.1 as measured; if a future servicing update
   removed `AppDomain.SetTargetFrameworkName`, the program would silently fall back to 4.0 defaults; the self-test line and the test show it.
3. **No managed shutdown at exit** (5.1): `ProcessExit` handlers and shutdown finalizers do not run in the standalone. Any future code
   that relies on them (none today) would behave differently from Classic Cart Studio's copy of the engine.
4. **PAR2 is single-threaded.** zig carries no OpenMP runtime, so libpar2 is built without `_OPENMP`. Measured here on a loaded PC:
   see 4.3. A multi-threaded build would need LLVM's OpenMP runtime built from pinned source and linked in (not attempted).
5. **Cancel latency.** A cancel takes effect at libpar2's next progress output (normally under 50 ms of work). The Reed-Solomon matrix of
   a large repair is built silently; a cancel during it waits for it. An exception unwinding through libpar2 may leak some heap memory
   of that call (its objects' destructors close every file; the files are then deleted). For a hard stop, run the parity work in the
   controlled worker process, which can be ended.
6. **One file per parity set, recorded name.** By design the ABI refuses multi-file sets and a protected file whose name differs from the
   name recorded in the PAR2 index (PAR2 stores the name). A renamed backup must be verified under its original name.
7. **Heavily damaged files scan slowly.** libpar2 searches every byte offset for displaced blocks when blocks do not match where expected;
   verifying a fully damaged 512 MB file took several minutes here (section 4.3).
8. **GPL source obligation.** The par2cmdline source tarball (pinned, unmodified) must be part of the corresponding source offered with
   each standalone release; `build_host.sh` downloads it by URL and hash but the release process must keep a copy.
9. **x86 (32-bit) Windows** is not built (not requested). The managed payload is AnyCPU and would run, but no host exists for it.
10. **Not integrated into the backup flow yet.** `Par2Native` and the self-test exist; the backup/verify/restore flows, the one-file
    transport outer ZIP and the UI do not call it yet (section 8).

## 8. Integration notes for the Workbench (not done in this branch)

- After a complete verified backup ZIP exists: `Par2Native.Create(zip, zip + ".par2", pct, volumes)` on a background thread (or in the
  worker), record `BlockSize`, `SourceBlocks`, `RecoveryBlocks`, `RecoveryFiles`, `ParityBytes`, the percentage and the library version in
  the backup's record, then `Par2Native.Verify` immediately (the "verify parity" step) and record its result.
- Space: parity bytes are about `pct` x the ZIP size plus about 40 KB of index (measured: 10% of 512 MB -> 51.6 MB; 10% of 8 MB -> 1.2 MB).
- One-file transport: build the outer ZIP from the finished inner ZIP, its `.par2` files and the descriptor; the outer ZIP is not itself
  protected by that parity (no circular protection). Restore-side: verify; if damaged and repairable, `Repair` to a new file and use only
  the rebuilt file after the backup's own SHA-256 check; never promise recovery beyond the intact recovery blocks.
- `Par2Native.Available` is false in Classic Cart Studio for Windows (no host): show its plain sentence, never a missing-feature error.

## 9. Licenses

- par2cmdline 1.4.0: GPL-2.0-or-later (`COPYING` in the tarball), unmodified; the combined Workbench is GPL-3.0-or-later. Credited in
  `tools/console-backup/NOTICE` and `THIRD_PARTY_NOTICES.md` (rows added in this branch).
- libc++, libc++abi, libunwind, compiler-rt: Apache-2.0 WITH LLVM-exception; mingw-w64 runtime: ZPL 2.1 (with public-domain/BSD parts),
  its notice already reproduced in `THIRD_PARTY_NOTICES.md` (rows extended to name the Workbench host).
- The host's own sources: GPL-3.0-or-later, Copyright (C) 2026 Rance9.

