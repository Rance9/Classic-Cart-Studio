// Classic Cart Studio console backup and restore engine. Copyright (C) 2026 Rance9. GPL-3.0-or-later.
// The checks every operation that changes files on a console's data storage shares, for each supported model (the NES Classic and the
// North American and European Super NES Classic): Expert Mode and Developer Mode's files, Console Composer and private restore packages,
// HOME designs, the Sega Mini menu pictures clean-up (ram/ccsfiles), CCS Mod and CCS Nucleus, system images (ram/ccsprep) and community
// mods (ram/ccscomm).
//   data storage   the console programs that change files mount the data storage from one fixed device, nandc, as both models' own
//                  storage tables put it (the part labeled "data"). A console whose table puts it anywhere else is refused before anything
//                  is planned: nothing is written to another part.
//   region         a Super NES Classic's built-in games and hakchi's game folders differ by region (hakchi's games/snes-usa and
//                  games/snes-eur; the console's own game codes), so its files move from one console to another only between consoles of
//                  the same region. Files move between consoles only of the same model in any case (the callers check the model first);
//                  the NES Classic's handling is as it was.
using System;
using System.Linq;

namespace CcsBackup.Device
{
    public static class ConsoleData
    {
        /// <summary>the device the file-changing console programs mount as the data storage</summary>
        public const string DataDevice = "nandc";

        /// <summary>null when the console's storage table has exactly one data part and it is the device the console programs mount; else why
        /// not (a sentence; the caller adds what was not done)</summary>
        public static string Refusal(ConsoleIdentity id)
        {
            var data = (id?.Table ?? new System.Collections.Generic.List<PartitionEntry>()).Where(p => p.Label == "data" || p.Label == "rootfs_data").ToList();
            if (data.Count == 1 && data[0].Device == DataDevice) return null;
            return "This console's data storage is not on the part of its storage where Classic Cart Studio's console programs change files (" + DataDevice + "), so they do not change it.";
        }

        /// <summary>whether files of this model belong to its region (its built-in games and hakchi's game folders differ by region)</summary>
        public static bool RegionBound(ConsoleModel m) => m != null && m == ConsoleModels.Snes;

        /// <summary>null when files of one console may move to another of the same model with these regions; else why not</summary>
        public static string RegionRefusal(ConsoleModel m, string fromRegion, string toRegion)
        {
            if (!RegionBound(m) || string.Equals(fromRegion, toRegion, StringComparison.Ordinal) && !string.IsNullOrEmpty(fromRegion)) return null;
            return "The two consoles are " + m.Name + "s of different regions (" + (fromRegion ?? "unknown") + " and " + (toRegion ?? "unknown") + "): their built-in games and hakchi's game folders differ, so files move only between consoles of the same region.";
        }
    }
}
