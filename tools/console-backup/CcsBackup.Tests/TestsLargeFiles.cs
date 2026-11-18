// Classic Cart Studio console backup and restore engine tests: files larger than 64,000,000 bytes are hashed in full in a backup's file
// list (no silent omission). GPL-3.0-or-later. Builds one small ext4 image with e2fsprogs in WSL (the fixture tools' way) and deletes it.
using CcsBackup.Ext4;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using static CcsBackup.Tests.Program;

namespace CcsBackup.Tests
{
    static partial class Tests
    {
        public static void LargeFiles()
        {
            var dir = NewDir("largefiles"); var img = Path.Combine(dir, "large.img"); var big = Path.Combine(dir, "big.bin");
            string W(string p) { p = Path.GetFullPath(p); return "/mnt/" + char.ToLowerInvariant(p[0]) + p.Substring(2).Replace('\\', '/'); }
            try
            {
                using (var f = new FileStream(big, FileMode.CreateNew)) { var buf = new byte[1 << 20]; for (int i = 0; i < 70; i++) { for (int k = 0; k < buf.Length; k += 64) buf[k] = (byte)(i + k); f.Write(buf, 0, buf.Length); } }
                string sha; using (var h = SHA256.Create()) using (var f = File.OpenRead(big)) sha = Hex.Of(h.ComputeHash(f));
                var script = Path.Combine(dir, "mk.sh");
                File.WriteAllText(script, "set -e\ntruncate -s 100M '" + W(img) + "'\n/sbin/mke2fs -q -t ext4 -F '" + W(img) + "'\n/sbin/debugfs -w -R \"write " + W(big) + " /big.bin\" '" + W(img) + "' >/dev/null 2>&1\necho ok\n");
                var p = Process.Start(new ProcessStartInfo("wsl.exe", "-e bash \"" + W(script) + "\"") { UseShellExecute = false, RedirectStandardOutput = true, CreateNoWindow = true });
                var o = p.StandardOutput.ReadToEnd(); p.WaitForExit();
                if (!o.Contains("ok")) throw new SkipException("e2fsprogs in WSL could not build the image");
                InventoryResult inv; using (var f = File.OpenRead(img)) inv = Inventory.Build(f, "nandc");
                var row = Json.Parse(Json.Write(inv.Document)).A("entries").OfType<System.Collections.Generic.Dictionary<string, object>>().FirstOrDefault(x => x.S("path") == "/big.bin");
                Check(row != null && row.L("size") == 70L * 1024 * 1024 && row.S("sha256") == sha, "a 73,400,320-byte file is hashed in full in the file list (" + (row?.S("sha256") ?? "no hash") + ")");
            }
            finally { try { Directory.Delete(dir, true); } catch { } }
        }
    }
}
