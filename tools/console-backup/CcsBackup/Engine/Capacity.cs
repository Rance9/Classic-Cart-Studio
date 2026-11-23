// Classic Cart Studio console backup and restore engine. Copyright (C) 2026 Rance9. GPL-3.0-or-later.
// Whether planned file writes fit on the console's data storage: read from the backup made in this session (its data storage image's own
// file system figures: free blocks, the reserve the system keeps, its block size), never estimated from a backup's size or the chip's
// capacity, and worked out by the shared storage accounting (StorageSpec): each new file is staged beside the file it replaces before the
// rename, so the peak holds both; room for saves and a safety margin stay free.
using CcsBackup.Archive;
using CcsBackup.Ext4;
using CcsBackup.Library;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace CcsBackup.Engine
{
    public static class Capacity
    {
        /// <summary>the data storage figures a backup recorded (its data image's file system header), or null with the reason</summary>
        public static Dictionary<string, object> Reading(string backup, out string problem)
        {
            problem = null;
            try
            {
                var m = History.Peek(backup);
                var dev = (m?.O("layout")?.A("partitions") ?? new List<object>()).OfType<Dictionary<string, object>>().FirstOrDefault(x => x.S("label") == "data" || x.S("label") == "rootfs_data")?.S("device");
                using (var z = ZipReader.Open(backup, BackupFormat.Allowed))
                {
                    var e = dev == null ? null : z.Find("restore/" + dev + ".img"); if (e == null || e.Method != 0) { problem = "the backup made just now has no data storage image"; return null; }
                    var fs = new Ext4Reader(z.StoredWindow(e)); long bs = fs.BlockSize;
                    return new Dictionary<string, object> { ["source"] = "measured", ["free_bytes"] = fs.FreeBlocks * bs, ["reserved_bytes"] = fs.ReservedBlocks * bs, ["block_bytes"] = bs };
                }
            }
            catch (Exception ex) when (ex is IOException || ex is Ext4Exception || ex is InvalidDataException) { problem = ex.Message; return null; }
        }

        /// <summary>the shared accounting's input for planned file changes, in the order they are applied (a "put" replacing a file frees it
        /// once the new copy is in place)</summary>
        public static Dictionary<string, object> Input(Dictionary<string, object> storage, IEnumerable<PlannedFileChange> changes, string profile = StorageSpec.NesModel) => new Dictionary<string, object>
        {
            ["profile"] = profile, ["storage"] = storage,
            ["items"] = changes.Where(c => c.Op == "put").Select(c => (object)new Dictionary<string, object>
            {
                ["category"] = "file", ["label"] = c.Path,
                ["files"] = new object[] { new Dictionary<string, object> { ["bytes"] = c.Bytes } },
                ["replaces"] = c.OldState != null && c.OldState != "absent" && c.OldBytes > 0 ? new object[] { c.OldBytes } : new object[0],
            }).ToArray(),
        };

        /// <summary>null when the writes fit; otherwise the reason. needed: the most the writes need at once with room for saves and the safety
        /// margin; available: the space outside the reserve the backup recorded</summary>
        public static string Check(string backup, IEnumerable<PlannedFileChange> changes, out long needed, out long available)
        {
            needed = available = 0;
            var st = Reading(backup, out var problem);
            if (st == null) return "The console's free space could not be read (" + problem + ").";
            return Why(StorageSpec.Account(Input(st, changes)), out needed, out available);
        }

        /// <summary>the plain reason an account does not allow a write (null when it does), with its figures</summary>
        public static string Why(JObj a, out long needed, out long available)
        {
            needed = (long)a["peak_bytes"]; available = a["available_bytes"] is long v ? v : 0;
            switch (a["status"] as string)
            {
                case "fits": case "near": return null;
                case "over": return "Not enough room on the console: these changes need about " + Units.Gb((long)a["steady_bytes"]) + " of free space, with room kept for saves; the console's data storage has " + Units.Gb(available) + " available (outside the space the system keeps in reserve).";
                case "over-at-peak": return "Not enough room on the console while each new file is beside the one it replaces: about " + Units.Gb(needed) + " is needed then (they would fit once they are done); the console's data storage has " + Units.Gb(available) + " available (outside the space the system keeps in reserve).";
                default: return "Whether these changes fit cannot be confirmed: " + string.Join("; ", ((List<object>)a["unknown"]).Cast<string>()) + " is not known.";
            }
        }
    }
}
