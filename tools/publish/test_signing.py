"""signing.py and the app's updater (app_update._signed_by) without credentials: verification against a file signed by someone else (the
running Python's own python3X.dll, signed and timestamped by the Python Software Foundation), an unsigned file, a tampered copy and the
wrong publisher; the unsigned mode is honest (exit 3, refused when a signed build is required); the subject parser. Both helpers are
also run as real child processes of a PowerShell 7 parent and of a Windows PowerShell 5.1 parent, each with the environment that shell
gives its children (Codex v1p F2: PowerShell 7's module path made both fail), on the updater's own temporary name (.part); and a check
that cannot run fails closed. Windows only (Get-AuthenticodeSignature).
  python tools/publish/test_signing.py"""
from __future__ import annotations
import glob, json, os, shutil, subprocess, sys, tempfile, unittest
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import signing as S
HERE = os.path.dirname(os.path.abspath(__file__))
COMPANION = os.path.normpath(os.path.join(HERE, "..", "companion"))
SIGNED = (glob.glob(os.path.join(sys.base_prefix, "python3[0-9]*.dll")) or [""])[0]
PWSH = shutil.which("pwsh")
# The child: both helpers on each file, as the build and the installed app call them. Its inputs come in environment variables.
CHILD = r"""import json, os, sys
sys.path[:0] = [os.environ["CCS_T_PUBLISH"], os.environ["CCS_T_COMPANION"]]
import signing, app_update
out = {"psmodulepath": os.environ.get("PSModulePath", ""), "files": {}}
names = json.loads(os.environ["CCS_T_NAMES"])
for p in json.loads(os.environ["CCS_T_FILES"]):
    try: signing.verify([p], names); b = "pass"
    except SystemExit as e: b = "refused: " + str(e)[:160]
    out["files"][os.path.basename(p)] = {"build": b, "update": app_update._signed_by(p, names)}
print(json.dumps(out))
"""

