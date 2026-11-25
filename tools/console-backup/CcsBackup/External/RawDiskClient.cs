// Classic Cart Studio console backup and restore engine. Copyright (C) 2026 Rance9. GPL-3.0-or-later.
// The window's side of a whole-drive read or write (the elevated side: RawDiskHost.cs). The window itself never has administrator
// permission and never opens a drive for reading or writing. For one operation it:
//   1. makes a pipe with a random name that only the person's own account and administrators can open, and never over the network;
//   2. writes the request file (RawDiskRequestFile) and asks Windows to start this same program with administrator permission, once
//      (RunasRawDiskLauncher: Windows' permission prompt; an already elevated window is not asked again). A refused prompt is reported as
//      that, with nothing read or written;
//   3. checks that the process connected to its pipe is the one Windows started, and that it holds the request's secret, and proves its
//      own; then waits for the step to check the drive again and say it is ready (or why it refused);
//   4. compares the identity the step read through the opened drive with the reviewed one, then gives the go-ahead: a read streams the
//      drive's bytes and each region's SHA-256 (RawDiskReader); a write reports progress, its own read-back and the result (Write).
// A write the step was told to start and never said it finished is reported as an incomplete drive (interrupted), never as finished or
// as unchanged. Cancel is sent to the step, which stops after the piece in progress and says exactly where.
using CcsBackup.Driver;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Security.AccessControl;
using System.Security.Principal;

namespace CcsBackup.External
{
    public interface IRawDiskProcess : IDisposable { int Id { get; } bool HasExited { get; } int ExitCode { get; } bool WaitForExit(int ms); }
    public interface IRawDiskLauncher { IRawDiskProcess Start(string arguments); }

    /// <summary>the real start: this same program through Windows' "runas" (its permission prompt), hidden, in System32 as its working folder.
    /// Made only through Open, right after the boundary check.</summary>
    public sealed class RunasRawDiskLauncher : IRawDiskLauncher
    {
        readonly string exe;
        RunasRawDiskLauncher(string exe) { this.exe = exe; }

        internal static IRawDiskLauncher Open(string kind)
        {
            if (kind == "read" ? !Gate.StorageRawReads : !Gate.StorageDiskWrites) throw new GateClosedException(kind == "read" ? Gate.StorageRawText : Gate.StorageDiskText, "the administrator step");
            return new RunasRawDiskLauncher(Process.GetCurrentProcess().MainModule.FileName);
        }

        public IRawDiskProcess Start(string arguments)
        {
            var psi = new ProcessStartInfo(exe, arguments) { UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden, WorkingDirectory = Environment.SystemDirectory };
            Process p;
            try { p = Process.Start(psi); }
            catch (Win32Exception ex) when (ex.NativeErrorCode == RawDiskExit.Canceled) { throw new ElevationCanceledException(); }
            if (p == null) throw new Win32Exception(RawDiskExit.Canceled);
            return new Proc(p);
        }

        sealed class Proc : IRawDiskProcess
        {
            readonly Process p; public Proc(Process p) { this.p = p; }
            public int Id => p.Id; public bool HasExited => p.HasExited; public int ExitCode => p.ExitCode;
            public bool WaitForExit(int ms) => p.WaitForExit(ms);
            public void Dispose() => p.Dispose();
        }
    }

    public sealed class RawDiskSession : IDisposable
    {
        /// <summary>why nothing was (or will be) read or written; null while the session can go ahead</summary>
        public string Refusal; public bool Declined, Canceled; public int? ExitCode; public string RefusalKind;
        public DiskIdentity Identity; public List<DiskVolume> Volumes = new List<DiskVolume>(); public bool Consistent; public List<string> NotLocked = new List<string>();
        internal FrameChannel Channel; internal IRawDiskProcess Process; internal NamedPipeServerStream Server; internal string RequestFolder;
        internal Dictionary<string, object> FinalResult; internal bool Finished; bool go;
        public bool GoSent => go;
        internal void Go() { Channel.Send(RawFrame.Go); go = true; }
        internal void Cancel() { try { Channel?.Send(RawFrame.Cancel); } catch (Exception) { } }
        public void Dispose()
        {
            if (!Finished) Cancel();
            Channel?.Dispose(); try { Server?.Dispose(); } catch (Exception) { }   // a step still waiting or working sees the pipe end and stops
            try { Process?.WaitForExit(20000); } catch (Exception) { }
            if (RequestFolder != null && Directory.Exists(RequestFolder)) try { Directory.Delete(RequestFolder, true); } catch (Exception) { }
            Process?.Dispose();
        }
    }

