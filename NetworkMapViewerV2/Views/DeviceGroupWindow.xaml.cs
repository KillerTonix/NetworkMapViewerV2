using Microsoft.Win32;
using NetworkMapViewerV2.Models;
using System;
using System.Windows;

namespace NetworkMapViewerV2.Views
{
    public partial class DeviceGroupWindow : Window
    {
        public DeviceGroup NewGroup { get; private set; }

        public DeviceGroupWindow(DeviceGroup? existingGroup = null)
        {
            InitializeComponent();

            // Load the commands from Settings so the user can pick the default double-click action!
            var appSettings = Services.SettingsService.Load();
            cmbCommands.ItemsSource = appSettings.Commands;

            if (existingGroup != null)
            {
                // We are EDITING an existing group
                this.Title = "Edit Device Type";
                NewGroup = existingGroup;

                txtName.Text = existingGroup.GroupName;
                txtIconPath.Text = existingGroup.IconPath;
                cmbCommands.SelectedValue = existingGroup.DefaultCommand;
            }
            else
            {
                // We are ADDING a new group
                NewGroup = new DeviceGroup();
                cmbCommands.SelectedValue = "Ping"; // Default fallback
            }
        }

        private void BtnBrowse_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog dlg = new()
            {
                Filter = "Image Files|*.png;*.gif;*.jpg;*.ico|All Files|*.*",
                Title = "Select Device Icon"
            };

            if (dlg.ShowDialog() == true)
            {
                txtIconPath.Text = dlg.FileName;
            }
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(txtName.Text))
            {
                MessageBox.Show("Please enter a name for this device type.", "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Transfer UI data to the Model
            NewGroup.GroupName = txtName.Text.Trim();
            NewGroup.IconPath = txtIconPath.Text;
            NewGroup.DefaultCommand = cmbCommands.SelectedValue?.ToString() ?? "Ping";

            // Save straight to SQLite!
            try
            {
                var repo = new Data.MapRepository();
                repo.SaveDeviceGroup(NewGroup);

                this.DialogResult = true;
                this.Close();
            }
            catch (Exception ex)
            {
                if (ex.Message.Contains("permission was denied"))
                {
                    MessageBox.Show($"Failed to save device group:\nYou don't have permission to modify the database.", "Database Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                else
                {
                    MessageBox.Show($"Failed to save device group:\n{ex.Message}", "Database Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = false;
            this.Close();
        }
    }
}