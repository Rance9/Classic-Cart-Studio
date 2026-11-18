// Classic Cart Studio console backup and restore engine. Copyright (C) 2026 Rance9. GPL-3.0-or-later.
// Putting a battery save into a save folder: the Backup Browser's import and Expert's save drop use
// this one set of rules.
//   - a save is never replaced silently: a different save already there needs the person's choice, and a save there that is NEWER than
//     the incoming one (or whose age cannot be told) needs the explicit "replace-newer" choice; the default is always to keep it;
//   - every save replaced is kept first, exactly, write-once, in replaced-<time>-<id>/ beside it with its name, its date and a record
//     (replaced.json, ccs-replaced-save/1), read back before anything is replaced; ListReplaced finds them again and Recover turns one
//     back into an incoming save (imported by these same rules, so the save it replaces is kept in turn);
//   - a battery save of a different size is never put over another (it belongs to another game file or format), a saved state is never
//     taken for a battery save, and a conversion that is a comparison copy (not confirmed by play) is never imported;
//   - the save folder is read again just before the change: if it changed since the plan, nothing is written.
// Decide is the rule itself (no files): a store other than a PC folder applies the same decision.
using CcsBackup.Library;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace CcsBackup.Saves
{
    public sealed class SaveFile
    {
        public string Name; public byte[] Bytes; public DateTime? ModifiedUtc; public string Origin;
    }

    public sealed class ImportPlan
    {
        public string Folder, Name, Target;
        /// <summary>add | same | older-here | newer-here | undated | refused</summary>
        public string Status, Code, Text;
        public SaveFile Incoming; public string IncomingSha256;
        public bool CurrentExists; public string CurrentSha256; public long CurrentBytes = -1; public DateTime? CurrentModifiedUtc;
        /// <summary>the choices that change something here (keep is always possible)</summary>
        public IReadOnlyList<string> Choices = new string[0];
        public JObj ToJson() => new JObj
        {
            { "name", Name }, { "status", Status }, { "code", Code }, { "text", Text }, { "choices", Choices.ToList() },
            { "incoming", new JObj { { "bytes", Incoming?.Bytes?.LongLength ?? -1 }, { "sha256", IncomingSha256 }, { "modified_utc", SaveSource.Iso(Incoming?.ModifiedUtc) }, { "origin", Incoming?.Origin } } },
            { "current", CurrentExists ? new JObj { { "bytes", CurrentBytes }, { "sha256", CurrentSha256 }, { "modified_utc", SaveSource.Iso(CurrentModifiedUtc) } } : null },
        };
    }

    public sealed class ImportOutcome
    {
        public bool Ok, Changed; public string State, Text, KeptPath, RecordPath, WrittenSha256;
    }

    public sealed class ReplacedSave
    {
        public string Folder, Name, KeptPath, RecordPath, Sha256, ReplacedBySha256, Choice, IncomingOrigin;
        public long Bytes; public DateTime? OriginalModifiedUtc; public DateTime? ReplacedUtc; public bool Intact;
    }

    public static class SaveImport
    {
        public const string Keep = "keep", Add = "add", Replace = "replace", ReplaceNewer = "replace-newer";
        public const string RecordSchema = "ccs-replaced-save/1", RecordName = "replaced.json";
        static readonly Regex NameRe = new Regex("^[A-Za-z0-9][A-Za-z0-9._-]{0,119}$"), KeptRe = new Regex("^replaced-[0-9]{8}-[0-9]{6}-[0-9a-f]{8}$");
        static readonly byte[] StateMagic = Encoding.ASCII.GetBytes("CCSSTATE");
        const long MaxSave = 1 << 20;

        static string Sha(byte[] b) { using (var h = SHA256.Create()) return Hex.Of(h.ComputeHash(b)); }
        static bool IsBattery(string name) => name.EndsWith(".sav", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".sram", StringComparison.OrdinalIgnoreCase);

        /// <summary>the rule: what importing incoming over current (null: nothing there) would be. Returns status, code and a plain text.</summary>
        public static (string status, string code, string text) Decide(byte[] current, DateTime? currentModifiedUtc, SaveFile incoming, SaveConversion conversion = null)
        {
            if (incoming?.Bytes == null || incoming.Name == null || !NameRe.IsMatch(incoming.Name) || incoming.Name.Contains(".."))
                return ("refused", "damaged", "The save to import has no usable name or contents. Nothing was changed.");
            if (incoming.Bytes.LongLength > MaxSave) return ("refused", "too-large", "The file is larger than any battery save. Nothing was changed.");
            if (StockSave.IsSuspendPointPath(incoming.Name)) return ("refused", "not-a-battery-save", "Suspend points and saved states are not battery saves and are not imported here. Nothing was changed.");
            if (conversion != null)
            {
                if (!conversion.Ok) return ("refused", "not-converted", "The save was not converted, so there is nothing to import. Nothing was changed.");
                if (conversion.Trial) return ("refused", "trial", "This is a comparison copy that is not confirmed by play on a real console. It can be exported, but it is not put into your saves. Nothing was changed.");
                if (!conversion.Importable) return ("refused", "not-importable", "This conversion cannot be imported. Nothing was changed.");
                if (!conversion.Output.SequenceEqual(incoming.Bytes)) return ("refused", "not-the-conversion", "The file to import is not the converted save that was shown. Nothing was changed.");
            }
            if (IsBattery(incoming.Name) && incoming.Bytes.Length >= StateMagic.Length && incoming.Bytes.Take(StateMagic.Length).SequenceEqual(StateMagic))
                return ("refused", "state-as-battery", "This is a saved state, not a battery save. Nothing was changed.");
            if (current == null) return ("add", "add", "There is no save here yet: the imported save is added.");
            if (current.SequenceEqual(incoming.Bytes)) return ("same", "same", "The same save is already here. Nothing needs to change.");
            if (IsBattery(incoming.Name) && current.LongLength != incoming.Bytes.LongLength)
                return ("refused", "different-size", "The save here has a different size, so it belongs to another version of the game or another format. It was not replaced. Nothing was changed.");
            if (currentModifiedUtc == null || incoming.ModifiedUtc == null)
                return ("undated", "undated", "A different save is already here, and which one is newer cannot be told. It is kept unless you choose to replace it; it would be kept first.");
            var c = SaveSource.Utc(currentModifiedUtc.Value); var i = SaveSource.Utc(incoming.ModifiedUtc.Value);
            if (c > i) return ("newer-here", "newer-here", "The save here (" + SaveSource.Iso(c) + ") is newer than the one being imported (" + SaveSource.Iso(i) + "). It is kept unless you choose to replace the newer save; it would be kept first.");
            if (c < i) return ("older-here", "older-here", "A different, older save is here (" + SaveSource.Iso(c) + "). It is kept unless you choose to replace it; it would be kept first.");
            return ("undated", "same-time", "A different save with the same date is already here, so which one is newer cannot be told. It is kept unless you choose to replace it; it would be kept first.");
        }

        static (bool exists, byte[] bytes, DateTime? mtime) ReadCurrent(string target)
        {
            if (Directory.Exists(target)) throw new IOException("a folder has the save's name");
            if (!File.Exists(target)) return (false, null, null);
            var fi = new FileInfo(target);
            if (fi.Length > MaxSave) throw new IOException("the save here is larger than any battery save");
            return (true, File.ReadAllBytes(target), fi.LastWriteTimeUtc);
        }

        public static ImportPlan Plan(string folder, SaveFile incoming, SaveConversion conversion = null)
        {
            var p = new ImportPlan { Folder = folder, Name = incoming?.Name, Incoming = incoming, IncomingSha256 = incoming?.Bytes == null ? null : Sha(incoming.Bytes) };
            (bool exists, byte[] bytes, DateTime? mtime) cur = (false, null, null);
            if (folder != null && incoming?.Name != null && NameRe.IsMatch(incoming.Name))
            {
                p.Target = Path.Combine(folder, incoming.Name);
                try { cur = ReadCurrent(p.Target); }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { p.Status = "refused"; p.Code = "unreadable"; p.Text = "The save here could not be read (" + ex.Message + "), so nothing was changed."; return p; }
            }
            p.CurrentExists = cur.exists;
            if (cur.exists) { p.CurrentSha256 = Sha(cur.bytes); p.CurrentBytes = cur.bytes.LongLength; p.CurrentModifiedUtc = cur.mtime; }
            var (st, code, text) = Decide(cur.bytes, cur.mtime, incoming, conversion);
            if (folder == null && st != "refused") { st = "refused"; code = "damaged"; text = "No save folder was given. Nothing was changed."; }
            p.Status = st; p.Code = code; p.Text = text;
            p.Choices = st == "add" ? new[] { Add } : st == "older-here" ? new[] { Replace, ReplaceNewer } : st == "newer-here" || st == "undated" ? new[] { ReplaceNewer } : new string[0];
            return p;
        }

        static ImportOutcome Out(string state, string text, bool ok = false, bool changed = false) => new ImportOutcome { State = state, Text = text, Ok = ok, Changed = changed };

        /// <summary>carries out a plan with the person's choice (null: the default, which adds to an empty place and otherwise keeps)</summary>
        public static ImportOutcome Apply(ImportPlan plan, string choice = null)
        {
            if (plan == null || plan.Status == "refused" || plan.Target == null) return Out("refused", plan?.Text ?? "Nothing was changed.");
            if (plan.Status == "same") return Out("same", plan.Text, true);
            choice = choice ?? (plan.Status == "add" ? Add : Keep);
            if (choice == Keep) return Out("kept", plan.Status == "add" ? "Nothing was imported." : "The save here was kept. Nothing was changed.", true);
            if (!plan.Choices.Contains(choice))
                return Out("refused", plan.Status == "newer-here" || plan.Status == "undated" ? "The save here may be newer than the one being imported: only Replace Newer Save replaces it. Nothing was changed." : "That choice does not apply to this save. Nothing was changed.");
            // the folder read again: it must be exactly as planned
            (bool exists, byte[] bytes, DateTime? mtime) now;
            try { now = ReadCurrent(plan.Target); }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { return Out("refused", "The save here could not be read again (" + ex.Message + "). Nothing was changed."); }
            if (now.exists != plan.CurrentExists || (now.exists && Sha(now.bytes) != plan.CurrentSha256)) return Out("refused", "The save here changed since this import was planned. Look at it again. Nothing was changed.");
            var data = plan.Incoming.Bytes; string keptPath = null, recordPath = null;
            if (now.exists)
            {
                try { (keptPath, recordPath) = KeepFirst(plan, now.bytes, now.mtime, choice); }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { return Out("refused", "The save here could not be kept first (" + ex.Message + "), so it was not replaced. Nothing was changed."); }
            }
            try
            {
                if (now.exists) Durable.WriteAtomic(plan.Target, data);   // the old save or the new one, never a part
                else
                {
                    var tmp = Path.Combine(plan.Folder, "." + plan.Name + ".import-" + Guid.NewGuid().ToString("N").Substring(0, 8));
                    using (var f = Durable.CreateNew(tmp)) { f.Write(data, 0, data.Length); Durable.Flush(f); }
                    try { Durable.Publish(tmp, plan.Target); } catch { try { File.Delete(tmp); } catch { } throw; }
                }
                if (plan.Incoming.ModifiedUtc.HasValue) File.SetLastWriteTimeUtc(plan.Target, SaveSource.Utc(plan.Incoming.ModifiedUtc.Value));
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                return new ImportOutcome { State = "failed", KeptPath = keptPath, RecordPath = recordPath, Text = "The imported save could not be written (" + ex.Message + "). The save here is unchanged" + (keptPath != null ? "; its copy is also kept in " + Path.GetFileName(Path.GetDirectoryName(keptPath)) + "." : ".") };
            }
            var back = File.ReadAllBytes(plan.Target);
            if (!back.SequenceEqual(data)) return new ImportOutcome { State = "check-failed", KeptPath = keptPath, RecordPath = recordPath, Changed = true, Text = "The imported save did not read back correctly." + (keptPath != null ? " The save that was here is kept in " + Path.GetFileName(Path.GetDirectoryName(keptPath)) + "." : "") };
            return new ImportOutcome
            {
                Ok = true, Changed = true, State = now.exists ? "replaced" : "added", KeptPath = keptPath, RecordPath = recordPath, WrittenSha256 = Sha(back),
                Text = now.exists ? "Imported. The save that was here is kept in " + Path.GetFileName(Path.GetDirectoryName(keptPath)) + " with its name and date." : "Imported.",
            };
        }

        /// <summary>the save being replaced, copied write-once beside it with its record, both read back</summary>
        static (string kept, string record) KeepFirst(ImportPlan plan, byte[] bytes, DateTime? mtime, string choice)
        {
            string dir = null;
            for (int i = 0; i < 20 && dir == null; i++)
            {
                var d = Path.Combine(plan.Folder, "replaced-" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + "-" + Guid.NewGuid().ToString("N").Substring(0, 8));
                if (!Directory.Exists(d) && !File.Exists(d)) { Directory.CreateDirectory(d); dir = d; }
            }
            if (dir == null) throw new IOException("no free place to keep the save");
            var kept = Path.Combine(dir, plan.Name);
            using (var f = Durable.CreateNew(kept)) { f.Write(bytes, 0, bytes.Length); Durable.Flush(f); }
            if (!File.ReadAllBytes(kept).SequenceEqual(bytes)) throw new IOException("the kept copy did not read back the same");
            if (mtime.HasValue) File.SetLastWriteTimeUtc(kept, mtime.Value);
            var rec = new JObj
            {
                { "schema", RecordSchema }, { "name", plan.Name }, { "replaced_utc", SaveSource.Iso(DateTime.UtcNow) }, { "choice", choice },
                { "original", new JObj { { "bytes", bytes.LongLength }, { "sha256", Sha(bytes) }, { "modified_utc", SaveSource.Iso(mtime) } } },
                { "replaced_by", new JObj { { "bytes", plan.Incoming.Bytes.LongLength }, { "sha256", plan.IncomingSha256 }, { "modified_utc", SaveSource.Iso(plan.Incoming.ModifiedUtc) }, { "origin", plan.Incoming.Origin } } },
                { "note", "The save that was here before an import, kept exactly. To put it back, import it the same way; the save it replaces is kept in turn." },
            };
            var rp = Path.Combine(dir, RecordName); var rb = Json.Bytes(rec);
            using (var f = Durable.CreateNew(rp)) { f.Write(rb, 0, rb.Length); Durable.Flush(f); }
            return (kept, rp);
        }

        /// <summary>every save kept by an import in this folder, newest first; Intact: the kept bytes still match their record</summary>
        public static List<ReplacedSave> ListReplaced(string folder)
        {
            var list = new List<ReplacedSave>();
            if (folder == null || !Directory.Exists(folder)) return list;
            foreach (var d in Directory.GetDirectories(folder).Where(x => KeptRe.IsMatch(Path.GetFileName(x))))
            {
                var rp = Path.Combine(d, RecordName);
                if (!File.Exists(rp) || new FileInfo(rp).Length > 65536) continue;   // a restore's own replaced- folder has no such record
                Dictionary<string, object> r;
                try { r = Json.Parse(File.ReadAllText(rp, Encoding.UTF8)); } catch (FormatException) { continue; }
                if (r.S("schema") != RecordSchema || r.S("name") == null || !NameRe.IsMatch(r.S("name"))) continue;
                var o = r.O("original"); var by = r.O("replaced_by");
                var s = new ReplacedSave
                {
                    Folder = d, Name = r.S("name"), KeptPath = Path.Combine(d, r.S("name")), RecordPath = rp, Sha256 = o?.S("sha256"), Bytes = o?.L("bytes") ?? -1,
                    OriginalModifiedUtc = Time(o?.S("modified_utc")), ReplacedUtc = Time(r.S("replaced_utc")), ReplacedBySha256 = by?.S("sha256"), Choice = r.S("choice"), IncomingOrigin = by?.S("origin"),
                };
                s.Intact = File.Exists(s.KeptPath) && new FileInfo(s.KeptPath).Length == s.Bytes && Sha(File.ReadAllBytes(s.KeptPath)) == s.Sha256;
                list.Add(s);
            }
            return list.OrderByDescending(x => x.ReplacedUtc ?? DateTime.MinValue).ToList();
        }

        static DateTime? Time(string iso) => DateTime.TryParseExact(iso ?? "", "yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var t) ? t : (DateTime?)null;

        /// <summary>a kept save as an incoming save, with its own name and date, to import back by the same rules</summary>
        public static SaveFile Recover(ReplacedSave r)
        {
            if (r == null || !r.Intact) return null;
            return new SaveFile { Name = r.Name, Bytes = File.ReadAllBytes(r.KeptPath), ModifiedUtc = r.OriginalModifiedUtc, Origin = "the save kept on " + SaveSource.Iso(r.ReplacedUtc) };
        }
    }
}
