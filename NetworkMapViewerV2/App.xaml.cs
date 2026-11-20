using NetworkMapViewerV2.Data;
using System.IO;
using System.Windows;

namespace NetworkMapViewerV2
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            DispatcherUnhandledException += (s, args) =>
            {
                LogError(args.Exception);
                MessageBox.Show($"Unexpected error:\n{args.Exception.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                args.Handled = true;   // keep the app running
            };
            TaskScheduler.UnobservedTaskException += (s, args) => { LogError(args.Exception); args.SetObserved(); };
            AppDomain.CurrentDomain.UnhandledException += (s, args) => LogError(args.ExceptionObject as Exception);

            base.OnStartup(e);
            try
            {
                // Test DB connection before opening main window
                DatabaseService.InitializeDatabase();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Database is not accessible. Please check your connection or server settings.\n\nDetails: {ex.Message}", "Database Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);

                // Exit application cleanly without throwing an unhandled crash
                Shutdown(-1);
                return;
            }

            var main = new Views.MainWindow();
            MainWindow = main;
            main.Show();
        }
        protected override void OnExit(ExitEventArgs e)
        {
            base.OnExit(e);
        }

        private static void LogError(Exception? ex)
        {
            try
            {
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "NetworkMapViewerV2", "Logs");
                Directory.CreateDirectory(dir);
                File.AppendAllText(Path.Combine(dir, "errors.log"), $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}  {ex}\n\n");
            }
            catch { }
        }
    }

}
