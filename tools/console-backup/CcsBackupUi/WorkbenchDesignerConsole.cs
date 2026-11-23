// Classic Cart Studio console backup and restore window. Copyright (C) 2026 Rance9. GPL-3.0-or-later.
// HOME Designer on the console: the design compiled for one console's games (from that console's installed HOME package, read from its
// latest backup), then checked with the native HOME renderer (in the single-file Workbench: the one linked into it; in Classic Cart Studio
// for Windows: its own ccs-sim-core.exe) on every screen it draws, with the preview frames shown here. Applying and Revert Design run in
// the controlled worker under the strict policy (a backup of the console in that session first; the same render check on the console's
// own HOME package before anything is admitted). Nothing on a console changes here until the plan is confirmed.
using CcsBackup.Compose;
using CcsBackup.Engine;
using CcsBackup.Library;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace CcsBackup.Ui
{
    public sealed partial class BackupWorkspace
    {
        string designCompiledDir, designCheckBackup; RenderCheckResult designCheck;
        /// <summary>what the open apply or revert review named: its kind and the console it changes (for the result)</summary>
        string designPlanKind, designTag;

        static string AppHomeRenderer()
        {
            var p = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "_internal", "native", "ccs-sim-core.exe");
            return File.Exists(p) ? p : null;
        }

        void ShowDesignOnConsole()
        {
            var h = design; if (h == null) { ShowDesigner(); return; }
            var sp = SectionHead("Check and Apply on My Console", "See your design with the games on your " + Con + ", drawn by the console's own HOME program here on this PC, then apply it to the console. Checking reads a backup and changes nothing. Applying changes the console, and it is backed up first.");
            if (!h.CanApply) { sp.Children.Add(Note("This design cannot be applied: " + string.Join("; ", h.Unsupported) + ".", "bad")); sp.Children.Add(Buttons(B("Back to the Design", "Btn.Ghost", () => ShowDesignEditor()))); Show(sp, "design-console"); return; }
            var c = new StackPanel();
            c.Children.Add(T("1. Check the design", Ty.Heading, true, false, new Thickness(0, 0, 0, 6)));
            c.Children.Add(T("The console's CCS HOME and its games are read from a backup of the console, the design is fitted to them, and every screen is drawn. No console is needed for this.", Ty.Small, false, true, new Thickness(0, 0, 0, 10)));
            var list = new ComboBox(); System.Windows.Automation.AutomationProperties.SetName(list, "Backup of the console");
            string readProblem = null;
            try
            {
                foreach (var e in History.List(settings).Where(x => x.Status == BackupStatus.Verified || x.Status == BackupStatus.NeedsVerification))
                    list.Items.Add(new ComboBoxItem { Content = e.When + "  ·  Console " + e.ConsoleTag, Tag = e.PrimaryPath, IsSelected = list.Items.Count == 0 });
            }
            catch (Exception ex) { readProblem = ex.Message; }
            c.Children.Add(Field("A backup of the console (the newest is best)", list));
            if (readProblem != null) c.Children.Add(Note("Your backup location could not be read: " + readProblem + " Connect its drive, or choose another location in Settings.", "bad"));
            else if (list.Items.Count == 0) c.Children.Add(Note("There are no complete backups in your backup location yet. Back up the console first (Back Up Console); the check reads its HOME and games from that backup.", "info"));
            if (designCheck != null && designCheckBackup != null) c.Children.Add(Note("Last check: " + (designCheck.Ok ? "passed" : "did not pass") + " with " + System.IO.Path.GetFileName(designCheckBackup) + ".", designCheck.Ok ? "ok" : "warn"));
            var renderer = HomeRenderers.Find(AppHomeRenderer());
            if (renderer == null) c.Children.Add(Note("The console's HOME program is not available in this copy, so a design cannot be checked or applied here.", "bad"));
            bool can = renderer != null && list.Items.Count > 0;
            c.Children.Add(Buttons(B("Check the Design", "Btn.Primary", () => RunDesignCheck((list.SelectedItem as ComboBoxItem)?.Tag as string, renderer), can),
                                   list.Items.Count == 0 && readProblem == null ? B("Back Up Console", "Btn", () => { if (LeaveOk()) { Enter("Console Backup"); ShowBackup(); } }) : null));
            c.Children.Add(T("2. Apply it", Ty.Heading, true, false, new Thickness(0, 14, 0, 6)));
            c.Children.Add(T("A design that passed the check can be applied: the console is backed up first, the design is checked again on the console's own HOME, and you confirm the change.", Ty.Small, false, true));
            sp.Children.Add(Card(c));
            sp.Children.Add(Group("Revert Design", 18));
            var rv = new StackPanel();
            rv.Children.Add(T("Puts the console's HOME back to its previous design, or to the theme's own look. Only the HOME's appearance changes; games and saves stay as they are, including saves made since. This changes the console: it is backed up first, and you confirm the change.", 13, false, true));
            rv.Children.Add(Buttons(B("Back to the Previous Design", "Btn.Ghost", () => StartDesignOp("revert", "previous")), B("Back to the Theme's Own Look", "Btn.Ghost", () => StartDesignOp("revert", "defaults"))));
            sp.Children.Add(Card(rv));
            if (string.IsNullOrEmpty(opt.SimulatedConsole) || !profile.ConsoleTasks) sp.Children.Add(ConnectSteps("Applying or reverting backs up the console first; nothing is written until you confirm.", "home-design"));
            sp.Children.Add(Buttons(B("Back to the Design", "Btn.Ghost", () => ShowDesignEditor())));
            Show(sp, "design-console");
        }

        void RunDesignCheck(string backup, IHomeRenderer renderer)
        {
            if (backup == null) { Tell("Choose a backup", "Choose a backup of the console first. The check reads the console's HOME and games from it."); return; }
            var h = design; var cts = new CancellationTokenSource();
            var pv = new ProgressView(this, "Checking the Design", "The console's HOME and games are read from the backup on this PC, the design is fitted to them and every screen is drawn. Nothing on the console changes.", "Cancel", () => cts.Cancel());
            Show(pv.Panel, "progress");
            var work = System.IO.Path.Combine(settings.RecordsFolder, "home-design", DateTime.UtcNow.ToString("yyyyMMdd-HHmmss"));
            Task.Run(() =>
            {
                Directory.CreateDirectory(work);
                var st = DesignApplyEngine.ReadState(backup);
                var pack = DesignApplyEngine.HomePackage(backup, st, null, System.IO.Path.Combine(work, "pack"));
                var games = Json.Parse(File.ReadAllText(System.IO.Path.Combine(pack, "games.json"), Encoding.UTF8)).A("games").OfType<Dictionary<string, object>>().Select(g => new ConsoleGame { Id = g.S("id"), Title = g.S("title"), CatalogId = g.S("catalog") }).ToList();
                var compiled = DesignCompiler.Compile(h, games); var cdir = System.IO.Path.Combine(work, "design"); compiled.WriteTo(cdir);
                var older = DesignApplyEngine.PackageRefuses(pack, compiled); if (older != null) throw new ComposeException("design.package", older);   // an earlier CCS HOME would set a moved layout aside
                var r = renderer.Check(pack, cdir, new RenderCheckOptions { WorkDir = System.IO.Path.Combine(work, "check"), Frames = true, Cancel = cts.Token });
                return (compiled, cdir, r, (string)null);
            }).ContinueWith(t =>
            {
                if (t.IsFaulted)
                {
                    var ex = t.Exception.GetBaseException(); bool plain = ex is ComposeException || ex is IOException || ex is InvalidDataException;
                    var ep = SectionHead("The design could not be checked", "Nothing on the console changed, and your design is as it was.");
                    ep.Children.Add(Note(plain ? ex.Message : "Something went wrong while the design was being checked.", "bad"));
                    ep.Children.Add(Details("Technical details", ex.GetType().Name + ": " + ex.Message, "Backup: " + backup));
                    ep.Children.Add(Buttons(B("Back to Check and Apply", "Btn", () => ShowDesignOnConsole()), B("Back to the Design", "Btn.Ghost", () => ShowDesignEditor())));
                    Show(ep, "msg"); return;
                }
                var (compiled, cdir, r, _) = t.Result;
                designCompiledDir = cdir; designCheckBackup = backup; designCheck = r;
                ShowDesignCheck(compiled, r);
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        void ShowDesignCheck(CompiledDesign compiled, RenderCheckResult r)
        {
            var sp = SectionHead(r.Ok ? "The design passed the check" : "The design did not pass the check", "Your design was fitted to the games in the backup and drawn on " + r.States.Count + " screens by the console's own HOME program, on this PC. Nothing on the console has changed.");
            var c = new StackPanel();
            if (!r.Ok) c.Children.Add(Note(string.Join("\n", PlainCheck(r).Select(x => "• " + x)), "warn"));
            foreach (var x in compiled.LeftOut.Take(20)) c.Children.Add(T("Left out: " + x, 12.5, false, true));
            foreach (var x in compiled.NotShown.Take(10)) c.Children.Add(T("Not shown: " + x, 12.5, false, true));
            if (r.Ok && r.Problems.Count == 0) c.Children.Add(Note("Every screen is readable and within the console's memory limits.", "ok"));
            if (!r.Ok) c.Children.Add(T("A design that did not pass cannot be applied. Change it in the editor, then check it again.", Ty.Text));
            c.Children.Add(Details("Technical details", "Drawn by " + r.Renderer, r.DesignId != null ? "Compiled design: " + r.DesignId + " (" + r.DesignTheme + ")" : null,
                r.Problems.Count > 0 ? "Problems: " + string.Join("; ", r.Problems.Take(30)) : null,
                r.AddedPeakBytes > 0 ? "Memory the design adds at its worst moment, as measured: " + CcsBackup.Engine.Units.Gb(r.AddedPeakBytes) + " (at most " + CcsBackup.Engine.Units.Gb(r.Budget) + ")" : null,
                r.ReportSha256 != null ? "Check report SHA-256: " + r.ReportSha256 : null, designCheckBackup != null ? "Backup: " + designCheckBackup : null));
            sp.Children.Add(Card(c));
            var shots = new CardPanel { MinItemWidth = 240, MaxColumns = 3, Gap = 12 };
            foreach (var f in r.Frames.Where(x => x.WithDesign).Take(24))
            {
                var cell = new StackPanel();
                try { var bi = new BitmapImage(); bi.BeginInit(); bi.StreamSource = new MemoryStream(f.Png()); bi.DecodePixelWidth = 480; bi.CacheOption = BitmapCacheOption.OnLoad; bi.EndInit(); bi.Freeze(); cell.Children.Add(new Image { Source = bi, Stretch = Stretch.Uniform, Margin = new Thickness(0, 0, 0, 4) }); }
                catch (Exception) { cell.Children.Add(T("(this frame cannot be shown)", 12, false, true)); }
                var st = r.States.FirstOrDefault(s => s.Theme == f.Theme && s.State == f.State);
                cell.Children.Add(PictureCaption((f.Theme == "startup" ? "" : DesignProfile.ThemeName(f.Theme) + " · ") + (StateNames.TryGetValue(f.State, out var sn) ? sn : f.State) + (st != null && !st.Ok ? "  ·  hard to read" : "")));
                shots.Children.Add(cell);
            }
            if (shots.Children.Count > 0) sp.Children.Add(Card(shots, 12));
            sp.Children.Add(Buttons(r.Ok ? B("Apply to the Console…", "Btn.Primary", () => StartDesignOp("apply", null)) : null, B("Back to the Design", "Btn.Ghost", () => ShowDesignEditor())));
            Show(sp, "design-check");
        }

        void StartDesignOp(string action, string to)
        {
            var r = ConsoleReq("home-design"); r.Add("action", action);
            if (action == "apply")
            {
                if (designCompiledDir == null || designCheck == null || !designCheck.Ok) { Tell("Check the design first", "A design is applied only after it passed the check with a backup of the console."); return; }
                r.Add("design", designCompiledDir); var exe = AppHomeRenderer(); if (exe != null) r.Add("renderer", exe);
            }
            else r.Add("to", to);
            designPlanKind = designTag = null;
            StartOp("home-design", "Preparing the Review", "The connected console is identified and backed up, and the design is checked again on the console's own HOME. Nothing is written until you confirm. Keep the USB cable connected.", r);
        }

        void ShowDesignPlan(Dictionary<string, object> p)
        {
            designPlanKind = p.S("kind"); designTag = p.S("console_tag"); bool revert = (designPlanKind ?? "").EndsWith("revert", StringComparison.Ordinal);   // the engine names it "design-revert"
            var sp = SectionHead(revert ? "Review: Revert Design" : "Review: Apply the Design", "What would change on your " + Con + ", console " + designTag + ", the console connected to this PC now. Nothing has been written yet.");
            var originalNote = OriginalUnavailableNote(designTag); if (originalNote != null) sp.Children.Add(originalNote);
            var ev = (p.A("evidence") ?? new List<object>()).OfType<Dictionary<string, object>>().FirstOrDefault();
            var d = new StackPanel();
            d.Children.Add(KeyValues(("Console", "Console " + designTag + ", connected to this PC now"),
                                     (revert ? "Goes back to" : "Design", revert ? (p.S("revert_to") == "defaults" ? "The theme's own look" : "The previous design") : p.S("design_name")),
                                     ("Backed up first", ev?.S("path") != null ? Path.GetFileName(ev.S("path")) + ", made in this session" : null)));
            d.Children.Add(Note("Only the HOME's appearance changes. Games and saves stay as they are; the backup made just now keeps the console as it was.", "info"));
            sp.Children.Add(Card(d));
            var c = new StackPanel();
            c.Children.Add(T("What changes on the console", Ty.Heading, true, false, new Thickness(0, 0, 0, 6)));
            c.Children.Add(ChangeTable((p.A("changes") ?? new List<object>()).OfType<Dictionary<string, object>>().Select(x => (x.S("op") == "del" ? "Remove" : x.S("old") == "absent" ? "Add" : "Replace", x.S("path"), x.L("bytes") > 0 ? CcsBackup.Engine.Units.Gb(x.L("bytes")) : ""))));
            foreach (var n in (p.A("notes") ?? new List<object>()).OfType<string>()) c.Children.Add(T("• " + n, 13, false, true));
            foreach (var n in (p.A("left_out") ?? new List<object>()).OfType<string>().Take(10)) c.Children.Add(T("Left out: " + n, 12.5, false, true));
            if (p.S("refusal") != null) c.Children.Add(Note(p.S("refusal"), "bad"));
            var tech = new List<object>();
            if (p.S("design") != null) tech.Add("Compiled design: " + p.S("design"));
            if (p.S("render_check_sha256") != null) tech.Add("Render check report SHA-256: " + p.S("render_check_sha256"));
            if (p.S("home_package_manifest_sha256") != null) tech.Add("The console's CCS HOME package (manifest SHA-256): " + p.S("home_package_manifest_sha256"));
            if (p.O("before") is Dictionary<string, object> b && p.O("after") is Dictionary<string, object> a) tech.Add("Design in use: " + (b.S("current") ?? "none") + " → " + (a.S("current") ?? "none") + "; kept as the fallback: " + (b.S("known_good") ?? "none") + " → " + (a.S("known_good") ?? "none"));
            foreach (var x in (p.A("changes") ?? new List<object>()).OfType<Dictionary<string, object>>()) if (x.S("new_sha256") != null) tech.Add(x.S("path") + ": new SHA-256 " + x.S("new_sha256"));
            if (ev != null) tech.Add("Backup made just now: " + ev.S("path") + "; SHA-256 " + ev.S("sha256"));
            tech.Add("Review digest " + p.S("digest") + "; the confirmation is good until " + (p.S("expires_utc") ?? "unknown") + " (UTC), once.");
            c.Children.Add(Details("Technical details", tech.ToArray()));
            sp.Children.Add(Card(c));
            // Cancel quits the session without writing and comes back to Check and Apply, with the design still open
            void Back() { Leave("Console Composer"); ShowDesignOnConsole(); }
            if (p.S("refusal") != null || p.S("nonce") == null) { sp.Children.Add(Buttons(B("Done", "Btn", Back))); Show(sp, "design-plan"); return; }
            sp.Children.Add(Buttons(B(revert ? "Revert Now" : "Apply Now", "Btn.Primary", () =>
            {
                progress = new ProgressView(this, revert ? "Reverting the Design" : "Applying the Design", "Do not unplug the console. Each file is written once and read back. Stop After This File finishes the file being written, then stops.", "Stop After This File", () => { worker?.Cancel(); progress.Say("Stopping after this file..."); });
                Show(progress.Panel, "progress");
                worker?.Send(new JObj { { "cmd", "home-design-confirm" }, { "digest", p.S("digest") }, { "nonce", p.S("nonce") } });
            }), B("Cancel", "Btn.Ghost", Back)));
            Show(sp, "design-plan");
        }

        void DesignResult(Dictionary<string, object> r)
        {
            var st = r.S("state"); var sp = new StackPanel(); bool revert = (designPlanKind ?? "").EndsWith("revert", StringComparison.Ordinal);
            var done = (r.A("done") ?? new List<object>()).OfType<string>().ToList();
            var (failTitle, failText) = NotDone(st, done.Count, "file");
            string title = st == "changed" ? (revert ? "Design reverted and checked" : "Design applied and checked") : failTitle;
            sp.Children.Add(PageHead(title, st == "changed" ? "Written to your " + Con + (designTag != null ? ", console " + designTag : "") + ", and read back." : failText));
            sp.Children.Add(Note(st == "changed" ? "The console shows the HOME's new appearance from its next start. Games and saves are as they were." : r.S("problem") ?? "", st == "changed" ? "ok" : st == "nothing-to-do" ? "info" : st == "canceled" ? "warn" : "bad"));
            if (r.S("protective") != null) { sp.Children.Add(T("The backup of the console made just before is kept:", Ty.Small, false, true)); sp.Children.Add(PathText(r.S("protective"))); }
            sp.Children.Add(Details("Technical details", done.Count > 0 ? "Written: " + string.Join(", ", done) : null, r.S("record") != null ? "Record: " + r.S("record") : null, "Result: " + st));
            sp.Children.Add(Buttons(B("Restart the Console", st == "changed" ? "Btn.Primary" : "Btn.Ghost", () => worker?.Send(new JObj { { "cmd", "finish" } })),
                                    design != null ? B("Back to the Design", "Btn.Ghost", () => { worker?.Send(new JObj { { "cmd", "quit" } }); ShowDesignEditor(); }) : null,
                                    B("Done", st == "changed" ? "Btn.Ghost" : "Btn", () => { worker?.Send(new JObj { { "cmd", "quit" } }); Go("Console Composer"); })));
            Show(sp, "result");
        }
    }
}
