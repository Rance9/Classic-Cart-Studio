// Classic Cart Studio console backup and restore engine tests: CCS Mod's start at power-on as an explicit choice in the Workbench (Prepare
// Console and Console Restore's change to CCS Mod), off by default. GPL-3.0-or-later. Simulated consoles only (files on this PC); the
// real-console boundaries (Gate.PreparationWrites, Gate.MethodWrites) stay closed. The choice is recorded as CCS Mod's own setting (ccsprep
// mstart, before the method's package) and applied by CCS Mod's install at the console's next normal start (SimulatedConsole.NormalStart
// stands in for it; tools/console-nucleus/test_nucleus.py runs CCS Mod's own install and start scripts under qemu-arm).
using CcsBackup.Device;
using CcsBackup.Engine;
using CcsBackup.Library;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using static CcsBackup.Tests.Program;

namespace CcsBackup.Tests
{
    static partial class Tests
    {
        static string ModStartNow(string c) => (File.Exists(Path.Combine(c, "ccs-mod-config.txt")) ? File.ReadAllText(Path.Combine(c, "ccs-mod-config.txt")).Trim() : "none") + " " + (File.Exists(Path.Combine(c, "ccs-mod-s81.txt")) ? File.ReadAllText(Path.Combine(c, "ccs-mod-s81.txt")).Trim() : "absent");

