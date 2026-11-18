"""The Installation Terms: one source, the installer's and release's copy and the Help chapter (legal_terms.py --check), and the setup page that
shows them (installer.iss); the setup's separate optional usage page and one account of what continues when usage reporting is off (the
setup's "What Is Sent?", Privacy and Help). Checked from the files alone. No installer is built or run here.   python tools/publish/test_legal_terms.py"""
from __future__ import annotations
import hashlib, os, re, shutil, subprocess, sys, tempfile, unittest
HERE = os.path.dirname(os.path.abspath(__file__)); REPO = os.path.normpath(os.path.join(HERE, "..", ".."))
sys.path.insert(0, HERE); import legal_terms as LT
ISS = os.path.join(REPO, "tools", "companion", "installer.iss")
# (owner, 2026-11-12) plain words on every page; the control is still expressly labeled "I agree" (Installation Terms section 1)
AGREE = "I agree to the terms and understand the risks."
# (owner, 8.362 Q1) the boundary: installing alone authorizes no console change; open-source and legal rights are preserved
NOTE = "Your open-source rights and rights protected by law are preserved. Installing Classic Cart Studio does not, by itself, authorize console changes. Any console modification requires a separate review and your explicit approval."
INTRO = "Classic Cart Studio is free, independent software. The terms below explain the risks of using it, of relying on backups and of changing your NES Classic."
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
        self.assertIn("version 1.3", md); self.assertIn("**Rance9**", md); self.assertIn("published and operated by **Rance9 LLC**", md); self.assertNotIn("separately incorporated", md); self.assertIn("Nebraska law governs", md)
        self.assertIn("GNU General Public License, version 3 or, at your option, any later version", md)
        s5 = md.split("## 5. ")[1].split("## 6. ")[0]   # section 5: the hosted Included Games apart from private files and commercial titles
        for fact in ("supply no original commercial NES game", "**Included Games**", "published licenses or written permissions", "Every other game file, including any original commercial NES title, is one you supply yourself", "CCS does not sell games"): self.assertIn(fact, s5)
        self.assertIn("not treated as having agreed to this version", md)
        # 1.3 (2026-11-15): the approved public wording "game file" replaces the four remaining "ROM" uses; nothing else changed. Version 1.2
        # stays, byte for byte, beside the source (assets/legal/previous), as presented with the releases that showed it
        self.assertNotRegex(md, r"\bROMs?\b")
        prev = os.path.join(os.path.dirname(os.path.abspath(__file__)), "assets", "legal", "previous", "INSTALLATION-TERMS-1.2.md")
        with open(prev, "rb") as f: self.assertEqual(hashlib.sha256(f.read()).hexdigest(), "6b106bf68426ac86f7931c44d3e56f25db8bd275b387fc310a6ee22eab1dd202")
        for n in range(1, 21): self.assertRegex(md, rf"\n## {n}\. ")
    def test_04_the_setup_page(self):
        s = rd(ISS)
        self.assertIn(AGREE, s); self.assertIn(NOTE, s); self.assertIn(INTRO, s)
        self.assertIn("CreateCustomPage(wpWelcome, 'Before You Install'", s); self.assertTrue(AGREE.startswith("I agree "))
        self.assertIn("AGREE_LABEL = '" + AGREE + "';", s); self.assertIn("TermsAgree.AddCheckBox(AGREE_LABEL, '', 0, False,", s)   # unchecked
        for proc in ("procedure InitializeWizard;", "procedure CurPageChanged(CurPageID: Integer);", "function NextButtonClick(CurPageID: Integer): Boolean;"): self.assertEqual(s.count(proc), 1, proc)
        self.assertIn("if WizardSilent then Exit;", s); self.assertIn("WizardForm.NextButton.Enabled := TermsAgree.Checked[0];", s)
        for src in ('Source: "..\\publish\\assets\\legal\\INSTALLATION-TERMS.txt"; DestDir: "{app}\\legal"', 'Source: "..\\..\\LICENSE"; DestDir: "{app}\\legal"', 'Source: "..\\..\\THIRD_PARTY_NOTICES.md"; DestDir: "{app}\\legal"'): self.assertIn(src, s)
        self.assertNotIn("RegWriteStringValue", s.split("[Code]", 1)[1].split("procedure RemoveOwnValue")[0], "no agreement receipt is written")
        # the copy the setup saves names the version it holds (the same version as Help and the guide)
        self.assertIn("'Classic Cart Studio Installation Terms " + LT.VERSION + ".txt'", s); self.assertEqual(len(re.findall(r"Installation Terms \d+\.\d+\.txt", s)), 1)
        self.assertIn("PrivilegesRequired=lowest", s); self.assertIn("AppId={{7E4C1C3A-6B0C-4B59-9F3D-2A1D5C0E9B11}", s)
    def test_05_help_reaches_the_terms(self):
        g = rd(LT.GUIDE); j = rd(os.path.join(REPO, "tools", "rom-pipeline", "studio", "studio.js"))
        self.assertIn('<a href="#" data-guide="terms">Installation Terms (EULA)</a>', g); self.assertIn('data-guide-to="g-privacy"', g); self.assertIn('id="g-privacy"', g)
        self.assertIn("['legal','Legal, Privacy & Disclaimers'],['terms','Installation Terms (EULA)'],['credits','Credits']", j)
        self.assertIn("Reading them here is not agreeing to them", g)
        self.assertNotRegex(g, r"There is no account, no analytics and no telemetry")
    def test_06_the_optional_usage_page(self):   # its own page after the terms; the default, the kept choice, silent setup and the saved file
        s = rd(ISS); code = s.split("[Code]", 1)[1]
        self.assertIn("CreateCustomPage(TermsPage.ID, 'Usage and Diagnostics', 'Choose whether to share usage and diagnostic data.')", s)   # (owner D-143, Q2)
        self.assertIn("USAGE_LABEL = '" + USAGE_LABEL + "';", s); self.assertIn("UsageBox.AddCheckBox(USAGE_LABEL, '', 0, UsageStartsChecked,", s)   # (owner, D-124/D-130) checked, unchecked where opt-in applies
        self.assertIn("Desc.Caption := '" + USAGE_DESC_ISS + "';", s); self.assertIn("'&View Details'", s); self.assertIn("ExtractTemporaryFile('ccs-usage.txt')", s)
        self.assertIn("under Studio \u2192 Usage Reporting", USAGE_DESC)   # where the choice really is (build_html.py's Studio menu)
        for claim in ("no personal identifiers", "never used for advertising or marketing"): self.assertIn(claim, USAGE_DESC); self.assertIn(claim, rd(USAGE_TXT)); self.assertIn(claim, rd(os.path.join(REPO, "PRIVACY.md")))
        self.assertIn('_mi("mUsage", "📊", "Usage Reporting…"', rd(os.path.join(REPO, "tools", "rom-pipeline", "build_html.py")))
        # (owner, D-124/D-130) unchecked when the Windows region OR the time zone is in the EU, the EEA, the UK or Switzerland, or the region is unknown
        self.assertIn("Result := (Region <> '') and (Pos(',' + Region + ',', OPTIN_REGIONS) = 0) and ((Zone = '') or (Pos(',' + Zone + ',', OPTIN_ZONES) = 0));", code)
        regions = re.search(r"OPTIN_REGIONS = '([A-Z,]+)';", s).group(1).strip(",").split(",")
        eu = "AT BE BG HR CY CZ DK EE FI FR DE GR HU IE IT LV LT LU MT NL PL PT RO SK SI ES SE".split()
        self.assertEqual(len(eu), 27); self.assertFalse(set(eu + ["IS", "LI", "NO", "GB", "CH"]) - set(regions)); self.assertNotIn("US", regions)
        zones = re.search(r"OPTIN_ZONES = '([^']+)';", s).group(1)
        for z in ("GMT Standard Time", "W. Europe Standard Time", "Romance Standard Time", "Central Europe Standard Time", "Central European Standard Time", "GTB Standard Time", "FLE Standard Time"): self.assertIn("," + z + ",", zones)
        for gone in ("GetUserGeoID", "GEOID_UNITED_STATES", "UsUsageDefault"): self.assertNotIn(gone, s)   # (k3f6's US-only rule stays gone)
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
    def test_07_one_account_of_what_continues(self):   # the setup's "What Is Sent?", Privacy and Help say the same three things
        u = rd(USAGE_TXT); p = rd(os.path.join(REPO, "PRIVACY.md")); g = rd(LT.GUIDE)
        for doc, name in ((u, "USAGE-REPORTS.txt"), (p, "PRIVACY.md"), (g, "guide.html")):
            for row in ROWS: self.assertIn(row, doc, name)
        for fact in ("metrics.classiccartstudio.com", "Workers Analytics Engine", "three months", "This setup itself sends nothing", "Studio menu under Usage Reporting"): self.assertIn(fact, u)
        self.assertIn("The box starts checked, except when this computer's Windows region or time zone is in the EU, the EEA, the UK or Switzerland: there it starts unchecked", p)   # and nowhere the old US rule
        for rel in ("PRIVACY.md", "README.md", "tools/rom-pipeline/studio/guide.html", "tools/publish/assets/legal/USAGE-REPORTS.txt"):
            t = rd(os.path.join(REPO, rel)); self.assertNotIn("United States", t, rel)
        OPT = "unchecked in the EU, the EEA, the UK and Switzerland; uncheck it"
        self.assertIn("the box starts checked (" + OPT + " to send nothing)", rd(os.path.join(REPO, "README.md"))); self.assertIn("the box starts checked (" + OPT + " to send nothing)", g); self.assertIn("where the box starts checked (" + OPT + " and nothing is sent)", u)
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
        art_py = rd(os.path.join(REPO, "tools", "publish", "make_installer_art.py"))
        self.assertIn("WORDMARK_W = 100; WORDMARK_H = 26;", code); self.assertIn("PANEL, WORDMARK, BACK = (164, 314), (100, 26), (690, 500)", art_py)
        # (owner, 2026-11-12) the ONE approved wordmark, as the website draws it: Press Start 2P, CLASSIC CART #f5f6fc over STUDIO #ff344b at 1 : 1.25
        self.assertIn('WM_FONT = os.path.join(REPO, "site-data", "brand", "fonts", "PressStart2P-Regular.ttf")', art_py)
        self.assertIn('WM_TOP, WM_BOTTOM, WM_RATIO, WM_GAP = ((245, 246, 252), "CLASSIC CART"), ((255, 52, 75), "STUDIO"), 1.25, 9 / 16', art_py)
        self.assertNotIn("Pixelify", art_py.split('"""', 2)[2]); self.assertEqual(art_py.count("ImageFont.truetype("), 1)   # no other lettering
        # ...and the Windows app's loading screens, the player's full-screen label and the Help picture of the app show that same wordmark
        import base64; uri = "data:image/png;base64," + base64.b64encode(open(os.path.join(REPO, "tools", "publish", "assets", "brand-wordmark.png"), "rb").read()).decode()
        app = os.path.join(REPO, "tools", "desktop", "ClassicCartStudio"); loading, xaml = rd(os.path.join(app, "Loading.html")), rd(os.path.join(app, "MainWindow.xaml"))
        self.assertIn(f'<img class="t" alt="Classic Cart Studio" src="{uri}">', loading); self.assertIn(f"href='{uri}'", rd(os.path.join(REPO, "tools", "publish", "assets", "help", "laptop-studio.svg")))
        self.assertIn('<Image Source="pack://application:,,,/brand-wordmark.png"', xaml); self.assertIn('<Resource Include="..\\..\\publish\\assets\\brand-wordmark.png" Link="brand-wordmark.png" />', rd(os.path.join(app, "ClassicCartStudio.csproj")))
        for doc, name in ((loading, "Loading.html"), (xaml, "MainWindow.xaml"), (rd(os.path.join(REPO, "tools", "publish", "assets", "help", "laptop-studio.svg")), "laptop-studio.svg")): self.assertNotIn("CLASSIC CART", doc, name)
        tv = rd(os.path.join(REPO, "tools", "rom-pipeline", "studio", "tv.css"))
        self.assertIn("font-family:var(--pixel);font-weight:400;font-size:8px;line-height:1;letter-spacing:0;color:#f5f6fc;", tv); self.assertIn(".cpsurf .cpbrand span{font-size:10px;color:#ff344b}", tv)
        css = rd(os.path.join(REPO, "tools", "rom-pipeline", "studio", "studio.css"))
        self.assertIn(".hero h1 .wmark .wm1{font-size:16px;color:#f5f6fc}", css); self.assertIn(".hero h1 .wmark .wm2{font-size:20px;color:#ff344b}", css)   # the same as the site's
        lines = code.splitlines()
        for row in ("  FitCheckRow(UsageBox, USAGE_LABEL);", "FitCheckRow(TermsAgree, AGREE_LABEL);"):   # rows as tall as their wrapped words, as code (not inside a comment)
            self.assertTrue([l for l in lines if row.strip() in l and "//" not in l.split(row.strip())[0]], row)
        # (owner, 2026-11-12) no words reach the background's horizon line; the desktop shortcut starts checked
        self.assertIn("Note.Top := SafeBottom(TermsPage.Surface) - Note.Height;", code); self.assertIn("HORIZON_PCT = 80;", code)
        self.assertIn("hz = round(H * 0.80)", art_py.split("def back(pct):", 1)[1])
        tasks = s.split("\n[Tasks]\n", 1)[1].split("\n[", 1)[0]; self.assertIn('Name: "desktopicon"', tasks); self.assertNotIn("Flags: unchecked", tasks)
        self.assertNotIn("ScaleX(LABEL_INDENT)", code); self.assertEqual(code.count("Left := LabelIndent;"), 3)   # the note, the description and its button under the box's words
        # (the k3f4 Sandbox run) a silent setup must not stop at the terms page's disabled Next ("Failed to proceed to next wizard page"),
        # the last page before installing says Install, and the files the pages extract come first (solid compression: no 20 s wait)
        self.assertIn("WizardForm.NextButton.Enabled := TermsAgree.Checked[0] or WizardSilent;", code)
        self.assertIn("if CurPageID = wpSelectTasks then WizardForm.NextButton.Caption := SetupMessage(msgButtonInstall);", code)
        self.assertIn("\nSelectTasksLabel2=Classic Cart Studio will be in your Start menu. You can also put it on your desktop. Then click Install.\n", s)   # its words name that button
        files = s.split("\n[Files]\n", 1)[1].split("\n[", 1)[0]
        self.assertLess(files.rfind("Flags: dontcopy"), files.index('Source: "{#SourceDir}\\*"'))
        for key, name in (("WizardImageFile", "wizard-panel"), ("WizardSmallImageFile", "wizard-wordmark"), ("WizardBackImageFile", "wizard-back")):
            files = [f"..\\publish\\assets\\installer\\{name}-{pct}.png" for pct in (100, 125, 150, 175, 200, 250)]
            self.assertIn(f"\n{key}={','.join(files)}\n", setup)
            for pct in (100, 125, 150, 175, 200, 250): self.assertTrue(os.path.isfile(os.path.join(art, f"{name}-{pct}.png")), (name, pct))
        self.assertNotIn(".bmp", s); self.assertNotIn("wizard-*.bmp", rd(os.path.join(REPO, ".github", "workflows", "release.yml")))
        self.assertIn("#if Ver < EncodeVer(6, 7, 0)", s)
        msgs = s.split("[Messages]", 1)[1].split("\n[", 1)[0]
        self.assertEqual(msgs.count("Click Next"), 1); self.assertIn("\nClickNext=Click Next to get started.\n", msgs)   # Setup adds that sentence itself, once
        for tech in ("administrator", "ROM", "EULA", "GPL", "registry", "Setup has", "Setup will"): self.assertNotIn(tech, msgs, tech)   # (owner, 2026-11-12) plain words
        self.assertIn("TermsAgree.WantTabs := True;", s); self.assertIn("UsageBox.WantTabs := True;", s)   # check-box look: no list box or selection bar

