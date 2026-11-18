// Classic Cart Studio console backup and restore engine. Copyright (C) 2026 Rance9. GPL-3.0-or-later.
// The controlled worker: the same executable as the window (the Utility, or Classic Cart Studio), started with --backup-worker, runs ONE
// console operation and reports to the window over its standard input and output (one JSON object per line). It is a separate process so
// that the window closing, hanging or crashing does not end an operation at an unsafe point:
//   - the window closes normally: it asks first, then sends "cancel"; the worker stops at the next safe boundary (a backup at once: it
//     only reads; a restore after the part being written has been written and read back) and reports where it stopped;
//   - the window ends without asking (crash, Task Manager): the worker sees its input end and does the same as for "cancel";
//   - the worker itself ends (crash, Task Manager): nothing it was writing on the PC is ever trusted afterwards (CaptureEngine's order),
//     and a console read by a backup was never written.
// It is not a service and leaves nothing running: it exits when its operation has ended. It takes the console lock for the operation.
//   requests  {"cmd":"backup","console":"real"|"simulated","folder":…}      {"cmd":"restore-prepare","backup":…,"console":…,"folder":…}
//             {"cmd":"restore-confirm","digest":…,"nonce":…}   {"cmd":"cancel"}   {"cmd":"finish"} (restart the console)   {"cmd":"quit"}
//             {"cmd":"prepare-plan","policy":"mod"|"nucleus"|"none",…} then "prepare-confirm"; {"cmd":"return-plan",…} then "return-confirm"
//             (CCS Console Workbench: the same functions in the standalone and in Classic Cart Studio, from the same engine and console programs)
//             {"cmd":"method-plan","op":"remove"|"change","method":"mod"|"nucleus",…} then {"cmd":"method-confirm","digest":…,"nonce":…}
//             (prepare-plan with a method and method-plan change also take "home": a CCS HOME package folder; default: the one this build carries;
//             with CCS Mod, "mod_start": true is the owner's choice of CCS HOME at power-on, off when absent)
//             (Console Restore: remove CCS Mod or CCS Nucleus, or change to the named one, through hakchi's transfer folder; MethodEngine)
//             {"cmd":"firmware-plan","op":"add","file":LOCAL .hsqs,"label":…,"id":… (optional)} or {"cmd":"firmware-plan","op":"remove","id":…}
//             then {"cmd":"firmware-confirm","digest":…,"nonce":…} (Console Composer's Browse for Firmware: a CCS Nucleus choice; FirmwareEngine)
//             {"cmd":"rescue",…} then {"cmd":"rescue-choose","backup":…,"stages":[…],"ack":…} then "rescue-confirm" (Console Rescue)
//             {"cmd":"files","scope":"user"|"dev",…} then any of {"cmd":"restore-point","backup":…} {"cmd":"stat","path":…}
//             {"cmd":"files-plan","changes":[{"op":"put","path":…,"from":LOCAL FILE}|{"op":"del","path":…}]} {"cmd":"files-confirm",…}
//             {"cmd":"update-restore-point"} {"cmd":"end"} (Expert Mode / Developer Mode, one connection, under its session restore point)
//             Developer Mode in that connection also: {"cmd":"kernel-plan","from":LOCAL IMAGE} then "kernel-confirm"; {"cmd":"system-plan",
//             "from":LOCAL .hsqs} then {"cmd":"system-confirm","digest":…,"nonce":…} (DeveloperEngine, DeveloperSystem)
//             {"cmd":"test-boot","class":"own-startup"|"image","image":LOCAL IMAGE (class image),"backup":RESTORE POINT,…} then
//             {"cmd":"test-boot-confirm","digest":…,"nonce":…} (Developer Mode's test boot: the console in its USB boot mode; TestBootEngine)
//             {"cmd":"home-design","action":"apply","design":COMPILED DESIGN FOLDER,"pack":HOME PACKAGE FOLDER (optional: else from the backup),
//              "renderer":ccs-sim-core.exe (optional: the native host's own renderer when hosted)} or {"cmd":"home-design","action":"revert",
//              "to":"previous"|"defaults"} (+ "console","folder"), then {"cmd":"home-design-confirm","digest":…,"nonce":…} (HOME Designer,
//              strict policy; DesignApplyEngine)
//   events    {"event":"progress",…} {"event":"plan","plan":{…}} {"event":"prep-plan","plan":{…}} {"event":"method-plan","plan":{…}} {"event":"firmware-plan","plan":{…}} {"event":"home-design-plan","plan":{…}}
//             {"event":"kernel-plan",…} {"event":"system-plan",…} {"event":"test-boot-plan","plan":{…}}
//             {"event":"result",…} {"event":"error","text":…}
using CcsBackup.Device;
using CcsBackup.Library;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

namespace CcsBackup.Engine
{
    public static class WorkerHost
    {
        public const string Flag = "--backup-worker";

        /// <summary>how a window writes its requests to the worker: UTF-8 without a byte-order mark, one "\n" line per request, whatever the
        /// window's console or ANSI code page (Process.StandardInput would use that code page, which cannot carry every folder name)</summary>
        public static StreamWriter RequestWriter(Stream workerInput) => new StreamWriter(workerInput, new UTF8Encoding(false)) { AutoFlush = true, NewLine = "\n" };
        public static string RequestLine(JObj request) => Json.Write(request).Replace("\n", " ").Trim();

        public static int Run(string[] args, string entry, string toolName, string toolVersion)
        {
            var input = Console.OpenStandardInput(); var output = Console.OpenStandardOutput();
            var settingsPath = Arg(args, "--settings");
            // the standalone Workbench's in-memory preferences come as arguments (no settings file, no AppData)
            Func<Settings> load = args.Contains("--primary-folder") ? (Func<Settings>)(() => Settings.Portable(Arg(args, "--primary-folder"), Arg(args, "--archive-folder"))) : () => Settings.Load(settingsPath);
            if (args.Contains("--portable")) ConsoleLock.WriteInfo = false;
            return new Session(input, output, entry, toolName, toolVersion, load).Run();
        }

