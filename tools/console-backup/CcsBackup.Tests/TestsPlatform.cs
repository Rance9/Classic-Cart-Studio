// Classic Cart Studio console backup and restore engine tests: the platform registry and its boundaries.
// GPL-3.0-or-later. No console, no USB device; the backups here are of simulated consoles.
using CcsBackup.Archive;
using CcsBackup.Compose;
using CcsBackup.Engine;
using CcsBackup.Library;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using static CcsBackup.Tests.Program;

namespace CcsBackup.Tests
{
    static partial class Tests
    {
        /// <summary>the repository's tools/platform folder, found above the test program (null when the build is elsewhere)</summary>
        static string PlatformSource()
        {
            for (var d = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory); d != null; d = d.Parent)
            {
                var p = Path.Combine(d.FullName, "tools", "platform");
                if (File.Exists(Path.Combine(p, "ccs-platform-registry.json"))) return p;
            }
            return null;
        }

        static Dictionary<string, object> D(object o) => o as Dictionary<string, object>;

        public static void Platform()
        {
            var src = PlatformSource();
            if (src == null) throw new SkipException("the repository's tools/platform folder is not above this test program");
            var file = File.ReadAllBytes(Path.Combine(src, "ccs-platform-registry.json"));
            Check(Sha(file) == Capabilities.RegistrySha256, "the engine's registry is tools/platform/ccs-platform-registry.json, byte for byte");

            // ---- the shared decisions (the same vectors the website and the Windows app's backend run)
            var vec = Json.Parse(File.ReadAllText(Path.Combine(src, "target-vectors.json"), Encoding.UTF8));
            int n = 0, good = 0; var bad = new List<string>();
            foreach (var v in (vec.A("vectors") ?? new List<object>()).OfType<Dictionary<string, object>>())
            {
                n++;
                TargetDecision d;
                if (v.ContainsKey("request")) d = Capabilities.CheckRequest(D(v["request"]));
                else
                {
                    var doc = D(v["doc"]); var lists = new List<IEnumerable<object>>();
                    if (v.S("entries") != null) lists.Add(doc.A(v.S("entries")));
                    d = Capabilities.ResolveTarget(doc, v.S("doc_kind"), lists.ToArray());
                }
                bool ok = d.Code == v.S("expect") && d.Ok == (v.S("expect") == "ok");
                if (ok && v.ContainsKey("legacy")) ok = d.Legacy == v.B("legacy");
                if (ok && v.O("target") != null) ok = v.O("target").Count == d.Target.Count && v.O("target").All(kv => d.Target.TryGetValue(kv.Key, out var x) && x == kv.Value as string);
                if (ok && !d.Ok) ok = !string.IsNullOrEmpty(d.Text) && !d.Text.ToLowerInvariant().Contains("snes");
                if (ok) good++; else bad.Add(v.S("name") + " -> " + d.Code);
            }
            Check(n >= 30 && good == n, "the shared target vectors: " + good + "/" + n + " decided as expected" + (bad.Count > 0 ? " (" + string.Join("; ", bad) + ")" : ""));

            // ---- the registry's invariants
            var all = Capabilities.Entries;
            Check(all.GroupBy(e => e.Kind + "/" + e.Id).All(g => g.Count() == 1) && all.All(e => e.Status == "supported" || e.Status == "reserved"), "every entry is unique and either supported or reserved");
            Check(all.Where(e => !e.Supported).All(e => !Capabilities.Allowed(e.Kind, e.Id)) && !Capabilities.Allowed("game-system", "snes") && !Capabilities.Allowed("capability", "snes-gameplay"), "no reserved entry is ever allowed");
            Check(Capabilities.SupportedIds("game-system").SequenceEqual(new[] { "nes" }) && Capabilities.SupportedIds("core").SequenceEqual(new[] { "fceumm" }) && Capabilities.SupportedIds("physical-model").SequenceEqual(new[] { "nes-classic-edition", "snes-classic-edition" }), "the only supported game system, core and console model are NES, FCEUmm and the NES Classic Edition");
            Check(all.Where(e => e.Kind == "core").All(e => e.Supported == e.Flag("bundled")) && Capabilities.Entry("core", "snes-unselected").Get("license") == null, "only the supported core is bundled; the reserved core has no license, pin or source (nothing is shipped for it)");
            Check(Capabilities.InputActions("nes-pad").SequenceEqual(new[] { "up", "down", "left", "right", "a", "b", "select", "start" }), "the NES controller definition is the eight existing actions, in the existing order");
            var snesPad = Capabilities.InputActions("snes-pad");
            Check(snesPad.Count == 12 && new[] { "x", "y", "l", "r" }.All(snesPad.Contains) && Capabilities.InputActions("nes-pad").All(snesPad.Contains) && !Capabilities.Allowed("input", "snes-pad"), "the reserved SNES controller definition adds X, Y, L and R to the same actions, and is not allowed");
            Check(!Capabilities.Entry("boot-option", "ccs-snes").Flag("visible") && !Capabilities.Allowed("boot-option", "ccs-snes"), "the reserved SNES boot option is never visible and never allowed");
            Check(Capabilities.ModelForBoard("dp-nes") == "nes-classic-edition" && new[] { "dp-hvc", "dp-shvc", "", null, "DP-NES" }.All(b => Capabilities.ModelForBoard(b) == null), "a board alone names only the NES Classic (the Super NES Classic is told apart by its region too)");
            Check(Capabilities.ModelFor("dp-shvc", "USA") == "snes-classic-edition" && Capabilities.ModelFor("dp-shvc", "EUR") == "snes-classic-edition" && new[] { "JPN", "usa", "", null, "EUR_USA" }.All(r => Capabilities.ModelFor("dp-shvc", r) == null)
                  && Capabilities.ModelFor("dp-nes", "EUR_USA") == "nes-classic-edition" && Capabilities.ModelFor("dp-hvc", "JPN") == null,
                  "board dp-shvc is the Super NES Classic only with REGION USA or EUR (the Super Famicom Classic Mini's JPN is not supported); the Famicom Mini is not supported");
            var copy = Capabilities.Entries.ToList(); copy.Clear();
            Check(Capabilities.Entries.Count == all.Count && typeof(RegistryEntry).GetProperties().All(p => !p.CanWrite), "the registry cannot be changed through what it hands out (copies; no setters)");
            var vendored = Path.Combine(src, "..", "player", "third_party", "fceumm", "VENDORED.md");
            Check(File.Exists(vendored) && File.ReadAllText(vendored).Contains(Capabilities.Entry("core", "fceumm").Get("pin")), "the supported core's pin is the vendored FCEUmm commit");

            // ---- the worker boundary: a request naming SNES stops before the console lock or any session
            JObj Ask(string line)
            {
                using (var i = new MemoryStream(new UTF8Encoding(false).GetBytes(line + "\n"))) using (var o = new MemoryStream())
                {
                    WorkerHost.RunOn(i, o, "tests", "CcsBackup.Tests", "test", Path.Combine(NewDir("worker-settings"), "settings.json"));
                    var lines = new UTF8Encoding(false).GetString(o.ToArray()).Split('\n').Where(l => l.Trim().Length > 0).ToList();
                    var last = lines.Count == 0 ? null : Json.Parse(lines.Last()); var j = new JObj(); if (last != null) foreach (var kv in last) j.Add(kv.Key, kv.Value); return j;
                }
            }
            var missing = Path.Combine(Work, "no-such-console-" + Guid.NewGuid().ToString("N").Substring(0, 6));
            foreach (var (what, extra) in new[] { ("SNES gameplay", "\"capability\":\"snes-gameplay\""), ("converting a SNES Classic's stock saves", "\"capability\":\"snes-stock-save-conversion\""), ("the reserved core provider", "\"provider\":{\"kind\":\"core\",\"id\":\"snes-unselected\"}"),
                                                ("a SNES target", "\"ccs_target\":{\"schema\":\"ccs-target/1\",\"game_system\":\"snes\"}") })
            {
                var r = Ask("{\"cmd\":\"backup\",\"console\":\"simulated\",\"folder\":" + Json.Write(missing).Trim() + "," + extra + "}");
                Check(r["state"] as string == "unavailable" && r["code"] as string == "reserved" && !Directory.Exists(missing), "worker: a backup request for " + what + " is refused before anything (" + r["problem"] + ")");
            }
            var nes = Ask("{\"cmd\":\"backup\",\"console\":\"simulated\",\"folder\":" + Json.Write(missing).Trim() + ",\"game_system\":\"nes\"}");
            Check(nes["code"] as string != "reserved" && (nes["event"] as string == "error" || nes["state"] as string != "unavailable"), "worker: the same request for NES passes the registry and goes on to the console (here: no such simulated console; " + (nes["state"] ?? nes["text"]) + ")");

            // ---- the recovery session's identity: a console answering with any other board is refused before the first read
            foreach (var board in new[] { "dp-shvc", "dp-hvc", "" })
            {
                var other = Console_("modified-noorig"); var cj = Path.Combine(other, "console.json");
                var jd = Json.Parse(File.ReadAllText(cj)); var jo = new JObj(); foreach (var kv in jd) jo.Add(kv.Key, kv.Value); jo.Set("board", board); File.WriteAllText(cj, Json.Write(jo));
                var od = NewDir("platform-board"); var os = SettingsIn(od, false);
                var ob = Backup(other, os);
                Check(ob.State == "refused" && ob.Problem.Contains("not an NES Classic Edition or a North American or European Super NES Classic Edition") && (!Directory.Exists(os.PrimaryFolder) || !Directory.EnumerateFiles(os.PrimaryFolder).Any()), "a console that answers with board \"" + board + "\": refused before anything is read (" + ob.Problem + ")");
            }

            // ---- the console backup boundary: a manifest that claims the SNES Classic is refused; an explicit NES one verifies
            var c = Console_("modified-noorig"); var dir = NewDir("platform-backup"); var s = SettingsIn(dir, false);
            var b = Backup(c, s);
            var vb = Verifier.VerifyFile(b.PrimaryPath);
            var bt = vb.Manifest.O(Capabilities.TargetKey);
            Check(b.Ok && vb.Ok && bt?.S("physical_model") == "nes-classic-edition" && bt?.S("firmware") == "nintendo-nes-classic" && bt?.S("layout") == "nes-classic-nand-512m" && vb.Model == CcsBackup.Device.ConsoleModels.Nes
                  && vb.Manifest.O("console")?.S("model") == "NES Classic Edition" && Path.GetFileName(b.PrimaryPath).StartsWith("SIMULATED NES Classic Backup ", StringComparison.Ordinal),
                  "a new NES Classic backup states its model, console software and storage layout (ccs_target) and verifies");
            Check(vb.Manifest.O("export")?.S("status") == "included", "the console's own HOME package (no ccs_target: NES) is still exported as CCS/Console-HOME.ccs (" + vb.Manifest.O("export")?.S("reason") + ")");
            // the manifest changed and the file written again, as an earlier-format backup: the two repair parts (whose records describe the
            // original file's exact bytes) are left out of the file and of the manifest's member list; an earlier-format backup verifies without them
            bool Repair(string n) => n == BackupFormat.DescriptorName || n == BackupFormat.ChunkMapName;
            string Rezip(string from, Func<Dictionary<string, object>, Dictionary<string, object>> edit, string name)
            {
                var to = Path.Combine(dir, name);
                using (var r = ZipReader.Open(from, BackupFormat.Allowed)) using (var f = new FileStream(to, FileMode.CreateNew))
                {
                    var w = new ZipWriter(f, new DateTime(2026, 11, 15, 8, 0, 0));
                    foreach (var e in r.Entries.OrderBy(x => x.HeaderOffset).Where(x => !Repair(x.Name)))
                    {
                        if (e.Name == BackupFormat.ManifestName)
                        {
                            Dictionary<string, object> m; using (var st = r.OpenMember(e)) using (var sr = new StreamReader(st, Encoding.UTF8)) m = Json.Parse(sr.ReadToEnd());
                            m["members"] = (m.A("members") ?? new List<object>()).OfType<Dictionary<string, object>>().Where(x => !Repair(x.S("path"))).Cast<object>().ToList();
                            var bytes = Json.Bytes(edit(m)); w.Add(e.Name, true, st => st.Write(bytes, 0, bytes.Length));
                        }
                        else w.Add(e.Name, e.Method == 8, st => { using (var ms = r.OpenMember(e)) Streams.Copy(ms, st); });
                    }
                    w.Finish();
                }
                return to;
            }
            Dictionary<string, object> With(Dictionary<string, object> m, object target) { var o = new Dictionary<string, object>(m); o[Capabilities.TargetKey] = target; return o; }
            var snes = Rezip(b.PrimaryPath, m => With(m, new Dictionary<string, object> { { "schema", "ccs-target/1" }, { "physical_model", "snes-classic-edition" } }), "claims-snes.zip");
            var vs = Verifier.VerifyFile(snes);
            Check(!vs.Ok && vs.Problems.Any(p => p.Contains("does not support")), "a backup whose manifest claims the SNES Classic: refused by the verifier (" + string.Join("; ", vs.Problems) + ")");
            var native = Rezip(b.PrimaryPath, m => With(m, new Dictionary<string, object> { { "schema", "ccs-target/1" }, { "physical_model", "nes-classic-edition" }, { "firmware", "nintendo-snes-classic" } }), "claims-snes-software.zip");
            Check(!Verifier.VerifyFile(native).Ok, "a backup that names Nintendo's SNES software on NES hardware: refused (console software is not a core, and SNES software is reserved)");
            var explicitNes = Rezip(b.PrimaryPath, m => With(m, new Dictionary<string, object> { { "schema", "ccs-target/1" }, { "physical_model", "nes-classic-edition" }, { "firmware", "nintendo-nes-classic" }, { "layout", "nes-classic-nand-512m" } }), "explicit-nes.zip");
            var ve = Verifier.VerifyFile(explicitNes);
            Check(ve.Ok, "the same backup with an explicit NES Classic target verifies (" + string.Join("; ", ve.Problems) + ")");
            File.Delete(snes); File.Delete(native); File.Delete(explicitNes);
        }
    }
}