    /// <summary>the drive's bytes as one stream, in order, from the step (each region's SHA-256 as the step computed it is kept for comparing)</summary>
    public sealed class RawDiskReader : Stream
    {
        readonly RawDiskSession s; readonly Func<bool> cancelled; byte[] cur; int pos; bool done; long read;
        public readonly List<(long k, string sha256, long bytes)> Regions = new List<(long, string, long)>();
        internal RawDiskReader(RawDiskSession s, Func<bool> cancelled) { this.s = s; this.cancelled = cancelled ?? (() => false); }
        public Dictionary<string, object> Result => s.FinalResult;

        bool Next()
        {
            var f = s.Channel.Take(250);
            if (f == null)
            {
                if (cancelled()) throw new OperationCanceledException();
                if (s.Channel.Ended) throw new IOException("Workbench's administrator step ended before it finished reading the drive (after " + Engine.Units.Gb(read) + ").");
                return false;
            }
            switch (f.Value.type)
            {
                case RawFrame.Data: cur = f.Value.payload; pos = 0; read += cur.Length; break;
                case RawFrame.Region: var j = RawFrame.P(f.Value.payload); Regions.Add((j.L("k"), j.S("sha256"), j.L("bytes"))); break;
                case RawFrame.Result:
                    done = true; s.Finished = true; s.FinalResult = RawFrame.P(f.Value.payload);
                    if (s.FinalResult?.L("code") != RawDiskExit.Done) throw new IOException(s.FinalResult?.S("text") ?? "Workbench's administrator step stopped reading the drive.");
                    break;
            }
            return true;
        }

        public override int Read(byte[] b, int o, int c)
        {
            while (cur == null || pos >= cur.Length) { if (done) return 0; Next(); }
            int n = Math.Min(c, cur.Length - pos); Buffer.BlockCopy(cur, pos, b, o, n); pos += n; return n;
        }
        /// <summary>the rest of the conversation, up to the step's result (which must be that it finished)</summary>
        public Dictionary<string, object> Finish() { while (!done) Next(); return s.FinalResult; }

