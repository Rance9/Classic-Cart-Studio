// Classic Cart Studio console backup and restore engine. Copyright (C) 2026 Rance9. GPL-3.0-or-later.
// The battery-save sizes conversion may use, one entry per exact game (SAVE-FORMATS.md, "Battery sizes"). A size is the number of bytes
// the pinned game player's core (FCEUmm) keeps as the game's battery memory for that exact game file: it depends on the file's header,
// mapper and the core's own rules, so there is no general size and none is guessed. Every entry carries its basis and sources; an entry
// is "needs-real-console-confirmation" until the owner's own save has been converted both ways and checked by play (hardware run W02),
// and only a "confirmed" entry gives a conversion that may be imported or written to a console.
using System;
using System.Collections.Generic;
using System.Linq;

namespace CcsBackup.Saves
{
    public sealed class BatteryEntry
    {
        public const string NeedsConfirmation = "needs-real-console-confirmation", ConfirmedStatus = "confirmed", SyntheticTest = "synthetic-test";
        public string Id { get; private set; }
        public string Title { get; private set; }
        public string ConsoleCode { get; private set; }       // the stock game's code (its folder under /var/lib/clover/profiles/0)
        public string ContentSha256 { get; private set; }     // the exact game file (content after the iNES header); null: not recorded yet
        public string Core { get; private set; } = "fceumm";
        public long RawBytes { get; private set; }            // the core's battery memory for that game file (cart.sav)
        public long? StockPayloadBytes { get; private set; }  // the console's payload length (battery memory + zero padding); null: unknown
        public string DigestCase { get; private set; }        // lower | upper; null: unknown
        public string StockFileName { get; private set; }     // save.sram or cartridge.sram, when confirmed
        public string Status { get; private set; }
        public string Basis { get; private set; }
        public IReadOnlyList<string> Sources { get; private set; } = new string[0];
        public bool Confirmed => Status == ConfirmedStatus && ContentSha256 != null;

        internal BatteryEntry(string id, string title, string consoleCode, string contentSha256, long rawBytes, long? stockPayloadBytes, string digestCase, string stockFileName, string status, string basis, params string[] sources)
        {
            Id = id; Title = title; ConsoleCode = consoleCode; ContentSha256 = contentSha256; RawBytes = rawBytes; StockPayloadBytes = stockPayloadBytes;
            DigestCase = digestCase; StockFileName = stockFileName; Status = status; Basis = basis; Sources = sources ?? new string[0];
        }

        /// <summary>tests only: an entry for synthetic vectors (never in BatteryTable.All; its conversions are never for a console)</summary>
        internal static BatteryEntry Synthetic(string consoleCode, string contentSha256, long rawBytes, long? stockPayloadBytes, string digestCase, string stockFileName = "save.sram")
            => new BatteryEntry("synthetic-" + (consoleCode ?? "game"), "Synthetic test game", consoleCode, contentSha256, rawBytes, stockPayloadBytes, digestCase, stockFileName, SyntheticTest, "synthetic test vector");

        public JObj ToJson() => new JObj
        {
            { "id", Id }, { "title", Title }, { "console_code", ConsoleCode }, { "content_sha256", ContentSha256 }, { "core", Core }, { "battery_bytes", RawBytes },
            { "stock_payload_bytes", StockPayloadBytes.HasValue ? (object)StockPayloadBytes.Value : null }, { "check_value_case", DigestCase }, { "stock_file_name", StockFileName },
            { "status", Status }, { "basis", Basis }, { "sources", Sources.ToList() },
        };
    }

    public static class BatteryTable
    {
        const string Fceumm = "FCEUmm ac0d00a7 (tools/player/third_party/fceumm): drivers/libretro/libretro.c retro_get_memory_size(RETRO_MEMORY_SAVE_RAM) returns the cartridge's SaveGameLen[0]; boards/mmc1.c Mapper1_Init/DetectMMC1WRAMSize give 8 KiB of battery memory for an iNES 1.0 mapper-1 file with the battery flag (an NES 2.0 header's PRG-NVRAM field decides instead)";
        const string Hakchi = "Hakchi2 CE 3.9.3 data/desktop_entries/nes-usa/CLV-P-NAANE.desktop: Path=/var/lib/clover/profiles/0//CLV-P-NAANE, SaveCount=3 (https://github.com/TeamShinkansen/Hakchi2-CE/blob/v3.9.3/data/desktop_entries/nes-usa/CLV-P-NAANE.desktop)";
        const string Player = "tools/player/core/gp_save.c gp_sram_load: Classic Cart Studio's player loads cart.sav only when its size equals the core's battery size";
        const string Cart = "community documentation of the original cartridge (believed, not checked against the console's file): MMC1 board with 8 KiB of battery-backed memory, e.g. the NESdev Wiki's MMC1 and SxROM pages (https://www.nesdev.org/wiki/MMC1, https://www.nesdev.org/wiki/SxROM)";

        static readonly List<BatteryEntry> all = new List<BatteryEntry>
        {
            // The owner's planned W02 sample. The folder is verified; the size is believed from the core's code for the cartridge type the
            // game is documented to use; the console's own game file (and so its exact identity and header) has not been examined.
            new BatteryEntry("clv-p-naane", "The Legend of Zelda (NES Classic, North America)", "CLV-P-NAANE", null, 8192, null, null, null, BatteryEntry.NeedsConfirmation,
                "Believed 8,192 bytes: the game is documented as an MMC1 (mapper 1) cartridge with battery-backed memory, and the pinned core keeps 8 KiB for such a file. Not confirmed: the console's own game file (its exact identity and header), the stock payload length, the check value's letter case and play in both directions are recorded in hardware run W02.",
                Hakchi, Cart, Fceumm, Player),
        };

        /// <summary>every entry (copies of the list; the entries themselves cannot be changed)</summary>
        public static IReadOnlyList<BatteryEntry> All => all.ToList();

        /// <summary>the entry for an exact game: a recorded game file first; else the entry for its console code, which is only ever used for a
        /// comparison copy until its exact game file is recorded</summary>
        public static BatteryEntry For(GameIdentity game)
        {
            if (game == null) return null;
            var exact = all.FirstOrDefault(e => e.ContentSha256 != null && e.ContentSha256 == game.ContentSha256);
            if (exact != null) return exact;
            return game.ConsoleCode == null ? null : all.FirstOrDefault(e => e.ContentSha256 == null && e.ConsoleCode == game.ConsoleCode);
        }
    }
}
