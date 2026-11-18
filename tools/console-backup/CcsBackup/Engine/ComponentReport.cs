// Classic Cart Studio console backup engine: which engine a program carries. Copyright (C) 2026 Rance9. GPL-3.0-or-later.
// "--engine-info" in Classic Cart Studio for Windows and in the CCS Console Backup & Restore Utility prints this report: the SHA-256 of
// each shared library as that program loads it (the Windows app: the files beside it; the Utility: the copies inside it) and the
// recovery images the engine carries. The release build compares the two reports: the same bytes mean the same engine. Reads only.
using CcsBackup.Device;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;

namespace CcsBackup.Engine
{
    public static class ComponentReport
    {
        /// <summary>the libraries both programs share (CcsBackupUi is the window; CcsBackup the engine with its recovery images; CcsConsole the
        /// FEL loader; LibUsbDotNet the USB library)</summary>
        public static readonly string[] Shared = { "CcsBackupUi.dll", "CcsBackup.dll", "CcsConsole.dll", "LibUsbDotNet.LibUsbDotNet.dll" };

        /// <summary>program: its name; open: the bytes of one shared library as this program loads it (null when it has none)</summary>
        public static string Describe(string program, string version, Func<string, Stream> open)
        {
            var libs = new JObj();
            foreach (var n in Shared)
                using (var s = open(n))
                    libs.Set(n, s == null ? null : (object)new JObj { { "sha256", Sha(s, out long len) }, { "bytes", len } });
            var images = new JObj();
            foreach (ImageKind k in Enum.GetValues(typeof(ImageKind)))
            {
                if (!RecoveryImages.Embedded(k)) continue;
                var (id, sha) = RecoveryImages.IdOf(k);
                images.Set(RecoveryImages.Variant(k), new JObj { { "image_id", id }, { "sha256", sha } });
            }
            return Json.Write(new JObj { { "schema", "ccs-engine-report/1" }, { "program", program }, { "version", version }, { "libraries", libs }, { "recovery_images", images } });
        }

        /// <summary>the Utility's copies: the libraries inside the executable (resources "asm/&lt;name&gt;")</summary>
        public static Func<string, Stream> Embedded(Assembly exe) => n => exe.GetManifestResourceStream("asm/" + n);

        /// <summary>the Windows app's copies: the files beside its executable</summary>
        public static Func<string, Stream> Beside(string folder) => n => { var p = Path.Combine(folder, n); return File.Exists(p) ? File.OpenRead(p) : null; };

        static string Sha(Stream s, out long len)
        {
            using (var h = SHA256.Create())
            {
                var buf = new byte[1 << 16]; long total = 0; int n;
                while ((n = s.Read(buf, 0, buf.Length)) > 0) { h.TransformBlock(buf, 0, n, null, 0); total += n; }
                h.TransformFinalBlock(buf, 0, 0); len = total;
                return string.Concat(h.Hash.Select(b => b.ToString("x2")));
            }
        }
    }
}
