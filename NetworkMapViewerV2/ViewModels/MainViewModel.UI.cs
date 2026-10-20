using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NetworkMapViewerV2.Data;
using NetworkMapViewerV2.Models;
using NetworkMapViewerV2.Services;
using NetworkMapViewerV2.Views;

using System.Diagnostics;
using System.Windows;

namespace NetworkMapViewerV2.ViewModels
{
    public partial class MainViewModel
    {
        [ObservableProperty]
        private bool _isEditingEnabled = false;

        [ObservableProperty]
        private bool _hasUnsavedChanges = false;

        [ObservableProperty]
        private bool _isGridVisible = false;


        [RelayCommand]
        private void ToggleHotkeys()
        {
            HelpWindow helpWindow = new(0) { Owner = Application.Current.MainWindow };
            helpWindow.ShowDialog();
        }
        [RelayCommand]
        private void ToggleAbout()
        {
            HelpWindow helpWindow = new(1) { Owner = Application.Current.MainWindow };
            helpWindow.ShowDialog();
        }


        [RelayCommand]
        private void Options()
        {
            OpenOptionsWindow(0); //options 0 page
        }


        [RelayCommand]
        private void ExitApplication()
        {
            Environment.Exit(0);
        }

        [RelayCommand]
        private void ToggleEditMode()
        {
            if (SelectedTab == null) return;

            // If we are currently IN Edit Mode and want to turn it OFF...
            if (SelectedTab.IsEditingEnabled)
            {
                if (SelectedTab.HasUnsavedChanges)
                {
                    var result = MessageBox.Show(
                        "You have unsaved edits. Do you want to save them to the database?",
                        "Save Changes?", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);

                    if (result == MessageBoxResult.Cancel)
                    {
                        return; // Abort everything, stay in Edit Mode
                    }
                    else if (result == MessageBoxResult.Yes)
                    {
                        SaveMap();
                        _ = ReloadMap();
                    }
                    else if (result == MessageBoxResult.No)
                    {
                        SelectedTab.HasUnsavedChanges = false;
                        _ = ReloadMap();
                    }
                }

                // We use a null check here just in case ReloadMap() completely destroyed and recreated the tab
                SelectedTab?.IsEditingEnabled = false; // EXPLICITLY turn off
            }
            else
            {
                // We are currently in View Mode and want to turn Edit Mode ON
                SelectedTab.IsEditingEnabled = true; // EXPLICITLY turn on
            }

            // Sync the global toolbar button state to match the tab's final state
            IsEditingEnabled = SelectedTab?.IsEditingEnabled ?? false;
        }

        [RelayCommand]
        private void ToggleGridMode()
        {
            IsGridVisible = !IsGridVisible;
        }


        [RelayCommand]
        private void PingOptions()
        {
            OpenOptionsWindow(2); //options 2 page
        }


        [RelayCommand]
        private void Report()
        {
            var result = MessageBox.Show(
                "Generate report as:\n\nYes = HTML report\nNo = CSV report\nCancel = Abort",
                "Generate Report",
                MessageBoxButton.YesNoCancel,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.Cancel) return;

            try
            {
                string path = result == MessageBoxResult.Yes
                    ? NotificationService.GenerateHtmlReport()
                    : NotificationService.GenerateCsvReport();

                Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to generate report:\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        [RelayCommand]
        private void NotificationOptions()
        {
            OpenOptionsWindow(3); //options 3 page
        }

        [RelayCommand]
        private void SearchOptions()
        {
            OpenOptionsWindow(4); //options 4 page
        }


        [RelayCommand]
        private void ActionLogs()
        {
            var window = new AuditLogWindow
            {
                Owner = Application.Current.MainWindow
            };

            window.OnSelectDeviceRequested = (mapName, targetId) =>
            {
                if (string.IsNullOrWhiteSpace(mapName)) return;

                // 1. Locate map tab in database
                var repo = new MapRepository();
                var map = repo.GetAvailableMaps()
                              .FirstOrDefault(m => m.MapName.Equals(mapName, StringComparison.OrdinalIgnoreCase));

                if (map == null) return;

                // 2. Open or switch to map tab
                OpenMapFromDatabase(map.MapId);

                // 3. Defer highlight until WPF finishes tab loading and canvas layout
                Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    if (SelectedTab == null) return;

                    NetworkDevice? targetDevice = null;
                    NetworkLabel? targetLabel = null;

                    // Strategy A: Resolve device by TargetID
                    if (targetId > 0)
                    {
                        targetDevice = SelectedTab.Devices.FirstOrDefault(d => d.DeviceId == targetId);
                        targetLabel = SelectedTab.Labels.FirstOrDefault(label => label.LabelId == targetId);
                    }
                    

                    if (targetDevice != null)
                    {
                        foreach (var dev in SelectedTab.Devices)
                        {
                            dev.IsSelected = false;
                        }

                        targetDevice.IsSelected = true;

                        HighlightedDeviceId = targetDevice.DeviceId;
                    }
                    if (targetLabel != null)
                    {                       
                        HighlightedDeviceId = targetLabel.LabelId;
                    }
                }, System.Windows.Threading.DispatcherPriority.Loaded);
            };

            window.ShowDialog();
        }

