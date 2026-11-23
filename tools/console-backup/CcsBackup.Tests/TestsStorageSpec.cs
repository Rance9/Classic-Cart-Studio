// Classic Cart Studio console backup and restore engine tests: the shared storage accounting (StorageSpec, the same vectors the website
// and the Windows app's backend decide) and Backup Browser -> Storage (StorageReport). GPL-3.0-or-later. Simulated consoles only (files on
// this PC); nothing here opens a console session of any kind, and the report never writes to a backup.
using CcsBackup.Browse;
using CcsBackup.Engine;
using CcsBackup.Ext4;
using CcsBackup.Library;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using static CcsBackup.Tests.Program;

namespace CcsBackup.Tests
{
    static partial class Tests
    {
        /// <summary>the same value: dictionaries by key, lists in order, numbers by value</summary>
        static bool Same(object a, object b)
        {
            if (a == null || b == null) return a == null && b == null;
            if (a is Dictionary<string, object> da && b is Dictionary<string, object> db) return da.Count == db.Count && da.All(kv => db.TryGetValue(kv.Key, out var v) && Same(kv.Value, v));
            if (a is string || b is string) return Equals(a, b);
            if (a is IEnumerable ea && b is IEnumerable eb) { var la = ea.Cast<object>().ToList(); var lb = eb.Cast<object>().ToList(); return la.Count == lb.Count && la.Zip(lb, Same).All(x => x); }
            if ((a is int || a is long || a is decimal) && (b is int || b is long || b is decimal)) return Convert.ToDecimal(a) == Convert.ToDecimal(b);
            return Equals(a, b);
        }
        static Dictionary<string, object> Round(object o) => Json.Parse(Json.Write(o));

