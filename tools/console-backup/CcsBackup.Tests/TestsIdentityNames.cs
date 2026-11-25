// Classic Cart Studio console backup and restore engine tests: the console's identity sent to the window before anything is read, Back Up
// Console's backup name (asked in the same session, before anything is read), and the friendly console names kept in the settings.
// GPL-3.0-or-later. Simulated consoles only; nothing opens a USB device.
using CcsBackup.Archive;
using CcsBackup.Device;
using CcsBackup.Engine;
using CcsBackup.Library;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using static CcsBackup.Tests.Program;

namespace CcsBackup.Tests
{
    static partial class Tests
    {
        /// <summary>one worker request driven as the window drives it: answer(event) gives the window's next request (or null), stop(event)
        /// ends the reading. Waits and tries again while another program holds the console lock ("busy").</summary>
        static List<Dictionary<string, object>> DriveWorker(Settings s, JObj request, Func<Dictionary<string, object>, JObj> answer, Func<Dictionary<string, object>, bool> stop)
        {
            for (int attempt = 0; ; attempt++)
            {
                var input = new QueueStream(); var output = new QueueStream(); var enc = new UTF8Encoding(false);
                var th = new System.Threading.Thread(() => { try { WorkerHost.RunOn(input, output, "tests", "CcsBackup.Tests", "test", s.Path); } finally { output.End(); } }) { IsBackground = true }; th.Start();
                void Line(JObj j) { var b = enc.GetBytes(WorkerHost.RequestLine(j) + "\n"); input.Write(b, 0, b.Length); }
                Line(request);
                var events = new List<Dictionary<string, object>>(); string line; var rd = new StreamReader(output, enc);
                while ((line = rd.ReadLine()) != null)
                {
                    Dictionary<string, object> ev; try { ev = Json.Parse(line); } catch (FormatException) { continue; }
                    events.Add(ev);
                    var reply = answer?.Invoke(ev); if (reply != null) Line(reply);
                    if (stop(ev)) break;
                }
                input.End(); th.Join(180000);
                if (events.LastOrDefault()?.S("state") != "busy" || attempt >= 90) return events;
                System.Threading.Thread.Sleep(2000);
            }
        }

        static int IndexOf(List<Dictionary<string, object>> evs, Func<Dictionary<string, object>, bool> f) { for (int i = 0; i < evs.Count; i++) if (f(evs[i])) return i; return -1; }
        static bool ReadProgress(Dictionary<string, object> e) => e.S("event") == "progress" && (e.S("phase") == "read" || e.S("phase") == "check" || e.S("phase") == "verify");
        static string[] Files(string folder) => Directory.Exists(folder) ? Directory.GetFiles(folder).Select(Path.GetFileName).OrderBy(x => x, StringComparer.Ordinal).ToArray() : new string[0];
        static string[] BackupRecords(Settings s) => Directory.Exists(s.RecordsFolder) ? Directory.GetFiles(s.RecordsFolder, "backup-*.json") : new string[0];

        public static void IdentityNames()
        {
            NamesOffline();
            EarlyIdentity();
            BackupNameRequests();
            ConcurrentNames();
        }

