# PyInstaller spec for the Windows companion (one-folder build: starts fast, no self-extraction, fewer false positives than one-file).
# Build:  pyinstaller tools/companion/companion.spec --distpath build/dist --workpath build/work --noconfirm
# The site/ folder (public build of the Studio page + data) is produced by tools/publish/build_public_repo.py before packaging.
import os
HERE = os.path.abspath(os.path.dirname(SPEC)); PROJECT = os.path.normpath(os.path.join(HERE, "..", ".."))
SITE = os.environ.get("NES_SITE_DIR") or os.path.join(PROJECT, "build", "site")
ICON = os.path.join(PROJECT, "tools", "publish", "assets", "app.ico")
a = Analysis([os.path.join(HERE, "companion_app.py")], pathex=[HERE, os.path.join(PROJECT, "tools", "rom-pipeline")],
             binaries=[], datas=[(SITE, "site"), (os.path.join(HERE, "Read-HakchiSelection.ps1"), "."), (os.path.join(HERE, "Get-ProcessWindows.ps1"), "."), (os.path.join(HERE, "Close-Hakchi.ps1"), ".")],
             hiddenimports=["tkinter", "tkinter.filedialog", "tkinter.ttk", "console_engine", "console_engine.worker", "console_engine.engine", "console_engine.plan", "console_engine.profiles", "console_engine.transport", "console_engine.journal", "console_engine.gate", "console_runs"], hookspath=[], runtime_hooks=[], excludes=["matplotlib", "numpy", "PIL", "pytest", "IPython"], noarchive=False)
pyz = PYZ(a.pure)
exe = EXE(pyz, a.scripts, [], exclude_binaries=True, name="NESClassicCollectionStudioCompanion", debug=False, bootloader_ignore_signals=False, strip=False, upx=False, console=False,
          icon=ICON if os.path.exists(ICON) else None, version=os.path.join(HERE, "version_info.txt") if os.path.exists(os.path.join(HERE, "version_info.txt")) else None)
coll = COLLECT(exe, a.binaries, a.datas, strip=False, upx=False, name="NESClassicCollectionStudioCompanion")
