// Classic Cart Studio console backup and restore engine. Copyright (C) 2026 Rance9. GPL-3.0-or-later.
// Console Composer's file formats (COMPOSER-FORMATS.md), the shared rules:
//  - ComposeException: every refusal has a stable code ("zip.traversal", "design.key", "composition.console-id", ...) for the interface and
//    the tests, and a plain reason a person can read.
//  - PlainText: the one rule for text a file carries (names, labels, credits, titles): no control or invisible formatting characters, no
//    unpaired surrogates, Unicode normal form C, a length limit, and nothing that is a link, a file location, markup, a console identity
//    or a file fingerprint. Text from a file is only ever data: it is never run, fetched, opened or interpreted.
//  - Display: escapes any text taken from a file before it is shown (HTML, or one plain line for a window).
//  - Inert: what unknown optional data ("optional" objects, kept for newer versions) may be: plain values, bounded in depth, count and size.
//  - Units: sizes for people in decimal units from the byte count (1 MB = 1,000,000 bytes), as everywhere else in Classic Cart Studio.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace CcsBackup.Compose
{
    public sealed class ComposeException : Exception
    {
        public readonly string Code;
        public ComposeException(string code, string text) : base(text) { Code = code; }
    }

    public static class PlainText
    {
        static readonly Regex Url = new Regex(@"://|^\s*(javascript|vbscript|data|file|blob|about|mailto|ms-[a-z0-9-]+|shell|res|jar|smb|ftp|https?)\s*:|\bwww\.", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        static readonly Regex PathLike = new Regex(@"(?i)(^|[\s""'(=])[a-z]:[\\/]|^[a-z]:$|^[\\/]|^~[\\/]|\\|\.\.[\\/]|%[a-z_][a-z0-9_]*%|\$env:|^\.{1,2}$", RegexOptions.CultureInvariant);
        static readonly Regex Markup = new Regex(@"<\s*[A-Za-z!/?]", RegexOptions.CultureInvariant);
        static readonly Regex HexRun = new Regex(@"[0-9A-Fa-f]{16,}", RegexOptions.CultureInvariant);
        // the form the console reports its identity in (four groups of eight hex digits); written without separators it is a 32-digit hex
        // run, which LooksLikeFingerprint refuses
        static readonly Regex ConsoleId = new Regex(@"(?i)[0-9a-f]{8}[\s:_-][0-9a-f]{8}[\s:_-][0-9a-f]{8}[\s:_-][0-9a-f]{8}", RegexOptions.CultureInvariant);

        public static bool LooksLikeUrl(string s) => s != null && Url.IsMatch(s);
        public static bool LooksLikePath(string s) => s != null && PathLike.IsMatch(s);
        public static bool LooksLikeMarkup(string s) => s != null && Markup.IsMatch(s);
        public static bool LooksLikeConsoleId(string s) => s != null && ConsoleId.IsMatch(s);
        public static bool LooksLikeFingerprint(string s) => s != null && HexRun.IsMatch(s);

        /// <summary>(code, reason) when s is not acceptable plain text of at most max characters; (null, null) when it is. multiline
        /// allows line breaks (\n only). Every class of problem has its own code (text.length, text.control, text.url, ...).</summary>
        public static (string code, string why) Check(string s, int max, bool multiline = false)
        {
            if (s == null) return ("text.type", "is not text");
            if (s.Length > max) return ("text.length", "is longer than " + max + " characters");
            for (int i = 0; i < s.Length; i++)
            {
                char c = s[i];
                if (char.IsHighSurrogate(c)) { if (i + 1 >= s.Length || !char.IsLowSurrogate(s[i + 1])) return ("text.encoding", "has a broken character"); i++; continue; }
                if (char.IsLowSurrogate(c)) return ("text.encoding", "has a broken character");
                if (c == '\n' && multiline) continue;
                var cat = char.GetUnicodeCategory(c);
                if (cat == UnicodeCategory.Control) return ("text.control", "has a control character");
                if (cat == UnicodeCategory.Format || cat == UnicodeCategory.LineSeparator || cat == UnicodeCategory.ParagraphSeparator) return ("text.control", "has an invisible formatting character");
                if (cat == UnicodeCategory.PrivateUse || cat == UnicodeCategory.OtherNotAssigned) return ("text.control", "has a character that is not defined");
            }
            if (!s.IsNormalized(NormalizationForm.FormC)) return ("text.encoding", "is not in Unicode normal form C");
            if (LooksLikeUrl(s)) return ("text.url", "contains a web or file link (links are never followed, and a file may not carry one)");
            if (LooksLikePath(s)) return ("text.path", "contains a file or folder location");
            if (LooksLikeMarkup(s)) return ("text.markup", "contains markup (text is shown as plain text only)");
            if (LooksLikeConsoleId(s)) return ("text.console-id", "contains what looks like a console's identity");
            if (LooksLikeFingerprint(s)) return ("text.fingerprint", "contains what looks like a file fingerprint (a hash)");
            return (null, null);
        }
    }

    public static class Display
    {
        /// <summary>text from a file, safe inside HTML element content or a quoted attribute; invisible formatting characters are shown as
        /// U+FFFD and the result is cut to max characters</summary>
        public static string Html(string s, int max = 400)
        {
            var sb = new StringBuilder();
            foreach (var c in Clean(s, max))
                switch (c)
                {
                    case '&': sb.Append("&amp;"); break;
                    case '<': sb.Append("&lt;"); break;
                    case '>': sb.Append("&gt;"); break;
                    case '"': sb.Append("&quot;"); break;
                    case '\'': sb.Append("&#39;"); break;
                    case '`': sb.Append("&#96;"); break;
                    default: sb.Append(c); break;
                }
            return sb.ToString();
        }

        /// <summary>text from a file as one plain line for a window or a report: control and formatting characters replaced, whitespace
        /// collapsed, cut to max characters with an ellipsis</summary>
        public static string Line(string s, int max = 120)
        {
            var t = Regex.Replace(Clean(s, int.MaxValue).Replace('\n', ' '), @"\s+", " ").Trim();
            return t.Length <= max ? t : t.Substring(0, Math.Max(0, max - 1)).TrimEnd() + "…";
        }

        static string Clean(string s, int max)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var sb = new StringBuilder();
            for (int i = 0; i < s.Length && sb.Length < max; i++)
            {
                char c = s[i];
                if (char.IsHighSurrogate(c) && i + 1 < s.Length && char.IsLowSurrogate(s[i + 1])) { sb.Append(c).Append(s[++i]); continue; }
                if (char.IsSurrogate(c)) { sb.Append('�'); continue; }
                var cat = char.GetUnicodeCategory(c);
                if (c == '\n') sb.Append('\n');
                else if (cat == UnicodeCategory.Control || cat == UnicodeCategory.Format || cat == UnicodeCategory.LineSeparator || cat == UnicodeCategory.ParagraphSeparator) sb.Append('�');
                else sb.Append(c);
            }
            return sb.ToString();
        }
    }

    /// <summary>Unknown optional data: kept exactly (open, edit something else, save: it is still there), never interpreted. Allowed:
    /// objects (keys a-z, 0-9, _ and -; at most 64 members), arrays (at most 64 items), plain text (PlainText, at most 500 characters), whole or
    /// decimal numbers, true/false/null; at most 6 levels deep inside the "optional" object; at most 64,000 bytes per "optional" object as written (the file as a whole has its own limit).
    /// fingerprintOk: a 64-hex text the file's own format allows there (a design's own picture), checked by the caller.</summary>
    public static class Inert
    {
        public const int MaxBytes = 64000, MaxDepth = 6, MaxItems = 64, MaxString = 500;
        static readonly Regex Key = new Regex("^[a-z][a-z0-9_-]{0,47}$", RegexOptions.CultureInvariant);

        public static void Check(object v, string where, string codePrefix, Func<string, bool> fingerprintOk = null)
        {
            Walk(v, where, codePrefix, fingerprintOk, 0);
            int n = Json.Bytes(v).Length;
            if (n > MaxBytes) throw new ComposeException(codePrefix + ".optional", where + " holds " + Units.Size(n) + " of optional data (at most " + Units.Size(MaxBytes) + ")");
        }

        static void Walk(object v, string where, string pre, Func<string, bool> fpOk, int depth)
        {
            if (depth > MaxDepth) throw new ComposeException(pre + ".optional", where + " is nested more than " + MaxDepth + " levels deep");
            switch (v)
            {
                case null: case bool _: case long _: case decimal _: return;
                case string s:
                    if (fpOk != null && fpOk(s)) return;
                    var (code, why) = PlainText.Check(s, MaxString, true);
                    if (code != null) throw new ComposeException(pre + "." + code.Substring(5), where + " " + why);
                    return;
                case JObj o:
                    if (o.Count() > MaxItems) throw new ComposeException(pre + ".optional", where + " has more than " + MaxItems + " members");
                    foreach (var kv in o)
                    {
                        if (!Key.IsMatch(kv.Key)) throw new ComposeException(pre + ".optional", where + " has a member name that is not allowed (\"" + Display.Line(kv.Key, 40) + "\")");
                        Walk(kv.Value, where + "." + kv.Key, pre, fpOk, depth + 1);
                    }
                    return;
                case List<object> a:
                    if (a.Count > MaxItems) throw new ComposeException(pre + ".optional", where + " has more than " + MaxItems + " items");
                    for (int i = 0; i < a.Count; i++) Walk(a[i], where + "[" + i + "]", pre, fpOk, depth + 1);
                    return;
                default: throw new ComposeException(pre + ".optional", where + " holds a value of an unknown kind");
            }
        }
    }

    public static class Units
    {
        /// <summary>a count in words: "1 picture", "3 pictures", "1,873 files"</summary>
        public static string Count(long n, string one, string many = null) => n.ToString("N0", CultureInfo.InvariantCulture) + " " + (n == 1 ? one : many ?? one + "s");
        /// <summary>bytes below 1,000; then KB (1 decimal), MB/GB/TB (2 decimals), trailing zeros dropped; decimal units only</summary>
        public static string Size(long n)
        {
            if (n < 0) n = 0;
            if (n < 1000) return n.ToString("N0", CultureInfo.InvariantCulture) + " B";
            var units = new[] { ("KB", 1e3, 1), ("MB", 1e6, 2), ("GB", 1e9, 2), ("TB", 1e12, 2) }; int i = 0;
            while (i < units.Length - 1 && n >= units[i + 1].Item2) i++;
            if (i < units.Length - 1 && Math.Round(n / units[i].Item2, units[i].Item3) >= 1000) i++;
            var (u, d, p) = units[i];
            var t = (n / d).ToString("N" + p, CultureInfo.InvariantCulture);
            if (t.Contains(".")) t = t.TrimEnd('0').TrimEnd('.');
            return t + " " + u;
        }
    }

    public static class Sha
    {
        public static string Of(byte[] b) { using (var h = SHA256.Create()) return Hex.Of(h.ComputeHash(b)); }
        public static string Of(Stream s) { using (var h = SHA256.Create()) return Hex.Of(h.ComputeHash(s)); }
        public static bool Is(string s) => Hex.IsHex(s, 64);
        public static string Of(string text) => Of(new UTF8Encoding(false).GetBytes(text));
    }

    /// <summary>a new random identifier (32 lowercase hex digits): a plan's or a design's own id, never derived from anything private</summary>
    public static class NewId
    {
        public static string Make() { var b = new byte[16]; using (var r = RandomNumberGenerator.Create()) r.GetBytes(b); return Hex.Of(b); }
        public static bool Is(string s) => Hex.IsHex(s, 32);
    }

    /// <summary>small reading helpers for documents parsed by StrictJson (JObj / List of object)</summary>
    static class Doc
    {
        public static JObj Obj(object v) => v as JObj;
        public static List<object> Arr(object v) => v as List<object>;
        public static IEnumerable<string> Keys(JObj o) => o.Select(kv => kv.Key);
        public static JObj Without(JObj o, string key) { var n = new JObj(); foreach (var kv in o) if (kv.Key != key) n.Add(kv.Key, kv.Value); return n; }
        public static bool IsInt(object v) => v is long;
        public static long Int(object v) => v is long l ? l : long.MinValue;
    }
}
