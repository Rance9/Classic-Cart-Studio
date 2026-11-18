// Classic Cart Studio console backup and restore window. Copyright (C) 2026 Rance9. GPL-3.0-or-later.
// CCS Console Workbench: the window's frame, the same in the standalone CCS-Console-Workbench.exe and in Classic Cart Studio for Windows.
//   Side column    the wordmark (as wide as the navigation under it) and the product's name; Dashboard; the section groups Console Operations,
//                  Backup Files and Advanced (each heading larger than the sections under it); then Settings, Help and About and, in the
//                  standalone only, Get Full Classic Cart Studio (an introduction page inside this window).
//   Console strip  the console family the window works with (ConsoleProfile) and what is known about the console in front of it: "Console not
//                  checked" until an operation's own session identifies one. Choosing the family is the window's context only.
//   Status strip   what the window is doing now: no task, a task running, or a console session that is still open.
//   Dashboard      the opening page: the console's status, Back Up Console first, the three common tasks, the other tools and recent backups.
// Nothing in this file starts a console session, a USB scan, a driver check, a full backup check or a network request. A web page opens only
// when the person clicks a website button (Launch). Backup files opened offline never change what the console strip says.
using CcsBackup.Browse;
using CcsBackup.Engine;
using CcsBackup.Library;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Ellipse = System.Windows.Shapes.Ellipse;

namespace CcsBackup.Ui
{
    /// <summary>A console family the Workbench works with: its names and what the window says about it. Choosing one never connects to,
    /// identifies or checks a console; a console is identified only by an operation's own session. More families can be added here later
    /// (with their own console support); this version has one.</summary>
    public sealed class ConsoleProfile
    {
        /// <summary>Id: what settings files keep (the first family kept the id it had before its display name changed: "nes-classic-na");
        /// Aliases: other ids that mean the same family. Name: the one name people see, in both console choices.</summary>
        public string Id, Name, Short, About;
        public string[] Aliases = new string[0];
        public (string region, string name)[] RegionalNames;
        /// <summary>what About This Console and Help say about it, heading and text (paragraphs split by a blank line)</summary>
        public (string head, string text)[] Sections;
        public override string ToString() => Name;

        public static readonly ConsoleProfile NesClassicMini = new ConsoleProfile
        {
            Id = "nes-classic-na", Name = "NES Classic Mini (2018 Release) — North America / Europe", Short = "NES Classic",
            About = "Nintendo's small NES console with 30 built-in games, sold in North America and in Europe. This choice is named for its 2018 release: Workbench's tests on a real console will use a North American console from that release.",
            RegionalNames = new[] { ("North America", "NES Classic Edition"), ("Europe", "Nintendo Classic Mini: Nintendo Entertainment System") },
            Sections = new[]
            {
                ("2016 and 2018", "The console first came out in 2016 and was released again in 2018. Both are small modern consoles, not the original NES from the 1980s.\n\n"
                    + "They are the same product, but not identical inside: there are different versions of its circuit board. Workbench's tests on a real console will use a genuine North American console from the 2018 release. Those tests have not been done yet, and no 2016 console and no console sold in Europe has been tested with Workbench."),
                ("What choosing it does", "Choosing this console sets the names and steps Workbench uses. It does not connect to, identify or check a console, and it never makes Workbench accept a console, a backup or a system file it would otherwise refuse.\n\n"
                    + "The year a console was sold does not tell which software it runs. When you start a console task, Workbench reads that console's own startup software and says what it found."),
                ("Startup software and system images", "A restore puts a console's own backup back on that same console; Workbench refuses a backup from another console. Some steps replace a console's startup software on purpose and say so before you confirm: preparing a console replaces its startup software and bootloader, and a change you review in Developer Mode can replace its startup software. Return to Original Software puts back Nintendo's startup software and the console's own bootloader.\n\n"
                    + "With CCS Nucleus, a Nintendo system image you supply, such as one from your own 2016 console, can be added as one of its choices. A system image is not startup software for another circuit board. Workbench checks every system image before it adds one; a system image from another version of the console has not been tested."),
                ("Other consoles", "The Super NES Classic Edition and the Famicom Mini are different consoles: Workbench does not work with their hardware."),
            },
        };
        /// <summary>the families this version supports: one. A test may add made-up ones (Probe); a release never ships one that is not supported.</summary>
        static readonly List<ConsoleProfile> catalog = new List<ConsoleProfile> { NesClassicMini };
        public static IReadOnlyList<ConsoleProfile> Supported => catalog;
        /// <summary>the family a saved id names (its id or an alias), or null when this version does not know it</summary>
        public static ConsoleProfile Find(string id) => string.IsNullOrEmpty(id) ? null : catalog.FirstOrDefault(p => p.Id == id || p.Aliases.Contains(id));
    }

