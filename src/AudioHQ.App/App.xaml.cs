using System.Windows;
using AudioHQ.Core;

namespace AudioHQ.App
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            Log.Write("=== AudioHQ started ===");

            DispatcherUnhandledException += (_, args) =>
            {
                Log.Write($"UNHANDLED: {args.Exception}");
                MessageBox.Show(args.Exception.Message, "AudioHQ - unexpected error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                args.Handled = true;
            };

            base.OnStartup(e);

            var window = new MainWindow();
            MainWindow = window;
            if (window.LaunchMinimized)
                Log.Write("Startup: main window kept hidden in system tray");
            else
                window.Show();
        }
    }
}
