using NetworkMapViewerV2.Data;
using NetworkMapViewerV2.Helpers.LocalFetcher;
using NetworkMapViewerV2.Models;
using System.Collections.ObjectModel;
using System.Windows;

namespace NetworkMapViewerV2.Views
{
    public partial class ImportADComputersWindow : Window
    {
        public ObservableCollection<SelectableADComputer> Computers { get; set; } = [];
        public List<ADComputerInfo> SelectedComputers { get; private set; } = [];

        public ImportADComputersWindow(List<ADComputerInfo> adComputers)
        {
            InitializeComponent();

            var allDevices = MapRepository.GetAllDevices();

            // 1. Create a HashSet for O(1) fast, case-insensitive DB address lookups
            var existingAddresses = allDevices
                .Where(d => !string.IsNullOrWhiteSpace(d.Address))
                .Select(d => d.Address)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            // 2. Filter out DB matches and deduplicate the incoming adComputers list
            var missingComputers = adComputers
                .Where(c => !string.IsNullOrWhiteSpace(c.IPAddress) && !existingAddresses.Contains(c.IPAddress))
                .DistinctBy(c => c.IPAddress) // Ensures uniqueness within adComputers
                .Select(c => new SelectableADComputer
                {
                    Name = c.Name,
                    IPAddress = c.IPAddress,
                    WhenCreated = c.WhenCreated ?? DateTime.MinValue,
                    IsSelected = false
                });

            foreach (var comp in missingComputers)
            {
                Computers.Add(comp);
            }

            dgComputers.ItemsSource = Computers;
            txtSummary.Text = $"Found {Computers.Count} computers in AD not currently on this map.";
        }

        private void BtnSelectAll_Click(object sender, RoutedEventArgs e)
        {
            foreach (var c in Computers) c.IsSelected = true;
        }

        private void BtnUnselectAll_Click(object sender, RoutedEventArgs e)
        {
            foreach (var c in Computers) c.IsSelected = false;
        }

        private void BtnAdd_Click(object sender, RoutedEventArgs e)
        {
            SelectedComputers = [.. Computers
                .Where(c => c.IsSelected)
                .Select(c => new ADComputerInfo
                {
                    Name = c.Name,
                    IPAddress = c.IPAddress,
                    WhenCreated = c.WhenCreated
                })];

            if (SelectedComputers.Count == 0)
            {
                MessageBox.Show("Please select at least one computer to add.", "Selection Required", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            DialogResult = true;
            Close();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}