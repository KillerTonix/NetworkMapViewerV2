using NetworkMapViewerV2.Models;
using System.IO;
using System.Text;

namespace NetworkMapViewerV2.Services
{
    /// <summary>
    /// Logs device state-change events to disk and generates HTML/CSV reports.
    /// </summary>
    public class NotificationService
    {
        private static readonly string UserSettingsFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "NetworkMapViewerV2");
        private static readonly string LogDirectory = Path.Combine(UserSettingsFolder, "Logs");
        private static readonly string EventLogPath = Path.Combine(LogDirectory, "events.log");
        private static readonly object FileLock = new();

        static NotificationService()
        {
            Directory.CreateDirectory(LogDirectory);
        }

        /// <summary>
        /// Logs a device state change to the persistent log file.
        /// </summary>
        public static void LogEvent(string deviceName, string address, bool isOnline)
        {
            var evt = new DeviceEvent
            {
                Timestamp = DateTime.Now,
                DeviceName = string.IsNullOrWhiteSpace(deviceName) ? "Unknown Device" : deviceName,
                Address = address,
                Status = isOnline ? "Online" : "Offline",
            };

            lock (FileLock)
            {
                try
                {
                    string sign = isOnline ? "+" : "-";
                    string updwn = isOnline ? "is up" : "is down";
                    string line = $"{sign} {evt.Timestamp:dd.MM.yyyy HH:mm:ss}\t{evt.DeviceName.Replace(evt.Address, "")}\t{evt.Address}\t{updwn}";
                    File.AppendAllText(EventLogPath, line + Environment.NewLine);
                }
                catch { /* Suppress IO failures to keep engine running */ }
            }
        }

        /// <summary>
        /// Reads all persisted events from the log file.
        /// </summary>
        public static List<DeviceEvent> LoadAllEvents()
        {
            var events = new List<DeviceEvent>();
            if (!File.Exists(EventLogPath)) return events;

            lock (FileLock)
            {
                try
                {
                    foreach (var line in File.ReadAllLines(EventLogPath))
                    {
                        var parts = line.Split('\t');
                        if (parts.Length >= 4)
                        {
                            events.Add(new DeviceEvent
                            {
                                Timestamp = DateTime.Parse(parts[0].Substring(1)),
                                DeviceName = parts[1],
                                Status = parts[3],
                                Address = parts[2]
                            });
                        }
                    }
                }
                catch { }
            }

            return events;
        }

        /// <summary>
        /// Deletes events older than the specified number of days from the log file.
        /// </summary>
        public static void PurgeOldEvents(int olderThanDays)
        {
            if (!File.Exists(EventLogPath)) return;

            lock (FileLock)
            {
                try
                {
                    var cutoff = DateTime.Now.AddDays(-olderThanDays);
                    var lines = File.ReadAllLines(EventLogPath);
                    var kept = new List<string>();

                    foreach (var line in lines)
                    {
                        var parts = line.Split('\t');
                        if (parts.Length >= 1 && DateTime.TryParse(parts[0], out DateTime ts) && ts >= cutoff)
                        {
                            kept.Add(line);
                        }
                    }

                    File.WriteAllLines(EventLogPath, kept);
                }
                catch { }
            }
        }

        /// <summary>
        /// Generates an HTML report from all persisted events.
        /// </summary>
        public static string GenerateHtmlReport()
        {
            var events = LoadAllEvents();
            var sb = new StringBuilder();

            sb.AppendLine("<!DOCTYPE html>");
            sb.AppendLine("<html><head><meta charset='utf-8'/>");
            sb.AppendLine("<title>Network Map Viewer — Event Report</title>");
            sb.AppendLine("<style>");
            sb.AppendLine("body { font-family: Segoe UI, sans-serif; margin: 20px; color: #0F172A; }");
            sb.AppendLine("h1 { color: #1E293B; }");
            sb.AppendLine("table { border-collapse: collapse; width: 100%; margin-top: 15px; }");
            sb.AppendLine("th, td { border: 1px solid #CBD5E1; padding: 8px 12px; text-align: left; }");
            sb.AppendLine("th { background: #2563EB; color: #FFFFFF; }");
            sb.AppendLine("tr:nth-child(even) { background: #F8FAFC; }");
            sb.AppendLine(".online { color: #16A34A; font-weight: bold; }");
            sb.AppendLine(".offline { color: #DC2626; font-weight: bold; }");
            sb.AppendLine("</style></head><body>");
            sb.AppendLine("<h1>Network Event Report</h1>");
            sb.AppendLine($"<p>Generated: {DateTime.Now:dd.MM.yyyy HH:mm:ss} &mdash; Total events: {events.Count}</p>");
            sb.AppendLine("<table><tr><th>#</th><th>Time</th><th>Status</th><th>Device</th><th>Address</th></tr>");

            for (int i = 0; i < events.Count; i++)
            {
                var e = events[i];
                string cls = e.Status.Equals("Online", StringComparison.OrdinalIgnoreCase) ? "online" : "offline";
                sb.AppendLine($"<tr><td>{i + 1}</td><td>{e.Timestamp:dd.MM.yyyy HH:mm:ss}</td><td class='{cls}'>{e.Status}</td><td>{EscapeHtml(e.DeviceName)}</td><td>{e.Address}</td></tr>");
            }

            sb.AppendLine("</table></body></html>");

            string reportPath = Path.Combine(LogDirectory, $"report_{DateTime.Now:yyyyMMdd_HHmmss}.html");
            File.WriteAllText(reportPath, sb.ToString());
            return reportPath;
        }

        /// <summary>
        /// Generates a CSV report from all persisted events.
        /// </summary>
        public static string GenerateCsvReport()
        {
            var events = LoadAllEvents();
            var sb = new StringBuilder();

            sb.AppendLine("Timestamp,Status,DeviceName,Address");
            foreach (var e in events)
            {
                sb.AppendLine($"{e.Timestamp:dd.MM.yyyy HH:mm:ss},{e.Status},{EscapeCsv(e.DeviceName)},{e.Address}");
            }

            string reportPath = Path.Combine(LogDirectory, $"report_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
            File.WriteAllText(reportPath, sb.ToString());
            return reportPath;
        }

        private static string EscapeCsv(string value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            if (value.Contains(',') || value.Contains('"') || value.Contains('\n'))
                return $"\"{value.Replace("\"", "\"\"")}\"";
            return value;
        }

        private static string EscapeHtml(string value)
        {
            if (string.IsNullOrEmpty(value)) return "";
            return value.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
        }
    }
}