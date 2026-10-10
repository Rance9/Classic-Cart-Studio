"""PC-side bridge between a validated schema-3 collection manifest and Hakchi2 CE 3.9.3's portable folder.

Everything here is PC-only. It never talks to the console. It changes exactly two things inside the hakchi portable
tree, transactionally and with a rollback snapshot: (1) config/config.json -> gamesCollectionSettings.NES.OriginalGames
(the checked factory games) and .SelectedGames (the checked custom games); (2) folders games/<code>/ for additional games
managed by this integration (each carries a nes-classic-catalog.json ownership marker). Nothing else in the tree is
touched; unrelated custom games keep their folders (they are only unchecked, because the manifest is exclusive).

Reconciliation, not import: every apply() computes the desired state from the manifest and the authoritative project
data (reports/catalog.json, reports/builtin.json, the payload store), diffs it against the current PC-side state, and
makes only the changes needed. Running the same manifest twice is a no-op apart from a new audit record.
"""
from __future__ import annotations
import os, sys, json, hashlib, time, shutil, re, sqlite3, subprocess, zlib
HERE = os.path.dirname(os.path.abspath(__file__))
PIPE = os.path.normpath(os.path.join(HERE, "..", "rom-pipeline"))
sys.path.insert(0, PIPE)
from common import PROJECT, REPORTS, DB_PATH, fmt_size, SITE_ROOT, DATA_ROOT, DEV_MODE, FROZEN, site_path, data_path  # noqa: E402
from import_selected import builtin_class, factory_reference, SCHEMAS, V3_POLICIES  # noqa: E402
from manifest_v4 import SCHEMA4, normalize as normalize_manifest, validate_v4  # noqa: E402
from library_lookup import resolve as resolve_version  # noqa: E402
try:
    from PIL import Image
except ImportError:  # art is optional
    Image = None

HAKCHI_SHA256 = "8e1fe79585ae2aceacab21157d10a74e0b3e64456e1efdd750c1eb61e3391a2b"   # hakchi.exe 3.9.3.0 as installed (docs/research.md)
HAKCHI_VERSION = "3.9.3"   # pinned build (docs/decisions.md D-01); the companion never installs or launches any other build
HAKCHI_PACKAGE = {"name": "hakchi2-ce-3.9.3-portable.zip", "size": 75518132, "sha256": "46520c8b9a3c768b5ee7c2cb4878adc11e3da1869aae281000d75012b7e9f122", "entries": 914,
                  "url": "https://github.com/TeamShinkansen/Hakchi2-CE/releases/download/v3.9.3/hakchi2-ce-3.9.3-portable.zip"}   # docs/research.md §2.3; URL re-downloaded and hash-matched 2026-10-09
DRIVER_EXE_SHA256 = "88a2c6bc3e296c81ca3544557a2e254e894c867ceb0d60e14c2fc2e5d610462d"   # driver\classic_driver.exe inside the package (execution log 1.3, 1.7)
DRIVER_HWID = "USB\\VID_1F3A&PID_EFE8"   # NES Classic in FEL mode; the libwdi-generated INF carries it as signer name
DRIVER_INF_NAME = "nintendo_classic.inf"
CFG_PATH = data_path("companion.json") if FROZEN else os.path.join(HERE, "companion.json")
LEDGER_NAME = "ledger.json"
MARKER = "nes-classic-catalog.json"
COLLECTION = "NES"   # gamesCollectionSettings key for the 2018 NA NES Classic (verified in config.json)
KACHI_EXEC = "/bin/clover-kachikachi-wr {rom} --guest-overscan-dimensions 0,0,9,3 --initial-fadein-durations 3,2 --volume 75 --enable-armet"   # NesGame.Patch (NesGame.cs:51-52)
SYSTEM_NAME = "Nintendo - Nintendo Entertainment System"   # CoreCollection.Systems entry for NES (metadata.json "System")

def load_cfg() -> dict:
    d = {"hakchi_exe": (data_path("hakchi", "hakchi2-ce-3.9.3", "hakchi.exe") if FROZEN else os.path.join(PROJECT, "hakchi", "hakchi2-ce-3.9.3", "hakchi.exe")), "hakchi_args": ["/portable"], "exports_dir": data_path("exports"), "staging_dir": data_path("hakchi-staging"), "port": 8765}
    path = os.environ.get("NES_COMPANION_CONFIG") or CFG_PATH   # tests point this at a config for the disposable rehearsal tree
    if os.path.exists(path): d.update(json.load(open(path, encoding="utf-8")))
    return d

def sha256_file(p: str) -> str:
    h = hashlib.sha256()
    with open(p, "rb") as f:
        for chunk in iter(lambda: f.read(1 << 20), b""): h.update(chunk)
    return h.hexdigest()

# ------------------------------------------------------------------ environment
def hakchi_exe_ok(path: str) -> tuple[bool, str]:
    if not path or os.path.basename(path).lower() != "hakchi.exe": return False, "path must point to a file named hakchi.exe"
    if not os.path.isfile(path): return False, "file not found"
    h = sha256_file(path)
    if h != HAKCHI_SHA256: return False, f"SHA-256 {h[:16]}… does not match the recorded Hakchi2 CE 3.9.3 build"
    return True, "ok"

def pwsh() -> str:
    """PowerShell 7 is required for the UI Automation read-back (Windows PowerShell 5.1 cannot see hakchi's DataGridView rows); 5.1 is enough for the process/device queries."""
    for c in (r"C:\Program Files\PowerShell\pwsh.exe", "pwsh.exe"):
        if os.path.exists(c) or c == "pwsh.exe": return c
    return "powershell.exe"

def hakchi_running() -> list[dict]:
    try:
        out = subprocess.run(["powershell.exe", "-NoProfile", "-Command", "Get-Process hakchi -ErrorAction SilentlyContinue | Select-Object Id, Path | ConvertTo-Json"], capture_output=True, text=True, timeout=30).stdout.strip()
        if not out: return []
        j = json.loads(out); return j if isinstance(j, list) else [j]
    except Exception: return []

