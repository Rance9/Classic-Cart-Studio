// Classic Cart Studio console backup and restore engine tests: CCS HOME's package placed by the Workbench (Engine/HomePackage; ccsprep hput,
// huse). GPL-3.0-or-later. Simulated consoles only (files on this PC); the real-console boundary (Gate.PreparationWrites) stays closed.
// tools/console-backup/ram/test_recovery_images.py runs the real ccsprep steps on the image's own BusyBox; tools/console-nucleus/
// test_nucleus.py --only chain runs hakchi's start with the result and the package's own console player.
using CcsBackup.Device;
using CcsBackup.Engine;
using CcsBackup.Library;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using static CcsBackup.Tests.Program;

namespace CcsBackup.Tests
{
    static partial class Tests
    {
        /// <summary>a small console package in the app's format (native_home/console_package.py): a player, its session, a few HOME files</summary>
        internal static string MakeHome(string layout, string tag, int kb = 64)
        {
            var d = NewDir("home-" + layout + "-" + tag);
            var files = new SortedDictionary<string, (byte[] b, string mode)>(StringComparer.Ordinal)
            {
                { "bin/ccs-player-armv7", (Encoding.ASCII.GetBytes("\x7f" + "ELF stand-in player " + tag), "0o755") },
                { "session.json", (Encoding.ASCII.GetBytes("{\"schema\": \"ccs-play-session/1\", \"leave\": \"" + layout + "\", \"tag\": \"" + tag + "\"}\n"), "0o644") },
                { "config/controller-map.txt", (Encoding.ASCII.GetBytes("key 304 a\n"), "0o644") },
                { "startup/startup.json", (Encoding.ASCII.GetBytes("{\"schema\": \"ccs-startup/1\"}\n"), "0o644") },
                { "home/pack.json", (Encoding.ASCII.GetBytes("{\"tag\": \"" + tag + "\"}\n"), "0o644") },
                { "home/art/scene.bin", (Enumerable.Range(0, kb * 1024).Select(i => (byte)(i * 7 + tag.Length)).ToArray(), "0o644") },
            };
            var fj = new JObj();
            foreach (var kv in files)
            {
                var p = Path.Combine(d, kv.Key.Replace('/', Path.DirectorySeparatorChar)); Directory.CreateDirectory(Path.GetDirectoryName(p)); File.WriteAllBytes(p, kv.Value.b);
                fj.Add(kv.Key, new JObj { { "size", (long)kv.Value.b.Length }, { "sha256", Sha(kv.Value.b) }, { "kind", "test" }, { "mode", kv.Value.mode } });
            }
            var man = new JObj { { "schema", "ccs-console-package/1" }, { "label", "Test " + tag }, { "layout", layout }, { "console_paths", new JObj { { "pkg", "/tmp/ccs/pkg" }, { "data", "/var/lib/ccs/data" } } },
                { "games", 1L }, { "playable", 1L }, { "player", new JObj { { "file", "bin/ccs-player-armv7" }, { "core", "fceumm ac0d00a7" } } }, { "total_bytes", files.Sum(x => (long)x.Value.b.Length) }, { "files", fj } };
            File.WriteAllText(Path.Combine(d, "MANIFEST.json"), Json.Write(man));
            return d;
        }

        static string HomeData(string c, params string[] p) => Path.Combine(new[] { c, "ccs-data" }.Concat(p).ToArray());
        static string Pointer(string c) => File.Exists(HomeData(c, "pkg", "current")) ? File.ReadAllText(HomeData(c, "pkg", "current")) : null;
        static void SetCfgValue(string c, string k, object v) { var f = Path.Combine(c, "console.json"); var j = Json.Parse(File.ReadAllText(f)); var o = new JObj(); foreach (var kv in j) if (kv.Key != k) o.Add(kv.Key, kv.Value); o.Add(k, v); File.WriteAllText(f, Json.Write(o)); }

