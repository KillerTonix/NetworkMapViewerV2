using NetworkMapViewerV2.Models;
using System.Diagnostics;
using System.IO;
using System.Media;
using System.Windows;

namespace NetworkMapViewerV2.Services
{
    public static class NotificationEngine
    {
        public static List<NotificationRule> ActiveRules { get; set; } = [];

        private static readonly List<string> PendingAlerts = [];
        private static readonly object BufferLock = new();
        private static Timer? _bufferTimer;

        public static void ProcessStateChange(NetworkDevice device, bool wentDown, bool wokeUp)
        {
            Debug.WriteLine($"[NotificationEngine] ProcessStateChange triggered for {device?.Address} (wentDown={wentDown}, wokeUp={wokeUp})");

            // 1. Sanity Check
            if (device == null || string.IsNullOrWhiteSpace(device.Address) || device.Address == "0.0.0.0")
            {
                Debug.WriteLine("[NotificationEngine] Skipped: Invalid device or IP.");
                return;
            }

            // 2. Rules Existence Check
            if (ActiveRules == null || ActiveRules.Count == 0)
            {
                Debug.WriteLine("[NotificationEngine] Skipped: ActiveRules is empty! Ensure rules are loaded in App/MainViewModel startup.");
                return;
            }

            // 3. Rules Match Check
            bool ruleMatched = ActiveRules.Any(rule =>
            {
                bool isGroupMatch = rule.TargetGroupId == null || rule.TargetGroupId == device.GroupId;
                bool isTriggerMatch = (wentDown && rule.TriggerOnDown) || (wokeUp && rule.TriggerOnUp);
                return isGroupMatch && isTriggerMatch;
            });

            if (!ruleMatched)
            {
                Debug.WriteLine($"[NotificationEngine] Skipped: No matching rule found for {device.Address} (GroupId={device.GroupId}).");
                return;
            }

            Debug.WriteLine($"[NotificationEngine] MATCHED notification rule for {device.Address}!");

            // 4. Format Device Name
            string deviceName = string.Join(" ", device.Titles?.Where(t => !string.IsNullOrWhiteSpace(t)) ?? [])
                                      .Replace("%Address", device.Address)
                                      .Trim();

            Debug.WriteLine($"[NotificationEngine] Formatted device name: {deviceName}");
            if (string.IsNullOrEmpty(deviceName))
                deviceName = device.Address;

            string stateString = wentDown ? "is down" : "is up";

            // 5. Log Event
            var settings = SettingsService.Load();
            if (settings.ENS_SaveToLog)
            {
                NotificationService.LogEvent(deviceName, device.Address, !wentDown);
            }

            // 6. Buffer Alerts (Thread-Safe Debounce Timer)
            string msg = $"{deviceName}: {stateString}";
            lock (BufferLock)
            {
                if (!PendingAlerts.Contains(msg))
                {
                    PendingAlerts.Add(msg);
                }

                // Reset/restart 3-second buffer timer on thread pool
                _bufferTimer?.Dispose();
                _bufferTimer = new Timer(FlushAlertsToToast, null, 3000, Timeout.Infinite);
            }
        }

        private static void FlushAlertsToToast(object? state)
        {
            List<string> alertsToFlush;
            lock (BufferLock)
            {
                if (PendingAlerts.Count == 0) return;
                alertsToFlush = [.. PendingAlerts];
                PendingAlerts.Clear();
            }

            Debug.WriteLine($"[NotificationEngine] Flushing {alertsToFlush.Count} alert(s) to Toast notification UI...");

            bool hasDownAlerts = alertsToFlush.Any(msg => msg.Contains("is down"));
            bool hasUpAlerts = alertsToFlush.Any(msg => msg.Contains("is up"));

            string title = (hasDownAlerts, hasUpAlerts) switch
            {
                (true, false) => "Device(s) Went Offline",
                (false, true) => "Device(s) Woke Up",
                _ => "Mixed Network Changes"
            };

            var settings = SettingsService.Load();

            // Play Audio
            PlayConfiguredSound(hasDownAlerts, hasUpAlerts, settings);

            // Show Toast Notification Window on WPF UI Thread
            if (settings.ENS_ShowMessage)
            {
                string message = string.Join("\n", alertsToFlush);
                Application.Current?.Dispatcher.Invoke(() =>
                {
                    var toast = new Views.ToastNotificationWindow(title, message, hasDownAlerts);
                    toast.Show();
                });
            }
        }

        private static void PlayConfiguredSound(bool hasDown, bool hasUp, AppSettings settings)
        {
            string? soundPath = null;

            if (hasDown && settings.ENS_PlayOfflineSound)
                soundPath = settings.ENS_OfflineSoundFilePath;
            else if (hasUp && settings.ENS_PlayOnlineSound)
                soundPath = settings.ENS_OnlineSoundFilePath;

            if (!string.IsNullOrWhiteSpace(soundPath) && File.Exists(soundPath))
            {
                try
                {
                    using var player = new SoundPlayer(soundPath);
                    player.Play();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[NotificationEngine] Sound Error: {ex.Message}");
                }
            }
        }
    }
}