        public static void ModStart()
        {
            var sM = SettingsIn(NewDir("modstart"), false);
            // ---- Console Restore, change from CCS Nucleus to CCS Mod: off by default (nothing about the start is written)
            var c = SmallConsole("modified", true); SetInstalled(c, "nucleus", "ccs-nucleus");
            var con = RecoverySessions.OpenSimulated(c, ImageKind.Method); var ctx = Ctx(sM, ImageKind.Method);
            var (p, r) = MethodEngine.Prepare(con, ctx, "change", "mod", HomePackage.Folder(MakeHome("mod", "s1"), "mod"));
            Check(r == null && p.ModStart == "off" && !p.Steps.Contains("mstart") && p.Notes.Any(n => n.StartsWith("The console starts in its own menu")) && Json.Parse(Json.Write(p.ToJson())).S("mod_start") == "off"
                  && p.Before.ModStartSetting == "none" && p.Before.ModStartScript == "absent",
                "change to CCS Mod, the choice off (the default): no start step, and the review says the console starts in its own menu (" + string.Join(",", p?.Steps ?? new List<string>()) + "; " + r?.Problem + ")");
            var o = MethodEngine.Execute(con, p, p.Digest, p.Nonce, ctx); Gone(p.ProtectivePath);
            var st = SimulatedConsole.NormalStart(c);
            Check(o.State == "scheduled" && !Writes(c).Any(w => w.StartsWith("mstart")) && st.Contains("install ccs-mod") && !st.Any(x => x.StartsWith("ccs-mod start")) && ModStartNow(c) == "none absent",
                "the next normal start installs CCS Mod; the console's start is unchanged (" + string.Join("; ", st) + ")");
            GoneConsole(c);

            // ---- the choice on: CCS Mod's own setting recorded first, then its package; its install at the next start turns the start on
            c = SmallConsole("modified", true); SetInstalled(c, "nucleus", "ccs-nucleus");
            con = RecoverySessions.OpenSimulated(c, ImageKind.Method);
            (p, r) = MethodEngine.Prepare(con, ctx, "change", "mod", HomePackage.Folder(MakeHome("mod", "s2"), "mod"), true);
            Check(r == null && p.ModStart == "on" && p.Steps.SequenceEqual(new[] { "mstart", "method", "unlist", "hput", "huse" }) && p.Notes.Any(n => n.StartsWith("When the console is turned on, CCS HOME opens first")),
                "change to CCS Mod with CCS HOME at power-on: CCS Mod's setting first, then its package and the removal list, then CCS HOME (" + string.Join(",", p?.Steps ?? new List<string>()) + "; " + r?.Problem + ")");
            Check(MethodEngine.StepLabel("mstart", p).Contains("CCS HOME at power-on") && Methods.Steps.Contains("mstart"), "the review names the step in words: " + MethodEngine.StepLabel("mstart", p));
            var d = p.Digest; p.ModStart = "off";
            Check(MethodEngine.Execute(con, p, d, p.Nonce, ctx).State == "refused" && Writes(c).Count == 0, "the choice is part of the plan's digest: a plan changed after the review is refused, nothing written");
            p.ModStart = "on";
            o = MethodEngine.Execute(con, p, p.Digest, p.Nonce, ctx); Gone(p.ProtectivePath);
            Check(o.State == "scheduled" && Writes(c).Take(3).SequenceEqual(new[] { "mstart on", "method ccs-mod", "unlist ccs-nucleus" }) && ModStartNow(c) == "on absent" && Record.Load(o.RecordFile).O("after")?.S("mod_start") == "on",
                "written in that order and read back; the setting waits for CCS Mod's install; the record names the choice (" + o.Problem + "; " + string.Join(", ", Writes(c)) + ")");
            st = SimulatedConsole.NormalStart(c);
            Check(st.SequenceEqual(new[] { "uninstall ccs-nucleus", "install ccs-mod", "ccs-mod start on", "transfer folder removed; restart" }) && ModStartNow(c) == "on ours" && Installed(c).policy == "mod",
                "the next normal start: hakchi removes CCS Nucleus, installs CCS Mod, whose install turns CCS HOME at power-on on with its own command (" + string.Join("; ", st) + ")");
            GoneConsole(c);

            // ---- an earlier CCS Mod's setting (on) kept on the console, the choice off: recorded off, so CCS Mod's install does not turn it on
            c = SmallConsole("modified", true); SetInstalled(c, "nucleus", "ccs-nucleus"); File.WriteAllText(Path.Combine(c, "ccs-mod-config.txt"), "on\n");
            con = RecoverySessions.OpenSimulated(c, ImageKind.Method);
            (p, r) = MethodEngine.Prepare(con, ctx, "change", "mod", HomePackage.Folder(MakeHome("mod", "s3"), "mod"));
            o = r != null ? null : MethodEngine.Execute(con, p, p.Digest, p.Nonce, ctx); Gone(p?.ProtectivePath); st = SimulatedConsole.NormalStart(c);
            Check(p != null && p.Steps.First() == "mstart" && o.Ok && Writes(c).First() == "mstart off" && ModStartNow(c) == "off absent" && !st.Contains("ccs-mod start on"),
                "a setting left on by an earlier CCS Mod, the choice off: CCS Mod's setting is recorded off first, and the console's start stays its own (" + (r?.Problem ?? string.Join("; ", st)) + ")");
            GoneConsole(c);

            // ---- refusals: CCS Nucleus is not offered it; a start script that CCS Mod did not write is kept
            c = SmallConsole("modified", true); SetInstalled(c, "mod", "ccs-mod");
            (p, r) = MethodEngine.Prepare(RecoverySessions.OpenSimulated(c, ImageKind.Method), ctx, "change", "nucleus", null, true);
            Check(p == null && r.Problem.Contains("CCS Mod only") && Writes(c).Count == 0, "changing to CCS Nucleus with CCS HOME at power-on: refused, nothing written (" + r?.Problem + ")");
            (p, r) = MethodEngine.Prepare(RecoverySessions.OpenSimulated(c, ImageKind.Method), ctx, "remove", "mod", null, true);
            Check(p == null && r.Problem.Contains("CCS Mod only"), "removing with it: refused (" + r?.Problem + ")");
            GoneConsole(c);
            c = SmallConsole("modified", true); SetInstalled(c, "nucleus", "ccs-nucleus"); File.WriteAllText(Path.Combine(c, "ccs-mod-s81.txt"), "other\n");
            (p, r) = MethodEngine.Prepare(RecoverySessions.OpenSimulated(c, ImageKind.Method), ctx, "change", "mod", HomePackage.Folder(MakeHome("mod", "s4"), "mod"), true);
            Check(p == null && r.Problem.Contains("S81ccs-mod") && Writes(c).Count == 0, "a start script named S81ccs-mod that CCS Mod did not write: kept, and turning the start on is refused (" + r?.Problem + ")");
            GoneConsole(c); GoneBackups(sM);
            var cs = MethodState.Parse("hakchi=v1.0.4-126\npackage=ccs-mod\npolicy=mod\nmodstart=on ours\nfree=1\nregistry=none\nhome_pointer=absent\nhome_identity=absent\nram=1\nresult mstate ok\n", 0);
            var ch = MethodState.Parse("hakchi=v1.0.4-126\npackage=ccs-mod\npolicy=mod\nmodstart=off absent\nfree=1\nregistry=none\nhome_pointer=absent\nhome_identity=absent\nram=1\nresult mstate ok\n", 0);
            Check(cs.Ok && cs.ModStartOn && ch.Ok && !ch.ModStartOn && cs.Digest() != ch.Digest() && !MethodState.Parse("hakchi=x\npolicy=none\nmodstart=maybe ours\nregistry=none\nhome_pointer=absent\nresult mstate ok\n", 0).Ok,
                "mstate's CCS Mod start line: on only with its setting on and its own start script; part of the console state the confirmation compares; anything else is refused");

            // ---- Prepare Console on a console already prepared with CCS Mod: a changed choice places CCS Mod's own package again, so its
            // install applies the setting; the same choice changes nothing (the full-size "modified" fixture: hakchi's files are read from the backup)
            var s = SettingsIn(NewDir("modstart-already"), false);
            var c3 = Console_("modified"); Backup(c3, s); SetInstalled(c3, "mod", "ccs-mod");
            var (pa, ra, cona, ctxa, adma) = PrepPlanWith(c3, s, "mod", null, true);
            Check(pa != null && pa.State == "already" && pa.Refusal == null && pa.Steps.Take(2).SequenceEqual(new[] { "mstart", "method" }) && pa.ModStart == "on" && pa.Text.Contains("only CCS HOME at power-on changes"),
                "already prepared with CCS Mod, CCS HOME at power-on chosen: CCS Mod's setting, then its own package again (no kernel, bootloader, system or method change) (" + string.Join(",", pa?.Steps ?? new List<string>()) + "; " + (pa?.Text ?? ra?.Problem) + ")");
            var oa = Preparation.Execute(cona, pa, pa.Digest, pa.Nonce, ctxa, () => UsePrep(s, adma, cona, pa)); Gone(pa.CurrentBackup);
            st = SimulatedConsole.NormalStart(c3);
            Check(oa.State == "method-scheduled" && Writes(c3).Take(2).SequenceEqual(new[] { "mstart on", "method ccs-mod" }) && st.Contains("ccs-mod start on") && ModStartNow(c3) == "on ours" && Record.Load(oa.RecordFile).O("after")?.O("method")?.S("mod_start") == "on",
                "scheduled; at the next start CCS Mod's install turns CCS HOME at power-on on (" + oa.Problem + "; " + string.Join("; ", st) + ")");
            var (pb, rb, conb, ctxb, admb) = PrepPlanWith(c3, s, "mod");
            Check(pb != null && pb.Steps.SequenceEqual(new[] { "mstart", "method" }) && pb.ModStart == "off" && pb.Notes.Any(n => n.StartsWith("The console starts in its own menu")), "turned off the same way: unchecked, CCS Mod's setting off and its package again (" + string.Join(",", pb?.Steps ?? new List<string>()) + ")");
            var ob = Preparation.Execute(conb, pb, pb.Digest, pb.Nonce, ctxb, () => UsePrep(s, admb, conb, pb)); Gone(pb.CurrentBackup); st = SimulatedConsole.NormalStart(c3);
            Check(ob.Ok && st.Contains("ccs-mod start off") && ModStartNow(c3) == "off absent", "and at the next start CCS Mod's install turns it off (" + string.Join("; ", st) + ")");
            var (pc, _, _, _, _) = PrepPlanWith(c3, s, "mod"); Gone(pc?.CurrentBackup);
            Check(pc != null && pc.Steps.Count == 0 && pc.State == "already", "the same choice again: nothing to do (" + string.Join(",", pc?.Steps ?? new List<string>()) + ")");
            var (pn, rn, _, _, _) = PrepPlanWith(c3, s, "nucleus", null, true);
            Check(pn == null && rn.Problem.Contains("CCS Mod only"), "Prepare Console with CCS Nucleus and the start chosen: refused (" + rn?.Problem + ")");
            GoneConsole(c3); GoneBackups(s);

            // ---- a first-time preparation (the original software) with CCS Mod and the start chosen: the setting before CCS Mod's package, all
            // before the kernel (the kernel stays last); a failure of the setting stops before the package and the kernel
            s = SettingsIn(NewDir("modstart-first"), false);
            var c4 = Console_("stock"); Backup(c4, s);
            var (p4, r4, con4, ctx4, adm4) = PrepPlanWith(c4, s, "mod", HomePackage.Folder(MakeHome("mod", "s5"), "mod"), true);
            Check(p4 != null && p4.State == "original" && p4.Steps.SequenceEqual(new[] { "files", "uboot", "slot68", "mstart", "method", "hput", "huse", "kernel" }) && p4.ModStart == "on",
                "the original software, CCS Mod with CCS HOME at power-on: hakchi's steps, then CCS Mod's setting, its package and CCS HOME, the kernel last (" + string.Join(",", p4?.Steps ?? new List<string>()) + "; " + r4?.Problem + ")");
            File.WriteAllText(Path.Combine(c4, "faults.json"), "{\"prep\":{\"step\":\"mstart\"}}");
            var o4 = Preparation.Execute(con4, p4, p4.Digest, p4.Nonce, ctx4, () => UsePrep(s, adm4, con4, p4));
            Check(o4.State == "stopped" && o4.Done.SequenceEqual(new[] { "files", "uboot", "slot68" }) && !Writes(c4).Any(w => w.StartsWith("method") || w == "prep kernel") && File.ReadAllBytes(SimulatedConsole.Dev(c4, "boot2")).SequenceEqual(StockKernel()),
                "the setting's step fails: stopped there; CCS Mod's package and the kernel are not written, so the console still starts its original system; nothing retried (" + o4.Problem + ")");
            GoneConsole(c4); GoneBackups(s);
        }
    }
}
