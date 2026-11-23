using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NetworkMapViewerV2.Models;
using NetworkMapViewerV2.Services;
using System.Collections.ObjectModel;
using System.Windows;

namespace NetworkMapViewerV2.ViewModels
{
    public partial class MainViewModel
    {
        [ObservableProperty]
        private ObservableCollection<MapTabState> _openTabs = [];
        [ObservableProperty]
        public MapTabState _selectedTab = new();

        /// <summary>
        /// Asks what to do with unsaved changes. Returns true if the tab may be closed.
        /// </summary>
        public async Task<bool> ConfirmCloseTabAsync(MapTabState tab)
        {
            if (!tab.HasUnsavedChanges) return true;

            var answer = MessageBox.Show(
                $"Map \"{tab.MapName}\" has unsaved changes.\n\nSave them before closing?",
                "Unsaved Changes", MessageBoxButton.YesNoCancel, MessageBoxImage.Warning);

            if (answer == MessageBoxResult.Cancel) return false;
            if (answer == MessageBoxResult.No) return true;

            // Yes: SaveMap saves the selected tab, so select this one first
            SelectedTab = tab;
            await SaveMap();
            return !tab.HasUnsavedChanges;   // save failed -> keep the tab open
        }

        [RelayCommand]
        public async Task CloseTab(MapTabState tabToClose)
        {
            if (tabToClose == null || !OpenTabs.Contains(tabToClose)) return;
            if (!await ConfirmCloseTabAsync(tabToClose)) return;
            CloseMapTab(tabToClose);
        }

        /// <summary>Closes a map tab without asking (used when the map no longer exists in the database).</summary>
        private void CloseMapTab(MapTabState tab)
        {
            OpenTabs.Remove(tab);
            if (SelectedTab == tab) SelectedTab = OpenTabs.FirstOrDefault()!;
            SyncOpenTabDevices();   // stop background-pinging the closed map's devices
        }

        partial void OnSelectedTabChanged(MapTabState value)
        {
            IsEditingEnabled = value?.IsEditingEnabled ?? false;

            if (value != null)
            {
                if (PingService.IsRunning) PingService.StopPinging();
                if (_appSettings.PingAutostart || IsPinging)
                {
                    PingService.StartPinging(value.Devices);
                    IsPinging = true;
                }
                              
                // Save the SQLite MapId to settings
                if (value.MapId > 0 && _appSettings?.LastOpenedMapId != value.MapId)
                {
                    _appSettings?.LastOpenedMapId = value.MapId;
                    SettingsService.SaveUserSpecific(_appSettings!);
                }
            }
        }


        [RelayCommand]
        public async Task CloseCurrentTab()
        {
            if (SelectedTab != null) await CloseTab(SelectedTab);
        }
    }
}
