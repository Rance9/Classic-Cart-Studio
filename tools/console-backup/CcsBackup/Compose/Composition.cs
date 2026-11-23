// Classic Cart Studio console backup and restore engine. Copyright (C) 2026 Rance9. GPL-3.0-or-later.
// The shareable composition (schema ccs-composition/1, a UTF-8 JSON file): Console Composer's portable PLAN ONLY.
// It holds choices (starting point, install policy, environments), generic references (public catalog games, public mod builds, a HOME
// design by id and fingerprint, CCS components by version) and safe public build fingerprints. It never holds a game, firmware or save,
// a console identity or anything derived from one, keys, private file locations, names or notes, screenshots, attachments or the private
// list of source files. Roles that need the receiver's own material (their console's original software, their saves, a game of the
// sender's own) say so and carry no fingerprint: those stay in the sender's private package. No Nintendo download link exists in it,
// because no field can hold a link at all. A plan cannot authorize a write or be a console restore point (CanAuthorizeWrites,
// IsRestorePoint: false, by construction).
//
// The schema is closed: every object has an allowlist of members; anything else is refused, not skipped. Data a newer minor version adds
// lives only in "optional" objects (Inert rules: plain values, bounded, no fingerprints, kept unchanged on open -> edit -> save). A newer
// schema major or an unknown "requires" capability opens for inspection only (apply blocked; the original can be exported unchanged);
// an unknown value in a known field (a newer starting point, policy, environment kind or input role) is viewable, not appliable.
//
// Sharing goes through CompositionShare.Prepare: it builds the shared plan from the Composer's private draft by copying only what the
// allowlist and the public registry permit, lists what is included and what was removed, refuses text that holds a private detail, and
// finally checks the written text for every private value it knows. CompositionShare.Write writes only a preview that passed.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace CcsBackup.Compose
{
    /// <summary>what this program knows to be public: catalog games (version id, title, the content fingerprint of that version) and
    /// public builds (CCS components, hakchi kernels, mods) by name, version and fingerprint</summary>
    public interface IPublicRegistry
    {
        string GameTitle(string catalogId);                                     // null when the id is not a public catalog version
        bool IsPublicGameHash(string catalogId, string contentSha256);
        bool IsPublicBuild(string kind, string name, string version, string sha256);   // kind: mod, ccs-component, hakchi-kernel
    }

    public sealed class CompositionDraft
    {
        public string PlanId, Title, Notes, StartingPoint, InstallPolicy, Platform = "nes-classic"; public long Revision = 1;
        public List<DraftEnvironment> Environments = new List<DraftEnvironment>();
        public List<DraftGame> Games = new List<DraftGame>();
        public List<DraftMod> Mods = new List<DraftMod>();
        public List<DraftInput> Inputs = new List<DraftInput>();
        public DraftDesign HomeDesign;
        public string SourceConsoleHwid, SourceConsoleTag, PrivateSourceManifest;
        public List<string> Screenshots = new List<string>(), Attachments = new List<string>();
    }
    public sealed class DraftEnvironment { public string Id, Kind; public bool Default; }
    public sealed class DraftGame { public string CatalogId, PersonalId, Title, FileName, LocalPath, ContentSha256, Environment, SaveSha256, SavePath; public bool BringSave; }
    public sealed class DraftMod { public string Name, Version, Sha256, LocalPath, Environment; }
    public sealed class DraftInput { public string Role, Ref, Version, Sha256, LocalPath, SourceBackupId, Environment; }
    public sealed class DraftDesign { public string Id, Sha256, Name; public long Revision = 1; }

    /// <summary>the person's private details the share check looks for in the written plan (user and computer names, console tags and
    /// identities, folder locations, backup ids, private fingerprints); the draft's own private parts are added automatically</summary>
    public sealed class PrivateValues
    {
        public readonly List<string> Values = new List<string>();
        public PrivateValues Add(params string[] v) { Values.AddRange(v.Where(x => !string.IsNullOrWhiteSpace(x))); return this; }
    }

    public sealed class SharePreview
    {
        public bool CanShare; public Composition Shared; public byte[] Json;
        public List<string> Included = new List<string>(), Removed = new List<string>(), Refusals = new List<string>();
    }

    public sealed class ChecklistItem { public string What, Status, Text; }   // status: ready, supply-your-own, missing, mismatched, unknown-build, unsupported

    public sealed class Composition
    {
        public const string Format = "classic-cart-studio-composition", Schema = "ccs-composition/1", Kind = "shared-plan", Extension = ".ccsplan", TypeName = "Shared Composition Plan";
        public const string Label = "Shared Composition Plan: choices and public references only; no games, saves, firmware or console identity.";
        public const bool CanAuthorizeWrites = false, IsRestorePoint = false;
        public const int MaxBytes = 1000000, MaxEnvironments = 8, MaxGames = 5000, MaxMods = 500, MaxInputs = 32;
        public static readonly string[] StartingPoints = { "like-my-other-console", "rebuild-from-backups", "fresh-ccs" };
        /// <summary>the add-ons a plan asks for: CCS Mod, CCS Nucleus, both, or none (each is added or removed on its own)</summary>
        public static readonly string[] InstallPolicies = { "ccs-mod", "ccs-nucleus", "ccs-mod+ccs-nucleus", "none" };
        /// <summary>a starting point's and the add-ons' names as the window shows them (the codes stay in the plan file)</summary>
        public static string StartingPointName(string s) => s == "like-my-other-console" ? "Set Up Like My Other Console" : s == "rebuild-from-backups" ? "Rebuild from My Backups" : s == "fresh-ccs" ? "Start Fresh with Classic Cart Studio" : s;
        public static string PolicyName(string s) => s == "ccs-mod" ? "CCS Mod (CCS HOME)" : s == "ccs-nucleus" ? "CCS Nucleus" : s == "ccs-mod+ccs-nucleus" ? "CCS Mod (CCS HOME) and CCS Nucleus" : s == "none" ? "No add-ons" : s;
        public static readonly string[] EnvironmentKinds = { "nintendo-original", "ccs-home", "hakchi-ce" };
        public static readonly string[] InputRoles = { "console-system", "console-saves", "ccs-component", "hakchi-kernel" };
        public static readonly string[] CcsComponents = { "ccs-mod", "ccs-nucleus" };
        public static readonly string[] Platforms = { "nes-classic" };
        public static readonly string[] KnownCapabilities = { "composition/1" };
        static readonly Regex EnvId = new Regex("^[a-z][a-z0-9-]{0,15}$"), ModName = new Regex("^[a-z0-9][a-z0-9._-]{0,63}$"), Version = new Regex("^[0-9A-Za-z][0-9A-Za-z.+_-]{0,31}$");

        JObj doc;
        public bool CanApply { get; private set; }
        public bool InspectOnly { get; private set; }
        public List<string> Unsupported { get; private set; } = new List<string>();
        public byte[] OriginalFile { get; private set; }
        Composition() { }

        public JObj Document => (JObj)StrictJson.Clone(doc);
        public string Id => (doc["plan"] as JObj)?["id"] as string;
        public string Title => (doc["plan"] as JObj)?["title"] as string;
        public string StartingPoint => doc["starting_point"] as string;
        public string InstallPolicy => doc["install_policy"] as string;
        public byte[] ToBytes() { if (InspectOnly) throw new ComposeException("composition.inspect-only", "This plan was made by a newer version; export the original file instead."); Validate(); return Json.Bytes(doc); }
        public string Sha256 => Sha.Of(Json.Bytes(doc));

        public static Composition Parse(byte[] json)
        {
            var doc = StrictJson.ParseObject(json, new JsonLimits { MaxBytes = MaxBytes, MaxDepth = 16, MaxValues = 100000, MaxString = 4000 }, "The plan");
            var c = new Composition { doc = doc, OriginalFile = json };
            c.Validate(); return c;
        }

        public void RequireApplicable()
        {
            if (!CanApply) throw new ComposeException("composition.not-applicable", "This plan cannot be used here: " + string.Join("; ", Unsupported) + ".");
        }

        /// <summary>changes the plan's title (an example of an edit: a copy is changed and validated; everything else, optional data
        /// included, stays exactly as it was)</summary>
        public void SetTitle(string title)
        {
            if (InspectOnly) throw new ComposeException("composition.inspect-only", "This plan was made by a newer version; it can be viewed, not changed.");
            var d = (JObj)StrictJson.Clone(doc); ((JObj)d["plan"])["title"] = title;
            var c = new Composition { doc = d }; c.Validate(); doc = d; CanApply = c.CanApply; Unsupported = c.Unsupported; OriginalFile = null;
        }

        /// <summary>what the receiver needs, item by item: their own material, what is ready on this computer, what is missing or does
        /// not match, what this version cannot use. Nothing is fetched, downloaded or changed by asking.</summary>
        public List<ChecklistItem> Checklist(IPublicRegistry reg, Func<string, bool> haveGame, Func<string, string, string, bool> haveBuild, Func<string, string, bool> haveDesign)
        {
            var l = new List<ChecklistItem>();
            void Add(string what, string status, string text) => l.Add(new ChecklistItem { What = what, Status = status, Text = text });
            foreach (var u in Unsupported) Add("This plan", "unsupported", u);
            if (InspectOnly) return l;
            foreach (var i in Arr(doc, "inputs").OfType<JObj>())
            {
                string role = i["role"] as string, version = i["version"] as string, sha = i["sha256"] as string, rf = i["ref"] as string;
                if (role == "console-system") Add("Your console's original software", "supply-your-own", "Choose a verified backup of your own console; the sender's console software is never part of a plan.");
                else if (role == "console-saves") Add("Your saves", "supply-your-own", "Your own saves are used; the sender's saves are never part of a plan.");
                else if (role == "ccs-component" || role == "hakchi-kernel")
                {
                    var name = role == "ccs-component" ? rf : "hakchi-kernel"; var what = Display.Line(name, 40) + " " + Display.Line(version, 32);
                    if (sha != null && !reg.IsPublicBuild(role, name, version, sha)) Add(what, "unknown-build", "This build's fingerprint is not one this program knows; it cannot be used until it is confirmed.");
                    else Add(what, haveBuild(role, name, version) ? "ready" : "missing", haveBuild(role, name, version) ? "Included with this program." : "This version is not available here.");
                }
            }
            foreach (var g in Arr(doc, "games").OfType<JObj>())
            {
                var r = GameRef.Of(g["ref"] as JObj); var sha = g["content_sha256"] as string;
                if (r.IsPlaceholder) { Add(r.DisplayName, "supply-your-own", "A game of the sender's own: choose one of your games for this place, or skip it."); continue; }
                var title = reg.GameTitle(r.Catalog) ?? r.Title ?? r.Catalog;
                if (sha != null && reg.GameTitle(r.Catalog) != null && !reg.IsPublicGameHash(r.Catalog, sha)) Add(Display.Line(title, 80), "mismatched", "The plan names a different version of this game than the catalog lists.");
                else Add(Display.Line(title, 80), haveGame(r.Catalog) ? "ready" : "missing", haveGame(r.Catalog) ? "Your copy will be used." : "Add your own copy of this game, or skip it.");
            }
            foreach (var m in Arr(doc, "mods").OfType<JObj>())
            {
                if (m.Has("placeholder")) { Add("Mod " + Doc.Int(m["placeholder"]), "supply-your-own", "A mod of the sender's own: choose your own, or skip it."); continue; }
                string n = m["name"] as string, v = m["version"] as string, s = m["sha256"] as string, what = Display.Line(n, 64) + " " + Display.Line(v, 32);
                if (!reg.IsPublicBuild("mod", n, v, s)) Add(what, "unknown-build", "This mod build is not one this program knows; it cannot be used until it is confirmed.");
                else Add(what, haveBuild("mod", n, v) ? "ready" : "missing", haveBuild("mod", n, v) ? "Your copy matches." : "Add this exact mod build, or skip it.");
            }
            if (doc["home_design"] is JObj hd)
                Add("HOME design \"" + Display.Line(hd["name"] as string, 60) + "\"", haveDesign(hd["id"] as string, hd["sha256"] as string) ? "ready" : "missing",
                    haveDesign(hd["id"] as string, hd["sha256"] as string) ? "This exact design is here." : "Open the matching .ccshome design file (the plan names it; it does not carry it).");
            return l;
        }

        static List<object> Arr(JObj d, string k) => Doc.Arr(d[k]) ?? new List<object>();
        static ComposeException Refuse(string code, string where, string why) => new ComposeException(code, "This plan was refused: " + where + " " + why + ".");

        // ------------------------------------------------------------------ validation
        void Validate()
        {
            Unsupported = new List<string>(); InspectOnly = false;
            if (doc["format"] as string != Format) throw Refuse("composition.format", "the file", "is not a Console Composer plan");
            var m = Regex.Match(doc["schema"] as string ?? "", "^ccs-composition/([0-9]{1,3})$");
            if (!m.Success) throw Refuse("composition.format", "the file", "does not name a plan schema");
            if (doc["kind"] as string != Kind) throw Refuse("composition.format", "the file", "is not a shared plan (a private plan or package is never shared)");
            if (m.Groups[1].Value != "1") { Unsupported.Add("it was made with a newer plan format (" + Display.Line(doc["schema"] as string, 40) + ")"); InspectOnly = true; }
            var req = doc.Has("requires") ? Doc.Arr(doc["requires"]) ?? throw Refuse("composition.type", "\"requires\"", "is not a list") : new List<object>();
            if (req.Count > 16) throw Refuse("composition.bounds", "\"requires\"", "lists more than 16 capabilities");
            foreach (var r in req)
            {
                if (!(r is string s) || !Regex.IsMatch(s, "^[a-z][a-z0-9-]{0,39}(/[0-9]{1,3})?$")) throw Refuse("composition.type", "\"requires\"", "has an entry that is not a capability name");
                if (!KnownCapabilities.Contains(s)) { Unsupported.Add("it needs a capability this version does not have (" + s + ")"); InspectOnly = true; }
            }
            var plan = doc["plan"] as JObj ?? throw Refuse("composition.type", "\"plan\"", "is missing");
            if (!NewId.Is(plan["id"] as string)) throw Refuse("composition.type", "the plan id", "is not 32 lowercase hex digits");
            Text(plan["title"], "the plan title", 80, true);
            CanApply = !InspectOnly;
            if (InspectOnly) { InspectScan(); return; }

            Keys(doc, "the plan", "format", "schema", "minor", "kind", "requires", "plan", "platform", "starting_point", "install_policy", "environments", "games", "mods", "home_design", "inputs");
            Int(doc, "minor", "the plan", 0, 999, false);
            Keys(plan, "plan", "id", "revision", "title"); Int(plan, "revision", "plan", 1, 1000000, true);
            Enum(doc, "platform", "the plan", Platforms, true); Enum(doc, "starting_point", "the plan", StartingPoints, true); Enum(doc, "install_policy", "the plan", InstallPolicies, true);
            var envs = List(doc, "environments", MaxEnvironments, true); var envIds = new HashSet<string>(); int defaults = 0;
            for (int i = 0; i < envs.Count; i++)
            {
                var w = "environments[" + i + "]"; var e = envs[i] as JObj ?? throw Refuse("composition.type", w, "is not an object");
                Keys(e, w, "id", "kind", "default");
                if (!(e["id"] is string id) || !EnvId.IsMatch(id)) throw Refuse("composition.type", w + ".id", "is not an environment id");
                if (!envIds.Add(id)) throw Refuse("composition.duplicate", w, "uses an id another environment uses");
                Enum(e, "kind", w, EnvironmentKinds, true);
                if (e.Has("default")) { if (!(e["default"] is bool b)) throw Refuse("composition.type", w + ".default", "is not true or false"); if (b) defaults++; }
            }
            if (envs.Count == 0) throw Refuse("composition.type", "\"environments\"", "is empty");
            if (defaults > 1) throw Refuse("composition.duplicate", "\"environments\"", "marks more than one environment as the default");
            string Env(JObj o, string w) { if (!(o["environment"] is string s) || !envIds.Contains(s)) throw Refuse("composition.type", w + ".environment", "does not name one of the plan's environments"); return s; }

            var games = List(doc, "games", MaxGames, false); var gseen = new HashSet<string>();
            for (int i = 0; i < games.Count; i++)
            {
                var w = "games[" + i + "]"; var g = games[i] as JObj ?? throw Refuse("composition.type", w, "is not an object");
                Keys(g, w, "ref", "environment", "content_sha256", "save");
                var r = g["ref"] as JObj ?? throw Refuse("composition.type", w + ".ref", "is missing");
                string key;
                if (r.Has("placeholder")) { Keys(r, w + ".ref", "placeholder"); key = "p" + Int(r, "placeholder", w + ".ref", 1, 99999, true); if (g.Has("content_sha256")) throw Refuse("composition.hash-place", w, "gives a fingerprint for a game of the sender's own (never shared)"); }
                else
                {
                    Keys(r, w + ".ref", "catalog", "title");
                    if (!(r["catalog"] is string c) || !GameRef.CatalogId.IsMatch(c)) throw Refuse(r["catalog"] is string pc && pc.StartsWith("p_") ? "composition.private-game" : "composition.type", w + ".ref.catalog", "is not a public catalog reference (a game of the sender's own is never named in a plan)");
                    key = c; Text(r["title"], w + ".ref.title", 120, false);
                    if (g.Has("content_sha256") && !Sha.Is(g["content_sha256"] as string)) throw Refuse("composition.type", w + ".content_sha256", "is not a SHA-256 fingerprint");
                }
                if (!gseen.Add(key + "@" + Env(g, w))) throw Refuse("composition.duplicate", w, "lists the same game twice in one environment");
                Enum(g, "save", w, new[] { "bring-own", "none" }, false);
            }
            var mods = List(doc, "mods", MaxMods, false);
            for (int i = 0; i < mods.Count; i++)
            {
                var w = "mods[" + i + "]"; var mo = mods[i] as JObj ?? throw Refuse("composition.type", w, "is not an object");
                Env(mo, w);
                if (mo.Has("placeholder")) { Keys(mo, w, "placeholder", "environment"); Int(mo, "placeholder", w, 1, 99999, true); continue; }
                Keys(mo, w, "name", "version", "sha256", "environment");
                if (!(mo["name"] is string n) || !ModName.IsMatch(n)) throw Refuse("composition.type", w + ".name", "is not a mod name (a-z, 0-9, . _ -)");
                if (!(mo["version"] is string v) || !Version.IsMatch(v)) throw Refuse("composition.type", w + ".version", "is not a version");
                if (!Sha.Is(mo["sha256"] as string)) throw Refuse("composition.type", w + ".sha256", "is not the build's SHA-256 fingerprint");
            }
            if (doc.Has("home_design"))
            {
                var hd = doc["home_design"] as JObj ?? throw Refuse("composition.type", "\"home_design\"", "is not an object");
                Keys(hd, "home_design", "id", "revision", "sha256", "name");
                if (!NewId.Is(hd["id"] as string)) throw Refuse("composition.type", "home_design.id", "is not a design id");
                if (!Sha.Is(hd["sha256"] as string)) throw Refuse("composition.type", "home_design.sha256", "is not the design's SHA-256 fingerprint");
                Int(hd, "revision", "home_design", 1, 1000000, true); Text(hd["name"], "home_design.name", 60, true);
            }
            var inputs = List(doc, "inputs", MaxInputs, false);
            for (int i = 0; i < inputs.Count; i++)
            {
                var w = "inputs[" + i + "]"; var inp = inputs[i] as JObj ?? throw Refuse("composition.type", w, "is not an object");
                Keys(inp, w, "role", "supply", "ref", "version", "sha256", "environment");
                var role = Enum(inp, "role", w, InputRoles, true); var supply = Enum(inp, "supply", w, new[] { "recipient", "plan" }, true);
                if (inp.Has("environment")) Env(inp, w);
                if (role == "console-system" || role == "console-saves")
                {
                    if (supply != "recipient") throw Refuse("composition.hash-place", w, "says the plan supplies the console's own software or saves (only the receiver's own can be used)");
                    if (inp.Has("sha256") || inp.Has("ref") || inp.Has("version")) throw Refuse("composition.hash-place", w, "carries a fingerprint or name of console-specific material (that stays in the sender's private package)");
                }
                else if (role != null)
                {
                    if (supply != "plan") throw Refuse("composition.type", w + ".supply", "must be \"plan\" for a public build");
                    if (!(inp["version"] is string v) || !Version.IsMatch(v)) throw Refuse("composition.type", w + ".version", "is missing or not a version");
                    if (role == "ccs-component") Enum(inp, "ref", w, CcsComponents, true); else if (inp.Has("ref")) throw Refuse("composition.key", w, "has \"ref\", which this role does not use");
                    if (inp.Has("sha256") && !Sha.Is(inp["sha256"] as string)) throw Refuse("composition.type", w + ".sha256", "is not a SHA-256 fingerprint");
                }
            }
        }

        void Keys(JObj o, string where, params string[] allowed)
        {
            foreach (var kv in o)
            {
                if (kv.Key == "optional") { if (!(kv.Value is JObj)) throw Refuse("composition.type", where + ".optional", "is not an object"); Inert.Check(kv.Value, where + ".optional", "composition"); continue; }
                if (!allowed.Contains(kv.Key)) throw Refuse("composition.key", where, "has \"" + Display.Line(kv.Key, 40) + "\", which is not part of a shared plan (plans have no links, commands, files or private details)");
            }
        }
        static void Text(object v, string where, int max, bool required)
        {
            if (v == null) { if (required) throw Refuse("composition.type", where, "is missing"); return; }
            if (!(v is string s)) throw Refuse("composition.type", where, "is not text");
            if (required && s.Trim().Length == 0) throw Refuse("composition.type", where, "is empty");
            var (c, w) = PlainText.Check(s, max);
            if (c != null) throw Refuse("composition." + c.Substring(5), where, w);
        }
        static long Int(JObj o, string k, string where, long min, long max, bool required)
        {
            if (!o.Has(k)) { if (required) throw Refuse("composition.type", where + "." + k, "is missing"); return long.MinValue; }
            if (!(o[k] is long v)) throw Refuse("composition.type", where + "." + k, "is not a whole number");
            if (v < min || v > max) throw Refuse("composition.bounds", where + "." + k, "is outside " + min + " to " + max);
            return v;
        }
        string Enum(JObj o, string k, string where, string[] values, bool required)
        {
            if (!o.Has(k)) { if (required) throw Refuse("composition.type", where + "." + k, "is missing"); return null; }
            if (!(o[k] is string s) || !Regex.IsMatch(s, "^[a-z0-9][a-z0-9_-]{0,47}$")) throw Refuse("composition.type", where + "." + k, "is not a name");
            if (!values.Contains(s)) { Unsupported.Add(where + "." + k + " is \"" + s + "\", which this version does not know"); CanApply = false; return null; }
            return s;
        }
        static List<object> List(JObj d, string k, int max, bool required)
        {
            if (!d.Has(k)) { if (required) throw Refuse("composition.type", "\"" + k + "\"", "is missing"); return new List<object>(); }
            var a = Doc.Arr(d[k]) ?? throw Refuse("composition.type", "\"" + k + "\"", "is not a list");
            if (a.Count > max) throw Refuse("composition.bounds", "\"" + k + "\"", "has more than " + max + " entries");
            return a;
        }

        /// <summary>inspect mode: the whole document follows the inert rules (no links, locations, markup, console identities; a
        /// fingerprint or id only as a whole value)</summary>
        void InspectScan()
        {
            void Walk(object v, string where, int depth)
            {
                switch (v)
                {
                    case string s:
                        if (Sha.Is(s) || NewId.Is(s) || GameRef.CatalogId.IsMatch(s)) return;
                        var (c, w) = PlainText.Check(s, 4000, true); if (c != null) throw Refuse("composition." + c.Substring(5), where, w); return;
                    case JObj o: foreach (var kv in o) { if (!Regex.IsMatch(kv.Key, "^[a-z][a-z0-9_-]{0,47}$")) throw Refuse("composition.key", where, "has a member name that is not allowed"); Walk(kv.Value, where + "." + kv.Key, depth + 1); } return;
                    case List<object> a: for (int i = 0; i < a.Count; i++) Walk(a[i], where + "[" + i + "]", depth + 1); return;
                }
            }
            Walk(doc, "the plan", 0);
        }

        internal static Composition FromDocument(JObj d) { var c = new Composition { doc = d }; c.Validate(); return c; }
    }

    public static class CompositionShare
    {
        /// <summary>the shared plan built from the Composer's private draft (see Composition's header); nothing is written</summary>
        public static SharePreview Prepare(CompositionDraft d, IPublicRegistry reg, PrivateValues known)
        {
            var p = new SharePreview(); var pv = new List<string>(known?.Values ?? new List<string>());
            // "{n} screenshot(s)" reads "1 screenshot" or "2 screenshots"
            void Removed(int n, string text) { if (n > 0) p.Removed.Add(text.Replace("{n}", n.ToString("N0", System.Globalization.CultureInfo.InvariantCulture)).Replace("(s)", n == 1 ? "" : "s")); }
            // the draft's own private parts are private values too
            pv.AddRange(new[] { d.SourceConsoleHwid, d.SourceConsoleTag, d.PrivateSourceManifest, Device.ConsoleIdentity.NormalizeHwid(d.SourceConsoleHwid) });
            bool PublicGame(DraftGame g) => g.PersonalId == null && g.CatalogId != null && reg.GameTitle(g.CatalogId) != null;
            pv.AddRange(d.Games.SelectMany(g => new[] { g.PersonalId, g.FileName, g.LocalPath, g.SaveSha256, g.SavePath, PublicGame(g) ? null : g.ContentSha256, PublicGame(g) ? null : g.Title }));
            pv.AddRange(d.Mods.SelectMany(m => reg.IsPublicBuild("mod", m.Name, m.Version, m.Sha256) ? new[] { m.LocalPath } : new[] { m.LocalPath, m.Name, m.Sha256 }));
            pv.AddRange(d.Inputs.SelectMany(i => new[] { i.LocalPath, i.SourceBackupId,
                i.Role == "console-system" || i.Role == "console-saves" || !reg.IsPublicBuild(i.Role, i.Role == "ccs-component" ? i.Ref : "hakchi-kernel", i.Version, i.Sha256) ? i.Sha256 : null }));
            pv.AddRange(d.Screenshots); pv.AddRange(d.Attachments);
            pv = pv.Where(v => !string.IsNullOrWhiteSpace(v) && v.Trim().Length >= 4).Distinct().ToList();

            // the plan's name: the person's own text, shown to the receiver; refused (not silently changed) when it holds a private detail
            var title = string.IsNullOrWhiteSpace(d.Title) ? "Untitled plan" : d.Title.Trim();
            var (tc, tw) = PlainText.Check(title, 80);
            if (tc != null) p.Refusals.Add("Change the plan's name: it " + tw + ".");
            foreach (var v in pv) if (title.IndexOf(v, StringComparison.OrdinalIgnoreCase) >= 0) { p.Refusals.Add("Change the plan's name: it contains one of your private details (\"" + Display.Line(v, 40) + "\")."); break; }

            var doc = new JObj
            {
                { "format", Composition.Format }, { "schema", Composition.Schema }, { "minor", 0L }, { "kind", Composition.Kind }, { "requires", new List<object>() },
                { "plan", new JObj { { "id", NewId.Is(d.PlanId) ? d.PlanId : NewId.Make() }, { "revision", Math.Max(1, d.Revision) }, { "title", title } } },
                { "platform", d.Platform ?? "nes-classic" }, { "starting_point", d.StartingPoint }, { "install_policy", d.InstallPolicy },
            };
            var envMap = new Dictionary<string, string>(); var envs = new List<object>();
            foreach (var e in d.Environments) { var id = "e" + (envMap.Count + 1); envMap[e.Id ?? id] = id; var o = new JObj { { "id", id }, { "kind", e.Kind } }; if (e.Default) o["default"] = true; envs.Add(o); }
            doc["environments"] = envs;
            string EnvOf(string id) => id != null && envMap.TryGetValue(id, out var m) ? m : envMap.Values.FirstOrDefault();

            var games = new List<object>(); var ph = new Dictionary<string, int>(); int own = 0, saveFp = 0, locations = 0;
            foreach (var g in d.Games)
            {
                locations += (g.LocalPath != null ? 1 : 0) + (g.SavePath != null ? 1 : 0);
                if (g.SaveSha256 != null) saveFp++;
                var pub = g.PersonalId == null && g.CatalogId != null ? reg.GameTitle(g.CatalogId) : null;
                JObj r;
                if (pub != null) r = new JObj { { "catalog", g.CatalogId }, { "title", pub } };
                else { var k = g.PersonalId ?? g.CatalogId ?? g.ContentSha256 ?? g.FileName ?? Guid.NewGuid().ToString(); if (!ph.TryGetValue(k, out var n)) { ph[k] = n = ph.Count + 1; own++; } r = new JObj { { "placeholder", (long)n } }; }
                var o = new JObj { { "ref", r }, { "environment", EnvOf(g.Environment) } };
                if (pub != null && g.ContentSha256 != null && reg.IsPublicGameHash(g.CatalogId, g.ContentSha256)) o["content_sha256"] = g.ContentSha256;
                if (g.BringSave) o["save"] = "bring-own";
                if (!games.OfType<JObj>().Any(x => StrictJson.Same(x["ref"], r) && x["environment"] as string == o["environment"] as string)) games.Add(o);
            }
            if (games.Count > 0) doc["games"] = games;
            var mods = new List<object>(); int privMods = 0;
            foreach (var m in d.Mods)
            {
                if (m.LocalPath != null) locations++;
                if (reg.IsPublicBuild("mod", m.Name, m.Version, m.Sha256)) mods.Add(new JObj { { "name", m.Name }, { "version", m.Version }, { "sha256", m.Sha256 }, { "environment", EnvOf(m.Environment) } });
                else mods.Add(new JObj { { "placeholder", (long)++privMods }, { "environment", EnvOf(m.Environment) } });
            }
            if (mods.Count > 0) doc["mods"] = mods;
            if (d.HomeDesign != null) doc["home_design"] = new JObj { { "id", d.HomeDesign.Id }, { "revision", Math.Max(1, d.HomeDesign.Revision) }, { "sha256", d.HomeDesign.Sha256 }, { "name", d.HomeDesign.Name } };
            var inputs = new List<object>(); int sysFp = 0, backups = 0, unconfirmed = 0;
            foreach (var i in d.Inputs)
            {
                if (i.LocalPath != null) locations++;
                if (i.SourceBackupId != null) backups++;
                if (i.Role == "console-system" || i.Role == "console-saves")
                {
                    if (i.Sha256 != null) sysFp++;
                    var o = new JObj { { "role", i.Role }, { "supply", "recipient" } }; if (i.Environment != null) o["environment"] = EnvOf(i.Environment); inputs.Add(o);
                }
                else
                {
                    var name = i.Role == "ccs-component" ? i.Ref : "hakchi-kernel";
                    var o = new JObj { { "role", i.Role }, { "supply", "plan" } }; if (i.Role == "ccs-component") o["ref"] = i.Ref; o["version"] = i.Version;
                    if (i.Sha256 != null) { if (reg.IsPublicBuild(i.Role, name, i.Version, i.Sha256)) o["sha256"] = i.Sha256; else unconfirmed++; }
                    if (i.Environment != null) o["environment"] = EnvOf(i.Environment); inputs.Add(o);
                }
            }
            if (inputs.Count > 0) doc["inputs"] = inputs;

            Removed(string.IsNullOrWhiteSpace(d.Notes) ? 0 : 1, "Your notes");
            Removed(d.SourceConsoleHwid != null || d.SourceConsoleTag != null ? 1 : 0, "This console's identity");
            Removed(d.Screenshots.Count, "{n} screenshot(s)"); Removed(d.Attachments.Count, "{n} attachment(s)");
            Removed(d.PrivateSourceManifest != null ? 1 : 0, "The private list of your source files");
            Removed(own, "{n} of your own games that are not in the public catalog: the receiver sees \"Your game 1\" and so on, and chooses their own");
            Removed(saveFp, "Fingerprints of {n} save(s)");
            Removed(sysFp, "Fingerprints of your console's own software ({n})");
            Removed(backups, "References to {n} of your backups");
            Removed(locations, "{n} file location(s) on this computer");
            Removed(privMods, "{n} mod(s) that are not public builds: the receiver supplies their own");
            Removed(unconfirmed, "{n} build fingerprint(s) this program cannot confirm as public");

            Composition c;
            try { c = Composition.FromDocument(doc); }
            catch (ComposeException ex) { p.Refusals.Add(ex.Message); return p; }
            var bytes = Json.Bytes(doc); var text = Encoding.UTF8.GetString(bytes).ToLowerInvariant();
            foreach (var v in pv)
            {
                var needle = Json.Write(v).Trim().Trim('"').ToLowerInvariant();
                if (text.Contains(needle) && !p.Refusals.Any(r => r.Contains("private details"))) p.Refusals.Add("A private detail was found in the plan (\"" + Display.Line(v, 40) + "\"); nothing was shared.");
            }
            p.Included.Add("Plan \"" + Display.Line(title, 80) + "\": " + Composition.StartingPointName(d.StartingPoint) + ", " + Composition.PolicyName(d.InstallPolicy) + ", " + Units.Count(envs.Count, "software environment"));
            int pubGames = games.OfType<JObj>().Count(g => (g["ref"] as JObj).Has("catalog"));
            if (pubGames > 0) p.Included.Add(Units.Count(pubGames, "game") + " by public catalog reference (titles only; no game files)");
            int pubMods = mods.OfType<JObj>().Count(m => m.Has("name"));
            if (pubMods > 0) p.Included.Add(pubMods + " public mod build(s) by name, version and fingerprint (no mod files)");
            if (d.HomeDesign != null) p.Included.Add("The HOME design \"" + Display.Line(d.HomeDesign.Name, 60) + "\" by its id and fingerprint (the design file itself is shared separately)");
            if (inputs.Count > 0) p.Included.Add(Units.Count(inputs.Count, "input") + ": what the receiver supplies themselves, and the CCS parts this plan uses");
            p.Shared = c; p.Json = bytes; p.CanShare = p.Refusals.Count == 0;
            return p;
        }

        /// <summary>writes a preview that passed (never a private package, a backup or anything else; see ShareGuard)</summary>
        public static void Write(SharePreview p, string path)
        {
            if (p == null || !p.CanShare || p.Json == null) throw new ComposeException("share.refused", "This plan cannot be shared: " + string.Join(" ", p?.Refusals ?? new List<string>()));
            Composition.Parse(p.Json);
            if (File.Exists(path)) throw new ComposeException("share.exists", "A file with that name already exists; choose another name.");
            var tmp = path + ".partial";
            File.WriteAllBytes(tmp, p.Json);
            try { ShareGuard.RequireShareable(tmp); File.Move(tmp, path); }
            finally { if (File.Exists(tmp)) File.Delete(tmp); }
        }
    }
}
