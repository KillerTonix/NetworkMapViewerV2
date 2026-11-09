using NetworkMapViewerV2.Data;
using NetworkMapViewerV2.Helpers;
using NetworkMapViewerV2.Models;
using NetworkMapViewerV2.Services;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace NetworkMapViewerV2.Views
{
    /// <summary>
    /// Interaction logic for IPAMWindow.xaml
    /// </summary>
    public partial class IPAMWindow : Window
    {
        public IPAMWindow()
        {
            InitializeComponent();
            Loaded += async (_, _) => await LoadAsync(isFirstLoad: true);
        }
        private const string FallbackCidr = "192.168.102.0/24";
        private const int PingConcurrency = 100;
        private const int ConfirmAbovePingCount = 1024;

        private List<UsedAddressInfo> _usedAddresses = [];
        private List<IpUsageRow> _rows = [];
        private ICollectionView? _view;
        private string _filter = "All";
        private string _currentCidr = "";
        private CancellationTokenSource? _pingCts;
        private bool _isPinging;
              
        // ─── LOADING ──────────────────────────────────────────────────────

        private async void BtnShow_Click(object sender, RoutedEventArgs e) => await LoadAsync(isFirstLoad: false);

        private async Task LoadAsync(bool isFirstLoad)
        {
            if (_isPinging) return;

            btnShow.IsEnabled = false;
            ShowMessage("");

            try
            {
                // Read the database off the UI thread so the window stays responsive.
                _usedAddresses = await Task.Run(() => new MapRepository().GetUsedAddresses());
            }
            catch (Exception ex)
            {
                ShowMessage($"Could not read devices from the database: {ex.Message}");
                if (string.IsNullOrWhiteSpace(txtCidr.Text)) txtCidr.Text = FallbackCidr;
                btnShow.IsEnabled = true;
                return;
            }

            if (isFirstLoad && string.IsNullOrWhiteSpace(txtCidr.Text))
            {
                txtCidr.Text = GuessDefaultCidr(_usedAddresses);
            }

            if (!IpRangeHelper.TryParseCidr(txtCidr.Text, out uint network, out int prefix, out string error))
            {
                ShowMessage(error);
                btnShow.IsEnabled = true;
                return;
            }

            BuildRows(network, prefix);
            btnShow.IsEnabled = true;
            txtCidr.Focus();
            txtCidr.SelectAll();
        }

        /// <summary>The /24 that already holds the most saved devices, so the window opens on the busiest subnet.</summary>
        private static string GuessDefaultCidr(List<UsedAddressInfo> used)
        {
            var busiest = used
                .Select(u => IpRangeHelper.TryParseIPv4(u.Address, out uint ip) ? (uint?)ip : null)
                .Where(ip => ip.HasValue)
                .GroupBy(ip => ip!.Value & 0xFFFFFF00u)
                .OrderByDescending(g => g.Count())
                .Select(g => (uint?)g.Key)
                .FirstOrDefault();

            return busiest.HasValue ? $"{IpRangeHelper.ToIpString(busiest.Value)}/24" : FallbackCidr;
        }

        private void BuildRows(uint network, int prefix)
        {
            // Index saved devices by numeric address. The same IP can appear more than once
            // (e.g. one device drawn on two maps), so keep every entry.
            var byAddress = new Dictionary<uint, List<UsedAddressInfo>>();
            foreach (var used in _usedAddresses)
            {
                if (!IpRangeHelper.TryParseIPv4(used.Address, out uint key)) continue;

                if (!byAddress.TryGetValue(key, out var entries))
                {
                    entries = new List<UsedAddressInfo>();
                    byAddress[key] = entries;
                }
                entries.Add(used);
            }

            var rows = new List<IpUsageRow>((int)Math.Min(IpRangeHelper.HostCount(prefix), int.MaxValue));
            foreach (uint host in IpRangeHelper.GetHostAddresses(network, prefix))
            {
                var row = new IpUsageRow { SortKey = host, Address = IpRangeHelper.ToIpString(host) };

                if (byAddress.TryGetValue(host, out var entries))
                {
                    row.State = IpState.Used;
                    row.DeviceName = string.Join(", ", entries.Select(x => x.DeviceName).Where(n => n.Length > 0).Distinct());
                    row.MapName = string.Join(", ", entries.Select(x => x.MapName).Distinct());
                    if (entries.Count > 1) row.Note = $"Listed {entries.Count} times";
                }

                rows.Add(row);
            }

            _rows = rows;
            _currentCidr = $"{IpRangeHelper.ToIpString(network)}/{prefix}";

            _view = CollectionViewSource.GetDefaultView(_rows);
            _view.Filter = FilterRow;
            dgIps.ItemsSource = _view;

            btnPing.IsEnabled = _rows.Any(r => r.State != IpState.Used);
            txtProgress.Text = "";
            UpdateSummary();
        }

        // ─── FILTER & SUMMARY ─────────────────────────────────────────────

        private bool FilterRow(object item)
        {
            if (item is not IpUsageRow row) return false;

            return _filter switch
            {
                "Used" => row.State == IpState.Used,
                "Free" => row.State is IpState.Free or IpState.FreeNoReply,
                "NoReply" => row.State == IpState.FreeNoReply,
                "Active" => row.State == IpState.ActiveNotOnMap,
                _ => true
            };
        }

        private void CmbFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Fires once while the XAML is still loading, before any rows exist.
            if (_view == null) return;

            _filter = (cmbFilter.SelectedItem as ComboBoxItem)?.Tag as string ?? "All";
            _view.Refresh();
        }

        private void UpdateSummary()
        {
            int used = _rows.Count(r => r.State == IpState.Used);
            int noReply = _rows.Count(r => r.State == IpState.FreeNoReply);
            int active = _rows.Count(r => r.State == IpState.ActiveNotOnMap);
            int free = _rows.Count - used;

            string text = $"{_currentCidr}: {_rows.Count} usable addresses · {used} used · {free} free";
            if (noReply + active > 0)
            {
                text += $" ({noReply} confirmed by no ping reply";
                if (active > 0) text += $", {active} answer ping but are not on any map";
                text += ")";
            }

            txtSummary.Text = text;
        }

        // ─── PING ─────────────────────────────────────────────────────────

        private async void BtnPing_Click(object sender, RoutedEventArgs e)
        {
            var targets = _rows.Where(r => r.State != IpState.Used).ToList();
            if (targets.Count == 0)
            {
                MessageBox.Show("Every address in this range is already used.", "Nothing to ping", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            if (targets.Count > ConfirmAbovePingCount)
            {
                var confirm = MessageBox.Show(
                    $"This will ping {targets.Count:N0} addresses and can take several minutes. You can press Stop at any time.\n\nContinue?",
                    "Ping Free IPs", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (confirm != MessageBoxResult.Yes) return;
            }

            // Start from a clean slate so old results never mix with the new run.
            foreach (var row in targets) row.State = IpState.Free;

            _pingCts = new CancellationTokenSource();
            var token = _pingCts.Token;
            SetPinging(true);
            ShowMessage("");

            pbPing.Maximum = targets.Count;
            pbPing.Value = 0;
            int done = 0;
            int replies = 0;
            bool cancelled = false;

            try
            {
                var options = new ParallelOptions { MaxDegreeOfParallelism = PingConcurrency, CancellationToken = token };

                await Parallel.ForEachAsync(targets, options, async (row, _) =>
                {
                    bool answered = await PingService.PingHostAsync(row.Address, maxAttempts: 2);

                    // Row state and progress are only touched on the UI thread.
                    await Dispatcher.InvokeAsync(() =>
                    {
                        row.State = answered ? IpState.ActiveNotOnMap : IpState.FreeNoReply;
                        if (answered) replies++;
                        done++;
                        pbPing.Value = done;
                        txtProgress.Text = $"Pinged {done:N0} / {targets.Count:N0}";
                    });
                });
            }
            catch (OperationCanceledException)
            {
                cancelled = true;
            }
            catch (Exception ex)
            {
                ShowMessage($"Ping stopped unexpectedly: {ex.Message}");
            }
            finally
            {
                _pingCts?.Dispose();
                _pingCts = null;
                SetPinging(false);
            }

            _view?.Refresh();
            UpdateSummary();
            txtProgress.Text = cancelled
                ? $"Stopped after {done:N0} of {targets.Count:N0} addresses."
                : $"Done: pinged {done:N0} addresses, {replies:N0} answered.";

            // Every ping failing usually means this PC cannot reach the subnet, not that the whole range is empty.
            if (!cancelled && replies == 0 && done >= 10)
            {
                ShowMessage("Nothing answered. If you expect devices in this range, check that this PC can reach that subnet before trusting the \"free\" results.");
            }
        }

        private void BtnStop_Click(object sender, RoutedEventArgs e)
        {
            _pingCts?.Cancel();
            btnStop.IsEnabled = false;
            txtProgress.Text = "Stopping…";
        }

        private void SetPinging(bool pinging)
        {
            _isPinging = pinging;
            btnPing.IsEnabled = !pinging && _rows.Any(r => r.State != IpState.Used);
            btnStop.IsEnabled = pinging;
            btnShow.IsEnabled = !pinging;
            txtCidr.IsEnabled = !pinging;
            pbPing.Visibility = pinging ? Visibility.Visible : Visibility.Collapsed;
        }

        // ─── MISC ─────────────────────────────────────────────────────────

        private void ShowMessage(string message)
        {
            txtMessage.Text = message;
            txtMessage.Visibility = string.IsNullOrEmpty(message) ? Visibility.Collapsed : Visibility.Visible;
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();

        protected override void OnClosing(CancelEventArgs e)
        {
            _pingCts?.Cancel();
            base.OnClosing(e);
        }

        private void SubnetEditor_PreviewKeyDown(object sender, KeyEventArgs e)
        {

        }

        private void LstSubnets_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {

        }

        private void DgIps_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
        {

        }

        private void BtnSubnetSave_Click(object sender, RoutedEventArgs e)
        {

        }

        private void BtnSubnetEdit_Click(object sender, RoutedEventArgs e)
        {

        }

        private void BtnSubnetDelete_Click(object sender, RoutedEventArgs e)
        {

        }

        private void BtnSubnetCancel_Click(object sender, RoutedEventArgs e)
        {

        }

        private void BtnSubnetAdd_Click(object sender, RoutedEventArgs e)
        {

        }
    }
}