        public override bool CanRead => true; public override bool CanSeek => false; public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => read; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long o, SeekOrigin so) => throw new NotSupportedException();
        public override void SetLength(long v) => throw new NotSupportedException();
        public override void Write(byte[] b, int o, int c) => throw new NotSupportedException();
    }

    public sealed class RawDiskWriteResult
    {
        /// <summary>complete | declined | refused | canceled (nothing written) | canceled-incomplete | stopped-incomplete | interrupted |
        /// written-not-checked | read-back-differs</summary>
        public string State, Text; public long Written, Checked; public List<long> BadRegions = new List<long>(); public DiskIdentity Identity; public int? ExitCode;
        public bool Ok => State == "complete";
        /// <summary>nothing was written to the drive (only for states where that is known)</summary>
        public bool NothingWritten => State == "declined" || State == "refused" || State == "canceled";
    }

    public static class RawDiskClient
    {
        /// <summary>how the step is started (tests: on a thread of this process with stand-in drives)</summary>
        internal static Func<string, IRawDiskLauncher> Launcher = kind => RunasRawDiskLauncher.Open(kind);
        /// <summary>this program's own file, which the step must also be</summary>
        internal static Func<string> Program = () => System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName;
        /// <summary>where request folders are made (null: the person's temporary folder)</summary>
        internal static string TempRoot;

        public static string DeclinedText(string kind) => kind == "read" ? "Windows did not give administrator permission, so the drive was not read." : "Windows did not give administrator permission, so nothing was written to the drive.";

        /// <summary>a whole-drive read, checked and ready for the go-ahead (RawDiskReader), or a session whose Refusal says why not</summary>
        public static RawDiskSession OpenRead(DiskIdentity target, long regionSize, IEnumerable<ProtectedPath> protect, Func<bool> cancelled)
            => Open(new RawDiskRequest { Kind = "read", Target = target, RegionSize = regionSize, Protect = (protect ?? Enumerable.Empty<ProtectedPath>()).Where(p => p?.Path != null).ToList() }, cancelled);

        public static RawDiskReader StartReading(RawDiskSession s, Func<bool> cancelled) { s.Go(); return new RawDiskReader(s, cancelled); }

        static NamedPipeServerStream NewServer(string name)
        {
            var ps = new PipeSecurity();
            ps.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.NetworkSid, null), PipeAccessRights.FullControl, AccessControlType.Deny));
            ps.AddAccessRule(new PipeAccessRule(WindowsIdentity.GetCurrent().User, PipeAccessRights.FullControl, AccessControlType.Allow));
            ps.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null), PipeAccessRights.ReadWrite | PipeAccessRights.Synchronize, AccessControlType.Allow));
            ps.SetAccessRuleProtection(true, false);
            return new NamedPipeServerStream(name, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 1 << 20, 1 << 20, ps);
        }

        /// <summary>the next message from the step within ms, or null (the step gone, ended, or silent that long)</summary>
        static (byte type, byte[] payload)? Next(RawDiskSession s, int ms, Func<bool> cancelled)
        {
            var until = DateTime.UtcNow.AddMilliseconds(ms);
            while (DateTime.UtcNow < until)
            {
                var f = s.Channel.Take(250);
                if (f != null) return f;
                if (s.Channel.Ended || cancelled()) return null;
            }
            return null;
        }

        internal static RawDiskSession Open(RawDiskRequest req, Func<bool> cancelled)
        {
            var s = new RawDiskSession(); cancelled = cancelled ?? (() => false);
            req.OperationId = Guid.NewGuid().ToString("N"); req.Nonce = Hex.Of(RawFrame.Random32()); req.Pipe = "ccs-raw-disk-" + Guid.NewGuid().ToString("N");
            req.WindowPid = System.Diagnostics.Process.GetCurrentProcess().Id; req.WindowProgram = Program(); req.CreatedUtc = DateTime.UtcNow;
            var problems = req.Problems(); if (problems.Count > 0) { s.Refusal = "Workbench could not make a usable request for its administrator step (" + string.Join("; ", problems) + ")."; return s; }
            IRawDiskLauncher launcher;
            try { launcher = Launcher(req.Kind); } catch (GateClosedException ex) { s.Refusal = ex.Text; return s; }
            string file, sha;
            try { s.Server = NewServer(req.Pipe); (file, sha) = RawDiskRequestFile.Write(req, TempRoot); s.RequestFolder = Path.GetDirectoryName(file); }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is InvalidOperationException) { s.Refusal = "Workbench could not prepare its administrator step (" + ex.Message + ")."; return s; }
            try { s.Process = launcher.Start(RawDiskHost.Flag + " \"" + file + "\" " + sha); }
            catch (ElevationCanceledException) { s.Declined = true; s.Refusal = DeclinedText(req.Kind); return s; }
            catch (Win32Exception ex) { s.Refusal = "Workbench could not start its administrator step: " + ex.Message; return s; }
            // the step's connection, from the process Windows started and no other
            var ar = s.Server.BeginWaitForConnection(null, null); var until = DateTime.UtcNow.AddSeconds(90);
            while (!ar.AsyncWaitHandle.WaitOne(250))
            {
                if (s.Process.HasExited) { s.ExitCode = s.Process.ExitCode; s.Refusal = RawDiskExit.Text(s.Process.ExitCode); s.Finished = true; return s; }
                if (cancelled()) { s.Canceled = true; s.Refusal = "Canceled before the drive was opened."; return s; }
                if (DateTime.UtcNow > until) { s.Refusal = "Workbench's administrator step did not answer, so nothing was read or written."; return s; }
            }
            try { s.Server.EndWaitForConnection(ar); } catch (IOException ex) { s.Refusal = "The connection to Workbench's administrator step failed (" + ex.Message + ")."; return s; }
            if (!N.GetNamedPipeClientProcessId(s.Server.SafePipeHandle, out int pid) || pid != s.Process.Id) { s.Refusal = "A program other than Workbench's administrator step answered, so nothing was read or written."; return s; }
            s.Channel = new FrameChannel(s.Server);
            var mine = RawFrame.Random32(); s.Channel.Send(RawFrame.Challenge, mine);
            var hello = Next(s, 30000, cancelled); var hj = hello?.type == RawFrame.Hello ? RawFrame.P(hello.Value.payload) : null;
            if (hj == null && cancelled()) { s.Canceled = true; s.Refusal = "Canceled before the drive was opened."; return s; }
            if (hj == null || hj.S("operation_id") != req.OperationId || !RawFrame.Same(hj.S("proof"), RawFrame.Proof(req.Nonce, "helper", req.OperationId, mine)) || !Hex.IsHex(hj.S("challenge"), 64))
            {
                s.Refusal = "Workbench could not confirm that its administrator step answered, so nothing was read or written.";
                if (hj == null && s.Process.WaitForExit(3000)) { s.ExitCode = s.Process.ExitCode; s.Refusal += " " + RawDiskExit.Text(s.Process.ExitCode); }
                return s;
            }
            var theirs = Enumerable.Range(0, 32).Select(i => Convert.ToByte(hj.S("challenge").Substring(2 * i, 2), 16)).ToArray();
            s.Channel.Send(RawFrame.Accept, RawFrame.J(new JObj { { "proof", RawFrame.Proof(req.Nonce, "window", req.OperationId, theirs) } }));
            var ready = Next(s, 180000, cancelled);
            if (ready?.type == RawFrame.Result)
            {
                var r = RawFrame.P(ready.Value.payload); s.Finished = true;
                s.Refusal = r?.S("text") ?? "Workbench's administrator step refused, so nothing was read or written."; s.RefusalKind = r?.S("refusal") ?? r?.S("state");
                return s;
            }
            if (ready?.type != RawFrame.Ready)
            {
                if (cancelled()) { s.Canceled = true; s.Refusal = "Canceled before the drive was opened."; return s; }
                s.Refusal = "Workbench's administrator step stopped before the drive was ready, so nothing was read or written."; return s;
            }
            var rj = RawFrame.P(ready.Value.payload);
            s.Identity = DiskIdentity.FromJson(rj?.O("identity")); s.Consistent = rj?.B("consistent") ?? false;
            foreach (var v in rj?.A("volumes") ?? new List<object>()) { var dv = DiskVolume.FromJson(v as Dictionary<string, object>); if (dv != null) s.Volumes.Add(dv); }
            foreach (var v in rj?.A("not_locked") ?? new List<object>()) if (v is string t) s.NotLocked.Add(t);
            var d = req.Target.Differs(s.Identity);
            if (d != null) { s.Refusal = "The drive the administrator step opened is not the one you chose: " + d + " is different. Nothing was read or written."; s.Cancel(); return s; }
            return s;
        }

        /// <summary>a whole drive image written onto the drive, read back by the step, and the result as it happened. beforeGo runs just
        /// before the go-ahead (the operation record says "writing" from then on).</summary>
        public static RawDiskWriteResult Write(DiskIdentity target, RawImagePlan image, IEnumerable<ProtectedPath> protect, Func<bool> cancelled, Action<string, long, long> progress, Action beforeGo)
        {
            var w = new RawDiskWriteResult(); cancelled = cancelled ?? (() => false);
            string Of(long n) => Engine.Units.Gb(n);
            using (var s = Open(new RawDiskRequest { Kind = "write", Target = target, Image = image, Protect = (protect ?? Enumerable.Empty<ProtectedPath>()).Where(p => p?.Path != null).ToList() }, cancelled))
            {
                w.ExitCode = s.ExitCode; w.Identity = s.Identity;
                if (s.Refusal != null)
                {
                    w.State = s.Declined ? "declined" : s.Canceled ? "canceled" : "refused";
                    w.Text = s.Refusal + (s.Refusal.Contains("Nothing was written") || s.Refusal.Contains("nothing was written") || s.Refusal.Contains("nothing was read or written") ? "" : " Nothing was written to the drive.");
                    return w;
                }
                if (cancelled()) { w.State = "canceled"; w.Text = "Canceled before anything was written. Nothing was written to the drive."; return w; }
                beforeGo?.Invoke();
                try { s.Go(); }
                catch (IOException)
                {   // the step ended as it was told to start: whether it began is not known, so the drive is not called unchanged
                    w.State = "interrupted"; w.Text = "Workbench's administrator step ended just as it was told to start writing. The drive may hold part of the image and cannot be relied on. Write the image again to finish.";
                    return w;
                }
                bool cancelSent = false;
                while (true)
                {
                    if (!cancelSent && cancelled()) { s.Cancel(); cancelSent = true; }
                    var f = s.Channel.Take(250);
                    if (f == null)
                    {
                        if (s.Channel.Ended)
                        {
                            if (s.Process.WaitForExit(5000)) w.ExitCode = s.Process.ExitCode;
                            w.State = "interrupted";
                            w.Text = "Workbench's administrator step ended before it said it had finished" + (w.Written > 0 ? " (it had written " + Of(w.Written) + " of " + Of(image.ImageBytes) + ")" : "") + ". Writing may have stopped part way: the drive may hold part of the image and part of what was there before, so it cannot be relied on. Write the image again to finish.";
                            return w;
                        }
                        continue;
                    }
                    if (f.Value.type == RawFrame.Progress)
                    {
                        var j = RawFrame.P(f.Value.payload); var phase = j?.S("phase");
                        if (phase == "write") w.Written = j.L("done", w.Written); else if (phase == "readback") w.Checked = j.L("done", w.Checked);
                        progress?.Invoke(phase, j?.L("done") ?? 0, j?.L("total") ?? 0);
                        continue;
                    }
                    if (f.Value.type != RawFrame.Result) continue;
                    var r = RawFrame.P(f.Value.payload); s.Finished = true;
                    var state = r?.S("state"); w.Written = r?.L("written", w.Written) ?? w.Written; w.Checked = r?.L("checked", w.Checked) ?? w.Checked;
                    foreach (var b in r?.A("bad_regions") ?? new List<object>()) w.BadRegions.Add(Convert.ToInt64(b, CultureInfo.InvariantCulture));
                    if (s.Process.WaitForExit(10000)) w.ExitCode = s.Process.ExitCode;
                    var why = r?.S("text") ?? "";
                    switch (state)
                    {
                        case "done": w.State = "complete"; w.Text = "The drive image was written and read back from the drive: every region matches the backup's recorded checksum."; break;
                        case "canceled" when w.Written == 0: w.State = "canceled"; w.Text = "Canceled before anything was written. Nothing was written to the drive."; break;
                        case "canceled":
                        case "canceled-incomplete": w.State = "canceled-incomplete"; w.Text = "Canceled while writing, after " + Of(w.Written) + " of " + Of(image.ImageBytes) + ". The drive now holds part of the image and part of what was there before, so it cannot be relied on. Write the image again to finish."; break;
                        case "stopped-incomplete": w.State = "stopped-incomplete"; w.Text = "Writing stopped after " + Of(w.Written) + " of " + Of(image.ImageBytes) + ": " + why.TrimEnd('.') + ". The drive holds part of the image and cannot be relied on. Nothing is retried."; break;
                        case "written-not-checked": w.State = "written-not-checked"; w.Text = why.TrimEnd('.') + ". Do not rely on the drive until the image is written again and read back in full."; break;
                        case "read-back-differs": w.State = "read-back-differs"; w.Text = "The image was written, but " + Engine.Units.Count(w.BadRegions.Count, "region") + " read back differently from the backup, so the drive cannot be relied on."; break;
                        default: w.State = "interrupted"; w.Text = "Workbench's administrator step stopped while writing (" + why.TrimEnd('.') + "). The drive may hold part of the image and cannot be relied on."; break;
                    }
                    return w;
                }
            }
        }
    }
}
