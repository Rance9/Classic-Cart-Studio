// Classic Cart Studio console backup and restore engine. Copyright (C) 2026 Rance9. GPL-3.0-or-later.
// Whether planned file writes fit on the console's data storage: read from the backup made in this session (its data storage image's own
// file system figures: free blocks outside the reserve the system keeps), never estimated from a backup's size or the chip's capacity.
// Each new file is staged beside the old one before the rename, so every file written needs its whole size in free blocks at that moment.
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
        /// <summary>null when the writes fit; otherwise the reason. needed/available in bytes (available: free blocks outside the reserve).</summary>
        public static string Check(string backup, IEnumerable<PlannedFileChange> changes, out long needed, out long available)
        {
            needed = available = 0;
            try
            {
                var m = History.Peek(backup);
                var dev = (m?.O("layout")?.A("partitions") ?? new List<object>()).OfType<Dictionary<string, object>>().FirstOrDefault(x => x.S("label") == "data" || x.S("label") == "rootfs_data")?.S("device");
                using (var z = ZipReader.Open(backup, BackupFormat.Allowed))
                {
                    var e = dev == null ? null : z.Find("restore/" + dev + ".img"); if (e == null || e.Method != 0) return "The console's free space could not be read (the backup made just now has no data storage image).";
                    var fs = new Ext4Reader(z.StoredWindow(e)); long bs = fs.BlockSize;
                    available = Math.Max(0, (long)fs.FreeBlocks - (long)fs.ReservedBlocks) * bs;
                    needed = changes.Where(c => c.Op == "put").Sum(c => (c.Bytes + bs - 1) / bs * bs + bs);   // its blocks, and one more for its directory entry and extent data
                }
            }
            catch (Exception ex) when (ex is IOException || ex is Ext4Exception || ex is InvalidDataException) { return "The console's free space could not be read (" + ex.Message + ")."; }
            return needed <= available ? null : "These changes need about " + Units.Gb(needed) + " of free space; the console's data storage has " + Units.Gb(available) + " available (outside the space the system keeps in reserve).";
        }
    }
}