        // ------------------------------------------------------------------ names and keys (no console)
        static void NamesOffline()
        {
            // the backup name as a file name
            var cases = new (string typed, string want)[]
            {
                ("Living Room", "Living Room"), ("  Living   Room  ", "Living Room"), ("a<b>c:d\"e/f\\g|h?i*j", "a-b-c-d-e-f-g-h-i-j"), ("tab\there\nline", "tab here line"),
                ("ends with dots...", "ends with dots"), ("...", ""), ("CON", "CON Backup"), ("com1.txt", "com1.txt Backup"), ("Lpt9", "Lpt9 Backup"), ("CONSOLE", "CONSOLE"),
                ("Cafe\u0301", "Caf\u00E9"), ("Spiel \uD83C\uDFAE Abend", "Spiel \uD83C\uDFAE Abend"), ("lone \uD83C half", "lone half"), ("\u0007bell", "-bell"),
                (new string('x', 300), new string('x', BackupNames.MaxLength)), ("", ""), (null, ""),
            };
            foreach (var (typed, want) in cases)
            {
                var got = BackupNames.Clean(typed);
                Check(got == want, "backup name: " + Show(typed) + " becomes " + Show(want) + " (got " + Show(got) + ")");
            }
            var cut = BackupNames.Clean(new string('a', BackupNames.MaxLength - 1) + "\uD83C\uDFAE");
            Check(cut.Length <= BackupNames.MaxLength && !char.IsHighSurrogate(cut[cut.Length - 1]), "backup name: shortening never cuts a character in two (" + cut.Length + " characters)");
            Check(BackupNames.Problem("   ") != null && BackupNames.Problem("<>") == null && BackupNames.Problem("Saturday") == null, "backup name: only a name with nothing usable in it is refused (the window then says so)");
            var deep = @"C:\" + string.Join(@"\", Enumerable.Repeat("folder-with-a-long-name", 8));
            var stem = BackupNames.Stem(new string('n', 100), false, deep);
            Check(stem != null && Path.Combine(deep, stem + " (999)" + BackupFormat.Partial).Length <= BackupNames.MaxPath, "backup name: shortened so the backup's full path stays within Windows' usual length (" + (deep.Length + stem.Length + 19) + " characters)");
            Check(BackupNames.Stem("My Backup", true, @"C:\b") == "SIMULATED My Backup" && BackupNames.Stem("SIMULATED test", true, @"C:\b") == "SIMULATED test" && BackupNames.Stem("My Backup", false, @"C:\b") == "My Backup",
                "backup name: a simulated console's backup is always marked SIMULATED in its name");

            // the console names: keys, numbers, names
            var book = new ConsoleNameBook();
            string ka = ConsoleNameBook.KeyOf(HwidA), kb = ConsoleNameBook.KeyOf(HwidB), kc = ConsoleNameBook.KeyOf("0461cccc 88180000 1e670000 6c118000");
            Check(ConsoleNameBook.IsKey(ka) && ka == ConsoleNameBook.KeyOf(HwidA.ToUpperInvariant().Replace(" ", "")) && ka.StartsWith(ConsoleIdentity.HwidTag(HwidA), StringComparison.Ordinal) && ka != kb,
                "console key: the full SHA-256 of the normalized hardware ID (the same for any spacing or letter case; the short tag is only its first 8 characters)");
            var a = book.Note(ka, ConsoleModels.Nes.Id, out bool addedA); var s1 = book.Note(kc, ConsoleModels.Snes.Id, out _); var b = book.Note(kb, ConsoleModels.Nes.Id, out _);
            var again = book.Note(ka, ConsoleModels.Nes.Id, out bool addedAgain);
            Check(addedA && !addedAgain && ReferenceEquals(a, again) && a.Display == "NES Classic (1)" && b.Display == "NES Classic (2)" && s1.Display == "SNES Classic (1)",
                "console names: each model numbered from (1) as its consoles are first identified; a console seen again keeps its number");
            Check(book.Rename(kb, "  Bedroom \t NES ") && b.Display == "Bedroom NES" && b.ModelShort == "NES Classic" && b.Number == 2, "console names: a rename keeps the console's number and model (its kind stays shown beside the name)");
            Check(!book.Rename(kb, new string('z', ConsoleNameBook.MaxName + 1)) && b.Display == "Bedroom NES", "console names: a name longer than " + ConsoleNameBook.MaxName + " characters is not kept, and the earlier name stays");
            Check(book.Rename(kb, "") && b.Display == "NES Classic (2)" && !book.Rename(ConsoleNameBook.KeyOf("0461dddd 88180000 1e670000 6c118000"), "x"), "console names: an empty name goes back to the model's name and number; an unknown console cannot be renamed");
            book.Rename(ka, "Living Room");

            // the settings file: a round trip, one console at a time (the single cable), and a file another version may read
            var dir = NewDir("console-names"); var path = Path.Combine(dir, "settings.json");
            var st = new Settings { Path = path, PrimaryFolder = Path.Combine(dir, "b"), ArchiveFolder = null, ConsoleNames = book.Copy() }; st.Save();
            var back = Settings.Load(path);
            Check(back.ConsoleNames.Signature() == book.Signature() && back.ConsoleNames.Find(ka).Display == "Living Room" && back.ConsoleNames.Find(kb).Number == 2,
                "console names: saved in the settings file and read back unchanged (numbers, models and names)");
            var text = File.ReadAllText(path);
            Check(!text.Contains(ConsoleIdentity.NormalizeHwid(HwidA)) && !text.Contains(HwidA) && text.Contains(ka), "console names: the settings file holds the key, never the console's hardware ID itself");
            var one = new ConsoleNameBook(); one.Note(ka, ConsoleModels.Nes.Id, out _);
            var s2 = new Settings { Path = path, PrimaryFolder = Path.Combine(dir, "b"), ConsoleNames = one }; s2.Save();
            var later = Settings.Load(path); later.ConsoleNames.Note(kb, ConsoleModels.Nes.Id, out _); later.Save();
            var latest = Settings.Load(path); latest.ConsoleNames.Note(ka, ConsoleModels.Nes.Id, out _);
            Check(latest.ConsoleNames.Find(ka).Number == 1 && latest.ConsoleNames.Find(kb).Number == 2 && latest.ConsoleNames.Entries.Count == 2,
                "console names: consoles identified one at a time, with the settings saved and opened again in between, keep (1) and (2); nothing is renumbered");
            File.WriteAllText(path, "{\"schema\":\"ccs-console-backup-settings/1\",\"primary_folder\":\"C:\\\\b\",\"console_names\":[{\"key\":\"" + ka + "\",\"model\":\"nes-classic-edition\",\"number\":3},{\"key\":\"short\",\"model\":\"x\",\"number\":1},{\"key\":\"" + ka + "\",\"model\":\"nes-classic-edition\",\"number\":9},{\"key\":\"" + kb + "\",\"model\":\"nes-classic-edition\",\"number\":0}],\"newer_field\":7}");
            var odd = Settings.Load(path); odd.Save(); var oddText = File.ReadAllText(path);
            Check(odd.ConsoleNames.Entries.Count == 1 && odd.ConsoleNames.Find(ka).Number == 3 && oddText.Contains("newer_field"),
                "console names: entries that are not well formed are left out, never guessed; a newer version's fields are kept");
            var copy = later.Copy(); copy.ConsoleNames.Rename(ka, "Changed");
            Check(later.ConsoleNames.Find(ka).Name == null, "console names: a settings copy (what the window compares to find unsaved changes) has its own names");

            // two consoles whose short tags are the same: their names never mix
            var (h1, h2) = SameShortTag();
            var k1 = ConsoleNameBook.KeyOf(h1); var k2 = ConsoleNameBook.KeyOf(h2);
            var tb = new ConsoleNameBook(); var e1 = tb.Note(k1, ConsoleModels.Nes.Id, out _); var e2 = tb.Note(k2, ConsoleModels.Nes.Id, out _); tb.Rename(k1, "First");
            Check(ConsoleIdentity.HwidTag(h1) == ConsoleIdentity.HwidTag(h2) && k1 != k2 && e1 != e2 && tb.Find(k1).Display == "First" && tb.Find(k2).Display == "NES Classic (2)",
                "console names: two consoles sharing the short tag " + ConsoleIdentity.HwidTag(h1) + " are kept apart by their full keys");
        }

