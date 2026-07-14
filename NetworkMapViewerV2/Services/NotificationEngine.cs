using NetworkMapViewerV2.Models;
using System.Windows;
using System.Windows.Threading;

namespace NetworkMapViewerV2.Services
{
    public static class NotificationEngine
    {
        // In reality, load this from your SQLite database!
        public static List<NotificationRule> ActiveRules { get; set; } = [];
        private static List<string> _pendingAlerts = new();
        private static DispatcherTimer _toastTimer = new();
        private static bool isShowMessage = SettingsService.Load().ENS_ShowMessage;

        static NotificationEngine()
        {
            _toastTimer.Interval = TimeSpan.FromSeconds(3); // Wait 3 seconds for other devices to fail before alerting
            _toastTimer.Tick += FlushAlertsToToast;
        }

        public static void ProcessStateChange(NetworkDevice device, bool wentDown, bool wokeUp)
        {
            // 1. THE SANITY CHECK: Ignore map placeholders and empty IPs entirely!
            if (string.IsNullOrWhiteSpace(device.Address) || device.Address == "0.0.0.0")
                return;

            // 2. THE RULES CHECK: Does this device match any rule in your Options tab?
            bool ruleMatched = false;

            // ActiveRules should be populated from your settings.json when the app starts
            foreach (var rule in ActiveRules)
            {
                // If TargetGroupId is null, it means "All Devices". Otherwise, it must match the device's group (e.g., Printers)
                bool isGroupMatch = (rule.TargetGroupId == null || rule.TargetGroupId == device.GroupId);

                // Does the rule care about the direction the device went?
                bool isTriggerMatch = (wentDown && rule.TriggerOnDown) || (wokeUp && rule.TriggerOnUp);

                if (isGroupMatch && isTriggerMatch)
                {
                    ruleMatched = true;
                    break; // We found a valid rule, stop checking and proceed to notify!
                }
            }

            // If no rules matched this specific device, silently exit and do nothing.
            if (!ruleMatched) return;

            // 3. BUILD THE BUFFER
            // We only reach this point if it's a real IP *and* a rule allows it!
            string stateString = wentDown ? "is down" : "is up";
            string deviceName = string.Empty;

           
            foreach (string deviceTitle in device.Titles)
            {
                if (!string.IsNullOrWhiteSpace(deviceTitle))
                {
                    deviceName += deviceTitle + " ";
                }
            }

            var settings = SettingsService.Load();
            if (settings.ENS_SaveToLog)
            {
                var logevent = new NotificationService();
                logevent.LogEvent(deviceName.Replace("%Address", ""), device.Address, !wentDown);
            }


            string msg = $"{deviceName.Replace("%Address", device.Address)}: {stateString}";       
            if (!_pendingAlerts.Contains(msg))
            {
                _pendingAlerts.Add(msg);

                // Reset the 3-second buffer timer
                _toastTimer.Stop();
                _toastTimer.Start();
            }
        }

        private static void FlushAlertsToToast(object? sender, EventArgs e)
        {
            _toastTimer.Stop();
            if (_pendingAlerts.Count == 0) return;

            // 1. Scan the buffer to see what kind of alerts we collected
            bool hasDownAlerts = _pendingAlerts.Any(msg => msg.Contains("is down"));
            bool hasUpAlerts = _pendingAlerts.Any(msg => msg.Contains("is up"));

            // 2. Dynamically set the Title based on what happened
            string title = "Network Status Alert";
            if (hasDownAlerts && !hasUpAlerts)
            {
                title = "Device(s) Went Offline";
            }
            else if (!hasDownAlerts && hasUpAlerts)
            {
                title = "Device(s) Woke Up";
            }
            else if (hasDownAlerts && hasUpAlerts)
            {
                title = "Mixed Network Changes";
            }

            // 3. Determine the Color (isDown = true makes it Red, false makes it Green)
            // If even ONE device went down, we force the window to be Red to get your attention!
            bool isCritical = hasDownAlerts;

            // 4. Build the message and clear the buffer
            string message = string.Join("\n", _pendingAlerts);
            _pendingAlerts.Clear();

            if (isShowMessage)
            {
                // 5. Show the smart Toast!
                Application.Current.Dispatcher.Invoke(() =>
                {
                    var toast = new Views.ToastNotificationWindow(title, message, isCritical);
                    toast.Show();
                });
            }
        }
    }
}