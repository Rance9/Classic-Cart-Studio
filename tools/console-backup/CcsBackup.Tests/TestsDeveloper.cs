// Classic Cart Studio console backup and restore engine tests: Developer Mode (startup software, system part, test boot in memory, the way
// back through Console Restore) and its stated limits. GPL-3.0-or-later. Simulated consoles and the FEL protocol model only; the real-console
// boundaries (Gate.DeveloperWrites, Gate.DeveloperTestBoot) stay closed. System images are made with squashfs-tools in WSL.
using CcsBackup.Archive;
using CcsBackup.Device;
using CcsBackup.Engine;
using CcsBackup.Library;
using CcsConsole.RamBoot;
using CcsConsole.Tests;
using FelLib;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using static CcsBackup.Tests.Program;

namespace CcsBackup.Tests
{
    static partial class Tests
    {
        // ------------------------------------------------------------------ test images
        /// <summary>a cpio (newc) archive of the given files, gzip-compressed (a ramdisk)</summary>
        static byte[] Ramdisk(params (string name, string text)[] files)
        {
            var o = new MemoryStream(); int ino = 1;
            void Put(string name, byte[] data, int mode)
            {
                var nm = Encoding.ASCII.GetBytes(name + "\0");
                var h = "070701" + string.Concat(new[] { ino++, mode, 0, 0, 1, 0, data.Length, 0, 0, 0, 0, nm.Length, 0 }.Select(x => x.ToString("X8")));
                var hb = Encoding.ASCII.GetBytes(h); o.Write(hb, 0, hb.Length); o.Write(nm, 0, nm.Length); while (o.Length % 4 != 0) o.WriteByte(0);
                o.Write(data, 0, data.Length); while (o.Length % 4 != 0) o.WriteByte(0);
            }
            foreach (var (n, t) in files) Put(n, Encoding.ASCII.GetBytes(t), 0x81A4);
            Put("TRAILER!!!", new byte[0], 0);
            var z = new MemoryStream(); using (var g = new GZipStream(z, CompressionMode.Compress, true)) { var b = o.ToArray(); g.Write(b, 0, b.Length); }
            return z.ToArray();
        }

        /// <summary>an Android boot image v0 in the console's own startup layout (a zImage-shaped kernel, mkbootimg's checksum); tamper changes
        /// it afterwards</summary>
        static byte[] DevImage(string cmdline, int kernelBytes, byte seed, byte[] ramdisk = null, Action<byte[]> tamper = null)
        {
            ramdisk = ramdisk ?? Ramdisk(("init", "#!/bin/sh\nexec /sbin/init\n"));
            var k = new byte[kernelBytes]; for (int i = 0; i < k.Length; i++) k[i] = (byte)(seed + i * 7);
            BitConverter.GetBytes(StartupImage.ZImageMagic).CopyTo(k, 0x24); BitConverter.GetBytes(0u).CopyTo(k, 0x28); BitConverter.GetBytes((uint)kernelBytes).CopyTo(k, 0x2C);
            int P(int n) => (n + 2047) / 2048 * 2048;
            var b = new byte[2048 + P(k.Length) + P(ramdisk.Length)];
            Encoding.ASCII.GetBytes("ANDROID!").CopyTo(b, 0);
            uint[] hdr = { (uint)k.Length, 0x40008000, (uint)ramdisk.Length, 0x41000000, 0, 0x40F00000, 0x40000100, 2048 };
            for (int i = 0; i < hdr.Length; i++) BitConverter.GetBytes(hdr[i]).CopyTo(b, 8 + 4 * i);
            Encoding.ASCII.GetBytes(cmdline).CopyTo(b, 64);
            k.CopyTo(b, 2048); ramdisk.CopyTo(b, 2048 + P(k.Length));
            using (var h = SHA1.Create())
            {
                foreach (var part in new[] { k, ramdisk, new byte[0] }) { h.TransformBlock(part, 0, part.Length, null, 0); h.TransformBlock(BitConverter.GetBytes((uint)part.Length), 0, 4, null, 0); }
                h.TransformFinalBlock(new byte[0], 0, 0); h.Hash.CopyTo(b, 576);
            }
            tamper?.Invoke(b);
            return b;
        }

        static string W(string p) { p = Path.GetFullPath(p); return "/mnt/" + char.ToLowerInvariant(p[0]) + p.Substring(2).Replace('\\', '/'); }

        /// <summary>a SquashFS system image built by squashfs-tools in WSL: a board type (or none), a start program link (or none), a
        /// 300,000-byte program (several blocks and a fragment) and 300 small files (several folder headers)</summary>
        static byte[] Squash(string dir, string name, string comp, string board, bool init = true)
        {
            var tree = Path.Combine(dir, name + "-tree"); var img = Path.Combine(dir, name + ".hsqs");
            Directory.CreateDirectory(Path.Combine(tree, "etc", "clover")); Directory.CreateDirectory(Path.Combine(tree, "bin")); Directory.CreateDirectory(Path.Combine(tree, "sbin")); Directory.CreateDirectory(Path.Combine(tree, "usr", "share", "games"));
            if (board != null) File.WriteAllText(Path.Combine(tree, "etc", "clover", "boardtype"), board + "\n");
            File.WriteAllBytes(Path.Combine(tree, "bin", "busybox"), BusyboxStandIn(name));
            for (int i = 1; i <= 300; i++) File.WriteAllText(Path.Combine(tree, "usr", "share", "games", "g" + i + ".txt"), "game " + i + " " + name + "\n");
            var sh = Path.Combine(dir, name + ".sh");
            File.WriteAllText(sh, "set -e\n" + (init ? "ln -s ../bin/busybox '" + W(tree) + "/sbin/init'\n" : "") + "/usr/bin/mksquashfs '" + W(tree) + "' '" + W(img) + "' -comp " + comp + " -noappend -all-root -no-xattrs -quiet > /dev/null\necho made\n");
            var p = Process.Start(new ProcessStartInfo("wsl.exe", "-e bash \"" + W(sh) + "\"") { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true, CreateNoWindow = true });
            p.StandardInput.Close(); var o = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd(); p.WaitForExit();
            if (!o.Contains("made") || !File.Exists(img)) throw new SkipException("squashfs-tools in WSL could not build a system image: " + o.Trim());
            return File.ReadAllBytes(img);
        }
        static byte[] BusyboxStandIn(string seed) { var b = new byte[300000]; var r = new Random(seed.GetHashCode()); r.NextBytes(b); for (int i = 0; i < b.Length; i += 3) b[i] = (byte)'x'; return b; }

