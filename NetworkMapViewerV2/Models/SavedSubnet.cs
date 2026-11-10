namespace NetworkMapViewerV2.Models
{
    /// <summary>A named CIDR range kept in the IPAM sidebar, e.g. "Montreal Branch" = 192.168.102.0/24.</summary>
    public class SavedSubnet
    {
        public int SubnetId { get; set; }
        public string Name { get; set; } = "";

        /// <summary>Normalised CIDR (host bits cleared), e.g. 192.168.28.0/22.</summary>
        public string Cidr { get; set; } = "";
    }
}
