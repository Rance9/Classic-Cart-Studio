// Classic Cart Studio console backup and restore window. Copyright (C) 2026 Rance9. GPL-3.0-or-later.
// CCS Console Workbench: explained words in their own sentences. Every block of text in a Workbench window (its pages, Help, About This
// Console and the other windows it opens), in both hosts, shows the glossary's words (WorkbenchGlossary.cs) where they are, with a dotted
// underline that is always visible. A click, a tap, or the keyboard (Tab to the word, then Enter or Space) opens the word's short explanation
// in a small panel just under it; Escape, Close or a click elsewhere shuts it, and Escape or Close puts the focus back on the word. One
// explanation is open at a time, nothing opens on hover, and the sentence keeps its font, size, color and wrapping. The panel starts at the
// word's left edge on every PC, and assistive technology reads each word as a link that expands and collapses (open or closed).
// Left plain: words inside a button, a check box, a choice or any other control (no control inside a control), headings, typed or chosen
// text, names of games and files, paths and web addresses (the glossary never matches inside them), status lines, and anything marked
// NoTerms (Credits with its license texts, the glossary itself, lists of a backup's files). Risks and confirmations never live only in an
// explanation: they stay on the page.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

namespace CcsBackup.Ui
{
    public sealed partial class BackupWorkspace
    {
        /// <summary>set on an element whose text (and its children's) must stay exactly as it is, with no explained words: verbatim legal
        /// text, the glossary's own entries, a backup's file and game names, status lines</summary>
        public static readonly DependencyProperty NoTermsProperty = DependencyProperty.RegisterAttached("NoTerms", typeof(bool), typeof(BackupWorkspace),
            new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.Inherits));
        public static void SetNoTerms(DependencyObject d, bool v) => d.SetValue(NoTermsProperty, v);
        public static bool GetNoTerms(DependencyObject d) => (bool)d.GetValue(NoTermsProperty);
        static readonly DependencyProperty TermsDoneProperty = DependencyProperty.RegisterAttached("TermsDone", typeof(bool), typeof(BackupWorkspace), new PropertyMetadata(false));
        /// <summary>on a page, a Help topic or a window's content: the product names already explained there (explained once per page)</summary>
        static readonly DependencyProperty TermScopeProperty = DependencyProperty.RegisterAttached("TermScope", typeof(HashSet<string>), typeof(BackupWorkspace), new PropertyMetadata(null));
        static readonly DependencyProperty ScopePendingProperty = DependencyProperty.RegisterAttached("ScopePending", typeof(bool), typeof(BackupWorkspace), new PropertyMetadata(false));
        internal static void NewTermScope(DependencyObject d) => d?.SetValue(TermScopeProperty, new HashSet<string>());
        static DependencyObject ScopeRootOf(DependencyObject d)
        {
            for (var p = d; p != null; p = (p is Visual ? VisualTreeHelper.GetParent(p) : null) ?? LogicalTreeHelper.GetParent(p)) { if (p.GetValue(TermScopeProperty) is HashSet<string>) return p; }
            return null;
        }

        /// <summary>what a term word is called for automation and probes: its Tag is "term:" plus the glossary key</summary>
        internal const string TermTag = "term:";
        static bool termsHooked;

        /// <summary>once per process: every text block that appears in a Workbench window gets its glossary words explained, when it is first
        /// laid out (WPF raises SizeChanged for every element on its first layout; Loaded reaches only parts of a window with their own Loaded
        /// handlers, so it is not used)</summary>
        static void HookTerms()
        {
            if (termsHooked) return; termsHooked = true;
            EventManager.RegisterClassHandler(typeof(TextBlock), SizeChangedEvent, new SizeChangedEventHandler((s, e) => { if (s is TextBlock tb) OnTextLoaded(tb); }));
        }

        /// <summary>the Workbench window a text block is in: the main window, or a window it opened (Help, About This Console, a picture)</summary>
        static BackupWorkspace WorkspaceOf(DependencyObject d)
        {
            var w = Window.GetWindow(d);
            for (int i = 0; w != null && i < 4; i++) { if (w is BackupWorkspace ws) return ws; w = w.Owner; }
            return null;
        }

