// Classic Cart Studio console backup and restore engine. Copyright (C) 2026 Rance9. GPL-3.0-or-later.
// Community Mods: installing, updating and removing listed authors' packages through hakchi's own route, on a console Hakchi2 CE 3.9.3
// prepared, in the preparation image's session (ram/ccscomm), behind its own boundary Gate.CommunityWrites. Like MethodEngine for CCS Mod
// and CCS Nucleus: the console's next normal start runs hakchi's p7080_hmods, which runs each listed package's own uninstall (the names in
// transfer/uninstall, in that order), then installs transfer/*.hmod in sorted order, then transfer/hmod/*.hmod.
//   1 check     the console identified and its model, region and Hakchi build matched against each record; hakchi's packages, what waits
//               in its transfer folder and the free space (ccsprep mstate) and each mod's state (ccscomm cstate); the change resolved in one
//               plan (CommunityResolver: dependencies, variant, conflicts, bases kept under what needs them, the author's own automatic
//               removals); each package file admitted by its exact pin and checked as an archive (CommunityAdmission)
//   2 protect   a mod that writes a USB drive or SD card: the USB and SD backup of that storage the person chose, verified from scratch,
//               holding every place the mod's record names, bound (CommunityStorage.cs); then a complete, verified backup of the console as
//               it is now, in this session
//   3 plan      a community menu CCS Nucleus's startup chooser starts (CommunityRecord.ChooserMenu) is a choice there: when the chooser is
//               not on the console, the same change adds it first (method:ccs-nucleus when CCS Nucleus is not there either, then
//               method:ccs-nucleus-chooser, their packages placed in hakchi's transfer folder; nstart, the chooser shown at start with
//               everything else of the start setting kept, or Original Menu when nothing is chosen where CCS Nucleus arrives now). A CCS
//               Nucleus (or chooser) installed in an earlier version is updated in the same change (this build's package placed over it,
//               installed by hakchi before CCS Nucleus starts anything; its settings and choices kept), named in the review.
//               ckeep (a mod's settings and favorites copied to Classic Cart Studio's own folder before its own removal deletes them, unless
//               the person chose to remove them too), cforget (that copy removed, only when chosen), nmdrop (a removed menu's choice taken
//               out of CCS Nucleus's list), cunlist (one removal list, a package that needs another before it), nmadd (a menu's choice
//               added to CCS Nucleus's list, or changed to its new package or program), cput (each package, its dependencies first: a
//               package whose dependency arrives in the same change waits in hmod/), crestore (kept settings put back where nothing is).
//               The review names every piece added. A one-time confirmation for exactly this plan
//   4 recheck   at the confirmation: the protective backup, the USB and SD backup and every package file held open and checked again
//               (the person must also confirm the USB and SD backup is of the storage the console uses: Workbench cannot read that storage
//               through the console), the console's state read again and unchanged, the recovery set of both backups and the record
//               saved before the first write
//   5 write     each step once; each is read back; the first failure stops every later step; nothing is retried or undone automatically
//   6 verify    what waits for the next start is exactly this plan's files; kept copies read back
// The PC keeps a ledger of what Workbench installed on each console (the record as it was listed, the pin, the payload's files, the backup
// made before), so a mod stays manageable after it is delisted, and the next connection reconciles it: hakchi listing a package is not
// enough; its files are read back and compared (ccscomm cverify). Hmods run with full control of the console: the pin proves the file is
// the approved one, it does not make the author's code safe or limit what it does.
using CcsBackup.Archive;
using CcsBackup.Device;
using CcsBackup.External;
using CcsBackup.Library;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace CcsBackup.Engine
{
    /// <summary>what a person asked for: entries to install, update or remove, kept settings to put back; files: the approved package file
    /// on this PC for each installed or updated entry (id -> file)</summary>
    public sealed class CommunityChange
    {
        public List<string> Install = new List<string>(), Update = new List<string>(), Remove = new List<string>(), RestoreSettings = new List<string>();
        /// <summary>with Remove: also remove the mod's settings and favorites (normal removal keeps them)</summary>
        public bool RemoveSettings;
        public Dictionary<string, string> Files = new Dictionary<string, string>(StringComparer.Ordinal);
        /// <summary>a mod that writes a USB drive or SD card: the USB and SD backup of that storage the person chose on this PC</summary>
        public string StorageBackup;
        public bool Empty => Install.Count + Update.Count + Remove.Count + RestoreSettings.Count == 0;
        public JObj ToJson() => new JObj { { "install", Install.Cast<object>().ToList() }, { "update", Update.Cast<object>().ToList() }, { "remove", Remove.Cast<object>().ToList() }, { "restore_settings", RestoreSettings.Cast<object>().ToList() }, { "remove_settings", RemoveSettings }, { "storage_backup", StorageBackup } };
    }

    /// <summary>a community menu as a choice in CCS Nucleus's startup chooser (its registry option of kind "community"): the menu's hakchi
    /// package, its program as a console path and the label the chooser shows</summary>
    public sealed class CommunityMenuChoice
    {
        public string Id, RecordId, Name, Package, Program, Label;
        public string Label64 => Convert.ToBase64String(Encoding.ASCII.GetBytes(Label ?? ""));
        public JObj ToJson() => new JObj { { "id", Id }, { "entry", RecordId }, { "name", Name }, { "package", Package }, { "program", Program }, { "label", Label } };
    }

    public static class CommunityMenus
    {
        /// <summary>the ccsprep steps that add (or change) and remove a menu's choice in CCS Nucleus's list</summary>
        public static readonly string[] Steps = { "nmadd", "nmdrop" };
        /// <summary>the choice's id in CCS Nucleus's list, from the entry's stable id</summary>
        public static string ChoiceId(string recordId) { using (var h = SHA256.Create()) return "cm-" + Hex.Of(h.ComputeHash(Encoding.UTF8.GetBytes(recordId ?? ""))).Substring(0, 12); }
        /// <summary>the label the chooser shows: the entry's name in the characters the chooser draws (others become spaces), at most 60</summary>
        public static string LabelOf(string name)
        {
            var t = new string((name ?? "").Select(ch => ch < 128 && (char.IsLetterOrDigit(ch) || " .,'!&()+:-".IndexOf(ch) >= 0) ? ch : ' ').ToArray());
            t = Regex.Replace(t, " {2,}", " ").Trim(); if (t.Length > 60) t = t.Substring(0, 60).TrimEnd();
            return t.Length == 0 ? "Community Menu" : t;
        }
        public static string ProgramOf(string relative) => "/var/lib/hakchi/rootfs/" + relative;
        public static CommunityMenuChoice For(CommunityRecord r, CommunityVariant v) => new CommunityMenuChoice { Id = ChoiceId(r.Id), RecordId = r.Id, Name = r.Name, Package = v.Package, Program = ProgramOf(r.LaunchProgram), Label = LabelOf(r.Name) };
        public static string Label64Text(string b64) { try { return b64 == null ? null : Encoding.ASCII.GetString(Convert.FromBase64String(b64)); } catch (FormatException) { return null; } }
    }

    /// <summary>one entry's part in a change. op: install, update, switch (to the variant the new combination needs), remove, restore-settings</summary>
    public sealed class CommunityItem
    {
        public CommunityRecord Record; public CommunityVariant Variant; public string Op, OldPackage, Reason; public int Tier; public bool KeepSettings;
        public bool Places => Op == "install" || Op == "update" || Op == "switch";
        public bool Unlists => Op == "remove" || Op == "switch" || Op == "update" && Record.UpdateMethod == "uninstall-first";
        public JObj ToJson() => new JObj { { "id", Record.Id }, { "name", Record.Name }, { "version", Record.Version }, { "op", Op }, { "variant", Variant?.Variant }, { "package", Variant?.Package }, { "old_package", OldPackage },
            { "tier", Tier }, { "reason", Reason }, { "keep_settings", KeepSettings }, { "alpha", Record.Alpha } };
    }

    public sealed class CommunityResolution
    {
        public readonly List<CommunityItem> Items = new List<CommunityItem>();
        /// <summary>dependency and variant feedback in plain words ("… needs …, so … has been selected too.")</summary>
        public readonly List<string> Feedback = new List<string>(), UpstreamRemovals = new List<string>(), External = new List<string>();
        public string Refusal;
        public bool Ok => Refusal == null;
        public IEnumerable<CommunityItem> Placed => Items.Where(i => i.Places).OrderBy(i => i.Tier).ThenBy(i => i.Variant.Package, StringComparer.Ordinal);
        /// <summary>hakchi's removal list, in its order: a package that needs another is removed before it</summary>
        public List<string> Unlist = new List<string>();
    }

    /// <summary>a package Workbench installed on a console, as the PC's ledger keeps it</summary>
    public sealed class CommunityInstalled
    {
        public string Id, Name, Version, Variant, Package, Sha256, State, Check, PlanRecord, ChangedUtc;
        public Dictionary<string, object> Record;   // the record as it was listed
        public Dictionary<string, string> Payload = new Dictionary<string, string>(StringComparer.Ordinal);   // path -> SHA-256
        public string BackupPath, BackupSha256, BackupCompletedUtc;
        /// <summary>a mod that writes a USB drive or SD card: the USB and SD backup of that storage bound to the change, and the recovery set
        /// that names it with the console's backup</summary>
        public string StorageBackupPath, StorageBackupSha256, StorageBackupMade, StorageName, RecoverySetRecord;
        /// <summary>its choice in CCS Nucleus's startup chooser (a community menu), or null</summary>
        public string ChoiceId;
        public JObj ToJson() => new JObj { { "id", Id }, { "name", Name }, { "version", Version }, { "variant", Variant }, { "package", Package }, { "sha256", Sha256 }, { "state", State }, { "check", Check },
            { "plan_record", PlanRecord }, { "changed_utc", ChangedUtc }, { "record", CommunityCatalog.ToJObj(Record) }, { "chooser_choice", ChoiceId },
            { "payload", Payload.Select(kv => (object)new JObj { { "path", kv.Key }, { "sha256", kv.Value } }).ToList() },
            { "pre_install_backup", BackupPath == null ? null : new JObj { { "path", BackupPath }, { "sha256", BackupSha256 }, { "completed_utc", BackupCompletedUtc } } },
            { "pre_install_storage_backup", StorageBackupPath == null ? null : new JObj { { "path", StorageBackupPath }, { "sha256", StorageBackupSha256 }, { "made", StorageBackupMade }, { "storage", StorageName }, { "recovery_set", RecoverySetRecord } } } };
        public static CommunityInstalled From(Dictionary<string, object> d)
        {
            var c = new CommunityInstalled { Id = d.S("id"), Name = d.S("name"), Version = d.S("version"), Variant = d.S("variant"), Package = d.S("package"), Sha256 = d.S("sha256"), State = d.S("state"), Check = d.S("check"),
                PlanRecord = d.S("plan_record"), ChangedUtc = d.S("changed_utc"), Record = d.O("record"), ChoiceId = d.S("chooser_choice") };
            foreach (var p in (d.A("payload") ?? new List<object>()).OfType<Dictionary<string, object>>()) if (p.S("path") != null) c.Payload[p.S("path")] = p.S("sha256");
            var b = d.O("pre_install_backup"); if (b != null) { c.BackupPath = b.S("path"); c.BackupSha256 = b.S("sha256"); c.BackupCompletedUtc = b.S("completed_utc"); }
            var sb = d.O("pre_install_storage_backup"); if (sb != null) { c.StorageBackupPath = sb.S("path"); c.StorageBackupSha256 = sb.S("sha256"); c.StorageBackupMade = sb.S("made"); c.StorageName = sb.S("storage"); c.RecoverySetRecord = sb.S("recovery_set"); }
            return c;
        }
        /// <summary>the record it was installed from (kept after delisting)</summary>
        public CommunityRecord ParsedRecord() { try { return Record == null ? null : CommunityCatalog.ParseRecord(Record); } catch (CommunityCatalogException) { return null; } }
    }

    /// <summary>the PC's ledger of the community mods Workbench installed on one console: a file in the records folder, written atomically</summary>
    public static class CommunityLedger
    {
        public const string Waiting = "waiting-install", Installed = "installed", Removing = "waiting-removal", Incomplete = "incomplete";
        static string Safe(string tag) => Regex.Replace(tag ?? "unknown", "[^A-Za-z0-9-]", "-");
        public static string FileOf(Settings s, string consoleTag) => Path.Combine(s.RecordsFolder, "community-ledger-" + Safe(consoleTag) + ".json");
        public static List<CommunityInstalled> Load(Settings s, string consoleTag)
        {
            var f = FileOf(s, consoleTag); if (!File.Exists(f)) return new List<CommunityInstalled>();
            var d = Record.Load(f); if (d == null || d.S("schema") != "ccs-community-ledger/1") return new List<CommunityInstalled>();
            return (d.A("mods") ?? new List<object>()).OfType<Dictionary<string, object>>().Select(CommunityInstalled.From).Where(x => x.Id != null && x.Package != null).ToList();
        }
        /// <summary>every console's ledger in this records folder: console tag -> its mods (Installed and Updates without a console connected)</summary>
        public static SortedDictionary<string, List<CommunityInstalled>> All(Settings s)
        {
            var r = new SortedDictionary<string, List<CommunityInstalled>>(StringComparer.Ordinal);
            if (!Directory.Exists(s.RecordsFolder)) return r;
            foreach (var f in Directory.GetFiles(s.RecordsFolder, "community-ledger-*.json"))
            {
                var d = Record.Load(f); var tag = d?.S("console_tag"); if (tag == null || d.S("schema") != "ccs-community-ledger/1") continue;
                var mods = Load(s, tag); if (mods.Count > 0) r[tag] = mods;
            }
            return r;
        }
        public static void Save(Settings s, string consoleTag, IEnumerable<CommunityInstalled> mods)
        {
            Directory.CreateDirectory(s.RecordsFolder);
            Durable.WriteAtomic(FileOf(s, consoleTag), Json.Write(new JObj { { "schema", "ccs-community-ledger/1" }, { "console_tag", consoleTag }, { "updated_utc", Record.Now() }, { "mods", mods.Select(m => (object)m.ToJson()).ToList() } }));
        }
    }

    public static class CommunityResolver
    {
        /// <summary>the entries known for this console: the catalog's, and (for mods Workbench installed that were since delisted) the
        /// records the ledger kept</summary>
        public static Dictionary<string, CommunityRecord> Known(CommunityCatalog cat, IEnumerable<CommunityInstalled> ledger)
        {
            var k = new Dictionary<string, CommunityRecord>(StringComparer.Ordinal);
            foreach (var e in cat?.Entries ?? new List<CommunityRecord>()) k[e.Id] = e;
            foreach (var l in ledger ?? Enumerable.Empty<CommunityInstalled>()) if (!k.ContainsKey(l.Id)) { var r = l.ParsedRecord(); if (r != null) k[r.Id] = r; }
            return k;
        }

        /// <summary>which variant of a record hakchi has installed now (null: none)</summary>
        public static CommunityVariant InstalledVariant(CommunityRecord r, ICollection<string> packages) => r.Variants.FirstOrDefault(v => packages.Contains(v.Package));

        /// <summary>the variant to use: the first whose condition names an entry or package in the combination, else the default</summary>
        public static CommunityVariant VariantFor(CommunityRecord r, ICollection<string> ids, ICollection<string> packages) =>
            r.Variants.FirstOrDefault(v => !v.IsDefault && v.UseWhen.Any(w => ids.Contains(w) || packages.Contains(w))) ?? r.DefaultVariant;

        /// <summary>an approved newer pin for an installed mod: the catalog's current record lists the installed file's SHA-256 among the
        /// versions it updates</summary>
        public static bool UpdateAvailable(CommunityCatalog cat, CommunityInstalled m)
        {
            var r = cat?.Find(m.Id); if (r == null || m.Sha256 == null) return false;
            if (r.Variants.Any(v => v.Sha256 == m.Sha256)) return false;
            return r.UpdatesFrom.Any(u => u.sha256 == m.Sha256);
        }

        static string Names(IEnumerable<string> n) { var l = n.ToList(); return l.Count <= 1 ? string.Join("", l) : string.Join(", ", l.Take(l.Count - 1)) + " and " + l.Last(); }

        /// <summary>the change in one plan, or why it cannot be made (Refusal). packages: hakchi's packages on the console now.</summary>
        public static CommunityResolution Resolve(CommunityCatalog cat, IEnumerable<CommunityInstalled> ledger, ICollection<string> packages, CommunityChange ch)
        {
            var res = new CommunityResolution();
            CommunityResolution No(string why) { res.Refusal = why; return res; }
            var ledgerList = (ledger ?? Enumerable.Empty<CommunityInstalled>()).ToList();
            var known = Known(cat, ledgerList);
            var installed = known.Values.Where(r => InstalledVariant(r, packages) != null).ToDictionary(r => r.Id, r => r);
            string NameOf(string id) => known.TryGetValue(id, out var r) ? r.Name : id;
            var install = new List<string>(); var reasons = new Dictionary<string, string>();
            foreach (var id in ch.Install.Distinct())
            {
                var r = cat?.Find(id);
                if (r == null) return No(known.ContainsKey(id) ? NameOf(id) + " is no longer listed, so it cannot be installed now. A copy already on your console stays manageable." : "There is no listed mod " + id + ".");
                if (installed.ContainsKey(id)) return No(r.Name + " is already installed on this console.");
                install.Add(id);
            }
            foreach (var id in ch.Remove.Distinct()) if (!installed.ContainsKey(id)) return No((known.ContainsKey(id) ? NameOf(id) : id) + " is not installed on this console, so there is nothing to remove.");
            foreach (var id in ch.Update.Distinct())
            {
                if (!installed.ContainsKey(id)) return No(NameOf(id) + " is not installed on this console, so it cannot be updated.");
                var m = ledgerList.FirstOrDefault(x => x.Id == id);
                if (m == null || !UpdateAvailable(cat, m)) return No("No approved update of " + NameOf(id) + " is available. Updates are offered only when a newer version is approved.");
            }
            foreach (var id in ch.RestoreSettings.Distinct()) if (!installed.ContainsKey(id)) return No(NameOf(id) + " is not installed now: its kept settings are put back only while it is installed.");
            if (install.Intersect(ch.Remove).Any() || ch.Update.Intersect(ch.Remove).Any()) return No("A mod cannot be installed or updated and removed in the same change.");
            // dependencies: selected with their reason (only listed entries can be added)
            var removing = new HashSet<string>(ch.Remove);
            for (int i = 0; i < install.Count + ch.Update.Count; i++)
            {
                var id = i < install.Count ? install[i] : ch.Update[i - install.Count];
                var r = cat.Find(id);
                foreach (var d in r.Depends)
                {
                    if (removing.Contains(d)) return No(r.Name + " needs " + NameOf(d) + ". Keep " + NameOf(d) + ", or do not add " + r.Name + ".");
                    if (installed.ContainsKey(d) || install.Contains(d)) continue;
                    var dr = cat.Find(d);
                    if (dr == null) return No(r.Name + " needs " + NameOf(d) + ", which Workbench cannot install" + (known.ContainsKey(d) ? " (it is no longer listed)" : "") + ", so " + r.Name + " cannot be installed now.");
                    install.Add(d); reasons[d] = r.Name + " needs " + dr.Name + ", so " + dr.Name + " has been selected too.";
                    res.Feedback.Add(reasons[d]);
                }
            }
            // a base never goes while something staying needs it
            foreach (var id in ch.Remove)
            {
                var needs = installed.Values.Concat(install.Select(x => cat.Find(x))).Where(r => !removing.Contains(r.Id) && r.Depends.Contains(id)).Select(r => r.Name).Distinct().ToList();
                if (needs.Count > 0) return No(Names(needs) + (needs.Count == 1 ? " needs " : " need ") + NameOf(id) + ". Remove " + Names(needs) + " too, or keep " + NameOf(id) + ".");
            }
            // the combination afterwards: conflicts refused with the record's own words
            var after = installed.Keys.Where(k => !removing.Contains(k)).Concat(install).Distinct().ToList();
            foreach (var a in after) foreach (var b in after)
                {
                    if (string.CompareOrdinal(a, b) >= 0) continue;
                    var ra = known[a]; var rb = known[b];
                    var c = ra.Conflicts.FirstOrDefault(x => x.id == b); if (c.id == null) c = rb.Conflicts.FirstOrDefault(x => x.id == a);
                    if (c.id != null) return No(c.text + " (" + ra.Name + ", " + rb.Name + ")");
                }
            var afterPackages = new HashSet<string>(packages.Where(p => !ch.Remove.Any(id => known[id].Packages.Contains(p))), StringComparer.Ordinal);
            // each item, with the variant for the combination afterwards
            foreach (var id in install)
            {
                var r = cat.Find(id); var v = VariantFor(r, after, afterPackages);
                res.Items.Add(new CommunityItem { Record = r, Variant = v, Op = "install", Reason = reasons.TryGetValue(id, out var why) ? why : null });
                if (!v.IsDefault) res.Feedback.Add(r.Name + ": " + v.Reason);
            }
            foreach (var id in ch.Update.Distinct())
            {
                var r = cat.Find(id); var v = VariantFor(r, after, afterPackages); var old = InstalledVariant(installed[id], packages);
                res.Items.Add(new CommunityItem { Record = r, Variant = v, Op = "update", OldPackage = old.Package, KeepSettings = r.Settings.Count > 0 });
            }
            // an installed mod whose variant the new combination changes (Options Menu's compat version beside a RetroArch that replaces a library)
            foreach (var id in installed.Keys.Where(k => !removing.Contains(k) && !ch.Update.Contains(k)))
            {
                var r = installed[id]; var now = InstalledVariant(r, packages); var want = VariantFor(r, after, afterPackages);
                if (now.Package == want.Package) continue;
                if (cat?.Find(id) == null) return No(r.Name + " would need its " + want.Variant + " version for this combination, but it is no longer listed. Nothing was changed.");
                res.Items.Add(new CommunityItem { Record = r, Variant = want, Op = "switch", OldPackage = now.Package, KeepSettings = r.Settings.Count > 0, Reason = r.Name + " changes to its " + want.Variant + " version: " + (want.IsDefault ? "the combination no longer needs " + now.Variant + "." : want.Reason) });
                res.Feedback.Add(res.Items.Last().Reason);
            }
            foreach (var id in ch.Remove)
            {
                var r = installed[id];
                res.Items.Add(new CommunityItem { Record = r, Variant = InstalledVariant(r, packages), Op = "remove", OldPackage = InstalledVariant(r, packages).Package, KeepSettings = !ch.RemoveSettings && r.Settings.Count > 0 });
            }
            foreach (var id in ch.RestoreSettings.Distinct()) res.Items.Add(new CommunityItem { Record = installed[id], Variant = InstalledVariant(installed[id], packages), Op = "restore-settings" });
            // where each placed package waits: hakchi installs transfer/ (sorted) before transfer/hmod/; a package whose dependency arrives in
            // this change waits in hmod/; a chain deeper than that needs two starts
            foreach (var it in res.Items.Where(i => i.Places))
            {
                var deps = it.Record.Depends.Where(d => res.Items.Any(o => o.Places && o.Record.Id == d)).ToList();
                if (deps.Count == 0) continue;
                if (deps.Any(d => res.Items.First(o => o.Places && o.Record.Id == d).Record.Depends.Any(dd => res.Items.Any(o => o.Places && o.Record.Id == dd))))
                    return No(it.Record.Name + " needs a mod that itself needs another being added now. Install " + Names(deps.Select(NameOf)) + " first, start the console once, then install " + it.Record.Name + ".");
                it.Tier = 1;
            }
            // hakchi's removal list: dependents before what they need
            var un = res.Items.Where(i => i.Unlists).ToList();
            foreach (var it in un.OrderByDescending(i => un.Count(o => o.Record.Depends.Contains(i.Record.Id)) == 0 ? 1 : 0).ThenBy(i => i.Record.Id, StringComparer.Ordinal))
                if (!res.Unlist.Contains(it.OldPackage)) res.Unlist.Add(it.OldPackage);
            if (res.Unlist.Count > 8) return No("At most eight mods can be removed or replaced in one change.");
            // what the author's own install removes by itself (listed before confirmation)
            foreach (var it in res.Items.Where(i => i.Places))
            {
                var gone = it.Record.RemovesOnInstall.Where(x => packages.Contains(x.package)).Select(x => x.name).ToList();
                var all = it.Record.RemovesOnInstall.Select(x => x.name).ToList();
                if (all.Count > 0) res.UpstreamRemovals.Add("Installing " + it.Record.Name + " removes " + Names(all) + " if " + (all.Count == 1 ? "it is" : "they are") + " installed" + (gone.Count > 0 ? " (on this console now: " + Names(gone) + ")" : "") + ". Your current settings are in the backup made first.");
            }
            foreach (var it in res.Items.Where(i => i.Op != "restore-settings" && i.Record.WritesExternal)) res.External.AddRange(it.Record.ExternalLocations.Where(x => !res.External.Contains(x)));
            return res;
        }
    }

    public sealed class CommunityPlan
    {
        public string Hwid, ConsoleTag, Session, Digest, Nonce, Text, CatalogSha; public int CatalogGeneration; public DateTime ExpiresUtc;
        public CommunityChange Change; public CommunityResolution Resolution;
        public List<AdmittedPackage> Packages = new List<AdmittedPackage>();
        public MethodState Before; public Dictionary<string, CommunityPackageState> BeforeMods; public string BeforeModsDigest;
        public BoundArchive ProtectiveArchive;
        /// <summary>a mod that writes a USB drive or SD card: the USB and SD backup of that storage, bound</summary>
        public CommunityStorageBinding Storage;
        public string ProtectivePath => ProtectiveArchive?.Path;
        public string UnlistSha;
        /// <summary>Classic Cart Studio's add-ons this change places because a community menu needs the startup chooser (ccs-nucleus,
        /// ccs-nucleus-chooser), CCS Nucleus's start setting it writes ("DEFAULT always HOME SELECTION TIMEOUT") or null, and the menus' choices
        /// added (or changed) and removed in CCS Nucleus's list</summary>
        public List<string> Addons = new List<string>(); public string StartLine;
        /// <summary>CCS Nucleus or its chooser installed in an earlier version, which this build's packages are placed over (an earlier CCS Nucleus
        /// cannot read a community menu's choice)</summary>
        public List<string> Updated = new List<string>();
        public List<CommunityMenuChoice> MenusAdded = new List<CommunityMenuChoice>(), MenusDropped = new List<CommunityMenuChoice>();
        /// <summary>the heading and words of the review's notice when the change turns on the startup chooser (null otherwise)</summary>
        public string ChooserTitle, ChooserText;
        /// <summary>the steps in order: "method:PKG", "nstart", "ckeep:PKG", "cforget:PKG", "nmdrop:ID", "cunlist", "nmadd:ID", "cput:PKG", "crestore:PKG"</summary>
        public List<string> Steps = new List<string>(), Notes = new List<string>();
        public AdmittedPackage PackageFor(string pkg) => Packages.FirstOrDefault(p => p.Variant.Package == pkg);
        /// <summary>the item a step's package names: the package it places, or else the one it removes or replaces</summary>
        public CommunityItem ItemFor(string pkg) => Resolution.Items.FirstOrDefault(i => i.Variant?.Package == pkg) ?? Resolution.Items.FirstOrDefault(i => i.OldPackage == pkg);
        public CommunityMenuChoice MenuFor(string id) => MenusAdded.FirstOrDefault(m => m.Id == id) ?? MenusDropped.FirstOrDefault(m => m.Id == id);
        public JObj ToJson() => new JObj
        {
            { "operation", "community" }, { "console_tag", ConsoleTag }, { "text", Text }, { "change", Change?.ToJson() }, { "items", Resolution.Items.Select(i => (object)i.ToJson()).ToList() },
            { "feedback", Resolution.Feedback.Cast<object>().ToList() }, { "upstream_removals", Resolution.UpstreamRemovals.Cast<object>().ToList() }, { "steps", Steps }, { "notes", Notes },
            { "packages", Packages.Select(p => (object)p.ToJson()).ToList() }, { "removal_list", Resolution.Unlist.Count == 0 ? null : new JObj { { "names", string.Join(" ", Resolution.Unlist) }, { "sha256", UnlistSha } } },
            { "addons", Addons.Select(a => (object)new JObj { { "name", a }, { "label", Methods.Label(a) }, { "sha256", Methods.Sha256(a) } }).ToList() }, { "start", StartLine },
            { "updates", Updated.Select(a => (object)new JObj { { "name", a }, { "label", Methods.Label(a) }, { "sha256", Methods.Sha256(a) } }).ToList() },
            { "chooser", ChooserTitle == null ? null : new JObj { { "title", ChooserTitle }, { "text", ChooserText } } },
            { "menus_added", MenusAdded.Select(m => (object)m.ToJson()).ToList() }, { "menus_removed", MenusDropped.Select(m => (object)m.ToJson()).ToList() },
            { "storage", Storage?.ToJson() }, { "storage_confirm", Storage == null ? null : CommunityStorage.ConfirmText },
            { "catalog", new JObj { { "generation", CatalogGeneration }, { "sha256", CatalogSha } } }, { "console_state", Before?.ToJson() },
            { "mods_state", BeforeMods?.Values.Select(m => (object)m.Line).ToList() }, { "protective_backup", ProtectivePath }, { "evidence", new List<object> { ProtectiveArchive?.ToJson(), Storage?.ToJson() } },
            { "digest", Digest }, { "nonce", Nonce }, { "expires_utc", ExpiresUtc.ToString("o", CultureInfo.InvariantCulture) },
        };
    }

    public sealed class CommunityOutcome { public string State, Problem, RecordFile, RecoverySetRecord; public List<string> Done = new List<string>(); public CommunityPlan Plan; public bool Ok => State == "scheduled"; }

    /// <summary>what Workbench read from a console about its community mods (nothing written): the reconciled ledger and the catalog's
    /// entries with each one's state</summary>
    public sealed class CommunityReading
    {
        public string Refusal, ConsoleTag, Family, Region; public MethodState State; public List<CommunityInstalled> Ledger = new List<CommunityInstalled>();
        public Dictionary<string, CommunityPackageState> Mods = new Dictionary<string, CommunityPackageState>(StringComparer.Ordinal);
        public JObj ToJson(CommunityCatalog cat) => new JObj
        {
            { "event", "community-state" }, { "console_tag", ConsoleTag }, { "family", Family }, { "region", Region }, { "refusal", Refusal },
            { "packages", State?.Packages.Cast<object>().ToList() }, { "ledger", Ledger.Select(m => (object)m.ToJson().Set("update_available", CommunityResolver.UpdateAvailable(cat, m)).Set("listed", cat?.Find(m.Id) != null)).ToList() },
            { "mods", Mods.Values.Select(m => (object)new JObj { { "package", m.Package }, { "listed", m.Listed }, { "kept", m.KeptCount } }).ToList() },
        };
    }

    public static class CommunityEngine
    {
        public static readonly string[] Steps = { "ckeep", "cforget", "cunlist", "cput", "crestore", "method", "nstart", "nmadd", "nmdrop" };
        public const string PrivilegeNote = "A mod's own scripts run on the console with full control of it. Workbench checks that each file is exactly the approved one (its size and SHA-256 fingerprint); that proves it is the file that was reviewed, not that it is safe, and it does not limit what the mod does.";

        /// <summary>the console's family as the records name it (from its board)</summary>
        public static string FamilyOf(ConsoleIdentity id)
        {
            var b = (id?.Board ?? "").ToLowerInvariant();
            return b == "dp-nes" ? "nes-classic" : b == "dp-shvc" ? "snes-classic" : b == "dp-hvc" ? "famicom-mini" : b == "dp-shvcj" || b == "dp-sfc" ? "super-famicom-mini" : "unknown";
        }
        static bool RegionFits(CommunityRecord r, ConsoleIdentity id) => r.Regions.Count == 0 || (id?.Region ?? "").Split('_').Any(x => r.Regions.Contains(x));

        public static string StepLabel(string s, CommunityPlan p = null)
        {
            var i = s.IndexOf(':'); var k = i < 0 ? s : s.Substring(0, i); var pkg = i < 0 ? null : s.Substring(i + 1);
            if (k == "method" || k == "nstart") return MethodEngine.StepLabel(s);
            if (k == "nmadd" || k == "nmdrop")
            {
                var m = p?.MenuFor(pkg); var mn = m?.Name ?? pkg;
                return k == "nmadd" ? mn + " added to CCS Nucleus's startup chooser as a choice" : mn + "'s choice removed from CCS Nucleus's startup chooser";
            }
            var name = pkg == null || p == null ? pkg : p.ItemFor(pkg)?.Record.Name ?? pkg;
            switch (k)
            {
                case "ckeep": return name + "'s settings and favorites copied to Classic Cart Studio's own folder on the console";
                case "cforget": return "The settings Workbench kept earlier for " + name + " removed";
                case "cunlist": return "Removal list placed for hakchi (" + (p == null ? "the mods to remove" : string.Join(", ", p.Resolution.Unlist.Select(x => p.ItemFor(x)?.Record.Name ?? x))) + ")";
                case "cput": return name + "'s package placed for hakchi to install at the next start";
                case "crestore": return name + "'s kept settings and favorites put back";
            }
            return s;
        }

        static string ListSha(IEnumerable<string> names) { using (var h = SHA256.Create()) return Hex.Of(h.ComputeHash(Encoding.ASCII.GetBytes(string.Join(" ", names) + "\n"))); }
        static string ModsDigest(Dictionary<string, CommunityPackageState> m) { using (var h = SHA256.Create()) return Hex.Of(h.ComputeHash(Encoding.UTF8.GetBytes(string.Join("\n", (m ?? new Dictionary<string, CommunityPackageState>()).Values.OrderBy(x => x.Package, StringComparer.Ordinal).Select(x => x.Line))))); }

        /// <summary>the packages a reading or plan asks the console about: every known record's</summary>
        static List<string> AskAbout(Dictionary<string, CommunityRecord> known) => known.Values.SelectMany(r => r.Packages).Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).Take(32).ToList();

        /// <summary>Phase 1 only (no write, no backup): the console's mods, with the PC's ledger reconciled against what the console reads
        /// back (saved). Every Community Mods session starts with it.</summary>
        public static CommunityReading Read(RecoveryConsole con, EngineContext ctx, CommunityCatalog cat)
        {
            var rd = new CommunityReading();
            if (con.Profile != SessionProfile.Prepare) { rd.Refusal = "The console is not in a preparation session."; return rd; }
            ConsoleIdentity id;
            try { id = con.Identify(); Support.CheckForBackup(id, ctx.RecoveryImageId, "community"); }
            catch (ConsoleRefusedException ex) { rd.Refusal = ex.Message; return rd; }
            var dw = ConsoleData.Refusal(id); if (dw != null) { rd.Refusal = dw + " Nothing was changed."; return rd; }
            rd.ConsoleTag = id.ShortTag; rd.Family = FamilyOf(id); rd.Region = id.Region;
            var st = con.PrepState(); rd.State = st;
            if (!st.Ok) { rd.Refusal = "The console's hakchi packages could not be read (" + st.Problem + "). Nothing was changed."; return rd; }
            if (st.Hakchi != Preparation.PinnedHakchiVersion) { rd.Refusal = HakchiRequirement.Refusal(HakchiRequirement.Detected(st.Hakchi)); return rd; }
            rd.Ledger = CommunityLedger.Load(ctx.Settings, id.ShortTag);
            var known = CommunityResolver.Known(cat, rd.Ledger);
            var mods = con.CommunityState(AskAbout(known));
            if (mods == null) { rd.Refusal = "The console's community mods could not be read. Nothing was changed."; return rd; }
            rd.Mods = mods;
            Reconcile(con, st, mods, rd.Ledger);
            CommunityLedger.Save(ctx.Settings, id.ShortTag, rd.Ledger);
            return rd;
        }

        /// <summary>what each ledger entry really is now: still waiting for the next start, installed (its files read back and compared with
        /// the approved package's), removed, or not as planned (the next start did not install or remove it)</summary>
        internal static void Reconcile(RecoveryConsole con, MethodState st, Dictionary<string, CommunityPackageState> mods, List<CommunityInstalled> ledger)
        {
            foreach (var m in ledger.ToList())
            {
                bool listed = st.Installed(m.Package);
                bool waits = st.Waiting.Keys.Any(w => w == m.Package + ".hmod" || w == "hmod/" + m.Package + ".hmod");
                bool unlisted = st.Waiting.ContainsKey("uninstall");
                if (m.State == CommunityLedger.Waiting || m.State == CommunityLedger.Installed || m.State == CommunityLedger.Incomplete && listed)
                {
                    if (waits) { m.State = CommunityLedger.Waiting; m.Check = "Waits for the console's next normal start, when hakchi installs it."; continue; }
                    if (!listed) { m.State = CommunityLedger.Incomplete; m.Check = "The console started, but hakchi does not list " + m.Name + ": it was not installed. The backup made before is unchanged."; continue; }
                    var files = con.CommunityVerify(m.Package);
                    if (files == null) { m.State = CommunityLedger.Incomplete; m.Check = "hakchi lists " + m.Name + ", but its files could not be read back."; continue; }
                    var have = files.Where(f => f.path != "uninstall").ToDictionary(f => f.path, f => f.state, StringComparer.Ordinal);
                    int same = m.Payload.Count(p => have.TryGetValue(p.Key, out var s) && s == p.Value), missing = m.Payload.Count(p => !have.TryGetValue(p.Key, out var s) || s == "absent");
                    int changed = m.Payload.Count - same - missing;
                    m.State = missing == 0 ? CommunityLedger.Installed : CommunityLedger.Incomplete;
                    m.Check = (missing == 0 ? "Installed: " : "Installed only in part: ") + same.ToString("N0", CultureInfo.InvariantCulture) + " of " + m.Payload.Count.ToString("N0", CultureInfo.InvariantCulture) + " files are the approved ones"
                        + (changed > 0 ? "; " + changed.ToString("N0", CultureInfo.InvariantCulture) + " changed since (by the mod itself or later use)" : "") + (missing > 0 ? "; " + missing.ToString("N0", CultureInfo.InvariantCulture) + " missing" : "") + ".";
                }
                else if (m.State == CommunityLedger.Removing)
                {
                    if (!listed) { ledger.Remove(m); continue; }
                    if (unlisted) { m.Check = "Waits for the console's next normal start, when hakchi removes it."; continue; }
                    m.State = CommunityLedger.Incomplete; m.Check = "The console started, but hakchi still lists " + m.Name + ": it was not removed.";
                }
            }
        }

        /// <summary>Phases 1 to 3 (nothing written to the console; the protective backup made on the PC)</summary>
        public static (CommunityPlan plan, CommunityOutcome refused) Prepare(RecoveryConsole con, EngineContext ctx, CommunityCatalog cat, CommunityChange change)
        {
            var o = new CommunityOutcome();
            CommunityOutcome Refuse(string why) { o.State = "refused"; o.Problem = why; return o; }
            if (con.Profile != SessionProfile.Prepare) return (null, Refuse("The console is not in a preparation session."));
            if (change == null || change.Empty) return (null, Refuse("Choose the mods to install, update or remove."));
            if (cat == null) return (null, Refuse("The community catalog cannot be read, so nothing can be installed or updated."));
            var rd = Read(con, ctx, cat);
            if (rd.Refusal != null) return (null, Refuse(rd.Refusal));
            var st = rd.State;
            var res = CommunityResolver.Resolve(cat, rd.Ledger, st.Packages, change);
            if (!res.Ok) return (null, Refuse(res.Refusal + (res.Refusal.EndsWith("Nothing was changed.") ? "" : " Nothing was changed.")));
            var id = con.Identify();
            var p = new CommunityPlan { Hwid = id.Hwid, ConsoleTag = id.ShortTag, Session = con.SessionId, Change = change, Resolution = res, Before = st, BeforeMods = rd.Mods, BeforeModsDigest = ModsDigest(rd.Mods), CatalogSha = cat.Sha256, CatalogGeneration = cat.Generation };
            // each placed package: for this console, and exactly the approved file
            long need = 0;
            foreach (var it in res.Items.Where(i => i.Places))
            {
                var r = it.Record;
                if (!r.Consoles.Contains(rd.Family)) return (null, Refuse(r.Name + " is not for this console's model. Nothing was changed."));
                if (!RegionFits(r, id)) return (null, Refuse(r.Name + " is not for this console's region (" + id.Region + "). Nothing was changed."));
                change.Files.TryGetValue(r.Id, out var file);
                var (pkg, why) = CommunityAdmission.Admit(r, it.Variant, file);
                if (pkg == null) return (null, Refuse(file == null ? r.Name + " " + r.Version + ": " + CommunityAdmission.NoFileText : why));
                if (r.ChooserMenu && !pkg.Inventory.Payload.Any(m => m.Type == "file" && m.Path == r.LaunchProgram)) return (null, Refuse("The approved " + r.Name + " file does not hold the program its record names (" + r.LaunchProgram + "), so the startup chooser could not start it. Nothing was installed."));
                p.Packages.Add(pkg); need += it.Variant.Bytes + pkg.Inventory.UnpackedBytes;
            }
            if (p.Packages.Count > 0)
            {
                if (st.Free < 0) return (null, Refuse("The console's free space could not be read. Nothing was changed."));
                var room = StorageSpec.Account(new Dictionary<string, object>
                {
                    ["profile"] = id.Model?.Id ?? StorageSpec.NesModel, ["storage"] = new Dictionary<string, object> { ["source"] = "measured", ["available_bytes"] = st.Free },
                    ["items"] = new object[] { new Dictionary<string, object> { ["category"] = "mods", ["label"] = "the mods' packages and their unpacked files", ["estimated"] = true, ["files"] = new object[] { new Dictionary<string, object> { ["bytes"] = need } } } },
                });
                if (!(bool)room["write_ok"]) return (null, Refuse(Capacity.Why(room, out _, out _) + " Nothing was changed."));
            }
            // kept settings: never overwritten, never silently discarded (ckeep itself answers "ok already" for the same copy, as when an
            // interrupted change is finished, and refuses a different earlier copy before anything else is written)
            foreach (var it in res.Items)
            {
                rd.Mods.TryGetValue(it.OldPackage ?? it.Variant.Package, out var ms);
                if (it.Op == "restore-settings" && (ms == null || !ms.Kept)) return (null, Refuse("No settings kept for " + it.Record.Name + " are on the console. Nothing was changed."));
                if (it.KeepSettings && ms != null && ms.Kept) p.Notes.Add("Settings Workbench kept earlier for " + it.Record.Name + " are on the console. If they differ from its settings now, the change stops at that step and writes nothing more, so nothing kept is lost.");
            }
            // a community menu the startup chooser starts is a choice there: its choice added (or changed to the new package or program)
            // with its package; a removed menu's choice taken out. The chooser itself comes in the same change when it is not on the console
            var menusIn = res.Items.Where(i => i.Places && i.Record.ChooserMenu).ToList();
            foreach (var it in menusIn)
            {
                var mc = CommunityMenus.For(it.Record, it.Variant);
                if (st.Options.TryGetValue(mc.Id, out var have) && have.kind != "community") return (null, Refuse("Another choice in CCS Nucleus already has the id " + mc.Id + ", so " + it.Record.Name + " cannot be added to its startup chooser. Nothing was changed."));
                bool same = have.kind == "community" && st.Menus.TryGetValue(mc.Id, out var m0) && m0.package == mc.Package && m0.program == mc.Program && m0.label64 == mc.Label64;
                if (!same) p.MenusAdded.Add(mc);
            }
            foreach (var it in res.Items.Where(i => i.Op == "remove" && i.Record.IsMenu))
            {
                var cid = CommunityMenus.ChoiceId(it.Record.Id);
                if (st.Menus.TryGetValue(cid, out var m1)) p.MenusDropped.Add(new CommunityMenuChoice { Id = cid, RecordId = it.Record.Id, Name = it.Record.Name, Package = m1.package, Program = m1.program, Label = CommunityMenus.Label64Text(m1.label64) });
            }
            if (menusIn.Count > 0)
            {
                bool nucleus = st.Installed(Methods.Nucleus), chooser = st.Installed(Methods.Chooser);
                if (st.Registry == "invalid" || st.Start == "invalid") return (null, Refuse("CCS Nucleus's list of choices on this console cannot be read, so a menu cannot be added to its startup chooser. Nothing was changed."));
                if (nucleus && st.NucleusProgram == null || chooser && st.AddonVersion(Methods.Chooser) == null) return (null, Refuse("Workbench cannot tell which version of CCS Nucleus this console has, so it does not add a menu to its startup chooser. Nothing was changed."));
                if (!nucleus) p.Addons.Add(Methods.Nucleus); else if (st.NucleusProgram == "earlier") p.Updated.Add(Methods.Nucleus);
                if (!chooser) p.Addons.Add(Methods.Chooser); else if (st.AddonVersion(Methods.Chooser) == "earlier") p.Updated.Add(Methods.Chooser);
                foreach (var a in p.Addons.Concat(p.Updated)) if (!Hex.IsHex(Methods.Sha256(a), 64)) return (null, Refuse("This build does not carry " + Methods.Label(a) + "'s package. Nothing was changed."));
                var names = string.Join(" and ", menusIn.Select(i => i.Record.Name));
                if (!chooser)
                {
                    // the chooser shown at start; what opens when nothing is chosen stays as it is (the console's own menu where CCS Nucleus arrives now)
                    var sp = st.StartParts;
                    if (sp == null && st.Start != "none") return (null, Refuse("CCS Nucleus's start setting cannot be read on this console. Nothing was changed."));
                    var line = sp == null ? "native always " + (st.Installed(Methods.Mod) ? "on" : "off") + " default " + AddonChoice.Countdown.ToString(CultureInfo.InvariantCulture) : sp[0] + " always " + sp[2] + " " + sp[3] + " " + sp[4];
                    if (st.Start != line) p.StartLine = line;
                    var after = line.Split(' ');
                    // what opens first and the countdown stay exactly as the console has them (also a setting its own screen made)
                    var dflt = after[0] == "ccs-home" ? "CCS HOME" : after[0] == "native" ? "Original Menu" : st.OptionName(after[0]) ?? "the menu set on the console";
                    var secs = AddonChoice.SecondsText(int.Parse(after[4], CultureInfo.InvariantCulture));
                    p.ChooserTitle = "This addition uses the startup chooser";
                    p.ChooserText = "Adding " + names + " will turn on CCS Nucleus's startup chooser and add the parts it needs. Your games, saves and other mods stay.";
                    p.Notes.Add("Added in this change so that " + names + " can be chosen: " + string.Join(" and ", p.Addons.Select(Methods.Label)) + ". hakchi installs " + (p.Addons.Count == 1 ? "it" : "them") + " at the console's next start, with the mod.");
                    p.Notes.Add("At power-on the startup chooser appears first. " + (after[4] == "0" ? (after[3] == "last" ? "Nothing starts until you choose; the menu you used last is selected first." : "Nothing starts until you choose.")
                        : after[3] == "last" ? "If you do nothing, the menu you used last starts in " + secs + "." : "If you do nothing, " + dflt + " starts in " + secs + ", as it opens now.")
                        + " You can change this in the chooser's Recovery and Settings, or in Console Restore: Add-ons and Startup.");
                }
                else if (st.StartParts != null && st.StartParts[1] == "never")
                    p.Notes.Add("The startup chooser is set not to appear at power-on (your choice, kept). " + names + (menusIn.Count == 1 ? " is" : " are") + " in it: open it from CCS Nucleus in Original Menu, or with Choose Another Menu in CCS HOME.");
                if (p.Updated.Count > 0)
                    p.Notes.Add(string.Join(" and ", p.Updated.Select(Methods.Label)) + " on this console " + (p.Updated.Count == 1 ? "is" : "are") + " an earlier version, which cannot list a community menu, so this change updates " + (p.Updated.Count == 1 ? "it" : "them") + ". " + MethodEngine.UpdateText(p.Updated));
                p.Notes.Add(names + (menusIn.Count == 1 ? " becomes a choice" : " become choices") + " in the startup chooser. " + string.Join(" ", menusIn.Select(i => i.Record.Name + ": " + i.Record.LaunchReturn)) + " Leaving it opens the startup chooser again, to choose another menu.");
            }
            if (p.MenusDropped.Count > 0)
                p.Notes.Add("Removing " + string.Join(" and ", p.MenusDropped.Select(m => m.Name)) + " also removes " + (p.MenusDropped.Count == 1 ? "its choice" : "their choices") + " from the startup chooser. The startup chooser itself stays; removing it is in Console Restore: Add-ons and Startup.");
            // the steps: the add-ons the chooser needs and its start, keep, forget, the removed menus' choices, one removal list, the added
            // menus' choices, place (dependencies first), put back
            foreach (var a in Methods.Packages.Where(x => p.Addons.Contains(x) || p.Updated.Contains(x))) p.Steps.Add("method:" + a);
            if (p.StartLine != null) p.Steps.Add("nstart");
            foreach (var it in res.Items.Where(i => i.KeepSettings)) p.Steps.Add("ckeep:" + (it.OldPackage ?? it.Variant.Package));
            if (change.RemoveSettings) foreach (var it in res.Items.Where(i => i.Op == "remove")) if (rd.Mods.TryGetValue(it.OldPackage, out var ms) && ms.Kept) p.Steps.Add("cforget:" + it.OldPackage);
            foreach (var m in p.MenusDropped) p.Steps.Add("nmdrop:" + m.Id);
            if (res.Unlist.Count > 0) { p.UnlistSha = ListSha(res.Unlist); p.Steps.Add("cunlist"); }
            foreach (var m in p.MenusAdded) p.Steps.Add("nmadd:" + m.Id);
            foreach (var it in res.Placed) p.Steps.Add("cput:" + it.Variant.Package);
            foreach (var it in res.Items.Where(i => i.Op == "restore-settings")) p.Steps.Add("crestore:" + it.Variant.Package);
            // what already waits for the next start may be only this plan's own files (an interrupted earlier run is finished, never written twice)
            var wp = MethodEngine.WaitingProblem(st, Mine(p));
            if (wp != null) return (null, Refuse("Changing the community mods is refused: " + wp + ". Start the console normally once so hakchi applies it, then check again. Nothing was changed."));
            p.Text = string.Join(" ", new[] {
                p.Addons.Count > 0 ? "Add " + string.Join(" and ", p.Addons.Select(Methods.Label)) + ", so the startup chooser can start " + string.Join(" and ", menusIn.Select(i => i.Record.Name)) + "." : null,
                p.Updated.Count > 0 ? "Update " + string.Join(" and ", p.Updated.Select(Methods.Label)) + " to this version, so the startup chooser can start " + string.Join(" and ", menusIn.Select(i => i.Record.Name)) + "." : null,
                res.Items.Any(i => i.Op == "install") ? "Install " + string.Join(", ", res.Items.Where(i => i.Op == "install").Select(i => i.Record.Name + " " + i.Record.Version)) + "." : null,
                res.Items.Any(i => i.Op == "update") ? "Update " + string.Join(", ", res.Items.Where(i => i.Op == "update").Select(i => i.Record.Name + " to " + i.Record.Version)) + "." : null,
                res.Items.Any(i => i.Op == "switch") ? "Change " + string.Join(", ", res.Items.Where(i => i.Op == "switch").Select(i => i.Record.Name + " to its " + i.Variant.Variant + " version")) + "." : null,
                res.Items.Any(i => i.Op == "remove") ? "Remove " + string.Join(", ", res.Items.Where(i => i.Op == "remove").Select(i => i.Record.Name)) + (change.RemoveSettings ? ", with their settings and favorites." : ". Their settings and favorites are kept.") : null,
                res.Items.Any(i => i.Op == "restore-settings") ? "Put back the kept settings and favorites of " + string.Join(", ", res.Items.Where(i => i.Op == "restore-settings").Select(i => i.Record.Name)) + "." : null,
                res.Items.Any(i => i.Places || i.Unlists) || p.Addons.Count + p.Updated.Count > 0 ? "hakchi applies this at the console's next normal start." : null }.Where(x => x != null));
            // phase 2: a mod that writes a USB drive or SD card: the USB and SD backup of that storage the person chose, checked and bound;
            // then the console backed up in this session
            if (res.External.Count > 0)
            {
                ctx.Say("protect", "Checking the USB and SD backup you chose...");
                var (sb, why) = CommunityStorage.Bind(change.StorageBackup, res.External, ctx.Cancelled);
                if (sb == null) return (null, Refuse(why));
                p.Storage = sb;
            }
            ctx.Say("protect", "Backing up the console as it is now, before anything changes...");
            var prot = CaptureEngine.Run(con, ctx);
            if (!prot.Ok || prot.Verification == null || !prot.Verification.Ok) { o.State = "stopped"; o.Problem = "The backup of the console as it is now did not complete (" + (prot.Problem ?? prot.State) + "), so nothing was written."; return (null, o); }
            if (!ConsoleIdentity.SameConsole(prot.Verification.Hwid, id.Hwid) || prot.Verification.Simulated != con.Simulated || !prot.Verification.RestoreReady) return (null, Refuse("The backup made just now is not a complete backup of this console, so nothing was written."));
            p.ProtectiveArchive = BoundArchive.From("backup made just now, before changing the community mods", prot.PrimaryPath, prot.Verification, con.SessionId);
            foreach (var s in res.Feedback) p.Notes.Add(s);
            foreach (var s in res.UpstreamRemovals) p.Notes.Add(s);
            foreach (var it in res.Items.Where(i => i.Places && i.Record.Alpha)) p.Notes.Add(it.Record.Name + " is an Alpha release." + (it.Record.Warnings.Count > 0 ? " Its author says: " + string.Join(" ", it.Record.Warnings) : ""));
            foreach (var it in res.Items.Where(i => i.Op == "remove" && !i.KeepSettings && i.Record.Settings.Count > 0)) p.Notes.Add(it.Record.Name + "'s settings and favorites are removed with it (you chose that). They are also in the backup made just now.");
            foreach (var it in res.Items.Where(i => i.KeepSettings)) p.Notes.Add(it.Record.Name + "'s own removal deletes its settings and favorites, so Workbench first copies them to its own folder on the console. They can be put back once " + it.Record.Name + " is installed again.");
            if (p.Storage != null)
                p.Notes.Add(string.Join(" ", res.Items.Where(i => i.Op != "restore-settings" && i.Record.WritesExternal).Select(i => i.Record.Name + ": " + i.Record.ExternalText).Distinct())
                    + " Workbench does not write that storage; the mod does, on the console. Its USB and SD backup and the backup of the console made just now are kept together as this change's recovery set: Console Restore puts the console back, and USB and SD Backup's Restore puts the USB drive or SD card back.");
            p.Notes.Add("Only files on the console's data storage are written: hakchi's transfer folder" + (p.Steps.Any(s => s.StartsWith("ckeep") || s.StartsWith("cforget") || s.StartsWith("crestore")) ? " and Classic Cart Studio's own folder" : "")
                + (p.Steps.Any(s => s.StartsWith("crestore")) ? ", and the kept settings' own places" : "") + (p.StartLine != null || p.MenusAdded.Count + p.MenusDropped.Count > 0 ? ", and CCS Nucleus's list of choices" : "") + ". No kernel, bootloader or system is changed by Workbench.");
            p.Notes.Add("Games, saves, other mods, system images and CCS Mod and CCS Nucleus are kept" + (p.Addons.Count + p.Updated.Count > 0 ? " (the parts of CCS Nucleus named above are added or updated)" : "") + ". A mod's own install or removal can change files of its own; its record lists them.");
            p.Notes.Add(PrivilegeNote);
            p.Notes.Add("The backup just made" + (p.Storage != null ? ", the USB and SD backup" : "") + " and the package files are checked again when you confirm and stay locked until the change ends; if anything is missing or changed, nothing is written.");
            p.Nonce = Hex.Of(Guid.NewGuid().ToByteArray()); p.ExpiresUtc = DateTime.UtcNow + RestoreEngine.ConfirmWindow;
            p.Digest = DigestOf(p);
            return (p, null);
        }

        /// <summary>what this plan puts in hakchi's transfer folder: transfer-folder name -> SHA-256</summary>
        static Dictionary<string, string> Mine(CommunityPlan p)
        {
            var mine = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var a in p.Addons.Concat(p.Updated)) mine[Methods.Waiting(a)] = Methods.Sha256(a);
            foreach (var it in p.Resolution.Placed) mine[(it.Tier == 1 ? "hmod/" : "") + it.Variant.Package + ".hmod"] = it.Variant.Sha256;
            if (p.UnlistSha != null) mine["uninstall"] = p.UnlistSha;
            return mine;
        }

        static string DigestOf(CommunityPlan p)
        {
            using (var h = SHA256.Create())
                return Hex.Of(h.ComputeHash(Encoding.UTF8.GetBytes("ccs-community-plan/2\n" + ConsoleIdentity.NormalizeHwid(p.Hwid) + "\n" + p.Session + "\n" + Json.Write(p.Change.ToJson())
                    + string.Join(",", p.Resolution.Items.Select(i => i.Op + " " + i.Record.Id + " " + i.Variant?.Package + " " + i.Variant?.Sha256 + " " + i.OldPackage + " " + i.Tier + " " + i.KeepSettings)) + "\n"
                    + string.Join(",", p.Packages.Select(x => x.Variant.Package + " " + x.Variant.Sha256 + " " + x.File + " " + x.Inventory.PayloadDigest())) + "\n" + string.Join(" ", p.Resolution.Unlist) + " " + p.UnlistSha + "\n"
                    + string.Join(",", p.Addons.Select(a => a + " " + Methods.Sha256(a))) + "|" + string.Join(",", p.Updated.Select(a => a + " " + Methods.Sha256(a))) + "\n" + p.StartLine + "\n"
                    + string.Join(",", p.MenusAdded.Select(m => "+" + m.Id + " " + m.Package + " " + m.Program + " " + m.Label64)) + string.Join(",", p.MenusDropped.Select(m => "-" + m.Id + " " + m.Package)) + "\n"
                    + p.Before?.Digest() + "\n" + p.BeforeModsDigest + "\n" + p.CatalogSha + "\n" + p.ProtectiveArchive?.DigestLine() + "\n" + p.Storage?.DigestLine() + "\n" + string.Join(",", p.Steps) + "\n" + p.Nonce + "\n" + p.ExpiresUtc.Ticks)));
        }

        /// <summary>Phases 4 to 6: only with the plan's own digest and nonce, in its session, before it expires, once. Every check that can
        /// refuse runs before the confirmation is used up and before the first write.</summary>
        public static CommunityOutcome Execute(RecoveryConsole con, CommunityPlan plan, string digest, string nonce, EngineContext ctx) => Execute(con, plan, digest, nonce, ctx, false);

        /// <summary>storageConfirmed: the person confirmed that the USB and SD backup in the plan is of the storage the console uses (required
        /// when the plan has one: Workbench cannot check it through the console)</summary>
        public static CommunityOutcome Execute(RecoveryConsole con, CommunityPlan plan, string digest, string nonce, EngineContext ctx, bool storageConfirmed)
        {
            var o = new CommunityOutcome { Plan = plan };
            CommunityOutcome Refuse(string why) { o.State = "refused"; o.Problem = why; return o; }
            if (plan == null) return Refuse("No plan.");
            if (digest != plan.Digest || nonce != plan.Nonce || plan.Nonce == null || DigestOf(plan) != plan.Digest) return Refuse("The confirmation does not match the reviewed change.");
            if (DateTime.UtcNow > plan.ExpiresUtc) return Refuse("The confirmation is more than 10 minutes old. Review the change again.");
            if (con.SessionId != plan.Session || plan.ProtectiveArchive?.Session != plan.Session) return Refuse("The console was connected again after the review. Review the change again. Nothing was changed.");
            if (plan.Storage != null && !storageConfirmed) return Refuse(CommunityStorage.NeedConfirm);
            var id = con.Identify();
            if (!ConsoleIdentity.SameConsole(id.Hwid, plan.Hwid)) return Refuse("A different console is connected now. Nothing was changed.");
            using (var hold = Evidence.Hold.Open(new[] { plan.ProtectivePath, plan.Storage?.Path }.Concat(plan.Packages.Select(x => x.File)).ToArray()))
            {
                if (hold.Problem != null) return Refuse("A file the change relies on cannot be used (" + hold.Problem + "). Nothing was changed; review the change again.");
                ctx.Say("validate", "Checking the backup" + (plan.Storage != null ? ", the USB and SD backup" : "") + " and the package files again...");
                var why = Evidence.Recheck(plan.ProtectiveArchive, plan.Hwid, con.Simulated, con.SessionId, ctx.Cancelled, out var cv);
                if (why != null) return Refuse(why + " Nothing was changed; review the change again.");
                StorageVerification sv = null;
                if (plan.Storage != null)
                {
                    var sw = CommunityStorage.Recheck(plan.Storage, ctx.Cancelled, out sv);
                    if (sw != null) return Refuse(sw + " Nothing was changed; review the change again.");
                }
                foreach (var pk in plan.Packages) { var bad = CommunityAdmission.Recheck(pk); if (bad != null) return Refuse(pk.Record.Name + ": " + bad + " Nothing was changed; review the change again."); }
                var now = con.PrepState();
                if (!now.Ok || now.Digest() != plan.Before.Digest()) return Refuse("The console's hakchi packages, CCS Nucleus's settings or its transfer folder changed since the review. Nothing was changed; review the change again.");
                var mods = con.CommunityState(plan.BeforeMods.Keys);
                if (mods == null || ModsDigest(mods) != plan.BeforeModsDigest) return Refuse("The console's community mods changed since the review. Nothing was changed; review the change again.");
                // the console's backup and the USB and SD backup named together as this change's recovery set, before the first write
                RecoverySetResult set = null;
                if (plan.Storage != null)
                {
                    set = RecoverySet.Make(ctx.Settings, plan.ProtectivePath, cv, plan.Storage.Path, sv, "before changing the community mods on console " + plan.ConsoleTag + " (" + string.Join(", ", plan.Resolution.Items.Where(i => i.Record.WritesExternal).Select(i => i.Record.Name).Distinct()) + ")");
                    if (!set.Ok) return Refuse("The recovery set of the two backups could not be made (" + set.Problem + "), so nothing was written. Nothing was changed; review the change again.");
                    o.RecoverySetRecord = set.RecordFile;
                }
                Record rec;
                try { rec = Record.Start(ctx.Settings.RecordsFolder, Guid.NewGuid().ToString("N").Substring(0, 12), "community", new JObj { { "plan", plan.ToJson() }, { "console_tag", plan.ConsoleTag }, { "simulated", con.Simulated },
                    { "storage_confirmed", plan.Storage == null ? null : (object)CommunityStorage.ConfirmText }, { "recovery_set", set?.RecordFile }, { "recovery_set_text", set?.ConsistencyText } }); }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { return Refuse("The change's record could not be saved in " + ctx.Settings.RecordsFolder + " (" + ex.Message + "), so nothing was written."); }
                plan.Nonce = null;   // used up only now
                o.RecordFile = rec.File; var phases = new List<object>();
                bool addons = plan.Addons.Count + plan.Updated.Count > 0 || plan.StartLine != null;
                var auth = new WriteAuthority(plan.Digest, plan.Hwid, plan.Steps.Select(s => s.Split(':')[0]).Distinct().ToArray(), () => con.Simulated || Gate.CommunityWrites && (!addons || Gate.MethodWrites), Gate.CommunityText);
                rec.State("writing"); con.Card("writing");
                var kept = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var s in plan.Steps)
                {
                    if (ctx.Cancelled()) { rec.State("stopped", "canceled between steps"); o.State = "stopped"; o.Problem = Failure(plan, "Canceled. Steps done: " + Done(o, plan) + ".", "Nothing more was written. Review the change again to finish it: what was already done is not done twice."); return o; }
                    ctx.Say("community", StepLabel(s, plan) + "... (do not unplug the console)");
                    phases.Add(new JObj { { "step", s }, { "state", "began" }, { "at", Record.Now() } }); rec.Set("phases", phases).Save();
                    var k = s.Split(':')[0]; var pkgName = s.Contains(":") ? s.Substring(s.IndexOf(':') + 1) : null;
                    WriteResult r; bool ok;
                    try
                    {
                        if (k == "cput")
                        {
                            var pk = plan.PackageFor(pkgName); var it = plan.ItemFor(pkgName);
                            using (var f = new FileStream(pk.File, FileMode.Open, FileAccess.Read, FileShare.Read))
                                r = con.CommunityStep("cput", auth, new[] { pkgName, it.Tier.ToString(CultureInfo.InvariantCulture), pk.Variant.Bytes.ToString(CultureInfo.InvariantCulture), pk.Variant.Sha256, Math.Max(1, pk.Inventory.UnpackedBytes).ToString(CultureInfo.InvariantCulture) }, f, pk.Variant.Bytes);
                            ok = r.Ok && Regex.IsMatch(r.Line ?? "", "^result cput ok( already)? " + pk.Variant.Sha256 + "$");
                        }
                        else if (k == "cunlist") { r = con.CommunityStep("cunlist", auth, plan.Resolution.Unlist.ToArray()); ok = r.Ok && Regex.IsMatch(r.Line ?? "", "^result cunlist ok( already)? " + plan.UnlistSha + "$"); }
                        else if (k == "ckeep")
                        {
                            var it = plan.ItemFor(pkgName);
                            r = con.CommunityStep("ckeep", auth, new[] { pkgName }.Concat(it.Record.Settings.Select(x => x.Path)).ToArray());
                            var m = Regex.Match(r.Line ?? "", "^result ckeep ok(?: already)? (none|[0-9a-f]{64}) ([0-9]{1,4})$"); ok = r.Ok && m.Success;
                            if (ok) kept[pkgName] = m.Groups[1].Value;
                        }
                        else if (k == "method") { r = con.PrepStep("method", auth, new[] { pkgName, Methods.Sha256(pkgName) }); ok = MethodEngine.Answer(r, "method", Methods.Sha256(pkgName)); }
                        else if (k == "nstart") { r = con.PrepStep("nstart", auth, plan.StartLine.Split(' ')); ok = MethodEngine.StartAnswer(r, plan.StartLine); }
                        else if (k == "nmadd") { var m = plan.MenuFor(pkgName); r = con.PrepStep("nmadd", auth, new[] { m.Id, m.Package, m.Program, m.Label64 }); ok = r.Ok && Regex.IsMatch(r.Line ?? "", "^result nmadd ok( already)? " + Regex.Escape(m.Id) + "$"); }
                        else if (k == "nmdrop") { var m = plan.MenuFor(pkgName); r = con.PrepStep("nmdrop", auth, new[] { m.Id, m.Package }); ok = r.Ok && Regex.IsMatch(r.Line ?? "", "^result nmdrop ok( already)? " + Regex.Escape(m.Id) + "$"); }
                        else { r = con.CommunityStep(k, auth, new[] { pkgName }); ok = r.Ok; }
                    }
                    catch (GateClosedException ex) { rec.State("refused", ex.Message); o.State = "refused"; o.Problem = ex.Message; return o; }
                    phases.Add(new JObj { { "step", s }, { "state", ok ? ((r.Line ?? "").Contains(" ok already") ? "already-done" : "done-and-read-back") : "failed" }, { "detail", r.Line ?? r.Problem }, { "at", Record.Now() } }); rec.Set("phases", phases).Save();
                    ctx.At("community-" + k);
                    if (!ok)
                    {
                        auth.Revoke(s + ": " + (r.Problem ?? r.Line)); rec.State("stopped", r.Problem ?? r.Line); con.Card("stopped"); o.State = "stopped";
                        o.Problem = Failure(plan, "It stopped at: " + StepLabel(s, plan) + " (" + (r.Problem ?? r.Line) + "). Steps done before: " + Done(o, plan) + ". Nothing more was written and nothing was retried; the console starts as before.",
                            "Connect again and review the change to finish it, or restore the backup made just now.");
                        return o;
                    }
                    o.Done.Add(s);
                }
                // verify: what waits is exactly this plan's own; CCS Nucleus's start and the menus' choices read back; kept copies read back
                var after = con.PrepState();
                var want = Mine(plan);
                bool waits = after.Ok && want.All(w => after.Waiting.TryGetValue(w.Key, out var sha) && sha == w.Value) && MethodEngine.WaitingProblem(after, want) == null;
                bool chooserOk = after.Ok && (plan.StartLine == null || after.Start == plan.StartLine)
                    && plan.MenusAdded.All(m => after.Options.TryGetValue(m.Id, out var x) && x.kind == "community" && after.Menus.TryGetValue(m.Id, out var y) && y.package == m.Package && y.program == m.Program && y.label64 == m.Label64)
                    && plan.MenusDropped.All(m => !after.Options.ContainsKey(m.Id));
                var modsAfter = con.CommunityState(plan.BeforeMods.Keys);
                bool keptOk = modsAfter != null && kept.All(kv => kv.Value == "none" ? !modsAfter[kv.Key].Kept : modsAfter[kv.Key].KeptSha == kv.Value)
                              && plan.Steps.Where(x => x.StartsWith("cforget:")).All(x => !modsAfter[x.Substring(8)].Kept);
                if (!waits || !chooserOk || !keptOk)
                {
                    rec.State("stopped", "the console does not read back as planned"); o.State = "stopped";
                    o.Problem = Failure(plan, "Every step reported success, but the console does not read back as planned" + (!waits ? " (hakchi's transfer folder)" : !chooserOk ? " (CCS Nucleus's startup chooser)" : " (the kept settings)") + ". It was not changed again.", "Check the console again before its next start, or restore the backup made just now.");
                    return o;
                }
                rec.Set("after", new JObj { { "console_state", after.ToJson() }, { "mods_state", modsAfter.Values.Select(m => (object)m.Line).ToList() }, { "applies", "at the console's next normal start (hakchi's p7080_hmods)" } });
                rec.State("scheduled"); con.Card("done"); o.State = "scheduled";
                WriteLedger(ctx.Settings, plan, rec.File, set?.RecordFile);
                return o;
            }
        }

        /// <summary>the failure copy: "The mod could not be installed. [What is now true.] [Safe next action.]"</summary>
        public static string Failure(CommunityPlan p, string now, string next)
        {
            var ops = p?.Resolution.Items.Select(i => i.Op).ToList() ?? new List<string>();
            var what = ops.Any(o => o == "install") ? "installed" : ops.Any(o => o == "update" || o == "switch") ? "updated" : ops.Any(o => o == "remove") ? "removed" : "changed";
            return (ops.Count(o => o != "restore-settings") > 1 ? "The mods could not be " : "The mod could not be ") + what + ". " + now + " " + next;
        }
        static string Done(CommunityOutcome o, CommunityPlan p) => o.Done.Count == 0 ? "none" : string.Join("; ", o.Done.Select(x => StepLabel(x, p)));

        /// <summary>the ledger after a scheduled change: placed packages wait for the next start (with their payload's files and the backup
        /// made before); removed ones wait for their removal</summary>
        static void WriteLedger(Settings s, CommunityPlan p, string recordFile, string recoverySet)
        {
            var mods = CommunityLedger.Load(s, p.ConsoleTag);
            foreach (var it in p.Resolution.Items)
            {
                if (it.Op == "restore-settings") continue;
                if (it.Op == "remove")
                {
                    var m = mods.FirstOrDefault(x => x.Id == it.Record.Id);
                    if (m == null) { m = new CommunityInstalled { Id = it.Record.Id, Name = it.Record.Name, Version = it.Record.Version, Variant = it.Variant?.Variant, Package = it.OldPackage, Record = it.Record.Raw }; mods.Add(m); }
                    m.State = CommunityLedger.Removing; m.Check = "Waits for the console's next normal start, when hakchi removes it."; m.PlanRecord = recordFile; m.ChangedUtc = Record.Now();
                    if (p.Storage != null && it.Record.WritesExternal) { m.StorageBackupPath = p.Storage.Path; m.StorageBackupSha256 = p.Storage.Sha256; m.StorageBackupMade = p.Storage.When; m.StorageName = p.Storage.Display; m.RecoverySetRecord = recoverySet; }
                    continue;
                }
                var pk = p.PackageFor(it.Variant.Package);
                mods.RemoveAll(x => x.Id == it.Record.Id);
                var n = new CommunityInstalled
                {
                    Id = it.Record.Id, Name = it.Record.Name, Version = it.Record.Version, Variant = it.Variant.Variant, Package = it.Variant.Package, Sha256 = it.Variant.Sha256, Record = it.Record.Raw,
                    State = CommunityLedger.Waiting, Check = "Waits for the console's next normal start, when hakchi installs it.", PlanRecord = recordFile, ChangedUtc = Record.Now(),
                    BackupPath = p.ProtectiveArchive.Path, BackupSha256 = p.ProtectiveArchive.Sha256, BackupCompletedUtc = p.ProtectiveArchive.CompletedUtc,
                    ChoiceId = it.Record.ChooserMenu ? CommunityMenus.ChoiceId(it.Record.Id) : null,
                };
                if (p.Storage != null && it.Record.WritesExternal) { n.StorageBackupPath = p.Storage.Path; n.StorageBackupSha256 = p.Storage.Sha256; n.StorageBackupMade = p.Storage.When; n.StorageName = p.Storage.Display; n.RecoverySetRecord = recoverySet; }
                foreach (var m in pk.Inventory.Payload.Where(x => x.Type == "file")) n.Payload[m.Path] = m.Sha256;
                mods.Add(n);
            }
            CommunityLedger.Save(s, p.ConsoleTag, mods);
        }
    }

    /// <summary>Restore the pre-install backup: a full rollback of the console to the backup made before a mod was installed (Console Restore
    /// does the restore itself, with its own review and a backup of the console as it is then). This only finds that backup and checks it,
    /// with the plain warning that everything returns to that time.</summary>
    public static class CommunityRollback
    {
        public static (string backup, string warning, string problem) Check(Settings s, string consoleTag, CommunityInstalled m)
        {
            if (m == null) return (null, null, "Choose an installed mod.");
            if (string.IsNullOrEmpty(m.BackupPath)) return (null, null, "Workbench has no record of a backup made before " + m.Name + " was installed.");
            if (!File.Exists(m.BackupPath)) return (null, null, "The backup made before " + m.Name + " was installed is not where it was saved (" + m.BackupPath + "). Nothing was changed.");
            var v = Verifier.VerifyFile(m.BackupPath);
            if (!v.Ok) return (null, null, "The backup made before " + m.Name + " was installed no longer passes its check (" + v.Problems[0] + "). Backup Repair may fix it. Nothing was changed.");
            if (v.ArchiveSha256 != m.BackupSha256) return (null, null, "The backup file at that place is not the one made before " + m.Name + " was installed. Nothing was changed.");
            if (ConsoleIdentity.HwidTag(v.Hwid) != consoleTag) return (null, null, "That backup is of a different console. Nothing was changed.");
            if (!v.RestoreReady) return (null, null, "That backup does not hold everything a restore needs. Nothing was changed.");
            var when = DateTime.TryParse(m.BackupCompletedUtc ?? v.Manifest?.S("completed_utc"), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var t)
                ? t.ToLocalTime().ToString("dddd, MMMM d, yyyy 'at' h:mm tt", CultureInfo.GetCultureInfo("en-US")) : "the time it was made";
            var warn = "Restore the pre-install backup puts the whole console back to how it was on " + when + ", before " + m.Name + " was installed. Games, saves, settings and other mods added or changed after that are replaced by what the backup holds. "
                     + "This is a full restore, not the same as Remove: your console as it is now is backed up first, so later progress can be kept from that backup.";
            return (m.BackupPath, warn, null);
        }
    }
}
