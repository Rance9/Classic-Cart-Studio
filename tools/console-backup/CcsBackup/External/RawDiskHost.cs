// Classic Cart Studio console backup and restore engine. Copyright (C) 2026 Rance9. GPL-3.0-or-later.
// The elevated --raw-disk step: this same executable (the single-file standalone Workbench, which Classic Cart Studio also installs and
// opens) started again by Windows with administrator permission ("runas"), before any window. It reads or writes the one drive the
// reviewed request names, and nothing else.
//   Command line   "--raw-disk <request file> <its SHA-256>": no secret and no instruction travels in it. Windows starts the elevated
//                  process with exactly the arguments the window gave, and a program without administrator permission cannot change
//                  them afterward, so the SHA-256 binds the step to the request the window wrote.
//   Request file   written by the window into a new folder of its own in its temporary folder, readable only by the person's own account
//                  and administrators (RawDiskRequestFile). The step refuses a path that is a link, junction or mount point, or whose
//                  folder is not one such folder; reads it with no other program able to open it; compares its SHA-256; and deletes it as
//                  it closes it, so the same request is used once (a second start with the same command line finds nothing). It holds the
//                  operation's id, a single-use secret (nonce), the pipe's random name, the window's process and program, when it was
//                  made, the reviewed drive's whole identity and the files whose drives are protected; for a write, the backup file, its
//                  size and time and every region's place and SHA-256.
//   Pipe           the window's own pipe, open only to the person's account and administrators and never over the network. Each side
//                  checks who is at the other end: the window checks that the connected process is the one Windows started; the step
//                  checks that the pipe belongs to the window's process and that it runs this same program. Then each proves it holds the
//                  request's secret (HMAC-SHA256 over a fresh random challenge from the other side), so neither talks to anything else.
//   The drive      found again by its number among the drives Windows lists, its device path resolved here (never taken from the request),
//                  and every check made again (RawDiskTarget.Check): still the same drive, field by field; a USB drive or SD card; not the
//                  drive Windows runs from; holding neither this program, the request, the backup, its recovery data nor the backup
//                  location. Then its volumes are locked (and, before a write, dismounted, as Windows requires), the drive is opened, and
//                  its identity is read once more through the opened handle immediately before the window is told it is ready. Nothing is
//                  read or written until the window says go.
//   Read           every byte in order, in whole sectors, each region's SHA-256 sent after its bytes; the identity read again at the end.
//   Write          each region read from the backup and checked against its SHA-256 from the request as it is written (a mismatch stops
//                  the write); a last part sector keeps the drive's own bytes after the image's end; flushed with write-through; then every
//                  region read back FROM THE DRIVE and compared; Windows asked to read the partition table again.
//   Cancel         the window can stop the step at any time: before the go-ahead nothing is read or written; during a write the step
//                  stops after the piece in progress and says exactly how many bytes were written (the drive is then incomplete).
//   Interruption   if the window disappears the step stops at once. If the step itself is ended, the window, which knows it gave the
//                  go-ahead and never heard the step finish, reports the drive as incomplete, never as unchanged or finished.
// The exit code repeats the result (RawDiskExit); the window relies on what came through the pipe.
using CcsBackup.Archive;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace CcsBackup.External
{
    public sealed class RawImagePlan
    {
        public string Backup; public long BackupBytes; public DateTime BackupMtimeUtc; public long ImageBytes; public int SectorBytes = -1; public long RegionSize;
        public List<(long offset, long size, string sha256)> Regions = new List<(long, long, string)>();
        public JObj ToJson() => new JObj { { "backup", Backup }, { "backup_bytes", BackupBytes }, { "backup_mtime_utc", BackupMtimeUtc.ToString("o", CultureInfo.InvariantCulture) },
            { "image_bytes", ImageBytes }, { "sector_bytes", (long)SectorBytes }, { "region_size", RegionSize },
            { "regions", Regions.Select(r => (object)new List<object> { r.offset, r.size, r.sha256 }).ToList() } };
        public static RawImagePlan FromJson(Dictionary<string, object> d)
        {
            if (d == null) return null;
            var p = new RawImagePlan { Backup = d.S("backup"), BackupBytes = d.L("backup_bytes"), ImageBytes = d.L("image_bytes"), SectorBytes = (int)d.L("sector_bytes", -1), RegionSize = d.L("region_size") };
            DateTime.TryParse(d.S("backup_mtime_utc"), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out p.BackupMtimeUtc);
            foreach (var o in d.A("regions") ?? new List<object>())
            {
                var l = (o as IEnumerable<object>)?.ToList(); if (l == null || l.Count != 3) { p.Regions = null; break; }   // the reader gives nested arrays as object[]
                p.Regions.Add((Convert.ToInt64(l[0], CultureInfo.InvariantCulture), Convert.ToInt64(l[1], CultureInfo.InvariantCulture), l[2] as string));
            }
            return p;
        }
    }

    public sealed class RawDiskRequest
    {
        public const string Schema = "ccs-raw-disk/1";
        public string OperationId, Kind, Nonce, Pipe, WindowProgram; public int WindowPid; public DateTime CreatedUtc;
        public DiskIdentity Target; public List<ProtectedPath> Protect = new List<ProtectedPath>(); public long RegionSize;
        public RawImagePlan Image;

        public JObj ToJson() => new JObj { { "schema", Schema }, { "operation_id", OperationId }, { "kind", Kind }, { "nonce", Nonce }, { "pipe", Pipe },
            { "window_pid", (long)WindowPid }, { "window_program", WindowProgram }, { "created_utc", CreatedUtc.ToString("o", CultureInfo.InvariantCulture) },
            { "target", Target?.ToJson() }, { "protect", Protect.Select(p => (object)p.ToJson()).ToList() }, { "region_size", RegionSize }, { "image", Image?.ToJson() } };

        public static RawDiskRequest Parse(byte[] bytes)
        {
            var d = Json.Parse(Encoding.UTF8.GetString(bytes));
            if (d.S("schema") != Schema) throw new FormatException("not a whole-drive request (" + (d.S("schema") ?? "no schema") + ")");
            var r = new RawDiskRequest { OperationId = d.S("operation_id"), Kind = d.S("kind"), Nonce = d.S("nonce"), Pipe = d.S("pipe"), WindowPid = (int)d.L("window_pid", -1), WindowProgram = d.S("window_program"),
                Target = DiskIdentity.FromJson(d.O("target")), RegionSize = d.L("region_size", 0), Image = RawImagePlan.FromJson(d.O("image")) };
            DateTime.TryParse(d.S("created_utc"), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out r.CreatedUtc);
            foreach (var o in d.A("protect") ?? new List<object>()) { var p = ProtectedPath.FromJson(o as Dictionary<string, object>); if (p != null) r.Protect.Add(p); }
            return r;
        }

        static readonly Regex PipeName = new Regex("^ccs-raw-disk-[0-9a-f]{32}$", RegexOptions.CultureInvariant);
        static bool Rooted(string p) => !string.IsNullOrEmpty(p) && p.Length >= 3 && char.IsLetter(p[0]) && p[1] == ':' && p[2] == '\\';

        /// <summary>empty when the request is well formed; it is then still only a request (every fact in it is checked again on the PC)</summary>
        public List<string> Problems()
        {
            var p = new List<string>();
            if (!Hex.IsHex(OperationId, 32)) p.Add("the request names no operation");
            if (Kind != "read" && Kind != "write") p.Add("unknown kind: " + (Kind ?? "(none)"));
            if (!Hex.IsHex(Nonce, 64)) p.Add("the request has no secret");
            if (Pipe == null || !PipeName.IsMatch(Pipe)) p.Add("the request names no pipe");
            if (WindowPid <= 0) p.Add("the request names no window");
            if (Target == null || Target.Number < 0 || Target.Bytes <= 0 || !new[] { 512, 1024, 2048, 4096 }.Contains(Target.SectorBytes) || string.IsNullOrEmpty(Target.Path)) p.Add("the request names no complete drive identity");
            if (Protect.Any(x => !Rooted(x.Path))) p.Add("a protected path is not a full local path");
            if (Kind == "read" && (RegionSize < 1 << 20 || RegionSize % 4096 != 0)) p.Add("the region size is not usable");
            if (Kind == "write")
            {
                var i = Image;
                if (i == null || i.Regions == null || i.Regions.Count == 0) p.Add("the write names no image");
                else
                {
                    if (!Rooted(i.Backup)) p.Add("the backup is not a full local path");
                    if (i.ImageBytes <= 0 || i.RegionSize < 1 << 20 || i.RegionSize % 4096 != 0 || i.BackupBytes <= 0) p.Add("the image's sizes are not usable");
                    if (i.Regions.Sum(r => r.size) != i.ImageBytes) p.Add("the regions do not add up to the image");
                    for (int k = 0; k < i.Regions.Count; k++)
                    {
                        var r = i.Regions[k];
                        if (r.offset < 0 || r.size <= 0 || !Hex.IsHex(r.sha256, 64) || (k < i.Regions.Count - 1 ? r.size != i.RegionSize : r.size > i.RegionSize) || r.offset + r.size > i.BackupBytes) { p.Add("region " + (k + 1) + " is not usable"); break; }
                    }
                }
            }
            return p;
        }
    }

    /// <summary>the request file: written by the window, read once and deleted by the step</summary>
    public static class RawDiskRequestFile
    {
        public const string FileName = "request.json";
        static readonly Regex FolderName = new Regex("^ccs-raw-disk-[0-9a-f]{32}$", RegexOptions.CultureInvariant);

        /// <summary>a new folder of its own in the person's temporary folder (only their account and administrators can open it), the
        /// request in it; returns the file and its SHA-256</summary>
        public static (string file, string sha256) Write(RawDiskRequest r, string tempRoot = null)
        {
            var dir = Path.Combine(tempRoot ?? Path.GetTempPath(), "ccs-raw-disk-" + Guid.NewGuid().ToString("N"));
            var ds = new System.Security.AccessControl.DirectorySecurity();
            ds.SetAccessRuleProtection(true, false);
            var inherit = System.Security.AccessControl.InheritanceFlags.ContainerInherit | System.Security.AccessControl.InheritanceFlags.ObjectInherit;
            foreach (var sid in new[] { WindowsIdentity.GetCurrent().User, new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null), new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null) })
                ds.AddAccessRule(new System.Security.AccessControl.FileSystemAccessRule(sid, System.Security.AccessControl.FileSystemRights.FullControl, inherit, System.Security.AccessControl.PropagationFlags.None, System.Security.AccessControl.AccessControlType.Allow));
            if (Directory.Exists(dir)) throw new IOException("the request folder already exists");
            Directory.CreateDirectory(dir, ds);
            var bytes = Json.Bytes(r.ToJson()); var file = Path.Combine(dir, FileName);
            using (var f = new FileStream(file, FileMode.CreateNew, FileAccess.Write, FileShare.None)) { f.Write(bytes, 0, bytes.Length); f.Flush(true); }
            using (var h = SHA256.Create()) return (file, Hex.Of(h.ComputeHash(bytes)));
        }

        /// <summary>the step's single read: refuses a link or a file in any other place, reads it with no other opener, compares its SHA-256 and
        /// deletes it as it closes it; throws FormatException for anything it will not use</summary>
        internal static byte[] Consume(string file, string sha256, IDiskQueries q)
        {
            if (string.IsNullOrEmpty(file) || !Hex.IsHex(sha256, 64)) throw new FormatException("no request file");
            string full; try { full = Path.GetFullPath(file); } catch (Exception ex) { throw new FormatException("the request path is not usable: " + ex.Message); }
            if (!string.Equals(full, file, StringComparison.OrdinalIgnoreCase) || full.StartsWith(@"\\", StringComparison.Ordinal)) throw new FormatException("the request path is not a full local path");
            if (!string.Equals(Path.GetFileName(full), FileName, StringComparison.Ordinal) || !FolderName.IsMatch(Path.GetFileName(Path.GetDirectoryName(full)) ?? "")) throw new FormatException("the request is not in a request folder");
            if (q.Redirected(full)) throw new FormatException("the request path goes through a link, junction or mount point");
            byte[] bytes;
            using (var f = new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.None, 4096, FileOptions.DeleteOnClose))
            {
                if (f.Length <= 0 || f.Length > 16_000_000) throw new FormatException("the request file has an unusable size");
                bytes = new byte[f.Length]; int got = 0; while (got < bytes.Length) { int n = f.Read(bytes, got, bytes.Length - got); if (n <= 0) throw new FormatException("the request file ended early"); got += n; }
            }
            try { Directory.Delete(Path.GetDirectoryName(full)); } catch (Exception) { }
            string now; using (var h = SHA256.Create()) now = Hex.Of(h.ComputeHash(bytes));
            if (now != sha256) throw new FormatException("the request file is not the one the window wrote");
            return bytes;
        }
    }

    /// <summary>the pipe's messages: a type byte, a 4-byte length (little-endian) and the payload</summary>
    internal static class RawFrame
    {
        public const byte Challenge = 1, Hello = 2, Accept = 3, Ready = 4, Go = 5, Data = 6, Region = 7, Progress = 8, Result = 9, Cancel = 10;
        public const int MaxPayload = (4 << 20) + 65536;
        public static void Write(Stream s, byte type, byte[] p, int n)
        {
            var h = new byte[5]; h[0] = type; BitConverter.GetBytes(n).CopyTo(h, 1);
            s.Write(h, 0, 5); if (n > 0) s.Write(p, 0, n); s.Flush();
        }
        /// <summary>null at the end of the pipe</summary>
        public static (byte type, byte[] payload)? Read(Stream s)
        {
            var h = new byte[5]; if (!Fill(s, h, 5, true)) return null;
            int n = BitConverter.ToInt32(h, 1); if (n < 0 || n > MaxPayload) throw new IOException("a message of an unusable size");
            var p = new byte[n]; if (n > 0 && !Fill(s, p, n, false)) throw new IOException("the pipe ended inside a message");
            return (h[0], p);
        }
        static bool Fill(Stream s, byte[] b, int n, bool eofOk)
        {
            int got = 0;
            while (got < n) { int r = s.Read(b, got, n - got); if (r <= 0) { if (got == 0 && eofOk) return false; throw new IOException("the pipe ended inside a message"); } got += r; }
            return true;
        }
        public static byte[] J(JObj o) => Json.Bytes(o);
        public static Dictionary<string, object> P(byte[] b) { try { return Json.Parse(Encoding.UTF8.GetString(b)); } catch (Exception) { return null; } }

        /// <summary>a side's proof that it holds the request's secret, over the other side's fresh challenge</summary>
        public static string Proof(string nonceHex, string role, string op, byte[] challenge)
        {
            var key = new byte[32]; for (int i = 0; i < 32; i++) key[i] = Convert.ToByte(nonceHex.Substring(2 * i, 2), 16);
            using (var h = new HMACSHA256(key)) return Hex.Of(h.ComputeHash(Encoding.UTF8.GetBytes(Schema + "|" + role + "|" + op + "|" + Hex.Of(challenge))));
        }
        const string Schema = RawDiskRequest.Schema;
        public static bool Same(string a, string b) { if (a == null || b == null || a.Length != b.Length) return false; int d = 0; for (int i = 0; i < a.Length; i++) d |= a[i] ^ b[i]; return d == 0; }
        public static byte[] Random32() { var b = new byte[32]; using (var r = RandomNumberGenerator.Create()) r.GetBytes(b); return b; }
    }

    /// <summary>one side of the pipe: the other side's messages read on a thread of their own (so a cancel is seen while data flows), and
    /// this side's messages written one at a time</summary>
    internal sealed class FrameChannel : IDisposable
    {
        readonly Stream s; readonly BlockingCollection<(byte, byte[])> q = new BlockingCollection<(byte, byte[])>(64); readonly object wl = new object(); readonly Thread t;
        readonly CancellationTokenSource stop = new CancellationTokenSource();
        public FrameChannel(Stream s)
        {
            this.s = s;
            t = new Thread(() =>
            {
                try { while (true) { var f = RawFrame.Read(s); if (f == null) break; q.Add(f.Value, stop.Token); } }
                catch (Exception) { }
                finally { q.CompleteAdding(); }
            }) { IsBackground = true, Name = "raw-disk pipe" };
            t.Start();
        }
        /// <summary>the next message, or null after ms milliseconds (or at once when the other side is gone)</summary>
        public (byte type, byte[] payload)? Take(int ms)
        {
            try { return q.TryTake(out var f, ms) ? f : ((byte, byte[])?)null; } catch (InvalidOperationException) { return null; }
        }
        /// <summary>the other side has ended the pipe and every message it sent was taken</summary>
        public bool Ended => q.IsCompleted;
        public void Send(byte type, byte[] p = null) { lock (wl) RawFrame.Write(s, type, p ?? new byte[0], p?.Length ?? 0); }
        public void Send(byte type, byte[] p, int n) { lock (wl) RawFrame.Write(s, type, p, n); }
        public void Dispose() { try { stop.Cancel(); } catch (Exception) { } try { s.Dispose(); } catch (Exception) { } }
    }

    public static class RawDiskHost
    {
        public const string Flag = "--raw-disk";
        const int Chunk = 1 << 20;

        /// <summary>the elevated mode's entry, called by the Workbench program before anything else when Flag is on the command line</summary>
        public static int Run(string[] args)
        {
            if (!Gate.StorageRawReads && !Gate.StorageDiskWrites) return RawDiskExit.GateClosed;   // closed in code: nothing is read and nothing is changed
            return Execute(args, WindowsRawDiskSystem.Open(), RawDiskPolicy.UsbAndSd);
        }

        /// <summary>how long the step waits for the window's pipe</summary>
        internal static int ConnectMs = 30000;
        /// <summary>how the step reaches the window's pipe: the stream and the process that owns the pipe (tests: the same, in one process).
        /// Opened for overlapped input and output: the step reads the window's messages on a thread of its own while it writes, and on a pipe
        /// opened without it Windows lets one wait for the other (a pending read would hold back every write).</summary>
        internal static Func<string, int, (Stream stream, int serverPid)> ConnectPipe = (name, ms) =>
        {
            var c = new NamedPipeClientStream(".", name, PipeDirection.InOut, PipeOptions.Asynchronous, TokenImpersonationLevel.Identification);
            c.Connect(ms);
            if (!N.GetNamedPipeServerProcessId(c.SafePipeHandle, out int pid)) pid = -1;
            return (c, pid);
        };

        /// <summary>the step itself, on any PC system (the tests give stand-in drives backed by files)</summary>
        internal static int Execute(string[] args, IRawDiskSystem sys, RawDiskPolicy policy)
        {
            if (!sys.Elevated) return RawDiskExit.NotElevated;   // nothing is read: not even the request
            RawDiskRequest req; string requestFile;
            try
            {
                int i = Array.IndexOf(args ?? new string[0], Flag);
                if (i < 0 || i + 2 >= args.Length) return RawDiskExit.BadRequest;
                requestFile = args[i + 1];
                req = RawDiskRequest.Parse(RawDiskRequestFile.Consume(requestFile, args[i + 2], sys));
                if (req.Problems().Count > 0) return RawDiskExit.BadRequest;
                if (!string.Equals(req.WindowProgram, sys.SelfProgram, StringComparison.OrdinalIgnoreCase)) return RawDiskExit.BadRequest;   // made by this same program only
                var age = DateTime.UtcNow - req.CreatedUtc.ToUniversalTime();
                if (age < TimeSpan.FromMinutes(-2) || age > TimeSpan.FromMinutes(15)) return RawDiskExit.BadRequest;
            }
            catch (Exception ex) when (ex is FormatException || ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException || ex is InvalidCastException) { return RawDiskExit.BadRequest; }
            if (req.Kind == "read" ? !Gate.StorageRawReads : !Gate.StorageDiskWrites) return RawDiskExit.GateClosed;
            Stream pipe; int serverPid;
            try { (pipe, serverPid) = ConnectPipe(req.Pipe, ConnectMs); }
            catch (Exception ex) when (ex is IOException || ex is TimeoutException || ex is UnauthorizedAccessException) { return RawDiskExit.PeerRefused; }
            using (var ch = new FrameChannel(pipe))
            {
                if (serverPid != req.WindowPid || !string.Equals(sys.ProgramOf(serverPid), sys.SelfProgram, StringComparison.OrdinalIgnoreCase)) return RawDiskExit.PeerRefused;
                var c = ch.Take(30000);
                if (c == null || c.Value.type != RawFrame.Challenge || c.Value.payload.Length != 32) return RawDiskExit.PeerRefused;
                var mine = RawFrame.Random32();
                ch.Send(RawFrame.Hello, RawFrame.J(new JObj { { "operation_id", req.OperationId }, { "proof", RawFrame.Proof(req.Nonce, "helper", req.OperationId, c.Value.payload) }, { "challenge", Hex.Of(mine) } }));
                var a = ch.Take(30000);
                if (a == null || a.Value.type != RawFrame.Accept || !RawFrame.Same(RawFrame.P(a.Value.payload)?.S("proof"), RawFrame.Proof(req.Nonce, "window", req.OperationId, mine))) return RawDiskExit.PeerRefused;
                // from here on, every outcome is also told through the pipe
                int Result(int code, string state, string text, JObj more = null)
                {
                    var j = new JObj { { "code", (long)code }, { "state", state }, { "text", text } };
                    foreach (var kv in more ?? new JObj()) j.Set(kv.Key, kv.Value);
                    try { ch.Send(RawFrame.Result, RawFrame.J(j)); } catch (Exception) { }
                    return code;
                }
                try
                {
                    var protect = new List<ProtectedPath>(req.Protect);
                    protect.AddRange(RawDiskTarget.Always(sys.SelfProgram));
                    protect.Add(new ProtectedPath(Path.GetDirectoryName(requestFile), "Workbench's request for this step"));
                    if (req.Kind == "write") protect.Add(new ProtectedPath(req.Image.Backup, "the backup being restored"));
                    var tc = RawDiskTarget.Check(sys, policy, req.Target, protect);
                    if (!tc.Ok) return Result(RawDiskExit.TargetRefused, "refused", tc.Text, new JObj { { "refusal", tc.Refusal.ToString() } });
                    return req.Kind == "read" ? Read(req, sys, tc, ch, Result) : Write(req, sys, tc, ch, Result);
                }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is System.ComponentModel.Win32Exception || ex is ArgumentException)
                { return Result(RawDiskExit.Unexpected, "stopped", "The administrator step stopped before it read or wrote the drive: " + ex.Message); }
            }
        }

        delegate int ResultFn(int code, string state, string text, JObj more = null);

        /// <summary>the go-ahead, a cancel, the window gone or fifteen minutes without an answer (false)</summary>
        static bool WaitForGo(FrameChannel ch)
        {
            var until = DateTime.UtcNow.AddMinutes(15);
            while (true)
            {
                var f = ch.Take(1000);
                if (f == null) { if (ch.Ended || DateTime.UtcNow > until) return false; continue; }
                if (f.Value.type == RawFrame.Go) return true;
                if (f.Value.type == RawFrame.Cancel) return false;
            }
        }

        /// <summary>a cancel from the window, or the window gone</summary>
        static bool Stopped(FrameChannel ch)
        {
            while (true)
            {
                var f = ch.Take(0);
                if (f == null) return ch.Ended;
                if (f.Value.type == RawFrame.Cancel) return true;
            }
        }

        static int Read(RawDiskRequest req, IRawDiskSystem sys, TargetCheck tc, FrameChannel ch, ResultFn Result)
        {
            using (var lk = sys.Lock(tc.Volumes, false))
            using (var disk = sys.Open(tc.Entry, false))
            {
                var d = req.Target.Differs(disk.Identity());
                if (d != null) return Result(RawDiskExit.TargetRefused, "refused", "The drive opened is not the one you chose: " + d + " is different. Nothing was read.");
                if (disk.Length != req.Target.Bytes) return Result(RawDiskExit.TargetRefused, "refused", "The drive's size is not the reviewed size. Nothing was read.");
                bool consistent = lk.NotLocked.Count == 0;
                ch.Send(RawFrame.Ready, RawFrame.J(new JObj { { "identity", disk.Identity().ToJson() }, { "volumes", tc.Volumes.Select(v => (object)v.ToJson()).ToList() }, { "consistent", consistent }, { "not_locked", lk.NotLocked.Cast<object>().ToList() } }));
                if (!WaitForGo(ch)) return Result(RawDiskExit.CanceledNothingWritten, "canceled", "Canceled before the drive was read.");
                long len = disk.Length, R = req.RegionSize, at = 0; var buf = new byte[Chunk]; int k = 1;
                var h = SHA256.Create(); long regionEnd = Math.Min(R, len);
                try
                {
                    while (at < len)
                    {
                        int want = (int)Math.Min(Chunk, regionEnd - at);
                        int got;
                        try { got = disk.Read(at, buf, want); }
                        catch (Exception ex) when (ex is IOException || ex is System.ComponentModel.Win32Exception || ex is ArgumentException)
                        { return Result(RawDiskExit.Stopped, "stopped", "Windows could not read the drive at byte " + at.ToString("N0", CultureInfo.InvariantCulture) + ": " + ex.Message); }
                        if (got <= 0) return Result(RawDiskExit.Stopped, "stopped", "The drive ended at byte " + at.ToString("N0", CultureInfo.InvariantCulture) + ", before its reported size.");
                        h.TransformBlock(buf, 0, got, null, 0); ch.Send(RawFrame.Data, buf, got); at += got;
                        if (at == regionEnd)
                        {
                            h.TransformFinalBlock(new byte[0], 0, 0);
                            ch.Send(RawFrame.Region, RawFrame.J(new JObj { { "k", (long)k }, { "sha256", Hex.Of(h.Hash) }, { "bytes", regionEnd - (k - 1) * R } }));
                            h.Dispose(); h = SHA256.Create(); k++; regionEnd = Math.Min(regionEnd + R, len);
                        }
                        if (Stopped(ch)) return Result(RawDiskExit.CanceledNothingWritten, "canceled", "Canceled while the drive was read. Nothing on it was changed.");
                    }
                }
                finally { h.Dispose(); }
                var after = req.Target.Differs(disk.Identity());
                if (after != null) return Result(RawDiskExit.Stopped, "stopped", "The drive changed while it was read (" + after + ").");
                return Result(RawDiskExit.Done, "done", "The drive was read.", new JObj { { "bytes", len }, { "consistent", consistent } });
            }
        }

        static int Write(RawDiskRequest req, IRawDiskSystem sys, TargetCheck tc, FrameChannel ch, ResultFn Result)
        {
            var img = req.Image; var now = tc.Now;
            if (img.ImageBytes > now.Bytes) return Result(RawDiskExit.TargetRefused, "refused", "The drive (" + Engine.Units.Gb(now.Bytes) + ") is smaller than the image (" + Engine.Units.Gb(img.ImageBytes) + "). Nothing was written.");
            if (img.SectorBytes > 0 && img.SectorBytes != now.SectorBytes) return Result(RawDiskExit.TargetRefused, "refused", "The drive uses " + now.SectorBytes + "-byte sectors and the image " + img.SectorBytes + "-byte sectors, so its partitions would not line up. Nothing was written.");
            if (sys.Redirected(img.Backup)) return Result(RawDiskExit.BadRequest, "refused", "The backup's path goes through a link, junction or mount point. Nothing was written.");
            using (var src = new FileStream(img.Backup, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16))
            {
                if (src.Length != img.BackupBytes || File.GetLastWriteTimeUtc(img.Backup) != img.BackupMtimeUtc) return Result(RawDiskExit.TargetRefused, "refused", "The backup file changed after the restore was reviewed. Nothing was written.");
                using (var lk = sys.Lock(tc.Volumes, true))
                {
                    if (lk.NotLocked.Count > 0) return Result(RawDiskExit.InUse, "in-use", "Another program is using the drive (" + string.Join("; ", lk.NotLocked) + "). Close it and try again. Nothing was written.");
                    using (var disk = sys.Open(tc.Entry, true))
                    {
                        var d = req.Target.Differs(disk.Identity());
                        if (d != null) return Result(RawDiskExit.TargetRefused, "refused", "The drive opened is not the one you chose: " + d + " is different. Nothing was written.");
                        if (disk.Length != req.Target.Bytes) return Result(RawDiskExit.TargetRefused, "refused", "The drive's size is not the reviewed size. Nothing was written.");
                        int sector = disk.SectorBytes;
                        ch.Send(RawFrame.Ready, RawFrame.J(new JObj { { "identity", disk.Identity().ToJson() }, { "volumes", tc.Volumes.Select(v => (object)v.ToJson()).ToList() } }));
                        if (!WaitForGo(ch)) return Result(RawDiskExit.CanceledNothingWritten, "canceled", "Canceled before anything was written. Nothing on the drive was written.", new JObj { { "written", 0L } });
                        long written = 0, reported = 0; var buf = new byte[Chunk]; var sec = new byte[Math.Max(sector, 512)];
                        JObj W() => new JObj { { "written", written }, { "image_bytes", img.ImageBytes } };
                        try
                        {
                            for (int i = 0; i < img.Regions.Count; i++)
                            {
                                var (off, size, sha) = img.Regions[i]; long pos = i * img.RegionSize, left = size;
                                using (var h = SHA256.Create())
                                {
                                    src.Position = off;
                                    while (left > 0)
                                    {
                                        int n = (int)Math.Min(Chunk, left), got = 0;
                                        while (got < n) { int r = src.Read(buf, got, n - got); if (r <= 0) break; got += r; }
                                        if (got < n) { disk.Flush(); return Result(RawDiskExit.Stopped, "stopped-incomplete", "The backup file ended early.", W()); }
                                        h.TransformBlock(buf, 0, n, null, 0);
                                        int whole = n - n % sector, tail = n - whole;
                                        if (whole > 0) { disk.Write(pos, buf, whole); written += whole; }
                                        if (tail > 0)
                                        {   // the image's end falls inside a sector: the drive's own bytes after it are kept
                                            if (disk.Read(pos + whole, sec, sector) != sector) throw new IOException("the drive's last part sector could not be read");
                                            Buffer.BlockCopy(buf, whole, sec, 0, tail); disk.Write(pos + whole, sec, sector); written += tail;
                                        }
                                        pos += n; left -= n;
                                        if (written - reported >= 8 << 20 || written == img.ImageBytes) { reported = written; ch.Send(RawFrame.Progress, RawFrame.J(new JObj { { "phase", "write" }, { "done", written }, { "total", img.ImageBytes } })); }
                                        if (Stopped(ch)) { disk.Flush(); return Result(RawDiskExit.CanceledIncomplete, "canceled-incomplete", "Canceled while writing.", W()); }
                                    }
                                    h.TransformFinalBlock(new byte[0], 0, 0);
                                    if (Hex.Of(h.Hash) != sha) { disk.Flush(); return Result(RawDiskExit.Stopped, "stopped-incomplete", "Region " + (i + 1) + " of the backup does not match its recorded checksum, so writing stopped there.", W()); }
                                }
                            }
                            disk.Flush();
                        }
                        catch (Exception ex) when (ex is IOException || ex is System.ComponentModel.Win32Exception || ex is ArgumentException)
                        {
                            try { disk.Flush(); } catch (Exception) { }
                            return Result(RawDiskExit.Stopped, "stopped-incomplete", "Windows could not write to the drive: " + ex.Message, W());
                        }
                        // every region read back from the drive itself
                        var bad = new List<object>(); long checkedBytes = 0;
                        try
                        {
                            for (int i = 0; i < img.Regions.Count; i++)
                            {
                                var (_, size, sha) = img.Regions[i]; long pos = i * img.RegionSize, left = size;
                                using (var h = SHA256.Create())
                                {
                                    while (left > 0)
                                    {
                                        int n = (int)Math.Min(Chunk, left); int whole = n % sector == 0 ? n : n - n % sector + sector;
                                        int got = disk.Read(pos, buf, whole); if (got < n) throw new IOException("the drive ended early while it was read back");
                                        h.TransformBlock(buf, 0, n, null, 0); pos += n; left -= n; checkedBytes += n;
                                        if (checkedBytes % (64 << 20) < Chunk || left == 0) ch.Send(RawFrame.Progress, RawFrame.J(new JObj { { "phase", "readback" }, { "done", checkedBytes }, { "total", img.ImageBytes } }));
                                        if (Stopped(ch)) return Result(RawDiskExit.CanceledIncomplete, "written-not-checked", "Canceled while the drive was read back: the image was written, but not every part was checked.", W());
                                    }
                                    h.TransformFinalBlock(new byte[0], 0, 0);
                                    if (Hex.Of(h.Hash) != sha) bad.Add((long)(i + 1));
                                }
                            }
                        }
                        catch (Exception ex) when (ex is IOException || ex is System.ComponentModel.Win32Exception || ex is ArgumentException)
                        { return Result(RawDiskExit.ReadBackDiffers, "written-not-checked", "The image was written, but the drive could not be read back: " + ex.Message, W()); }
                        disk.Refresh();
                        var more = W(); more.Set("bad_regions", bad); more.Set("checked", checkedBytes);
                        return bad.Count == 0 ? Result(RawDiskExit.Done, "done", "Written and read back.", more) : Result(RawDiskExit.ReadBackDiffers, "read-back-differs", bad.Count + " region(s) read back differently.", more);
                    }
                }
            }
        }
    }
}