        /// <summary>the same worker on given streams (the tests' in-process requests)</summary>
        internal static int RunOn(Stream input, Stream output, string entry, string toolName, string toolVersion, string settingsPath) => new Session(input, output, entry, toolName, toolVersion, () => Settings.Load(settingsPath)).Run();

        static string Arg(string[] a, string k) { int i = Array.IndexOf(a, k); return i >= 0 && i + 1 < a.Length ? a[i + 1] : null; }

        sealed class Session
        {
            readonly Stream inS, outS; readonly string entry, toolName, toolVersion; readonly Func<Settings> loadSettings;
            readonly BlockingCollection<Dictionary<string, object>> queue = new BlockingCollection<Dictionary<string, object>>();
            readonly object outLock = new object();
            volatile bool cancel, uiGone;
            public Session(Stream i, Stream o, string entry, string toolName, string toolVersion, Func<Settings> loadSettings) { inS = i; outS = o; this.entry = entry; this.toolName = toolName; this.toolVersion = toolVersion; this.loadSettings = loadSettings; }

            void Send(JObj ev) { var b = Encoding.UTF8.GetBytes(Json.Write(ev).Replace("\n", " ").Trim() + "\n"); lock (outLock) { try { outS.Write(b, 0, b.Length); outS.Flush(); } catch { uiGone = true; cancel = true; } } }

            public int Run()
            {
                new Thread(ReadLoop) { IsBackground = true, Name = "window requests" }.Start();
                var first = queue.Take();
                if (first == null) return 0;
                try { return Dispatch(first); }
                catch (Exception ex) { Send(new JObj { { "event", "error" }, { "text", ex.GetType().Name + ": " + ex.Message } }); return 1; }
            }

            void ReadLoop()
            {
                var r = new StreamReader(inS, new UTF8Encoding(false)); string line;
                try
                {
                    while ((line = r.ReadLine()) != null)
                    {
                        if (line.Length > 100000) continue;
                        Dictionary<string, object> d; try { d = Json.Parse(line); } catch { continue; }
                        if (d.S("cmd") == "cancel") { cancel = true; continue; }
                        queue.Add(d);
                    }
                }
                catch { }
                uiGone = true; cancel = true; queue.Add(null);   // the window has gone: stop at the next safe boundary
            }

            EngineContext Ctx(RecoveryConsole con, ImageKind kind, string folder)
            {
                var (id, sha) = RecoveryImages.IdOf(kind);
                var ctx = new EngineContext
                {
                    Settings = loadSettings(), Entry = entry, ToolName = toolName, ToolVersion = toolVersion, RecoveryImageId = id, RecoveryImageSha256 = sha,
                    Cancelled = () => cancel,
                };
                long lastSent = 0;
                ctx.Progress = p =>
                {
                    var now = Environment.TickCount;
                    if (p.Total > 0 && p.Done < p.Total && now - lastSent < 200) return;
                    lastSent = now;
                    Send(new JObj { { "event", "progress" }, { "phase", p.Phase }, { "text", p.Text }, { "done", p.Done }, { "total", p.Total } });
                };
                if (con.Simulated && folder != null)
                {
                    // the tests' deterministic termination points: only ever for a simulated console (faults.json crash_at)
                    var f = Path.Combine(folder, "faults.json");
                    var crashAt = File.Exists(f) ? Json.Parse(File.ReadAllText(f)).S("crash_at") : null;
                    if (crashAt != null) ctx.Boundary = b => { if (b == crashAt) Environment.FailFast("injected termination at " + b); };
                }
                return ctx;
            }

            RecoveryConsole Open(Dictionary<string, object> req, ImageKind kind, out string folder)
            {
                folder = null;
                if (req.S("console") == "simulated") { folder = req.S("folder"); return RecoverySessions.OpenSimulated(folder, kind); }
                return RecoverySessions.OpenReal(kind, t => Send(new JObj { { "event", "progress" }, { "phase", "connect" }, { "text", t } }), () => cancel);
            }

