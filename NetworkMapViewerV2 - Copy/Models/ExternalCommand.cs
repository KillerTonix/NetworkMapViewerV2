namespace NetworkMapViewerV2.Models
{
    public class ExternalCommand
    {
        public string Name { get; set; } = string.Empty;
        public string Path { get; set; } = string.Empty;
        public string Arguments { get; set; } = string.Empty;
        public string Icon { get; set; } = "⚙️";
    }
}