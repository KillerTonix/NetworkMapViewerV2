using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NetworkMapViewerV2.Data;
using NetworkMapViewerV2.Models;
using NetworkMapViewerV2.Services;
using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;

namespace NetworkMapViewerV2.ViewModels
{
    public partial class MainViewModel : ObservableObject
    {
        private AppSettings _appSettings = new();
        private DispatcherTimer? _mapSyncTimer;
        private static MapRepository _repo = new();
        private bool _isCheckingForUpdates;
        public MainViewModel()
        {
            _appSettings = SettingsService.Load();

            // 1. Initialize Notification Engine Rules
            NotificationEngine.ActiveRules = _appSettings.ENS_Rules ?? [];

            // 2. Start Background Monitoring Service if Ping Autostart is enabled
            if (_appSettings.PingAutostart)
            {
                BackgroundPingService.Instance.Start();
            }

            // 3. Subscribe to global device state changes for UI updates
            BackgroundPingService.Instance.DeviceStatusChanged += OnDeviceStatusChanged;

            // 4. START THE MAP SYNC TIMER HERE!
            InitializeMapSyncTimer();

            LoadMapDirectories();

            Application.Current.Dispatcher.InvokeAsync(() =>
            {
                // Instantly load from MSSQL on startup!
                if (_appSettings.LastOpenedMapId > 0)
                {
                    OpenMapFromDatabase(_appSettings.LastOpenedMapId);
                }
            });

           
        }

        private async Task CheckForRemoteMapUpdatesAsync()
        {
            var tab = SelectedTab;
            if (_isCheckingForUpdates || tab == null || tab.MapId <= 0 || tab.HasUnsavedChanges || IsEditingEnabled)
                return;

            _isCheckingForUpdates = true;
            try
            {
                int serverVersion = await Task.Run(() => _repo.GetMapVersionAsync(tab.MapId));
                if (serverVersion > tab.Version)
                {
                    tab.Version = serverVersion;
                    await RefreshCurrentMapAsync(tab.MapId);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MapSync] Version check failed: {ex.Message}");  // DB down: skip, retry next tick
            }
            finally
            {
                _isCheckingForUpdates = false;
            }
        }

        /// <summary>
        /// Handles background ping state changes and updates open map tab UI elements.
        /// </summary>
        private void OnDeviceStatusChanged(string address, bool isOnline)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                // 1. Update devices across ALL open tabs in memory (not just SelectedTab)
                if (OpenTabs != null)
                {
                    foreach (var tab in OpenTabs)
                    {
                        if (tab.Devices == null) continue;

                        foreach (var device in tab.Devices.Where(d => d.Address == address))
                        {
                            device.IsOnline = isOnline;
                        }
                    }
                }
            });
        }

        public void InitializeMapSyncTimer()
        {
            _mapSyncTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(3)
            };
            _mapSyncTimer.Tick += async (s, e) => await CheckForRemoteMapUpdatesAsync();
            _mapSyncTimer.Start();
        }       

        public void StopMapSyncTimer()
        {
            _mapSyncTimer?.Stop();
        }
               

        /// <summary>
        /// Call this when options/settings are saved (e.g. from OptionsWindow) to restart pinging with new intervals.
        /// </summary>
        [RelayCommand]
        public void ApplyPingSettings()
        {
            _appSettings = SettingsService.Load();

            // Sync engine rules
            NotificationEngine.ActiveRules = _appSettings.ENS_Rules ?? [];

            // Restart background ping service with updated interval
            BackgroundPingService.Instance.Stop();
            if (_appSettings.PingAutostart)
            {
                BackgroundPingService.Instance.Start();
            }
        }


        /// <summary>
        /// Syncs the IP addresses of all currently opened tabs with BackgroundPingService.
        /// </summary>
        public void SyncOpenTabDevices()
        {
            if (OpenTabs == null) return;

            var activeAddresses = OpenTabs
                .Where(tab => tab.Devices != null)
                .SelectMany(tab => tab.Devices)
                .Select(d => d.Address)
                .Where(addr => !string.IsNullOrWhiteSpace(addr))
                .Distinct();

            BackgroundPingService.Instance.UpdateOpenTabAddresses(activeAddresses);
        }
    }
}