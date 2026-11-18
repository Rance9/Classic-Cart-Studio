// Classic Cart Studio console backup engine: PAR2 parity for a finished backup, and the one-file transport copy. Copyright (C) 2026 Rance9.
// GPL-3.0-or-later.
//   Protect    parity for ONE complete backup that passes its full check: "<backup>.par2" (the index) and its volume files beside it
//              (Par2Native: the single-file host's libpar2, or ccs-par2.dll in Classic Cart Studio for Windows), then checked at once, and
//              its parameters recorded in "<backup>.parity.json" (ccs-backup-parity/1). The parity protects the backup file only; nothing
//              protects itself. A damaged or unfinished backup is never given parity.
//   Check      the backup against its parity (reads only).
//   Rebuild    a damaged backup rebuilt from its parity into a NEW file "<name> (repaired with parity <time>).zip", then given the full
//              check; the damaged file and the parity are only read. Recovery is only as good as the intact parity: a backup damaged
//              beyond its recovery blocks cannot be rebuilt, and nothing is written then.
//   Transport  one file to move or store a protected backup: an outer ZIP "<name> (one file with parity).zip" holding backup/<name>.zip,
//              parity/<its .par2 files> and transport.json (each member's SHA-256 and size), all stored. The outer file is not itself
//              protected by that parity. Unpack writes the backup and its parity into a chosen folder (new names, never over a file) and
//              compares each part with transport.json. A part damaged on the way is still unpacked: the parity is there to rebuild the backup
//              (into a new file, checked in full); a damaged parity file only lowers what can be rebuilt. Nothing is called a backup unless it
//              passes its own full check.
using CcsBackup.Archive;
using CcsBackup.Library;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;

namespace CcsBackup.Repair
{
    public sealed class ParityResult
    {
        public bool Ok; public string Text, OutputPath, RecordPath; public Par2Report Report; public Verification OutputCheck;
    }

    public static class Parity
    {
        public const string RecordSchema = "ccs-backup-parity/1", TransportSchema = "ccs-backup-transport/1", TransportDescriptor = "transport.json";
        public static string IndexOf(string backup) => backup + ".par2";
        public static string RecordOf(string backup) => backup + ".parity.json";
        /// <summary>the backup's parity files as they are on disk: the index and its volumes (name.volNNN+MMM.par2)</summary>
        public static List<string> FilesOf(string backup)
        {
            var dir = Path.GetDirectoryName(backup); var name = Path.GetFileName(backup);
            var l = new List<string>(); if (File.Exists(IndexOf(backup))) l.Add(IndexOf(backup));
            if (Directory.Exists(dir)) l.AddRange(Directory.GetFiles(dir, name + ".vol*.par2").OrderBy(x => x, StringComparer.OrdinalIgnoreCase));
            return l;
        }
        static string Sha(string path) { using (var f = File.OpenRead(path)) using (var h = SHA256.Create()) return Hex.Of(h.ComputeHash(f)); }
        static Action<Par2Stage, int> Stages(Action<string, long, long> p, string what) => (st, pm) => p?.Invoke(what + " (" + st + ")", pm, 1000);

        public static ParityResult Protect(string backup, int percent, Action<string, long, long> progress = null, CancellationToken cancel = default)
        {
            var r = new ParityResult();
            if (!Par2Native.Available) { r.Text = Par2Native.UnavailableReason; return r; }
            if (FilesOf(backup).Count > 0) { r.Text = "This backup already has recovery data; check it instead (nothing is replaced)."; return r; }
            var v = Verifier.VerifyFile(backup, null, () => cancel.IsCancellationRequested, (d, t) => progress?.Invoke("Checking the backup", d, t));
            if (!v.Ok) { r.Text = "Recovery data is made only for a backup that passes its full check (" + v.Problems.FirstOrDefault() + ")."; return r; }
            var c = Par2Native.Create(backup, IndexOf(backup), percent, 1, 0, Stages(progress, "Making recovery data"), cancel); r.Report = c;
            if (!c.Ok) { r.Text = c.Message; return r; }
            var k = Par2Native.Verify(IndexOf(backup), backup, 0, Stages(progress, "Checking the recovery data"), cancel);
            var rec = new JObj
            {
                { "schema", RecordSchema }, { "backup", Path.GetFileName(backup) }, { "backup_sha256", v.ArchiveSha256 }, { "backup_bytes", v.Bytes },
                { "percent", (long)percent }, { "block_size", (long)c.BlockSize }, { "source_blocks", (long)c.SourceBlocks }, { "recovery_blocks", (long)c.RecoveryBlocks },
                { "recovery_files", (long)c.RecoveryFiles }, { "parity_bytes", c.ParityBytes }, { "library", Par2Native.LibraryVersion }, { "host", Par2Native.HostVersion + " " + Par2Native.HostArchitecture },
                { "files", FilesOf(backup).Select(f => (object)new JObj { { "name", Path.GetFileName(f) }, { "bytes", new FileInfo(f).Length }, { "sha256", Sha(f) } }).ToList() },
                { "created_utc", DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture) }, { "check", k.Status.ToString() }, { "check_text", k.Message },
            };
            Durable.WriteAtomic(RecordOf(backup), Json.Bytes(rec)); r.RecordPath = RecordOf(backup);
            r.Ok = k.Ok; r.Report = c; r.Text = c.Message + " " + (k.Ok ? "The recovery data was checked against the backup: it matches." : "The recovery data did not pass its check: " + k.Message);
            return r;
        }

