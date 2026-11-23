r"""The notices every console package carries (tools/companion/native_home/notices.py): made from Classic Cart Studio's records, and a
package whose notices are missing, changed or incomplete is refused. Offline, on this computer; nothing is sent anywhere.

  python test_package_notices.py [--site SITE] [--package BUILT_PACKAGE_DIR ...]      Exit 0 = PASS.

SITE: the site whose included/ and art/scenes/ records are read (default: the repository's site-data, which has the same records).
Checks: the records audit (every record file, notices row and section; the compiled libretro-common list matches build_player.sh); a
synthetic package with every kind of part (the player, the HOME program, fonts, theme art, two scenes, an Included Game with its cover,
a game of the person's own with box art, the person's own pictures, music, startup images, descriptions, Classic Cart Studio's files):
every license text is byte for byte its record, each Included Game's own license file is carried and its source named, Magic Cliffs is
credited under CC BY 3.0 with its license address and changes, and the package checks out. Then each kind of fault is refused: a missing
text, a changed text, a changed NOTICE.txt, an extra file in licenses/, a file no part covers, and a scene without a credit record. Each
--package (a built console package folder) is checked as it is."""
from __future__ import annotations
import argparse, hashlib, json, os, shutil, sys, tempfile
HERE = os.path.dirname(os.path.abspath(__file__)); REPO = os.path.normpath(os.path.join(HERE, "..", ".."))

