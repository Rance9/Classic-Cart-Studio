// Classic Cart Studio console backup and restore window. Copyright (C) 2026 Rance9. GPL-3.0-or-later.
// CCS Console Workbench: its version in the window title, and Check for Updates. One quiet check per session as soon as the window is
// shown (no wait, no window over the page, a short time limit), and Check for Updates in Help and About (About Workbench and the Help page);
// the status line at the bottom shows "Update available" when a newer version is published. The answer is one of Update available, Up to
// date or Could not check (no answer, a slow one, or one that cannot be used: never "Up to date").
// The one request: the website's release information (UpdateFeed, release-streams.json on classiccartstudio.com; in tests only, a loopback
// server named by CCS_WORKBENCH_UPDATE_FEED), with a User-Agent naming this program and its version. Nothing about the console, the
// backups, the settings or this PC is sent; nothing is downloaded, installed or saved; no setting, service or schedule is added.
//   Portable (no Classic Cart Studio launch context): this program's own product and channel (a preview is offered only newer previews);
//   Open the Download Page opens classiccartstudio.com/app/ in the web browser. The Workbench never replaces or updates itself.
//   Started by Classic Cart Studio from its installation (opt.Studio): the Workbench is updated with Classic Cart Studio, so the check reads
//   Classic Cart Studio's releases of Classic Cart Studio's channel, compared with Classic Cart Studio's version and this Workbench's (the
//   bundle), and says "Classic Cart Studio update available"; Update Classic Cart Studio… asks Classic Cart Studio's own update
//   (ClassicCartStudio.exe --check-updates: the running Classic Cart Studio, or a new start when its window has been closed).
// The decision itself is the engine's (CcsBackup.Library.ReleaseStreams), the same rules as Classic Cart Studio's (app_update.py).
using CcsBackup.Library;
using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace CcsBackup.Ui
{
    public sealed partial class BackupWorkspace
    {
        public const string UpdateFeed = "https://classiccartstudio.com/release-streams.json";
        public const string DownloadPage = "https://classiccartstudio.com/app/";

        /// <summary>the window's title, in its own title bar and in Windows (the taskbar, Alt+Tab): this program's own version and channel,
        /// also when Classic Cart Studio started it ("CCS Console Workbench — v0.11.4 (Preview)")</summary>
        internal string WindowTitle => ProductTitle + " — v" + VersionLabel;
        bool IsPreview => opt.Channel != "production";
        /// <summary>this program's version as people see it: "0.11.4 (Preview)", or "1.0.0" for a production release</summary>
        internal string VersionLabel => opt.Version + (IsPreview ? " (Preview)" : "");
        static string Label(string version, string channel) => version + (channel == "production" ? "" : " (Preview)");

        /// <summary>the address the check reads: the website's release information, or (tests only) a loopback server</summary>
        static string FeedAddress()
        {
            var f = Environment.GetEnvironmentVariable("CCS_WORKBENCH_UPDATE_FEED");
            return f != null && Regex.IsMatch(f, @"^http://127\.0\.0\.1:\d{2,5}/$") ? f + "release-streams.json" : UpdateFeed;
        }

        /// <summary>the request (tests may replace it): the text at url, or an exception</summary>
        internal static Func<string, string, int, Task<string>> FetchText = (url, agent, ms) => Task.Run(() =>
        {
            var req = (HttpWebRequest)WebRequest.Create(url);
            req.Method = "GET"; req.UserAgent = agent; req.Accept = "application/json"; req.Timeout = ms; req.ReadWriteTimeout = ms;
            req.AllowAutoRedirect = false; req.CachePolicy = new System.Net.Cache.RequestCachePolicy(System.Net.Cache.RequestCacheLevel.NoCacheNoStore);
            using (var resp = (HttpWebResponse)req.GetResponse())
            {
                if (resp.StatusCode != HttpStatusCode.OK) throw new WebException("status " + (int)resp.StatusCode);
                using (var s = resp.GetResponseStream())
                using (var m = new MemoryStream())
                {
                    var buf = new byte[65536]; int n;
                    while ((n = s.Read(buf, 0, buf.Length)) > 0) { m.Write(buf, 0, n); if (m.Length > 2_000_000) throw new InvalidDataException("the answer is too large"); }
                    return new UTF8Encoding(false, true).GetString(m.ToArray());
                }
            }
        });

        /// <summary>asks Classic Cart Studio (its installed program) for its own update check (tests may replace it); false when it could not
        /// be started</summary>
        internal static Func<string, bool> StartStudioUpdate = exe =>
        {
            try { Process.Start(new ProcessStartInfo(exe, "--check-updates") { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(exe) }); return true; }
            catch (Exception) { return false; }
        };

        UpdateAnswer updAnswer; bool updChecking, updStarted;
        /// <summary>raised (on the window's thread) when a check starts or ends; Help and About and the status line redraw their part</summary>
        event Action UpdateChanged;

        /// <summary>the quiet check, once per session, as soon as the window is shown (WorkspaceOptions.CheckForUpdates)</summary>
        void HookUpdates()
        {
            ContentRendered += (s, e) => { if (opt.CheckForUpdates && !updStarted) StartUpdateCheck(); };
        }

        /// <summary>Classic Cart Studio, when it started this program from its installation and named itself well enough to update</summary>
        bool StudioUpdates => opt.Studio != null;

        void StartUpdateCheck()
        {
            if (updChecking) return;
            updStarted = updChecking = true; UpdateChanged?.Invoke();
            string product = StudioUpdates ? "app" : "workbench", channel = StudioUpdates ? opt.Studio.Channel : opt.Channel, running = StudioUpdates ? opt.Studio.Version : opt.Version;
            string installed = StudioUpdates ? opt.Version : null;
            Task<string> t;
            try { t = FetchText(FeedAddress(), "CCS-Console-Workbench/" + opt.Version, 8000); }
            catch (Exception) { t = Task.FromResult<string>(null); }
            var limit = Task.Delay(10000);
            Task.WhenAny(t, limit).ContinueWith(_ =>
            {
                UpdateAnswer a;
                if (t.Status == TaskStatus.RanToCompletion && t.Result != null) a = ReleaseStreams.Plan(t.Result, product, channel, running, installed);
                else { var ex = t.Exception?.GetBaseException(); a = new UpdateAnswer { State = UpdateState.CouldNotCheck, Product = product, Channel = channel, Running = running, Problem = t.IsCompleted ? (ex != null ? ex.GetType().Name + ": " + ex.Message : "no answer") : "no answer in time" }; }
                try { Dispatcher.BeginInvoke(new Action(() => { updAnswer = a; updChecking = false; UpdateChanged?.Invoke(); })); } catch (Exception) { }
            });
        }

        /// <summary>the update part of About Workbench and of the Help page: this version, the answer, Check for Updates and the one action</summary>
        FrameworkElement UpdatePanel()
        {
            var host = new ContentControl(); SetNoTerms(host, true);
            void Draw() { host.Content = UpdateBody(); }
            Action h = () => Draw();
            host.Loaded += (s, e) => { UpdateChanged -= h; UpdateChanged += h; Draw(); };
            host.Unloaded += (s, e) => UpdateChanged -= h;
            Draw();
            return host;
        }

        UIElement UpdateBody()
        {
            var sp = new StackPanel { Margin = new Thickness(0, 4, 0, 0) };
            sp.Children.Add(T("Updates", Ty.Heading, true, false, new Thickness(0, 6, 0, 4)));
            sp.Children.Add(T("This version: " + ProductTitle + " " + VersionLabel + (StudioUpdates ? ", installed with Classic Cart Studio " + Label(opt.Studio.Version, opt.Studio.Channel) : "") + ".", Ty.Text));
            string status; string kind = null;
            var a = updAnswer;
            if (updChecking) status = "Checking for updates…";
            else if (a == null) status = "Not checked yet.";
            else if (a.State == UpdateState.Available) { kind = "ok"; status = (StudioUpdates ? "Classic Cart Studio update available: version " : "Update available: version ") + Label(a.Newest, a.Channel) + "."; }
            else if (a.State == UpdateState.UpToDate) status = "Up to date." + (a.Ahead ? " This version is newer than the newest published one." : "");
            else { kind = "warn"; status = "Could not check. Workbench works the same without it. Check the internet connection and try again."; }
            var line = kind != null ? (UIElement)Note(status, kind) : T(status, Ty.Text, true);
            AutomationProperties.SetName(line, status);
            sp.Children.Add(line);
            var problem = new ContentControl();
            var check = B("Check for Updates", "Btn", () => StartUpdateCheck(), !updChecking);
            Button act = null;
            if (!updChecking && a != null && a.State == UpdateState.Available)
                act = StudioUpdates
                    ? B("Update Classic Cart Studio…", "Btn.Primary", () => problem.Content = StudioExeOk() && StartStudioUpdate(opt.Studio.Exe) ? Note("Classic Cart Studio opens its update. Its window shows the next step.", "info") : Note("Classic Cart Studio could not be opened from here. Open Classic Cart Studio and choose Check for Updates in its menu.", "warn"))
                    : B("Open the Download Page", "Btn.Primary", () => problem.Content = Launch(DownloadPage) ? null : Note("The web browser could not be opened. The address is " + DownloadPage, "warn"));
            sp.Children.Add(act != null ? Buttons(act, check) : Buttons(check));
            sp.Children.Add(problem);
            sp.Children.Add(T(StudioUpdates
                ? "Workbench is updated with Classic Cart Studio. The check asks classiccartstudio.com which version of Classic Cart Studio is newest. It sends only Workbench's name and version: nothing about your console, your backups or this PC. Nothing is downloaded or installed here."
                : "The check asks classiccartstudio.com which version is newest" + (IsPreview ? " (a preview version is offered only newer preview versions)" : "") + ". It sends only Workbench's name and version: nothing about your console, your backups or this PC. Workbench never downloads or installs anything itself.", Ty.Small, false, true));
            return sp;
        }

        /// <summary>the program Classic Cart Studio named when it started this one: a full path to its existing ClassicCartStudio.exe</summary>
        bool StudioExeOk()
        {
            var exe = opt.Studio?.Exe;
            try { return !string.IsNullOrEmpty(exe) && Path.IsPathRooted(exe) && string.Equals(Path.GetFileName(exe), "ClassicCartStudio.exe", StringComparison.OrdinalIgnoreCase) && File.Exists(exe); }
            catch (Exception) { return false; }
        }

        /// <summary>the status line's "Update available": shown only when a newer version is published; it opens About Workbench</summary>
        UIElement UpdateIndicator()
        {
            var b = new Button { Style = Res<Style>("Btn.Link"), Visibility = Visibility.Collapsed, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 12, 0), FontSize = Ty.Fine };
            void Draw()
            {
                bool on = !updChecking && updAnswer != null && updAnswer.State == UpdateState.Available;
                b.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
                if (on) { var t = (StudioUpdates ? "Classic Cart Studio update available: " : "Update available: ") + Label(updAnswer.Newest, updAnswer.Channel); b.Content = t; AutomationProperties.SetName(b, t); b.ToolTip = "Open About Workbench"; }
            }
            UpdateChanged += Draw;
            b.Click += (s, e) => { helpTopic = "about-app"; if (section == "Help and About" && !Busy) ShowHelp(); else Go("Help and About"); };
            Draw();
            return b;
        }
    }
}
