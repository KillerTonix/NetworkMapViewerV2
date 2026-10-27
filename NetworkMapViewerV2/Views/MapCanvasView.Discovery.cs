using NetworkMapViewerV2.Helpers.LocalFetcher;
using NetworkMapViewerV2.Models;
using System.Collections.Concurrent;
using System.Data;
using System.Windows;
using System.Windows.Controls;

namespace NetworkMapViewerV2.Views
{
    public partial class MapCanvasView : UserControl
    {
        public async Task GetNewlyAddedComputers()
        {
            if (_currentState == null || !_currentState.IsEditingEnabled) return;

            // 1. Fetch recent AD computers via PowerShell
            var fetcher = new ADComputer();
            List<ADComputerInfo> adComputers = await fetcher.GetRecentADComputersAsync();

            if (adComputers == null || adComputers.Count == 0)
            {
                MessageBox.Show("No new AD computers found in the last 15 days.", "Active Directory Sync", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // 3. Show dialog
            var importWin = new ImportADComputersWindow(adComputers)
            {
                Owner = Application.Current.MainWindow
            };

            if (importWin.ShowDialog() == true && importWin.SelectedComputers.Count != 0)
            {
                // 4. Position and spawn selected devices onto map canvas
            
                double startX = _lastRightClickPosition.X;
                double startY = _lastRightClickPosition.Y;
                double spacingX = 120;
                int index = 0;

                foreach (var computer in importWin.SelectedComputers)
                {
                    var newDevice = new NetworkDevice
                    {
                        MapId = _currentState.MapId,
                        Address = computer.IPAddress,
                        GroupId = 1, // Set to your default GroupId (e.g., Workstation ID)
                        Left = startX + (index * spacingX),
                        Top = startY
                    };
                    newDevice.Titles.Add($"%Address\n{computer.Name}"); // Add the IP as the title for visibility
                    _currentState.Devices.Add(newDevice);
                    DrawMap(_currentState);

                    index++;
                }

                // 5. Persist map state
                _currentState.HasUnsavedChanges = true;


                MessageBox.Show($"Successfully added {importWin.SelectedComputers.Count} device(s) to the map.", "Import Complete", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private async Task RunAutoDiscoveryAsync()
        {
            if (_currentState == null) return;

            // 1. Set a safe fallback just in case the map is completely empty
            string defaultCidr = "192.168.102.0/24";

            // 2. Grab the first valid IP from the map's current devices
            var referenceDevice = _currentState.Devices.FirstOrDefault(d =>
                !string.IsNullOrWhiteSpace(d.Address) &&
                d.Address.StartsWith("192.168.") && d.Address.EndsWith("101") &&
                System.Net.IPAddress.TryParse(d.Address, out _));

            // 3. If we found a device, calculate the base network
            if (referenceDevice != null)
            {
                var parts = referenceDevice.Address.Split('.');
                if (parts.Length == 4)
                {
                    // Replaces the last octet with .0/24 (e.g., 192.168.50.45 becomes 192.168.50.0/24)
                    defaultCidr = $"{parts[0]}.{parts[1]}.{parts[2]}.0/24";
                }
            }

            // 4. Show the native WPF InputDialog with the dynamically calculated default!
            var inputDialog = new InputDialog(
                "Enter the CIDR network range to scan (e.g., 192.168.102.0/24 or 192.168.110.0/24):",
                "Auto-Discovery",
                defaultCidr)
            {
                // Center it over the main window
                Owner = Window.GetWindow(this)
            };

            if (inputDialog.ShowDialog() != true || string.IsNullOrWhiteSpace(inputDialog.ResponseText))
            {
                return; // User canceled or left it blank
            }

            string cidrInput = inputDialog.ResponseText.Trim();

            if (string.IsNullOrWhiteSpace(cidrInput)) return;

            var allTargetIps = Services.PingService.GenerateMathematicalIps(cidrInput);
            if (allTargetIps.Count == 0)
            {
                MessageBox.Show("Invalid CIDR format.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            // 2. Strip out IPs that are ALREADY on the current map
            var existingIps = _currentState.Devices
                .Where(d => !string.IsNullOrWhiteSpace(d.Address))
                .Select(d => d.Address)
                .ToHashSet();

            var ipsToScan = await Services.PingService.GetNewlyDiscoveredOnlineIpsAsync(cidrInput, existingIps);

            if (ipsToScan.Count == 0)
            {
                MessageBox.Show("All IPs in this range are already on the map!", "Finished", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // 3. Multithreaded Ping Sweep
            var foundIps = new ConcurrentBag<string>();
            using var semaphore = new SemaphoreSlim(100); // 100 concurrent pings for rapid sweeping
            var tasks = new List<Task>();

            // Optional: Show a loading overlay here so the user knows it's scanning

            foreach (var ip in ipsToScan)
            {
                tasks.Add(Task.Run(async () =>
                {
                    await semaphore.WaitAsync();
                    try
                    {
                        foundIps.Add(ip);
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                }));
            }

            await Task.WhenAll(tasks);
            var sortedResults = foundIps.OrderBy(ip => Version.Parse(ip)).ToList();
            // 4. Process Results
            if (foundIps.IsEmpty)
            {
                MessageBox.Show("No new active devices found in that range.", "Discovery Complete", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            else
            {
                var result = MessageBox.Show($"Found {foundIps.Count} new active devices. Do you want to add them to the map?", "Discovery Complete", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (result == MessageBoxResult.Yes)
                {
                    foreach (var ip in sortedResults)
                    {
                        if (foundIps.Contains(ip))
                        {
                            var newDevice = new NetworkDevice
                            {
                                MapId = _currentState!.MapId,
                                Address = ip,
                                Left = 50, // Default position; you might want to adjust this
                                Top = 50,  // Default position; you might want to adjust this
                                GroupId = 1 // Default group; you might want to adjust this
                            };
                            newDevice.Titles.Add(ip); // Add the IP as the title for visibility
                            _currentState.Devices.Add(newDevice);

                            _currentState?.HasUnsavedChanges = true;
                            DrawMap(_currentState);
                        }
                    }
                }

            }
        }
    }
}
