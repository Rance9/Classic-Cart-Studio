// Classic Cart Studio console backup and restore window. Copyright (C) 2026 Rance9. GPL-3.0-or-later.
// The Workbench's windows and their frames.
//   Maximized      The window draws its own title bar (WindowChrome); maximized, Windows places such a window past the monitor's work area by
//                  the width of its invisible resize border on every side (8 pixels at 100%), so its title bar, buttons and status strip
//                  would be cut off at the screen's edges and under the taskbar. While the window is maximized, its content is kept inside
//                  the work area of the monitor it is on: the content's margin is how far the window reaches past that work area on each
//                  side, measured in the screen's own pixels and converted at the window's display scale; again after a move to another
//                  monitor or a scale change. (A WM_GETMINMAXINFO answer is not used: WPF's own handling of that message comes first, so
//                  another answer never takes effect.) Everything else (double-click on the title bar, Snap, Win+Up/Down, the system menu)
//                  is Windows' own.
//   Dark frame     (WB-UX-07) Every Workbench window asks Windows for a dark title bar and frame, checks that Windows took it (with the
//                  attribute's older number on earlier Windows 10 builds), and asks again after a display, scale or color change. Its own
//                  drawing surface is painted with the window's dark background before anything else: WindowChrome leaves that surface
//                  transparent (so the frame's 1 pixel of glass shows), and a transparent surface is where Windows' frame color (the
//                  accent color, when "Show accent color on title bars" is on) would show if the window had nothing drawn there yet.
//   Opening place  (WB-UX-07) The main window opens centered on the display the program was opened on (the display Windows names when it
//                  starts the program, as File Explorer does; the main display otherwise), always wholly inside that display's work area.
//                  WPF's own centering placed it on another display with its title bar above the screen when that display's scale
//                  differed (seen on this project's PC: a 200% display beside a 100% one).
using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace CcsBackup.Ui
{
    static class WindowFrame
    {
        const int MONITOR_DEFAULTTONEAREST = 2, MONITOR_DEFAULTTOPRIMARY = 1;
        [StructLayout(LayoutKind.Sequential)] struct RECT { public int left, top, right, bottom; }
        [StructLayout(LayoutKind.Sequential)] struct POINT { public int x, y; }
        [StructLayout(LayoutKind.Sequential)] struct MONITORINFO { public int cbSize; public RECT rcMonitor, rcWork; public int dwFlags; }
        // the three text fields are pointers into the process's own start-up block: never strings, which the marshaller would free
        [StructLayout(LayoutKind.Sequential)]
        struct STARTUPINFO
        {
            public int cb; public IntPtr lpReserved, lpDesktop, lpTitle; public int dwX, dwY, dwXSize, dwYSize, dwXCountChars, dwYCountChars, dwFillAttribute, dwFlags;
            public short wShowWindow, cbReserved2; public IntPtr lpReserved2, hStdInput, hStdOutput, hStdError;
        }
        [DllImport("user32.dll")] static extern IntPtr MonitorFromWindow(IntPtr hwnd, int flags);
        [DllImport("user32.dll")] static extern IntPtr MonitorFromPoint(POINT pt, int flags);
        [DllImport("user32.dll")] static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFO info);
        [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
        [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
        [DllImport("kernel32.dll")] static extern void GetStartupInfoW(ref STARTUPINFO si);
        [DllImport("shcore.dll")] static extern int GetDpiForMonitor(IntPtr monitor, int type, out uint dpiX, out uint dpiY);
        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr h, int attribute, ref int value, int size);

        /// <summary>keeps the window's content (root) inside its monitor's work area while the window is maximized</summary>
        public static void Attach(Window w, FrameworkElement root)
        {
            void Fit()
            {
                if (w.WindowState != WindowState.Maximized) { if (root.Margin != default(Thickness)) root.Margin = default(Thickness); return; }
                try
                {
                    var h = new WindowInteropHelper(w).Handle; if (h == IntPtr.Zero || !GetWindowRect(h, out var r)) return;
                    var mi = new MONITORINFO { cbSize = Marshal.SizeOf(typeof(MONITORINFO)) };
                    if (!GetMonitorInfo(MonitorFromWindow(h, MONITOR_DEFAULTTONEAREST), ref mi)) return;
                    var k = VisualTreeHelper.GetDpi(w);
                    var m = new Thickness(Math.Max(0, mi.rcWork.left - r.left) / k.DpiScaleX, Math.Max(0, mi.rcWork.top - r.top) / k.DpiScaleY,
                                          Math.Max(0, r.right - mi.rcWork.right) / k.DpiScaleX, Math.Max(0, r.bottom - mi.rcWork.bottom) / k.DpiScaleY);
                    if (root.Margin != m) root.Margin = m;
                }
                catch (Exception) { }
            }
            void Later() => w.Dispatcher.BeginInvoke(new Action(Fit), DispatcherPriority.Loaded);
            w.StateChanged += (s, e) => Later(); w.SizeChanged += (s, e) => Later(); w.LocationChanged += (s, e) => Later(); w.DpiChanged += (s, e) => Later();
        }

        // ------------------------------------------------------------------ the dark frame and the window's own surface (WB-UX-07)
        /// <summary>what the last Prepare found, for a test: whether Windows took the dark frame, the caption color, and whether the window's
        /// surface was made opaque</summary>
        internal static string LastResult;
        /// <summary>the window's dark background color: the window's own brush when it is an opaque color, else the theme's (C.Bg)</summary>
        static Color Base(Window w) => w.Background is SolidColorBrush b && b.Color.A == 255 ? b.Color : Color.FromRgb(0x10, 0x10, 0x14);

        /// <summary>at SourceInitialized (after WindowChrome has set up the frame): the dark frame and the opaque surface, and again after a
        /// display, scale or color change (Windows and WindowChrome may set the frame up again then)</summary>
        public static void Prepare(Window w)
        {
            Apply(w);
            var src = HwndSource.FromHwnd(new WindowInteropHelper(w).Handle);
            src?.AddHook((IntPtr hwnd, int msg, IntPtr wp, IntPtr lp, ref bool handled) =>
            {
                // WM_DPICHANGED, WM_THEMECHANGED, WM_SETTINGCHANGE, WM_DWMCOMPOSITIONCHANGED, WM_DWMCOLORIZATIONCOLORCHANGED: after the window's own handling
                if (msg == 0x02E0 || msg == 0x031A || msg == 0x001A || msg == 0x031E || msg == 0x0320) w.Dispatcher.BeginInvoke(new Action(() => Apply(w)), DispatcherPriority.Loaded);
                return IntPtr.Zero;
            });
        }

        static void Apply(Window w)
        {
            try
            {
                var h = new WindowInteropHelper(w).Handle; if (h == IntPtr.Zero) return;
                int on = 1, hr = DwmSetWindowAttribute(h, 20, ref on, 4);                  // DWMWA_USE_IMMERSIVE_DARK_MODE
                if (hr < 0) hr = DwmSetWindowAttribute(h, 19, ref on, 4);                   // its number before Windows 10 20H1
                int caption = 0x00202024, hc = DwmSetWindowAttribute(h, 35, ref caption, 4);  // DWMWA_CAPTION_COLOR (Windows 11; the value it always had)
                bool opaque = false;
                var src = HwndSource.FromHwnd(h);
                if (src?.CompositionTarget != null) { src.CompositionTarget.BackgroundColor = Base(w); opaque = true; }
                LastResult = "dark frame " + (hr >= 0 ? "taken" : "refused 0x" + hr.ToString("X8")) + "; caption color " + (hc >= 0 ? "taken" : "not available 0x" + hc.ToString("X8")) + "; surface " + (opaque ? "opaque" : "not set");
            }
            catch (Exception ex) { LastResult = "not applied: " + ex.GetType().Name; }
        }

        // ------------------------------------------------------------------ where the main window opens (WB-UX-07)
        /// <summary>what the opening placement did, for a test</summary>
        internal static string LastPlacement;

        /// <summary>the main window (no owner) opens centered on the display the program was opened on, wholly inside its work area. Call
        /// before the window is shown; it takes effect at SourceInitialized (the window exists, WPF has sized it, nothing is shown yet).</summary>
        public static void PlaceAtStart(Window w)
        {
            if (w.Owner != null) return;
            w.WindowStartupLocation = WindowStartupLocation.Manual;
            // a host (a test, the page renderer) that gives the window its own place before showing it keeps that place: a Left or Top set
            // while the window has no handle yet is the host's (WPF itself fills them in only once the window exists)
            bool hostPlaced = false;
            EventHandler mark = (s, e) => { if (new WindowInteropHelper(w).Handle == IntPtr.Zero) hostPlaced = true; };
            var left = System.ComponentModel.DependencyPropertyDescriptor.FromProperty(Window.LeftProperty, typeof(Window)); var top = System.ComponentModel.DependencyPropertyDescriptor.FromProperty(Window.TopProperty, typeof(Window));
            left.AddValueChanged(w, mark); top.AddValueChanged(w, mark);
            w.SourceInitialized += (s, e) =>
            {
                left.RemoveValueChanged(w, mark); top.RemoveValueChanged(w, mark);
                if (hostPlaced || w.WindowStartupLocation != WindowStartupLocation.Manual) { LastPlacement = "left where its host put it"; return; }
                try { Place(w); } catch (Exception ex) { LastPlacement = "not placed: " + ex.GetType().Name; }
            };
        }

        static void Place(Window w)
        {
            var h = new WindowInteropHelper(w).Handle; if (h == IntPtr.Zero) return;
            var si = new STARTUPINFO { cb = Marshal.SizeOf(typeof(STARTUPINFO)) }; GetStartupInfoW(ref si);
            var mi = new MONITORINFO { cbSize = Marshal.SizeOf(typeof(MONITORINFO)) };
            IntPtr mon = IntPtr.Zero; string from;
            if ((si.dwFlags & 0x400) != 0 && si.hStdOutput != IntPtr.Zero && GetMonitorInfo(si.hStdOutput, ref mi)) { mon = si.hStdOutput; from = "the display the program was opened on"; }   // STARTF_USEMONITOR
            else { mon = MonitorFromPoint(new POINT(), MONITOR_DEFAULTTOPRIMARY); from = "the main display"; if (!GetMonitorInfo(mon, ref mi)) return; }
            uint dpi = 96; try { if (GetDpiForMonitor(mon, 0, out var dx, out _) == 0 && dx > 0) dpi = dx; } catch (Exception) { }
            double k = dpi / 96.0;
            int ww = mi.rcWork.right - mi.rcWork.left, wh = mi.rcWork.bottom - mi.rcWork.top;
            int cx = Math.Min(ww, (int)Math.Round(w.Width * k)), cy = Math.Min(wh, (int)Math.Round(w.Height * k));
            int x = mi.rcWork.left + (ww - cx) / 2, y = mi.rcWork.top + (wh - cy) / 2;
            const uint NoZ = 0x0004, NoActivate = 0x0010;
            for (int i = 0; i < 2; i++)
            {   // a move to a display with another scale makes WPF rescale the window (WM_DPICHANGED); the second pass puts it exactly in place
                SetWindowPos(h, IntPtr.Zero, x, y, cx, cy, NoZ | NoActivate);
                if (GetWindowRect(h, out var r) && r.left == x && r.top == y && r.right - r.left == cx && r.bottom - r.top == cy) break;
            }
            GetWindowRect(h, out var f);
            LastPlacement = "centered on " + from + " (" + dpi + " dpi): " + f.left + "," + f.top + " " + (f.right - f.left) + "x" + (f.bottom - f.top) + " in work area " + mi.rcWork.left + "," + mi.rcWork.top + "-" + mi.rcWork.right + "," + mi.rcWork.bottom;
        }
    }
}
