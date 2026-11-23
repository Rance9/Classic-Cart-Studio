# PyInstaller spec for the Windows companion (one-folder build: starts fast, no self-extraction, fewer false positives than one-file).
# Build:  pyinstaller tools/companion/companion.spec --distpath build/dist --workpath build/work --noconfirm
# The site/ folder (public build of the Studio page + data) is produced by tools/publish/build_public_repo.py before packaging.
import os
HERE = os.path.abspath(os.path.dirname(SPEC)); PROJECT = os.path.normpath(os.path.join(HERE, "..", ".."))
if os.environ.get("NES_SITE_DIR"): raise SystemExit("NES_SITE_DIR is not read: set CCS_SITE_DIR instead (nothing was built)")
SITE = os.environ.get("CCS_SITE_DIR") or os.path.join(PROJECT, "build", "site")
ICON = os.path.join(PROJECT, "tools", "publish", "assets", "app.ico")
# the native HOME runtime for the console (32-bit ARM, static; sim.ps1 build-arm -> arm-musl-device/ccs-home-armv7): bundled when
# provided; without it the app still prepares packages but reports them "not ready: runtime missing" (never install-ready)
_rt = os.environ.get("CCS_NATIVE_HOME_RUNTIME") or r"C:\NES\work\console-simulator\build\arm-musl-device\ccs-home-armv7"
NATIVE = [(_rt, "native")] if os.path.isfile(_rt) else []
# the game player for the console (32-bit ARM, static; build_player.sh console -> arm-musl-device/ccs-player-armv7), beside the runtime
_cp = os.environ.get("CCS_CONSOLE_PLAYER") or os.path.join(os.path.dirname(_rt), "ccs-player-armv7")
NATIVE += [(_cp, "native")] if os.path.isfile(_cp) else []
# N3: the same native HOME renderer built for Windows draws Preview on TV (tools/companion/native_preview.py)
_pv = os.environ.get("CCS_NATIVE_PREVIEW_CORE") or r"C:\NES\work\console-simulator\build\windows-x86_64\ccs-sim-core.exe"
NATIVE += [(_pv, "native")] if os.path.isfile(_pv) else []
# M3: the game player (Play on PC; tools/player) is built beside the preview core (wsl_build.sh windows-x86_64); its menu fonts are baked
# here from the site's own HOME fonts (tools/player/bake_fonts.py, the native HOME's atlas format)
_pl = os.environ.get("CCS_PLAYER_EXE") or os.path.join(os.path.dirname(_pv), "ccs-player.exe")
PLAYER = []
if os.path.isfile(_pl) and os.path.isdir(os.path.join(SITE, "brand", "home")):
    import subprocess as _sp, sys as _sys, tempfile as _tf
    _pf = os.path.join(_tf.mkdtemp(prefix="ccs-player-fonts-"), "fonts")
    _sp.run([_sys.executable, os.path.join(PROJECT, "tools", "player", "bake_fonts.py"), os.path.join(SITE, "brand", "home"), _pf], check=True)
    PLAYER = [(_pl, "player"), (_pf, os.path.join("player", "fonts")), (os.path.join(PROJECT, "tools", "publish", "assets", "app.ico"), "player")]
if os.environ.get("CCS_REQUIRE_NATIVE") and not PLAYER:
    raise SystemExit(f"the game player is missing for a release build: {_pl!r} present={os.path.isfile(_pl)}")
NATIVE += PLAYER
# a release build (CCS_REQUIRE_NATIVE, set by the release workflow) never ships without them: a distributed app would otherwise
# report "Not ready: runtime missing" and fall back to the browser preview (N3.1)
if os.environ.get("CCS_REQUIRE_NATIVE") and len(NATIVE) - len(PLAYER) != 3:
    raise SystemExit(f"native HOME binaries missing for a release build: runtime {_rt!r} present={os.path.isfile(_rt)}, console player {_cp!r} present={os.path.isfile(_cp)}, preview core {_pv!r} present={os.path.isfile(_pv)}")
