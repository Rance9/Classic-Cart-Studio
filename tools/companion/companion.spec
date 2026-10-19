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
# N3: the same native HOME renderer built for Windows draws Preview on TV (tools/companion/native_preview.py)
_pv = os.environ.get("CCS_NATIVE_PREVIEW_CORE") or r"C:\NES\work\console-simulator\build\windows-x86_64\ccs-sim-core.exe"
NATIVE += [(_pv, "native")] if os.path.isfile(_pv) else []
# a release build (CCS_REQUIRE_NATIVE, set by the release workflow) never ships without them: a distributed app would otherwise
# report "Not ready: runtime missing" and fall back to the browser preview (N3.1)
if os.environ.get("CCS_REQUIRE_NATIVE") and len(NATIVE) != 2:
    raise SystemExit(f"native HOME binaries missing for a release build: runtime {_rt!r} present={os.path.isfile(_rt)}, preview core {_pv!r} present={os.path.isfile(_pv)}")
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
DATAS = [(SITE_BUNDLED, "site"), (os.path.join(HERE, "native_home", "fonts"), os.path.join("native_home", "fonts"))] + NATIVE + EXTRA + [(os.path.join(HERE, "Read-HakchiSelection.ps1"), "."), (os.path.join(HERE, "Get-ProcessWindows.ps1"), "."), (os.path.join(HERE, "Close-Hakchi.ps1"), ".")]
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
             hiddenimports=["bundle_integrity", "tkinter", "tkinter.filedialog", "tkinter.ttk", "console_engine", "console_engine.worker", "console_engine.engine", "console_engine.plan", "console_engine.profiles", "console_engine.transport", "console_engine.journal", "console_engine.gate", "console_runs", "native_preview", "overrides", "mock_cover", "native_home", "native_home.package", "native_home.config", "native_home.derive", "PIL.Image", "PIL.ImageDraw", "PIL.ImageFont", "PIL.JpegImagePlugin", "PIL.PngImagePlugin"], hookspath=[], runtime_hooks=[], excludes=["matplotlib", "numpy", "pytest", "IPython"], noarchive=False)
pyz = PYZ(a.pure)
exe = EXE(pyz, a.scripts, [], exclude_binaries=True, name="ClassicCartStudioBackend", debug=False, bootloader_ignore_signals=False, strip=False, upx=False, console=False,
          icon=ICON if os.path.exists(ICON) else None, version=os.path.join(HERE, "version_info.txt") if os.path.exists(os.path.join(HERE, "version_info.txt")) else None)
coll = COLLECT(exe, a.binaries, a.datas, strip=False, upx=False, name="ClassicCartStudioBackend")
