using System;

namespace NetworkMapViewerV2.Models
{
    public class AuditLog
    {
        public int LogId { get; set; }
        public string Timestamp { get; set; } = string.Empty; // ISO 8601 format
        public string Username { get; set; } = string.Empty;
        public string ActionType { get; set; } = string.Empty; // e.g., "INSERT", "UPDATE", "DELETE"
        public string TableName { get; set; } = string.Empty;  // e.g., "Devices", "Maps"
        public int RecordId { get; set; } // The ID of the item that was changed
        public string Details { get; set; } = string.Empty; // e.g., "Changed IP from 192.168.1.10 to .11"
    }
}