def console_connected() -> list[str]:
    """NES Classic over USB: FEL (1F3A:EFE8) or the RNDIS/clovershell gadget (04E8:6863, also 'Samsung' composite)."""
    try:
        out = subprocess.run(["powershell.exe", "-NoProfile", "-Command", "Get-PnpDevice -PresentOnly -ErrorAction SilentlyContinue | Where-Object { $_.InstanceId -match 'VID_1F3A|VID_04E8&PID_6863' } | Select-Object -ExpandProperty InstanceId"], capture_output=True, text=True, timeout=30).stdout.strip()
        return [l.strip() for l in out.splitlines() if l.strip()]
    except Exception: return []

def env_status(cfg: dict) -> dict:
    ok, why = hakchi_exe_ok(cfg["hakchi_exe"]); root = os.path.dirname(cfg["hakchi_exe"])
    return {"hakchi_path": cfg["hakchi_exe"], "hakchi_found": ok, "hakchi_check": why, "hakchi_root": root, "config_exists": os.path.exists(os.path.join(root, "config", "config.json")),
            "hakchi_running": bool(hakchi_running()), "console_connected": bool(console_connected()), "exports_dir": cfg["exports_dir"], "staging_dir": cfg["staging_dir"],
            "install": hakchi_install_status(cfg), "driver": driver_status(cfg)}

def file_version(path: str) -> str:
    try: return subprocess.run(["powershell.exe", "-NoProfile", "-Command", f"(Get-Item -LiteralPath '{path}').VersionInfo.FileVersion"], capture_output=True, text=True, timeout=30).stdout.strip()
    except Exception: return ""

def packages_dir(cfg: dict) -> str: return cfg.get("packages_dir") or (data_path("packages") if FROZEN else os.path.join(PROJECT, "packages"))

_pkg_cache: dict = {}
def package_verified(path: str) -> bool:
    """Size + SHA-256 check of a release package, cached per (size, mtime) so status calls stay cheap."""
    try: key = (path, os.path.getsize(path), int(os.path.getmtime(path)))
    except OSError: return False
    if key not in _pkg_cache: _pkg_cache[key] = key[1] == HAKCHI_PACKAGE["size"] and sha256_file(path) == HAKCHI_PACKAGE["sha256"]
    return _pkg_cache[key]

def hakchi_install_status(cfg: dict) -> dict:
    """ok = the pinned 3.9.3 build is at the configured path (hash match); missing = nothing there; different-version = some other
    hakchi.exe (never replaced automatically: the pin is an owner decision, D-01)."""
    exe = cfg["hakchi_exe"]; root = os.path.dirname(exe); pk = os.path.join(packages_dir(cfg), HAKCHI_PACKAGE["name"])
    st = {"path": exe, "root": root, "pinned_version": HAKCHI_VERSION, "expected_sha256": HAKCHI_SHA256, "package_name": HAKCHI_PACKAGE["name"], "package_size": HAKCHI_PACKAGE["size"], "package_sha256": HAKCHI_PACKAGE["sha256"],
          "package_url": cfg.get("hakchi_package_url") or HAKCHI_PACKAGE["url"], "package_local": os.path.isfile(pk), "package_local_verified": package_verified(pk) if os.path.isfile(pk) else None, "version": "", "sha256": "", "root_nonempty": False}
    if os.path.isfile(exe):
        st["sha256"] = sha256_file(exe); st["version"] = file_version(exe); st["state"] = "ok" if st["sha256"] == HAKCHI_SHA256 else "different-version"
    else:
        st["state"] = "missing"; st["root_nonempty"] = os.path.isdir(root) and bool(os.listdir(root))
    st["config_exists"] = os.path.exists(os.path.join(root, "config", "config.json"))
    return st

def parse_pnputil(text: str) -> list[dict]:
    """Blocks of 'Key: value' lines from `pnputil /enum-drivers` (blank-line separated; indented continuation lines are appended)."""
    blocks, cur = [], {}
    for line in text.splitlines():
        if not line.strip():
            if cur: blocks.append(cur); cur = {}
            continue
        m = re.match(r"^([A-Za-z][A-Za-z ]+?):\s*(.*)$", line)
        if m: cur[m.group(1).strip().lower().replace(" ", "_")] = m.group(2).strip()
        elif cur and line[:1].isspace(): k = list(cur)[-1]; cur[k] = (cur[k] + " " + line.strip()).strip()
    if cur: blocks.append(cur)
    return [b for b in blocks if "published_name" in b]

def driver_status(cfg: dict | None = None, pnputil_text: str | None = None) -> dict:
    """Is the console's FEL driver (libwdi-generated WinUSB INF for USB\\VID_1F3A&PID_EFE8) in the Windows driver store? Read-only
    (`pnputil /enum-drivers`, no elevation). Also reports whether Hakchi's bundled installer is present and matches the recorded hash."""
    cfg = cfg or load_cfg(); root = os.path.dirname(cfg["hakchi_exe"]); inst = os.path.join(root, "driver", "classic_driver.exe")
    st = {"hwid": DRIVER_HWID, "installed": False, "inf": None, "original_name": None, "provider": None, "version": None, "installer": inst, "installer_present": os.path.isfile(inst), "installer_verified": None, "simulated": False}
    if st["installer_present"]: st["installer_verified"] = sha256_file(inst) == DRIVER_EXE_SHA256
    if pnputil_text is None:
        try: pnputil_text = subprocess.run(["pnputil.exe", "/enum-drivers"], capture_output=True, text=True, timeout=90, errors="replace").stdout
        except Exception as e: st["error"] = f"pnputil unavailable: {e}"; pnputil_text = ""
    for b in parse_pnputil(pnputil_text):
        blob = " ".join(b.values()).lower()
        if (b.get("provider_name") or "").lower() == "libwdi" and ("vid_1f3a&pid_efe8" in blob or (b.get("original_name") or "").lower() == DRIVER_INF_NAME):
            st.update(installed=True, inf=b.get("published_name"), original_name=b.get("original_name"), provider=b.get("provider_name"), version=b.get("driver_version")); break
    if cfg.get("rehearsal_tree") and cfg.get("simulate_driver_missing"): st.update(installed=False, inf=None, original_name=None, version=None, simulated=True)   # rehearsal/screenshot hook only
    st["state"] = "installed" if st["installed"] else ("missing" if st["installer_verified"] else "installer-unavailable")
    return st