    public sealed partial class BackupWorkspace
    {
        public const string ProductTitle = "CCS Console Workbench", Tagline = "Back up, restore, repair and compose your NES Classic.";
        public const string SourceUrl = "https://classiccartstudio.com/source/";
        const string FullAppEntry = "Get Full Classic Cart Studio";
        /// <summary>Owner decision, November 17, 2026: the standalone's Get Full Classic Cart Studio entry stays hidden until Classic Cart
        /// Studio is released. Its page is built and checked; setting this to true (in a release after the full app's) adds the entry to the
        /// side column. Classic Cart Studio itself never shows it.</summary>
        public static readonly bool FullAppEntryReleased = false;
        static readonly (string group, string[] items)[] Sections =
        {
            (null, new[] { "Dashboard" }),
            ("Console Operations", new[] { "Console Backup", "Console Restore", "Console Rescue", "Console Composer", "Console Browser" }),
            ("Backup Files", new[] { "Backup Browser", "Backup Repair" }),
            ("Advanced", new[] { "Developer Mode" }),
        };
        readonly Dictionary<string, Button> navButtons = new Dictionary<string, Button>();
        string section = "Dashboard";
        ConsoleProfile profile = ConsoleProfile.Supported[0];
        /// <summary>the console family's short name for sentences ("your NES Classic")</summary>
        string Con => profile.Short;

