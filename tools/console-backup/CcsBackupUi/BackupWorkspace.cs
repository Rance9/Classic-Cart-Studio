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
using System.Windows.Shell;

namespace CcsBackup.Ui
{
    public sealed class WorkspaceOptions
    {
        public bool HostedInFullApp;
        public string ProductName = "CCS Console Workbench", Version = EngineInfo.Version;
        public string WorkerExe; public List<string> WorkerArgs = new List<string> { WorkerHost.Flag };
        public string SettingsPath, SimulatedConsole;
        /// <summary>the standalone: ordinary preferences in memory unless a settings file was chosen (--settings or Save to a File…)</summary>
        public bool Portable;
        public string FullAppUrl = "https://classiccartstudio.com/app/";   // the official Windows download page (Get Full Classic Cart Studio)
        public ImageSource Wordmark, AppIcon; public string NoticeText;
        /// <summary>Classic Cart Studio for Windows: the HOME package it prepared last (its data folder's native-home\current), for HOME
        /// Designer's preview drawn with the person's own games; null in the standalone Workbench</summary>
        public string PreparedHomePackage;
    }

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

        public BackupWorkspace(WorkspaceOptions o, Window owner = null)
        {
            opt = o; settings = o.Portable && o.SettingsPath == null ? Settings.Portable(null, null) : Settings.Load(o.SettingsPath);
            InitPrefs(); HookTerms(); Deactivated += (s, e) => DropHover();
            Title = ProductTitle;
            Width = 1180; Height = 780; MinWidth = 860; MinHeight = 560;
            if (owner != null) { Owner = owner; WindowStartupLocation = WindowStartupLocation.CenterOwner; } else WindowStartupLocation = WindowStartupLocation.CenterScreen;
            try { Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/CcsBackupUi;component/Workbench.xaml", UriKind.Absolute) }); DeriveStyles(); } catch { }
            Background = Res<Brush>("B.Bg"); UseLayoutRounding = true; SnapsToDevicePixels = true; TextOptions.SetTextFormattingMode(this, TextFormattingMode.Ideal);
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
            SourceInitialized += (s, e) => Dark(this);
            PreviewKeyDown += (s, e) => { if (e.Key == Key.Escape) { if (CloseTerm(true)) e.Handled = true; else OnEscape(e); } else if (e.Key == Key.F1) { OpenPageHelp(); e.Handled = true; } };
            Closing += OnClosing;
            Enter("Dashboard"); ShowDashboard();
        }

        // ------------------------------------------------------------------ chrome and building blocks
        T Res<T>(string k) where T : class => (TryFindResource(k) as T);
        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr h, int a, ref int v, int s);
        static void Dark(Window w) { try { var h = new WindowInteropHelper(w).Handle; int on = 1; DwmSetWindowAttribute(h, 20, ref on, 4); int c = 0x00202024; DwmSetWindowAttribute(h, 35, ref c, 4); } catch { } }

