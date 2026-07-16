using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NetworkMapViewerV2.Helpers.LocalFetcher;
using NetworkMapViewerV2.Models;
using NetworkMapViewerV2.Services;
using NetworkMapViewerV2.Views;
using System.Windows;

namespace NetworkMapViewerV2.ViewModels
{
    public partial class MainViewModel
    {
        private readonly PingService _pingService = new();
        [ObservableProperty]
        public partial bool IsPinging { get; set; } = false;
        public PingService PingService => _pingService;


        [RelayCommand]
        public void TogglePing()
        {
            if (PingService.IsRunning)
            {
                PingService.StopPinging();
                IsPinging = false;

                // Clear the colors when stopped
                if (SelectedTab != null)
                {
                    foreach (var device in SelectedTab.Devices) device.IsOnline = false;
                }
            }
            else if (SelectedTab != null)
            {
                PingService.StartPinging(SelectedTab.Devices);
                IsPinging = true;
            }
        }

        private static async Task RecalculatePingsAsync(IEnumerable<NetworkDevice> devices)
        {
            // ==========================================
            // THE SAFEGUARD: Only allow 2000 pings at a time
            // to prevent the memory/network avalanche!
            // ==========================================
            using var semaphore = new SemaphoreSlim(2000);
            var pingTasks = new List<Task>();

            foreach (var device in devices)
            {
                if (string.IsNullOrWhiteSpace(device.Address)) continue;

                pingTasks.Add(Task.Run(async () =>
                {
                    await semaphore.WaitAsync(); // Wait in line if 20 are already running
                    try
                    {
                        using var ping = new System.Net.NetworkInformation.Ping();
                        var reply = await ping.SendPingAsync(device.Address, 2000); // Strict 2-second timeout

                        // Changing this property instantly triggers your PingStatusImageConverter!
                        device.IsOnline = (reply.Status == System.Net.NetworkInformation.IPStatus.Success);
                    }
                    catch
                    {
                        device.IsOnline = false;
                    }
                    finally
                    {
                        semaphore.Release(); // Let the next ping in line start
                    }
                }));
            }

            // Wait for all background pings to completely finish
            await Task.WhenAll(pingTasks);
        }

        [RelayCommand]
        private async Task UpdateGroupData()
        {
            var tab = SelectedTab;
            if (tab == null || tab.Devices.Count == 0) return;

            var activeGroupIds = tab.Devices.GroupBy(d => d.GroupId).Select(g => new { Id = g.Key, Count = g.Count() }).ToList();
            var repo = new Data.MapRepository();
            var allDbGroups = repo.GetAllDeviceGroups().ToDictionary(g => g.GroupId, g => g.GroupName);

            var activeGroupsForDialog = new List<ActiveGroupItem>();
            foreach (var groupInfo in activeGroupIds)
            {
                switch (groupInfo.Id)
                {
                    case 1:
                    case 2:
                    case 3:
                    case 4:
                        string name = allDbGroups.TryGetValue(groupInfo.Id, out string? gName) ? gName : "Unknown";
                        activeGroupsForDialog.Add(new ActiveGroupItem { GroupId = groupInfo.Id, GroupName = name, DeviceCount = groupInfo.Count });
                        break;
                    default:
                        continue; // Skip any other group IDs

                }
            }

            if (activeGroupsForDialog.Count == 0) return;

            var dialog = new UpdateGroupDataWindow(activeGroupsForDialog, _appSettings.ScriptsPath)
            {
                Owner = Application.Current.MainWindow
            };

            if (dialog.ShowDialog() == true)
            {
                int targetGroupId = dialog.SelectedGroupId;
                string targetScript = dialog.SelectedScript;

                // ==========================================
                // THE NEW LOGIC: STRICTLY ONLINE DEVICES ONLY
                // ==========================================
                var devicesToUpdate = tab.Devices.Where(d =>
                    d.GroupId == targetGroupId &&
                    d.IsOnline == true && // <-- The crucial check!
                    !string.IsNullOrWhiteSpace(d.Address) &&
                    d.Address != "0.0.0.0").ToList();

                if (devicesToUpdate.Count == 0)
                {
                    MessageBox.Show("No ONLINE devices found in this group. Scan aborted.", "Skipped", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                // ==========================================
                // MULTI-THREADING: PROCESS 3 AT A TIME
                // ==========================================
                using var semaphore = new SemaphoreSlim(3); // Change this to 2 or 4 if you want to tweak the speed!
                var scanTasks = new List<Task>();

                foreach (var device in devicesToUpdate)
                {
                    scanTasks.Add(Task.Run(async () =>
                    {
                        await semaphore.WaitAsync(); // Wait in line if 3 are already running
                        try
                        {
                            if (targetScript == "Printer Web Scraper")
                            {
                                await AutoFill.RunPrinterAutoFill(this, device);
                            }
                            else if (targetScript == "Grandstream Web Scraper")
                            {
                                await AutoFill.RunGrandstreamAutoFill(this, device);
                            }
                            else
                            {
                                await AutoFill.RunAutoFillScript(this, device, targetScript);
                            }

                            // A tiny delay ensures the UI thread has time to draw the changes smoothly
                            await Task.Delay(100);
                        }
                        finally
                        {
                            semaphore.Release(); // Let the next device in line start
                        }
                    }));
                }

                // Wait for all the parallel batches to completely finish
                await Task.WhenAll(scanTasks);
                tab.HasUnsavedChanges = true;
                MessageBox.Show($"Update complete for {devicesToUpdate.Count} online devices.\nOffline devices were skipped.", "Finished", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }
    }
}
