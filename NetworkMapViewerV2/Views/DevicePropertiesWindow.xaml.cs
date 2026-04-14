using Microsoft.Win32;
using NetworkMapViewerV2.Models;
using System.Windows;
using System.Windows.Input;

namespace NetworkMapViewerV2.Views
{
    public class DeviceTypeItem
    {
        public string DisplayName { get; set; } = string.Empty;
        public int GroupId { get; set; }
    }

    public partial class DevicePropertiesWindow : Window
    {
        public NetworkDevice EditingDevice { get; private set; }

        public DevicePropertiesWindow(NetworkDevice device, bool isEditing)
        {
            InitializeComponent();
            EditingDevice = device;

            // --- LOAD DESCRIPTION TAB DATA ---
            txtName.Text = string.Join(Environment.NewLine, device.Titles);

            // Use DisplayHints so the text box doesn't accidentally show the "IMG|..." or "MAP|..." tags!
            txtHint.Text = string.Join(Environment.NewLine, device.Hints);

            // Use your new helper property!
            txtImagePath.Text = device.HintImagePath;

            // --- LOAD TYPE TAB DATA ---
            txtAddress.Text = device.Address;

            // 1. Create ONE repository connection for the whole window
            var repo = new Data.MapRepository();

            // 2. Fetch the device types dynamically from the SQLite Database!
            var deviceTypes = repo.GetAllDeviceGroups();
            cmbType.ItemsSource = deviceTypes;

            // Since we are using the new DeviceGroup model, we bind to GroupName
            cmbType.DisplayMemberPath = "GroupName";
            cmbType.SelectedValuePath = "GroupId";

            cmbType.SelectedValue = device.GroupId;

            // 3. Load the Maps into your target map dropdown
            if (cmbTargetMap != null)
            {
                cmbTargetMap.ItemsSource = repo.GetAvailableMaps();
                cmbTargetMap.DisplayMemberPath = "Value";  // Shows the Map Name
                cmbTargetMap.SelectedValuePath = "Key";    // Saves the Map ID

                if (device.TargetMapId.HasValue)
                {
                    cmbTargetMap.SelectedValue = device.TargetMapId.Value;
                }
            }

            // --- TOGGLE EDIT MODE ---
            if (isEditing)
            {
                txtName.IsReadOnly = false;
                txtHint.IsReadOnly = false;
                btnBold.IsEnabled = true;
                txtImagePath.IsReadOnly = false;
                BtnBrowseImage.IsEnabled = true;
                txtAddress.IsReadOnly = false;
                cmbType.IsEnabled = true;
                if (cmbTargetMap != null) cmbTargetMap.IsEnabled = true;
            }
            else
            {
                txtName.IsReadOnly = true;
                txtHint.IsReadOnly = true;
                btnBold.IsEnabled = false;
                txtImagePath.IsReadOnly = true;
                BtnBrowseImage.IsEnabled = false;
                txtAddress.IsReadOnly = true;
                cmbType.IsEnabled = false;
                if (cmbTargetMap != null) cmbTargetMap.IsEnabled = false;
                this.Title += " {View Mode}";
            }

            UpdateFieldVisibility();
        }

        private void BtnBold_Click(object sender, RoutedEventArgs e)
        {
            string openTag = "<b>";
            string closeTag = "</b>";

            if (txtHint.SelectionLength > 0)
            {
                int selectionStart = txtHint.SelectionStart;
                string selectedText = txtHint.SelectedText;

                txtHint.Text = txtHint.Text.Remove(selectionStart, txtHint.SelectionLength)
                                           .Insert(selectionStart, openTag + selectedText + closeTag);

                txtHint.SelectionStart = selectionStart + openTag.Length + selectedText.Length + closeTag.Length;
            }
            else
            {
                int caretIndex = txtHint.CaretIndex;
                txtHint.Text = txtHint.Text.Insert(caretIndex, openTag + closeTag);
                txtHint.SelectionStart = caretIndex + openTag.Length;
            }

            txtHint.Focus();
        }

        private void BtnBrowseImage_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog dlg = new()
            {
                Filter = "Image Files|*.jpg;*.jpeg;*.png;*.gif|All Files|*.*",
                Title = "Select Hint Image"
            };

            if (dlg.ShowDialog() == true)
            {
                txtImagePath.Text = dlg.FileName;
            }
        }

        private void BtnOk_Click(object sender, RoutedEventArgs e)
        {
            // Safely update Titles
            EditingDevice.Titles.Clear();
            foreach (var line in txtName.Text.Split([Environment.NewLine], StringSplitOptions.RemoveEmptyEntries))
                EditingDevice.Titles.Add(line);

            // Safely update Hints
            EditingDevice.Hints.Clear();
            foreach (var line in txtHint.Text.Split([Environment.NewLine], StringSplitOptions.RemoveEmptyEntries))
                EditingDevice.Hints.Add(line);

            EditingDevice.HintImagePath = txtImagePath.Text;
            EditingDevice.Address = txtAddress.Text;

            if (cmbType.SelectedValue != null)
            {
                EditingDevice.GroupId = (int)cmbType.SelectedValue;
            }

            this.DialogResult = true;
            this.Close();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = false;
            this.Close();
        }

        // You can safely delete the "Browse Map" button from your XAML now since everything is in SQLite!
        private void BtnBrowseMap_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("Map files are now stored entirely inside the SQLite Database. External .map linking is obsolete!", "Info", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void CmbType_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            UpdateFieldVisibility();
        }

        private void UpdateFieldVisibility()
        {
            // Because we bound the ComboBox to your DeviceGroup list, the SelectedItem is the actual object!
            if (cmbType.SelectedItem is Models.DeviceGroup selectedGroup)
            {
                if (selectedGroup.IsMapLink)
                {
                    // Hide Address
                    lblAddress.Visibility = Visibility.Collapsed;
                    txtAddress.Visibility = Visibility.Collapsed;

                    // Show Map Dropdown
                    lblTargetMap.Visibility = Visibility.Visible;
                    cmbTargetMap.Visibility = Visibility.Visible;
                }
                else
                {
                    // Show Address
                    lblAddress.Visibility = Visibility.Visible;
                    txtAddress.Visibility = Visibility.Visible;

                    // Hide Map Dropdown
                    lblTargetMap.Visibility = Visibility.Collapsed;
                    cmbTargetMap.Visibility = Visibility.Collapsed;
                }
            }
        }

        private void Window_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.Escape)
                this.Close();
        }
               

        private void txtHint_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.B)
            {
                BtnBold_Click(sender, e);
                e.Handled = true;
            }
        }
    }
}