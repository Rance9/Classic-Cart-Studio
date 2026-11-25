// Classic Cart Studio console backup and restore window. Copyright (C) 2026 Rance9. GPL-3.0-or-later.
// CCS Console Workbench: the window's layout pieces. Every page is built from these, so nothing is cut off at any window size the window
// allows: cards wrap to as many columns as fit (CardPanel), a row of buttons wraps to the next line, a button's own text wraps rather than
// being cut, labeled fields stretch to the page, and a page never grows wider than its column.
using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace CcsBackup.Ui
{
    /// <summary>cards side by side as the width allows: each at least MinItemWidth wide, at most MaxColumns in a row, every card in a row as
    /// tall as the tallest (so their buttons line up)</summary>
    public sealed class CardPanel : Panel
    {
        public double MinItemWidth { get; set; } = 280;
        public int MaxColumns { get; set; } = 3;
        public double Gap { get; set; } = 16;
        double measuredWidth = -1;

        int Columns(double width)
        {
            int n = InternalChildren.Count; if (n == 0) return 1;
            if (double.IsInfinity(width)) return Math.Min(n, MaxColumns);
            int c = (int)Math.Floor((width + Gap) / (MinItemWidth + Gap));
            return Math.Max(1, Math.Min(Math.Min(c, MaxColumns), n));
        }
        double ItemWidth(double width, int cols) => double.IsInfinity(width) ? MinItemWidth : Math.Max(0, (width - Gap * (cols - 1)) / cols);

        double[] Rows(double width, out int cols, out double w)
        {
            cols = Columns(width); w = ItemWidth(width, cols); int n = InternalChildren.Count;
            var rows = new double[(n + cols - 1) / cols];
            for (int i = 0; i < n; i++) { var c = InternalChildren[i]; c.Measure(new Size(w, double.PositiveInfinity)); rows[i / cols] = Math.Max(rows[i / cols], c.DesiredSize.Height); }
            measuredWidth = width; return rows;
        }

        protected override Size MeasureOverride(Size available)
        {
            var rows = Rows(available.Width, out int cols, out double w);
            double h = rows.Sum() + Gap * Math.Max(0, rows.Length - 1);
            return new Size(double.IsInfinity(available.Width) ? cols * w + Gap * (cols - 1) : available.Width, h);
        }

        protected override Size ArrangeOverride(Size final)
        {
            var rows = Rows(final.Width, out int cols, out double w);
            double y = 0;
            for (int r = 0; r < rows.Length; r++)
            {
                for (int k = 0; k < cols; k++) { int i = r * cols + k; if (i >= InternalChildren.Count) break; InternalChildren[i].Arrange(new Rect(k * (w + Gap), y, w, rows[r])); }
                y += rows[r] + Gap;
            }
            return final;
        }
    }

    /// <summary>a template for plain text only: anything else (a picture, a composed label) is shown as it is</summary>
    public sealed class TextOnlyTemplate : DataTemplateSelector
    {
        public DataTemplate Template { get; set; }
        public override DataTemplate SelectTemplate(object item, DependencyObject container) => item is string ? Template : null;
    }

    /// <summary>the Workbench's one type scale (device-independent pixels). In the side column a group's heading is larger than the
    /// sections listed under it.</summary>
    static class Ty
    {
        public const double PageTitle = 22, CardTitle = 16.5, Heading = 15, NavHeading = 15, NavItem = 13.5, Body = 13.5, Text = 13, Small = 12.5, Fine = 12;
    }

    /// <summary>the kinds of file the Workbench works with, each with one name and one short description, used the same way in pickers,
    /// reviews, results and Help. None of them can be used as another.</summary>
    static class FileKinds
    {
        public const string ConsoleBackup = "Console backup", ConsoleBackupWhat = "A complete copy of one console's built-in storage, made by Back Up Console (a .zip file). Restore puts it back on that same console.";
        public const string ConsoleBackupFilter = "Console backups (*.zip)|*.zip";
        public const string StudioBackup = "Classic Cart Studio backup", StudioBackupWhat = "A collection or full Studio backup made in the Classic Cart Studio app (a .ccs file), with its game files. It is not a console backup and is not used here.";
        public const string GameSaves = "Game save file", GameSavesWhat = "A battery save converted from a backup for Classic Cart Studio's player, with a report beside it. The backup itself is never changed.";
        public const string HomeDesign = "Shared HOME Design", HomeDesignWhat = "Pictures and appearance settings for CCS HOME, made in HOME Designer (a .ccshome file). No games, saves or console files.";
        public const string Plan = "Shared Composition Plan", PlanWhat = "A plan from Console Composer (a .ccsplan file): the choices for a setup, with no game files, saves or private console details. It never changes a console by itself.";
        public const string Package = "Private Restore Package", PackageWhat = "Built by Console Composer for one console (a .zip file): the files to put on that console. It holds your own files, so keep it private.";
    }

    public sealed partial class BackupWorkspace
    {
        /// <summary>the Workbench's own versions of the theme's text boxes, choice lists, check boxes and choices, each based on the theme's
        /// style as this window finds it: typed text in the interface's font at a comfortable height, a long choice shortened with "…" in
        /// its box, and a check box's or choice's sentence wrapping</summary>
        void DeriveStyles()
        {
            void Derive(Type t, params Setter[] setters)
            {
                var s = new Style(t, TryFindResource(t) as Style);
                foreach (var x in setters) s.Setters.Add(x);
                Resources[t] = s;
            }
            var ui = Res<FontFamily>("F.Ui");
            Derive(typeof(TextBox), new Setter(Control.FontFamilyProperty, ui), new Setter(Control.FontSizeProperty, 13.0), new Setter(Control.PaddingProperty, new Thickness(8, 6, 8, 6)),
                   new Setter(MinHeightProperty, 34.0), new Setter(Control.VerticalContentAlignmentProperty, VerticalAlignment.Center));
            Derive(typeof(ComboBox), new Setter(MinHeightProperty, 34.0), new Setter(Control.FontSizeProperty, 13.0));
            // WB-UX-14: the theme's red glow around a focused choice list shows only while the keyboard is in use (FocusCue), not after a
            // choice made with the mouse
            if (Resources[typeof(ComboBox)] is Style combo)
                combo.Triggers.Add(new MultiTrigger { Conditions = { new Condition(IsKeyboardFocusWithinProperty, true), new Condition(FocusCue.ShownProperty, false) }, Setters = { new Setter(EffectProperty, null) } });
            var wrap = new TextOnlyTemplate { Template = Res<DataTemplate>("Wb.CheckText") };
            // a choice list's own box shows the chosen item with that item's template (not a selector), and every choice here is plain text
            Derive(typeof(ComboBoxItem), new Setter(ContentControl.ContentTemplateProperty, Res<DataTemplate>("Wb.ChoiceText")));
            Derive(typeof(CheckBox), new Setter(ContentControl.ContentTemplateSelectorProperty, wrap), new Setter(Control.FontSizeProperty, 13.0), new Setter(Control.ForegroundProperty, Res<Brush>("B.Ink")));
            Derive(typeof(RadioButton), new Setter(ContentControl.ContentTemplateSelectorProperty, wrap), new Setter(Control.FontSizeProperty, 13.0), new Setter(Control.ForegroundProperty, Res<Brush>("B.Ink")));
            // the theme's main button draws its own template without a disabled look: dimmed here like the other buttons, so a button that
            // cannot be used yet never looks ready
            if (TryFindResource("Btn.Primary") is Style prim)
            {
                var s = new Style(typeof(Button), prim);
                s.Triggers.Add(new Trigger { Property = IsEnabledProperty, Value = false, Setters = { new Setter(OpacityProperty, 0.45) } });
                Resources["Btn.Primary"] = s;
            }
        }

        /// <summary>a page's title and the sentence under it</summary>
        StackPanel PageHead(string title, string text)
        {
            var sp = new StackPanel { Margin = new Thickness(0, 0, 0, 4) };
            sp.Children.Add(PageTitle(title));
            if (!string.IsNullOrEmpty(text)) sp.Children.Add(new TextBlock { Text = text, Style = Res<Style>("T.Muted"), FontSize = 13.5, TextWrapping = TextWrapping.Wrap, MaxWidth = 780, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 0, 18) });
            else ((FrameworkElement)sp.Children[0]).Margin = new Thickness(0, 0, 0, 16);
            return sp;
        }
        TextBlock PageTitle(string title) => new TextBlock { Text = title, Style = Res<Style>("T.Title"), FontSize = 22, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 6) };

        /// <summary>WB-UX-13: a picture's caption, the one way every picture is captioned (Help's screenshots, About Classic Cart Studio's
        /// screenshots, a picture opened larger, HOME Designer's theme examples): italic, smaller, directly under its picture (style
        /// Wb.Caption). With under, it is never wider than that picture, so it reads as the picture's own line.</summary>
        internal TextBlock PictureCaption(string text, FrameworkElement under = null)
        {
            var t = new TextBlock { Text = text, Style = Res<Style>("Wb.Caption") };
            if (t.Style == null) { t.FontStyle = FontStyles.Italic; t.FontSize = Ty.Small; t.TextWrapping = TextWrapping.Wrap; t.HorizontalAlignment = HorizontalAlignment.Left; t.Margin = new Thickness(2, 6, 0, 0); }
            if (under != null) t.SetBinding(MaxWidthProperty, new System.Windows.Data.Binding("ActualWidth") { Source = under });
            return t;
        }

        /// <summary>WB-UX-12: a normal button with a left arrow, for going back (Help's Back, Settings' Back to Back Up Console): the same
        /// size and look as the window's other buttons, at the top left of what it belongs to</summary>
        internal Button BackButton(string text, Action click)
        {
            var b = new Button { Style = Res<Style>("Btn"), HorizontalAlignment = HorizontalAlignment.Left, MinHeight = 34, MinWidth = 96, Padding = new Thickness(12, 6, 16, 6) };
            b.Content = new StackPanel { Orientation = Orientation.Horizontal, Children = {
                new TextBlock { Text = "", FontFamily = Res<FontFamily>("F.Icons") ?? new FontFamily("Segoe MDL2 Assets"), FontSize = 13, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 1, 9, 0) },
                new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center } } };
            System.Windows.Automation.AutomationProperties.SetName(b, text);
            if (click != null) b.Click += (s, e) => click();
            return b;
        }

        /// <summary>a heading inside a page, above a group of cards or fields</summary>
        TextBlock Group(string title, double top = 8) => new TextBlock { Text = title, FontSize = 15, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, top, 0, 10) };

        CardPanel CardGrid(double minItemWidth, int maxColumns, params UIElement[] cards)
        {
            var p = new CardPanel { MinItemWidth = minItemWidth, MaxColumns = maxColumns, Margin = new Thickness(0, 0, 0, 16) };
            foreach (var c in cards) if (c != null) { if (c is FrameworkElement f) f.Margin = new Thickness(0); p.Children.Add(c); }
            return p;
        }

        /// <summary>a labeled input: the label above, the input across the page (up to maxWidth) and an optional button beside it</summary>
        FrameworkElement Field(string label, FrameworkElement input, UIElement action = null, double maxWidth = 680)
        {
            var sp = new StackPanel();
            sp.Children.Add(new TextBlock { Text = label, Style = Res<Style>("T.Muted"), FontSize = 12.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 5) });
            var g = new Grid(); g.ColumnDefinitions.Add(new ColumnDefinition()); g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            input.Width = double.NaN; input.MinWidth = 0; input.HorizontalAlignment = HorizontalAlignment.Stretch; input.Margin = new Thickness(0);
            if (string.IsNullOrEmpty(System.Windows.Automation.AutomationProperties.GetName(input))) System.Windows.Automation.AutomationProperties.SetName(input, label);
            g.Children.Add(input);
            if (action is FrameworkElement a) { a.Margin = new Thickness(8, 0, 0, 0); a.VerticalAlignment = VerticalAlignment.Stretch; Grid.SetColumn(a, 1); g.Children.Add(a); }
            sp.Children.Add(g);
            // as wide as the page allows up to maxWidth, starting at the left edge
            var host = new Grid { Margin = new Thickness(0, 0, 0, 12) };
            host.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MaxWidth = maxWidth }); host.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            host.Children.Add(sp);
            return host;
        }

        /// <summary>small fields side by side (each its own width), wrapping to the next line when the page is narrow</summary>
        WrapPanel FieldRow(params (string label, FrameworkElement input, double width)[] fields)
        {
            var w = new WrapPanel { Margin = new Thickness(0, 0, 0, 4) };
            foreach (var (label, input, width) in fields)
            {
                var sp = new StackPanel { Width = width, Margin = new Thickness(0, 0, 12, 10) };
                sp.Children.Add(new TextBlock { Text = label, Style = Res<Style>("T.Muted"), FontSize = 12.5, Margin = new Thickness(0, 0, 0, 5) });
                input.Width = double.NaN; input.MinWidth = 0; input.HorizontalAlignment = HorizontalAlignment.Stretch; input.Margin = new Thickness(0);
                if (string.IsNullOrEmpty(System.Windows.Automation.AutomationProperties.GetName(input))) System.Windows.Automation.AutomationProperties.SetName(input, label);
                sp.Children.Add(input); w.Children.Add(sp);
            }
            return w;
        }

        /// <summary>a sentence with its button on the right (the button moves under the sentence when the page is narrow)</summary>
        FrameworkElement Inline(string text, Button button)
        {
            var g = new Grid(); g.ColumnDefinitions.Add(new ColumnDefinition()); g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var t = T(text, 13, false, true, new Thickness(0, 0, 16, 0)); t.VerticalAlignment = VerticalAlignment.Center; g.Children.Add(t);
            button.Margin = new Thickness(0); button.VerticalAlignment = VerticalAlignment.Center; Grid.SetColumn(button, 1); g.Children.Add(button);
            return g;
        }

        /// <summary>facts as a two-column table: the name on the left, its value beside it</summary>
        Grid KeyValues(params (string key, string value)[] rows)
        {
            var g = new Grid { Margin = new Thickness(0, 2, 0, 6) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto, MaxWidth = 220 }); g.ColumnDefinitions.Add(new ColumnDefinition());
            int r = 0;
            foreach (var (k, v) in rows)
            {
                if (v == null) continue;
                g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                // the key's own width limit (the column's 220 less its margin): an Auto column measures its text without a limit, so a long key
                // would be cut off at the column's edge instead of wrapping
                var kt = new TextBlock { Text = k, Style = Res<Style>("T.Muted"), FontSize = 13, TextWrapping = TextWrapping.Wrap, MaxWidth = 202, Margin = new Thickness(0, 3, 18, 3) }; Grid.SetRow(kt, r); g.Children.Add(kt);
                var vt = new TextBlock { Text = v, FontSize = 13, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 3) }; Grid.SetRow(vt, r); Grid.SetColumn(vt, 1); g.Children.Add(vt);
                r++;
            }
            return g;
        }

        /// <summary>changes as a table: what happens (Add, Replace, Remove…), the file's place (shortened with "…", whole in its tooltip),
        /// and its size</summary>
        Grid ChangeTable(System.Collections.Generic.IEnumerable<(string action, string path, string size)> rows)
        {
            var g = new Grid { Margin = new Thickness(0, 0, 0, 6) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); g.ColumnDefinitions.Add(new ColumnDefinition { MinWidth = 120 }); g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            int r = 0;
            foreach (var (action, path, size) in rows)
            {
                g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                var brush = action == "Remove" || action == "Removed" ? "B.Red3" : action == "Add" ? "B.Ok" : "B.Ink2";
                var a = new TextBlock { Text = action, FontSize = 12.5, FontWeight = FontWeights.SemiBold, Foreground = Res<Brush>(brush), Margin = new Thickness(0, 3, 16, 3) }; Grid.SetRow(a, r); g.Children.Add(a);
                var p = new TextBlock { Text = path, FontSize = 13, TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = path, Margin = new Thickness(0, 3, 16, 3) }; Grid.SetRow(p, r); Grid.SetColumn(p, 1); g.Children.Add(p);
                var s = new TextBlock { Text = size ?? "", FontSize = 12.5, Foreground = Res<Brush>("B.Muted"), TextAlignment = TextAlignment.Right, Margin = new Thickness(0, 3, 0, 3) }; Grid.SetRow(s, r); Grid.SetColumn(s, 2); g.Children.Add(s);
                r++;
            }
            return g;
        }

        /// <summary>numbered steps: the number in its own column, each step's text wrapping beside it</summary>
        Grid Numbered(params string[] steps)
        {
            var g = new Grid { Margin = new Thickness(0, 0, 0, 4) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) }); g.ColumnDefinitions.Add(new ColumnDefinition());
            for (int i = 0; i < steps.Length; i++)
            {
                g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                var n = new TextBlock { Text = (i + 1) + ".", FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = Res<Brush>("B.Muted"), Margin = new Thickness(0, 0, 0, 6) }; Grid.SetRow(n, i); g.Children.Add(n);
                var t = new TextBlock { Text = steps[i], FontSize = 13, TextWrapping = TextWrapping.Wrap, LineHeight = 19, Margin = new Thickness(0, 0, 0, 6) }; Grid.SetRow(t, i); Grid.SetColumn(t, 1); g.Children.Add(t);
            }
            return g;
        }

        /// <summary>a file location: one line, shortened in the middle part with "…" when it does not fit, the whole path in its tooltip</summary>
        TextBlock PathText(string path, double size = 12.5) => new TextBlock { Text = path ?? "", FontSize = size, Style = Res<Style>("T.Muted"), TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = path, Margin = new Thickness(0, 0, 0, 4) };

        /// <summary>a labeled "Technical details" area, closed at first: exact component names, paths, checksums, codes and diagnostics for
        /// someone who wants them, with a button that copies all of it. Each item is a line of text or a finished element. Risks, the
        /// destination, destructive changes, missing evidence and confirmations never go in here: they stay on the page itself.</summary>
        FrameworkElement Details(string title, params object[] items)
        {
            var lines = new System.Collections.Generic.List<string>();
            var body = new StackPanel { Visibility = Visibility.Collapsed, Margin = new Thickness(26, 4, 0, 4) };
            foreach (var it in items)
            {
                if (it == null) continue;
                if (it is string s) { if (s.Length == 0) continue; lines.Add(s); var t = new TextBox { Text = s, IsReadOnly = true, TextWrapping = TextWrapping.Wrap, BorderThickness = new Thickness(0), Background = Brushes.Transparent, Padding = new Thickness(0), Margin = new Thickness(0, 0, 0, 4), FontSize = Ty.Small, FontFamily = Res<FontFamily>("F.Ui"), Foreground = Res<Brush>("B.Ink2"), MinHeight = 0 }; System.Windows.Automation.AutomationProperties.SetName(t, s.Length > 120 ? s.Substring(0, 120) : s); body.Children.Add(t); }
                else if (it is UIElement u) { body.Children.Add(u); if (u is TextBlock tb) lines.Add(tb.Text); }
            }
            if (body.Children.Count == 0) return null;
            if (lines.Count > 0) { var copy = B("Copy Details", "Btn.Ghost", () => { try { Clipboard.SetText(string.Join(Environment.NewLine, lines)); } catch (Exception) { } }); copy.Margin = new Thickness(0, 6, 0, 0); body.Children.Add(copy); }
            var glyph = new TextBlock { Text = "", FontFamily = Res<FontFamily>("F.Icons"), FontSize = 10, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 1, 10, 0), Foreground = Res<Brush>("B.Muted") };
            var head = new System.Windows.Controls.Primitives.ToggleButton { Style = Res<Style>("Wb.Disclosure"), Content = new StackPanel { Orientation = Orientation.Horizontal, Children = { glyph, new TextBlock { Text = title ?? "Technical details", FontSize = Ty.Text, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center } } } };
            System.Windows.Automation.AutomationProperties.SetName(head, title ?? "Technical details");
            head.Checked += (s, e) => { body.Visibility = Visibility.Visible; glyph.Text = ""; };
            head.Unchecked += (s, e) => { body.Visibility = Visibility.Collapsed; glyph.Text = ""; };
            var sp = new StackPanel { Margin = new Thickness(0, 4, 0, 8) }; sp.Children.Add(head); sp.Children.Add(body);
            return sp;
        }

        /// <summary>a short status label with a colored outline (its words carry the meaning; the color only repeats it)</summary>
        Border Pill(string text, string brush) => NoTerms(new Border { CornerRadius = new CornerRadius(10), BorderBrush = Res<Brush>(brush), BorderThickness = new Thickness(1), Padding = new Thickness(10, 2, 10, 3), VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left, Child = new TextBlock { Text = text, FontSize = Ty.Fine, Foreground = Res<Brush>(brush), TextWrapping = TextWrapping.Wrap } });
        /// <summary>marks an element whose text stays exactly as it is, with no explained words (a status label, a file list, legal text)</summary>
        static T NoTerms<T>(T e) where T : DependencyObject { SetNoTerms(e, true); return e; }
    }
}
