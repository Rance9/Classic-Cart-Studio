// Classic Cart Studio console backup and restore engine. Copyright (C) 2026 Rance9. GPL-3.0-or-later.
// JSON: JObj keeps its keys in the order they were added (manifests and records stay readable); Json.Write renders indented UTF-8;
// Json.Parse reads untrusted text with bounded depth and size (JavaScriptSerializer) into Dictionary/object[] values.
using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;

namespace CcsBackup
{
    public sealed class JObj : IEnumerable<KeyValuePair<string, object>>
    {
        readonly List<KeyValuePair<string, object>> items = new List<KeyValuePair<string, object>>();
        public void Add(string k, object v) { Set(k, v); }
        public JObj Set(string k, object v)
        {
            for (int i = 0; i < items.Count; i++) if (items[i].Key == k) { items[i] = new KeyValuePair<string, object>(k, v); return this; }
            items.Add(new KeyValuePair<string, object>(k, v)); return this;
        }
        public object this[string k] { get { foreach (var kv in items) if (kv.Key == k) return kv.Value; return null; } set => Set(k, value); }
        public bool Has(string k) => items.Any(kv => kv.Key == k);
        public IEnumerator<KeyValuePair<string, object>> GetEnumerator() => items.GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => items.GetEnumerator();
    }

    public static class Json
    {
        public const int MaxText = 8 * 1000 * 1000;

        public static Dictionary<string, object> Parse(string text)
        {
            if (text == null || text.Length > MaxText) throw new FormatException("the JSON text is missing or too large");
            var o = new JavaScriptSerializer { MaxJsonLength = MaxText, RecursionLimit = 32 }.DeserializeObject(text);
            return o as Dictionary<string, object> ?? throw new FormatException("not a JSON object");
        }

        public static string Write(object o) { var sb = new StringBuilder(); W(sb, o, 0); sb.Append('\n'); return sb.ToString(); }
        public static byte[] Bytes(object o) => new UTF8Encoding(false).GetBytes(Write(o));

        static void W(StringBuilder sb, object o, int ind)
        {
            switch (o)
            {
                case null: sb.Append("null"); return;
                case string s: Str(sb, s); return;
                case bool b: sb.Append(b ? "true" : "false"); return;
                case int i: sb.Append(i.ToString(CultureInfo.InvariantCulture)); return;
                case long l: sb.Append(l.ToString(CultureInfo.InvariantCulture)); return;
                case double d: sb.Append(d.ToString("R", CultureInfo.InvariantCulture)); return;
                case decimal m: sb.Append(m.ToString(CultureInfo.InvariantCulture)); return;
                case JObj jo: Obj(sb, jo.Select(kv => kv), ind); return;
                case IDictionary<string, object> d2: Obj(sb, d2, ind); return;
                case IEnumerable e:
                    var list = e.Cast<object>().ToList();
                    if (list.Count == 0) { sb.Append("[]"); return; }
                    if (list.All(x => !(x is JObj) && !(x is IDictionary) && (x is string || !(x is IEnumerable))))
                    { sb.Append('['); for (int k = 0; k < list.Count; k++) { if (k > 0) sb.Append(", "); W(sb, list[k], ind); } sb.Append(']'); return; }
                    sb.Append("[\n");
                    for (int k = 0; k < list.Count; k++) { sb.Append(' ', ind + 2); W(sb, list[k], ind + 2); sb.Append(k + 1 < list.Count ? ",\n" : "\n"); }
                    sb.Append(' ', ind).Append(']'); return;
                default: Str(sb, Convert.ToString(o, CultureInfo.InvariantCulture)); return;
            }
        }

        static void Obj(StringBuilder sb, IEnumerable<KeyValuePair<string, object>> kvs, int ind)
        {
            var l = kvs.ToList();
            if (l.Count == 0) { sb.Append("{}"); return; }
            sb.Append("{\n");
            for (int k = 0; k < l.Count; k++)
            {
                sb.Append(' ', ind + 2); Str(sb, l[k].Key); sb.Append(": "); W(sb, l[k].Value, ind + 2);
                sb.Append(k + 1 < l.Count ? ",\n" : "\n");
            }
            sb.Append(' ', ind).Append('}');
        }

        static void Str(StringBuilder sb, string s)
        {
            sb.Append('"');
            foreach (var c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default: if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4")); else sb.Append(c); break;
                }
            }
            sb.Append('"');
        }

        // ---- reading helpers (untrusted dictionaries)
        public static string S(this Dictionary<string, object> d, string k) => d != null && d.TryGetValue(k, out var v) ? v as string : null;
        public static long L(this Dictionary<string, object> d, string k, long def = -1) => d != null && d.TryGetValue(k, out var v) && (v is int || v is long || v is decimal) ? Convert.ToInt64(v, CultureInfo.InvariantCulture) : def;
        public static bool B(this Dictionary<string, object> d, string k) => d != null && d.TryGetValue(k, out var v) && v is bool b && b;
        public static Dictionary<string, object> O(this Dictionary<string, object> d, string k) => d != null && d.TryGetValue(k, out var v) ? v as Dictionary<string, object> : null;
        public static List<object> A(this Dictionary<string, object> d, string k)
        {
            if (d == null || !d.TryGetValue(k, out var v) || v == null) return null;
            if (v is object[] arr) return arr.ToList();
            if (v is ArrayList al) return al.Cast<object>().ToList();
            return null;
        }
    }
}