        /// <summary>a text block appeared: inside a page, a Help topic or a window, the whole of it is explained once it is laid out, in reading
        /// order (so a product name is explained where it first appears on the page); anywhere else, the block at once</summary>
        static void OnTextLoaded(TextBlock tb)
        {
            if ((bool)tb.GetValue(TermsDoneProperty)) return;
            var ws = WorkspaceOf(tb); if (ws == null) { tb.SetValue(TermsDoneProperty, true); return; }
            var root = ScopeRootOf(tb);
            if (root == null) { tb.SetValue(TermsDoneProperty, true); if (Eligible(tb)) ws.ExplainText(tb, new HashSet<string>()); return; }
            if ((bool)root.GetValue(ScopePendingProperty)) return;
            root.SetValue(ScopePendingProperty, true);
            ws.Dispatcher.BeginInvoke(new Action(() => { root.SetValue(ScopePendingProperty, false); ws.ExplainScope(root); }), System.Windows.Threading.DispatcherPriority.Loaded);
        }

        /// <summary>every text block not yet explained in a page, a Help topic or a window, in reading order (the order of the tree). The product
        /// names already explained there are the ones its text shows now (text the page replaced or hides does not count).</summary>
        internal int ExplainScope(DependencyObject root)
        {
            var page = new HashSet<string>(); var todo = new List<TextBlock>();
            void Walk(DependencyObject d)
            {
                if (d is UIElement ue && !ue.IsVisible) return;   // a hidden part is explained when it appears (its first layout starts another pass)
                if (d is TextBlock tb)
                {
                    foreach (var h in Links(tb.Inlines)) if (h.Tag is string t && t.StartsWith(TermTag, StringComparison.Ordinal)) page.Add(t.Substring(TermTag.Length));
                    if (!(bool)tb.GetValue(TermsDoneProperty)) todo.Add(tb);
                }
                if (!(d is Visual)) return;
                int c = VisualTreeHelper.GetChildrenCount(d);
                for (int i = 0; i < c; i++) Walk(VisualTreeHelper.GetChild(d, i));
            }
            Walk(root);
            int n = 0;
            foreach (var tb in todo) { tb.SetValue(TermsDoneProperty, true); if (Eligible(tb)) n += ExplainText(tb, page); }
            return n;
        }
        static IEnumerable<Hyperlink> Links(InlineCollection ic)
        {
            foreach (var i in ic) { if (i is Hyperlink h) yield return h; else if (i is Span s) foreach (var x in Links(s.Inlines)) yield return x; }
        }

        /// <summary>whether a text block's words may be explained: plain prose or a label, not a heading, not shortened, not marked NoTerms, and
        /// not inside a button, a check box, a choice, a list row, a text box, a menu or a tab (no control inside a control)</summary>
        static bool Eligible(TextBlock tb)
        {
            if (GetNoTerms(tb) || tb.FontWeight.ToOpenTypeWeight() >= 600 || tb.TextTrimming != TextTrimming.None) return false;
            for (DependencyObject p = VisualTreeHelper.GetParent(tb); p != null; p = VisualTreeHelper.GetParent(p))
            {
                if (p is Window) return true;
                if (p is ButtonBase || p is ComboBox || p is ComboBoxItem || p is ListBoxItem || p is TextBoxBase || p is MenuItem || p is TabItem) return false;
            }
            return true;
        }

        /// <summary>turns the glossary's words in a block of text into explained words (the first place each term appears in it); the block's
        /// words for screen readers stay its whole sentence</summary>
        internal int ExplainText(TextBlock tb, HashSet<string> page)
        {
            var before = HelpInlineText(tb);
            var used = new HashSet<string>(); int n = Explain(tb, tb.Inlines, used, page);
            if (n > 0 && string.IsNullOrEmpty(AutomationProperties.GetName(tb))) AutomationProperties.SetName(tb, before);
            return n;
        }

