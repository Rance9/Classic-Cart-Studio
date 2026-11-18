// Classic Cart Studio console backup and restore window. Copyright (C) 2026 Rance9. GPL-3.0-or-later.
// CCS Console Workbench: the glossary. One short explanation per unfamiliar word, the same on every page that uses it and in Help's
// Glossary. On a page a word is a text button with a dotted underline (Term): a click, a tap, Enter or Space opens its explanation in a
// small panel beside it; Escape or Close shuts it and puts the focus back on the word. Never on hover alone, and never the only place a risk
// or a confirmation is said: those stay on the page. Each explanation keeps the technical name for people who know it.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace CcsBackup.Ui
{
    /// <summary>one word and its explanation; Tech: its technical name(s), for people who know them</summary>
    public sealed class GlossaryTerm { public string Key, Word, Text, Tech; }

    public static class Glossary
    {
        public static readonly IReadOnlyList<GlossaryTerm> Terms = new[]
        {
            new GlossaryTerm { Key = "home", Word = "CCS HOME", Text = "The game-selection screen Classic Cart Studio puts on the console, in one of four themes. It is separate from the console's original menu, which stays." },
            new GlossaryTerm { Key = "nucleus", Word = "CCS Nucleus", Text = "An installation option that adds a chooser for CCS HOME and other system software you set up on the console. It uses Hakchi2 CE underneath. Setting it up changes the console, and any other system software must come from you.", Tech = "the hakchi package ccs-nucleus" },
            new GlossaryTerm { Key = "mod", Word = "CCS Mod", Text = "An installation option that adds CCS HOME as an entry in the console's own menu, using Hakchi2 CE underneath. The console still starts in its own menu unless you choose to open CCS HOME when it is turned on. Setting it up changes the console.", Tech = "the hakchi package ccs-mod (CCS Mod for Hakchi)" },
            new GlossaryTerm { Key = "hakchi", Word = "Hakchi2 CE", Text = "The free community project that lets people add games and software to these consoles. Workbench uses parts of its version 3.9.3, and CCS Mod and CCS Nucleus run on top of it.", Tech = "hakchi" },
            new GlossaryTerm { Key = "recovery", Word = "Recovery mode", Text = "A mode built into the console's chip. With the USB cable connected, hold RESET while you turn the console on: the console waits for the PC instead of starting, and Workbench can run a small program in its memory. Nothing on the console changes just by being in this mode.", Tech = "FEL mode" },
            new GlossaryTerm { Key = "startup", Word = "Startup software", Text = "The software the console loads first when it is turned on; it starts everything else. Preparing a console replaces it, a change you review in Developer Mode can, and a restore puts back the console's own copy from its backup.", Tech = "kernel (boot image)" },
            new GlossaryTerm { Key = "bootloader", Word = "Bootloader", Text = "The very first small programs the console's chip runs, before the startup software. Console Rescue can repair them from this console's own backup.", Tech = "boot0 and boot1 (U-Boot)" },
            new GlossaryTerm { Key = "system-image", Word = "System image", Text = "A console's system (its own software and built-in games) as one file. CCS Nucleus can add system images you supply as choices. A system image is not startup software.", Tech = "a SquashFS root file system, .hsqs" },
            new GlossaryTerm { Key = "storage", Word = "Built-in storage", Text = "The memory chip inside the console that keeps its startup software, its system, games, saves and settings. A console backup copies all of it. Games on a USB drive or SD card are not in it.", Tech = "NAND" },
            new GlossaryTerm { Key = "battery-save", Word = "Battery save", Text = "A game's own save, the kind the original cartridge kept with a battery: your progress, kept by the game itself.", Tech = "cartridge.sram, save.sram" },
            new GlossaryTerm { Key = "suspend-point", Word = "Suspend point", Text = "A snapshot of a game at one moment, made when you suspend it from the console's menu, so you can continue from exactly there. It is not the game's own battery save.", Tech = "*.state, suspendpoint1–4" },
            new GlossaryTerm { Key = "restore-point", Word = "Restore point", Text = "In Expert Mode and Developer Mode: a backup of this console you choose as the dated point you can go back to. Changes made after that backup are not in it.", Tech = null },
            new GlossaryTerm { Key = "file-check", Word = "File check", Text = "Reading a backup file back in full and comparing every part with its checksums. A passed check means the file is intact; it is not a test of restoring it.", Tech = "SHA-256 checksums" },
            new GlossaryTerm { Key = "recovery-data", Word = "Recovery data", Text = "Extra data Backup Repair can make beside a backup file, so that a later small damage to the file can be repaired. It is not a second copy.", Tech = "PAR2 parity" },
            new GlossaryTerm { Key = "session", Word = "Console session", Text = "The time a console task is connected to the console. Workbench identifies a console only in a task's own session; an earlier session's console is shown apart, marked earlier.", Tech = null },
            new GlossaryTerm { Key = "console-backup", Word = FileKinds.ConsoleBackup, Text = FileKinds.ConsoleBackupWhat, Tech = ".zip" },
            new GlossaryTerm { Key = "studio-backup", Word = FileKinds.StudioBackup, Text = FileKinds.StudioBackupWhat, Tech = ".ccs" },
            new GlossaryTerm { Key = "game-saves", Word = FileKinds.GameSaves, Text = FileKinds.GameSavesWhat, Tech = null },
            new GlossaryTerm { Key = "home-design", Word = FileKinds.HomeDesign, Text = FileKinds.HomeDesignWhat, Tech = ".ccshome" },
            new GlossaryTerm { Key = "plan", Word = FileKinds.Plan, Text = FileKinds.PlanWhat, Tech = ".ccsplan" },
            new GlossaryTerm { Key = "package", Word = FileKinds.Package, Text = FileKinds.PackageWhat, Tech = ".zip" },
        };
        public static GlossaryTerm Find(string key) => Terms.FirstOrDefault(t => t.Key == key);
    }

    public sealed partial class BackupWorkspace
    {
        /// <summary>a word with its explanation: a text button (dotted underline) that opens a small panel beside it</summary>
        internal FrameworkElement Term(string key, string shown = null)
        {
            var t = Glossary.Find(key); if (t == null) return new TextBlock { Text = shown ?? key };
            var brush = Res<Brush>("B.Red3") ?? Brushes.IndianRed;
            var word = new TextBlock { Text = shown ?? t.Word, FontSize = Ty.Small, Foreground = brush };
            word.TextDecorations = new TextDecorationCollection { new TextDecoration { Location = TextDecorationLocation.Underline, Pen = new Pen(brush, 1) { DashStyle = DashStyles.Dot }, PenOffset = 2 } };
            var b = new Button { Content = word, Style = Res<Style>("Wb.Plain"), Padding = new Thickness(3, 1, 3, 2), Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center, Cursor = Cursors.Hand };
            AutomationProperties.SetName(b, "What is " + t.Word + "?"); AutomationProperties.SetItemStatus(b, "collapsed");
            var pop = new Popup { PlacementTarget = b, Placement = PlacementMode.Bottom, StaysOpen = false, AllowsTransparency = true, Focusable = false };
            var panel = TermPanel(t, () => { pop.IsOpen = false; b.Focus(); }, () => { pop.IsOpen = false; OpenHelp("glossary"); }, out var close);
            pop.Child = panel;
            pop.Opened += (s, e) => { AutomationProperties.SetItemStatus(b, "expanded"); close.Focus(); };
            pop.Closed += (s, e) => AutomationProperties.SetItemStatus(b, "collapsed");
            b.Click += (s, e) => pop.IsOpen = !pop.IsOpen;
            return b;
        }

        /// <summary>a word's explanation panel: the word, its explanation, its technical name, Close and More in Help; Escape closes it</summary>
        internal Border TermPanel(GlossaryTerm t, Action onClose, Action onHelp, out Button close)
        {
            close = B("Close", "Btn.Ghost", onClose); close.Margin = new Thickness(0, 8, 0, 0); close.MinHeight = 30;
            var help = new Button { Content = "More in Help", Style = Res<Style>("Btn.Link"), Margin = new Thickness(14, 8, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            help.Click += (s, e) => onHelp();
            var body = new StackPanel { MaxWidth = 380 };
            body.Children.Add(new TextBlock { Text = t.Word, FontSize = 13.5, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4), TextWrapping = TextWrapping.Wrap });
            body.Children.Add(new TextBlock { Text = t.Text, FontSize = 13, LineHeight = 19, TextWrapping = TextWrapping.Wrap });
            if (t.Tech != null) body.Children.Add(new TextBlock { Text = "Technical name: " + t.Tech, FontSize = 12, Foreground = Res<Brush>("B.Muted"), Margin = new Thickness(0, 6, 0, 0), TextWrapping = TextWrapping.Wrap });
            var row = new StackPanel { Orientation = Orientation.Horizontal }; row.Children.Add(close); row.Children.Add(help); body.Children.Add(row);
            var panel = new Border { Background = Res<Brush>("B.MenuSurface") ?? Res<Brush>("B.Surface"), BorderBrush = Res<Brush>("B.MenuEdge") ?? Res<Brush>("B.Line2"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(16, 12, 16, 12), Margin = new Thickness(0, 4, 8, 8), Child = body };
            AutomationProperties.SetName(panel, t.Word + ": " + t.Text);
            panel.PreviewKeyDown += (s, e) => { if (e.Key == Key.Escape) { onClose(); e.Handled = true; } };
            return panel;
        }

        /// <summary>a short line of explained words for a page: "What these words mean: [CCS Mod] [CCS Nucleus] …"</summary>
        internal FrameworkElement Terms(params string[] keys)
        {
            var w = new WrapPanel { Margin = new Thickness(0, 2, 0, 8) };
            w.Children.Add(new TextBlock { Text = keys.Length == 1 ? "What this means:" : "What these words mean:", FontSize = Ty.Small, Foreground = Res<Brush>("B.Muted"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) });
            foreach (var k in keys) w.Children.Add(Term(k));
            return w;
        }
    }
}
