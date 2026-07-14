using Microsoft.Data.SqlClient;
using NetworkMapViewerV2.Models;
using NetworkMapViewerV2.Services;
using System.Collections.ObjectModel;
using System.Text.Json;

namespace NetworkMapViewerV2.Data
{
    public class MapRepository
    {
        private static AppSettings settings = SettingsService.Load();

        private static string DbPath = settings.DatabasePath ?? "";

        public List<MapTabState> GetAvailableMaps()
        {
            var maps = new List<MapTabState>();
            using var connection = GetOpenConnection();
            using var cmd = new SqlCommand("SELECT MapId, MapName, MapType FROM Maps ORDER BY MapName", connection);
            using var reader = cmd.ExecuteReader();

            while (reader.Read())
            {
                maps.Add(new MapTabState
                {
                    MapId = reader.GetInt32(0),
                    MapName = reader.GetString(1),
                    MapType = reader.IsDBNull(2) ? "Head Office" : reader.GetString(2)
                });
            }
            return maps;
        }

        public int CreateNewMap(string mapName, string mapType)
        {
            using var connection = GetOpenConnection();
            // Changed last_insert_rowid() to SCOPE_IDENTITY()
            string sql = "INSERT INTO Maps (MapName, MapType) VALUES (@MapName, @MapType); SELECT SCOPE_IDENTITY();";
            using var cmd = new SqlCommand(sql, connection);

            cmd.Parameters.AddWithValue("@MapName", mapName);
            cmd.Parameters.AddWithValue("@MapType", mapType);

            // Log this critical update action for accountability!
            InsertAuditLog("INSERT", "Maps", 0, $"Created new map: {mapName} : {mapType}");

            return Convert.ToInt32(cmd.ExecuteScalar());
        }

        public MapTabState LoadMap(int mapId)
        {
            var state = new MapTabState { MapId = mapId };

            using var connection = GetOpenConnection();

            // 1. Get Map Name
            using (var cmd = new SqlCommand("SELECT MapName FROM Maps WHERE MapId = @MapId", connection))
            {
                cmd.Parameters.AddWithValue("@MapId", mapId);
                var nameResult = cmd.ExecuteScalar();
                if (nameResult != null) state.MapName = nameResult.ToString() ?? "Unknown Map";
            }

            // 2. Get Devices
            using (var cmd = new SqlCommand("SELECT * FROM Devices WHERE MapId = @MapId", connection))
            {
                cmd.Parameters.AddWithValue("@MapId", mapId);
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    string titlesJson = reader["TitleJson"].ToString() ?? "[]";
                    string hintsJson = reader["HintsJson"].ToString() ?? "[]";

                    state.Devices.Add(new NetworkDevice
                    {
                        DeviceId = Convert.ToInt32(reader["DeviceId"]),
                        MapId = Convert.ToInt32(reader["MapId"]),
                        GroupId = Convert.ToInt32(reader["GroupId"]),
                        Left = Convert.ToDouble(reader["Left"]),
                        Top = Convert.ToDouble(reader["Top"]),
                        Address = reader["Address"].ToString() ?? "",
                        HintImagePath = reader["HintImagePath"].ToString() ?? "",
                        TargetMapId = reader["TargetMapId"] != DBNull.Value ? Convert.ToInt32(reader["TargetMapId"]) : (int?)null,
                        // Deserialize JSON back into C# ObservableCollections
                        Titles = JsonSerializer.Deserialize<ObservableCollection<string>>(titlesJson) ?? new(),
                        Hints = JsonSerializer.Deserialize<ObservableCollection<string>>(hintsJson) ?? new()
                    });
                }
            }

