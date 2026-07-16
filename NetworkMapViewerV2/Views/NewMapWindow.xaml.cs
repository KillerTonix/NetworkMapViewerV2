using System.Windows;
using System.Windows.Controls;

namespace NetworkMapViewerV2.Views
{
    public partial class NewMapWindow : Window
    {
        public string MapName { get; private set; } = string.Empty;
        public string MapType { get; private set; } = string.Empty;

        public NewMapWindow()
        {
            InitializeComponent();

            // Instantly focus and select the text when the window opens
            this.Loaded += (s, e) =>
            {
                txtMapName.Focus();
                txtMapName.SelectAll();
            };
        }

        private void BtnCreate_Click(object sender, RoutedEventArgs e)
        {
            MapName = txtMapName.Text.Trim();

            // Prevent users from creating nameless maps!
            if (string.IsNullOrWhiteSpace(MapName))
            {
                MessageBox.Show("Map name cannot be empty.", "Invalid Name", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Extract the string from the ComboBoxItem safely
            MapType = (cmbMapType.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Head Office";

            this.DialogResult = true; // Signals success and closes the window
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = false;
        }
    }
}