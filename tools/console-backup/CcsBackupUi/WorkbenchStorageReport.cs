// Classic Cart Studio console backup and restore window. Copyright (C) 2026 Rance9. GPL-3.0-or-later.
// Backup Browser -> Storage (the fourth view of an opened backup): how the console's data storage was used when the backup was made, from
// the backup alone (CcsBackup.Browse.StorageReport): what the storage's own header recorded (total, free, the reserve), every stored file
// counted once (hard links once), by type and by who added them, CCS HOME's copy in use and the earlier copies kept beside it, the largest
// files, and the backup file's own parts apart (the backup's size is not the console's storage). A damaged or incomplete backup says its
// figures are partial. Export Report writes a plain text report the export's way (a new folder, the file read back and checked). Nothing
// here changes the backup or any console: this view only reports. Its row of pictures for the Sega Mini menu offers the separate, reviewed
// clean-up (WorkbenchSega.cs), which is a console task of its own.
using CcsBackup.Browse;
using CcsBackup.Engine;
using System;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace CcsBackup.Ui
{
    public sealed partial class BackupWorkspace
    {
        static readonly CultureInfo StorageEn = CultureInfo.GetCultureInfo("en-US");
        static string Sz(long? b) => b == null ? "Not known" : Units.Gb(b.Value);

        /// <summary>a two-column table of figures: a label and a value, each value with its exact bytes in its tooltip</summary>
        Grid Figures(params (string label, long? bytes, string note)[] rows)
        {
            var g = new Grid { Margin = new Thickness(0, 2, 0, 4) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            for (int i = 0; i < rows.Length; i++)
            {
                g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                var l = T(rows[i].label + (rows[i].note != null ? " (" + rows[i].note + ")" : ""), Ty.Text, false, false, new Thickness(0, 3, 16, 3));
                var v = T(Sz(rows[i].bytes), Ty.Text, true, false, new Thickness(0, 3, 0, 3)); v.TextAlignment = TextAlignment.Right;
                if (rows[i].bytes != null) v.ToolTip = rows[i].bytes.Value.ToString("N0", StorageEn) + " bytes";
                Grid.SetRow(l, i); Grid.SetRow(v, i); Grid.SetColumn(v, 1); g.Children.Add(l); g.Children.Add(v);
                if (i > 0) { var line = new Border { BorderBrush = Res<Brush>("B.Line"), BorderThickness = new Thickness(0, 1, 0, 0) }; Grid.SetRow(line, i); Grid.SetColumnSpan(line, 2); g.Children.Add(line); }
            }
            return g;
        }

        /// <summary>the Storage view of the open backup</summary>
        UIElement StorageView(BackupContents c)
        {
            var sp = new StackPanel();
            StorageReport r;
            try { r = StorageReport.Build(c, earlierKeep: settings.EarlierHomeKeep); }
            catch (Exception ex) when (ex is System.IO.IOException || ex is InvalidOperationException || ex is CcsBackup.Ext4.Ext4Exception)
            { sp.Children.Add(Note("The storage figures could not be worked out from this backup (" + ex.Message + ").", "bad")); return sp; }
            sp.Children.Add(T("How the console's data storage was used when this backup was made, worked out on this PC from the backup alone. It is not a reading of the console now, and nothing here changes the backup or any console.", Ty.Body, false, true, new Thickness(0, 0, 0, 10)));
            if (r.Partial) sp.Children.Add(Note("These figures are partial, not an exact total. " + string.Join(" ", r.PartialWhy), "warn"));
            var head = new StackPanel();
            head.Children.Add(T("The data storage", Ty.CardTitle, true));
            head.Children.Add(T("As its own file system header recorded it when the backup was made.", Ty.Small, false, true));
            head.Children.Add(Figures(("Total", r.Total, null), ("Available outside the reserve", r.Available, null), ("Kept in reserve by the system", r.Reserved, "part of the free space, not used by files"), ("In use", r.Used, null)));
            sp.Children.Add(Card(head, 18));
            var files = new StackPanel();
            files.Children.Add(T("Files", Ty.CardTitle, true));
            files.Children.Add(T(r.Files.ToString("N0", StorageEn) + " stored file(s), each counted once" + (r.ExtraNames > 0 ? "; " + r.ExtraNames.ToString("N0", StorageEn) + " more name(s) of the same files are not counted again" : "") + ".", Ty.Small, false, true));
            files.Children.Add(Figures(("Their length", r.Bytes, null), ("Space allocated to them", r.Allocated, r.EstimatedFiles > 0 ? "partly estimated" : "measured from the backup"),
                ("Folders and links", r.FolderAllocated, null), ("The file system's own records and journal", r.OtherUse, null)));
            sp.Children.Add(Card(files, 18));
            UIElement Table(string title, string sub, System.Collections.Generic.IEnumerable<StorageReport.Row> rows)
            {
                var t = new StackPanel(); t.Children.Add(T(title, Ty.CardTitle, true)); if (sub != null) t.Children.Add(T(sub, Ty.Small, false, true));
                t.Children.Add(Figures(rows.Select(x => (x.Label + " (" + x.Files.ToString("N0", StorageEn) + " file" + (x.Files == 1 ? "" : "s") + ")", (long?)x.Allocated, x.Estimated > 0 ? "partly estimated" : null)).ToArray()));
                return Card(t, 18);
            }
            sp.Children.Add(Table("By type", null, r.Categories));
            sp.Children.Add(Table("By who added them", "Saves and settings of the console's own games count as the console's own data.", r.Owners));
            var home = new StackPanel(); home.Children.Add(T("CCS HOME on the console", Ty.CardTitle, true));
            if (r.HomeCopies.Count == 0) home.Children.Add(T("This backup holds no CCS HOME copy.", Ty.Text, false, true));
            else
            {
                home.Children.Add(T("The copy in use and the earlier copies kept beside it, as the backup holds them. Each shows the space it takes on the storage.", Ty.Small, false, true));
                string When(string u) => DateTime.TryParseExact(u ?? "", "yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var t)
                    ? "installed " + t.ToLocalTime().ToString("MMMM d, yyyy", StorageEn) : "date not known";
                var hf = Figures(r.HomeCopies.Select(h => ((h.InUse ? "In use: " : "Earlier version: ") + (h.Collection ?? "name not known") + (h.Games != null ? ", " + h.Games.Value.ToString("N0", StorageEn) + " game" + (h.Games == 1 ? "" : "s") : "")
                    + " · " + When(h.Installed) + " · " + Sz(h.Bytes) + " long" + (h.Named == "yes" ? " · named by an entry of Original Menu" : ""), (long?)h.Allocated, (string)null)).ToArray());
                SetNoTerms(hf, true); home.Children.Add(hf);
                var nu = r.NextHomeUpdate();
                if (nu != null)
                {
                    var rm = nu.Remove;
                    home.Children.Add(T("The next update", Ty.Body, true, false, new Thickness(0, 10, 0, 2)));
                    home.Children.Add(T("For an update the size of the copy in use, keeping " + (r.EarlierKeep == 0 ? "no earlier version" : r.EarlierKeep == 1 ? "one earlier version" : r.EarlierKeep.ToString(StorageEn) + " earlier versions")
                        + " (Settings > CCS HOME > Earlier HOME versions). The update is sent beside the copy in use and checked before the switch; the earlier versions beyond that number go only afterwards. The figures include room kept for saves and a safety margin.", Ty.Small, false, true));
                    home.Children.Add(Figures(("Needed while both are there (the peak)", nu.Peak, null), ("Available on this storage when the backup was made", nu.Available, null),
                        ("Needed after the update", nu.Steady, rm.Count == 0 ? "nothing removed" : rm.Count + " earlier version" + (rm.Count == 1 ? "" : "s") + " removed")));
                    var stt = nu.Status;
                    home.Children.Add(Note(stt == "fits" ? "It would fit." : stt == "near" ? "It would fit, with little room to spare." : stt == "over-at-peak" ? "It would not fit while both copies are there, though it would fit afterwards: keep fewer earlier versions, or make room first." : stt == "over" ? "It would not fit." : "Not known: this backup does not record enough of the storage.",
                        stt == "fits" ? "ok" : stt == "near" ? "warn" : stt == "unconfirmed" ? "warn" : "bad"));
                }
            }
            sp.Children.Add(Card(home, 18));
            if (r.Sega.Eligible.Count > 0 || r.Sega.Kept.Count > 0)
            {
                var sg = new StackPanel(); sg.Children.Add(T(SegaPictures.Title, Ty.CardTitle, true));
                sg.Children.Add(T(SegaPictures.Explanation, Ty.Text, false, true));
                var rbo = r.SegaAccount?["removed_bytes"]; var removed = rbo != null ? Convert.ToInt64(rbo, CultureInfo.InvariantCulture) : (long?)null;
                if (r.Sega.Eligible.Count > 0)
                    sg.Children.Add(Figures(("Pictures Workbench can remove (" + r.Sega.Eligible.Count.ToString("N0", StorageEn) + ")", r.Sega.Bytes, "their length"),
                        ("Space removing them would give back", removed, "each file once"),
                        ("Available afterwards", r.Available != null && removed != null ? r.Available + removed : null, null)));
                else sg.Children.Add(T("None of them can be removed by Workbench.", Ty.Text, false, true));
                if (r.Sega.Eligible.Count > 0) { var ef = Details("The files (" + r.Sega.Eligible.Count.ToString("N0", StorageEn) + ")", r.Sega.Eligible.Select(x => (object)(x.Path + " · " + Units.Gb(x.Bytes))).ToArray()); SetNoTerms(ef, true); sg.Children.Add(ef); }
                if (r.Sega.Kept.Count > 0) { var kf = Details("Pictures of this kind that stay (" + r.Sega.Kept.Count.ToString("N0", StorageEn) + ")", r.Sega.Kept.Select(x => (object)(x.entry.ImagePath.Substring(1) + ": " + x.why)).ToArray()); SetNoTerms(kf, true); sg.Children.Add(kf); }
                sg.Children.Add(T("Removing them is optional. " + SegaPictures.Recreated + " The console's list is made again from a new backup when you start, so it can differ from this backup's.", Ty.Small, false, true, new Thickness(0, 6, 0, 0)));
                if (r.Sega.Eligible.Count > 0) sg.Children.Add(Buttons(B("Remove from the Console…", "Btn", () => ShowSegaPictures())));
                sp.Children.Add(Card(sg, 18));
            }
            var big = new StackPanel(); big.Children.Add(T("Largest files", Ty.CardTitle, true));
            var bf = Figures(r.Largest.Select(e => (e.DisplayPath, (long?)r.AllocationOf(e), e.Allocated >= 0 ? (string)null : "estimated")).ToArray()); SetNoTerms(bf, true);
            big.Children.Add(bf); sp.Children.Add(Card(big, 18));
            var parts = new StackPanel(); parts.Children.Add(T("The backup file's own parts", Ty.CardTitle, true));
            parts.Children.Add(T("They hold the console's storage areas. Together they are larger than the console's storage, because the raw reference copy and the separate parts hold the same chip." + (r.NestedFiles > 0 ? " Files inside system images, startup images and packages belong to the parts that hold them and are not counted again." : ""), Ty.Small, false, true));
            var pf = Figures(r.Parts.Select(p => (p.Name, (long?)p.Bytes, (string)null)).ToArray()); SetNoTerms(pf, true); parts.Children.Add(pf);
            sp.Children.Add(Card(parts, 18));
            var result = new StackPanel();
            sp.Children.Add(Buttons(B("Export Report…", "Btn", () =>
            {
                var q = new ExportRequest { FolderName = StorageReport.DefaultFolderName };
                if (!ChooseExportTarget(q, "Export Report", "The figures are saved as a text file in a new folder.")) return;
                var x = StorageReport.Export(r, q.Destination, q.FolderName);
                result.Children.Clear();
                result.Children.Add(x.Ok ? Note("The report was saved and read back: " + x.ManifestPath, "ok") : Note(x.Problem ?? "The report was not saved.", "bad"));
            })));
            sp.Children.Add(result);
            sp.Children.Add(Details("Technical details",
                "Total: " + (r.Total?.ToString("N0", StorageEn) ?? "not known") + " bytes; free (the reserve included): " + (r.Free?.ToString("N0", StorageEn) ?? "not known") + "; reserve: " + (r.Reserved?.ToString("N0", StorageEn) ?? "not known") + "; block size: " + (r.Block?.ToString("N0", StorageEn) ?? "not known") + " bytes.",
                "Files: " + r.Bytes.ToString("N0", StorageEn) + " bytes long, " + r.Allocated.ToString("N0", StorageEn) + " bytes allocated (each file's own allocation from its inode; " + r.EstimatedFiles.ToString("N0", StorageEn) + " estimated from its length in whole blocks).",
                "Checksums recorded when the backup was made: " + r.HashRecorded.ToString("N0", StorageEn) + " file(s); worked out on this PC when a file is exported: " + r.HashOther.ToString("N0", StorageEn) + ".",
                r.HomeCurrent != null ? "CCS HOME's pointer (pkg/current) names: " + r.HomeCurrent + "." : "No CCS HOME pointer (pkg/current) in this backup.",
                "MB = 1,000,000 bytes."));
            return sp;
        }
    }
}