        UIElement TitleBar()
        {
            var bar = new Grid { Background = Res<Brush>("B.Bar") };
            bar.ColumnDefinitions.Add(new ColumnDefinition()); for (int i = 0; i < 3; i++) bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            bar.Children.Add(new TextBlock { Text = Title, FontSize = 12.5, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14, 0, 0, 1) });
            var min = new Button { Content = "", Style = Res<Style>("Caption"), Height = 38, ToolTip = "Minimize" }; System.Windows.Automation.AutomationProperties.SetName(min, "Minimize"); min.Click += (s, e) => WindowState = WindowState.Minimized; WindowChrome.SetIsHitTestVisibleInChrome(min, true); Grid.SetColumn(min, 1);
            var close = new Button { Content = "", Style = Res<Style>("Caption.Close"), Height = 38, ToolTip = "Close" }; System.Windows.Automation.AutomationProperties.SetName(close, "Close"); close.Click += (s, e) => Close(); WindowChrome.SetIsHitTestVisibleInChrome(close, true); Grid.SetColumn(close, 3);
            // maximize / restore between them: its symbol, name and tooltip follow the window's state however it changed (this button, a
            // double-click on the title bar, Snap, Win+Up/Down or the system menu)
            var max = new Button { Content = "\uE922", Style = Res<Style>("Caption"), Height = 38, ToolTip = "Maximize" }; System.Windows.Automation.AutomationProperties.SetName(max, "Maximize");
            max.Click += (s, e) => { if (WindowState == WindowState.Maximized) SystemCommands.RestoreWindow(this); else SystemCommands.MaximizeWindow(this); };
            WindowChrome.SetIsHitTestVisibleInChrome(max, true); Grid.SetColumn(max, 2);
            StateChanged += (s, e) => { bool m = WindowState == WindowState.Maximized; max.Content = m ? "\uE923" : "\uE922"; max.ToolTip = m ? "Restore" : "Maximize"; System.Windows.Automation.AutomationProperties.SetName(max, m ? "Restore" : "Maximize"); };
            bar.Children.Add(min); bar.Children.Add(max); bar.Children.Add(close);
            return bar;
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
            var c = kind == "ok" ? "B.Ok" : kind == "bad" ? "B.Red3" : kind == "warn" ? "B.Warn" : "B.Line2";
            return new Border { BorderBrush = Res<Brush>(c), BorderThickness = new Thickness(3, 0, 0, 0), Background = Res<Brush>("B.Surface2"), CornerRadius = new CornerRadius(0, 6, 6, 0), Padding = new Thickness(14, 10, 14, 10), Margin = new Thickness(0, 8, 0, 10), Child = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, FontSize = 13, LineHeight = 19 } };
        }
        WrapPanel Buttons(params UIElement[] b) { var p = new WrapPanel { Margin = new Thickness(0, 6, 0, 0) }; foreach (var x in b) if (x != null) p.Children.Add(x); return p; }
        /// <summary>shows a page: its tag names it for Escape and the status strip; a page with unsaved changes sets leaveCheck after this</summary>
        void Show(FrameworkElement e, string tag)
        {
            CloseTerm(false); NewTermScope(e); e.Tag = tag; page.Content = e; scroller?.ScrollToTop(); leaveCheck = null; settingsPending = null; settingsRefresh = null;
            if (tag != null && TagSection.TryGetValue(tag, out var own) && own != section) Enter(own);   // the side column follows the page
            Dispatcher.BeginInvoke(new Action(UpdateChrome), System.Windows.Threading.DispatcherPriority.Background);   // after the caller has started its worker
        }

        /// <summary>leaves a console session before its result (a review's Cancel, a check-up's Done): the worker quits without writing, and
        /// the section's first page (or the one named) opens</summary>
        void Leave(string name = null)
        {
            if (worker != null && worker.Running) { quitting = true; worker.Cancel(); worker.Send(new JObj { { "cmd", "quit" } }); }
            if (browsing?.Live == true) { browsing.Dispose(); browsing = null; }
            Go(name ?? section);
        }
        static string Size(long b) => Units.Gb(b);
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
                foreach (var e in list) lb.Items.Add(new ListBoxItem { Content = HistoryRow(e), Tag = e, Padding = new Thickness(0), Margin = new Thickness(0, 0, 0, 6), HorizontalContentAlignment = HorizontalAlignment.Stretch });
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

        UIElement HistoryRow(HistoryEntry e)
        {
            var g = new Grid(); g.ColumnDefinitions.Add(new ColumnDefinition()); g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var left = new StackPanel { Margin = new Thickness(0, 0, 12, 0) };
            left.Children.Add(T(e.When, 14, true, false, new Thickness(0, 0, 0, 2)));
            // the console and size only: what the console's startup software was is shown when the backup is reviewed or opened
            var bits = new List<string>(); if (e.ConsoleTag != null) bits.Add((e.Simulated ? "Simulated console " : "Console ") + e.ConsoleTag); if (e.Bytes > 0) bits.Add(Size(e.Bytes));
            left.Children.Add(T(string.Join("  ·  ", bits), 12.5, false, true, new Thickness(0)));
            if (e.Status == BackupStatus.Verified && e.TwoCopies && e.SameDisk) left.Children.Add(T("Both copies are on the same drive: that protects against a damaged file, not against losing the drive.", 12, false, true, new Thickness(0, 2, 0, 0)));
            if (e.Problem != null && e.Status != BackupStatus.Verified) left.Children.Add(T(e.Problem.Length > 200 ? e.Problem.Substring(0, 200) + "…" : e.Problem, 12, false, true, new Thickness(0, 2, 0, 0)));
            g.Children.Add(left);
            var kind = e.Status == BackupStatus.Verified ? "B.Ok" : e.Status == BackupStatus.NeedsVerification ? "B.Warn" : "B.Red3";
            // the Dashboard's and the Backup Browser's words for the same states
            var said = e.Status == BackupStatus.Verified ? (e.TwoCopies ? "Complete · checked, with an additional copy" : "Complete · checked") : e.Status == BackupStatus.NeedsVerification ? "Complete · not checked yet"
                     : e.Status == BackupStatus.Incomplete ? "Incomplete: not a backup" : e.Status == BackupStatus.Damaged ? "Damaged" : e.StatusText;
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
            what.Children.Add(T(profile.ConsoleTasks ? "One file of about 1.0 GB in your backup location:" : "One file in your backup location:", Ty.Text));
            what.Children.Add(PathText(settings.PrimaryFolder));
            if (string.IsNullOrEmpty(settings.ArchiveFolder)) what.Children.Add(T("Additional copy not configured.", Ty.Text, false, true));
            else { what.Children.Add(T("An additional copy goes to:", Ty.Text)); what.Children.Add(PathText(settings.ArchiveFolder)); }
            if (!string.IsNullOrEmpty(settings.ArchiveFolder) && Durable.VolumeId(settings.ArchiveFolder) == Durable.VolumeId(settings.PrimaryFolder))
                what.Children.Add(Note("The additional copy is on the same drive. That protects against a damaged file, but not against losing the drive or the PC. You can choose another drive in Settings.", "warn"));
            what.Children.Add(Buttons(B("Backup Locations…", "Btn.Ghost", () => Go("Settings"))));
            what.Children.Add(Details(TechNes, "The file is a ZIP: the startup software and each part of the built-in storage as images (restore/), the original Nintendo kernel when it is found (recovery/), the raw NAND image, bootloaders and storage table kept as evidence and never written to a console (reference/), the list of files on the data storage (inventory/), and a manifest with every part's size and checksums.",
                "While it is made, the file is written under a temporary name; it is read back in full and checked against what the console sent before it gets its final name. An unfinished file is kept as Incomplete and is never used as a backup."));
            sp.Children.Add(Card(what));
            if (string.IsNullOrEmpty(opt.SimulatedConsole) || !profile.ConsoleTasks) sp.Children.Add(ConnectSteps("A backup only reads the console's storage."));
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
            if (!FamilyHasTasks()) return;
            if (!driverReady && string.IsNullOrEmpty(opt.SimulatedConsole)) { WithDriver(() => StartBackup()); return; }
            if (ConsoleLock.IsBusy()) { Show(Message("Another console task is running", "The console is already in use by this window, another CCS Console Workbench window or Classic Cart Studio. Wait for that task to finish, then try again.", "bad"), "msg"); return; }
            progress = new ProgressView(this, "Backing Up the Console", "Keep the USB cable connected. Cancel stops at once: nothing on the console changes, and the unfinished file is kept and marked Incomplete.", "Cancel", () => { worker?.Cancel(); progress.Say("Canceling..."); });
            Show(progress.Panel, "progress");
            NewSession("backup");
            worker.Send(ConsoleReq("backup"));
        }

        // ------------------------------------------------------------------ worker events
        Dictionary<string, object> lastPlan;
        void OnWorkerEvent(Dictionary<string, object> ev)
        {
            if (quitting) { if (ev.S("event") == "result") gotResult = true; return; }
            switch (ev.S("event"))
            {
                case "progress": progress?.Update(ev.S("text"), ev.L("done", 0), ev.L("total", 0)); if (ev.S("phase") == "write" || ev.S("phase") == "prepare") writing = true; break;
                case "plan": lastPlan = ev.O("plan"); ShowPlan(lastPlan); break;
                case "prep-plan": ShowPrepPlan(ev.O("plan")); break;
                case "return-plan": ShowReturnPlan(ev.O("plan")); break;
                case "result": gotResult = true; writing = false; if (workerOp == "backup") BackupResult(ev); else if (workerOp == "prepare") PrepResult(ev); else if (workerOp == "return") ReturnResult(ev); else if (workerOp == "rescue") RescueResult(ev); else if (workerOp == "browse") { if (browsing?.Live == true) { browsing.Dispose(); browsing = null; } ShowCheckup(ev, ended: true); } else if (workerOp == "compose") ComposeResult(ev); else if (workerOp == "home-design") DesignResult(ev); else if (workerOp == "method" || workerOp == "firmware") MethodOrFirmwareResult(workerOp, ev); else if (workerOp == "test-boot") TestBootResult(ev); else if (workerOp == "files") { worker?.Send(new JObj { { "cmd", "quit" } }); Go(filesScope == "dev" ? "Developer Mode" : "Console Browser"); } else RestoreResult(ev); break;
                case "finished": Show(Message("The console is restarting", "It starts into its normal HOME in a moment. You can unplug the USB cable once the TV shows the HOME.", "ok"), "msg"); worker?.Send(new JObj { { "cmd", "quit" } }); break;
                case "error": if (workerOp == "files") ShowFilesSession(ev.S("text"), "bad"); else Show(Message("The console task stopped with a problem", ev.S("text") ?? "", "bad", ev.S("code")), "msg"); break;
                default: OnWorkbenchEvent(ev); break;
            }
        }

        bool gotResult;
        void OnWorkerExit(int code)
        {
            var w = worker; worker = null; var wasWriting = writing; var left = quitting; writing = false; quitting = false; w?.Dispose();
            UpdateChrome();
            if (closeWhenDone) { closeWhenDone = false; Close(); return; }
            if (left) return;
            if (!gotResult && (page.Content as FrameworkElement)?.Tag as string == "progress")
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
                c.Children.Add(T("Console " + r.S("console_tag") + "  ·  " + (r.S("state_text") ?? ""), Ty.Body));
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
            foreach (var e in list) lb.Items.Add(new ListBoxItem { Content = HistoryRow(e), Tag = e.PrimaryPath ?? e.ArchivePath, Margin = new Thickness(0, 0, 0, 6), HorizontalContentAlignment = HorizontalAlignment.Stretch });
            if (list.Count == 0) sp.Children.Add(Card(T("No complete backups in your backup locations. Choose Browse for a Backup File to use one saved somewhere else.", Ty.Body, false, true))); else sp.Children.Add(lb);
            var next = B("Review This Backup", "Btn.Primary", () => { if (lb.SelectedItem is ListBoxItem it) ShowRestoreReview(it.Tag as string); }, false);
            lb.SelectionChanged += (s, e) => next.IsEnabled = lb.SelectedItem != null;
            sp.Children.Add(Buttons(next, B("Browse for a Backup File…", "Btn", () => { var d = new Microsoft.Win32.OpenFileDialog { Filter = FileKinds.ConsoleBackupFilter, Title = "Choose a console backup" }; if (d.ShowDialog(this) == true) ShowRestoreReview(d.FileName); }), B("Back", "Btn.Ghost", BackToSection)));
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
                if (string.IsNullOrEmpty(opt.SimulatedConsole) || !profile.ConsoleTasks) sp.Children.Add(ConnectSteps("Nothing is written until you confirm what changes."));
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
            if (!FamilyHasTasks()) return;
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

        // ------------------------------------------------------------------ Prepare Console (Classic Cart Studio only)
        void ShowPrepare()
        {
            var sp = PageHead("Prepare Console", "Set your " + Con + " up for Classic Cart Studio's HOME. This changes the console's startup software and bootloader and adds files; you review every step before anything is written.");
            var c = new StackPanel();
            c.Children.Add(T("What this does", Ty.Heading, true));
            c.Children.Add(T("Workbench installs the software Classic Cart Studio's HOME runs on. No other program is needed. Nintendo's HOME, its built-in games and their saves stay. A console that is already prepared this way is left as it is.", Ty.Text));
            c.Children.Add(T("Before anything changes", Ty.Heading, true, false, new Thickness(0, 10, 0, 6)));
            c.Children.Add(T("You need a checked backup of this console as it is now, before it is prepared: make it with Back Up Console. When you prepare, Workbench backs the console up once more in the same session, with every save; that backup is your way back (Return to Original Software). You review the exact steps and confirm them. If a step fails, the steps after it are not done, and nothing is retried.", Ty.Text));
            c.Children.Add(Details(TechNes, "What is installed: Hakchi2 CE 3.9.3's kernel and files, with CCS HOME's package and the start method you choose below (CCS Mod for Hakchi or CCS Nucleus).", "As Hakchi2 CE's own installation does, hakchi's bootloader (u-boot) replaces the console's own and the startup software (kernel) is replaced by hakchi's; a copy of the original Nintendo kernel is kept in the spare startup slot, and the originals stay in your backups. Return to Original Software puts this console's own bootloader and Nintendo's kernel back."));
            sp.Children.Add(Card(c));
            sp.Children.Add(PolicyChoice());
            if (string.IsNullOrEmpty(opt.SimulatedConsole) || !profile.ConsoleTasks) sp.Children.Add(ConnectSteps("Nothing is written until you confirm the steps."));
            sp.Children.Add(Buttons(B("Check the Console", "Btn.Primary", () => StartPrepare()), B("Back", "Btn.Ghost", BackToSection)));
            sp.Children.Add(Group("Already prepared?", 18));
            sp.Children.Add(Card(Inline("Return to Original Software undoes the preparation: it puts back Nintendo's startup software and this console's own bootloader, removes hakchi's files and restores the original storage layout (as Hakchi2 CE's own uninstall does), then restores the backup made before preparing, with its games and saves exactly as they were.", B("Return to Original Software…", "Btn.Ghost", () => ShowReturn())), 16));
            Show(sp, "prepare");
        }

        // ------------------------------------------------------------------ Return to Original Software (Classic Cart Studio only)
        void ShowReturn()
        {
            var sp = PageHead("Return to Original Software", "Undo Classic Cart Studio's preparation of your " + Con + ". This changes the console's startup software, bootloader and storage; you review every step first.");
            var c = new StackPanel();
            c.Children.Add(T("What this does", Ty.Heading, true));
            c.Children.Add(T("It works in two parts. First, in this session, Workbench puts back Nintendo's startup software and this console's original bootloader, removes hakchi's files and restores the original storage layout. Then, after the console restarts, Restore a Backup puts back the backup made before preparing, so the console's storage is exactly as it was then.", 13));
            c.Children.Add(T("Before anything changes", 15, true, false, new Thickness(0, 10, 0, 6)));
            c.Children.Add(T("This works only on a console exactly as Classic Cart Studio's preparation left it; anything else is refused. Workbench first backs the console up as it is now, so saves made since preparing are kept. You review the steps and confirm them. If a step fails, the steps after it are not done, and nothing is retried.", 13));
            sp.Children.Add(Card(c));
            if (string.IsNullOrEmpty(opt.SimulatedConsole) || !profile.ConsoleTasks) sp.Children.Add(ConnectSteps("Nothing is written until you confirm the steps."));
            sp.Children.Add(Buttons(B("Check the Console", "Btn.Primary", () => StartReturn()), B("Back", "Btn.Ghost", BackToSection)));
            Show(sp, "return");
        }

        void StartReturn()
        {
            if (!FamilyHasTasks()) return;
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
            if (!FamilyHasTasks()) return;
            if (!driverReady && string.IsNullOrEmpty(opt.SimulatedConsole)) { WithDriver(() => StartPrepare()); return; }
            if (ConsoleLock.IsBusy()) { Show(Message("Another console operation is running", "Wait for it to finish, then try again.", "bad"), "msg"); return; }
            progress = new ProgressView(this, "Checking the Console", "Nothing is changed in this step.", "Cancel", () => { worker?.Cancel(); progress.Say("Canceling..."); });
            Show(progress.Panel, "progress");
            NewSession("prepare");
            var req = ConsoleReq("prepare-plan"); req.Add("policy", preparePolicy); if (preparePolicy == "mod" && prepareModStart) req.Add("mod_start", true); worker.Send(req);
        }

        void ShowPrepPlan(Dictionary<string, object> p)
        {
            var sp = PageHead("Review the Preparation", "What preparing would change on your " + Con + ". Nothing has been written yet.");
            var c = new StackPanel(); c.Children.Add(T("Console " + p.S("console_tag") + ": " + p.S("text"), 14, true));
            var steps = (p.A("steps") ?? new List<object>()).Cast<string>().ToList();
            foreach (var s in steps) c.Children.Add(T((steps.IndexOf(s) + 1) + ". " + Capital(Preparation.StepLabel(s)), 13));
            var hm = p.O("home");
            if (hm != null) c.Children.Add(T("CCS HOME's package: " + hm.S("size") + (hm.S("replaces") != null && hm.S("replaces") != "absent" ? ". The HOME the console uses now stays until the switch." : "."), 12.5, false, true, new Thickness(0, 4, 0, 0)));
            foreach (var n in (p.A("notes") ?? new List<object>())) c.Children.Add(Note(n as string, "info"));
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
            var okText = st == "prepared" ? "Every step was written once and read back. Restart the console" + (done.Contains("method") ? ": at that start Hakchi installs the method and restarts the console once; then CCS HOME is ready on it." : " and check that Nintendo's HOME starts.")
                : st == "method-scheduled" ? "CCS HOME is on the console now, checked file by file. The way it starts (CCS Mod or CCS Nucleus) is waiting in hakchi's package folder: hakchi installs it the next time the console starts normally, then restarts the console once."
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
        void Pick(TextBox box)
        {
            using (var d = new System.Windows.Forms.FolderBrowserDialog { SelectedPath = box.Text, ShowNewFolderButton = true })
                if (d.ShowDialog() == System.Windows.Forms.DialogResult.OK) box.Text = d.SelectedPath;
        }

        FrameworkElement Message(string title, string text, string kind, string details = null)
        {
            var sp = new StackPanel(); sp.Children.Add(PageTitle(title)); sp.Children.Add(Note(text, kind));
            if (!string.IsNullOrEmpty(details)) sp.Children.Add(Details("Technical details", details));
            sp.Children.Add(Buttons(B("Back", "Btn", () => { if (worker != null && worker.Running) Leave(); else BackToSection(); }))); return sp;
        }

        // ------------------------------------------------------------------ closing while an operation runs
        void OnClosing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            // the preferences first (WorkbenchPrefs.cs): a Cancel there has stopped no task and touched no driver
            if (!PrefsAtClose()) { e.Cancel = true; return; }
            closePrefsAnswered = true; settingsPending = null; if ((page.Content as FrameworkElement)?.Tag as string == "settings") leaveCheck = null;
            ClosingAfterPrefs(e);
            if (e.Cancel && !closeWhenDone) closePrefsAnswered = false;   // the window stays open: the next close asks again
        }

        void ClosingAfterPrefs(System.ComponentModel.CancelEventArgs e)
        {
            if (!Busy && (!LeaveOk() || !DesignLeaveOk())) { e.Cancel = true; return; }
            if (worker == null || !worker.Running || gotResult) { if (!DriverAtClose()) { e.Cancel = true; return; } }
            if (worker == null || !worker.Running) return;
            if (gotResult) { worker.Detach(); return; }   // the operation has ended: nothing to stop; its worker sees its input end and exits
            if (writing)
            {
                if (Ask("Keep the window open?", "A restore is writing to the console now. If you close, it stops after the part being written is finished and checked, and the console may be left partly restored.",
                        new[] { "Keep the Window Open", "Close After This Part" }, 0, 0) != 1) { e.Cancel = true; return; }
                worker.Cancel(); closeWhenDone = true; e.Cancel = true; progress?.Say("Stopping after this part, then closing..."); return;
            }
            bool backup = workerOp == "backup";
            if (Ask(backup ? "Stop the backup?" : "Stop the console task?", backup ? "Nothing on the console changes; the unfinished file is kept and marked Incomplete." : "Nothing has been written to the console.",
                    new[] { backup ? "Stop the Backup and Close" : "Stop and Close", "Keep Working" }, 1, 1) != 0) { e.Cancel = true; return; }
            worker.Cancel(); worker.Detach();
        }

        // ------------------------------------------------------------------ the progress panel
        sealed class ProgressView
        {
            public readonly StackPanel Panel = new StackPanel();
            readonly TextBlock text, bytes; readonly ProgressBar bar; readonly DateTime start = DateTime.UtcNow; readonly Button stop;
            public ProgressView(BackupWorkspace w, string title, string note, string stopText, Action onStop)
            {
                w.progressTitle = title;
                Panel.Children.Add(w.PageHead(title, note));
                text = w.T("Starting...", 14, true, false, new Thickness(0, 0, 0, 10));
                bar = new ProgressBar { Height = 8, Minimum = 0, Maximum = 1000, IsIndeterminate = true, Foreground = w.Res<Brush>("B.Red"), Background = w.Res<Brush>("B.Surface2"), BorderThickness = new Thickness(0) };
                bytes = w.T("", 12.5, false, true, new Thickness(0, 10, 0, 0));
                var box = new StackPanel(); box.Children.Add(text); box.Children.Add(bar); box.Children.Add(bytes);
                Panel.Children.Add(w.Card(box, 22));
                if (onStop != null) { stop = w.B(stopText, "Btn", onStop); Panel.Children.Add(w.Buttons(stop)); }
            }
            public void Say(string t) { text.Text = t; if (stop != null) stop.IsEnabled = false; }
            public void Update(string t, long done, long total)
            {
                if (!string.IsNullOrEmpty(t)) text.Text = t;
                var el = DateTime.UtcNow - start; var e = (int)el.TotalHours > 0 ? el.ToString(@"h\:mm\:ss") : el.ToString(@"m\:ss");
                if (total > 0) { bar.IsIndeterminate = false; bar.Value = Math.Min(1000, 1000.0 * done / total); bytes.Text = Units.Gb(done) + " of " + Units.Gb(total) + "  ·  " + e + " elapsed"; }
                else { bar.IsIndeterminate = true; bytes.Text = e + " elapsed"; }
            }
        }
    }
}
