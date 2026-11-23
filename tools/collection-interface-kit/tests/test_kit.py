#!/usr/bin/env python3
"""Tests of the CCS Collection Interface kit: the schema, the examples, the conformance checker and the reference reader.
Copyright (c) 2026 Rance9. MIT License (see LICENSE).

  python3 tests/test_kit.py        exit 0: every test passed

The reader's tests run it with a POSIX shell and every awk found (awk, gawk, mawk, busybox awk; on Windows through WSL) and compare its
output with the same answers worked out here from the JSON; with no shell they are skipped (and say so). CCS_CI_AWK adds another awk."""
from __future__ import annotations
import glob, json, os, shutil, subprocess, sys, tempfile, unittest
KIT = os.path.normpath(os.path.join(os.path.dirname(os.path.abspath(__file__)), ".."))
sys.path.insert(0, os.path.join(KIT, "checker"))
import ccs_ci_check as C   # noqa: E402

EXAMPLES = sorted(glob.glob(os.path.join(KIT, "examples", "*.json")))
INVALID = {   # each invalid example and a part of the problem it must be reported for
    "wrong-version.json": "$.version: must be 1", "generation-zero.json": "$.generation: below 1",
    "path-traversal.json": "may not name . or ..", "absolute-path.json": "entry.path: does not match",
    "unlisted-base.json": "is not listed in bases", "missing-game.json": "is not in games",
    "parent-after-folder.json": "must be a folder listed before it", "command.json": "are not part of the interface",
    "pc-path.json": "a computer's own path", "embedded-file.json": "an embedded file",
    "repeated-name.json": "appears twice", "not-json.json": "not JSON"}

def read(p: str, mode: str = "r") -> str | bytes:
    with open(p, mode, **({} if "b" in mode else {"encoding": "utf-8", "errors": "replace"})) as f: return f.read()

def wsl_path(p: str) -> str:
    p = os.path.abspath(p); return "/mnt/" + p[0].lower() + p[2:].replace("\\", "/") if os.name == "nt" else p

def shell(script: str, d: str) -> subprocess.CompletedProcess | None:
    """a POSIX sh script (written to a file in d); None when no shell is available"""
    f = os.path.join(d, "run.sh")
    with open(f, "w", newline="\n") as fh: fh.write(script)
    cmd = (["wsl.exe", "--", "sh", wsl_path(f)] if os.name == "nt" else ["sh", f])
    try: return subprocess.run(cmd, capture_output=True, timeout=300)
    except (OSError, subprocess.TimeoutExpired): return None

class Schema(unittest.TestCase):
    def test_01_schema_and_examples(self):
        with open(C.SCHEMA_PATH, encoding="utf-8") as f: s = json.load(f)
        self.assertEqual(s["properties"]["schema"]["const"], "ccs-collection-interface"); self.assertEqual(s["properties"]["version"]["const"], 1)
        self.assertEqual(set(s["required"]), {"schema", "version", "generation", "bases", "collections", "games"})
        self.assertGreaterEqual(len(EXAMPLES), 2)
        for e in EXAMPLES:
            with self.subTest(example=os.path.basename(e)):
                with open(e, encoding="utf-8") as f: doc = json.load(f)
                self.assertEqual(C.validate(doc, s, s), [], "the example matches the schema")
                self.assertEqual(C.check(e), [], "and every rule beyond it")
    def test_02_unknown_fields_are_allowed(self):
        with open(os.path.join(KIT, "examples", "minimal.json"), encoding="utf-8") as f: doc = json.load(f)
        doc["future_field"] = {"anything": [1, 2]}; doc["games"][next(iter(doc["games"]))]["rating"] = 5; doc["capabilities"] = ["hakchi-entries", "something-new"]
        with tempfile.TemporaryDirectory() as d:
            p = os.path.join(d, "x.json")
            with open(p, "w", encoding="utf-8") as f: json.dump(doc, f)
            self.assertEqual(C.check(p), [])

