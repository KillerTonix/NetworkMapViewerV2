using NetworkMapViewerV2.Data;
using NetworkMapViewerV2.Models;
using System.Collections.Concurrent;
using System.Net.NetworkInformation;
using System.Windows;

namespace NetworkMapViewerV2.Services
{
    public class PingService
    {
        private CancellationTokenSource? _cts;

        public bool IsRunning => _cts != null && !_cts.IsCancellationRequested;

        // NOTICE: We added the parameter back so the ViewModel can pass the Hybrid list!
        // 1. Add the "Action<string, bool> onPingResult" parameter
        public void StartPinging(IEnumerable<NetworkDevice> devicesToPing, Action<string, bool>? onPingResult = null)
        {
            StopPinging();
            _cts = new CancellationTokenSource();
            var token = _cts.Token;

            Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(2000, token);

                    while (!token.IsCancellationRequested)
                    {
                        // 1. THE CRITICAL FIX: Safely snapshot the devices list on the UI thread!
                        // This completely prevents "Collection was modified" crashes.
                        List<NetworkDevice> safeDevicesSnapshot = [];
                        var app = Application.Current;

                        if (app != null && app.Dispatcher != null && !app.Dispatcher.HasShutdownStarted)
                        {
                            await app.Dispatcher.InvokeAsync(() =>
                            {
                                // Freezes a copy of the list for this specific ping cycle
                                safeDevicesSnapshot = devicesToPing.ToList();
                            });
                        }

                        if (safeDevicesSnapshot.Count == 0)
                        {
                            await Task.Delay(2000, token);
                            continue; // Skip this cycle if the map is empty
                        }

                        var tasks = new List<Task>();
                        using var throttler = new SemaphoreSlim(20);

                        // 2. Iterate over the SAFE snapshot, not the live UI collection!
                        foreach (var device in safeDevicesSnapshot)
                        {
                            if (string.IsNullOrWhiteSpace(device.Address) || device.Address == "0.0.0.0") continue;

                            tasks.Add(Task.Run(async () =>
                            {
                                await throttler.WaitAsync(token);
                                try
                                {
                                    bool isUp = await PingHostAsync(device.Address);

                                    if (!token.IsCancellationRequested && app != null && !app.Dispatcher.HasShutdownStarted)
                                    {
                                        await app.Dispatcher.InvokeAsync(() =>
                                        {
                                            // 3. Trigger the UI change safely
                                            device.IsOnline = isUp;
                                            onPingResult?.Invoke(device.Address, isUp);
                                        });
                                    }
                                }
                                catch { }
                                finally { throttler.Release(); }
                            }, token));
                        }

                        await Task.WhenAll(tasks);

                        var settings = SettingsService.Load();
                        int delayMs = (settings.PingPeriodSeconds > 0 ? settings.PingPeriodSeconds : 4) * 1000;
                        await Task.Delay(delayMs, token);
                    }
                }
                catch { } // Silently exit if cancellation is requested
            }, token);
        }

        public void StopPinging()
        {
            if (_cts != null)
            {
                _cts.Cancel(); // Tell the loop to stop
                // THE FIX: DO NOT call _cts.Dispose() here! 
                // Let the garbage collector handle it, otherwise running tasks crash when checking IsCancellationRequested
                _cts = null;
            }
        }

        public static async Task<bool> PingHostAsync(string ipAddress, int maxAttempts = 2)
        {
            if (string.IsNullOrWhiteSpace(ipAddress) || ipAddress == "0.0.0.0")
            {
                return false;
            }

            using var ping = new Ping();

            // 2. The Retry Loop
            for (int attempt = 1; attempt <= maxAttempts; attempt++)
            {
                try
                {
                    var reply = await ping.SendPingAsync(ipAddress, 1000);

                    if (reply.Status == IPStatus.Success)
                    {                       
                        return true;
                    }
                }
                catch (PingException)
                {
                    // A network exception occurred (e.g., DNS failed or route dropped)
                    // Do nothing here, just let it proceed to the retry delay
                }

                // 3. The "Phantom Drop" Delay
                // If the ping failed, and we still have attempts left, wait 500ms 
                // to let the switch catch its breath before we hit it again.
                if (attempt < maxAttempts)
                {
                    await Task.Delay(500);
                }
            }

            // 4. If we reach this line, it failed all 3 attempts.
            // It is officially offline.
            return false;
        }




        // 1. Rename this so it's clear it just generates the mathematical addresses
        public static List<string> GenerateMathematicalIps(string cidr)
        {
            var usableIps = new List<string>();
            var parts = cidr.Split('/');

            if (parts.Length != 2 || !System.Net.IPAddress.TryParse(parts[0], out var baseIp) || !int.TryParse(parts[1], out int maskBits))
                return usableIps;

            byte[] ipBytes = baseIp.GetAddressBytes();
            uint ip = (uint)((ipBytes[0] << 24) | (ipBytes[1] << 16) | (ipBytes[2] << 8) | ipBytes[3]);
            uint mask = 0xffffffff << (32 - maskBits);

            uint network = ip & mask;
            uint broadcast = network | ~mask;

            // This bitwise loop ensures that if you scan a wide /22 range, 
            // you correctly get all the intermediate .0 and .255 addresses!
            for (uint i = network + 2; i < broadcast; i++)
            {
                byte[] bytes = [(byte)(i >> 24), (byte)(i >> 16), (byte)(i >> 8), (byte)i];
                usableIps.Add(new System.Net.IPAddress(bytes).ToString());
            }

            return usableIps;
        }

        // 2. THIS is the method you actually call to get your final list of online devices!
        public static async Task<List<string>> GetNewlyDiscoveredOnlineIpsAsync(string cidr, HashSet<string> existingIps)
        {
            // Step A: Get all possible mathematical IPs (e.g., 254)
            var allPossibleIps = GenerateMathematicalIps(cidr);

            // Step B: Filter out the ones already on your map (e.g., leaves 213)
            var ipsToScan = allPossibleIps.Where(ip => !existingIps.Contains(ip)).ToList();

            var onlineIps = new ConcurrentBag<string>();
            using var semaphore = new SemaphoreSlim(100);
            var tasks = new List<Task>();

            // Step C: Ping the remaining 213 addresses concurrently
            foreach (var ip in ipsToScan)
            {
                tasks.Add(Task.Run(async () =>
                {
                    await semaphore.WaitAsync();
                    try
                    {
                        // If it replies, it gets added to the final list!
                        if (await PingHostAsync(ip, maxAttempts: 1))
                        {
                            onlineIps.Add(ip);
                        }
                    }
                    finally
                    {
                        semaphore.Release();
                    }
                }));
            }

            await Task.WhenAll(tasks);

            // Returns ONLY the addresses that are alive and not on the map yet
            return [.. onlineIps.OrderBy(ip => Version.Parse(ip))];
        }
    }
}