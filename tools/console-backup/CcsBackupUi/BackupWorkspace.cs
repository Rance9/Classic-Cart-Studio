// Classic Cart Studio console backup and restore window. Copyright (C) 2026 Rance9. GPL-3.0-or-later.
// CCS Console Workbench: one native window shared by the standalone CCS-Console-Workbench.exe and Classic Cart Studio for Windows (which
// opens it from its first screen and its Studio menu, without the standalone's Classic Cart Studio introduction page). It needs no
// collection, game, controller or HOME setup. It opens on the Dashboard (WorkbenchShell.cs, with the side column, the console strip and the
// status strip). This file: the window, Console Backup (Back Up Console and the dated list of backups with Check the File, Open Folder and
// Restore on the one selected), Restore a Backup, Prepare Console and Return to Original Software, and the file check. Every console
// operation runs in the controlled worker (WorkerClient); checking a backup file runs here (it never touches a console).
using CcsBackup.Archive;
using CcsBackup.Engine;
using CcsBackup.Library;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shell;

namespace CcsBackup.Ui
{
    public sealed class WorkspaceOptions
    {
        public bool HostedInFullApp;
        public string ProductName = "CCS Console Workbench", Version = EngineInfo.Version;
        public string WorkerExe; public List<string> WorkerArgs = new List<string> { WorkerHost.Flag };
        public string SettingsPath, SimulatedConsole;
        /// <summary>the standalone: ordinary preferences in memory unless a settings file was chosen (--settings, the remembered file, or
        /// Save Settings / Load Settings)</summary>
        public bool Portable;
        /// <summary>the ordinary standalone window (set only by its program; never in installed mode or with a simulated console): a settings
        /// file it saves or loads successfully is remembered for its next start (LastSettingsFile)</summary>
        public bool RememberSettingsFile;
        /// <summary>the remembered settings file that could not be opened at this start, and why (the window offers to locate a file or to use
        /// the usual settings this time; the file and the record are left as they are)</summary>
        public string RememberedSettingsFile, RememberedSettingsProblem;
        public string FullAppUrl = "https://classiccartstudio.com/app/";   // the official Windows download page (Get Full Classic Cart Studio)
        public ImageSource Wordmark, AppIcon; public string NoticeText;
        /// <summary>Classic Cart Studio for Windows: the HOME package it prepared last (its data folder's native-home\current), for HOME
        /// Designer's preview drawn with the person's own games; null in the standalone Workbench</summary>
        public string PreparedHomePackage;
        /// <summary>the Workbench's own release channel ("preview" or "production"; tools/console-backup/WORKBENCH-CHANNEL): with Version it
        /// names the window and picks the update stream. Never Classic Cart Studio's channel or version.</summary>
        public string Channel = "preview";
        /// <summary>set only when Classic Cart Studio for Windows started this program from its installation (installed mode, from a validated
        /// launch context): Studio's own program, version and channel, for the one Classic Cart Studio update; null in a portable launch</summary>
        public StudioLaunch Studio;
        /// <summary>the quiet update check once per session, when the window is first shown (the program sets it; off by default, so a
        /// test or picture renderer never asks the network). Check for Updates in About Workbench works either way (WorkbenchUpdates.cs).</summary>
        public bool CheckForUpdates;
    }

    /// <summary>Classic Cart Studio, as the installed Workbench knows it: the program that started it, Studio's version and channel</summary>
    public sealed class StudioLaunch { public string Exe, Version, Channel; }

    public sealed partial class BackupWorkspace : Window
    {
        readonly WorkspaceOptions opt; Settings settings;
        readonly ContentControl page = new ContentControl();
        ScrollViewer scroller;
        WorkerClient worker; string workerOp; bool writing, closeWhenDone;
        /// <summary>the person left a console session before its result (Cancel on a review, Done on a check-up): its worker is told to quit,
        /// and nothing it still sends changes the page</summary>
        bool quitting;
        /// <summary>an operation is under way: its worker runs and its result has not arrived (after the result the worker only waits, with the
        /// console idle in its RAM program, for Restart the Console or the window to go). The hosting app asks before it closes.</summary>
        public bool Busy => worker != null && worker.Running && !gotResult && !quitting;
        static readonly CultureInfo En = CultureInfo.GetCultureInfo("en-US");

        /// <summary>WB-UX-07: the window's background before its theme is loaded, the theme's own C.Bg, so nothing can ever show through</summary>
        static readonly Brush BaseBg = Frozen(Color.FromRgb(0x10, 0x10, 0x14));
        static Brush Frozen(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }
        /// <summary>why the window's look (Workbench.xaml and the theme) could not be loaded, said in the status strip (null: loaded)</summary>
        string lookProblem;

