using Microsoft.Data.SqlClient;
using NetworkMapViewerV2.Models;
using System.Collections.ObjectModel;
using System.Text.Json;
using System.Windows;

namespace NetworkMapViewerV2.Data
{
    public partial class MapRepository
    {
        public void UpdateDevice(NetworkDevice device, string mapName)
        {
            using var connection = GetOpenConnection();
            using var transaction = connection.BeginTransaction();
            try
            {
                UpdateDeviceInternal(device, connection, transaction, mapName);
                transaction.Commit();
            }
            catch { transaction.Rollback(); throw; }
        }

        private void UpdateDeviceInternal(NetworkDevice device, SqlConnection connection, SqlTransaction transaction, string mapName)
        {
            string titlesJson = JsonSerializer.Serialize(device.Titles);
            string hintsJson = JsonSerializer.Serialize(device.Hints);
            string targetType = GetGroupNameInternal(device.GroupId, connection, transaction);

            string deviceName = device.Titles?.FirstOrDefault() ?? "";
            string deviceLabel = !string.IsNullOrWhiteSpace(deviceName) ? $"{deviceName} ({device.Address})" : device.Address ?? "Unknown IP";

            if (device.DeviceId == 0) // INSERT
            {
                string sql = @"
            INSERT INTO Devices (MapId, GroupId, [Left], [Top], Address, TitleJson, HintsJson, HintImagePath, TargetMapId) 
            VALUES (@MapId, @GroupId, @Left, @Top, @Address, @TitleJson, @HintsJson, @HintImagePath, @TargetMapId);
            SELECT SCOPE_IDENTITY();";

                using var cmd = new SqlCommand(sql, connection, transaction);
                cmd.Parameters.AddWithValue("@MapId", device.MapId);
                cmd.Parameters.AddWithValue("@GroupId", device.GroupId <= 0 ? 1 : device.GroupId);
                cmd.Parameters.AddWithValue("@Left", device.Left);
                cmd.Parameters.AddWithValue("@Top", device.Top);
                cmd.Parameters.AddWithValue("@Address", device.Address ?? "");
                cmd.Parameters.AddWithValue("@TitleJson", titlesJson);
                cmd.Parameters.AddWithValue("@HintsJson", hintsJson);
                cmd.Parameters.AddWithValue("@HintImagePath", device.HintImagePath ?? "");
                cmd.Parameters.AddWithValue("@TargetMapId", device.TargetMapId.HasValue ? (object)device.TargetMapId.Value : DBNull.Value);

                // 1. MUST FETCH NEW ID FIRST!
                device.DeviceId = Convert.ToInt32(cmd.ExecuteScalar());

                // 2. PASS device.DeviceId AS targetId
                InsertAuditLogInternal(actionType: "INSERT", target: targetType, details: $"Added new {targetType}: {deviceLabel}", targetId: device.DeviceId,
                    mapName: mapName, oldValue: null, newValue: null, connection: connection, transaction: transaction);
            }
            else // UPDATE
            {
                bool hasChanges = false;
                List<string> changedFields = new();

                int dbGroupId = 0;
                double dbLeft = 0, dbTop = 0;
                string dbAddress = "", dbTitleJson = "[]", dbHintsJson = "[]", dbImagePath = "";
                int? dbTargetMapId = null;

                string checkSql = "SELECT GroupId, [Left], [Top], Address, TitleJson, HintsJson, HintImagePath, TargetMapId FROM Devices WHERE DeviceId = @DeviceId";
                using (var checkCmd = new SqlCommand(checkSql, connection, transaction))
                {
                    checkCmd.Parameters.AddWithValue("@DeviceId", device.DeviceId);
                    using var reader = checkCmd.ExecuteReader();

                    if (reader.Read())
                    {
                        dbGroupId = reader.GetInt32(0);
                        dbLeft = reader.GetDouble(1);
                        dbTop = reader.GetDouble(2);
                        dbAddress = reader.IsDBNull(3) ? "" : reader.GetString(3);
                        dbTitleJson = reader.IsDBNull(4) ? "[]" : reader.GetString(4);
                        dbHintsJson = reader.IsDBNull(5) ? "[]" : reader.GetString(5);
                        dbImagePath = reader.IsDBNull(6) ? "" : reader.GetString(6);
                        dbTargetMapId = reader.IsDBNull(7) ? null : reader.GetInt32(7);

                        if (dbGroupId != (device.GroupId <= 0 ? 1 : device.GroupId)) { hasChanges = true; changedFields.Add("Type"); }
                        if (Math.Abs(dbLeft - device.Left) > 0.01 || Math.Abs(dbTop - device.Top) > 0.01) { hasChanges = true; changedFields.Add("Position"); }
                        if (dbAddress != (device.Address ?? "")) { hasChanges = true; changedFields.Add($"IP: {dbAddress} -> {device.Address}"); }
                        if (dbTitleJson != titlesJson) { hasChanges = true; changedFields.Add("Titles"); }
                        if (dbHintsJson != hintsJson) { hasChanges = true; changedFields.Add("Hints"); }
                        if (dbImagePath != (device.HintImagePath ?? "")) { hasChanges = true; changedFields.Add("Image"); }
                        if (dbTargetMapId != device.TargetMapId) { hasChanges = true; changedFields.Add("Map Link"); }
                    }
                    else
                    {
                        hasChanges = true;
                        changedFields.Add("Forced Update");
                    }
                }

                if (!hasChanges) return;

                string sql = @"
            UPDATE Devices SET GroupId=@GroupId, [Left]=@Left, [Top]=@Top, Address=@Address, 
            TitleJson=@TitleJson, HintsJson=@HintsJson, HintImagePath=@HintImagePath, TargetMapId=@TargetMapId 
            WHERE DeviceId=@DeviceId;";

                using var cmd = new SqlCommand(sql, connection, transaction);
                cmd.Parameters.AddWithValue("@DeviceId", device.DeviceId);
                cmd.Parameters.AddWithValue("@GroupId", device.GroupId <= 0 ? 1 : device.GroupId);
                cmd.Parameters.AddWithValue("@Left", device.Left);
                cmd.Parameters.AddWithValue("@Top", device.Top);
                cmd.Parameters.AddWithValue("@Address", device.Address ?? "");
                cmd.Parameters.AddWithValue("@TitleJson", titlesJson);
                cmd.Parameters.AddWithValue("@HintsJson", hintsJson);
                cmd.Parameters.AddWithValue("@HintImagePath", device.HintImagePath ?? "");
                cmd.Parameters.AddWithValue("@TargetMapId", device.TargetMapId.HasValue ? (object)device.TargetMapId.Value : DBNull.Value);

                cmd.ExecuteNonQuery();

                var jsonOptions = new JsonSerializerOptions { WriteIndented = true };

                var oldSnapshot = new
                {
                    Device = deviceLabel,
                    Type = GetGroupNameInternal(dbGroupId, connection, transaction),
                    Address = dbAddress,
                    Position = new { Left = Math.Round(dbLeft, 1), Top = Math.Round(dbTop, 1) },
                    Titles = JsonSerializer.Deserialize<List<string>>(dbTitleJson) ?? [],
                    Hints = JsonSerializer.Deserialize<List<string>>(dbHintsJson) ?? [],
                    HintImagePath = dbImagePath
                };

                var newSnapshot = new
                {
                    Device = deviceLabel,
                    Type = targetType,
                    Address = device.Address ?? "",
                    Position = new { Left = Math.Round(device.Left, 1), Top = Math.Round(device.Top, 1) },
                    Titles = device.Titles?.ToList() ?? [],
                    Hints = device.Hints?.ToList() ?? [],
                    HintImagePath = device.HintImagePath ?? ""
                };

                string oldValueJson = JsonSerializer.Serialize(oldSnapshot, jsonOptions);
                string newValueJson = JsonSerializer.Serialize(newSnapshot, jsonOptions);
                string details = $"Device: {deviceLabel} | Changed: {string.Join(", ", changedFields)}";

                // PASS device.DeviceId AS targetId USING NAMED PARAMETERS
                InsertAuditLogInternal(actionType: "UPDATE", target: targetType, details: details, targetId: device.DeviceId,
                    mapName: mapName, oldValue: oldValueJson, newValue: newValueJson, connection: connection, transaction: transaction);
            }
        }

