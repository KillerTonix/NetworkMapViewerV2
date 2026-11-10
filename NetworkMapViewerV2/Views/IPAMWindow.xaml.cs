using NetworkMapViewerV2.Data;
using NetworkMapViewerV2.Helpers;
using NetworkMapViewerV2.Models;
using NetworkMapViewerV2.Services;
using NetworkMapViewerV2.ViewModels;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
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
            DataContext = this; // the Device Type / Map Name drop-downs bind to the lists below
            Loaded += async (_, _) => await LoadAsync(isFirstLoad: true);
        }

        /// <summary>Device types (map-link groups excluded) offered by the Device Type drop-down.</summary>
        public List<string> AvailableDeviceTypes { get; private set; } = [];

        /// <summary>Map names offered by the Map Name drop-down.</summary>
        public List<string> AvailableMaps { get; private set; } = [];
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

        private List<SavedSubnet> _subnets = [];
        private SavedSubnet? _editingSubnet;      // null = the editor is adding a new range
        private bool _suppressSubnetSelection;    // true while the code (not the user) changes the list selection

        private readonly Dictionary<string, int> _typeIds = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, int> _mapIds = new(StringComparer.OrdinalIgnoreCase);
        private bool _lookupsLoaded;
        private bool _isSaving;

        private bool _cutActive;      // the grid currently shows only the planned blocks
        private int _hiddenUsed;      // used addresses of the range that fall outside those blocks

        // ─── LOADING ──────────────────────────────────────────────────────

        private async void BtnShow_Click(object sender, RoutedEventArgs e) => await LoadAsync(isFirstLoad: false);

        private async Task LoadAsync(bool isFirstLoad)
        {
            if (_isPinging) return;

            btnShow.IsEnabled = false;
            ShowMessage("");

            // The sidebar and drop-down lists load independently, so a problem there never blocks the address grid.
            if (isFirstLoad) await RefreshSubnetsAsync();
            if (!_lookupsLoaded) await LoadLookupsAsync();

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
                // Prefer the first saved range; otherwise open on the busiest /24.
                txtCidr.Text = _subnets.FirstOrDefault()?.Cidr ?? GuessDefaultCidr(_usedAddresses);
            }

            if (!IpRangeHelper.TryParseCidr(txtCidr.Text, out uint network, out int prefix, out string error))
            {
                ShowMessage(error);
                btnShow.IsEnabled = true;
                return;
            }

            BuildRows(network, prefix);
            HighlightSavedSubnet($"{IpRangeHelper.ToIpString(network)}/{prefix}");
            btnShow.IsEnabled = true;
            txtCidr.Focus();
            txtCidr.SelectAll();
        }

        // ─── SIDEBAR: SAVED CIDR RANGES ───────────────────────────────────

        private async Task RefreshSubnetsAsync(int? selectId = null)
        {
            try
            {
                _subnets = await Task.Run(() => new MapRepository().GetSubnets());
                ShowSubnetError("");
            }
            catch (Exception ex)
            {
                _subnets = [];
                ShowSubnetError($"Could not load saved ranges: {ex.Message}");
            }

            _suppressSubnetSelection = true;
            try
            {
                lstSubnets.ItemsSource = null;
                lstSubnets.ItemsSource = _subnets;
                lstSubnets.SelectedItem = selectId.HasValue ? _subnets.FirstOrDefault(s => s.SubnetId == selectId.Value) : null;
            }
            finally
            {
                _suppressSubnetSelection = false;
            }

            txtSubnetEmpty.Visibility = _subnets.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            UpdateSubnetButtons();
        }

        /// <summary>Marks the saved range that matches the CIDR now on screen (or clears the mark) without reloading anything.</summary>
        private void HighlightSavedSubnet(string cidr)
        {
            _suppressSubnetSelection = true;
            try
            {
                lstSubnets.SelectedItem = _subnets.FirstOrDefault(s => s.Cidr == cidr);
            }
            finally
            {
                _suppressSubnetSelection = false;
            }
            UpdateSubnetButtons();
        }

        private void UpdateSubnetButtons()
        {
            bool hasSelection = lstSubnets.SelectedItem is SavedSubnet;
            btnSubnetEdit.IsEnabled = hasSelection;
            btnSubnetDelete.IsEnabled = hasSelection;
        }

        private async void LstSubnets_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateSubnetButtons();
            if (_suppressSubnetSelection || lstSubnets.SelectedItem is not SavedSubnet subnet) return;

            // Clicking a saved range shows its addresses right away.
            txtCidr.Text = subnet.Cidr;
            await LoadAsync(isFirstLoad: false);
        }

        private void BtnSubnetAdd_Click(object sender, RoutedEventArgs e)
        {
            // Start from the range currently on screen, so "+ Add" names what you are looking at.
            string current = IpRangeHelper.TryParseCidr(txtCidr.Text, out uint network, out int prefix, out _)
                ? $"{IpRangeHelper.ToIpString(network)}/{prefix}"
                : "";

            OpenSubnetEditor(null, "", current, SubnetKind.Branch);
        }

        private void BtnSubnetEdit_Click(object sender, RoutedEventArgs e)
        {
            if (lstSubnets.SelectedItem is SavedSubnet subnet) OpenSubnetEditor(subnet, subnet.Name, subnet.Cidr, subnet.Kind);
        }

        private void OpenSubnetEditor(SavedSubnet? subnet, string name, string cidr, SubnetKind kind)
        {
            _editingSubnet = subnet;
            txtSubnetEditorTitle.Text = subnet == null ? "New range" : "Edit range";
            txtSubnetName.Text = name;
            txtSubnetCidr.Text = cidr;
            cmbSubnetKind.SelectedIndex = kind == SubnetKind.HeadOffice ? 1 : 0;
            ShowSubnetEditorError("");
            pnlSubnetEditor.Visibility = Visibility.Visible;
            txtSubnetName.Focus();
            txtSubnetName.SelectAll();
        }

        private void CloseSubnetEditor()
        {
            _editingSubnet = null;
            pnlSubnetEditor.Visibility = Visibility.Collapsed;
        }

        private void BtnSubnetCancel_Click(object sender, RoutedEventArgs e) => CloseSubnetEditor();

        // Enter saves and Esc cancels while typing in the editor (otherwise Enter would hit the default "Show IPs" button).
        private void SubnetEditor_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                BtnSubnetSave_Click(sender, e);
            }
            else if (e.Key == Key.Escape)
            {
                e.Handled = true;
                CloseSubnetEditor();
            }
        }

        private async void BtnSubnetSave_Click(object sender, RoutedEventArgs e)
        {
            string name = txtSubnetName.Text.Trim();
            if (name.Length == 0)
            {
                ShowSubnetEditorError("Enter a name, e.g. Montreal Branch.");
                return;
            }

            if (!IpRangeHelper.TryParseCidr(txtSubnetCidr.Text, out uint network, out int prefix, out string error))
            {
                ShowSubnetEditorError(error);
                return;
            }

            // Store the normalised form, so 192.168.28.5/22 is saved as 192.168.28.0/22.
            string cidr = $"{IpRangeHelper.ToIpString(network)}/{prefix}";
            int editingId = _editingSubnet?.SubnetId ?? 0;

            var duplicate = _subnets.FirstOrDefault(s => s.Cidr == cidr && s.SubnetId != editingId);
            if (duplicate != null)
            {
                ShowSubnetEditorError($"{cidr} is already saved as \"{duplicate.Name}\".");
                return;
            }

            btnSubnetSave.IsEnabled = false;
            try
            {
                var kind = cmbSubnetKind.SelectedIndex == 1 ? SubnetKind.HeadOffice : SubnetKind.Branch;
                var toSave = new SavedSubnet { SubnetId = editingId, Name = name, Cidr = cidr, Kind = kind };
                var saved = await Task.Run(() => new MapRepository().SaveSubnet(toSave));

                CloseSubnetEditor();
                await RefreshSubnetsAsync(saved.SubnetId);

                // Show the saved range (covers an edit that changed the CIDR).
                txtCidr.Text = saved.Cidr;
                await LoadAsync(isFirstLoad: false);
            }
            catch (Exception ex)
            {
                ShowSubnetEditorError(ex.Message);
            }
            finally
            {
                btnSubnetSave.IsEnabled = true;
            }
        }

        private async void BtnSubnetDelete_Click(object sender, RoutedEventArgs e)
        {
            if (lstSubnets.SelectedItem is not SavedSubnet subnet) return;

            var confirm = MessageBox.Show(
                $"Remove the saved range \"{subnet.Name}\" ({subnet.Cidr})?\n\nOnly the saved name is removed. Devices on your maps are not affected.",
                "Delete saved range", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (confirm != MessageBoxResult.Yes) return;

            try
            {
                await Task.Run(() => new MapRepository().DeleteSubnet(subnet.SubnetId));
                if (_editingSubnet?.SubnetId == subnet.SubnetId) CloseSubnetEditor();
                await RefreshSubnetsAsync();
            }
            catch (Exception ex)
            {
                ShowSubnetError($"Could not delete the range: {ex.Message}");
            }
        }

        private void ShowSubnetError(string message)
        {
            txtSubnetError.Text = message;
            txtSubnetError.Visibility = string.IsNullOrEmpty(message) ? Visibility.Collapsed : Visibility.Visible;
        }

        private void ShowSubnetEditorError(string message)
        {
            txtSubnetEditorError.Text = message;
            txtSubnetEditorError.Visibility = string.IsNullOrEmpty(message) ? Visibility.Collapsed : Visibility.Visible;
        }


        /// <summary>Loads the device types and maps the grid drop-downs offer, and the name -> id lookups used when saving.</summary>
        private async Task LoadLookupsAsync()
        {
            try
            {
                var (groups, maps) = await Task.Run(() =>
                {
                    var repo = new MapRepository();
                    return (repo.GetAllDeviceGroups(), repo.GetAvailableMaps());
                });

                _typeIds.Clear();
                foreach (var g in groups.Where(g => !g.IsMapLink && !string.IsNullOrWhiteSpace(g.GroupName)))
                    _typeIds[g.GroupName.Trim()] = g.GroupId;

                _mapIds.Clear();
                foreach (var m in maps.Where(m => !string.IsNullOrWhiteSpace(m.MapName)))
                    _mapIds[m.MapName.Trim()] = m.MapId;

                AvailableDeviceTypes = _typeIds.Keys.OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase).ToList();
                AvailableMaps = _mapIds.Keys.OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase).ToList();
                _lookupsLoaded = true;
            }
            catch (Exception ex)
            {
                ShowMessage($"Could not load device types and maps, so editing is disabled: {ex.Message}");
            }
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

            string cidr = $"{IpRangeHelper.ToIpString(network)}/{prefix}";
            bool cut = IsCutActive(cidr);
            uint mask = IpRangeHelper.MaskFor(prefix);

            // Used addresses that sit outside the planned blocks are not listed in cut mode; count them so the summary can say so.
            _hiddenUsed = cut
                ? byAddress.Keys.Count(k => (k & mask) == network && IpamHelper.GetPool(k) == IpPool.None)
                : 0;

            var rows = new List<IpUsageRow>((int)Math.Min(IpRangeHelper.HostCount(prefix), int.MaxValue));
            foreach (uint host in IpRangeHelper.GetHostAddresses(network, prefix))
            {
                var pool = IpamHelper.GetPool(host);
                if (cut && pool == IpPool.None) continue;

                var row = new IpUsageRow
                {
                    SortKey = host,
                    Address = IpRangeHelper.ToIpString(host),
                    Pool = cut ? IpamHelper.PoolLabel(pool) : ""
                };

                if (byAddress.TryGetValue(host, out var entries))
                {
                    row.State = IpState.Used;
                    row.EntryCount = entries.Count;

                    if (entries.Count == 1)
                    {
                        var only = entries[0];
                        row.DeviceId = only.DeviceId;
                        row.MapId = only.MapId;
                        row.GroupId = only.GroupId;
                        row.DeviceName = only.DeviceName;
                        row.DeviceType = only.DeviceType;
                        row.MapName = only.MapName;
                        row.Note = only.Note;
                    }
                    else
                    {
                        // Same IP on several devices: show them all, but do not allow editing a guess.
                        row.Warning = $"listed {entries.Count} times";
                        row.DeviceName = string.Join(", ", entries.Select(x => x.DeviceName).Where(n => n.Length > 0).Distinct());
                        row.DeviceType = string.Join(", ", entries.Select(x => x.DeviceType).Where(n => n.Length > 0).Distinct());
                        row.MapName = string.Join(", ", entries.Select(x => x.MapName).Distinct());
                        row.Note = string.Join(" | ", entries.Select(x => x.Note).Where(n => n.Length > 0).Distinct());
                    }
                }

                row.AcceptChanges();

                rows.Add(row);
            }

            _rows = rows;
            _currentCidr = cidr;
            _cutActive = cut;
            colPool.Visibility = cut ? Visibility.Visible : Visibility.Collapsed;
            UpdateCutOption();

            _view = CollectionViewSource.GetDefaultView(_rows);
            _view.Filter = FilterRow;
            dgIps.ItemsSource = _view;

            btnPing.IsEnabled = _rows.Any(r => r.State != IpState.Used);
            btnExport.IsEnabled = _rows.Count > 0;
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

            string text = _cutActive
                ? $"{_currentCidr} (cut range): {_rows.Count} addresses in the planned blocks · {used} used · {free} free"
                : $"{_currentCidr}: {_rows.Count} usable addresses · {used} used · {free} free";
            if (noReply + active > 0)
            {
                text += $" ({noReply} confirmed by no ping reply";
                if (active > 0) text += $", {active} answer ping but are not on any map";
                text += ")";
            }

            if (_cutActive)
            {
                // Free addresses left in each block, e.g. "Printers .10-.30: 14 free of 21".
                var parts = IpamHelper.CutRanges.Select(r =>
                {
                    var inBlock = _rows.Where(x => x.Pool == r.Label).ToList();
                    return $"{r.Label} .{r.From}-.{r.To}: {inBlock.Count(x => x.State != IpState.Used)} free of {inBlock.Count}";
                });
                text += "\n" + string.Join("  ·  ", parts);

                if (_hiddenUsed > 0) text += $"\n{_hiddenUsed} used address(es) outside these blocks are hidden. Untick \"Cut range\" to see them.";
            }

            txtSummary.Text = text;
        }

        // ─── CUT RANGE ────────────────────────────────────────────────────

        private SavedSubnet? FindSavedSubnet(string cidr) => _subnets.FirstOrDefault(s => s.Cidr == cidr);

        /// <summary>Cut range applies when the box is ticked and the range is not a saved Head Office range.</summary>
        private bool IsCutActive(string cidr) =>
            chkCutRange.IsChecked == true && FindSavedSubnet(cidr)?.Kind != SubnetKind.HeadOffice;

        /// <summary>Head Office ranges always show everything, so the box is disabled for them.</summary>
        private void UpdateCutOption()
        {
            bool headOffice = FindSavedSubnet(_currentCidr)?.Kind == SubnetKind.HeadOffice;
            chkCutRange.IsEnabled = !_isPinging && !headOffice;
            chkCutRange.ToolTip = headOffice
                ? "Head Office ranges always show the full range."
                : "Show only the planned blocks: Printers .10-.30, Phones .50-.70, PCs / NUCs .101-.140";
        }

        private void ChkCutRange_Changed(object sender, RoutedEventArgs e)
        {
            // Also fires while the XAML loads, before any range is shown.
            if (_view == null || _isPinging || _isSaving) return;

            if (IpRangeHelper.TryParseCidr(_currentCidr, out uint network, out int prefix, out _))
            {
                BuildRows(network, prefix); // from the already loaded devices, no database read needed
            }
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
            pnlSidebar.IsEnabled = !pinging; // switching ranges mid-ping would mix results
            UpdateCutOption();               // so would switching between cut and full range
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

        // ─── EDITING THE GRID  (creates / updates real devices) ───────────

        private void DgIps_BeginningEdit(object sender, DataGridBeginningEditEventArgs e)
        {
            if (e.Row.Item is not IpUsageRow row) return;

            if (_isPinging || _isSaving || row.IsSaving || !_lookupsLoaded)
            {
                e.Cancel = true;
                return;
            }

            if (!row.IsEditable)
            {
                e.Cancel = true;
                ShowMessage($"{row.Address} is listed on {row.EntryCount} devices. Remove the duplicates on the map first, then press Show.");
            }
        }

        private void DgIps_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
        {
            if (e.EditAction != DataGridEditAction.Commit || e.Row.Item is not IpUsageRow row) return;

            // The new value reaches the row only after this event, so save once the commit has finished.
            Dispatcher.BeginInvoke(new Action(async () => await SaveRowAsync(row)), System.Windows.Threading.DispatcherPriority.Background);
        }

        private async Task SaveRowAsync(IpUsageRow row)
        {
            if (!row.IsDirty || row.IsSaving || _isSaving) return;

            // An address that is not on a map yet only keeps what was typed; the "Add to map" button puts it on the map.
            if (row.DeviceId == 0) return;

            string name = row.DeviceName.Trim();
            string type = row.DeviceType.Trim();
            string map = row.MapName.Trim();

            if (type.Length == 0 || map.Length == 0)
            {
                RejectWithMessage(row, "Device Type and Map Name cannot be empty for a device that is on a map.");
                return;
            }

            // Only look names up for fields the user changed, so a device with an unusual type/map is not forced to change.
            int groupId = row.GroupId;
            if (row.TypeChanged && !_typeIds.TryGetValue(type, out groupId))
            {
                RejectWithMessage(row, $"\"{type}\" is not a known device type. Pick one from the list.");
                return;
            }

            int mapId = row.MapId;
            if (row.MapChanged && !_mapIds.TryGetValue(map, out mapId))
            {
                RejectWithMessage(row, $"\"{map}\" is not a known map. Pick one from the list.");
                return;
            }

            // A map tab with unsaved or in-progress edits would overwrite this change the next time it is saved.
            var blocked = FindBusyTab(new[] { mapId, row.MapId }.Where(id => id != 0));
            if (blocked != null)
            {
                RejectWithMessage(row, $"Map \"{blocked}\" has unsaved changes or is in edit mode. Save or close it first, then try again.");
                return;
            }

            var edit = new IpamDeviceEdit(row.DeviceId, row.Address, name, groupId, mapId, row.Note.Trim());

            row.IsSaving = true;
            _isSaving = true;
            txtProgress.Text = $"Saving {row.Address}…";
            ShowMessage("");

            try
            {
                int deviceId = await Task.Run(() => new MapRepository().SaveIpamDevice(edit));

                row.DeviceId = deviceId;
                row.GroupId = groupId;
                row.MapId = mapId;
                row.EntryCount = 1;
                row.State = IpState.Used;
                row.AcceptChanges();

                // Keep the cached list in step so Show/filters stay right without another database read.
                _usedAddresses.RemoveAll(u => u.DeviceId == deviceId);
                _usedAddresses.Add(new UsedAddressInfo(row.Address, row.DeviceName, row.MapName, deviceId, mapId, groupId, row.DeviceType, row.Note));

                txtProgress.Text = $"{row.Address} saved.";
                btnPing.IsEnabled = _rows.Any(r => r.State != IpState.Used);
                UpdateSummary();
                RefreshViewSafe();
            }
            catch (Exception ex)
            {
                RejectWithMessage(row, $"Could not save {row.Address}: {ex.Message}");
            }
            finally
            {
                row.IsSaving = false;
                _isSaving = false;
            }
        }

        /// <summary>
        /// Re-applies the filter after a save (a new device turns a Free row into Used). The grid refuses a refresh
        /// while a row is still being edited, so finish that edit first and retry later if it still objects.
        /// </summary>
        private void RefreshViewSafe()
        {
            try { dgIps.CommitEdit(DataGridEditingUnit.Row, true); } catch (InvalidOperationException) { }

            try
            {
                _view?.Refresh();
            }
            catch (InvalidOperationException)
            {
                Dispatcher.BeginInvoke(new Action(() => _view?.Refresh()), System.Windows.Threading.DispatcherPriority.ContextIdle);
            }
        }

        private void RejectWithMessage(IpUsageRow row, string message)
        {
            row.RejectChanges();
            txtProgress.Text = "";
            ShowMessage(message);
        }

        /// <summary>Name of the first open map tab (among the given maps) that has unsaved changes or is in edit mode, or null.</summary>
        private static string? FindBusyTab(IEnumerable<int> mapIds)
        {
            var ids = mapIds.ToHashSet();
            if (Application.Current?.MainWindow?.DataContext is not MainViewModel vm || vm.OpenTabs == null) return null;

            return vm.OpenTabs
                .FirstOrDefault(t => ids.Contains(t.MapId) && (t.HasUnsavedChanges || t.IsEditingEnabled))
                ?.MapName;
        }

        // ─── ADD TO MAP ───────────────────────────────────────────────────

        /// <summary>
        /// Puts this address on its map as an unsaved device and switches the main window to that map in edit mode.
        /// This window is modal, so it closes; saving is then the usual Save (Ctrl+S) or the question when leaving edit mode.
        /// </summary>
        private void BtnAddToMap_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is not IpUsageRow row || _isPinging || _isSaving) return;

            try { dgIps.CommitEdit(DataGridEditingUnit.Row, true); } catch (InvalidOperationException) { }
            ShowMessage("");

            string name = row.DeviceName.Trim();
            string type = row.DeviceType.Trim();
            string map = row.MapName.Trim();

            if (name.Length == 0 || type.Length == 0 || map.Length == 0)
            {
                ShowMessage($"Fill in Device Name, Device Type and Map Name for {row.Address}, then press Add to map.");
                return;
            }
            if (!_typeIds.TryGetValue(type, out int groupId))
            {
                ShowMessage($"\"{type}\" is not a known device type. Pick one from the list.");
                return;
            }
            if (!_mapIds.TryGetValue(map, out int mapId))
            {
                ShowMessage($"\"{map}\" is not a known map. Pick one from the list.");
                return;
            }
            if (Application.Current?.MainWindow?.DataContext is not MainViewModel vm)
            {
                ShowMessage("The main window is not available.");
                return;
            }

            if (!vm.TryAddDeviceToMapInEditMode(mapId, row.Address, name, groupId, out string error))
            {
                ShowMessage(error);
                return;
            }

            Close();
        }

        // ─── EXPORT ───────────────────────────────────────────────────────

        private void BtnExport_Click(object sender, RoutedEventArgs e)
        {
            if (_view == null) return;

            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Title = "Export IP list",
                Filter = "CSV file (*.csv)|*.csv",
                DefaultExt = ".csv",
                FileName = $"IPAM_{_currentCidr.Replace('/', '_')}{(_cutActive ? "_cut" : "")}_{DateTime.Now:yyyyMMdd}.csv"
            };
            if (dialog.ShowDialog(this) != true) return;

            try
            {
                // Exports what the grid shows right now: the current filter and sort order.
                var rows = _view.Cast<IpUsageRow>().ToList();

                var sb = new StringBuilder();
                sb.AppendLine(_cutActive
                    ? "IP Address,Pool,Status,Device Name,Device Type,Map Name"
                    : "IP Address,Status,Device Name,Device Type,Map Name");
                foreach (var r in rows)
                {
                    sb.AppendLine(string.Join(",",
                        IpamHelper.CsvEscape(r.Address),
                        _cutActive ? IpamHelper.CsvEscape(r.Pool) + "," + IpamHelper.CsvEscape(r.StateText) : IpamHelper.CsvEscape(r.StateText),
                        IpamHelper.CsvEscape(r.DeviceName),
                        IpamHelper.CsvEscape(r.DeviceType),
                        IpamHelper.CsvEscape(r.MapName)));
                }

                // UTF-8 with BOM so Excel reads non-English device names correctly.
                System.IO.File.WriteAllText(dialog.FileName, sb.ToString(), new UTF8Encoding(true));
                txtProgress.Text = $"Exported {rows.Count:N0} rows to {System.IO.Path.GetFileName(dialog.FileName)}.";
            }
            catch (Exception ex)
            {
                ShowMessage($"Could not export: {ex.Message}");
            }
        }
    }
}
