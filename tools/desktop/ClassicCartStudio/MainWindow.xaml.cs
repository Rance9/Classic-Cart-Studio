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
        readonly System.Collections.Generic.List<string[]> pendingActions = new System.Collections.Generic.List<string[]>();   // [kind, value] for the page: share, package, file
        int devtoolsPort;
        internal string LastNavigation = "not yet";
        DispatcherTimer blockTimer; int blockStep; bool hitLogged;

        public MainWindow(string[] args)
        {
            startupArgs = args ?? new string[0];
            App.LocalOrigin = backend.Base;   // the bundled interface, served by this window's backend
            foreach (var a in startupArgs) { var m = Regex.Match(a, @"^--devtools-port=(\d{4,5})$"); if (m.Success) devtoolsPort = int.Parse(m.Groups[1].Value); }
            InitializeComponent();
            ApplyPlacement();
            // our hook must see WM_NCHITTEST / WM_GETMINMAXINFO before WindowChrome's own hook: subscribe first, attach the chrome after
            SourceInitialized += (s, e) => { Native.ApplyDarkChrome(this, 0x00202024); HwndSource.FromHwnd(new WindowInteropHelper(this).Handle)?.AddHook(WndProc); };
            DpiChanged += (s, e) => Dispatcher.BeginInvoke(new Action(() => Native.ApplyTaskbarIcon(this)), DispatcherPriority.Background);   // in case WPF re-applies its own icon after a monitor change            System.Windows.Shell.WindowChrome.SetWindowChrome(this, new System.Windows.Shell.WindowChrome { CaptionHeight = 36, GlassFrameThickness = new Thickness(0, 0, 0, 1), ResizeBorderThickness = new Thickness(6), CornerRadius = new CornerRadius(0), UseAeroCaptionButtons = false });
            Loaded += (s, e) => { try { HwndSource.FromHwnd(new WindowInteropHelper(this).Handle)?.AddHook(WndProc); } catch { } Native.ApplyTaskbarIcon(this); };   // second registration: last-added hooks run first; the icon again once the button exists
            // right-aligned under the menu button, in the window's own coordinates (independent of DPI, monitor position or maximize state)
            menuPopup.CustomPopupPlacementCallback = (size, target, offset) => new[] { new System.Windows.Controls.Primitives.CustomPopupPlacement(new Point(target.Width - size.Width, target.Height + 4), System.Windows.Controls.Primitives.PopupPrimaryAxis.Horizontal) };
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
            // The window draws its own title bar. WindowChrome left the system caption in place on current Windows 11 builds (two
            // title bars, owner 2026-10-18) and then gave no caption to drag, so this hook does it: only the system caption is
            // removed (the invisible resize frame stays on the left, right and bottom), and the hit test below makes the title bar
            // draggable, its top edge resizable and its buttons buttons.
            if (msg == Native.WM_NCCALCSIZE && wParam != IntPtr.Zero) { Native.CalcClientWithoutCaption(hwnd, lParam); handled = true; return IntPtr.Zero; }
            if (msg == Native.WM_NCHITTEST)
            {
                try
                {
                    var def = Native.DefaultHitTest(hwnd, lParam);
                    if (def != Native.HTCLIENT) { handled = true; return new IntPtr(def); }   // the resize frame on the left, right and bottom
                    var pt = PointFromScreen(new Point(Native.GetX(lParam), Native.GetY(lParam)));
                    // Snap Layouts: Windows 11 shows the layout flyout when the point over our maximize button reports HTMAXBUTTON
                    if (maxBtn != null && maxBtn.IsVisible)
                    {
                        var r = new Rect(maxBtn.TranslatePoint(new Point(0, 0), this), maxBtn.RenderSize);
                        if (r.Contains(pt)) { handled = true; if (!hitLogged) { hitLogged = true; App.Log("snap-layout hit test active"); } maxBtn.Background = (Brush)FindResource("B.Hover"); return new IntPtr(Native.HTMAXBUTTON); }
                        else if (maxBtn.Background != Brushes.Transparent) maxBtn.Background = Brushes.Transparent;
                    }
                    var maximized = WindowState == WindowState.Maximized; var w = root.ActualWidth;
                    if (!maximized && pt.Y >= 0 && pt.Y < 5)   // the top edge resizes, like the other edges
                    { handled = true; return new IntPtr(pt.X < 12 ? Native.HTTOPLEFT : pt.X > w - 12 ? Native.HTTOPRIGHT : Native.HTTOP); }
                    if (pt.Y >= 0 && pt.Y < titleBar.ActualHeight && !OverButton(pt)) { handled = true; return new IntPtr(Native.HTCAPTION); }   // drag, double-click, Snap
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

        bool OverButton(Point pt)
        {
            for (var d = InputHitTest(pt) as DependencyObject; d != null && d != this; d = VisualTreeHelper.GetParent(d) ?? LogicalTreeHelper.GetParent(d))
                if (d is System.Windows.Controls.Primitives.ButtonBase) return true;
            return false;
        }

        void Min_Click(object s, RoutedEventArgs e) { SystemCommands.MinimizeWindow(this); }
        void Max_Click(object s, RoutedEventArgs e) { ToggleMaximize(); }
        void Close_Click(object s, RoutedEventArgs e) { Close(); }
        void ToggleMaximize() { if (WindowState == WindowState.Maximized) SystemCommands.RestoreWindow(this); else SystemCommands.MaximizeWindow(this); }
        void UpdateMaxGlyph()
        {
            var max = WindowState == WindowState.Maximized;
            maxBtn.Content = max ? "\uE923" : "\uE922"; maxBtn.ToolTip = max ? "Restore" : "Maximize";
            root.Margin = new Thickness(0);   // maximized, the client area is already exactly the work area (Native.CalcClientWithoutCaption)
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
        void Menu_Diagnostics(object s, RoutedEventArgs e) { menuPopup.IsOpen = false; App.Log("menu: diagnostics"); ShowDiagnostics(); }
        void Menu_Updates(object s, RoutedEventArgs e) { menuPopup.IsOpen = false; App.OpenExternal(App.UpdateUrl); }
        void Menu_About(object s, RoutedEventArgs e) { menuPopup.IsOpen = false; App.Log("menu: about"); try { new AboutWindow(this).ShowDialog(); } catch (Exception ex) { App.Log("about failed: " + ex); MessageDialog.Show(this, "About", "The About window could not open. Try again.", ex.Message); } }
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
                core.ProcessFailed += (s, e) => { App.Log("webview process failed: " + e.ProcessFailedKind); ShowRecovery("Classic Cart Studio stopped responding.", "Part of the window closed unexpectedly. Choose Try Again to reload it; your collections are kept.", e.ProcessFailedKind.ToString()); };
                coreReady = true;
                ShowLoadingPage(); HideLoading();
                SetLoading("Getting things ready…");
                // Start the backend first and give it a moment: a page that probes a port nobody listens on makes Chromium back off from
                // that host for 10-60 s (measured), which would delay every later request. The page is told whether the backend is ready
                // and otherwise waits for the host's signal instead of probing.
                var startTask = Task.Run(() => StartBackend());
                // the workspace build (Start-ClassicCartStudio.ps1) has the backend serve the site itself: that page cannot load before it listens
                var siteOnBackend = string.Equals(App.OriginOf(App.StartUrl), backend.Base, StringComparison.OrdinalIgnoreCase) || string.Equals(App.OriginOf(App.StartUrl), "http://localhost:" + backend.Port, StringComparison.OrdinalIgnoreCase);
                if (siteOnBackend) await startTask; else await Task.WhenAny(startTask, Task.Delay(4000));
                await core.AddScriptToExecuteOnDocumentCreatedAsync(DesktopBootstrapScript(backend.Ready));
                SetLoading("Loading Classic Cart Studio…");
                await Task.Delay(120);   // let the status text paint once before the site navigation replaces the page
                Navigate();
            }
            catch (Exception e)
            {
                App.Log("webview init failed: " + e);
                blockTimer?.Stop();
                loadingText.Text = "Microsoft Edge WebView2 is not available";
                MessageDialog.Show(this, "WebView2 Needed", "Classic Cart Studio needs Microsoft Edge WebView2, which comes with Microsoft Edge on Windows 10 and 11. Install or repair Microsoft Edge, then start Classic Cart Studio again.", e.Message);
            }
        }

        // Only pages of the approved origin get the desktop session (token + port). Every other origin sees nothing.
        string DesktopBootstrapScript(bool ready)
        {
            var origins = new StringBuilder("[");
            origins.Append(App.JsString(App.LocalOrigin)).Append(',');   // only the bundled interface on this computer gets the desktop session
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
            var retry = MessageDialog.Show(this, "Startup Problem", backend.LastError, null, "Try Again", "Continue Anyway");
            if (retry) _ = Task.Run(() => StartBackend());
        }

        void Navigate()
        {
            if (!coreReady) return; recoveryShown = false; loadingPage = false;
            try { web.CoreWebView2.Navigate(App.StartUrl); } catch (Exception e) { App.Log("navigate failed: " + e.Message); }
        }

        DateTime lastBlocked = DateTime.MinValue;
        // The website's short links forward to GitHub (/source, /app). Followed inside the window they would start loading on the
        // approved origin and then be cancelled mid-load by the policy below, which used to surface the recovery screen. They are
        // external links in substance, so the host hands them to the user's browser without navigating the window.
        static bool IsShortLink(string url)
        {
            try { var u = new Uri(url); var p = u.AbsolutePath.TrimEnd('/').ToLowerInvariant(); return p == "/source" || p == "/app"; } catch { return false; }
        }

        void OnNavigationStarting(object sender, CoreWebView2NavigationStartingEventArgs e)
        {
            var url = e.Uri ?? ""; var origin = App.OriginOf(url);
            if (url.StartsWith("about:") || url.StartsWith("data:")) return;
            if (App.IsApprovedOrigin(origin) && !IsShortLink(url)) return;
            e.Cancel = true; lastBlocked = DateTime.Now; App.Log("blocked navigation to " + (App.IsApprovedOrigin(origin) ? url : origin) + " (opened externally)"); App.OpenExternal(url);
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
            if (e.WebErrorStatus == CoreWebView2WebErrorStatus.OperationCanceled && (DateTime.Now - lastBlocked).TotalSeconds < 5)
            {   // a load the policy itself cut short (the external link was handed to the browser): the window keeps its page
                App.Log("navigation cancelled by policy for " + src + " (no recovery screen)"); return;
            }
            LastNavigation = "failed: " + e.WebErrorStatus + " at " + DateTime.Now.ToString("HH:mm:ss");
            App.Log("navigation failed: " + e.WebErrorStatus + " for " + src);
            ShowRecovery("Classic Cart Studio couldn't load its interface.", null, e.WebErrorStatus + " — " + src);
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

        // Links and files handed to the application (command line, or forwarded by a second instance):
        //   classiccartstudio://open                      bring the window forward
        //   classiccartstudio://share/<id>                a retired public share link: the page explains that collections are shared as .ccs files
        //   classiccartstudio://handoff?offer=<id>        a collection the website offered (the existing inbox)
        //   classiccartstudio://package?offer=<id>        a .ccs package the website handed to this computer's backend (staged there)
        //   <path>.ccs                                    a collection package opened from File Explorer (double-click, Open With)
        // nesstudio:// (earlier releases) is accepted the same way. Every value is validated; nothing in a link is ever run as a command.
        public void OnActivatedFromAnotherInstance(string payload)
        {
            App.Log("activated by another instance"); BringToFrontNow();
            foreach (var line in (payload ?? "").Split('\n')) HandleLink(line.Trim());
        }

        void HandleLink(string arg)
        {
            if (string.IsNullOrEmpty(arg)) return;
            if (arg.EndsWith(".ccs", StringComparison.OrdinalIgnoreCase) && arg.IndexOf("://", StringComparison.Ordinal) < 0)
            {
                string full = null;
                try { full = System.IO.Path.GetFullPath(arg); } catch { }
                if (full != null && System.IO.File.Exists(full)) { App.Log("package file opened (" + System.IO.Path.GetFileName(full).Length + " characters)"); QueueAction("file", full); }
                else App.Log("package file not found");
                return;
            }
            var m = Regex.Match(arg, @"^(?:classiccartstudio|nesstudio)://([a-z]+)(?:/([A-Za-z0-9]{6,32}))?/?(?:\?(?:.*&)?(?:offer|id)=([A-Za-z0-9_-]{8,64}))?(?:&.*)?$", RegexOptions.IgnoreCase);
            if (!m.Success) { if (arg.IndexOf("://", StringComparison.Ordinal) > 0) { var safe = Regex.Replace(arg, "[^A-Za-z0-9:/?=&._-]", ""); App.Log("link ignored: " + safe.Substring(0, Math.Min(80, safe.Length))); } return; }
            var verb = m.Groups[1].Value.ToLowerInvariant(); var path = m.Groups[2].Value; var q = m.Groups[3].Value;
            if (verb == "handoff" && q.Length > 0) { pendingLink = q; App.Log("handoff link for offer " + q); DeliverPendingLink(); return; }
            if (verb == "share" && (path.Length > 0 || Regex.IsMatch(q, "^[A-Za-z0-9]{6,32}$"))) { QueueAction("share", path.Length > 0 ? path : q); return; }
            if (verb == "package" && Regex.IsMatch(q, "^[A-Za-z0-9_-]{16,40}$")) { QueueAction("package", q); return; }
            if (verb == "open") return;
            App.Log("link ignored: " + verb);
        }

        void QueueAction(string kind, string value) { pendingActions.Add(new[] { kind, value }); App.Log("link: " + kind); DeliverPendingLink(); }

        void DeliverPendingLink()
        {
            // only once the website page itself has loaded: a link handed to the loading screen would be lost when the page replaces it
            if (!coreReady || !backend.Ready || recoveryShown || !firstPageShown) return;
            if (!string.IsNullOrEmpty(pendingLink))
            {
                var id = pendingLink; pendingLink = null;
                _ = web.CoreWebView2.ExecuteScriptAsync("window.__ccsHandoff ? window.__ccsHandoff(" + App.JsString(id) + ") : (window.__ccsPendingOffer=" + App.JsString(id) + ")");
            }
            foreach (var a in pendingActions.ToArray())
            {
                _ = web.CoreWebView2.ExecuteScriptAsync("window.__ccsLink ? window.__ccsLink(" + App.JsString(a[0]) + "," + App.JsString(a[1]) + ") : (window.__ccsLinkQ=window.__ccsLinkQ||[]).push([" + App.JsString(a[0]) + "," + App.JsString(a[1]) + "])");
            }
            pendingActions.Clear();
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

        internal void ShowDiagnostics() { try { new DiagnosticsWindow(this).ShowDialog(); } catch (Exception ex) { App.Log("diagnostics failed: " + ex); MessageDialog.Show(this, "Diagnostics", "The Diagnostics window could not open. Try again.", ex.Message); } }
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
