r"""A collection's own startup picture in the NES Classic console package, built offline with no console attached (nothing is
sent anywhere). The page sends the chosen picture inside the HOME settings (startup_custom: its reference and its bytes); the package
checks it again and writes it as the console reads it.
  boundary  startup_custom(): a still 1280x720 JPEG named by its SHA-256 is accepted and written again with its pixels only (no EXIF,
            ICC profile or comment); the wrong bytes, size, type, an animated or damaged picture, a missing picture or a reference that does
            not match are each refused with a plain reason; Random and the five supplied pictures need no picture
  package   with an Included Game: the package builds, startup.json chooses the picture and lists it first (then the five), the file is
            there with the SHA-256 startup.json and MANIFEST.json list, it is 1280x720 and carries no metadata, the package verifies
  refused   a chosen picture that is not with the collection stops the package with that reason (never quietly Random)
  python test_startup_custom_package.py <built site folder> [--player ccs-player-armv7] [--out results.json]      Exit 0 = PASS."""
from __future__ import annotations
import argparse, base64, hashlib, io, json, os, socket, sys, tempfile
HERE = os.path.dirname(os.path.abspath(__file__))

def jpeg(size=(1280, 720), meta=True, fmt="JPEG", color=(30, 90, 160)) -> bytes:
    from PIL import Image
    im = Image.new("RGB", size, color)
    for x in range(0, size[0], 40): im.paste((200, 60, 40), (x, 0, x + 20, size[1]))
    b = io.BytesIO()
    if fmt == "JPEG" and meta:
        exif = Image.Exif(); exif[0x010E] = "private description"; exif[0x013B] = "Owner Name"   # ImageDescription, Artist
        im.save(b, "JPEG", quality=88, exif=exif.tobytes(), icc_profile=b"\x00" * 128, comment=b"secret comment")
    else: im.save(b, fmt)
    return b.getvalue()

