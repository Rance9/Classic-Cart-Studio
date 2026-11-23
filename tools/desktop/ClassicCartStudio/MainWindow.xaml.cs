// The one application window: custom dark title bar (App tools, caption buttons with Snap Layouts), WebView2 showing the live
// Studio website, a branded loading state, the recovery screen when the website cannot load, the desktop token injection for the
// approved origin only, navigation filtering (anything else opens in the user's browser) and the classiccartstudio:// link handling.
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
        // The interface is served by this window's own backend. A page from the backend's address is loaded only while that is this window's
        // own backend: the process this window started (with its data folder), still running, ready and accepting this window's own token
        // (OwnBackendRunning). A program that merely answers on that port (another Classic Cart Studio window with another data folder, or
        // anything else) is never trusted: the window stays on its recovery screen until its own backend runs, then loads its page. Every way
        // to the page goes through LoadInterface (startup, Reload, Try Again, the backend starting) and the navigation check refuses the rest.
        bool waitingForOwnBackend;
        string pendingLink; bool pendingUpdateCheck;
        readonly System.Collections.Generic.List<string[]> pendingActions = new System.Collections.Generic.List<string[]>();   // [kind, value] for the page: share, package, file
        int devtoolsPort;
        internal string LastNavigation = "not yet";
        DispatcherTimer blockTimer; int blockStep; bool hitLogged;
        CoreWebView2Environment wvEnv; PlayerBridge bridge; bool exitConfirmed;   // the Preview's game player and the close that asked first (F6)
        int openGen;   // the Preview's latest "embed-open"; an earlier one still waiting for the previous player is dropped

        public MainWindow(string[] args)
        {
            startupArgs = args ?? new string[0];
            App.LocalOrigin = backend.Base;   // the bundled interface, served by this window's backend
            foreach (var a in startupArgs) { var m = Regex.Match(a, @"^--devtools-port=(\d{4,5})$"); if (m.Success) devtoolsPort = int.Parse(m.Groups[1].Value); }
            InitializeComponent();
            Title = titleText.Text = App.WindowTitle;   // the running version and channel, in the title bar and the taskbar alike
            ApplyIcons();
            ApplyPlacement();
            // the title bar is drawn by the window itself and WndProc removes the system caption (no WindowChrome: see WndProc)
            SourceInitialized += (s, e) => { Native.ApplyDarkChrome(this, 0x00202024); HwndSource.FromHwnd(new WindowInteropHelper(this).Handle)?.AddHook(WndProc); };
            DpiChanged += (s, e) => { ApplyIcons(); Dispatcher.BeginInvoke(new Action(() => Native.ApplyTaskbarIcon(this)), DispatcherPriority.Background); };   // in case WPF re-applies its own icon after a monitor change
            Loaded += (s, e) => { try { HwndSource.FromHwnd(new WindowInteropHelper(this).Handle)?.AddHook(WndProc); } catch { } Native.ApplyTaskbarIcon(this); };   // second registration: last-added hooks run first; the icon again once the button exists
            // right-aligned under the menu button, in the window's own coordinates (independent of DPI, monitor position or maximize state)
            menuPopup.CustomPopupPlacementCallback = (size, target, offset) => new[] { new System.Windows.Controls.Primitives.CustomPopupPlacement(new Point(target.Width - size.Width, target.Height + 4), System.Windows.Controls.Primitives.PopupPrimaryAxis.Horizontal) };
            // App tools: the first command takes keyboard focus; Escape closes it and returns focus to its button
            menuPopup.Opened += (s, e) => { foreach (var c in menuItems.Children) if (c is Button b) { b.Focus(); break; } };
            Loaded += async (s, e) => { ApplyIcons(); StartBlocks(); await InitAsync(); };
            Closing += OnClosingWindow;
            StateChanged += (s, e) => { UpdateMaxGlyph(); if (!closing) SavePlacement(); };
            SizeChanged += (s, e) => { if (IsLoaded && WindowState == WindowState.Normal && !closing) SavePlacement(); };
            LocationChanged += (s, e) => { if (IsLoaded && WindowState == WindowState.Normal && !closing) SavePlacement(); };
            PreviewKeyDown += OnPreviewKeyDown;
            titleBar.MouseRightButtonUp += (s, e) => { var p = PointToScreen(e.GetPosition(this)); SystemCommands.ShowSystemMenu(this, p); };
        }

        // the app icon's frame for each place this window draws it, at the window's scale (App.IconFrame); again when the scale changes
        void ApplyIcons()
        {
            try { var k = VisualTreeHelper.GetDpi(this).DpiScaleX; titleIcon.Source = App.IconFrame(16, k); loadIcon.Source = App.IconFrame(48, k); menuIcon.Source = App.IconFrame(22, k); }
            catch (Exception e) { App.Log("icon: " + e.Message); }
        }

        // ---------------- chrome ----------------
        IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (handled) return IntPtr.Zero;   // the same hook may be registered twice; only the first pass does the work
            // The window draws its own title bar. WindowChrome left the system caption in place on current Windows 11 builds (two
            // title bars) and then gave no caption to drag, so this hook does it: only the system caption is
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
            if (e.Key == Key.Escape && menuPopup.IsOpen) { menuPopup.IsOpen = false; menuBtn.Focus(); e.Handled = true; }
            else if (e.Key == Key.F10 && Keyboard.Modifiers == ModifierKeys.None) { menuPopup.IsOpen = !menuPopup.IsOpen; e.Handled = true; }
        }

        // menu actions
        void Menu_Reload(object s, RoutedEventArgs e) { menuPopup.IsOpen = false; LoadInterface("reload", true); }
        void Menu_OpenSite(object s, RoutedEventArgs e) { menuPopup.IsOpen = false; App.OpenExternal(App.WebsiteUrl); }
        void Menu_DataFolder(object s, RoutedEventArgs e) { menuPopup.IsOpen = false; try { Directory.CreateDirectory(App.DataRoot); Process.Start(new ProcessStartInfo("explorer.exe", "\"" + App.DataRoot + "\"") { UseShellExecute = true }); } catch { } }
        void Menu_Diagnostics(object s, RoutedEventArgs e) { menuPopup.IsOpen = false; App.Log("menu: diagnostics"); ShowDiagnostics(); }
        void Menu_Updates(object s, RoutedEventArgs e) { menuPopup.IsOpen = false; if (backend.Ready) _ = web.CoreWebView2?.ExecuteScriptAsync("window.__ccsCheckUpdates&&window.__ccsCheckUpdates()"); else App.OpenExternal(App.UpdateUrl); }
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
                // the WebView2 Runtime: Microsoft's discovery, else a verified Microsoft Runtime folder (Runtime.cs); none usable is said plainly
                var rt = Runtime.Find();
                App.Log("webview2 runtime: " + rt.Kind + (rt.Version != null ? " " + rt.Version : "") + (rt.Folder != null ? " at " + rt.Folder : "") + (rt.Detail.Length > 0 ? " (" + rt.Detail + ")" : ""));
                if (rt.Kind != "default" && rt.Kind != "folder") { RuntimeMissing(rt); return; }
                var env = await CoreWebView2Environment.CreateAsync(rt.Kind == "folder" ? rt.Folder : null, App.WebViewDataFolder, opts); wvEnv = env;
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
                LoadInterface("start", false);
            }
            catch (WebView2RuntimeNotFoundException e) { App.Log("webview init: runtime not found: " + e); RuntimeMissing(new Runtime.Found { Detail = Runtime.Describe(e) }); }
            catch (Exception e)
            {
                App.Log("webview init failed: " + e);
                blockTimer?.Stop();
                loadingText.Text = "The window could not start";
                MessageDialog.Show(this, "Classic Cart Studio Could Not Start Its Window", "The part of Windows that shows Classic Cart Studio's window (the Microsoft Edge WebView2 Runtime) is installed, but it did not start. Close Classic Cart Studio and open it again. If it keeps happening, restart the computer, then run the Classic Cart Studio installer again to repair it. Your collections are not affected.", Runtime.Describe(e));
            }
        }

        // no usable WebView2 Runtime: Setup normally adds it (offline, from its own copy of Microsoft's installer); say so, nothing else
        void RuntimeMissing(Runtime.Found rt)
        {
            blockTimer?.Stop(); loadingText.Text = "Microsoft Edge WebView2 Runtime needed";
            var why = rt.Kind == "old" ? "The version on this computer (" + rt.Version + ") is too old; Classic Cart Studio needs " + Runtime.Minimum + " or later." : "It was not found on this computer.";
            MessageDialog.Show(this, "Microsoft Edge WebView2 Runtime Needed", "Classic Cart Studio shows its window with the Microsoft Edge WebView2 Runtime, a free part of Windows from Microsoft. " + why + " Run the Classic Cart Studio installer again: it adds the Runtime from its own copy, without an internet connection. Your collections are not affected.", rt.Detail + (rt.Seen.Count > 0 ? " | " + string.Join(" | ", rt.Seen) : ""));
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
                    else if (waitingForOwnBackend) LoadInterface("its own backend started", false);   // its own backend at last: its own page
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

        /// <summary>the page comes from this window's backend's address (the installed app; a workspace build may serve it elsewhere)</summary>
        bool SiteOnBackend => IsBackendOrigin(App.OriginOf(App.StartUrl));
        bool IsBackendOrigin(string origin) => string.Equals(origin, backend.Base, StringComparison.OrdinalIgnoreCase) || string.Equals(origin, "http://localhost:" + backend.Port, StringComparison.OrdinalIgnoreCase);
        /// <summary>this window's own backend runs at its address: the process it started is alive and became ready by accepting this window's
        /// own token (Backend.Start); while that process runs, no other program can hold the port</summary>
        bool OwnBackendRunning => backend.Ready && backend.Process != null && !backend.Process.HasExited;

        /// <summary>the one way to the interface: loaded only once this window's own backend is confirmed again (alive, and answering this window's
        /// own token now); otherwise the recovery screen stays, and the page loads when its own backend has started</summary>
        async void LoadInterface(string why, bool startBackend)
        {
            if (!coreReady) return;
            var own = !SiteOnBackend || (OwnBackendRunning && await Task.Run(() => { var v = backend.Version(); return v != null && v.Value.Key && OwnBackendRunning; }));
            if (own) { waitingForOwnBackend = false; Navigate(); return; }
            waitingForOwnBackend = true; App.Log(why + ": this window's own backend is not running at its address; its page is not loaded");
            ShowRecovery("Classic Cart Studio Cannot Start Yet", backend.ForeignOnPort ? backend.LastError : "Classic Cart Studio's background helper for this window is not running. Choose Try Again.", "");
            if (startBackend && !backendStarting) _ = Task.Run(() => StartBackend());
        }

        void Navigate()   // only from LoadInterface, after the check
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
            if (IsBackendOrigin(origin) && !OwnBackendRunning)   // a reload, a key, a link or the page itself: never a page this window's own backend does not serve
            {
                e.Cancel = true; lastBlocked = DateTime.Now; App.Log("refused a page from " + origin + ": this window's own backend is not running there");
                Dispatcher.BeginInvoke(new Action(() => LoadInterface("navigation", false))); return;
            }
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

        // Messages come from the bundled recovery page (about:blank origin) and, for the Preview's game player only, from the bundled
        // Studio (this window's own backend origin): "embed-open", "embed-cmd <one checked command>", "embed-dispose", and "app-update-exit" (close the
        // window, asking first if a game is open, after the backend started a checked update installer). Nothing else.
        void OnWebMessage(object sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            var source = e.Source ?? "";
            if (App.IsApprovedOrigin(App.OriginOf(source)))
            {
                string m = null; try { m = e.TryGetWebMessageAsString(); } catch { }
                if (m == null) return;
                if (m == "embed-open") _ = OpenBridge(++openGen);
                else if (m.StartsWith("embed-cmd ")) bridge?.Command(m.Substring(10));
                else if (m == "embed-dispose") { openGen++; if (bridge != null && !bridge.Active) { bridge.Dispose(); bridge = null; } }
                else if (m == "app-update-exit") { App.Log("update: closing for the installer"); Close(); }   // the installer waits for this window to close (installer.iss)
                else if (m == "console-backup-open") OpenConsoleBackup();   // Console Backup & Restore (the first screen's button and the Studio menu)
                return;
            }
            if (!source.StartsWith("about:")) return;
            string cmd = ""; try { var m = Regex.Match(e.WebMessageAsJson ?? "", "\"cmd\"\\s*:\\s*\"([a-z]+)\""); if (m.Success) cmd = m.Groups[1].Value; } catch { }
            switch (cmd)
            {
                case "retry": LoadInterface("try again", true); break;
                case "network": try { Process.Start(new ProcessStartInfo("ms-settings:network-status") { UseShellExecute = true }); } catch { } break;
                case "diagnostics": ShowDiagnostics(); break;
                case "exit": Close(); break;
            }
        }

        // the Preview opened again while the previous one's player is still ending (closed a moment ago): wait for it (bounded, without
        // blocking this window's thread), then open; one player at a time. A player that does not end is reported as busy.
        async Task OpenBridge(int gen)
        {
            var old = bridge;
            if (old != null && old.Active)
            {
                old.LetGo();   // its own late "ended" is not this new Preview's
                bool ended = await Task.Run(() => old.WaitEnded(10000));
                if (gen != openGen) return;
                if (!ended) { _ = web.CoreWebView2.ExecuteScriptAsync("window.__ccsPlayerBusy && window.__ccsPlayerBusy()"); return; }
            }
            if (gen != openGen) return;
            bridge?.Dispose(); bridge = new PlayerBridge(this, web.CoreWebView2, wvEnv); bridge.Open();
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
        //   classiccartstudio://share/<id>                a retired public share link: the page explains that shared links are no longer used
        //   classiccartstudio://handoff?offer=<id>        a collection the website offered (the existing inbox)
        //   classiccartstudio://package?offer=<id>        a .ccs package the website handed to this computer's backend (staged there)
        //   <path>.ccs                                    a collection package opened from File Explorer (double-click, Open With)
        //   --check-updates                               the update check, as Check for Updates (CCS Console Workbench started from this
        //                                                 installation asks for it: Update Classic Cart Studio…)
        // No other link type is accepted. Every value is validated; nothing in a link is ever run as a command.
        public void OnActivatedFromAnotherInstance(string payload)
        {
            App.Log("activated by another instance"); BringToFrontNow();
            foreach (var line in (payload ?? "").Split('\n')) HandleLink(line.Trim());
        }

        void HandleLink(string arg)
        {
            if (string.IsNullOrEmpty(arg)) return;
            if (arg == "--check-updates") { pendingUpdateCheck = true; App.Log("update check asked for"); DeliverPendingLink(); return; }
            if (arg.EndsWith(".ccs", StringComparison.OrdinalIgnoreCase) && arg.IndexOf("://", StringComparison.Ordinal) < 0)
            {
                string full = null;
                try { full = System.IO.Path.GetFullPath(arg); } catch { }
                if (full != null && System.IO.File.Exists(full)) { App.Log("package file opened (" + System.IO.Path.GetFileName(full).Length + " characters)"); QueueAction("file", full); }
                else App.Log("package file not found");
                return;
            }
            var m = Regex.Match(arg, @"^classiccartstudio://([a-z]+)(?:/([A-Za-z0-9]{6,32}))?/?(?:\?(?:.*&)?(?:offer|id)=([A-Za-z0-9_-]{8,64}))?(?:&.*)?$", RegexOptions.IgnoreCase);
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
            if (pendingUpdateCheck) { pendingUpdateCheck = false; _ = web.CoreWebView2.ExecuteScriptAsync("window.__ccsCheckUpdates&&window.__ccsCheckUpdates()"); }
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
        internal string WebViewRuntime() { var r = Runtime.Find(); return r.Kind == "default" ? r.Version : r.Kind == "folder" ? r.Version + " (from " + r.Folder + ")" : r.Kind == "old" ? "too old: " + r.Version : "not found"; }
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

        // ---- CCS Console Workbench: the program installed with Studio (workbench\CCS-Console-Workbench.exe, the same file as the standalone
        // release), started as its own program in installed mode (WorkbenchLauncher.cs); an open one comes forward instead. It needs no
        // collection, game or console setup, keeps running when Studio closes, and owns its own close, console tasks and USB driver question.
        internal void OpenConsoleBackup()
        {
            try
            {
                var problem = WorkbenchLauncher.Open(startupArgs);
                if (problem != null) { App.Log("console workbench: " + problem); MessageDialog.Show(this, "CCS Console Workbench", problem, WorkbenchLauncher.ExePath); }
            }
            catch (Exception ex) { App.Log("console workbench failed: " + ex); MessageDialog.Show(this, "CCS Console Workbench", "CCS Console Workbench could not open. Try again. If it happens again, run the Classic Cart Studio installer again to repair it; your collections, backups and settings are kept.", ex.Message); }
        }

        void OnClosingWindow(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (closing) return;
            // F6: the games and HOME this window owns close with it, after asking (Cancel is the default)
            if (!exitConfirmed && bridge != null && bridge.Active)
            {
                e.Cancel = true; App.Log("close asked while the Preview's player is open");
                Dispatcher.BeginInvoke(new Action(() => { if (bridge.ConfirmStudioExit(this)) { exitConfirmed = true; Close(); } }));
                return;
            }
            if (!exitConfirmed && OtherPlayerOpen())
            {
                e.Cancel = true;
                Dispatcher.BeginInvoke(new Action(() => { if (PlayerBridge.Ask(this, "Close Classic Cart Studio?", "A game window from Classic Cart Studio is open. Closing Classic Cart Studio closes it too: it writes the game's own cartridge save and ends. Anything you have not saved to a save slot is lost.", "Keep Studio Open", "Close Studio")) { exitConfirmed = true; Close(); } }));
                return;
            }
            closing = true; SavePlacement();
            App.Log("closing");
            try { bridge?.Dispose(); } catch { }
            try { backend.Stop(); } catch { }
        }
        // a game window the backend started outside the Preview (Play on PC from older pages or tests): it ends when this window is gone
        bool OtherPlayerOpen()
        {
            try { var r = backend.Ready ? backend.Call("GET", "/api/play/status", null, 1500) : null; return r != null && Regex.IsMatch(r, "\"running\"\\s*:\\s*true"); } catch { return false; }
        }
    }
}
