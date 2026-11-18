// Classic Cart Studio console backup and restore engine tests: the process lifecycle of the built Utility executable. GPL-3.0-or-later.
// 1. its worker ended at each durable boundary of a backup (Environment.FailFast: no finally block, no close handler, no cleanup) and of a
//    restore, then the history, the admission and the files checked from scratch: nothing incomplete is ever accepted, nothing earlier
//    is touched;
// 2. its window closed (and answered) during a backup; its window process ended without warning; its worker ended without warning: the
//    console is only read, the unfinished file is kept and listed Incomplete, every process exits. A simulated console only.
using CcsBackup.Archive;
using CcsBackup.Device;
using CcsBackup.Engine;
using CcsBackup.Library;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Windows.Automation;
using static CcsBackup.Tests.Program;

namespace CcsBackup.Tests
{
    static class LifecycleTests
    {
        static Process Worker(Settings s, string request, out StreamWriter stdin, bool readAsync = true)
        {
            var p = Process.Start(new ProcessStartInfo(Utility, "--backup-worker --settings \"" + s.Path + "\"") { UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true });
            if (readAsync) { p.OutputDataReceived += (o, e) => { }; p.BeginOutputReadLine(); }
            p.ErrorDataReceived += (o, e) => { }; p.BeginErrorReadLine();
            stdin = WorkerHost.RequestWriter(p.StandardInput.BaseStream); stdin.WriteLine(request);   // as the window writes; stdin stays open: an ended input means "the window has gone"
            return p;
        }

        /// <summary>the worker's "result" event (its other lines skipped); null when its output ended first</summary>
        static Dictionary<string, object> ResultOf(Process p)
        {
            string line;
            while ((line = p.StandardOutput.ReadLine()) != null)
                if (line.Contains("\"event\": \"result\"")) { try { return Json.Parse(line); } catch { return null; } }
            return null;
        }

        /// <summary>any folder name reaches the worker intact: the window writes its requests in UTF-8 (WorkerHost.RequestWriter); the
        /// console's or the ANSI code page (what Process.StandardInput uses by default) cannot carry every name</summary>
        static void FolderNames()
        {
            const string consoleName = "Konsole é ü コンソール", backupName = "Sauvegardes é 日本のバックアップ";
            var d = NewDir("lc-names"); var c = Path.Combine(d, consoleName); Directory.Move(Program.Console_("modified"), c);
            var s = new Settings { Path = Path.Combine(d, "settings.json"), PrimaryFolder = Path.Combine(d, backupName), ArchiveFolder = null }; s.Save();
            var before = Directory.GetFiles(Path.Combine(c, "dev")).ToDictionary(f => f, f => Sha(f));
            var p = Worker(s, Req("backup", c), out var stdin, false);
            var r = ResultOf(p); stdin.Close(); bool exited = p.WaitForExit(120000);
            var zips = Directory.Exists(s.PrimaryFolder) ? Directory.GetFiles(s.PrimaryFolder, "*.zip") : new string[0];
            Check(r != null && r.S("state") == "complete-one-copy" && exited && zips.Length == 1 && Verifier.VerifyFile(zips[0]).Ok && before.All(kv => Sha(kv.Key) == kv.Value),
                  "a console folder and a backup folder with accented and Japanese names: the worker reads the request intact and the backup is made there and verifies (" + (r?.S("state") ?? "no result") + ")");
            // the control: the same request in this PC's ANSI code page (the old window's way) reaches the worker damaged
            var ansi = Encoding.Default;
            if (ansi.GetString(ansi.GetBytes(c)) == c) { Console.WriteLine("   (control not applicable: this PC's ANSI code page " + ansi.CodePage + " can carry the names)"); return; }
            var d2 = NewDir("lc-names-ansi"); var s2 = new Settings { Path = Path.Combine(d2, "settings.json"), PrimaryFolder = Path.Combine(d2, "backups"), ArchiveFolder = null }; s2.Save();
            var q = Process.Start(new ProcessStartInfo(Utility, "--backup-worker --settings \"" + s2.Path + "\"") { UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true });
            q.ErrorDataReceived += (o, e) => { }; q.BeginErrorReadLine();
            var w = new StreamWriter(q.StandardInput.BaseStream, ansi) { AutoFlush = true }; w.WriteLine(Req("backup", c));
            var r2 = ResultOf(q); w.Close(); q.WaitForExit(120000);
            Check(r2 == null || r2.S("state") != "complete-one-copy",
                  "control: written in ANSI code page " + ansi.CodePage + " (the old way) the same request names a folder that does not exist: no backup (" + (r2?.S("state") ?? "no result") + ")");
        }

