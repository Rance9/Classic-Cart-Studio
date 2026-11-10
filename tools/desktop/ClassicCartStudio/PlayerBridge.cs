// The Preview's game player inside this window (D-104): the Studio page asks for it ("embed-open"); this window creates, for that one
// session, a named pipe only this user's processes can open and a WebView2 shared frame buffer, posts the buffer to the page, and waits
// for the game player the backend starts with the pipe's name. The player is checked (it must be this installation's ccs-player.exe)
// before it gets a duplicate of the buffer's handle; it then writes each finished picture there and the page draws it in the TV area.
// The page's input and the Preview's controls come back as short command lines (a fixed list, checked here) and go down the pipe; what
// the player reports (state, HOME, pop-out, its end) goes up to the page. Closing the Studio while a session is open asks first (F6,
// D-91): Cancel is the default; confirmed, the player is asked to end (its game's own save written, never a slot) before the Studio
// closes, and a failed save is said instead of being hidden. If this window goes away anyway, the broken pipe ends the player.
using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Web.WebView2.Core;

namespace ClassicCartStudio
{
    internal class PlayerBridge : IDisposable
    {
        public const int Header = 4096, Slot = 2560 * 1440 * 4;
        public static readonly long Size = Header + 2L * Slot;
        static readonly Regex Allowed = new Regex(@"^(key [du] \d{1,3}|pad [du] (up|down|left|right|a|b|select|start)|pt [0-4] -?\d{1,5} -?\d{1,5} -?\d{1,2}|own [01]|text [ -~]{1,31}|text-bs|text-enter|menu|close|popout|popin|front|theme [a-z-]{3,40}|music (on|off)|select [A-Za-z0-9_.:-]{1,64}|reload)$");
        readonly MainWindow win; readonly CoreWebView2 core; readonly CoreWebView2Environment env;
        CoreWebView2SharedBuffer buf; System.Runtime.InteropServices.SafeHandle mapSafe; IntPtr mapHandle; NamedPipeServerStream pipe; StreamWriter wr; Thread thread; readonly object wlock = new object();   // mapSafe/mapHandle: the buffer's file mapping, read on this window's own thread (WebView2 objects belong to it)
        public string PipeName { get; private set; }
        public int PlayerPid { get; private set; }
        public bool Connected { get; private set; }
        public bool Ended { get; private set; }
        public bool GameOpen { get; private set; }
        string exitReply;   // "bye" / "exit-failed ..." / "exit-canceled" while the Studio waits to close
        readonly ManualResetEventSlim exitEvent = new ManualResetEventSlim(false);
        readonly ManualResetEventSlim endedEvent = new ManualResetEventSlim(false);
        volatile bool letGo;   // this window no longer listens to it (disposed, or superseded by the next Preview)

        public PlayerBridge(MainWindow w, CoreWebView2 c, CoreWebView2Environment e) { win = w; core = c; env = e; }
        public bool Active => Connected && !Ended;

        public string Open()
        {
            var id = new byte[16]; using (var r = System.Security.Cryptography.RandomNumberGenerator.Create()) r.GetBytes(id);
            PipeName = "ccs-embed-" + BitConverter.ToString(id).Replace("-", "").ToLowerInvariant();
            var sec = new PipeSecurity(); var me = WindowsIdentity.GetCurrent().User;
            sec.AddAccessRule(new PipeAccessRule(me, PipeAccessRights.ReadWrite | PipeAccessRights.CreateNewInstance, AccessControlType.Allow));   // this user only
            pipe = new NamedPipeServerStream(PipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 65536, 65536, sec);
            buf = env.CreateSharedBuffer((ulong)Size); mapSafe = buf.FileMappingHandle; mapHandle = mapSafe.DangerousGetHandle();
            core.PostSharedBufferToScript(buf, CoreWebView2SharedBufferAccess.ReadOnly, "{\"kind\":\"ccs-frames\",\"pipe\":\"" + PipeName + "\",\"header\":" + Header + ",\"slot\":" + Slot + "}");
            thread = new Thread(Run) { IsBackground = true, Name = "ccs-player-bridge" }; thread.Start();
            App.Log("player bridge: listening " + PipeName); return PipeName;
        }

