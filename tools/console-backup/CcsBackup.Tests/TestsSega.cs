// Classic Cart Studio console backup and restore engine tests: pictures for the Sega Mini menu. The shared rule (which existing files are
// offered for removal and which stay) on made-up listings, then the optional clean-up on simulated consoles (changes through debugfs in
// WSL): declined, refused without using the confirmation, removed, put back exactly; Composer leaves these pictures out. GPL-3.0-or-later.
using CcsBackup.Archive;
using CcsBackup.Browse;
using CcsBackup.Device;
using CcsBackup.Engine;
using CcsBackup.Library;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using static CcsBackup.Tests.Program;

namespace CcsBackup.Tests
{
    static partial class Tests
    {
        static BrowseEntry DataEntry(string path, EntryKind kind = EntryKind.File, string role = null, int links = 1, uint inode = 0, long size = 1000)
            => new BrowseEntry { Source = EntrySource.DataFile, ImagePath = path, ConsolePath = "/var/lib" + path, Name = path.Substring(path.LastIndexOf('/') + 1), Kind = kind,
                                 TypeCode = kind == EntryKind.File ? 'f' : kind == EntryKind.Folder ? 'd' : kind == EntryKind.Link ? 'l' : 'c', Role = role, Links = links, Inode = inode, Size = size, Allocated = 4096 };