class Checker(unittest.TestCase):
    def test_03_every_invalid_example_is_reported(self):
        found = {os.path.basename(p) for p in glob.glob(os.path.join(KIT, "examples", "invalid", "*.json"))}
        self.assertEqual(found, set(INVALID), "each invalid example has its expected problem here")
        for name, part in INVALID.items():
            with self.subTest(example=name):
                p = os.path.join(KIT, "examples", "invalid", name)
                try: probs = C.check(p)
                except C.Unreadable as e: probs = [str(e)]
                self.assertTrue(probs, "reported"); self.assertTrue(any(part in x for x in probs), probs)
    def test_04_command_line(self):
        ok = subprocess.run([sys.executable, os.path.join(KIT, "checker", "ccs_ci_check.py")] + EXAMPLES, capture_output=True, text=True)
        self.assertEqual(ok.returncode, 0, ok.stdout + ok.stderr); self.assertIn("conforms to CCS Collection Interface v1", ok.stdout)
        bad = subprocess.run([sys.executable, os.path.join(KIT, "checker", "ccs_ci_check.py"), os.path.join(KIT, "examples", "invalid", "command.json")], capture_output=True, text=True)
        self.assertEqual(bad.returncode, 1); nj = subprocess.run([sys.executable, os.path.join(KIT, "checker", "ccs_ci_check.py"), os.path.join(KIT, "examples", "invalid", "not-json.json")], capture_output=True, text=True)
        self.assertEqual(nj.returncode, 2)

def expected(doc: dict, cmd: list[str]) -> str:
    """the reader's answer, worked out here from the JSON"""
    def esc(v): return str(v).replace("\\", "\\\\").replace("\n", "\\n").replace("\t", "\\t").replace("\r", "\\r")
    def ref(r):
        import re
        if not isinstance(r, dict): return "-"
        b = doc["bases"].get(r.get("base")); p = r.get("path")
        if not isinstance(b, str) or not isinstance(p, str) or not re.match(r"^[A-Za-z0-9_][A-Za-z0-9._-]*(/[A-Za-z0-9_][A-Za-z0-9._-]*)*$", p) or any(x in (".", "..") for x in p.split("/")): return "-"
        return b + "/" + p
    col = lambda i: next((c for c in doc["collections"] if c["id"] == i), None)
    if cmd[0] in ("check", "generation"): return f"{doc['generation']}\n"
    if cmd[0] == "collections": return "".join(f"{c['id']}\t{esc(c['name'])}\n" for c in doc["collections"])
    c = col(cmd[1]) if len(cmd) > 1 else None
    if cmd[0] == "games": return "".join(f"{g}\t{esc(doc['games'][g]['title'])}\t{ref(doc['games'][g].get('entry'))}\n" for g in c["games"])
    if cmd[0] == "folders": return "".join(f"{f['id']}\t{f['parent'] or '-'}\t{esc(f['name'])}\t{ref(f.get('entry'))}\n" for f in c.get("folders") or [])
    if cmd[0] == "folder-games": return "".join(g + "\n" for g in next(f for f in c["folders"] if f["id"] == cmd[2])["games"])
    if cmd[0] == "favorites": return "".join(g + "\n" for g in c.get("favorites") or [])
    if cmd[0] == "game":
        g = doc["games"][cmd[1]]; art = g.get("art") or {}
        return "".join(f"{k}\t{v}\n" for k, v in (("title", esc(g["title"])), ("status", g.get("status", "-")), ("reason", esc(g.get("reason", "-"))), ("entry", ref(g.get("entry"))),
                                                   ("cover", ref(art.get("cover"))), ("thumbnail", ref(art.get("thumbnail")))))
    raise ValueError(cmd)