        public static void StorageSpecGroup()
        {
            var src = PlatformSource();
            if (src == null) throw new SkipException("the repository's tools/platform folder is not above this test program");
            Check(Sha(File.ReadAllBytes(Path.Combine(src, "ccs-storage-spec.json"))) == StorageSpec.SpecSha256, "the engine's storage specification is tools/platform/ccs-storage-spec.json, byte for byte");
            var vec = Json.Parse(File.ReadAllText(Path.Combine(src, "storage-vectors.json"), Encoding.UTF8));
            Check(vec.S("spec_sha256") == StorageSpec.SpecSha256, "the storage vectors were made from this specification");
            int n = 0; var bad = new List<string>();
            foreach (var v in (vec.A("account") ?? new List<object>()).OfType<Dictionary<string, object>>())
            {
                n++; var got = Round(StorageSpec.Account(v.O("input")));
                if (!Same(got, v.O("result"))) bad.Add(v.S("name"));
            }
            Check(n >= 18 && bad.Count == 0, "the shared storage vectors: " + (n - bad.Count) + "/" + n + " give exactly the recorded result" + (bad.Count > 0 ? " (" + string.Join("; ", bad) + ")" : ""));
            int nr = 0; var badr = new List<string>();
            foreach (var v in (vec.A("retention") ?? new List<object>()).OfType<Dictionary<string, object>>())
            { nr++; if (!Same(Round(StorageSpec.Retention(v.O("input"))), v.O("result"))) badr.Add(v.S("name")); }
            Check(nr >= 8 && badr.Count == 0, "the shared retention vectors (earlier CCS HOME versions): " + (nr - badr.Count) + "/" + nr + " give exactly the recorded result" + (badr.Count > 0 ? " (" + string.Join("; ", badr) + ")" : ""));
            Check(StorageSpec.EarlierKeep(null) == 1 && StorageSpec.EarlierKeep(0L) == 0 && StorageSpec.EarlierKeep(9L) == 9 && StorageSpec.EarlierKeep(10L) == 1 && StorageSpec.EarlierKeep(-1L) == 1,
                "earlier CCS HOME versions: one by default, none or up to nine; anything else is the default");
            var st0 = new CcsBackup.Library.Settings { EarlierHomeVersions = 3, PrimaryFolder = "C:\\x" }; var sf = Path.Combine(Work, "earlier-settings-" + Guid.NewGuid().ToString("N").Substring(0, 6) + ".json");
            st0.Write(sf); var st1 = CcsBackup.Library.Settings.Load(sf); File.WriteAllText(sf, "{\"schema\": \"ccs-console-backup-settings/1\", \"earlier_home_versions\": 12}"); var st2 = CcsBackup.Library.Settings.Load(sf); File.Delete(sf);
            Check(st1.EarlierHomeVersions == 3 && st1.EarlierHomeKeep == 3 && st2.EarlierHomeVersions == null && st2.EarlierHomeKeep == 1 && new CcsBackup.Library.Settings().EarlierHomeKeep == 1,
                "the setting is saved and read back with the other settings (earlier_home_versions); a number out of range is not used");
            // a handed-out copy cannot change the specification
            var sp = StorageSpec.Spec; sp.O("policies")["margin_fixed_bytes"] = 0;
            Check(StorageSpec.Spec.O("policies").L("margin_fixed_bytes") == 8388608, "the specification handed out is a copy: changing it changes nothing");
            // Console Composer's check: planned changes in order, a replacement freeing the file it replaces only once it is in place
            var st = new Dictionary<string, object> { ["source"] = "measured", ["available_bytes"] = 18900000L };
            var ch = new List<PlannedFileChange> { new PlannedFileChange { Op = "put", Path = "/ccs/x.bin", Bytes = 36864, OldState = new string('a', 64), OldBytes = 32768 } };
            var a1 = StorageSpec.Account(Capacity.Input(st, ch));
            var why = Capacity.Why(a1, out var need, out var have);
            Check((string)a1["status"] == "over-at-peak" && (long)a1["steady_bytes"] == 18880307 && need == 18913075 && have == 18900000 && why != null && why.Contains("while each new file is beside the one it replaces"),
                "Console Composer: a larger copy of a file fits once it replaces the old one but not while both are present: refused, with why (" + why + ")");
            ch[0].OldState = "absent"; ch[0].OldBytes = 0;
            Check((string)StorageSpec.Account(Capacity.Input(new Dictionary<string, object> { ["source"] = "measured", ["available_bytes"] = 100000000L }, ch))["status"] == "fits", "Console Composer: a new file that fits is allowed");
            var a2 = StorageSpec.Account(Capacity.Input(new Dictionary<string, object> { ["source"] = "measured", ["free_bytes"] = 395460608L, ["reserved_bytes"] = 20656128L, ["block_bytes"] = 4096L }, ch));
            Check((long)a2["available_bytes"] == 374804480 && (string)a2["block_source"] == "measured", "a backup's file system header: the reserve is subtracted once and its block size is used as measured");
            var a3 = StorageSpec.Account(Capacity.Input(new Dictionary<string, object> { ["source"] = "measured", ["available_bytes"] = 100000000L }, ch, StorageSpec.SnesModel));
            Check((string)a3["status"] == "unconfirmed" && !(bool)a3["write_ok"] && Capacity.Why(a3, out _, out _).Contains("room for saves and suspend points"),
                "a Super NES Classic: no room for saves is established, so a write is not confirmed (it says why)");
            // CCS HOME through the Workbench: its files, each in whole blocks, against the console's df figure
            var hp = new HomePackage(); hp.Files.Add(new HomePackage.Entry { Rel = "a", Size = 1000 }); hp.Files.Add(new HomePackage.Entry { Rel = "b", Size = 5000 });
            var h1 = StorageSpec.Account(HomePackage.SpaceInput(hp, 100000000)); var h2 = StorageSpec.Account(HomePackage.SpaceInput(hp, 1000000));
            Check((long)h1["allocated_bytes"] == 4096 + 8192 && (string)h1["status"] == "fits" && (string)h2["status"] == "over", "CCS HOME's package: its files in whole blocks; refused when it does not fit");
            // HOME Designer's apply: the reserve is part of the free blocks the header records and is left out once; older designs removed
            // after the switch lower the final figure, not the peak
            var ds = new DesignConsoleState { FreeBytes = 60000000, ReservedBytes = 20656128, BlockBytes = 4096 };
            var dch = new List<PlannedFileChange> { new PlannedFileChange { Op = "put", Path = "ccs/d/new/bg.jpg", Bytes = 1000000, OldState = "absent" }, new PlannedFileChange { Op = "del", Path = "ccs/d/old/bg.jpg", OldState = new string('b', 64), OldBytes = 900000 } };
            var dr = DesignApplyEngine.Room(ds, dch);
            Check((long)dr["available_bytes"] == 60000000 - 20656128 && (long)dr["removed_bytes"] == StorageSpec.Rounded(900000, 4096) && (long)dr["peak_bytes"] - (long)dr["steady_bytes"] == (long)dr["removed_bytes"] && (string)dr["status"] == "fits",
                "HOME Designer: free space outside the reserve (the reserve is not free for a design), older designs freed only after the switch (" + dr["available_bytes"] + ")");
            ds.FreeBytes = 20656128 + 18000000;
            Check(!(bool)DesignApplyEngine.Room(ds, dch)["write_ok"], "HOME Designer: a design that would only fit by using the reserve is refused");
            // the backup's own reading, from a simulated console's backup
            var dir = NewDir("storage-spec"); var s = SettingsIn(dir, false);
            var con = Console_("modified-noorig"); var o = Backup(con, s);
            Check(o.Ok, "a backup of a simulated console (" + o.State + ")");
            var rd = Capacity.Reading(o.PrimaryPath, out var problem);
            long fb, rb, bs;
            using (var f = File.OpenRead(Path.Combine(Fx, "data-modified.img"))) { var fs = new Ext4Reader(f); bs = fs.BlockSize; fb = fs.FreeBlocks * bs; rb = fs.ReservedBlocks * bs; }
            Check(rd != null && (long)rd["free_bytes"] == fb && (long)rd["reserved_bytes"] == rb && (long)rd["block_bytes"] == bs, "Console Composer reads the free blocks, reserve and block size the backup's data storage recorded (" + problem + ")");
            Check(Capacity.Check(o.PrimaryPath, ch, out var nd, out var av) == null && av == fb - rb && nd > 36864, "Console Composer's check on that backup: available is free outside the reserve (" + av + ")");
            Directory.Delete(Path.GetDirectoryName(con), true);
        }