        int Explain(TextBlock host, InlineCollection inlines, HashSet<string> used, HashSet<string> pageUsed)
        {
            int n = 0;
            foreach (var inl in inlines.ToList())
            {
                if (inl is Hyperlink) continue;   // a link already (a Help topic link or an explained word)
                if (inl is Span sp) { n += Explain(host, sp.Inlines, used, pageUsed); continue; }
                if (!(inl is Run r) || string.IsNullOrEmpty(r.Text)) continue;
                var m = Glossary.Matches(r.Text, used, pageUsed); if (m.Count == 0) continue;
                int at = 0; var text = r.Text;
                foreach (var (start, length, term) in m)
                {
                    if (start > at) inlines.InsertBefore(r, Piece(r, text.Substring(at, start - at)));
                    inlines.InsertBefore(r, TermLink(term, text.Substring(start, length), host, r));
                    at = start + length; n++;
                }
                if (at < text.Length) inlines.InsertBefore(r, Piece(r, text.Substring(at)));
                inlines.Remove(r);
            }
            return n;
        }

        /// <summary>a piece of a run with the run's own look</summary>
        static Run Piece(Run r, string text)
        {
            var x = new Run(text) { FontWeight = r.FontWeight, FontStyle = r.FontStyle };
            if (r.ReadLocalValue(TextElement.ForegroundProperty) != DependencyProperty.UnsetValue) x.Foreground = r.Foreground;
            if (r.ReadLocalValue(TextElement.FontSizeProperty) != DependencyProperty.UnsetValue) x.FontSize = r.FontSize;
            return x;
        }

        TextDecorationCollection TermUnderline(bool focused)
        {
            var brush = Res<Brush>("B.Red3") ?? Brushes.IndianRed;
            var pen = new Pen(brush, focused ? 2 : 1.5) { DashStyle = focused ? DashStyles.Solid : new DashStyle(new double[] { 1, 1 }, 0), DashCap = PenLineCap.Flat };
            pen.Freeze();
            var c = new TextDecorationCollection { new TextDecoration(TextDecorationLocation.Underline, pen, 2, TextDecorationUnit.Pixel, TextDecorationUnit.Pixel) };
            c.Freeze(); return c;
        }

        /// <summary>an explained word: the word itself, in its sentence's own color, with a dotted underline; Enter, Space or a click opens its
        /// explanation just under it</summary>
        Hyperlink TermLink(GlossaryTerm t, string shown, TextBlock host, Run like)
        {
            var h = new TermHyperlink(Piece(like, shown)) { Cursor = Cursors.Hand, FocusVisualStyle = null, Tag = TermTag + t.Key, TextDecorations = TermUnderline(false) };
            h.SetBinding(TextElement.ForegroundProperty, new Binding("Foreground") { Source = host });   // the sentence's own color, not a link color
            AutomationProperties.SetName(h, shown);
            AutomationProperties.SetHelpText(h, "Explained word: opens a short explanation of " + t.Word);
            SetTermExpanded(h, false);
            ToolTipService.SetIsEnabled(h, false);
            var focusBg = new SolidColorBrush(Color.FromArgb(0x40, 0xFF, 0x3B, 0x45)); focusBg.Freeze();
            h.GotKeyboardFocus += (s, e) => { h.Background = focusBg; h.TextDecorations = TermUnderline(true); };
            h.LostKeyboardFocus += (s, e) => { h.Background = null; h.TextDecorations = TermUnderline(false); };
            h.Toggle = () => ToggleTerm(h, host, t);
            h.Click += (s, e) => ToggleTerm(h, host, t);
            h.PreviewKeyDown += (s, e) => { if (e.Key == Key.Space) { ToggleTerm(h, host, t); e.Handled = true; } };
            return h;
        }

        /// <summary>an explained word's open or closed state, for assistive technology: UI Automation's expand/collapse state and the item
        /// status (the window's equivalent of the website's aria-expanded), with a change notice when it changes</summary>
        static void SetTermExpanded(Hyperlink h, bool expanded)
        {
            string was = AutomationProperties.GetItemStatus(h), now = expanded ? "expanded" : "collapsed";
            AutomationProperties.SetItemStatus(h, now);
            if (was == now || string.IsNullOrEmpty(was)) return;
            try
            {
                if (System.Windows.Automation.Peers.AutomationPeer.ListenerExists(System.Windows.Automation.Peers.AutomationEvents.PropertyChanged)
                    && System.Windows.Automation.Peers.ContentElementAutomationPeer.FromElement(h) is System.Windows.Automation.Peers.AutomationPeer p)
                {
                    p.RaisePropertyChangedEvent(ExpandCollapsePatternIdentifiers.ExpandCollapseStateProperty,
                        expanded ? ExpandCollapseState.Collapsed : ExpandCollapseState.Expanded, expanded ? ExpandCollapseState.Expanded : ExpandCollapseState.Collapsed);
                    p.RaisePropertyChangedEvent(AutomationElementIdentifiers.ItemStatusProperty, was, now);
                }
            }
            catch (InvalidOperationException) { }
        }

