using NetworkMapViewerV2.Data;
using NetworkMapViewerV2.Models;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.NetworkInformation;
using System.Threading;
using System.Threading.Tasks;

namespace NetworkMapViewerV2.Services
{
    public class BackgroundPingService
    {
        private static BackgroundPingService? _instance;
        public static BackgroundPingService Instance => _instance ??= new BackgroundPingService();

        private readonly ConcurrentDictionary<string, bool> _deviceStates = new();
        private readonly HashSet<string> _openTabAddresses = new(StringComparer.OrdinalIgnoreCase);
        private readonly object _tabLock = new();

        private CancellationTokenSource? _cts;
        private bool _isRunning;

        public event Action<string, bool>? DeviceStatusChanged;

        private BackgroundPingService() { }

        /// <summary>
        /// Updates the list of IP addresses currently open in active tabs.
        /// </summary>
        public void UpdateOpenTabAddresses(IEnumerable<string> addresses)
        {
            lock (_tabLock)
            {
                _openTabAddresses.Clear();
                foreach (var addr in addresses)
                {
                    if (!string.IsNullOrWhiteSpace(addr) && addr != "0.0.0.0")
                        _openTabAddresses.Add(addr);
                }
            }
        }

        public void Start()
        {
            if (_isRunning) return;

            _cts = new CancellationTokenSource();
            _isRunning = true;

            Task.Run(() => MonitoringLoopAsync(_cts.Token));
        }

        public void Stop()
        {
            if (!_isRunning) return;

            _cts?.Cancel();
            _isRunning = false;
        }

        private async Task MonitoringLoopAsync(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                var settings = SettingsService.Load();
                int periodSeconds = Math.Max(1, settings.PingPeriodSeconds);

                try
                {
                    // 1. Fetch all devices from database
                    List<NetworkDevice> allDevices = MapRepository.GetAllDevices();
                    var activeRules = NotificationEngine.ActiveRules ?? [];

                    // 2. Filter: Only ping devices that match Notification Rules OR are on Open Tabs
                    var targetDevices = allDevices
                        .Where(d => !string.IsNullOrWhiteSpace(d.Address) && d.Address != "0.0.0.0")
                        .Where(d => IsMonitoredByNotificationRules(d, activeRules) || IsInOpenTabs(d.Address))
                        .GroupBy(d => d.Address)
                        .Select(g => g.First())
                        .ToList();

                    Debug.WriteLine($"[BackgroundPing] Pinging {targetDevices.Count} active device(s) out of {allDevices.Count} total.");

                    var parallelOptions = new ParallelOptions
                    {
                        MaxDegreeOfParallelism = 20,
                        CancellationToken = ct
                    };

                    await Parallel.ForEachAsync(targetDevices, parallelOptions, async (device, token) =>
                    {
                        bool isOnline = await PingDeviceAsync(device.Address, 1000);
                        ProcessDeviceResult(device, isOnline);
                    });
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[BackgroundPing] Error in monitoring loop: {ex.Message}");
                }

                await Task.Delay(TimeSpan.FromSeconds(periodSeconds), ct).ConfigureAwait(false);
            }
        }

        private bool IsInOpenTabs(string address)
        {
            lock (_tabLock)
            {
                return _openTabAddresses.Contains(address);
            }
        }

        private static bool IsMonitoredByNotificationRules(NetworkDevice device, List<NotificationRule> rules)
        {
            if (rules == null || rules.Count == 0) return false;

            return rules.Any(rule =>
                rule.TargetGroupId == null ||
                rule.TargetGroupId == 0 || // 0 or null = All Groups
                rule.TargetGroupId == device.GroupId);
        }

        private void ProcessDeviceResult(NetworkDevice device, bool isOnline)
        {
            string ip = device.Address;

            if (_deviceStates.TryGetValue(ip, out bool wasOnline))
            {
                if (wasOnline != isOnline)
                {
                    _deviceStates[ip] = isOnline;

                    bool wentDown = wasOnline && !isOnline;
                    bool wokeUp = !wasOnline && isOnline;

                    NotificationEngine.ProcessStateChange(device, wentDown, wokeUp);
                    DeviceStatusChanged?.Invoke(ip, isOnline);
                }
            }
            else
            {
                _deviceStates[ip] = isOnline;
                DeviceStatusChanged?.Invoke(ip, isOnline);
            }
        }

        private static async Task<bool> PingDeviceAsync(string address, int timeoutMs)
        {
            try
            {
                using var ping = new Ping();
                PingReply reply = await ping.SendPingAsync(address, timeoutMs);
                return reply.Status == IPStatus.Success;
            }
            catch
            {
                return false;
            }
        }
       
    }
}