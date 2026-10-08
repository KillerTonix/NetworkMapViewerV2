using NetworkMapViewerV2.Models;
using System.IO;
using System.Media;
using System.Windows;
using System.Windows.Threading;

namespace NetworkMapViewerV2.Services
{
    public static class NotificationEngine
    {
        public static List<NotificationRule> ActiveRules { get; set; } = [];

        private static readonly List<string> PendingAlerts = [];
        private static readonly object BufferLock = new();
        private static readonly DispatcherTimer ToastTimer;

        static NotificationEngine()
        {
            ToastTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(3)
            };
            ToastTimer.Tick += FlushAlertsToToast;
        }

        public static void ProcessStateChange(NetworkDevice device, bool wentDown, bool wokeUp)
        {
            // 1. Sanity Check
            if (device == null || string.IsNullOrWhiteSpace(device.Address) || device.Address == "0.0.0.0")
                return;

            // 2. Rules Matching Check
            bool ruleMatched = ActiveRules.Any(rule =>
            {
                bool isGroupMatch = rule.TargetGroupId == null || rule.TargetGroupId == device.GroupId;
                bool isTriggerMatch = (wentDown && rule.TriggerOnDown) || (wokeUp && rule.TriggerOnUp);
                return isGroupMatch && isTriggerMatch;
            });

            if (!ruleMatched) return;

            // 3. Format Device Name
            string deviceName = string.Join(" ", device.Titles?.Where(t => !string.IsNullOrWhiteSpace(t)) ?? [])
                                      .Replace("%Address", device.Address)
                                      .Trim();

            if (string.IsNullOrEmpty(deviceName))
                deviceName = device.Address;

            string stateString = wentDown ? "is down" : "is up";

            // 4. Log Event
            var settings = SettingsService.Load();
            if (settings.ENS_SaveToLog)
            {
                NotificationService.LogEvent(deviceName, device.Address, !wentDown);
            }

            // 5. Buffer Alerts
            string msg = $"{deviceName}: {stateString}";
            lock (BufferLock)
            {
                if (!PendingAlerts.Contains(msg))
                {
                    PendingAlerts.Add(msg);

                    ToastTimer.Stop();
                    ToastTimer.Start();
                }
            }
        }

        private static void FlushAlertsToToast(object? sender, EventArgs e)
        {
            ToastTimer.Stop();

            List<string> alertsToFlush;
            lock (BufferLock)
            {
                if (PendingAlerts.Count == 0) return;
                alertsToFlush = [.. PendingAlerts];
                PendingAlerts.Clear();
            }

            bool hasDownAlerts = alertsToFlush.Any(msg => msg.Contains("is down"));
            bool hasUpAlerts = alertsToFlush.Any(msg => msg.Contains("is up"));

            string title = (hasDownAlerts, hasUpAlerts) switch
            {
                (true, false) => "Device(s) Went Offline",
                (false, true) => "Device(s) Woke Up",
                _ => "Mixed Network Changes"
            };

            var settings = SettingsService.Load();

            // Play Audio Alerts
            PlayConfiguredSound(hasDownAlerts, hasUpAlerts, settings);

            // Show Toast Notification Window
            if (settings.ENS_ShowMessage)
            {
                string message = string.Join("\n", alertsToFlush);
                Application.Current.Dispatcher.Invoke(() =>
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
            {
                soundPath = settings.ENS_OfflineSoundFilePath;
            }
            else if (hasUp && settings.ENS_PlayOnlineSound)
            {
                soundPath = settings.ENS_OnlineSoundFilePath;
            }

            if (!string.IsNullOrWhiteSpace(soundPath) && File.Exists(soundPath))
            {
                try
                {
                    using var player = new SoundPlayer(soundPath);
                    player.Play();
                }
                catch { /* Ignore audio playback errors */ }
            }
        }
    }
}