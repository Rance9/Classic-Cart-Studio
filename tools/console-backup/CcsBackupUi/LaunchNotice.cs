// Classic Cart Studio console backup and restore window. Copyright (C) 2026 Rance9. GPL-3.0-or-later.
// CCS Console Workbench: a notice in the Workbench's own look for the moment before its window exists (a start it refuses, or an installed
// window it cannot reach): the title, the words and one OK button, on the Workbench's theme with a dark title bar. Never a Windows message box.
using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;

namespace CcsBackup.Ui
{
    public static class LaunchNotice
    {
        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr h, int a, ref int v, int s);

        /// <summary>shows the notice and returns when it is closed (OK, Enter, Escape or its X); needs a WPF Application in this thread</summary>
        public static void Show(string title, string body, ImageSource icon = null)
        {
            var w = new Window { Title = title, Width = 520, SizeToContent = SizeToContent.Height, ResizeMode = ResizeMode.NoResize, WindowStartupLocation = WindowStartupLocation.CenterScreen, UseLayoutRounding = true, SnapsToDevicePixels = true, Topmost = true };
            try { w.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/CcsBackupUi;component/Workbench.xaml", UriKind.Absolute) }); } catch { }
            w.Background = w.TryFindResource("B.Bg") as Brush ?? Brushes.Black;
            if (icon != null) w.Icon = icon;
            TextOptions.SetTextFormattingMode(w, TextFormattingMode.Ideal);
            var sp = new StackPanel { Margin = new Thickness(24, 20, 24, 20) };
            sp.Children.Add(new TextBlock { Text = title, FontSize = 17, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10) });
            sp.Children.Add(new TextBlock { Text = body, FontSize = 13.5, LineHeight = 20, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 18) });
            var ok = new Button { Content = "OK", MinWidth = 96, MinHeight = 34, IsDefault = true, IsCancel = true, HorizontalAlignment = HorizontalAlignment.Right, Style = w.TryFindResource("Btn.Primary") as Style };
            System.Windows.Automation.AutomationProperties.SetName(ok, "OK");
            ok.Click += (s, e) => w.Close();
            sp.Children.Add(ok);
            w.Content = sp;
            w.SourceInitialized += (s, e) => { try { var h = new WindowInteropHelper(w).Handle; int on = 1; DwmSetWindowAttribute(h, 20, ref on, 4); int c = 0x00202024; DwmSetWindowAttribute(h, 35, ref c, 4); } catch { } };
            w.Loaded += (s, e) => { ok.Focus(); Keyboard.Focus(ok); };
            w.ShowDialog();
        }
    }
}
