"""The Installation Terms: one source, the installer's and release's copy and the Help chapter (legal_terms.py --check), and the setup page that
shows them (installer.iss); the setup's separate optional usage page and one account of what continues when usage reporting is off (the
setup's "What is collected?", Privacy and Help). Checked from the files alone. No installer is built or run here.   python tools/publish/test_legal_terms.py"""
from __future__ import annotations
import os, re, shutil, subprocess, sys, tempfile, unittest
HERE = os.path.dirname(os.path.abspath(__file__)); REPO = os.path.normpath(os.path.join(HERE, "..", ".."))
sys.path.insert(0, HERE); import legal_terms as LT
ISS = os.path.join(REPO, "tools", "companion", "installer.iss")
AGREE = "I agree to the Installation Terms and acknowledge the important risks. My GPL and non-waivable legal rights remain unchanged."
NOTE = "This does not restrict your open-source rights or authorize console changes, advertising tracking or purchases."
INTRO = "Classic Cart Studio is independent, open-source software. Please review its licenses and the important software, backup and console risks before continuing. Your GPL and non-waivable legal rights remain unchanged."
def wr(p, b):
    with open(p, "wb") as f: f.write(b)
def rd(p):
    with open(p, encoding="utf-8-sig") as f: return f.read()

class T(unittest.TestCase):
    def test_01_the_three_copies_are_the_source(self):
        r = subprocess.run([sys.executable, os.path.join(HERE, "legal_terms.py"), "--check"], capture_output=True, text=True)
        self.assertEqual(r.returncode, 0, r.stdout + r.stderr); self.assertIn("RESULT OK", r.stdout)
    def test_02_a_changed_copy_is_found(self):   # drift in any copy fails the check (on a temporary copy of the files, never the real ones)
        d = tempfile.mkdtemp(prefix="ccs-terms-")
        try:
            for rel in ("tools/publish/legal_terms.py", "tools/publish/assets/legal/INSTALLATION-TERMS.md", "tools/publish/assets/legal/INSTALLATION-TERMS.txt",
                        "tools/publish/assets/legal/terms-manifest.json", "tools/rom-pipeline/studio/guide.html"):
                os.makedirs(os.path.dirname(os.path.join(d, rel)), exist_ok=True); shutil.copy2(os.path.join(REPO, rel), os.path.join(d, rel))
            run = lambda: subprocess.run([sys.executable, os.path.join(d, "tools", "publish", "legal_terms.py"), "--check"], capture_output=True, text=True)
            self.assertEqual(run().returncode, 0)
            t = os.path.join(d, "tools", "publish", "assets", "legal", "INSTALLATION-TERMS.txt")
            with open(t, "rb") as f: b = f.read()
            wr(t, b.replace("US $100".encode(), "US $10".encode())); r = run(); self.assertEqual(r.returncode, 1, r.stdout); self.assertIn("txt", r.stdout)
            wr(t, b); g = os.path.join(d, "tools", "rom-pipeline", "studio", "guide.html"); s = rd(g)
            wr(g, s.replace("Nebraska law governs", "Delaware law governs").encode("utf-8")); r = run(); self.assertEqual(r.returncode, 1, r.stdout); self.assertIn("Help", r.stdout)
        finally: shutil.rmtree(d, ignore_errors=True)
    def test_03_the_shipped_terms_are_adopted_text(self):
        md = rd(LT.SRC)
        for gone in ("Draft 1", "Not yet adopted", "End of draft", "requires legal and licensing review before adoption"): self.assertNotIn(gone, md)
        self.assertIn("version 1.0", md); self.assertIn("**Rance9**", md); self.assertIn("Nebraska law governs", md)
        self.assertIn("GNU General Public License, version 3 or, at your option, any later version", md)
        for n in range(1, 21): self.assertRegex(md, rf"\n## {n}\. ")
    def test_04_the_setup_page(self):
        s = rd(ISS)
        self.assertIn(AGREE, s); self.assertIn(NOTE, s); self.assertIn(INTRO, s)
        self.assertIn("CreateCustomPage(wpWelcome, 'Licenses and Important Risks'", s)
        self.assertIn("AGREE_LABEL = '" + AGREE + "';", s); self.assertIn("TermsAgree.AddCheckBox(AGREE_LABEL, '', 0, False,", s)   # unchecked
        for proc in ("procedure InitializeWizard;", "procedure CurPageChanged(CurPageID: Integer);", "function NextButtonClick(CurPageID: Integer): Boolean;"): self.assertEqual(s.count(proc), 1, proc)
        self.assertIn("if WizardSilent then Exit;", s); self.assertIn("WizardForm.NextButton.Enabled := TermsAgree.Checked[0];", s)
        for src in ('Source: "..\\publish\\assets\\legal\\INSTALLATION-TERMS.txt"; DestDir: "{app}\\legal"', 'Source: "..\\..\\LICENSE"; DestDir: "{app}\\legal"', 'Source: "..\\..\\THIRD_PARTY_NOTICES.md"; DestDir: "{app}\\legal"'): self.assertIn(src, s)
        self.assertNotIn("RegWriteStringValue", s.split("[Code]", 1)[1].split("procedure RemoveOwnValue")[0], "no agreement receipt is written")
        self.assertIn("PrivilegesRequired=lowest", s); self.assertIn("AppId={{7E4C1C3A-6B0C-4B59-9F3D-2A1D5C0E9B11}", s)
    def test_05_help_reaches_the_terms(self):
        g = rd(LT.GUIDE); j = rd(os.path.join(REPO, "tools", "rom-pipeline", "studio", "studio.js"))
        self.assertIn('<a href="#" data-guide="terms">Installation Terms (EULA)</a>', g); self.assertIn('data-guide-to="g-privacy"', g); self.assertIn('id="g-privacy"', g)
        self.assertIn("['legal','Legal, Privacy & Disclaimers'],['terms','Installation Terms (EULA)'],['credits','Credits']", j)
        self.assertIn("Reading them here is not agreeing to them", g)
        self.assertNotRegex(g, r"There is no account, no analytics and no telemetry")
    def test_06_the_optional_usage_page(self):   # its own page after the terms; the default, the kept choice, silent setup and the saved file
        s = rd(ISS); code = s.split("[Code]", 1)[1]
        self.assertIn("CreateCustomPage(TermsPage.ID, 'Optional Usage and Error Reports'", s)
        self.assertIn("USAGE_LABEL = '" + USAGE_LABEL + "';", s); self.assertIn("UsageBox.AddCheckBox(USAGE_LABEL, '', 0, False,", s)   # (k3f7, K3F-F3) unchecked: only the person turns it on
        self.assertIn("Desc.Caption := '" + USAGE_DESC + "';", s); self.assertIn("'&What is collected?'", s); self.assertIn("ExtractTemporaryFile('ccs-usage.txt')", s)
        self.assertIn("in the Studio menu, under Usage Reporting", USAGE_DESC)   # where the choice really is (build_html.py's Studio menu)
        self.assertIn('_mi("mUsage", "📊", "Usage Reporting…"', rd(os.path.join(REPO, "tools", "rom-pipeline", "build_html.py")))
        # (k3f7, K3F-F3) Windows' Country or region and time zone are settings, not where the setup runs or which rule applies: none is read
        for gone in ("GetUserGeoID", "GEOID_UNITED_STATES", "TimeZoneKeyName", "UsUsageDefault"): self.assertNotIn(gone, s)
        for lang in ("GetUILanguage", "ActiveLanguage", "LangID", "LCID"): self.assertNotIn(lang, code, "the default never comes from the display language")
        for proc in ("function ShouldSkipPage(PageID: Integer): Boolean;", "procedure CurStepChanged(CurStep: TSetupStep);", "  CreateUsagePage;"): self.assertEqual(s.count(proc), 1, proc)
        self.assertIn("Result := (PageID = UsagePage.ID) and (UsageStored <> '');", s)   # a choice already kept: no page, nothing written
        self.assertIn("if (CurStep = ssPostInstall) and (not WizardSilent) and (UsageStored = '') then begin", s)
        relay = rd(os.path.join(REPO, "tools", "companion", "metrics_relay.py"))
        self.assertIn('CHOICE_FILE = "usage-reporting.json"; CHOICE_SCHEMA = "ccs-usage-choice/1"', relay); self.assertIn("USAGE_SCHEMA = 'ccs-usage-choice/1';", s)
        for c in ("on", "off"): self.assertIn("'\\usage-reporting.json', '{\"schema\":\"' + USAGE_SCHEMA + '\",\"choice\":\"" + c + "\",\"source\":\"installer\"}'", s)
        self.assertIn("ExpandConstant('{localappdata}\\ClassicCartStudio')", s); self.assertIn("ExpandConstant('{localappdata}\\NESClassicCollectionStudio')", s)
        for net in ("DownloadTemporaryFile", "CreateOleObject", "WinHttp", "http"): self.assertNotIn(net, code, "the setup sends nothing")
        for src in ('Source: "..\\publish\\assets\\legal\\USAGE-REPORTS.txt"; DestDir: "{app}\\legal"', 'Source: "..\\publish\\assets\\legal\\USAGE-REPORTS.txt"; DestName: "ccs-usage.txt"; Flags: dontcopy'): self.assertIn(src, s)
    def test_07_one_account_of_what_continues(self):   # the setup's "What is collected?", Privacy and Help say the same three things
        u = rd(USAGE_TXT); p = rd(os.path.join(REPO, "PRIVACY.md")); g = rd(LT.GUIDE)
        for doc, name in ((u, "USAGE-REPORTS.txt"), (p, "PRIVACY.md"), (g, "guide.html")):
            for row in ROWS: self.assertIn(row, doc, name)
        for fact in ("metrics.classiccartstudio.com", "Workers Analytics Engine", "three months", "This setup itself sends nothing", "Studio menu under Usage Reporting"): self.assertIn(fact, u)
        self.assertIn("The box starts unchecked: nothing is sent unless you check it.", p)   # (k3f7) and nowhere the old US rule
        for rel in ("PRIVACY.md", "README.md", "tools/rom-pipeline/studio/guide.html", "tools/publish/assets/legal/USAGE-REPORTS.txt"):
            t = rd(os.path.join(REPO, rel)); self.assertNotIn("United States", t, rel); self.assertNotIn("time zone", t, rel)
        self.assertIn("the box starts unchecked", rd(os.path.join(REPO, "README.md"))); self.assertIn("the box starts unchecked", g); self.assertIn("where the box starts unchecked", u)
        self.assertIn("If the choice cannot be saved, reporting stays off", p); self.assertIn("If the choice cannot be saved, reporting stays off", u)
        for rel in ("PRIVACY.md", "README.md", "SECURITY.md", "tools/rom-pipeline/studio/guide.html", "tools/publish/assets/legal/USAGE-REPORTS.txt"):
            t = rd(os.path.join(REPO, rel))
            for stale in ("off by default", "off until you turn it on", "it starts off", "essential"): self.assertNotIn(stale, t, rel)

    def test_08_the_choice_moves_with_an_earlier_data_folder(self):   # the setup never creates the new data folder while the app still has to move the earlier one
        s = rd(ISS)
        self.assertIn("if (not DirExists(Now_)) and DirExists(Old) then Result := Old else Result := Now_;", s)
        self.assertIn("if not FileExists(UsageDataDir + '\\usage-reporting.json') then Exit;\n  Result := 'file';", s)   # any file there: no page, kept as it is
        self.assertIn("if FileExists(Dir + '\\usage-reporting.json') then Exit;", s); self.assertIn("if not DirExists(Dir) then ForceDirectories(Dir);", s)
        self.assertNotIn("{localappdata}\\ClassicCartStudio", s.split("[Code]", 1)[0], "nothing else in the setup creates the data folder")
        # the app's own rule, in both of its halves: the earlier folder is moved whole, and only while the new one does not exist
        self.assertIn("if (!Directory.Exists(now) && Directory.Exists(old)) { try { Directory.Move(old, now); }", rd(os.path.join(REPO, "tools", "desktop", "ClassicCartStudio", "App.cs")))
        self.assertIn("if not os.path.isdir(new) and os.path.isdir(old):\n        try: os.rename(old, new)", rd(os.path.join(REPO, "tools", "rom-pipeline", "common.py")))
        sys.path.insert(0, os.path.join(REPO, "tools", "companion")); import metrics_relay as MR
        def first_start(la):   # the packaged backend's first start (common.py at import), in a fresh process with this LOCALAPPDATA
            env = {k: v for k, v in os.environ.items() if k != "NES_STUDIO_DATA"}; env["LOCALAPPDATA"] = la
            code = "import sys; sys.frozen = True; sys.path.insert(0, sys.argv[1]); import common; print(common.DATA_ROOT)"
            r = subprocess.run([sys.executable, "-c", code, os.path.join(REPO, "tools", "rom-pipeline")], capture_output=True, text=True, env=env)
            self.assertEqual(r.returncode, 0, r.stderr); return r.stdout.strip()
        la = tempfile.mkdtemp(prefix="ccs-data-")
        try:
            old, new = os.path.join(la, "NESClassicCollectionStudio"), os.path.join(la, "ClassicCartStudio"); os.makedirs(old)
            wr(os.path.join(old, "library.json"), b"{}"); wr(os.path.join(old, "usage-reporting.json"), INSTALLER_ON.encode())   # where the setup writes it
            self.assertEqual(os.path.normcase(first_start(la)), os.path.normcase(new))
            self.assertFalse(os.path.exists(old)); self.assertTrue(os.path.exists(os.path.join(new, "library.json")), "the person's data moved")
            self.assertEqual(MR.load_choice(lambda *p: os.path.join(new, *p)), {"choice": "on", "chosen": True, "source": "installer"})
            # what the rule prevents: a setup that had created the new folder for its file would have stopped the move
            shutil.rmtree(new); os.makedirs(old); os.makedirs(new); wr(os.path.join(old, "library.json"), b"{}"); wr(os.path.join(new, "usage-reporting.json"), INSTALLER_ON.encode())
            first_start(la); self.assertFalse(os.path.exists(os.path.join(new, "library.json"))); self.assertTrue(os.path.exists(os.path.join(old, "library.json")))
        finally: shutil.rmtree(la, ignore_errors=True)
    def test_09_the_owner_approved_look(self):   # dark style, the PNG art at six scalings from the repository, the CCS icon, no BMP wizard images,
        # the CCS wordmark top right (not the controller icon), and text laid out from the display's own measures (no clipped or overlapping words)
        s = rd(ISS); setup = s.split("[Setup]", 1)[1].split("\n[", 1)[0]; code = s.split("[Code]", 1)[1]
        self.assertIn("\nWizardStyle=modern dark includetitlebar hidebevels\n", setup); self.assertIn("\nWizardBackColor=#151220\n", setup)
        self.assertIn("\nSetupIconFile=..\\publish\\assets\\app.ico\n", setup)
        art = os.path.join(REPO, "tools", "publish", "assets", "installer")
        self.assertFalse([f for f in os.listdir(art) if f.startswith("wizard-mark-")], "the controller mark is not the wordmark")
        self.assertIn('Source: "..\\publish\\assets\\installer\\wizard-wordmark-*.png"; Flags: dontcopy', s)
        self.assertIn("procedure ShowWordmark;", code); self.assertIn("Img.PngImage.LoadFromFile(ExpandConstant('{tmp}\\' + F));", code)
        self.assertIn("ShowWordmark; LabelIndent := CheckLabelIndent;", code)
        self.assertIn("WORDMARK_W = 92; WORDMARK_H = 28;", code); self.assertIn("PANEL, WORDMARK, BACK = (164, 314), (92, 28), (690, 500)", rd(os.path.join(REPO, "tools", "publish", "make_installer_art.py")))
        self.assertIn("FitCheckRow(UsageBox, USAGE_LABEL);", code); self.assertIn("FitCheckRow(TermsAgree, AGREE_LABEL);", code)   # rows as tall as their wrapped words
        self.assertNotIn("ScaleX(LABEL_INDENT)", code); self.assertEqual(code.count("Left := LabelIndent;"), 3)   # the note, the description and its button under the box's words
        # (the k3f4 Sandbox run) a silent setup must not stop at the terms page's disabled Next ("Failed to proceed to next wizard page"),
        # the last page before installing says Install, and the files the pages extract come first (solid compression: no 20 s wait)
        self.assertIn("WizardForm.NextButton.Enabled := TermsAgree.Checked[0] or WizardSilent;", code)
        self.assertIn("if CurPageID = wpSelectTasks then WizardForm.NextButton.Caption := SetupMessage(msgButtonInstall);", code)
        self.assertIn("\nSelectTasksLabel2=Select the additional tasks you would like Setup to perform while installing [name], then click Install.\n", s)   # its words name that button
        files = s.split("\n[Files]\n", 1)[1].split("\n[", 1)[0]
        self.assertLess(files.rfind("Flags: dontcopy"), files.index('Source: "{#SourceDir}\\*"'))
        for key, name in (("WizardImageFile", "wizard-panel"), ("WizardSmallImageFile", "wizard-wordmark"), ("WizardBackImageFile", "wizard-back")):
            files = [f"..\\publish\\assets\\installer\\{name}-{pct}.png" for pct in (100, 125, 150, 175, 200, 250)]
            self.assertIn(f"\n{key}={','.join(files)}\n", setup)
            for pct in (100, 125, 150, 175, 200, 250): self.assertTrue(os.path.isfile(os.path.join(art, f"{name}-{pct}.png")), (name, pct))
        self.assertNotIn(".bmp", s); self.assertNotIn("wizard-*.bmp", rd(os.path.join(REPO, ".github", "workflows", "release.yml")))
        self.assertIn("#if Ver < EncodeVer(6, 7, 0)", s)
        self.assertNotIn("Click Next", s.split("[Messages]", 1)[1].split("\n[", 1)[0], "Setup adds that sentence itself")
        self.assertIn("TermsAgree.WantTabs := True;", s); self.assertIn("UsageBox.WantTabs := True;", s)   # check-box look: no list box or selection bar

INSTALLER_ON = '{"schema":"ccs-usage-choice/1","choice":"on","source":"installer"}'
USAGE_LABEL = "Help improve Classic Cart Studio with basic usage and error reports (recommended)"
USAGE_DESC = ("These reports help us understand which features are useful and spot common problems, so we can prioritize fixes and make CCS more reliable. "
              "They never include your ROMs, game names, collections, saves or personal file details. You can turn reporting off now or anytime in the Studio menu, "
              "under Usage Reporting. All features work either way.")
USAGE_TXT = os.path.join(REPO, "tools", "publish", "assets", "legal", "USAGE-REPORTS.txt")
ROWS = ("None are sent, and the Windows app makes no remote request for them.",
        "because the Usage Reporting choice does not cover it. A content or privacy blocker can block it without breaking the website.",
        "These providers process connection information, such as your IP address, and may keep security and access records under their own terms.",
        "Classic Cart Studio has no compulsory usage reporting.")

if __name__ == "__main__":
    r = unittest.main(exit=False, verbosity=2).result; print(f"RESULT {r.testsRun - len(r.failures) - len(r.errors)} / {r.testsRun}"); sys.exit(0 if r.wasSuccessful() else 1)
