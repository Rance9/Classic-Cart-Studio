// Classic Cart Studio console backup and restore engine. Copyright (C) 2026 Rance9. GPL-3.0-or-later.
// The console as the engine sees it during a recovery session: the fixed ccsbr verbs (tools/console-backup/ram/ccsbr) over a console
// shell (clovershell on a real console; SimulatedConsole on the PC). The engine never builds shell text from anything but these verbs
// and a target name checked against the fixed list, and never sends a path or a device. A backup session cannot write: Write refuses
// before anything is sent unless the session was opened for a restore or a preparation, the matching boundary in Gate is open (or the
// console is simulated) and the operation's WriteAuthority admits this exact target. Every read is checked end to end: the bytes the PC
// received (count, MD5, SHA-256) against the console's own count and MD5 of the same pass, and the exit status of the source.
using CcsWorker.Shell;
using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace CcsBackup.Device
{
    public enum SessionProfile { Backup, Restore, Prepare, Rescue, Files }

    public sealed class StartupRead
    {
        public string State, Cmdline, Md5, Why;
        public bool Ok => State == "ok";
        /// <summary>the reason in plain words, after "The console's startup software ..."</summary>
        public string Text => State == "read-failed" ? "could not be read in full (" + Why + ")" : State == "size" ? "read back with a size no startup software has (" + Why + ")"
            : State == "header" ? "read back without a valid startup header (" + Why + ")" : "was read";
    }

    public sealed class ReadResult
    {
        public string Target; public long Bytes = -1; public string Sha256, Md5; public uint Crc;
        public string ConsoleMd5; public long ConsoleBytes = -1; public int SourceStatus = -1, TeeStatus = -1, ExitCode = -1; public long Expected = -1;
        public string Problem;
        public bool Ok => Problem == null;
    }

    public sealed class WriteResult { public string Target; public bool Ok; public bool Began; public string Line, Problem; public string ReadbackMd5; }

    /// <summary>What a restore or preparation has been admitted to write, and whether its boundary is open now. Revoke stops every later
    /// write (the first failure revokes it: there is never a second attempt in the same operation).</summary>
    public sealed class WriteAuthority
    {
        readonly string[] targets; readonly Func<bool> boundaryOpen; readonly string boundaryText;
        public readonly string PlanDigest, Hwid; public bool Revoked { get; private set; } public string RevokedWhy { get; private set; }
        public WriteAuthority(string planDigest, string hwid, string[] targets, Func<bool> boundaryOpen, string boundaryText)
        { PlanDigest = planDigest; Hwid = hwid; this.targets = targets ?? new string[0]; this.boundaryOpen = boundaryOpen; this.boundaryText = boundaryText; }
        public void Revoke(string why) { if (!Revoked) { Revoked = true; RevokedWhy = why; } }
        public void Require(string target, string hwid)
        {
            if (boundaryOpen == null || !boundaryOpen()) throw new GateClosedException(boundaryText ?? Gate.RestoreText, "write " + target);
            if (Revoked) throw new InvalidOperationException("Writing stopped after an earlier problem (" + RevokedWhy + "); nothing more is written in this operation.");
            if (!targets.Contains(target)) throw new InvalidOperationException("The reviewed plan does not include writing " + target + ".");
            if (!ConsoleIdentity.SameConsole(hwid, Hwid)) throw new InvalidOperationException("The connected console is not the one the plan was reviewed for.");
        }
    }

    public sealed class RecoveryConsole
    {
        public const string Helper = "/ccs/bin/ccsbr";
        public static readonly string[] Targets = { "boot0", "boot1", "boot2", "boot2-68", "nand", "nanda", "nandb", "nandc", "nandd", "nande", "phy", "parttable" };
        public static readonly string[] WriteTargets = { "boot2", "boot2-68", "nanda", "nandb", "nandc", "nandd", "nande" };
        public static readonly string[] Cards = { "starting", "connected", "reading", "writing", "done", "stopped", "restarting" };
        public const int ReadSilenceMs = 120000, WriteSilenceMs = 600000;

        readonly IConsoleShell shell;
        public readonly SessionProfile Profile;
        public readonly bool Simulated;
        /// <summary>this connection to the console program; a reconnection is a new session (what a plan captured belongs to its session)</summary>
        public readonly string SessionId = Guid.NewGuid().ToString("N");
        public string Hwid { get; private set; }

        public RecoveryConsole(IConsoleShell shell, SessionProfile profile, bool simulated)
        { this.shell = shell ?? throw new ArgumentNullException(nameof(shell)); Profile = profile; Simulated = simulated; }

        public string Kind => shell.Kind;
        public bool IsOnline => shell.IsOnline;

        static void CheckTarget(string t, string[] list) { if (!list.Contains(t)) throw new ArgumentException("not a console target: " + t); }

        string Text(string verb, string arg, int silenceMs, out int code)
        {
            var o = new MemoryStream(); var e = new MemoryStream();
            code = shell.Execute(Helper + " " + verb + (arg == null ? "" : " " + arg), null, o, e, silenceMs);
            if (o.Length > 1000000) throw new IOException("the console's answer was unexpectedly large");
            return Encoding.UTF8.GetString(o.ToArray());
        }

        public ConsoleIdentity Identify()
        {
            var t = Text("identify", null, ReadSilenceMs, out var code);
            if (code != 0) throw new ConsoleRefusedException("unreadable", "The console's identity could not be read (status " + code + ").");
            var id = ConsoleIdentity.Parse(t); Hwid = id.Hwid; return id;
        }

        /// <summary>Streams one target into sink (hashing it on the way); then compares the PC's count and MD5 with the console's.</summary>
        public ReadResult Read(string target, Stream sink, long expected, Action<long> progress, Func<bool> cancelled)
        {
            CheckTarget(target, Targets);
            var r = new ReadResult { Target = target, Expected = expected };
            var h = new HashingSink(sink, progress, cancelled); var err = new MemoryStream();
            try { r.ExitCode = shell.Execute(Helper + " read " + target, null, h, err, ReadSilenceMs); }
            catch (ShellLostException) when (cancelled != null && cancelled()) { throw new OperationCanceledException("canceled"); }
            h.Finish(); r.Bytes = h.Count; r.Sha256 = h.Sha256; r.Md5 = h.Md5; r.Crc = h.Crc;
            var sum = Text("sum", target, ReadSilenceMs, out var sc);
            var m = Regex.Match(sum.Trim(), "^([0-9a-f]{32}|none) ([0-9]{1,15}) ([0-9]{1,3}) ([0-9]{1,3})$");
            if (sc != 0 || !m.Success) { r.Problem = "the console did not report its own check of " + target; return r; }
            r.ConsoleMd5 = m.Groups[1].Value; r.ConsoleBytes = long.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
            r.SourceStatus = int.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture); r.TeeStatus = int.Parse(m.Groups[4].Value, CultureInfo.InvariantCulture);
            if (r.ExitCode != 0 || r.SourceStatus != 0 || r.TeeStatus != 0) r.Problem = "the console reported a read error for " + target + " (status " + r.ExitCode + "/" + r.SourceStatus + "/" + r.TeeStatus + ")";
            else if (r.ConsoleBytes != r.Bytes) r.Problem = "the PC received " + r.Bytes + " bytes of " + target + " but the console sent " + r.ConsoleBytes;
            else if (r.ConsoleMd5 != r.Md5) r.Problem = "the bytes of " + target + " the PC received differ from what the console read";
            else if (expected >= 0 && r.Bytes != expected) r.Problem = target + " has " + r.Bytes + " bytes; " + expected + " were expected";
            return r;
        }

        public static readonly string[] RangeTargets = { "nanda", "nandb", "nandc", "nandd", "nande" };
        public const int RangeMax = 16 * 1024 * 1024;

        /// <summary>Console Browser: length bytes of a logical storage part from offset (both multiples of 4096, at most 16,777,216 bytes),
        /// checked end to end as a whole read is (the console's own count and MD5 of the same pass). Reads only, in any session.</summary>
        public ReadResult ReadRange(string target, long offset, int length, Stream sink, Func<bool> cancelled)
        {
            CheckTarget(target, RangeTargets);
            if (offset < 0 || offset % 4096 != 0 || length <= 0 || length % 4096 != 0 || length > RangeMax) throw new ArgumentException("bad range");
            var r = new ReadResult { Target = target, Expected = length };
            var h = new HashingSink(sink, null, cancelled); var err = new MemoryStream();
            r.ExitCode = shell.Execute(Helper + " range " + target + " " + offset.ToString(CultureInfo.InvariantCulture) + " " + length.ToString(CultureInfo.InvariantCulture), null, h, err, ReadSilenceMs);
            h.Finish(); r.Bytes = h.Count; r.Sha256 = h.Sha256; r.Md5 = h.Md5;
            var sum = Text("sum", target, ReadSilenceMs, out var sc);
            var m = Regex.Match(sum.Trim(), "^([0-9a-f]{32}|none) ([0-9]{1,15}) ([0-9]{1,3}) ([0-9]{1,3})$");
            if (sc != 0 || !m.Success) { r.Problem = "the console did not report its own check of " + target; return r; }
            r.ConsoleMd5 = m.Groups[1].Value; r.ConsoleBytes = long.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
            r.SourceStatus = int.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture); r.TeeStatus = int.Parse(m.Groups[4].Value, CultureInfo.InvariantCulture);
            if (r.ExitCode != 0 || r.SourceStatus != 0 || r.TeeStatus != 0) r.Problem = "the console reported a read error for " + target;
            else if (r.ConsoleBytes != r.Bytes || r.ConsoleMd5 != r.Md5) r.Problem = "the bytes of " + target + " the PC received differ from what the console read";
            else if (r.Bytes > length) r.Problem = "more bytes than asked for";
            return r;
        }

        /// <summary>The console reads the target again on its own (nothing is sent): its MD5 and byte count.</summary>
        public (string md5, long bytes, bool ok) Digest(string target)
        {
            CheckTarget(target, Targets);
            var t = Text("digest", target, WriteSilenceMs, out var code).Trim().Split('\n').LastOrDefault() ?? "";
            var m = Regex.Match(t.Trim(), "^([0-9a-f]{32}|none) ([0-9]{1,15}) ([0-9]{1,3}) ([0-9]{1,3})$");
            if (!m.Success) return (null, -1, false);
            return (m.Groups[1].Value, long.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture), code == 0 && m.Groups[3].Value == "0" && m.Groups[4].Value == "0");
        }

        /// <summary>Writes exactly size bytes of source onto target (restore and preparation sessions only); the console checks what it
        /// received, writes once and reads the target back. Any failure revokes the authority: nothing more is written.</summary>
        public WriteResult Write(string target, Stream source, long size, string md5, WriteAuthority authority)
        {
            if (Profile != SessionProfile.Restore && Profile != SessionProfile.Prepare) throw new InvalidOperationException("This session cannot write to the console's storage parts.");
            CheckTarget(target, WriteTargets);
            if (authority == null) throw new InvalidOperationException("No write was admitted.");
            authority.Require(target, Hwid);
            if (!Hex.IsHex(md5, 32) || size < 0) throw new ArgumentException("bad write request");
            var wr = new WriteResult { Target = target };
            var o = new MemoryStream(); var e = new MemoryStream();
            int code;
            try { code = shell.Execute(Helper + " write " + target + " " + size.ToString(CultureInfo.InvariantCulture) + " " + md5, new LimitedStream(source, size), o, e, WriteSilenceMs); }
            catch (Exception ex) { authority.Revoke(target + ": " + ex.Message); wr.Problem = "the connection ended while " + target + " was being written: " + ex.Message; wr.Began = true; return wr; }
            var lines = Encoding.UTF8.GetString(o.ToArray()).Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
            wr.Began = lines.Any(l => l == "began " + target);
            wr.Line = lines.LastOrDefault(l => l.StartsWith("result " + target + " "));
            var m = Regex.Match(wr.Line ?? "", "^result " + Regex.Escape(target) + " ok ([0-9a-f]{32}) ([0-9]+)$");
            wr.Ok = code == 0 && m.Success && m.Groups[1].Value == md5 && m.Groups[2].Value == size.ToString(CultureInfo.InvariantCulture);
            if (wr.Ok) wr.ReadbackMd5 = m.Groups[1].Value;
            else { wr.Problem = wr.Line ?? ("no result from the console (status " + code + ")"); authority.Revoke(target + ": " + wr.Problem); }
            return wr;
        }

        static string Md5Hex(byte[] b) { using (var h = System.Security.Cryptography.MD5.Create()) return Hex.Of(h.ComputeHash(b)); }

        /// <summary>the arguments each ccsprep step takes: MD5s the PC reviewed (m), byte counts (n), a CCS install-method package name (p)
        /// and its SHA-256 (s), a CCS HOME copy name (c) and the copy it replaces (q: a copy name or "absent"), on or off (o: CCS Mod's start at
        /// power-on, mstart); ret-stage also sends exactly
        /// that many bytes of input (the console's own bootloader from its snapshot)</summary>
        static readonly System.Collections.Generic.Dictionary<string, string> StepArgs = new System.Collections.Generic.Dictionary<string, string>
        { { "files", "" }, { "uboot", "" }, { "slot68", "" }, { "kernel", "" }, { "ret-stage", "nm" }, { "ret-kernel", "m" }, { "ret-uboot", "mm" }, { "ret-files", "" }, { "ret-table", "" }, { "method", "ps" }, { "unlist", "p" }, { "rput", "ins" }, { "rimport", "isnb" }, { "rdrop", "is" },
          { "hput", "cnsn" }, { "huse", "cq" }, { "mstart", "o" } };

        /// <summary>One step of ram/ccsprep (preparation sessions only): a first-time preparation step, or a Return to Original Software
        /// step with the MD5s the PC reviewed (ret-kernel: what it replaces; ret-uboot: what it replaces and what it writes; ret-stage: the
        /// size and MD5 of input); admitted by the authority for this console.</summary>
        public WriteResult PrepStep(string step, WriteAuthority authority, string[] args = null, byte[] input = null)
        {
            if (Profile != SessionProfile.Prepare) throw new InvalidOperationException("Only a preparation session prepares a console.");
            if (!Engine.Preparation.Order.Contains(step) && !Engine.ReturnEngine.Order.Contains(step) && !Engine.Methods.Steps.Contains(step) && !Engine.FirmwareEngine.Steps.Contains(step) && !Engine.HomePackage.Steps.Contains(step) || !StepArgs.TryGetValue(step, out var shape)) throw new ArgumentException("not a preparation step: " + step);
            args = args ?? new string[0];
            if (!ArgsOk(shape, args)) throw new ArgumentException("bad step request");
            if ((step == "ret-stage") != (input != null) || input != null && (input.LongLength.ToString(CultureInfo.InvariantCulture) != args[0] || Md5Hex(input) != args[1])) throw new ArgumentException("bad step input");
            if (step == "rput" || step == "hput") throw new ArgumentException(step + " takes its bytes from a stream (PrepStepStream)");
            return PrepRun(step, authority, args, input != null ? new MemoryStream(input, false) : null);
        }

        static bool ArgsOk(string shape, string[] args)
        {
            if (args.Length != shape.Length) return false;
            for (int i = 0; i < args.Length; i++)
                if (shape[i] == 'm' ? !Hex.IsHex(args[i], 32) : shape[i] == 's' ? !Hex.IsHex(args[i], 64) : shape[i] == 'p' ? !Engine.Methods.IsPackage(args[i])
                    : shape[i] == 'i' ? !Engine.FirmwareEngine.IdOk(args[i]) : shape[i] == 'b' ? !Regex.IsMatch(args[i] ?? "", "^[A-Za-z0-9+/]{4,120}={0,2}$")
                    : shape[i] == 'c' ? !Engine.HomePackage.CopyRe.IsMatch(args[i] ?? "") : shape[i] == 'q' ? !(args[i] == "absent" || Engine.HomePackage.CopyRe.IsMatch(args[i] ?? "")) : shape[i] == 'o' ? !(args[i] == "on" || args[i] == "off")
                    : !Regex.IsMatch(args[i] ?? "", "^[1-9][0-9]{0,8}$")) return false;
            return true;
        }

        /// <summary>A ccsprep step whose input is exactly size bytes of a stream, its size and SHA-256 among the arguments (the console checks
        /// both; the PC checked the stream's bytes when it bound them): rput (an owner's system image: ID SIZE SHA) or hput (CCS HOME's package
        /// archive: COPY SIZE SHA BYTES)</summary>
        public WriteResult PrepStepStream(string step, WriteAuthority authority, string[] args, Stream input, long size)
        {
            if (Profile != SessionProfile.Prepare) throw new InvalidOperationException("Only a preparation session prepares a console.");
            if (step != "rput" && step != "hput" || args == null || input == null || !ArgsOk(StepArgs[step], args) || args[1] != size.ToString(CultureInfo.InvariantCulture)) throw new ArgumentException("bad step request");
            return PrepRun(step, authority, args, new LimitedStream(input, size));
        }

        WriteResult PrepRun(string step, WriteAuthority authority, string[] args, Stream input)
        {
            if (authority == null) throw new InvalidOperationException("No preparation was admitted.");
            authority.Require(step, Hwid);
            var wr = new WriteResult { Target = step }; var o = new MemoryStream(); var e = new MemoryStream(); int code;
            try { code = shell.Execute("/ccs/bin/ccsprep " + step + (args.Length > 0 ? " " + string.Join(" ", args) : ""), input, o, e, WriteSilenceMs); }
            catch (Exception ex) { authority.Revoke(step + ": " + ex.Message); wr.Began = true; wr.Problem = "the connection ended during " + step + ": " + ex.Message; return wr; }
            var lines = Encoding.UTF8.GetString(o.ToArray()).Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
            wr.Began = lines.Any(l => l == "began " + step);
            wr.Line = lines.LastOrDefault(l => l.StartsWith("result " + step + " "));
            wr.Ok = code == 0 && wr.Line != null && Regex.IsMatch(wr.Line, "^result " + Regex.Escape(step) + " ok( |$)");
            if (!wr.Ok) { wr.Problem = wr.Line != null ? wr.Line.Substring(("result " + step + " ").Length) : "no result from the console (status " + code + ")"; authority.Revoke(step + ": " + wr.Problem); }
            return wr;
        }

        /// <summary>ccsprep mstate (preparation sessions only; reads only, the data storage mounted read-only without journal replay): hakchi's
        /// version, the hakchi packages installed, Classic Cart Studio's method in charge and what waits in hakchi's transfer folder</summary>
        public Engine.MethodState PrepState()
        {
            if (Profile != SessionProfile.Prepare) throw new InvalidOperationException("Only a preparation session reads the console's hakchi packages.");
            var o = new MemoryStream(); var e = new MemoryStream();
            var code = shell.Execute("/ccs/bin/ccsprep mstate", null, o, e, ReadSilenceMs);
            if (o.Length > 1000000) throw new IOException("the console's answer was unexpectedly large");
            return Engine.MethodState.Parse(Encoding.UTF8.GetString(o.ToArray()), code);
        }

        /// <summary>Console Rescue's read-only diagnosis (ram/ccsrescue diag): each boot stage's MD5 or ccsboot's typed result (invalid, noimage,
        /// unreadable, nodriver); the flash tool's MD5 or "damaged"</summary>
        public System.Collections.Generic.Dictionary<string, string> RescueDiagnose()
        {
            if (Profile != SessionProfile.Rescue) throw new InvalidOperationException("Only a rescue session diagnoses the startup stages.");
            var o = new MemoryStream(); var e = new MemoryStream();
            int code = shell.Execute("/ccs/bin/ccsrescue diag", null, o, e, ReadSilenceMs);
            var d = new System.Collections.Generic.Dictionary<string, string>();
            foreach (var l in Encoding.UTF8.GetString(o.ToArray()).Split('\n').Select(x => x.Trim()))
            { var m = Regex.Match(l, "^(boot0|boot0_size|boot1|boot1_flash_tool|boot1_size)=([0-9a-f]{32}|invalid|noimage|unreadable|nodriver|damaged|[0-9]{1,9})$"); if (m.Success) d[m.Groups[1].Value] = m.Groups[2].Value; }
            if (code != 0 || !d.ContainsKey("boot0") || !d.ContainsKey("boot1") || !d.ContainsKey("boot1_flash_tool")) throw new ConsoleRefusedException("unreadable", "The console's startup stages could not be checked (status " + code + ").");
            return d;
        }

        static readonly System.Collections.Generic.Dictionary<string, string> RescueArgs = new System.Collections.Generic.Dictionary<string, string> { { "stage0", "nm" }, { "stage1", "nm" }, { "burn0", "dm" }, { "burn1", "dm" } };

        /// <summary>One step of ram/ccsrescue (rescue sessions only): stage0/stage1 (SIZE MD5 + exactly that input: this console's own boot stage
        /// from its verified backup) or burn0/burn1 (NOW WANT: NOW an MD5 or the typed word invalid, noimage or unreadable, never nodriver);
        /// admitted by the authority for this console.</summary>
        public WriteResult RescueStep(string step, WriteAuthority authority, string[] args, byte[] input = null)
        {
            if (Profile != SessionProfile.Rescue) throw new InvalidOperationException("Only a rescue session repairs startup stages.");
            if (!RescueArgs.TryGetValue(step, out var shape) || args == null || args.Length != shape.Length) throw new ArgumentException("bad rescue step");
            for (int i = 0; i < args.Length; i++)
                if (shape[i] == 'm' ? !Hex.IsHex(args[i], 32) : shape[i] == 'd' ? !(Hex.IsHex(args[i], 32) || Engine.RescueDiagnosis.DamageWords.Contains(args[i])) : !Regex.IsMatch(args[i] ?? "", "^[1-9][0-9]{0,6}$")) throw new ArgumentException("bad rescue step");
            if (step.StartsWith("stage") != (input != null) || input != null && (input.LongLength.ToString(CultureInfo.InvariantCulture) != args[0] || Md5Hex(input) != args[1])) throw new ArgumentException("bad rescue input");
            if (authority == null) throw new InvalidOperationException("No rescue was admitted.");
            authority.Require(step, Hwid);
            var wr = new WriteResult { Target = step }; var o = new MemoryStream(); var e = new MemoryStream(); int code;
            try { code = shell.Execute("/ccs/bin/ccsrescue " + step + " " + string.Join(" ", args), input != null ? new MemoryStream(input, false) : null, o, e, WriteSilenceMs); }
            catch (Exception ex) { authority.Revoke(step + ": " + ex.Message); wr.Began = true; wr.Problem = "the connection ended during " + step + ": " + ex.Message; return wr; }
            var lines = Encoding.UTF8.GetString(o.ToArray()).Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
            wr.Began = lines.Any(l => l == "began " + step);
            wr.Line = lines.LastOrDefault(l => l.StartsWith("result " + step + " "));
            wr.Ok = code == 0 && wr.Line != null && Regex.IsMatch(wr.Line, "^result " + Regex.Escape(step) + " ok( |$)");
            if (!wr.Ok) { wr.Problem = wr.Line != null ? wr.Line.Substring(("result " + step + " ").Length) : "no result from the console (status " + code + ")"; authority.Revoke(step + ": " + wr.Problem); }
            return wr;
        }

        /// <summary>ram/ccsfiles stat (files sessions; read-only): the file's SHA-256, "absent", "folder", "link" or "special", and its size</summary>
        public (string state, long bytes) FileStat(string scope, string path)
        {
            if (Profile != SessionProfile.Files) throw new InvalidOperationException("Only a file session reads file states.");
            if ((scope != "user" && scope != "dev" && scope != "compose") || !Engine.FilesEngine.PathOk(path)) throw new ArgumentException("bad file request");
            var o = new MemoryStream(); var e = new MemoryStream();
            int code = shell.Execute("/ccs/bin/ccsfiles stat " + scope + " " + Engine.FilesEngine.Quote(path), null, o, e, ReadSilenceMs);
            var m = Regex.Match(Encoding.UTF8.GetString(o.ToArray()), "result stat ok ([0-9a-f]{64}|absent|folder|link|special) ([0-9]{1,12})");
            if (code != 0 || !m.Success) throw new ConsoleRefusedException("unreadable", "The file's state could not be read (status " + code + ").");
            return (m.Groups[1].Value, long.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture));
        }

        /// <summary>ram/ccsfiles put/del (files sessions): one checked file change, admitted by the authority (its targets are the plan's
        /// change ids "put:PATH" / "del:PATH")</summary>
        public WriteResult FileStep(string op, string scope, string path, string sha256, string oldState, byte[] input, WriteAuthority authority)
        {
            if (Profile != SessionProfile.Files) throw new InvalidOperationException("Only a file session changes files.");
            if ((op != "put" && op != "del") || (scope != "user" && scope != "dev" && scope != "compose") || !Engine.FilesEngine.PathOk(path)) throw new ArgumentException("bad file request");
            if (op == "put" && (!Hex.IsHex(sha256, 64) || input == null || !(oldState == "absent" || Hex.IsHex(oldState, 64)))) throw new ArgumentException("bad file request");
            if (op == "del" && (input != null || !Hex.IsHex(oldState, 64))) throw new ArgumentException("bad file request");
            if (authority == null) throw new InvalidOperationException("No file change was admitted.");
            authority.Require(op + ":" + path, Hwid);
            var cmd = "/ccs/bin/ccsfiles " + op + " " + scope + " " + Engine.FilesEngine.Quote(path) + (op == "put" ? " " + input.LongLength.ToString(CultureInfo.InvariantCulture) + " " + sha256 + " " + oldState : " " + oldState);
            var wr = new WriteResult { Target = op + ":" + path }; var o = new MemoryStream(); var e = new MemoryStream(); int code;
            try { code = shell.Execute(cmd, input != null ? new MemoryStream(input, false) : null, o, e, WriteSilenceMs); }
            catch (Exception ex) { authority.Revoke(op + ": " + ex.Message); wr.Began = true; wr.Problem = "the connection ended during the change: " + ex.Message; return wr; }
            var lines = Encoding.UTF8.GetString(o.ToArray()).Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
            wr.Began = lines.Any(l => l == "began " + op);
            wr.Line = lines.LastOrDefault(l => l.StartsWith("result " + op + " "));
            wr.Ok = code == 0 && wr.Line != null && wr.Line == "result " + op + " ok " + (op == "put" ? sha256 : "absent");
            if (!wr.Ok) { wr.Problem = wr.Line != null ? wr.Line.Substring(("result " + op + " ").Length) : "no result from the console (status " + code + ")"; authority.Revoke(op + ": " + wr.Problem); }
            return wr;
        }

        /// <summary>the startup software's command line, read from the console's own startup image (reads only); null when it cannot be read</summary>
        /// <summary>the startup software (boot2) read whole and its header checked, with what went wrong kept apart: "ok" (Cmdline, Md5),
        /// "read-failed" (the read itself: the connection or the console's reader), "size" or "header" (the bytes read are not a startup image)</summary>
        public StartupRead ReadStartup()
        {
            var ms = new MemoryStream(); ReadResult r;
            try { r = Read("boot2", ms, -1, null, null); } catch (Exception ex) { return new StartupRead { State = "read-failed", Why = ex.Message }; }
            if (!r.Ok) return new StartupRead { State = "read-failed", Why = r.Problem };
            if (ms.Length < 1632 || ms.Length > 16_000_000) return new StartupRead { State = "size", Why = ms.Length.ToString("N0", CultureInfo.InvariantCulture) + " bytes" };
            var b = ms.ToArray(); var (_, cmd, why) = Engine.DeveloperEngine.BootHeader(b);
            if (why != null) return new StartupRead { State = "header", Why = why };
            return new StartupRead { State = "ok", Cmdline = cmd, Md5 = Md5Hex(b) };
        }

        public string ReadStartupCmdline() { var s = ReadStartup(); return s.Ok ? s.Cmdline : null; }

        /// <summary>ram/ccsdev (files sessions, Developer Mode): kstage SIZE MD5 + exactly that input, or kburn NOW WANT</summary>
        public WriteResult DevStep(string step, WriteAuthority authority, string[] args, byte[] input = null)
        {
            if (Profile != SessionProfile.Files) throw new InvalidOperationException("Only a file session changes the startup software.");
            if ((step != "kstage" && step != "kburn") || args == null || args.Length != 2) throw new ArgumentException("bad developer step");
            if (step == "kstage" && (!Regex.IsMatch(args[0], "^[1-9][0-9]{0,7}$") || !Hex.IsHex(args[1], 32) || input == null || input.LongLength.ToString(CultureInfo.InvariantCulture) != args[0] || Md5Hex(input) != args[1])) throw new ArgumentException("bad developer step");
            if (step == "kburn" && (!Hex.IsHex(args[0], 32) || !Hex.IsHex(args[1], 32) || input != null)) throw new ArgumentException("bad developer step");
            if (authority == null) throw new InvalidOperationException("No developer change was admitted.");
            authority.Require(step, Hwid);
            var wr = new WriteResult { Target = step }; var o = new MemoryStream(); var e = new MemoryStream(); int code;
            try { code = shell.Execute("/ccs/bin/ccsdev " + step + " " + args[0] + " " + args[1], input != null ? new MemoryStream(input, false) : null, o, e, WriteSilenceMs); }
            catch (Exception ex) { authority.Revoke(step + ": " + ex.Message); wr.Began = true; wr.Problem = "the connection ended during " + step + ": " + ex.Message; return wr; }
            var lines = Encoding.UTF8.GetString(o.ToArray()).Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
            wr.Began = lines.Any(l => l == "began " + step);
            wr.Line = lines.LastOrDefault(l => l.StartsWith("result " + step + " "));
            wr.Ok = code == 0 && wr.Line != null && Regex.IsMatch(wr.Line, "^result " + step + " ok( |$)");
            if (!wr.Ok) { wr.Problem = wr.Line != null ? wr.Line.Substring(("result " + step + " ").Length) : "no result from the console (status " + code + ")"; authority.Revoke(step + ": " + wr.Problem); }
            return wr;
        }

        /// <summary>ram/ccsdev sinfo (files sessions, Developer Mode; reads only): the console's system part as its own startup opens it:
        /// system (crypt, plain or none), key (yes/no), part_bytes, comp and bytes_used (its SquashFS superblock), board, mem_free</summary>
        public System.Collections.Generic.Dictionary<string, string> DevSystemInfo()
        {
            if (Profile != SessionProfile.Files) throw new InvalidOperationException("Only a file session reads the system part.");
            var o = new MemoryStream(); var e = new MemoryStream();
            int code = shell.Execute("/ccs/bin/ccsdev sinfo", null, o, e, WriteSilenceMs);
            if (o.Length > 100000) throw new IOException("the console's answer was unexpectedly large");
            var d = new System.Collections.Generic.Dictionary<string, string>(); bool ok = false;
            foreach (var l in Encoding.UTF8.GetString(o.ToArray()).Split('\n').Select(x => x.Trim()))
            {
                if (l == "result sinfo ok") ok = true;
                var m = Regex.Match(l, "^(system|key|part_bytes|comp|bytes_used|board|mem_free)=([A-Za-z0-9_.-]{0,40})$"); if (m.Success && !d.ContainsKey(m.Groups[1].Value)) d[m.Groups[1].Value] = m.Groups[2].Value;
            }
            if (code != 0 || !ok || !d.ContainsKey("system")) throw new ConsoleRefusedException("unreadable", "The console's system part could not be checked (status " + code + ").");
            return d;
        }

        /// <summary>ram/ccsdev sstage SIZE SHA256 (exactly SIZE bytes of input: a system image kept in the console's RAM and checked there) or
        /// sburn NOWMD5 SHA256 (the staged image written once through the console's own system-part mapping, read back); admitted by the
        /// authority for this console</summary>
        public WriteResult DevSystemStep(string step, WriteAuthority authority, string[] args, Stream input, long size)
        {
            if (Profile != SessionProfile.Files) throw new InvalidOperationException("Only a file session changes the system part.");
            if (step == "sstage" ? args == null || args.Length != 2 || args[0] != size.ToString(CultureInfo.InvariantCulture) || !Regex.IsMatch(args[0], "^[1-9][0-9]{0,8}$") || !Hex.IsHex(args[1], 64) || input == null
                : step != "sburn" || args == null || args.Length != 2 || !Hex.IsHex(args[0], 32) || !Hex.IsHex(args[1], 64) || input != null) throw new ArgumentException("bad developer step");
            if (authority == null) throw new InvalidOperationException("No developer change was admitted.");
            authority.Require(step, Hwid);
            var wr = new WriteResult { Target = step }; var o = new MemoryStream(); var e = new MemoryStream(); int code;
            try { code = shell.Execute("/ccs/bin/ccsdev " + step + " " + args[0] + " " + args[1], input != null ? new LimitedStream(input, size) : null, o, e, WriteSilenceMs); }
            catch (Exception ex) { authority.Revoke(step + ": " + ex.Message); wr.Began = true; wr.Problem = "the connection ended during " + step + ": " + ex.Message; return wr; }
            var lines = Encoding.UTF8.GetString(o.ToArray()).Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
            wr.Began = lines.Any(l => l == "began " + step);
            wr.Line = lines.LastOrDefault(l => l.StartsWith("result " + step + " "));
            wr.Ok = code == 0 && wr.Line != null && Regex.IsMatch(wr.Line, "^result " + step + " ok " + args[1] + "( |$)");
            if (!wr.Ok) { wr.Problem = wr.Line != null ? wr.Line.Substring(("result " + step + " ").Length) : "no result from the console (status " + code + ")"; authority.Revoke(step + ": " + wr.Problem); }
            return wr;
        }

        public void Card(string name) { if (!Cards.Contains(name)) return; try { Text("card", name, 10000, out _); } catch { } }

        /// <summary>The console restarts into its normal start a few seconds later (nothing is written).</summary>
        public bool Finish() { try { var t = Text("finish", null, 10000, out var code); return code == 0 && t.Contains("restarting"); } catch { return false; } }

        public void Disconnect() { try { shell.Disconnect(); } catch { } }

        sealed class LimitedStream : Stream
        {
            readonly Stream inner; long left;
            public LimitedStream(Stream i, long n) { inner = i; left = n; }
            public override int Read(byte[] b, int o, int c)
            {
                if (left <= 0) return 0;
                int n = inner.Read(b, o, (int)Math.Min(c, left));
                if (n <= 0) throw new EndOfStreamException("the backup member ended early");
                left -= n; return n;
            }
            public override bool CanRead => true; public override bool CanSeek => false; public override bool CanWrite => false;
            public override long Length => throw new NotSupportedException(); public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
            public override void Flush() { }
            public override long Seek(long o, SeekOrigin s) => throw new NotSupportedException();
            public override void SetLength(long v) => throw new NotSupportedException();
            public override void Write(byte[] b, int o, int c) => throw new NotSupportedException();
        }
    }
}
