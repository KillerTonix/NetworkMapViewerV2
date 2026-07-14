using NetworkMapViewerV2.Models;
using System.IO;
using System.Text;

namespace NetworkMapViewerV2.Services
{
    /// <summary>
    /// Logs device state-change events to a text file and generates HTML/CSV reports.
    /// </summary>
    public class NotificationService
    {
        private static readonly string LogDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs");
        private static readonly string EventLogPath = Path.Combine(LogDirectory, "events.log");

        private readonly object _lock = new();
        private readonly List<DeviceEvent> _sessionEvents = [];

        public IReadOnlyList<DeviceEvent> SessionEvents => _sessionEvents;

        public NotificationService()
        {
            Directory.CreateDirectory(LogDirectory);
        }

        /// <summary>
        /// Logs a device state change to disk and keeps it in session memory.
        /// </summary>
        public void LogEvent(string deviceName, string address, bool isOnline)
        {
            var evt = new DeviceEvent
            {
                Timestamp = DateTime.Now,
                DeviceName = deviceName,
                Address = address,
                Status = isOnline ? "Online" : "Offline",
            };

            lock (_lock)
            {
                _sessionEvents.Add(evt);

                try
                {
                    string line = $"{evt.Timestamp:yyyy-MM-dd HH:mm:ss}\t{evt.DeviceName}\t{evt.Status}\t{evt.Address}";
                    File.AppendAllText(EventLogPath, line + Environment.NewLine);
                }
                catch { /* Don't crash if log write fails */ }
            }
        }

        /// <summary>
        /// Reads all persisted events from the log file.
        /// </summary>
        public static List<DeviceEvent> LoadAllEvents()
        {
            var events = new List<DeviceEvent>();
            if (!File.Exists(EventLogPath)) return events;

            try
            {
                foreach (var line in File.ReadAllLines(EventLogPath))
                {
                    var parts = line.Split('\t');
                    if (parts.Length >= 4 && DateTime.TryParse(parts[0], out DateTime ts))
                    {
                        events.Add(new DeviceEvent
                        {
                            Timestamp = ts,
                            Status = parts[1],
                            DeviceName = parts[2],
                            Address = parts[3],
                        });
                    }
                }
            }
            catch { }

            return events;
        }

        /// <summary>
        /// Deletes events older than the specified number of days from the log file.
        /// </summary>
        public static void PurgeOldEvents(int olderThanDays)
        {
            if (!File.Exists(EventLogPath)) return;

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

        /// <summary>
        /// Generates an HTML report from all persisted events and returns the file path.
        /// </summary>
        public static string GenerateHtmlReport()
        {
            var events = LoadAllEvents();
            var sb = new StringBuilder();

            sb.AppendLine("<!DOCTYPE html>");
            sb.AppendLine("<html><head><meta charset='utf-8'/>");
            sb.AppendLine("<title>Network Map Viewer — Event Report</title>");
            sb.AppendLine("<style>");
            sb.AppendLine("body { font-family: Segoe UI, sans-serif; margin: 20px; }");
            sb.AppendLine("h1 { color: #333; }");
            sb.AppendLine("table { border-collapse: collapse; width: 100%; margin-top: 15px; }");
            sb.AppendLine("th, td { border: 1px solid #ccc; padding: 8px 12px; text-align: left; }");
            sb.AppendLine("th { background: #4a90d9; color: #fff; }");
            sb.AppendLine("tr:nth-child(even) { background: #f2f2f2; }");
            sb.AppendLine(".online { color: green; font-weight: bold; }");
            sb.AppendLine(".offline { color: red; font-weight: bold; }");
            sb.AppendLine("</style></head><body>");
            sb.AppendLine($"<h1>Network Event Report</h1>");
            sb.AppendLine($"<p>Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss} &mdash; Total events: {events.Count}</p>");
            sb.AppendLine("<table><tr><th>#</th><th>Time</th><th>Status</th><th>Device</th><th>Address</th></tr>");

            for (int i = 0; i < events.Count; i++)
            {
                var e = events[i];
                string cls = e.Status == "Online" ? "online" : "offline";
                sb.AppendLine($"<tr><td>{i + 1}</td><td>{e.Timestamp:yyyy-MM-dd HH:mm:ss}</td><td class='{cls}'>{e.DeviceName}</td><td>{e.Status}</td><td>{e.Address}</td></tr>");
            }

            sb.AppendLine("</table></body></html>");

            string reportPath = Path.Combine(LogDirectory, $"report_{DateTime.Now:yyyyMMdd_HHmmss}.html");
            File.WriteAllText(reportPath, sb.ToString());
            return reportPath;
        }

        /// <summary>
        /// Generates a CSV report from all persisted events and returns the file path.
        /// </summary>
        public static string GenerateCsvReport()
        {
            var events = LoadAllEvents();
            var sb = new StringBuilder();

            sb.AppendLine("Timestamp,Status,DeviceName,Address,RoundtripMs");
            foreach (var e in events)
            {
                sb.AppendLine($"{e.Timestamp:yyyy-MM-dd HH:mm:ss},{EscapeCsv(e.DeviceName)},{e.Status},{e.Address}");
            }

            string reportPath = Path.Combine(LogDirectory, $"report_{DateTime.Now:yyyyMMdd_HHmmss}.csv");
            File.WriteAllText(reportPath, sb.ToString());
            return reportPath;
        }

        private static string EscapeCsv(string value)
        {
            if (value.Contains(',') || value.Contains('"') || value.Contains('\n'))
                return $"\"{value.Replace("\"", "\"\"")}\"";
            return value;
        }
    }
}
