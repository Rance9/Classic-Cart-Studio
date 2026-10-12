"""Offline tests for the hero batch tooling (no network, no key, nothing submitted).
  python -m unittest tools/rom-pipeline/test_hero_batch.py"""
import os, sys, io, json, base64, unittest
HERE = os.path.dirname(os.path.abspath(__file__)); sys.path.insert(0, HERE)
import hero_batch as hb
from PIL import Image, ImageDraw
import numpy as np

def png_bytes(im):
    b = io.BytesIO(); im.save(b, "PNG"); return b.getvalue()

def scene(w=1920, h=1080, border=0, flat=False):
    """a structured synthetic scene (sky gradient, ridges, scattered shapes) or a flat card"""
    if flat: im = Image.new("RGB", (w, h), (40, 40, 40))
    else:
        rnd = np.random.default_rng(3); y = np.linspace(0, 1, h)[:, None]; a = np.zeros((h, w, 3), np.uint8); a[..., 0] = (10 + 120 * y); a[..., 1] = (12 + 60 * y); a[..., 2] = (60 + 90 * (1 - y))
        im = Image.fromarray(a); d = ImageDraw.Draw(im)
        d.polygon([(0, h), (0, int(h * 0.7)), (w // 3, int(h * 0.5)), (w // 2, int(h * 0.75)), (w, int(h * 0.6)), (w, h)], fill=(18, 14, 30))
        for _ in range(900): x0, y0 = rnd.integers(0, w), rnd.integers(0, h); d.rectangle((x0, y0, x0 + rnd.integers(3, 40), y0 + rnd.integers(3, 40)), fill=tuple(int(v) for v in rnd.integers(0, 255, 3)))
        for _ in range(20000): x0, y0 = rnd.integers(0, w), rnd.integers(0, h); d.point((int(x0), int(y0)), fill=tuple(int(v) for v in rnd.integers(0, 255, 3)))
    if border: ImageDraw.Draw(im).rectangle((0, 0, w, border), fill=(0, 0, 0)); ImageDraw.Draw(im).rectangle((0, h - border, w, h), fill=(0, 0, 0))
    return im

class HeroBatchTests(unittest.TestCase):
    def test_prompt_has_facts_style_and_no_text_rule(self):
        g = {"display_title": "Contra", "year": "1988", "publisher": "Konami", "genre": "Shooter", "players": "2"}
        p = hb.prompt_for(g, 4)
        for s in ("Contra", "1988", "Konami", "Shooter", "16:9", "no text", "upper right third", "pixel art", "STYLE references only", "Image 4 is THIS game", "Image 6 is THIS game"): self.assertIn(s, p)
        self.assertNotIn("navy sky with an orange sunset template", p)
    def test_style_references_are_the_three_approved_illustrations(self):
        refs = hb.style_refs(); self.assertEqual([n for n, _ in refs], [f"style-{g}" for g in hb.STYLE_REF_IDS]); self.assertTrue(all(len(d) > 10000 for _, d in refs))
    def test_technical_check_accepts_a_detailed_scene(self): self.assertEqual(hb.technical_check(scene()), [])
    def test_technical_check_refuses_small_flat_bordered(self):
        self.assertTrue(any("too small" in x for x in hb.technical_check(scene(640, 360))))
        self.assertTrue(any("flat" in x for x in hb.technical_check(scene(flat=True))))
        self.assertTrue(any("border" in x for x in hb.technical_check(scene(border=40))))
        self.assertTrue(any("aspect" in x for x in hb.technical_check(scene(1600, 1200))))
    def test_extract_image_both_casings_and_errors(self):
        data = base64.b64encode(png_bytes(scene(64, 36))).decode()
        k, img, err = hb.extract_image({"key": "g1", "response": {"candidates": [{"content": {"parts": [{"text": "x"}, {"inlineData": {"mimeType": "image/png", "data": data}}]}}]}})
        self.assertEqual((k, err), ("g1", None)); self.assertTrue(img.startswith(b"\x89PNG"))
        k, img, err = hb.extract_image({"key": "g2", "response": {"candidates": [{"content": {"parts": [{"inline_data": {"mime_type": "image/png", "data": data}}]}}]}})
        self.assertIsNotNone(img)
        k, img, err = hb.extract_image({"key": "g3", "error": {"code": 429, "message": "quota"}})
        self.assertIsNone(img); self.assertIn("quota", str(err))
    def test_submit_refuses_without_confirm_and_without_key(self):
        import argparse
        with self.assertRaises(SystemExit) as cm: hb.cmd_submit(argparse.Namespace(batch=None, confirm=False))
        self.assertIn("PAID", str(cm.exception))
        os.environ.pop("GEMINI_API_KEY", None); os.environ.pop("GEMINI_API_KEY_FILE", None)
        with self.assertRaises(SystemExit) as cm: hb.client()
        self.assertIn("GEMINI_API_KEY is not set", str(cm.exception))
    def test_request_line_shape(self):
        m = hb.load_manifest(); name = hb.latest(m) if m["batches"] else None
        if not name: self.skipTest("no prepared batch")
        line = open(os.path.join(hb.BATCH_DIR, name, "requests.jsonl"), encoding="utf-8").readline(); r = json.loads(line)
        self.assertIn("key", r); parts = r["request"]["contents"][0]["parts"]; self.assertTrue(any("text" in p for p in parts)); self.assertTrue(any("inline_data" in p for p in parts))
        self.assertEqual(r["request"]["generation_config"]["image_config"]["aspect_ratio"], "16:9")
        self.assertNotIn("GEMINI", line.upper().replace("GEMINI-3-PRO-IMAGE", ""))

if __name__ == "__main__": unittest.main()
