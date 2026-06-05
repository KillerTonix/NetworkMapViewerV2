namespace NetworkMapViewerV2.Models
{
    public class DeviceEvent
    {
        public DateTime Timestamp { get; set; }
        public string DeviceName { get; set; } = string.Empty;
        public string Address { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty; // "Online" or "Offline"
        public long RoundtripMs { get; set; }
    }
}
