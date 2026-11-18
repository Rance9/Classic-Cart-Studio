// Classic Cart Studio console backup and restore engine. Copyright (C) 2026 Rance9. GPL-3.0-or-later.
// Developer Mode's SYSTEM IMAGE operation: the console's system part (its root file system: the "rootfs" part, nandb) replaced by an
// owner's SquashFS image, in the files image's session (ram/ccsdev sinfo, sstage, sburn), under the SESSION RESTORE POINT and
// Gate.DeveloperWrites. The supported class is the console's own system-part format, read from the console itself:
//   image       SquashFS 4.0 whose compression is the current system part's (the one the console's startup software is known to read;
//               this version reads gzip contents only), no larger than the system part, with a start program (/sbin/init) and the
//               console's own board type (/etc/clover/boardtype, checked on the PC by SquashFsReader and against the console's board)
//   layout      the console's table names its system part "rootfs" on nandb, and its startup software starts from it (root=/dev/nandb)
//   key         an encrypted system part is written through the console's own mapping (plain dm-crypt, aes-xts-plain: hakchi 3.9.3's
//               "flash system partition" parameters) with the key its own startup software holds; the key is read on the console and
//               never leaves it. Without that key the operation is refused (nothing could be read back as the console reads it).
//   protection  the restore point must hold the system part (and the console's table must still equal its own): Console Restore puts the
//               original part back under its own strict rules. No fresh full backup per change; later changes are not protected.
//   plan        the image's facts and the console's (system, key, sizes, compression, board, free memory for staging), the system part's
//               current MD5 as the console reads it raw (ccsbr digest), a one-time confirmation for exactly these bytes
//   execute     the restore point held and checked again, the part's MD5 read again, the record saved first; sstage (the image in the
//               console's RAM, checked there: SHA-256, SquashFS 4.0, compression) then sburn (written once through the mapping, read back
//               through it, never written a second time); the first failure stops. The startup stages, the startup software, the
//               storage table, the data storage and the keys are never written by this operation.
// Code in a system image controls the console once it starts, like any startup code: the plan says so.
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
    public sealed class SystemPlan
    {
        public string Hwid, ConsoleTag, Session, NowMd5, WantSha256, Origin, Board, Encryption, Compression, Digest, Nonce, StartupMd5, StartupCmdline; public long Bytes, PartBytes, BytesUsed, CurrentBytesUsed; public DateTime ExpiresUtc; public bool Simulated;
        public SessionRestorePoint Point; public byte[] Image; public List<string> Notes = new List<string>();
        public JObj ToJson() => new JObj
        {
            { "console_tag", ConsoleTag }, { "origin", Origin }, { "now_md5", NowMd5 }, { "want_sha256", WantSha256 }, { "bytes", Bytes }, { "part_bytes", PartBytes }, { "bytes_used", BytesUsed },
            { "current_bytes_used", CurrentBytesUsed }, { "board", Board }, { "encryption", Encryption }, { "compression", Compression }, { "restore_point", Point?.ToJson() },
            { "notice", SessionRestorePoint.NoticeText }, { "code_notice", DeveloperSystem.CodeNotice }, { "notes", Notes }, { "digest", Digest }, { "nonce", Nonce }, { "expires_utc", ExpiresUtc.ToString("o", CultureInfo.InvariantCulture) },
        };
    }

    public static class DeveloperSystem
    {
        public const string CodeNotice = "Code in this system controls the console once it starts. It can write the console's storage, startup stages or keys; a restore point cannot undo what it does to the startup stages or keys.";
        public const long Reserve = 32000000;   // free RAM kept beyond the staged image (the console's recovery session runs from RAM)
        public const long MaxImageBytes = 64000000;   // the PC reads at most this much of a chosen file (the system part itself is far smaller)
        static string Sha(byte[] b) { using (var h = SHA256.Create()) return Hex.Of(h.ComputeHash(b)); }

        /// <summary>the image file as a system root for this console, checked on the PC (null with the reason otherwise): SquashFS 4.0, its
        /// contents readable, a start program, the board type</summary>
        public static (SquashFsReader fs, string board, string why) CheckImage(byte[] image)
        {
            SquashFsReader fs;
            try { fs = new SquashFsReader(new MemoryStream(image, false)); }
            catch (InvalidDataException ex) { return (null, null, "it is " + ex.Message); }
            if (!fs.CanRead) return (fs, null, "its compression (" + fs.CompressionName + ") cannot be read by this version, so its contents cannot be checked, and the console's startup software is not known to read it");
            try
            {
                if (fs.Find("/sbin/init") == null) return (fs, null, "it has no start program (/sbin/init)");
                var b = fs.ReadFile("/etc/clover/boardtype", 4096);
                if (b == null) return (fs, null, "it does not record a board type (/etc/clover/boardtype), so it is not known to be a system for this console");
                return (fs, Encoding.ASCII.GetString(b).Trim(), null);
            }
            catch (Exception ex) when (ex is InvalidDataException || ex is NotSupportedException) { return (fs, null, "its contents could not be read (" + ex.Message + ")"); }
        }

        public static (SystemPlan plan, string why) Plan(RecoveryConsole con, SessionRestorePoint point, byte[] image, string origin, Func<bool> cancelled)
        {
            if (con.Profile != SessionProfile.Files) return (null, "The console is not in a file session.");
            var rp = RestorePoints.Require(point, con, RestorePoints.SystemPart, cancelled); if (rp != null) return (null, rp);
            if (image == null || image.Length < 96) return (null, "This file cannot be the console's system: it is not a SquashFS file system.");
            var id = con.Identify();
            var sys = id.Table.FirstOrDefault(t => t.Label == "rootfs");
            if (sys == null || sys.Device != "nandb") return (null, "This console's storage table does not name its system part as this version knows it (rootfs on nandb), so it is not changed.");
            var (fs, board, bad) = CheckImage(image); if (bad != null) return (null, "This file cannot be the console's system: " + bad + ".");
            if (image.Length % 4096 != 0) return (null, "This file cannot be the console's system: its size is not a whole number of 4,096-byte blocks (a SquashFS image is padded to them).");
            Dictionary<string, string> info;
            try { info = con.DevSystemInfo(); } catch (ConsoleRefusedException ex) { return (null, ex.Message); }
            string I(string k) => info.TryGetValue(k, out var v) ? v : null;
            long L(string k) => long.TryParse(I(k), NumberStyles.None, CultureInfo.InvariantCulture, out var v) ? v : -1;
            if (I("system") == "crypt" && I("key") != "yes") return (null, "The console's key for its system part is not available in this session (its startup software did not provide it), so the system part cannot be written as the console reads it.");
            if (I("system") == "none" || I("comp") == null || I("comp") == "none") return (null, "The console's current system part could not be read as a SquashFS system, so the new one's dependencies cannot be compared with it.");
            if (I("comp") != fs.Compression.ToString(CultureInfo.InvariantCulture)) return (null, "This image's compression (" + fs.CompressionName + ") is not the one the console's current system uses, which is the only one its startup software is known to read.");
            if (string.IsNullOrEmpty(board) || board != (I("board") ?? id.Board)) return (null, "This image is for another console (its board type is \"" + board + "\"; this console's is \"" + (I("board") ?? id.Board) + "\").");
            long part = L("part_bytes");
            if (part <= 0 || image.LongLength > part) return (null, "This image (" + Units.Gb(image.LongLength) + ") is larger than the console's system part (" + Units.Gb(Math.Max(0, part)) + ").");
            if (L("mem_free") >= 0 && L("mem_free") < image.LongLength + Reserve) return (null, "The console does not have enough free memory to hold this image before writing it (" + Units.Gb(L("mem_free")) + " free).");
            // required evidence, not a remark: the startup software read whole, its header valid and its command line starting the console from
            // the system part. A read, size or header failure refuses the plan (it is never written past as a note); the startup read here is
            // bound in the plan and read again at the confirmation
            var su = con.ReadStartup();
            if (!su.Ok) return (null, "The console's startup software " + su.Text + ", so whether it starts the console from the system part could not be checked. Nothing was changed.");
            var words = su.Cmdline.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (!words.Contains("root=/dev/nandb")) return (null, "The console's startup software does not start its system from the system part (root=/dev/nandb), so a new system part would not be used.");
            var (now, _, ok) = con.Digest("nandb"); if (!ok || now == null) return (null, "The console's system part could not be read in full, so it is not changed.");
            var p = new SystemPlan { Hwid = point.Hwid, ConsoleTag = point.ConsoleTag, Session = con.SessionId, Simulated = con.Simulated, Point = point, Image = image, Origin = origin, NowMd5 = now, WantSha256 = Sha(image), Bytes = image.LongLength,
                PartBytes = part, BytesUsed = (long)fs.BytesUsed, CurrentBytesUsed = L("bytes_used"), Board = board, Encryption = I("system"), Compression = fs.CompressionName,
                StartupMd5 = su.Md5, StartupCmdline = su.Cmdline };
            p.Notes.Add(p.Encryption == "crypt" ? "Only the system part is written, through the console's own encryption with the key its startup software holds; the key is read on the console and never leaves it." : "Only the system part is written.");
            p.Notes.Add("The rest of the system part after the image is left as it is. The startup software, the startup stages, the storage table and the data storage are not written.");
            p.Notes.Add("If this console does not start with the new system, connect it in recovery mode and use Console Restore with your restore point (" + point.DateText + "): it brings the system part back.");
            p.Notes.Add(point.Banner);
            p.Nonce = Hex.Of(Guid.NewGuid().ToByteArray()); p.ExpiresUtc = DateTime.UtcNow + RestoreEngine.ConfirmWindow; p.Digest = DigestOf(p);
            return (p, null);
        }

        static string DigestOf(SystemPlan p)
        {
            var s = "ccs-dev-system-plan/2\n" + ConsoleIdentity.NormalizeHwid(p.Hwid) + "\n" + p.Session + "\n" + p.Point?.Archive?.DigestLine() + "\n" + p.NowMd5 + "\n" + p.WantSha256 + " " + p.Bytes.ToString(CultureInfo.InvariantCulture) + " " + p.Board + " " + p.Compression + " " + p.Encryption + "\n" + p.StartupMd5 + " " + p.StartupCmdline + "\n" + p.Nonce;
            using (var h = SHA256.Create()) return Hex.Of(h.ComputeHash(Encoding.UTF8.GetBytes(s)));
        }

        public static FileOutcome Flash(RecoveryConsole con, SystemPlan plan, string digest, string nonce, EngineContext ctx)
        {
            var o = new FileOutcome();
            FileOutcome Refuse(string why) { o.State = "refused"; o.Problem = why; return o; }
            if (plan == null) return Refuse("No plan.");
            if (digest != plan.Digest || nonce != plan.Nonce || plan.Nonce == null || DigestOf(plan) != plan.Digest) return Refuse("The confirmation does not match the reviewed change.");
            if (DateTime.UtcNow > plan.ExpiresUtc) return Refuse("The confirmation is more than 10 minutes old. Review the change again.");
            if (con.SessionId != plan.Session) return Refuse("The console was connected again after the review. Nothing was changed.");
            var id = con.Identify(); if (!ConsoleIdentity.SameConsole(id.Hwid, plan.Hwid)) return Refuse("A different console is connected now. Nothing was changed.");
            if (plan.Image == null || Sha(plan.Image) != plan.WantSha256 || plan.Image.LongLength != plan.Bytes) return Refuse("The image changed since the review. Nothing was changed.");
            // the startup software the plan was checked against, read again: a changed, unreadable or invalid startup voids the plan before anything is written
            var su = con.ReadStartup();
            if (!su.Ok) return Refuse("The console's startup software " + su.Text + ", so the reviewed plan cannot be used. Nothing was changed.");
            if (su.Md5 != plan.StartupMd5 || su.Cmdline != plan.StartupCmdline) return Refuse("The console's startup software changed since the review. Nothing was changed; review the change again.");
            var (now, _, ok) = con.Digest("nandb"); if (!ok || now != plan.NowMd5) return Refuse("The system part changed since the review (or could not be read). Nothing was changed.");
            using (var hold = Evidence.Hold.Open(plan.Point?.Archive?.Path))
            {
                if (hold.Problem != null) return Refuse("The restore point cannot be used (" + hold.Problem + "). Nothing was changed.");
                var rp = RestorePoints.Require(plan.Point, con, RestorePoints.SystemPart, ctx.Cancelled); if (rp != null) return Refuse(rp + " Nothing was changed.");
                Record rec;
                try { rec = Record.Start(ctx.Settings.RecordsFolder, Guid.NewGuid().ToString("N").Substring(0, 12), "developer-system", new JObj { { "plan", plan.ToJson() }, { "console_tag", plan.ConsoleTag }, { "simulated", con.Simulated } }); }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { return Refuse("The change record could not be saved (" + ex.Message + "), so nothing was written."); }
                plan.Nonce = null; o.RecordFile = rec.File;
                var auth = new WriteAuthority(plan.Digest, plan.Hwid, new[] { "sstage", "sburn" }, () => con.Simulated || Gate.DeveloperWrites, Gate.DeveloperText);
                rec.State("writing"); con.Card("writing"); ctx.Say("system", "Sending the system image to the console's memory...", 0, plan.Bytes);
                WriteResult st;
                try { st = con.DevSystemStep("sstage", auth, new[] { plan.Bytes.ToString(CultureInfo.InvariantCulture), plan.WantSha256 }, new MemoryStream(plan.Image, false), plan.Bytes); }
                catch (GateClosedException ex) { rec.State("refused", ex.Message); o.State = "refused"; o.Problem = ex.Message; return o; }
                if (!st.Ok) { rec.State("stopped", st.Problem); con.Card("stopped"); o.State = "stopped"; o.Problem = "The console did not accept the image (" + st.Problem + "). Nothing was written."; return o; }
                ctx.Say("system", "Writing the system part...");
                var r = con.DevSystemStep("sburn", auth, new[] { plan.NowMd5, plan.WantSha256 }, null, 0);
                rec.Set("result", r.Line ?? r.Problem).Save();
                if (!r.Ok) { rec.State("stopped", r.Problem); con.Card("stopped"); o.State = "stopped"; o.Problem = (r.Began ? "Writing the system part did not complete or did not read back (" + r.Problem + "). Nothing was written again." : "The console refused the write (" + r.Problem + "). Nothing was written.") + " Use Console Restore with your restore point (" + plan.Point.DateText + ")" + (r.Began ? "." : " if anything changed."); return o; }
                o.Done.Add("system part"); rec.State("changed"); con.Card("done"); o.State = "changed"; return o;
            }
        }
    }
}