        public static ParityResult Check(string backup, Action<string, long, long> progress = null, CancellationToken cancel = default)
        {
            var r = new ParityResult();
            if (!Par2Native.Available) { r.Text = Par2Native.UnavailableReason; return r; }
            if (!File.Exists(IndexOf(backup))) { r.Text = "This backup has no recovery data."; return r; }
            var k = Par2Native.Verify(IndexOf(backup), backup, 0, Stages(progress, "Checking against the recovery data"), cancel);
            r.Report = k; r.Ok = k.Ok; r.Text = k.Message; return r;
        }

        public static ParityResult Rebuild(string backup, Action<string, long, long> progress = null, CancellationToken cancel = default)
        {
            var r = new ParityResult();
            if (!Par2Native.Available) { r.Text = Par2Native.UnavailableReason; return r; }
            var dir = Path.GetDirectoryName(backup);
            var outName = Path.GetFileNameWithoutExtension(backup) + " (repaired with parity " + DateTime.Now.ToString("yyyy-MM-dd HHmmss", CultureInfo.InvariantCulture) + ").zip";
            var output = Path.Combine(dir, outName);
            if (File.Exists(output)) { r.Text = "A file with the repaired name already exists; nothing was written."; return r; }
            // The native rebuild works in a hidden folder named after its output, beside it, holding a file with the backup's own name, and
            // checks and names its result with ordinary Windows paths (at most 260 characters). Two long names in one path passed that limit
            // in an ordinary backup folder (the parity library wrote the file, the host could not find it, and its working folder with a
            // whole backup in it was left behind), so the rebuild gets a short new name and the result is renamed beside it once the native
            // rebuild succeeds.
            var temp = Path.Combine(dir, "~ccs-rebuild-" + Guid.NewGuid().ToString("N").Substring(0, 8) + ".tmp");
            var k = Par2Native.Repair(IndexOf(backup), backup, temp, 0, Stages(progress, "Rebuilding from the recovery data"), cancel); r.Report = k;
            if (k.Status == Par2Status.NotNeeded) { r.Ok = true; r.Text = "The backup is intact; nothing needed rebuilding."; return r; }
            k.Message = (k.Message ?? "").Replace(Path.GetFileName(temp), outName);
            if (!k.Ok) { r.Text = k.Message; return r; }
            try { File.Move(temp, output); }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            { try { File.Delete(temp); } catch (Exception) { } r.Text = "The rebuilt file could not be named " + outName + " (" + ex.Message.TrimEnd('.') + "); nothing was kept."; return r; }
            var v = Verifier.VerifyFile(output, null, () => cancel.IsCancellationRequested, (d, t) => progress?.Invoke("Checking the rebuilt backup", d, t)); r.OutputCheck = v; r.OutputPath = output;
            r.Ok = v.Ok; r.Text = k.Message + (v.Ok ? " The rebuilt backup passes its full check." : " The rebuilt file does not pass the backup's full check (" + v.Problems.FirstOrDefault() + "), so it is not a backup.");
            return r;
        }