            // 3. Get Labels
            using (var cmd = new SqlCommand("SELECT * FROM Labels WHERE MapId = @MapId", connection))
            {
                cmd.Parameters.AddWithValue("@MapId", mapId);
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    string textJson = reader["TextJson"].ToString() ?? "[]";

                    state.Labels.Add(new NetworkLabel
                    {
                        LabelId = Convert.ToInt32(reader["LabelId"]),
                        MapId = Convert.ToInt32(reader["MapId"]),
                        Left = Convert.ToDouble(reader["Left"]),
                        Top = Convert.ToDouble(reader["Top"]),
                        Width = Convert.ToDouble(reader["Width"]),
                        Height = Convert.ToDouble(reader["Height"]),
                        Background = reader["Background"].ToString() ?? "Transparent",
                        BorderBrush = reader["BorderBrush"].ToString() ?? "Transparent",
                        BorderThickness = Convert.ToInt32(reader["BorderThickness"]),
                        HorizontalAlignment = reader["HorizontalAlignment"].ToString() ?? "Center",
                        VerticalAlignment = reader["VerticalAlignment"].ToString() ?? "Center",
                        FontFamily = reader["FontFamily"].ToString() ?? "Segoe UI",
                        FontSize = Convert.ToDouble(reader["FontSize"]),
                        FontStyle = reader["FontStyle"].ToString() ?? "Normal",
                        FontWeight = reader["FontWeight"].ToString() ?? "Normal",
                        Foreground = reader["Foreground"].ToString() ?? "#000000",

                        // Deserialize JSON Text lines
                        TextLines = JsonSerializer.Deserialize<ObservableCollection<string>>(textJson) ?? []
                    });
                }
            }

