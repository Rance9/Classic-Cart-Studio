"""Write the Windows version resource for the frozen companion (PyInstaller --version-file).   python write_version_info.py <out.txt>"""
from __future__ import annotations
import os, sys
sys.path.insert(0, os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "companion"))
from version import VERSION

def main(out: str):
    parts = [int(x) for x in VERSION.split("-")[0].split(".")] + [0, 0, 0, 0]; v = tuple(parts[:4]); vs = ".".join(map(str, v))
    txt = f"""VSVersionInfo(
  ffi=FixedFileInfo(filevers={v}, prodvers={v}, mask=0x3f, flags=0x0, OS=0x40004, fileType=0x1, subtype=0x0, date=(0, 0)),
  kids=[StringFileInfo([StringTable('040904B0', [
    StringStruct('CompanyName', 'NES Classic Collection Studio project (independent; not affiliated with Nintendo)'),
    StringStruct('FileDescription', 'NES Classic Collection Studio Companion'),
    StringStruct('FileVersion', '{vs}'), StringStruct('ProductVersion', '{VERSION}'),
    StringStruct('InternalName', 'NESClassicCollectionStudioCompanion'), StringStruct('OriginalFilename', 'NESClassicCollectionStudioCompanion.exe'),
    StringStruct('ProductName', 'NES Classic Collection Studio Companion'),
    StringStruct('LegalCopyright', 'MIT License. Not affiliated with Nintendo.')])]), VarFileInfo([VarStruct('Translation', [1033, 1200])])])
"""
    open(out, "w", encoding="utf-8").write(txt); print("wrote", out, VERSION)

if __name__ == "__main__":
    main(sys.argv[1])