class Reader(unittest.TestCase):
    AWKS = ("awk", "gawk", "mawk", "busybox awk") + ((os.environ["CCS_CI_AWK"],) if os.environ.get("CCS_CI_AWK") else ())
    @classmethod
    def setUpClass(cls):
        cls.d = tempfile.mkdtemp(prefix="ccs-ci-kit-")
        r = shell("for a in awk gawk mawk busybox; do command -v $a >/dev/null 2>&1 && echo $a; done; exit 0\n", cls.d)
        if r is None or r.returncode != 0: raise unittest.SkipTest("no POSIX shell to run the reader")
        have = r.stdout.decode().split()
        cls.awks = [a for a in cls.AWKS if a.split()[0] in have]
        # an index with names that need care: quotes, a tab, a backslash, a newline, non-ASCII text, an escaped character, a name with a dot
        doc = json.loads(read(os.path.join(KIT, "examples", "folders.json")))
        g = next(iter(doc["games"])); doc["games"][g]["title"] = 'Tab\there "quoted" back\\slash\nnew line \u00e9 \u263a \U0001f600'
        doc["games"][g]["x.y"] = "a name with a dot"; doc["games"]["nes-1a2b3c4d5e6f708192a3b4c5d6e7f809"]["art"]["thumbnail"] = {"base": "entries", "path": "../../etc/shadow"}
        cls.tricky = os.path.join(cls.d, "tricky.json")
        with open(cls.tricky, "w", encoding="utf-8") as f: json.dump(doc, f, ensure_ascii=True, indent=None)
    @classmethod
    def tearDownClass(cls): shutil.rmtree(cls.d, ignore_errors=True)
    def run_reader(self, awk: str, index: str, *args: str) -> subprocess.CompletedProcess:
        a = " ".join("'" + x + "'" for x in args)
        return shell(f"CCS_CI_AWK='{awk}' sh '{wsl_path(os.path.join(KIT, 'reader', 'ccs-ci'))}' -f '{wsl_path(index)}' {a}\n", self.d)
    def test_05_reader_answers_on_the_examples(self):
        self.assertTrue(self.awks, "at least one awk")
        for awk in self.awks:
            for path in EXAMPLES + [self.tricky]:
                doc = json.loads(read(path)); c0 = doc["collections"][0]
                cmds = [["check"], ["collections"], ["games", c0["id"]], ["folders", c0["id"]], ["favorites", c0["id"]]] + [["game", g] for g in doc["games"]]
                cmds += [["folder-games", c0["id"], f["id"]] for f in c0.get("folders") or []]
                for cmd in cmds:
                    with self.subTest(awk=awk, index=os.path.basename(path), cmd=cmd):
                        r = self.run_reader(awk, path, *cmd)
                        self.assertEqual(r.returncode, 0, r.stderr.decode(errors="replace")); self.assertEqual(r.stdout.decode("utf-8"), expected(doc, cmd))
    def test_06_reader_refuses_what_it_must(self):
        for awk in self.awks:
            with self.subTest(awk=awk):
                r = self.run_reader(awk, os.path.join(KIT, "examples", "invalid", "not-json.json"), "check"); self.assertEqual(r.returncode, 2, "not JSON: exit 2")
                r = self.run_reader(awk, os.path.join(KIT, "examples", "invalid", "wrong-version.json"), "collections"); self.assertEqual(r.returncode, 1, "not v1: nothing used")
                self.assertEqual(r.stdout, b"")
                r = self.run_reader(awk, self.tricky, "game", "nes-1a2b3c4d5e6f708192a3b4c5d6e7f809"); self.assertIn(b"thumbnail\t-\n", r.stdout, "a path naming .. is never used")
                r = self.run_reader(awk, os.path.join(KIT, "examples", "minimal.json"), "games", "no-such"); self.assertEqual((r.returncode, r.stdout), (1, b""))
                r = self.run_reader(awk, os.path.join(KIT, "examples", "minimal.json"), "flat"); self.assertIn(b"schema\tstring\tccs-collection-interface\n", r.stdout)
                deep = os.path.join(self.d, "deep.json")
                with open(deep, "w") as f: f.write("[" * 70 + "]" * 70)
                r = self.run_reader(awk, deep, "check"); self.assertEqual(r.returncode, 2, "nested deeper than 64 levels: refused")

class Boundary(unittest.TestCase):
    def test_07_the_kit_stands_alone(self):
        """nothing in the kit imports, names or copies Classic Cart Studio's own code; every source file says MIT"""
        lic = read(os.path.join(KIT, "LICENSE")); self.assertTrue(lic.startswith("MIT License\n\nCopyright (c) 2026 Rance9"))
        for p in glob.glob(os.path.join(KIT, "**", "*"), recursive=True):
            if os.path.isdir(p) or "__pycache__" in p or os.path.abspath(p) == os.path.abspath(__file__): continue
            t = read(p); rel = os.path.relpath(p, KIT)
            self.assertNotRegex(t, r"GPL-3|GNU General Public|console_engine|native_home|companion|hakchi_publish|ccs_install|CcsBackup", rel)
            if rel.endswith((".py", ".awk")) or os.path.basename(p) == "ccs-ci": self.assertIn("MIT License", t, rel)

if __name__ == "__main__": unittest.main(verbosity=1)
