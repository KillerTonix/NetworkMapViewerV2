namespace NetworkMapViewerV2.Models
{
    public class GlobalSettingsModel
    {
        public string? DatabaseServer { get; set; } = "";
        public string? DatabaseName { get; set; } = "";
        public string? DatabaseUser { get; set; } = "";
        public string? DatabasePassword { get; set; } = "";

        public string? DeviceIconsPath { get; set; } = "";
        public string? HintImagesPath { get; set; } = "";
        public string? ScriptsPath { get; set; } = "";

        public string? PrinterPassword { get; set; } = "";
        public string? GrandstreamPassword { get; set; } = "";
        public string? VNCPassword { get; set; } = "";
        public string? SSHPassword { get; set; } = "";
        public string? ManagersPCPassword { get; set; } = "";
        public string? QMSPassword { get; set; } = "";
    }
}
