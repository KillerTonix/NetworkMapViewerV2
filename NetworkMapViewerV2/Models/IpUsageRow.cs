using CommunityToolkit.Mvvm.ComponentModel;

namespace NetworkMapViewerV2.Models
{
    public enum IpState
    {
        /// <summary>Saved on at least one map.</summary>
        Used,
        /// <summary>Not on any map; not checked by ping yet.</summary>
        Free,
        /// <summary>Not on any map and did not answer ping.</summary>
        FreeNoReply,
        /// <summary>Not on any map, but answered ping: something is using it.</summary>
        ActiveNotOnMap
    }

    /// <summary>One row of the IP usage grid: a single host address in the scanned CIDR range.</summary>
    public partial class IpUsageRow : ObservableObject
    {
        /// <summary>Numeric address, used so the grid sorts 10.0.0.2 before 10.0.0.10.</summary>
        public uint SortKey { get; init; }
        public string Address { get; init; } = "";

        [ObservableProperty] private IpState _state = IpState.Free;
        [ObservableProperty] private string _deviceName = "";
        [ObservableProperty] private string _mapName = "";
        [ObservableProperty] private string _note = "";

        public string StateText => State switch
        {
            IpState.Used => "Used",
            IpState.FreeNoReply => "Free · no ping reply",
            IpState.ActiveNotOnMap => "Answers ping · not on any map",
            _ => "Free"
        };

        partial void OnStateChanged(IpState value) => OnPropertyChanged(nameof(StateText));
    }
}
