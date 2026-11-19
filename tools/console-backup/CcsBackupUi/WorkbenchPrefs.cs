// Classic Cart Studio console backup and restore window. Copyright (C) 2026 Rance9. GPL-3.0-or-later.
// CCS Console Workbench: Settings (the backup locations, and where the settings are kept) and what happens to the preferences when the window
// closes. One preference model (CcsBackup.Library.Settings: the backup location, the additional copy and the console family chosen at the
// top of the window) and one file schema (ccs-console-backup-settings/1; fields a newer version wrote are kept) in both hosts.
//   The standalone   preferences live in memory until the person saves them to a settings file of their own (Save Settings…; no AppData
//                    file, no registry, nothing beside the program, no automatic remembering). Closing the window with changes that are not
//                    in that file (made on Settings, by the console choice, or still typed in Settings' boxes) asks: Save Settings…,
//                    Don't Save, Cancel. Loading another settings file asks the same first.
//   Inside the app   a change is used and saved at once to the app's Workbench settings file (%LOCALAPPDATA%\CCS Console Backup, where it
//                    always was), when a box is left, Enter is pressed, a folder is chosen or the box is ticked: never on every keystroke,
//                    and only when the value is valid. A save that fails says so, keeps the change and offers Try Again; nothing says
//                    "saved" unless the file was written. No save-on-close question.
// Settings, an unsaved HOME design, a console task's safe stop and the USB driver's close question stay separate questions at close; the
// settings question comes first, so its Cancel has not stopped a task or touched a driver.
using CcsBackup.Library;
using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace CcsBackup.Ui
{
    public sealed partial class BackupWorkspace
    {
        /// <summary>a test can answer the file choosers (title, suggested name) → the chosen file, or null for Cancel</summary>
#pragma warning disable CS0649   // set only by tests
        internal static Func<string, string, string> SaveFileHook, OpenFileHook;
#pragma warning restore CS0649
        /// <summary>the preferences as they were last saved (or loaded, or the defaults the standalone starts with)</summary>
        Settings savedPrefs;
        /// <summary>inside the app: why the last automatic save failed (null: the file holds what the window uses)</summary>
        string autoSaveProblem;
        /// <summary>the open Settings page: uses what is still typed in its boxes when it is valid; returns why not otherwise (null: nothing
        /// waiting, or used)</summary>
        Func<string> settingsPending;
        Action settingsRefresh;
        bool closePrefsAnswered;

        static string Sig(Settings s) => (s.PrimaryFolder ?? "") + "\n" + (s.ArchiveFolder ?? "") + "\n" + FamilyKey(s.ConsoleProfile);
        /// <summary>freeze 16: no family saved means the first one (the NES Classic Mini), and an alias means its family, so choosing the SNES
        /// Classic Mini and then the NES Classic Mini again leaves nothing unsaved; an id this version does not know stays as written</summary>
        static string FamilyKey(string id) => ConsoleProfile.Find(id)?.Id ?? (string.IsNullOrEmpty(id) ? ConsoleProfile.Supported[0].Id : id);
        bool PrefsUnsaved => savedPrefs == null || Sig(settings) != Sig(savedPrefs);
        bool Standalone => opt.Portable && !opt.HostedInFullApp;

        /// <summary>at start: what was loaded (or the standalone's defaults) is what is saved; the window's console family is the one the
        /// settings name, when this version knows it (an unknown one is kept in the file and the first family is used)</summary>
        void InitPrefs()
        {
            savedPrefs = settings.Copy();
            profile = ConsoleProfile.Find(settings.ConsoleProfile) ?? ConsoleProfile.Supported[0];
        }

        /// <summary>the settings a page should use: the window's own; a settings file is read again (another window may have changed it)
        /// only when the window holds exactly what that file held</summary>
        Settings Reload()
        {
            if (settings == null) return Settings.Load(opt.SettingsPath);
            if (settings.InMemory || PrefsUnsaved || string.IsNullOrEmpty(settings.Path)) return settings;
            var s = Settings.Load(settings.Path); savedPrefs = s.Copy();
            // freeze 16: the window keeps its own console family (it changes only through ApplyProfile); when another window saved another
            // family to the file, the difference shows as not saved. An id this version does not know stays in the file as written.
            if (profile != null && FamilyKey(s.ConsoleProfile) != profile.Id && (string.IsNullOrEmpty(s.ConsoleProfile) || ConsoleProfile.Find(s.ConsoleProfile) != null)) s.ConsoleProfile = profile.Id;
            return s;
        }

        /// <summary>a preference changed (a location, the additional copy, the console family): inside the app it is saved now</summary>
        void PrefsChanged()
        {
            if (!Standalone) AutoSave();
            settingsRefresh?.Invoke();
        }

        void AutoSave()
        {
            try { settings.Save(); savedPrefs = settings.Copy(); autoSaveProblem = null; }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is System.Security.SecurityException) { autoSaveProblem = ex.Message; }
        }

        /// <summary>what differs from the saved preferences, in words ("the backup location and the console")</summary>
        string WhatChanged()
        {
            var l = new System.Collections.Generic.List<string>();
            if (savedPrefs == null || (settings.PrimaryFolder ?? "") != (savedPrefs.PrimaryFolder ?? "")) l.Add("the backup location");
            if (savedPrefs == null || (settings.ArchiveFolder ?? "") != (savedPrefs.ArchiveFolder ?? "")) l.Add("the additional copy");
            if (savedPrefs == null || FamilyKey(settings.ConsoleProfile) != FamilyKey(savedPrefs.ConsoleProfile)) l.Add("the console chosen at the top of the window");
            return l.Count == 0 ? "your settings" : l.Count == 1 ? l[0] : string.Join(", ", l.Take(l.Count - 1)) + " and " + l.Last();
        }

        // ------------------------------------------------------------------ the Settings page
        void ShowSettings()
        {
            settings = Reload();
            var sp = PageHead("Settings", Standalone ? "Where Workbench saves new console backups, and the settings file that keeps your choices for next time."
                                                     : "Where Workbench saves new console backups. Changes are saved automatically.");
            var prim = new TextBox { Text = settings.PrimaryFolder };
            var arch = new TextBox { Text = settings.ArchiveFolder ?? "" };
            var two = new CheckBox { Content = "Also save an additional copy of each new backup", IsChecked = !string.IsNullOrEmpty(settings.ArchiveFolder), Margin = new Thickness(0, 4, 0, 10) };
            var problem = new ContentControl();
            var loc = new StackPanel();
            loc.Children.Add(T("Backup locations", Ty.Heading, true, false, new Thickness(0, 0, 0, 10)));
            // a value is used when you leave its box, press Enter, choose a folder with Change… or tick the box (never on every keystroke)
            string Pending()
            {
                var p = prim.Text.Trim(); var a = two.IsChecked == true ? arch.Text.Trim() : null;
                if (p == (settings.PrimaryFolder ?? "") && (a ?? "") == (settings.ArchiveFolder ?? "")) { problem.Content = null; return null; }
                if (!Path.IsPathRooted(p)) return "the backup location is not a full folder path (for example D:\\Console Backups)";
                if (a != null && a.Length == 0) return "the additional copy needs a folder: choose one with Change…, or clear the box";
                if (a != null && !Path.IsPathRooted(a)) return "the additional copy's location is not a full folder path (for example E:\\Console Backups)";
                settings.PrimaryFolder = p; settings.ArchiveFolder = a; problem.Content = null;
                PrefsChanged();
                return null;
            }
            void Use() { var why = Pending(); if (why != null) problem.Content = Note("Not used yet: " + why + ".", "warn"); }
            Button Change(TextBox box) => B("Change…", "Btn", () => { Pick(box); Use(); });
            loc.Children.Add(Field("Backup location: new backups are saved here", prim, Change(prim), 760));
            loc.Children.Add(LinkRow(("Open the Folder", () => OpenPath(prim.Text)), ("Copy the Path", () => CopyText(prim.Text))));
            loc.Children.Add(two);
            var archBox = Field("Additional copy location", arch, Change(arch), 760);
            var archButtons = LinkRow(("Open the Folder", () => OpenPath(arch.Text)), ("Copy the Path", () => CopyText(arch.Text)));
            var none = T("Additional copy not configured. Each new backup is saved once, in the backup location.", Ty.Text, false, true, new Thickness(0, 0, 0, 8));
            loc.Children.Add(archBox); loc.Children.Add(archButtons); loc.Children.Add(none);
            bool sameDisk() { try { return two.IsChecked == true && Path.IsPathRooted(arch.Text) && Path.IsPathRooted(prim.Text) && Durable.VolumeId(arch.Text.Trim()) == Durable.VolumeId(prim.Text.Trim()); } catch (Exception) { return false; } }
            var indep = new ContentControl();
            void Refresh()
            {
                bool on = two.IsChecked == true;
                archBox.Visibility = archButtons.Visibility = on ? Visibility.Visible : Visibility.Collapsed; none.Visibility = on ? Visibility.Collapsed : Visibility.Visible;
                indep.Content = on ? Note(sameDisk() ? "The additional copy is on the same drive as the backup location. That protects against a damaged file, but not against losing the drive or the PC. For real protection, choose another drive."
                                                    : "Each copy is read back and checked on its own. Two folders in the same cloud account, or on the same drive, are not independent protection.", sameDisk() ? "warn" : "info") : null;
            }
            two.Checked += (s, e) => { Refresh(); Use(); }; two.Unchecked += (s, e) => { Refresh(); Use(); };
            arch.TextChanged += (s, e) => Refresh(); prim.TextChanged += (s, e) => Refresh();
            foreach (var box in new[] { prim, arch })
            {
                box.LostKeyboardFocus += (s, e) => Use();
                box.KeyDown += (s, e) => { if (e.Key == Key.Enter) { Use(); e.Handled = true; } };
            }
            loc.Children.Add(indep);
            loc.Children.Add(T("Changing these affects only new backups. Backups already saved stay where they are; Workbench never moves them.", Ty.Small, false, true, new Thickness(0, 4, 0, 8)));
            Refresh();
            loc.Children.Add(problem);
            sp.Children.Add(Card(loc));

            // where the settings are kept, and whether they are saved
            var keep = new StackPanel();
            var state = new ContentControl();
            void State()
            {
                if (Standalone)
                    state.Content = PrefsUnsaved ? Note("Not saved to a settings file: " + WhatChanged() + ". Workbench uses " + (WhatChanged() == "your settings" ? "them" : "the change") + " now; save to keep " + (WhatChanged() == "your settings" ? "them" : "it") + " for next time.", "warn")
                                                 : settings.InMemory ? (UIElement)T("Nothing to save: Workbench is using its usual settings.", Ty.Small, false, true) : Note("Saved in your settings file.", "ok");
                else
                    state.Content = autoSaveProblem == null && !PrefsUnsaved ? (UIElement)Note("Saved.", "ok")
                        : new StackPanel { Children = { Note("Your change is used now, but it could not be saved: " + (autoSaveProblem ?? "the settings file was not written") + ". It is lost when Workbench closes unless it is saved.", "bad"), Buttons(B("Try Again", "Btn", () => { AutoSave(); State(); })) } };
            }
            if (Standalone)
            {
                keep.Children.Add(T("Your settings file", Ty.Heading, true, false, new Thickness(0, 0, 0, 8)));
                if (settings.InMemory) keep.Children.Add(T("Workbench keeps your settings only while it is open: it writes no settings file of its own. To keep them, save them to a settings file you choose, and load that file the next time.", Ty.Text, false, true));
                else { keep.Children.Add(T("Your settings are kept in the settings file you chose:", Ty.Text, false, true)); keep.Children.Add(PathText(settings.Path)); }
                keep.Children.Add(state);
                keep.Children.Add(Buttons(B(settings.InMemory ? "Save Settings…" : "Save Settings", "Btn", () => { if (Pending() is string why) { problem.Content = Note("Not used yet: " + why + ".", "warn"); return; } if (SavePrefs(false)) ShowSettings(); }),
                                          settings.InMemory ? null : B("Save Settings As…", "Btn.Ghost", () => { if (Pending() is string why) { problem.Content = Note("Not used yet: " + why + ".", "warn"); return; } if (SavePrefs(true)) ShowSettings(); }),
                                          B("Load Settings from a File…", "Btn.Ghost", () => { if (LoadPrefs()) ShowSettings(); })));
            }
            else
            {
                keep.Children.Add(T("Where these settings are kept", Ty.Heading, true, false, new Thickness(0, 0, 0, 8)));
                keep.Children.Add(T("On this PC, with Classic Cart Studio's settings:", Ty.Text, false, true));
                keep.Children.Add(PathText(settings.Path));
                keep.Children.Add(state);
            }
            keep.Children.Add(T("Kept with your settings: the backup locations above and the console chosen at the top of the window (now " + profile.Name + "). A settings file is separate from your console backups.", Ty.Small, false, true, new Thickness(0, 6, 0, 0)));
            sp.Children.Add(Card(keep));
            State();
            Show(sp, "settings");
            settingsPending = Pending; settingsRefresh = State;
            leaveCheck = () =>
            {
                var why = Pending(); if (why == null) return true;
                return Ask("Leave Settings?", "What you typed is not used: " + why + ".\n\nLeave Settings without using it?", new[] { "Keep Editing", "Leave Without It" }, 0, 0) == 1;
            };
        }

        /// <summary>saves the preferences to the settings file in use, or to one the person chooses (always when there is none, or for Save
        /// As): true only when the file was written</summary>
        bool SavePrefs(bool saveAs)
        {
            var file = settings.InMemory || saveAs || string.IsNullOrEmpty(settings.Path) ? ChooseFile(true, "Save Workbench settings", "ccs-workbench-settings.json") : settings.Path;
            if (file == null) return false;
            try { settings.Write(file); }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is System.Security.SecurityException || ex is ArgumentException || ex is NotSupportedException)
            {
                Ask("The settings were not saved", "The settings file could not be written: " + ex.Message + "\n\nNothing was saved. Your settings are still used while Workbench is open.", new[] { "OK" }, 0, 0);
                return false;
            }
            settings.InMemory = false; settings.Path = file; savedPrefs = settings.Copy();
            UpdateStatus("Settings saved to " + file + ".");
            return true;
        }

        /// <summary>the standalone's Load Settings from a File…: unsaved preferences are asked about first; a file that is not a Workbench
        /// settings file changes nothing</summary>
        bool LoadPrefs()
        {
            if (settingsPending?.Invoke() is string why && Ask("Settings not used", "What you typed in Settings is not used: " + why + ".\n\nLoad another settings file anyway?", new[] { "Keep Editing", "Load Anyway" }, 0, 0) != 1) return false;
            if (PrefsUnsaved)
            {
                int k = Ask("Save your settings first?", "Your settings have changes that are not saved: " + WhatChanged() + ". Loading another settings file replaces them.", new[] { settings.InMemory ? "Save Settings…" : "Save Settings", "Don't Save", "Cancel" }, 0, 2);
                if (k == 2 || k < 0) return false;
                if (k == 0 && !SavePrefs(false)) return false;
            }
            var file = ChooseFile(false, "Load Workbench settings", null);
            if (file == null) return false;
            try { if (Json.Parse(File.ReadAllText(file)).S("schema") != "ccs-console-backup-settings/1") { Ask("Not a settings file", "That file is not a Workbench settings file. Nothing was changed.", new[] { "OK" }, 0, 0); return false; } }
            catch (Exception ex) { Ask("Not a settings file", "That file could not be read as Workbench settings: " + ex.Message + " Nothing was changed.", new[] { "OK" }, 0, 0); return false; }
            var s = Settings.Load(file); var p = s.ConsoleProfile == null ? ConsoleProfile.Supported[0] : ConsoleProfile.Find(s.ConsoleProfile);   // a file naming no console: the first family
            if (p != null && p != profile && ProfileBlocked() is string blocked) { Ask("Settings not loaded", blocked + " The settings file names another console, so it was not loaded.", new[] { "OK" }, 0, 0); return false; }
            settings = s; savedPrefs = s.Copy();
            if (p != null && p != profile) { profile = p; ShowProfileInStrip(); }
            UpdateStatus("Settings loaded from " + file + "." + (s.ConsoleProfile != null && p == null ? " The console it names is not one this version knows; Workbench keeps " + profile.Name + "." : ""));
            return true;
        }

        string ChooseFile(bool save, string title, string name)
        {
            if (save ? SaveFileHook != null : OpenFileHook != null) return (save ? SaveFileHook : OpenFileHook)(title, name);
            if (save) { var d = new Microsoft.Win32.SaveFileDialog { Title = title, FileName = name, Filter = "Workbench settings (*.json)|*.json", OverwritePrompt = true }; return d.ShowDialog(this) == true ? d.FileName : null; }
            var o = new Microsoft.Win32.OpenFileDialog { Title = title, Filter = "Workbench settings (*.json)|*.json" }; return o.ShowDialog(this) == true ? o.FileName : null;
        }

        /// <summary>the preferences' part of closing the window, before anything else: false keeps the window open, with everything as it was</summary>
        bool PrefsAtClose()
        {
            if (closePrefsAnswered) return true;
            if (settingsPending?.Invoke() is string why)
            {
                int k = Ask("Settings not used", "What you typed in Settings is not used: " + why + ".\n\nClose without it?", new[] { "Keep Editing", "Close Without It" }, 0, 0);
                if (k != 1) return false;
                settingsPending = null;
            }
            if (!PrefsUnsaved) return true;
            if (Standalone)
            {
                int k = Ask("Save your settings?", "Your settings have changes that are not saved: " + WhatChanged() + ".\n\n"
                    + (settings.InMemory ? "Workbench keeps settings only while it is open. To use them next time, save them to a settings file of your own. A settings file is separate from your console backups."
                                         : "To use them next time, save them to your settings file (" + Path.GetFileName(settings.Path) + "). A settings file is separate from your console backups."),
                    new[] { settings.InMemory ? "Save Settings…" : "Save Settings", "Don't Save", "Cancel" }, 0, 2);
                if (k == 1) return true;
                if (k != 0) return false;
                return SavePrefs(false);
            }
            while (true)
            {
                int k = Ask("Your settings change is not saved", "Workbench could not save your settings change: " + (autoSaveProblem ?? "the settings file was not written") + ".\n\nTry saving it again before closing?", new[] { "Try Again", "Close Without Saving", "Cancel" }, 0, 2);
                if (k == 1) return true;
                if (k != 0) return false;
                AutoSave(); settingsRefresh?.Invoke();
                if (!PrefsUnsaved) return true;
            }
        }
    }
}
