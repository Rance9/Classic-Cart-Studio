// Diagnostics: plain-language status of the application, the WebView2 runtime, the backend and the website, the data folder and
// log, the raw backend answers (no tokens: the backend never echoes the session token), and the actions that fix common problems.
using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ClassicCartStudio
{
    internal class DiagnosticsWindow : DialogWindow
    {
        readonly MainWindow main; readonly StackPanel rows = new StackPanel(); readonly TextBox raw = new TextBox { IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.NoWrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto };
        string report = "";

        public DiagnosticsWindow(MainWindow owner) : base(owner, "Diagnostics", 760, 600)
        {
            main = owner;
            var grid = new Grid(); grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); grid.RowDefinitions.Add(new RowDefinition()); grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.Children.Add(rows);
            var rawBox = new Border { Margin = new Thickness(0, 12, 0, 0), BorderBrush = (Brush)Application.Current.FindResource("B.Line"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Background = new SolidColorBrush(Color.FromRgb(11, 11, 14)) };
            raw.BorderThickness = new Thickness(0); raw.Background = Brushes.Transparent; rawBox.Child = raw; Grid.SetRow(rawBox, 1); grid.Children.Add(rawBox);
            var acts = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) }; Grid.SetRow(acts, 2);
            acts.Children.Add(Btn("Restart backend", "Btn.Ghost", (s, e) => { main.RestartBackend(); Refresh(); }));
            acts.Children.Add(Btn("Open log", "Btn.Ghost", (s, e) => { try { if (File.Exists(App.LogPath)) System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(App.LogPath) { UseShellExecute = true }); } catch { } }));
            acts.Children.Add(Btn("Open data folder", "Btn.Ghost", (s, e) => { try { Directory.CreateDirectory(App.DataRoot); System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", "\"" + App.DataRoot + "\"") { UseShellExecute = true }); } catch { } }));
            acts.Children.Add(Btn("Copy report", "Btn.Ghost", (s, e) => { try { Clipboard.SetText(report); } catch { } }));
            acts.Children.Add(Btn("Refresh", "Btn", (s, e) => Refresh()));
            var close = Btn("Close", "Btn.Primary", (s, e) => Close()); close.IsDefault = true; acts.Children.Add(close);
            grid.Children.Add(acts);
            Body.Children.Add(grid);
            Refresh();
        }

        void Refresh()
        {
            rows.Children.Clear();
            var b = main.backend; var ok = b.Ready && b.Process != null && !b.Process.HasExited;
            var ver = ok ? (b.Call("GET", "/api/version", null, 3000) ?? "") : "";
            var st = ok ? (b.Call("GET", "/api/status", null, 8000) ?? "") : "";
            var desktop = ver.Contains("\"desktop\": true") || ver.Contains("\"desktop\":true");
            var proto = Regex.Match(ver, "\"protocol\"\\s*:\\s*(\\d+)"); var caps = Regex.Match(ver, "\"capabilities\"\\s*:\\s*\\[([^\\]]*)\\]");
            var runtime = main.WebViewRuntime();
            Add("Application", "Classic Cart Studio " + App.Version, Good);
            Add("WebView2 runtime", runtime == "not found" ? "not found (install or repair Microsoft Edge)" : "version " + runtime, runtime == "not found" ? Bad : Good);
            Add("Interface", "bundled, " + App.StartUrl + " — " + (main.WebsiteShown ? "loaded" : "not loaded") + " (" + main.LastNavigation + ")", main.WebsiteShown ? Good : Warn);
            Add("Backend", ok ? ("running, version " + b.BackendVersion + " on 127.0.0.1:" + b.Port + " (pid " + b.Process.Id + ")") : ("not running" + (string.IsNullOrEmpty(b.LastError) ? "" : ": " + b.LastError)), ok ? Good : Bad);
            Add("Compatibility", ok ? (desktop ? "the backend accepts this application's session" : "the backend did not accept the desktop session") + (proto.Success ? " · protocol " + proto.Groups[1].Value : "") + (caps.Success ? " · " + caps.Groups[1].Value.Replace("\"", "").Replace(" ", "") : "") : "unknown until the backend runs", ok && desktop ? Good : Warn);
            Add("Data folder", App.DataRoot, Neutral);
            Add("Log file", App.LogPath, Neutral);
            var tail = ""; try { if (File.Exists(App.LogPath)) { var lines = File.ReadAllLines(App.LogPath); var n = Math.Max(0, lines.Length - 12); tail = string.Join("\n", lines, n, lines.Length - n); } } catch { }
            raw.Text = "/api/version\n" + Trim(ver, 1200) + "\n\n/api/status\n" + Trim(st, 2400) + "\n\nlog (last lines)\n" + tail;
            report = "Classic Cart Studio " + App.Version + "\nWebView2 runtime: " + runtime + "\nInterface (bundled): " + App.StartUrl + " (" + main.LastNavigation + ")\nBackend: " + (ok ? b.BackendVersion + " on 127.0.0.1:" + b.Port : "not running " + b.LastError) + "\nData folder: " + App.DataRoot + "\n\n" + raw.Text;
        }

        static readonly Color Good = Color.FromRgb(52, 209, 110), Warn = Color.FromRgb(240, 160, 48), Bad = Color.FromRgb(255, 90, 90), Neutral = Color.FromRgb(156, 154, 149);
        void Add(string label, string value, Color dot)
        {
            var g = new Grid { Margin = new Thickness(0, 0, 0, 7) }; g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(14) }); g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(126) }); g.ColumnDefinitions.Add(new ColumnDefinition());
            g.Children.Add(new Border { Width = 8, Height = 8, CornerRadius = new CornerRadius(4), Background = new SolidColorBrush(dot), VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 1, 0, 0) });
            var l = Text(label, "T.Label"); Grid.SetColumn(l, 1); l.VerticalAlignment = VerticalAlignment.Center; g.Children.Add(l);
            var v = Text(value, null, 12.5); Grid.SetColumn(v, 2); v.TextWrapping = TextWrapping.Wrap; g.Children.Add(v);
            rows.Children.Add(g);
        }
        static string Trim(string s, int n) { s = s ?? ""; return s.Length > n ? s.Substring(0, n) + "…" : s; }
    }
}
