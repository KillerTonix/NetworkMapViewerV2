using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NetworkMapViewerV2.Models;
using NetworkMapViewerV2.Services;
using System.Windows;

namespace NetworkMapViewerV2.ViewModels
{
    public partial class MainViewModel
    {
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

                var rawResults = repo.SearchDevices(query, currentSettings.DeepperSearchMode, currentSettings.EqualitySearchMode);

                if (rawResults.Count == 0)
                {
                    MessageBox.Show($"No devices found matching '{query}'.", "Search", MessageBoxButton.OK, MessageBoxImage.Information);
                    _globalSearchResults?.Clear();
                    return;
                }

                // Get the ID of the currently opened map (safely fallback to -1 if no map is open)
                int currentMapId = SelectedTab?.MapId ?? -1;

                // THE FIX: Sort the results so the current map is prioritized!
                // OrderByDescending on a boolean puts 'true' before 'false'.
                // ThenBy groups the remaining results neatly by their respective Map IDs.
                _globalSearchResults = rawResults
                    .OrderByDescending(r => r.MapId == currentMapId)
                    .ThenBy(r => r.MapId)
                    .ToList();
            }

            // --- PHASE 2: CYCLE THROUGH RESULTS ---
            if (_globalSearchResults != null && _globalSearchResults.Count > 0)
            {
                _currentSearchIndex++;
                if (_currentSearchIndex >= _globalSearchResults.Count) _currentSearchIndex = 0;

                var target = _globalSearchResults[_currentSearchIndex];

                // 1. Open the Map (or switch to it if it's already open)
                OpenMapFromDatabase(target.MapId);

                // 2. Fire the animation signal immediately!
                // (We set it to 0 first to guarantee the PropertyChanged event fires)
                HighlightedDeviceId = 0;
                HighlightedDeviceId = target.DeviceId;
            }
        }
    }
}
