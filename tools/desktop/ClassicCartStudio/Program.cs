// Classic Cart Studio for Windows — entry point: single instance, command line (classiccartstudio:// links, .ccs files, --version, --selftest, --play FILE (PlayFile.cs),
// --runtime-check <file>: Setup's WebView2 Runtime check, Runtime.cs), then the
// WPF application with the one main window. The window shows the live website; the Python backend is started and supervised by Backend.cs.
// One window per data folder (NES_STUDIO_DATA when set, else the default folder): a second start for the same folder hands its link or file
// to that window; a start for another folder never reaches it (it has its own lock and handover pipe), and if the first window's backend
// holds the port, its own backend says so instead of using that one.
using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using System.Windows;

namespace ClassicCartStudio
{
    internal static class Program
    {
        public const string ProductName = "Classic Cart Studio";
        /// <summary>held by every window, whatever its data folder: Setup waits until no window holds it (installer.iss)</summary>
        public const string MutexName = @"Local\ClassicCartStudio.Main";
        /// <summary>the data folder's identity: its full path, case and trailing separators set aside, as a short SHA-256 (no path in the name)</summary>
        internal static string DataRootId
        {
            get
            {
                string p; try { p = Path.GetFullPath(App.DataRoot); } catch (Exception) { p = App.DataRoot ?? ""; }
                p = p.TrimEnd('\\', '/').ToUpperInvariant();
                using (var h = System.Security.Cryptography.SHA256.Create())
                    return BitConverter.ToString(h.ComputeHash(Encoding.UTF8.GetBytes(p)), 0, 8).Replace("-", "").ToLowerInvariant();
            }
        }
        /// <summary>the one window of this data folder, and the pipe it takes handed-over links and files on</summary>
        internal static string DataRootMutexName => MutexName + "." + DataRootId;
        internal static string PipeName => "ClassicCartStudio.ipc." + DataRootId;

        [STAThread]
        static int Main(string[] args)
        {
            foreach (var a in args)
            {
                if (a == "--version") { Console.WriteLine(App.Version); return 0; }
            }
            if (Array.IndexOf(args, "--selftest") >= 0) return SelfTest.Run();
            // CCS Console Workbench is its own program installed beside this one (WorkbenchLauncher.cs): its window, its console workers, its USB
            // driver step and HOME Designer's compiler (the backend runs its --design-compile, Backend.cs) all run there, never in this process
            var rc = Array.IndexOf(args, "--runtime-check");   // before anything else: no window, no data folder, no single-instance lock
            if (rc >= 0) return Runtime.CheckMain(rc + 1 < args.Length ? args[rc + 1] : null);
            // one game file in the game player (PlayFile.cs): before the single-instance lock, so an open Classic Cart Studio window is neither
            // used nor brought forward, and no window of this program opens
            if (PlayFile.Wanted(args)) return PlayFile.Run(args);

            bool created;
            using (var mutex = new Mutex(true, DataRootMutexName, out created))
            using (var any = new Mutex(false, MutexName))   // Setup's wait (any data folder)
            {
                if (!created)
                {
                    // this data folder's window is open: hand the link over and leave (no second backend, no port conflict)
                    ForwardToRunningInstance(string.Join("\n", args));
                    return 0;
                }
                var app = new StudioApp();
                app.InitializeComponent();
                var window = new MainWindow(args);
                app.MainWindow = window;
                StartPipeServer(window);
                window.Show();
                app.Run();
                GC.KeepAlive(mutex); GC.KeepAlive(any);
            }
            return 0;
        }

        static void ForwardToRunningInstance(string payload)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                using (var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out))
                {
                    client.Connect(2000);
                    var b = Encoding.UTF8.GetBytes(string.IsNullOrEmpty(payload) ? "classiccartstudio://open" : payload);
                    client.Write(b, 0, b.Length); client.Flush(); client.WaitForPipeDrain();
                }
                var shown = (payload ?? "").Replace('\n', ' ');
                App.Log("forwarded to the running instance in " + sw.ElapsedMilliseconds + " ms: " + shown.Substring(0, Math.Min(80, shown.Length)));
            }
            catch (Exception e) { App.Log("forward to the running instance failed after " + sw.ElapsedMilliseconds + " ms: " + e.Message); }
        }

        static void StartPipeServer(MainWindow window)
        {
            var t = new Thread(() =>
            {
                while (true)
                {
                    try
                    {
                        using (var server = new NamedPipeServerStream(PipeName, PipeDirection.In, 1, PipeTransmissionMode.Message, PipeOptions.None, 8192, 8192))
                        {
                            server.WaitForConnection();
                            // one message per connection: deliver it as soon as a complete message was read (do not wait for the client to close)
                            var buf = new byte[8192]; var sb = new StringBuilder(); int n;
                            do { n = server.Read(buf, 0, buf.Length); if (n > 0) sb.Append(Encoding.UTF8.GetString(buf, 0, n)); } while (n > 0 && !server.IsMessageComplete && sb.Length < 16384);
                            var text = sb.ToString();
                            if (text.Length > 0) { try { window.Dispatcher.BeginInvoke(new Action(() => window.OnActivatedFromAnotherInstance(text))); } catch { } }
                        }
                    }
                    catch { Thread.Sleep(500); }
                }
            }) { IsBackground = true, Name = "ipc" };
            t.Start();
        }
    }
}
