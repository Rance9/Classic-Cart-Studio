// Classic Cart Studio console backup and restore window. Copyright (C) 2026 Rance9. GPL-3.0-or-later.
// CCS Mod and CCS Nucleus (Engine/MethodEngine, Engine/FirmwareEngine; tools/console-nucleus): the install method chosen when preparing,
// with CCS Mod's optional CCS HOME at power-on (an explicit choice, off by default, here and when changing to CCS Mod; turned off the same
// way), removing a method or changing between them (in Console Restore), and an owner's own system image added to or removed from CCS Nucleus's
// start choices (Console Composer's Your System Images). Each runs in the controlled worker under the strict policy: the console is backed
// up in the same session, the plan is reviewed and confirmed once, and a method change takes effect at the console's next normal start
// through hakchi's own package folder. The pages explain what each does in plain words; package names, step codes and checksums are in
// each page's Technical details.
using CcsBackup.Engine;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace CcsBackup.Ui
{
    public sealed partial class BackupWorkspace
    {
        string preparePolicy = "mod";
        bool prepareModStart, methodModStart;   // CCS HOME at power-on with CCS Mod: the owner's explicit choice, off by default
        /// <summary>the console the open method or system image review names (for the result)</summary>
        string methodTag;

        const string ModStartName = "Start CCS HOME when the console is turned on";
        CheckBox ModStartBox(bool value, Action<bool> set)
        {
            var cb = new CheckBox { Content = new TextBlock { Text = ModStartName, TextWrapping = TextWrapping.Wrap }, IsChecked = value, Margin = new Thickness(26, 0, 0, 2), Foreground = Res<System.Windows.Media.Brush>("B.Ink") };
            System.Windows.Automation.AutomationProperties.SetName(cb, ModStartName);
            cb.Checked += (s, e) => set(true); cb.Unchecked += (s, e) => set(false); return cb;
        }
        const string ModStartText = "Off: the console starts in its own menu. On: CCS HOME opens first, and leaving it continues the console's own start. Turn it off later the same way.";

        UIElement PolicyChoice()
        {
            var c = new StackPanel(); c.Children.Add(T("How Classic Cart Studio starts on this console", Ty.Heading, true, false, new Thickness(0, 0, 0, 8)));
            CheckBox start = null;
            foreach (var (id, name, text) in new[]
            {
                ("mod", "CCS Mod for Hakchi", "With CCS Mod, CCS HOME is an entry in the console's own menu; the console still starts in its own menu."),
                ("nucleus", "CCS Nucleus", "With CCS Nucleus, a chooser opens when the console starts: pick CCS HOME, the console's own system or one of your own system images."),
                ("none", "Hakchi only", "Only the foundation from Hakchi2 CE, with no way to start CCS HOME yet. To add one later, prepare the console again and choose it."),
            })
            {
                var rb = new RadioButton { Content = T(name, Ty.Body, true, false, new Thickness(0)), GroupName = "policy", IsChecked = preparePolicy == id, Margin = new Thickness(0, 4, 0, 2), Foreground = Res<System.Windows.Media.Brush>("B.Ink"), VerticalContentAlignment = VerticalAlignment.Top };
                System.Windows.Automation.AutomationProperties.SetName(rb, name); System.Windows.Automation.AutomationProperties.SetHelpText(rb, text);
                var k = id; rb.Checked += (s, e) => { preparePolicy = k; if (start != null) start.IsEnabled = k == "mod"; }; c.Children.Add(rb);
                c.Children.Add(T(text, Ty.Small, false, true, new Thickness(26, 0, 0, 6)));
                if (id == "mod")
                {   // CCS Mod's optional start, under its own choice (CCS Nucleus's chooser decides what starts there)
                    start = ModStartBox(prepareModStart, v => prepareModStart = v); start.IsEnabled = preparePolicy == "mod"; c.Children.Add(start);
                    c.Children.Add(T(ModStartText, Ty.Small, false, true, new Thickness(26, 0, 0, 6)));
                }
            }
            c.Children.Add(T("CCS HOME, with the Included Games, is put on the console in the same session. The way it starts takes effect the next time the console starts normally. Classic Cart Studio for Windows can later put your own collection there.", Ty.Small, false, true, new Thickness(0, 6, 0, 0)));
            c.Children.Add(Details(TechNes, "CCS Mod for Hakchi is the hakchi package ccs-mod (ccs-mod.hmod); CCS Nucleus is ccs-nucleus (ccs-nucleus.hmod). The chosen package is placed in hakchi's own transfer folder, and hakchi installs it at the console's next normal start, then restarts the console once.",
                "CCS HOME at power-on is CCS Mod's own optional start setting (its start script in hakchi's overlay); CCS Nucleus does not use it."));
            return Card(c);
        }

        // ------------------------------------------------------------------ Console Restore: remove or change the method
        void ShowMethods()
        {
            var sp = SectionHead("CCS Mod and CCS Nucleus", "Change how Classic Cart Studio starts on your " + Con + ", or remove the way it starts. This changes files on the console, and your games and saves stay. You review the change first, and the console is backed up before anything is written.");
            var what = new StackPanel();
            what.Children.Add(T("The two ways Classic Cart Studio starts", Ty.Heading, true, false, new Thickness(0, 0, 0, 8)));
            what.Children.Add(KeyValues(("CCS Mod for Hakchi", "CCS HOME is an entry in the console's own menu. The console starts in its own menu, unless you choose to open CCS HOME when the console is turned on."),
                                        ("CCS Nucleus", "A chooser: you pick CCS HOME, the console's own system or one of your own system images.")));
            what.Children.Add(T("Removing one takes away that way of starting, and the console starts in its own menu again. Changing installs the other one in its place. Either way, games, saves, settings, other hakchi packages and your system images stay, and the change takes effect the next time the console starts normally.", Ty.Text));
            what.Children.Add(T("Only for a console Classic Cart Studio prepared; the review refuses any other. If neither is installed, choose one by preparing the console again (Prepare Console).", Ty.Small, false, true, new Thickness(0)));
            what.Children.Add(Details(TechNes, "CCS Mod is the hakchi package ccs-mod (ccs-mod.hmod) and CCS Nucleus is ccs-nucleus (ccs-nucleus.hmod), both pinned in this build's preparation program.",
                "Removing places the package's removal list in hakchi's transfer folder; changing places the new package and the old one's removal list there, and CCS HOME's package in the new method's layout. hakchi applies them at the console's next normal start and restarts the console once. Only the removed package's unchanged files are removed; its settings stay on the console as data.",
                "Only files on the console's data storage are written (hakchi's transfer folder and CCS HOME's folder); no kernel, bootloader or system is changed. The console must run Hakchi2 CE 3.9.3's hakchi."));
            sp.Children.Add(Card(what));
            var c = CardGrid(260, 2);
            var start = new StackPanel(); start.Children.Add(T("When changing to CCS Mod", Ty.Heading, true, false, new Thickness(0, 0, 0, 6)));
            start.Children.Add(ModStartBox(methodModStart, v => methodModStart = v)); start.Children.Add(T(ModStartText, Ty.Small, false, true, new Thickness(26, 0, 0, 0)));
            foreach (var (op, m, title, text) in new[]
            {
                ("remove", "nucleus", "Remove CCS Nucleus", "The chooser goes, and the console starts in its own menu again. Your system images stay on the console as files."),
                ("remove", "mod", "Remove CCS Mod", "CCS HOME's entry in the console's menu goes, and the console starts in its own menu. Games and saves stay."),
                ("change", "nucleus", "Change to CCS Nucleus", "CCS Nucleus takes over from CCS Mod, with its chooser."),
                ("change", "mod", "Change to CCS Mod", "CCS Mod takes over from CCS Nucleus. Your system images stay on the console as files. Its start at power-on is the choice below."),
            })
            {
                var o = op; var mm = m;
                var card = ActionCard(title, text, "Btn", () => { methodTag = null; var q = ConsoleReq("method-plan"); q.Add("op", o); q.Add("method", mm); if (o == "change" && mm == "mod" && methodModStart) q.Add("mod_start", true); StartOp("method", "Preparing the Review", "The connected console is identified and backed up first. Nothing is written until you confirm. Keep the USB cable connected.", q); });
                // the card's button names the step with "…" (it opens a review first)
                if (card is Border cb && cb.Child is Grid cg && cg.Children.OfType<Button>().FirstOrDefault() is Button btn) { btn.Content = title + "…"; System.Windows.Automation.AutomationProperties.SetName(btn, title + "…"); }
                c.Children.Add(card);
            }
            sp.Children.Add(c); sp.Children.Add(Card(start));
            if (string.IsNullOrEmpty(opt.SimulatedConsole) || !profile.ConsoleTasks) sp.Children.Add(ConnectSteps("Nothing changes until you confirm; the change takes effect at the console's next normal start."));
            sp.Children.Add(Buttons(B("Back", "Btn.Ghost", BackToSection)));
            Show(sp, "methods");
        }

        // ------------------------------------------------------------------ Console Composer: an owner's system image as a CCS Nucleus choice
        void ShowFirmware()
        {
            var sp = SectionHead("Your System Images", "Add a system image file of your own from this PC as a start choice in CCS Nucleus on your " + Con + ", or remove one you added. This changes files on the console: you review it first, and the console is backed up before anything is written.");
            var how = new StackPanel();
            how.Children.Add(T("How it works", Ty.Heading, true, false, new Thickness(0, 0, 0, 8)));
            how.Children.Add(Numbered("You choose a system image file on this PC. Workbench does not supply or download system images.",
                                      "Workbench checks the file and tells you which system it holds, in the review, before anything is written.",
                                      "The image becomes a choice in CCS Nucleus's chooser. It starts only when you pick it, for that one start. When you don't pick it, your console starts its own system as before, and that stays your way back."));
            how.Children.Add(T("CCS Nucleus must be installed on the console (Prepare Console, or CCS Mod and CCS Nucleus in Console Restore).", Ty.Small, false, true, new Thickness(0, 4, 0, 0)));
            how.Children.Add(Details(TechNes, "A system image is a SquashFS file (.hsqs) holding a console system's root file system. It is checked with hakchi's own rules, and a kernel file is refused.",
                "It goes to CCS Nucleus's own folder on the console's data storage, never to hakchi's firmware folder (hakchi starts the first image there by itself, without a choice). The image and " + Units.Gb(FirmwareEngine.Reserve) + " more must fit on the data storage.",
                "The console must run Hakchi2 CE 3.9.3's hakchi with CCS Nucleus installed; its list of start choices is read before anything is written."));
            sp.Children.Add(Card(how));
            var c = new StackPanel();
            c.Children.Add(T("Add an image", Ty.Heading, true, false, new Thickness(0, 0, 0, 10)));
            var label = new TextBox(); System.Windows.Automation.AutomationProperties.SetName(label, "Name shown in the chooser (optional)");
            c.Children.Add(Field("Name shown in the chooser (optional)", label, null, 420));
            c.Children.Add(T("Left empty, the name follows the system the image holds; the review shows which system that is.", Ty.Small, false, true, new Thickness(0, 0, 0, 8)));
            c.Children.Add(Buttons(B("Choose an Image File and Review…", "Btn.Primary", () =>
            {
                var d = new Microsoft.Win32.OpenFileDialog { Title = "Choose a system image file (.hsqs)", Filter = "System images (*.hsqs)|*.hsqs" }; if (d.ShowDialog(this) != true) return;
                methodTag = null; var q = ConsoleReq("firmware-plan"); q.Add("op", "add"); q.Add("file", d.FileName); q.Add("label", label.Text.Trim());
                StartOp("firmware", "Preparing the Review", "The image file is checked on this PC, then the connected console is identified and backed up. Nothing is written until you confirm. Keep the USB cable connected.", q);
            })));
            var id = new TextBox(); System.Windows.Automation.AutomationProperties.SetName(id, "Image to remove (its id)");
            c.Children.Add(new Border { Height = 1, Background = Res<System.Windows.Media.Brush>("B.Line"), Margin = new Thickness(0, 10, 0, 16) });
            c.Children.Add(T("Remove an image", Ty.Heading, true, false, new Thickness(0, 0, 0, 10)));
            c.Children.Add(Field("The image's id (shown when it was added)", id, null, 420));
            var remove = B("Review Removing It…", "Btn", () =>
            {
                if (string.IsNullOrWhiteSpace(id.Text)) return;
                methodTag = null; var q = ConsoleReq("firmware-plan"); q.Add("op", "remove"); q.Add("id", id.Text.Trim());
                StartOp("firmware", "Preparing the Review", "The connected console is identified and backed up first. Nothing is written until you confirm. Keep the USB cable connected.", q);
            }, false);
            var need = T("Type the id of the image to remove.", Ty.Small, false, true, new Thickness(0, 0, 0, 0));
            id.TextChanged += (s, e) => { remove.IsEnabled = !string.IsNullOrWhiteSpace(id.Text); need.Visibility = remove.IsEnabled ? Visibility.Collapsed : Visibility.Visible; };
            c.Children.Add(Buttons(remove)); c.Children.Add(need);
            sp.Children.Add(Card(c));
            if (string.IsNullOrEmpty(opt.SimulatedConsole) || !profile.ConsoleTasks) sp.Children.Add(ConnectSteps("Nothing changes until you confirm."));
            sp.Children.Add(Buttons(B("Back", "Btn.Ghost", () => { if (LeaveOk()) BackToSection(); })));
            Show(sp, "firmware");
            leaveCheck = () => (label.Text.Trim().Length == 0 && id.Text.Trim().Length == 0)
                || MessageBox.Show(this, "What you typed on this page is not kept if you leave it. Leave anyway?", ProductTitle, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;
        }

        // the plans and results of both (worker events method-plan / firmware-plan; result operation method / firmware)
        void ShowMethodOrFirmwarePlan(string kind, Dictionary<string, object> p)
        {
            bool fw = kind == "firmware"; string op = p.S("operation"), method = p.S("method"); methodTag = p.S("console_tag");
            var title = fw ? (op == "remove" ? "Review: Remove a System Image" : "Review: Add a System Image")
                           : op == "remove" ? "Review: Remove " + Methods.Label(method) : "Review: Change to " + Methods.Label(method);
            var sp = SectionHead(title, "What this would change on your " + Con + ", console " + methodTag + ", the console connected to this PC now. Nothing has been written yet.");
            var c = new StackPanel();
            if (p.S("text") != null) c.Children.Add(T(p.S("text"), Ty.Heading, true, false, new Thickness(0, 0, 0, 8)));
            var img = p.O("image"); var sys = p.O("system");
            c.Children.Add(KeyValues(("Console", "Console " + methodTag + ", connected to this PC now"),
                                     ("Image file", fw && op != "remove" && img != null ? Path.GetFileName(img.S("file") ?? "") + ", " + (img.S("size") ?? Size(img.L("bytes"))) : null),
                                     ("System in the file", fw && sys != null ? sys.S("text") : null),
                                     ("Takes effect", fw ? null : "The next time the console starts normally; it then restarts once"),
                                     ("Backed up first", p.S("protective_backup") != null ? Path.GetFileName(p.S("protective_backup")) + ", made in this session" : null)));
            if (!fw) c.Children.Add(Note("Your games and saves stay. Only files on the console's data storage are written.", "info"));
            var steps = (p.A("steps") ?? new List<object>()).OfType<string>().ToList();
            string Label(string s) => fw ? (FirmwareEngine.Steps.Contains(s) ? FirmwareEngine.StepLabel(s) : s) : Methods.Steps.Contains(s) || HomePackage.Steps.Contains(s) ? MethodEngine.StepLabel(s, op, method, p.S("from")) : s;
            c.Children.Add(T("What is written, in order", Ty.Body, true, false, new Thickness(0, 6, 0, 6)));
            // the plan names its steps by code (the engine's step list); the page shows the engine's own words for them
            c.Children.Add(Numbered(steps.Select(s => Cap(Label(s))).ToArray()));
            var notes = (p.A("notes") ?? new List<object>()).OfType<string>().ToList();
            if (notes.Count > 0) { c.Children.Add(T("Notes", Ty.Body, true, false, new Thickness(0, 6, 0, 6))); foreach (var n in notes) c.Children.Add(T("• " + n, Ty.Text)); }
            if (p.S("refusal") != null) c.Children.Add(Note(p.S("refusal"), "bad"));
            var tech = new List<object> { "Steps: " + string.Join(", ", steps) };
            if (p.O("package") is Dictionary<string, object> pk) tech.Add("Package: " + pk.S("name") + ".hmod, SHA-256 " + pk.S("sha256"));
            if (p.O("removal_list") is Dictionary<string, object> rl) tech.Add("Removal list for: " + rl.S("names") + ", SHA-256 " + rl.S("sha256"));
            if (p.O("home") is Dictionary<string, object> hm) tech.Add("CCS HOME's package: copy " + hm.S("copy") + (hm.S("size") != null ? ", " + hm.S("size") : "") + (hm.S("replaces") != null ? ", replaces " + hm.S("replaces") : ""));
            if (p.S("mod_start") != null) tech.Add("CCS HOME at power-on: " + p.S("mod_start"));
            if (img != null && fw && op != "remove") tech.Add("Image: " + img.S("file") + "; SHA-256 " + img.S("sha256") + "; " + img.L("bytes").ToString("N0", En) + " bytes");
            if (fw && p.S("id") != null) tech.Add("Choice id: " + p.S("id") + (p.S("label") != null ? "; name in the chooser: " + p.S("label") : ""));
            if (sys != null) tech.Add("System: platform " + sys.S("platform") + ", region " + sys.S("region") + ", board " + sys.S("board") + ", compression " + sys.S("compression") + (sys.S("version") != null ? ", version " + sys.S("version") : ""));
            if (p.O("console_state") is Dictionary<string, object> cs) tech.Add("The console now: hakchi " + cs.S("hakchi") + "; packages " + string.Join(", ", (cs.A("packages") ?? new List<object>()).OfType<string>()) + "; installed method " + cs.S("installed"));
            if (p.S("protective_backup") != null) tech.Add("Backup made just now: " + p.S("protective_backup"));
            tech.Add("Review digest " + p.S("digest") + "; the confirmation is good until " + (p.S("expires_utc") ?? "unknown") + " (UTC), once.");
            c.Children.Add(Details("Technical details", tech.ToArray()));
            sp.Children.Add(Card(c));
            var section = fw ? "Console Composer" : "Console Restore";
            if (p.S("refusal") != null || p.S("nonce") == null) { sp.Children.Add(Buttons(B("Done", "Btn", () => Leave(section)))); Show(sp, "method-plan"); return; }
            var go = fw ? (op == "remove" ? "Remove Now" : "Add Now") : op == "remove" ? "Remove Now" : "Change Now";
            sp.Children.Add(Buttons(B(go, "Btn.Primary", () =>
            {
                progress = new ProgressView(this, fw ? (op == "remove" ? "Removing the System Image" : "Adding the System Image") : op == "remove" ? "Removing " + Methods.Label(method) : "Changing to " + Methods.Label(method),
                    "Do not unplug the console. Each step is written once and read back; the first problem stops every later step. Stop After This Step finishes the step in progress, then stops.", "Stop After This Step", () => { worker?.Cancel(); progress.Say("Stopping after this step..."); });
                Show(progress.Panel, "progress");
                worker?.Send(new JObj { { "cmd", fw ? "firmware-confirm" : "method-confirm" }, { "digest", p.S("digest") }, { "nonce", p.S("nonce") } });
            }), B("Cancel", "Btn.Ghost", () => Leave(section))));
            Show(sp, "method-plan");
        }

        /// <summary>the title and first sentence of a console change's result that did not complete (states from the engine: nothing-to-do,
        /// canceled, stopped, refused). A refusal after a step was written reads as stopped.</summary>
        static (string title, string text) NotDone(string st, int doneCount, string unit) =>
            st == "nothing-to-do" ? ("Nothing needed to change", "Nothing was written to the console.")
            : st == "canceled" ? ("Canceled: nothing was changed", null)
            : st == "stopped" || doneCount > 0 ? ("Stopped before every " + unit + " was written", "The first problem stopped every later " + unit + ", and nothing was retried. What happened is below.")
            : ("Nothing was changed", "The console was not changed. The reason is below.");

        void MethodOrFirmwareResult(string kind, Dictionary<string, object> r)
        {
            bool fw = kind == "firmware"; var st = r.S("state"); bool ok = st == "scheduled" || st == "added" || st == "removed";
            var (failTitle, failText) = NotDone(st, (r.A("done") ?? new List<object>()).Count, "step");
            var title = st == "scheduled" ? "Change scheduled for the next start" : st == "added" ? "System image added" : st == "removed" ? "System image removed" : failTitle;
            var sp = SectionHead(title, ok ? "Written to your " + Con + (methodTag != null ? ", console " + methodTag : "") + ", and read back." : failText);
            var text = st == "scheduled" ? "At the console's next normal start, hakchi makes the change and restarts the console once. Your games and saves stay."
                : st == "added" ? "It is a choice in CCS Nucleus's chooser, and starts only when you pick it." + (r.S("id") != null ? " Its id is " + r.S("id") + ": you need it to remove the image later." : "")
                : st == "removed" ? "The choice and its image file are gone from the console. Your console's own system is unchanged."
                : r.S("problem") ?? st ?? "";
            sp.Children.Add(Note(text, ok ? "ok" : st == "canceled" ? "warn" : "bad"));
            if (r.S("protective") != null) { sp.Children.Add(T("The backup of the console made just before is kept:", Ty.Small, false, true)); sp.Children.Add(PathText(r.S("protective"))); }
            var done = (r.A("done") ?? new List<object>()).OfType<string>().ToList();
            sp.Children.Add(Details("Technical details", done.Count > 0 ? "Steps done: " + string.Join(", ", done) : null, r.S("id") != null ? "Choice id: " + r.S("id") : null, r.S("record") != null ? "Record: " + r.S("record") : null, "Result: " + st));
            var section = fw ? "Console Composer" : "Console Restore";
            sp.Children.Add(Buttons(B("Restart the Console", ok ? "Btn.Primary" : "Btn.Ghost", () => worker?.Send(new JObj { { "cmd", "finish" } })),
                                    B("Done", ok ? "Btn.Ghost" : "Btn", () => { worker?.Send(new JObj { { "cmd", "quit" } }); Go(section); })));
            Show(sp, "result");
        }
    }
}
