// Classic Cart Studio console backup and restore window. Copyright (C) 2026 Rance9. GPL-3.0-or-later.
// The window's taskbar and Alt-Tab icon, as Classic Cart Studio's main window sets it (tools/desktop/ClassicCartStudio/Native.cs,
// ApplyTaskbarIcon): the running executable's own icon (ApplicationIcon = workbench.ico), loaded at the small-icon size of
// the system DPI and given to Windows as both the small and the big icon, so the taskbar draws the icon file's own frame 1:1 whichever one
// it uses. Without this, WPF hands Windows the one frame a BitmapImage keeps of the icon (its smallest), which the taskbar draws tiny on a
// high-DPI screen. The Workbench's executables carry workbench.ico (the wrench); Classic Cart Studio's carries app.ico (the cartridge).
using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace CcsBackup.Ui
{
    static class WindowIcon
    {
        const int WM_SETICON = 0x0080, SM_CXSMICON = 49, IMAGE_ICON = 1, APP_ICON_ID = 32512, HOST_ICON_ID = 1;
        [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")] static extern uint GetDpiForSystem();
        [DllImport("user32.dll")] static extern int GetSystemMetricsForDpi(int index, uint dpi);
        [DllImport("user32.dll")] static extern IntPtr LoadImage(IntPtr hinst, IntPtr name, int type, int cx, int cy, int flags);
        [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr icon);
        [DllImport("kernel32.dll")] static extern IntPtr GetModuleHandle(IntPtr name);
        static IntPtr icon;

        public static void Apply(Window w)
        {
            try
            {
                var h = new WindowInteropHelper(w).Handle; if (h == IntPtr.Zero) return;
                uint dpi = GetDpiForSystem(); if (dpi == 0) dpi = 96;
                int size = GetSystemMetricsForDpi(SM_CXSMICON, dpi); if (size <= 0) return;
                // the executable's icon (workbench.ico): a .NET executable keeps it as resource 32512; the single-file CCS-Console-Workbench.exe (the
                // native host, native-host/build_host.sh) keeps the same workbench.ico as resource 1. Without the second, that window fell back to
                // WPF's smallest frame, drawn tiny on a high-DPI taskbar.
                var mod = GetModuleHandle(IntPtr.Zero);
                var i = LoadImage(mod, (IntPtr)APP_ICON_ID, IMAGE_ICON, size, size, 0);
                if (i == IntPtr.Zero) i = LoadImage(mod, (IntPtr)HOST_ICON_ID, IMAGE_ICON, size, size, 0);
                if (i == IntPtr.Zero) return;
                SendMessage(h, WM_SETICON, (IntPtr)1, i); SendMessage(h, WM_SETICON, (IntPtr)0, i);
                if (icon != IntPtr.Zero && icon != i) DestroyIcon(icon); icon = i;
            }
            catch { }
        }
    }
}
