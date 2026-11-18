// Classic Cart Studio console backup and restore engine tests: the Backup Browser (the backup list, the three views, classification,
// search, export). GPL-3.0-or-later. Simulated consoles only (files on this PC); nothing here opens a console session of any kind.
// Two backups are made: A from the unchanged data-modified fixture (hakchi, Classic Cart Studio's HOME and saves, Nintendo's saves, other
// programs' files), and B from a copy of it patched in place: a 70,000,000-byte sparse file whose date reads zero, hostile names (a
// reserved device name with a trailing dot, backslashes, a colon and wildcards, a control character and a trailing space) and two
// case-only name collisions. Expected checksums and metadata come from e2fsprogs' own listing of the fixture (debugfs), not from the
// engine's reader.
using CcsBackup.Archive;
using CcsBackup.Browse;
using CcsBackup.Ext4;
using CcsBackup.Library;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using static CcsBackup.Tests.Program;

namespace CcsBackup.Tests
{
    static partial class Tests
    {
        const long BigFile = 70000000;
        const string Hostile27 = "AUX\u0001<notes>|later*???????? ";   // 27 bytes: the length of "notes for later (draft).txt"

        public static void Browser()
        {
            var dir = NewDir("browser"); var s = SettingsIn(dir, false);
            var listing = Json.Parse(File.ReadAllText(Path.Combine(Fx, "listing-data-modified.json"))).A("entries").OfType<Dictionary<string, object>>().ToDictionary(e => e.S("path"));
            // ---- the two backups
            var ca = Console_("modified-noorig"); var oa = Backup(ca, s);
            Check(oa.Ok, "backup A made from the unchanged fixture (" + oa.State + " " + oa.Problem + ")");
            var img = Path.Combine(dir, "data-hostile.img"); File.Copy(Path.Combine(Fx, "data-modified.img"), img);
            using (var p = new ImagePatch(img))
            {
                p.Grow("/unknown/sparse-ish.bin", BigFile); p.SetMtime("/unknown/sparse-ish.bin", 0, 0);
                p.Rename("/unknown/exec.sh", "con.sh."); p.Rename("/unknown/a/b/c/d/e/f/g/deep.dat", "..\\a:b*?"); p.Rename("/unknown/notes for later (draft).txt", Hostile27);
                p.Rename("/hakchi", "Clover"); p.Rename("/clover/profiles/0/CLV-P-NAACE", "clv-p-naaae");
            }
            var cb = Console_("modified-noorig", HwidA, img); var ob = Backup(cb, s);
            Check(ob.Ok, "backup B made from the patched copy (" + ob.State + " " + ob.Problem + ")");
            File.Delete(img); Directory.Delete(Path.GetDirectoryName(ca), true); Directory.Delete(Path.GetDirectoryName(cb), true);
            var keep = new[] { oa.PrimaryPath, ob.PrimaryPath }.ToDictionary(x => x, x => (Sha(x), new FileInfo(x).Length, File.GetLastWriteTimeUtc(x)));
            var recordsBefore = Directory.GetFiles(s.RecordsFolder).Select(Path.GetFileName).ToList();

            BrowserList(s, oa, ob);
            BrowserViews(s, oa, listing);
            BrowserBig(s, ob);

            // ---- nothing the browser did changed a backup or anything else in the backup folder (only the check log and the derived index were added)
            Check(keep.All(k => File.Exists(k.Key) && Sha(k.Key) == k.Value.Item1 && new FileInfo(k.Key).Length == k.Value.Item2 && File.GetLastWriteTimeUtc(k.Key) == k.Value.Item3),
                "both backups are byte for byte unchanged after listing, checking, opening, salvage, searching and every export (SHA-256, size and time)");
            var added = Directory.GetFiles(s.RecordsFolder).Select(Path.GetFileName).Except(recordsBefore).ToList();
            Check(added.All(f => f == "browser-checks.json" || f.StartsWith("backup-", StringComparison.Ordinal) || f.StartsWith("derived-index-", StringComparison.Ordinal)) && added.Contains("browser-checks.json"),
                "the only files the browser added beside the backups are its check log and the derived index, in the records folder (" + string.Join(", ", added) + ")");
            var left = Directory.GetFiles(s.PrimaryFolder).Select(Path.GetFileName).ToList();
            Check(left.Count(f => f.EndsWith(".zip", StringComparison.Ordinal)) == 2 && !left.Any(f => f.Contains("~ccs-") || f.EndsWith(".part", StringComparison.Ordinal)),
                "the backup folder holds the two backups and the test's own unfinished files, nothing written by an export (" + string.Join(", ", left) + ")");

            BrowserNames();
            BrowserRules();
            try { Directory.Delete(dir, true); } catch (Exception) { }
        }

