// Classic Cart Studio console backup and restore engine. Copyright (C) 2026 Rance9. GPL-3.0-or-later.
// The convenience export CCS/Console-HOME.ccs: the Classic Cart Studio HOME found on the console, as an ordinary .ccs collection package
// (ccs-package/2, kind "collection"; tools/companion/CCS-FORMAT.md), so it can be opened with Restore a Backup like any other. It is
// built from what the console really holds (the package copy named by /var/lib/ccs/pkg/current: its session.json game list, its
// MANIFEST.json label and its ROM files), never from the PC's own collections. Only games whose exact ROM file is on the console and is
// the version its id names go in; the others are listed as left out. Its startup screen goes in as a Studio's .ccs carries it (D-174):
// Random or a supplied picture by its id, or the collection's own picture with its bytes (checked against startup.json). The rest of
// the HOME's look (theme, folders, favorites, music) is not written into the .ccs: the console keeps it in its own format, and the full
// backup holds it exactly. A restore never uses this file: it writes the captured storage itself.
using CcsBackup.Archive;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace CcsBackup.Ext4
{
    public sealed class ExportPlan
    {
        public string Status = "none", Reason, Name, Copy; public int ConsoleGames;
        public string StartupChoice, StartupNote; public JObj StartupCustom;   // the HOME's startup screen (null: not read; StartupNote says why)
        public List<(string id, string title, uint inode, string ext, long size, string sha256, string content)> Games = new List<(string, string, uint, string, long, string, string)>();
        public List<object> LeftOut = new List<object>();
        public JObj Describe() => new JObj
        {
            { "status", Status }, { "reason", Reason }, { "name", Name }, { "console_package", Copy }, { "games_on_console", ConsoleGames }, { "games_exported", Games.Count }, { "left_out", LeftOut },
            { "startup", new JObj { { "choice", StartupChoice }, { "own_picture", StartupCustom != null }, { "note", StartupNote } } },
            { "not_in_export", "The HOME's look (theme, folders, favorites, music) is not in the .ccs file; it is in the full backup exactly as on the console. The startup screen is in the .ccs file (with the collection's own picture, when that is the choice)." },
            { "role", "convenience: open it with Restore a Backup in Classic Cart Studio; a restore never uses it" },
        };
    }

    public static class CcsExport
    {
        static readonly Regex CopyRe = new Regex("^[0-9a-f]{16}\\.[0-9a-f]{8}$"), IdRe = new Regex("^(p_)?[0-9a-f]{16}$"), Hex64 = new Regex("^[0-9a-f]{64}$");
        public const string PkgOnConsole = "/tmp/ccs/pkg/";
        public const long MaxRom = 16L * 1000 * 1000;
        public const long MaxStartup = 450L * 1000;   // the collection's own startup picture (studio/startup-custom.js, console_package.py: the same limit)
        static readonly Regex CustomRe = new Regex("^custom-[0-9a-f]{16}$"), SuppliedRe = new Regex("^(random|forgotten-keep|galactic-frontier|kingdoms-beyond|neon-highway|pixel-worlds)$");
        const string Readme = "Classic Cart Studio collection package (.ccs)\r\n\r\nThis file was made from the Classic Cart Studio HOME on an NES Classic, by a console backup. It holds the games whose\r\nROM files were on the console (exact versions). Open it with Restore a Backup in Classic Cart Studio: the Windows app, or the\r\nwebsite https://classiccartstudio.com.\r\n\r\nClassic Cart Studio supplies only its Included Games, which their authors license or permit it to share; it does not supply or fetch\r\noriginal commercial NES games. The other game files in this backup are your own: you are responsible for having the rights to copy\r\nand use them. Creating a backup does not grant permission to redistribute copyrighted games.\r\n";

        static object Home(ExportPlan plan)
        {
            if (plan.StartupChoice == null) return null;
            var h = new JObj { { "startup_image", plan.StartupChoice } };
            if (plan.StartupCustom != null) h.Add("startup_custom", plan.StartupCustom);
            return h;
        }

        public static (string sha256, long size) ContentOf(byte[] b)
        {
            int off = 0;
            if (b.Length >= 16 && b[0] == 'N' && b[1] == 'E' && b[2] == 'S' && b[3] == 0x1A) { off = 16; if ((b[6] & 4) != 0 && b.Length >= 16 + 512) off += 512; }
            using (var h = SHA256.Create()) return (Hex.Of(h.ComputeHash(b, off, b.Length - off)), b.Length - off);
        }

        static string Text(Ext4Reader fs, string path, long max)
        {
            var ino = fs.Lookup(path);
            if (ino == null || !fs.IsRegular(ino.Value)) return null;
            return Encoding.UTF8.GetString(fs.ReadAll(ino.Value, max));
        }

        public static ExportPlan Plan(Ext4Reader fs)
        {
            var p = new ExportPlan();
            if (fs.Lookup("/ccs") == null) { p.Status = "none"; p.Reason = "There is no Classic Cart Studio HOME on this console."; return p; }
            var cur = Text(fs, "/ccs/pkg/current", 256)?.Trim();
            if (cur == null) { p.Status = "none"; p.Reason = "Classic Cart Studio's folder is on the console, but no HOME is selected in it (no pkg/current)."; return p; }
            if (!CopyRe.IsMatch(cur)) { p.Status = "unavailable"; p.Reason = "The console's record of its HOME is not in the expected form."; return p; }
            p.Copy = cur; var root = "/ccs/pkg/" + cur + "/";
            Dictionary<string, object> man, sess;
            try { man = Json.Parse(Text(fs, root + "MANIFEST.json", 4000000)); sess = Json.Parse(Text(fs, root + "session.json", 8000000)); }
            catch (Exception) { p.Status = "unavailable"; p.Reason = "The console's HOME package files could not be read."; return p; }
            if (man.S("schema") != "ccs-console-package/1") { p.Status = "unavailable"; p.Reason = "The console's HOME package is not one this version reads."; return p; }
            // a package that names a game system, core or method this version does not support is never turned into a .ccs file (SNES-GROUNDWORK.md)
            if (!Compose.Capabilities.ResolveTarget(man, "console-package", sess.A("games")).Ok) { p.Status = "unavailable"; p.Reason = "The console's HOME package is for a game system this version does not support."; return p; }
            p.Name = (man.S("label") ?? "HOME from the console").Trim(); if (p.Name.Length > 200) p.Name = p.Name.Substring(0, 200);
            var games = (sess.A("games") ?? new List<object>()).OfType<Dictionary<string, object>>().ToList();
            p.ConsoleGames = games.Count; var seen = new HashSet<string>();
            foreach (var g in games)
            {
                var id = g.S("id") ?? ""; var title = (g.S("title") ?? id); if (title.Length > 200) title = title.Substring(0, 200);
                string why = null; uint ino = 0; byte[] bytes = null; string cs = null;
                if (!IdRe.IsMatch(id)) why = "its id is not an exact version";
                else if (!seen.Add(id)) why = "listed twice";
                else if (!string.IsNullOrEmpty(g.S("unavailable")) || string.IsNullOrEmpty(g.S("rom"))) why = "its game file is not on the console";
                else
                {
                    var rom = g.S("rom"); var want = g.S("content_sha256") ?? "";
                    if (!rom.StartsWith(PkgOnConsole) || !Regex.IsMatch(rom.Substring(PkgOnConsole.Length), "^roms/[0-9a-f]{64}\\.nes$") || !Hex64.IsMatch(want)) why = "its game file is not where the package keeps game files";
                    else
                    {
                        var hit = fs.Lookup(root + rom.Substring(PkgOnConsole.Length));
                        if (hit == null || !fs.IsRegular(hit.Value)) why = "its game file is missing from the console";
                        else
                        {
                            try { bytes = fs.ReadAll(hit.Value, MaxRom); ino = hit.Value; } catch (Ext4Exception) { why = "its game file could not be read"; }
                            if (bytes != null)
                            {
                                cs = ContentOf(bytes).sha256;
                                bool idOk = id.StartsWith("p_") ? id == "p_" + cs.Substring(0, 16) : id == cs.Substring(0, 16);
                                if (cs != want || !idOk) why = "its game file is not the version its id names";
                            }
                        }
                    }
                }
                if (why != null) { p.LeftOut.Add(new JObj { { "id", id }, { "title", title }, { "why", why } }); continue; }
                bool hdr = bytes.Length >= 16 && bytes[0] == 'N' && bytes[1] == 'E' && bytes[2] == 'S' && bytes[3] == 0x1A;
                string sha; using (var h = SHA256.Create()) sha = Hex.Of(h.ComputeHash(bytes));
                p.Games.Add((id, title, ino, hdr ? ".nes" : ".unh", bytes.LongLength, sha, cs));
            }
            if (p.Games.Count == 0) { p.Status = "unavailable"; p.Reason = p.ConsoleGames == 0 ? "The console's HOME has no games." : "None of the console HOME's games has its exact game file on the console, so no valid .ccs file can be made."; return p; }
            p.Status = "included"; Startup(fs, root, p);
            if (p.LeftOut.Count > 0) p.Reason = p.LeftOut.Count + " of the console HOME's " + p.ConsoleGames + " games could not be exported (listed in left_out); a .ccs file carries only complete games.";
            return p;
        }

        // the package copy's startup screen (startup/startup.json): its choice, and for the collection's own picture the listed file's bytes
        // when they are exactly the listed picture (SHA-256) and a JPEG within the limit; anything else is noted and the .ccs names no picture
        static void Startup(Ext4Reader fs, string root, ExportPlan p)
        {
            Dictionary<string, object> sj;
            try { var t = Text(fs, root + "startup/startup.json", 1000000); if (t == null) { p.StartupNote = "the console's HOME has no startup settings"; return; } sj = Json.Parse(t); }
            catch (Exception) { p.StartupNote = "the startup settings could not be read"; return; }
            if (sj.S("schema") != "ccs-startup/1") { p.StartupNote = "the startup settings are not in the expected form"; return; }
            var choice = sj.S("choice") ?? "random";
            if (SuppliedRe.IsMatch(choice)) { p.StartupChoice = choice; return; }
            if (!CustomRe.IsMatch(choice)) { p.StartupNote = "the startup choice is not one this version knows"; return; }
            var e = (sj.A("images") ?? new List<object>()).OfType<Dictionary<string, object>>().FirstOrDefault(x => x.S("id") == choice);
            var file = e?.S("file") ?? ""; var want = e?.S("sha256") ?? "";
            if (!Regex.IsMatch(file, "^startup-custom-[0-9a-f]{16}\\.jpg$") || !Hex64.IsMatch(want)) { p.StartupNote = "the chosen picture is not listed correctly"; return; }
            var hit = fs.Lookup(root + "startup/" + file);
            if (hit == null || !fs.IsRegular(hit.Value)) { p.StartupNote = "the chosen picture is missing from the console"; return; }
            byte[] b;
            try { b = fs.ReadAll(hit.Value, MaxStartup + 1); } catch (Ext4Exception) { p.StartupNote = "the chosen picture could not be read"; return; }
            string sha; using (var h = SHA256.Create()) sha = Hex.Of(h.ComputeHash(b));
            if (b.LongLength > MaxStartup || sha != want || b.Length < 4 || b[0] != 0xFF || b[1] != 0xD8) { p.StartupNote = "the chosen picture is not the one the startup settings list"; return; }
            var id = "custom-" + sha.Substring(0, 16); var name = (e.S("name") ?? "Your picture").Trim(); if (name.Length > 80) name = name.Substring(0, 80); if (name.Length == 0) name = "Your picture";
            p.StartupChoice = id;
            p.StartupCustom = new JObj { { "id", id }, { "sha256", sha }, { "name", name }, { "fit", "fit" }, { "bytes", b.LongLength }, { "width", 1280L }, { "height", 720L }, { "mime", "image/jpeg" }, { "data", Convert.ToBase64String(b) } };
        }

        public static void Write(Ext4Reader fs, ExportPlan plan, Stream output)
        {
            // the .ccs is itself a ZIP: written into a seekable buffer file next to nothing (memory: the ROMs of one HOME, at most a few MB each)
            var ms = new MemoryStream();
            var z = new ZipWriter(ms, DateTime.Now);
            var files = new List<object>(); var included = new List<object>();
            var now = DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
            var sel = plan.Games.Select(g => (object)g.id).ToList();
            var collection = new JObj
            {
                { "schema", "nes-classic-collection/4" }, { "name", plan.Name },
                { "selections", plan.Games.Select(g => (object)new JObj { { "version_id", g.id }, { "title", g.title }, { "source", g.id.StartsWith("p_") ? "personal" : "library" } }).ToList() },
                { "personal_library", plan.Games.Where(g => g.id.StartsWith("p_")).Select(g => (object)new JObj { { "id", g.id }, { "title", g.title } }).ToList() },
                { "ccs_collection", new JObj { { "schema", "ccs-collection/1" }, { "id", "" }, { "name", plan.Name }, { "created", now }, { "modified", now }, { "sel", sel }, { "decisions", new JObj() }, { "coll", new JObj { { "missing", new JObj() } } }, { "home", Home(plan) } } },
                { "exported_from", "an NES Classic's Classic Cart Studio HOME, by a console backup" },
            };
            var cb = Json.Bytes(collection);   // home: the startup screen only (Home)
            var cr = z.Add("collection.json", true, s => s.Write(cb, 0, cb.Length));
            files.Add(new JObj { { "path", "collection.json" }, { "sha256", cr.sha256 }, { "size", (long)cb.Length }, { "kind", "collection" } });
            foreach (var g in plan.Games)
            {
                var name = "roms/" + g.id + g.ext;
                var r = z.Add(name, false, s => fs.ReadFile(g.inode, s, MaxRom));
                if (r.sha256 != g.sha256 || r.info.Size != g.size) throw new IOException("a game file read differently the second time");
                files.Add(new JObj { { "path", name }, { "sha256", g.sha256 }, { "size", g.size }, { "kind", "rom" }, { "version_id", g.id }, { "title", g.title }, { "content_sha256", g.content } });
                included.Add(new JObj { { "version_id", g.id }, { "title", g.title }, { "path", name }, { "sha256", g.sha256 }, { "size", g.size }, { "content_sha256", g.content } });
            }
            var rb = Encoding.UTF8.GetBytes(Readme);
            var rr = z.Add("README.txt", true, s => s.Write(rb, 0, rb.Length));
            files.Add(new JObj { { "path", "README.txt" }, { "sha256", rr.sha256 }, { "size", (long)rb.Length }, { "kind", "readme" } });
            var manifest = new JObj
            {
                { "format", "classic-cart-studio-package" }, { "schema", "ccs-package/2" }, { "kind", "collection" }, { "created", now },
                { "app", new JObj { { "name", "Classic Cart Studio" }, { "version", "console backup " + Engine.EngineInfo.Version } } },
                { "summary", new JObj { { "name", plan.Name }, { "games", plan.Games.Count }, { "personal", plan.Games.Count(g => g.id.StartsWith("p_")) }, { "roms", plan.Games.Count }, { "rom_bytes", plan.Games.Sum(g => g.size) } } },
                { "files", files }, { "roms", new JObj { { "included", included } } },
            };
            var mb = Json.Bytes(manifest);
            z.Add("ccs-manifest.json", true, s => s.Write(mb, 0, mb.Length));
            z.Finish();
            ms.Position = 0; ms.CopyTo(output);
        }
    }
}