        public static void Sega()
        {
            // ---- the rule's names (Hakchi2 CE 3.9.3: NesMenuElementBase from the icon file, NesMenuFolder from the code)
            var names = SegaPictures.NamesFor("CLV-H-AAAAA", "CLV-H-AAAAA.png");
            Check(names.SetEquals(new[] { "CLV-H-AAAAA_mdmini.png" }) && SegaPictures.NamesFor("CLV-S-00001", null).SetEquals(new[] { "CLV-S-00001_mdmini.png" })
                  && SegaPictures.NamesFor("CLV-H-AAAAA", "cover.png").SetEquals(new[] { "CLV-H-AAAAA_mdmini.png", "cover_mdmini.png" }), "the names: <icon name>_mdmini<ext> and <code>_mdmini.png, nothing else");
            Check(SegaPictures.Explanation == "These pictures are only used if you start a Sega Mini menu on this console.", "the approved explanation, word for word");

            // ---- the rule on a made-up listing: only a regular, unshared, unnamed picture in a hakchi entry, named as hakchi names it
            const string G = "/hakchi/games/nes-usa/.storage/CLV-H-AAAAA";
            var desk = DataEntry(G + "/CLV-H-AAAAA.desktop", inode: 10);
            var list = new List<BrowseEntry>
            {
                desk,
                DataEntry(G + "/CLV-H-AAAAA_mdmini.png", role: SegaPictures.Role, inode: 11),                                        // eligible
                DataEntry("/hakchi/games/nes-usa/.storage/CLV-H-BBBBB/CLV-H-BBBBB_mdmini.png", role: SegaPictures.Role, links: 2, inode: 12),   // shared with another name
                DataEntry("/hakchi/games/nes-usa/.storage/CLV-H-CCCCC/CLV-H-CCCCC_mdmini.png", EntryKind.Link, SegaPictures.Role, inode: 13), // a link
                DataEntry(G + "/other_mdmini.png", inode: 14),                                                                       // not hakchi's name
                DataEntry("/ccs/data/CLV-H-AAAAA_mdmini.png", role: SegaPictures.Role, inode: 15),                                    // outside hakchi's games
                DataEntry("/hakchi/rootfs/usr/share/CLV-M2-ABCDE_mdmini.png", inode: 16),                                             // a mod's own file
                DataEntry("/hakchi/games/nes-usa/.storage/CLV-H-DDDDD/CLV-H-DDDDD_mdmini.png", role: "box-art", inode: 17),           // shown by its entry
                DataEntry(G + "/CLV-H-AAAAA_mdmini.png.bak", inode: 18),                                                             // not the name at all
            };
            var named = DataEntry("/hakchi/games/nes-usa/.storage/CLV-H-EEEEE/CLV-H-EEEEE_mdmini.png", role: SegaPictures.Role, inode: 19); list.Add(named);
            var unread = DataEntry("/hakchi/games/nes-usa/.storage/CLV-H-FFFFF/CLV-H-FFFFF_mdmini.png", role: SegaPictures.Role, inode: 20); unread.Unreadable = "damaged"; list.Add(unread);
            var ref_ = DataEntry("/hakchi/games/nes-usa/.storage/CLV-H-EEEEE/CLV-H-EEEEE.desktop", inode: 21); list.Add(ref_);
            string Text(BrowseEntry d) => d == ref_ ? "[Desktop Entry]\nIcon=/var/games/CLV-H-EEEEE/CLV-H-EEEEE.png\nX-Menu-Picture=/var/games/CLV-H-EEEEE/CLV-H-EEEEE_mdmini.png\n" : "[Desktop Entry]\nIcon=/var/games/CLV-H-AAAAA/CLV-H-AAAAA.png\n";
            var set = SegaPictures.Find(list, Text);
            Check(set.Complete && set.Eligible.Count == 1 && set.Eligible[0].Path == "hakchi/games/nes-usa/.storage/CLV-H-AAAAA/CLV-H-AAAAA_mdmini.png" && set.Bytes == 1000 && set.Allocated == 4096,
                  "eligible: exactly the regular, unshared picture hakchi named in its game's folder (" + string.Join(", ", set.Eligible.Select(x => x.Path)) + ")");
            string WhyOf(string p) => set.Kept.Where(k => k.entry.ImagePath == p).Select(k => k.why).FirstOrDefault() ?? "";
            Check(WhyOf("/hakchi/games/nes-usa/.storage/CLV-H-BBBBB/CLV-H-BBBBB_mdmini.png").Contains("shared with another name") && WhyOf("/hakchi/games/nes-usa/.storage/CLV-H-CCCCC/CLV-H-CCCCC_mdmini.png").Contains("link")
                  && WhyOf(G + "/other_mdmini.png").Contains("not known") && WhyOf("/ccs/data/CLV-H-AAAAA_mdmini.png").Contains("outside hakchi's game folders")
                  && WhyOf("/hakchi/rootfs/usr/share/CLV-M2-ABCDE_mdmini.png").Contains("not known") && WhyOf("/hakchi/games/nes-usa/.storage/CLV-H-DDDDD/CLV-H-DDDDD_mdmini.png").Contains("shows it")
                  && WhyOf(named.ImagePath).Contains("a menu entry names it") && WhyOf(unread.ImagePath).Contains("cannot be read") && set.Kept.All(k => k.entry.Name != "CLV-H-AAAAA_mdmini.png.bak") && set.Kept.Count == 8,
                  "kept, each with why: shared, a link, unknown owner, outside hakchi's games, a mod's own file, shown by its entry, named by an entry, unreadable; a longer name is not matched at all");
            var partial = SegaPictures.Find(list, d => d == ref_ ? null : Text(d));
            Check(!partial.Complete && partial.Eligible.Count == 0 && partial.Kept.Count == 9, "a menu entry that cannot be read: nothing is offered (a menu might name a picture)");
            Check(SegaPictures.Find(list, Text, complete: false).Eligible.Count == 0, "an incomplete listing (a damaged backup): nothing is offered");
            var twice = new List<BrowseEntry>(list) { DataEntry("/hakchi/games/nes-usa/.storage/CLV-H-GGGGG/CLV-H-GGGGG_mdmini.png", role: SegaPictures.Role, inode: 11) };
            var tw = SegaPictures.Find(twice, Text); var ta = SegaPictures.Account(tw, "nes-classic-edition", 1000000, 4096);
            Check(tw.Eligible.Count == 1 && (long)ta["removed_bytes"] == 4096, "one stored file met twice (the same inode): counted once");
            foreach (var prof in new[] { "nes-classic-edition", "snes-classic-edition" })
            {
                var acc = SegaPictures.Account(set, prof, null, null);
                Check((long)acc["removed_bytes"] == 4096 && (long)acc["steady_bytes"] == -4096 && (string)acc["status"] == "fits", prof + " reference: removing them gives back their space once, and needs no room");
            }
            Check(SegaCleanup.NeverWritten("hakchi/games/nes-usa/CLV-H-AAAAA/CLV-H-AAAAA_mdmini.png") && !SegaCleanup.NeverWritten("hakchi/games/nes-usa/CLV-H-AAAAA/CLV-H-AAAAA.png") && !SegaCleanup.NeverWritten("ccs/data/x_mdmini.png"),
                  "Workbench never writes a picture for the Sega Mini menu into hakchi's game folders");
            Check(CcsBackup.Gate.CleanupWrites, "a real console: the clean-up's boundary is open in this version (its eligibility and strict-policy checks still apply)");
            SegaRuns();
        }

