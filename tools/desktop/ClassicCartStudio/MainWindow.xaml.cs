// The one application window: custom dark title bar (Studio menu, caption buttons with Snap Layouts), WebView2 showing the live
// Studio website, a branded loading state, the recovery screen when the website cannot load, the desktop token injection for the
// approved origin only, navigation filtering (anything else opens in the user's browser) and the nesstudio:// link handling.
using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;

namespace ClassicCartStudio
{
    public partial class MainWindow : Window
    {
        internal readonly Backend backend = new Backend();
        readonly Settings settings = Settings.Load();
        readonly string[] startupArgs;
        bool coreReady, backendStarting, closing, recoveryShown, firstPageShown;
        string pendingLink;
        int devtoolsPort;
        internal string LastNavigation = "not yet";
        DispatcherTimer blockTimer; int blockStep; bool hitLogged;

        public MainWindow(string[] args)
        {
            startupArgs = args ?? new string[0];
            foreach (var a in startupArgs) { var m = Regex.Match(a, @"^--devtools-port=(\d{4,5})$"); if (m.Success) devtoolsPort = int.Parse(m.Groups[1].Value); }
            InitializeComponent();
            ApplyPlacement();
            // our hook must see WM_NCHITTEST / WM_GETMINMAXINFO before WindowChrome's own hook: subscribe first, attach the chrome after
            SourceInitialized += (s, e) => { Native.ApplyDarkChrome(this, 0x00202024); HwndSource.FromHwnd(new WindowInteropHelper(this).Handle)?.AddHook(WndProc); };
            System.Windows.Shell.WindowChrome.SetWindowChrome(this, new System.Windows.Shell.WindowChrome { CaptionHeight = 36, GlassFrameThickness = new Thickness(0, 0, 0, 1), ResizeBorderThickness = new Thickness(6), CornerRadius = new CornerRadius(0), UseAeroCaptionButtons = false });
            Loaded += (s, e) => { try { HwndSource.FromHwnd(new WindowInteropHelper(this).Handle)?.AddHook(WndProc); } catch { } };   // second registration: last-added hooks run first
            menuPopup.Opened += (s, e) => { var first = ((menuPopup.Child as Border)?.Child as StackPanel)?.Children[0] as Button; first?.Focus(); };
            Loaded += async (s, e) => { StartBlocks(); await InitAsync(); };
            Closing += OnClosingWindow;
            StateChanged += (s, e) => { UpdateMaxGlyph(); if (!closing) SavePlacement(); };
            SizeChanged += (s, e) => { if (IsLoaded && WindowState == WindowState.Normal && !closing) SavePlacement(); };
            LocationChanged += (s, e) => { if (IsLoaded && WindowState == WindowState.Normal && !closing) SavePlacement(); };
            PreviewKeyDown += OnPreviewKeyDown;
            titleBar.MouseRightButtonUp += (s, e) => { var p = PointToScreen(e.GetPosition(this)); SystemCommands.ShowSystemMenu(this, p); };
        }

        // ---------------- chrome ----------------
        IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (handled) return IntPtr.Zero;   // the same hook may be registered twice; only the first pass does the work
            // Snap Layouts: Windows 11 shows the layout flyout when the point over our maximize button reports HTMAXBUTTON
            if (msg == Native.WM_NCHITTEST && maxBtn != null && maxBtn.IsVisible)
            {
                try
                {
                    var pt = PointFromScreen(new Point(Native.GetX(lParam), Native.GetY(lParam)));
                    var r = new Rect(maxBtn.TranslatePoint(new Point(0, 0), this), maxBtn.RenderSize);
                    if (r.Contains(pt)) { handled = true; if (!hitLogged) { hitLogged = true; App.Log("snap-layout hit test active"); } maxBtn.Background = (Brush)FindResource("B.Hover"); return new IntPtr(Native.HTMAXBUTTON); }
                    else if (maxBtn.Background != Brushes.Transparent) maxBtn.Background = Brushes.Transparent;
                }
                catch { }
            }
            else if (msg == Native.WM_NCLBUTTONDOWN && wParam.ToInt32() == Native.HTMAXBUTTON) { handled = true; }
            else if (msg == Native.WM_NCLBUTTONUP && wParam.ToInt32() == Native.HTMAXBUTTON) { handled = true; ToggleMaximize(); }
            else if (msg == Native.WM_NCMOUSELEAVE && maxBtn != null) maxBtn.Background = Brushes.Transparent;
            else if (msg == Native.WM_GETMINMAXINFO)
            {
                var dpi = VisualTreeHelper.GetDpi(this);
                if (Native.FillMinMax(hwnd, lParam, MinWidth * dpi.DpiScaleX, MinHeight * dpi.DpiScaleY)) handled = true;
            }
            return IntPtr.Zero;
        }

