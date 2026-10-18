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
        public const int WM_NCCALCSIZE = 0x0083, WM_NCHITTEST = 0x0084, WM_NCLBUTTONDOWN = 0x00A1, WM_NCLBUTTONUP = 0x00A2, WM_NCMOUSELEAVE = 0x02A2;
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

        // taskbar icon (owner-approved look: app.ico's own small frame, drawn 1:1). The taskbar draws a window's button from its
        // big icon (no pinned shortcut) or its small icon (a separate button beside a pin) and shrinks a larger image crudely; WPF
        // sets a big icon of SM_CXICON (64 px at 200 %), which came out rough. Both icons are therefore the icon file's exact frame
        // at the small-icon size, so nothing is resampled whichever one the taskbar uses. The size follows the system DPI (the main
        // display's taskbar), not the window's monitor: a window on a 100 % screen would otherwise get 16 px, enlarged on a 200 % taskbar.
        const int WM_SETICON = 0x0080, SM_CXSMICON = 49, IMAGE_ICON = 1, APP_ICON_ID = 32512;
        [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")] static extern uint GetDpiForSystem();
        [DllImport("user32.dll")] static extern int GetSystemMetricsForDpi(int index, uint dpi);
        [DllImport("user32.dll")] static extern IntPtr LoadImage(IntPtr hinst, IntPtr name, int type, int cx, int cy, int flags);
        [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr icon);
        [DllImport("kernel32.dll")] static extern IntPtr GetModuleHandle(IntPtr name);
        static IntPtr appIcon;
        public static void ApplyTaskbarIcon(Window w)
        {
            try
            {
                var h = new WindowInteropHelper(w).Handle; if (h == IntPtr.Zero) return;
                uint dpi = GetDpiForSystem(); if (dpi == 0) dpi = 96;
                int size = GetSystemMetricsForDpi(SM_CXSMICON, dpi); if (size <= 0) return;
                var icon = LoadImage(GetModuleHandle(IntPtr.Zero), (IntPtr)APP_ICON_ID, IMAGE_ICON, size, size, 0);   // the exe's icon (ApplicationIcon = app.ico)
                if (icon == IntPtr.Zero) return;
                SendMessage(h, WM_SETICON, (IntPtr)1, icon); SendMessage(h, WM_SETICON, (IntPtr)0, icon);
                if (appIcon != IntPtr.Zero) DestroyIcon(appIcon); appIcon = icon;
            }
            catch { }
        }

        // Custom title bar without WindowChrome (it left the system caption in place on current Windows 11 builds): Windows keeps
        // its invisible resize frame on the left, right and bottom; only the caption is removed (the top of the client area is the
        // top of the window). Maximized, the top is moved down by the frame thickness so nothing is hidden above the screen.
        [StructLayout(LayoutKind.Sequential)] struct NCCALCSIZE_PARAMS { public RECT r0, r1, r2; public IntPtr pos; }
        [DllImport("user32.dll")] static extern IntPtr DefWindowProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")] static extern bool IsZoomed(IntPtr hwnd);
        public const int HTCAPTION = 2, HTTOP = 12, HTTOPLEFT = 13, HTTOPRIGHT = 14;
        public static void CalcClientWithoutCaption(IntPtr hwnd, IntPtr lParam)
        {
            var p = (NCCALCSIZE_PARAMS)Marshal.PtrToStructure(lParam, typeof(NCCALCSIZE_PARAMS)); int top = p.r0.top, left = p.r0.left;
            DefWindowProc(hwnd, WM_NCCALCSIZE, (IntPtr)1, lParam);
            p = (NCCALCSIZE_PARAMS)Marshal.PtrToStructure(lParam, typeof(NCCALCSIZE_PARAMS));
            p.r0.top = top;
            if (IsZoomed(hwnd))
            {   // maximized: exactly the monitor's work area, whatever the window overhangs
                var mi = new MONITORINFO(); var mon = MonitorFromWindow(hwnd, 2);
                if (mon != IntPtr.Zero && GetMonitorInfo(mon, mi))
                {
                    p.r0.left = Math.Max(p.r0.left, mi.rcWork.left); p.r0.top = Math.Max(top, mi.rcWork.top);
                    p.r0.right = Math.Min(p.r0.right, mi.rcWork.right); p.r0.bottom = Math.Min(p.r0.bottom, mi.rcWork.bottom);
                }
            }
            Marshal.StructureToPtr(p, lParam, false);
        }
        public static int DefaultHitTest(IntPtr hwnd, IntPtr lParam) { return DefWindowProc(hwnd, WM_NCHITTEST, IntPtr.Zero, lParam).ToInt32(); }

        public static int GetX(IntPtr lParam) { return (short)(lParam.ToInt64() & 0xFFFF); }
        public static int GetY(IntPtr lParam) { return (short)((lParam.ToInt64() >> 16) & 0xFFFF); }
    }
}