        static void SegaRuns()
        {
            SimulatedConsole.FileStore = new DebugfsFileStore();
            try
            {
                var s = SettingsIn(NewDir("sega"), false); var c = Console_("modified");
                const string g = "hakchi/games/nes-usa/CLV-H-AAAAA", f = "hakchi/games/nes-usa/CLV-S-00001", e = "hakchi/games/nes-usa/CLV-H-EEEEE";
                var pic = Encoding.ASCII.GetBytes(new string('m', 9000)); var fpic = Encoding.ASCII.GetBytes(new string('f', 5000));
                var fs = SimulatedConsole.FileStore;
                fs.Write(c, g + "/CLV-H-AAAAA.png", Encoding.ASCII.GetBytes("main cover")); fs.Write(c, g + "/CLV-H-AAAAA_small.png", Encoding.ASCII.GetBytes("small cover"));
                fs.Write(c, g + "/CLV-H-AAAAA_mdmini.png", pic);
                fs.Write(c, f + "/CLV-S-00001.desktop", Encoding.ASCII.GetBytes("[Desktop Entry]\nType=Application\nExec=/bin/chmenu 001 /var/games\nIcon=/var/games/CLV-S-00001/CLV-S-00001.png\nCode=CLV-S-00001\nName=Folder\n"));
                fs.Write(c, f + "/CLV-S-00001.png", Encoding.ASCII.GetBytes("folder cover")); fs.Write(c, f + "/CLV-S-00001_mdmini.png", fpic);
                fs.Write(c, g + "/other_mdmini.png", Encoding.ASCII.GetBytes("someone else's"));
                fs.Write(c, e + "/CLV-H-EEEEE.desktop", Encoding.ASCII.GetBytes("[Desktop Entry]\nType=Application\nExec=/bin/true\nIcon=/var/games/CLV-H-EEEEE/CLV-H-EEEEE.png\nX-Picture=/var/games/CLV-H-EEEEE/CLV-H-EEEEE_mdmini.png\nCode=CLV-H-EEEEE\n"));
                fs.Write(c, e + "/CLV-H-EEEEE_mdmini.png", Encoding.ASCII.GetBytes("named by its entry"));
                fs.Write(c, "ccs/data/CLV-H-AAAAA_mdmini.png", Encoding.ASCII.GetBytes("ccs data"));
                const string save = "clover/profiles/0/CLV-P-NAAAE/save.sav"; fs.Write(c, save, Encoding.ASCII.GetBytes("a save"));
                // another program's PC folders (its picture library and sync folder): never read or changed here
                var pc = NewDir("sega-pc-hakchi"); Directory.CreateDirectory(Path.Combine(pc, "games", "CLV-H-AAAAA"));
                foreach (var n in new[] { "CLV-H-AAAAA.png", "CLV-H-AAAAA_mdmini.png", "CLV-H-AAAAA_original.png" }) File.WriteAllBytes(Path.Combine(pc, "games", "CLV-H-AAAAA", n), Encoding.ASCII.GetBytes("pc " + n));
                string PcState() => string.Join(";", Directory.GetFiles(pc, "*", SearchOption.AllDirectories).OrderBy(x => x).Select(x => x + "=" + Sha(File.ReadAllBytes(x))));
                var pcBefore = PcState();
                var boots = new[] { "boot0", "boot1", "boot2" }.ToDictionary(t => t, t => Sha(SimulatedConsole.Dev(c, t)));
                var con = RecoverySessions.OpenSimulated(c, ImageKind.Files); var ctx = Ctx(s, ImageKind.Files);
                string St(string p) => con.FileStat("user", p).state;
                var keepStates = new[] { g + "/CLV-H-AAAAA.png", g + "/CLV-H-AAAAA_small.png", g + "/other_mdmini.png", e + "/CLV-H-EEEEE_mdmini.png", save, f + "/CLV-S-00001.png" }.ToDictionary(p => p, St);

                // declined: a review, no confirmation
                var (d0, dw) = SegaCleanup.PlanRemove(con, ctx);
                Check(d0 != null && d0.Refusal == null && d0.Changes.Count == 2 && d0.Changes.All(x => x.Op == "del") && d0.Changes.Select(x => x.Path).OrderBy(x => x).SequenceEqual(new[] { g + "/CLV-H-AAAAA_mdmini.png", f + "/CLV-S-00001_mdmini.png" }.OrderBy(x => x))
                      && d0.Protective?.Session == con.SessionId && Verifier.VerifyFile(d0.Protective.Path).Ok && d0.Notes.Contains(SegaPictures.Explanation) && d0.Notes.Contains(SegaPictures.Recreated),
                      "the review: the game's and the folder's pictures for the Sega Mini menu, from a backup made in this session; the approved explanation and the later-sync note (" + dw + ")");
                Check(d0.Kept.Any(k => k.StartsWith(g + "/other_mdmini.png")) && d0.Kept.Any(k => k.StartsWith(e + "/CLV-H-EEEEE_mdmini.png") && k.Contains("names it")) && d0.Kept.Any(k => k.StartsWith("ccs/data/")),
                      "unknown, named or outside pictures stay, each listed with why");
                long sum = d0.Changes.Sum(x => x.OldBytes);
                Check(sum == 9000 + 5000 && (long)d0.Account["removed_bytes"] == RoundUp(9000) + RoundUp(5000), "the exact list's bytes (" + sum + ") and the space removing them gives back, each file once");
                Check(St(g + "/CLV-H-AAAAA_mdmini.png") == Sha(pic) && St(f + "/CLV-S-00001_mdmini.png") == Sha(fpic), "declined (no confirmation): nothing removed");
                // the confirmation is not used up by a refusal: changed plan, missing protective backup, a picture changed on the console
                var (p, why) = SegaCleanup.PlanRemove(con, ctx); var keep = p.Nonce;
                var x1 = SegaCleanup.Execute(con, p, Other(p.Digest), keep, ctx);
                Check(x1.State == "refused" && p.Nonce == keep, "a confirmation for a different list: refused, the confirmation not used (" + x1.Problem + ")");
                File.Move(p.Protective.Path, p.Protective.Path + ".aside");
                var x2 = SegaCleanup.Execute(con, p, p.Digest, keep, ctx);
                Check(x2.State == "refused" && p.Nonce == keep && St(g + "/CLV-H-AAAAA_mdmini.png") == Sha(pic), "the backup made for the review missing: refused, nothing removed, the confirmation not used (" + x2.Problem + ")");
                File.Move(p.Protective.Path + ".aside", p.Protective.Path);
                var bad = File.ReadAllBytes(p.Protective.Path); var tampered = p.Protective.Path + ".t"; File.Copy(p.Protective.Path, tampered);
                using (var w = new FileStream(p.Protective.Path, FileMode.Open, FileAccess.Write)) { w.Seek(bad.Length / 2, SeekOrigin.Begin); w.WriteByte((byte)(bad[bad.Length / 2] ^ 0xFF)); }
                var x3 = SegaCleanup.Execute(con, p, p.Digest, keep, ctx);
                Check(x3.State == "refused" && p.Nonce == keep, "the backup made for the review changed (no longer valid): refused, the confirmation not used (" + x3.Problem + ")");
                File.Delete(p.Protective.Path); File.Move(tampered, p.Protective.Path);
                fs.Write(c, f + "/CLV-S-00001_mdmini.png", Encoding.ASCII.GetBytes("changed since the review"));
                var x4 = SegaCleanup.Execute(con, p, p.Digest, keep, ctx);
                Check(x4.State == "refused" && p.Nonce == keep && St(g + "/CLV-H-AAAAA_mdmini.png") == Sha(pic), "a picture changed on the console after the review: refused before anything is used up or written (" + x4.Problem + ")");
                fs.Write(c, f + "/CLV-S-00001_mdmini.png", fpic);
                // removed
                var o = SegaCleanup.Execute(con, p, p.Digest, keep, ctx);
                Check(o.Ok && p.Nonce == null && St(g + "/CLV-H-AAAAA_mdmini.png") == "absent" && St(f + "/CLV-S-00001_mdmini.png") == "absent" && o.Done.Count == 2, "removed: exactly the two reviewed pictures are gone (" + o.Problem + ")");
                Check(keepStates.All(kv => St(kv.Key) == kv.Value), "everything else is as it was: the main and small covers, the folder's picture, the kept pictures and the save");
                Check(boots.All(kv => Sha(SimulatedConsole.Dev(c, kv.Key)) == kv.Value), "the startup stages and kernel were never written");
                var again = SegaCleanup.Execute(con, p, p.Digest, keep, ctx);
                Check(again.State == "refused", "the same confirmation twice: refused");
                var whole = BackupContents.Open(p.Protective.Path);
                Check(whole.Ok && Verifier.VerifyFile(p.Protective.Path).Ok && SegaPictures.Find(whole.Contents.Entries, d => Encoding.UTF8.GetString(whole.Contents.ReadVerified(d, 65536, out _) ?? new byte[0])).Eligible.Count == 2,
                      "the backup made before the removal is whole and still holds both pictures");
                var rep = StorageReport.Build(whole.Contents);
                Check(rep.Sega.Eligible.Count == 2 && rep.Sega.Kept.Count >= 3 && rep.SegaAccount != null && (long)rep.SegaAccount["removed_bytes"] == RoundUp(9000) + RoundUp(5000), "the Storage view's row: the same two files, their space counted once, the kept ones listed");
                whole.Contents.Dispose();
                var rec = Json.Parse(File.ReadAllText(o.RecordFile));
                Check(rec.S("kind") == SegaCleanup.RemoveKind && (rec.A("changes") ?? new List<object>()).OfType<Dictionary<string, object>>().Count(x => x.S("state") == "removed-tombstone") == 2, "the record names each removed file");
                // put back, exactly
                var con2 = RecoverySessions.OpenSimulated(c, ImageKind.Files);
                var (b, bw) = SegaCleanup.PlanPutBack(con2, ctx, o.RecordFile);
                Check(b != null && b.Refusal == null && b.Changes.Count == 2 && b.Changes.All(x => x.Op == "put" && x.OldState == "absent") && b.Protective?.Session == con2.SessionId && b.Source?.Path == p.Protective.Path,
                      "put back: both files from the backup made before the removal; the console backed up again first (" + bw + ")");
                var bo = SegaCleanup.Execute(con2, b, b.Digest, b.Nonce, ctx);
                Check(bo.Ok && con2.FileStat("user", g + "/CLV-H-AAAAA_mdmini.png").state == Sha(pic) && con2.FileStat("user", f + "/CLV-S-00001_mdmini.png").state == Sha(fpic), "put back: the exact bytes, read back (" + bo.Problem + ")");
                var (b2, _) = SegaCleanup.PlanPutBack(RecoverySessions.OpenSimulated(c, ImageKind.Files), ctx, o.RecordFile);
                Check(b2 != null && b2.Refusal != null && b2.Changes.Count == 0, "put back again: nothing needs to be put back (" + b2?.Refusal + ")");
                // put back never replaces a different file, and never puts a picture where its game is gone
                fs.Delete(c, g + "/CLV-H-AAAAA_mdmini.png"); fs.Write(c, g + "/CLV-H-AAAAA_mdmini.png", Encoding.ASCII.GetBytes("a new picture"));
                fs.Delete(c, f + "/CLV-S-00001_mdmini.png"); fs.Delete(c, f + "/CLV-S-00001.desktop");
                var (b3, b3w) = SegaCleanup.PlanPutBack(RecoverySessions.OpenSimulated(c, ImageKind.Files), ctx, o.RecordFile);
                Check(b3 != null && b3.Changes.Count == 0 && b3.Kept.Any(k => k.Contains("a different file has that name")) && b3.Kept.Any(k => k.Contains("no longer on the console")), "a different file there now stays; a game no longer there gets nothing back (" + (b3w ?? string.Join(" | ", b3?.Kept ?? new List<string>())) + ")");
                // a record that is not a removal of these pictures is refused
                var other = Path.Combine(NewDir("sega-rec"), "x.json"); File.WriteAllText(other, "{\"kind\":\"compose\",\"plan\":{}}");
                var (b4, b4w) = SegaCleanup.PlanPutBack(RecoverySessions.OpenSimulated(c, ImageKind.Files), ctx, other);
                Check(b4 == null && b4w.Contains("not a removal"), "a record of something else: refused (" + b4w + ")");
                Check(PcState() == pcBefore, "another program's PC picture library and sync folder are unchanged");
                // Console Composer leaves these pictures out (rebuild from the backup made before the removal)
                var (cp, cw) = ComposerEngine.Plan(RecoverySessions.OpenSimulated(c, ImageKind.Files), ctx, "rebuild", new[] { new ComposeItem { SourceBackup = p.Protective.Path, SourcePath = f + "/CLV-S-00001_mdmini.png" }, new ComposeItem { SourceBackup = p.Protective.Path, SourcePath = save } });
                Check(cp != null && cp.Changes.All(x => !SegaPictures.LooksLike(Path.GetFileName(x.Path))) && cp.Skipped.Any(x => x.Contains(SegaCleanup.NotWritten)), "Console Composer never writes a picture for the Sega Mini menu (" + (cw ?? string.Join(" | ", cp.Skipped)) + ")");
                // a Super Famicom (JPN) console: refused before anything is read or backed up (a North American or European Super NES Classic
                // has its own clean-up run in the snes-data group)
                var sn = SnesConsole("modified-noorig", "JPN");
                var (sp_, sw) = SegaCleanup.PlanRemove(RecoverySessions.OpenSimulated(sn, ImageKind.Files), Ctx(SettingsIn(NewDir("sega-snes"), false), ImageKind.Files));
                Check(sp_ == null && sw.Contains("JPN") && Writes(sn).Count == 0, "a Super Famicom (JPN) console: refused before anything is read or backed up (" + sw + ")");
            }
            finally { SimulatedConsole.FileStore = null; }
        }

        static long RoundUp(long n) => (n + 4095) / 4096 * 4096;
    }
}