        /// <summary>the HOME steps alone on a simulated console in a preparation session (the plans that carry them are tested in method and
        /// method-already): what the console holds decides them, they are written once each and read back</summary>
        static (HomeSteps.Decision d, List<WriteResult> runs, MethodState after) HomeRun(string c, HomePackage pkg, Func<HomeSteps.Decision, bool> beforeRun = null)
        {
            var con = RecoverySessions.OpenSimulated(c, ImageKind.Prepare); var id = con.Identify();
            var d = HomeSteps.Plan(pkg, con.PrepState(), id.Hwid); var runs = new List<WriteResult>();
            if (d.Refusal != null || beforeRun != null && !beforeRun(d)) return (d, runs, null);
            var auth = new WriteAuthority("test", id.Hwid, d.Steps.ToArray(), () => true, "test");
            foreach (var s in d.Steps) { var r = HomeSteps.Run(con, auth, s, d.Archive, d.Prior); runs.Add(r); if (!HomeSteps.Answer(r, s, d.Archive)) break; }
            return (d, runs, con.PrepState());
        }

        public static void Home()
        {
            // ---- the release-built package this build carries (tools/console-nucleus/build_home_package.py)
            var em = HomePackage.Embedded("mod");
            Check(em != null && HomePackage.Embedded("nucleus") == null, "this build carries the release-built CCS HOME package in its one layout, CCS Mod's (CCS Nucleus never installs a HOME)" + (em == null ? " (NOT CARRIED: build with ccs-home/ in the console programs' folder)" : " (" + em.Describe + ")"));
            if (em != null)
            {
                var bad = em.Files.Where(f => { try { if (f.Rel != "MANIFEST.json") em.Read(f.Rel); return false; } catch (HomePackageException) { return true; } }).Select(f => f.Rel).ToList();
                Check(bad.Count == 0 && em.Playable == em.Games && em.Games > 0 && em.Files.Any(f => f.Rel == "bin/ccs-player-armv7" && f.Mode == "0755") && em.Files.Any(f => f.Rel.StartsWith("home/")) && em.Files.Any(f => f.Rel == "session.json"),
                    "every file reads back with its manifest's size and SHA-256; every game playable, the console player and the HOME inside (" + em.Files.Count + " files" + (bad.Count > 0 ? "; not readable: " + string.Join(", ", bad.Take(5)) : "") + ")");
            }
            // the records are the installer's own format (Python's json.dumps sort_keys indent 1), read back by Python's json the same way
            var h1 = HomePackage.Folder(MakeHome("mod", "one"), "mod");
            var a1 = HomeArchive.Build(h1, "0a1b2c3d", HwidA, "absent"); var a1b = HomeArchive.Build(h1, "0a1b2c3d", HwidA, "absent");
            var recs = UstarMembers(a1.Bytes);
            Check(a1.Copy == h1.Version + ".0a1b2c3d" && recs.Keys.Where(k => k.StartsWith("rec/") && !k.EndsWith("/")).OrderBy(x => x).SequenceEqual(new[] { "rec/IDENTITY.json", "rec/attempt-0a1b2c3d.json", "rec/v-" + a1.Copy + ".json" }.OrderBy(x => x))
                && recs.Keys.Count(k => k.StartsWith("pkg/") && !k.EndsWith("/")) == h1.Files.Count && recs["rec/v-" + a1.Copy + ".json"].SequenceEqual(UstarMembers(a1b.Bytes)["rec/v-" + a1.Copy + ".json"]),
                "the archive: pkg/ with every file of the package, rec/ with the attempt's, the copy's and the saves' identity records; the copy's record is the same bytes when the same attempt is reviewed again");
            if (Python != null)
            {
                var chk = Path.Combine(NewDir("pyjson"), "check.py"); var rf = Path.Combine(Path.GetDirectoryName(chk), "v.json"); File.WriteAllBytes(rf, recs["rec/v-" + a1.Copy + ".json"]);
                File.WriteAllText(chk, "import json,sys\nb=open(sys.argv[1],'rb').read()\no=json.loads(b)\nprint('SAME' if (json.dumps(o,sort_keys=True,indent=1)+'\\n').encode()==b and o['schema']=='ccs-owned-copy/1' and o['files']['MANIFEST.json']==o['manifest_sha256'] else 'DIFF')\n");
                var pi = new ProcessStartInfo(Python, "-I \"" + chk + "\" \"" + rf + "\"") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardInput = true, CreateNoWindow = true };
                using (var pr = Process.Start(pi)) { pr.StandardInput.Close(); var outp = pr.StandardOutput.ReadToEnd().Trim(); pr.WaitForExit(); Check(outp == "SAME", "the copy's record is byte for byte what ccs_install.py's json.dumps(sort_keys, indent 1) writes for the same content (" + outp + ")"); }
            }
            Check(Throws<HomePackageException>(() => HomePackage.Folder(MakeHome("mod", "x"), "nucleus")), "a package made for CCS Mod's layout is refused for CCS Nucleus (its leave item would say the wrong thing)");
            var tamper = MakeHome("mod", "tamper"); File.AppendAllText(Path.Combine(tamper, "session.json"), " ");
            Check(Throws<HomePackageException>(() => HomeArchive.Build(HomePackage.Folder(tamper, "mod"), "00000001", HwidA, "absent")), "a package file that differs from its manifest is refused before any archive is made");

