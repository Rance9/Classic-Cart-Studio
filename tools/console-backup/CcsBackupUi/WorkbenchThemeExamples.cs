// Classic Cart Studio console backup and restore window. Copyright (C) 2026 Rance9. GPL-3.0-or-later.
// HOME Designer's starting-theme examples: beside the Starting theme choice (when a design starts, and in the editor), a picture of that
// theme as the console HOME draws it, filled with games; a click, a tap, Enter or Space opens it larger in its own window. The pictures and
// their names and descriptions come from theme-examples/theme-examples.json (carried in this library): replacing a picture is a new file and
// a changed entry there, with no change here. An example is never the person's design and never a passed render or memory check: the
// editor's own preview (WorkbenchDesignerPreview.cs) and design check stay separate. Offline: no console, backup, network or worker.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace CcsBackup.Ui
{
    public sealed partial class BackupWorkspace
    {
        sealed class ThemeExample { public string Theme, Name, File, Description; }
        static List<ThemeExample> themeExamples;

        /// <summary>the examples the manifest lists (empty when this copy carries none)</summary>
        static List<ThemeExample> ThemeExamples()
        {
            if (themeExamples != null) return themeExamples;
            var l = new List<ThemeExample>();
            try
            {
                var info = Application.GetResourceStream(new Uri("pack://application:,,,/CcsBackupUi;component/theme-examples/theme-examples.json", UriKind.Absolute));
                if (info != null)
                    using (var r = new StreamReader(info.Stream))
                        foreach (var e in Json.Parse(r.ReadToEnd()).A("examples")?.OfType<Dictionary<string, object>>() ?? Enumerable.Empty<Dictionary<string, object>>())
                            l.Add(new ThemeExample { Theme = e.S("theme"), Name = e.S("name"), File = e.S("file"), Description = e.S("description") });
            }
            catch (Exception ex) when (ex is IOException || ex is FormatException || ex is InvalidDataException || ex is ArgumentException) { }
            return themeExamples = l;
        }

        static readonly Dictionary<string, BitmapSource> themeThumbs = new Dictionary<string, BitmapSource>();
        /// <summary>an example picture: small (decoded at 640 pixels wide, kept) for the thumbnail, or whole (decoded each time) for the larger view</summary>
        static BitmapSource ThemePicture(ThemeExample x, bool small)
        {
            if (x?.File == null) return null;
            if (small && themeThumbs.TryGetValue(x.Theme, out var t)) return t;
            try
            {
                var info = Application.GetResourceStream(new Uri("pack://application:,,,/CcsBackupUi;component/theme-examples/" + x.File, UriKind.Absolute));
                if (info == null) return null;
                var b = new BitmapImage(); b.BeginInit(); b.StreamSource = info.Stream; b.CacheOption = BitmapCacheOption.OnLoad; if (small) b.DecodePixelWidth = 640; b.EndInit(); b.Freeze();
                if (small) themeThumbs[x.Theme] = b;
                return b;
            }
            catch (Exception ex) when (ex is IOException || ex is NotSupportedException || ex is InvalidOperationException) { return null; }
        }

        /// <summary>the example of the theme a choice list has chosen: a thumbnail that follows the choice and opens larger</summary>
        FrameworkElement ThemeExampleFor(ComboBox choice)
        {
            var sp = new StackPanel { Margin = new Thickness(0, 2, 0, 10) };
            var img = new Image { Width = 320, Height = 180, Stretch = Stretch.Uniform }; RenderOptions.SetBitmapScalingMode(img, BitmapScalingMode.HighQuality);
            var frame = new Border { BorderBrush = Res<Brush>("B.Line2"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Child = img, Background = Res<Brush>("B.Surface2") };
            var b = new Button { Content = frame, Style = Res<Style>("Wb.Plain"), HorizontalAlignment = HorizontalAlignment.Left };
            var caption = new TextBlock { FontSize = Ty.Small, Foreground = Res<Brush>("B.Muted"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 0), MaxWidth = 520, HorizontalAlignment = HorizontalAlignment.Left };
            sp.Children.Add(b); sp.Children.Add(caption);
            ThemeExample cur = null;
            void Refresh()
            {
                var theme = (choice.SelectedItem as ComboBoxItem)?.Tag as string;
                cur = ThemeExamples().FirstOrDefault(x => x.Theme == theme);
                var src = ThemePicture(cur, true);
                img.Source = src; b.IsEnabled = src != null;
                var name = cur?.Name ?? ThemeName(theme);
                caption.Text = src != null ? "An example of " + name + ", filled with games. Select the picture to see it larger. It shows the theme, not your design." : "No example picture of " + name + " in this copy.";
                AutomationProperties.SetName(b, "Example of the " + name + " theme. Select to see it larger.");
                AutomationProperties.SetHelpText(b, cur?.Description ?? "");
            }
            b.Click += (s, e) =>
            {
                var big = ThemePicture(cur, false); if (big == null) return;
                ShowPicture(big, cur.Name + ": an example", cur.Description,
                    "An example of the " + cur.Name + " theme with the NES Classic's 30 built-in games. It is not your design, and it is not a check of your design: the editor's own preview and the design check are separate.");
                b.Focus();   // back to the picture that opened it
            };
            choice.SelectionChanged += (s, e) => Refresh();
            Refresh();
            return sp;
        }
    }
}
