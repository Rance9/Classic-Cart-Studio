// Classic Cart Studio console backup and restore engine. Copyright (C) 2026 Rance9. GPL-3.0-or-later.
// Developer Mode's typed operations beyond file changes (FilesEngine scope "dev"), each under the SESSION RESTORE POINT:
//   kernel flash   a developer's startup image (an Android boot image) written once as the startup software (ram/ccsdev kstage/kburn),
//                  read back; Gate.DeveloperWrites. Allowed only when the restore point holds this console's own startup software
//                  (RestorePoints.Kernel: same storage layout) so a guided return exists: Console Restore with the restore point (strict
//                  policy, its own plan). The image must fit the console's own startup layout (CcsConsole.RamBoot.StartupImage: page size,
//                  load addresses, header version, no second stage, a zImage kernel, its own checksum, a plain command line, a known ramdisk
//                  format), the slot's size, and must not be known to lack the console's key when it asks to open the system part; its
//                  command line is compared with the console's current one (a key it carries is never shown). The spare slot, bootloaders,
//                  storage table and data are never written.
//   system image   the console's system part replaced by an owner's SquashFS image of the console's own system-part class (DeveloperSystem).
//   RAM test boot  a startup image started from the console's memory without writing storage (TestBootEngine, Gate.DeveloperTestBoot):
//                  the restore point's own startup software, or a startup image file of the console's layout. Only Classic Cart Studio's
//                  own read-only session is a "nothing installed on your console" session (ClassifyRamImage); any other image's code controls
//                  the console once it starts and can write its storage, startup stages or keys, and the plan says so.
//   spare slot (68) never written by Developer Mode: it holds the original kernel that Return to Original Software depends on.
//   boot0/boot1/partition table: never Developer Mode operations (Console Rescue writes only this console's own recorded stages).
using CcsBackup.Archive;
using CcsBackup.Device;
using CcsBackup.Library;
using CcsConsole.RamBoot;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace CcsBackup.Engine
{
    public sealed class KernelPlan
    {
        public string Hwid, ConsoleTag, Session, NowMd5, WantMd5, WantSha256, Digest, Nonce, RamdiskFormat; public long Size; public DateTime ExpiresUtc; public bool Simulated;
        public SessionRestorePoint Point; public byte[] Image; public List<string> Notes = new List<string>(), CmdlineDifferences = new List<string>(), Dependencies = new List<string>();
        public JObj ToJson() => new JObj { { "console_tag", ConsoleTag }, { "now_md5", NowMd5 }, { "want_md5", WantMd5 }, { "want_sha256", WantSha256 }, { "bytes", Size }, { "ramdisk_format", RamdiskFormat }, { "restore_point", Point?.ToJson() },
            { "notice", SessionRestorePoint.NoticeText }, { "code_notice", DeveloperEngine.KernelCodeNotice }, { "cmdline_differences", CmdlineDifferences }, { "dependencies", Dependencies }, { "notes", Notes }, { "digest", Digest }, { "nonce", Nonce }, { "expires_utc", ExpiresUtc.ToString("o", CultureInfo.InvariantCulture) } };
    }

    public static class DeveloperEngine
    {
        /// <summary>the startup software review's warning (the system part's own is DeveloperSystem.CodeNotice)</summary>
        public const string KernelCodeNotice = "Code in this startup image controls the console once it starts. It can write the console's storage, startup stages or keys; a restore point cannot undo what it does to the startup stages or keys.";
        public const string RamBootLimitation = "A startup image you test in memory controls the console once it starts: it could write the console's storage, startup stages or keys, and nothing in the loader can stop that.";
        public const long SlotMax = 7340032;   // the startup slot

        /// <summary>the qualified "nothing installed on your console" class: only Classic Cart Studio's own read-only backup image; any other
        /// image (a test boot, TestBootEngine) is never described that way</summary>
        public static (bool qualified, string why) ClassifyRamImage(string manifestImageId) =>
            manifestImageId != null && manifestImageId.StartsWith("ccs-backup/", StringComparison.Ordinal) && manifestImageId == RecoveryImages.IdOf(ImageKind.Backup).id
                ? (true, "Classic Cart Studio's read-only session: nothing installed on your console.") : (false, RamBootLimitation);

        static string Md5(byte[] b) { using (var h = MD5.Create()) return Hex.Of(h.ComputeHash(b)); }
        static string Sha(byte[] b) { using (var h = SHA256.Create()) return Hex.Of(h.ComputeHash(b)); }

        /// <summary>the image's own header (Android boot image v0): its size, and its command line; null with the reason when it is not one</summary>
        public static (long size, string cmdline, string why) BootHeader(byte[] img)
        {
            if (img == null || img.Length < 1632 || Encoding.ASCII.GetString(img, 0, 8) != "ANDROID!") return (0, null, "not an Android boot image");
            uint U(int o) => (uint)(img[o] | img[o + 1] << 8 | img[o + 2] << 16 | img[o + 3] << 24);
            long ks = U(8), rs = U(16), ss = U(24), ps = U(36);
            if (ps != 2048 || ks == 0 || ks > 16_000_000 || rs > 16_000_000 || ss > 16_000_000) return (0, null, "its header sizes are not a startup image's");
            long Pages(long n) => (n + ps - 1) / ps;
            long size = ps * (1 + Pages(ks) + Pages(rs) + Pages(ss));
            if (size > img.Length) return (0, null, "the image is shorter than its header says");
            if (size > SlotMax) return (0, null, "larger than the startup slot");
            var cmd = Encoding.ASCII.GetString(img, 64, 512).TrimEnd('\0');
            return (size, cmd, null);
        }

        public static (KernelPlan plan, string why) PlanKernel(RecoveryConsole con, SessionRestorePoint point, byte[] image, Func<bool> cancelled)
        {
            if (con.Profile != SessionProfile.Files) return (null, "The console is not in a file session.");
            var rp = RestorePoints.Require(point, con, RestorePoints.Kernel, cancelled); if (rp != null) return (null, rp);
            var (size, cmd, bad) = BootHeader(image); if (bad != null) return (null, "This file cannot be the startup software: " + bad + ".");
            var f = StartupImage.Inspect(image); if (!f.Ok) return (null, "This file cannot be the startup software: " + string.Join("; ", f.Problems) + ".");
            if (f.Bytes != image.LongLength) return (null, "This file cannot be the startup software: it has padding after its last part, which the startup slot does not keep (it reads back only what the header describes), so its readback could never match; use the image without the padding.");
            var keys = TestBootEngine.KeyDependencies(f);
            if (f.Words.Contains("decrypt") && f.KeyInRamdisk == false && !f.HakchiKeyFallback && !f.Words.Any(w => w.StartsWith("hakchi-key-file=", StringComparison.Ordinal)))
                return (null, "This file cannot be the startup software: " + keys[0]);
            var id = con.Identify(); var mw = ConsoleModels.RefusalFor(id, "developer"); if (mw != null) return (null, mw + " Nothing was changed.");
            var p = new KernelPlan { Hwid = id.Hwid, ConsoleTag = id.ShortTag, Session = con.SessionId, Simulated = con.Simulated, Point = point, Image = image, NowMd5 = id.ActiveMd5, WantMd5 = Md5(image), WantSha256 = Sha(image), Size = image.Length, RamdiskFormat = f.RamdiskFormat };
            if (p.NowMd5 == p.WantMd5) return (null, "This image is already the console's startup software.");
            // the current startup is only compared here (the plan replaces it whole, bound by its MD5), but a failed read is a connection or
            // reader problem, never a note to write past; a current startup that reads back but is not a valid image is only described
            var su = con.ReadStartup();
            if (su.State == "read-failed") return (null, "The console's startup software " + su.Text + ". Nothing was changed; check the connection and review the change again.");
            var now = su.Ok ? su.Cmdline : null;
            p.CmdlineDifferences.AddRange(TestBootEngine.Differences(now, cmd));
            p.Dependencies.AddRange(keys);
            if (!f.Words.Contains("root=/dev/nandb")) p.Dependencies.Add("Its command line does not start the console's system part (root=/dev/nandb).");
            p.Dependencies.Add("Its kernel must run on this console; test it in memory first (Test Boot), which writes nothing to the console's storage.");
            if (now == null) p.Notes.Add("The console's current startup software " + su.Text + ", so its command line could not be compared.");
            p.Notes.Add("Only the startup software is written. If this console does not start with it, connect it in recovery mode and use Console Restore with your restore point (" + point.DateText + ").");
            p.Notes.Add(point.Banner);
            p.Nonce = Hex.Of(Guid.NewGuid().ToByteArray()); p.ExpiresUtc = DateTime.UtcNow + RestoreEngine.ConfirmWindow; p.Digest = DigestOf(p);
            return (p, null);
        }

        static string DigestOf(KernelPlan p)
        {
            using (var h = SHA256.Create()) return Hex.Of(h.ComputeHash(Encoding.UTF8.GetBytes("ccs-dev-kernel-plan/1\n" + ConsoleIdentity.NormalizeHwid(p.Hwid) + "\n" + p.Session + "\n" + p.Point?.Archive?.DigestLine() + "\n" + p.NowMd5 + "\n" + p.WantMd5 + " " + p.WantSha256 + " " + p.Size.ToString(CultureInfo.InvariantCulture) + "\n" + p.Nonce)));
        }

        public static FileOutcome FlashKernel(RecoveryConsole con, KernelPlan plan, string digest, string nonce, EngineContext ctx)
        {
            var o = new FileOutcome();
            FileOutcome Refuse(string why) { o.State = "refused"; o.Problem = why; return o; }
            if (plan == null) return Refuse("No plan.");
            if (digest != plan.Digest || nonce != plan.Nonce || plan.Nonce == null || DigestOf(plan) != plan.Digest) return Refuse("The confirmation does not match the reviewed change.");
            if (DateTime.UtcNow > plan.ExpiresUtc) return Refuse("The confirmation is more than 10 minutes old. Review the change again.");
            if (con.SessionId != plan.Session) return Refuse("The console was connected again after the review. Nothing was changed.");
            var id = con.Identify(); if (!ConsoleIdentity.SameConsole(id.Hwid, plan.Hwid)) return Refuse("A different console is connected now. Nothing was changed.");
            if (id.ActiveMd5 != plan.NowMd5) return Refuse("The startup software changed since the review. Nothing was changed.");
            if (Md5(plan.Image) != plan.WantMd5 || Sha(plan.Image) != plan.WantSha256) return Refuse("The image changed since the review. Nothing was changed.");
            using (var hold = Evidence.Hold.Open(plan.Point?.Archive?.Path))
            {
                if (hold.Problem != null) return Refuse("The restore point cannot be used (" + hold.Problem + "). Nothing was changed.");
                var rp = RestorePoints.Require(plan.Point, con, RestorePoints.Kernel, ctx.Cancelled); if (rp != null) return Refuse(rp + " Nothing was changed.");
                Record rec;
                try { rec = Record.Start(ctx.Settings.RecordsFolder, Guid.NewGuid().ToString("N").Substring(0, 12), "developer-kernel", new JObj { { "plan", plan.ToJson() }, { "console_tag", plan.ConsoleTag }, { "simulated", con.Simulated } }); }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { return Refuse("The change record could not be saved (" + ex.Message + "), so nothing was written."); }
                plan.Nonce = null; o.RecordFile = rec.File;
                var auth = new WriteAuthority(plan.Digest, plan.Hwid, new[] { "kstage", "kburn" }, () => con.Simulated || Gate.DeveloperWrites, Gate.DeveloperText);
                rec.State("writing"); con.Card("writing");
                var st = con.DevStep("kstage", auth, new[] { plan.Size.ToString(CultureInfo.InvariantCulture), plan.WantMd5 }, plan.Image);
                if (!st.Ok) { rec.State("stopped", st.Problem); o.State = "stopped"; o.Problem = "The console did not accept the image (" + st.Problem + "). Nothing was written."; return o; }
                var r = con.DevStep("kburn", auth, new[] { plan.NowMd5, plan.WantMd5 });
                rec.Set("result", r.Line ?? r.Problem).Save();
                if (!r.Ok) { rec.State("stopped", r.Problem); con.Card("stopped"); o.State = "stopped"; o.Problem = "Writing the startup software did not complete or did not read back (" + r.Problem + "). Nothing was written again. Use Console Restore with your restore point (" + plan.Point.DateText + ")."; return o; }
                o.Done.Add("startup software"); rec.State("changed"); con.Card("done"); o.State = "changed"; return o;
            }
        }
    }
}
