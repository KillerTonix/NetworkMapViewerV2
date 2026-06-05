using Microsoft.Data.Sqlite;
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
        public Dictionary<int, string> GetAvailableMaps()
        {
            var maps = new Dictionary<int, string>();
            using var connection = GetOpenConnection();
            using var cmd = new SqliteCommand("SELECT MapId, MapName FROM Maps ORDER BY MapName", connection);
            using var reader = cmd.ExecuteReader();
            
            while (reader.Read())
            {
                maps.Add(reader.GetInt32(0), reader.GetString(1));
            }
            return maps;
        }

        public int CreateNewMap(string mapName)
        {
            using var connection = GetOpenConnection();
            string sql = "INSERT INTO Maps (MapName) VALUES (@MapName); SELECT last_insert_rowid();";
            using var cmd = new SqliteCommand(sql, connection);

            cmd.Parameters.AddWithValue("@MapName", mapName);

            // Log this critical update action for accountability!
            InsertAuditLog("INSERT", "Maps", 0, $"Created new map: {mapName}");

            return Convert.ToInt32(cmd.ExecuteScalar());


        }

        public MapTabState LoadMap(int mapId)
        {
            var state = new MapTabState { MapId = mapId };

            using var connection = GetOpenConnection();

            // 1. Get Map Name
            using (var cmd = new SqliteCommand("SELECT MapName FROM Maps WHERE MapId = @MapId", connection))
            {
                cmd.Parameters.AddWithValue("@MapId", mapId);
                var nameResult = cmd.ExecuteScalar();
                if (nameResult != null) state.MapName = nameResult.ToString() ?? "Unknown Map";
            }

            // 2. Get Devices
            using (var cmd = new SqliteCommand("SELECT * FROM Devices WHERE MapId = @MapId", connection))
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
            using (var cmd = new SqliteCommand("SELECT * FROM Labels WHERE MapId = @MapId", connection))
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

        public void UpdateDevice(NetworkDevice device)
        {
            using var connection = GetOpenConnection();

            // Serialize lists to JSON text
            string titlesJson = JsonSerializer.Serialize(device.Titles);
            string hintsJson = JsonSerializer.Serialize(device.Hints);

            if (device.DeviceId == 0) // New Device (INSERT)
            {
                string sql = @"
                    INSERT INTO Devices (MapId, GroupId, Left, Top, Address, TitleJson, HintsJson, HintImagePath, TargetMapId) 
                    VALUES (@MapId, @GroupId, @Left, @Top, @Address, @TitleJson, @HintsJson, @HintImagePath, @TargetMapId);
                    SELECT last_insert_rowid();";

                using var cmd = new SqliteCommand(sql, connection);
                cmd.Parameters.AddWithValue("@MapId", device.MapId);
                cmd.Parameters.AddWithValue("@GroupId", device.GroupId <= 0 ? 1 : device.GroupId); // Fallback to group 1
                cmd.Parameters.AddWithValue("@Left", device.Left);
                cmd.Parameters.AddWithValue("@Top", device.Top);
                cmd.Parameters.AddWithValue("@Address", device.Address ?? "");
                cmd.Parameters.AddWithValue("@TitleJson", titlesJson);
                cmd.Parameters.AddWithValue("@HintsJson", hintsJson);
                cmd.Parameters.AddWithValue("@HintImagePath", device.HintImagePath ?? "");
                cmd.Parameters.AddWithValue("@TargetMapId", device.TargetMapId.HasValue ? (object)device.TargetMapId.Value : DBNull.Value);

                device.DeviceId = Convert.ToInt32(cmd.ExecuteScalar());
            }
            else // Existing Device (UPDATE)
            {
                // ==========================================
                // 1. CHANGE DETECTION (Avoid unnecessary saves)
                // ==========================================
                bool hasChanges = false;
                List<string> changedFields = new();

                string checkSql = "SELECT GroupId, Left, Top, Address, TitleJson, HintsJson, HintImagePath, TargetMapId FROM Devices WHERE DeviceId = @DeviceId";
                using (var checkCmd = new SqliteCommand(checkSql, connection))
                {
                    checkCmd.Parameters.AddWithValue("@DeviceId", device.DeviceId);
                    using var reader = checkCmd.ExecuteReader();

                    if (reader.Read())
                    {
                        // Read current DB values safely
                        int dbGroupId = reader.GetInt32(0);
                        double dbLeft = reader.GetDouble(1);
                        double dbTop = reader.GetDouble(2);
                        string dbAddress = reader.IsDBNull(3) ? "" : reader.GetString(3);
                        string dbTitleJson = reader.IsDBNull(4) ? "" : reader.GetString(4);
                        string dbHintsJson = reader.IsDBNull(5) ? "" : reader.GetString(5);
                        string dbImagePath = reader.IsDBNull(6) ? "" : reader.GetString(6);
                        int? dbTargetMapId = reader.IsDBNull(7) ? null : reader.GetInt32(7);

                        // Compare them against the object trying to be saved
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
                        hasChanges = true; // If it somehow wasn't in the DB, force the update
                        changedFields.Add("Forced Update");
                    }
                }

                // IF NOTHING CHANGED, ABORT THE UPDATE AND DO NOT LOG!
                if (!hasChanges) return;

                // ==========================================
                // 2. EXECUTE THE UPDATE
                // ==========================================
                string sql = @"
            UPDATE Devices SET GroupId=@GroupId, Left=@Left, Top=@Top, Address=@Address, 
            TitleJson=@TitleJson, HintsJson=@HintsJson, HintImagePath=@HintImagePath, TargetMapId=@TargetMapId 
            WHERE DeviceId=@DeviceId;";

                using var cmd = new SqliteCommand(sql, connection);
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

                // The audit log now dynamically tells you exactly what they changed!
                InsertAuditLog("UPDATE", "Devices", device.DeviceId, $"Device IP: {device.Address ?? ""}; Changed: {string.Join(", ", changedFields)}");
            }
        }

        public void UpdateLabel(NetworkLabel label)
        {
            using var connection = GetOpenConnection();
            string textJson = JsonSerializer.Serialize(label.TextLines);

            if (label.LabelId == 0) // New Label (INSERT)
            {
                string sql = @"
                    INSERT INTO Labels (MapId, Left, Top, Width, Height, Background, BorderBrush, BorderThickness, HorizontalAlignment, VerticalAlignment, FontFamily, FontSize, FontStyle, FontWeight, Foreground, TextJson) 
                    VALUES (@MapId, @Left, @Top, @Width, @Height, @Background, @BorderBrush, @BorderThickness, @HorizontalAlignment, @VerticalAlignment, @FontFamily, @FontSize, @FontStyle, @FontWeight, @Foreground, @TextJson);
                    SELECT last_insert_rowid();";

                using var cmd = new SqliteCommand(sql, connection);
                AddLabelParameters(cmd, label, textJson);
                label.LabelId = Convert.ToInt32(cmd.ExecuteScalar());
            }
            else // Existing Label (UPDATE)
            {
                // ==========================================
                // 1. CHANGE DETECTION (Avoid unnecessary saves)
                // ==========================================
                bool hasChanges = false;
                List<string> changedFields = new();

                string checkSql = "SELECT Left, Top, Width, Height, TextJson FROM Labels WHERE LabelId = @LabelId";
                using (var checkCmd = new SqliteCommand(checkSql, connection))
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

                // IF NOTHING CHANGED, ABORT THE UPDATE AND DO NOT LOG!
                if (!hasChanges) return;

                // ==========================================
                // 2. EXECUTE THE UPDATE
                // ==========================================
                string sql = @"
            UPDATE Labels SET 
                Left = @Left, Top = @Top, Width = @Width, Height = @Height, 
                Background = @Background, BorderBrush = @BorderBrush, BorderThickness = @BorderThickness, 
                HorizontalAlignment = @HorizontalAlignment, VerticalAlignment = @VerticalAlignment, 
                FontFamily = @FontFamily, FontSize = @FontSize, FontStyle = @FontStyle, FontWeight = @FontWeight,
                Foreground = @Foreground, TextJson = @TextJson
            WHERE LabelId = @LabelId";

                using var cmd = new SqliteCommand(sql, connection);
                cmd.Parameters.AddWithValue("@LabelId", label.LabelId);
                AddLabelParameters(cmd, label, textJson); // Uses your existing parameter helper
                cmd.ExecuteNonQuery();

                InsertAuditLog("UPDATE", "Labels", label.LabelId, $"Changed: {string.Join(", ", changedFields)}");
            }
        }

        private void AddLabelParameters(SqliteCommand cmd, NetworkLabel label, string textJson)
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
            using var cmd = new SqliteCommand("DELETE FROM Devices WHERE DeviceId = @DeviceId", connection);
            cmd.Parameters.AddWithValue("@DeviceId", deviceId);
            cmd.ExecuteNonQuery();

            // Log this critical action for accountability!
            InsertAuditLog("DELETE", "Devices", deviceId, "Device removed from map.");
        }

        public void DeleteLabel(int labelId)
        {
            if (labelId <= 0) return;
            using var connection = GetOpenConnection();
            using var cmd = new SqliteCommand("DELETE FROM Labels WHERE LabelId = @LabelId", connection);
            cmd.Parameters.AddWithValue("@LabelId", labelId);
            cmd.ExecuteNonQuery();

            // Log this critical action for accountability!
            InsertAuditLog("DELETE", "Labels", labelId, "Label removed from map.");
        }

        // ─── SEARCH OPERATIONS ──────────────────────────────────────────

        public List<GlobalSearchResult> SearchDevices(string query, bool deepSearch)
        {
            var results = new List<GlobalSearchResult>();
            if (string.IsNullOrWhiteSpace(query)) return results;

            using var connection = GetOpenConnection();

            // Updated to search the new JSON columns!
            string sql = deepSearch
                ? @"SELECT MapId, DeviceId FROM Devices 
                    WHERE INSTR(LOWER(TitleJson), @Query) > 0 
                       OR INSTR(LOWER(Address), @Query) > 0 
                       OR INSTR(LOWER(HintsJson), @Query) > 0"
                : @"SELECT MapId, DeviceId FROM Devices 
                    WHERE INSTR(LOWER(TitleJson), @Query) > 0";

            using var cmd = new SqliteCommand(sql, connection);
            cmd.Parameters.AddWithValue("@Query", query.ToLower());

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

            // Fetches all groups and sorts them alphabetically for a cleaner UI!
            using var cmd = new SqliteCommand("SELECT GroupId, GroupName FROM Groups ORDER BY GroupName", connection);
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

        private SqliteConnection GetOpenConnection()
        {
            var connection = new SqliteConnection(DatabaseService.ConnectionString);
            connection.Open();

            // CRITICAL: SQLite requires Foreign Keys to be enabled on every single connection!
            using (var pragmaCmd = new SqliteCommand("PRAGMA foreign_keys = ON;", connection))
            {
                pragmaCmd.ExecuteNonQuery();
            }

            return connection;
        }


        public void SaveDeviceGroup(DeviceGroup group)
        {
            using var connection = GetOpenConnection();

            if (group.GroupId == 0) // It's a BRAND NEW group
            {
                string sql = @"
                    INSERT INTO Groups (GroupName, IconPath, DefaultCommand, IsMapLink) 
                    VALUES (@GroupName, @IconPath, @DefaultCommand, @IsMapLink);
                    SELECT last_insert_rowid();";

                using var cmd = new SqliteCommand(sql, connection);
                AddGroupParameters(cmd, group);
                group.GroupId = Convert.ToInt32(cmd.ExecuteScalar());
            }
            else // It's an EXISTING group being edited
            {
                string sql = @"
                    UPDATE Groups SET 
                        GroupName = @GroupName, IconPath = @IconPath, 
                        DefaultCommand = @DefaultCommand, IsMapLink = @IsMapLink
                    WHERE GroupId = @GroupId";

                using var cmd = new SqliteCommand(sql, connection);
                cmd.Parameters.AddWithValue("@GroupId", group.GroupId);
                AddGroupParameters(cmd, group);
                cmd.ExecuteNonQuery();

                // Log this critical update action for accountability!
                InsertAuditLog("UPDATE", "Groups", group.GroupId, $"Updated group name: {group.GroupName}");
            }
        }

        private void AddGroupParameters(SqliteCommand cmd, DeviceGroup group)
        {
            cmd.Parameters.AddWithValue("@GroupName", group.GroupName ?? "New Group");
            cmd.Parameters.AddWithValue("@IconPath", group.IconPath ?? "");
            cmd.Parameters.AddWithValue("@DefaultCommand", group.DefaultCommand ?? "Ping");
            cmd.Parameters.AddWithValue("@IsMapLink", group.IsMapLink ? 1 : 0);
        }

        public List<DeviceGroup> GetAllDeviceGroups()
        {
            if (DbPath == null || DbPath == "")
                return new List<DeviceGroup>();

            var groups = new List<DeviceGroup>();
            using var connection = GetOpenConnection();
            using var cmd = new SqliteCommand("SELECT GroupId, GroupName, IconPath, DefaultCommand, IsMapLink FROM Groups ORDER BY GroupName", connection);
            using var reader = cmd.ExecuteReader();

            while (reader.Read())
            {
                groups.Add(new DeviceGroup
                {
                    GroupId = reader.GetInt32(0),
                    GroupName = reader.GetString(1),
                    IconPath = reader.IsDBNull(2) ? "" : reader.GetString(2),
                    DefaultCommand = reader.IsDBNull(3) ? "Ping" : reader.GetString(3),
                    IsMapLink = !reader.IsDBNull(4) && reader.GetInt32(4) == 1
                });
            }
            return groups;
        }

        public void DeleteDeviceGroup(int groupId)
        {
            using var connection = GetOpenConnection();
            using var cmd = new SqliteCommand("DELETE FROM Groups WHERE GroupId = @GroupId", connection);
            cmd.Parameters.AddWithValue("@GroupId", groupId);
            cmd.ExecuteNonQuery();

            // Log this critical action for accountability!
            InsertAuditLog("DELETE", "Groups", groupId, "Device group removed.");
        }



        // --- 1. WRITE TO LOG ---
        public void InsertAuditLog(string actionType, string tableName, int recordId, string details = "")
        {
            using var connection = GetOpenConnection();
            connection.Open();
            string sql = @"
                INSERT INTO AuditLogs (Timestamp, Username, ActionType, TableName, RecordId, Details) 
                VALUES (@Timestamp, @Username, @ActionType, @TableName, @RecordId, @Details)";

            using var cmd = new SqliteCommand(sql, connection);
            // Save the exact moment it happened in a sortable format
            cmd.Parameters.AddWithValue("@Timestamp", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            cmd.Parameters.AddWithValue("@Username", Environment.UserName);
            cmd.Parameters.AddWithValue("@ActionType", actionType);
            cmd.Parameters.AddWithValue("@TableName", tableName);
            cmd.Parameters.AddWithValue("@RecordId", recordId);
            cmd.Parameters.AddWithValue("@Details", details);

            cmd.ExecuteNonQuery();
        }

        private void InsertAuditLogInternal(string actionType, string tableName, int recordId, string details, SqliteConnection connection, SqliteTransaction transaction)
        {
            string sql = @"
        INSERT INTO AuditLogs (Timestamp, Username, ActionType, TableName, RecordId, Details) 
        VALUES (@Timestamp, @Username, @ActionType, @TableName, @RecordId, @Details)";

            using var cmd = new SqliteCommand(sql, connection, transaction); // Pass the transaction here!
            cmd.Parameters.AddWithValue("@Timestamp", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
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
            connection.Open();
            // Order by descending so the newest actions are always at the top!
            string sql = "SELECT * FROM AuditLogs ORDER BY LogId DESC";

            using var cmd = new SqliteCommand(sql, connection);
            using var reader = cmd.ExecuteReader();

            while (reader.Read())
            {
                logs.Add(new AuditLog
                {
                    LogId = reader.GetInt32(0),
                    Timestamp = reader.GetString(1),
                    Username = reader.GetString(2),
                    ActionType = reader.GetString(3),
                    TableName = reader.GetString(4),
                    RecordId = reader.GetInt32(5),
                    Details = reader.IsDBNull(6) ? "" : reader.GetString(6)
                });
            }
            return logs;
        }


        public void SaveMapBatch(IEnumerable<NetworkDevice> devices, IEnumerable<NetworkLabel> labels)
        {
            using var connection = GetOpenConnection();
            using var transaction = connection.BeginTransaction(); // LOCK THE DB ONCE

            try
            {
                foreach (var device in devices)
                {
                    // You can call your exact same UpdateDevice logic here, 
                    // just ensure it accepts the existing 'connection' and 'transaction'
                    // OR put the raw SQL UPDATE command here.
                }

                foreach (var label in labels)
                {
                    // Update labels...
                }

                transaction.Commit(); // WRITE ALL CHANGES TO DISK INSTANTLY
            }
            catch
            {
                transaction.Rollback(); // If one fails, cancel everything so the DB doesn't corrupt
                throw;
            }
        }
    }
}