        static Dictionary<string, object> J(JObj o) => Json.Parse(Json.Write(o));
        /// <summary>a plan as the window receives it, kept beside the run for the window's page renderer (tests only)</summary>
        static void Sample(string name, JObj plan) { var d = Path.Combine(Work, "developer-samples"); Directory.CreateDirectory(d); File.WriteAllText(Path.Combine(d, name + ".json"), Json.Write(new JObj { { "event", name }, { "ok", true }, { "plan", plan } })); }
        static Dictionary<string, string> DevHashes(string c) => Directory.GetFiles(Path.Combine(c, "dev")).ToDictionary(Path.GetFileName, f => Sha(f));

        public static void Developer()
        {
            DeveloperStartupImages();
            DeveloperKernel();
            DeveloperSystemAndTestBoot();
            DeveloperFel();
        }

        // ------------------------------------------------------------------ the console's startup-image layout (PC checks only)
        static void DeveloperStartupImages()
        {
            var good = DevImage("root=/dev/nandb decrypt ro console=ttyS0,115200", 300000, 7, Ramdisk(("key-file", "K"), ("init", "#!/bin/sh\n")));
            var f = StartupImage.Inspect(good);
            Check(f.Ok && f.RamdiskFormat == "gzip" && f.KeyInRamdisk == true && f.Bytes == good.Length && f.Padded % 0x20000 == 0, "a startup image in the console's layout passes every check (" + string.Join("; ", f.Problems) + ")");
            void Bad(string what, Action<byte[]> t, string expect)
            {
                var g = StartupImage.Inspect(DevImage("root=/dev/nandb ro", 300000, 9, null, t));
                Check(!g.Ok && g.Problems.Any(x => x.Contains(expect)), "refused: " + what + " (" + string.Join("; ", g.Problems) + ")");
            }
            Bad("another kernel load address", b => BitConverter.GetBytes(0x42008000u).CopyTo(b, 12), "load addresses");
            Bad("another ramdisk address", b => BitConverter.GetBytes(0x42000000u).CopyTo(b, 20), "load addresses");
            Bad("a second-stage loader", b => BitConverter.GetBytes(4096u).CopyTo(b, 24), "second-stage");
            Bad("another page size", b => BitConverter.GetBytes(4096u).CopyTo(b, 36), "page size");
            Bad("a newer header version", b => BitConverter.GetBytes(2u).CopyTo(b, 40), "newer boot image version");
            Bad("a kernel that is not a zImage", b => b[2048 + 0x24] ^= 0xFF, "zImage");
            Bad("a part changed after the build", b => b[2048 + 5000] ^= 1, "own checksum");
            Bad("an extended command line", b => b[700] = (byte)'x', "extended command line");
            Bad("a command line with a control character", b => b[66] = 7, "not plain text");
            Bad("a ramdisk in an unknown format", b => { int r = 2048 + (300000 + 2047) / 2048 * 2048; b[r] = 0x13; b[r + 1] = 0x37; }, "ramdisk is not in a format");
            var trailing = DevImage("root=/dev/nandb ro", 300000, 9).Concat(new byte[] { 1, 2, 3 }).ToArray();
            Check(StartupImage.Inspect(trailing).Problems.Any(x => x.Contains("bytes after its last part")), "refused: bytes after the parts the header describes");
            Check(StartupImage.Inspect(DevImage("root=/dev/nandb ro", 300000, 9).Take(5000).ToArray()).Problems.Any(x => x.Contains("shorter than its header")), "refused: a truncated image");
            Check(StartupImage.Inspect(DevImage("root=/dev/nandb ro", 17000000, 9)).Problems.Any(x => x.Contains("overlap the ramdisk")), "refused: a kernel too large for its place in memory (it would overlap the ramdisk)");
            Check(StartupImage.Inspect(Encoding.ASCII.GetBytes(new string('x', 4096))).Problems.Any(x => x.Contains("not an Android boot image")), "refused: a file that is not a startup image");
            var keyed = StartupImage.Inspect(DevImage("root=/dev/nandb decrypt ro hakchi-key-file=base64:U0VDUkVU", 300000, 3));
            Check(keyed.Ok && !keyed.MaskedCmdline.Contains("U0VDUkVU") && keyed.MaskedCmdline.Contains("not shown") && TestBootEngine.Differences("root=/dev/nandb", keyed.Cmdline).All(d => !d.Contains("U0VDUkVU")),
                "a key carried on the command line is never shown or recorded (" + keyed.MaskedCmdline + ")");
            var nokey = StartupImage.Inspect(DevImage("root=/dev/nandb decrypt ro", 300000, 4));
            Check(nokey.KeyInRamdisk == false && TestBootEngine.KeyDependencies(nokey).Single().Contains("will not open"), "a ramdisk without the console's key, asking to open the system part: its dependency is stated (" + TestBootEngine.KeyDependencies(nokey).Single() + ")");
            var hk = StartupImage.Inspect(DevImage("root=/dev/nandb decrypt ro", 300000, 5, Ramdisk(("init", "kfb64=1\nsunxi-flash ramdisk 68 | cpio -iu key-file\n"))));
            Check(hk.HakchiKeyFallback && TestBootEngine.KeyDependencies(hk).Single().Contains("reads from the console's own startup software"), "an /init that reads the key from the console's own startup software (as hakchi's does) is recognized");
            // the console's own images: the owner's genuine stock kernel (when given) and hakchi 3.9.3's recovery kernel (inside this build's image)
            if (KernelPath != null && File.Exists(KernelPath)) { var st = StartupImage.Inspect(File.ReadAllBytes(KernelPath)); Check(st.Ok && st.RamdiskFormat == "lzo" && st.KeyInRamdisk == true && TestBootEngine.KeyDependencies(st).Single().Contains("key file in its own ramdisk"), "the genuine stock kernel fits the layout these checks enforce, and its LZO ramdisk is read: it holds the key file (" + string.Join("; ", st.Problems) + ")"); }
            var hakchi = RecoveryImages.Load(ImageKind.Files);
            var hb = new byte[hakchi.ImageBytes]; Array.Copy(hakchi.Image, hb, hb.Length); var hf = StartupImage.Inspect(hb);
            Check(hf.Ok && hf.RamdiskFormat == "xz", "hakchi 3.9.3's kernel (in Classic Cart Studio's own console program) fits the same layout (" + string.Join("; ", hf.Problems) + ")");
            var (ok, _) = DeveloperEngine.ClassifyRamImage(RecoveryImages.IdOf(ImageKind.Backup).id); var (no, nw) = DeveloperEngine.ClassifyRamImage("someone-elses-kernel");
            Check(ok && !no && nw.Contains("controls the console once it starts"), "\"nothing installed on your console\": only Classic Cart Studio's own read-only session qualifies; any other image is described as controlling the console");
        }