def obtain_package(cfg: dict) -> dict:
    """The verified 3.9.3 portable package: the copy in packages\\ when its size and SHA-256 match; otherwise the pinned release asset is
    downloaded into packages\\ (.part file, size and SHA-256 verified, then renamed). A copy that fails verification is moved aside, never used."""
    import urllib.request
    pdir = packages_dir(cfg); os.makedirs(pdir, exist_ok=True); pk = os.path.join(pdir, HAKCHI_PACKAGE["name"]); url = cfg.get("hakchi_package_url") or HAKCHI_PACKAGE["url"]
    if os.path.isfile(pk) and package_verified(pk): return {"path": pk, "source": "local", "sha256": HAKCHI_PACKAGE["sha256"]}
    if os.path.isfile(pk): os.replace(pk, pk + ".rejected-" + time.strftime("%Y%m%d-%H%M%S"))
    part = pk + ".part"
    try:
        req = urllib.request.Request(url, headers={"User-Agent": "nes-classic-catalog-companion/1.0"})
        with urllib.request.urlopen(req, timeout=60) as r, open(part, "wb") as f:
            n = 0
            while True:
                chunk = r.read(1 << 20)
                if not chunk: break
                f.write(chunk); n += len(chunk)
                if n > HAKCHI_PACKAGE["size"]: raise RuntimeError("download is larger than the recorded package size")
        if os.path.getsize(part) != HAKCHI_PACKAGE["size"]: raise RuntimeError(f"downloaded size {os.path.getsize(part)} != recorded {HAKCHI_PACKAGE['size']}")
        h = sha256_file(part)
        if h != HAKCHI_PACKAGE["sha256"]: raise RuntimeError(f"downloaded SHA-256 {h[:16]}… != recorded {HAKCHI_PACKAGE['sha256'][:16]}…; the file was discarded")
        os.replace(part, pk); _pkg_cache.clear(); return {"path": pk, "source": "download", "url": url, "sha256": h}
    except Exception:
        if os.path.exists(part): os.remove(part)
        raise

def install_hakchi(cfg: dict, dry_run: bool = False) -> dict:
    """Set up the pinned Hakchi2 CE 3.9.3 portable build at the configured path from the verified package — only when nothing is there.
    An existing or different build is never replaced (D-01). Portable = extraction only: no installer, no registry, no configuration written
    (hakchi creates config\\config.json itself; ConfigIni.Load falls back to defaults when the file is absent)."""
    import zipfile
    st = hakchi_install_status(cfg); res = {"ok": False, "state_before": st["state"], "root": st["root"], "dry_run": dry_run}
    if st["state"] == "ok": res["message"] = f"Hakchi2 CE {HAKCHI_VERSION} is already present and verified at {st['path']}; nothing to do"; return res
    if st["state"] == "different-version": res["message"] = f"a different Hakchi build is present at {st['path']} (file version {st['version'] or 'unknown'}, SHA-256 {st['sha256'][:16]}…); the project pins {HAKCHI_VERSION} and never replaces an existing installation automatically"; return res
    if st["root_nonempty"]: res["message"] = f"{st['root']} exists and is not empty but has no hakchi.exe; move it aside first (nothing is deleted automatically)"; return res
    if dry_run: res.update(ok=True, message=f"dry run: would obtain {HAKCHI_PACKAGE['name']} ({'local, verified' if st['package_local_verified'] else 'download from ' + st['package_url']}) and extract it to {st['root']}"); return res
    pk = obtain_package(cfg); res["package"] = pk
    tmp = st["root"].rstrip("\\/") + ".installing"; shutil.rmtree(tmp, ignore_errors=True); os.makedirs(tmp)
    try:
        with zipfile.ZipFile(pk["path"]) as z:
            names = z.namelist()
            for n in names:
                if n.startswith(("/", "\\")) or ".." in re.split(r"[\\/]", n) or (len(n) > 1 and n[1] == ":"): raise RuntimeError(f"unsafe path in package: {n}")
            z.extractall(tmp)
        if sha256_file(os.path.join(tmp, "hakchi.exe")) != HAKCHI_SHA256: raise RuntimeError("extracted hakchi.exe does not match the recorded hash")
        if sha256_file(os.path.join(tmp, "driver", "classic_driver.exe")) != DRIVER_EXE_SHA256: raise RuntimeError("extracted driver installer does not match the recorded hash")
        if os.path.isdir(st["root"]) and not os.listdir(st["root"]): os.rmdir(st["root"])
        os.makedirs(os.path.dirname(st["root"]), exist_ok=True)
        for attempt in range(6):   # a virus scanner or indexer may hold the freshly extracted folder for a moment
            try: os.replace(tmp, st["root"]); break
            except PermissionError:
                if attempt == 5: raise
                time.sleep(1.0)
    except Exception:
        shutil.rmtree(tmp, ignore_errors=True); raise
    after = hakchi_install_status(cfg)
    res.update(ok=after["state"] == "ok", state_after=after["state"], entries=len(names), version=after["version"],
               message=f"Hakchi2 CE {HAKCHI_VERSION} extracted to {st['root']} ({len(names)} entries; hakchi.exe and the driver installer verified by SHA-256; package from {pk['source']}). No configuration was written: Hakchi creates config\\config.json itself, and the first Open-in-Hakchi run writes only the fields it manages.")
    return res

def install_driver(cfg: dict, dry_run: bool = False) -> dict:
    """Run Hakchi's bundled console driver installer (driver\\classic_driver.exe, libwdi WinUSB for USB\\VID_1F3A&PID_EFE8) elevated: Windows shows a UAC
    prompt the owner must accept. Covered by implementation-plan step 1.7 (Gate G0). Only on an explicit click in the catalog, never when the driver
    is already in the driver store, and only with the hash-verified installer."""
    st = driver_status(cfg); res = {"ok": False, "dry_run": dry_run, "state_before": st["state"], "installer": st["installer"], "hwid": DRIVER_HWID}
    if st["installed"]: res["message"] = f"console driver already installed ({st['inf']}, {st['provider']} {st['version']}); nothing to do"; return res
    if not st["installer_verified"]: res["message"] = "driver\\classic_driver.exe is missing or does not match the recorded hash; set up Hakchi 3.9.3 first"; return res
    ps = f"$p = Start-Process -FilePath '{st['installer']}' -WorkingDirectory '{os.path.dirname(st['installer'])}' -Verb RunAs -Wait -PassThru; exit $p.ExitCode"
    res["command"] = ps
    if dry_run or st["simulated"]: res.update(ok=True, message="dry run: would start the installer elevated (Windows UAC prompt) and wait for it; nothing was run"); return res
    try: p = subprocess.run(["powershell.exe", "-NoProfile", "-Command", ps], capture_output=True, text=True, timeout=600); res["exit_code"] = p.returncode; res["stderr"] = p.stderr.strip()[-300:]
    except subprocess.TimeoutExpired: res["message"] = "the installer did not finish within 10 minutes (UAC prompt not answered?)"; return res
    after = driver_status(cfg); res.update(state_after=after["state"], inf=after.get("inf"), ok=bool(after["installed"]) and res["exit_code"] == 0)
    res["message"] = f"console driver installed: {after.get('inf')} ({after.get('provider')} {after.get('version')})" if after["installed"] else f"installer exit code {res['exit_code']}; the driver is not in the driver store afterwards" + (" (UAC prompt declined?)" if res["exit_code"] != 0 else "")
    return res

