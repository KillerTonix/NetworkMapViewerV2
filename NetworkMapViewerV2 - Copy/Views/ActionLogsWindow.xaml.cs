using System.Windows;

namespace NetworkMapViewerV2.Views
{
    public partial class ActionLogsWindow : Window
    {
        public ActionLogsWindow()
        {
            InitializeComponent();
            LoadLogs();
        }

        private void LoadLogs()
        {
            var repo = new Data.MapRepository();
            var logs = repo.GetAuditLogs();
            dgLogs.ItemsSource = logs;
        }
    }
}
