using NetworkMapViewerV2.Models;
using System.Net.NetworkInformation;
using System.Windows;

namespace NetworkMapViewerV2.Services
{
    public class PingService
    {
        private CancellationTokenSource _cts;

        public bool IsRunning => _cts != null && !_cts.IsCancellationRequested;

        public void StartPinging(IEnumerable<NetworkDevice> devices)
        {
            StopPinging(); // Stop any existing sweeps before starting a new one

            _cts = new CancellationTokenSource();
            var token = _cts.Token;

            Task.Run(async () =>
            {
                // --- FIX 1: Give the UI a 2-second head start to draw the window! ---
                await Task.Delay(2000, token);

                while (!token.IsCancellationRequested)
                {
                    var tasks = new List<Task>();

                    foreach (var device in devices)
                    {
                        if (string.IsNullOrWhiteSpace(device.Address)) continue;

                        // Create an async task for each device
                        tasks.Add(Task.Run(async () =>
                        {
                            bool isUp = await PingHostAsync(device.Address);

                            if (!token.IsCancellationRequested)
                            {
                                // --- FIX: Safely check if the app is still alive before updating the UI! ---
                                var app = Application.Current;
                                if (app != null && app.Dispatcher != null && !app.Dispatcher.HasShutdownStarted)
                                {
                                    app.Dispatcher.InvokeAsync(() =>
                                    {
                                        device.IsOnline = isUp;
                                    });
                                }
                            }
                        }, token));
                    }

                    // Execute all pings simultaneously
                    await Task.WhenAll(tasks);

                    // Wait for the next sweep based on your settings
                    var settings = SettingsService.Load();
                    int delayMs = (settings.PingPeriodSeconds > 0 ? settings.PingPeriodSeconds : 4) * 1000;
                    await Task.Delay(delayMs, token);
                }
            }, token);
        }

        public void StopPinging()
        {
            if (_cts != null)
            {
                _cts.Cancel();
                _cts.Dispose();
                _cts = null;
            }
        }

        private async Task<bool> PingHostAsync(string ipAddress)
        {
            try
            {
                using var pinger = new Ping();
                // 1500ms timeout. If it takes longer than 1.5s to reply, consider it down.
                var reply = await pinger.SendPingAsync(ipAddress, 1500);
                return reply.Status == IPStatus.Success;
            }
            catch
            {
                return false;
            }
        }
    }
}