        /// <summary>opens a web page in the person's browser; only ever called from a click on a website button. A test can replace it.</summary>
        internal static Func<string, bool> Launch = url => { try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); return true; } catch (Exception) { return false; } };

        /// <summary>the open page's check before the window leaves it (unsaved changes): null when the page has none</summary>
        Func<bool> leaveCheck;
        bool LeaveOk() { var c = leaveCheck; return c == null || c(); }

        // ------------------------------------------------------------------ the side column
        const double NavSide = 14;   // the side column's left and right padding: the wordmark and the navigation rows share it

        UIElement Nav()
        {
            var sp = new StackPanel();
            sp.Children.Add(Brand());
            sp.Children.Add(new Border { Height = 1, Background = Res<Brush>("B.Line"), Margin = new Thickness(NavSide, 0, NavSide, 6) });
            var list = new StackPanel { Margin = new Thickness(NavSide, 0, NavSide, 12) };
            // the window's own pages are a group like the others: settings, help and about and (standalone only) the full app's introduction
            var own = opt.HostedInFullApp || !FullAppEntryReleased ? new[] { "Settings", "Help and About", "Credits" } : new[] { "Settings", "Help and About", "Credits", FullAppEntry };
            foreach (var (g, items) in Sections.Concat(new[] { ("Settings and Help", own) }))
            {
                if (g != null)
                {
                    var h = new TextBlock { Text = g, FontSize = Ty.NavHeading, FontWeight = FontWeights.SemiBold, Foreground = Res<Brush>("B.Ink"), Margin = new Thickness(12, 10, 0, 3) };
                    list.Children.Add(h);
                }
                // an entry without a group (Dashboard) stands at the same level as the group headings, in their size and weight
                foreach (var it in items) list.Children.Add(NavItem(it, g != null));
            }
            sp.Children.Add(list);
            var sv = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = sp, Focusable = false };
            return new Border { Width = 252, Background = Res<Brush>("B.Side"), BorderBrush = Res<Brush>("B.Line"), BorderThickness = new Thickness(0, 0, 1, 0), Child = sv };
        }

        Button NavItem(string name, bool indented)
        {
            var b = new Button { Content = name, Style = Res<Style>(indented ? "Wb.Nav" : "Wb.NavTop") };
            if (indented) { b.FontSize = Ty.NavItem; b.Padding = new Thickness(26, 7, 10, 7); }
            AutomationProperties.SetName(b, name);
            b.Click += (s, e) => Go(name); navButtons[name] = b; return b;
        }

        /// <summary>the top of the side column: the approved wordmark, scaled to the width of the navigation rows (its empty right-hand edge
        /// and top and bottom rows are left out of the frame so the visible letters line up), then the product's name</summary>
        UIElement Brand()
        {
            var sp = new StackPanel { Margin = new Thickness(NavSide, 14, NavSide, 10) };
            var mark = opt.Wordmark is BitmapSource bs ? Trimmed(bs) : opt.Wordmark;
            if (mark != null)
            {
                var img = new Image { Source = mark, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(0, 0, 0, 8), SnapsToDevicePixels = true };
                RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
                AutomationProperties.SetName(img, "Classic Cart Studio");
                sp.Children.Add(img);
            }
            sp.Children.Add(new TextBlock { Text = ProductTitle, Style = Res<Style>("T.Title"), FontSize = 15.5, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center });
            return sp;
        }

        /// <summary>the picture without its fully transparent outer rows and columns (the picture itself is unchanged)</summary>
        static ImageSource Trimmed(BitmapSource src)
        {
            try
            {
                var b = src.Format == PixelFormats.Bgra32 ? src : new FormatConvertedBitmap(src, PixelFormats.Bgra32, null, 0);
                int w = b.PixelWidth, h = b.PixelHeight, stride = w * 4; var px = new byte[stride * h]; b.CopyPixels(px, stride, 0);
                int x0 = w, x1 = -1, y0 = h, y1 = -1;
                for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) if (px[y * stride + x * 4 + 3] > 8) { if (x < x0) x0 = x; if (x > x1) x1 = x; if (y < y0) y0 = y; if (y > y1) y1 = y; }
                if (x1 < x0 || y1 < y0) return src;
                var c = new CroppedBitmap(src, new Int32Rect(x0, y0, x1 - x0 + 1, y1 - y0 + 1)); c.Freeze(); return c;
            }
            catch (Exception) { return src; }
        }

        void Mark(string name)
        {
            foreach (var kv in navButtons) kv.Value.Tag = kv.Key == name ? "on" : null;
        }

        /// <summary>marks a section as the open one without opening its first page (a Dashboard action that goes straight to a task in it)</summary>
        void Enter(string name) { section = name; Mark(name); }

        /// <summary>opens a section's first page. Not while a console operation runs (its own page stays, and the status strip says why), and
        /// not before the open page's unsaved changes are kept or let go.</summary>
        public void Go(string name)
        {
            if (Busy) { UpdateStatus("A console task is running. Finish or cancel it on this page before opening another page."); return; }
            if (!LeaveOk()) return;   // also when going back to the open section's own first page
            if (worker != null) { worker.Send(new JObj { { "cmd", "quit" } }); }
            if (worker == null && (page.Content as FrameworkElement)?.Tag as string == "progress") backgroundWork = progressTitle;
            Enter(name);
            switch (name)
            {
                case "Dashboard": ShowDashboard(); break;
                case "Console Backup": ShowBackupHome(); break;
                case "Console Restore": ShowRestoreHub(); break;
                case "Console Rescue": ShowRescue(); break;
                case "Console Composer": ShowComposer(); break;
                case "Console Browser": ShowConsoleBrowser(); break;
                case "Backup Browser": ShowBackupBrowser(); break;
                case "Backup Repair": ShowBackupRepair(); break;
                case "Developer Mode": ShowDeveloper(); break;
                case "Settings": ShowSettings(); break;
                case "Help and About": ShowHelp(); break;
                case "Credits": ShowCredits(); break;
                case FullAppEntry: ShowFullApp(); break;
            }
        }

        /// <summary>the section each page belongs to, when only one section shows it: a page marks its own section in the side column however
        /// it was reached (a Dashboard card, another section's button, a result's next step). Pages several sections share (progress,
        /// messages, results, the driver pages, the Expert/Developer file session) keep the section that opened them.</summary>
        static readonly Dictionary<string, string> TagSection = new Dictionary<string, string>
        {
            { "dashboard", "Dashboard" },
            { "backup-home", "Console Backup" }, { "backup", "Console Backup" }, { "verify", "Console Backup" },
            { "restore-hub", "Console Restore" }, { "restore-pick", "Console Restore" }, { "restore-review", "Console Restore" }, { "plan", "Console Restore" },
            { "methods", "Console Restore" }, { "return", "Console Restore" }, { "return-plan", "Console Restore" },
            { "rescue", "Console Rescue" }, { "rescue-choose", "Console Rescue" }, { "rescue-plan", "Console Rescue" },
            { "composer", "Console Composer" }, { "composer-plan", "Console Composer" }, { "compose-plan", "Console Composer" }, { "build-package", "Console Composer" },
            { "package-built", "Console Composer" }, { "apply-package", "Console Composer" }, { "plan-share", "Console Composer" }, { "plan-saved", "Console Composer" },
            { "plan-checklist", "Console Composer" }, { "firmware", "Console Composer" }, { "designer", "Console Composer" }, { "design-editor", "Console Composer" },
            { "design-share", "Console Composer" }, { "design-console", "Console Composer" }, { "design-check", "Console Composer" }, { "design-plan", "Console Composer" },
            { "prepare", "Console Composer" }, { "prep-plan", "Console Composer" },
            { "console-browser", "Console Browser" }, { "checkup", "Console Browser" },
            { "backup-browser", "Backup Browser" }, { "browse", "Backup Browser" }, { "browse-damaged", "Backup Browser" }, { "export-result", "Backup Browser" },
            { "backup-repair", "Backup Repair" }, { "repair-routes", "Backup Repair" }, { "repair-result", "Backup Repair" }, { "supply", "Backup Repair" },
            { "parity", "Backup Repair" }, { "parity-result", "Backup Repair" },
            { "developer", "Developer Mode" }, { "developer-return", "Developer Mode" }, { "test-boot", "Developer Mode" }, { "test-boot-plan", "Developer Mode" },
            { "kernel-plan", "Developer Mode" }, { "system-plan", "Developer Mode" },
            { "settings", "Settings" }, { "about", "Help and About" }, { "help", "Help and About" }, { "credits", "Credits" }, { "ccs-intro", FullAppEntry },
        };

        /// <summary>back to the open section's first page (a task's Back, Cancel or Done)</summary>
        void BackToSection() => Go(section);

        // pages Escape may leave for their section's first page: nothing is being typed, chosen or run on them
        static readonly HashSet<string> EscapePages = new HashSet<string> { "backup", "restore-pick", "restore-review", "result", "msg", "verify", "prepare", "return", "files-intro", "methods", "firmware", "designer", "repair-routes", "supply", "developer-return", "driver", "driver-setup", "plan-checklist", "package-built", "plan-saved", "apply-package", "design-check", "design-console", "browse-damaged", "repair-result", "parity-result" };
        void OnEscape(System.Windows.Input.KeyEventArgs e)
        {
            var tag = (page.Content as FrameworkElement)?.Tag as string;
            if (worker != null || tag == null || !EscapePages.Contains(tag)) return;   // Go asks about unsaved changes
            Go(section); e.Handled = true;
        }

        // ------------------------------------------------------------------ the console strip (top of the page column)
        readonly TextBlock consoleState = new TextBlock(); readonly Ellipse consoleDot = new Ellipse { Width = 8, Height = 8, Margin = new Thickness(0, 1, 8, 0), VerticalAlignment = VerticalAlignment.Center };
        ComboBox consoleChoice;
        readonly TextBlock consoleEarlier = new TextBlock();
        /// <summary>a console an operation's own session identified: its label, when, and whether it was simulated</summary>
        sealed class ConsoleSeen { public string Tag; public DateTime At; public bool Simulated; }
        /// <summary>the latest operation session (one per worker; 0 before the first), what that session's own worker identified (null until
        /// it does) and, kept apart as history only, the console an earlier session identified. An earlier session's console is never shown
        /// as the current session's.</summary>
        int consoleSession; ConsoleSeen seenNow, seenEarlier;

        UIElement ConsoleStrip()
        {
            // the choice and the status side by side; the status moves to a second line when the window is narrow
            var g = new Grid { Background = Res<Brush>("B.Bar") };
            g.ColumnDefinitions.Add(new ColumnDefinition()); g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var wrap = new WrapPanel { Margin = new Thickness(32, 4, 12, 4), VerticalAlignment = VerticalAlignment.Center };
            var pick = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 20, 4) };
            var label = new TextBlock { Text = "Console", Style = Res<Style>("T.Muted"), FontSize = Ty.Small, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
            pick.Children.Add(label);
            consoleChoice = new ComboBox { MinWidth = 270, VerticalAlignment = VerticalAlignment.Center };
            foreach (var p in ConsoleProfile.Supported) consoleChoice.Items.Add(new ComboBoxItem { Content = p.Name, Tag = p });
            ShowProfileInStrip(); if (consoleChoice.SelectedIndex < 0) consoleChoice.SelectedIndex = 0;
            AutomationProperties.SetName(consoleChoice, "Console"); AutomationProperties.SetLabeledBy(consoleChoice, label);
            consoleChoice.SelectionChanged += (s, e) => OnStripProfileChanged();   // ApplyProfile: the one guarded change
            pick.Children.Add(consoleChoice); wrap.Children.Add(pick);
            var state = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 4, 20, 4) };
            consoleState.FontSize = Ty.Text; consoleState.VerticalAlignment = VerticalAlignment.Center;
            state.Children.Add(consoleDot); state.Children.Add(consoleState);
            AutomationProperties.SetName(state, "Console status"); AutomationProperties.SetLiveSetting(consoleState, AutomationLiveSetting.Polite);
            wrap.Children.Add(state);
            consoleEarlier.FontSize = Ty.Small; consoleEarlier.Foreground = Res<Brush>("B.Muted"); consoleEarlier.VerticalAlignment = VerticalAlignment.Center; consoleEarlier.Margin = new Thickness(0, 4, 0, 4);
            AutomationProperties.SetLiveSetting(consoleEarlier, AutomationLiveSetting.Polite);
            wrap.Children.Add(consoleEarlier);
            g.Children.Add(wrap);
            // Help on This Page (also F1): the open page's Help topic in the Help window, beside the task; About This Console: its own window
            var links = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 24, 0), VerticalAlignment = VerticalAlignment.Center };
            var help = new Button { Content = "Help on This Page", Style = Res<Style>("Btn.Link"), Margin = new Thickness(0, 0, 22, 0), VerticalAlignment = VerticalAlignment.Center, ToolTip = "F1" };
            help.Click += (s, e) => OpenPageHelp(); AutomationProperties.SetName(help, "Help on This Page"); AutomationProperties.SetAcceleratorKey(help, "F1");
            var about = new Button { Content = "About This Console", Style = Res<Style>("Btn.Link"), VerticalAlignment = VerticalAlignment.Center };
            about.Click += (s, e) => OpenAboutConsole(); AutomationProperties.SetName(about, "About This Console");
            links.Children.Add(help); links.Children.Add(about);
            Grid.SetColumn(links, 1); g.Children.Add(links);
            var border = new Border { BorderBrush = Res<Brush>("B.Line"), BorderThickness = new Thickness(0, 0, 0, 1), Child = g };
            UpdateConsoleStrip();
            return border;
        }

        /// <summary>the console strip's status: only what the latest operation's own session identified, with when, and whether that session
        /// is still open; never "connected" from a remembered session. A new session starts unidentified; a console an earlier session
        /// identified is shown apart, marked as earlier.</summary>
        void UpdateConsoleStrip()
        {
            bool open = worker != null && worker.Running;
            consoleState.Text = ConsoleStatusText(out var earlier);
            consoleEarlier.Text = earlier ?? ""; consoleEarlier.Visibility = earlier == null ? Visibility.Collapsed : Visibility.Visible;
            consoleState.Foreground = Res<Brush>(seenNow == null ? "B.Muted" : "B.Ink");
            consoleDot.Fill = seenNow != null && open ? Res<Brush>("B.Ok") : null;
            consoleDot.Stroke = Res<Brush>(seenNow != null && open ? "B.Ok" : "B.Muted"); consoleDot.StrokeThickness = 1.5;
            if (consoleChoice != null) consoleChoice.IsEnabled = !Busy;
        }

        static string Seen(ConsoleSeen s) => (s.Simulated ? "Simulated console " : "Console ") + s.Tag + ", identified at " + s.At.ToString("h:mm tt", En);

        /// <summary>the latest session's console status in words; and, while that session has not identified its console, the console an
        /// earlier session identified, marked as earlier (null when there is none to show)</summary>
        string ConsoleStatusText(out string earlier)
        {
            bool open = worker != null && worker.Running;
            earlier = seenNow == null && seenEarlier != null ? "Earlier session: " + Seen(seenEarlier) : null;
            if (seenNow != null) return Seen(seenNow) + (open ? " · session open" : " · session ended");
            if (open) return "Console not identified yet · session open";
            return consoleSession == 0 ? "Console not checked" : "The last session ended before identifying a console";
        }

        /// <summary>a worker event naming the console the latest session is working with (the connected console: a check-up, a backup's
        /// result, a diagnosis or an operation's plan). NewSession passes only the latest session's events here. Backup files opened
        /// offline never come here.</summary>
        void NoteConsole(Dictionary<string, object> ev)
        {
            var tag = ev.S("console_tag") ?? ev.O("plan")?.S("console_tag") ?? ev.O("checkup")?.S("console_tag");
            if (string.IsNullOrEmpty(tag)) return;
            seenNow = new ConsoleSeen { Tag = tag, At = DateTime.Now, Simulated = !string.IsNullOrEmpty(opt.SimulatedConsole) };
            UpdateConsoleStrip();
        }

        /// <summary>starts an operation's worker: a new console session. Its console starts unidentified (a console an earlier session
        /// identified is kept as history only), and its events and exit are its own: a superseded session's late event or exit (its worker
        /// was told to quit when the person left it) never names the current session's console or changes its page or status.</summary>
        WorkerClient NewSession(string op)
        {
            if (seenNow != null) seenEarlier = seenNow;
            seenNow = null; int session = ++consoleSession;
            gotResult = false; writing = false; quitting = false; workerOp = op;
            var w = new WorkerClient(opt.WorkerExe, Args(), Dispatcher);
            w.Event += ev => { if (session != consoleSession) return; NoteConsole(ev); OnWorkerEvent(ev); };
            w.Exited += code => { if (session != consoleSession) { w.Dispose(); return; } OnWorkerExit(code); };
            worker = w; UpdateConsoleStrip();
            return w;
        }

        // ------------------------------------------------------------------ the status strip (bottom of the window)
        readonly TextBlock statusText = new TextBlock(); string progressTitle;
        /// <summary>a task on this PC (a file check, an export, a repair) still running after the person left its progress page</summary>
        string backgroundWork;

        UIElement StatusStrip()
        {
            var g = new Grid { Background = Res<Brush>("B.Bar") }; g.ColumnDefinitions.Add(new ColumnDefinition()); g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            g.Children.Add(new Border { Height = 1, VerticalAlignment = VerticalAlignment.Top, Background = Res<Brush>("B.Line") }); Grid.SetColumnSpan(g.Children[0], 2);
            statusText.Margin = new Thickness(22, 8, 16, 8); statusText.FontSize = Ty.Fine; statusText.Style = Res<Style>("T.Muted"); statusText.TextWrapping = TextWrapping.Wrap; statusText.VerticalAlignment = VerticalAlignment.Center;
            AutomationProperties.SetLiveSetting(statusText, AutomationLiveSetting.Polite);   // its own words are its name for screen readers
            g.Children.Add(statusText);
            if (!string.IsNullOrEmpty(opt.SimulatedConsole)) { var sim = Pill("Simulated console", "B.Warn"); sim.Margin = new Thickness(0, 6, 22, 6); Grid.SetColumn(sim, 1); g.Children.Add(sim); }
            return g;
        }

        /// <summary>the status strip and the side column after a page change: what is running, and which sections can be opened now</summary>
        void UpdateChrome()
        {
            bool busy = Busy;
            foreach (var kv in navButtons) kv.Value.IsEnabled = !busy || kv.Key == section;
            UpdateStatus(null); UpdateConsoleStrip();
        }

        void UpdateStatus(string say)
        {
            var tag = (page.Content as FrameworkElement)?.Tag as string;
            string text;
            if (say != null) { text = say; backgroundWork = null; }
            else if (backgroundWork != null && !(worker != null && worker.Running)) text = "Still working on this PC: " + backgroundWork + ". You can keep using Workbench; this line says when it finishes.";
            else if (worker != null && worker.Running && !gotResult) text = "Working: " + (tag == "progress" && progressTitle != null ? progressTitle : "a console task") + ". Other pages open again when it finishes or is canceled.";
            else if (worker != null && worker.Running) text = "The console session is still open: the console waits in its memory program. Restart the Console or Done ends it.";
            else if (tag == "progress") text = "Working: " + (progressTitle ?? "a task on this PC") + ".";
            else text = "No task running.";
            statusText.Text = text;
        }

        // ------------------------------------------------------------------ Dashboard
        void ShowDashboard()
        {
            settings = Reload();
            var sp = PageHead("Dashboard", Tagline);
            // the console's status and the first thing to do, side by side when there is room
            var status = new StackPanel();
            status.Children.Add(T("Your console", Ty.Small, true, true, new Thickness(0, 0, 0, 4)));
            status.Children.Add(T(profile.Name, Ty.CardTitle, true, false, new Thickness(0, 0, 0, 8)));
            var st = Pill(ConsoleStatusText(out var earlierSeen), seenNow == null ? "B.Muted" : "B.Ink2");
            st.Margin = new Thickness(0, 0, 0, earlierSeen == null ? 10 : 4); status.Children.Add(st);
            if (earlierSeen != null) status.Children.Add(T(earlierSeen, Ty.Small, false, true, new Thickness(0, 0, 0, 10)));
            status.Children.Add(T("Workbench connects to a console only when you start a console task and follow its steps. Backups already on this PC can be browsed, checked and repaired without a console or a USB driver.", Ty.Text, false, true, new Thickness(0, 0, 0, 10)));
            var about = new Button { Content = "About This Console", Style = Res<Style>("Btn.Link"), HorizontalAlignment = HorizontalAlignment.Left }; about.Click += (s, e) => OpenAboutConsole(); AutomationProperties.SetName(about, "About This Console"); status.Children.Add(about);
            var first = new StackPanel();
            first.Children.Add(T("Start here", Ty.Small, true, true, new Thickness(0, 0, 0, 4)));
            first.Children.Add(T("Back Up Console", Ty.CardTitle, true, false, new Thickness(0, 0, 0, 8)));
            first.Children.Add(T("Make a complete, checked copy of your " + Con + "'s built-in storage on this PC: its games, saves, settings and everything else on it, including other programs' files. A backup only reads the console; nothing on it changes.", Ty.Text, false, true, new Thickness(0, 0, 0, 14)));
            var bb = B("Back Up Console", "Btn.Primary", () => { if (LeaveOk()) { Enter("Console Backup"); ShowBackup(); } }); bb.Margin = new Thickness(0);
            first.Children.Add(bb);
            sp.Children.Add(CardGrid(320, 2, Card(status), Card(first)));

            sp.Children.Add(CardGrid(230, 3,
                ActionCard("Browse a Backup", "Look through a backup on this PC by game, by type or as stored, and copy files out of it. No console needed.", "Btn", () => Go("Backup Browser")),
                ActionCard("Restore a Backup", "Put a backup back on the console it came from. You review every change before anything is written, and the console is backed up first.", "Btn", () => { Enter("Console Restore"); ShowRestorePick(); }),
                ActionCard("Check the Console", "A check-up that only reads: whether the console's startup software is recognized, what is installed and how much space is free.", "Btn", () => Go("Console Browser"))));

            sp.Children.Add(Group("More tools"));
            var more = new StackPanel();
            more.Children.Add(ToolRow("Repair a Backup File", "Check a backup file on this PC and make a repaired copy. The original file is never changed.", () => Go("Backup Repair")));
            more.Children.Add(ToolRow("HOME Designer", "Design CCS HOME with your own pictures, colors, folders and game order. Part of Console Composer.", () => { Enter("Console Composer"); ShowDesigner(); }));
            more.Children.Add(ToolRow("Set Up or Rebuild a Console", "Console Composer: start fresh with Classic Cart Studio, set a console up like another one, or rebuild it from your backups. Make a backup first.", () => Go("Console Composer"), last: true));
            sp.Children.Add(Card(more, 8));

            var rh = new Grid { Margin = new Thickness(0, 8, 0, 10) }; rh.ColumnDefinitions.Add(new ColumnDefinition()); rh.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var rt = Group("Recent backups", 0); rt.Margin = new Thickness(0); rt.VerticalAlignment = VerticalAlignment.Center; rh.Children.Add(rt);
            var all = B("See All Backups", "Btn.Ghost", () => Go("Backup Browser")); all.Margin = new Thickness(0); Grid.SetColumn(all, 1); rh.Children.Add(all);
            sp.Children.Add(rh);
            sp.Children.Add(RecentBackups());
            Show(sp, "dashboard");
        }

        /// <summary>one of the Dashboard's other tools: its name and what it does, with an Open button</summary>
        UIElement ToolRow(string title, string text, Action go, bool last = false)
        {
            var g = new Grid { Margin = new Thickness(12, 10, 12, 10) }; g.ColumnDefinitions.Add(new ColumnDefinition()); g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var left = new StackPanel { Margin = new Thickness(0, 0, 16, 0), VerticalAlignment = VerticalAlignment.Center };
            left.Children.Add(T(title, Ty.Body, true, false, new Thickness(0, 0, 0, 2))); left.Children.Add(T(text, Ty.Small, false, true, new Thickness(0)));
            g.Children.Add(left);
            var b = B("Open", "Btn", go); b.Margin = new Thickness(0); b.VerticalAlignment = VerticalAlignment.Center; AutomationProperties.SetName(b, "Open " + title); Grid.SetColumn(b, 1); g.Children.Add(b);
            return new Border { BorderBrush = Res<Brush>("B.Line"), BorderThickness = new Thickness(0, 0, 0, last ? 0 : 1), Child = g };
        }

        /// <summary>the newest backups in the backup locations, one row per backup: when it was made, its console, its state as the list knows
        /// it, and its last passed file check (never a new check: the list reads names, records and the small description in each file)</summary>
        UIElement RecentBackups()
        {
            var sp = new StackPanel();
            List<BackupListEntry> list;
            try { list = BackupList.List(settings); }
            catch (Exception ex)
            {
                sp.Children.Add(Note("Your backup location could not be read, so Workbench cannot list its backups. This doesn't mean you have none. If it is on a drive that is not connected, connect it and try again, or choose another location in Settings.", "bad"));
                sp.Children.Add(Details("Technical details", "Location: " + settings.PrimaryFolder, "What Windows reported: " + ex.Message));
                sp.Children.Add(Buttons(B("Try Again", "Btn", () => ShowDashboard()), B("Backup Locations", "Btn.Ghost", () => Go("Settings"))));
                return sp;
            }
            if (list.Count == 0)
            {
                var c = new StackPanel();
                c.Children.Add(T("No backups yet", Ty.Heading, true));
                bool exists = false; try { exists = Directory.Exists(settings.PrimaryFolder); } catch (Exception) { }
                c.Children.Add(T("To make your first backup, choose Back Up Console and follow its steps. Already have a console backup on this PC? Open it to look through it, or change your backup location in Settings.", Ty.Text, false, true));
                if (!exists) c.Children.Add(T("Your backup location does not exist yet; Workbench creates it with your first backup. If it is on a drive that is not connected, connect the drive and choose Try Again.", Ty.Small, false, true));
                var pt = PathText(settings.PrimaryFolder); c.Children.Add(pt);
                c.Children.Add(Buttons(B("Open a Backup File…", "Btn", OpenBackupFile), B("Backup Locations", "Btn.Ghost", () => Go("Settings")), exists ? null : B("Try Again", "Btn.Ghost", () => ShowDashboard())));
                sp.Children.Add(Card(c));
                return sp;
            }
            // one row per backup file, except that a backup's second copy is noted on its first copy's row instead of a row of its own
            var rows = new List<BackupListEntry>();
            foreach (var e in list)
            {
                if (e.Copy == "Second copy" && e.BackupId != null && list.Any(x => x.Copy == "First copy" && x.BackupId == e.BackupId)) continue;
                rows.Add(e);
                if (rows.Count == 4) break;
            }
            foreach (var e in rows)
            {
                var second = list.FirstOrDefault(x => x != e && x.BackupId == e.BackupId && e.BackupId != null && x.Copy == "Second copy");
                sp.Children.Add(RecentRow(e, second));
            }
            sp.Children.Add(T("A passed file check means the file was intact when it was checked. It is not a test of restoring it to a console.", Ty.Fine, false, true, new Thickness(0, 2, 0, 0)));
            return sp;
        }

        UIElement RecentRow(BackupListEntry e, BackupListEntry second)
        {
            var row = new StackPanel();
            var top = new Grid(); top.ColumnDefinitions.Add(new ColumnDefinition()); top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var who = new StackPanel { Margin = new Thickness(0, 0, 12, 0) };
            who.Children.Add(T(e.MadeUtc?.ToLocalTime().ToString("dddd, MMMM d, yyyy 'at' h:mm tt", En) ?? "Date not known", Ty.Body, true, false, new Thickness(0, 0, 0, 2)));
            var bits = new List<string>(); if (e.ConsoleTag != null) bits.Add((e.Simulated ? "Simulated console " : "Console ") + e.ConsoleTag); if (e.Bytes > 0) bits.Add(Units.Gb(e.Bytes)); if (second != null && second.Status == BrowseStatus.Complete) bits.Add("additional copy kept");
            who.Children.Add(T(string.Join("  ·  ", bits), Ty.Small, false, true, new Thickness(0)));
            who.Children.Add(T(LastCheckText(e), Ty.Small, false, true, new Thickness(0, 2, 0, 0)));
            top.Children.Add(who);
            var (pill, brush) = RowState(e); var p = Pill(pill, brush); p.VerticalAlignment = VerticalAlignment.Top; Grid.SetColumn(p, 1); top.Children.Add(p);
            row.Children.Add(top);
            var path = e.Path; var x = e;
            var acts = Buttons(e.CanOpen ? B("Browse", "Btn", () => { Enter("Backup Browser"); OpenBrowse(path, false); }) : null,
                               e.CanOpenSalvage ? B("Open What Can Be Saved", "Btn", () => { Enter("Backup Browser"); OpenBrowse(path, true); }) : null,
                               path != null && File.Exists(path) ? B("Check the File", "Btn.Ghost", () => { Enter("Backup Browser"); CheckBackup(path); }) : null);
            acts.Margin = new Thickness(0, 10, 0, -10); row.Children.Add(acts);
            var card = Card(row, 14); card.Margin = new Thickness(0, 0, 0, 8); return card;
        }

        /// <summary>a backup's state in the Dashboard's words: complete or not, and whether its file passed a check (never "restorable")</summary>
        static (string, string) RowState(BackupListEntry e)
        {
            switch (e.Status)
            {
                case BrowseStatus.Complete: return e.Check == CheckState.Verified ? ("Complete · file check passed", "B.Ok") : e.Check == CheckState.ChangedSinceCheck ? ("Changed since its last check", "B.Warn") : ("Complete · not checked yet", "B.Warn");
                case BrowseStatus.Incomplete: return ("Incomplete: not a backup", "B.Red3");
                case BrowseStatus.Damaged: return ("Damaged", "B.Red3");
                case BrowseStatus.Salvage: return ("Damaged · some files can be saved", "B.Warn");
                default: return ("File not found", "B.Red3");
            }
        }

        static string LastCheckText(BackupListEntry e)
        {
            if (e.Status == BrowseStatus.Missing) return "The file is no longer where it was saved.";
            if (e.Status == BrowseStatus.Incomplete) return "The backup did not finish; nothing in this file is used.";
            if (e.LastVerifiedUtc == null) return e.Status == BrowseStatus.Damaged || e.Status == BrowseStatus.Salvage ? "The file did not pass its last check." : "No passed file check on this PC yet.";
            return "Last passed file check: " + e.LastVerifiedUtc.Value.ToLocalTime().ToString("MMMM d, yyyy 'at' h:mm tt", En) + (e.LastVerifiedBy == "made" ? " (when it was made)" : "");
        }

        /// <summary>opens a console backup file from anywhere on this PC in the Backup Browser (checked first; nothing is written to it)</summary>
        void OpenBackupFile()
        {
            var d = new Microsoft.Win32.OpenFileDialog { Filter = FileKinds.ConsoleBackupFilter, Title = "Open a console backup" };
            if (d.ShowDialog(this) != true) return;
            Enter("Backup Browser"); OpenBrowse(d.FileName, false);
        }

        static void OpenPath(string folder)
        {
            try { if (!string.IsNullOrWhiteSpace(folder) && Directory.Exists(folder.Trim())) Process.Start("explorer.exe", "\"" + folder.Trim() + "\""); } catch (Exception) { }
        }
        static void CopyText(string s) { try { if (!string.IsNullOrEmpty(s)) Clipboard.SetText(s.Trim()); } catch (Exception) { } }

        /// <summary>small text buttons in a row (a folder's Open and Copy), lighter than a page's main buttons</summary>
        WrapPanel LinkRow(params (string text, Action click)[] links)
        {
            var p = new WrapPanel { Margin = new Thickness(0, 0, 0, 8) };
            foreach (var (text, click) in links) { var b = new Button { Content = text, Style = Res<Style>("Btn.Link"), Margin = new Thickness(0, 0, 18, 0) }; AutomationProperties.SetName(b, text); b.Click += (s, e) => click(); p.Children.Add(b); }
            return p;
        }

        // ------------------------------------------------------------------ Get Full Classic Cart Studio (the standalone only)
        void ShowFullApp()
        {
            var sp = PageHead("Classic Cart Studio", "Workbench looks after your console's backups. The full Classic Cart Studio app for Windows is where you build and play your NES game collection, and the same Workbench opens from inside it.");
            sp.Children.Add(CardGrid(260, 2,
                InfoCard("Organize your collection", "Bring in the NES game files you own, see them with their artwork and details, and arrange them in collections."),
                InfoCard("Play", "Play NES games in the app on Windows, or on classiccartstudio.com in a web browser or on a phone where the browser supports it."),
                InfoCard("Included Games", "Free games and demos whose developers gave permission, ready to play. No commercial Nintendo games are included."),
                InfoCard("Your NES Classic's HOME", "Plan collections for your " + Con + " and preview Classic Cart Studio's HOME on your PC. Putting them on a console uses guided steps with a backup first; those steps are still being tested on real consoles.")));
            var dl = new StackPanel();
            dl.Children.Add(T("Get Classic Cart Studio for Windows", Ty.Heading, true, false, new Thickness(0, 0, 0, 8)));
            dl.Children.Add(T("The button opens classiccartstudio.com/app/, the official download page, in your web browser. Nothing is downloaded or installed until you choose to there. For Windows 10 and 11.", Ty.Text, false, true));
            var problem = new ContentControl();
            var go = B("Download Classic Cart Studio", "Btn.Primary", () => { if (!Launch(opt.FullAppUrl)) problem.Content = Note("The web browser could not be opened. The address is " + opt.FullAppUrl, "warn"); });
            AutomationProperties.SetHelpText(go, opt.FullAppUrl);
            dl.Children.Add(Buttons(go)); dl.Children.Add(problem);
            dl.Children.Add(T("You don't need the full app to use Workbench: everything here works on its own.", Ty.Small, false, true, new Thickness(0, 4, 0, 0)));
            sp.Children.Add(Card(dl));
            Show(sp, "ccs-intro");
        }

        UIElement InfoCard(string title, string text)
        {
            var s = new StackPanel();
            s.Children.Add(T(title, Ty.Heading, true, false, new Thickness(0, 0, 0, 6)));
            var t = T(text, Ty.Text, false, true, new Thickness(0)); t.LineHeight = 19; s.Children.Add(t);
            var c = Card(s, 18); c.Margin = new Thickness(0); return c;
        }
    }
}