        // ------------------------------------------------------------------ the startup software (slot 30)
        static void DeveloperKernel()
        {
            var s = SettingsIn(NewDir("developer"), false);
            var c = Console_("modified"); var a = Backup(c, s);
            var con = RecoverySessions.OpenSimulated(c, ImageKind.Files); var ctx = Ctx(s, ImageKind.Files);
            var (rp, _) = RestorePoints.Establish(con, a.PrimaryPath, null);
            Check(rp != null && rp.Covers.Contains(RestorePoints.Kernel) && rp.Covers.Contains(RestorePoints.BootStages) && rp.LayoutChecked, "the restore point holds this console's startup software and startup stages (a kernel change has a way back)");
            var (bad, bw) = DeveloperEngine.PlanKernel(con, rp, Encoding.ASCII.GetBytes("not a kernel"), null);
            Check(bad == null && bw.Contains("not an Android boot image"), "a file that is not a startup image is refused (" + bw + ")");
            var (lay, lw) = DeveloperEngine.PlanKernel(con, rp, DevImage("root=/dev/nandb ro", 300000, 7, null, b => BitConverter.GetBytes(0x42008000u).CopyTo(b, 12)), null);
            Check(lay == null && lw.Contains("load addresses"), "a startup image in another layout is refused before anything is sent (" + lw + ")");
            var (nk, nkw) = DeveloperEngine.PlanKernel(con, rp, DevImage("root=/dev/nandb decrypt ro", 300000, 7), null);
            Check(nk == null && nkw.Contains("will not open"), "a startup image that asks to open the system part without the console's key is refused (" + nkw + ")");
            var (pd, pdw) = DeveloperEngine.PlanKernel(con, rp, DevImage("root=/dev/nandb ro", 300000, 7).Concat(new byte[2048]).ToArray(), null);
            Check(pd == null && pdw.Contains("padding"), "a startup image with padding after its parts is refused: the slot reads back only what the header describes (" + pdw + ")");
            var img = DevImage("root=/dev/nandb decrypt ro console=ttyS0,115200 dev-test=1", 300000, 7, Ramdisk(("key-file", "K"), ("init", "#!/bin/sh\n")));
            var (p, why) = DeveloperEngine.PlanKernel(con, rp, img, null);
            Check(p != null && p.CmdlineDifferences.Any(d => d == "added: dev-test=1") && p.Notes.Any(n => n.Contains("Console Restore with your restore point")) && p.Notes.Any(n => n.Contains(SessionRestorePoint.NoticeText)) && p.Dependencies.Any(d => d.Contains("key file in its own ramdisk")),
                "the plan shows each command-line difference, its dependencies, the guided way back and the restore point's notice (" + why + ")");
            Sample("kernel-plan", p.ToJson());
            var other = RecoverySessions.OpenSimulated(c, ImageKind.Files);
            Check(DeveloperEngine.FlashKernel(other, p, p.Digest, p.Nonce, ctx).State == "refused" && !File.ReadAllBytes(SimulatedConsole.Dev(c, "boot2")).SequenceEqual(img), "another connection cannot use the plan");
            var o = DeveloperEngine.FlashKernel(con, p, p.Digest, p.Nonce, ctx);
            Check(o.Ok && File.ReadAllBytes(SimulatedConsole.Dev(c, "boot2")).SequenceEqual(img) && File.ReadAllLines(Path.Combine(c, "writes.log")).Count(l => l.EndsWith("dev kernel")) == 1, "the startup software written once and read back (" + o.Problem + ")");
            File.WriteAllText(Path.Combine(c, "faults.json"), "{\"dev_readback_differs\":true}");
            var con2 = RecoverySessions.OpenSimulated(c, ImageKind.Files); var (rp2, _) = RestorePoints.Establish(con2, a.PrimaryPath, null);
            var (p2, _) = DeveloperEngine.PlanKernel(con2, rp2, DevImage("root=/dev/nandb ro", 200000, 9), null);
            var o2 = DeveloperEngine.FlashKernel(con2, p2, p2.Digest, p2.Nonce, ctx);
            Check(o2.State == "stopped" && o2.Problem.Contains("Console Restore with your restore point") && File.ReadAllLines(Path.Combine(c, "writes.log")).Count(l => l.EndsWith("dev kernel")) == 2, "a write that reads back differently stops, is not written again, and points to the way back (" + o2.Problem + ")");
            File.Delete(Path.Combine(c, "faults.json"));
            try { RecoverySessions.OpenReal(ImageKind.Files, null, () => false); Check(false, "a real file session opened"); }
            catch (GateClosedException) { Check(!CcsBackup.Gate.DeveloperWrites, "a real console: Developer Mode's boundary is closed in code"); }
        }

