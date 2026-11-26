// Classic Cart Studio console backup and restore window. Copyright (C) 2026 Rance9. GPL-3.0-or-later.
// CCS Console Workbench: the window's frame, the same in the standalone CCS-Console-Workbench.exe and in Classic Cart Studio for Windows.
//   Side column    the wordmark (as wide as the navigation under it; a button: About Classic Cart Studio) and the product's name; Dashboard; the section groups Console Operations,
//                  Backup Files and Advanced (each heading larger than the sections under it); then Settings, Help and About and, in the
//                  standalone only, Get Full Classic Cart Studio (an introduction page inside this window).
//   Console strip  Back and Forward at its left (WorkbenchNav.cs), then the console family the window works with (ConsoleProfile) and what
//                  is known about the console in front of it: "Console not checked" until an operation's own session identifies one, then
//                  its friendly name and short tag with the pencil that renames it. Choosing the family is the window's context only. When
//                  the window is narrow the console choice moves to a second line under the arrows (and the two links to a third when
//                  needed); nothing is cut off or covered.
//   Status strip   what the window is doing now: no task, a task running, or a console session that is still open; with Return to Task
//                  while the task's page is not in front (WorkbenchNav.cs).
//   Dashboard      the opening page: Back Up Console first, the three common tasks, the other tools and recent backups (with a refresh button
//                  beside its heading). The console's status is the console strip's alone.
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
    /// identifies or checks a console; a console is identified only by an operation's own session (its own board and region). This version
    /// has two: the NES Classic Mini and the SNES Classic Mini. Which console tasks a family has is its model's (ConsoleModel.Refusal, from the
    /// registry's capabilities); a family with ConsoleTasks false has none (TasksNotYet says why).</summary>
    public sealed class ConsoleProfile
    {
        /// <summary>Id: what settings files keep (the first family kept the id it had before its display name changed: "nes-classic-na");
        /// Aliases: other ids that mean the same family. Name: the one name people see, in both console choices.</summary>
        public string Id, Name, Short, About;
        public string[] Aliases = new string[0];
        public (string region, string name)[] RegionalNames;
        /// <summary>what About This Console and Help say about it, heading and text (paragraphs split by a blank line)</summary>
        public (string head, string text)[] Sections;
        /// <summary>whether this version has console tasks for the family; false: none starts (TasksNotYet says why). Which of them are
        /// available is the console model's (PhysicalModel: ConsoleModel.Refusal, from the registry's capabilities)</summary>
        public bool ConsoleTasks = true;
        public string TasksNotYet;
        /// <summary>the registry's physical model of this family (the names and availability Workbench shows; never what a console is: a
        /// task reads that from the console itself)</summary>
        public string PhysicalModel;
        /// <summary>a backup file's expected size from the family's own storage (its physical reference plus its storage parts and startup
        /// software), 0: the general description</summary>
        public long ArchiveBytes;
        /// <summary>the console's whole built-in storage in bytes, used and free space together (0: not given, for a family without tasks)</summary>
        public long StorageBytes;
        /// <summary>the family's own way into recovery mode, as the second of the connection steps (a task's and the test boot's)</summary>
        public string RecoveryStep, TestBootRecoveryStep;
        public override string ToString() => Name;

        /// <summary>the sentence both families' About text uses to keep a CCS Nucleus system image apart from a console's own software</summary>
        const string SystemImageSeparate = "A system image you add as a choice in CCS Nucleus is a separate thing: it is one of the menus the console can start, never the console's own startup software or its backup.";

        public static readonly ConsoleProfile NesClassicMini = new ConsoleProfile
        {
            Id = "nes-classic-na", Name = "NES Classic Mini — North America / Europe", Short = "NES Classic", PhysicalModel = "nes-classic-edition",
            StorageBytes = 536870912L,   // its NAND: 4,096 blocks of 64 pages of 2,048 bytes (ConsoleFacts.PhysicalBytes)
            About = "Nintendo's small NES console with 30 built-in games, first sold in 2016 and again in 2018, in North America and in Europe.",
            RegionalNames = new[] { ("North America", "NES Classic Edition"), ("Europe", "Nintendo Classic Mini: Nintendo Entertainment System") },
            Sections = new[]
            {
                ("2016 and 2018", "The console first came out in 2016 and was released again in 2018. Both are small modern consoles, not the original NES from the 1980s.\n\n"
                    + "They are the same product, but not identical inside: there are different versions of its circuit board."),
                ("What choosing it does", "Choosing this console sets the names and steps Workbench uses. It does not connect to, identify or check a console, and it never makes Workbench accept a console, a backup or a system file it would otherwise refuse.\n\n"
                    + "The year a console was sold does not tell which software it runs. When you start a console task, Workbench reads that console's own startup software and says what it found."),
                ("In this version", "Back Up Console and the Console Browser check-up only read the console: nothing on it changes.\n\n"
                    + "The tasks that change a console (restoring a backup, Prepare Console, Return to Original Software, Console Rescue, Console Composer, add-ons, system images, community mods, Expert Mode and Developer Mode) work with a real console. Each one first checks that it is the right console, makes or checks the backup its rules require, and shows you exactly what will change; nothing is written until you confirm. A preview version's console tasks have not all been tried on real consoles yet."),
                ("Startup software and system images", "A restore puts a console's own backup back on that same console; Workbench refuses a backup from another console. Some steps replace a console's startup software on purpose and say so before you confirm: preparing a console replaces its startup software and bootloader, and a change you review in Developer Mode can replace its startup software. Return to Original Software puts back Nintendo's startup software and the console's own bootloader.\n\n"
                    + "With CCS Nucleus, a Nintendo system image you supply, such as one from your own 2016 console, can be added as one of its choices. A system image is not startup software for another circuit board. Workbench checks every system image before it adds one and says why when it cannot use one."),
                ("Other consoles", "The SNES Classic Mini is a different console, with its own choice at the top of the window; Workbench's console tasks work with it too. Workbench never puts one kind of console's backup or startup software on a console of another kind. " + SystemImageSeparate + "\n\n"
                    + "The Famicom Mini and the Super Famicom Mini, sold in Japan, are different consoles too: Workbench does not work with their hardware."),
            },
            RecoveryStep = "Hold the console's RESET button, switch POWER on, and keep holding RESET for about 5 seconds, then let go: the console starts in recovery mode.",
            TestBootRecoveryStep = "Hold the console's RESET button, switch POWER on, and keep holding RESET for about 5 seconds, then let go. The TV stays dark: the console waits for this PC.",
        };

        public static readonly ConsoleProfile SnesClassicMini = new ConsoleProfile
        {
            Id = "snes-classic", Name = "SNES Classic Mini — North America / Europe", Short = "SNES Classic", PhysicalModel = "snes-classic-edition",
            StorageBytes = 536870912L,   // its NAND reads 2048 64 4096 like the NES Classic's: 536,870,912 bytes
            ArchiveBytes = 536870912L + 3145728L + 67108864L + 314572800L + 1048576L + 65536000L + 2 * 2736128L,   // the physical reference, its five storage parts and its startup software twice, as a North American console reported them
            About = "Nintendo's small Super NES console with 21 built-in games, sold in North America and in Europe since 2017.",
            RegionalNames = new[] { ("North America", "Super NES Classic Edition"), ("Europe", "Nintendo Classic Mini: Super Nintendo Entertainment System") },
            Sections = new[]
            {
                ("2017", "The console came out in 2017, in North America and in Europe. It is a small modern console, not the original Super NES from the 1990s.\n\n"
                    + "The North American and the European console share this one choice, but their software is not the same. Hakchi2 CE, which Workbench builds on, tells them apart by their startup software, and Workbench keeps them apart the same way: a console's software is always read from the console itself, never assumed from this choice or from its box."),
                ("In this version", "Every console task works with a North American or European SNES Classic Mini: Back Up Console and the Console Browser check-up, restoring its own backup, Prepare Console, Return to Original Software, Console Rescue, Console Composer, CCS Mod and CCS Nucleus, system images, community mods, Expert Mode and Developer Mode. Each one first reads the console's own board and region and checks that it is the right console, makes or checks the backup its rules require, and shows you exactly what will change; nothing is written until you confirm.\n\n"
                    + "Preparing it uses the same Hakchi2 CE 3.9.3 files as for the NES Classic Mini: hakchi's kernel gets the console's own key from the console itself, and its 21 games and their saves stay. Return to Original Software puts back Nintendo's original kernel for the console's region and the console's own bootloader.\n\n"
                    + "A preview version's console tasks have not all been tried on real consoles yet. A European SNES Classic Mini is handled by the same rules as a North American one, but it has not been tried.\n\n"
                    + "Backup files already on this PC can be browsed, checked and repaired under Backup Files, whichever console is chosen."),
                ("What choosing it does", "Choosing this console sets the names Workbench uses. It does not connect to, identify or check a console, it does not change a console an earlier task identified, and it never makes Workbench accept a console, a backup or a system file it would otherwise refuse."),
                ("Other consoles", "The NES Classic Mini is a different console, with its own choice at the top of the window. Workbench never puts one kind of console's backup or startup software on a console of another kind: a backup goes back only to the console it was made from, and Console Rescue writes a startup stage only from that console's own backup. " + SystemImageSeparate + "\n\n"
                    + "The Super Famicom Mini, sold in Japan, is a different console too: Workbench does not work with its hardware."),
            },
            RecoveryStep = "Hold the console's RESET button, switch POWER on, and keep holding RESET for about 5 seconds, then let go: the console starts in recovery mode.",
            TestBootRecoveryStep = "Hold the console's RESET button, switch POWER on, and keep holding RESET for about 5 seconds, then let go. The TV stays dark: the console waits for this PC.",
            TasksNotYet = null,
        };
        /// <summary>the families this version offers: the NES Classic Mini (the first: a settings file naming none gets it) and the SNES Classic
        /// Mini. A test may add made-up ones (Probe).</summary>
        static readonly List<ConsoleProfile> catalog = new List<ConsoleProfile> { NesClassicMini, SnesClassicMini };
        public static IReadOnlyList<ConsoleProfile> Supported => catalog;
        /// <summary>the family a saved id names (its id or an alias), or null when this version does not know it</summary>
        public static ConsoleProfile Find(string id) => string.IsNullOrEmpty(id) ? null : catalog.FirstOrDefault(p => p.Id == id || p.Aliases.Contains(id));
    }

    public sealed partial class BackupWorkspace
    {
        public const string ProductTitle = "CCS Console Workbench", Tagline = "Back up, restore, repair and compose your NES Classic.";
        public const string SourceUrl = "https://classiccartstudio.com/source/";
        const string FullAppEntry = "Get Full Classic Cart Studio";
        /// <summary>The standalone's Get Full Classic Cart Studio entry stays hidden until Classic Cart
        /// Studio is released. Its page is built and checked; setting this to true (in a release after the full app's) adds the entry to the
        /// side column. Classic Cart Studio itself never shows it.</summary>
        public static readonly bool FullAppEntryReleased = false;
        static readonly (string group, string[] items)[] Sections =
        {
            (null, new[] { "Dashboard" }),
            ("Console Operations", new[] { "Console Backup", "Console Restore", "Console Rescue", "Console Composer", "Console Browser" }),
            ("Backup Files", new[] { "Backup Browser", "Backup Repair", "USB and SD Backup" }),
            ("Advanced", new[] { "Developer Mode" }),
        };
        readonly Dictionary<string, Button> navButtons = new Dictionary<string, Button>();
        string section = "Dashboard";
        ConsoleProfile profile = ConsoleProfile.Supported[0];
        /// <summary>the console family's short name for sentences ("your NES Classic", "your SNES Classic")</summary>
        string Con => profile.Short;

        /// <summary>the title of the technical details that describe how this version works with the NES Classic Mini (its storage, startup
        /// stages and system format), shown on pages any family can open</summary>
        const string TechNes = "Technical details (NES Classic Mini)";

        /// <summary>every console task asks this first: true when this version has console tasks for the chosen family; otherwise the task's
        /// page says so and nothing starts (no console session, no USB driver check, no worker)</summary>
        bool FamilyHasTasks(string operation)
        {
            if (!profile.ConsoleTasks) { Show(Message("Not in this preview version yet", profile.TasksNotYet, "info"), "msg"); return false; }
            var why = CcsBackup.Device.ConsoleModels.ById(profile.PhysicalModel)?.Refusal(operation);
            if (why == null) return true;
            Show(Message("Not available for the " + profile.Short + " Mini", why + " Nothing was started: no console connection, no USB driver check, nothing written.", "info"), "msg");
            return false;
        }
        /// <summary>true for a worker request that only reads a real console (Back Up Console, the Console Browser check-up: the backup image,
        /// which has no write verb)</summary>
        internal static bool ReadOnlyRequest(string cmd) => cmd == "backup" || cmd == "browse";

        /// <summary>the connection steps' line about the PC's USB driver: with DriverChanges closed (this version) Workbench only uses a driver the
        /// PC already has; setting one up is offered only when that boundary is open</summary>
        static string DriverStepNote => CcsBackup.Gate.DriverChanges
            ? "This PC needs the console's USB driver. If Windows does not have it, Workbench asks before setting it up; it never installs a driver by itself."
            : "This PC needs a USB driver for the console. In this version Workbench uses one this PC already has, such as the one Hakchi2 CE set up; if there is none, it says so and changes nothing.";

        /// <summary>the closed physical boundary (Gate.cs) of a real console's task, as its own sentence, or null when this version opens it. It
        /// is the engine's own check (RecoverySessions.Refusal, DeveloperTestBoot.Refusal), so the window and the worker always agree. cmd: the
        /// worker request</summary>
        internal static string ClosedOnRealConsole(string cmd) => (cmd == "test-boot" ? CcsBackup.Engine.DeveloperTestBoot.Refusal()
            : CcsBackup.Device.RecoverySessions.Refusal(CcsBackup.Device.RecoverySessions.KindOf(cmd)))?.Text;

        /// <summary>every console task asks this after FamilyHasTasks: with a real console, a task whose boundary is closed in this version says
        /// so and nothing starts (no console connection, no USB driver check, no worker). A simulated console is never behind a boundary.</summary>
        bool BoundaryOpen(string cmd)
        {
            if (!string.IsNullOrEmpty(opt.SimulatedConsole)) return true;
            var closed = ClosedOnRealConsole(cmd);
            if (closed == null) return true;
            Show(Message("Not available in this version", closed + " Nothing was started: no console connection, no USB driver check, nothing written.", "info"), "msg");
            return false;
        }

        /// <summary>the console model operation a worker request is (ConsoleModel.Refusal), for a page's availability line</summary>
        static string OperationOfRequest(string cmd) => cmd == "restore-prepare" ? "restore" : cmd == "prepare-plan" ? "preparation" : cmd == "return-plan" ? "return"
            : cmd == "method-plan" ? "addons" : cmd == "firmware-plan" ? "firmware" : OperationOf(cmd);

        /// <summary>the console model operation a worker task is (ConsoleModel.Refusal)</summary>
        static string OperationOf(string op) => op == "browse" ? "identify" : op == "sega-pictures" ? "files" : op == "compose" ? "composition" : op == "method" || op == "home-design" ? "addons" : op == "test-boot" ? "developer" : op;

        /// <summary>on a console task's page, for a family without console tasks in this version: what the page cannot do yet</summary>
        UIElement FamilyNotYetNote(bool dashboard = false) => Note("Workbench's console tasks for the " + profile.Short + " Mini are being built and are not in this preview version yet"
            + (dashboard ? ". Backup files on this PC can still be browsed, checked and repaired under Backup Files." : ", so this page's console task starts nothing."), "info");

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
                    var h = new TextBlock { Text = g, FontSize = Ty.NavHeading, FontWeight = FontWeights.SemiBold, Foreground = Res<Brush>("B.Ink"), Margin = new Thickness(12, 8, 0, 3) };
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
            if (indented) { b.FontSize = Ty.NavItem; b.Padding = new Thickness(26, 6, 10, 6); }
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
                // the wordmark is a button: About Classic Cart Studio (a click, or Tab then Enter or Space). WB-UX-14: its hover area keeps
                // LogoPad of room around the letters on every side; the negative margin (that room and the 2 px ring) keeps the letters
                // exactly where and as large as before, lined up with the navigation rows. Its ring shows only with the keyboard (FocusCue).
                var img = new Image { Source = mark, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Stretch, SnapsToDevicePixels = true };
                RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
                const double LogoPad = 8, Ring = 2;
                var logo = new Button { Content = img, Style = Res<Style>("Wb.Plain"), Padding = new Thickness(LogoPad), Margin = new Thickness(-(LogoPad + Ring), -(LogoPad + Ring), -(LogoPad + Ring), 4 - (LogoPad - 2)), HorizontalAlignment = HorizontalAlignment.Stretch, ToolTip = "About Classic Cart Studio" };
                AutomationProperties.SetName(logo, "About Classic Cart Studio"); AutomationProperties.SetHelpText(logo, "Opens a window about Classic Cart Studio");
                logo.Click += (s, e) => OpenAboutStudio();
                sp.Children.Add(logo);
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

        /// <summary>opens a section's first page, also while a console task runs: the task goes on (it belongs to the window, not to its page;
        /// Return to Task brings its page back), and nothing is canceled, restarted or repeated. Not before the open page's unsaved work is
        /// kept or let go.</summary>
        public void Go(string name)
        {
            if (!LeaveOk()) return;   // also when going back to the open section's own first page
            if (PageTag == "progress" && page.Content != taskPage) backgroundWork = progressTitle;   // a task on this PC goes on; the status strip says when it ends
            RememberShown();   // the page's place in Back and Forward keeps its state before the next page's builder changes anything
            NoteLeavingTask();
            navigating++;
            try { Open(name); }
            finally { navigating--; }
        }

        void Open(string name)
        {
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
                case "USB and SD Backup": ShowStorageHome(); break;   // WorkbenchStorage.cs
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
            { "composer", "Console Composer" }, { "composer-plan", "Console Composer" }, { "sega-pictures", "Console Composer" }, { "sega-plan", "Console Composer" }, { "compose-plan", "Console Composer" }, { "build-package", "Console Composer" },
            { "package-built", "Console Composer" }, { "apply-package", "Console Composer" }, { "plan-share", "Console Composer" }, { "plan-saved", "Console Composer" },
            { "plan-checklist", "Console Composer" }, { "firmware", "Console Composer" }, { "designer", "Console Composer" }, { "design-editor", "Console Composer" },
            { "design-share", "Console Composer" }, { "design-console", "Console Composer" }, { "design-check", "Console Composer" }, { "design-plan", "Console Composer" },
            { "prepare", "Console Composer" }, { "prep-plan", "Console Composer" },
            { "community", "Console Composer" }, { "community-details", "Console Composer" }, { "community-files", "Console Composer" }, { "community-storage", "Console Composer" }, { "community-plan", "Console Composer" },
            { "community-remove", "Console Composer" }, { "community-rollback", "Console Composer" }, { "community-result", "Console Composer" },
            { "console-browser", "Console Browser" }, { "checkup", "Console Browser" },
            { "backup-browser", "Backup Browser" }, { "browse", "Backup Browser" }, { "browse-damaged", "Backup Browser" }, { "export-result", "Backup Browser" },
            { "backup-repair", "Backup Repair" }, { "repair-routes", "Backup Repair" }, { "repair-result", "Backup Repair" }, { "supply", "Backup Repair" },
            { "parity", "Backup Repair" }, { "parity-result", "Backup Repair" },
            { "storage-home", "USB and SD Backup" }, { "storage-select", "USB and SD Backup" }, { "storage-result", "USB and SD Backup" }, { "storage-repair", "USB and SD Backup" },
            { "storage-restore", "USB and SD Backup" }, { "storage-plan", "USB and SD Backup" },
            { "developer", "Developer Mode" }, { "developer-return", "Developer Mode" }, { "test-boot", "Developer Mode" }, { "test-boot-plan", "Developer Mode" },
            { "kernel-plan", "Developer Mode" }, { "system-plan", "Developer Mode" },
            { "settings", "Settings" }, { "about", "Help and About" }, { "help", "Help and About" }, { "credits", "Credits" }, { "ccs-intro", FullAppEntry },
        };

        /// <summary>back to the open section's first page (a task's Back, Cancel or Done)</summary>
        void BackToSection() => Go(section);

        // pages Escape may leave for their section's first page: nothing is being typed, chosen or run on them
        static readonly HashSet<string> EscapePages = new HashSet<string> { "backup", "restore-pick", "restore-review", "result", "msg", "verify", "prepare", "return", "files-intro", "methods", "firmware", "designer", "repair-routes", "supply", "developer-return", "driver", "driver-setup", "plan-checklist", "package-built", "plan-saved", "apply-package", "design-check", "design-console", "browse-damaged", "repair-result", "parity-result", "community", "community-details", "community-remove", "community-rollback" };
        void OnEscape(System.Windows.Input.KeyEventArgs e)
        {
            var tag = (page.Content as FrameworkElement)?.Tag as string;
            if (worker != null && page.Content == taskPage || tag == null || !EscapePages.Contains(tag)) return;   // never the console task's own page; Go asks about unsaved changes
            Go(section); e.Handled = true;
        }

        // ------------------------------------------------------------------ the console strip (top of the page column)
        readonly TextBlock consoleState = new TextBlock(); readonly Ellipse consoleDot = new Ellipse { Width = 8, Height = 8, Margin = new Thickness(0, 1, 8, 0), VerticalAlignment = VerticalAlignment.Center };
        ComboBox consoleChoice;
        readonly TextBlock consoleEarlier = new TextBlock(); Button renameButton;
        /// <summary>a console an operation's own session identified: its label, its full key and model when the session sent them (the
        /// friendly name's key: ConsoleNameBook), when, and whether it was simulated</summary>
        internal sealed class ConsoleSeen { public string Tag, Key, Model; public DateTime At; public bool Simulated; }
        /// <summary>the latest operation session (one per worker; 0 before the first), what that session's own worker identified (null until
        /// it does) and, kept apart as history only, the console an earlier session identified. An earlier session's console is never shown
        /// as the current session's.</summary>
        int consoleSession; ConsoleSeen seenNow, seenEarlier;

        /// <summary>WB-UX-10: a console choice list as wide as its longest console name needs (so the chosen name is never shortened with "…"
        /// where the list has that room), every family in the one shared order</summary>
        ComboBox ConsoleChoiceList(ConsoleProfile selected = null)
        {
            var c = new ComboBox { MinWidth = ConsoleNameWidth() };
            foreach (var p in ConsoleProfile.Supported) c.Items.Add(new ComboBoxItem { Content = p.Name, Tag = p });
            if (selected != null) c.SelectedIndex = ConsoleProfile.Supported.ToList().IndexOf(selected);
            return c;
        }
        /// <summary>the width a choice list needs to show the longest console name whole: the name at the lists' text size, plus the list's
        /// own padding and arrow</summary>
        double ConsoleNameWidth()
        {
            double w = 0;
            foreach (var p in ConsoleProfile.Supported)
            {
                var t = new TextBlock { Text = p.Name, FontSize = 13, FontFamily = Res<FontFamily>("F.Ui") ?? new FontFamily("Segoe UI") };
                t.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity)); w = Math.Max(w, t.DesiredSize.Width);
            }
            return Math.Ceiling(w) + 48;
        }

        UIElement ConsoleStrip()
        {
            // Back and Forward first, at the strip's left edge; then the console choice and the status side by side (the status moves under
            // the choice when there is not room for both), and the two links at the right. Narrower: the choice moves to a second line under
            // the arrows; narrower still, the links to a third. The arrows always stay on the first line.
            var g = new Grid { Background = Res<Brush>("B.Bar") };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); g.ColumnDefinitions.Add(new ColumnDefinition()); g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            for (int r = 0; r < 3; r++) g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var arrows = new Border { Child = HistoryArrows(), Margin = new Thickness(16, 7, 14, 7), VerticalAlignment = VerticalAlignment.Top };
            g.Children.Add(arrows);
            var wrap = new WrapPanel { Margin = new Thickness(0, 4, 12, 4), VerticalAlignment = VerticalAlignment.Center }; Grid.SetColumn(wrap, 1);
            var pick = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 4, 20, 4) };
            var label = new TextBlock { Text = "Console", Style = Res<Style>("T.Muted"), FontSize = Ty.Small, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
            pick.Children.Add(label);
            consoleChoice = ConsoleChoiceList(); consoleChoice.VerticalAlignment = VerticalAlignment.Center;
            ShowProfileInStrip(); if (consoleChoice.SelectedIndex < 0) consoleChoice.SelectedIndex = 0;
            AutomationProperties.SetName(consoleChoice, "Console"); AutomationProperties.SetLabeledBy(consoleChoice, label);
            consoleChoice.SelectionChanged += (s, e) => OnStripProfileChanged();   // ApplyProfile: the one guarded change
            pick.Children.Add(consoleChoice); wrap.Children.Add(pick);
            // the dot, the console's name and state (wrapping onto a second line when the strip is narrow) and the pencil that renames it
            var state = new Grid { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 4, 20, 4) };
            state.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); state.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); state.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            SetNoTerms(consoleState, true); SetNoTerms(consoleEarlier, true); consoleState.FontSize = Ty.Text; consoleState.VerticalAlignment = VerticalAlignment.Center; consoleState.TextWrapping = TextWrapping.Wrap;
            consoleEarlier.TextWrapping = TextWrapping.Wrap;
            renameButton = RenameButton();
            Grid.SetColumn(consoleState, 1); Grid.SetColumn(renameButton, 2);
            state.Children.Add(consoleDot); state.Children.Add(consoleState); state.Children.Add(renameButton);
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
            Grid.SetColumn(links, 2); g.Children.Add(links);
            // 0: one line; 1: the console choice on a second line (the links stay on the first, beside a status with room to read);
            // 2: the status alone beside the arrows, the choice and the links on the second line; 3: the links on a third line
            int mode = 0;
            void Fit()
            {
                if (g.ActualWidth <= 0) return;
                var inf = new Size(double.PositiveInfinity, double.PositiveInfinity);
                arrows.Measure(inf); pick.Measure(inf); links.Measure(inf);
                double w = g.ActualWidth, a = arrows.DesiredSize.Width, p = pick.DesiredSize.Width, l = links.DesiredSize.Width;
                int want = w >= a + p + l + wrap.Margin.Right + 24 ? 0 : w >= a + l + 380 ? 1 : w >= 16 + p + 12 + l + 24 ? 2 : 3;
                if (want == mode && (want == 0) == (pick.Parent == wrap)) return;
                mode = want;
                if (pick.Parent is Panel from) from.Children.Remove(pick);
                if (mode == 0) { wrap.Children.Insert(0, pick); pick.Margin = new Thickness(0, 4, 20, 4); }
                else { Grid.SetRow(pick, 1); Grid.SetColumn(pick, 0); Grid.SetColumnSpan(pick, mode == 2 ? 2 : 3); pick.HorizontalAlignment = HorizontalAlignment.Left; pick.Margin = new Thickness(16, 0, 12, 8); g.Children.Add(pick); }
                Grid.SetRow(links, mode == 3 ? 2 : mode == 2 ? 1 : 0); Grid.SetColumn(links, mode == 3 ? 0 : 2); Grid.SetColumnSpan(links, mode == 3 ? 3 : 1);
                links.HorizontalAlignment = HorizontalAlignment.Left;
                links.Margin = mode == 3 ? new Thickness(16, 0, 12, 10) : mode == 2 ? new Thickness(0, 0, 24, 8) : new Thickness(0, 0, 24, 0);
                Grid.SetColumnSpan(wrap, mode >= 2 ? 2 : 1);   // the status alone on the first line: the whole width beside the arrows
            }
            g.SizeChanged += (s, e) => Fit();
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
            if (renameButton != null) renameButton.Visibility = seenNow?.Key != null ? Visibility.Visible : Visibility.Collapsed;
        }

        /// <summary>"NES Classic (1) · Console 73f23039, identified at 2:05 PM": the friendly name (when the session sent the console's key)
        /// and the short tag</summary>
        string Seen(ConsoleSeen s) => (FriendlyName(s) is string n ? n + " · " : "") + (s.Simulated ? "Simulated console " : "Console ") + s.Tag + ", identified at " + s.At.ToString("h:mm tt", En);

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
            var key = ev.S("console_key"); if (key != null && !CcsBackup.Library.ConsoleNameBook.IsKey(key)) key = null;
            // the same console named again by a later event of this session: when it was identified, its key and its name stay
            if (seenNow != null && seenNow.Tag == tag && (key == null || key == seenNow.Key)) return;
            if (seenNow != null && seenNow.Tag == tag && seenNow.Key == null) { seenNow.Key = key; seenNow.Model = ev.S("physical_model"); }
            else seenNow = new ConsoleSeen { Tag = tag, Key = key, Model = ev.S("physical_model"), At = DateTime.Now, Simulated = !string.IsNullOrEmpty(opt.SimulatedConsole) };
            NameOf(seenNow, true);   // its friendly name: numbered the first time this console is identified (WorkbenchNav.cs)
            UpdateConsoleStrip();
        }

        /// <summary>starts an operation's worker: a new console session. Its console starts unidentified (a console an earlier session
        /// identified is kept as history only), and its events and exit are its own: a superseded session's late event or exit (its worker
        /// was told to quit when the person left it) never names the current session's console or changes its page or status.</summary>
        WorkerClient NewSession(string op)
        {
            // never reached for a family without console tasks (each task asks FamilyHasTasks first); if it were, no worker starts
            if (!profile.ConsoleTasks) throw new InvalidOperationException("No console tasks for " + profile.Name + " in this version (" + op + ").");
            if (seenNow != null) seenEarlier = seenNow;
            seenNow = null; int session = ++consoleSession;
            gotResult = false; writing = false; quitting = false; workerOp = op;
            var w = new WorkerClient(opt.WorkerExe, Args(), Dispatcher);
            // the session's own events and end; a page they show is the task's (WorkbenchNav.cs: kept for Return to Task while another page
            // is in front). The person's quit (Done, a review's Cancel, Restart the Console's end) ends the session: nothing it still sends
            // changes a page.
            w.Event += ev => { if (session != consoleSession) return; workerDispatch++; try { NoteConsole(ev); OnWorkerEvent(ev); } finally { workerDispatch--; } };
            w.Exited += code => { if (session != consoleSession) { w.Dispose(); return; } workerDispatch++; try { OnWorkerExit(code); } finally { workerDispatch--; } };
            w.Sending += req => { if (session == consoleSession && req["cmd"] as string == "quit") quitting = true; };
            worker = w; BeginTask(op); UpdateConsoleStrip();
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
            SetNoTerms(statusText, true); statusText.Margin = new Thickness(22, 8, 16, 8); statusText.FontSize = Ty.Fine; statusText.Style = Res<Style>("T.Muted"); statusText.TextWrapping = TextWrapping.Wrap; statusText.VerticalAlignment = VerticalAlignment.Center;
            AutomationProperties.SetLiveSetting(statusText, AutomationLiveSetting.Polite);   // its own words are its name for screen readers
            g.Children.Add(statusText);
            var right = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 6, 22, 6) }; Grid.SetColumn(right, 1); g.Children.Add(right);
            right.Children.Add(ReturnButton());   // Return to Task, while the console task's page is not in front (WorkbenchNav.cs)
            right.Children.Add(UpdateIndicator());   // "Update available", only when a newer version is published (WorkbenchUpdates.cs)
            if (!string.IsNullOrEmpty(opt.SimulatedConsole)) right.Children.Add(Pill("Simulated console", "B.Warn"));
            return g;
        }

        /// <summary>the status strip, the arrows and the console strip after a page change. Every section can always be opened.</summary>
        void UpdateChrome()
        {
            UpdateStatus(null); UpdateConsoleStrip(); UpdateArrows();
        }

        void UpdateStatus(string say)
        {
            var tag = (page.Content as FrameworkElement)?.Tag as string;
            string text; var task = TaskStatusLine(out bool canReturn);
            if (returnButton != null) { returnButton.Visibility = canReturn ? Visibility.Visible : Visibility.Collapsed; returnButton.ToolTip = canReturn ? "Back to " + (taskTitle ?? "the console task") : null; }
            if (say != null) { text = say; backgroundWork = null; }
            else if (task != null) text = task;
            else if (backgroundWork != null && !(worker != null && worker.Running)) text = "Still working on this PC: " + backgroundWork + ". You can keep using Workbench; this line says when it finishes.";
            else if (worker != null && worker.Running && !gotResult) text = "Working: " + (tag == "progress" && progressTitle != null ? progressTitle : taskTitle ?? "a console task") + ". You can open other pages while it runs; it keeps going, and Return to Task brings you back.";
            else if (worker != null && worker.Running) text = "The console session is still open: the console waits in its memory program. Restart the Console or Done ends it.";
            else if (tag == "progress") text = "Working: " + (progressTitle ?? "a task on this PC") + ".";
            else if (lookProblem != null) text = "Workbench could not load its colors and styles (" + lookProblem + "). Close Workbench and open it again.";   // WB-UX-07: never silent
            else text = "No task running.";
            statusText.Text = text;
        }

        // ------------------------------------------------------------------ Dashboard
        void ShowDashboard()
        {
            settings = Reload();
            var sp = PageHead("Dashboard", Tagline);
            if (!profile.ConsoleTasks) sp.Children.Add(FamilyNotYetNote(true));
            // the first thing to do, across the page: its words on the left, its button on the right. The console line above names the
            // console and what is known about it, so the Dashboard does not repeat it.
            var first = new Grid(); first.ColumnDefinitions.Add(new ColumnDefinition()); first.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var words = new StackPanel { Margin = new Thickness(0, 0, 24, 0), MaxWidth = 720, HorizontalAlignment = HorizontalAlignment.Left };
            words.Children.Add(T("Start here", Ty.Small, true, true, new Thickness(0, 0, 0, 4)));
            words.Children.Add(T("Back Up Console", Ty.CardTitle, true, false, new Thickness(0, 0, 0, 8)));
            words.Children.Add(T("Make a complete, checked copy of your " + Con + "'s built-in storage on this PC: its games, saves, settings and everything else on it, including other programs' files. A backup only reads the console; nothing on it changes.", Ty.Text, false, true, new Thickness(0, 0, 0, 8)));
            words.Children.Add(T("Workbench connects to a console only when you start a console task. Backups already on this PC can be browsed, checked and repaired without a console or a USB driver.", Ty.Small, false, true, new Thickness(0)));
            first.Children.Add(words);
            var bb = B("Back Up Console", "Btn.Primary", () => { if (LeaveOk()) { Enter("Console Backup"); ShowBackup(); } }); bb.Margin = new Thickness(0); bb.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(bb, 1); first.Children.Add(bb);
            sp.Children.Add(Card(first));

            sp.Children.Add(CardGrid(230, 3,
                ActionCard("Browse a Backup", "Look through a backup on this PC by game, by type or as stored, and copy files out of it. No console needed.", "Btn", () => Go("Backup Browser")),
                ActionCard("Restore a Backup", "Put a backup back on the console it came from. You review every change before anything is written, and the console is backed up first.", "Btn", () => { Enter("Console Restore"); ShowRestorePick(); }),
                ActionCard("Check the Console", "A check-up that only reads: whether the console's startup software is recognized, what is installed and how much space is free.", "Btn", () => Go("Console Browser"))));

            sp.Children.Add(Group("More tools"));
            var more = new StackPanel();
            more.Children.Add(ToolRow("Repair a Backup File", "Check a backup file on this PC and make a repaired copy. The original file is never changed.", () => Go("Backup Repair")));
            more.Children.Add(ToolRow("HOME Designer", "Design CCS HOME with your own pictures, colors, folders and game order. Part of Console Composer.", () => { Enter("Console Composer"); ShowDesigner(); }));
            more.Children.Add(ToolRow("Set Up or Rebuild a Console", "Choose how to set up your console, with options for a fresh setup, a transfer from another console, or a rebuild from your backups. Part of Console Composer.", () => Go("Console Composer"), last: true));
            sp.Children.Add(Card(more, 8));

            // Recent backups, with its small refresh button right beside the heading (shown only when there is a location to read again)
            var rh = new Grid { Margin = new Thickness(0, 8, 0, 10) };
            rh.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); rh.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            rh.ColumnDefinitions.Add(new ColumnDefinition()); rh.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var rt = Group("Recent backups", 0); rt.Margin = new Thickness(0); rt.VerticalAlignment = VerticalAlignment.Center; rh.Children.Add(rt);
            recentRefresh = RefreshButton(); Grid.SetColumn(recentRefresh, 1); rh.Children.Add(recentRefresh);
            var all = B("See All Backups", "Btn.Ghost", () => Go("Backup Browser")); all.Margin = new Thickness(0); Grid.SetColumn(all, 3); rh.Children.Add(all);
            sp.Children.Add(rh);
            recentHost = new ContentControl { Focusable = false }; sp.Children.Add(recentHost);
            FillRecent();
            Show(sp, "dashboard");
        }

        Button recentRefresh; ContentControl recentHost;

        /// <summary>the small circular-arrow button beside Recent backups: reads the backup locations again (keyboard: Tab, then Enter or
        /// Space). Not offered while a console task runs, while the list is being read, or when there is nothing to read yet.</summary>
        Button RefreshButton()
        {
            var glyph = new TextBlock { Text = "\uE72C", FontFamily = Res<FontFamily>("F.Icons") ?? new FontFamily("Segoe MDL2 Assets"), FontSize = 13, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            var b = new Button { Content = glyph, Style = Res<Style>("Btn.Ghost"), Width = 32, Height = 32, MinWidth = 0, MinHeight = 0, Padding = new Thickness(0), Margin = new Thickness(10, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center, ToolTip = "Refresh backup list" };
            AutomationProperties.SetName(b, "Refresh backup list");
            b.Click += (s, e) => RefreshRecent();
            return b;
        }

        /// <summary>reads the backup locations again: the button is turned off (and says so) until the list is shown again; the rest of the
        /// Dashboard, its scroll position and the keyboard focus stay as they are</summary>
        void RefreshRecent()
        {
            if (recentRefresh == null || recentHost == null || Busy || !recentRefresh.IsEnabled) return;
            bool focused = recentRefresh.IsKeyboardFocused;
            recentRefresh.IsEnabled = false; recentRefresh.ToolTip = "Reading the backup list…";
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (recentHost == null || ((page.Content as FrameworkElement)?.Tag as string) != "dashboard") return;
                settings = Reload(); FillRecent();
                if (focused && recentRefresh.IsEnabled) recentRefresh.Focus();
            }), System.Windows.Threading.DispatcherPriority.Background);
        }

        void FillRecent()
        {
            recentHost.Content = RecentBackups(out bool canRefresh);
            recentRefresh.Visibility = canRefresh ? Visibility.Visible : Visibility.Collapsed;
            recentRefresh.IsEnabled = !Busy; recentRefresh.ToolTip = "Refresh backup list";
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
        /// <summary>the backup location's state for the Dashboard: a folder not made yet (the normal start: Workbench makes it with the first
        /// backup), a drive that is not available, a readable folder with no backups, a folder that could not be read (never called empty),
        /// or the backups it holds. canRefresh: whether reading it again can show something new (never for a folder not made yet).</summary>
        UIElement RecentBackups(out bool canRefresh)
        {
            var sp = new StackPanel(); canRefresh = true;
            var folder = settings.PrimaryFolder;
            bool folderThere = false, driveThere = true;
            try { folderThere = Directory.Exists(folder); if (!folderThere) { var root = Path.GetPathRoot(folder); driveThere = !string.IsNullOrEmpty(root) && Directory.Exists(root); } } catch (Exception) { }
            List<BackupListEntry> list;
            try { list = BackupList.List(settings); }
            catch (Exception ex)
            {
                // the location is there but could not be read: this never says there are no backups
                var c = new StackPanel();
                c.Children.Add(T("Workbench could not read your backup location", Ty.Heading, true));
                c.Children.Add(T("This does not mean there are no backups there. Check that you can open the folder in File Explorer, then refresh the list, or choose another location in Settings. A backup you already have elsewhere can still be opened.", Ty.Text, false, true));
                c.Children.Add(PathText(folder));
                c.Children.Add(Details("Technical details", "Location: " + folder, string.IsNullOrEmpty(settings.ArchiveFolder) ? null : "Additional copy location: " + settings.ArchiveFolder, "What Windows reported: " + ex.Message));
                c.Children.Add(Buttons(B("Open a Backup File…", "Btn", OpenBackupFile), B("Backup Locations", "Btn.Ghost", () => Go("Settings"))));
                var card = Card(c); card.BorderBrush = Res<Brush>("B.Red3"); sp.Children.Add(card);
                return sp;
            }
            if (list.Count == 0)
            {
                var c = new StackPanel();
                if (!folderThere && !driveThere)
                {
                    c.Children.Add(T("Your backup location is not available", Ty.Heading, true));
                    c.Children.Add(T("Its drive is not connected or cannot be found, so Workbench cannot see the backups there. Connect the drive and refresh the list, choose another location in Settings, or open a backup you already have.", Ty.Text, false, true));
                }
                else
                {
                    canRefresh = folderThere;   // a folder that is not made yet holds nothing to read again
                    c.Children.Add(T("No backups in this location yet", Ty.Heading, true));
                    c.Children.Add(T("Back up your console to get started, or open a backup you already have." + (folderThere ? "" : " Workbench makes this folder when it saves your first backup."), Ty.Text, false, true));
                }
                c.Children.Add(PathText(folder));
                c.Children.Add(Buttons(B("Open a Backup File…", "Btn", OpenBackupFile), B("Backup Locations", "Btn.Ghost", () => Go("Settings"))));
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
            sp.Children.Add(T(VerifiedNote + " " + BackupSizeNote, Ty.Fine, false, true, new Thickness(0, 2, 0, 0)));
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

        /// <summary>a backup's state in the words of every list (the Dashboard, the Backup Browser, Backup Repair, Console Backup): the result
        /// of a full check (BackupListEntry.StatusText), never what the file's description says about itself, and never "restorable"</summary>
        static (string, string) RowState(BackupListEntry e)
        {
            switch (e.Status)
            {
                case BrowseStatus.Complete: return (e.StatusText, e.Check == CheckState.Verified ? "B.Ok" : "B.Warn");
                case BrowseStatus.Salvage: return (e.StatusText, "B.Warn");
                default: return (e.StatusText, "B.Red3");
            }
        }

        /// <summary>the line under a backup's date: its last full check and what it found (BackupListEntry.CheckText)</summary>
        static string LastCheckText(BackupListEntry e) => e.CheckText;

        /// <summary>opens a console backup file from anywhere on this PC in the Backup Browser (checked first; nothing is written to it)</summary>
        void OpenBackupFile()
        {
            var chosenFile = CcsFileDialog.OpenFile(this, "Open a console backup", null, FileKinds.ConsoleBackupFilter, "console-backup");
            if (chosenFile == null) return;
            Enter("Backup Browser"); OpenBrowse(chosenFile, false);
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
            sp.Children.Add(StudioCards());
            var dl = new StackPanel();
            dl.Children.Add(T("Get Classic Cart Studio for Windows", Ty.Heading, true, false, new Thickness(0, 0, 0, 8)));
            dl.Children.Add(T("The button opens ClassicCartStudio.com/app, the official download page, in your web browser. For Windows 10 and 11.", Ty.Text, false, true));
            var problem = new ContentControl();
            var go = B("Download Classic Cart Studio", "Btn.Primary", () => { if (!Launch(opt.FullAppUrl)) problem.Content = Note("The web browser could not be opened. The address is " + opt.FullAppUrl, "warn"); });
            AutomationProperties.SetHelpText(go, opt.FullAppUrl);
            dl.Children.Add(Buttons(go)); dl.Children.Add(problem);
            dl.Children.Add(T("You don't need the full app to use Workbench: everything here works on its own.", Ty.Small, false, true, new Thickness(0, 4, 0, 0)));
            sp.Children.Add(Card(dl));
            Show(sp, "ccs-intro");
        }

        /// <summary>what the full Classic Cart Studio app gives you, the same in Get Full Classic Cart Studio and About Classic Cart Studio</summary>
        CardPanel StudioCards() => CardGrid(260, 2,
            InfoCard("Organize your collection", "Bring in the NES game files you own, see them with their artwork and details, and arrange them in collections."),
            InfoCard("Play", "Play NES games in the app on Windows, or on " + HomepageName + " in a web browser or on a phone where the browser supports it."),
            InfoCard("Included Games", "Free games and demos whose developers gave permission, ready to play. No commercial Nintendo games are included."),
            InfoCard("Your NES Classic's HOME", "Plan collections for your NES Classic and preview Classic Cart Studio's HOME on your PC. Putting them on a console uses guided steps with a backup first."));

        // ------------------------------------------------------------------ About Classic Cart Studio (the wordmark's window, both hosts)
        /// <summary>the address the window's button opens, exactly (the site's front page)</summary>
        internal const string HomepageUrl = "https://classiccartstudio.com/";
        /// <summary>WB-UX-15: how the window writes the site's name (the button and the line beside it): this capitalization, no "https://",
        /// no trailing slash. The button still opens HomepageUrl exactly.</summary>
        internal const string HomepageName = "ClassicCartStudio.com";
        /// <summary>the window's pictures: screenshots of the current Classic Cart Studio with sample games (intro/*.png; where each came from:
        /// intro/SOURCES.json), cropped to the page itself, without a window's or a panel's own buttons. WB-UX-15: the HOME picture shows the
        /// Featured Shelf theme (its file and source record are refreshed with the theme's final look).</summary>
        static readonly (string file, string title, string caption)[] IntroShots =
        {
            ("collection", "Your collections", "A collection's games with their box art, each ready to play. Sample games."),
            ("home", "HOME, previewed on a PC", "A collection's HOME in the Featured Shelf theme, as Classic Cart Studio previews it. Sample games."),
            ("details", "Game Details", "Each game's box art, game screens, details and overview, with Play. Sample game."),
        };

        /// <summary>About Classic Cart Studio: what the name means, what the full app gives you, how Workbench relates to it, a few screenshots
        /// that open larger, and a button that opens ClassicCartStudio.com in the browser (nothing opens by itself)</summary>
        internal Window AboutStudioWindow()
        {
            var w = Owned("About Classic Cart Studio", Math.Max(720, Math.Min(980, ActualWidth - 60)), true); w.Height = Math.Max(560, Math.Min(940, ActualHeight - 20)); w.MinWidth = 640; w.MinHeight = 480;
            var root = new DockPanel();
            var foot = new Border { BorderBrush = Res<Brush>("B.Line"), BorderThickness = new Thickness(0, 1, 0, 0), Background = Res<Brush>("B.Bar"), Padding = new Thickness(28, 12, 28, 12) };
            var fg = new Grid(); fg.ColumnDefinitions.Add(new ColumnDefinition()); fg.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var problem = new ContentControl { Focusable = false };
            var left = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            var visit = B("Visit " + HomepageName, "Btn.Primary", () => { problem.Content = Launch(HomepageUrl) ? null : Note("The web browser could not be opened. Go to " + HomepageName + " in your web browser.", "warn"); });
            visit.Margin = new Thickness(0, 0, 14, 0); AutomationProperties.SetHelpText(visit, HomepageUrl);
            var row = new WrapPanel(); row.Children.Add(visit);
            row.Children.Add(T("Opens " + HomepageName + " in your web browser.", Ty.Small, false, true, new Thickness(0, 9, 0, 0)));
            left.Children.Add(row); left.Children.Add(problem); fg.Children.Add(left);
            var close = B("Close", "Btn", () => w.Close()); close.Margin = new Thickness(16, 0, 0, 0); close.VerticalAlignment = VerticalAlignment.Center; Grid.SetColumn(close, 1); fg.Children.Add(close);
            foot.Child = fg; DockPanel.SetDock(foot, Dock.Bottom); root.Children.Add(foot);

            var sp = new StackPanel { Margin = new Thickness(36, 26, 36, 24), MaxWidth = 900 };
            // the approved wordmark and tagline, as on the website and the app
            var mark = opt.Wordmark is BitmapSource bs ? Trimmed(bs) : opt.Wordmark;
            if (mark != null)
            {
                var img = new Image { Source = mark, Stretch = Stretch.Uniform, MaxWidth = 340, HorizontalAlignment = HorizontalAlignment.Center, SnapsToDevicePixels = true, Margin = new Thickness(0, 4, 0, 10) };
                RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality); AutomationProperties.SetName(img, "Classic Cart Studio"); sp.Children.Add(img);
            }
            else sp.Children.Add(new TextBlock { Text = "Classic Cart Studio", Style = Res<Style>("T.Title"), FontSize = 26, HorizontalAlignment = HorizontalAlignment.Center });
            sp.Children.Add(new TextBlock { Text = "YOUR CLASSICS. YOUR WAY.", FontSize = 12.5, FontWeight = FontWeights.SemiBold, Foreground = Res<Brush>("B.Muted"), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 0, 0, 22) });
            var lede = T("Classic Cart Studio, CCS for short, is a free app for your NES game collections. The name says what it is for: classic games from their cartridges, the carts, and a studio where you put them together into collections, play them and get them ready for your " + Con + ".", Ty.Body, false, false, new Thickness(0, 0, 0, 18));
            lede.LineHeight = 21; lede.TextAlignment = TextAlignment.Center; lede.MaxWidth = 700; lede.HorizontalAlignment = HorizontalAlignment.Center; sp.Children.Add(lede);

            var shots = IntroShots.Select(x => (x, src: Picture("intro/" + x.file + ".png"))).Where(x => x.src != null).Select(x => ShotCard(x.src, x.x.title, x.x.caption)).ToArray();
            if (shots.Length > 0) sp.Children.Add(CardGrid(230, 3, shots));

            sp.Children.Add(Group("What the full app gives you", 6));
            sp.Children.Add(StudioCards());

            var alone = new StackPanel();
            bool fromStudio = opt.HostedInFullApp || opt.Studio != null;   // opened from Classic Cart Studio for Windows (in its window, or started by it)
            alone.Children.Add(T(fromStudio ? "Workbench, here and on its own" : "Workbench works on its own", Ty.Heading, true, false, new Thickness(0, 0, 0, 6)));
            alone.Children.Add(T(fromStudio
                ? "You opened CCS Console Workbench from Classic Cart Studio for Windows. It also comes as a standalone program with the same pages, for people who only want to look after their console's backups."
                : "You don't need the full app to use this window: CCS Console Workbench is a standalone program, and everything in it works on its own. Classic Cart Studio for Windows installs this same program and opens it in its own window.", Ty.Text, false, true, new Thickness(0)));
            sp.Children.Add(Card(alone, 18));

            var sv = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = sp, Focusable = false };
            root.Children.Add(sv);
            w.Content = root; w.Loaded += (s, e) => visit.Focus();
            return w;
        }
        void OpenAboutStudio() => Modal(AboutStudioWindow());

        /// <summary>a screenshot in a framed card: its title above, the picture in its own frame and its caption (WB-UX-13: italic,
        /// "Screenshot:" first) directly under it, so it is clearly a picture; it opens larger with a click, Enter or Space, and closing that
        /// puts the focus back here (its ring shows only when the keyboard was used: FocusCue)</summary>
        UIElement ShotCard(BitmapSource src, string title, string caption)
        {
            var s = new StackPanel();
            s.Children.Add(T(title, Ty.Body, true, false, new Thickness(0, 0, 0, 8)));
            var img = new Image { Source = src, Stretch = Stretch.Uniform, SnapsToDevicePixels = true }; RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
            var frame = new Border { BorderBrush = Res<Brush>("B.Line2"), BorderThickness = new Thickness(1), Background = Res<Brush>("B.Bg"), Padding = new Thickness(6), CornerRadius = new CornerRadius(6), Child = img };
            var b = new Button { Content = frame, Style = Res<Style>("Wb.Plain"), HorizontalAlignment = HorizontalAlignment.Stretch, ToolTip = "Select to see it larger" };
            AutomationProperties.SetName(b, "Screenshot: " + title + ". Select to see it larger.");
            b.Click += (o, e) => { ShowPicture(src, title, caption, "A screenshot of Classic Cart Studio with sample games: a picture, not the app itself.", Window.GetWindow(b)); b.Focus(); };
            s.Children.Add(b);
            s.Children.Add(PictureCaption("Screenshot: " + caption));
            var c = Card(s, 14); c.Margin = new Thickness(0); return c;
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
