// Classic Cart Studio console backup and restore engine. Copyright (C) 2026 Rance9. GPL-3.0-or-later.
// Back Up Console: reads every supported part of the console's built-in storage from the recovery image and writes ONE backup file.
// The order of what is written to the PC is what makes an interruption safe (the Windows program closed, crashing or ended at any point;
// the console is only ever read):
//   1. the operation record (state "reading") and a new, uniquely named <name>.zip.partial (never an existing file)
//   2. each member streamed into the partial file and hashed on the way, checked against the console's own count and MD5, the record
//      updated after each member
//   3. the data partition described (inventory), the convenience .ccs export, README, the repair map (its chunk map, then its early
//      descriptor filled in at the place reserved for it first in the file) and the manifest; the file closed and flushed (record: "checking")
//   4. the closed file verified from scratch (Verifier), compared with what was received (record: "checked", with the file's SHA-256)
//   5. the file renamed to <name>.zip (never over a file) (record: "published")
//   6. the second copy written as its own .zip.partial, flushed, read back in full and compared, renamed (record: "copying" then
//      "complete", or "complete-one-copy" with the reason)
// A file that stops at any step keeps a .partial name or a record that is not "complete"; the history shows it as Incomplete or
// Needs Verification, and no admission ever accepts a file without verifying it again (Admission, RestoreEngine).
using CcsBackup.Archive;
using CcsBackup.Device;
using CcsBackup.Ext4;
using CcsBackup.Library;
using RepairMap = CcsBackup.Repair.RepairMap;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace CcsBackup.Engine
{
    public sealed class EngineProgress { public string Phase, Text; public long Done, Total; }

    public sealed class EngineContext
    {
        public Settings Settings; public string Entry = "workbench"; public string ToolName = "CCS Console Workbench"; public string ToolVersion = EngineInfo.Version;
        public Action<EngineProgress> Progress = _ => { }; public Func<bool> Cancelled = () => false;
        /// <summary>called at each durable boundary; the worker sets it only for a simulated console whose faults.json asks for a crash
        /// there (the tests' deterministic termination points). Never set for a real console.</summary>
        public Action<string> Boundary;
        public string RecoveryImageId, RecoveryImageSha256;
        /// <summary>tests only (internal): wraps the file the engine writes ("primary" or "copy") to inject a disk-full or write error</summary>
        internal Func<string, Stream, Stream> WrapFile;
        internal Stream Wrap(string which, Stream s) => WrapFile == null ? s : WrapFile(which, s);
        /// <summary>tests only (internal): a backup without repair information, exactly as versions before it wrote them</summary>
        internal bool LegacyFormat;
        public void At(string b) => Boundary?.Invoke(b);
        public void Say(string phase, string text, long done = 0, long total = 0) => Progress?.Invoke(new EngineProgress { Phase = phase, Text = text, Done = done, Total = total });
    }

    public static class EngineInfo
    {
        /// <summary>the product version this engine was built as (the build's -p:Version, the repository's VERSION in release builds), as
        /// export records and the window's About show it; never a fixed number of its own</summary>
        public static string Version => typeof(EngineInfo).Assembly.GetName().Version.ToString(3);
        public static string Engine => "CcsBackup " + typeof(EngineInfo).Assembly.GetName().Version;
    }

    public sealed class BackupOutcome
    {
        public string State, Problem, Id, PrimaryPath, ArchivePath, PartialPath, CopyProblem, RecordFile;
        public bool TwoCopies, SameDisk, Simulated;
        public ConsoleIdentity Identity; public Classification Classification; public InventoryResult Inventory; public Verification Verification;
        public long Bytes;
        public bool Ok => State == "complete" || State == "complete-one-copy";
    }

    /// <summary>Console Rescue's protective capture only: boot stages known to be damaged (from the session's read-only diagnosis). A damaged
    /// stage that the console still reads (the PC and the console agree on its bytes) is kept as read and marked damaged; one that cannot be
    /// read at all is recorded as unreadable. Such a backup protects everything else, says what it could not preserve, and is never a recovery
    /// baseline. Every other part must read cleanly, as in any backup.</summary>
    public sealed class CaptureOptions { public HashSet<string> DamagedStages = new HashSet<string>(); }

    public static class CaptureEngine
    {
        public const long Reserve = 64L * 1000 * 1000;

        public static long EstimateBytes(IEnumerable<PartitionEntry> parts) => parts.Sum(p => p.Bytes) + Support.PhysicalBytes + 16L * 1024 * 1024 + Reserve;

        public static BackupOutcome Run(RecoveryConsole con, EngineContext ctx, CaptureOptions opt = null)
        {
            var damaged = new List<object>();
            var o = new BackupOutcome { Simulated = con.Simulated };
            var started = DateTime.Now;
            ctx.Say("identify", "Checking the console...");
            ConsoleIdentity id; List<PartitionEntry> parts;
            try { id = con.Identify(); parts = Support.CheckForBackup(id, ctx.RecoveryImageId); }
            catch (ConsoleRefusedException ex) { o.State = "refused"; o.Problem = ex.Message; return o; }
            o.Identity = id;
            var s = ctx.Settings; long need = EstimateBytes(parts);
            try { Directory.CreateDirectory(s.PrimaryFolder); }
            catch (Exception ex) { o.State = "refused"; o.Problem = "The backup folder could not be created (" + ex.Message + "). Choose another folder."; return o; }
            long free = Durable.FreeBytes(s.PrimaryFolder);
            if (free >= 0 && free < need) { o.State = "refused"; o.Problem = "There is not enough free space in the backup folder: about " + Units.Gb(need) + " is needed and " + Units.Gb(free) + " is free. Nothing was read."; return o; }
            bool second = !string.IsNullOrEmpty(s.ArchiveFolder);
            if (second)
            {
                try { Directory.CreateDirectory(s.ArchiveFolder); } catch (Exception ex) { o.State = "refused"; o.Problem = "The second-copy folder could not be created (" + ex.Message + "). Choose another folder, or turn the second copy off."; return o; }
                long f2 = Durable.FreeBytes(s.ArchiveFolder);
                bool sameVol = Durable.VolumeId(s.ArchiveFolder) == Durable.VolumeId(s.PrimaryFolder);
                if (f2 >= 0 && f2 < (sameVol ? need * 2 : need)) { o.State = "refused"; o.Problem = "There is not enough free space for the second copy: about " + Units.Gb(sameVol ? need * 2 : need) + " is needed and " + Units.Gb(f2) + " is free. Nothing was read."; return o; }
            }
            o.Id = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + "-" + Guid.NewGuid().ToString("N").Substring(0, 8);
            var stem = BackupFormat.FileStem(started, id.ShortTag, con.Simulated, id.Model);
            o.PartialPath = Unique(s.PrimaryFolder, stem, BackupFormat.Partial);
            var finalPath = o.PartialPath.Substring(0, o.PartialPath.Length - ".partial".Length);
            Record rec;
            try
            {
                rec = Record.Start(s.RecordsFolder, o.Id, "backup", new JObj { { "entry", ctx.Entry }, { "console_tag", id.ShortTag }, { "simulated", con.Simulated }, { "primary", new JObj { { "partial", o.PartialPath }, { "final", finalPath } } } });
            }
            catch (Exception ex) { o.State = "refused"; o.Problem = "The backup folder cannot be written (" + ex.Message + "). Choose another folder. Nothing was read."; return o; }
            o.RecordFile = rec.File;
            rec.State("reading"); ctx.At("record-started");
            var expected = new Dictionary<string, string>();
            var memberRows = new List<object>();
            byte[] active = null, slot68 = null;
            Classification cls = null; InventoryResult inv = null; JObj export = null; JObj rel = null;
            string activeSha = null, slotSha = null;
            try
            {
                using (var fs = Durable.CreateNew(o.PartialPath))
                {
                    var z = new ZipWriter(ctx.Wrap("primary", fs), started) { ChunkSize = ctx.LegacyFormat ? 0 : RepairMap.ChunkSize };
                    // the repair map's early descriptor: placed first now, filled when everything else is written (Repair/RepairMap.cs)
                    var desc = ctx.LegacyFormat ? null : z.Reserve(BackupFormat.DescriptorName, RepairMap.DescriptorReserve);
                    var order = new List<string> { "boot2" };
                    if (id.Slot68Valid) order.Add("boot2-68");
                    order.AddRange(new[] { "boot0", "boot1", "parttable" });
                    order.AddRange(parts.Select(p => p.Device).OrderBy(d => d == parts.FirstOrDefault(x => x.Label == "data" || x.Label == "rootfs_data")?.Device ? 1 : 0).ThenBy(d => d));   // the data storage last, found by its label
                    if (id.KernelKiB.ContainsKey("nand")) order.Add("nand");
                    order.Add("phy");
                    long totalBytes = parts.Sum(p => p.Bytes) + Support.PhysicalBytes + (id.KernelKiB.TryGetValue("nand", out var nk) ? nk * 1024 : 0) + Math.Max(0, id.ActiveSize) * 2;
                    long doneBytes = 0;
                    con.Card("reading");
                    foreach (var t in order)
                    {
                        if (ctx.Cancelled()) throw new OperationCanceledException();
                        bool tolerate = opt != null && (t == "boot0" || t == "boot1") && opt.DamagedStages.Contains(t);
                        if (tolerate)
                        {   // a damaged stage: read once into memory; kept only when the PC's and the console's checks of the same bytes agree
                            var ms = new MemoryStream(); var dr = con.Read(t, ms, -1, null, ctx.Cancelled);
                            bool consistent = dr.Bytes > 0 && dr.ConsoleBytes == dr.Bytes && dr.ConsoleMd5 == dr.Md5 && dr.TeeStatus == 0;
                            if (dr.Ok) tolerate = false;   // it reads cleanly now: stored as usual below (read again)
                            else if (!consistent) { damaged.Add(new JObj { { "component", t }, { "state", "unreadable" }, { "detail", dr.Problem } }); ctx.At("member-" + t); continue; }
                            else
                            {
                                var (dn, drole) = BackupFormat.MemberFor(t); var bytes = ms.ToArray();
                                var dres = z.Add(dn, false, st => st.Write(bytes, 0, bytes.Length), false, null, ctx.Cancelled);
                                expected[dn] = dres.sha256;
                                memberRows.Add(new JObj { { "path", dn }, { "role", drole }, { "target", t }, { "size", (long)bytes.Length }, { "sha256", dres.sha256 }, { "md5", dres.md5 }, { "console_md5", dr.ConsoleMd5 }, { "console_bytes", dr.ConsoleBytes }, { "source", Units.SourceText(t) }, { "state", "damaged-as-read" } });
                                damaged.Add(new JObj { { "component", t }, { "state", "invalid" }, { "detail", dr.Problem }, { "md5", dr.Md5 } });
                                rec.Set("members", memberRows.ToList()).Save(); ctx.At("member-" + t); continue;
                            }
                        }
                        var (name, role) = BackupFormat.MemberFor(t);
                        long exp = t == "boot2" ? id.ActiveSize : t == "boot2-68" ? id.Slot68Size : t == "phy" ? Support.PhysicalBytes : t == "nand" ? nk * 1024 : parts.Where(p => p.Device == t).Select(p => p.Bytes).DefaultIfEmpty(-1).First();
                        var label = Units.TargetLabel(t, parts);
                        ReadResult rr = null; MemoryStream keep = (t == "boot2" || t == "boot2-68") ? new MemoryStream() : null;
                        long before = doneBytes;
                        var res = z.Add(name, t == "parttable", st =>
                        {
                            Stream sink = keep == null ? st : new TeeStream(st, keep);
                            rr = con.Read(t, sink, exp, n => ctx.Say("read", "Reading " + label + "...", before + n, totalBytes), ctx.Cancelled);
                            if (!rr.Ok) throw new CaptureException(rr.Problem);
                        }, false, null, ctx.Cancelled);
                        if (res.sha256 != rr.Sha256) throw new CaptureException("the PC's two checks of " + t + " disagree");
                        doneBytes += rr.Bytes;
                        expected[name] = res.sha256;
                        memberRows.Add(new JObj { { "path", name }, { "role", role }, { "target", t }, { "size", rr.Bytes }, { "sha256", res.sha256 }, { "md5", res.md5 }, { "console_md5", rr.ConsoleMd5 }, { "console_bytes", rr.ConsoleBytes }, { "source", Units.SourceText(t) } });
                        if (t == "boot2") { active = keep.ToArray(); activeSha = rr.Sha256; }
                        if (t == "boot2-68") { slot68 = keep.ToArray(); slotSha = rr.Sha256; }
                        rec.Set("members", memberRows.ToList()).Save();
                        ctx.At("member-" + t);
                    }
                    cls = Support.Classify(id, activeSha, slotSha);
                    fs.Flush();
                    // the original kernel on its own, from the member it came from
                    if (cls.OriginalKnown)
                    {
                        var src = cls.OriginalSource == "active" ? active : slot68;
                        var res = z.Add(BackupFormat.OriginalKernelName, false, st => st.Write(src, 0, src.Length));
                        expected[BackupFormat.OriginalKernelName] = res.sha256;
                        memberRows.Add(new JObj { { "path", BackupFormat.OriginalKernelName }, { "role", "recovery" }, { "target", cls.OriginalSource == "active" ? "boot2" : "boot2-68" }, { "size", (long)src.Length }, { "sha256", res.sha256 }, { "md5", res.md5 }, { "source", "the genuine original Nintendo kernel: the same bytes as " + (cls.OriginalSource == "active" ? "restore/boot2-active.img" : "restore/boot2-slot68.img") + " (" + cls.OriginalRelease + ")" } });
                    }
                    fs.Flush();
                    ctx.Say("check", "Checking what was read...");
                    using (var rd = new FileStream(o.PartialPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    {
                        var pe = z.Entries.First(e => e.Name == "reference/nand-physical.bin");
                        var phy = new SubStream(rd, pe.DataOffset, pe.Size);
                        long ab = Verifier.FindInPhysical(phy, active.Take((int)Math.Min(Verifier.BootImageSize(active), active.Length)).ToArray(), 0x30);
                        long sb = slot68 == null ? -2 : Verifier.FindInPhysical(phy, slot68.Take((int)Math.Min(Verifier.BootImageSize(slot68), slot68.Length)).ToArray(), 0x68);
                        rel = new JObj { { "active_block", ab }, { "slot68_block", sb == -2 ? (object)null : sb }, { "confirmed", ab >= 0 && sb != -1 } };
                        var dataDev = parts.FirstOrDefault(p => p.Label == "data" || p.Label == "rootfs_data")?.Device;
                        if (dataDev != null)
                        {
                            var de = z.Entries.First(e => e.Name == "restore/" + dataDev + ".img");
                            ctx.Say("check", "Listing the files on the console's data storage...");
                            inv = Inventory.Build(new SubStream(rd, de.DataOffset, de.Size), dataDev, ctx.Cancelled);
                            if (inv.Problem == null)
                            {
                                export = TryExport(z, rd, de, expected, memberRows, ctx);
                            }
                            else export = new JObj { { "status", "unavailable" }, { "reason", "The data storage could not be read as a file system on the PC (" + inv.Problem + "); its exact image is still in the backup." } };
                        }
                    }
                    if (inv != null)
                    {
                        var ir = z.Add(BackupFormat.InventoryName, true, st => { var b = Json.Bytes(inv.Document); st.Write(b, 0, b.Length); });
                        expected[BackupFormat.InventoryName] = ir.sha256;
                        memberRows.Add(new JObj { { "path", BackupFormat.InventoryName }, { "role", "inventory" }, { "size", ir.info.Size }, { "sha256", ir.sha256 }, { "md5", ir.md5 }, { "source", "the data storage's file list, read on the PC from the captured image" } });
                    }
                    var completed = DateTime.UtcNow;
                    var man = Manifest(o, ctx, con, id, parts, cls, rel, inv, export, memberRows, started, completed, damaged);
                    var rm = z.Add(BackupFormat.ReadmeName, true, st => { var b = System.Text.Encoding.UTF8.GetBytes(BackupFormat.Readme(Json.Parse(Json.Write(man)), desc != null)); st.Write(b, 0, b.Length); });
                    memberRows.Add(new JObj { { "path", BackupFormat.ReadmeName }, { "role", "text" }, { "size", rm.info.Size }, { "sha256", rm.sha256 }, { "md5", rm.md5 } });
                    expected[BackupFormat.ReadmeName] = rm.sha256;
                    if (desc != null)
                    {
                        // the piece checksums near the end, then the early descriptor filled in (it lists the chunk map too); the manifest lists both
                        var mj = Json.Parse(Json.Write(man));
                        var hashes = memberRows.OfType<JObj>().ToDictionary(r => (string)r["path"], r => ((string)r["sha256"], (string)r["md5"]));
                        var cmap = RepairMap.Build("chunk-map", mj, z, z.Entries.Where(e => e != desc), hashes).Bytes();
                        var cr = z.Add(BackupFormat.ChunkMapName, true, st => st.Write(cmap, 0, cmap.Length));
                        expected[BackupFormat.ChunkMapName] = cr.sha256; hashes[BackupFormat.ChunkMapName] = (cr.sha256, cr.md5);
                        memberRows.Add(new JObj { { "path", BackupFormat.ChunkMapName }, { "role", "repair" }, { "size", cr.info.Size }, { "sha256", cr.sha256 }, { "md5", cr.md5 }, { "source", "a second copy of the checksums of every 1,048,576-byte piece of the members before it, so a copy damaged at its start can still be checked and repaired piece by piece" } });
                        var dmap = RepairMap.Build("descriptor", mj, z, z.Entries.Where(e => e != desc), hashes).Bytes(RepairMap.DescriptorReserve);
                        var dr = z.Fill(desc, dmap);
                        expected[BackupFormat.DescriptorName] = dr.sha256;
                        memberRows.Insert(0, new JObj { { "path", BackupFormat.DescriptorName }, { "role", "repair" }, { "size", (long)dmap.Length }, { "sha256", dr.sha256 }, { "md5", dr.md5 }, { "source", "an early copy of this member list with every member's place, header and piece checksums, first in the file so a copy damaged at its end can still be identified and repaired" } });
                    }
                    man.Set("members", memberRows.ToList());
                    z.Add(BackupFormat.ManifestName, true, st => { var b = Json.Bytes(man); st.Write(b, 0, b.Length); });
                    z.Finish();
                    Durable.Flush(fs);
                    o.Classification = cls; o.Inventory = inv;
                    rec.Set("state_text", cls.Text).Set("restore_ready", true).Set("recovery_baseline", cls.OriginalKnown && (bool)rel["confirmed"]);
                }
                rec.State("checking"); ctx.At("captured");
            }
            catch (OperationCanceledException) { rec.State("canceled", "Canceled while reading the console. Nothing was written to the console."); o.State = "canceled"; o.Problem = rec.Data["problem"] as string; return o; }
            catch (CaptureException ex) { rec.State("stopped", ex.Message); o.State = "stopped"; o.Problem = "The backup stopped: " + ex.Message + ". Nothing was written to the console; the unfinished file is marked Incomplete."; return o; }
            catch (Exception ex) when (ex is IOException || ex is CcsWorker.Shell.ShellException || ex is UnauthorizedAccessException)
            { rec.State("stopped", ex.Message); o.State = "stopped"; o.Problem = "The backup stopped: " + ex.Message + ". Nothing was written to the console; the unfinished file is marked Incomplete."; return o; }

            // 4. the closed file, verified from scratch
            ctx.Say("verify", "Verifying the backup file...");
            Verification v;
            try { v = Verifier.VerifyFile(o.PartialPath, expected, ctx.Cancelled, (d, t) => ctx.Say("verify", "Verifying the backup file...", d, t)); }
            catch (OperationCanceledException) { rec.State("canceled", "Canceled while the backup file was being checked; it is not verified."); o.State = "canceled"; o.Problem = rec.Data["problem"] as string; return o; }
            o.Verification = v;
            if (!v.Ok) { rec.State("damaged", string.Join(" ", v.Problems)); o.State = "damaged"; o.Problem = "The backup file did not pass its check: " + v.Problems[0]; return o; }
            rec.Set("primary", new JObj { { "partial", o.PartialPath }, { "final", finalPath }, { "sha256", v.ArchiveSha256 }, { "bytes", v.Bytes }, { "verified_utc", Record.Now() } })
               .Set("restore_ready", v.RestoreReady).Set("recovery_baseline", v.RecoveryBaseline);
            rec.State("checked"); ctx.At("checked");
            // 5. its final name
            try { Durable.Publish(o.PartialPath, finalPath); }
            catch (IOException ex) { rec.State("stopped", ex.Message); o.State = "stopped"; o.Problem = ex.Message; return o; }
            o.PrimaryPath = finalPath; o.Bytes = v.Bytes;
            var prim = (JObj)rec.Data["primary"]; prim.Set("mtime_utc", File.GetLastWriteTimeUtc(finalPath).ToString("o", CultureInfo.InvariantCulture));
            rec.State("published"); ctx.At("published");
            // 6. the second copy
            if (!second) { rec.Set("archive", null).State("complete-one-copy", "No second copy is set up."); o.State = "complete-one-copy"; o.CopyProblem = "No second copy is set up."; ctx.At("complete"); return Done(o, con); }
            var copyPartial = Unique(s.ArchiveFolder, Path.GetFileNameWithoutExtension(finalPath), BackupFormat.Partial);
            var copyFinal = copyPartial.Substring(0, copyPartial.Length - ".partial".Length);
            rec.Set("archive", new JObj { { "partial", copyPartial } }).State("copying");   // recorded before it exists: an unfinished copy always belongs to this backup
            try
            {
                ctx.Say("copy", "Making the second copy...");
                using (var src = new FileStream(finalPath, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20, FileOptions.SequentialScan))
                using (var dst = Durable.CreateNew(copyPartial))
                { Streams.Copy(src, new ProgressSink(ctx.Wrap("copy", dst), n => ctx.Say("copy", "Making the second copy...", n, v.Bytes), ctx.Cancelled), -1, ctx.Cancelled); Durable.Flush(dst); }
                ctx.At("copy-written");
                ctx.Say("copy", "Checking the second copy...");
                var h = Durable.HashFile(copyPartial, ctx.Cancelled);
                if (h.sha256 != v.ArchiveSha256 || h.bytes != v.Bytes) throw new IOException("the second copy does not match the first when read back");
                ctx.At("copy-checked");
                Durable.Publish(copyPartial, copyFinal);
                o.ArchivePath = copyFinal; o.TwoCopies = true; var drive = Repair.Drives.Compare(s.ArchiveFolder, s.PrimaryFolder, out _);   // the physical drives; the volume when they cannot be told
                o.SameDisk = drive == "same" || drive == "unknown" && Durable.VolumeId(s.ArchiveFolder) == Durable.VolumeId(s.PrimaryFolder);
                rec.Set("archive", new JObj { { "final", copyFinal }, { "sha256", h.sha256 }, { "bytes", h.bytes }, { "verified_utc", Record.Now() }, { "same_disk", o.SameDisk } });
                rec.State("complete"); o.State = "complete";
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is OperationCanceledException)
            {
                o.CopyProblem = ex is OperationCanceledException ? "The second copy was canceled." : "The second copy could not be made: " + ex.Message;
                rec.Set("archive", new JObj { { "partial", copyPartial }, { "problem", o.CopyProblem } }).State("complete-one-copy", o.CopyProblem);
                o.State = "complete-one-copy";
            }
            ctx.At("complete");
            return Done(o, con);
        }

        static BackupOutcome Done(BackupOutcome o, RecoveryConsole con) { con.Card("done"); return o; }

        static JObj TryExport(ZipWriter z, FileStream rd, ZipEntryInfo dataEntry, Dictionary<string, string> expected, List<object> rows, EngineContext ctx)
        {
            try
            {
                var fs = new Ext4Reader(new SubStream(rd, dataEntry.DataOffset, dataEntry.Size));
                var plan = CcsExport.Plan(fs);
                if (plan.Status != "included") return plan.Describe();
                ctx.Say("check", "Packaging the console's Classic Cart Studio HOME as a .ccs file...");
                var res = z.Add(BackupFormat.ExportName, false, st => CcsExport.Write(fs, plan, st), false, null, ctx.Cancelled);
                expected[BackupFormat.ExportName] = res.sha256;
                rows.Add(new JObj { { "path", BackupFormat.ExportName }, { "role", "export" }, { "size", res.info.Size }, { "sha256", res.sha256 }, { "md5", res.md5 }, { "source", "a convenience copy of the console's Classic Cart Studio HOME as an ordinary .ccs package; a restore never uses it" } });
                return plan.Describe().Set("path", BackupFormat.ExportName).Set("size", res.info.Size);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { return new JObj { { "status", "unavailable" }, { "reason", "The .ccs export could not be made (" + ex.Message + "); the backup itself is unaffected." } }; }
        }

        static JObj Manifest(BackupOutcome o, EngineContext ctx, RecoveryConsole con, ConsoleIdentity id, List<PartitionEntry> parts, Classification cls, JObj rel, InventoryResult inv, JObj export, List<object> rows, DateTime started, DateTime completed, List<object> damaged)
        {
            var notes = inv?.ExternalNotes ?? new List<string> { "The data storage could not be listed, so its settings for USB or SD storage are unknown." };
            bool baseline = cls.OriginalKnown && (bool)rel["confirmed"] && damaged.Count == 0;
            var reasons = new List<object>();
            if (!cls.OriginalKnown) reasons.Add("the original Nintendo kernel was not on the console");
            if (!(bool)rel["confirmed"]) reasons.Add("the startup software could not be found in the physical NAND image");
            if (damaged.Count > 0) reasons.Add("a startup stage was damaged when this backup was made");
            long kernelPhy = rows.OfType<JObj>().Where(r => (string)r["role"] == "restore" || (string)r["role"] == "recovery").Sum(r => (long)r["size"]);
            var manifest = new JObj
            {
                { "format", BackupFormat.Format }, { "schema", BackupFormat.Schema }, { "backup_id", o.Id }, { "simulated", con.Simulated },
                { "created_utc", started.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture) },
                { "completed_utc", completed.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture) },
                { "scope", new JObj { { "kind", "internal" }, { "label", "Internal Console Backup" }, { "text", BackupFormat.ScopeText }, { "external_storage", notes },
                    { "not_included", new[] { "USB or SD storage connected to the console (left untouched by backup and restore)", "the console's memory (RAM) and other volatile state", "NAND wear, flash-translation tables and free-space history" } } } },
                { "tool", new JObj { { "name", ctx.ToolName }, { "version", ctx.ToolVersion }, { "engine", EngineInfo.Engine }, { "entry", ctx.Entry }, { "recovery_image", new JObj { { "id", ctx.RecoveryImageId }, { "sha256", ctx.RecoveryImageSha256 } } }, { "transport", con.Kind } } },
                { "console", new JObj { { "model", (id.Model ?? ConsoleModels.Nes).Name }, { "physical_model", (id.Model ?? ConsoleModels.Nes).Id }, { "board", id.Board }, { "region", id.Region }, { "oem_version", id.OemVersion }, { "hwid", id.Hwid }, { "tag", id.ShortTag } } },
                { Compose.Capabilities.TargetKey, (id.Model ?? ConsoleModels.Nes).TargetJson() },   // the physical model, its software and storage layout, stated
                { "layout", new JObj { { "nandinfo", id.NandInfo }, { "partition_table", id.Get("parttable") }, { "partitions", parts.Select(p => (object)new JObj { { "device", p.Device }, { "label", p.Label }, { "bytes", p.Bytes } }).ToList() }, { "kernel_view_kib", id.Get("partitions") } } },
                { "state", new JObj { { "kind", cls.Kind }, { "text", cls.Text }, { "active_md5", id.ActiveMd5 }, { "active_size", id.ActiveSize }, { "active_hakchi_entries", (long)id.ActiveHakchiEntries },
                    { "slot68", new JObj { { "present", id.Slot68Valid }, { "md5", id.Slot68Md5 }, { "size", id.Slot68Valid ? id.Slot68Size : 0 } } },
                    { "original_kernel", cls.OriginalKnown }, { "original_source", cls.OriginalSource }, { "original_release", cls.OriginalRelease },
                    { "relationship_confirmed", (bool)rel["confirmed"] }, { "relationship", rel } } },
                { "data_partition", inv == null ? null : new JObj { { "summary", inv.Summary }, { "condition", inv.Condition }, { "hakchi", inv.HakchiPresent }, { "classic_cart_studio", inv.CcsPresent } } },
                { "export", export ?? new JObj { { "status", "none" }, { "reason", "No data storage was captured." } } },
                { "restore_ready", true }, { "recovery_baseline", baseline }, { "recovery_baseline_missing", reasons },
                { "duplication", new JObj { { "note", "reference/nand-physical.bin holds the same storage as the restore members (and older versions of blocks): it is evidence, kept separately as required, and never written to a console. recovery/original-kernel.img repeats one restore member so the original kernel stands on its own." }, { "restore_and_recovery_bytes", kernelPhy }, { "physical_reference_bytes", Support.PhysicalBytes } } },
                { "members", rows },
            };
            if (damaged.Count > 0) manifest.Add("damaged_components", damaged);
            return manifest;
        }

        public static string Unique(string folder, string stem, string ext)
        {
            for (int i = 1; i < 1000; i++)
            {
                var name = stem + (i == 1 ? "" : " (" + i + ")");
                var p = Path.Combine(folder, name + ext); var fin = Path.Combine(folder, name + BackupFormat.Extension);
                if (!File.Exists(p) && !File.Exists(fin)) return p;
            }
            throw new IOException("too many backups with the same name in " + folder);
        }

        sealed class TeeStream : Stream
        {
            readonly Stream a, b; public TeeStream(Stream a, Stream b) { this.a = a; this.b = b; }
            public override void Write(byte[] x, int o, int c) { a.Write(x, o, c); b.Write(x, o, c); }
            public override bool CanRead => false; public override bool CanSeek => false; public override bool CanWrite => true;
            public override long Length => throw new NotSupportedException(); public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
            public override void Flush() { a.Flush(); b.Flush(); }
            public override int Read(byte[] x, int o, int c) => throw new NotSupportedException();
            public override long Seek(long o, SeekOrigin s) => throw new NotSupportedException();
            public override void SetLength(long v) => throw new NotSupportedException();
        }

        sealed class ProgressSink : Stream
        {
            readonly Stream inner; readonly Action<long> p; readonly Func<bool> c; long n;
            public ProgressSink(Stream i, Action<long> p, Func<bool> c) { inner = i; this.p = p; this.c = c; }
            public override void Write(byte[] x, int o, int k) { if (c != null && c()) throw new OperationCanceledException(); inner.Write(x, o, k); n += k; p?.Invoke(n); }
            public override bool CanRead => false; public override bool CanSeek => false; public override bool CanWrite => true;
            public override long Length => n; public override long Position { get => n; set => throw new NotSupportedException(); }
            public override void Flush() => inner.Flush();
            public override int Read(byte[] x, int o, int k) => throw new NotSupportedException();
            public override long Seek(long o, SeekOrigin s) => throw new NotSupportedException();
            public override void SetLength(long v) => throw new NotSupportedException();
        }
    }

    public sealed class CaptureException : Exception { public CaptureException(string m) : base(m) { } }

    public static class Units
    {
        /// <summary>decimal units from bytes (1 GB = 1,000,000,000 bytes), as everywhere in Classic Cart Studio; under 1,000 bytes the bytes themselves (never "0 KB")</summary>
        /// <summary>a count in words: "1 file", "3 files", "1,873 files"</summary>
        public static string Count(long n, string one, string many = null) => n.ToString("N0", CultureInfo.InvariantCulture) + " " + (n == 1 ? one : many ?? one + "s");
        public static string Gb(long b) => b < 1000 ? Math.Max(0, b).ToString("N0", CultureInfo.InvariantCulture) + " bytes" : b >= 1000000000 ? (b / 1e9).ToString("0.0", CultureInfo.InvariantCulture) + " GB" : b >= 1000000 ? (b / 1e6).ToString("0", CultureInfo.InvariantCulture) + " MB" : (b / 1e3).ToString("0", CultureInfo.InvariantCulture) + " KB";
        public static string TargetLabel(string t, List<PartitionEntry> parts)
        {
            switch (t)
            {
                case "boot2": return "the startup software"; case "boot2-68": return "the spare startup slot"; case "boot0": case "boot1": return "the bootloader";
                case "parttable": return "the storage table"; case "nand": return "the start of the storage"; case "phy": return "the whole raw storage (reference copy)";
            }
            var p = parts.FirstOrDefault(x => x.Device == t);
            switch (p?.Label) { case "data": case "rootfs_data": return "the data storage (games, saves, settings)"; case "rootfs": return "the system storage"; case "boot": return "the boot storage"; case "private": return "the console's private storage"; case "UDISK": return "the user storage"; }
            return t;
        }
        public static string SourceText(string t)
        {
            switch (t)
            {
                case "boot2": return "sunxi-flash read_boot2 (the startup software in use)";
                case "boot2-68": return "sunxi-flash read_boot2 68 (hakchi's spare startup slot)";
                case "boot0": return "sunxi-flash read_boot0 (reference only)"; case "boot1": return "sunxi-flash read_boot1 (reference only)";
                case "parttable": return "sunxi-part (the storage table as printed; reference)";
                case "nand": return "a read of the logical device /dev/nand (reference only)";
                case "phy": return "sntool sunxi_flash phy_read 0 (the physical NAND, reference only: it also holds older block versions)";
            }
            return "a read of the logical partition /dev/" + t + " through the console's NAND driver, nothing mounted";
        }
    }
}