            int Dispatch(Dictionary<string, object> req)
            {
                var cmd = req.S("cmd");
                if (cmd != "backup" && cmd != "restore-prepare" && cmd != "prepare-plan" && cmd != "return-plan" && cmd != "method-plan" && cmd != "firmware-plan" && cmd != "rescue" && cmd != "files" && cmd != "browse" && cmd != "compose" && cmd != "home-design" && cmd != "test-boot") { Send(new JObj { { "event", "error" }, { "text", "unknown request" } }); return 2; }
                if (cmd == "prepare-plan" && req.S("policy") != null && req.S("policy") != "none" && !Methods.IsPolicy(req.S("policy"))) { Send(new JObj { { "event", "error" }, { "text", "unknown install method" } }); return 2; }
                if (cmd == "method-plan" && (req.S("op") != "remove" && req.S("op") != "change" || !Methods.IsPolicy(req.S("method")))) { Send(new JObj { { "event", "error" }, { "text", "unknown method request" } }); return 2; }
                if (cmd == "firmware-plan" && req.S("op") != "add" && req.S("op") != "remove") { Send(new JObj { { "event", "error" }, { "text", "unknown system image request" } }); return 2; }
                if (cmd == "files" && req.S("scope") != "user" && req.S("scope") != "dev") { Send(new JObj { { "event", "error" }, { "text", "unknown file mode" } }); return 2; }
                // a request that names a game system, console model, provider or capability this version does not support stops here,
                // before the console lock or any console session (SNES-GROUNDWORK.md); one that names none is the NES Classic, as always
                var target = CcsBackup.Compose.Capabilities.CheckRequest(req);
                if (!target.Ok) { Send(new JObj { { "event", "result" }, { "state", "unavailable" }, { "code", target.Code }, { "problem", target.Text } }); return 4; }
                using (var lk = ConsoleLock.TryAcquire(toolName, cmd, out var busy))
                {
                    if (lk == null) { Send(new JObj { { "event", "result" }, { "state", "busy" }, { "problem", "Another console operation is running in " + busy + ". Wait for it to finish." } }); return 3; }
                    if (cmd == "test-boot") return TestBootOp(req);   // no console program runs: the console stays in its boot ROM's USB mode
                    var kind = cmd == "backup" || cmd == "browse" ? ImageKind.Backup : cmd == "prepare-plan" ? ImageKind.Prepare : cmd == "return-plan" ? ImageKind.Return : cmd == "method-plan" ? ImageKind.Method : cmd == "firmware-plan" ? ImageKind.Firmware : cmd == "rescue" ? ImageKind.Rescue : cmd == "files" || cmd == "compose" || cmd == "home-design" ? ImageKind.Files : ImageKind.Restore;
                    RecoveryConsole con;
                    try { con = Open(req, kind, out var folder0); }
                    catch (GateClosedException ex) { Send(new JObj { { "event", "result" }, { "state", "unavailable" }, { "problem", ex.Message } }); return 4; }
                    catch (ConsoleRefusedException ex) { Send(new JObj { { "event", "result" }, { "state", "refused" }, { "problem", ex.Message } }); return 4; }
                    catch (OperationCanceledException) { Send(new JObj { { "event", "result" }, { "state", "canceled" }, { "problem", "Canceled before the console answered. Nothing was read or written." } }); return 0; }
                    var folder = req.S("console") == "simulated" ? req.S("folder") : null;
                    var ctx = Ctx(con, kind, folder);
                    if (cmd == "backup") return Backup(con, ctx);
                    // CCS HOME's package for a method: the one this build carries, unless the window names a package folder ("home")
                    HomePackage home = null; var hl = cmd == "prepare-plan" ? req.S("policy") : cmd == "method-plan" && req.S("op") == "change" ? req.S("method") : null;
                    if (req.S("home") != null && Methods.IsPolicy(hl))
                    {
                        try { home = HomePackage.Folder(req.S("home"), hl); }
                        catch (Exception ex) when (ex is HomePackageException || ex is IOException || ex is UnauthorizedAccessException) { Send(new JObj { { "event", "result" }, { "operation", cmd == "prepare-plan" ? "prepare" : "method" }, { "state", "refused" }, { "problem", "The HOME package cannot be used: " + ex.Message } }); AfterOperation(con); return 1; }
                    }
                    if (cmd == "prepare-plan") return Prepare(con, ctx, req.S("policy"), home, req.B("mod_start"));
                    if (cmd == "return-plan") return Return(con, ctx);
                    if (cmd == "method-plan") return Method(con, ctx, req.S("op"), req.S("method"), home, req.B("mod_start"));
                    if (cmd == "firmware-plan") return Firmware(con, ctx, req.S("op"), req.S("id"), req.S("label"), req.S("file"));
                    if (cmd == "rescue") return Rescue(con, ctx);
                    if (cmd == "compose") return Compose(con, ctx, req);
                    if (cmd == "home-design") return HomeDesignOp(con, ctx, req);
                    if (cmd == "browse") return Browse(con, ctx);
                    if (cmd == "files") return Files(con, ctx, req.S("scope"));
                    return Restore(con, ctx, req.S("backup"), req.A("only")?.OfType<string>().ToList());
                }
            }

            // Console Browser: the check-up, then (when the window asks) bounded reads of the data storage for the live views; reads only
            int Browse(RecoveryConsole con, EngineContext ctx)
            {
                var c0 = ConsoleBrowser.Checkup(con, ctx.RecoveryImageId, ctx.Cancelled);
                Send(new JObj { { "event", "checkup" }, { "state", c0.Problem == null ? "checked" : "partial" }, { "checkup", c0.ToJson() } });
                if (c0.Problem != null || c0.DataDevice == null || c0.PartBytes == null) { Send(new JObj { { "event", "result" }, { "operation", "browse" }, { "state", "partial" }, { "checkup", c0.ToJson() } }); AfterOperation(con); return 0; }
                long size = c0.PartBytes.Value; DateTime idle = DateTime.UtcNow.AddHours(2);
                while (!cancel && DateTime.UtcNow < idle)
                {
                    if (!queue.TryTake(out var c, 500)) continue;
                    if (c == null || c.S("cmd") == "quit" || c.S("cmd") == "end") break;
                    if (c.S("cmd") != "range") { Send(new JObj { { "event", "error" }, { "text", "Console Browser only reads." } }); continue; }
                    idle = DateTime.UtcNow.AddHours(2);
                    long off = c.L("offset"); long len = c.L("length");
                    if (off < 0 || len <= 0 || len > RecoveryConsole.RangeMax || off + len > size || off % 4096 != 0 || len % 4096 != 0)
                    { Send(new JObj { { "event", "range-data" }, { "id", c.S("id") }, { "ok", false }, { "problem", "a read outside the data storage was refused" } }); continue; }
                    var ms = new MemoryStream(); var rr = con.ReadRange(c0.DataDevice, off, (int)len, ms, ctx.Cancelled);
                    Send(rr.Ok ? new JObj { { "event", "range-data" }, { "id", c.S("id") }, { "ok", true }, { "data", Convert.ToBase64String(ms.ToArray()) } }
                               : new JObj { { "event", "range-data" }, { "id", c.S("id") }, { "ok", false }, { "problem", rr.Problem } });
                }
                Send(new JObj { { "event", "result" }, { "operation", "browse" }, { "state", "checked" }, { "checkup", c0.ToJson() } });
                AfterOperation(con); return 0;
            }

