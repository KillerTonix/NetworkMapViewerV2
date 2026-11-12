using NetworkMapViewerV2.Data;
using NetworkMapViewerV2.Helpers;
using NetworkMapViewerV2.Models;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;

namespace NetworkMapViewerV2.Views
{
    /// <summary>
    /// Edits the planned address blocks used by the IPAM "Cut range" option (Printers .10-.30 and so on).
    /// Returns true from ShowDialog when the list was saved.
    /// </summary>
    public partial class RangeCutWindow : Window
    {
        private readonly ObservableCollection<RangeCut> _items = [];

        public RangeCutWindow()
        {
            InitializeComponent();
            dgRanges.ItemsSource = _items;
            Loaded += async (_, _) => await LoadAsync();
        }

        private async Task LoadAsync()
        {
            try
            {
                var ranges = await Task.Run(() => new MapRepository().GetRangeCuts());
                foreach (var r in ranges) _items.Add(r);
                btnSave.IsEnabled = true;
            }
            catch (Exception ex)
            {
                // Without the stored list, saving would overwrite it blindly, so editing stays off.
                ShowError($"Could not load the ranges: {ex.Message}");
                btnAdd.IsEnabled = btnRemove.IsEnabled = btnDefaults.IsEnabled = false;
                dgRanges.IsReadOnly = true;
            }
        }

        private void ShowError(string message) => txtError.Text = message;

        /// <summary>Finishes any cell being edited. False when the cell holds something that is not a number.</summary>
        private bool CommitGridEdits()
        {
            bool cell = dgRanges.CommitEdit(DataGridEditingUnit.Cell, true);
            bool row = dgRanges.CommitEdit(DataGridEditingUnit.Row, true);
            return cell && row;
        }

        private void BtnAdd_Click(object sender, RoutedEventArgs e)
        {
            if (!CommitGridEdits()) { ShowError("Fix the highlighted cell first (numbers only)."); return; }
            ShowError("");

            // Start the new block just after the last one, so it does not overlap by accident.
            int next = _items.Count == 0 ? 1 : Math.Min(254, _items.Max(r => r.To) + 1);
            var item = new RangeCut { Label = "", From = next, To = next };
            _items.Add(item);

            dgRanges.SelectedItem = item;
            dgRanges.ScrollIntoView(item);
            dgRanges.CurrentCell = new DataGridCellInfo(item, dgRanges.Columns[0]);
            dgRanges.BeginEdit();
        }

        private void BtnRemove_Click(object sender, RoutedEventArgs e)
        {
            if (dgRanges.SelectedItem is not RangeCut selected) { ShowError("Select the block to remove."); return; }

            dgRanges.CancelEdit();
            _items.Remove(selected);
            ShowError("");
        }

        private void BtnDefaults_Click(object sender, RoutedEventArgs e)
        {
            dgRanges.CancelEdit();
            _items.Clear();
            foreach (var r in IpamHelper.DefaultCutRanges()) _items.Add(r);
            ShowError("");
        }

        private async void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            if (!CommitGridEdits()) { ShowError("Fix the highlighted cell first (numbers only)."); return; }

            var ranges = _items.Select(r => new RangeCut { RangeCutId = r.RangeCutId, Label = (r.Label ?? "").Trim(), From = r.From, To = r.To }).ToList();

            string? problem = IpamHelper.ValidateCutRanges(ranges);
            if (problem != null) { ShowError(problem); return; }

            btnSave.IsEnabled = false;
            ShowError("");

            try
            {
                await Task.Run(() => new MapRepository().SaveRangeCuts(ranges));
                DialogResult = true;
            }
            catch (Exception ex)
            {
                ShowError($"Could not save: {ex.Message}");
                btnSave.IsEnabled = true;
            }
        }
    }
}
