// WPF application object: resources (Theme.xaml) and a safety net for unhandled exceptions (logged, shown once, never a raw dump).
using System;
using System.Windows;

namespace ClassicCartStudio
{
    public partial class StudioApp : Application
    {
        public StudioApp()
        {
            DispatcherUnhandledException += (s, e) =>
            {
                App.Log("unhandled: " + e.Exception);
                e.Handled = true;
                try { MessageDialog.Show(MainWindow, "Unexpected Problem", "Classic Cart Studio hit an unexpected problem but is still running. If the window looks wrong, choose Reload in the Studio menu; the details are in Diagnostics.", e.Exception.Message); } catch { }
            };
        }
    }
}
