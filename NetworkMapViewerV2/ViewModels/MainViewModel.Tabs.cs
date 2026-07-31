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

        [RelayCommand]
        public void AddNewTab(string tabName)
        {
            var newTab = new MapTabState
            {
                MapName = tabName
            };

            OpenTabs.Add(newTab);
            SelectedTab = newTab; // Automatically switch to the new tab
        }

        [RelayCommand]
        public void CloseTab(MapTabState tabToClose)
        {
            if (tabToClose != null && OpenTabs.Contains(tabToClose))
            {
                OpenTabs.Remove(tabToClose);
            }
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
                    SettingsService.Save(_appSettings);
                }
            }
        }


        [RelayCommand]
        public void CloseCurrentTab()
        {
            // Closes whatever tab is currently selected when the user hits Ctrl+W
            if (SelectedTab != null)
            {
                if (IsEditingEnabled)
                {
                    var result = MessageBox.Show("You have unsaved changes. Are you sure you want to close this tab?", "Unsaved Changes", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                    if (result == MessageBoxResult.Yes)
                    {
                        CloseTab(SelectedTab);
                    }
                }
                else
                {
                    CloseTab(SelectedTab);
                }
            }
        }

        [RelayCommand]
        public void OutOfBoundsDevices()
        {
            // Tell the currently selected tab to broadcast the gather request
            SelectedTab?.RequestGatherDevices?.Invoke();
        }
       
    }
}
