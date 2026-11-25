// Classic Cart Studio console backup and restore window. Copyright (C) 2026 Rance9. GPL-3.0-or-later.
// Backup Repair (offline, no console and no USB driver): backup files on this PC are checked, copied, given recovery data or repaired,
// and a repair always writes a new file (the original stays exactly as it is). Open a Backup File... brings in a backup file from any
// folder on this PC for this window only: it is listed apart, with the same actions, and is never moved, copied first or added to the
// backup locations (Close This File puts it away again). It is not Console Rescue, which concerns a console that no
// longer starts and changes the console. The optional scheduled check is a per-user Windows task that the person turns on here, pointing
// at the Workbench program, settings file and report folder they choose; it checks backups offline and shows a window only when it finds a
// problem. Nothing is scheduled unless the person turns it on, and Remove Schedule takes it away again. A check of a file runs only when
// the person asks for it.
using CcsBackup.Browse;
using CcsBackup.Engine;
using CcsBackup.Library;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace CcsBackup.Ui
{
    public sealed partial class BackupWorkspace
    {
        /// <summary>the backups in the backup locations with what can be done to each file, then the optional scheduled check; justChecked:
        /// a file the person just checked here (its result is said at the top)</summary>
        void ShowBackupRepair(BackupListEntry justChecked = null, DateTime checkedFrom = default(DateTime))
        {
            settings = Reload();
            var sp = SectionHead("Backup Repair", "Check, copy and repair your backup files on this PC, without a console: run a file check, copy a file to another folder, add recovery data that can repair it later, or make a repaired copy of a damaged one. A repair always writes a new file; the original stays exactly as it is.");
            sp.Children.Add(T("This is not Console Rescue, which is for a console that no longer starts and changes the console.", Ty.Text, false, true, new Thickness(0, -8, 0, 12)));
            if (justChecked != null) sp.Children.Add(CheckedNote(justChecked, checkedFrom));
            sp.Children.Add(Buttons(B("Open a Backup File…", "Btn", OpenFileForRepair), B("Backup Locations", "Btn.Ghost", () => Go("Settings"))));
            List<BackupListEntry> list = null;
            try { list = BackupList.List(settings); }
            catch (Exception) { }
            // a backup file opened from another folder: listed apart, with the same actions; it stays where it is (in the backup locations
            // already, it is simply in the list below)
            if (repairOpened != null && list != null && list.Any(x => string.Equals(x.Path, repairOpened, StringComparison.OrdinalIgnoreCase))) repairOpened = null;
            if (repairOpened != null)
            {
                sp.Children.Add(Group("A backup file you opened", 0));
                sp.Children.Add(T("It stays where it is: it is not moved or added to your backup locations. Anything a repair makes is a new file; this one is only read.", Ty.Text, false, true));
                var row = BackupList.Row(settings, repairOpened); var path = repairOpened;
                var acts = new List<UIElement> { File.Exists(path) ? B("Check the File", "Btn.Ghost", () => CheckForRepair(path)) : null };
                if (row != null && path.EndsWith(CcsBackup.Archive.BackupFormat.Extension, StringComparison.OrdinalIgnoreCase) && (row.Status == BrowseStatus.Damaged || row.Status == BrowseStatus.Salvage) && File.Exists(path)) acts.Add(B("Find What Can Be Saved…", "Btn.Ghost", () => FindWhatCanBeSaved(row)));
                if (row != null) acts.AddRange(RepairActionList(row));
                acts.Add(B("Close This File", "Btn.Ghost", () => { repairOpened = null; ShowBackupRepair(); }));
                sp.Children.Add(row != null ? (UIElement)BackupEntryCard(row, acts.ToArray()) : Card(T("The file could not be read.", Ty.Text, false, true)));
            }
            sp.Children.Add(Group("Your backups", repairOpened != null ? 14 : 0));
            try { if (list == null) list = BackupList.List(settings); }
            catch (Exception ex) { sp.Children.Add(LocationsUnreadable(ex, () => ShowBackupRepair())); }
            if (list != null && list.Count == 0) sp.Children.Add(NoBackupsYet(true, () => ShowBackupRepair()));
            foreach (var e in list ?? new List<BackupListEntry>())
            {
                var x = e; var acts = new List<UIElement> { File.Exists(e.Path) ? B("Check the File", "Btn.Ghost", () => CheckForRepair(x.Path)) : null }; acts.AddRange(RepairActionList(x));
                sp.Children.Add(BackupEntryCard(e, acts.ToArray()));
            }
            if (list != null && list.Count > 0) sp.Children.Add(T(VerifiedNote, Ty.Fine, false, true, new Thickness(0, 2, 0, 0)));
            sp.Children.Add(Group("Scheduled check (optional)", 14));
            Func<bool> scheduleChanged = null;
            sp.Children.Add(ScheduleCard(f => scheduleChanged = f));
            Show(sp, "backup-repair");
            // the schedule's typed choices are not lost silently when the person opens another page
            LeaveGuard(() => scheduleChanged == null || !scheduleChanged() ? null : "The scheduled check's choices, which are not turned on",
                       "The scheduled check's choices are not turned on yet. If you leave, they are not kept.");
        }

        /// <summary>the repair routes offered for one backup (joined from the repair engine); null when there is none to offer</summary>
        partial void RepairActionsFor(BackupListEntry e, List<UIElement> into);
        List<UIElement> RepairActionList(BackupListEntry e) { var l = new List<UIElement>(); RepairActionsFor(e, l); return l; }

        void CheckForRepair(string path) => CheckFile(path, (row, from) => ShowBackupRepair(row, from));

        /// <summary>a backup file from another folder, opened in Backup Repair for this window (never saved, moved or added to a list)</summary>
        string repairOpened;

        /// <summary>Open a Backup File in Backup Repair: any console backup on this PC, chosen with the file picker; it is only read</summary>
        void OpenFileForRepair()
        {
            var chosenFile = CcsFileDialog.OpenFile(this, "Open a console backup to check or repair", null, FileKinds.ConsoleBackupFilter, "console-backup");
            if (chosenFile == null) return;
            repairOpened = chosenFile; ShowBackupRepair();
        }

        // ------------------------------------------------------------------ the optional scheduled check
        /// <summary>changed: given a test of whether the person has changed the schedule's choices without turning it on</summary>
        UIElement ScheduleCard(Action<Func<bool>> changed)
        {
            var c = new StackPanel();
            c.Children.Add(T("Windows can run CCS Console Workbench on a schedule to check your backup files on this PC. Nothing is scheduled unless you turn it on here. The check reads your backup locations from a settings file you saved, writes a report to a folder you choose, and shows a window only when a check finds a problem. It never connects to a console or repairs anything by itself.", 13, false, true, new Thickness(0, 0, 0, 14)));
            ScheduleState st; try { st = ScheduledCheck.Query(new SchtasksScheduler()); } catch (Exception ex) { st = new ScheduleState { Problem = "Windows' task list could not be read: " + ex.Message }; }
            if (st.Registered)
            {
                c.Children.Add(T("On: Windows checks your backups for this user.", 13.5, true));
                c.Children.Add(KeyValues(("Runs", st.Trigger ?? "not known"), ("Next", st.NextRun ?? "not known"), ("Last", st.LastRun ?? "never")));
                if (st.Problem != null) c.Children.Add(Note(st.Problem, "warn"));
                c.Children.Add(Details("Technical details", "Windows task: \"" + ScheduledCheck.TaskName + "\" (for this user only).", "Program it starts: " + (st.Exe ?? "not known"), st.LastResult != null ? "Last result code: " + st.LastResult : null));
                c.Children.Add(Buttons(B("Remove Schedule", "Btn", () =>
                {
                    var why = ScheduledCheck.Remove(new SchtasksScheduler()); if (why != null) Tell("The schedule was not removed", why); ShowBackupRepair();
                })));
                return Card(c);
            }
            c.Children.Add(T("Currently off.", 13.5, true));
            if (st.Problem != null) c.Children.Add(Note(st.Problem, "warn"));
            if (settings.InMemory || string.IsNullOrEmpty(settings.Path))
            {
                c.Children.Add(Note("To turn it on, first save your Workbench settings to a file (Settings, then Save Settings to a File…): the scheduled check reads your backup locations from that file.", "info"));
                c.Children.Add(Buttons(B("Settings", "Btn.Ghost", () => Go("Settings"))));
                return Card(c);
            }
            var self = Process.GetCurrentProcess().MainModule.FileName;
            var exe = new TextBox { Text = opt.Portable ? self : "" }; System.Windows.Automation.AutomationProperties.SetName(exe, "Workbench program");
            var report = new TextBox { Text = Path.Combine(settings.PrimaryFolder, "Check reports") }; System.Windows.Automation.AutomationProperties.SetName(report, "Report folder");
            var freq = new ComboBox { MinWidth = 120 }; freq.Items.Add(new ComboBoxItem { Content = "Weekly", Tag = "weekly", IsSelected = true }); freq.Items.Add(new ComboBoxItem { Content = "Daily", Tag = "daily" });
            var day = new ComboBox { MinWidth = 140 }; foreach (DayOfWeek d in Enum.GetValues(typeof(DayOfWeek))) day.Items.Add(new ComboBoxItem { Content = d.ToString(), Tag = d, IsSelected = d == DayOfWeek.Sunday });
            var hour = new ComboBox { MinWidth = 110 }; for (int h = 0; h < 24; h++) hour.Items.Add(new ComboBoxItem { Content = new DateTime(2000, 1, 1, h, 0, 0).ToString("h tt", En), Tag = h, IsSelected = h == 12 });
            System.Windows.Automation.AutomationProperties.SetName(freq, "How often"); System.Windows.Automation.AutomationProperties.SetName(day, "Day"); System.Windows.Automation.AutomationProperties.SetName(hour, "Time");
            string exe0 = exe.Text, report0 = report.Text;
            changed(() => exe.Text != exe0 || report.Text != report0 || freq.SelectedIndex != 0 || day.SelectedIndex != (int)DayOfWeek.Sunday || hour.SelectedIndex != 12);
            c.Children.Add(Field("Workbench program (keep it where it is; the schedule starts it from here)", exe, B("Choose…", "Btn.Ghost", () => { var chosenFile = CcsFileDialog.OpenFile(this, "Choose CCS-Console-Workbench.exe", null, "CCS Console Workbench (*.exe)|*.exe", "workbench-program"); if (chosenFile != null) exe.Text = chosenFile; })));
            c.Children.Add(Field("Report folder", report));
            c.Children.Add(FieldRow(("How often", freq, 150), ("Day (weekly)", day, 170), ("Time", hour, 130)));
            c.Children.Add(T("Settings file it reads:", Ty.Small, false, true, new Thickness(0, 0, 0, 2))); c.Children.Add(PathText(settings.Path, 12));
            c.Children.Add(Buttons(B("Turn On the Schedule", "Btn.Primary", () =>
            {
                var spec = new ScheduleSpec { Exe = exe.Text.Trim(), SettingsFile = settings.Path, ReportFolder = report.Text.Trim(), Frequency = (string)((ComboBoxItem)freq.SelectedItem).Tag, Day = (DayOfWeek)((ComboBoxItem)day.SelectedItem).Tag, Hour = (int)((ComboBoxItem)hour.SelectedItem).Tag };
                var why = spec.Problem();
                if (why == null && !File.Exists(spec.Exe)) why = "The Workbench program was not found at that path.";
                if (why == null && Ask("Turn on the scheduled check?", "Windows will run " + Path.GetFileName(spec.Exe) + " " + (spec.Frequency == "daily" ? "every day" : "every " + spec.Day) + " at " + new DateTime(2000, 1, 1, spec.Hour, spec.Minute, 0).ToString("h:mm tt", En) + " for this user, to check your backups offline. You can remove it here at any time.",
                                       new[] { "Turn It On", "Cancel" }, 0, 1) != 0) return;
                if (why == null) why = ScheduledCheck.Register(new SchtasksScheduler(), spec, Environment.UserDomainName + "\\" + Environment.UserName, Path.GetDirectoryName(settings.Path));
                if (why != null) Tell("The schedule was not turned on", why);
                ShowBackupRepair();
            })));
            return Card(c);
        }
    }
}