        static string Show(string s) => s == null ? "(none)" : "\"" + string.Concat(s.Select(c => c < 32 ? "\\x" + ((int)c).ToString("x2") : c.ToString())) + "\"";

        /// <summary>two well-formed hardware IDs whose 8-character tags are the same (found by trying IDs in a fixed order)</summary>
        static (string, string) SameShortTag()
        {
            var seen = new Dictionary<string, string>(); var rnd = new Random(20261124);
            for (int i = 0; i < 2000000; i++)
            {
                var h = string.Join(" ", Enumerable.Range(0, 4).Select(_ => rnd.Next().ToString("x8")));
                var t = ConsoleIdentity.HwidTag(h);
                if (seen.TryGetValue(t, out var other) && other != h) return (other, h);
                seen[t] = h;
            }
            throw new InvalidOperationException("no two IDs with the same tag were found");
        }

        // ------------------------------------------------------------------ the identity, before anything is read
        static void EarlyIdentity()
        {
            foreach (var (family, console, hwid, model) in new[] { ("NES Classic", Console_("modified-noorig"), HwidA, ConsoleModels.Nes), ("SNES Classic", SnesConsole("modified-noorig"), HwidA, ConsoleModels.Snes) })
            {
                var s = SettingsIn(NewDir("early-" + model.Id), false); string[] atIdentity = null;
                var evs = DriveWorker(s, new JObj { { "cmd", "backup" }, { "console", "simulated" }, { "folder", console } },
                    e => { if (e.S("event") == "console" && atIdentity == null) atIdentity = Files(s.PrimaryFolder).Concat(BackupRecords(s)).ToArray(); return e.S("event") == "result" ? new JObj { { "cmd", "quit" } } : null; },
                    e => e.S("event") == "result");
                int ci = IndexOf(evs, e => e.S("event") == "console"), ri = IndexOf(evs, ReadProgress), res = IndexOf(evs, e => e.S("event") == "result");
                var ce = ci >= 0 ? evs[ci] : null; var result = res >= 0 ? evs[res] : null;
                Check(ci >= 0 && ri > ci && res > ri && atIdentity != null && atIdentity.Length == 0,
                    family + ": the console's identity reaches the window before anything is read (no backup file or record yet at that moment; event " + ci + ", first read " + ri + ")");
                Check(ce != null && ce.S("console_tag") == ConsoleIdentity.HwidTag(hwid) && ce.S("console_key") == ConsoleNameBook.KeyOf(hwid) && ce.S("physical_model") == model.Id && ce.S("model") == model.ShortName && !ce.ContainsKey("hwid"),
                    family + ": the identity event names the short tag, the full key and the model, and never the hardware ID itself");
                Check(result?.S("state") == "complete-one-copy" && result.S("console_tag") == ce?.S("console_tag") && result.S("console_key") == ce?.S("console_key") && evs.Count(e => e.S("event") == "console") == 1 && !evs.Any(e => e.S("event") == "backup-name"),
                    family + ": the backup itself goes on as before (one identity event; no name asked unless the window asks) (" + result?.S("state") + " " + result?.S("problem") + ")");
            }

            // an identity that is refused is never sent as known
            var zc = Console_("modified-noorig", "00000000 00000000 00000000 00000000");
            var japan = SnesConsole("modified-noorig", "JPN");
            foreach (var (what, c) in new[] { ("a console whose identity cannot be read", zc), ("a Super Famicom Mini (not supported)", japan) })
            {
                var s = SettingsIn(NewDir("refused-id"), false);
                var evs = DriveWorker(s, new JObj { { "cmd", "backup" }, { "console", "simulated" }, { "folder", c }, { "name", "ask" } }, e => e.S("event") == "result" ? new JObj { { "cmd", "quit" } } : null, e => e.S("event") == "result");
                var r = evs.LastOrDefault();
                Check(!evs.Any(e => e.S("event") == "console" || e.S("event") == "backup-name") && r?.S("event") == "result" && (r.S("state") == "refused" || r.S("state") == "unavailable") && Files(s.PrimaryFolder).Length == 0,
                    "refused identity: " + what + " is never sent as identified, and no name is asked (" + r?.S("state") + ")");
            }

            // the check-up: the identity first, then what was read
            {
                var s = SettingsIn(NewDir("early-checkup"), false); var c = Console_("modified-noorig");
                var evs = DriveWorker(s, new JObj { { "cmd", "browse" }, { "console", "simulated" }, { "folder", c } }, e => e.S("event") == "checkup" ? new JObj { { "cmd", "quit" } } : null, e => e.S("event") == "result");
                int ci = IndexOf(evs, e => e.S("event") == "console"), ki = IndexOf(evs, e => e.S("event") == "checkup");
                Check(ci >= 0 && ki > ci && evs[ci].S("console_key") == ConsoleNameBook.KeyOf(HwidA), "check-up: the console's identity reaches the window before the check-up reads its storage");
            }
        }

