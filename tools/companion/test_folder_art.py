"""Unit tests for the custom folder artwork store (transactional replace, validation, failure paths, removal).
  python -m unittest tools/companion/test_folder_art.py"""
import os, sys, tempfile, unittest, shutil
HERE = os.path.dirname(os.path.abspath(__file__)); sys.path.insert(0, HERE)
WORK = tempfile.mkdtemp(prefix="ccs-folder-art-"); os.environ["NES_PERSONAL_DIR"] = os.path.join(WORK, "personal")
import folder_art as fa
from PIL import Image

def png(name, size, color):
    p = os.path.join(WORK, name); Image.new("RGB", size, color).save(p); return p

class FolderArtTests(unittest.TestCase):
    @classmethod
    def tearDownClass(cls): shutil.rmtree(WORK, ignore_errors=True)
    def files(self, fid): return sorted(fa._files(fid))
    def test_01_set_creates_source_and_derivatives(self):
        r = fa.set_art("f1", png("a.png", (640, 400), (200, 40, 40)))
        self.assertEqual(r["provenance"], "custom"); self.assertEqual(self.files("f1"), ["f1-cover.jpg", "f1-src.png", "f1-wide.jpg"])
        with Image.open(os.path.join(fa.FOLDER_ART_DIR, "f1-cover.jpg")) as c: self.assertEqual(c.size, (480, 720))
        with Image.open(os.path.join(fa.FOLDER_ART_DIR, "f1-wide.jpg")) as w: self.assertEqual(w.size, (810, 447))
        self.assertFalse(any(n.endswith(fa.STAGE) for n in os.listdir(fa.FOLDER_ART_DIR)))
    def test_02_replace_swaps_and_drops_old_source_extension(self):
        jpg = os.path.join(WORK, "b.jpg"); Image.new("RGB", (900, 300), (10, 200, 10)).save(jpg, "JPEG")
        before = open(os.path.join(fa.FOLDER_ART_DIR, "f1-cover.jpg"), "rb").read()
        fa.set_art("f1", jpg)
        self.assertEqual(self.files("f1"), ["f1-cover.jpg", "f1-src.jpg", "f1-wide.jpg"])
        self.assertNotEqual(before, open(os.path.join(fa.FOLDER_ART_DIR, "f1-cover.jpg"), "rb").read())
        with Image.open(os.path.join(fa.FOLDER_ART_DIR, "f1-cover.jpg")) as c: self.assertEqual(c.getpixel((240, 360))[1] > 150, True)
    def test_03_invalid_replacement_keeps_previous(self):
        before = {n: open(os.path.join(fa.FOLDER_ART_DIR, n), "rb").read() for n in self.files("f1")}
        for bad, msg in ((png("small.png", (80, 80), (0, 0, 0)), "smaller"), (os.path.join(WORK, "missing.png"), "no file"), (os.path.join(WORK, "fake.png"), "readable image")):
            if bad.endswith("fake.png"): open(bad, "wb").write(b"not an image")
            with self.assertRaises(ValueError) as cm: fa.set_art("f1", bad)
            self.assertIn(msg, str(cm.exception))
        after = {n: open(os.path.join(fa.FOLDER_ART_DIR, n), "rb").read() for n in self.files("f1")}
        self.assertEqual(before, after); self.assertFalse(any(n.endswith(fa.STAGE) for n in os.listdir(fa.FOLDER_ART_DIR)))
    def test_04_failed_derivative_keeps_previous_and_no_staging_left(self):
        before = {n: open(os.path.join(fa.FOLDER_ART_DIR, n), "rb").read() for n in self.files("f1")}
        orig = fa._crop_to; calls = {"n": 0}
        def boom(im, size):
            calls["n"] += 1
            if calls["n"] == 2: raise RuntimeError("disk full while writing the wide derivative")
            return orig(im, size)
        fa._crop_to = boom
        try:
            with self.assertRaises(ValueError) as cm: fa.set_art("f1", png("c.png", (700, 700), (0, 0, 220)))
        finally: fa._crop_to = orig
        self.assertIn("previous artwork is unchanged", str(cm.exception))
        after = {n: open(os.path.join(fa.FOLDER_ART_DIR, n), "rb").read() for n in self.files("f1")}
        self.assertEqual(before, after); self.assertFalse(any(n.endswith(fa.STAGE) for n in os.listdir(fa.FOLDER_ART_DIR)))
    def test_05_describe_and_clear(self):
        self.assertEqual(fa.describe("f1")["provenance"], "custom")
        r = fa.clear("f1"); self.assertEqual(r["removed"], 3); self.assertEqual(self.files("f1"), []); self.assertEqual(fa.describe("f1")["provenance"], "generated")
    def test_06_bad_folder_id(self):
        with self.assertRaises(ValueError): fa.set_art("", png("d.png", (400, 400), (1, 1, 1)))

if __name__ == "__main__": unittest.main(verbosity=1)
