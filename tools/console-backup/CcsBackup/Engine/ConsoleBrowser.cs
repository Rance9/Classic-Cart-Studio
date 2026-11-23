// Classic Cart Studio console backup and restore engine. Copyright (C) 2026 Rance9. GPL-3.0-or-later.
// Console Browser: the console's check-up and its files, read live from a backup-image session that runs in the console's memory
// (nothing installed on the console; the backup image has no code that writes its storage). The data storage is read through ccsbr's
// "range" verb, each piece checked end to end (the console's own count and MD5), and parsed on the PC by the same read-only ext4 reader
// backups use: no mount, no journal replay, no repair.
//   check-up   startup software recognized or not (a kernel outside the known list is "not recognized", never "fake"); what is installed
//              (Nintendo's, hakchi's, Classic Cart Studio's, other programs' files); the data storage's file system figures as the file
//              system itself records them: total, used, free, reserved for the system and available (free minus reserved); its condition.
//              A figure that cannot be read is reported as unknown, never estimated.
using CcsBackup.Device;
using CcsBackup.Ext4;
using CcsBackup.Library;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace CcsBackup.Engine
{
    /// <summary>a read-only, seekable view of one console storage part, read in checked pieces of up to 1 MiB (a few kept in memory)</summary>
    public sealed class RemotePartStream : Stream
    {
        const int Chunk = 1 << 20; const int Keep = 16;
        readonly RecoveryConsole con; readonly string target; readonly long length; readonly Func<bool> cancelled;
        readonly Dictionary<long, byte[]> cache = new Dictionary<long, byte[]>(); readonly LinkedList<long> order = new LinkedList<long>();
        long pos; public long RangeReads { get; private set; }
        public RemotePartStream(RecoveryConsole con, string target, long length, Func<bool> cancelled = null) { this.con = con; this.target = target; this.length = length; this.cancelled = cancelled; }
        byte[] Get(long idx)
        {
            if (cache.TryGetValue(idx, out var b)) { order.Remove(idx); order.AddFirst(idx); return b; }
            long off = idx * Chunk; int n = (int)Math.Min(Chunk, length - off); n = (n + 4095) / 4096 * 4096;
            var ms = new MemoryStream(); var r = con.ReadRange(target, off, n, ms, cancelled); RangeReads++;
            if (!r.Ok) throw new IOException(r.Problem);
            b = ms.ToArray(); cache[idx] = b; order.AddFirst(idx);
            if (order.Count > Keep) { cache.Remove(order.Last.Value); order.RemoveLast(); }
            return b;
        }
        public override int Read(byte[] buffer, int offset, int count)
        {
            if (pos >= length) return 0; count = (int)Math.Min(count, length - pos); int done = 0;
            while (done < count)
            {
                var c = Get((pos + done) / Chunk); int at = (int)((pos + done) % Chunk); int k = Math.Min(count - done, c.Length - at);
                if (k <= 0) break; Buffer.BlockCopy(c, at, buffer, offset + done, k); done += k;
            }
            pos += done; return done;
        }
        public override bool CanRead => true; public override bool CanSeek => true; public override bool CanWrite => false;
        public override long Length => length; public override long Position { get => pos; set => pos = value; }
        public override long Seek(long o, SeekOrigin s) => pos = s == SeekOrigin.Begin ? o : s == SeekOrigin.Current ? pos + o : length + o;
        public override void Flush() { } public override void SetLength(long v) => throw new NotSupportedException(); public override void Write(byte[] b, int o, int c) => throw new NotSupportedException();
    }

    public sealed class ConsoleCheckup
    {
        public string ConsoleTag, KernelText, ActiveMd5, Condition, DataDevice, Problem; public bool KernelRecognized, HakchiPresent, CcsPresent;
        /// <summary>the console model its board and region answer with (ConsoleModels), its name and the games it ships with</summary>
        public string PhysicalModel, ModelName; public int StockGames;
        public long? PartBytes, TotalBytes, UsedBytes, FreeBytes, ReservedBytes, AvailableBytes; public JObj Installed;
        public JObj ToJson() => new JObj
        {
            { "console_tag", ConsoleTag }, { "physical_model", PhysicalModel }, { "model", ModelName }, { "stock_games", (long)StockGames }, { "kernel_recognized", KernelRecognized }, { "kernel", KernelText }, { "active_md5", ActiveMd5 }, { "hakchi", HakchiPresent }, { "classic_cart_studio", CcsPresent },
            { "data_device", DataDevice }, { "condition", Condition }, { "installed", Installed }, { "problem", Problem },
            { "storage", new JObj { { "part_bytes", PartBytes }, { "total_bytes", TotalBytes }, { "used_bytes", UsedBytes }, { "free_bytes", FreeBytes }, { "reserved_bytes", ReservedBytes }, { "available_bytes", AvailableBytes } } },
        };
    }

    public static class ConsoleBrowser
    {
        /// <summary>the check-up: reads only</summary>
        public static ConsoleCheckup Checkup(RecoveryConsole con, string expectedImage, Func<bool> cancelled = null)
        {
            var id = con.Identify(); var c = new ConsoleCheckup { ConsoleTag = id.ShortTag, ActiveMd5 = id.ActiveMd5 };
            c.PhysicalModel = id.Model?.Id; c.ModelName = id.Model?.Name; c.StockGames = id.Model?.StockGames ?? 0;
            c.KernelRecognized = KnownKernels.IsStockFor(id.Model, id.Region, id.ActiveMd5);
            c.KernelText = c.KernelRecognized ? "Nintendo's original startup software (" + KnownKernels.ReleaseFor(id.Model, id.Region, id.ActiveMd5) + ")"
                : id.ActiveHakchiEntries > 0 ? "Modified startup software (hakchi), not on this version's list of recognized startup software" : "Startup software not on this version's list of recognized startup software";
            List<PartitionEntry> parts;
            try { parts = Support.CheckForBackup(id, expectedImage, "identify"); } catch (ConsoleRefusedException ex) { c.Problem = ex.Message; return c; }
            var data = parts.FirstOrDefault(p => p.Label == "data" || p.Label == "rootfs_data");
            if (data == null) { c.Problem = "The console's data storage was not found in its storage table."; return c; }
            c.DataDevice = data.Device; c.PartBytes = data.Bytes;
            try
            {
                var st = new RemotePartStream(con, data.Device, data.Bytes, cancelled); var fs = new Ext4Reader(st);
                long bs = fs.BlockSize; c.Condition = fs.Condition;
                c.TotalBytes = fs.BlocksCount * bs; c.FreeBytes = fs.FreeBlocks * bs; c.ReservedBytes = fs.ReservedBlocks * bs;
                c.UsedBytes = c.TotalBytes - c.FreeBytes; c.AvailableBytes = Math.Max(0, c.FreeBytes.Value - c.ReservedBytes.Value);
                var inv = Inventory.Build(st, data.Device, cancelled);
                if (inv.Problem == null) { c.Installed = inv.Summary; c.HakchiPresent = inv.HakchiPresent; c.CcsPresent = inv.CcsPresent; }
                else c.Problem = "The data storage's files could not all be listed: " + inv.Problem;
            }
            catch (Exception ex) when (ex is IOException || ex is Ext4Exception) { c.Problem = "The data storage could not be read: " + ex.Message; }
            return c;
        }
    }
}
