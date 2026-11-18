// Classic Cart Studio console backup and restore engine tests: the Backup Browser inside startup images (their parts and ramdisk files)
// and the console's encrypted system part, decrypted on this PC with the key a startup image in the same backup carries.
// GPL-3.0-or-later. Simulated consoles only (files on this PC); nothing opens a console session of any kind.
//   browser-system   synthetic: a random key made for each run (no key is a fixture). The encrypted system part is made by Python's
//                    cryptography package (OpenSSL's AES-XTS, 512-byte sectors, the "plain" IV), the ramdisks' cpio archives by Python,
//                    their LZO by liblzo2 (LZO1X-999 and LZO1X-1) in lzop framing, gzip and xz by Python's own modules, the system by
//                    squashfs-tools; expected listings from Python and unsquashfs, never from the engine's own readers. Positive cases,
//                    wrong/absent/short/unusable keys, unsupported and damaged ramdisks, malformed boot headers, a damaged system, a
//                    damaged key source, a changed system part and a damaged ZIP index; the key never in any record or export list.
//   browser-owner-system  private, only with --owner-system DIR (the derived encrypted system part assembled from the owner's own physical
//                    NAND dump) and --owner-decrypted FILE (the owner's recorded decrypted copy) and --kernel (the owner's original kernel):
//                    the real key from the original kernel's LZO ramdisk, the real encrypted system part decrypted and compared with the
//                    recorded copy, every file and the listing (unsquashfs). Prints counts and public hashes only; never key bytes or
//                    file contents. SKIPPED without those files.
using CcsBackup.Archive;
using CcsBackup.Browse;
using CcsBackup.Device;
using CcsBackup.Engine;
using CcsBackup.Library;
using CcsConsole.RamBoot;
using RamdiskFiles = CcsConsole.RamBoot.Ramdisk;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using static CcsBackup.Tests.Program;

namespace CcsBackup.Tests
{
    static partial class Tests
    {
        internal static string OwnerSystemDir, OwnerDecrypted;