# the console transport worker (tools/console-worker: ccs-console-worker.exe, GPL-3.0-or-later, derived from Hakchi2 CE 3.9.3's clovershell
# and SSH code; every real connection is closed in its own code, Gate G1) is REQUIRED in every build from this spec: CCS_CONSOLE_WORKER_DIR
# is the release folder tools/console-worker/build-worker.ps1 wrote, and every file its BUILD-INFO.json lists (the program, NOTICE,
# SOURCE.md, licenses) must be there with exactly that size and SHA-256, built from the pinned Hakchi2 CE 3.9.3 and SSH.NET commits. The
# files and BUILD-INFO.json are bundled in console-worker/ and covered by the installation's file list; console-worker-source.zip (the
# corresponding source) is published beside the installer, not bundled.
WORKER_HAKCHI = "25bfcfd04b8ed12f7d513dc87c2a0f7096d38ebc"; WORKER_SSHNET = "32181aeb4ce69e8f07e5e1d585fd592a44017bc1"
WORKER_FILES = ("ccs-console-worker.exe", "ccs-console-worker.exe.config", "CcsWorker.dll", "CcsBackup.dll", "CcsConsole.dll", "Renci.SshNet.dll", "LibUsbDotNet.LibUsbDotNet.dll", "NOTICE", "SOURCE.md")
def _console_worker(d):
    import hashlib as _hl, json as _js
    def _sha(p):
        h = _hl.sha256()
        with open(p, "rb") as f:
            for b in iter(lambda: f.read(1 << 20), b""): h.update(b)
        return h.hexdigest()
    if not d or not os.path.isdir(d): raise SystemExit("the console worker is required: set CCS_CONSOLE_WORKER_DIR to the release folder tools/console-worker/build-worker.ps1 wrote")
    try:
        with open(os.path.join(d, "BUILD-INFO.json"), encoding="utf-8-sig") as f: info = _js.load(f)
    except (OSError, ValueError) as e: raise SystemExit(f"the console worker in {d!r} has no readable BUILD-INFO.json ({e})")
    if not isinstance(info, dict) or info.get("schema") != "ccs-console-worker-build/1": raise SystemExit(f"{d!r}: BUILD-INFO.json is not a ccs-console-worker-build/1 record")
    hk = info.get("hakchi") or {}
    if hk.get("commit") != WORKER_HAKCHI or (hk.get("submodules") or {}).get("Libraries/SSH.NET") != WORKER_SSHNET:
        raise SystemExit(f"{d!r}: the console worker was not built from the pinned Hakchi2 CE 3.9.3 ({WORKER_HAKCHI[:7]}) and SSH.NET ({WORKER_SSHNET[:7]}) commits")
    files = info.get("files")
    if not isinstance(files, dict) or any(f not in files for f in WORKER_FILES) or not any(f.startswith("licenses/") for f in files):
        raise SystemExit(f"{d!r}: BUILD-INFO.json does not list the whole worker ({', '.join(WORKER_FILES)}, licenses/)")
    out = []
    for rel, rec in sorted(files.items()):
        parts = rel.split("/")
        if not rel or "\\" in rel or rel.startswith("/") or any(p in ("", ".", "..") for p in parts) or rel in ("BUILD-INFO.json", "console-worker-source.zip"): raise SystemExit(f"{d!r}: BUILD-INFO.json lists a path not allowed: {rel!r}")
        p = os.path.join(d, *parts)
        if not os.path.isfile(p) or not isinstance(rec, dict) or os.path.getsize(p) != rec.get("size") or _sha(p) != rec.get("sha256"):
            raise SystemExit(f"{d!r}: {rel} is missing or differs from its BUILD-INFO.json")
        out.append((p, os.path.join("console-worker", *parts[:-1])))
    listed = {os.path.normcase(os.path.abspath(p)) for p, _ in out}
    extra = [os.path.relpath(os.path.join(a, n), d) for a, _, ns in os.walk(d) for n in ns if os.path.normcase(os.path.abspath(os.path.join(a, n))) not in listed and n not in ("BUILD-INFO.json", "console-worker-source.zip")]
    if extra: raise SystemExit(f"{d!r}: files BUILD-INFO.json does not list: {extra[:5]}")
    return out + [(os.path.join(d, "BUILD-INFO.json"), "console-worker")]
