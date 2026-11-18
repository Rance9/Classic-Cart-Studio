// Classic Cart Studio console backup and restore engine. Copyright (C) 2026 Rance9. GPL-3.0-or-later.
// Applying a HOME design on THIS console, and Revert Design, through the files image (ram/ccsfiles,
// scope "compose") under Console Composer's STRICT policy and its own boundary Gate.ComposerWrites (false in this version):
//   layout     on the data storage (the console's /var/lib): ccs/data/home-design/
//                d/<id>/design.json, d/<id>/img/<sha256>.png|.jpg   a compiled design (id = SHA-256 of its design.json), never changed
//                current       the design the HOME shows: a design id, or "none" (the theme defaults, chosen)
//                known-good    the design the HOME falls back to when current does not check out: a design id or "none"
//                journal.json  what the last apply or revert was doing: written before the commit and again after it
//              The HOME reads current -> known-good -> the theme defaults at every start (design.h design_resolve) and uses a design only
//              when every one of its files checks out. Saves, Game Settings and the HOME's other choices are other files: never touched.
//   plan       the compiled design checked again (every file against its SHA-256); a complete backup of the console as it is now, in this
//              session (strict policy); the console's design state, its installed HOME package and free space read from that backup; the
//              render check run with the native HOME on the console's own package and this design, and passed (its report bound); every
//              file change bound to the state it must find (a stat now); one-time confirmation. The design's files first, then the
//              journal ("commit"), then known-good, then current (one rename: the commit), then the journal ("committed"), then the files
//              of designs neither current nor known-good any more.
//   execute    the protective backup verified again under a hold; the record saved first; each change once (stage, compare, new name,
//              sync, rename, sync, read back); the first failure stops every later change. A stop before the commit leaves the HOME as
//              it was; a stop after it leaves the new design in place with its fallback named.
//   revert     appearance only: current := known-good ("previous") or "none" ("defaults"), with the journal; nothing else changes
using CcsBackup.Archive;
using CcsBackup.Compose;
using CcsBackup.Device;
using CcsBackup.Ext4;
using CcsBackup.Library;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace CcsBackup.Engine
{
    /// <summary>the console's HOME designs as one backup holds them</summary>
    public sealed class DesignConsoleState
    {
        public string Current, KnownGood;                  // a design id, "none", or null (no such file)
        public string CurrentRaw, KnownGoodRaw;            // the pointer files' SHA-256 ("absent" when there is none)
        public string JournalSha256 = "absent";
        public Dictionary<string, string> Files = new Dictionary<string, string>(StringComparer.Ordinal);   // path under the data storage -> SHA-256
        public HashSet<string> Designs = new HashSet<string>(StringComparer.Ordinal);
        public HashSet<string> Intact = new HashSet<string>(StringComparer.Ordinal);   // designs whose every listed file is there and unchanged
        public string PackManifestSha256;                  // the installed HOME package's manifest.json (ccs/pkg/current -> pkg/<copy>/home/)
        public string PackCopy;
        public long FreeBytes;
    }

    public sealed class DesignApplyPlan
    {
        public string Kind, Hwid, ConsoleTag, Session, Digest, Nonce, Refusal, DesignId, DesignName, RevertTo, RenderCheckSha256, PackManifestSha256;
        public string CurrentBefore, KnownGoodBefore, CurrentAfter, KnownGoodAfter;
        public DateTime ExpiresUtc; public bool Simulated; public long NewBytes, FreeBytes;
        public BoundArchive Protective; public JObj CompiledManifest;
        public List<PlannedFileChange> Changes = new List<PlannedFileChange>(); public List<string> Notes = new List<string>(), LeftOut = new List<string>();
        public RenderCheckResult Check;
        public JObj ToJson() => new JObj
        {
            { "kind", Kind }, { "console_tag", ConsoleTag }, { "design", DesignId }, { "design_name", DesignName }, { "revert_to", RevertTo }, { "compiled", CompiledManifest },
            { "render_check_sha256", RenderCheckSha256 }, { "home_package_manifest_sha256", PackManifestSha256 },
            { "before", new JObj { { "current", CurrentBefore }, { "known_good", KnownGoodBefore } } }, { "after", new JObj { { "current", CurrentAfter }, { "known_good", KnownGoodAfter } } },
            { "evidence", Protective == null ? new List<object>() : new List<object> { Protective.ToJson() } },
            { "changes", Changes.Select(c => (object)new JObj { { "op", c.Op }, { "path", c.Path }, { "new_sha256", c.NewSha256 }, { "bytes", c.Bytes }, { "old", c.OldState } }).ToList() },
            { "new_bytes", NewBytes }, { "free_bytes", FreeBytes }, { "notes", Notes }, { "left_out", LeftOut }, { "refusal", Refusal }, { "digest", Digest }, { "nonce", Nonce },
            { "expires_utc", ExpiresUtc.ToString("o", CultureInfo.InvariantCulture) },
        };
    }

    public static class DesignApplyEngine
    {
        public const string Root = "ccs/data/home-design", Current = Root + "/current", KnownGood = Root + "/known-good", JournalPath = Root + "/journal.json";
        public const string JournalSchema = "ccs-home-design-journal/1";
        public static string Folder(string id) => Root + "/d/" + id;
        static readonly Regex Id = new Regex("^[0-9a-f]{64}$", RegexOptions.CultureInvariant);
        static string Sha(byte[] b) { using (var h = SHA256.Create()) return Hex.Of(h.ComputeHash(b)); }

        /// <summary>the console's designs, pointers, installed HOME package and free space, as a verified backup holds them (reads only)</summary>
        public static DesignConsoleState ReadState(string backup)
        {
            var st = new DesignConsoleState();
            var m = History.Peek(backup);
            var dev = (m?.O("layout")?.A("partitions") ?? new List<object>()).OfType<Dictionary<string, object>>().FirstOrDefault(x => x.S("label") == "data" || x.S("label") == "rootfs_data")?.S("device");
            using (var z = ZipReader.Open(backup, BackupFormat.Allowed))
            {
                var e = dev == null ? null : z.Find("restore/" + dev + ".img");
                if (e == null || e.Method != 0) throw new InvalidDataException("the backup has no data storage");
                var fs = new Ext4Reader(z.StoredWindow(e));
                st.FreeBytes = fs.FreeBlocks * fs.BlockSize;
                byte[] Read(string p, long max) { var ino = fs.Lookup("/" + p); return ino != null && fs.IsRegular(ino.Value) && fs.FileSize(fs.Inode(ino.Value)) <= max ? fs.ReadAll(ino.Value, max) : null; }
                string Pointer(string p, out string raw) { var b = Read(p, 200); raw = b == null ? "absent" : Sha(b); if (b == null) return null; var t = Encoding.ASCII.GetString(b).Trim(); return t == "none" || Id.IsMatch(t) ? t : "unreadable"; }
                st.Current = Pointer(Current, out st.CurrentRaw); st.KnownGood = Pointer(KnownGood, out st.KnownGoodRaw);
                var jb = Read(JournalPath, 100000); if (jb != null) st.JournalSha256 = Sha(jb);
                foreach (var en in fs.List().Where(x => x.Type == 'f' && x.Path.StartsWith("/" + Root + "/d/", StringComparison.Ordinal)))
                {
                    var rel = en.Path.Substring(1); var b = Read(rel, FilesEngine.MaxFileBytes); if (b == null) continue; st.Files[rel] = Sha(b);
                    var id = rel.Substring((Root + "/d/").Length).Split('/')[0]; if (Id.IsMatch(id)) st.Designs.Add(id);
                }
                foreach (var id in st.Designs)
                {
                    if (!st.Files.TryGetValue(Folder(id) + "/design.json", out var js) || js != id) continue;
                    try
                    {
                        var doc = StrictJson.ParseObject(Read(Folder(id) + "/design.json", 2000000), new JsonLimits { MaxBytes = 2000000, MaxDepth = 16, MaxValues = 400000, MaxString = 4000 }, "design");
                        bool ok = (doc["files"] as List<object> ?? new List<object>()).OfType<JObj>().All(f => st.Files.TryGetValue(Folder(id) + "/" + (f["name"] as string), out var s) && s == f["sha256"] as string);
                        if (ok) st.Intact.Add(id);
                    }
                    catch (ComposeException) { }
                }
                var pc = Read("ccs/pkg/current", 200);
                if (pc != null) { st.PackCopy = Encoding.ASCII.GetString(pc).Trim(); var mb = Regex.IsMatch(st.PackCopy, "^[0-9a-f]{16}\\.[0-9a-f]{8}$") ? Read("ccs/pkg/" + st.PackCopy + "/home/manifest.json", 32000000) : null; if (mb != null) st.PackManifestSha256 = Sha(mb); }
            }
            return st;
        }

        /// <summary>the HOME package the console runs, as files on this PC: packDir (checked file by file against the console's own
        /// home/manifest.json) or, when null, copied out of the backup into outDir</summary>
        public static string HomePackage(string backup, DesignConsoleState st, string packDir, string outDir)
        {
            if (st.PackManifestSha256 == null) throw new InvalidDataException("Classic Cart Studio's HOME is not installed on this console");
            if (packDir != null)
            {
                var mp = Path.Combine(packDir, "manifest.json");
                if (!File.Exists(mp) || Sha(File.ReadAllBytes(mp)) != st.PackManifestSha256) throw new InvalidDataException("the HOME package on this computer is not the one installed on the console");
                foreach (var f in (Json.Parse(File.ReadAllText(mp, Encoding.UTF8)).A("files") ?? new List<object>()).OfType<Dictionary<string, object>>())
                {
                    var p = Path.Combine(packDir, (f.S("path") ?? "").Replace('/', Path.DirectorySeparatorChar));
                    if (!File.Exists(p) || new FileInfo(p).Length != f.L("size") || Sha(File.ReadAllBytes(p)) != f.S("sha256")) throw new InvalidDataException("the HOME package on this computer differs from its own list (" + f.S("path") + ")");
                }
                return packDir;
            }
            var m = History.Peek(backup);
            var dev = (m?.O("layout")?.A("partitions") ?? new List<object>()).OfType<Dictionary<string, object>>().First(x => x.S("label") == "data" || x.S("label") == "rootfs_data").S("device");
            using (var z = ZipReader.Open(backup, BackupFormat.Allowed))
            {
                var fs = new Ext4Reader(z.StoredWindow(z.Find("restore/" + dev + ".img")));
                var home = "/ccs/pkg/" + st.PackCopy + "/home/";
                foreach (var en in fs.List().Where(x => x.Type == 'f' && x.Path.StartsWith(home, StringComparison.Ordinal)))
                {
                    var rel = en.Path.Substring(home.Length); if (!FilesEngine.PathOk(rel)) continue;
                    var dst = Path.Combine(outDir, rel.Replace('/', Path.DirectorySeparatorChar)); Directory.CreateDirectory(Path.GetDirectoryName(dst));
                    using (var o = new FileStream(dst, FileMode.CreateNew, FileAccess.Write)) fs.ReadFile(fs.Lookup(en.Path).Value, o, 64L * 1000 * 1000);
                }
            }
            return HomePackage(backup, st, outDir, null);
        }

        static (DesignApplyPlan, string) Begin(RecoveryConsole con, EngineContext ctx, string kind, out DesignConsoleState st, out string backup)
        {
            st = null; backup = null;
            if (con.Profile != SessionProfile.Files) return (null, "The console is not in a file session.");
            var id = con.Identify();
            var p = new DesignApplyPlan { Kind = kind, Hwid = id.Hwid, ConsoleTag = id.ShortTag, Session = con.SessionId, Simulated = con.Simulated };
            ctx.Say("protect", "Backing up the console as it is now, before anything changes...");
            var prot = CaptureEngine.Run(con, ctx);
            if (!prot.Ok || prot.Verification == null || !prot.Verification.Ok || !ConsoleIdentity.SameConsole(prot.Verification.Hwid, id.Hwid) || !prot.Verification.RestoreReady)
                return (null, "The backup of the console as it is now did not complete (" + (prot.Problem ?? prot.State) + "), so nothing was written.");
            p.Protective = BoundArchive.From("backup made just now, before changing the HOME design", prot.PrimaryPath, prot.Verification, con.SessionId);
            backup = prot.PrimaryPath;
            try { st = ReadState(backup); }
            catch (Exception ex) when (ex is IOException || ex is InvalidDataException || ex is Ext4Exception) { return (null, "The console's HOME designs could not be read from the backup made just now (" + ex.Message + "), so nothing was written."); }
            if (st.Current == "unreadable" || st.KnownGood == "unreadable") p.Notes.Add("A design pointer on the console was not readable; it is written new.");
            p.CurrentBefore = st.Current ?? "none"; p.KnownGoodBefore = st.KnownGood ?? "none"; p.FreeBytes = st.FreeBytes;
            return (p, null);
        }

        static void Pointer(RecoveryConsole con, DesignApplyPlan p, string path, string value)
        {
            var b = Encoding.ASCII.GetBytes(value + "\n"); var (state, n) = con.FileStat("compose", path);
            if (state == Sha(b)) return;
            if (state == "folder" || state == "link" || state == "special") throw new InvalidDataException(path + " is a " + state + " on the console");
            p.Changes.Add(new PlannedFileChange { Op = "put", Path = path, Data = b, Bytes = b.Length, NewSha256 = Sha(b), OldState = state, OldBytes = n });
        }
        static void Journal(DesignApplyPlan p, string step, string old)
        {
            var j = new JObj { { "schema", JournalSchema }, { "operation", p.Kind == "design-revert" ? "revert" : "apply" }, { "step", step }, { "design", p.DesignId }, { "revert_to", p.RevertTo },
                               { "before", new JObj { { "current", p.CurrentBefore }, { "known_good", p.KnownGoodBefore } } }, { "after", new JObj { { "current", p.CurrentAfter }, { "known_good", p.KnownGoodAfter } } },
                               { "nonce", p.Nonce } };
            var b = Json.Bytes(j); p.Changes.Add(new PlannedFileChange { Op = "put", Path = JournalPath, Data = b, Bytes = b.Length, NewSha256 = Sha(b), OldState = old });
        }
        static void Finish(DesignApplyPlan p) { p.ExpiresUtc = DateTime.UtcNow + RestoreEngine.ConfirmWindow; p.Digest = DigestOf(p); }

        /// <summary>why the console's HOME package cannot show this compiled design (null: it can). A design that moves the HOME's controls
        /// (compiled Schema2) needs a HOME package whose pack.json lists that schema under "design_schemas" (written by the package builder of
        /// this version): an earlier CCS HOME would set the design aside at every start.</summary>
        public static string PackageRefuses(string packDir, CompiledDesign design)
        {
            if (design.SchemaName == CompiledDesign.Schema) return null;
            Dictionary<string, object> pk; try { pk = Json.Parse(File.ReadAllText(Path.Combine(packDir, "pack.json"), Encoding.UTF8)); } catch (Exception ex) when (ex is IOException || ex is FormatException || ex is ArgumentException || ex is InvalidOperationException) { pk = null; }
            var schemas = (pk?.A("design_schemas") ?? new List<object>()).OfType<string>();
            return schemas.Contains(design.SchemaName) ? null : "The CCS HOME on this console is an earlier version, which cannot move the HOME's controls: it would not show this design. Update CCS HOME on the console first, or put the controls back in the theme's places in HOME Designer.";
        }

        /// <summary>applying one compiled design. renderer: the native HOME (HomeRenderers.Find); packDir: the console's HOME package on this
        /// PC (checked against the console's own), or null to take it from the backup made in this plan</summary>
        public static (DesignApplyPlan plan, string why) PlanApply(RecoveryConsole con, EngineContext ctx, CompiledDesign design, IHomeRenderer renderer, string packDir = null)
        {
            if (design == null) return (null, "No design was chosen.");
            if (renderer == null) return (null, "The console HOME renderer is not available, so the design cannot be checked before it is written. Nothing was changed.");
            foreach (var f in design.Manifest) if (!design.Files.TryGetValue(f.Name, out var fb) || Sha(fb) != f.Sha256 || fb.LongLength != f.Bytes) return (null, "The compiled design changed after it was made (" + f.Name + "). Compile it again.");
            if (Sha(design.DesignJson) != design.Id) return (null, "The compiled design's id does not match its settings file.");
            var (p, why) = Begin(con, ctx, "design-apply", out var st, out var backup); if (p == null) return (null, why);
            p.DesignId = design.Id; p.DesignName = design.Name; p.CompiledManifest = design.ManifestJson(); p.LeftOut.AddRange(design.LeftOut); p.Notes.AddRange(design.NotShown.Select(x => "Not shown on the console: " + x + "."));
            // the render check: this design with the console's own HOME package, on the native renderer, before anything is admitted
            var work = Path.Combine(Path.GetTempPath(), "ccs-design-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            try
            {
                Directory.CreateDirectory(work);
                string pack; try { pack = HomePackage(backup, st, packDir, packDir == null ? Path.Combine(work, "home") : null); }
                catch (Exception ex) when (ex is IOException || ex is InvalidDataException || ex is Ext4Exception) { return (null, ex.Message.Substring(0, 1).ToUpperInvariant() + ex.Message.Substring(1) + ", so the design cannot be checked against it. Nothing was changed."); }
                var unsupported = PackageRefuses(pack, design); if (unsupported != null) return (null, unsupported + " Nothing was changed.");
                var dd = Path.Combine(work, "design"); design.WriteTo(dd);
                ctx.Say("check", "Drawing every HOME screen with the design, as the console will...");
                var check = renderer.Check(pack, dd, new RenderCheckOptions { WorkDir = Path.Combine(work, "check"), Frames = false });
                p.Check = check; p.RenderCheckSha256 = check.ReportSha256; p.PackManifestSha256 = st.PackManifestSha256;
                if (!check.Ok || check.DesignId != design.Id) return (null, "The design did not pass the check on the console's own HOME, so nothing was written: " + string.Join("; ", check.Problems.Take(3)) + (check.Problems.Count > 3 ? " (and " + (check.Problems.Count - 3) + " more)" : "") + ".");
            }
            finally { try { Directory.Delete(work, true); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
            // the changes, each bound to what is on the console now
            var known = st.Current != null && st.Current != "none" && st.Current != design.Id && st.Intact.Contains(st.Current) ? st.Current : st.KnownGood ?? "none";
            if (known != "none" && !st.Intact.Contains(known)) known = "none";
            p.CurrentAfter = design.Id; p.KnownGoodAfter = known; p.Nonce = Hex.Of(Guid.NewGuid().ToByteArray());
            try
            {
                foreach (var f in design.Manifest)
                {
                    var path = Folder(design.Id) + "/" + f.Name; var (state, n) = con.FileStat("compose", path);
                    if (state == f.Sha256) continue;
                    if (state == "folder" || state == "link" || state == "special") return (null, path + " is a " + state + " on the console. Nothing was changed.");
                    var b = design.Files[f.Name]; p.Changes.Add(new PlannedFileChange { Op = "put", Path = path, Data = b, Bytes = b.LongLength, NewSha256 = f.Sha256, OldState = state, OldBytes = n }); p.NewBytes += b.LongLength;
                }
                var (js, _) = con.FileStat("compose", JournalPath); Journal(p, "commit", js);
                Pointer(con, p, KnownGood, known); Pointer(con, p, Current, design.Id);
                Journal(p, "committed", p.Changes.Last(c => c.Path == JournalPath).NewSha256);
                foreach (var kv in st.Files.Where(kv => { var id = kv.Key.Substring((Root + "/d/").Length).Split('/')[0]; return id != design.Id && id != known; }).OrderBy(kv => kv.Key, StringComparer.Ordinal))
                {
                    var (now, n) = con.FileStat("compose", kv.Key); if (now != kv.Value) continue;   // changed since the backup: kept
                    p.Changes.Add(new PlannedFileChange { Op = "del", Path = kv.Key, NewSha256 = null, OldState = now, OldBytes = n });
                }
            }
            catch (InvalidDataException ex) { return (null, ex.Message + ". Nothing was changed."); }
            if (st.FreeBytes - p.NewBytes < DesignProfile.StorageReserve)
                return (null, "The console's data storage has " + CcsBackup.Compose.Units.Size(st.FreeBytes) + " free; the design needs " + CcsBackup.Compose.Units.Size(p.NewBytes) + " and " + CcsBackup.Compose.Units.Size(DesignProfile.StorageReserve) + " must stay free for saves and settings. Nothing was changed.");
            p.Notes.Add("Only the HOME's appearance changes: saves, Game Settings and the HOME's other choices are not touched.");
            if (known != "none") p.Notes.Add("If the new design cannot be shown at a start, the console shows the design it shows now."); else p.Notes.Add("If the new design cannot be shown at a start, the console shows the theme as it comes.");
            Finish(p); return (p, null);
        }

        /// <summary>Revert Design: to = "previous" (the known-good design) or "defaults" (the themes as they come); appearance only</summary>
        public static (DesignApplyPlan plan, string why) PlanRevert(RecoveryConsole con, EngineContext ctx, string to)
        {
            if (to != "previous" && to != "defaults") return (null, "Unknown choice.");
            var (p, why) = Begin(con, ctx, "design-revert", out var st, out _); if (p == null) return (null, why);
            p.RevertTo = to;
            var target = to == "defaults" ? "none" : st.KnownGood;
            if (to == "previous" && (target == null || target == "none")) return (null, "There is no earlier design on this console to go back to. Choose the themes as they come instead.");
            if (to == "previous" && !st.Intact.Contains(target)) return (null, "The earlier design on this console is not complete any more, so it cannot be shown. Choose the themes as they come instead.");
            if ((st.Current ?? "none") == target) { p.Refusal = "The console already shows " + (target == "none" ? "the themes as they come." : "that design."); p.Nonce = Hex.Of(Guid.NewGuid().ToByteArray()); Finish(p); return (p, null); }
            p.DesignId = target; p.CurrentAfter = target; p.KnownGoodAfter = st.KnownGood ?? "none"; p.Nonce = Hex.Of(Guid.NewGuid().ToByteArray());
            try
            {
                var (js, _) = con.FileStat("compose", JournalPath); Journal(p, "commit", js);
                Pointer(con, p, Current, target);
                Journal(p, "committed", p.Changes.Last(c => c.Path == JournalPath).NewSha256);
            }
            catch (InvalidDataException ex) { return (null, ex.Message + ". Nothing was changed."); }
            p.Notes.Add("Only the HOME's appearance changes: saves, Game Settings and the HOME's other choices are not touched.");
            Finish(p); return (p, null);
        }

        static string DigestOf(DesignApplyPlan p)
        {
            var sb = new StringBuilder("ccs-home-design-plan/1\n").Append(p.Kind).Append('\n').Append(ConsoleIdentity.NormalizeHwid(p.Hwid)).Append('\n').Append(p.Session).Append('\n')
                .Append(p.Protective?.DigestLine()).Append('\n').Append(p.DesignId).Append('\n').Append(p.RenderCheckSha256).Append('\n').Append(p.PackManifestSha256).Append('\n')
                .Append(p.CurrentBefore).Append('>').Append(p.CurrentAfter).Append(' ').Append(p.KnownGoodBefore).Append('>').Append(p.KnownGoodAfter).Append('\n');
            foreach (var c in p.Changes) sb.Append(c.Op).Append(' ').Append(c.Path).Append(' ').Append(c.NewSha256 ?? "-").Append(' ').Append(c.Bytes.ToString(CultureInfo.InvariantCulture)).Append(' ').Append(c.OldState).Append('\n');
            sb.Append(p.Nonce);
            using (var h = SHA256.Create()) return Hex.Of(h.ComputeHash(Encoding.UTF8.GetBytes(sb.ToString())));
        }

        public static FileOutcome Execute(RecoveryConsole con, DesignApplyPlan plan, string digest, string nonce, EngineContext ctx)
        {
            var o = new FileOutcome();
            FileOutcome Refuse(string why) { o.State = "refused"; o.Problem = why; return o; }
            if (plan == null || plan.Refusal != null) return Refuse(plan?.Refusal ?? "No plan.");
            if (digest != plan.Digest || nonce != plan.Nonce || plan.Nonce == null || DigestOf(plan) != plan.Digest) return Refuse("The confirmation does not match the reviewed change.");
            if (DateTime.UtcNow > plan.ExpiresUtc) return Refuse("The confirmation is more than 10 minutes old. Review the change again.");
            if (con.SessionId != plan.Session || plan.Protective?.Session != plan.Session) return Refuse("The console was connected again after the review. Review the change again. Nothing was changed.");
            if (!ConsoleIdentity.SameConsole(con.Identify().Hwid, plan.Hwid)) return Refuse("A different console is connected now. Nothing was changed.");
            using (var hold = Evidence.Hold.Open(plan.Protective.Path))
            {
                if (hold.Problem != null) return Refuse("The backup made for this change cannot be used (" + hold.Problem + "). Nothing was changed.");
                var pw = Evidence.Recheck(plan.Protective, plan.Hwid, con.Simulated, con.SessionId, ctx.Cancelled); if (pw != null) return Refuse(pw + " Nothing was changed.");
                Record rec;
                try { rec = Record.Start(ctx.Settings.RecordsFolder, Guid.NewGuid().ToString("N").Substring(0, 12), plan.Kind, new JObj { { "plan", plan.ToJson() }, { "console_tag", plan.ConsoleTag }, { "simulated", con.Simulated } }); }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { return Refuse("The change's record could not be saved (" + ex.Message + "), so nothing was written."); }
                plan.Nonce = null; o.RecordFile = rec.File; var log = new List<object>(); bool committed = false;
                var auth = new WriteAuthority(plan.Digest, plan.Hwid, plan.Changes.Select(c => c.Op + ":" + c.Path).Distinct().ToArray(), () => con.Simulated || Gate.ComposerWrites, Gate.ComposerText);
                rec.State("writing"); con.Card("writing");
                foreach (var c in plan.Changes)
                {
                    if (ctx.Cancelled()) { rec.State("stopped", "canceled between files"); o.State = "stopped"; o.Problem = "Canceled. " + (committed ? "The new HOME appearance is in place." : "The HOME looks as it did."); return o; }
                    ctx.Say("design", (c.Op == "put" ? "Writing " : "Removing ") + c.Path + "...");
                    WriteResult r;
                    try { r = con.FileStep(c.Op, "compose", c.Path, c.NewSha256, c.OldState, c.Op == "put" ? c.Data : null, auth); }
                    catch (GateClosedException ex) { rec.State("refused", ex.Message); o.State = "refused"; o.Problem = ex.Message; return o; }
                    log.Add(new JObj { { "op", c.Op }, { "path", c.Path }, { "state", r.Ok ? (c.Op == "del" ? "removed" : "written-and-read-back") : "failed" }, { "detail", r.Line ?? r.Problem }, { "at", Record.Now() } }); rec.Set("changes", log).Save();
                    if (!r.Ok)
                    {
                        rec.State("stopped", r.Problem); con.Card("stopped"); o.State = "stopped";
                        o.Problem = "Stopped at " + c.Path + " (" + r.Problem + "). Nothing more was changed and nothing was retried. " + (committed ? "The new HOME appearance is in place; its fallback is " + (plan.KnownGoodAfter == "none" ? "the themes as they come." : "the design shown before.") : "The HOME looks as it did.") + " The backup made just now holds the console as it was.";
                        return o;
                    }
                    if (c.Path == Current) committed = true;
                    o.Done.Add(c.Path);
                }
                rec.State("changed"); con.Card("done"); o.State = "changed"; return o;
            }
        }
    }
}
