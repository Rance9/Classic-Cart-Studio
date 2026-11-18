// Classic Cart Studio console backup and restore engine tests: the NES Classic's own battery saves. GPL-3.0-or-later.
// Every save here is a synthetic vector made in memory: pseudo-random bytes in the documented wrapper, and a "game" whose identity is the
// SHA-256 of a text, never a game file. No real save, ROM or console is used.
using CcsBackup.Compose;
using CcsBackup.Ext4;
using CcsBackup.Saves;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using static CcsBackup.Tests.Program;

namespace CcsBackup.Tests
{
    static partial class Tests
    {
        static byte[] Pseudo(int n, int seed) { var b = new byte[n]; var r = new Random(seed); r.NextBytes(b); return b; }
        static string Sha1(byte[] b) { using (var h = SHA1.Create()) return CcsBackup.Hex.Of(h.ComputeHash(b)); }
        /// <summary>a synthetic stock save: the 40-character SHA-1 of the payload, then the payload (battery bytes, then zero padding)</summary>
        static byte[] Wrap(byte[] battery, int payload, bool upper = false, byte tailByte = 0, int tailAt = -1)
        {
            var p = new byte[payload]; Array.Copy(battery, p, Math.Min(battery.Length, payload));
            if (tailAt >= 0) p[tailAt] = tailByte;
            var d = Sha1(p); if (upper) d = d.ToUpperInvariant();
            return Encoding.ASCII.GetBytes(d).Concat(p).ToArray();
        }
        static string GameSha(string what) => Sha(Encoding.UTF8.GetBytes("synthetic game: " + what));

