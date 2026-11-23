// Classic Cart Studio console backup and restore engine. Copyright (C) 2026 Rance9. GPL-3.0-or-later.
// Files and folders mode (ExternalFormat.cs): the selected used data of a USB drive or SD card (or of chosen folders on it).
//   Scan    every selected folder listed through NativeFiles (no link is ever followed; each folder's final path checked), in a fixed order
//           (paths compared ordinally), with each item's size, times, attributes and extended-attribute size. At a volume's root, Windows'
//           own folders (System Volume Information, $RECYCLE.BIN) are left out unless asked for, and listed as left out. Links (junctions and
//           symbolic links) are recorded, never followed or copied. A name Windows can read but could not create again (a trailing dot or
//           space, a reserved device name) is kept exactly and marked; two names that differ only in case are marked as twins. More than
//           ExternalFormat.MaxEntries entries are refused, never cut short. The scan is also the estimate: the actual selected bytes.
//   Write   in that order: small files one after another into packs, each large file into its own member; each file opened read-only
//           (other programs kept from writing to it while it is read), read for exactly its listed size and hashed (SHA-256) on the way;
//           its size, last-write and change times compared when it is opened (with the listing) and after it is read (with the opening):
//           a file that differs is "changed" and one that cannot be opened or read is "unreadable" (its place in the pack holds zeros);
//           either is listed with the reason, never exported or restored as a good file, and makes the backup's scope incomplete.
//           The entries go into inventory chunks of at most ExternalFormat.InventoryChunk, each written after the payload it describes.
// What is kept and what is not (said in the manifest): contents, sizes, the creation, last-write and last-access times (100-nanosecond
// steps, as Windows reports them; FAT keeps 2-second and date-only steps), the read-only, hidden, system and archive attributes, empty
// folders and the folder tree. Not kept: permissions and owners (Windows security), alternate data streams, extended attributes (their size
// is recorded when there are some), short 8.3 names, and links' targets (links are listed, not followed).
using CcsBackup.Engine;
using Microsoft.Win32.SafeHandles;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Security.Cryptography;

namespace CcsBackup.External
{
    public sealed class PlanItem
    {
        public string Rel, Name; public string Kind;   // file | folder | link
        public long Size, Created, Accessed, Modified, Changed; public uint Attributes, EaSize;
        public bool WindowsName = true; public string CaseTwin;
    }

    public sealed class ScanPlan
    {
        public SourceIdentity Identity; public string RootLong, RootFinal; public List<string> Folders = new List<string>();
        public readonly List<PlanItem> Items = new List<PlanItem>();
        public readonly List<(string path, string why)> Omitted = new List<(string, string)>();
        public readonly List<(string path, string why)> Problems = new List<(string, string)>();
        public long Bytes, Files, FolderCount, Links;
    }

    public static class FileCapture
    {
        static readonly string[] SystemFolders = { "System Volume Information", "$RECYCLE.BIN" };

        static string Win(string rel) => rel == "/" ? "\\" : rel.Replace('/', '\\');