            int Backup(RecoveryConsole con, EngineContext ctx)
            {
                var o = CaptureEngine.Run(con, ctx);
                Send(new JObj
                {
                    { "event", "result" }, { "operation", "backup" }, { "state", o.State }, { "problem", o.Problem }, { "id", o.Id }, { "primary", o.PrimaryPath }, { "archive", o.ArchivePath },
                    { "partial", o.PartialPath }, { "copy_problem", o.CopyProblem }, { "two_copies", o.TwoCopies }, { "same_disk", o.SameDisk }, { "bytes", o.Bytes }, { "simulated", o.Simulated },
                    { "console_tag", o.Identity?.ShortTag }, { "state_text", o.Classification?.Text }, { "recovery_baseline", o.Verification?.RecoveryBaseline ?? false },
                    { "inventory", o.Inventory?.Summary }, { "external", o.Inventory?.ExternalNotes }, { "ui_gone", uiGone },
                });
                AfterOperation(con);
                return o.Ok ? 0 : 1;
            }

            int Restore(RecoveryConsole con, EngineContext ctx, string backup, List<string> only)
            {
                var (plan, refused) = only == null ? RestoreEngine.Prepare(con, backup, ctx) : RestoreEngine.Prepare(con, backup, ctx, new HashSet<string>(only));
                if (refused != null) { Send(new JObj { { "event", "result" }, { "operation", "restore" }, { "state", refused.State }, { "problem", refused.Problem }, { "protective", refused.Protective?.PrimaryPath } }); AfterOperation(con); return 1; }
                Send(new JObj { { "event", "plan" }, { "plan", plan.ToJson() } });
                if (!plan.Writable) { Send(new JObj { { "event", "result" }, { "operation", "restore" }, { "state", plan.Refusals.Count > 0 ? "refused" : "nothing-to-do" }, { "problem", plan.Refusals.FirstOrDefault() }, { "protective", plan.ProtectivePath } }); AfterOperation(con); return plan.Refusals.Count > 0 ? 1 : 0; }
                // wait for the window's confirmation (or cancel / the window ending)
                Dictionary<string, object> c = null;
                while (!cancel && DateTime.UtcNow < plan.ExpiresUtc) { if (queue.TryTake(out c, 500)) { if (c == null || c.S("cmd") == "restore-confirm" || c.S("cmd") == "quit") break; } }
                if (c == null || c.S("cmd") != "restore-confirm")
                {
                    Send(new JObj { { "event", "result" }, { "operation", "restore" }, { "state", "canceled" }, { "problem", cancel ? "Canceled before anything was written. The console is unchanged." : "The restore was not confirmed within 10 minutes. Nothing was written." }, { "protective", plan.ProtectivePath } });
                    AfterOperation(con); return 0;
                }
                var o = RestoreEngine.Execute(con, plan, c.S("digest"), c.S("nonce"), ctx);
                Send(new JObj
                {
                    { "event", "result" }, { "operation", "restore" }, { "state", o.State }, { "problem", o.Problem }, { "written", o.Written }, { "partial", o.Partial }, { "verified", o.Verified },
                    { "protective", plan.ProtectivePath }, { "record", o.RecordFile }, { "ui_gone", uiGone },
                });
                AfterOperation(con);
                return o.Ok ? 0 : 1;
            }

            int Prepare(RecoveryConsole con, EngineContext ctx, string policy, HomePackage home, bool modStart)
            {
                const string subject = "ccs-prepare/hakchi-" + Preparation.PinnedHakchiVersion;
                var id = con.Identify();
                var adm = Admission.Admit(ctx.Settings, id.Hwid, "prepare", subject, ctx.Cancelled, con.Simulated, con.SessionId);
                if (!adm.Ok) { Send(new JObj { { "event", "result" }, { "operation", "prepare" }, { "state", "refused" }, { "code", adm.Code }, { "problem", adm.Text } }); AfterOperation(con); return 1; }
                var (plan, refused) = Preparation.Prepare(con, ctx, adm, policy, home, modStart);
                if (refused != null) { Send(new JObj { { "event", "result" }, { "operation", "prepare" }, { "state", refused.State }, { "problem", refused.Problem } }); AfterOperation(con); return 1; }
                Send(new JObj { { "event", "prep-plan" }, { "plan", plan.ToJson() } });
                if (plan.Refusal != null) { Send(new JObj { { "event", "result" }, { "operation", "prepare" }, { "state", "refused" }, { "problem", plan.Refusal } }); AfterOperation(con); return 1; }
                Dictionary<string, object> c = null;
                while (!cancel && DateTime.UtcNow < plan.ExpiresUtc) { if (queue.TryTake(out c, 500)) { if (c == null || c.S("cmd") == "prepare-confirm" || c.S("cmd") == "quit") break; } }
                if (c == null || c.S("cmd") != "prepare-confirm") { Send(new JObj { { "event", "result" }, { "operation", "prepare" }, { "state", "canceled" }, { "problem", "Nothing was changed." } }); AfterOperation(con); return 0; }
                // the admission is checked inside Execute, under the evidence hold and after the plan's own checks (a refused
                // confirmation does not use it up)
                var o = Preparation.Execute(con, plan, c.S("digest"), c.S("nonce"), ctx,
                    () => Admission.Use(ctx.Settings, adm.TicketId, "prepare", subject, con.Hwid, ctx.Cancelled, con.Simulated, null, new SessionCapture(plan.CurrentBackup, plan.CurrentSha256, con.SessionId)));
                Send(new JObj { { "event", "result" }, { "operation", "prepare" }, { "state", o.State }, { "problem", o.Problem }, { "done", o.Done }, { "record", o.RecordFile }, { "ui_gone", uiGone } });
                AfterOperation(con); return o.Ok ? 0 : 1;
            }