        // ------------------------------------------------------------------ Back Up Console's backup name
        static void BackupNameRequests()
        {
            // the name asked after the identity and before anything is read; a typed name used for both copies, never over an existing file
            {
                var dir = NewDir("named"); var s = SettingsIn(dir, true); var c = Console_("modified-noorig");
                const string typed = "Living Room: NES / Saturday";
                var clean = BackupNames.Clean(typed);
                Directory.CreateDirectory(s.PrimaryFolder); Directory.CreateDirectory(s.ArchiveFolder);
                string[] atName = null; string usual = null;
                var evs = DriveWorker(s, new JObj { { "cmd", "backup" }, { "console", "simulated" }, { "folder", c }, { "name", "ask" } }, e =>
                {
                    if (e.S("event") == "backup-name")
                    {
                        atName = Files(s.PrimaryFolder).Concat(BackupRecords(s)).ToArray(); usual = e.S("usual");
                        // a file of that name appears in both folders before the answer: neither is written over
                        File.WriteAllText(Path.Combine(s.PrimaryFolder, "SIMULATED " + clean + BackupFormat.Extension), "not a backup");
                        File.WriteAllText(Path.Combine(s.ArchiveFolder, "SIMULATED " + clean + " (2)" + BackupFormat.Extension), "not a backup either");
                        return new JObj { { "cmd", "backup-name" }, { "name", typed } };
                    }
                    return e.S("event") == "result" ? new JObj { { "cmd", "quit" } } : null;
                }, e => e.S("event") == "result");
                int ci = IndexOf(evs, e => e.S("event") == "console"), ni = IndexOf(evs, e => e.S("event") == "backup-name"), ri = IndexOf(evs, ReadProgress);
                var r = evs.LastOrDefault();
                Check(ci >= 0 && ni > ci && ri > ni && atName != null && atName.Length == 0 && usual != null && usual.Contains("(console " + ConsoleIdentity.HwidTag(HwidA) + ")"),
                    "backup name: asked after the identity and before anything is read, in the same session (no file or record yet; the usual name offered too)");
                var prim = r?.S("primary"); var arch = r?.S("archive");
                Check(r?.S("state") == "complete" && Path.GetFileName(prim) == "SIMULATED " + clean + " (2)" + BackupFormat.Extension && File.ReadAllText(Path.Combine(s.PrimaryFolder, "SIMULATED " + clean + BackupFormat.Extension)) == "not a backup",
                    "backup name: the typed name, made a valid file name (" + clean + "), with the next free number when the name is taken; nothing written over (" + Path.GetFileName(prim) + ", " + Path.GetFileName(arch) + ")");
                Check(Path.GetFileName(arch) == "SIMULATED " + clean + " (2) (2)" + BackupFormat.Extension && File.ReadAllText(Path.Combine(s.ArchiveFolder, "SIMULATED " + clean + " (2)" + BackupFormat.Extension)) == "not a backup either" && Sha(arch) == Sha(prim),
                    "backup name: the additional copy has the accepted name too (the next free one in its folder) and the same bytes");
                var m = History.Peek(prim);
                Check(m != null && m.O("console")?.S("tag") == ConsoleIdentity.HwidTag(HwidA) && m.O("console")?.S("hwid") == HwidA && !Json.Write(m).Contains(typed), "backup name: the backup's own record of the console is unchanged; the name is only the file's name");
            }
            // no typing: the usual name
            {
                var s = SettingsIn(NewDir("named-usual"), false); var c = Console_("modified-noorig"); string usual = null;
                var evs = DriveWorker(s, new JObj { { "cmd", "backup" }, { "console", "simulated" }, { "folder", c }, { "name", "ask" } },
                    e => e.S("event") == "backup-name" ? (usual = e.S("usual")) != null ? new JObj { { "cmd", "backup-name" } } : null : e.S("event") == "result" ? new JObj { { "cmd", "quit" } } : null, e => e.S("event") == "result");
                var r = evs.LastOrDefault();
                Check(r?.S("state") == "complete-one-copy" && Path.GetFileNameWithoutExtension(r.S("primary")).StartsWith("SIMULATED NES Classic Backup ", StringComparison.Ordinal), "backup name: an answer without a name keeps the usual name (" + Path.GetFileName(r?.S("primary")) + ")");
            }
            // a name the engine cannot use is never trusted from the window: only what Clean makes of it
            {
                var s = SettingsIn(NewDir("named-odd"), false); var c = Console_("modified-noorig");
                var evs = DriveWorker(s, new JObj { { "cmd", "backup" }, { "console", "simulated" }, { "folder", c }, { "name", "ask" } },
                    e => e.S("event") == "backup-name" ? new JObj { { "cmd", "backup-name" }, { "name", "..\\..\\NUL" } } : e.S("event") == "result" ? new JObj { { "cmd", "quit" } } : null, e => e.S("event") == "result");
                var r = evs.LastOrDefault();
                Check(r?.S("state") == "complete-one-copy" && Path.GetDirectoryName(r.S("primary")) == s.PrimaryFolder && Path.GetFileName(r.S("primary")) == "SIMULATED " + BackupNames.Clean("..\\..\\NUL") + BackupFormat.Extension,
                    "backup name: a name with folder parts or a device name stays a plain file name in the backup folder (" + Path.GetFileName(r?.S("primary")) + ")");
            }
            // Cancel, or the window leaving, at the name: nothing read, no file, no record
            foreach (var how in new[] { "cancel", "quit" })
            {
                var s = SettingsIn(NewDir("named-" + how), false); var c = Console_("modified-noorig");
                var evs = DriveWorker(s, new JObj { { "cmd", "backup" }, { "console", "simulated" }, { "folder", c }, { "name", "ask" } },
                    e => e.S("event") == "backup-name" ? new JObj { { "cmd", how } } : e.S("event") == "result" ? new JObj { { "cmd", "quit" } } : null, e => e.S("event") == "result");
                var r = evs.LastOrDefault();
                Check(r?.S("state") == "canceled" && r.S("partial") == null && Files(s.PrimaryFolder).Length == 0 && BackupRecords(s).Length == 0 && !evs.Any(ReadProgress) && File.Exists(Path.Combine(c, "writes.log")) == false,
                    "backup name: " + how + " at the name stops cleanly before anything is read: no file, no record, nothing written to the console (" + r?.S("problem") + ")");
            }
        }