WORKER = _console_worker(os.environ.get("CCS_CONSOLE_WORKER_DIR") or "")
NATIVE += WORKER
# the pinned Hakchi2 CE package (console tools and the console USB driver) ships inside the application; it is never downloaded at run
# time (tools/publish/fetch_hakchi_package.py or build_windows_local.py provide it and set CCS_HAKCHI_PACKAGE after verifying its SHA-256)
_hk = os.path.abspath(os.environ["CCS_HAKCHI_PACKAGE"]) if os.environ.get("CCS_HAKCHI_PACKAGE") else ""
EXTRA = [(_hk, "packages")] if os.path.isfile(_hk) else []
RELEASE = bool(os.environ.get("CCS_RELEASE_BUILD") or os.environ.get("CCS_REQUIRE_NATIVE"))
_tpn = os.path.join(PROJECT, "tools", "publish", "repo-files", "THIRD_PARTY_NOTICES.md")
_tpn = _tpn if os.path.isfile(_tpn) else os.path.join(PROJECT, "THIRD_PARTY_NOTICES.md")   # the public repository keeps it at the root
EXTRA += [(_tpn, ".")] if os.path.isfile(_tpn) else []
# CCS Mod / CCS Nucleus (tools/console-nucleus): the installer builds the same pinned package the CCS Console Workbench places (console_engine/
# ccs_install.py method_package); the sources and their builder ship, never a separately built package
_nu = os.path.join(PROJECT, "tools", "console-nucleus")
EXTRA += [(os.path.join(_nu, "console"), os.path.join("console-nucleus", "console")), (os.path.join(_nu, "build_packages.py"), "console-nucleus")]
# the website's Store (site/store: curated seller links, Amazon Associates links once published) is never part of the application:
# Amazon Associates forbids Special Links in installable client software, and the app opens https://classiccartstudio.com/store/ in the
# browser instead. PyInstaller copies a data folder whole, so the site is bundled from a staging copy without store/ (hard links where
# possible, plain copies otherwise); the staging folder lives until PyInstaller has collected it and is removed when the build exits.
import shutil, tempfile, atexit
SITE_EXCLUDE = {"store"}   # top-level folders of the site that never ship in the application
def _stage_site(site):
    if not os.path.isdir(site): return site   # no site: keep the original path so PyInstaller reports it as before
    stage = os.path.join(tempfile.mkdtemp(prefix="ccs-site-"), "site"); atexit.register(shutil.rmtree, os.path.dirname(stage), True)
    top = os.path.normcase(os.path.abspath(site))
    def _ignore(d, names): return [n for n in names if os.path.normcase(os.path.abspath(d)) == top and n.lower() in SITE_EXCLUDE]
    def _link_or_copy(src, dst):
        try: os.link(src, dst)
        except OSError: shutil.copy2(src, dst)
        return dst
    shutil.copytree(site, stage, ignore=_ignore, copy_function=_link_or_copy)
    return stage
SITE_BUNDLED = _stage_site(SITE)
# HOME Music: the native PCM copy of the selected song (the game window's HOME and every console HOME package; the site has its own M4A)
_hm = os.path.join(PROJECT, "tools", "publish", "assets", "home-music")
if not os.path.isfile(os.path.join(_hm, "ccs-home-theme.wav")): raise SystemExit(f"the HOME Music file is missing: {_hm!r}")
# the five console startup images (1280 x 720 derivatives, thumbnails, manifest) for the console packages this app builds
_st = os.path.join(PROJECT, "tools", "publish", "assets", "startup")
if not os.path.isfile(os.path.join(_st, "startup-images.json")): raise SystemExit(f"the console startup images are missing: {_st!r}")
# the records every console package's notices are made from (native_home/notices.py RECORDS), at their repository paths under notices/
import sys; sys.path[:0] = [HERE, os.path.join(PROJECT, "tools", "rom-pipeline")]
from native_home import notices as _notices
_nt = _notices.stage(os.path.join(tempfile.mkdtemp(prefix="ccs-notices-"), "notices")); atexit.register(shutil.rmtree, os.path.dirname(_nt), True)
DATAS = [(SITE_BUNDLED, "site"), (_nt, "notices"), (os.path.join(HERE, "native_home", "fonts"), os.path.join("native_home", "fonts")), (_hm, "home-music"), (_st, "startup")] + NATIVE + EXTRA + [(os.path.join(HERE, "Read-HakchiSelection.ps1"), "."), (os.path.join(HERE, "Get-ProcessWindows.ps1"), "."), (os.path.join(HERE, "Close-Hakchi.ps1"), ".")]
# the installation's file list (every bundled file with its size and SHA-256): the installed application checks itself against it and
# reports a missing or damaged component as an installation to repair (tools/companion/bundle_integrity.py)
import sys, hashlib, json, tempfile
sys.path[:0] = [HERE, os.path.join(PROJECT, "tools", "rom-pipeline")]
import bundle_integrity as BI
from hakchi_bridge import HAKCHI_PACKAGE as _HP
if RELEASE:   # a release build never ships without a required component
    if not os.path.isfile(_hk): raise SystemExit("release build: the Hakchi2 CE 3.9.3 package is missing (set CCS_HAKCHI_PACKAGE; tools/publish/fetch_hakchi_package.py)")
    if os.path.getsize(_hk) != _HP["size"] or BI.sha256(_hk) != _HP["sha256"]: raise SystemExit(f"release build: {_hk} is not the recorded Hakchi2 CE 3.9.3 package")