        // ------------------------------------------------------------------ the system part, the test boot and the way back
        static void DeveloperSystemAndTestBoot()
        {
            var dir = NewDir("developer-system"); var s = SettingsIn(dir, false);
            var A = Squash(dir, "system-a", "gzip", "dp-nes"); var B = Squash(dir, "system-b", "gzip", "dp-nes");
            // the SquashFS reader against squashfs-tools' own output
            var fs = new SquashFsReader(new MemoryStream(B));
            Check(fs.Compression == SquashFsReader.Gzip && fs.ReadFile("/bin/busybox", 1 << 20).SequenceEqual(BusyboxStandIn("system-b")) && fs.ReadFile("/sbin/init", 1 << 20).SequenceEqual(BusyboxStandIn("system-b"))
                  && Encoding.ASCII.GetString(fs.ReadFile("/usr/share/games/g300.txt", 100)) == "game 300 system-b\n" && fs.Find("/etc/missing") == null && fs.LinkTarget(fs.Find("/sbin/init")) == "../bin/busybox",
                  "the SquashFS reader reads files across blocks and fragments, follows a start-program link and lists 300 entries, as squashfs-tools wrote them");
            // a stock console whose system part holds image A (through the simulated console's stand-in encryption); its backup is the restore point
            var c = Console_("stock");
            var sysDev = SimulatedConsole.Dev(c, "nandb"); var rawBefore = File.ReadAllBytes(sysDev);
            using (var f = new FileStream(sysDev, FileMode.Open, FileAccess.Write)) { var e = SimulatedConsole.SimulatedSystemCipher(A); f.Write(e, 0, e.Length); }
            var rawA = File.ReadAllBytes(sysDev);
            var a = Backup(c, s); Check(a.Ok, "the restore point backup is made (" + a.State + ")");
            var con = RecoverySessions.OpenSimulated(c, ImageKind.Files); var ctx = Ctx(s, ImageKind.Files);
            var (rp, rpw) = RestorePoints.Establish(con, a.PrimaryPath, null);
            Check(rp != null && rp.Covers.Contains(RestorePoints.SystemPart) && rp.Covers.Contains(RestorePoints.Layout), "the restore point holds the system part (" + rpw + ")");
            var info = con.DevSystemInfo();
            Check(info["system"] == "crypt" && info["key"] == "yes" && info["comp"] == "1" && info["bytes_used"] == new SquashFsReader(new MemoryStream(A)).BytesUsed.ToString() && info["board"] == "dp-nes",
                "the console reports its system part as its startup opens it: encrypted, its key available, gzip, the current size, its board (reads only)");
            var X = Squash(dir, "system-xz", "xz", "dp-nes"); var H = Squash(dir, "system-hvc", "gzip", "dp-hvc"); var N = Squash(dir, "system-noinit", "gzip", "dp-nes", init: false); var U = Squash(dir, "system-noboard", "gzip", null);
            foreach (var (img, what, expect) in new[] { (Encoding.ASCII.GetBytes(new string('z', 8192)), "not a SquashFS file system", "not a SquashFS"), (X, "an xz system", "compression (xz)"), (H, "another console's board type", "for another console"),
                (N, "no start program", "no start program"), (U, "no board type", "does not record a board type"), (B.Concat(new byte[100]).ToArray(), "a size that is not whole 4,096-byte blocks", "4,096-byte blocks") })
            {
                var (bp, bw) = DeveloperSystem.Plan(con, rp, img, "x.hsqs", null);
                Check(bp == null && bw.Contains(expect), "a system image is refused before anything is sent: " + what + " (" + bw + ")");
            }
            Check(!File.Exists(Path.Combine(c, "writes.log")) && File.ReadAllBytes(sysDev).SequenceEqual(rawA), "nothing was written by the refused plans");
            var cfg = Path.Combine(c, "console.json"); var cfgText = File.ReadAllText(cfg);
            File.WriteAllText(cfg, cfgText.TrimEnd().TrimEnd('}') + ",\"system_key\":false}");
            var (kp, kw) = DeveloperSystem.Plan(con, rp, B, "b.hsqs", null); Check(kp == null && kw.Contains("key"), "without the console's key in the session the system part is not changed (" + kw + ")");
            File.WriteAllText(cfg, cfgText.TrimEnd().TrimEnd('}') + ",\"mem_free\":20000000}");
            var (mp, mw) = DeveloperSystem.Plan(con, rp, B, "b.hsqs", null); Check(mp == null && mw.Contains("free memory"), "an image that does not fit in the console's free memory is not staged (" + mw + ")");
            File.WriteAllText(cfg, cfgText);
            var (sp, sw) = DeveloperSystem.Plan(con, rp, B, "b.hsqs", null);
            Check(sp != null && sp.Encryption == "crypt" && sp.Board == "dp-nes" && sp.Compression == "gzip" && sp.Notes.Any(n => n.Contains("never leaves it")) && sp.Notes.Any(n => n.Contains("Console Restore with your restore point")) && J(sp.ToJson()).S("code_notice") == DeveloperSystem.CodeNotice,
                "the system plan: the image's and the console's facts, the key never leaving the console, the way back and the code notice (" + sw + ")");
            Sample("system-plan", sp.ToJson());
            Check(DeveloperSystem.Flash(RecoverySessions.OpenSimulated(c, ImageKind.Files), sp, sp.Digest, sp.Nonce, ctx).State == "refused" && File.ReadAllBytes(sysDev).SequenceEqual(rawA), "another connection cannot use the system plan; nothing written");
            Check(DeveloperSystem.Flash(con, sp, Other(sp.Digest), sp.Nonce, ctx).State == "refused" && sp.Nonce != null, "a confirmation for another plan is refused; the confirmation is not used");
            var so = DeveloperSystem.Flash(con, sp, sp.Digest, sp.Nonce, ctx);
            var raw = File.ReadAllBytes(sysDev); var plainNow = SimulatedConsole.SimulatedSystemCipher(raw.Take(B.Length).ToArray());
            Check(so.Ok && plainNow.SequenceEqual(B) && raw.Skip(B.Length).SequenceEqual(rawA.Skip(B.Length)) && File.ReadAllLines(Path.Combine(c, "writes.log")).Count(l => l.EndsWith("dev system")) == 1 && File.Exists(so.RecordFile),
                "the system part written once through the console's encryption and read back; the rest of the part unchanged; recorded (" + so.Problem + ")");
            Check(DeveloperSystem.Flash(con, sp, sp.Digest, sp.Nonce, ctx).State == "refused", "the same confirmation cannot write twice");
            File.WriteAllText(Path.Combine(c, "faults.json"), "{\"dev_system_readback_differs\":true}");
            var (sp2, _) = DeveloperSystem.Plan(con, rp, A, "a.hsqs", null); var so2 = DeveloperSystem.Flash(con, sp2, sp2.Digest, sp2.Nonce, ctx);
            Check(so2.State == "stopped" && so2.Problem.Contains("Nothing was written again") && so2.Problem.Contains("Console Restore with your restore point") && File.ReadAllLines(Path.Combine(c, "writes.log")).Count(l => l.EndsWith("dev system")) == 2,
                "a system write that reads back differently stops, is not written again, and points to the way back (" + so2.Problem + ")");
            File.Delete(Path.Combine(c, "faults.json"));

            // the startup software is required evidence for a system plan (review R2): a failed read, a short image, a wrong root and a
            // startup changed after the review all refuse before anything is staged, and a refusal never uses the confirmation
            var boot2 = SimulatedConsole.Dev(c, "boot2"); var boot2Keep = File.ReadAllBytes(boot2); var faults = Path.Combine(c, "faults.json");
            int Writes2() => File.Exists(Path.Combine(c, "writes.log")) ? File.ReadAllLines(Path.Combine(c, "writes.log")).Length : 0;
            var sysNow = File.ReadAllBytes(sysDev); int w0 = Writes2();
            File.WriteAllText(faults, "{\"read\":{\"target\":\"boot2\",\"mode\":\"status\",\"after\":0}}");
            var (rf, rfw) = DeveloperSystem.Plan(con, rp, A, "a.hsqs", null);
            Check(rf == null && rfw.Contains("could not be read in full") && rfw.Contains("could not be checked"), "the startup software cannot be read: no system plan, the read failure is not written past (" + rfw + ")");
            var (kf, kfw) = DeveloperEngine.PlanKernel(con, rp, DevImage("root=/dev/nandb ro", 200000, 11), null);
            Check(kf == null && kfw.Contains("could not be read in full"), "the same failed read refuses a startup software plan too (" + kfw + ")");
            File.Delete(faults);
            File.WriteAllBytes(boot2, boot2Keep.Take(6000).ToArray());
            var (hf, hfw) = DeveloperSystem.Plan(con, rp, A, "a.hsqs", null);
            Check(hf == null && hfw.Contains("without a valid startup header") && hfw.Contains("shorter than its header"), "a truncated startup image (its header promises more than is there): no system plan (" + hfw + ")");
            File.WriteAllBytes(boot2, DevImage("console=ttyS0,115200 ro", 300000, 12));
            var (wf, wfw) = DeveloperSystem.Plan(con, rp, A, "a.hsqs", null);
            Check(wf == null && wfw.Contains("root=/dev/nandb"), "a startup software that does not start the console from the system part: no system plan (" + wfw + ")");
            File.WriteAllBytes(boot2, boot2Keep);
            var (sp3, sp3w) = DeveloperSystem.Plan(con, rp, B, "b.hsqs", null);   // B: the console then differs from the restore point (A), as the way back below needs
            Check(sp3 != null && sp3.StartupMd5 != null && sp3.StartupCmdline.Contains("root=/dev/nandb"), "with the startup software read and checked, the plan binds it (" + sp3w + ")");
            File.WriteAllBytes(boot2, DevImage("root=/dev/nandb ro console=ttyS0,115200 changed=1", 300000, 13));
            var cn = sp3.Nonce; var co = DeveloperSystem.Flash(con, sp3, sp3.Digest, sp3.Nonce, ctx);
            Check(co.State == "refused" && co.Problem.Contains("changed since the review") && sp3.Nonce == cn && Writes2() == w0 && File.ReadAllBytes(sysDev).SequenceEqual(sysNow) && co.RecordFile == null,
                "the startup software changed after the review: refused at the confirmation, nothing staged or written, no record, the confirmation unused (" + co.Problem + ")");
            File.WriteAllBytes(boot2, boot2Keep); File.WriteAllText(faults, "{\"read\":{\"target\":\"boot2\",\"mode\":\"short\",\"after\":4096}}");
            var cr = DeveloperSystem.Flash(con, sp3, sp3.Digest, sp3.Nonce, ctx);
            Check(cr.State == "refused" && cr.Problem.Contains("could not be read in full") && sp3.Nonce == cn && Writes2() == w0 && File.ReadAllBytes(sysDev).SequenceEqual(sysNow),
                "the startup software cannot be read again at the confirmation: refused, nothing written, the confirmation unused (" + cr.Problem + ")");
            File.Delete(faults);
            var cv = DeveloperSystem.Flash(con, sp3, sp3.Digest, sp3.Nonce, ctx);
            Check(cv.Ok && SimulatedConsole.SimulatedSystemCipher(File.ReadAllBytes(sysDev).Take(B.Length).ToArray()).SequenceEqual(B), "the same plan, its startup software unchanged and readable again: written once and read back (" + cv.Problem + ")");

            // the test boot: the console in its USB mode, known only by its hardware id
            var devBefore = DevHashes(c); var writesBefore = File.ReadAllText(Path.Combine(c, "writes.log"));
            using (var t = DeveloperTestBoot.OpenSimulated(c))
            {
                var (tp, tpw) = TestBootEngine.Point(t, a.PrimaryPath, null);
                Check(tp != null && !tp.LayoutChecked && !tp.Covers.Contains(RestorePoints.Layout) && TestBootEngine.Needs.All(tp.Covers.Contains), "a test boot's restore point: this console's backup by its hardware id, holding everything a way back needs; the layout not claimed (" + tpw + ")");
                Check(RestorePoints.Require(tp, con, RestorePoints.UserFiles, null) != null, "a restore point chosen without the storage table is never accepted by a file session");
                var (own, ow) = TestBootEngine.Plan(t, tp, TestBootEngine.OwnStartup, null, null, null);
                Check(own != null && own.Notice == TestBootEngine.OwnNotice && J(own.ToJson()).TryGetValue("nothing_written_claim", out var nw) && nw is bool nwb && !nwb && own.Notes.Any(n => n.Contains("writes nothing to the console's storage")) && own.Image.Length % 0x20000 == 0,
                    "own startup software: planned from the restore point's recorded bytes, with its own notice (" + ow + ")");
                var dimg = DevImage("root=/dev/nandb decrypt ro console=ttyS0,115200 hakchi-key-file=base64:U0VDUkVU dev-test=1", 300000, 11);
                var (ip, iw) = TestBootEngine.Plan(t, tp, TestBootEngine.ImageFile, dimg, "dev.img", null);
                Check(ip != null && ip.Notice == TestBootEngine.ImageNotice && ip.Differences.Contains("added: dev-test=1") && !Json.Write(ip.ToJson()).Contains("U0VDUkVU") && ip.Dependencies.Any(d => d.Contains("command line carries")),
                    "a startup image file: its differences, its dependencies and the plain notice that its code controls the console; the key it carries never shown (" + iw + ")");
                Sample("test-boot-plan-own", own.ToJson()); Sample("test-boot-plan-image", ip.ToJson());
                var (lp, lw) = TestBootEngine.Plan(t, tp, TestBootEngine.ImageFile, DevImage("root=/dev/nandb ro", 300000, 7, null, b => BitConverter.GetBytes(4096u).CopyTo(b, 36)), "x.img", null);
                Check(lp == null && lw.Contains("page size"), "an image in another layout is refused (" + lw + ")");
                var cut = new SessionRestorePoint { Hwid = tp.Hwid, ConsoleTag = tp.ConsoleTag, Session = tp.Session, MadeUtc = tp.MadeUtc, Archive = tp.Archive, Simulated = true, LayoutChecked = false }; foreach (var x in tp.Covers.Where(x => x != RestorePoints.BootStages)) cut.Covers.Add(x);
                var (cp, cw) = TestBootEngine.Plan(t, cut, TestBootEngine.ImageFile, dimg, "dev.img", null);
                Check(cp == null && cw.Contains("startup stages"), "a restore point without the startup stages cannot protect code that could change them (" + cw + ")");
                using (var t2 = DeveloperTestBoot.OpenSimulated(c))
                    Check(TestBootEngine.Execute(t2, ip, ip.Digest, ip.Nonce, Ctx(s)).State == "refused" && !File.Exists(Path.Combine(c, "ram-test-boot.bin")), "another connection cannot use the plan; nothing sent");
                Check(TestBootEngine.Execute(t, ip, Other(ip.Digest), ip.Nonce, Ctx(s)).State == "refused" && ip.Nonce != null, "a confirmation for another plan is refused, unused");
                File.WriteAllText(cfg, cfgText.Replace(HwidA, HwidB));
                var sw2 = TestBootEngine.Execute(t, ip, ip.Digest, ip.Nonce, Ctx(s));
                Check(sw2.State == "refused" && sw2.Problem.Contains("not the one") && !File.Exists(Path.Combine(c, "ram-test-boot.bin")) && ip.Nonce != null, "another console in USB mode at the confirmation (its hardware id): refused, nothing sent (" + sw2.Problem + ")");
                File.WriteAllText(cfg, cfgText);
                File.WriteAllText(Path.Combine(c, "faults.json"), "{\"test_boot_fail\":true}");
                var (fp, _) = TestBootEngine.Plan(t, tp, TestBootEngine.ImageFile, dimg, "dev.img", null); var fo = TestBootEngine.Execute(t, fp, fp.Digest, fp.Nonce, Ctx(s));
                Check(fo.State == "stopped" && fo.Problem.Contains("wrote nothing") && Record.Load(fo.RecordFile).S("state") == "stopped", "a transfer that stops: reported and recorded, nothing more sent (" + fo.Problem + ")");
                File.Delete(Path.Combine(c, "faults.json"));
                var go = TestBootEngine.Execute(t, ip, ip.Digest, ip.Nonce, Ctx(s));
                var ram = Path.Combine(c, "ram-test-boot.bin");
                Check(go.Ok && File.Exists(ram) && File.ReadAllBytes(ram).SequenceEqual(ip.Image) && Record.Load(go.RecordFile).S("state") == "sent" && File.ReadAllText(Path.Combine(c, "test-boot.log")).Contains(Sha(ip.Image)),
                    "sent: the reviewed bytes in the console's memory, recorded first (" + go.Problem + ")");
                Check(DevHashes(c).OrderBy(x => x.Key).SequenceEqual(devBefore.OrderBy(x => x.Key)) && File.ReadAllText(Path.Combine(c, "writes.log")) == writesBefore, "the test boot wrote nothing to the console's storage (every storage file unchanged)");
                Check(TestBootEngine.Execute(t, ip, ip.Digest, ip.Nonce, Ctx(s)).State == "refused", "the same confirmation cannot start it twice");
            }
            var other = Console_("stock", HwidB); var ob = Backup(other, s);
            using (var t = DeveloperTestBoot.OpenSimulated(c)) { var (xp, xw) = TestBootEngine.Point(t, ob.PrimaryPath, null); Check(xp == null && xw.Contains("different console"), "another console's backup is never a test boot's restore point (" + xw + ")"); }
            var mc = Console_("modified"); var mb = Backup(mc, s);
            using (var t = DeveloperTestBoot.OpenSimulated(mc)) { var (mp2, _) = TestBootEngine.Point(t, mb.PrimaryPath, null); var (op, opw) = TestBootEngine.Plan(t, mp2, TestBootEngine.OwnStartup, null, null, null); Check(op == null && opw.Contains("cannot be started from memory"), "a restore point whose startup software does not fit the console's layout is not test-started (" + opw + ")"); }
            try { DeveloperTestBoot.OpenReal(); Check(false, "a real test boot opened"); }
            catch (GateClosedException ex) { Check(!CcsBackup.Gate.DeveloperTestBoot && ex.Message.Contains("not available"), "a real console: the test boot's own boundary is closed in code (" + ex.Message + ")"); }

            // the way back: Console Restore with the restore point, under its own strict rules (its own protective backup, plan, confirmation)
            var rcon = RecoverySessions.OpenSimulated(c, ImageKind.Restore); var rctx = Ctx(s, ImageKind.Restore);
            var (plan, refused) = RestoreEngine.Prepare(rcon, a.PrimaryPath, rctx);
            Check(refused == null && plan != null && plan.Writable && plan.ProtectivePath != null && plan.Steps.Single(x => x.Target == "nandb").Action == "write", "the way back is Console Restore's own plan: a protective backup of the console now first, the changed system part written back (" + refused?.Problem + ")");
            var ro = RestoreEngine.Execute(rcon, plan, plan.Digest, plan.Nonce, rctx);
            Check(ro.State == "restored" && File.ReadAllBytes(sysDev).SequenceEqual(rawA), "restored: the system part is again exactly the restore point's (" + ro.Problem + ")");
            GC.KeepAlive(rawBefore);
            DeveloperWorker(c, a.PrimaryPath, s);
        }

