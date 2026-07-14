using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace NetworkMapViewerV2.Models
{
    public class MapTabState : INotifyPropertyChanged
    {
        public int MapId { get; set; }
        public string FilePath { get; set; } = string.Empty;    
        public string MapName { get; set; } = string.Empty;
        public List<NetworkDevice> Devices { get; set; } = [];
        public List<NetworkLabel> Labels { get; set; } = [];

        public Action? RequestGatherDevices;
        // ==========================================
        // --- NEW: UI STATE PROPERTIES ---
        // ==========================================
        private bool _isEditingEnabled;
        public bool IsEditingEnabled
        {
            get => _isEditingEnabled;
            // When this changes, tell the UI to update the Tab color!
            set { _isEditingEnabled = value; OnPropertyChanged(); }
        }

        private bool _hasUnsavedChanges;
        public bool HasUnsavedChanges
        {
            get => _hasUnsavedChanges;
            // When this changes, tell the UI to turn the Tab red!
            set { _hasUnsavedChanges = value; OnPropertyChanged(); }
        }

        public Action? TriggerRedraw { get; set; }

        // ==========================================
        // --- INOTIFYPROPERTYCHANGED LOGIC ---
        // ==========================================
        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