_man = BI.build_manifest(DATAS)
if RELEASE and _man["required_missing"]: raise SystemExit(f"release build: required components missing: {_man['required_missing']}")
_excl = [f for f in _man["files"] if any(f.lower().startswith(f"site/{x}/") for x in SITE_EXCLUDE)]
if _excl: raise SystemExit(f"the website's store must never be bundled in the application: {_excl[:5]}")
_mdir = tempfile.mkdtemp(prefix="ccs-bundle-"); _mp = os.path.join(_mdir, BI.MANIFEST_NAME)
json.dump(_man, open(_mp, "w", encoding="utf-8"), indent=0); DATAS.append((_mp, "."))
# v1.0 payload audit (v1p): proven leftovers left out of the frozen application.
#   PIL._avif (7.9 MB): the AVIF codec. CCS accepts PNG, JPEG, WebP, GIF and BMP only (overrides.FORMATS, folder_art.ALLOWED); an AVIF
#     file is refused with the same message either way, and Pillow's AvifImagePlugin registers nothing without it (SUPPORTED = False).
#   Tcl's time-zone database (_tcl_data/tzdata, 1.2 MB): only Tcl's clock command with time zones reads it; CCS uses Tk for the native
#     file, folder and Save As pickers and the standalone status window (filedialog, messagebox, ttk), none of which do.
#   pyreadline3: a console line editor that PyInstaller substitutes for readline (wanted only by the code module and sqlite3's command
#     line, which a windowless backend never runs) when it happens to be installed on the build computer; the release workflow never
#     installs it, so leaving it out also makes a local build match the published one.
PRUNED_MODULES = ["PIL._avif", "pyreadline3"]
PRUNED_DATA = ("_tcl_data/tzdata/",)
# The runtime's own notices (v1p): the build's Python and Pillow license files (the exact texts of the copies bundled: Python, bzip2,
# libffi and Microsoft's redistributable runtime; Pillow and the image libraries it carries) and the texts kept in the repository
# (tools/publish/assets/legal/runtime: the incorporated-software notices of the build's CPython version, with OpenSSL, expat, libffi, zlib and
# libmpdec; Tcl/Tk).
import importlib.metadata as _md
_lic_py = os.path.join(sys.base_prefix, "LICENSE.txt")
_pil = _md.distribution("pillow"); _lic_pil = [str(_pil.locate_file(f)) for f in (_pil.files or []) if os.path.basename(str(f)).upper().startswith("LICENSE")]
if not os.path.isfile(_lic_py) or not _lic_pil: raise SystemExit("the build environment's Python or Pillow license file was not found")
_rt = os.path.join(PROJECT, "tools", "publish", "assets", "legal", "runtime"); _cpy = os.path.join(_rt, "cpython-%d.%d-license.rst" % sys.version_info[:2])
if not os.path.isfile(_cpy): raise SystemExit(f"no CPython incorporated-software notice for Python {sys.version_info[0]}.{sys.version_info[1]} in {_rt}: add that version's Doc/license.rst")
LICENSES = [(_lic_py, os.path.join("licenses", "python")), (_lic_pil[0], os.path.join("licenses", "pillow")), (_cpy, os.path.join("licenses", "runtime")),
            (os.path.join(_rt, "tcl-tk-8.6-license.terms"), os.path.join("licenses", "runtime")), (os.path.join(_rt, "README.md"), os.path.join("licenses", "runtime"))]
a = Analysis([os.path.join(HERE, "companion_app.py")], pathex=[HERE, os.path.join(PROJECT, "tools", "rom-pipeline")],
             binaries=[], datas=DATAS + LICENSES,
             hiddenimports=["bundle_integrity", "metrics_relay", "app_update", "authenticode", "package_api", "ccs_package", "startup_picture", "console_engine.storage_model", "tkinter", "tkinter.filedialog", "tkinter.ttk", "console_engine", "console_engine.worker", "console_engine.engine", "console_engine.plan", "console_engine.profiles", "console_engine.transport", "console_engine.journal", "console_engine.gate", "console_engine.ccs_install", "console_engine.hakchi_publish", "console_engine.games_only", "console_engine.worker_console", "console_runs", "console_home", "console_confirm", "native_preview", "overrides", "mock_cover", "native_home", "native_home.package", "native_home.config", "native_home.derive", "native_home.design_ingest", "native_home.design_compile", "native_home.own_sprites", "design_embed", "PIL.Image", "PIL.ImageDraw", "PIL.ImageFont", "PIL.JpegImagePlugin", "PIL.PngImagePlugin"], hookspath=[], runtime_hooks=[], excludes=["matplotlib", "numpy", "pytest", "IPython"] + PRUNED_MODULES, noarchive=False)
a.datas = [d for d in a.datas if not d[0].replace("\\", "/").startswith(PRUNED_DATA)]
pyz = PYZ(a.pure)
exe = EXE(pyz, a.scripts, [], exclude_binaries=True, name="ClassicCartStudioBackend", debug=False, bootloader_ignore_signals=False, strip=False, upx=False, console=False,
          icon=ICON if os.path.exists(ICON) else None, version=os.path.join(HERE, "version_info.txt") if os.path.exists(os.path.join(HERE, "version_info.txt")) else None)
coll = COLLECT(exe, a.binaries, a.datas, strip=False, upx=False, name="ClassicCartStudioBackend")
