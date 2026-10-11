// Base for the native dialogs (About, Diagnostics, messages): same dark chrome as the main window, a compact custom title bar with
// a close button, Escape closes, centered on the owner. Dialogs are modal and never larger than the owner's screen.
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shell;

namespace ClassicCartStudio
{
    internal class DialogWindow : Window
    {
        public readonly Grid Body = new Grid();

        public DialogWindow(Window owner, string title, double width, double height)
        {
            Owner = owner; Title = title; Width = width; Height = height; MinWidth = 360; MinHeight = 200;
            WindowStartupLocation = owner != null ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen;
            ShowInTaskbar = false; ResizeMode = ResizeMode.CanResize; Background = (Brush)Application.Current.FindResource("B.Bg");
            UseLayoutRounding = true; SnapsToDevicePixels = true; TextOptions.SetTextFormattingMode(this, TextFormattingMode.Ideal);
            try { Icon = owner?.Icon; } catch { }
            WindowChrome.SetWindowChrome(this, new WindowChrome { CaptionHeight = 34, GlassFrameThickness = new Thickness(0, 0, 0, 1), ResizeBorderThickness = new Thickness(6), UseAeroCaptionButtons = false, CornerRadius = new CornerRadius(0) });
            var root = new Grid(); root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(34) }); root.RowDefinitions.Add(new RowDefinition());
            var bar = new Grid { Background = (Brush)Application.Current.FindResource("B.Bar") };
            bar.ColumnDefinitions.Add(new ColumnDefinition()); bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var t = new TextBlock { Text = title, FontSize = 12.5, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(14, 0, 0, 1) };
            var close = new Button { Content = "", Style = (Style)Application.Current.FindResource("Caption.Close"), Height = 34, ToolTip = "Close" };
            close.Click += (s, e) => Close(); Grid.SetColumn(close, 1);
            bar.Children.Add(t); bar.Children.Add(close);
            root.Children.Add(bar);
            root.Children.Add(new Border { Height = 1, VerticalAlignment = VerticalAlignment.Bottom, Background = (Brush)Application.Current.FindResource("B.Line") });
            Grid.SetRow(Body, 1); Body.Margin = new Thickness(22, 18, 22, 18); root.Children.Add(Body);
            Content = root;
            SourceInitialized += (s, e) => { Native.ApplyDarkChrome(this, 0x00202024); };
            PreviewKeyDown += (s, e) => { if (e.Key == Key.Escape) { Close(); e.Handled = true; } };
            Loaded += (s, e) => { if (owner != null) { MaxWidth = Math.Max(MinWidth, SystemParameters.WorkArea.Width - 40); MaxHeight = Math.Max(MinHeight, SystemParameters.WorkArea.Height - 40); } };
        }

        protected static TextBlock Text(string s, string style = null, double? size = null, Thickness? margin = null)
        {
            var t = new TextBlock { Text = s, TextWrapping = TextWrapping.Wrap };
            if (style != null) t.Style = (Style)Application.Current.FindResource(style);
            if (size != null) t.FontSize = size.Value;
            if (margin != null) t.Margin = margin.Value;
            return t;
        }
        protected static Button Btn(string text, string style, RoutedEventHandler click)
        {
            var b = new Button { Content = text, Style = (Style)Application.Current.FindResource(style), Margin = new Thickness(8, 0, 0, 0) };
            b.Click += click; return b;
        }
        protected static Button Link(string text, string url)
        {
            var b = new Button { Content = text, Style = (Style)Application.Current.FindResource("Btn.Link"), ToolTip = url, HorizontalAlignment = HorizontalAlignment.Left };
            b.Click += (s, e) => App.OpenExternal(url); return b;
        }
    }

    // A small modal message with up to two choices (never a raw exception dump; details are optional and collapsed).
    internal static class MessageDialog
    {
        public static bool Show(Window owner, string title, string message, string details = null, string primary = "OK", string secondary = null)
        {
            bool result = false;
            var d = new DialogWindow(owner, title, 520, 260) { ResizeMode = ResizeMode.NoResize, SizeToContent = SizeToContent.Height };
            var stack = new StackPanel();
            stack.Children.Add(DialogText(message));
            TextBox detailBox = null;
            if (!string.IsNullOrEmpty(details))
            {
                var toggle = new Button { Content = "Details", Style = (Style)Application.Current.FindResource("Btn.Link"), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 10, 0, 0) };
                detailBox = new TextBox { Text = details, IsReadOnly = true, TextWrapping = TextWrapping.Wrap, MaxHeight = 140, Margin = new Thickness(0, 8, 0, 0), Visibility = Visibility.Collapsed, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
                toggle.Click += (s, e) => detailBox.Visibility = detailBox.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
                stack.Children.Add(toggle); stack.Children.Add(detailBox);
            }
            var acts = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 18, 0, 0) };
            if (secondary != null) { var b2 = new Button { Content = secondary, Style = (Style)Application.Current.FindResource("Btn"), Margin = new Thickness(8, 0, 0, 0) }; b2.Click += (s, e) => { result = false; d.Close(); }; acts.Children.Add(b2); }
            var b1 = new Button { Content = primary, Style = (Style)Application.Current.FindResource("Btn.Primary"), Margin = new Thickness(8, 0, 0, 0), IsDefault = true }; b1.Click += (s, e) => { result = true; d.Close(); }; acts.Children.Add(b1);
            stack.Children.Add(acts);
            d.Body.Children.Add(stack);
            d.Loaded += (s, e) => b1.Focus();
            d.ShowDialog();
            return result;
        }
        static TextBlock DialogText(string s) { return new TextBlock { Text = s, TextWrapping = TextWrapping.Wrap, FontSize = 13.5, LineHeight = 20 }; }
    }
}
