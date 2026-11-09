namespace NetworkMapViewerV2.Models
{
    /// <summary>One saved device entry that has an IP address: the address, a readable device name and the map it sits on.</summary>
    public record UsedAddressInfo(string Address, string DeviceName, string MapName);
}
