// Shared facts for the host: version, approved origins, folders, settings and the small JSON helpers it needs.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

namespace ClassicCartStudio
{
    internal static class App
    {
        // The app icon at the size it is drawn. app.ico holds code-drawn frames from 16 to 256 px, but a WPF Image given the .ico shows only its
        // first (16 px) frame, stretched: a blurry controller on the startup screen (48 DIP is 96 device pixels at 200 %). This is the smallest
        // frame at least as large as the drawn size in device pixels (scaled down, never up), or else the largest one.
        public static System.Windows.Media.Imaging.BitmapSource IconFrame(double dip, double scale)
        {
            var px = (int)Math.Ceiling(dip * (scale > 0 ? scale : 1));
            using (var s = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/app.ico")).Stream)
            {
                var dec = new System.Windows.Media.Imaging.IconBitmapDecoder(s, System.Windows.Media.Imaging.BitmapCreateOptions.PreservePixelFormat, System.Windows.Media.Imaging.BitmapCacheOption.OnLoad);
                System.Windows.Media.Imaging.BitmapFrame best = null, largest = null;
                foreach (var f in dec.Frames) { if (largest == null || f.PixelWidth > largest.PixelWidth) largest = f; if (f.PixelWidth >= px && (best == null || f.PixelWidth < best.PixelWidth)) best = f; }
                var r = best ?? largest; if (r.CanFreeze) r.Freeze(); return r;
            }
        }

        /// <summary>--simulated-console FOLDER: Console Backup & Restore with a simulated console (tests and demonstrations only; files on the PC
        /// stand in for a console, and every backup made from it is marked simulated)</summary>
        public static string SimulatedConsoleArg(string[] args)
        {
            int i = Array.IndexOf(args ?? new string[0], "--simulated-console");
            return i >= 0 && i + 1 < args.Length && Directory.Exists(args[i + 1]) ? args[i + 1] : null;
        }

        /// <summary>--backup-settings FILE: Console Backup & Restore's settings file (its backup folders) instead of the shared one in
        /// %LOCALAPPDATA%\CCS Console Backup (tests and demonstrations, so they never write into the person's own backup folders; the
        /// Utility's --settings is the same)</summary>
        public static string BackupSettingsArg(string[] args)
        {
            int i = Array.IndexOf(args ?? new string[0], "--backup-settings");
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }

        public static string Version
        {
            get
            {
                var a = Assembly.GetExecutingAssembly();
                var v = a.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
                if (string.IsNullOrEmpty(v)) v = a.GetName().Version?.ToString() ?? "0.0.0";
                var i = v.IndexOf('+'); return i > 0 ? v.Substring(0, i) : v;
            }
        }

        /// <summary>this release's channel, "preview" or "production" (the repository's CHANNEL file, recorded at build time); a build
        /// without it is treated as a preview</summary>
        public static string Channel
        {
            get
            {
                foreach (var m in Assembly.GetExecutingAssembly().GetCustomAttributes<AssemblyMetadataAttribute>())
                    if (m.Key == "ReleaseChannel" && (m.Value == "preview" || m.Value == "production")) return m.Value;
                return "preview";
            }
        }
        public static bool IsPreview => Channel != "production";
        /// <summary>the version as people see it: "0.11.4 (Preview)", or "1.0.0" for a production release</summary>
        public static string VersionLabel => Version + (IsPreview ? " (Preview)" : "");
        /// <summary>the main window's title (its own title bar and the taskbar): the running version, never another product's</summary>
        public static string WindowTitle => "Classic Cart Studio — v" + VersionLabel;

        // The production website. The github.io project URL is a legacy redirect, not a production dependency.
        public const string CanonicalOrigin = "https://classiccartstudio.com";
        public const string SiteUrl = CanonicalOrigin + "/";
        // where Open Website takes a person: the usable site. During the pre-launch period the root is a coming-soon page and the site is at
        // /preview/; at the launch the host redirects /preview/ to / permanently, so this address keeps working (LAUNCH-RUNBOOK.md)
        public const string WebsiteUrl = CanonicalOrigin + "/preview/";
        public static readonly string[] SiteOrigins = { CanonicalOrigin, "https://www.classiccartstudio.com" };
        public const string RepoUrl = "https://github.com/Rance9/Classic-Cart-Studio";
        public const string UpdateUrl = "https://ClassicCartStudio.com/app";

        // Data folder: %LOCALAPPDATA%\ClassicCartStudio (the earlier releases' NESClassicCollectionStudio folder is moved there once, on first start)
        public static string DataRoot
        {
            get
            {
                var env = Environment.GetEnvironmentVariable("NES_STUDIO_DATA");
                if (!string.IsNullOrEmpty(env)) return env;
                var la = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                var now = Path.Combine(la, "ClassicCartStudio"); var old = Path.Combine(la, "NESClassicCollectionStudio");
                if (!Directory.Exists(now) && Directory.Exists(old)) { try { Directory.Move(old, now); } catch { return old; } }
                return now;
            }
        }
        public static string AppDir => AppDomain.CurrentDomain.BaseDirectory;
        public static string LogPath => Path.Combine(DataRoot, "logs", "desktop.log");
        public static string SettingsPath => Path.Combine(DataRoot, "desktop.json");
        public static string WebViewDataFolder => Path.Combine(DataRoot, "webview2");

        // Development: CCS_DEV=1 lets a local preview origin (http://localhost:<port> or http://127.0.0.1:<port>) act as the site.
        public static bool Dev => Environment.GetEnvironmentVariable("CCS_DEV") == "1";
        public static string DevSiteUrl => Environment.GetEnvironmentVariable("CCS_SITE_URL");

        // Offline first (0.5.0): the application's interface is the copy bundled with this installation, served by its own backend on
        // 127.0.0.1 (the frozen backend's _internal\site). The public website is a separate delivery of the same frontend: links to it
        // open in the browser, and a website deployment never changes what this installation shows.
        public static string LocalOrigin { get; set; } = "http://127.0.0.1:8765";   // set from the backend this window starts

        public static string StartUrl
        {
            get
            {
                if (Dev && !string.IsNullOrEmpty(DevSiteUrl) && Regex.IsMatch(DevSiteUrl, @"^http://(localhost|127\.0\.0\.1):\d{2,5}/")) return DevSiteUrl;
                return LocalOrigin + "/";
            }
        }

        public static string OriginOf(string url)
        {
            try { var u = new Uri(url); return u.Scheme + "://" + u.Host + ((u.IsDefaultPort) ? "" : ":" + u.Port); } catch { return ""; }
        }

        public static bool IsApprovedOrigin(string origin)
        {
            if (string.IsNullOrEmpty(origin)) return false;
            if (string.Equals(LocalOrigin, origin, StringComparison.OrdinalIgnoreCase)) return true;
            if (Dev && !string.IsNullOrEmpty(DevSiteUrl) && string.Equals(OriginOf(DevSiteUrl), origin, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        public static void Log(string line)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(LogPath));
                var fi = new FileInfo(LogPath);
                if (fi.Exists && fi.Length > 512 * 1024) { try { File.Copy(LogPath, LogPath + ".1", true); File.Delete(LogPath); } catch { } }
                File.AppendAllText(LogPath, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " " + line + Environment.NewLine, Encoding.UTF8);
            }
            catch { }
        }

        public static void OpenExternal(string url)
        {
            // only web links leave the application; anything else is ignored (no shell commands, no file paths)
            if (string.IsNullOrEmpty(url)) return;
            if (!(url.StartsWith("https://", StringComparison.OrdinalIgnoreCase) || url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || url.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase))) return;
            try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch (Exception e) { Log("open external failed: " + e.Message); }
        }