        static string Req(string cmd, string folder, string backup = null) => Json.Write(new JObj { { "cmd", cmd }, { "console", "simulated" }, { "folder", folder }, { "backup", backup } }).Replace("\n", " ");

        public static void Run()
        {
            // an earlier good backup (no recovery baseline: it can never admit anything) that no interruption may touch
            var good = Backup(Program.Console_("modified-noorig"), SettingsIn(NewDir("lc-good"), false));
            string[] boundaries = { "record-started", "member-boot2", "member-nandc", "member-phy", "captured", "checked", "published", "copy-written", "copy-checked", "complete" };
            foreach (var b in boundaries)
            {
                var d = NewDir("lc-" + b); var s = SettingsIn(d, true); Directory.CreateDirectory(s.PrimaryFolder);
                var goodCopy = Path.Combine(s.PrimaryFolder, Path.GetFileName(good.PrimaryPath)); File.Copy(good.PrimaryPath, goodCopy); var goodSha = Sha(goodCopy);
                var c = Program.Console_("modified"); File.WriteAllText(Path.Combine(c, "faults.json"), "{\"crash_at\":\"" + b + "\"}");
                var before = Directory.GetFiles(Path.Combine(c, "dev")).ToDictionary(f => f, f => Sha(f));
                var p = Worker(s, Req("backup", c), out var stdin);
                bool exited = p.WaitForExit(600000);
                var latest = History.List(s).Where(e => !string.Equals(e.PrimaryPath, goodCopy, StringComparison.OrdinalIgnoreCase)).OrderByDescending(e => e.CreatedUtc).FirstOrDefault();
                var finalZips = Directory.GetFiles(s.PrimaryFolder, "*.zip").Where(f => !string.Equals(f, goodCopy, StringComparison.OrdinalIgnoreCase)).ToList();
                var partials = Directory.GetFiles(s.PrimaryFolder, "*.partial").ToList();
                bool consoleSame = before.All(kv => Sha(kv.Key) == kv.Value);
                bool expectVerified = b == "complete", expectFinal = Array.IndexOf(boundaries, b) >= Array.IndexOf(boundaries, "published");
                var st = latest?.Status;
                bool statusOk = expectVerified ? st == BackupStatus.Verified : expectFinal ? st == BackupStatus.NeedsVerification : st == BackupStatus.Incomplete;
                Check(exited && p.ExitCode != 0 && consoleSame && statusOk && (expectFinal ? finalZips.Count == 1 : finalZips.Count == 0 && (b == "record-started" || partials.Count >= 1)),
                      "worker ended at \"" + b + "\" (exit " + (exited ? p.ExitCode.ToString() : "none") + "): the console unchanged; the history says " + st + "; " + finalZips.Count + " final file(s), " + partials.Count + " unfinished");
                var adm = Admission.Admit(s, HwidA, "install", "x", null, true);
                Check(adm.Ok == expectFinal && (!adm.Ok || finalZips.Contains(adm.BaselinePath)),
                      "after \"" + b + "\": " + (expectFinal ? "the published file (complete and checked before its name was given) admits only after it verifies again from scratch" : "nothing unfinished admits a change (" + adm.Code + ")"));
                if (finalZips.Count == 1) Check(Verifier.VerifyFile(finalZips[0]).Ok, "after \"" + b + "\": the published file verifies from scratch");
                Check(Sha(goodCopy) == goodSha, "after \"" + b + "\": the earlier backup in the same folder is untouched");
            }
            FolderNames();
            // restore: the worker ended while writing
            {
                var d = NewDir("lc-restore"); var s = SettingsIn(d, false); var c = Program.Console_("modified");
                var snap = Backup(c, s); File.Copy(Path.Combine(Fx, "data-later.img"), SimulatedConsole.Dev(c, "nandc"), true);
                File.Copy(Path.Combine(Fx, "kernel-other.img"), SimulatedConsole.Dev(c, "boot2"), true);
                File.WriteAllText(Path.Combine(c, "faults.json"), "{\"crash_at\":\"restore-wrote-nandc\"}");
                var p = Worker(s, Req("restore-prepare", c, snap.PrimaryPath), out var stdin, false);
                // read the plan, confirm it
                string planLine = null; var lines = new List<string>();
                var t0 = DateTime.UtcNow;
                while (DateTime.UtcNow - t0 < TimeSpan.FromMinutes(10)) { var l = p.StandardOutput.ReadLine(); if (l == null) break; lines.Add(l); if (l.Contains("\"event\": \"plan\"")) { planLine = l; break; } }
                var plan = planLine == null ? null : Json.Parse(planLine).O("plan");
                if (plan != null) { stdin.WriteLine(Json.Write(new JObj { { "cmd", "restore-confirm" }, { "digest", plan.S("digest") }, { "nonce", plan.S("nonce") } }).Replace("\n", " ")); stdin.Flush(); }
                bool exited = p.WaitForExit(600000);
                var rec = Directory.GetFiles(s.RecordsFolder, "restore-*.json").Select(Record.Load).FirstOrDefault();
                var log = File.ReadAllText(Path.Combine(c, "writes.log"));
                Check(plan != null && exited && p.ExitCode != 0 && rec != null && rec.S("state") == "writing" && log.Contains("write nandc") && !log.Contains("write boot2 "),
                      "restore worker ended after writing the data storage: the record stays \"writing\" (never \"restored\"), the startup software was not written, nothing more happened");
                Check(Verifier.VerifyFile(snap.PrimaryPath).Ok, "the backup it was restoring from is unchanged");
            }
            Window();
        }

