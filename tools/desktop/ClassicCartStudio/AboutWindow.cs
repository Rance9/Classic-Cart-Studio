// About: compact, branded, with the version, the links that matter and the independent-project disclaimer.
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ClassicCartStudio
{
    internal class AboutWindow : DialogWindow
    {
        public AboutWindow(MainWindow owner) : base(owner, "About Classic Cart Studio", 520, 430)
        {
            ResizeMode = ResizeMode.NoResize; SizeToContent = SizeToContent.Height;
            var stack = new StackPanel();
            var head = new Grid(); head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto }); head.ColumnDefinitions.Add(new ColumnDefinition());
            var icon = new Image { Width = 56, Height = 56, Margin = new Thickness(0, 0, 16, 0), VerticalAlignment = VerticalAlignment.Top };
            try { icon.Source = new BitmapImage(new System.Uri("pack://application:,,,/app.ico")); RenderOptions.SetBitmapScalingMode(icon, BitmapScalingMode.HighQuality); } catch { }
            head.Children.Add(icon);
            var titles = new StackPanel(); Grid.SetColumn(titles, 1);
            var t = new TextBlock { Style = (Style)Application.Current.FindResource("T.Title"), Margin = new Thickness(0, 2, 0, 0) };
            t.Inlines.Add(new System.Windows.Documents.Run("Classic Cart "));
            t.Inlines.Add(new System.Windows.Documents.Run("Studio") { Foreground = (Brush)Application.Current.FindResource("B.Red3") });
            titles.Children.Add(t);
            titles.Children.Add(Text("for Windows · version " + App.Version, "T.Muted", 13, new Thickness(0, 2, 0, 0)));
            var backend = owner.backend.Ready ? ("Backend " + owner.backend.BackendVersion + " on 127.0.0.1:" + owner.backend.Port) : "Backend not running";
            titles.Children.Add(Text(backend + " · WebView2 " + owner.WebViewRuntime(), "T.Muted", 12, new Thickness(0, 2, 0, 0)));
            head.Children.Add(titles);
            stack.Children.Add(head);
            stack.Children.Add(Text("An independent game collection manager for the Nintendo NES Classic Edition. The application shows the live Studio website in its own window and adds everything that needs your computer.", null, 13, new Thickness(0, 16, 0, 0)));

            var links = new StackPanel { Margin = new Thickness(0, 14, 0, 0) };
            links.Children.Add(Row("Website", Link("ClassicCartStudio.com", "https://ClassicCartStudio.com/")));
            links.Children.Add(Row("Download", Link("ClassicCartStudio.com/app", App.UpdateUrl)));
            links.Children.Add(Row("Source code", Link("github.com/Rance9/Classic-Cart-Studio", App.RepoUrl)));
            links.Children.Add(Row("License", Link("MIT License · third-party notices", App.RepoUrl + "/blob/main/THIRD_PARTY_NOTICES.md")));
            stack.Children.Add(links);

            stack.Children.Add(Text("Classic Cart Studio is an independent, open-source project and is not produced, licensed, sponsored, approved, or endorsed by Nintendo. Nintendo, Nintendo Entertainment System, NES and NES Classic Edition are trademarks of Nintendo. No game files or console software are distributed with this software.", "T.Muted", 11.5, new Thickness(0, 16, 0, 0)));

            var acts = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 18, 0, 0) };
            acts.Children.Add(Btn("Release notes", "Btn.Ghost", (s, e) => App.OpenExternal(App.RepoUrl + "/releases")));
            var close = Btn("Close", "Btn", (s, e) => Close()); close.IsDefault = true; acts.Children.Add(close);
            stack.Children.Add(acts);
            Body.Children.Add(stack);
            Loaded += (s, e) => close.Focus();
        }

        static Grid Row(string label, UIElement value)
        {
            var g = new Grid { Margin = new Thickness(0, 0, 0, 6) }; g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(96) }); g.ColumnDefinitions.Add(new ColumnDefinition());
            var l = Text(label, "T.Label"); g.Children.Add(l); Grid.SetColumn(value, 1); g.Children.Add(value); return g;
        }
    }
}
