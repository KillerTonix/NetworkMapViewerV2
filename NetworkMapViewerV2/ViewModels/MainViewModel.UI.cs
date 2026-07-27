using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
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
                        ReloadMap();
                    }
                    else if (result == MessageBoxResult.No)
                    {
                        SelectedTab.HasUnsavedChanges = false;
                        ReloadMap();
                    }
                }

                // We use a null check here just in case ReloadMap() completely destroyed and recreated the tab
                if (SelectedTab != null)
                {
                    SelectedTab.IsEditingEnabled = false; // EXPLICITLY turn off
                }
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
        private void Notification()
        {
            var events = NotificationService.LoadAllEvents();
            if (events.Count == 0)
            {
                MessageBox.Show("No events recorded yet.\nStart pinging to begin logging device state changes.",
                    "Notifications", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // Show last 20 events in a summary
            var recent = events.Skip(Math.Max(0, events.Count - 20)).ToList();
            string summary = string.Join("\n", recent.Select(ev =>
                $"[{ev.Timestamp:HH:mm:ss}] {ev.Status,-7} {ev.DeviceName} ({ev.Address})"));

            if (events.Count > 20)
                summary = $"... showing last 20 of {events.Count} events:\n\n" + summary;

            MessageBox.Show(summary, "Recent Events", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        [RelayCommand]
        private void Report()
        {
            var result = MessageBox.Show(
               "Generate report as:\n\n• Yes = HTML report\n• No = CSV report\n• Cancel = abort",
               "Generate Report", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);

            if (result == MessageBoxResult.Cancel) return;

            try
            {

                string path;
                if (result == MessageBoxResult.Yes)
                    path = NotificationService.GenerateHtmlReport();
                else
                    path = NotificationService.GenerateCsvReport();

                // Open the generated file in default app
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
            ActionLogsWindow actionLogs = new();
            actionLogs.ShowDialog();
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
                    SelectedTab = null;
                    SelectedTab = currentTab;
                }

                if (PingService.IsRunning && SelectedTab != null)
                {
                    PingService.StopPinging();
                    PingService.StartPinging(SelectedTab.Devices);
                }
            }
        }
    }
}
