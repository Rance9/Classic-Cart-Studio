// Windows 11 chrome details for the custom title bar: dark system frame, rounded corners, and Snap Layouts on our own maximize
// button (Windows shows the layout flyout when the hit test reports HTMAXBUTTON). Everything degrades silently on older Windows.
using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace ClassicCartStudio
{
    internal static class Native
    {
        const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20, DWMWA_WINDOW_CORNER_PREFERENCE = 33, DWMWA_BORDER_COLOR = 34;
        const int DWMWCP_ROUND = 2;
        public const int WM_NCHITTEST = 0x0084, WM_NCLBUTTONDOWN = 0x00A1, WM_NCLBUTTONUP = 0x00A2, WM_NCMOUSELEAVE = 0x02A2;
        public const int HTCLIENT = 1, HTMAXBUTTON = 9;

        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        public static void ApplyDarkChrome(Window w, uint borderColorBgr)
        {
            try
            {
                var h = new WindowInteropHelper(w).Handle; if (h == IntPtr.Zero) return;
                int on = 1; DwmSetWindowAttribute(h, DWMWA_USE_IMMERSIVE_DARK_MODE, ref on, sizeof(int));
                int round = DWMWCP_ROUND; DwmSetWindowAttribute(h, DWMWA_WINDOW_CORNER_PREFERENCE, ref round, sizeof(int));
                int color = unchecked((int)borderColorBgr); DwmSetWindowAttribute(h, DWMWA_BORDER_COLOR, ref color, sizeof(int));
            }
            catch { }
        }

        // maximize must fill the monitor's work area exactly (WindowChrome would otherwise overshoot by the resize border)
        public const int WM_GETMINMAXINFO = 0x0024;
        [StructLayout(LayoutKind.Sequential)] public struct POINT { public int x, y; }
        [StructLayout(LayoutKind.Sequential)] public struct MINMAXINFO { public POINT ptReserved, ptMaxSize, ptMaxPosition, ptMinTrackSize, ptMaxTrackSize; }
        [StructLayout(LayoutKind.Sequential)] public struct RECT { public int left, top, right, bottom; }
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)] public class MONITORINFO { public int cbSize = Marshal.SizeOf(typeof(MONITORINFO)); public RECT rcMonitor, rcWork; public int dwFlags; }
        [DllImport("user32.dll")] static extern IntPtr MonitorFromWindow(IntPtr hwnd, int flags);
        [DllImport("user32.dll")] static extern bool GetMonitorInfo(IntPtr hMonitor, [In, Out] MONITORINFO info);
        public static bool FillMinMax(IntPtr hwnd, IntPtr lParam, double minW, double minH)
        {
            try
            {
                var mon = MonitorFromWindow(hwnd, 2 /* MONITOR_DEFAULTTONEAREST */); if (mon == IntPtr.Zero) return false;
                var mi = new MONITORINFO(); if (!GetMonitorInfo(mon, mi)) return false;
                var mmi = (MINMAXINFO)Marshal.PtrToStructure(lParam, typeof(MINMAXINFO));
                mmi.ptMaxPosition.x = mi.rcWork.left - mi.rcMonitor.left; mmi.ptMaxPosition.y = mi.rcWork.top - mi.rcMonitor.top;
                mmi.ptMaxSize.x = mi.rcWork.right - mi.rcWork.left; mmi.ptMaxSize.y = mi.rcWork.bottom - mi.rcWork.top;
                mmi.ptMinTrackSize.x = (int)minW; mmi.ptMinTrackSize.y = (int)minH;
                Marshal.StructureToPtr(mmi, lParam, true); return true;
            }
            catch { return false; }
        }

        // how far the maximized frame extends past the monitor's work area (WindowChrome keeps the resize border outside): per side, in physical px
        [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
        public static RECT MaximizedOvershoot(IntPtr hwnd)
        {
            var o = new RECT();
            try
            {
                RECT f; if (!GetWindowRect(hwnd, out f)) return o;   // the real window rect: WindowChrome keeps the invisible resize border outside the screen when maximized
                var mon = MonitorFromWindow(hwnd, 2); var mi = new MONITORINFO(); if (mon == IntPtr.Zero || !GetMonitorInfo(mon, mi)) return o;
                o.left = Math.Max(0, mi.rcWork.left - f.left); o.top = Math.Max(0, mi.rcWork.top - f.top); o.right = Math.Max(0, f.right - mi.rcWork.right); o.bottom = Math.Max(0, f.bottom - mi.rcWork.bottom);
            }
            catch { }
            return o;
        }

        public static int GetX(IntPtr lParam) { return (short)(lParam.ToInt64() & 0xFFFF); }
        public static int GetY(IntPtr lParam) { return (short)((lParam.ToInt64() >> 16) & 0xFFFF); }
    }
}