        // ------------------------------------------------------------------ the list: complete, incomplete, missing, damaged, salvage
        static void BrowserList(Settings s, Engine.BackupOutcome oa, Engine.BackupOutcome ob)
        {
            var rows = BackupList.List(s);
            var ra = rows.FirstOrDefault(r => r.Path == oa.PrimaryPath);
            var made = DateTime.Parse(Record.Load(oa.RecordFile).O("primary").S("verified_utc"), System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AdjustToUniversal);
            Check(ra != null && ra.Status == BrowseStatus.Complete && ra.Check == CheckState.Verified && ra.LastVerifiedBy == "made" && ra.LastVerifiedUtc == made && ra.ConsoleTag != null && ra.Bytes == new FileInfo(oa.PrimaryPath).Length,
                "a finished backup: Complete, verified, last verified when its own check passed as it was made (" + ra?.StatusText + ", " + ra?.LastVerifiedText + ")");
            var t0 = DateTime.UtcNow.AddSeconds(-1); var ca = BackupList.Check(s, oa.PrimaryPath); var t1 = DateTime.UtcNow.AddSeconds(1);
            Check(ca.Status == BrowseStatus.Complete && ca.Check == CheckState.Verified && ca.LastVerifiedBy == "check" && ca.LastVerifiedUtc >= t0 && ca.LastVerifiedUtc <= t1,
                "checked again now: the list records the real time of that check as its last verified date (" + ca.LastVerifiedText + ")");
            // incomplete: a backup canceled after 30 MB, and an unfinished file nobody accounts for
            var cc = Console_("modified-noorig"); long n = 0;
            var oc = Backup(cc, s, ctx => { ctx.Cancelled = () => Interlocked.Read(ref n) > 0; ctx.Progress = p => { if (p.Done > 30L * 1000 * 1000) Interlocked.Exchange(ref n, 1); }; });
            Directory.Delete(Path.GetDirectoryName(cc), true);
            var stray = Path.Combine(s.PrimaryFolder, "NES Classic Backup 2026-11-14 0000 (console abcdef12).zip.partial"); File.WriteAllBytes(stray, new byte[1000]);
            rows = BackupList.List(s);
            Check(oc.State == "canceled" && rows.Any(r => r.Path == oc.PartialPath && r.Status == BrowseStatus.Incomplete && r.Detail.Contains("canceled")) && rows.Any(r => r.Path == stray && r.Status == BrowseStatus.Incomplete),
                "Incomplete: the canceled backup's unfinished file and a stray unfinished file are listed as Incomplete, never as backups");
            // missing
            File.Move(oa.PrimaryPath, oa.PrimaryPath + ".away");
            var rm = BackupList.List(s).FirstOrDefault(r => r.Path == oa.PrimaryPath);
            File.Move(oa.PrimaryPath + ".away", oa.PrimaryPath);
            Check(rm != null && rm.Status == BrowseStatus.Missing && !rm.CanOpen, "Missing: a finished backup whose file is no longer where it was saved (" + rm?.Detail + ")");
            // a copy brought in from elsewhere, then damaged in two ways
            var copy = Path.Combine(s.PrimaryFolder, "copied in.zip"); File.Copy(ob.PrimaryPath, copy);
            var rc = BackupList.List(s).FirstOrDefault(r => r.Path == copy);
            Check(rc != null && rc.Status == BrowseStatus.Complete && rc.Check == CheckState.NotChecked && rc.LastVerifiedUtc == null, "a backup copied in from elsewhere (the same backup id as B): listed on its own, not checked yet, no verified date invented");
            var cv = BackupList.Check(s, copy);
            Check(cv.Status == BrowseStatus.Complete && cv.Check == CheckState.Verified && cv.LastVerifiedBy == "check", "checked: Complete, verified now");
            Flip(copy, "reference/nand-physical.bin", 1000);
            rc = BackupList.List(s).First(r => r.Path == copy);
            Check(rc.Check == CheckState.ChangedSinceCheck && rc.LastVerifiedUtc == cv.LastVerifiedUtc, "one byte changed: \"Changed since it was last checked\"; the last verified date stays the real one, not refreshed (" + rc.StatusText + ")");
            var cs = BackupList.Check(s, copy);
            Check(cs.Status == BrowseStatus.Salvage && cs.DamagedParts.Contains("reference/nand-physical.bin") && cs.IntactParts.Contains("restore/nandc.img") && cs.LastVerifiedUtc == cv.LastVerifiedUtc && cs.CanOpenSalvage && !cs.CanOpen,
                "Salvage: the raw reference copy is damaged but the data storage still matches its trusted checksum (" + cs.Detail + ")");
            var so = BackupContents.Open(copy, s);
            Check(!so.Ok && so.Problem.Contains("did not pass"), "a damaged backup is never opened as a complete backup (" + so.Problem + ")");
            var sv = BackupContents.OpenSalvage(copy, s);
            if (sv.Ok)
                using (var c = sv.Contents)
                {
                    var phy = c.Find("m:reference/nand-physical.bin"); var save = c.Find("d:/clover/profiles/0/CLV-P-NAAAE/cartridge.sram");
                    Check(c.Salvage && phy.Unreadable != null && save != null && save.Unreadable == null && save.HashOrigin == HashOrigin.Inventory && c.Notes[0].StartsWith("This backup did not pass its full check") && !string.IsNullOrEmpty(c.DamageDetail),
                        "opened in salvage mode: the damaged member is marked and cannot be exported; files in the intact data storage keep their recorded checksums; its first note says plainly that it is not a complete backup, with the check's first problem kept for Technical details");
                    var x = Exporter.Export(c, ExportRequest.GameBundle(c, c.Games.First(g => g.Code == "CLV-P-NAAAE"), NewDir("browser-salvage")));
                    var px = Exporter.Export(c, One(c, phy, NewDir("browser-salvage-phy")));
                    Check(x.Ok && x.Files.Count >= 2 && px.State == "incomplete" && px.Files.Count == 0 && px.NotExported[0].reason.Contains("damaged"),
                        "from a salvage backup, files that still check out are exported and checked; the damaged member is refused with its reason (" + px.NotExported.FirstOrDefault().reason + ")");
                }
            else Check(false, "the salvage open failed: " + sv.Problem);
            Flip(copy, "restore/nandc.img", 5000000);
            var cd = BackupList.Check(s, copy);
            Check(cd.DamagedParts.Contains("restore/nandc.img") && cd.FilesChecked > 0 && (cd.FilesVerified > 0 ? cd.Status == BrowseStatus.Salvage && cd.CanOpenSalvage : cd.Status == BrowseStatus.Damaged && !cd.CanOpenSalvage),
                "once the data storage image itself is changed it is damaged as a whole; its files are checked one by one against the intact file list (" + cd.StatusText + ": " + cd.Detail + ")");
            var sd = BackupContents.OpenSalvage(copy, s);
            if (sd.Ok) using (var c = sd.Contents)
                {
                    var files = c.Entries.Where(e => e.Source == EntrySource.DataFile && e.Kind == EntryKind.File).ToList();
                    Check(c.DataProblem != null && files.Count == cd.FilesChecked && files.Count(e => e.Unreadable == null) == cd.FilesVerified && files.All(e => e.Unreadable == null ? e.HashOrigin == HashOrigin.Inventory : !e.HashTrusted),
                        "its files are listed from the surviving file list; only those whose own bytes still match their recorded checksum are readable, the rest are marked (" + files.Count(e => e.Unreadable == null) + " of " + files.Count + "; " + c.DataProblem + ")");
                }
            else Check(false, "the salvage open of the data-damaged copy failed: " + sd.Problem);
            File.Delete(copy);
            Check(!BackupList.List(s).Any(r => r.Path == copy), "a file that is gone and has no record of being finished is simply no longer listed");
        }

