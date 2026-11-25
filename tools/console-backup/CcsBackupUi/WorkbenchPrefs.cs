// Classic Cart Studio console backup and restore window. Copyright (C) 2026 Rance9. GPL-3.0-or-later.
// CCS Console Workbench: Settings (the backup locations, CCS HOME's earlier versions to keep, and where the settings are kept) and what happens to the preferences when the window
// closes. One preference model (CcsBackup.Library.Settings: the backup location, the additional copy, the earlier HOME versions and the console family chosen at the
// top of the window) and one file schema (ccs-console-backup-settings/1; fields a newer version wrote are kept) in both hosts.
//   The standalone   preferences live in memory until the person saves them to a settings file of their own (Save Settings…). The
//                    settings file last saved or loaded successfully is remembered (only its path, LastSettingsFile in AppData) and opened
//                    at the next start unless --settings names another; one that cannot be opened is offered for locating, or the usual
//                    settings are used this time, and the record is left as it is. Saving the settings themselves is always the person's
//                    choice. Changes that are not in that file (made on Settings, by the console choice, or still typed in Settings'
//                    boxes) are listed in the window's one close review (BackupWorkspace.CloseReview, WB-UX-20). Loading another settings
//                    file asks about them first.
//   Installed mode   started by Classic Cart Studio from its installation (WorkspaceOptions.Studio, a validated launch context) or inside the
//                    app (HostedInFullApp): a change is used and saved at once to the app's Workbench settings file (%LOCALAPPDATA%\CCS
//                    Console Backup, where it always was), when a box is left, Enter is pressed, a folder is chosen or the box is ticked:
//                    never on every keystroke, and only when the value is valid. A save that fails says so, keeps the change and offers Try
//                    Again; nothing says "saved" unless the file was written. No Save Settings or Save Settings As buttons and no
//                    save-on-close question; a change that could not be saved is listed in the close review, whose Save & Close tries again.
//                    Which mode applies comes from how the program was started, never from which program file runs.
// Unsaved settings and an unsaved HOME design are one close review; a console task's safe stop and the USB driver's close question come
// after it as their own steps, so its Cancel has not stopped a task or touched a driver.
using CcsBackup.Engine;
using CcsBackup.Library;
using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

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
        /// <summary>WB-UX-11: set only while Back Up Console's Edit in Settings opens Settings: where Settings' Back returns, and that the
        /// backup location's box is brought into view with the keyboard focus</summary>
        Action settingsBack; bool settingsFocusLocation; string settingsBackLabel;

        static string Sig(Settings s) => (s.PrimaryFolder ?? "") + "\n" + (s.ArchiveFolder ?? "") + "\n" + FamilyKey(s.ConsoleProfile) + "\n" + s.EarlierHomeKeep + "\n" + s.ConsoleNames.Signature();
        /// <summary>freeze 16: no family saved means the first one (the NES Classic Mini), and an alias means its family, so choosing the SNES
        /// Classic Mini and then the NES Classic Mini again leaves nothing unsaved; an id this version does not know stays as written</summary>
        static string FamilyKey(string id) => ConsoleProfile.Find(id)?.Id ?? (string.IsNullOrEmpty(id) ? ConsoleProfile.Supported[0].Id : id);
        bool PrefsUnsaved => savedPrefs == null || Sig(settings) != Sig(savedPrefs);
        /// <summary>the standalone's explicit settings files; false in installed mode (Classic Cart Studio started it: opt.Studio, or it is
        /// inside the app), whichever program file runs</summary>
        bool Standalone => opt.Portable && !opt.HostedInFullApp && opt.Studio == null;

        /// <summary>at start: what was loaded (or the standalone's defaults) is what is saved; the window's console family is the one the
        /// settings name, when this version knows it (an unknown one is kept in the file and the first family is used)</summary>
        void InitPrefs()
        {
            savedPrefs = settings.Copy();
            profile = ConsoleProfile.Find(settings.ConsoleProfile) ?? ConsoleProfile.Supported[0];
            if (opt.RememberedSettingsProblem != null)
            {   // the remembered settings file could not be opened: asked once the window shows
                EventHandler ask = null; ask = (s, e) => { ContentRendered -= ask; Dispatcher.BeginInvoke(new Action(RememberedFileMissing), System.Windows.Threading.DispatcherPriority.Background); };
                ContentRendered += ask;
            }
        }

        /// <summary>the remembered settings file could not be opened at this start: locate a settings file (one that loads becomes the
        /// remembered file), or use the usual settings this time. The file and the record are never changed or removed here.</summary>
        void RememberedFileMissing()
        {
            var file = opt.RememberedSettingsFile; var why = opt.RememberedSettingsProblem;
            while (true)
            {
                int k = Ask("Your settings file could not be opened", "Workbench opens the settings file you used last, " + file + ", but " + why + ".\n\nLocate your settings file, or use the usual settings this time. Nothing is saved over your settings file unless you save it.",
                            new[] { "Locate the Settings File…", "Use Usual Settings This Time" }, 0, 1);
                if (k != 0) { UpdateStatus("Using the usual settings this time. Your settings file was not changed; Workbench tries it again next time."); return; }
                var pick = ChooseFile(false, "Locate your Workbench settings file", null);
                if (pick == null) continue;
                if (LoadPrefsFrom(pick)) { if (page.Content is FrameworkElement fe && fe.Tag as string == "settings") ShowSettings(); return; }
            }
        }

        /// <summary>remembers this settings file for the next start (the ordinary standalone window only); a record that cannot be written
        /// is said, and changes nothing else</summary>
        void RememberFile(string file)
        {
            if (!Standalone || !opt.RememberSettingsFile) return;
            try { LastSettingsFile.Write(file); opt.RememberedSettingsFile = null; opt.RememberedSettingsProblem = null; }
            catch (Exception ex) { UpdateStatus("Workbench could not note this settings file for next time (" + ex.Message.TrimEnd('.') + "). Load it again next time."); }
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
            if (savedPrefs == null || settings.EarlierHomeKeep != savedPrefs.EarlierHomeKeep) l.Add("the earlier HOME versions to keep");
            if (savedPrefs == null || settings.ConsoleNames.Signature() != savedPrefs.ConsoleNames.Signature()) l.Add("your consoles' names");
            return l.Count == 0 ? "your settings" : l.Count == 1 ? l[0] : string.Join(", ", l.Take(l.Count - 1)) + " and " + l.Last();
        }

        // ------------------------------------------------------------------ the Settings page
        /// <summary>WB-UX-11: Back Up Console's Edit in Settings: this Settings page (the one place the locations are chosen, with its own
        /// checks and saving) at Backup locations, the backup location's box in view and focused, and Back to Back Up Console at the top,
        /// which shows the location as it is then. Changing the location affects new backups only; nothing is moved.</summary>
        void EditBackupLocation()
        {
            settingsBack = () => { Enter("Console Backup"); ShowBackup(); }; settingsFocusLocation = true;
            try { Go("Settings"); }   // with Go's own checks: not while a task runs, and the open page's unsaved work first
            finally { settingsBack = null; settingsFocusLocation = false; }
        }

        /// <summary>Settings > CCS HOME > Earlier HOME versions: how many earlier CCS HOME versions the console keeps when CCS HOME is updated
        /// (one by default; none; or a number), used at once and saved the way every setting here is (the standalone: Save Settings; started
        /// by Classic Cart Studio: automatically). A number that is not valid is not used, and the page says so.</summary>
        UIElement EarlierHomeCard()
        {
            int max = (int)StorageSpec.Spec.O("policies").L("earlier_home_versions_max");
            var c = new StackPanel();
            c.Children.Add(T("CCS HOME", Ty.Heading, true, false, new Thickness(0, 0, 0, 8)));
            c.Children.Add(T("Earlier HOME versions", Ty.Body, true, false, new Thickness(0, 0, 0, 4)));
            var head = T("Keep earlier HOME versions on this console", Ty.Text, false, false, new Thickness(0, 0, 0, 6));
            c.Children.Add(head);
            var one = new RadioButton { GroupName = "earlier-home", Margin = new Thickness(0, 0, 0, 6), Content = new TextBlock { TextWrapping = TextWrapping.Wrap, Text = "Keep one earlier version (recommended)" } };
            var none = new RadioButton { GroupName = "earlier-home", Margin = new Thickness(0, 0, 0, 6), Content = new TextBlock { TextWrapping = TextWrapping.Wrap, Text = "Keep none" } };
            var pick = new RadioButton { GroupName = "earlier-home", Margin = new Thickness(0, 0, 0, 6), Content = new TextBlock { TextWrapping = TextWrapping.Wrap, Text = "Choose a number" } };
            foreach (var r in new[] { one, none, pick }) System.Windows.Automation.AutomationProperties.SetLabeledBy(r, head);
            var num = new TextBox { Width = 64, VerticalContentAlignment = VerticalAlignment.Center };
            System.Windows.Automation.AutomationProperties.SetName(num, "Number of earlier HOME versions to keep (0 to " + max + ")");
            // the number's step arrows (typing it stays): one more / one fewer within 0 to max; the Up and Down keys in the box do the same
            RepeatButton Arrow(string name, string path)
            {
                var b = new RepeatButton { Style = Res<Style>("Wb.Spin"), Content = new System.Windows.Shapes.Path { Data = Geometry.Parse(path), Stroke = Res<Brush>("B.Ink"), StrokeThickness = 1.6, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round, Width = 10, Height = 6, Stretch = Stretch.None } };
                System.Windows.Automation.AutomationProperties.SetName(b, name); b.ToolTip = name; return b;
            }
            var up = Arrow("One more earlier version", "M1,5 L5,1 L9,5"); var down = Arrow("One fewer earlier version", "M1,1 L5,5 L9,1");
            var arrows = new Grid { Margin = new Thickness(4, 0, 0, 0) };
            arrows.RowDefinitions.Add(new RowDefinition()); arrows.RowDefinitions.Add(new RowDefinition());
            down.Margin = new Thickness(0, 2, 0, 0); Grid.SetRow(down, 1); arrows.Children.Add(up); arrows.Children.Add(down);
            var numRow = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(26, 0, 0, 8) };
            numRow.Children.Add(num); numRow.Children.Add(arrows);
            var warn = new ContentControl(); var problem = new ContentControl();
            int keep = settings.EarlierHomeKeep;
            (keep == 1 ? one : keep == 0 ? none : pick).IsChecked = true; num.Text = keep.ToString(System.Globalization.CultureInfo.InvariantCulture);
            int Typed() => int.TryParse(num.Text.Trim(), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var t) ? t : -1;
            void Arrows() { int t = Typed(); bool on = pick.IsChecked == true; up.IsEnabled = on && (t < 0 || t < max); down.IsEnabled = on && t > 0; }
            void Show()
            {
                num.IsEnabled = pick.IsChecked == true; Arrows();
                warn.Content = settings.EarlierHomeKeep == 0 ? Note("There will be no earlier HOME version on the console to go back to. Your backups on this PC will not be removed.", "warn") : null;
            }
            void Set(int v) { if (settings.EarlierHomeVersions == v) { Show(); return; } settings.EarlierHomeVersions = v; problem.Content = null; Show(); PrefsChanged(); }
            void UseNumber()
            {
                if (pick.IsChecked != true) return;
                if (int.TryParse(num.Text.Trim(), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var v) && v >= 0 && v <= max) Set(v);
                else problem.Content = Note("Not used yet: choose a whole number from 0 to " + max + ".", "warn");
            }
            void Step(int by)
            {
                if (pick.IsChecked != true) return;
                int t = Typed(); if (t < 0 || t > max) t = Math.Min(Math.Max(settings.EarlierHomeKeep, 0), max);   // from the number in use when the box holds none
                num.Text = Math.Min(max, Math.Max(0, t + by)).ToString(System.Globalization.CultureInfo.InvariantCulture); num.CaretIndex = num.Text.Length; UseNumber(); Arrows();
            }
            up.Click += (s, e) => Step(1); down.Click += (s, e) => Step(-1);
            one.Checked += (s, e) => Set(1); none.Checked += (s, e) => Set(0); pick.Checked += (s, e) => { Show(); UseNumber(); };
            num.TextChanged += (s, e) => Arrows();
            num.LostKeyboardFocus += (s, e) => UseNumber();
            num.PreviewKeyDown += (s, e) =>
            {
                if (e.Key == Key.Enter) { UseNumber(); e.Handled = true; }
                else if (e.Key == Key.Up) { Step(1); e.Handled = true; }
                else if (e.Key == Key.Down) { Step(-1); e.Handled = true; }
            };
            c.Children.Add(one); c.Children.Add(none); c.Children.Add(pick); c.Children.Add(numRow); c.Children.Add(problem);
            c.Children.Add(T("When you update CCS HOME, keep this many earlier versions on the console so you can go back to one. Your backups on this PC are kept separately.", Ty.Small, false, true, new Thickness(0, 2, 0, 6)));
            c.Children.Add(warn);
            Show();
            var card = Card(c); System.Windows.Automation.AutomationProperties.SetName(card, "Earlier HOME versions"); return card;
        }

        void ShowSettings()
        {
            var back = settingsBack; var backLabel = settingsBackLabel ?? "Back to Back Up Console"; bool focusLocation = settingsFocusLocation; settingsBack = null; settingsBackLabel = null; settingsFocusLocation = false;
            settings = Reload();
            var sp = PageHead("Settings", Standalone ? "Where Workbench saves new console backups, and the settings file that keeps your choices for next time."
                                                     : "Where Workbench saves new console backups. Changes are saved automatically.");
            if (back != null) { var bb = BackButton(backLabel, () => { if (LeaveOk()) back(); }); bb.Margin = new Thickness(0, 0, 0, 16); sp.Children.Insert(0, bb); }
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
            Button Change(TextBox box, string title) => B("Change…", "Btn", () => { Pick(box, title); Use(); });
            loc.Children.Add(Field("Backup location: new backups are saved here", prim, Change(prim, "Choose the backup location"), 760));
            loc.Children.Add(LinkRow(("Open the Folder", () => OpenPath(prim.Text)), ("Copy the Path", () => CopyText(prim.Text))));
            loc.Children.Add(two);
            var archBox = Field("Additional copy location", arch, Change(arch, "Choose the additional copy location"), 760);
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
            sp.Children.Add(EarlierHomeCard());   // CCS HOME > Earlier HOME versions

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
                keep.Children.Add(T("Workbench remembers which settings file you used and opens it next time. Save your changes with Save Settings.", Ty.Text, false, true, new Thickness(0, 0, 0, 6)));
                if (settings.InMemory) keep.Children.Add(T("No settings file is open: Workbench is using these settings now. To keep them, save them to a settings file you choose.", Ty.Text, false, true));
                else { keep.Children.Add(T("Your settings file:", Ty.Text, false, true)); keep.Children.Add(PathText(settings.Path)); }
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
            keep.Children.Add(T("Kept with your settings: the backup locations above, the earlier HOME versions to keep, the console chosen at the top of the window (now " + profile.Name + ") and the names your consoles are shown by on this PC. A settings file is separate from your console backups.", Ty.Small, false, true, new Thickness(0, 6, 0, 0)));
            sp.Children.Add(Card(keep));
            State();
            Show(sp, "settings");
            settingsPending = Pending; settingsRefresh = State;
            leaveCheck = () =>
            {
                var why = Pending(); if (why == null) return true;
                return AskToLeave("What you typed in Settings is not used: " + why + ". If you leave, it is not kept.", "Leave Without Saving", null);
            };
            if (focusLocation)   // Edit in Settings: the backup location's box in view, with the keyboard focus and the cursor at its end
                Dispatcher.BeginInvoke(new Action(() => { if (page.Content != sp) return; prim.BringIntoView(); prim.Focus(); Keyboard.Focus(prim); prim.CaretIndex = prim.Text.Length; }), System.Windows.Threading.DispatcherPriority.Loaded);
        }

        /// <summary>saves the preferences to the settings file in use, or to one the person chooses (always when there is none, or for Save
        /// As): true only when the file was written. A file that cannot be written is said in a themed notice.</summary>
        bool SavePrefs(bool saveAs)
        {
            if (TrySavePrefs(saveAs, out var problem)) return true;
            if (problem != null) Tell("The settings were not saved", "The settings file could not be written: " + problem + "\n\nNothing was saved. Your settings are still used while Workbench is open.");
            return false;
        }

        /// <summary>the save itself: true only when the file was written; problem says why it was not (null: no file was chosen)</summary>
        bool TrySavePrefs(bool saveAs, out string problem)
        {
            problem = null;
            var file = settings.InMemory || saveAs || string.IsNullOrEmpty(settings.Path) ? ChooseFile(true, "Save Workbench settings", "ccs-workbench-settings.json") : settings.Path;
            if (file == null) return false;
            try { settings.Write(file); }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is System.Security.SecurityException || ex is ArgumentException || ex is NotSupportedException)
            {
                problem = ex.Message;
                return false;
            }
            settings.InMemory = false; settings.Path = file; savedPrefs = settings.Copy();
            UpdateStatus("Settings saved to " + file + ".");
            RememberFile(file);
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
            return LoadPrefsFrom(file);
        }

        /// <summary>loads this file as the window's settings: true only when it is a Workbench settings file that could be used (then it is
        /// also remembered for next time); otherwise nothing changes and the person is told why</summary>
        bool LoadPrefsFrom(string file)
        {
            try { if (Json.Parse(File.ReadAllText(file)).S("schema") != "ccs-console-backup-settings/1") { Ask("Not a settings file", "That file is not a Workbench settings file. Nothing was changed.", new[] { "OK" }, 0, 0); return false; } }
            catch (Exception ex) { Ask("Not a settings file", "That file could not be read as Workbench settings: " + ex.Message + " Nothing was changed.", new[] { "OK" }, 0, 0); return false; }
            var s = Settings.Load(file); var p = s.ConsoleProfile == null ? ConsoleProfile.Supported[0] : ConsoleProfile.Find(s.ConsoleProfile);   // a file naming no console: the first family
            if (p != null && p != profile && ProfileBlocked() is string blocked) { Ask("Settings not loaded", blocked + " The settings file names another console, so it was not loaded.", new[] { "OK" }, 0, 0); return false; }
            settings = s; savedPrefs = s.Copy();
            if (p != null && p != profile) { profile = p; ShowProfileInStrip(); }
            UpdateStatus("Settings loaded from " + file + "." + (s.ConsoleProfile != null && p == null ? " The console it names is not one this version knows; Workbench keeps " + profile.Name + "." : ""));
            RememberFile(file);
            return true;
        }

        string ChooseFile(bool save, string title, string name)
        {
            if (save ? SaveFileHook != null : OpenFileHook != null) return (save ? SaveFileHook : OpenFileHook)(title, name);
            if (save) return CcsFileDialog.SaveFile(this, title, null, name, "Workbench settings (*.json)|*.json", true, "settings-file");
            return CcsFileDialog.OpenFile(this, title, null, "Workbench settings (*.json)|*.json", "settings-file");
        }

        /// <summary>the preferences' lines in the close review (WB-UX-20): what is typed in Settings and valid is used first (as on leaving
        /// its box); typed text that is not valid cannot be saved (lost); unsaved preferences (the standalone) or a change the automatic
        /// save could not write (installed mode) can be (save). Installed settings are normally saved already, so nothing is listed.</summary>
        void PrefsForReview(System.Collections.Generic.List<string> save, System.Collections.Generic.List<string> lost)
        {
            if (settingsPending?.Invoke() is string why) lost.Add("What you typed in Settings, which is not used: " + why);
            if (!PrefsUnsaved) return;
            if (Standalone) save.Add("Your settings: " + WhatChanged() + (settings.InMemory ? " (Save & Close asks where to save them)" : " (Save & Close saves them in " + Path.GetFileName(settings.Path) + ")"));
            else save.Add("Your settings change, which could not be saved: " + (autoSaveProblem ?? "the settings file was not written"));
        }

        /// <summary>the close review's Save &amp; Close for the preferences: the standalone saves to its settings file (choosing one first when
        /// there is none); installed mode tries the automatic save again. null when saved; otherwise why not, in words.</summary>
        string SavePrefsForClose()
        {
            if (!PrefsUnsaved) return null;
            if (Standalone) return TrySavePrefs(false, out var why) ? null : why == null ? "Your settings were not saved: no settings file was chosen." : "Your settings were not saved: the settings file could not be written (" + why + ").";
            AutoSave(); settingsRefresh?.Invoke();
            return PrefsUnsaved ? "Your settings change still could not be saved: " + (autoSaveProblem ?? "the settings file was not written") + "." : null;
        }
    }
}
