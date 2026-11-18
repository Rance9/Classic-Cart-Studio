// Classic Cart Studio console backup and restore window. Copyright (C) 2026 Rance9. GPL-3.0-or-later.
// HOME Designer (inside Console Composer; the same window in the standalone Workbench and in Classic Cart Studio for Windows): the person's
// own pictures for the HOME's picture places, the startup picture, text colors and the existing fonts, settings, safe layout regions,
// folders and game order, credits, Save As and a checked Share. Every change goes through the one design model (CcsBackup.Compose.HomeDesign),
// which validates a copy before it replaces the design and keeps what a newer version added. A design this version can only inspect is
// shown, never changed, and its original file can be exported as it was received.
using CcsBackup.Compose;
using CcsBackup.Library;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using CUnits = CcsBackup.Compose.Units;

namespace CcsBackup.Ui
{
    public sealed partial class BackupWorkspace
    {
        HomeDesign design, designOpened; string designPath, designProblem; bool designDirty;
        const string DesignFilter = "Shared HOME Designs (*.ccshome)|*.ccshome";
        const string PictureFilter = "Pictures (PNG or JPEG)|*.png;*.jpg;*.jpeg";

        void ShowDesigner()
        {
            if (design != null) { ShowDesignEditor(); return; }
            var sp = SectionHead("HOME Designer", "Design how CCS HOME looks on your " + Con + " with your own pictures, colors, folders and game order, starting from one of its four themes. You work on a " + FileKinds.HomeDesign + " file on this PC; nothing on the console changes until you check the design and apply it.");
            var c = new StackPanel(); c.Children.Add(T("Start a new design", Ty.Heading, true, false, new Thickness(0, 0, 0, 10)));
            var theme = new ComboBox();
            foreach (var t in DesignProfile.Themes) theme.Items.Add(new ComboBoxItem { Content = ThemeName(t), Tag = t });
            theme.SelectedIndex = 0; System.Windows.Automation.AutomationProperties.SetName(theme, "Starting theme");
            c.Children.Add(FieldRow(("Starting theme", theme, 260)));
            c.Children.Add(ThemeExampleFor(theme));   // the chosen theme's example, larger on a click (WorkbenchThemeExamples.cs)
            c.Children.Add(Buttons(B("New Design", "Btn.Primary", () => DesignTry(() => { design = HomeDesign.New("My HOME", (string)((ComboBoxItem)theme.SelectedItem).Tag); designOpened = null; designPath = null; designDirty = true; })),
                                   B("Open a Shared HOME Design…", "Btn", OpenDesign)));
            c.Children.Add(T("A " + FileKinds.HomeDesign + " is a .ccshome file with pictures and appearance settings only: no games, saves or console files.", Ty.Small, false, true, new Thickness(0, 4, 0, 0)));
            sp.Children.Add(Card(c));
            var can = new StackPanel(); can.Children.Add(T("What a design can change", Ty.Heading, true, false, new Thickness(0, 0, 0, 8)));
            can.Children.Add(KeyValues(("Pictures", Cap(string.Join(", ", DesignProfile.SlotIds.Select(s => DesignProfile.Slots[s].Text))) + "."),
                                       ("Startup picture", "A built-in one, a different one each time, or your own picture."),
                                       ("Text and colors", "Text and panel colors, with the HOME's existing fonts."),
                                       ("Layout", "Move groups of the HOME's controls, and add panels, labels and pictures of your own inside the screen's safe area."),
                                       ("Folders and game order", "Folders, each with a picture of your own if you like, and the order of the game list."),
                                       ("Not part of a design", "Animation, new fonts, programs or scripts, and any games, saves or console files.")));
            can.Children.Add(T("Limits: the HOME's controls move as whole groups and keep their theme's size (only their place changes), and a design's pictures must fit in the memory the console's HOME has. The editor shows both as you work.", Ty.Small, false, true, new Thickness(0, 4, 0, 0)));
            sp.Children.Add(Card(can));
            sp.Children.Add(Buttons(B("Back", "Btn.Ghost", BackToSection)));
            Show(sp, "designer");
        }

        /// <summary>a built-in startup picture's display name ("forgotten-keep" → "Forgotten Keep")</summary>
        static string StartupName(string id) => string.Join(" ", (id ?? "").Split('-').Where(w => w.Length > 0).Select(w => char.ToUpperInvariant(w[0]) + w.Substring(1)));
        /// <summary>a baked HOME font's name and size ("sans400_20" → "Barlow, 20 px"; the families as native_home/derive.py FAMILY_LABEL names them)</summary>
        static string FontName(string id)
        {
            var m = System.Text.RegularExpressions.Regex.Match(id ?? "", @"^([a-z]+?)(\d{3})?_?(\d+)(b?)$");
            if (!m.Success) return id;
            string fam = m.Groups[1].Value, w = m.Groups[2].Value, px = m.Groups[3].Value, alt = m.Groups[4].Value;
            string name = fam == "silk" ? "Silkscreen" : fam == "silkh" || fam == "silkt" ? "Silkscreen, capitals" : fam == "sans" ? "Barlow" : fam == "cin" ? "Cinzel" : fam == "vt" ? "VT323" : fam == "px" ? "Press Start 2P" : fam == "mono" ? "Space Mono" : fam;
            string weight = w == "600" ? " Semibold" : w == "700" ? " Bold" : w == "800" ? " Extra Bold" : "";
            return name + weight + ", " + px + " px" + (alt == "b" ? " (second style)" : "");
        }
        static string ThemeName(string t) => t == "arcade-grid" ? "Arcade Grid" : t == "box-art-showcase" ? "Box Art Showcase" : t == "featured-shelf" ? "Featured Shelf" : t == "adventure-map" ? "Adventure Map" : t;

        void DesignTry(Action change)
        {
            try { Mouse.OverrideCursor = Cursors.Wait; change(); designProblem = null; }
            catch (ComposeException ex) { designProblem = ex.Message; }
            catch (IOException ex) { designProblem = "The file could not be used: " + ex.Message; }
            catch (UnauthorizedAccessException ex) { designProblem = "The file could not be used: " + ex.Message; }
            finally { Mouse.OverrideCursor = null; }
            if (design != null) ShowDesignEditor(); else ShowDesigner();
        }
        void Change(Action change) => DesignTry(() => { change(); designDirty = true; });