# ------------------------------------------------------------------ manifest validation + plan
def code_for(crc32: int) -> str:
    """hakchi custom-game code = GenerateCode(crc32, 'H') (NesApplication.cs:1236-1245, v3.9.3): 'CLV-H-' + five letters
    'A'+(crc%26), 'A'+((crc>>5)%26), 'A'+((crc>>10)%26), 'A'+((crc>>15)%26), 'A'+((crc>>20)%26); crc = headerless PRG+CHR CRC32."""
    return "CLV-H-" + "".join(chr(ord("A") + ((crc32 >> sh) % 26)) for sh in (0, 5, 10, 15, 20))

def plan(man: dict, approve_alternates: bool = False) -> dict:
    """Validate the manifest against project data and compute the desired hakchi state. Fails closed: any problem -> can_apply False."""
    cat = {g["game_id"]: g for g in json.load(open(site_path("catalog.json"), encoding="utf-8"))["games"]}
    bpath = site_path("builtin.json"); factory_ref, factory = factory_reference(bpath)
    problems, warnings, rows = [], [], []
    if man.get("schema") == SCHEMA4: problems += validate_v4(man, factory_ref, factory, lambda vid: cat.get(vid) or resolve_version(vid))
    man = normalize_manifest(man)
    if man.get("schema") not in (SCHEMAS[2], SCHEMA4): problems.append(f"the collection must be schema 3 or 4 (got {man.get('schema')!r}); older files are loaded in the Studio and saved again")
    for k, allowed in V3_POLICIES.items():
        if man.get(k) not in allowed: problems.append(f"{k}={man.get(k)!r} not in {sorted(allowed)}")
    fi = man.get("factory_inventory") or {}
    if fi.get("sha256") != factory_ref["sha256"]: problems.append("factory_inventory.sha256 does not match reports/builtin.json (stale catalog page or edited manifest)")
    if fi.get("count") != factory_ref["count"] or sorted(fi.get("codes", [])) != factory_ref["codes"]: problems.append("factory inventory codes/count differ from the verified inventory")
    ids = [g.get("game_id") for g in man.get("games", [])]
    dup = sorted({i for i in ids if ids.count(i) > 1})
    if dup: problems.append(f"duplicate game ids: {dup}")
    stored, ident = {}, {}
    if os.path.exists(DB_PATH):   # development checkout: the pipeline's payload store
        con = sqlite3.connect(DB_PATH); stored = {r[0]: r[1] for r in con.execute("SELECT sha256, stored FROM payloads")}; ident = {r[0]: r[1] for r in con.execute("SELECT sha256, header_hex FROM ident")}; con.close()
    visible, custom, seen_codes = set(), [], {}
    for g in man.get("games", []):
        c = cat.get(g.get("game_id")) or resolve_version(g.get("game_id") or "")
        if not c: problems.append(f"{g.get('display_title') or g.get('title') or g.get('game_id')!r}: not in the catalog, the Extended Library or the personal library"); continue
        cls, b = builtin_class(c, factory); dec = (g.get("revision_decision") or "").lower()
        if g.get("payload_sha256") and g.get("payload_sha256") != c["payload_sha256"]: problems.append(f"{c['display_title']}: manifest payload hash differs from the catalog (edited or stale manifest)")
        if (cls.startswith("revision") or cls.startswith("identity")) and dec not in ("factory", "alternate"): problems.append(f"{c['display_title']}: revision decision unresolved (got {g.get('revision_decision')!r}; expected 'factory' or 'alternate')")
        want_install = (dec == "alternate") if (cls.startswith("revision") or cls.startswith("identity")) else (not cls.startswith("exact"))
        if "install_required" in g and bool(g.get("install_required")) != want_install: problems.append(f"{c['display_title']}: install_required={g.get('install_required')!r} is inconsistent with its kind ({'factory' if not want_install else 'additional/alternate'})")
        inp = {k[6:]: c.get(k) for k in c if k.startswith("input_")}
        row = {"game_id": c["game_id"], "title": c["display_title"], "nointro_name": c["nointro_name"], "emulator": c.get("recommended_emulator"), "core": c.get("core"), "statuses": [], "detail": "", "kind": "", "code": c.get("builtin_code") or "", "source": c.get("source", "catalog")}
        if c.get("source") == "personal" and c.get("file_missing"): problems.append(f"{c['display_title']}: game file missing — use Locate File in the Studio"); row["statuses"].append("missing ROM payload"); row["detail"] = "the game file for this personal entry is no longer where the Studio stored it"
        if c.get("recommended_emulator") == "unknown" and not c.get("header_hex"): problems.append(f"{c['display_title']}: no header information for this ROM, so it cannot be prepared (identity unknown)"); row["statuses"].append("missing ROM payload"); row["detail"] = "no evidence-based iNES header; the Studio never guesses one" 
        hw = inp.get("standard_controller_playable") != "true" and inp.get("nes_classic_support") in ("unverified", "unsupported", "unknown")
        if hw: row["statuses"].append("controller warning"); row["detail"] = f"{inp.get('special_accessory') or 'input'} — NES Classic support {inp.get('nes_classic_support')}"
        if cls.startswith("exact"):
            row["kind"] = "factory"; row["statuses"].insert(0, "already installed"); row["statuses"].insert(1, "factory game retained")
            if cls == "exact": visible.add(c["builtin_code"])
            else: problems.append(f"{c['display_title']}: catalog says exact factory match but the inventory could not confirm it")
        elif cls.startswith("revision") or cls.startswith("identity"):
            row["kind"] = "factory (revision)" if cls.startswith("revision") else "factory title (other release)"
            if dec == "alternate":
                row["statuses"].insert(0, "revision approval required"); row["detail"] = (row["detail"] + "; " if row["detail"] else "") + f"catalog version chosen over factory {c.get('builtin_factory_version') or ''}; approval needed before it can be prepared"
                if approve_alternates and not cls.endswith("unverified"):
                    row["statuses"] = [s for s in row["statuses"] if s != "revision approval required"] + ["ready to prepare"]; custom.append((c, row))
                    if cls.startswith("revision"): pass   # the factory copy stays hidden: the alternate replaces it
                else: problems.append(f"{c['display_title']}: revision decision 'alternate' needs explicit approval (--approve-alternates); choose 'Keep factory version' or approve")
            else:
                if cls.startswith("revision"): row["statuses"].insert(0, "factory game retained"); visible.add(c["builtin_code"])
                else:
                    visible.add(c["builtin_code"])   # factory title kept visible for the count; the problem below still blocks apply
                    row["statuses"].insert(0, "needs review"); row["detail"] = (row["detail"] + "; " if row["detail"] else "") + f"same title as factory {c.get('builtin_factory_version') or ''}, different release; nothing prepared"
                    problems.append(f"{c['display_title']}: identity-only match needs review — select the factory title instead, or choose the catalog release and approve it")
        else:
            row["kind"] = {"personal": "personal game", "extended": "additional (Extended Library)"}.get(c.get("source"), "additional"); custom.append((c, row))
        if c.get("recommended_emulator") == "RetroArch": row["statuses"].append("emulator prerequisite"); row["detail"] = (row["detail"] + "; " if row["detail"] else "") + f"needs the RetroArch module ({c.get('core')}) on the console — not installed; deployment prerequisite"
        rows.append(row)
    # custom games: payload + header availability, code + hash
    plan_custom = []
    for c, row in custom:
        sha = c["payload_sha256"]; path = c.get("stored") or stored.get(sha); hdr = c.get("header_hex") or ident.get(sha) or ""
        if c.get("file_missing") or "missing ROM payload" in row["statuses"]: continue   # already reported above
        if not path or not os.path.exists(path): row["statuses"].append("missing ROM payload"); row["detail"] = (row["detail"] + "; " if row["detail"] else "") + ("the game file is missing" if c.get("source") == "personal" else "payload not in the work store (C:/NES/work) — re-run inventory.py"); problems.append(f"{c['display_title']}: ROM payload missing"); continue
        if not hdr: row["statuses"].append("missing ROM payload"); problems.append(f"{c['display_title']}: no evidence-based header"); continue
        if "ready to prepare" not in row["statuses"]: row["statuses"].insert(0, "ready to prepare")
        plan_custom.append({"game": c, "row": row, "payload_path": path, "header_hex": hdr, "converted_sha256": c.get("converted_sha256"), "headered_file": bool(c.get("headered_file"))})
    hidden = [cd for cd in factory_ref["codes"] if cd not in visible]
    declared = sorted(h.get("code") for h in man.get("hidden_factory_games", []))
    if man.get("schema") == SCHEMAS[2] and declared != sorted(hidden): problems.append(f"manifest hidden_factory_games ({len(declared)}) disagrees with the derived list ({len(hidden)})")
    if not man.get("games"): problems.append("the collection is EMPTY: every factory game would be hidden and the TV library would be empty")
    for cd in hidden: rows.append({"game_id": "", "title": factory[cd]["factory_title"], "code": cd, "kind": "factory", "statuses": ["factory game hidden"], "emulator": "kachikachi (factory)", "detail": "not selected — original game preserved on the console, hidden from the intended TV collection"})
    S = {"collection": len(man.get("games", [])), "factory_visible": len(visible), "factory_hidden": len(hidden), "to_prepare_total": len(plan_custom), "to_prepare": len(plan_custom), "reused": 0,
         "retroarch": sum(1 for r in rows if "emulator prerequisite" in r["statuses"]), "controller_warnings": sum(1 for r in rows if "controller warning" in r["statuses"]), "revision_pending": sum(1 for r in rows if "revision approval required" in r["statuses"] or "needs review" in r["statuses"]),
         "install_bytes": sum(os.path.getsize(p["payload_path"]) + 16 for p in plan_custom)}
    S["install_size"] = fmt_size(S["install_bytes"])
    if S["retroarch"]: warnings.append(f"{S['retroarch']} selected game(s) are routed to RetroArch, which is not installed on the console; they can be staged in Hakchi but the deployment stays incomplete until the module is approved and installed (post-G1).")
    if S["controller_warnings"]: warnings.append(f"{S['controller_warnings']} selected game(s) need input hardware that has not been verified with this console.")
    return {"can_apply": not problems, "problems": problems, "warnings": warnings, "summary": S, "games": rows, "visible_codes": sorted(visible), "hidden_codes": hidden, "custom": plan_custom, "factory_ref": factory_ref, "factory": factory}

