// Classic Cart Studio console backup and restore engine. Copyright (C) 2026 Rance9. GPL-3.0-or-later.
// The platform registry and its one decision rule (tools/platform/ccs-platform-registry.json, embedded byte for byte).
// Independent identifiers say what a file or a request is for: game system, core, console
// model, console software, installation method, storage layout; plus controllers, boot options, save formats, drivers and capabilities.
// An entry is "supported" (this version may use it; every gate and approval still applies) or "reserved" (named, and denied everywhere).
//   ResolveTarget   a document's optional ccs_target (ccs-target/1). Absent: the document kind's NES defaults (files written before the
//                   field existed are NES, unchanged). Present: its schema, its fields and every identifier must be known and supported.
//                   The document's own entries may each carry game_system: it must be supported too.
//   CheckRequest    a worker request: any identifier it names (directly, in ccs_target or as a provider) must be supported. A request
//                   that names none is the NES default, as every request was before.
//   ModelForBoard   the console model a board answers with, only when that model is supported.
// The registry is read once from the assembly. No file, request, URL, setting or environment variable can add an entry or change a
// status: there is no API to do it, and every list handed out is a copy.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace CcsBackup.Compose
{
    public sealed class RegistryEntry
    {
        internal readonly Dictionary<string, object> Raw;
        internal RegistryEntry(Dictionary<string, object> raw) { Raw = raw; }
        public string Kind => Raw.S("kind");
        public string Id => Raw.S("id");
        public string Status => Raw.S("status");
        public bool Supported => Status == "supported";
        public string Get(string key) => Raw.S(key);
        public IReadOnlyList<string> List(string key) => (Raw.A(key) ?? new List<object>()).OfType<string>().ToList();
        public bool Flag(string key) => Raw.B(key);
    }

    /// <summary>One decision: Ok, or a refusal Code (damaged | target-schema | unknown-field | unknown-id | reserved | inconsistent) with a
    /// plain Text. Target: the identifiers the document is for (its defaults filled in); Legacy: it had no ccs_target.</summary>
    public sealed class TargetDecision
    {
        public bool Ok; public string Code, Text, Field, Value; public bool Legacy;
        public Dictionary<string, string> Target = new Dictionary<string, string>();
        public JObj ToJson() => new JObj { { "ok", Ok }, { "code", Code }, { "field", Field }, { "value", Value }, { "legacy", Legacy }, { "target", Target.ToDictionary(k => k.Key, k => (object)k.Value) } };
    }

    public static class Capabilities
    {
        public const string RegistrySchema = "ccs-platform-registry/1", TargetSchema = "ccs-target/1", TargetKey = "ccs_target", EntryKey = "game_system";
        public const string DocumentText = "This file is for a game system or console that this version of Classic Cart Studio does not support, so it was not used. Nothing was changed.";
        public const string RequestText = "This request names a game system, console or feature that this version does not support. Nothing was done.";
        /// <summary>the ccs_target fields, in the order they are checked, and the registry kind each one names</summary>
        public static readonly IReadOnlyList<(string field, string kind)> Fields = new[]
        { ("game_system", "game-system"), ("core", "core"), ("physical_model", "physical-model"), ("firmware", "firmware"), ("method", "method"), ("layout", "layout") };
        /// <summary>request keys that name one registry entry each (checked after ccs_target and the Fields), and their kinds</summary>
        public static readonly IReadOnlyList<(string key, string kind)> RequestKeys = new[]
        { ("capability", "capability"), ("boot_option", "boot-option"), ("input", "input"), ("save_adapter", "save-adapter"), ("driver", "driver") };

        static readonly byte[] registryBytes; static readonly Dictionary<string, object> registry;
        static readonly List<RegistryEntry> entries; static readonly Dictionary<string, Dictionary<string, object>> defaults;

        static Capabilities()
        {
            using (var s = typeof(Capabilities).Assembly.GetManifestResourceStream("platform/ccs-platform-registry.json"))
            {
                if (s == null) throw new InvalidOperationException("the platform registry is not in this build");
                using (var m = new MemoryStream()) { s.CopyTo(m); registryBytes = m.ToArray(); }
            }
            registry = Json.Parse(new UTF8Encoding(false, true).GetString(registryBytes));
            if (registry.S("schema") != RegistrySchema) throw new InvalidOperationException("the platform registry is not " + RegistrySchema);
            entries = (registry.A("entries") ?? new List<object>()).OfType<Dictionary<string, object>>().Select(e => new RegistryEntry(e)).ToList();
            defaults = new Dictionary<string, Dictionary<string, object>>();
            var dd = registry.O("document_defaults");
            if (dd != null) foreach (var kv in dd) if (kv.Value is Dictionary<string, object> d) defaults[kv.Key] = d;
        }

        /// <summary>SHA-256 of the embedded registry (the same bytes as tools/platform/ccs-platform-registry.json)</summary>
        public static string RegistrySha256 { get { using (var h = System.Security.Cryptography.SHA256.Create()) return Hex.Of(h.ComputeHash(registryBytes)); } }
        public static IReadOnlyList<RegistryEntry> Entries => entries.ToList();
        public static IReadOnlyList<string> Kinds => entries.Select(e => e.Kind).Distinct().ToList();
        public static IReadOnlyList<string> DocumentKinds => defaults.Keys.ToList();

        public static RegistryEntry Entry(string kind, string id) => entries.FirstOrDefault(e => e.Kind == kind && e.Id == id);
        /// <summary>true only for a registered, supported entry (a reserved or unknown one is never allowed)</summary>
        public static bool Allowed(string kind, string id) => Entry(kind, id)?.Supported == true;
        public static IReadOnlyList<string> SupportedIds(string kind) => entries.Where(e => e.Kind == kind && e.Supported).Select(e => e.Id).ToList();
        /// <summary>a controller definition's actions, in order (nes-pad: up, down, left, right, a, b, select, start)</summary>
        public static IReadOnlyList<string> InputActions(string id) => Entry("input", id)?.List("actions") ?? new List<string>();

        /// <summary>the supported console model whose boards include this one; null for any other board (unknown or reserved)</summary>
        public static string ModelForBoard(string board)
        {
            if (string.IsNullOrEmpty(board)) return null;
            return entries.FirstOrDefault(e => e.Kind == "physical-model" && e.Supported && e.List("boards").Contains(board))?.Id;
        }

        public static IReadOnlyDictionary<string, string> Defaults(string documentKind)
        {
            if (!defaults.TryGetValue(documentKind ?? "", out var d)) throw new ArgumentException("no defaults for document kind " + documentKind);
            return d.Where(kv => kv.Value is string).ToDictionary(kv => kv.Key, kv => (string)kv.Value);
        }

        static TargetDecision Deny(TargetDecision d, string code, string field = null, string value = null, string text = DocumentText) { d.Ok = false; d.Code = code; d.Field = field; d.Value = value; d.Text = text; return d; }

        /// <summary>the decision for one document: doc is the parsed JSON object; entryLists are the document's lists whose items may carry
        /// game_system (a collection's selections, a saves file's games). Every refusal names its reason; nothing is ever assumed to be NES
        /// except a document with no ccs_target at all (a file written before the field existed).</summary>
        public static TargetDecision ResolveTarget(Dictionary<string, object> doc, string documentKind, params IEnumerable<object>[] entryLists)
        {
            var d = new TargetDecision();
            if (!defaults.ContainsKey(documentKind ?? "")) throw new ArgumentException("no defaults for document kind " + documentKind);
            if (doc == null) return Deny(d, "damaged");
            foreach (var kv in Defaults(documentKind)) d.Target[kv.Key] = kv.Value;
            if (!doc.TryGetValue(TargetKey, out var tv)) d.Legacy = true;
            else
            {
                if (!(tv is Dictionary<string, object> t)) return Deny(d, "damaged", TargetKey);
                if (!(t.TryGetValue("schema", out var sch) && sch is string ss && ss == TargetSchema)) return Deny(d, "target-schema", "schema", sch as string);
                foreach (var k in t.Keys) if (k != "schema" && !Fields.Any(f => f.field == k)) return Deny(d, "unknown-field", k);
                foreach (var (field, _) in Fields)
                {
                    if (!t.TryGetValue(field, out var v)) continue;
                    if (!(v is string sv) || sv.Length == 0) return Deny(d, "damaged", field);
                    d.Target[field] = sv;
                }
            }
            foreach (var (field, kind) in Fields)
            {
                if (!d.Target.TryGetValue(field, out var id)) continue;
                var e = Entry(kind, id);
                if (e == null) return Deny(d, "unknown-id", field, id);
                if (!e.Supported) return Deny(d, "reserved", field, id);
            }
            if (d.Target.TryGetValue("core", out var core) && d.Target.TryGetValue("game_system", out var gs) && Entry("core", core).Get("game_system") != gs) return Deny(d, "inconsistent", "core", core);
            if (d.Target.TryGetValue("physical_model", out var pm))
                foreach (var f in new[] { ("firmware", "firmware"), ("method", "method"), ("layout", "layout") })
                    if (d.Target.TryGetValue(f.Item1, out var x) && Entry(f.Item2, x).Get("physical_model") != pm) return Deny(d, "inconsistent", f.Item1, x);
            foreach (var list in entryLists)
            {
                if (list == null) continue;
                foreach (var item in list)
                {
                    if (!(item is Dictionary<string, object> it) || !it.TryGetValue(EntryKey, out var v)) continue;
                    if (!(v is string sv) || sv.Length == 0) return Deny(d, "damaged", EntryKey);
                    var e = Entry("game-system", sv);
                    if (e == null) return Deny(d, "unknown-id", EntryKey, sv);
                    if (!e.Supported) return Deny(d, "reserved", EntryKey, sv);
                    if (d.Target.TryGetValue("game_system", out var own) && own != sv) return Deny(d, "inconsistent", EntryKey, sv);
                }
            }
            d.Ok = true; d.Code = "ok"; return d;
        }

        /// <summary>the decision for one worker request (its arguments): ccs_target, the Fields named directly, the RequestKeys and a
        /// provider {kind, id} must all be supported. A request that names none of them is the NES default (Ok).</summary>
        public static TargetDecision CheckRequest(Dictionary<string, object> req)
        {
            var d = new TargetDecision();
            if (req == null) return Deny(d, "damaged", null, null, RequestText);
            var asDoc = new Dictionary<string, object>();
            if (req.TryGetValue(TargetKey, out var tv)) asDoc[TargetKey] = tv;
            var r = ResolveTarget(asDoc, "worker-request");
            if (!r.Ok) { r.Text = RequestText; return r; }
            foreach (var (field, kind) in Fields.Concat(RequestKeys))
            {
                if (!req.TryGetValue(field, out var v)) continue;
                if (!(v is string sv) || sv.Length == 0) return Deny(r, "damaged", field, null, RequestText);
                var e = Entry(kind, sv);
                if (e == null) return Deny(r, "unknown-id", field, sv, RequestText);
                if (!e.Supported) return Deny(r, "reserved", field, sv, RequestText);
            }
            if (req.TryGetValue("provider", out var pv))
            {
                if (!(pv is Dictionary<string, object> p) || !(p.TryGetValue("kind", out var pk) && pk is string k && k.Length > 0) || !(p.TryGetValue("id", out var pi) && pi is string i && i.Length > 0))
                    return Deny(r, "damaged", "provider", null, RequestText);
                var e = Entry(k, i);
                if (e == null) return Deny(r, "unknown-id", "provider", k + "/" + i, RequestText);
                if (!e.Supported) return Deny(r, "reserved", "provider", k + "/" + i, RequestText);
            }
            r.Ok = true; r.Code = "ok"; r.Text = null; return r;
        }
    }
}