        public bool DeleteDevice(int deviceId, string mapName)
        {
            try
            {
                if (deviceId <= 0) return true;
                using var connection = GetOpenConnection();
                using var cmd = new SqlCommand("DELETE FROM Devices WHERE DeviceId = @DeviceId", connection);
                cmd.Parameters.AddWithValue("@DeviceId", deviceId);
                cmd.ExecuteNonQuery();

                InsertAuditLogInternal(actionType: "DELETE", target: "Devices", details: $"Device removed from {mapName}", targetId: deviceId, mapName: mapName, oldValue: null, newValue: null, connection: connection, transaction: null);
                return true;
            }
            catch (Exception ex)
            {
                if (ex.Message.Contains("permission was denied"))
                {
                    MessageBox.Show("Failed to delete device:\nYou don't have permission to modify the database.", "Database Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                else
                {
                    MessageBox.Show($"Failed to delete device:\n{ex.Message}", "Database Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                return false;
            }
        }

        public List<GlobalSearchResult> SearchDevices(string query, bool deepSearch, bool equalitySearch)
        {
            var results = new List<GlobalSearchResult>();
            if (string.IsNullOrWhiteSpace(query)) return results;

            using var connection = GetOpenConnection();

            string sql = deepSearch
                ? @"SELECT MapId, DeviceId FROM Devices 
                    WHERE TitleJson LIKE '%' + @Query + '%' 
                       OR Address LIKE '%' + @Query + '%' 
                       OR HintsJson LIKE '%' + @Query + '%'"
                : @"SELECT MapId, DeviceId FROM Devices 
                    WHERE TitleJson LIKE '%' + @Query + '%'";

            if (equalitySearch)
            {
                sql = deepSearch
                    ? @"SELECT MapId, DeviceId FROM Devices 
                        WHERE (JSON_VALUE(TitleJson, '$[2]') = @Query OR JSON_VALUE(TitleJson, '$[2]') LIKE @Query + ' %')
                           OR Address = @Query
                           OR ' ' + HintsJson + ' ' LIKE '%[^a-zA-Z0-9]' + @Query + '[^a-zA-Z0-9]%'"
                    : @"SELECT MapId, DeviceId FROM Devices 
                        WHERE (JSON_VALUE(TitleJson, '$[2]') = @Query OR JSON_VALUE(TitleJson, '$[2]') LIKE @Query + ' %')";
            }

            using var cmd = new SqlCommand(sql, connection);
            cmd.Parameters.AddWithValue("@Query", query);

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                results.Add(new GlobalSearchResult
                {
                    MapId = reader.GetInt32(0),
                    DeviceId = reader.GetInt32(1)
                });
            }
            return results;
        }

        public static List<NetworkDevice> GetAllDevices()
        {
            if (string.IsNullOrEmpty(DbPath))
                return [];

            var devices = new List<NetworkDevice>();
            using var connection = GetOpenConnection();
            using var cmd = new SqlCommand("SELECT DeviceId, Address, GroupId, TitleJson FROM Devices", connection);
            using var reader = cmd.ExecuteReader();

            while (reader.Read())
            {
                devices.Add(new NetworkDevice
                {
                    DeviceId = reader.GetInt32(0),
                    Address = reader.GetString(1),
                    GroupId = reader.GetInt32(2),
                    Titles = reader.GetString(3) != null ? JsonSerializer.Deserialize<ObservableCollection<string>>(reader.GetString(3)) ?? [] : []
                });
            }

            return devices;
        }
    }
}