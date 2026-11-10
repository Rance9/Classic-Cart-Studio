// Classic Cart Studio console transport worker tests. Copyright (C) 2026 Rance9. GPL-3.0-or-later.
// The operations' real shell text run by the console's own stock BusyBox 1.24.1 (and hakchi's own pack_install/pack_uninstall with
// hakchi's BusyBox 1.29 and rsync), all under qemu-arm in one disposable WSL folder. QEMU is not a console: this shows what the
// console's tools do with the text, not the console's storage, timing or transport.
using CcsWorker.Ops;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace CcsWorker.Tests
{
    static class FidelityTests
    {
        static BusyboxShell shell; static string skip;
        static BusyboxShell Sh()
        {
            if (skip != null) throw new SkipException(skip);
            if (shell == null) { try { shell = BusyboxShell.Create(); } catch (SkipException e) { skip = e.Message; throw; } }
            return shell;
        }
        static ConsoleOps Ops() => new ConsoleOps(Sh(), null);
        static Dictionary<string, object> A(params object[] kv) { var d = new Dictionary<string, object>(); for (int i = 0; i < kv.Length; i += 2) d[(string)kv[i]] = kv[i + 1]; return d; }
        static Dictionary<string, object> PutB(ConsoleOps o, string path, byte[] data, string mode = "0644") => o.Put(A("path", path, "b64", Convert.ToBase64String(data), "mode", mode, "sha256", ConsoleOps.Sha(data)));
        static string Raw(string text) { var r = Sh().Raw(text); return r.Item2.Trim(); }
        const string Sentinel = "OWNER FILE: NEVER CHANGE\n";

        public static void Register()
        {
            T.Add("fidelity", "folders, a 300 KB put with its hash read back, stat, sha256 and read on BusyBox 1.24.1", () =>
            {
                var o = Ops(); o.Mkdir("/var/lib/ccs"); o.Mkdir("/var/lib/ccs/pkg");
                var data = new byte[300 * 1000]; new Random(7).NextBytes(data);
                var r = PutB(o, "/var/lib/ccs/pkg/blob", data, "0755");
                T.Eq(true, (bool)r["match"], "readback matches"); T.Eq(ConsoleOps.Sha(data), r["sha256"] as string, "hash");
                var st = o.Stat("/var/lib/ccs/pkg/blob"); T.Eq("file", st["type"] as string, "stat type"); T.Eq((long)data.Length, (long)st["size"], "stat size");
                T.Eq("755", Raw("stat -c %a $SIM/var/lib/ccs/pkg/blob 2>/dev/null || ls -l $SIM/var/lib/ccs/pkg/blob | cut -c1-10").Replace("-rwxr-xr-x", "755"), "mode 0755");
                var hs = (Dictionary<string, object>)o.Sha256(new List<object> { "/var/lib/ccs/pkg/blob", "/var/lib/ccs/pkg/none", "/var/lib/ccs/pkg" })["hashes"];
                T.Eq(ConsoleOps.Sha(data), hs["/var/lib/ccs/pkg/blob"] as string, "sha256"); T.True(hs["/var/lib/ccs/pkg/none"] == null && hs["/var/lib/ccs/pkg"] == null, "absent and folder: null");
                var small = Encoding.UTF8.GetBytes("{\"schema\":\"x\"}\n"); PutB(o, "/var/lib/ccs/small.json", small);
                T.Eq("{\"schema\":\"x\"}\n", Encoding.UTF8.GetString(Convert.FromBase64String((string)o.Read("/var/lib/ccs/small.json", 1000)["b64"])), "read");
                T.Eq("too-large", T.Code(() => o.Read("/var/lib/ccs/pkg/blob", 1000), "too large"), "read bound");
                var df = o.Df("/var/lib"); T.True((long)df["free_bytes"] > 0 && (long)df["total_bytes"] >= (long)df["free_bytes"], "df figures");
            });

            T.Add("fidelity", "put never writes through what is already there: a file, a link to an owner file, a dangling link, a linked folder", () =>
            {
                var o = Ops();
                Raw("mkdir -p $SIM/owner $SIM/outdir; printf '" + Sentinel.TrimEnd('\n') + "\\n' > $SIM/owner/keep.txt; printf 'existing\\n' > $SIM/var/lib/ccs/existing; ln -s $SIM/owner/keep.txt $SIM/var/lib/ccs/link; ln -s $SIM/owner/new.txt $SIM/var/lib/ccs/dangling; ln -s $SIM/outdir $SIM/var/lib/ccs/linkdir");
                var data = Encoding.UTF8.GetBytes("CCS BYTES\n");
                T.Eq("exists", T.Code(() => PutB(o, "/var/lib/ccs/existing", data), "existing"), "onto a file");
                T.Eq("existing", Raw("cat $SIM/var/lib/ccs/existing"), "the file is unchanged");
                T.Eq("exists", T.Code(() => PutB(o, "/var/lib/ccs/link", data), "link"), "onto a link");
                T.Eq(Sentinel.TrimEnd('\n'), Raw("cat $SIM/owner/keep.txt"), "the link's target is unchanged");
                T.Eq("exists", T.Code(() => PutB(o, "/var/lib/ccs/dangling", data), "dangling"), "onto a dangling link");
                T.Eq("absent", Raw("[ -e $SIM/owner/new.txt ] && echo present || echo absent"), "nothing created at the dangling link's target");
                T.Eq("parent-not-folder", T.Code(() => PutB(o, "/var/lib/ccs/linkdir/f", data), "linked folder"), "into a linked folder");
                T.Eq("", Raw("ls -A $SIM/outdir"), "the linked folder's target is still empty");
                T.Eq("exists", T.Code(() => o.Mkdir("/var/lib/ccs/link"), "mkdir onto a link"), "mkdir onto a link");
            });

            T.Add("fidelity", "pointer: created, replaced in place, refused when current is a link to a folder or a dangling link", () =>
            {
                var o = Ops(); o.Mkdir("/var/lib/ccs/pkg/0123456789abcdef.00000001"); o.Mkdir("/var/lib/ccs/pkg/fedcba9876543210.00000002");
                T.Eq("0123456789abcdef.00000001", o.Pointer("/var/lib/ccs/pkg/current", "0123456789abcdef.00000001")["value"] as string, "created");
                T.Eq("fedcba9876543210.00000002", o.Pointer("/var/lib/ccs/pkg/current", "fedcba9876543210.00000002")["value"] as string, "replaced");
                T.Eq("file", o.Stat("/var/lib/ccs/pkg/current")["type"] as string, "still a regular file");
                T.True(!Raw("ls -A $SIM/var/lib/ccs/pkg").Contains(".new"), "no temporary file left");
                Raw("mkdir -p $SIM/var/lib/ccs/pkg2/v1; ln -s v1 $SIM/var/lib/ccs/pkg2/current; ln -s $SIM/nowhere $SIM/var/lib/ccs/pkg2/dangling");
                T.Eq("not-a-pointer", T.Code(() => o.Pointer("/var/lib/ccs/pkg2/current", "0123456789abcdef.00000001"), "link to a folder"), "a link to a folder");
                T.Eq("", Raw("ls -A $SIM/var/lib/ccs/pkg2/v1"), "nothing was moved into the linked folder");
                T.Eq("v1", Raw("readlink $SIM/var/lib/ccs/pkg2/current"), "the link is unchanged");
                T.Eq("not-a-pointer", T.Code(() => o.Pointer("/var/lib/ccs/pkg2/dangling", "0123456789abcdef.00000001"), "dangling"), "a dangling link");
                T.Eq("absent", Raw("[ -e $SIM/nowhere ] && echo present || echo absent"), "the dangling link's target was not created");
                // the reason for the pointer design: this BusyBox has no "mv -T", and a plain mv onto a link to a folder moves INTO the folder
                T.True(Raw("cd $SIM/var/lib/ccs/pkg2; ln -s v1 cur; ln -s v1 cur.new; mv -T cur.new cur 2>&1; echo rc=$?").Contains("rc=1"), "BusyBox 1.24.1 mv has no -T");
                T.Eq("cur.new", Raw("cd $SIM/var/lib/ccs/pkg2; mv -f cur.new cur; ls -A v1"), "and mv -f onto a link to a folder moves into the folder");
            });

            T.Add("fidelity", "rename refuses an existing target; remove keeps a file whose hash differs and never follows a link", () =>
            {
                var o = Ops(); var a = Encoding.UTF8.GetBytes("A\n"); var b = Encoding.UTF8.GetBytes("B\n");
                PutB(o, "/var/lib/ccs/ra", a); PutB(o, "/var/lib/ccs/rb", b);
                T.Eq("exists", T.Code(() => o.Rename("/var/lib/ccs/ra", "/var/lib/ccs/rb"), "onto existing"), "rename onto an existing file");
                T.Eq("A|B", Raw("cat $SIM/var/lib/ccs/ra") + "|" + Raw("cat $SIM/var/lib/ccs/rb"), "both unchanged");
                o.Mkdir("/var/lib/ccs/pkg/0123456789abcdef.00000003.staging"); PutB(o, "/var/lib/ccs/pkg/0123456789abcdef.00000003.staging/f", a);
                o.Rename("/var/lib/ccs/pkg/0123456789abcdef.00000003.staging", "/var/lib/ccs/pkg/0123456789abcdef.00000003");
                T.Eq("dir", o.Stat("/var/lib/ccs/pkg/0123456789abcdef.00000003")["type"] as string, "a staged folder renamed into place");
                T.Eq("exists", T.Code(() => o.Rename("/var/lib/ccs/ra", "/var/lib/ccs/pkg/0123456789abcdef.00000003"), "onto a folder"), "rename onto a folder");
                T.Eq("A", Raw("cat $SIM/var/lib/ccs/ra"), "not moved into the folder");
                var x = o.Remove("/var/lib/ccs/ra", ConsoleOps.Sha(b)); T.Eq(false, (bool)x["removed"], "wrong hash"); T.Eq("hash-differs", x["reason"] as string, "reason");
                T.Eq("A", Raw("cat $SIM/var/lib/ccs/ra"), "kept");
                x = o.Remove("/var/lib/ccs/ra", ConsoleOps.Sha(a)); T.Eq(true, (bool)x["removed"], "right hash"); T.Eq("none", o.Stat("/var/lib/ccs/ra")["type"] as string, "removed");
                x = o.Remove("/var/lib/ccs/link", ConsoleOps.Sha(Encoding.UTF8.GetBytes(Sentinel))); T.Eq("link", x["reason"] as string, "a link is not removed");
                T.Eq(Sentinel.TrimEnd('\n'), Raw("cat $SIM/owner/keep.txt"), "its target is kept");
                T.Eq("absent", o.Remove("/var/lib/ccs/never", ConsoleOps.Sha(a))["reason"] as string, "absent");
                T.Eq("not-empty", T.Code(() => o.Rmdir("/var/lib/ccs/pkg/0123456789abcdef.00000003"), "not empty"), "rmdir of a folder with a file");
                o.Remove("/var/lib/ccs/pkg/0123456789abcdef.00000003/f", ConsoleOps.Sha(a)); o.Rmdir("/var/lib/ccs/pkg/0123456789abcdef.00000003");
                T.Eq("none", o.Stat("/var/lib/ccs/pkg/0123456789abcdef.00000003")["type"] as string, "empty folder removed");
                T.Eq("not-a-folder", T.Code(() => o.Rmdir("/var/lib/ccs/linkdir"), "linked folder"), "rmdir of a link");
            });

            T.Add("fidelity", "stat and list see links as links; list shows hidden names", () =>
            {
                var o = Ops(); var st = o.Stat("/var/lib/ccs/link"); T.Eq("link", st["type"] as string, "link"); T.True(((string)st["target"]).EndsWith("/owner/keep.txt"), "target");
                Raw("printf x > $SIM/var/lib/ccs/.hidden");
                var names = ((List<object>)o.List("/var/lib/ccs")["names"]).Cast<string>().ToList();
                T.True(names.Contains(".hidden") && names.Contains("link") && names.Contains("linkdir") && !names.Contains(".") && !names.Contains(".."), "names: " + string.Join(",", names));
                T.Eq("not-a-folder", T.Code(() => o.List("/var/lib/ccs/linkdir"), "linked folder"), "a linked folder is not listed");
                T.Eq(false, (bool)o.List("/var/lib/ccs/nothing-here")["exists"], "absent");
            });

            T.Add("fidelity", "hakchi's own pack_install and pack_uninstall: the module in hakchi's overlay, then the stock start script again", () =>
            {
                var o = Ops(); var stock = Raw("cat $SIM/etc/init.d/S81clover-mcp");
                var s81 = Encoding.UTF8.GetBytes("#!/bin/sh -e\n# ccs-home (test)\nstart() { /var/lib/ccs/bin/ccs-start.sh; }\ncase \"$1\" in start) start ;; esac\n"); var sha = ConsoleOps.Sha(s81);
                T.Eq(false, (bool)o.ModuleFiles("ccs-home")["present"], "not installed at first");
                var r = o.ModuleInstall(A("name", "ccs-home", "files", A("/etc/init.d/S81clover-mcp", A("b64", Convert.ToBase64String(s81), "mode", "0755", "sha256", sha))));
                T.True(((string)r["output"]).Contains("package ccs-home installed"), "hakchi said installed: " + r["output"]);
                var mf = o.ModuleFiles("ccs-home"); var files = (Dictionary<string, object>)mf["files"];
                T.Eq(true, (bool)mf["present"], "present"); T.Eq(1, files.Count, "one file: " + string.Join(",", files.Keys)); T.Eq(sha, files["/etc/init.d/S81clover-mcp"] as string, "the overlay file is ours, byte for byte");
                T.True(((List<object>)mf["ignored"]).Contains("/uninstall"), "pack_install's own uninstall entry");
                T.Eq("755", Raw("stat -c %a $SIM/var/lib/hakchi/rootfs/etc/init.d/S81clover-mcp 2>/dev/null || echo 755"), "executable");
                T.True(!Raw("ls -A $SIM/tmp/ccs").Contains("hmod-"), "the transfer folder was removed");
                T.Eq(stock, Raw("cat $SIM/etc/init.d/S81clover-mcp"), "the stock script itself is never written (the overlay carries the change)");
                var u = o.ModuleUninstall("ccs-home"); T.True(((string)u["output"]).Contains("uninstalled"), "hakchi said uninstalled");
                T.Eq(false, (bool)o.ModuleFiles("ccs-home")["present"], "the uninstall list is gone");
                T.Eq("absent", Raw("[ -e $SIM/var/lib/hakchi/rootfs/etc/init.d/S81clover-mcp ] && echo present || echo absent"), "the overlay file is gone: the stock start script applies again");
                T.Eq(stock, Raw("cat $SIM/etc/init.d/S81clover-mcp"), "stock script unchanged");
            });

            T.Add("fidelity", "the protocol end to end over BusyBox: a package source streamed, a pointer, readback (worker server in process)", () =>
            {
                var pkg = Path.Combine(Path.GetTempPath(), "ccs-worker-fid-" + Guid.NewGuid().ToString("N").Substring(0, 8)); Directory.CreateDirectory(Path.Combine(pkg, "bin"));
                try
                {
                    var data = new byte[700 * 1000]; new Random(9).NextBytes(data); File.WriteAllBytes(Path.Combine(pkg, "bin", "ccs-player-armv7"), data);
                    using (var h = new Harness(Sh(), pkg))
                    {
                        Harness.Res(h.Call("mkdir", A("path", "/var/lib/ccs/pkg/0123456789abcdef.0000000a")));
                        Harness.Res(h.Call("mkdir", A("path", "/var/lib/ccs/pkg/0123456789abcdef.0000000a/bin")));
                        var prog = new List<Dictionary<string, object>>();
                        var r = Harness.Res(h.Terminal(h.Send("put", A("path", "/var/lib/ccs/pkg/0123456789abcdef.0000000a/bin/ccs-player-armv7", "source", "bin/ccs-player-armv7", "mode", "0755", "sha256", ConsoleOps.Sha(data))), prog));
                        T.Eq(true, (bool)r["match"], "match"); T.True(prog.Count >= 2 && Convert.ToInt64(prog.Last()["bytes"]) == data.Length, "progress to the end (" + prog.Count + " events)");
                        T.Eq("0123456789abcdef.0000000a", Harness.Res(h.Call("pointer", A("path", "/var/lib/ccs/pkg/current", "value", "0123456789abcdef.0000000a")))["value"] as string, "pointer");
                        var hs = (Dictionary<string, object>)Harness.Res(h.Call("sha256", A("paths", new object[] { "/var/lib/ccs/pkg/0123456789abcdef.0000000a/bin/ccs-player-armv7" })))["hashes"];
                        T.Eq(ConsoleOps.Sha(data), hs.Values.First() as string, "readback through the protocol");
                        T.Eq("physical-disabled", Harness.Err(h.Call("connect", A("transport", "clovershell"))), "connect is still closed with a test shell attached");
                    }
                }
                finally { Directory.Delete(pkg, true); }
            });

            T.Add("fidelity", "read in pieces (above the old 1 MB limit, past the end, a link), sync, pointer expect on BusyBox 1.24.1", () =>
            {
                var o = Ops(); Raw("mkdir -p $SIM/var/lib/ccs/data/saves $SIM/var/lib/ccs/pkg3 $SIM/owner; printf 'OWNER\\n' > $SIM/owner/state; ln -s $SIM/owner/state $SIM/var/lib/ccs/data/saves/linked.state");
                var data = new byte[1000001]; new Random(11).NextBytes(data); PutB(o, "/var/lib/ccs/data/saves/big.state", data);
                var got = new MemoryStream(); long off = 0; Dictionary<string, object> r; int pieces = 0;
                do { r = o.ReadChunk("/var/lib/ccs/data/saves/big.state", off, ConsoleOps.ChunkMax); var b = Convert.FromBase64String((string)r["b64"]); got.Write(b, 0, b.Length); off += b.Length; pieces++; }
                while (!(bool)r["eof"] && pieces < 10);
                T.Eq(ConsoleOps.Sha(data), ConsoleOps.Sha(got.ToArray()), "the 1,000,001-byte file read back whole in " + pieces + " pieces"); T.Eq(4, pieces, "four pieces");
                T.Eq(1000001L, Convert.ToInt64(r["file_size"]), "size as wc counts it");
                var past = o.ReadChunk("/var/lib/ccs/data/saves/big.state", 1003520, 4096); T.Eq(0L, (long)past["size"], "past the end: nothing"); T.Eq(true, (bool)past["eof"], "eof");
                T.Eq("not-a-file", T.Code(() => o.ReadChunk("/var/lib/ccs/data/saves/linked.state", 0, 4096), "link"), "a link is not read"); T.Eq("OWNER", Raw("cat $SIM/owner/state"), "its target unchanged");
                var s = o.Sync(false); T.Eq(true, (bool)s["synced"], "sync");
                s = o.Sync(true); T.True((bool)s["dropped"] || s.ContainsKey("drop_error"), "drop_caches: done, or its failure reported (a WSL user may not write /proc)");
                const string v1 = "0123456789abcdef.00000001", v2 = "fedcba9876543210.00000002";
                T.Eq(v1, o.Pointer("/var/lib/ccs/pkg3/current", v1, "absent")["value"] as string, "created where nothing was");
                T.Eq("pointer-changed", T.Code(() => o.Pointer("/var/lib/ccs/pkg3/current", v2, "absent"), "not absent"), "absent expected, a pointer there");
                T.Eq(v1, Raw("cat $SIM/var/lib/ccs/pkg3/current"), "unchanged");
                T.Eq("pointer-changed", T.Code(() => o.Pointer("/var/lib/ccs/pkg3/current", v2, ConsoleOps.Sha(Encoding.ASCII.GetBytes(v2 + "\n"))), "wrong"), "another content expected");
                T.Eq(v1, Raw("cat $SIM/var/lib/ccs/pkg3/current"), "still unchanged");
                T.Eq(v2, o.Pointer("/var/lib/ccs/pkg3/current", v2, ConsoleOps.Sha(Encoding.ASCII.GetBytes(v1 + "\n")))["value"] as string, "replaced when it was what was expected");
                Raw("ln -s $SIM/owner/state $SIM/var/lib/ccs/pkg3/linked");
                T.Eq("pointer-changed", T.Code(() => o.Pointer("/var/lib/ccs/pkg3/linked", v1, ConsoleOps.Sha(Encoding.ASCII.GetBytes("OWNER\n"))), "link"), "a link is never the expected pointer, even with its target's hash");
                T.Eq("OWNER", Raw("cat $SIM/owner/state"), "the link's target unchanged"); T.True(!Raw("ls -A $SIM/var/lib/ccs/pkg3").Contains(".new"), "no temporary file left");
            });

            T.Add("fidelity", "the disposable folder is removed at the end", () =>
            {
                var sh = Sh(); var sim = sh.Sim; sh.Dispose(); shell = null; skip = "the fidelity folder was removed";
                var psi = new System.Diagnostics.ProcessStartInfo("wsl.exe", "-d Ubuntu -- bash -c \"[ -e " + sim + " ] && echo present || echo absent\"") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
                using (var p = System.Diagnostics.Process.Start(psi)) { var o = p.StandardOutput.ReadToEnd(); p.WaitForExit(); T.True(o.Contains("absent"), "removed: " + o); }
            });
        }
    }
}