            // Console Restore: remove CCS Mod or CCS Nucleus, or change from one to the other (MethodEngine; hakchi applies it at the next start)
            int Method(RecoveryConsole con, EngineContext ctx, string op, string method, HomePackage home, bool modStart)
            {
                var (plan, refused) = MethodEngine.Prepare(con, ctx, op, method, home, modStart);
                if (refused != null) { Send(new JObj { { "event", "result" }, { "operation", "method" }, { "state", refused.State }, { "problem", refused.Problem } }); AfterOperation(con); return 1; }
                Send(new JObj { { "event", "method-plan" }, { "plan", plan.ToJson() } });
                Dictionary<string, object> c = null;
                while (!cancel && DateTime.UtcNow < plan.ExpiresUtc) { if (queue.TryTake(out c, 500)) { if (c == null || c.S("cmd") == "method-confirm" || c.S("cmd") == "quit") break; } }
                if (c == null || c.S("cmd") != "method-confirm") { Send(new JObj { { "event", "result" }, { "operation", "method" }, { "state", "canceled" }, { "problem", "Nothing was changed." }, { "protective", plan.ProtectivePath } }); AfterOperation(con); return 0; }
                var o = MethodEngine.Execute(con, plan, c.S("digest"), c.S("nonce"), ctx);
                Send(new JObj { { "event", "result" }, { "operation", "method" }, { "state", o.State }, { "problem", o.Problem }, { "done", o.Done }, { "record", o.RecordFile }, { "protective", plan.ProtectivePath }, { "applies", o.Ok ? "at the console's next normal start" : null }, { "ui_gone", uiGone } });
                AfterOperation(con); return o.Ok ? 0 : 1;
            }

            // Console Composer's Browse for Firmware: an owner's system image added as a CCS Nucleus choice, or one removed (FirmwareEngine)
            int Firmware(RecoveryConsole con, EngineContext ctx, string op, string id, string label, string file)
            {
                var (plan, refused) = FirmwareEngine.Prepare(con, ctx, op, id, label, file);
                if (refused != null) { Send(new JObj { { "event", "result" }, { "operation", "firmware" }, { "state", refused.State }, { "problem", refused.Problem } }); AfterOperation(con); return 1; }
                Send(new JObj { { "event", "firmware-plan" }, { "plan", plan.ToJson() } });
                Dictionary<string, object> c = null;
                while (!cancel && DateTime.UtcNow < plan.ExpiresUtc) { if (queue.TryTake(out c, 500)) { if (c == null || c.S("cmd") == "firmware-confirm" || c.S("cmd") == "quit") break; } }
                if (c == null || c.S("cmd") != "firmware-confirm") { Send(new JObj { { "event", "result" }, { "operation", "firmware" }, { "state", "canceled" }, { "problem", "Nothing was changed." }, { "protective", plan.ProtectivePath } }); AfterOperation(con); return 0; }
                var o = FirmwareEngine.Execute(con, plan, c.S("digest"), c.S("nonce"), ctx);
                Send(new JObj { { "event", "result" }, { "operation", "firmware" }, { "state", o.State }, { "problem", o.Problem }, { "done", o.Done }, { "record", o.RecordFile }, { "protective", plan.ProtectivePath }, { "id", plan.Id }, { "ui_gone", uiGone } });
                AfterOperation(con); return o.Ok ? 0 : 1;
            }

            int Return(RecoveryConsole con, EngineContext ctx)
            {
                var (plan, refused) = ReturnEngine.Prepare(con, ctx);
                if (refused != null) { Send(new JObj { { "event", "result" }, { "operation", "return" }, { "state", refused.State }, { "problem", refused.Problem } }); AfterOperation(con); return 1; }
                Send(new JObj { { "event", "return-plan" }, { "plan", plan.ToJson() } });
                Dictionary<string, object> c = null;
                while (!cancel && DateTime.UtcNow < plan.ExpiresUtc) { if (queue.TryTake(out c, 500)) { if (c == null || c.S("cmd") == "return-confirm" || c.S("cmd") == "quit") break; } }
                if (c == null || c.S("cmd") != "return-confirm") { Send(new JObj { { "event", "result" }, { "operation", "return" }, { "state", "canceled" }, { "problem", "Nothing was changed." }, { "protective", plan.ProtectivePath } }); AfterOperation(con); return 0; }
                var o = ReturnEngine.Execute(con, plan, c.S("digest"), c.S("nonce"), ctx);
                Send(new JObj { { "event", "result" }, { "operation", "return" }, { "state", o.State }, { "problem", o.Problem }, { "done", o.Done }, { "record", o.RecordFile }, { "snapshot", plan.Snapshot }, { "protective", plan.ProtectivePath }, { "ui_gone", uiGone } });
                AfterOperation(con); return o.Ok ? 0 : 1;
            }

            Dictionary<string, object> Next(DateTime until, params string[] cmds)
            {
                Dictionary<string, object> c = null;
                while (!cancel && DateTime.UtcNow < until) { if (queue.TryTake(out c, 500)) { if (c == null || c.S("cmd") == "quit" || cmds.Contains(c.S("cmd"))) return c; } }
                return null;
            }

