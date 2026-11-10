# PyInstaller spec for the Windows companion (one-folder build: starts fast, no self-extraction, fewer false positives than one-file).
# Build:  pyinstaller tools/companion/companion.spec --distpath build/dist --workpath build/work --noconfirm
# The site/ folder (public build of the Studio page + data) is produced by tools/publish/build_public_repo.py before packaging.
import os
HERE = os.path.abspath(os.path.dirname(SPEC)); PROJECT = os.path.normpath(os.path.join(HERE, "..", ".."))
SITE = os.environ.get("NES_SITE_DIR") or os.path.join(PROJECT, "build", "site")
ICON = os.path.join(PROJECT, "tools", "publish", "assets", "app.ico")
# the native HOME runtime for the console (32-bit ARM, static; sim.ps1 build-arm -> arm-musl-device/ccs-home-armv7): bundled when
# provided; without it the app still prepares packages but reports them "not ready: runtime missing" (never install-ready)
_rt = os.environ.get("CCS_NATIVE_HOME_RUNTIME") or r"C:\NES\work\console-simulator\build\arm-musl-device\ccs-home-armv7"
NATIVE = [(_rt, "native")] if os.path.isfile(_rt) else []
# D-113: the game player for the console (32-bit ARM, static; build_player.sh console -> arm-musl-device/ccs-player-armv7), beside the runtime
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
WORKER_FILES = ("ccs-console-worker.exe", "ccs-console-worker.exe.config", "CcsWorker.dll", "Renci.SshNet.dll", "LibUsbDotNet.LibUsbDotNet.dll", "NOTICE", "SOURCE.md")
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
# HOME Music (D-81): the native PCM copy of the selected song (the game window's HOME and every console HOME package; the site has its own M4A)
_hm = os.path.join(PROJECT, "tools", "publish", "assets", "home-music")
if not os.path.isfile(os.path.join(_hm, "ccs-home-theme.wav")): raise SystemExit(f"the HOME Music file is missing: {_hm!r}")
# D-113: the five console startup images (1280 x 720 derivatives, thumbnails, manifest) for the console packages this app builds
_st = os.path.join(PROJECT, "tools", "publish", "assets", "startup")
if not os.path.isfile(os.path.join(_st, "startup-images.json")): raise SystemExit(f"the console startup images are missing: {_st!r}")
DATAS = [(SITE_BUNDLED, "site"), (os.path.join(HERE, "native_home", "fonts"), os.path.join("native_home", "fonts")), (_hm, "home-music"), (_st, "startup")] + NATIVE + EXTRA + [(os.path.join(HERE, "Read-HakchiSelection.ps1"), "."), (os.path.join(HERE, "Get-ProcessWindows.ps1"), "."), (os.path.join(HERE, "Close-Hakchi.ps1"), ".")]
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
a = Analysis([os.path.join(HERE, "companion_app.py")], pathex=[HERE, os.path.join(PROJECT, "tools", "rom-pipeline")],
             binaries=[], datas=DATAS,
             hiddenimports=["bundle_integrity", "metrics_relay", "package_api", "ccs_package", "console_engine.storage_model", "tkinter", "tkinter.filedialog", "tkinter.ttk", "console_engine", "console_engine.worker", "console_engine.engine", "console_engine.plan", "console_engine.profiles", "console_engine.transport", "console_engine.journal", "console_engine.gate", "console_engine.ccs_install", "console_engine.worker_console", "console_runs", "console_home", "console_confirm", "native_preview", "overrides", "mock_cover", "native_home", "native_home.package", "native_home.config", "native_home.derive", "PIL.Image", "PIL.ImageDraw", "PIL.ImageFont", "PIL.JpegImagePlugin", "PIL.PngImagePlugin"], hookspath=[], runtime_hooks=[], excludes=["matplotlib", "numpy", "pytest", "IPython"], noarchive=False)
pyz = PYZ(a.pure)
exe = EXE(pyz, a.scripts, [], exclude_binaries=True, name="ClassicCartStudioBackend", debug=False, bootloader_ignore_signals=False, strip=False, upx=False, console=False,
          icon=ICON if os.path.exists(ICON) else None, version=os.path.join(HERE, "version_info.txt") if os.path.exists(os.path.join(HERE, "version_info.txt")) else None)
coll = COLLECT(exe, a.binaries, a.datas, strip=False, upx=False, name="ClassicCartStudioBackend")
