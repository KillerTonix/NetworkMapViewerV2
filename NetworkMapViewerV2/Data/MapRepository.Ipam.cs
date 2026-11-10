using Microsoft.Data.SqlClient;
using NetworkMapViewerV2.Helpers;
using NetworkMapViewerV2.Models;
using System.Collections.ObjectModel;

namespace NetworkMapViewerV2.Data
{
    // Device create/update used by the IPAM grid.
    public partial class MapRepository
    {
        /// <summary>
        /// Creates (DeviceId = 0) or updates the device behind one IP address, in a single transaction.
        /// Name goes to the titles, the note to a "NOTE:" hint line, and a changed map moves the device to a free
        /// spot on the new map. Every affected map gets its Version bumped so open tabs reload the change.
        /// Returns the saved device id.
        /// </summary>
        public int SaveIpamDevice(IpamDeviceEdit edit)
        {
            string address = (edit.Address ?? "").Trim();
            if (address.Length == 0) throw new InvalidOperationException("The IP address is empty.");

            using var connection = GetOpenConnection();
            using var transaction = connection.BeginTransaction();

            try
            {
                var affectedMaps = new HashSet<int> { edit.MapId };
                NetworkDevice device;

                if (edit.DeviceId == 0)
                {
                    // Someone may have added this IP since the window loaded.
                    using (var dup = new SqlCommand("SELECT COUNT(*) FROM Devices WHERE LTRIM(RTRIM(Address)) = @Address", connection, transaction))
                    {
                        dup.Parameters.AddWithValue("@Address", address);
                        if (Convert.ToInt32(dup.ExecuteScalar()) > 0)
                            throw new InvalidOperationException($"{address} was just added to a map by someone else. Press Show to reload.");
                    }

                    var (left, top) = IpamHelper.FindFreeSlot(GetDevicePositionsInternal(edit.MapId, connection, transaction));
                    device = new NetworkDevice
                    {
                        DeviceId = 0,
                        MapId = edit.MapId,
                        GroupId = edit.GroupId,
                        Left = left,
                        Top = top,
                        Address = address,
                        Titles = new ObservableCollection<string>(IpamHelper.BuildTitles([IpamHelper.AddressPlaceholder], edit.Name)),
                        Hints = new ObservableCollection<string>(IpamHelper.BuildHints([], edit.Note))
                    };
                }
                else
                {
                    device = LoadDeviceInternal(edit.DeviceId, connection, transaction)
                        ?? throw new InvalidOperationException("This device no longer exists. Press Show to reload.");

                    affectedMaps.Add(device.MapId);

                    device.GroupId = edit.GroupId;
                    device.Titles = new ObservableCollection<string>(IpamHelper.BuildTitles(device.Titles, edit.Name));
                    device.Hints = new ObservableCollection<string>(IpamHelper.BuildHints(device.Hints, edit.Note));

                    if (device.MapId != edit.MapId)
                    {
                        string oldMapName = GetMapNameInternal(device.MapId, connection, transaction);
                        string newMapName = GetMapNameInternal(edit.MapId, connection, transaction);

                        using (var move = new SqlCommand("UPDATE Devices SET MapId = @MapId WHERE DeviceId = @DeviceId", connection, transaction))
                        {
                            move.Parameters.AddWithValue("@MapId", edit.MapId);
                            move.Parameters.AddWithValue("@DeviceId", device.DeviceId);
                            move.ExecuteNonQuery();
                        }

                        InsertAuditLogInternal(actionType: "UPDATE", target: "Devices",
                            details: $"Device {device.Address} moved from map '{oldMapName}' to '{newMapName}' (IPAM)",
                            targetId: device.DeviceId, mapName: newMapName, oldValue: null, newValue: null,
                            connection: connection, transaction: transaction);

                        var (left, top) = IpamHelper.FindFreeSlot(GetDevicePositionsInternal(edit.MapId, connection, transaction));
                        device.MapId = edit.MapId;
                        device.Left = left;
                        device.Top = top;
                    }
                }

                UpdateDeviceInternal(device, connection, transaction, GetMapNameInternal(device.MapId, connection, transaction));

                foreach (int mapId in affectedMaps)
                {
                    using var bump = new SqlCommand("UPDATE Maps SET [Version] = ISNULL([Version], 1) + 1 WHERE MapId = @MapId", connection, transaction);
                    bump.Parameters.AddWithValue("@MapId", mapId);
                    bump.ExecuteNonQuery();
                }

                transaction.Commit();
                return device.DeviceId;
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }

        private static string GetMapNameInternal(int mapId, SqlConnection connection, SqlTransaction transaction)
        {
            using var cmd = new SqlCommand("SELECT MapName FROM Maps WHERE MapId = @MapId", connection, transaction);
            cmd.Parameters.AddWithValue("@MapId", mapId);
            object? result = cmd.ExecuteScalar();
            return result == null || result == DBNull.Value ? "" : result.ToString()!;
        }

        private static List<(double Left, double Top)> GetDevicePositionsInternal(int mapId, SqlConnection connection, SqlTransaction transaction)
        {
            var positions = new List<(double, double)>();
            using var cmd = new SqlCommand("SELECT [Left], [Top] FROM Devices WHERE MapId = @MapId", connection, transaction);
            cmd.Parameters.AddWithValue("@MapId", mapId);
            using var reader = cmd.ExecuteReader();
            while (reader.Read()) positions.Add((reader.GetDouble(0), reader.GetDouble(1)));
            return positions;
        }

        private static NetworkDevice? LoadDeviceInternal(int deviceId, SqlConnection connection, SqlTransaction transaction)
        {
            const string sql = @"
                SELECT MapId, GroupId, [Left], [Top], Address, TitleJson, HintsJson, HintImagePath, TargetMapId
                FROM Devices WHERE DeviceId = @DeviceId";

            using var cmd = new SqlCommand(sql, connection, transaction);
            cmd.Parameters.AddWithValue("@DeviceId", deviceId);
            using var reader = cmd.ExecuteReader();
            if (!reader.Read()) return null;

            return new NetworkDevice
            {
                DeviceId = deviceId,
                MapId = reader.GetInt32(0),
                GroupId = reader.GetInt32(1),
                Left = reader.GetDouble(2),
                Top = reader.GetDouble(3),
                Address = reader.IsDBNull(4) ? "" : reader.GetString(4),
                Titles = new ObservableCollection<string>(ParseJsonList(reader.IsDBNull(5) ? null : reader.GetString(5))),
                Hints = new ObservableCollection<string>(ParseJsonList(reader.IsDBNull(6) ? null : reader.GetString(6))),
                HintImagePath = reader.IsDBNull(7) ? "" : reader.GetString(7),
                TargetMapId = reader.IsDBNull(8) ? null : reader.GetInt32(8)
            };
        }
    }
}
