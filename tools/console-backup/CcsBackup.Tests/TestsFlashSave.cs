// Classic Cart Studio console backup engine tests: a Mapper 30 (UNROM 512) flash save in a console's Classic Cart Studio data.
// GPL-3.0-or-later. Simulated consoles only (files on this PC); nothing here opens a console session of any kind.
// The player keeps such a save as the game's cartridge save (cart.sav): 512 bytes of little-endian write counters, then the 512 KiB flash,
// 524,800 bytes, nearly all one byte (boards/unrom512.c). It must reach a console backup and come out of the Backup Browser exactly as the
// player wrote it: not trimmed, rebuilt, converted or refused for compressing well.
using CcsBackup.Browse;
using CcsBackup.Device;
using CcsBackup.Engine;
using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using static CcsBackup.Tests.Program;

namespace CcsBackup.Tests
{
    static partial class Tests
    {
        /// <summary>the cartridge save a generated Mapper 30 test cartridge leaves after programming $42 at $8100: sector 0 of bank 0
        /// copied from its ROM (bank marker 0, then $FF) with that byte programmed, its write counter 1, everything else zero. The same
        /// bytes as the Windows and browser players wrote in the 0.11.7 candidate checks (SHA-256 9e512b6a…)</summary>
        static byte[] Mapper30FlashSave()
        {
            var b = new byte[524800]; b[0] = 1; const int f = 512;
            for (int i = 1; i < 4096; i++) b[f + i] = 0xFF;
            b[f + 0x100] = 0x42; return b;
        }

        static string HexSha(byte[] b) { using (var h = SHA256.Create()) return string.Concat(h.ComputeHash(b).Select(x => x.ToString("x2"))); }

        public static void FlashSave()
        {
            var flash = Mapper30FlashSave();
            Check(HexSha(flash) == "9e512b6a51a4685b99e869be938fa5daa905af670a4f44b8a955d5b890021278", "the test's flash save is the one the players wrote (524,800 bytes)");
            const string game = "c2a4b6c8d0e2f4a6b8c0d2e4f6a8b0c2d4e6f8a0b2c4d6e8f0a2b4c6d8e0f2a4";
            var rel = "ccs/data/saves/" + game + "/cart.sav";
            SimulatedConsole.FileStore = new DebugfsFileStore();
            try
            {
                var s = SettingsIn(NewDir("flash-save"), false); var con = Console_("modified-noorig");
                SimulatedConsole.FileStore.Write(con, rel, flash);
                var o = Backup(con, s);
                Check(o.Ok, "a backup of the console holding the flash save (" + o.State + " " + o.Problem + ")");
                if (!o.Ok) return;
                var op = BackupContents.Open(o.PrimaryPath, s);
                Check(op.Ok, "the backup opens in the Backup Browser after its full check (" + op.Problem + ")");
                if (!op.Ok) return;
                using (var c = op.Contents)
                {
                    var e = c.Entries.FirstOrDefault(x => x.Source == EntrySource.DataFile && x.ImagePath == "/" + rel);
                    Check(e != null && e.Kind == EntryKind.File && e.Size == flash.Length && e.Sha256 == HexSha(flash) && e.Category == Category.Saves,
                        "the backup lists it as a save, at its size and with its SHA-256 (" + (e == null ? "missing" : e.Size + " " + e.Category) + ")");
                    var g = c.Games.FirstOrDefault(x => x.ContentSha256 == game);
                    Check(g != null && g.BatterySaves.Any(x => x.ImagePath == "/" + rel), "the Backup Browser shows it as that game's cartridge save");
                    if (e == null) return;
                    var x1 = Exporter.Export(c, One(c, e, NewDir("flash-save-export")));
                    var p = x1.Files.FirstOrDefault() == null ? null : Path.Combine(x1.Folder, x1.Files.First().RelativePath);
                    Check(x1.Ok && p != null && File.ReadAllBytes(p).SequenceEqual(flash), "exported by itself, it is the player's file byte for byte (" + x1.Problem + ")");
                    if (g != null)
                    {
                        var x2 = Exporter.Export(c, ExportRequest.GameBundle(c, g, NewDir("flash-save-bundle")));
                        var f2 = x2.Files.FirstOrDefault(f => f.RelativePath.EndsWith("cart.sav", StringComparison.Ordinal));
                        Check(x2.Ok && f2 != null && File.ReadAllBytes(Path.Combine(x2.Folder, f2.RelativePath)).SequenceEqual(flash), "in the game's bundle too, byte for byte (" + x2.Problem + ")");
                    }
                }
            }
            finally { SimulatedConsole.FileStore = null; }
        }
    }
}
