// Classic Cart Studio console backup and restore tests. Copyright (C) 2026 Rance9. GPL-3.0-or-later.
// The Super NES Classic as a physical console (group "snes"), on simulated consoles of its own layout (board dp-shvc, REGION USA or EUR,
// the storage sizes a North American console reported): which consoles count as it, its stock kernels by region, identity refusals
// (the Super Famicom's JPN region, another layout, a table its running system disagrees with), a backup that states the model, its
// verification and browsing, the restore plan's order from its own table, a backup of one model never restored or composed onto the
// other, and every operation not available for it refused before anything is read or written. The stock cases need a genuine stock
// Super NES Classic kernel (--snes-kernel, read locally); without one they are skipped and said so.
using CcsBackup.Archive;
using CcsBackup.Browse;
using CcsBackup.Compose;
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
        static void SetCfg(string console, string key, object value)
        {
            var cj = Path.Combine(console, "console.json"); var jd = Json.Parse(File.ReadAllText(cj)); var jo = new JObj();
            foreach (var kv in jd) jo.Add(kv.Key, kv.Value); jo.Set(key, value); File.WriteAllText(cj, Json.Write(jo));
        }
        static List<string> FilesIn(Settings s) => Directory.Exists(s.PrimaryFolder) ? Directory.GetFiles(s.PrimaryFolder).ToList() : new List<string>();

        public static void Snes()
        {
            Part("the model and its stock kernels", () =>
            {
                Check(ConsoleModels.For("dp-shvc", "USA") == ConsoleModels.Snes && ConsoleModels.For("dp-shvc", "EUR") == ConsoleModels.Snes && ConsoleModels.For("dp-shvc", "JPN") == null
                      && ConsoleModels.For("dp-nes", "EUR_USA") == ConsoleModels.Nes && ConsoleModels.Snes.StockGames == 21 && ConsoleModels.Nes.StockGames == 30,
                      "the Super NES Classic is board dp-shvc with REGION USA or EUR (21 built-in games); the Super Famicom's JPN is not supported; the NES Classic is unchanged (30)");
                const string usa14 = "228967ab1035a347caa9c880419df487", eur14 = "0f890bc78cbd9ede43b83b015ba4c022", nes107 = "90eec1e2b4f00e53dc2dd53a9e7334c1", sfc14 = "c3378edfc1b96a5268a066d5fbe12d89";
                Check(KnownKernels.IsStockFor(ConsoleModels.Snes, "USA", usa14) && !KnownKernels.IsStockFor(ConsoleModels.Snes, "EUR", usa14) && KnownKernels.IsStockFor(ConsoleModels.Snes, "EUR", eur14)
                      && !KnownKernels.IsStockFor(ConsoleModels.Snes, "JPN", usa14) && !KnownKernels.IsStockFor(ConsoleModels.Snes, "USA", nes107) && !KnownKernels.IsStockFor(ConsoleModels.Nes, "USA", usa14)
                      && KnownKernels.IsStockFor(ConsoleModels.Nes, "EUR_USA", nes107) && !KnownKernels.IsStockFor(ConsoleModels.Snes, "USA", sfc14) && KnownKernels.IsStock(nes107) && !KnownKernels.IsStock(usa14)
                      && KnownKernels.ReleaseFor(ConsoleModels.Snes, "USA", usa14) == "kernel-dp-snesusa-release-v2.0.14-0-gd8b65c6"
                      && KnownKernels.Sha256ByMd5[usa14] == "a358326433d7bb5d1093d313fd7656f8706b493972cea446fd926437704f8911",
                      "a kernel is stock only for its own model and region: the North American and European lists are separate, the Super Famicom's is not used, and the NES Classic's list is unchanged");
                var on = new[] { "identify", "backup", "restore" }; var off = new[] { "preparation", "return", "rescue", "files", "developer", "composition", "addons", "firmware", "community" };
                Check(on.All(o => ConsoleModels.Snes.Refusal(o) == null) && off.All(o => (ConsoleModels.Snes.Refusal(o) ?? "").StartsWith("This operation is not available for the Super NES Classic Edition in this version: ", StringComparison.Ordinal))
                      && on.Concat(off).All(o => ConsoleModels.Nes.Refusal(o) == null) && Capabilities.Allowed("capability", "snes-classic-browse") && !Capabilities.Allowed("game-system", "snes"),
                      "backup, restore and the check-up are available for the Super NES Classic; each other operation is refused with its own reason; the NES Classic's operations are as before; its games stay unplayable (" + ConsoleModels.Snes.Refusal("preparation") + ")");
                var jpn = ConsoleIdentity.Parse("ccsbr=1\nboard=dp-shvc\nregion=JPN\nend=1\n"); var nob = ConsoleIdentity.Parse("ccsbr=1\nend=1\n");
                Check(ConsoleModels.RefusalFor(jpn, "rescue") != null && ConsoleModels.RefusalFor(nob, "rescue") == null, "a dp-shvc console of another region is refused even where its model cannot be read; an unreadable board keeps the NES Classic's handling (Console Rescue)");
            });

            Part("identity refusals, before anything is read", () =>
            {
                var cases = new List<(string what, string console, string code)>
                {
                    ("the Super Famicom Classic Mini's region (JPN)", SnesConsole("modified-noorig", "JPN"), "not an NES Classic Edition or a North American or European Super NES Classic Edition"),
                    ("another storage geometry", SnesConsole("modified-noorig", "USA", "4096 64 2048"), "not the layout Classic Cart Studio knows for the Super NES Classic Edition"),
                };
                var bad = SnesConsole("modified-noorig", "EUR"); SetCfg(bad, "parttable", "mbr:mbr:32 nanda:boot:6144 nandb:rootfs:131000 nandc:data:131072 nandd:private:2048 nande:UDISK:128000");
                cases.Add(("a table its running system disagrees with", bad, "disagree about nandb"));
                foreach (var (what, console, text) in cases)
                {
                    var s = SettingsIn(NewDir("snes-refuse"), false); var o = Backup(console, s);
                    Check(o.State == "refused" && (o.Problem ?? "").Contains(text) && FilesIn(s).Count == 0, "a Super NES Classic console with " + what + ": refused, nothing read or saved (" + o.Problem + ")");
                    GoneConsole(console);
                }
            });

            var c = SnesConsole("modified-noorig"); var sb = SettingsIn(NewDir("snes-backup"), false);
            BackupOutcome b = null;
            Part("a backup states the model, and verifies as it", () =>
            {
                b = Backup(c, sb); var v = b.Ok ? Verifier.VerifyFile(b.PrimaryPath) : null; var t = v?.Manifest.O(Capabilities.TargetKey); var con = v?.Manifest.O("console");
                Check(b.Ok && v.Ok && v.Model == ConsoleModels.Snes && v.Region == "USA" && v.Board == "dp-shvc" && t?.S("physical_model") == "snes-classic-edition" && t?.S("firmware") == "nintendo-snes-classic"
                      && t?.S("layout") == "snes-classic-nand-512m" && con?.S("model") == "Super NES Classic Edition" && Path.GetFileName(b.PrimaryPath).StartsWith("SIMULATED SNES Classic Backup ", StringComparison.Ordinal),
                      "a Super NES Classic backup names its model, console software and storage layout (ccs_target), its board and region, and verifies (" + b.Problem + (v == null ? "" : "; " + string.Join("; ", v.Problems)) + ")");
                var parts = (v?.Manifest.O("layout")?.A("partitions") ?? new List<object>()).OfType<Dictionary<string, object>>().ToList();
                Check(v != null && v.StateKind == "modified" && !v.RecoveryBaseline && v.OriginalSource == null && parts.Select(p => p.S("device") + ":" + p.L("bytes")).SequenceEqual(SnesLayout.Select(x => x.dev + ":" + x.bytes))
                      && v.Manifest.O("state")?.S("text").Contains("original Nintendo kernel was not found") == true,
                      "without its original kernel the backup is a truthful current-state backup, not an original recovery baseline; its table is the console's own (" + v?.StateKind + ")");
                var opened = BackupContents.Open(b.PrimaryPath, sb);
                using (var bc = opened.Contents)
                {
                    var rep = bc == null ? null : StorageReport.Build(bc);
                    Check(bc != null && bc.Model == ConsoleModels.Snes && !bc.NesSaves && rep?.Profile == "snes-classic-edition", "it opens in the Backup Browser as a Super NES Classic backup: its saves are kept and exported as they are, never converted; the Storage view uses its own model (" + opened.Problem + ")");
                }
                // the same file claiming the NES Classic, or another model's target: refused by the verifier
                var j = Classifier.StockConsole("CLV-P-SAAAE") == "SNES Classic" && Classifier.StockConsole("CLV-P-NAAAE") == "NES Classic" && Classifier.StockGames["CLV-P-SADKE"] == "Star Fox 2";
                Check(j && Classifier.StockGames.Keys.Count(k => k.StartsWith("CLV-P-S", StringComparison.Ordinal)) == 21, "the Backup Browser knows the Super NES Classic's 21 built-in games by code (Hakchi2 CE 3.9.3's own desktop entries)");
            });

            Part("restore: its order from its own table, and only onto its own model", () =>
            {
                if (b == null || !b.Ok) { Check(false, "no Super NES Classic backup to restore"); return; }
                var nesOrder = RestoreEngine.OrderFor(Layout.Select(x => new Dictionary<string, object> { ["device"] = x.dev, ["label"] = x.label }).ToList());
                var snesOrder = RestoreEngine.OrderFor(SnesLayout.Select(x => new Dictionary<string, object> { ["device"] = x.dev, ["label"] = x.label }).ToList());
                var noLabels = RestoreEngine.OrderFor(new List<Dictionary<string, object>> { new Dictionary<string, object> { ["device"] = "nandb" } });
                Check(nesOrder.SequenceEqual(RestoreEngine.WriteOrder) && noLabels.SequenceEqual(RestoreEngine.WriteOrder) && snesOrder.SequenceEqual(new[] { "nandb", "nanda", "nandd", "nande", "nandc", "boot2-68", "boot2" }),
                      "the write order comes from the backup's labeled table (system, boot, the other parts, the data storage last, then the startup software): the NES Classic's order is unchanged");
                var con = RecoverySessions.OpenSimulated(c, ImageKind.Restore);
                var (plan, refused) = RestoreEngine.Prepare(con, b.PrimaryPath, Ctx(sb, ImageKind.Restore));
                Check(plan != null && plan.Refusals.Count == 0 && plan.SystemTarget == "nandb" && Writes(c).Count == 0, "the same console's restore is planned (its system part by its label), nothing written yet (" + (refused?.Problem ?? string.Join("; ", plan?.Refusals ?? new List<string>())) + ")");
                if (plan?.ProtectivePath != null) Gone(plan.ProtectivePath);
                // an NES Classic with the same identity (a made-up case): the Super NES Classic's backup is never planned onto it
                var nes = Console_("modified-noorig", HwidA); var sn = SettingsIn(NewDir("snes-onto-nes"), false);
                var (p2, r2) = RestoreEngine.Prepare(RecoverySessions.OpenSimulated(nes, ImageKind.Restore), b.PrimaryPath, Ctx(sn, ImageKind.Restore));
                Check(p2 == null && (r2?.Problem ?? "").Contains("never put on another kind") && Writes(nes).Count == 0, "a Super NES Classic backup is refused for an NES Classic, nothing written (" + r2?.Problem + ")");
                GoneConsole(nes); GoneBackups(sn);
            });

            Part("every operation not available for it is refused before anything is read or written", () =>
            {
                var con = RecoverySessions.OpenSimulated(c, ImageKind.Prepare); var id = con.Identify();
                foreach (var op in new[] { "preparation", "return", "firmware", "addons", "community" })
                {
                    string code = null, text = null;
                    try { Support.CheckForBackup(id, id.Image, op); } catch (ConsoleRefusedException ex) { code = ex.Code; text = ex.Message; }
                    Check(code == "unsupported-operation" && text.Contains("not available for the Super NES Classic Edition") && text.EndsWith("Nothing was read.", StringComparison.Ordinal), op + ": refused at the identity check, before any backup or write (" + text + ")");
                }
                Check(Support.CheckForBackup(id, id.Image, "backup").Count == 5 && Support.CheckForBackup(id, id.Image, "identify").Count == 5, "backup and the check-up pass the same identity check");
                var (pt, why) = RestorePoints.Establish(RecoverySessions.OpenSimulated(c, ImageKind.Files), b?.PrimaryPath ?? "x", () => false);
                Check(pt == null && (why ?? "").Contains("Expert Mode"), "Expert Mode's file changes are refused for the Super NES Classic before a restore point is made (" + why + ")");
                Check(Writes(c).Count == 0, "nothing was written to the Super NES Classic by any of these");
            });

            Part("a stock Super NES Classic (genuine kernel)", () =>
            {
                var st = SnesConsole("stock"); var ss = SettingsIn(NewDir("snes-stock"), false); var o = Backup(st, ss); var v = o.Ok ? Verifier.VerifyFile(o.PrimaryPath) : null;
                Check(o.Ok && v.Ok && v.StateKind == "stock" && v.RecoveryBaseline && v.OriginalSource == "active" && v.Manifest.O("state")?.S("original_release") == "kernel-dp-snesusa-release-v2.0.14-0-gd8b65c6",
                      "a stock North American console: its genuine kernel is recognized for its own region and the backup is its original recovery baseline (" + o.Problem + ")");
                var eur = SnesConsole("stock", "EUR"); var se = SettingsIn(NewDir("snes-stock-eur"), false); var oe = Backup(eur, se); var ve = oe.Ok ? Verifier.VerifyFile(oe.PrimaryPath) : null;
                Check(oe.Ok && ve.Ok && ve.StateKind == "modified" && !ve.RecoveryBaseline, "the same North American kernel on a console that says EUR is not its stock kernel: a current-state backup, never an original baseline");
                GoneConsole(st); GoneConsole(eur); GoneBackups(ss); GoneBackups(se);
            });
            GoneConsole(c); GoneBackups(sb);
        }
    }
}
