// The one application window: WebView2 showing the live Studio website, a small Studio menu (About, Diagnostics, data folder,
// updates, exit), the recovery screen when the website cannot load, the desktop token injection for the approved origin only,
// navigation filtering (anything else opens in the user's browser) and the nesstudio:// link handling.
using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace ClassicCartStudio
{
    internal class MainForm : Form
    {
        readonly WebView2 web = new WebView2();
        readonly Backend backend = new Backend();
        readonly Settings settings = Settings.Load();
        readonly MenuStrip menu = new MenuStrip();
        readonly StatusStrip status = new StatusStrip();
        readonly ToolStripStatusLabel statusText = new ToolStripStatusLabel("Starting…");
        readonly string[] startupArgs;
        bool coreReady, backendStarting, closing, recoveryShown;
        string pendingLink;
        int devtoolsPort;

        public MainForm(string[] args)
        {
            startupArgs = args ?? new string[0];
            foreach (var a in startupArgs) { var m = Regex.Match(a, @"^--devtools-port=(\d{4,5})$"); if (m.Success) devtoolsPort = int.Parse(m.Groups[1].Value); }
            Text = Program.ProductName; Icon = TryIcon(); BackColor = Color.FromArgb(16, 16, 20);
            StartPosition = FormStartPosition.Manual; ApplyPlacement();
            menu.Items.Add(BuildStudioMenu()); menu.BackColor = Color.FromArgb(24, 24, 30); menu.ForeColor = Color.White; menu.RenderMode = ToolStripRenderMode.System;
            status.Items.Add(statusText); status.BackColor = Color.FromArgb(24, 24, 30); status.ForeColor = Color.Gainsboro; status.SizingGrip = true;
            web.Dock = DockStyle.Fill; web.BackColor = Color.FromArgb(16, 16, 20); web.DefaultBackgroundColor = Color.FromArgb(16, 16, 20);
            Controls.Add(web); Controls.Add(status); Controls.Add(menu); MainMenuStrip = menu;
            Load += async (s, e) => await InitAsync();
            FormClosing += OnClosing; ResizeEnd += (s, e) => SavePlacement(); LocationChanged += (s, e) => { if (WindowState == FormWindowState.Normal) SavePlacement(); };
        }

        static Icon TryIcon()
        {
            try { return Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { return null; }
        }

        ToolStripMenuItem BuildStudioMenu()
        {
            var m = new ToolStripMenuItem("&Studio");
            m.DropDownItems.Add("Reload the website", null, (s, e) => Navigate());
            m.DropDownItems.Add("Open the website in your browser", null, (s, e) => App.OpenExternal(App.StartUrl));
            m.DropDownItems.Add(new ToolStripSeparator());
            m.DropDownItems.Add("Open the data folder", null, (s, e) => { try { Directory.CreateDirectory(App.DataRoot); Process.Start(new ProcessStartInfo("explorer.exe", "\"" + App.DataRoot + "\"") { UseShellExecute = true }); } catch { } });
            m.DropDownItems.Add("Diagnostics…", null, (s, e) => ShowDiagnostics());
            m.DropDownItems.Add("Check for updates (releases page)", null, (s, e) => App.OpenExternal("https://github.com/Rance9/NES-Classic-Collection-Studio/releases"));
            m.DropDownItems.Add(new ToolStripSeparator());
            m.DropDownItems.Add("About Classic Cart Studio", null, (s, e) => ShowAbout());
            m.DropDownItems.Add("E&xit", null, (s, e) => Close());
            return m;
        }

        async Task InitAsync()
        {
            try
            {
                Directory.CreateDirectory(App.DataRoot);
                App.Log("start " + App.Version + " args=" + string.Join(" ", startupArgs));
                var opts = new CoreWebView2EnvironmentOptions();
                var extra = App.Dev ? (Environment.GetEnvironmentVariable("CCS_BROWSER_ARGS") ?? "") : "";   // development experiments only
                if (devtoolsPort > 0 || extra.Length > 0) opts.AdditionalBrowserArguments = ((devtoolsPort > 0 ? "--remote-debugging-port=" + devtoolsPort + " " : "") + extra).Trim();
                var env = await CoreWebView2Environment.CreateAsync(null, App.WebViewDataFolder, opts);
                await web.EnsureCoreWebView2Async(env);
                var core = web.CoreWebView2;
                core.Settings.IsStatusBarEnabled = false; core.Settings.AreDevToolsEnabled = App.Dev || devtoolsPort > 0; core.Settings.IsZoomControlEnabled = true;
                core.Settings.AreDefaultContextMenusEnabled = App.Dev; core.Settings.IsPasswordAutosaveEnabled = false; core.Settings.IsGeneralAutofillEnabled = false;
                core.Settings.UserAgent = core.Settings.UserAgent + " ClassicCartStudio/" + App.Version;
                core.NavigationStarting += OnNavigationStarting; core.NewWindowRequested += OnNewWindow; core.NavigationCompleted += OnNavigationCompleted;
                core.WebMessageReceived += OnWebMessage; core.PermissionRequested += OnPermission; core.DocumentTitleChanged += (s, e) => { var t = core.DocumentTitle; Text = string.IsNullOrEmpty(t) ? Program.ProductName : (t.Contains(Program.ProductName) ? t : Program.ProductName + " — " + t); };
                core.ProcessFailed += (s, e) => { App.Log("webview process failed: " + e.ProcessFailedKind); ShowRecovery("The Studio window stopped responding.", "The web content process ended (" + e.ProcessFailedKind + "). Choose Try Again to reload.", e.ProcessFailedKind.ToString()); };
                coreReady = true;
                statusText.Text = "Starting the Studio backend…";
                // Start the backend first and give it a moment: a page that probes a port nobody listens on makes Chromium back off from
                // that host for 10-60 s (measured), which would delay every later request. The page is told whether the backend is ready
                // and otherwise waits for the host's signal instead of probing.
                var startTask = Task.Run(() => StartBackend());
                await Task.WhenAny(startTask, Task.Delay(4000));
                await core.AddScriptToExecuteOnDocumentCreatedAsync(DesktopBootstrapScript(backend.Ready));
                Navigate();
            }
            catch (Exception e)
            {
                App.Log("webview init failed: " + e);
                var msg = "Microsoft Edge WebView2 could not be started. Classic Cart Studio needs the WebView2 runtime that comes with Microsoft Edge on Windows 10/11.";
                MessageBox.Show(this, msg + "\n\n" + e.Message, Program.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                statusText.Text = "WebView2 unavailable";
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
                BeginInvoke(new Action(() =>
                {
                    statusText.Text = ok ? ("Backend ready · version " + backend.BackendVersion + " · port " + backend.Port) : ("Backend problem: " + backend.LastError);
                    if (!ok) ShowBackendProblem();
                    else if (coreReady && web.CoreWebView2 != null) { _ = web.CoreWebView2.ExecuteScriptAsync("window.__ccsBackendReady && window.__ccsBackendReady()"); DeliverPendingLink(); }
                    if (ok) _ = Task.Run(async () => { await Task.Delay(1500); try { BeginInvoke(new Action(() => { if (web.CoreWebView2 != null) _ = web.CoreWebView2.ExecuteScriptAsync("window.__ccsBackendReady && window.__ccsBackendReady()"); })); } catch { } });
                }));
            }
            finally { backendStarting = false; }
        }

        void ShowBackendProblem()
        {
            var r = MessageBox.Show(this, backend.LastError + "\n\nTry again?", Program.ProductName + " — backend", MessageBoxButtons.RetryCancel, MessageBoxIcon.Warning);
            if (r == DialogResult.Retry) { statusText.Text = "Starting the Studio backend…"; _ = Task.Run(() => StartBackend()); }
        }

        void Navigate()
        {
            if (!coreReady) return; recoveryShown = false;
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
            if (e.IsSuccess) { if (!recoveryShown) statusText.Text = backend.Ready ? ("Backend ready · version " + backend.BackendVersion) : statusText.Text; if (backend.Ready) _ = web.CoreWebView2.ExecuteScriptAsync("window.__ccsBackendReady && window.__ccsBackendReady()"); DeliverPendingLink(); return; }
            var src = web.CoreWebView2.Source ?? "";
            if (src.StartsWith("about:") || src.StartsWith("data:")) return;
            App.Log("navigation failed: " + e.WebErrorStatus + " for " + src);
            ShowRecovery("The Studio website couldn't be reached.", null, e.WebErrorStatus + " — " + src);
        }

        void ShowRecovery(string title, string message, string details)
        {
            if (!coreReady) return; recoveryShown = true;
            try
            {
                string html;
                using (var s = typeof(MainForm).Assembly.GetManifestResourceStream("Recovery.html")) using (var r = new StreamReader(s, Encoding.UTF8)) html = r.ReadToEnd();
                web.CoreWebView2.NavigateToString(html);
                var info = "{\"title\":" + JsonStr(title) + ",\"message\":" + JsonStr(message ?? "") + ",\"details\":" + JsonStr(details ?? "") + "}";
                web.CoreWebView2.NavigationCompleted += (s2, e2) => { if (recoveryShown) _ = web.CoreWebView2.ExecuteScriptAsync("window.__ccsRecovery && window.__ccsRecovery(" + App.JsString(info) + ")"); };
                statusText.Text = title;
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
            try { if (WindowState == FormWindowState.Minimized) WindowState = FormWindowState.Normal; Activate(); TopMost = true; TopMost = false; } catch { }
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            foreach (var a in startupArgs) HandleLink(a);
        }

        void ShowAbout()
        {
            MessageBox.Show(this, Program.ProductName + " " + App.Version + "\nBackend: " + (backend.Ready ? backend.BackendVersion : "not running") + " on 127.0.0.1:" + backend.Port +
                "\nWebsite: " + App.StartUrl + "\nData folder: " + App.DataRoot +
                "\n\nClassic Cart Studio is an independent, open-source project and is not produced, licensed, sponsored, approved, or endorsed by Nintendo. Nintendo, Nintendo Entertainment System, NES and NES Classic Edition are trademarks of Nintendo.",
                "About " + Program.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        void ShowDiagnostics()
        {
            var st = backend.Ready ? (backend.Call("GET", "/api/status", null, 5000) ?? "(no answer)") : "(backend not running: " + backend.LastError + ")";
            var ver = backend.Ready ? (backend.Call("GET", "/api/version", null, 3000) ?? "") : "";
            var text = "Classic Cart Studio " + App.Version + "\nWebView2 runtime: " + SafeRuntimeVersion() + "\nWebsite: " + App.StartUrl + "\nBackend: 127.0.0.1:" + backend.Port + (backend.Process != null && !backend.Process.HasExited ? " (pid " + backend.Process.Id + ")" : "") +
                       "\nData folder: " + App.DataRoot + "\nLog: " + App.LogPath + "\n\n/api/version\n" + Trim(ver, 600) + "\n\n/api/status\n" + Trim(st, 1600);
            using (var f = new Form { Text = "Diagnostics — " + Program.ProductName, Width = 760, Height = 560, StartPosition = FormStartPosition.CenterParent, Icon = Icon })
            {
                var tb = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Both, Dock = DockStyle.Fill, Font = new Font("Consolas", 9.5f), Text = text, BackColor = Color.FromArgb(16, 16, 20), ForeColor = Color.Gainsboro, WordWrap = false };
                f.Controls.Add(tb); f.ShowDialog(this);
            }
        }

        string SafeRuntimeVersion() { try { return CoreWebView2Environment.GetAvailableBrowserVersionString(); } catch { return "not found"; } }
        static string Trim(string s, int n) { s = s ?? ""; return s.Length > n ? s.Substring(0, n) + "…" : s; }

        void ApplyPlacement()
        {
            var area = Screen.PrimaryScreen.WorkingArea; int w = settings.W > 0 ? settings.W : Math.Min(1440, area.Width - 80), h = settings.H > 0 ? settings.H : Math.Min(920, area.Height - 60);
            var r = new Rectangle(settings.X, settings.Y, w, h); bool visible = false;
            foreach (var s in Screen.AllScreens) if (s.WorkingArea.IntersectsWith(r) && s.WorkingArea.Contains(new Point(r.X + 40, r.Y + 20))) visible = true;
            if (settings.X < 0 || settings.Y < 0 || !visible) r = new Rectangle(area.X + (area.Width - w) / 2, area.Y + (area.Height - h) / 2, w, h);
            Bounds = r; if (settings.Maximized) WindowState = FormWindowState.Maximized; MinimumSize = new Size(720, 480);
        }

        void SavePlacement()
        {
            if (closing) return;
            settings.Maximized = WindowState == FormWindowState.Maximized;
            var b = WindowState == FormWindowState.Normal ? Bounds : RestoreBounds; settings.X = b.X; settings.Y = b.Y; settings.W = b.Width; settings.H = b.Height; settings.Save();
        }

        void OnClosing(object sender, FormClosingEventArgs e)
        {
            if (closing) return; closing = true; SavePlacement();
            statusText.Text = "Stopping…"; App.Log("closing");
            try { backend.Stop(); } catch { }
        }
    }
}
