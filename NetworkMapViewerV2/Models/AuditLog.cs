using System;

namespace NetworkMapViewerV2.Models
{
    public class AuditLog
    {
        public int Id { get; set; }
        public string TimeStamp { get; set; } = string.Empty;
        public string MapName { get; set; } = string.Empty;
        public string UserName { get; set; } = string.Empty;
        public string ActionType { get; set; } = string.Empty;
        public string Target { get; set; } = string.Empty;
        public int TargetID { get; set; } = 0;
        public string Details { get; set; } = string.Empty;
        public string MachineName { get; set; } = string.Empty;
        public string OldValue { get; set; } = string.Empty;
        public string NewValue { get; set; } = string.Empty;

    }
}