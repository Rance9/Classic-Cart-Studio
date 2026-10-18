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
# offline first (0.5.0): the pinned Hakchi2 CE package (with its console USB driver) installed with the application when the build
# provides it (tools/publish/build_windows_local.py sets CCS_HAKCHI_PACKAGE after verifying its SHA-256); otherwise it is downloaded on request
_hk = os.path.abspath(os.environ["CCS_HAKCHI_PACKAGE"]) if os.environ.get("CCS_HAKCHI_PACKAGE") else ""
EXTRA = [(_hk, "packages")] if os.path.isfile(_hk) else []
_tpn = os.path.join(PROJECT, "tools", "publish", "repo-files", "THIRD_PARTY_NOTICES.md")
_tpn = _tpn if os.path.isfile(_tpn) else os.path.join(PROJECT, "THIRD_PARTY_NOTICES.md")   # the public repository keeps it at the root
EXTRA += [(_tpn, ".")] if os.path.isfile(_tpn) else []
a = Analysis([os.path.join(HERE, "companion_app.py")], pathex=[HERE, os.path.join(PROJECT, "tools", "rom-pipeline")],
             binaries=[], datas=[(SITE, "site"), (os.path.join(HERE, "native_home", "fonts"), os.path.join("native_home", "fonts"))] + NATIVE + EXTRA + [(os.path.join(HERE, "Read-HakchiSelection.ps1"), "."), (os.path.join(HERE, "Get-ProcessWindows.ps1"), "."), (os.path.join(HERE, "Close-Hakchi.ps1"), ".")],
             hiddenimports=["tkinter", "tkinter.filedialog", "tkinter.ttk", "console_engine", "console_engine.worker", "console_engine.engine", "console_engine.plan", "console_engine.profiles", "console_engine.transport", "console_engine.journal", "console_engine.gate", "console_runs", "native_preview", "overrides", "mock_cover", "native_home", "native_home.package", "native_home.config", "native_home.derive", "PIL.Image", "PIL.ImageDraw", "PIL.ImageFont", "PIL.JpegImagePlugin", "PIL.PngImagePlugin"], hookspath=[], runtime_hooks=[], excludes=["matplotlib", "numpy", "pytest", "IPython"], noarchive=False)
pyz = PYZ(a.pure)
exe = EXE(pyz, a.scripts, [], exclude_binaries=True, name="ClassicCartStudioBackend", debug=False, bootloader_ignore_signals=False, strip=False, upx=False, console=False,
          icon=ICON if os.path.exists(ICON) else None, version=os.path.join(HERE, "version_info.txt") if os.path.exists(os.path.join(HERE, "version_info.txt")) else None)
coll = COLLECT(exe, a.binaries, a.datas, strip=False, upx=False, name="ClassicCartStudioBackend")
