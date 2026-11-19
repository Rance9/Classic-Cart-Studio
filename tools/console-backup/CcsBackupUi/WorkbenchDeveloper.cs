// Classic Cart Studio console backup and restore window. Copyright (C) 2026 Rance9. GPL-3.0-or-later.
// CCS Console Workbench: Developer Mode, separate from Expert Mode. Its hub, the file session's developer steps (the startup software and the
// system part, each reviewed and read back), the test boot of a startup image in the console's memory, and the guided way back to a
// restore point (Console Restore and Console Rescue, each with its own checks). Every console step runs in the controlled worker under the
// session restore point; the window never claims that code started on the console writes nothing. Each page says first what it changes;
// formats, checksums, sizes, part names and the test boot's own workings are under its Technical details, while the restore point's date
// and notice, the code warning and the destination console stay on the page.
using CcsBackup.Engine;
using CcsBackup.Library;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace CcsBackup.Ui
{
    public sealed partial class BackupWorkspace
    {
        /// <summary>the warning Developer Mode's hub shows before anything is chosen (the same facts as DeveloperSystem.CodeNotice and
        /// TestBootEngine.ImageNotice, said of the mode rather than of one file)</summary>
        const string DeveloperCodeWarning = "Code you put on the console runs with full control of it once it starts. It can write the console's storage, its startup stages or its keys, and a restore point cannot undo what it does to the startup stages or keys.";
        /// <summary>the session restore point policy in the hub's and the test boot's words (Restore, Rescue, Prepare, Return and Composer
        /// make a fresh backup in their own session instead)</summary>
        const string DeveloperPointPolicy = "Developer Mode does not make a fresh backup before a change. You choose one of this console's backups as the session's restore point, and its date stays on screen while you work. It can be older than the console's current state.";

        void ShowDeveloper()
        {
            var sp = SectionHead("Developer Mode", "Developer Mode changes your " + Con + "'s own software: its startup software, its system part and any file on its data storage. It is for experienced users who develop for the console.");
            sp.Children.Add(Note(DeveloperCodeWarning, "warn"));
            sp.Children.Add(Note(DeveloperPointPolicy + " " + SessionRestorePoint.NoticeText, "warn"));
            sp.Children.Add(CardGrid(270, 3,
                ActionCard("Files, Startup Software and System", "Change any file on the data storage, including hakchi's system overlay, or replace the startup software or the system part. Each change is reviewed, written once and read back.", "Btn.Primary", () => ShowFilesIntro("dev"), "Start a Session"),
                ActionCard("Test Boot in Memory", "Try a startup image, or your restore point's own startup software, by starting it from the console's memory. Workbench writes nothing to the console's storage for it.", "Btn", () => ShowTestBootIntro(), "Test Boot"),
                ActionCard("Return to a Restore Point", "Put the console back the way one of its backups recorded it, through Console Restore or Console Rescue and their own checks.", "Btn", () => ShowDeveloperReturn(), "Return")));
            sp.Children.Add(T("Developer Mode never writes the console's startup stages, its storage table or hakchi's spare startup slot.", Ty.Small, false, true));
            sp.Children.Add(Details(TechNes,
                "Startup software: the console's active startup image (boot2), an Android boot image holding its kernel, ramdisk and command line.",
                "System part: the console's root file system (rootfs on nandb), a SquashFS image. When the console's startup software opens it with a key, Workbench writes it through the console's own encryption: the console uses that key there, and the write does not send it to this PC. A backup's copy of the startup software holds the key too, so keep backups private.",
                "Data storage: any regular file, including hakchi/rootfs/ (hakchi's system overlay); never a link, a folder or a device.",
                "Never written in Developer Mode: the startup stages boot0 and boot1 (u-boot), the storage table, and hakchi's spare startup slot (boot2 slot 68), which holds the original kernel that Return to Original Software depends on.",
                "Test boot: the console is connected in its boot ROM's USB mode (FEL) and identified by its hardware id (SID); the image is sent once to its memory by Classic Cart Studio's loader and started from there."));
            Show(sp, "developer");
        }

        // ------------------------------------------------------------------ in the file session: the startup software and the system part
        string pickedKernel;   // the startup image file chosen for the review (shown by name; the plan itself carries its checksums)
        void PickKernel() { var d = new Microsoft.Win32.OpenFileDialog { Title = "Choose a startup image file" }; if (d.ShowDialog(this) == true) { pickedKernel = d.FileName; worker?.Send(new JObj { { "cmd", "kernel-plan" }, { "from", d.FileName } }); } }
        void PickSystem() { var d = new Microsoft.Win32.OpenFileDialog { Title = "Choose a system image file", Filter = "System images (*.hsqs;*.squashfs;*.img)|*.hsqs;*.squashfs;*.img|All files|*.*" }; if (d.ShowDialog(this) == true) worker?.Send(new JObj { { "cmd", "system-plan" }, { "from", d.FileName } }); }

        static string Banner(Dictionary<string, object> p) => (p.A("notes") ?? new List<object>()).OfType<string>().FirstOrDefault(n => n.StartsWith("Restore point:")) ?? SessionRestorePoint.NoticeText;
        void Bullets(StackPanel c, string title, IEnumerable<string> items)
        {
            var list = items.ToList(); if (list.Count == 0) return;
            if (title != null) c.Children.Add(T(title, Ty.Text, true, false, new Thickness(0, 8, 0, 4)));
            foreach (var x in list) c.Children.Add(T("• " + x, Ty.Small));
        }
        static IEnumerable<string> PlanNotes(Dictionary<string, object> p) => (p.A("notes") ?? new List<object>()).OfType<string>().Where(n => !n.StartsWith("Restore point:"));
        static List<string> CmdlineDiffs(Dictionary<string, object> p) => (p.A("cmdline_differences") ?? new List<object>()).OfType<string>().ToList();

        /// <summary>the startup command line in one plain sentence; the differences themselves are under Technical details</summary>
        static string CmdlineSummary(List<string> diffs, string against) => diffs.Count == 0 ? "Its startup command line is the same as " + against + "."
            : "Its startup command line differs from " + against + " in " + diffs.Count + (diffs.Count == 1 ? " place" : " places") + " (listed under Technical details).";

        /// <summary>the plan's restore point and its one-time confirmation, for Technical details</summary>
        static IEnumerable<string> PointDetails(Dictionary<string, object> p)
        {
            var rp = p.O("restore_point"); var a = rp?.O("archive");
            if (a?.S("path") != null) yield return "Restore point file: " + a.S("path");
            if (a?.S("sha256") != null) yield return "SHA-256 of the restore point file (checked when it was chosen): " + a.S("sha256");
            var covers = (rp?.A("covers") ?? new List<object>()).OfType<string>().Select(x => x == RestorePoints.BootStages ? "the startup stages" : x == RestorePoints.Kernel ? "the startup software" : x == RestorePoints.SystemPart ? "the system part" : x == RestorePoints.UserFiles ? "the files on the data storage" : x == RestorePoints.Layout ? "the storage table (the same as now)" : x).ToList();
            if (covers.Count > 0) yield return "The restore point can bring back: " + string.Join(", ", covers) + ".";
            if (DateTime.TryParse(p.S("expires_utc") ?? "", CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var t))
                yield return "This review can be confirmed until " + t.ToLocalTime().ToString("h:mm tt", En) + "; after that, review it again.";
        }

        void ShowKernelPlan(Dictionary<string, object> p)
        {
            var sp = SectionHead("Replace the Startup Software", "What would change on your " + Con + ": its startup software would be replaced with the startup image you chose. Nothing has been written yet.");
            sp.Children.Add(Note(filesPoint?.S("banner") ?? SessionRestorePoint.NoticeText, "warn"));
            var c = new StackPanel();
            c.Children.Add(KeyValues(("Console", p.S("console_tag")), ("New startup image", pickedKernel != null ? Path.GetFileName(pickedKernel) : null), ("Changes", "The startup software only")));
            var diffs = CmdlineDiffs(p);
            c.Children.Add(T(CmdlineSummary(diffs, "the console's current one"), Ty.Text, false, false, new Thickness(0, 8, 0, 4)));
            Bullets(c, "What it needs from the console", (p.A("dependencies") ?? new List<object>()).OfType<string>());
            foreach (var n in PlanNotes(p)) c.Children.Add(T(n, Ty.Text, false, true));
            var tech = new List<object> { pickedKernel != null ? "File: " + pickedKernel : null, "Format: Android boot image" + (p.S("ramdisk_format") != null ? ", its ramdisk compressed with " + p.S("ramdisk_format") : ""),
                "Size: " + p.L("bytes").ToString("N0", En) + " bytes (" + Units.Gb(p.L("bytes")) + ")", p.S("want_sha256") != null ? "SHA-256 of the new image: " + p.S("want_sha256") : null,
                p.S("want_md5") != null ? "MD5 of the new image: " + p.S("want_md5") : null, p.S("now_md5") != null ? "MD5 of the console's startup software now: " + p.S("now_md5") : null,
                diffs.Count > 0 ? "Command line differences: " + string.Join("; ", diffs) + "." : null,
                "Written to the active startup slot (boot2) only, once, then read back. The spare slot, the startup stages, the storage table and the data storage are not written." };
            tech.AddRange(PointDetails(p));
            c.Children.Add(Details("Technical details", tech.ToArray()));
            sp.Children.Add(Card(c));
            sp.Children.Add(Note(p.S("code_notice") ?? DeveloperSystem.CodeNotice, "warn"));
            sp.Children.Add(Buttons(B("Write the Startup Software", "Btn.Primary", () => { if (progress != null) { Show(progress.Panel, "progress"); progress.Update("Writing the startup software...", 0, 0); } worker?.Send(new JObj { { "cmd", "kernel-confirm" }, { "digest", p.S("digest") }, { "nonce", p.S("nonce") } }); }),
                                    B("Cancel", "Btn.Ghost", () => ShowFilesSession())));
            sp.Children.Add(T("Cancel goes back to the session without writing anything; the restore point stays chosen.", Ty.Small, false, true));
            Show(sp, "kernel-plan");
        }

        void ShowSystemPlan(Dictionary<string, object> p)
        {
            var sp = SectionHead("Replace the System Part", "What would change on your " + Con + ": its system part would be replaced with the system image you chose. Nothing has been written yet.");
            sp.Children.Add(Note(filesPoint?.S("banner") ?? SessionRestorePoint.NoticeText, "warn"));
            var c = new StackPanel();
            c.Children.Add(KeyValues(("Console", p.S("console_tag")), ("New system image", p.S("origin")), ("Changes", "The system part only")));
            c.Children.Add(T("Checked: it is a system for this console's board, it uses the same compression as the console's current system (the only one its startup software is known to read), and it fits in the system part.", Ty.Text, false, false, new Thickness(0, 8, 0, 4)));
            foreach (var n in PlanNotes(p)) c.Children.Add(T(n, Ty.Text, false, true));
            var tech = new List<object> { "Format: SquashFS" + (p.S("compression") != null ? ", compressed with " + p.S("compression") : "") + (p.S("board") != null ? ", made for board " + p.S("board") : ""),
                "Size: " + p.L("bytes").ToString("N0", En) + " bytes (" + Units.Gb(p.L("bytes")) + ")", p.S("want_sha256") != null ? "SHA-256 of the new image: " + p.S("want_sha256") : null,
                "System part (rootfs on nandb): " + Units.Gb(p.L("part_bytes")) + "; the current system uses " + (p.L("current_bytes_used") >= 0 ? Units.Gb(p.L("current_bytes_used")) : "an unknown amount") + (p.L("bytes_used") > 0 ? "; the new one uses " + Units.Gb(p.L("bytes_used")) : ""),
                p.S("now_md5") != null ? "MD5 of the system part now: " + p.S("now_md5") : null,
                "Written " + (p.S("encryption") == "crypt" ? "through the console's own encryption (the system part is encrypted on this console)" : "as it is (the console's system part is not encrypted)") + ", once, then read back." };
            tech.AddRange(PointDetails(p));
            c.Children.Add(Details("Technical details", tech.ToArray()));
            sp.Children.Add(Card(c));
            sp.Children.Add(Note(p.S("code_notice") ?? DeveloperSystem.CodeNotice, "warn"));
            sp.Children.Add(Buttons(B("Write the System Part", "Btn.Primary", () => { if (progress != null) { Show(progress.Panel, "progress"); progress.Update("Writing the system part...", 0, 0); } worker?.Send(new JObj { { "cmd", "system-confirm" }, { "digest", p.S("digest") }, { "nonce", p.S("nonce") } }); }),
                                    B("Cancel", "Btn.Ghost", () => ShowFilesSession())));
            sp.Children.Add(T("Cancel goes back to the session without writing anything; the restore point stays chosen.", Ty.Small, false, true));
            Show(sp, "system-plan");
        }

        // ------------------------------------------------------------------ choosing a restore point (the test boot and the way back)
        /// <summary>the complete console backups in the backup location for a restore point, with the reason when the list is empty or could
        /// not be read (the restore point's own check decides whether a backup is this console's)</summary>
        (ComboBox list, string empty) RestorePointList()
        {
            var list = new ComboBox(); string empty = null;
            try
            {
                foreach (var e in History.List(settings).Where(x => (x.Status == BackupStatus.Verified || x.Status == BackupStatus.NeedsVerification) && (x.PrimaryPath ?? x.ArchivePath) != null))
                    list.Items.Add(new ComboBoxItem { Content = (e.Simulated ? "Simulated: " : "") + e.When + "  ·  console " + e.ConsoleTag, Tag = e.PrimaryPath ?? e.ArchivePath });
                if (list.Items.Count == 0) empty = "No complete console backups are in your backup location. Choose Browse… to use one saved somewhere else, or make one with Back Up Console first.";
            }
            catch (Exception) { empty = "Your backup location could not be read. Choose Browse… to use a backup saved somewhere else, or check the location in Settings."; }
            if (list.Items.Count > 0) list.SelectedIndex = 0;
            return (list, empty);
        }
        Button BrowseRestorePoint(ComboBox list, Action chosen = null) => B("Browse…", "Btn.Ghost", () =>
        {
            var d = new Microsoft.Win32.OpenFileDialog { Filter = FileKinds.ConsoleBackupFilter, Title = "Choose a console backup as the restore point" };
            if (d.ShowDialog(this) == true) { list.Items.Insert(0, new ComboBoxItem { Content = d.FileName, Tag = d.FileName }); list.SelectedIndex = 0; chosen?.Invoke(); }
        });

        // ------------------------------------------------------------------ test boot in memory
        void ShowTestBootIntro()
        {
            var sp = SectionHead("Test Boot in Memory", "Try a startup image on your " + Con + " by starting it from the console's memory. Workbench writes nothing to the console's storage for a test boot; switching the console off ends it.");
            var c = new StackPanel();
            c.Children.Add(T("What to start", Ty.Heading, true, false, new Thickness(0, 0, 0, 8)));
            var own = new RadioButton { Content = "My restore point's own startup software", GroupName = "testboot", IsChecked = true, Margin = new Thickness(0, 4, 0, 6) };
            var file = new RadioButton { Content = "A startup image file", GroupName = "testboot", Margin = new Thickness(0, 4, 0, 10) };
            c.Children.Add(own); c.Children.Add(file);
            var path = new TextBox { IsReadOnly = true };
            c.Children.Add(Field("Startup image file", path, B("Browse…", "Btn.Ghost", () => { var d = new Microsoft.Win32.OpenFileDialog { Title = "Choose a startup image file" }; if (d.ShowDialog(this) == true) { path.Text = d.FileName; file.IsChecked = true; } })));
            c.Children.Add(T("Restore point", Ty.Heading, true, false, new Thickness(0, 6, 0, 6)));
            c.Children.Add(T("A test boot does not make a fresh backup first. You choose one of this console's backups as its restore point, and its date is shown when you review the test boot. It can be older than the console's current state. " + SessionRestorePoint.NoticeText + " It must be a complete backup: startup software, system part, files and startup stages.", Ty.Text, false, true, new Thickness(0, 0, 0, 10)));
            var (list, empty) = RestorePointList();
            var emptyLine = T(empty ?? "", Ty.Small, false, true); emptyLine.Visibility = empty == null ? Visibility.Collapsed : Visibility.Visible;
            c.Children.Add(Field("Restore point (a backup of this console)", list, BrowseRestorePoint(list, () => emptyLine.Visibility = Visibility.Collapsed)));
            c.Children.Add(emptyLine);
            c.Children.Add(Details(TechNes,
                "A startup image file must be an Android boot image in this console's own startup layout: its page size, load addresses and header version, a zImage kernel, its own checksum, a plain command line and a ramdisk format the console knows. It is checked on this PC before anything is sent.",
                "My restore point's own startup software is the startup image recorded in that backup, checked against its recorded checksums.",
                "The console waits in its boot ROM's USB mode (FEL). Workbench reads its hardware id (SID) and compares it with the restore point's console before anything is sent; the image is then sent to its memory once and started, never again on that connection."));
            sp.Children.Add(Card(c));
            sp.Children.Add(Note(TestBootEngine.ImageNotice, "warn"));
            if (!profile.ConsoleTasks || profile.TestBootRecoveryStep == null) sp.Children.Add(FamilyNotYetNote());
            else if (string.IsNullOrEmpty(opt.SimulatedConsole))
            {
                var s = new StackPanel(); s.Children.Add(T("Connect the console", Ty.Heading, true, false, new Thickness(0, 0, 0, 10)));
                s.Children.Add(Numbered("Turn your " + Con + " off and connect it to this PC with its USB cable.", profile.TestBootRecoveryStep, "Workbench reads only the console's hardware id until you confirm the test boot."));
                s.Children.Add(T("This PC needs the console's USB driver. If Windows does not have it, Workbench asks before setting it up; it never installs a driver by itself.", Ty.Small, false, true, new Thickness(0, 6, 0, 0)));
                sp.Children.Add(Card(s));
            }
            var problem = new ContentControl { Focusable = false };
            sp.Children.Add(Buttons(B("Review the Test Boot", "Btn.Primary", () =>
            {
                var rp = (list.SelectedItem as ComboBoxItem)?.Tag as string; bool img = file.IsChecked == true;
                if (string.IsNullOrEmpty(rp)) { problem.Content = Note("Choose a restore point: a backup of this console.", "bad"); return; }
                if (img && string.IsNullOrEmpty(path.Text)) { problem.Content = Note("Choose the startup image file.", "bad"); return; }
                problem.Content = null;
                var r = ConsoleReq("test-boot"); r.Add("class", img ? TestBootEngine.ImageFile : TestBootEngine.OwnStartup); r.Add("backup", rp); if (img) r.Add("image", path.Text);
                StartOp("test-boot", "Checking the Restore Point", "The console is only identified; nothing is sent to it until you confirm.", r);
            }), B("Back", "Btn.Ghost", BackToSection)));
            sp.Children.Add(problem);
            Show(sp, "test-boot");
        }

        void ShowTestBootPlan(Dictionary<string, object> p)
        {
            bool own = p.S("class") == TestBootEngine.OwnStartup;
            var sp = SectionHead("Review the Test Boot", "What Workbench would send to your " + Con + "'s memory and start. Nothing has been sent yet, and Workbench writes nothing to the console's storage for a test boot.");
            sp.Children.Add(Note(Banner(p), "warn"));
            var c = new StackPanel();
            c.Children.Add(KeyValues(("Console", p.S("console_tag")), ("What starts", p.S("origin"))));
            var diffs = CmdlineDiffs(p);
            if (!own) c.Children.Add(T(CmdlineSummary(diffs, "your restore point's startup software"), Ty.Text, false, false, new Thickness(0, 8, 0, 4)));
            Bullets(c, "What it needs from the console", (p.A("dependencies") ?? new List<object>()).OfType<string>());
            foreach (var n in PlanNotes(p)) c.Children.Add(T(n, Ty.Text, false, true));
            var tech = new List<object> { "Format: Android boot image" + (p.S("ramdisk_format") != null && p.L("ramdisk_bytes") > 0 ? ", its ramdisk compressed with " + p.S("ramdisk_format") : ""),
                "Size: " + p.L("bytes").ToString("N0", En) + " bytes (" + Units.Gb(p.L("bytes")) + ")" + (p.L("sent_bytes") > 0 ? "; " + p.L("sent_bytes").ToString("N0", En) + " bytes sent to memory with its padding" : ""),
                "Kernel: " + Units.Gb(p.L("kernel_bytes")) + "; ramdisk: " + (p.L("ramdisk_bytes") > 0 ? Units.Gb(p.L("ramdisk_bytes")) : "none"),
                p.S("sha256") != null ? "SHA-256: " + p.S("sha256") : null, p.S("md5") != null ? "MD5: " + p.S("md5") : null,
                p.S("cmdline") != null ? "Command line: " + p.S("cmdline") : null,
                diffs.Count > 0 ? "Command line differences from the restore point's startup software: " + string.Join("; ", diffs) + "." : null,
                "Sent once to the console's memory in its boot ROM's USB mode (FEL) and started there; the console's hardware id (SID) is read again before it is sent." };
            tech.AddRange(PointDetails(p));
            c.Children.Add(Details("Technical details", tech.ToArray()));
            sp.Children.Add(Card(c));
            sp.Children.Add(Note(p.S("notice"), own ? "info" : "warn"));
            sp.Children.Add(Buttons(B("Start It in Memory", "Btn.Primary", () => { Show(progress.Panel, "progress"); progress.Update("Sending the image to the console's memory...", 0, 0); worker?.Send(new JObj { { "cmd", "test-boot-confirm" }, { "digest", p.S("digest") }, { "nonce", p.S("nonce") } }); }),
                                    B("Cancel", "Btn.Ghost", () => Leave("Developer Mode"))));
            Show(sp, "test-boot-plan");
        }

        void TestBootResult(Dictionary<string, object> r)
        {
            var st = r.S("state"); var sp = new StackPanel();
            if (st == "sent")
            {
                sp.Children.Add(PageHead("Started in the console's memory", "The image is in your " + Con + "'s memory and was started. Only the TV shows whether it runs: Workbench cannot see the console from here."));
                sp.Children.Add(Note("Workbench wrote nothing to the console's storage. What the image does once it runs is up to its code. To end the test, switch the console off. If the console's storage or startup changed, use Return to a Restore Point.", "info"));
                if (r.S("record") != null) sp.Children.Add(Details("Technical details", "Record of this test boot: " + r.S("record")));
                sp.Children.Add(Buttons(B("Return to a Restore Point", "Btn", () => { BackToSection(); ShowDeveloperReturn(); }), B("Done", "Btn.Ghost", BackToSection)));
            }
            else
            {
                sp.Children.Add(PageTitle(st == "canceled" ? "Test boot canceled" : st == "unavailable" ? "Not available in this version" : "The test boot did not start"));
                sp.Children.Add(Note(r.S("problem") ?? "Nothing was sent to the console.", st == "canceled" ? "info" : "bad"));
                if (st != "unavailable") sp.Children.Add(T("Workbench wrote nothing to the console's storage.", Ty.Text, false, true));
                if (r.S("record") != null) sp.Children.Add(Details("Technical details", "Record of this test boot: " + r.S("record")));
                sp.Children.Add(Buttons(st == "unavailable" ? null : B("Try Again", "Btn", () => { BackToSection(); ShowTestBootIntro(); }), B("Done", st == "unavailable" ? "Btn" : "Btn.Ghost", BackToSection)));
            }
            Show(sp, "result");
        }

        // ------------------------------------------------------------------ the guided way back
        void ShowDeveloperReturn()
        {
            var sp = SectionHead("Return to a Restore Point", "Put your " + Con + " back the way one of its backups recorded it. This changes the console, so it goes through Console Restore, which makes a fresh backup of the console as it is now, shows exactly what changes and asks you to confirm.");
            var c = new StackPanel();
            c.Children.Add(Numbered("Choose the backup you used as the restore point.", "Workbench opens Console Restore with it. Connect the console in recovery mode when it asks.",
                "Console Restore backs up the console as it is now, then writes back the parts of the backup that differ (the startup software, the system part, the data storage) and reads each back.",
                "If the console's startup stages were changed, use Console Rescue with the same backup."));
            var (list, empty) = RestorePointList();
            var emptyLine = T(empty ?? "", Ty.Small, false, true); emptyLine.Visibility = empty == null ? Visibility.Collapsed : Visibility.Visible;
            Button open = null; var why = T("Choose a restore point above to open Console Restore.", Ty.Small, false, true); why.Visibility = list.Items.Count > 0 ? Visibility.Collapsed : Visibility.Visible;
            c.Children.Add(Field("Restore point", list, BrowseRestorePoint(list, () => { emptyLine.Visibility = Visibility.Collapsed; why.Visibility = Visibility.Collapsed; if (open != null) open.IsEnabled = true; })));
            c.Children.Add(emptyLine);
            c.Children.Add(Details(TechNes, "The startup stages are boot0 and boot1 (u-boot). Console Restore does not write them; Console Rescue writes a stage back only from this console's own backup.",
                "Console Restore checks that the console is the backup's console and that its storage table still matches the backup's before it writes anything."));
            sp.Children.Add(Card(c));
            sp.Children.Add(Note(SessionRestorePoint.NoticeText, "warn"));
            open = B("Open Console Restore", "Btn.Primary", () => { if ((list.SelectedItem as ComboBoxItem)?.Tag is string f) { Enter("Console Restore"); ShowRestoreReview(f); } }, list.Items.Count > 0);
            sp.Children.Add(Buttons(open, B("Console Rescue", "Btn", () => Go("Console Rescue")), B("Back", "Btn.Ghost", BackToSection)));
            sp.Children.Add(why);
            Show(sp, "developer-return");
        }

        /// <summary>Developer Mode's worker events (the rest are the file session's and the Workbench's)</summary>
        bool OnDeveloperEvent(Dictionary<string, object> ev)
        {
            switch (ev.S("event"))
            {
                case "kernel-plan": if (ev.B("ok")) ShowKernelPlan(ev.O("plan")); else ShowFilesSession(ev.S("problem"), "bad"); return true;
                case "system-plan": if (ev.B("ok")) ShowSystemPlan(ev.O("plan")); else ShowFilesSession(ev.S("problem"), "bad"); return true;
                case "test-boot-plan": ShowTestBootPlan(ev.O("plan")); return true;
            }
            return false;
        }
    }
}