        // ------------------------------------------------------------------ the three views, classification, search and export on backup A
        static void BrowserViews(Settings s, Engine.BackupOutcome oa, Dictionary<string, Dictionary<string, object>> listing)
        {
            var o = BackupContents.Open(oa.PrimaryPath, s);
            Check(o.Ok, "backup A opens after its full check (" + o.Problem + ")");
            if (!o.Ok) return;
            using (var c = o.Contents)
            {
                // All files: every entry debugfs lists, with its metadata, and every member of the backup file
                var data = c.Entries.Where(e => e.Source == EntrySource.DataFile).ToDictionary(e => e.ImagePath);
                int metaBad = 0, shaBad = 0;
                foreach (var kv in listing)
                {
                    if (!data.TryGetValue(kv.Key, out var e)) { metaBad++; continue; }
                    var w = kv.Value;
                    if (e.TypeCode.ToString() != w.S("type") || Convert.ToString(e.Mode, 8).PadLeft(4, '0') != w.S("mode") || e.Uid != w.L("uid") || e.Gid != w.L("gid") || e.Inode != w.L("inode") || e.Links != w.L("links") || (e.Kind == EntryKind.File && e.Size != w.L("size")) || w.S("target") != null && e.LinkTarget != w.S("target")) metaBad++;
                    if (w.S("sha256") != null && (e.Sha256 != w.S("sha256") || e.HashOrigin != HashOrigin.Inventory)) shaBad++;
                }
                Check(data.Count == listing.Count && metaBad == 0, "All files: the same " + listing.Count + " data storage entries as debugfs, each with its type, permissions, owner, group, inode, links, size and link target (" + metaBad + " differ)");
                Check(shaBad == 0, "every regular file carries the SHA-256 recorded when the backup was made, equal to debugfs' own read");
                var members = c.Entries.Where(e => e.Source == EntrySource.Member).Select(e => e.Member).OrderBy(x => x, StringComparer.Ordinal).ToList();
                var zipNames = c.Verification.Members.Keys.Concat(new[] { BackupFormat.ManifestName }).OrderBy(x => x, StringComparer.Ordinal).ToList();
                Check(members.SequenceEqual(zipNames) && c.Entries.Where(e => e.Source == EntrySource.Member).All(e => e.HashTrusted), "every member of the backup file is an entry with a trusted checksum (" + members.Count + ")");
                var top = c.Children("").Select(e => e.Name).ToList();
                var dm = c.Find("m:" + c.DataMember);
                var roots = c.Children(dm.Id).Select(e => e.Name).OrderBy(x => x, StringComparer.Ordinal).ToList();
                Check(top.Contains("restore") && top.Contains("reference") && top.Contains(BackupFormat.ManifestName) && dm.IsImage && dm.ImageType == "ext2/3/4 file system" && roots.SequenceEqual(listing.Keys.Where(k => k.LastIndexOf('/') == 0).Select(k => k.Substring(1)).OrderBy(x => x, StringComparer.Ordinal)),
                    "the tree: the backup's members at the top, the data storage image (identified as ext4) holding the console's top folders (" + string.Join(", ", roots) + ")");
                var boot = c.Find("m:restore/boot2-active.img"); var b0 = c.Find("m:reference/boot0.bin"); var nanda = c.Find("m:restore/nanda.img");
                Check(boot.ImageType.StartsWith("Startup software") && b0.ImageType == "Bootloader (first stage)" && nanda.IsImage && nanda.ImageNote.Contains("not listed") && nanda.ImageType != null,
                    "storage images are identified by what they hold, and those not browsed file by file say so (" + nanda.ImageType + ")");
                var fifo = data["/unknown/a-fifo"]; var lnk = data["/link-to-notes"]; var empty = data["/unknown/empty folder"];
                Check(fifo.Kind == EntryKind.Special && fifo.TypeCode == 'p' && lnk.Kind == EntryKind.Link && lnk.LinkTarget == "unknown/notes for later (draft).txt" && data["/dangling-absolute"].LinkTarget == "/usr/share/games" && empty.Kind == EntryKind.Folder && c.Children(empty.Id).Count == 0 && data.ContainsKey("/unknown/\u00fcn\u00efc\u00f6d\u00e9 & spaces/file #1 [x].bin"),
                    "special files, links (as written, never followed), an empty folder and unusual names are all present");
                Check(data.Values.All(e => e.MtimeSeconds != null && e.DateNote == null) && data["/clover/profiles/0/CLV-P-NAAAE/cartridge.sram"].DateText.EndsWith("(console clock)"),
                    "every entry has its exact recorded date, labeled as the console's clock; none of the fixture's dates is implausible");
                // By type
                string c1 = CcsExport.ContentOf(File.ReadAllBytes(Path.Combine(Fx, "rom-1.nes"))).sha256, c2 = CcsExport.ContentOf(File.ReadAllBytes(Path.Combine(Fx, "rom-2.nes"))).sha256;
                const string Copy = "/ccs/pkg/0123456789abcdef.89abcdef";
                var want = new Dictionary<string, Category>
                {
                    ["/clover/profiles/0/CLV-P-NAAAE/cartridge.sram"] = Category.Saves, ["/clover/profiles/0/CLV-P-NAAAE/save.sav"] = Category.EverythingElse, ["/clover/profiles/0/settings.json"] = Category.SystemFiles,
                    ["/clover/system/version"] = Category.SystemFiles, ["/hakchi/config"] = Category.Settings, ["/hakchi/rootfs/bin/retroarch"] = Category.ModsAndEmulators, ["/hakchi/hmod/uninstall-retroarch"] = Category.ModsAndEmulators,
                    ["/hakchi/rootfs/etc/libretro/core/fceumm_libretro.so"] = Category.ModsAndEmulators, ["/hakchi/games/nes-usa/CLV-H-AAAAA/CLV-H-AAAAA.nes"] = Category.Games, ["/hakchi/games/nes-usa/CLV-H-AAAAA/CLV-H-AAAAA.desktop"] = Category.Games,
                    ["/hakchi/games/nes-usa/CLV-H-AAAAA/save/1.state"] = Category.EverythingElse, ["/hakchi/themes/custom menu/background.png"] = Category.ModsAndEmulators, ["/ccs/pkg/current"] = Category.Settings,
                    [Copy + "/roms/" + c1 + ".nes"] = Category.Games, [Copy + "/roms/" + c2 + ".nes"] = Category.Games, [Copy + "/session.json"] = Category.Settings, [Copy + "/startup/startup.json"] = Category.Settings,
                    [Copy + "/MANIFEST.json"] = Category.ModsAndEmulators, [Copy + "/home/config.json"] = Category.ModsAndEmulators, ["/ccs/data/saves/" + c1 + "/cart.sav"] = Category.Saves, ["/ccs/install/v-0123456789abcdef.89abcdef.json"] = Category.ModsAndEmulators,
                    ["/ccs-install-attempt.json"] = Category.EverythingElse, ["/unknown/exec.sh"] = Category.EverythingElse, ["/unknown/a-fifo"] = Category.EverythingElse, ["/link-to-notes"] = Category.EverythingElse,
                };
                var startup = data.Values.First(e => e.ImagePath.StartsWith(Copy + "/startup/startup-custom-", StringComparison.Ordinal));
                want[startup.ImagePath] = Category.Artwork;
                var wrong = want.Where(kv => !data.ContainsKey(kv.Key) || data[kv.Key].Category != kv.Value).Select(kv => kv.Key + " = " + (data.ContainsKey(kv.Key) ? data[kv.Key].Category.ToString() : "absent")).ToList();
                Check(wrong.Count == 0, "By type: each documented structure lands in its category; anything unrecognized stays in Everything else (" + string.Join("; ", wrong) + ")");
                Check(c.Find("m:restore/nandc.img").Category == Category.SystemFiles && c.Find("m:" + BackupFormat.ExportName).Category == Category.Games && c.Find("m:" + BackupFormat.InventoryName).Category == Category.EverythingElse,
                    "the backup's own parts: storage images in System files, the .ccs export in Games, its description in Everything else");
                var counts = c.Counts(); int nonFolders = c.Entries.Count(e => e.Kind != EntryKind.Folder);
                Check(counts.Values.Sum(v => v.count) == nonFolders && Categories.All.All(k => c.ByType(k).Count == counts[k].count) && c.Entries.Where(e => e.Kind != EntryKind.Folder).All(e => !string.IsNullOrEmpty(e.CategoryReason)),
                    "every file, link and special file is in exactly one of the eight categories, with its reason; nothing is left out (" + string.Join(", ", Categories.All.Select(k => Categories.Label(k) + " " + counts[k].count)) + ")");
                // By game
                var smb = c.Games.FirstOrDefault(g => g.Code == "CLV-P-NAAAE"); var hk = c.Games.FirstOrDefault(g => g.Code == "CLV-H-AAAAA");
                var g1 = c.Game("ccs:" + c1); var g2 = c.Game("ccs:" + c2);
                Check(smb != null && smb.Title == "Super Mario Bros." && smb.TitleKnown && smb.Kind == "NES Classic original game" && smb.BatterySaves.Single().Name == "cartridge.sram" && smb.OtherFiles.Single().Name == "save.sav" && smb.LatestSaveUtc != null,
                    "By game: a stock game's save folder is named from the NES Classic's own game list; its battery save and an unrecognized file stay with it");
                Check(hk != null && !hk.TitleKnown && hk.Title == "CLV-H-AAAAA" && hk.GameFiles.Select(e => e.Name).OrderBy(x => x).SequenceEqual(new[] { "CLV-H-AAAAA.desktop", "CLV-H-AAAAA.nes" }) && hk.OtherFiles.Any(e => e.Name == "1.state"),
                    "a hakchi game whose entry gives no name keeps its code, never a guessed name; its game file is recognized by its NES header, its other files stay with it");
                Check(g1 != null && g1.Title == "Test Cart One" && g1.GameFiles.Single().Name == c1 + ".nes" && g1.BatterySaves.Single().ConsolePath == "/var/lib/ccs/data/saves/" + c1 + "/cart.sav" && g2 != null && g2.Title == "My Own Test Cart" && g2.GameFiles.Count() == 1 && !c.Games.Any(g => g.Title == "Missing Cart"),
                    "Classic Cart Studio's games: game file and battery save together by the game's identity, named from the HOME's own list; a listed game with no files on the console has no group");
                var three = c.Games.Where(g => g.Code != null && g.Code.StartsWith("CLV-P-")).Select(g => g.Title).OrderBy(x => x).ToList();
                Check(three.SequenceEqual(new[] { "GRADIUS", "Super Mario Bros.", "Super Mario Bros. 3" }), "the three stock save folders are three games (" + string.Join(", ", three) + ")");
                // search and selection
                var sm = c.SearchGames("mario").Select(g => g.Code).ToList(); var srams = c.Search("CARTRIDGE.SRAM"); var rootfs = c.Search("/var/lib/hakchi/rootfs");
                Check(sm.Contains("CLV-P-NAAAE") && sm.Contains("CLV-P-NAACE") && c.SearchGames("TEST CART").Count == 2 && srams.Count == 3 && srams.All(e => e.Name == "cartridge.sram")
                      && rootfs.Count == listing.Keys.Count(k => k == "/hakchi/rootfs" || k.StartsWith("/hakchi/rootfs/", StringComparison.Ordinal)) && c.Search("zzz-not-there").Count == 0 && c.Search("naaae", c.ByType(Category.Saves)).Count == 1
                      && c.Search("Super Mario Bros. 3").Any(e => e.Name == "cartridge.sram"),
                    "search is case-insensitive over names, paths, games and categories, within a view or everywhere");
                var sel = new Selection(); sel.Add(smb.BatterySaves.First()); sel.Toggle(g1.GameFiles.First()); sel.Toggle(g1.GameFiles.First()); sel.Add(g1.BatterySaves.First());
                Check(sel.Count == 2 && sel.Entries(c).Count == 2, "the selection keeps exactly what was selected (toggling twice removes)");
                // export: the whole category
                var exp = NewDir("browser-export");
                var whole = Exporter.Export(c, ExportRequest.WholeCategory(c, Category.Saves, exp));
                var wm = Json.Parse(File.ReadAllText(whole.ManifestPath));
                int badFile = 0, badTime = 0;
                foreach (var f in wm.A("files").OfType<Dictionary<string, object>>())
                {
                    var full = Path.Combine(whole.Folder, f.S("exported_as")); var path = f.S("original_path").Substring("/var/lib".Length); var e = data[path];
                    if (!File.Exists(full) || Sha(full) != f.S("sha256") || f.S("sha256") != listing[path].S("sha256") || new FileInfo(full).Length != f.L("size") || f.S("checksum_from") != "inventory") badFile++;
                    if (File.GetLastWriteTimeUtc(full) != e.MtimeUtc || !f.O("console_date").S("source").Contains("console")) badTime++;
                }
                Check(whole.Ok && whole.Files.Count == c.ByType(Category.Saves).Count && wm.A("files").Count == whole.Files.Count && badFile == 0 && wm.O("asked_for").S("kind") == "whole-category",
                    "Export All of This Type (Saves): every save exported, each equal to debugfs' read, with the manifest naming the whole category (" + whole.Summary + ")");
                Check(badTime == 0 && File.Exists(Path.Combine(whole.Folder, "Super Mario Bros", "Battery saves", "cartridge.sram")) && File.Exists(Path.Combine(whole.Folder, "Test Cart One", "Battery saves", "cart.sav")),
                    "each file keeps the console's recorded time, and lands in its game's folder by role (\"Super Mario Bros\\Battery saves\\cartridge.sram\")");
                // export: only the search results
                var shown = c.Search("naaae", c.ByType(Category.Saves));
                var fr = Exporter.Export(c, ExportRequest.SearchResults(c, Category.Saves, "naaae", shown, exp));
                var fm = Json.Parse(File.ReadAllText(fr.ManifestPath)).O("asked_for");
                Check(fr.Ok && fr.Files.Count == 1 && fm.S("kind") == "search-results" && fm.L("shown") == 1 && fm.L("category_total") == whole.Files.Count && fm.S("text").Contains("filtered") && fr.Folder != whole.Folder,
                    "exporting search results exports only what is shown, and its manifest says it was filtered (" + fm.S("text") + "); it went into a new folder, not the earlier one");
                // export: a game bundle
                var gb = Exporter.Export(c, ExportRequest.GameBundle(c, g1, exp));
                var rom = Path.Combine(gb.Folder, "Game files", c1 + ".nes");
                Check(gb.Ok && File.Exists(rom) && Sha(rom) == Sha(Path.Combine(Fx, "rom-1.nes")) && File.Exists(Path.Combine(gb.Folder, "Battery saves", "cart.sav")) && Path.GetFileName(gb.Folder).StartsWith("Test Cart One", StringComparison.Ordinal),
                    "a game bundle: \"Test Cart One\" with its game file (identical to the original) and battery save, in a folder named for the game");
                // export: a folder with its original layout, links and a FIFO
                var fsel = new Selection(); fsel.Add(data["/unknown"]); fsel.Add(data["/link-to-notes"]); fsel.Add(data["/dangling-absolute"]);
                var fx = Exporter.Export(c, ExportRequest.Selected(c, fsel, exp, ExportLayout.OriginalPaths));
                var under = listing.Where(kv => kv.Key.StartsWith("/unknown/", StringComparison.Ordinal) && kv.Value.S("type") == "f").ToList();
                bool allThere = under.All(kv => { var p = Path.Combine(new[] { fx.Folder, "Console files" }.Concat(kv.Key.Trim('/').Split('/')).ToArray()); return File.Exists(p) && Sha(p) == kv.Value.S("sha256"); });
                var fxm = Json.Parse(File.ReadAllText(fx.ManifestPath)); var described = fxm.A("links_and_special_files").OfType<Dictionary<string, object>>().ToList();
                Check(fx.Ok && allThere && Directory.Exists(Path.Combine(fx.Folder, "Console files", "unknown", "empty folder")) && fx.Files.Count == under.Count,
                    "a selected folder in its original layout: all " + under.Count + " files (deep, Unicode and spaced names) exported and equal to debugfs' read; the empty folder is recreated");
                Check(described.Count == 3 && described.Any(d => d.S("target") == "/usr/share/games" && d.S("kind") == "link") && described.Any(d => d.S("kind").Contains("FIFO"))
                      && !File.Exists(Path.Combine(fx.Folder, "Console files", "link-to-notes")) && !File.Exists(Path.Combine(fx.Folder, "Console files", "dangling-absolute")) && !File.Exists(Path.Combine(fx.Folder, "Console files", "unknown", "a-fifo")),
                    "links and the FIFO are described in the manifest (targets as written) and nothing is created for them on the PC");
                // export: a member above 64,000,000 bytes (the data storage image), then the same canceled part way
                var big = Exporter.Export(c, One(c, dm, exp));
                var bf = big.Files.FirstOrDefault(); var bp = bf == null ? null : Path.Combine(big.Folder, bf.RelativePath);
                Check(big.Ok && bp != null && new FileInfo(bp).Length == new FileInfo(Path.Combine(Fx, "data-modified.img")).Length && Sha(bp) == Sha(Path.Combine(Fx, "data-modified.img")),
                    "a " + Engine.Units.Gb(dm.Size) + " member exports completely and verified (the data storage image, identical to the fixture)");
                long seen = 0;
                var cr = Exporter.Export(c, One(c, dm, exp), () => Interlocked.Read(ref seen) > 100L * 1000 * 1000, p => Interlocked.Exchange(ref seen, p.BytesDone));
                var crm = Json.Parse(File.ReadAllText(cr.ManifestPath));
                Check(cr.State == "canceled" && !cr.Ok && cr.Files.Count == 0 && cr.NotExported.Count == 1 && cr.NotExported[0].reason.Contains("Canceled") && !Directory.EnumerateFiles(cr.Folder, "*", SearchOption.AllDirectories).Any(f => !f.EndsWith(Exporter.ManifestName, StringComparison.Ordinal)) && !crm.B("complete") && crm.S("state") == "canceled",
                    "Cancel part way: nothing is left but the manifest saying canceled; the unfinished copy is removed and the item is listed as not exported (" + cr.Summary + ")");
                // refused destinations
                var bad = new[] { @"\\server\share\x", @"\\?\C:\x", @"\\.\PhysicalDrive0", "relative\\folder", exp + "\\..\\" + Path.GetFileName(exp), exp + ":stream", Path.Combine(exp, "does-not-exist"), "" };
                var refusedAll = bad.Select(b => Exporter.Export(c, One(c, data["/hakchi/config"], b))).ToList();
                Check(refusedAll.All(r => r.State == "refused" && r.Folder == null), "UNC, device, relative, \"..\", stream, missing and empty destinations are refused before anything is written");
                // preview: verified bytes only
                var pic = c.ReadVerified(startup, 1000000, out var why);
                Check(pic != null && BackupContents.ImageKindOf(pic) == "jpeg" && c.ReadVerified(dm, 1000000, out var why2) == null && why2.Contains("too large"), "a preview gets the picture's bytes only after they match their checksum; a large file is not previewed but stays exportable");
            }
        }

