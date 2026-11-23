// Classic Cart Studio console backup and restore engine. Copyright (C) 2026 Rance9. GPL-3.0-or-later.
// The optional scheduled backup check (Backup Repair, D8): ONE per-user Windows Scheduled Task, registered only when the person turns it on,
// that starts the Workbench executable they chose (where it is, never a copy) with "--scheduled-check": it verifies the backups in the
// chosen folders offline (no console, no USB, no repair, no write to a backup) and writes a report to the chosen report folder. When a check
// finds a problem it shows a window saying so; otherwise it shows nothing. Remove Schedule deletes the task. Nothing else is registered: no
// service, startup entry, updater or hidden copy of the program. Moving or deleting the executable, or an unavailable drive, stops checks;
// the Workbench shows that from the task's own last-run information.
using CcsBackup.Archive;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security;
using System.Text;

namespace CcsBackup.Library
{
    public sealed class ScheduleSpec
    {
        public string Exe, SettingsFile, ReportFolder; public string Frequency = "weekly"; public DayOfWeek Day = DayOfWeek.Sunday; public int Hour = 12, Minute = 0;
        static bool Rooted(string p) { try { return !string.IsNullOrEmpty(p) && p.IndexOfAny(Path.GetInvalidPathChars()) < 0 && Path.IsPathRooted(p); } catch (ArgumentException) { return false; } }
        public string Problem()
        {
            foreach (var x in new[] { Exe, SettingsFile, ReportFolder }) if (x != null && x.IndexOf('"') >= 0) return "A path cannot contain a quotation mark.";
            if (string.IsNullOrEmpty(Exe) || !Rooted(Exe) || !Exe.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) return "Choose where the Workbench program is kept (a full path to its .exe).";
            if (string.IsNullOrEmpty(SettingsFile) || !Rooted(SettingsFile)) return "Save the Workbench settings to a file first: the scheduled check reads its backup folders from it.";
            if (string.IsNullOrEmpty(ReportFolder) || !Rooted(ReportFolder)) return "Choose a folder for the check reports.";
            if (Frequency != "daily" && Frequency != "weekly") return "Choose daily or weekly.";
            if (Hour < 0 || Hour > 23 || Minute < 0 || Minute > 59) return "Choose a valid time.";
            return null;
        }
    }

    public sealed class ScheduleState { public bool Registered; public string Exe, Arguments, Trigger, LastRun, LastResult, NextRun, Problem; public bool ExeMissing; }

