using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NetworkMapViewerV2.Helpers.LocalFetcher;
using NetworkMapViewerV2.Models;
using NetworkMapViewerV2.Services;
using NetworkMapViewerV2.Views;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace NetworkMapViewerV2.ViewModels
{
    public partial class MainViewModel : ObservableObject
    {
        // 1. The list of open tabs. 
        // ObservableCollection automatically tells the UI to update when items are added/removed.
        [ObservableProperty]
        private ObservableCollection<MapTabState> _openTabs = [];

        // 2. The currently active tab
        [ObservableProperty]
        public MapTabState _selectedTab = new();

        // 3. Global App States
        [ObservableProperty]
        private bool _isEditingEnabled = false;

        [ObservableProperty]
        private bool _hasUnsavedChanges = false;

        [ObservableProperty]
        private bool _isGridVisible = false;

        [ObservableProperty]
        private ObservableCollection<MapTabState> _headOfficeMaps = [];

        [ObservableProperty]
        private ObservableCollection<MapTabState> _branchMaps = [];

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

        // --- COMMANDS ---
        // [RelayCommand] automatically turns this method into an ICommand that buttons can bind to.

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

        [RelayCommand]
        private void NewMap()
        {
            var (mapName, mapType) = ShowNewTabDialog();
            if (!string.IsNullOrWhiteSpace(mapName))
            {
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
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Failed to create map in database:\n{ex.Message}", "Database Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }


        [RelayCommand]
        public void OpenMap()
        {
            // Opens the SQLite selection dialog
            int? mapIdToOpen = ShowDatabaseMapSelector();
            if (mapIdToOpen.HasValue)
            {
                OpenMapFromDatabase(mapIdToOpen.Value);
            }
        }


        // ─── PURE SQLITE RELOAD ──────────────────────────────
        [RelayCommand]
        private async Task ReloadMap() // Notice the change to async Task!
        {
            var state = SelectedTab;
            if (state != null && state.MapId > 0)
            {
                var repo = new Data.MapRepository();

                // 1. Fetch the fresh data from SQLite quietly in the background
                var freshData = repo.LoadMap(state.MapId);

                // 2. Clear the old items and add the new ones directly to the EXISTING tab
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

                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();

                // 3. FIRE THE SAFE PING RECALCULATION
                await RecalculatePingsAsync(state.Devices);
            }
        }

        // Add this new helper method right below it!
        private static async Task RecalculatePingsAsync(IEnumerable<NetworkDevice> devices)
        {
            // ==========================================
            // THE SAFEGUARD: Only allow 2000 pings at a time
            // to prevent the memory/network avalanche!
            // ==========================================
            using var semaphore = new SemaphoreSlim(2000);
            var pingTasks = new List<Task>();

            foreach (var device in devices)
            {
                if (string.IsNullOrWhiteSpace(device.Address)) continue;

                pingTasks.Add(Task.Run(async () =>
                {
                    await semaphore.WaitAsync(); // Wait in line if 20 are already running
                    try
                    {
                        using var ping = new System.Net.NetworkInformation.Ping();
                        var reply = await ping.SendPingAsync(device.Address, 2000); // Strict 2-second timeout

                        // Changing this property instantly triggers your PingStatusImageConverter!
                        device.IsOnline = (reply.Status == System.Net.NetworkInformation.IPStatus.Success);
                    }
                    catch
                    {
                        device.IsOnline = false;
                    }
                    finally
                    {
                        semaphore.Release(); // Let the next ping in line start
                    }
                }));
            }

            // Wait for all background pings to completely finish
            await Task.WhenAll(pingTasks);
        }

        // ─── SAVE TO DATABASE (Ctrl + S) ─────────────────────
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
                MessageBox.Show($"Failed to save to database:\n{ex.Message}", "Save Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }


        [RelayCommand]
        private void ToggleHotkeys()
        {
            HelpWindow helpWindow = new(0) { Owner = Application.Current.MainWindow };
            helpWindow.ShowDialog();
        }
        [RelayCommand]
        private void ToggleAbout()
        {
            HelpWindow helpWindow = new(1) { Owner = Application.Current.MainWindow };
            helpWindow.ShowDialog();
        }


        [RelayCommand]
        private void Options()
        {
            OpenOptionsWindow(0); //options 0 page
        }


        [RelayCommand]
        private void ExitApplication()
        {
            Environment.Exit(0);
        }

        [RelayCommand]
        private void ToggleEditMode()
        {
            if (SelectedTab == null) return;

            // If we are currently IN Edit Mode and want to turn it OFF...
            if (SelectedTab.IsEditingEnabled)
            {
                if (SelectedTab.HasUnsavedChanges)
                {
                    var result = MessageBox.Show(
                        "You have unsaved edits. Do you want to save them to the database?",
                        "Save Changes?", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);

                    if (result == MessageBoxResult.Cancel)
                    {
                        return; // Abort everything, stay in Edit Mode
                    }
                    else if (result == MessageBoxResult.Yes)
                    {
                        SaveMap();
                    }
                    else if (result == MessageBoxResult.No)
                    {
                        SelectedTab.HasUnsavedChanges = false;
                        ReloadMap();
                    }
                }

                // We use a null check here just in case ReloadMap() completely destroyed and recreated the tab
                if (SelectedTab != null)
                {
                    SelectedTab.IsEditingEnabled = false; // EXPLICITLY turn off
                }
            }
            else
            {
                // We are currently in View Mode and want to turn Edit Mode ON
                SelectedTab.IsEditingEnabled = true; // EXPLICITLY turn on
            }

            // Sync the global toolbar button state to match the tab's final state
            IsEditingEnabled = SelectedTab?.IsEditingEnabled ?? false;
        }

        [RelayCommand]
        private void ToggleGridMode()
        {
            IsGridVisible = !IsGridVisible;
        }


        [RelayCommand]
        private void PingOptions()
        {
            OpenOptionsWindow(2); //options 2 page
        }

        [RelayCommand]
        private void Notification()
        {
            var events = NotificationService.LoadAllEvents();
            if (events.Count == 0)
            {
                MessageBox.Show("No events recorded yet.\nStart pinging to begin logging device state changes.",
                    "Notifications", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // Show last 20 events in a summary
            var recent = events.Skip(Math.Max(0, events.Count - 20)).ToList();
            string summary = string.Join("\n", recent.Select(ev =>
                $"[{ev.Timestamp:HH:mm:ss}] {ev.Status,-7} {ev.DeviceName} ({ev.Address})"));

            if (events.Count > 20)
                summary = $"... showing last 20 of {events.Count} events:\n\n" + summary;

            MessageBox.Show(summary, "Recent Events", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        [RelayCommand]
        private void Report()
        {
            var result = MessageBox.Show(
               "Generate report as:\n\n• Yes = HTML report\n• No = CSV report\n• Cancel = abort",
               "Generate Report", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);

            if (result == MessageBoxResult.Cancel) return;

            try
            {

                string path;
                if (result == MessageBoxResult.Yes)
                    path = NotificationService.GenerateHtmlReport();
                else
                    path = NotificationService.GenerateCsvReport();

                // Open the generated file in default app
                Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to generate report:\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        [RelayCommand]
        private void NotificationOptions()
        {
            OpenOptionsWindow(3); //options 3 page
        }

        [RelayCommand]
        private void SearchOptions()
        {
            OpenOptionsWindow(4); //options 4 page
        }


        [RelayCommand]
        private void ActionLogs()
        {
            ActionLogsWindow actionLogs = new();
            actionLogs.ShowDialog();
        }


        // Add the service to the top of your ViewModel
        private readonly PingService _pingService = new();

        [ObservableProperty]
        public partial bool IsPinging { get; set; } = false;

        public PingService PingService => _pingService;


       
        [RelayCommand]
        public void TogglePing()
        {
            if (PingService.IsRunning)
            {
                PingService.StopPinging();
                IsPinging = false;

                // Clear the colors when stopped
                if (SelectedTab != null)
                {
                    foreach (var device in SelectedTab.Devices) device.IsOnline = false;
                }
            }
            else if (SelectedTab != null)
            {
                PingService.StartPinging(SelectedTab.Devices);
                IsPinging = true;
            }
        }

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
        private async Task UpdateGroupData()
        {
            var tab = SelectedTab;
            if (tab == null || tab.Devices.Count == 0) return;

            var activeGroupIds = tab.Devices.GroupBy(d => d.GroupId).Select(g => new { Id = g.Key, Count = g.Count() }).ToList();
            var repo = new Data.MapRepository();
            var allDbGroups = repo.GetAllDeviceGroups().ToDictionary(g => g.GroupId, g => g.GroupName);

            var activeGroupsForDialog = new List<ActiveGroupItem>();
            foreach (var groupInfo in activeGroupIds)
            {
                switch (groupInfo.Id)
                {
                    case 1:
                    case 2:
                    case 3:
                    case 4:
                        string name = allDbGroups.TryGetValue(groupInfo.Id, out string? gName) ? gName : "Unknown";
                        activeGroupsForDialog.Add(new ActiveGroupItem { GroupId = groupInfo.Id, GroupName = name, DeviceCount = groupInfo.Count });
                        break;
                    default:
                        continue; // Skip any other group IDs

                }
            }

            if (activeGroupsForDialog.Count == 0) return;

            var dialog = new UpdateGroupDataWindow(activeGroupsForDialog, _appSettings.ScriptsPath)
            {
                Owner = Application.Current.MainWindow
            };

            if (dialog.ShowDialog() == true)
            {
                int targetGroupId = dialog.SelectedGroupId;
                string targetScript = dialog.SelectedScript;

                // ==========================================
                // THE NEW LOGIC: STRICTLY ONLINE DEVICES ONLY
                // ==========================================
                var devicesToUpdate = tab.Devices.Where(d =>
                    d.GroupId == targetGroupId &&
                    d.IsOnline == true && // <-- The crucial check!
                    !string.IsNullOrWhiteSpace(d.Address) &&
                    d.Address != "0.0.0.0").ToList();

                if (devicesToUpdate.Count == 0)
                {
                    MessageBox.Show("No ONLINE devices found in this group. Scan aborted.", "Skipped", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                // ==========================================
                // MULTI-THREADING: PROCESS 3 AT A TIME
                // ==========================================
                using var semaphore = new SemaphoreSlim(3); // Change this to 2 or 4 if you want to tweak the speed!
                var scanTasks = new List<Task>();

                foreach (var device in devicesToUpdate)
                {
                    scanTasks.Add(Task.Run(async () =>
                    {
                        await semaphore.WaitAsync(); // Wait in line if 3 are already running
                        try
                        {
                            if (targetScript == "Printer Web Scraper")
                            {
                                await AutoFill.RunPrinterAutoFill(this, device);
                            }
                            else if (targetScript == "Grandstream Web Scraper")
                            {
                                await AutoFill.RunGrandstreamAutoFill(this, device);
                            }
                            else
                            {
                                await AutoFill.RunAutoFillScript(this, device, targetScript);
                            }

                            // A tiny delay ensures the UI thread has time to draw the changes smoothly
                            await Task.Delay(100);
                        }
                        finally
                        {
                            semaphore.Release(); // Let the next device in line start
                        }
                    }));
                }

                // Wait for all the parallel batches to completely finish
                await Task.WhenAll(scanTasks);
                tab.HasUnsavedChanges = true;
                MessageBox.Show($"Update complete for {devicesToUpdate.Count} online devices.\nOffline devices were skipped.", "Finished", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        // --- NEW: Detect when the user switches tabs ---
        // --- Detect when the user switches tabs ---
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
                    _appSettings.LastOpenedMapId = value.MapId;
                    SettingsService.Save(_appSettings);
                }                
            }
        }


        private void OpenOptionsWindow(int tabIndex)
        {
            OptionsWindow options = new(tabIndex) { Owner = Application.Current.MainWindow };
            options.ShowDialog();

            if (options.Saved)
            {
                // 1. Reload the settings into memory
                _appSettings = SettingsService.Load();

                // 2. Force the MapCanvasView to completely redraw using the new settings
                // By briefly setting it to null, we trigger the DataContextChanged event in the View!
                var currentTab = SelectedTab;
                if (currentTab != null)
                {
                    SelectedTab = null;
                    SelectedTab = currentTab;
                }

                if (PingService.IsRunning && SelectedTab != null)
                {
                    PingService.StopPinging();
                    PingService.StartPinging(SelectedTab.Devices);
                }
            }
        }


        private (string, string) ShowNewTabDialog()
        {
            var dlg = new Window
            {
                Title = "New Map",
                Width = 360,
                Height = 240, // Increased height from 220 to 240 to fit the extra spacing perfectly
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                WindowStyle = WindowStyle.ToolWindow,
                ResizeMode = ResizeMode.NoResize
            };

            var sp = new StackPanel { Margin = new Thickness(15) };
            sp.Children.Add(new TextBlock { Text = "Enter a name for the new tab:", Margin = new Thickness(0, 0, 0, 8) });

            var txtName = new TextBox { Text = "New Map", Padding = new Thickness(4), FontSize = 14 };
            sp.Children.Add(txtName);

            // FIXED: Added a top margin of 15 so it doesn't touch the TextBox above it
            sp.Children.Add(new TextBlock { Text = "Select map type:", Margin = new Thickness(0, 15, 0, 8) });

            var cmbType = new ComboBox { ItemsSource = new List<string> { "Head Office", "Branch" }, SelectedIndex = 0, Padding = new Thickness(4), FontSize = 14 };
            sp.Children.Add(cmbType);

            var btnPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 15, 0, 0) };
            var btnOk = new Button { Content = "OK", Width = 80, Margin = new Thickness(0, 0, 10, 0), IsDefault = true };
            var btnCancel = new Button { Content = "Cancel", Width = 80, IsCancel = true };

            btnPanel.Children.Add(btnOk);
            btnPanel.Children.Add(btnCancel);
            sp.Children.Add(btnPanel);

            dlg.Content = sp;
            dlg.ContentRendered += (s, ev) => { txtName.Focus(); txtName.SelectAll(); };

            string resultName = string.Empty;
            string resultType = string.Empty;

            btnOk.Click += (s, ev) =>
            {
                resultName = txtName.Text.Trim();

                // FIXED: Uses SelectedItem and the null-coalescing operator to prevent crashes
                resultType = cmbType.SelectedItem?.ToString() ?? "Head Office";

                // FIXED: Tells WPF the user successfully submitted the dialog
                dlg.DialogResult = true;
            };

            // FIXED: Tells WPF the user backed out
            btnCancel.Click += (s, ev) => dlg.DialogResult = false;

            // ShowDialog pauses the code here until the window closes.
            // If they clicked OK, return the values. If they cancelled, return empty strings!
            if (dlg.ShowDialog() == true)
            {
                return (resultName, resultType);
            }

            return (string.Empty, string.Empty);
        }


        [RelayCommand]
        public void CloseCurrentTab()
        {
            // Closes whatever tab is currently selected when the user hits Ctrl+W
            if (SelectedTab != null)
            {
                CloseTab(SelectedTab);
            }
        }

        [RelayCommand]
        public void OutOfBoundsDevices()
        {
            // Tell the currently selected tab to broadcast the gather request
            SelectedTab?.RequestGatherDevices?.Invoke();
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

        private int? ShowDatabaseMapSelector()
        {
            var repo = new Data.MapRepository();
            var availableMaps = repo.GetAvailableMaps();

            if (availableMaps.Count == 0)
            {
                MessageBox.Show("There are no maps in the database yet.\nPlease use 'Import Legacy Map' first.", "No Maps Found", MessageBoxButton.OK, MessageBoxImage.Information);
                return null;
            }

            var dlg = new Window
            {
                Title = "Open Map from Database",
                Width = 350,
                Height = 400,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                WindowStyle = WindowStyle.ToolWindow,
                ResizeMode = ResizeMode.NoResize
            };

            var sp = new StackPanel { Margin = new Thickness(15) };
            sp.Children.Add(new TextBlock { Text = "Select a map to open:", Margin = new Thickness(0, 0, 0, 10) });

            var listBox = new ListBox { Height = 250, DisplayMemberPath = "Value", SelectedValuePath = "Key", ItemsSource = availableMaps };
            sp.Children.Add(listBox);

            var btnPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 15, 0, 0) };
            var btnOk = new Button { Content = "Open", Width = 80, Margin = new Thickness(0, 0, 10, 0), IsDefault = true };
            var btnCancel = new Button { Content = "Cancel", Width = 80, IsCancel = true };

            btnPanel.Children.Add(btnOk); btnPanel.Children.Add(btnCancel);
            sp.Children.Add(btnPanel);
            dlg.Content = sp;

            int? selectedMapId = null;
            btnOk.Click += (s, ev) => { if (listBox.SelectedValue != null) { selectedMapId = (int)listBox.SelectedValue; dlg.Close(); } };
            btnCancel.Click += (s, ev) => dlg.Close();

            dlg.ShowDialog();
            return selectedMapId;
        }


        // ─── SEARCH STATE ──────────────────────────────────────────────────
        [ObservableProperty] private bool _isSearchVisible = false;
        [ObservableProperty] private string _searchQuery = "";

        // This is the "Signal" we send to the UI to play the animation
        [ObservableProperty] private int _highlightedDeviceId = 0;

        private string _lastSearchQuery = "";
        private int _currentSearchIndex = 0;
        private List<GlobalSearchResult> _globalSearchResults = [];

        [RelayCommand]
        public void ToggleSearch()
        {
            IsSearchVisible = !IsSearchVisible;
            if (!IsSearchVisible)
            {
                SearchQuery = "";
                _globalSearchResults.Clear();
            }
        }


        [RelayCommand]
        public void PerformSearch()
        {
            string query = SearchQuery?.Trim().ToLower() ?? "";
            if (string.IsNullOrEmpty(query)) return;

            // --- PHASE 1: SQLITE SEARCH ---
            if (query != _lastSearchQuery)
            {
                _lastSearchQuery = query;
                _currentSearchIndex = -1;

                var repo = new Data.MapRepository();
                var currentSettings = SettingsService.Load();

                _globalSearchResults = repo.SearchDevices(query, currentSettings.DeepperSearchMode);

                if (_globalSearchResults.Count == 0)
                {
                    MessageBox.Show($"No devices found matching '{query}'.", "Search", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
            }

            // --- PHASE 2: CYCLE THROUGH RESULTS ---
            if (_globalSearchResults.Count > 0)
            {
                _currentSearchIndex++;
                if (_currentSearchIndex >= _globalSearchResults.Count) _currentSearchIndex = 0;

                var target = _globalSearchResults[_currentSearchIndex];

                // 1. Open the Map (or switch to it if it's already open)
                OpenMapFromDatabase(target.MapId);

                // 2. Fire the animation signal immediately!
                // (We set it to 0 first to guarantee the PropertyChanged event fires, even if you are searching for the same device twice)
                HighlightedDeviceId = 0;
                HighlightedDeviceId = target.DeviceId;
            }
        }

        public void StopAllScanners()
        {
            try
            {
                PingService.StopPinging();
            }
            catch { }
        }



    }
}