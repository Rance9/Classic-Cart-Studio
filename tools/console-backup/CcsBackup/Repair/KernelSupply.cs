// Classic Cart Studio console backup and restore engine. Copyright (C) 2026 Rance9. GPL-3.0-or-later.
// Supply a missing or damaged startup software file (D7). A backup records each of its startup software parts (the kernel images:
// restore/boot2-active.img, restore/boot2-slot68.img and recovery/original-kernel.img) with its exact size, SHA-256 and MD5, and with the
// SHA-256 of each 1,048,576-byte piece when it has repair information. When such a part is damaged or missing in the backup and in every
// copy of it, and the person has an exact copy of that same file kept somewhere on this PC, they can choose it here:
//   - it is accepted only for a startup software part, only when the backup's own trusted records (its manifest and repair map, in
//     agreement: TrustedMap) list that part, only when the part really is damaged or missing in every file given, and only when the chosen
//     file's size, SHA-256 and MD5 (and every piece checksum, when recorded) equal that record exactly. Where the file came from does not
//     matter and is never asked: only its bytes are checked, so the result holds exactly the bytes this backup recorded;
//   - everything else is refused with the reason and nothing is written: a file whose size or checksum differs (also when it is another of
//     this backup's parts, which is said), any other kind of part, a part the records do not list, a backup whose records do not survive;
//   - an accepted file becomes one more source for the one way a repair makes a new backup file (Rebuild.cs): every byte re-hashed as it is
//     copied, the new file checked in full from scratch before it gets its name, the backup, its copies and the chosen file only read.
// No startup software is supplied, linked, searched for or downloaded by Classic Cart Studio, a file is never taken from another console's
// records, and a different file is never made to fit: a new checksum is never put in place of the recorded one. Choosing a different,
// compatible system for a console is Console Composer's separate job, not a repair.
using CcsBackup.Archive;
using CcsBackup.Library;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace CcsBackup.Repair
{
    /// <summary>One startup software part of a backup, as its records describe it and as it is found in the files given.</summary>
    public sealed class SupplyPart
    {
        public string Member, Name, Sha256, Md5, RecordedBy;
        public long Size;
        /// <summary>intact | damaged | missing (in every file given)</summary>
        public string State;
        public bool Needed => State != "intact";
    }

    public sealed class SupplySurvey
    {
        public string Problem, BackupId, ConsoleTag;
        public readonly List<SupplyPart> Parts = new List<SupplyPart>();
    }

    public static class KernelSupply
    {
        /// <summary>the only parts a file can be supplied for: the startup software images</summary>
        public static readonly string[] Members = { "restore/boot2-active.img", "restore/boot2-slot68.img", BackupFormat.OriginalKernelName };

        public static string NameOf(string member) =>
            member == "restore/boot2-active.img" ? "The startup software in use" : member == "restore/boot2-slot68.img" ? "The spare startup slot (hakchi's)"
            : member == BackupFormat.OriginalKernelName ? "The original Nintendo startup software" : member;

        static string N(long x) => x.ToString("N0", CultureInfo.InvariantCulture);

        /// <summary>The backup's recorded startup software parts and whether each is intact, damaged or missing in the backup and every copy
        /// given (only read; nothing is written). Problem: the records do not survive, so nothing can be checked.</summary>
        public static SupplySurvey Parts(string backup, IList<string> copies = null, RepairOptions opt = null)
        {
            opt = opt ?? new RepairOptions(); var sv = new SupplySurvey(); var files = new List<BackupFile>();
            try
            {
                var paths = new List<string> { backup }; if (copies != null) paths.AddRange(copies);
                foreach (var p in paths) { if (!File.Exists(p)) { sv.Problem = "The file " + p + " was not found."; return sv; } files.Add(BackupFile.Open(p, Path.GetFileName(p))); }
                var map = TrustedMap.Build(files, out var problem);
                if (map == null) { sv.Problem = "No trustworthy record of this backup's parts survives, so a supplied file could not be checked against anything (" + problem + ")."; return sv; }
                sv.BackupId = map.BackupId; sv.ConsoleTag = map.Tag;
                long total = files.Sum(f => f.Length), seen = 0;
                foreach (var f in files) f.Examine(map, opt.Cancelled, n => { seen += n; opt.Say("examine", "Checking the backup...", seen, total); });
                foreach (var m in Members)
                {
                    var r = map.Find(m); if (r == null || r.Sha256 == null) continue;   // this backup has no such part (a stock console has no spare slot)
                    var checks = files.Select(f => f.Check(m)).Where(c => c != null).ToList();
                    sv.Parts.Add(new SupplyPart { Member = m, Name = NameOf(m), Sha256 = r.Sha256, Md5 = r.Md5, Size = r.Size, RecordedBy = r.RecordedBy,
                        State = checks.Any(c => c.Verified) ? "intact" : checks.Any(c => c.At != null) ? "damaged" : "missing" });
                }
                if (sv.Parts.Count == 0) sv.Problem = "This backup's records list no startup software part.";
            }
            catch (OperationCanceledException) { sv.Problem = "Canceled before the check finished; nothing was written."; }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { sv.Problem = "A file could not be read: " + ex.Message; }
            finally { foreach (var f in files) f.Dispose(); }
            return sv;
        }

        /// <summary>A new, complete backup file in outputFolder: the damaged backup (and any copies of it) with each chosen file filling in the
        /// startup software part it was chosen for, accepted only when it is byte-for-byte that part as recorded. Everything given is only
        /// read; cancel or any failure leaves nothing behind.</summary>
        public static RepairResult Run(string backup, IList<(string member, string file)> supplied, string outputFolder, RepairOptions options = null, IList<string> copies = null)
        {
            var all = new List<string> { backup }; if (copies != null) all.AddRange(copies);
            return Runner.Run("supply", all, new List<string>(), outputFolder, options, supplied);
        }

        /// <summary>Checks every chosen file against its part's record and opens the accepted ones as sources (into supplies). Returns the
        /// refusal in plain words, or null when every chosen file is accepted.</summary>
        internal static string Admit(TrustedMap map, IList<BackupFile> files, IList<(string member, string file)> supplied, List<BackupFile> supplies, RepairReport rep, RepairOptions opt)
        {
            foreach (var (member, file) in supplied)
            {
                var name = Path.GetFileName(file);
                if (!Members.Contains(member)) return "A file can be supplied only for a startup software part (" + string.Join(", ", Members) + "), not for " + (member ?? "an unnamed part") + ".";
                var r = map.Find(member);
                if (r == null || r.Sha256 == null || r.Md5 == null || r.Size < 0) return "This backup's records do not list " + member + " with its size and checksums, so there is nothing to check " + name + " against.";
                if (files.Any(f => f.Check(member)?.Verified == true)) return NameOf(member) + " (" + member + ") is intact in this backup; nothing needs to be supplied for it.";
                var sf = BackupFile.Supplied(file, "the file you supplied (" + name + ")", r, opt.Cancelled);
                supplies.Add(sf);
                var c = sf.Check(member);
                if (!c.Verified)
                {
                    var why = sf.Length != r.Size ? name + " is " + N(sf.Length) + " bytes; this backup recorded " + N(r.Size) + " bytes for " + member + "."
                        : c.Sha256 == null ? name + " could not be read in full (" + c.Note + ")."
                        : name + " has the SHA-256 " + c.Sha256 + "; this backup recorded " + r.Sha256 + " for " + member + ".";
                    // another of this backup's startup software parts? (a file of that part's recorded size is read once more against its record)
                    var other = Members.Where(m => m != member).Select(map.Find).Where(o => o?.Sha256 != null && o.Size == sf.Length).FirstOrDefault(o =>
                    {
                        if (c.Sha256 != null) return o.Sha256 == c.Sha256 && o.Md5 == c.Md5;
                        using (var t = BackupFile.Supplied(file, sf.Label, o, opt.Cancelled)) return t.Check(o.Path).Verified;
                    });
                    if (other != null) why += " It is this backup's " + other.Path + " (" + NameOf(other.Path).ToLowerInvariant() + ") instead, so it was not used for " + member + ".";
                    else why += " It is not the file this backup recorded, so it was not used.";
                    rep.Skip(sf.Label, why);
                    return why;
                }
                rep.Others.Add(new JObj { { "role", "supplied file" }, { "label", sf.Label }, { "file", name }, { "folder", Path.GetDirectoryName(sf.Path) }, { "bytes", sf.Length },
                    { "modified_utc", sf.ModifiedUtc.ToString("o", CultureInfo.InvariantCulture) }, { "sha256_as_read", c.Sha256 }, { "for_part", member },
                    { "condition", "chosen for " + member + "; its size, SHA-256 and MD5" + (r.Map?.Chunks != null ? " and every piece checksum" : "") + " equal this backup's record of that part (" + r.RecordedBy + ")" } });
                rep.Methods.Add("The file you supplied for " + member + " (" + name + ") was accepted only because its size (" + N(r.Size) + " bytes), SHA-256 and MD5 equal this backup's own record of that part (from " + r.RecordedBy + "). It was only read; where it came from was not asked and does not matter, because only its bytes were checked.");
            }
            rep.Unknowns.Add("Where the supplied file came from: Classic Cart Studio checked only that its bytes are exactly the ones this backup recorded.");
            return null;
        }
    }
}
