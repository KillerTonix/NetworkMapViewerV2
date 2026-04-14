using Microsoft.Win32;
using NetworkMapViewerV2.Models;
using NetworkMapViewerV2.Services;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;

namespace NetworkMapViewerV2.Views
{
    public partial class OptionsWindow : Window
    {
        private readonly AppSettings _settings;
        public ObservableCollection<ExternalCommand> Commands { get; set; }
        public ObservableCollection<DeviceGroup> DeviceGroups { get; set; } 
        /// <summary>True if user pressed OK (settings should be re-read by caller).</summary>
        public bool Saved { get; private set; }

        public OptionsWindow(int tabIndex = 0)
        {
            InitializeComponent();

            _settings = SettingsService.Load();
            Commands = new ObservableCollection<ExternalCommand>(_settings.Commands);
            CommandsListBox.ItemsSource = Commands;

            // --- LOAD PING SETTINGS ---
            AutostartPingChk.IsChecked = _settings.PingAutostart;
            PingPeriodTextBox.Text = _settings.PingPeriodSeconds.ToString();

            // --- LOAD NOTIFICATION SETTINGS ---
            DeleteDaysTextBox.Text = _settings.DeleteEventsOlderThanDays.ToString();
            HideMessageTextBox.Text = _settings.HideMessageSeconds.ToString();
            NotificationHeaderTextBox.Text = _settings.NotificationHeaderTemplate;
            NotificationUpTextBox.Text = _settings.NotificationUpTemplate;
            NotificationDownTextBox.Text = _settings.NotificationDownTemplate;

            // --- LOAD SEARCH SETTINGS ---
            DeeperSearchRB.IsChecked = _settings.DeepperSearchMode;

            SettingsTC.SelectedIndex = tabIndex;
            var repo = new Data.MapRepository();
            DeviceGroups = new ObservableCollection<DeviceGroup>(repo.GetAllDeviceGroups());
            DeviceGroupsListBox.ItemsSource = DeviceGroups;
        }

        // ─── Commands CRUD ───────────────────────────────────────────────

        private void AddCommandButton_Click(object sender, RoutedEventArgs e)
        {
            var cmd = new ExternalCommand { Name = "New Command", Icon = "▶️", Path = "", Arguments = "{Address}" };
            if (ShowCommandEditor(cmd, "Add Command"))
            {
                Commands.Add(cmd);
                CommandsListBox.SelectedItem = cmd;
            }
        }

        private void AddCoppyCommandButton_Click(object sender, RoutedEventArgs e)
        {
            if (CommandsListBox.SelectedItem is ExternalCommand selected)
            {
                var copy = new ExternalCommand
                {
                    Name = selected.Name + " (Copy)",
                    Icon = selected.Icon,
                    Path = selected.Path,
                    Arguments = selected.Arguments
                };
                Commands.Insert(CommandsListBox.SelectedIndex + 1, copy);
                CommandsListBox.SelectedItem = copy;
            }
        }

