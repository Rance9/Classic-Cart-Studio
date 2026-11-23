// Classic Cart Studio console backup and restore window. Copyright (C) 2026 Rance9. GPL-3.0-or-later.
// Console Composer's two backup-based starting points: Rebuild from My Backups (this console's backups) and Set Up Like My Other Console
// (another console's backup: its saves, settings, hakchi games and Classic Cart Studio data move; this console keeps its own startup
// software and identity). The files offered are those in the chosen backup's data storage under what Composer moves; nothing is guessed
// from names. The plan is made and applied in the controlled worker (ComposerEngine), under the strict policy: the console connected now is
// backed up in the same session before anything is written. Neither is a restore: a restore puts a whole backup back on the console it came
// from (Console Restore), with its own identity and startup rules.
using CcsBackup.Archive;
using CcsBackup.Engine;
using CcsBackup.Ext4;
using CcsBackup.Library;
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
        string composeKind;
        /// <summary>what the open composition review named: its kind, the console it changes and the backup made just before (for the result)</summary>
        string composePlanKind, composeTag, composeProtective;

        /// <summary>the regular files of a backup's data storage under Composer's folders, with sizes (reads only)</summary>
        static List<(string path, long size)> ComposableFiles(string backup, out string why)
        {
            why = null; var list = new List<(string, long)>();
            try
            {
                var m = History.Peek(backup);
                var dev = (m?.O("layout")?.A("partitions") ?? new List<object>()).OfType<Dictionary<string, object>>().FirstOrDefault(x => x.S("label") == "data" || x.S("label") == "rootfs_data")?.S("device");
                using (var z = ZipReader.Open(backup, BackupFormat.Allowed))
                {
                    var e = dev == null ? null : z.Find("restore/" + dev + ".img"); if (e == null || e.Method != 0) { why = "This backup has no data storage."; return list; }
                    var fs = new Ext4Reader(z.StoredWindow(e));
                    foreach (var x in fs.List())
                    {
                        if (x.Type != 'f') continue; var p = x.Path.TrimStart('/');
                        if (FilesEngine.PathOk(p) && FilesEngine.InScope("compose", p)) list.Add((p, x.Size));
                    }
                }
            }
            catch (Exception ex) { why = "This backup's files could not be listed: " + ex.Message; }
            return list;
        }

        static string ComposeGroup(string path) => path.StartsWith("clover/") ? "Saves and settings (Nintendo HOME)" : path.StartsWith("hakchi/games/") ? "Games added with hakchi" : "Classic Cart Studio's data";

        /// <summary>start: "like-my-other-console" or "rebuild-from-backups". keep: choices to show again (coming back from a package or a
        /// shared plan), with the plan name and the start method chosen then.</summary>
        void ShowComposerPlan(string start, List<(string backup, string path)> keep = null, string keepName = null, string keepPolicy = null)
        {
            settings = Reload();
            composeKind = start == "like-my-other-console" ? "migrate" : "rebuild";
            bool migrate = composeKind == "migrate";
            var sp = SectionHead(migrate ? "Set Up Like My Other Console" : "Rebuild from My Backups",
                migrate ? "Copy saves, settings, games added with hakchi and Classic Cart Studio's data from a backup of your other " + Con + " to the " + Con + " connected to this PC. This changes the connected console: you review every change first, and it is backed up before anything is written."
                        : "Put chosen files from your own backups of this " + Con + " back on it: saves and settings, games added with hakchi and Classic Cart Studio's data. This changes the console: you review every change first, and it is backed up before anything is written.");

            // what stays as it is: always on the page, never in Technical details
            var same = new StackPanel();
            same.Children.Add(T(migrate ? "This console keeps its own startup software and identity" : "Only the files you choose are written", Ty.Heading, true, false, new Thickness(0, 0, 0, 6)));
            same.Children.Add(T(migrate ? "The " + Con + " connected to this PC keeps its own startup software, bootloaders, storage layout and identity. Only the files you choose are copied to it; nothing that starts or identifies your other console moves."
                                        : "The console's startup software, bootloaders, storage layout and identity are not part of a rebuild. Every backup you choose here must be of the " + Con + " connected to this PC.", Ty.Text));
            same.Children.Add(T(migrate ? "This is not a restore. To put a whole backup back on the console it came from, use Restore a Backup in Console Restore. To bring back files from backups of the connected console itself, use Rebuild from My Backups."
                                        : "This is not a full restore. To put a whole backup back exactly as it was, use Restore a Backup in Console Restore. To bring files over from a backup of another console, use Set Up Like My Other Console.", Ty.Small, false, true, new Thickness(0)));
            sp.Children.Add(Card(same));

            List<HistoryEntry> history; string readProblem = null;
            try { history = History.List(settings).Where(x => x.Status == BackupStatus.Verified || x.Status == BackupStatus.NeedsVerification).ToList(); }
            catch (Exception ex) { history = new List<HistoryEntry>(); readProblem = ex.Message; }
            var c = new StackPanel();
            var boxes = new List<(CheckBox box, string backup, string path)>();
            var sources = new StackPanel();
            // one source: a backup and its files. Rebuild can take several backups of this console (each file from the backup chosen for
            // it; a file chosen from two backups is refused); migration takes one backup of the other console.
            // select/pre: a backup chosen before and its files checked again (coming back to this page)
            void AddSource(string select = null, HashSet<string> pre = null)
            {
                var list = new ComboBox(); System.Windows.Automation.AutomationProperties.SetName(list, "Source backup " + (sources.Children.Count + 1));
                foreach (var e in history) list.Items.Add(new ComboBoxItem { Content = e.When + "  ·  Console " + e.ConsoleTag, Tag = e.PrimaryPath });
                var files = new StackPanel { Margin = new Thickness(0, 4, 0, 0) }; var block = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
                block.Children.Add(Field(migrate ? "A backup of your other console" : sources.Children.Count == 0 ? "A backup of this console" : "Another backup of this console", list));
                block.Children.Add(new ScrollViewer { Content = files, MaxHeight = 340, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
                sources.Children.Add(block);
                var again = select == null ? null : list.Items.OfType<ComboBoxItem>().FirstOrDefault(x => string.Equals(x.Tag as string, select, StringComparison.OrdinalIgnoreCase));
                if (again != null) { list.SelectedItem = again; Load(select, files, pre); }
                list.SelectionChanged += (s, e) => Load((list.SelectedItem as ComboBoxItem)?.Tag as string, files, null);
            }
            void Load(string file, StackPanel files, HashSet<string> pre)
            {
                boxes.RemoveAll(x => files.Children.Contains(x.box)); files.Children.Clear(); if (file == null) return;
                var got = ComposableFiles(file, out var why);
                if (why != null) { files.Children.Add(Note(why, "bad")); return; }
                foreach (var g in got.GroupBy(x => ComposeGroup(x.path)))
                {
                    var all = new CheckBox { Content = new TextBlock { Text = g.Key + " (" + g.Count() + ")", FontWeight = FontWeights.SemiBold }, Margin = new Thickness(0, 10, 0, 4) }; System.Windows.Automation.AutomationProperties.SetName(all, g.Key + " (" + g.Count() + ")");
                    files.Children.Add(all); var mine = new List<CheckBox>();
                    foreach (var (path, size) in g.OrderBy(x => x.path, StringComparer.Ordinal))
                    {   // the file's place (shortened with "…" when long, whole in its tooltip) and its size in their own column
                        var row = new Grid(); row.ColumnDefinitions.Add(new ColumnDefinition()); row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(90) });
                        row.Children.Add(new TextBlock { Text = path, FontSize = 12.5, TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = path });
                        var sz = new TextBlock { Text = Units.Gb(size), FontSize = 12.5, Foreground = Res<System.Windows.Media.Brush>("B.Muted"), TextAlignment = TextAlignment.Right }; Grid.SetColumn(sz, 1); row.Children.Add(sz);
                        var b = new CheckBox { Content = row, HorizontalContentAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(20, 2, 4, 2), IsChecked = pre != null && pre.Contains(path) }; System.Windows.Automation.AutomationProperties.SetName(b, path);
                        files.Children.Add(b); boxes.Add((b, file, path)); mine.Add(b);
                    }
                    all.IsChecked = mine.Count > 0 && mine.All(x => x.IsChecked == true);
                    all.Click += (s, e) => { foreach (var b in mine) b.IsChecked = all.IsChecked; };
                }
                if (files.Children.Count == 0) files.Children.Add(T("This backup has nothing Console Composer moves.", 13, false, true));
            }
            c.Children.Add(T("What to bring", Ty.Heading, true, false, new Thickness(0, 0, 0, 6)));
            c.Children.Add(T(migrate ? "Choose a backup of your other console, then the files to copy. A group's box chooses every file in it."
                                     : "Choose a backup of this console, then the files to bring back. To take other files from another backup of it, add that backup too; each file comes from the backup you choose it in.", Ty.Small, false, true, new Thickness(0, 0, 0, 12)));
            if (readProblem != null)
            {
                c.Children.Add(Note("Your backup location could not be read, so its backups cannot be listed: " + readProblem + " If it is on a drive that is not connected, connect it and try again, or choose another location in Settings.", "bad"));
                c.Children.Add(Buttons(B("Try Again", "Btn", () => ShowComposerPlan(start, keep, keepName, keepPolicy)), B("Backup Locations", "Btn.Ghost", () => Go("Settings"))));
            }
            else if (history.Count == 0)
            {
                c.Children.Add(Note(migrate ? "There are no complete backups in your backup location yet. Connect your other console and back it up first (Back Up Console), then come back here."
                                            : "There are no complete backups in your backup location yet, so there is nothing to rebuild from. If your backups are in another folder, choose it in Settings.", "info"));
                c.Children.Add(Buttons(migrate ? B("Back Up Console", "Btn", () => { if (LeaveOk()) { Enter("Console Backup"); ShowBackup(); } }) : null, B("Backup Locations", "Btn.Ghost", () => Go("Settings"))));
            }
            else
            {
                var again = (keep ?? new List<(string backup, string path)>()).Select(x => x.backup).Distinct(StringComparer.OrdinalIgnoreCase).Take(migrate ? 1 : int.MaxValue).ToList();
                if (again.Count == 0) AddSource();
                foreach (var b in again) AddSource(b, new HashSet<string>(keep.Where(x => string.Equals(x.backup, b, StringComparison.OrdinalIgnoreCase)).Select(x => x.path), StringComparer.Ordinal));
                c.Children.Add(sources);
                if (!migrate) c.Children.Add(Buttons(B("Add Another Backup", "Btn.Ghost", () => AddSource())));
            }
            c.Children.Add(Details("Technical details",
                "Console Composer moves only files under these folders of the console's data storage: clover/profiles/ (saves and settings of Nintendo's HOME), hakchi/games/ (games added with hakchi) and ccs/ (Classic Cart Studio's data).",
                "When you review, every chosen backup is checked again in full, and each file is compared with that backup's own file list (SHA-256). Each file is then written to a new name, renamed into place and read back; the first failure stops every later file.",
                migrate ? "A backup of the connected console is refused here (it belongs in Rebuild from My Backups), and a migration takes one backup of one other console at a time."
                        : "A backup of another console is refused here (it belongs in Set Up Like My Other Console), and the same file chosen from two backups is refused."));
            sp.Children.Add(Card(c));

            List<(string backup, string path)> Chosen()
            {
                var ch = boxes.Where(x => x.box.IsChecked == true).Select(x => (x.backup, x.path)).ToList();
                if (ch.Count == 0) { Tell("Choose files first", "Choose a backup and at least one file."); return null; }
                var twice = ch.GroupBy(x => x.path).FirstOrDefault(g => g.Count() > 1);
                if (twice != null) { Tell("Chosen twice", twice.Key + " is chosen from more than one backup. Choose it from one."); return null; }
                return ch;
            }
            var share = new StackPanel(); share.Children.Add(T("Keep or share these choices (optional)", Ty.Heading, true));
            share.Children.Add(T("A " + FileKinds.Package + " holds copies of the chosen files for one console: keep it private, and apply it to that console later. A " + FileKinds.Plan + " holds only your choices, never your games, saves or console details: whoever uses it chooses their own files. Neither changes a console by itself.", Ty.Small, false, true, new Thickness(0, 0, 0, 12)));
            var planName = new TextBox { Text = keepName ?? "" }; System.Windows.Automation.AutomationProperties.SetName(planName, "Plan name");
            var policy = new ComboBox();
            foreach (var code in CcsBackup.Compose.Composition.InstallPolicies) policy.Items.Add(new ComboBoxItem { Content = CcsBackup.Compose.Composition.PolicyName(code), Tag = code, IsSelected = code == (keepPolicy ?? "ccs-mod") });
            if (policy.SelectedIndex < 0) policy.SelectedIndex = 0;
            System.Windows.Automation.AutomationProperties.SetName(policy, "Add-ons");
            share.Children.Add(FieldRow(("Plan name (for a shared plan)", planName, 300), ("Add-ons", policy, 300)));
            string PolicyTag() => (string)((ComboBoxItem)policy.SelectedItem).Tag;
            share.Children.Add(Buttons(B("Build a Private Restore Package…", "Btn", () =>
            {
                var ch = Chosen(); if (ch == null) return;
                ShowBuildPackage(start, PolicyTag(), ch, planName.Text);
            }), B("Save a Shared Composition Plan…", "Btn", () =>
            {
                var ch = Chosen(); if (ch == null) return; var name = planName.Text; var pol = PolicyTag();
                ShowSharePlan(DraftFrom(start, pol, name, ch.First().backup, ch.Select(x => x.path).ToList()), () => ShowComposerPlan(start, ch, name, pol));
            })));
            sp.Children.Add(Card(share));
            if (string.IsNullOrEmpty(opt.SimulatedConsole) || !profile.ConsoleTasks) sp.Children.Add(ConnectSteps("Nothing is written until you confirm the changes; the console is backed up first."));
            sp.Children.Add(Buttons(B("Review the Changes", "Btn.Primary", () =>
            {
                var ch = Chosen(); if (ch == null) return;
                composePlanKind = composeTag = composeProtective = null;
                var r = ConsoleReq("compose"); r.Add("kind", composeKind); r.Add("items", ch.Select(x => (object)new JObj { { "source", x.backup }, { "path", x.path } }).ToList());
                StartOp("compose", "Preparing the Review", "The connected console is identified and backed up, and every chosen backup is checked again. Nothing is written until you confirm. Keep the USB cable connected.", r);
            }), B("Back", "Btn.Ghost", () => { if (LeaveOk()) BackToSection(); })));
            Show(sp, "composer-plan");
            LeaveGuard(() => !boxes.Any(x => x.box.IsChecked == true) && planName.Text.Trim().Length == 0 ? null : "The files and plan name you chose in Console Composer",
                       "The files you chose on this page are not kept if you leave it.");
        }

        /// <summary>the review of a composition (from backups or from a private restore package): the console it changes, where the files
        /// come from, the backup made just before, and every file written</summary>
        void ShowComposePlan(Dictionary<string, object> p)
        {
            var kind = p.S("kind"); composePlanKind = kind; composeTag = p.S("console_tag");
            var ev = (p.A("evidence") ?? new List<object>()).OfType<Dictionary<string, object>>().ToList();
            var prot = ev.FirstOrDefault(x => (x.S("role") ?? "").StartsWith("backup made just now", StringComparison.Ordinal));
            composeProtective = prot?.S("path");
            var srcs = ev.Where(x => x != prot).ToList();
            var changes = (p.A("changes") ?? new List<object>()).OfType<Dictionary<string, object>>().ToList();
            var title = kind == "migrate" ? "Review: Set Up Like My Other Console" : kind == "package" ? "Review: Apply a Private Restore Package" : "Review: Rebuild from My Backups";
            var originalNote = OriginalUnavailableNote(composeTag);
            var sp = SectionHead(title, "What would change on your " + Con + ", console " + composeTag + ", the console connected to this PC now. Nothing has been written yet.");
            if (originalNote != null) sp.Children.Add(originalNote);

            // the destination and what stays: always visible
            var d = new StackPanel();
            d.Children.Add(T("The console that changes: console " + composeTag + ", connected to this PC now", Ty.Heading, true, false, new Thickness(0, 0, 0, 8)));
            string from = kind == "package" ? "Your " + FileKinds.Package + ", " + Path.GetFileName(p.O("package")?.S("path") ?? "")
                : string.Join("; ", srcs.Select(x => (kind == "migrate" ? "Your other console's backup of " : "Your backup of ") + When(x.S("completed_utc")) + (x.S("console") != null ? " (console " + x.S("console") + ")" : "")));
            d.Children.Add(KeyValues(("Files come from", from.Length == 0 ? null : from),
                                     ("Backed up first", prot == null ? "No backup made just now is named in this review" : Path.GetFileName(prot.S("path")) + ", made in this session")));
            d.Children.Add(Note(kind == "rebuild" ? "Only the files listed below are written. The console's startup software, bootloaders, storage layout and identity are not changed."
                                                  : "This console keeps its own startup software, bootloaders, storage layout and identity. Only the files listed below are written to it.", "info"));
            sp.Children.Add(Card(d));

            var c = new StackPanel();
            c.Children.Add(T("What changes on the console", Ty.Heading, true, false, new Thickness(0, 0, 0, 6)));
            int added = changes.Count(x => x.S("old") == "absent"), replaced = changes.Count - added;
            if (changes.Count > 0) c.Children.Add(T(changes.Count + (changes.Count == 1 ? " file: " : " files: ") + added + " added, " + replaced + " replaced, " + Units.Gb(changes.Sum(x => x.L("bytes"))) + " in all.", Ty.Text, false, true));
            c.Children.Add(ChangeTable(changes.Select(x => (x.S("old") == "absent" ? "Add" : "Replace", x.S("path"), Units.Gb(x.L("bytes"))))));
            foreach (var n in (p.A("skipped") ?? new List<object>()).OfType<string>()) c.Children.Add(Note("Not moved: " + n, "warn"));
            // the engine's notes (its sentence about what this console keeps is said above in the same words)
            foreach (var n in (p.A("notes") ?? new List<object>()).OfType<string>().Where(n => !n.StartsWith("This console keeps its own startup software", StringComparison.Ordinal))) c.Children.Add(T("• " + n, Ty.Text, false, true));
            if (p.S("refusal") != null) c.Children.Add(Note(p.S("refusal"), "bad"));
            var tech = new List<object>();
            foreach (var x in ev) tech.Add(Cap(x.S("role") ?? "backup") + ": " + x.S("path") + "; SHA-256 " + x.S("sha256") + "; " + Units.Gb(x.L("bytes")) + "; made " + (x.S("completed_utc") ?? "unknown") + "; console " + (x.S("console") ?? "unknown") + (x.B("simulated") ? "; simulated" : "") + "; contents digest " + x.S("coverage"));
            if (p.O("package") is Dictionary<string, object> pk) tech.Add("Private Restore Package: " + pk.S("path") + "; SHA-256 " + pk.S("sha256") + "; " + Units.Gb(pk.L("bytes")) + "; package ID " + pk.S("id"));
            foreach (var x in changes) tech.Add((x.S("old") == "absent" ? "Add " : "Replace ") + x.S("path") + ": new SHA-256 " + x.S("new_sha256") + (x.S("old") != "absent" ? ", now " + x.S("old") : ""));
            tech.Add("Each file is written to a new name, compared, renamed into place and read back; the first failure stops every later file, and nothing is retried.");
            tech.Add("Review digest " + p.S("digest") + "; the confirmation is good until " + (p.S("expires_utc") ?? "unknown") + " (UTC), once.");
            c.Children.Add(Details("Technical details", tech.ToArray()));
            sp.Children.Add(Card(c));
            if (p.S("refusal") != null || p.S("nonce") == null) { sp.Children.Add(Buttons(B("Done", "Btn", () => Leave("Console Composer")))); Show(sp, "compose-plan"); return; }
            sp.Children.Add(Buttons(B("Apply to This Console", "Btn.Primary", () =>
            {
                progress = new ProgressView(this, "Writing the Files", "Do not unplug the console. Each file is written once and read back. Stop After This File finishes the file being written, then stops.", "Stop After This File", () => { worker?.Cancel(); progress.Say("Stopping after this file..."); });
                Show(progress.Panel, "progress");
                worker?.Send(new JObj { { "cmd", "compose-confirm" }, { "digest", p.S("digest") }, { "nonce", p.S("nonce") } });
            }), B("Cancel", "Btn.Ghost", () => Leave("Console Composer"))));
            Show(sp, "compose-plan");
        }

        void ComposeResult(Dictionary<string, object> r)
        {
            var st = r.S("state"); var sp = new StackPanel();
            var done = (r.A("done") ?? new List<object>()).OfType<string>().ToList();
            var (failTitle, failText) = NotDone(st, done.Count, "file");
            string title = st == "changed" ? "Changes written and checked" : failTitle;
            sp.Children.Add(PageHead(title, st == "changed" ? "The chosen files are on your " + Con + (composeTag != null ? ", console " + composeTag : "") + ", and each one was read back." : failText));
            if (st == "changed") sp.Children.Add(Note(done.Count + (done.Count == 1 ? " file was" : " files were") + " written and read back. Restart the console to use " + (done.Count == 1 ? "it" : "them") + ".", "ok"));
            else sp.Children.Add(Note(r.S("problem") ?? "", st == "nothing-to-do" ? "info" : st == "canceled" ? "warn" : "bad"));
            if (composeProtective != null && st != "refused") { sp.Children.Add(T("The backup of the console made just before is kept:", Ty.Small, false, true)); sp.Children.Add(PathText(composeProtective)); }
            sp.Children.Add(Details("Technical details", done.Count > 0 ? "Written: " + string.Join(", ", done) : null, r.S("record") != null ? "Record: " + r.S("record") : null, "Result: " + st));
            sp.Children.Add(Buttons(B("Restart the Console", st == "changed" ? "Btn.Primary" : "Btn.Ghost", () => worker?.Send(new JObj { { "cmd", "finish" } })),
                                    B("Done", st == "changed" ? "Btn.Ghost" : "Btn", () => { worker?.Send(new JObj { { "cmd", "quit" } }); Go("Console Composer"); })));
            Show(sp, "result");
        }
    }
}