            // ---- the steps on a simulated console (a preparation session; hakchi present)
            var c = SmallConsole("modified", true);
            var (d1, r1, s1) = HomeRun(c, h1);
            Check(d1.Refusal == null && d1.Steps.SequenceEqual(new[] { "hput", "huse" }) && d1.Prior == "absent" && r1.Count == 2 && r1.All(x => x.Ok) && HomeSteps.InUse(s1, d1.Archive) && Pointer(c) == d1.Archive.Copy + "\n"
                && File.Exists(HomeData(c, "data", "IDENTITY.json")) && Writes(c).SequenceEqual(new[] { "hput " + d1.Archive.Copy, "huse " + d1.Archive.Copy }),
                "a console without CCS HOME: the package sent (hput), checked file by file, then switched to (huse); the console reads it back in use, with the saves' identity (" + string.Join("; ", r1.Select(x => x.Line ?? x.Problem)) + ")");
            Check(h1.Files.All(f => File.Exists(HomeData(c, "pkg", d1.Archive.Copy, f.Rel)) && Sha(HomeData(c, "pkg", d1.Archive.Copy, f.Rel)) == f.Sha256) && !Directory.Exists(HomeData(c, "staging")) && !Directory.EnumerateFiles(HomeData(c, "install"), "attempt-*").Any(),
                "every file of the copy is on the console with its SHA-256; no staging folder or unfinished installation is left");
            var (d2, r2, _) = HomeRun(c, h1);
            Check(d2.Refusal == null && d2.Steps.Count == 0 && r2.Count == 0 && d2.Text.Contains("already"), "the same package again: nothing to do (" + d2.Text + ")");
            var ident = File.ReadAllBytes(HomeData(c, "data", "IDENTITY.json"));
            Directory.CreateDirectory(HomeData(c, "data", "saves")); File.WriteAllText(HomeData(c, "data", "saves", "game.sav"), "a save made on the console");
            var h2 = HomePackage.Folder(MakeHome("mod", "two"), "mod");
            var (d3, r3, s3) = HomeRun(c, h2);
            Check(d3.Steps.SequenceEqual(new[] { "hput", "huse" }) && d3.Prior == d1.Archive.Copy && r3.All(x => x.Ok) && HomeSteps.InUse(s3, d3.Archive) && Directory.Exists(HomeData(c, "pkg", d1.Archive.Copy))
                && File.ReadAllBytes(HomeData(c, "data", "IDENTITY.json")).SequenceEqual(ident) && File.ReadAllText(HomeData(c, "data", "saves", "game.sav")) == "a save made on the console",
                "an update: the new copy beside the one in use, switched only when complete; the earlier copy, the saves and their identity are kept");
            // interrupted while unpacking: the copy in use stays; the next review finishes the same attempt (its records reused, nothing else)
            var h3 = HomePackage.Folder(MakeHome("mod", "three", 256), "mod");
            File.WriteAllText(Path.Combine(c, "faults.json"), "{\"prep\":{\"step\":\"hput\"}}");
            var (d4, r4, _) = HomeRun(c, h3);
            Check(r4.Count == 1 && !r4[0].Ok && r4[0].Problem.StartsWith("failed write") && Pointer(c) == d3.Archive.Copy + "\n" && Directory.Exists(HomeData(c, "staging", d4.Archive.Copy)) && File.Exists(HomeData(c, "install", "attempt-" + d4.Archive.Attempt + ".json")),
                "the transfer stops while unpacking: stopped there, the HOME in use unchanged, the attempt's record and its staging folder left (" + r4[0].Problem + ")");
            File.Delete(Path.Combine(c, "faults.json"));
            var (d5, r5, s5) = HomeRun(c, h3);
            Check(d5.Archive.Attempt == d4.Archive.Attempt && d5.Steps.SequenceEqual(new[] { "hput", "huse" }) && r5.All(x => x.Ok) && !r5[0].Line.Contains("already") && HomeSteps.InUse(s5, d5.Archive) && !Directory.Exists(HomeData(c, "staging")),
                "a new review finishes that same attempt: its staging folder replaced, the copy completed, checked and switched to (" + string.Join("; ", r5.Select(x => x.Line)) + ")");
            // the switch interrupted: the next review sends nothing again (hput: already there), then switches
            var h4 = HomePackage.Folder(MakeHome("mod", "four"), "mod");
            var (d6, r6, _) = HomeRun(c, h4, d => { File.WriteAllText(Path.Combine(c, "faults.json"), "{\"prep\":{\"step\":\"huse\"}}"); return true; });
            File.Delete(Path.Combine(c, "faults.json"));
            Check(r6.Count == 2 && r6[0].Ok && !r6[1].Ok && Pointer(c) == d5.Archive.Copy + "\n", "the switch fails: the complete new copy waits, the HOME in use is unchanged (" + r6[1].Problem + ")");
            var (d7, r7, s7) = HomeRun(c, h4);
            Check(d7.Archive.Copy == d6.Archive.Copy && r7.Count == 2 && r7[0].Line.Contains(" ok already ") && r7[1].Ok && HomeSteps.InUse(s7, d7.Archive), "the next review: the copy is already there (not sent again), the switch finishes (" + string.Join("; ", r7.Select(x => x.Line)) + ")");
            // a failed readback: nothing switched
            File.WriteAllText(Path.Combine(c, "faults.json"), "{\"home_readback_differs\":true}");
            var (d8, r8, _) = HomeRun(c, HomePackage.Folder(MakeHome("mod", "five"), "mod"));
            File.Delete(Path.Combine(c, "faults.json"));
            Check(r8.Count == 1 && r8[0].Problem != null && r8[0].Problem.StartsWith("failed readback") && Pointer(c) == d7.Archive.Copy + "\n", "a copy that does not read back as its record: nothing is switched (" + r8[0].Problem + ")");
            var (d9, _, _) = HomeRun(c, HomePackage.Folder(MakeHome("mod", "six"), "mod"));
            Check(d9.Refusal != null && d9.Refusal.Contains("did not finish"), "with that unfinished installation on the console, another package is refused (" + d9.Refusal + ")");
            GoneConsole(c);