        public static void SavesFormat()
        {
            const string code = "CLV-P-SYNTH";
            var gameA = new GameIdentity { ConsoleCode = code, ContentSha256 = GameSha("A"), Title = "Synthetic A", IdentitySource = "test" };
            var battery = Pseudo(8192, 1); battery[8191] = 0x5A;   // the last battery byte is in use: nothing after it may be cut
            var stock = Wrap(battery, 32768); var stockSha = Sha(stock);
            var entry = BatteryEntry.Synthetic(code, gameA.ContentSha256, 8192, 32768, "lower");
            var src = SaveSource.FromConsolePath("/var/lib/clover/profiles/0/" + code + "/save.sram", new DateTime(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc), "synthetic console backup");

            // ---- the wrapper description and the registry
            var desc = StockWrapper.Describe();
            Check(desc["schema"] as string == "ccs-stock-save-wrapper/1" && ((object[])desc["layout"]).Length == 3 && Capabilities.Allowed("save-adapter", StockWrapper.Id), "the wrapper description is versioned, lists its fields with their status, and names the registry's supported save format");
            Check(src.ConsoleCode == code && src.Name == "save.sram", "a console path gives the save's game code and file name (" + src.ConsoleCode + ")");

            // ---- inspection
            var i = StockSave.Inspect(stock, src);
            Check(i.Recognized && i.DigestCase == "lower" && i.PayloadBytes == 32768 && i.UsedBytes == 8192 && i.NameKnown && i.SourceSha256 == stockSha, "a synthetic stock save is recognized: check value matches, lowercase, 32,768 payload bytes, data up to byte 8,192 (" + i.Code + ")");
            var iu = StockSave.Inspect(Wrap(battery, 32768, true), src);
            Check(iu.Recognized && iu.DigestCase == "upper", "an uppercase check value is recognized and recorded as uppercase");
            var mixed = (byte[])stock.Clone(); for (int k = 0; k < 40; k++) if (mixed[k] >= 'a' && mixed[k] <= 'f') { mixed[k] = (byte)char.ToUpperInvariant((char)mixed[k]); break; }
            Check(StockSave.Inspect(mixed).Code == "no-check-value", "a check value in mixed letter case is not taken for one");
            var bad = (byte[])stock.Clone(); bad[100] ^= 1;
            Check(StockSave.Inspect(bad).Kind == "damaged" && StockSave.Inspect(bad).Code == "check-value-mismatch", "one changed save byte: damaged (the check value no longer matches)");
            Check(StockSave.Inspect(stock.Concat(new byte[] { 10 }).ToArray()).Code == "check-value-mismatch", "a trailing newline after the payload: refused, not trimmed");
            var nohex = (byte[])stock.Clone(); nohex[0] = (byte)'g';
            Check(StockSave.Inspect(nohex).Code == "no-check-value", "a file that does not start with 40 hexadecimal characters: not recognized, no bytes stripped");
            Check(StockSave.Inspect(battery).Code == "no-check-value" || StockSave.Inspect(battery).Code == "check-value-mismatch", "raw battery bytes are not mistaken for a stock save");
            Check(StockSave.Inspect(stock.Take(40).ToArray()).Code == "too-short" && StockSave.Inspect(new byte[0]).Kind == "empty", "an empty file and a file of just a check value: no save in them");
            Check(stock.SequenceEqual(stock) && Sha(stock) == stockSha, "inspection never changes the bytes it reads");

            // ---- both directions, synthetic entry
            var toRaw = StockSave.ConvertToRaw(stock, gameA, entry, src);
            Check(toRaw.Ok && toRaw.Output.SequenceEqual(battery) && toRaw.OutputName == "cart.sav" && toRaw.Synthetic && toRaw.Importable && !toRaw.ForConsole && !toRaw.Trial, "stock -> raw: exactly the 8,192 battery bytes; synthetic, importable in tests, never for a console (" + toRaw.Code + ")");
            Check(Sha(stock) == stockSha, "the original stock bytes are unchanged after the conversion (hash before = after)");
            var toStock = StockSave.ConvertToStock(toRaw.Output, gameA, entry, new SaveSource { Name = "cart.sav", Origin = "test" });
            Check(toStock.Ok && toStock.Output.SequenceEqual(stock) && toStock.OutputName == "save.sram", "raw -> stock: the same file as the original, byte for byte (round trip)");
            var upEntry = BatteryEntry.Synthetic(code, gameA.ContentSha256, 8192, 32768, "upper");
            var upStock = Wrap(battery, 32768, true);
            Check(StockSave.ConvertToStock(battery, gameA, upEntry).Output?.SequenceEqual(upStock) == true && StockSave.ConvertToRaw(upStock, gameA, upEntry, src).Output?.SequenceEqual(battery) == true, "an uppercase entry: both directions round-trip with an uppercase check value");
            var rep = toRaw.Report; var repSrc = (JObj)((JObj)rep["source"])["file"]; var repCore = (JObj)rep["core"]; var repOut = (JObj)rep["output"]; var repWr = (JObj)rep["wrapper"];
            Check(rep["schema"] as string == "ccs-save-conversion/1" && repSrc["console_code"] as string == code && repSrc["modified_utc"] as string == "2026-11-01T12:00:00Z" && ((JObj)rep["source"])["sha256"] as string == stockSha
                  && repCore["id"] as string == "fceumm" && repCore["pin"] as string == "ac0d00a7fc01166f84c7bac01d1084ebfd2b865e" && (long)repCore["battery_bytes"] == 8192
                  && repOut["sha256"] as string == Sha(battery) && (long)repWr["padding_bytes"] == 24576 && rep["status"] as string == "synthetic",
                "the report shows the source game, date and hash, the core and its battery size, the padding removed and the output");

            // ---- refusals
            var r = StockSave.ConvertToRaw(stock, gameA, null, src);
            Check(!r.Ok && r.Code == "no-battery-size" && r.Output == null, "no recorded battery size for the game: refused, no size guessed (" + r.Text + ")");
            r = StockSave.ConvertToRaw(stock, new GameIdentity { ConsoleCode = code, ContentSha256 = GameSha("B") }, entry, src);
            Check(!r.Ok && r.Code == "wrong-game", "another version of the game (another game file): refused");
            r = StockSave.ConvertToRaw(stock, new GameIdentity { ConsoleCode = "CLV-P-OTHER", ContentSha256 = gameA.ContentSha256 }, entry, src);
            Check(!r.Ok && r.Code == "wrong-game", "a save offered for another game's code: refused");
            r = StockSave.ConvertToRaw(stock, gameA, entry, SaveSource.FromConsolePath("/var/lib/clover/profiles/0/CLV-P-ELSEW/save.sram", null, "x"));
            Check(!r.Ok && r.Code == "wrong-game", "a save found in another game's folder: refused");
            r = StockSave.ConvertToRaw(stock, new GameIdentity { ConsoleCode = code, Title = "Synthetic A" }, entry, src);
            Check(!r.Ok && r.Code == "no-game-file", "only a title and code, no exact game file: refused");
            r = StockSave.ConvertToRaw(stock, new GameIdentity { GameSystem = "snes", ConsoleCode = code, ContentSha256 = gameA.ContentSha256 }, entry, src);
            Check(!r.Ok && r.Code == "unsupported-system", "a save of the reserved SNES game system: refused, kept as it is");
            r = StockSave.ConvertToRaw(bad, gameA, entry, src);
            Check(!r.Ok && r.Code == "check-value-mismatch", "a damaged wrapper: refused");
            var susp = SaveSource.FromConsolePath("/var/lib/clover/profiles/0/" + code + "/suspendpoint1/state", null, "x");
            r = StockSave.ConvertToRaw(stock, gameA, entry, susp);
            Check(!r.Ok && r.Code == "suspend-point" && StockSave.Inspect(stock, susp).Kind == "suspend-point", "a suspend point offered as a battery save (its folder): refused, even when its bytes look like one");
            r = StockSave.ConvertToRaw(stock, gameA, entry, new SaveSource { Name = "1.state", ConsoleCode = code });
            Check(!r.Ok && r.Code == "suspend-point", "a numbered .state file offered as a battery save: refused");
            var ccsState = Encoding.ASCII.GetBytes("CCSSTATE").Concat(new byte[8184]).ToArray();
            Check(StockSave.Inspect(ccsState).Kind == "ccs-state" && StockSave.ConvertToStock(ccsState, gameA, entry).Code == "ccs-state", "a Classic Cart Studio saved state of the battery size offered as a battery save: refused in both directions");
            Check(StockSave.ConvertToStock(battery, gameA, entry, new SaveSource { Name = "slot1.ccsstate" }).Code == "ccs-state", "a file named as a saved state: refused");
            // truncation and padding attempts
            Check(StockSave.ConvertToStock(battery.Concat(new byte[1]).ToArray(), gameA, entry).Code == "wrong-size", "raw -> stock with one byte too many (even a zero): refused, nothing cut off");
            Check(StockSave.ConvertToStock(battery.Take(8191).ToArray(), gameA, entry).Code == "wrong-size", "raw -> stock one byte short: refused, nothing added");
            Check(StockSave.ConvertToStock(Pseudo(4096, 2), gameA, entry).Code == "wrong-size", "a 4,096-byte raw save for an 8,192-byte game: refused");
            Check(StockSave.ConvertToRaw(Wrap(battery, 32768, false, 0x01, 20000), gameA, entry, src).Code == "nonzero-tail", "stock -> raw with a non-zero byte in the padding (valid check value): refused, nothing removed");
            Check(StockSave.ConvertToRaw(Wrap(battery, 32768, false, 0x01, 8192), gameA, entry, src).Code == "nonzero-tail", "a non-zero byte right after the battery size: refused");
            Check(StockSave.ConvertToRaw(Wrap(battery, 4096), gameA, entry, src).Code == "short", "a stock payload shorter than the battery size: refused, nothing padded");
            Check(StockSave.ConvertToRaw(Wrap(battery, 16384), gameA, entry, src).Code == "unexpected-length", "a stock payload length other than the one recorded for the game: refused");
            var noStockLen = BatteryEntry.Synthetic(code, gameA.ContentSha256, 8192, null, null);
            Check(StockSave.ConvertToStock(battery, gameA, noStockLen).Code == "not-confirmed", "raw -> stock when the console's padding and case are not recorded: refused (not written after a warning)");

            // ---- the production table: nothing confirmed, nothing guessed
            var all = BatteryTable.All;
            Check(all.Count > 0 && all.All(e => e.Status == BatteryEntry.NeedsConfirmation && !e.Confirmed && e.Sources.Count > 0 && !string.IsNullOrEmpty(e.Basis) && e.RawBytes > 0), "every table entry needs real-console confirmation, and carries its basis and sources (" + all.Count + ")");
            Check(all.All(e => e.Status != BatteryEntry.SyntheticTest), "no synthetic entry in the table");
            var zelda = all.Single(e => e.ConsoleCode == "CLV-P-NAANE");
            Check(zelda.ContentSha256 == null && zelda.StockPayloadBytes == null && zelda.DigestCase == null && zelda.RawBytes == 8192, "the W02 entry: the exact game file, the console's payload length and case are not recorded; 8,192 bytes believed");
            var consoleGame = new GameIdentity { ConsoleCode = "CLV-P-NAANE", ContentSha256 = GameSha("console file"), IdentitySource = "test" };
            Check(BatteryTable.For(consoleGame) == zelda && BatteryTable.For(new GameIdentity { ContentSha256 = GameSha("console file") }) == null, "the table finds an entry by the save's game code only when the exact file is not recorded, never by a game file alone");
            var w02src = SaveSource.FromConsolePath("/var/lib/clover/profiles/0/CLV-P-NAANE/save.sram", new DateTime(2026, 10, 1, 8, 0, 0, DateTimeKind.Utc), "synthetic");
            var w02 = Wrap(battery, 32768);
            var trial = StockSave.ConvertToRaw(w02, consoleGame, zelda, w02src);
            Check(trial.Ok && trial.Trial && !trial.Importable && !trial.ForConsole && trial.Report["status"] as string == "trial", "a believed entry gives only a comparison copy (trial): never importable, never for a console");
            Check(StockSave.ConvertToStock(trial.Output, consoleGame, zelda).Code == "not-confirmed", "raw -> stock for the unconfirmed entry without the console's own file: refused");
            var model = StockSave.Inspect(w02, w02src);
            var back = StockSave.ConvertToStock(trial.Output, consoleGame, zelda, null, model);
            Check(back.Ok && back.Trial && back.Output.SequenceEqual(w02) && !back.ForConsole, "W02's offline check: following the console's own file, raw -> stock gives that file back byte for byte (a trial, never written to a console)");
            var otherModel = StockSave.Inspect(w02, SaveSource.FromConsolePath("/var/lib/clover/profiles/0/CLV-P-NAAAE/save.sram", null, "x"));
            Check(StockSave.ConvertToStock(trial.Output, consoleGame, zelda, null, otherModel).Code == "template-other-game", "another game's file as the model: refused");
            Check(StockSave.ConvertToStock(trial.Output, consoleGame, zelda, null, StockSave.Inspect(bad, w02src)).Code == "template-not-recognized", "a damaged file as the model: refused");

            // ---- the fixture's stock layout (synthetic bytes): found, not recognized, nothing stripped
            using (var f = File.OpenRead(Path.Combine(Fx, "data-stock.img")))
            {
                var fs = new Ext4Reader(f); var ino = fs.Lookup("/clover/profiles/0/CLV-P-NAAAE/cartridge.sram");
                var b = ino == null ? null : fs.ReadAll(ino.Value, 1 << 20);
                var fi = StockSave.Inspect(b, SaveSource.FromConsolePath("/clover/profiles/0/CLV-P-NAAAE/cartridge.sram", null, "fixture data-stock.img"));
                Check(b != null && b.Length == 8192 && !fi.Recognized && fi.Source.ConsoleCode == "CLV-P-NAAAE" && fi.NameKnown, "the fixture's synthetic cartridge.sram: its game code and name are read from where it is, and its bytes are not recognized as a wrapped save (" + fi.Code + ")");
            }
        }

