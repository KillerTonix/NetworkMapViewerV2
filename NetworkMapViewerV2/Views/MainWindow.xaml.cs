using NetworkMapViewerV2.Services;
using NetworkMapViewerV2.ViewModels;
using System.Windows;

namespace NetworkMapViewerV2.Views
{
    public partial class MainWindow : Window
    {
        public MainViewModel ViewModel { get; }

        public MainWindow()
        {
            InitializeComponent();
            ViewModel = new MainViewModel();
            this.DataContext = ViewModel;

            this.Closed += (sender, args) =>
            {
                // 1. Tell your ViewModel/PingService to stop the network loop
                try { ViewModel?.PingService.StopPinging(); } catch { }

                // 2. Release all MS SQL connection pools 
                try { Microsoft.Data.SqlClient.SqlConnection.ClearAllPools(); } catch { }

                // 3. Annihilate the background process
                System.Diagnostics.Process.GetCurrentProcess().Kill();
            };
            NotificationEngine.ActiveRules = SettingsService.Load().ENS_Rules;
        }

        private void TxtSearch_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            // If the search box just became visible...
            if (txtSearch.IsVisible)
            {
                // Wait for the UI to finish rendering it, then focus it!
                Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    txtSearch.Focus();
                    txtSearch.SelectAll(); // Highlights any existing text so you can instantly overwrite it
                }, System.Windows.Threading.DispatcherPriority.Input);
            }
        }


        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
            Environment.Exit(0);
        }

        
    }
}