        void Run() { try { RunPipe(); } finally { endedEvent.Set(); } }
        void RunPipe()
        {
            try
            {
                var ar = pipe.BeginWaitForConnection(null, null);
                if (!ar.AsyncWaitHandle.WaitOne(TimeSpan.FromMinutes(5))) { App.Log("player bridge: no player connected"); ToPage("player:gone no-player"); Ended = true; return; }
                pipe.EndWaitForConnection(ar);
                uint pid; if (!GetNamedPipeClientProcessId(pipe.SafePipeHandle.DangerousGetHandle(), out pid) || !TrustedPlayer((int)pid)) { App.Log("player bridge: refused a client " + pid); ToPage("player:gone refused"); pipe.Disconnect(); Ended = true; return; }
                PlayerPid = (int)pid; Connected = true;
                wr = new StreamWriter(pipe, new UTF8Encoding(false)) { AutoFlush = true, NewLine = "\n" };
                var hp = OpenProcess(0x0040 /* PROCESS_DUP_HANDLE */, false, pid); IntPtr dup = IntPtr.Zero;
                if (hp == IntPtr.Zero || !DuplicateHandle(GetCurrentProcess(), mapHandle, hp, out dup, 0, false, 2 /* DUPLICATE_SAME_ACCESS */)) { App.Log("player bridge: handle not duplicated " + Marshal.GetLastWin32Error()); ToPage("player:gone no-frames"); }
                else Send("map " + dup.ToInt64().ToString("x") + " " + Size, true);
                if (hp != IntPtr.Zero) CloseHandle(hp);
                AllowSetForegroundWindow((int)pid);   // Pop Out and Bring to Front may bring the player's own window forward
                var rd = new StreamReader(pipe, new UTF8Encoding(false)); string line;
                while ((line = rd.ReadLine()) != null)
                {
                    if (line.StartsWith("state ")) { var p = line.Split(' '); GameOpen = p.Length > 2 && p[2] == "1"; }
                    if (line.StartsWith("bye") || line.StartsWith("exit-failed") || line.StartsWith("exit-canceled")) { exitReply = line; exitEvent.Set(); }
                    ToPage("player:" + line);
                }
            }
            catch (Exception e) { App.Log("player bridge: " + e.Message); }
            Ended = true; Connected = false; GameOpen = false; exitReply = exitReply ?? "bye"; exitEvent.Set(); ToPage("player:ended");
            App.Log("player bridge: ended " + PipeName);
        }