# ------------------------------------------------------------------ hakchi tree access
class HakchiTree:
    def __init__(self, root: str):
        self.root = root; self.cfg_path = os.path.join(root, "config", "config.json"); self.games_dir = os.path.join(root, "games")
    def read_cfg(self) -> dict:
        """config\\config.json. Absent on a freshly extracted tree: ConfigIni.Load (hakchi_gui/ConfigIni.cs) then starts from `new ConfigIni()` and JSON.NET
        leaves absent fields at their C# defaults, so the bridge starts from the same empty collection state and writes only the fields it manages."""
        if not os.path.exists(self.cfg_path): return {"gamesCollectionSettings": {COLLECTION: {"SelectedGames": [], "OriginalGames": []}}}
        return json.load(open(self.cfg_path, encoding="utf-8"))
    def write_cfg_atomic(self, cfg: dict):
        os.makedirs(os.path.dirname(self.cfg_path), exist_ok=True)
        tmp = self.cfg_path + ".nes-staging"
        try: open(tmp, "w", encoding="utf-8").write(json.dumps(cfg, indent=2)); os.replace(tmp, self.cfg_path)
        except Exception:
            if os.path.exists(tmp): os.remove(tmp)   # never leave a half-written staging file next to hakchi's config
            raise
    def custom_entries(self) -> dict:
        """code -> {folder, desktop, marker (ours) or None, rom_sha256 (if a .nes exists)}"""
        out = {}
        if not os.path.isdir(self.games_dir): return out
        for code in os.listdir(self.games_dir):
            d = os.path.join(self.games_dir, code)
            if not os.path.isdir(d): continue
            desk = os.path.join(d, code + ".desktop"); mk = os.path.join(d, MARKER)
            e = {"folder": d, "desktop": desk if os.path.exists(desk) else None, "marker": json.load(open(mk, encoding="utf-8")) if os.path.exists(mk) else None, "rom_sha256": None}
            roms = [f for f in os.listdir(d) if f.lower().endswith(".nes")]
            if roms: e["rom_sha256"] = sha256_file(os.path.join(d, roms[0]))
            out[code] = e
        return out

