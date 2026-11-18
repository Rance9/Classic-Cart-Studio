// Classic Cart Studio console backup and restore window. Copyright (C) 2026 Rance9. GPL-3.0-or-later.
// Backup Browser (offline, no console and no USB driver): the list of backups in the backup locations (complete, incomplete, damaged,
// salvage, missing, each with its state and last passed file check), Open a Backup File for one saved anywhere else on this PC, then a
// chosen backup By game (the default), By type or as stored (All files), with search, multi-select, a picture preview and export
// (CcsBackup.Browse: names made safe, nothing overwritten, every file read back against its checksum). System images, the console's own
// system part (decrypted on the PC with the key its startup images carry), the startup images' parts and ramdisk files and the CCS HOME
// package open under their own entries, each file with where its checksum comes from (a short line on the page, the engine's full words
// and the exact checksums under Technical details). The system key is never shown. Its separate key file is exported only when it is
// itself selected; whole startup images, ramdisks and raw NAND images export as they are and can still contain the key, and the page and
// the export's result say so. A damaged backup leads to Find What Can Be Saved: Backup Repair's part finder reads it (nothing is written
// to it), then it can be browsed in place, rebuilt into a new file in a folder the person chooses, or have what still checks out copied
// out. Reading a backup is never a restore and never a new check of a console. A check of a file runs only when the person asks for it.
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

        /// <summary>the window's own words about the console's system key (the engine's KeyPrivateText says the same for its records)</summary>
        const string KeyUiText = "This is the console's system key: its startup software uses it to open the encrypted system part. Its bytes are never shown here. Exporting a folder, a type, search results or a game skips this separate key file; it is exported only when you select it itself. Whole startup images, ramdisks and raw NAND images can still contain the key. Keep those exports, and any copy of the key file, private.";

        // ------------------------------------------------------------------ the list of backups
        /// <summary>the backups in the backup locations; justChecked: a file the person just checked here (its result is said at the top)</summary>
        void ShowBackupBrowser(BackupListEntry justChecked = null, DateTime checkedFrom = default(DateTime))
        {
            browsing?.Dispose(); browsing = null; browseSel.Clear(); browseSearch = "";
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
            sp.Children.Add(T("A passed file check means the file was intact when it was checked. It is not a test of restoring it to a console.", Ty.Fine, false, true, new Thickness(0, 2, 0, 0)));
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
                browsing?.Dispose(); browsing = r.Contents; browseSel.Clear(); browseView = "game"; browseSearch = ""; ShowBrowse();
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        /// <summary>a backup that did not pass the check before opening: what that means, the engine's words, and where to go next</summary>
        void ShowOpenFailed(string path, string problem, List<string> problems)
        {
            var sp = PageHead("This backup could not be opened", "The file did not pass the check that runs before a backup is opened, so its contents are not shown. Nothing was written to it.");
            { var at = PathText(path, 13); at.Margin = new Thickness(0, 0, 0, 12); sp.Children.Add(at); }
            sp.Children.Add(Note(problem == null ? "It is not a complete console backup." : "Part of the file does not match what was recorded when the backup was made, so the file is damaged.", "bad"));
            if (problem != null || problems?.Count > 0) sp.Children.Add(Details("Technical details", new object[] { problem }.Concat((problems ?? new List<string>()).Where(x => x != problem)).ToArray()));
            bool listed = false; try { listed = BackupList.List(Reload()).Any(x => string.Equals(x.Path, path, StringComparison.OrdinalIgnoreCase)); } catch (Exception) { }
            if (listed) sp.Children.Add(T("The backup list offers Find What Can Be Saved for a damaged backup, and Backup Repair can make a repaired copy. The file itself is never changed.", Ty.Text, false, true));
            sp.Children.Add(Buttons(B("Back to the Backup List", "Btn", BackToList), listed ? B("Backup Repair", "Btn.Ghost", () => Go("Backup Repair")) : null));
            Show(sp, "msg");
        }

        void OpenLiveBrowse(long partBytes, string tag, string state)
        {
            var w = worker; if (w == null || !w.Running) return;
            var cts = new CancellationTokenSource();
            var pv = new ProgressView(this, "Reading the Console's Files", "The console's data storage is read through the session in its memory. Nothing on the console is changed.", "Cancel", () => cts.Cancel());
            Show(pv.Panel, "progress");
            var stream = new WorkerRangeStream(w, partBytes);
            Task.Run(() => BackupContents.OpenLive(stream, tag, state, () => cts.IsCancellationRequested)).ContinueWith(t =>
            {
                var r = t.Result;
                if (!r.Ok) { stream.Dispose(); Show(Message("The console's files could not be read", r.Problem ?? "", "bad"), "msg"); return; }
                browsing?.Dispose(); browsing = r.Contents; browseSel.Clear(); browseView = "game"; browseSearch = ""; ShowBrowse();
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
            // a file that is missing or unfinished says why in the list's own words; every other file says its last passed file check
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
            else if (!gone && e.Detail != null && !(e.Status == BrowseStatus.Complete && e.Check == CheckState.NotChecked)) row.Children.Add(T(e.Detail, Ty.Small, false, true, new Thickness(0, 6, 0, 0)));
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
                who.Children.Add(T("Backup of " + (c.Simulated ? "simulated console " : "console ") + (c.ConsoleTag ?? "not known") + ", made " + when + (c.Salvage ? "" : " · passed its full check when it was opened"), Ty.Body, true, false, new Thickness(0, 0, 0, 2)));
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
                    BackupContents.ClockText));
                var wc = Card(who, 14); wc.Margin = new Thickness(0, -6, 0, 12); sp.Children.Add(wc);
            }
            foreach (var n in c.Notes) { if (c.Live && n.StartsWith("Read live", StringComparison.Ordinal)) continue; sp.Children.Add(Note(n, "info")); }
            // the view (a joined row of three) and the search box across the rest of the line
            var bar = new Grid { Margin = new Thickness(0, 0, 0, 12) };
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); bar.ColumnDefinitions.Add(new ColumnDefinition { MinWidth = 150 }); bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var views = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 12, 0) };
            foreach (var (v, label) in new[] { ("game", "By Game"), ("type", "By Type"), ("all", "All Files") })
            { var vv = v; var b = B(label, browseView == v ? "Btn.Primary" : "Btn.Ghost", () => { browseView = vv; ShowBrowse(); }); b.Margin = new Thickness(0, 0, 4, 0); b.MinWidth = 0; views.Children.Add(b); }
            bar.Children.Add(views);
            var search = new TextBox { Text = browseSearch }; System.Windows.Automation.AutomationProperties.SetName(search, "Search"); Grid.SetColumn(search, 1); bar.Children.Add(search);
            var go = B("Search", "Btn.Ghost", () => { browseSearch = search.Text.Trim(); ShowBrowse(); }); go.Margin = new Thickness(8, 0, 0, 0); Grid.SetColumn(go, 2); bar.Children.Add(go);
            search.KeyDown += (s, ev) => { if (ev.Key == System.Windows.Input.Key.Enter) { browseSearch = search.Text.Trim(); ShowBrowse(); } };
            sp.Children.Add(bar);

            var preview = new StackPanel();
            preview.Children.Add(T("Click a file's name to see what it is and where its checksum comes from, with a preview of a picture. " + BackupContents.ClockText, 12.5, false, true));
            var listPanel = new StackPanel();
            IEnumerable<BrowseEntry> shown;
            if (browseView == "game")
            {
                var games = string.IsNullOrEmpty(browseSearch) ? c.Games.ToList() : c.SearchGames(browseSearch);
                if (games.Count == 0) listPanel.Children.Add(T("No games" + (string.IsNullOrEmpty(browseSearch) ? "." : " match the search."), 13, false, true));
                foreach (var g in games.Take(300))
                {
                    var gp = new StackPanel();
                    var files = g.GameFiles.Concat(g.BatterySaves).Concat(g.SuspendPoints.SelectMany(x => x.Value)).Concat(g.Artwork).Concat(g.SettingsFiles).Concat(g.OtherFiles).Concat(g.PackageCopies).Distinct().ToList();
                    var gg = g; var exp = B("Export This Game…", "Btn.Ghost", () => DoExport(ExportRequest.GameBundle(c, gg, PickFolder()))); exp.Margin = new Thickness(0); exp.VerticalAlignment = VerticalAlignment.Center;
                    var head = new Grid { Margin = new Thickness(0, 0, 0, 10) }; head.ColumnDefinitions.Add(new ColumnDefinition()); head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                    var title = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0) };
                    title.Children.Add(T(g.Title + (g.TitleKnown ? "" : " (name not known)"), 14.5, true, false, new Thickness(0)));
                    title.Children.Add(T(files.Count + " file" + (files.Count == 1 ? "" : "s"), 12, false, true, new Thickness(0)));
                    head.Children.Add(title); Grid.SetColumn(exp, 1); head.Children.Add(exp);
                    gp.Children.Add(head);
                    gp.Children.Add(FileHeader());
                    foreach (var e in files.Take(60)) gp.Children.Add(FileRow(e, preview));
                    var card = Card(gp, 14); card.Margin = new Thickness(0, 0, 0, 10); listPanel.Children.Add(card);
                }
                shown = games.SelectMany(g => g.GameFiles);
            }
            else if (browseView == "type")
            {
                var counts = c.Counts(); var tabs = new WrapPanel { Margin = new Thickness(0, 0, 0, 6) };
                foreach (var cat in Categories.All) { var k = cat; var b = B(Categories.Label(cat) + " (" + counts[cat].count + ")", browseCat == cat ? "Btn.Primary" : "Btn.Ghost", () => { browseCat = k; ShowBrowse(); }); b.MinWidth = 0; b.Margin = new Thickness(0, 0, 6, 6); tabs.Children.Add(b); }
                listPanel.Children.Add(tabs);
                shown = string.IsNullOrEmpty(browseSearch) ? c.ByType(browseCat) : c.Search(browseSearch, c.ByType(browseCat));
                var table = new StackPanel(); table.Children.Add(FileHeader());
                int n = 0; foreach (var e in shown.Take(1000)) { table.Children.Add(FileRow(e, preview)); n++; }
                if (n == 0) table.Children.Add(T("Nothing of this type" + (string.IsNullOrEmpty(browseSearch) ? "." : " matches the search."), 13, false, true, new Thickness(0, 6, 0, 0)));
                var card = Card(table, 14); card.Margin = new Thickness(0, 0, 0, 10); listPanel.Children.Add(card);
                listPanel.Children.Add(Buttons(B("Export All of This Type…", "Btn", () => DoExport(ExportRequest.WholeCategory(c, browseCat, PickFolder()))),
                    !string.IsNullOrEmpty(browseSearch) ? B("Export These Search Results…", "Btn.Ghost", () => DoExport(ExportRequest.SearchResults(c, browseCat, browseSearch, shown, PickFolder()))) : null));
            }
            else
            {
                shown = string.IsNullOrEmpty(browseSearch) ? c.Children("") : c.Search(browseSearch);
                var table = new StackPanel(); table.Children.Add(FileHeader());
                // a flat row with a chevron; what is inside is listed under it the first time it is opened, with its export button
                void Expander(string label, int depth, StackPanel into, Func<IEnumerable<BrowseEntry>> kids, Func<ExportRequest> export)
                {
                    var inner = new StackPanel { Visibility = Visibility.Collapsed }; bool loaded = false;
                    var chev = new TextBlock { Text = "", FontFamily = Res<FontFamily>("F.Icons"), FontSize = 10, Foreground = Res<Brush>("B.Muted"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 10, 0) };
                    var head = new StackPanel { Orientation = Orientation.Horizontal }; head.Children.Add(chev);
                    head.Children.Add(new TextBlock { Text = label, FontSize = 13, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
                    var row = new Button { Content = head, Style = Res<Style>("Wb.Row"), Margin = new Thickness(depth * 16, 1, 0, 1) };
                    System.Windows.Automation.AutomationProperties.SetName(row, label);
                    row.Click += (s, ev) =>
                    {
                        if (!loaded) { loaded = true; Tree(kids(), depth + 1, inner); var eb = Buttons(B("Export This Folder…", "Btn.Ghost", () => DoExport(export()))); eb.Margin = new Thickness((depth + 1) * 16, 6, 0, 0); inner.Children.Add(eb); }
                        bool open = inner.Visibility != Visibility.Visible; inner.Visibility = open ? Visibility.Visible : Visibility.Collapsed; chev.Text = open ? "" : "";
                    };
                    into.Children.Add(row); into.Children.Add(inner);
                }
                void Tree(IEnumerable<BrowseEntry> es, int depth, StackPanel into)
                {
                    foreach (var e in es.Take(2000))
                    {
                        var ee = e;
                        if (e.Kind == EntryKind.Folder) { Expander(e.Name + "/", depth, into, () => c.Children(ee.Id), () => ExportRequest.Folder(c, ee, PickFolder())); continue; }
                        into.Children.Add(FileRow(e, preview, depth * 16));
                        // the data storage image is a part of the backup file that also holds the console's files: they open under it
                        if (c.Children(e.Id).Count > 0)
                            Expander("Files inside " + e.Name, depth, into, () => c.Children(ee.Id), () =>
                            {
                                var dest = PickFolder(); if (dest == null) return null;
                                var r = new ExportRequest { Destination = dest, Scope = ExportScope.Folder, Layout = ExportLayout.OriginalPaths, FolderName = ee.Member == c.DataMember && ee.Source == EntrySource.Member ? "Console files" : "Files inside " + ee.Name, ScopeText = "Everything inside " + ee.DisplayPath + ", with its original folders." };
                                r.Entries.AddRange(c.Children(ee.Id)); return r;
                            });
                    }
                }
                Tree(shown, 0, table);
                var card = Card(table, 14); card.Margin = new Thickness(0, 0, 0, 10); listPanel.Children.Add(card);
            }
            var listHost = new ScrollViewer { Content = listPanel, MaxHeight = 520, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Padding = new Thickness(0, 0, 6, 0) };
            var previewCard = Card(preview, 16); previewCard.Margin = new Thickness(0, 0, 0, 10); previewCard.VerticalAlignment = VerticalAlignment.Top;
            sp.Children.Add(SideBySide(listHost, previewCard, 320, 860));
            sp.Children.Add(Buttons(B("Export Selected…", "Btn.Primary", () => { if (browseSel.Count == 0) { MessageBox.Show(this, "Select the files to export first: check the box beside each one.", ProductTitle); return; } DoExport(ExportRequest.Selected(c, browseSel, PickFolder())); }),
                                    B("Clear Selection", "Btn.Ghost", () => { browseSel.Clear(); ShowBrowse(); }), c.Live ? B("Back to the Check-Up", "Btn.Ghost", () => ShowCheckup(lastCheckup)) : B("Back to the Backup List", "Btn.Ghost", BackToList)));
            sp.Children.Add(T("Exports copy files to a new folder on this PC that you choose; each one is read back and compared with its checksum. Nothing in the " + (c.Live ? "console" : "backup") + " changes.", Ty.Fine, false, true, new Thickness(0, 2, 0, 0)));
            Show(sp, "browse");
        }

        /// <summary>the column names over a list of files</summary>
        Grid FileHeader()
        {
            var g = FileGrid(0); g.Margin = new Thickness(0, 0, 0, 4);
            TextBlock H(string t, int col, TextAlignment a = TextAlignment.Left) { var x = new TextBlock { Text = t, FontSize = 11.5, FontWeight = FontWeights.SemiBold, Foreground = Res<Brush>("B.Muted"), TextAlignment = a, TextTrimming = TextTrimming.CharacterEllipsis }; Grid.SetColumn(x, col); g.Children.Add(x); return x; }
            H("NAME", 1); H("SIZE", 2, TextAlignment.Right).Margin = new Thickness(0, 0, 16, 0); H("DATE", 3).ToolTip = "The date each file has on the console, by the console's own clock (often not set).";
            return g;
        }
        static Grid FileGrid(double indent)
        {
            var g = new Grid { Margin = new Thickness(indent, 0, 0, 0) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(30) });
            g.ColumnDefinitions.Add(new ColumnDefinition { MinWidth = 120 });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(92) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(150) });
            return g;
        }

        /// <summary>one file: its check box (selection), its name with its folder under it (click to preview), its size and its date</summary>
        FrameworkElement FileRow(BrowseEntry e, StackPanel preview, double indent = 0)
        {
            var g = FileGrid(indent);
            // a file that cannot be copied (damaged, or not checkable in a damaged backup) is shown with its state and cannot be selected
            bool blocked = e.Kind == EntryKind.File && e.Unreadable != null;
            var cb = new CheckBox { IsChecked = browseSel.Contains(e) && !blocked, IsEnabled = !blocked, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2, 0, 0, 0) };
            System.Windows.Automation.AutomationProperties.SetName(cb, "Select " + e.DisplayPath);
            cb.Click += (s, ev) => { browseSel.Toggle(e); };
            g.Children.Add(cb);
            var nm = new StackPanel { Margin = new Thickness(0, 5, 12, 5), Background = Brushes.Transparent, Cursor = System.Windows.Input.Cursors.Hand, ToolTip = e.Unreadable ?? e.DisplayPath };
            var name = string.IsNullOrEmpty(e.Name) ? e.DisplayPath : e.Name;
            // a file inside a system image or the package gets its checksum from its verified holder when it is read: not marked
            var noSum = (e.HashOrigin == HashOrigin.None || e.HashOrigin == HashOrigin.Unreadable) && e.Container == null;
            var mark = e.StateWord ?? (blocked ? "damaged" : e.Private != null ? "private" : noSum && e.Kind == EntryKind.File ? "no checksum" : null);
            var nt = new TextBlock { Text = name + (mark != null ? "  (" + mark + ")" : ""), FontSize = 13, TextTrimming = TextTrimming.CharacterEllipsis };
            if (blocked) nt.Foreground = Res<Brush>("B.Muted");
            nm.Children.Add(nt);
            var folder = e.DisplayPath != null && e.DisplayPath.Length > name.Length && e.DisplayPath.EndsWith(name) ? e.DisplayPath.Substring(0, e.DisplayPath.Length - name.Length).TrimEnd('/') : null;
            if (!string.IsNullOrEmpty(folder)) nm.Children.Add(new TextBlock { Text = folder, FontSize = 11.5, Foreground = Res<Brush>("B.Muted"), TextTrimming = TextTrimming.CharacterEllipsis });
            nm.MouseLeftButtonUp += (s, ev) => Preview(e, preview);
            Grid.SetColumn(nm, 1); g.Children.Add(nm);
            var size = new TextBlock { Text = e.Kind == EntryKind.Folder ? "" : Units.Gb(e.Size), FontSize = 12.5, Foreground = Res<Brush>("B.Ink2"), TextAlignment = TextAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 16, 0) };
            Grid.SetColumn(size, 2); g.Children.Add(size);
            var date = new TextBlock { Text = e.MtimeUtc?.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) ?? "No date", FontSize = 12.5, Foreground = Res<Brush>("B.Muted"), VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = e.DateText };
            Grid.SetColumn(date, 3); g.Children.Add(date);
            var row = new Border { Child = g, BorderBrush = Res<Brush>("B.Line"), BorderThickness = new Thickness(0, 1, 0, 0), Background = Brushes.Transparent };
            row.MouseEnter += (s, ev) => row.Background = Res<Brush>("B.Hover"); row.MouseLeave += (s, ev) => row.Background = Brushes.Transparent;
            return row;
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
            if (e.Kind == EntryKind.File && browsing?.Live == false) { var ee = e; acts.Add(B(e.Private != null ? "Export the Key File…" : e.IsImage ? "Export This Image…" : "Export This File…", "Btn.Ghost", () => DoExport(OneEntry(browsing, ee, PickFolder())))); }
            var saveArea = new StackPanel();
            if (e.Category == Category.Saves && e.Kind == EntryKind.File && e.Source == EntrySource.DataFile) acts.Add(B("Convert to a Game Save File", "Btn.Ghost", () => ConvertSave(e, saveArea)));
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

        string PickFolder()
        {
            using (var d = new System.Windows.Forms.FolderBrowserDialog { Description = "Choose where to copy the files (a new folder is made inside it)", ShowNewFolderButton = true })
                return d.ShowDialog(new Win32Owner(this)) == System.Windows.Forms.DialogResult.OK ? d.SelectedPath : null;
        }

        /// <summary>this window as the owner of a Windows Forms dialog (a folder picker), so the dialog stays in front of it</summary>
        sealed class Win32Owner : System.Windows.Forms.IWin32Window
        {
            public Win32Owner(Window w) { Handle = new System.Windows.Interop.WindowInteropHelper(w).Handle; }
            public IntPtr Handle { get; }
        }

        void DoExport(ExportRequest r)
        {
            if (r == null || string.IsNullOrEmpty(r.Destination)) return;
            var why = Exporter.CheckDestination(r.Destination); if (why != null) { MessageBox.Show(this, why, ProductTitle); return; }
            var cts = new CancellationTokenSource();
            var pv = new ProgressView(this, "Copying Files", "The files are copied to a new folder on this PC. Each one is read back and compared with its checksum before it gets its name. Nothing in the " + (browsing?.Live == true ? "console" : "backup") + " changes.", "Cancel", () => cts.Cancel());
            Show(pv.Panel, "progress"); var c = browsing;
            Task.Run(() => Exporter.Export(c, r, () => cts.IsCancellationRequested, p => Dispatcher.BeginInvoke(new Action(() => pv.Update(p.Current ?? p.Phase, p.BytesDone, p.BytesTotal))))).ContinueWith(t =>
            {
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
            tech.Add(BackupContents.ClockText);
            sp.Children.Add(Details("Technical details", tech.ToArray()));
            var folder = x.Folder;
            sp.Children.Add(Buttons(folder != null && Directory.Exists(folder) ? B("Open the Folder", "Btn", () => { try { System.Diagnostics.Process.Start("explorer.exe", "\"" + folder + "\""); } catch { } }) : null,
                                    B(c?.Live == true ? "Back to the Console's Files" : "Back to the Backup", "Btn.Ghost", () => ShowBrowse())));
            Show(sp, "export-result");
        }

        /// <summary>one entry on its own, with its place as stored (a storage part under "Backup parts", a file inside an image under "Files
        /// inside" the image)</summary>
        static ExportRequest OneEntry(BackupContents c, BrowseEntry e, string dest)
        {
            if (dest == null) return null;
            var r = new ExportRequest { Destination = dest, Scope = ExportScope.Selection, Layout = ExportLayout.OriginalPaths, FolderName = e.Name, ShownCount = 1, ScopeText = "One item: " + e.DisplayPath + "." };
            r.Entries.Add(e); return r;
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

        string PickBackupFolder()
        {
            using (var d = new System.Windows.Forms.FolderBrowserDialog { Description = "Choose where to save the repaired backup file (the damaged file is not changed)", ShowNewFolderButton = true })
                return d.ShowDialog(new Win32Owner(this)) == System.Windows.Forms.DialogResult.OK ? d.SelectedPath : null;
        }
    }
}