        public BackupWorkspace(WorkspaceOptions o, Window owner = null)
        {
            Background = BaseBg;   // first, before anything can draw (WB-UX-07)
            FocusCue.Install();    // focus marks only while the keyboard is in use (WB-UX-14)
            opt = o; settings = o.Portable && o.SettingsPath == null ? Settings.Portable(null, null) : Settings.Load(o.SettingsPath);
            InitPrefs(); HookTerms(); Deactivated += (s, e) => DropHover();
            Title = WindowTitle; HookUpdates();   // this program's own version and channel (WorkbenchUpdates.cs)
            Width = 1180; Height = 780; MinWidth = 860; MinHeight = 560;
            if (owner != null) { Owner = owner; WindowStartupLocation = WindowStartupLocation.CenterOwner; } else WindowFrame.PlaceAtStart(this);   // on the display it was opened on, wholly visible
            // the window's look: a failure is never silent (the status strip says it), and the dark background stays
            try { Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/CcsBackupUi;component/Workbench.xaml", UriKind.Absolute) }); }
            catch (Exception ex) { lookProblem = ex.GetBaseException().Message; }
            if (lookProblem == null) { try { DeriveStyles(); } catch (Exception ex) { lookProblem = ex.GetBaseException().Message; } }
            Background = Res<Brush>("B.Bg") ?? BaseBg; UseLayoutRounding = true; SnapsToDevicePixels = true; TextOptions.SetTextFormattingMode(this, TextFormattingMode.Ideal);
            if (o.AppIcon != null) Icon = o.AppIcon;
            // the taskbar and Alt-Tab icon at its real size (WindowIcon); again once shown, and after a display change, in case WPF re-applies its own
            SourceInitialized += (s, e) => WindowIcon.Apply(this); Loaded += (s, e) => WindowIcon.Apply(this);
            DpiChanged += (s, e) => Dispatcher.BeginInvoke(new Action(() => WindowIcon.Apply(this)), System.Windows.Threading.DispatcherPriority.Background);
            WindowChrome.SetWindowChrome(this, new WindowChrome { CaptionHeight = 38, GlassFrameThickness = new Thickness(0, 0, 0, 1), ResizeBorderThickness = new Thickness(6), UseAeroCaptionButtons = false });
            var root = new Grid(); root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(38) }); root.RowDefinitions.Add(new RowDefinition()); root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.Children.Add(TitleBar());
            var body = new Grid(); body.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); body.ColumnDefinitions.Add(new ColumnDefinition());
            var nav = Nav(); body.Children.Add(nav);
            // the page: its own column, scrolled up and down only; never wider than the column, and at most a comfortable reading width
            var pageHost = new Grid();   // the page fills its column up to 1,120 px, starting at the left (a text-only page as wide as a page of cards)
            pageHost.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MaxWidth = 1120 }); pageHost.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            pageHost.Children.Add(page);
            var main = new DockPanel();
            var strip = ConsoleStrip(); DockPanel.SetDock(strip, Dock.Top); main.Children.Add(strip);
            if (!string.IsNullOrEmpty(o.SimulatedConsole)) { var sim = Note("Simulated console: no console is connected. Files on this PC stand in for one (a test and demonstration mode). Backups made this way are marked simulated and can never be restored to a real console.", "warn"); sim.Margin = new Thickness(32, 16, 32, 0); DockPanel.SetDock(sim, Dock.Top); main.Children.Add(sim); }
            var sv = scroller = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = pageHost, Padding = new Thickness(32, 24, 32, 28), Focusable = false };
            main.Children.Add(sv); Grid.SetColumn(main, 1); body.Children.Add(main);
            Grid.SetRow(body, 1); root.Children.Add(body);
            var foot = StatusStrip(); Grid.SetRow(foot, 2); root.Children.Add(foot);
            Content = root;
            WindowFrame.Attach(this, root);   // maximized: the content inside the monitor's work area
            SourceInitialized += (s, e) => WindowFrame.Prepare(this);   // the dark frame, checked, and an opaque surface (WB-UX-07)
            PreviewKeyDown += (s, e) => { if (e.Key == Key.Escape) { if (CloseTerm(true)) e.Handled = true; else OnEscape(e); } else if (e.Key == Key.F1) { OpenPageHelp(); e.Handled = true; } };
            HookHistoryKeys();   // Alt+Left / Alt+Right and a mouse's back and forward buttons (WorkbenchNav.cs)
            Closing += OnClosing;
            Enter("Dashboard"); ShowDashboard();
        }

        // ------------------------------------------------------------------ chrome and building blocks
        T Res<T>(string k) where T : class => (TryFindResource(k) as T);

        /// <summary>WB-UX-22: the title bar's own surface, a little lighter than the page under it, with a quiet line between them (fixed colors:
        /// never the Windows accent color)</summary>
        static readonly Brush TitleBarBg = Frozen(Color.FromRgb(0x20, 0x20, 0x26)), TitleBarLine = Frozen(Color.FromRgb(0x34, 0x34, 0x3C));

        UIElement TitleBar()
        {
            var bar = new Grid { Background = TitleBarBg };
            bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); bar.ColumnDefinitions.Add(new ColumnDefinition()); for (int i = 0; i < 3; i++) bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            // WB-UX-22: the program's own icon (workbench.ico, the wrench: the executable's and the taskbar's icon) beside the title, 16 x 16 like any
            // Windows title bar, from the icon file's frame for the display's scale so it stays sharp (chosen again when the scale changes)
            var icon = new Image { Width = 16, Height = 16, Stretch = Stretch.Uniform, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 1), SnapsToDevicePixels = true, Focusable = false };
            RenderOptions.SetBitmapScalingMode(icon, BitmapScalingMode.HighQuality); System.Windows.Automation.AutomationProperties.SetName(icon, "CCS Console Workbench");
            void IconFor() { double scale = 1; try { scale = VisualTreeHelper.GetDpi(this).DpiScaleX; } catch (Exception) { } icon.Source = TitleIcon(16 * scale) ?? opt.AppIcon; icon.Visibility = icon.Source != null ? Visibility.Visible : Visibility.Collapsed; }
            IconFor(); Loaded += (s, e) => IconFor(); DpiChanged += (s, e) => IconFor();
            bar.Children.Add(icon);
            var caption = new TextBlock { FontSize = 12.5, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(9, 0, 0, 1) };
            caption.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding(nameof(Title)) { Source = this });   // always the window's own title
            Grid.SetColumn(caption, 1); bar.Children.Add(caption);
            var min = new Button { Content = "", Style = Res<Style>("Caption"), Height = 37, ToolTip = "Minimize" }; System.Windows.Automation.AutomationProperties.SetName(min, "Minimize"); min.Click += (s, e) => WindowState = WindowState.Minimized; WindowChrome.SetIsHitTestVisibleInChrome(min, true); Grid.SetColumn(min, 2);
            var close = new Button { Content = "", Style = Res<Style>("Caption.Close"), Height = 37, ToolTip = "Close" }; System.Windows.Automation.AutomationProperties.SetName(close, "Close"); close.Click += (s, e) => Close(); WindowChrome.SetIsHitTestVisibleInChrome(close, true); Grid.SetColumn(close, 4);
            // maximize / restore between them: its symbol, name and tooltip follow the window's state however it changed (this button, a
            // double-click on the title bar, Snap, Win+Up/Down or the system menu)
            var max = new Button { Content = "\uE922", Style = Res<Style>("Caption"), Height = 37, ToolTip = "Maximize" }; System.Windows.Automation.AutomationProperties.SetName(max, "Maximize");
            max.Click += (s, e) => { if (WindowState == WindowState.Maximized) SystemCommands.RestoreWindow(this); else SystemCommands.MaximizeWindow(this); };
            WindowChrome.SetIsHitTestVisibleInChrome(max, true); Grid.SetColumn(max, 3);
            StateChanged += (s, e) => { bool m = WindowState == WindowState.Maximized; max.Content = m ? "\uE923" : "\uE922"; max.ToolTip = m ? "Restore" : "Maximize"; System.Windows.Automation.AutomationProperties.SetName(max, m ? "Restore" : "Maximize"); };
            bar.Children.Add(min); bar.Children.Add(max); bar.Children.Add(close);
            return new Border { Background = TitleBarBg, BorderBrush = TitleBarLine, BorderThickness = new Thickness(0, 0, 0, 1), Child = bar };
        }

        /// <summary>the frame of this library's workbench.ico (the wrench every Workbench window and the executable carry) at least px pixels
        /// wide, the smallest such (else the largest there is); null when it cannot be read</summary>
        static BitmapSource TitleIcon(double px)
        {
            try
            {
                var info = Application.GetResourceStream(new Uri("pack://application:,,,/CcsBackupUi;component/workbench.ico", UriKind.Absolute)); if (info == null) return null;
                using (var s = info.Stream)
                {
                    var d = new IconBitmapDecoder(s, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
                    var f = d.Frames.Where(x => x.PixelWidth >= px - 0.5).OrderBy(x => x.PixelWidth).ThenByDescending(x => x.Format.BitsPerPixel).FirstOrDefault() ?? d.Frames.OrderByDescending(x => x.PixelWidth).FirstOrDefault();
                    if (f != null && f.CanFreeze) f.Freeze();
                    return f;
                }
            }
            catch (Exception) { return null; }
        }

        TextBlock T(string t, double size = 13.5, bool bold = false, bool muted = false, Thickness? m = null)
        {
            var x = new TextBlock { Text = t, FontSize = size, FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal, TextWrapping = TextWrapping.Wrap, Margin = m ?? new Thickness(0, 0, 0, 6) };
            if (muted) x.Foreground = Res<Brush>("B.Muted");   // otherwise the theme's own text color (never a local null)
            return x;
        }
        Button B(string text, string style, Action click, bool enabled = true)
        {
            var b = new Button { Content = text, ContentTemplateSelector = new TextOnlyTemplate { Template = Res<DataTemplate>("Wb.ButtonText") }, Style = Res<Style>(style), IsEnabled = enabled, Margin = new Thickness(0, 0, 10, 10), MinWidth = 96, MinHeight = 34, HorizontalAlignment = HorizontalAlignment.Left };
            System.Windows.Automation.AutomationProperties.SetName(b, text);
            b.Click += (s, e) => click(); return b;
        }
        Border Card(UIElement child, double pad = 20) => new Border { Background = Res<Brush>("B.Surface"), BorderBrush = Res<Brush>("B.Line"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), Padding = new Thickness(pad), Margin = new Thickness(0, 0, 0, 16), Child = child };
        Border Note(string text, string kind)
        {
            if (CcsBackup.Engine.HakchiRequirement.Is(text)) return HakchiNote(text);
            var c = kind == "ok" ? "B.Ok" : kind == "bad" ? "B.Red3" : kind == "warn" ? "B.Warn" : "B.Line2";
            return new Border { BorderBrush = Res<Brush>(c), BorderThickness = new Thickness(3, 0, 0, 0), Background = Res<Brush>("B.Surface2"), CornerRadius = new CornerRadius(0, 6, 6, 0), Padding = new Thickness(14, 10, 14, 10), Margin = new Thickness(0, 8, 0, 10), Child = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, FontSize = 13, LineHeight = 19 } };
        }
        /// <summary>the refusal of a console whose Hakchi setup is not Hakchi2 CE 3.9.3: the same words wherever a task stops for it, and the
        /// official release page, opened in the default web browser (never inside Workbench, never run from here); what was found below</summary>
        Border HakchiNote(string problem)
        {
            var sp = new StackPanel();
            sp.Children.Add(T(CcsBackup.Engine.HakchiRequirement.Title, Ty.Body, true, true));
            sp.Children.Add(T(CcsBackup.Engine.HakchiRequirement.Body, Ty.Body, false, true, new Thickness(0, 4, 0, 0)));
            sp.Children.Add(T(CcsBackup.Engine.HakchiRequirement.Next, Ty.Body, false, true, new Thickness(0, 4, 0, 0)));
            sp.Children.Add(Buttons(B(CcsBackup.Engine.HakchiRequirement.Button, "Btn.Primary", () => Launch(CcsBackup.Engine.HakchiRequirement.Url))));
            sp.Children.Add(Details("Technical details", "What the console reports: " + CcsBackup.Engine.HakchiRequirement.DetectedOf(problem) + ".",
                "Workbench needs Hakchi2 CE 3.9.3's console components: hakchi " + CcsBackup.Engine.HakchiRequirement.ConsoleVersion + " with kernel " + CcsBackup.Engine.HakchiRequirement.Kernel + ".",
                "The button opens the official release page in your web browser: " + CcsBackup.Engine.HakchiRequirement.Url,
                "After you update the console with Hakchi2 CE, start this task again: Workbench checks the console again before anything is written. Hakchi2 CE's own update is not covered by Workbench's backup and checks."));
            return new Border { BorderBrush = Res<Brush>("B.Warn"), BorderThickness = new Thickness(3, 0, 0, 0), Background = Res<Brush>("B.Surface2"), CornerRadius = new CornerRadius(0, 6, 6, 0), Padding = new Thickness(14, 10, 14, 10), Margin = new Thickness(0, 8, 0, 0), Child = sp };
        }
        /// <summary>on a review of a change to a console: "Returning to Nintendo's original software is unavailable", when this console's
        /// backups hold no original Nintendo material (the change itself can still go ahead with a complete current backup); null otherwise</summary>
        UIElement OriginalUnavailableNote(string consoleTag)
        {
            try { if (consoleTag == null || CcsBackup.Engine.Admission.HasRecordedOriginal(settings, consoleTag)) return null; } catch (Exception) { return null; }
            var sp = new StackPanel();
            sp.Children.Add(T(CcsBackup.Engine.Admission.OriginalUnavailableTitle, Ty.Body, true, true));
            sp.Children.Add(T(CcsBackup.Engine.Admission.OriginalUnavailableText, Ty.Body, false, true, new Thickness(0, 4, 0, 0)));
            return new Border { BorderBrush = Res<Brush>("B.Warn"), BorderThickness = new Thickness(3, 0, 0, 0), Background = Res<Brush>("B.Surface2"), CornerRadius = new CornerRadius(0, 6, 6, 0), Padding = new Thickness(14, 10, 14, 10), Margin = new Thickness(0, 8, 0, 0), Child = sp };
        }
        WrapPanel Buttons(params UIElement[] b) { var p = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) }; foreach (var x in b) if (x != null) p.Children.Add(x); return p; }
        /// <summary>shows a page: its tag names it for Escape and the status strip; a page with unsaved changes sets leaveCheck after this. A
        /// page of the console task while another page is in front is kept for Return to Task instead, and the page in front stays
        /// (WorkbenchNav.cs).</summary>
        void Show(FrameworkElement e, string tag)
        {
            e.Tag = tag;
            if (KeepTaskPage(e)) return;
            if (page.Content != e) { RememberShown(); NoteLeavingTask(); }
            CloseTerm(false); NewTermScope(e); page.Content = e; scroller?.ScrollToTop(); leaveCheck = null; pageUnsaved = null; settingsPending = null; settingsRefresh = null;
            if (tag != null && TagSection.TryGetValue(tag, out var own) && own != section) Enter(own);   // the side column follows the page
            if (ReferenceEquals(e, taskPage)) { taskSection = section; taskUnseen = false; }
            NoteShown(e, tag);   // its place in Back and Forward, when it is one
            Dispatcher.BeginInvoke(new Action(UpdateChrome), System.Windows.Threading.DispatcherPriority.Background);   // after the caller has started its worker
        }

        /// <summary>leaves a console session before its result (a review's Cancel, a check-up's Done): the worker quits without writing, and
        /// the section's first page (or the one named) opens</summary>
        void Leave(string name = null)
        {
            if (worker != null && worker.Running) { quitting = true; worker.Cancel(); worker.Send(new JObj { { "cmd", "quit" } }); }
            if (browsing?.Live == true) { browsing.Dispose(); browsing = null; }
            DropLiveStash();
            Go(name ?? section);
        }
        static string Size(long b) => Units.Gb(b);
        /// <summary>why a backup file is larger than the console it came from (beside every place its size could be read as the console's)</summary>
        internal const string BackupSizeNote = "The backup can be larger than your console's storage. It keeps both a full storage copy and separate parts used for restoring and browsing.";
        /// <summary>"1 file", "3 files", "1,873 files"</summary>
        static string Count(long n, string one, string many = null) => n.ToString("N0", En) + " " + (n == 1 ? one : many ?? one + "s");
        static string Capital(string s) => string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s.Substring(1);

        // ------------------------------------------------------------------ Console Backup: the two paths and the list of backups
        void ShowBackupHome()
        {
            settings = Reload();
            var sp = new StackPanel();
            sp.Children.Add(PageHead("Console Backup", "Make a complete copy of your " + Con + "'s built-in storage on this PC, and see every backup you have made."));
            sp.Children.Add(CardGrid(300, 2,
                ActionCard("Back Up Console", "Make a complete, checked copy of the console's built-in storage: its games, saves, settings and everything else on it, including other programs' files. A backup only reads the console; nothing on it changes.", "Btn.Primary", () => ShowBackup()),
                ActionCard("Restore a Backup", "Put a backup back on the console it came from. You review exactly what changes first, and the console as it is now is backed up before anything is written.", "Btn", () => ShowRestorePick())));
            sp.Children.Add(Group("Your Backups"));
            List<HistoryEntry> list;
            try { list = History.List(settings); }
            catch (Exception ex)
            {
                list = new List<HistoryEntry>();
                sp.Children.Add(Note("Your backup location could not be read: " + ex.Message + " If it is on a drive that is not connected, connect it and try again, or choose another location in Settings.", "bad"));
                sp.Children.Add(Buttons(B("Try Again", "Btn", () => ShowBackupHome()), B("Backup Locations", "Btn.Ghost", () => Go("Settings"))));
            }
            if (list.Count == 0) { var c = new StackPanel(); c.Children.Add(T("No backups yet. Back Up Console saves the first one in your backup location:", Ty.Body, false, true)); c.Children.Add(PathText(settings.PrimaryFolder)); sp.Children.Add(Card(c)); }
            else
            {
                var lb = new ListBox { Background = Brushes.Transparent, BorderThickness = new Thickness(0), MaxHeight = 380 };
                ScrollViewer.SetHorizontalScrollBarVisibility(lb, ScrollBarVisibility.Disabled);
                var rows = ListedRows();
                foreach (var e in list) lb.Items.Add(new ListBoxItem { Content = HistoryRow(e, rows), Tag = e, Padding = new Thickness(0), Margin = new Thickness(0, 0, 0, 6), HorizontalContentAlignment = HorizontalAlignment.Stretch });
                var acts = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0) };
                lb.SelectionChanged += (s, ev) =>
                {
                    acts.Children.Clear(); var e = (lb.SelectedItem as ListBoxItem)?.Tag as HistoryEntry; if (e == null) return;
                    var file = e.PrimaryPath ?? e.ArchivePath;
                    if (file != null && File.Exists(file)) acts.Children.Add(B("Check the File", "Btn", () => ShowVerify(file, e)));
                    acts.Children.Add(B("Open Folder", "Btn.Ghost", () => OpenFolder(file ?? e.PartialPath)));
                    if (file != null && File.Exists(file) && e.Status != BackupStatus.Damaged && e.Status != BackupStatus.Incomplete) acts.Children.Add(B("Restore This Backup…", "Btn.Ghost", () => ShowRestoreReview(file)));
                };
                sp.Children.Add(lb); sp.Children.Add(acts);
                sp.Children.Add(T(BackupSizeNote, Ty.Fine, false, true, new Thickness(0, 6, 0, 0)));
            }
            Show(sp, "backup-home");
        }

        /// <summary>a card for one thing to do: its name, what it does, and its button at the bottom (in line with its neighbors')</summary>
        UIElement ActionCard(string title, string text, string style, Action go, string button = null)
        {
            var g = new Grid(); g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); g.RowDefinitions.Add(new RowDefinition()); g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            g.Children.Add(new TextBlock { Text = title, Style = Res<Style>("T.Title"), FontSize = 16.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) });
            var t = T(text, 13, false, true, new Thickness(0, 0, 0, 16)); t.LineHeight = 19; Grid.SetRow(t, 1); g.Children.Add(t);
            var b = B(button ?? title, style, go); b.Margin = new Thickness(0); b.VerticalAlignment = VerticalAlignment.Bottom; Grid.SetRow(b, 2); g.Children.Add(b);
            var c = Card(g, 20); c.Margin = new Thickness(0); return c;
        }

        /// <summary>the backup list's rows by file (the same status words as the Dashboard and the Backup Browser); empty when the list cannot
        /// be read (each row then says what its record says)</summary>
        Dictionary<string, CcsBackup.Browse.BackupListEntry> ListedRows()
        {
            var d = new Dictionary<string, CcsBackup.Browse.BackupListEntry>(StringComparer.OrdinalIgnoreCase);
            try { foreach (var r in CcsBackup.Browse.BackupList.List(settings)) if (r.Path != null && !d.ContainsKey(r.Path)) d[r.Path] = r; } catch (Exception) { }
            return d;
        }

        UIElement HistoryRow(HistoryEntry e, Dictionary<string, CcsBackup.Browse.BackupListEntry> rows = null)
        {
            var g = new Grid(); g.ColumnDefinitions.Add(new ColumnDefinition()); g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var left = new StackPanel { Margin = new Thickness(0, 0, 12, 0) };
            left.Children.Add(T(e.When, 14, true, false, new Thickness(0, 0, 0, 2)));
            // the console and size only: what the console's startup software was is shown when the backup is reviewed or opened
            var bits = new List<string>(); if (e.ConsoleTag != null) bits.Add((e.Simulated ? "Simulated console " : "Console ") + e.ConsoleTag); if (e.Bytes > 0) bits.Add(Size(e.Bytes));
            left.Children.Add(T(string.Join("  ·  ", bits), 12.5, false, true, new Thickness(0)));
            // the Dashboard's and the Backup Browser's words for the same states (BackupList: Verified only for a full check recorded for the
            // file as it is now; a file whose description reads as finished, with no such check recorded here, is Not checked)
            CcsBackup.Browse.BackupListEntry row = null; var file = e.PrimaryPath ?? e.ArchivePath;
            if (file != null) (rows ?? ListedRows()).TryGetValue(file, out row);
            string said, kind, line;
            if (row != null)
            {
                (said, kind) = RowState(row); line = row.CheckText;
                if (row.Status == CcsBackup.Browse.BrowseStatus.Complete && row.Check == CcsBackup.Browse.CheckState.Verified && e.TwoCopies) said += ", with an additional copy";
            }
            else
            {
                bool verified = e.Status == BackupStatus.Verified || e.Status == BackupStatus.NeedsVerification && e.CheckedWhenMade;
                kind = verified ? "B.Ok" : e.Status == BackupStatus.NeedsVerification || e.Status == BackupStatus.Changed ? "B.Warn" : "B.Red3";
                said = verified ? (e.TwoCopies ? "Verified, with an additional copy" : "Verified") : e.Status == BackupStatus.NeedsVerification ? "Not checked" : e.StatusText;
                line = verified && e.VerifiedUtc != null ? "Full check passed when it was made: " + e.VerifiedUtc.Value.ToLocalTime().ToString("MMMM d, yyyy 'at' h:mm tt", En) + "."
                     : e.Status == BackupStatus.NeedsVerification ? "No full check of this file is recorded here. Check the File runs one." : null;
            }
            if (line != null && e.Status != BackupStatus.Damaged && e.Status != BackupStatus.Incomplete) left.Children.Add(T(line, 12, false, true, new Thickness(0, 2, 0, 0)));
            if (e.Status == BackupStatus.Verified && e.TwoCopies && e.SameDisk) left.Children.Add(T("Both copies are on the same drive: that protects against a damaged file, not against losing the drive.", 12, false, true, new Thickness(0, 2, 0, 0)));
            if (e.Problem != null && e.Status != BackupStatus.Verified) left.Children.Add(T(e.Problem.Length > 200 ? e.Problem.Substring(0, 200) + "…" : e.Problem, 12, false, true, new Thickness(0, 2, 0, 0)));
            g.Children.Add(left);
            var pill = new Border { CornerRadius = new CornerRadius(10), BorderBrush = Res<Brush>(kind), BorderThickness = new Thickness(1), Padding = new Thickness(10, 2, 10, 3), VerticalAlignment = VerticalAlignment.Center, Child = new TextBlock { Text = said, FontSize = 12, Foreground = Res<Brush>(kind) } };
            Grid.SetColumn(pill, 1); g.Children.Add(pill);
            return new Border { Background = Res<Brush>("B.Surface"), BorderBrush = Res<Brush>("B.Line"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(14, 10, 14, 10), Child = g };
        }

        static void OpenFolder(string file)
        {
            try { if (file != null && File.Exists(file)) Process.Start("explorer.exe", "/select,\"" + file + "\""); else if (file != null) Process.Start("explorer.exe", "\"" + Path.GetDirectoryName(file) + "\""); } catch { }
        }

        // ------------------------------------------------------------------ Back Up Console

        void ShowBackup()
        {
            settings = Reload();
            var sp = PageHead("Back Up Console", "Make a complete, checked copy of your " + Con + "'s built-in storage on this PC. A backup only reads the console; nothing on it changes.");
            var what = new StackPanel();
            what.Children.Add(T("What is backed up", Ty.Heading, true));
            what.Children.Add(T("Everything on the console's built-in storage, exactly as stored: its startup software, Nintendo's system and games, every save and setting, and anything other programs added (games, emulators, menus, scripts). If the console's original Nintendo startup software is still on it, a separate copy of that is saved too.", Ty.Text));
            what.Children.Add(T("Games and saves on a USB drive or SD card connected to the console are not included.", Ty.Text, false, true));
            what.Children.Add(T("Where it is saved", Ty.Heading, true, false, new Thickness(0, 10, 0, 6)));
            // 1.0 GB: the NES Classic Mini's backup
            what.Children.Add(T(!profile.ConsoleTasks ? "One file in your backup location:" : profile.ArchiveBytes > 0 ? "One file of about " + Size(profile.ArchiveBytes) + " in your backup location:" : "One file of about 1.0 GB in your backup location:", Ty.Text));
            // WB-UX-11: the location, with Edit in Settings beside it (Settings at Backup locations; its Back returns here with the location as it is then)
            var where = new Grid(); where.ColumnDefinitions.Add(new ColumnDefinition()); where.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var path = PathText(settings.PrimaryFolder); path.Margin = new Thickness(0, 0, 18, 4); path.VerticalAlignment = VerticalAlignment.Center; where.Children.Add(path);
            var edit = new Button { Content = "Edit in Settings", Style = Res<Style>("Btn.Link"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 0, 4), ToolTip = "Choose where new backups are saved" };
            System.Windows.Automation.AutomationProperties.SetName(edit, "Edit in Settings"); System.Windows.Automation.AutomationProperties.SetHelpText(edit, "Opens Settings at Backup locations");
            edit.Click += (s, e) => EditBackupLocation(); Grid.SetColumn(edit, 1); where.Children.Add(edit);
            what.Children.Add(where);
            // the file is larger than the console's storage: why, with the console's real capacity (a file's size is never its capacity)
            if (profile.ConsoleTasks) what.Children.Add(T(BackupSizeNote + (profile.StorageBytes > 0 ? " The " + profile.Short + "'s built-in storage is " + Size(profile.StorageBytes) + " in all, used and free space together; the backup file is about 1.0 GB, and an additional copy needs about as much again on its drive." : ""), Ty.Small, false, true, new Thickness(0, 2, 0, 10)));
            if (string.IsNullOrEmpty(settings.ArchiveFolder)) what.Children.Add(T("Additional copy not configured.", Ty.Text, false, true));
            else { what.Children.Add(T("An additional copy goes to:", Ty.Text)); what.Children.Add(PathText(settings.ArchiveFolder)); }
            if (!string.IsNullOrEmpty(settings.ArchiveFolder) && Durable.VolumeId(settings.ArchiveFolder) == Durable.VolumeId(settings.PrimaryFolder))
                what.Children.Add(Note("The additional copy is on the same drive. That protects against a damaged file, but not against losing the drive or the PC. You can choose another drive in Settings.", "warn"));
            what.Children.Add(Details(TechNes, "The file is a ZIP: the startup software and each part of the built-in storage as images (restore/), the original Nintendo kernel when it is found (recovery/), the raw NAND image, bootloaders and storage table kept as evidence and never written to a console (reference/), the list of files on the data storage (inventory/), and a manifest with every part's size and checksums.",
                "While it is made, the file is written under a temporary name; it is read back in full and checked against what the console sent before it gets its final name. An unfinished file is kept as Incomplete and is never used as a backup."));
            sp.Children.Add(Card(what));
            if (string.IsNullOrEmpty(opt.SimulatedConsole) || !profile.ConsoleTasks) sp.Children.Add(ConnectSteps("A backup only reads the console's storage.", "backup"));
            sp.Children.Add(Buttons(B(string.IsNullOrEmpty(opt.SimulatedConsole) ? "Start Backup" : "Start Backup (Simulated Console)", "Btn.Primary", () => StartBackup()), B("Back", "Btn.Ghost", BackToSection)));
            Show(sp, "backup");
        }

        List<string> Args()
        {
            var a = new List<string>(opt.WorkerArgs);
            if (settings.InMemory || PrefsUnsaved || string.IsNullOrEmpty(settings.Path)) { a.Add("--primary-folder"); a.Add(settings.PrimaryFolder); if (!string.IsNullOrEmpty(settings.ArchiveFolder)) { a.Add("--archive-folder"); a.Add(settings.ArchiveFolder); } }
            else { a.Add("--settings"); a.Add(settings.Path); }
            if (opt.Portable) a.Add("--portable");
            return a;
        }

        JObj ConsoleReq(string cmd) { var r = new JObj { { "cmd", cmd } }; if (!string.IsNullOrEmpty(opt.SimulatedConsole)) { r.Add("console", "simulated"); r.Add("folder", opt.SimulatedConsole); } else r.Add("console", "real"); return r; }

        ProgressView progress;

        void StartBackup()
        {
            if (!FamilyHasTasks("backup") || !BoundaryOpen("backup") || !OwnTaskAllows("Back Up Console")) return;
            if (!driverReady && string.IsNullOrEmpty(opt.SimulatedConsole)) { WithDriver(() => StartBackup()); return; }
            if (ConsoleLock.IsBusy()) { Show(Message("Another console task is running", "The console is already in use by this window, another CCS Console Workbench window or Classic Cart Studio. Wait for that task to finish, then try again.", "bad"), "msg"); return; }
            progress = new ProgressView(this, "Backing Up the Console", "Keep the USB cable connected. Cancel stops at once: nothing on the console changes, and the unfinished file is kept and marked Incomplete.", "Cancel", () => { worker?.Cancel(); progress.Say("Canceling..."); });
            Show(progress.Panel, "progress");
            NewSession("backup");
            var req = ConsoleReq("backup"); req.Add("name", "ask");   // the backup's name, asked once the console is identified (WorkbenchNav.cs)
            worker.Send(req);
        }

        // ------------------------------------------------------------------ worker events
        Dictionary<string, object> lastPlan;
        void OnWorkerEvent(Dictionary<string, object> ev)
        {
            if (quitting) { if (ev.S("event") == "result") gotResult = true; return; }
            switch (ev.S("event"))
            {
                case "progress": progress?.Update(ev.S("text"), ev.L("done", 0), ev.L("total", 0)); if (ev.S("phase") == "write" || ev.S("phase") == "prepare") writing = true; break;
                case "console": break;   // the console's identity, before anything is read: the console strip shows it (NoteConsole)
                case "backup-name": ShowBackupName(ev); break;   // Back Up Console's name, asked before anything is read (WorkbenchNav.cs)
                case "plan": lastPlan = ev.O("plan"); ShowPlan(lastPlan); break;
                case "prep-plan": ShowPrepPlan(ev.O("plan")); break;
                case "return-plan": ShowReturnPlan(ev.O("plan")); break;
                case "result": gotResult = true; writing = false; if (workerOp == "backup") BackupResult(ev); else if (workerOp == "prepare") PrepResult(ev); else if (workerOp == "return") ReturnResult(ev); else if (workerOp == "rescue") RescueResult(ev); else if (workerOp == "browse") { if (browsing?.Live == true) { browsing.Dispose(); browsing = null; } DropLiveStash(); ShowCheckup(ev, ended: true); } else if (workerOp == "compose") ComposeResult(ev); else if (workerOp == "home-design") DesignResult(ev); else if (workerOp == "method" || workerOp == "firmware") MethodOrFirmwareResult(workerOp, ev); else if (workerOp == "test-boot") TestBootResult(ev); else if (workerOp == "community") CommunityResult(ev); else if (workerOp == "sega-pictures") SegaResult(ev); else if (workerOp == "files") { worker?.Send(new JObj { { "cmd", "quit" } }); Go(filesScope == "dev" ? "Developer Mode" : "Console Browser"); } else RestoreResult(ev); break;
                case "finished": Show(Message("The console is restarting", "It starts into its normal HOME in a moment. You can unplug the USB cable once the TV shows the HOME.", "ok"), "msg"); worker?.Send(new JObj { { "cmd", "quit" } }); break;
                case "error": if (workerOp == "files") ShowFilesSession(ev.S("text"), "bad"); else Show(Message("The console task stopped with a problem", ev.S("text") ?? "", "bad", ev.S("code")), "msg"); break;
                default: OnWorkbenchEvent(ev); break;
            }
        }

        bool gotResult;
        void OnWorkerExit(int code)
        {
            var w = worker; worker = null; var wasWriting = writing; var left = quitting; writing = false; quitting = false; w?.Dispose();
            if (taskPage != null && progressViews.TryGetValue(taskPage, out var pv)) pv.Stop();   // the task's clock stops with its session
            DropLiveStash();   // the console's files read live end with the session
            UpdateChrome();
            if (closeWhenDone) { closeWhenDone = false; if (!inClosing) Close(); return; }   // inside a close's own question: that close goes on
            if (left) return;
            if (!gotResult && taskPage?.Tag as string == "progress")
                Show(Message(workerOp == "backup" ? "The backup ended unexpectedly" : "The console operation ended unexpectedly",
                    workerOp == "backup" ? "The program doing the backup stopped before it finished. Nothing on the console was changed. The unfinished file is kept and listed as Incomplete; it is never used as a backup."
                                         : wasWriting ? "The program writing to the console stopped. The console may be partly changed and its state is not verified. Your backups are unchanged. Make a new backup to see the console's state before anything else."
                                                      : "The program stopped before anything was written to the console. Your backups are unchanged.", "bad"), "msg");
        }

        void BackupResult(Dictionary<string, object> r)
        {
            var st = r.S("state"); var sp = new StackPanel();
            bool ok = st == "complete" || st == "complete-one-copy";
            if (ok)
            {
                sp.Children.Add(PageHead(r.B("two_copies") ? "Backup complete: saved and checked, with an additional copy" : "Backup complete: saved and checked", "Your " + Con + "'s built-in storage is saved on this PC. Nothing on the console was changed."));
                var c = new StackPanel();
                c.Children.Add(T((seenNow != null && seenNow.Tag == r.S("console_tag") && FriendlyName(seenNow) is string fn ? fn + "  ·  " : "") + "Console " + r.S("console_tag") + "  ·  " + (r.S("state_text") ?? ""), Ty.Body));
                c.Children.Add(T("Made " + DateTime.Now.ToString("dddd, MMMM d, yyyy 'at' h:mm tt", En) + "  ·  " + Size(r.L("bytes", 0)), Ty.Body));
                c.Children.Add(T("Saved as:", Ty.Small, false, true, new Thickness(0, 4, 0, 2))); c.Children.Add(PathText(r.S("primary")));
                if (r.S("archive") != null) { c.Children.Add(T("Additional copy:", Ty.Small, false, true, new Thickness(0, 2, 0, 2))); c.Children.Add(PathText(r.S("archive"))); }
                if (r.B("two_copies") && r.B("same_disk")) c.Children.Add(Note("Both copies are on the same drive: that protects against a damaged file, not against losing the drive.", "warn"));
                if (!r.B("two_copies") && r.S("copy_problem") != null) c.Children.Add(Note("The additional copy was not made: " + r.S("copy_problem"), "warn"));
                var inv = r.O("inventory");
                if (inv != null && inv.B("readable")) { var by = inv.O("files_by_owner"); c.Children.Add(T("Data storage: " + Count(inv.L("files"), "file") + " (" + by.L("nintendo").ToString("N0", En) + " Nintendo's, " + by.L("hakchi").ToString("N0", En) + " hakchi's, " + by.L("classic_cart_studio").ToString("N0", En) + " Classic Cart Studio's, " + by.L("other").ToString("N0", En) + " from other programs or unknown) and " + Count(inv.L("folders"), "folder") + ", all included.", 13)); }
                foreach (var x in r.A("external") ?? new List<object>()) c.Children.Add(T(x as string, 12.5, false, true));
                if (!r.B("recovery_baseline")) c.Children.Add(Note("The original Nintendo kernel was not found on this console, so this backup cannot serve as its original recovery baseline. It can still be restored to this console.", "warn"));
                c.Children.Add(Note("Checked means the file was read back in full and every part matches what the console sent. Restoring it to this console is a separate step.", "info"));
                sp.Children.Add(Card(c));
                sp.Children.Add(T("The console is still running Workbench's program from its memory. Restart the Console starts it normally; Done leaves it on that screen until you switch it off.", Ty.Small, false, true));
                sp.Children.Add(Buttons(B("Open Backup Folder", "Btn", () => OpenFolder(r.S("primary"))), B("Restart the Console", "Btn.Ghost", () => worker?.Send(new JObj { { "cmd", "finish" } })), B("Done", "Btn.Ghost", () => { worker?.Send(new JObj { { "cmd", "quit" } }); BackToSection(); })));
            }
            else
            {
                var title = st == "canceled" ? "Backup canceled" : st == "unavailable" ? "Not available in this version" : st == "busy" ? "Another console operation is running" : st == "refused" ? "The backup could not start" : "The backup stopped";
                sp.Children.Add(PageTitle(title));
                sp.Children.Add(Note(r.S("problem") ?? "", st == "canceled" ? "warn" : "bad"));
                if (st != "unavailable" && st != "busy") sp.Children.Add(T("Nothing on the console was changed. " + (r.S("partial") != null ? "The unfinished file is kept and listed as Incomplete; it is never used as a backup." : ""), 13));
                sp.Children.Add(Buttons(B("Done", "Btn", () => { worker?.Send(new JObj { { "cmd", "quit" } }); BackToSection(); })));
            }
            Show(sp, "result");
        }

        // ------------------------------------------------------------------ Restore a Backup
        void ShowRestorePick()
        {
            var sp = PageHead("Restore a Backup", "Choose the backup to put back on your " + Con + ". A backup is restored only to the console it came from, and only if that console's storage is still laid out the same way. Restoring changes the console: you review every change first.");
            var list = History.List(Reload()).Where(e => (e.Status == BackupStatus.Verified || e.Status == BackupStatus.NeedsVerification) && (e.PrimaryPath ?? e.ArchivePath) != null).ToList();
            var lb = new ListBox { Background = Brushes.Transparent, BorderThickness = new Thickness(0), MaxHeight = 340 }; ScrollViewer.SetHorizontalScrollBarVisibility(lb, ScrollBarVisibility.Disabled);
            var rows = ListedRows();
            foreach (var e in list) lb.Items.Add(new ListBoxItem { Content = HistoryRow(e, rows), Tag = e.PrimaryPath ?? e.ArchivePath, Margin = new Thickness(0, 0, 0, 6), HorizontalContentAlignment = HorizontalAlignment.Stretch });
            if (list.Count == 0) sp.Children.Add(Card(T("No complete backups in your backup locations. Choose Browse for a Backup File to use one saved somewhere else.", Ty.Body, false, true))); else { sp.Children.Add(lb); sp.Children.Add(T(BackupSizeNote, Ty.Fine, false, true, new Thickness(0, 0, 0, 8))); }
            var next = B("Review This Backup", "Btn.Primary", () => { if (lb.SelectedItem is ListBoxItem it) ShowRestoreReview(it.Tag as string); }, false);
            lb.SelectionChanged += (s, e) => next.IsEnabled = lb.SelectedItem != null;
            sp.Children.Add(Buttons(next, B("Browse for a Backup File…", "Btn", () => { var chosenFile = CcsFileDialog.OpenFile(this, "Choose a console backup", null, FileKinds.ConsoleBackupFilter, "console-backup"); if (chosenFile != null) ShowRestoreReview(chosenFile); }), B("Back", "Btn.Ghost", BackToSection)));
            Show(sp, "restore-pick");
        }

        void ShowRestoreReview(string file)
        {
            var pv = new ProgressView(this, "Checking the Backup", "The whole file is read and every part compared with its checksums. Nothing is written anywhere.", "Cancel", null);
            Show(pv.Panel, "progress");
            Task.Run(() => Verifier.VerifyFile(file, null, null, (d, t) => Dispatcher.BeginInvoke(new Action(() => pv.Update("Checking the backup file...", d, t))))).ContinueWith(t =>
            {
                if (page.Content != pv.Panel) { UpdateStatus("The backup's check for Restore a Backup finished after you left that page. Choose the backup again to review it."); return; }
                if (t.IsFaulted) { Show(Message("The backup could not be checked", "The file could not be read to the end. Nothing was written anywhere.", "bad", t.Exception?.GetBaseException().Message), "msg"); return; }
                var v = t.Result; var sp = new StackPanel();
                sp.Children.Add(PageHead("Restore a Backup", "Put this backup back on the console it came from. Restoring changes the console; nothing is written until you confirm what changes."));
                if (!v.Ok || !v.RestoreReady) { sp.Children.Add(Note("This file cannot be restored: " + (v.Problems.FirstOrDefault() ?? "it is not a complete console backup."), "bad")); sp.Children.Add(Details("Technical details", v.Problems.Skip(1).Take(8).Cast<object>().ToArray())); sp.Children.Add(Buttons(B("Back", "Btn", () => ShowRestorePick()))); Show(sp, "restore-review"); return; }
                var m = v.Manifest; var c = new StackPanel();
                c.Children.Add(T("Backup made " + When(m.S("completed_utc")), Ty.Heading, true));
                c.Children.Add(T((v.Simulated ? "Simulated console " : "Console ") + v.ConsoleTag, Ty.Body));
                if (m.O("state")?.S("text") is string stateText) c.Children.Add(T("When it was made, the console had: " + stateText, Ty.Small, false, true));
                c.Children.Add(T("Includes the whole built-in storage of that console: Nintendo's system, games and saves, and everything else on it at that time, including other programs' files. Games and saves on a USB drive or SD card were not part of it.", Ty.Text));
                if (v.Simulated) c.Children.Add(Note("This backup was made from a simulated console.", "warn"));
                c.Children.Add(T("Checked just now: the file is intact (every part matches its checksum).", Ty.Small, false, true));
                c.Children.Add(Details("Technical details", "File: " + file, "SHA-256 of the file: " + v.ArchiveSha256, "Backup ID: " + m.S("backup_id"), "Size: " + Size(v.Bytes)));
                sp.Children.Add(Card(c));
                var w = new StackPanel();
                w.Children.Add(T("What happens next", 15, true, false, new Thickness(0, 0, 0, 10)));
                w.Children.Add(Numbered("You connect the console in its recovery mode (as for a backup).", "Classic Cart Studio checks it is the same console with the same storage layout.", "It makes a complete backup of the console as it is now, so nothing is lost if you change your mind.", "It shows exactly what would change. Nothing is written until you confirm."));
                sp.Children.Add(Card(w));
                if (string.IsNullOrEmpty(opt.SimulatedConsole) || !profile.ConsoleTasks) sp.Children.Add(ConnectSteps("Nothing is written until you confirm what changes.", "restore-prepare"));
                // a partial restore (Backup Repair, D6): only the chosen storage parts; the startup software and the system part go together
                var parts = new StackPanel(); var picks = new List<(CheckBox b, string t)>();
                parts.Children.Add(T("Restore only these parts (the others stay as they are now):", 13.5, true));
                foreach (var t0 in v.RestoreTargets) { var cb = new CheckBox { Content = Units.TargetLabel(t0, (m.O("layout")?.A("partitions") ?? new List<object>()).OfType<Dictionary<string, object>>().Select(x => new CcsBackup.Device.PartitionEntry { Device = x.S("device"), Label = x.S("label"), Blocks512 = x.L("bytes") / 512 }).ToList()), Margin = new Thickness(0, 2, 0, 2) }; parts.Children.Add(cb); picks.Add((cb, t0)); }
                var partsCard = Card(parts, 18); partsCard.Visibility = Visibility.Collapsed;   // shown (with its frame) only when chosen
                sp.Children.Add(partsCard);
                sp.Children.Add(Buttons(B("Connect and Prepare", "Btn.Primary", () => StartRestorePrepare(file, partsCard.Visibility == Visibility.Visible ? picks.Where(x => x.b.IsChecked == true).Select(x => x.t).ToList() : null)),
                                        B("Only Some Parts…", "Btn.Ghost", () => partsCard.Visibility = partsCard.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible), B("Back", "Btn.Ghost", () => ShowRestorePick())));
                Show(sp, "restore-review");
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        static string When(string utc) => DateTime.TryParse(utc, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var t) ? t.ToLocalTime().ToString("dddd, MMMM d, yyyy 'at' h:mm tt", En) : "at an unknown time";

        void StartRestorePrepare(string file, List<string> only = null)
        {
            if (!FamilyHasTasks("restore") || !BoundaryOpen("restore-prepare") || !OwnTaskAllows("Restore a Backup")) return;
            if (!driverReady && string.IsNullOrEmpty(opt.SimulatedConsole)) { WithDriver(() => StartRestorePrepare(file, only)); return; }
            if (ConsoleLock.IsBusy()) { Show(Message("Another console operation is running", "Wait for it to finish, then try again.", "bad"), "msg"); return; }
            progress = new ProgressView(this, "Preparing the Restore", "Checking the console and backing it up as it is now. Nothing is written to the console in this step. Cancel stops at once.", "Cancel", () => { worker?.Cancel(); progress.Say("Canceling..."); });
            Show(progress.Panel, "progress");
            NewSession("restore");
            var req = ConsoleReq("restore-prepare"); req.Add("backup", file); if (only != null) req.Add("only", only.Cast<object>().ToList()); worker.Send(req);
        }

        void ShowPlan(Dictionary<string, object> p)
        {
            var sp = new StackPanel();
            sp.Children.Add(PageHead("Review the Restore", "What restoring this backup would change on your " + Con + ". Nothing has been written yet."));
            var refusals = (p.A("refusals") ?? new List<object>()).Cast<string>().ToList();
            var steps = (p.A("steps") ?? new List<object>()).OfType<Dictionary<string, object>>().ToList();
            var writes = steps.Where(s => s.S("action") == "write").ToList();
            var c = new StackPanel();
            c.Children.Add(T("Return console " + p.S("console_tag") + " to its state of " + When(p.S("backup_when")) + ".", 15, true, false, new Thickness(0, 0, 0, 8)));
            c.Children.Add(KeyValues(("In the backup", p.S("backup_state")), ("Now", p.S("current_state")), ("Backed up first", Path.GetFileName(p.S("protective_backup") ?? ""))));
            sp.Children.Add(Card(c));
            if (refusals.Count > 0) { foreach (var r in refusals) sp.Children.Add(Note(r, "bad")); sp.Children.Add(T("Nothing was written. The console is unchanged.", Ty.Text)); sp.Children.Add(Buttons(B("Done", "Btn", () => Leave()))); Show(sp, "plan"); return; }
            var d = new StackPanel();
            d.Children.Add(T("What changes", 15, true, false, new Thickness(0, 0, 0, 8)));
            d.Children.Add(ChangeTable(steps.Select(s => (s.S("action") == "write" ? "Write" : s.S("action") == "skipped" ? "Not chosen" : "Same", s.S("label"), Size(s.L("size"))))));
            long added = p.L("files_added_since", 0), removed = p.L("files_removed_since", 0), changed = p.L("files_changed_since", 0);
            d.Children.Add(T(Count(added, "file") + " added to the console since the backup will be removed, " + Count(removed, "file") + " removed since then will come back, and " + Count(changed, "file") + " changed since then will go back to how they were.", 13.5, true, false, new Thickness(0, 8, 0, 6)));
            if (p.A("added_sample")?.Count > 0) d.Children.Add(T("Some of the files that will be removed:", Ty.Small, false, true, new Thickness(0, 0, 0, 4)));
            var samples = (p.A("added_sample") ?? new List<object>()).Take(8).Select(x => ("Removed", x as string, (string)null)).ToList();
            if (samples.Count > 0) d.Children.Add(ChangeTable(samples));
            foreach (var x in (p.A("notes") ?? new List<object>())) d.Children.Add(Note(x as string, "info"));
            sp.Children.Add(Card(d));
            var risk = new StackPanel();
            risk.Children.Add(T("Before you confirm", 14, true));
            risk.Children.Add(T("Writing changes the console's storage. Keep the USB cable connected and the PC on until it finishes. If it is interrupted, the console may be left partly restored: your backup and the backup made just now stay unchanged, and nothing is retried automatically.", 13));
            var cb = new CheckBox { Content = new TextBlock { Text = "I understand: this replaces what is on the console now, including everything added since the backup.", TextWrapping = TextWrapping.Wrap }, Margin = new Thickness(0, 8, 0, 0), Foreground = Res<Brush>("B.Ink") };
            risk.Children.Add(cb);
            sp.Children.Add(Card(risk));
            var go = B("Restore Now", "Btn.Primary", () =>
            {
                worker?.Send(new JObj { { "cmd", "restore-confirm" }, { "digest", p.S("digest") }, { "nonce", p.S("nonce") } });
                progress = new ProgressView(this, "Restoring the Console", "Do not unplug the console. Stop After This Part finishes and checks the part being written, then stops; the console may then be partly restored.", "Stop After This Part", () => { worker?.Cancel(); progress.Say("Stopping after this part..."); });
                Show(progress.Panel, "progress");
            }, false);
            cb.Checked += (s, e) => go.IsEnabled = true; cb.Unchecked += (s, e) => go.IsEnabled = false;
            sp.Children.Add(Buttons(go, B("Cancel", "Btn.Ghost", () => Leave())));
            Show(sp, "plan");
        }

        void RestoreResult(Dictionary<string, object> r)
        {
            var st = r.S("state"); var sp = new StackPanel();
            if (st == "restored")
            {
                sp.Children.Add(PageTitle("Restore complete"));
                sp.Children.Add(Note("Every part that was written was read back from the console and matches the backup. Restart the console and check that it starts normally.", "ok"));
                sp.Children.Add(T("The backup of the console as it was just before is kept:", Ty.Small, false, true)); sp.Children.Add(PathText(r.S("protective")));
                sp.Children.Add(Buttons(B("Restart the Console", "Btn.Primary", () => worker?.Send(new JObj { { "cmd", "finish" } })), B("Done", "Btn.Ghost", () => { worker?.Send(new JObj { { "cmd", "quit" } }); BackToSection(); })));
            }
            else
            {
                var title = st == "stopped" ? "The restore stopped partway" : st == "canceled" ? "Restore canceled" : st == "unavailable" ? "Not available in this version" : st == "nothing-to-do" ? "Nothing to restore" : "The restore could not continue";
                sp.Children.Add(PageTitle(title));
                sp.Children.Add(Note(r.S("problem") ?? "The console already matches this backup.", st == "nothing-to-do" ? "ok" : st == "canceled" ? "warn" : "bad"));
                var written = (r.A("written") ?? new List<object>()).Cast<string>().ToList();
                if (st == "stopped") sp.Children.Add(T("Parts fully written: " + (written.Count == 0 ? "none" : string.Join(", ", written)) + ". Nothing more was written and nothing was retried, so the console may be partly restored. Don't start it normally yet: first make a new backup of it as it is now (Back Up Console starts it in its recovery mode), then decide what to do next.", 13));
                if (r.S("protective") != null) { sp.Children.Add(T("The backup made just before:", Ty.Small, false, true)); sp.Children.Add(PathText(r.S("protective"))); }
                sp.Children.Add(Buttons(B("Done", "Btn", () => { worker?.Send(new JObj { { "cmd", "quit" } }); BackToSection(); })));
            }
            Show(sp, "result");
        }

        // ------------------------------------------------------------------ Prepare Console
        void ShowPrepare()
        {
            var sp = PageHead("Prepare Console", "Set your " + Con + " up for Classic Cart Studio's HOME. This changes the console's startup software and bootloader and adds files; you review every step before anything is written.");
            var c = new StackPanel();
            c.Children.Add(T("What this does", Ty.Heading, true));
            c.Children.Add(T("Workbench installs the software Classic Cart Studio's HOME runs on. No other program is needed. Nintendo's HOME, its built-in games and their saves stay. A console that is already prepared this way is left as it is.", Ty.Text));
            c.Children.Add(T("Before anything changes", Ty.Heading, true, false, new Thickness(0, 10, 0, 6)));
            c.Children.Add(T("You need a checked backup of this console as it is now, before it is prepared: make it with Back Up Console. When you prepare, Workbench backs the console up once more in the same session, with every save; that backup is your way back (Return to Original Software). You review the exact steps and confirm them. If a step fails, the steps after it are not done, and nothing is retried.", Ty.Text));
            c.Children.Add(Details(TechNes, "What is installed: Hakchi2 CE 3.9.3's kernel and files, with the add-ons you choose (CCS Mod with CCS HOME's package; CCS Nucleus, with or without its startup chooser).", "As Hakchi2 CE's own installation does, hakchi's bootloader (u-boot) replaces the console's own and the startup software (kernel) is replaced by hakchi's; a copy of the original Nintendo kernel is kept in the spare startup slot, and the originals stay in your backups. Return to Original Software puts this console's own bootloader and Nintendo's kernel back."));
            sp.Children.Add(Card(c));
            var ad = new StackPanel();
            ad.Children.Add(T("Your add-ons", Ty.Heading, true, false, new Thickness(0, 0, 0, 6)));
            ad.Children.Add(T("After the console is checked, you choose your add-ons on two pages: CCS Mod, which adds CCS HOME, a menu for your game collections, and CCS Nucleus, which chooses what opens when you turn on the console. Add-ons already on the console stay.", Ty.Text));
            sp.Children.Add(Card(ad));
            if (string.IsNullOrEmpty(opt.SimulatedConsole) || !profile.ConsoleTasks) sp.Children.Add(ConnectSteps("Nothing is written until you confirm the steps.", "prepare-plan"));
            sp.Children.Add(Buttons(B("Check the Console", "Btn.Primary", () => StartPrepare()), B("Back", "Btn.Ghost", BackToSection)));
            sp.Children.Add(Group("Already prepared?", 18));
            sp.Children.Add(Card(Inline("Return to Original Software undoes the preparation: it puts back Nintendo's startup software and this console's own bootloader, removes hakchi's files and restores the original storage layout (as Hakchi2 CE's own uninstall does), then restores the backup made before preparing, with its games and saves exactly as they were.", B("Return to Original Software…", "Btn.Ghost", () => ShowReturn())), 16));
            Show(sp, "prepare");
        }

        // ------------------------------------------------------------------ Return to Original Software
        void ShowReturn()
        {
            var sp = PageHead("Return to Original Software", "Undo Classic Cart Studio's preparation of your " + Con + ". This changes the console's startup software, bootloader and storage; you review every step first.");
            var c = new StackPanel();
            c.Children.Add(T("What this does", Ty.Heading, true));
            c.Children.Add(T("It works in two parts. First, in this session, Workbench puts back Nintendo's startup software and this console's original bootloader, removes hakchi's files and restores the original storage layout. Then, after the console restarts, Restore a Backup puts back the backup made before preparing, so the console's storage is exactly as it was then.", 13));
            c.Children.Add(T("Before anything changes", 15, true, false, new Thickness(0, 10, 0, 6)));
            c.Children.Add(T("This works only on a console exactly as Classic Cart Studio's preparation left it; anything else is refused. Workbench first backs the console up as it is now, so saves made since preparing are kept. You review the steps and confirm them. If a step fails, the steps after it are not done, and nothing is retried.", 13));
            sp.Children.Add(Card(c));
            if (string.IsNullOrEmpty(opt.SimulatedConsole) || !profile.ConsoleTasks) sp.Children.Add(ConnectSteps("Nothing is written until you confirm the steps.", "return-plan"));
            sp.Children.Add(Buttons(B("Check the Console", "Btn.Primary", () => StartReturn()), B("Back", "Btn.Ghost", BackToSection)));
            Show(sp, "return");
        }

        void StartReturn()
        {
            if (!FamilyHasTasks("return") || !BoundaryOpen("return-plan") || !OwnTaskAllows("Return to Original Software")) return;
            if (!driverReady && string.IsNullOrEmpty(opt.SimulatedConsole)) { WithDriver(() => StartReturn()); return; }
            if (ConsoleLock.IsBusy()) { Show(Message("Another console operation is running", "Wait for it to finish, then try again.", "bad"), "msg"); return; }
            progress = new ProgressView(this, "Checking the Console", "Nothing is changed in this step.", "Cancel", () => { worker?.Cancel(); progress.Say("Canceling..."); });
            Show(progress.Panel, "progress");
            NewSession("return");
            worker.Send(ConsoleReq("return-plan"));
        }

        void ShowReturnPlan(Dictionary<string, object> p)
        {
            var sp = new StackPanel(); sp.Children.Add(PageTitle("Review the Return"));
            var c = new StackPanel(); c.Children.Add(T("Console " + p.S("console_tag") + ": back to Nintendo's original software", 15, true, false, new Thickness(0, 0, 0, 10)));
            var steps = (p.A("steps") ?? new List<object>()).Cast<string>().ToList();
            c.Children.Add(Numbered(steps.Select(s => Capital(ReturnEngine.StepLabel(s))).Concat(new[] { "After the console restarts: Restore a Backup, with the backup made before preparing" }).ToArray()));
            foreach (var n in (p.A("notes") ?? new List<object>())) c.Children.Add(Note(n as string, "info"));
            sp.Children.Add(Card(c));
            var go = B("Return Now", "Btn.Primary", () =>
            {
                worker?.Send(new JObj { { "cmd", "return-confirm" }, { "digest", p.S("digest") }, { "nonce", p.S("nonce") } });
                progress = new ProgressView(this, "Returning the Console", "Do not unplug the console. Stop After This Step finishes the step in progress, then stops.", "Stop After This Step", () => { worker?.Cancel(); progress.Say("Stopping after this step..."); });
                Show(progress.Panel, "progress");
            }, false);
            var cb = new CheckBox { Content = new TextBlock { Text = "I understand: this changes the console's startup software, bootloader, data storage and storage table as listed.", TextWrapping = TextWrapping.Wrap }, Margin = new Thickness(0, 0, 0, 10), Foreground = Res<Brush>("B.Ink") };
            cb.Checked += (s, e) => go.IsEnabled = true; cb.Unchecked += (s, e) => go.IsEnabled = false; sp.Children.Add(cb);
            sp.Children.Add(Buttons(go, B("Cancel", "Btn.Ghost", () => Leave())));
            Show(sp, "return-plan");
        }

        void ReturnResult(Dictionary<string, object> r)
        {
            var st = r.S("state"); var sp = new StackPanel(); var snap = r.S("snapshot");
            var title = st == "returned" ? "Nintendo's software is back" : st == "unavailable" ? "Not available in this version" : st == "stopped" ? "The return stopped partway" : st == "canceled" ? "Canceled" : "The return could not continue";
            sp.Children.Add(PageTitle(title));
            sp.Children.Add(Note(st == "returned" ? "Each step was written once and read back: Nintendo's startup software and this console's own bootloader are back, hakchi's files are removed and the storage layout is as it was. One part is left: putting back the games and saves the console had before it was prepared." : (r.S("problem") ?? ""), st == "returned" ? "ok" : st == "canceled" ? "warn" : "bad"));
            if (st == "returned" && snap != null)
            {
                sp.Children.Add(T("Next: restore the backup made before preparing. When Workbench asks, connect the console again the same way (POWER off, hold RESET, POWER on).", 13));
                sp.Children.Add(Buttons(B("Restore the Backup Made Before Preparing…", "Btn.Primary", () => { worker?.Send(new JObj { { "cmd", "quit" } }); ShowRestoreReview(snap); }), B("Done", "Btn.Ghost", () => { worker?.Send(new JObj { { "cmd", "quit" } }); BackToSection(); })));
            }
            else sp.Children.Add(Buttons(B("Done", "Btn", () => { worker?.Send(new JObj { { "cmd", "quit" } }); BackToSection(); })));
            Show(sp, "result");
        }

        void StartPrepare()
        {
            if (!FamilyHasTasks("preparation") || !BoundaryOpen("prepare-plan") || !OwnTaskAllows("Prepare Console")) return;
            if (!driverReady && string.IsNullOrEmpty(opt.SimulatedConsole)) { WithDriver(() => StartPrepare()); return; }
            if (ConsoleLock.IsBusy()) { Show(Message("Another console operation is running", "Wait for it to finish, then try again.", "bad"), "msg"); return; }
            progress = new ProgressView(this, "Checking the Console", "Nothing is changed in this step.", "Cancel", () => { worker?.Cancel(); progress.Say("Canceling..."); });
            Show(progress.Panel, "progress");
            NewSession("prepare");
            addons = null; var req = ConsoleReq("prepare-plan"); req.Add("choose", true); worker.Send(req);   // the add-on pages follow the console's check
        }

        void ShowPrepPlan(Dictionary<string, object> p)
        {
            var sp = PageHead("Review the Preparation", "What preparing would change on your " + Con + ". Nothing has been written yet.");
            var c = new StackPanel(); c.Children.Add(T("Console " + p.S("console_tag") + ": " + p.S("text"), 14, true));
            if (p.S("refusal") == null) c.Children.Add(AddonSummary(ChoiceOf(p.O("addons")), ChoiceOf(p.O("console_state")?.O("addons"))));
            var steps = (p.A("steps") ?? new List<object>()).Cast<string>().ToList();
            foreach (var s in steps) c.Children.Add(T((steps.IndexOf(s) + 1) + ". " + Capital(Preparation.StepLabel(s)), 13));
            var hm = p.O("home");
            if (hm != null) c.Children.Add(T("CCS HOME's package: " + hm.S("size") + (hm.S("replaces") != null && hm.S("replaces") != "absent" ? ". The HOME the console uses now stays until the switch." : "."), 12.5, false, true, new Thickness(0, 4, 0, 0)));
            foreach (var n in (p.A("notes") ?? new List<object>())) c.Children.Add(Note(n as string, "info"));
            if (EarlierHomeReview(p.O("earlier_home"), () => Leave(), "Back", p.O("home")?.S("size")) is UIElement eh) c.Children.Add(eh);
            if (p.S("refusal") != null) c.Children.Add(Note(p.S("refusal"), "bad"));
            sp.Children.Add(Card(c));
            if (p.S("refusal") != null) { sp.Children.Add(Buttons(B("Done", "Btn", () => Leave()))); Show(sp, "prep-plan"); return; }
            var go = B(steps.Count == 0 ? "Done" : "Prepare Now", "Btn.Primary", () =>
            {
                worker?.Send(new JObj { { "cmd", "prepare-confirm" }, { "digest", p.S("digest") }, { "nonce", p.S("nonce") } });
                progress = new ProgressView(this, "Preparing the Console", "Do not unplug the console. Stop After This Step finishes the step in progress, then stops.", "Stop After This Step", () => { worker?.Cancel(); progress.Say("Stopping after this step..."); });
                Show(progress.Panel, "progress");
            }, steps.Count == 0);
            if (steps.Count > 0)
            {
                var cb = new CheckBox { Content = new TextBlock { Text = "I understand: this changes the console's startup software, bootloader and data storage as listed.", TextWrapping = TextWrapping.Wrap }, Margin = new Thickness(0, 0, 0, 10), Foreground = Res<Brush>("B.Ink") };
                cb.Checked += (s, e) => go.IsEnabled = true; cb.Unchecked += (s, e) => go.IsEnabled = false; sp.Children.Add(cb);
            }
            sp.Children.Add(Buttons(go, B("Cancel", "Btn.Ghost", () => Leave())));
            Show(sp, "prep-plan");
        }

        void PrepResult(Dictionary<string, object> r)
        {
            var st = r.S("state"); var sp = new StackPanel();
            var title = st == "prepared" ? "Console prepared" : st == "method-scheduled" ? "Ready for the next start" : st == "home-placed" ? "CCS HOME is on the console" : st == "already-prepared" ? "Already prepared" : st == "unavailable" ? "Not available in this version" : st == "stopped" ? "The preparation stopped partway" : st == "canceled" ? "Canceled" : "The preparation could not continue";
            sp.Children.Add(PageTitle(title));
            var done = (r.A("done") ?? new List<object>()).OfType<string>().ToList();
            bool added = done.Any(d => d.StartsWith("method:", StringComparison.Ordinal));
            var okText = st == "prepared" ? "Every step was written once and read back. Restart the console" + (added ? ": at that start hakchi installs your add-ons and restarts the console once." : " and check that Nintendo's HOME starts.")
                : st == "method-scheduled" ? (added ? "Your add-ons are waiting in hakchi's package folder: hakchi installs them the next time the console starts normally, then restarts the console once." : "What opens when the console is turned on is set, and read back.") + (done.Contains("huse") ? " CCS HOME is on the console now, checked file by file." : "")
                : st == "home-placed" ? "CCS HOME's package was written once, checked file by file, and is now the one the console uses. How CCS HOME starts, and the rest of the console, are unchanged."
                : st == "already-prepared" ? "Nothing needed to change." : null;
            sp.Children.Add(Note(okText ?? r.S("problem") ?? "", okText != null ? "ok" : st == "canceled" ? "warn" : "bad"));
            if (r.S("code") == "current-backup-required" || r.S("code") == "backup-required") sp.Children.Add(T("Back Up Console makes the backup it needs; then Prepare Console again.", 13));
            sp.Children.Add(Buttons(st == "prepared" || st == "method-scheduled" || st == "home-placed" ? B("Restart the Console", "Btn.Primary", () => worker?.Send(new JObj { { "cmd", "finish" } })) : (r.S("code") == "current-backup-required" || r.S("code") == "backup-required" ? B("Back Up Console", "Btn", () => { worker?.Send(new JObj { { "cmd", "quit" } }); Enter("Console Backup"); ShowBackup(); }) : null), B("Done", "Btn.Ghost", () => { worker?.Send(new JObj { { "cmd", "quit" } }); BackToSection(); })));
            Show(sp, "result");
        }

        // ------------------------------------------------------------------ Verify (here; no console)
        void ShowVerify(string file, HistoryEntry e)
        {
            var pv = new ProgressView(this, "Checking the Backup File", "Every part is read back from the file on this PC and compared with its checksums. No console is needed, and nothing is written to the file.", "Cancel", null);
            Show(pv.Panel, "progress");
            Task.Run(() => Verifier.VerifyFile(file, null, null, (d, t) => Dispatcher.BeginInvoke(new Action(() => pv.Update("Checking...", d, t))))).ContinueWith(t =>
            {
                if (t.IsFaulted) { if (page.Content == pv.Panel) Show(Message("The backup file could not be checked", "The file could not be read to the end. Nothing was written to it.", "bad", t.Exception?.GetBaseException().Message), "msg"); else UpdateStatus("The file check could not read the file to the end."); return; }
                if (page.Content != pv.Panel) { if (t.Result.Ok) { try { NoteVerified(file, t.Result, e); } catch { } } UpdateStatus(t.Result.Ok ? "The file check finished after you left: the file passed its check." : "The file check finished after you left: the file did not pass. Check it again from Console Backup to see why."); return; }
                var v = t.Result; var sp = new StackPanel();
                sp.Children.Add(PageTitle(v.Ok ? "The file passed its check: every part matches" : "This backup did not pass its check"));
                if (v.Ok)
                {
                    sp.Children.Add(Note("Read back in full just now: " + Size(v.Bytes) + ", made " + When(v.Manifest.S("completed_utc")) + " from console " + v.ConsoleTag + "." + (v.RecoveryBaseline ? " It holds the original Nintendo kernel and can serve as this console's recovery baseline." : " It does not hold the original Nintendo kernel."), "ok"));
                    sp.Children.Add(T("This file check shows the file is intact. It does not restore anything or check the console.", Ty.Small, false, true));
                    sp.Children.Add(Details("Technical details", "File: " + file, "SHA-256 of the file: " + v.ArchiveSha256));
                    try { NoteVerified(file, v, e); } catch { }
                }
                else { foreach (var p in v.Problems.Take(3)) sp.Children.Add(Note(p, "bad")); sp.Children.Add(Details("Technical details", new object[] { "File: " + file }.Concat(v.Problems.Skip(3).Take(12)).ToArray())); sp.Children.Add(T("Backup Repair can check what is damaged and make a repaired copy.", Ty.Text, false, true)); }
                sp.Children.Add(Buttons(B("Back", "Btn", BackToSection), v.Ok ? null : B("Backup Repair", "Btn.Ghost", () => Go("Backup Repair"))));
                Show(sp, "verify");
            }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        /// <summary>a backup without a record (copied in, or an earlier unfinished record) that verifies gets a record of this check</summary>
        void NoteVerified(string file, Verification v, HistoryEntry e)
        {
            if (e?.RecordData != null && (e.RecordData.S("state") == "complete" || e.RecordData.S("state") == "complete-one-copy")) return;
            var id = v.Manifest.S("backup_id") ?? Guid.NewGuid().ToString("N");
            var f = Path.Combine(settings.RecordsFolder, "backup-" + id + ".json");
            if (File.Exists(f)) return;
            var rec = Record.Start(settings.RecordsFolder, id, "backup", new JObj { { "entry", opt.HostedInFullApp ? "classic-cart-studio" : "workbench" }, { "console_tag", v.ConsoleTag }, { "simulated", v.Simulated }, { "verified_later", true },
                { "primary", new JObj { { "final", file }, { "sha256", v.ArchiveSha256 }, { "bytes", v.Bytes }, { "verified_utc", Record.Now() }, { "mtime_utc", File.GetLastWriteTimeUtc(file).ToString("o", CultureInfo.InvariantCulture) } } },
                { "state_text", v.Manifest.O("state")?.S("text") }, { "restore_ready", v.RestoreReady }, { "recovery_baseline", v.RecoveryBaseline } });
            rec.State("complete-one-copy", "Checked after it was made; one copy is known.");
        }

        // ------------------------------------------------------------------ a folder choice and messages
        /// <summary>a folder for a text box (Settings' Change…): the dialog starts at the folder the box names</summary>
        void Pick(TextBox box, string title = "Choose a folder")
        {
            var f = CcsFileDialog.ChooseFolder(this, title, box.Text, "settings-folder");
            if (f != null) box.Text = f;
        }

        FrameworkElement Message(string title, string text, string kind, string details = null)
        {
            var sp = new StackPanel(); sp.Children.Add(PageTitle(title)); sp.Children.Add(Note(text, kind));
            if (!string.IsNullOrEmpty(details)) sp.Children.Add(Details("Technical details", details));
            // Back on the console task's own message ends its session (as its Cancel does); on any other page it only goes back, and a task
            // running behind it goes on
            sp.Children.Add(Buttons(B("Back", "Btn", () => { if (worker != null && worker.Running && page.Content == sp && ReferenceEquals(sp, taskPage)) Leave(); else BackToSection(); }))); return sp;
        }

        // ------------------------------------------------------------------ closing: one review of unsaved work, then a task's safe stop, then the USB driver
        // WB-UX-20. Closing the window (its Close button, the title bar's X, Alt+F4, Windows) goes through three separate steps, in this order:
        //   1. the close review: ONE themed question listing the work this close would lose (the standalone's unsaved settings, a settings
        //      change installed mode could not save, the HOME design with changes, what a page holds that is not kept, typed Settings text
        //      that is not valid), with Save & Close, Close Without Saving and Cancel. Nothing is asked when there is nothing to lose. Close
        //      Without Saving lets go of the listed items only, and only once the window really closes; Cancel keeps everything, the session
        //      and the task. A file choice canceled or a save that fails keeps the window open with the work as it was and says why, in
        //      the Workbench's own look; nothing is called saved unless it was written.
        //   2. a console task's safe stop (a backup stops, a restore finishes the part it is writing), as before
        //   3. the USB driver question, only for a driver this Workbench set up and only when the rules allow (WorkbenchDriver.cs), as before
        // A later step's Cancel keeps the window open with the review's work untouched (the review asks again at the next close).
        bool closeReviewed, discardDesignAtClose, inClosing;
        /// <summary>a page's work that is not kept if the window leaves it, in words for the close review (null: none); set with LeaveGuard</summary>
        Func<string> pageUnsaved;

        /// <summary>a page whose work is lost if the window leaves it: leaving the page asks first, naming what (Stay Here and leaveWord:
        /// "Leave Setup" for a guided setup's choices, otherwise "Leave Without Saving" unless the page has a more exact word; Save and
        /// Leave first when save is given), and the close review lists what as not kept. letGo (optional): runs when the window leaves the
        /// page (after leaving without it, or with nothing to ask about), for work kept apart from the page itself (HOME Designer's typed
        /// text); Stay Here keeps it.</summary>
        void LeaveGuard(Func<string> what, string question, Action letGo = null, string leaveWord = null, Func<bool> save = null)
        {
            pageUnsaved = what;
            leaveCheck = () =>
            {
                if (what() != null && !AskToLeave(question, leaveWord, save)) return false;
                letGo?.Invoke(); return true;
            };
        }

        void OnClosing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (inClosing) { e.Cancel = true; return; }   // never a second close inside the first one's questions
            inClosing = true;
            try
            {
                if (!closeReviewed) { if (!CloseReview()) { e.Cancel = true; return; } closeReviewed = true; }
                ClosingAfterReview(e);
                if (e.Cancel) { if (!closeWhenDone) { closeReviewed = false; discardDesignAtClose = false; } return; }
                if (discardDesignAtClose) { discardDesignAtClose = false; DiscardDesignForClose(); }   // the window closes now: let go of the listed design
            }
            finally { inClosing = false; }
        }

        /// <summary>a test can answer the close review (title, words, buttons) → the chosen button's index (-1: closed without one); null: the
        /// review is shown as a dialog of the window</summary>
#pragma warning disable CS0649   // set only by tests
        internal static Func<string, string, string[], int> ReviewHook;
#pragma warning restore CS0649
        internal const int ReviewSave = 0, ReviewDiscard = 1, ReviewCancel = 2;

        /// <summary>step 1 of closing: false keeps the window open with everything as it was</summary>
        bool CloseReview()
        {
            // a page that has not named its work for this review keeps its own question (never while a task runs: its page is the task's)
            if (!(Busy && !TaskAway) && pageUnsaved == null && leaveCheck != null && PageTag != "settings" && !LeaveOk()) return false;
            var save = new List<string>(); var lost = new List<string>();
            PrefsForReview(save, lost);
            var design = DesignUnsavedName();
            if (design != null) save.Add("Your HOME design “" + design + "”");
            if (pageUnsaved?.Invoke() is string page) lost.Add(page);
            // HOME Designer's typed text not added yet stays with the open design while another page is open: named here too
            if (DesignDraftWords() is string typed && !lost.Contains(typed)) lost.Add(typed);
            if (save.Count == 0 && lost.Count == 0) return true;
            int k = Review(save, lost);
            if (k == ReviewDiscard) { discardDesignAtClose = design != null; return true; }
            if (k != ReviewSave || save.Count == 0) return false;
            // Save & Close: the settings, then the design; the first that is not saved stops the close, with the work as it was
            string done = null;
            if (SavePrefsForClose() is string prefsProblem) { NotSaved(prefsProblem, null); return false; }
            if (save.Any(x => x.StartsWith("Your settings"))) done = "Your settings were saved.";
            if (design != null && !SaveDesignAtClose(design, done)) return false;
            return true;
        }

        /// <summary>the review itself: a themed dialog of the window with the two lists and the three buttons (two when nothing can be saved)</summary>
        int Review(List<string> save, List<string> lost)
        {
            var title = save.Count > 0 ? "Save before closing?" : "Close Workbench?";
            var buttons = save.Count > 0 ? new[] { "Save & Close", "Close Without Saving", "Cancel" } : new[] { "Close Without Saving", "Cancel" };
            int Map(int k) => save.Count > 0 ? k : k == 0 ? ReviewDiscard : ReviewCancel;
            if (ReviewHook != null)
            {
                var words = (save.Count > 0 ? "Not saved yet:\n" + string.Join("\n", save.Select(x => "• " + x)) : "")
                          + (lost.Count > 0 ? (save.Count > 0 ? "\n\n" : "") + "Not kept when Workbench closes:\n" + string.Join("\n", lost.Select(x => "• " + x)) : "");
                int a = ReviewHook(title, words, buttons); return a < 0 ? ReviewCancel : Map(a);
            }
            int chosen = -1;
            var dlg = ReviewWindow(title, save, lost, buttons, k => chosen = k);
            Modal(dlg);
            return chosen < 0 ? ReviewCancel : Map(chosen);
        }

        /// <summary>the close review's dialog, not shown yet (the page renderer draws it): done(k) runs when button k closes it; Escape and its X are Cancel</summary>
        internal Window ReviewWindow(string title, List<string> save, List<string> lost, string[] buttons, Action<int> done)
        {
            var dlg = Owned(title, 580, false);
            var sp = new StackPanel { Margin = new Thickness(24, 20, 24, 16) };
            sp.Children.Add(T(title, 17, true, false, new Thickness(0, 0, 0, 10)));
            void List(string head, List<string> items)
            {
                if (items.Count == 0) return;
                sp.Children.Add(T(head, 13.5, true, false, new Thickness(0, 2, 0, 6)));
                var g = new Grid { Margin = new Thickness(0, 0, 0, 10) }; g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(18) }); g.ColumnDefinitions.Add(new ColumnDefinition());
                for (int i = 0; i < items.Count; i++)
                {
                    g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                    var dot = new TextBlock { Text = "•", FontSize = 13.5, Foreground = Res<Brush>("B.Muted"), Margin = new Thickness(2, 0, 0, 4) }; Grid.SetRow(dot, i); g.Children.Add(dot);
                    var t = T(items[i], 13.5, false, false, new Thickness(0, 0, 0, 4)); SetNoTerms(t, true); Grid.SetRow(t, i); Grid.SetColumn(t, 1); g.Children.Add(t);
                }
                sp.Children.Add(g);
            }
            List("Not saved yet:", save);
            List("Not kept when Workbench closes:", lost);
            var row = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) };
            for (int i = 0; i < buttons.Length; i++)
            {
                // Enter is Save & Close when there is something to save; letting work go is never the default
                int k = i; var b = B(buttons[i], i == 0 && save.Count > 0 ? "Btn.Primary" : "Btn", () => { done(k); dlg.Close(); });
                b.IsDefault = i == 0 && save.Count > 0; row.Children.Add(b);
            }
            sp.Children.Add(row);
            dlg.Content = sp;
            dlg.Loaded += (s, e) => (row.Children[save.Count > 0 ? 0 : row.Children.Count - 1] as Button)?.Focus();
            return dlg;
        }

        /// <summary>Save &amp; Close for the design: its own Save (Save As when it has no file yet). The page a running task shows is kept (the
        /// design's save draws its own page). false when the file choice was canceled or the save failed, said in a themed notice.</summary>
        bool SaveDesignAtClose(string name, string done)
        {
            var keepPage = page.Content; var keepSection = section; bool session = worker != null && worker.Running;
            designProblem = null;
            bool ok = SaveDesignForClose();
            if (session && page.Content != keepPage) { page.Content = keepPage; Enter(keepSection); UpdateChrome(); }
            if (ok) return true;
            NotSaved("Your HOME design “" + name + "” was not saved: " + (designProblem != null ? designProblem.TrimEnd('.') : "no file was chosen") + ".", done);
            return false;
        }

        /// <summary>a save at close did not happen: Workbench stays open with the work as it was, and says so</summary>
        void NotSaved(string what, string done) =>
            Tell("Workbench is still open", what + (done != null ? " " + done : "") + "\n\nNothing was closed and nothing was let go: your work is still here.");

        void ClosingAfterReview(System.ComponentModel.CancelEventArgs e)
        {
            if (worker == null || !worker.Running || gotResult) { if (!DriverAtClose()) { e.Cancel = true; return; } }
            var w = worker;
            if (w == null || !w.Running) return;
            if (gotResult) { w.Detach(); return; }   // the operation has ended: nothing to stop; its worker sees its input end and exits
            if (writing)
            {
                if (Ask("Keep the window open?", "A restore is writing to the console now. If you close, it stops after the part being written is finished and checked, and the console may be left partly restored.",
                        new[] { "Keep the Window Open", "Close After This Part" }, 0, 0) != 1) { e.Cancel = true; return; }
                if (worker == null || !worker.Running) return;   // it ended while the question was open: nothing left to stop
                worker.Cancel(); closeWhenDone = true; e.Cancel = true; progress?.Say("Stopping after this part, then closing..."); return;
            }
            bool backup = workerOp == "backup";
            if (Ask(backup ? "Stop the backup?" : "Stop the console task?", backup ? "Nothing on the console changes; the unfinished file is kept and marked Incomplete." : "Nothing has been written to the console.",
                    new[] { backup ? "Stop the Backup and Close" : "Stop and Close", "Keep Working" }, 1, 1) != 0) { e.Cancel = true; return; }
            w = worker; if (w == null || !w.Running) return;
            w.Cancel(); w.Detach();
        }

        // ------------------------------------------------------------------ the progress panel
        /// <summary>a task's progress page. Its elapsed time is its own clock (a monotonic Stopwatch), shown once a second while the page is in
        /// front, whether or not the task has said anything; the clock starts with the task's step, stops when the step ends (a review, a
        /// result, a failure, a cancel: the task's next page), and is right again when the page comes back in front. The bytes and the bar
        /// only ever show what the task reported.</summary>
        sealed class ProgressView
        {
            public readonly StackPanel Panel = new StackPanel(); public readonly string Title;
            readonly TextBlock text, bytes; readonly ProgressBar bar; readonly Button stop;
            readonly System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew(); readonly System.Windows.Threading.DispatcherTimer tick;
            long done, total; bool shown;
            public ProgressView(BackupWorkspace w, string title, string note, string stopText, Action onStop)
            {
                w.progressTitle = title; Title = title;
                Panel.Children.Add(w.PageHead(title, note));
                text = w.T("Starting...", 14, true, false, new Thickness(0, 0, 0, 10));
                bar = new ProgressBar { Height = 8, Minimum = 0, Maximum = 1000, IsIndeterminate = true, Foreground = w.Res<Brush>("B.Red"), Background = w.Res<Brush>("B.Surface2"), BorderThickness = new Thickness(0) };
                bytes = w.T("", 12.5, false, true, new Thickness(0, 10, 0, 0));
                var box = new StackPanel(); box.Children.Add(text); box.Children.Add(bar); box.Children.Add(bytes);
                Panel.Children.Add(w.Card(box, 22));
                if (onStop != null) { stop = w.B(stopText, "Btn", onStop); Panel.Children.Add(w.Buttons(stop)); }
                tick = new System.Windows.Threading.DispatcherTimer(System.Windows.Threading.DispatcherPriority.Background, w.Dispatcher) { Interval = TimeSpan.FromSeconds(1) };
                tick.Tick += (s, e) => Render();
                Panel.Loaded += (s, e) => { shown = true; if (clock.IsRunning) tick.Start(); Render(); };
                Panel.Unloaded += (s, e) => { shown = false; tick.Stop(); };
                w.progressViews.Add(Panel, this);
                Render();
            }
            /// <summary>whether its step's clock is running</summary>
            public bool Running => clock.IsRunning;
            public TimeSpan Elapsed => clock.Elapsed;
            public void Say(string t) { text.Text = t; if (stop != null) stop.IsEnabled = false; }
            public void Update(string t, long done, long total)
            {
                if (!string.IsNullOrEmpty(t)) text.Text = t;
                this.done = done; this.total = total;
                if (total > 0) { bar.IsIndeterminate = false; bar.Value = Math.Min(1000, 1000.0 * done / total); } else bar.IsIndeterminate = true;
                Render();
            }
            /// <summary>its step has ended: the clock stops at the time it took</summary>
            public void Stop() { clock.Stop(); tick.Stop(); Render(); }
            /// <summary>the same page for the task's next step: a new clock, and nothing reported yet</summary>
            public void Restart() { clock.Restart(); done = total = 0; bar.IsIndeterminate = true; if (stop != null) stop.IsEnabled = true; if (shown) tick.Start(); Render(); }
            void Render()
            {
                var el = clock.Elapsed; var e = (int)el.TotalHours > 0 ? el.ToString(@"h\:mm\:ss") : el.ToString(@"m\:ss");
                bytes.Text = total > 0 ? Units.Gb(done) + " of " + Units.Gb(total) + "  ·  " + e + " elapsed" : e + " elapsed";
            }
        }
    }
}
