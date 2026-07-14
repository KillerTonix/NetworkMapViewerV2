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

            // Ensure the SQLite database exists before any windows open
            DatabaseService.InitializeDatabase();
        }
        protected override void OnExit(ExitEventArgs e)
        {
            base.OnExit(e);
            Environment.FailFast("Force closing application from App.OnExit");
        }
    }

}
