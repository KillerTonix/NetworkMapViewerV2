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
        private bool _isOnline = true;

        public bool IsOnline
        {
            get => _isOnline;
            set
            {
                bool oldValue = _isOnline; // Snapshot the state before changing it

                // SetProperty automatically updates _isOnline AND fires the UI refresh event!
                // It returns 'true' only if the value actually changed.
                if (SetProperty(ref _isOnline, value))
                {
                    bool wentDown = oldValue == true && value == false;
                    bool wokeUp = oldValue == false && value == true;

                    if (wentDown || wokeUp)
                    {
                        Services.NotificationEngine.ProcessStateChange(this, wentDown, wokeUp);
                    }
                }
            }
        }
    }
}