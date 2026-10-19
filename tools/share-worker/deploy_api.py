r"""Deploy the share Worker through the Cloudflare REST API (no Wrangler). Reads CLOUDFLARE_API_TOKEN and CLOUDFLARE_ACCOUNT_ID from the
environment (or, on Windows, the user's environment in the registry); never prints them.

  python deploy_api.py check     token, account, zone and existing records (read-only)
  python deploy_api.py deploy    create the R2 bucket if missing, upload the Worker with its bindings, attach the custom domain"""
from __future__ import annotations
import json, os, sys, uuid, urllib.request, urllib.error
HERE = os.path.dirname(os.path.abspath(__file__))
API = "https://api.cloudflare.com/client/v4"
SCRIPT = "classic-cart-studio-share"; BUCKET = "classic-cart-studio-shares"; HOST = "share.classiccartstudio.com"; ZONE = "classiccartstudio.com"
SITE_ORIGIN = "https://classiccartstudio.com"; COMPAT = "2025-09-01"

def env(name):
    v = os.environ.get(name)
    if not v and os.name == "nt":
        import winreg
        try:
            with winreg.OpenKey(winreg.HKEY_CURRENT_USER, "Environment") as k: v = winreg.QueryValueEx(k, name)[0]
        except OSError: v = None
    if not v: raise SystemExit(f"{name} is not set")
    return v.strip()
TOKEN, ACCOUNT = env("CLOUDFLARE_API_TOKEN"), env("CLOUDFLARE_ACCOUNT_ID")

def call(method, path, body=None, ctype="application/json", raw=False):
    data = body if isinstance(body, (bytes, type(None))) else json.dumps(body).encode()
    r = urllib.request.Request(API + path, data=data, method=method, headers={"Authorization": f"Bearer {TOKEN}", "Content-Type": ctype})
    try:
        with urllib.request.urlopen(r, timeout=60) as x: return json.load(x)
    except urllib.error.HTTPError as e:
        try: return json.load(e)
        except Exception: return {"success": False, "errors": [{"message": f"HTTP {e.code}"}]}

def errs(j): return "; ".join(f"{e.get('code', '')} {e.get('message', '')}" for e in (j.get("errors") or []))

def zone_id():
    j = call("GET", f"/zones?name={ZONE}"); z = (j.get("result") or [])
    return z[0]["id"] if z else None

def check():
    v = call("GET", f"/accounts/{ACCOUNT}/tokens/verify"); v2 = v if v.get("success") else call("GET", "/user/tokens/verify")
    print("token:", "valid" if v2.get("success") else "INVALID " + errs(v2))
    zid = zone_id(); print("zone:", "found" if zid else "NOT FOUND (token needs Zone Read on classiccartstudio.com)")
    if zid:
        recs = call("GET", f"/zones/{zid}/dns_records?name={HOST}"); print("existing DNS records for", HOST + ":", [(r["type"], r.get("content")) for r in recs.get("result") or []])
        apex = call("GET", f"/zones/{zid}/dns_records?name={ZONE}"); print("apex records (unchanged by this deployment):", len(apex.get("result") or []))
    b = call("GET", f"/accounts/{ACCOUNT}/r2/buckets"); print("R2:", "reachable" if b.get("success") else "ERROR " + errs(b), [x["name"] for x in ((b.get("result") or {}).get("buckets") or [])])
    s = call("GET", f"/accounts/{ACCOUNT}/workers/scripts"); print("Workers:", "reachable" if s.get("success") else "ERROR " + errs(s), [x["id"] for x in s.get("result") or []])
    d = call("GET", f"/accounts/{ACCOUNT}/workers/domains"); print("Worker domains:", "reachable" if d.get("success") else "ERROR " + errs(d), [x["hostname"] for x in d.get("result") or []])

def deploy():
    b = call("GET", f"/accounts/{ACCOUNT}/r2/buckets"); names = [x["name"] for x in ((b.get("result") or {}).get("buckets") or [])]
    if BUCKET not in names:
        j = call("POST", f"/accounts/{ACCOUNT}/r2/buckets", {"name": BUCKET}); print("R2 bucket created" if j.get("success") else "R2 bucket ERROR " + errs(j))
        if not j.get("success"): raise SystemExit(1)
    else: print("R2 bucket exists")
    meta = {"main_module": "worker.js", "compatibility_date": COMPAT, "bindings": [
        {"type": "r2_bucket", "name": "SHARE_BUCKET", "bucket_name": BUCKET},
        {"type": "plain_text", "name": "SITE_ORIGIN", "text": SITE_ORIGIN},
        {"type": "plain_text", "name": "SHARE_ORIGIN", "text": f"https://{HOST}"}]}
    boundary = "----ccs" + uuid.uuid4().hex; parts = []
    def part(name, filename, ctype, data):
        parts.append(f"--{boundary}\r\nContent-Disposition: form-data; name=\"{name}\"" + (f"; filename=\"{filename}\"" if filename else "") + f"\r\nContent-Type: {ctype}\r\n\r\n".encode().decode())
        parts.append(data)
    part("metadata", None, "application/json", json.dumps(meta).encode())
    src = os.path.join(HERE, "src")
    for n in sorted(os.listdir(src)):
        if n.endswith(".js"): part(n, n, "application/javascript+module", open(os.path.join(src, n), "rb").read())
    body = b"".join(p.encode() if isinstance(p, str) else p + b"\r\n" for p in parts) + f"--{boundary}--\r\n".encode()
    j = call("PUT", f"/accounts/{ACCOUNT}/workers/scripts/{SCRIPT}", body, f"multipart/form-data; boundary={boundary}")
    print("Worker uploaded" if j.get("success") else "Worker ERROR " + errs(j))
    if not j.get("success"): raise SystemExit(1)
    zid = zone_id()
    if not zid: raise SystemExit("zone not found")
    j = call("PUT", f"/accounts/{ACCOUNT}/workers/domains", {"hostname": HOST, "zone_id": zid, "service": SCRIPT, "environment": "production"})
    print("custom domain attached" if j.get("success") else "custom domain ERROR " + errs(j))
    if not j.get("success"): raise SystemExit(1)

if __name__ == "__main__":
    {"check": check, "deploy": deploy}[sys.argv[1] if len(sys.argv) > 1 else "check"]()