@unittest.skipUnless(os.name == "nt" and SIGNED, "Windows with a signed python3X.dll")
class T(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.d = tempfile.mkdtemp(prefix="ccs-signing-test-")
        cls.unsigned = os.path.join(cls.d, "unsigned.exe"); shutil.copy(os.path.join(HERE, "signing.py"), cls.unsigned)
        cls.tampered = os.path.join(cls.d, "tampered.dll"); b = bytearray(open(SIGNED, "rb").read()); b[len(b) // 2] ^= 0xFF; open(cls.tampered, "wb").write(b)
        cls.who = sorted(S._subject_names(S.inspect([SIGNED])[0]["subject"]))
    @classmethod
    def tearDownClass(cls): shutil.rmtree(cls.d, ignore_errors=True)
    def test_1_a_signed_timestamped_file_by_the_expected_publisher_passes(self):
        self.assertTrue(self.who, "the reference file has a signer")
        rec = S.verify([SIGNED], self.who)[0]; self.assertTrue(rec["ok"] and rec["timestamped"] and rec["status"] == "Valid", rec)
    def test_2_another_publisher_is_refused(self):
        with self.assertRaises(SystemExit) as e: S.verify([SIGNED], ["Classic Cart Studio Test Publisher"])
        self.assertIn("publisher", str(e.exception))
    def test_3_unsigned_and_tampered_files_are_refused(self):
        for p, word in ((self.unsigned, "status"), (self.tampered, "status")):
            with self.assertRaises(SystemExit) as e: S.verify([p], self.who)
            self.assertIn(word, str(e.exception), p)
    def test_4_unsigned_mode_is_honest_and_refused_when_a_signed_build_is_required(self):
        env = {k: v for k, v in os.environ.items() if not k.startswith("CCS_SIGN") and k != "CCS_REQUIRE_SIGNED"}
        r = subprocess.run([sys.executable, os.path.join(HERE, "signing.py"), "sign", self.unsigned], capture_output=True, text=True, env=env)
        self.assertEqual(r.returncode, S.NOT_CONFIGURED); self.assertIn("UNSIGNED", r.stdout)
        r = subprocess.run([sys.executable, os.path.join(HERE, "signing.py"), "sign", self.unsigned], capture_output=True, text=True, env={**env, "CCS_REQUIRE_SIGNED": "1"})
        self.assertNotIn(r.returncode, (0, S.NOT_CONFIGURED)); self.assertIn("must be signed", r.stdout + r.stderr)
        r = subprocess.run([sys.executable, os.path.join(HERE, "signing.py"), "iscc-args"], capture_output=True, text=True, env=env)
        self.assertEqual(r.stdout.strip(), "[]")
    def test_5_subject_names_are_read_from_cn_and_o(self):
        self.assertEqual(S._subject_names('CN="Rance9, LLC", O="Rance9, LLC", L=Omaha, S=Nebraska, C=US'), {"Rance9, LLC"})
        self.assertEqual(S._subject_names("CN=Jane Doe, O=Jane Doe, C=US"), {"Jane Doe"})

    def _from(self, shell):
        """both helpers in a Python child of shell (its own children's environment), on the signed file under the updater's .part name,
        an unsigned file and a tampered copy"""
        part = os.path.join(self.d, "ClassicCartStudio-Setup-9.9.9.exe.part"); shutil.copy(SIGNED, part)
        child = os.path.join(self.d, "child.py")
        with open(child, "w", encoding="utf-8") as f: f.write(CHILD)
        env = {k: v for k, v in os.environ.items() if k.upper() != "PSMODULEPATH"}   # the shell sets its own, as it does for anything it starts
        env.update(CCS_T_PY=sys.executable, CCS_T_CHILD=child, CCS_T_PUBLISH=HERE, CCS_T_COMPANION=COMPANION,
                   CCS_T_NAMES=json.dumps(self.who), CCS_T_FILES=json.dumps([part, self.unsigned, self.tampered]))
        r = subprocess.run([shell, "-NoProfile", "-NonInteractive", "-Command", "& $env:CCS_T_PY $env:CCS_T_CHILD"], capture_output=True, text=True, env=env, timeout=300)
        self.assertEqual(r.returncode, 0, r.stderr[-800:])
        return json.loads(r.stdout.strip().splitlines()[-1])

    def _expect(self, got, label):
        f = got["files"]
        self.assertEqual(f["ClassicCartStudio-Setup-9.9.9.exe.part"], {"build": "pass", "update": True}, f"{label}: the signed file passes both")
        for n in ("unsigned.exe", "tampered.dll"):
            self.assertTrue(f[n]["build"].startswith("refused") and f[n]["update"] is False, f"{label}: {n} is refused by both ({f[n]})")

    @unittest.skipUnless(PWSH, "PowerShell 7 (pwsh) is not installed here")
    def test_6_from_a_powershell_7_parent_both_helpers_verify(self):
        got = self._from(PWSH)
        self.assertIn("\\powershell\\7", got["psmodulepath"].lower(), "the child really inherited PowerShell 7's module path")
        self._expect(got, "PowerShell 7 parent")

    def test_7_from_a_windows_powershell_parent_both_helpers_verify(self):
        winps = S.A.windows_powershell()[0]
        got = self._from(winps)
        self.assertNotIn("\\powershell\\7", got["psmodulepath"].lower())
        self._expect(got, "Windows PowerShell parent")

    def test_8_a_check_that_cannot_run_fails_closed(self):
        sys.path.insert(0, COMPANION)
        import app_update, authenticode
        saved = (S.A.windows_powershell, authenticode.windows_powershell)
        broken = lambda: (os.path.join(self.d, "no-such-powershell.exe"), dict(os.environ))
        try:
            S.A.windows_powershell = authenticode.windows_powershell = broken
            with self.assertRaises(SystemExit) as e: S.verify([SIGNED], self.who)
            self.assertIn("signing:", str(e.exception))
            self.assertIs(app_update._signed_by(SIGNED, self.who), False)
        finally:
            S.A.windows_powershell, authenticode.windows_powershell = saved

if __name__ == "__main__":
    r = unittest.main(exit=False, verbosity=2).result; print(f"RESULT {r.testsRun - len(r.failures) - len(r.errors)} / {r.testsRun} (skipped {len(r.skipped)})"); sys.exit(0 if r.wasSuccessful() else 1)
