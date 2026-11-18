// Classic Cart Studio console backup and restore engine. Copyright (C) 2026 Rance9. GPL-3.0-or-later.
// A strict JSON reader for files other people send (Console Composer's formats). RFC 8259 only: no comments, trailing commas, NaN or
// single quotes; strict UTF-8 without a byte order mark; bounded bytes, depth, values and string length; a member name that repeats in
// one object (also when it differs only in case) is refused, so two readers can never see two different values. Objects come back as
// JObj in file order (Json.Write writes them back in that order), arrays as List<object>, numbers as long (whole numbers) or decimal
// (anything else, with its written scale), so data this program does not understand survives a round trip unchanged.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace CcsBackup.Compose
{
    public sealed class JsonLimits
    {
        public int MaxBytes = 1000000, MaxDepth = 24, MaxValues = 100000, MaxString = 100000;
        public static JsonLimits Document => new JsonLimits();
    }

    public static class StrictJson
    {
        static readonly UTF8Encoding Utf8 = new UTF8Encoding(false, true);

        public static JObj ParseObject(byte[] utf8, JsonLimits lim, string what)
        {
            var v = Parse(utf8, lim, what);
            return v as JObj ?? throw new ComposeException("json.syntax", what + " is not a JSON object");
        }

        public static object Parse(byte[] utf8, JsonLimits lim, string what)
        {
            lim = lim ?? JsonLimits.Document;
            if (utf8 == null) throw new ComposeException("json.syntax", what + " is missing");
            if (utf8.Length > lim.MaxBytes) throw new ComposeException("json.size", what + " is larger than " + Units.Size(lim.MaxBytes));
            if (utf8.Length >= 3 && utf8[0] == 0xEF && utf8[1] == 0xBB && utf8[2] == 0xBF) throw new ComposeException("json.encoding", what + " starts with a byte order mark");
            string text;
            try { text = Utf8.GetString(utf8); }
            catch (ArgumentException) { throw new ComposeException("json.encoding", what + " is not valid UTF-8 text"); }
            var p = new P { s = text, lim = lim, what = what };
            p.Ws(); var v = p.Value(0); p.Ws();
            if (p.i != text.Length) throw p.Err("json.syntax", "has text after its end");
            return v;
        }

        sealed class P
        {
            public string s, what; public int i, values; public JsonLimits lim;
            public ComposeException Err(string code, string m) => new ComposeException(code, what + " " + m + " (at character " + i + ")");
            public void Ws() { while (i < s.Length && (s[i] == ' ' || s[i] == '\t' || s[i] == '\n' || s[i] == '\r')) i++; }
            char Peek() => i < s.Length ? s[i] : '\0';

            public object Value(int depth)
            {
                if (++values > lim.MaxValues) throw Err("json.size", "has more than " + lim.MaxValues + " values");
                char c = Peek();
                switch (c)
                {
                    case '{': return Object(depth + 1);
                    case '[': return Array(depth + 1);
                    case '"': return Str();
                    case 't': Lit("true"); return true;
                    case 'f': Lit("false"); return false;
                    case 'n': Lit("null"); return null;
                    default: if (c == '-' || c >= '0' && c <= '9') return Number(); throw Err("json.syntax", "is not valid JSON");
                }
            }

            void Lit(string w) { if (string.CompareOrdinal(s, i, w, 0, w.Length) != 0) throw Err("json.syntax", "is not valid JSON"); i += w.Length; }

            JObj Object(int depth)
            {
                if (depth > lim.MaxDepth) throw Err("json.depth", "is nested more than " + lim.MaxDepth + " levels deep");
                i++; var o = new JObj(); var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                Ws(); if (Peek() == '}') { i++; return o; }
                while (true)
                {
                    Ws(); if (Peek() != '"') throw Err("json.syntax", "has an object member without a quoted name");
                    var k = Str();
                    if (!seen.Add(k)) throw Err("json.duplicate", "names the member \"" + Display.Line(k, 40) + "\" twice in one object");
                    Ws(); if (Peek() != ':') throw Err("json.syntax", "is missing a ':'"); i++;
                    Ws(); o.Add(k, Value(depth)); Ws();
                    if (Peek() == ',') { i++; continue; }
                    if (Peek() == '}') { i++; return o; }
                    throw Err("json.syntax", "is missing a ',' or '}'");
                }
            }

            List<object> Array(int depth)
            {
                if (depth > lim.MaxDepth) throw Err("json.depth", "is nested more than " + lim.MaxDepth + " levels deep");
                i++; var a = new List<object>();
                Ws(); if (Peek() == ']') { i++; return a; }
                while (true)
                {
                    Ws(); a.Add(Value(depth)); Ws();
                    if (Peek() == ',') { i++; continue; }
                    if (Peek() == ']') { i++; return a; }
                    throw Err("json.syntax", "is missing a ',' or ']'");
                }
            }

            string Str()
            {
                i++; var sb = new StringBuilder();
                while (true)
                {
                    if (i >= s.Length) throw Err("json.syntax", "has text that never ends");
                    char c = s[i++];
                    if (c == '"') break;
                    if (c < 0x20) throw Err("json.syntax", "has a control character inside text");
                    if (c == '\\')
                    {
                        if (i >= s.Length) throw Err("json.syntax", "has text that never ends");
                        char e = s[i++];
                        switch (e)
                        {
                            case '"': sb.Append('"'); break; case '\\': sb.Append('\\'); break; case '/': sb.Append('/'); break;
                            case 'b': sb.Append('\b'); break; case 'f': sb.Append('\f'); break; case 'n': sb.Append('\n'); break;
                            case 'r': sb.Append('\r'); break; case 't': sb.Append('\t'); break;
                            case 'u':
                                if (i + 4 > s.Length || !int.TryParse(s.Substring(i, 4), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out int u)) throw Err("json.syntax", "has a broken \\u escape");
                                i += 4; sb.Append((char)u); break;
                            default: throw Err("json.syntax", "has an unknown escape");
                        }
                    }
                    else sb.Append(c);
                    if (sb.Length > lim.MaxString) throw Err("json.size", "has text longer than " + lim.MaxString + " characters");
                }
                var r = sb.ToString();
                for (int k = 0; k < r.Length; k++)   // escapes can spell an unpaired surrogate: refused (the text could not be written back)
                {
                    if (char.IsHighSurrogate(r[k])) { if (k + 1 < r.Length && char.IsLowSurrogate(r[k + 1])) { k++; continue; } throw Err("json.encoding", "has a broken character"); }
                    if (char.IsLowSurrogate(r[k])) throw Err("json.encoding", "has a broken character");
                }
                return r;
            }

            object Number()
            {
                int st = i;
                if (Peek() == '-') i++;
                if (Peek() == '0') i++;
                else if (Peek() >= '1' && Peek() <= '9') while (Peek() >= '0' && Peek() <= '9') i++;
                else throw Err("json.number", "has a broken number");
                bool whole = true;
                if (Peek() == '.') { whole = false; i++; if (!(Peek() >= '0' && Peek() <= '9')) throw Err("json.number", "has a broken number"); while (Peek() >= '0' && Peek() <= '9') i++; }
                if (Peek() == 'e' || Peek() == 'E')
                {
                    whole = false; i++; if (Peek() == '+' || Peek() == '-') i++;
                    if (!(Peek() >= '0' && Peek() <= '9')) throw Err("json.number", "has a broken number");
                    while (Peek() >= '0' && Peek() <= '9') i++;
                }
                var t = s.Substring(st, i - st);
                if (t.Length > 40) throw Err("json.number", "has a number with too many digits");
                if (whole && long.TryParse(t, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out long l)) return l;
                if (decimal.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out decimal d)) return d;
                throw Err("json.number", "has a number outside the supported range");
            }
        }

        /// <summary>the same JSON value (objects: the same members with the same values, in the same order)</summary>
        public static bool Same(object a, object b)
        {
            if (a == null || b == null) return a == null && b == null;
            if (a is JObj oa && b is JObj ob)
            {
                var la = oa.ToList(); var lb = ob.ToList();
                if (la.Count != lb.Count) return false;
                for (int k = 0; k < la.Count; k++) if (la[k].Key != lb[k].Key || !Same(la[k].Value, lb[k].Value)) return false;
                return true;
            }
            if (a is List<object> xa && b is List<object> xb) return xa.Count == xb.Count && xa.Zip(xb, Same).All(x => x);
            if (a is string || b is string) return a is string sa && b is string sb && sa == sb;
            if (a is bool || b is bool) return a is bool ba && b is bool bb && ba == bb;
            if ((a is long || a is decimal) && (b is long || b is decimal)) return Convert.ToDecimal(a, CultureInfo.InvariantCulture) == Convert.ToDecimal(b, CultureInfo.InvariantCulture);
            return false;
        }

        public static object Clone(object v)
        {
            switch (v)
            {
                case JObj o: { var n = new JObj(); foreach (var kv in o) n.Add(kv.Key, Clone(kv.Value)); return n; }
                case List<object> a: return a.Select(Clone).ToList();
                default: return v;
            }
        }
    }
}
