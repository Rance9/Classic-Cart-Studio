// Classic Cart Studio console backup and restore engine. Copyright (C) 2026 Rance9. GPL-3.0-or-later.
// The NES Classic's own battery saves (SAVE-FORMATS.md): recognizing one, and converting between it and the raw battery memory Classic
// Cart Studio's game player keeps as cart.sav. Written from the documented format facts listed in SAVE-FORMATS.md, not from any other
// converter's code. The one implementation: the Backup Browser and Expert's save drop both call this.
//   Inspect         what a file is (a stock battery save, a suspend point, a Classic Cart Studio saved state, empty, damaged, unknown),
//                   with the check value, its letter case, the payload length and where the save data ends; never a guess
//   ConvertToRaw    stock -> raw, only for an exact game with a battery size in BatteryTable, and only removing zero padding after it
//   ConvertToStock  raw -> stock, only with the exact size, and the wrapper's padding length and letter case confirmed for that game (or,
//                   as a trial, taken from that console's own file for the same game)
// Nothing here changes its input (every output is a new array), reads or writes a file, or talks to a console. Unknown cases are
// refused with a reason; there is no "convert anyway".
using CcsBackup.Compose;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace CcsBackup.Saves
{
    /// <summary>where a save came from (shown with every inspection and conversion)</summary>
    public sealed class SaveSource
    {
        public string Name;            // the file name, e.g. save.sram
        public string Path;            // where it was, e.g. /var/lib/clover/profiles/0/CLV-P-NAANE/save.sram (in a backup: its path there)
        public string ConsoleCode;     // the stock game's code from that path (CLV-P-NAANE), when there is one
        public string Title;           // a display name only; never identity
        public DateTime? ModifiedUtc;  // the save's own date (its file time where it was found)
        public string Origin;          // e.g. "console backup 20261114-173512-1a2b3c4d" or "a file chosen on this computer"
        public JObj ToJson() => new JObj { { "name", Name }, { "path", Path }, { "console_code", ConsoleCode }, { "title", Title }, { "modified_utc", Iso(ModifiedUtc) }, { "origin", Origin } };
        internal static string Iso(DateTime? t) => t.HasValue ? Utc(t.Value).ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture) : null;
        /// <summary>a save date as UTC: a date of unspecified kind is taken as UTC already (save dates are never local times)</summary>
        internal static DateTime Utc(DateTime t) => t.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(t, DateTimeKind.Utc) : t.ToUniversalTime();

        static readonly Regex CodeRe = new Regex("^CLV-[A-Z]-[A-Z0-9]{5}$");
        /// <summary>a source for a file found at a console path; the console code is taken from the path's game folder when it has that form</summary>
        public static SaveSource FromConsolePath(string path, DateTime? modifiedUtc, string origin)
        {
            var parts = (path ?? "").Split('/').Where(p => p.Length > 0).ToList();
            var code = parts.Count >= 2 && CodeRe.IsMatch(parts[parts.Count - 2]) ? parts[parts.Count - 2] : null;
            return new SaveSource { Name = parts.LastOrDefault(), Path = path, ConsoleCode = code, ModifiedUtc = modifiedUtc, Origin = origin };
        }
    }

    /// <summary>the exact game a save belongs to. ContentSha256 is the SHA-256 of the game file's content after its 16-byte iNES header
    /// (and trainer), as Classic Cart Studio identifies every game (tools/companion/CCS-FORMAT.md); it must come from the actual game
    /// file, never from a title or a code.</summary>
    public sealed class GameIdentity
    {
        public string GameSystem = "nes";
        public string ConsoleCode;     // the stock game's code, for a save from the console's own games
        public string ContentSha256;
        public string Title;
        public string IdentitySource;  // how ContentSha256 was established, e.g. "the console's game file in this backup"
        public JObj ToJson() => new JObj { { "game_system", GameSystem }, { "console_code", ConsoleCode }, { "content_sha256", ContentSha256 }, { "title", Title }, { "identity_source", IdentitySource } };
    }

    /// <summary>the stock battery-save wrapper as SAVE-FORMATS.md describes it, with what is verified, believed and unknown</summary>
    public static class StockWrapper
    {
        public const string Id = "nes-classic-stock-battery";   // the registry's save-adapter
        public const int Version = 1;
        public const string Schema = "ccs-stock-save-wrapper/1";
        public const int DigestChars = 40;
        public static readonly IReadOnlyList<string> BatteryNames = new[] { "save.sram", "cartridge.sram" };

        public static JObj Describe() => new JObj
        {
            { "schema", Schema }, { "id", Id }, { "version", Version },
            { "layout", new object[]
                {
                    new JObj { { "field", "check value" }, { "offset", 0 }, { "bytes", DigestChars }, { "content", "the SHA-1 of the payload (every byte after this field), as 40 ASCII hexadecimal characters" }, { "status", "believed" },
                               { "unknown", "letter case on a real console (read: all lowercase or all uppercase is accepted and recorded; write: only a case confirmed for the game, or taken from that console's own file)" } },
                    new JObj { { "field", "payload" }, { "offset", DigestChars }, { "bytes", "the rest of the file" }, { "content", "the game's battery memory, then zero bytes up to the payload length" }, { "status", "believed" },
                               { "unknown", "the payload length for each game, and whether it ever differs from the battery size" } },
                    new JObj { { "field", "anything after the payload" }, { "status", "unknown" }, { "content", "none is expected; a file with a trailing newline or other bytes fails the check value and is refused" } },
                } },
            { "file_names", new JObj { { "save.sram", "believed to be the NES Classic's battery file" }, { "cartridge.sram", "recognized by Hakchi2 CE 3.9.3 when larger than 20 bytes; which console writes it is unknown" } } },
            { "place", "/var/lib/clover/profiles/0/<game code>/ (verified: Hakchi2 CE 3.9.3 game entries); the data storage holds it as /clover/profiles/0/<game code>/" },
            { "not_battery_saves", "suspend points: suspendpoint1..4 folders and 1.state..4.state files (verified: Hakchi2 CE 3.9.3 SaveStateManager), and Classic Cart Studio .ccsstate files" },
            { "evidence", "no real NES Classic save has been examined; the owner's sample in hardware run W02 confirms or corrects every believed field" },
        };
    }

    public sealed class StockSaveInspection
    {
        /// <summary>stock-battery | suspend-point | ccs-state | empty | not-recognized | damaged</summary>
        public string Kind, Code, Text;
        public bool Recognized => Kind == "stock-battery";
        public SaveSource Source;
        public long SourceBytes; public string SourceSha256;
        public string Digest, DigestCase;     // DigestCase: lower | upper | digits-only
        public long PayloadBytes = -1, UsedBytes = -1;   // UsedBytes: the payload up to and including its last non-zero byte
        public bool NameKnown;                // the file name is one the stock software is believed or known to use
        public JObj ToJson() => new JObj
        {
            { "kind", Kind }, { "code", Code }, { "text", Text }, { "wrapper", StockWrapper.Id + "/" + StockWrapper.Version }, { "source", Source?.ToJson() },
            { "source_bytes", SourceBytes }, { "source_sha256", SourceSha256 }, { "check_value", Digest }, { "check_value_case", DigestCase },
            { "payload_bytes", PayloadBytes }, { "used_bytes", UsedBytes }, { "name_known", NameKnown },
        };
    }

    public sealed class SaveConversion
    {
        public bool Ok; public string Code, Text;
        public string Direction;              // to-raw | to-stock
        public byte[] Output; public string OutputName, OutputSha256;
        public bool Confirmed;                // the game's entry is confirmed by real-console gameplay in both directions
        public bool Trial;                    // a comparison copy (W02): exported only; never imported into saves or written to a console
        public bool Synthetic;                // made from a synthetic test entry: never for a console
        public BatteryEntry Entry;
        public JObj Report;                   // ccs-save-conversion/1: source game, core, dates, the conversion and the output
        /// <summary>may go into a save store through SaveImport (a confirmed conversion, or a synthetic one in tests); never a trial</summary>
        public bool Importable => Ok && !Trial && (Confirmed || Synthetic);
        /// <summary>may be written to a console (with that write's own approval): only a confirmed conversion</summary>
        public bool ForConsole => Ok && Confirmed && !Trial && !Synthetic;
    }

    public static class StockSave
    {
        public const string ReportSchema = "ccs-save-conversion/1";
        static readonly byte[] CcsStateMagic = Encoding.ASCII.GetBytes("CCSSTATE");
        static readonly Regex SuspendDir = new Regex("^suspendpoint[0-9]+$", RegexOptions.IgnoreCase), StateFile = new Regex("\\.(state|ccsstate)$", RegexOptions.IgnoreCase), Hex64 = new Regex("^[0-9a-f]{64}$");

        static string Sha256(byte[] b) { using (var h = SHA256.Create()) return Hex.Of(h.ComputeHash(b)); }
        static string Sha1Hex(byte[] b, int off, int n) { using (var h = SHA1.Create()) return Hex.Of(h.ComputeHash(b, off, n)); }
        static bool StartsWith(byte[] b, byte[] m) { if (b.Length < m.Length) return false; for (int i = 0; i < m.Length; i++) if (b[i] != m[i]) return false; return true; }

        /// <summary>a suspend point by where it was found: a suspendpointN folder, or an N.state / .ccsstate file</summary>
        public static bool IsSuspendPointPath(string path)
        {
            var parts = (path ?? "").Replace('\\', '/').Split('/').Where(p => p.Length > 0).ToList();
            return parts.Any(p => SuspendDir.IsMatch(p)) || (parts.Count > 0 && StateFile.IsMatch(parts[parts.Count - 1]));
        }

        static StockSaveInspection Not(StockSaveInspection r, string kind, string code, string text) { r.Kind = kind; r.Code = code; r.Text = text; return r; }

        public static StockSaveInspection Inspect(byte[] bytes, SaveSource source = null)
        {
            var r = new StockSaveInspection { Source = source, SourceBytes = bytes?.LongLength ?? 0, SourceSha256 = bytes == null ? null : Sha256(bytes) };
            var name = source?.Name ?? (source?.Path ?? "").Split('/').LastOrDefault();
            r.NameKnown = name != null && StockWrapper.BatteryNames.Contains(name);
            if (IsSuspendPointPath(source?.Path) || IsSuspendPointPath(source?.Name))
                return Not(r, "suspend-point", "suspend-point", "This file belongs to a suspend point, not to the game's battery save. Suspend points are kept and exported as they are; they are never converted or used as a battery save.");
            if (bytes == null || bytes.Length == 0) return Not(r, "empty", "empty", "The file is empty, so there is no battery save in it.");
            if (StartsWith(bytes, CcsStateMagic)) return Not(r, "ccs-state", "ccs-state", "This is a Classic Cart Studio saved state, not a battery save. It is never converted into one.");
            if (bytes.Length <= StockWrapper.DigestChars) return Not(r, "not-recognized", "too-short", "The file is too short to hold the console's battery-save format (a 40-character check value followed by the save), so nothing in it was used.");
            bool lower = true, upper = true, digits = true;
            for (int i = 0; i < StockWrapper.DigestChars; i++)
            {
                var c = bytes[i];
                bool d = c >= '0' && c <= '9', l = c >= 'a' && c <= 'f', u = c >= 'A' && c <= 'F';
                if (!(d || l)) lower = false; if (!(d || u)) upper = false; if (!d) digits = false;
            }
            if (!lower && !upper) return Not(r, "not-recognized", "no-check-value", "The file does not start with the 40-character check value of the console's battery-save format, so nothing in it was treated as save data.");
            r.Digest = Encoding.ASCII.GetString(bytes, 0, StockWrapper.DigestChars);
            r.DigestCase = digits ? "digits-only" : lower ? "lower" : "upper";
            int n = bytes.Length - StockWrapper.DigestChars;
            if (Sha1Hex(bytes, StockWrapper.DigestChars, n) != r.Digest.ToLowerInvariant())
                return Not(r, "damaged", "check-value-mismatch", "The file's check value does not match its contents: it is damaged, or not in the format this converter knows. Nothing was converted.");
            r.PayloadBytes = n;
            int last = bytes.Length - 1; while (last >= StockWrapper.DigestChars && bytes[last] == 0) last--;
            r.UsedBytes = last + 1 - StockWrapper.DigestChars;
            r.Kind = "stock-battery"; r.Code = "ok"; r.Text = "A battery save in the NES Classic's own format: its check value matches its " + n.ToString("N0", CultureInfo.InvariantCulture) + " bytes of save data.";
            return r;
        }

        static SaveConversion Refuse(SaveConversion c, string code, string text) { c.Ok = false; c.Code = code; c.Text = text + " The original is unchanged."; c.Output = null; return c; }

        /// <summary>the checks both directions share: the system is supported, the game is exact and it is the entry's game</summary>
        static bool GameChecks(SaveConversion c, GameIdentity game, BatteryEntry entry)
        {
            if (game == null || !Capabilities.Allowed("game-system", game.GameSystem) || !Capabilities.Allowed("capability", "nes-stock-save-conversion") || !Capabilities.Allowed("save-adapter", StockWrapper.Id))
            { Refuse(c, "unsupported-system", "Saves of this game system are kept and exported exactly as they are; this version does not convert them."); return false; }
            if (game.ContentSha256 == null || !Hex64.IsMatch(game.ContentSha256))
            { Refuse(c, "no-game-file", "The exact game file this save belongs to is not known, so the save was not converted (a game's name or code is not enough)."); return false; }
            if (entry == null)
            { Refuse(c, "no-battery-size", "There is no recorded battery size for this exact game, so the save was not converted. Sizes differ between games and versions, and none is guessed."); return false; }
            if (entry.ConsoleCode != null && game.ConsoleCode != null && entry.ConsoleCode != game.ConsoleCode)
            { Refuse(c, "wrong-game", "This save belongs to a different game (" + game.ConsoleCode + ") than the size it was offered with (" + entry.ConsoleCode + ")."); return false; }
            if (entry.ContentSha256 != null && entry.ContentSha256 != game.ContentSha256)
            { Refuse(c, "wrong-game", "The game file is not the exact version this battery size is recorded for, so the save was not converted."); return false; }
            if (entry.ContentSha256 == null && (entry.ConsoleCode == null || game.ConsoleCode != entry.ConsoleCode))
            { Refuse(c, "wrong-game", "This save's game could not be matched to a recorded battery size."); return false; }
            if (entry.RawBytes <= 0 || entry.RawBytes > 1 << 20)
            { Refuse(c, "no-battery-size", "The recorded battery size for this game is not usable."); return false; }
            c.Entry = entry;
            c.Synthetic = entry.Status == BatteryEntry.SyntheticTest;
            c.Confirmed = entry.Status == BatteryEntry.ConfirmedStatus && entry.ContentSha256 != null;
            c.Trial = !c.Confirmed && !c.Synthetic;   // a believed size, or the exact game file not recorded yet: comparison copies only
            return true;
        }

        /// <summary>stock -> raw (cart.sav): the payload's first entry.RawBytes bytes, when every byte after them is zero padding</summary>
        public static SaveConversion ConvertToRaw(byte[] stock, GameIdentity game, BatteryEntry entry, SaveSource source = null)
        {
            var c = new SaveConversion { Direction = "to-raw" };
            var insp = Inspect(stock, source);
            if (insp.Kind == "suspend-point" || insp.Kind == "ccs-state") return Refuse(c, insp.Code, insp.Text.Replace(" It is never converted into one.", ""));
            if (!GameChecks(c, game, entry)) return c;
            if (!insp.Recognized) return Refuse(c, insp.Code, insp.Text);
            if (source?.ConsoleCode != null && game.ConsoleCode != null && source.ConsoleCode != game.ConsoleCode) return Refuse(c, "wrong-game", "The save was found in another game's folder (" + source.ConsoleCode + ").");
            long raw = entry.RawBytes;
            if (insp.PayloadBytes < raw) return Refuse(c, "short", "The save holds " + insp.PayloadBytes.ToString("N0", CultureInfo.InvariantCulture) + " bytes, fewer than this game's battery size of " + raw.ToString("N0", CultureInfo.InvariantCulture) + " bytes. Nothing is added to make up the difference.");
            if (entry.StockPayloadBytes.HasValue && insp.PayloadBytes != entry.StockPayloadBytes.Value) return Refuse(c, "unexpected-length", "The save's length is not the length recorded for this game on the console, so it was not converted.");
            for (long i = StockWrapper.DigestChars + raw; i < stock.LongLength; i++)
                if (stock[i] != 0) return Refuse(c, "nonzero-tail", "Bytes after the game's battery size are not empty padding, so nothing was removed and the save was not converted.");
            var outb = new byte[raw]; Array.Copy(stock, StockWrapper.DigestChars, outb, 0, raw);
            c.Ok = true; c.Code = "ok"; c.Output = outb; c.OutputName = "cart.sav"; c.OutputSha256 = Sha256(outb);
            c.Text = c.Trial ? "Converted as a comparison copy. It is not confirmed by play on a real console yet, so it is only exported, never put into your saves." : "Converted.";
            c.Report = Report(c, insp, game, stock, null);
            return c;
        }

        /// <summary>raw (cart.sav) -> stock: exactly entry.RawBytes bytes, zero padding to the console's payload length, the check value in
        /// the console's letter case. The payload length and case come from a confirmed entry; for an entry not confirmed yet only from
        /// that console's own file for the same game (template), and the result is then a comparison copy.</summary>
        public static SaveConversion ConvertToStock(byte[] raw, GameIdentity game, BatteryEntry entry, SaveSource source = null, StockSaveInspection template = null)
        {
            var c = new SaveConversion { Direction = "to-stock" };
            if (raw == null || raw.Length == 0) return Refuse(c, "empty", "The battery save is empty, so there is nothing to convert.");
            if (StartsWith(raw, CcsStateMagic) || IsSuspendPointPath(source?.Path) || IsSuspendPointPath(source?.Name))
                return Refuse(c, "ccs-state", "This is a saved state or a suspend point, not a battery save. Saved states are never converted into battery saves.");
            if (!GameChecks(c, game, entry)) return c;
            if (raw.LongLength != entry.RawBytes) return Refuse(c, "wrong-size", "The battery save holds " + raw.LongLength.ToString("N0", CultureInfo.InvariantCulture) + " bytes; this game's battery size is " + entry.RawBytes.ToString("N0", CultureInfo.InvariantCulture) + " bytes. Nothing is cut off or added.");
            long payload; string kase;
            if (template != null)
            {
                if (!template.Recognized) return Refuse(c, "template-not-recognized", "The console's own file for this game is not a recognized battery save, so its layout cannot be followed.");
                if (template.Source?.ConsoleCode == null || template.Source.ConsoleCode != game.ConsoleCode) return Refuse(c, "template-other-game", "The console file offered as the model is not this game's own save.");
                if (template.DigestCase == "digits-only") return Refuse(c, "case-unknown", "The letter case of the console's check value cannot be told from its own file.");
                if (entry.StockPayloadBytes.HasValue && entry.StockPayloadBytes.Value != template.PayloadBytes) return Refuse(c, "template-differs", "The console's own file for this game has a different length than the one recorded for it.");
                if (entry.DigestCase != null && entry.DigestCase != template.DigestCase) return Refuse(c, "template-differs", "The console's own file for this game writes its check value differently than recorded.");
                payload = template.PayloadBytes; kase = template.DigestCase;
                if (!c.Confirmed && !c.Synthetic) c.Trial = true;
            }
            else
            {
                if (c.Trial || !entry.StockPayloadBytes.HasValue || entry.DigestCase == null)
                    return Refuse(c, "not-confirmed", "The console's own format for this game (its padding and check value) is not confirmed yet, so no console save was made.");
                payload = entry.StockPayloadBytes.Value; kase = entry.DigestCase;
            }
            if (kase != "lower" && kase != "upper") return Refuse(c, "case-unknown", "The letter case of the console's check value is not known for this game.");
            if (payload < raw.LongLength || payload > 1 << 20) return Refuse(c, "unexpected-length", "The console's payload length for this game is shorter than its battery save, so nothing was made.");
            var body = new byte[payload]; Array.Copy(raw, body, raw.Length);
            var digest = Sha1Hex(body, 0, body.Length); if (kase == "upper") digest = digest.ToUpperInvariant();
            var outb = new byte[StockWrapper.DigestChars + payload];
            Encoding.ASCII.GetBytes(digest, 0, StockWrapper.DigestChars, outb, 0); Array.Copy(body, 0, outb, StockWrapper.DigestChars, payload);
            // the result is read back with the reverse conversion: it must give the same battery save, byte for byte
            var back = ConvertToRaw(outb, game, entry, new SaveSource { Name = "check", ConsoleCode = game.ConsoleCode });
            if (!back.Ok || !back.Output.SequenceEqual(raw)) return Refuse(c, "self-check", "The converted save did not read back as the same battery save, so it was not used.");
            c.Ok = true; c.Code = "ok"; c.Output = outb; c.OutputSha256 = Sha256(outb);
            c.OutputName = template?.Source?.Name ?? entry.StockFileName;
            c.Text = c.Trial ? "Converted as a comparison copy, following this console's own file for the game. It is not confirmed by play on a real console yet, so it is only exported, never written to a console." : "Converted.";
            c.Report = Report(c, Inspect(outb), game, raw, source);
            return c;
        }

        static JObj Report(SaveConversion c, StockSaveInspection stockSide, GameIdentity game, byte[] input, SaveSource rawSource)
        {
            var e = c.Entry; var core = Capabilities.Entry("core", e.Core ?? "fceumm");
            var src = c.Direction == "to-raw" ? stockSide.Source : rawSource;
            long padding = stockSide.PayloadBytes >= 0 ? stockSide.PayloadBytes - e.RawBytes : -1;
            var notes = new List<object>
            {
                "The original file is unchanged: it was only read (SHA-256 " + Sha256(input) + ").",
                c.Confirmed ? "This game's conversion is confirmed by play on a real console." : c.Synthetic ? "Made from a synthetic test description, never for a console." : "Not confirmed by play on a real console yet: a comparison copy, exported only.",
                "A battery save is the game's own saved progress. Suspend points are a different thing and are never converted.",
            };
            return new JObj
            {
                { "schema", ReportSchema }, { "direction", c.Direction }, { "status", c.Confirmed ? "confirmed" : c.Synthetic ? "synthetic" : "trial" },
                { "source", new JObj { { "file", src?.ToJson() }, { "bytes", input.LongLength }, { "sha256", Sha256(input) }, { "kept", "unchanged" } } },
                { "game", game.ToJson() },
                { "core", new JObj { { "id", core?.Id }, { "pin", core?.Get("pin") }, { "battery_bytes", e.RawBytes } } },
                { "wrapper", new JObj { { "id", StockWrapper.Id }, { "version", StockWrapper.Version }, { "check_value", stockSide.Digest }, { "check_value_case", stockSide.DigestCase }, { "payload_bytes", stockSide.PayloadBytes }, { "padding_bytes", padding }, { "padding_all_zero", true } } },
                { "output", new JObj { { "name", c.OutputName }, { "bytes", c.Output.LongLength }, { "sha256", c.OutputSha256 } } },
                { "entry", e.ToJson() },
                { "notes", notes },
            };
        }
    }
}
