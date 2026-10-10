"""Smoke test for the frozen companion executable: it starts (isolated data folder, test port), answers /api/version with the app id,
serves the bundled site, refuses an untrusted origin, hands over to an existing instance, and stops cleanly. Exit 0 = PASS.
  python test_frozen_smoke.py <path to NESClassicCollectionStudioCompanion.exe> [--port 8794]"""
from __future__ import annotations
import os, sys, json, time, subprocess, tempfile, shutil, urllib.request, urllib.error, argparse

def get(url, headers=None):
    r = urllib.request.Request(url, headers=headers or {})
    try:
        with urllib.request.urlopen(r, timeout=5) as x: return x.status, dict(x.headers), x.read()
    except urllib.error.HTTPError as e: return e.code, dict(e.headers), e.read()

def main():
    ap = argparse.ArgumentParser(); ap.add_argument("exe"); ap.add_argument("--port", type=int, default=8794); a = ap.parse_args(); a.exe = os.path.abspath(a.exe)
    data = os.path.join(tempfile.gettempdir(), "nes-frozen-smoke"); shutil.rmtree(data, ignore_errors=True); os.makedirs(data)
    json.dump({"port": a.port}, open(os.path.join(data, "companion.json"), "w"))
    env = dict(os.environ, NES_STUDIO_DATA=data); checks = []; ok = lambda k, v, n="": checks.append(f"{k}={'true' if v else 'false'}{(' ('+n+')') if n and not v else ''}")
    p = subprocess.Popen([a.exe], env=env, cwd=os.path.dirname(a.exe)); B = f"http://127.0.0.1:{a.port}"
    try:
        up = False
        for _ in range(80):
            try: urllib.request.urlopen(B + "/api/ping", timeout=1); up = True; break
            except Exception: time.sleep(0.25)
        ok("starts_and_listens", up)
        s, h, b = get(B + "/api/version"); j = json.loads(b or b"{}"); ok("version_endpoint", s == 200 and j.get("app") == "nes-classic-collection-studio-companion" and j.get("frozen") is True and j.get("protocol") >= 1, b[:200].decode(errors="replace"))
        s, h, b = get(B + "/nes-catalog.html"); ok("serves_bundled_site", s == 200 and b"NES Classic Collection Studio" in b and b"brand/public/" in b and b"brand/oem/" not in b, str(s))
        s, h, b = get(B + "/brand/public/byte.png"); ok("serves_public_sprites", s == 200 and b[:4] == b"\x89PNG")
        s, h, b = get(B + "/api/status", headers={"Origin": "https://evil.example"}); ok("untrusted_origin_refused", s == 401 and "Access-Control-Allow-Origin" not in h)
        s, h, b = get(B + "/api/status", headers={"Host": "evil.example"}); ok("rebinding_host_refused", s == 403)
        ok("data_folder_created", os.path.isdir(os.path.join(data, "logs")) and os.path.exists(os.path.join(data, "logs", "companion.log")))
        # second instance hands over and exits
        p2 = subprocess.Popen([a.exe], env=dict(env, BROWSER="echo"), cwd=os.path.dirname(a.exe))
        try: rc = p2.wait(timeout=30); ok("second_instance_exits", rc == 0, str(rc))
        except subprocess.TimeoutExpired: p2.kill(); ok("second_instance_exits", False, "still running")
        log = open(os.path.join(data, "logs", "companion.log"), encoding="utf-8", errors="replace").read(); ok("log_mentions_handover", "handing over" in log and "listening on" in log)
    finally:
        p.terminate()
        try: p.wait(timeout=10)
        except Exception: p.kill()
    bad = [c for c in checks if "=false" in c]; print("\n".join(checks)); print(f"RESULT: {'PASS' if not bad else 'FAIL '+str(bad)} ({len(checks)} checks)"); sys.exit(1 if bad else 0)

if __name__ == "__main__":
    main()
