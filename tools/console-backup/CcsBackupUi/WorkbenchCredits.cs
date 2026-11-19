// Classic Cart Studio console backup and restore window. Copyright (C) 2026 Rance9. GPL-3.0-or-later.
// CCS Console Workbench: Credits (under Settings and Help, the same page in the standalone and in Classic Cart Studio for Windows). Nothing
// here is written for this page: every word comes from a source the rest of Classic Cart Studio already keeps, so nothing is maintained twice.
//   Classic Cart Studio's Credits   the approved Credits section of Classic Cart Studio's own Help (tools/rom-pipeline/studio/guide.html,
//                                   carried in this library as it is and read from its <section data-ch="credits">): its list and "How
//                                   Classic Cart Studio is made", word for word, links included.
//   In this program                 this program's own notice (NOTICE, the file every build already carries): what it contains, part by part.
//   Licenses and notices            the whole NOTICE, then the CCS HOME package this program carries: its own notice of every part
//                                   (licenses/NOTICE.txt, made by tools/companion/native_home/notices.py), the license texts beside it and
//                                   the HOME's own license files (home/licenses/). No claim that they are complete is made here.
//   Source code                     the notice's own words on the corresponding source, and classiccartstudio.com/source/ (only on a click).
using CcsBackup.Engine;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace CcsBackup.Ui
{
    public sealed partial class BackupWorkspace
    {
        void ShowCredits()
        {
            var sp = PageHead("Credits", "The people and projects Classic Cart Studio and Workbench are built on, the licenses that come with this program, and where to get its source code.");

            var cc = new StackPanel();
            cc.Children.Add(T("Classic Cart Studio's Credits", Ty.Heading, true, false, new Thickness(0, 0, 0, 4)));
            cc.Children.Add(T("CCS Console Workbench is part of Classic Cart Studio. These are Classic Cart Studio's Credits, as its Help shows them.", Ty.Small, false, true, new Thickness(0, 0, 0, 10)));
            var blocks = ApprovedCredits();
            if (blocks.Count == 0) cc.Children.Add(Note("Classic Cart Studio's Credits could not be read in this copy.", "warn"));
            foreach (var b in blocks) cc.Children.Add(b);
            sp.Children.Add(Card(cc));

            var own = new StackPanel();
            own.Children.Add(T("In this program", Ty.Heading, true, false, new Thickness(0, 0, 0, 4)));
            own.Children.Add(T(ProductTitle + ", version " + VersionLabel + ". By Rance9 (Rance9 LLC). Independent; not affiliated with Nintendo.", Ty.Text));
            own.Children.Add(T("Its code from Hakchi2 CE comes from Hakchi2 CE 3.9.3 as released (tag v3.9.3, commit 25bfcfd). Later Hakchi2 CE changes are not part of this version.", Ty.Text));
            foreach (var b in NoticeOverview()) own.Children.Add(b);
            sp.Children.Add(Card(own));

            var lic = new StackPanel();
            lic.Children.Add(T("Licenses and notices", Ty.Heading, true, false, new Thickness(0, 0, 0, 4)));
            lic.Children.Add(T("This program's notice, then the CCS HOME package this program places on a console: its notice of each part, the license texts beside it and the HOME's own license files.", Ty.Small, false, true));
            if (!string.IsNullOrEmpty(opt.NoticeText)) lic.Children.Add(Details("This program's notice (NOTICE)", NoticeBox(opt.NoticeText)));
            foreach (var (name, text) in HomeLicenseFiles()) lic.Children.Add(Details("CCS HOME, carried in this program: " + name, NoticeBox(text)));
            sp.Children.Add(Card(lic));

            var src = new StackPanel();
            src.Children.Add(T("Source code", Ty.Heading, true, false, new Thickness(0, 0, 0, 6)));
            var corr = NoticeParagraph("Corresponding source");
            if (corr != null) src.Children.Add(T(corr, Ty.Text));
            src.Children.Add(T("The button opens classiccartstudio.com/source/ in your web browser.", Ty.Small, false, true));
            var problem = new ContentControl();
            src.Children.Add(Buttons(B("Source Code", "Btn", () => { if (!Launch(SourceUrl)) problem.Content = Note("The web browser could not be opened. The address is " + SourceUrl, "warn"); })));
            src.Children.Add(problem);
            src.Children.Add(T("Version " + VersionLabel + " · engine " + EngineInfo.Engine, Ty.Fine, false, true));
            sp.Children.Add(Card(src));
            SetNoTerms(sp, true);   // approved Credits words, notices and license texts: exactly as they are
            Show(sp, "credits");
        }

        TextBox NoticeBox(string text) => new TextBox { Text = text, IsReadOnly = true, TextWrapping = TextWrapping.Wrap, MaxHeight = 360, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, FontFamily = Res<FontFamily>("F.Mono"), FontSize = 11.5 };

        // ------------------------------------------------------------------ Classic Cart Studio's approved Credits (guide.html)
        static string guideCredits;
        /// <summary>the Credits section of Classic Cart Studio's Help, as carried in this library (null when it is not)</summary>
        internal static string CreditsSection()
        {
            if (guideCredits != null) return guideCredits.Length == 0 ? null : guideCredits;
            string html = null;
            try { using (var s = typeof(BackupWorkspace).Assembly.GetManifestResourceStream("credits/guide.html")) if (s != null) using (var r = new StreamReader(s)) html = r.ReadToEnd(); } catch (IOException) { }
            var m = html == null ? null : Regex.Match(html, "<section data-ch=\"credits\">(.*?)</section>", RegexOptions.Singleline);
            guideCredits = m != null && m.Success ? m.Groups[1].Value : "";
            return guideCredits.Length == 0 ? null : guideCredits;
        }

        /// <summary>the approved Credits as the page shows them: the list, then "How Classic Cart Studio is made" with its paragraphs (the
        /// section's own navigation buttons are left out); every word and link as the Help has it</summary>
        List<UIElement> ApprovedCredits()
        {
            var l = new List<UIElement>(); var sec = CreditsSection(); if (sec == null) return l;
            sec = Regex.Replace(sec, "<div class=\"gft\">.*?</div>", "", RegexOptions.Singleline);
            sec = Regex.Replace(sec, "<h3>.*?</h3>", "", RegexOptions.Singleline);
            foreach (Match m in Regex.Matches(sec, "<(li|h4|p)>(.*?)</\\1>", RegexOptions.Singleline))
            {
                var kind = m.Groups[1].Value; var tb = HtmlText(m.Groups[2].Value);
                if (kind == "h4") { tb.FontSize = Ty.Heading; tb.FontWeight = FontWeights.SemiBold; tb.Margin = new Thickness(0, 12, 0, 6); l.Add(tb); }
                else if (kind == "li") l.Add(Bullet(tb));
                else { tb.Margin = new Thickness(0, 0, 0, 8); l.Add(tb); }
            }
            return l;
        }

        /// <summary>a bullet: a dot in its own column, the text wrapping beside it</summary>
        Grid Bullet(TextBlock tb)
        {
            var g = new Grid { Margin = new Thickness(0, 0, 0, 6) }; g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(18) }); g.ColumnDefinitions.Add(new ColumnDefinition());
            g.Children.Add(new TextBlock { Text = "•", FontSize = 13, Foreground = Res<Brush>("B.Muted") });
            tb.Margin = new Thickness(0); Grid.SetColumn(tb, 1); g.Children.Add(tb); return g;
        }

        /// <summary>a little HTML (b, i, a, span and character references) as a wrapping text block; a link opens only when clicked</summary>
        TextBlock HtmlText(string html)
        {
            var tb = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = 13, LineHeight = 19, Margin = new Thickness(0, 0, 0, 6) };
            bool bold = false, italic = false; string href = null; Hyperlink link = null;
            foreach (Match t in Regex.Matches(html, "<[^>]+>|[^<]+"))
            {
                var v = t.Value;
                if (v.StartsWith("<"))
                {
                    var tag = Regex.Match(v, "^</?\\s*([a-zA-Z0-9]+)").Groups[1].Value.ToLowerInvariant(); bool close = v.StartsWith("</");
                    if (tag == "b" || tag == "strong") bold = !close;
                    else if (tag == "i" || tag == "em") italic = !close;
                    else if (tag == "a")
                    {
                        if (close) { link = null; href = null; }
                        else
                        {
                            href = WebUtility.HtmlDecode(Regex.Match(v, "href=\"([^\"]*)\"").Groups[1].Value);
                            var url = href; link = new Hyperlink { Foreground = Res<Brush>("B.Red3") }; link.Click += (s, e) => Launch(url);
                            AutomationProperties.SetHelpText(link, url); tb.Inlines.Add(link);
                        }
                    }
                    continue;
                }
                var run = new Run(Regex.Replace(WebUtility.HtmlDecode(v), "\\s+", " ")) { FontWeight = bold ? FontWeights.SemiBold : FontWeights.Normal, FontStyle = italic ? FontStyles.Italic : FontStyles.Normal };
                if (link != null) link.Inlines.Add(run); else tb.Inlines.Add(run);
            }
            // the first and last runs: no leading or trailing space from the source's line breaks
            if (tb.Inlines.FirstInline is Run f) f.Text = f.Text.TrimStart();
            if (tb.Inlines.LastInline is Run z) z.Text = z.Text.TrimEnd();
            AutomationProperties.SetName(tb, InlineText(tb));   // its words, for screen readers (a block built from pieces has no Text)
            return tb;
        }

        // ------------------------------------------------------------------ this program's notice (NOTICE)
        /// <summary>the notice's first part (what this program contains, part by part, before the reproduced license texts), as readable
        /// paragraphs, headings and bullets</summary>
        List<UIElement> NoticeOverview()
        {
            var l = new List<UIElement>(); var text = opt.NoticeText; if (string.IsNullOrEmpty(text)) return l;
            var end = text.IndexOf("NOTICES REPRODUCED AS THEIR LICENSES REQUIRE", StringComparison.Ordinal); if (end > 0) text = text.Substring(0, end);
            foreach (var (kind, s) in NoticeBlocks(text))
            {
                if (s.StartsWith("=====")) continue;
                if (kind == "h") l.Add(T(Capital(s.ToLowerInvariant()), Ty.Heading, true, false, new Thickness(0, 12, 0, 6)));
                else if (kind == "li") l.Add(Bullet(T(s, 13)));
                else l.Add(T(s, 13, false, false, new Thickness(0, 0, 0, 8)));
            }
            return l;
        }

        /// <summary>the notice's plain text as blocks: a line of capitals is a heading, "- " starts a bullet (its indented lines continue it),
        /// a blank line ends a paragraph</summary>
        static IEnumerable<(string kind, string text)> NoticeBlocks(string text)
        {
            string kind = null; var cur = new List<string>();
            foreach (var raw in text.Replace("\r\n", "\n").Split('\n'))
            {
                var line = raw.TrimEnd();
                bool heading = line.Length > 6 && line == line.ToUpperInvariant() && line.Any(char.IsLetter) && !line.StartsWith("-");
                if (line.Length == 0 || heading || line.StartsWith("- ") || (kind == "li" && !line.StartsWith(" ")))
                {
                    if (cur.Count > 0) yield return (kind, string.Join(" ", cur)); cur.Clear(); kind = null;
                    if (line.Length == 0) continue;
                    if (heading) { yield return ("h", line); continue; }
                    if (line.StartsWith("- ")) { kind = "li"; cur.Add(line.Substring(2).Trim()); continue; }
                }
                if (kind == null) kind = "p";
                cur.Add(line.Trim());
            }
            if (cur.Count > 0) yield return (kind, string.Join(" ", cur));
        }

        /// <summary>the notice's paragraph that starts with these words (as one line), or null</summary>
        string NoticeParagraph(string start) => string.IsNullOrEmpty(opt.NoticeText) ? null : NoticeBlocks(opt.NoticeText).Where(b => b.kind == "p" && b.text.StartsWith(start, StringComparison.Ordinal)).Select(b => b.text).FirstOrDefault();

        /// <summary>the notice and license files of the CCS HOME package this program carries, read from the package itself: licenses/NOTICE.txt,
        /// the other files in licenses/ (named with their folder), then the HOME's own home/licenses/ (THIRD-PARTY.txt first; named by file)</summary>
        static List<(string name, string text)> HomeLicenseFiles()
        {
            var l = new List<(string, string)>();
            try
            {
                var p = HomePackage.Embedded("mod"); if (p == null) return l;
                foreach (var f in p.Files.Where(x => x.Rel.StartsWith("licenses/", StringComparison.Ordinal)).OrderBy(x => x.Rel == "licenses/NOTICE.txt" ? 0 : 1).ThenBy(x => x.Rel, StringComparer.Ordinal))
                    l.Add((f.Rel, System.Text.Encoding.UTF8.GetString(p.Read(f.Rel))));
                foreach (var f in p.Files.Where(x => x.Rel.StartsWith("home/licenses/", StringComparison.Ordinal)).OrderBy(x => x.Rel == "home/licenses/THIRD-PARTY.txt" ? 0 : 1).ThenBy(x => x.Rel, StringComparer.Ordinal))
                    l.Add((Path.GetFileName(f.Rel), System.Text.Encoding.UTF8.GetString(p.Read(f.Rel))));
            }
            catch (Exception ex) when (ex is HomePackageException || ex is IOException || ex is InvalidDataException) { }
            return l;
        }
    }
}
