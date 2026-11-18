// Classic Cart Studio console backup and restore engine. Copyright (C) 2026 Rance9. GPL-3.0-or-later.
// The one gate every share or send action in Console Composer and HOME Designer calls before a file leaves through a share path.
// Classify decides a file's role from its CONTENT (never its name or extension): a renamed private package is still a private package.
// Only two roles are shareable, and only after their full validation passes again: a Shared Composition Plan (ccs-composition/1) and a
// Shared HOME Design (.ccshome). A private restore package, a console backup, a collection package (.ccs, which carries game files) and
// anything unknown are refused with the reason. Nothing here uploads, sends or fetches anything; it only says yes or no.
using CcsBackup.Archive;
using System;
using System.IO;
using System.Linq;
using System.Text;

namespace CcsBackup.Compose
{
    public enum FileRole { Unknown, SharedComposition, SharedHomeDesign, PrivateRestorePackage, ConsoleBackup, CcsCollection }

    public static class ShareGuard
    {
        /// <summary>what a person is told each role is (the same words on every surface)</summary>
        public static string Describe(FileRole r)
        {
            switch (r)
            {
                case FileRole.SharedComposition: return Composition.Label;
                case FileRole.SharedHomeDesign: return DesignProfile.Label;
                case FileRole.PrivateRestorePackage: return "Private Restore Package: your console's own files and identity. Private; never shared.";
                case FileRole.ConsoleBackup: return "Console Backup: a complete copy of your console's built-in storage. Private; never shared.";
                case FileRole.CcsCollection: return "Collection package (.ccs): a collection with its game files. Not shared from here.";
                default: return "Not a file Console Composer can share.";
            }
        }

        public static FileRole Classify(string path)
        {
            var len = new FileInfo(path).Length;
            byte[] head = new byte[Math.Min(8, len)];
            using (var f = File.OpenRead(path)) { int got = 0; while (got < head.Length) { int r = f.Read(head, got, head.Length - got); if (r <= 0) break; got += r; } }
            if (head.Length >= 4 && head[0] == 'P' && head[1] == 'K' && (head[2] == 3 && head[3] == 4 || head[2] == 5 && head[3] == 6)) return ClassifyZip(path, len);
            int p = head.Length >= 3 && head[0] == 0xEF && head[1] == 0xBB && head[2] == 0xBF ? 3 : 0;
            while (p < head.Length && (head[p] == ' ' || head[p] == '\r' || head[p] == '\n' || head[p] == '\t')) p++;
            if (p < head.Length && head[p] == '{' && len <= 16000000)
            {
                try
                {
                    var d = StrictJson.ParseObject(File.ReadAllBytes(path), new JsonLimits { MaxBytes = 16000000 }, "The file");
                    var fmt = d["format"] as string;
                    if (fmt == Composition.Format) return FileRole.SharedComposition;
                    if (fmt == PrivatePackage.Format) return FileRole.PrivateRestorePackage;
                    if (fmt == BackupFormat.Format) return FileRole.ConsoleBackup;
                    if (fmt == DesignProfile.Format) return FileRole.SharedHomeDesign;
                }
                catch (ComposeException) { }
            }
            return FileRole.Unknown;
        }

        static FileRole ClassifyZip(string path, long len)
        {
            string[] names = null;
            try
            {
                using (var z = SafeZip.Open(path, new ZipLimits { What = "file", MaxMembers = 200000, MaxArchiveBytes = long.MaxValue, MaxTotalBytes = long.MaxValue, MaxRatio = int.MaxValue, MaxMemberBytes = n => long.MaxValue }))
                    names = z.Entries.Select(e => e.Name).ToArray();
            }
            catch (ComposeException) { }
            if (names != null)
            {
                if (names.Any(n => n.EndsWith(PrivatePackage.ManifestName) || n.StartsWith("payload/"))) return FileRole.PrivateRestorePackage;
                if (names.Any(n => n.EndsWith(BackupFormat.ManifestName))) return FileRole.ConsoleBackup;
                if (names.Contains("ccs-manifest.json")) return FileRole.CcsCollection;
                if (names.Contains(DesignProfile.ManifestName)) return FileRole.SharedHomeDesign;
                return FileRole.Unknown;
            }
            // an archive the strict reader refuses: still look for a private manifest's name in its index, so the refusal says so
            int n2 = (int)Math.Min(len, 16 * 1000 * 1000); var tail = new byte[n2];
            using (var f = File.OpenRead(path)) { f.Position = len - n2; int got = 0; while (got < n2) { int r = f.Read(tail, got, n2 - got); if (r <= 0) break; got += r; } }
            var text = Encoding.ASCII.GetString(tail);
            if (text.Contains(PrivatePackage.ManifestName)) return FileRole.PrivateRestorePackage;
            if (text.Contains(BackupFormat.ManifestName)) return FileRole.ConsoleBackup;
            return FileRole.Unknown;
        }

        /// <summary>refuses (ComposeException "share.private", "share.collection", "share.unknown" or the format's own refusal) unless the
        /// file is a valid Shared Composition Plan or Shared HOME Design</summary>
        public static FileRole RequireShareable(string path)
        {
            var role = Classify(path);
            switch (role)
            {
                case FileRole.SharedComposition: Composition.Parse(File.ReadAllBytes(path)); return role;
                case FileRole.SharedHomeDesign: HomeDesign.Open(path); return role;
                case FileRole.PrivateRestorePackage: case FileRole.ConsoleBackup:
                    throw new ComposeException("share.private", "This file was not shared: it is a " + (role == FileRole.ConsoleBackup ? "console backup" : "private restore package") + ", which holds your console's own files and identity. Private files are never shared.");
                case FileRole.CcsCollection: throw new ComposeException("share.collection", "This file was not shared: it is a collection package with game files. Console Composer shares only plans and HOME designs.");
                default: throw new ComposeException("share.unknown", "This file was not shared: it is not a Shared Composition Plan or a Shared HOME Design.");
            }
        }
    }
}
