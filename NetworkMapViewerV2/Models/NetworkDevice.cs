using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;

namespace NetworkMapViewerV2.Models
{
    // Inherits from ObservableObject (which handles INotifyPropertyChanged natively)
    public partial class NetworkDevice : ObservableObject
    {
        [ObservableProperty] private int _deviceId;
        [ObservableProperty] private int _mapId;
        [ObservableProperty] private int _groupId;
        [ObservableProperty] private double _left;
        [ObservableProperty] private double _top;
        [ObservableProperty] private string _address = "";

        [ObservableProperty] private ObservableCollection<string> _titles = [];
        [ObservableProperty] private ObservableCollection<string> _hints = [];

        [ObservableProperty] private string _hintImagePath = "";
        [ObservableProperty] private int? _targetMapId = null;

        public int FailedPingCount { get; set; } = 0;

        // THE FIX: Use standard property syntax, but power it with SetProperty!
        private bool _isOnline;

        public bool IsOnline
        {
            get => _isOnline;
            set => SetProperty(ref _isOnline, value); // Pure UI notification only
        }

        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set => SetProperty(ref _isSelected, value);
        }
    }
}