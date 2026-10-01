using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NetworkMapViewerV2.Models;
using NetworkMapViewerV2.Services;
using System.Linq;
using System.Windows;

namespace NetworkMapViewerV2.ViewModels
{
    public partial class MainViewModel
    {
        // ─── SEARCH STATE ──────────────────────────────────────────────────
        [ObservableProperty] private bool _isSearchVisible = false;
        [ObservableProperty] private string _searchQuery = "";

        // Signal to UI for animation trigger
        [ObservableProperty] private int _highlightedDeviceId = 0;

        private string _lastSearchQuery = "";
        private int _currentSearchIndex = -1;
        private List<GlobalSearchResult> _globalSearchResults = [];

        // Partial method automatically called by CommunityToolkit.Mvvm when SearchQuery changes
        partial void OnSearchQueryChanged(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                ResetSearchState();
            }
        }

        [RelayCommand]
        public void ToggleSearch()
        {
            IsSearchVisible = true;
            SearchQuery = "";
            ResetSearchState();
        }

        [RelayCommand]
        public void ToggleSearchClose()
        {
            IsSearchVisible = false;
            SearchQuery = "";
            ResetSearchState();
        }

        private void ResetSearchState()
        {
            _lastSearchQuery = "";
            _currentSearchIndex = -1;
            _globalSearchResults.Clear();
        }

        [RelayCommand]
        public void PerformSearch()
        {
            string query = SearchQuery?.Trim().ToLower() ?? "";
            if (string.IsNullOrEmpty(query)) return;
                        
            if (query != _lastSearchQuery)
            {
                _lastSearchQuery = query;
                _currentSearchIndex = -1;

                var repo = new Data.MapRepository();
                var currentSettings = SettingsService.Load();

                var rawResults = repo.SearchDevices(query, currentSettings.DeepperSearchMode, currentSettings.EqualitySearchMode);

                if (rawResults == null || rawResults.Count == 0)
                {
                    MessageBox.Show($"No devices found matching '{query}'.", "Search", MessageBoxButton.OK, MessageBoxImage.Information);
                    ResetSearchState();
                    return;
                }

                // Prioritize current map, then group remaining by Map ID
                int currentMapId = SelectedTab?.MapId ?? -1;
                _globalSearchResults = [.. rawResults
                    .OrderByDescending(r => r.MapId == currentMapId)
                    .ThenBy(r => r.MapId)];
            }

            // --- PHASE 2: CYCLE THROUGH RESULTS ---
            if (_globalSearchResults.Count > 0)
            {
                _currentSearchIndex++;
                if (_currentSearchIndex >= _globalSearchResults.Count)
                {
                    _currentSearchIndex = 0;
                }

                var target = _globalSearchResults[_currentSearchIndex];

                // 1. Open or switch to map
                OpenMapFromDatabase(target.MapId);

                // 2. Fire animation signal
                HighlightedDeviceId = 0;
                HighlightedDeviceId = target.DeviceId;
            }
        }
    }
}