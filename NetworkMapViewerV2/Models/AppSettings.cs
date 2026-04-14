using System.Collections.Generic;

namespace NetworkMapViewerV2.Models
{
    public class AppSettings
    {
        public int LastOpenedMapId { get; set; } = 0;
        public List<ExternalCommand> Commands { get; set; } = [];
        public Dictionary<int, string> DefaultDoubleClickCommands { get; set; } = [];


        public bool PingAutostart { get; set; } = false;
        public int PingPeriodSeconds { get; set; } = 5;
        public int DeleteEventsOlderThanDays { get; set; } = 30;
        public int HideMessageSeconds { get; set; } = 5;
        public string NotificationHeaderTemplate { get; set; } = "You asked to be notified when...";
        public string NotificationUpTemplate { get; set; } = "[Address] is up at %Time";
        public string NotificationDownTemplate { get; set; } = "[Address] is down at %Time";

        public bool DeepperSearchMode { get; set; } = false;
    }
}