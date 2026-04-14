using System;
using System.Collections.Generic;
using System.Text;

namespace NetworkMapViewerV2.Models
{
    public class MapTabState
    {
        public int MapId { get; set; }
        public string FilePath { get; set; } = string.Empty;    
        public string MapName { get; set; } = string.Empty;
        public List<NetworkDevice> Devices { get; set; } = [];
        public List<NetworkLabel> Labels { get; set; } = [];
    }
}
