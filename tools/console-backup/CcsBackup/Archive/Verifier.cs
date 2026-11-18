// Classic Cart Studio console backup and restore engine. Copyright (C) 2026 Rance9. GPL-3.0-or-later.
// Checks a closed backup file from scratch, the same way every time it is used (after it is made, when it is opened, before a restore and
// at every maintenance admission). Nothing is taken on trust from the file, its name, an earlier result or a record:
//  1. the strict ZIP structure (ZipReader) with the format's member names and per-member size limits;
//  2. the manifest's format and schema, and its member list equal to the file's members (none unlisted, none missing);
//  3. every member read in full: size, CRC-32, SHA-256 and MD5 against the manifest (and, right after a capture, against what the engine
//     itself computed while the bytes arrived: expected);
//  4. the console facts recomputed from the bytes: boot images are Android boot images whose size matches their header; the
//     partitions match the recorded layout; the original kernel member is a genuine stock kernel and equals the member it came from; the
//     startup software's place in the physical image is found again; restore-ready and recovery-baseline are decided here, never read;
//     and, in a backup that has repair information (repair/), every record of it against the file: each member's place, local header,
//     CRC-32, sizes and checksums, and the SHA-256 of every 1,048,576-byte piece recomputed (Repair/RepairMap.cs); a backup made before
//     repair information existed has none and is checked exactly as before;
//  5. the whole file's SHA-256, read again from the start.
// The report lists every problem; Ok is true only with none.
using CcsBackup.Device;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace CcsBackup.Archive
{
    public sealed class Verification
    {
        public readonly List<string> Problems = new List<string>();
        public bool Ok => Problems.Count == 0;
        public Dictionary<string, object> Manifest;
        public string ArchiveSha256; public long Bytes;
        public bool RestoreReady, RecoveryBaseline, Simulated, RelationshipConfirmed;
        public bool RepairMap;   // the backup has repair information and all of it matches the file
        public string Hwid, ConsoleTag, StateKind, OriginalSource;
        public List<string> RestoreTargets = new List<string>();
        /// <summary>startup stages a Console Rescue protective backup recorded as damaged ("invalid": kept as read) or "unreadable" (absent)</summary>
        public Dictionary<string, string> DamagedComponents = new Dictionary<string, string>();
        public Dictionary<string, (string sha256, string md5, long size)> Members = new Dictionary<string, (string, string, long)>();
        public void Fail(string p) { if (!Problems.Contains(p)) Problems.Add(p); }
    }

    public static class Verifier
    {
        public const long BootArea = 0x20000;   // one NAND block (64 pages of 2 KiB)

        public static Verification VerifyFile(string path, IDictionary<string, string> expected = null, Func<bool> cancelled = null, Action<long, long> progress = null)
        {
            var v = new Verification();
            if (!File.Exists(path)) { v.Fail("The backup file was not found."); return v; }
            try
            {
                using (var z = ZipReader.Open(path, BackupFormat.Allowed))
                {
                    long total = z.Entries.Sum(e => e.Size); long fileLen = new FileInfo(path).Length;
                    if (total > BackupFormat.MaxTotal) { v.Fail("The backup expands to more than a console backup can."); return v; }
                    foreach (var e in z.Entries) if (e.Size > BackupFormat.MaxSize(e.Name)) { v.Fail("Member " + e.Name + " is larger than allowed."); return v; }
                    var me = z.Find(BackupFormat.ManifestName);
                    if (me == null) { v.Fail("The backup has no manifest."); return v; }
                    if (z.Entries.OrderBy(e => e.HeaderOffset).Last() != me) v.Fail("The manifest is not the last member, so the file was not finished the way Classic Cart Studio finishes it.");
                    Dictionary<string, object> m;
                    using (var st = z.OpenMember(me)) using (var r = new StreamReader(st, Encoding.UTF8)) m = Json.Parse(r.ReadToEnd());
                    v.Manifest = m;
                    if (m.S("format") != BackupFormat.Format || m.S("schema") != BackupFormat.Schema) { v.Fail("This file is not a Classic Cart Studio console backup (format or schema)."); return v; }
                    v.Simulated = m.B("simulated");
                    var members = (m.A("members") ?? new List<object>()).OfType<Dictionary<string, object>>().ToList();
                    var listed = new Dictionary<string, Dictionary<string, object>>();
                    foreach (var mm in members) { var p = mm.S("path"); if (p == null || listed.ContainsKey(p)) { v.Fail("The manifest lists a member twice or without a name."); return v; } listed[p] = mm; }
                    foreach (var e in z.Entries) if (e != me && !listed.ContainsKey(e.Name)) v.Fail("Member " + e.Name + " is not in the manifest.");
                    foreach (var p in listed.Keys) if (z.Find(p) == null) v.Fail("The manifest lists " + p + ", which is missing from the file.");
                    if (!v.Ok) return v;
                    long done = 0;
                    byte[] active = null, slot68 = null, original = null;
                    bool mapped = z.Find(BackupFormat.DescriptorName) != null || z.Find(BackupFormat.ChunkMapName) != null;
                    var kept = new Dictionary<string, byte[]>(); var chunks = new Dictionary<string, List<string>>();
                    foreach (var e in z.Entries.Where(x => x != me).OrderBy(x => x.HeaderOffset))
                    {
                        var mm = listed[e.Name];
                        if (mm.L("size") != e.Size) { v.Fail("Member " + e.Name + " has a different size than the manifest says."); continue; }
                        var keep = e.Name == "restore/boot2-active.img" || e.Name == "restore/boot2-slot68.img" || e.Name == BackupFormat.OriginalKernelName || e.Name == BackupFormat.DescriptorName || e.Name == BackupFormat.ChunkMapName;
                        var mem = keep ? new MemoryStream() : null;
                        var pieces = mapped && e.Method == 0 && e.Name != BackupFormat.DescriptorName ? new ChunkHasher(Repair.RepairMap.ChunkSize, mem) : null;
                        var h = new HashingSink((Stream)pieces ?? mem, null, cancelled);
                        try
                        {
                            using (var st = z.OpenMember(e)) { var b = new byte[1 << 20]; int n; while ((n = st.Read(b, 0, b.Length)) > 0) { h.Write(b, 0, n); done += n; progress?.Invoke(done, total + fileLen); } }
                            if (mapped && e.Method != 0) { pieces = new ChunkHasher(Repair.RepairMap.ChunkSize); using (var raw = z.RawWindow(e)) Streams.Copy(raw, pieces, -1, cancelled); }
                        }
                        catch (IOException ex) { v.Fail("Member " + e.Name + " could not be read back: " + ex.Message); continue; }
                        catch (System.IO.InvalidDataException ex) { v.Fail("Member " + e.Name + " could not be read back: " + ex.Message); continue; }
                        h.Finish();
                        if (pieces != null) chunks[e.Name] = pieces.Finish();
                        v.Members[e.Name] = (h.Sha256, h.Md5, h.Count);
                        if (h.Sha256 != mm.S("sha256") || h.Md5 != mm.S("md5")) v.Fail("Member " + e.Name + " does not match its checksum: the file has changed or is damaged.");
                        if (expected != null && expected.TryGetValue(e.Name, out var x) && x != h.Sha256) v.Fail("Member " + e.Name + " differs from what was received from the console.");
                        if (keep) { var bytes = mem.ToArray(); if (e.Name.EndsWith("active.img")) active = bytes; else if (e.Name.EndsWith("slot68.img")) slot68 = bytes; else if (e.Name == BackupFormat.OriginalKernelName) original = bytes; else kept[e.Name] = bytes; }
                    }
                    if (expected != null) foreach (var k in expected.Keys) if (!v.Members.ContainsKey(k)) v.Fail("Member " + k + " that was received from the console is missing from the file.");
                    if (!v.Ok) return v;
                    if (mapped) { Repair.RepairMap.Check(v, m, z, kept, chunks); if (!v.Ok) return v; }
                    Facts(v, m, z, listed, active, slot68, original);
                }
                long before = v.Members.Values.Sum(m => m.size);
                var whole = CcsBackup.Library.Durable.HashFile(path, cancelled, n => progress?.Invoke(before + n, before + new FileInfo(path).Length));
                v.ArchiveSha256 = whole.sha256; v.Bytes = whole.bytes;
            }
            catch (ZipFormatException ex) { v.Fail("The file is not a valid console backup: " + ex.Message); }
            catch (FormatException ex) { v.Fail("The backup's manifest is not readable: " + ex.Message); }
            catch (IOException ex) { v.Fail("The backup file could not be read: " + ex.Message); }
            catch (UnauthorizedAccessException ex) { v.Fail("The backup file could not be opened: " + ex.Message); }
            return v;
        }

        /// <summary>Android boot image v0: the size the header describes (Shared.CalcKernelSize), or -1</summary>
        public static long BootImageSize(byte[] b)
        {
            if (b == null || b.Length < 2048 || Encoding.ASCII.GetString(b, 0, 8) != "ANDROID!") return -1;
            uint U(int o) => BitConverter.ToUInt32(b, o);
            ulong page = U(36); if (page == 0 || page > 65536) return -1;
            ulong pages = 1 + (U(8) + page - 1) / page + (U(16) + page - 1) / page + (U(24) + page - 1) / page + (U(40) + page - 1) / page;
            return (long)(pages * page);
        }

        static string Md5(byte[] b) { using (var h = MD5.Create()) return Hex.Of(h.ComputeHash(b)); }
        static string Sha(byte[] b) { using (var h = SHA256.Create()) return Hex.Of(h.ComputeHash(b)); }

        /// <summary>the 128 KiB block where the image starts in the physical image: the expected block first, then every block in the first
        /// 64 MiB (a unit with bad blocks may hold it further on); -1 when it is not there</summary>
        public static long FindInPhysical(Stream phy, byte[] image, long expectedBlock)
        {
            if (image == null || image.Length == 0) return -1;
            bool At(long blk)
            {
                long pos = blk * BootArea; if (pos + image.Length > phy.Length) return false;
                phy.Position = pos; var buf = new byte[image.Length]; int got = 0;
                while (got < buf.Length) { int r = phy.Read(buf, got, buf.Length - got); if (r <= 0) return false; got += r; }
                for (int i = 0; i < buf.Length; i++) if (buf[i] != image[i]) return false;
                return true;
            }
            if (At(expectedBlock)) return expectedBlock;
            var head = new byte[64];
            for (long blk = 0; blk * BootArea < Math.Min(phy.Length, 64L * 1024 * 1024); blk++)
            {
                phy.Position = blk * BootArea; if (phy.Read(head, 0, 64) < 64) break;
                bool same = true; for (int i = 0; i < 64; i++) if (head[i] != image[i]) { same = false; break; }
                if (same && blk != expectedBlock && At(blk)) return blk;
            }
            return -1;
        }

        static void Facts(Verification v, Dictionary<string, object> m, ZipReader z, Dictionary<string, Dictionary<string, object>> listed, byte[] active, byte[] slot68, byte[] original)
        {
            var con = m.O("console"); var st = m.O("state"); var lay = m.O("layout");
            v.Hwid = con?.S("hwid"); v.ConsoleTag = con?.S("tag");
            if (v.Hwid == null || ConsoleIdentity.HwidTag(v.Hwid) != v.ConsoleTag) v.Fail("The backup does not name its console consistently.");
            if (Compose.Capabilities.ModelForBoard(con?.S("board")) == null) v.Fail("The backup is not of a supported NES Classic.");
            // the identifiers the backup is for (SNES-GROUNDWORK.md): none recorded is an NES Classic backup; any other model, console
            // software or storage layout named in it is refused here, before anything in it is used
            var target = Compose.Capabilities.ResolveTarget(m, "console-backup");
            if (!target.Ok) v.Fail("The backup names a console, console software or storage layout this version does not support.");
            var reasons = new List<string>();
            // startup software
            if (active == null) { v.Fail("The console's startup software is missing from the backup."); return; }
            long asz = BootImageSize(active);
            if (asz < 0 || asz > active.Length) v.Fail("The captured startup software is not a valid boot image.");
            if (slot68 != null) { long ssz = BootImageSize(slot68); if (ssz < 0 || ssz > slot68.Length) v.Fail("The captured spare startup slot is not a valid boot image."); }
            string aMd5 = Md5(active), aSha = Sha(active);
            bool activeStock = KnownKernels.IsStock(aMd5) && KnownKernels.ShaAgrees(aMd5, aSha) && (st?.L("active_hakchi_entries", -1) ?? -1) == 0;
            string sMd5 = slot68 == null ? null : Md5(slot68); bool slotStock = slot68 != null && KnownKernels.IsStock(sMd5) && KnownKernels.ShaAgrees(sMd5, Sha(slot68));
            v.OriginalSource = activeStock ? "active" : slotStock ? "slot68" : null;
            v.StateKind = activeStock && slot68 == null ? "stock" : "modified";
            if (st?.S("kind") != v.StateKind) v.Fail("The backup's description of the console's software does not match its contents.");
            if (original != null)
            {
                var src = v.OriginalSource == "active" ? active : v.OriginalSource == "slot68" ? slot68 : null;
                if (src == null || !KnownKernels.IsStock(Md5(original)) || !original.SequenceEqual(src)) v.Fail("The standalone original kernel in the backup is not the genuine kernel it should be.");
            }
            else if (v.OriginalSource != null) v.Fail("The original kernel was on the console but is missing from the backup.");
            if (v.OriginalSource == null) reasons.Add("the original Nintendo kernel was not on the console");
            // layout and restore members
            var parts = (lay?.A("partitions") ?? new List<object>()).OfType<Dictionary<string, object>>().ToList();
            if (lay?.S("nandinfo") != Support.NandInfo || parts.Count < 3) v.Fail("The backup's storage layout is not one this version supports.");
            foreach (var p in parts)
            {
                var dev = p.S("device"); long bytes = p.L("bytes");
                var name = "restore/" + dev + ".img";
                if (!listed.ContainsKey(name)) { v.Fail("Storage part " + dev + " is missing from the backup."); continue; }
                if (listed[name].L("size") != bytes) v.Fail("Storage part " + dev + " has the wrong size.");
                v.RestoreTargets.Add(dev);
            }
            v.RestoreTargets.Add("boot2"); if (slot68 != null) v.RestoreTargets.Add("boot2-68");
            foreach (var d in (m.A("damaged_components") ?? new List<object>()).OfType<Dictionary<string, object>>())
            {
                var c = d.S("component"); var stt = d.S("state");
                if ((c != "boot0" && c != "boot1") || (stt != "invalid" && stt != "unreadable") || v.DamagedComponents.ContainsKey(c)) { v.Fail("The backup's list of damaged startup stages is not valid."); continue; }
                v.DamagedComponents[c] = stt;
            }
            if (v.DamagedComponents.Count > 0) reasons.Add("a startup stage was damaged when this backup was made");
            foreach (var r in new[] { "reference/nand-physical.bin", "reference/boot0.bin", "reference/boot1.bin", "reference/partition-table.txt" })
            {
                var comp = r == "reference/boot0.bin" ? "boot0" : r == "reference/boot1.bin" ? "boot1" : null;
                bool absentOk = comp != null && v.DamagedComponents.TryGetValue(comp, out var ds) && ds == "unreadable";
                if (listed.ContainsKey(r) == absentOk) v.Fail(absentOk ? "Reference member " + r + " is listed as unreadable but present." : "Reference member " + r + " is missing.");
            }
            if (!v.Ok) return;
            // the startup software where it belongs in the physical image (the relationship of the kernel and the snapshot)
            var pe = z.Find("reference/nand-physical.bin");
            if (pe.Size != Support.PhysicalBytes) v.Fail("The physical NAND image has the wrong size.");
            else
            {
                Stream phy = pe.Method == 0 ? z.StoredWindow(pe) : null;
                if (phy == null) v.Fail("The physical NAND image is not stored in place.");
                else
                {
                    long ab = FindInPhysical(phy, active.Take((int)Math.Max(0, Math.Min(asz, active.Length))).ToArray(), 0x30);
                    long sb = slot68 == null ? -2 : FindInPhysical(phy, slot68.Take((int)Math.Max(0, Math.Min(BootImageSize(slot68), slot68.Length))).ToArray(), 0x68);
                    v.RelationshipConfirmed = ab >= 0 && sb != -1;
                    if (st != null && st.B("relationship_confirmed") != v.RelationshipConfirmed) v.Fail("The backup's record of where the startup software lies in the physical image does not match its contents.");
                    if (!v.RelationshipConfirmed) reasons.Add("the startup software could not be found in the physical NAND image");
                }
            }
            v.RestoreReady = v.Ok;   // every restore member present, valid and matching the layout (a simulated backup is refused for a real console elsewhere)
            v.RecoveryBaseline = v.RestoreReady && reasons.Count == 0;
            if (m.B("restore_ready") != v.RestoreReady || m.B("recovery_baseline") != v.RecoveryBaseline) v.Fail("The backup's own summary (restore-ready, recovery baseline) does not match what its contents show.");
        }
    }
}