        void OpenDesign()
        {
            if (!DesignLeaveOk()) return;
            var d = new Microsoft.Win32.OpenFileDialog { Title = "Open a Shared HOME Design", Filter = DesignFilter };
            if (d.ShowDialog(this) != true) return;
            DesignTry(() =>
            {
                var len = new FileInfo(d.FileName).Length;
                if (len > DesignProfile.MaxArchiveBytes) throw new ComposeException("design.size", "This file is larger than a Shared HOME Design can be (" + CUnits.Size(DesignProfile.MaxArchiveBytes) + ").");
                design = HomeDesign.Open(d.FileName); designOpened = HomeDesign.Open(d.FileName); designPath = d.FileName; designDirty = false;
            });
        }

        bool DesignLeaveOk() => design == null || !designDirty || MessageBox.Show(this, "This design has changes that are not saved. Leave it without saving?", ProductTitle, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;

        void SaveDesign(bool saveAs)
        {
            var path = designPath;
            if (saveAs || path == null)
            {
                var d = new Microsoft.Win32.SaveFileDialog { Title = "Save the Shared HOME Design", Filter = DesignFilter, FileName = SafeFileName(design.Name ?? "My HOME") + DesignProfile.Extension, OverwritePrompt = true };
                if (d.ShowDialog(this) != true) return; path = d.FileName;
            }
            DesignTry(() =>
            {
                if (designOpened != null && design.ContentSha256 != designOpened.ContentSha256) design.NextRevision();
                design.Save(path, overwrite: true); designPath = path; designOpened = HomeDesign.Open(path); designDirty = false;
            });
        }

        static string SafeFileName(string s) { foreach (var ch in System.IO.Path.GetInvalidFileNameChars()) s = s.Replace(ch, '-'); s = s.Trim().TrimEnd('.'); return s.Length == 0 ? "My HOME" : s.Length > 60 ? s.Substring(0, 60) : s; }

        void ShareDesign()
        {
            var titles = new Dictionary<string, string>();
            foreach (var r in design.Order.Concat(design.Folders.SelectMany(f => (f["games"] as List<object> ?? new List<object>()).OfType<JObj>().Select(GameRef.Of))))
                if (!r.IsPlaceholder && r.Title != null) titles[r.Catalog] = r.Title;
            var priv = new List<string> { Environment.UserName, Environment.MachineName, settings.PrimaryFolder, settings.ArchiveFolder };
            try { priv.AddRange(History.List(settings).Select(e => e.ConsoleTag)); } catch (Exception) { }
            var p = design.PrepareShare(id => titles.TryGetValue(id, out var t) ? t : null, priv.Where(x => !string.IsNullOrEmpty(x)));
            var sp = SectionHead("Share the HOME Design", "Save a copy of this design that you can give to someone else, as a " + FileKinds.HomeDesign + " file on this PC. Check what it holds before you pass it on; your own design file stays as it is.");
            var c = new StackPanel(); c.Children.Add(T(DesignProfile.Label, Ty.Small, false, true));
            c.Children.Add(T("Included", Ty.Body, true, false, new Thickness(0, 8, 0, 4))); foreach (var x in p.Included) c.Children.Add(T("• " + x, Ty.Text));
            if (p.Removed.Count > 0) { c.Children.Add(T("Removed from the shared copy", Ty.Body, true, false, new Thickness(0, 8, 0, 4))); foreach (var x in p.Removed) c.Children.Add(T("• " + x, Ty.Text)); }
            foreach (var x in p.Refusals) c.Children.Add(Note(x, "bad"));
            c.Children.Add(Note("Share only pictures you have the right to share.", "warn"));
            sp.Children.Add(Card(c));
            sp.Children.Add(Buttons(p.CanShare ? B("Save the Shared Copy…", "Btn.Primary", () =>
            {
                var d = new Microsoft.Win32.SaveFileDialog { Title = "Save a Shared Copy (Shared HOME Design)", Filter = DesignFilter, FileName = SafeFileName(design.Name ?? "My HOME") + " (shared)" + DesignProfile.Extension, OverwritePrompt = true };
                if (d.ShowDialog(this) != true) return;
                DesignTry(() => { p.Shared.Save(d.FileName, overwrite: true); try { ShareGuard.RequireShareable(d.FileName); } catch (ComposeException) { File.Delete(d.FileName); throw; } MessageBox.Show(this, "The shared copy was saved and checked:\n\n" + d.FileName, ProductTitle); });
            }) : null, B("Back to the Design", "Btn.Ghost", () => ShowDesignEditor())));
            if (!p.CanShare) sp.Children.Add(T("A shared copy cannot be saved until the problems above are fixed in the design.", Ty.Small, false, true));
            Show(sp, "design-share");
        }

        // ------------------------------------------------------------------ the editor
        void ShowDesignEditor()
        {
            var h = design; var sp = new StackPanel();
            var head = PageHead("HOME Designer", "Make your changes here, then save the design to a file on this PC. Nothing on your " + Con + " changes until you check the design and apply it to the console.");
            ((FrameworkElement)head.Children[head.Children.Count - 1]).Margin = new Thickness(0, 0, 0, 6);
            sp.Children.Add(head);
            sp.Children.Add(T(designPath == null ? "Not saved yet" : FileKinds.HomeDesign + " " + System.IO.Path.GetFileName(designPath) + (designDirty ? "  ·  has changes that are not saved" : "  ·  all changes saved"), Ty.Small, true, true));
            if (designProblem != null) sp.Children.Add(Note(designProblem, "bad"));
            if (h.InspectOnly || !h.CanApply)
                sp.Children.Add(Note((h.InspectOnly ? "This design was made by a newer version of Classic Cart Studio. It is shown here but cannot be changed, shared or applied; Export the Original File keeps it exactly as it was received. " : "This design cannot be applied yet: ") + string.Join("; ", h.Unsupported) + ".", "warn"));
            sp.Children.Add(Buttons(B("Save", "Btn.Primary", () => SaveDesign(false), !h.InspectOnly), B("Save As…", "Btn", () => SaveDesign(true), !h.InspectOnly),
                                    B("Share…", "Btn", ShareDesign, !h.InspectOnly), B("Check and Apply on My Console…", "Btn", () => ShowDesignOnConsole(), h.CanApply), h.OriginalFile != null ? B("Export the Original File…", "Btn.Ghost", () =>
                                    {
                                        var d = new Microsoft.Win32.SaveFileDialog { Title = "Export the Original File (Shared HOME Design)", Filter = DesignFilter, FileName = System.IO.Path.GetFileName(designPath ?? "design.ccshome"), OverwritePrompt = false };
                                        if (d.ShowDialog(this) == true) DesignTry(() => h.ExportOriginal(d.FileName));
                                    }) : null,
                                    B("Close", "Btn.Ghost", () => { if (DesignLeaveOk()) { design = designOpened = null; designPath = null; designDirty = false; designProblem = null; ShowDesigner(); } })));
            // the console HOME's memory for this design's pictures, as the editor works it out (the check on the console's own HOME program
            // measures the real figures before a design can be applied)
            var bud = h.Budget(); sp.Children.Add(Note(bud.Text + (bud.Ok ? "" : " Use fewer or smaller pictures to fit."), bud.Ok ? "info" : "bad"));
            bool ro = h.InspectOnly;

            // name, author, theme
            var g = new StackPanel();
            var name = new TextBox { Text = h.Name ?? "", IsReadOnly = ro }; var author = new TextBox { Text = (h.Document["design"] as JObj)?["author"] as string ?? "", IsReadOnly = ro };
            System.Windows.Automation.AutomationProperties.SetName(name, "Design name"); System.Windows.Automation.AutomationProperties.SetName(author, "Made by");
            var theme = new ComboBox { IsEnabled = !ro }; foreach (var t in DesignProfile.Themes) theme.Items.Add(new ComboBoxItem { Content = ThemeName(t), Tag = t, IsSelected = t == h.BaseTheme });
            g.Children.Add(FieldRow(("Design name", name, 280), ("Made by (optional)", author, 240), ("Starting theme", theme, 220)));
            g.Children.Add(ThemeExampleFor(theme));   // the starting theme's example: the theme, not this design
            g.Children.Add(Buttons(B("Apply", "Btn", () => Change(() => { if (name.Text != h.Name) h.SetName(name.Text.Trim()); var a = author.Text.Trim(); if (a != ((h.Document["design"] as JObj)?["author"] as string ?? "")) h.SetAuthor(a); var th = (string)((ComboBoxItem)theme.SelectedItem).Tag; if (th != h.BaseTheme) h.SetBaseTheme(th); }), !ro)));
            sp.Children.Add(Section("Design", g));

            sp.Children.Add(Section("Preview", DesignPreview(h)));

            // pictures
            var pics = new CardPanel { MinItemWidth = 220, MaxColumns = 4, Gap = 12 };
            foreach (var slot in DesignProfile.SlotIds)
            {
                var rule = DesignProfile.Slots[slot];
                var cell = new Grid(); cell.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); cell.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); cell.RowDefinitions.Add(new RowDefinition()); cell.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                cell.Children.Add(T(Cap(rule.Text), 13.5, true, false, new Thickness(0, 0, 0, 2)));
                var size = T((rule.Exact ? "exactly " : "up to ") + rule.MaxW + " × " + rule.MaxH + (rule.Alpha ? ", see-through allowed" : ""), 11.5, false, true, new Thickness(0, 0, 0, 8)); Grid.SetRow(size, 1); cell.Children.Add(size);
                var sha = h.ImageFor(slot);
                var pic = sha != null && h.Images.TryGetValue(sha, out var b) ? Thumb(b, 230, 130) : (UIElement)new Border { Height = 72, CornerRadius = new CornerRadius(6), BorderBrush = Res<Brush>("B.Line2"), BorderThickness = new Thickness(1), Child = T("The theme's own", 12.5, false, true, new Thickness(0)) };
                if (pic is Border pb && pb.Child is TextBlock pt) { pt.HorizontalAlignment = HorizontalAlignment.Center; pt.VerticalAlignment = VerticalAlignment.Center; }
                Grid.SetRow(pic, 2); cell.Children.Add(pic);
                var s = slot; var orig = designOpened?.ImageFor(slot);
                var row = new WrapPanel { Margin = new Thickness(0, 8, 0, 0) };
                row.Children.Add(Small("Choose…", () => PickPicture(rule, bytes => h.SetSlot(s, bytes)), !ro));
                if (sha != null) row.Children.Add(Small("Use the Theme's Own", () => Change(() => h.SetSlot(s, null)), !ro));
                if (designOpened != null && orig != sha) row.Children.Add(Small("Revert", () => Change(() => h.SetSlot(s, orig == null ? null : designOpened.Images[orig])), !ro));
                Grid.SetRow(row, 3); cell.Children.Add(row);
                pics.Children.Add(new Border { Background = Res<Brush>("B.Surface2"), CornerRadius = new CornerRadius(8), Padding = new Thickness(12), Child = cell });
            }
            sp.Children.Add(Section("Pictures", pics));