def desktop_text(code: str, title: str, rom_name: str, exec_cmd: str, players: str, simultaneous: str, year: str, publisher: str, copyright_: str, battery: bool) -> str:
    """Byte layout of hakchi CE 3.9.3 DesktopFile.GetConfig (LF endings, UTF-8). Path=/var/saves/<code>; Icon=/var/games/<code>/<code>.png."""
    rel = f"{year}-01-01" if re.match(r"^\d{4}$", year or "") else "1900-01-01"
    sort_title = re.sub(r"^the\s+", "", (title or "").lower())
    return "\n".join(["[Desktop Entry]", "Type=Application", f"Exec={exec_cmd}", f"Path=/var/saves/{code}", f"Name={title}", "CePrefix=", f"Icon=/var/games/{code}/{code}.png", "",
                      "[X-CLOVER Game]", f"Code={code}", "TestID=0", "ID=0", f"Players={'2' if (players or '1') != '1' else '1'}", f"Simultaneous={'1' if simultaneous == 'simultaneous' else '0'}", f"ReleaseDate={rel}", f"SaveCount={'3' if battery else '0'}",
                      f"SortRawTitle={sort_title}", f"SortRawPublisher={(publisher or 'UNKNOWN').upper()}", f"Copyright={copyright_}", "", "[m2engage]", "regionTag=", "sortRawGenre=", "index=", "demo_time=", "country=", "", "[Description]", "Text = ", ""])

def exec_for(game: dict, code: str, rom_name: str) -> tuple[str, str]:
    """Exec line and metadata Core for the game's recommended emulator. kachikachi: NesGame.Patch command. RetroArch: the
    catalog's recorded core launch command ('/bin/<core> {rom}', hakchi CE libretro convention) — the core binary exists on
    the console only once the RetroArch module is installed (deployment prerequisite)."""
    rom = f"/var/games/{code}/{rom_name}"
    if game.get("recommended_emulator") == "RetroArch" and game.get("launch_command"):
        cmd = game["launch_command"].replace("{rom}", rom); core = cmd.split()[0].rsplit("/", 1)[-1]; return cmd, core
    return KACHI_EXEC.replace("{rom}", rom), "clover-kachikachi-wr"

def metadata_json(core: str, original_filename: str, crc32: int) -> str:
    return json.dumps({"System": SYSTEM_NAME, "Core": core, "OriginalFilename": original_filename, "OriginalCrc32": crc32, "CustomCoverArt": True}, indent=2) + "\n"

def make_art(src_jpg: str, dest_dir: str, code: str):
    """Cover fitted inside 228x204 and thumbnail inside 40x40 (NesApplication image sizes, v3.9.3). Optional: missing art never blocks."""
    if not Image or not src_jpg or not os.path.exists(src_jpg): return False
    im = Image.open(src_jpg).convert("RGB")
    for name, box in ((code + ".png", (228, 204)), (code + "_small.png", (40, 40))):
        c = im.copy(); c.thumbnail(box, Image.LANCZOS); c.save(os.path.join(dest_dir, name))
    return True

