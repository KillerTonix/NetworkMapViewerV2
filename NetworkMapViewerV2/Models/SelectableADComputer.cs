using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace NetworkMapViewerV2.Models
{
    public class SelectableADComputer : INotifyPropertyChanged
    {
        private bool _isSelected = true;

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected != value)
                {
                    _isSelected = value;
                    OnPropertyChanged();
                }
            }
        }

        public string Name { get; set; } = string.Empty;
        public string IPAddress { get; set; } = string.Empty;
        public DateTime WhenCreated { get; set; }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}