        /// <summary>an explained word's link: UI Automation sees a hyperlink that also expands and collapses (its explanation)</summary>
        sealed class TermHyperlink : Hyperlink
        {
            public TermHyperlink(Inline i) : base(i) { }
            internal Action Toggle;
            protected override System.Windows.Automation.Peers.AutomationPeer OnCreateAutomationPeer() => new TermLinkPeer(this);
        }

        sealed class TermLinkPeer : System.Windows.Automation.Peers.HyperlinkAutomationPeer, System.Windows.Automation.Provider.IExpandCollapseProvider
        {
            public TermLinkPeer(TermHyperlink o) : base(o) { }
            bool Expanded => AutomationProperties.GetItemStatus(Owner) == "expanded";
            public override object GetPattern(System.Windows.Automation.Peers.PatternInterface p) =>
                p == System.Windows.Automation.Peers.PatternInterface.ExpandCollapse ? this : base.GetPattern(p);
            protected override string GetItemStatusCore() => AutomationProperties.GetItemStatus(Owner);
            public ExpandCollapseState ExpandCollapseState => Expanded ? ExpandCollapseState.Expanded : ExpandCollapseState.Collapsed;
            public void Expand() { if (!Expanded) ((TermHyperlink)Owner).Toggle?.Invoke(); }
            public void Collapse() { if (Expanded) ((TermHyperlink)Owner).Toggle?.Invoke(); }
        }

        // ------------------------------------------------------------------ the explanation panel (one open at a time, in every window)
        static Popup openTerm; static Hyperlink openLink;
        /// <summary>the explanation open now (for tests and probes): the term's key, or null</summary>
        internal static string OpenTermKey => openTerm != null && openTerm.IsOpen ? (openLink?.Tag as string)?.Substring(TermTag.Length) : null;
        internal static Popup OpenTermPopup => openTerm != null && openTerm.IsOpen ? openTerm : null;

        void ToggleTerm(Hyperlink h, TextBlock host, GlossaryTerm t)
        {
            if (openTerm != null && openLink == h && openTerm.IsOpen) { CloseTerm(true); return; }
            CloseTerm(false);
            var pop = new Popup { PlacementTarget = host, Placement = PlacementMode.Bottom, StaysOpen = false, AllowsTransparency = true, Focusable = false };
            var panel = TermPanel(t, () => CloseTerm(true), () => { CloseTerm(false); OpenHelp("glossary"); }, out var close);
            pop.Child = panel;
            var rect = LinkRect(h, host);
            if (!rect.IsEmpty)
            {
                // the panel starts at the word's left edge on every PC: a placement rectangle as wide as the panel, so Windows' handedness
                // setting (which lines a panel up with the right edge for right-handed tablet use) gives the same place; near the screen's
                // edge Windows moves it inside
                panel.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                pop.PlacementRectangle = new Rect(rect.Left, rect.Top, Math.Max(rect.Width, panel.DesiredSize.Width), rect.Height);
            }
            pop.Opened += (s, e) =>
            {
                // where it really is on the screen can still differ by a few pixels (its size at the screen's scale, the handedness
                // setting's alignment): line its left edge up with the word's (Windows still keeps it inside the screen)
                try
                {
                    var src = PresentationSource.FromVisual(panel);
                    if (!rect.IsEmpty && src?.CompositionTarget != null && PresentationSource.FromVisual(host) != null)
                    {
                        double dx = (host.PointToScreen(new Point(rect.Left, rect.Bottom)).X - panel.PointToScreen(new Point(0, 0)).X) / src.CompositionTarget.TransformToDevice.M11;
                        if (Math.Abs(dx) > 0.5) pop.HorizontalOffset += dx;
                    }
                }
                catch (InvalidOperationException) { }
                SetTermExpanded(h, true); close.Focus();
            };
            pop.Closed += (s, e) => { SetTermExpanded(h, false); if (openTerm == pop) { openTerm = null; openLink = null; } };
            openTerm = pop; openLink = h; pop.IsOpen = true;
        }

