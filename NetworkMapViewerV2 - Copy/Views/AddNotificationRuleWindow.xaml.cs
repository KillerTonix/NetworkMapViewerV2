using System.Collections.Generic;
using System.Windows;
using NetworkMapViewerV2.Models;

namespace NetworkMapViewerV2.Views
{
    public class TargetGroupItem
    {
        public int? GroupId { get; set; }
        public string GroupName { get; set; } = string.Empty;
    }

    public partial class AddNotificationRuleWindow : Window
    {
        public NotificationRule? CreatedRule { get; private set; }

        public AddNotificationRuleWindow(List<TargetGroupItem> availableGroups)
        {
            InitializeComponent();

            // Insert "All Devices" at the very top of the list
            availableGroups.Insert(0, new TargetGroupItem { GroupId = null, GroupName = "All Devices" });

            cmbTargets.ItemsSource = availableGroups;
            cmbTargets.SelectedIndex = 0;
        }

        private void BtnOK_Click(object sender, RoutedEventArgs e)
        {
            if (cmbTargets.SelectedItem is not TargetGroupItem selectedTarget) return;
            string wakeup = chkWakesUp.IsChecked == true ? "Wakes Up" : "";
            string down = chkGoesDown.IsChecked == true ? "Goes Down" : "";
            // Build the rule based on the UI selections
            CreatedRule = new NotificationRule
            {
                TargetGroupId = selectedTarget.GroupId,
                TargetName = selectedTarget.GroupId == null ? "Any Device" : $"Any '{selectedTarget.GroupName}' {wakeup} {down}",
                TriggerOnDown = chkGoesDown.IsChecked == true,
                TriggerOnUp = chkWakesUp.IsChecked == true
            };

            this.DialogResult = true;
            this.Close();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = false;
            this.Close();
        }
    }
}