def main() -> int:
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    ap = argparse.ArgumentParser(); ap.add_argument("--site", default=os.path.join(REPO, "site-data")); ap.add_argument("--package", action="append", default=[]); a = ap.parse_args()
    os.environ["CCS_STUDIO_SITE"] = os.path.abspath(a.site)
    for p in (HERE, os.path.join(HERE, "..", "rom-pipeline")): sys.path.insert(0, os.path.normpath(p))
    from native_home import notices as NT, console_package as cpk
    res = []
    def ok(n, c, d=""): res.append((n, bool(c))); print(("PASS " if c else "FAIL ") + n + (f"  ({str(d)[:300]})" if d and not c else ""), flush=True)

    ok("the records are complete (audit)", not NT.audit(), NT.audit())
    inc = NT.included(); thw = next(g for g in inc.values() if g["key"] == "thwaite")
    other = "ab" * 32
    games = [{"id": "p_" + thw["content_sha256"][:16], "title": thw["title"], "cover": "cover/1111111111111111.jpg", "cover_source": "included/art/thwaite-cover.png", "cover_kind": "authentic",
              "scene": "magic-cliffs", "text": "text/aaaaaaaaaaaaaaaa.json"},
             {"id": "g1", "title": "A game", "cover": "cover/2222222222222222.jpg", "cover_source": "art/x.jpg", "cover_kind": "authentic", "scene": "forest-of-illusion", "text": "text/bbbbbbbbbbbbbbbb.json"},
             {"id": "g2", "title": "Another", "cover": "cover/3333333333333333.jpg", "cover_source": None, "cover_kind": "user", "scene": "forest-of-illusion", "text": None}]
    files = {"bin/ccs-player-armv7": b"\x7fELF player", "home/bin/ccs-home-armv7": b"\x7fELF home", "fonts/silk17.ccsf": b"f", "fonts/OFL-Silkscreen.txt": b"ofl",
             "home/font/body.ccsf": b"f", "home/licenses/OFL-Barlow.txt": b"ofl", "home/licenses/THIRD-PARTY.txt": b"tp", "home/theme/arcade-grid/scene.png": b"s",
             "home/theme/icon-games-22.png": b"i", "home/hero/aaaa.jpg": b"h", "home/snap/dddd.png": b"s", "home/cover/1111111111111111.jpg": b"c1",
             "home/cover/2222222222222222.jpg": b"c2", "home/cover/3333333333333333.jpg": b"c3", "home/folder/cccc.jpg": b"fo", "home/design/design.json": b"{}",
             "home/music/ccs-home-theme.wav": b"RIFF", "home/text/aaaaaaaaaaaaaaaa.json": b"{}", "home/text/bbbbbbbbbbbbbbbb.json": b"{}",
             f"roms/{thw['content_sha256']}.nes": b"NES\x1a", f"roms/{other}.nes": b"NES\x1a", "startup/startup-pixel-worlds.jpg": b"j", "startup/startup-custom-0123456789abcdef.jpg": b"j",
             "startup/startup.json": b"{}", "config/controller-map.txt": b"k", "session.json": b"{}", "home/games.json": json.dumps({"games": games}).encode(),
             "home/pack.json": b"{}", "home/config.json": b"{}", "home/manifest.json": b"{}"}
    work = tempfile.mkdtemp(prefix="ccs-notices-test-")
    try:
        def make(name, fs, gs=games, label="Test Collection"):
            d = os.path.join(work, name); st = cpk.Stage(d)
            for rel, b in fs.items(): st.put(rel, b, "x")
            mx = NT.write(st, gs, label)
            with open(os.path.join(d, "MANIFEST.json"), "w", encoding="utf-8") as f: json.dump({"label": label, "files": st.files}, f)
            return d, mx
        d, mx = make("good", files)
        ok("a complete package checks out", not NT.check_dir(d), NT.check_dir(d))
        lic = sorted(os.listdir(os.path.join(d, "licenses")))
        want = sorted(set(NT.PLAYER_TEXTS) | {"NOTICE.txt", NT.game_license_name(thw)})
        ok("licenses/ holds exactly the texts its parts need", lic == want, lic)
        recs = NT.texts(NT.PLAYER_TEXTS)
        ok("every license text is its record, byte for byte", all(open(os.path.join(d, "licenses", n), "rb").read() == b for n, b in recs.items()))
        gl = open(os.path.join(REPO, "site-data", *thw["license_file"].split("/")), "rb").read()
        ok("the Included Game's own license file is carried as it is", open(os.path.join(d, "licenses", NT.game_license_name(thw)), "rb").read() == gl)
        notice = open(os.path.join(d, "licenses", "NOTICE.txt"), encoding="utf-8").read()
        ok("NOTICE.txt names the Included Game's source and archive", f"site-data/{thw['source']}" in notice and thw["original_archive"]["sha256"] in notice and thw["file_sha256"] in notice)
        ok("Magic Cliffs credited: CC BY 3.0, license address, author, source, changes",
           all(x in notice for x in ("Magic Cliffs Environment", "CC BY 3.0", "https://creativecommons.org/licenses/by/3.0/", "Luis Zuno", "https://opengameart.org/content/magic-cliffs-environment", "Changes: Composed")))
        ok("NOTICE.txt covers each part present", all(x in notice for x in ("The game player", "The HOME program", "Fonts", "HOME theme art", "Game backdrops", "Game screens", "The Included Games' covers",
           "Game box art", "Pictures Classic Cart Studio draws", "The collection's own pictures", "HOME Music", "Console startup images", "Game descriptions", "Games (roms/)", "Classic Cart Studio's own files")))
        ok("the person's own game is not given a license", "1 other game file(s): supplied by the person who made this collection" in notice)
        ok("no license is claimed for box art", "no license claimed" in notice)
        ok("the catalog descriptions' missing record is reported", any("descriptions" in g for g in mx["record_gaps"]), mx["record_gaps"])
        # faults
        def fault(name, act):
            f = os.path.join(work, name); shutil.copytree(d, f); act(f); return NT.check_dir(f)
        p = fault("missing", lambda f: os.remove(os.path.join(f, "licenses", "musl-COPYRIGHT.txt")) or _drop(f, "licenses/musl-COPYRIGHT.txt"))
        ok("a missing license text is refused", any("musl-COPYRIGHT.txt is missing" in x for x in p), p)
        p = fault("changed", lambda f: open(os.path.join(f, "licenses", "GPL-3.0.txt"), "ab").write(b" "))
        ok("a changed license text is refused", any("GPL-3.0.txt is not the text of its record" in x for x in p), p)
        p = fault("notice", lambda f: open(os.path.join(f, "licenses", "NOTICE.txt"), "ab").write(b"\nextra"))
        ok("a changed NOTICE.txt is refused", any("NOTICE.txt is not the notice of this package" in x for x in p), p)
        p = fault("extra", lambda f: _add(f, "licenses/OTHER.txt", b"x"))
        ok("a text the package's parts do not need is refused", any("OTHER.txt is not a notice of this package" in x for x in p), p)
        p = fault("uncovered", lambda f: _add(f, "home/extra/thing.bin", b"x"))
        ok("a file no part covers is refused", any("no notice covers" in x and "home/extra/thing.bin" in x for x in p), p)
        try: make("uncovered-build", files | {"tools/x.bin": b"x"}); built = True
        except ValueError: built = False
        ok("the build stops at a file no part covers", not built)
        try: make("noscene", files, [dict(games[0], scene="not-a-scene")] + games[1:]); built = True
        except ValueError: built = False
        ok("the build stops at a scene without a credit record", not built)
        p = fault("nonotice", lambda f: os.remove(os.path.join(f, "licenses", "NOTICE.txt")) or _drop(f, "licenses/NOTICE.txt"))
        ok("a package without NOTICE.txt is refused", p == ["the package has no licenses/NOTICE.txt"], p)
        # HOME Music as MP3 (a player built with it, held in this version): its notice names minimp3 and carries its license text; the
        # shipped player's notices never mention it, and a package whose texts disagree with its player is refused
        mfiles = dict(files); mfiles["bin/ccs-player-armv7"] = b"\x7fELF player " + NT.MP3_MARK; mfiles.pop("home/music/ccs-home-theme.wav"); mfiles["home/music/ccs-home-theme.mp3"] = b"ID3"
        dm, _ = make("mp3", mfiles); mn = open(os.path.join(dm, "licenses", "NOTICE.txt"), encoding="utf-8").read()
        ok("a player built with MP3: minimp3 named in the notice, its license text carried, the package checks out",
           not NT.check_dir(dm) and sorted(os.listdir(os.path.join(dm, "licenses"))) == sorted(want + ["minimp3-LICENSE.txt"]) and "minimp3 at commit 142823796ab8efa55e1bf11406028f3be77ecceb" in mn
           and open(os.path.join(dm, "licenses", "minimp3-LICENSE.txt"), "rb").read() == NT.texts(["minimp3-LICENSE.txt"])["minimp3-LICENSE.txt"], NT.check_dir(dm))
        ok("the shipped player's notice does not mention minimp3", "minimp3" not in open(os.path.join(d, "licenses", "NOTICE.txt"), encoding="utf-8").read() and not NT.player_has_mp3(lambda r: files[r]))
        dx, _ = make("mp3x", mfiles); os.remove(os.path.join(dx, "licenses", "minimp3-LICENSE.txt")); _drop(dx, "licenses/minimp3-LICENSE.txt")
        ok("an MP3 player's package without minimp3's license text is refused", any("minimp3-LICENSE.txt is missing" in x for x in NT.check_dir(dx)), NT.check_dir(dx))
        for pk in a.package:
            p = NT.check_dir(pk); ok(f"built package {os.path.basename(os.path.normpath(pk))} checks out", not p, p)
    finally:
        shutil.rmtree(work, ignore_errors=True)
    bad = [n for n, c in res if not c]; print(f"{len(res) - len(bad)}/{len(res)} passed"); return 1 if bad else 0

def _drop(d, rel):
    p = os.path.join(d, "MANIFEST.json"); m = json.load(open(p, encoding="utf-8")); m["files"].pop(rel); json.dump(m, open(p, "w", encoding="utf-8"))

def _add(d, rel, b):
    q = os.path.join(d, *rel.split("/")); os.makedirs(os.path.dirname(q), exist_ok=True); open(q, "wb").write(b)
    p = os.path.join(d, "MANIFEST.json"); m = json.load(open(p, encoding="utf-8")); m["files"][rel] = {"size": len(b), "sha256": hashlib.sha256(b).hexdigest(), "kind": "x"}; json.dump(m, open(p, "w", encoding="utf-8"))

if __name__ == "__main__": sys.exit(main())
