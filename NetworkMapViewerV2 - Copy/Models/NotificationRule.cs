namespace NetworkMapViewerV2.Models
{
    public class NotificationRule
    {      
        public int? TargetGroupId { get; set; }  // e.g., NUCs or Printers. Null if specific device.
        public string TargetName { get; set; } = "Unknown"; // E.g., "Any 'Printer'"

        // Triggers
        public bool TriggerOnDown { get; set; } = true;
        public bool TriggerOnUp { get; set; } = true;

    }
}