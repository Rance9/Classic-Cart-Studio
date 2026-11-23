"""The CCS Collection Interface kit on the downloads page (kit_download.py): off unless asked for; when asked for, the kit's own archive and
manifest in downloads/ and one entry on /app/. Copyright (C) 2026 Rance9, GPL-3.0-or-later.
  python tools/publish/test_kit_download.py      Exit 0 = PASS."""
from __future__ import annotations
import hashlib, json, os, shutil, sys, tempfile, unittest, zipfile
HERE = os.path.dirname(os.path.abspath(__file__)); sys.path.insert(0, HERE)
import kit_download as KD   # noqa: E402

class KitDownload(unittest.TestCase):
    def setUp(self):
        self.t = tempfile.mkdtemp(prefix="ccs-kitdl-"); os.makedirs(os.path.join(self.t, "app"))
        shutil.copy2(os.path.join(HERE, "site-static", "app", "index.html"), os.path.join(self.t, "app", "index.html"))
        with open(os.path.join(self.t, "app", "index.html"), encoding="utf-8") as f: self.page = f.read()
    def tearDown(self): shutil.rmtree(self.t, ignore_errors=True)
    def test_01_off_unless_asked_for(self):
        self.assertIsNone(KD.apply(self.t, {})); self.assertIsNone(KD.apply(self.t, {KD.SWITCH: "0"}))
        with open(os.path.join(self.t, "app", "index.html"), encoding="utf-8") as f: self.assertEqual(f.read(), self.page, "the page as it was")
        self.assertFalse(os.path.exists(os.path.join(self.t, "downloads")))
    def test_02_kit_archive_manifest_and_entry(self):
        man = KD.apply(self.t, {KD.SWITCH: "1"}); self.assertIsNotNone(man)
        z = os.path.join(self.t, "downloads", man["file"])
        with open(z, "rb") as f: b = f.read()
        self.assertEqual((man["sha256"], man["size"]), (hashlib.sha256(b).hexdigest(), len(b))); self.assertEqual((man["interface"], man["license"]), (1, "MIT"))
        with zipfile.ZipFile(z) as zf:
            names = zf.namelist(); top = man["file"][:-4]
            self.assertTrue(all(n.startswith(top + "/") for n in names))
            for need in ("LICENSE", "README.md", "schema/ccs-collection-interface-v1.schema.json", "reader/ccs-ci", "reader/ccs-ci-flat.awk", "checker/ccs_ci_check.py", "examples/minimal.json"):
                self.assertIn(f"{top}/{need}", names)
            self.assertTrue(zf.read(f"{top}/LICENSE").startswith(b"MIT License"))
            for n in names: self.assertNotIn(b"GPL-3.0", zf.read(n), f"{n}: nothing of Classic Cart Studio's own GPL code is in the kit")
        with open(os.path.join(self.t, "app", "index.html"), encoding="utf-8") as f: page = f.read()
        self.assertEqual(page.count("<h2>CCS Collection Interface kit</h2>"), 1); self.assertIn(f'href="../downloads/{man["file"]}"', page)
        self.assertIn(man["sha256"], page); self.assertRegex(page, r"ZIP, \d+ KB")
        man2 = KD.build(os.path.join(self.t, "again")); self.assertEqual(man2["sha256"], man["sha256"], "the same kit always gives the same archive")

if __name__ == "__main__": unittest.main(verbosity=1)
