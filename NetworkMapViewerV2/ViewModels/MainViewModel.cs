using CommunityToolkit.Mvvm.ComponentModel;
using NetworkMapViewerV2.Models;
using NetworkMapViewerV2.Services;
using System.Windows;
using System.Windows.Threading;

namespace NetworkMapViewerV2.ViewModels
{
    public partial class MainViewModel : ObservableObject
    {         
        private AppSettings _appSettings = new();
        private DispatcherTimer? _autoRefreshTimer; // Timer for auto-refreshing tabs

        public MainViewModel()
        {
            _appSettings = SettingsService.Load();

            LoadMapDirectories();
            Application.Current.Dispatcher.InvokeAsync(() =>
            {
                // Instantly load from SQLite on startup!
                if (_appSettings.LastOpenedMapId > 0)
                {
                    OpenMapFromDatabase(_appSettings.LastOpenedMapId);
                }
            });

            _autoRefreshTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMinutes(30) // Set to 10 minutes
            };
            _autoRefreshTimer.Tick += AutoRefreshTimer_Tick;
            _autoRefreshTimer.Start();
        }

        private void AutoRefreshTimer_Tick(object? sender, EventArgs e)
        {
            // SAFETY CHECK: Never reload if they are actively editing or have unsaved changes!
            if (SelectedTab != null && !SelectedTab.IsEditingEnabled && !SelectedTab.HasUnsavedChanges)
            {
                ReloadMap(); // Your existing method
            }
        }        
    }
}