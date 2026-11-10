namespace NetworkMapViewerV2.Models
{
    /// <summary>
    /// What the IPAM grid wants saved for one address. DeviceId = 0 creates a new device on <see cref="MapId"/>;
    /// otherwise the existing device is updated (and moved if the map changed).
    /// </summary>
    public record IpamDeviceEdit(int DeviceId, string Address, string Name, int GroupId, int MapId, string Note);
}