# ------------------------------------------------------------------ apply (transactional)
def apply(man: dict, manifest_file: str, cfg: dict | None = None, approve_alternates: bool = False, launch: bool = True, tree_root: str | None = None, dry_run: bool = False) -> dict:
    cfg = cfg or load_cfg(); root = tree_root or os.path.dirname(cfg["hakchi_exe"]); tree = HakchiTree(root)
    if cfg.get("rehearsal_tree") and os.path.normcase(os.path.abspath(root)) == os.path.normcase(os.path.abspath(os.path.join(PROJECT, "hakchi", "hakchi2-ce-3.9.3"))): raise RuntimeError("rehearsal_tree config must not point at the real hakchi tree")
    man = normalize_manifest(man)
    run_id = time.strftime("%Y%m%d-%H%M%S"); res = {"run_id": run_id, "tree_root": root, "ok": False, "launched": False, "prepared": 0, "reused": 0, "unchecked": 0, "backup_dir": None, "rolled_back": False, "verification": {"match": None}, "message": ""}
    pv = plan(man, approve_alternates); res["preview"] = {k: pv[k] for k in ("can_apply", "problems", "warnings", "summary", "games")}
    if not pv["can_apply"]: res["message"] = "manifest failed validation; nothing changed"; return res
    if cfg.get("rehearsal_tree"):   # test configuration pointing at a disposable copy: the live-hakchi / console checks are about the real tree
        tree_root = tree_root or root
    if not tree_root:
        if hakchi_running(): res["message"] = "Hakchi is running; close it first (the companion never edits its configuration while it is open)"; return res
        if console_connected(): res["message"] = "the console is connected over USB; disconnect it first"; return res
    fresh = not os.path.exists(tree.cfg_path)   # freshly set-up tree: hakchi has not written its configuration yet
    if fresh and not os.path.isfile(os.path.join(root, "hakchi.exe")): res["message"] = f"hakchi config not found: {tree.cfg_path}"; return res
    staging = os.path.join(cfg["staging_dir"], run_id); os.makedirs(staging, exist_ok=True); backup = os.path.join(staging, "backup"); os.makedirs(backup, exist_ok=True)
    ledger_path = os.path.join(cfg["staging_dir"], LEDGER_NAME); ledger = json.load(open(ledger_path, encoding="utf-8")) if os.path.exists(ledger_path) else {"managed": {}, "runs": []}
    # ---- backup config + inventory of games folders (hashes) ----
    if fresh: open(os.path.join(backup, "config-absent.txt"), "w", encoding="utf-8").write("config.json did not exist before this run (fresh Hakchi tree); rollback removes the file this run wrote\n"); hashes = {"config.json": None}
    else: shutil.copy2(tree.cfg_path, os.path.join(backup, "config.json")); hashes = {"config.json": sha256_file(tree.cfg_path)}
    before = tree.custom_entries(); json.dump({k: {"marker": v["marker"], "rom_sha256": v["rom_sha256"], "has_desktop": bool(v["desktop"])} for k, v in before.items()}, open(os.path.join(backup, "games-before.json"), "w", encoding="utf-8"), indent=1)
    json.dump(hashes, open(os.path.join(backup, "SHA256SUMS.json"), "w", encoding="utf-8"), indent=1); res["backup_dir"] = staging
    journal = {"run_id": run_id, "manifest_file": manifest_file, "created_folders": [], "config_before": hashes["config.json"], "tree_root": root}
    # ---- desired state ----
    cfg_json = tree.read_cfg(); coll = cfg_json["gamesCollectionSettings"][COLLECTION]
    desired_originals = sorted(pv["visible_codes"]); desired_selected = []
    stage_dir = os.path.join(staging, "games"); os.makedirs(stage_dir, exist_ok=True)
    try:
        for item in pv["custom"]:
            g = item["game"]; raw = open(item["payload_path"], "rb").read()
            if item.get("headered_file"): data = raw[16:]; out = raw   # personal headered .nes used as-is (its own header, verified by the content hash)
            else: data = raw; out = bytes.fromhex(item["header_hex"]) + data
            if hashlib.sha256(data).hexdigest() != g["payload_sha256"]: raise RuntimeError(f"{g['display_title']}: stored payload hash mismatch")
            csha = hashlib.sha256(out).hexdigest()
            if item["converted_sha256"] and csha != item["converted_sha256"]: raise RuntimeError(f"{g['display_title']}: converted hash differs from catalog")
            crc = zlib.crc32(data) & 0xffffffff; code = code_for(crc)   # hakchi uses the headerless ROM CRC32
            existing = before.get(code)
            if existing and existing["rom_sha256"] == csha and existing["desktop"]:
                res["reused"] += 1; item["row"]["statuses"] = [s for s in item["row"]["statuses"] if s != "ready to prepare"] + ["reused existing ROM"]
            elif existing and existing["rom_sha256"] and existing["rom_sha256"] != csha:
                raise RuntimeError(f"{g['display_title']}: Hakchi already has a different ROM under code {code} (not managed by this integration); refusing to overwrite")
            else:
                d = os.path.join(stage_dir, code); os.makedirs(d, exist_ok=True); rom_name = code + ".nes"
                with open(os.path.join(d, rom_name), "wb") as f: f.write(out)
                if sha256_file(os.path.join(d, rom_name)) != csha: raise RuntimeError(f"{g['display_title']}: staged file verification failed")
                exec_cmd, core = exec_for(g, code, rom_name)
                with open(os.path.join(d, code + ".desktop"), "w", encoding="utf-8", newline="\n") as f: f.write(desktop_text(code, g["display_title"], rom_name, exec_cmd, g.get("players"), g.get("simultaneous"), g.get("year"), g.get("publisher"), f"{g['display_title']} ©{g.get('year') or ''} {g.get('publisher') or ''}".strip(), bool(g.get("battery"))))
                with open(os.path.join(d, "metadata.json"), "w", encoding="utf-8", newline="\n") as f: f.write(metadata_json(core, g["nointro_name"] + ".nes", crc))
                make_art(site_path(g["art"]) if g.get("art") and os.path.exists(site_path(g["art"])) else "", d, code)
                json.dump({"managed_by": "nes-classic-catalog", "game_id": g["game_id"], "nointro_name": g["nointro_name"], "payload_sha256": g["payload_sha256"], "converted_sha256": csha, "headerless_crc32": crc, "code": code, "emulator": g.get("recommended_emulator"), "core": core, "created_run": run_id}, open(os.path.join(d, MARKER), "w", encoding="utf-8"), indent=1)
                item["row"]["statuses"] = [s for s in item["row"]["statuses"] if s != "ready to prepare"] + ["prepared for Hakchi"]; res["prepared"] += 1
            desired_selected.append(code); ledger["managed"][code] = {"game_id": g["game_id"], "converted_sha256": csha, "last_run": run_id}
        desired_selected = sorted(set(desired_selected))
        # entries previously checked but no longer selected (managed or unrelated): unchecked, never deleted
        prev_selected = set(coll.get("SelectedGames", [])); res["unchecked"] = len(prev_selected - set(desired_selected))
        unrelated_checked = [c for c in prev_selected - set(desired_selected) if c not in ledger["managed"]]
        new_cfg = json.loads(json.dumps(cfg_json)); new_cfg["gamesCollectionSettings"][COLLECTION]["OriginalGames"] = desired_originals; new_cfg["gamesCollectionSettings"][COLLECTION]["SelectedGames"] = desired_selected
        # pin rule (docs/decisions.md): hakchi must not download a newer hakchi.hmod / CE build at start-up (MainForm.cs:214-241)
        new_cfg["EnableHakchiUpdate"] = False; new_cfg["EnableCeUpdate"] = False
        # ---- validate the staged configuration before touching the tree ----
        if set(desired_originals) - set(pv["factory_ref"]["codes"]): raise RuntimeError("internal: desired originals contain unknown codes")
        if len(desired_originals) + len(pv["hidden_codes"]) != pv["factory_ref"]["count"]: raise RuntimeError("internal: visible + hidden != 30")
        if dry_run: res.update(ok=True, message="dry run: staged only, hakchi tree untouched", staged_dir=stage_dir); res["verification"] = {"match": None}; return res
        # no-op detection by MEANING, not bytes: hakchi re-serialises config.json on every exit, so an equivalent file is left untouched
        same = (sorted(coll.get("OriginalGames", [])) == desired_originals and sorted(coll.get("SelectedGames", [])) == desired_selected
                and cfg_json.get("EnableHakchiUpdate") is False and cfg_json.get("EnableCeUpdate") is False and not os.listdir(stage_dir) and not fresh)
        res["noop"] = bool(same)
        # ---- commit: move staged folders, then write config ----
        for code in os.listdir(stage_dir):
            dest = os.path.join(tree.games_dir, code); os.makedirs(tree.games_dir, exist_ok=True)
            if os.path.exists(dest):
                if before.get(code, {}).get("marker"): shutil.rmtree(dest)   # re-prepare a managed folder whose ROM changed (revision decision)
                else: raise RuntimeError(f"folder {code} exists and is not managed by this integration")
            shutil.move(os.path.join(stage_dir, code), dest); journal["created_folders"].append(code)
        if not same: tree.write_cfg_atomic(new_cfg)
        # ---- verify ----
        after_cfg = tree.read_cfg()["gamesCollectionSettings"][COLLECTION]; after_entries = tree.custom_entries()
        mism = []
        if sorted(after_cfg.get("OriginalGames", [])) != desired_originals: mism.append("OriginalGames differ")
        if sorted(after_cfg.get("SelectedGames", [])) != desired_selected: mism.append("SelectedGames differ")
        full_after = tree.read_cfg()
        if full_after.get("EnableHakchiUpdate") or full_after.get("EnableCeUpdate"): mism.append("auto-update flags still enabled")
        for code in desired_selected:
            e = after_entries.get(code)
            if not e or not e["desktop"]: mism.append(f"{code}: folder/desktop missing")
            elif e["rom_sha256"] != ledger["managed"][code]["converted_sha256"]: mism.append(f"{code}: ROM hash differs")
        res["verification"] = {"match": not mism, "mismatches": mism, "factory_enabled_count": len(after_cfg.get("OriginalGames", [])), "factory_expected_count": len(desired_originals), "custom_checked_count": len(after_cfg.get("SelectedGames", [])), "custom_expected_count": len(desired_selected), "factory_enabled": sorted(after_cfg.get("OriginalGames", [])), "custom_checked": sorted(after_cfg.get("SelectedGames", [])), "unrelated_unchecked": unrelated_checked}
        if mism: raise RuntimeError("post-apply verification failed: " + "; ".join(mism))
        journal["config_after"] = sha256_file(tree.cfg_path); json.dump(journal, open(os.path.join(staging, "journal.json"), "w", encoding="utf-8"), indent=1)
        ledger["runs"].append({"run_id": run_id, "manifest_file": manifest_file, "originals": desired_originals, "selected": desired_selected, "prepared": res["prepared"], "reused": res["reused"], "unchecked": res["unchecked"]})
        json.dump(ledger, open(ledger_path, "w", encoding="utf-8"), indent=1)
        res["ok"] = True; res["message"] = "collection already applied in Hakchi; configuration left untouched (no-op)" if same else "PC-side Hakchi collection reconciled with the manifest"
        if launch and cfg.get("no_launch"): res["message"] += "; launch disabled by configuration"; launch = False
        if launch:
            ok, why = hakchi_exe_ok(cfg["hakchi_exe"] if not tree_root else os.path.join(root, "hakchi.exe"))
            if ok:
                exe = cfg["hakchi_exe"] if not tree_root else os.path.join(root, "hakchi.exe")
                try: p = subprocess.Popen([exe] + list(cfg.get("hakchi_args", ["/portable"])), cwd=root); res["launched"] = True; res["hakchi_pid"] = p.pid
                except Exception as e:   # the collection IS applied and verified; only the launch failed — never roll back a verified state for that
                    res["launch_error"] = str(e); res["message"] += f"; Hakchi could not be started ({e})"
                    res["recovery"] = f"The collection is applied on the PC side. Start Hakchi manually ({exe} /portable) and review it, or undo this run with hakchi_bridge.rollback(r'{staging}')."
            else: res["message"] += f"; Hakchi not launched ({why})"
        return res
    except Exception as e:
        res["message"] = f"failed: {e}"; res["error"] = str(e)
        json.dump(journal, open(os.path.join(staging, "journal.json"), "w", encoding="utf-8"), indent=1)   # the journal must be on disk BEFORE rollback reads it (folders moved so far)
        try: rollback(staging, tree); res["rolled_back"] = True; res["recovery"] = f"Nothing to do: the previous configuration was restored from {backup} and the folders this run created were removed; Hakchi's library is as it was before the click. Fix the reported cause and click Open in Hakchi again."
        except Exception as e2: res["message"] += f"; ROLLBACK FAILED: {e2}"; res["recovery"] = f"Manual recovery: copy {os.path.join(backup, 'config.json')} over config\\config.json (or delete config.json if backup\\config-absent.txt exists) and delete the folders listed under created_folders in {os.path.join(staging, 'journal.json')}; then verify with hakchi_inspect.py."
        json.dump(journal, open(os.path.join(staging, "journal.json"), "w", encoding="utf-8"), indent=1); return res