        /// <summary>a stream one side writes and the other reads, line by line (the worker's input and output in the same process)</summary>
        sealed class QueueStream : Stream
        {
            readonly System.Collections.Concurrent.BlockingCollection<byte[]> q = new System.Collections.Concurrent.BlockingCollection<byte[]>(); byte[] cur; int pos; bool ended;
            public override int Read(byte[] b, int o, int c)
            {
                while (!ended && (cur == null || pos >= cur.Length)) { if (!q.TryTake(out cur, 180000) || cur.Length == 0) { ended = true; break; } pos = 0; }
                if (ended) return 0;
                int n = Math.Min(c, cur.Length - pos); Array.Copy(cur, pos, b, o, n); pos += n; return n;
            }
            public override void Write(byte[] b, int o, int c) { var x = new byte[c]; Array.Copy(b, o, x, 0, c); if (c > 0) q.Add(x); }
            public void End() => q.Add(new byte[0]);
            public override bool CanRead => true; public override bool CanSeek => false; public override bool CanWrite => true;
            public override long Length => throw new NotSupportedException(); public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
            public override void Flush() { } public override long Seek(long o, SeekOrigin k) => throw new NotSupportedException(); public override void SetLength(long v) => throw new NotSupportedException();
        }

        /// <summary>the worker's own test-boot request path, as both Workbench hosts use it: one request, the plan event, the confirmation
        /// with the plan's digest and one-time code, the result (another program holding the console lock is waited for: "busy")</summary>
        static (Dictionary<string, object> plan, Dictionary<string, object> result, int rc) Worker(string request, Settings s, bool confirm)
        {
            for (int attempt = 0; ; attempt++)
            {
                var input = new QueueStream(); var output = new QueueStream(); int rc = -1; var enc = new UTF8Encoding(false);
                var th = new System.Threading.Thread(() => { try { rc = WorkerHost.RunOn(input, output, "tests", "CcsBackup.Tests", "test", s.Path); } finally { output.End(); } }) { IsBackground = true }; th.Start();
                void Line(string l) { var b = enc.GetBytes(l + "\n"); input.Write(b, 0, b.Length); }
                Line(request);
                Dictionary<string, object> plan = null, result = null; string line; var rd = new StreamReader(output, enc);
                while ((line = rd.ReadLine()) != null)
                {
                    var ev = Json.Parse(line);
                    if (ev.S("event") == "test-boot-plan") { plan = ev.O("plan"); Line(confirm ? Json.Write(new JObj { { "cmd", "test-boot-confirm" }, { "digest", plan.S("digest") }, { "nonce", plan.S("nonce") } }).Replace("\n", " ") : "{\"cmd\":\"quit\"}"); }
                    if (ev.S("event") == "result") { result = ev; break; }
                }
                input.End(); th.Join(120000);
                if (result?.S("state") != "busy" || attempt >= 90) return (plan, result, rc);
                System.Threading.Thread.Sleep(2000);
            }
        }

