// Starts, watches and stops the Python backend (the existing companion, headless). The host mints a random session token per run,
// hands it to the backend through the environment (never a command line or a file), and gives it only to pages of the approved
// origin (MainForm injects it). The backend treats that token as the "desktop" principal for the privileged endpoints.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace ClassicCartStudio
{
    internal class Backend
    {
        public const string CompanionExe = "ClassicCartStudioBackend.exe";
        public readonly string Token;
        public int Port { get; private set; } = 8765;
        public Process Process { get; private set; }
        public string LastError { get; private set; } = "";
        public bool Ready { get; private set; }
        public string BackendVersion { get; private set; } = "";
        public bool ForeignOnPort { get; private set; }

        public Backend()
        {
            var b = new byte[32]; using (var r = RandomNumberGenerator.Create()) r.GetBytes(b);
            Token = BitConverter.ToString(b).Replace("-", "").ToLowerInvariant();
            Port = ReadConfiguredPort();
        }

        static int ReadConfiguredPort()
        {
            try
            {
                var p = Path.Combine(App.DataRoot, "companion.json");
                if (File.Exists(p)) { var m = Regex.Match(File.ReadAllText(p), "\"port\"\\s*:\\s*(\\d{2,5})"); if (m.Success) return int.Parse(m.Groups[1].Value); }
            }
            catch { }
            return 8765;
        }

        public string Base => "http://127.0.0.1:" + Port;

        /// <summary>Start the backend (or adopt nothing: if a foreign program holds the port the user is told). Returns when /api/ping answers or after the timeout.</summary>
        public bool Start(int timeoutMs = 60000)
        {
            Ready = false; LastError = ""; ForeignOnPort = false;
            var probe = Probe();
            if (probe != null)
            {
                // something already listens: an older companion window (not ours: it does not know our token) or another program
                ForeignOnPort = true;
                LastError = probe.Value.Key ? ("A copy of the Studio companion (version " + probe.Value.Value + ") is already running on this computer. Close it, then choose Try Again.")
                                            : ("Another program is using port " + Port + " on this computer, which the Studio needs. Close it, then choose Try Again.");
                App.Log("port busy: " + LastError); return false;
            }
            var exe = Path.Combine(App.AppDir, CompanionExe);
            var psi = new ProcessStartInfo { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = App.AppDir };
            if (File.Exists(exe)) { psi.FileName = exe; psi.Arguments = "--headless --port " + Port; }
            else if (App.Dev)
            {
                // development checkout: run the companion from source
                var src = Environment.GetEnvironmentVariable("CCS_BACKEND_SOURCE");
                if (string.IsNullOrEmpty(src) || !File.Exists(Path.Combine(src, "companion_app.py"))) { LastError = "Backend not found: set CCS_BACKEND_SOURCE to tools\\companion in development."; return false; }
                psi.FileName = "python"; psi.Arguments = "-u \"" + Path.Combine(src, "companion_app.py") + "\" --headless --port " + Port; psi.WorkingDirectory = src;
            }
            else { LastError = "The Studio backend (" + CompanionExe + ") is missing next to the application. Reinstall Classic Cart Studio."; App.Log(LastError); return false; }
            psi.Environment["CCS_HOST_TOKEN"] = Token;
            psi.Environment["CCS_HOST_PID"] = Process.GetCurrentProcess().Id.ToString();
            psi.Environment["PYTHONIOENCODING"] = "utf-8";
            try { Process = Process.Start(psi); }
            catch (Exception e) { LastError = "The Studio backend could not be started: " + e.Message; App.Log(LastError); return false; }
            App.Log("backend started pid " + Process.Id + " port " + Port);
            var sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < timeoutMs)
            {
                if (Process.HasExited) { LastError = "The Studio backend stopped right after starting (exit code " + Process.ExitCode + "). See " + Path.Combine(App.DataRoot, "logs") + "."; App.Log(LastError); return false; }
                if (Ping()) { var v = Version(); if (v != null && v.Value.Key) { BackendVersion = v.Value.Value; Ready = true; App.Log("backend ready version " + BackendVersion + " in " + sw.ElapsedMilliseconds + " ms"); return true; } }
                Thread.Sleep(150);
            }
            LastError = "The Studio backend did not answer within " + (timeoutMs / 1000) + " seconds."; App.Log(LastError); return false;
        }

        KeyValuePair<bool, string>? Probe()
        {
            try
            {
                var req = (HttpWebRequest)WebRequest.Create(Base + "/api/version"); req.Timeout = 1500;
                using (var resp = (HttpWebResponse)req.GetResponse()) using (var sr = new StreamReader(resp.GetResponseStream()))
                {
                    var body = sr.ReadToEnd(); var ours = body.Contains("\"classic-cart-studio-backend\"");
                    var m = Regex.Match(body, "\"version\"\\s*:\\s*\"([^\"]*)\""); return new KeyValuePair<bool, string>(ours, m.Success ? m.Groups[1].Value : "?");
                }
            }
            catch (WebException e) { if (e.Response != null) return new KeyValuePair<bool, string>(false, "?"); return null; }
            catch { return null; }
        }

        public bool Ping()
        {
            try { var req = (HttpWebRequest)WebRequest.Create(Base + "/api/ping"); req.Timeout = 1000; using (var r = req.GetResponse()) return true; } catch { return false; }
        }

        /// <summary>GET /api/version as the desktop principal: (desktop accepted, version).</summary>
        public KeyValuePair<bool, string>? Version()
        {
            var body = Call("GET", "/api/version", null, 3000);
            if (body == null) return null;
            var m = Regex.Match(body, "\"version\"\\s*:\\s*\"([^\"]*)\"");
            return new KeyValuePair<bool, string>(body.Contains("\"desktop\": true") || body.Contains("\"desktop\":true"), m.Success ? m.Groups[1].Value : "?");
        }

        public string Call(string method, string path, string json, int timeoutMs)
        {
            try
            {
                var req = (HttpWebRequest)WebRequest.Create(Base + path); req.Method = method; req.Timeout = timeoutMs;
                req.Headers["Authorization"] = "Bearer " + Token; req.Headers["Origin"] = App.CanonicalOrigin; req.Headers["X-NES-Protocol"] = "1";
                if (json != null) { var b = Encoding.UTF8.GetBytes(json); req.ContentType = "application/json"; req.ContentLength = b.Length; using (var s = req.GetRequestStream()) s.Write(b, 0, b.Length); }
                using (var resp = (HttpWebResponse)req.GetResponse()) using (var sr = new StreamReader(resp.GetResponseStream())) return sr.ReadToEnd();
            }
            catch (WebException e)
            {
                try { if (e.Response != null) using (var sr = new StreamReader(e.Response.GetResponseStream())) return sr.ReadToEnd(); } catch { }
                return null;
            }
            catch { return null; }
        }

        public void Stop()
        {
            if (Process == null) return;
            try
            {
                if (!Process.HasExited) { Call("POST", "/api/shutdown", "{}", 3000); }
                if (!Process.WaitForExit(5000)) { App.Log("backend did not stop in time; terminating"); try { Process.Kill(); } catch { } }
                App.Log("backend stopped");
            }
            catch (Exception e) { App.Log("backend stop: " + e.Message); }
            finally { Process = null; Ready = false; }
        }
    }

    // --selftest: start the backend, prove the desktop channel, stop. Used by CI (no window needed) and by the acceptance harness.
    internal static class SelfTest
    {
        public static int Run()
        {
            var b = new Backend(); var sw = Stopwatch.StartNew();
            Console.WriteLine("ClassicCartStudio " + App.Version + " selftest: data=" + App.DataRoot + " port=" + b.Port);
            if (!b.Start(60000)) { Console.WriteLine("FAIL start: " + b.LastError); return 1; }
            Console.WriteLine("backend ready in " + sw.ElapsedMilliseconds + " ms, version " + b.BackendVersion);
            var v = b.Call("GET", "/api/version", null, 3000) ?? "";
            var caps = v.Contains("\"capabilities\""); var desk = v.Contains("\"desktop\": true") || v.Contains("\"desktop\":true");
            var st = b.Call("GET", "/api/status", null, 5000) ?? "";
            var anon = AnonStatus(b.Base);
            Console.WriteLine("desktop_principal=" + desk + " capabilities=" + caps + " status_ok=" + st.Contains("\"ok\": true") + " anonymous_status_refused=" + anon);
            b.Stop();
            var okAll = desk && caps && st.Contains("\"ok\": true") && anon;
            Console.WriteLine(okAll ? "RESULT: PASS" : "RESULT: FAIL"); return okAll ? 0 : 1;
        }
        static bool AnonStatus(string baseUrl)
        {
            try
            {
                var req = (HttpWebRequest)WebRequest.Create(baseUrl + "/api/status"); req.Timeout = 3000; req.Headers["Origin"] = App.CanonicalOrigin;
                using (var r = (HttpWebResponse)req.GetResponse()) return false;   // 200 without a token would be wrong
            }
            catch (WebException e) { return e.Response != null && ((HttpWebResponse)e.Response).StatusCode == HttpStatusCode.Unauthorized; }
            catch { return false; }
        }
    }
}
