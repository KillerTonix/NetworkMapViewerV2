using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace NetworkMapViewerV2.Models
{
    public partial class NetworkDevice : ObservableObject, INotifyPropertyChanged
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

        private bool _isOnline = true; // Assume online until proven otherwise
        public bool IsOnline
        {
            get => _isOnline;
            set
            {
                if (_isOnline != value)
                {
                    bool wentDown = _isOnline == true && value == false;
                    bool wokeUp = _isOnline == false && value == true;

                    _isOnline = value;
                    OnPropertyChanged();

                    // FIRE THE NOTIFICATION CHECK!
                    if (wentDown || wokeUp)
                    {
                        Services.NotificationEngine.ProcessStateChange(this, wentDown, wokeUp);
                    }
                }
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}