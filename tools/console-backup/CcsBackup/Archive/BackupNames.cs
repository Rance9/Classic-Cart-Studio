// Classic Cart Studio console backup and restore engine. Copyright (C) 2026 Rance9. GPL-3.0-or-later.
// A backup's file name when the person names it (Back Up Console's Backup name). The name is only the file's name: the backup's own record of
// the console (its manifest, its ID and the console's identity) is the same whatever it is called, and a console's friendly name changing
// later never renames a backup file.
//   Clean     what a typed name becomes as a file name on Windows: Unicode in its composed form; characters Windows does not allow in a
//             file name (< > : " / \ | ? * and control characters) each become "-"; runs of spaces one space; no leading or trailing spaces
//             or dots; a name Windows reserves for a device (CON, PRN, AUX, NUL, COM1 to COM9, LPT1 to LPT9, also with an extension) gets
//             " Backup" added; at most MaxLength characters, never cutting a character in two
//   Stem      the cleaned name as the engine uses it: "SIMULATED " first for a simulated console (as every simulated backup's name is), and
//             short enough that the backup's full path, with a " (999)" collision number and ".zip.partial", stays within Windows' usual
//             path length
// The engine never writes over a file: a name already used in the folder gets " (2)", " (3)", ... (CaptureEngine.Unique), for the backup
// and its additional copy alike.
using System;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace CcsBackup.Archive
{
    public static class BackupNames
    {
        public const int MaxLength = 120;
        /// <summary>the longest full path a backup file gets (Windows' usual limit of 260 characters, less the terminating character)</summary>
        public const int MaxPath = 259;
        const string CollisionRoom = " (999)";

        static readonly Regex Reserved = new Regex(@"^(CON|PRN|AUX|NUL|CONIN\$|CONOUT\$|COM[0-9¹²³]|LPT[0-9¹²³])(\..*)?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        /// <summary>the typed name as a Windows file name (without an extension); "" when nothing usable is left</summary>
        public static string Clean(string typed)
        {
            if (string.IsNullOrEmpty(typed)) return "";
            var invalid = System.IO.Path.GetInvalidFileNameChars();
            var sb = new StringBuilder(); bool space = false;
            var t = WholeCharacters(typed).Normalize(NormalizationForm.FormC);
            for (int i = 0; i < t.Length; i++)
            {
                char ch = t[i];
                // a lone half of a character pair cannot be a file name's character
                if (char.IsHighSurrogate(ch)) { if (i + 1 < t.Length && char.IsLowSurrogate(t[i + 1])) { if (space) { sb.Append(' '); space = false; } sb.Append(ch).Append(t[i + 1]); i++; } continue; }
                if (char.IsLowSurrogate(ch)) continue;
                if (char.IsWhiteSpace(ch)) { space = sb.Length > 0; continue; }   // tabs and line breaks too: one space
                if (invalid.Contains(ch) || char.IsControl(ch)) ch = '-';
                if (space) { sb.Append(' '); space = false; }
                sb.Append(ch);
            }
            var s = Trim(sb.ToString());
            if (s.Length > 0 && Reserved.IsMatch(s)) s += " Backup";
            return Cut(s, MaxLength);
        }

        /// <summary>why a typed name cannot be used, in the window's words (null: it can; Clean may still change it, and the window shows the
        /// file name it becomes)</summary>
        public static string Problem(string typed) => Clean(typed).Length == 0 ? "Type a name for the backup, or use the suggested one." : null;

        /// <summary>the cleaned name as the engine uses it in folder (null: the name was not usable, so the usual name is used)</summary>
        public static string Stem(string typed, bool simulated, string folder)
        {
            var s = Clean(typed); if (s.Length == 0) return null;
            if (simulated && !s.StartsWith("SIMULATED ", StringComparison.Ordinal)) s = "SIMULATED " + s;
            // the whole path, with room for a collision number and the unfinished file's extension, within Windows' usual length
            int room = MaxPath - (folder ?? "").TrimEnd('\\', '/').Length - 1 - CollisionRoom.Length - BackupFormat.Partial.Length;
            if (room < 16) room = 16;   // a folder that long is the person's choice; the name still gets a useful length
            return Cut(s, Math.Min(MaxLength + (simulated ? 10 : 0), room));
        }

        static string Trim(string s) => s.Trim(' ', '.');

        /// <summary>the text without a lone half of a character pair (which cannot be a file name's character, and which Unicode's composed
        /// form cannot be made from)</summary>
        public static string WholeCharacters(string s)
        {
            var sb = new StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                if (char.IsHighSurrogate(s[i])) { if (i + 1 < s.Length && char.IsLowSurrogate(s[i + 1])) { sb.Append(s[i]).Append(s[i + 1]); i++; } continue; }
                if (char.IsLowSurrogate(s[i])) continue;
                sb.Append(s[i]);
            }
            return sb.ToString();
        }

        /// <summary>at most n characters, never ending inside a character pair, without a trailing space or dot</summary>
        static string Cut(string s, int n)
        {
            if (s.Length <= n) return s;
            int k = n; if (k > 0 && char.IsHighSurrogate(s[k - 1])) k--;
            return Trim(s.Substring(0, k));
        }
    }
}
