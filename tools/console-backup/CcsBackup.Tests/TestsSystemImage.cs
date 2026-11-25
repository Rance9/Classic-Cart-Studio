// Classic Cart Studio console backup and restore engine tests. Copyright (C) 2026 Rance9. GPL-3.0-or-later.
// Owner system images for CCS Nucleus (an SNES Classic system on this console): the xz decoder against xz's own output, SquashFS
// images built by squashfs-tools in WSL (gzip and xz), which system an image holds by hakchi's own rules, and an SNES Classic system added
// and removed as a CCS Nucleus choice on a simulated console. Optionally (CCS_PROBE_XZ_SYSTEM and CCS_PROBE_GZIP_SYSTEM: two images of
// the same system, read-only) the same files read from an xz dump and a gzip system part must match byte for byte.
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
        static string RunWsl(string dir, string name, string script)
        {
            var sh = Path.Combine(dir, name + ".sh"); File.WriteAllText(sh, "set -e\ncd '" + W(dir) + "'\n" + script + "\necho made\n");
            var p = Process.Start(new ProcessStartInfo("wsl.exe", "-e bash \"" + W(sh) + "\"") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true, CreateNoWindow = true });
            p.StandardInput.Close(); var o = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd(); p.WaitForExit();
            if (!o.Contains("made")) throw new SkipException("WSL could not run " + name + ": " + o.Trim());
            return o;
        }

        /// <summary>a console system root built by squashfs-tools in WSL: the files given (path: text), a start program link (sbin/init to
        /// ../bin/busybox) unless init is false; big: the stand-in program's bytes (several blocks and a fragment)</summary>
        static byte[] SystemRoot(string dir, string name, string comp, Dictionary<string, string> files, bool init = true, byte[] big = null)
        {
            var tree = Path.Combine(dir, name + "-tree"); var img = Path.Combine(dir, name + ".hsqs");
            Directory.CreateDirectory(Path.Combine(tree, "bin")); Directory.CreateDirectory(Path.Combine(tree, "sbin"));
            File.WriteAllBytes(Path.Combine(tree, "bin", "busybox"), big ?? Encoding.ASCII.GetBytes("stand-in " + name + "\n"));
            foreach (var kv in files) { var f = Path.Combine(tree, kv.Key.Replace('/', Path.DirectorySeparatorChar)); Directory.CreateDirectory(Path.GetDirectoryName(f)); File.WriteAllText(f, kv.Value); }
            RunWsl(dir, name, (init ? "ln -s ../bin/busybox '" + W(tree) + "/sbin/init'\n" : "") + "/usr/bin/mksquashfs '" + W(tree) + "' '" + W(img) + "' -comp " + comp + " -noappend -all-root -no-xattrs -quiet > /dev/null");
            if (!File.Exists(img)) throw new SkipException("squashfs-tools in WSL did not write " + name);
            return File.ReadAllBytes(img);
        }

        static Dictionary<string, string> Clover(string board, string region, string issue = "Welcome\n") => new Dictionary<string, string>
        {
            ["etc/clover/boardtype"] = board + "\n", ["etc/clover/REGION"] = region + "\n", ["etc/issue"] = issue, ["etc/inittab"] = "::sysinit:/etc/init.d/rcS\n",
            ["etc/clover/VERSION"] = "release-v9.9.9-0-gtest" + board.Substring(3) + "\n",
        };

        public static void SystemImages()
        {
            var dir = NewDir("system-image");

            // ---- the xz decoder against xz's own output
            var text = new StringBuilder(); var rnd = new Random(11);
            for (int i = 0; i < 9000; i++) text.Append("line ").Append(i % 97).Append(" of the console's system: ").Append(rnd.Next(1000)).Append('\n');
            var tb = Encoding.ASCII.GetBytes(text.ToString()); File.WriteAllBytes(Path.Combine(dir, "text.bin"), tb);
            var rb = new byte[200000]; rnd.NextBytes(rb); File.WriteAllBytes(Path.Combine(dir, "rand.bin"), rb);
            var mix = new MemoryStream(); for (int k = 0; k < 9; k++) { mix.Write(tb, 0, tb.Length); mix.Write(rb, 0, 40000 + k * 1000); }
            var mb = mix.ToArray(); File.WriteAllBytes(Path.Combine(dir, "mix.bin"), mb);
            var variants = new[]
            {
                ("text", "-0 --check=crc32", tb), ("text", "-6 --check=crc64", tb), ("text", "-9e --check=sha256", tb), ("text", "-6 --check=none", tb),
                ("text", "--lzma2=preset=6,lc=0,lp=2,pb=0", tb), ("text", "--lzma2=preset=6,lc=4,lp=0,pb=4", tb), ("text", "--lzma2=preset=1,dict=4KiB", tb),
                ("rand", "-6", rb), ("mix", "-6", mb), ("mix", "-1 --check=crc32", mb),
            };
            var script = new StringBuilder();
            for (int i = 0; i < variants.Length; i++) script.Append("xz -T1 -c ").Append(variants[i].Item2).Append(' ').Append(variants[i].Item1).Append(".bin > v").Append(i).Append(".xz\n");
            script.Append("xz -T1 -c --armthumb --lzma2=preset=6 text.bin > arm.xz\nxz -T1 -c --delta=dist=4 --lzma2=preset=6 text.bin > delta.xz\n");
            RunWsl(dir, "xz-variants", script.ToString());
            int good = 0; var bad = new List<string>();
            for (int i = 0; i < variants.Length; i++)
            {
                try { var d = Xz.Decode(File.ReadAllBytes(Path.Combine(dir, "v" + i + ".xz")), 16 * 1000 * 1000); if (d.SequenceEqual(variants[i].Item3)) good++; else bad.Add(variants[i].Item2 + ": different bytes"); }
                catch (InvalidDataException ex) { bad.Add(variants[i].Item1 + " " + variants[i].Item2 + ": " + ex.Message); }
            }
            Check(bad.Count == 0, "xz: " + good + " of " + variants.Length + " streams xz wrote decode to exactly their input (presets 0, 1, 6, 9e; CRC32, CRC64, SHA-256 and no check; lc/lp/pb and a 4 KiB dictionary; incompressible data; several LZMA2 chunks) (" + string.Join("; ", bad) + ")");
            string Refused(string file, int max = 16 * 1000 * 1000, Func<byte[], byte[]> change = null)
            {
                var z = File.ReadAllBytes(Path.Combine(dir, file)); if (change != null) z = change(z);
                try { Xz.Decode(z, max); return null; } catch (InvalidDataException ex) { return ex.Message; }
            }
            var arm = Refused("arm.xz"); var delta = Refused("delta.xz");
            Check(arm != null && arm.Contains("ARM-Thumb branch converter") && delta != null && delta.Contains("delta"), "xz: a stream with another filter is refused by name, not guessed at (" + arm + "; " + delta + ")");
            var flip = Refused("v1.xz", change: z => { var c = (byte[])z.Clone(); c[c.Length / 2] ^= 0x10; return c; });
            var cut = Refused("v1.xz", change: z => z.Take(z.Length - 9).ToArray());
            var head = Refused("v1.xz", change: z => { var c = (byte[])z.Clone(); c[7] = 0x02; return c; });
            var limit = Refused("v1.xz", max: tb.Length - 1);
            Check(flip != null && cut != null && head != null && limit != null && limit.Contains("larger than allowed"), "xz: a changed byte, a cut stream, a damaged header and output past the caller's limit are refused, never returned (" + flip + "; " + cut + "; " + head + "; " + limit + ")");

            // ---- SquashFS with xz, as squashfs-tools writes it
            var big = BusyboxStandIn("system-image");
            var many = Clover("dp-shvc", "EUR"); for (int i = 1; i <= 300; i++) many["usr/share/games/g" + i + ".txt"] = "game " + i + "\n";
            var xzImg = SystemRoot(dir, "snes-eur-xz", "xz", many, big: big);
            var fs = new SquashFsReader(new MemoryStream(xzImg));
            Check(fs.CompressionName == "xz" && fs.CanRead && fs.ReadFile("/bin/busybox", 1 << 20).SequenceEqual(big) && fs.ReadFile("/sbin/init", 1 << 20).SequenceEqual(big)
                  && Encoding.ASCII.GetString(fs.ReadFile("/usr/share/games/g300.txt", 100)) == "game 300\n" && Encoding.ASCII.GetString(fs.ReadFile("/etc/clover/REGION", 100)) == "EUR\n",
                  "SquashFS xz: files across blocks and fragments, a start-program link and 300 entries read exactly as squashfs-tools wrote them");

            // ---- which system an image holds, by hakchi's own rules
            SystemImageInfo Id(byte[] b) => SystemImage.Identify(new MemoryStream(b));
            var snes = Id(xzImg);
            Check(snes.Ok && snes.Platform == "snes" && snes.Region == "eur" && snes.Board == "dp-shvc" && snes.Compression == "xz" && snes.Text == "SNES Classic system (Europe)" && snes.DefaultLabel == "My SNES System" && snes.Version == "release-v9.9.9-0-gtestshvc"
                  && snes.Notes.Any(n => n.Contains("X, Y, L and R")) && snes.Notes.Any(n => n.Contains("saves apart")),
                  "an SNES Classic system (board dp-shvc, region EUR, xz): recognized, with its controller and saves notes (" + snes.Why + ")");
            var snesUsa = Id(SystemRoot(dir, "snes-usa-gzip", "gzip", Clover("dp-shvc", "EUR_USA")));
            var nes = Id(SystemRoot(dir, "nes-usa", "gzip", Clover("dp-nes", "USA")));
            var fam = Id(SystemRoot(dir, "hvcj", "xz", Clover("dp-hvc", "JPN", "Nintendo dp-hvcj\n")));
            var noRegion = Id(SystemRoot(dir, "snes-noregion", "gzip", new Dictionary<string, string> { ["etc/clover/boardtype"] = "dp-shvc\n" }));
            Check(snesUsa.Platform == "snes" && snesUsa.Region == "usa" && snesUsa.RegionFile == "EUR_USA" && nes.Platform == "nes" && nes.Text == "NES Classic system (USA)" && nes.Notes.Count == 0 && fam.Platform == "hvcj" && fam.Region == "jpn"
                  && noRegion.Ok && noRegion.Region == "usa",
                  "NES Classic, Famicom Mini (dp-hvcj in /etc/issue, JPN), region EUR_USA and a system with no REGION file (both USA, as hakchi decides): told apart as hakchi does (" + nes.Text + "; " + fam.Text + "; " + noRegion.Text + ")");
            var md = Id(SystemRoot(dir, "md", "gzip", new Dictionary<string, string> { ["usr/game/version"] = "xxxxUS01\n" }));
            var notClover = Id(SystemRoot(dir, "plain", "gzip", new Dictionary<string, string> { ["etc/hostname"] = "box\n" }));
            var noInit = Id(SystemRoot(dir, "noinit", "gzip", Clover("dp-shvc", "USA"), init: false));
            var kernelFile = Id(Kernel());
            var lz4 = Id(SystemRoot(dir, "lz4", "lz4", Clover("dp-shvc", "USA")));
            var junkFile = Id(Cipher(8192));
            var damaged = (byte[])xzImg.Clone(); var sb = new SquashFsReader(new MemoryStream(xzImg)); damaged[(int)sb.InodeTable + 20] ^= 0x40;
            var dmg = Id(damaged);
            Check(!md.Ok && md.Why.Contains("Mega Drive") && !notClover.Ok && notClover.Why.Contains("/etc/clover") && !noInit.Ok && noInit.Why.Contains("/sbin/init") && !kernelFile.Ok && kernelFile.Why.Contains("startup software (kernel) file")
                  && !lz4.Ok && lz4.Why.Contains("lz4") && !junkFile.Ok && junkFile.Why.Contains("SquashFS") && !dmg.Ok && dmg.Why.Contains("could not be read"),
                  "refused with the reason: a Mega Drive system, a file system with no /etc/clover, no start program, a kernel file, a compression this version cannot read, not SquashFS, a damaged xz block ("
                  + string.Join("; ", new[] { md, notClover, noInit, kernelFile, lz4, junkFile, dmg }.Select(x => x.Why)) + ")");

            // ---- an SNES Classic system added and removed as a CCS Nucleus choice (simulated console)
            var s = SettingsIn(NewDir("system-image-console"), false);
            var c = SmallConsole("modified", true); SetInstalled(c, Methods.Nucleus, Methods.Chooser);
            var img = Path.Combine(dir, "My SNES dump.hsqs"); File.WriteAllBytes(img, xzImg);
            var kimg = Path.Combine(dir, "kernel_snes.img"); File.WriteAllBytes(kimg, Kernel());
            var (pk, rk) = FirmwareEngine.Prepare(RecoverySessions.OpenSimulated(c, ImageKind.Firmware), Ctx(s, ImageKind.Firmware), "add", null, null, kimg);
            Check(pk == null && rk.Problem.Contains("startup software (kernel) file") && rk.Problem.Contains(".hsqs") && Writes(c).Count == 0, "a kernel file chosen as a system image: refused before the console is backed up or written (" + rk?.Problem + ")");
            var con = RecoverySessions.OpenSimulated(c, ImageKind.Firmware); var ctx = Ctx(s, ImageKind.Firmware);
            var (p, r) = FirmwareEngine.Prepare(con, ctx, "add", null, null, img);
            var pj = p == null ? null : Json.Parse(Json.Write(p.ToJson()));
            Check(r == null && p.Label == "My SNES System" && p.Text.StartsWith("Add the SNES Classic system (Europe) (") && pj.O("system")?.S("platform") == "snes" && pj.O("system")?.S("region") == "eur"
                  && p.Notes.Any(n => n.Contains("X, Y, L and R")) && p.Notes.Any(n => n.Contains("not changed by adding it")) && p.Notes.Any(n => n.Contains("system version as release-v9.9.9-0-gtestshvc")) && Writes(c).Count == 0,
                  "the plan names the SNES Classic system and its region, chooses the name My SNES System when none is given, and says what is needed and what stays (" + (r?.Problem ?? p.Text) + ")");
            var o = FirmwareEngine.Execute(con, p, p.Digest, p.Nonce, ctx);
            var reg = SimRegistry(c); var opt = reg.ContainsKey(p.Id) ? reg[p.Id] as Dictionary<string, object> : null;
            Check(o.State == "added" && opt?.S("kind") == "owner-root" && opt?.S("label") == "My SNES System" && opt?.S("sha256") == Sha(xzImg) && Sha(Path.Combine(c, "nucleus-roots", p.Id + ".hsqs")) == Sha(xzImg) && reg.ContainsKey("native"),
                  "added: CCS Nucleus lists My SNES System with the image's SHA-256 in its own folder, beside the console's own system (" + o.Problem + ")");
            Gone(p.ProtectivePath);
            var con2 = RecoverySessions.OpenSimulated(c, ImageKind.Firmware);
            var (p2, _) = FirmwareEngine.Prepare(con2, ctx, "remove", p.Id, null, null);
            var o2 = FirmwareEngine.Execute(con2, p2, p2.Digest, p2.Nonce, ctx);
            Check(o2.State == "removed" && !SimRegistry(c).ContainsKey(p.Id) && SimRegistry(c).ContainsKey("native") && !File.Exists(Path.Combine(c, "nucleus-roots", p.Id + ".hsqs")), "removed again: the console's own system stays the start and the way back (" + o2.Problem + ")");
            Gone(p2.ProtectivePath); GoneConsole(c); GoneBackups(s);

            // ---- optional: one system as an xz dump and as a gzip system part, read the same (read-only)
            var px = Environment.GetEnvironmentVariable("CCS_PROBE_XZ_SYSTEM"); var pg = Environment.GetEnvironmentVariable("CCS_PROBE_GZIP_SYSTEM");
            if (px != null && pg != null && File.Exists(px) && File.Exists(pg))
            {
                using (var fx = new FileStream(px, FileMode.Open, FileAccess.Read, FileShare.Read)) using (var fg = new FileStream(pg, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    var ix = SystemImage.Identify(fx); var ig = SystemImage.Identify(fg);
                    var rx = new SquashFsReader(fx); var rg = new SquashFsReader(fg); var same = new List<string>(); var diff = new List<string>();
                    foreach (var path in new[] { "/etc/clover/boardtype", "/etc/clover/REGION", "/etc/issue", "/etc/inittab", "/etc/passwd", "/sbin/init", "/bin/busybox", "/usr/bin/clover-mcp" })
                    {
                        byte[] a, b; try { a = rx.ReadFile(path, 8 * 1000 * 1000); b = rg.ReadFile(path, 8 * 1000 * 1000); } catch (InvalidDataException ex) { diff.Add(path + ": " + ex.Message); continue; }
                        if (a == null && b == null) continue; if (a != null && b != null && a.SequenceEqual(b)) same.Add(path + " " + a.Length); else diff.Add(path);
                    }
                    Check(ix.Ok && ig.Ok && ix.Text == ig.Text && ix.Board == ig.Board && rx.CompressionName == "xz" && rg.CompressionName == "gzip" && diff.Count == 0 && same.Count >= 4,
                          "the owner's xz dump and gzip system part (read-only): the same system (" + ix.Text + ", board " + ix.Board + ") and the same bytes for " + string.Join(", ", same) + (diff.Count > 0 ? "; different: " + string.Join(", ", diff) : ""));
                }
            }
            else Console.WriteLine("NOTE the owner-dump comparison needs CCS_PROBE_XZ_SYSTEM and CCS_PROBE_GZIP_SYSTEM (two images of the same system)");
        }
    }
}