        public static void SavesImport()
        {
            const string code = "CLV-P-SYNTH";
            var dir = NewDir("saves-import");
            var older = new DateTime(2026, 10, 1, 9, 0, 0, DateTimeKind.Utc); var newer = new DateTime(2026, 11, 10, 18, 30, 0, DateTimeKind.Utc);
            var a = Pseudo(8192, 11); var b = Pseudo(8192, 12); var c = Pseudo(8192, 13);
            Func<string, string> P = n => Path.Combine(dir, n);

            // add
            var p = SaveImport.Plan(dir, new SaveFile { Name = "cart.sav", Bytes = a, ModifiedUtc = older, Origin = "test" });
            var o = SaveImport.Apply(p);
            Check(p.Status == "add" && o.State == "added" && File.ReadAllBytes(P("cart.sav")).SequenceEqual(a) && File.GetLastWriteTimeUtc(P("cart.sav")) == older, "nothing there: the save is added, with the save's own date");
            Check(SaveImport.Apply(SaveImport.Plan(dir, new SaveFile { Name = "cart.sav", Bytes = a, ModifiedUtc = older })).State == "same", "the same save again: nothing changes");

            // an older save here, a newer one coming
            p = SaveImport.Plan(dir, new SaveFile { Name = "cart.sav", Bytes = b, ModifiedUtc = newer, Origin = "the converted console save" });
            o = SaveImport.Apply(p);
            Check(p.Status == "older-here" && o.State == "kept" && File.ReadAllBytes(P("cart.sav")).SequenceEqual(a) && SaveImport.ListReplaced(dir).Count == 0, "a different older save here: kept by default, nothing written");
            o = SaveImport.Apply(p, SaveImport.Replace);
            var kept = SaveImport.ListReplaced(dir);
            Check(o.Ok && o.State == "replaced" && File.ReadAllBytes(P("cart.sav")).SequenceEqual(b) && kept.Count == 1 && kept[0].Intact && File.ReadAllBytes(kept[0].KeptPath).SequenceEqual(a), "replace chosen: the new save is in place and the old one is kept first, exactly");
            Check(kept[0].Name == "cart.sav" && kept[0].OriginalModifiedUtc == older && File.GetLastWriteTimeUtc(kept[0].KeptPath) == older && kept[0].ReplacedBySha256 == Sha(b) && kept[0].Choice == "replace" && kept[0].IncomingOrigin == "the converted console save",
                "the kept save has its name and its own date, and the record says what replaced it and why");

            // a newer save here, an older one coming
            p = SaveImport.Plan(dir, new SaveFile { Name = "cart.sav", Bytes = c, ModifiedUtc = older });
            Check(p.Status == "newer-here" && p.Choices.SequenceEqual(new[] { SaveImport.ReplaceNewer }), "the save here is newer: only the explicit Replace Newer Save choice applies (" + p.Text + ")");
            o = SaveImport.Apply(p, SaveImport.Replace);
            Check(!o.Ok && o.State == "refused" && File.ReadAllBytes(P("cart.sav")).SequenceEqual(b) && SaveImport.ListReplaced(dir).Count == 1, "an ordinary Replace over a newer save: refused, nothing changed");
            Check(SaveImport.Apply(p).State == "kept" && File.ReadAllBytes(P("cart.sav")).SequenceEqual(b), "the default keeps the newer save");
            o = SaveImport.Apply(p, SaveImport.ReplaceNewer);
            kept = SaveImport.ListReplaced(dir);
            Check(o.Ok && File.ReadAllBytes(P("cart.sav")).SequenceEqual(c) && kept.Count == 2 && kept.All(k => k.Intact) && kept.Any(k => File.ReadAllBytes(k.KeptPath).SequenceEqual(b)) && kept.Select(k => k.Folder).Distinct().Count() == 2,
                "Replace Newer Save: replaced, and the newer save kept in its own folder (the earlier kept copy untouched)");

            // undated, different size, a state, a changed folder
            p = SaveImport.Plan(dir, new SaveFile { Name = "cart.sav", Bytes = a, ModifiedUtc = null });
            Check(p.Status == "undated" && SaveImport.Apply(p, SaveImport.Replace).State == "refused", "an incoming save without a date: which is newer cannot be told; an ordinary Replace is refused");
            p = SaveImport.Plan(dir, new SaveFile { Name = "cart.sav", Bytes = Pseudo(2048, 14), ModifiedUtc = newer.AddDays(1) });
            Check(p.Status == "refused" && p.Code == "different-size" && SaveImport.Apply(p, SaveImport.ReplaceNewer).State == "refused" && File.ReadAllBytes(P("cart.sav")).SequenceEqual(c), "a battery save of another size: never put over this one, whatever the choice");
            var st = Encoding.ASCII.GetBytes("CCSSTATE").Concat(new byte[8184]).ToArray();
            Check(SaveImport.Plan(dir, new SaveFile { Name = "cart.sav", Bytes = st, ModifiedUtc = newer.AddDays(2) }).Code == "state-as-battery", "a saved state offered as cart.sav: refused");
            Check(SaveImport.Plan(dir, new SaveFile { Name = "1.state", Bytes = a, ModifiedUtc = newer }).Code == "not-a-battery-save", "a suspend-point file: not imported as a battery save");
            Check(SaveImport.Plan(dir, new SaveFile { Name = "..\\cart.sav", Bytes = a }).Status == "refused" && SaveImport.Plan(dir, new SaveFile { Name = "x/cart.sav", Bytes = a }).Status == "refused", "a name with a path in it: refused");
            p = SaveImport.Plan(dir, new SaveFile { Name = "cart.sav", Bytes = b, ModifiedUtc = newer.AddDays(3) });
            File.WriteAllBytes(P("cart.sav"), a);   // the game saved again after the plan was shown
            o = SaveImport.Apply(p, SaveImport.ReplaceNewer);
            Check(!o.Ok && o.Text.Contains("changed") && File.ReadAllBytes(P("cart.sav")).SequenceEqual(a) && SaveImport.ListReplaced(dir).Count == 2, "the save changed after the plan: nothing written, nothing kept, the new save in place");

            // recovery: a kept save goes back by the same rules (and the save it replaces is kept in turn)
            File.WriteAllBytes(P("cart.sav"), c); File.SetLastWriteTimeUtc(P("cart.sav"), older);
            var keptB = SaveImport.ListReplaced(dir).First(k => File.ReadAllBytes(k.KeptPath).SequenceEqual(b));
            var rec = SaveImport.Recover(keptB);
            p = SaveImport.Plan(dir, rec);
            o = SaveImport.Apply(p, p.Status == "older-here" ? SaveImport.Replace : SaveImport.ReplaceNewer);
            kept = SaveImport.ListReplaced(dir);
            Check(rec.ModifiedUtc == keptB.OriginalModifiedUtc && o.Ok && File.ReadAllBytes(P("cart.sav")).SequenceEqual(b) && kept.Count == 3 && kept.Any(k => File.ReadAllBytes(k.KeptPath).SequenceEqual(c)), "a kept save put back: it returns with its own date, and the save it replaced is kept too");
            Check(kept.All(k => k.Intact) && File.Exists(keptB.KeptPath), "every kept copy is still intact; none was ever written over");

            // imports of conversions
            var game = new GameIdentity { ConsoleCode = code, ContentSha256 = GameSha("A") };
            var entry = BatteryEntry.Synthetic(code, game.ContentSha256, 8192, 32768, "lower");
            var battery = Pseudo(8192, 21); var stock = Wrap(battery, 32768);
            var conv = StockSave.ConvertToRaw(stock, game, entry, SaveSource.FromConsolePath("/var/lib/clover/profiles/0/" + code + "/save.sram", older, "x"));
            var d2 = NewDir("saves-import-conv");
            p = SaveImport.Plan(d2, new SaveFile { Name = "cart.sav", Bytes = conv.Output, ModifiedUtc = older }, conv);
            Check(p.Status == "add" && SaveImport.Apply(p).Ok && File.ReadAllBytes(Path.Combine(d2, "cart.sav")).SequenceEqual(battery), "a converted save that may be imported (synthetic here) goes in as cart.sav");
            Check(SaveImport.Plan(d2, new SaveFile { Name = "cart.sav", Bytes = Pseudo(8192, 22), ModifiedUtc = newer }, conv).Code == "not-the-conversion", "bytes other than the conversion shown: refused");
            var zelda = BatteryTable.All.Single(e => e.ConsoleCode == "CLV-P-NAANE");
            var trial = StockSave.ConvertToRaw(stock, new GameIdentity { ConsoleCode = "CLV-P-NAANE", ContentSha256 = GameSha("console file") }, zelda, SaveSource.FromConsolePath("/var/lib/clover/profiles/0/CLV-P-NAANE/save.sram", older, "x"));
            var d3 = NewDir("saves-import-trial");
            p = SaveImport.Plan(d3, new SaveFile { Name = "cart.sav", Bytes = trial.Output, ModifiedUtc = older }, trial);
            Check(trial.Ok && p.Status == "refused" && p.Code == "trial" && SaveImport.Apply(p).State == "refused" && !File.Exists(Path.Combine(d3, "cart.sav")), "a comparison copy (believed entry): refused, nothing written");
        }
    }
}
