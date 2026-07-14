using System.IO;
using System.Windows;

namespace NetworkMapViewerV2.Views
{
    public class ActiveGroupItem
    {
        public int GroupId { get; set; }
        public string GroupName { get; set; } = string.Empty;
        public int DeviceCount { get; set; }
        public string DisplayText => $"{GroupName} ({DeviceCount} devices)";
    }

    public partial class UpdateGroupDataWindow : Window
    {
        public int SelectedGroupId { get; private set; }
        public string SelectedScript { get; private set; } = string.Empty;

        public UpdateGroupDataWindow(List<ActiveGroupItem> activeGroups, string scriptsDirectory)
        {
            InitializeComponent();

            cmbGroups.ItemsSource = activeGroups;
            if (activeGroups.Count != 0) cmbGroups.SelectedIndex = 0;

            List<string> availableScanners = [];

            // Add the PowerShell scripts from the folder
            if (Directory.Exists(scriptsDirectory))
            {
                availableScanners.Add("SystemInfo Linux.ps1");
                availableScanners.Add("SystemInfo Non Domain.ps1");
                availableScanners.Add("SystemInfo.ps1");
            }

            cmbScripts.ItemsSource = availableScanners;
            if (availableScanners.Count != 0) cmbScripts.SelectedIndex = 0;
        }

        private void BtnRun_Click(object sender, RoutedEventArgs e)
        {
            if (cmbGroups.SelectedValue != null && cmbScripts.SelectedItem != null)
            {
                SelectedGroupId = (int)cmbGroups.SelectedValue;
                SelectedScript = cmbScripts.SelectedItem.ToString() ?? "";
                this.DialogResult = true;
                this.Close();
            }
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = false;
            this.Close();
        }

        // Prevent memory leaks
        protected override void OnClosed(EventArgs e)
        {
            cmbGroups.ItemsSource = null;
            cmbScripts.ItemsSource = null;
            base.OnClosed(e);
        }
    }
}