        static void DeveloperWorker(string c, string backup, Settings s)
        {
            string Req(string cls, string image) => Json.Write(new JObj { { "cmd", "test-boot" }, { "console", "simulated" }, { "folder", c }, { "class", cls }, { "backup", backup }, { "image", image } }).Replace("\n", " ");
            var ram = Path.Combine(c, "ram-test-boot.bin"); if (File.Exists(ram)) File.Delete(ram);
            var (plan, result, rc) = Worker(Req(TestBootEngine.OwnStartup, null), s, true);
            Check(plan != null && plan.S("class") == TestBootEngine.OwnStartup && plan.TryGetValue("nothing_written_claim", out var nw) && nw is bool nwb && !nwb && result?.S("state") == "sent" && rc == 0 && File.Exists(ram),
                "worker: a test-boot request plans, waits for the confirmation with the plan's own code, and sends (" + result?.S("state") + " " + result?.S("problem") + ")");
            File.Delete(ram);
            var (p2, r2, _) = Worker(Req(TestBootEngine.OwnStartup, null), s, false);
            Check(p2 != null && r2?.S("state") == "canceled" && !File.Exists(ram), "worker: without the confirmation nothing is sent (" + r2?.S("problem") + ")");
            var notImage = Path.Combine(Work, "not-a-startup-image-" + Guid.NewGuid().ToString("N").Substring(0, 6) + ".img"); File.WriteAllText(notImage, "not a startup image");
            var (p3, r3, _) = Worker(Req(TestBootEngine.ImageFile, notImage), s, true);
            Check(p3 == null && r3?.S("state") == "refused" && r3.S("problem").Contains("not an Android boot image") && !File.Exists(ram), "worker: a file that is not a startup image is refused before any plan (" + r3?.S("problem") + ")");
            var (p4, r4, _) = Worker(Json.Write(new JObj { { "cmd", "test-boot" }, { "console", "real" }, { "class", TestBootEngine.OwnStartup }, { "backup", backup } }).Replace("\n", " "), s, true);
            Check(p4 == null && r4?.S("state") == "unavailable", "worker: a real console's test boot is unavailable, closed in code (" + r4?.S("problem") + ")");
        }

