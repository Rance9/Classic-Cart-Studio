// Classic Cart Studio console backup and restore engine. Copyright (C) 2026 Rance9. GPL-3.0-or-later.
// Windows-safe names for exported files. A name from a backup is console data (any bytes but "/" and NUL), so every name is rebuilt before
// it touches the PC, and only ever as ONE path component under a folder the export itself created:
//   - characters Windows refuses or treats specially are replaced (":" becomes " -", "/" and "\" become "-", the quote becomes an
//     apostrophe, < > | ? * and control characters become "_"), so no name can be absolute, a UNC or device path, a drive letter, an
//     alternate data stream or a ".." step; an unpaired surrogate becomes "_";
//   - trailing dots and spaces are removed (Windows drops them silently), "" "." ".." and names of only dots become "unnamed";
//   - reserved device names (CON PRN AUX NUL CONIN$ CONOUT$ COM0-9 LPT0-9 and their superscript forms), with or without an extension, get
//     a leading "_";
//   - a component longer than MaxComponent characters is shortened, keeping its extension.
// ExportPaths gives each original file or folder one place in the export, deterministically: names are compared the way Windows compares
// them (ignoring case), a second different original with the same Windows name gets " (2)", " (3)" ... before its extension, and the
// same original folder always gets the same place. The original path is always kept in the export's manifest.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace CcsBackup.Browse
{
    public static class SafeNames
    {
        public const int MaxComponent = 80;
        static readonly HashSet<string> Reserved = new HashSet<string>(
            new[] { "CON", "PRN", "AUX", "NUL", "CONIN$", "CONOUT$" }
            .Concat(Enumerable.Range(0, 10).SelectMany(i => new[] { "COM" + i, "LPT" + i }))
            .Concat(new[] { "\u00b9", "\u00b2", "\u00b3" }.SelectMany(s => new[] { "COM" + s, "LPT" + s })), StringComparer.OrdinalIgnoreCase);

        /// <summary>one name from a backup, made into a single safe Windows path component (never empty, never a path)</summary>
        public static string Component(string name, int max = MaxComponent)
        {
            var sb = new StringBuilder(); var n = name ?? "";
            for (int i = 0; i < n.Length; i++)
            {
                char ch = n[i];
                if (char.IsHighSurrogate(ch) && i + 1 < n.Length && char.IsLowSurrogate(n[i + 1])) { sb.Append(ch).Append(n[i + 1]); i++; continue; }
                if (char.IsSurrogate(ch) || ch < 0x20 || ch == 0x7F) { sb.Append('_'); continue; }
                switch (ch)
                {
                    case ':': sb.Append(" -"); break;
                    case '/': case '\\': sb.Append('-'); break;
                    case '"': sb.Append('\''); break;
                    case '<': case '>': case '|': case '?': case '*': sb.Append('_'); break;
                    default: sb.Append(ch); break;
                }
            }
            var s = sb.ToString().TrimEnd(' ', '.');
            if (s.Length == 0) s = "unnamed";
            if (s.Length > max)
            {
                int dot = s.LastIndexOf('.'); var ext = dot > 0 && s.Length - dot <= 16 ? s.Substring(dot) : "";
                var stem = Cut(s.Substring(0, s.Length - ext.Length), Math.Max(1, max - ext.Length)).TrimEnd(' ', '.');
                s = (stem.Length == 0 ? "unnamed" : stem) + ext;
            }
            if (IsReserved(s)) s = "_" + s;
            return s;
        }

        static string Cut(string s, int n) { if (s.Length <= n) return s; if (char.IsHighSurrogate(s[n - 1])) n--; return s.Substring(0, n); }

        static bool IsReserved(string s) { int dot = s.IndexOf('.'); var stem = (dot >= 0 ? s.Substring(0, dot) : s).TrimEnd(' '); return Reserved.Contains(stem); }

        /// <summary>the n-th name for a different original with the same Windows name: "name (n).ext" (folders: "name (n)")</summary>
        public static string WithSuffix(string safe, int n, bool folder)
        {
            int dot = folder ? -1 : safe.LastIndexOf('.');
            return dot > 0 ? safe.Substring(0, dot) + " (" + n + ")" + safe.Substring(dot) : safe + " (" + n + ")";
        }

        /// <summary>true when the component is one Windows takes literally, as one name, under the folder it is created in</summary>
        public static bool IsSafe(string c)
        {
            if (string.IsNullOrEmpty(c) || c.Length > 255 || c == "." || c == "..") return false;
            if (c.EndsWith(" ", StringComparison.Ordinal) || c.EndsWith(".", StringComparison.Ordinal)) return false;
            if (c.Any(ch => ch < 0x20 || "<>:\"/\\|?*".IndexOf(ch) >= 0) || c.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return false;
            return !IsReserved(c);
        }
    }

    /// <summary>Places in one export: every original folder or file (by a key: its original path) gets one relative path; distinct originals
    /// whose Windows names collide get deterministic suffixes. Relative paths use "\" and never start with it.</summary>
    public sealed class ExportPaths
    {
        readonly Dictionary<string, Dictionary<string, string>> names = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        readonly Dictionary<string, string> assigned = new Dictionary<string, string>(StringComparer.Ordinal);

        public string Folder(string parent, string desired, string key) => Assign(parent, desired, key, true);
        public string File(string parent, string desired, string key) => Assign(parent, desired, key, false);
        /// <summary>takes an exact name first (the export's own manifest), so nothing from the backup can have it</summary>
        public void Reserve(string parent, string name, string key) { Kids(parent)[name] = key; assigned[key] = Join(parent, name); }

        Dictionary<string, string> Kids(string parent) { if (!names.TryGetValue(parent, out var k)) names[parent] = k = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase); return k; }
        static string Join(string parent, string name) => parent.Length == 0 ? name : parent + "\\" + name;

        string Assign(string parent, string desired, string key, bool folder)
        {
            if (assigned.TryGetValue(key, out var r)) return r;
            var safe = SafeNames.Component(desired); var kids = Kids(parent);
            for (int n = 1; n < 1000000; n++)
            {
                var nm = n == 1 ? safe : SafeNames.WithSuffix(safe, n, folder);
                if (kids.ContainsKey(nm)) continue;
                kids[nm] = key; return assigned[key] = Join(parent, nm);
            }
            throw new IOException("too many different files share the name " + safe);
        }
    }
}
