using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;

namespace NetworkMapViewerV2.Models
{
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

        [ObservableProperty] private bool? _isOnline = null;
        [ObservableProperty] private string _hintImagePath = "";
        [ObservableProperty] private int? _targetMapId = null;
    }
}