            // ---- refusals before anything is written
            string Refusal(Action<string> setup, HomePackage p = null) { var cc = SmallConsole("modified", true); setup(cc); var (dx, rx, _) = HomeRun(cc, p ?? h1); var w = Writes(cc); GoneConsole(cc); return dx.Refusal != null && rx.Count == 0 && w.Count == 0 ? dx.Refusal : null; }
            var why1 = Refusal(cc => { Directory.CreateDirectory(HomeData(cc, "pkg")); File.WriteAllText(HomeData(cc, "pkg", "current"), "someone else's\n"); });
            Check(why1 != null && why1.Contains("not a HOME this program recorded"), "a pointer this program did not write: refused, kept (" + why1 + ")");
            var why2 = Refusal(cc => { Directory.CreateDirectory(HomeData(cc, "data")); File.WriteAllText(HomeData(cc, "data", "IDENTITY.json"), "{\"schema\": \"ccs-console-data/1\", \"state_identity\": {\"platform\": \"linux-armv7\", \"core\": \"fceumm\", \"core_version\": \"0ld0ld\"}}"); });
            Check(why2 != null && why2.Contains("another game player build"), "saves made by another player build: refused, the saves kept (" + why2 + ")");
            var why3 = Refusal(cc => SetCfgValue(cc, "data_free_bytes", 1000000L));
            Check(why3 != null && why3.Contains("Not enough room"), "not enough room for the package and the save reserve: refused (" + why3 + ")");
            var why4 = Refusal(cc => SetCfgValue(cc, "ram_bytes", 4000000L));
            Check(why4 != null && why4.Contains("memory"), "not enough RAM to receive the transfer: refused (" + why4 + ")");
            var why5 = Refusal(cc => File.WriteAllText(Path.Combine(cc, "faults.json"), "{\"home_link\":true}"));
            Check(why5 != null && why5.Contains("link"), "a link on the way to Classic Cart Studio's folder: refused, nothing written through it (" + why5 + ")");
            var why6 = Refusal(cc => { Directory.CreateDirectory(HomeData(cc, "install")); File.WriteAllText(HomeData(cc, "install", "attempt-deadbeef.json"), "{\"schema\": \"ccs-install-attempt/1\", \"copy\": \"0123456789abcdef.deadbeef\"}"); });
            Check(why6 != null && why6.Contains("did not finish"), "another program's unfinished installation (Classic Cart Studio's installer): refused, kept (" + why6 + ")");
            Check(Refusal(_ => { }, null) == null, "(control) the same console without those: no refusal");
            var cc2 = SmallConsole("modified", true); var con2 = RecoverySessions.OpenSimulated(cc2, ImageKind.Prepare); var id2 = con2.Identify();
            var dd = HomeSteps.Plan(h1, con2.PrepState(), id2.Hwid); var au = new WriteAuthority("t", id2.Hwid, new[] { "hput" }, () => true, "t");
            Check(Throws<ArgumentException>(() => con2.PrepStepStream("hput", au, new[] { dd.Archive.Copy, (dd.Archive.Size + 1).ToString(), dd.Archive.Sha256, dd.Archive.Total.ToString() }, new MemoryStream(dd.Archive.Bytes), dd.Archive.Size))
                && Throws<ArgumentException>(() => con2.PrepStep("hput", au, new[] { dd.Archive.Copy, dd.Archive.Size.ToString(), dd.Archive.Sha256, dd.Archive.Total.ToString() })) && Writes(cc2).Count == 0,
                "hput's bytes go only through the stream whose size its arguments name");
            var damaged = dd.Archive.Bytes.ToArray(); damaged[damaged.Length / 2] ^= 1; var wrong = new MemoryStream(damaged);
            var rw = con2.PrepStepStream("hput", au, new[] { dd.Archive.Copy, dd.Archive.Size.ToString(), dd.Archive.Sha256, dd.Archive.Total.ToString() }, wrong, dd.Archive.Size);
            Check(!rw.Ok && rw.Problem.StartsWith("refused received-mismatch") && Writes(cc2).Count == 0 && !Directory.Exists(HomeData(cc2, "pkg")), "an archive damaged on the way: refused before anything is written (" + rw.Problem + ")");
            var au2 = new WriteAuthority("t", id2.Hwid, new[] { "hput" }, () => true, "t");
            var rc = con2.PrepStepStream("hput", au2, new[] { dd.Archive.Copy, dd.Archive.Size.ToString(), dd.Archive.Sha256, dd.Archive.Total.ToString() }, new EndsAfter(dd.Archive.Bytes, 20000), dd.Archive.Size);
            Check(!rc.Ok && au2.Revoked && Writes(cc2).Count == 0 && !Directory.Exists(HomeData(cc2, "pkg")) && !Directory.Exists(HomeData(cc2, "install")),
                "the PC side ends while the archive is on its way (the worker or its window gone): nothing is written on the console, and this operation writes nothing more (" + rc.Problem + ")");
            GoneConsole(cc2);