        /// <summary>shuts the open explanation, if any; refocus: the focus goes back to its word. True when one was open.</summary>
        internal static bool CloseTerm(bool refocus)
        {
            var p = openTerm; var l = openLink; openTerm = null; openLink = null;
            if (p == null) return false;
            bool was = p.IsOpen; p.IsOpen = false;
            if (refocus && l != null) { try { l.Focus(); } catch (InvalidOperationException) { } }
            return was;
        }

        /// <summary>where the word is in its block (its first line when it wraps), for the panel to open just under it</summary>
        static Rect LinkRect(Hyperlink h, TextBlock host)
        {
            try
            {
                var a = h.ContentStart.GetCharacterRect(LogicalDirection.Forward); var b = h.ContentEnd.GetCharacterRect(LogicalDirection.Backward);
                if (a.IsEmpty || double.IsInfinity(a.Left)) return Rect.Empty;
                if (!b.IsEmpty && Math.Abs(a.Top - b.Top) < 1 && b.Right > a.Left) return new Rect(a.Left, a.Top, b.Right - a.Left, Math.Max(a.Height, b.Height));
                return new Rect(a.Left, a.Top, Math.Max(1, host.ActualWidth - a.Left), a.Height);
            }
            catch (Exception) { return Rect.Empty; }
        }

        /// <summary>a word's explanation panel: the word, its explanation, its technical name, Close and More in Help; Escape closes it</summary>
        internal Border TermPanel(GlossaryTerm t, Action onClose, Action onHelp, out Button close)
        {
            close = B("Close", "Btn.Ghost", onClose); close.Margin = new Thickness(0, 8, 0, 0); close.MinHeight = 30;
            var help = new Button { Content = "More in Help", Style = Res<Style>("Btn.Link"), Margin = new Thickness(14, 8, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            AutomationProperties.SetName(help, "More in Help"); help.Click += (s, e) => onHelp();
            var body = new StackPanel { MaxWidth = 380 };
            body.Children.Add(new TextBlock { Text = t.Word, FontSize = 13.5, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4), TextWrapping = TextWrapping.Wrap });
            body.Children.Add(new TextBlock { Text = t.Text, FontSize = 13, LineHeight = 19, TextWrapping = TextWrapping.Wrap });
            if (t.Tech != null) body.Children.Add(new TextBlock { Text = "Technical name: " + t.Tech, FontSize = 12, Foreground = Res<Brush>("B.Muted"), Margin = new Thickness(0, 6, 0, 0), TextWrapping = TextWrapping.Wrap });
            var row = new StackPanel { Orientation = Orientation.Horizontal }; row.Children.Add(close); row.Children.Add(help); body.Children.Add(row);
            var panel = new Border { Background = Res<Brush>("B.MenuSurface") ?? Res<Brush>("B.Surface2"), BorderBrush = Res<Brush>("B.Red3") ?? Res<Brush>("B.Line2"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(16, 12, 16, 12), Margin = new Thickness(0, 4, 8, 8), Child = body };
            // a popup is not inside the window's tree: its text color, font and the window's styles are given here
            panel.SetValue(TextElement.ForegroundProperty, Res<Brush>("B.Ink")); var ui = Res<FontFamily>("F.Ui"); if (ui != null) panel.SetValue(TextElement.FontFamilyProperty, ui);
            foreach (var k in Resources.Keys) panel.Resources[k] = Resources[k];
            foreach (var md in Resources.MergedDictionaries) panel.Resources.MergedDictionaries.Add(md);
            SetNoTerms(panel, true);
            AutomationProperties.SetName(panel, t.Word + ": " + t.Text);
            panel.PreviewKeyDown += (s, e) => { if (e.Key == Key.Escape) { onClose(); e.Handled = true; } };
            return panel;
        }

        /// <summary>the words of a text block built from pieces (or its Text)</summary>
        static string HelpInlineText(TextBlock tb) => tb.Inlines.Count > 0 ? InlineText(tb) : tb.Text ?? "";
    }
}
