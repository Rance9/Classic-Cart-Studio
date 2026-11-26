"""Every file and folder choice in CCS Console Workbench, Classic Cart Studio for Windows and its website goes through the one CCS dialog:
  Workbench  CcsBackupUi/CcsFileDialog.cs only (no Windows Forms or Microsoft.Win32 file dialogs anywhere else in the window's code, and the
             window's project does not use Windows Forms)
  backend    no tkinter file dialogs (the person chooses in the page's dialog, tools/rom-pipeline/studio/files.js; the page sends the choice)
  page       the browser's own pickers (showOpenFilePicker, showSaveFilePicker, showDirectoryPicker) and file inputs made by script only in
             files.js; the page's static hidden file inputs are never opened by a click
A new page that opens a file or folder some other way fails here.   python test_owned_dialogs.py"""
from __future__ import annotations
import os, re, sys
ROOT = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", ".."))
res = []
def ok(n, c, d=""): res.append(bool(c)); print(("PASS " if c else "FAIL ") + n + (f"  ({d})" if d and not c else ""))
def files(folder, ext):
    for r, _, fs in os.walk(os.path.join(ROOT, folder)):
        if any(x in r for x in ("\\bin", "\\obj", "/bin", "/obj")): continue
        for f in fs:
            if f.endswith(ext): yield os.path.join(r, f)
def rel(p): return os.path.relpath(p, ROOT).replace("\\", "/")

ui = os.path.join("tools", "console-backup", "CcsBackupUi")
bad = [rel(p) for p in files(ui, ".cs") if os.path.basename(p) != "CcsFileDialog.cs" and re.search(r"FolderBrowserDialog|OpenFileDialog|SaveFileDialog|System\.Windows\.Forms|CommonDialog", open(p, encoding="utf-8").read())]
ok("Workbench: no Windows file or folder dialogs outside CcsFileDialog.cs", not bad, bad)
proj = open(os.path.join(ROOT, ui, "CcsBackupUi.csproj"), encoding="utf-8").read()
ok("Workbench: the window's project does not use Windows Forms", "UseWindowsForms" not in proj)
cfd = open(os.path.join(ROOT, ui, "CcsFileDialog.cs"), encoding="utf-8").read()
ok("Workbench: CcsFileDialog.cs is the CCS dialog itself (no Windows dialog inside it either)", not re.search(r"FolderBrowserDialog|Microsoft\.Win32\.(Open|Save)FileDialog|IFileDialog", cfd))
calls = sum(len(re.findall(r"CcsFileDialog\.(ChooseFolder|ChooseExportFolder|OpenFiles|OpenFile|SaveFile)\(", open(p, encoding="utf-8").read())) for p in files(ui, ".cs"))
ok("Workbench: the pages call the shared dialog", calls >= 30, calls)

bad = [rel(p) for p in files(os.path.join("tools", "companion"), ".py") if not os.path.basename(p).startswith("test_") and re.search(r"filedialog|askopenfilename|asksaveasfilename|askdirectory", open(p, encoding="utf-8").read())]
ok("backend: no tkinter file dialogs", not bad, bad)

studio = os.path.join("tools", "rom-pipeline", "studio")
bad = []
for p in files(studio, ".js"):
    if os.path.basename(p) == "files.js": continue
    t = open(p, encoding="utf-8").read()
    if re.search(r"show(Open|Save)FilePicker\s*\(|showDirectoryPicker\s*\(|\.type\s*=\s*['\"]file['\"]", t): bad.append(rel(p))
ok("page: the browser's pickers and script-made file inputs only in files.js", not bad, bad)
bad = []
for p in list(files(studio, ".js")) + [os.path.join(ROOT, "tools", "rom-pipeline", "build_html.py")]:
    t = open(p, encoding="utf-8").read()
    for m in re.finditer(r"<input[^>]*type=[\"']file[\"'][^>]*id=[\"']([A-Za-z0-9_]+)[\"']", t):
        for q in list(files(studio, ".js")):
            if re.search(r"\$\(['\"]" + m.group(1) + r"['\"]\)\.click\(\)", open(q, encoding="utf-8").read()): bad.append(f"{m.group(1)} opened in {rel(q)}")
ok("page: the static hidden file inputs are never opened by a click", not bad, bad)
# the browser's own confirm/alert/prompt (they show the page's address and the browser's look): the page's own ccsAsk/ccsTell/ccsPrompt
# instead, in every page script: the Install on Console flow (install.js) and build_html.py's page script included.
NATIVE = re.compile(r"(^|[^.\w$])(window\.)?(confirm|alert|prompt)\s*\(")
bad = []
for p in list(files(studio, ".js")) + [os.path.join(ROOT, "tools", "rom-pipeline", "build_html.py")]:
    for i, line in enumerate(open(p, encoding="utf-8").read().split("\n"), 1):
        code = line.split("//")[0] if line.lstrip().startswith("//") else line
        if NATIVE.search(code) and "function " + "confirm(" not in code: bad.append(f"{rel(p)}:{i}")
ok("page: no browser confirm(), alert() or prompt() (the page's own dialog instead)", not bad, bad)
fj = open(os.path.join(ROOT, studio, "files.js"), encoding="utf-8").read()
ok("page: files.js has the app's dialog, the website's pickers and their fallbacks", all(x in fj for x in ("/api/files/list", "/api/files/places", "/api/files/mkdir", "showOpenFilePicker", "showSaveFilePicker", "showDirectoryPicker", "type='file'")))
# one name, one function: the page is one script (build_html.py's page script and every studio script it includes), so a second top-level
# function of the same name silently replaces the first (the warnings' titled question and the one-text question were both ccsAsk once,
# and the warning lost its text). These four are earlier page functions that the studio scripts replace on purpose.
REPLACED = {"buildManifest", "companionStatus", "hkLabelUpdate", "hkPost"}
bh = open(os.path.join(ROOT, "tools", "rom-pipeline", "build_html.py"), encoding="utf-8").read()
seen = {}
for label, text in [("build_html.py", bh)] + [("studio/" + n, open(os.path.join(ROOT, studio, n), encoding="utf-8").read()) for n in sorted(set(re.findall(r'part\("([a-z0-9-]+\.js)"\)', bh)))]:
    for i, line in enumerate(text.split("\n"), 1):
        m = re.match(r"(?:async\s+)?function\s+([A-Za-z_$][\w$]*)\s*\(", line)
        if m: seen.setdefault(m.group(1), []).append(f"{label}:{i}")
dups = {k: v for k, v in seen.items() if len(v) > 1 and k not in REPLACED}
ok("page: no two top-level functions of the page share a name (except the four earlier ones the studio scripts replace)", not dups and all(len(seen.get(k, [])) == 2 for k in REPLACED), dups)
print(f"{sum(res)}/{len(res)} passed")
sys.exit(0 if all(res) else 1)
