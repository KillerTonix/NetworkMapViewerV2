using Microsoft.Data.SqlClient;
using NetworkMapViewerV2.Models;
using NetworkMapViewerV2.Services;
using System.Collections.ObjectModel;
using System.Text.Json;
using System.Windows;

namespace NetworkMapViewerV2.Data
{
    public class MapRepository
    {
        private static readonly AppSettings settings = SettingsService.Load();
        private static readonly string DbPath = settings.DatabaseServer ?? "";

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
            string sql = "INSERT INTO Maps (MapName, MapType) VALUES (@MapName, @MapType); SELECT SCOPE_IDENTITY();";
            using var cmd = new SqlCommand(sql, connection);

            cmd.Parameters.AddWithValue("@MapName", mapName);
            cmd.Parameters.AddWithValue("@MapType", mapType);

            // CHANGED: Fixed arguments to match the new schema
            InsertAuditLog("INSERT", "Maps", $"Created new map: {mapType}", mapName);

            return Convert.ToInt32(cmd.ExecuteScalar());
        }

        public void DeleteMap(int mapId)
        {
            try
            {
                if (mapId <= 0) return;

                using var connection = GetOpenConnection();

                // Look up the Map Name first so the Audit Log knows what was deleted!
                string mapName = "Unknown Map";
                using (var nameCmd = new SqlCommand("SELECT MapName FROM Maps WHERE MapId = @MapId", connection))
                {
                    nameCmd.Parameters.AddWithValue("@MapId", mapId);
                    var result = nameCmd.ExecuteScalar();
                    if (result != null) mapName = result.ToString() ?? "Unknown Map";
                }

                using var cmd = new SqlCommand("DELETE FROM Maps WHERE MapId = @MapId", connection);
                cmd.Parameters.AddWithValue("@MapId", mapId);
                cmd.ExecuteNonQuery();

                // CHANGED: Fixed arguments to match the new schema
                InsertAuditLog("DELETE", "Maps", "Entire map deleted.", mapName);
            }
            catch (Exception ex)
            {
                if (ex.Message.Contains("permission was denied"))
                {
                    MessageBox.Show($"Failed to delete map:\nYou don't have permission to modify the database.", "Database Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                else
                {
                    MessageBox.Show($"Failed to delete map:\n{ex.Message}", "Database Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        public MapTabState LoadMap(int mapId)
        {
            var state = new MapTabState { MapId = mapId };

            using var connection = GetOpenConnection();

            // 1. Get Map Name
            using (var cmd = new SqlCommand("SELECT MapId, MapName, MapType FROM Maps WHERE MapId = @MapId", connection))
            {
                cmd.Parameters.AddWithValue("@MapId", mapId);
                using var reader = cmd.ExecuteReader();
                if (reader.Read())
                {
                    state.MapId = reader.GetInt32(0);
                    state.MapName = reader.GetString(1);
                    state.MapType = reader.IsDBNull(2) ? "Head Office" : reader.GetString(2);
                }
                reader.Close();
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
                        Titles = JsonSerializer.Deserialize<ObservableCollection<string>>(titlesJson) ?? [],
                        Hints = JsonSerializer.Deserialize<ObservableCollection<string>>(hintsJson) ?? []
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
                        TextLines = JsonSerializer.Deserialize<ObservableCollection<string>>(textJson) ?? []
                    });
                }
            }

            return state;
        }

        // ─── UPSERT (UPDATE OR INSERT) OPERATIONS ───────────────────────

        private void UpdateDeviceInternal(NetworkDevice device, SqlConnection connection, SqlTransaction transaction, string mapName)
        {
            string titlesJson = JsonSerializer.Serialize(device.Titles);
            string hintsJson = JsonSerializer.Serialize(device.Hints);
            List<string> changedFields = [];

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

                device.DeviceId = Convert.ToInt32(cmd.ExecuteScalar());

                // CHANGED: Fixed arguments to match the new schema
                InsertAuditLogInternal("INSERT", "Devices", $"Added new device (IP: {device.Address})", mapName, connection, transaction);
            }
            else // UPDATE
            {
                bool hasChanges = false;

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
                try
                {
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

                    // CHANGED: Fixed arguments to match the new schema
                    InsertAuditLogInternal("UPDATE", "Devices", $"Device IP: {device.Address ?? ""}; Changed: {string.Join(", ", changedFields)}", mapName, connection, transaction);
                }
                catch (Exception ex)
                {
                    if (ex.Message.Contains("permission was denied"))
                    {
                        MessageBox.Show($"Failed to update device:\nYou don't have permission to modify the database.", "Database Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                    else
                    {
                        MessageBox.Show($"Failed to update device:\n{ex.Message}", "Database Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            }
        }

        private void UpdateLabelInternal(NetworkLabel label, SqlConnection connection, SqlTransaction transaction, string mapName)
        {
            string textJson = JsonSerializer.Serialize(label.TextLines);

            if (label.LabelId == 0) // INSERT
            {
                string sql = @"
            INSERT INTO Labels (MapId, [Left], [Top], Width, Height, Background, BorderBrush, BorderThickness, HorizontalAlignment, VerticalAlignment, FontFamily, FontSize, FontStyle, FontWeight, Foreground, TextJson) 
            VALUES (@MapId, @Left, @Top, @Width, @Height, @Background, @BorderBrush, @BorderThickness, @HorizontalAlignment, @VerticalAlignment, @FontFamily, @FontSize, @FontStyle, @FontWeight, @Foreground, @TextJson);
            SELECT SCOPE_IDENTITY();";

                using var cmd = new SqlCommand(sql, connection, transaction);
                AddLabelParameters(cmd, label, textJson);
                label.LabelId = Convert.ToInt32(cmd.ExecuteScalar());

                // CHANGED: Added missing INSERT audit log here
                InsertAuditLogInternal("INSERT", "Labels", "Added new label", mapName, connection, transaction);
            }
            else // UPDATE
            {
                bool hasChanges = false;
                List<string> changedFields = [];

                string checkSql = @"SELECT [Left], [Top], Width, Height, TextJson, Background, Foreground, FontSize, FontFamily, FontWeight, FontStyle FROM Labels WHERE LabelId = @LabelId";
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
                        string dbBackground = reader.IsDBNull(5) ? "Transparent" : reader.GetString(5);
                        string dbForeground = reader.IsDBNull(6) ? "#000000" : reader.GetString(6);
                        double dbFontSize = reader.IsDBNull(7) ? 12.0 : reader.GetDouble(7);
                        string dbFontFamily = reader.IsDBNull(8) ? "Segoe UI" : reader.GetString(8);
                        string dbFontWeight = reader.IsDBNull(9) ? "Normal" : reader.GetString(9);
                        string dbFontStyle = reader.IsDBNull(10) ? "Normal" : reader.GetString(10);

                        if (Math.Abs(dbLeft - label.Left) > 0.01) { hasChanges = true; changedFields.Add($"Left: {Math.Round(dbLeft, 1)} -> {Math.Round(label.Left, 1)}"); }
                        if (Math.Abs(dbTop - label.Top) > 0.01) { hasChanges = true; changedFields.Add($"Top: {Math.Round(dbTop, 1)} -> {Math.Round(label.Top, 1)}"); }
                        if (Math.Abs(dbWidth - label.Width) > 0.01) { hasChanges = true; changedFields.Add($"Width: {Math.Round(dbWidth, 1)} -> {Math.Round(label.Width, 1)}"); }
                        if (dbTextJson != textJson) { hasChanges = true; changedFields.Add($"Text Updated"); }

                        if (dbBackground != (label.Background ?? "Transparent"))
                        {
                            hasChanges = true;
                            changedFields.Add($"Bg: {dbBackground} -> {label.Background}");
                        }
                        if (dbForeground != (label.Foreground ?? "#000000"))
                        {
                            hasChanges = true;
                            changedFields.Add($"Fg: {dbForeground} -> {label.Foreground}");
                        }
                        if (Math.Abs(dbFontSize - label.FontSize) > 0.01)
                        {
                            hasChanges = true;
                            changedFields.Add($"Font Size: {dbFontSize} -> {label.FontSize}");
                        }
                    }
                    else
                    {
                        hasChanges = true;
                        changedFields.Add("Format");
                    }
                }

                if (!hasChanges) return;

                string sql = @"
            UPDATE Labels SET 
                [Left] = @Left, [Top] = @Top, Width = @Width, Height = @Height, 
                Background = @Background, BorderBrush = @BorderBrush, BorderThickness = @BorderThickness, 
                HorizontalAlignment = @HorizontalAlignment, VerticalAlignment = @VerticalAlignment, 
                FontFamily = @FontFamily, FontSize = @FontSize, FontStyle = @FontStyle, FontWeight = @FontWeight,
                Foreground = @Foreground, TextJson = @TextJson
            WHERE LabelId = @LabelId";

                try
                {
                    using var cmd = new SqlCommand(sql, connection, transaction);
                    cmd.Parameters.AddWithValue("@LabelId", label.LabelId);
                    AddLabelParameters(cmd, label, textJson);
                    cmd.ExecuteNonQuery();

                    // CHANGED: Fixed arguments to match the new schema
                    InsertAuditLogInternal("UPDATE", "Labels", $"Changed: {string.Join(", ", changedFields)}", mapName, connection, transaction);
                }
                catch (Exception ex)
                {
                    if (ex.Message.Contains("permission was denied"))
                    {
                        MessageBox.Show($"Failed to update label:\nYou don't have permission to modify the database.", "Database Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                    else
                    {
                        MessageBox.Show($"Failed to update label:\n{ex.Message}", "Database Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
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

        public bool DeleteDevice(int deviceId)
        {
            try
            {
                if (deviceId <= 0) return true;
                using var connection = GetOpenConnection();
                using var cmd = new SqlCommand("DELETE FROM Devices WHERE DeviceId = @DeviceId", connection);
                cmd.Parameters.AddWithValue("@DeviceId", deviceId);
                cmd.ExecuteNonQuery();

                // CHANGED: Fixed arguments to match the new schema
                InsertAuditLog("DELETE", "Devices", "Device removed from map.", null);
                return true;
            }
            catch (Exception ex)
            {
                if (ex.Message.Contains("permission was denied"))
                {
                    MessageBox.Show($"Failed to delete device:\nYou don't have permission to modify the database.", "Database Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                else
                {
                    MessageBox.Show($"Failed to delete device:\n{ex.Message}", "Database Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                return false;
            }
        }

        public bool DeleteLabel(int labelId)
        {
            try
            {
                if (labelId <= 0) return true;
                using var connection = GetOpenConnection();
                using var cmd = new SqlCommand("DELETE FROM Labels WHERE LabelId = @LabelId", connection);
                cmd.Parameters.AddWithValue("@LabelId", labelId);
                cmd.ExecuteNonQuery();

                // CHANGED: Fixed arguments to match the new schema
                InsertAuditLog("DELETE", "Labels", "Label removed from map.", null);
                return true;
            }
            catch (Exception ex)
            {
                if (ex.Message.Contains("permission was denied"))
                {
                    MessageBox.Show($"Failed to delete Label:\nYou don't have permission to modify the database.", "Database Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                else
                {
                    MessageBox.Show($"Failed to delete Label:\n{ex.Message}", "Database Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                return false;
            }
        }

        // ─── SEARCH OPERATIONS ──────────────────────────────────────────

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

                // CHANGED: Added missing INSERT log 
                InsertAuditLogInternal("INSERT", "Groups", $"Created new group: {group.GroupName}", null, connection, null);
            }
            else // EXISTING GROUP
            {
                // CHANGED: Added dynamic diffing logic just like Labels and Devices!
                bool hasChanges = false;
                List<string> changedFields = [];

                string checkSql = "SELECT GroupName, IconPath, IsMapLink FROM Groups WHERE GroupId = @GroupId";
                using (var checkCmd = new SqlCommand(checkSql, connection))
                {
                    checkCmd.Parameters.AddWithValue("@GroupId", group.GroupId);
                    using var reader = checkCmd.ExecuteReader();

                    if (reader.Read())
                    {
                        string dbGroupName = reader.IsDBNull(0) ? "" : reader.GetString(0);
                        string dbIconPath = reader.IsDBNull(1) ? "" : reader.GetString(1);
                        bool dbIsMapLink = !reader.IsDBNull(2) && reader.GetBoolean(2);

                        if (dbGroupName != (group.GroupName ?? "")) { hasChanges = true; changedFields.Add($"Name: {dbGroupName} -> {group.GroupName}"); }
                        if (dbIconPath != (group.IconPath ?? "")) { hasChanges = true; changedFields.Add("Icon"); }
                        if (dbIsMapLink != group.IsMapLink) { hasChanges = true; changedFields.Add($"IsMapLink: {dbIsMapLink} -> {group.IsMapLink}"); }
                    }
                    else
                    {
                        hasChanges = true;
                        changedFields.Add("Forced Update");
                    }
                }

                if (hasChanges)
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

                    InsertAuditLogInternal("UPDATE", "Groups", $"Changed: {string.Join(", ", changedFields)}", null, connection, null);
                }
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

            // CHANGED: Fixed arguments to match the new schema
            InsertAuditLog("DELETE", "Groups", "Device group removed.", null);
        }

        // --- 1. WRITE TO LOG ---

        // CHANGED: Completely updated signature and parameters to match the new AuditLogs table!
        public void InsertAuditLog(string actionType, string target, string details, string mapName = null)
        {
            using var connection = GetOpenConnection();

            string sql = @"
                INSERT INTO AuditLogs (TimeStamp, mapName, userName, ActionType, Target, Details) 
                VALUES (@TimeStamp, @mapName, @userName, @ActionType, @Target, @Details)";

            using var cmd = new SqlCommand(sql, connection);

            cmd.Parameters.AddWithValue("@TimeStamp", DateTime.Now);
            cmd.Parameters.AddWithValue("@mapName", string.IsNullOrWhiteSpace(mapName) ? DBNull.Value : mapName);
            cmd.Parameters.AddWithValue("@userName", Environment.UserName);
            cmd.Parameters.AddWithValue("@ActionType", actionType);
            cmd.Parameters.AddWithValue("@Target", target);
            cmd.Parameters.AddWithValue("@Details", details);

            cmd.ExecuteNonQuery();
        }

        private void InsertAuditLogInternal(string actionType, string target, string details, string mapName, SqlConnection connection, SqlTransaction transaction)
        {
            string sql = @"
        INSERT INTO AuditLogs (TimeStamp, mapName, userName, ActionType, Target, Details) 
        VALUES (@TimeStamp, @mapName, @userName, @ActionType, @Target, @Details)";

            using var cmd = new SqlCommand(sql, connection, transaction);

            cmd.Parameters.AddWithValue("@TimeStamp", DateTime.Now);
            cmd.Parameters.AddWithValue("@mapName", string.IsNullOrWhiteSpace(mapName) ? DBNull.Value : mapName);
            cmd.Parameters.AddWithValue("@userName", Environment.UserName);
            cmd.Parameters.AddWithValue("@ActionType", actionType);
            cmd.Parameters.AddWithValue("@Target", target);
            cmd.Parameters.AddWithValue("@Details", details);

            cmd.ExecuteNonQuery();
        }

        // --- 2. READ THE LOGS ---
        public List<AuditLog> GetAuditLogs()
        {
            var logs = new List<AuditLog>();
            using var connection = GetOpenConnection();

            string sql = @"
        SELECT TOP (1000) 
            [id], 
            [TimeStamp], 
            [mapName], 
            [userName], 
            [ActionType], 
            [Target], 
            [Details]
        FROM [AuditLogs]
        ORDER BY [TimeStamp] DESC";

            using var cmd = new SqlCommand(sql, connection);
            using var reader = cmd.ExecuteReader();

            while (reader.Read())
            {
                logs.Add(new AuditLog
                {
                    Id = reader.GetInt32(0),
                    TimeStamp = Convert.ToDateTime(reader.GetDateTime(1)).ToString("yyyy-MM-dd HH:mm:ss"),
                    MapName = reader.IsDBNull(2) ? "Unknown Map" : reader.GetString(2),
                    UserName = reader.IsDBNull(3) ? "System" : reader.GetString(3),
                    ActionType = reader.IsDBNull(4) ? "" : reader.GetString(4),
                    Target = reader.IsDBNull(5) ? "" : reader.GetString(5),
                    Details = reader.IsDBNull(6) ? "" : reader.GetString(6)
                });
            }

            return logs;
        }

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

        public void UpdateLabel(NetworkLabel label, string mapName)
        {
            using var connection = GetOpenConnection();
            using var transaction = connection.BeginTransaction();
            try
            {
                UpdateLabelInternal(label, connection, transaction, mapName);
                transaction.Commit();
            }
            catch { transaction.Rollback(); throw; }
        }
    }
}