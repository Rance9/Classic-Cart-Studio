// Classic Cart Studio for Windows — entry point: single instance, command line (nesstudio:// links, --version, --selftest), then the
// WPF application with the one main window. The window shows the live website; the Python backend is started and supervised by Backend.cs.
using System;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using System.Windows;

namespace ClassicCartStudio
{
    internal static class Program
    {
        public const string ProductName = "Classic Cart Studio";
        public const string MutexName = @"Local\ClassicCartStudio.Main";
        public const string PipeName = "ClassicCartStudio.ipc";

        [STAThread]
        static int Main(string[] args)
        {
            foreach (var a in args)
            {
                if (a == "--version") { Console.WriteLine(App.Version); return 0; }
            }
            if (Array.IndexOf(args, "--selftest") >= 0) return SelfTest.Run();

            bool created;
            using (var mutex = new Mutex(true, MutexName, out created))
            {
                if (!created)
                {
                    // another instance owns the window: hand the link over and leave (no second backend, no port conflict)
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
                GC.KeepAlive(mutex);
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
                    var b = Encoding.UTF8.GetBytes(string.IsNullOrEmpty(payload) ? "nesstudio://open" : payload);
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
