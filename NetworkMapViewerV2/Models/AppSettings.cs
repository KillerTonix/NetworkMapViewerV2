namespace NetworkMapViewerV2.Models
{
    public class AppSettings
    {
        public string? DatabaseServer { get; set; }
        public string? DatabaseName { get; set; }
        public string? DatabaseUser { get; set; }
        public string? DeviceIconsPath { get; set; }
        public string? HintImagesPath { get; set; }
        public string? ScriptsPath { get; set; }

        public int LastOpenedMapId { get; set; } = 0;
        public List<ExternalCommand> Commands { get; set; } = [];
        public Dictionary<int, string> GroupDefaultCommands { get; set; } = [];

        public bool PingAutostart { get; set; } = false;
        public int PingPeriodSeconds { get; set; } = 5;

        public bool DeepperSearchMode { get; set; } = false;
        public bool EqualitySearchMode { get; set; } = false;

        public string? DatabasePassword { get; set; }
        public string? PrinterPassword { get; set; }
        public string? GrandstreamPassword { get; set; }
        public string? VNCPassword { get; set; }
        public string? SSHPassword { get; set; }
        public string? ManagersPCPassword { get; set; }
        public string? QMSPassword { get; set; }


        // Event Notification System (ENS) Settings
        public List<NotificationRule> ENS_Rules { get; set; } = [];
        public bool ENS_SaveToLog { get; set; } = true;
        public bool ENS_ShowMessage { get; set; } = true;

        public bool ENS_PlayOfflineSound { get; set; } = true;
        public string ENS_OfflineSoundFilePath { get; set; } = @"Sounds\Offline.wav";

        public bool ENS_PlayOnlineSound { get; set; } = true;
        public string ENS_OnlineSoundFilePath { get; set; } = @"Sounds\Online.wav";

    }
}