            return state;
        }

        // ─── UPSERT (UPDATE OR INSERT) OPERATIONS ───────────────────────

        private void UpdateDeviceInternal(NetworkDevice device, SqlConnection connection, SqlTransaction transaction)
        {
            string titlesJson = JsonSerializer.Serialize(device.Titles);
            string hintsJson = JsonSerializer.Serialize(device.Hints);

            if (device.DeviceId == 0) // INSERT
            {
                // Wrapped Left and Top in brackets
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

                device.DeviceId = Convert.ToInt32(cmd.ExecuteScalar());
                InsertAuditLogInternal("INSERT", "Devices", device.DeviceId, $"Added new device on map", connection, transaction);
            }
            else // UPDATE
            {
                bool hasChanges = false;
                List<string> changedFields = new();

                // Wrapped Left and Top in brackets
                string checkSql = "SELECT GroupId, [Left], [Top], Address, TitleJson, HintsJson, HintImagePath, TargetMapId FROM Devices WHERE DeviceId = @DeviceId";
                using (var checkCmd = new SqlCommand(checkSql, connection, transaction))
                {
                    checkCmd.Parameters.AddWithValue("@DeviceId", device.DeviceId);
                    using var reader = checkCmd.ExecuteReader();

                    if (reader.Read())
                    {
                        int dbGroupId = reader.GetInt32(0);
                        double dbLeft = reader.GetDouble(1);
                        double dbTop = reader.GetDouble(2);
                        string dbAddress = reader.IsDBNull(3) ? "" : reader.GetString(3);
                        string dbTitleJson = reader.IsDBNull(4) ? "" : reader.GetString(4);
                        string dbHintsJson = reader.IsDBNull(5) ? "" : reader.GetString(5);
                        string dbImagePath = reader.IsDBNull(6) ? "" : reader.GetString(6);
                        int? dbTargetMapId = reader.IsDBNull(7) ? null : reader.GetInt32(7);

                        if (dbGroupId != (device.GroupId <= 0 ? 1 : device.GroupId)) { hasChanges = true; changedFields.Add("Type"); }
                        if (Math.Abs(dbLeft - device.Left) > 0.01 || Math.Abs(dbTop - device.Top) > 0.01) { hasChanges = true; changedFields.Add("Position"); }
                        if (dbAddress != (device.Address ?? "")) { hasChanges = true; changedFields.Add("IP"); }
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

                // Wrapped Left and Top in brackets
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

                InsertAuditLogInternal("UPDATE", "Devices", device.DeviceId, $"Device IP: {device.Address ?? ""}; Changed: {string.Join(", ", changedFields)}", connection, transaction);
            }
        }

        private void UpdateLabelInternal(NetworkLabel label, SqlConnection connection, SqlTransaction transaction)
        {
            string textJson = JsonSerializer.Serialize(label.TextLines);

            if (label.LabelId == 0) // INSERT
            {
                // Wrapped Left and Top in brackets
                string sql = @"
            INSERT INTO Labels (MapId, [Left], [Top], Width, Height, Background, BorderBrush, BorderThickness, HorizontalAlignment, VerticalAlignment, FontFamily, FontSize, FontStyle, FontWeight, Foreground, TextJson) 
            VALUES (@MapId, @Left, @Top, @Width, @Height, @Background, @BorderBrush, @BorderThickness, @HorizontalAlignment, @VerticalAlignment, @FontFamily, @FontSize, @FontStyle, @FontWeight, @Foreground, @TextJson);
            SELECT SCOPE_IDENTITY();";

                using var cmd = new SqlCommand(sql, connection, transaction);
                AddLabelParameters(cmd, label, textJson);
                label.LabelId = Convert.ToInt32(cmd.ExecuteScalar());
            }
            else // UPDATE
            {
                bool hasChanges = false;
                List<string> changedFields = new();

                // Wrapped Left and Top in brackets
                string checkSql = "SELECT [Left], [Top], Width, Height, TextJson FROM Labels WHERE LabelId = @LabelId";
                using (var checkCmd = new SqlCommand(checkSql, connection, transaction))
                {
                    checkCmd.Parameters.AddWithValue("@LabelId", label.LabelId);
                    using var reader = checkCmd.ExecuteReader();

                    if (reader.Read())
                    {
                        double dbLeft = reader.GetDouble(0);
                        double dbTop = reader.GetDouble(1);
                        double dbWidth = reader.GetDouble(2);
                        double dbHeight = reader.GetDouble(3);
                        string dbTextJson = reader.IsDBNull(4) ? "" : reader.GetString(4);

                        if (Math.Abs(dbLeft - label.Left) > 0.01 || Math.Abs(dbTop - label.Top) > 0.01) { hasChanges = true; changedFields.Add("Position"); }
                        if (Math.Abs(dbWidth - label.Width) > 0.01 || Math.Abs(dbHeight - label.Height) > 0.01) { hasChanges = true; changedFields.Add("Size"); }
                        if (dbTextJson != textJson) { hasChanges = true; changedFields.Add("Text"); }
                    }
                    else
                    {
                        hasChanges = true;
                        changedFields.Add("Format");
                    }
                }

                if (!hasChanges) return;

                // Wrapped Left and Top in brackets
                string sql = @"
            UPDATE Labels SET 
                [Left] = @Left, [Top] = @Top, Width = @Width, Height = @Height, 
                Background = @Background, BorderBrush = @BorderBrush, BorderThickness = @BorderThickness, 
                HorizontalAlignment = @HorizontalAlignment, VerticalAlignment = @VerticalAlignment, 
                FontFamily = @FontFamily, FontSize = @FontSize, FontStyle = @FontStyle, FontWeight = @FontWeight,
                Foreground = @Foreground, TextJson = @TextJson
            WHERE LabelId = @LabelId";

                using var cmd = new SqlCommand(sql, connection, transaction);
                cmd.Parameters.AddWithValue("@LabelId", label.LabelId);
                AddLabelParameters(cmd, label, textJson);
                cmd.ExecuteNonQuery();

                InsertAuditLogInternal("UPDATE", "Labels", label.LabelId, $"Changed: {string.Join(", ", changedFields)}", connection, transaction);
            }
        }

        private void AddLabelParameters(SqlCommand cmd, NetworkLabel label, string textJson)
        {
            cmd.Parameters.AddWithValue("@MapId", label.MapId);
            cmd.Parameters.AddWithValue("@Left", label.Left);
            cmd.Parameters.AddWithValue("@Top", label.Top);
            cmd.Parameters.AddWithValue("@Width", label.Width);
            cmd.Parameters.AddWithValue("@Height", label.Height);
            cmd.Parameters.AddWithValue("@Background", label.Background ?? "Transparent");
            cmd.Parameters.AddWithValue("@BorderBrush", label.BorderBrush ?? "Transparent");
            cmd.Parameters.AddWithValue("@BorderThickness", label.BorderThickness);
            cmd.Parameters.AddWithValue("@HorizontalAlignment", label.HorizontalAlignment ?? "Center");
            cmd.Parameters.AddWithValue("@VerticalAlignment", label.VerticalAlignment ?? "Center");
            cmd.Parameters.AddWithValue("@FontFamily", label.FontFamily ?? "Segoe UI");
            cmd.Parameters.AddWithValue("@FontSize", label.FontSize);
            cmd.Parameters.AddWithValue("@FontStyle", label.FontStyle ?? "Normal");
            cmd.Parameters.AddWithValue("@FontWeight", label.FontWeight ?? "Normal");
            cmd.Parameters.AddWithValue("@Foreground", label.Foreground ?? "#000000");
            cmd.Parameters.AddWithValue("@TextJson", textJson);
        }

        // ─── DELETE OPERATIONS ──────────────────────────────────────────

        public void DeleteDevice(int deviceId)
        {
            if (deviceId <= 0) return;
            using var connection = GetOpenConnection();
            using var cmd = new SqlCommand("DELETE FROM Devices WHERE DeviceId = @DeviceId", connection);
            cmd.Parameters.AddWithValue("@DeviceId", deviceId);
            cmd.ExecuteNonQuery();

            InsertAuditLog("DELETE", "Devices", deviceId, "Device removed from map.");
        }

        public void DeleteLabel(int labelId)
        {
            if (labelId <= 0) return;
            using var connection = GetOpenConnection();
            using var cmd = new SqlCommand("DELETE FROM Labels WHERE LabelId = @LabelId", connection);
            cmd.Parameters.AddWithValue("@LabelId", labelId);
            cmd.ExecuteNonQuery();

            InsertAuditLog("DELETE", "Labels", labelId, "Label removed from map.");
        }

        // ─── SEARCH OPERATIONS ──────────────────────────────────────────

        public List<GlobalSearchResult> SearchDevices(string query, bool deepSearch)
        {
            var results = new List<GlobalSearchResult>();
            if (string.IsNullOrWhiteSpace(query)) return results;

            using var connection = GetOpenConnection();

            // MS SQL doesn't use INSTR. We use standard LIKE clauses instead.
            string sql = deepSearch
                ? @"SELECT MapId, DeviceId FROM Devices 
                    WHERE TitleJson LIKE '%' + @Query + '%' 
                       OR Address LIKE '%' + @Query + '%' 
                       OR HintsJson LIKE '%' + @Query + '%'"
                : @"SELECT MapId, DeviceId FROM Devices 
                    WHERE TitleJson LIKE '%' + @Query + '%'";

            using var cmd = new SqlCommand(sql, connection);
            cmd.Parameters.AddWithValue("@Query", query); // MS SQL LIKE is case-insensitive by default!

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

        public List<Views.DeviceTypeItem> GetDeviceGroups()
        {
            var groups = new List<Views.DeviceTypeItem>();
            using var connection = GetOpenConnection();

            using var cmd = new SqlCommand("SELECT GroupId, GroupName FROM Groups ORDER BY GroupName", connection);
            using var reader = cmd.ExecuteReader();

            while (reader.Read())
            {
                groups.Add(new Views.DeviceTypeItem
                {
                    GroupId = reader.GetInt32(0),
                    DisplayName = reader.GetString(1)
                });
            }
            return groups;
        }

        private SqlConnection GetOpenConnection()
        {
            var connection = new SqlConnection(DatabaseService.ConnectionString);
            connection.Open();
            // PRAGMA foreign_keys = ON; removed. SQL Server natively enforces Foreign Keys!
            return connection;
        }

        public void SaveDeviceGroup(DeviceGroup group)
        {
            using var connection = GetOpenConnection();

            if (group.GroupId == 0) // BRAND NEW GROUP
            {
                string sql = @"
                    INSERT INTO Groups (GroupName, IconPath, DefaultCommand, IsMapLink) 
                    VALUES (@GroupName, @IconPath, @DefaultCommand, @IsMapLink);
                    SELECT SCOPE_IDENTITY();";

                using var cmd = new SqlCommand(sql, connection);
                AddGroupParameters(cmd, group);
                group.GroupId = Convert.ToInt32(cmd.ExecuteScalar());
            }
            else // EXISTING GROUP
            {
                string sql = @"
                    UPDATE Groups SET 
                        GroupName = @GroupName, IconPath = @IconPath, 
                        IsMapLink = @IsMapLink
                    WHERE GroupId = @GroupId";

                using var cmd = new SqlCommand(sql, connection);
                cmd.Parameters.AddWithValue("@GroupId", group.GroupId);
                cmd.Parameters.AddWithValue("@GroupName", group.GroupName ?? "New Group");
                cmd.Parameters.AddWithValue("@IconPath", group.IconPath ?? "");
                cmd.Parameters.AddWithValue("@IsMapLink", group.IsMapLink ? 1 : 0);
                cmd.ExecuteNonQuery();

                InsertAuditLogInternal("UPDATE", "Groups", group.GroupId, $"Updated group name: {group.GroupName}", connection, null);
            }

            SettingsService.UpdateGroupDefaultCommand(group.GroupId, group.DefaultCommand ?? "Ping");
        }

        private void AddGroupParameters(SqlCommand cmd, DeviceGroup group)
        {
            cmd.Parameters.AddWithValue("@GroupName", group.GroupName ?? "New Group");
            cmd.Parameters.AddWithValue("@IconPath", group.IconPath ?? "");
            cmd.Parameters.AddWithValue("@DefaultCommand", group.DefaultCommand ?? "Ping");
            cmd.Parameters.AddWithValue("@IsMapLink", group.IsMapLink ? 1 : 0);
        }

        public List<DeviceGroup> GetAllDeviceGroups()
        {
            if (DbPath == null || DbPath == "")
                return [];

            var groups = new List<DeviceGroup>();
            using var connection = GetOpenConnection();
            using var cmd = new SqlCommand("SELECT GroupId, GroupName, IconPath, DefaultCommand, IsMapLink FROM Groups ORDER BY GroupName", connection);
            using var reader = cmd.ExecuteReader();

            var localSettings = SettingsService.Load();
            while (reader.Read())
            {
                int groupId = reader.GetInt32(0);
                string dbCommand = reader.IsDBNull(3) ? "Ping" : reader.GetString(3);
                string finalCommand = dbCommand;
                if (localSettings.GroupDefaultCommands != null && localSettings.GroupDefaultCommands.ContainsKey(groupId))
                {
                    finalCommand = localSettings.GroupDefaultCommands[groupId];
                }

                groups.Add(new DeviceGroup
                {
                    GroupId = groupId,
                    GroupName = reader.GetString(1),
                    IconPath = reader.IsDBNull(2) ? "" : reader.GetString(2),
                    DefaultCommand = finalCommand,

                    // Changed GetInt32(4) == 1 to GetBoolean(4)
                    IsMapLink = !reader.IsDBNull(4) && reader.GetBoolean(4)
                });
            }
            return groups;
        }

        public void DeleteDeviceGroup(int groupId)
        {
            using var connection = GetOpenConnection();
            using var cmd = new SqlCommand("DELETE FROM Groups WHERE GroupId = @GroupId", connection);
            cmd.Parameters.AddWithValue("@GroupId", groupId);
            cmd.ExecuteNonQuery();

            InsertAuditLog("DELETE", "Groups", groupId, "Device group removed.");
        }

        // --- 1. WRITE TO LOG ---
        public void InsertAuditLog(string actionType, string tableName, int recordId, string details = "")
        {
            using var connection = GetOpenConnection();

            string sql = @"
                INSERT INTO AuditLogs (Timestamp, Username, ActionType, TableName, RecordId, Details) 
                VALUES (@Timestamp, @Username, @ActionType, @TableName, @RecordId, @Details)";

            using var cmd = new SqlCommand(sql, connection);

            // Replaced string conversion with direct DateTime parsing (standard for MS SQL)
            cmd.Parameters.AddWithValue("@Timestamp", DateTime.Now);
            cmd.Parameters.AddWithValue("@Username", Environment.UserName);
            cmd.Parameters.AddWithValue("@ActionType", actionType);
            cmd.Parameters.AddWithValue("@TableName", tableName);
            cmd.Parameters.AddWithValue("@RecordId", recordId);
            cmd.Parameters.AddWithValue("@Details", details);

            cmd.ExecuteNonQuery();
        }

        private void InsertAuditLogInternal(string actionType, string tableName, int recordId, string details, SqlConnection connection, SqlTransaction transaction)
        {
            string sql = @"
        INSERT INTO AuditLogs (Timestamp, Username, ActionType, TableName, RecordId, Details) 
        VALUES (@Timestamp, @Username, @ActionType, @TableName, @RecordId, @Details)";

            using var cmd = new SqlCommand(sql, connection, transaction);
            cmd.Parameters.AddWithValue("@Timestamp", DateTime.Now);
            cmd.Parameters.AddWithValue("@Username", Environment.UserName);
            cmd.Parameters.AddWithValue("@ActionType", actionType);
            cmd.Parameters.AddWithValue("@TableName", tableName);
            cmd.Parameters.AddWithValue("@RecordId", recordId);
            cmd.Parameters.AddWithValue("@Details", details);

            cmd.ExecuteNonQuery();
        }

        // --- 2. READ THE LOGS ---
        public List<AuditLog> GetAuditLogs()
        {
            var logs = new List<AuditLog>();
            using var connection = GetOpenConnection();

            string sql = "SELECT * FROM AuditLogs ORDER BY LogId DESC";

            using var cmd = new SqlCommand(sql, connection);
            using var reader = cmd.ExecuteReader();

            while (reader.Read())
            {
                logs.Add(new AuditLog
                {
                    LogId = reader.GetInt32(0),
                    // Converted to safely read native DATETIME back to a string
                    Timestamp = Convert.ToDateTime(reader["Timestamp"]).ToString("yyyy-MM-dd HH:mm:ss"),
                    Username = reader.GetString(2),
                    ActionType = reader.GetString(3),
                    TableName = reader.GetString(4),
                    RecordId = reader.GetInt32(5),
                    Details = reader.IsDBNull(6) ? "" : reader.GetString(6)
                });
            }
            return logs;
        }

        // --- The Public Methods your ViewModel will call ---

        public void SaveMapBatch(IEnumerable<NetworkDevice> devices, IEnumerable<NetworkLabel> labels)
        {
            using var connection = GetOpenConnection();
            using var transaction = connection.BeginTransaction();

            try
            {
                foreach (var device in devices)
                {
                    UpdateDeviceInternal(device, connection, transaction);
                }

                foreach (var label in labels)
                {
                    UpdateLabelInternal(label, connection, transaction);
                }

                transaction.Commit();
            }
            catch
            {
                transaction.Rollback();
                throw;
            }
        }

        public void UpdateDevice(NetworkDevice device)
        {
            using var connection = GetOpenConnection();
            using var transaction = connection.BeginTransaction();
            try
            {
                UpdateDeviceInternal(device, connection, transaction);
                transaction.Commit();
            }
            catch { transaction.Rollback(); throw; }
        }

        public void UpdateLabel(NetworkLabel label)
        {
            using var connection = GetOpenConnection();
            using var transaction = connection.BeginTransaction();
            try
            {
                UpdateLabelInternal(label, connection, transaction);
                transaction.Commit();
            }
            catch { transaction.Rollback(); throw; }
        }

        public List<NetworkDevice> GetAllDevices()
        {
            var allDevices = new List<NetworkDevice>();

            using var connection = GetOpenConnection();
            string sql = "SELECT * FROM Devices";

            using var cmd = new SqlCommand(sql, connection);
            using var reader = cmd.ExecuteReader();

            while (reader.Read())
            {
                allDevices.Add(new NetworkDevice
                {
                    GroupId = Convert.ToInt32(reader["GroupId"]),
                    DeviceId = Convert.ToInt32(reader["DeviceId"]),
                    MapId = Convert.ToInt32(reader["MapId"]),
                    Address = reader["Address"].ToString() ?? "",
                    Titles = JsonSerializer.Deserialize<ObservableCollection<string>>(reader["TitleJson"].ToString() ?? "[]") ?? []
                });
            }

            return allDevices;
        }
    }
}