        void Min_Click(object s, RoutedEventArgs e) { SystemCommands.MinimizeWindow(this); }
        void Max_Click(object s, RoutedEventArgs e) { ToggleMaximize(); }
        void Close_Click(object s, RoutedEventArgs e) { Close(); }
        void ToggleMaximize() { if (WindowState == WindowState.Maximized) SystemCommands.RestoreWindow(this); else SystemCommands.MaximizeWindow(this); }
        void UpdateMaxGlyph()
        {
            var max = WindowState == WindowState.Maximized;
            maxBtn.Content = max ? "\uE923" : "\uE922"; maxBtn.ToolTip = max ? "Restore" : "Maximize";
            // keep the whole window on the work area when maximized: inset the content by whatever the frame overshoots (measured, per side)
            if (max) Dispatcher.BeginInvoke(new Action(() => { try { var o = Native.MaximizedOvershoot(new WindowInteropHelper(this).Handle); var dpi = VisualTreeHelper.GetDpi(this); root.Margin = new Thickness(o.left / dpi.DpiScaleX, o.top / dpi.DpiScaleY, o.right / dpi.DpiScaleX, o.bottom / dpi.DpiScaleY); } catch { } }), DispatcherPriority.Loaded);
            else root.Margin = new Thickness(0);
        }
        void MenuBtn_Click(object s, RoutedEventArgs e) { menuPopup.IsOpen = !menuPopup.IsOpen; }
        void OnPreviewKeyDown(object s, KeyEventArgs e)
        {
            // only shell-level keys; everything else belongs to the website in WebView2
            if (e.Key == Key.Escape && menuPopup.IsOpen) { menuPopup.IsOpen = false; e.Handled = true; }
            else if (e.Key == Key.F10 && Keyboard.Modifiers == ModifierKeys.None) { menuPopup.IsOpen = !menuPopup.IsOpen; e.Handled = true; }
        }

        // menu actions
        void Menu_Reload(object s, RoutedEventArgs e) { menuPopup.IsOpen = false; Navigate(); }
        void Menu_OpenSite(object s, RoutedEventArgs e) { menuPopup.IsOpen = false; App.OpenExternal(App.SiteUrl); }
        void Menu_DataFolder(object s, RoutedEventArgs e) { menuPopup.IsOpen = false; try { Directory.CreateDirectory(App.DataRoot); Process.Start(new ProcessStartInfo("explorer.exe", "\"" + App.DataRoot + "\"") { UseShellExecute = true }); } catch { } }
        void Menu_Diagnostics(object s, RoutedEventArgs e) { menuPopup.IsOpen = false; ShowDiagnostics(); }
        void Menu_Updates(object s, RoutedEventArgs e) { menuPopup.IsOpen = false; App.OpenExternal(App.UpdateUrl); }
        void Menu_About(object s, RoutedEventArgs e) { menuPopup.IsOpen = false; new AboutWindow(this).ShowDialog(); }
        void Menu_Exit(object s, RoutedEventArgs e) { menuPopup.IsOpen = false; Close(); }

