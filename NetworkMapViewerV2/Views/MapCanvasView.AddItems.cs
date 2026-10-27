using NetworkMapViewerV2.Models;
using System.Windows;
using System.Windows.Controls;

namespace NetworkMapViewerV2.Views
{
    public partial class MapCanvasView : UserControl
    {
        private void AddSpecificDevice(int groupId, Point position)
        {
            if (_currentState == null) return;

            var newDevice = new NetworkDevice
            {
                MapId = _currentState.MapId,
                Address = "0.0.0.0",
                Left = position.X,
                Top = position.Y,
                GroupId = groupId
            };
            newDevice.Titles.Add("%Address");

            // --- NEW: Predefined hints based on device type (GroupId) ---
            switch (groupId)
            {
                case 1: // Windows PC
                    newDevice.Hints.Add("<b>NAME:</b> ");
                    newDevice.Hints.Add("<b>MAC:</b> ");
                    newDevice.Hints.Add("<b>IP:</b> ");
                    newDevice.Hints.Add("<b>CPU:</b>");
                    newDevice.Hints.Add("<b>RAM:</b> 16GB");
                    newDevice.Hints.Add("<b>SSD:</b>");
                    newDevice.Hints.Add("<b>GRAPHIC:</b> Intel(R) UHD Graphics 730");
                    newDevice.Hints.Add("<b>OS:</b> Microsoft Windows 11 Pro");
                    break;

                case 2: // Ubuntu Server
                    newDevice.Hints.Add("<b>NAME:</b> ");
                    newDevice.Hints.Add("<b>MAC:</b> ");
                    newDevice.Hints.Add("<b>IP:</b> ");
                    newDevice.Hints.Add("<b>CPU:</b> ");
                    newDevice.Hints.Add("<b>RAM:</b> ");
                    newDevice.Hints.Add("<b>OS:</b> Ubuntu 20.04 LTS");
                    break;

                case 9: // Grandstream Phone
                    newDevice.Hints.Add("<b>NAME:</b> GRANDSTREAM");
                    newDevice.Hints.Add("<b>MODEL:</b> GXP1628");
                    newDevice.Hints.Add("<b>MAC:</b> ");
                    newDevice.Hints.Add("<b>IP:</b> ");
                    break;

                case 12: // Printer
                    newDevice.Hints.Add("<b>NAME:</b> HP LaserJet");
                    newDevice.Hints.Add("<b>MODEL:</b> MFP ");
                    newDevice.Hints.Add("<b>MAC:</b> ");
                    newDevice.Hints.Add("<b>IP:</b> ");
                    newDevice.Hints.Add("<b>Host Name:</b> ");
                    break;

                default: // Fallback for any other device type not listed above
                    newDevice.Hints.Add("<b>NAME:</b> ");
                    newDevice.Hints.Add("<b>IP:</b> ");
                    break;
            }

            // AUTO-OPEN THE EDIT WINDOW!
            var dlg = new DevicePropertiesWindow(newDevice, true) { Owner = Window.GetWindow(this) };
            if (dlg.ShowDialog() == true)
            {
                _currentState.Devices.Add(newDevice);
                if (GlobalViewModel != null) _currentState?.HasUnsavedChanges = true;
                DrawMap(_currentState);
            }
        }

        private void AddLabel_Click(object sender, RoutedEventArgs e)
        {
            if (_currentState == null) return;

            var newLabel = new NetworkLabel
            {
                MapId = _currentState.MapId,
                Left = _lastRightClickPosition.X,
                Top = _lastRightClickPosition.Y,
                Width = 100,
                Height = 30,
                FontSize = 12,
                Foreground = "#000000"
            };
            newLabel.TextLines.Add("New Label Text");

            // AUTO-OPEN THE EDIT WINDOW!
            var dlg = new LabelPropertiesWindow(newLabel, true) { Owner = Window.GetWindow(this) };
            if (dlg.ShowDialog() == true)
            {
                _currentState.Labels.Add(newLabel);
                if (GlobalViewModel != null) _currentState?.HasUnsavedChanges = true;
                DrawMap(_currentState);
            }
        }

        private void BatchAddLabels_Click(object sender, RoutedEventArgs e)
        {
            var window = new BatchAddLabelsWindow(_lastRightClickPosition.X, _lastRightClickPosition.Y)
            {
                Owner = Window.GetWindow(this)
            };

            if (window.ShowDialog() == true && _currentState != null)
            {
                for (int i = 0; i < window.Quantity; i++)
                {
                    // 2. Calculate offsets. (Horizontal uses Width + 10 gap, Vertical uses 40 as requested)
                    double leftOffset = window.IsHorizontal ? (i * window.Width) : 0;
                    double topOffset = !window.IsHorizontal ? (i * window.Height) : 0;

                    var newLabel = new NetworkLabel
                    {
                        MapId = _currentState.MapId,
                        Left = window.StartLeft + leftOffset,
                        Top = window.StartTop + topOffset,
                        Width = window.LabelWidth,
                        Height = window.LabelHeight,
                        FontSize = 12,
                        Foreground = "#FFFFFF",
                        Background = window.SelectedColorHex,
                        BorderBrush = "#000000"
                    };
                    newLabel.TextLines.Add($"Label {i + 1}");
                    _currentState.Labels.Add(newLabel);
                }

                if (GlobalViewModel != null) _currentState?.HasUnsavedChanges = true;
                DrawMap(_currentState);
            }
        }



        private void BatchAdd_Click(object sender, RoutedEventArgs e)
        {
            // Creates a fast, dynamic popup window just for batch adding!
            var repo = new Data.MapRepository();
            var groups = repo.GetDeviceGroups();

            var window = new BatchAddDevicesWindow(groups)
            {
                Owner = Window.GetWindow(this)
            };

            if (window.ShowDialog() == true && _currentState != null)
            {                
                for (int i = 0; i < window.Quantity; i++)
                {
                    // 2. Calculate offsets. (Horizontal uses Width + 10 gap, Vertical uses 40 as requested)
                    double leftOffset = window.IsHorizontal ? (i * 125.0) : 0;
                    double topOffset = !window.IsHorizontal ? (i * 50.0) : 0;

                    var newDevice = new NetworkDevice
                    {
                        MapId = _currentState.MapId,
                        Address = "0.0.0.0",
                        GroupId = window.SelectedGroupId,
                        Left = _lastRightClickPosition.X + leftOffset,
                        Top = _lastRightClickPosition.Y + topOffset
                    };
                    newDevice.Titles.Add("%Address");
                    _currentState.Devices.Add(newDevice);
                }
                if (GlobalViewModel != null) _currentState?.HasUnsavedChanges = true;
                DrawMap(_currentState);
            }
        }

        private void AddDevice_Click(object sender, RoutedEventArgs e)
        {
            if (_currentState == null) return;

            var newDevice = new NetworkDevice
            {
                MapId = _currentState.MapId,
                Address = "0.0.0.0",
                Left = _lastRightClickPosition.X,
                Top = _lastRightClickPosition.Y,
                GroupId = 0
            };

            newDevice.Titles.Add("New Device"); // Adds text safely to the JSON list!

            _currentState.Devices.Add(newDevice);
            GlobalViewModel?.HasUnsavedChanges = true;
            DrawMap(_currentState);
        }
    }
}
