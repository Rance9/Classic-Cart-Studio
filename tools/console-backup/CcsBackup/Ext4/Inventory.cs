// Classic Cart Studio console backup and restore engine. Copyright (C) 2026 Rance9. GPL-3.0-or-later.
// The data partition's file list (inventory/data-files.json): every entry the captured file system holds, with its metadata and, for
// regular files, its SHA-256; who it belongs to as far as the path shows (Classic Cart Studio, hakchi, Nintendo, other); and what the
// captured settings say about storage outside the console (USB/SD), which a backup of built-in storage cannot include. The list is a
// description: the restore puts back the captured partition image itself, byte for byte, so nothing here is needed to restore.
// The data partition is mounted at /var/lib on the console, so "/ccs/..." here is /var/lib/ccs on the console.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace CcsBackup.Ext4
{
    public sealed class InventoryResult
    {
        public JObj Document; public JObj Summary; public string Condition; public bool HakchiPresent, CcsPresent; public List<string> ExternalNotes = new List<string>();
        public string Problem;   // the partition could not be read as a file system (the image is still captured exactly)
    }

    public static class Inventory
    {
        /// <summary>every readable regular file is hashed in full, whatever its size, streamed through a fixed buffer (no size limit, no silent omission)</summary>

        public static string OwnerOf(string path)
        {
            if (path == "/ccs" || path.StartsWith("/ccs/")) return "classic-cart-studio";
            if (path == "/hakchi" || path.StartsWith("/hakchi/")) return "hakchi";
            if (path == "/clover" || path.StartsWith("/clover/")) return "nintendo";
            return "other";
        }

        public static InventoryResult Build(Stream partition, string device, Func<bool> cancelled = null)
        {
            var r = new InventoryResult();
            Ext4Reader fs;
            try { fs = new Ext4Reader(partition); }
            catch (Ext4Exception ex) { r.Problem = ex.Message; r.Summary = new JObj { { "readable", false }, { "problem", ex.Message } }; r.Document = new JObj { { "schema", "ccs-console-inventory/1" }, { "partition", device }, { "summary", r.Summary } }; return r; }
            List<Ext4Entry> entries;
            try { entries = fs.List(); }
            catch (Ext4Exception ex) { r.Problem = ex.Message; r.Summary = new JObj { { "readable", false }, { "problem", ex.Message } }; r.Document = new JObj { { "schema", "ccs-console-inventory/1" }, { "partition", device }, { "summary", r.Summary } }; return r; }
            var list = new List<object>(); long bytes = 0; var counts = new Dictionary<string, int> { ["classic-cart-studio"] = 0, ["hakchi"] = 0, ["nintendo"] = 0, ["other"] = 0 };
            int files = 0, dirs = 0, links = 0, other = 0, unreadable = 0;
            var hashOf = new Dictionary<uint, string>();
            using (var sha = SHA256.Create())
            {
                foreach (var e in entries)
                {
                    if (cancelled != null && cancelled()) throw new OperationCanceledException("canceled");
                    var o = new JObj { { "path", e.Path }, { "type", e.Type.ToString() }, { "mode", Convert.ToString(e.Mode, 8).PadLeft(4, '0') }, { "uid", e.Uid }, { "gid", e.Gid }, { "size", e.Size }, { "mtime", e.Mtime }, { "links", e.Links }, { "inode", (long)e.Inode } };
                    var owner = OwnerOf(e.Path); o.Add("owner", owner); if (e.Type == 'f') counts[owner]++;   // files by owner (folders and links are counted apart)
                    switch (e.Type) { case 'f': files++; bytes += e.Size; break; case 'd': dirs++; break; case 'l': links++; break; default: other++; break; }
                    if (e.LinkTarget != null) o.Add("target", e.LinkTarget);
                    if (e.Unreadable) { unreadable++; o.Add("unreadable", e.Note ?? "unreadable"); }
                    else if (e.Type == 'f')
                    {
                        if (!hashOf.TryGetValue(e.Inode, out var h))
                        {
                            try { var hs = new HashingSink(null, null, cancelled); fs.ReadFile(e.Inode, hs); hs.Finish(); h = hs.Sha256; }
                            catch (Ext4Exception ex) { h = null; unreadable++; o.Add("unreadable", ex.Message); }
                            hashOf[e.Inode] = h;
                        }
                        if (h != null) o.Add("sha256", h);
                    }
                    list.Add(o);
                }
            }
            r.Condition = fs.Condition;
            r.HakchiPresent = entries.Any(e => e.Path == "/hakchi");
            r.CcsPresent = entries.Any(e => e.Path == "/ccs");
            if (r.HakchiPresent)
            {
                string cfg = null;
                var ci = fs.Lookup("/hakchi/config");
                if (ci != null && fs.IsRegular(ci.Value)) { try { cfg = Encoding.UTF8.GetString(fs.ReadAll(ci.Value, 1000000)); } catch (Ext4Exception) { } }
                bool usbHost = cfg == null || !cfg.Contains("cfg_usb_host='n'");
                if (usbHost) r.ExternalNotes.Add("This console's modification software (hakchi) can use games and saves on a USB drive connected to the console. Anything kept on such a drive is not in this backup; it stays on that drive, and games kept there still need it.");
                if (cfg != null && cfg.Contains("cfg_usb_rw='y'")) r.ExternalNotes.Add("The captured hakchi settings let the console write to a connected USB drive (saves there are not in this backup).");
            }
            else r.ExternalNotes.Add("No use of USB or SD storage was found in the captured settings.");
            r.Summary = new JObj
            {
                { "readable", true }, { "filesystem", "ext" + ((fs.FeatureIncompat & 0x40) != 0 ? "4" : (fs.FeatureCompat & 0x4) != 0 ? "3" : "2") }, { "condition", r.Condition },
                { "entries", entries.Count }, { "files", files }, { "folders", dirs }, { "links", links }, { "other", other }, { "unreadable_on_pc", unreadable }, { "file_bytes", bytes },
                { "files_by_owner", new JObj { { "classic_cart_studio", counts["classic-cart-studio"] }, { "hakchi", counts["hakchi"] }, { "nintendo", counts["nintendo"] }, { "other", counts["other"] } } },
                { "hard_linked_inodes", entries.Where(e => e.Type == 'f' && e.Links > 1).Select(e => e.Inode).Distinct().Count() },
            };
            r.Document = new JObj
            {
                { "schema", "ccs-console-inventory/1" }, { "partition", device }, { "mounted_on_console_at", "/var/lib" },
                { "note", "A description of the captured data partition, read on the PC. A restore writes the partition image itself; it does not use this list." },
                { "filesystem", new JObj { { "block_size", fs.BlockSize }, { "volume_name", fs.VolumeName }, { "last_mounted", fs.LastMounted }, { "condition", r.Condition }, { "feature_compat", fs.FeatureCompat }, { "feature_incompat", fs.FeatureIncompat }, { "feature_ro_compat", fs.FeatureRoCompat } } },
                { "summary", r.Summary }, { "external_storage", r.ExternalNotes }, { "entries", list }
            };
            return r;
        }
    }
}
