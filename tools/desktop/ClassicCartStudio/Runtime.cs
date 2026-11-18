// The Microsoft Edge WebView2 Runtime the window needs (D-144/D-146): one way of finding it for the app's own start and for Setup
// (ClassicCartStudio.exe --runtime-check <file>, run by the installer before anything is installed).
//   1. Microsoft's own discovery (GetAvailableCoreWebView2BrowserVersionString), as every Evergreen app uses it, with the version this
//      SDK needs (Minimum).
//   2. Only when that finds nothing usable: the Evergreen Runtime's own installation folders (per machine and per user), each version
//      folder accepted only when its msedgewebview2.exe has a valid Authenticode signature by Microsoft Corporation and the SDK's loader
//      reads that same version from it. This is the documented browserExecutableFolder argument of CoreWebView2Environment.CreateAsync
//      pointed at a verified Microsoft Runtime folder; it answers registrations that name a version whose files are gone (Windows Sandbox,
//      https://github.com/MicrosoftEdge/WebView2Feedback/issues/5697). Never the Microsoft Edge browser's folders, never a registry or
//      permission change, never a hard-coded version.
//   3. The check proves the Runtime starts: an environment and a hidden WebView with its own temporary data folder load a page.
// Exit codes of --runtime-check: 0 usable (Microsoft's discovery), 1 usable (verified Runtime folder), 2 too old, 3 none found,
// 4 found but it did not start, 8 the check took too long, 9 the check failed. The file gets the details as JSON (the installer keeps it).
// Testing Setup's own install and failure paths on computers that already have the Runtime (Windows Sandbox always does): with
// CCS_RUNTIME_CHECK_SIMULATE=absent the check reports none found (3) without looking; =absent-once does that only while the file named by
// CCS_RUNTIME_CHECK_MARKER does not exist (it creates it), so the next check is the real one. The JSON says "simulated". Only this check
// reads them; the app's own start never does.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Interop;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;

namespace ClassicCartStudio
{
    internal static class Runtime
    {
        public const string Minimum = "137.0.3296.44";   // the WebView2 SDK 1.0.3296.44's documented Runtime requirement (ClassicCartStudio.csproj)

        internal sealed class Found
        {
            public string Kind = "none";   // default | folder | old | none
            public string Version, Folder, Detail = "";
            public readonly List<string> Seen = new List<string>();
        }

        // skipDiscovery: only for the check run by hand (CCS_RUNTIME_CHECK_SKIP_DISCOVERY=1 with the runtime-check option), to prove step 2 on a
        // computer whose discovery works; the app's own start never skips it
        public static Found Find(bool skipDiscovery = false)
        {
            var f = new Found(); string v = null;
            if (skipDiscovery) f.Detail = "Microsoft's discovery was skipped for this check.";
            else try { v = CoreWebView2Environment.GetAvailableBrowserVersionString(); }
            catch (Exception e) { f.Detail = "Microsoft's discovery found no Runtime (" + Describe(e) + ")."; }
            if (v != null && Newer(v)) { f.Kind = "default"; f.Version = v; return f; }
            if (v != null) { f.Kind = "old"; f.Version = v; f.Detail = "Microsoft's discovery found version " + v + "; " + Minimum + " or later is needed."; }
            string best = null, bestV = null;
            foreach (var dir in Candidates())
            {
                var note = Verify(dir, out var ver);
                f.Seen.Add(dir + ": " + note);
                if (ver != null && (bestV == null || CoreWebView2Environment.CompareBrowserVersions(ver, bestV) > 0)) { best = dir; bestV = ver; }
            }
            if (best != null) { f.Kind = "folder"; f.Folder = best; f.Version = bestV; }
            return f;
        }

        static bool Newer(string v) { try { return CoreWebView2Environment.CompareBrowserVersions(v, Minimum) >= 0; } catch { return false; } }

        // the Evergreen Runtime's installation folders (Microsoft Edge WebView2 only; the browser lives in Microsoft\Edge and is never used)
        static IEnumerable<string> Candidates()
        {
            var roots = new List<string>();
            foreach (var b in new[] { Environment.GetEnvironmentVariable("ProgramFiles(x86)"), Environment.GetEnvironmentVariable("ProgramFiles"), Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) })
                if (!string.IsNullOrEmpty(b)) { var r = Path.Combine(b, "Microsoft", "EdgeWebView", "Application"); if (!roots.Contains(r, StringComparer.OrdinalIgnoreCase)) roots.Add(r); }
            foreach (var r in roots)
            {
                string[] dirs; try { dirs = Directory.Exists(r) ? Directory.GetDirectories(r) : new string[0]; } catch { continue; }
                foreach (var d in dirs) if (Version.TryParse(Path.GetFileName(d), out _)) yield return d;
            }
        }

