using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NetworkMapViewerV2.Models;
using NetworkMapViewerV2.Services;
using NetworkMapViewerV2.Views;
using System.Collections.ObjectModel;
using System.Windows;

namespace NetworkMapViewerV2.ViewModels
{
    public partial class MainViewModel
    {
        [ObservableProperty]
        private ObservableCollection<MapTabState> _headOfficeMaps = [];

        [ObservableProperty]
        private ObservableCollection<MapTabState> _branchMaps = [];

        // --- HEAD OFFICE MAP SELECTION ---
        private MapTabState? _selectedHeadOfficeMap;
        public MapTabState? SelectedHeadOfficeMap
        {
            get => _selectedHeadOfficeMap;
            set
            {
                // SetProperty updates the UI and returns true if the value actually changed
                if (SetProperty(ref _selectedHeadOfficeMap, value))
                {
                    if (value != null)
                    {
                        // 1. Open the map
                        OpenMapFromDatabase(value.MapId);

                        // 2. Reset the ComboBox
                        Application.Current.Dispatcher.InvokeAsync(() =>
                        {
                            SelectedHeadOfficeMap = null;
                        });
                    }
                }
            }
        }

        // --- BRANCH MAP SELECTION ---
        private MapTabState? _selectedBranchMap;
        public MapTabState? SelectedBranchMap
        {
            get => _selectedBranchMap;
            set
            {
                if (SetProperty(ref _selectedBranchMap, value))
                {
                    if (value != null)
                    {
                        // 1. Open the map
                        OpenMapFromDatabase(value.MapId);

                        // 2. Reset the ComboBox
                        Application.Current.Dispatcher.InvokeAsync(() =>
                        {
                            SelectedBranchMap = null;
                        });
                    }
                }
            }
        }


        [RelayCommand]
        private void NewMap()
        {
            var dlg = new NewMapWindow { Owner = Application.Current.MainWindow };

            if (dlg.ShowDialog() == true) // If they clicked "Create"
            {
                string mapName = dlg.MapName;
                string mapType = dlg.MapType;

                try
                {
                    // 1. Create the map in the database instantly
                    var repo = new Data.MapRepository();
                    int newMapId = repo.CreateNewMap(mapName, mapType);

                    // 2. Create the blank Tab State tied to the real Database ID
                    var newTab = new MapTabState
                    {
                        MapId = newMapId,
                        MapName = mapName,
                        Devices = [], // Start with empty lists
                        Labels = []
                    };

                    // 3. Add to UI and select it
                    OpenTabs.Add(newTab);
                    SelectedTab = newTab;

                    // 4. Save as the last opened map
                    if (_appSettings != null)
                    {
                        _appSettings.LastOpenedMapId = newMapId;
                        SettingsService.Save(_appSettings);
                    }

                    // 5. Update the ComboBoxes in the sidebar so the new map is visible!
                    LoadMapDirectories();
                }
                catch (Exception ex)
                {
                    if (ex.Message.Contains("permission was denied"))
                    {
                        MessageBox.Show($"Failed to create map in database::\nYou don't have permission to modify the database.", "Database Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                    else
                    {
                        MessageBox.Show($"Failed to create map in database:\n{ex.Message}", "Database Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            }
        }


        [RelayCommand]
        public void OpenMap()
        {
            var dlg = new OpenDeleteMapsWindow { Owner = Application.Current.MainWindow };

            if (dlg.ShowDialog() == true)
            {
                if (dlg.ActionTaken == MapDialogAction.Open)
                {
                    // 1. User clicked "Open"
                    OpenMapFromDatabase(dlg.SelectedMapId);
                }
                else if (dlg.ActionTaken == MapDialogAction.Delete)
                {
                    // 2. User clicked "Delete"
                    var repo = new Data.MapRepository();
                    repo.DeleteMap(dlg.SelectedMapId);

                    // Close the tab if it happens to be currently open
                    var tabToRemove = OpenTabs.FirstOrDefault(t => t.MapId == dlg.SelectedMapId);
                    if (tabToRemove != null)
                    {
                        OpenTabs.Remove(tabToRemove);
                        if (SelectedTab == tabToRemove)
                        {
                            SelectedTab = OpenTabs.FirstOrDefault();
                        }
                    }

                    // Refresh the UI dropdowns globally
                    LoadMapDirectories();

                    // Re-open the window automatically so they can see the map is gone, 
                    // or perform another action!
                    OpenMap();
                }
            }
        }
                

        // ─── PURE SQL RELOAD ──────────────────────────────
        [RelayCommand]
        private async Task ReloadMap() // Notice the change to async Task!
        {
            var state = SelectedTab;
            if (state != null && state.MapId > 0)
            {
                var repo = new Data.MapRepository();
                var freshData = repo.LoadMap(state.MapId);
                state.Devices.Clear();
                foreach (var device in freshData.Devices)
                {
                    state.Devices.Add(device);
                }

                state.Labels.Clear();
                foreach (var label in freshData.Labels)
                {
                    state.Labels.Add(label);
                }

                state.HasUnsavedChanges = false;

                SelectedTab = null;
                SelectedTab = state;

                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();

                await RecalculatePingsAsync(state.Devices);
            }
        }


        [RelayCommand]
        private void SaveMap()
        {
            if (SelectedTab == null || SelectedTab.MapId <= 0) return;

            try
            {
                var repo = new Data.MapRepository();

                // Save all devices and labels in memory to the database
                foreach (var device in SelectedTab.Devices) repo.UpdateDevice(device);
                foreach (var label in SelectedTab.Labels) repo.UpdateLabel(label);

                SelectedTab.HasUnsavedChanges = false;
            }
            catch (Exception ex)
            {
                if (ex.Message.Contains("permission was denied"))
                {
                    MessageBox.Show($"Failed to save to database:\nYou don't have permission to modify the database.", "Save Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                else
                {
                    MessageBox.Show($"Failed to save to database:\n{ex.Message}", "Save Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                SelectedTab.HasUnsavedChanges = false;
                ReloadMap();
            }
        }
               

        public void OpenMapFromDatabase(int mapId)
        {
            // 1. Primary Check: Is it already open by ID?
            foreach (var tab in OpenTabs)
            {
                if (tab.MapId == mapId)
                {
                    SelectedTab = tab;
                    return;
                }
            }

            try
            {
                var repository = new Data.MapRepository();
                var dbMapState = repository.LoadMap(mapId); // Loads entirely from SQLite!

                // FIX 1: Force the MapId to be correct just in case the repository forgot to set it!
                dbMapState.MapId = mapId;

                // FIX 2: Fallback Check - Is the tab open, but its MapId was 0?
                foreach (var tab in OpenTabs)
                {
                    if (tab.MapName == dbMapState.MapName)
                    {
                        // Silently fix the broken ID on the existing tab and switch to it!
                        tab.MapId = mapId;
                        SelectedTab = tab;
                        return;
                    }
                }

                // 3. If it's truly not open, add the new tab to the UI
                OpenTabs.Add(dbMapState);
                SelectedTab = dbMapState;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to load map from DB:\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }


        public void LoadMapDirectories()
        {
            var repo = new Data.MapRepository();
            var allMaps = repo.GetAvailableMaps(); // Uses the updated method from the previous step

            HeadOfficeMaps.Clear();
            BranchMaps.Clear();

            foreach (var map in allMaps)
            {
                if (map.MapType == "Branch")
                {
                    BranchMaps.Add(map);
                }
                else
                {
                    HeadOfficeMaps.Add(map);
                }
            }
        }
    }
}
