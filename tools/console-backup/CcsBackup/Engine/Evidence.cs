// Classic Cart Studio console backup and restore engine. Copyright (C) 2026 Rance9. GPL-3.0-or-later.
// The evidence a strict operation (restore, preparation, Return to Original Software, rescue, Console Composer) relies on, bound into its
// reviewed plan and checked again at its write boundary:
//   BoundArchive  one backup file as the plan saw it: its role, path, SHA-256, size, completion time, console, simulated or real, whether
//                 it holds everything a restore needs, a digest of its members (coverage) and the console session that made it (when this
//                 operation made it). Its DigestLine goes into the plan's digest, so a confirmation names exactly these bytes.
//   Hold          opens each bound file for reading and keeps it open, sharing it for reading only: while held, no program can change,
//                 replace, rename or delete it. The check and every later use read the same file.
//   Recheck       the bound file verified again from scratch, under the hold, immediately before the first write: missing, changed,
//                 unreadable, incomplete, another console's, the other kind (simulated/real) or another session's refuses, with nothing
//                 written and the confirmation not used up.
using CcsBackup.Archive;
using CcsBackup.Device;
using CcsBackup.Library;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace CcsBackup.Engine
{
    public sealed class BoundArchive
    {
        public string Role, Path, Sha256, CompletedUtc, Hwid, Coverage, Session; public long Bytes; public bool Simulated, RestoreReady;

        /// <summary>a verified backup, bound as the plan sees it. session: the console session that made it (null when it was not made in
        /// this operation's session).</summary>
        public static BoundArchive From(string role, string path, Verification v, string session = null) => new BoundArchive
        {
            Role = role, Path = path, Sha256 = v.ArchiveSha256, Bytes = v.Bytes, CompletedUtc = v.Manifest?.S("completed_utc"), Hwid = ConsoleIdentity.NormalizeHwid(v.Hwid),
            Simulated = v.Simulated, RestoreReady = v.RestoreReady, Coverage = CoverageOf(v), Session = session,
        };

        /// <summary>a digest of the archive's members (name, size, SHA-256), so the plan says exactly what the file covers</summary>
        public static string CoverageOf(Verification v)
        {
            var sb = new StringBuilder("ccs-coverage/1\n");
            foreach (var kv in v.Members.OrderBy(x => x.Key, StringComparer.Ordinal)) sb.Append(kv.Key).Append(' ').Append(kv.Value.size.ToString(CultureInfo.InvariantCulture)).Append(' ').Append(kv.Value.sha256).Append('\n');
            using (var h = SHA256.Create()) return Hex.Of(h.ComputeHash(Encoding.UTF8.GetBytes(sb.ToString())));
        }

        public string DigestLine() => string.Join("|", Role, Path, Sha256, Bytes.ToString(CultureInfo.InvariantCulture), CompletedUtc, Hwid, Simulated ? "simulated" : "real", RestoreReady ? "restore-ready" : "not-restore-ready", Coverage, Session ?? "-");

        public JObj ToJson() => new JObj
        {
            { "role", Role }, { "path", Path }, { "sha256", Sha256 }, { "bytes", Bytes }, { "completed_utc", CompletedUtc }, { "console", ConsoleIdentity.HwidTag(Hwid) },
            { "simulated", Simulated }, { "restore_ready", RestoreReady }, { "coverage", Coverage }, { "session", Session },
        };
    }

    public static class Evidence
    {
        /// <summary>Open files kept open for reading only, shared for reading only: no one can write, replace, rename or delete them while
        /// held. Dispose releases them.</summary>
        public sealed class Hold : IDisposable
        {
            readonly List<FileStream> open = new List<FileStream>();
            public string Problem { get; private set; }
            public static Hold Open(params string[] paths)
            {
                var h = new Hold();
                foreach (var p in paths.Where(x => x != null).Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    try { h.open.Add(new FileStream(p, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.None)); }
                    catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { h.Problem = System.IO.Path.GetFileName(p) + ": " + (ex is FileNotFoundException || ex is DirectoryNotFoundException ? "missing" : "cannot be opened for checking (" + ex.Message.TrimEnd('.') + ")"); break; }
                }
                return h;
            }
            public void Dispose() { foreach (var f in open) try { f.Dispose(); } catch { } open.Clear(); }
        }

        /// <summary>null when the file under the hold is still exactly the bound archive; otherwise the reason, in plain words</summary>
        public static string Recheck(BoundArchive b, string hwid, bool simulated, string session, Func<bool> cancelled) => Recheck(b, hwid, simulated, session, cancelled, out _);

        /// <summary>the same; verification: the new check of the file (null when it was not read)</summary>
        public static string Recheck(BoundArchive b, string hwid, bool simulated, string session, Func<bool> cancelled, out Verification verification)
        {
            verification = null;
            var name = b?.Role ?? "backup";
            if (b == null || string.IsNullOrEmpty(b.Path) || !Hex.IsHex(b.Sha256, 64)) return "The plan does not name its " + name + " completely.";
            if (!File.Exists(b.Path)) return "The " + name + " is missing (" + b.Path + ").";
            var v = Verifier.VerifyFile(b.Path, null, cancelled); verification = v;
            if (!v.Ok) return "The " + name + " no longer passes its check (" + v.Problems[0] + ").";
            if (v.ArchiveSha256 != b.Sha256 || v.Bytes != b.Bytes) return "The " + name + " changed after the review.";
            if (v.Manifest?.S("completed_utc") != b.CompletedUtc || BoundArchive.CoverageOf(v) != b.Coverage || v.RestoreReady != b.RestoreReady) return "The " + name + " does not match what the review saw.";
            if (!ConsoleIdentity.SameConsole(v.Hwid, hwid) || ConsoleIdentity.NormalizeHwid(v.Hwid) != b.Hwid) return "The " + name + " belongs to a different console.";
            if (v.Simulated != simulated || b.Simulated != simulated) return "A simulated console's backup and a real console are never mixed.";
            if (b.Session != null && b.Session != session) return "The " + name + " was made in an earlier connection to the console; it does not show the console's state in this one.";
            return null;
        }
    }
}