            // startup
            var st = h.Document["startup"] as JObj; var mode = st?["mode"] as string ?? "random";
            var stp = new StackPanel();
            stp.Children.Add(T(mode == "random" ? "A different built-in startup picture each time." : mode == "builtin" ? "Built-in: " + st["builtin"] : "Your own picture (1280 × 720 JPEG, at most " + CUnits.Size(450000) + ").", 13));
            if (mode == "image" && st["image"] is string ss && h.Images.TryGetValue(ss, out var sb)) stp.Children.Add(Thumb(sb, 320, 180));
            var builtin = new ComboBox { Width = 220, IsEnabled = !ro, Margin = new Thickness(0, 4, 6, 0) }; foreach (var b0 in DesignProfile.StartupBuiltins) builtin.Items.Add(new ComboBoxItem { Content = StartupName(b0), Tag = b0, IsSelected = b0 == st?["builtin"] as string });
            System.Windows.Automation.AutomationProperties.SetName(builtin, "Built-in startup picture");
            if (builtin.SelectedIndex < 0) builtin.SelectedIndex = 0;
            var srow = new WrapPanel(); srow.Children.Add(Small("A Different One Each Time", () => Change(() => h.SetStartup("random")), !ro)); srow.Children.Add(builtin);
            srow.Children.Add(Small("Use This One", () => Change(() => h.SetStartup("builtin", (string)((ComboBoxItem)builtin.SelectedItem).Tag)), !ro));
            srow.Children.Add(Small("My Own Picture…", () => PickPicture(DesignProfile.Slots["startup"], bytes => h.SetStartup("image", null, bytes)), !ro));
            stp.Children.Add(srow); sp.Children.Add(Section("Startup picture", stp));

