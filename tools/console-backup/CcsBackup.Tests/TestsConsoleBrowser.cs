// Classic Cart Studio console backup and restore engine tests: Console Browser's check-up, read live through checked range reads.
// GPL-3.0-or-later. Simulated consoles only; nothing is written.
using CcsBackup.Browse;
using CcsBackup.Device;
using CcsBackup.Engine;
using CcsBackup;
using CcsBackup.Ext4;
using System;
using System.IO;
using System.Linq;
using static CcsBackup.Tests.Program;

namespace CcsBackup.Tests
{
    static partial class Tests
    {
        public static void ConsoleBrowserCases()
        {
            var s = SettingsIn(NewDir("console-browser"), false);
            foreach (var kind in new[] { "stock", "modified" })
            {
                var c = Console_(kind); var before = Directory.GetFiles(Path.Combine(c, "dev")).ToDictionary(f => f, f => Sha(f));
                var con = RecoverySessions.OpenSimulated(c, ImageKind.Backup); var ctx = Ctx(s, ImageKind.Backup);
                var k = ConsoleBrowser.Checkup(con, ctx.RecoveryImageId, null);
                Ext4Reader direct; using (var f = File.OpenRead(SimulatedConsole.Dev(c, "nandc"))) direct = new Ext4Reader(f);
                long bs = direct.BlockSize;
                Check(k.Problem == null && k.TotalBytes == direct.BlocksCount * bs && k.FreeBytes == direct.FreeBlocks * bs && k.ReservedBytes == direct.ReservedBlocks * bs && k.UsedBytes + k.FreeBytes == k.TotalBytes && k.AvailableBytes == Math.Max(0, k.FreeBytes.Value - k.ReservedBytes.Value),
                    kind + ": the storage figures are the file system's own (total, used, free, reserved, available), read through checked range reads (" + k.Problem + ")");
                Check(k.PartBytes > k.TotalBytes || k.PartBytes == k.TotalBytes, kind + ": the part's size and the file system's size are reported separately");
                Check(kind == "stock" ? k.KernelRecognized && k.KernelText.Contains("original") : !k.KernelRecognized && k.KernelText.Contains("not on this version's list") && !k.KernelText.ToLowerInvariant().Contains("fake"),
                    kind + ": startup software " + (kind == "stock" ? "recognized as Nintendo's original" : "reported as not recognized, never as fake") + " (" + k.KernelText + ")");
                var by = k.Installed == null ? null : Json.Parse(Json.Write(k.Installed)).O("files_by_owner");
                Check(by != null && (kind == "stock" ? k.HakchiPresent == false : k.HakchiPresent && k.CcsPresent), kind + ": what is installed is listed (" + (by == null ? "none" : by.L("nintendo") + " Nintendo's, " + by.L("hakchi") + " hakchi's, " + by.L("classic_cart_studio") + " CCS") + ")");
                Check(before.All(kv => Sha(kv.Key) == kv.Value) && !File.Exists(Path.Combine(c, "writes.log")), kind + ": the check-up wrote nothing to the console");
            }
            var c2 = Console_("stock"); var con2 = RecoverySessions.OpenSimulated(c2, ImageKind.Backup);
            bool refused = false; try { con2.ReadRange("nandc", 100, 4096, new MemoryStream(), null); } catch (ArgumentException) { refused = true; }
            bool refused2 = false; try { con2.ReadRange("boot0", 0, 4096, new MemoryStream(), null); } catch (ArgumentException) { refused2 = true; }
            Check(refused && refused2, "range reads: only whole 4096-byte pieces of a logical storage part, never a boot stage or the physical image");
            var r = con2.ReadRange("nandc", 4096, 8192, new MemoryStream(), null);
            Check(r.Ok && r.Bytes == 8192 && r.ConsoleMd5 == r.Md5, "a range read is checked end to end like a whole read (the console's count and MD5 of the same pass)");

            // the live views: the same model as Backup Browser over checked range reads; previews and exports are checked; nothing written
            var c3 = Console_("modified"); var before3 = Directory.GetFiles(Path.Combine(c3, "dev")).ToDictionary(f => f, f => Sha(f));
            var con3 = RecoverySessions.OpenSimulated(c3, ImageKind.Backup); var k3 = ConsoleBrowser.Checkup(con3, Ctx(s, ImageKind.Backup).RecoveryImageId, null);
            var live = BackupContents.OpenLive(new RemotePartStream(con3, k3.DataDevice, k3.PartBytes.Value), k3.ConsoleTag, k3.KernelText);
            var dataFile = SimulatedConsole.Dev(c3, k3.DataDevice);
            System.Collections.Generic.List<string> want; using (var f = File.OpenRead(dataFile)) want = new Ext4Reader(f).List().Select(x => x.Path).OrderBy(x => x, StringComparer.Ordinal).ToList();
            var got = live.Ok ? live.Contents.Entries.Where(e => e.Source == EntrySource.DataFile).Select(e => e.ImagePath).OrderBy(x => x, StringComparer.Ordinal).ToList() : null;
            Check(live.Ok && got.SequenceEqual(want) && live.Contents.Live && live.Contents.Games.Count > 0,
                "live: every file of the console's data storage is listed (" + (got?.Count ?? 0) + " of " + want.Count + "), with the By Game grouping (" + (live.Contents?.Games.Count ?? 0) + " games) (" + live.Problem + ")");
            if (live.Ok)
            {
                var lc = live.Contents; var file = lc.Entries.First(e => e.Source == EntrySource.DataFile && e.Kind == EntryKind.File && e.Size > 0);
                byte[] direct; using (var f = File.OpenRead(dataFile)) { var fs = new Ext4Reader(f); direct = fs.ReadAll(file.Inode, file.Size + 1); }
                var b = lc.ReadVerified(file, 20_000_000, out var why);
                Check(b != null && b.SequenceEqual(direct) && file.HashOrigin == HashOrigin.Derived, "live: a preview reads the file's exact bytes from the console and checks them (" + file.ImagePath + ", " + why + ")");
                var dest = NewDir("live-export"); var ex = Exporter.Export(lc, ExportRequest.WholeCategory(lc, Category.Saves, dest));
                Check(ex.Ok && ex.Files.Count == lc.ByType(Category.Saves).Count(e => e.Kind == EntryKind.File), "live: Export All of This Type copies every save to the PC and reads each back (" + ex.Summary + ")");
                lc.Dispose();
            }
            Check(before3.All(kv => Sha(kv.Key) == kv.Value) && !File.Exists(Path.Combine(c3, "writes.log")), "live: browsing and exporting wrote nothing to the console");

            // the check-up page's Restart the Console, through the worker as both hosts drive it: the live session ends (its result), then the
            // console restarts normally; no error, nothing written
            var c4 = Console_("stock"); var before4 = Directory.GetFiles(Path.Combine(c4, "dev")).ToDictionary(f => f, f => Sha(f));
            var events = new System.Collections.Generic.List<System.Collections.Generic.Dictionary<string, object>>();
            for (int attempt = 0; attempt < 90; attempt++)
            {
                var input = new QueueStream(); var output = new QueueStream(); var enc = new System.Text.UTF8Encoding(false);
                var th = new System.Threading.Thread(() => { try { WorkerHost.RunOn(input, output, "tests", "CcsBackup.Tests", "test", s.Path); } finally { output.End(); } }) { IsBackground = true }; th.Start();
                void Line(JObj j) { var bl = enc.GetBytes(WorkerHost.RequestLine(j) + "\n"); input.Write(bl, 0, bl.Length); }
                Line(new JObj { { "cmd", "browse" }, { "console", "simulated" }, { "folder", c4 } });
                events.Clear(); string line; var rd = new StreamReader(output, enc);
                while ((line = rd.ReadLine()) != null)
                {
                    System.Collections.Generic.Dictionary<string, object> ev; try { ev = Json.Parse(line); } catch (FormatException) { continue; }
                    events.Add(ev);
                    if (ev.S("event") == "checkup") Line(new JObj { { "cmd", "finish" } });
                    if (ev.S("event") == "finished" || ev.S("event") == "error" || ev.S("state") == "busy") break;
                }
                input.End(); th.Join(60000);
                if (events.LastOrDefault()?.S("state") != "busy") break;
                System.Threading.Thread.Sleep(2000);
            }
            var fin = events.LastOrDefault(); var log4 = Path.Combine(c4, "writes.log");   // the simulated console notes the restart there, and nothing else
            var logLines = File.Exists(log4) ? File.ReadAllLines(log4).Where(l => l.Trim().Length > 0).ToList() : new System.Collections.Generic.List<string>();
            Check(events.Any(e => e.S("event") == "result" && e.S("operation") == "browse") && fin?.S("event") == "finished" && fin.B("restarting") && !events.Any(e => e.S("event") == "error")
                  && before4.All(kv => Sha(kv.Key) == kv.Value) && logLines.Count == 1 && logLines[0].EndsWith(" finish (restart)", StringComparison.Ordinal),
                  "check-up: Restart the Console ends the live session and restarts the console normally; no error, nothing written (" + string.Join(", ", events.Select(e => e.S("event"))) + ")");

            // the check-up page's Done (quit), as both hosts send it: the live session ends at once, without a second request (the reading loop
            // takes the quit itself; the worker must not then wait for another one with the console in its memory program)
            var c5 = Console_("stock"); var before5 = Directory.GetFiles(Path.Combine(c5, "dev")).ToDictionary(f => f, f => Sha(f));
            var ev5 = new System.Collections.Generic.List<System.Collections.Generic.Dictionary<string, object>>(); bool ended5 = false; double secs5 = -1;
            for (int attempt = 0; attempt < 90; attempt++)
            {
                var input = new QueueStream(); var output = new QueueStream(); var enc = new System.Text.UTF8Encoding(false);
                var th = new System.Threading.Thread(() => { try { WorkerHost.RunOn(input, output, "tests", "CcsBackup.Tests", "test", s.Path); } finally { output.End(); } }) { IsBackground = true }; th.Start();
                void Line5(JObj j) { var bl = enc.GetBytes(WorkerHost.RequestLine(j) + "\n"); input.Write(bl, 0, bl.Length); }
                Line5(new JObj { { "cmd", "browse" }, { "console", "simulated" }, { "folder", c5 } });
                ev5.Clear(); string line; var rd = new StreamReader(output, enc); var quitAt = DateTime.MinValue;
                while ((line = rd.ReadLine()) != null)
                {
                    System.Collections.Generic.Dictionary<string, object> ev; try { ev = Json.Parse(line); } catch (FormatException) { continue; }
                    ev5.Add(ev);
                    if (ev.S("event") == "checkup") { quitAt = DateTime.UtcNow; Line5(new JObj { { "cmd", "quit" } }); }
                    if ((ev.S("event") == "result" && ev.S("operation") == "browse") || ev.S("event") == "error" || ev.S("state") == "busy") break;
                }
                ended5 = th.Join(20000); secs5 = quitAt == DateTime.MinValue ? -1 : (DateTime.UtcNow - quitAt).TotalSeconds;   // before the window goes: only the quit can end it
                input.End(); th.Join(60000);
                if (ev5.LastOrDefault()?.S("state") != "busy") break;
                System.Threading.Thread.Sleep(2000);
            }
            Check(ended5 && ev5.Any(e => e.S("event") == "result" && e.S("operation") == "browse") && !ev5.Any(e => e.S("event") == "error" || e.S("event") == "finished")
                  && before5.All(kv => Sha(kv.Key) == kv.Value) && !File.Exists(Path.Combine(c5, "writes.log")),
                  "check-up: Done ends the live session at once (the worker ended " + secs5.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " s after the request, with the window still there); no restart, no error, nothing written (" + string.Join(", ", ev5.Select(e => e.S("event"))) + ")");
        }
    }
}
