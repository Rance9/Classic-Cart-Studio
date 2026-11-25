// Classic Cart Studio console backup and restore window. Copyright (C) 2026 Rance9. GPL-3.0-or-later.
// USB and SD Backup (side column, Backup Files): backups of USB drives, SD cards, folders and drive image files on this PC, without a
// console (the engine: CcsBackup/External). The pages:
//   storage-home      the two tasks and the list of USB and SD backups, each with Check the File, Browse (the Backup Browser's own views),
//                     Repair… and Restore…
//   storage-select    what to back up: the console's built-in storage (a console backup is a separate file: Back Up Console), each USB drive
//                     and SD card connected now with Files and Folders (the default) or Whole Drive Image (advanced; also for a drive without
//                     a drive letter: WorkbenchStorageDisk.cs), chosen folders, what is left out, a folder or a drive image file; the space
//                     needed worked out from the actual selected bytes (the backup, its additional copy, recovery data and room to repair
//                     it), with why used files and a whole image differ in size
//   storage-result    what was copied, what was not and why, when each source was read, the file and its additional copy
//   storage-repair    the file checked, then: a second copy, recovery data (make, check, rebuild), repair from other copies or backups of the
//                     same storage, and Save What Can Be Saved; each makes something new and checks it, the backup never changes
//   storage-restore   files into a folder or onto a drive (merge or replace, reviewed), or a whole drive image onto a drive image file or
//                     a connected USB drive or SD card (reviewed, destructive: WorkbenchStorageDisk.cs)
// Every task here runs on this PC only: no console, no USB driver, no worker. Reading or writing a connected drive whole is done by
// Workbench's administrator step, one Windows permission prompt per operation (CcsBackup/External/RawDiskClient.cs).
using CcsBackup.Engine;
using CcsBackup.External;
using CcsBackup.Repair;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace CcsBackup.Ui
{
    public sealed partial class BackupWorkspace
    {
        sealed class StorageChoice { public StorageRequest Req; public SourceIdentity Id; public bool Connected; }
        readonly List<StorageChoice> storageSel = new List<StorageChoice>();
        int storageParity = 0; bool storageSecond = true;
        StorageEstimate storageEst;

        const string StorageIntro = "Back up the USB drives and SD cards you use with your console, or any folder or drive image file, on this PC. No console is needed, and backing up only reads the drives.";
        const string StorageSizeNote = "A Files and Folders backup is about as large as the files you choose. A Whole Drive Image is as large as the whole drive, used or not.";

        // ------------------------------------------------------------------ the first page
        void ShowStorageHome()
        {
            settings = Reload();
            var sp = SectionHead("USB and SD Backup", StorageIntro);
            AddUnfinishedDiskNotes(sp);
            sp.Children.Add(CardGrid(300, 2,
                ActionCard("Back Up USB or SD Storage", "Choose drives, folders or a drive image file, see how much space the backup needs, then make a checked backup. Nothing on the drives changes.", "Btn.Primary", () => { storageSel.Clear(); storageEst = null; ShowStorageSelect(); }),
                ActionCard("Restore From a USB or SD Backup", "Put files back into a folder or onto a drive, or write a whole drive image onto a USB drive, SD card or drive image file. You review every change first.", "Btn", () => { var f = PickStorageBackup(); if (f != null) ShowStorageRestore(f); }, "Choose a Backup…")));
            sp.Children.Add(Group("Your USB and SD backups"));
            List<StorageBackupRow> rows;
            try { rows = StorageLibrary.List(settings); }
            catch (Exception ex) { sp.Children.Add(Note("Your backup location could not be read: " + ex.Message, "bad")); Show(sp, "storage-home"); return; }
            if (rows.Count == 0)
            {
                var c = new StackPanel(); c.Children.Add(T("No USB or SD backups yet. Back Up USB or SD Storage saves them in this folder:", Ty.Body, false, true)); c.Children.Add(PathText(StorageBackup.Folder(settings)));
                sp.Children.Add(Card(c));
            }
            foreach (var r in rows)
            {
                var x = r; var row = new StackPanel();
                var top = new Grid(); top.ColumnDefinitions.Add(new ColumnDefinition()); top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var who = new StackPanel { Margin = new Thickness(0, 0, 12, 0) };
                who.Children.Add(T(r.MadeUtc?.ToLocalTime().ToString("dddd, MMMM d, yyyy 'at' h:mm tt", En) ?? "Date not known", Ty.Body, true, false, new Thickness(0, 0, 0, 2)));
                var bits = new List<string>(); if (r.Label != null) bits.Add(r.Label); if (r.Bytes > 0) bits.Add(Size(r.Bytes)); if (r.Copy == "Additional copy") bits.Add("additional copy");
                if (r.Sources.Count > 0) bits.Add(string.Join(", ", r.Sources.Select(s => s.mode == "image" ? "whole drive image" : "files and folders").Distinct()));
                who.Children.Add(T(string.Join("  ·  ", bits), Ty.Small, false, true, new Thickness(0)));
                if (r.Purpose != null) who.Children.Add(T("Made " + r.Purpose.Substring(0, 1).ToLowerInvariant() + r.Purpose.Substring(1).TrimEnd('.') + ".", Ty.Small, false, true, new Thickness(0, 2, 0, 0)));
                if (r.Detail != null) who.Children.Add(T(r.Detail, Ty.Small, false, true, new Thickness(0, 2, 0, 0)));
                top.Children.Add(who);
                var pill = Pill(r.StatusText, r.Status == "complete" ? "B.Ok" : r.Status == "not-checked" || r.Status == "changed" || r.Status == "incomplete-scope" ? "B.Warn" : "B.Red3"); pill.VerticalAlignment = VerticalAlignment.Top; Grid.SetColumn(pill, 1); top.Children.Add(pill);
                row.Children.Add(top);
                var path = PathText(r.Path, 12); path.Margin = new Thickness(0, 6, 0, 0); row.Children.Add(path);
                bool exists = File.Exists(r.Path) && r.Status != "unfinished";
                var acts = Buttons(exists ? B("Check the File", "Btn.Ghost", () => CheckStorage(x.Path)) : null, r.CanOpen ? B("Browse", "Btn", () => OpenBrowse(x.Path, false)) : null,
                    exists ? B("Repair…", "Btn.Ghost", () => ShowStorageRepair(x.Path)) : null, r.CanOpen ? B("Restore…", "Btn.Ghost", () => ShowStorageRestore(x.Path)) : null, B("Open Folder", "Btn.Ghost", () => OpenFolder(x.Path)));
                acts.Margin = new Thickness(0, 10, 0, -10); row.Children.Add(acts);
                var card = Card(row, 16); card.Margin = new Thickness(0, 0, 0, 10); sp.Children.Add(card);
            }
            sp.Children.Add(T(StorageSizeNote + " A passed file check means the file was intact when it was checked; it is not a test of restoring it.", Ty.Fine, false, true, new Thickness(0, 2, 0, 0)));
            Show(sp, "storage-home");
        }

        string PickStorageBackup()
        {
            var chosenFile = CcsFileDialog.OpenFile(this, "Choose a USB and SD backup", Directory.Exists(StorageBackup.Folder(settings)) ? StorageBackup.Folder(settings) : null, "USB and SD backups (*.zip)|*.zip", "storage-backup");
            if (chosenFile == null) return null;
            if (!ExternalFormat.Is(chosenFile)) { Tell("Not a USB and SD backup", "That file is not a USB and SD backup made by Workbench. A console backup is restored with Console Restore."); return null; }
            return chosenFile;
        }

        void CheckStorage(string path)
        {
            var cts = new CancellationTokenSource();
            var pv = new ProgressView(this, "Checking the Backup File", "Every part of the file is read and compared with its recorded checksums. Nothing is written to it.", "Cancel", () => cts.Cancel()); Show(pv.Panel, "progress");
            Task.Run(() => StorageLibrary.Check(Reload(), path, () => cts.IsCancellationRequested, (d, t) => Dispatcher.BeginInvoke(new Action(() => pv.Update("Checking...", d, t))))).ContinueWith(t =>
            {
                if (page.Content != pv.Panel) return;
                if (t.IsFaulted) { Show(Message("The file could not be checked", t.Exception.GetBaseException().Message, "bad"), "msg"); return; }
                var v = t.Result;
                if (cts.IsCancellationRequested) { ShowStorageHome(); UpdateStatus("The check was canceled; nothing was recorded."); return; }
                var sp = PageHead(v.Ok ? "The backup file passed its check" : "The backup file did not pass its check", null);
                sp.Children.Add(PathText(path, 13));
                sp.Children.Add(Note(v.Ok ? "Every part, every piece and every file list matches what was recorded when the backup was made" + (v.ScopeComplete ? "." : ". It says itself that not everything selected was copied: see its details when you browse it.") : "Part of the file no longer matches what was recorded when the backup was made: it is damaged. Repair… can make a new, repaired file from it and other copies, or save what is still intact. The file itself is not changed.", v.Ok ? "ok" : "bad"));
                sp.Children.Add(Details("Technical details", v.ArchiveSha256 != null ? "SHA-256 of the file: " + v.ArchiveSha256 : null, v.BackupId != null ? "Backup ID: " + v.BackupId : null, v.Ok ? "Files checked against their lists: " + v.FilesChecked : string.Join(Environment.NewLine, v.Problems.Take(20))));
                sp.Children.Add(Buttons(B("Back to USB and SD Backup", "Btn", () => Go("USB and SD Backup")), v.Ok ? null : B("Repair…", "Btn.Ghost", () => ShowStorageRepair(path))));
                Show(sp, "storage-result");
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        // ------------------------------------------------------------------ choosing what to back up
        void ShowStorageSelect()
        {
            settings = Reload();
            var sp = PageHead("Back Up USB or SD Storage", "Choose what to back up. The drives and folders are only read: nothing on them changes.");
            // the console's own storage: a separate file, made by Back Up Console
            var inner = new StackPanel();
            inner.Children.Add(T("Your console's built-in storage", Ty.Heading, true));
            inner.Children.Add(T("It is backed up with Back Up Console, as a console backup: a separate file. A console backup and a USB and SD backup are made one after the other, not at the same moment, and each says when it was made.", Ty.Text, false, true));
            inner.Children.Add(Buttons(B("Back Up Console", "Btn.Ghost", () => { if (LeaveOk()) { Enter("Console Backup"); ShowBackup(); } })));
            sp.Children.Add(Card(inner));
            // USB drives and SD cards connected now
            sp.Children.Add(Group("USB drives and SD cards connected now"));
            List<SourceIdentity> connected;
            try { connected = Devices.Connected(); } catch (Exception) { connected = new List<SourceIdentity>(); }
            if (connected.Count == 0) sp.Children.Add(Note("No USB drive or SD card is connected that Windows shows with a drive letter. Connect one, then choose Look Again.", "info"));
            foreach (var id in connected)
            {
                var ch = storageSel.FirstOrDefault(x => x.Connected && x.Id.Key == id.Key);
                var c = new StackPanel();
                var cb = new CheckBox { Content = new TextBlock { Text = id.Display, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap }, IsChecked = ch != null, Foreground = Res<Brush>("B.Ink") };
                c.Children.Add(cb);
                c.Children.Add(T((id.FileSystem ?? "file system not known") + "  ·  " + (id.TotalBytes > 0 ? Size(id.TotalBytes - Math.Max(0, id.FreeBytes)) + " used of " + Size(id.TotalBytes) : "size not known") + (id.Vendor != null ? "  ·  " + (id.Vendor + " " + id.Product).Trim() : ""), Ty.Small, false, true, new Thickness(26, 2, 0, 4)));
                var opts = new StackPanel { Margin = new Thickness(26, 4, 0, 0), Visibility = ch != null ? Visibility.Visible : Visibility.Collapsed };
                bool isImage = ch?.Req.Mode == "image";
                var files = new RadioButton { GroupName = "mode-" + id.Key, Content = "Files and Folders (recommended): the files on it, with their dates", IsChecked = !isImage, Margin = new Thickness(0, 2, 0, 2) };
                var image = new RadioButton { GroupName = "mode-" + id.Key, Content = "Whole Drive Image (advanced): every byte of the drive, free space included", IsChecked = isImage, IsEnabled = Gate.StorageRawReads && id.DiskNumber >= 0, Margin = new Thickness(0, 2, 0, 2) };
                opts.Children.Add(files); opts.Children.Add(image);
                var imageNote = DiskModeNote(id.DiskNumber); imageNote.Visibility = isImage ? Visibility.Visible : Visibility.Collapsed; opts.Children.Add(imageNote);
                if (!Gate.StorageRawReads) opts.Children.Add(T(Gate.StorageRawText + " To keep a whole image of a drive, make one with another program and add it below as a drive image file.", Ty.Small, false, true, new Thickness(22, 0, 0, 6)));
                var folders = new StackPanel { Margin = new Thickness(0, 4, 0, 0) };
                void ShowFolders()
                {
                    folders.Children.Clear(); var cur = storageSel.FirstOrDefault(x => x.Connected && x.Id.Key == id.Key);
                    var list = cur?.Req.Folders;
                    folders.Children.Add(T(list == null || list.Count == 0 ? "Everything on the drive." : "Only these folders: " + string.Join(", ", list), Ty.Small, false, false, new Thickness(0, 0, 0, 2)));
                    folders.Children.Add(T("Left out: System Volume Information and $RECYCLE.BIN, Windows' own folders on the drive.", Ty.Small, false, true, new Thickness(0, 0, 0, 4)));
                    folders.Children.Add(Buttons(B("Choose Folders…", "Btn.Ghost", () => { var f = PickFolderIn(id.Root); if (f != null && cur != null) { cur.Req.Folders = (cur.Req.Folders ?? new List<string>()).Concat(new[] { f }).Distinct().ToList(); storageEst = null; ShowFolders(); } }),
                        list != null && list.Count > 0 ? B("Everything on the Drive", "Btn.Ghost", () => { cur.Req.Folders = null; storageEst = null; ShowFolders(); }) : null));
                }
                opts.Children.Add(folders); folders.Visibility = isImage ? Visibility.Collapsed : Visibility.Visible;
                c.Children.Add(opts);
                StorageChoice Cur() => storageSel.FirstOrDefault(x => x.Connected && x.Id.Key == id.Key);
                // Whole Drive Image copies the drive this volume is on, every volume of it; Files and Folders this volume's files
                files.Checked += (s, e) => { var cur = Cur(); if (cur != null) { cur.Req.Mode = "files"; cur.Req.DiskNumber = -1; cur.Req.Disk = null; cur.Req.Path = id.Root; } folders.Visibility = Visibility.Visible; imageNote.Visibility = Visibility.Collapsed; storageEst = null; };
                image.Checked += (s, e) => { var cur = Cur(); if (cur != null) { cur.Req.Mode = "image"; cur.Req.DiskNumber = id.DiskNumber; cur.Req.Disk = null; cur.Req.Folders = null; } folders.Visibility = Visibility.Collapsed; imageNote.Visibility = Visibility.Visible; storageEst = null; };
                cb.Checked += (s, e) => { if (!storageSel.Any(x => x.Connected && x.Id.Key == id.Key)) storageSel.Add(new StorageChoice { Req = image.IsChecked == true ? new StorageRequest { Path = id.Root, Mode = "image", DiskNumber = id.DiskNumber } : new StorageRequest { Path = id.Root }, Id = id, Connected = true }); opts.Visibility = Visibility.Visible; storageEst = null; ShowFolders(); };
                cb.Unchecked += (s, e) => { storageSel.RemoveAll(x => x.Connected && x.Id.Key == id.Key); opts.Visibility = Visibility.Collapsed; storageEst = null; };
                if (ch != null) ShowFolders();
                var card = Card(c, 16); card.Margin = new Thickness(0, 0, 0, 10); sp.Children.Add(card);
            }
            AddUnletteredDrives(sp, connected);
            sp.Children.Add(Buttons(B("Look Again", "Btn.Ghost", ShowStorageSelect), B("Add a Folder…", "Btn.Ghost", () => { var f = PickFolderIn(null); if (f != null) { storageSel.Add(new StorageChoice { Req = new StorageRequest { Path = f }, Id = Devices.ForFolder(f) }); storageEst = null; ShowStorageSelect(); } }),
                B("Add a Drive Image File…", "Btn.Ghost", () => { var chosenFile = CcsFileDialog.OpenFile(this, "Choose a drive image file (a copy of a whole USB drive or SD card)", null, "Drive images (*.img;*.bin;*.raw;*.iso)|*.img;*.bin;*.raw;*.iso|All files (*.*)|*.*", "drive-image"); if (chosenFile != null) { try { storageSel.Add(new StorageChoice { Req = new StorageRequest { Path = chosenFile, Mode = "image" }, Id = Devices.ForImage(chosenFile) }); storageEst = null; } catch (Exception ex) { Tell("That file cannot be used", ex.Message); } ShowStorageSelect(); } })));
            var others = storageSel.Where(x => !x.Connected).ToList();
            if (others.Count > 0)
            {
                var c = new StackPanel(); c.Children.Add(T("Also chosen", Ty.Heading, true));
                foreach (var o in others) { var oo = o; c.Children.Add(Inline(oo.Id.Display + (oo.Req.Mode == "image" ? " (whole drive image)" : "") + "  ·  " + oo.Req.Path, B("Remove", "Btn.Ghost", () => { storageSel.Remove(oo); storageEst = null; ShowStorageSelect(); }))); }
                sp.Children.Add(Card(c));
            }
            // the space needed, from the actual selected bytes
            sp.Children.Add(Group("Space needed"));
            var space = new StackPanel();
            var parity = new ComboBox();
            foreach (var (pct, label) in new[] { (0, "No recovery data"), (5, "Recovery data: 5% of the file"), (10, "Recovery data: 10% of the file") }) parity.Items.Add(new ComboBoxItem { Content = label, Tag = pct, IsSelected = pct == storageParity });
            parity.SelectionChanged += (s, e) => { storageParity = (int)((ComboBoxItem)parity.SelectedItem).Tag; storageEst = null; };
            space.Children.Add(Field("Recovery data lets Workbench rebuild the file later if part of it is damaged", parity, null, 420));
            bool hasSecond = !string.IsNullOrEmpty(settings.ArchiveFolder);
            var second = new CheckBox { Content = new TextBlock { Text = hasSecond ? "Make an additional copy in your additional-copy location (recommended: on another drive)" : "Make an additional copy (set an additional-copy location in Settings first)", TextWrapping = TextWrapping.Wrap }, IsChecked = hasSecond && storageSecond, IsEnabled = hasSecond, Foreground = Res<Brush>("B.Ink"), Margin = new Thickness(0, 0, 0, 10) };
            second.Checked += (s, e) => { storageSecond = true; storageEst = null; }; second.Unchecked += (s, e) => { storageSecond = false; storageEst = null; };
            space.Children.Add(second);
            if (storageEst != null)
            {
                var e = storageEst; bool withCopy = hasSecond && storageSecond;
                space.Children.Add(KeyValues(("Files chosen", e.Files > 0 || e.DataBytes > 0 ? Count(e.Files, "file") + ", " + Size(e.DataBytes) : null), ("Drive images", e.ImageBytes > 0 ? Size(e.ImageBytes) : null),
                    ("The backup file", "about " + Size(e.BackupBytes)), ("Its additional copy", withCopy ? "about " + Size(e.BackupBytes) + ", in your additional-copy location" : hasSecond ? "none (not chosen)" : "none (no additional-copy location is set)"),
                    ("Recovery data", e.ParityBytes > 0 ? "about " + Size(e.ParityBytes) : "none"), ("Room to repair it later", "about " + Size(e.WorkspaceBytes) + " (a repair writes a new file beside it)"),
                    ("Free in your backup location", e.FreeBytes >= 0 ? Size(e.FreeBytes) : "not known")));
                space.Children.Add(Note(e.SizeNote, "info"));
                if (!e.Fits) space.Children.Add(Note("There is not enough free space in your backup location for this backup. Free some space, or choose another location in Settings.", "bad"));
            }
            else space.Children.Add(T(StorageSizeNote + " Work Out the Space lists the chosen folders and adds up their files.", Ty.Text, false, true));
            space.Children.Add(T("Saved in:", Ty.Small, false, true, new Thickness(0, 6, 0, 2))); space.Children.Add(PathText(StorageBackup.Folder(settings)));
            sp.Children.Add(Card(space));
            var go = B("Back Up", "Btn.Primary", StartStorageBackup, storageSel.Count > 0 && (storageEst == null || storageEst.Fits));
            sp.Children.Add(Buttons(B("Work Out the Space", "Btn", WorkOutStorageSpace, storageSel.Count > 0), go, B("Cancel", "Btn.Ghost", () => Go("USB and SD Backup"))));
            Show(sp, "storage-select");
        }

        /// <summary>a folder chosen by the person (inside root when given); returned relative to root ("/hakchi"), or the full path when root is null</summary>
        string PickFolderIn(string root)
        {
            var chosen = CcsFileDialog.ChooseFolder(this, root == null ? "Choose a folder to back up" : "Choose a folder on " + root + " to back up", root, "storage-source", newFolder: false);
            if (chosen == null) return null;
            if (root == null) return chosen;
            var full = Path.GetFullPath(chosen); var r = Path.GetFullPath(root).TrimEnd('\\');
            if (!full.StartsWith(r, StringComparison.OrdinalIgnoreCase)) { Tell("Choose a folder on that drive", "The folder must be on " + root + "."); return null; }
            var rel = full.Substring(r.Length).Replace('\\', '/'); return rel.Length == 0 ? "/" : rel;
        }

        void WorkOutStorageSpace()
        {
            var reqs = storageSel.Select(x => x.Req).ToList(); var cts = new CancellationTokenSource(); var s = Reload();
            var pv = new ProgressView(this, "Working Out the Space", "The chosen folders are listed and their files added up. Nothing is copied yet.", "Cancel", () => cts.Cancel()); Show(pv.Panel, "progress");
            Task.Run(() => StorageBackup.Estimate(reqs, StorageBackup.Folder(s), !string.IsNullOrEmpty(s.ArchiveFolder) && storageSecond, storageParity, () => cts.IsCancellationRequested)).ContinueWith(t =>
            {
                if (page.Content != pv.Panel) return;
                if (t.IsFaulted) { var ex = t.Exception.GetBaseException(); if (ex is OperationCanceledException) { ShowStorageSelect(); return; } Show(Message("The space could not be worked out", ex is StorageRefusedException ? ex.Message : "A drive or folder could not be read: " + ex.Message, "bad"), "msg"); return; }
                storageEst = t.Result; ShowStorageSelect();
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        void StartStorageBackup()
        {
            var reqs = storageSel.Select(x => x.Req).ToList(); var cts = new CancellationTokenSource(); var s = Reload(); int pct = storageParity;
            var pv = new ProgressView(this, "Backing Up USB or SD Storage", "The drives are only read. Keep them connected until this finishes. Cancel stops it: the unfinished file is never used as a backup.", "Cancel", () => cts.Cancel()); Show(pv.Panel, "progress");
            Task.Run(() => StorageBackup.Run(reqs, new StorageOptions { Settings = s, ParityPercent = pct, SecondCopy = storageSecond && !string.IsNullOrEmpty(s.ArchiveFolder), Entry = opt.HostedInFullApp ? "studio" : "workbench", Cancelled = () => cts.IsCancellationRequested, Progress = p => Dispatcher.BeginInvoke(new Action(() => pv.Update(p.Text, p.Done, p.Total))) })).ContinueWith(t =>
            {
                if (page.Content != pv.Panel) { UpdateStatus(t.IsFaulted ? "The USB or SD backup stopped with a problem." : t.Result.Ok ? "The USB or SD backup finished." : "The USB or SD backup did not finish: " + t.Result.Problem); return; }
                if (t.IsFaulted) { Show(Message("The backup stopped", "Nothing on the drives changed. The unfinished file is never used as a backup.", "bad", t.Exception.GetBaseException().Message), "msg"); return; }
                ShowStorageResult(t.Result);
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        void ShowStorageResult(StorageOutcome o)
        {
            var sp = new StackPanel();
            if (!o.Ok)
            {
                sp.Children.Add(PageHead(o.State == "refused" ? "The backup was not started" : o.State == "canceled" ? "The backup was canceled" : "The backup did not finish", null));
                sp.Children.Add(Note((o.Problem ?? "") + " Nothing on the drives changed.", o.State == "canceled" ? "info" : "bad"));
                sp.Children.Add(Buttons(B("Back to Choosing", "Btn", ShowStorageSelect), B("Back to USB and SD Backup", "Btn.Ghost", () => Go("USB and SD Backup"))));
                Show(sp, "storage-result"); return;
            }
            sp.Children.Add(PageHead(o.ScopeComplete ? "The backup is complete and checked" : "The backup is checked, but not everything was copied", null));
            sp.Children.Add(Note(o.ScopeComplete ? "Everything you chose was copied, and the file passed its full check." : "The file passed its full check, but some files could not be read or changed while they were copied. They are named below and in the backup; the other files can be used.", o.ScopeComplete ? "ok" : "warn"));
            var facts = new StackPanel();
            facts.Children.Add(KeyValues(("Saved as", Path.GetFileName(o.PrimaryPath)), ("Size", Size(o.Bytes)), ("Additional copy", o.ArchivePath != null ? "made and checked" + (o.SameDisk ? " (on the same drive: it protects against a damaged file, not a lost drive)" : "") : o.CopyProblem ?? "none"),
                ("Recovery data", o.ParityText == null ? null : (o.ParityOk ? "made and checked" : "not made: " + o.ParityText))));
            foreach (var x in o.Sources)
            {
                facts.Children.Add(T(x.Label + (x.Mode == "image" ? " (whole drive image)" : ""), Ty.Heading, true, false, new Thickness(0, 10, 0, 2)));
                facts.Children.Add(KeyValues(("Copied", x.Mode == "image" ? Size(x.Bytes) + " (the whole drive)" + (x.Files > 0 ? ", " + Count(x.Files, "file") + " listed for browsing" : "") : Count(x.Files, "file") + ", " + Size(x.Bytes)),
                    ("Not read", x.Unreadable > 0 ? Count(x.Unreadable, "file") : null), ("Changed while copied", x.Changed > 0 ? Count(x.Changed, "file") : null),
                    ("Left out", x.Omitted.Count > 0 ? string.Join(", ", x.Omitted.Select(om => om.path)) : null)));
                if (x.Problems.Count > 0) facts.Children.Add(Details("What could not be copied (" + x.Problems.Count + ")", x.Problems.Take(200).Cast<object>().ToArray()));
            }
            facts.Children.Add(T(o.ConsistencyText, Ty.Small, false, true, new Thickness(0, 8, 0, 0)));
            sp.Children.Add(Card(facts));
            sp.Children.Add(Details("Technical details", "File: " + o.PrimaryPath, o.ArchivePath != null ? "Additional copy: " + o.ArchivePath : null, "SHA-256 of the file: " + o.Sha256, "Backup ID: " + o.Id));
            sp.Children.Add(Buttons(B("Browse This Backup", "Btn", () => OpenBrowse(o.PrimaryPath, false)), B("Back to USB and SD Backup", "Btn.Ghost", () => Go("USB and SD Backup"))));
            Show(sp, "storage-result");
        }

        // ------------------------------------------------------------------ repair
        void ShowStorageRepair(string path)
        {
            var sp = PageHead("Repair a USB and SD Backup", "Each way here reads the backup and makes something new, checked in full. The backup file itself is never changed.");
            sp.Children.Add(PathText(path, 13));
            bool parity = Parity.FilesOf(path).Count > 0;
            sp.Children.Add(CardGrid(300, 2,
                ActionCard("Make a Second Copy", "A checked copy of a good backup in another folder, preferably on another drive. A damaged backup is not copied.", "Btn", () => { var f = PickFolder(); if (f != null) StorageRepairRun("Making a Second Copy", o => StorageRepair.MakeSecondCopy(path, f, o)); }, "Make a Second Copy…"),
                parity ? ActionCard("Recovery Data", "Check the backup against its recovery data, or rebuild a damaged backup from it into a new file.", "Btn", () => StorageParityRun(path, true), "Check or Rebuild")
                       : ActionCard("Make Recovery Data", "Extra data saved beside the backup (10% of its size) that can rebuild it later if part of it is damaged. Made only for a backup that passes its check.", "Btn", () => StorageParityRun(path, false)),
                ActionCard("Repair From Other Copies or Backups", "A new, repaired file, each damaged piece taken from another copy of this backup, or from another backup of the same drive where that piece is exactly the same.", "Btn", () =>
                {
                    var chosenFiles = CcsFileDialog.OpenFiles(this, "Choose other copies of this backup, or other backups of the same drive", null, "USB and SD backups (*.zip)|*.zip", true, "storage-backup");
                    if (chosenFiles == null) return; var donors = chosenFiles.Where(f => !string.Equals(Path.GetFullPath(f), Path.GetFullPath(path), StringComparison.OrdinalIgnoreCase)).ToList(); var outF = PickFolder(); if (outF == null) return;
                    StorageRepairRun("Repairing the Backup", o => StorageRepair.Rebuild(new[] { path }, donors, outF, o));
                }, "Choose Files…"),
                ActionCard("Save What Can Be Saved", "When a backup cannot be repaired: every file that still matches its recorded checksum is copied into a new folder. That folder is not a backup.", "Btn", () => { var outF = PickFolder(); if (outF != null) StorageRepairRun("Saving What Can Be Saved", o => StorageRepair.Salvage(new[] { path }, outF, o)); }, "Choose a Folder…")));
            sp.Children.Add(Buttons(B("Back to USB and SD Backup", "Btn.Ghost", () => Go("USB and SD Backup"))));
            Show(sp, "storage-repair");
        }

        void StorageRepairRun(string title, Func<RepairOptions, RepairResult> run)
        {
            var cts = new CancellationTokenSource();
            var pv = new ProgressView(this, title, "The files are only read; anything new is checked in full before it gets its name.", "Cancel", () => cts.Cancel()); Show(pv.Panel, "progress");
            Task.Run(() => run(new RepairOptions { Cancelled = () => cts.IsCancellationRequested, Progress = p => Dispatcher.BeginInvoke(new Action(() => pv.Update(p.Text, p.Done, p.Total))) })).ContinueWith(t =>
            {
                if (page.Content != pv.Panel) return;
                if (t.IsFaulted) { Show(Message("It stopped with a problem", "Nothing was changed in the backup.", "bad", t.Exception.GetBaseException().Message), "msg"); return; }
                var r = t.Result; var sp = PageHead(r.Outcome == "repaired" ? "A repaired backup was made" : r.Outcome == "copied" ? "A second copy was made" : r.Outcome == "salvaged" ? "Files were saved" : r.Outcome == "not-needed" ? "Nothing needed repairing" : r.Outcome == "canceled" ? "Canceled" : "It could not be done", null);
                sp.Children.Add(Note(r.Text ?? r.Outcome, r.Outcome == "repaired" || r.Outcome == "copied" || r.Outcome == "salvaged" || r.Outcome == "not-needed" ? "ok" : "bad"));
                if (r.OutputPath != null) { sp.Children.Add(T("Made:", Ty.Small, false, true, new Thickness(0, 4, 0, 2))); sp.Children.Add(PathText(r.OutputPath)); }
                sp.Children.Add(Details("Technical details", r.ReportText != null ? "Report: " + r.ReportText : null, string.Join(Environment.NewLine, r.Report?.Unrecoverable.Take(30).Select(u => u["what"] + ": " + u["why"]) ?? new string[0])));
                sp.Children.Add(Buttons(B("Back to USB and SD Backup", "Btn", () => Go("USB and SD Backup")), r.OutputPath != null ? B("Open Folder", "Btn.Ghost", () => OpenFolder(r.OutputPath)) : null));
                Show(sp, "storage-result");
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        void StorageParityRun(string path, bool exists)
        {
            var pv = new ProgressView(this, exists ? "Checking Against the Recovery Data" : "Making Recovery Data", "The backup is only read.", null, null); Show(pv.Panel, "progress");
            Task.Run(() =>
            {
                Action<string, long, long> prog = (t, d, n) => Dispatcher.BeginInvoke(new Action(() => pv.Update(t, d, n)));
                if (!exists) return StorageRepair.Protect(path, 10, prog);
                var k = StorageRepair.CheckParity(path, prog);
                return k.Report?.Status == Par2Status.DamagedRepairable ? StorageRepair.RebuildFromParity(path, prog) : k;
            }).ContinueWith(t =>
            {
                if (page.Content != pv.Panel) return;
                var r = t.IsFaulted ? new ParityResult { Text = t.Exception.GetBaseException().Message } : t.Result;
                var sp = PageHead(r.Ok ? "Done" : "It could not be done", null); sp.Children.Add(Note(r.Text ?? "", r.Ok ? "ok" : "bad"));
                if (r.OutputPath != null) { sp.Children.Add(T("Rebuilt file:", Ty.Small, false, true, new Thickness(0, 4, 0, 2))); sp.Children.Add(PathText(r.OutputPath)); }
                sp.Children.Add(Buttons(B("Back to USB and SD Backup", "Btn", () => Go("USB and SD Backup"))));
                Show(sp, "storage-result");
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        // ------------------------------------------------------------------ restore
        void ShowStorageRestore(string path)
        {
            var pv = new ProgressView(this, "Opening the Backup", "The backup is checked from scratch before anything is restored from it.", null, null); Show(pv.Panel, "progress");
            Task.Run(() => StorageVerifier.Verify(path, null, null, (d, t) => Dispatcher.BeginInvoke(new Action(() => pv.Update("Checking...", d, t))))).ContinueWith(t =>
            {
                if (page.Content != pv.Panel) return;
                var v = t.IsFaulted ? null : t.Result;
                if (v == null || !v.Ok) { Show(Message("Nothing can be restored from this file", "It did not pass its check" + (v?.Problems.Count > 0 ? ": " + v.Problems[0] : "") + ". Repair… can make a repaired copy or save what is intact.", "bad"), "msg"); return; }
                var sp = PageHead("Restore From a USB and SD Backup", "Choose what to restore and where. You review exactly what changes before anything is written.");
                sp.Children.Add(PathText(path, 13));
                foreach (var s in v.SourceList)
                {
                    var id = s.S("id"); var c = new StackPanel();
                    c.Children.Add(T(s.S("label") + (s.S("mode") == "image" ? " (whole drive image)" : ""), Ty.Heading, true));
                    var merge = new RadioButton { GroupName = "restore-mode-" + id, Content = "Keep files that are already there and differ (merge)", IsChecked = true, Margin = new Thickness(0, 4, 0, 2) };
                    var replace = new RadioButton { GroupName = "restore-mode-" + id, Content = "Replace files that differ (the ones there now are moved into a \"Replaced by restore\" folder, not deleted)", Margin = new Thickness(0, 2, 0, 6) };
                    c.Children.Add(T("Restore files into a folder or onto a drive:", Ty.Text, false, true)); c.Children.Add(merge); c.Children.Add(replace);
                    var acts = new List<UIElement> { B("Choose Where and Review…", "Btn", () => { var tgt = PickFolder(); if (tgt != null) PlanStorageFiles(path, id, tgt, replace.IsChecked == true ? "replace" : "merge"); }) };
                    if (s.S("mode") == "image")
                    {
                        acts.Add(B("Write the Whole Drive Image to a File…", "Btn.Ghost", () => { var chosenFile = CcsFileDialog.SaveFile(this, "Choose the drive image file to write (a new file, or one to replace)", null, null, "Drive images (*.img)|*.img|All files (*.*)|*.*", false, "drive-image"); if (chosenFile != null) PlanStorageImage(path, id, new DiskTarget { ImageFile = chosenFile }); }));
                        if (Gate.StorageDiskWrites) acts.Insert(1, B("Write the Whole Drive Image to a USB Drive or SD Card…", "Btn", () => ShowDiskTargets(path, id)));
                        c.Children.Add(T(Gate.StorageDiskWrites ? "The whole drive image can be written back onto a USB drive or SD card, replacing everything on it, or into a drive image file." : Gate.StorageDiskText + " The whole drive image can be written to a drive image file, which other programs can put on a card.", Ty.Small, false, true, new Thickness(0, 0, 0, 6)));
                    }
                    c.Children.Add(Buttons(acts.ToArray()));
                    sp.Children.Add(Card(c));
                }
                sp.Children.Add(Buttons(B("Back to USB and SD Backup", "Btn.Ghost", () => Go("USB and SD Backup"))));
                Show(sp, "storage-restore");
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        void PlanStorageFiles(string path, string source, string target, string mode)
        {
            var pv = new ProgressView(this, "Reviewing the Restore", "The backup is checked and the folder compared with it. Nothing is written yet.", null, null); Show(pv.Panel, "progress");
            Task.Run(() => StorageRestore.PlanFiles(path, source, null, target, mode)).ContinueWith(t =>
            {
                if (page.Content != pv.Panel) return;
                var p = t.IsFaulted ? new FileRestorePlan { Problem = t.Exception.GetBaseException().Message } : t.Result;
                if (p.Problem != null) { Show(Message("This restore cannot be done", p.Problem, "bad"), "msg"); return; }
                var sp = PageHead("Review the Restore", "Nothing has been written yet. This is exactly what happens in " + target + ".");
                int Files(string a) => p.Items.Count(i => i.Kind == "file" && i.Action == a);
                sp.Children.Add(Card(KeyValues(("From", p.SourceLabel), ("Into", target + (p.TargetFileSystem != null ? " (" + p.TargetFileSystem + ")" : "")), ("Written", Count(Files("create"), "new file") + ", " + Size(p.Bytes)),
                    ("Replaced", p.Mode == "replace" ? Count(Files("replace"), "file") + " (moved first into a \"Replaced by restore\" folder)" : null), ("Already there, the same", Count(Files("same"), "file")),
                    ("Kept as they are", Files("keep") > 0 ? Count(Files("keep"), "file") + " that differ (merge)" : null), ("Not restored", p.Count("not-restored") > 0 ? Count(p.Count("not-restored"), "item") : null),
                    ("Space needed", Size(p.NeedBytes) + (p.FreeBytes >= 0 ? " of " + Size(p.FreeBytes) + " free" : "")))));
                sp.Children.Add(Note(p.MetadataText, "info"));
                foreach (var n in p.Notes.Take(20)) sp.Children.Add(T(n, Ty.Small, false, true));
                sp.Children.Add(ChangeTable(p.Items.Where(i => i.Kind == "file" && i.Action != "same").Take(300).Select(i => (i.Action == "create" ? "Add" : i.Action == "replace" ? "Replace" : i.Action == "keep" ? "Keep" : "Skip", i.Path + (i.Why != null ? " (" + i.Why + ")" : ""), Size(i.Size)))));
                var cb = new CheckBox { Content = new TextBlock { Text = "I understand: files are written into " + target + " as listed" + (p.Mode == "replace" ? ", and the ones they replace are moved aside." : "."), TextWrapping = TextWrapping.Wrap }, Margin = new Thickness(0, 8, 0, 0), Foreground = Res<Brush>("B.Ink") };
                sp.Children.Add(cb);
                var go = B("Restore These Files", "Btn.Primary", () => RunStorageRestore("Restoring Files", () => StorageRestore.RestoreFiles(p, p.Digest, p.Nonce)), false);
                cb.Checked += (s, e) => go.IsEnabled = true; cb.Unchecked += (s, e) => go.IsEnabled = false;
                sp.Children.Add(Buttons(go, B("Cancel", "Btn.Ghost", () => ShowStorageRestore(path))));
                Show(sp, "storage-plan");
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        void PlanStorageImage(string path, string source, DiskTarget target)
        {
            var pv = new ProgressView(this, "Reviewing the Drive Image Restore", "The backup is checked and the target examined. Nothing is written yet.", null, null); Show(pv.Panel, "progress");
            var protect = StorageRestore.Protected(Reload());
            Task.Run(() => StorageRestore.PlanImage(path, source, target, null, null, protect)).ContinueWith(t =>
            {
                if (page.Content != pv.Panel) return;
                var p = t.IsFaulted ? new ImageRestorePlan { Refusal = t.Exception.GetBaseException().Message } : t.Result;
                if (p.Refusal != null) { Show(Message("This target cannot be used", p.Refusal, "bad"), "msg"); return; }
                if (p.TargetDisk != null) { ReviewDiskRestore(p, path); return; }
                var sp = PageHead("Review the Drive Image Restore", "Nothing has been written yet. This replaces what is on the target.");
                sp.Children.Add(Card(KeyValues(("From", p.SourceLabel + ", " + Size(p.ImageBytes)), ("Onto", p.TargetText))));
                foreach (var r in p.Review) sp.Children.Add(Note(r, r.Contains("lost") || r.Contains("replaced") ? "warn" : "info"));
                var cb = new CheckBox { Content = new TextBlock { Text = "I understand: everything on this target from its start up to " + Size(p.ImageBytes) + " is replaced by the drive image.", TextWrapping = TextWrapping.Wrap }, Margin = new Thickness(0, 8, 0, 0), Foreground = Res<Brush>("B.Ink") };
                sp.Children.Add(cb);
                var go = B("Write the Drive Image", "Btn.Primary", () => RunStorageRestore("Writing the Drive Image", () => StorageRestore.RestoreImage(p, p.Digest, p.Nonce, Reload())), false);
                cb.Checked += (s, e) => go.IsEnabled = true; cb.Unchecked += (s, e) => go.IsEnabled = false;
                sp.Children.Add(Buttons(go, B("Cancel", "Btn.Ghost", () => ShowStorageRestore(path))));
                Show(sp, "storage-plan");
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        void RunStorageRestore(string title, Func<StorageRestoreResult> run)
        {
            var pv = new ProgressView(this, title, "Each file is checked against its recorded checksum while it is written, and read back.", null, null); Show(pv.Panel, "progress");
            Task.Run(run).ContinueWith(t =>
            {
                if (page.Content != pv.Panel) return;
                var r = t.IsFaulted ? new StorageRestoreResult { Text = t.Exception.GetBaseException().Message } : t.Result;
                var sp = PageHead(r.Ok ? "Restored" : "The restore did not finish as planned", null);
                sp.Children.Add(Note(r.Text, r.Ok ? "ok" : "bad"));
                if (r.ProtectionBackup != null) { sp.Children.Add(T("What was on the target before is in this backup:", Ty.Small, false, true, new Thickness(0, 4, 0, 2))); sp.Children.Add(PathText(r.ProtectionBackup)); }
                if (r.Problems.Count > 0) sp.Children.Add(Details("What did not work (" + r.Problems.Count + ")", r.Problems.Take(100).Cast<object>().ToArray()));
                sp.Children.Add(Buttons(B("Back to USB and SD Backup", "Btn", () => Go("USB and SD Backup"))));
                Show(sp, "storage-result");
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }
    }
}
