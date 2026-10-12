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

        // The production website. The github.io project URL is a legacy redirect, not a production dependency.
        public const string CanonicalOrigin = "https://classiccartstudio.com";
        public const string SiteUrl = CanonicalOrigin + "/";
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

        public static string StartUrl
        {
            get
            {
                if (Dev && !string.IsNullOrEmpty(DevSiteUrl) && Regex.IsMatch(DevSiteUrl, @"^http://(localhost|127\.0\.0\.1):\d{2,5}/")) return DevSiteUrl;
                var s = Settings.Load();
                // narrowly controlled override for a future canonical address change: https only, and only hosts on the allow list
                if (!string.IsNullOrEmpty(s.SiteUrl) && IsApprovedOrigin(OriginOf(s.SiteUrl)) && s.SiteUrl.StartsWith("https://")) return s.SiteUrl;
                return SiteUrl;
            }
        }

        public static string OriginOf(string url)
        {
            try { var u = new Uri(url); return u.Scheme + "://" + u.Host + ((u.IsDefaultPort) ? "" : ":" + u.Port); } catch { return ""; }
        }

        public static bool IsApprovedOrigin(string origin)
        {
            if (string.IsNullOrEmpty(origin)) return false;
            foreach (var o in SiteOrigins) if (string.Equals(o, origin, StringComparison.OrdinalIgnoreCase)) return true;
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
