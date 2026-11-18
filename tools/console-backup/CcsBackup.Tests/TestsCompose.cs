// Classic Cart Studio console backup and restore engine tests: Console Composer's three file formats (CcsBackup/Compose; COMPOSER-
// FORMATS.md). GPL-3.0-or-later. Every sample here is synthetic: generated pictures, synthetic headers (SquashFS, Android boot image,
// eGON/uboot, ext4, iNES, tar.gz) and made-up identities. No Nintendo material, no real console identity, nothing opened on a device.
using CcsBackup.Archive;
using CcsBackup.Compose;
using CcsBackup.Device;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using static CcsBackup.Tests.Program;
using CSha = CcsBackup.Compose.Sha;

namespace CcsBackup.Tests
{
    static partial class Tests
    {
        static string Code(Action a) { try { a(); return "ok"; } catch (ComposeException ex) { return ex.Code; } }
        static string Msg(Action a) { try { a(); return "ok"; } catch (ComposeException ex) { return ex.Message; } }

        // ------------------------------------------------------------------ synthetic material
        static byte[] Px(int w, int h, int seed, bool alpha)
        {
            var b = new byte[w * h * 4]; var r = new Random(seed);
            for (int y = 0; y < h; y++) for (int x = 0; x < w; x++)
            { int i = (y * w + x) * 4; b[i] = (byte)(x * 255 / Math.Max(1, w - 1)); b[i + 1] = (byte)(y * 255 / Math.Max(1, h - 1)); b[i + 2] = (byte)((x ^ y) + r.Next(8)); b[i + 3] = (byte)(alpha && x < w / 4 ? 64 : 255); }
            return b;
        }
        static byte[] Png(int w, int h, int seed = 1, bool alpha = false) => ImageRules.EncodePng(w, h, Px(w, h, seed, alpha), alpha);
        static byte[] GdiJpeg(int w, int h, int seed, long quality = 90, bool noise = false)
        {
            using (var bmp = new Bitmap(w, h, PixelFormat.Format24bppRgb))
            {
                var r = new Random(seed); var d = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format24bppRgb);
                var row = new byte[w * 3];
                for (int y = 0; y < h; y++)
                {
                    for (int x = 0; x < w; x++) { int i = x * 3; if (noise) { row[i] = (byte)r.Next(256); row[i + 1] = (byte)r.Next(256); row[i + 2] = (byte)r.Next(256); } else { row[i] = (byte)(x * 255 / w); row[i + 1] = (byte)(y * 255 / h); row[i + 2] = (byte)((x / 16 + y / 16) % 2 * 200 + seed * 5); } }
                    System.Runtime.InteropServices.Marshal.Copy(row, 0, IntPtr.Add(d.Scan0, y * d.Stride), row.Length);
                }
                bmp.UnlockBits(d);
                var enc = ImageCodecInfo.GetImageEncoders().First(c => c.FormatID == ImageFormat.Jpeg.Guid);
                var ep = new EncoderParameters(1); ep.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, quality);
                var ms = new MemoryStream(); bmp.Save(ms, enc, ep); return ms.ToArray();
            }
        }
        /// <summary>a JPEG with an EXIF segment (orientation 6: the camera was turned) inserted after its start marker</summary>
        static byte[] WithExif(byte[] jpeg)
        {
            var tiff = new byte[] { 0x49, 0x49, 0x2A, 0, 8, 0, 0, 0, 1, 0, 0x12, 0x01, 3, 0, 1, 0, 0, 0, 6, 0, 0, 0, 0, 0, 0, 0 };
            var app1 = new List<byte> { 0xFF, 0xE1 }; int len = 2 + 6 + tiff.Length; app1.Add((byte)(len >> 8)); app1.Add((byte)len);
            app1.AddRange(Encoding.ASCII.GetBytes("Exif\0\0")); app1.AddRange(tiff);
            return jpeg.Take(2).Concat(app1).Concat(jpeg.Skip(2)).ToArray();
        }
        static byte[] PngWithChunk(byte[] png, string type, byte[] data)
        {
            var c = new Crc32(); var t = Encoding.ASCII.GetBytes(type); c.Update(t, 0, 4); c.Update(data, 0, data.Length);
            var chunk = new List<byte>(); void Be(uint v) { chunk.Add((byte)(v >> 24)); chunk.Add((byte)(v >> 16)); chunk.Add((byte)(v >> 8)); chunk.Add((byte)v); }
            Be((uint)data.Length); chunk.AddRange(t); chunk.AddRange(data); Be(c.Value);
            return png.Take(33).Concat(chunk).Concat(png.Skip(33)).ToArray();   // after the signature and IHDR
        }

        static void P32(byte[] b, int o, uint v) { b[o] = (byte)v; b[o + 1] = (byte)(v >> 8); b[o + 2] = (byte)(v >> 16); b[o + 3] = (byte)(v >> 24); }
        static void P64(byte[] b, int o, ulong v) { P32(b, o, (uint)v); P32(b, o + 4, (uint)(v >> 32)); }
        static void P16(byte[] b, int o, int v) { b[o] = (byte)v; b[o + 1] = (byte)(v >> 8); }
        static byte[] SquashFs()
        {
            var b = new byte[4096]; Encoding.ASCII.GetBytes("hsqs").CopyTo(b, 0); P32(b, 4, 5); P32(b, 12, 131072); P16(b, 20, 1); P16(b, 22, 17); P16(b, 26, 1); P16(b, 28, 4); P16(b, 30, 0);
            P64(b, 40, 4096); P64(b, 48, 4000); P64(b, 56, ulong.MaxValue); P64(b, 64, 96); P64(b, 72, 2000); P64(b, 80, ulong.MaxValue); P64(b, 88, ulong.MaxValue);
            for (int i = 96; i < b.Length; i++) b[i] = (byte)(i * 7);
            return b;
        }
        static byte[] Cipher(int n = 1024 * 1024) { var b = new byte[n]; new Random(7).NextBytes(b); return b; }
        static byte[] Ext4Image()
        {
            var b = new byte[65536]; int sb = 1024;
            P32(b, sb, 16); P32(b, sb + 4, 64); P32(b, sb + 0x14, 1); P32(b, sb + 0x18, 0); P32(b, sb + 0x20, 8192); P32(b, sb + 0x28, 16);
            P16(b, sb + 0x38, 0xEF53); P16(b, sb + 0x3A, 1); P32(b, sb + 0x4C, 1); P16(b, sb + 0x58, 128);
            P32(b, 2048 + 8, 5); P16(b, 5 * 1024 + 128, 0x41ED);
            return b;
        }
        static byte[] Kernel()
        {
            var b = new byte[6144]; Encoding.ASCII.GetBytes("ANDROID!").CopyTo(b, 0); P32(b, 8, 4096); P32(b, 36, 2048);
            for (int i = 2048; i < b.Length; i++) b[i] = (byte)(i * 13); return b;
        }
        static byte[] NesGame(int prg = 2, int chr = 1, int extra = 0)
        {
            var b = new byte[16 + prg * 16384 + chr * 8192 + extra]; b[0] = (byte)'N'; b[1] = (byte)'E'; b[2] = (byte)'S'; b[3] = 0x1A; b[4] = (byte)prg; b[5] = (byte)chr;
            for (int i = 16; i < b.Length; i++) b[i] = (byte)(i * 31 + 5); return b;
        }
        static byte[] Tar(string name, byte[] content)
        {
            var h = new byte[512];
            Encoding.ASCII.GetBytes(name).CopyTo(h, 0); Encoding.ASCII.GetBytes("0000755\0").CopyTo(h, 100); Encoding.ASCII.GetBytes("0000000\0").CopyTo(h, 108); Encoding.ASCII.GetBytes("0000000\0").CopyTo(h, 116);
            Encoding.ASCII.GetBytes(Convert.ToString(content.Length, 8).PadLeft(11, '0') + "\0").CopyTo(h, 124); Encoding.ASCII.GetBytes("00000000000\0").CopyTo(h, 136);
            for (int i = 148; i < 156; i++) h[i] = 32; h[156] = (byte)'0'; Encoding.ASCII.GetBytes("ustar\0").CopyTo(h, 257); Encoding.ASCII.GetBytes("00").CopyTo(h, 263);
            long sum = h.Sum(x => (long)x); Encoding.ASCII.GetBytes(Convert.ToString(sum, 8).PadLeft(6, '0') + "\0 ").CopyTo(h, 148);
            var ms = new MemoryStream(); ms.Write(h, 0, 512); ms.Write(content, 0, content.Length); ms.Write(new byte[(512 - content.Length % 512) % 512 + 1024], 0, (512 - content.Length % 512) % 512 + 1024);
            return ms.ToArray();
        }
        static byte[] Gz(byte[] b) { var ms = new MemoryStream(); using (var g = new GZipStream(ms, CompressionLevel.Optimal, true)) g.Write(b, 0, b.Length); return ms.ToArray(); }
        static byte[] ZipOf(params (string name, byte[] data, bool deflate)[] members)
        {
            var ms = new MemoryStream(); var w = new ZipWriter(ms, new DateTime(2026, 11, 15));
            foreach (var m in members) w.Add(m.name, m.deflate, s => s.Write(m.data, 0, m.data.Length));
            w.Finish(); return ms.ToArray();
        }

        sealed class RawMember { public string Name; public byte[] NameBytes; public byte[] Data = new byte[0]; public bool Deflate; public ushort Flags = 0x0800; public uint ExtAttr = 0x81A40000; public ushort MadeBy = 0x031E; }
        /// <summary>a ZIP written byte by byte, so that the hostile cases a correct writer never makes can be made</summary>
        static byte[] RawZip(IList<RawMember> ms, byte[] prefix = null, byte[] suffix = null)
        {
            var o = new MemoryStream(); var bw = new BinaryWriter(o);
            if (prefix != null) bw.Write(prefix);
            var cds = new List<(RawMember m, uint crc, byte[] comp, long off, byte[] nb)>();
            foreach (var m in ms)
            {
                var nb = m.NameBytes ?? Encoding.UTF8.GetBytes(m.Name); var c = new Crc32(); c.Update(m.Data, 0, m.Data.Length);
                var comp = m.Data;
                if (m.Deflate) { var z = new MemoryStream(); using (var d = new DeflateStream(z, CompressionLevel.Optimal, true)) d.Write(m.Data, 0, m.Data.Length); comp = z.ToArray(); }
                long off = o.Position;
                bw.Write(0x04034b50u); bw.Write((ushort)20); bw.Write(m.Flags); bw.Write((ushort)(m.Deflate ? 8 : 0)); bw.Write((ushort)0); bw.Write((ushort)0x5B6F);
                bw.Write(c.Value); bw.Write((uint)comp.Length); bw.Write((uint)m.Data.Length); bw.Write((ushort)nb.Length); bw.Write((ushort)0); bw.Write(nb); bw.Write(comp);
                cds.Add((m, c.Value, comp, off, nb));
            }
            long cd = o.Position;
            foreach (var x in cds)
            {
                bw.Write(0x02014b50u); bw.Write(x.m.MadeBy); bw.Write((ushort)20); bw.Write(x.m.Flags); bw.Write((ushort)(x.m.Deflate ? 8 : 0)); bw.Write((ushort)0); bw.Write((ushort)0x5B6F);
                bw.Write(x.crc); bw.Write((uint)x.comp.Length); bw.Write((uint)x.m.Data.Length); bw.Write((ushort)x.nb.Length); bw.Write((ushort)0); bw.Write((ushort)0); bw.Write((ushort)0); bw.Write((ushort)0);
                bw.Write(x.m.ExtAttr); bw.Write((uint)x.off); bw.Write(x.nb);
            }
            long end = o.Position;
            bw.Write(0x06054b50u); bw.Write((ushort)0); bw.Write((ushort)0); bw.Write((ushort)cds.Count); bw.Write((ushort)cds.Count); bw.Write((uint)(end - cd)); bw.Write((uint)cd); bw.Write((ushort)0);
            if (suffix != null) bw.Write(suffix);
            bw.Flush(); return o.ToArray();
        }

        /// <summary>a .ccshome made directly from a document and picture bytes (by member name): no validation on the way, so a test can
        /// write exactly the file a hostile sender would</summary>
        static byte[] DesignFile(JObj doc, Dictionary<string, byte[]> byFile, params (string name, byte[] data)[] extra)
        {
            var list = new List<(string, byte[], bool)> { ("README.txt", Encoding.UTF8.GetBytes(HomeDesign.Readme), true) };
            foreach (var kv in byFile) list.Add((kv.Key, kv.Value, false));
            foreach (var e in extra) list.Add((e.name, e.data, false));
            list.Add((DesignProfile.ManifestName, Json.Bytes(doc), true));
            return ZipOf(list.ToArray());
        }
        static Dictionary<string, byte[]> FilesOf(HomeDesign d) => d.Images.ToDictionary(kv => "images/" + kv.Key + (kv.Value[0] == 0x89 ? ".png" : ".jpg"), kv => kv.Value);
        /// <summary>the design's document changed by edit, written as a file and opened again (a refusal is the test's result)</summary>
        static string Mutated(HomeDesign d, Action<JObj> edit, Dictionary<string, byte[]> files = null) { var doc = d.Document; edit(doc); return Code(() => HomeDesign.Open(DesignFile(doc, files ?? FilesOf(d)))); }
        static JObj Region(JObj doc, string id) => ((List<object>)((JObj)doc["layout"])["regions"]).OfType<JObj>().First(r => r["id"] as string == id);
        /// <summary>the document with two of the HOME's own control regions placed (games, status): the format checks them; the console HOME does not move them</summary>
        static JObj WithUi(JObj doc)
        {
            var regs = (List<object>)((JObj)doc["layout"])["regions"];
            if (!regs.OfType<JObj>().Any(r => r["id"] as string == "games"))
            {
                regs.Add(new JObj { { "id", "games" }, { "kind", "ui" }, { "ui", "games" }, { "x", 40L }, { "y", 120L }, { "w", 900L }, { "h", 480L }, { "focus", 1L }, { "hit", new JObj { { "x", 40L }, { "y", 120L }, { "w", 900L }, { "h", 480L } } } });
                regs.Add(new JObj { { "id", "status" }, { "kind", "ui" }, { "ui", "status" }, { "x", 1100L }, { "y", 20L }, { "w", 150L }, { "h", 40L }, { "focus", 2L } });
            }
            return doc;
        }
        static JObj Folder(JObj doc, string id) => ((List<object>)doc["folders"]).OfType<JObj>().First(r => r["id"] as string == id);

        sealed class Reg : IPublicRegistry
        {
            public const string Game = "0123456789abcdef"; public static readonly string GameSha = CSha.Of("synthetic public game"), ModSha = CSha.Of("synthetic public mod"), CcsSha = CSha.Of("synthetic ccs-mod 1.0.0");
            public string GameTitle(string id) => id == Game ? "Example Quest" : null;
            public bool IsPublicGameHash(string id, string sha) => id == Game && sha == GameSha;
            public bool IsPublicBuild(string kind, string name, string version, string sha) => kind == "mod" && name == "example-mod" && version == "1.0" && sha == ModSha || kind == "ccs-component" && name == "ccs-mod" && version == "1.0.0" && sha == CcsSha;
        }

        // ------------------------------------------------------------------ the group
        public static void Compose()
        {
            var dir = NewDir("compose");
            ComposeJsonAndText();
            ComposeZip(dir);
            var design = ComposePictures();
            var designFile = ComposeDesign(dir, design);
            var planFile = ComposePlan(dir, design);
            ComposePrivate(dir, designFile, planFile);
        }

        static void ComposeJsonAndText()
        {
            byte[] U(string s) => new UTF8Encoding(false).GetBytes(s);
            string J(byte[] b) => Code(() => StrictJson.Parse(b, null, "test"));
            Check(J(U("{\"a\":1,\"a\":2}")) == "json.duplicate" && J(U("{\"a\":1,\"A\":2}")) == "json.duplicate", "JSON: a member named twice (also differing only in case) is refused: no two readers can see different values");
            Check(J(new byte[] { 0xEF, 0xBB, 0xBF, (byte)'{', (byte)'}' }) == "json.encoding" && J(new byte[] { (byte)'{', 0xFF, (byte)'}' }) == "json.encoding" && J(U("{\"a\":\"\\ud800\"}")) == "json.encoding",
                "JSON: a byte order mark, invalid UTF-8 and an escaped broken character are refused");
            Check(J(U(new string('[', 100) + new string(']', 100))) == "json.depth" && J(U("{\"a\":1,}")) == "json.syntax" && J(U("{/*x*/}")) == "json.syntax" && J(U("{\"a\":1e999}")) == "json.number",
                "JSON: deep nesting, trailing commas, comments and out-of-range numbers are refused");
            var keep = StrictJson.Parse(U("{\"z\":1.50,\"a\":[1,true,null,\"x\"],\"m\":{\"k\":-0.25}}"), null, "test");
            Check(Encoding.UTF8.GetString(Json.Bytes(keep)).Contains("1.50") && StrictJson.Same(keep, StrictJson.Parse(Json.Bytes(keep), null, "test")) && string.Join(",", ((JObj)keep).Select(kv => kv.Key)) == "z,a,m",
                "JSON: member order and number scale survive a read and a write");
            Check(PlainText.Check("Puzzle Night", 40).code == null && PlainText.Check("Visit https://example.com", 80).code == "text.url" && PlainText.Check("D:\\Profiles\\someone", 80).code == "text.path"
                  && PlainText.Check("\\\\server\\share", 80).code == "text.path" && PlainText.Check("<script>alert(1)</script>", 80).code == "text.markup" && PlainText.Check("0461aaaa 88180000 1e670000 6c118000", 80).code == "text.console-id"
                  && PlainText.Check(new string('a', 61), 60).code == "text.length" && PlainText.Check("abc\u202Edef", 60).code == "text.control" && PlainText.Check("cafe\u0301", 60).code == "text.encoding" && PlainText.Check(CSha.Of("x"), 80).code == "text.fingerprint",
                "plain text: links, file locations (drive and network), markup, console identities, invisible characters, unnormalized text, fingerprints and overlong text are each refused with their own code");
            Check(Display.Html("<script>alert('x')</script> & \"q\"") == "&lt;script&gt;alert(&#39;x&#39;)&lt;/script&gt; &amp; &quot;q&quot;" && Display.Line("a\u202Eb\nc  d", 40) == "a\uFFFDb c d",
                "display: text from a file is escaped for HTML and made one plain line (invisible controls shown as \uFFFD)");
            Check(Units.Size(ImageRules.FrameBytes) == "3.69 MB" && ImageRules.FrameBytes == 3686400 && Units.Size(450000) == "450 KB" && Units.Size(DesignProfile.DecodeWorkBudget) == "29.49 MB" && Units.Size(DesignProfile.ActiveDecodedBudget) == "16 MB" && Units.Size(999) == "999 B",
                "sizes in decimal units: one 1280x720 RGBA frame is 3,686,400 bytes = " + Units.Size(ImageRules.FrameBytes) + "; the decoding-work budget is " + Units.Size(DesignProfile.DecodeWorkBudget) + " and the measured memory budget " + Units.Size(DesignProfile.ActiveDecodedBudget));
        }

        static void ComposeZip(string dir)
        {
            var ok = new RawMember { Name = "README.txt", Data = Encoding.UTF8.GetBytes(HomeDesign.Readme) };
            string Z(params RawMember[] ms) => Code(() => HomeDesign.Open(RawZip(ms)));
            Check(Z(new RawMember { Name = "ccs-home-design.json", Data = new byte[900000], Deflate = true }) == "zip.ratio", "a member that expands about 1,000 times (a decompression bomb) is refused from the index, before it is decompressed");
            Check(Z(new RawMember { Name = "README.txt", Data = Encoding.ASCII.GetBytes(new string('x', 20000)), Deflate = true }) == "zip.size", "a member larger than its limit (README over 16,000 bytes) is refused before it is read");
            Check(Z(Enumerable.Range(0, 51).Select(i => new RawMember { Name = "images/" + CSha.Of("m" + i) + ".png", Data = new byte[1] }).ToArray()) == "zip.count", "more members than a design can have (51) is refused");
            var names = new (string name, string code)[]
            {
                ("images/../../evil.png", "zip.traversal"), ("/etc/passwd", "zip.absolute"), ("\\windows\\evil.png", "zip.absolute"), ("//server/share/x.png", "zip.unc"), ("\\\\server\\share\\x.png", "zip.unc"),
                ("C:/Windows/x.png", "zip.drive"), ("c:evil.png", "zip.drive"), ("images/CON.png", "zip.device"), ("NUL", "zip.device"), ("images/com1.txt", "zip.device"), ("LPT9.png", "zip.device"),
                ("images/a.png:Zone.Identifier", "zip.stream"), ("images\\a.png", "zip.backslash"), ("images//a.png", "zip.path"), ("images/./a.png", "zip.path"), ("images/a.png.", "zip.path"),
                ("images/gpj\u202Eexe.png", "zip.name"), ("images/a\u0001.png", "zip.name"),
            };
            foreach (var (n, c) in names) { var got = Z(ok, new RawMember { Name = n, Data = new byte[] { 1 } }); Check(got == c, "member name \"" + Display.Line(n, 60) + "\" is refused as " + c + " (" + got + ")"); }
            Check(Z(ok, new RawMember { Name = "images/", Data = new byte[0], ExtAttr = 0x41ED0010 }) == "zip.folder", "a folder entry is refused");
            Check(Z(ok, ok) == "zip.duplicate", "two members with the same name are refused");
            Check(Z(ok, new RawMember { Name = "readme.TXT", Data = new byte[] { 1 } }) == "zip.collision", "two names that differ only in case are refused (a Windows folder would merge them)");
            Check(Z(new RawMember { Name = "images/caf\u00e9.png", Data = new byte[] { 1 } }, new RawMember { Name = "images/cafe\u0301.png", Data = new byte[] { 2 } }) == "zip.collision", "two names equal after Unicode normalization (composed and decomposed \u00e9) are refused");
            Check(Z(ok, new RawMember { Name = "images/link.png", Data = Encoding.ASCII.GetBytes("/etc/passwd"), ExtAttr = 0xA1FF0000 }) == "zip.link", "a symbolic link entry is refused");
            Check(Z(ok, new RawMember { Name = "images/rp.png", Data = new byte[] { 1 }, MadeBy = 0x0014, ExtAttr = 0x420 }) == "zip.link", "a reparse-point entry (DOS attributes) is refused");
            Check(Z(new RawMember { Name = "README.txt", Data = new byte[] { 1 }, Flags = 0x0801 }) == "zip.encrypted", "an encrypted member is refused");
            Check(Z(new RawMember { Name = "README.txt", Data = new byte[] { 1 }, Flags = 0x0808 }) == "zip.descriptor", "a member with a data descriptor is refused");
            Check(Z(new RawMember { Name = "x", NameBytes = new byte[] { (byte)'a', 0x82, (byte)'b' }, Flags = 0 }) == "zip.name-encoding", "a non-ASCII name not marked UTF-8 (a guessed code page) is refused");
            Check(Code(() => HomeDesign.Open(RawZip(new[] { ok }, null, new byte[] { 1, 2, 3 }))) == "zip.trailing", "bytes after the end of the archive are refused");
            Check(Code(() => HomeDesign.Open(RawZip(new[] { ok }, new byte[] { 0x4D, 0x5A, 0, 0 }))) == "zip.gap", "bytes before the first member (a program glued in front) are refused");
            Check(Code(() => HomeDesign.Open(Encoding.ASCII.GetBytes("<svg xmlns='http://www.w3.org/2000/svg'/>"))) == "zip.not-zip", "a file that is not a ZIP at all is refused");
        }

        static HomeDesign ComposePictures()
        {
            // the canonical profile, read from structure only
            var png = Png(320, 180, 3); var pi = ImageRules.Inspect(png, 1280, 1280 * 720);
            Check(pi.Format == "png" && pi.Width == 320 && pi.Height == 180 && pi.Canonical && !pi.Alpha && pi.DecodedBytes == 320 * 180 * 4, "a canonical PNG: kind, size and decoded bytes read from its header; its pixel data checked");
            var apng = Png(64, 64, 4, true); Check(ImageRules.Inspect(apng, 1280, 1280 * 720).Alpha, "a PNG with see-through pixels is RGBA");
            string I(byte[] b) => Code(() => ImageRules.Inspect(b, 1280, 1280 * 720));
            Check(I(Encoding.ASCII.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\"><script>alert(1)</script></svg>")) == "image.type" && Msg(() => ImageRules.Inspect(Encoding.ASCII.GetBytes("<svg/>"), 1280, 921600)).Contains("SVG"), "an SVG named as a picture is refused and named as SVG");
            Check(I(Encoding.ASCII.GetBytes("<!doctype html><html><script>x</script></html>")) == "image.type" && I(new byte[] { 0x4D, 0x5A, 0x90, 0, 3, 0, 0, 0 }) == "image.type" && Msg(() => ImageRules.Inspect(new byte[] { 0x4D, 0x5A, 0x90, 0 }, 1280, 921600)).Contains("Windows program"),
                "HTML and a Windows program (MZ) disguised as pictures are refused and named for what they are");
            Check(I(Encoding.ASCII.GetBytes("GIF89a\u0001\0\u0001\0")) == "image.type" && I(ZipOf(("a", new byte[] { 1 }, false))) == "image.type", "GIF and ZIP are refused (only PNG and JPEG)");
            Check(I(PngWithChunk(png, "acTL", new byte[8])) == "image.animated", "an animated PNG (acTL) is refused: still pictures only");
            var big = (byte[])png.Clone(); big[16] = 0; big[17] = 0; big[18] = 0x4E; big[19] = 0x20;   // header says 20,000 pixels wide (checksum fixed below)
            var c = new Crc32(); c.Update(big, 12, 17); big[29] = (byte)(c.Value >> 24); big[30] = (byte)(c.Value >> 16); big[31] = (byte)(c.Value >> 8); big[32] = (byte)c.Value;
            Check(I(big) == "image.dimensions", "a header claiming 20,000 pixels is refused before any pixel is decoded (no allocation for it)");
            var shortPx = (byte[])png.Clone(); int idat = 33 + 8; shortPx[idat + 20] ^= 0x55; var c2 = new Crc32(); int ilen = shortPx[33] << 24 | shortPx[34] << 16 | shortPx[35] << 8 | shortPx[36]; c2.Update(shortPx, 37, ilen + 4);
            shortPx[37 + 4 + ilen] = (byte)(c2.Value >> 24); shortPx[38 + 4 + ilen] = (byte)(c2.Value >> 16); shortPx[39 + 4 + ilen] = (byte)(c2.Value >> 8); shortPx[40 + 4 + ilen] = (byte)c2.Value;
            Check(I(shortPx) == "image.damaged", "pixel data that does not inflate to exactly its size (damaged, checksum fixed) is refused");
            var meta = PngWithChunk(png, "tEXt", Encoding.ASCII.GetBytes("Author\0Alex Example"));
            var mi = ImageRules.Inspect(meta, 1280, 921600); Check(!mi.Canonical && mi.NotCanonical.Contains("tEXt"), "a PNG with text metadata is a picture but not canonical (" + mi.NotCanonical + ")");
            var jpg = GdiJpeg(400, 300, 5); var exif = WithExif(jpg);
            var ji = ImageRules.Inspect(exif, 1280, 921600); Check(ji.Format == "jpeg" && !ji.Canonical && ji.NotCanonical.Contains("EXIF"), "a JPEG with EXIF is not canonical (" + ji.NotCanonical + ")");
            Check(I(jpg.Concat(ZipOf(("payload.exe", new byte[] { 0x4D, 0x5A }, false))).ToArray()) == "image.trailing", "a JPEG with an archive glued after its end (a polyglot) is refused");
            // normalization on this PC: metadata gone, upright, fitted, canonical
            var deco = DesignProfile.Slots["decoration"];
            var n1 = ImageNormalizer.Normalize(exif, deco); var n1i = ImageRules.Inspect(n1, 1280, 921600);
            Check(n1i.Canonical && n1i.Format == "jpeg" && n1i.Width == 300 && n1i.Height == 400 && !Encoding.ASCII.GetString(n1).Contains("Exif"), "a person's JPEG with EXIF: turned upright by its orientation (400x300 -> 300x400), written as a canonical baseline JPEG without its metadata");
            var n2 = ImageNormalizer.Normalize(PngWithChunk(Png(2000, 1000, 6, true), "tEXt", Encoding.ASCII.GetBytes("Comment\0private")), deco); var n2i = ImageRules.Inspect(n2, 1280, 921600);
            Check(n2i.Canonical && n2i.Format == "png" && n2i.Alpha && n2i.Width == 1256 && n2i.Height == 628 && !Encoding.ASCII.GetString(n2).Contains("private"), "a see-through PNG with metadata: fitted inside 1256x696, kept RGBA, metadata gone");
            var scene = ImageNormalizer.Normalize(Png(1600, 1000, 7), DesignProfile.Slots["scene"]); var si = ImageRules.Inspect(scene, 1280, 921600);
            Check(si.Canonical && si.Width == 1280 && si.Height == 720 && si.Format == "jpeg", "a background picture is filled to exactly 1280x720 (centered crop) as a baseline JPEG");
            var startup = ImageNormalizer.Normalize(GdiJpeg(1920, 1080, 8, 95), DesignProfile.Slots["startup"]);
            Check(startup.Length <= 450000 && ImageRules.Inspect(startup, 1280, 921600).Width == 1280 && DesignProfile.Slots["startup"].Check(ImageRules.Inspect(startup, 1280, 921600), startup.Length).code == null,
                "the startup picture: a still 1280x720 JPEG of at most 450,000 bytes (" + startup.Length.ToString("N0") + " bytes)");
            Check(Code(() => ImageNormalizer.Normalize(Encoding.ASCII.GetBytes("<svg/>"), deco)) == "image.type" && Code(() => ImageNormalizer.Normalize(new byte[ImageRules.ImportMaxBytes + 1], deco)) == "image.size",
                "the converter refuses what is not a PNG or JPEG, and files over 25,000,000 bytes, before decoding");

            // a design made here
            var d = HomeDesign.New("Neon Night", "arcade-grid");
            d.SetSlot("scene", scene); d.SetSlot("plate", Png(600, 120, 9, true)); d.SetSlot("icon-star", Png(64, 64, 10, true));
            d.SetStartup("image", null, startup);
            var art = d.AddImage(ImageNormalizer.Normalize(Png(800, 300, 11), DesignProfile.Slots["folder-art"]));
            var sticker = d.AddImage(n2);
            d.UpsertRegion("sticker", r => { r["kind"] = "image"; r["x"] = 960L; r["y"] = 300L; r["w"] = 296L; r["h"] = 148L; r["layer"] = 2L; r["image"] = sticker; });
            d.UpsertRegion("hello", r => { r["kind"] = "label"; r["x"] = 40L; r["y"] = 40L; r["w"] = 400L; r["h"] = 40L; r["text"] = "Game Night"; r["font"] = "sans700_20"; r["color"] = "#ffcc00"; r["align"] = "left"; r["layer"] = 3L; });
            d.UpsertFolder("puzzles", f => { f["name"] = "Puzzle Night"; f["parent"] = null; f["pos"] = 0L; f["art"] = art; f["games"] = new List<object> { new GameRef { Catalog = Reg.Game, Title = "Example Quest" }.ToJson(), new GameRef { Catalog = "fedcba9876543210" }.ToJson() }; });
            d.UpsertFolder("late", f => { f["name"] = "Late Night"; f["parent"] = "puzzles"; f["pos"] = 0L; });
            d.SetOrder(new[] { new GameRef { Catalog = Reg.Game, Title = "Example Quest" }, new GameRef { Catalog = "fedcba9876543210", Title = "Home Project" }, new GameRef { Placeholder = 1 } });
            d.SetMapShortcuts("puzzles", null);
            d.AddCredit("Rance9", "Neon Night background", "CC BY 4.0", d.ImageFor("scene"));
            Check(d.CanApply && d.Images.Count == 6 && d.Budget().Ok, "a design with a background, plate, icon, startup picture, folder picture, decoration, labels, folders, order and credits validates and can be applied (" + d.Budget().Text + ")");
            // the HOME's own controls: groups that move whole in their theme, at the theme's size; another size is kept, viewable, not applicable
            var ui = HomeDesign.Open(DesignFile(WithUi(d.Document), FilesOf(d)));
            Check(!ui.CanApply && !ui.InspectOnly && ui.Unsupported.Count == 2 && ui.Unsupported.All(u => u.Contains("keeps Arcade Grid's size")) && Code(ui.RequireApplicable) == "design.not-applicable",
                "a design that gives the HOME's controls (games, status) other sizes than the theme's opens and can be kept, but is not applied (" + string.Join("; ", ui.Unsupported) + ")");
            ComposeRegions(d);
            var bigf = d.Document; ((JObj)bigf["appearance"])["title_font"] = "silkh30"; ((JObj)bigf["appearance"])["text_font"] = "cin700_46";
            var bo = HomeDesign.Open(DesignFile(bigf, FilesOf(d)));
            Check(!bo.CanApply && bo.Unsupported.Count == 2 && bo.Unsupported.Any(u => u.Contains("menu titles")) && bo.Unsupported.Any(u => u.Contains("menu text")), "menu lettering larger than the console HOME's menus lay out (a 30 px title, 46 px text) is viewable, not applicable");
            var before = d.ContentSha256;
            Check(Code(() => d.SetSlot("icon-music", Png(64, 64, 2, true))) == "design.slot" && Code(() => d.SetAppearance("css", "body{}")) == "design.key" && Code(() => d.AddImage(PngWithChunk(Png(8, 8), "tEXt", new byte[] { 65, 0, 66 }))) == "design.image"
                  && Code(() => d.SetSlot("plate", ImageNormalizer.Normalize(Png(400, 100), DesignProfile.Slots["scene"]))) == "design.slot-format" && d.ContentSha256 == before,
                "edits that would make the design invalid are refused and leave it exactly as it was (an unknown slot, a CSS setting, a picture with metadata, a JPEG where only PNG is allowed)");
            return d;
        }

        static string ComposeDesign(string dir, HomeDesign d)
        {
            var path = Path.Combine(dir, "Neon Night.ccshome"); d.Save(path);
            var back = HomeDesign.Open(path);
            Check(back.CanApply && back.ContentSha256 == d.ContentSha256 && back.Images.Count == d.Images.Count && back.Images.All(kv => d.Images[kv.Key].SequenceEqual(kv.Value)), "saved and opened again: the same design (manifest fingerprint " + d.ContentSha256.Substring(0, 12) + "), the same pictures byte for byte");
            var path2 = Path.Combine(dir, "again.ccshome"); back.Save(path2);
            Check(File.ReadAllBytes(path).SequenceEqual(File.ReadAllBytes(path2)), "the same design saves to the same bytes (both hosts get the same file for the same design)");
            Check(Code(() => d.Save(path)) == "design.exists", "saving never replaces an existing file unless asked (Save As)");
            using (var z = SafeZip.Open(path, new ZipLimits { MaxMembers = 100 })) Check(z.Entries.Select(e => e.Name).All(n => n == "README.txt" || n == DesignProfile.ManifestName || n.StartsWith("images/")) && z.Entries.Last().Name == DesignProfile.ManifestName,
                "the file holds only README.txt, the pictures and ccs-home-design.json (last)");

            // unknown optional data survives open -> change an understood setting -> save -> open
            var doc = d.Document; var poster = d.AddImage(Png(100, 50, 12));
            doc = d.Document;
            doc["optional"] = new JObj { { "glow", new JObj { { "strength", 3L }, { "tint_name", "sunset" } } }, { "poster", poster }, { "ratio", 1.50m } };
            Region(doc, "sticker")["optional"] = new JObj { { "wobble", true } };
            Folder(doc, "puzzles")["optional"] = new JObj { { "badge", "new" } };
            ((JObj)doc["slots"])["optional"] = new JObj { { "parallax", 0L } };
            var f1 = Path.Combine(dir, "future-minor.ccshome"); File.WriteAllBytes(f1, DesignFile(doc, FilesOf(d)));
            var o1 = HomeDesign.Open(f1);
            Check(o1.CanApply && o1.Images.ContainsKey(poster), "a design with unknown optional data opens normally (the data is inert, kept, never interpreted)");
            o1.SetName("Neon Night (edited)"); o1.SetAppearance("text_color", "#fffff0");
            var newScene = ImageNormalizer.Normalize(Png(1280, 720, 13), DesignProfile.Slots["scene"]); var oldScene = o1.ImageFor("scene");
            o1.SetSlot("scene", newScene);
            var f2 = Path.Combine(dir, "future-minor-edited.ccshome"); o1.Save(f2);
            var o2 = HomeDesign.Open(f2); var d2 = o2.Document;
            Check(o2.Name == "Neon Night (edited)" && StrictJson.Same(d2["optional"], doc["optional"]) && StrictJson.Same(Region(d2, "sticker")["optional"], Region(doc, "sticker")["optional"])
                  && StrictJson.Same(Folder(d2, "puzzles")["optional"], Folder(doc, "puzzles")["optional"]) && StrictJson.Same(((JObj)d2["slots"])["optional"], ((JObj)doc["slots"])["optional"]),
                "after changing the name, a color and the background and saving: every unknown optional field (top level, slot list, region, folder) is unchanged");
            Check(o2.Images.ContainsKey(poster) && o2.Images[poster].SequenceEqual(o1.Images[poster]) && o2.Images.ContainsKey(oldScene) == true,
                "pictures named only by unknown optional data stay (the poster); the replaced background stays too while the credit still names it");
            // unknown required capability / newer major: inspect and export the original, never apply, edit or downgrade
            var req = d.Document; req["requires"] = new List<object> { "animated-scenes/1" }; req["animation"] = new JObj { { "frames", 12L } };
            var rb = DesignFile(req, FilesOf(d)); var r = HomeDesign.Open(rb);
            Check(r.InspectOnly && !r.CanApply && Code(r.RequireApplicable) == "design.not-applicable" && Code(() => r.SetName("x")) == "design.inspect-only" && Code(() => r.Save(Path.Combine(dir, "r.ccshome"))) == "design.inspect-only",
                "an unknown REQUIRED capability: the design opens for viewing only; applying, editing and re-saving are refused (" + string.Join("; ", r.Unsupported) + ")");
            var exp = Path.Combine(dir, "exported-original.ccshome"); r.ExportOriginal(exp);
            Check(File.ReadAllBytes(exp).SequenceEqual(rb), "...and its original file can be exported unchanged");
            var maj = d.Document; maj["schema"] = "ccs-home-design/2"; maj["scenes3d"] = new List<object> { new JObj { { "depth", 4L } } };
            var mo = HomeDesign.Open(DesignFile(maj, FilesOf(d), ("audio/theme.ogg", new byte[] { 1, 2, 3 })));
            Check(mo.InspectOnly && !mo.CanApply && mo.Members.Any(m => m.name == "audio/theme.ogg") && mo.Name == "Neon Night", "a newer schema major with members this version does not know: viewable (name, member list), not applicable; nothing in it is decoded");
            var majBad = d.Document; majBad["schema"] = "ccs-home-design/2"; majBad["fetch"] = "https://example.com/theme.zip";
            Check(Code(() => HomeDesign.Open(DesignFile(majBad, FilesOf(d)))) == "design.url", "even a newer major is refused when it carries a link");
            var unk = d.Document; Region(unk, "hello")["font"] = "comic9000"; var uo = HomeDesign.Open(DesignFile(unk, FilesOf(d)));
            Check(!uo.CanApply && !uo.InspectOnly && uo.Unsupported.Any(u => u.Contains("comic9000")), "an unknown value in a known field (a font this version does not have) is viewable, not applicable: never silently replaced");

            // hostile documents
            Check(Mutated(d, x => x["url"] = "https://example.com/x.png") == "design.key", "a URL field is refused (a design has no link or fetch field at all)");
            Check(Mutated(d, x => x["on_apply"] = "cmd /c del") == "design.key" && Mutated(d, x => Region(x, "hello")["script"] = "alert(1)") == "design.key", "script and command fields are refused (allowlisted schema, not a field blacklist)");
            Check(Mutated(d, x => Region(x, "hello")["text"] = "Visit https://evil.example") == "design.url", "a link inside label text is refused");
            Check(Mutated(d, x => Folder(x, "puzzles")["name"] = "<script>alert(1)</script>") == "design.markup", "markup in a folder name is refused");
            Check(Mutated(d, x => ((JObj)((List<object>)x["credits"])[0])["by"] = "D:\\Profiles\\someone") == "design.path", "a file location in a credit is refused");
            Check(Mutated(d, x => ((JObj)x["design"])["name"] = "0461aaaa 88180000 1e670000 6c118000") == "design.console-id", "a console identity in the design name is refused");
            Check(Mutated(d, x => ((List<object>)x["order"]).Add(new JObj { { "catalog", "p_0123456789abcdef" } })) == "design.private-game", "a private game reference (p_...) is refused: a design names public catalog games only");
            Check(Mutated(d, x => x["optional"] = new JObj { { "note", CSha.Of("a private save") } }) == "design.fingerprint", "optional data holding a fingerprint that is not one of the design's own pictures is refused");
            Check(Mutated(d, x => x["optional"] = new JObj { { "blob", string.Join("", Enumerable.Repeat("abcdefghij ", 55)) } }) == "design.length", "optional text over 500 characters is refused (inert and bounded)");
            Check(Mutated(d, x => x["optional"] = new JObj { { "a", new JObj { { "b", new JObj { { "c", new JObj { { "d", new JObj { { "e", new JObj { { "f", new JObj { { "g", 1L } } } } } } } } } } } } } }) == "design.optional", "optional data nested deeper than 6 levels is refused");
            Check(Mutated(d, x => Region(x, "hello")["x"] = 0L) == "design.layout-bounds" && Mutated(d, x => Region(x, "hello")["x"] = 1000L) == "design.layout-bounds" && Mutated(d, x => Region(WithUi(x), "games")["y"] = 11L) == "design.layout-bounds",
                "a region at the edge, beyond it, or within 12 px of it is refused (the HOME edge rule)");
            Check(Mutated(d, x => Region(x, "hello")["text"] = new string('W', 61)) == "design.length", "label text over 60 characters is refused");
            Check(Mutated(d, x => Region(x, "hello")["font"] = "px9") == "design.layout-size", "a label in a font smaller than 13 px is refused (too small to read on a TV)");
            Check(Mutated(d, x => { Region(WithUi(x), "status")["x"] = 900L; Region(x, "status")["y"] = 200L; }) == "design.layout-overlap", "two HOME control regions that overlap are refused");
            Check(Mutated(d, x => { Region(WithUi(x), "status")["w"] = 50L; }) == "design.layout-size", "a control region smaller than readable is refused");
            Check(Mutated(d, x => Region(WithUi(x), "status")["focus"] = 1L) == "design.layout-focus", "two regions with the same focus position are refused");
            Check(Mutated(d, x => ((JObj)Region(WithUi(x), "games")["hit"])["w"] = 1000L) == "design.layout-hit", "a pointer area outside its region is refused");
            Check(Mutated(d, x => Region(x, "sticker")["layer"] = 8L) == "design.bounds", "a decoration above the highest decoration layer (over the HOME's controls) is refused");
            Check(Mutated(d, x => ((JObj)x["appearance"])["text_color"] = "#151520") == "design.contrast", "text too close to its panel color to read is refused");
            Check(Mutated(d, x => Folder(x, "puzzles")["parent"] = "late") == "design.folder-cycle", "a folder cycle is refused");
            Check(Mutated(d, x => Folder(x, "late")["parent"] = "missing") == "design.folder-parent", "a folder inside a folder the design does not have is refused");
            Check(Mutated(d, x => { var fs = (List<object>)x["folders"]; for (int i = 1; i <= 6; i++) fs.Add(new JObj { { "id", "deep" + i }, { "name", "Deep " + i }, { "parent", i == 1 ? "late" : "deep" + (i - 1) } }); }) == "design.folder-depth",
                "folders nested more than 5 levels deep are refused");
            // pictures inside the file
            var files = FilesOf(d); var sceneFile = files.Keys.First(k => k.Contains(d.ImageFor("scene")));
            Check(Code(() => HomeDesign.Open(DesignFile(d.Document, files.Where(kv => kv.Key != sceneFile).ToDictionary(kv => kv.Key, kv => kv.Value)))) == "design.missing-image", "a listed picture missing from the file is refused");
            var swapped = new Dictionary<string, byte[]>(files) { [sceneFile] = ImageNormalizer.Normalize(Png(1280, 720, 99), DesignProfile.Slots["scene"]) };
            Check(Code(() => HomeDesign.Open(DesignFile(d.Document, swapped))) == "design.image-size" || Code(() => HomeDesign.Open(DesignFile(d.Document, swapped))) == "design.image-hash", "a picture whose bytes are not the ones its fingerprint names is refused");
            var svg = Encoding.ASCII.GetBytes("<svg xmlns=\"http://www.w3.org/2000/svg\" onload=\"alert(1)\"/>"); var svgSha = CSha.Of(svg);
            Check(Mutated(d, x => ((List<object>)x["images"]).Add(new JObj { { "sha256", svgSha }, { "file", "images/" + svgSha + ".png" }, { "bytes", (long)svg.Length }, { "format", "png" }, { "width", 10L }, { "height", 10L }, { "alpha", false } }),
                new Dictionary<string, byte[]>(files) { ["images/" + svgSha + ".png"] = svg }) == "image.type", "an SVG with a script, named and listed as a PNG, is refused by its content");
            var exe = new byte[] { 0x4D, 0x5A, 0x90, 0, 3, 0, 0, 0, 4, 0 }; var exeSha = CSha.Of(exe);
            Check(Mutated(d, x => ((List<object>)x["images"]).Add(new JObj { { "sha256", exeSha }, { "file", "images/" + exeSha + ".jpg" }, { "bytes", (long)exe.Length }, { "format", "jpeg" }, { "width", 10L }, { "height", 10L }, { "alpha", false } }),
                new Dictionary<string, byte[]>(files) { ["images/" + exeSha + ".jpg"] = exe }) == "image.type", "a program named and listed as a JPEG is refused by its content");
            var exifSha = CSha.Of(WithExif(GdiJpeg(64, 64, 3)));
            Check(Mutated(d, x => ((List<object>)x["images"]).Add(new JObj { { "sha256", exifSha }, { "file", "images/" + exifSha + ".jpg" }, { "bytes", (long)WithExif(GdiJpeg(64, 64, 3)).Length }, { "format", "jpeg" }, { "width", 64L }, { "height", 64L }, { "alpha", false } }),
                new Dictionary<string, byte[]>(files) { ["images/" + exifSha + ".jpg"] = WithExif(GdiJpeg(64, 64, 3)) }) == "design.image-profile", "a received JPEG with EXIF metadata is refused (received pictures are never converted)");
            Check(Code(() => HomeDesign.Open(DesignFile(d.Document, files, ("images/" + CSha.Of("x") + ".exe", new byte[] { 0x4D, 0x5A })))) == "zip.unlisted" && Code(() => HomeDesign.Open(DesignFile(d.Document, files, ("images/notes.txt", new byte[] { 1 })))) == "zip.unlisted",
                "a member that is not one of the listed pictures is refused");
            var badStartup = d.Document; var pngStart = Png(1280, 720, 14); var pss = CSha.Of(pngStart);
            ((List<object>)badStartup["images"]).Add(new JObj { { "sha256", pss }, { "file", "images/" + pss + ".png" }, { "bytes", (long)pngStart.Length }, { "format", "png" }, { "width", 1280L }, { "height", 720L }, { "alpha", false } });
            ((JObj)badStartup["startup"])["image"] = pss;
            Check(Code(() => HomeDesign.Open(DesignFile(badStartup, new Dictionary<string, byte[]>(files) { ["images/" + pss + ".png"] = pngStart }))) == "design.slot-format", "a PNG as the startup picture is refused (a still JPEG only)");
            var bigJpeg = GdiJpeg(1280, 720, 15, 90, true); var bjs = CSha.Of(bigJpeg); var big = d.Document;
            ((List<object>)big["images"]).Add(new JObj { { "sha256", bjs }, { "file", "images/" + bjs + ".jpg" }, { "bytes", (long)bigJpeg.Length }, { "format", "jpeg" }, { "width", 1280L }, { "height", 720L }, { "alpha", false } });
            ((JObj)big["startup"])["image"] = bjs;
            Check(bigJpeg.Length > 450000 && Code(() => HomeDesign.Open(DesignFile(big, new Dictionary<string, byte[]>(files) { ["images/" + bjs + ".jpg"] = bigJpeg }))) == "design.slot-bytes", "a startup JPEG over 450,000 bytes is refused (" + bigJpeg.Length.ToString("N0") + " bytes)");
            // the decoded picture budget: what the renderer would hold, not the file size
            var heavy = HomeDesign.New("Heavy", "featured-shelf");
            for (int i = 0; i < 9; i++) { var img = heavy.AddImage(ImageRules.StripJpeg(GdiJpeg(1256, 696, 20 + i))); var id = "deco" + i; heavy.UpsertRegion(id, rr => { rr["kind"] = "image"; rr["x"] = 12L; rr["y"] = 12L; rr["w"] = 1256L; rr["h"] = 696L; rr["layer"] = (long)(i % 8); rr["image"] = img; }); }
            var hb = heavy.Budget();
            Check(!heavy.CanApply && !hb.Ok && hb.DecodeWorkBytes == 9L * 1256 * 696 * 4 && heavy.Unsupported.Any(u => u.Contains("decoding")), "nine full-size decorations need " + Units.Size(hb.DecodeWorkBytes) + " of decoding at each start: over the budget, so the design is viewable but not applicable");
            // sharing: public catalog games keep their titles; every other game becomes a numbered placeholder
            var sp = d.PrepareShare(id => id == Reg.Game ? "Example Quest" : null, new[] { "Alex Example", "ALEX-PC" });
            var shared = Encoding.UTF8.GetString(sp.Shared.ManifestBytes());
            Check(sp.CanShare && !shared.Contains("fedcba9876543210") && !shared.Contains("Home Project") && shared.Contains(Reg.Game) && sp.Shared.Order.Count(g => g.IsPlaceholder) == 2 && sp.Removed.Any(x => x.Contains("1 of your own game")),
                "sharing a design: the game not in the public catalog (and its title) becomes \"Your game 2\"; the public one keeps its catalog reference (" + string.Join(" | ", sp.Removed) + ")");
            var priv = d.PrepareShare(id => null, new[] { "Neon Night" });
            Check(!priv.CanShare && priv.Refusals.Any(x => x.Contains("private details")), "a design whose text holds one of the person's private details is not shared until it is changed");
            var match = sp.Shared.MatchGames(id => id == Reg.Game);
            Check(match.Available.Count == 1 && match.Missing.Count == 0 && match.Placeholders.Count == 2, "receiving: the games this computer has, lacks and the placeholders are listed for explicit mapping (nothing added or removed)");
            var shareFile = Path.Combine(dir, "shared.ccshome"); sp.Shared.Save(shareFile);
            return shareFile;
        }

        static string ComposePlan(string dir, HomeDesign design)
        {
            var reg = new Reg(); var tag = ConsoleIdentity.HwidTag(HwidA); var privSha = CSha.Of("synthetic private homebrew");
            var draft = new CompositionDraft
            {
                PlanId = NewId.Make(), Title = "Living room setup", Notes = "remember: the blue cable is flaky", StartingPoint = "like-my-other-console", InstallPolicy = "ccs-mod",
                SourceConsoleHwid = HwidA, SourceConsoleTag = tag, PrivateSourceManifest = @"D:\Profiles\alex\Workbench\sources.json",
                Screenshots = { @"D:\Profiles\alex\Pictures\home.png" }, Attachments = { @"D:\Profiles\alex\notes.txt" },
                Environments = { new DraftEnvironment { Id = "orig", Kind = "nintendo-original" }, new DraftEnvironment { Id = "ccs", Kind = "ccs-home", Default = true } },
                Games =
                {
                    new DraftGame { CatalogId = Reg.Game, Title = "My favorite", ContentSha256 = Reg.GameSha, LocalPath = @"D:\Profiles\alex\Games\Example Quest.nes", FileName = "Example Quest (copy).nes", Environment = "ccs", BringSave = true, SaveSha256 = CSha.Of("save bytes"), SavePath = @"D:\Profiles\alex\Saves\eq.sav" },
                    new DraftGame { PersonalId = "p_" + privSha.Substring(0, 16), ContentSha256 = privSha, Title = "Secret Homebrew WIP", FileName = "secret-homebrew-wip.nes", LocalPath = @"D:\dev\secret-homebrew-wip.nes", Environment = "ccs" },
                },
                Mods = { new DraftMod { Name = "example-mod", Version = "1.0", Sha256 = Reg.ModSha, Environment = "ccs" }, new DraftMod { Name = "my-private-tweak", Version = "0.1", Sha256 = CSha.Of("private mod"), LocalPath = @"D:\Profiles\alex\mods\tweak.hmod", Environment = "ccs" } },
                Inputs =
                {
                    new DraftInput { Role = "console-system", Sha256 = CSha.Of("console-keyed kernel"), SourceBackupId = "20261114-173512-1a2b3c4d", LocalPath = @"D:\Profiles\alex\Backups\NES Classic Backup.zip", Environment = "orig" },
                    new DraftInput { Role = "console-saves", Sha256 = CSha.Of("saves blob") },
                    new DraftInput { Role = "ccs-component", Ref = "ccs-mod", Version = "1.0.0", Sha256 = Reg.CcsSha },
                    new DraftInput { Role = "hakchi-kernel", Version = "3.9.3", Sha256 = CSha.Of("unconfirmed kernel build") },
                },
                HomeDesign = new DraftDesign { Id = design.Id, Revision = design.Revision, Sha256 = design.ContentSha256, Name = "Neon Night" },
            };
            var pv = new PrivateValues().Add("Alex Example", "ALEX-PC", @"D:\Profiles\alex");
            var p = CompositionShare.Prepare(draft, reg, pv);
            var text = p.Json == null ? "" : Encoding.UTF8.GetString(p.Json);
            var leaks = new[] { HwidA, tag, ConsoleIdentity.NormalizeHwid(HwidA), "p_" + privSha.Substring(0, 16), privSha, "secret-homebrew", "Secret Homebrew", "D:\\\\dev", CSha.Of("save bytes"), "alex", "Users", "flaky",
                                "sources.json", "home.png", "notes.txt", CSha.Of("console-keyed kernel"), CSha.Of("saves blob"), "20261114-173512-1a2b3c4d", CSha.Of("private mod"), "my-private-tweak", CSha.Of("unconfirmed kernel build"), "My favorite", "(copy)" }
                        .Where(x => text.IndexOf(x, StringComparison.OrdinalIgnoreCase) >= 0).ToList();
            Check(p.CanShare && leaks.Count == 0, "the shared plan holds none of the private details (console identity and tag, private game id/hash/name/file, save and console-software fingerprints, backup id, notes, locations, screenshots, attachments, private mod, unconfirmed build) (" + string.Join(", ", leaks) + string.Join(" ", p.Refusals) + ")");
            Check(text.Contains(Reg.Game) && text.Contains("Example Quest") && text.Contains(Reg.GameSha) && text.Contains(Reg.ModSha) && text.Contains(Reg.CcsSha) && text.Contains(design.Id) && text.Contains(design.ContentSha256) && text.Contains("\"bring-own\""),
                "it keeps the choices and public references: the catalog game (public title and public fingerprint), the public mod build, the CCS component build, the HOME design by id and fingerprint, the save choice");
            var removedNeeded = new[] { "notes", "identity", "screenshot", "attachment", "private list", "of your own games", "Fingerprints of 1 save", "console's own software", "backups", "file location", "not public builds", "cannot confirm" };
            var missing = removedNeeded.Where(x => !p.Removed.Any(r => r.Contains(x))).ToList();
            Check(missing.Count == 0 && p.Included.Count >= 4, "the share preview lists what is included (" + p.Included.Count + " lines) and every class of what was removed (" + string.Join(" | ", p.Removed) + (missing.Count > 0 ? "; MISSING: " + string.Join(", ", missing) : "") + ")");
            Check(p.Shared.CanApply && !Composition.CanAuthorizeWrites && !Composition.IsRestorePoint && StrictJson.Same(Composition.Parse(p.Json).Document, p.Shared.Document), "the shared plan reads back as a valid plan that cannot authorize a write or be a restore point");
            foreach (var (title, why) in new[] { ("Setup for console " + tag, "a console tag"), (@"D:\Profiles\alex\plan", "a file location"), ("see https://example.com", "a link"), ("Alex Example's setup", "the person's name") })
            {
                var dd = draft; var keep = dd.Title; dd.Title = title; var pp = CompositionShare.Prepare(dd, reg, pv); dd.Title = keep;
                Check(!pp.CanShare && pp.Refusals.Any(x => x.StartsWith("Change the plan's name")), "a plan name holding " + why + " is not shared until it is changed (" + string.Join(" ", pp.Refusals) + ")");
            }
            var planPath = Path.Combine(dir, "Living room setup.ccsplan"); CompositionShare.Write(p, planPath);
            Check(ShareGuard.RequireShareable(planPath) == FileRole.SharedComposition, "the written plan passes the share gate as a Shared Composition Plan");

            // receiving: untrusted plans
            JObj Doc0() => (JObj)StrictJson.Parse(p.Json, null, "t");
            string Pc(Action<JObj> edit) { var x = Doc0(); edit(x); return Code(() => Composition.Parse(Json.Bytes(x))); }
            JObj G0(JObj x) => (JObj)((List<object>)x["games"])[0];
            Check(Pc(x => x["url"] = "https://example.com/firmware.bin") == "composition.key" && Pc(x => x["download"] = "x") == "composition.key" && Pc(x => ((JObj)x["home_design"])["download_url"] = "x") == "composition.key",
                "URL and download fields are refused anywhere (a plan has no fetch field at all; no Nintendo link can exist in one)");
            Check(Pc(x => x["script"] = "rm -rf /") == "composition.key" && Pc(x => G0(x)["command"] = "powershell -enc AAAA") == "composition.key" && Pc(x => x["exec"] = new JObj()) == "composition.key",
                "script, command and executable fields are refused");
            Check(Pc(x => x["confirm"] = true) == "composition.key" && Pc(x => x["restore_point"] = "x") == "composition.key" && Pc(x => x["admission"] = "x") == "composition.key",
                "a plan cannot carry a confirmation, an admission or a restore point (those exist only for a real console, in its own session)");
            Check(Pc(x => ((JObj)x["plan"])["title"] = "0461aaaa 88180000 1e670000 6c118000") == "composition.console-id" && Pc(x => ((JObj)x["plan"])["title"] = "\\\\nas\\share\\plan") == "composition.path" && Pc(x => ((JObj)x["plan"])["title"] = "C:\\plans\\a") == "composition.path",
                "a console identity, a UNC path or a drive path in a plan's text is refused");
            Check(Pc(x => ((JObj)G0(x)["ref"])["catalog"] = "p_0123456789abcdef") == "composition.private-game", "a private game reference is refused");
            Check(Pc(x => ((List<object>)x["inputs"]).Add(new JObj { { "role", "console-system" }, { "supply", "recipient" }, { "sha256", CSha.Of("k") } })) == "composition.hash-place"
                  && Pc(x => ((List<object>)x["inputs"]).Add(new JObj { { "role", "console-saves" }, { "supply", "plan" } })) == "composition.hash-place",
                "a fingerprint of console-specific software, or a plan claiming to supply someone's console software or saves, is refused");
            Check(Pc(x => G0(x)["save_sha256"] = CSha.Of("s")) == "composition.key", "a save fingerprint has no place in a plan (refused)");
            Check(Pc(x => x["optional"] = new JObj { { "mirror", "https://example.com/x" } }) == "composition.url" && Pc(x => x["optional"] = new JObj { { "h", CSha.Of("x") } }) == "composition.fingerprint",
                "optional data with a link or a fingerprint is refused");
            var pkgBlob = Convert.ToBase64String(Cipher(4096)); var big = Pc(x => x["optional"] = new JObj { { "payload", pkgBlob } });
            Check((big == "json.size" || big == "composition.length") && Pc(x => x["optional"] = new JObj { { "payload", pkgBlob.Substring(0, 600) } }) == "composition.length" && Pc(x => x["payload"] = pkgBlob.Substring(0, 100)) == "composition.key",
                "a plan cannot carry file contents: a private package's bytes as text are refused (too long for any text: " + big + "; over 500 characters as optional data; no payload field exists)");
            // forward compatibility
            var fut = Doc0(); fut["optional"] = new JObj { { "layout_hint", "compact" }, { "level", 2L } }; ((JObj)fut["plan"])["optional"] = new JObj { { "color_tag", "blue" } };
            var fc = Composition.Parse(Json.Bytes(fut)); fc.SetTitle("Den setup"); var fb = Composition.Parse(fc.ToBytes());
            Check(fb.Title == "Den setup" && StrictJson.Same(fb.Document["optional"], fut["optional"]) && StrictJson.Same(((JObj)fb.Document["plan"])["optional"], ((JObj)fut["plan"])["optional"]), "unknown optional data survives open -> edit the title -> save -> open");
            var req = Doc0(); req["requires"] = new List<object> { "boot-chooser/2" }; req["boot_chooser"] = new JObj { { "timeout", 5L } };
            var rc = Composition.Parse(Json.Bytes(req));
            Check(rc.InspectOnly && !rc.CanApply && Code(rc.RequireApplicable) == "composition.not-applicable" && Code(() => rc.SetTitle("x")) == "composition.inspect-only", "an unknown required capability: viewable, not usable or editable");
            var maj = Doc0(); maj["schema"] = "ccs-composition/2"; Check(Composition.Parse(Json.Bytes(maj)).InspectOnly, "a newer schema major: viewable only");
            var en = Doc0(); en["starting_point"] = "clone-from-cloud"; var ec = Composition.Parse(Json.Bytes(en));
            Check(!ec.CanApply && !ec.InspectOnly && ec.Unsupported.Any(u => u.Contains("clone-from-cloud")), "an unknown starting point is reported, not silently replaced, and blocks use");
            Check(Pc(x => x["kind"] = "private-plan") == "composition.format", "only a shared plan is accepted on this path (never a private plan or package)");
            var list = p.Shared.Checklist(reg, id => false, (role, name, v) => role == "ccs-component", (id, sha) => false);
            Check(list.Any(i => i.Status == "supply-your-own" && i.What.Contains("original software")) && list.Any(i => i.Status == "missing" && i.What.Contains("Example Quest")) && list.Any(i => i.Status == "supply-your-own" && i.What.StartsWith("Your game"))
                  && list.Any(i => i.Status == "ready" && i.What.StartsWith("ccs-mod")) && list.Any(i => i.Status == "missing" && i.What.StartsWith("HOME design")) && list.Any(i => i.What.StartsWith("Mod 1")),
                "the receiver's checklist: their own console software and saves, games they lack, placeholders to map, CCS parts ready, the design file to open, mods to supply (" + list.Count + " items)");
            var mis = Doc0(); G0(mis)["content_sha256"] = CSha.Of("another version"); var ml = Composition.Parse(Json.Bytes(mis)).Checklist(reg, id => true, (a, b, c) => true, (a, b) => true);
            Check(ml.Any(i => i.Status == "mismatched"), "a plan naming a different version of a catalog game is reported as mismatched");
            return planPath;
        }

        static void ComposePrivate(string dir, string designFile, string planFile)
        {
            var tag = ConsoleIdentity.HwidTag(HwidA);
            var samples = new Dictionary<string, byte[]>
            {
                ["root-squashfs"] = SquashFs(), ["encrypted-partition"] = Cipher(), ["filesystem-image"] = Ext4Image(), ["kernel"] = Kernel(),
                ["boot0"] = BootImage.Synthetic(true, 24576, "boot0"), ["boot1"] = BootImage.Synthetic(false, 65536, "uboot", 4096), ["game-nes"] = NesGame(), ["save-battery"] = Cipher(8192),
                ["mod-overlay"] = Gz(Tar("install", Encoding.ASCII.GetBytes("#!/bin/sh\necho synthetic\n"))),
                ["fs-metadata"] = Encoding.UTF8.GetBytes("{\"schema\":\"ccs-fs-metadata/1\",\"entries\":[{\"path\":\"games/example.nes\",\"type\":\"f\",\"mode\":420,\"uid\":0,\"gid\":0,\"size\":40976}]}"),
                ["ccs-collection"] = ZipOf(("ccs-manifest.json", Encoding.UTF8.GetBytes("{\"format\":\"classic-cart-studio-package\",\"schema\":\"ccs-package/2\",\"kind\":\"collection\",\"files\":[]}"), true)),
            };
            foreach (var kv in samples) Check(PrivateRoles.Check(kv.Key, new MemoryStream(kv.Value)) == null, "role " + kv.Key + ": the synthetic sample passes its content check");
            string R(string role, byte[] b) => PrivateRoles.Check(role, new MemoryStream(b)) ?? "ok";
            var flipped = BootImage.Synthetic(true, 24576, "boot0"); flipped[1000] ^= 1;
            var wrong = new (string role, byte[] data, string why)[]
            {
                ("kernel", SquashFs(), "a SquashFS root given as a kernel"), ("root-squashfs", Kernel(), "a kernel (ANDROID!) given as a .hsqs root"), ("root-squashfs", Cipher(4096), "random bytes given as a root"),
                ("encrypted-partition", SquashFs().Concat(new byte[1024 * 1024 - 4096]).ToArray(), "a readable SquashFS given as the encrypted partition"), ("encrypted-partition", new byte[1024 * 1024], "zeros given as the encrypted partition"),
                ("filesystem-image", Cipher(65536), "random bytes given as ext4"), ("boot0", flipped, "boot0 with a wrong checksum"), ("boot1", BootImage.Synthetic(true, 24576, "b"), "a boot0 given as boot1"),
                ("game-nes", NesGame().Skip(16).ToArray(), "a game without its header"), ("game-nes", NesGame(2, 1, 100), "a game longer than its header declares"), ("save-battery", NesGame(), "a game given as a save"),
                ("save-battery", Encoding.ASCII.GetBytes("FCSX").Concat(new byte[8188]).ToArray(), "a save state (FCSX) given as a battery save"), ("save-battery", new byte[5000], "a save of an impossible size"),
                ("mod-overlay", Cipher(2048), "random bytes given as a mod"), ("fs-metadata", Encoding.UTF8.GetBytes("{\"schema\":\"ccs-fs-metadata/1\",\"entries\":[{\"path\":\"../etc/shadow\",\"type\":\"f\",\"mode\":420,\"uid\":0,\"gid\":0,\"size\":1}]}"), "metadata with a path leading outside"),
                ("ccs-collection", ZipOf(("other.json", new byte[] { 1 }, false)), "a ZIP that is not a collection package"),
            };
            foreach (var (role, data, why) in wrong) { var got = R(role, data); Check(got != "ok", why + " is refused by content: " + got); }

            var spec = new PrivatePackageSpec { DestinationHwid = HwidA, InstallPolicy = "ccs-mod", StartingPoint = "rebuild-from-backups", Environments = { ("e1", "nintendo-original"), ("e2", "ccs-home") },
                CompositionRef = new JObj { { "id", Composition.Parse(File.ReadAllBytes(planFile)).Id }, { "revision", 1L }, { "sha256", CSha.Of(File.ReadAllBytes(planFile)) } } };
            JObj Prov(string kind) => kind == "console-backup" ? new JObj { { "kind", kind }, { "backup_id", "20261115-101500-" + tag }, { "backup_member", "restore/nandb.img" }, { "console_tag", tag } } : new JObj { { "kind", kind } };
            var places = new Dictionary<string, string> { ["root-squashfs"] = "environment:e1/root", ["encrypted-partition"] = "partition:system", ["filesystem-image"] = "partition:data", ["kernel"] = "kernel:spare", ["boot0"] = "same-console-repair", ["boot1"] = "reference",
                ["game-nes"] = "environment:e2/games", ["save-battery"] = "environment:e2/saves", ["mod-overlay"] = "environment:e2/mods", ["fs-metadata"] = "environment:e2/metadata", ["ccs-collection"] = "environment:e2/collection" };
            foreach (var kv in samples) spec.Members.Add(new PrivateMemberSource { Role = kv.Key, Place = places[kv.Key], Bytes = kv.Value, Provenance = Prov(kv.Key == "boot0" || kv.Key == "encrypted-partition" ? "console-backup" : kv.Key == "mod-overlay" ? "public-build" : "user-file") });
            var pkg = Path.Combine(dir, PrivatePackage.FileStem(new DateTime(2026, 11, 15, 10, 0, 0), tag) + ".zip");
            var info = PrivatePackage.Write(pkg, spec);
            Check(info.Members.Count == samples.Count && info.Label.StartsWith("PRIVATE") && (bool)info.Manifest["private"] && info.DestinationTag == tag && info.Members.All(m => m.Name == PrivateRoles.MemberName(m.Role, m.Sha256) && m.Provenance != null && m.Places.Count == 1),
                "a private restore package of every role: each member bound by role, size, SHA-256, provenance and target place; labeled PRIVATE; bound to its destination console");
            using (var z = SafeZip.Open(pkg, new ZipLimits { MaxMembers = 100, MaxArchiveBytes = long.MaxValue, MaxTotalBytes = long.MaxValue, MaxMemberBytes = n => long.MaxValue }))
                Check(z.Entries.Last().Name == PrivatePackage.ManifestName && z.Entries.Any(e => e.Name == PrivatePackage.ReadmeName) && Encoding.UTF8.GetString(z.ReadAll(z.Find(PrivatePackage.ReadmeName), 64000)).StartsWith("PRIVATE"), "its README says PRIVATE first; the manifest is the last member");
            Check(Path.GetFileName(pkg).StartsWith("PRIVATE"), "its file name starts with PRIVATE");
            // refusals while building
            PrivatePackageSpec With(Action<PrivatePackageSpec> change) { var s = new PrivatePackageSpec { DestinationHwid = HwidA, InstallPolicy = "ccs-mod", StartingPoint = "fresh-ccs", Environments = { ("e1", "ccs-home") } }; change(s); return s; }
            string W(PrivatePackageSpec s, string name = null) => Code(() => PrivatePackage.Write(Path.Combine(dir, (name ?? Guid.NewGuid().ToString("N").Substring(0, 8)) + ".zip"), s));
            Check(W(With(s => s.Members.Add(new PrivateMemberSource { Role = "root-squashfs", Place = "environment:e1/root", Bytes = Kernel() }))) == "private.role", "a member whose content is not its role (a kernel given as a root) stops the build; nothing is saved");
            Check(W(With(s => s.Members.Add(new PrivateMemberSource { Role = "boot1", Place = "same-console-repair", Bytes = samples["boot1"], Provenance = new JObj { { "kind", "console-backup" }, { "console_tag", ConsoleIdentity.HwidTag(HwidB) } } }))) == "private.boot-transplant",
                "a boot stage from ANOTHER console's backup as repair material is refused (no boot-chain transplant)");
            Check(W(With(s => s.Members.Add(new PrivateMemberSource { Role = "boot0", Place = "partition:system", Bytes = samples["boot0"] }))) == "private.place", "a boot stage aimed at an ordinary storage place is refused (ordinary composed restores never write boot stages)");
            Check(Code(() => PrivatePackage.Write(Path.Combine(dir, "x" + DesignProfile.Extension), With(s => s.Members.Add(new PrivateMemberSource { Role = "game-nes", Place = "environment:e1/games", Bytes = NesGame() })))) == "private.extension"
                  && Code(() => PrivatePackage.Write(Path.Combine(dir, "x" + Composition.Extension), With(s => { }))) == "private.extension", "a private package is never saved under a shared file type (.ccshome, .ccsplan)");
            Check(Code(() => PrivatePackage.Write(pkg, spec)) == "private.exists", "an existing package is never overwritten");
            // refusals while opening
            var tampered = Path.Combine(dir, "tampered.zip"); File.Copy(pkg, tampered);
            long at; using (var z = SafeZip.Open(tampered, new ZipLimits { MaxMembers = 100, MaxArchiveBytes = long.MaxValue, MaxTotalBytes = long.MaxValue, MaxMemberBytes = n => long.MaxValue })) at = z.Find(info.Members.First(m => m.Role == "game-nes").Name).DataOffset + 100;
            using (var f = new FileStream(tampered, FileMode.Open, FileAccess.ReadWrite)) { f.Position = at; int b = f.ReadByte(); f.Position = at; f.WriteByte((byte)(b ^ 0xFF)); }
            var tc = Code(() => PrivatePackage.Open(tampered)); Check(tc == "private.damaged" || tc == "private.hash", "a payload byte changed after building is found when the package is opened (" + tc + ")");
            string Rewrite(Func<JObj, JObj> edit, Func<string, bool> keep = null)
            {
                var o = Path.Combine(dir, "rw-" + Guid.NewGuid().ToString("N").Substring(0, 6) + ".zip"); var list = new List<(string, byte[], bool)>();
                using (var z = SafeZip.Open(pkg, new ZipLimits { MaxMembers = 100, MaxArchiveBytes = long.MaxValue, MaxTotalBytes = long.MaxValue, MaxMemberBytes = n => long.MaxValue }))
                    foreach (var e in z.Entries)
                    {
                        if (keep != null && !keep(e.Name)) continue;
                        var data = z.ReadAll(e, int.MaxValue);
                        if (e.Name == PrivatePackage.ManifestName) data = Json.Bytes(edit((JObj)StrictJson.Parse(data, new JsonLimits { MaxBytes = 16000000 }, "m")));
                        list.Add((e.Name, data, e.Method == 8));
                    }
                File.WriteAllBytes(o, ZipOf(list.ToArray())); return Code(() => PrivatePackage.Open(o));
            }
            Check(Rewrite(m => { var c = (List<object>)((JObj)m["target_plan"])["components"]; c.RemoveAll(x => ((JObj)x)["role"] as string == "mod-overlay"); return m; }) == "private.unexplained", "a payload member the target plan does not use is refused (no unexplained payload)");
            Check(Rewrite(m => { m["private"] = false; return m; }) == "private.label", "a package not labeled private is refused");
            Check(Rewrite(m => m, n => !n.StartsWith("payload/games/")) == "private.missing", "a listed member missing from the file is refused");
            Check(Rewrite(m => { ((JObj)m["destination"])["tag"] = "00000000"; return m; }) == "private.destination", "a destination whose tag does not match its identity is refused");

            // the share gate decides by content
            Check(Code(() => ShareGuard.RequireShareable(pkg)) == "share.private" && ShareGuard.Classify(pkg) == FileRole.PrivateRestorePackage, "the share gate refuses the private package");
            foreach (var ext in new[] { ".ccshome", ".ccsplan", ".jpg" }) { var renamed = Path.Combine(dir, "renamed" + ext); File.Copy(pkg, renamed, true); Check(Code(() => ShareGuard.RequireShareable(renamed)) == "share.private", "renamed to " + ext + ", it is still refused (decided by content, not by name)"); }
            var mani = Path.Combine(dir, "manifest-only.json");
            using (var z = SafeZip.Open(pkg, new ZipLimits { MaxMembers = 100, MaxArchiveBytes = long.MaxValue, MaxTotalBytes = long.MaxValue, MaxMemberBytes = n => long.MaxValue })) File.WriteAllBytes(mani, z.ReadAll(z.Find(PrivatePackage.ManifestName), 16000000));
            Check(Code(() => ShareGuard.RequireShareable(mani)) == "share.private", "its manifest on its own (destination identity, plan, fingerprints) is refused too");
            var backupLike = Path.Combine(dir, "backup-like.zip"); File.WriteAllBytes(backupLike, ZipOf((BackupFormat.ManifestName, Encoding.UTF8.GetBytes("{}"), true)));
            var ccs = Path.Combine(dir, "collection.ccs"); File.WriteAllBytes(ccs, samples["ccs-collection"]);
            var junk = Path.Combine(dir, "junk.bin"); File.WriteAllBytes(junk, Cipher(1000));
            Check(Code(() => ShareGuard.RequireShareable(backupLike)) == "share.private" && Code(() => ShareGuard.RequireShareable(ccs)) == "share.collection" && Code(() => ShareGuard.RequireShareable(junk)) == "share.unknown",
                "a console backup is refused as private, a collection package as carrying game files, anything else as unknown");
            Check(ShareGuard.RequireShareable(designFile) == FileRole.SharedHomeDesign && ShareGuard.RequireShareable(planFile) == FileRole.SharedComposition, "a Shared HOME Design and a Shared Composition Plan pass the gate");
            var designPlus = Path.Combine(dir, "design-with-package.ccshome"); var dd = HomeDesign.Open(designFile);
            File.WriteAllBytes(designPlus, DesignFile(dd.Document, FilesOf(dd), ("images/package.zip", File.ReadAllBytes(pkg))));
            Check(Code(() => ShareGuard.RequireShareable(designPlus)) == "zip.unlisted", "a design with a private package tucked inside is refused at the share gate (not one of its listed pictures)");
            Check(CompositionShare.Prepare(new CompositionDraft { Title = "x", StartingPoint = "fresh-ccs", InstallPolicy = "ccs-mod", Environments = { new DraftEnvironment { Id = "e", Kind = "ccs-home" } }, Attachments = { pkg } }, new Reg(), new PrivateValues()).Json is byte[] pj
                  && !Encoding.UTF8.GetString(pj).Contains("PRIVATE") && !Encoding.UTF8.GetString(pj).Contains(Path.GetFileName(pkg)), "a private package attached to a plan's draft is removed from the shared plan (an attachment); a plan has no field to carry it");
        }
    }
}