        private void OpenOptionsWindow(int tabIndex)
        {
            OptionsWindow options = new(tabIndex) { Owner = Application.Current.MainWindow };
            options.ShowDialog();

            if (options.Saved)
            {
                // 1. Reload the settings into memory
                _appSettings = SettingsService.Load();

                // 2. Force the MapCanvasView to completely redraw using the new settings
                // By briefly setting it to null, we trigger the DataContextChanged event in the View!
                var currentTab = SelectedTab;
                if (currentTab != null)
                {
                    SelectedTab = null!;
                    SelectedTab = currentTab;
                }

                if (PingService.IsRunning && SelectedTab != null)
                {
                    PingService.StopPinging();
                    PingService.StartPinging(SelectedTab.Devices);
                }
            }
        }


        [RelayCommand]
        private void AlignMapElements()
        {
            // 1. Ensure Edit Mode is active
            if (!SelectedTab.IsEditingEnabled) // Adjust to SelectedTab.IsEditMode if stored per tab
            {
                MessageBox.Show("Please enable Edit Mode before aligning map elements.", "Edit Mode Required", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (SelectedTab == null) return;

            const double LabelWidthStep = 125.0; // Standard label width grid step
            const double RowStep = 130.0;        // 120px height + 10px vertical gap

            const double MaxAllowedX = 1725.0;   // Boundary limit for X
            const double MaxAllowedY = 790.0;    // Boundary limit for Y

            bool elementsMoved = false;

            foreach (var label in SelectedTab.Labels)
            {
                double oldLeft = label.Left;
                double oldTop = label.Top;

                if (oldLeft > MaxAllowedX || oldTop > MaxAllowedY) continue;

                double newLeft = Math.Round(oldLeft / LabelWidthStep) * LabelWidthStep;
                double newTop = Math.Round(oldTop / RowStep) * RowStep;

                if (newLeft > MaxAllowedX || newTop > MaxAllowedY) continue;

                double deltaX = newLeft - oldLeft;
                double deltaY = newTop - oldTop;

                if (Math.Abs(deltaX) < 0.01 && Math.Abs(deltaY) < 0.01) continue;

                double labelRight = oldLeft + (label.Width > 0 ? label.Width : LabelWidthStep);
                double labelBottom = oldTop + (label.Height > 0 ? label.Height : 120.0);

                var overlappingDevices = SelectedTab.Devices.Where(d =>
                    d.Left >= oldLeft - 10 &&
                    d.Left <= labelRight + 10 &&
                    d.Top >= oldTop - 10 &&
                    d.Top <= labelBottom + 10
                ).ToList();

                // 1. Update model coordinates in memory
                label.Left = newLeft;
                label.Top = newTop;

                foreach (var device in overlappingDevices)
                {
                    device.Left += deltaX;
                    device.Top += deltaY;
                }

                elementsMoved = true;
            }

            if (elementsMoved)
            {
                SelectedTab.HasUnsavedChanges = true;

                // 2. Locate current MapCanvasView control and update canvas child positions visually
                if (Application.Current.MainWindow is Window mainWindow)
                {
                    var canvasView = FindVisualChild<MapCanvasView>(mainWindow);

                    // Sync positions directly on DrawingCanvas.Children
                    canvasView?.SyncVisualPositions();                           
                }

                SelectedTab.IsEditingEnabled = true; // Ensure Edit Mode remains active after alignment
            }
        }

        private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
        {
            int count = System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < count; i++)
            {
                var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);
                if (child is T typedChild && (child as FrameworkElement)?.IsVisible == true)
                    return typedChild;

                var result = FindVisualChild<T>(child);
                if (result != null) return result;
            }
            return null;
        }

    }
}
