// Classic Cart Studio console backup and restore engine tests: the optional scheduled backup check. GPL-3.0-or-later. Windows' task
// scheduler is replaced by a recording stand-in: nothing is registered on this PC.
using CcsBackup.Library;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using static CcsBackup.Tests.Program;

namespace CcsBackup.Tests
{
    sealed class FakeScheduler : ITaskScheduler
    {
        public readonly List<string> Calls = new List<string>(); public string QueryOutput; public int QueryRc = 1; public string LastXml;
        public int Run(string args, out string output)
        {
            Calls.Add(args); output = "";
            var i = args.IndexOf("/XML \""); if (i >= 0) { var p = args.Substring(i + 6); p = p.Substring(0, p.IndexOf('"')); LastXml = File.ReadAllText(p); }
            if (args.StartsWith("/Query")) { output = QueryOutput ?? ""; return QueryRc; }
            return 0;
        }
    }

    static partial class Tests
    {
        public static void Schedule()
        {
            var dir = NewDir("schedule"); var s = SettingsIn(dir, false);
            var exe = Path.Combine(dir, "CCS-Console-Workbench.exe"); File.WriteAllBytes(exe, new byte[] { 0x4D, 0x5A });
            var spec = new ScheduleSpec { Exe = exe, SettingsFile = s.Path ?? Path.Combine(dir, "settings.json"), ReportFolder = Path.Combine(dir, "reports"), Frequency = "weekly", Day = DayOfWeek.Sunday, Hour = 12 };
            if (!File.Exists(spec.SettingsFile)) { s.Path = spec.SettingsFile; s.InMemory = false; s.Save(); }
            var fake = new FakeScheduler();
            Check(fake.Calls.Count == 0, "nothing is scheduled unless the person turns it on");
            var xml = ScheduledCheck.TaskXml(spec, "PC\\someone");
            Check(xml.Contains("<LogonType>InteractiveToken</LogonType>") && xml.Contains("<RunLevel>LeastPrivilege</RunLevel>") && !xml.Contains("Password") && xml.Contains("<WakeToRun>false</WakeToRun>") && xml.Contains("<MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>") && xml.Contains("<ExecutionTimeLimit>PT2H</ExecutionTimeLimit>"),
                "the task runs as this user only while signed in, with no stored password, no elevation, no waking the PC, one at a time, at most 2 hours");
            Check(xml.Contains("<Command>" + System.Security.SecurityElement.Escape(exe) + "</Command>") && xml.Contains(ScheduledCheck.Flag) && xml.Contains("Remove Schedule"), "the task starts the Workbench where the person keeps it, with the offline check, and says how to remove it");
            Check(new ScheduleSpec { Exe = exe, ReportFolder = spec.ReportFolder }.Problem() != null && new ScheduleSpec { Exe = exe + "\"", SettingsFile = spec.SettingsFile, ReportFolder = spec.ReportFolder }.Problem() != null, "no schedule without a settings file of the person's, or with a quotation mark in a path");
            var why = ScheduledCheck.Register(fake, spec, "PC\\someone", dir);
            Check(why == null && fake.Calls.Count == 1 && fake.Calls[0].StartsWith("/Create /TN \"" + ScheduledCheck.TaskName + "\" /XML") && fake.LastXml != null && !File.Exists(Path.Combine(dir, "ccs-workbench-check-task.xml")),
                "registering: one /Create of the named task from its definition; the definition file is removed afterwards (" + why + ")");
            fake.QueryRc = 0; fake.QueryOutput = "TaskName: \\" + ScheduledCheck.TaskName + "\nTask To Run: \"" + exe + "\" --scheduled-check --settings \"x\"\nLast Run Time: 11/22/2026 12:00:00 PM\nLast Result: 0\nNext Run Time: 11/29/2026 12:00:00 PM\nSchedule Type: Weekly\n";
            var q = ScheduledCheck.Query(fake);
            Check(q.Registered && q.LastRun.Contains("2026") && q.NextRun != null && !q.ExeMissing, "the window shows the task as Windows has it: what it starts, its last run and result, its next run");
            File.Delete(exe);
            var q2 = ScheduledCheck.Query(fake);
            Check(q2.ExeMissing && q2.Problem.Contains("no longer"), "a moved or deleted program is shown plainly: checks do not run (" + q2.Problem + ")");
            Check(ScheduledCheck.Remove(fake) == null && fake.Calls.Last().StartsWith("/Delete /TN \"" + ScheduledCheck.TaskName + "\" /F"), "Remove Schedule deletes exactly that task");
            // the scheduled run: offline verification and a report; a damaged backup is a problem, a missing folder too
            var c = Console_("stock"); var b = Backup(c, s);
            var bad = Path.Combine(s.PrimaryFolder, "damaged copy.zip"); var bytes = File.ReadAllBytes(b.PrimaryPath); bytes[bytes.Length / 2] ^= 1; File.WriteAllBytes(bad, bytes);
            var probs = ScheduledCheck.RunCheck(s, spec.ReportFolder);
            var reports = Directory.GetFiles(spec.ReportFolder, "Backup check *.json");
            Check(probs.Count == 1 && probs[0].StartsWith("damaged copy.zip") && reports.Length == 1 && Verifier_Ok(b.PrimaryPath), "the check verifies every backup offline, reports the damaged one, and changes no backup");
            var rows = CcsBackup.Browse.BackupList.List(s); var good = rows.First(r => r.Path == b.PrimaryPath); var badRow = rows.First(r => r.Path == bad);
            Check(good.StatusText == "Verified" && good.LastVerifiedBy == "check" && good.LastVerifiedUtc > DateTime.UtcNow.AddMinutes(-10) && (badRow.Status == CcsBackup.Browse.BrowseStatus.Damaged || badRow.Status == CcsBackup.Browse.BrowseStatus.Salvage),
                "the lists show the scheduled check as each file's last full check: Verified now for the good one, Damaged for the changed copy (" + good.CheckText + "; " + badRow.StatusText + ")");
            var s2 = Settings.Portable(Path.Combine(dir, "not-there"), null);
            Check(ScheduledCheck.RunCheck(s2, spec.ReportFolder).Any(p => p.Contains("not available")), "an unavailable backup folder (a disconnected drive) is reported, not skipped silently");
        }
        static bool Verifier_Ok(string f) => CcsBackup.Archive.Verifier.VerifyFile(f).Ok;
    }
}