        public static void StorageReportGroup()
        {
            // ---- the accounting on entries made for the test: hard links, folders, a CCS HOME copy in use and an earlier one, a part of the
            // backup and a file inside it, a file whose allocation is not known
            BrowseEntry F(string path, long size, long alloc, uint inode, Category cat = Category.EverythingElse) => new BrowseEntry { Id = "d:" + path, Name = path.Substring(path.LastIndexOf('/') + 1), Source = EntrySource.DataFile, Kind = EntryKind.File, ImagePath = path, ConsolePath = "/var/lib" + path, Size = size, Allocated = alloc, Inode = inode, Category = cat, Owner = Inventory.OwnerOf(path) };
            var member = new BrowseEntry { Id = "m:phy", Name = "raw.img", Member = "raw.img", Source = EntrySource.Member, Kind = EntryKind.File, Size = 536870912, IsImage = true };
            var es = new List<BrowseEntry>
            {
                F("/ccs/pkg/current", 15, 4096, 20), F("/ccs/pkg/aaaa.00000001/home.bin", 100000, 102400, 21, Category.ModsAndEmulators), F("/ccs/pkg/aaaa.00000001/a.txt", 10, 4096, 22),
                F("/ccs/pkg/bbbb.00000002/home.bin", 90000, 90112, 23, Category.ModsAndEmulators),
                F("/clover/profiles/0/save.sav", 8192, 8192, 30, Category.Saves), F("/clover/profiles/0/save-link.sav", 8192, 8192, 30, Category.Saves),   // a hard link: one file
                new BrowseEntry { Id = "d:/ccs", Name = "ccs", Source = EntrySource.DataFile, Kind = EntryKind.Folder, ImagePath = "/ccs", Allocated = 4096, Inode = 12 },
                member, new BrowseEntry { Id = "i:x", Name = "x", Source = EntrySource.ImageFile, Kind = EntryKind.File, Container = member, InnerPath = "/x", Size = 999999 },
            };
            var r = StorageReport.Compute(es, 413138944, 395460608, 20656128, 4096, false, null, e => e.ImagePath == "/ccs/pkg/current" ? Encoding.ASCII.GetBytes("aaaa.00000001\n") : null, new StorageReport());
            Check(r.Files == 5 && r.ExtraNames == 1 && r.Bytes == 15 + 100000 + 10 + 90000 + 8192 && r.Allocated == 4096 + 102400 + 4096 + 90112 + 8192, "each stored file once: a hard link's second name is not counted again (" + r.Files + " files, " + r.Allocated + " bytes)");
            Check(r.Available == 374804480 && r.Used == 413138944 - 395460608 && r.OtherUse == r.Used - r.Allocated - 4096, "the header's reserve is part of the free space and subtracted once; other use is what files and folders do not hold");
            Check(r.HomeCurrent == "aaaa.00000001" && r.HomeCopies.Count == 2 && r.HomeCopies[0].InUse && r.HomeCopies[0].Allocated == 106496 && !r.HomeCopies[1].InUse && r.HomeCopies[1].Allocated == 90112,
                "CCS HOME: the copy pkg/current names is in use, the earlier copy is listed with its own size");
            Check(r.Parts.Count == 1 && r.Parts[0].Bytes == 536870912 && r.NestedFiles == 1 && r.Allocated < 536870912, "the backup's own parts are listed apart and the files inside them are not counted again");
            Check(!r.Partial && r.Categories.Sum(x => x.Allocated) == r.Allocated && r.Owners.Sum(x => x.Allocated) == r.Allocated, "by type and by who added them add up to the files' total, each once");
            // ---- each copy's name, games and date from its MANIFEST.json and record; an entry of Original Menu naming the earlier copy; the next
            // update by the setting, through the shared retention rule and accounting (the peak can refuse what would fit afterwards)
            const string A = "1111111111111111.aaaaaaaa", B = "2222222222222222.bbbbbbbb", Z = "0000000000000000.cccccccc";
            var small = new Dictionary<string, string>
            {
                ["/ccs/pkg/current"] = A + "\n",
                ["/ccs/install/v-" + A + ".json"] = "{\"schema\": \"ccs-owned-copy/1\", \"copy\": \"" + A + "\", \"previous\": \"" + B + "\", \"installed_utc\": \"2026-11-20T15:04:05Z\"}",
                ["/ccs/install/v-" + B + ".json"] = "{\"schema\": \"ccs-owned-copy/1\", \"copy\": \"" + B + "\", \"previous\": \"" + Z + "\"}",
                ["/ccs/install/v-" + Z + ".json"] = "{\"schema\": \"ccs-owned-copy/1\", \"copy\": \"" + Z + "\"}",
                ["/ccs/pkg/" + A + "/MANIFEST.json"] = "{\"label\": \"Family Favorites\", \"games\": 31}",
                ["/ccs/pkg/" + B + "/MANIFEST.json"] = "{\"label\": \"Weekend Games\", \"games\": 12}",
                ["/hakchi/games/nes-usa/000/CLV-Z-00001/CLV-Z-00001.desktop"] = "[Desktop Entry]\nExec=/bin/clover-kachikachi-wr /var/lib/ccs/pkg/" + Z + "/roms/x.nes\n",
            };
            List<BrowseEntry> HomeEntries(bool desktop) => new List<BrowseEntry>
            {
                F("/ccs/pkg/current", 26, 4096, 50), F("/ccs/install/v-" + A + ".json", 200, 4096, 51), F("/ccs/install/v-" + B + ".json", 150, 4096, 52), F("/ccs/install/v-" + Z + ".json", 100, 4096, 53),
                F("/ccs/pkg/" + A + "/MANIFEST.json", 40, 4096, 54), F("/ccs/pkg/" + A + "/home.bin", 20000000, 20004864, 55),
                F("/ccs/pkg/" + B + "/MANIFEST.json", 40, 4096, 56), F("/ccs/pkg/" + B + "/home.bin", 18000000, 18001920, 57),
                F("/ccs/pkg/" + Z + "/home.bin", 15000000, 15003648, 58),
            }.Concat(desktop ? new[] { F("/hakchi/games/nes-usa/000/CLV-Z-00001/CLV-Z-00001.desktop", 80, 4096, 59) } : new BrowseEntry[0]).ToList();
            Func<BrowseEntry, byte[]> Read = e => small.TryGetValue(e.ImagePath, out var t) ? Encoding.UTF8.GetBytes(t) : null;
            StorageReport Rep(bool desktop, int keep, long free) => StorageReport.Compute(HomeEntries(desktop), 413138944, free, 20656128, 4096, false, null, Read,
                new StorageReport { ConsoleModel = "NES Classic Edition", ConsoleBoard = "dp-nes", EarlierKeep = keep });
            var h1 = Rep(true, 1, 395460608); var ha = h1.HomeCopies.First(x => x.Copy == A); var hb = h1.HomeCopies.First(x => x.Copy == B); var hz = h1.HomeCopies.First(x => x.Copy == Z);
            Check(ha.InUse && ha.Collection == "Family Favorites" && ha.Games == 31 && ha.Installed == "2026-11-20T15:04:05Z" && ha.Previous == B && ha.Recorded && hb.Collection == "Weekend Games" && hb.Installed == null
                  && hz.Collection == null && hz.Named == "yes" && hb.Named == "no" && h1.Text().Contains("Family Favorites, 31 game(s), installed 2026-11-20T15:04:05Z"),
                  "each CCS HOME copy with its collection name, games and date (date not known without one in its record), and whether an entry of Original Menu names it");
            var n1 = h1.NextHomeUpdate(); var n0 = Rep(true, 0, 395460608).NextHomeUpdate(); var nfree = Rep(false, 0, 395460608).NextHomeUpdate();
            Check(n1 != null && n1.Remove.SequenceEqual(new[] { B }) && n0.Remove.SequenceEqual(new[] { A, B }) && nfree.Remove.SequenceEqual(new[] { A, B, Z }) && n0.Status == "fits",
                  "the next update by the setting: one earlier version keeps the copy in use now and removes the older one; none removes them all after the switch, except one an entry of Original Menu names ("
                  + Listed(n1.Remove) + "; " + Listed(n0.Remove) + "; " + Listed(nfree.Remove) + ")");
            var acc = StorageSpec.Account(new Dictionary<string, object>
            {
                ["profile"] = "nes-classic-edition", ["storage"] = new Dictionary<string, object> { ["source"] = "measured", ["free_bytes"] = 395460608L, ["reserved_bytes"] = 20656128L, ["block_bytes"] = 4096L },
                ["items"] = new object[] { new Dictionary<string, object> { ["category"] = "home", ["label"] = "the next CCS HOME update (the size of the copy in use)", ["estimate"] = new Dictionary<string, object> { ["logical_bytes"] = ha.Bytes, ["allocated_bytes"] = ha.Allocated } } },
                ["removed_after"] = new object[] { new Dictionary<string, object> { ["label"] = A, ["allocated_bytes"] = ha.Allocated }, new Dictionary<string, object> { ["label"] = B, ["allocated_bytes"] = hb.Allocated } },
            });
            Check(n0.Peak == (long)acc["peak_bytes"] && n0.Steady == (long)acc["steady_bytes"] && n0.Available == 374804480,
                  "the report's next-update figures are the shared accounting's for the same reading and copies (peak " + n0.Peak + ", after " + n0.Steady + ")");
            long tight = n0.Steady.Value + 20656128 + (n0.Peak.Value - n0.Steady.Value) / 2;   // room for the state after the switch, not for both copies at once
            var nt = Rep(false, 0, tight).NextHomeUpdate();
            Check(nt.Status == "over-at-peak" && nt.Steady <= nt.Available && nt.Peak > nt.Available, "the peak while the update is sent beside the copy in use refuses an update whose state after the switch would fit (" + nt.Status + ")");
            var est = new List<BrowseEntry>(es) { F("/other/listed.bin", 5000, -1, 40) };
            var r2 = StorageReport.Compute(est, null, null, null, null, true, null, e => null, new StorageReport());
            Check(r2.Partial && r2.EstimatedFiles == 1 && r2.OtherUse == null && r2.Available == null && r2.Allocated == 4096 + 102400 + 4096 + 90112 + 8192 + 8192 && r2.HomeCurrent == "unreadable",
                "a damaged backup: partial, a file listed without its allocation is estimated in whole blocks, no exact total and no other-use figure");
            // ---- a backup of a simulated console, opened in the Backup Browser
            var dir = NewDir("storage-report"); var s = SettingsIn(dir, false);
            var con = Console_("modified-noorig"); var o = Backup(con, s);
            Check(o.Ok, "a backup of a simulated console (" + o.State + ")");
            var keep = (Sha(o.PrimaryPath), new FileInfo(o.PrimaryPath).Length, File.GetLastWriteTimeUtc(o.PrimaryPath));
            var before = Directory.GetFiles(dir, "*", SearchOption.AllDirectories).Select(x => x.Substring(dir.Length)).OrderBy(x => x).ToList();
            var listing = Json.Parse(File.ReadAllText(Path.Combine(Fx, "listing-data-modified.json"))).A("entries").OfType<Dictionary<string, object>>().ToList();
            var opened = BackupContents.Open(o.PrimaryPath, s);
            Check(opened.Contents != null, "the backup opens (" + opened.Problem + ")");
            using (var c = opened.Contents)
            {
                var rep = StorageReport.Build(c);
                var files = listing.Where(e => e.S("type") == "f").GroupBy(e => e.L("inode")).Select(g => g.First()).ToList();
                Check(!rep.Partial && rep.Files == files.Count && rep.Bytes == files.Sum(e => e.L("size")), "the report's files and their length agree with e2fsprogs' own listing of the data storage (" + rep.Files + " files, " + rep.Bytes + " bytes)");
                long fb, rb, tb; using (var f = File.OpenRead(Path.Combine(Fx, "data-modified.img"))) { var fs = new Ext4Reader(f); fb = fs.FreeBlocks * fs.BlockSize; rb = fs.ReservedBlocks * fs.BlockSize; tb = fs.BlocksCount * fs.BlockSize; }
                Check(rep.Total == tb && rep.Free == fb && rep.Reserved == rb && rep.Available == fb - rb && rep.OtherUse >= 0, "the data storage's header as the backup holds it: total, free, the reserve once; files, folders and the file system's own use add up to what is in use");
                var dataFiles = c.Entries.Where(e => e.Source == EntrySource.DataFile && e.Kind == EntryKind.File).ToList();
                Check(dataFiles.All(e => e.Allocated >= 0) && dataFiles.Where(e => e.Size > 0 && e.Size <= 4096).All(e => e.Allocated == 4096) && rep.EstimatedFiles == 0,
                    "each file's allocation is measured from its inode; a small text takes one whole block");
                var text = rep.Text();
                Check(text.Contains("Available outside the reserve") && text.Contains("not a reading of the console now") && text.Contains("each stored file counted once") && !text.Contains("SNES"), "the plain report says where its figures come from");
                var out1 = NewDir("storage-report-out");
                var x1 = StorageReport.Export(rep, out1); var x2 = StorageReport.Export(rep, out1);
                Check(x1.Ok && x2.Ok && x1.Folder != x2.Folder && File.ReadAllText(x1.ManifestPath) == text && Directory.GetFiles(x1.Folder).Length == 1, "Export Report writes the report into a new folder each time, read back and checked; never over an earlier one");
                Check(!StorageReport.Export(rep, "relative\\folder").Ok && !StorageReport.Export(rep, Path.Combine(out1, "missing")).Ok, "Export Report refuses a relative or missing folder");
            }
            Check(Sha(o.PrimaryPath) == keep.Item1 && new FileInfo(o.PrimaryPath).Length == keep.Item2 && File.GetLastWriteTimeUtc(o.PrimaryPath) == keep.Item3, "the backup is byte for byte unchanged after the report and its exports");
            var after = Directory.GetFiles(dir, "*", SearchOption.AllDirectories).Select(x => x.Substring(dir.Length)).OrderBy(x => x).ToList();
            Check(after.Except(before).All(x => x.EndsWith("browser-checks.json", StringComparison.Ordinal) || x.Contains("derived-index-")), "nothing beside the backup was added by the report (" + string.Join(", ", after.Except(before)) + ")");
            Directory.Delete(Path.GetDirectoryName(con), true);
        }
    }
}
