// Classic Cart Studio console backup and restore window. Copyright (C) 2026 Rance9. GPL-3.0-or-later.
// Backup Browser (offline, no console and no USB driver): the list of backups in the backup locations (complete, incomplete, damaged,
// salvage, missing, each with its state and last full check), Open a Backup File for one saved anywhere else on this PC, then a
// chosen backup By game (the default), By type or as stored (All files), with search, multi-select, a picture preview and export
// (CcsBackup.Browse: names made safe, nothing overwritten, every file read back against its checksum). Every view lists files as rows of
// one table: a check box, an icon and the name (indented one step a level in All Files, a folder with its arrow), the size and the date
// under a column header that stays in view while the list scrolls. A click anywhere on a file's row (its name included) selects or clears
// it, exactly once, and shows its details beside the list; the details button at the end of a row shows them without changing the
// selection. All Files names the folder or file last clicked in a bar above the list, with what can be done to it (Export This
// Folder...), and every row has the same actions on its context menu. Before anything is copied the export step offers the new folder's
// name (ExportNames: the game's title, the kind, the folder or the search), which can be changed. System images, the console's own
// system part (decrypted on the PC with the key its startup images carry), the startup images' parts and ramdisk files and the CCS HOME
// package open under their own entries, each file with where its checksum comes from (a short line on the page, the engine's full words
// and the exact checksums under Technical details). The system key is never shown. Its separate key file is exported only when it is
// itself selected; whole startup images, ramdisks and raw NAND images export as they are and can still contain the key, and the page and
// the export's result say so. A damaged backup leads to Find What Can Be Saved: Backup Repair's part finder reads it (nothing is written
// to it), then it can be browsed in place, rebuilt into a new file in a folder the person chooses, or have what still checks out copied
// out. A file that could not be opened (also one opened from another folder, not in the backup locations) offers Find What Can Be Saved
// and Repair This File for that same file; nothing is repaired by itself, and the file is never moved or added to the backup locations.
// Reading a backup is never a restore and never a new check of a console. A check of a file runs only when the person asks for it.
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
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace CcsBackup.Ui
{
    public sealed partial class BackupWorkspace
    {
        BackupContents browsing; readonly Selection browseSel = new Selection(); string browseView = "game"; Category browseCat = Category.Games; string browseSearch = "";
        /// <summary>All Files: the folders open now (kept when the page is drawn again: another view, a search, a selection change)</summary>
        readonly HashSet<string> browseOpen = new HashSet<string>(StringComparer.Ordinal);
        /// <summary>the folder or file last clicked: All Files' action bar names it and offers what can be done to it</summary>
        BrowseTarget browseCurrent;
        /// <summary>where the last export in this window went (the export step offers it again; never saved)</summary>
        string lastExportFolder;

        /// <summary>what Verified means, under every list of backups (the Dashboard, the Backup Browser, Backup Repair)</summary>
        internal const string VerifiedNote = "Verified means the whole file passed a full check: it was intact when it was checked. Not checked means no full check of it is recorded here. Neither is a test of restoring it to a console.";

        /// <summary>the window's own words about the console's system key (the engine's KeyPrivateText says the same for its records)</summary>
        const string KeyUiText = "This is the console's system key: its startup software uses it to open the encrypted system part. Its bytes are never shown here. Exporting a folder, a type, search results or a game skips this separate key file; it is exported only when you select it itself. Whole startup images, ramdisks and raw NAND images can still contain the key. Keep those exports, and any copy of the key file, private.";

        // ------------------------------------------------------------------ the list of backups
        /// <summary>the backups in the backup locations; justChecked: a file the person just checked here (its result is said at the top)</summary>
        void ShowBackupBrowser(BackupListEntry justChecked = null, DateTime checkedFrom = default(DateTime))
        {
            RememberShown();   // an open backup's view, search, open folders and chosen files are kept for Back before they are let go (WorkbenchNav.cs)
            browsing?.Dispose(); browsing = null; browseSel.Clear(); browseSearch = ""; browseOpen.Clear(); browseCurrent = null; browseDetails = null;
            settings = Reload();
            var sp = SectionHead("Backup Browser", "See the backups in your backup locations and open any of them on this PC, without a console: look through it by game, by type or as it is stored, and copy files out of it. Reading a backup never changes it or any console.");
            List<BackupListEntry> list;
            try { list = BackupList.List(settings); }
            catch (Exception ex) { sp.Children.Add(LocationsUnreadable(ex, () => ShowBackupBrowser(), true)); Show(sp, "backup-browser"); return; }
            if (list.Count == 0) { sp.Children.Add(NoBackupsYet(false, () => ShowBackupBrowser())); Show(sp, "backup-browser"); return; }
            sp.Children.Add(Buttons(B("Open a Backup File…", "Btn", OpenBackupFile), B("Backup Locations", "Btn.Ghost", () => Go("Settings"))));
            if (justChecked != null) sp.Children.Add(CheckedNote(justChecked, checkedFrom));
            sp.Children.Add(Group("Your backups", 10));
            foreach (var e in list)
            {
                var x = e;
                bool damaged = (e.Status == BrowseStatus.Damaged || e.Status == BrowseStatus.Salvage) && File.Exists(e.Path) && e.Path.EndsWith(CcsBackup.Archive.BackupFormat.Extension, StringComparison.OrdinalIgnoreCase);
                sp.Children.Add(BackupEntryCard(e, e.CanOpen ? B("Browse", "Btn", () => OpenBrowse(x.Path, false)) : null, e.CanOpenSalvage ? B("Open What Can Be Saved", "Btn", () => OpenBrowse(x.Path, true)) : null,
                    damaged ? B("Find What Can Be Saved…", e.CanOpenSalvage ? "Btn.Ghost" : "Btn", () => FindWhatCanBeSaved(x)) : null,
                    File.Exists(e.Path) ? B("Check the File", "Btn.Ghost", () => CheckBackup(x.Path)) : null, B("Open Folder", "Btn.Ghost", () => OpenFolder(x.Path))));
            }
            sp.Children.Add(T(VerifiedNote, Ty.Fine, false, true, new Thickness(0, 2, 0, 0)));
            Show(sp, "backup-browser");
        }

        /// <summary>back to the Backup Browser's list (the open backup, if any, is closed)</summary>
        void BackToList() { Enter("Backup Browser"); ShowBackupBrowser(); }

        /// <summary>the button back to the list a file task started from: the Backup Browser's or Backup Repair's</summary>
        Button BackToFiles(string style = "Btn.Ghost") => B(section == "Backup Browser" ? "Back to the Backup List" : section == "Backup Repair" ? "Back to Backup Repair" : "Back", style, BackToSection);

        /// <summary>the backup locations could not be listed: a read error with what to do, never "no backups"</summary>
        UIElement LocationsUnreadable(Exception ex, Action retry, bool browser = false)
        {
            var c = new StackPanel();
            c.Children.Add(T("Your backup locations could not be read", Ty.Heading, true));
            c.Children.Add(T("Workbench cannot list the backups in them; this does not mean you have none. If a location is on a drive that is not connected, connect it and choose Try Again, or choose another location in Settings.", Ty.Text, false, true));
            c.Children.Add(T("Backup location:", Ty.Small, false, true, new Thickness(0, 4, 0, 2))); c.Children.Add(PathText(settings.PrimaryFolder));
            if (!string.IsNullOrEmpty(settings.ArchiveFolder)) { c.Children.Add(T("Additional copy:", Ty.Small, false, true, new Thickness(0, 2, 0, 2))); c.Children.Add(PathText(settings.ArchiveFolder)); }
            c.Children.Add(Details("Technical details", ex.GetType().Name + ": " + ex.Message));
            c.Children.Add(Buttons(B("Try Again", "Btn", retry), B("Backup Locations", "Btn.Ghost", () => Go("Settings")), browser ? B("Open a Backup File…", "Btn.Ghost", OpenBackupFile) : null));
            var card = Card(c); card.BorderBrush = Res<Brush>("B.Red3"); return card;
        }

        /// <summary>the backup locations hold no backup files: how to make the first one, and how to use one already on this PC</summary>
        UIElement NoBackupsYet(bool repair, Action retry)
        {
            var c = new StackPanel();
            c.Children.Add(T("No backups yet", Ty.Heading, true));
            c.Children.Add(T(repair
                ? "Your backup locations hold no console backups, so there is nothing to check or repair here. To make your first backup, choose Back Up Console and follow its steps. A console backup saved somewhere else on this PC is listed here once it is in your backup location."
                : "Your backup locations hold no console backups. To make your first one, choose Back Up Console and follow its steps. Already have a console backup somewhere else on this PC? Open a Backup File opens it here.", Ty.Text, false, true));
            bool exists = false; try { exists = Directory.Exists(settings.PrimaryFolder); } catch (Exception) { }
            if (!exists) c.Children.Add(T("Your backup location does not exist yet; Workbench creates it with your first backup. If it is on a drive that is not connected, connect the drive and choose Try Again.", Ty.Small, false, true));
            c.Children.Add(T("Backup location:", Ty.Small, false, true, new Thickness(0, 4, 0, 2))); c.Children.Add(PathText(settings.PrimaryFolder));
            c.Children.Add(Buttons(B("Back Up Console", "Btn.Primary", () => { if (LeaveOk()) { Enter("Console Backup"); ShowBackup(); } }), repair ? null : B("Open a Backup File…", "Btn", OpenBackupFile),
                                   B("Backup Locations", "Btn.Ghost", () => Go("Settings")), exists ? null : B("Try Again", "Btn.Ghost", retry)));
            return Card(c);
        }

        /// <summary>where a file sits in the backup's records, in the Settings page's words (the list itself says "First copy", "Second copy"…)</summary>
        static string CopyWords(string copy)
        {
            switch (copy)
            {
                case null: case "File": return null;
                case "First copy": return "in your backup location";
                case "Second copy": return "additional copy";
                case "First copy (unfinished)": return "unfinished";
                case "Second copy (unfinished)": return "additional copy, unfinished";
                case "Record only": return "record only: no file";
                case "Unfinished file": return "unfinished file";
                default: return copy.ToLowerInvariant();
            }
        }

        /// <summary>the result of a file check the person just ran here: passed, did not pass, or did not finish (nothing recorded)</summary>
        UIElement CheckedNote(BackupListEntry r, DateTime from)
        {
            var which = "The backup " + (r.MadeUtc != null ? "of " + r.MadeUtc.Value.ToLocalTime().ToString("MMMM d, yyyy 'at' h:mm tt", En) + " " : "") + "was checked just now";
            bool ran = r.LastCheckUtc != null && r.LastCheckUtc.Value.ToUniversalTime() >= from.AddSeconds(-5);
            if (ran && r.Status == BrowseStatus.Complete && r.Check == CheckState.Verified)
                return Note(which + ": the file passed. Every part matches its checksums. This shows the file is intact; it does not restore anything or check a console.", "ok");
            if (ran)
                return Note(which + ": the file did not pass its check." + (r.Status == BrowseStatus.Salvage ? " Some of its files can still be saved." : "") + " Nothing was written to it. Backup Repair can make a repaired copy from it.", "bad");
            return Note(r.Detail ?? "The check did not finish, so nothing was recorded.", "warn");
        }

        // ------------------------------------------------------------------ checking a file (only when the person asks)
        void CheckBackup(string path) => CheckFile(path, (row, from) => ShowBackupBrowser(row, from));

        /// <summary>checks one backup file in full here (nothing is written to it) and opens the given list with its result</summary>
        void CheckFile(string path, Action<BackupListEntry, DateTime> then)
        {
            var from = DateTime.UtcNow;
            var pv = new ProgressView(this, "Checking the Backup File", "Every part is read back from the file on this PC and compared with its checksums. No console is needed, and nothing is written to the file.", null, null); Show(pv.Panel, "progress");
            Task.Run(() => BackupList.Check(Reload(), path, null, (d, t) => Dispatcher.BeginInvoke(new Action(() => pv.Update("Checking...", d, t))))).ContinueWith(t =>
            {
                if (page.Content != pv.Panel) { UpdateStatus("The file check finished. Its result is in the backup list."); return; }
                if (t.IsFaulted) { Show(Message("The file could not be checked", "Nothing was written to it, and nothing was recorded. If it is on a drive that is not connected, connect it and try again.", "bad", t.Exception.GetBaseException().Message), "msg"); return; }
                then(t.Result, from);
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        // ------------------------------------------------------------------ opening a backup
        void OpenBrowse(string path, bool salvage)
        {
            Enter("Backup Browser");
            var pv = new ProgressView(this, "Opening the Backup", "The backup file is checked on this PC before it is opened. Nothing is written to it, and no console is needed.", null, null); Show(pv.Panel, "progress");
            Task.Run(() => salvage ? BackupContents.OpenSalvage(path, Reload(), null, (d, t) => Dispatcher.BeginInvoke(new Action(() => pv.Update("Checking...", d, t))))
                                   : BackupContents.Open(path, Reload(), null, (d, t) => Dispatcher.BeginInvoke(new Action(() => pv.Update("Checking...", d, t))))).ContinueWith(t =>
            {
                if (page.Content != pv.Panel) { if (!t.IsFaulted) t.Result.Contents?.Dispose(); UpdateStatus("The backup finished opening after you left that page. Open it again from the Backup Browser."); return; }
                if (t.IsFaulted) { ShowOpenFailed(path, t.Exception.GetBaseException().Message, null); return; }
                var r = t.Result;
                if (!r.Ok) { ShowOpenFailed(path, r.Problem, r.Problems); return; }
                browsing?.Dispose(); browsing = r.Contents; browseSel.Clear(); browseOpen.Clear(); browseCurrent = null; browseDetails = null; browseView = "game"; browseSearch = ""; ShowBrowseOpened(path);   // as it was when opened again by Back or Forward
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        /// <summary>a backup that did not pass the check before opening: what that means, the engine's words, and what can be done with that same
        /// file, wherever it is (in the backup locations or opened from another folder): Find What Can Be Saved and Repair This File. Nothing
        /// is repaired by itself; both only read the file, and anything new is written as a new file.</summary>
        void ShowOpenFailed(string path, string problem, List<string> problems)
        {
            var sp = PageHead("This backup could not be opened", "The file did not pass the check that runs before a backup is opened, so its contents are not shown. Nothing was written to it.");
            { var at = PathText(path, 13); at.Margin = new Thickness(0, 0, 0, 12); sp.Children.Add(at); }
            sp.Children.Add(Note(problem == null ? "It is not a complete console backup." : "Part of the file does not match what was recorded when the backup was made, so the file is damaged.", "bad"));
            if (problem != null || problems?.Count > 0) sp.Children.Add(Details("Technical details", new object[] { problem }.Concat((problems ?? new List<string>()).Where(x => x != problem)).ToArray()));
            bool exists = false; try { exists = File.Exists(path); } catch (Exception) { }
            bool storage = exists && CcsBackup.External.ExternalFormat.Is(path);
            BackupListEntry row = null; bool listed = false;
            if (exists && !storage)
            {
                var s0 = Reload();
                try { row = BackupList.List(s0).FirstOrDefault(x => string.Equals(x.Path, path, StringComparison.OrdinalIgnoreCase)); listed = row != null; } catch (Exception) { }
                if (row == null) try { row = BackupList.Row(s0, path); } catch (Exception) { }
            }
            if (exists)
                sp.Children.Add(T(storage ? "Repair This File shows how a repaired copy of this USB and SD backup can be made. The file itself is never changed."
                    : "Find What Can Be Saved reads the file and shows what still matches its records: you can browse that, copy it out or rebuild a repaired copy. Repair This File shows every way to make a repaired copy. Both only read this file; anything new is written as a new file." + (listed ? "" : " The file stays where it is and is not added to your backup locations."), Ty.Text, false, true));
            sp.Children.Add(Buttons(row != null ? B("Find What Can Be Saved…", "Btn", () => FindWhatCanBeSaved(row)) : null,
                                    row != null ? B("Repair This File…", "Btn.Ghost", () => { if (!listed) repairOpened = path; ShowRepairRoutes(row); }) : storage ? B("Repair This File…", "Btn.Ghost", () => ShowStorageRepair(path)) : null,
                                    B("Back to the Backup List", "Btn.Ghost", BackToList)));
            Show(sp, "msg");
        }

        void OpenLiveBrowse(long partBytes, string tag, string state, string physicalModel = null)
        {
            var w = worker; if (w == null || !w.Running) return;
            var cts = new CancellationTokenSource();
            var pv = new ProgressView(this, "Reading the Console's Files", "The console's data storage is read through the session in its memory. Nothing on the console is changed.", "Cancel", () => cts.Cancel());
            Show(pv.Panel, "progress");
            var stream = new WorkerRangeStream(w, partBytes);
            Task.Run(() => BackupContents.OpenLive(stream, tag, state, () => cts.IsCancellationRequested, physicalModel)).ContinueWith(t =>
            {
                var r = t.Result;
                // the person opened another page meanwhile: the console's files wait for Return to Task (WorkbenchNav.cs)
                if (TaskStepAway(pv.Panel, () => { if (!r.Ok) { stream.Dispose(); Show(Message("The console's files could not be read", r.Problem ?? "", "bad"), "msg"); } else KeepLiveAway(r.Contents); })) return;
                if (!r.Ok) { stream.Dispose(); Show(Message("The console's files could not be read", r.Problem ?? "", "bad"), "msg"); return; }
                browsing?.Dispose(); browsing = r.Contents; browseSel.Clear(); browseOpen.Clear(); browseCurrent = null; browseDetails = null; browseView = "game"; browseSearch = ""; ShowBrowse();
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        /// <summary>one backup as a row: when it was made and its state, its console, size and place, its last passed file check, what the
        /// list knows about it, its file, then its buttons (the same state words as the Dashboard's)</summary>
        Border BackupEntryCard(BackupListEntry e, params UIElement[] actions)
        {
            var row = new StackPanel();
            var top = new Grid(); top.ColumnDefinitions.Add(new ColumnDefinition()); top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var who = new StackPanel { Margin = new Thickness(0, 0, 12, 0) };
            who.Children.Add(T(e.MadeUtc?.ToLocalTime().ToString("dddd, MMMM d, yyyy 'at' h:mm tt", En) ?? "Date not known", Ty.Body, true, false, new Thickness(0, 0, 0, 2)));
            var bits = new List<string>();
            if (e.ConsoleTag != null) bits.Add((e.Simulated ? "Simulated console " : "Console ") + e.ConsoleTag);
            if (e.Bytes > 0) bits.Add(Units.Gb(e.Bytes));
            var where = CopyWords(e.Copy); if (where != null) bits.Add(where);
            if (bits.Count > 0) who.Children.Add(T(string.Join("  ·  ", bits), Ty.Small, false, true, new Thickness(0)));
            // a file that is missing or unfinished says why in the list's own words; every other file says its last full check and what it
            // found (Verified only for a full check of the file as it is now)
            bool gone = e.Status == BrowseStatus.Missing || e.Status == BrowseStatus.Incomplete;
            who.Children.Add(T(gone ? e.Detail ?? LastCheckText(e) : LastCheckText(e), Ty.Small, false, true, new Thickness(0, 2, 0, 0)));
            top.Children.Add(who);
            var (state, brush) = RowState(e); var pill = Pill(state, brush); pill.VerticalAlignment = VerticalAlignment.Top; Grid.SetColumn(pill, 1); top.Children.Add(pill);
            row.Children.Add(top);
            object tech = null;
            if (e.Status == BrowseStatus.Damaged || e.Status == BrowseStatus.Salvage)
            {   // what is damaged in plain words; the check's exact words (part names, CRC-32, how the parts were found) under Technical details
                var said = new List<string>();
                if (e.IndexDamaged) said.Add("Its table of contents is damaged" + (e.FoundBy != null ? "; " + e.IntactParts.Count + " of its parts are intact and " + e.DamagedParts.Count + " damaged or missing." : ", and no trustworthy list of its parts survives."));
                else if (e.DamagedParts.Count > 0) said.Add(e.DamagedParts.Count + (e.DamagedParts.Count == 1 ? " part of the file is" : " parts of the file are") + " damaged or missing.");
                if (e.FilesVerified >= 0) said.Add(e.FilesVerified + " of the " + e.FilesChecked + " files on its data storage still match the checksums recorded when the backup was made" + (e.FilesVerified > 0 ? " and can be copied." : "."));
                else if (e.Status == BrowseStatus.Salvage) said.Add("Some of it still matches the checksums recorded when the backup was made and can be copied.");
                if (said.Count == 0) said.Add("It did not pass its last check and cannot be used as a backup.");
                row.Children.Add(T(string.Join(" ", said), Ty.Small, false, true, new Thickness(0, 6, 0, 0)));
                tech = e.Detail;
            }
            else if (!gone && e.Detail != null && e.Status != BrowseStatus.Complete) row.Children.Add(T(e.Detail, Ty.Small, false, true, new Thickness(0, 6, 0, 0)));   // a complete file's state is its check line above
            var path = PathText(e.Path, 12); path.Margin = new Thickness(0, 6, 0, 0); row.Children.Add(path);
            if (tech != null) row.Children.Add(Details("Technical details", tech, e.DamagedParts.Count > 0 ? "Damaged or missing parts: " + string.Join(", ", e.DamagedParts) : null, e.FoundBy != null ? "Its parts were found through " + e.FoundBy + "." : null));
            var acts = Buttons(actions); acts.Margin = new Thickness(0, 10, 0, -10); row.Children.Add(acts);
            var card = Card(row, 16); card.Margin = new Thickness(0, 0, 0, 10); return card;
        }

        // ------------------------------------------------------------------ an open backup (or the console's files, read live)
        void ShowBrowse()
        {
            var c = browsing; if (c == null) { ShowBackupBrowser(); return; }
            var sp = SectionHead(c.Live ? "Console Browser" : "Backup Browser", c.Live
                ? "The files on console " + c.ConsoleTag + ", read live through the session in its memory. Reading them changes nothing on the console."
                : c.Salvage
                    ? "A damaged backup file, read on this PC: only files that still match a trusted checksum can be copied out. Nothing on any console changes; this is not a restore and not a new check of the console."
                    : "You're looking through a backup file on this PC. Nothing here changes or checks a console, and this isn't a restore.");
            bool hasKey = !c.Live && c.Entries.Any(x => x.Private != null);
            if (!c.Live)
            {
                var who = new StackPanel();
                DateTime made; var when = DateTime.TryParse(c.CompletedUtc, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out made) ? made.ToLocalTime().ToString("MMMM d, yyyy 'at' h:mm tt", En) : "at a time not known";
                who.Children.Add(T((c.StorageLabel != null ? "USB and SD backup of " + c.StorageLabel : "Backup of " + (c.Simulated ? "simulated console " : "console ") + (c.ConsoleTag ?? "not known")) + ", made " + when + (c.Salvage ? "" : " · passed its full check when it was opened"), Ty.Body, true, false, new Thickness(0, 0, 0, 2)));   // a USB and SD backup: WorkbenchStorage.cs
                who.Children.Add(PathText(c.Path));
                if (hasKey) who.Children.Add(T("It holds the console's system key. Keep it private. Exporting a folder, a type, search results or a game skips the separate key file, but whole startup images, ramdisks and raw NAND images can still contain the key: keep those exports private too.", Ty.Small, false, true, new Thickness(0, 2, 0, 0)));
                who.Children.Add(Details("Technical details",
                    "File: " + c.Path,
                    c.ArchiveSha256 != null ? "SHA-256 of the file: " + c.ArchiveSha256 : null,
                    c.BackupId != null ? "Backup ID: " + c.BackupId : null,
                    c.StateText != null ? "The console's state when it was made: " + c.StateText : null,
                    c.DataMember != null ? "Data storage part: " + c.DataMember + (c.DataMemberSha256 != null ? " (SHA-256 " + c.DataMemberSha256 + ")" : "") : null,
                    c.FileSystemCondition != null ? "Data storage: " + FileSystemCondition(c.FileSystemCondition) + " (the status its own header records: " + c.FileSystemCondition + "; not a complete file system check)" : null,
                    c.DataProblem,
                    c.DamageDetail != null ? "Its full check stopped at: " + c.DamageDetail : null,
                    c.Salvage && c.FoundBy != null ? "Its parts were found through " + c.FoundBy + "." : null,
                    c.DerivedIndexPath != null ? "Checksums worked out on this PC are kept beside the backup's records, never in the backup: " + c.DerivedIndexPath : null,
                    hasKey ? "The system key is read from the backup's own startup software (a startup image's ramdisk) each time a part of the encrypted system part is read; its bytes are never shown or kept by the window. Exporting a folder, a type, search results or a game skips the separate key file (it is listed in the export's list without its bytes); selecting the key file itself exports it. Whole startup images, ramdisks and raw NAND images are exported exactly as the backup holds them, so they can still contain the key." : null,
                    (c?.DatesText ?? BackupContents.ClockText)));
                var wc = Card(who, 14); wc.Margin = new Thickness(0, -6, 0, 12); sp.Children.Add(wc);
            }
            foreach (var n in c.Notes) { if (c.Live && n.StartsWith("Read live", StringComparison.Ordinal)) continue; sp.Children.Add(Note(n, "info")); }
            // the view (a joined row of three) and the search box across the rest of the line
            var bar = new Grid { Margin = new Thickness(0, 0, 0, 12) };
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); bar.ColumnDefinitions.Add(new ColumnDefinition { MinWidth = 150 }); bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var views = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 12, 0) };
            foreach (var (v, label) in c.Live || c.StorageLabel != null ? new[] { ("game", "By Game"), ("type", "By Type"), ("all", "All Files") } : new[] { ("game", "By Game"), ("type", "By Type"), ("all", "All Files"), ("storage", "Storage") })
            { var vv = v; var b = B(label, browseView == v ? "Btn.Primary" : "Btn.Ghost", () => { browseView = vv; ShowBrowse(); }); b.Margin = new Thickness(0, 0, 4, 0); b.MinWidth = 0; views.Children.Add(b); }
            bar.Children.Add(views);
            var search = new TextBox { Text = browseSearch }; System.Windows.Automation.AutomationProperties.SetName(search, "Search"); Grid.SetColumn(search, 1); bar.Children.Add(search);
            var go = B("Search", "Btn.Ghost", () => { browseSearch = search.Text.Trim(); ShowBrowse(); }); go.Margin = new Thickness(8, 0, 0, 0); Grid.SetColumn(go, 2); bar.Children.Add(go);
            search.KeyDown += (s, ev) => { if (ev.Key == System.Windows.Input.Key.Enter) { browseSearch = search.Text.Trim(); ShowBrowse(); } };
            // the views and the search on one line when there is room for both, otherwise the search on its own line under the views
            bar.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); bar.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            void LayBar()
            {
                var room = (bar.Parent as FrameworkElement)?.ActualWidth ?? 0; if (room <= 0) return;
                bool one = room >= views.ActualWidth + views.Margin.Right + 150 + 8 + Math.Max(go.ActualWidth, 70);
                bar.ColumnDefinitions[1].MinWidth = one ? 150 : 0;
                Grid.SetRow(search, one ? 0 : 1); Grid.SetColumn(search, one ? 1 : 0); Grid.SetColumnSpan(search, one ? 1 : 2); search.Margin = new Thickness(0, one ? 0 : 8, 0, 0);
                Grid.SetRow(go, one ? 0 : 1); go.Margin = new Thickness(8, one ? 0 : 8, 0, 0);
            }
            sp.SizeChanged += (s, ev) => LayBar(); views.SizeChanged += (s, ev) => LayBar();
            if (browseView == "storage" && !c.Live && c.StorageLabel == null) { bar.Children.Remove(search); bar.Children.Remove(go); sp.Children.Add(bar); sp.Children.Add(StorageView(c)); Show(sp, "browse-storage"); return; }   // WorkbenchStorageReport.cs
            sp.Children.Add(bar);

            var preview = new StackPanel();
            preview.Children.Add(T(PreviewHint + " " + (c?.DatesText ?? BackupContents.ClockText), 12.5, false, true));
            var cols = new BrowseColumns(); drawn.Clear(); actionBar = null;
            FrameworkElement list;
            if (browseView == "game")
            {
                var listPanel = new StackPanel();
                var games = string.IsNullOrEmpty(browseSearch) ? c.Games.ToList() : c.SearchGames(browseSearch);
                if (games.Count == 0) listPanel.Children.Add(T("No games" + (string.IsNullOrEmpty(browseSearch) ? "." : " match the search."), 13, false, true));
                foreach (var g in games.Take(300))
                {
                    var gp = new StackPanel();
                    var files = g.GameFiles.Concat(g.BatterySaves).Concat(g.SuspendPoints.SelectMany(x => x.Value)).Concat(g.Artwork).Concat(g.SettingsFiles).Concat(g.OtherFiles).Concat(g.PackageCopies).Distinct().ToList();
                    var gg = g; var exp = B("Export This Game…", "Btn.Ghost", () => ExportFlow(ExportRequest.GameBundle(c, gg, null), "Export This Game")); exp.Margin = new Thickness(0); exp.VerticalAlignment = VerticalAlignment.Center;
                    System.Windows.Automation.AutomationProperties.SetHelpText(exp, "Every file of " + g.Title);
                    var head = new Grid { Margin = new Thickness(0, 0, 0, 10) }; head.ColumnDefinitions.Add(new ColumnDefinition()); head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    var title = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
                    title.Children.Add(T(g.Title + (g.TitleKnown ? "" : " (name not known)"), 14.5, true, false, new Thickness(0)));
                    title.Children.Add(T(files.Count + " file" + (files.Count == 1 ? "" : "s"), 12, false, true, new Thickness(0)));
                    head.Children.Add(title); Grid.SetColumn(exp, 1); head.Children.Add(exp);
                    gp.Children.Add(head);
                    gp.Children.Add(FileHeader(cols));
                    foreach (var e in files.Take(60)) gp.Children.Add(FileRow(e, preview, cols));
                    if (files.Count > 60) gp.Children.Add(MoreLine(files.Count - 60, -1));
                    var card = Card(gp, 14); card.Margin = new Thickness(0, 0, 0, 10); listPanel.Children.Add(card);
                    cols.Watch(card);
                }
                if (games.Count > 300) listPanel.Children.Add(T("And " + (games.Count - 300).ToString("N0", En) + " more games. Search to find one.", 12.5, false, true));
                list = new ScrollViewer { Content = listPanel, MaxHeight = 520, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Padding = new Thickness(0, 0, 6, 0) };
            }
            else if (browseView == "type")
            {
                var counts = c.Counts(); var tabs = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
                foreach (var cat in Categories.All) { var k = cat; var b = B(Categories.Label(cat) + " (" + counts[cat].count + ")", browseCat == cat ? "Btn.Primary" : "Btn.Ghost", () => { browseCat = k; ShowBrowse(); }); b.MinWidth = 0; b.Margin = new Thickness(0, 0, 6, 6); tabs.Children.Add(b); }
                var shown = string.IsNullOrEmpty(browseSearch) ? c.ByType(browseCat) : c.Search(browseSearch, c.ByType(browseCat));
                var rows = new StackPanel(); int n = 0;
                foreach (var e in shown.Take(1000)) { rows.Children.Add(FileRow(e, preview, cols)); n++; }
                if (n == 0) rows.Children.Add(T("Nothing of this type" + (string.IsNullOrEmpty(browseSearch) ? "." : " matches the search."), 13, false, true, new Thickness(0, 8, 0, 0)));
                if (shown.Count > 1000) rows.Children.Add(MoreLine(shown.Count - 1000, -1));
                var left = new StackPanel();
                left.Children.Add(Table(tabs, rows, cols));
                left.Children.Add(Buttons(B("Export All of This Type…", "Btn", () => ExportFlow(ExportRequest.WholeCategory(c, browseCat, null), "Export All of This Type")),
                    !string.IsNullOrEmpty(browseSearch) ? B("Export These Search Results…", "Btn.Ghost", () => ExportFlow(ExportRequest.SearchResults(c, browseCat, browseSearch, shown, null), "Export These Search Results")) : null));
                list = left;
            }
            else
            {
                // All Files: the backup as it is stored, as one table. A folder's row has its arrow, its icon and how many items it holds; what is
                // inside is drawn under it, one step further in, the first time it is opened. The bar above the list names the folder or file
                // last clicked and offers what can be done to it; every row has the same actions on its right-click menu.
                bool searching = !string.IsNullOrEmpty(browseSearch);
                var rows = new StackPanel();
                var abar = actionBar = new ContentControl { Focusable = false };
                Tree(c, searching ? c.Search(browseSearch) : c.Children(""), 0, rows, preview, cols, searching);
                if (rows.Children.Count == 0) rows.Children.Add(T(searching ? "Nothing matches the search." : "This backup lists no files.", 13, false, true, new Thickness(0, 8, 0, 0)));
                UpdateActionBar();
                list = Table(abar, rows, cols);
            }
            var previewCard = Card(preview, 16); previewCard.Margin = new Thickness(0, 0, 0, 10); previewCard.VerticalAlignment = VerticalAlignment.Top;
            sp.Children.Add(SideBySide(list, previewCard, 320, 860));
            // what is selected (in any view), and what can be done with it
            var picked = selectionLine = T("", Ty.Small, false, true, new Thickness(0, 4, 0, 0)); UpdateSelectionLine(); sp.Children.Add(picked);
            sp.Children.Add(Buttons(B("Export Selected…", "Btn.Primary", () => { if (browseSel.Count == 0) { Tell("Select files first", "Select the files to export first: click a file, or its check box, to select it."); return; } ExportFlow(ExportRequest.Selected(c, browseSel, null), "Export Selected Files"); }),
                                    B("Clear Selection", "Btn.Ghost", () => { browseSel.Clear(); RefreshSelection(); }), c.Live ? B("Back to the Check-Up", "Btn.Ghost", () => ShowCheckup(lastCheckup)) : B("Back to the Backup List", "Btn.Ghost", BackToList)));
            sp.Children.Add(T("Exports copy files to a new folder on this PC: you name it and choose where it goes before anything is copied. Each file is read back and compared with its checksum. Nothing in the " + (c.Live ? "console" : "backup") + " changes.", Ty.Fine, false, true, new Thickness(0, 2, 0, 0)));
            // the details shown before the page was drawn again stay shown
            var again = browseDetails != null ? c.Find(browseDetails) : null;
            if (again != null) Preview(again, preview);
            Show(sp, "browse");
        }

        /// <summary>the words over the details beside the list before a file is clicked</summary>
        const string PreviewHint = "Click a file to select it. What it is and where its checksum comes from show here, with a preview when it is a picture. The details button at the end of a row shows a file here without selecting it.";

        /// <summary>the folder or file last clicked or reached with the keyboard: what All Files' bar and a row's menu act on. Inside: the files
        /// inside Entry (a file that holds files: the data storage image, a system image, a startup image, the CCS HOME package), shown as a
        /// folder under it.</summary>
        sealed class BrowseTarget
        {
            public BrowseEntry Entry; public bool Inside;
            public string Key => (Inside ? "in:" : "") + Entry.Id;
            public bool IsFolder => Inside || Entry.Kind == EntryKind.Folder;
            public string Label => Inside ? "Files inside " + Entry.Name : Entry.Kind == EntryKind.Folder ? Entry.Name + "/" : string.IsNullOrEmpty(Entry.Name) ? Entry.DisplayPath : Entry.Name;
        }

        /// <summary>the columns of every file table on the page: the check box, the name, the size and the date. The date column is left out
        /// while the table is narrower than NarrowBelow (the date stays in a file's details), so names keep their room.</summary>
        sealed class BrowseColumns
        {
            public const double Check = 36, Size = 86, Date = 132, NarrowBelow = 600;
            public bool Narrow;
            readonly List<(ColumnDefinition col, UIElement cell)> dates = new List<(ColumnDefinition, UIElement)>();
            public ColumnDefinition DateColumn() => new ColumnDefinition { Width = new GridLength(Narrow ? 0 : Date) };
            public void Track(ColumnDefinition col, UIElement cell) { dates.Add((col, cell)); if (Narrow) cell.Visibility = Visibility.Collapsed; }
            public void Watch(FrameworkElement host)
            {
                host.SizeChanged += (s, e) =>
                {
                    bool n = e.NewSize.Width < NarrowBelow; if (n == Narrow) return; Narrow = n;
                    foreach (var (col, cell) in dates) { col.Width = new GridLength(n ? 0 : Date); cell.Visibility = n ? Visibility.Collapsed : Visibility.Visible; }
                };
            }
        }

        /// <summary>a glyph from the icon font that UI Automation does not read out (the row or button around it has the words)</summary>
        sealed class Glyph : TextBlock { protected override System.Windows.Automation.Peers.AutomationPeer OnCreateAutomationPeer() => null; }
        Glyph IconText(string glyph, double size, string brush) => new Glyph { Text = glyph, FontFamily = Res<FontFamily>("F.Icons"), FontSize = size, Foreground = Res<Brush>(brush), VerticalAlignment = VerticalAlignment.Center, TextAlignment = TextAlignment.Center };
        static string FolderGlyph(bool open) => open ? "\uE838" : "\uE8B7";
        static string ChevronGlyph(bool open) => open ? "\uE70D" : "\uE76C";
        static string FileGlyph(BrowseEntry e)
        {
            if (e.Kind == EntryKind.Link) return "\uE71B";
            if (e.Private != null) return "\uE72E";
            if (e.IsImage) return "\uEDA2";
            switch (e.Category)
            {
                case Category.Games: return "\uE7FC";
                case Category.Saves: case Category.SuspendPoints: return "\uE74E";
                case Category.Artwork: return "\uEB9F";
                case Category.Settings: return "\uE713";
                default: return "\uE7C3";
            }
        }

        /// <summary>a file table in a card: what sits above it (By Type's tabs, All Files' bar), the column header, then the rows, which scroll
        /// under the header (the header stays in view, lined up with the rows when the scroll bar shows)</summary>
        Border Table(UIElement top, UIElement rows, BrowseColumns cols)
        {
            var g = new Grid();
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            if (top != null) g.Children.Add(top);
            var head = FileHeader(cols); Grid.SetRow(head, 1); g.Children.Add(head);
            var sv = new ScrollViewer { Content = rows, MaxHeight = 460, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Focusable = false };
            void Line() => head.Margin = new Thickness(0, 0, sv.ComputedVerticalScrollBarVisibility == Visibility.Visible ? Math.Max(0, sv.ActualWidth - sv.ViewportWidth) : 0, 0);
            sv.ScrollChanged += (s, e) => Line(); sv.SizeChanged += (s, e) => Line();
            Grid.SetRow(sv, 2); g.Children.Add(sv);
            var card = Card(g, 14); card.Margin = new Thickness(0, 0, 0, 10);
            cols.Watch(card);
            return card;
        }

        /// <summary>the column names over a list of files</summary>
        Grid FileHeader(BrowseColumns cols)
        {
            var g = FileGrid(cols); g.Margin = new Thickness(0, 0, 0, 2); SetNoTerms(g, true);
            TextBlock H(string t, int col, TextAlignment a = TextAlignment.Left) { var x = new TextBlock { Text = t, FontSize = 11.5, FontWeight = FontWeights.SemiBold, Foreground = Res<Brush>("B.Muted"), TextAlignment = a, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 4, 0, 6) }; Grid.SetColumn(x, col); g.Children.Add(x); return x; }
            H("NAME", 1); H("SIZE", 2, TextAlignment.Right).Margin = new Thickness(0, 4, 14, 6);
            var d = H("DATE", 3); d.ToolTip = "The date each file has on the console, by the console's own clock (often not set)."; cols.Track(g.ColumnDefinitions[3], d);
            return g;
        }
        Grid FileGrid(BrowseColumns cols)
        {
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(BrowseColumns.Check) });
            g.ColumnDefinitions.Add(new ColumnDefinition { MinWidth = 120 });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(BrowseColumns.Size) });
            g.ColumnDefinitions.Add(cols.DateColumn());
            return g;
        }

        /// <summary>the indent of a row at this depth of the All Files tree: 20 pixels a level. One faint line, under the folder the row is in
        /// (at its arrow), ties the row to that folder; rows further in draw their own folder's line instead, so lines never stack up.</summary>
        const double IndentStep = 20;
        FrameworkElement Indent(int depth, double reach = 0)
        {
            var g = new Grid { Width = Math.Max(0, depth) * IndentStep };
            if (depth >= 1) g.Children.Add(new Border { Width = 1, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness((depth - 1) * IndentStep + 7.5, -reach, 0, -reach), Background = Res<Brush>("B.Line2") });
            return g;
        }

        /// <summary>a line at the end of a long list: how many more there are, and that a search finds them</summary>
        UIElement MoreLine(int more, int depth)
        {
            var t = T("And " + more.ToString("N0", En) + " more. Search to find them.", 12.5, false, true, new Thickness(Math.Max(0, depth) * IndentStep + BrowseColumns.Check, 8, 0, 8));
            return new Border { Child = t, BorderBrush = Res<Brush>("B.Line"), BorderThickness = new Thickness(0, 1, 0, 0) };
        }

        /// <summary>All Files: entries as rows of the table, each level one step further in. A file that holds files (the data storage image, a
        /// system image, a startup image, the CCS HOME package) has a folder "Files inside" right under it. paths: search results, out of their
        /// folders: each row shows where it is.</summary>
        void Tree(BackupContents c, IEnumerable<BrowseEntry> es, int depth, StackPanel into, StackPanel preview, BrowseColumns cols, bool paths)
        {
            var all = es as IList<BrowseEntry> ?? es.ToList();
            foreach (var e in all.Take(2000))
            {
                if (e.Kind == EntryKind.Folder) { FolderRow(c, new BrowseTarget { Entry = e }, depth, into, preview, cols, paths); continue; }
                into.Children.Add(FileRow(e, preview, cols, depth, paths));
                if (c.Children(e.Id).Count > 0) FolderRow(c, new BrowseTarget { Entry = e, Inside = true }, depth + 1, into, preview, cols, false);
            }
            if (all.Count > 2000) into.Children.Add(MoreLine(all.Count - 2000, depth));
        }

        /// <summary>a folder of the All Files tree (or the files inside a file): its arrow, icon, name and how many items it holds. Clicking it
        /// (or Enter, Space, the right and left arrow keys) opens and closes it and makes it the bar's folder; its contents are drawn the
        /// first time it opens. Its right-click menu has Open or Close and Export This Folder.</summary>
        void FolderRow(BackupContents c, BrowseTarget t, int depth, StackPanel into, StackPanel preview, BrowseColumns cols, bool paths)
        {
            var kids = c.Children(t.Entry.Id); int count = kids.Count;
            var inner = new StackPanel { Visibility = Visibility.Collapsed }; bool loaded = false;
            var g = FileGrid(cols); g.Margin = new Thickness(-2, 0, -2, 0); SetNoTerms(g, true);   // the button's 2-pixel focus edge: the columns stay in line with the files'
            var line = new DockPanel();
            var ind = Indent(depth, 2); DockPanel.SetDock(ind, Dock.Left); line.Children.Add(ind);
            var chev = IconText(ChevronGlyph(false), 10, "B.Muted"); chev.Width = 16; DockPanel.SetDock(chev, Dock.Left); line.Children.Add(chev);
            var icon = IconText(FolderGlyph(false), 15, "B.Ink2"); icon.Margin = new Thickness(4, 0, 8, 0); DockPanel.SetDock(icon, Dock.Left); line.Children.Add(icon);
            var names = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 7, 8, 7) };
            names.Children.Add(new TextBlock { Text = t.Label, FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = Res<Brush>("B.Ink"), TextTrimming = TextTrimming.CharacterEllipsis });
            var where = paths ? FolderOfEntry(t.Entry) : null;
            if (!string.IsNullOrEmpty(where)) names.Children.Add(new TextBlock { Text = where, FontSize = 11.5, Foreground = Res<Brush>("B.Muted"), TextTrimming = TextTrimming.CharacterEllipsis });
            line.Children.Add(names);
            Grid.SetColumn(line, 1); g.Children.Add(line);
            var n = new TextBlock { Text = count == 1 ? "1 item" : count.ToString("N0", CultureInfo.InvariantCulture) + " items", FontSize = 12, Foreground = Res<Brush>("B.Muted"), TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 14, 0) };
            Grid.SetColumn(n, 2); g.Children.Add(n);
            var row = new Button { Content = g, Style = Res<Style>("Wb.Plain"), HorizontalContentAlignment = HorizontalAlignment.Stretch, Padding = new Thickness(0), ToolTip = t.Entry.DisplayPath };
            void Name(bool open) => System.Windows.Automation.AutomationProperties.SetName(row, t.Label + ", folder, " + n.Text + (open ? ", open" : ", closed"));
            Name(false);
            void Open(bool open)
            {
                if (open && !loaded)
                {
                    loaded = true;
                    if (t.Inside || t.Entry.Kind == EntryKind.Folder) Tree(c, kids, depth + 1, inner, preview, cols, false);
                    if (count == 0) inner.Children.Add(new Border { BorderBrush = Res<Brush>("B.Line"), BorderThickness = new Thickness(0, 1, 0, 0), Child = T("This folder is empty.", 12.5, false, true, new Thickness((depth + 1) * IndentStep + BrowseColumns.Check + 20, 7, 0, 7)) });
                }
                inner.Visibility = open ? Visibility.Visible : Visibility.Collapsed; chev.Text = ChevronGlyph(open); icon.Text = FolderGlyph(open); Name(open);
                if (open) browseOpen.Add(t.Key); else browseOpen.Remove(t.Key);
            }
            bool IsOpen() => inner.Visibility == Visibility.Visible;
            row.Click += (s, ev) => { Open(!IsOpen()); SetCurrent(t); };
            row.KeyDown += (s, ev) =>
            {
                if (ev.Key == System.Windows.Input.Key.Right && !IsOpen()) { Open(true); ev.Handled = true; }
                else if (ev.Key == System.Windows.Input.Key.Left && IsOpen()) { Open(false); ev.Handled = true; }
            };
            row.GotKeyboardFocus += (s, ev) => SetCurrent(t);
            Menu(row, m =>
            {
                MenuLabel(m, t.Label);
                m.Items.Add(MenuItemOf(IsOpen() ? "Close" : "Open", () => { Open(!IsOpen()); SetCurrent(t); }));
                foreach (var (text, go) in ActionsFor(t)) m.Items.Add(MenuItemOf(text, go));
            });
            into.Children.Add(new Border { Child = row, BorderBrush = Res<Brush>("B.Line"), BorderThickness = new Thickness(0, 1, 0, 0) }); into.Children.Add(inner);
            if (browseOpen.Contains(t.Key)) Open(true);
        }

        /// <summary>the folder an entry is in, as people see it ("/var/lib/clover"), for search results shown out of their folders</summary>
        static string FolderOfEntry(BrowseEntry e)
        {
            var p = e?.DisplayPath; if (string.IsNullOrEmpty(p)) return null;
            var name = string.IsNullOrEmpty(e.Name) ? p : e.Name;
            return p.Length > name.Length && p.EndsWith(name, StringComparison.Ordinal) ? p.Substring(0, p.Length - name.Length).TrimEnd('/') : null;
        }

        // the check boxes drawn now, by entry id (one entry can show in several places: a game's card and By Type, a search): a change of the
        // selection shows on every one at once, each exactly once
        readonly Dictionary<string, List<(CheckBox box, Action paint)>> drawn = new Dictionary<string, List<(CheckBox, Action)>>(StringComparer.Ordinal);
        bool syncingSelection; TextBlock selectionLine; ContentControl actionBar; string browseDetails;
        static readonly Brush SelectedRow = Frozen(Color.FromArgb(0x30, 0xE4, 0x00, 0x0F));

        /// <summary>selects or clears one entry: the selection, every check box that shows it, its rows' look and the line that counts it</summary>
        void SelectEntry(BrowseEntry e, bool on)
        {
            if (syncingSelection) return;
            if (on) browseSel.Add(e); else browseSel.Remove(e);
            syncingSelection = true;
            try { if (drawn.TryGetValue(e.Id, out var l)) foreach (var (b, paint) in l) { if (b.IsChecked != on) b.IsChecked = on; paint(); } }
            finally { syncingSelection = false; }
            UpdateSelectionLine();
        }

        /// <summary>every check box drawn now shows the selection again (after Clear Selection)</summary>
        void RefreshSelection()
        {
            syncingSelection = true;
            try { foreach (var kv in drawn) { var e = browsing?.Find(kv.Key); bool on = e != null && browseSel.Contains(e); foreach (var (b, paint) in kv.Value) { if (b.IsEnabled && b.IsChecked != on) b.IsChecked = on; paint(); } } }
            finally { syncingSelection = false; }
            UpdateSelectionLine();
        }

        void UpdateSelectionLine()
        {
            var t = selectionLine; if (t == null) return;
            var c = browsing; var sel = c == null ? new List<BrowseEntry>() : browseSel.Entries(c);
            int files = sel.Count(e => e.Kind != EntryKind.Folder); long bytes = sel.Where(e => e.Kind == EntryKind.File).Sum(e => e.Size);
            t.Text = files == 0 ? "No files selected." : files.ToString("N0", En) + (files == 1 ? " file" : " files") + " selected (" + Units.Gb(bytes) + ").";
        }

        /// <summary>makes a folder or file the one All Files' bar names, with what can be done to it</summary>
        void SetCurrent(BrowseTarget t)
        {
            if (t == null || browseCurrent != null && browseCurrent.Key == t.Key) { browseCurrent = t ?? browseCurrent; return; }
            browseCurrent = t; UpdateActionBar();
        }

        /// <summary>All Files' bar above the list: the folder or file last clicked (its name and where it is) and the buttons for it, or what to
        /// do to get some</summary>
        void UpdateActionBar()
        {
            var bar = actionBar; if (bar == null) return;
            var box = new Border { Background = Res<Brush>("B.Surface2"), CornerRadius = new CornerRadius(6), Padding = new Thickness(12, 8, 8, 8), Margin = new Thickness(0, 0, 0, 10), MinHeight = 50 };
            var t = browseCurrent; var c = browsing;
            if (t == null || c == null || c.Find(t.Entry.Id) == null)
            {
                var hint = T("Click a folder or a file to see what you can do with it here. Right-click any row for the same actions.", Ty.Small, false, true, new Thickness(0)); hint.VerticalAlignment = VerticalAlignment.Center;
                box.Child = hint; bar.Content = box; return;
            }
            var g = new Grid(); g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); g.ColumnDefinitions.Add(new ColumnDefinition { MinWidth = 80 }); g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var icon = IconText(t.IsFolder ? FolderGlyph(false) : FileGlyph(t.Entry), 16, "B.Ink2"); icon.Margin = new Thickness(0, 0, 10, 0); g.Children.Add(icon);
            var words = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) }; SetNoTerms(words, true);
            words.Children.Add(new TextBlock { Text = (t.IsFolder ? "Folder: " : "File: ") + t.Label, FontSize = 13, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = t.Entry.DisplayPath });
            words.Children.Add(new TextBlock { Text = t.Inside ? "Everything inside " + t.Entry.DisplayPath : t.Entry.DisplayPath, FontSize = 11.5, Foreground = Res<Brush>("B.Muted"), TextTrimming = TextTrimming.CharacterEllipsis });
            Grid.SetColumn(words, 1); g.Children.Add(words);
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            foreach (var (text, go) in ActionsFor(t)) { var b = B(text, "Btn", go); b.Margin = new Thickness(6, 0, 0, 0); b.MinHeight = 32; b.MinWidth = 0; System.Windows.Automation.AutomationProperties.SetHelpText(b, t.Entry.DisplayPath); buttons.Children.Add(b); }
            if (buttons.Children.Count == 0) buttons.Children.Add(T(c.Live ? "Select it to export it with Export Selected." : "Nothing to export here.", Ty.Small, false, true, new Thickness(0)));
            Grid.SetColumn(buttons, 2); g.Children.Add(buttons);
            box.Child = g; bar.Content = box;
        }

        /// <summary>what can be done to a folder or file in All Files' bar and in its row's menu (the export is offered only where it was before:
        /// a file's own export only from a backup, a folder's also from a console read live)</summary>
        List<(string text, Action go)> ActionsFor(BrowseTarget t)
        {
            var c = browsing; var l = new List<(string, Action)>();
            if (c == null || t?.Entry == null) return l;
            if (t.Inside) l.Add(("Export This Folder…", () => ExportFlow(ExportRequest.Inside(c, t.Entry, null), "Export This Folder")));
            else if (t.Entry.Kind == EntryKind.Folder) l.Add(("Export This Folder…", () => ExportFlow(ExportRequest.Folder(c, t.Entry, null), "Export This Folder")));
            else if (t.Entry.Kind == EntryKind.File && t.Entry.Unreadable == null && !c.Live) { var text = ExportOneText(t.Entry); l.Add((text, () => ExportFlow(ExportRequest.One(c, t.Entry, null), text.TrimEnd('…')))); }
            return l;
        }
        static string ExportOneText(BrowseEntry e) => e.Private != null ? "Export the Key File…" : e.IsImage ? "Export This Image…" : "Export This File…";

        // ------------------------------------------------------------------ a row's right-click menu (also Shift+F10 and the menu key)
        static ControlTemplate menuTemplate, menuItemTemplate, menuSeparatorTemplate;
        /// <summary>gives an element a menu in the Workbench's look, filled each time it opens (so it shows the row as it is then)</summary>
        void Menu(FrameworkElement on, Action<ContextMenu> fill)
        {
            if (menuTemplate == null)
            {
                const string ns = "xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" xmlns:x=\"http://schemas.microsoft.com/winfx/2006/xaml\"";
                menuTemplate = (ControlTemplate)System.Windows.Markup.XamlReader.Parse("<ControlTemplate " + ns + " TargetType=\"ContextMenu\"><Border Background=\"{TemplateBinding Background}\" BorderBrush=\"{TemplateBinding BorderBrush}\" BorderThickness=\"1\" CornerRadius=\"6\" Padding=\"4\"><StackPanel IsItemsHost=\"True\" KeyboardNavigation.DirectionalNavigation=\"Cycle\"/></Border></ControlTemplate>");
                menuItemTemplate = (ControlTemplate)System.Windows.Markup.XamlReader.Parse("<ControlTemplate " + ns + " TargetType=\"MenuItem\"><Border x:Name=\"bd\" Background=\"Transparent\" CornerRadius=\"4\" Padding=\"12,7,20,7\" MinWidth=\"190\"><ContentPresenter ContentSource=\"Header\" RecognizesAccessKey=\"False\"/></Border><ControlTemplate.Triggers><Trigger Property=\"IsHighlighted\" Value=\"True\"><Setter TargetName=\"bd\" Property=\"Background\" Value=\"#26FFFFFF\"/></Trigger><Trigger Property=\"IsEnabled\" Value=\"False\"><Setter Property=\"Opacity\" Value=\"0.45\"/></Trigger></ControlTemplate.Triggers></ControlTemplate>");
                menuSeparatorTemplate = (ControlTemplate)System.Windows.Markup.XamlReader.Parse("<ControlTemplate " + ns + " TargetType=\"Separator\"><Border Height=\"1\" Margin=\"8,4,8,4\" Background=\"#30FFFFFF\"/></ControlTemplate>");
            }
            var m = new ContextMenu { Template = menuTemplate, Background = Res<Brush>("B.Surface2"), BorderBrush = Res<Brush>("B.Line2"), Foreground = Res<Brush>("B.Ink"), FontFamily = Res<FontFamily>("F.Ui"), FontSize = 13, HasDropShadow = false };
            on.ContextMenu = m;
            on.ContextMenuOpening += (s, e) => { m.Items.Clear(); fill(m); };
        }
        /// <summary>the menu's first line: the name of what it acts on (not an action: the keyboard and the mouse pass over it)</summary>
        void MenuLabel(ContextMenu m, string text)
        {
            m.Items.Add(new MenuItem { Header = text, Template = menuItemTemplate, Foreground = Res<Brush>("B.Muted"), FontWeight = FontWeights.SemiBold, Focusable = false, IsHitTestVisible = false });
            m.Items.Add(new Separator { Template = menuSeparatorTemplate });
        }
        MenuItem MenuItemOf(string text, Action go) { var mi = new MenuItem { Header = text, Template = menuItemTemplate, Foreground = Res<Brush>("B.Ink") }; mi.Click += (s, e) => go(); return mi; }

        /// <summary>one file as a row of the table: its check box, its icon, its name (with its folder under it outside All Files, and its state
        /// when it cannot be copied), the details button, its size and its date. A click anywhere on the row selects or clears it, exactly
        /// once (a click on the check box itself is the check box's own), and shows its details; the details button shows them without
        /// changing the selection. Space on the focused check box selects or clears it, Enter shows its details, and the right-click menu
        /// has Select, Show Details and the file's export. In the All Files tree (depth 0 or more) the name is indented to its level.</summary>
        FrameworkElement FileRow(BrowseEntry e, StackPanel preview, BrowseColumns cols, int depth = -1, bool paths = false)
        {
            var g = FileGrid(cols); SetNoTerms(g, true);   // a backup's own file names and folders
            // a file that cannot be copied (damaged, or not checkable in a damaged backup) is shown with its state and cannot be selected
            bool blocked = e.Kind == EntryKind.File && e.Unreadable != null;
            var name = string.IsNullOrEmpty(e.Name) ? e.DisplayPath : e.Name;
            var cb = new CheckBox { IsChecked = browseSel.Contains(e) && !blocked, IsEnabled = !blocked, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(8, 0, 0, 0) };
            System.Windows.Automation.AutomationProperties.SetName(cb, "Select " + e.DisplayPath);
            System.Windows.Automation.AutomationProperties.SetHelpText(cb, "Space selects or clears it. Enter shows its details.");
            g.Children.Add(cb);
            var line = new DockPanel();
            if (depth >= 0)
            {   // the tree's indent, then the place of a folder's arrow, so a file's icon lines up with its folders' icons
                var ind = Indent(depth); DockPanel.SetDock(ind, Dock.Left); line.Children.Add(ind);
                var gap = new Border { Width = 16 }; DockPanel.SetDock(gap, Dock.Left); line.Children.Add(gap);
            }
            var icon = IconText(FileGlyph(e), 15, blocked ? "B.Muted" : "B.Ink2"); icon.Margin = new Thickness(depth >= 0 ? 4 : 0, 0, 8, 0); DockPanel.SetDock(icon, Dock.Left); line.Children.Add(icon);
            var details = new Button { Content = IconText("\uE946", 14, "B.Muted"), Style = Res<Style>("Wb.Plain"), Padding = new Thickness(7, 5, 7, 5), Margin = new Thickness(2, 0, 4, 0), VerticalAlignment = VerticalAlignment.Center, IsTabStop = false, ToolTip = "Show details" };
            System.Windows.Automation.AutomationProperties.SetName(details, "Details: " + name);
            DockPanel.SetDock(details, Dock.Right); line.Children.Add(details);
            var nm = new StackPanel { Margin = new Thickness(0, 6, 4, 6), VerticalAlignment = VerticalAlignment.Center, Background = Brushes.Transparent, ToolTip = e.Unreadable ?? e.DisplayPath };
            // a file inside a system image or the package gets its checksum from its verified holder when it is read: not marked
            var noSum = (e.HashOrigin == HashOrigin.None || e.HashOrigin == HashOrigin.Unreadable) && e.Container == null;
            var mark = e.StateWord ?? (blocked ? "damaged" : e.Private != null ? "private" : noSum && e.Kind == EntryKind.File ? "no checksum" : null);
            var nt = new TextBlock { Text = name + (mark != null ? "  (" + mark + ")" : ""), FontSize = 13, TextTrimming = TextTrimming.CharacterEllipsis };
            if (blocked) nt.Foreground = Res<Brush>("B.Muted");
            nm.Children.Add(nt);
            var folder = depth < 0 || paths ? FolderOfEntry(e) : null;
            if (!string.IsNullOrEmpty(folder)) nm.Children.Add(new TextBlock { Text = folder, FontSize = 11.5, Foreground = Res<Brush>("B.Muted"), TextTrimming = TextTrimming.CharacterEllipsis });
            line.Children.Add(nm);
            Grid.SetColumn(line, 1); g.Children.Add(line);
            var size = new TextBlock { Text = e.Kind == EntryKind.Folder ? "" : Units.Gb(e.Size), FontSize = 12.5, Foreground = Res<Brush>("B.Ink2"), TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 14, 0) };
            Grid.SetColumn(size, 2); g.Children.Add(size);
            var date = new TextBlock { Text = e.MtimeUtc?.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) ?? "No date", FontSize = 12.5, Foreground = Res<Brush>("B.Muted"), VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = e.DateText };
            Grid.SetColumn(date, 3); g.Children.Add(date); cols.Track(g.ColumnDefinitions[3], date);
            var row = new Border { Child = g, BorderBrush = Res<Brush>("B.Line"), BorderThickness = new Thickness(0, 1, 0, 0), Background = Brushes.Transparent, Cursor = blocked ? null : System.Windows.Input.Cursors.Hand, MinHeight = 34 };
            bool hover = false;
            void Paint() => row.Background = cb.IsChecked == true ? SelectedRow : hover || cb.IsKeyboardFocused && FocusCue.KeyboardInUse ? Res<Brush>("B.Hover") : Brushes.Transparent;
            var target = new BrowseTarget { Entry = e };
            void ShowIt() { SetCurrent(target); Preview(e, preview); }
            cb.Checked += (s, ev) => { SelectEntry(e, true); Paint(); };
            cb.Unchecked += (s, ev) => { SelectEntry(e, false); Paint(); };
            if (!drawn.TryGetValue(e.Id, out var boxes)) drawn[e.Id] = boxes = new List<(CheckBox, Action)>();
            boxes.Add((cb, Paint));
            details.Click += (s, ev) => ShowIt();
            // a click on the row (its name, icon, size or date, or the space between): one change of the selection. The name answers first
            // and marks the click handled, so the row does not answer it again; the check box and the details button handle their own
            // clicks (also marked handled), which never reach here a second time
            void RowClick(object s, System.Windows.Input.MouseButtonEventArgs ev)
            {
                if (ev.Handled || InsideButton(ev.OriginalSource as DependencyObject, row)) return;
                ev.Handled = true;
                if (!blocked) { cb.IsChecked = cb.IsChecked != true; cb.Focus(); }
                ShowIt();
            }
            nm.MouseLeftButtonUp += RowClick; row.MouseLeftButtonUp += RowClick;
            row.MouseEnter += (s, ev) => { hover = true; Paint(); }; row.MouseLeave += (s, ev) => { hover = false; Paint(); };
            cb.GotKeyboardFocus += (s, ev) => { SetCurrent(target); Paint(); }; cb.LostKeyboardFocus += (s, ev) => Paint();
            cb.KeyDown += (s, ev) => { if (ev.Key == System.Windows.Input.Key.Enter) { ShowIt(); ev.Handled = true; } };
            Menu(row, m =>
            {
                MenuLabel(m, name);
                if (!blocked) m.Items.Add(MenuItemOf(cb.IsChecked == true ? "Remove from Selection" : "Select", () => { cb.IsChecked = cb.IsChecked != true; SetCurrent(target); }));
                m.Items.Add(MenuItemOf("Show Details", ShowIt));
                foreach (var (text, go) in ActionsFor(target)) m.Items.Add(MenuItemOf(text, go));
            });
            Paint();
            return row;
        }

        /// <summary>true when the click landed on a control inside the row (its check box, its details button) rather than on the row itself</summary>
        static bool InsideButton(DependencyObject d, DependencyObject row)
        {
            for (; d != null && !ReferenceEquals(d, row); d = d is Visual || d is System.Windows.Media.Media3D.Visual3D ? VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d))
                if (d is ButtonBase) return true;
            return false;
        }

        /// <summary>two panels side by side when the page is at least `wide` wide (the second `second` wide), one under the other otherwise</summary>
        Grid SideBySide(FrameworkElement first, FrameworkElement second, double secondWidth, double wide)
        {
            var g = new Grid();
            g.ColumnDefinitions.Add(new ColumnDefinition()); g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            g.Children.Add(first); g.Children.Add(second);
            bool? side = null;
            void Lay(double w)
            {
                bool s = w >= wide; if (side == s) return; side = s;
                if (s) { Grid.SetRow(second, 0); Grid.SetColumn(second, 1); second.Width = secondWidth; second.Margin = new Thickness(16, 0, 0, 10); }
                else { Grid.SetRow(second, 1); Grid.SetColumn(second, 0); second.Width = double.NaN; second.Margin = new Thickness(0, 0, 0, 10); }
            }
            Lay(0); g.SizeChanged += (s, ev) => Lay(ev.NewSize.Width);
            return g;
        }

        /// <summary>where a file's checksum comes from, in a sentence: recorded when the backup was made, or worked out on this PC (which is
        /// never a checksum recorded when the backup was made); a file without one says why (the engine's own words)</summary>
        static string Provenance(BrowseEntry e, bool live)
        {
            switch (e.HashOrigin)
            {
                case HashOrigin.Manifest: case HashOrigin.Inventory: case HashOrigin.PackageList: return "Its checksum was recorded when the backup was made.";
                case HashOrigin.Derived: case HashOrigin.DerivedIndex:
                    return live && e.Sha256 != null ? "Its checksum was worked out from the console during this session." : e.Sha256 != null ? "Its checksum was worked out on this PC from a checked part of the " + (e.KeyFrom != null ? "backup (decrypted with the backup's own key)" : "backup") + ". It is not a checksum recorded when the backup was made." : e.HashText;
                default: return e.HashText ?? "No checksum is known for it.";
            }
        }

        /// <summary>the exact facts about one entry for Technical details: the engine's provenance words, checksums, where it is held</summary>
        static object[] EntryFacts(BrowseEntry e, bool live) => new object[]
        {
            live ? "On the console: " + (e.ConsolePath ?? e.DisplayPath) : "In the backup file: " + e.Locator,
            e.ImageNote,
            e.HashText,
            e.Sha256 != null ? "SHA-256: " + e.Sha256 + " (" + e.ChecksumKind + ")" : "Checksum: " + e.ChecksumKind,
            e.BoundTo != null ? "Worked out through " + e.BoundTo + (e.BoundToSha256 != null ? ", SHA-256 " + e.BoundToSha256 : "") : null,
            e.KeyFrom != null ? "Decrypted on this PC with the system key from " + e.KeyFrom + (e.KeyFromSha256 != null ? " (SHA-256 " + e.KeyFromSha256 + ")" : "") + "; the key itself is never shown." : null,
            e.Kind == EntryKind.Folder ? null : "Size: " + Units.Gb(e.Size) + " (" + e.Size.ToString("N0", CultureInfo.GetCultureInfo("en-US")) + " bytes)",
            "Date: " + e.DateText + (e.DateNote != null ? " " + e.DateNote : ""),
            e.Source == EntrySource.DataFile && e.Mode != 0 ? "Permissions " + Convert.ToString(e.Mode & 0xFFF, 8).PadLeft(4, '0') + ", owner " + e.Uid + ", group " + e.Gid + ", inode " + e.Inode + ", links " + e.Links : null,
            e.LinkTarget != null ? "Link to: " + e.LinkTarget + " (described, never followed)" : null,
            "Kind: " + Categories.Label(e.Category) + ". " + e.CategoryReason,
        };

        void Preview(BrowseEntry e, StackPanel into)
        {
            browseDetails = e.Id;
            into.Children.Clear(); into.Children.Add(T(string.IsNullOrEmpty(e.Name) ? e.DisplayPath : e.Name, 14, true, false, new Thickness(0, 0, 0, 2))); into.Children.Add(T(e.DisplayPath, 12, false, true));
            // what a part or an image holds, then where its checksum comes from in a sentence; the engine's full words and the exact
            // checksums under Technical details (kept open when the read below updates them)
            if (e.ImageSystem != null) into.Children.Add(T("Holds: " + e.ImageSystem, 12.5, true, false, new Thickness(0, 4, 0, 2)));
            else if (e.IsImage && e.ImageType != null) into.Children.Add(T(e.ImageType, 12.5, true, false, new Thickness(0, 4, 0, 2)));
            // an image whose files are not listed says why on the page (that is missing information, not a detail); a listed one's
            // description stays under Technical details
            if (e.IsImage && e.ImageNote != null && (browsing == null || browsing.Children(e.Id).Count == 0)) into.Children.Add(T(e.ImageNote, 12, false, true, new Thickness(0, 2, 0, 4)));
            var prov = T(Provenance(e, browsing?.Live == true), 12.5, false, true, new Thickness(0, 4, 0, 6));
            var tech = new ContentControl();
            void Tech()
            {
                bool open = (tech.Content as Panel)?.Children.OfType<ToggleButton>().FirstOrDefault()?.IsChecked == true;
                var d = Details("Technical details", EntryFacts(e, browsing?.Live == true)); tech.Content = d;
                if (open && d is Panel p) { var tb = p.Children.OfType<ToggleButton>().FirstOrDefault(); if (tb != null) tb.IsChecked = true; }
            }
            Tech();
            if (e.Kind == EntryKind.File && e.Unreadable != null)
            {
                into.Children.Add(Note(e.StateWord == "damaged" ? "Damaged: it cannot be shown or copied." : e.StateWord == "missing" ? "Missing from the file: it cannot be shown or copied." : "It cannot be checked, so it cannot be shown or copied.", "warn"));
                into.Children.Add(tech); return;
            }
            if (e.Kind == EntryKind.File && e.Private == null) into.Children.Add(prov);   // the key is never read for a preview: its provenance is under Technical details
            if (e.Private != null) into.Children.Add(Note(KeyUiText, "warn"));
            var acts = new List<UIElement>();
            if (e.Kind == EntryKind.File && browsing?.Live == false) { var ee = e; var text = ExportOneText(e); acts.Add(B(text, "Btn.Ghost", () => ExportFlow(ExportRequest.One(browsing, ee, null), text.TrimEnd('…')))); }
            var saveArea = new StackPanel();
            if (e.Category == Category.Saves && e.Kind == EntryKind.File && e.Source == EntrySource.DataFile && browsing.NesSaves) acts.Add(B("Convert to a Game Save File", "Btn.Ghost", () => ConvertSave(e, saveArea)));
            if (acts.Count > 0) into.Children.Add(Buttons(acts.ToArray()));
            into.Children.Add(saveArea);
            var pic = new StackPanel(); into.Children.Add(pic); into.Children.Add(tech);
            // the key's bytes are never read for a preview
            if (e.Private != null) return;
            if (!(e.Kind == EntryKind.File && e.Size <= 20_000_000)) { pic.Children.Add(T("No preview for this kind of file.", 12.5, false, true)); return; }
            var c = browsing; pic.Children.Add(T("Reading...", 12.5, false, true));
            Task.Run(() => { string why0 = null; var b = c == null ? null : c.ReadVerified(e, 20_000_000, out why0); return (b, b == null ? null : BackupContents.ImageKindOf(b), why0); }).ContinueWith(t =>
            {
                if (browsing != c) return; pic.Children.Clear();
                var (b, kind, why) = t.Result;
                prov.Text = Provenance(e, c?.Live == true); Tech();   // a checksum this read worked out is shown with where it comes from
                if (b == null) { pic.Children.Add(Note(why ?? "It could not be read against its checksum.", "warn")); return; }
                if (kind == null) { pic.Children.Add(T("Not a picture; no preview for this kind of file.", 12.5, false, true)); return; }
                try { var bi = new BitmapImage(); bi.BeginInit(); bi.StreamSource = new MemoryStream(b); bi.DecodePixelWidth = 640; bi.CacheOption = BitmapCacheOption.OnLoad; bi.EndInit(); bi.Freeze(); pic.Children.Add(new Image { Source = bi, MaxWidth = 640, MaxHeight = 360, Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 8, 0, 0) }); }
                catch (Exception) { pic.Children.Add(Note("This picture could not be shown.", "warn")); }
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        /// <summary>a folder to copy into (Export…, a rebuild, a copy out, a second copy): the files go into a new folder made inside it</summary>
        string PickFolder() => CcsFileDialog.ChooseFolder(this, "Choose where to copy the files (a new folder is made inside it)", null, "copy-destination");

        /// <summary>the name an export of the current selection is offered with (Export Selected): what the selected files are, never a selection
        /// mode's name (ExportNames.ForEntries), already made a valid folder name (ExportNames.Clean: "Super Mario Bros." is offered as "Super
        /// Mario Bros"). The export step, or the folder dialog that asks where to export, shows it to be changed.</summary>
        internal string SuggestedExportName() => browsing == null ? ExportNames.Neutral : ExportNames.Clean(ExportRequest.Selected(browsing, browseSel, null).FolderName) ?? ExportNames.Neutral;

        /// <summary>an export from the Backup Browser: the export step first (the new folder's name and where it goes), then the copying</summary>
        void ExportFlow(ExportRequest r, string title)
        {
            if (r == null || browsing == null) return;
            if (!ChooseExportTarget(r, title, ExportWhat(browsing, r))) return;
            DoExport(r);
        }

        /// <summary>what an export copies, in a sentence: how many files and how much</summary>
        static string ExportWhat(BackupContents c, ExportRequest r)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal); int files = 0; long bytes = 0;
            void Take(BrowseEntry e) { if (e == null || !seen.Add(e.Id)) return; if (e.Kind == EntryKind.File) { files++; bytes += e.Size; } if (e.Kind == EntryKind.Folder) foreach (var d in c.Descendants(e)) Take(d); }
            foreach (var e in r.Entries) Take(e);
            return files == 0 ? "Nothing in it is a file that can be copied." : files.ToString("N0", En) + (files == 1 ? " file" : " files") + " (" + Units.Gb(bytes) + ") will be copied into a new folder.";
        }

        /// <summary>The export step, before anything is copied: the new folder's name, offered with the suggestion (r.FolderName) and changed
        /// freely, and the folder it is made in (Choose… or typed). It says the exact folder that will be made: a name already used there gets
        /// " (2)", " (3)" ..., and nothing is replaced. True when the person chose Export: r.Destination and r.FolderName are then set. This is
        /// the one place the export's name and destination are asked for.</summary>
        bool ChooseExportTarget(ExportRequest r, string title, string what)
        {
            var dlg = Owned(title, 620, false);
            bool go = false;
            var sp = new StackPanel { Margin = new Thickness(24, 20, 24, 14) };
            sp.Children.Add(T(title, 17, true, false, new Thickness(0, 0, 0, 8)));
            if (what != null) sp.Children.Add(T(what, 13.5, false, true, new Thickness(0, 0, 0, 14)));
            var name = new TextBox { Text = ExportNames.Clean(r.FolderName) ?? ExportNames.Neutral, MaxLength = 120 }; System.Windows.Automation.AutomationProperties.SetName(name, "Name of the new folder");
            sp.Children.Add(Field("Name of the new folder", name, null, 560));
            var at = new TextBox { Text = lastExportFolder ?? "" }; System.Windows.Automation.AutomationProperties.SetName(at, "Make it in this folder");
            var said = T("", 12.5, false, true, new Thickness(0, -4, 0, 10));
            var ok = B("Export", "Btn.Primary", () => { go = true; dlg.Close(); }); ok.IsDefault = true;
            var cancel = B("Cancel", "Btn.Ghost", () => dlg.Close()); cancel.IsCancel = true;
            void Update()
            {
                var dest = at.Text.Trim(); var clean = ExportNames.Clean(name.Text); string why = null, final = null;
                if (clean == null) why = "Type a name for the new folder.";
                else if (dest.Length == 0) why = "Choose the folder to make it in.";
                else why = Exporter.CheckDestination(dest);
                if (why == null) { final = Exporter.FolderNameFor(dest, name.Text); if (final == null) why = "That folder cannot be used; choose another one."; }
                ok.IsEnabled = why == null;
                said.Foreground = Res<Brush>(why == null ? "B.Muted" : "B.Warn");
                said.Text = why ?? "The files go into a new folder: " + Path.Combine(Path.GetFullPath(dest).TrimEnd('\\'), final) + "."
                    + (final != clean ? " A folder named \u201c" + clean + "\u201d is already there, so this one gets a number; nothing is replaced." : "")
                    + (clean != name.Text.Trim() ? " Windows cannot use that name exactly as typed in a folder name, so it was changed as shown." : "");
            }
            var choose = B("Choose…", "Btn.Ghost", () =>
            {
                // the folder dialog shows the new folder's name too (it may be changed there): both come back into this step
                var start = at.Text.Trim().Length > 0 ? at.Text.Trim() : lastExportFolder;
                var c = CcsFileDialog.ChooseExportFolder(this, "Choose where to make the new folder", start, ExportNames.Clean(name.Text) ?? ExportNames.Neutral, "copy-destination");
                if (c != null) { at.Text = c.Folder; name.Text = c.Name; Update(); }
            });
            sp.Children.Add(Field("Make it in this folder", at, choose, 560));
            sp.Children.Add(said);
            sp.Children.Add(T("Each file is read back and compared with its checksum before it gets its name. Nothing is written over, and nothing in the " + (browsing?.Live == true ? "console" : "backup") + " changes.", Ty.Small, false, true, new Thickness(0, 0, 0, 6)));
            sp.Children.Add(Buttons(ok, cancel));
            name.TextChanged += (s, e) => Update(); at.TextChanged += (s, e) => Update(); Update();
            dlg.Content = sp; dlg.Loaded += (s, e) => { name.Focus(); name.SelectAll(); };
            Modal(dlg);
            if (!go) return false;
            r.Destination = Path.GetFullPath(at.Text.Trim()); r.FolderName = name.Text.Trim(); lastExportFolder = r.Destination;
            return true;
        }

        void DoExport(ExportRequest r)
        {
            if (r == null || string.IsNullOrEmpty(r.Destination)) return;
            var why = Exporter.CheckDestination(r.Destination); if (why != null) { Tell("Choose another folder", why); return; }
            var cts = new CancellationTokenSource();
            var pv = new ProgressView(this, "Copying Files", "The files are copied to a new folder on this PC. Each one is read back and compared with its checksum before it gets its name. Nothing in the " + (browsing?.Live == true ? "console" : "backup") + " changes.", "Cancel", () => cts.Cancel());
            Show(pv.Panel, "progress"); var c = browsing;
            Task.Run(() => Exporter.Export(c, r, () => cts.IsCancellationRequested, p => Dispatcher.BeginInvoke(new Action(() => pv.Update(p.Current ?? p.Phase, p.BytesDone, p.BytesTotal))))).ContinueWith(t =>
            {
                // copied from the console's files in a session the person left: the result waits for Return to Task (WorkbenchNav.cs)
                if (TaskStepAway(pv.Panel, () => { if (t.IsFaulted) Show(Message("The files were not copied", "Copying stopped with a problem. Nothing on the console changed.", "bad", t.Exception.GetBaseException().Message), "msg"); else ShowExportResult(t.Result, c); })) return;
                if (page.Content != pv.Panel) { UpdateStatus(t.IsFaulted ? "Copying files stopped with a problem." : "Copying files finished: " + t.Result.Summary); return; }
                if (t.IsFaulted) { Show(Message("The files were not copied", "Copying stopped with a problem. Nothing in the backup changed.", "bad", t.Exception.GetBaseException().Message), "msg"); return; }
                ShowExportResult(t.Result, c);
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        /// <summary>an export's result: what was copied and where it was saved, what was left out and why; the export's list, the engine's
        /// summary and the date notes under Technical details</summary>
        void ShowExportResult(ExportResult x, BackupContents c)
        {
            int n = x.Files.Count; string files = n + (n == 1 ? " file" : " files");
            string title = x.Ok ? "Files copied" : x.State == "canceled" ? "Copying canceled" : x.State == "refused" ? "Nothing was copied" : "Some files were not copied";
            string first = x.Ok ? files + (n == 1 ? " was" : " were") + " copied to a new folder on this PC (" + Units.Gb(x.Bytes) + "). Each one was read back and matches its checksum."
                : x.State == "canceled" ? "Copying stopped. " + files + (n == 1 ? " was" : " were") + " copied and checked before it stopped; the rest were not copied."
                : x.State == "refused" ? (x.Problem ?? "Nothing was copied.")
                : files + (n == 1 ? " was" : " were") + " copied and checked; " + x.NotExported.Count + " could not be copied (listed below).";
            var sp = SectionHead(title, first + " Nothing in the " + (c?.Live == true ? "console" : "backup") + " changed.");
            if (x.Folder != null) { var f = new StackPanel(); f.Children.Add(T("Saved in this folder:", Ty.Small, false, true, new Thickness(0, 0, 0, 2))); f.Children.Add(PathText(x.Folder, 13)); sp.Children.Add(Card(f, 16)); }
            if (x.Withheld.Count > 0) sp.Children.Add(Note(x.Withheld.Count == 1 ? "The separate system key file was skipped. To export it, select the key file itself." : "The " + x.Withheld.Count + " separate system key files were skipped. To export one, select that key file itself.", "info"));
            var holders = x.KeyHolders;
            if (holders.Count > 0) sp.Children.Add(Note("This export includes whole files that can contain the console's system key: " + string.Join(", ", holders.Take(4).Select(h => h.Entry.DisplayPath))
                + (holders.Count > 4 ? " and " + (holders.Count - 4) + " more" : "") + ". Keep this export private.", "warn"));
            foreach (var m in x.NotExported.Take(20)) sp.Children.Add(Note(m.entry.DisplayPath + ": " + m.reason, "warn"));
            if (x.NotExported.Count > 20) sp.Children.Add(T("And " + (x.NotExported.Count - 20) + " more, listed in the export's list.", Ty.Small, false, true));
            if (x.Leftovers.Count > 0) sp.Children.Add(Note("Some unfinished copies could not be removed; they are not checked files: " + string.Join(", ", x.Leftovers.Take(5)), "warn"));
            var tech = new List<object> { x.Summary };
            if (x.ManifestPath != null) tech.Add("The export's list (every file, its checksum and where that checksum comes from): " + x.ManifestPath);
            if (x.Described.Count > 0) tech.Add(x.Described.Count + " link(s) or special file(s) are described in the export's list; they are never followed or created on this PC.");
            foreach (var d in x.DateNotes.Take(5)) tech.Add(d.RelativePath + ": " + d.DateNote);
            tech.Add((c?.DatesText ?? BackupContents.ClockText));
            sp.Children.Add(Details("Technical details", tech.ToArray()));
            var folder = x.Folder;
            sp.Children.Add(Buttons(folder != null && Directory.Exists(folder) ? B("Open the Folder", "Btn", () => { try { System.Diagnostics.Process.Start("explorer.exe", "\"" + folder + "\""); } catch { } }) : null,
                                    B(c?.Live == true ? "Back to the Console's Files" : "Back to the Backup", "Btn.Ghost", () => ShowBrowse())));
            Show(sp, "export-result");
        }

        // ------------------------------------------------------------------ a damaged backup: Backup Repair's part finder, from the browser
        /// <summary>Find What Can Be Saved: the part finder reads the damaged file (and checks every part it finds against the backup's own
        /// records); nothing is written anywhere</summary>
        void FindWhatCanBeSaved(BackupListEntry e)
        {
            var cts = new CancellationTokenSource();
            var pv = new ProgressView(this, "Finding What Can Be Saved", "The damaged file is read on this PC and every part found in it is compared with the backup's own records. Nothing is written to it or anywhere else.", "Cancel", () => cts.Cancel());
            Show(pv.Panel, "progress");
            var o = new RepairOptions { Cancelled = () => cts.IsCancellationRequested, Progress = p => Dispatcher.BeginInvoke(new Action(() => pv.Update(p.Text ?? p.Phase, p.Done, p.Total))), SaveReport = false };
            Task.Run(() =>
            {
                using (var sv = Salvage.Survey(new[] { e.Path }, o))
                {
                    var parts = sv.Items.Where(i => i.Kind == "part").ToList(); var files = sv.Items.Where(i => i.Kind == "file").ToList();
                    return new DamagedView
                    {
                        Problem = sv.Problem, Summary = sv.Summary, Notes = sv.Notes.ToList(), Conditions = sv.Conditions.Select(x => x.condition).ToList(), Parts = parts.Count, PartsOk = parts.Count(i => i.Exportable),
                        Files = files.Count(i => i.Status != "not-exportable"), FilesOk = files.Count(i => i.Exportable), Damaged = parts.Where(i => !i.Exportable).Select(i => i.Path + " (" + i.Status.Replace('-', ' ') + ")").ToList(),
                        CanRebuild = sv.Map?.Manifest != null && parts.Count > 0 && parts.All(i => i.Exportable), Any = sv.Exportable.Any(),
                    };
                }
            }).ContinueWith(t =>
            {
                if (page.Content != pv.Panel) { UpdateStatus("Finding what can be saved finished. Open it again from the Backup Browser to see the result."); return; }
                ShowDamagedBackup(e, t.IsFaulted ? new DamagedView { Problem = "The damaged file could not be read through to the end.", Raw = t.Exception.GetBaseException().Message } : t.Result);
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        sealed class DamagedView { public string Problem, Summary, Raw; public List<string> Notes = new List<string>(), Conditions = new List<string>(), Damaged = new List<string>(); public int Parts, PartsOk, Files, FilesOk; public bool CanRebuild, Any; }

        void ShowDamagedBackup(BackupListEntry e, DamagedView v)
        {
            var sp = SectionHead("Find What Can Be Saved", "What can still be used from this damaged backup file on this PC. It is only read: nothing in it changes, and it is never offered as a backup to restore.");
            { var at = PathText(e.Path, 13); at.Margin = new Thickness(0, 0, 0, 12); sp.Children.Add(at); }
            var back = B("Back to the Backup List", "Btn.Ghost", BackToList);
            if (v.Problem != null && v.Problem != "Canceled.") { sp.Children.Add(Note(v.Problem, "bad")); sp.Children.Add(Details("Technical details", v.Raw, v.Summary != v.Problem ? v.Summary : null)); sp.Children.Add(Buttons(B("Other Ways to Repair", "Btn.Ghost", () => ShowRepairRoutes(e)), back)); Show(sp, "browse-damaged"); return; }
            if (v.Problem == "Canceled.") { sp.Children.Add(Note("Canceled. Nothing was written.", "info")); sp.Children.Add(Buttons(back)); Show(sp, "browse-damaged"); return; }
            var info = new StackPanel();
            info.Children.Add(T("What was found", 15, true, false, new Thickness(0, 0, 0, 6)));
            foreach (var c in v.Conditions) info.Children.Add(T("The file: " + c + ".", 13, false, true, new Thickness(0, 0, 0, 6)));
            info.Children.Add(KeyValues(("Parts that check out", v.PartsOk + " of " + v.Parts), ("Files that check out", v.Files == 0 ? "none can be checked" : v.FilesOk + " of " + v.Files), ("Damaged or missing parts", v.Damaged.Count == 0 ? "none" : v.Damaged.Count.ToString(CultureInfo.InvariantCulture))));
            foreach (var n in v.Notes.Take(3)) info.Children.Add(T(n, 12.5, false, true, new Thickness(0, 6, 0, 0)));
            info.Children.Add(Details("Technical details", new object[] { v.Damaged.Count > 0 ? "Damaged or missing: " + string.Join(", ", v.Damaged) + "." : null, v.Summary, "Found by Backup Repair's part finder: the parts are located without trusting the file's damaged table of contents and each is checked against the backup's own records (its manifest and repair map, which must agree)." }.Concat(v.Notes.Skip(3)).ToArray()));
            sp.Children.Add(Card(info));
            var grid = CardGrid(250, 3);
            void Way(string title, string text, string button, Action go, bool enabled, string why)
            {
                var g = new Grid(); g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); g.RowDefinitions.Add(new RowDefinition()); g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                g.Children.Add(T(title, 15, true, false, new Thickness(0, 0, 0, 6)));
                var body = new StackPanel { Margin = new Thickness(0, 0, 0, 14) }; body.Children.Add(T(text, 13, false, true));
                if (!enabled && why != null) body.Children.Add(T(why, 12.5, false, false, new Thickness(0, 4, 0, 0)));
                Grid.SetRow(body, 1); g.Children.Add(body);
                var b = B(button, "Btn", go, enabled); b.Margin = new Thickness(0); Grid.SetRow(b, 2); g.Children.Add(b);
                var card = Card(g, 18); card.Margin = new Thickness(0); grid.Children.Add(card);
            }
            Way("Browse what can be saved", "Open it where it is: every part and file that still matches its recorded checksum can be looked at and copied. Nothing is written, not even a temporary file.", "Browse What Can Be Saved",
                () => OpenBrowse(e.Path, true), v.Any, "Nothing in it still matches a recorded checksum.");
            Way("Rebuild a repaired copy", "When every part is intact and only the file's table of contents is damaged: a new backup file is made in a folder you choose. It must pass the full check before it gets its name; this file stays as it is.", "Rebuild a Repaired Copy…",
                () => { var dest = PickBackupFolder(); if (dest == null) return; RunRepair("Rebuilding the Table of Contents", "Every part is checked against its recorded checksums; the new file is checked in full before it gets its name.", o => ZipIndexRecovery.Run(e.Path, dest, o), e.BackupId); },
                v.CanRebuild, "Some parts are damaged or missing in this file, so a whole backup cannot be made from it alone. Other Ways to Repair can also use other copies and backups.");
            Way("Copy out what can be saved", "Every part and file that still matches its recorded checksum is copied into a new folder you choose. That folder is not a backup and can never be restored.", "Copy Out…",
                () => { var dest = PickFolder(); if (dest == null) return; RunRepair("Copying Out What Can Be Saved", "Only files whose recorded checksums still match are copied.", o => { using (var s = Salvage.Survey(new[] { e.Path }, o)) return Salvage.Export(s, dest, o, i => i.Exportable); }, e.BackupId); },
                v.Any, "Nothing in it still matches a recorded checksum.");
            sp.Children.Add(grid);
            sp.Children.Add(Buttons(B("Other Ways to Repair", "Btn.Ghost", () => ShowRepairRoutes(e)), back));
            Show(sp, "browse-damaged");
        }

        string PickBackupFolder() => CcsFileDialog.ChooseFolder(this, "Choose where to save the repaired backup file (the damaged file is not changed)", null, "repair-destination");
    }
}
