// Classic Cart Studio console backup and restore engine. Copyright (C) 2026 Rance9. GPL-3.0-or-later.
// Community Mods: the curated catalog of other authors' Hakchi packages (hmods) that Workbench can install, update and remove.
//   The catalog   ccs-community-catalog/1 (tools/community/ccs-community-catalog.json, carried in this assembly and read by the website's
//                 browse-only showcase): a generation number, the four sections in their fixed order (Menus, Utilities, Themes,
//                 Emulators) and the listed entries. An entry is listed only after its author agreed and its exact version was reviewed;
//                 none is listed in this version, so the catalog this build carries is empty.
//   A record      ccs-community-record/1, one per entry: its stable id, name, section and place in it (the catalog's own order), version, Alpha flag,
//                 the author's own warnings, author/project/support/license/source links, the exact release file of each variant (its
//                 address, file name, bytes, SHA-256, unpacked bytes and when it is the one to use), permitted pictures (address, bytes,
//                 SHA-256), the consoles, regions and Hakchi build it is for, dependencies, conflicts, the packages its own install
//                 removes, external storage it writes, the places it writes (its payload paths, and what its scripts change), the menu it
//                 belongs to, how it starts and returns, a companion package, how an update applies and which earlier pins it updates,
//                 and its settings and favorites (what normal removal keeps). Every field is checked; an unknown field refuses the whole
//                 catalog. A record never carries the author's permission or any device-check results: those stay private.
//   Menus         an entry in the Menus section is a community menu. It starts either as a choice in CCS Nucleus's startup chooser
//                 (launch kind "launcher": its program, one of the files it installs, which the chooser starts with no arguments and
//                 returns from when it ends) or from its own entry in Original Menu ("menu-entry"). A menu that starts by itself at
//                 power-on is never listed: what opens at power-on is CCS Nucleus's to decide.
//   USB and SD    a mod that writes a USB drive or SD card names those places as the console sees them (/media/...), so Workbench can
//                 tell whether a USB and SD backup covers them before the mod is installed.
//   Data only     nothing in a record is ever run: there are no commands in it. What runs on a console is the author's unchanged package,
//                 placed through hakchi's own install route by the reviewed steps of CommunityEngine.
//   Updates       a catalog fetched later is used only when its ECDSA P-256 (SHA-256) signature checks against the public key pinned in
//                 this build, its generation is newer, and no version it shares with the catalog in use names a different file; anything
//                 else is refused. This version has no pinned key and no catalog address, so it uses only the catalog it carries.
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
    public sealed class CommunityCatalogException : Exception { public CommunityCatalogException(string m) : base(m) { } }

    public sealed class CommunityVariant
    {
        /// <summary>variant: its name (standard, compat …); package: hakchi's name for it (the .hmod file's name without .hmod, which hakchi's
        /// uninstall list is named after); use_when: catalog ids or hakchi package names whose presence (installed or chosen) makes this the
        /// variant to use (empty: the default variant); reason: why, in plain words</summary>
        public string Variant, Package, Url, FileName, Sha256, Reason; public long Bytes, UnpackedBytes; public List<string> UseWhen = new List<string>();
        public bool IsDefault => UseWhen.Count == 0;
        public JObj ToJson() => new JObj { { "variant", Variant }, { "package", Package }, { "url", Url }, { "file_name", FileName }, { "bytes", Bytes }, { "sha256", Sha256 }, { "unpacked_bytes", UnpackedBytes }, { "use_when", UseWhen.Cast<object>().ToList() }, { "reason", Reason } };
    }

    public sealed class CommunityShot { public string Url, Sha256, Caption; public long Bytes; }
    public sealed class CommunitySetting { public string Path, Kind; }

    public sealed class CommunityRecord
    {
        public string Id, Name, Section, Version, Summary, Applies, Description, Target, LaunchKind, LaunchReturn, LaunchProgram, UpdateMethod, RemovalText, ExternalText;
        public int RecordVersion, Order; public bool Alpha, WritesExternal;
        public string AuthorName, ProjectUrl, SupportUrl, LicenseName, LicenseUrl, SourceUrl, CompanionId, CompanionVersion, Hakchi;
        public List<string> Warnings = new List<string>(), Credits = new List<string>(), Notices = new List<string>(), Consoles = new List<string>(), Regions = new List<string>();
        public List<string> Menus = new List<string>(), Depends = new List<string>(), PayloadPaths = new List<string>(), ScriptWrites = new List<string>(), ExternalLocations = new List<string>();
        public List<CommunityVariant> Variants = new List<CommunityVariant>();
        public List<CommunityShot> Shots = new List<CommunityShot>();
        public List<(string id, string text)> Conflicts = new List<(string, string)>();
        public List<(string package, string name)> RemovesOnInstall = new List<(string, string)>();
        public List<(string version, string sha256)> UpdatesFrom = new List<(string, string)>();
        public List<CommunitySetting> Settings = new List<CommunitySetting>();
        /// <summary>the record exactly as the catalog had it (kept with an installed mod, so it stays manageable after delisting)</summary>
        public Dictionary<string, object> Raw;
        public CommunityVariant DefaultVariant => Variants.First(v => v.IsDefault);
        public IEnumerable<string> Packages => Variants.Select(v => v.Package);
        public string SectionTitle => CommunityCatalog.SectionTitle(Section);
        /// <summary>an entry of the Menus section: a community menu</summary>
        public bool IsMenu => Section == "menus";
        /// <summary>a community menu CCS Nucleus's startup chooser starts (its program); installing it adds it as a choice there</summary>
        public bool ChooserMenu => IsMenu && LaunchKind == "launcher" && LaunchProgram != null;
        public JObj ToJson() => (JObj)CommunityCatalog.ToJObj(Raw);
    }

    public sealed class CommunityCatalog
    {
        public const string Schema = "ccs-community-catalog/1", RecordSchema = "ccs-community-record/1";
        public static readonly string[] SectionOrder = { "menus", "utilities", "themes", "emulators" };
        public static string SectionTitle(string s) => s == "menus" ? "Menus" : s == "utilities" ? "Utilities" : s == "themes" ? "Themes" : s == "emulators" ? "Emulators" : s;
        /// <summary>the Hakchi build a listed package must be for: Hakchi2 CE 3.9.3's console build</summary>
        public static string RequiredHakchi => Preparation.PinnedHakchiVersion;
        public int Generation; public string Issued; public byte[] Bytes;
        public List<CommunityRecord> Entries = new List<CommunityRecord>();
        public bool Empty => Entries.Count == 0;
        public CommunityRecord Find(string id) => Entries.FirstOrDefault(e => e.Id == id);
        public CommunityRecord ByPackage(string package) => Entries.FirstOrDefault(e => e.Packages.Contains(package));
        /// <summary>a section's entries in the catalog's own order (never alphabetical)</summary>
        public List<CommunityRecord> Section(string s) => Entries.Where(e => e.Section == s).OrderBy(e => e.Order).ToList();
        public string Sha256 { get { using (var h = SHA256.Create()) return Hex.Of(h.ComputeHash(Bytes ?? new byte[0])); } }

        /// <summary>the catalog this build carries (tools/community/ccs-community-catalog.json); null and a reason when it cannot be read</summary>
        public static (CommunityCatalog catalog, string problem) Shipping()
        {
            using (var s = typeof(CommunityCatalog).Assembly.GetManifestResourceStream("community/ccs-community-catalog.json"))
            {
                if (s == null) return (null, "This copy of Workbench carries no community catalog.");
                var m = new MemoryStream(); s.CopyTo(m);
                try { return (Parse(m.ToArray()), null); }
                catch (CommunityCatalogException ex) { return (null, "The community catalog this copy carries cannot be used (" + ex.Message + ")."); }
            }
        }

        // ------------------------------------------------------------------ parsing: strict, every field checked, the first problem refuses
        static readonly Regex IdRe = new Regex("^[a-z0-9][a-z0-9-]{1,47}$"), VariantRe = new Regex("^[a-z0-9][a-z0-9-]{0,23}$"),
            VersionRe = new Regex(@"^[A-Za-z0-9][A-Za-z0-9 ._+\-\[\]()]{0,39}$"), DateRe = new Regex("^20[0-9]{2}-[01][0-9]-[0-3][0-9]$"),
            FamilyRe = new Regex("^[a-z0-9][a-z0-9-]{2,31}$"), RegionRe = new Regex("^[A-Z]{2,8}$");
        /// <summary>hakchi's package name (the .hmod file's name): plain characters, never one of Classic Cart Studio's own (ccs-…) or hakchi itself</summary>
        public static readonly Regex PackageRe = new Regex("^[A-Za-z0-9][A-Za-z0-9._+-]{0,63}$");
        public static bool PackageOk(string p) => p != null && PackageRe.IsMatch(p) && !p.StartsWith("ccs-", StringComparison.OrdinalIgnoreCase) && !p.Equals("hakchi", StringComparison.OrdinalIgnoreCase) && !p.EndsWith(".hmod", StringComparison.OrdinalIgnoreCase);
        /// <summary>a payload path (where the package's files go in hakchi's root overlay): relative, plain parts; a folder ends with "/"</summary>
        public static readonly Regex PayloadRe = new Regex(@"^[A-Za-z0-9_][A-Za-z0-9._+-]*(/[A-Za-z0-9_][A-Za-z0-9._+ -]*)*/?$");
        /// <summary>a setting's or favorite's place on the data storage, under hakchi's root overlay or the console's profiles (no spaces: the
        /// console's steps take these as words)</summary>
        public static readonly Regex SettingRe = new Regex(@"^(hakchi/rootfs|clover/profiles)/[A-Za-z0-9_][A-Za-z0-9._+-]*(/[A-Za-z0-9_][A-Za-z0-9._+-]*)*$");
        /// <summary>a menu's program: one file it installs, a plain relative path without spaces (CCS Nucleus records it as a console path)</summary>
        public static readonly Regex ProgramRe = new Regex(@"^[A-Za-z0-9_][A-Za-z0-9._-]*(/[A-Za-z0-9_][A-Za-z0-9._-]*)*$");
        /// <summary>a place a mod writes on a USB drive or SD card, as the console sees it: under /media (where hakchi mounts that storage)</summary>
        public static readonly Regex ExternalRe = new Regex(@"^/media(/[A-Za-z0-9_][A-Za-z0-9._+ -]*)+/?$");
        public static readonly string[] Targets = { "original-menu", "system", "launchers" }, LaunchKinds = { "none", "service", "menu-entry", "launcher" }, UpdateMethods = { "install-over", "uninstall-first" };
        /// <summary>what a record must never hold: an author's permission or device-check results are kept private, never in the catalog</summary>
        public static readonly string[] PrivateFields = { "permission", "permissions", "opt_in", "author_permission", "permission_ledger", "tested", "tested_on", "test_results", "hardware", "hardware_evidence", "qualified", "qualification", "verified_on", "device_tests", "evidence" };

        public static CommunityCatalog Parse(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0 || bytes.Length > 4000000) throw new CommunityCatalogException("the catalog is missing or larger than 4 MB");
            Dictionary<string, object> d;
            try { d = Json.Parse(new UTF8Encoding(false, true).GetString(bytes)); }
            catch (Exception ex) when (ex is FormatException || ex is ArgumentException || ex is InvalidOperationException || ex is DecoderFallbackException) { throw new CommunityCatalogException("the catalog is not valid JSON text"); }
            Only(d, "the catalog", "schema", "generation", "issued", "sections", "entries");
            if (d.S("schema") != Schema) throw new CommunityCatalogException("the catalog's schema is not " + Schema);
            var c = new CommunityCatalog { Bytes = bytes, Generation = (int)Int(d, "generation", 1, 100000000, "the catalog"), Issued = Text(d, "issued", 10, "the catalog") };
            if (!DateRe.IsMatch(c.Issued)) throw new CommunityCatalogException("the catalog's issue date is not a date");
            var secs = Strings(d, "sections", 4, 20, "the catalog", true);
            if (!secs.SequenceEqual(SectionOrder)) throw new CommunityCatalogException("the catalog's sections must be menus, utilities, themes and emulators, in that order");
            if (!(d.TryGetValue("entries", out var ev) && ev is object[] ea)) throw new CommunityCatalogException("the catalog has no entries list");
            if (ea.Length > 200) throw new CommunityCatalogException("the catalog lists more than 200 entries");
            foreach (var e in ea) c.Entries.Add(ParseRecord(e as Dictionary<string, object> ?? throw new CommunityCatalogException("an entry is not an object")));
            var dup = c.Entries.GroupBy(e => e.Id).FirstOrDefault(g => g.Count() > 1); if (dup != null) throw new CommunityCatalogException("two entries have the id " + dup.Key);
            var place = c.Entries.GroupBy(e => e.Section + "/" + e.Order).FirstOrDefault(g => g.Count() > 1); if (place != null) throw new CommunityCatalogException("two entries share the place " + place.Key);
            var pk = c.Entries.SelectMany(e => e.Packages.Select(p => (p: p.ToLowerInvariant(), e.Id))).GroupBy(x => x.p).FirstOrDefault(g => g.Select(x => x.Id).Distinct().Count() > 1);
            if (pk != null) throw new CommunityCatalogException("two entries use the hakchi package name " + pk.Key);
            return c;
        }

        public static CommunityRecord ParseRecord(Dictionary<string, object> r)
        {
            foreach (var k in r.Keys) if (PrivateFields.Contains(k.ToLowerInvariant())) throw new CommunityCatalogException("a record holds \"" + k + "\": permissions and device-check results stay private and are never part of the catalog");
            var id = r.S("id"); var who = "the entry " + (id ?? "(no id)");
            Only(r, who, "schema", "id", "record_version", "name", "section", "order", "version", "alpha", "summary", "applies", "description", "warnings", "author", "license", "variants", "screenshots",
                "compatible", "depends", "conflicts", "removes_on_install", "external_storage", "writes", "target", "launch", "companion", "update", "removal");
            if (r.S("schema") != RecordSchema) throw new CommunityCatalogException(who + ": its schema is not " + RecordSchema);
            if (id == null || !IdRe.IsMatch(id)) throw new CommunityCatalogException("an entry's id is not a stable id (lower-case letters, digits and -)");
            var x = new CommunityRecord { Raw = r, Id = id, RecordVersion = (int)Int(r, "record_version", 1, 1000000, who), Name = Text(r, "name", 60, who), Section = Text(r, "section", 20, who),
                Order = (int)Int(r, "order", 0, 9999, who), Version = Text(r, "version", 40, who), Alpha = Bool(r, "alpha", who), Summary = Text(r, "summary", 300, who), Applies = Text(r, "applies", 400, who),
                Description = OptText(r, "description", 2000, who), Target = Text(r, "target", 20, who) };
            if (!SectionOrder.Contains(x.Section)) throw new CommunityCatalogException(who + ": its section is not menus, utilities, themes or emulators");
            if (!VersionRe.IsMatch(x.Version)) throw new CommunityCatalogException(who + ": its version has characters a version cannot have");
            if (!Targets.Contains(x.Target)) throw new CommunityCatalogException(who + ": its target is not original-menu, system or launchers");
            x.Warnings = Strings(r, "warnings", 12, 600, who, false);
            var a = Obj(r, "author", who); Only(a, who + "'s author", "name", "credits", "project_url", "support_url");
            x.AuthorName = Text(a, "name", 80, who); x.Credits = Strings(a, "credits", 20, 160, who, false); x.ProjectUrl = Url(a, "project_url", who, true); x.SupportUrl = Url(a, "support_url", who, false);
            var l = Obj(r, "license", who); Only(l, who + "'s license", "name", "url", "source_url", "notices");
            x.LicenseName = Text(l, "name", 80, who); x.LicenseUrl = Url(l, "url", who, true); x.SourceUrl = Url(l, "source_url", who, true); x.Notices = Strings(l, "notices", 20, 400, who, false);
            if (!(r.TryGetValue("variants", out var vv) && vv is object[] va) || va.Length < 1 || va.Length > 4) throw new CommunityCatalogException(who + ": it needs one to four variants (the exact release files)");
            foreach (var o in va)
            {
                var v = o as Dictionary<string, object> ?? throw new CommunityCatalogException(who + ": a variant is not an object");
                Only(v, who + "'s variant", "variant", "package", "url", "file_name", "bytes", "sha256", "unpacked_bytes", "use_when", "reason");
                var cv = new CommunityVariant { Variant = Text(v, "variant", 24, who), Package = Text(v, "package", 64, who), Url = Url(v, "url", who, true), FileName = Text(v, "file_name", 80, who),
                    Bytes = Int(v, "bytes", 1, 400000000, who), Sha256 = Text(v, "sha256", 64, who), UnpackedBytes = Int(v, "unpacked_bytes", 1, 1000000000, who), UseWhen = Strings(v, "use_when", 8, 64, who, false), Reason = OptText(v, "reason", 300, who) };
                if (!VariantRe.IsMatch(cv.Variant)) throw new CommunityCatalogException(who + ": a variant's name is not plain");
                if (!PackageOk(cv.Package)) throw new CommunityCatalogException(who + ": the package name " + cv.Package + " cannot be used (plain characters only; never ccs-… or hakchi)");
                if (!Hex.IsHex(cv.Sha256, 64)) throw new CommunityCatalogException(who + ": a variant's SHA-256 is not 64 lower-case hex digits");
                if (!cv.Url.EndsWith("/" + cv.FileName, StringComparison.Ordinal) || !cv.FileName.EndsWith(".hmod", StringComparison.Ordinal)) throw new CommunityCatalogException(who + ": a variant's address does not end with its .hmod file's name");
                if (!cv.IsDefault && cv.Reason == null) throw new CommunityCatalogException(who + ": the variant " + cv.Variant + " says when it is used but not why");
                x.Variants.Add(cv);
            }
            if (x.Variants.Count(v => v.IsDefault) != 1) throw new CommunityCatalogException(who + ": exactly one variant must be the default (no use_when)");
            if (x.Variants.Select(v => v.Variant).Distinct().Count() != x.Variants.Count || x.Variants.Select(v => v.Package.ToLowerInvariant()).Distinct().Count() != x.Variants.Count) throw new CommunityCatalogException(who + ": two variants share a name or a package name");
            if (r.TryGetValue("screenshots", out var sv))
            {
                if (!(sv is object[] sa) || sa.Length > 6) throw new CommunityCatalogException(who + ": screenshots must be a list of at most six");
                foreach (var o in sa)
                {
                    var s = o as Dictionary<string, object> ?? throw new CommunityCatalogException(who + ": a screenshot is not an object");
                    Only(s, who + "'s screenshot", "url", "bytes", "sha256", "caption");
                    var sh = new CommunityShot { Url = Url(s, "url", who, true), Bytes = Int(s, "bytes", 1, 5000000, who), Sha256 = Text(s, "sha256", 64, who), Caption = Text(s, "caption", 160, who) };
                    if (!Hex.IsHex(sh.Sha256, 64)) throw new CommunityCatalogException(who + ": a screenshot's SHA-256 is not 64 lower-case hex digits");
                    x.Shots.Add(sh);
                }
            }
            var cp = Obj(r, "compatible", who); Only(cp, who + "'s compatibility", "consoles", "regions", "hakchi", "menus");
            x.Consoles = Strings(cp, "consoles", 8, 32, who, true); x.Regions = Strings(cp, "regions", 12, 8, who, false); x.Menus = Strings(cp, "menus", 8, 32, who, false); x.Hakchi = Text(cp, "hakchi", 40, who);
            if (x.Consoles.Count == 0 || x.Consoles.Any(f => !FamilyRe.IsMatch(f)) || x.Regions.Any(g => !RegionRe.IsMatch(g))) throw new CommunityCatalogException(who + ": its consoles or regions are not plain names");
            if (x.Hakchi != RequiredHakchi) throw new CommunityCatalogException(who + ": it is not for Hakchi2 CE 3.9.3's console build (" + RequiredHakchi + ")");
            x.Depends = Strings(r, "depends", 8, 48, who, false);
            if (x.Depends.Any(i => !IdRe.IsMatch(i) || i == x.Id)) throw new CommunityCatalogException(who + ": a dependency is not another entry's id");
            foreach (var o in List(r, "conflicts", 8, who)) { Only(o, who + "'s conflict", "id", "text"); var cid = Text(o, "id", 48, who); if (!IdRe.IsMatch(cid) || cid == x.Id) throw new CommunityCatalogException(who + ": a conflict is not another entry's id"); x.Conflicts.Add((cid, Text(o, "text", 300, who))); }
            foreach (var o in List(r, "removes_on_install", 12, who)) { Only(o, who + "'s removal", "package", "name"); var p = Text(o, "package", 64, who); if (!PackageRe.IsMatch(p)) throw new CommunityCatalogException(who + ": a package its install removes is not a plain name"); x.RemovesOnInstall.Add((p, Text(o, "name", 80, who))); }
            var ex = Obj(r, "external_storage", who); Only(ex, who + "'s external storage", "writes", "locations", "text");
            x.WritesExternal = Bool(ex, "writes", who); x.ExternalLocations = Strings(ex, "locations", 8, 200, who, false); x.ExternalText = OptText(ex, "text", 400, who);
            if (x.WritesExternal && (x.ExternalLocations.Count == 0 || x.ExternalText == null)) throw new CommunityCatalogException(who + ": it writes external storage but does not say where and why");
            if (!x.WritesExternal && x.ExternalLocations.Count > 0) throw new CommunityCatalogException(who + ": it names places on external storage but says it does not write there");
            if (x.ExternalLocations.Any(p => !ExternalRe.IsMatch(p) || p.Split('/').Any(s => s == ".." || s == "."))) throw new CommunityCatalogException(who + ": a place it writes on a USB drive or SD card is not a plain path under /media/");
            var w = Obj(r, "writes", who); Only(w, who + "'s writes", "payload", "scripts");
            x.PayloadPaths = Strings(w, "payload", 64, 200, who, true); x.ScriptWrites = Strings(w, "scripts", 16, 300, who, false);
            if (x.PayloadPaths.Count == 0 || x.PayloadPaths.Any(p => !PayloadRe.IsMatch(p) || p.Split('/').Any(s => s == ".." || s == "."))) throw new CommunityCatalogException(who + ": a place it writes is not a plain relative path");
            var la = Obj(r, "launch", who); Only(la, who + "'s launch", "kind", "return", "program");
            x.LaunchKind = Text(la, "kind", 20, who); x.LaunchReturn = OptText(la, "return", 300, who); x.LaunchProgram = OptText(la, "program", 200, who);
            if (!LaunchKinds.Contains(x.LaunchKind)) throw new CommunityCatalogException(who + ": its launch kind is not none, service, menu-entry or launcher");
            if (x.LaunchKind == "launcher" && x.LaunchProgram == null) throw new CommunityCatalogException(who + ": it starts as a launcher but does not name its program");
            if (x.LaunchProgram != null)
            {
                if (x.LaunchKind != "launcher") throw new CommunityCatalogException(who + ": only a launcher names its program");
                if (!ProgramRe.IsMatch(x.LaunchProgram) || x.LaunchProgram.Split('/').Any(s => s == ".." || s == ".")) throw new CommunityCatalogException(who + ": its program is not a plain relative path to a file");
                if (!CommunityArchive.Covered(x.LaunchProgram, x.PayloadPaths)) throw new CommunityCatalogException(who + ": its program is not among the places it writes");
            }
            // a community menu starts from CCS Nucleus's startup chooser or from its own entry in Original Menu, never by itself at power-on
            if (x.IsMenu && x.LaunchKind != "launcher" && x.LaunchKind != "menu-entry") throw new CommunityCatalogException(who + ": a menu must start as a launcher (a choice in CCS Nucleus's startup chooser) or from its entry in Original Menu, never by itself at power-on");
            if (x.IsMenu && x.LaunchReturn == null) throw new CommunityCatalogException(who + ": a menu must say how to choose another menu from it (its launch's return)");
            if (r.TryGetValue("companion", out var co) && co != null)
            {
                var cm = co as Dictionary<string, object> ?? throw new CommunityCatalogException(who + ": its companion is not an object");
                Only(cm, who + "'s companion", "id", "version"); x.CompanionId = Text(cm, "id", 48, who); x.CompanionVersion = Text(cm, "version", 40, who);
                if (!IdRe.IsMatch(x.CompanionId) || !VersionRe.IsMatch(x.CompanionVersion)) throw new CommunityCatalogException(who + ": its companion is not an id and a version");
            }
            var up = Obj(r, "update", who); Only(up, who + "'s update", "method", "from");
            x.UpdateMethod = Text(up, "method", 20, who);
            if (!UpdateMethods.Contains(x.UpdateMethod)) throw new CommunityCatalogException(who + ": its update method is not install-over or uninstall-first");
            foreach (var o in List(up, "from", 16, who)) { Only(o, who + "'s earlier version", "version", "sha256"); var fv = Text(o, "version", 40, who); var fs = Text(o, "sha256", 64, who); if (!Hex.IsHex(fs, 64)) throw new CommunityCatalogException(who + ": an earlier version's SHA-256 is not 64 hex digits"); x.UpdatesFrom.Add((fv, fs)); }
            var rm = Obj(r, "removal", who); Only(rm, who + "'s removal", "settings", "text");
            x.RemovalText = OptText(rm, "text", 400, who);
            foreach (var o in List(rm, "settings", 16, who))
            {
                Only(o, who + "'s setting", "path", "kind"); var st = new CommunitySetting { Path = Text(o, "path", 200, who), Kind = Text(o, "kind", 12, who) };
                if (!SettingRe.IsMatch(st.Path) || st.Path.Split('/').Any(s => s == ".." || s == ".")) throw new CommunityCatalogException(who + ": a setting's place is not under hakchi/rootfs/ or clover/profiles/");
                if (st.Kind != "settings" && st.Kind != "favorites") throw new CommunityCatalogException(who + ": a setting is neither settings nor favorites");
                x.Settings.Add(st);
            }
            return x;
        }

        // ---- field readers (each refuses with the field's name)
        static void Only(Dictionary<string, object> d, string who, params string[] keys) { foreach (var k in d.Keys) if (!keys.Contains(k)) throw new CommunityCatalogException(who + " has an unknown field \"" + k + "\""); }
        static bool Plain(string s) => s.All(ch => ch >= 32 && ch != 127 && !(ch >= 128 && ch < 160) && ch != 8232 && ch != 8233);
        static string Text(Dictionary<string, object> d, string k, int max, string who)
        {
            var s = d.S(k); if (string.IsNullOrWhiteSpace(s)) throw new CommunityCatalogException(who + ": \"" + k + "\" is missing");
            if (s.Length > max || !Plain(s) || s != s.Trim()) throw new CommunityCatalogException(who + ": \"" + k + "\" is longer than " + max + " characters or holds control characters");
            return s;
        }
        static string OptText(Dictionary<string, object> d, string k, int max, string who) => d.TryGetValue(k, out var v) && v != null ? Text(d, k, max, who) : null;
        static long Int(Dictionary<string, object> d, string k, long min, long max, string who)
        {
            if (!d.TryGetValue(k, out var v) || !(v is int || v is long)) throw new CommunityCatalogException(who + ": \"" + k + "\" is not a whole number");
            var n = Convert.ToInt64(v, CultureInfo.InvariantCulture); if (n < min || n > max) throw new CommunityCatalogException(who + ": \"" + k + "\" is outside " + min + " to " + max);
            return n;
        }
        static bool Bool(Dictionary<string, object> d, string k, string who) { if (!d.TryGetValue(k, out var v) || !(v is bool b)) throw new CommunityCatalogException(who + ": \"" + k + "\" is not true or false"); return b; }
        static Dictionary<string, object> Obj(Dictionary<string, object> d, string k, string who) => d.TryGetValue(k, out var v) && v is Dictionary<string, object> o ? o : throw new CommunityCatalogException(who + ": \"" + k + "\" is missing");
        static List<Dictionary<string, object>> List(Dictionary<string, object> d, string k, int max, string who)
        {
            if (!d.TryGetValue(k, out var v) || v == null) return new List<Dictionary<string, object>>();
            if (!(v is object[] a) || a.Length > max) throw new CommunityCatalogException(who + ": \"" + k + "\" must be a list of at most " + max);
            return a.Select(o => o as Dictionary<string, object> ?? throw new CommunityCatalogException(who + ": an item of \"" + k + "\" is not an object")).ToList();
        }
        static List<string> Strings(Dictionary<string, object> d, string k, int max, int len, string who, bool required)
        {
            if (!d.TryGetValue(k, out var v) || v == null) { if (required) throw new CommunityCatalogException(who + ": \"" + k + "\" is missing"); return new List<string>(); }
            if (!(v is object[] a) || a.Length > max) throw new CommunityCatalogException(who + ": \"" + k + "\" must be a list of at most " + max);
            var o = new List<string>();
            foreach (var x in a) { var s = x as string; if (string.IsNullOrWhiteSpace(s) || s.Length > len || !Plain(s) || s != s.Trim()) throw new CommunityCatalogException(who + ": an item of \"" + k + "\" is empty, too long or holds control characters"); o.Add(s); }
            if (o.Distinct().Count() != o.Count) throw new CommunityCatalogException(who + ": \"" + k + "\" names something twice");
            return o;
        }
        /// <summary>an author's address: https only, a host name, no user name or password in it</summary>
        static string Url(Dictionary<string, object> d, string k, string who, bool required)
        {
            var s = required ? Text(d, k, 500, who) : OptText(d, k, 500, who); if (s == null) return null;
            if (!UrlOk(s)) throw new CommunityCatalogException(who + ": \"" + k + "\" is not an https address");
            return s;
        }
        public static bool UrlOk(string s) => Uri.TryCreate(s, UriKind.Absolute, out var u) && u.Scheme == Uri.UriSchemeHttps && string.IsNullOrEmpty(u.UserInfo) && u.HostNameType == UriHostNameType.Dns && u.Host.Contains(".") && !u.IsLoopback && s.IndexOf(' ') < 0 && s.IndexOf('\\') < 0;

        /// <summary>the parsed JSON value back as JObj/lists (records kept in the PC's ledger, plans and events)</summary>
        public static object ToJObj(object v)
        {
            if (v is Dictionary<string, object> d) { var j = new JObj(); foreach (var kv in d) j.Add(kv.Key, ToJObj(kv.Value)); return j; }
            if (v is object[] a) return a.Select(ToJObj).ToList();
            return v;
        }
    }

    /// <summary>a catalog fetched later: used only when signed with the key pinned in this build (ECDSA P-256, SHA-256), newer, and with no
    /// approved version's file changed. This version pins no key and has no catalog address (both are set by the publisher when a real
    /// signing key exists), so it accepts no fetched catalog.</summary>
    public static class CommunityCatalogTrust
    {
        /// <summary>the address a newer signed catalog would be fetched from; none in this version</summary>
        public const string RemoteUrl = null;
        /// <summary>the publisher's public key, "p256:" and the base64 of its 65-byte uncompressed point; none in this version</summary>
        public const string PinnedKey = null;
        public const string NoKeyText = "This version of Workbench has no catalog signing key, so it does not accept a catalog from anywhere else. It uses the catalog it carries.";

        public static (CommunityCatalog catalog, string problem) VerifyUpdate(byte[] catalog, byte[] signature, CommunityCatalog current) => VerifyUpdate(catalog, signature, current, PinnedKey);

        /// <summary>key: the public key to check against (tests pass their own; the product passes PinnedKey)</summary>
        internal static (CommunityCatalog catalog, string problem) VerifyUpdate(byte[] catalog, byte[] signature, CommunityCatalog current, string key)
        {
            if (key == null) return (null, NoKeyText);
            ECParameters p;
            try { p = KeyOf(key); } catch (FormatException) { return (null, "The catalog signing key in this build cannot be read."); }
            var sig = SignatureOf(signature);
            if (sig == null) return (null, "The catalog's signature is not a P-256 signature. The catalog was not used.");
            bool ok;
            try { using (var ec = ECDsa.Create(p)) ok = ec.VerifyData(catalog ?? new byte[0], sig, HashAlgorithmName.SHA256); }
            catch (CryptographicException) { ok = false; }
            if (!ok) return (null, "The catalog's signature does not match the publisher's key: it was changed or not made by the publisher. The catalog was not used.");
            CommunityCatalog c;
            try { c = CommunityCatalog.Parse(catalog); } catch (CommunityCatalogException ex) { return (null, "The signed catalog cannot be used (" + ex.Message + "). The catalog was not used."); }
            if (current != null && c.Generation <= current.Generation) return (null, "The signed catalog is not newer than the one in use (generation " + c.Generation + ", in use " + current.Generation + "). The catalog was not used.");
            if (DateTime.TryParseExact(c.Issued, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var issued) && issued > DateTime.UtcNow.AddDays(2))
                return (null, "The signed catalog is dated in the future (" + c.Issued + "). The catalog was not used.");
            if (current != null)
                foreach (var e in c.Entries)
                {
                    var old = current.Find(e.Id); if (old == null || old.Version != e.Version) continue;
                    foreach (var v in e.Variants)
                    {
                        var ov = old.Variants.FirstOrDefault(x => x.Variant == v.Variant);
                        if (ov != null && (ov.Sha256 != v.Sha256 || ov.Bytes != v.Bytes || ov.Url != v.Url))
                            return (null, "The signed catalog names a different file for " + e.Name + " " + e.Version + " than the approved one. A changed file is never accepted for the same version. The catalog was not used.");
                    }
                }
            return (c, null);
        }

        internal static ECParameters KeyOf(string key)
        {
            if (!key.StartsWith("p256:", StringComparison.Ordinal)) throw new FormatException();
            var b = Convert.FromBase64String(key.Substring(5));
            if (b.Length != 65 || b[0] != 4) throw new FormatException();
            return new ECParameters { Curve = ECCurve.NamedCurves.nistP256, Q = new ECPoint { X = b.Skip(1).Take(32).ToArray(), Y = b.Skip(33).Take(32).ToArray() } };
        }
        /// <summary>the signature file: the 64-byte r||s signature, raw or as base64 text</summary>
        static byte[] SignatureOf(byte[] s)
        {
            if (s == null) return null;
            if (s.Length == 64) return s;
            try { var t = Encoding.ASCII.GetString(s).Trim(); var b = Convert.FromBase64String(t); return b.Length == 64 ? b : null; } catch (FormatException) { return null; }
        }
        internal static string KeyText(ECParameters p) => "p256:" + Convert.ToBase64String(new byte[] { 4 }.Concat(p.Q.X).Concat(p.Q.Y).ToArray());
    }
}