        // ---------------------------------------------------------------- the window
        static AutomationElement Win(int pid, int ms = 20000)
        {
            var until = DateTime.UtcNow.AddMilliseconds(ms);
            while (DateTime.UtcNow < until)
            {
                AutomationElement w = null;
                try { w = AutomationElement.RootElement.FindFirst(TreeScope.Children, new PropertyCondition(AutomationElement.ProcessIdProperty, pid)); }
                catch (ElementNotAvailableException) { } catch (System.Runtime.InteropServices.COMException) { } catch (InvalidOperationException) { }
                if (w != null) return w; Thread.Sleep(300);
            }
            return null;
        }
        /// <summary>the element inside root (one of the test's own windows); a window that is closing or slow to answer UI Automation is
        /// "not found yet", never an exception that ends the run</summary>
        static AutomationElement Find(AutomationElement root, string name, ControlType type, int ms = 20000)
        {
            var until = DateTime.UtcNow.AddMilliseconds(ms);
            while (DateTime.UtcNow < until)
            {
                var e = TryFind(root, name, type);
                if (e != null) return e; Thread.Sleep(300);
            }
            return null;
        }
        static AutomationElement TryFind(AutomationElement root, string name, ControlType type)
        {
            try { return root?.FindFirst(TreeScope.Descendants, new AndCondition(new PropertyCondition(AutomationElement.NameProperty, name), new PropertyCondition(AutomationElement.ControlTypeProperty, type))); }
            catch (ElementNotAvailableException) { return null; }
            catch (System.Runtime.InteropServices.COMException) { return null; }   // UIA_E_TIMEOUT and the like: try again until the deadline
            catch (InvalidOperationException) { return null; }
        }
        /// <summary>the element in any top-level window of process pid (the window itself or a dialog it opened): only the test's own
        /// program is searched, never the whole desktop (an unrelated program that does not answer must not decide a result)</summary>
        static AutomationElement FindOwned(int pid, string name, ControlType type, int ms)
        {
            var until = DateTime.UtcNow.AddMilliseconds(ms);
            while (DateTime.UtcNow < until)
            {
                AutomationElementCollection wins = null;
                try { wins = AutomationElement.RootElement.FindAll(TreeScope.Children, new PropertyCondition(AutomationElement.ProcessIdProperty, pid)); }
                catch (ElementNotAvailableException) { } catch (System.Runtime.InteropServices.COMException) { } catch (InvalidOperationException) { }
                if (wins != null) foreach (AutomationElement w in wins) { var e = TryFind(w, name, type); if (e != null) return e; }
                Thread.Sleep(300);
            }
            return null;
        }
        static void Click(AutomationElement e) => ((InvokePattern)e.GetCurrentPattern(InvokePattern.Pattern)).Invoke();
        static List<Process> Workers(int parentStart) => Process.GetProcessesByName(Path.GetFileNameWithoutExtension(Utility)).Where(x => { try { return x.MainWindowHandle == IntPtr.Zero; } catch { return false; } }).ToList();