        // ------------------------------------------------------------------ the real adapter over the FEL protocol model
        static void DeveloperFel()
        {
            var sid = new byte[] { 0x61, 0xaa, 0x61, 0x04, 0x00, 0x00, 0x18, 0x88, 0x00, 0x00, 0x67, 0x1e, 0x00, 0x80, 0x11, 0x6c };
            var boot = RecoveryImages.Load(ImageKind.Files);
            FakeDeviceList One() => new FakeDeviceList { Items = { FakeDeviceList.Dev(0x1F3A, 0xEFE8, "NES Classic") } };
            var dev = new SimulatedFelDevice(); dev.Poke(Fel.sid_base, sid);
            var s = TestBootSession.Open(One(), () => dev, boot.Fes1, boot.UbootSd, out var why);
            Check(s != null && s.Sid == "0461aa61 88180000 1e670000 6c118000" && dev.Downloads.Count == 0 && dev.Executed.Count == 0 && dev.Violations.Count == 0,
                "opening a test-boot connection reads only the board id and the SID (as the console prints its hardware id): nothing written or run (" + why + ")");
            var f = StartupImage.Inspect(DevImage("root=/dev/nandb ro", 300000, 7)); var img = DevImage("root=/dev/nandb ro", 300000, 7); var padded = new byte[f.Padded]; Array.Copy(img, padded, img.Length);
            s.DramInitSleepMs = 0;
            var r = s.Boot(padded, null);
            Check(r.Error == null && r.BootCommandSent && dev.Executed.SequenceEqual(new uint[] { 0x2000, 0x47000000 }) && dev.Read(0x47400000, padded.Length).SequenceEqual(padded) && dev.BootCommand == "boota 47400000" && dev.Violations.Count == 0,
                "the image goes to 0x47400000 after fes1 and starts with hakchi's memboot command, nothing else (" + r.Error + ")");
            Check(s.Boot(padded, null).Error != null, "a second start on the same connection is refused");
            s.Dispose();
            var d2 = new SimulatedFelDevice { Board = 0x00123400 };
            Check(TestBootSession.Open(One(), () => d2, boot.Fes1, boot.UbootSd, out var w2) == null && w2.Contains("board") && d2.Downloads.Count == 0, "another board: refused when opening, nothing written (" + w2 + ")");
            Check(TestBootSession.Open(new FakeDeviceList(), () => new SimulatedFelDevice(), boot.Fes1, boot.UbootSd, out var w3) == null && w3.Contains("no console"), "no console in FEL mode: refused");
            var d4 = new SimulatedFelDevice(); d4.Poke(Fel.sid_base, sid); var s4 = TestBootSession.Open(One(), () => d4, boot.Fes1, boot.UbootSd, out _);
            using (var t = new DeveloperTestBoot.FelTarget(s4))
            {
                var dir = NewDir("developer-fel"); var st = SettingsIn(dir, false); var c = Console_("stock", "0461aa61 88180000 1e670000 6c118000"); var b = Backup(c, st);
                var (p, pw) = TestBootEngine.Point(t, b.PrimaryPath, null);
                Check(p == null && pw.Contains("never mixed") && d4.Downloads.Count == 0, "the real adapter reads the console's id and never takes a simulated console's backup as its restore point (" + pw + ")");
            }
        }
    }
}
