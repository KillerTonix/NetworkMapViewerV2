using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Data.SqlClient;
using Microsoft.Win32;
using NetworkMapViewerV2.Helpers;
using NetworkMapViewerV2.Models;
using NetworkMapViewerV2.Services;
using NetworkMapViewerV2.Views;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Threading;

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


        [RelayCommand(CanExecute = nameof(CanEdit))]
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
                    if (newMapId <= 0) return;   // CreateNewMap already showed the error

                    // 2. Create the blank Tab State tied to the real Database ID
                    var newTab = new MapTabState
                    {
                        MapId = newMapId,
                        MapName = mapName,
                        MapType = mapType,
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
                        SettingsService.SaveUserSpecific(_appSettings);
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
        public async Task OpenMap()
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
                    int mapId = dlg.SelectedMapId;

                    // 1. Screenshot first, as a record of what the map looked like
                    string? shotPath = await SaveDeletedMapScreenshotAsync(mapId);
                    if (shotPath == null &&
                        MessageBox.Show("Could not save a screenshot of this map.\n\nDelete it anyway?", "Screenshot Failed",
                                        MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                    {
                        return;
                    }

                    // 2. Delete. LoadMapDirectories closes the tab because the map no longer exists.
                    if (_repo.DeleteMap(mapId))
                    {
                        LoadMapDirectories();
                        if (shotPath != null)
                            MessageBox.Show($"Map deleted.\n\nA screenshot was saved to:\n{shotPath}",
                                            "Map Deleted", MessageBoxButton.OK, MessageBoxImage.Information);
                    }

                    // 3. Re-open the list for the next action
                    await OpenMap();
                }
                else if (dlg.ActionTaken == MapDialogAction.Rename)
                {
                    try
                    {
                        _repo.RenameMap(dlg.SelectedMapId, dlg.NewMapName, dlg.NewMapType);                      

                        LoadMapDirectories(); // sidebar dropdowns
                    }
                    catch (InvalidOperationException ex)
                    {
                        MessageBox.Show(ex.Message, "Name Already Used", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                    catch (SqlException ex) when (ex.Number is 229 or 230)
                    {
                        MessageBox.Show("Failed to rename:\nYou don't have permission to modify the database.", "Rename Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Failed to rename map:\n{ex.Message}", "Rename Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    }

                    await OpenMap();
                }

            }
        }

        /// <summary>
        /// Opens the map, waits until it is drawn and saves a JPG of it to Documents\Network Map Viewer\Deleted Maps.
        /// Returns the file path, or null if it failed.
        /// </summary>
        private async Task<string?> SaveDeletedMapScreenshotAsync(int mapId)
        {
            try
            {
                OpenMapFromDatabase(mapId);   // opens the tab, or switches to it if already open
                var tab = SelectedTab;
                if (tab == null || tab.MapId != mapId) return null;

                // Let WPF finish drawing the map before taking the picture
                await Application.Current.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);
                await Task.Delay(300);

                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Network Map Viewer", "Deleted Maps");
                Directory.CreateDirectory(dir);

                string safeName = string.Concat(tab.MapName.Split(Path.GetInvalidFileNameChars()));
                string path = Path.Combine(dir, $"{safeName}_{DateTime.Now:yyyyMMdd_HHmmss}.jpg");

                MapExporter.SaveCanvasScreenshotToJpeg(Application.Current.MainWindow!, path, qualityLevel: 95);
                return path;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[Delete] Screenshot failed: {ex.Message}");
                return null;
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
                state.Version = freshData.Version;
                state.PendingDeletedDeviceIds.Clear();
                state.PendingDeletedLabelIds.Clear();
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

                SelectedTab = null!;
                SelectedTab = state;

                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();

                await RecalculatePingsAsync(state.Devices);
            }
        }


        [RelayCommand(CanExecute = nameof(CanEdit))]
        private async Task SaveMap()
        {
            var tab = SelectedTab;
            if (tab == null || tab.MapId <= 0) return;

            try
            {
                _repo.SaveMapChanges(tab);
                tab.HasUnsavedChanges = false;
            }
            catch (Data.MapConflictException ex)
            {
                var answer = MessageBox.Show(
                    $"{ex.Message}\n\nYour changes were NOT saved.\n\n" +
                    "Yes – reload the map now (your changes will be lost)\n" +
                    "No – keep your changes on screen (saving will keep failing until you reload)",
                    "Map changed by someone else", MessageBoxButton.YesNo, MessageBoxImage.Warning);

                if (answer == MessageBoxResult.Yes) await ReloadMap();
            }
            catch (SqlException ex) when (ex.Number is 229 or 230)   // SQL Server "permission denied"
            {
                MessageBox.Show("Failed to save:\nYou don't have permission to modify the database.", "Save Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to save. Nothing was changed in the database.\n\n{ex.Message}", "Save Error", MessageBoxButton.OK, MessageBoxImage.Error);
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
                var dbMapState = repository.LoadMap(mapId); // Loads entirely from MSSQL!

                // FIX 1: Force the MapId to be correct just in case the repository forgot to set it!
                dbMapState.MapId = mapId;
                               

                // 3. If it's truly not open, add the new tab to the UI
                OpenTabs.Add(dbMapState);
                SelectedTab = dbMapState;

                SyncOpenTabDevices();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to load map from DB:\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public void LoadMapDirectories()
        {
            ApplyMapList(new Data.MapRepository().GetAvailableMaps());
        }

        /// <summary>Fills the sidebar dropdowns and keeps open tab titles in step with the database.</summary>
        private List<string> ApplyMapList(List<MapTabState> allMaps)
        {
            _mapListSignature = BuildMapListSignature(allMaps);

            HeadOfficeMaps.Clear();
            BranchMaps.Clear();
            foreach (var map in allMaps)
            {
                if (map.MapType == "Branch") BranchMaps.Add(map);
                else HeadOfficeMaps.Add(map);
            }

            // Someone may have renamed a map that is open here
            foreach (var tab in OpenTabs)
            {
                var dbMap = allMaps.FirstOrDefault(m => m.MapId == tab.MapId);
                if (dbMap == null) continue;
                if (tab.MapName != dbMap.MapName) tab.MapName = dbMap.MapName;
                if (tab.MapType != dbMap.MapType) tab.MapType = dbMap.MapType;
            }
            var closed = new List<string>();
            foreach (var tab in OpenTabs.Where(t => t.MapId > 0 && !allMaps.Any(m => m.MapId == t.MapId)).ToList())
            {
                closed.Add(tab.MapName);
                CloseMapTab(tab);
            }
            return closed;
        }

        private static string BuildMapListSignature(List<MapTabState> maps) =>
            string.Join("|", maps.Select(m => $"{m.MapId}:{m.MapType}:{m.MapName}"));


        /// <summary>
        /// Reloads map devices and labels from the database when a remote version change is detected.
        /// </summary>
        private async Task RefreshCurrentMapAsync(int mapId)
        {
            var tab = OpenTabs.FirstOrDefault(t => t.MapId == mapId);
            if (tab == null) return;

            try
            {
                // Load on a background thread so the window stays responsive while the DB answers
                var freshData = await Task.Run(() => _repo.LoadMap(mapId));

                // While we were loading, the user may have closed the tab or started editing it
                if (!OpenTabs.Contains(tab) || tab.HasUnsavedChanges || tab.IsEditingEnabled) return;

                tab.Version = freshData.Version;   // the version of the data we actually loaded
                tab.PendingDeletedDeviceIds.Clear();
                tab.PendingDeletedLabelIds.Clear();

                tab.Devices.Clear();
                foreach (var device in freshData.Devices) tab.Devices.Add(device);

                tab.Labels.Clear();
                foreach (var label in freshData.Labels) tab.Labels.Add(label);

                tab.HasUnsavedChanges = false;
                tab.TriggerRedraw?.Invoke();
                SyncOpenTabDevices();

                if (SelectedTab == tab)
                    await RecalculatePingsAsync(tab.Devices);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to auto-refresh map {mapId}: {ex.Message}");
            }
        }



        [RelayCommand]
        private void ExportMap(object? parameter)
        {
            var mainWindow = Application.Current.MainWindow;
            if (mainWindow == null)
            {
                MessageBox.Show("Could not locate main application window.", "Screenshot Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            var dialog = new SaveFileDialog
            {
                Title = "Save Map Canvas Screenshot",
                FileName = $"{SelectedTab?.MapName ?? "Map"}_Export_{DateTime.Now:yyyyMMdd_HHmmss}",
                Filter = "JPEG Image (*.jpg)|*.jpg;*.jpeg",
                DefaultExt = ".jpg"
            };

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    MapExporter.SaveCanvasScreenshotToJpeg(mainWindow, dialog.FileName, qualityLevel: 95);

                    MessageBox.Show($"Map screenshot saved successfully to:\n{dialog.FileName}", "Export Complete", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Failed to save screenshot:\n{ex.Message}", "Screenshot Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }
    }
    
}
