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

    /// <summary>
    /// One row of the IPAM grid: a single host address in the scanned CIDR range. The editable columns
    /// (name, type, map, note) are compared with a saved snapshot so the window knows what really changed.
    /// </summary>
    public partial class IpUsageRow : ObservableObject
    {
        /// <summary>Numeric address, used so the grid sorts 10.0.0.2 before 10.0.0.10.</summary>
        public uint SortKey { get; init; }
        public string Address { get; init; } = "";

        /// <summary>Planned block of this address (Printers / Phones / PCs / NUCs); empty when cut range is off.</summary>
        public string Pool { get; init; } = "";

        [ObservableProperty] private IpState _state = IpState.Free;
        [ObservableProperty] private string _deviceName = "";
        [ObservableProperty] private string _deviceType = "";
        [ObservableProperty] private string _mapName = "";
        [ObservableProperty] private string _note = "";

        // Identity of the saved device behind this row (0 = no device yet).
        public int DeviceId { get; set; }
        public int MapId { get; set; }
        public int GroupId { get; set; }

        /// <summary>How many saved devices carry this IP. More than one means the row cannot be edited here.</summary>
        public int EntryCount { get; set; }
        public string Warning { get; set; } = "";
        public bool IsEditable => EntryCount <= 1;
        public bool IsSaving { get; set; }

        public string StateText => State switch
        {
            IpState.Used => Warning.Length > 0 ? $"Used · {Warning}" : "Used",
            IpState.FreeNoReply => "Free · no ping reply",
            IpState.ActiveNotOnMap => "Answers ping · not on any map",
            _ => "Free"
        };

        partial void OnStateChanged(IpState value)
        {
            OnPropertyChanged(nameof(StateText));
            OnPropertyChanged(nameof(CanAddToMap));
        }

        /// <summary>The "Add to map" button is offered for addresses that are not on any map yet.</summary>
        public bool CanAddToMap => DeviceId == 0 && State != IpState.Used;

        // ─── CHANGE TRACKING ──────────────────────────────────────────────

        private string _savedName = "", _savedType = "", _savedMap = "", _savedNote = "";

        private static string Clean(string? value) => (value ?? "").Trim();

        public bool NameChanged => Clean(DeviceName) != _savedName;
        public bool TypeChanged => Clean(DeviceType) != _savedType;
        public bool MapChanged => Clean(MapName) != _savedMap;
        public bool NoteChanged => Clean(Note) != _savedNote;
        public bool IsDirty => NameChanged || TypeChanged || MapChanged || NoteChanged;

        /// <summary>Takes the current values as the saved state (after loading, or after a successful save).</summary>
        public void AcceptChanges()
        {
            _savedName = DeviceName = Clean(DeviceName);
            _savedType = DeviceType = Clean(DeviceType);
            _savedMap = MapName = Clean(MapName);
            _savedNote = Note = Clean(Note);
        }

        /// <summary>Puts the saved values back (after a failed save, or a refused edit).</summary>
        public void RejectChanges()
        {
            DeviceName = _savedName;
            DeviceType = _savedType;
            MapName = _savedMap;
            Note = _savedNote;
        }
    }
}
