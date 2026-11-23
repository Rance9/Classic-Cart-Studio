// Classic Cart Studio console backup and restore window. Copyright (C) 2026 Rance9. GPL-3.0-or-later.
// CCS Console Workbench: the window's sections, the same in the standalone (CCS-Console-Workbench.exe) and in Classic Cart Studio (the side
// column and the Dashboard are in WorkbenchShell.cs):
//   Console Operations  Console Backup · Console Restore (a backup, the way back from preparation) · Console Rescue (startup stages) ·
//                       Console Composer (fresh setup, rebuilding, migration, HOME Designer) · Console Browser (check-up; Expert Mode inside)
//   Backup Files        Backup Browser · Backup Repair
//   Advanced            Developer Mode
// Every console operation runs in the controlled worker (WorkerClient), each under its own write boundary in Gate; offline work on backup
// files never needs a console or a USB driver.
using CcsBackup.Engine;
using CcsBackup.Library;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace CcsBackup.Ui
{
    public sealed partial class BackupWorkspace
    {
        StackPanel SectionHead(string title, string text) => PageHead(title, text);

        /// <summary>a console task's connection steps, after one line on whether this version does the task with a real console of the chosen
        /// family (cmd: the task's worker request): available (reads only), not available for this console model, or behind a closed boundary</summary>
        UIElement ConnectSteps(string what, string cmd)
        {
            if (!profile.ConsoleTasks || profile.RecoveryStep == null) return FamilyNotYetNote();
            var steps = new StackPanel();
            var modelWhy = CcsBackup.Device.ConsoleModels.ById(profile.PhysicalModel)?.Refusal(OperationOfRequest(cmd));
            var closed = ClosedOnRealConsole(cmd);
            steps.Children.Add(modelWhy != null ? Note(modelWhy + " This page starts nothing.", "info")
                : closed != null ? Note(closed + " " + ReadsAvailable, "warn")
                : Note("Available in this version with a real " + Con + ": this task only reads the console. Nothing on it changes.", "ok"));
            steps.Children.Add(T("Connect the console", 15, true, false, new Thickness(0, 0, 0, 10)));
            steps.Children.Add(Numbered("Turn the " + Con + " off and connect it to this PC with its USB cable.", profile.RecoveryStep, "The TV stays dark until you start the task below; then it shows a Classic Cart Studio screen while a small program runs from the console's memory. " + what));
            steps.Children.Add(T(DriverStepNote, 12.5, false, true, new Thickness(0, 6, 0, 0)));
            return Card(steps);
        }

        void StartOp(string op, string title, string note, JObj req)
        {
            if (!FamilyHasTasks(OperationOf(op)) || !BoundaryOpen(req["cmd"] as string)) return;
            if (!driverReady && string.IsNullOrEmpty(opt.SimulatedConsole)) { WithDriver(() => StartOp(op, title, note, req)); return; }
            if (ConsoleLock.IsBusy()) { Show(Message("Another console task is running", "The console is already in use by this window, another CCS Console Workbench window or Classic Cart Studio. Wait for that task to finish, then try again.", "bad"), "msg"); return; }
            progress = new ProgressView(this, title, note, "Cancel", () => { worker?.Cancel(); progress.Say("Canceling..."); });
            Show(progress.Panel, "progress");
            NewSession(op);
            worker.Send(req);
        }

        // ------------------------------------------------------------------ Console Restore
        void ShowRestoreHub()
        {
            var sp = SectionHead("Console Restore", "Put a backup back on the console it came from, or undo what Classic Cart Studio changed on your " + Con + ". Each of these changes the console: you review every change first, and the console is backed up before anything is written.");
            sp.Children.Add(CardGrid(260, 3,
                ActionCard("Restore a Backup", "Put a backup of this console back on it. You review exactly what changes first, and the console as it is now is backed up in the same session before anything is written.", "Btn.Primary", () => ShowRestorePick()),
                ActionCard("Add-ons and Startup", "Add or remove CCS Mod (CCS HOME) and CCS Nucleus, and choose what opens when you turn on your console. Games and saves stay.", "Btn", () => ShowMethods(), "Choose Add-ons"),
                ActionCard("Return to Original Software", "Undo Classic Cart Studio's preparation: put back Nintendo's startup software and this console's own bootloader, remove hakchi's files, then restore the backup made before preparing. This is not a factory reset.", "Btn", () => ShowReturn())));
            sp.Children.Add(T("A console that no longer starts? Console Rescue writes a damaged startup stage back from this console's own backup.", Ty.Small, false, true));
            Show(sp, "restore-hub");
        }

        // ------------------------------------------------------------------ Console Rescue (startup stages)
        Dictionary<string, object> rescueDiag; string rescueTag;
        void ShowRescue()
        {
            var sp = SectionHead("Console Rescue", "For an " + Con + " that no longer starts because a stage of its bootloader is damaged: Console Rescue writes this console's own copy of that stage back from one of its backups. It changes the console only after you confirm.");
            var c = new StackPanel();
            c.Children.Add(T("How it works", 15, true, false, new Thickness(0, 0, 0, 10)));
            c.Children.Add(Numbered("The console's startup stages are read and checked (nothing is written).", "You choose a backup of this same console that holds complete copies of them.", "The console as it is now is backed up first. A damaged stage is kept exactly as it reads, and you confirm, by typing a sentence, that it cannot be saved as it was.", "Only the chosen stage is written, once, and read back. Then you switch the console off and on."));
            c.Children.Add(Note("A stage is only ever taken from a backup of this same console. A black screen alone does not mean a stage is damaged: the check in step 1 decides. Software cannot repair failed hardware.", "warn"));
            c.Children.Add(T("Not the same as Backup Repair: Backup Repair fixes a backup file on this PC and never touches a console.", Ty.Small, false, true));
            c.Children.Add(Details(TechNes, "The startup stages are boot0 (the first stage) and boot1 (u-boot, the bootloader), read from the console's storage and checked against the copies in the backup.", "Each chosen stage is written once and read back; if a write or its read-back fails, nothing is retried."));
            sp.Children.Add(Card(c));
            if (string.IsNullOrEmpty(opt.SimulatedConsole) || !profile.ConsoleTasks) sp.Children.Add(ConnectSteps("Nothing is written until you confirm the plan.", "rescue"));
            sp.Children.Add(Buttons(B(string.IsNullOrEmpty(opt.SimulatedConsole) ? "Check the Console" : "Check the Console (Simulated)", "Btn.Primary", () => StartOp("rescue", "Checking the Startup Stages", "Keep the USB cable connected.", ConsoleReq("rescue")))));
            Show(sp, "rescue");
        }

        void ShowRescueChoice(Dictionary<string, object> ev)
        {
            rescueDiag = ev; rescueTag = ev.S("console_tag");
            var sp = SectionHead("Console Rescue", ev.S("text"));
            var damaged = (ev.A("damaged") ?? new List<object>()).OfType<string>().ToList();
            var c = new StackPanel();
            c.Children.Add(T("Stages to write", 14, true));
            var cb1 = new CheckBox { Content = "The bootloader (boot1, u-boot)", IsChecked = damaged.Contains("boot1"), Margin = new Thickness(0, 4, 0, 4) };
            var cb0 = new CheckBox { Content = "The first startup stage (boot0)", IsChecked = damaged.Contains("boot0"), Margin = new Thickness(0, 4, 0, 4) };
            c.Children.Add(cb1); c.Children.Add(cb0);
            var list = new ComboBox();
            foreach (var e in History.List(settings).Where(x => (x.Status == BackupStatus.Verified || x.Status == BackupStatus.NeedsVerification) && (rescueTag == null || x.ConsoleTag == rescueTag)))
                list.Items.Add(new ComboBoxItem { Content = e.When + "  ·  " + Path.GetFileName(e.PrimaryPath), Tag = e.PrimaryPath });
            if (list.Items.Count > 0) list.SelectedIndex = 0;
            c.Children.Add(new Border { Height = 10 });
            c.Children.Add(Field("Backup of this console to take them from", list, B("Browse…", "Btn.Ghost", () => { var d = new Microsoft.Win32.OpenFileDialog { Filter = "Console backups (*.zip)|*.zip" }; if (d.ShowDialog(this) == true) { list.Items.Insert(0, new ComboBoxItem { Content = d.FileName, Tag = d.FileName }); list.SelectedIndex = 0; } })));
            TextBox ack = null;
            if (damaged.Count > 0)
            {
                c.Children.Add(Note("A damaged stage cannot be saved as it was. To continue, type exactly: " + ev.S("acknowledgment"), "warn"));
                ack = new TextBox(); System.Windows.Automation.AutomationProperties.SetName(ack, "Acknowledgment"); c.Children.Add(Field("Type it here", ack));
            }
            sp.Children.Add(Card(c));
            sp.Children.Add(Buttons(B("Review the Repair", "Btn.Primary", () =>
            {
                var stages = new List<object>(); if (cb1.IsChecked == true) stages.Add("boot1"); if (cb0.IsChecked == true) stages.Add("boot0");
                var file = (list.SelectedItem as ComboBoxItem)?.Tag as string;
                Show(progress.Panel, "progress"); progress.Update("Backing up the console as it is now...", 0, 0);
                worker?.Send(new JObj { { "cmd", "rescue-choose" }, { "backup", file }, { "stages", stages }, { "ack", ack?.Text } });
            }), B("Cancel", "Btn.Ghost", () => Leave("Console Rescue"))));
            Show(sp, "rescue-choose");
        }

        void ShowRescuePlan(Dictionary<string, object> p)
        {
            var sp = SectionHead("Review the Repair", "What Console Rescue would write to your " + Con + ". Nothing has been written yet.");
            var c = new StackPanel();
            foreach (var s in (p.A("stages") ?? new List<object>()).OfType<Dictionary<string, object>>().Where(x => x.B("change")))
                c.Children.Add(T("• " + (s.S("stage") == "boot0" ? "The first startup stage (boot0)" : "The bootloader (boot1, u-boot)") + ": " + (s.S("now") == "unreadable" ? "cannot be read now, so it is written without comparing it first" : s.S("now") == "invalid" || s.S("now") == "noimage" ? "damaged now" : "differs from your backup now") + ". " + Units.Gb(s.L("bytes")) + " will be written from your backup.", 13));
            foreach (var n in (p.A("not_preserved") ?? new List<object>()).OfType<string>()) c.Children.Add(Note("Not preserved: " + n, "warn"));
            foreach (var n in (p.A("notes") ?? new List<object>()).OfType<string>()) c.Children.Add(T(n, 13));
            sp.Children.Add(Card(c));
            sp.Children.Add(Buttons(B("Repair Now", "Btn.Primary", () => { Show(progress.Panel, "progress"); worker?.Send(new JObj { { "cmd", "rescue-confirm" }, { "digest", p.S("digest") }, { "nonce", p.S("nonce") } }); }),
                                    B("Cancel", "Btn.Ghost", () => Leave("Console Rescue"))));
            Show(sp, "rescue-plan");
        }

        void RescueResult(Dictionary<string, object> r)
        {
            var st = r.S("state"); var sp = new StackPanel();
            if (st == "rescued")
            {
                sp.Children.Add(PageTitle("Repaired and read back"));
                sp.Children.Add(Note("Switch the console off, disconnect the USB cable, and switch it on. Only a normal start, with your games and saves, shows that the repair worked.", "ok"));
            }
            else { sp.Children.Add(PageTitle(st == "nothing-to-do" ? "Nothing to repair" : st == "canceled" ? "Canceled" : "The repair did not complete")); sp.Children.Add(Note(r.S("problem") ?? "", st == "nothing-to-do" ? "info" : "bad")); }
            sp.Children.Add(Buttons(B("Done", "Btn", () => { worker?.Send(new JObj { { "cmd", "quit" } }); Go("Console Rescue"); })));
            Show(sp, "result");
        }

        // ------------------------------------------------------------------ Console Composer
        void ShowComposer()
        {
            var sp = SectionHead("Console Composer", "Set up your " + Con + " from your own backups and files: fresh with Classic Cart Studio, like another console of yours, or rebuilt from your backups. Each setup changes the console; you review it first, and the console is backed up before anything is written.");
            sp.Children.Add(Group("Set up a console", 0));
            sp.Children.Add(CardGrid(270, 3,
                ActionCard("Start Fresh with Classic Cart Studio", "Prepare this console with Classic Cart Studio's HOME (after a complete backup of it), then add your games and saves.", "Btn.Primary", () => ShowPrepare(), "Start Fresh"),
                ActionCard("Set Up Like My Other Console", "Use a backup of another console as the source: its games, saves and settings, keeping this console's own startup software and identity.", "Btn", () => ShowComposerPlan("like-my-other-console")),
                ActionCard("Rebuild from My Backups", "Choose what to bring back from one or more backups of this console.", "Btn", () => ShowComposerPlan("rebuild-from-backups"))));
            sp.Children.Add(Group("Community mods"));
            sp.Children.Add(CardGrid(270, 2, ActionCard("Community Mods", "Browse mods other authors make for your " + Con + ", and install, update or remove them. You review each change, and the console is backed up first.", "Btn", () => ShowCommunity("browse"))));
            sp.Children.Add(Group("Designs, system images and packages"));
            sp.Children.Add(CardGrid(270, 2,
                ActionCard("HOME Designer", "Design Classic Cart Studio's HOME with your own pictures, colors, folders and game order.", "Btn", () => ShowDesigner()),
                ActionCard("Your System Images", "Add a system image of your own as a CCS Nucleus start choice, or remove one. It starts only when chosen.", "Btn", () => ShowFirmware()),
                ActionCard("Apply a Private Restore Package", "Put the files of a " + FileKinds.Package + " you built on the console it was built for. You review every change first, and the console is backed up before anything is written.", "Btn", () => ShowApplyPackage()),
                ActionCard(CcsBackup.Browse.SegaPictures.Title, CcsBackup.Browse.SegaPictures.Explanation + " See them and choose whether to remove them; the console is backed up first, and they can be put back.", "Btn", () => ShowSegaPictures()),
                ActionCard("Open a Shared Plan", "See what a " + FileKinds.Plan + " someone shared asks for: what you supply yourself, what is ready and what cannot be used. A plan never changes a console by itself.", "Btn", () => OpenPlan())));
            Show(sp, "composer");
        }

        // ------------------------------------------------------------------ Console Browser (check-up; Expert Mode inside)
        void ShowConsoleBrowser()
        {
            var sp = SectionHead("Console Browser", "Check your " + Con + " and look through its files while it is connected. The check-up and browsing only read the console.");
            var c = new StackPanel();
            c.Children.Add(T("The check-up shows whether the startup software is one this version recognizes, what is installed, and how much of the storage is used and free. Then browse the files by game, by type or as they are stored, and copy any of them to this PC.", 13));
            c.Children.Add(Note("This console session runs in memory, with nothing installed on your console. Workbench does not write to the console's storage in this mode.", "info"));
            sp.Children.Add(Card(c));
            if (string.IsNullOrEmpty(opt.SimulatedConsole) || !profile.ConsoleTasks) sp.Children.Add(ConnectSteps("This session only reads.", "browse"));
            sp.Children.Add(Buttons(B(string.IsNullOrEmpty(opt.SimulatedConsole) ? "Start the Check-Up" : "Start the Check-Up (Simulated)", "Btn.Primary", () => StartOp("browse", "Checking the Console", "This session only reads. Keep the USB cable connected.", ConsoleReq("browse"))),
                                    B("Expert Mode…", "Btn.Ghost", () => ShowFilesIntro("user"))));
            Show(sp, "console-browser");
        }

        Dictionary<string, object> lastCheckup;
        /// <summary>the data storage's file system condition as the file system itself records it in its header (Ext4Reader.Condition): a
        /// recorded status, not the result of a complete file system check, which Workbench does not run</summary>
        static string FileSystemCondition(string condition)
        {
            switch (condition)
            {
                case "clean": return "File system marked clean";
                case "needs-recovery": return "File system marked as needing recovery: its journal records changes that were not finished";
                case "not-cleanly-unmounted": return "File system not marked clean: it was not closed normally the last time it was used";
                case null: return "Not known: the file system could not be read";
                default: return "Not known";
            }
        }
        static string FileSystemConditionDetail(string condition) =>
            "Condition is the status the file system records in its own header (" + (condition == null ? "not read" : "recorded value: " + condition) + "). It is not a complete file system check; Workbench does not run one, and it does not repair or replay the journal.";

        void ShowCheckup(Dictionary<string, object> r, bool ended = false)
        {
            lastCheckup = r;
            var c = r.O("checkup") ?? new Dictionary<string, object>(); var s = c.O("storage") ?? new Dictionary<string, object>();
            string Gb(string k) => s.L(k) >= 0 ? Units.Gb(s.L(k)) : "unknown";   // L(): whole numbers arrive as int or long
            var sp = SectionHead("Console Check-Up", "What Workbench read from console " + c.S("console_tag") + " just now. Nothing on the console was changed.");
            var k = new StackPanel();
            var inv = c.O("installed"); var by = inv?.O("files_by_owner");
            k.Children.Add(KeyValues(("Startup software", c.S("kernel") ?? "unknown"),
                ("What is installed", by == null ? "Could not be listed." : Count(inv.L("files"), "file") + ": " + by.L("nintendo").ToString("N0", En) + " Nintendo's, " + by.L("hakchi").ToString("N0", En) + " hakchi's, " + by.L("classic_cart_studio").ToString("N0", En) + " Classic Cart Studio's, " + by.L("other").ToString("N0", En) + " from other programs or unknown."),
                ("Data storage", "Used " + Gb("used_bytes") + " of " + Gb("total_bytes") + "."),
                ("Free", Gb("free_bytes") + ", of which " + Gb("reserved_bytes") + " is reserved for the system"),
                ("Available", Gb("available_bytes")),
                ("Condition", FileSystemCondition(c.S("condition")))));
            k.Children.Add(T("These are the file system's own figures, not the size of a backup or of the whole chip.", 12.5, false, true, new Thickness(0, 4, 0, 0)));
            if (c.S("problem") != null) k.Children.Add(Note(c.S("problem"), "warn"));
            k.Children.Add(Details("Technical details", FileSystemConditionDetail(c.S("condition")), c.S("active_md5") != null ? "The startup software's MD5: " + c.S("active_md5") + (c.B("kernel_recognized") ? " (on this version's list)" : " (not on this version's list)") : null,
                "hakchi's files: " + (c.B("hakchi") ? "present" : "not found") + "; Classic Cart Studio's files: " + (c.B("classic_cart_studio") ? "present" : "not found"),
                s.L("part_bytes") > 0 ? "Data storage part: " + Units.Gb(s.L("part_bytes")) : null));
            sp.Children.Add(Card(k));
            sp.Children.Add(Note("This session runs in the console's memory, with nothing installed on your console. Workbench did not write to the console's storage.", "info"));
            bool canBrowse = !ended && r.S("state") == "checked" && worker != null && worker.Running && s.L("part_bytes") > 0;
            if (ended) sp.Children.Add(Note("The console session has ended. Start the check-up again to browse its files.", "info"));
            sp.Children.Add(Buttons(canBrowse ? B("Browse the Files", "Btn.Primary", () => OpenLiveBrowse(s.L("part_bytes"), c.S("console_tag"), c.S("kernel"), c.S("physical_model"))) : null,
                                    !ended && worker != null && worker.Running ? B("Restart the Console", "Btn.Ghost", () => worker?.Send(new JObj { { "cmd", "finish" } })) : null, B("Done", "Btn", () => Leave("Console Browser"))));
            Show(sp, "checkup");
        }

        // ------------------------------------------------------------------ Expert Mode and Developer Mode (one file session each)
        string filesScope; Dictionary<string, object> filesPoint; Dictionary<string, object> filesPlan;
        void ShowFilesIntro(string scope)
        {
            filesScope = scope;
            var sp = SectionHead(scope == "dev" ? "Developer Mode" : "Expert Mode", scope == "dev"
                ? "Change your " + Con + "'s own software: any file on its data storage (including hakchi's system overlay), its startup software and its system part. This writes to the console's storage; you review each change before it is made. Custom startup stages are not part of Developer Mode."
                : "Change your own files on your " + Con + ": add, replace or remove game files, saves, artwork and settings. This writes to the console's storage; you review each change before it is made.");
            var c = new StackPanel();
            c.Children.Add(T("Before the first change, choose a backup of this console as the session's restore point. It can be older than the console's current state: restoring returns the console to that point, and changes and saves made after it are not protected.", 13));
            c.Children.Add(T("Each change is checked before it is written and read back afterwards. Nothing else on the console is written.", 13));
            c.Children.Add(Details("Technical details", "Before a change, the file on the console must still be exactly what you reviewed. It is written under a temporary name, renamed into place, then read back and compared."));
            if (scope == "dev") c.Children.Add(Note("Developer Mode can change the console's own software. Code you add runs with full control of the console once it starts; a restore point cannot undo what that code does to the startup stages or the console's keys.", "warn"));
            sp.Children.Add(Card(c));
            if (string.IsNullOrEmpty(opt.SimulatedConsole) || !profile.ConsoleTasks) sp.Children.Add(ConnectSteps("Files change only as you confirm.", "files"));
            sp.Children.Add(Buttons(B("Start the Session", "Btn.Primary", () => { var r = ConsoleReq("files"); r.Add("scope", scope); StartOp("files", scope == "dev" ? "Developer Mode Session" : "Expert Mode Session", "Keep the USB cable connected.", r); }), B("Back", "Btn.Ghost", () => Go(scope == "dev" ? "Developer Mode" : "Console Browser"))));
            Show(sp, "files-intro");
        }

        void ShowFilesSession(string message = null, string kind = "info")
        {
            var sp = SectionHead(filesScope == "dev" ? "Developer Mode" : "Expert Mode", null);
            if (filesPoint == null)
            {
                var c = new StackPanel(); c.Children.Add(T("Choose the restore point", 15, true, false, new Thickness(0, 0, 0, 6)));
                c.Children.Add(T("Choose a backup of this console. If something goes wrong, Restore a Backup can put the console back to how it was in that backup. Changes and saves made after it are not protected.", Ty.Text, false, true));
                var list = new ComboBox();
                foreach (var e in History.List(settings).Where(x => x.Status == BackupStatus.Verified || x.Status == BackupStatus.NeedsVerification))
                    list.Items.Add(new ComboBoxItem { Content = e.When + "  ·  console " + e.ConsoleTag, Tag = e.PrimaryPath });
                if (list.Items.Count > 0) list.SelectedIndex = 0;
                c.Children.Add(Field("A backup of this console", list));
                c.Children.Add(Buttons(B("Use This Backup", "Btn.Primary", () => worker?.Send(new JObj { { "cmd", "restore-point" }, { "backup", (list.SelectedItem as ComboBoxItem)?.Tag as string } })),
                    B("Browse…", "Btn.Ghost", () => { var d = new Microsoft.Win32.OpenFileDialog { Filter = FileKinds.ConsoleBackupFilter, Title = "Choose a backup of this console as the restore point" }; if (d.ShowDialog(this) == true) worker?.Send(new JObj { { "cmd", "restore-point" }, { "backup", d.FileName } }); })));
                sp.Children.Add(Card(c));
            }
            else
            {
                sp.Children.Add(Note(filesPoint.S("banner") ?? SessionRestorePoint.NoticeText, "warn"));
                var c = new StackPanel(); c.Children.Add(T("Change a file", 15, true, false, new Thickness(0, 0, 0, 10)));
                var path = new TextBox(); System.Windows.Automation.AutomationProperties.SetName(path, "File on the console");
                c.Children.Add(Field(filesScope == "dev" ? "Path on the data storage (for example hakchi/rootfs/etc/…)" : "Path on the data storage (inside clover/profiles/, hakchi/games/ or ccs/data/)", path, null, 760));
                c.Children.Add(Buttons(
                    B("Add or Replace with a File…", "Btn", () => { var d = new Microsoft.Win32.OpenFileDialog(); if (d.ShowDialog(this) == true) worker?.Send(new JObj { { "cmd", "files-plan" }, { "changes", new List<object> { new JObj { { "op", "put" }, { "path", path.Text.Trim() }, { "from", d.FileName } } } } }); }),
                    B("Remove…", "Btn.Ghost", () => worker?.Send(new JObj { { "cmd", "files-plan" }, { "changes", new List<object> { new JObj { { "op", "del" }, { "path", path.Text.Trim() } } } } })),
                    B("Check", "Btn.Ghost", () => worker?.Send(new JObj { { "cmd", "stat" }, { "path", path.Text.Trim() } }))));
                sp.Children.Add(Card(c));
                sp.Children.Add(Buttons(B("Update Restore Point", "Btn.Ghost", () => { progress.Update("Backing up the console as it is now...", 0, 0); Show(progress.Panel, "progress"); worker?.Send(new JObj { { "cmd", "update-restore-point" } }); }),
                    filesScope == "dev" ? B("Replace the Startup Software…", "Btn.Ghost", PickKernel) : null, filesScope == "dev" ? B("Replace the System Part…", "Btn.Ghost", PickSystem) : null));
                if (filesScope == "dev") sp.Children.Add(Note(DeveloperSystem.CodeNotice, "info"));
            }
            if (message != null) sp.Children.Add(Note(message, kind));
            sp.Children.Add(Buttons(B("End the Session", "Btn.Ghost", () => { worker?.Send(new JObj { { "cmd", "end" } }); })));
            Show(sp, "files");
        }

        void ShowFilesPlan(Dictionary<string, object> p)
        {
            filesPlan = p;
            var sp = SectionHead("Review the Changes", "What would change on the console. Nothing has been written yet.");
            sp.Children.Add(Note(filesPoint?.S("banner") ?? SessionRestorePoint.NoticeText, "warn"));
            var c = new StackPanel();
            c.Children.Add(ChangeTable((p.A("changes") ?? new List<object>()).OfType<Dictionary<string, object>>().Select(x => (x.S("op") == "del" ? "Remove" : x.S("old") == "absent" ? "Add" : "Replace", x.S("path"), x.S("op") == "put" ? Units.Gb(x.L("bytes")) : ""))));
            foreach (var n in (p.A("notes") ?? new List<object>()).OfType<string>().Where(n => !n.StartsWith("Restore point:"))) c.Children.Add(T(n, 13, false, true));
            sp.Children.Add(Card(c));
            sp.Children.Add(Buttons(B("Make These Changes", "Btn.Primary", () => worker?.Send(new JObj { { "cmd", "files-confirm" }, { "digest", p.S("digest") }, { "nonce", p.S("nonce") } })), B("Cancel", "Btn.Ghost", () => ShowFilesSession())));
            Show(sp, "files-plan");
        }

        // ------------------------------------------------------------------ Developer Mode: WorkbenchDeveloper.cs

        /// <summary>the Workbench's own worker events (rescue and file sessions); the rest are handled with the backup and restore ones</summary>
        bool OnWorkbenchEvent(Dictionary<string, object> ev)
        {
            switch (ev.S("event"))
            {
                case "checkup": ShowCheckup(ev); return true;
                case "home-design-plan": ShowDesignPlan(ev.O("plan")); return true;
                case "addon-state": OnAddonState(ev); return true;
                case "method-plan": ShowMethodOrFirmwarePlan("method", ev.O("plan")); return true;
                case "firmware-plan": ShowMethodOrFirmwarePlan("firmware", ev.O("plan")); return true;
                case "rescue-diagnosis": ShowRescueChoice(ev); return true;
                case "rescue-plan": ShowRescuePlan(ev.O("plan")); return true;
                case "compose-plan": ShowComposePlan(ev.O("plan")); return true;
                case "sega-pictures-plan": ShowSegaPlan(ev.O("plan")); return true;
                case "files-ready": filesPoint = null; ShowFilesSession(); return true;
                case "restore-point": if (ev.B("ok")) filesPoint = new Dictionary<string, object> { { "banner", ev.S("banner") } }; ShowFilesSession(ev.B("ok") ? null : ev.S("problem"), ev.B("ok") ? "info" : "bad"); return true;
                case "stat": ShowFilesSession(ev.S("path") + ": " + (ev.S("state") == "absent" ? "not on the console" : ev.S("state") == "folder" ? "a folder" : ev.S("state") == "link" ? "a link" : Units.Gb(ev.L("bytes")) + ", SHA-256 " + ev.S("state"))); return true;
                case "files-plan": if (ev.B("ok")) ShowFilesPlan(ev.O("plan")); else ShowFilesSession(ev.S("problem"), "bad"); return true;
                case "files-result": ShowFilesSession(ev.S("state") == "changed" ? "Changed and read back: " + string.Join(", ", (ev.A("done") ?? new List<object>()).OfType<string>()) : ev.S("problem"), ev.S("state") == "changed" ? "ok" : "bad"); return true;
            }
            return OnCommunityEvent(ev) || OnDeveloperEvent(ev);
        }
    }
}
