// Classic Cart Studio console backup and restore engine. Copyright (C) 2026 Rance9. GPL-3.0-or-later.
// Still pictures for CCS HOME designs (COMPOSER-FORMATS.md, "Pictures"). The console's HOME renderer decodes PNG and JPEG only
// (stb_image, tools/console-simulator/core/gfx.c), so those are the only kinds a design carries, in one small canonical profile:
//   PNG   8 bits per channel, RGB or RGBA, not interlaced; chunks IHDR, IDAT (consecutive), IEND only; every chunk checksum correct;
//         the pixel data inflates to exactly height x (1 + width x channels) bytes with a valid filter byte on every row
//   JPEG  baseline (SOF0), 8-bit, 1 or 3 components, one scan; an optional JFIF APP0; no other APPn (EXIF, XMP, ICC), no comment;
//         nothing after the end marker
// Inspect decides a file's kind from its first bytes (never its name), reads the dimensions from the header, refuses a picture over the
// size limits BEFORE any pixel is decoded, and says whether it is already canonical. A file that is something else (SVG, HTML, a program,
// an archive, GIF, WebP, an animated PNG, CMYK or lossless JPEG) is refused with what it really is.
// ImageNormalizer turns a person's own PNG or JPEG (at most 25,000,000 bytes, 12,000 pixels a side and 50,000,000 pixels: the startup
// picture picker's limits) into the canonical profile for one slot on this PC: decoded once here (Windows' own decoder, after the checks
// above), turned upright by its EXIF orientation, fitted to the slot, and written again, which drops every piece of metadata. A picture
// with see-through pixels becomes an RGBA PNG where the slot allows it; any other becomes a baseline JPEG (or an RGB PNG for PNG-only
// slots). Pictures received in a design are never converted: they must already be canonical, so their fingerprints stay theirs.
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace CcsBackup.Compose
{
    public sealed class ImageInfo
    {
        public string Format;          // "png" or "jpeg"
        public int Width, Height, BitDepth, ColorType, Components;
        public bool Alpha, Interlaced, Progressive;
        public bool Canonical;         // already in the console profile
        public string NotCanonical;    // why not (metadata, progressive, 16-bit, palette, ...)
        /// <summary>what the renderer holds once decoded: 4 bytes a pixel (RGBA), whatever the file's own format</summary>
        public long DecodedBytes => (long)Width * Height * 4;
    }

    /// <summary>where a picture goes in a design and what it must be there</summary>
    public sealed class ImageSlotRule
    {
        public string Id, Text; public int MaxW, MaxH; public bool Exact, Png, Jpeg, Alpha; public long MaxBytes;
        public (string code, string why) Check(ImageInfo i, long bytes)
        {
            if (i.Format == "png" && !Png || i.Format == "jpeg" && !Jpeg) return ("design.slot-format", "must be a " + (Png ? "PNG" : "JPEG") + " picture");
            if (Exact && (i.Width != MaxW || i.Height != MaxH)) return ("design.slot-size", "must be exactly " + MaxW + "x" + MaxH + " pixels (it is " + i.Width + "x" + i.Height + ")");
            if (!Exact && (i.Width > MaxW || i.Height > MaxH)) return ("design.slot-size", "must be at most " + MaxW + "x" + MaxH + " pixels (it is " + i.Width + "x" + i.Height + ")");
            if (i.Alpha && !Alpha) return ("design.slot-alpha", "cannot have see-through pixels");
            if (bytes > MaxBytes) return ("design.slot-bytes", "must be at most " + Units.Size(MaxBytes) + " (it is " + Units.Size(bytes) + ")");
            return (null, null);
        }
    }

    public static class ImageRules
    {
        public const long FrameBytes = 1280L * 720 * 4;   // one full RGBA frame: 3,686,400 bytes (3.69 MB)
        public const int ImportMaxBytes = 25000000, ImportMaxSide = 12000; public const long ImportMaxPixels = 50000000;

        static ComposeException Bad(string code, string why) => new ComposeException(code, "The picture " + why + ".");
        static uint BE32(byte[] b, int o) => (uint)(b[o] << 24 | b[o + 1] << 16 | b[o + 2] << 8 | b[o + 3]);
        static int BE16(byte[] b, int o) => b[o] << 8 | b[o + 1];
        static bool At(byte[] b, int o, params byte[] m) { if (b == null || o + m.Length > b.Length) return false; for (int k = 0; k < m.Length; k++) if (b[o + k] != m[k]) return false; return true; }
        static bool AtText(byte[] b, int o, string t) => At(b, o, Encoding.ASCII.GetBytes(t));

        /// <summary>what a file really is, from its first bytes (a name or an extension never decides it)</summary>
        public static string Sniff(byte[] b)
        {
            if (b == null || b.Length < 4) return "empty or too short";
            if (At(b, 0, 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A)) return "PNG";
            if (At(b, 0, 0xFF, 0xD8, 0xFF)) return "JPEG";
            if (AtText(b, 0, "GIF8")) return "GIF";
            if (AtText(b, 0, "RIFF") && AtText(b, 8, "WEBP")) return "WebP";
            if (AtText(b, 4, "ftyp")) return "HEIF or video";
            if (AtText(b, 0, "BM")) return "BMP";
            if (At(b, 0, 0x49, 0x49, 0x2A, 0x00) || At(b, 0, 0x4D, 0x4D, 0x00, 0x2A)) return "TIFF";
            if (At(b, 0, 0x00, 0x00, 0x01, 0x00)) return "icon";
            if (AtText(b, 0, "MZ")) return "Windows program";
            if (At(b, 0, 0x7F, 0x45, 0x4C, 0x46)) return "Linux program";
            if (At(b, 0, 0x50, 0x4B, 0x03, 0x04) || At(b, 0, 0x50, 0x4B, 0x05, 0x06)) return "ZIP archive";
            if (At(b, 0, 0x1F, 0x8B)) return "gzip archive";
            if (AtText(b, 0, "%PDF")) return "PDF";
            if (AtText(b, 0, "#!")) return "script";
            int p = At(b, 0, 0xEF, 0xBB, 0xBF) ? 3 : 0;
            while (p < b.Length && (b[p] == ' ' || b[p] == '\t' || b[p] == '\r' || b[p] == '\n')) p++;
            if (p < b.Length && b[p] == '<')
            {
                var head = Encoding.ASCII.GetString(b, p, Math.Min(4096, b.Length - p)).ToLowerInvariant();
                return head.Contains("<svg") ? "SVG" : head.Contains("<html") || head.Contains("<!doctype html") || head.Contains("<script") ? "HTML" : "XML or HTML";
            }
            if (p < b.Length && (b[p] == '{' || b[p] == '[')) return "JSON text";
            return "unknown";
        }

        /// <summary>the facts of a still PNG or JPEG, decided from its structure before any pixel is decoded; refused (ComposeException)
        /// when it is anything else, damaged, animated, or larger than maxSide pixels a side or maxPixels in all. verifyPixels: for a
        /// canonical PNG, also inflate its pixel data (bounded by the size checked first) and check every row.</summary>
        public static ImageInfo Inspect(byte[] b, int maxSide, long maxPixels, bool verifyPixels = true)
        {
            var kind = Sniff(b);
            if (kind == "PNG") return Png(b, maxSide, maxPixels, verifyPixels);
            if (kind == "JPEG") return Jpeg(b, maxSide, maxPixels);
            if (kind == "GIF" || kind == "WebP" || kind == "BMP" || kind == "TIFF" || kind == "HEIF or video" || kind == "icon")
                throw Bad("image.type", "is a " + kind + " file; only PNG and JPEG pictures are accepted");
            throw Bad("image.type", "is not a picture (it is " + (kind == "unknown" ? "of an unknown kind" : kind.StartsWith("empty") ? kind : "a " + kind + " file") + ")");
        }

        static void Dims(int w, int h, int maxSide, long maxPixels)
        {
            if (w <= 0 || h <= 0) throw Bad("image.damaged", "has no size");
            if (w > maxSide || h > maxSide || (long)w * h > maxPixels)
                throw Bad("image.dimensions", "is " + w + "x" + h + " pixels; at most " + maxSide + " pixels a side and " + maxPixels.ToString("N0", System.Globalization.CultureInfo.InvariantCulture) + " pixels are accepted");
        }

        static ImageInfo Png(byte[] b, int maxSide, long maxPixels, bool verifyPixels)
        {
            var info = new ImageInfo { Format = "png" }; var notes = new List<string>();
            int p = 8; bool ihdr = false, iend = false, plte = false, idatSeen = false, idatDone = false; var idat = new MemoryStream();
            while (p < b.Length)
            {
                if (iend) throw Bad("image.trailing", "has data after its end (" + Units.Size(b.Length - p) + ")");
                if (p + 12 > b.Length) throw Bad("image.damaged", "ends inside a part");
                uint len = BE32(b, p);
                if (len > (uint)(b.Length - p - 12)) throw Bad("image.damaged", "has a part longer than the file");
                var type = Encoding.ASCII.GetString(b, p + 4, 4);
                if (!type.All(c => c >= 'A' && c <= 'Z' || c >= 'a' && c <= 'z')) throw Bad("image.damaged", "has a damaged part");
                var crc = new Crc32(); crc.Update(b, p + 4, (int)len + 4);
                if (crc.Value != BE32(b, p + 8 + (int)len)) throw Bad("image.damaged", "has a part that fails its checksum");
                int d = p + 8;
                if (!ihdr && type != "IHDR") throw Bad("image.damaged", "does not start with its header");
                if (idatSeen && type != "IDAT") idatDone = true;
                switch (type)
                {
                    case "IHDR":
                        if (ihdr || len != 13) throw Bad("image.damaged", "has a damaged header");
                        ihdr = true; info.Width = (int)Math.Min(int.MaxValue, BE32(b, d)); info.Height = (int)Math.Min(int.MaxValue, BE32(b, d + 4));
                        info.BitDepth = b[d + 8]; info.ColorType = b[d + 9]; info.Interlaced = b[d + 12] == 1;
                        if (b[d + 10] != 0 || b[d + 11] != 0 || b[d + 12] > 1) throw Bad("image.damaged", "has a damaged header");
                        var ok = new Dictionary<int, int[]> { [0] = new[] { 1, 2, 4, 8, 16 }, [2] = new[] { 8, 16 }, [3] = new[] { 1, 2, 4, 8 }, [4] = new[] { 8, 16 }, [6] = new[] { 8, 16 } };
                        if (!ok.TryGetValue(info.ColorType, out var depths) || !depths.Contains(info.BitDepth)) throw Bad("image.damaged", "has a color format PNG does not define");
                        Dims(info.Width, info.Height, maxSide, maxPixels);
                        info.Alpha = info.ColorType == 4 || info.ColorType == 6;
                        info.Components = info.ColorType == 0 ? 1 : info.ColorType == 2 ? 3 : info.ColorType == 3 ? 1 : info.ColorType == 4 ? 2 : 4;
                        break;
                    case "PLTE": if (idatSeen || plte) throw Bad("image.damaged", "has its palette in the wrong place"); plte = true; break;
                    case "IDAT": if (idatDone) throw Bad("image.damaged", "has its pixel data in pieces"); idatSeen = true; idat.Write(b, d, (int)len); break;
                    case "IEND": if (len != 0) throw Bad("image.damaged", "has a damaged end"); iend = true; break;
                    case "acTL": case "fcTL": case "fdAT": throw Bad("image.animated", "is animated; only still pictures are accepted");
                    default:
                        if (char.IsUpper(type[0])) throw Bad("image.unsupported", "uses a PNG feature this program does not support (" + type + ")");
                        if (type == "tRNS") info.Alpha = true;
                        notes.Add("metadata or extra part " + type); break;
                }
                p = d + (int)len + 4;
            }
            if (!ihdr || !idatSeen || !iend) throw Bad("image.damaged", "is incomplete");
            if (info.ColorType == 3 && !plte) throw Bad("image.damaged", "has no palette");
            if (info.BitDepth != 8) notes.Add(info.BitDepth + "-bit channels");
            if (info.ColorType != 2 && info.ColorType != 6) notes.Add(info.ColorType == 3 ? "palette colors" : "gray colors");
            if (info.Interlaced) notes.Add("interlaced");
            info.Canonical = notes.Count == 0; info.NotCanonical = notes.Count == 0 ? null : string.Join(", ", notes);
            if (info.Canonical && verifyPixels) PixelData(idat.ToArray(), info.Width, info.Height, info.ColorType == 6 ? 4 : 3);
            return info;
        }

        /// <summary>a canonical PNG's pixel data: a zlib stream that inflates to exactly height rows of (1 + width x channels) bytes, each
        /// row starting with a filter type 0-4, and whose Adler-32 matches. The size is known before inflating (bounded by Dims).</summary>
        static void PixelData(byte[] z, int w, int h, int ch)
        {
            if (z.Length < 6) throw Bad("image.damaged", "has no pixel data");
            int cmf = z[0], flg = z[1];
            if ((cmf & 0x0F) != 8 || (cmf >> 4) > 7 || ((cmf << 8) | flg) % 31 != 0 || (flg & 0x20) != 0) throw Bad("image.damaged", "has damaged pixel data");
            long row = 1 + (long)w * ch, expect = row * h, n = 0; uint a = 1, s2 = 0;
            var buf = new byte[1 << 16];
            try
            {
                using (var ds = new DeflateStream(new MemoryStream(z, 2, z.Length - 6), CompressionMode.Decompress))
                    while (true)
                    {
                        int got = ds.Read(buf, 0, buf.Length);
                        if (got <= 0) break;
                        for (int k = 0; k < got; k++)
                        {
                            if (n % row == 0 && buf[k] > 4) throw Bad("image.damaged", "has a damaged pixel row");
                            if (++n > expect) throw Bad("image.damaged", "holds more pixel data than its size");
                            a = (a + buf[k]) % 65521; s2 = (s2 + a) % 65521;
                        }
                    }
            }
            catch (InvalidDataException) { throw Bad("image.damaged", "has pixel data that cannot be decompressed"); }
            if (n != expect) throw Bad("image.damaged", "holds less pixel data than its size");
            if ((s2 << 16 | a) != BE32(z, z.Length - 4)) throw Bad("image.damaged", "has pixel data that fails its checksum");
        }

        static ImageInfo Jpeg(byte[] b, int maxSide, long maxPixels)
        {
            var info = new ImageInfo { Format = "jpeg" }; var notes = new List<string>();
            int p = 2, sofMarker = -1, scans = 0, jfif = 0, lastNs = 0; bool eoi = false;
            while (!eoi)
            {
                if (p >= b.Length) throw Bad("image.damaged", "ends without its end marker");
                if (b[p] != 0xFF) throw Bad("image.damaged", "has bytes outside its segments");
                while (p < b.Length && b[p] == 0xFF) p++;
                if (p >= b.Length) throw Bad("image.damaged", "ends without its end marker");
                int m = b[p++];
                if (m == 0xD9) { eoi = true; break; }
                if (m == 0x00 || m == 0x01 || m == 0xD8 || m >= 0xD0 && m <= 0xD7) throw Bad("image.damaged", "has a marker in the wrong place");
                if (p + 2 > b.Length) throw Bad("image.damaged", "ends inside a segment");
                int L = BE16(b, p); if (L < 2 || p + L > b.Length) throw Bad("image.damaged", "has a segment longer than the file");
                int d = p + 2, dl = L - 2;
                if (m == 0xE0) { if (dl >= 5 && AtText(b, d, "JFIF\0")) { if (++jfif > 1) notes.Add("two JFIF headers"); } else notes.Add("metadata APP0"); }
                else if (m >= 0xE1 && m <= 0xEF) notes.Add("metadata APP" + (m - 0xE0) + (m == 0xE1 ? " (EXIF or XMP)" : m == 0xE2 ? " (color profile)" : ""));
                else if (m == 0xFE) notes.Add("comment");
                else if (m == 0xDB || m == 0xC4 || m == 0xDD) { }
                else if (m == 0xC0 || m == 0xC1 || m == 0xC2)
                {
                    if (sofMarker >= 0) throw Bad("image.damaged", "describes two pictures");
                    sofMarker = m;
                    if (dl < 6) throw Bad("image.damaged", "has a damaged frame header");
                    int prec = b[d]; info.Height = BE16(b, d + 1); info.Width = BE16(b, d + 3); info.Components = b[d + 5];
                    if (dl != 6 + 3 * info.Components) throw Bad("image.damaged", "has a damaged frame header");
                    if (prec != 8) throw Bad("image.unsupported", "uses " + prec + "-bit samples; only 8-bit JPEG is accepted");
                    if (info.Components != 1 && info.Components != 3) throw Bad("image.unsupported", "has " + info.Components + " color components (CMYK and other kinds are not accepted)");
                    if (info.Height == 0) throw Bad("image.unsupported", "declares its height later (not supported)");
                    for (int c = 0; c < info.Components; c++) { int hv = b[d + 6 + 3 * c + 1]; if ((hv >> 4) < 1 || (hv >> 4) > 4 || (hv & 15) < 1 || (hv & 15) > 4) throw Bad("image.damaged", "has damaged sampling factors"); }
                    Dims(info.Width, info.Height, maxSide, maxPixels);
                    info.Progressive = m == 0xC2; if (m != 0xC0) notes.Add(m == 0xC2 ? "progressive" : "extended sequential");
                }
                else if (m == 0xDA)
                {
                    if (sofMarker < 0) throw Bad("image.damaged", "has picture data before its frame header");
                    if (dl < 1 || dl != 4 + 2 * b[d]) throw Bad("image.damaged", "has a damaged scan header");
                    lastNs = b[d]; scans++; p += L;
                    while (true)   // the entropy-coded data: up to the next marker that is not a stuffed byte or a restart marker
                    {
                        if (p + 1 >= b.Length) throw Bad("image.damaged", "ends inside its picture data");
                        if (b[p] != 0xFF) { p++; continue; }
                        int n2 = b[p + 1];
                        if (n2 == 0x00 || n2 >= 0xD0 && n2 <= 0xD7) { p += 2; continue; }
                        if (n2 == 0xFF) { p++; continue; }
                        break;
                    }
                    continue;
                }
                else if (m >= 0xC3 && m <= 0xCF || m == 0xDC || m == 0xDE || m == 0xDF || m >= 0xF0 && m <= 0xFD)
                    throw Bad("image.unsupported", "uses a JPEG kind this program does not support (lossless, hierarchical or arithmetic coding)");
                else throw Bad("image.damaged", "has an unknown segment");
                p += L;
            }
            if (p != b.Length) throw Bad("image.trailing", "has data after its end (" + Units.Size(b.Length - p) + ")");
            if (sofMarker < 0 || scans == 0) throw Bad("image.damaged", "is incomplete");
            if (scans > 1 && sofMarker == 0xC0) notes.Add("more than one scan");
            if (sofMarker == 0xC0 && lastNs != info.Components) notes.Add("separate scans per color");
            info.Canonical = notes.Count == 0; info.NotCanonical = notes.Count == 0 ? null : string.Join(", ", notes);
            return info;
        }

        /// <summary>a JPEG without its APP1-APP15 segments, comments and any APP0 that is not JFIF (pixels untouched); used on the encoder's
        /// own output so the result is canonical</summary>
        public static byte[] StripJpeg(byte[] b)
        {
            Inspect(b, ImportMaxSide, ImportMaxPixels, false);
            var o = new MemoryStream(); o.WriteByte(0xFF); o.WriteByte(0xD8); int p = 2;
            while (true)
            {
                while (b[p] == 0xFF && b[p + 1] == 0xFF) p++;
                int m = b[p + 1];
                if (m == 0xD9) { o.WriteByte(0xFF); o.WriteByte(0xD9); return o.ToArray(); }
                int L = BE16(b, p + 2), end = p + 2 + L;
                if (m == 0xDA) { end = p + 2 + L; while (!(b[end] == 0xFF && b[end + 1] != 0x00 && !(b[end + 1] >= 0xD0 && b[end + 1] <= 0xD7) && b[end + 1] != 0xFF)) end++; }
                bool drop = m >= 0xE1 && m <= 0xEF || m == 0xFE || m == 0xE0 && !AtText(b, p + 4, "JFIF\0");
                if (!drop) o.Write(b, p, end - p);
                p = end;
            }
        }

        // ---------------------------------------------------------------- the canonical PNG writer (deterministic for the same pixels)
        /// <summary>an 8-bit RGB (alpha false) or RGBA PNG of BGRA pixels (stride w x 4), each row with the filter that makes it smallest</summary>
        public static byte[] EncodePng(int w, int h, byte[] bgra, bool alpha)
        {
            int ch = alpha ? 4 : 3, rb = w * ch;
            var prev = new byte[rb]; var cur = new byte[rb]; var outRow = new byte[rb]; var best = new byte[rb];
            var z = new MemoryStream(); z.WriteByte(0x78); z.WriteByte(0xDA); uint a = 1, s2 = 0;
            using (var ds = new DeflateStream(z, CompressionLevel.Optimal, true))
            {
                for (int y = 0; y < h; y++)
                {
                    for (int x = 0, i = y * w * 4, j = 0; x < w; x++, i += 4)
                    { cur[j++] = bgra[i + 2]; cur[j++] = bgra[i + 1]; cur[j++] = bgra[i]; if (alpha) cur[j++] = bgra[i + 3]; }
                    int bestType = 0; long bestSum = long.MaxValue;
                    for (int t = 0; t <= 4; t++)
                    {
                        long sum = 0;
                        for (int k = 0; k < rb; k++)
                        {
                            int left = k >= ch ? cur[k - ch] : 0, up = prev[k], ul = k >= ch ? prev[k - ch] : 0, pred;
                            switch (t) { case 0: pred = 0; break; case 1: pred = left; break; case 2: pred = up; break; case 3: pred = (left + up) >> 1; break; default: pred = Paeth(left, up, ul); break; }
                            outRow[k] = (byte)(cur[k] - pred); sum += outRow[k] < 128 ? outRow[k] : 256 - outRow[k];
                        }
                        if (sum < bestSum) { bestSum = sum; bestType = t; Buffer.BlockCopy(outRow, 0, best, 0, rb); }
                    }
                    ds.WriteByte((byte)bestType); ds.Write(best, 0, rb);
                    a = (a + (uint)bestType) % 65521; s2 = (s2 + a) % 65521;
                    for (int k = 0; k < rb; k++) { a = (a + best[k]) % 65521; s2 = (s2 + a) % 65521; }
                    var tmp = prev; prev = cur; cur = tmp;
                }
            }
            uint adler = s2 << 16 | a; z.WriteByte((byte)(adler >> 24)); z.WriteByte((byte)(adler >> 16)); z.WriteByte((byte)(adler >> 8)); z.WriteByte((byte)adler);
            var png = new MemoryStream(); png.Write(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, 0, 8);
            var ihdr = new byte[13]; Put(ihdr, 0, (uint)w); Put(ihdr, 4, (uint)h); ihdr[8] = 8; ihdr[9] = (byte)(alpha ? 6 : 2);
            Chunk(png, "IHDR", ihdr); Chunk(png, "IDAT", z.ToArray()); Chunk(png, "IEND", new byte[0]);
            return png.ToArray();
        }

        static int Paeth(int a, int b, int c) { int p = a + b - c, pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c); return pa <= pb && pa <= pc ? a : pb <= pc ? b : c; }
        static void Put(byte[] b, int o, uint v) { b[o] = (byte)(v >> 24); b[o + 1] = (byte)(v >> 16); b[o + 2] = (byte)(v >> 8); b[o + 3] = (byte)v; }
        static void Chunk(Stream s, string type, byte[] data)
        {
            var hd = new byte[8]; Put(hd, 0, (uint)data.Length); Encoding.ASCII.GetBytes(type).CopyTo(hd, 4); s.Write(hd, 0, 8); s.Write(data, 0, data.Length);
            var c = new Crc32(); c.Update(hd, 4, 4); c.Update(data, 0, data.Length); var cb = new byte[4]; Put(cb, 0, c.Value); s.Write(cb, 0, 4);
        }
    }

    public static class ImageNormalizer
    {
        static ComposeException Bad(string code, string why) => new ComposeException(code, "The picture " + why + ".");

        /// <summary>a person's own PNG or JPEG as the slot's canonical picture (see the file header). fit, for a slot of an exact size:
        /// "cover" fills it (centered crop) or "contain" shows all of it (centered, on black or, where see-through is allowed, on nothing).
        /// A slot with a maximum size is never enlarged. Refused: anything that is not a still PNG or JPEG, over the import limits, or
        /// still over the slot's byte limit after conversion.</summary>
        public static byte[] Normalize(byte[] input, ImageSlotRule rule, string fit = "cover")
        {
            if (input == null || input.Length > ImageRules.ImportMaxBytes) throw Bad("image.size", "is larger than " + Units.Size(ImageRules.ImportMaxBytes));
            var info = ImageRules.Inspect(input, ImageRules.ImportMaxSide, ImageRules.ImportMaxPixels, false);
            Bitmap src;
            try { src = new Bitmap(new MemoryStream(input), false); }
            catch (Exception ex) when (ex is ArgumentException || ex is ExternalException || ex is OutOfMemoryException) { throw Bad("image.damaged", "could not be decoded"); }
            try { return Convert(src, info, rule, fit); }
            catch (Exception ex) when (ex is ExternalException || ex is OutOfMemoryException) { throw Bad("image.damaged", "could not be decoded"); }   // how Windows' decoder reports damaged data
            finally { src.Dispose(); }
        }

        static byte[] Convert(Bitmap src, ImageInfo info, ImageSlotRule rule, string fit)
        {
            if (src.Width != info.Width || src.Height != info.Height) throw Bad("image.damaged", "decodes to a different size than its header says");
            Orient(src);
            int sw = src.Width, sh = src.Height, W, H; RectangleF from; Rectangle to;
            if (rule.Exact)
            {
                W = rule.MaxW; H = rule.MaxH;
                if (fit == "contain")
                {
                    double s = Math.Min((double)W / sw, (double)H / sh); int dw = Math.Max(1, (int)Math.Round(sw * s)), dh = Math.Max(1, (int)Math.Round(sh * s));
                    from = new RectangleF(0, 0, sw, sh); to = new Rectangle((W - dw) / 2, (H - dh) / 2, dw, dh);
                }
                else
                {
                    double s = Math.Max((double)W / sw, (double)H / sh); float cw = (float)(W / s), chh = (float)(H / s);
                    from = new RectangleF((sw - cw) / 2, (sh - chh) / 2, cw, chh); to = new Rectangle(0, 0, W, H);
                }
            }
            else
            {
                double s = Math.Min(1.0, Math.Min((double)rule.MaxW / sw, (double)rule.MaxH / sh));
                W = Math.Max(1, (int)Math.Round(sw * s)); H = Math.Max(1, (int)Math.Round(sh * s)); from = new RectangleF(0, 0, sw, sh); to = new Rectangle(0, 0, W, H);
            }
            byte[] px;
            using (var dst = new Bitmap(W, H, PixelFormat.Format32bppArgb))
            {
                using (var g = Graphics.FromImage(dst))
                using (var ia = new ImageAttributes())
                {
                    g.Clear(rule.Alpha ? Color.Transparent : Color.Black);
                    g.CompositingMode = CompositingMode.SourceOver; g.CompositingQuality = CompositingQuality.HighQuality;
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic; g.PixelOffsetMode = PixelOffsetMode.HighQuality; g.SmoothingMode = SmoothingMode.None;
                    ia.SetWrapMode(WrapMode.TileFlipXY);
                    g.DrawImage(src, to, from.X, from.Y, from.Width, from.Height, GraphicsUnit.Pixel, ia);
                }
                px = Pixels(dst);
            }
            bool alpha = false; for (int i = 3; i < px.Length; i += 4) if (px[i] != 255) { alpha = true; break; }
            if (alpha && !rule.Alpha)
            {
                for (int i = 0; i < px.Length; i += 4) { int al = px[i + 3]; px[i] = (byte)(px[i] * al / 255); px[i + 1] = (byte)(px[i + 1] * al / 255); px[i + 2] = (byte)(px[i + 2] * al / 255); px[i + 3] = 255; }
                alpha = false;
            }
            byte[] outBytes;
            if (alpha || !rule.Jpeg) outBytes = ImageRules.EncodePng(W, H, px, alpha);
            else
            {
                outBytes = null;
                foreach (var q in new[] { 90, 85, 80, 75, 70, 65, 60, 50 }) { outBytes = Jpeg(W, H, px, q); if (outBytes.Length <= rule.MaxBytes) break; }
            }
            var oi = ImageRules.Inspect(outBytes, Math.Max(rule.MaxW, rule.MaxH), (long)rule.MaxW * rule.MaxH, true);
            if (!oi.Canonical) throw Bad("image.internal", "could not be written in the console profile (" + oi.NotCanonical + ")");
            var (code, why) = rule.Check(oi, outBytes.Length);
            if (code != null) throw Bad(code == "design.slot-bytes" ? "image.size" : code, "does not fit this place after conversion: it " + why);
            return outBytes;
        }

        static void Orient(Bitmap b)
        {
            if (!b.PropertyIdList.Contains(0x0112)) return;
            var it = b.GetPropertyItem(0x0112); int o = it.Value != null && it.Value.Length >= 2 ? it.Value[0] | it.Value[1] << 8 : 1;
            var t = o == 2 ? RotateFlipType.RotateNoneFlipX : o == 3 ? RotateFlipType.Rotate180FlipNone : o == 4 ? RotateFlipType.Rotate180FlipX : o == 5 ? RotateFlipType.Rotate90FlipX
                  : o == 6 ? RotateFlipType.Rotate90FlipNone : o == 7 ? RotateFlipType.Rotate270FlipX : o == 8 ? RotateFlipType.Rotate270FlipNone : RotateFlipType.RotateNoneFlipNone;
            if (t != RotateFlipType.RotateNoneFlipNone) b.RotateFlip(t);
        }

        static byte[] Pixels(Bitmap b)
        {
            var d = b.LockBits(new Rectangle(0, 0, b.Width, b.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            try
            {
                var px = new byte[b.Width * b.Height * 4];
                for (int y = 0; y < b.Height; y++) Marshal.Copy(IntPtr.Add(d.Scan0, y * d.Stride), px, y * b.Width * 4, b.Width * 4);
                return px;
            }
            finally { b.UnlockBits(d); }
        }

        static byte[] Jpeg(int w, int h, byte[] bgra, int quality)
        {
            using (var bmp = new Bitmap(w, h, PixelFormat.Format24bppRgb))
            {
                var d = bmp.LockBits(new Rectangle(0, 0, w, h), ImageLockMode.WriteOnly, PixelFormat.Format24bppRgb);
                try
                {
                    var row = new byte[w * 3];
                    for (int y = 0; y < h; y++) { for (int x = 0; x < w; x++) { int i = (y * w + x) * 4; row[x * 3] = bgra[i]; row[x * 3 + 1] = bgra[i + 1]; row[x * 3 + 2] = bgra[i + 2]; } Marshal.Copy(row, 0, IntPtr.Add(d.Scan0, y * d.Stride), row.Length); }
                }
                finally { bmp.UnlockBits(d); }
                var enc = ImageCodecInfo.GetImageEncoders().First(c => c.FormatID == ImageFormat.Jpeg.Guid);
                using (var ep = new EncoderParameters(1))
                {
                    ep.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, (long)quality);
                    var ms = new MemoryStream(); bmp.Save(ms, enc, ep);
                    return ImageRules.StripJpeg(ms.ToArray());
                }
            }
        }
    }
}
