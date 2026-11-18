// Classic Cart Studio console backup and restore engine. Copyright (C) 2026 Rance9. GPL-3.0-or-later.
// The HOME Designer's one compiler (DesignCompiler) from the command line, so Classic Cart Studio for Windows' HOME package builder
// (tools/companion/native_home/design_compile.py) applies a collection's carried design with exactly the bytes the Workbench makes. Both
// Windows hosts answer it (ClassicCartStudio.exe and CCS-Console-Workbench.exe --design-compile) with this same code; nothing else in
// them starts, no window, no settings, no console:
//   <host> --design-compile --design FILE.ccshome --games GAMES.json --out DIR --result RESULT.json [--placeholders MAP.json]
//   GAMES.json   {"games": [{"id", "title", "catalog"}]}: the HOME package's games, in its order (catalog optional: the public catalog
//                version id when the HOME's own id is not one)
//   MAP.json     {"<placeholder number>": "<one of the games' ids>"}: the person's explicit choices for "Your game N"
//   DIR          a new or empty folder: the compiled design (design.json and img/), written and read back
//   RESULT.json  ccs-home-design-compile-result/1: ok, code, error (in words for the person), id, schema, theme, name, manifest
//                [{name, sha256, bytes}], left_out, not_shown, unsupported
// Exit: 0 compiled; 1 refused (RESULT says why); 2 the request itself is wrong. Reads only the files named; writes only DIR and RESULT.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace CcsBackup.Compose
{
    public static class DesignCompileCli
    {
        public const string Flag = "--design-compile", ResultSchema = "ccs-home-design-compile-result/1";
        const long MaxGamesJson = 32000000, MaxMapJson = 1000000;

        static string Arg(string[] a, string k) { int i = Array.IndexOf(a, k); return i >= 0 && i + 1 < a.Length ? a[i + 1] : null; }

        public static int Run(string[] args)
        {
            string design = Arg(args, "--design"), gamesPath = Arg(args, "--games"), outDir = Arg(args, "--out"), resultPath = Arg(args, "--result"), map = Arg(args, "--placeholders");
            if (design == null || gamesPath == null || outDir == null || resultPath == null) { Console.Error.WriteLine("usage: --design-compile --design FILE --games FILE --out DIR --result FILE [--placeholders FILE]"); return 2; }
            var r = new JObj { { "schema", ResultSchema } }; int rc;
            try { rc = Compile(design, gamesPath, outDir, map, r); }
            catch (ComposeException ex) { r["ok"] = false; r["code"] = ex.Code; r["error"] = ex.Message; rc = 1; }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is FormatException || ex is ArgumentException || ex is InvalidDataException)
            { r["ok"] = false; r["code"] = "request"; r["error"] = "The HOME design could not be compiled: " + ex.Message; rc = 2; }
            try { File.WriteAllBytes(resultPath, Json.Bytes(r)); } catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { Console.Error.WriteLine(ex.Message); return 2; }
            Console.Out.Write(Json.Write(r)); return rc;
        }

        static int Compile(string design, string gamesPath, string outDir, string map, JObj r)
        {
            var d = HomeDesign.Open(design);
            r["name"] = d.Name; r["theme"] = d.BaseTheme; r["unsupported"] = d.Unsupported.Select(x => (object)x).ToList();
            if (!d.CanApply) { r["ok"] = false; r["code"] = d.InspectOnly ? "design.inspect-only" : "design.not-applicable"; r["error"] = (d.InspectOnly ? "it was made by a newer version of Classic Cart Studio" : "it cannot be shown by the console HOME") + ": " + string.Join("; ", d.Unsupported); return 1; }
            if (new FileInfo(gamesPath).Length > MaxGamesJson) throw new InvalidDataException("the game list is too large");
            var games = (Json.Parse(File.ReadAllText(gamesPath, Encoding.UTF8)).A("games") ?? new List<object>()).OfType<Dictionary<string, object>>()
                .Select(g => new ConsoleGame { Id = g.S("id"), Title = g.S("title"), CatalogId = g.S("catalog") }).ToList();
            Dictionary<int, string> ph = null;
            if (map != null)
            {
                if (new FileInfo(map).Length > MaxMapJson) throw new InvalidDataException("the placeholder choices are too large");
                ph = new Dictionary<int, string>();
                foreach (var kv in Json.Parse(File.ReadAllText(map, Encoding.UTF8))) if (int.TryParse(kv.Key, out var n) && kv.Value is string id) ph[n] = id;
            }
            var c = DesignCompiler.Compile(d, games, ph);
            c.WriteTo(outDir);
            r["ok"] = true; r["code"] = null; r["error"] = null; r["id"] = c.Id; r["schema_name"] = c.SchemaName; r["theme"] = c.Theme; r["name"] = c.Name;
            r["manifest"] = c.Manifest.Select(f => (object)new JObj { { "name", f.Name }, { "sha256", f.Sha256 }, { "bytes", f.Bytes } }).ToList();
            r["left_out"] = c.LeftOut.Select(x => (object)x).ToList(); r["not_shown"] = c.NotShown.Select(x => (object)x).ToList();
            return 0;
        }
    }
}
