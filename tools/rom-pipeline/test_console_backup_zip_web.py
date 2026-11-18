r"""A whole-console backup is never read as game files (the built website, headless Edge, one fresh profile). The files are synthetic: a
small ZIP shaped like a Console Backup & Restore backup (its manifest member ccs-console-backup.json, a README and a storage image) that also
carries a sentinel NES ROM, and an ordinary ZIP with the same sentinel ROM as the control. The browser's file chooser is the only thing
replaced (cvPick).
  add      Add ROM Files: the console backup adds nothing and says what it is and where it is put back; the ordinary ZIP adds its game
  sources  the ROM-file picker of .ccs packages and Full Studio Backup (ccsAddRomSources): the console backup is counted as one, never read
           for ROM files; the ordinary ZIP gives its ROM
  python test_console_backup_zip_web.py <built site folder> [--out results.json]      Exit 0 = PASS."""
from __future__ import annotations
import argparse, base64, hashlib, io, json, os, sys, tempfile, time, zipfile
HERE = os.path.dirname(os.path.abspath(__file__)); sys.path.insert(0, HERE)
from studio_harness import EdgeSession, READY, CALM, serve

def rom(tag: str) -> bytes:
    hdr = bytes([0x4E, 0x45, 0x53, 0x1A, 1, 1, 0, 0]) + bytes(8); body = bytearray(); seed = hashlib.sha256(("CCS-SENTINEL-ROM-BYTES " + tag).encode()).digest()
    while len(body) < 24 * 1024: seed = hashlib.sha256(seed).digest(); body += seed
    return hdr + bytes(body[:24 * 1024])

def zipped(members: dict) -> bytes:
    o = io.BytesIO()
    with zipfile.ZipFile(o, "w", zipfile.ZIP_DEFLATED) as z:
        for n, b in members.items(): z.writestr(n, b)
    return o.getvalue()

def main() -> int:
    ap = argparse.ArgumentParser(); ap.add_argument("site"); ap.add_argument("--out", default=""); a = ap.parse_args()
    srv = serve(a.site); url = f"http://127.0.0.1:{srv.server_address[1]}/index.html"; res = []
    def ok(n, c, d=""):
        res.append((n, bool(c))); print(("PASS " if c else "FAIL ") + n + (f"  ({str(d)[:300]})" if d and not c else ""), flush=True)
    g = rom("console-backup-zip-guard"); gid = "p_" + hashlib.sha256(g[16:]).hexdigest()[:16]
    backup = zipped({"ccs-console-backup.json": json.dumps({"schema": "ccs-console-backup/1"}).encode(), "README.txt": b"A console backup.",
                     "restore/nanda.img": bytes(4096), "games/sentinel.nes": g})
    plain = zipped({"games/sentinel.nes": g})
    def files(ed, var, *items):
        parts = ",".join(f"new File([Uint8Array.from(atob('{base64.b64encode(b).decode()}'), c=>c.charCodeAt(0))], {json.dumps(n)})" for n, b in items)
        ed.js(f"window.{var}=[{parts}]; 1")
    try:
        with EdgeSession(profile=tempfile.mkdtemp(prefix="ccs-cbzip-"), width=1440, height=900) as ed:
            ed.navigate(url); ed.wait_for(READY, timeout=120); ed.js(CALM)
            ed.wait_for("CS.starter&&CS.starter.state==='done'", timeout=90); time.sleep(1.0)
            base = ed.js("[...sel]")
            # ---- add ----
            files(ed, "__f", ("Console-Backup-2026-11-14.zip", backup)); ed.js("cvPick=async()=>window.__f; window.__a=0; cvAddFiles().then(()=>window.__a=1); 1")
            ed.wait_for("window.__a===1", timeout=60); time.sleep(0.4)
            d = ed.js("$('collm').classList.contains('open')?$('collBody').innerText:''") or ""
            ok("add: the console backup adds no game, even with an NES ROM inside it", ed.js("[...sel]") == base and gid not in ed.js("[...sel]"), ed.js("[...sel]"))
            ok("add: it says the file is a backup of a whole console and where it is put back", "backup of a whole NES Classic console" in d and "CCS Console Workbench" in d and "Classic Cart Studio for Windows" in d, d)
            ed.js("closeColl(); 1"); time.sleep(0.2)
            files(ed, "__f", ("roms.zip", plain)); ed.js("cvPick=async()=>window.__f; window.__a=0; cvAddFiles().then(()=>window.__a=1); 1")
            ed.wait_for("window.__a===1", timeout=60); time.sleep(0.4)
            ok("add (control): an ordinary ZIP with the same ROM adds the game", gid in ed.js("[...sel]"), ed.js("[...sel]")); ed.js("closeColl(); 1")
            # ---- sources ----
            files(ed, "__s", ("Console-Backup-2026-11-14.zip", backup))
            r = json.loads(ed.js("ccsAddRomSources(window.__s).then(r=>JSON.stringify(r))"))
            ok("sources: the console backup is recognized and not read for ROM files", r.get("found") == 0 and r.get("consoleBackups") == 1, r)
            files(ed, "__s", ("roms.zip", plain))
            r = json.loads(ed.js("ccsAddRomSources(window.__s).then(r=>JSON.stringify(r))"))
            ok("sources (control): the ordinary ZIP gives its ROM", r.get("found") == 1 and not r.get("consoleBackups"), r)
            ok("no page errors", not ed.exceptions, ed.exceptions[:2])
    finally:
        srv.shutdown()
    fails = [n for n, c in res if not c]
    print(f"\n{len(res) - len(fails)}/{len(res)} PASS" + (f"; FAIL: {', '.join(fails)}" if fails else ""))
    if a.out: json.dump({"results": [{"name": n, "pass": c} for n, c in res]}, open(a.out, "w", encoding="utf-8"), indent=1)
    return 0 if not fails else 1

if __name__ == "__main__": sys.exit(main())
