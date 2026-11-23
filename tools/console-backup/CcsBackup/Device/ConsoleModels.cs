// Classic Cart Studio console backup and restore engine. Copyright (C) 2026 Rance9. GPL-3.0-or-later.
// The physical console models this engine works with: the registry's physical-model entries (tools/platform/ccs-platform-registry.json),
// each told apart by the board and REGION its own software reports (an NES Classic by its board; a Super NES Classic by board dp-shvc and
// REGION USA or EUR, never the Super Famicom Classic Mini's JPN), with the facts the engine needs about each: its names, its console
// software and storage layout identifiers, how many games it ships with, and whether an operation is allowed on it. A console model is
// hardware: it says nothing about which game system or core can play its games.
// Operations: identify (read only: identity, startup software, storage table, check-up), backup, restore, preparation, return, rescue,
// files (Expert Mode file changes), developer, composition, addons (CCS Mod and CCS Nucleus), firmware, community. On the NES Classic each
// works as before; on any other model the registry's capability "<model>-<operation>" must be supported, and its reason is the refusal.
// Every Gate constant still applies on top of this: a supported capability never opens a console session or a write by itself.
using CcsBackup.Compose;
using System.Collections.Generic;

namespace CcsBackup.Device
{
    public sealed class ConsoleModel
    {
        public string Id, Name, ShortName, Firmware, Layout, CapabilityPrefix; public int StockGames;
        /// <summary>the layout's nandinfo, as the console's own flash tool prints it</summary>
        public string NandInfo => Capabilities.Entry("layout", Layout)?.Get("nandinfo");
        /// <summary>the partition labels the layout knows</summary>
        public IReadOnlyList<string> Labels => Capabilities.Entry("layout", Layout)?.List("labels") ?? new List<string>();
        /// <summary>the start of a backup file's name ("NES Classic Backup ", "SNES Classic Backup ")</summary>
        public string BackupFilePrefix => ShortName + " Backup ";
        public bool IsNes => Id == ConsoleModels.Nes.Id;
        /// <summary>null when the operation is allowed on this model, else why not (the registry capability's reason)</summary>
        public string Refusal(string operation)
        {
            if (IsNes) return null;   // the NES Classic's operations, exactly as before this model table existed
            var cap = CapabilityPrefix + "-" + operation; var e = Capabilities.Entry("capability", cap);
            if (e != null && e.Supported) return null;
            return "This operation is not available for the " + Name + " in this version" + (e?.Get("reason") is string r ? ": " + r.TrimEnd('.') + "." : ".");
        }
        public JObj TargetJson() => new JObj { { "schema", Capabilities.TargetSchema }, { "physical_model", Id }, { "firmware", Firmware }, { "layout", Layout } };
    }

    public static class ConsoleModels
    {
        public static readonly ConsoleModel Nes = new ConsoleModel { Id = "nes-classic-edition", Name = "NES Classic Edition", ShortName = "NES Classic", Firmware = "nintendo-nes-classic", Layout = "nes-classic-nand-512m", StockGames = 30, CapabilityPrefix = "nes-classic" };
        public static readonly ConsoleModel Snes = new ConsoleModel { Id = "snes-classic-edition", Name = "Super NES Classic Edition", ShortName = "SNES Classic", Firmware = "nintendo-snes-classic", Layout = "snes-classic-nand-512m", StockGames = 21, CapabilityPrefix = "snes-classic" };
        /// <summary>the supported model this board and region answer with (Capabilities.ModelFor), or null</summary>
        public static ConsoleModel For(string board, string region) => ById(Capabilities.ModelFor(board, region));
        public static ConsoleModel ById(string id) => id == Nes.Id ? Nes : id == Snes.Id ? Snes : null;
        /// <summary>null when the operation may go on with the console this identity describes, else why not. An identity whose board and
        /// region name no supported model is refused only when its board is one of another family's (dp-shvc: the Super NES Classic's,
        /// whose Super Famicom JPN region is not supported); an unreadable board keeps the NES Classic's handling (Console Rescue reads a
        /// console whose system may not start).</summary>
        public static string RefusalFor(ConsoleIdentity id, string operation)
        {
            var m = id?.Model;
            if (m != null) return m.Refusal(operation);
            if (id?.Board == "dp-shvc") return "This console is not a North American or European Super NES Classic Edition (region " + (id.Region ?? "unknown") + "), so Classic Cart Studio does not support it.";
            return null;
        }
        /// <summary>the model a backup's manifest names: its ccs_target's physical_model when it has one, else (a backup made before the
        /// field existed) the model its board answers with</summary>
        public static ConsoleModel OfManifest(Dictionary<string, object> m)
        {
            var t = m?.O(Capabilities.TargetKey); var con = m?.O("console");
            if (t != null) { var byTarget = ById(t.S("physical_model")); return byTarget != null && byTarget == For(con?.S("board"), con?.S("region")) ? byTarget : null; }
            var legacy = For(con?.S("board"), con?.S("region")) ?? ById(Capabilities.ModelForBoard(con?.S("board")));
            return legacy != null && legacy.IsNes ? legacy : null;   // a backup without a target is an NES Classic backup or nothing
        }
    }
}