        const string StartupFixturePy = @"
import sys, os, json, struct, zlib, gzip, lzma, hashlib, ctypes, ctypes.util
from cryptography.hazmat.primitives.ciphers import Cipher, algorithms, modes
d, part = sys.argv[1], int(sys.argv[2])
def rd(n): return open(os.path.join(d, n), 'rb').read()
def wr(n, b): open(os.path.join(d, n), 'wb').write(b)
key, wrong = rd('key.bin'), rd('wrong.bin')
def xts(img, k):
    img = img + bytes(part - len(img)); out = bytearray()
    for s in range(part // 512):
        e = Cipher(algorithms.AES(k), modes.XTS(struct.pack('<I', s) + bytes(12))).encryptor()
        out += e.update(img[s * 512:(s + 1) * 512]) + e.finalize()
    return bytes(out)
wr('nandb-cipher.img', xts(rd('sys.hsqs'), key))
wr('nandb-damaged-cipher.img', xts(rd('sys-damaged.hsqs'), key))
big = b''.join(hashlib.sha256(b'stand-in %d' % i).digest() * (1 + i % 7) for i in range(3000))
def newc(kb):
    o = bytearray(); listing = []; ino = [300]
    def put(name, mode, data, rdev=(0, 0), mtime=1500000000, uid=0, gid=0):
        nm = name.encode() + b'\0'
        h = '070701' + ''.join('%08X' % x for x in (ino[0], mode, uid, gid, 1, mtime, len(data), 0, 0, rdev[0], rdev[1], len(nm), 0))
        ino[0] += 1
        o.extend(h.encode() + nm)
        while len(o) % 4: o.append(0)
        o.extend(data)
        while len(o) % 4: o.append(0)
        if name not in ('.', 'TRAILER!!!'):
            t = {0o040000: 'd', 0o100000: 'f', 0o120000: 'l', 0o020000: 'c'}[mode & 0o170000]
            listing.append({'path': '/' + name, 'type': t, 'mode': mode & 0o7777, 'uid': uid, 'gid': gid, 'mtime': mtime, 'size': len(data) if t in 'fl' else 0,
                            'sha256': hashlib.sha256(data).hexdigest() if t == 'f' else None, 'target': data.decode() if t == 'l' else None})
    put('.', 0o040755, b'')
    put('bin', 0o040755, b'', uid=1337, gid=1337)
    put('bin/busybox', 0o100755, big, uid=1337, gid=1337)
    put('bin/sh', 0o120777, b'busybox')
    put('dev', 0o040755, b'')
    put('dev/console', 0o020600, b'', rdev=(5, 1))
    put('etc/clover/REGION', 0o100644, b'USA\n', mtime=1480000000)
    put('init', 0o100755, b'#!/bin/sh\nexec /sbin/init\n')
    if kb is not None: put('key-file', 0o100400, kb)
    put('sbin/init', 0o100755, b'#!/bin/sh\ncryptsetup open --type plain --cipher aes-xts-plain --key-file /key-file\n', mtime=0)
    put('TRAILER!!!', 0, b'', mtime=0)
    return bytes(o), listing
lzo = ctypes.CDLL(ctypes.util.find_library('lzo2'))
def lzo_block(b, best):
    f = lzo.lzo1x_999_compress if best else lzo.lzo1x_1_compress
    f.argtypes = [ctypes.c_char_p, ctypes.c_size_t, ctypes.c_void_p, ctypes.POINTER(ctypes.c_size_t), ctypes.c_void_p]
    dst = ctypes.create_string_buffer(len(b) + len(b) // 16 + 64 + 3); n = ctypes.c_size_t(len(dst)); wrk = ctypes.create_string_buffer(1 << 20)
    assert f(b, len(b), dst, ctypes.byref(n), wrk) == 0
    return dst.raw[:n.value]
def lzop(data, best, crc=False):
    flags = 0x03000001 | (0x100 if crc else 0)
    h = struct.pack('>HHHBBI', 0x1030, 0x2080, 0x0940, 3 if best else 1, 9 if best else 1, flags) + struct.pack('>III', 0o100644, 1500000000, 0) + bytes([0])
    o = bytearray(b'\x89LZO\x00\r\n\x1a\n') + h + struct.pack('>I', zlib.adler32(h))
    for i in range(0, len(data), 256 * 1024):
        blk = data[i:i + 256 * 1024]; c = lzo_block(blk, best)
        if len(c) >= len(blk): c = blk
        o += struct.pack('>II', len(blk), len(c)) + struct.pack('>I', zlib.adler32(blk)) + (struct.pack('>I', zlib.crc32(blk)) if crc else b'') + c
    return bytes(o + struct.pack('>I', 0))
right, right_list = newc(key)
wr('rd-plain.cpio', right)
wr('rd-lzo-best.lzo', lzop(right, True))
wr('rd-lzo-fast.lzo', lzop(right, False, True))
wr('rd-gzip.gz', gzip.compress(right, mtime=0))
wr('rd-xz.xz', lzma.compress(right, format=lzma.FORMAT_XZ))
nokey, nokey_list = newc(None)
wr('rd-nokey.lzo', lzop(nokey, True)); wr('rd-nokey.gz', gzip.compress(nokey, mtime=0))
wr('rd-short.lzo', lzop(newc(key[:16])[0], True))
wrongc, wrong_list = newc(wrong)
wr('rd-wrong.lzo', lzop(wrongc, True))
bad = bytearray(lzop(right, True)); bad[len(bad) // 2] ^= 0x55; wr('rd-bad.lzo', bytes(bad))
json.dump({'listing': right_list, 'nokey': nokey_list, 'wrong': wrong_list}, open(os.path.join(d, 'expected.json'), 'w'))
print('fixtures written')
";

        /// <summary>a small console with the given startup images and system part (the 64 MB data fixture, hakchi's three entries)</summary>
        static string SysConsole(string name, byte[] active, byte[] slot68, byte[] nandb, (string, string, long)[] layout = null, string data = null, int entries = 3)
        {
            var dir = NewDir("console-" + name); var files = new Dictionary<string, Action<Stream>>();
            files["boot2"] = st => st.Write(active, 0, active.Length);
            if (slot68 != null) files["boot2-68"] = st => st.Write(slot68, 0, slot68.Length);
            files["nandb"] = st => st.Write(nandb, 0, nandb.Length);
            files["nandc"] = st => { using (var f = File.OpenRead(data ?? Path.Combine(Fx, "data-modern.img"))) f.CopyTo(st); };
            files["phy"] = st => { SimulatedConsole.Pattern(st, Support.PhysicalBytes, "phy"); st.Position = 0x30 * 0x20000; st.Write(active, 0, active.Length); if (slot68 != null) { st.Position = 0x68 * 0x20000; st.Write(slot68, 0, slot68.Length); } st.Position = st.Length; };
            files["boot0"] = st => { var b = BootImage.Synthetic(true, 24576, "boot0"); st.Write(b, 0, b.Length); };
            files["boot1"] = st => { var b = BootImage.Synthetic(false, 757760, "uboot", 4096); st.Write(b, 0, b.Length); };
            SimulatedConsole.Create(Path.Combine(dir, "c"), HwidA, layout ?? SmallLayout, files, entries);
            return Path.Combine(dir, "c");
        }

        /// <summary>every file the engine wrote beside the backups (its records) and every export list: none may hold the key in any form</summary>
        static List<string> KeyLeaks(byte[] key, params string[] roots)
        {
            var forms = new List<byte[]> { key, Encoding.ASCII.GetBytes(Hex.Of(key)), Encoding.ASCII.GetBytes(Hex.Of(key).ToUpperInvariant()), Encoding.ASCII.GetBytes(Convert.ToBase64String(key)) };
            var leaks = new List<string>();
            foreach (var root in roots.Where(Directory.Exists))
                foreach (var f in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).Where(p => !p.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) && !Path.GetFileName(p).Equals("key-file", StringComparison.Ordinal)))
                {
                    var b = File.ReadAllBytes(f);
                    foreach (var k in forms) for (int i = 0; i + k.Length <= b.Length; i++) { int j = 0; while (j < k.Length && b[i + j] == k[j]) j++; if (j == k.Length) { leaks.Add(f); goto next; } }
                    next:;
                }
            return leaks;
        }

        static Dictionary<string, Dictionary<string, object>> ExpectedRamdisk(Dictionary<string, object> doc, string which) =>
            (doc.A(which) ?? new List<object>()).OfType<Dictionary<string, object>>().ToDictionary(x => x.S("path"), StringComparer.Ordinal);

        /// <summary>the ramdisk's files under its part match Python's own listing (path, kind, permissions, owner, date, size, link target) and
        /// every file's worked-out checksum matches Python's</summary>
        static bool RamdiskMatches(BackupContents c, BrowseEntry rpart, Dictionary<string, Dictionary<string, object>> want, out string why)
        {
            var got = c.Entries.Where(e => e.Container == rpart).ToDictionary(e => e.InnerPath, StringComparer.Ordinal);
            var implied = new[] { "/etc", "/etc/clover", "/sbin" };
            why = null;
            if (!want.Keys.All(got.ContainsKey) || got.Keys.Except(want.Keys).Except(implied).Any()) { why = "paths differ: " + string.Join(",", got.Keys.Except(want.Keys)) + " / " + string.Join(",", want.Keys.Except(got.Keys)); return false; }
            if (!implied.All(p => got[p].Kind == EntryKind.Folder)) { why = "the implied folders are not folders"; return false; }
            foreach (var kv in want)
            {
                var e = got[kv.Key]; var w = kv.Value;
                if (e.TypeCode != w.S("type")[0] || e.Mode != (int)w.L("mode") || e.Uid != (int)w.L("uid") || e.Gid != (int)w.L("gid") || e.MtimeSeconds != w.L("mtime") || (e.Kind == EntryKind.File || e.Kind == EntryKind.Link ? e.Size : 0) != w.L("size") || e.LinkTarget != w.S("target"))
                { why = kv.Key + " metadata differs"; return false; }
                if (e.Kind == EntryKind.File && c.ExpectedHash(e) != w.S("sha256")) { why = kv.Key + " checksum differs"; return false; }
            }
            return true;
        }

        public static void BrowserSystem()
        {
            var dir = NewDir("browser-system"); var s = SettingsIn(dir, false); var exp = Path.Combine(dir, "export"); Directory.CreateDirectory(exp);
            // ---------------------------------------------------------------- the fixtures (a random key for this run only)
            var key = new byte[32]; var wrong = new byte[32]; using (var r = RandomNumberGenerator.Create()) { r.GetBytes(key); r.GetBytes(wrong); }
            File.WriteAllBytes(Path.Combine(dir, "key.bin"), key); File.WriteAllBytes(Path.Combine(dir, "wrong.bin"), wrong);
            var sys = NestedImage(dir, "sys", "gzip", NesRoot("dp-nes", "USA", "sys"), true);
            var dmg = NestedImage(dir, "sysx", "xz", NesRoot("dp-nes", "USA", "sysx"), false);
            var damaged = (byte[])dmg.Bytes.Clone(); damaged[(int)new SquashFsReader(new MemoryStream(dmg.Bytes)).InodeTable + 20] ^= 0x40;   // xz: its block check makes the damage certain to be found
            File.WriteAllBytes(Path.Combine(dir, "sys-damaged.hsqs"), damaged);
            const long Part = 4096L * 1024;   // SmallLayout's system part
            File.WriteAllText(Path.Combine(dir, "startup_fixtures.py"), StartupFixturePy.Replace("\r\n", "\n"));
            var made = RunWsl(dir, "startup-fixtures", "python3 '" + W(Path.Combine(dir, "startup_fixtures.py")) + "' '" + W(dir) + "' " + Part);
            Check(made.Contains("fixtures written"), "the synthetic fixtures are made by independent tools: OpenSSL's AES-XTS (Python cryptography), liblzo2, Python's gzip and xz, squashfs-tools");
            byte[] F(string n) => File.ReadAllBytes(Path.Combine(dir, n));
            var expected = Json.Parse(File.ReadAllText(Path.Combine(dir, "expected.json")));
            var cipher = F("nandb-cipher.img"); var padded = sys.Bytes.Concat(new byte[Part - sys.Bytes.Length]).ToArray();

            // ---------------------------------------------------------------- the readers on their own (known answers, independent producers)
            bool Vec(string k1, string k2, ulong unit, string pt, string ct)
            {
                byte[] H(string h) => Enumerable.Range(0, h.Length / 2).Select(i => Convert.ToByte(h.Substring(2 * i, 2), 16)).ToArray();
                var kk = H(k1 + k2); var (d, t) = XtsView.Ciphers(kk, true); var b = H(pt); XtsView.Transform(d, t, b, 0, b.Length, unit);
                var (dd, tt) = XtsView.Ciphers(kk, false); var back = (byte[])b.Clone(); XtsView.Transform(dd, tt, back, 0, back.Length, unit);
                return Hex.Of(b) == ct && Hex.Of(back) == pt;
            }
            Check(Vec(new string('0', 32), new string('0', 32), 0, new string('0', 64), "917cf69ebd68b2ec9b9fe9a3eadda692cd43d2f59598ed858c02c2652fbf922e")
                  && Vec(string.Concat(Enumerable.Repeat("11", 16)), string.Concat(Enumerable.Repeat("22", 16)), 0x3333333333, string.Concat(Enumerable.Repeat("44", 32)), "c454185e6a16936e39334038acef838bfb186fff7480adc4289382ecd6d394f0")
                  && Vec("fffefdfcfbfaf9f8f7f6f5f4f3f2f1f0", string.Concat(Enumerable.Repeat("22", 16)), 0x3333333333, string.Concat(Enumerable.Repeat("44", 32)), "af85336b597afc1a900b2eb21ec949d292df4c047e0b21532186a5971a227a89"),
                "XTS-AES-128: the IEEE 1619 known answers (vectors 1 to 3), both ways");
            byte[] Dec(byte[] c0, byte[] k) { using (var v = new XtsView(new MemoryStream(c0, false), k)) { var o = new byte[c0.Length]; int got = 0; while (got < o.Length) got += v.Read(o, got, Math.Min(70000, o.Length - got)); return o; } }
            Check(Dec(cipher, key).SequenceEqual(padded), "the read-only decrypted view reads OpenSSL's aes-xts-plain encryption back to the exact system image (" + Engine.Units.Gb(Part) + ", 512-byte sectors, the plain IV)");
            string WrongWhy() { try { new SquashFsReader(new XtsView(new MemoryStream(cipher, false), wrong)); return null; } catch (InvalidDataException ex) { return ex.Message; } }
            Check(WrongWhy() == "not a SquashFS file system", "with a wrong key it decrypts to bytes that are not a file system (" + WrongWhy() + ")");
            var plainCpio = F("rd-plain.cpio");
            Check(Lzo.DecodeLzop(F("rd-lzo-best.lzo"), 64000000).SequenceEqual(plainCpio) && Lzo.DecodeLzop(F("rd-lzo-fast.lzo"), 64000000).SequenceEqual(plainCpio),
                "LZO: liblzo2's LZO1X-999 (one block) and LZO1X-1 (two blocks, with CRC-32 as well as Adler-32) in lzop framing decode to the exact archive (" + plainCpio.Length + " bytes)");
            string Throws(Func<object> f) { try { f(); return null; } catch (Exception ex) when (ex is InvalidDataException || ex is NotSupportedException) { return ex.GetType().Name + ": " + ex.Message; } }
            var badWhy = Throws(() => RamdiskFiles.Decode(F("rd-bad.lzo")));
            Check(badWhy != null && badWhy.StartsWith("InvalidDataException"), "a damaged LZO ramdisk is refused, never decoded into something else (" + badWhy + ")");
            var cut = F("rd-lzo-best.lzo").Take(120).ToArray(); var cutWhy = Throws(() => Lzo.DecodeLzop(cut, 64000000));
            var bigWhy = Throws(() => Lzo.DecodeLzop(F("rd-lzo-best.lzo"), 1000));
            Check(cutWhy != null && cutWhy.Contains("ends inside") && bigWhy != null && bigWhy.Contains("more than 1000 bytes"), "a cut-off lzop file and one larger than the limit are refused (" + cutWhy + "; " + bigWhy + ")");
            var xzWhy = Throws(() => RamdiskFiles.Decode(F("rd-xz.xz")));
            Check(xzWhy != null && xzWhy.StartsWith("NotSupportedException") && xzWhy.Contains("xz-compressed, which this version does not decode"), "an xz ramdisk is named, not decoded (" + xzWhy + ")");
            var cpioCut = Throws(() => RamdiskFiles.Decode(plainCpio.Take(plainCpio.Length / 3).ToArray()));
            Check(cpioCut != null && cpioCut.StartsWith("InvalidDataException"), "a cut-off cpio archive is refused (" + cpioCut + ")");
            var gz = RamdiskFiles.Decode(F("rd-gzip.gz")); var pl = RamdiskFiles.Decode(plainCpio); var lz = RamdiskFiles.Decode(F("rd-lzo-fast.lzo"));
            Check(gz.Entries.Select(e => e.Path).SequenceEqual(pl.Entries.Select(e => e.Path)) && lz.Entries.Select(e => e.Path).SequenceEqual(pl.Entries.Select(e => e.Path)) && pl.Entries.Count == ExpectedRamdisk(expected, "listing").Count,
                "gzip, LZO and an uncompressed archive list the same " + pl.Entries.Count + " entries");
            var shortKey = SystemKey.Find(null, RamdiskFiles.Decode(F("rd-short.lzo")), null);
            var noKey = SystemKey.Find(null, RamdiskFiles.Decode(F("rd-nokey.lzo")), null);
            Check(!shortKey.Found && shortKey.Why.Contains("is 16 bytes, not the 32") && !noKey.Found && noKey.Why.Contains("holds no key-file"), "a key file of the wrong length is not used, and an absent one is named (" + shortKey.Why + "; " + noKey.Why + ")");
            BootParts Hdr(string cmd) { var img = DevImage(cmd, 4000, 3, F("rd-nokey.gz")); return BootParts.Parse(img.Take(4096).ToArray(), img.Length); }
            var b64 = SystemKey.Find(Hdr("root=/dev/nandb decrypt hakchi-key-file=base64:" + Convert.ToBase64String(key)), null, "x");
            var badB64 = SystemKey.Find(Hdr("decrypt hakchi-key-file=base64:!!notbase64"), null, "x");
            var shortB64 = SystemKey.Find(Hdr("decrypt hakchi-key-file=base64:" + Convert.ToBase64String(key.Take(31).ToArray())), null, "x");
            Check(b64.Found && b64.Use().SequenceEqual(key) && b64.FoundIn == "the command line" && Hdr("x hakchi-key-file=base64:" + Convert.ToBase64String(key)).MaskedCmdline == "x hakchi-key-file=(the console's key, not shown)"
                  && !badB64.Found && badB64.Why.Contains("not valid Base64") && !shortB64.Found && shortB64.Why.Contains("31 bytes"),
                "the command line's key: found and masked wherever the command line is shown; an unusable one is named (" + badB64.Why + "; " + shortB64.Why + ")");
            var good = DevImage("x", 4000, 3, F("rd-nokey.gz"));
            byte[] With(Action<byte[]> t) { var b = (byte[])good.Clone(); t(b); return b; }
            var p1 = BootParts.Parse(With(b => BitConverter.GetBytes(1000u).CopyTo(b, 36)), good.Length); var p2 = BootParts.Parse(With(b => BitConverter.GetBytes(2u).CopyTo(b, 40)), good.Length); var p3 = BootParts.Parse(good, good.Length - 4096);
            Check(!p1.Ok && p1.Problem.Contains("page size") && !p2.Ok && p2.Problem.Contains("header version 2") && !p3.Ok && p3.Problem.Contains("shorter"), "malformed boot image headers are refused with the reason (" + p1.Problem + "; " + p2.Problem + "; " + p3.Problem + ")");

            // ---------------------------------------------------------------- P: a wrong key first, the right key in the spare slot (LZO)
            const string Cmd = "root=/dev/nandb decrypt ro console=ttyS0,115200";
            var kWrong = DevImage(Cmd, 40000, 11, F("rd-wrong.lzo")); var kRight = DevImage(Cmd, 50000, 12, F("rd-lzo-fast.lzo"));
            var conP = SysConsole("sysP", kWrong, kRight, cipher);
            var a = Backup(conP, s); var A = a.PrimaryPath;
            Check(a.Ok && Verifier.VerifyFile(A).Ok, "the backup holding the encrypted system part and both startup images is made and verifies (" + a.State + " " + a.Problem + ")");
            // CCS_BROWSE_SAMPLE=<folder>: keep a copy of this backup (and of N's below) there for the window's page renderer (a random key)
            var sample = Environment.GetEnvironmentVariable("CCS_BROWSE_SAMPLE");
            if (!string.IsNullOrEmpty(sample)) { Directory.CreateDirectory(sample); File.Copy(A, Path.Combine(sample, "Encrypted system (sample).zip"), true); }
            var recBefore = Directory.GetFiles(s.RecordsFolder, "*", SearchOption.AllDirectories).Select(Path.GetFileName).ToList();
            using (var c = BackupContents.Open(A, s).Contents)
            {
                var sp = c.Find("m:restore/nandb.img"); var act = c.Find("m:restore/boot2-active.img"); var s68 = c.Find("m:restore/boot2-slot68.img");
                var kids = c.Entries.Where(e => e.Container == sp).ToList();
                Check(sp.ImageType == "System part (encrypted; SquashFS, gzip): NES Classic system (USA)" && kids.Count == sys.Listing.Count && sp.ImageNote.Contains("Decrypted on this PC") && sp.ImageNote.Contains("restore/boot2-slot68.img (the ramdisk's key-file)") && sp.HashTrusted,
                    "the encrypted system part opens with the key in the spare slot's LZO ramdisk (the active image's key does not decrypt it), identified by hakchi's rules, every entry listed (" + sp.ImageType + "; " + kids.Count + " of " + sys.Listing.Count + ")");
                Check(kids.All(e => sys.Listing.ContainsKey(e.InnerPath)) && kids.Where(e => e.Kind == EntryKind.File).All(e => e.Size == sys.Files[e.InnerPath].Length),
                    "its listing is unsquashfs's listing of the image it was made from, with the same sizes");
                var files = kids.Where(e => e.Kind == EntryKind.File).ToList();
                Check(files.All(e => c.ExpectedHash(e) == Sha(sys.Files[e.InnerPath])), "every file read through the decrypted view matches the bytes it was built from (" + files.Count + " files)");
                var region = kids.First(e => e.InnerPath == "/etc/clover/REGION");
                Check(region.HashOrigin == HashOrigin.Derived && region.BoundTo == sp.DisplayPath && region.BoundToSha256 == sp.Sha256 && sp.Sha256 == Sha(cipher) && region.KeyFrom == "restore/boot2-slot68.img" && region.KeyFromSha256 == s68.Sha256
                      && region.HashText.Contains("decrypted on this PC with the key in restore/boot2-slot68.img") && region.HashText.Contains("It is not a checksum recorded when the backup was made") && region.ChecksumKind == "worked out on this PC from a verified parent",
                    "provenance: worked out on this PC, bound to the encrypted part's recorded SHA-256 and to the startup image the key came from, never called a recorded checksum (" + region.HashText + ")");
                var png = kids.First(e => e.InnerPath.EndsWith(".png"));
                var pb = c.ReadVerified(png, 1000000, out var pwhy);
                Check(pb != null && Sha(pb) == Sha(TinyPng), "preview: a picture inside the decrypted part is shown only after its bytes match their worked-out checksum (" + pwhy + ")");
                var sel = new Selection(); sel.Add(region); sel.Add(png);
                var x = Exporter.Export(c, ExportRequest.Selected(c, sel, exp, ExportLayout.OriginalPaths));
                var man = Json.Parse(File.ReadAllText(x.ManifestPath)); var rows = man.A("files").OfType<Dictionary<string, object>>().ToList();
                Check(x.Ok && x.Files.Count == 2 && x.Files.All(f => Sha(Path.Combine(x.Folder, f.RelativePath)) == Sha(sys.Files[f.Entry.InnerPath])) && rows.All(rw => rw.O("checksum_bound_to")?.S("decrypted_with_key_from") == "restore/boot2-slot68.img" && rw.O("checksum_bound_to")?.S("parent_sha256") == sp.Sha256 && rw.S("checksum_kind") == "worked out on this PC from a verified parent"),
                    "export from the decrypted part: exact files, each read back; the export list names the encrypted parent, its recorded SHA-256 and the key's startup image (" + x.Summary + ")");
                var xr = Exporter.Export(c, One(c, sp, exp));
                Check(xr.Ok && Sha(Path.Combine(xr.Folder, xr.Files[0].RelativePath)) == Sha(cipher), "the part itself still exports as one exact encrypted image (what a restore puts back)");
                // ---- the startup images: their parts and ramdisk files
                var actParts = c.Children(act.Id).Select(e => e.Name).ToList(); var rAct = c.Find("b:" + act.Id + "|ramdisk.cpio.lzo"); var r68 = c.Find("b:" + s68.Id + "|ramdisk.cpio.lzo");
                Check(actParts.SequenceEqual(new[] { "kernel", "ramdisk.cpio.lzo" }) && act.ImageType == "Startup software (an Android boot image: kernel and ramdisk with " + (pl.Entries.Count + 3) + " entries)" && act.ImageNote.Contains("not a bootloader") && act.ImageNote.Contains("It carries the system key (the ramdisk's key-file), which is never shown")
                      && rAct.IsImage && rAct.ImageType == "Ramdisk (LZO cpio archive)",
                    "a startup image is a container: its kernel and its LZO ramdisk are listed under it, not 'no files' (" + act.ImageType + ")");
                bool m68 = RamdiskMatches(c, r68, ExpectedRamdisk(expected, "listing"), out var w68), mAct = RamdiskMatches(c, rAct, ExpectedRamdisk(expected, "wrong"), out var wAct);
                Check(m68 && mAct,
                    "each ramdisk's files match Python's own listing: kinds, permissions, owners, dates, sizes, link targets, implied folders and every checksum (" + (w68 ?? wAct ?? "all equal") + ")");
                var kern = c.Find("b:" + s68.Id + "|kernel"); var kb = new MemoryStream(); c.CopyTo(kern, kb);
                var rb = new MemoryStream(); c.CopyTo(r68, rb);
                Check(kb.ToArray().SequenceEqual(kRight.Skip(2048).Take(50000)) && rb.ToArray().SequenceEqual(F("rd-lzo-fast.lzo")) && c.ExpectedHash(r68) == Sha(F("rd-lzo-fast.lzo")) && r68.BoundTo == s68.DisplayPath,
                    "each part reads back exactly as the image holds it (the kernel, the stored LZO ramdisk), its checksum bound to the startup image");
                var keyE = c.Find("r:" + r68.Id + "|/key-file"); var dc = c.Find("r:" + r68.Id + "|/dev/console"); var sh = c.Find("r:" + r68.Id + "|/bin/sh"); var si = c.Find("r:" + r68.Id + "|/sbin/init");
                Check(dc.Kind == EntryKind.Special && dc.TypeCode == 'c' && sh.Kind == EntryKind.Link && sh.LinkTarget == "busybox" && si.DateNote == "No date was recorded in the ramdisk." && c.Find("r:" + r68.Id + "|/bin/busybox").HashText.Contains("ramdisk"),
                    "a device, a link and an entry with no date are listed as what they are");
                var kv = c.ReadVerified(keyE, 1000, out var kwhy);
                Check(keyE.Private == BackupContents.KeyPrivateText && kv == null && kwhy == BackupContents.KeyPrivateText && keyE.Category == Category.SystemFiles, "the key file is listed as private: it is never previewed (" + kwhy + ")");
                var fr = new ExportRequest { Destination = exp, Scope = ExportScope.Folder, Layout = ExportLayout.OriginalPaths, FolderName = "Files inside " + r68.Name, ScopeText = "Everything inside " + r68.DisplayPath + "." };
                fr.Entries.AddRange(c.Children(r68.Id));   // as the window's "Files inside" export asks for it
                var fx = Exporter.Export(c, fr);
                var fman = Json.Parse(File.ReadAllText(fx.ManifestPath));
                var fsk = fman.O("system_key"); var fw = fman.A("withheld").OfType<Dictionary<string, object>>().ToList();
                Check(fx.Ok && fx.Withheld.Count == 1 && fx.Withheld[0] == keyE && !Directory.EnumerateFiles(fx.Folder, "key-file", SearchOption.AllDirectories).Any() && fw.Count == 1 && fw[0].S("reason") == Exporter.WithheldReason
                      && fx.Summary.Contains("; the separate system key file was skipped (select the key file itself to export it)") && fx.KeyHolders.Count == 0 && !fx.Summary.Contains("can contain")
                      && fsk != null && fsk.L("separate_key_files_skipped") == 1 && fsk.A("whole_files_that_can_contain_it").Count == 0 && fsk.S("note") == Exporter.SkippedOnlyNote
                      && fx.Files.Count == ExpectedRamdisk(expected, "listing").Values.Count(v => v.S("type") == "f") - 1 && r68.ImageNote.Contains("carries the key, so keep such a copy private"),
                    "decoded files only (the ramdisk's folder): the separate key file is skipped and the result and list say only that, with no whole file that can contain the key (" + fx.Summary + ")");
                // a bulk export (search results) that takes the whole startup image and its ramdisk with the decoded files: the containers
                // export unchanged and can contain the key; only the separate key file is skipped, and nothing says the export holds no key
                var hold = Path.Combine(dir, "export-holders"); Directory.CreateDirectory(hold);
                var shown = c.Search("boot2-slot68").Where(e => e.Kind != EntryKind.Folder).ToList();
                var sx = Exporter.Export(c, ExportRequest.SearchResults(c, null, "boot2-slot68", shown, hold, ExportLayout.OriginalPaths));
                var sman = Json.Parse(File.ReadAllText(sx.ManifestPath)); var ssk = sman.O("system_key");
                var sw = sman.A("withheld").OfType<Dictionary<string, object>>().ToList();
                var xImg = sx.Files.FirstOrDefault(f => f.Entry == s68); var xRd = sx.Files.FirstOrDefault(f => f.Entry == r68);
                bool sameImg = xImg != null && Sha(Path.Combine(sx.Folder, xImg.RelativePath)) == s68.Sha256, sameRd = xRd != null && Sha(Path.Combine(sx.Folder, xRd.RelativePath)) == Sha(F("rd-lzo-fast.lzo"));
                bool rdHoldsKey = false;
                if (xRd != null)
                {
                    var dec = RamdiskFiles.Decode(File.ReadAllBytes(Path.Combine(sx.Folder, xRd.RelativePath)));
                    var kf = dec.Entries.FirstOrDefault(x => x.Path == "/key-file");
                    if (kf != null) { var kms = new MemoryStream(); dec.CopyFile(kf, kms); rdHoldsKey = kms.ToArray().SequenceEqual(key); Array.Clear(kms.GetBuffer(), 0, (int)kms.Length); }
                    Array.Clear(dec.Data, 0, dec.Data.Length);
                }
                var holderPaths = new HashSet<string>(ssk?.A("whole_files_that_can_contain_it")?.OfType<string>() ?? Enumerable.Empty<string>());
                Check(sx.Ok && shown.Contains(s68) && shown.Contains(r68) && shown.Contains(keyE) && sx.Withheld.Count == 1 && sx.Withheld[0] == keyE && !Directory.EnumerateFiles(sx.Folder, "key-file", SearchOption.AllDirectories).Any()
                      && sameImg && sameRd && rdHoldsKey,
                    "a bulk export with the whole startup image and its ramdisk: both export exactly as the backup holds them (the exported ramdisk, decoded, still holds the made-up key file), and only the separate key file is skipped (" + sx.Files.Count + " files)");
                Check(sx.KeyHolders.Count == 2 && sx.KeyHolders.Any(f => f.Entry == s68) && sx.KeyHolders.Any(f => f.Entry == r68) && !sx.KeyHolders.Any(f => f.Entry.Name == "kernel")
                      && sx.Summary.Contains("; the separate system key file was skipped (select the key file itself to export it); 2 exported file(s) are whole startup images, ramdisks or raw NAND images, which can contain the system key: keep this export private.")
                      && sw.Count == 1 && sw[0].S("reason") == Exporter.WithheldReason && ssk != null && ssk.L("separate_key_files_skipped") == 1 && holderPaths.SetEquals(new[] { s68.DisplayPath, r68.DisplayPath }) && ssk.S("note") == Exporter.HoldersNote
                      && !sx.Summary.Contains("not included") && !File.ReadAllText(sx.ManifestPath).Contains("never as part of"),
                    "its result and list: the separate key file skipped, the two whole files named as able to contain the key, keep the export private; nothing says the key is not in it (" + sx.Summary + ")");
                var sxl = Path.Combine(dir, "export-holders-list"); Directory.CreateDirectory(sxl); File.Copy(sx.ManifestPath, Path.Combine(sxl, Exporter.ManifestName));
                Check(KeyLeaks(key, sxl).Count == 0, "that export's list names the files without the key's bytes in any form");
                var ix = Exporter.Export(c, One(c, s68, hold));
                Check(ix.Ok && ix.Withheld.Count == 0 && ix.KeyHolders.Count == 1 && Sha(Path.Combine(ix.Folder, ix.Files[0].RelativePath)) == s68.Sha256 && ix.Summary.Contains("; 1 exported file(s) are whole startup images, ramdisks or raw NAND images, which can contain the system key")
                      && !ix.Summary.Contains("skipped"),
                    "Export This Image… of the startup image still exports it exactly, and its result says it can contain the key (" + ix.Summary + ")");
                var tx = Exporter.Export(c, ExportRequest.SearchResults(c, null, "key-file", c.Search("key-file"), exp));
                Check(tx.Ok && tx.Withheld.Count == 2 && tx.Withheld.All(e => e.Private != null) && tx.Files.Count == 0, "exporting search results withholds both key files, even when they are all the search found (" + tx.Withheld.Count + ")");
                var kx = Exporter.Export(c, One(c, keyE, exp));
                Check(kx.Ok && kx.Files.Count == 1 && File.ReadAllBytes(Path.Combine(kx.Folder, kx.Files[0].RelativePath)).SequenceEqual(key), "selected on its own, the key file exports exactly (a private action the person chose)");
                Check(sp.ImageNote.IndexOf(Convert.ToBase64String(key), StringComparison.Ordinal) < 0 && c.Entries.All(e => (e.ImageNote ?? "").IndexOf(Hex.Of(key), StringComparison.OrdinalIgnoreCase) < 0), "no note shows the key");
                c.DeriveMissingHashes();
                Check(c.HashesComplete, "Work Out All Checksums completes, inside the startup images and the decrypted part too");
            }
            var recAfter = Directory.GetFiles(s.RecordsFolder, "*", SearchOption.AllDirectories).Select(Path.GetFileName).ToList();
            var newRec = recAfter.Except(recBefore).ToList();
            Check(newRec.All(n => n == CheckLog.FileName || n.StartsWith("derived-index-", StringComparison.Ordinal)), "browsing made no restore point and no record other than its check log and derived index (" + string.Join(", ", newRec) + ")");
            var leaks = KeyLeaks(key, s.RecordsFolder, exp);
            Check(leaks.Count == 0, "the key is in no record, check log, derived index or export list, in any form (raw, hex, Base64) (" + string.Join(", ", leaks) + ")");

            // ---------------------------------------------------------------- the same backup damaged in three ways (salvage opens)
            string Copy(string n) { var p = Path.Combine(dir, n + ".zip"); File.Copy(A, p); return p; }
            var keyBad = Copy("key-source-damaged"); var e68 = EntryOf(keyBad, "restore/boot2-slot68.img"); Flip(keyBad, e68.DataOffset + 2048 + 100);
            using (var c = BackupContents.OpenSalvage(keyBad, s).Contents)
            {
                var sp = c.Find("m:restore/nandb.img");
                Check(c.Find("m:restore/boot2-slot68.img").Unreadable != null && sp.ImageType == "System part (not a file system as it is stored)" && c.Entries.Count(e => e.Container == sp) == 0
                      && sp.ImageNote.Contains("restore/boot2-slot68.img:") && sp.ImageNote.Contains("so its key is not used") && sp.ImageNote.Contains("restore/boot2-active.img: decrypted with its key (the ramdisk's key-file), it is not a SquashFS file system"),
                    "the startup image holding the key no longer matches its checksum: its key is not used, and the part is not decrypted (" + sp.ImageNote + ")");
            }
            var partBad = Copy("system-part-changed"); var enb = EntryOf(partBad, "restore/nandb.img"); Flip(partBad, enb.DataOffset + 300000);
            using (var c = BackupContents.OpenSalvage(partBad, s).Contents)
            {
                var sp = c.Find("m:restore/nandb.img");
                Check(sp.Unreadable != null && c.Entries.Count(e => e.Container == sp) == 0 && c.Children(c.Find("m:restore/boot2-active.img").Id).Count == 2,
                    "a changed system part (damaged since the backup was made) is never decrypted or listed; the startup images still are (" + sp.Unreadable + ")");
            }
            var idx = Copy("index-damaged"); DamageIndex(idx);
            using (var c = BackupContents.OpenSalvage(idx, s).Contents)
            {
                var sp = c.Find("m:restore/nandb.img"); var kids = c.Entries.Where(e => e.Container == sp).ToList(); var region = kids.FirstOrDefault(e => e.InnerPath == "/etc/clover/REGION");
                var x = Exporter.Export(c, One(c, region, exp));
                Check(c.FoundBy != null && kids.Count == sys.Listing.Count && region != null && x.Ok && Sha(Path.Combine(x.Folder, x.Files[0].RelativePath)) == Sha(sys.Files["/etc/clover/REGION"]),
                    "a damaged table of contents: the parts found and checked by the part finder are read in place, the system part is still decrypted and its files export exactly (" + c.FoundBy + ")");
            }
            Check(new FileInfo(idx).Length == new FileInfo(A).Length && !Directory.EnumerateFiles(dir, "*rebuilt*", SearchOption.AllDirectories).Any(), "nothing was rebuilt or written by those opens");

            // ---------------------------------------------------------------- G: the key on the command line, gzip and uncompressed ramdisks, a damaged system
            var kCmd = DevImage(Cmd + " hakchi-key-file=base64:" + Convert.ToBase64String(key), 40000, 13, F("rd-nokey.gz")); var kPlain = DevImage(Cmd, 40000, 14, plainCpio);
            var conG = SysConsole("sysG", kCmd, kPlain, F("nandb-damaged-cipher.img"));
            var g = Backup(conG, s);
            using (var c = BackupContents.Open(g.PrimaryPath, s).Contents)
            {
                var sp = c.Find("m:restore/nandb.img"); var act = c.Find("m:restore/boot2-active.img"); var s68 = c.Find("m:restore/boot2-slot68.img");
                Check(sp.ImageType == "System part (encrypted; SquashFS, xz), damaged" && sp.ImageNote.Contains("(the command line)") && sp.ImageNote.Contains("cannot be read") && c.Entries.Count(e => e.Container == sp) == 0 && sp.HashTrusted,
                    "a key that opens a damaged system: nothing in a list that cannot be read is offered (" + sp.ImageType + ")");
                var rx = Exporter.Export(c, One(c, sp, exp));
                Check(rx.Ok && Sha(Path.Combine(rx.Folder, rx.Files[0].RelativePath)) == Sha(F("nandb-damaged-cipher.img")), "and it still exports as one exact image");
                Check(act.ImageNote.Contains("hakchi-key-file=(the console's key, not shown)") && act.ImageNote.IndexOf(Convert.ToBase64String(key), StringComparison.Ordinal) < 0 && act.ImageNote.Contains("It carries the system key (the command line)"),
                    "the command line is shown with its key masked (" + act.ImageNote.Substring(act.ImageNote.IndexOf("command line:", StringComparison.Ordinal)) + ")");
                bool mg = RamdiskMatches(c, c.Find("b:" + act.Id + "|ramdisk.cpio.gz"), ExpectedRamdisk(expected, "nokey"), out var wg), mp = RamdiskMatches(c, c.Find("b:" + s68.Id + "|ramdisk.cpio"), ExpectedRamdisk(expected, "listing"), out var wp);
                Check(mg && mp,
                    "a gzip ramdisk and an uncompressed one are listed file by file, each matching Python's listing (" + (wg ?? wp ?? "all equal") + ")");
            }

            // ---------------------------------------------------------------- N: no key anywhere it can be used (none in the active image, an xz ramdisk)
            var kNo = DevImage(Cmd, 40000, 15, F("rd-nokey.lzo")); var kXz = DevImage(Cmd, 40000, 16, F("rd-xz.xz"));
            var conN = SysConsole("sysN", kNo, kXz, cipher);
            var n = Backup(conN, s);
            if (!string.IsNullOrEmpty(sample)) File.Copy(n.PrimaryPath, Path.Combine(sample, "Encrypted system without a key (sample).zip"), true);
            using (var c = BackupContents.Open(n.PrimaryPath, s).Contents)
            {
                var sp = c.Find("m:restore/nandb.img"); var s68 = c.Find("m:restore/boot2-slot68.img"); var rx = c.Find("b:" + s68.Id + "|ramdisk.cpio.xz");
                Check(sp.ImageType == "System part (not a file system as it is stored)" && sp.ImageNote.Contains("No key that could open it was found in this backup's startup software") && sp.ImageNote.Contains("restore/boot2-active.img: its ramdisk (LZO) holds no key-file")
                      && sp.ImageNote.Contains("restore/boot2-slot68.img: its ramdisk is xz-compressed, which this version does not decode") && sp.ImageNote.Contains("recovery/original-kernel.img: not in this backup") && !sp.ImageNote.Contains("never leaves") && c.Entries.Count(e => e.Container == sp) == 0,
                    "without a usable key the part stays one image, with each startup image's exact reason; it is not called impossible (" + sp.ImageNote + ")");
                Check(rx.IsImage && rx.ImageType == "Ramdisk (xz)" && rx.ImageNote.Contains("xz-compressed") && c.Children(rx.Id).Count == 0, "the xz ramdisk is a part of its own with what is missing to list it (" + rx.ImageNote + ")");
                var xr = Exporter.Export(c, One(c, sp, exp));
                Check(xr.Ok && Sha(Path.Combine(xr.Folder, xr.Files[0].RelativePath)) == Sha(cipher), "and the part exports as one exact image");
            }
            Check(KeyLeaks(key, s.RecordsFolder, exp).Count == 0 && KeyLeaks(wrong, s.RecordsFolder, exp).Count == 0, "after every open and export, no record or export list holds either key");
        }

        public static void BrowserOwnerSystem()
        {
            if (OwnerSystemDir == null || OwnerDecrypted == null || !File.Exists(OwnerDecrypted)) throw new SkipException("the owner's derived system part and recorded decrypted copy were not given (--owner-system, --owner-decrypted)");
            var cipherPath = Path.Combine(OwnerSystemDir, "nandb-cipher-derived.img"); if (!File.Exists(cipherPath)) throw new SkipException("no derived encrypted system part in " + OwnerSystemDir);
            var kernel = StockKernel();   // the owner's original kernel (--kernel), read only
            var dir = NewDir("browser-owner-system"); var s = SettingsIn(dir, false); var exp = Path.Combine(dir, "export"); Directory.CreateDirectory(exp);
            var cipher = File.ReadAllBytes(cipherPath); var plain = File.ReadAllBytes(OwnerDecrypted);
            Check(Sha(kernel) == "dae9eec3e7ad25379484f7e7db4be7d09b1135b0f1f3c8cb326f8da97187b521" && Sha(plain) == "e845bdcabaf5922cd164ef4ebad2e6b6427c780e424746de00d1f6b89b8ff36f" && cipher.Length == 19922944,
                "the inputs: the owner's original kernel (dae9eec3…), the recorded decrypted copy (e845bdca…, " + plain.Length + " bytes) and the derived encrypted part (" + Sha(cipher).Substring(0, 16) + "…, " + cipher.Length + " bytes)");
            var lay = new[] { ("nanda", "boot", 3072L * 1024), ("nandb", "rootfs", 19456L * 1024), ("nandc", "data", 65536L * 1024), ("nandd", "private", 1024L * 1024), ("nande", "UDISK", 2048L * 1024) };
            var con = SysConsole("owner", kernel, null, cipher, lay, null, 0);
            var a = Backup(con, s);
            Check(a.Ok && Verifier.VerifyFile(a.PrimaryPath).Ok, "a current-format backup holding them is made and verifies (" + a.State + " " + a.Problem + ")");
            var bp = BootParts.Parse(kernel.Take(4096).ToArray(), kernel.Length);
            var key = SystemKey.Find(bp, RamdiskFiles.Decode(kernel.Skip((int)bp.RamdiskAt).Take((int)bp.RamdiskSize).ToArray()), null);
            using (var c = BackupContents.Open(a.PrimaryPath, s).Contents)
            {
                var act = c.Find("m:restore/boot2-active.img"); var orig = c.Find("m:" + BackupFormat.OriginalKernelName); var sp = c.Find("m:restore/nandb.img");
                var rd = c.Find("b:" + act.Id + "|ramdisk.cpio.lzo"); var kf = rd == null ? null : c.Find("r:" + rd.Id + "|/key-file");
                Check(key.Found && rd != null && kf != null && kf.Size == 32 && kf.Private != null && c.Entries.Count(e => e.Container == rd) == 16 && orig != null && c.Children(orig.Id).Count == 2,
                    "the original kernel's LZO ramdisk lists 16 entries with a 32-byte key-file, listed as private; the original kernel kept on its own is a container too");
                var kids = c.Entries.Where(e => e.Container == sp).ToList();
                Check(sp.ImageType.StartsWith("System part (encrypted; SquashFS, gzip): NES Classic system") && sp.KeyFrom == "restore/boot2-active.img" && kids.Count > 0 && kids[0].KeyFrom == "restore/boot2-active.img",
                    "the real encrypted system part opens with the key in the original kernel's ramdisk (" + sp.ImageType + ", " + kids.Count + " entries)");
                // the whole decrypted view against the recorded decrypted copy
                using (var v = new XtsView(new MemoryStream(cipher, false), key.Use()))
                {
                    var dec = new byte[plain.Length]; int got = 0; while (got < dec.Length) got += v.Read(dec, got, dec.Length - got);
                    Check(Sha(dec) == Sha(plain), "decrypted on this PC, its first " + plain.Length + " bytes are byte for byte the copy the console's own dm-crypt mapping gave on October 9 (SHA-256 e845bdca…)");
                }
                // the listing: unsquashfs's, of the recorded decrypted copy (read only)
                var lln = RunWsl(dir, "owner-lln", "TZ=UTC /usr/bin/unsquashfs -lln '" + W(OwnerDecrypted) + "' | sed -n 's#^.* squashfs-root##p' | sed 's# -> .*##' | grep -v '^$' | sort");
                var paths = lln.Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.StartsWith("/")).ToList();
                Check(paths.Count == kids.Count && kids.Select(e => e.InnerPath).OrderBy(p => p, StringComparer.Ordinal).SequenceEqual(paths.OrderBy(p => p, StringComparer.Ordinal)),
                    "every entry unsquashfs lists in the recorded copy is listed, and nothing else (" + kids.Count + " of " + paths.Count + ")");
                var recorded = new SquashFsReader(new MemoryStream(plain, false));
                var files = kids.Where(e => e.Kind == EntryKind.File).ToList(); int same = 0;
                foreach (var e in files) { var m = new MemoryStream(); recorded.ReadFile(e.InnerRef, m, e.Size); if (c.ExpectedHash(e) == Sha(m.ToArray())) same++; }
                Check(same == files.Count && files.All(e => e.HashOrigin == HashOrigin.Derived && e.BoundToSha256 == sp.Sha256 && e.KeyFromSha256 == act.Sha256),
                    "every file read through the decrypted view matches the same file in the recorded copy (" + same + " of " + files.Count + "), each bound to the encrypted part and the startup image");
                Check(sp.ImageSystem != null, "identified by hakchi's rules: " + sp.ImageSystem);
            }
            var leaks = KeyLeaks(key.Use(), s.RecordsFolder, exp); key.Clear();
            Check(leaks.Count == 0, "the real key is in no record, check log or derived index in any form (" + string.Join(", ", leaks) + ")");
        }
    }
}
