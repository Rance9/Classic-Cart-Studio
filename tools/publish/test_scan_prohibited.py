"""The prohibited-content scanner's local-path rule (scan_prohibited.LOCAL_PATH): shipped data and presentation must not link to a file on
the build computer. Representative forms are found (file: URIs in any case, JSON-escaped and percent-encoded, drive paths into private
roots with any slash style); ordinary text, game names, protocol checks and file inputs are not; build scripts are not held to the rule.
  python tools/publish/test_scan_prohibited.py"""
from __future__ import annotations
import os, sys, tempfile, unittest
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import scan_prohibited as S

D = "C" + ":"   # assembled, so this file holds no literal local path
FOUND = [f"file:///{D}/NES/work/db/MesenNesDB.txt", f"FILE:///{D.lower()}/nes/x.txt", "file:\\/\\/\\/" + D + "\\/NES\\/work", f"file://localhost/{D}/x",
         "file:///home/someone/notes.txt", f"file:%2F%2F%2F{D}%2FUsers%2Fx", f"{D}/NES/work/db", f"{D}\\NES\\work", f"{D}\\\\NES\\\\work",
         D + "/" + "Users" + "/someone/x", "c%3A%2FNES%2Fwork", "Some" + "One" + "Drive/Apps/x"]
NOT_FOUND = ["Gemini Man and Needle Man", "if(location.protocol==='file:') return;", '<input type="file" accept=".nes,.zip">',
             "s.startsWith('file://')", "https://github.com/SourMesen/Mesen2/blob/b9fa69d/UI/Dependencies/MesenNesDB.txt",
             "the NES Classic Edition", "Save the file: then open it", "%LOCALAPPDATA%\\ClassicCartStudio", "A:B testing"]

class T(unittest.TestCase):
    def test_1_representative_local_paths_are_found(self):
        for s in FOUND: self.assertTrue(S.LOCAL_PATH.search(s), s)
    def test_2_ordinary_text_is_not_a_local_path(self):
        for s in NOT_FOUND: self.assertIsNone(S.LOCAL_PATH.search(s), s)
    def test_3_tree_scan_holds_shipped_files_to_the_rule_and_leaves_build_scripts(self):
        with tempfile.TemporaryDirectory() as d:
            for name, text in (("catalog.json", '{"url":"' + FOUND[0] + '"}'), ("index.html", "<a href=\"" + FOUND[1] + "\">x</a>"),
                               ("ok.json", '{"t":"Gemini Man"}'), ("tool.py", "# reads " + FOUND[6]), ("build.ps1", "# " + FOUND[7])):
                open(os.path.join(d, name), "w", encoding="utf-8").write(text)
            flagged = sorted(f["file"] for f in S.scan_tree(d, []) if f["kind"] == "private-path")
            self.assertEqual(flagged, ["catalog.json", "index.html"])
    def test_5_only_the_pinned_included_games_pass_by_path_and_hash(self):
        self.assertTrue(S.INCLUDED, "included-games.sha256 is read")
        site = os.path.join(os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__)))), "site-data")
        p, want = next(iter(S.INCLUDED.items())); real = open(os.path.join(site, *p.split("/")), "rb").read()
        with tempfile.TemporaryDirectory() as d:
            def put(rel, b): q = os.path.join(d, *rel.split("/")); os.makedirs(os.path.dirname(q), exist_ok=True); open(q, "wb").write(b)
            put(p, real)                                                        # the pinned file at its published path: accepted
            put("_internal/site/" + p, real)                                    # the app's copy of the site: accepted
            put("elsewhere/" + os.path.basename(p), real)                       # the same bytes at another path: refused
            put(os.path.dirname(p) + "/other.nes", real)                        # an unlisted name in the folder: refused
            put("copy2/" + p, b"NES\x1a" + b"\0" * 40000)                       # other bytes at a path ending with the pinned one: refused (hash)
            bad = sorted(f["file"] for f in S.scan_tree(d, []) if f["kind"] == "prohibited")
        self.assertNotIn(p, bad); self.assertNotIn("_internal/site/" + p, bad)
        for r in ("elsewhere/" + os.path.basename(p), os.path.dirname(p) + "/other.nes", "copy2/" + p): self.assertIn(r, bad)
    def test_4_the_catalog_export_refuses_local_sources_and_keeps_citations(self):
        sys.path.insert(0, os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), "rom-pipeline")); import input_requirements as IR
        cite = {"source": "Mesen game database (MesenNesDB.txt)", "url": IR.MESEN_DB_URL, "note": "generated 2022-07-21"}
        self.assertEqual(IR.curated_evidence(["https://en.wikipedia.org/wiki/NES_Zapper", cite]),
                         [{"source": "curated accessory list", "url": "https://en.wikipedia.org/wiki/NES_Zapper"}, cite])
        for bad in (FOUND[0], {"source": "x", "url": FOUND[6]}, {"source": "x"}):
            with self.assertRaises(ValueError): IR.curated_evidence([bad])

if __name__ == "__main__":
    r = unittest.main(exit=False, verbosity=2).result; print(f"RESULT {r.testsRun - len(r.failures) - len(r.errors)} / {r.testsRun}"); sys.exit(0 if r.wasSuccessful() else 1)