            // ---- the release-built package itself on a simulated console (its real size through the same steps)
            if (em != null)
            {
                var ce = SmallConsole("modified", true); var (de, re, se) = HomeRun(ce, em);
                Check(de.Refusal == null && re.Count == 2 && re.All(x => x.Ok) && HomeSteps.InUse(se, de.Archive) && em.Files.All(f => Sha(HomeData(ce, "pkg", de.Archive.Copy, f.Rel)) == f.Sha256),
                    "the release-built package (" + CcsBackup.Compose.Units.Size(de.Archive?.Size ?? 0) + " in one transfer) placed, checked and in use on a simulated console (" + string.Join("; ", re.Select(x => x.Line ?? x.Problem)) + ")");
                GoneConsole(ce);
            }
        }

        /// <summary>with CCS_HOME_CHAIN_OUT and --utility: the built Workbench's own worker prepares a console (already prepared with hakchi)
        /// with CCS Mod and CCS Nucleus opening CCS HOME at power-on; the archive it sends for CCS HOME is kept in that folder (home-archive.tar
        /// and its arguments, the add-ons and CCS Nucleus's start among them) for the image's own steps under qemu (test_recovery_images.py
        /// --home-archive) and hakchi's start with the package's own player (test_nucleus.py --only chain)</summary>
        public static void HomeChain()
        {
            var outDir = Environment.GetEnvironmentVariable("CCS_HOME_CHAIN_OUT");
            if (string.IsNullOrEmpty(outDir)) throw new SkipException("CCS_HOME_CHAIN_OUT is not set");
            if (Utility == null || !File.Exists(Utility)) throw new SkipException("the built Workbench (--utility) is needed");
            var c = Console_("modified"); var s = SettingsIn(NewDir("home-chain"), false); Backup(c, s); SetCfgValue(c, "keep_home_archive", true);
            var p = Process.Start(new ProcessStartInfo(Utility, "--backup-worker --settings \"" + s.Path + "\"") { UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true });
            p.ErrorDataReceived += (o, e) => { }; p.BeginErrorReadLine();
            var w = WorkerHost.RequestWriter(p.StandardInput.BaseStream);
            var addons = new AddonChoice { Mod = true, Nucleus = "direct", Start = "ccs-home" };
            w.WriteLine(WorkerHost.RequestLine(new JObj { { "cmd", "prepare-plan" }, { "console", "simulated" }, { "folder", c }, { "mod", addons.Mod }, { "nucleus", addons.Nucleus }, { "start", addons.Start } }));
            Dictionary<string, object> plan = null, result = null; string line;
            while ((line = p.StandardOutput.ReadLine()) != null)
            {
                Dictionary<string, object> ev; try { ev = Json.Parse(line); } catch (FormatException) { continue; }
                if (ev.S("event") == "prep-plan") { plan = ev.O("plan"); w.WriteLine(WorkerHost.RequestLine(new JObj { { "cmd", "prepare-confirm" }, { "digest", plan.S("digest") }, { "nonce", plan.S("nonce") } })); }
                else if (ev.S("event") == "result") { result = ev; w.WriteLine(WorkerHost.RequestLine(new JObj { { "cmd", "quit" } })); break; }
            }
            w.Close(); p.WaitForExit(60000);
            var steps = (plan?.A("steps") ?? new List<object>()).OfType<string>().ToList();
            var startFile = Path.Combine(c, "nucleus-start.txt");
            Check(plan != null && steps.SequenceEqual(new[] { "method:ccs-mod", "method:ccs-nucleus", "nstart", "hput", "huse" }) && plan.S("start") == addons.StartLine && result?.S("state") == "method-scheduled" && File.Exists(Path.Combine(c, "home-archive.tar")) && Pointer(c) == plan.O("home")?.S("copy") + "\n"
                  && File.Exists(startFile) && File.ReadAllText(startFile).Trim() == addons.StartLine,
                "the built Workbench's worker (" + Path.GetFileName(Utility) + ") prepares the console with CCS Mod, CCS Nucleus opening CCS HOME at power-on, and the release-built CCS HOME package: both packages, CCS Nucleus's start, hput, huse; the start recorded (" + addons.StartLine + "), CCS HOME in use (" + (result?.S("state") ?? "no result") + " " + result?.S("problem") + ")");
            if (plan == null || !File.Exists(Path.Combine(c, "home-archive.tar"))) { GoneConsole(c); GoneBackups(s); return; }
            Directory.CreateDirectory(outDir);
            File.Copy(Path.Combine(c, "home-archive.tar"), Path.Combine(outDir, "home-archive.tar"), true);
            var args = Json.Parse(File.ReadAllText(Path.Combine(c, "home-archive.json"))); var em = HomePackage.Embedded("mod");
            File.WriteAllText(Path.Combine(outDir, "home-archive.json"), Json.Write(new JObj { { "copy", args.S("copy") }, { "size", args.L("size") }, { "sha256", args.S("sha256") }, { "total", args.L("total") }, { "files", (long)em.Files.Count },
                { "layout", plan.O("home")?.S("layout") }, { "manifest_sha256", em.ManifestSha256 }, { "addons", addons.ToJson() }, { "start", plan.S("start") }, { "worker", Path.GetFileName(Utility) }, { "worker_sha256", Sha(Utility) } }));
            Check(Sha(Path.Combine(outDir, "home-archive.tar")) == plan.O("home")?.S("archive_sha256"), "the archive kept for the chain is the one the reviewed plan bound by its SHA-256 (" + outDir + ")");
            GoneConsole(c); GoneBackups(s);
        }

