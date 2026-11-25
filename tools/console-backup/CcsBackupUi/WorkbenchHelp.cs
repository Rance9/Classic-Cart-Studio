// Classic Cart Studio console backup and restore window. Copyright (C) 2026 Rance9. GPL-3.0-or-later.
// CCS Console Workbench: Help. One illustrated guide, carried inside this library (help/help.txt and its pictures help/shots/*.png), so it
// works offline, in the standalone's single file and in Classic Cart Studio for Windows alike, with no console, backup, network or files
// written. Its topics cover every page and setting; each page's own topic opens with "Help on This Page" (or F1) in a Help window beside the
// task, which stays as it is. Help and About in the side column shows the same guide in the page.
//   The guide      contents by section, a search over every topic's words, links between topics with Back, and pictures of the actual
//                  pages (sample data) that open larger with a click, Enter or Space.
//   Built in       four topics are drawn from the window's own sources rather than written twice: About This Console (the console family's
//                  information: WorkbenchAbout.cs), About Workbench (version, license, source), the Glossary (WorkbenchGlossary.cs).
// Format of help.txt: @section, @topic, @title, @pages (page tags), @summary, @builtin, @end; blocks: "## " heading, paragraph, "- " list,
// "1. " steps, "! " important note, "[shot NAME | caption]" picture, "| a | b |" table; inline **bold** and [text](#topic).
using CcsBackup.Engine;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace CcsBackup.Ui
{
    public sealed class HelpBlock { public string Kind, Text, Shot; public List<string> Items = new List<string>(); public List<string[]> Rows = new List<string[]>(); }
    /// <summary>one picture of a set the larger-picture window steps through: the picture, its name and its caption</summary>
    public sealed class GalleryPicture { public ImageSource Source; public string Title, Caption; }
    public sealed class HelpTopic
    {
        public string Id, Title, Section, Summary, Builtin; public string[] Pages = new string[0];
        public List<HelpBlock> Blocks = new List<HelpBlock>();
        /// <summary>every word of the topic in lower case, for search</summary>
        public string Words;
    }

    public static class HelpContent
    {
        static List<HelpTopic> topics;
        public static List<HelpTopic> Topics { get { if (topics == null) topics = Parse(Read()); return topics; } }
        public static HelpTopic Find(string id) => Topics.FirstOrDefault(t => t.Id == id);

        static string Read()
        {
            using (var s = typeof(HelpContent).Assembly.GetManifestResourceStream("help/help.txt"))
            {
                if (s == null) return "";
                using (var r = new StreamReader(s)) return r.ReadToEnd();
            }
        }

        public static List<HelpTopic> Parse(string text)
        {
            var list = new List<HelpTopic>(); string section = null; HelpTopic t = null; var block = new List<string>();
            void Flush()
            {
                if (t == null || block.Count == 0) { block.Clear(); return; }
                var first = block[0]; var b = new HelpBlock();
                if (first.StartsWith("## ")) { b.Kind = "h"; b.Text = first.Substring(3).Trim(); }
                else if (first.StartsWith("[shot ")) { var m = Regex.Match(string.Join(" ", block), @"^\[shot\s+([A-Za-z0-9._-]+)\s*\|\s*(.*)\]$"); b.Kind = "shot"; b.Shot = m.Groups[1].Value; b.Text = m.Groups[2].Value.Trim(); }
                else if (first.StartsWith("| "))
                {
                    b.Kind = "table";
                    foreach (var l in block) { var cells = l.Trim().Trim('|').Split('|').Select(c => c.Trim()).ToArray(); if (cells.All(c => Regex.IsMatch(c, "^-+$"))) continue; b.Rows.Add(cells); }
                }
                else if (first.StartsWith("! ")) { b.Kind = "note"; b.Text = string.Join(" ", block.Select(l => l.Trim())).Substring(2).Trim(); }
                else if (first.StartsWith("- ") || Regex.IsMatch(first, @"^\d+\. "))
                {
                    b.Kind = first.StartsWith("- ") ? "ul" : "ol";
                    foreach (var l in block)
                    {
                        var m = Regex.Match(l, b.Kind == "ul" ? @"^- (.*)$" : @"^\d+\. (.*)$");
                        if (m.Success) b.Items.Add(m.Groups[1].Value.Trim()); else if (b.Items.Count > 0) b.Items[b.Items.Count - 1] += " " + l.Trim();
                    }
                }
                else { b.Kind = "p"; b.Text = string.Join(" ", block.Select(l => l.Trim())); }
                t.Blocks.Add(b); block.Clear();
            }
            foreach (var raw in text.Replace("\r\n", "\n").Split('\n'))
            {
                var line = raw.TrimEnd();
                if (line.StartsWith("@"))
                {
                    Flush();
                    var m = Regex.Match(line, @"^@(\w+)\s*(.*)$"); var key = m.Groups[1].Value; var v = m.Groups[2].Value.Trim();
                    if (key == "section") section = v;
                    else if (key == "topic") { t = new HelpTopic { Id = v, Section = section }; list.Add(t); }
                    else if (t == null) continue;
                    else if (key == "title") t.Title = v;
                    else if (key == "pages") t.Pages = v.Split(',').Select(x => x.Trim()).Where(x => x.Length > 0).ToArray();
                    else if (key == "summary") t.Summary = v;
                    else if (key == "builtin") t.Builtin = v;
                    else if (key == "end") t = null;
                    continue;
                }
                if (line.Trim().Length == 0 || line.StartsWith("TODO-CHECK")) { Flush(); continue; }
                block.Add(line);
            }
            Flush();
            foreach (var x in list)
                x.Words = Plain(string.Join(" ", new[] { x.Title, x.Summary }.Concat(x.Blocks.SelectMany(b => new[] { b.Text }.Concat(b.Items).Concat(b.Rows.SelectMany(r => r)))).Where(s => s != null))).ToLowerInvariant();
            return list;
        }

        /// <summary>a line's words without its markup ("**Save**" → "Save", "[Restore](#restore)" → "Restore")</summary>
        public static string Plain(string s) => Regex.Replace(Regex.Replace(s ?? "", @"\[([^\]]+)\]\(#[a-z0-9-]+\)", "$1"), @"\*\*", "");

        /// <summary>the topics whose words include every word searched for, those with a match in the title first</summary>
        public static List<HelpTopic> Search(string q)
        {
            var words = (q ?? "").ToLowerInvariant().Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (words.Length == 0) return new List<HelpTopic>();
            return Topics.Where(t => words.All(w => t.Words.Contains(w))).OrderBy(t => words.All(w => (t.Title ?? "").ToLowerInvariant().Contains(w)) ? 0 : 1).ToList();
        }
    }

    public sealed partial class BackupWorkspace
    {
        /// <summary>the Help topic for a page: the one whose @pages names it; for pages several sections share (progress, results,
        /// messages, the driver pages), the open section's topic</summary>
        internal string HelpTopicFor(string tag)
        {
            var shared = new HashSet<string> { "progress", "result", "msg", "driver", "driver-setup", "files", "files-intro", "files-plan" };
            if (tag != null && !shared.Contains(tag)) { var t = HelpContent.Topics.FirstOrDefault(x => x.Pages.Contains(tag)); if (t != null) return t.Id; }
            if (tag == "driver" || tag == "driver-setup") return "connect";
            if (tag == "files" || tag == "files-intro" || tag == "files-plan") return section == "Developer Mode" ? "developer" : "expert";
            if (tag == "progress" || tag == "msg") return HelpContent.Find("progress") != null ? "progress" : "start";
            switch (section)
            {
                case "Console Backup": return "backup"; case "Console Restore": return "restore"; case "Console Rescue": return "rescue"; case "Console Composer": return "composer";
                case "Console Browser": return "console-browser"; case "Backup Browser": return "backup-browser"; case "Backup Repair": return "backup-repair"; case "Developer Mode": return "developer";
                case "Settings": return "settings"; case "Credits": return "credits"; case "Dashboard": return "dashboard";
                default: return "start";
            }
        }
        string PageTag => (page.Content as FrameworkElement)?.Tag as string;

        // ------------------------------------------------------------------ Help and About (the page) and the Help window
        string helpTopic = "start";
        void ShowHelp()
        {
            var sp = PageHead("Help and About", "Step-by-step help for every Workbench page and setting, the console Workbench works with, and this program's version and license. Help on This Page, at the top of the window, opens the help for the page you are on.");
            sp.Children.Add(HelpView(helpTopic, false, out _));
            Show(sp, "help");
        }

        Window helpWindow; Action<string> helpGo;
        /// <summary>the Help window, beside the main window (one at a time; the main window's page and task stay as they are)</summary>
        internal void OpenHelp(string topic)
        {
            topic = HelpContent.Find(topic) != null ? topic : "start";
            if (helpWindow != null) { helpGo?.Invoke(topic); if (helpWindow.WindowState == WindowState.Minimized) helpWindow.WindowState = WindowState.Normal; helpWindow.Activate(); return; }
            var w = Owned("Workbench Help", 1080, true); w.Height = Math.Max(560, Math.Min(900, ActualHeight)); w.MinWidth = 760; w.MinHeight = 480; w.ShowInTaskbar = false;
            w.Content = HelpView(topic, true, out helpGo);
            w.Closed += (s, e) => { helpWindow = null; helpGo = null; };
            helpWindow = w; w.Show();
        }
        void OpenPageHelp() => OpenHelp(HelpTopicFor(PageTag));

        /// <summary>the guide: Back, search and contents on the left, the topic on the right. In the Help window each side scrolls on its
        /// own; in the page, the page scrolls.
        ///   Back (WB-UX-12)   a normal button with a left arrow at the top left of both Help views (also Alt+Left and a mouse's back button):
        ///                     it returns to the topic read before, with the search and the place on the page it had then; it is dimmed
        ///                     while there is no earlier topic. Every way to another topic (a link, the contents, a search result, Help on
        ///                     This Page) adds to that history.</summary>
        FrameworkElement HelpView(string start, bool window, out Action<string> go)
        {
            var g = new Grid(); g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(window ? 270 : 250) }); g.ColumnDefinitions.Add(new ColumnDefinition());
            var left = new StackPanel { Margin = new Thickness(window ? 18 : 0, window ? 16 : 0, 18, 16) };
            var backButton = BackButton("Back", null); backButton.Margin = new Thickness(0, 0, 0, 14); backButton.IsEnabled = false;
            AutomationProperties.SetAcceleratorKey(backButton, "Alt+Left");
            ToolTipService.SetShowOnDisabled(backButton, true);
            left.Children.Add(backButton);
            var searchLabel = new TextBlock { Text = "Search Help", FontSize = Ty.Small, Foreground = Res<Brush>("B.Muted"), Margin = new Thickness(0, 0, 0, 5) };
            var search = new TextBox(); AutomationProperties.SetName(search, "Search Help"); AutomationProperties.SetLabeledBy(search, searchLabel);
            left.Children.Add(searchLabel); left.Children.Add(search);
            var list = new StackPanel { Margin = new Thickness(0, 12, 0, 0) }; left.Children.Add(list);
            var topicHost = new ContentControl();
            var right = new StackPanel { Margin = new Thickness(window ? 8 : 6, window ? 16 : 0, window ? 28 : 0, 24) }; right.Children.Add(topicHost);
            UIElement L = left, R = right;
            ScrollViewer rsv = null;
            if (window)
            {
                L = new Border { Background = Res<Brush>("B.Side"), BorderBrush = Res<Brush>("B.Line"), BorderThickness = new Thickness(0, 0, 1, 0), Child = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = left, Focusable = false } };
                R = rsv = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = right, Focusable = false };
            }
            g.Children.Add(L); Grid.SetColumn(R, 1); g.Children.Add(R);
            // what Back returns to: the topic, what was in the search box and how far down the topic was scrolled
            var history = new Stack<(string topic, string search, double offset)>(); string current = null;
            ScrollViewer Scroll() => window ? rsv : scroller;
            Action<string> open = null;
            void Contents()
            {
                list.Children.Clear();
                var q = search.Text.Trim();
                if (q.Length > 0)
                {
                    var found = HelpContent.Search(q);
                    list.Children.Add(new TextBlock { Text = found.Count == 0 ? "No topics have all of those words." : Count(found.Count, "topic") + " found", FontSize = Ty.Small, Foreground = Res<Brush>("B.Muted"), Margin = new Thickness(0, 0, 0, 6), TextWrapping = TextWrapping.Wrap });
                    foreach (var t in found) list.Children.Add(TopicRow(t, true));
                    return;
                }
                foreach (var grp in HelpContent.Topics.GroupBy(t => t.Section ?? ""))
                {
                    if (grp.Key.Length > 0) list.Children.Add(new TextBlock { Text = grp.Key, FontSize = Ty.Text, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 10, 0, 3), TextWrapping = TextWrapping.Wrap });
                    foreach (var t in grp) list.Children.Add(TopicRow(t, false));
                }
            }
            Button TopicRow(HelpTopic t, bool withSummary)
            {
                var content = new StackPanel();
                content.Children.Add(new TextBlock { Text = t.Title, FontSize = Ty.Text, TextWrapping = TextWrapping.Wrap });
                if (withSummary && t.Summary != null) content.Children.Add(new TextBlock { Text = t.Summary, FontSize = Ty.Fine, Foreground = Res<Brush>("B.Muted"), TextWrapping = TextWrapping.Wrap });
                var b = new Button { Content = content, Style = Res<Style>("Wb.Nav"), Tag = t.Id == current ? "on" : null, Padding = new Thickness(10, 5, 8, 5), HorizontalContentAlignment = HorizontalAlignment.Stretch };
                AutomationProperties.SetName(b, t.Title); var id = t.Id; b.Click += (s, e) => open(id); return b;
            }
            void ShowTopic(HelpTopic t)
            {
                current = t.Id; if (!window) helpTopic = t.Id;
                topicHost.Content = TopicView(t, x => open(x));
                Contents();
                backButton.IsEnabled = history.Count > 0;
                var before = history.Count > 0 ? HelpContent.Find(history.Peek().topic)?.Title : null;
                backButton.ToolTip = before != null ? "Back to " + before + " (Alt+Left)" : "No earlier topic yet";
                AutomationProperties.SetHelpText(backButton, before != null ? "Back to the topic " + before : "No earlier topic yet");
            }
            open = id =>
            {
                var t = HelpContent.Find(id) ?? HelpContent.Find("start") ?? HelpContent.Topics.FirstOrDefault(); if (t == null) { topicHost.Content = Note("Help is not available in this copy.", "warn"); return; }
                if (current != null && current != t.Id) history.Push((current, search.Text, Scroll()?.VerticalOffset ?? 0));
                ShowTopic(t); Scroll()?.ScrollToTop();
            };
            void GoBack()
            {
                if (history.Count == 0) return;
                var (id, q, offset) = history.Pop(); var t = HelpContent.Find(id); if (t == null) return;
                if (search.Text != q) search.Text = q;   // the contents or the search results as they were
                ShowTopic(t);
                var sv = Scroll(); if (sv == null) return;
                sv.ScrollToVerticalOffset(offset);
                Dispatcher.BeginInvoke(new Action(() => sv.ScrollToVerticalOffset(offset)), System.Windows.Threading.DispatcherPriority.Loaded);   // once the topic is laid out
            }
            backButton.Click += (s, e) => GoBack();
            // on the Help page, with no earlier topic, Alt+Left and the mouse's back button are the window's Back (WorkbenchNav.cs)
            g.PreviewKeyDown += (s, e) => { if ((e.Key == Key.System ? e.SystemKey : e.Key) == Key.Left && Keyboard.Modifiers == ModifierKeys.Alt && (window || history.Count > 0)) { GoBack(); e.Handled = true; } };
            g.PreviewMouseDown += (s, e) => { if (e.ChangedButton == MouseButton.XButton1 && (window || history.Count > 0)) { GoBack(); e.Handled = true; } };
            search.TextChanged += (s, e) => Contents();
            go = open; open(start);
            return g;
        }

        /// <summary>one topic: its title and summary and its blocks (Back is at the top left of Help, HelpView)</summary>
        FrameworkElement TopicView(HelpTopic t, Action<string> link)
        {
            var sp = new StackPanel(); NewTermScope(sp);
            sp.Children.Add(new TextBlock { Text = t.Title, Style = Res<Style>("T.Title"), FontSize = 20, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 4) });
            if (t.Summary != null) sp.Children.Add(new TextBlock { Text = t.Summary, Style = Res<Style>("T.Muted"), FontSize = 13.5, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 14) });
            if (t.Builtin != null) sp.Children.Add(BuiltinTopic(t.Builtin));
            foreach (var b in t.Blocks)
            {
                switch (b.Kind)
                {
                    case "h": sp.Children.Add(new TextBlock { Text = b.Text, FontSize = 15.5, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 6) }); break;
                    case "p": sp.Children.Add(Rich(b.Text, link)); break;
                    case "note": { var n = Note("", "warn"); ((Border)n).Child = Rich(b.Text, link, 0); sp.Children.Add(n); break; }
                    case "ul": foreach (var i in b.Items) sp.Children.Add(Bullet(Rich(i, link))); break;
                    case "ol":
                    {
                        var g = new Grid { Margin = new Thickness(0, 0, 0, 6) }; g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(26) }); g.ColumnDefinitions.Add(new ColumnDefinition());
                        for (int k = 0; k < b.Items.Count; k++)
                        {
                            g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                            var n = new TextBlock { Text = (k + 1) + ".", FontSize = 13, FontWeight = FontWeights.SemiBold, Foreground = Res<Brush>("B.Muted") }; Grid.SetRow(n, k); g.Children.Add(n);
                            var x = Rich(b.Items[k], link); Grid.SetRow(x, k); Grid.SetColumn(x, 1); g.Children.Add(x);
                        }
                        sp.Children.Add(g); break;
                    }
                    case "table": sp.Children.Add(HelpTable(b.Rows, link)); break;
                    case "shot": sp.Children.Add(Shot(b.Shot, b.Text)); break;
                }
            }
            return sp;
        }

        /// <summary>a line with **bold** words and [links](#topic) as a wrapping text block</summary>
        TextBlock Rich(string text, Action<string> link, double bottom = 8)
        {
            var tb = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 13.5, LineHeight = 20, Margin = new Thickness(0, 0, 0, bottom) };
            foreach (Match m in Regex.Matches(text ?? "", @"\*\*(.+?)\*\*|\[([^\]]+)\]\(#([a-z0-9-]+)\)|[^*\[]+|[*\[]"))
            {
                if (m.Groups[1].Success) tb.Inlines.Add(new Run(m.Groups[1].Value) { FontWeight = FontWeights.SemiBold });
                else if (m.Groups[2].Success)
                {
                    var id = m.Groups[3].Value; var h = new Hyperlink(new Run(m.Groups[2].Value)) { Foreground = Res<Brush>("B.Red3") };
                    h.Click += (s, e) => link(id); AutomationProperties.SetHelpText(h, "Opens the Help topic " + (HelpContent.Find(id)?.Title ?? id));
                    tb.Inlines.Add(h);
                }
                else tb.Inlines.Add(new Run(m.Value));
            }
            LinkNames(tb);   // "Rance9" and "Rance9 LLC" open Rance9.com
            AutomationProperties.SetName(tb, InlineText(tb));   // its words, for screen readers (a block built from pieces has no Text)
            return tb;
        }

        /// <summary>the developer's and the publisher's own site, which every shown "Rance9" and "Rance9 LLC" opens</summary>
        internal const string DeveloperUrl = "https://Rance9.com";
        /// <summary>the names as words: never part of an address, an e-mail, a file or an account name (Rance9.com, rance9@…, /Rance9/)</summary>
        static readonly Regex DeveloperName = new Regex(@"(?<![\w@/.\-])Rance9(?: LLC)?(?![\w@/\-])(?!\.\w)");
        /// <summary>every "Rance9" and "Rance9 LLC" in a text block's own text (not inside its links) becomes a link to Rance9.com, opened in the
        /// web browser; the words, their weight and their size stay exactly as they were</summary>
        internal T LinkNames<T>(T tb) where T : TextBlock
        {
            foreach (var r in tb.Inlines.OfType<Run>().ToList())
            {
                var ms = DeveloperName.Matches(r.Text ?? ""); if (ms.Count == 0) continue;
                Run Piece(string text) { var x = new Run(text) { FontWeight = r.FontWeight, FontStyle = r.FontStyle }; if (r.ReadLocalValue(TextElement.FontSizeProperty) != DependencyProperty.UnsetValue) x.FontSize = r.FontSize; return x; }
                int at = 0;
                foreach (Match m in ms)
                {
                    if (m.Index > at) tb.Inlines.InsertBefore(r, Piece(r.Text.Substring(at, m.Index - at)));
                    var h = new Hyperlink(Piece(m.Value)) { Foreground = Res<Brush>("B.Red3"), ToolTip = "Rance9.com" };
                    h.Click += (s, e) => Launch(DeveloperUrl);
                    AutomationProperties.SetName(h, m.Value); AutomationProperties.SetHelpText(h, "Opens Rance9.com in your web browser");
                    tb.Inlines.InsertBefore(r, h); at = m.Index + m.Length;
                }
                if (at < r.Text.Length) tb.Inlines.InsertBefore(r, Piece(r.Text.Substring(at)));
                tb.Inlines.Remove(r);
            }
            return tb;
        }
        /// <summary>the words of a text block built from pieces</summary>
        internal static string InlineText(TextBlock tb) => InlineText(tb.Inlines);
        static string InlineText(InlineCollection inl) => string.Concat(inl.Select(i => i is Run r ? r.Text : i is Span sp ? InlineText(sp.Inlines) : i is LineBreak ? " " : ""));

        Grid HelpTable(List<string[]> rows, Action<string> link)
        {
            var g = new Grid { Margin = new Thickness(0, 4, 0, 12) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MaxWidth = 260 }); g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2.4, GridUnitType.Star) });
            for (int r = 0; r < rows.Count; r++)
            {
                g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                for (int c = 0; c < 2; c++)
                {
                    var cell = Rich(c < rows[r].Length ? rows[r][c] : "", link, 0); cell.FontSize = 13; cell.Margin = new Thickness(0, 5, 14, 5);
                    if (r == 0) { cell.FontWeight = FontWeights.SemiBold; cell.Foreground = Res<Brush>("B.Muted"); }
                    var bd = new Border { BorderBrush = Res<Brush>("B.Line"), BorderThickness = new Thickness(0, 0, 0, 1), Child = cell }; Grid.SetRow(bd, r); Grid.SetColumn(bd, c); g.Children.Add(bd);
                }
            }
            return g;
        }

        /// <summary>a screenshot of a page (sample data), as wide as the topic allows, in a frame of its own with its caption (italic,
        /// "Screenshot:" first) directly under it, so it is never taken for the live page; a click, Enter or Space opens it larger, and
        /// closing that puts the focus back here (its focus ring shows only when the keyboard was used: FocusCue)</summary>
        FrameworkElement Shot(string name, string caption)
        {
            var sp = new StackPanel { Margin = new Thickness(0, 4, 0, 16) };
            var src = HelpPicture(name);
            if (src == null) { sp.Children.Add(Note("Picture not available in this copy: " + caption, "info")); return sp; }
            var img = new Image { Source = src, Stretch = Stretch.Uniform, MaxWidth = src.PixelWidth, HorizontalAlignment = HorizontalAlignment.Left, SnapsToDevicePixels = true };
            RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
            var frame = new Border { BorderBrush = Res<Brush>("B.Line2"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Background = Res<Brush>("B.Bg"), Padding = new Thickness(6), Child = img, HorizontalAlignment = HorizontalAlignment.Left };
            var b = new Button { Content = frame, Style = Res<Style>("Wb.Plain"), HorizontalAlignment = HorizontalAlignment.Left };
            AutomationProperties.SetName(b, "Screenshot: " + caption + " Select to see it larger."); b.ToolTip = "Select to see it larger";
            // over the window it was chosen in (Help or the main window)
            b.Click += (s, e) => { ShowPicture(src, "Screenshot from Help", caption, "A screenshot of a Workbench page with sample data: a picture, not the page itself.", Window.GetWindow(b)); b.Focus(); };
            sp.Children.Add(b);
            sp.Children.Add(PictureCaption("Screenshot: " + caption, b));
            return sp;
        }

        static readonly Dictionary<string, BitmapSource> helpPictures = new Dictionary<string, BitmapSource>();
        /// <summary>a Help picture carried in this library (help/shots/NAME.png), or null</summary>
        internal static BitmapSource HelpPicture(string name) => Picture("help/shots/" + name + ".png") ?? Picture("help/shots/" + name + ".jpg");   // .jpg: a page with photographs
        internal static BitmapSource Picture(string path)
        {
            lock (helpPictures)
            {
                if (helpPictures.TryGetValue(path, out var b)) return b;
                try
                {
                    var info = Application.GetResourceStream(new Uri("pack://application:,,,/CcsBackupUi;component/" + path, UriKind.Absolute));
                    if (info == null) return helpPictures[path] = null;
                    using (var s = info.Stream) { var d = BitmapDecoder.Create(s, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad); b = d.Frames[0]; b.Freeze(); }
                }
                catch (IOException) { b = null; }
                catch (NotSupportedException) { b = null; }
                return helpPictures[path] = b;
            }
        }

        /// <summary>a picture larger, in its own window (its whole width and height kept in proportion), with its name above it and its
        /// caption (WB-UX-13: italic, directly under it). The picture sits in its own frame; the window's one Close button is below,
        /// apart from the picture, beside a line that says what the picture is. Close, Escape or the window's X shut it; the focus goes
        /// back to what opened it.</summary>
        internal void ShowPicture(ImageSource src, string title, string description, string note, Window owner = null) => Modal(PictureWindow(src, title, description, note, owner));
        internal Window PictureWindow(ImageSource src, string title, string description, string note, Window owner = null) =>
            GalleryWindow(new List<GalleryPicture> { new GalleryPicture { Source = src, Title = title, Caption = description } }, 0, note, owner);

        /// <summary>WB-UX-24: several pictures of one set (one design's screens) in the same window, opened at the one chosen</summary>
        internal void ShowGallery(IList<GalleryPicture> items, int start, string note, Window owner = null) { if (items != null && items.Count > 0) Modal(GalleryWindow(items, start, note, owner)); }

        /// <summary>the larger-picture window: one picture, or one of a set with Previous and Next (WB-UX-24). The window's title, the picture's
        /// name above it and its caption under it follow the picture shown; Previous, Next and "3 of 15" are in a row of their own under the
        /// caption, never over the picture, and only when there is more than one; they go round from the last to the first. The Left and Right
        /// arrow keys do the same; Escape, Close or the window's X shut it, and the focus goes back to what opened it.</summary>
        internal Window GalleryWindow(IList<GalleryPicture> items, int start, string note, Window owner = null)
        {
            int at = Math.Max(0, Math.Min(items.Count - 1, start)); bool many = items.Count > 1;
            var w = Owned(items[at].Title, Math.Max(760, Math.Min(1500, ActualWidth - 40)), true, owner); w.Height = Math.Max(520, Math.Min(1000, ActualHeight - 20)); w.MinWidth = 520; w.MinHeight = 400;
            var root = new DockPanel { Margin = new Thickness(20, 16, 20, 14) };
            var head = new StackPanel(); DockPanel.SetDock(head, Dock.Top);
            var title = new TextBlock { FontSize = 18, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10) };
            head.Children.Add(title);
            root.Children.Add(head);
            var foot = new Grid { Margin = new Thickness(0, 12, 0, 0) }; DockPanel.SetDock(foot, Dock.Bottom);
            foot.ColumnDefinitions.Add(new ColumnDefinition()); foot.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            foot.Children.Add(new Border { Height = 1, Background = Res<Brush>("B.Line"), VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 0, 0, 0) }); Grid.SetColumnSpan(foot.Children[0], 2);
            if (note != null) foot.Children.Add(new TextBlock { Text = note, FontSize = Ty.Small, Foreground = Res<Brush>("B.Muted"), TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 12, 16, 0) });
            var close = B("Close", "Btn.Primary", () => w.Close()); close.Margin = new Thickness(0, 12, 0, 0); close.IsDefault = true; Grid.SetColumn(close, 1); foot.Children.Add(close);
            root.Children.Add(foot);
            var img = new Image { Stretch = Stretch.Uniform }; RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
            // the picture in a frame of its own: a picture, apart from the window's own controls; its caption directly under the frame, then
            // (a set) the row that moves between the pictures
            var figure = new DockPanel();
            var under = new StackPanel(); DockPanel.SetDock(under, Dock.Bottom); figure.Children.Add(under);
            var cap = PictureCaption(null); under.Children.Add(cap);
            var pos = new TextBlock { FontSize = Ty.Small, Foreground = Res<Brush>("B.Muted"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 12, 0), MinWidth = 60, TextAlignment = TextAlignment.Center };
            AutomationProperties.SetLiveSetting(pos, AutomationLiveSetting.Polite);
            void Show(int k)
            {
                at = (k % items.Count + items.Count) % items.Count; var x = items[at];
                img.Source = x.Source; AutomationProperties.SetName(img, x.Caption ?? x.Title);
                title.Text = x.Title; w.Title = x.Title;
                cap.Text = x.Caption ?? ""; cap.Visibility = string.IsNullOrEmpty(x.Caption) ? Visibility.Collapsed : Visibility.Visible;
                pos.Text = (at + 1) + " of " + items.Count; AutomationProperties.SetName(pos, x.Title + ", picture " + (at + 1) + " of " + items.Count);
                if (many) try { System.Windows.Automation.Peers.UIElementAutomationPeer.CreatePeerForElement(pos)?.RaiseAutomationEvent(System.Windows.Automation.Peers.AutomationEvents.LiveRegionChanged); } catch (Exception) { }
            }
            if (many)
            {
                var nav = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 10, 0, 0) };
                var prev = B("‹  Previous", "Btn", () => Show(at - 1)); prev.Margin = new Thickness(0); prev.MinWidth = 120; AutomationProperties.SetName(prev, "Previous screenshot");
                var next = B("Next  ›", "Btn", () => Show(at + 1)); next.Margin = new Thickness(0); next.MinWidth = 120; AutomationProperties.SetName(next, "Next screenshot");
                nav.Children.Add(prev); nav.Children.Add(pos); nav.Children.Add(next);
                under.Children.Add(nav);
                // the arrow keys move between the pictures wherever the focus is in this window (no text box or list here takes them)
                w.PreviewKeyDown += (s, e) =>
                {
                    if (e.Handled || Keyboard.Modifiers != ModifierKeys.None) return;
                    if (e.Key == Key.Left) { Show(at - 1); e.Handled = true; } else if (e.Key == Key.Right) { Show(at + 1); e.Handled = true; }
                };
            }
            figure.Children.Add(new Border { BorderBrush = Res<Brush>("B.Line2"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Background = Res<Brush>("B.Surface2"), Padding = new Thickness(8), Child = img });
            root.Children.Add(figure);
            Show(at);
            w.Content = root; w.Loaded += (s, e) => close.Focus();
            return w;
        }

        // ------------------------------------------------------------------ the built-in topics
        UIElement BuiltinTopic(string name)
        {
            switch (name)
            {
                case "about-console":
                {
                    var sp = new StackPanel(); sp.Children.Add(ConsoleInfo(profile)); sp.Children.Add(SessionInfo());
                    sp.Children.Add(Buttons(B("Open About This Console…", "Btn", OpenAboutConsole)));
                    return Card(sp);
                }
                case "about-app":
                {
                    var ab = new StackPanel();
                    ab.Children.Add(T(ProductTitle + ", version " + VersionLabel, Ty.Body, true));
                    ab.Children.Add(LinkNames(T("By Rance9 (Rance9 LLC). Independent; not affiliated with Nintendo.", Ty.Text)));
                    ab.Children.Add(T("Free software under the GNU General Public License, version 3 or later. It includes code from Hakchi2 CE and FelLib (GPL-3.0), LibUsbDotNet (LGPL-3.0) and, in the program it sends to the console, hakchi's recovery kernel and tools (GPL). Credits lists every part and its license.", Ty.Text));
                    ab.Children.Add(T("Version " + VersionLabel + " · engine " + EngineInfo.Engine, Ty.Fine, false, true));
                    var problem = new ContentControl();
                    ab.Children.Add(Buttons(B("Credits and Licenses", "Btn", () => { Activate(); Go("Credits"); }), B("Source Code", "Btn.Ghost", () => { if (!Launch(SourceUrl)) problem.Content = Note("The web browser could not be opened. The address is " + SourceUrl, "warn"); })));
                    ab.Children.Add(problem);
                    ab.Children.Add(UpdatePanel());   // the version check: Check for Updates and its answer (WorkbenchUpdates.cs)
                    return Card(ab);
                }
                case "glossary":
                {
                    var sp = new StackPanel(); SetNoTerms(sp, true);   // the explanations themselves
                    foreach (var t in Glossary.Terms)
                    {
                        sp.Children.Add(new TextBlock { Text = t.Word, FontSize = 14, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 8, 0, 2), TextWrapping = TextWrapping.Wrap });
                        sp.Children.Add(new TextBlock { Text = t.Text, FontSize = 13.5, LineHeight = 20, TextWrapping = TextWrapping.Wrap });
                        if (t.Detail != null) sp.Children.Add(new TextBlock { Text = t.Detail, FontSize = 12.5, Foreground = Res<Brush>("B.Muted"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 4, 0, 0) });
                        if (t.Tech != null) sp.Children.Add(new TextBlock { Text = "Technical name: " + t.Tech, FontSize = 12.5, Foreground = Res<Brush>("B.Muted"), TextWrapping = TextWrapping.Wrap });
                    }
                    return sp;
                }
                default: return new TextBlock();
            }
        }
    }
}
