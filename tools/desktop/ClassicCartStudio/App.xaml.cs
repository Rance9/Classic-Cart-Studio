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
                try { MessageDialog.Show(MainWindow, "Something went wrong", "Classic Cart Studio hit an unexpected problem. The details were written to the log (Studio menu → Diagnostics).", e.Exception.Message); } catch { }
            };
        }
    }
}