        /// <summary>the selected folders, listed; throws StorageRefusedException when the selection itself cannot be used</summary>
        public static ScanPlan Scan(string root, IList<string> folders, bool includeSystemFolders, Func<bool> cancelled = null)
        {
            var full = Path.GetFullPath(root);
            var plan = new ScanPlan { Identity = Devices.ForFolder(full) };
            plan.RootLong = NativeFiles.Long(full.TrimEnd('\\'));   // a path inside it is RootLong + "\name" (the root itself: RootLong + "\")
            using (var h = Open(plan.RootLong + "\\", null, null))
            {
                plan.RootFinal = NativeFiles.FinalPath(h)?.TrimEnd('\\');
                if ((NativeFiles.Of(h).Attributes & 0x400) != 0) throw new StorageRefusedException("The chosen folder is a link to somewhere else. Choose the folder it points to instead.");
            }
            var sel = (folders == null || folders.Count == 0 ? new List<string> { "/" } : folders.Select(Norm).ToList()).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(x => x, StringComparer.Ordinal).ToList();
            sel = sel.Where(f => !sel.Any(o => o != f && (o == "/" || f.StartsWith(o + "/", StringComparison.OrdinalIgnoreCase)))).ToList();
            plan.Folders = sel;
            bool volumeRoot = plan.Identity.Kind == SourceKind.Volume;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var top in sel)
            {
                // the selected folder itself (and the folders above it, so its place is kept)
                if (top != "/")
                {
                    var parts = top.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries); var acc = "";
                    foreach (var p in parts)
                    {
                        acc += "/" + p; if (!seen.Add(acc)) continue;
                        using (var h = Open(plan.RootLong + Win(acc), plan.RootFinal, acc))
                        {
                            var st = NativeFiles.Of(h);
                            if ((st.Attributes & 0x400) != 0) throw new StorageRefusedException("The chosen folder " + acc + " is a link to somewhere else; links are not followed. Choose the folder it points to instead.");
                            if ((st.Attributes & 0x10) == 0) throw new StorageRefusedException("The chosen folder " + acc + " is not a folder.");
                            plan.Items.Add(new PlanItem { Rel = acc, Name = p, Kind = "folder", Created = st.Created, Accessed = st.Accessed, Modified = st.Modified, Changed = st.Changed, Attributes = st.Attributes, WindowsName = Words.WindowsSafe(p) });
                        }
                    }
                }
                var stack = new Stack<string>(); stack.Push(top);
                while (stack.Count > 0)
                {
                    if (cancelled != null && cancelled()) throw new OperationCanceledException();
                    var dir = stack.Pop(); List<DirItem> kids;
                    try { using (var h = Open(plan.RootLong + Win(dir), plan.RootFinal, dir)) kids = NativeFiles.List(h); }
                    catch (StorageRefusedException) { throw; }
                    catch (Exception ex) when (ex is Win32Exception || ex is IOException || ex is UnauthorizedAccessException || ex is LinkException)
                    { plan.Problems.Add((dir, "the folder could not be listed (" + ex.Message.TrimEnd('.') + ")")); continue; }
                    var subs = new List<string>();
                    foreach (var k in kids.OrderBy(x => x.Name, StringComparer.Ordinal))
                    {
                        var rel = (dir == "/" ? "" : dir) + "/" + k.Name;
                        if (volumeRoot && dir == "/" && !includeSystemFolders && SystemFolders.Contains(k.Name, StringComparer.OrdinalIgnoreCase)) { plan.Omitted.Add((rel, "Windows' own system folder (left out unless asked for)")); continue; }
                        if (plan.Items.Count >= ExternalFormat.MaxEntries) throw new StorageRefusedException("The selection holds more than " + Words.N(ExternalFormat.MaxEntries) + " files and folders, more than one backup can list. Choose fewer folders, or back the drive up in parts.");
                        if (!seen.Add(rel)) continue;
                        var it = new PlanItem { Rel = rel, Name = k.Name, Size = k.IsFolder ? 0 : k.Size, Created = k.Created, Accessed = k.Accessed, Modified = k.Modified, Changed = k.Changed, Attributes = k.Attributes, EaSize = k.EaSize, WindowsName = Words.WindowsSafe(k.Name) };
                        if (k.IsLink) { it.Kind = "link"; it.Size = 0; plan.Links++; }
                        else if (k.IsFolder) { it.Kind = "folder"; plan.FolderCount++; subs.Add(rel); }
                        else { it.Kind = "file"; plan.Files++; plan.Bytes += Math.Max(0, k.Size); }
                        plan.Items.Add(it);
                    }
                    for (int i = subs.Count - 1; i >= 0; i--) stack.Push(subs[i]);
                }
            }
            plan.Items.Sort((a, b) => string.CompareOrdinal(a.Rel, b.Rel));
            foreach (var g in plan.Items.GroupBy(x => x.Rel.ToUpperInvariant()).Where(g => g.Count() > 1))
                foreach (var x in g) x.CaseTwin = g.First(y => y != x).Rel;
            return plan;
        }

        static string Norm(string f)
        {
            var p = (f ?? "/").Replace('\\', '/').Trim();
            if (!p.StartsWith("/")) p = "/" + p;
            p = p.TrimEnd('/'); if (p.Length == 0) return "/";
            if (p.Split('/').Skip(1).Any(x => x.Length == 0 || x == "." || x == "..")) throw new StorageRefusedException("A chosen folder's path is not valid: " + f);
            return p;
        }

        sealed class LinkException : Exception { public LinkException(string m) : base(m) { } }

        /// <summary>opens a folder by its long path; refuses a link and a final path other than expected (root + rel)</summary>
        static SafeFileHandle Open(string longPath, string rootFinal, string rel)
        {
            var h = NativeFiles.OpenFolder(longPath);
            try
            {
                if (rootFinal != null)
                {
                    var fp = NativeFiles.FinalPath(h)?.TrimEnd('\\');
                    var want = rootFinal + (rel == "/" ? "" : rel.Replace('/', '\\'));
                    if (fp != null && !string.Equals(fp, want, StringComparison.OrdinalIgnoreCase)) throw new LinkException("it is reached through a link to somewhere else, so it is not read");
                }
                return h;
            }
            catch { h.Dispose(); throw; }
        }

        // ------------------------------------------------------------------ writing
        internal sealed class Counters { public long Done, Total; public long Files, Bytes, Unreadable, Changed, Folders, Links; public readonly List<string> Problems = new List<string>(); }

        /// <summary>writes one source's files, packs and inventory chunks; returns whether every planned file was read whole and unchanged</summary>
        internal static bool Write(StorageWriter w, string src, ScanPlan plan, StorageOptions opt, Counters c)
        {
            var inv = new List<JObj>(); int invNo = 0, packNo = 0, fileNo = 0; long sinceInv = 0;
            var batch = new List<PlanItem>(); long batchBytes = 0; bool clean = true;
            void Inv(bool force)
            {
                if (inv.Count == 0 || !force && inv.Count < ExternalFormat.InventoryChunk && sinceInv < ExternalFormat.PackTarget) return;
                while (inv.Count > 0)
                {
                    var take = inv.Take(ExternalFormat.InventoryChunk).ToList(); inv.RemoveRange(0, take.Count);
                    w.AddJson(ExternalFormat.Inventory(src, ++invNo), new JObj { { "schema", "ccs-storage-inventory/1" }, { "source", src }, { "chunk", (long)invNo }, { "entries", take } }, opt.Cancelled);
                }
                sinceInv = 0; w.MaybeMap();
            }
            void Pack()
            {
                if (batch.Count == 0) return;
                var name = ExternalFormat.Pack(src, ++packNo); var rows = new List<JObj>(); var items = batch.ToList();
                w.Add(name, false, s => { long off = 0; foreach (var it in items) { rows.Add(ReadInto(plan, it, s, name, off, opt, c, ref clean)); off += it.Size; } }, opt.Cancelled);
                inv.AddRange(rows); sinceInv += batchBytes; batch.Clear(); batchBytes = 0; w.MaybeMap(); Inv(false);
            }
            foreach (var it in plan.Items)
            {
                if (opt.Cancelled()) throw new OperationCanceledException();
                if (it.Kind != "file") { inv.Add(Entry(it)); if (it.Kind == "folder") c.Folders++; else c.Links++; continue; }
                if (it.Size >= ExternalFormat.OwnMemberAt)
                {
                    Pack();
                    var name = ExternalFormat.FileMember(src, ++fileNo); JObj row = null;
                    w.Add(name, false, s => row = ReadInto(plan, it, s, name, 0, opt, c, ref clean), opt.Cancelled);
                    inv.Add(row); sinceInv += it.Size; w.MaybeMap(); Inv(false); continue;
                }
                batch.Add(it); batchBytes += it.Size;
                if (batchBytes >= ExternalFormat.PackTarget || batch.Count >= ExternalFormat.InventoryChunk) Pack();
            }
            Pack(); Inv(true);
            foreach (var (p, why) in plan.Problems) { c.Problems.Add(p + ": " + why); clean = false; }
            return clean;
        }

        static JObj Entry(PlanItem it)
        {
            var j = new JObj { { "type", it.Kind }, { "path", it.Rel } };
            if (it.Kind == "file") j.Set("size", it.Size);
            j.Set("times", new JObj { { "created", it.Created }, { "modified", it.Modified }, { "accessed", it.Accessed }, { "changed", it.Changed } });
            j.Set("attributes", (long)it.Attributes);
            if (it.EaSize > 0) j.Set("ea_bytes", (long)it.EaSize);
            if (!it.WindowsName) j.Set("windows_name", false);
            if (it.CaseTwin != null) j.Set("case_twin", it.CaseTwin);
            if (it.Kind == "link") j.Set("note", "a link (junction or symbolic link): listed, not followed or copied");
            return j;
        }

        /// <summary>one file's planned bytes into the member (exactly it.Size: zeros where it could not be read), hashed and checked</summary>
        static JObj ReadInto(ScanPlan plan, PlanItem it, Stream sink, string member, long offset, StorageOptions opt, Counters c, ref bool clean)
        {
            var row = Entry(it); row.Set("member", member); row.Set("offset", offset);
            string problem = null, state = "ok"; long wrote = 0; var buf = new byte[Streams.Buffer];
            using (var sha = FastHash.Sha256())
            {
                void Put(byte[] b, int n) { sha.TransformBlock(b, 0, n, null, 0); sink.Write(b, 0, n); wrote += n; c.Done += n; opt.Say("read", "Copying " + it.Rel + "...", c.Done, c.Total); }
                try
                {
                    using (var h = NativeFiles.OpenFile(plan.RootLong + Win(it.Rel), out bool shared))
                    {
                        var fp = NativeFiles.FinalPath(h)?.TrimEnd('\\');
                        if (plan.RootFinal != null && fp != null && !string.Equals(fp, plan.RootFinal + Win(it.Rel), StringComparison.OrdinalIgnoreCase)) throw new LinkException("it is reached through a link to somewhere else, so it was not read");
                        var s0 = NativeFiles.Of(h);
                        if ((s0.Attributes & 0x400) != 0) throw new LinkException("it became a link after the folder was listed, so it was not read");
                        // the file as it is when it is opened is what is copied: its own size, times and attributes (a folder's listing can
                        // lag behind a file that was just changed, so only a different SIZE, which the pack's plan depends on, counts here)
                        if (s0.Size != it.Size) { state = "changed"; problem = "its size changed between the listing and the copy (" + Words.N(it.Size) + " then, " + Words.N(s0.Size) + " now)"; }
                        row.Set("attributes", (long)s0.Attributes);
                        row.Set("times", new JObj { { "created", s0.Created }, { "modified", s0.Modified }, { "accessed", it.Accessed }, { "changed", s0.Changed } });
                        if (shared) row.Set("in_use", "another program had it open for writing while it was read");
                        using (var f = new FileStream(h, FileAccess.Read, 1 << 16))
                        {
                            bool first = true;
                            while (wrote < it.Size)
                            {
                                if (opt.Cancelled()) throw new OperationCanceledException();
                                int n = f.Read(buf, 0, (int)Math.Min(buf.Length, it.Size - wrote));
                                if (n <= 0) { state = "changed"; problem = "it became shorter while it was read (" + Words.N(wrote) + " of " + Words.N(it.Size) + " bytes)"; break; }
                                Put(buf, n);
                                if (first) { first = false; opt.DuringFile?.Invoke(it.Rel); }
                            }
                            // the same handle asked again after the read (the stream owns the handle: asked before it is closed)
                            var s1 = NativeFiles.Of(h);
                            if (s1.Size != s0.Size || s1.Modified != s0.Modified || s1.Changed != s0.Changed) { state = "changed"; problem = "it changed while it was read"; }
                        }
                    }
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex) when (ex is Win32Exception || ex is IOException || ex is UnauthorizedAccessException || ex is LinkException)
                { state = "unreadable"; problem = "it could not be read (" + ex.Message.TrimEnd('.') + ")" + (wrote > 0 ? " after " + Words.N(wrote) + " bytes" : ""); }
                // the planned size always: what could not be read is zeros, so every later file keeps its recorded place
                Array.Clear(buf, 0, buf.Length);
                while (wrote < it.Size) Put(buf, (int)Math.Min(buf.Length, it.Size - wrote));
                sha.TransformFinalBlock(new byte[0], 0, 0);
                if (state == "unreadable") row.Set("sha256", null); else row.Set("sha256", Hex.Of(sha.Hash));
            }
            row.Set("state", state); if (problem != null) { row.Set("problem", problem); c.Problems.Add(it.Rel + ": " + problem); clean = false; }
            if (state == "unreadable") c.Unreadable++; else if (state == "changed") c.Changed++; else { c.Files++; c.Bytes += it.Size; }
            return row;
        }
    }
}
