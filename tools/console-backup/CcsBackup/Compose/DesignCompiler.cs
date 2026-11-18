// Classic Cart Studio console backup and restore engine. Copyright (C) 2026 Rance9. GPL-3.0-or-later.
// The HOME Designer's one compiler (HOME-DESIGNER.md): an applicable Shared HOME Design (HomeDesign) and the console's own list of HOME
// games become the compiled design the console's native HOME reads (tools/console-simulator/core/design.h, ccs-home-design-compiled/1):
//   design.json        everything the renderer needs, already resolved: the base theme, each picture place, the decorations in drawing
//                      order (by layer, then as listed), the colors and menu lettering, the folders (each parent before its folders, games
//                      by the console's own game ids), the All Games order, the map shortcuts, the startup choice, two settings
//   img/<sha256>.png|.jpg   the design's own pictures, byte for byte, named by their SHA-256 (only those the console draws)
// The same design and the same game list always give the same bytes: one writer (Json.Write), no time, no randomness, no paths. The
// compiled design's id is the SHA-256 of its design.json; every file's SHA-256 is in design.json and in Manifest (the compiled package
// hashes a plan records). Both Windows hosts and the Python package builder use these bytes as they are (native_home/design_ingest.py
// checks them, never re-creates them). Refused: anything HomeDesign.RequireApplicable refuses.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace CcsBackup.Compose
{
    /// <summary>a game of the console's HOME (its games.json, in order): Id is the HOME's own game id; CatalogId the public catalog version id
    /// when the game has one (for the HOME's library games it is the same as Id)</summary>
    public sealed class ConsoleGame { public string Id, Title, CatalogId; }

    public sealed class CompiledFile { public string Name, Sha256; public long Bytes; }

    public sealed class CompiledDesign
    {
        /// <summary>Schema2: the same, with "regions" (the control groups the design moves). Written only when a design moves one, so a design
        /// without moved controls compiles to the same bytes as before, and a HOME that reads only /1 sets a moved layout aside (never shows
        /// it half-applied)</summary>
        public const string Schema = "ccs-home-design-compiled/1", Schema2 = "ccs-home-design-compiled/2", JsonName = "design.json";
        /// <summary>SHA-256 of design.json: the design's folder name on the console (ccs/data/home-design/d/&lt;Id&gt;)</summary>
        public string Id { get; internal set; }
        public string SourceId { get; internal set; }
        public long SourceRevision { get; internal set; }
        /// <summary>the .ccshome design it was compiled from (HomeDesign.ContentSha256)</summary>
        public string SourceContentSha256 { get; internal set; }
        public string Name { get; internal set; }
        public string Theme { get; internal set; }
        /// <summary>Schema, or Schema2 when the design moves the HOME's controls</summary>
        public string SchemaName { get; internal set; } = Schema;
        public byte[] DesignJson { get; internal set; }
        /// <summary>design.json first, then the pictures by name: the compiled package hashes</summary>
        public IReadOnlyList<CompiledFile> Manifest { get; internal set; }
        public IReadOnlyDictionary<string, byte[]> Files { get; internal set; }
        /// <summary>games the design names that this console does not have, and placeholders left unmapped: left out, never invented</summary>
        public List<string> LeftOut { get; } = new List<string>();
        /// <summary>parts of the design this console HOME keeps but does not draw (honest limits, HOME-DESIGNER.md)</summary>
        public List<string> NotShown { get; } = new List<string>();
        public DesignBudget Budget { get; internal set; }
        public long FileBytes => Manifest.Sum(f => f.Bytes);
        public JObj ManifestJson() => new JObj
        {
            { "schema", SchemaName }, { "id", Id }, { "source", new JObj { { "id", SourceId }, { "revision", SourceRevision }, { "content_sha256", SourceContentSha256 } } },
            { "files", Manifest.Select(f => (object)new JObj { { "name", f.Name }, { "sha256", f.Sha256 }, { "bytes", f.Bytes } }).ToList() }, { "bytes", FileBytes },
        };

        /// <summary>writes the compiled design into dir (created; it must not exist or be empty), then reads every file back</summary>
        public void WriteTo(string dir)
        {
            if (Directory.Exists(dir) && Directory.EnumerateFileSystemEntries(dir).Any()) throw new IOException("the folder for the compiled design is not empty: " + dir);
            foreach (var f in Manifest)
            {
                var p = Path.Combine(dir, f.Name.Replace('/', Path.DirectorySeparatorChar)); Directory.CreateDirectory(Path.GetDirectoryName(p));
                using (var s = new FileStream(p, FileMode.CreateNew, FileAccess.Write)) { var b = Files[f.Name]; s.Write(b, 0, b.Length); s.Flush(true); }
            }
            var back = Read(dir);
            if (back.Id != Id) throw new IOException("the compiled design did not read back the same");
        }

        /// <summary>a compiled design folder, every file checked against design.json (size and SHA-256) and nothing else in it</summary>
        public static CompiledDesign Read(string dir)
        {
            var jp = Path.Combine(dir, JsonName);
            if (!File.Exists(jp) || new FileInfo(jp).Length > 2000000) throw new ComposeException("compiled.missing", "This is not a compiled HOME design (no design.json).");
            var jb = File.ReadAllBytes(jp); var doc = StrictJson.ParseObject(jb, new JsonLimits { MaxBytes = 2000000, MaxDepth = 16, MaxValues = 400000, MaxString = 4000 }, "The compiled design");
            if (doc["schema"] as string != Schema && doc["schema"] as string != Schema2) throw new ComposeException("compiled.schema", "This compiled HOME design is not " + Schema + " or " + Schema2 + ".");
            var files = new Dictionary<string, byte[]> { [JsonName] = jb }; var man = new List<CompiledFile> { new CompiledFile { Name = JsonName, Sha256 = Sha.Of(jb), Bytes = jb.Length } };
            foreach (var f in (doc["files"] as List<object> ?? new List<object>()).OfType<JObj>())
            {
                string name = f["name"] as string, sha = f["sha256"] as string;
                if (name == null || !System.Text.RegularExpressions.Regex.IsMatch(name, "^img/[0-9a-f]{64}\\.(png|jpg)$") || !name.Contains(sha ?? "-")) throw new ComposeException("compiled.file", "The compiled design lists a picture wrongly.");
                var p = Path.Combine(dir, "img", name.Substring(4));
                if (!File.Exists(p)) throw new ComposeException("compiled.missing", "The compiled design's picture " + name + " is missing.");
                var b = File.ReadAllBytes(p);
                if (b.LongLength != Doc.Int(f["bytes"]) || Sha.Of(b) != sha) throw new ComposeException("compiled.changed", "The compiled design's picture " + name + " is not the one design.json names (changed or damaged).");
                files[name] = b; man.Add(new CompiledFile { Name = name, Sha256 = sha, Bytes = b.LongLength });
            }
            var extra = Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories).Select(p => p.Substring(dir.TrimEnd('\\', '/').Length + 1).Replace('\\', '/')).Where(n => !files.ContainsKey(n)).ToList();
            if (extra.Count > 0) throw new ComposeException("compiled.extra", "The compiled design's folder holds a file it does not list (" + Display.Line(extra[0], 60) + ").");
            var src = doc["source"] as JObj;
            return new CompiledDesign
            {
                Id = Sha.Of(jb), DesignJson = jb, Files = files, Manifest = man, Theme = doc["theme"] as string, Name = src?["name"] as string, SchemaName = doc["schema"] as string,
                SourceId = src?["id"] as string, SourceRevision = Doc.Int(src?["revision"]), SourceContentSha256 = src?["content_sha256"] as string,
            };
        }
    }

    public static class DesignCompiler
    {
        /// <summary>the compiled design of an applicable design for a console whose HOME has these games. placeholders: "Your game N" ->
        /// one of the console's game ids (the receiver's explicit choice); an unmapped placeholder and a catalog game the console does not
        /// have are left out (LeftOut). Refused (ComposeException): a design RequireApplicable refuses.</summary>
        public static CompiledDesign Compile(HomeDesign design, IReadOnlyList<ConsoleGame> games, IReadOnlyDictionary<int, string> placeholders = null)
        {
            if (design == null) throw new ArgumentNullException(nameof(design));
            design.RequireApplicable();
            games = games ?? new List<ConsoleGame>();
            var doc = design.Document; var cd = new CompiledDesign();
            var byCatalog = new Dictionary<string, string>(); var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var g in games) { if (string.IsNullOrEmpty(g?.Id)) continue; ids.Add(g.Id); var c = g.CatalogId ?? (GameRef.CatalogId.IsMatch(g.Id) ? g.Id : null); if (c != null && !byCatalog.ContainsKey(c)) byCatalog[c] = g.Id; }
            string Resolve(GameRef r, string where)
            {
                if (r.IsPlaceholder)
                {
                    if (placeholders != null && placeholders.TryGetValue(r.Placeholder, out var gid) && gid != null && ids.Contains(gid)) return gid;
                    cd.LeftOut.Add(where + ": " + r.DisplayName + " (not matched to one of this console's games)"); return null;
                }
                if (byCatalog.TryGetValue(r.Catalog, out var id)) return id;
                cd.LeftOut.Add(where + ": " + r.DisplayName + " (not on this console)"); return null;
            }
            var images = design.Images; var used = new SortedSet<string>(StringComparer.Ordinal);
            var listed = (doc["images"] as List<object> ?? new List<object>()).OfType<JObj>().ToDictionary(x => x["sha256"] as string, x => x);
            string Pic(string sha) { if (sha == null) return null; used.Add(sha); return FileName(sha, listed[sha]["format"] as string); }

            var outDoc = new JObj { { "schema", CompiledDesign.Schema }, { "profile", DesignProfile.Profile } };
            outDoc["source"] = new JObj { { "id", design.Id }, { "revision", design.Revision }, { "content_sha256", design.ContentSha256 }, { "name", design.Name } };
            outDoc["theme"] = design.BaseTheme;
            var slots = new JObj();
            foreach (var s in DesignProfile.SlotIds) { var sha = design.ImageFor(s); if (sha != null) slots[s] = Pic(sha); }
            foreach (var s in DesignProfile.SlotIds.Where(x => design.ImageFor(x) != null && !DesignProfile.ThemeDraws(design.BaseTheme, x)))
                cd.NotShown.Add("the " + DesignProfile.Slots[s].Text + " (" + DesignProfile.ThemeName(design.BaseTheme) + " has no such picture place)");
            var ap = doc["appearance"] as JObj; var a = new JObj();
            if (ap != null)
            {
                if (ap["accent_color"] is string ac) a["accent"] = ac;
                if (ap["panel_color"] is string pc) { a["panel"] = pc; a["panel_opacity"] = ap["panel_opacity"] is long po ? po : 100L; }
                if (ap["text_color"] is string tc) a["text"] = tc;
                if (ap["title_font"] is string tf) a["title_font"] = tf;
                if (ap["text_font"] is string xf) a["text_font"] = xf;
            }
            // decorations in drawing order: by layer, then as listed (the HOME's own controls are always above them)
            var layers = new List<object>();
            var deco = design.Regions.Select((r, i) => (r, i)).Where(t => t.r["kind"] as string != "ui").OrderBy(t => t.r["layer"] is long l ? l : 0L).ThenBy(t => t.i).Select(t => t.r);
            foreach (var r in deco)
            {
                var o = new JObj { { "kind", r["kind"] as string }, { "x", r["x"] }, { "y", r["y"] }, { "w", r["w"] }, { "h", r["h"] }, { "opacity", r["opacity"] is long op ? op : 100L } };
                switch (r["kind"] as string)
                {
                    case "image": o["image"] = Pic(r["image"] as string); break;
                    case "panel": o["color"] = r["color"]; break;
                    case "label": o["text"] = r["text"]; o["font"] = r["font"]; if (r["color"] is string lc) o["color"] = lc; o["align"] = r["align"] as string ?? "left"; break;
                }
                layers.Add(o);
            }
            // folders: each parent before its own folders (by position, then as listed), games by the console's ids
            var folders = design.Folders; object outFolders = null; var fIndex = new Dictionary<string, int>(StringComparer.Ordinal);
            if (folders.Count > 0)
            {
                var ordered = new List<JObj>(); var byParent = folders.Select((f, i) => (f, i)).GroupBy(t => t.f["parent"] as string ?? "").ToDictionary(g => g.Key, g => g.OrderBy(t => t.f["pos"] is long p ? p : 0L).ThenBy(t => t.i).Select(t => t.f).ToList());
                void Walk(string parent) { if (!byParent.TryGetValue(parent ?? "", out var kids)) return; foreach (var k in kids) { ordered.Add(k); Walk(k["id"] as string); } }
                Walk(null);
                var fl = new List<object>();
                for (int i = 0; i < ordered.Count; i++) fIndex[ordered[i]["id"] as string] = i;
                foreach (var f in ordered)
                {
                    var gl = new List<object>(); var seen = new HashSet<string>();
                    foreach (var r in (f["games"] as List<object> ?? new List<object>()).OfType<JObj>().Select(GameRef.Of)) { var id = Resolve(r, "folder \"" + f["name"] + "\""); if (id != null && seen.Add(id)) gl.Add(id); }
                    fl.Add(new JObj { { "id", f["id"] }, { "name", f["name"] }, { "parent", f["parent"] is string ps && fIndex.TryGetValue(ps, out var pi) ? (long)pi : -1L }, { "art", Pic(f["art"] as string) }, { "games", gl } });
                }
                outFolders = fl;
            }
            var order = design.Order; object outOrder = null;
            if (order.Count > 0) { var ol = new List<object>(); var seen = new HashSet<string>(); foreach (var r in order) { var id = Resolve(r, "the game order"); if (id != null && seen.Add(id)) ol.Add(id); } outOrder = ol; }
            var ms = doc["map_shortcuts"] as List<object>;
            var outMap = new List<object> { -1L, -1L };
            if (outFolders != null && ms != null) for (int k = 0; k < 2 && k < ms.Count; k++) if (ms[k] is string sid && fIndex.TryGetValue(sid, out var fi)) outMap[k] = (long)fi;
            var st = doc["settings"] as JObj; var outSettings = new JObj();
            if (st?["reduced_motion"] is bool rm) outSettings["reduced_motion"] = rm;
            if (st?["music"] is bool mu) outSettings["music"] = mu;
            if (st?["hints"] is bool hi && !hi) cd.NotShown.Add("hiding the controller hints (the console HOME always shows them: the controller can focus them)");
            var su = doc["startup"] as JObj; var outStartup = new JObj { { "mode", su?["mode"] as string ?? "random" } };
            if (su?["mode"] as string == "builtin") outStartup["builtin"] = su["builtin"];
            if (su?["mode"] as string == "image") outStartup["image"] = Pic(su["image"] as string);
            // the file list last: every picture the console draws, by name
            var files = new List<object>(); var fileBytes = new Dictionary<string, byte[]>();
            foreach (var sha in used)
            {
                var li = listed[sha]; var name = FileName(sha, li["format"] as string); var b = images[sha];
                files.Add(new JObj { { "name", name }, { "sha256", sha }, { "bytes", (long)b.Length }, { "kind", li["format"] as string }, { "w", li["width"] }, { "h", li["height"] } });
                fileBytes[name] = b;
            }
            // the control groups the design moves in its base theme (in the HOME's order): their places; the theme's own size each
            var regions = new List<object>(); var cr = design.ControlRegions;
            foreach (var u in DesignProfile.UiOrder) if (cr.TryGetValue(u, out var rg)) regions.Add(new JObj { { "ui", u }, { "x", rg["x"] }, { "y", rg["y"] }, { "w", rg["w"] }, { "h", rg["h"] } });
            if (cr.Values.Any(r => r.Has("focus") || r.Has("hit"))) cd.NotShown.Add("a moved region's own focus position and pointer area (the controller's order and the pointer areas are the HOME's own, and move with their group)");
            if (regions.Count > 0) outDoc["schema"] = CompiledDesign.Schema2;
            var budget = design.Budget();
            outDoc["files"] = files; outDoc["slots"] = slots; outDoc["appearance"] = a; outDoc["layers"] = layers; if (regions.Count > 0) outDoc["regions"] = regions; outDoc["folders"] = outFolders; outDoc["order"] = outOrder;
            outDoc["map_shortcuts"] = outMap; outDoc["settings"] = outSettings; outDoc["startup"] = outStartup;
            outDoc["budget"] = new JObj { { "decoded_bytes", budget.DecodeWorkBytes }, { "peak_bytes", budget.PeakDecodedBytes }, { "file_bytes", fileBytes.Values.Sum(b => (long)b.Length) } };
            var jb = Json.Bytes(outDoc);
            if (jb.Length > 2000000) throw new ComposeException("compiled.size", "The compiled design is larger than the console HOME reads (2 MB of settings).");
            fileBytes[CompiledDesign.JsonName] = jb;
            var man = new List<CompiledFile> { new CompiledFile { Name = CompiledDesign.JsonName, Sha256 = Sha.Of(jb), Bytes = jb.Length } };
            man.AddRange(fileBytes.Where(kv => kv.Key != CompiledDesign.JsonName).OrderBy(kv => kv.Key, StringComparer.Ordinal).Select(kv => new CompiledFile { Name = kv.Key, Sha256 = Sha.Of(kv.Value), Bytes = kv.Value.Length }));
            cd.Id = Sha.Of(jb); cd.DesignJson = jb; cd.Files = fileBytes; cd.Manifest = man; cd.Budget = budget; cd.SchemaName = outDoc["schema"] as string;
            cd.SourceId = design.Id; cd.SourceRevision = design.Revision; cd.SourceContentSha256 = design.ContentSha256; cd.Name = design.Name; cd.Theme = design.BaseTheme;
            return cd;
        }

        static string FileName(string sha, string format) => "img/" + sha + (format == "png" ? ".png" : ".jpg");
    }
}
