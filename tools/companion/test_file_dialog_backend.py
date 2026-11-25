r"""Classic Cart Studio for Windows' own file dialog, its backend half (local_files.py, /api/files/*), on a real backend process with the
desktop principal and an isolated data folder, over a sample folder made here (the places come from the test's own configuration, so
nothing else on the PC is listed):
  places     the configured places (Desktop/Documents/Downloads/Pictures, This PC, drives) and This PC's drives
  list       a folder's folders and files with size, date and kind; hidden and system items left out; plain answers for a missing folder,
             a file, a path that is not a full path; a folder path longer than 260 characters
  mkdir      New Folder with a Unicode name; a name already there, a name Windows cannot use and a reserved name refused, nothing made
  stat       a file, a folder, a missing path
  grant      the chosen file read back exactly (and only with its own token); the chosen save written whole, once per choice, nothing
             left beside it; a save over an existing file only replaces it after the write is complete
  principal  the routes answer only the app's own window (no token, or a token from another origin: refused)
  routes     Add Your Own Game and Add Games from Files take the chosen path(s); without a choice nothing is asked and nothing changes
  python test_file_dialog_backend.py <built site folder> [--out results.json]"""
from __future__ import annotations
import argparse, ctypes, json, os, shutil, sys, tempfile, time, urllib.error, urllib.request
HERE = os.path.dirname(os.path.abspath(__file__)); sys.path.insert(0, HERE)
from test_included_backend import Backend

def nes_rom(seed: int) -> bytes:
    return b"NES\x1a\x01\x00\x00\x00" + b"\x00" * 8 + bytes((seed + i) % 251 for i in range(16384))

