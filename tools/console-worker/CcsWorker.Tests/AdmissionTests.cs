// Classic Cart Studio console transport worker tests: the backup prerequisite at the write boundary. Copyright (C) 2026 Rance9. GPL-3.0-or-later.
// Every command that changes the console needs an admission the worker itself checked ("admit", with the console identity it read
// itself); the production admitter is CcsBackup.Engine.Admission.Use (its backups verified from scratch: CcsBackup.Tests covers it).
using CcsWorker.Protocol;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace CcsWorker.Tests
{
    static class AdmissionTests
    {
        static ScriptedShell Shell(string hwid = "0461aaaa 88180000 1e670000 6c118000") => new ScriptedShell
        {
            Answer = (c, i) => c.Contains("hakchi hwid") ? Tuple.Create(0, hwid + "\n", "") : c.Contains("osrelease") ? Tuple.Create(0, "3.4.113.29-madmonkey\n", "") : Tuple.Create(0, "", "")
        };

        public static void Register()
        {
            T.Add("admission", "every write command without an admission is refused (backup-required) before anything is sent", () =>
            {
                var sh = Shell();
                using (var h = new Harness(sh, writes: true, admitted: false))
                {
                    foreach (var w in WorkerServer.WriteCommands)
                        T.Eq("backup-required", Harness.Err(h.Call(w, new Dictionary<string, object> { ["path"] = "/var/lib/ccs/x", ["name"] = "ccs-home", ["from"] = "/var/lib/ccs/a", ["to"] = "/var/lib/ccs/b", ["value"] = "x", ["sha256"] = new string('0', 64) })), w);
                    T.True(sh.Commands.Count == 0, "nothing reached the console (" + sh.Commands.Count + " commands)");
                }
            });
            T.Add("admission", "a request field never admits (skip, force, verified, admitted, a backup path, continue anyway)", () =>
            {
                using (var h = new Harness(Shell(), writes: true, admitted: false))
                {
                    var a = new Dictionary<string, object> { ["path"] = "/var/lib/ccs/x", ["skip_backup"] = true, ["force"] = true, ["verified"] = true, ["admitted"] = true, ["backup"] = @"C:\backups\x.zip", ["continue_anyway"] = true };
                    T.Eq("backup-required", Harness.Err(h.Call("mkdir", a)), "mkdir with every bypass-looking field");
                }
            });
            T.Add("admission", "admit uses the identity the worker reads now, not one in the request; a refusal keeps writes closed", () =>
            {
                string seen = null;
                using (var h = new Harness(Shell("0461bbbb 88180000 1e670000 6c118000"), writes: true, admitted: false, admitter: (t, o, s, hw, live) => { seen = hw; return new CcsBackup.Engine.AdmissionResult { Ok = false, Code = "wrong-console", Text = "another console" }; }))
                {
                    T.Eq("wrong-console", Harness.Err(h.Call("admit", new Dictionary<string, object> { ["ticket"] = "t", ["operation"] = "install", ["subject"] = "p", ["hwid"] = "0461aaaa88180000" })), "admit refused");
                    T.Eq("0461bbbb881800001e6700006c118000", seen, "the admitter got the console's own identity");
                    T.Eq("backup-required", Harness.Err(h.Call("mkdir", new Dictionary<string, object> { ["path"] = "/var/lib/ccs/x" })), "still closed");
                }
            });
            T.Add("admission", "a granted admission opens writes for this connection only; disconnect voids it", () =>
            {
                string op = null, sub = null;
                using (var h = new Harness(Shell(), writes: true, admitted: false, admitter: (t, o, s, hw, live) => { op = o; sub = s; return new CcsBackup.Engine.AdmissionResult { Ok = true, Code = "admitted", BaselinePath = "b.zip" }; }))
                {
                    T.True(Harness.Err(h.Call("admit", new Dictionary<string, object> { ["ticket"] = "0123", ["operation"] = "install", ["subject"] = "plan-hash" })).StartsWith("(result)"), "admitted");
                    T.Eq("install", op, "operation passed through"); T.Eq("plan-hash", sub, "subject passed through");
                    T.True(Harness.Err(h.Call("mkdir", new Dictionary<string, object> { ["path"] = "/var/lib/ccs/x" })) != "backup-required", "a write now reaches the operation");
                    h.Call("disconnect");
                    T.Eq("backup-required", Harness.Err(h.Call("mkdir", new Dictionary<string, object> { ["path"] = "/var/lib/ccs/x" })), "after disconnect the admission is void");
                }
            });
            T.Add("admission", "the write tier is still asked first: with writes closed an admission changes nothing", () =>
            {
                using (var h = new Harness(Shell(), writes: false, admitted: true))
                    T.Eq("physical-disabled", Harness.Err(h.Call("mkdir", new Dictionary<string, object> { ["path"] = "/var/lib/ccs/x" })), "closed write tier");
            });
            T.Add("admission", "a removal with data, and an install, read Classic Cart Studio's data area on THIS connection for the admitter (read only)", () =>
            {
                const string sha = "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
                var sh = new ScriptedShell
                {
                    Answer = (c, i) => c.Contains("hakchi hwid") ? Tuple.Create(0, "0461aaaa 88180000 1e670000 6c118000\n", "") : c.Contains("osrelease") ? Tuple.Create(0, "3.4.113.29-madmonkey\n", "")
                        : c.Contains("R='/var/lib/ccs'") ? Tuple.Create(0, "tree\nd\t/var/lib/ccs\nd\t/var/lib/ccs/data\nf\t/var/lib/ccs/data/save.bin\t" + sha + "\nl\t/var/lib/ccs/pkg/current\tabc\nend\n", "") : Tuple.Create(0, "", "")
                };
                CcsBackup.Engine.LiveTree got = null; string gotOp = null;
                using (var h = new Harness(sh, writes: true, admitted: false, admitter: (t, o, s, hw, live) => { got = live; gotOp = o; return new CcsBackup.Engine.AdmissionResult { Ok = true, Code = "admitted", BaselinePath = "b.zip" }; }))
                {
                    h.Call("admit", new Dictionary<string, object> { ["ticket"] = "0123", ["operation"] = "uninstall-data", ["subject"] = "rev" });
                    T.True(gotOp == "uninstall-data" && got != null && got.Problem == null && got.Entries.Count == 4, "the admitter got the console's list (" + (got?.Problem ?? got?.Entries.Count.ToString()) + ")");
                    T.True(got.Entries["/ccs/data/save.bin"] == "f:" + sha && got.Entries["/ccs/pkg/current"] == "l:abc" && got.Entries["/ccs"] == "d", "in the backup inventory's terms (/var/lib/ccs is /ccs)");
                    var reads = sh.Commands.Count(c => c.Contains("R='/var/lib/ccs'"));
                    got = null; h.Call("admit", new Dictionary<string, object> { ["ticket"] = "0124", ["operation"] = "install", ["subject"] = "plan" });
                    // an install reads the data area's list too (read only, once): a data-only admission compares it (Admission.MayNeedLiveTree)
                    T.True(got != null && got.Problem == null && sh.Commands.Count(c => c.Contains("R='/var/lib/ccs'")) == reads + 1, "an install reads the data area's list once, read only, for the admitter (a data-only admission needs it)");
                }
            });
            T.Add("admission", "the data area's list that cannot be read or told apart reaches the admitter as a problem (never as an empty console)", () =>
            {
                foreach (var answer in new[] { "tree\nd\t/var/lib/ccs\n", "link\n", "tree\nd\t/var/lib/ccs\nf\t/var/lib/ccs/a\tnot-a-hash\nend\n", "tree\nd\t/etc/passwd\nend\n" })
                {
                    var sh = new ScriptedShell { Answer = (c, i) => c.Contains("hakchi hwid") ? Tuple.Create(0, "0461aaaa 88180000 1e670000 6c118000\n", "") : c.Contains("R='/var/lib/ccs'") ? Tuple.Create(0, answer, "") : Tuple.Create(0, "", "") };
                    CcsBackup.Engine.LiveTree got = null;
                    using (var h = new Harness(sh, writes: true, admitted: false, admitter: (t, o, s, hw, live) => { got = live; return new CcsBackup.Engine.AdmissionResult { Ok = false, Code = "current-backup-required", Text = "x" }; }))
                        h.Call("admit", new Dictionary<string, object> { ["ticket"] = "0123", ["operation"] = "recover", ["subject"] = "attempt" });
                    T.True(got != null && got.Problem != null, "a problem for: " + answer.Replace("\n", "|").Replace("\t", " "));
                }
            });
            T.Add("admission", "the production admitter refuses a ticket that names no backup at all (Codex cbr1 F1), for an install and a removal with data", () =>
            {
                var d = Path.Combine(Path.GetTempPath(), "ccs-adm-" + Guid.NewGuid().ToString("N").Substring(0, 6)); Directory.CreateDirectory(d);
                var st = new CcsBackup.Library.Settings { Path = Path.Combine(d, "settings.json"), PrimaryFolder = Path.Combine(d, "b") }; st.Save();
                Directory.CreateDirectory(st.RecordsFolder);
                foreach (var op in new[] { "install", "uninstall-data" })
                {
                    var id = Guid.NewGuid().ToString("N");
                    File.WriteAllText(Path.Combine(st.RecordsFolder, "admission-" + id + ".json"), "{\"schema\":\"ccs-maintenance-admission/2\",\"ticket\":\"" + id + "\",\"operation\":\"" + op + "\",\"subject\":\"p\",\"hwid\":\"0461aaaa 88180000 1e670000 6c118000\",\"simulated\":false,\"issued_utc\":\"" + DateTime.UtcNow.ToString("o") + "\",\"expires_utc\":\"" + DateTime.UtcNow.AddMinutes(8).ToString("o") + "\",\"current_proof\":\"console-data-list\"}");
                    var sh = new ScriptedShell { Answer = (c, i) => c.Contains("hakchi hwid") ? Tuple.Create(0, "0461aaaa 88180000 1e670000 6c118000\n", "") : c.Contains("R='/var/lib/ccs'") ? Tuple.Create(0, "none\n", "") : Tuple.Create(0, "", "") };
                    using (var h = new Harness(sh, writes: true, admitted: false, admitter: (t, o, s, hw, live) => CcsBackup.Engine.Admission.Use(CcsBackup.Library.Settings.Load(st.Path), t, o, s, hw, null, false, live, null)))
                    {
                        T.Eq("backup-required", Harness.Err(h.Call("admit", new Dictionary<string, object> { ["ticket"] = id, ["operation"] = op, ["subject"] = "p" })), op + ": refused");
                        T.Eq("backup-required", Harness.Err(h.Call("mkdir", new Dictionary<string, object> { ["path"] = "/var/lib/ccs/x" })), op + ": still closed");
                    }
                    T.True(!File.Exists(Path.Combine(st.RecordsFolder, "admission-" + id + ".used")), op + ": the ticket was not consumed");
                }
                try { Directory.Delete(d, true); } catch { }
            });
            T.Add("admission", "the production admitter refuses an admission it cannot find (CcsBackup.Engine.Admission.Use)", () =>
            {
                var d = Path.Combine(Path.GetTempPath(), "ccs-adm-" + Guid.NewGuid().ToString("N").Substring(0, 6)); Directory.CreateDirectory(d);
                var st = new CcsBackup.Library.Settings { Path = Path.Combine(d, "settings.json"), PrimaryFolder = Path.Combine(d, "b") }; st.Save();
                using (var h = new Harness(Shell(), writes: true, admitted: false, admitter: (t, o, s, hw, live) => CcsBackup.Engine.Admission.Use(CcsBackup.Library.Settings.Load(st.Path), t, o, s, hw, null, false, live, null)))
                {
                    T.Eq("backup-required", Harness.Err(h.Call("admit", new Dictionary<string, object> { ["ticket"] = "0123456789abcdef0123456789abcdef", ["operation"] = "install", ["subject"] = "p" })), "unknown ticket");
                    T.Eq("backup-required", Harness.Err(h.Call("put", new Dictionary<string, object> { ["path"] = "/var/lib/ccs/x" })), "still closed");
                }
                try { Directory.Delete(d, true); } catch { }
            });
            T.Add("admission", "the built worker's --admit-backup refuses a console with no backup (a JSON answer, exit 1)", () =>
            {
                var exe = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "ccs-console-worker", "Release", "net48", "ccs-console-worker.exe"));
                if (!File.Exists(exe)) throw new SkipException("the worker executable is not built beside the tests (" + exe + ")");
                var d = Path.Combine(Path.GetTempPath(), "ccs-adm-" + Guid.NewGuid().ToString("N").Substring(0, 6)); Directory.CreateDirectory(d);
                var st = new CcsBackup.Library.Settings { Path = Path.Combine(d, "settings.json"), PrimaryFolder = Path.Combine(d, "b") }; st.Save();
                var p = Process.Start(new ProcessStartInfo(exe, "--admit-backup --hwid 0461aaaa881800001e6700006c118000 --operation install --subject p --settings \"" + st.Path + "\"") { UseShellExecute = false, RedirectStandardOutput = true, CreateNoWindow = true });
                var o = p.StandardOutput.ReadToEnd(); p.WaitForExit(60000);
                T.Eq(1, p.ExitCode, "exit code"); T.True(o.Contains("\"code\": \"backup-required\""), o);
                try { Directory.Delete(d, true); } catch { }
            });
        }
    }
}
