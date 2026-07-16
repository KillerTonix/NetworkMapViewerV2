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
