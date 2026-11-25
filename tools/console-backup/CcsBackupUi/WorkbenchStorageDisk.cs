// Classic Cart Studio console backup and restore window. Copyright (C) 2026 Rance9. GPL-3.0-or-later.
// USB and SD Backup's whole-drive pages (the engine: CcsBackup/External/RawDisk*.cs). Files and Folders stays the default way to back up a
// USB drive or SD card; Whole Drive Image is the advanced choice, for a drive with a drive letter or one without (a card Windows cannot
// read, such as one formatted on a console). Reading or writing a drive whole needs administrator permission: Windows asks once for each
// operation, and the rest of the window never has it.
//   storage-disk-targets   the USB drives and SD cards connected now, each named as Windows reports it (maker and model, size, serial
//                          number, how it is connected, its volumes), to write a whole drive image onto
//   storage-disk-review    the destructive restore reviewed: the drive, exactly what is replaced and lost (nothing is backed up first),
//                          whether it looks like the drive the backup was made from; then a checkbox AND the word REPLACE typed in a box
//                          before Write the Drive Image is available. The drive is checked again by Workbench's administrator step right
//                          before it writes.
//   storage-disk-result    what happened, exactly: written and read back; nothing written (a refused prompt, a refused drive, canceled
//                          before the go-ahead); or the drive incomplete (canceled part way, stopped, interrupted, read back differently)
// On USB and SD Backup's first page, a whole-drive restore that did not finish is named until that drive is restored completely.
using CcsBackup.Engine;
using CcsBackup.External;
using System;
using System.Collections.Generic;
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
        /// <summary>the one sentence about Windows' permission prompt for a whole-drive operation, for this person's account</summary>
        static string DiskAdminSentence()
        {
            switch (CcsBackup.Driver.Elevation.Current())
            {
                case CcsBackup.Driver.ElevationState.Elevated: return "Workbench already has administrator permission, so Windows does not ask again.";
                case CcsBackup.Driver.ElevationState.StandardUser: return "Windows asks for an administrator's name and password once for this operation.";
                default: return "Windows asks for administrator permission once for this operation.";
            }
        }

        /// <summary>what Whole Drive Image means for a connected drive, under its choice (disk: the drive a chosen volume is on, named with every
        /// volume on it, since all of them are read)</summary>
        FrameworkElement DiskModeNote(int disk = -1)
        {
            string which = "";
            if (disk >= 0)
            {
                DiskIdentity d = null;
                try { d = ConnectedDisks().FirstOrDefault(x => x.Number == disk); } catch (Exception) { }
                if (d != null) which = "The whole drive this is on is read: " + d.Display + (d.Serial != null ? ", serial number " + d.Serial : "") + ", with every volume on it (" + DiskFacts(d).Split(new[] { "  ·  " }, StringSplitOptions.None).Last() + "). ";
            }
            return T(which + (which.Length == 0 ? "The whole drive is read, sector by sector. " : "") + DiskAdminSentence() + " Close other windows and programs that use the drive first, so Workbench can keep them from changing it while it is read.", Ty.Small, false, true, new Thickness(22, 0, 0, 6));
        }

        /// <summary>USB drives and SD cards connected now that have no drive letter (Windows does not read their file system, or they have
        /// none): offered as Whole Drive Image only</summary>
        void AddUnletteredDrives(StackPanel sp, List<SourceIdentity> lettered)
        {
            List<DiskIdentity> disks;
            try { disks = ConnectedDisks(); } catch (Exception) { return; }
            var withLetter = new HashSet<int>(lettered.Where(x => x.DiskNumber >= 0).Select(x => x.DiskNumber));
            var rest = disks.Where(d => !withLetter.Contains(d.Number)).ToList();
            if (rest.Count == 0) return;
            sp.Children.Add(Group("USB drives and SD cards without a drive letter"));
            sp.Children.Add(T("Windows shows no drive letter for these, for example a card formatted on a console. They can be copied as a whole drive image only.", Ty.Text, false, true, new Thickness(0, 0, 0, 8)));
            foreach (var d in rest)
            {
                var dd = d; var c = new StackPanel(); var ch = storageSel.FirstOrDefault(x => x.Req.DiskNumber == dd.Number && x.Req.Mode == "image");
                var cb = new CheckBox { Content = new TextBlock { Text = dd.Display + " (whole drive image)", FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap }, IsChecked = ch != null, Foreground = Res<Brush>("B.Ink") };
                c.Children.Add(cb);
                c.Children.Add(T(DiskFacts(dd), Ty.Small, false, true, new Thickness(26, 2, 0, 4)));
                c.Children.Add(DiskModeNote());
                cb.Checked += (s, e) => { if (!storageSel.Any(x => x.Req.DiskNumber == dd.Number && x.Req.Mode == "image")) storageSel.Add(new StorageChoice { Req = new StorageRequest { Mode = "image", DiskNumber = dd.Number, Disk = dd, Path = dd.Path }, Id = dd.ToSource(), Connected = true }); storageEst = null; };
                cb.Unchecked += (s, e) => { storageSel.RemoveAll(x => x.Req.DiskNumber == dd.Number && x.Req.Mode == "image"); storageEst = null; };
                var card = Card(c, 16); card.Margin = new Thickness(0, 0, 0, 10); sp.Children.Add(card);
            }
        }

        /// <summary>the USB drives and SD cards Windows lists now, as it describes them (nothing on them is read)</summary>
        static List<DiskIdentity> ConnectedDisks()
        {
            var q = StorageBackup.DiskQueries; var sys = q.SystemDisk();
            return q.Disks().Select(e => { try { return q.Describe(e); } catch (Exception) { return null; } })
                .Where(d => d != null && d.Number != sys && RawDiskPolicy.UsbAndSd.Allows(d.Bus)).OrderBy(d => d.Number).ToList();
        }

        /// <summary>a drive's facts in one line: size, serial number, how it is connected, its volumes</summary>
        static string DiskFacts(DiskIdentity d, List<DiskVolume> vols = null)
        {
            vols = vols ?? SafeVolumes(d.Number);
            return (d.Bytes > 0 ? Units.Gb(d.Bytes) : "no card in the reader") + "  ·  " + (d.Serial != null ? "serial number " + d.Serial : "no serial number reported") + "  ·  connected by " + d.BusText
                + (vols.Count > 0 ? "  ·  " + string.Join("; ", vols.Select(v => v.Display)) : "  ·  no volume Windows shows");
        }

        static List<DiskVolume> SafeVolumes(int n) { try { return StorageBackup.DiskQueries.VolumesOn(n) ?? new List<DiskVolume>(); } catch (Exception) { return new List<DiskVolume>(); } }

        // ------------------------------------------------------------------ a whole drive image written onto a connected drive
        void ShowDiskTargets(string path, string source)
        {
            var sp = PageHead("Choose the Drive to Write", "Choose the USB drive or SD card to write the whole drive image onto. Everything on it is replaced. Nothing is written until you review it and confirm.");
            List<DiskIdentity> disks;
            try { disks = ConnectedDisks(); } catch (Exception ex) { disks = new List<DiskIdentity>(); sp.Children.Add(Note("Windows' list of drives could not be read: " + ex.Message, "bad")); }
            if (disks.Count == 0) sp.Children.Add(Note("No USB drive or SD card is connected. Connect one, then choose Look Again.", "info"));
            foreach (var d in disks)
            {
                var dd = d; var c = new StackPanel();
                c.Children.Add(T(dd.Display, Ty.Heading, true, false, new Thickness(0, 0, 0, 4)));
                c.Children.Add(T(DiskFacts(dd), Ty.Small, false, true, new Thickness(0, 0, 0, 8)));
                c.Children.Add(Buttons(B("Review Writing to This Drive…", "Btn", () => PlanStorageImage(path, source, new DiskTarget { DiskNumber = dd.Number, Disk = dd }))));
                var card = Card(c, 16); card.Margin = new Thickness(0, 0, 0, 10); sp.Children.Add(card);
            }
            sp.Children.Add(T("Only USB drives and SD cards are listed. The drive Windows runs from, and any drive holding this backup or your backup location, are never written.", Ty.Small, false, true, new Thickness(0, 4, 0, 0)));
            sp.Children.Add(Buttons(B("Look Again", "Btn.Ghost", () => ShowDiskTargets(path, source)), B("Back", "Btn.Ghost", () => ShowStorageRestore(path))));
            Show(sp, "storage-disk-targets");
        }

        /// <summary>the destructive restore onto a connected drive, reviewed: the drive, what is lost, the deliberate confirmation</summary>
        void ReviewDiskRestore(ImageRestorePlan p, string path)
        {
            var d = p.TargetDisk;
            var sp = PageHead("Review Writing the Drive Image", "Nothing has been written yet. This replaces what is on the drive below.");
            sp.Children.Add(Card(KeyValues(("From", p.SourceLabel + ", " + Size(p.ImageBytes)), ("Onto", d.Display), ("Maker and model", string.IsNullOrWhiteSpace(d.Name) ? "not reported" : d.Name),
                ("Size", Size(d.Bytes)), ("Serial number", d.Serial ?? "not reported"), ("Connected by", d.BusText),
                ("On it now", p.TargetVolumes.Count == 0 ? "no volume Windows shows" : string.Join("; ", p.TargetVolumes.Select(v => v.Display))))));
            foreach (var r in p.Review) sp.Children.Add(Note(r, r.Contains("lost") || r.Contains("replace") || r.Contains("looks like") ? "warn" : "info"));
            var c = new StackPanel();
            var cb = new CheckBox { Content = new TextBlock { Text = "I understand: everything on " + d.Display + " from its start up to " + Size(p.ImageBytes) + " is replaced, and what is there now is lost.", TextWrapping = TextWrapping.Wrap }, Foreground = Res<Brush>("B.Ink") };
            c.Children.Add(cb);
            c.Children.Add(T("To confirm, type REPLACE:", Ty.Text, false, false, new Thickness(0, 12, 0, 4)));
            var typed = new TextBox { MaxWidth = 260, HorizontalAlignment = HorizontalAlignment.Left }; System.Windows.Automation.AutomationProperties.SetName(typed, "Type REPLACE to confirm");
            c.Children.Add(typed);
            sp.Children.Add(Card(c));
            var go = B("Write the Drive Image", "Btn.Primary", () => RunDiskRestore(p, path), false);
            void Update() => go.IsEnabled = cb.IsChecked == true && string.Equals(typed.Text.Trim(), "REPLACE", StringComparison.OrdinalIgnoreCase);
            cb.Checked += (s, e) => Update(); cb.Unchecked += (s, e) => Update(); typed.TextChanged += (s, e) => Update();
            sp.Children.Add(Buttons(go, B("Cancel", "Btn.Ghost", () => ShowStorageRestore(path))));
            Show(sp, "storage-disk-review");
        }

        void RunDiskRestore(ImageRestorePlan p, string path)
        {
            var cts = new CancellationTokenSource();
            var pv = new ProgressView(this, "Writing the Drive Image", DiskAdminSentence() + " Then the drive is checked again, written, and read back. Cancel stops after the piece being written; a drive canceled part way holds part of the image.", "Cancel", () => cts.Cancel());
            Show(pv.Panel, "progress");
            Task.Run(() => StorageRestore.RestoreImage(p, p.Digest, p.Nonce, Reload(), () => cts.IsCancellationRequested, x => Dispatcher.BeginInvoke(new Action(() => pv.Update(x.Text, x.Done, x.Total))))).ContinueWith(t =>
            {
                var r = t.IsFaulted ? new StorageRestoreResult { Text = "The restore stopped with a problem Workbench did not expect: " + t.Exception.GetBaseException().Message + " The drive may hold part of the image and cannot be relied on.", DiskState = "interrupted" } : t.Result;
                if (page.Content != pv.Panel) { UpdateStatus(r.Ok ? "The drive image was written and read back." : "The drive image restore did not finish: " + r.Text); return; }
                ShowDiskResult(r, path);
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        void ShowDiskResult(StorageRestoreResult r, string path)
        {
            string head; string kind;
            switch (r.DiskState)
            {
                case "complete": head = "Written and read back"; kind = "ok"; break;
                case "declined": head = "Windows did not give permission"; kind = "info"; break;
                case "canceled": head = "Canceled: nothing was written"; kind = "info"; break;
                case "refused": head = "Nothing was written"; kind = "bad"; break;
                case "read-back-differs": head = "The drive does not read back the same"; kind = "bad"; break;
                case "written-not-checked": head = "Written, but not checked"; kind = "bad"; break;
                default: head = r.NothingWritten ? "Nothing was written" : "The drive is incomplete"; kind = "bad"; break;
            }
            var sp = PageHead(head, null);
            sp.Children.Add(Note(r.Text ?? "", kind));
            if (r.Problems.Count > 0) sp.Children.Add(Details("What did not match (" + r.Problems.Count + ")", r.Problems.Take(100).Cast<object>().ToArray()));
            sp.Children.Add(Buttons(B("Back to USB and SD Backup", "Btn", () => Go("USB and SD Backup")), r.Ok ? null : B("Back to the Backup", "Btn.Ghost", () => ShowStorageRestore(path))));
            Show(sp, "storage-disk-result");
        }

        /// <summary>on the first page: whole-drive restores that did not finish (until that drive is restored completely)</summary>
        void AddUnfinishedDiskNotes(StackPanel sp)
        {
            List<(DateTime? when, string target, string state, string text)> u;
            try { u = StorageRestore.UnfinishedDiskRestores(settings); } catch (Exception) { return; }
            foreach (var x in u.Take(3))
                sp.Children.Add(Note("A whole drive image restore onto " + x.target + (x.when.HasValue ? " on " + x.when.Value.ToLocalTime().ToString("MMMM d, yyyy 'at' h:mm tt", En) : "") + " did not finish"
                    + (x.state == "writing" ? " (Workbench closed while it was writing)" : "") + ". That drive may hold part of the image and cannot be relied on until an image is written onto it completely.", "warn"));
        }
    }
}
