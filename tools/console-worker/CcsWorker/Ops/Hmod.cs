// Classic Cart Studio console transport worker. Copyright (C) 2026 Rance9. GPL-3.0-or-later.
// A hakchi module (.hmod: a gzip-compressed tar, the format hakchi's pack_install extracts with "tar -xzf") made in memory, byte for
// byte the same every time: ustar entries "./etc/", "./etc/init.d/" and the files, owner 0:0 (root), modification time 0.
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;

namespace CcsWorker.Ops
{
    public static class Hmod
    {
        public static byte[] Build(IDictionary<string, KeyValuePair<byte[], int>> files)
        {
            var tar = new MemoryStream();
            var dirs = new SortedSet<string>(System.StringComparer.Ordinal);
            foreach (var path in files.Keys)
                for (var d = ConsolePath.Parent(path); d != "/"; d = ConsolePath.Parent(d)) dirs.Add(d);
            foreach (var d in dirs) Entry(tar, "." + d + "/", '5', 0x1ED, null);
            foreach (var f in files.OrderBy(k => k.Key, System.StringComparer.Ordinal)) Entry(tar, "." + f.Key, '0', f.Value.Value, f.Value.Key);
            tar.Write(new byte[1024], 0, 1024);
            var gz = new MemoryStream();
            using (var z = new GZipStream(gz, CompressionLevel.Optimal, true)) { var t = tar.ToArray(); z.Write(t, 0, t.Length); }
            return gz.ToArray();
        }

        static void Entry(Stream s, string name, char type, int mode, byte[] data)
        {
            var h = new byte[512];
            Put(h, 0, 100, name);
            Put(h, 100, 8, Oct(mode, 7));
            Put(h, 108, 8, Oct(0, 7)); Put(h, 116, 8, Oct(0, 7));
            Put(h, 124, 12, Oct(data?.Length ?? 0, 11)); Put(h, 136, 12, Oct(0, 11));
            for (int i = 148; i < 156; i++) h[i] = (byte)' ';
            h[156] = (byte)type;
            Put(h, 257, 6, "ustar"); Put(h, 263, 2, "00"); Put(h, 265, 32, "root"); Put(h, 297, 32, "root");
            int sum = h.Sum(b => b);
            Put(h, 148, 8, Oct(sum, 6)); h[154] = 0; h[155] = (byte)' ';
            s.Write(h, 0, 512);
            if (data == null || data.Length == 0) return;
            s.Write(data, 0, data.Length);
            int pad = (512 - data.Length % 512) % 512; s.Write(new byte[pad], 0, pad);
        }

        static string Oct(long v, int digits) => System.Convert.ToString(v, 8).PadLeft(digits, '0');
        static void Put(byte[] h, int at, int max, string v) { var b = Encoding.ASCII.GetBytes(v); System.Array.Copy(b, 0, h, at, System.Math.Min(b.Length, max)); }
    }
}