        static (Process ui, AutomationElement w, Settings s, string console) StartBackupInWindow(string tag)
        {
            var d = NewDir("lc-ui-" + tag); var s = SettingsIn(d, false); var c = Program.Console_("modified-noorig");
            var ui = Process.Start(new ProcessStartInfo(Utility, "--simulated-console \"" + c + "\" --settings \"" + s.Path + "\"") { UseShellExecute = false });
            var w = Win(ui.Id); if (w == null) return (ui, null, s, c);
            Click(Find(w, "Back Up Console", ControlType.Button)); Click(Find(w, "Start Backup (Simulated Console)", ControlType.Button));
            // wait until it is reading (the worker exists and progress is shown)
            var until = DateTime.UtcNow.AddSeconds(60);
            while (DateTime.UtcNow < until && Find(w, "Cancel", ControlType.Button, 500) == null) { }
            Thread.Sleep(2500);
            return (ui, w, s, c);
        }

        static void Window()
        {
            // a. the window closed during a backup: it asks; Stop the Backup and Close stops the backup; every process exits; nothing incomplete
            //    is accepted (the question is the window's own dialog since freeze 12; before, a message box's Yes)
            {
                var before = DateTime.UtcNow;
                var (ui, w, s, c) = StartBackupInWindow("close");
                var devBefore = Directory.GetFiles(Path.Combine(c, "dev")).ToDictionary(f => f, f => Sha(f));
                Click(Find(w, "Close", ControlType.Button));
                var yes = FindOwned(ui.Id, "Stop the Backup and Close", ControlType.Button, 15000);
                var dialogText = yes == null ? null : TreeWalker.ControlViewWalker.GetParent(yes)?.Current.Name;
                if (yes != null) Click(yes);
                bool uiGone = ui.WaitForExit(60000); Thread.Sleep(3000);
                var rec = Directory.GetFiles(s.RecordsFolder, "backup-*.json").Select(Record.Load).FirstOrDefault();
                var stillRunning = Workers(0).Where(x => x.StartTime.ToUniversalTime() > before).ToList();
                Check(yes != null && uiGone && rec != null && (rec.S("state") == "canceled" || rec.S("state") == "stopped") && !Directory.GetFiles(s.PrimaryFolder, "*.zip").Any() && stillRunning.Count == 0 && devBefore.All(kv => Sha(kv.Key) == kv.Value),
                      "closing the window during a backup asks first; Stop the Backup and Close stops it at once: record " + rec?.S("state") + ", no final file, no process left, the console unchanged");
                Check(History.List(s).Any(e => e.Status == BackupStatus.Incomplete), "the stopped backup is listed as Incomplete");
            }
            // b. the window process ended without warning: its worker sees its input end and stops
            {
                var before = DateTime.UtcNow;
                var (ui, w, s, c) = StartBackupInWindow("kill-ui");
                ui.Kill(); ui.WaitForExit(10000);
                var until = DateTime.UtcNow.AddSeconds(60); List<Process> left;
                do { Thread.Sleep(1000); left = Workers(0).Where(x => x.StartTime.ToUniversalTime() > before).ToList(); } while (left.Count > 0 && DateTime.UtcNow < until);
                var rec = Directory.GetFiles(s.RecordsFolder, "backup-*.json").Select(Record.Load).FirstOrDefault();
                Check(left.Count == 0 && rec != null && rec.S("state") == "canceled" && !Directory.GetFiles(s.PrimaryFolder, "*.zip").Any(), "the window process ended without warning: its worker canceled the backup by itself and exited (record " + rec?.S("state") + ")");
            }
            // c. the worker ended without warning: the window says so; the file is Incomplete
            {
                var before = DateTime.UtcNow;
                var (ui, w, s, c) = StartBackupInWindow("kill-worker");
                foreach (var x in Workers(0).Where(x => x.StartTime.ToUniversalTime() > before)) { try { x.Kill(); } catch { } }
                var msg = Find(w, "The backup ended unexpectedly", ControlType.Text, 20000);
                var rec = Directory.GetFiles(s.RecordsFolder, "backup-*.json").Select(Record.Load).FirstOrDefault();
                Check(msg != null && rec != null && rec.S("state") == "reading" && History.List(s).Any(e => e.Status == BackupStatus.Incomplete) && !Directory.GetFiles(s.PrimaryFolder, "*.zip").Any(),
                      "the worker ended without warning: the window says the backup ended unexpectedly; the file is listed Incomplete, never Verified");
                try { ui.Kill(); } catch { }
            }
            // d. after a backup has finished (its worker only waits for Restart the Console): closing asks nothing, nothing is stopped or
            //    marked Incomplete, the window and its worker exit, the backup stays Verified
            {
                var before = DateTime.UtcNow;
                var d = NewDir("lc-ui-done"); var s = SettingsIn(d, false); var c = Program.Console_("modified-noorig");
                var ui = Process.Start(new ProcessStartInfo(Utility, "--simulated-console \"" + c + "\" --settings \"" + s.Path + "\"") { UseShellExecute = false });
                var w = Win(ui.Id);
                Click(Find(w, "Back Up Console", ControlType.Button)); Click(Find(w, "Start Backup (Simulated Console)", ControlType.Button));
                var done = Find(w, "Done", ControlType.Button, 600000);
                bool workerWaiting = Workers(0).Any(x => x.StartTime.ToUniversalTime() > before);
                Click(Find(w, "Close", ControlType.Button));
                // any question at close: a message box's Yes, the window's own questions about a task or about settings
                var asked = FindOwned(ui.Id, "Yes", ControlType.Button, 2000) ?? FindOwned(ui.Id, "Keep Working", ControlType.Button, 1500) ?? FindOwned(ui.Id, "Keep the Window Open", ControlType.Button, 1000) ?? FindOwned(ui.Id, "Don't Save", ControlType.Button, 1000);
                if (asked != null) Click(asked);
                bool uiGone = ui.WaitForExit(30000); Thread.Sleep(3000);
                var stillRunning = Workers(0).Where(x => x.StartTime.ToUniversalTime() > before).ToList();
                var st = History.List(s).FirstOrDefault()?.Status;
                Check(done != null && workerWaiting && asked == null && uiGone && stillRunning.Count == 0 && st == BackupStatus.Verified,
                      "closing the window after a finished backup asks nothing: the window and its waiting worker exit, the backup stays Verified (" + (asked != null ? "asked" : "no question") + ", " + st + ")");
                if (!uiGone) try { ui.Kill(); } catch { }
                foreach (var x in stillRunning) { try { x.Kill(); } catch { } }
            }
        }
    }
}