            int Rescue(RecoveryConsole con, EngineContext ctx)
            {
                RescueDiagnosis dg; ConsoleIdentity id;
                try { id = con.Identify(); dg = RescueEngine.Diagnose(con); }
                catch (ConsoleRefusedException ex) { Send(new JObj { { "event", "result" }, { "operation", "rescue" }, { "state", "refused" }, { "problem", ex.Message } }); AfterOperation(con); return 1; }
                var blocked = RescueEngine.Blocked(dg);   // no repair is offered when the driver did not answer or the two readers disagree
                if (blocked != null) { Send(new JObj { { "event", "result" }, { "operation", "rescue" }, { "state", "refused" }, { "problem", blocked }, { "diagnosis", dg.ToJson() } }); AfterOperation(con); return 1; }
                Send(new JObj { { "event", "rescue-diagnosis" }, { "console_tag", id.ShortTag }, { "diagnosis", dg.ToJson() }, { "text", dg.Text }, { "damaged", dg.DamagedStages.Cast<object>().ToList() }, { "unreadable", dg.DamagedStages.Where(dg.Unreadable).Cast<object>().ToList() }, { "acknowledgment", dg.DamagedStages.Count > 0 ? RescueEngine.AcknowledgmentFor(dg.DamagedStages, dg) : null } });
                var ch = Next(DateTime.UtcNow + RestoreEngine.ConfirmWindow, "rescue-choose");
                if (ch == null || ch.S("cmd") != "rescue-choose") { Send(new JObj { { "event", "result" }, { "operation", "rescue" }, { "state", "canceled" }, { "problem", "Nothing was changed." } }); AfterOperation(con); return 0; }
                var stages = (ch.A("stages") ?? new List<object>()).OfType<string>().ToList();
                var (plan, refused) = RescueEngine.Prepare(con, ctx, ch.S("backup"), stages, ch.S("ack"));
                if (refused != null) { Send(new JObj { { "event", "result" }, { "operation", "rescue" }, { "state", refused.State }, { "problem", refused.Problem } }); AfterOperation(con); return 1; }
                Send(new JObj { { "event", "rescue-plan" }, { "plan", plan.ToJson() } });
                if (plan.Refusal != null) { Send(new JObj { { "event", "result" }, { "operation", "rescue" }, { "state", "nothing-to-do" }, { "problem", plan.Refusal } }); AfterOperation(con); return 0; }
                var c = Next(plan.ExpiresUtc, "rescue-confirm");
                if (c == null || c.S("cmd") != "rescue-confirm") { Send(new JObj { { "event", "result" }, { "operation", "rescue" }, { "state", "canceled" }, { "problem", "Nothing was changed." }, { "protective", plan.ProtectiveArchive?.Path } }); AfterOperation(con); return 0; }
                var o = RescueEngine.Execute(con, plan, c.S("digest"), c.S("nonce"), ctx);
                Send(new JObj { { "event", "result" }, { "operation", "rescue" }, { "state", o.State }, { "problem", o.Problem }, { "done", o.Done }, { "record", o.RecordFile }, { "protective", plan.ProtectiveArchive?.Path }, { "ui_gone", uiGone } });
                AfterOperation(con); return o.Ok ? 0 : 1;
            }

            /// <summary>Console Composer: one reviewed composition (strict policy: a backup of the console in this session first)</summary>
            int Compose(RecoveryConsole con, EngineContext ctx, Dictionary<string, object> req)
            {
                if (req.S("kind") == "package") return ComposePackage(con, ctx, req.S("package"));
                var items = (req.A("items") ?? new List<object>()).OfType<Dictionary<string, object>>().Select(x => new ComposeItem { SourceBackup = x.S("source"), SourcePath = x.S("path"), TargetPath = x.S("target") }).ToList();
                var (plan, why) = ComposerEngine.Plan(con, ctx, req.S("kind"), items);
                if (plan == null) { Send(new JObj { { "event", "result" }, { "operation", "compose" }, { "state", "refused" }, { "problem", why } }); AfterOperation(con); return 1; }
                Send(new JObj { { "event", "compose-plan" }, { "plan", plan.ToJson() } });
                if (plan.Refusal != null) { Send(new JObj { { "event", "result" }, { "operation", "compose" }, { "state", "nothing-to-do" }, { "problem", plan.Refusal } }); AfterOperation(con); return 0; }
                var c = Next(plan.ExpiresUtc, "compose-confirm");
                if (c == null || c.S("cmd") != "compose-confirm") { Send(new JObj { { "event", "result" }, { "operation", "compose" }, { "state", "canceled" }, { "problem", "Nothing was changed." } }); AfterOperation(con); return 0; }
                var o = ComposerEngine.Execute(con, plan, c.S("digest"), c.S("nonce"), ctx);
                Send(new JObj { { "event", "result" }, { "operation", "compose" }, { "state", o.State }, { "problem", o.Problem }, { "done", o.Done }, { "record", o.RecordFile }, { "ui_gone", uiGone } });
                AfterOperation(con); return o.Ok ? 0 : 1;
            }

            // Console Composer: a private restore package applied to the console it names (strict policy, like a composition from backups)
            int ComposePackage(RecoveryConsole con, EngineContext ctx, string package)
            {
                var (plan, why) = PackageComposer.Plan(con, ctx, package);
                if (plan == null) { Send(new JObj { { "event", "result" }, { "operation", "compose" }, { "state", "refused" }, { "problem", why } }); AfterOperation(con); return 1; }
                Send(new JObj { { "event", "compose-plan" }, { "plan", plan.ToJson() } });
                if (plan.Refusal != null) { Send(new JObj { { "event", "result" }, { "operation", "compose" }, { "state", "nothing-to-do" }, { "problem", plan.Refusal } }); AfterOperation(con); return 0; }
                var c = Next(plan.ExpiresUtc, "compose-confirm");
                if (c == null || c.S("cmd") != "compose-confirm") { Send(new JObj { { "event", "result" }, { "operation", "compose" }, { "state", "canceled" }, { "problem", "Nothing was changed." } }); AfterOperation(con); return 0; }
                var o = PackageComposer.Execute(con, plan, c.S("digest"), c.S("nonce"), ctx);
                Send(new JObj { { "event", "result" }, { "operation", "compose" }, { "state", o.State }, { "problem", o.Problem }, { "done", o.Done }, { "record", o.RecordFile }, { "ui_gone", uiGone } });
                AfterOperation(con); return o.Ok ? 0 : 1;
            }

