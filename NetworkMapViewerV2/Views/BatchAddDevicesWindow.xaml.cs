using System.Collections.Generic;
using System.Windows;
// Add using for your DeviceTypeItem namespace here

namespace NetworkMapViewerV2.Views
{
    public partial class BatchAddDevicesWindow : Window
    {
        public int Quantity { get; private set; }
        public int SelectedGroupId { get; private set; }
        public bool IsHorizontal { get; private set; }

        public BatchAddDevicesWindow(object groupsDataSource) // Pass your List<DeviceTypeItem> here
        {
            InitializeComponent();
            CmbGroups.ItemsSource = (System.Collections.IEnumerable)groupsDataSource;
            if (CmbGroups.Items.Count > 0) CmbGroups.SelectedIndex = 0;
        }

        private void BtnSpawn_Click(object sender, RoutedEventArgs e)
        {
            if (int.TryParse(TxtCount.Text, out int count) && CmbGroups.SelectedValue != null)
            {
                Quantity = count;
                SelectedGroupId = (int)CmbGroups.SelectedValue;
                IsHorizontal = RbHorizontal.IsChecked == true;

                DialogResult = true;
            }
            else
            {
                MessageBox.Show("Please enter a valid quantity and select a group.");
            }
        }
    }
}