        // two backups given the same name at the same moment (two programs, two consoles, one folder): two files, neither written over
        static void ConcurrentNames()
        {
            var dir = NewDir("named-together"); var s = SettingsIn(dir, false);
            var c1 = Console_("modified-noorig", HwidA); var c2 = Console_("modified-noorig", HwidB);
            var gate = new System.Threading.Barrier(2); BackupOutcome o1 = null, o2 = null;
            BackupOutcome Run(string c)
            {
                var con = RecoverySessions.OpenSimulated(c, ImageKind.Backup); var ctx = Ctx(s);
                ctx.NameBackup = (id, usual) => { gate.SignalAndWait(60000); return new BackupNameAnswer { Name = "Same Name" }; };
                return CaptureEngine.Run(con, ctx);
            }
            var t1 = new System.Threading.Thread(() => o1 = Run(c1)); var t2 = new System.Threading.Thread(() => o2 = Run(c2));
            t1.Start(); t2.Start(); t1.Join(600000); t2.Join(600000);
            var names = new[] { o1?.PrimaryPath, o2?.PrimaryPath }.Select(Path.GetFileName).OrderBy(x => x, StringComparer.Ordinal).ToArray();
            Check(o1?.Ok == true && o2?.Ok == true && names.SequenceEqual(new[] { "SIMULATED Same Name (2).zip", "SIMULATED Same Name.zip" }) && Verifier.VerifyFile(o1.PrimaryPath, null).Ok && Verifier.VerifyFile(o2.PrimaryPath, null).Ok,
                "backup name: two backups named the same at the same moment get two files (" + string.Join(", ", names) + "), both complete and checked; neither is written over");
        }
    }
}