def main() -> int:
    ap = argparse.ArgumentParser(); ap.add_argument("site"); ap.add_argument("--out", default=""); a = ap.parse_args()
    site = os.path.abspath(a.site); work = tempfile.mkdtemp(prefix="ccs-file-dialog-")
    res = []
    def ok(n, c, d=""): res.append((n, bool(c))); print(("PASS " if c else "FAIL ") + n + (f"  ({str(d)[:300]})" if d and not c else ""), flush=True)
    sample = os.path.join(work, "Sample Library"); docs = os.path.join(sample, "Documents"); os.makedirs(os.path.join(docs, "Saves"))
    for n in ("Desktop", "Downloads", "Pictures"): os.makedirs(os.path.join(sample, n))
    open(os.path.join(docs, "Game one.nes"), "wb").write(nes_rom(1)); open(os.path.join(docs, "Game two.nes"), "wb").write(nes_rom(2))
    open(os.path.join(docs, "notes.txt"), "w").write("notes")
    hidden = os.path.join(docs, "hidden.txt"); open(hidden, "w").write("x"); ctypes.windll.kernel32.SetFileAttributesW(hidden, 2)
    places = [{"name": "Desktop", "path": os.path.join(sample, "Desktop")}, {"name": "Documents", "path": docs}, {"name": "Downloads", "path": os.path.join(sample, "Downloads")},
              {"name": "Pictures", "path": os.path.join(sample, "Pictures")}, {"name": "This PC", "path": "", "pc": True}, {"name": "Sample drive (" + sample[:2] + ")", "path": sample[:3], "drive": True}]
    cfg = os.path.join(work, "config.json"); os.makedirs(os.path.join(work, "tree"))
    json.dump({"rehearsal_tree": os.path.join(work, "tree"), "file_places": places}, open(cfg, "w"))
    os.makedirs(os.path.join(work, "A")); B = None
    try:
        B = Backend(site, os.path.join(work, "A"), cfg)
        def raw(path, data=None, headers=None, method="POST"):
            q = urllib.request.Request(B.base + path, data=data, method=method, headers=headers or {})
            try:
                with urllib.request.urlopen(q, timeout=60) as r: return r.status, r.read()
            except urllib.error.HTTPError as e: return e.code, e.read()
        auth = {"Origin": B.base, "Authorization": "Bearer " + B.tok}
        def call(path, body=None):   # the answer whatever its status (a refusal is 409)
            code, b = raw(path, json.dumps(body or {}).encode(), dict(auth, **{"Content-Type": "application/json"})); return json.loads(b)

        # ---- places and This PC
        r = call("/api/files/places"); ok("places: the configured places, in order", r.get("ok") and [p["name"] for p in r["places"]] == [p["name"] for p in places], r)
        r = call("/api/files/list", {"path": ""}); ok("This PC lists the drives only", r.get("ok") and [e["name"] for e in r["entries"]] == [places[-1]["name"]] and r["entries"][0]["drive"], r)
        # ---- list
        r = call("/api/files/list", {"path": docs}); names = {e["name"]: e for e in r.get("entries", [])}
        ok("list: folders and files with kind, size and date", r.get("ok") and set(names) == {"Saves", "Game one.nes", "Game two.nes", "notes.txt"} and names["Saves"]["folder"] and names["Game one.nes"]["size"] == 16400
           and names["Game one.nes"]["kind"] == "NES game file" and names["notes.txt"]["kind"] == "Text file" and all(e["modified"] for e in r["entries"]), list(names))
        ok("list: hidden items are left out", "hidden.txt" not in names)
        ok("list: the folder's steps start at This PC and end at the folder", r["crumbs"][0] == {"name": "This PC", "path": ""} and r["crumbs"][-1]["path"] == docs and r["crumbs"][1]["name"] == places[-1]["name"], r.get("crumbs"))
        r = call("/api/files/list", {"path": os.path.join(sample, "No such folder")}); ok("list: a missing folder is said plainly", not r.get("ok") and r["error"].startswith("There is no folder at that place"), r)
        r = call("/api/files/list", {"path": os.path.join(docs, "notes.txt")}); ok("list: a file is not a folder", not r.get("ok") and "That is a file" in r["error"], r)
        r = call("/api/files/list", {"path": "Documents"}); ok("list: a path that is not a full path is refused", not r.get("ok") and "not a full folder path" in r["error"], r)
        deep = sample; i = 0
        while len(deep) < 300: i += 1; deep = os.path.join(deep, f"Ein sehr langer Ordnername für Prüfungen {i:02d}")
        os.makedirs("\\\\?\\" + deep); open("\\\\?\\" + os.path.join(deep, "Lange Sicherung — ü.zip"), "wb").write(b"PK\x05\x06" + b"\x00" * 18)
        r = call("/api/files/list", {"path": deep}); ok(f"list: a folder path of {len(deep)} characters", r.get("ok") and [e["name"] for e in r["entries"]] == ["Lange Sicherung — ü.zip"], r)
        # ---- New Folder
        r = call("/api/files/mkdir", {"path": docs, "name": "Copias — 新しいフォルダー"}); made = os.path.join(docs, "Copias — 新しいフォルダー")
        ok("mkdir: a Unicode folder is made", r.get("ok") and r["path"] == made and os.path.isdir(made), r)
        before = sorted(os.listdir(docs))
        r1 = call("/api/files/mkdir", {"path": docs, "name": "Saves"}); r2 = call("/api/files/mkdir", {"path": docs, "name": "a?b"}); r3 = call("/api/files/mkdir", {"path": docs, "name": "CON"}); r4 = call("/api/files/mkdir", {"path": docs, "name": "ends."})
        ok("mkdir: a name already there, a name Windows cannot use, a reserved name and a trailing period are refused; nothing made",
           not r1.get("ok") and "already here" in r1["error"] and not r2.get("ok") and "cannot contain" in r2["error"] and not r3.get("ok") and "keeps for itself" in r3["error"] and not r4.get("ok") and sorted(os.listdir(docs)) == before, (r1, r2, r3, r4))
        # ---- stat
        r = call("/api/files/stat", {"paths": [os.path.join(docs, "Game one.nes"), docs, os.path.join(docs, "missing.nes")]}); it = r.get("items", [])
        ok("stat: a file, a folder and a missing path", len(it) == 3 and it[0]["file"] and it[0]["size"] == 16400 and it[1]["folder"] and not it[2]["exists"], it)
        # ---- reading a chosen file
        g = call("/api/files/grant", {"mode": "read", "paths": [os.path.join(docs, "Game two.nes")]}); tok = g["files"][0]["token"] if g.get("ok") else ""
        code, body = raw("/api/files/read?token=" + tok, method="GET", headers=auth)
        ok("read: the chosen file's bytes, exactly", code == 200 and body == nes_rom(2), (code, len(body)))
        code2, _ = raw("/api/files/read?token=nottoken", method="GET", headers=auth); ok("read: an unknown token is refused", code2 == 404)
        g2 = call("/api/files/grant", {"mode": "read", "paths": [os.path.join(docs, "missing.nes")]}); ok("grant: a missing file cannot be chosen to read", not g2.get("ok") and "not there" in g2["error"], g2)
        gl = call("/api/files/grant", {"mode": "read", "paths": [os.path.join(deep, "Lange Sicherung — ü.zip")]})
        code3, body3 = raw("/api/files/read?token=" + gl["files"][0]["token"], method="GET", headers=auth) if gl.get("ok") else (0, b"")
        ok("read: a file in a folder path over 260 characters", code3 == 200 and body3.startswith(b"PK\x05\x06"), (gl, code3))
        # ---- saving to a chosen file
        out = os.path.join(made, "Game saves 2026-11-24.zip"); data = os.urandom(3_000_000)
        g = call("/api/files/grant", {"mode": "write", "paths": [out]}); wt = g["files"][0]["token"] if g.get("ok") else ""
        code, body = raw("/api/files/write?token=" + wt, data=data, headers=dict(auth, **{"Content-Type": "application/octet-stream"}))
        ok("write: the page's bytes saved whole to the chosen file, nothing left beside it", code == 200 and json.loads(body)["ok"] and open(out, "rb").read() == data and os.listdir(made) == ["Game saves 2026-11-24.zip"], (code, body[:200], os.listdir(made)))
        code, body = raw("/api/files/write?token=" + wt, data=b"again", headers=dict(auth, **{"Content-Type": "application/octet-stream"}))
        ok("write: one write per choice (the same token again is refused, the file unchanged)", code == 409 and open(out, "rb").read() == data, (code, body[:200]))
        g = call("/api/files/grant", {"mode": "write", "paths": [out]}); new = b"replacement"
        code, body = raw("/api/files/write?token=" + g["files"][0]["token"], data=new, headers=dict(auth, **{"Content-Type": "application/octet-stream"}))
        ok("write: an existing file the person chose to replace is replaced", code == 200 and open(out, "rb").read() == new, body[:200])
        g = call("/api/files/grant", {"mode": "write", "paths": [os.path.join(docs, "Saves")]}); ok("grant: a folder cannot be chosen as a file to save", not g.get("ok") and "folder named" in g["error"], g)
        g = call("/api/files/grant", {"mode": "write", "paths": [os.path.join(docs, "No folder", "x.zip")]}); ok("grant: a save into a folder that is not there is refused", not g.get("ok") and "not there" in g["error"], g)
        g = call("/api/files/grant", {"mode": "write", "paths": [os.path.join(docs, "bad|name.zip")]}); ok("grant: a file name Windows cannot use is refused", not g.get("ok"), g)
        # ---- only the app's own window
        code, body = raw("/api/files/list", data=json.dumps({"path": docs}).encode(), headers={"Origin": B.base, "Content-Type": "application/json"})
        ok("principal: without the app's token the routes refuse", code in (401, 403) and b"entries" not in body, (code, body[:160]))
        code, body = raw("/api/files/list", data=json.dumps({"path": docs}).encode(), headers={"Origin": "https://example.com", "Authorization": "Bearer " + B.tok, "Content-Type": "application/json"})
        ok("principal: the app's token from another origin is refused", code in (401, 403) and b"entries" not in body, (code, body[:160]))
        code, body = raw("/api/files/read?token=" + tok, method="GET", headers={"Origin": B.base}); ok("principal: reading needs the app's token", code in (401, 403), code)
        # ---- the operations take the chosen path(s)
        r = call("/api/personal/pick", {}); ok("Add Your Own Game without a choice: canceled, nothing asked", r == {"ok": False, "canceled": True}, r)
        r = call("/api/personal/pick", {"path": os.path.join(docs, "Game one.nes")}); ok("Add Your Own Game takes the chosen file", r.get("ok") and (r.get("entry") or r.get("known")), r)
        r = call("/api/import/pick", {"mode": "files", "paths": [os.path.join(docs, "Game two.nes")]}); ok("Add Games from Files takes the chosen files", r.get("ok") and r.get("job") and r.get("items") == 1, r)
        r = call("/api/import/pick", {"mode": "folder", "paths": [docs]}); ok("Add Games from a Folder takes the chosen folder", r.get("ok") and r.get("job") and r.get("items") == 1, r)
        r = call("/api/import/pick", {"mode": "files", "paths": ["relative.nes"]}); ok("Add Games from Files: a path that is not full is not used", r == {"ok": False, "canceled": True}, r)
    finally:
        if B: B.stop()
    failed = [n for n, c in res if not c]
    print(f"{len(res) - len(failed)}/{len(res)} passed")
    if a.out: json.dump({"passed": len(res) - len(failed), "total": len(res), "failed": failed}, open(a.out, "w"), indent=1)
    shutil.rmtree("\\\\?\\" + work, ignore_errors=True)
    return 1 if failed else 0

if __name__ == "__main__": sys.exit(main())
