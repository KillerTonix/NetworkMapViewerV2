namespace NetworkMapViewerV2.Models
{
    public class AppSettings
    {
        public string? DatabasePath { get; set; }
        public string? DeviceIconsPath { get; set; }
        public string? HintImagesPath { get; set; }
        public string? ScriptsPath { get; set; }

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

        public string? PrinterPassword { get; set; }
        public string? GrandstreamPassword { get; set; }
        public string? VNCPassword { get; set; }
        public string? SSHPassword { get; set; }
        public string? ManagersPCPassword { get; set; }
        public string? QMSPassword { get; set; }



    }
}