            /// <summary>HOME Designer: apply one compiled design, or Revert Design (strict policy: a backup of the console in this session first; the
            /// render check on the console's own HOME package before anything is admitted)</summary>
            int HomeDesignOp(RecoveryConsole con, EngineContext ctx, Dictionary<string, object> req)
            {
                DesignApplyPlan plan; string why;
                if (req.S("action") == "revert") (plan, why) = DesignApplyEngine.PlanRevert(con, ctx, req.S("to"));
                else if (req.S("action") == "apply")
                {
                    CcsBackup.Compose.CompiledDesign d = null; why = null;
                    try { d = CcsBackup.Compose.CompiledDesign.Read(req.S("design") ?? ""); } catch (CcsBackup.Compose.ComposeException ex) { why = ex.Message; } catch (IOException ex) { why = ex.Message; }
                    (plan, why) = d == null ? (null, why) : DesignApplyEngine.PlanApply(con, ctx, d, CcsBackup.Compose.HomeRenderers.Find(req.S("renderer")), req.S("pack"));
                }
                else { plan = null; why = "Unknown HOME design action."; }
                if (plan == null) { Send(new JObj { { "event", "result" }, { "operation", "home-design" }, { "state", "refused" }, { "problem", why } }); AfterOperation(con); return 1; }
                Send(new JObj { { "event", "home-design-plan" }, { "plan", plan.ToJson() } });
                if (plan.Refusal != null) { Send(new JObj { { "event", "result" }, { "operation", "home-design" }, { "state", "nothing-to-do" }, { "problem", plan.Refusal } }); AfterOperation(con); return 0; }
                var c = Next(plan.ExpiresUtc, "home-design-confirm");
                if (c == null || c.S("cmd") != "home-design-confirm") { Send(new JObj { { "event", "result" }, { "operation", "home-design" }, { "state", "canceled" }, { "problem", "Nothing was changed." } }); AfterOperation(con); return 0; }
                var o = DesignApplyEngine.Execute(con, plan, c.S("digest"), c.S("nonce"), ctx);
                Send(new JObj { { "event", "result" }, { "operation", "home-design" }, { "state", o.State }, { "problem", o.Problem }, { "done", o.Done }, { "record", o.RecordFile }, { "protective", plan.Protective?.Path }, { "ui_gone", uiGone } });
                AfterOperation(con); return o.Ok ? 0 : 1;
            }

            /// <summary>one Expert Mode / Developer Mode connection: a restore point first, then any number of reviewed change sets (no full
            /// backup per change), until the window ends the session</summary>
            int Files(RecoveryConsole con, EngineContext ctx, string scope)
            {
                SessionRestorePoint rp = null; FilePlan plan = null; KernelPlan kplan = null; SystemPlan splan = null; int rc = 0;
                Send(new JObj { { "event", "files-ready" }, { "scope", scope }, { "notice", SessionRestorePoint.NoticeText } });
                while (true)
                {
                    var c = Next(DateTime.UtcNow.AddHours(4), "restore-point", "stat", "files-plan", "files-confirm", "update-restore-point", "kernel-plan", "kernel-confirm", "system-plan", "system-confirm", "end");
                    if (c == null || c.S("cmd") == "quit" || c.S("cmd") == "end") break;
                    try
                    {
                        switch (c.S("cmd"))
                        {
                            case "restore-point":
                                { var (p, why) = RestorePoints.Establish(con, c.S("backup"), ctx.Cancelled); if (p != null) rp = p; Send(new JObj { { "event", "restore-point" }, { "ok", p != null }, { "problem", why }, { "point", rp?.ToJson() }, { "banner", rp?.Banner } }); break; }
                            case "update-restore-point":
                                { var (p, why) = RestorePoints.Update(con, ctx, rp); rp = p ?? rp; Send(new JObj { { "event", "restore-point" }, { "ok", why == null }, { "problem", why }, { "point", rp?.ToJson() }, { "banner", rp?.Banner } }); break; }
                            case "stat":
                                { var (st, n) = con.FileStat(scope, c.S("path")); Send(new JObj { { "event", "stat" }, { "path", c.S("path") }, { "state", st }, { "bytes", n } }); break; }
                            case "files-plan":
                                {
                                    var changes = new List<FileChange>();
                                    foreach (var x in (c.A("changes") ?? new List<object>()).OfType<Dictionary<string, object>>())
                                        changes.Add(x.S("op") == "del" ? FileChange.Delete(x.S("path")) : FileChange.Put(x.S("path"), File.ReadAllBytes(x.S("from"))));
                                    string why; (plan, why) = FilesEngine.Plan(con, rp, scope, changes, ctx.Cancelled);
                                    Send(plan == null ? new JObj { { "event", "files-plan" }, { "ok", false }, { "problem", why } } : new JObj { { "event", "files-plan" }, { "ok", true }, { "plan", plan.ToJson() } });
                                    break;
                                }
                            case "kernel-plan":
                                {
                                    if (scope != "dev") { Send(new JObj { { "event", "error" }, { "text", "Only Developer Mode changes the startup software." } }); break; }
                                    string why; (kplan, why) = DeveloperEngine.PlanKernel(con, rp, File.ReadAllBytes(c.S("from")), ctx.Cancelled);
                                    Send(kplan == null ? new JObj { { "event", "kernel-plan" }, { "ok", false }, { "problem", why } } : new JObj { { "event", "kernel-plan" }, { "ok", true }, { "plan", kplan.ToJson() } });
                                    break;
                                }
                            case "kernel-confirm":
                                {
                                    var o = DeveloperEngine.FlashKernel(con, kplan, c.S("digest"), c.S("nonce"), ctx); kplan = null; rc = o.Ok ? rc : 1;
                                    Send(new JObj { { "event", "files-result" }, { "state", o.State }, { "problem", o.Problem }, { "done", o.Done }, { "record", o.RecordFile }, { "banner", rp?.Banner } });
                                    break;
                                }
                            case "system-plan":
                                {
                                    if (scope != "dev") { Send(new JObj { { "event", "error" }, { "text", "Only Developer Mode changes the system part." } }); break; }
                                    var fi = new FileInfo(c.S("from") ?? ""); string why;
                                    if (!fi.Exists || fi.Length > DeveloperSystem.MaxImageBytes) { splan = null; why = "Choose a system image file (SquashFS) of at most " + Units.Gb(DeveloperSystem.MaxImageBytes) + "."; }
                                    else (splan, why) = DeveloperSystem.Plan(con, rp, File.ReadAllBytes(fi.FullName), fi.Name, ctx.Cancelled);
                                    Send(splan == null ? new JObj { { "event", "system-plan" }, { "ok", false }, { "problem", why } } : new JObj { { "event", "system-plan" }, { "ok", true }, { "plan", splan.ToJson() } });
                                    break;
                                }
                            case "system-confirm":
                                {
                                    var o = DeveloperSystem.Flash(con, splan, c.S("digest"), c.S("nonce"), ctx); splan = null; rc = o.Ok ? rc : 1;
                                    Send(new JObj { { "event", "files-result" }, { "state", o.State }, { "problem", o.Problem }, { "done", o.Done }, { "record", o.RecordFile }, { "banner", rp?.Banner } });
                                    break;
                                }
                            case "files-confirm":
                                {
                                    var o = FilesEngine.Execute(con, plan, c.S("digest"), c.S("nonce"), ctx); plan = null; rc = o.Ok ? rc : 1;
                                    Send(new JObj { { "event", "files-result" }, { "state", o.State }, { "problem", o.Problem }, { "done", o.Done }, { "record", o.RecordFile }, { "banner", rp?.Banner } });
                                    break;
                                }
                        }
                    }
                    catch (Exception ex) when (ex is ConsoleRefusedException || ex is ArgumentException || ex is IOException || ex is UnauthorizedAccessException)
                    { Send(new JObj { { "event", "error" }, { "text", ex.Message } }); }
                }
                Send(new JObj { { "event", "result" }, { "operation", "files" }, { "state", "ended" }, { "ui_gone", uiGone } });
                AfterOperation(con); return rc;
            }

