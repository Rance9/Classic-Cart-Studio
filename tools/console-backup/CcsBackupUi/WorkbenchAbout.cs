// Classic Cart Studio console backup and restore window. Copyright (C) 2026 Rance9. GPL-3.0-or-later.
// CCS Console Workbench: About This Console (one modal window, opened from the console strip, the Dashboard and Help), the console family
// catalog's information (ConsoleInfo: shared with Help), the one guarded way the window's console family changes (ApplyProfile), and the
// window's own small dialogs (Ask) and owned windows (Owned).
//   About This Console   console Help, whatever opened it. Its labeled console choice starts on the window's family; choosing another one
//                        only changes what it shows (a reading draft): the window's family, its settings, a console session and every
//                        operation stay as they are. On every way of closing it (Close, the title bar's X, Escape) a different final
//                        choice asks one compact question, Change console? (WB-UX-10): the current console once, a Use console choice
//                        list starting on Help's choice, and Use Selection, Keep Current, Back to Help (Escape and its X are Back to
//                        Help). The same choice as at the start (also after browsing away and back) just closes. Nothing chosen inside
//                        it is saved unless Use Selection applies it, through ApplyProfile.
// A console family is never a console: choosing one never connects to, identifies or checks a console, never relabels a console a session
// identified, and never makes the engine accept a console, a backup or a firmware it would otherwise refuse.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace CcsBackup.Ui
{
    public sealed partial class BackupWorkspace
    {
        /// <summary>a test can answer the window's questions (title, text, buttons) → the chosen button's index (-1: closed without one)</summary>
#pragma warning disable CS0649   // set only by tests
        internal static Func<string, string, string[], int> AskHook;
        /// <summary>a test or the page renderer can show the window's own dialogs without waiting for them (null: shown as dialogs)</summary>
        internal static Func<Window, bool?> ModalHook;
#pragma warning restore CS0649
        internal static bool? Modal(Window w) => ModalHook != null ? ModalHook(w) : w.ShowDialog();

        /// <summary>a question as a dialog of the window (or of another owned window): the chosen button's index; cancelIndex (or -1 when
        /// there is none) when it is closed with Escape or its X</summary>
        internal int Ask(string title, string body, string[] buttons, int defaultIndex, int cancelIndex, Window owner = null)
        {
            if (AskHook != null) { var k = AskHook(title, body, buttons); return k < 0 ? cancelIndex : k; }
            var dlg = Owned(title, 560, false, owner);
            int chosen = cancelIndex;
            dlg.Content = DriverDialogPanel(title, body, buttons, defaultIndex, cancelIndex, null, k => { chosen = k; dlg.Close(); });
            Modal(dlg);
            return chosen;
        }

        /// <summary>a notice in the Workbench's own look (one OK button), in place of a Windows message box</summary>
        internal void Tell(string title, string body, Window owner = null) => Ask(title, body, new[] { "OK" }, 0, 0, owner);

        /// <summary>a window owned by this one (or by owner), in the Workbench's look: its resources, its dark title bar, Escape closes it</summary>
        internal Window Owned(string title, double width, bool resizable, Window owner = null)
        {
            var o = owner ?? (IsLoaded ? this : null);
            var w = new Window
            {
                Title = title, Owner = o, Width = width, SizeToContent = resizable ? SizeToContent.Manual : SizeToContent.Height, ResizeMode = resizable ? ResizeMode.CanResize : ResizeMode.NoResize,
                WindowStartupLocation = o != null ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen, Background = Res<Brush>("B.Bg"), ShowInTaskbar = false,
                UseLayoutRounding = true, SnapsToDevicePixels = true,
            };
            foreach (var md in Resources.MergedDictionaries) w.Resources.MergedDictionaries.Add(md);
            foreach (var k in Resources.Keys) w.Resources[k] = Resources[k];   // the Workbench's own derived styles (DeriveStyles)
            TextOptions.SetTextFormattingMode(w, TextFormattingMode.Ideal);
            w.SourceInitialized += (s, e) => WindowFrame.Prepare(w); NewTermScope(w); w.Deactivated += (s, e) => DropHover();
            // Escape closes the window, except while a choice list is open (Escape closes the list first, as everywhere in Windows)
            w.PreviewKeyDown += (s, e) => { if (e.Key == Key.Escape && !e.Handled && !(Keyboard.FocusedElement is ComboBoxItem)) { e.Handled = true; if (!CloseTerm(true)) w.Close(); } };
            return w;
        }

        // ------------------------------------------------------------------ the console family: its information
        /// <summary>everything Workbench says about a console family: what it is, its regional names and what
        /// choosing it means. The same words in About This Console and in Help.</summary>
        internal StackPanel ConsoleInfo(ConsoleProfile p)
        {
            var sp = new StackPanel();
            sp.Children.Add(T(p.Name, Ty.CardTitle, true, false, new Thickness(0, 0, 0, 6)));
            sp.Children.Add(T(p.About, Ty.Text));
            if (p.RegionalNames != null && p.RegionalNames.Length > 0)
            {
                sp.Children.Add(T("Its names", Ty.Heading, true, false, new Thickness(0, 10, 0, 4)));
                foreach (var (region, name) in p.RegionalNames)
                {
                    var tb = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 13, Margin = new Thickness(0, 2, 0, 4) };
                    tb.Inlines.Add(new System.Windows.Documents.Run(region + ": ") { Foreground = Res<Brush>("B.Muted") }); tb.Inlines.Add(new System.Windows.Documents.Run(name));
                    AutomationProperties.SetName(tb, region + ": " + name); sp.Children.Add(tb);
                }
            }
            foreach (var (head, text) in p.Sections ?? new (string, string)[0])
            {
                sp.Children.Add(T(head, Ty.Heading, true, false, new Thickness(0, 10, 0, 4)));
                foreach (var para in text.Split(new[] { "\n\n" }, StringSplitOptions.RemoveEmptyEntries)) sp.Children.Add(T(para, Ty.Text));
            }
            return sp;
        }

        /// <summary>what this window knows about the console in front of it: only the latest session's own identification, an earlier
        /// session's apart and marked earlier. Never a full hardware ID.</summary>
        StackPanel SessionInfo()
        {
            var sp = new StackPanel();
            sp.Children.Add(T("The console in front of you", Ty.Heading, true, false, new Thickness(0, 10, 0, 4)));
            sp.Children.Add(T(ConsoleStatusText(out var earlier) + ".", Ty.Text));
            if (earlier != null) sp.Children.Add(T(earlier + ".", Ty.Small, false, true));
            sp.Children.Add(T("Workbench identifies a console only in a console task's own session. Labels such as \"Console 73f23039\" are short names worked out from a console's own hardware ID: they keep backups of different consoles apart without showing the full ID.", Ty.Small, false, true, new Thickness(0, 4, 0, 0)));
            return sp;
        }

        // ------------------------------------------------------------------ About This Console
        bool aboutOpen;
        /// <summary>the one About This Console window (every entry point opens this); the main window's page and task stay as they are</summary>
        internal void OpenAboutConsole()
        {
            if (aboutOpen) return;
            ConsoleProfile use = null;
            var w = AboutWindow(p => use = p);
            aboutOpen = true;
            try { Modal(w); } finally { aboutOpen = false; }
            if (use != null && !ApplyProfile(use, out var refused)) UpdateStatus(refused);
        }

        /// <summary>the About This Console window, not shown yet; onUse gets the console to apply when it closes with Use</summary>
        internal Window AboutWindow(Action<ConsoleProfile> onUse)
        {
            var entry = profile; var draft = entry;
            var w = Owned("About This Console", 720, true); w.Height = Math.Min(760, Math.Max(520, ActualHeight - 60)); w.MinWidth = 520; w.MinHeight = 420;
            var root = new DockPanel { Margin = new Thickness(24, 18, 24, 16) };
            var head = new StackPanel(); DockPanel.SetDock(head, Dock.Top);
            head.Children.Add(T("About This Console", 20, true, false, new Thickness(0, 0, 0, 10)));
            var pick = ConsoleChoiceList(entry);
            if (pick.SelectedIndex < 0) pick.SelectedIndex = 0;
            AutomationProperties.SetName(pick, "Console to read about");
            head.Children.Add(Field("Console to read about", pick, null, 640));
            var problem = new ContentControl(); head.Children.Add(problem);
            var hint = T("Reading about another console here does not change the console Workbench works with. When you close this window, Workbench asks before it uses a different one.", Ty.Small, false, true, new Thickness(0, -4, 0, 8));
            hint.Visibility = ConsoleProfile.Supported.Count > 1 ? Visibility.Visible : Visibility.Collapsed; head.Children.Add(hint);
            root.Children.Add(head);
            var close = B("Close", "Btn.Primary", () => w.Close()); close.Margin = new Thickness(0, 10, 0, 0); close.IsCancel = false;
            var foot = new WrapPanel(); foot.Children.Add(close); DockPanel.SetDock(foot, Dock.Bottom); root.Children.Add(foot);
            var body = new ContentControl();
            var sv = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = body, Focusable = false };
            root.Children.Add(sv);
            void Fill()
            {
                var sp = new StackPanel(); sp.Children.Add(ConsoleInfo(draft));
                if (draft == profile) sp.Children.Add(SessionInfo());
                else sp.Children.Add(Note("Workbench works with " + profile.Name + " now. This page only shows " + draft.Name + ".", "info"));
                body.Content = Card(sp); sv.ScrollToTop();
            }
            pick.SelectionChanged += (s, e) => { if ((pick.SelectedItem as ComboBoxItem)?.Tag is ConsoleProfile p) { draft = p; problem.Content = null; Fill(); } };
            Fill();
            w.Content = root;
            w.Closing += (s, e) =>
            {
                if (draft == entry || draft == profile) return;   // the same as at the start (or already the window's): just close
                var (k, use) = ChangeConsole(draft, w);
                if (k == ChangeKeep) return;                              // Keep Current: close; nothing changes
                if (k != ChangeUse) { e.Cancel = true; return; }          // Back to Help (also Escape and the question's X): stay, with the draft
                if (use == null || use == profile) return;                // Use Selection on the console Workbench already uses: nothing to change
                var why = ProfileBlocked();
                if (why != null) { e.Cancel = true; problem.Content = Note(why, "warn"); return; }
                onUse(use);                                               // Use Selection: applied once the window has closed (ApplyProfile)
            };
            w.Loaded += (s, e) => pick.Focus();
            return w;
        }

        // ------------------------------------------------------------------ Change console? (WB-UX-10)
        internal const int ChangeUse = 0, ChangeKeep = 1, ChangeBack = 2;
        /// <summary>a test can answer Change console? (the console Workbench uses, Help's choice) → (the button: ChangeUse, ChangeKeep or
        /// ChangeBack, and the console chosen in its Use console list)</summary>