INSTALLER_ON = '{"schema":"ccs-usage-choice/1","choice":"on","source":"installer"}'
USAGE_LABEL = "Send optional usage and diagnostic data (recommended)"   # (owner D-143, Q2)
USAGE_DESC = ("Help improve Classic Cart Studio's performance and reliability. Reports contain no personal identifiers and are never used for advertising or marketing. "
              "You can change this setting at any time under Studio \u2192 Usage Reporting.")
USAGE_DESC_ISS = USAGE_DESC.replace("'", "''").replace("Studio \u2192 Usage", "Studio ' + #$2192 + ' Usage")   # as Pascal writes it
USAGE_TXT = os.path.join(REPO, "tools", "publish", "assets", "legal", "USAGE-REPORTS.txt")
ROWS = ("None are sent, and the Windows app makes no remote request for them.",
        "because the Usage Reporting choice does not cover it. A content or privacy blocker can block it without breaking the website.",
        "These providers process connection information, such as your IP address, and may keep security and access records under their own terms.",
        "Classic Cart Studio has no compulsory usage reporting.")

if __name__ == "__main__":
    r = unittest.main(exit=False, verbosity=2).result; print(f"RESULT {r.testsRun - len(r.failures) - len(r.errors)} / {r.testsRun}"); sys.exit(0 if r.wasSuccessful() else 1)