            /// <summary>Developer Mode's test boot: the console in its boot ROM's USB mode (no console program runs), its hardware id read, a
            /// restore point of that console checked, the reviewed plan, one confirmation, the image sent to its memory once (TestBootEngine).
            /// Nothing follows it on this connection: the image runs the console.</summary>
            int TestBootOp(Dictionary<string, object> req)
            {
                ITestBootTarget t;
                try { t = req.S("console") == "simulated" ? DeveloperTestBoot.OpenSimulated(req.S("folder")) : DeveloperTestBoot.OpenReal(); }
                catch (GateClosedException ex) { Send(new JObj { { "event", "result" }, { "operation", "test-boot" }, { "state", "unavailable" }, { "problem", ex.Message } }); return 4; }
                catch (ConsoleRefusedException ex) { Send(new JObj { { "event", "result" }, { "operation", "test-boot" }, { "state", "refused" }, { "problem", ex.Message } }); return 4; }
                using (t)
                {
                    var ctx = new EngineContext { Settings = loadSettings(), Entry = entry, ToolName = toolName, ToolVersion = toolVersion, Cancelled = () => cancel };
                    ctx.Progress = p => Send(new JObj { { "event", "progress" }, { "phase", p.Phase }, { "text", p.Text }, { "done", p.Done }, { "total", p.Total } });
                    JObj Result(string state, string problem, string record = null) => new JObj { { "event", "result" }, { "operation", "test-boot" }, { "state", state }, { "problem", problem }, { "record", record }, { "ui_gone", uiGone } };
                    ctx.Say("check", "Checking the restore point...");
                    var (rp, why) = TestBootEngine.Point(t, req.S("backup"), ctx.Cancelled);
                    if (rp == null) { Send(Result("refused", why)); return 1; }
                    byte[] img = null; string origin = null;
                    if (req.S("class") == TestBootEngine.ImageFile)
                    {
                        var fi = new FileInfo(req.S("image") ?? "");
                        if (!fi.Exists || fi.Length > 64L * 1024 * 1024) { Send(Result("refused", "Choose a startup image file (an Android boot image) of at most " + Units.Gb(64L * 1024 * 1024) + ".")); return 1; }
                        img = File.ReadAllBytes(fi.FullName); origin = fi.Name;
                    }
                    var (plan, pwhy) = TestBootEngine.Plan(t, rp, req.S("class"), img, origin, ctx.Cancelled);
                    if (plan == null) { Send(Result("refused", pwhy)); return 1; }
                    Send(new JObj { { "event", "test-boot-plan" }, { "plan", plan.ToJson() } });
                    var c = Next(plan.ExpiresUtc, "test-boot-confirm");
                    if (c == null || c.S("cmd") != "test-boot-confirm") { Send(Result("canceled", "Nothing was sent to the console.")); return 0; }
                    var o = TestBootEngine.Execute(t, plan, c.S("digest"), c.S("nonce"), ctx);
                    Send(Result(o.State, o.Problem, o.RecordFile)); return o.Ok ? 0 : 1;
                }
            }

            /// <summary>the window may ask to restart the console into its normal start; nothing else follows an operation</summary>
            void AfterOperation(RecoveryConsole con)
            {
                if (uiGone) { con.Disconnect(); return; }
                Dictionary<string, object> c;
                while (queue.TryTake(out c, 600000))
                {
                    if (c == null || c.S("cmd") == "quit") break;
                    if (c.S("cmd") == "finish") { var ok = con.Finish(); Send(new JObj { { "event", "finished" }, { "restarting", ok } }); break; }
                }
                con.Disconnect();
            }
        }
    }
}