            // text and colors
            var ap = h.Document["appearance"] as JObj ?? new JObj(); var app = new StackPanel();
            var boxes = new Dictionary<string, TextBox>();
            var colorFields = new List<(string, FrameworkElement, double)>();
            foreach (var (k, label) in new[] { ("text_color", "Text color"), ("accent_color", "Highlight color"), ("panel_color", "Panel color") })
            {   // the color as #rrggbb, its swatch beside it
                var tb = new TextBox { Text = ap[k] as string ?? "", IsReadOnly = ro }; System.Windows.Automation.AutomationProperties.SetName(tb, label); boxes[k] = tb;
                var cg = new Grid(); cg.ColumnDefinitions.Add(new ColumnDefinition()); cg.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); cg.Children.Add(tb);
                var sw = Swatch(ap[k] as string); ((FrameworkElement)sw).Margin = new Thickness(8, 0, 0, 0); ((FrameworkElement)sw).VerticalAlignment = VerticalAlignment.Center; Grid.SetColumn(sw, 1); cg.Children.Add(sw);
                colorFields.Add((label + " (#rrggbb)", cg, 190));
            }
            app.Children.Add(FieldRow(colorFields.ToArray()));
            var op = new Slider { Minimum = 0, Maximum = 100, Value = ap["panel_opacity"] is long o ? o : 80, IsSnapToTickEnabled = true, TickFrequency = 5, IsEnabled = !ro, VerticalAlignment = VerticalAlignment.Center };
            System.Windows.Automation.AutomationProperties.SetName(op, "Panel opacity"); var opText = T(((int)op.Value) + "%", 13, false, false, new Thickness(10, 0, 0, 0)); opText.VerticalAlignment = VerticalAlignment.Center; op.ValueChanged += (s0, e0) => opText.Text = ((int)op.Value) + "%";
            var opRow = new Grid { MinHeight = 34 }; opRow.ColumnDefinitions.Add(new ColumnDefinition()); opRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(48) }); opRow.Children.Add(op); Grid.SetColumn(opText, 1); opRow.Children.Add(opText);
            var fonts = new Dictionary<string, ComboBox>(); var fontFields = new List<(string, FrameworkElement, double)> { ("Panel opacity", opRow, 300) };
            foreach (var (k, label) in new[] { ("title_font", "Title font"), ("text_font", "Text font") })
            { var cb = new ComboBox { IsEnabled = !ro }; foreach (var f in DesignProfile.Fonts.Where(f => k == "title_font" ? DesignProfile.FontPx(f) <= DesignProfile.MaxMenuTitlePx : DesignProfile.FontPx(f) >= DesignProfile.MinMenuTextPx && DesignProfile.FontPx(f) <= DesignProfile.MaxMenuTextPx)) cb.Items.Add(new ComboBoxItem { Content = FontName(f), Tag = f, IsSelected = f == ap[k] as string }); fonts[k] = cb; fontFields.Add((label, cb, 230)); }
            app.Children.Add(FieldRow(fontFields.ToArray()));
            app.Children.Add(Buttons(B("Apply", "Btn", () => Change(() =>
            {
                foreach (var kv in boxes) { var v = kv.Value.Text.Trim().ToLowerInvariant(); if (v != (ap[kv.Key] as string ?? "")) h.SetAppearance(kv.Key, v); }
                if ((int)op.Value != (ap["panel_opacity"] is long o2 ? o2 : -1)) h.SetAppearance("panel_opacity", (int)op.Value);
                foreach (var kv in fonts) { var v = (kv.Value.SelectedItem as ComboBoxItem)?.Tag as string; if (v != null && v != ap[kv.Key] as string) h.SetAppearance(kv.Key, v); }
            }), !ro)));
            sp.Children.Add(Section("Text and colors", app));

            // settings
            var se = h.Document["settings"] as JObj ?? new JObj(); var sep = new StackPanel();
            foreach (var (k, label) in new[] { ("music", "HOME music on"), ("hints", "Show button hints"), ("reduced_motion", "Less motion") })
            { var cb = new CheckBox { Content = label, IsChecked = se[k] is bool bv && bv, IsEnabled = !ro, Margin = new Thickness(0, 2, 0, 2) }; var kk = k; cb.Click += (s0, e0) => Change(() => h.SetSetting(kk, cb.IsChecked == true)); sep.Children.Add(cb); }
            sp.Children.Add(Section("Settings", sep));

            sp.Children.Add(Section("Layout", LayoutEditor(h, ro)));
            sp.Children.Add(Section("Folders and game order", FolderEditor(h, ro)));

            // credits
            var cr = new StackPanel();
            foreach (var c0 in (h.Document["credits"] as List<object> ?? new List<object>()).OfType<JObj>()) cr.Children.Add(T("• " + Display.Line(c0["by"] as string, 60) + (c0["title"] is string ct ? ": " + Display.Line(ct, 80) : "") + (c0["license"] is string cl ? " (" + Display.Line(cl, 60) + ")" : ""), 13));
            var by = new TextBox { IsReadOnly = ro }; var ti = new TextBox { IsReadOnly = ro }; var li = new TextBox { IsReadOnly = ro };
            System.Windows.Automation.AutomationProperties.SetName(by, "Made by"); System.Windows.Automation.AutomationProperties.SetName(ti, "Title"); System.Windows.Automation.AutomationProperties.SetName(li, "License");
            cr.Children.Add(FieldRow(("Made by", by, 220), ("Title (optional)", ti, 240), ("License (optional)", li, 180))); cr.Children.Add(Buttons(B("Add the Credit", "Btn", () => Change(() => h.AddCredit(by.Text.Trim(), Null(ti.Text), Null(li.Text))), !ro)));
            sp.Children.Add(Section("Credits", cr));

            Show(sp, "design-editor");
        }

        static string Null(string s) { s = s?.Trim(); return string.IsNullOrEmpty(s) ? null : s; }
        static string Cap(string s) => string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s.Substring(1);
        UIElement Section(string title, UIElement body) { var s = new StackPanel(); s.Children.Add(T(title, 15, true, false, new Thickness(0, 0, 0, 12))); s.Children.Add(body); return Card(s, 20); }
        UIElement Labeled(string label, UIElement e) { var s = new StackPanel { Margin = new Thickness(0, 0, 12, 8) }; s.Children.Add(T(label, 12.5, false, true, new Thickness(0, 0, 0, 5))); s.Children.Add(e); return s; }
        /// <summary>a button beside fields, level with their inputs (not stretched to the labels' height)</summary>
        static Button BottomAligned(Button b) { b.VerticalAlignment = VerticalAlignment.Bottom; b.Margin = new Thickness(0, 0, 0, 10); b.MinHeight = 34; return b; }
        Button Small(string text, Action click, bool enabled = true) { var b = B(text, "Btn.Ghost", click, enabled); b.MinWidth = 0; b.MinHeight = 30; b.Padding = new Thickness(10, 4, 10, 4); b.Margin = new Thickness(0, 4, 6, 0); return b; }
        static UIElement Swatch(string hex)
        {
            Brush br = Brushes.Transparent; try { if (hex != null) br = (Brush)new BrushConverter().ConvertFromInvariantString(hex); } catch (FormatException) { }
            return new Border { Width = 22, Height = 22, Background = br, BorderBrush = Brushes.Gray, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(3) };
        }
        static UIElement Thumb(byte[] b, int w, int h)
        {
            try { var bi = new BitmapImage(); bi.BeginInit(); bi.StreamSource = new MemoryStream(b); bi.DecodePixelWidth = w; bi.CacheOption = BitmapCacheOption.OnLoad; bi.EndInit(); bi.Freeze(); return new Image { Source = bi, MaxWidth = w, MaxHeight = h, Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 4, 0, 4) }; }
            catch (Exception) { return new TextBlock { Text = "(this picture cannot be shown)" }; }
        }

        void PickPicture(ImageSlotRule rule, Action<byte[]> use)
        {
            var d = new Microsoft.Win32.OpenFileDialog { Title = "Choose a picture for " + rule.Text + " (PNG or JPEG)", Filter = PictureFilter };
            if (d.ShowDialog(this) != true) return;
            string fit = "cover";
            if (rule.Exact)
            {
                var r = MessageBox.Show(this, "Fill " + rule.Text + " (" + rule.MaxW + " × " + rule.MaxH + ") with the picture, trimming its edges if its shape differs?\n\nYes: fill and trim.  No: show all of it, with bars where needed.", ProductTitle, MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
                if (r == MessageBoxResult.Cancel) return; fit = r == MessageBoxResult.Yes ? "cover" : "contain";
            }
            Change(() => use(ImageNormalizer.Normalize(File.ReadAllBytes(d.FileName), rule, fit)));
        }

        // ------------------------------------------------------------------ a layout sketch (the console's renderer check comes before any apply)
        UIElement DesignPreview(HomeDesign h)
        {
            var cv = new Canvas { Width = DesignProfile.StageW, Height = DesignProfile.StageH, Background = new SolidColorBrush(Color.FromRgb(0x1a, 0x1a, 0x24)), ClipToBounds = true };
            BitmapImage Img(string sha) { if (sha == null || !h.Images.TryGetValue(sha, out var b)) return null; try { var bi = new BitmapImage(); bi.BeginInit(); bi.StreamSource = new MemoryStream(b); bi.CacheOption = BitmapCacheOption.OnLoad; bi.EndInit(); bi.Freeze(); return bi; } catch (Exception) { return null; } }
            void Put(UIElement e, double x, double y) { Canvas.SetLeft(e, x); Canvas.SetTop(e, y); cv.Children.Add(e); }
            var scene = Img(h.ImageFor("scene"));
            if (scene != null) Put(new Image { Source = scene, Width = 1280, Height = 720, Stretch = Stretch.Fill }, 0, 0);
            // without a picture of the person's own, the sketch is plain: its caption names the theme's own background (a label drawn on the
            // sketch sat under the tab bar's outline)
            var plate = Img(h.ImageFor("plate")); if (plate != null) Put(new Image { Source = plate, Stretch = Stretch.None }, 12, 12);
            var ap = h.Document["appearance"] as JObj ?? new JObj();
            Brush Hex(string s, Brush fallback) { try { return s == null ? fallback : (Brush)new BrushConverter().ConvertFromInvariantString(s); } catch (FormatException) { return fallback; } }
            foreach (var r in h.Regions.OrderBy(r => r["layer"] is long l ? l : 0))
            {
                double x = r["x"] is long rx ? rx : 0, y = r["y"] is long ry ? ry : 0, w = r["w"] is long rw ? rw : 0, hh = r["h"] is long rh ? rh : 0; var kind = r["kind"] as string;
                double opac = r["opacity"] is long op ? op / 100.0 : 1;
                if (kind == "ui") continue;   // the HOME's control groups are outlined below, where they will be
                else if (kind == "panel") Put(new Rectangle { Width = w, Height = hh, Fill = Hex(r["color"] as string, Brushes.Black), Opacity = opac, RadiusX = 8, RadiusY = 8 }, x, y);
                else if (kind == "image") { var im = Img(r["image"] as string); if (im != null) Put(new Image { Source = im, Width = w, Height = hh, Stretch = Stretch.Uniform, Opacity = opac }, x, y); }
                else if (kind == "label") Put(new TextBlock { Text = r["text"] as string ?? "", Width = w, Height = hh, Foreground = Hex(r["color"] as string ?? ap["text_color"] as string, Brushes.White), FontSize = Math.Max(DesignProfile.FontPx(r["font"] as string), 13), Opacity = opac, TextAlignment = r["align"] as string == "center" ? TextAlignment.Center : r["align"] as string == "right" ? TextAlignment.Right : TextAlignment.Left }, x, y);
            }
            // where the HOME's own controls will be: the theme's groups (moved or in their place) and the controls that stay
            var placed = h.ControlRegions;
            foreach (var f in DesignProfile.FixedControls(h.BaseTheme))
                Put(new Border { Width = f.box[2] - f.box[0], Height = f.box[3] - f.box[1], Background = new SolidColorBrush(Color.FromArgb(70, 128, 128, 128)), BorderBrush = Brushes.Gray, BorderThickness = new Thickness(2), Child = new TextBlock { Text = Cap(f.name) + " (stays)", Foreground = Brushes.White, FontSize = 20, Margin = new Thickness(8, 4, 8, 4) } }, f.box[0], f.box[1]);
            foreach (var ui in DesignProfile.UiOrder)
            {
                var b = DesignProfile.RegionBox(h.BaseTheme, ui); if (b == null) continue;
                double x = b[0], y = b[1]; if (placed.TryGetValue(ui, out var r) && r["x"] is long rx && r["y"] is long ry) { x = rx; y = ry; }
                Put(new Border { Width = b[2] - b[0], Height = b[3] - b[1], BorderBrush = Hex(ap["accent_color"] as string, Brushes.Red), BorderThickness = new Thickness(3), Background = new SolidColorBrush(Color.FromArgb(40, 0, 0, 0)),
                                 Child = new TextBlock { Text = Cap(DesignProfile.UiName(ui)), Foreground = Brushes.White, FontSize = 20, Margin = new Thickness(8, 4, 8, 4) } }, x, y);
            }
            var edge = new Rectangle { Width = 1280 - 24, Height = 720 - 24, Stroke = new SolidColorBrush(Color.FromArgb(90, 255, 255, 255)), StrokeDashArray = new DoubleCollection { 6, 6 }, StrokeThickness = 2 };
            Put(edge, 12, 12);
            var sp = new StackPanel();
            sp.Children.Add(T("Layout sketch", 13.5, true, false, new Thickness(0, 0, 0, scene == null ? 2 : 6)));
            if (scene == null) sp.Children.Add(T("Background: " + ThemeName(h.BaseTheme) + "'s own picture (no picture of yours yet)", Ty.Small, false, true, new Thickness(0, 0, 0, 6)));
            sp.Children.Add(new Border { Child = new Viewbox { Child = cv, Stretch = Stretch.Uniform, StretchDirection = StretchDirection.DownOnly }, MaxWidth = 720, HorizontalAlignment = HorizontalAlignment.Left, BorderBrush = Res<Brush>("B.Line"), BorderThickness = new Thickness(1) });
            sp.Children.Add(T("A sketch, not the console's drawing: your pictures, panels and labels, the outlines of the HOME's controls where they will be, and the controls that stay in place. The dashed line is the screen's safe area. The preview below is drawn by the console's own HOME program.", 12, false, true, new Thickness(0, 6, 0, 0)));
            sp.Children.Add(DesignRealPreview(h));
            return sp;
        }

        UIElement LayoutEditor(HomeDesign h, bool ro)
        {
            var sp = new StackPanel();
            sp.Children.Add(T("The HOME's own controls", 13.5, true, false, new Thickness(0, 0, 0, 4)));
            sp.Children.Add(T("In " + ThemeName(h.BaseTheme) + " you can move these groups of the HOME's controls. Each group moves whole and keeps the theme's size: only its place changes, inside the screen's safe area. Moving a group needs your own HOME background, because each theme's own background has its controls' panels painted in place.", 12.5, false, true));
            sp.Children.Add(Details("Technical details", "The safe area is " + DesignProfile.Margin + " px from every edge of the " + DesignProfile.StageW + " × " + DesignProfile.StageH + " screen. A group never covers another group or a control that stays in place, and the controller still reaches it the same way: the game list under the navigation, the button hints under the game list."));
            if (h.ImageFor("scene") == null) sp.Children.Add(Note("Choose your own HOME background (Pictures) to move the HOME's controls.", "info"));
            var placed = h.ControlRegions;
            foreach (var ui in DesignProfile.UiOrder)
            {
                var box = DesignProfile.RegionBox(h.BaseTheme, ui);
                if (box == null)
                {
                    if (placed.TryGetValue(ui, out var gone)) { var gid = gone["id"] as string; var gr = new WrapPanel { Margin = new Thickness(0, 6, 0, 2) }; gr.Children.Add(T(Cap(DesignProfile.UiName(ui)) + ": " + DesignProfile.RegionUnavailable(h.BaseTheme, ui) + ".", 12.5, false, true, new Thickness(0, 4, 10, 0))); gr.Children.Add(Small("Remove", () => Change(() => h.RemoveRegion(gid)), !ro)); sp.Children.Add(gr); }
                    else sp.Children.Add(T(Cap(DesignProfile.UiName(ui)) + ": " + DesignProfile.RegionUnavailable(h.BaseTheme, ui) + ".", 12, false, true, new Thickness(0, 6, 0, 2)));
                    continue;
                }
                int w = box[2] - box[0], hh = box[3] - box[1]; var mine = placed.TryGetValue(ui, out var r) ? r : null;
                long x = mine?["x"] is long mx ? mx : box[0], y = mine?["y"] is long my ? my : box[1];
                var g = new StackPanel { Margin = new Thickness(0, 10, 0, 0) };
                g.Children.Add(T(Cap(DesignProfile.UiName(ui)) + (mine != null && (x != box[0] || y != box[1]) ? "  ·  moved" : "  ·  the theme's place"), 13, true));
                g.Children.Add(T(w + " × " + hh + " (the theme's size). Its top-left corner can be from x " + DesignProfile.Margin + " to " + (DesignProfile.StageW - DesignProfile.Margin - w) + " and y " + DesignProfile.Margin + " to " + (DesignProfile.StageH - DesignProfile.Margin - hh) + "; the theme puts it at " + box[0] + ", " + box[1] + ".", 12, false, true, new Thickness(0, 2, 0, 2)));
                var xb = new TextBox { Text = x.ToString(En), IsReadOnly = ro }; var yb = new TextBox { Text = y.ToString(En), IsReadOnly = ro };
                System.Windows.Automation.AutomationProperties.SetName(xb, DesignProfile.UiName(ui) + " x"); System.Windows.Automation.AutomationProperties.SetName(yb, DesignProfile.UiName(ui) + " y");
                var row = FieldRow(("x (left)", xb, 110), ("y (top)", yb, 110)); var u = ui;
                row.Children.Add(BottomAligned(Small("Move", () => Change(() =>
                {
                    if (!long.TryParse(xb.Text.Trim(), System.Globalization.NumberStyles.Integer, En, out var nx) || !long.TryParse(yb.Text.Trim(), System.Globalization.NumberStyles.Integer, En, out var ny)) throw new ComposeException("design.type", "Use whole numbers for the place.");
                    h.PlaceRegion(u, nx, ny);
                }), !ro)));
                if (mine != null) { var id = mine["id"] as string; row.Children.Add(BottomAligned(Small("Back to the Theme's Place", () => Change(() => h.RemoveRegion(id)), !ro))); }
                g.Children.Add(row); sp.Children.Add(g);
            }
            sp.Children.Add(T("Panels, labels and pictures", 13.5, true, false, new Thickness(0, 16, 0, 4)));
            sp.Children.Add(T("Your own panels, labels and pictures stay inside the screen's safe area and are drawn under the HOME's controls.", 12.5, false, true));
            var regs = h.Regions;
            foreach (var r in regs.Where(x => x["kind"] as string != "ui"))
            {
                var row = new WrapPanel { Margin = new Thickness(0, 4, 0, 4) };
                var k = r["kind"] as string; row.Children.Add(new TextBlock { Text = k == "panel" ? "Panel" : k == "label" ? "Label: " + Display.Line(r["text"] as string, 24) : k == "image" ? "Picture" : "Other (" + k + ")", Width = 150, FontSize = 13, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 4, 8, 0) });
                if (k == "panel" || k == "label" || k == "image") AddRect(row, h, r, ro); else row.Children.Add(T("kept as it is", 12.5, false, true));
                sp.Children.Add(row);
            }
            string NewId(string kind) { int n = 1; while (regs.Any(x => x["id"] as string == kind + "-" + n)) n++; return kind + "-" + n; }
            var lt = new TextBox { IsReadOnly = ro }; System.Windows.Automation.AutomationProperties.SetName(lt, "Label text");
            var add = FieldRow(("New label's text", lt, 240)); add.Margin = new Thickness(0, 10, 0, 0);
            add.Children.Insert(0, BottomAligned(Small("Add a Panel", () => Change(() => h.UpsertRegion(NewId("panel"), x => { x["kind"] = "panel"; x["x"] = 40L; x["y"] = 40L; x["w"] = 300L; x["h"] = 120L; x["layer"] = 1L; x["opacity"] = 80L; x["color"] = "#101018"; })), !ro)));
            ((FrameworkElement)add.Children[0]).Margin = new Thickness(0, 0, 12, 10);
            add.Children.Add(BottomAligned(Small("Add a Label", () => Change(() => h.UpsertRegion(NewId("label"), x => { x["kind"] = "label"; x["x"] = 40L; x["y"] = 40L; x["w"] = 400L; x["h"] = 40L; x["layer"] = 2L; x["text"] = lt.Text.Trim(); x["font"] = "sans700_20"; })), !ro)));
            ((FrameworkElement)add.Children[add.Children.Count - 1]).Margin = new Thickness(0, 0, 6, 10);
            add.Children.Add(BottomAligned(Small("Add a Picture…", () => PickPicture(DesignProfile.Slots["decoration"], bytes => { var sha = h.AddImage(bytes); h.UpsertRegion(NewId("picture"), x => { x["kind"] = "image"; x["x"] = 40L; x["y"] = 40L; x["w"] = 300L; x["h"] = 200L; x["layer"] = 1L; x["image"] = sha; }); }), !ro)));
            sp.Children.Add(add);
            return sp;
        }

        void AddRect(WrapPanel row, HomeDesign h, JObj r, bool ro)
        {
            var id = r["id"] as string; var f = new Dictionary<string, TextBox>();
            foreach (var k in new[] { "x", "y", "w", "h" })
            { var tb = new TextBox { Text = (r[k] is long v ? v : 0).ToString(En), Width = 64, IsReadOnly = ro, Margin = new Thickness(4, 4, 10, 0) }; System.Windows.Automation.AutomationProperties.SetName(tb, id + " " + k); f[k] = tb; row.Children.Add(new TextBlock { Text = k, VerticalAlignment = VerticalAlignment.Center, FontSize = 12, Foreground = Res<Brush>("B.Muted"), Margin = new Thickness(0, 4, 0, 0) }); row.Children.Add(tb); }
            row.Children.Add(Small("Apply", () => Change(() =>
            {
                var vals = new Dictionary<string, long>();
                foreach (var kv in f) { if (!long.TryParse(kv.Value.Text.Trim(), System.Globalization.NumberStyles.Integer, En, out var n)) throw new ComposeException("design.type", "Use whole numbers for the region's position and size."); vals[kv.Key] = n; }
                h.UpsertRegion(id, x => { foreach (var kv in vals) x[kv.Key] = kv.Value; });
            }), !ro));
            row.Children.Add(Small(r["kind"] as string == "ui" ? "Back to the Theme's" : "Remove", () => Change(() => h.RemoveRegion(id)), !ro));
        }

        UIElement FolderEditor(HomeDesign h, bool ro)
        {
            var sp = new StackPanel(); var folders = h.Folders;
            var byParent = folders.GroupBy(f => f["parent"] as string ?? "").ToDictionary(x => x.Key, x => x.OrderBy(f => f["pos"] is long p ? p : 0).ThenBy(f => f["name"] as string, StringComparer.CurrentCultureIgnoreCase).ToList());
            void Level(string parent, int depth)
            {
                if (!byParent.TryGetValue(parent, out var list)) return;
                foreach (var f in list)
                {
                    var id = f["id"] as string; var box = new StackPanel { Margin = new Thickness(depth * 22, 4, 0, 6) };
                    var games = (f["games"] as List<object> ?? new List<object>()).Count;
                    var line = new DockPanel();
                    if (f["art"] is string art && h.Images.TryGetValue(art, out var ab)) { var th = Thumb(ab, 110, 30); DockPanel.SetDock(th, Dock.Right); line.Children.Add(th); }
                    line.Children.Add(new TextBlock { Text = Display.Line(f["name"] as string, 40) + "  (" + games + " game" + (games == 1 ? "" : "s") + ")", FontSize = 13.5, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center });
                    box.Children.Add(line);
                    var row = new WrapPanel();
                    var nm = new TextBox { Text = f["name"] as string, Width = 180, IsReadOnly = ro, Margin = new Thickness(0, 4, 6, 0) }; System.Windows.Automation.AutomationProperties.SetName(nm, "Folder name");
                    row.Children.Add(nm); row.Children.Add(Small("Rename", () => Change(() => h.UpsertFolder(id, x => x["name"] = nm.Text.Trim())), !ro));
                    row.Children.Add(Small("Picture…", () => PickPicture(DesignProfile.Slots["folder-art"], bytes => { var sha = h.AddImage(bytes); h.UpsertFolder(id, x => x["art"] = sha); }), !ro));
                    bool hasChildren = byParent.ContainsKey(id);
                    row.Children.Add(Small("Remove", () => Change(() => h.RemoveFolder(id)), !ro && !hasChildren));
                    box.Children.Add(row); sp.Children.Add(box);
                    Level(id, depth + 1);
                }
            }
            Level("", 0);
            if (folders.Count == 0) sp.Children.Add(T("No folders: games are listed in the game order below.", 12.5, false, true));
            var nameBox = new TextBox { IsReadOnly = ro }; System.Windows.Automation.AutomationProperties.SetName(nameBox, "New folder name");
            var parentBox = new ComboBox { IsEnabled = !ro }; parentBox.Items.Add(new ComboBoxItem { Content = "(top level)", Tag = null, IsSelected = true });
            foreach (var f in folders) parentBox.Items.Add(new ComboBoxItem { Content = "inside " + Display.Line(f["name"] as string, 40), Tag = f["id"] as string });
            sp.Children.Add(T("Add a folder", 13.5, true, false, new Thickness(0, 12, 0, 8)));
            var addRow = FieldRow(("New folder", nameBox, 220), ("Where", parentBox, 220));
            addRow.Children.Add(BottomAligned(Small("Add the Folder", () => Change(() =>
            {
                var n = nameBox.Text.Trim(); if (n.Length == 0) throw new ComposeException("design.type", "Give the folder a name.");
                var slug = new string(n.ToLowerInvariant().Select(ch => ch >= 'a' && ch <= 'z' || ch >= '0' && ch <= '9' ? ch : '-').ToArray()).Trim('-'); if (slug.Length == 0) slug = "folder"; if (slug.Length > 32) slug = slug.Substring(0, 32).Trim('-');
                var id = slug; int k = 2; while (folders.Any(f => f["id"] as string == id)) id = slug + "-" + k++;
                var parent = (parentBox.SelectedItem as ComboBoxItem)?.Tag as string;
                h.UpsertFolder(id, x => { x["name"] = n; if (parent != null) x["parent"] = parent; x["pos"] = (long)folders.Count(f => (f["parent"] as string) == parent); x["games"] = new List<object>(); });
            }), !ro)));
            sp.Children.Add(addRow);

            // game order (games are named by their public catalog entries or as numbered placeholders; no game files)
            var order = h.Order;
            sp.Children.Add(T("Game order" + (order.Count == 0 ? ": the HOME's own order" : " (" + order.Count + ")"), 13.5, true, false, new Thickness(0, 14, 0, 4)));
            for (int i = 0; i < Math.Min(order.Count, 300); i++)
            {
                var line = new Grid { Margin = new Thickness(0, 0, 0, 2) }; line.ColumnDefinitions.Add(new ColumnDefinition()); line.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                line.Children.Add(new TextBlock { Text = (i + 1) + ". " + Display.Line(order[i].DisplayName, 60), FontSize = 13, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 4, 8, 0) });
                var row = new StackPanel { Orientation = Orientation.Horizontal }; Grid.SetColumn(row, 1); line.Children.Add(row); int at = i;
                row.Children.Add(Small("Up", () => Change(() => { var l = h.Order; var x = l[at]; l.RemoveAt(at); l.Insert(at - 1, x); h.SetOrder(l); }), !ro && i > 0));
                row.Children.Add(Small("Down", () => Change(() => { var l = h.Order; var x = l[at]; l.RemoveAt(at); l.Insert(at + 1, x); h.SetOrder(l); }), !ro && i < order.Count - 1));
                row.Children.Add(Small("Take Out", () => Change(() => { var l = h.Order; l.RemoveAt(at); h.SetOrder(l); }), !ro));
                sp.Children.Add(line);
            }
            if (order.Count > 300) sp.Children.Add(T("…and " + (order.Count - 300) + " more.", 12.5, false, true));
            return sp;
        }
    }
}