        // ------------------------------------------------------------------ backup B: above 64 MB, a zero date, the derived index, hostile names and collisions
        static void BrowserBig(Settings s, Engine.BackupOutcome ob)
        {
            var o = BackupContents.Open(ob.PrimaryPath, s);
            Check(o.Ok, "backup B opens (" + o.Problem + ")"); if (!o.Ok) return;
            string expected = BigSha(); var exp = NewDir("browser-big"); string idx;
            using (var c = o.Contents)
            {
                var e = c.Find("d:/unknown/sparse-ish.bin");
                // every file is hashed in full when the backup is made (no 64,000,000-byte limit any more): the checksum comes from its file list
                Check(e.Size == BigFile && e.HashOrigin == HashOrigin.Inventory && e.HashTrusted && e.Sha256 == expected, "a 70,000,000-byte file has its full checksum in the backup's file list (equal to the independent expectation)");
                Check(e.DateNote != null && e.DateNote.Contains("zero") && e.MtimeUtc == new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc), "its date reads zero and is flagged as no date recorded (" + e.DateNote + ")");
                var x = Exporter.Export(c, One(c, e, exp)); var f = x.Files.FirstOrDefault(); var p = f == null ? null : Path.Combine(x.Folder, f.RelativePath);
                var xm = x.ManifestPath == null ? null : Json.Parse(File.ReadAllText(x.ManifestPath)).A("files").OfType<Dictionary<string, object>>().FirstOrDefault();
                Check(x.Ok && new FileInfo(p).Length == BigFile && Sha(p) == expected && xm?.S("checksum_from") == "inventory",
                    "it exports completely, read back and checked against the checksum the backup recorded (" + Engine.Units.Gb(BigFile) + ")");
                Check(f.DateSet && f.DateNote != null && File.GetLastWriteTimeUtc(p) == new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc) && x.DateNotes.Count() == 1, "its zero date is kept as recorded and reported (" + f.DateNote + ")");
                c.DeriveMissingHashes();
                idx = c.DerivedIndexPath;
                Check(c.HashesComplete && idx == null && !Directory.EnumerateFiles(s.RecordsFolder, "derived-index-*").Any(), "nothing is missing, so no derived index is made (it is only for earlier backups that left large files without a checksum)");
            }
            // hostile names and case collisions, in the original layout
            using (var c = BackupContents.Open(ob.PrimaryPath, s).Contents)
            {
                var sel = new Selection(); sel.Add(c.Find("d:/unknown")); sel.Add(c.Find("d:/clover")); sel.Add(c.Find("d:/Clover"));
                var x = Exporter.Export(c, ExportRequest.Selected(c, sel, exp, ExportLayout.OriginalPaths));
                var rels = x.Files.Select(f => f.RelativePath).ToList();
                Check(x.Ok && x.Files.Count == c.Entries.Count(e => e.Source == EntrySource.DataFile && e.Kind == EntryKind.File && (e.ImagePath.StartsWith("/unknown/") || e.ImagePath.StartsWith("/clover/") || e.ImagePath.StartsWith("/Clover/"))),
                    "every file under three folders with hostile names and colliding names was exported and checked (" + x.Summary + ")");
                Check(rels.All(r => r.Split('\\').All(SafeNames.IsSafe) && Path.GetFullPath(Path.Combine(x.Folder, r)).StartsWith(x.Folder + "\\", StringComparison.OrdinalIgnoreCase) && File.Exists(Path.Combine(x.Folder, r))),
                    "every exported name is a safe Windows name inside the export folder: nothing reached outside it");
                Check(File.Exists(Path.Combine(x.Folder, "Console files", "unknown", "_con.sh")) && File.Exists(Path.Combine(x.Folder, "Console files", "unknown", "a", "b", "c", "d", "e", "f", "g", "..-a -b__"))
                      && rels.Any(r => r.StartsWith(@"Console files\unknown\AUX_", StringComparison.Ordinal)),
                    "a reserved device name with a trailing dot becomes \"_con.sh\"; backslashes, a colon and wildcards become \"..-a -b__\"; a control character and a trailing space are removed");
                var cl = Path.Combine(x.Folder, "Console files");
                Check(File.Exists(Path.Combine(cl, "Clover", "config")) && File.Exists(Path.Combine(cl, "clover (2)", "system", "version")) && File.Exists(Path.Combine(cl, "clover (2)", "profiles", "0", "CLV-P-NAAAE", "cartridge.sram")) && File.Exists(Path.Combine(cl, "clover (2)", "profiles", "0", "clv-p-naaae (2)", "cartridge.sram")),
                    "folders whose names differ only by case get separate folders with deterministic suffixes (\"Clover\" and \"clover (2)\", \"CLV-P-NAAAE\" and \"clv-p-naaae (2)\")");
                var firstHashes = Directory.GetFiles(x.Folder, "*", SearchOption.AllDirectories).ToDictionary(f => f, f => Sha(f));
                var y = Exporter.Export(c, ExportRequest.Selected(c, sel, exp, ExportLayout.OriginalPaths));
                Check(y.Ok && y.Folder != x.Folder && y.Files.Select(f => f.RelativePath).SequenceEqual(rels) && firstHashes.All(kv => File.Exists(kv.Key) && Sha(kv.Key) == kv.Value),
                    "the same export again goes into a new folder with exactly the same names (deterministic); the first export is untouched (nothing is ever written over)");
                var hostile = c.Find("d:/unknown/" + Hostile27); var lk = c.Find("d:/link-to-notes");
                Check(hostile != null && lk.LinkTarget == "unknown/notes for later (draft).txt" && c.Find("d:/unknown/con.sh.").Category == Category.EverythingElse && c.Games.All(g => g.Code != "clv-p-naaae"),
                    "the hostile names are browsed exactly as recorded, and a folder that is not named the way the console names save folders is not taken for a game");
            }
        }

        // ------------------------------------------------------------------ the documented rules the fixtures do not exercise, on synthetic entries
        static void BrowserRules()
        {
            var files = new Dictionary<string, string>(StringComparer.Ordinal); var entries = new List<BrowseEntry>();
            void D(string p) => entries.Add(new BrowseEntry { Id = "d:" + p, ImagePath = p, ConsolePath = "/var/lib" + p, Name = p.Substring(p.LastIndexOf('/') + 1), Source = EntrySource.DataFile, Kind = EntryKind.Folder, TypeCode = 'd' });
            void F(string p, string text = "x") { files[p] = text; entries.Add(new BrowseEntry { Id = "d:" + p, ImagePath = p, ConsolePath = "/var/lib" + p, Name = p.Substring(p.LastIndexOf('/') + 1), Source = EntrySource.DataFile, Kind = EntryKind.File, TypeCode = 'f', Size = Encoding.UTF8.GetByteCount(text), Inode = (uint)entries.Count + 100 }); }
            const string Sv = "/clover/profiles/0/CLV-P-NAANE", Hk = "/hakchi/games/nes-usa/.storage/CLV-H-BBBBB", Linked = "/clover/profiles/0/CLV-H-ZZZZZ", Pk = "/ccs/pkg/0123456789abcdef.89abcdef";
            var sha = new string('a', 64); var cs = "/ccs/data/saves/" + sha;
            foreach (var d in new[] { "/clover", "/clover/profiles", "/clover/profiles/0", Sv, Sv + "/suspendpoint2", "/hakchi", "/hakchi/games", "/hakchi/games/nes-usa", "/hakchi/games/nes-usa/.storage", Hk, Linked, "/ccs", "/ccs/pkg", Pk, Pk + "/roms", Pk + "/bin", Pk + "/home", Pk + "/home/cover", Pk + "/licenses", "/ccs/data", "/ccs/data/saves", cs })
                D(d);
            F(Sv + "/cartridge.sram"); F(Sv + "/1.state"); F(Sv + "/4.state"); F(Sv + "/5.state"); F(Sv + "/suspendpoint2/state.tar"); F(Sv + "/suspendpoint2/screenshot.png");
            F(Hk + "/CLV-H-BBBBB.desktop", "[Desktop Entry]\nType=Application\nExec=/bin/clover-kachikachi-wr /var/games/CLV-H-BBBBB/game.7z --volume 75\nPath=/var/saves/CLV-H-ZZZZZ\nName=Zap Game\nIcon=/var/games/CLV-H-BBBBB/cover.png\n\n[X-CLOVER Game]\nCode=CLV-H-BBBBB\n");
            F(Hk + "/game.7z"); F(Hk + "/cover.png"); F(Hk + "/CLV-H-BBBBB_small.png"); F(Hk + "/notes.txt"); F(Linked + "/save.sram");
            var rom = new string('b', 64);
            F(Pk + "/MANIFEST.json", "{\"schema\":\"ccs-console-package/1\",\"files\":{\"roms/" + rom + ".nes\":{\"kind\":\"rom\"},\"bin/ccs-player-armv7\":{\"kind\":\"executable\"},\"home/cover/c1.jpg\":{\"kind\":\"home\"},\"home/pack.json\":{\"kind\":\"home\"},\"licenses/NOTICE.txt\":{\"kind\":\"license\"}}}");
            F(Pk + "/home/manifest.json", "{\"files\":{\"cover/c1.jpg\":{\"category\":\"game-covers\",\"type\":\"cover-jpeg\"},\"pack.json\":{\"category\":\"catalog\",\"type\":\"pack-json\"}}}");
            F(Pk + "/home/games.json", "{\"games\":[{\"id\":\"g1\",\"cover\":\"cover/c1.jpg\"}]}");
            F(Pk + "/session.json", "{\"games\":[{\"id\":\"g1\",\"title\":\"Listed Game\",\"rom\":\"/tmp/ccs/pkg/roms/" + rom + ".nes\",\"content_sha256\":\"" + rom + "\"}]}");
            F(Pk + "/roms/" + rom + ".nes"); F(Pk + "/bin/ccs-player-armv7"); F(Pk + "/home/cover/c1.jpg"); F(Pk + "/home/pack.json"); F(Pk + "/licenses/NOTICE.txt"); F(Pk + "/stray.bin");
            F(cs + "/cart.sav"); F(cs + "/slot1.ccsstate"); F(cs + "/slot4.ccsstate"); F(cs + "/continue.ccsstate"); F(cs + "/slot-names.json"); F(cs + "/session.lock"); F(cs + "/game.json", "{\"last_title\":\"Zed\"}"); F(cs + "/extra.bin");
            var games = Classifier.Run(entries, (e, max) => files.TryGetValue(e.ImagePath, out var t) ? Encoding.UTF8.GetBytes(t) : null, (e, n) => files.TryGetValue(e.ImagePath, out var t) ? Encoding.UTF8.GetBytes(t).Take(n).ToArray() : null);
            BrowseEntry E(string p) => entries.First(e => e.ImagePath == p);
            var nane = games.First(g => g.Code == "CLV-P-NAANE"); var sp = nane.SuspendPoints;
            Check(nane.Title == "The Legend of Zelda" && nane.BatterySaves.Single().Name == "cartridge.sram" && sp.Keys.SequenceEqual(new[] { 1, 2, 4 }) && sp[2].Count == 2 && E(Sv + "/5.state").Category == Category.EverythingElse && E(Sv + "/5.state").GameKey == nane.Key,
                "stock saves: 1.state-4.state and a suspendpointN folder (its files kept together) are suspend points 1-4; 5.state is not documented, so it stays with the game as Everything else");
            var zap = games.First(g => g.Code == "CLV-H-BBBBB");
            Check(zap.Title == "Zap Game" && zap.GameFiles.Select(e => e.Name).OrderBy(x => x).SequenceEqual(new[] { "CLV-H-BBBBB.desktop", "game.7z" }) && zap.BoxArt?.Name == "cover.png" && zap.Artwork.Count() == 2
                  && zap.BatterySaves.Single().ImagePath == Linked + "/save.sram" && E(Hk + "/notes.txt").Category == Category.EverythingElse && !games.Any(g => g.Code == "CLV-H-ZZZZZ"),
                "a hakchi game: Name=, the file Exec= names, its Icon= picture and hakchi's picture names; Path= joins its save folder to it, even under another code");
            var listed = games.First(g => g.ContentSha256 == rom); var own = games.First(g => g.ContentSha256 == sha);
            Check(listed.Title == "Listed Game" && listed.GameFiles.Single().Name == rom + ".nes" && listed.BoxArt?.Name == "c1.jpg" && E(Pk + "/bin/ccs-player-armv7").Category == Category.ModsAndEmulators && E(Pk + "/home/pack.json").Category == Category.ModsAndEmulators
                  && E(Pk + "/home/cover/c1.jpg").Category == Category.Artwork && E(Pk + "/stray.bin").Category == Category.ModsAndEmulators
                  && E(Pk + "/licenses/NOTICE.txt").Category == Category.ModsAndEmulators && (E(Pk + "/licenses/NOTICE.txt").CategoryReason ?? "").Contains("notice of its parts"),
                "a HOME package: its list's kinds (its notices and license texts too), the game list's game file and the HOME's cover for that game");
            var osp = own.SuspendPoints;
            Check(own.Title == "Zed" && own.TitleSource.Contains("last name") && own.BatterySaves.Single().Name == "cart.sav" && osp.Keys.SequenceEqual(new[] { 0, 1, 4 }) && E(cs + "/slot-names.json").Category == Category.Settings
                  && E(cs + "/session.lock").Category == Category.SystemFiles && E(cs + "/extra.bin").Category == Category.EverythingElse && E(cs + "/extra.bin").GameKey == own.Key,
                "Classic Cart Studio saves: cart.sav, slots 1-4 and the older continuation, their details as settings; named by game.json when no HOME lists the game");
            Check(entries.Where(e => e.Kind == EntryKind.File).All(e => e.CategoryReason != null), "every synthetic file got a category and a reason");
        }

        // ------------------------------------------------------------------ names, without a backup
        static void BrowserNames()
        {
            var cases = new[] { "CON", "con.txt", "Nul.tar.gz", "COM1", "lpt9.log", "COM\u00b9", "CONIN$", "a.", "b ", "c . .", ".", "..", "...", "", "x:y", "file.txt:stream", "..\\..\\evil", "C:\\Windows\\system32", "\\\\server\\share",
                                "a/b", "<>|?*\"", "tab\there", "\u0001\u001f", "\ud800lone", "ok \ud83d\ude00 emoji", new string('n', 300) + ".sram", new string('n', 300), "AUX .txt", "Clock$" };
            var outs = cases.Select(x => SafeNames.Component(x)).ToList();
            Check(outs.All(SafeNames.IsSafe) && outs.All(x => x.Length <= SafeNames.MaxComponent + 2) && cases.Select(x => SafeNames.Component(x)).SequenceEqual(outs),
                "every hostile name becomes one safe, deterministic Windows name (" + string.Join(" | ", outs.Take(16)) + ")");
            Check(SafeNames.Component(new string('n', 300) + ".sram").EndsWith(".sram") && SafeNames.Component("ok \ud83d\ude00 emoji") == "ok \ud83d\ude00 emoji" && SafeNames.Component("Super Mario Bros.") == "Super Mario Bros" && SafeNames.Component("con.txt") == "_con.txt",
                "long names keep their extension, ordinary Unicode is kept, a trailing dot is dropped, a device name gets a leading \"_\"");
            var a = new ExportPaths(); a.Reserve("", Exporter.ManifestName, "m");
            var p1 = a.File("", "Save.sav", "1"); var p2 = a.File("", "save.sav", "2"); var p3 = a.File("", "SAVE.SAV", "3"); var p4 = a.File("", Exporter.ManifestName, "4"); var again = a.File("", "Save.sav", "1");
            var d1 = a.Folder("", "Dir", "d1"); var d2 = a.Folder("", "dir", "d2"); var f1 = a.File(d1, "x", "x1"); var f2 = a.File(d2, "x", "x2");
            Check(p1 == "Save.sav" && p2 == "save (2).sav" && p3 == "SAVE (3).SAV" && p4 != Exporter.ManifestName && again == p1 && d2 == "dir (2)" && f1 == "Dir\\x" && f2 == "dir (2)\\x",
                "names that collide ignoring case get \" (2)\", \" (3)\" before the extension; the same original keeps its place; nothing can take the manifest's name");
        }

        // ------------------------------------------------------------------ helpers
        static ExportRequest One(BackupContents c, BrowseEntry e, string dest) { var r = new ExportRequest { Destination = dest, Scope = ExportScope.Selection, FolderName = "One file", ScopeText = "One selected item." }; r.Entries.Add(e); return r; }

        static void Flip(string zip, string member, long at)
        {
            long off; using (var z = ZipReader.Open(zip, BackupFormat.Allowed)) off = z.Find(member).DataOffset + at;
            using (var f = new FileStream(zip, FileMode.Open, FileAccess.ReadWrite)) { f.Position = off; int b = f.ReadByte(); f.Position = off; f.WriteByte((byte)(b ^ 0x20)); }
        }

        /// <summary>SHA-256 of the patched sparse file: the fixture's 200,000 zero bytes and "end", then zeros up to 70,000,000 bytes</summary>
        static string BigSha()
        {
            using (var h = SHA256.Create())
            {
                var head = new byte[200003]; Encoding.ASCII.GetBytes("end").CopyTo(head, 200000); h.TransformBlock(head, 0, head.Length, null, 0);
                var z = new byte[1 << 20]; long left = BigFile - head.Length;
                while (left > 0) { int n = (int)Math.Min(z.Length, left); h.TransformBlock(z, 0, n, null, 0); left -= n; }
                h.TransformFinalBlock(new byte[0], 0, 0); return Hex.Of(h.Hash);
            }
        }

        /// <summary>in-place edits of a copy of a fixture image (tests only): a file made larger (a sparse tail), its date, an entry renamed
        /// to another name of the same length</summary>
        sealed class ImagePatch : IDisposable
        {
            readonly FileStream f; readonly Ext4Reader r; readonly long first;
            public ImagePatch(string img) { f = new FileStream(img, FileMode.Open, FileAccess.ReadWrite); r = new Ext4Reader(f); first = BitConverter.ToUInt32(Read(1024, 1024), 0x14); }
            byte[] Read(long pos, int n) { var b = new byte[n]; f.Position = pos; int g = 0; while (g < n) { int k = f.Read(b, g, n - g); if (k <= 0) throw new EndOfStreamException(); g += k; } return b; }
            void Write(long pos, byte[] b) { f.Position = pos; f.Write(b, 0, b.Length); }
            uint Ino(string p) => r.Lookup(p) ?? throw new InvalidOperationException("not in the image: " + p);
            long InodeAt(uint ino)
            {
                uint g = (ino - 1) / r.InodesPerGroup, i = (ino - 1) % r.InodesPerGroup;
                var d = Read((first + 1) * r.BlockSize + (long)g * r.DescSize, r.DescSize); long table = BitConverter.ToUInt32(d, 8); if (r.DescSize >= 64) table |= (long)BitConverter.ToUInt32(d, 0x28) << 32;
                return table * r.BlockSize + (long)i * r.InodeSize;
            }
            public void Grow(string p, long size)
            {
                var ino = Ino(p); var node = r.Inode(ino); long old = r.FileSize(node); var blocks = r.Blocks(node);
                long last = blocks[(int)((old - 1) / r.BlockSize)]; int used = (int)(old % r.BlockSize);
                if (used != 0 && last != 0) Write(last * r.BlockSize + used, new byte[r.BlockSize - used]);   // the old last block's slack reads as zeros
                var at = InodeAt(ino); Write(at + 4, BitConverter.GetBytes((uint)size)); Write(at + 0x6C, BitConverter.GetBytes((uint)(size >> 32)));
            }
            public void SetMtime(string p, uint sec, uint extra) { var at = InodeAt(Ino(p)); Write(at + 0x10, BitConverter.GetBytes(sec)); if (r.InodeSize >= 0x8C) Write(at + 0x88, BitConverter.GetBytes(extra)); }
            public void Rename(string p, string name)
            {
                int cut = p.LastIndexOf('/'); var parent = p.Substring(0, cut); var old = Encoding.UTF8.GetBytes(p.Substring(cut + 1)); var nb = Encoding.UTF8.GetBytes(name);
                if (nb.Length != old.Length) throw new ArgumentException("a rename here keeps the name's length: " + name);
                var child = Ino(p); var pino = parent.Length == 0 ? 2u : Ino(parent);
                foreach (var blk in r.Blocks(r.Inode(pino)))
                {
                    if (blk == 0) continue; var b = Read(blk * r.BlockSize, r.BlockSize); int q = 0;
                    while (q + 8 <= r.BlockSize)
                    {
                        uint ci = BitConverter.ToUInt32(b, q); int rec = BitConverter.ToUInt16(b, q + 4), nl = b[q + 6]; if (rec < 8) break;
                        if (ci == child && nl == old.Length && b.Skip(q + 8).Take(nl).SequenceEqual(old)) { Write(blk * r.BlockSize + q + 8, nb); return; }
                        q += rec;
                    }
                }
                throw new InvalidOperationException("entry not found: " + p);
            }
            public void Dispose() { f.Flush(true); f.Dispose(); }
        }
    }
}