        public static ParityResult MakeTransport(string backup, string destinationFolder, Action<string, long, long> progress = null, CancellationToken cancel = default)
        {
            var r = new ParityResult();
            var files = FilesOf(backup);
            if (files.Count == 0) { r.Text = "Make the backup's recovery data first; the one-file copy carries the backup with its recovery data."; return r; }
            var k = Check(backup, progress, cancel); if (!k.Ok) { r.Text = "The backup does not match its recovery data, so no one-file copy was made (" + k.Text + ")."; return r; }
            var outPath = Path.Combine(destinationFolder, Path.GetFileNameWithoutExtension(backup) + " (one file with parity).zip");
            if (File.Exists(outPath)) { r.Text = "A file with that name already exists in the folder; nothing was written."; return r; }
            var tmp = outPath + ".partial"; var rows = new List<object>();
            try
            {
                using (var f = new FileStream(tmp, FileMode.CreateNew, FileAccess.ReadWrite))
                {
                    var w = new ZipWriter(f, DateTime.Now);
                    var readme = Encoding.UTF8.GetBytes("CCS Console Workbench - a console backup with its parity, in one file\r\n\r\nOpen it with CCS Console Workbench (Backup Repair, Unpack a One-File Copy). It checks every part, then the backup\r\nagainst its parity (rebuilding a new file when it is damaged and the parity can), then the backup's own full check.\r\nThe parity protects the backup inside; it does not protect this outer file.\r\n");
                    w.Add("README.txt", true, s => s.Write(readme, 0, readme.Length));
                    foreach (var (src, name) in new[] { (backup, "backup/" + Path.GetFileName(backup)) }.Concat(files.Select(p => (p, "parity/" + Path.GetFileName(p)))))
                    {
                        if (cancel.IsCancellationRequested) throw new OperationCanceledException();
                        progress?.Invoke("Copying " + Path.GetFileName(src), 0, 0);
                        var e = w.Add(name, false, o => { using (var s = File.OpenRead(src)) Streams.Copy(s, o, -1, () => cancel.IsCancellationRequested); }, true);
                        rows.Add(new JObj { { "name", name }, { "bytes", e.info.Size }, { "sha256", e.sha256 } });
                    }
                    var desc = Json.Bytes(new JObj { { "schema", TransportSchema }, { "backup", "backup/" + Path.GetFileName(backup) }, { "members", rows }, { "created_utc", DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture) } });
                    w.Add(TransportDescriptor, true, s => s.Write(desc, 0, desc.Length));
                    w.Finish(); f.Flush(true);
                }
                File.Move(tmp, outPath);
            }
            catch (OperationCanceledException) { r.Text = "Canceled; no one-file copy was kept."; return r; }
            finally { if (File.Exists(tmp)) File.Delete(tmp); }
            r.Ok = true; r.OutputPath = outPath; r.Text = "Made " + Path.GetFileName(outPath) + ": the backup and its " + files.Count + " recovery data file(s), each listed with its checksum.";
            return r;
        }

        public static ParityResult Unpack(string transport, string destinationFolder, Action<string, long, long> progress = null, CancellationToken cancel = default)
        {
            var r = new ParityResult(); var written = new List<string>(); var damaged = new List<string>();
            try
            {
                using (var z = ZipReader.Open(transport, n => n == "README.txt" || n == TransportDescriptor || n.StartsWith("backup/") || n.StartsWith("parity/")))
                {
                    var de = z.Find(TransportDescriptor) ?? throw new InvalidDataException("it is not a one-file copy (no " + TransportDescriptor + ")");
                    Dictionary<string, object> d; using (var s = z.OpenMember(de)) using (var t = new StreamReader(s)) d = Json.Parse(t.ReadToEnd());
                    if (d.S("schema") != TransportSchema) throw new InvalidDataException("it is not a one-file copy this version reads");
                    string backupOut = null;
                    foreach (var m in (d.A("members") ?? new List<object>()).OfType<Dictionary<string, object>>())
                    {
                        var name = m.S("name") ?? ""; var file = name.Substring(name.IndexOf('/') + 1);
                        if (!(name.StartsWith("backup/") || name.StartsWith("parity/")) || file.Length == 0 || file.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || file.Contains("..")) throw new InvalidDataException("it names a file that is not allowed (" + name + ")");
                        var e = z.Find(name) ?? throw new InvalidDataException(name + " is listed but missing");
                        var target = Path.Combine(destinationFolder, file);
                        if (File.Exists(target)) throw new IOException(file + " already exists in the folder; nothing is overwritten");
                        progress?.Invoke("Unpacking " + file, 0, 0);
                        string sha;
                        // stored parts are read raw: a part damaged on the way is still unpacked, for the parity to rebuild
                        using (var s = e.Method == 0 ? z.StoredWindow(e) : z.OpenMember(e)) using (var o = new FileStream(target, FileMode.CreateNew, FileAccess.Write)) { written.Add(target); var h = new HashingSink(o, null, () => cancel.IsCancellationRequested); Streams.Copy(s, h, -1, () => cancel.IsCancellationRequested); h.Finish(); sha = h.Sha256; o.Flush(true); }
                        if (sha != m.S("sha256") || new FileInfo(target).Length != m.L("bytes")) damaged.Add(file);
                        if (name == d.S("backup")) backupOut = target;
                    }
                    if (backupOut == null) throw new InvalidDataException("it holds no backup");
                    r.OutputPath = backupOut;
                }
            }
            catch (Exception ex) when (ex is IOException || ex is InvalidDataException || ex is ZipFormatException || ex is OperationCanceledException)
            {
                foreach (var w in written) try { File.Delete(w); } catch { }
                r.Text = ex is OperationCanceledException ? "Canceled; nothing was kept." : "The one-file copy could not be unpacked: " + ex.Message + ". Nothing was kept."; r.OutputPath = null; return r;
            }
            var hurt = damaged.Count == 0 ? "" : "Damaged on the way: " + string.Join(", ", damaged) + ". ";
            var k = Check(r.OutputPath, progress, cancel);
            if (k.Report?.Status == Par2Status.DamagedRepairable) { var b = Rebuild(r.OutputPath, progress, cancel); b.Text = "Unpacked to " + destinationFolder + ". " + hurt + "The backup inside was damaged: " + b.Text; return b; }
            var v = Verifier.VerifyFile(r.OutputPath, null, () => cancel.IsCancellationRequested); r.OutputCheck = v; r.Ok = v.Ok;
            r.Text = "Unpacked to " + destinationFolder + ". " + hurt + (k.Ok ? "It matches its recovery data. " : Par2Native.Available ? "Recovery data: " + k.Text + " " : "") + (v.Ok ? "The backup passes its full check." : "The backup does not pass its full check (" + v.Problems.FirstOrDefault() + "), so it is not a backup.");
            return r;
        }
    }
}