        // ---------------- loading state ----------------
        void StartBlocks()
        {
            if (!SystemParameters.ClientAreaAnimation) { foreach (Border b in blocks.Children) b.Background = (Brush)FindResource("B.Red2"); return; }   // reduced motion: static
            blockTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(110) };
            blockTimer.Tick += (s, e) =>
            {
                blockStep = (blockStep + 1) % 12; int i = 0;
                foreach (Border b in blocks.Children) { b.Background = (Brush)FindResource(i <= blockStep ? "B.Red2" : "B.Surface2"); i++; }
            };
            blockTimer.Start();
        }
        bool loadingPage;
        void SetLoading(string text)
        {
            if (loading.Visibility == Visibility.Visible) loadingText.Text = text;
            if (loadingPage && coreReady) { try { _ = web.CoreWebView2.ExecuteScriptAsync("window.__ccsLoading && window.__ccsLoading(" + App.JsString(text) + ")"); } catch { } }
        }
        void ShowLoadingPage()
        {
            try
            {
                string html; using (var s = typeof(MainWindow).Assembly.GetManifestResourceStream("Loading.html")) using (var r = new StreamReader(s, Encoding.UTF8)) html = r.ReadToEnd();
                loadingPage = true; web.CoreWebView2.NavigateToString(html);
            }
            catch (Exception e) { App.Log("loading page failed: " + e.Message); }
        }
        void HideLoading()
        {
            if (loading.Visibility != Visibility.Visible) return;
            blockTimer?.Stop();
            if (SystemParameters.ClientAreaAnimation)
            {
                var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(180)) { FillBehavior = FillBehavior.Stop };
                fade.Completed += (s, e) => loading.Visibility = Visibility.Collapsed;
                loading.BeginAnimation(OpacityProperty, fade);
            }
            else loading.Visibility = Visibility.Collapsed;
        }

        // ---------------- WebView2 ----------------
        async Task InitAsync()
        {
            try
            {
                Directory.CreateDirectory(App.DataRoot);
                App.Log("start " + App.Version + " args=" + string.Join(" ", startupArgs));
                SetLoading("Starting…");
                var opts = new CoreWebView2EnvironmentOptions();
                var extra = App.Dev ? (Environment.GetEnvironmentVariable("CCS_BROWSER_ARGS") ?? "") : "";   // development experiments only
                if (devtoolsPort > 0 || extra.Length > 0) opts.AdditionalBrowserArguments = ((devtoolsPort > 0 ? "--remote-debugging-port=" + devtoolsPort + " " : "") + extra).Trim();
                var env = await CoreWebView2Environment.CreateAsync(null, App.WebViewDataFolder, opts);
                await web.EnsureCoreWebView2Async(env);
                var core = web.CoreWebView2;
                core.Settings.IsStatusBarEnabled = false; core.Settings.AreDevToolsEnabled = App.Dev || devtoolsPort > 0; core.Settings.IsZoomControlEnabled = true;
                core.Settings.AreDefaultContextMenusEnabled = App.Dev; core.Settings.IsPasswordAutosaveEnabled = false; core.Settings.IsGeneralAutofillEnabled = false;
                core.Settings.AreBrowserAcceleratorKeysEnabled = true; core.Settings.IsSwipeNavigationEnabled = false;
                core.Settings.UserAgent = core.Settings.UserAgent + " ClassicCartStudio/" + App.Version;
                core.NavigationStarting += OnNavigationStarting; core.NewWindowRequested += OnNewWindow; core.NavigationCompleted += OnNavigationCompleted;
                core.WebMessageReceived += OnWebMessage; core.PermissionRequested += OnPermission;
                core.ProcessFailed += (s, e) => { App.Log("webview process failed: " + e.ProcessFailedKind); ShowRecovery("The Studio window stopped responding.", "The web content process ended (" + e.ProcessFailedKind + "). Choose Try Again to reload.", e.ProcessFailedKind.ToString()); };
                coreReady = true;
                ShowLoadingPage(); HideLoading();
                SetLoading("Starting the Studio backend…");
                // Start the backend first and give it a moment: a page that probes a port nobody listens on makes Chromium back off from
                // that host for 10-60 s (measured), which would delay every later request. The page is told whether the backend is ready
                // and otherwise waits for the host's signal instead of probing.
                var startTask = Task.Run(() => StartBackend());
                await Task.WhenAny(startTask, Task.Delay(4000));
                await core.AddScriptToExecuteOnDocumentCreatedAsync(DesktopBootstrapScript(backend.Ready));
                SetLoading("Loading the Studio website…");
                await Task.Delay(120);   // let the status text paint once before the site navigation replaces the page
                Navigate();
            }
            catch (Exception e)
            {
                App.Log("webview init failed: " + e);
                blockTimer?.Stop();
                loadingText.Text = "Microsoft Edge WebView2 is not available";
                MessageDialog.Show(this, "WebView2 is needed", "Classic Cart Studio shows the Studio website through Microsoft Edge WebView2, which comes with Microsoft Edge on Windows 10 and 11. Install or repair Microsoft Edge, then start Classic Cart Studio again.", e.Message);
            }
        }

        // Only pages of the approved origin get the desktop session (token + port). Every other origin sees nothing.
        string DesktopBootstrapScript(bool ready)
        {
            var origins = new StringBuilder("[");
            foreach (var o in App.SiteOrigins) origins.Append(App.JsString(o)).Append(',');
            if (App.Dev && !string.IsNullOrEmpty(App.DevSiteUrl)) origins.Append(App.JsString(App.OriginOf(App.DevSiteUrl))).Append(',');
            origins.Append("]");
            return "(function(){ var ok=" + origins + ".indexOf(location.origin)>=0; if(!ok) return; " +
                   "var d=Object.freeze({app:'classic-cart-studio',version:" + App.JsString(App.Version) + ",token:" + App.JsString(backend.Token) + ",port:" + backend.Port + ",ready:" + (ready ? "true" : "false") + "}); " +
                   "Object.defineProperty(window,'__CCS_DESKTOP',{value:d,writable:false,configurable:false,enumerable:false}); })();";
        }

        void StartBackend()
        {
            if (backendStarting) return; backendStarting = true;
            try
            {
                var ok = backend.Start(60000);
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (!ok) ShowBackendProblem();
                    else if (coreReady && web.CoreWebView2 != null) { _ = web.CoreWebView2.ExecuteScriptAsync("window.__ccsBackendReady && window.__ccsBackendReady()"); DeliverPendingLink(); }
                    if (ok) _ = Task.Run(async () => { await Task.Delay(1500); try { Dispatcher.BeginInvoke(new Action(() => { if (web.CoreWebView2 != null) _ = web.CoreWebView2.ExecuteScriptAsync("window.__ccsBackendReady && window.__ccsBackendReady()"); })); } catch { } });
                }));
            }
            finally { backendStarting = false; }
        }

        internal void RestartBackend() { if (backendStarting) return; try { backend.Stop(); } catch { } _ = Task.Run(() => StartBackend()); }

        void ShowBackendProblem()
        {
            var retry = MessageDialog.Show(this, "The Studio backend did not start", backend.LastError, null, "Try Again", "Continue without it");
            if (retry) _ = Task.Run(() => StartBackend());
        }

        void Navigate()
        {
            if (!coreReady) return; recoveryShown = false; loadingPage = false;
            try { web.CoreWebView2.Navigate(App.StartUrl); } catch (Exception e) { App.Log("navigate failed: " + e.Message); }
        }

        void OnNavigationStarting(object sender, CoreWebView2NavigationStartingEventArgs e)
        {
            var url = e.Uri ?? ""; var origin = App.OriginOf(url);
            if (url.StartsWith("about:") || url.StartsWith("data:") || App.IsApprovedOrigin(origin)) return;
            e.Cancel = true; App.Log("blocked navigation to " + origin + " (opened externally)"); App.OpenExternal(url);
        }

        void OnNewWindow(object sender, CoreWebView2NewWindowRequestedEventArgs e)
        {
            e.Handled = true; var url = e.Uri ?? "";
            if (App.IsApprovedOrigin(App.OriginOf(url))) { try { web.CoreWebView2.Navigate(url); } catch { } } else App.OpenExternal(url);
        }

        void OnNavigationCompleted(object sender, CoreWebView2NavigationCompletedEventArgs e)
        {
            var src = web.CoreWebView2.Source ?? "";
            if (e.IsSuccess)
            {
                if (!(src.StartsWith("about:") || src.StartsWith("data:"))) { LastNavigation = "loaded " + DateTime.Now.ToString("HH:mm:ss"); firstPageShown = true; }
                HideLoading();
                if (backend.Ready) _ = web.CoreWebView2.ExecuteScriptAsync("window.__ccsBackendReady && window.__ccsBackendReady()");
                DeliverPendingLink(); return;
            }
            if (src.StartsWith("about:") || src.StartsWith("data:")) return;
            LastNavigation = "failed: " + e.WebErrorStatus + " at " + DateTime.Now.ToString("HH:mm:ss");
            App.Log("navigation failed: " + e.WebErrorStatus + " for " + src);
            ShowRecovery("The Studio website couldn't be reached.", null, e.WebErrorStatus + " — " + src);
        }

        void ShowRecovery(string title, string message, string details)
        {
            if (!coreReady) return; recoveryShown = true; loadingPage = false;
            try
            {
                string html;
                using (var s = typeof(MainWindow).Assembly.GetManifestResourceStream("Recovery.html")) using (var r = new StreamReader(s, Encoding.UTF8)) html = r.ReadToEnd();
                web.CoreWebView2.NavigateToString(html);
                var info = "{\"title\":" + JsonStr(title) + ",\"message\":" + JsonStr(message ?? "") + ",\"details\":" + JsonStr(details ?? "") + "}";
                web.CoreWebView2.NavigationCompleted += (s2, e2) => { if (recoveryShown) _ = web.CoreWebView2.ExecuteScriptAsync("window.__ccsRecovery && window.__ccsRecovery(" + App.JsString(info) + ")"); };
                HideLoading();
            }
            catch (Exception ex) { App.Log("recovery screen failed: " + ex.Message); }
        }

        static string JsonStr(string s) { return "\"" + (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "") + "\""; }

        // Messages come only from the bundled recovery page (about:blank origin). Pages from the website have no message channel.
        void OnWebMessage(object sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            var source = e.Source ?? ""; if (!source.StartsWith("about:")) return;
            string cmd = ""; try { var m = Regex.Match(e.WebMessageAsJson ?? "", "\"cmd\"\\s*:\\s*\"([a-z]+)\""); if (m.Success) cmd = m.Groups[1].Value; } catch { }
            switch (cmd)
            {
                case "retry": Navigate(); if (!backend.Ready && !backendStarting) _ = Task.Run(() => StartBackend()); break;
                case "network": try { Process.Start(new ProcessStartInfo("ms-settings:network-status") { UseShellExecute = true }); } catch { } break;
                case "diagnostics": ShowDiagnostics(); break;
                case "exit": Close(); break;
            }
        }

        // Local Network Access: the approved website inside this window may reach the backend on 127.0.0.1 without a prompt
        // (that is the app-managed channel). Any other permission (camera, location, ...) is denied.
        void OnPermission(object sender, CoreWebView2PermissionRequestedEventArgs e)
        {
            var kind = e.PermissionKind.ToString(); var origin = App.OriginOf(e.Uri ?? "");
            if (App.IsApprovedOrigin(origin) && (kind.IndexOf("LocalNetwork", StringComparison.OrdinalIgnoreCase) >= 0 || kind.IndexOf("Unknown", StringComparison.OrdinalIgnoreCase) >= 0)) { e.State = CoreWebView2PermissionState.Allow; App.Log("permission " + kind + " allowed for " + origin); return; }
            e.State = CoreWebView2PermissionState.Deny; App.Log("permission " + kind + " denied for " + origin);
        }

        // nesstudio:// links: open (bring forward) and handoff?offer=<id> (tell the page to look at the pending collection). Nothing else.
        public void OnActivatedFromAnotherInstance(string payload)
        {
            App.Log("activated by another instance"); BringToFrontNow();
            foreach (var line in (payload ?? "").Split('\n')) HandleLink(line.Trim());
        }

        void HandleLink(string arg)
        {
            if (string.IsNullOrEmpty(arg) || !arg.StartsWith("nesstudio:", StringComparison.OrdinalIgnoreCase)) return;
            var m = Regex.Match(arg, @"^nesstudio://handoff\?(?:.*&)?offer=([A-Za-z0-9-]{8,64})", RegexOptions.IgnoreCase);
            if (m.Success) { pendingLink = m.Groups[1].Value; App.Log("handoff link for offer " + pendingLink); DeliverPendingLink(); return; }
            var safe = Regex.Replace(arg, "[^A-Za-z0-9:/?=&._-]", ""); App.Log("link ignored: " + safe.Substring(0, Math.Min(80, safe.Length)));
        }

        void DeliverPendingLink()
        {
            if (string.IsNullOrEmpty(pendingLink) || !coreReady || !backend.Ready || recoveryShown) return;
            var id = pendingLink; pendingLink = null;
            _ = web.CoreWebView2.ExecuteScriptAsync("window.__ccsHandoff ? window.__ccsHandoff(" + App.JsString(id) + ") : (window.__ccsPendingOffer=" + App.JsString(id) + ")");
        }

        void BringToFrontNow()
        {
            try { if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal; Activate(); Topmost = true; Topmost = false; } catch { }
        }

        protected override void OnContentRendered(EventArgs e)
        {
            base.OnContentRendered(e);
            UpdateMaxGlyph();
            foreach (var a in startupArgs) HandleLink(a);
        }

        internal void ShowDiagnostics() { new DiagnosticsWindow(this).ShowDialog(); }
        internal string WebViewRuntime() { try { return CoreWebView2Environment.GetAvailableBrowserVersionString(); } catch { return "not found"; } }
        internal bool WebsiteShown => firstPageShown && !recoveryShown;

        // ---------------- placement ----------------
        void ApplyPlacement()
        {
            double vw = SystemParameters.VirtualScreenWidth, vh = SystemParameters.VirtualScreenHeight, vx = SystemParameters.VirtualScreenLeft, vy = SystemParameters.VirtualScreenTop;
            var work = SystemParameters.WorkArea;
            double w = settings.W > 0 ? settings.W : Math.Min(1440, work.Width - 80), h = settings.H > 0 ? settings.H : Math.Min(920, work.Height - 60);
            bool visible = settings.X >= vx - 8 && settings.Y >= vy - 8 && settings.X + 120 <= vx + vw && settings.Y + 60 <= vy + vh;
            if (settings.X < -10000 || settings.Y < -10000 || settings.W <= 0 || !visible) { WindowStartupLocation = WindowStartupLocation.CenterScreen; Width = w; Height = h; }
            else { WindowStartupLocation = WindowStartupLocation.Manual; Left = settings.X; Top = settings.Y; Width = w; Height = h; }
            if (settings.Maximized) WindowState = WindowState.Maximized;
        }

        void SavePlacement()
        {
            if (closing) return;
            settings.Maximized = WindowState == WindowState.Maximized;
            var b = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
            if (b.Width < 100 || b.Height < 100) return;
            settings.X = (int)b.X; settings.Y = (int)b.Y; settings.W = (int)b.Width; settings.H = (int)b.Height; settings.Save();
        }

        void OnClosingWindow(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (closing) return; closing = true; SavePlacement();
            App.Log("closing");
            try { backend.Stop(); } catch { }
        }
    }
}