        public static string JsString(string s)
        {
            var sb = new StringBuilder("'");
            foreach (var c in s ?? "")
            {
                if (c == '\\' || c == '\'') sb.Append('\\').Append(c);
                else if (c == '\n') sb.Append("\\n"); else if (c == '\r') sb.Append("\\r");
                else if (c < 32 || c == '<' || c == '>' || c == '&' || c > 126) sb.Append("\\u").Append(((int)c).ToString("x4"));
                else sb.Append(c);
            }
            return sb.Append('\'').ToString();
        }
    }

    // desktop.json: window placement and the controlled site override. Tiny hand-written JSON (no dependencies).
    internal class Settings
    {
        public int X = -1, Y = -1, W = 0, H = 0; public bool Maximized; public string SiteUrl = "";
        static readonly Regex Num = new Regex("\"(x|y|w|h)\"\\s*:\\s*(-?\\d+)"); static readonly Regex Bool = new Regex("\"maximized\"\\s*:\\s*(true|false)"); static readonly Regex Site = new Regex("\"site_url\"\\s*:\\s*\"([^\"]*)\"");
        public static Settings Load()
        {
            var s = new Settings();
            try
            {
                if (!File.Exists(App.SettingsPath)) return s;
                var t = File.ReadAllText(App.SettingsPath);
                foreach (Match m in Num.Matches(t)) { var v = int.Parse(m.Groups[2].Value); switch (m.Groups[1].Value) { case "x": s.X = v; break; case "y": s.Y = v; break; case "w": s.W = v; break; case "h": s.H = v; break; } }
                var b = Bool.Match(t); if (b.Success) s.Maximized = b.Groups[1].Value == "true";
                var u = Site.Match(t); if (u.Success) s.SiteUrl = u.Groups[1].Value;
            }
            catch { }
            return s;
        }
        public void Save()
        {
            try
            {
                Directory.CreateDirectory(App.DataRoot);
                var site = string.IsNullOrEmpty(SiteUrl) ? "" : SiteUrl.Replace("\\", "").Replace("\"", "");
                File.WriteAllText(App.SettingsPath, "{\"x\": " + X + ", \"y\": " + Y + ", \"w\": " + W + ", \"h\": " + H + ", \"maximized\": " + (Maximized ? "true" : "false") + ", \"site_url\": \"" + site + "\"}\n", Encoding.UTF8);
            }
            catch (Exception e) { App.Log("settings save failed: " + e.Message); }
        }
    }
}
