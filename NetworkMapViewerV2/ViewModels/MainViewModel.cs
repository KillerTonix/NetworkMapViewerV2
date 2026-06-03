using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NetworkMapViewerV2.Models;
using NetworkMapViewerV2.Services;
using NetworkMapViewerV2.Views;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Reflection.Metadata.Ecma335;
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

        private AppSettings _appSettings = new();

        private DispatcherTimer? _autoRefreshTimer; // Timer for auto-refreshing tabs

        public MainViewModel()
        {
            _appSettings = SettingsService.Load();

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
            string newName = ShowNewTabDialog();
            if (!string.IsNullOrWhiteSpace(newName))
            {
                try
                {
                    // 1. Create the map in the database instantly
                    var repo = new Data.MapRepository();
                    int newMapId = repo.CreateNewMap(newName);

                    // 2. Create the blank Tab State tied to the real Database ID
                    var newTab = new MapTabState
                    {
                        MapId = newMapId,
                        MapName = newName,
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
        private void ReloadMap()
        {
            var state = SelectedTab;
            if (state != null && state.MapId > 0)
            {
                int mapId = state.MapId;
                CloseTab(state);
                OpenMapFromDatabase(mapId); // Re-fetches the clean state from SQLite
                SelectedTab.HasUnsavedChanges = false;
            }
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
            Application.Current.Shutdown();
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
        private void StartPing()
        {
            PingService.StartPinging(SelectedTab.Devices);
            IsPinging = true;

        }


        [RelayCommand]
        private void StopPing()
        {
            PingService.StopPinging();
            IsPinging = false;
        }

        [RelayCommand]
        private void PingOptions()
        {
            OpenOptionsWindow(1); //options 1 page
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
                // Purge old events first
                NotificationService.PurgeOldEvents(_appSettings.DeleteEventsOlderThanDays);

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
            OpenOptionsWindow(2); //options 2 page
        }

        [RelayCommand]
        private void SearchOptions()
        {
            OpenOptionsWindow(3); //options 2 page
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
        private bool _isPinging = false;

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
                    foreach (var device in SelectedTab.Devices) device.IsOnline = null;
                }
            }
            else if (SelectedTab != null)
            {
                PingService.StartPinging(SelectedTab.Devices);
                IsPinging = true;
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
                    _appSettings?.LastOpenedMapId = value.MapId;
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

                // 3. Restart the ping service if it was running
                if (PingService.IsRunning && SelectedTab != null)
                {
                    PingService.StopPinging();
                    PingService.StartPinging(SelectedTab.Devices);
                }
            }
        }


        private string ShowNewTabDialog()
        {
            var dlg = new Window
            {
                Title = "New Map",
                Width = 360,
                Height = 160,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                WindowStyle = WindowStyle.ToolWindow,
                ResizeMode = ResizeMode.NoResize
            };

            var sp = new StackPanel { Margin = new Thickness(15) };
            sp.Children.Add(new TextBlock { Text = "Enter a name for the new tab:", Margin = new Thickness(0, 0, 0, 8) });

            var txtName = new TextBox { Text = "New Map", Padding = new Thickness(4), FontSize = 14 };
            sp.Children.Add(txtName);

            var btnPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 12, 0, 0) };
            var btnOk = new Button { Content = "OK", Width = 80, Margin = new Thickness(0, 0, 10, 0), IsDefault = true };
            var btnCancel = new Button { Content = "Cancel", Width = 80, IsCancel = true };

            btnPanel.Children.Add(btnOk);
            btnPanel.Children.Add(btnCancel);
            sp.Children.Add(btnPanel);

            dlg.Content = sp;
            dlg.ContentRendered += (s, ev) => { txtName.Focus(); txtName.SelectAll(); };

            string result = string.Empty;
            btnOk.Click += (s, ev) => { result = txtName.Text.Trim(); dlg.Close(); };
            btnCancel.Click += (s, ev) => dlg.Close();

            dlg.ShowDialog();
            return result;
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
        private List<GlobalSearchResult> _globalSearchResults = new();

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



    }
}