        // a version folder is used only when Microsoft signed its msedgewebview2.exe (valid Authenticode, Microsoft Corporation) and the SDK's
        // loader reads a Runtime of at least Minimum from it
        static string Verify(string dir, out string version)
        {
            version = null; var exe = Path.Combine(dir, "msedgewebview2.exe");
            if (!File.Exists(exe)) return "no msedgewebview2.exe";
            if (!SignedByMicrosoft(exe, out var why)) return "not used: " + why;
            string v; try { v = CoreWebView2Environment.GetAvailableBrowserVersionString(dir); } catch (Exception e) { return "not used: the loader could not read it (" + Describe(e) + ")"; }
            if (string.IsNullOrEmpty(v)) return "not used: the loader found no Runtime there";
            if (!Newer(v)) return "not used: version " + v + " is older than " + Minimum;
            version = v; return "usable, version " + v;
        }

        static bool SignedByMicrosoft(string path, out string why)
        {
            why = "";
            int rc = Trust.Verify(path);
            if (rc != 0) { why = "its signature is not valid (0x" + rc.ToString("X8") + ")"; return false; }
            try
            {
                var c = new X509Certificate2(X509Certificate.CreateFromSignedFile(path));
                var s = c.Subject ?? "";
                if (s.IndexOf("CN=Microsoft Corporation", StringComparison.Ordinal) < 0 || s.IndexOf("O=Microsoft Corporation", StringComparison.Ordinal) < 0) { why = "not signed by Microsoft Corporation (" + s + ")"; return false; }
            }
            catch (Exception e) { why = "its signer could not be read (" + e.Message + ")"; return false; }
            return true;
        }

        public static string Describe(Exception e) => e == null ? "" : (e.GetType().Name + (e.HResult != 0 ? " 0x" + e.HResult.ToString("X8") : "") + ": " + e.Message).Replace('\r', ' ').Replace('\n', ' ');

        // the Runtime starts: an environment and a hidden WebView (its own temporary data folder, removed afterwards) load a small page
        public static async Task<string> Probe(string folder)
        {
            var udf = Path.Combine(Path.GetTempPath(), "ClassicCartStudio-runtime-check-" + Guid.NewGuid().ToString("N").Substring(0, 12));
            HwndSource win = null; CoreWebView2Controller ctl = null;
            try
            {
                var env = await CoreWebView2Environment.CreateAsync(folder, udf, new CoreWebView2EnvironmentOptions());
                win = new HwndSource(new HwndSourceParameters("Classic Cart Studio runtime check") { Width = 64, Height = 64, WindowStyle = unchecked((int)0x80000000) });   // WS_POPUP, never shown
                ctl = await env.CreateCoreWebView2ControllerAsync(win.Handle);
                var done = new TaskCompletionSource<bool>();
                ctl.CoreWebView2.NavigationCompleted += (s, e) => done.TrySetResult(e.IsSuccess);
                ctl.CoreWebView2.NavigateToString("<!doctype html><title>Classic Cart Studio</title><p>ready</p>");
                var first = await Task.WhenAny(done.Task, Task.Delay(30000));
                if (first != done.Task) return "the Runtime did not load a page within 30 seconds";
                if (!done.Task.Result) return "the Runtime could not load a page";
                return null;
            }
            catch (Exception e) { return Describe(e); }
            finally
            {
                try { ctl?.Close(); } catch { }
                try { win?.Dispose(); } catch { }
                var u = udf; _ = Task.Run(async () => { for (int i = 0; i < 20; i++) { try { if (Directory.Exists(u)) Directory.Delete(u, true); return; } catch { await Task.Delay(500); } } });
            }
        }