    /// <summary>the Windows side, replaceable in tests: schtasks.exe for the real one</summary>
    public interface ITaskScheduler { int Run(string args, out string output); }
    public sealed class SchtasksScheduler : ITaskScheduler
    {
        public int Run(string args, out string output)
        {
            var psi = new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "schtasks.exe"), args) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true, StandardOutputEncoding = Encoding.UTF8 };
            using (var p = Process.Start(psi)) { output = p.StandardOutput.ReadToEnd() + p.StandardError.ReadToEnd(); p.WaitForExit(60000); return p.HasExited ? p.ExitCode : -1; }
        }
    }

    public static class ScheduledCheck
    {
        public const string TaskName = "CCS Console Workbench backup check";
        public const string Flag = "--scheduled-check";
        static string X(string s) => SecurityElement.Escape(s);

        public static string Arguments(ScheduleSpec s) => Flag + " --settings \"" + s.SettingsFile + "\" --report \"" + s.ReportFolder + "\"";

        /// <summary>the task definition: this user only, only while they are signed in (no stored password), no elevation, no retries,
        /// stopped after 2 hours, never started on battery when that would drain it, not woken from sleep</summary>
        public static string TaskXml(ScheduleSpec s, string userId)
        {
            var start = new DateTime(2026, 1, 4, s.Hour, s.Minute, 0).ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture);
            var trig = s.Frequency == "daily" ? "<ScheduleByDay><DaysInterval>1</DaysInterval></ScheduleByDay>" : "<ScheduleByWeek><DaysOfWeek><" + s.Day + " /></DaysOfWeek><WeeksInterval>1</WeeksInterval></ScheduleByWeek>";
            return "<?xml version=\"1.0\" encoding=\"UTF-16\"?>\n<Task version=\"1.2\" xmlns=\"http://schemas.microsoft.com/windows/2004/02/mit/task\">\n" +
                   "  <RegistrationInfo><Description>Checks your NES Classic console backups (offline). Created by CCS Console Workbench when you turned on scheduled checks; remove it there with Remove Schedule.</Description></RegistrationInfo>\n" +
                   "  <Triggers><CalendarTrigger><StartBoundary>" + start + "</StartBoundary><Enabled>true</Enabled>" + trig + "</CalendarTrigger></Triggers>\n" +
                   "  <Principals><Principal id=\"Author\"><UserId>" + X(userId) + "</UserId><LogonType>InteractiveToken</LogonType><RunLevel>LeastPrivilege</RunLevel></Principal></Principals>\n" +
                   "  <Settings><MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy><DisallowStartIfOnBatteries>true</DisallowStartIfOnBatteries><StopIfGoingOnBatteries>true</StopIfGoingOnBatteries>" +
                   "<StartWhenAvailable>true</StartWhenAvailable><WakeToRun>false</WakeToRun><ExecutionTimeLimit>PT2H</ExecutionTimeLimit><Enabled>true</Enabled></Settings>\n" +
                   "  <Actions Context=\"Author\"><Exec><Command>" + X(s.Exe) + "</Command><Arguments>" + X(Arguments(s)) + "</Arguments></Exec></Actions>\n</Task>\n";
        }

        /// <summary>registers the task (the person asked for it); xmlFolder: where the definition is written for schtasks and removed again</summary>
        public static string Register(ITaskScheduler ts, ScheduleSpec s, string userId, string xmlFolder)
        {
            var why = s.Problem(); if (why != null) return why;
            if (!File.Exists(s.Exe)) return "The Workbench program was not found at " + s.Exe + ".";
            var xml = Path.Combine(xmlFolder, "ccs-workbench-check-task.xml");
            File.WriteAllText(xml, TaskXml(s, userId), Encoding.Unicode);
            try { int rc = ts.Run("/Create /TN \"" + TaskName + "\" /XML \"" + xml + "\" /F", out var o); return rc == 0 ? null : "Windows did not register the scheduled check: " + o.Trim(); }
            finally { try { File.Delete(xml); } catch { } }
        }

        public static string Remove(ITaskScheduler ts) { int rc = ts.Run("/Delete /TN \"" + TaskName + "\" /F", out var o); return rc == 0 ? null : "Windows did not remove the scheduled check: " + o.Trim(); }

        /// <summary>the task as Windows has it now (schtasks /Query /V /FO LIST): what it starts, when, its last run and result</summary>
        public static ScheduleState Query(ITaskScheduler ts)
        {
            var st = new ScheduleState(); int rc = ts.Run("/Query /TN \"" + TaskName + "\" /V /FO LIST", out var o);
            if (rc != 0) return st;
            st.Registered = true;
            foreach (var line in o.Split('\n'))
            {
                int i = line.IndexOf(':'); if (i < 0) continue; var k = line.Substring(0, i).Trim(); var v = line.Substring(i + 1).Trim();
                if (k == "Task To Run") st.Exe = v; else if (k == "Last Run Time") st.LastRun = v; else if (k == "Last Result") st.LastResult = v; else if (k == "Next Run Time") st.NextRun = v; else if (k == "Schedule Type") st.Trigger = v;
            }
            var exe = st.Exe != null && st.Exe.StartsWith("\"") ? st.Exe.Substring(1, Math.Max(0, st.Exe.IndexOf('"', 1) - 1)) : st.Exe?.Split(new[] { " --" }, StringSplitOptions.None)[0];
            st.ExeMissing = exe != null && !File.Exists(exe);
            if (st.ExeMissing) st.Problem = "The Workbench program the schedule starts is no longer at " + exe + ", so checks do not run. Turn the schedule on again from where the program is now, or remove it.";
            return st;
        }

        /// <summary>the scheduled run: every backup in the settings' folders verified offline; a report; returns the problems found</summary>
        public static List<string> RunCheck(Settings s, string reportFolder, Func<bool> cancelled = null)
        {
            var problems = new List<string>(); var rows = new List<object>();
            foreach (var folder in new[] { s.PrimaryFolder, s.ArchiveFolder }.Where(f => !string.IsNullOrEmpty(f)))
            {
                if (!Directory.Exists(folder)) { problems.Add("The backup folder " + folder + " is not available (a drive missing or disconnected?)."); continue; }
                // the console backups, then the USB and SD backups in the location's own subfolder (each checked by its own format's rules)
                var storage = Path.Combine(folder, External.ExternalFormat.FolderName);
                foreach (var f in Directory.GetFiles(folder, "*.zip").OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
                    .Concat(Directory.Exists(storage) ? Directory.GetFiles(storage, "*.zip").OrderBy(x => x, StringComparer.OrdinalIgnoreCase) : Enumerable.Empty<string>()))
                {
                    var v = External.AnyBackup.Verify(f, null, cancelled);
                    rows.Add(new JObj { { "file", f }, { "ok", v.Ok }, { "problem", v.Ok ? null : v.Problems[0] }, { "checked_utc", Record.Now() } });
                    if (!v.Ok) problems.Add(Path.GetFileName(f) + ": " + v.Problems[0]);
                }
            }
            Directory.CreateDirectory(reportFolder);
            var stamp = DateTime.Now.ToString("yyyy-MM-dd HHmm", CultureInfo.InvariantCulture);
            Durable.WriteAtomic(Path.Combine(reportFolder, "Backup check " + stamp + ".json"), Json.Write(new JObj { { "schema", "ccs-backup-check/1" }, { "checked", rows }, { "problems", problems.Cast<object>().ToList() } }));
            return problems;
        }
    }
}
