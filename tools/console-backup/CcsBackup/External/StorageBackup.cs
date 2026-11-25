// Classic Cart Studio console backup and restore engine. Copyright (C) 2026 Rance9. GPL-3.0-or-later.
// Back Up USB or SD Storage: one storage backup file (ExternalFormat.cs) of one or more USB drives, SD cards, folders on them or drive image
// files, without any console and without a console backup. The order of what is written makes an interruption safe (the source is only
// read; a Windows program that is closed, crashes or is ended at any point leaves a .partial file or a record that is not complete, and no
// such file is ever offered as a backup):
//   1. the selection checked: each source exists; the backup location is not inside a selected folder, and a whole drive is never backed up
//      onto itself; the files and folders sources listed (the actual selected bytes); enough free space for the file, its additional copy
//      and its recovery data. A connected drive copied whole (one per backup) is described as Windows reports it, needs to be a USB drive
//      or SD card, and must hold neither the backup location nor the additional-copy location; then, before anything is written, Windows
//      is asked once for administrator permission and Workbench's administrator step checks the drive again and locks its volumes
//      (RawDiskClient.OpenRead). A refused permission prompt or a refused drive ends the backup with nothing written.
//   2. the operation record (state "reading") and a new, uniquely named "<name>.zip.partial"
//   3. README, then each source in turn (FileCapture / ImageCapture), maps as the records fill, then every map's copy and the manifest;
//      the file closed and flushed (record: "checking")
//   4. the closed file verified from scratch (StorageVerifier), every member compared with what was computed while it was written
//      (record: "checked", with the file's SHA-256)
//   5. renamed to "<name>.zip", never over a file (record: "published")
//   6. the additional copy (in the additional-copy location's "USB and SD Backups" folder): written as its own .partial, read back in full
//      and compared, renamed (record: "copying", then "complete", or "complete-one-copy" with the reason)
//   7. recovery data (PAR2) when asked for, made for the checked file and checked (StorageRepair.Protect)
// A backup whose scope is incomplete (a file that could not be read or changed while it was read, a folder that could not be listed, a
// source that changed) is still a verified FILE, but it says so everywhere: its manifest, the record and the result. It is never called
// complete storage protection (StorageOutcome.Protected).
//   BackUpVolumesNow   the entry point another task uses before it changes USB or SD storage ("back up these volumes now, verified"):
//                      the same run, and Protected only when EVERY requested volume was captured completely and the file verified.
using CcsBackup.Engine;
using CcsBackup.Library;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace CcsBackup.External
{
    public sealed class StorageRefusedException : Exception { public StorageRefusedException(string m) : base(m) { } }

    /// <summary>One source to back up.</summary>
    public sealed class StorageRequest
    {
        /// <summary>files mode: a drive's root ("E:\") or a folder; image mode: a drive image file</summary>
        public string Path;
        /// <summary>"files" (Files and folders, the default) or "image" (Whole drive image)</summary>
        public string Mode = "files";
        /// <summary>files mode: the folders to include, relative to Path ("/saves", "/hakchi/games"); null or empty: everything</summary>
        public List<string> Folders;
        /// <summary>files mode at a drive's root: include Windows' own folders (System Volume Information, $RECYCLE.BIN)</summary>
        public bool IncludeSystemFolders;
        /// <summary>image mode of a connected drive by its number (-1: Path is an image file)</summary>
        public int DiskNumber = -1;
        /// <summary>image mode of a connected drive: its identity when it was chosen (the drive must still be exactly that one), or null</summary>
        public DiskIdentity Disk;
    }

    public sealed class StorageOptions
    {
        public Settings Settings; public string Destination;
        /// <summary>null: make the additional copy when the settings name an additional-copy location</summary>
        public bool? SecondCopy;
        /// <summary>recovery data (PAR2) as a percentage of the file (0: none)</summary>
        public int ParityPercent;
        public string Purpose, Entry = "workbench", ToolName = "CCS Console Workbench", ToolVersion = EngineInfo.Version;
        public Func<bool> Cancelled = () => false;
        public Action<EngineProgress> Progress = _ => { };
        /// <summary>tests only: called after the first block of each file (or after each region) is read, to change the source meanwhile</summary>
        internal Action<string> DuringFile;
        /// <summary>tests only: called at each durable boundary (a crash there leaves what that step left)</summary>
        internal Action<string> Boundary;
        internal void Say(string phase, string text, long done = 0, long total = 0) => Progress?.Invoke(new EngineProgress { Phase = phase, Text = text, Done = done, Total = total });
        internal void At(string b) => Boundary?.Invoke(b);
    }

    public sealed class StorageSourceResult
    {
        public string Id, Mode, Label; public SourceIdentity Identity; public bool Complete;
        public long Files, Folders, Links, Bytes, Unreadable, Changed;
        public readonly List<(string path, string why)> Omitted = new List<(string, string)>();
        public readonly List<string> Problems = new List<string>();
        public DateTime StartedUtc, FinishedUtc; public JObj Summary = new JObj();
    }

    public sealed class StorageOutcome
    {
        /// <summary>complete | complete-one-copy | refused | canceled | failed</summary>
        public string State, Problem, Id, PrimaryPath, ArchivePath, PartialPath, RecordFile, Sha256, CopyProblem, ParityText, ConsistencyText;
        public long Bytes; public bool ScopeComplete, ParityOk, SameDisk, ElevationDeclined; public string SameDrive;
        public readonly List<StorageSourceResult> Sources = new List<StorageSourceResult>();
        public StorageVerification Verification;
        public bool Ok => State == "complete" || State == "complete-one-copy";
        /// <summary>a verified backup file of every requested source, each captured completely: the only state another task may treat as
        /// its protective backup of that storage</summary>
        public bool Protected => Ok && ScopeComplete && Sources.All(s => s.Complete);
    }

    public sealed class StorageEstimate
    {
        public string Problem; public long DataBytes, Files, Folders, ImageBytes, BackupBytes, CopyBytes, ParityBytes, WorkspaceBytes, TotalBytes, FreeBytes = -1;
        public readonly List<(StorageRequest req, SourceIdentity id, ScanPlan plan)> Sources = new List<(StorageRequest, SourceIdentity, ScanPlan)>();
        public bool Fits => FreeBytes < 0 || TotalBytes <= FreeBytes;
        /// <summary>why a backup of used files is smaller than the drive and a whole drive image is not (from the actual numbers)</summary>
        public string SizeNote;
    }

    public static class StorageBackup
    {
        /// <summary>what Windows reports about this PC's drives (tests: stand-in drives)</summary>
        public static IDiskQueries DiskQueries { get; internal set; } = WindowsDiskQueries.Instance;
        /// <summary>which drives the window lets through to the administrator step (the step decides again with its own policy); the product's
        /// is UsbAndSd, and only a test program on a disposable test machine sets another</summary>
        internal static RawDiskPolicy WindowPolicy = RawDiskPolicy.UsbAndSd;
        public static string Folder(Settings s) => Path.Combine(s.PrimaryFolder, ExternalFormat.FolderName);
        public static string SecondFolder(Settings s) => string.IsNullOrEmpty(s.ArchiveFolder) ? null : Path.Combine(s.ArchiveFolder, ExternalFormat.FolderName);

        static string Root(string p) { try { return Path.GetPathRoot(Path.GetFullPath(p)); } catch (Exception) { return null; } }
        static bool Inside(string path, string folder)
        {
            var a = Path.GetFullPath(path).TrimEnd('\\') + "\\"; var b = Path.GetFullPath(folder).TrimEnd('\\') + "\\";
            return a.StartsWith(b, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>the selection checked and the files and folders sources listed: the actual selected bytes, the size of the backup, its
        /// additional copy, its recovery data and room to repair it later; throws StorageRefusedException when the selection cannot be used</summary>
        public static StorageEstimate Estimate(IList<StorageRequest> reqs, string destination, bool secondCopy, int parityPercent, Func<bool> cancelled = null)
        {
            var e = new StorageEstimate();
            if (reqs == null || reqs.Count == 0) throw new StorageRefusedException("Choose at least one USB drive, SD card, folder or drive image to back up.");
            if (reqs.Count > ExternalFormat.MaxSources) throw new StorageRefusedException("One backup can hold at most " + ExternalFormat.MaxSources + " sources; make two backups.");
            foreach (var r in reqs)
            {
                if (r.Mode != "files" && r.Mode != "image") throw new StorageRefusedException("Choose Files and Folders or Whole Drive Image for each source.");
                if (r.Mode == "image" && r.DiskNumber >= 0)
                {
                    if (!Gate.StorageRawReads) throw new StorageRefusedException(Gate.StorageRawText + " Back up its files and folders instead, or choose a drive image file made with another program.");
                    if (reqs.Count(x => x.Mode == "image" && x.DiskNumber >= 0) > 1) throw new StorageRefusedException("One backup can copy one connected drive whole. Make a backup for each drive.");
                    var entry = DiskQueries.Disks().FirstOrDefault(d => d.Number == r.DiskNumber); var did = entry == null ? null : DiskQueries.Describe(entry);
                    if (did == null) throw new StorageRefusedException("That drive is no longer connected. Connect it and choose it again.");
                    if (did.Bytes <= 0) throw new StorageRefusedException("That drive reports no size: there may be no card in the reader.");
                    if (r.Disk != null && r.Disk.Differs(did) != null) throw new StorageRefusedException("The drive connected now is not the one you chose (" + r.Disk.Differs(did) + " is different). Choose it again.");
                    if (!WindowPolicy.Allows(did.Bus)) throw new StorageRefusedException("That drive is not a USB drive or SD card (Windows reports it as connected by " + did.BusText + "). Only USB drives and SD cards are copied whole.");
                    if (did.Number == DiskQueries.SystemDisk()) throw new StorageRefusedException("That is the drive Windows runs from. It is never copied whole.");
                    if (destination != null)
                    {
                        var dd = DiskQueries.DisksOf(destination);
                        if (dd == null) throw new StorageRefusedException("Which drive holds the backup location cannot be determined, so it cannot be shown to be a different drive from the one being copied.");
                        if (dd.Contains(did.Number)) throw new StorageRefusedException("The backup location is on the drive being copied. A whole drive is never backed up onto itself: choose a backup location on another drive.");
                    }
                    r.Disk = did;
                    var sid = did.ToSource(); e.Sources.Add((r, sid, null)); e.ImageBytes += did.Bytes;
                    continue;
                }
                if (r.Mode == "image")
                {
                    if (string.IsNullOrEmpty(r.Path) || !File.Exists(r.Path)) throw new StorageRefusedException("The drive image file " + r.Path + " was not found.");
                    var id = Devices.ForImage(r.Path); e.Sources.Add((r, id, null)); e.ImageBytes += id.TotalBytes;
                    if (destination != null && string.Equals(Path.GetDirectoryName(Path.GetFullPath(r.Path)).TrimEnd('\\'), Path.GetFullPath(destination).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase) && Path.GetFileName(r.Path).StartsWith("USB and SD Backup", StringComparison.OrdinalIgnoreCase))
                        throw new StorageRefusedException("That file is a storage backup, not a drive image.");
                    continue;
                }
                if (string.IsNullOrEmpty(r.Path) || !Directory.Exists(r.Path)) throw new StorageRefusedException("The drive or folder " + r.Path + " was not found. Connect the drive and try again.");
                var plan = FileCapture.Scan(r.Path, r.Folders, r.IncludeSystemFolders, cancelled);
                if (destination != null)
                {
                    foreach (var f in plan.Folders)
                    {
                        var abs = Path.Combine(Path.GetFullPath(r.Path), f.TrimStart('/').Replace('/', '\\'));
                        if (Inside(destination, abs)) throw new StorageRefusedException("The backup location is inside a folder being backed up. Choose a backup location outside it, preferably on another drive.");
                    }
                    if (plan.Identity.Kind == SourceKind.Volume && plan.Folders.Contains("/") && string.Equals(Root(destination), Root(r.Path), StringComparison.OrdinalIgnoreCase))
                        throw new StorageRefusedException("The backup location is on the drive being backed up. A whole drive is never backed up onto itself: choose a backup location on another drive.");
                }
                e.Sources.Add((r, plan.Identity, plan)); e.DataBytes += plan.Bytes; e.Files += plan.Files; e.Folders += plan.FolderCount;
            }
            if (e.Sources.Select(x => x.id.Key + "|" + x.req.Mode).Distinct().Count() != e.Sources.Count) throw new StorageRefusedException("The same source is chosen twice.");
            long entries = e.Sources.Sum(x => x.plan?.Items.Count ?? 0);
            long payload = e.DataBytes + e.ImageBytes;
            long pieces = payload / ExternalFormat.ChunkSize + entries / 2000 + 16;
            e.BackupBytes = payload + entries * 160 + pieces * 150 + 2_000_000;   // payload once, the lists, two copies of the maps, the ZIP structure
            e.CopyBytes = secondCopy ? e.BackupBytes : 0;
            e.ParityBytes = parityPercent > 0 ? e.BackupBytes * parityPercent / 100 + 1_000_000 : 0;
            e.WorkspaceBytes = e.BackupBytes;   // a repair writes a new file of the same size beside it (only when one is needed)
            e.TotalBytes = e.BackupBytes + e.ParityBytes + CaptureEngine.Reserve;
            if (destination != null) { try { Directory.CreateDirectory(destination); } catch (Exception) { } e.FreeBytes = Durable.FreeBytes(destination); }
            var used = e.Sources.Where(x => x.plan != null).ToList();
            e.SizeNote = (used.Count > 0 ? "Files and folders copies only the files you chose (" + Units.Gb(e.DataBytes) + " now" + (used.Any(x => x.id.Kind == SourceKind.Volume && x.id.TotalBytes > 0) ? ", on " + string.Join(" and ", used.Where(x => x.id.Kind == SourceKind.Volume && x.id.TotalBytes > 0).Select(x => "a " + Units.Gb(x.id.TotalBytes) + " drive")) : "") + "). " : "")
                + (e.ImageBytes > 0 ? "A whole drive image is always as large as the whole drive (" + Units.Gb(e.ImageBytes) + "), used or not, because it keeps every byte, free space included. " : "")
                + "An additional copy needs the same space again, and recovery data the percentage you choose. A repair later writes a new file of the same size, so keep that much free where you would repair it.";
            return e;
        }

        /// <summary>"back up these volumes now, verified": one new storage backup of exactly these sources in the backup location (additional
        /// copy as the settings say, no recovery data unless asked); check Protected before changing any of that storage</summary>
        public static StorageOutcome BackUpVolumesNow(IList<StorageRequest> volumes, Settings settings, string purpose, Func<bool> cancelled = null, Action<EngineProgress> progress = null)
            => Run(volumes, new StorageOptions { Settings = settings, Purpose = purpose, Cancelled = cancelled ?? (() => false), Progress = progress ?? (_ => { }) });

        public static StorageOutcome Run(IList<StorageRequest> reqs, StorageOptions opt)
        {
            var o = new StorageOutcome(); var s = opt.Settings ?? throw new ArgumentException("settings are needed");
            var dest = opt.Destination ?? Folder(s);
            bool second = opt.SecondCopy ?? !string.IsNullOrEmpty(s.ArchiveFolder);
            var secondDir = second ? SecondFolder(s) : null;   // null when no additional-copy location is set
            StorageEstimate est;
            opt.Say("scan", "Listing the files to back up...");
            try
            {
                try { Directory.CreateDirectory(dest); } catch (Exception ex) { throw new StorageRefusedException("The backup location could not be created (" + ex.Message + "). Choose another location."); }
                est = Estimate(reqs, dest, secondDir != null, opt.ParityPercent, opt.Cancelled);
                if (!est.Fits) throw new StorageRefusedException("There is not enough free space in the backup location: about " + Units.Gb(est.TotalBytes) + " is needed and " + Units.Gb(est.FreeBytes) + " is free. Nothing was copied.");
                if (secondDir != null)
                {
                    try { Directory.CreateDirectory(secondDir); } catch (Exception ex) { throw new StorageRefusedException("The additional-copy location could not be created (" + ex.Message + "). Choose another, or turn the additional copy off."); }
                    foreach (var x in est.Sources.Where(x => x.req.Mode == "image" && x.req.DiskNumber >= 0))
                    {
                        var dd = DiskQueries.DisksOf(secondDir);
                        if (dd == null || dd.Contains(x.req.DiskNumber)) throw new StorageRefusedException(dd == null ? "Which drive holds the additional-copy location cannot be determined, so it cannot be shown to be a different drive from the one being copied. Turn the additional copy off, or choose another location." : "The additional-copy location is on the drive being copied. Choose another location, or turn the additional copy off.");
                    }
                    long f2 = Durable.FreeBytes(secondDir); bool sameVol = Durable.VolumeId(secondDir) == Durable.VolumeId(dest);
                    long need2 = sameVol ? est.TotalBytes + est.CopyBytes : est.CopyBytes + CaptureEngine.Reserve;
                    if (f2 >= 0 && f2 < need2) throw new StorageRefusedException("There is not enough free space for the additional copy: about " + Units.Gb(need2) + " is needed and " + Units.Gb(f2) + " is free. Nothing was copied.");
                }
            }
            catch (StorageRefusedException ex) { o.State = "refused"; o.Problem = ex.Message; return o; }
            catch (OperationCanceledException) { o.State = "canceled"; o.Problem = "Canceled before anything was copied."; return o; }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is System.ComponentModel.Win32Exception) { o.State = "refused"; o.Problem = "The source could not be read: " + ex.Message + " Nothing was copied."; return o; }
            // a connected drive copied whole: Windows' permission asked for once, the drive checked again and its volumes locked by the
            // administrator step, all before anything is written here
            RawDiskSession raw = null;
            var whole = est.Sources.FirstOrDefault(x => x.req.Mode == "image" && x.req.DiskNumber >= 0);
            if (whole.req != null)
            {
                opt.Say("elevate", "Asking Windows for administrator permission to read the drive whole...");
                var protect = new List<ProtectedPath> { new ProtectedPath(dest, "your backup location"), new ProtectedPath(s.RecordsFolder, "Workbench's records") };
                if (secondDir != null) protect.Add(new ProtectedPath(secondDir, "your additional-copy location"));
                raw = RawDiskClient.OpenRead(whole.req.Disk, ExternalFormat.RegionSizeFor(whole.req.Disk.Bytes), protect, opt.Cancelled);
                if (raw.Refusal != null)
                {
                    o.State = raw.Canceled ? "canceled" : "refused"; o.Problem = raw.Refusal + " Nothing was copied."; o.ElevationDeclined = raw.Declined;
                    raw.Dispose(); return o;
                }
            }
            try { return RunOpened(reqs, opt, o, s, dest, secondDir, est, raw); }
            finally { raw?.Dispose(); }
        }

        static StorageOutcome RunOpened(IList<StorageRequest> reqs, StorageOptions opt, StorageOutcome o, Settings s, string dest, string secondDir, StorageEstimate est, RawDiskSession raw)
        {

            var started = DateTime.Now; var startedUtc = DateTime.UtcNow;
            o.Id = startedUtc.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + "-" + Guid.NewGuid().ToString("N").Substring(0, 8);
            var label = est.Sources.Count == 1 ? est.Sources[0].id.Display : est.Sources.Count + " sources";
            o.PartialPath = CaptureEngine.Unique(dest, ExternalFormat.FileStem(started, label), ExternalFormat.Partial);
            var finalPath = o.PartialPath.Substring(0, o.PartialPath.Length - ".partial".Length);
            Record rec;
            try { rec = Record.Start(s.RecordsFolder, o.Id, "storage", new JObj { { "entry", opt.Entry }, { "purpose", opt.Purpose }, { "label", label }, { "sources", est.Sources.Select(x => (object)new JObj { { "display", x.id.Display }, { "key", x.id.Key }, { "mode", x.req.Mode } }).ToList() }, { "primary", new JObj { { "partial", o.PartialPath }, { "final", finalPath } } } }); }
            catch (Exception ex) { o.State = "refused"; o.Problem = "The backup location cannot be written (" + ex.Message + "). Choose another location. Nothing was copied."; return o; }
            o.RecordFile = rec.File; rec.State("reading"); opt.At("record-started");
            var counters = new FileCapture.Counters { Total = est.DataBytes + est.ImageBytes };
            StorageWriter w = null;
            try
            {
                using (var fs = Durable.CreateNew(o.PartialPath))
                {
                    w = new StorageWriter(fs, o.Id, started);
                    var srcJson = new List<object>();
                    for (int i = 0; i < est.Sources.Count; i++)
                    {
                        var (r, id, _) = est.Sources[i];
                        srcJson.Add(new JObj { { "id", ExternalFormat.SourceId(i + 1) }, { "mode", r.Mode }, { "label", id.Display } });
                    }
                    var readme = Encoding.UTF8.GetBytes(ExternalFormat.Readme(Json.Parse(Json.Write(new JObj { { "created_utc", Words.Utc(startedUtc) }, { "tool", new JObj { { "name", opt.ToolName }, { "version", opt.ToolVersion } } }, { "sources", srcJson } }))));
                    w.Add(ExternalFormat.ReadmeName, true, st => st.Write(readme, 0, readme.Length));
                    for (int i = 0; i < est.Sources.Count; i++)
                    {
                        var (r, id, plan) = est.Sources[i]; var src = ExternalFormat.SourceId(i + 1);
                        var sr = new StorageSourceResult { Id = src, Mode = r.Mode, Identity = id, Label = id.Display, StartedUtc = DateTime.UtcNow };
                        o.Sources.Add(sr);
                        var c = new FileCapture.Counters { Done = counters.Done, Total = counters.Total };
                        bool clean;
                        if (r.Mode == "files")
                        {
                            foreach (var om in plan.Omitted) sr.Omitted.Add(om);
                            clean = FileCapture.Write(w, src, plan, opt, c);
                            var after = Devices.ForFolder(r.Path);
                            if (after.Key != id.Key) { clean = false; c.Problems.Add("the drive's identity changed while it was read (it was disconnected, replaced or reformatted)"); }
                            sr.Summary.Set("files", plan.Files); sr.Summary.Set("folders", plan.FolderCount); sr.Summary.Set("links", plan.Links); sr.Summary.Set("bytes", plan.Bytes);
                        }
                        else if (r.DiskNumber >= 0) clean = ImageCapture.WriteDisk(w, src, raw, opt, c, sr.Summary);
                        else clean = ImageCapture.Write(w, src, r.Path, opt, c, sr.Summary);
                        counters.Done = c.Done;
                        sr.FinishedUtc = DateTime.UtcNow; sr.Complete = clean;
                        sr.Files = c.Files; sr.Folders = c.Folders; sr.Links = c.Links; sr.Bytes = c.Bytes; sr.Unreadable = c.Unreadable; sr.Changed = c.Changed; sr.Problems.AddRange(c.Problems);
                        rec.Set("progress", src + " " + r.Mode).Save(); opt.At("source-done");
                    }
                    o.ScopeComplete = o.Sources.All(x => x.Complete);
                    o.ConsistencyText = Consistency(o.Sources);
                    rec.State("finishing"); opt.At("before-manifest");
                    w.Finish(maps => Manifest(o, opt, startedUtc, w, maps));
                }
                opt.At("partial-closed");
                rec.State("checking");
                opt.Say("verify", "Checking the backup file...");
                var v = StorageVerifier.Verify(o.PartialPath, w.Expected, opt.Cancelled, (d, t) => opt.Say("verify", "Checking the backup file...", d, t));
                o.Verification = v;
                if (!v.Ok) { rec.Set("problem", v.Problems[0]); rec.State("damaged"); o.State = "failed"; o.Problem = "The backup file did not pass its check, so it was not kept as a backup: " + v.Problems[0]; return o; }
                o.Sha256 = v.ArchiveSha256; o.Bytes = v.Bytes;
                rec.Set("primary", new JObj { { "partial", o.PartialPath }, { "final", finalPath }, { "sha256", v.ArchiveSha256 }, { "bytes", v.Bytes } }).Set("scope_complete", o.ScopeComplete); rec.State("checked"); opt.At("checked");
                Durable.Publish(o.PartialPath, finalPath); o.PrimaryPath = finalPath; o.PartialPath = null;
                var fi = new FileInfo(finalPath);
                rec.Set("primary", new JObj { { "final", finalPath }, { "sha256", v.ArchiveSha256 }, { "bytes", v.Bytes }, { "mtime_utc", fi.LastWriteTimeUtc.ToString("o", CultureInfo.InvariantCulture) }, { "verified_utc", Record.Now() } });
                rec.State("published"); opt.At("published");
            }
            catch (OperationCanceledException) { rec.State("canceled"); o.State = "canceled"; o.Problem = "Canceled. The unfinished file is not a backup and is never used."; TryDelete(o.PartialPath); return o; }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is System.ComponentModel.Win32Exception || ex is StorageRefusedException)
            { rec.Set("problem", ex.Message); rec.State("stopped"); o.State = "failed"; o.Problem = "The backup stopped: " + ex.Message + " The unfinished file is not a backup and is never used."; TryDelete(o.PartialPath); return o; }
            // the additional copy
            o.State = "complete-one-copy";
            if (secondDir != null)
            {
                rec.State("copying");
                var r2 = Repair.SecondCopy.Make(o.PrimaryPath, secondDir, new Repair.RepairOptions { Cancelled = opt.Cancelled, SaveReport = false, Progress = p => opt.Say("copy", "Making the additional copy...", p.Done, p.Total) });
                if (r2.Outcome == "copied") { o.ArchivePath = r2.OutputPath; o.SameDrive = r2.SameDrive; o.SameDisk = r2.SameDrive == "same"; rec.Set("archive", new JObj { { "final", r2.OutputPath }, { "same_disk", o.SameDisk }, { "verified_utc", Record.Now() } }); o.State = "complete"; }
                else { o.CopyProblem = r2.Text; rec.Set("archive", new JObj { { "problem", r2.Text } }); }
            }
            if (opt.ParityPercent > 0)
            {
                var p = StorageRepair.Protect(o.PrimaryPath, opt.ParityPercent, (t, d, n) => opt.Say("parity", t, d, n), opt.Cancelled);
                o.ParityOk = p.Ok; o.ParityText = p.Text; rec.Set("parity", new JObj { { "ok", p.Ok }, { "text", p.Text } });
            }
            rec.State(o.State); opt.At("complete");
            return o;
        }

        static void TryDelete(string p) { try { if (p != null && File.Exists(p)) File.Delete(p); } catch (Exception) { } }

        /// <summary>when each source was read, in plain words: never one snapshot when there are several</summary>
        public static string Consistency(IList<StorageSourceResult> src)
        {
            string W(DateTime t) => t.ToLocalTime().ToString("h:mm:ss tt", CultureInfo.GetCultureInfo("en-US"));
            string Day(DateTime t) => t.ToLocalTime().ToString("MMMM d, yyyy", CultureInfo.GetCultureInfo("en-US"));
            if (src.Count == 1)
            {
                var x = src[0];
                return x.Label + " was read on " + Day(x.StartedUtc) + " from " + W(x.StartedUtc) + " to " + W(x.FinishedUtc) + ". " + (x.Mode == "image" && x.Identity?.Kind == SourceKind.Disk
                    ? (x.Complete ? "Its volumes were locked while it was read, so no other program could write to it meanwhile." : "Another program had it open, so its volumes could not all be locked while it was read: a program could have written to it meanwhile.")
                    : x.Mode == "image"
                    ? "No other program could write to the drive image file while it was read."
                    : "Each file was checked for changes before and after it was copied; files are copied one after another, so files that other programs changed during that time are named.");
            }
            return "The " + src.Count + " sources were read one after another, not at one moment: " + string.Join("; ", src.Select(x => x.Label + " from " + W(x.StartedUtc) + " to " + W(x.FinishedUtc))) + " on " + Day(src[0].StartedUtc)
                + ". Together they are not one snapshot taken at a single moment: a change on one source after it was read is not in this backup.";
        }

        static JObj Manifest(StorageOutcome o, StorageOptions opt, DateTime startedUtc, StorageWriter w, List<MapListing> maps)
        {
            var content = w.Entries.Where(e => !ExternalFormat.IsMap(e.Name)).Select(e => e.Name).ToList();
            string namesSha; using (var h = SHA256.Create()) namesSha = Hex.Of(h.ComputeHash(Encoding.UTF8.GetBytes(string.Join("\n", content))));
            var notIncluded = new List<object> { "A console's built-in storage: that is a console backup, a separate file made with Back Up Console." };
            foreach (var x in o.Sources)
            {
                foreach (var om in x.Omitted) notIncluded.Add(x.Label + ": " + om.path + " (" + om.why + ")");
                if (x.Unreadable > 0) notIncluded.Add(x.Label + ": " + Words.Count(x.Unreadable, "file") + " could not be read");
                if (x.Changed > 0) notIncluded.Add(x.Label + ": " + Words.Count(x.Changed, "file") + " changed while " + (x.Changed == 1 ? "it was" : "they were") + " copied, so " + (x.Changed == 1 ? "its copy is" : "their copies are") + " not offered as good copies");
            }
            return new JObj
            {
                { "format", ExternalFormat.Format }, { "schema", ExternalFormat.Schema }, { "backup_id", o.Id },
                { "created_utc", Words.Utc(startedUtc) }, { "completed_utc", Words.Utc(DateTime.UtcNow) },
                { "tool", new JObj { { "name", opt.ToolName }, { "version", opt.ToolVersion }, { "engine", EngineInfo.Engine }, { "entry", opt.Entry } } },
                { "purpose", opt.Purpose },
                { "scope", new JObj
                    {
                        { "complete", o.ScopeComplete },
                        { "text", o.ScopeComplete ? "Everything selected was copied, and every file was unchanged while it was copied." : "Not everything selected was copied as it was: see what is not included. The files that were copied unchanged can still be used." },
                        { "consistency", o.ConsistencyText }, { "not_included", notIncluded },
                    } },
                { "sources", o.Sources.Select(x => (object)new JObj
                    {
                        { "id", x.Id }, { "mode", x.Mode }, { "label", x.Label }, { "identity", x.Identity.ToJson() }, { "complete", x.Complete },
                        { "started_utc", Words.Utc(x.StartedUtc) }, { "finished_utc", Words.Utc(x.FinishedUtc) },
                        { "omitted", x.Omitted.Select(om => (object)new JObj { { "path", om.path }, { "why", om.why } }).ToList() },
                        { "problems", x.Problems.Take(500).Cast<object>().ToList() }, { "problem_count", (long)x.Problems.Count },
                        { "summary", x.Summary },
                        { "metadata", x.Mode == "files"
                            ? new JObj { { "kept", new List<object> { "file contents and sizes", "the folder tree, empty folders included", "creation, last-write and last-access times as Windows reports them (FAT keeps 2-second and date-only steps)", "read-only, hidden, system and archive attributes" } },
                                         { "not_kept", new List<object> { "permissions and owners (Windows security settings)", "alternate data streams", "extended attributes (their size is recorded)", "short 8.3 names", "links: listed, not followed or copied" } } }
                            : new JObj { { "kept", new List<object> { "every byte of the drive: its partition table, file systems and free space" } },
                                         { "not_kept", new List<object> { "nothing on the drive is left out; listing its files needs a FAT or ext file system" } } } },
                    }).ToList() },
                { "maps", maps.Select(m => (object)StorageWriter.ListingJson(m)).ToList() },
                { "members", (long)content.Count }, { "member_names_sha256", namesSha },
                { "payload_bytes", w.Entries.Where(e => ExternalFormat.IsPayload(e.Name)).Sum(e => e.Size) },
            };
        }
    }
}
