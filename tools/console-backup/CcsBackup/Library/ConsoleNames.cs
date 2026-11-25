// Classic Cart Studio console backup and restore engine. Copyright (C) 2026 Rance9. GPL-3.0-or-later.
// Friendly console names: what CCS Console Workbench calls each console it has identified ("NES Classic (1)", "SNES Classic (2)", or a name
// the person gave it). A name is only a label on this PC: it never identifies a console, never takes part in a backup's admission, a
// restore's same-console check or any other decision, and it is never written to a console, a backup file, a shared plan or a design.
//   key       the console's complete hardware ID in its one normalized form (ConsoleIdentity.NormalizeHwid), as its full SHA-256 in
//             lowercase hex (64 characters). Never the 8-character short tag (which is only the first 8 characters of this same hash, so two
//             consoles can share it), never a USB port, the order consoles were connected in, or the software a console runs. The ID itself
//             is not written to the settings file.
//   number    given once, when a console of that model is first identified: one more than the highest number any console of the same model
//             has had. Never given again to another console and never changed, also when consoles are seen one at a time.
//   name      the person's own name for it (Rename Console); none means the model's name and number
// Kept in the Workbench's settings (console_names): in memory and in the settings file, under the same saving rules as every other setting.
using CcsBackup.Device;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace CcsBackup.Library
{
    public sealed class ConsoleNameBook
    {
        public sealed class Entry
        {
            public string Key, Model, Name; public int Number;
            /// <summary>the name shown for this console: the person's own, or its model's short name and number ("NES Classic (1)")</summary>
            public string Display => !string.IsNullOrEmpty(Name) ? Name : ModelName(Model) + " (" + Number.ToString(CultureInfo.InvariantCulture) + ")";
            /// <summary>its model's short name ("NES Classic"), so a console with a name of its own still says what kind of console it is</summary>
            public string ModelShort => ModelName(Model);
            public bool Custom => !string.IsNullOrEmpty(Name);
            internal Entry Copy() => new Entry { Key = Key, Model = Model, Name = Name, Number = Number };
        }

        /// <summary>the longest name the person can give a console (characters)</summary>
        public const int MaxName = 40;
        readonly List<Entry> entries = new List<Entry>();
        public IReadOnlyList<Entry> Entries => entries;

        /// <summary>the key for a console's hardware ID: the full SHA-256 of its normalized form (null for no ID)</summary>
        public static string KeyOf(string hwid)
        {
            var n = ConsoleIdentity.NormalizeHwid(hwid);
            if (n.Length == 0) return null;
            using (var h = System.Security.Cryptography.SHA256.Create()) return Hex.Of(h.ComputeHash(Encoding.ASCII.GetBytes(n)));
        }
        /// <summary>a well-formed key: 64 lowercase hex characters</summary>
        public static bool IsKey(string k) => k != null && k.Length == 64 && k.All(c => (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f'));

        static string ModelName(string model) => ConsoleModels.ById(model)?.ShortName ?? "Console";

        public Entry Find(string key) => key == null ? null : entries.FirstOrDefault(e => e.Key == key);

        /// <summary>the console with this key, given its number the first time it is seen (added: true then). A known console keeps its
        /// number and name, whatever model a later identification names.</summary>
        public Entry Note(string key, string model, out bool added)
        {
            added = false;
            if (!IsKey(key)) throw new ArgumentException("not a console key");
            var e = Find(key); if (e != null) return e;
            int n = 1 + entries.Where(x => x.Model == model).Select(x => x.Number).DefaultIfEmpty(0).Max();
            e = new Entry { Key = key, Model = model, Number = n }; entries.Add(e); added = true;
            return e;
        }

        /// <summary>the person's name for a console (null or empty: back to the model's name and number); false when the key is unknown or the
        /// name is not one Workbench can keep (NameProblem says why)</summary>
        public bool Rename(string key, string name)
        {
            var e = Find(key); if (e == null) return false;
            var clean = CleanName(name);
            if (clean.Length > 0 && NameProblem(clean) != null) return false;
            e.Name = clean.Length == 0 ? null : clean;
            return true;
        }

        /// <summary>a typed name in its kept form: control characters removed, runs of spaces made one, trimmed, in Unicode's composed form</summary>
        public static string CleanName(string name)
        {
            if (name == null) return "";
            var sb = new StringBuilder(); bool space = false;
            foreach (var ch in CcsBackup.Archive.BackupNames.WholeCharacters(name).Normalize(NormalizationForm.FormC))
            {
                if (char.IsControl(ch)) continue;
                if (char.IsWhiteSpace(ch)) { space = sb.Length > 0; continue; }
                if (space) { sb.Append(' '); space = false; }
                sb.Append(ch);
            }
            return sb.ToString();
        }

        /// <summary>why a (cleaned) name cannot be kept, or null</summary>
        public static string NameProblem(string clean)
        {
            if (clean.Length > MaxName) return "A console's name can be at most " + MaxName + " characters long.";
            for (int i = 0; i < clean.Length; i++)
            {
                if (char.IsHighSurrogate(clean[i]) && (i + 1 >= clean.Length || !char.IsLowSurrogate(clean[i + 1]))) return "The name has a character that cannot be kept.";
                if (char.IsLowSurrogate(clean[i]) && (i == 0 || !char.IsHighSurrogate(clean[i - 1]))) return "The name has a character that cannot be kept.";
            }
            return null;
        }

        public ConsoleNameBook Copy() { var c = new ConsoleNameBook(); c.entries.AddRange(entries.Select(e => e.Copy())); return c; }

        /// <summary>everything the book holds, in one line (two books with the same text hold the same names)</summary>
        public string Signature() => string.Join("|", entries.OrderBy(e => e.Key, StringComparer.Ordinal).Select(e => e.Key + "," + e.Model + "," + e.Number.ToString(CultureInfo.InvariantCulture) + "," + (e.Name ?? "")));

        public List<object> ToJson() => entries.Select(e =>
        {
            var j = new JObj { { "key", e.Key }, { "model", e.Model }, { "number", (long)e.Number } };
            if (e.Custom) j.Add("name", e.Name);
            return (object)j;
        }).ToList();

        /// <summary>a settings file's console_names: entries that are not well formed are left out (never guessed); a key listed twice keeps
        /// its first entry</summary>
        public static ConsoleNameBook FromJson(List<object> list)
        {
            var b = new ConsoleNameBook();
            foreach (var d in (list ?? new List<object>()).OfType<Dictionary<string, object>>())
            {
                var key = d.S("key"); long n = d.L("number"); var model = d.S("model");
                if (!IsKey(key) || n < 1 || n > 100000 || b.Find(key) != null || string.IsNullOrEmpty(model) || model.Length > 64) continue;
                var name = CleanName(d.S("name"));
                b.entries.Add(new Entry { Key = key, Model = model, Number = (int)n, Name = name.Length > 0 && NameProblem(name) == null ? name : null });
            }
            return b;
        }
    }
}
