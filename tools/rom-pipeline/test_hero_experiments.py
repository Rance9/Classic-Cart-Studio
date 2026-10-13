"""Offline tests for the experiment campaign tooling: unique ids, request-to-experiment mapping, ceilings, no overwrite, no key.
  python -m unittest tools/rom-pipeline/test_hero_experiments.py"""
import os, sys, io, json, base64, tempfile, shutil, unittest, argparse
HERE = os.path.dirname(os.path.abspath(__file__)); sys.path.insert(0, HERE)
import hero_experiments as hx, hero_batch as hb
from PIL import Image

class CampaignTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.tmp = tempfile.mkdtemp(prefix="ccs-hero-exp-"); cls.orig_dir, cls.orig_campaign = hx.DIR, hx.CAMPAIGN
        hx.DIR = cls.tmp; hx.CAMPAIGN = os.path.join(cls.tmp, "campaign.json")
    @classmethod
    def tearDownClass(cls): hx.DIR, hx.CAMPAIGN = cls.orig_dir, cls.orig_campaign; shutil.rmtree(cls.tmp, ignore_errors=True)
    def test_01_plan_build_validate_wave1(self):
        hx.cmd_plan(argparse.Namespace(wave=1, from_json=None)); c = hx.load(); self.assertEqual(len(c["experiments"]), 12); self.assertEqual(len(set(c["experiments"])), 12)
        hx.cmd_build(argparse.Namespace(wave=1)); self.assertEqual(hx.cmd_validate(argparse.Namespace(wave=1)), 0)
        c = hx.load(); w = c["waves"]["1"]; self.assertEqual(w["requests"], 12); self.assertLess(w["est_usd"], 2.0); self.assertGreater(w["est_usd"], 1.0)
        lines = [json.loads(l) for l in open(os.path.join(self.tmp, "waves", "1", "requests.jsonl"), encoding="utf-8") if l.strip()]
        self.assertEqual([l["key"] for l in lines], w["ids"])
        e = c["experiments"]["w1-D1a"]; self.assertEqual(e["method"], "edit"); self.assertEqual(e["refs"][0], "source-smoke3"); self.assertEqual(len(e["refs"]), 4)
        e = c["experiments"]["w1-A1a"]; self.assertEqual(e["refs"], ["style-The Legend of Zelda", "identity-cover", "identity-title", "identity-gameplay"])
        for l in lines:
            t = l["request"]["contents"][0]["parts"][0]["text"].lower(); self.assertNotIn("1 to 3", t); self.assertNotIn("dither", t)
            self.assertNotIn("use checker", t); self.assertNotIn("with checker", t); self.assertIn("no ground strip or checkerboard band", t)   # the only mention is a prohibition
        self.assertFalse(any((os.environ.get("GEMINI_API_KEY") or "NOKEY") in l for l in open(os.path.join(self.tmp, "waves", "1", "requests.jsonl"), encoding="utf-8")))
    def test_02_ids_never_reused(self):
        with self.assertRaises(SystemExit): hx.cmd_plan(argparse.Namespace(wave=1, from_json=None))
    def test_03_ceilings_refuse(self):
        c = hx.load(); c["submitted_requests"] = 30; hx.save(c); self.assertEqual(hx.cmd_validate(argparse.Namespace(wave=1)), 1)
        c["submitted_requests"] = 0; c["estimated_usd"] = 6.0; hx.save(c); self.assertEqual(hx.cmd_validate(argparse.Namespace(wave=1)), 1)
        c["estimated_usd"] = 0.0; hx.save(c); self.assertEqual(hx.cmd_validate(argparse.Namespace(wave=1)), 0)
        with self.assertRaises(SystemExit) as cm: hx.cmd_submit(argparse.Namespace(wave=1, confirm=False))
        self.assertIn("PAID", str(cm.exception))
    def test_04_fetch_local_maps_each_result_to_its_experiment_without_overwriting(self):
        # fake responses: twelve distinct images keyed by experiment id; import goes to the real hero store under unique tags, so use a temp OUT
        real_out = hx.hero_art.OUT; tmp_out = os.path.join(self.tmp, "hero"); os.makedirs(os.path.join(tmp_out, "originals"), exist_ok=True)
        for g in hb.STYLE_REF_IDS: shutil.copy2(hx.ORIG[g], os.path.join(tmp_out, "originals", f"{g}-owner-poc.png"))
        hx.hero_art.OUT = tmp_out; hb.hero_art.OUT = tmp_out
        try:
            c = hx.load(); ids = c["waves"]["1"]["ids"]; lines = []
            for i, eid in enumerate(ids):
                im = Image.new("RGB", (1920, 1080), (i * 20, 90, 200 - i * 10)); d = __import__("PIL.ImageDraw", fromlist=["Draw"]).Draw(im)
                for k in range(0, 1920, 7): d.line((k, 0, k, 1080), fill=((k * 13) % 255, 30, 60))
                b = io.BytesIO(); im.save(b, "PNG"); data = base64.b64encode(b.getvalue()).decode()
                lines.append(json.dumps({"key": eid, "response": {"modelVersion": "fake", "usageMetadata": {"promptTokenCount": 3000, "candidatesTokenCount": 1120}, "candidates": [{"finishReason": "STOP", "content": {"parts": [{"inlineData": {"mimeType": "image/png", "data": data}}]}}]}}))
            os.makedirs(os.path.join(self.tmp, "waves", "1"), exist_ok=True); open(os.path.join(self.tmp, "waves", "1", "responses.jsonl"), "w", encoding="utf-8").write("\n".join(lines))
            hx.cmd_fetch(argparse.Namespace(wave=1, local=True)); c = hx.load()
            self.assertEqual(sum(1 for e in c["experiments"].values() if e["status"] == "candidate"), 12)
            reg = json.load(open(os.path.join(tmp_out, "index.json"), encoding="utf-8"))["games"][hx.GAME]; self.assertEqual(set(reg["candidates"]), set(ids))
            files = os.listdir(os.path.join(tmp_out, "candidates")); self.assertEqual(len(files), 12)
            for eid in ids: self.assertTrue(os.path.exists(os.path.join(self.tmp, "waves", "1", eid, "result.png"))); self.assertEqual(c["experiments"][eid]["usage"]["promptTokenCount"], 3000)
            self.assertTrue(os.path.exists(os.path.join(self.tmp, "review", "01-contact-sheet-all-results.png")))
        finally: hx.hero_art.OUT = real_out; hb.hero_art.OUT = real_out

if __name__ == "__main__": unittest.main()