        // this installation's player (the backend starts it from its own folder); a development build only with CCS_DEV and CCS_PLAYER_EXE
        static bool TrustedPlayer(int pid)
        {
            try
            {
                var path = Process.GetProcessById(pid).MainModule.FileName;
                if (!string.Equals(Path.GetFileName(path), "ccs-player.exe", StringComparison.OrdinalIgnoreCase)) return false;
                var full = Path.GetFullPath(path);
                if (full.StartsWith(Path.GetFullPath(App.AppDir), StringComparison.OrdinalIgnoreCase)) return true;
                var dev = Environment.GetEnvironmentVariable("CCS_PLAYER_EXE");
                return App.Dev && !string.IsNullOrEmpty(dev) && string.Equals(Path.GetFullPath(dev), full, StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }

        // a bridge this window has let go never speaks to the page again: its late "ended" would otherwise end the next Preview's session
        void ToPage(string s) { try { win.Dispatcher.BeginInvoke(new Action(() => { if (letGo) return; try { core.PostWebMessageAsString(s); } catch { } })); } catch { } }
        public bool WaitEnded(int ms) => endedEvent.Wait(ms);   // the player's pipe has closed (it ended, or never came)
        public void LetGo() { letGo = true; }

        // a command from the page: only the fixed list
        public bool Command(string line)
        {
            if (line == null || !Allowed.IsMatch(line)) { App.Log("player bridge: refused command"); return false; }
            if (line.StartsWith("popout") || line.StartsWith("front")) AllowSetForegroundWindow(PlayerPid);
            return Send(line, false);
        }
        bool Send(string line, bool internalLine)
        {
            if (!Connected || Ended || wr == null) return false;
            try { lock (wlock) wr.WriteLine(line); return true; } catch (Exception e) { App.Log("player bridge: write " + e.Message); return false; }
        }

        // F6: the Studio closes with a session open. Returns true when the Studio may close now.
        public bool ConfirmStudioExit(Window owner)
        {
            if (!Active) return true;
            var what = GameOpen ? "A game is open in Preview on TV" : "The HOME is open in Preview on TV";
            if (!Ask(owner, "Close Classic Cart Studio?", what + ". Closing Classic Cart Studio closes it too. Anything you have not saved to a save slot is lost; the game's own cartridge save is written first. To keep your place, choose Keep Studio Open and save to a slot.", "Keep Studio Open", "Close Studio")) return false;
            exitReply = null; exitEvent.Reset(); Send("studio-exit", true);
            if (!exitEvent.Wait(TimeSpan.FromSeconds(10))) { App.Log("player bridge: the player did not answer the close"); return Ask(owner, "The Game Did Not Answer", "The game did not finish closing. If you close Classic Cart Studio now, the game ends by itself as soon as it notices; its cartridge save is attempted then.", "Keep Studio Open", "Close Studio") ? CloseAnyway() : false; }
            if (exitReply != null && exitReply.StartsWith("exit-failed"))
            {
                var why = exitReply.Length > 12 ? exitReply.Substring(12).Trim().Trim('"') : "";
                if (!Ask(owner, "The Game's Save Was Not Written", "The game's own cartridge save could not be written" + (why.Length > 0 ? ": " + why : ".") + " The game is still open with its choices. Return to it to try again, or close without that save.", "Return to the Game", "Close Without the Save")) return false;
                exitReply = null; exitEvent.Reset(); Send("studio-exit force", true); exitEvent.Wait(TimeSpan.FromSeconds(10)); return true;
            }
            if (exitReply != null && exitReply.StartsWith("exit-canceled")) return false;
            return true;
        }
        bool CloseAnyway() { try { pipe?.Dispose(); } catch { } return true; }   // the player sees the broken pipe and ends itself (never a kill)

        // a small modal with the safe choice as the default (Enter and Escape both keep the Studio)
        internal static bool Ask(Window owner, string title, string message, string safe, string act)
        {
            bool go = false; var d = new DialogWindow(owner, title, 540, 260) { ResizeMode = ResizeMode.NoResize, SizeToContent = SizeToContent.Height };
            var stack = new StackPanel(); stack.Children.Add(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, FontSize = 13.5, LineHeight = 20 });
            var acts = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 18, 0, 0) };
            var b2 = new Button { Content = act, Style = (Style)Application.Current.FindResource("Btn"), Margin = new Thickness(8, 0, 0, 0) }; b2.Click += (s, e) => { go = true; d.Close(); };
            var b1 = new Button { Content = safe, Style = (Style)Application.Current.FindResource("Btn.Primary"), Margin = new Thickness(8, 0, 0, 0), IsDefault = true, IsCancel = true }; b1.Click += (s, e) => { go = false; d.Close(); };
            acts.Children.Add(b2); acts.Children.Add(b1); stack.Children.Add(acts); d.Body.Children.Add(stack); d.Loaded += (s, e) => b1.Focus(); d.ShowDialog();
            App.Log("ask: " + title + " -> " + (go ? act : safe)); return go;
        }

        public void Dispose()
        {
            letGo = true;
            try { pipe?.Dispose(); } catch { }
            try { buf?.Dispose(); } catch { }
            buf = null; pipe = null;
        }

        [DllImport("kernel32.dll", SetLastError = true)] static extern bool GetNamedPipeClientProcessId(IntPtr pipe, out uint pid);
        [DllImport("kernel32.dll", SetLastError = true)] static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);
        [DllImport("kernel32.dll", SetLastError = true)] static extern bool DuplicateHandle(IntPtr srcProc, IntPtr src, IntPtr dstProc, out IntPtr dst, uint access, bool inherit, uint options);
        [DllImport("kernel32.dll")] static extern IntPtr GetCurrentProcess();
        [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr h);
        [DllImport("user32.dll")] static extern bool AllowSetForegroundWindow(int pid);
    }
}