def main() -> int:
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    ap = argparse.ArgumentParser(); ap.add_argument("site"); ap.add_argument("--player", default=""); ap.add_argument("--out", default=""); a = ap.parse_args()
    site = os.path.abspath(a.site); work = tempfile.mkdtemp(prefix="ccs-startup-custom-"); res = []; net = []
    os.environ["CCS_STUDIO_SITE"] = site; os.environ["CCS_STUDIO_DATA"] = os.path.join(work, "data"); os.makedirs(os.environ["CCS_STUDIO_DATA"])
    real = socket.socket.connect
    def guard(self, addr):
        host = addr[0] if isinstance(addr, tuple) else str(addr)
        if host not in ("127.0.0.1", "localhost", "::1"): net.append(host); raise OSError("network blocked by the test")
        return real(self, addr)
    socket.socket.connect = guard
    for p in (HERE, os.path.join(HERE, "..", "rom-pipeline")): sys.path.insert(0, os.path.normpath(p))
    def ok(n, c, d=""): res.append((n, bool(c))); print(("PASS " if c else "FAIL ") + n + (f"  ({str(d)[:300]})" if d and not c else ""), flush=True)
    sha = lambda b: hashlib.sha256(b).hexdigest()
    def ref(b, **kw):
        h = sha(b); c = {"id": "custom-" + h[:16], "sha256": h, "name": "Living room", "fit": "fill", "bytes": len(b), "width": 1280, "height": 720, "data": base64.b64encode(b).decode()}
        c.update(kw); return {"startup_image": c["id"], "startup_custom": c}
    try:
        from PIL import Image
        import personal as ps, library_lookup as LL
        from native_home import console_package as cpk
        # ---- the packaging boundary ----
        good = jpeg(); e, b, why = cpk.startup_custom(ref(good))
        im = Image.open(io.BytesIO(b or b"")) if b else None
        ok("boundary: a still 1280x720 JPEG named by its SHA-256 is accepted", e and not why and e["id"] == "custom-" + sha(good)[:16] and e["source_sha256"] == sha(good) and e["sha256"] == sha(b), why)
        ok("boundary: it is written again with its pixels only (no EXIF, ICC profile or comment; the name and source stay off the picture)",
           im and im.size == (1280, 720) and im.format == "JPEG" and "exif" not in im.info and "icc_profile" not in im.info and "comment" not in im.info and b"Owner Name" not in b and b"secret comment" not in b, im and im.info.keys())
        bad = {"another picture's bytes": dict(data=base64.b64encode(jpeg(color=(1, 2, 3))).decode()),
               "a 1920x1080 picture": None, "a PNG": None, "damaged data": dict(data="not base64!!"), "no picture with it": dict(data=None), "an id that does not match": dict(id="custom-0123456789abcdef")}
        for k, kw in bad.items():
            if k == "a 1920x1080 picture": h = ref(jpeg((1920, 1080)))
            elif k == "a PNG": h = ref(jpeg(fmt="PNG"))
            else:
                h = ref(good); h["startup_custom"].update(kw or {})
                if kw and kw.get("data") is None: h["startup_custom"].pop("data")
            e, b, why = cpk.startup_custom(h)
            ok(f"boundary: {k} is refused with a plain reason", e is None and b is None and isinstance(why, str) and why, why)
        anim = io.BytesIO(); Image.new("RGB", (1280, 720)).save(anim, "GIF", save_all=True, append_images=[Image.new("RGB", (1280, 720), (9, 9, 9))])
        e, b, why = cpk.startup_custom(ref(anim.getvalue())); ok("boundary: an animated picture is refused", e is None and why, why)
        ok("boundary: Random and the five supplied pictures need no picture", all(cpk.startup_custom({"startup_image": v}) == (None, None, None) for v in ("random", "forgotten-keep", "pixel-worlds")))
        ok("boundary: startup_choice keeps the collection's own picture as the choice (never Random)", cpk.startup_choice(ref(good)) == "custom-" + sha(good)[:16])
        # ---- the package ----
        idx = json.load(open(os.path.join(site, "included", "index.json"), encoding="utf-8")); g = idx["games"][0]; fp = os.path.join(site, "included", g["file_name"])
        r = ps.ingest(fp, origin="included"); pid = r["entry"]["id"]; ps.merge_metadata(pid, {"title": g["title"], "version_label": g["version"]}); en = LL.personal_index()["entries"][pid]
        man = {"schema": "nes-classic-collection/4", "name": "Startup picture", "hidden_factory_games": [], "selections": [{"game_id": pid, "version_id": pid, "display_title": en["user"]["title"], "source": "personal", "install_required": True}]}
        home = {"schema": "ccs-home-collection/1", "theme": "arcade-grid", "order": [pid], "games": [{"id": pid, "title": en["user"]["title"], "version_label": "", "kind": "personal"}],
                "folders": [], "favorites": {"items": {}}, "settings": {"reduced_motion": False, "hints": True}, "sample": False}
        home.update(ref(good)); out = os.path.join(work, "console")
        bc = cpk.build_console(man, home, out, layout="install", player=a.player or None, label="Startup picture")
        ok("package: it builds offline with the collection's own picture", bc.get("ok"), bc.get("error"))
        if bc.get("ok"):
            st = json.load(open(os.path.join(out, "startup", "startup.json"), encoding="utf-8")); m = json.load(open(os.path.join(out, "MANIFEST.json"), encoding="utf-8"))
            first = st["images"][0]; f = os.path.join(out, "startup", first["file"]); fb = open(f, "rb").read() if os.path.isfile(f) else b""
            ok("package: startup.json chooses the picture and lists it first, then the five", st["choice"] == "custom-" + sha(good)[:16] and first["id"] == st["choice"] and [i["id"] for i in st["images"][1:]] == list(cpk.STARTUP_IDS), st)
            ok("package: the file is there with the SHA-256 startup.json and MANIFEST.json list", fb and sha(fb) == first["sha256"] and m["files"].get("startup/" + first["file"], {}).get("sha256") == sha(fb), first)
            pim = Image.open(io.BytesIO(fb)) if fb else None
            ok("package: it is a 1280x720 JPEG with no metadata", pim and pim.size == (1280, 720) and pim.format == "JPEG" and not ({"exif", "icc_profile", "comment"} & set(pim.info)), pim and pim.info.keys())
            ok("package: MANIFEST.json names the picture (choice, name, SHA-256)", m["startup"].get("choice") == st["choice"] and (m["startup"].get("custom") or {}).get("sha256") == sha(fb) and m["startup"]["custom"]["name"] == "Living room", m["startup"])
            ok("package: the package verifies", cpk.verify(out)["ok"])
        h2 = dict(home); h2["startup_custom"] = dict(home["startup_custom"]); h2["startup_custom"].pop("data")
        bc2 = cpk.build_console(man, h2, os.path.join(work, "console2"), layout="install", player=a.player or None, label="Startup picture")
        ok("refused: a chosen picture that is not with the collection stops the package with that reason (never quietly Random)",
           not bc2.get("ok") and "startup picture" in (bc2.get("error") or "") and not os.path.exists(os.path.join(work, "console2")), bc2.get("error"))
        ok("no network connection was attempted", not net, net)
    except Exception as e:
        import traceback; traceback.print_exc(); ok("the test ran to its end", False, e)
    fails = [n for n, c in res if not c]
    print(f"\n{len(res) - len(fails)}/{len(res)} PASS" + (f"; FAIL: {', '.join(fails)}" if fails else ""))
    if a.out: json.dump({"results": [{"name": n, "pass": c} for n, c in res]}, open(a.out, "w", encoding="utf-8"), indent=1)
    return 0 if not fails else 1

if __name__ == "__main__": sys.exit(main())