def rollback(staging: str, tree: HakchiTree | None = None, cfg: dict | None = None) -> dict:
    """Restore config.json from the run's backup and remove the folders that run created. Unrelated content is never touched."""
    cfg = cfg or load_cfg(); j = json.load(open(os.path.join(staging, "journal.json"), encoding="utf-8")) if os.path.exists(os.path.join(staging, "journal.json")) else {"created_folders": []}
    tree = tree or HakchiTree(j.get("tree_root") or os.path.dirname(cfg["hakchi_exe"]))
    bk = os.path.join(staging, "backup", "config.json"); want = json.load(open(os.path.join(staging, "backup", "SHA256SUMS.json"), encoding="utf-8"))["config.json"]
    if want is None:   # fresh tree before the run: the run created config.json, so restoring means removing it
        if os.path.exists(tree.cfg_path): os.remove(tree.cfg_path)
    else:
        if sha256_file(bk) != want: raise RuntimeError("backup config.json hash mismatch; not restoring")
        shutil.copy2(bk, tree.cfg_path)
    removed = []
    for code in j.get("created_folders", []):
        d = os.path.join(tree.games_dir, code)
        if os.path.isdir(d) and os.path.exists(os.path.join(d, MARKER)): shutil.rmtree(d); removed.append(code)
    return {"restored_config": (not os.path.exists(tree.cfg_path)) if want is None else sha256_file(tree.cfg_path) == want, "removed_folders": removed}
