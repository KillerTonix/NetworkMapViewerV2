using CommunityToolkit.Mvvm.ComponentModel;

namespace NetworkMapViewerV2.Models
{
    public partial class DeviceGroup : ObservableObject
    {
        [ObservableProperty] private int _groupId; // Auto-incremented by SQLite!
        [ObservableProperty] private string _groupName = "";
        [ObservableProperty] private string _iconPath = "";
        [ObservableProperty] private string _defaultCommand = "Ping";
        [ObservableProperty] private bool _isMapLink = false;
    }
}