        private void RemoveCommandButton_Click(object sender, RoutedEventArgs e)
        {
            if (CommandsListBox.SelectedItem is ExternalCommand selected)
            {
                var result = MessageBox.Show($"Remove command '{selected.Name}'?", "Confirm", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (result == MessageBoxResult.Yes)
                {
                    int idx = Commands.IndexOf(selected);
                    Commands.Remove(selected);
                    if (Commands.Count > 0)
                        CommandsListBox.SelectedIndex = Math.Min(idx, Commands.Count - 1);
                }
            }
        }

        private void EditCommandButton_CLick(object sender, RoutedEventArgs e)
        {
            if (CommandsListBox.SelectedItem is ExternalCommand selected)
            {
                // Edit a copy, then apply if OK
                var editCopy = new ExternalCommand
                {
                    Name = selected.Name,
                    Icon = selected.Icon,
                    Path = selected.Path,
                    Arguments = selected.Arguments
                };

                if (ShowCommandEditor(editCopy, "Edit Command"))
                {
                    selected.Name = editCopy.Name;
                    selected.Icon = editCopy.Icon;
                    selected.Path = editCopy.Path;
                    selected.Arguments = editCopy.Arguments;

                    // Refresh the listbox display
                    int idx = CommandsListBox.SelectedIndex;
                    CommandsListBox.ItemsSource = null;
                    CommandsListBox.ItemsSource = Commands;
                    CommandsListBox.SelectedIndex = idx;
                }
            }
        }

        private void MoveUpCommandButton_Click(object sender, RoutedEventArgs e)
        {
            int idx = CommandsListBox.SelectedIndex;
            if (idx > 0)
            {
                Commands.Move(idx, idx - 1);
                CommandsListBox.SelectedIndex = idx - 1;
            }
        }

        private void MoveDownCommandButton_Click(object sender, RoutedEventArgs e)
        {
            int idx = CommandsListBox.SelectedIndex;
            if (idx >= 0 && idx < Commands.Count - 1)
            {
                Commands.Move(idx, idx + 1);
                CommandsListBox.SelectedIndex = idx + 1;
            }
        }

        // ─── Inline Command Editor Dialog ────────────────────────────────

        private static bool ShowCommandEditor(ExternalCommand cmd, string title)
        {
            var dlg = new Window
            {
                Title = title,
                Width = 500,
                Height = 300,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                WindowStyle = WindowStyle.ToolWindow,
                ResizeMode = ResizeMode.NoResize
            };

            var grid = new Grid { Margin = new Thickness(15) };
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var lblName = new Label { Content = "Name:" }; Grid.SetRow(lblName, 0); Grid.SetColumn(lblName, 0);
            var txtName = new TextBox { Text = cmd.Name, Margin = new Thickness(0, 2, 0, 5) }; Grid.SetRow(txtName, 0); Grid.SetColumn(txtName, 1);

            var lblIcon = new Label { Content = "Icon (emoji):" }; Grid.SetRow(lblIcon, 1); Grid.SetColumn(lblIcon, 0);
            var txtIcon = new TextBox { Text = cmd.Icon, Margin = new Thickness(0, 2, 0, 5), MaxWidth = 60, HorizontalAlignment = HorizontalAlignment.Left }; Grid.SetRow(txtIcon, 1); Grid.SetColumn(txtIcon, 1);

            var lblExe = new Label { Content = "Executable Path:" }; Grid.SetRow(lblExe, 2); Grid.SetColumn(lblExe, 0);
            var txtExe = new TextBox { Text = cmd.Path, Margin = new Thickness(0, 2, 0, 5) }; Grid.SetRow(txtExe, 2); Grid.SetColumn(txtExe, 1);

            var lblArgs = new Label { Content = "Arguments:" }; Grid.SetRow(lblArgs, 3); Grid.SetColumn(lblArgs, 0);
            var txtArgs = new TextBox { Text = cmd.Arguments, Margin = new Thickness(0, 2, 0, 5) }; Grid.SetRow(txtArgs, 3); Grid.SetColumn(txtArgs, 1);

            var lblHint = new TextBlock { Text = "Use {Address} as placeholder for the device IP/hostname.", FontStyle = FontStyles.Italic, Foreground = System.Windows.Media.Brushes.Gray, Margin = new Thickness(0, 0, 0, 10) };
            Grid.SetRow(lblHint, 4); Grid.SetColumn(lblHint, 0); Grid.SetColumnSpan(lblHint, 2);

            var btnPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var btnOk = new Button { Content = "OK", Width = 80, Margin = new Thickness(0, 0, 10, 0), IsDefault = true };
            var btnCancel = new Button { Content = "Cancel", Width = 80, IsCancel = true };
            btnPanel.Children.Add(btnOk); btnPanel.Children.Add(btnCancel);
            Grid.SetRow(btnPanel, 6); Grid.SetColumn(btnPanel, 0); Grid.SetColumnSpan(btnPanel, 2);

            grid.Children.Add(lblName); grid.Children.Add(txtName);
            grid.Children.Add(lblIcon); grid.Children.Add(txtIcon);
            grid.Children.Add(lblExe); grid.Children.Add(txtExe);
            grid.Children.Add(lblArgs); grid.Children.Add(txtArgs);
            grid.Children.Add(lblHint);
            grid.Children.Add(btnPanel);

            dlg.Content = grid;

            bool result = false;
            btnOk.Click += (s, e) =>
            {
                cmd.Name = txtName.Text.Trim();
                cmd.Icon = txtIcon.Text.Trim();
                cmd.Path = txtExe.Text.Trim();
                cmd.Arguments = txtArgs.Text.Trim();
                result = true;
                dlg.Close();
            };
            btnCancel.Click += (s, e) => dlg.Close();

            dlg.ShowDialog();
            return result;
        }

        // ─── DEVICE GROUPS CRUD ──────────────────────────────────────────

        private void AddGroupButton_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new DeviceGroupWindow(null) { Owner = this }; // Null means "Create New"
            if (dlg.ShowDialog() == true)
            {
                DeviceGroups.Add(dlg.NewGroup);
            }
        }

