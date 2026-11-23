// Classic Cart Studio console backup and restore engine. Copyright (C) 2026 Rance9. GPL-3.0-or-later.
// Expert Mode and Developer Mode's SESSION RESTORE POINT: a deliberate policy for those two modes only, kept apart from the
// strict operations by type. Console Restore, Console Rescue, Console Composer and first-time preparation take an Admission ticket and a
// backup captured in their own session (Evidence); they have no overload that takes a SessionRestorePoint, and Expert/Developer
// operations take nothing else.
//   establish  before the first write of a connection: a confirmed backup of THIS console (same hardware id, simulated/real alike), verified
//              from scratch, chosen or found by the owner. It may be older than the console's current state: that is the policy. It becomes
//              the session's restore point, bound to this connection (a reconnection or restart needs it again; a record on disk never
//              re-activates it).
//   covers     what that backup can bring back, computed from its contents and the console now: user files (its data storage is listable),
//              the kernel (its startup software), the system (its system part), the layout (the console's storage table still equals the
//              backup's), the startup stages (boot0 and boot1 as Console Rescue takes them: recorded and not recorded as damaged). An
//              operation is refused when its recovery route is not covered. Developer Mode's test boot names the console only by its
//              hardware id (the console is in its boot ROM's USB mode: EstablishFor): there the coverage is the backup's own, the layout is
//              never claimed, and Console Restore checks the layout itself when it is used.
//   notice     shown continuously with its date: "Restoring returns this console to that restore point. Changes and saves made after it are
//              not protected."
//   update     Update Restore Point captures the console now in this session; if that fails, the earlier point stays valid.
// Rollback is a guided return to this point through Console Restore, under Restore's own strict rules (fresh protection, its plan, its
// confirmation): never an automatic flash.
using CcsBackup.Archive;
using CcsBackup.Device;
using CcsBackup.Library;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace CcsBackup.Engine
{
    public sealed class SessionRestorePoint
    {
        public const string NoticeText = "Restoring returns this console to that restore point. Changes and saves made after it are not protected.";
        public string Hwid, ConsoleTag, Session, MadeUtc; public BoundArchive Archive; public DateTime EstablishedUtc; public bool Simulated;
        /// <summary>false when the console's storage table could not be read when the point was chosen (Developer Mode's test boot): a
        /// RecoveryConsole operation never accepts it then</summary>
        public bool LayoutChecked = true;
        public HashSet<string> Covers = new HashSet<string>();
        public string DateText => DateTime.TryParse(MadeUtc, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var t) ? t.ToLocalTime().ToString("MMMM d, yyyy h:mm tt", CultureInfo.GetCultureInfo("en-US")) : "a date not recorded";
        public string Banner => "Restore point: " + DateText + ". " + NoticeText;
        public JObj ToJson() => new JObj { { "console_tag", ConsoleTag }, { "made_utc", MadeUtc }, { "established_utc", EstablishedUtc.ToString("o", CultureInfo.InvariantCulture) }, { "covers", Covers.OrderBy(x => x).ToList<object>() }, { "archive", Archive?.ToJson() }, { "notice", NoticeText } };
    }

    public static class RestorePoints
    {
        public const string UserFiles = "user-files", Kernel = "kernel", SystemPart = "system", Layout = "layout", BootStages = "boot-stages";

        /// <summary>a verified backup of this console as the session restore point (null with the reason otherwise). Reads only.</summary>
        public static (SessionRestorePoint point, string why) Establish(RecoveryConsole con, string backupPath, Func<bool> cancelled)
        {
            if (string.IsNullOrEmpty(backupPath) || !File.Exists(backupPath)) return (null, "Choose a backup of this console to use as the restore point.");
            ConsoleIdentity id;
            try { id = con.Identify(); } catch (ConsoleRefusedException ex) { return (null, ex.Message); }
            var mw = ConsoleModels.RefusalFor(id, "files"); if (mw != null) return (null, mw);
            return EstablishFor(id.Hwid, con.Simulated, con.SessionId, id.Get("parttable") ?? "", backupPath, cancelled);
        }

        /// <summary>the same for a connection that knows the console only by its hardware id (Developer Mode's test boot: the console is in
        /// its boot ROM's USB mode, its storage table cannot be read: parttableNow null). Reads only.</summary>
        public static (SessionRestorePoint point, string why) EstablishFor(string hwid, bool simulated, string session, string parttableNow, string backupPath, Func<bool> cancelled)
        {
            if (string.IsNullOrEmpty(backupPath) || !File.Exists(backupPath)) return (null, "Choose a backup of this console to use as the restore point.");
            var v = Verifier.VerifyFile(backupPath, null, cancelled);
            if (!v.Ok) return (null, "This backup did not pass its check: " + v.Problems[0]);
            if (!ConsoleIdentity.SameConsole(v.Hwid, hwid)) return (null, "This backup belongs to a different console (console " + v.ConsoleTag + "). Another console's backup can be a source in Console Composer, but never this console's restore point.");
            if (v.Simulated != simulated) return (null, "A simulated console's backup and a real console are never mixed.");
            if (!v.RestoreReady) return (null, "This backup does not hold everything a restore needs, so it cannot be a restore point.");
            var p = new SessionRestorePoint { Hwid = ConsoleIdentity.NormalizeHwid(hwid), ConsoleTag = ConsoleIdentity.HwidTag(hwid), Session = session, MadeUtc = v.Manifest.S("completed_utc"), EstablishedUtc = DateTime.UtcNow, Simulated = simulated, Archive = BoundArchive.From("session restore point", backupPath, v), LayoutChecked = parttableNow != null };
            var layoutSame = parttableNow != null && v.Manifest.O("layout")?.S("partition_table") == parttableNow;
            var layoutOk = layoutSame || parttableNow == null;   // unknown here: the backup's own coverage, and Console Restore checks the layout when used
            if (layoutSame) p.Covers.Add(Layout);
            var dataDev = (v.Manifest.O("layout")?.A("partitions") ?? new List<object>()).OfType<Dictionary<string, object>>().FirstOrDefault(x => x.S("label") == "data" || x.S("label") == "rootfs_data")?.S("device");
            if (layoutOk && dataDev != null && v.Members.ContainsKey("restore/" + dataDev + ".img") && v.Members.ContainsKey(BackupFormat.InventoryName)) p.Covers.Add(UserFiles);
            if (layoutOk && v.Members.ContainsKey("restore/boot2-active.img")) p.Covers.Add(Kernel);
            var sysDev = SystemDevice(v);
            if (layoutOk && sysDev != null && v.Members.ContainsKey("restore/" + sysDev + ".img")) p.Covers.Add(SystemPart);
            if (v.Members.ContainsKey("reference/boot0.bin") && v.Members.ContainsKey("reference/boot1.bin") && !v.DamagedComponents.ContainsKey("boot0") && !v.DamagedComponents.ContainsKey("boot1")) p.Covers.Add(BootStages);
            return (p, null);
        }

        /// <summary>the device of the backup's system part (label rootfs or system), or null</summary>
        public static string SystemDevice(Verification v) => (v.Manifest.O("layout")?.A("partitions") ?? new List<object>()).OfType<Dictionary<string, object>>().FirstOrDefault(x => x.S("label") == "rootfs" || x.S("label") == "system")?.S("device");

        /// <summary>null when the point may protect an operation needing capability on this connection; otherwise why not. Verifies the
        /// backup again (the point names exact bytes) unless quick (between file changes of one session, the hold of the operation covers it).</summary>
        public static string Require(SessionRestorePoint p, RecoveryConsole con, string capability, Func<bool> cancelled)
        {
            if (p == null) return "Choose a restore point first: a confirmed backup of this console.";
            if (p.Session != con.SessionId) return "The console was connected again, so this session needs its restore point chosen again.";
            if (!ConsoleIdentity.SameConsole(p.Hwid, con.Hwid ?? con.Identify().Hwid)) return "A different console is connected now.";
            if (!p.LayoutChecked) return "This restore point was chosen without reading the console's storage table; choose it again in this session.";
            if (!p.Covers.Contains(capability)) return capability == UserFiles ? "This restore point cannot bring back the console's files (its storage is divided differently now, or the backup has no file list)."
                : capability == Kernel ? "This restore point cannot bring back the startup software, so a kernel change is not allowed under it." : capability == SystemPart ? "This restore point does not hold the system part, so a system change is not allowed under it."
                : capability == BootStages ? "This restore point does not hold this console's startup stages (boot0 and boot1) as Console Rescue needs them, so code that could change them is not started under it."
                : "This restore point does not cover this change.";
            return Evidence.Recheck(p.Archive, p.Hwid, con.Simulated, null, cancelled);
        }

        /// <summary>Update Restore Point: a complete backup of the console now, in this session; on any failure the earlier point stays.</summary>
        public static (SessionRestorePoint point, string why) Update(RecoveryConsole con, EngineContext ctx, SessionRestorePoint earlier)
        {
            var b = CaptureEngine.Run(con, ctx);
            if (!b.Ok || b.Verification == null || !b.Verification.Ok) return (earlier, "The new backup did not complete (" + (b.Problem ?? b.State) + "). Your earlier restore point is still the one in use.");
            var (p, why) = Establish(con, b.PrimaryPath, ctx.Cancelled);
            return p == null ? (earlier, why + " Your earlier restore point is still the one in use.") : (p, null);
        }
    }
}
