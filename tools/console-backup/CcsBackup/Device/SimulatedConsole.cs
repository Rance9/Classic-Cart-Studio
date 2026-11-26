// Classic Cart Studio console backup and restore engine. Copyright (C) 2026 Rance9. GPL-3.0-or-later.
// A simulated console: a folder of files on the PC that answers the ccsbr verbs the way the recovery image does (identity, reads with
// the console's own count and MD5, the restore image's checked writes and readbacks). It stands in for the hardware in the tests and in
// the simulated mode of both entry points; it can only ever change files inside its own folder, and every backup made from it is
// marked simulated (refused for a real console). Folder layout (SimulatedConsole.Create builds one):
//   console.json        hwid, board, region, oem_version, nandinfo, parttable, active_hakchi_entries
//   dev/<target>        boot0, boot1, boot2, boot2-68 (absent: hakchi's spare slot is empty), nand, nanda..nande, phy, parttable
//   session.json        written when a recovery image is "loaded": its image id and profile
//   faults.json         optional, tests only: a fault to inject (see Fault)
//   writes.log          every write the simulated console performed (target, bytes, MD5)
//   hakchi-transfer/    hakchi's transfer folder on the data storage (ccsprep method/unlist place files there; NormalStart applies it)
//   hakchi-packages.json   the hakchi packages installed (hakchi's uninstall lists) and Classic Cart Studio's method in charge
//   nucleus-registry.json  CCS Nucleus's registry options (present while CCS Nucleus is installed); nucleus-staging/, nucleus-roots/ its
//                       image folders; console.json "data_free_bytes" (default 300,000,000) less those images and ccs-data is the free space
//   ccs-data/           Classic Cart Studio's folder on the data storage (/var/lib/ccs: CCS HOME's package copies, their pointer and records)
//   hakchi-data/        optional: hakchi's folders on the data storage (/var/lib/hakchi), where Original Menu's entries (.desktop files) are
using CcsWorker.Shell;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace CcsBackup.Device
{
    public sealed partial class SimulatedConsole : IConsoleShell   // (Community Mods: SimulatedCommunity.cs)
    {
        public readonly string Folder;
        readonly Dictionary<string, string> last = new Dictionary<string, string>();
        volatile bool online = true;
        public SimulatedConsole(string folder) { Folder = Path.GetFullPath(folder); if (!File.Exists(Path.Combine(Folder, "console.json"))) throw new ConsoleRefusedException("no-console", "This folder is not a simulated console."); }
        public string Kind => "simulated";
        public bool IsOnline => online;
        public void Disconnect() { online = false; }
        public void Dispose() { online = false; }

        public static string Dev(string folder, string target) => Path.Combine(folder, "dev", target);
        string D(string t) => Dev(Folder, t);
        Dictionary<string, object> Cfg() => Json.Parse(File.ReadAllText(Path.Combine(Folder, "console.json")));
        Dictionary<string, object> Session() { var p = Path.Combine(Folder, "session.json"); return File.Exists(p) ? Json.Parse(File.ReadAllText(p)) : null; }
        Dictionary<string, object> Fault() { var p = Path.Combine(Folder, "faults.json"); return File.Exists(p) ? Json.Parse(File.ReadAllText(p)) : null; }
        void Log(string line) { File.AppendAllText(Path.Combine(Folder, "writes.log"), DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture) + " " + line + "\n"); }

        /// <summary>"Loads" a recovery image: from now on the simulated console answers as that image (its id and profile).</summary>
        public static void Boot(string folder, string imageId, SessionProfile profile)
        {
            Restart(folder);
            File.WriteAllText(Path.Combine(folder, "session.json"), Json.Write(new JObj { { "image", imageId }, { "profile", profile.ToString().ToLowerInvariant() }, { "at", DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture) } }));
        }

        public int Execute(string command, Stream stdin, Stream stdout, Stream stderr, int timeoutMs)
        {
            if (!online) throw new ShellLostException("the simulated console is disconnected");
            if (command.StartsWith("/ccs/bin/ccsfiles ", StringComparison.Ordinal))
            { var sp = Session()?.S("profile") ?? "none"; if (sp != "files") { Err(stderr, "ccsfiles: this image cannot change files"); return 13; } return FilesVerb(Tokens(command).Skip(1).ToArray(), stdin, stdout); }
            var parts = command.Split(' ');
            var s = Session(); var profile = s?.S("profile") ?? "none";
            if (parts.Length == 4 && parts[0] == "/ccs/bin/ccsdev" && (parts[1] == "kstage" || parts[1] == "kburn")) { if (profile != "files") { Err(stderr, "ccsdev: this image cannot change the startup software"); return 13; } return DevVerb(parts[1], parts[2], parts[3], stdin, stdout); }
            if (parts.Length >= 2 && parts.Length <= 4 && parts[0] == "/ccs/bin/ccsdev") { if (profile != "files") { Err(stderr, "ccsdev: this image cannot change the startup software"); return 13; } return DevSystemVerb(parts[1], parts.Skip(2).ToArray(), stdin, stdout); }
            if (parts.Length >= 2 && parts.Length <= 4 && parts[0] == "/ccs/bin/ccsrescue") { if (profile != "rescue") { Err(stderr, "ccsrescue: this image cannot rescue a console"); return 13; } return RescueVerb(parts[1], parts.Skip(2).ToArray(), stdin, stdout); }
            if (parts.Length >= 2 && parts.Length <= 40 && parts[0] == RecoveryConsole.CommunityHelper) { if (profile != "prepare") { Err(stderr, "ccscomm: this image cannot change community mods"); return 13; } return CommVerb(parts[1], parts.Skip(2).ToArray(), stdin, stdout); }
            if (parts.Length >= 2 && parts.Length <= 7 && parts[0] == "/ccs/bin/ccsprep") { if (profile != "prepare") { Err(stderr, "ccsprep: this image cannot prepare a console"); return 13; } return PrepVerb(parts[1], parts.Skip(2).ToArray(), stdin, stdout); }
            if (parts.Length < 2 || parts[0] != RecoveryConsole.Helper) { Err(stderr, "not a ccsbr command"); return 127; }
            switch (parts[1])
            {
                case "identify": Out(stdout, Identify(s)); return 0;
                case "read": return ReadVerb(parts.Length > 2 ? parts[2] : "", stdout, true);
                case "range":
                    {
                        if (parts.Length != 5 || !RecoveryConsole.RangeTargets.Contains(parts[2]) || !long.TryParse(parts[3], out var off) || !int.TryParse(parts[4], out var len) || off % 4096 != 0 || len % 4096 != 0 || len <= 0 || len > RecoveryConsole.RangeMax) { Err(stderr, "ccsbr: bad range"); return 2; }
                        var path = D(parts[2]); if (!File.Exists(path)) { last[parts[2]] = "none 0 1 0"; return 1; }
                        using (var f = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite)) using (var md5 = MD5.Create())
                        {
                            var buf = new byte[len]; f.Position = Math.Min(off, f.Length); int n = 0, k; while (n < len && (k = f.Read(buf, n, len - n)) > 0) n += k;
                            md5.TransformFinalBlock(buf, 0, n); stdout.Write(buf, 0, n); last[parts[2]] = Hex.Of(md5.Hash) + " " + n.ToString(CultureInfo.InvariantCulture) + " 0 0"; return 0;
                        }
                    }
                case "digest": { int rc = ReadVerb(parts.Length > 2 ? parts[2] : "", null, false); Out(stdout, last.TryGetValue(parts[2], out var v) ? v + "\n" : ""); return rc; }
                case "sum": Out(stdout, parts.Length > 2 && last.TryGetValue(parts[2], out var sum) ? sum + "\n" : ""); return parts.Length > 2 && last.ContainsKey(parts[2]) ? 0 : 1;
                case "card": return 0;
                case "finish": Out(stdout, "restarting\n"); Log("finish (restart)"); return 0;
                case "write":
                    if (profile != "restore" && profile != "prepare") { Err(stderr, "ccsbr: this image cannot write"); return 13; }
                    return WriteVerb(parts, stdin, stdout);
            }
            Err(stderr, "ccsbr: unknown verb"); return 2;
        }

        static void Out(Stream s, string t) { var b = Encoding.UTF8.GetBytes(t); s?.Write(b, 0, b.Length); }
        static void Err(Stream s, string t) { Out(s, t + "\n"); }
        static string Md5(byte[] b) { using (var h = MD5.Create()) return Hex.Of(h.ComputeHash(b)); }
        static string Md5File(string p) { using (var h = MD5.Create()) using (var f = File.OpenRead(p)) return Hex.Of(h.ComputeHash(f)); }

        string Identify(Dictionary<string, object> s)
        {
            var c = Cfg(); var sb = new StringBuilder();
            void Kv(string k, string v) => sb.Append(k).Append('=').Append(v ?? "").Append('\n');
            var fault = Fault();
            Kv("ccsbr", "1"); Kv("image", s?.S("image") ?? ""); Kv("profile", s?.S("profile") ?? "");
            Kv("kernel", c.S("kernel") ?? Support.RecoveryKernel); Kv("hwid", c.S("hwid")); Kv("nandinfo", c.S("nandinfo") ?? Support.NandInfo);
            var parts = new StringBuilder();
            foreach (var t in new[] { "nand", "nanda", "nandb", "nandc", "nandd", "nande" })
                if (File.Exists(D(t))) parts.Append(t).Append(':').Append(new FileInfo(D(t)).Length / 1024).Append(',');
            Kv("partitions", parts.ToString()); Kv("parttable", c.S("parttable"));
            Kv("board", c.S("board")); Kv("region", c.S("region")); Kv("oem_version", c.S("oem_version"));
            Kv("system_mounted", "/dev/mapper/root-crypt:squashfs:ro,noatime");
            Kv("storage_mounts", fault?.S("storage_mounts") ?? "");
            Kv("readonly", "nand:1,nanda:1,nandb:1,nandc:1,nandd:1,nande:1,");
            if (File.Exists(D("boot2"))) { Kv("active_md5", Md5File(D("boot2"))); Kv("active_size", new FileInfo(D("boot2")).Length.ToString(CultureInfo.InvariantCulture)); }
            else Kv("active_md5", "none");
            Kv("active_hakchi_entries", c.L("active_hakchi_entries", 0).ToString(CultureInfo.InvariantCulture));
            if (File.Exists(D("boot2-68"))) { Kv("slot68", "valid"); Kv("slot68_md5", Md5File(D("boot2-68"))); Kv("slot68_size", new FileInfo(D("boot2-68")).Length.ToString(CultureInfo.InvariantCulture)); }
            else Kv("slot68", "absent");
            Kv("usb_role", "2"); Kv("end", "1");
            return sb.ToString();
        }

        int ReadVerb(string t, Stream stdout, bool send)
        {
            if (!RecoveryConsole.Targets.Contains(t)) return 2;
            var path = D(t); var f = Fault(); var ft = f?.O("read");
            bool faulty = ft != null && ft.S("target") == t; string mode = faulty ? ft.S("mode") : null; long after = faulty ? ft.L("after", 0) : -1;
            if (!File.Exists(path)) { last[t] = "none 0 1 0"; return 1; }
            using (var md5 = MD5.Create())
            using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 16))
            {
                var buf = new byte[256 * 1024]; long n = 0; int got; int src = 0;
                while ((got = fs.Read(buf, 0, buf.Length)) > 0)
                {
                    if (faulty && after >= 0 && n + got > after)
                    {
                        int keep = (int)(after - n);
                        if (mode == "disconnect") { if (send && keep > 0) stdout.Write(buf, 0, keep); online = false; throw new ShellLostException("the simulated console was disconnected during " + t); }
                        if (mode == "short") { got = keep; md5.TransformBlock(buf, 0, got, null, 0); if (send && got > 0) stdout.Write(buf, 0, got); n += got; src = 1; break; }
                        if (mode == "corrupt") { var copy = (byte[])buf.Clone(); copy[keep] ^= 0x5A; md5.TransformBlock(buf, 0, got, null, 0); if (send) stdout.Write(copy, 0, got); n += got; faulty = false; continue; }
                        if (mode == "hang") { if (send && keep > 0) stdout.Write(buf, 0, keep); Thread.Sleep(ft.L("ms", 5000) > 60000 ? 60000 : (int)ft.L("ms", 5000)); faulty = false; }
                    }
                    md5.TransformBlock(buf, 0, got, null, 0); if (send) stdout.Write(buf, 0, got); n += got;
                    if (faulty && mode == "status" && n >= after) src = 5;
                }
                md5.TransformFinalBlock(new byte[0], 0, 0);
                if ((t == "boot0" || t == "boot1") && src == 0 && !(f?.TryGetValue("lenient_stage_reads", out var lr) == true && lr is bool lb && lb) && BootImage.Check(File.ReadAllBytes(path), t == "boot0") != null) src = 1;   // as the console's reader: a stage whose image does not check out reads with an error
                var hash = Hex.Of(md5.Hash);
                if (faulty && mode == "lie") hash = new string('0', 32);
                last[t] = hash + " " + n.ToString(CultureInfo.InvariantCulture) + " " + src + " 0";
                return src == 0 ? 0 : 1;
            }
        }

        int WriteVerb(string[] p, Stream stdin, Stream stdout)
        {
            if (p.Length != 5 || !RecoveryConsole.WriteTargets.Contains(p[2]) || !long.TryParse(p[3], out var size) || !Regex.IsMatch(p[4], "^[0-9a-f]{32}$"))
            { Out(stdout, "result " + (p.Length > 2 ? p[2] : "?") + " refused request\n"); return 14; }
            var t = p[2]; var want = p[4]; var fw = Fault()?.O("write"); bool faulty = fw != null && fw.S("target") == t;
            var data = new MemoryStream(); var buf = new byte[1 << 16]; long left = size; int n;
            while (left > 0 && (n = stdin.Read(buf, 0, (int)Math.Min(buf.Length, left))) > 0) { data.Write(buf, 0, n); left -= n; }
            var bytes = data.ToArray();
            if (bytes.LongLength != size || Md5(bytes) != want) { Out(stdout, "result " + t + " refused received-mismatch\n"); return 15; }
            if ((t == "boot2" || t == "boot2-68") && !(bytes.Length > 2048 && Encoding.ASCII.GetString(bytes, 0, 8) == "ANDROID!")) { Out(stdout, "result " + t + " refused invalid-boot-image\n"); return 16; }
            if (t != "boot2" && t != "boot2-68" && (!File.Exists(D(t)) || new FileInfo(D(t)).Length < size)) { Out(stdout, "result " + t + " refused no-device\n"); return 16; }
            Out(stdout, "began " + t + "\n");
            if (faulty && fw.S("mode") == "disconnect") { var half = new byte[bytes.Length / 2]; Array.Copy(bytes, half, half.Length); Patch(t, half); Log("write " + t + " INTERRUPTED after " + half.Length); online = false; throw new ShellLostException("the simulated console was disconnected while writing " + t); }
            Patch(t, faulty && fw.S("mode") == "corrupt" ? Flip(bytes) : bytes);
            Log("write " + t + " " + bytes.Length + " " + want);
            var back = t == "boot2" || t == "boot2-68" ? Md5File(D(t)) : Md5(ReadPrefix(D(t), size));
            if (back != want) { Out(stdout, "result " + t + " failed readback " + back + "\n"); return 17; }
            Out(stdout, "result " + t + " ok " + back + " " + size.ToString(CultureInfo.InvariantCulture) + "\n"); return 0;
        }

        /// <summary>A start of the simulated console: like the console's storage driver, which reads the storage table only when it starts, the
        /// parts take the sizes the table gives them now (after hakchi's tune or shrink); a part that shrinks loses its end, one that grows
        /// reads zeros there.</summary>
        static void Restart(string folder)
        {
            foreach (var ram in Directory.GetFiles(folder, "ram-*.bin")) File.Delete(ram);   // RAM does not survive a start
            var cfg = Path.Combine(folder, "console.json"); if (!File.Exists(cfg)) return;
            var c = Json.Parse(File.ReadAllText(cfg)); if (!(c.TryGetValue("table_changed", out var tc) && tc is bool tb && tb)) return;   // only after tune or shrink
            var table = c.S("parttable") ?? "";
            foreach (Match m in Regex.Matches(table, "(nand[a-e]):[A-Za-z0-9_-]*:([0-9]+)"))
            {
                var dev = Dev(folder, m.Groups[1].Value); var want = long.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture) * 512;
                if (File.Exists(dev) && new FileInfo(dev).Length != want) using (var f = new FileStream(dev, FileMode.Open, FileAccess.Write)) f.SetLength(want);
            }
            var j = new JObj(); foreach (var kv in c) if (kv.Key != "table_changed") j.Add(kv.Key, kv.Value); File.WriteAllText(cfg, Json.Write(j));
        }

        /// <summary>the preparation steps' effect on a simulated console: hakchi's files (a marker beside the storage: a simulated console has
        /// no file system to change) and its tune of the storage table (the data part grows by half of UDISK; the parts follow at the next
        /// start), hakchi's u-boot (the console's own is kept as the flash tool's default), the original kernel's copy in the spare slot,
        /// hakchi's kernel; and Return to Original Software's steps (ccsprep ret-*: ret-stage keeps the bootloader the PC sends in the
        /// console's "RAM", a file the next start removes). faults.json: "prep" fails a step; "uboot_readback_differs" makes the written
        /// bootloader read back differently (a failed write)</summary>
        public static readonly byte[] SimHakchiUboot = BootImage.Synthetic(false, 2048, "SIMULATED HAKCHI U-BOOT");

        /// <summary>Developer Mode's kernel steps (ccsdev) on a simulated console: kstage keeps the image in "RAM"; kburn writes it once as the startup
        /// software and reads it back. faults.json "dev_readback_differs" makes it read back differently.</summary>
        int DevVerb(string step, string a1, string a2, Stream stdin, Stream stdout)
        {
            void Res(string t) => Out(stdout, "result " + step + " " + t + "\n");
            var staged = Path.Combine(Folder, "ram-dev-kernel.img");
            if (step == "kstage")
            {
                if (!Regex.IsMatch(a1, "^[1-9][0-9]{0,7}$") || !Regex.IsMatch(a2, "^[0-9a-f]{32}$")) { Res("refused request"); return 17; }
                int size = int.Parse(a1, CultureInfo.InvariantCulture); var got = new MemoryStream(); var buf = new byte[1 << 16]; int n;
                while (got.Length < size && stdin != null && (n = stdin.Read(buf, 0, (int)Math.Min(buf.Length, size - got.Length))) > 0) got.Write(buf, 0, n);
                var b = got.ToArray(); if (File.Exists(staged)) File.Delete(staged);
                if (b.Length != size || Md5(b) != a2) { Res("refused received-mismatch"); return 17; }
                if (Engine.DeveloperEngine.BootHeader(b).why != null) { Res("refused not a valid startup image"); return 17; }
                File.WriteAllBytes(staged, b); Res("ok " + a2 + " " + a1); return 0;
            }
            if (step == "kburn")
            {
                if (!File.Exists(staged) || Md5File(staged) != a2) { Res("refused the staged image is not the reviewed one"); return 17; }
                if (Md5File(D("boot2")) != a1) { Res("refused the startup software changed since the review"); return 17; }
                Out(stdout, "began kburn\n"); var w = File.ReadAllBytes(staged);
                if (Fault()?.TryGetValue("dev_readback_differs", out var dv) == true && dv is bool db && db) w[w.Length / 2] ^= 0x11;
                File.WriteAllBytes(D("boot2"), w); Log("dev kernel"); var back = Md5File(D("boot2"));
                if (back != a2) { Res("failed readback " + back + " burn=0 (not burned again)"); return 17; }
                Res("ok " + back); return 0;
            }
            Res("refused unknown step"); return 2;
        }

        // ---- Developer Mode (simulated): the system part and the test boot. The simulated console's system part (the "rootfs" device in its
        // table) is "encrypted" with a stand-in keystream (SimulatedSystemCipher: an XOR by position, not the console's real encryption, which
        // a simulated console does not have); console.json "system_key": false makes its key absent, "mem_free" sets the free RAM it reports.
        // faults.json "dev_system_readback_differs" makes a system write read back differently; "test_boot_fail" stops a test boot's transfer.

        /// <summary>the stand-in for the console's own system-part encryption (its own inverse); offset: the bytes' place in the part</summary>
        public static byte[] SimulatedSystemCipher(byte[] b, long offset = 0)
        {
            var o = new byte[b.Length]; byte[] ks = null; long blk = -1;
            using (var h = SHA256.Create())
                for (int i = 0; i < b.Length; i++)
                {
                    long at = offset + i, k = at / 32;
                    if (k != blk) { blk = k; ks = h.ComputeHash(Encoding.ASCII.GetBytes("ccs-simulated-system-key/" + k.ToString(CultureInfo.InvariantCulture))); }
                    o[i] = (byte)(b[i] ^ ks[at % 32]);
                }
            return o;
        }

        string SystemDev() { var m = Regex.Match(Cfg().S("parttable") ?? "", "(nand[a-e]):rootfs:"); return m.Success ? m.Groups[1].Value : null; }

        /// <summary>what ccsdev sinfo reports: the system part as the console's startup opens it (decrypted with its key)</summary>
        Dictionary<string, string> SystemInfo()
        {
            var r = new Dictionary<string, string>(); var dev = SystemDev(); var c = Cfg();
            r["mem_free"] = c.L("mem_free", 150000000).ToString(CultureInfo.InvariantCulture); r["board"] = c.S("board") ?? "";
            if (dev != "nandb" || !File.Exists(D(dev))) { r["system"] = "none"; return r; }
            var raw = File.ReadAllBytes(D(dev)); r["part_bytes"] = raw.LongLength.ToString(CultureInfo.InvariantCulture);
            bool plain = raw.Length >= 4 && Encoding.ASCII.GetString(raw, 0, 4) == "hsqs";
            bool key = !(c.TryGetValue("system_key", out var sk) && sk is bool skb && !skb);
            r["system"] = plain ? "plain" : "crypt"; r["key"] = plain || key ? "yes" : "no";
            if (!plain && !key) return r;
            var head = plain ? raw.Take(96).ToArray() : SimulatedSystemCipher(raw.Take(96).ToArray());
            if (head.Length == 96 && Encoding.ASCII.GetString(head, 0, 4) == "hsqs") { r["comp"] = BitConverter.ToUInt16(head, 20).ToString(CultureInfo.InvariantCulture); r["bytes_used"] = BitConverter.ToUInt64(head, 40).ToString(CultureInfo.InvariantCulture); }
            else r["comp"] = "none";
            return r;
        }

        /// <summary>ccsdev sinfo / sstage SIZE SHA256 / sburn NOWMD5 SHA256 on a simulated console (the same checks as the console's script)</summary>
        int DevSystemVerb(string step, string[] a, Stream stdin, Stream stdout)
        {
            void Res(string t) => Out(stdout, "result " + step + " " + t + "\n");
            var staged = Path.Combine(Folder, "ram-dev-system.bin"); var info = SystemInfo();
            if (step == "sinfo") { if (a.Length != 0) { Res("refused request"); return 17; } Out(stdout, string.Concat(info.Select(kv => kv.Key + "=" + kv.Value + "\n"))); Res("ok"); return 0; }
            if (step == "sstage")
            {
                if (a.Length != 2 || !Regex.IsMatch(a[0], "^[1-9][0-9]{0,8}$") || !Regex.IsMatch(a[1], "^[0-9a-f]{64}$")) { Res("refused request"); return 17; }
                long size = long.Parse(a[0], CultureInfo.InvariantCulture); if (File.Exists(staged)) File.Delete(staged);
                if (info["system"] == "none" || !info.ContainsKey("part_bytes")) { Res("refused no system part"); return 17; }
                if (size > long.Parse(info["part_bytes"], CultureInfo.InvariantCulture)) { Res("refused larger than the system part"); return 17; }
                if (size % 4096 != 0) { Res("refused not a whole number of 4,096-byte blocks"); return 17; }
                if (long.Parse(info["mem_free"], CultureInfo.InvariantCulture) < size + 32000000) { Res("refused not enough free memory to hold it"); return 17; }
                var got = new MemoryStream(); var buf = new byte[1 << 16]; int n;
                while (got.Length < size && stdin != null && (n = stdin.Read(buf, 0, (int)Math.Min(buf.Length, size - got.Length))) > 0) got.Write(buf, 0, n);
                var b = got.ToArray(); string sh; using (var h = SHA256.Create()) sh = Hex.Of(h.ComputeHash(b));
                if (b.LongLength != size || sh != a[1]) { Res("refused received-mismatch " + sh + " " + b.Length); return 17; }
                if (b.Length < 96 || Encoding.ASCII.GetString(b, 0, 4) != "hsqs" || BitConverter.ToUInt16(b, 28) != 4 || BitConverter.ToUInt16(b, 30) != 0 || BitConverter.ToUInt64(b, 40) > (ulong)size) { Res("refused not a SquashFS 4.0 system"); return 17; }
                if (!info.TryGetValue("comp", out var comp) || comp != BitConverter.ToUInt16(b, 20).ToString(CultureInfo.InvariantCulture)) { Res("refused its compression is not the current system's"); return 17; }
                File.WriteAllBytes(staged, b); Res("ok " + a[1] + " " + a[0]); return 0;
            }
            if (step == "sburn")
            {
                if (a.Length != 2 || !Regex.IsMatch(a[0], "^[0-9a-f]{32}$") || !Regex.IsMatch(a[1], "^[0-9a-f]{64}$")) { Res("refused request"); return 17; }
                if (!File.Exists(staged) || ShaFile(staged) != a[1]) { Res("refused the staged system is not the reviewed one"); return 17; }
                var dev = SystemDev(); if (dev != "nandb" || !File.Exists(D(dev))) { Res("refused no system part"); return 17; }
                if (Md5File(D(dev)) != a[0]) { Res("refused the system part changed since the review"); return 17; }
                if (info["system"] == "crypt" && info["key"] != "yes") { Res("refused the console's key is not available"); return 17; }
                Out(stdout, "began sburn\n"); var w = File.ReadAllBytes(staged); bool plain = info["system"] == "plain";
                if (Fault()?.TryGetValue("dev_system_readback_differs", out var dv) == true && dv is bool db && db) w[w.Length / 2] ^= 0x11;
                Patch(dev, plain ? w : SimulatedSystemCipher(w)); Log("dev system");
                var back = ReadPrefix(D(dev), w.Length); if (!plain) back = SimulatedSystemCipher(back);
                string bs; using (var h = SHA256.Create()) bs = Hex.Of(h.ComputeHash(back));
                if (bs != a[1]) { Res("failed readback " + bs + " write=0 (not written again)"); return 17; }
                Res("ok " + a[1] + " " + Md5File(D(dev))); return 0;
            }
            Res("refused unknown step"); return 2;
        }

        /// <summary>Developer Mode's test boot on a simulated console: its hardware id as the boot ROM's USB mode reports it</summary>
        public static string TestBootSid(string folder) { var p = Path.Combine(folder, "console.json"); return File.Exists(p) ? Json.Parse(File.ReadAllText(p)).S("hwid") : null; }

        /// <summary>the image "in RAM" and started: the console restarts out of any recovery session into the image (a file the next start
        /// removes); its storage files (dev/) are never touched; test-boot.log records it. null when sent, otherwise why not.</summary>
        public static string TestBoot(string folder, byte[] padded)
        {
            var f = Path.Combine(folder, "faults.json");
            if (File.Exists(f) && Json.Parse(File.ReadAllText(f)).TryGetValue("test_boot_fail", out var tf) && tf is bool tb && tb) return "the transfer stopped (simulated USB error)";
            Restart(folder);
            string sh; using (var h = SHA256.Create()) sh = Hex.Of(h.ComputeHash(padded));
            File.WriteAllBytes(Path.Combine(folder, "ram-test-boot.bin"), padded);
            File.WriteAllText(Path.Combine(folder, "session.json"), Json.Write(new JObj { { "image", "developer-test-boot/" + sh.Substring(0, 16) }, { "profile", "test-boot" }, { "at", DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture) } }));
            File.AppendAllText(Path.Combine(folder, "test-boot.log"), DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture) + " test boot " + padded.Length + " " + sh + "\n");
            return null;
        }

        /// <summary>how a simulated console's data storage (dev/nandc, an ext4 image) receives Expert/Developer file changes. Tests set one
        /// (e2fsprogs through WSL); without it, simulated file changes are refused (reading file states still works).</summary>
        public interface IFileStore { void Write(string consoleFolder, string path, byte[] data); void Delete(string consoleFolder, string path); }
        public static IFileStore FileStore;

        static string[] Tokens(string command)
        {
            var t = new List<string>(); var cur = new StringBuilder(); bool q = false, any = false;
            foreach (var ch in command)
            {
                if (ch == '\'') { q = !q; any = true; continue; }
                if (ch == ' ' && !q) { if (any) { t.Add(cur.ToString()); cur.Clear(); any = false; } continue; }
                cur.Append(ch); any = true;
            }
            if (any) t.Add(cur.ToString());
            return t.ToArray();
        }

        (string state, long bytes) FileState(string path)
        {
            using (var f = new FileStream(D("nandc"), FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                var fs = new Ext4.Ext4Reader(f); var ino = fs.Lookup("/" + path); if (ino == null) return ("absent", 0);
                var e = fs.Describe(ino.Value, "/" + path);
                if (e.Type == 'd') return ("folder", 0); if (e.Type == 'l') return ("link", 0); if (e.Type != 'f') return ("special", 0);
                using (var h = SHA256.Create()) return (Hex.Of(h.ComputeHash(fs.ReadAll(ino.Value, long.MaxValue))), e.Size);
            }
        }

        /// <summary>ccsfiles on a simulated console: the same path and scope rules, file states read from the storage image, changes through FileStore</summary>
        int FilesVerb(string[] a, Stream stdin, Stream stdout)
        {
            string step = a.Length > 0 ? a[0] : "?";
            void Res(string t) => Out(stdout, "result " + step + " " + t + "\n");
            if (a.Length < 3 || (step != "stat" && step != "put" && step != "del")) { Res("refused request"); return 2; }
            string scope = a[1], path = a[2];
            if (scope != "user" && scope != "dev" && scope != "compose") { Res("refused scope"); return 17; }
            if (!Engine.FilesEngine.PathOk(path)) { Res("refused path"); return 17; }
            if (!Engine.FilesEngine.InScope(scope, path)) { Res(scope == "compose" ? "refused not something Console Composer moves" : "refused not a user file"); return 17; }
            var (now, size) = FileState(path);
            if (step == "stat") { Res("ok " + now + " " + size.ToString(CultureInfo.InvariantCulture)); return 0; }
            if (FileStore == null) { Res("failed simulated file changes are not available"); return 17; }
            if (Engine.FilesEngine.InScope(scope, path) && path.Split('/').Length > 1)
            {   // a link on the way is refused, as on the console
                var parts = path.Split('/'); for (int i = 1; i < parts.Length; i++) { var (ps, _) = FileState(string.Join("/", parts.Take(i))); if (ps == "link") { Res("refused a link on the way"); return 17; } }
            }
            if (step == "put")
            {
                if (a.Length != 6 || !long.TryParse(a[3], NumberStyles.None, CultureInfo.InvariantCulture, out var n) || !Regex.IsMatch(a[4], "^[0-9a-f]{64}$") || !Regex.IsMatch(a[5], "^([0-9a-f]{64}|absent)$")) { Res("refused request"); return 17; }
                var got = new MemoryStream(); var buf = new byte[1 << 16]; int k;
                while (got.Length < n && stdin != null && (k = stdin.Read(buf, 0, (int)Math.Min(buf.Length, n - got.Length))) > 0) got.Write(buf, 0, k);
                var data = got.ToArray(); string sh; using (var h = SHA256.Create()) sh = Hex.Of(h.ComputeHash(data));
                if (data.LongLength != n || sh != a[4]) { Res("refused received-mismatch " + sh + " " + data.Length); return 17; }
                if (now != a[5]) { Res("refused the file changed since the review (" + now + ")"); return 17; }
                Out(stdout, "began put\n"); FileStore.Write(Folder, path, data); Log("files put " + path);
                var (back, _) = FileState(path); if (back != a[4]) { Res("failed readback " + back); return 17; }
                Res("ok " + a[4]); return 0;
            }
            if (a.Length != 4 || !Regex.IsMatch(a[3], "^[0-9a-f]{64}$")) { Res("refused request"); return 17; }
            if (now != a[3]) { Res("refused the file changed since the review (" + now + ")"); return 17; }
            Out(stdout, "began del\n"); FileStore.Delete(Folder, path); Log("files del " + path);
            var (gone, _) = FileState(path); if (gone != "absent") { Res("failed readback " + gone); return 17; }
            Res("ok absent"); return 0;
        }

        /// <summary>Console Rescue's steps (ccsrescue) on a simulated console: diag reads each stage (MD5, or ccsboot's typed result: noimage
        /// when no image header is there, invalid when its checksum or structure does not hold, unreadable while the marker file
        /// "unreadable-boot0"/"unreadable-boot1" exists, nodriver with the fault "nand_driver_fails"; the flash tool says "damaged" for any of
        /// them); stage0/stage1 keep the PC's bytes in "RAM" (a file the next start removes); burn0/burn1 write them once and read back, the
        /// unreadable stage only when NOW says so (the write removes its marker, as rewriting the damaged blocks would).
        /// faults.json: "rescue_readback_differs" (a stage reads back differently after its write), "readers_disagree" (the flash tool sees
        /// another boot1), "prep": {"step": ...} fails that step, "burn_stops": {"step": ..., "blocks": N} stops the writer after N blocks
        /// (the first N × 128 KB of the new bytes written, the rest as it was)</summary>
        int RescueVerb(string step, string[] args, Stream stdin, Stream stdout)
        {
            void Res(string t) => Out(stdout, "result " + step + " " + t + "\n");
            var f = Fault(); bool fail = f?.O("prep")?.S("step") == step;
            bool nodriver = f?.TryGetValue("nand_driver_fails", out var nd) == true && nd is bool ndb && ndb;
            string State(string t)
            {
                if (nodriver) return "nodriver";
                if (File.Exists(Path.Combine(Folder, "unreadable-" + t))) return "unreadable";
                var p = D(t); if (!File.Exists(p)) return "noimage";
                var b = File.ReadAllBytes(p); var why = BootImage.Check(b, t == "boot0");
                return why == null ? Md5(b) : why.StartsWith("not a boot") ? "noimage" : "invalid";
            }
            string Tool(string s) => Engine.RescueDiagnosis.IsMd5(s) ? s : "damaged";
            switch (step)
            {
                case "diag":
                    {
                        var b1 = State("boot1"); var other = f?.TryGetValue("readers_disagree", out var rd) == true && rd is bool rb && rb;
                        Out(stdout, "boot0=" + State("boot0") + "\nboot0_size=" + (File.Exists(D("boot0")) ? new FileInfo(D("boot0")).Length : 0) + "\nboot1=" + b1 + "\nboot1_flash_tool=" + (other ? new string('e', 32) : Tool(b1)) + "\nboot1_size=" + (File.Exists(D("boot1")) ? new FileInfo(D("boot1")).Length : 0) + "\n");
                        Res("ok"); return 0;
                    }
                case "stage0": case "stage1":
                    {
                        var kind = "boot" + step.Substring(5); var staged = Path.Combine(Folder, "ram-rescue-" + kind + ".bin");
                        if (args.Length != 2 || !Regex.IsMatch(args[0], "^[1-9][0-9]{0,6}$") || !Regex.IsMatch(args[1], "^[0-9a-f]{32}$")) { Res("refused request"); return 17; }
                        int size = int.Parse(args[0], CultureInfo.InvariantCulture); var got = new MemoryStream(); var buf = new byte[1 << 16]; int n;
                        while (got.Length < size && stdin != null && (n = stdin.Read(buf, 0, (int)Math.Min(buf.Length, size - got.Length))) > 0) got.Write(buf, 0, n);
                        var bytes = got.ToArray(); if (File.Exists(staged)) File.Delete(staged);
                        if (bytes.Length != size || Md5(bytes) != args[1]) { Res("refused received-mismatch " + Md5(bytes) + " " + bytes.Length); return 17; }
                        if (BootImage.Check(bytes, kind == "boot0") != null) { Res("refused not a complete " + kind + " image"); return 17; }
                        File.WriteAllBytes(staged, bytes); Res("ok " + args[1] + " " + args[0]); return 0;
                    }
                case "burn0": case "burn1":
                    {
                        var kind = "boot" + step.Substring(4); var staged = Path.Combine(Folder, "ram-rescue-" + kind + ".bin");
                        if (args.Length != 2 || !Regex.IsMatch(args[0], "^([0-9a-f]{32}|invalid|noimage|unreadable)$") || !Regex.IsMatch(args[1], "^[0-9a-f]{32}$")) { Res("refused md5"); return 17; }
                        if (!File.Exists(staged) || Md5File(staged) != args[1]) { Res("refused the staged " + kind + " is not the reviewed one"); return 17; }
                        if (State(kind) != args[0]) { Res("refused " + kind + " changed since the review (" + State(kind) + ")"); return 17; }
                        Out(stdout, "began " + step + "\n"); if (fail) { Res("failed burn (not burned again)"); return 17; }
                        var w = File.ReadAllBytes(staged); if (f?.TryGetValue("rescue_readback_differs", out var dv) == true && dv is bool db && db) w[w.Length / 2] ^= 0x5A;
                        var stop = f?.O("burn_stops");
                        if (stop?.S("step") == step)
                        {   // the writer stopped after N blocks: those written, the rest as it was; the stage is then partly written
                            int blocks = (int)stop.L("blocks", 0); var cur = File.Exists(D(kind)) ? File.ReadAllBytes(D(kind)) : new byte[0]; var mix = new byte[Math.Max(cur.Length, w.Length)];
                            for (int i = 0; i < mix.Length; i++) mix[i] = i < blocks * 0x20000 && i < w.Length ? w[i] : i < cur.Length ? cur[i] : (byte)0xff;
                            File.WriteAllBytes(D(kind), mix); Log("rescue " + step + " stopped " + blocks); Res("failed burn stopped " + blocks + " (not burned again)"); return 17;
                        }
                        File.WriteAllBytes(D(kind), w); Log("rescue " + step + (args[0] == "unreadable" ? " unreadable" : ""));
                        var um = Path.Combine(Folder, "unreadable-" + kind); if (File.Exists(um)) File.Delete(um);
                        var back = Md5File(D(kind)); if (back != args[1]) { Res("failed readback " + back + " (not burned again)"); return 17; }
                        Res("ok " + back); return 0;
                    }
            }
            Res("refused unknown step"); return 2;
        }
        void SetCfg(string key, object value) { var c = Cfg(); var j = new JObj(); foreach (var kv in c) if (kv.Key != key) j.Add(kv.Key, kv.Value); if (value != null) j.Add(key, value); File.WriteAllText(Path.Combine(Folder, "console.json"), Json.Write(j)); }
        static string Tuned(string table)
        {
            var e = Regex.Match(table ?? "", "nande:([A-Za-z0-9_-]*):([0-9]+)"); var c = Regex.Match(table ?? "", "nandc:([A-Za-z0-9_-]*):([0-9]+)");
            if (!e.Success || !c.Success) return table;
            long eb = long.Parse(e.Groups[2].Value, CultureInfo.InvariantCulture), give = eb / 2 / 2048 * 2048;
            return table.Replace(c.Value, "nandc:" + c.Groups[1].Value + ":" + (long.Parse(c.Groups[2].Value, CultureInfo.InvariantCulture) + give).ToString(CultureInfo.InvariantCulture))
                        .Replace(e.Value, "nande:" + e.Groups[1].Value + ":" + (eb - give).ToString(CultureInfo.InvariantCulture));
        }
        // ---- CCS Mod and CCS Nucleus: hakchi's transfer folder and installed packages, beside the storage (a simulated console has no file
        // system to change); hakchi's version is read from the data storage's ext4 image when its files are not the preparation's marker
        string TransferDir => Path.Combine(Folder, "hakchi-transfer");
        string HakchiOnData()
        {
            if (File.Exists(Path.Combine(Folder, "prepared-files.txt"))) return Engine.Preparation.PinnedHakchiVersion;
            try
            {
                var dev = Regex.Match(Cfg().S("parttable") ?? "", "(nand[a-e]):data:").Groups[1].Value; if (dev.Length == 0) dev = "nandc";
                using (var fs = new FileStream(D(dev), FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    var x = new Ext4.Ext4Reader(fs); var d = x.Lookup("/hakchi/rootfs/etc/preinit.d/b0000_defines"); if (d == null) return "none";
                    var m = Regex.Match(Encoding.UTF8.GetString(x.ReadAll(d.Value, 100000)), "hakchiVersion='([^']{1,40})'"); return m.Success ? m.Groups[1].Value : "none";
                }
            }
            catch (Exception) { return "none"; }
        }
        static Dictionary<string, object> Packages(string folder) { var p = Path.Combine(folder, "hakchi-packages.json"); return File.Exists(p) ? Json.Parse(File.ReadAllText(p)) : new Dictionary<string, object>(); }
        static void SavePackages(string folder, List<string> packages) => File.WriteAllText(Path.Combine(folder, "hakchi-packages.json"), Json.Write(new JObj { { "packages", packages.Cast<object>().ToList() } }));
        static string WaitingSha(string file)
        {
            var t = File.ReadAllBytes(file); var m = Regex.Match(Encoding.ASCII.GetString(t), "^simulated [a-z-]+\\.hmod ([0-9a-f]{64})\n$");
            if (m.Success) return m.Groups[1].Value; using (var h = SHA256.Create()) return Hex.Of(h.ComputeHash(t));
        }
        // CCS Nucleus's start setting (simulated; its registry on the console): nucleus-start.txt holds "DEFAULT CHOOSER HOME SELECTION
        // TIMEOUT"; none without it
        static string StartOf(string folder) { var f = Path.Combine(folder, "nucleus-start.txt"); return File.Exists(f) ? File.ReadAllText(f).Trim() : "none"; }
        static void SetStart(string folder, string line) => File.WriteAllText(Path.Combine(folder, "nucleus-start.txt"), line + "\n");
        // CCS Nucleus's registry and image folders (simulated): its options while it is installed
        string RegistryPath => Path.Combine(Folder, "nucleus-registry.json");
        string NStage => Path.Combine(Folder, "nucleus-staging"); string NRoots => Path.Combine(Folder, "nucleus-roots");
        bool NucleusInstalled() => (Packages(Folder).A("packages") ?? new List<object>()).OfType<string>().Contains("ccs-nucleus");
        Dictionary<string, object> Registry()
        {
            if (File.Exists(RegistryPath)) return Json.Parse(File.ReadAllText(RegistryPath)).O("options") ?? new Dictionary<string, object>();
            return new Dictionary<string, object> { { "native", new Dictionary<string, object> { { "kind", "native" } } }, { "ccs-home", new Dictionary<string, object> { { "kind", "ccs-home" } } } };
        }
        void SaveRegistry(Dictionary<string, object> opts) { var j = new JObj(); foreach (var kv in opts.OrderBy(x => x.Key, StringComparer.Ordinal)) j.Add(kv.Key, kv.Value); File.WriteAllText(RegistryPath, Json.Write(new JObj { { "options", j } })); }
        long FreeBytes()
        {
            long used = 0; foreach (var d in new[] { NStage, NRoots }) if (Directory.Exists(d)) used += Directory.GetFiles(d).Sum(x => new FileInfo(x).Length);
            if (Directory.Exists(HomeDir)) used += Directory.GetFiles(HomeDir, "*", SearchOption.AllDirectories).Sum(x => new FileInfo(x).Length);
            return Cfg().L("data_free_bytes", 300000000) - used;
        }
        // CCS HOME's package (ccsprep hput, huse): /var/lib/ccs as a folder beside the storage, in the installer's layout (pkg/<copy>,
        // pkg/current, install/v-<copy>.json and attempt-<a>.json, data/IDENTITY.json, staging/<copy>). console.json "ram_bytes" (default
        // 180,000,000) is the RAM free for a transfer. faults.json: "prep": {"step": "hput"} stops after half of the files were unpacked into
        // the staging folder (the records written), "home_readback_differs" changes one byte of the copy after it was unpacked, "home_link"
        // reports a link on the way
        public string HomeDir => Path.Combine(Folder, "ccs-data");
        static readonly Regex CopyName = new Regex("^[0-9a-f]{16}\\.[0-9a-f]{8}$"), TarName = new Regex("^(pkg|rec)/([A-Za-z0-9_][A-Za-z0-9._-]*/?)*$");
        string HomeCur() { var f = Path.Combine(HomeDir, "pkg", "current"); if (!File.Exists(f)) return Directory.Exists(f) ? "other" : "absent"; var b = File.ReadAllBytes(f); var v = Encoding.ASCII.GetString(b).TrimEnd('\n'); return b.Length == 26 && CopyName.IsMatch(v) && b[25] == (byte)'\n' ? v : "other"; }
        static Dictionary<string, string> RecordFiles(string vrec)
        {
            try { var f = Json.Parse(File.ReadAllText(vrec)).O("files"); return f?.ToDictionary(x => x.Key, x => x.Value as string, StringComparer.Ordinal); } catch (Exception) { return null; }
        }
        static bool CopyComplete(string dir, Dictionary<string, string> files)
        {
            if (files == null || files.Count == 0 || !Directory.Exists(dir)) return false;
            var have = Directory.GetFiles(dir, "*", SearchOption.AllDirectories).Select(x => x.Substring(dir.Length + 1).Replace('\\', '/')).ToList();
            return have.Count == files.Count && files.All(kv => File.Exists(Path.Combine(dir, kv.Key)) && ShaFile(Path.Combine(dir, kv.Key)) == kv.Value);
        }
        static List<(string name, char type, byte[] data)> ReadTar(byte[] b)
        {
            var o = new List<(string, char, byte[])>(); int p = 0;
            while (p + 512 <= b.Length)
            {
                if (b.Skip(p).Take(512).All(x => x == 0)) break;
                var name = Encoding.ASCII.GetString(b, p, 100).TrimEnd('\0'); var size = Convert.ToInt64(Encoding.ASCII.GetString(b, p + 124, 11).Trim('\0', ' '), 8); var t = (char)b[p + 156];
                var data = new byte[size]; Array.Copy(b, p + 512, data, 0, size); o.Add((name, t, data)); p += 512 + (int)((size + 511) / 512 * 512);
            }
            return o;
        }
        int HomeVerb(string step, string[] a, Stream stdin, Stream stdout, bool fail)
        {
            void Res(string t) => Out(stdout, "result " + step + " " + t + "\n");
            string P = Path.Combine(HomeDir, "pkg"), I = Path.Combine(HomeDir, "install");
            bool link = Fault()?.TryGetValue("home_link", out var hl) == true && hl is bool hb && hb;
            if (step == "hput")
            {
                if (a.Length != 4 || !CopyName.IsMatch(a[0]) || !Regex.IsMatch(a[1], "^[1-9][0-9]{0,8}$") || !Regex.IsMatch(a[2], "^[0-9a-f]{64}$") || !Regex.IsMatch(a[3], "^[1-9][0-9]{0,8}$")) { Res("refused request"); return 17; }
                string copy = a[0], att = copy.Substring(17); long size = long.Parse(a[1], CultureInfo.InvariantCulture), total = long.Parse(a[3], CultureInfo.InvariantCulture);
                if (Cfg().L("ram_bytes", 180000000) < size + 16000000) { Res("refused not enough memory to receive it"); return 17; }
                var got = new MemoryStream(); var buf = new byte[1 << 16]; int n;
                while (got.Length < size && stdin != null && (n = stdin.Read(buf, 0, (int)Math.Min(buf.Length, size - got.Length))) > 0) got.Write(buf, 0, n);
                var bytes = got.ToArray(); string sh; using (var h = SHA256.Create()) sh = Hex.Of(h.ComputeHash(bytes));
                if (bytes.LongLength != size || sh != a[2]) { Res("refused received-mismatch " + sh + " " + bytes.Length); return 17; }
                if (Cfg().B("keep_home_archive"))   // tests: what the engine sent, kept for the image's own steps under qemu (test_recovery_images.py --home-archive)
                { File.WriteAllBytes(Path.Combine(Folder, "home-archive.tar"), bytes); File.WriteAllText(Path.Combine(Folder, "home-archive.json"), Json.Write(new JObj { { "copy", copy }, { "size", size }, { "sha256", sh }, { "total", total } })); }
                List<(string name, char type, byte[] data)> ents;
                try { ents = ReadTar(bytes); } catch (Exception) { Res("refused not a HOME package archive for " + copy); return 17; }
                var rec = ents.Where(e => e.type == '0' && e.name.StartsWith("rec/")).ToDictionary(e => e.name.Substring(4), e => e.data);
                if (ents.Any(e => e.type != '0' && e.type != '5' || !TarName.IsMatch(e.name)) || rec.Count != 3 || !rec.ContainsKey("v-" + copy + ".json") || !rec.ContainsKey("attempt-" + att + ".json") || !rec.ContainsKey("IDENTITY.json")
                    || !Encoding.ASCII.GetString(rec["v-" + copy + ".json"]).Contains("\"copy\": \"" + copy + "\"")) { Res("refused not a HOME package archive for " + copy); return 17; }
                var tmp = Path.Combine(Folder, "ram-home-v.json"); File.WriteAllBytes(tmp, rec["v-" + copy + ".json"]); var sums = RecordFiles(tmp); File.Delete(tmp);
                var pkgFiles = ents.Where(e => e.type == '0' && e.name.StartsWith("pkg/")).ToList();
                if (sums == null || sums.Count != pkgFiles.Count) { Res("refused the archive's files are not its copy record's"); return 17; }
                if (link) { Res("refused a link is on the way (ccs)"); return 17; }
                var other = Directory.Exists(I) ? Directory.GetFiles(I, "attempt-*.json").Select(Path.GetFileName).Where(x => x != "attempt-" + att + ".json").ToList() : new List<string>();
                if (other.Count > 0) { Res("refused an earlier installation of a HOME did not finish (" + string.Join(" ", other) + ")"); return 17; }
                bool mine = File.Exists(Path.Combine(I, "attempt-" + att + ".json")); string vpath = Path.Combine(I, "v-" + copy + ".json"), cdir = Path.Combine(P, copy);
                bool sameV = File.Exists(vpath) && File.ReadAllBytes(vpath).SequenceEqual(rec["v-" + copy + ".json"]);
                if (File.Exists(vpath) || Directory.Exists(cdir))
                {
                    if (sameV && CopyComplete(cdir, sums)) { Res("ok already " + copy + " " + a[2]); return 0; }
                    if (!(mine && !Directory.Exists(cdir) && sameV)) { Res("refused another copy named " + copy + " is already there (it is kept)"); return 17; }
                }
                var stage = Path.Combine(HomeDir, "staging", copy); long part = Directory.Exists(stage) ? Directory.GetFiles(stage, "*", SearchOption.AllDirectories).Sum(x => new FileInfo(x).Length) : 0;
                if (FreeBytes() + part < total + 8000000) { Res("refused not enough free space (" + FreeBytes() + " bytes free)"); return 17; }
                Out(stdout, "began hput\n");
                Directory.CreateDirectory(I);
                foreach (var x in new[] { "attempt-" + att + ".json", "v-" + copy + ".json" }) if (!File.Exists(Path.Combine(I, x))) File.WriteAllBytes(Path.Combine(I, x), rec[x]);
                if (Directory.Exists(stage)) Directory.Delete(stage, true);
                Directory.CreateDirectory(Path.Combine(stage, "pkg")); int k = 0;
                foreach (var e in pkgFiles)
                {
                    if (fail && k++ >= pkgFiles.Count / 2) { Log("hput " + copy + " INTERRUPTED"); Res("failed write (the HOME in use was not changed; not written again)"); return 17; }
                    var q = Path.Combine(stage, e.name.Replace('/', Path.DirectorySeparatorChar)); Directory.CreateDirectory(Path.GetDirectoryName(q)); File.WriteAllBytes(q, e.data);
                }
                Directory.CreateDirectory(P); Directory.Move(Path.Combine(stage, "pkg"), cdir); Directory.Delete(stage); if (!Directory.EnumerateFileSystemEntries(Path.Combine(HomeDir, "staging")).Any()) Directory.Delete(Path.Combine(HomeDir, "staging"));
                var idf = Path.Combine(HomeDir, "data", "IDENTITY.json"); if (!File.Exists(idf)) { Directory.CreateDirectory(Path.GetDirectoryName(idf)); File.WriteAllBytes(idf, rec["IDENTITY.json"]); }
                if (Fault()?.TryGetValue("home_readback_differs", out var rd) == true && rd is bool rdb && rdb) { var f0 = Path.Combine(cdir, "session.json"); var b0 = File.ReadAllBytes(f0); b0[0] ^= 0x20; File.WriteAllBytes(f0, b0); }
                Log("hput " + copy);
                if (!CopyComplete(cdir, sums)) { Res("failed readback (the HOME in use was not changed; nothing was switched)"); return 17; }
                Res("ok " + copy + " " + a[2]); return 0;
            }
            if (step == "huse")
            {
                if (a.Length != 2 || !CopyName.IsMatch(a[0]) || !(a[1] == "absent" || CopyName.IsMatch(a[1]))) { Res("refused request"); return 17; }
                string copy = a[0], att = copy.Substring(17), cur = HomeCur(); bool mine = File.Exists(Path.Combine(I, "attempt-" + att + ".json"));
                if (link) { Res("refused a link is on the way (ccs)"); return 17; }
                if (!CopyComplete(Path.Combine(P, copy), RecordFiles(Path.Combine(I, "v-" + copy + ".json")))) { Res("refused the copy " + copy + " is not complete on the console (nothing was switched)"); return 17; }
                if (cur == copy && !mine) { Res("ok already " + copy); return 0; }
                if (cur != a[1] && cur != copy) { Res("refused the HOME in use changed since the review (" + cur + ")"); return 17; }
                if (!mine) { Res("refused no installation of " + copy + " is in progress"); return 17; }
                Out(stdout, "began huse\n"); if (fail) { Res("failed switch (not written again)"); return 17; }
                if (cur != copy) File.WriteAllText(Path.Combine(P, "current"), copy + "\n");
                File.Delete(Path.Combine(I, "attempt-" + att + ".json")); Log("huse " + copy);
                Res("ok " + copy); return 0;
            }
            if (step == "hdrop")   // ccsprep hdrop: an earlier copy removed only while another is in use and it is exactly what its record names
            {
                if (a.Length != 2 || !CopyName.IsMatch(a[0]) || !CopyName.IsMatch(a[1]) || a[0] == a[1]) { Res("refused request"); return 17; }
                string copy = a[0], keep = a[1], cur = HomeCur(), cdir = Path.Combine(P, copy), vpath = Path.Combine(I, "v-" + copy + ".json");
                if (link) { Res("refused a link is on the way (ccs)"); return 17; }
                if (cur != keep) { Res("refused the HOME in use is not " + keep + " (" + cur + ")"); return 17; }
                var other = Directory.Exists(I) ? Directory.GetFiles(I, "attempt-*.json").Select(Path.GetFileName).ToList() : new List<string>();
                if (other.Count > 0) { Res("refused an installation did not finish (" + string.Join(" ", other) + ")"); return 17; }
                if (!Directory.Exists(cdir) && !File.Exists(vpath)) { Res("ok already " + copy); return 0; }
                var named = Named(copy); if (named != "no") { Res("refused an entry of Original Menu may name the copy " + copy + " (" + named + "; it is kept)"); return 17; }
                if (!File.Exists(vpath) || !CopyComplete(cdir, RecordFiles(vpath))) { Res("refused the copy " + copy + " is not exactly what its record names (it is kept)"); return 17; }
                Out(stdout, "began hdrop\n");
                if (fail) { var first = Directory.GetFiles(cdir, "*", SearchOption.AllDirectories).OrderBy(x => x, StringComparer.Ordinal).First(); File.Delete(first); Log("hdrop " + copy + " INTERRUPTED"); Res("failed removal (stopped; the backup made before keeps the copy)"); return 17; }
                Directory.Delete(cdir, true); File.Delete(vpath); Log("hdrop " + copy);
                if (Directory.Exists(cdir) || File.Exists(vpath) || HomeCur() != keep) { Res("failed readback"); return 17; }
                Res("ok " + copy); return 0;
            }
            Res("refused unknown step"); return 2;
        }
        /// <summary>ccsprep mstate's home_copy_info fields after the copy's name: the copy in use before it (its record), exact | differs |
        /// nodir, its files' bytes, the space it takes in KB (du: each file and folder in 4 KB blocks), its collection name (base64 of the
        /// MANIFEST.json text between the quotes), games, its record's installed_utc and whether an entry of Original Menu names it</summary>
        string CopyInfo(string c, string record)
        {
            string prev = "-", x = "nodir", b = "-", k = "-", l = "-", g = "-", t = "-"; var dir = Path.Combine(HomeDir, "pkg", c);
            try
            {
                var rj = Json.Parse(File.ReadAllText(record)); var pv = rj.S("previous"); if (pv != null && CopyName.IsMatch(pv)) prev = pv;
                var iu = rj.S("installed_utc"); if (iu != null && Regex.IsMatch(iu, "^[0-9]{4}-[0-9]{2}-[0-9]{2}T[0-9]{2}:[0-9]{2}:[0-9]{2}Z$")) t = iu;
            }
            catch (Exception) { }
            if (Directory.Exists(dir))
            {
                x = CopyComplete(dir, RecordFiles(record)) ? "exact" : "differs";
                var files = Directory.GetFiles(dir, "*", SearchOption.AllDirectories); var dirs = Directory.GetDirectories(dir, "*", SearchOption.AllDirectories).Length + 1;
                b = files.Sum(f => new FileInfo(f).Length).ToString(CultureInfo.InvariantCulture);
                k = (files.Sum(f => (new FileInfo(f).Length + 4095) / 4096 * 4) + dirs * 4).ToString(CultureInfo.InvariantCulture);
                var mf = Path.Combine(dir, "MANIFEST.json");
                if (File.Exists(mf))
                {
                    var mt = File.ReadAllText(mf);
                    var lm = Regex.Match(mt, "^ \"label\": \"(.*)\",?$", RegexOptions.Multiline); if (lm.Success && lm.Groups[1].Value.Length > 0) l = Convert.ToBase64String(Encoding.UTF8.GetBytes(lm.Groups[1].Value.Length > 400 ? lm.Groups[1].Value.Substring(0, 400) : lm.Groups[1].Value));
                    var gm = Regex.Match(mt, "^ \"games\": ([0-9]{1,6}),?$", RegexOptions.Multiline); if (gm.Success) g = gm.Groups[1].Value;
                }
            }
            return prev + " " + x + " " + b + " " + k + " " + l + " " + g + " " + t + " " + Named(c);
        }

        /// <summary>ccsprep hnamed: whether an entry of Original Menu names the copy's files (yes), none here but the console program recorded
        /// entries on a USB drive or SD card (unknown), or no</summary>
        string Named(string c)
        {
            var hk = Path.Combine(Folder, "hakchi-data");
            if (Directory.Exists(hk) && Directory.GetFiles(hk, "*.desktop", SearchOption.AllDirectories).Any(f => File.ReadAllText(f).Contains("ccs/pkg/" + c + "/"))) return "yes";
            var own = Path.Combine(HomeDir, "owned", "published.own");
            if (File.Exists(own) && File.ReadAllLines(own).Any(l => Regex.IsMatch(l, "^file=[0-9a-f]{64} [0-7]{4} /media/"))) return "unknown";
            return "no";
        }

        /// <summary>mstate's CCS HOME lines, as ccsprep prints them</summary>
        string HomeState()
        {
            var sb = new StringBuilder();
            if (Fault()?.TryGetValue("home_link", out var hl) == true && hl is bool hb && hb) sb.Append("home=link\n");
            else
            {
                sb.Append("home_pointer=" + HomeCur() + "\n"); var I = Path.Combine(HomeDir, "install");
                if (Directory.Exists(I))
                {
                    foreach (var f in Directory.GetFiles(I, "v-*.json").OrderBy(x => x, StringComparer.Ordinal))
                    {
                        var c = Path.GetFileName(f).Substring(2); c = c.Substring(0, c.Length - 5); if (!CopyName.IsMatch(c)) continue;
                        string m = null; try { m = Json.Parse(File.ReadAllText(f)).S("manifest_sha256"); } catch (Exception) { }
                        sb.Append("home_copy=" + c + " " + (m != null && Regex.IsMatch(m, "^[0-9a-f]{64}$") ? m : "-") + " " + (Directory.Exists(Path.Combine(HomeDir, "pkg", c)) ? "dir" : "nodir") + "\n");
                        sb.Append("home_copy_info=" + c + " " + CopyInfo(c, f) + "\n");   // ccsprep mstate's details of the copy (the earlier-versions review)
                    }
                    foreach (var f in Directory.GetFiles(I, "attempt-*.json").OrderBy(x => x, StringComparer.Ordinal))
                    {
                        var at = Path.GetFileName(f).Substring(8); at = at.Substring(0, at.Length - 5); string c = null; try { c = Json.Parse(File.ReadAllText(f)).S("copy"); } catch (Exception) { }
                        sb.Append("home_attempt=" + at + " " + (c != null && CopyName.IsMatch(c) ? c : "-") + "\n");
                    }
                }
                var idf = Path.Combine(HomeDir, "data", "IDENTITY.json");
                sb.Append("home_identity=" + (File.Exists(idf) ? Convert.ToBase64String(File.ReadAllBytes(idf)) : "absent") + "\n");
            }
            sb.Append("ram=" + Cfg().L("ram_bytes", 180000000).ToString(CultureInfo.InvariantCulture) + "\n");
            return sb.ToString();
        }
        static string ShaFile(string p) { using (var h = SHA256.Create()) using (var f = File.OpenRead(p)) return Hex.Of(h.ComputeHash(f)); }

        /// <summary>ccsprep rput / rimport / rdrop on a simulated console: an owner's system image as a CCS Nucleus choice (faults.json
        /// "prep": {"step": ...} fails that step after it began)</summary>
        int RootVerb(string step, string[] a, Stream stdin, Stream stdout, bool fail)
        {
            void Res(string t) => Out(stdout, "result " + step + " " + t + "\n");
            if (a.Length < 2 || !Engine.FirmwareEngine.IdOk(a[0])) { Res("refused request"); return 17; }
            if (HakchiOnData() == "none" || !NucleusInstalled()) { Res("refused CCS Nucleus is not installed"); return 17; }
            string id = a[0], t = Path.Combine(NStage, id + ".hsqs"), dest = Path.Combine(NRoots, id + ".hsqs"); var reg = Registry();
            if (step == "rput")
            {
                if (a.Length != 3 || !Regex.IsMatch(a[1], "^[1-9][0-9]{0,8}$") || !Regex.IsMatch(a[2], "^[0-9a-f]{64}$")) { Res("refused request"); return 17; }
                long size = long.Parse(a[1], CultureInfo.InvariantCulture); string want = a[2];
                byte[] Receive() { var got = new MemoryStream(); var buf = new byte[1 << 16]; int n; while (got.Length < size && stdin != null && (n = stdin.Read(buf, 0, (int)Math.Min(buf.Length, size - got.Length))) > 0) got.Write(buf, 0, n); return got.ToArray(); }
                foreach (var x in new[] { dest, t }) if (File.Exists(x)) { if (ShaFile(x) == want) { Receive(); Res("ok already " + want); return 0; } Res("refused another file is already there"); return 17; }
                if (FreeBytes() < size + 8000000) { Res("refused not enough free space (" + FreeBytes() + " bytes free)"); return 17; }
                Out(stdout, "began rput\n"); if (fail) { Res("failed write (not written again)"); return 17; }
                var b = Receive(); string sh; using (var h = SHA256.Create()) sh = Hex.Of(h.ComputeHash(b));
                if (b.LongLength != size || sh != want || b.Length < 4 || Encoding.ASCII.GetString(b, 0, 4) != "hsqs") { Res("refused received-mismatch " + sh + " " + b.Length); return 17; }
                Directory.CreateDirectory(NStage); File.WriteAllBytes(t, b); Log("rput " + id);
                Res("ok " + want); return 0;
            }
            if (step == "rimport")
            {
                if (a.Length != 4) { Res("refused request"); return 17; }
                string want = a[1]; long size = long.Parse(a[2], CultureInfo.InvariantCulture);
                if (reg.TryGetValue(id, out var have) && (have as Dictionary<string, object>)?.S("sha256") == want && File.Exists(dest)) { Res("ok " + id + " " + want); return 0; }
                if (reg.ContainsKey(id)) { Res("failed refused an option with this id exists"); return 17; }
                if (!File.Exists(t) || ShaFile(t) != want || new FileInfo(t).Length != size) { Res("failed refused the uploaded image is not there"); return 17; }
                Out(stdout, "began rimport\n"); if (fail) { Res("failed registry"); return 17; }
                Directory.CreateDirectory(NRoots); File.Move(t, dest);
                reg[id] = new Dictionary<string, object> { { "kind", "owner-root" }, { "sha256", want }, { "bytes", size }, { "label", Encoding.ASCII.GetString(Convert.FromBase64String(a[3])) } };
                SaveRegistry(reg); Log("rimport " + id);
                Res("ok " + id + " " + want); return 0;
            }
            if (step == "rdrop")
            {
                string want = a[1];
                if (File.Exists(dest) && ShaFile(dest) != want) { Res("refused the image is not CCS Nucleus's own unchanged file with that SHA-256 (nothing was changed)"); return 17; }
                Out(stdout, "began rdrop\n"); if (fail) { Res("failed remove (not tried again)"); return 17; }
                reg.Remove(id); SaveRegistry(reg); if (File.Exists(dest)) File.Delete(dest); Log("rdrop " + id);
                Res("ok " + id); return 0;
            }
            Res("refused unknown step"); return 2;
        }

        /// <summary>an add-on on a simulated console against this image's package: none (not listed), current or earlier (console.json
        /// "addon_installed" names another package's SHA-256)</summary>
        internal static string AddonVersion(string folder, string name)
        {
            if (!(Packages(folder).A("packages") ?? new List<object>()).OfType<string>().Contains(name)) return "none";
            var c = Json.Parse(File.ReadAllText(Path.Combine(folder, "console.json"))); var have = c.O("addon_installed")?.S(name);
            return have == null || have == RecoveryImages.MethodSha(name) ? "current" : "earlier";
        }
        static string WaitingShaOrNull(string file) => File.Exists(file) ? WaitingSha(file) : null;
        /// <summary>tests only: record which package of an add-on a simulated console has installed (null: this image's)</summary>
        public static void SetAddonInstalled(string folder, string name, string sha256)
        {
            var f = Path.Combine(folder, "console.json"); var c = Json.Parse(File.ReadAllText(f)); var j = new JObj();
            foreach (var kv in c) if (kv.Key != "addon_installed") j.Add(kv.Key, kv.Value);
            var a = new JObj(); foreach (var kv in c.O("addon_installed") ?? new Dictionary<string, object>()) if (kv.Key != name) a.Add(kv.Key, kv.Value);
            if (sha256 != null) a.Add(name, sha256);
            j.Add("addon_installed", a); File.WriteAllText(f, Json.Write(j));
        }

        /// <summary>ccsprep nmadd / nmdrop on a simulated console: a community menu as a choice in CCS Nucleus's list (its registry; it needs a
        /// valid registry: CCS Nucleus installed or its start already written, and an installed CCS Nucleus of this image's own version)</summary>
        int MenuVerb(string step, string[] a, Stream stdout, bool fail)
        {
            void Res(string t) => Out(stdout, "result " + step + " " + t + "\n");
            if (step == "nmadd" ? a.Length != 4 : a.Length != 2) { Res("refused request"); return 17; }
            if (!Engine.FirmwareEngine.IdOk(a[0]) || !Engine.CommunityCatalog.PackageOk(a[1])) { Res("refused request"); return 17; }
            if (HakchiOnData() == "none") { Res("refused hakchi is not installed on the data storage"); return 17; }
            if (!NucleusInstalled() && StartOf(Folder) == "none") { Res("refused CCS Nucleus's list of choices is not on the console"); return 17; }
            if (AddonVersion(Folder, "ccs-nucleus") != "current" && WaitingShaOrNull(Path.Combine(TransferDir, "ccs-nucleus.hmod")) != RecoveryImages.MethodSha("ccs-nucleus")) { Res("refused CCS Nucleus on the console is an earlier version, which cannot list a community menu (and this version's is not waiting to be installed)"); return 17; }
            var reg = Registry(); reg.TryGetValue(a[0], out var hv); var have = hv as Dictionary<string, object>;
            if (have != null && have.S("kind") != "community") { Res("refused another kind of choice has this id"); return 17; }
            if (step == "nmadd")
            {
                if (have != null && have.S("package") == a[1] && have.S("program") == a[2] && have.S("label64") == a[3]) { Res("ok already " + a[0]); return 0; }
                Out(stdout, "began nmadd\n"); if (fail) { Res("failed registry (not written again)"); return 17; }
                reg[a[0]] = new Dictionary<string, object> { { "kind", "community" }, { "package", a[1] }, { "program", a[2] }, { "label64", a[3] } };
                SaveRegistry(reg); Log("nmadd " + a[0]); Res("ok " + a[0]); return 0;
            }
            if (have == null) { Res("ok already " + a[0]); return 0; }
            if (have.S("package") != a[1]) { Res("refused the choice names another package (nothing was changed)"); return 17; }
            Out(stdout, "began nmdrop\n"); if (fail) { Res("failed registry (not written again)"); return 17; }
            reg.Remove(a[0]); SaveRegistry(reg); Log("nmdrop " + a[0]); Res("ok " + a[0]); return 0;
        }

        /// <summary>tests only: the console's next normal start with hakchi running, as hakchi's p7080_hmods applies its transfer folder:
        /// packs_uninstall on the names in transfer/uninstall (CCS's own uninstall takes its method out of charge), then pack_install of every
        /// *.hmod there (CCS's postinstall puts its method in charge when none is), then the folder removed. What it did, in order.</summary>
        public static List<string> NormalStart(string folder)
        {
            var done = new List<string>(); var t = Path.Combine(folder, "hakchi-transfer");
            var c = Json.Parse(File.ReadAllText(Path.Combine(folder, "console.json")));
            if (c.L("active_hakchi_entries", 0) == 0 || !Directory.Exists(t)) return done;   // the original kernel never runs hakchi's preinit
            var pk = Packages(folder); var list = (pk.A("packages") ?? new List<object>()).OfType<string>().ToList();
            var un = Path.Combine(t, "uninstall");
            if (File.Exists(un)) foreach (var n in File.ReadAllText(un).Split(new[] { ' ', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries))
                    if (list.Remove(n))
                    {
                        done.Add("uninstall " + n); CommunityUninstall(folder, n);
                        // the chooser's own removal: the chooser off at start (CCS Nucleus's registry stays)
                        if (n == "ccs-nucleus-chooser" && StartOf(folder).Split(' ').Length == 5) { var p5 = StartOf(folder).Split(' '); p5[1] = "never"; SetStart(folder, string.Join(" ", p5)); }
                    }
            // hakchi installs transfer/*.hmod in sorted order, then transfer/hmod/*.hmod
            var hm = Path.Combine(t, "hmod");
            foreach (var h in Directory.GetFiles(t, "*.hmod").OrderBy(x => x, StringComparer.Ordinal).Concat(Directory.Exists(hm) ? Directory.GetFiles(hm, "*.hmod").OrderBy(x => x, StringComparer.Ordinal) : Enumerable.Empty<string>()))
            {
                var n = Path.GetFileNameWithoutExtension(h);
                if (n == "ccs-nucleus-chooser" && !list.Contains("ccs-nucleus")) { done.Add("install " + n + " refused (CCS Nucleus is not installed)"); continue; }
                bool installs = true; CommunityInstall(folder, n, h, ref installs); if (!installs) { done.Add("install " + n + " not installed"); continue; }
                if (!list.Contains(n)) list.Add(n); done.Add("install " + n);
                if (Engine.Methods.IsPackage(n)) SetAddonInstalled(folder, n, WaitingSha(h) == RecoveryImages.MethodSha(n) ? null : WaitingSha(h));
                // CCS Nucleus's install keeps a valid registry, or writes the first one (Original Menu directly; CCS HOME offered with CCS Mod)
                if (n == "ccs-nucleus" && StartOf(folder) == "none") SetStart(folder, "native never " + (list.Contains("ccs-mod") ? "on" : "off") + " default 10");
            }
            SavePackages(folder, list); Directory.Delete(t, true); done.Add("transfer folder removed; restart");
            return done;
        }

        int PrepVerb(string step, string[] args, Stream stdin, Stream stdout)
        {
            var f = Fault()?.O("prep"); bool fail = f != null && f.S("step") == step;
            void Res(string t) => Out(stdout, "result " + step + " " + t + "\n");
            string arg = args.Length > 0 ? args[0] : null;
            bool Md5Arg() => arg != null && Regex.IsMatch(arg, "^[0-9a-f]{32}$");
            var staged = Path.Combine(Folder, "ram-ret-boot1.bin");
            switch (step)
            {
                case "mstate":
                    {
                        var sb = new StringBuilder(); sb.Append("hakchi=" + HakchiOnData() + "\n");
                        var pk = Packages(Folder);
                        foreach (var n in (pk.A("packages") ?? new List<object>()).OfType<string>().OrderBy(x => x, StringComparer.Ordinal)) sb.Append("package=" + n + "\n");
                        sb.Append("start=" + StartOf(Folder) + "\n");
                        {   // the name of what opens first (its registry option's label), as ccsprep reports it
                            var w = StartOf(Folder).Split(' ');
                            if (w.Length == 5)
                            {
                                Registry().TryGetValue(w[0], out var dv); var dd = dv as Dictionary<string, object>;
                                string lab = dd?.S("label"); if (lab == null && dd?.S("label64") != null) try { lab = Encoding.UTF8.GetString(Convert.FromBase64String(dd.S("label64"))); } catch (FormatException) { }
                                lab = lab ?? (w[0] == "native" ? "Original Menu" : w[0] == "ccs-home" ? "CCS HOME" : "");
                                sb.Append("start_label=" + Convert.ToBase64String(Encoding.UTF8.GetBytes(lab)) + "\n");
                            }
                        }
                        sb.Append("free=" + FreeBytes().ToString(CultureInfo.InvariantCulture) + "\n");
                        if (NucleusInstalled() || StartOf(Folder) != "none")
                        {
                            sb.Append("registry=ok\n");
                            foreach (var kv in Registry().OrderBy(x => x.Key, StringComparer.Ordinal)) { var o = kv.Value as Dictionary<string, object>; sb.Append("option=" + kv.Key + " " + o.S("kind") + " " + (o.S("sha256") ?? "") + " " + (o.ContainsKey("bytes") ? o.L("bytes", 0).ToString(CultureInfo.InvariantCulture) : "") + "\n"); }
                            foreach (var kv in Registry().Where(x => (x.Value as Dictionary<string, object>)?.S("kind") == "community").OrderBy(x => x.Key, StringComparer.Ordinal)) { var o = kv.Value as Dictionary<string, object>; sb.Append("menu=" + kv.Key + " " + o.S("package") + " " + o.S("program") + " " + o.S("label64") + "\n"); }
                        }
                        else sb.Append("registry=none\n");
                        // each add-on installed against this image's own package (console.json "addon_installed": {NAME: SHA-256 of the package
                        // installed}; none recorded: this image's)
                        foreach (var n in Engine.Methods.Packages) sb.Append("addon=" + n + " " + AddonVersion(Folder, n) + "\n");
                        if (Directory.Exists(TransferDir)) foreach (var w in Directory.GetFiles(TransferDir).OrderBy(x => x, StringComparer.Ordinal)) sb.Append("waiting=" + Path.GetFileName(w) + " " + WaitingSha(w) + "\n");
                        var thm = Path.Combine(TransferDir, "hmod");
                        if (Directory.Exists(thm)) foreach (var w in Directory.GetFiles(thm).OrderBy(x => x, StringComparer.Ordinal)) sb.Append("waiting=hmod/" + Path.GetFileName(w) + " " + WaitingSha(w) + "\n");
                        sb.Append(HomeState());
                        Out(stdout, sb.ToString()); Res("ok"); return 0;
                    }
                case "rput": case "rimport": case "rdrop":
                    return RootVerb(step, args, stdin, stdout, fail);
                case "nmadd": case "nmdrop":
                    return MenuVerb(step, args, stdout, fail);
                case "nstart":
                    {
                        if (args.Length != 5 || !Regex.IsMatch(args[0], "^[a-z0-9][a-z0-9-]{0,30}$") || args[1] != "always" && args[1] != "never" || args[2] != "on" && args[2] != "off"
                            || args[3] != "default" && args[3] != "last" || !Regex.IsMatch(args[4], "^[0-9]{1,2}$")) { Res("refused request"); return 17; }
                        if (HakchiOnData() == "none") { Res("refused hakchi is not installed on the data storage"); return 17; }
                        var line = string.Join(" ", args);
                        if (StartOf(Folder) == line) { Res("ok already " + line); return 0; }
                        // another option as what opens first: only one the registry lists, an owner's system image only with the chooser at start
                        // (refused before anything is written, as ccsprep does)
                        if (args[0] != "native" && args[0] != "ccs-home")
                        {
                            if (StartOf(Folder).Split(' ').Length != 5 || !Registry().TryGetValue(args[0], out var dopt)) { Res("refused the default is not one of CCS Nucleus's choices"); return 17; }
                            if ((dopt as Dictionary<string, object>)?.S("kind") == "owner-root" && args[1] != "always") { Res("refused an owner's system image opens first only with the chooser at start"); return 17; }
                        }
                        Out(stdout, "began nstart\n"); if (fail) { Res("failed registry (not written again)"); return 17; }
                        if (args[0] == "ccs-home" && args[2] == "off") { Res("failed CCS HOME is not offered (not written again)"); return 17; }
                        SetStart(Folder, line); Log("nstart " + line);
                        Res("ok " + line); return 0;
                    }
                case "hput": case "huse": case "hdrop":
                    return HomeVerb(step, args, stdin, stdout, fail);
                case "method":
                case "unlist":
                    {
                        string name = arg, want = step == "method" ? (args.Length > 1 ? args[1] : null) : null;
                        if (step == "unlist" ? !(args.Length >= 1 && args.Length <= 3 && args.All(Engine.Methods.IsPackage)) : !Engine.Methods.IsPackage(name) || want == null || !Regex.IsMatch(want, "^[0-9a-f]{64}$")) { Res("refused request"); return 17; }
                        if (step == "method" && RecoveryImages.MethodSha(name) != want) { Res("refused this image does not carry that package"); return 17; }
                        if (step == "unlist") { want = Engine.Methods.ListSha256(args); name = string.Join(" ", args); }
                        if (HakchiOnData() == "none") { Res("refused hakchi is not installed on the data storage"); return 17; }
                        var t = Path.Combine(TransferDir, step == "method" ? Engine.Methods.Waiting(name).Replace('/', Path.DirectorySeparatorChar) : "uninstall");
                        if (File.Exists(t)) { if (WaitingSha(t) == want) { Res("ok already " + want); return 0; } Res("refused another " + (step == "method" ? name + ".hmod" : "removal list") + " already waits in the transfer folder"); return 17; }
                        Out(stdout, "began " + step + "\n"); if (fail) { Res("failed write (not written again)"); return 17; }
                        Directory.CreateDirectory(Path.GetDirectoryName(t));
                        File.WriteAllText(t, step == "method" ? "simulated " + name + ".hmod " + want + "\n" : name + "\n");
                        Log(step + " " + name); var back = WaitingSha(t);
                        if (back != want) { Res("failed readback " + back); return 17; }
                        Res("ok " + want); return 0;
                    }
                case "files":
                    Out(stdout, "began files\n"); if (fail) { Res("failed mod_install"); return 17; } File.WriteAllText(Path.Combine(Folder, "prepared-files.txt"), "hakchi v1.0.4-126 files installed (simulated)\n");
                    if (Cfg().S("parttable_before_tune") == null) { var t0 = Cfg().S("parttable"); SetCfg("parttable_before_tune", t0); SetCfg("parttable", Tuned(t0)); SetCfg("table_changed", true); }
                    Log("prep files"); Res("ok"); return 0;
                case "uboot":
                    Out(stdout, "began uboot\n"); if (fail) { Res("failed burn-or-readback"); return 17; }
                    if (!File.Exists(D("boot1.factory"))) File.Copy(D("boot1"), D("boot1.factory"));
                    File.WriteAllBytes(D("boot1"), SimHakchiUboot); Log("prep uboot"); Res("ok " + Md5(SimHakchiUboot)); return 0;
                case "ret-kernel":
                    if (!Md5Arg()) { Res("refused md5"); return 17; }
                    if (Cfg().L("active_hakchi_entries", 0) == 0) { Res("ok already " + Md5File(D("boot2"))); return 0; }
                    if (!File.Exists(D("boot2-68"))) { Res("failed the spare slot holds no original kernel"); return 17; }
                    if (Md5File(D("boot2-68")) != arg) { Res("refused the spare slot changed since the review"); return 17; }
                    Out(stdout, "began ret-kernel\n"); if (fail) { Res("failed burn-or-readback"); return 17; }
                    File.Copy(D("boot2-68"), D("boot2"), true); SetCfg("active_hakchi_entries", 0L); Log("ret kernel"); Res("ok " + Md5File(D("boot2"))); return 0;
                case "ret-stage":
                    {
                        if (args.Length != 2 || !Regex.IsMatch(args[0], "^[1-9][0-9]{0,6}$") || !Regex.IsMatch(args[1], "^[0-9a-f]{32}$")) { Res("refused request"); return 17; }
                        int size = int.Parse(args[0], CultureInfo.InvariantCulture); var got = new MemoryStream(); var buf = new byte[1 << 16]; int n;
                        while (got.Length < size && stdin != null && (n = stdin.Read(buf, 0, (int)Math.Min(buf.Length, size - got.Length))) > 0) got.Write(buf, 0, n);
                        var bytes = got.ToArray(); if (File.Exists(staged)) File.Delete(staged);
                        if (Fault()?.TryGetValue("ret_stage_corrupt", out var sc) == true && sc is bool scb && scb && bytes.Length > 0) bytes[bytes.Length / 3] ^= 0x01;
                        if (bytes.Length != size || Md5(bytes) != args[1]) { Res("refused received-mismatch " + Md5(bytes) + " " + bytes.Length); return 17; }
                        if (BootImage.Check(bytes, false) != null) { Res("refused not a complete u-boot image"); return 17; }
                        File.WriteAllBytes(staged, bytes); Res("ok " + args[1] + " " + args[0]); return 0;
                    }
                case "ret-uboot":
                    if (args.Length != 2 || !Regex.IsMatch(args[0], "^[0-9a-f]{32}$") || !Regex.IsMatch(args[1], "^[0-9a-f]{32}$")) { Res("refused md5"); return 17; }
                    if (!File.Exists(staged) || Md5File(staged) != args[1]) { Res("refused the staged bootloader is not the reviewed one"); return 17; }
                    if (Md5File(D("boot1")) != args[0]) { Res("refused the bootloader changed since the review"); return 17; }
                    Out(stdout, "began ret-uboot\n"); if (fail) { Res("failed burn (not burned again)"); return 17; }
                    {
                        var w = File.ReadAllBytes(staged); var bad = Fault()?.TryGetValue("uboot_readback_differs", out var dv) == true && dv is bool db && db;
                        if (bad) w[w.Length / 2] ^= 0x5A;
                        File.WriteAllBytes(D("boot1"), w); Log("ret uboot");
                        var back = Md5File(D("boot1")); if (back != args[1]) { Res("failed readback " + back + " " + back + " (not burned again)"); return 17; }
                        Res("ok " + back); return 0;
                    }
                case "ret-files":
                    Out(stdout, "began ret-files\n"); if (fail) { Res("failed mount"); return 17; }
                    File.Delete(Path.Combine(Folder, "prepared-files.txt"));   // hakchi's whole folder: its transfer folder and package lists too
                    if (Directory.Exists(TransferDir)) Directory.Delete(TransferDir, true);
                    { var pk = Packages(Folder); if (pk.Count > 0) SavePackages(Folder, new List<string>()); }
                    Log("ret files"); Res("ok"); return 0;
                case "ret-table":
                    Out(stdout, "began ret-table\n"); if (fail) { Res("failed shrink"); return 17; }
                    var before = Cfg().S("parttable_before_tune"); if (before != null) { SetCfg("parttable", before); SetCfg("parttable_before_tune", null); SetCfg("table_changed", true); }
                    Log("ret table"); Res("ok"); return 0;
                case "slot68":
                    if (File.Exists(D("boot2-68"))) { Res("ok already " + Md5File(D("boot2-68"))); return 0; }
                    if (Cfg().L("active_hakchi_entries", 0) != 0) { Res("failed the running kernel is not the original"); return 17; }
                    Out(stdout, "began slot68\n"); if (fail) { Res("failed burn-or-readback"); return 17; }
                    File.Copy(D("boot2"), D("boot2-68")); Log("prep slot68"); Res("ok " + Md5File(D("boot2-68"))); return 0;
                case "kernel":
                    if (!File.Exists(D("boot2-68"))) { Res("failed the original kernel is not in the spare slot"); return 17; }
                    Out(stdout, "began kernel\n"); if (fail) { Res("failed burn-or-readback"); return 17; }
                    var k = new MemoryStream(); var hdr = new byte[2048]; Encoding.ASCII.GetBytes("ANDROID!").CopyTo(hdr, 0); BitConverter.GetBytes(4096u).CopyTo(hdr, 8); BitConverter.GetBytes(2048u).CopyTo(hdr, 36); k.Write(hdr, 0, hdr.Length); Pattern(k, 4096, "hakchi-kernel");
                    File.WriteAllBytes(D("boot2"), k.ToArray());
                    var c = Cfg(); var j = new JObj(); foreach (var kv in c) j.Add(kv.Key, kv.Value); j.Set("active_hakchi_entries", 3); File.WriteAllText(Path.Combine(Folder, "console.json"), Json.Write(j));
                    Log("prep kernel"); Res("ok " + Md5File(D("boot2"))); return 0;
            }
            Out(stdout, "ccsprep: unknown step\n"); return 2;
        }

        static byte[] Flip(byte[] b) { var c = (byte[])b.Clone(); if (c.Length > 0) c[c.Length / 2] ^= 0xFF; return c; }
        static byte[] ReadPrefix(string p, long n) { using (var f = File.OpenRead(p)) { var b = new byte[n]; int got = 0; while (got < n) { int r = f.Read(b, got, (int)Math.Min(1 << 20, n - got)); if (r <= 0) break; got += r; } return b; } }
        void Patch(string t, byte[] bytes)
        {
            if (t == "boot2" || t == "boot2-68") { File.WriteAllBytes(D(t), bytes); return; }
            using (var f = new FileStream(D(t), FileMode.Open, FileAccess.Write)) { f.Write(bytes, 0, bytes.Length); f.Flush(true); }
        }

        // ---------------------------------------------------------------- building a simulated console (tests, demonstrations)
        /// <summary>Creates a simulated console in an empty folder. parts: device -> (label, bytes). files: target -> content producer
        /// (boot0, boot1, boot2, boot2-68, nand, phy and the partitions; a missing partition is filled with a deterministic pattern).</summary>
        public static void Create(string folder, string hwid, IList<(string dev, string label, long bytes)> parts, IDictionary<string, Action<Stream>> files, int hakchiEntries = 0, string region = "EUR_USA", string oem = "release-v1.0.7-0-g4ea4041", string board = "dp-nes", string nandinfo = Support.NandInfo)
        {
            if (Directory.Exists(folder) && Directory.EnumerateFileSystemEntries(folder).Any()) throw new IOException("the simulated console folder must be new");
            Directory.CreateDirectory(Path.Combine(folder, "dev"));
            var table = "mbr:mbr:32 " + string.Join(" ", parts.Select(p => p.dev + ":" + p.label + ":" + (p.bytes / 512).ToString(CultureInfo.InvariantCulture)));
            File.WriteAllText(Path.Combine(folder, "console.json"), Json.Write(new JObj { { "hwid", hwid }, { "board", board }, { "region", region }, { "oem_version", oem }, { "nandinfo", nandinfo }, { "parttable", table }, { "active_hakchi_entries", hakchiEntries } }));
            var all = new List<string> { "boot0", "boot1", "boot2", "nand", "phy" }; all.AddRange(parts.Select(p => p.dev));
            if (files.ContainsKey("boot2-68")) all.Add("boot2-68");
            foreach (var t in all)
            {
                long want = t == "phy" ? Support.PhysicalBytes : t == "nand" ? 16 * 1024 * 1024 : parts.Where(p => p.dev == t).Select(p => p.bytes).FirstOrDefault();
                using (var f = new FileStream(Dev(folder, t), FileMode.CreateNew, FileAccess.Write))
                {
                    if (files.TryGetValue(t, out var make)) make(f);
                    else Pattern(f, want, t);
                    if (want > 0 && f.Length < want) f.SetLength(want);
                    if (want > 0 && f.Length != want) throw new IOException(t + " has " + f.Length + " bytes; " + want + " expected");
                }
            }
            File.WriteAllText(Dev(folder, "parttable"), string.Join("\n", table.Split(' ')) + "\n");
        }

        /// <summary>a deterministic, non-repeating fill (so a swapped or shifted block is detected)</summary>
        public static void Pattern(Stream f, long n, string seed)
        {
            var b = new byte[1 << 16]; uint x = 2166136261; foreach (var ch in seed) x = (x ^ ch) * 16777619;
            for (long done = 0; done < n;)
            {
                int c = (int)Math.Min(b.Length, n - done);
                for (int i = 0; i < c; i += 4) { x ^= x << 13; x ^= x >> 17; x ^= x << 5; b[i] = (byte)x; if (i + 1 < c) b[i + 1] = (byte)(x >> 8); if (i + 2 < c) b[i + 2] = (byte)(x >> 16); if (i + 3 < c) b[i + 3] = (byte)(x >> 24); }
                f.Write(b, 0, c); done += c;
            }
        }
    }
}