        private void EditGroupButton_Click(object sender, RoutedEventArgs e)
        {
            if (DeviceGroupsListBox.SelectedItem is DeviceGroup selected)
            {
                var dlg = new DeviceGroupWindow(selected) { Owner = this }; // Pass the existing group in!
                if (dlg.ShowDialog() == true)
                {
                    // Refresh the listbox to show the new name/command
                    int idx = DeviceGroupsListBox.SelectedIndex;
                    DeviceGroupsListBox.ItemsSource = null;
                    DeviceGroupsListBox.ItemsSource = DeviceGroups;
                    DeviceGroupsListBox.SelectedIndex = idx;
                }
            }
        }

        private void RemoveGroupButton_Click(object sender, RoutedEventArgs e)
        {
            if (DeviceGroupsListBox.SelectedItem is DeviceGroup selected)
            {
                // Safety block: prevent deleting Group 1 to ensure standard icons always have a fallback
                if (selected.GroupId == 1)
                {
                    MessageBox.Show("You cannot delete the default Server group.", "Protected Group", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var result = MessageBox.Show($"Are you sure you want to delete the '{selected.GroupName}' type?\n\nDevices currently using this type will fall back to default icons.", "Confirm Delete", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (result == MessageBoxResult.Yes)
                {
                    var repo = new Data.MapRepository();
                    repo.DeleteDeviceGroup(selected.GroupId); // Delete from SQLite
                    DeviceGroups.Remove(selected);            // Remove from UI
                }
            }
        }



        // ─── OK / Cancel ─────────────────────────────────────────────────

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            // Save Commands
            _settings.Commands = [.. Commands];

            // Save Ping settings
            _settings.PingAutostart = AutostartPingChk.IsChecked == true;
            if (int.TryParse(PingPeriodTextBox.Text, out int period) && period > 0)
                _settings.PingPeriodSeconds = period;

            // Save Notification settings
            if (int.TryParse(DeleteDaysTextBox.Text, out int days) && days > 0)
                _settings.DeleteEventsOlderThanDays = days;

            if (int.TryParse(HideMessageTextBox.Text, out int hideSecs) && hideSecs > 0)
                _settings.HideMessageSeconds = hideSecs;

            _settings.NotificationHeaderTemplate = NotificationHeaderTextBox.Text;
            _settings.NotificationUpTemplate = NotificationUpTextBox.Text;
            _settings.NotificationDownTemplate = NotificationDownTextBox.Text;

            // Save Search settings
            _settings.DeepperSearchMode = DeeperSearchRB.IsChecked == true;

            // Write to disk
            SettingsService.Save(_settings);
            Saved = true;
            this.Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

       
    }
}
