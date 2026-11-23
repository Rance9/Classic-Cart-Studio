// Classic Cart Studio console backup and restore engine. Copyright (C) 2026 Rance9. GPL-3.0-or-later.
// The console storage accounting (tools/platform/ccs-storage-spec.json, embedded byte for byte): the same calculation as
// tools/companion/console_engine/storage_model.py and the website's storage-model.js, which decide tools/platform/storage-vectors.json the
// same way. Account() distinguishes the space available (a reading, or the model's dated reference, with the reserved blocks left out
// once), each new file's length and its whole blocks, files freed when a replacement is in place, room for saves, the safety margin and
// the peak while a new copy and the one it replaces are both present. An unknown size or allowance never counts as zero: the fit is then
// unconfirmed and no write may rely on it.
using CcsBackup.Library;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace CcsBackup.Engine
{
    public static class StorageSpec
    {
        public const string SpecSchema = "ccs-storage-spec/1", AccountSchema = "ccs-storage-account/1";
        public const string NesModel = "nes-classic-edition", SnesModel = "snes-classic-edition";
        static readonly byte[] specBytes; static readonly Dictionary<string, object> spec;

        static StorageSpec()
        {
            using (var s = typeof(StorageSpec).Assembly.GetManifestResourceStream("platform/ccs-storage-spec.json"))
            {
                if (s == null) throw new InvalidOperationException("the storage specification is not in this build");
                using (var m = new MemoryStream()) { s.CopyTo(m); specBytes = m.ToArray(); }
            }
            spec = Json.Parse(new UTF8Encoding(false, true).GetString(specBytes));
            if (spec.S("schema") != SpecSchema) throw new InvalidOperationException("the storage specification is not " + SpecSchema);
        }

        /// <summary>SHA-256 of the embedded specification (the same bytes as tools/platform/ccs-storage-spec.json)</summary>
        public static string SpecSha256 { get { using (var h = System.Security.Cryptography.SHA256.Create()) return Hex.Of(h.ComputeHash(specBytes)); } }

        /// <summary>a fresh copy of the specification (nothing handed out can change the embedded one)</summary>
        public static Dictionary<string, object> Spec => Json.Parse(new UTF8Encoding(false, true).GetString(specBytes));

        /// <summary>the dated reference reading for a console model, or null</summary>
        public static Dictionary<string, object> Reference(string profile) =>
            (Spec.A("references") ?? new List<object>()).OfType<Dictionary<string, object>>().FirstOrDefault(r => r.S("profile") == profile);

        public static long Rounded(long n, long block) => n <= 0 ? 0 : (n + block - 1) / block * block;

        /// <summary>the block size the specification uses when none was read (a policy, labeled as such where it is used)</summary>
        public static long DefaultBlockBytes => spec.O("policies").L("default_block_bytes");

        static bool IsInt(object v) => v is int || v is long;
        static long I(object v) => Convert.ToInt64(v);
        static object Get(Dictionary<string, object> d, string k) => d != null && d.TryGetValue(k, out var v) ? v : null;
        static IEnumerable<object> Items(object v) => v is object[] a ? a : v is ArrayList al ? al.Cast<object>() : v is IEnumerable<object> e && !(v is string) ? e : Enumerable.Empty<object>();
        static bool Truthy(object v) => v is bool b ? b : v != null && !(v is string s && s.Length == 0) && !(IsInt(v) && I(v) == 0);

        /// <summary>how many earlier CCS HOME versions to keep: a whole number from 0 to the specification's maximum; anything else is the default</summary>
        public static int EarlierKeep(object value)
        {
            var pol = spec.O("policies"); long max = pol.L("earlier_home_versions_max"), def = pol.L("earlier_home_versions_default");
            return (int)(IsInt(value) && I(value) >= 0 && I(value) <= max ? I(value) : def);
        }

        /// <summary>which earlier CCS HOME copies stay and which go once a new copy is in use and read back: the same input and result as
        /// storage_model.retention (see there). Only an owned, unchanged, unreferenced earlier copy can go; the most recent earlier versions
        /// stay; with an unfinished installation nothing goes.</summary>
        public static JObj Retention(Dictionary<string, object> inp)
        {
            int keep = EarlierKeep(Get(inp, "keep")); var cur = Get(inp, "current") as string; var prior = Get(inp, "prior") as string;
            var copies = new Dictionary<string, Dictionary<string, object>>(StringComparer.Ordinal);
            foreach (var o in Items(Get(inp, "copies"))) if (o is Dictionary<string, object> c && Get(c, "copy") is string k) copies[k] = c;
            var why = new SortedDictionary<string, string>(StringComparer.Ordinal);
            if (cur != null && copies.ContainsKey(cur)) why[cur] = "in use";
            JObj Result(List<string> remove)
            {
                var w = new JObj(); foreach (var kv in why) w.Add(kv.Key, kv.Value);
                return new JObj { { "keep", (long)keep }, { "kept", copies.Keys.Where(k => !remove.Contains(k)).OrderBy(k => k, StringComparer.Ordinal).Cast<object>().ToList() }, { "remove", remove.Cast<object>().ToList() }, { "why", w } };
            }
            if (Truthy(Get(inp, "unfinished")))
            {
                foreach (var k in copies.Keys) if (!why.ContainsKey(k)) why[k] = "an installation did not finish";
                return Result(new List<string>());
            }
            var order = new List<string>(); var seen = new HashSet<string>(StringComparer.Ordinal); var c0 = prior;
            while (c0 != null && copies.ContainsKey(c0) && !seen.Contains(c0) && c0 != cur) { order.Add(c0); seen.Add(c0); c0 = Get(copies[c0], "previous") as string; }
            order.AddRange(copies.Keys.Where(k => !seen.Contains(k) && k != cur).OrderBy(k => k, StringComparer.Ordinal));
            var eligible = new List<string>();
            foreach (var k in order)
            {
                var x = copies[k];
                if (!Truthy(Get(x, "owned"))) why[k] = "not recorded as this program's";
                else if (!Truthy(Get(x, "unchanged"))) why[k] = "changed since it was installed";
                else if (Truthy(Get(x, "referenced"))) why[k] = "something on the console still names it";
                else eligible.Add(k);
            }
            foreach (var k in eligible.Take(keep)) why[k] = "an earlier version kept";
            return Result(eligible.Skip(keep).ToList());
        }

        /// <summary>One storage account, the same input and result as storage_model.account (see there)</summary>
        public static JObj Account(Dictionary<string, object> inp) => Account(inp, spec);

        public static JObj Account(Dictionary<string, object> inp, Dictionary<string, object> sp)
        {
            var pol = sp.O("policies"); var prof = Get(inp, "profile") as string;
            var refs = (sp.A("references") ?? new List<object>()).OfType<Dictionary<string, object>>().ToList();
            var rf = refs.FirstOrDefault(r => prof != null && r.S("profile") == prof);
            var st = Get(inp, "storage") as Dictionary<string, object>; var unknown = new List<object>();
            long? available; string asrc;
            if (st == null)
            {
                if (rf != null) { available = rf.L("available_bytes"); asrc = "reference"; }
                else { available = null; asrc = "unknown"; unknown.Add("the space available on this console"); }
            }
            else
            {
                var src = st.S("source"); asrc = src == "measured" || src == "simulated" ? src : "measured";
                if (IsInt(Get(st, "available_bytes"))) available = I(st["available_bytes"]);
                else if (IsInt(Get(st, "free_bytes")) && IsInt(Get(st, "reserved_bytes"))) available = Math.Max(0, I(st["free_bytes"]) - I(st["reserved_bytes"]));
                else { available = null; unknown.Add("the space available on this console"); }
            }
            long block; string bsrc;
            if (st != null && IsInt(Get(st, "block_bytes")) && I(st["block_bytes"]) > 0) { block = I(st["block_bytes"]); bsrc = "measured"; }
            else if (rf != null) { block = rf.L("block_bytes"); bsrc = "reference"; }
            else { block = pol.L("default_block_bytes"); bsrc = "default"; }
            var seen = new HashSet<string>(StringComparer.Ordinal); var rows = new List<object>(); var steps = new List<(long a, long f)>();
            bool anyUnknownRow = false;
            foreach (var o in Items(Get(inp, "items")))
            {
                var it = o as Dictionary<string, object>; if (it == null) continue;
                var est = Get(it, "estimate") as Dictionary<string, object>;
                string kind = Truthy(Get(it, "estimated")) || Get(it, "estimate") != null ? "estimate" : "exact"; long lg = 0, al = 0;
                if (Get(it, "estimate") != null)
                {
                    if (est != null && IsInt(Get(est, "logical_bytes")) && IsInt(Get(est, "allocated_bytes"))) { lg += I(est["logical_bytes"]); al += I(est["allocated_bytes"]); }
                    else kind = "unknown";
                }
                foreach (var fo in Items(Get(it, "files")))
                {
                    var f = fo as Dictionary<string, object>; if (f == null) continue;
                    var k = Get(f, "key") as string;
                    if (k != null) { if (seen.Contains(k)) continue; seen.Add(k); }
                    var b = Get(f, "bytes");
                    if (!IsInt(b) || I(b) < 0) { kind = "unknown"; continue; }
                    lg += I(b); al += Rounded(I(b), block);
                }
                long fr = 0; foreach (var b in Items(Get(it, "replaces"))) if (IsInt(b)) fr += Rounded(I(b), block);
                string label = Get(it, "label") as string, category = Get(it, "category") as string;
                if (kind == "unknown") { anyUnknownRow = true; unknown.Add(!string.IsNullOrEmpty(label) ? label : !string.IsNullOrEmpty(category) ? category : "a file"); }
                rows.Add(new JObj { { "category", category }, { "label", label }, { "kind", kind }, { "logical_bytes", lg }, { "allocated_bytes", al }, { "freed_bytes", fr }, { "what", Get(it, "what") as string } });
                steps.Add((al, fr));
            }
            long run = 0, peak = 0;
            foreach (var (a, f) in steps) { peak = Math.Max(peak, run + a); run += a - f; }
            long removed = 0; var gone = new HashSet<string>(StringComparer.Ordinal);
            foreach (var o in Items(Get(inp, "removed_after")))
            {
                var r = o as Dictionary<string, object>;
                var rk = Get(r, "key") as string;   // a removal whose key was counted already is not counted again
                if (rk != null) { if (gone.Contains(rk)) continue; gone.Add(rk); }
                if (IsInt(Get(r, "allocated_bytes"))) removed += I(r["allocated_bytes"]);
                else { var l = Get(r, "label") as string; unknown.Add(!string.IsNullOrEmpty(l) ? l : "a copy to remove"); }
            }
            run -= removed;
            long logical = rows.Cast<JObj>().Sum(r => (long)r["logical_bytes"]), allocated = rows.Cast<JObj>().Sum(r => (long)r["allocated_bytes"]);
            bool nothing = allocated == 0 && !anyUnknownRow;
            var sv = pol.O("saves_allowance_bytes"); long? saves = prof != null && sv != null && sv.TryGetValue(prof, out var svv) && IsInt(svv) ? I(svv) : (long?)null;
            if (saves == null && !nothing) unknown.Add("room for saves and suspend points");
            long margin = nothing ? 0 : pol.L("margin_fixed_bytes") + allocated * pol.L("margin_fraction_ppm") / 1000000;
            long extra = nothing ? 0 : (saves ?? 0) + margin; long steady = run + extra, peakTotal = Math.Max(peak, run) + extra;
            string status;
            if (available == null) status = "unconfirmed";
            else if (steady > available) status = "over";
            else if (peakTotal > available) status = "over-at-peak";
            else if (unknown.Count > 0) status = "unconfirmed";
            else if (available - peakTotal < margin) status = "near";
            else status = "fits";
            return new JObj
            {
                { "schema", AccountSchema }, { "profile", prof },
                { "reference", rf != null && asrc == "reference" ? new JObj { { "date", rf.S("date") }, { "what", rf.S("what") } } : null },
                { "available_bytes", available }, { "available_source", asrc }, { "block_bytes", block }, { "block_source", bsrc }, { "rows", rows },
                { "logical_bytes", logical }, { "allocated_bytes", allocated }, { "freed_bytes", rows.Cast<JObj>().Sum(r => (long)r["freed_bytes"]) }, { "removed_bytes", removed },
                { "saves_allowance_bytes", nothing ? (long?)0 : saves }, { "margin_bytes", margin }, { "steady_bytes", steady }, { "peak_bytes", peakTotal },
                { "remaining_bytes", available == null ? null : (long?)(available - peakTotal) }, { "unknown", unknown }, { "status", status },
                { "write_ok", status == "fits" || status == "near" },
            };
        }
    }
}
