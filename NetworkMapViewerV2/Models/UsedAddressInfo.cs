namespace NetworkMapViewerV2.Models
{
    /// <summary>
    /// One saved device entry that has an IP address: the address, a readable device name and the map it sits on,
    /// plus the ids and type/note IPAM needs to edit that device.
    /// </summary>
    public record UsedAddressInfo(
        string Address,
        string DeviceName,
        string MapName,
        int DeviceId = 0,
        int MapId = 0,
        int GroupId = 0,
        string DeviceType = "",
        string Note = "");
}
