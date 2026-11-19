// Classic Cart Studio console backup and restore window. Copyright (C) 2026 Rance9. GPL-3.0-or-later.
// CCS Console Workbench: the glossary. One short explanation per unfamiliar word, the same in every sentence that uses it, in both hosts
// (the standalone and Classic Cart Studio for Windows), in Help's Glossary and on the Classic Cart Studio website: all of them read the one
// file tools/rom-pipeline/studio/glossary.json (carried in this library as glossary/glossary.json). How a word becomes a dotted-underlined
// word in its own sentence, with its explanation beside it: WorkbenchTerms.cs.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace CcsBackup.Ui
{
    /// <summary>one word and its explanation (the same for every console); Detail: what is particular to the consoles this version knows,
    /// named as such; Tech: its technical name(s), for people who know them; Forms: the exact words that name it in a
    /// sentence (case matters; a plural s is matched too); FileKind: the FileKinds name it explains, when it is a kind of file</summary>
    public sealed class GlossaryTerm { public string Key, Word, Text, Detail, Tech, FileKind; public string[] Forms = new string[0]; public bool OncePerPage; }

    public static class Glossary
    {
        static IReadOnlyList<GlossaryTerm> terms;
        static (string form, GlossaryTerm term)[] forms = new (string, GlossaryTerm)[0];

        public static IReadOnlyList<GlossaryTerm> Terms { get { Load(); return terms; } }
        public static GlossaryTerm Find(string key) => Terms.FirstOrDefault(t => t.Key == key);

        static void Load()
        {
            if (terms != null) return;
            var list = new List<GlossaryTerm>();
            try
            {
                using (var s = typeof(Glossary).Assembly.GetManifestResourceStream("glossary/glossary.json"))
                    if (s != null) using (var r = new StreamReader(s)) list = Parse(r.ReadToEnd());
            }
            catch (Exception) { list = new List<GlossaryTerm>(); }
            Use(list);
        }

        /// <summary>the terms of a glossary file (ccs-glossary/1)</summary>
        public static List<GlossaryTerm> Parse(string json)
        {
            var d = Json.Parse(json); var list = new List<GlossaryTerm>();
            if (d.S("schema") != "ccs-glossary/1") throw new FormatException("not a ccs-glossary/1 file");
            foreach (var o in d.A("terms") ?? new List<object>())
            {
                if (!(o is Dictionary<string, object> t)) continue;
                var g = new GlossaryTerm { Key = t.S("key"), Word = t.S("word"), Text = t.S("text"), Detail = t.S("detail"), Tech = t.S("tech"), FileKind = t.S("filekind"), OncePerPage = t.S("scope") == "page" };
                g.Forms = (t.A("forms") ?? new List<object>()).OfType<string>().Where(x => x.Length > 0).ToArray();
                if (g.Key != null && g.Word != null && g.Text != null) list.Add(g);
            }
            return list;
        }

        /// <summary>uses these terms (the library's own file; a test may pass others)</summary>
        public static void Use(IEnumerable<GlossaryTerm> list)
        {
            var l = list.ToList();
            // longest first, so "CCS HOME" wins over "CCS", and "Classic Cart Studio backup" over "Classic Cart Studio"
            forms = l.SelectMany(t => t.Forms.Select(f => (f, t))).OrderByDescending(x => x.f.Length).ToArray();
            terms = l;
        }

        /// <summary>where the glossary's words are in a sentence: at most one place per term (its first), never inside a file name, a path or
        /// a web address, and never part of a longer word. used: terms already explained in the same block of text (left out, and added
        /// to).</summary>
        public static List<(int start, int length, GlossaryTerm term)> Matches(string text, ISet<string> used = null, ISet<string> pageUsed = null)
        {
            Load(); var found = new List<(int, int, GlossaryTerm)>();
            if (string.IsNullOrEmpty(text) || forms.Length == 0) return found;
            var blocked = Blocked(text);
            int i = 0;
            while (i < text.Length)
            {
                if (blocked[i] || !StartOk(text, i)) { i++; continue; }
                int len = 0; GlossaryTerm hit = null;
                foreach (var (f, t) in forms)
                {
                    if (i + f.Length > text.Length || string.CompareOrdinal(text, i, f, 0, f.Length) != 0) continue;
                    int n = f.Length;
                    if (i + n < text.Length && text[i + n] == 's' && char.IsLetter(f[f.Length - 1]) && EndOk(text, i + n + 1)) n++;
                    else if (!EndOk(text, i + n)) continue;
                    bool clear = true; for (int k = i; k < i + n; k++) if (blocked[k]) { clear = false; break; }
                    if (!clear) continue;
                    len = n; hit = t; break;
                }
                if (hit == null) { i++; continue; }
                if (hit.OncePerPage && pageUsed != null ? pageUsed.Add(hit.Key) : used == null || used.Add(hit.Key)) found.Add((i, len, hit));
                i += len;
            }
            return found;
        }

        static bool Joins(char c) => char.IsLetterOrDigit(c) || c == '-' || c == '_' || c == '\\' || c == '/' || c == '@';
        static bool StartOk(string s, int i) => i == 0 || !(Joins(s[i - 1]) || s[i - 1] == '.');
        static bool EndOk(string s, int j)
        {
            if (j >= s.Length) return true;
            var c = s[j];
            if (Joins(c)) return false;
            if (c == '.' && j + 1 < s.Length && char.IsLetterOrDigit(s[j + 1])) return false;   // a file name's extension
            return true;
        }

        static readonly Regex FileName = new Regex(@"[A-Za-z0-9_\-]\.[A-Za-z0-9]{2,8}$");
        /// <summary>the characters of words that are file names, paths or web addresses: never explained</summary>
        static bool[] Blocked(string s)
        {
            var b = new bool[s.Length]; int i = 0;
            while (i < s.Length)
            {
                if (char.IsWhiteSpace(s[i])) { i++; continue; }
                int j = i; while (j < s.Length && !char.IsWhiteSpace(s[j])) j++;
                var w = s.Substring(i, j - i).TrimEnd('.', ',', ';', ':', ')', ']', '"', '\'', '”', '’');
                bool block = w.Contains("://") || w.IndexOf('\\') >= 0 || w.IndexOf('/') >= 0 || w.StartsWith("www.", StringComparison.OrdinalIgnoreCase) || FileName.IsMatch(w);
                if (block) for (int k = i; k < j; k++) b[k] = true;
                i = j;
            }
            return b;
        }
    }
}