#pragma warning disable CS0649   // set only by tests
        internal static Func<ConsoleProfile, ConsoleProfile, (int, ConsoleProfile)> ChangeConsoleHook;
#pragma warning restore CS0649

        /// <summary>the question when console Help closes on a different console than the window's: (the button, the console its Use console
        /// list holds). Escape and its X are Back to Help.</summary>
        internal (int button, ConsoleProfile use) ChangeConsole(ConsoleProfile draft, Window owner)
        {
            if (ChangeConsoleHook != null) return ChangeConsoleHook(profile, draft);
            int chosen = ChangeBack; ConsoleProfile use = draft;
            var dlg = ChangeConsoleWindow(draft, owner, (k, p) => { chosen = k; use = p; });
            Modal(dlg);
            return (chosen, use);
        }

        /// <summary>Change console?, not shown yet: why it asks (first), the current console once, a Use console choice list that starts on
        /// Help's choice and can still be changed, and three short buttons. done(button, console) runs when a button closes it.</summary>
        internal Window ChangeConsoleWindow(ConsoleProfile draft, Window owner, Action<int, ConsoleProfile> done)
        {
            var dlg = Owned("Change console?", 560, false, owner);
            var sp = new StackPanel { Margin = new Thickness(24, 20, 24, 16) };
            sp.Children.Add(T("Change console?", 17, true, false, new Thickness(0, 0, 0, 8)));
            sp.Children.Add(T("You selected a different console in Help. Do you want to use that selection in Workbench?", 13.5, false, false, new Thickness(0, 0, 0, 14)));
            var g = new Grid { Margin = new Thickness(0, 0, 0, 16) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); g.ColumnDefinitions.Add(new ColumnDefinition());
            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var curLabel = new TextBlock { Text = "Current console:", Style = Res<Style>("T.Muted"), FontSize = 13, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 14, 12) };
            var cur = new TextBlock { Text = profile.Name, FontSize = 13.5, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 0, 12) };
            SetNoTerms(cur, true); AutomationProperties.SetName(cur, profile.Name); AutomationProperties.SetLabeledBy(cur, curLabel);
            Grid.SetColumn(cur, 1); g.Children.Add(curLabel); g.Children.Add(cur);
            var useLabel = new TextBlock { Text = "Use console:", Style = Res<Style>("T.Muted"), FontSize = 13, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 14, 0) };
            var list = ConsoleChoiceList(draft); list.MinWidth = 0; list.HorizontalAlignment = HorizontalAlignment.Stretch;
            AutomationProperties.SetName(list, "Use console"); AutomationProperties.SetLabeledBy(list, useLabel);
            Grid.SetRow(useLabel, 1); Grid.SetRow(list, 1); Grid.SetColumn(list, 1); g.Children.Add(useLabel); g.Children.Add(list);
            sp.Children.Add(g);
            ConsoleProfile Chosen() => (list.SelectedItem as ComboBoxItem)?.Tag as ConsoleProfile ?? draft;
            var row = new WrapPanel();
            var use = B("Use Selection", "Btn.Primary", () => { done(ChangeUse, Chosen()); dlg.Close(); }); use.IsDefault = true;
            var keep = B("Keep Current", "Btn", () => { done(ChangeKeep, Chosen()); dlg.Close(); });
            var back = B("Back to Help", "Btn.Ghost", () => { done(ChangeBack, Chosen()); dlg.Close(); });
            row.Children.Add(use); row.Children.Add(keep); row.Children.Add(back);
            sp.Children.Add(row);
            dlg.Content = sp;
            dlg.Loaded += (s, e) => list.Focus();
            return dlg;
        }

        // ------------------------------------------------------------------ the one way the window's console family changes
        bool selectingProfile;
        /// <summary>why the window's console family cannot change now (null: it can): never while a console task or session is open</summary>
        string ProfileBlocked() => worker != null && worker.Running
            ? "A console task or session is open, so the console Workbench works with cannot change now. Finish or cancel it first; you can keep reading here." : null;

        /// <summary>the main console choice and About This Console's Use both come here: refused while a console task or session is open, or
        /// when the open page's unsaved changes are kept (the page's own question); then the family changes once, both choices show it, the
        /// preferences note it (Settings), and the open section opens again in its words. Nothing is cancelled, disconnected, retargeted or
        /// thrown away to make it happen, and no console identity changes.</summary>
        internal bool ApplyProfile(ConsoleProfile p, out string refused)
        {
            refused = null;
            if (p == null || p == profile) return true;
            refused = ProfileBlocked();
            if (refused != null) return false;
            if (!LeaveOk()) { refused = "The open page's changes were kept, so Workbench still works with " + profile.Name + "."; return false; }
            leaveCheck = null;
            profile = p; settings.ConsoleProfile = p.Id;
            ShowProfileInStrip();
            PrefsChanged();
            Go(section);
            UpdateStatus("Workbench now works with " + p.Name + ".");
            return true;
        }

        void ShowProfileInStrip()
        {
            if (consoleChoice == null) return;
            selectingProfile = true;
            try { foreach (ComboBoxItem it in consoleChoice.Items) if (it.Tag == profile) consoleChoice.SelectedItem = it; }
            finally { selectingProfile = false; }
        }

        void OnStripProfileChanged()
        {
            if (selectingProfile || !((consoleChoice.SelectedItem as ComboBoxItem)?.Tag is ConsoleProfile p) || p == profile) return;
            if (!ApplyProfile(p, out var refused)) { ShowProfileInStrip(); UpdateStatus(refused); }
        }
    }
}
