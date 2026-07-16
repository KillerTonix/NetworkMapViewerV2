using System.Windows;
using System.Windows.Input;

namespace NetworkMapViewerV2.Views
{
    // Custom Enum to tell the ViewModel exactly what button the user clicked
    public enum MapDialogAction
    {
        None,
        Open,
        Delete
    }

    public partial class OpenDeleteMapsWindow : Window
    {
        public int SelectedMapId { get; private set; }
        public MapDialogAction ActionTaken { get; private set; } = MapDialogAction.None;

        public OpenDeleteMapsWindow()
        {
            InitializeComponent();
            LoadMaps();
        }

        private void LoadMaps()
        {
            var repo = new Data.MapRepository();
            var allMaps = repo.GetAvailableMaps();

            Dictionary<int, string> availableMaps = allMaps.ToDictionary(m => m.MapId, m => $"{m.MapName} ({m.MapType})");

            lstMaps.ItemsSource = availableMaps;

            if (availableMaps.Count == 0)
            {
                MessageBox.Show("There are no maps in the database.", "No Maps Found", MessageBoxButton.OK, MessageBoxImage.Information);
                this.Close(); // Close immediately if empty
            }
        }

        private void BtnOpen_Click(object sender, RoutedEventArgs e)
        {
            ConfirmOpen();
        }

        private void LstMaps_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            ConfirmOpen(); // Double-clicking a map instantly opens it!
        }

        private void ConfirmOpen()
        {
            if (lstMaps.SelectedValue == null)
            {
                MessageBox.Show("Please select a map to open.", "No Selection", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            SelectedMapId = (int)lstMaps.SelectedValue;
            ActionTaken = MapDialogAction.Open;
            this.DialogResult = true; // Closes the window
        }

        private void BtnDelete_Click(object sender, RoutedEventArgs e)
        {
            if (lstMaps.SelectedValue == null)
            {
                MessageBox.Show("Please select a map to delete.", "No Selection", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var confirm = MessageBox.Show("Are you sure you want to permanently delete this map?\nAll devices and labels on this map will also be destroyed.",
                                          "Confirm Deletion", MessageBoxButton.YesNo, MessageBoxImage.Error);

            if (confirm == MessageBoxResult.Yes)
            {
                SelectedMapId = (int)lstMaps.SelectedValue;
                ActionTaken = MapDialogAction.Delete;
                this.DialogResult = true; // Closes the window
            }
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            ActionTaken = MapDialogAction.None;
            this.DialogResult = false;
        }
    }
}