        static bool Throws<T>(Action a) where T : Exception { try { a(); return false; } catch (T) { return true; } }
        /// <summary>a source that fails after n bytes, as a PC-side stream does when its process or connection ends</summary>
        sealed class EndsAfter : Stream
        {
            readonly byte[] b; readonly int n; int pos;
            public EndsAfter(byte[] b, int n) { this.b = b; this.n = n; }
            public override int Read(byte[] buf, int o, int c) { if (pos >= n) throw new IOException("the source ended"); int k = Math.Min(c, Math.Min(n, b.Length) - pos); Array.Copy(b, pos, buf, o, k); pos += k; return k; }
            public override bool CanRead => true; public override bool CanSeek => false; public override bool CanWrite => false; public override long Length => b.Length;
            public override long Position { get => pos; set => throw new NotSupportedException(); }
            public override void Flush() { } public override long Seek(long o, SeekOrigin s) => throw new NotSupportedException(); public override void SetLength(long v) => throw new NotSupportedException(); public override void Write(byte[] x, int o, int c) => throw new NotSupportedException();
        }
        static Dictionary<string, byte[]> UstarMembers(byte[] b)
        {
            var o = new Dictionary<string, byte[]>(StringComparer.Ordinal); int p = 0;
            while (p + 512 <= b.Length && b.Skip(p).Take(512).Any(x => x != 0))
            {
                var name = Encoding.ASCII.GetString(b, p, 100).TrimEnd('\0'); var size = Convert.ToInt64(Encoding.ASCII.GetString(b, p + 124, 11), 8);
                var d = new byte[size]; Array.Copy(b, p + 512, d, 0, size); o[name] = d; p += 512 + (int)((size + 511) / 512 * 512);
            }
            return o;
        }
    }
}