        // ClassicCartStudio.exe --runtime-check <file>: the installer's check (also usable by hand); writes JSON, returns the exit code
        public static int CheckMain(string outPath)
        {
            int code = 9; var json = new StringBuilder();
            var t = new Thread(() =>
            {
                var d = Dispatcher.CurrentDispatcher;
                d.BeginInvoke(new Action(async () =>
                {
                    try
                    {
                        var sim = Simulated(); var f = sim ? new Found { Detail = "Simulated absence for a test (CCS_RUNTIME_CHECK_SIMULATE); nothing was looked for." } : Find(Environment.GetEnvironmentVariable("CCS_RUNTIME_CHECK_SKIP_DISCOVERY") == "1"); string started = null;
                        if (f.Kind == "default" || f.Kind == "folder") { started = await Probe(f.Kind == "folder" ? f.Folder : null); code = started == null ? (f.Kind == "default" ? 0 : 1) : 4; }
                        else code = f.Kind == "old" ? 2 : 3;
                        json.Append("{\"schema\":\"ccs-runtime-check/1\",\"code\":").Append(code)
                            .Append(",\"kind\":").Append(Q(f.Kind)).Append(",\"version\":").Append(Q(f.Version)).Append(",\"folder\":").Append(Q(f.Folder))
                            .Append(",\"minimum\":").Append(Q(Minimum)).Append(",\"detail\":").Append(Q(f.Detail)).Append(",\"start\":").Append(Q(started ?? (code <= 1 ? "ok" : "")))
                            .Append(",\"os\":").Append(Q(Environment.OSVersion.VersionString)).Append(",\"arch\":").Append(Q(RuntimeInformation.OSArchitecture.ToString()))
                            .Append(",\"process\":").Append(Q(RuntimeInformation.ProcessArchitecture.ToString())).Append(",\"time\":").Append(Q(DateTime.Now.ToString("o")))
                            .Append(",\"simulated\":").Append(sim ? "true" : "false")
                            .Append(",\"seen\":[").Append(string.Join(",", f.Seen.Select(Q))).Append("]}");
                    }
                    catch (Exception e) { code = 9; json.Clear().Append("{\"schema\":\"ccs-runtime-check/1\",\"code\":9,\"error\":").Append(Q(Describe(e))).Append('}'); }
                    finally { d.InvokeShutdown(); }
                }));
                Dispatcher.Run();
            }) { IsBackground = true, Name = "runtime-check" };
            t.SetApartmentState(ApartmentState.STA); t.Start();
            if (!t.Join(TimeSpan.FromSeconds(120))) { code = 8; json.Clear().Append("{\"schema\":\"ccs-runtime-check/1\",\"code\":8,\"error\":\"the check took longer than two minutes\"}"); }
            try { if (!string.IsNullOrEmpty(outPath)) File.WriteAllText(outPath, json.ToString(), new UTF8Encoding(false)); } catch { }
            return code;
        }

        static bool Simulated()
        {
            var s = Environment.GetEnvironmentVariable("CCS_RUNTIME_CHECK_SIMULATE");
            if (s == "absent") return true;
            if (s != "absent-once") return false;
            var m = Environment.GetEnvironmentVariable("CCS_RUNTIME_CHECK_MARKER");
            if (string.IsNullOrEmpty(m) || File.Exists(m)) return false;
            try { File.WriteAllText(m, DateTime.Now.ToString("o")); } catch { return false; }
            return true;
        }

        static string Q(string s)
        {
            if (s == null) return "null";
            var b = new StringBuilder("\"");
            foreach (var c in s) { if (c == '"' || c == '\\') b.Append('\\').Append(c); else if (c < 32) b.Append("\\u").Append(((int)c).ToString("x4")); else b.Append(c); }
            return b.Append('"').ToString();
        }

        // WinVerifyTrust (Authenticode), offline: no revocation lookup (Setup may run without a network)
        static class Trust
        {
            static readonly Guid Action = new Guid("00AAC56B-CD44-11d0-8CC2-00C04FC295EE");   // WINTRUST_ACTION_GENERIC_VERIFY_V2
            [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
            struct FileInfo { public uint cbStruct; public string pcwszFilePath; public IntPtr hFile; public IntPtr pgKnownSubject; }
            [StructLayout(LayoutKind.Sequential)]
            struct Data
            {
                public uint cbStruct; public IntPtr pPolicyCallbackData, pSIPClientData; public uint dwUIChoice, fdwRevocationChecks, dwUnionChoice; public IntPtr pFile;
                public uint dwStateAction; public IntPtr hWVTStateData, pwszURLReference; public uint dwProvFlags, dwUIContext; public IntPtr pSignatureSettings;
            }
            [DllImport("wintrust.dll", ExactSpelling = true, SetLastError = false, CharSet = CharSet.Unicode)]
            static extern int WinVerifyTrust(IntPtr hwnd, [MarshalAs(UnmanagedType.LPStruct)] Guid action, IntPtr data);
            public static int Verify(string path)
            {
                var fi = new FileInfo { cbStruct = (uint)Marshal.SizeOf(typeof(FileInfo)), pcwszFilePath = path };
                var pfi = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(FileInfo))); Marshal.StructureToPtr(fi, pfi, false);
                var wd = new Data { cbStruct = (uint)Marshal.SizeOf(typeof(Data)), dwUIChoice = 2 /*WTD_UI_NONE*/, fdwRevocationChecks = 0 /*WTD_REVOKE_NONE*/, dwUnionChoice = 1 /*WTD_CHOICE_FILE*/, pFile = pfi, dwStateAction = 1 /*VERIFY*/, dwProvFlags = 0x80 /*WTD_CACHE_ONLY_URL_RETRIEVAL*/ };
                var pwd = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(Data))); Marshal.StructureToPtr(wd, pwd, false);
                try { return WinVerifyTrust(IntPtr.Zero, Action, pwd); }
                finally
                {
                    try { var close = (Data)Marshal.PtrToStructure(pwd, typeof(Data)); close.dwStateAction = 2; /*CLOSE*/ Marshal.StructureToPtr(close, pwd, true); WinVerifyTrust(IntPtr.Zero, Action, pwd); } catch { }
                    Marshal.FreeHGlobal(pwd); Marshal.FreeHGlobal(pfi);
                }
            }
        }
    }
}
