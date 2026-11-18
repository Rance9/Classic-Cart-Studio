// Classic Cart Studio console backup and restore engine. Copyright (C) 2026 Rance9. GPL-3.0-or-later.
// What an owner's system image is (Console Composer's Browse for Firmware, a CCS Nucleus choice), read on the PC from the chosen file
// before anything is sent: a console system's root (SquashFS, .hsqs: the decrypted system part, as hakchi's own dump writes it), never
// a startup software (kernel) file. Its system and region are decided by the rules hakchi itself applies at every start (hakchi 3.9.3,
// etc/preinit.d/b0010_functions softwareCheck): /etc/clover makes it a Nintendo Classic system (NES, region USA); /etc/clover/boardtype
// "dp-shvc" makes it a Super NES one; "dp-hvcj" in /etc/issue a Famicom one; /etc/clover/REGION "JPN" or "EUR" its region;
// /usr/game/version a Mega Drive one (another console's hardware: refused here). The file is opened read-only; only the superblock and a
// few small named files are read (gzip or xz, SquashFs.cs).
using CcsBackup.Device;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace CcsBackup.Engine
{
    public sealed class SystemImageInfo
    {
        public string Platform, Region, Board, Compression, Why, Version, RegionFile;   // Version, RegionFile: /etc/clover/VERSION and REGION as recorded (plain text only)
        public bool Ok => Why == null;
        public string Name => Platform == "snes" ? "SNES Classic system" : Platform == "hvcj" ? "Famicom Mini system" : "NES Classic system";
        public string RegionName => Region == "jpn" ? "Japan" : Region == "eur" ? "Europe" : "USA";
        public string Text => Name + " (" + RegionName + ")";
        public string DefaultLabel => Platform == "snes" ? "My SNES System" : Platform == "hvcj" ? "My Famicom System" : "My NES System";
        public List<string> Notes = new List<string>();
        public JObj ToJson() => new JObj { { "platform", Platform }, { "region", Region }, { "board", Board }, { "compression", Compression }, { "version", Version }, { "region_file", RegionFile }, { "text", Ok ? Text : null } };
    }

    public static class SystemImage
    {
        const int Small = 4096;

        static string Cat(SquashFsReader fs, string path)
        {
            var b = fs.ReadFile(path, Small); if (b == null) return null;
            return Encoding.UTF8.GetString(b).TrimEnd('\n');   // as the shell's $(cat file): trailing newlines removed, nothing else
        }

        /// <summary>a recorded value shown as it is only when it is one short line of plain characters</summary>
        static string Plain(string s) => s != null && s.Length <= 60 && System.Text.RegularExpressions.Regex.IsMatch(s, "^[A-Za-z0-9._+-]+$") ? s : null;

        /// <summary>the chosen file read on the PC; Why is set (a phrase after "The chosen file ...") when it is not a system this console can start</summary>
        public static SystemImageInfo Identify(string file)
        {
            try { using (var f = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read)) return Identify(f); }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException) { return new SystemImageInfo { Why = "could not be read (" + ex.Message + ")" }; }
        }

        public static SystemImageInfo Identify(Stream f)
        {
            var r = new SystemImageInfo();
            var head = new byte[8]; f.Position = 0; int got = 0; while (got < 8) { int n = f.Read(head, got, 8 - got); if (n <= 0) break; got += n; }
            if (got == 8 && Encoding.ASCII.GetString(head) == "ANDROID!") { r.Why = "is a startup software (kernel) file, not a system image. CCS Nucleus starts another system from that system's own image (.hsqs: its decrypted system part, as hakchi's own dump writes it); a kernel file alone cannot start one"; return r; }
            SquashFsReader fs;
            try { fs = new SquashFsReader(f); }
            catch (InvalidDataException ex) { r.Why = "is " + ex.Message + ", so it is not a system image (.hsqs)"; return r; }
            r.Compression = fs.CompressionName;
            if (!fs.CanRead) { r.Why = "uses a compression (" + fs.CompressionName + ") this version cannot read, so which system it is cannot be checked"; return r; }
            try
            {
                var clover = fs.Find("/etc/clover");
                if (clover == null || clover.Type != 1)
                {
                    r.Why = fs.Find("/usr/game/version") != null ? "is a Sega Mega Drive / Genesis Mini system, made for another console's hardware: it cannot start on this console"
                        : "is not a Nintendo Classic console's system (it has no /etc/clover folder)";
                    return r;
                }
                if (fs.Find("/sbin/init") == null) { r.Why = "has no start program (/sbin/init), so it cannot start a console"; return r; }
                r.Board = Cat(fs, "/etc/clover/boardtype");
                r.Platform = r.Board == "dp-shvc" ? "snes" : "nes";
                var issue = fs.ReadFile("/etc/issue", Small);
                if (issue != null && Encoding.UTF8.GetString(issue).Contains("dp-hvcj")) r.Platform = "hvcj";
                var region = Cat(fs, "/etc/clover/REGION");
                r.Region = region == "JPN" ? "jpn" : region == "EUR" ? "eur" : "usa";   // as hakchi: anything else (EUR_USA on a North American NES Classic) is usa
                r.RegionFile = Plain(region); r.Version = Plain(Cat(fs, "/etc/clover/VERSION"));
            }
            catch (Exception ex) when (ex is InvalidDataException || ex is NotSupportedException) { r.Why = "could not be read as a system image (" + ex.Message + ")"; return r; }
            if (r.Platform == "snes")
            {
                r.Notes.Add("Its games use the X, Y, L and R buttons, which the NES Classic controller does not have: connect an SNES Classic controller to play them.");
                r.Notes.Add("hakchi keeps this system's HOME menu saves apart from the NES Classic system's: it swaps them at each start, so neither system's saves replace the other's.");
            }
            return r;
        }
    }
}
