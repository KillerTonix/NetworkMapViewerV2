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
        public string Details { get; set; } = string.Empty;
    }
}