using NetworkMapViewerV2.Data;
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
        }
        protected override void OnExit(ExitEventArgs e)
        {
            base.OnExit(e);
            Environment.FailFast("Force closing application from App.OnExit");
        }
    }

}
