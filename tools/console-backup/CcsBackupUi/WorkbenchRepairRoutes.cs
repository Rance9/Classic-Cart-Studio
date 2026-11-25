// Classic Cart Studio console backup and restore window. Copyright (C) 2026 Rance9. GPL-3.0-or-later.
// Backup Repair's routes for one backup file on this PC (CcsBackup.Repair): a second copy; for a backup that does not pass its check,
// rebuilding its table of contents, merging copies of it, repairing from other backups of the same console, supplying a startup software
// file and copying out what can still be saved; recovery data (PAR2 parity) to protect a complete backup, check it, rebuild from it, and
// the one-file copy with recovery data. Every route writes a new file or folder and never changes the original; each ends with the result
// in plain words, where it was saved, and the full report under Technical details. How each route works (part finder, pieces, PAR2) is
// under Technical details too. None of this touches a console (that is Console Rescue's job, and it changes the console).
using CcsBackup.Browse;
using CcsBackup.Engine;
using CcsBackup.Library;
using CcsBackup.Repair;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace CcsBackup.Ui
{
    public sealed partial class BackupWorkspace
    {
        partial void RepairActionsFor(BackupListEntry e, List<UIElement> into)
        {
            if (!File.Exists(e.Path ?? "")) return;
            var x = e;
            into.Add(B("Copy to Another Folder…", "Btn.Ghost", () =>
            {
                var dest = PickFolder(); if (dest == null) return;
                RunRepair("Making a Second Copy", "The backup file is copied to the folder you chose, and the copy is read back and checked in full. The backup itself is not changed.", o => SecondCopy.Make(x.Path, dest, o));
            }));
            if (e.Status != BrowseStatus.Complete || e.Check == CheckState.Failed) into.Add(B("Repair", "Btn", () => ShowRepairRoutes(x)));
            if (e.Status == BrowseStatus.Complete) into.Add(B("Recovery Data", "Btn.Ghost", () => ShowParity(x)));
        }

        RepairOptions Options(CancellationTokenSource cts, ProgressView pv, string backupId) => new RepairOptions
        {
            Cancelled = () => cts.IsCancellationRequested,
            Progress = p => Dispatcher.BeginInvoke(new Action(() => pv.Update(p.Text ?? p.Phase, p.Done, p.Total))),
            RecordedSha256 = backupId == null ? null : RecordedIdentity.Find(settings, backupId)?.sha256,
            RecordedBytes = backupId == null ? -1 : RecordedIdentity.Find(settings, backupId)?.bytes ?? -1,
        };

        /// <summary>runs one repair route on this PC with its progress; its result page follows (unless the person has left the progress page)</summary>
        void RunRepair(string title, string note, Func<RepairOptions, RepairResult> work, string backupId = null)
        {
            var cts = new CancellationTokenSource();
            var pv = new ProgressView(this, title, note, "Cancel", () => cts.Cancel()); Show(pv.Panel, "progress");
            var opt0 = Options(cts, pv, backupId);
            Task.Run(() =>
            {
                try { return work(opt0); }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is RepairException || ex is InvalidDataException) { return new RepairResult { Outcome = "failed", OutputPath = null, ReportText = ex.Message }; }
            }).ContinueWith(t =>
            {
                if (page.Content != pv.Panel) { UpdateStatus(title + " finished" + (t.IsFaulted ? " with a problem." : ": " + (t.Result.Text ?? t.Result.Outcome))); return; }
                ShowRepairResult(title, t.IsFaulted ? new RepairResult { Outcome = "failed", ReportText = t.Exception.GetBaseException().Message } : t.Result);
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        /// <summary>a repair route's result: the outcome as the title, what it means in a sentence, the engine's summary, where the new file
        /// or folder was saved and where its report is; the whole report under Technical details</summary>
        void ShowRepairResult(string title, RepairResult r)
        {
            var o = r.Outcome;
            bool good = o == "repaired" || o == "copied" || o == "salvaged" || o == "not-needed";
            string head = o == "repaired" ? "Repaired copy made" : o == "copied" ? "Copy made and checked" : o == "salvaged" ? "Files copied out" : o == "not-needed" ? "Nothing needed repairing"
                : o == "canceled" ? "Canceled" : o == "failed" ? "The repair stopped with a problem" : "Not repaired";
            string first = o == "repaired" ? "A new backup file was made on this PC and passed its full check. The original backup file was only read and is unchanged."
                : o == "copied" ? "A copy of the backup file was made on this PC and read back in full: it matches. The backup itself was only read."
                : o == "salvaged" ? "Files that still match their recorded checksums were copied to a new folder on this PC. That folder is not a backup and can never be restored. The backup file was only read and is unchanged."
                : o == "not-needed" ? "The backup file needed no repair. Nothing was written."
                : o == "canceled" ? "Canceled before it finished. Nothing it was making was kept, and the backup file is unchanged."
                : "No new backup file was made. The backup file was only read and is unchanged.";
            var sp = SectionHead(head, first);
            var c = new StackPanel();
            c.Children.Add(T(title, Ty.Heading, true, false, new Thickness(0, 0, 0, 4)));
            // the engine's summary of what happened; a problem without a report (it stopped early) is described here and shown exactly below
            // (with a report, ReportText is where the report was saved, never the summary)
            var summary = r.Text ?? (r.Report != null ? null : o == "failed" ? "It stopped with a problem before it finished." : r.ReportText);
            if (summary != null) c.Children.Add(Note(summary, good ? "ok" : o == "canceled" ? "info" : "bad"));
            if (o == "failed" && r.Report == null) c.Children.Add(T("If a file is on a drive that is not connected, or is open in another program, fix that and try again. The exact problem is under Technical details.", Ty.Small, false, true));
            if (r.OutputPath != null) { c.Children.Add(T(o == "salvaged" ? "Saved in this folder:" : "Saved as:", Ty.Small, false, true, new Thickness(0, 4, 0, 2))); c.Children.Add(PathText(r.OutputPath, 13)); }
            if (r.SameDrive == "same") c.Children.Add(Note("The copy is on the same physical drive as the backup: it protects against a damaged file, not against losing the drive.", "warn"));
            else if (r.SameDrive == "unknown") c.Children.Add(Note("Whether the copy is on another physical drive could not be told.", "info"));
            if (r.RestorePoint) c.Children.Add(T("It is a complete backup that passed its full check.", 12.5, false, true));
            var rep = r.Report;
            // what is known about the new file as a whole: the same bytes as the original file (its recorded checksum matches), a new file
            // whose every part is proven, or, without a recorded checksum of the original file, not known
            if (o == "repaired" && rep?.Output != null && rep.Output.Has("identical_to_original"))
            {
                var same = rep.Output["identical_to_original"] as bool?;
                c.Children.Add(T(same == true ? "It is byte-for-byte the original backup file: its checksum equals the one recorded when the backup was made."
                    : same == false ? "Every part matches its recorded checksum, but the file as a whole is not byte-for-byte the original: it is a new file with its own checksum."
                    : "Every part matches its recorded checksum. Whether the file as a whole is byte-for-byte the original is not known: no checksum of the original whole file is recorded here.", 12.5, false, true, new Thickness(0, 2, 0, 4)));
            }
            if (rep != null && rep.Unrecoverable.Count > 0) c.Children.Add(Note(rep.Unrecoverable.Count + (rep.Unrecoverable.Count == 1 ? " part could not be recovered." : " parts could not be recovered.") + " The report below lists each one and why.", "warn"));
            if (rep != null) foreach (var u in rep.Unknowns.Where(u => !u.StartsWith("Whether the new file is byte-for-byte", StringComparison.Ordinal)).Take(3)) c.Children.Add(Note("Not known: " + u, "warn"));
            if (rep != null && r.ReportText != null) { c.Children.Add(T("Its report is saved beside it:", Ty.Small, false, true, new Thickness(0, 6, 0, 2))); c.Children.Add(PathText(r.ReportText, 12)); }
            c.Children.Add(Details("Technical details", rep != null ? rep.ToText() : r.ReportText, rep != null && r.ReportJson != null ? "The same report as JSON: " + r.ReportJson : null));
            sp.Children.Add(Card(c));
            var folder = r.OutputPath == null ? null : (Directory.Exists(r.OutputPath) ? r.OutputPath : Path.GetDirectoryName(r.OutputPath));
            var made = r.OutputPath;
            sp.Children.Add(Buttons((o == "repaired" || o == "copied") && r.RestorePoint && made != null && File.Exists(made) ? B(o == "repaired" ? "Browse the Repaired Backup" : "Browse the Copy", "Btn.Primary", () => OpenBrowse(made, false)) : null,
                folder != null && Directory.Exists(folder) ? B("Open the Folder", "Btn", () => { try { System.Diagnostics.Process.Start("explorer.exe", "\"" + folder + "\""); } catch { } }) : null, BackToFiles()));
            Show(sp, "repair-result");
        }

        void ShowRepairRoutes(BackupListEntry e)
        {
            List<BackupListEntry> all; try { all = BackupList.List(Reload()); } catch (Exception) { all = new List<BackupListEntry>(); }
            var copies = all.Where(x => x.BackupId == e.BackupId && x.Path != e.Path && File.Exists(x.Path ?? "")).ToList();
            var donors = all.Where(x => x.ConsoleTag == e.ConsoleTag && x.BackupId != e.BackupId && x.Status == BrowseStatus.Complete && File.Exists(x.Path ?? "")).ToList();
            var folder = Path.GetDirectoryName(e.Path);
            var sp = SectionHead("Repair a Backup", "Ways to make a repaired copy of this backup file on this PC. Each one writes a new file or folder; this backup and every other file are only read. A repaired backup must pass the full check before it gets its name.");
            var which = new StackPanel();
            which.Children.Add(T("Backup " + (e.MadeUtc != null ? "of " + e.MadeUtc.Value.ToLocalTime().ToString("MMMM d, yyyy 'at' h:mm tt", En) : "") + (e.ConsoleTag != null ? ", " + (e.Simulated ? "simulated console " : "console ") + e.ConsoleTag : ""), Ty.Body, true, false, new Thickness(0, 0, 0, 2)));
            which.Children.Add(PathText(e.Path, 13));
            which.Children.Add(T("New files are saved beside it, in the same folder; Copy Out asks for a folder. No console is needed. If your console no longer starts, that is Console Rescue, which changes the console.", Ty.Small, false, true, new Thickness(0, 4, 0, 0)));
            var wc = Card(which, 16); wc.Margin = new Thickness(0, 0, 0, 14); sp.Children.Add(wc);
            var c = CardGrid(280, 2);
            void Route(string title, string text, string button, Action go, bool enabled = true, string why = null)
            {   // one way to repair: what it does, why it cannot be used here (when it cannot), and its button at the bottom of the card
                var g = new Grid(); g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); g.RowDefinitions.Add(new RowDefinition()); g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                g.Children.Add(T(title, 15, true, false, new Thickness(0, 0, 0, 6)));
                var body = new StackPanel { Margin = new Thickness(0, 0, 0, 14) }; body.Children.Add(T(text, 13, false, true));
                if (!enabled && why != null) body.Children.Add(T(why, 12.5, false, false, new Thickness(0, 4, 0, 0)));
                Grid.SetRow(body, 1); g.Children.Add(body);
                var b = B(button.Trim(), "Btn", go, enabled); b.Margin = new Thickness(0); Grid.SetRow(b, 2); g.Children.Add(b);
                var card = Card(g, 18); card.Margin = new Thickness(0); c.Children.Add(card);
            }
            Route("Rebuild its table of contents", "For a backup whose end was cut off or whose list of parts is damaged: its parts are found again and each is checked against the backup's own records.", "Rebuild",
                  () => RunRepair("Rebuilding the Table of Contents", "Every part is checked against its recorded checksums; the new file is checked in full before it gets its name.", o => ZipIndexRecovery.Run(e.Path, folder, o), e.BackupId));
            Route("Merge with another copy of this backup", "When two copies are damaged in different places, the intact pieces of each make one whole backup.", "Merge " + (copies.Count == 0 ? "" : "(" + copies.Count + " other cop" + (copies.Count == 1 ? "y" : "ies") + ")"),
                  () => RunRepair("Merging Copies", "Each piece comes from a copy whose piece matches its recorded checksum.", o => Merge.Run(new[] { e.Path }.Concat(copies.Select(x => x.Path)).ToList(), folder, o), e.BackupId),
                  copies.Count > 0, "No other copy of this backup was found in your backup locations.");
            Route("Repair from other backups of this console", "Pieces that are byte-for-byte the same in another backup of this console (by their recorded checksums) fill in the damage. A newer save is never used to repair an older one.", "Repair " + (donors.Count == 0 ? "" : "(" + donors.Count + " other backup" + (donors.Count == 1 ? "" : "s") + ")"),
                  () => RunRepair("Repairing from Other Backups", "Only pieces whose checksums match the damaged backup's own records are used.", o => Donor.Run(e.Path, donors.Select(x => x.Path).ToList(), folder, o, copies.Select(x => x.Path).ToList()), e.BackupId),
                  donors.Count > 0, "No other complete backup of this console was found in your backup locations.");
            Route("Copy out what can still be saved", "When it cannot be repaired: every file whose checksum still matches is copied to a new folder you choose, with a list of what could not be. That folder is not a backup and can never be restored.", "Copy Out…", () =>
            {
                var dest = PickFolder(); if (dest == null) return;
                RunRepair("Copying Out What Can Be Saved", "Only files whose recorded checksums still match are copied.", o => { using (var s = Salvage.Survey(new[] { e.Path }.Concat(copies.Select(x => x.Path)).ToList(), o)) return Salvage.Export(s, dest, o, i => i.Exportable); }, e.BackupId);
            });
            Route("Supply a startup software file", "When the console's startup software saved in this backup is damaged or missing in every copy, and you have an exact copy of that same file on this PC, it can fill in the damage. It is used only if it is byte-for-byte the file this backup recorded.", "Supply a File…",
                  () => ShowSupply(e, copies.Select(x => x.Path).ToList()));
            if (File.Exists(Parity.IndexOf(e.Path))) Route("Rebuild from its recovery data", "This backup has recovery data beside it: the damaged blocks are rebuilt from it into a new file, as far as the intact recovery data allows.", "Rebuild from Recovery Data", () => RunParity("Rebuilding from the Recovery Data", (p, ct) => Parity.Rebuild(e.Path, p, ct)), Par2Native.Available, ParityUnavailableText);
            sp.Children.Add(c);
            sp.Children.Add(Details("Technical details",
                "Rebuild its table of contents: the parts are found through the backup's repair map (its early copy of the part list), a lenient read of a ZIP index whose part headers are damaged, or a bounded walk through the ZIP part headers from the start of the file; a \"PK\" pattern inside a part is never taken as a part. Every part must match its recorded checksum.",
                "Merge and Repair from other backups: every 1,048,576-byte piece of the new file comes from a file whose piece matches the recorded piece checksum; a part that changed between two backups has another checksum and is never used. Backups of another console, and simulated backups for a real one, are never used.",
                "Every new backup file is written as \"<name> (repaired <date>).zip.partial\", checked in full from scratch, then renamed, never over a file. A canceled or failed repair leaves no partial file.",
                "Copy out: every part and file is checked against a surviving record (the manifest, the repair map or the file list); nothing without one is copied, and no checksum is ever made up.",
                "Supply a startup software file: a file is accepted only for a startup software part the backup's own records list, only when that part is damaged or missing in every copy, and only when its size, SHA-256 and MD5 equal the record exactly.",
                File.Exists(Parity.IndexOf(e.Path)) ? "Recovery data: PAR2 parity beside the backup (" + Path.GetFileName(Parity.IndexOf(e.Path)) + " and its volume files)." : null,
                "This backup: " + e.Path + (e.BackupId != null ? " (backup ID " + e.BackupId + ")" : "")));
            sp.Children.Add(Buttons(BackToFiles()));
            Show(sp, "repair-routes");
        }

        /// <summary>Supply a startup software file: the backup's recorded startup software parts are checked first (only read), then each
        /// damaged or missing one can be given a file from this PC; the engine accepts a file only when it is byte-for-byte that part as
        /// recorded (KernelSupply) and makes a new backup file.</summary>
        void ShowSupply(BackupListEntry e, List<string> copies)
        {
            var cts = new CancellationTokenSource();
            var pv = new ProgressView(this, "Checking the Backup File", "Its startup software parts are checked against the backup's own records on this PC. Nothing is written.", "Cancel", () => cts.Cancel()); Show(pv.Panel, "progress");
            var o = new RepairOptions { Cancelled = () => cts.IsCancellationRequested, Progress = p => Dispatcher.BeginInvoke(new Action(() => pv.Update(p.Text ?? p.Phase, p.Done, p.Total))) };
            Task.Run(() => KernelSupply.Parts(e.Path, copies, o)).ContinueWith(t =>
            {
                if (page.Content != pv.Panel) return;
                ShowSupplyParts(e, copies, t.IsFaulted ? new SupplySurvey { Problem = t.Exception.GetBaseException().Message } : t.Result);
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        void ShowSupplyParts(BackupListEntry e, List<string> copies, SupplySurvey sv)
        {
            var sp = SectionHead("Supply a Startup Software File", "Fill in a damaged or missing startup software part of this backup file with an exact copy of that file from this PC. A new backup file is made; this backup and the file you choose are only read.");
            sp.Children.Add(T("A file is used only if it has exactly the size and checksum this backup recorded for that part. Workbench never supplies, searches for or downloads startup software.", Ty.Text, false, true, new Thickness(0, -8, 0, 8)));
            var at = PathText(e.Path, 13); at.Margin = new Thickness(0, 0, 0, 14); sp.Children.Add(at);
            var back = B("Back", "Btn.Ghost", () => ShowRepairRoutes(e));
            if (sv.Problem != null) { sp.Children.Add(Note(sv.Problem, "bad")); sp.Children.Add(Buttons(back)); Show(sp, "supply"); return; }
            var chosen = new Dictionary<string, TextBox>();
            foreach (var part in sv.Parts)
            {
                var c = new StackPanel(); var pt = part;
                c.Children.Add(T(part.Name, 15, true, false, new Thickness(0, 0, 0, 6)));
                c.Children.Add(KeyValues(("Condition", part.State == "intact" ? "Intact: nothing to supply" : part.State == "damaged" ? "Damaged in every copy" : "Missing from every copy"),
                    ("Size", Units.Gb(part.Size) + " (" + part.Size.ToString("N0", En) + " bytes)")));
                if (part.Needed)
                {
                    var box = new TextBox { IsReadOnly = true }; chosen[part.Member] = box;
                    c.Children.Add(Field("File to use (it must have exactly this size and the SHA-256 under Technical details)", box, B("Choose…", "Btn.Ghost", () =>
                    {
                        var chosenFile = CcsFileDialog.OpenFile(this, "Choose the file for " + pt.Name.ToLowerInvariant(), null, "All files (*.*)|*.*", "repair-part");
                        if (chosenFile != null) box.Text = chosenFile;
                    })));
                }
                c.Children.Add(Details("Technical details", "Part: " + part.Member, "SHA-256: " + part.Sha256, "Size: " + part.Size.ToString("N0", En) + " bytes"));
                sp.Children.Add(Card(c));
            }
            if (!sv.Parts.Any(p => p.Needed)) { sp.Children.Add(Note("Every startup software part of this backup is intact, so nothing needs to be supplied. The other ways to repair it are on the previous page.", "ok")); sp.Children.Add(Buttons(back)); Show(sp, "supply"); return; }
            var folder = Path.GetDirectoryName(e.Path);
            sp.Children.Add(T("The new backup file is saved beside this one, in the same folder.", Ty.Small, false, true));
            sp.Children.Add(Buttons(B("Make the Repaired Backup", "Btn.Primary", () =>
            {
                var list = chosen.Where(kv => kv.Value.Text.Length > 0).Select(kv => (kv.Key, kv.Value.Text)).ToList();
                if (list.Count == 0) { Tell("Choose a file first", "Choose a file for a damaged or missing part first."); return; }
                RunRepair("Supplying a Startup Software File", "Each file you chose is checked against this backup's own record of its part. The new backup file is checked in full before it gets its name.", o => KernelSupply.Run(e.Path, list, folder, o, copies), e.BackupId);
            }), back));
            Show(sp, "supply");
            // files chosen here are not lost silently when the person opens another page
            LeaveGuard(() => !chosen.Values.Any(b => b.Text.Length > 0) ? null : "The files you chose for a repaired backup, which is not made yet",
                       "The files you chose have not been used yet. If you leave this setup, they are not kept and the repaired backup is not made.", null, "Leave Setup");
        }

        /// <summary>why recovery data cannot be made, checked or used in this copy, in plain words (the exact reason under Technical details)</summary>
        const string ParityUnavailableText = "Recovery data cannot be made, checked or used in this copy of Workbench: the library it needs is not available here.";

        void RunParity(string title, Func<Action<string, long, long>, CancellationToken, ParityResult> work)
        {
            var cts = new CancellationTokenSource();
            var pv = new ProgressView(this, title, "The backup file is only read; anything new is written beside it on this PC.", "Cancel", () => cts.Cancel()); Show(pv.Panel, "progress");
            Task.Run(() => work((s, d, t) => Dispatcher.BeginInvoke(new Action(() => pv.Update(s, d, t))), cts.Token)).ContinueWith(t =>
            {
                var r = t.IsFaulted ? new ParityResult { Text = t.Exception.GetBaseException().Message } : t.Result;
                if (page.Content != pv.Panel) { UpdateStatus(title + " finished: " + (r.Ok ? "done." : "it did not succeed.")); return; }
                var (okTitle, notTitle) = ParityTitles(title);
                var sp = SectionHead(r.Ok ? okTitle : notTitle, (r.Ok ? "Finished on this PC." : "It did not finish.") + " The files it read are unchanged.");
                var c = new StackPanel();
                bool missing = !r.Ok && !Par2Native.Available && r.Text != null && r.Text == Par2Native.UnavailableReason;   // the library is not in this copy: said plainly, the exact reason below
                c.Children.Add(Note(t.IsFaulted ? "It stopped with a problem before it finished." : missing ? ParityUnavailableText : r.Text ?? "", r.Ok ? "ok" : "bad"));
                if (r.OutputPath != null) { c.Children.Add(T("Saved as:", Ty.Small, false, true, new Thickness(0, 4, 0, 2))); c.Children.Add(PathText(r.OutputPath, 13)); }
                c.Children.Add(Details("Technical details", t.IsFaulted || missing ? r.Text : null,
                    r.Report != null ? "Blocks: " + r.Report.SourceBlocks + " of " + r.Report.BlockSize.ToString("N0", En) + " bytes; recovery blocks: " + r.Report.RecoveryBlocks + "; recovery data " + Units.Gb(r.Report.ParityBytes) + "." : null,
                    r.RecordPath != null ? "Its settings are recorded in " + r.RecordPath : null, "Recovery data is PAR2 parity."));
                sp.Children.Add(Card(c)); sp.Children.Add(Buttons(BackToFiles("Btn")));
                Show(sp, "parity-result");
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        /// <summary>a recovery-data task's result titles: done, and not done</summary>
        static (string, string) ParityTitles(string title)
        {
            switch (title)
            {
                case "Making Recovery Data": return ("Recovery data made", "Recovery data was not made");
                case "Checking Against the Recovery Data": return ("The backup matches its recovery data", "The backup does not match its recovery data");
                case "Rebuilding from the Recovery Data": return ("Rebuilt from the recovery data", "Not rebuilt");
                case "Making a One-File Copy": return ("One-file copy made", "The one-file copy was not made");
                case "Unpacking the One-File Copy": return ("One-file copy unpacked", "The one-file copy was not unpacked");
                default: return (title + ": done", title + ": not done");
            }
        }

        void ShowParity(BackupListEntry e)
        {
            var sp = SectionHead("Recovery Data for This Backup", "Make, check or use recovery data for this backup file on this PC. Recovery data (also called parity) is extra data saved beside a complete backup: if part of the backup is damaged later, the damaged blocks can be rebuilt from it, as long as enough of the recovery data is intact.");
            { var at = PathText(e.Path, 13); at.Margin = new Thickness(0, 0, 0, 12); sp.Children.Add(at); }
            var c = new StackPanel();
            c.Children.Add(T("The backup itself is only read, and new files are written beside it. Recovery data does not help if the backup and its recovery data are both lost, for example with the drive they are on.", 13, false, true, new Thickness(0, 0, 0, 12)));
            string why = Par2Native.Available ? null : "Why it is not available: " + (Par2Native.UnavailableReason ?? "parity is not available in this copy.");
            if (why != null) c.Children.Add(Note(ParityUnavailableText, "warn"));
            var has = File.Exists(Parity.IndexOf(e.Path));
            if (has)
            {
                var files = Parity.FilesOf(e.Path);
                c.Children.Add(T("This backup has recovery data beside it (" + files.Count + (files.Count == 1 ? " file" : " files") + ").", 13, true));
                c.Children.Add(Buttons(B("Check Against the Recovery Data", "Btn", () => RunParity("Checking Against the Recovery Data", (p, ct) => Parity.Check(e.Path, p, ct)), Par2Native.Available),
                                       B("Make a One-File Copy with Recovery Data…", "Btn.Ghost", () => { var dest = PickFolder(); if (dest != null) RunParity("Making a One-File Copy", (p, ct) => Parity.MakeTransport(e.Path, dest, p, ct)); }, Par2Native.Available)));
                c.Children.Add(Details("Technical details", new object[] { why, "Recovery data is PAR2 parity: an index file and its volume files beside the backup, with its settings recorded in " + Path.GetFileName(Parity.RecordOf(e.Path)) + "." }.Concat(files.Select(f => (object)Path.GetFileName(f))).ToArray()));
            }
            else
            {
                var pct = new ComboBox { MinWidth = 140 }; foreach (var n in new[] { 5, 10, 20, 30 }) pct.Items.Add(new ComboBoxItem { Content = n + "%", Tag = n, IsSelected = n == 10 });
                System.Windows.Automation.AutomationProperties.SetName(pct, "How much recovery data");
                c.Children.Add(T("How much recovery data: about this share of the backup's size. More recovery data can rebuild more damage.", 13, false, false, new Thickness(0, 0, 0, 6)));
                c.Children.Add(FieldRow(("Recovery data", pct, 160)));
                c.Children.Add(Buttons(B("Make Recovery Data", "Btn.Primary", () => { var n = (int)((ComboBoxItem)pct.SelectedItem).Tag; RunParity("Making Recovery Data", (p, ct) => Parity.Protect(e.Path, n, p, ct)); }, Par2Native.Available)));
                c.Children.Add(Details("Technical details", why, "Recovery data is PAR2 parity, made only for a backup that passes its full check: \"" + Path.GetFileName(Parity.IndexOf(e.Path)) + "\" (the index) and its volume files beside the backup, checked at once, with its settings recorded in \"" + Path.GetFileName(Parity.RecordOf(e.Path)) + "\". It protects the backup file only."));
            }
            sp.Children.Add(Card(c));
            var u = new StackPanel(); u.Children.Add(T("Unpack a one-file copy", 15, true));
            u.Children.Add(T("A one-file copy made here holds a backup and its recovery data in one file, to move or store them. Unpacking writes them into a folder you choose, checks every part, rebuilds the backup from its recovery data when it is damaged, and runs the backup's full check.", 13, false, true));
            u.Children.Add(Buttons(B("Unpack a One-File Copy…", "Btn.Ghost", () =>
            {
                var chosenFile = CcsFileDialog.OpenFile(this, "Open a one-file copy with recovery data", null, "One-file copy with recovery data (*.zip)|*.zip", "one-file-copy"); if (chosenFile == null) return;
                var dest = PickFolder(); if (dest == null) return;
                RunParity("Unpacking the One-File Copy", (p, ct) => Parity.Unpack(chosenFile, dest, p, ct));
            })));
            u.Children.Add(Details("Technical details", "A one-file copy is an outer ZIP holding backup/<name>.zip, parity/<its .par2 files> and transport.json (each part's SHA-256 and size), all stored without compression. The outer file is not itself protected by that recovery data.", "Unpacking never writes over a file; a part damaged on the way is still unpacked so the recovery data can rebuild the backup into a new file, checked in full."));
            sp.Children.Add(Card(u));
            sp.Children.Add(Buttons(BackToFiles()));
            Show(sp, "parity");
        }
    }
}
