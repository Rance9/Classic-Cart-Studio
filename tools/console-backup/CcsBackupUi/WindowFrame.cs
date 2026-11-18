// Classic Cart Studio console backup and restore window. Copyright (C) 2026 Rance9. GPL-3.0-or-later.
// The Workbench window maximized. The window draws its own title bar (WindowChrome); maximized, Windows places such a window past the
// monitor's work area by the width of its invisible resize border on every side (8 pixels at 100%), so its title bar, buttons and status
// strip would be cut off at the screen's edges and under the taskbar. While the window is maximized, its content is kept inside the work
// area of the monitor it is on: the content's margin is how far the window reaches past that work area on each side, measured in the
// screen's own pixels and converted at the window's display scale; again after a move to another monitor or a scale change. (A
// WM_GETMINMAXINFO answer is not used: WPF's own handling of that message comes first, so another answer never takes effect.) Everything
// else (double-click on the title bar, Snap, Win+Up/Down, the system menu) is Windows' own.
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
        const int MONITOR_DEFAULTTONEAREST = 2;
        [StructLayout(LayoutKind.Sequential)] struct RECT { public int left, top, right, bottom; }
        [StructLayout(LayoutKind.Sequential)] struct MONITORINFO { public int cbSize; public RECT rcMonitor, rcWork; public int dwFlags; }
        [DllImport("user32.dll")] static extern IntPtr MonitorFromWindow(IntPtr hwnd, int flags);
        [DllImport("user32.dll")] static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFO info);
        [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);

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
    }
}
