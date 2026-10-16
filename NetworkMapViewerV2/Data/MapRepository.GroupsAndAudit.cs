using Microsoft.Data.SqlClient;
using NetworkMapViewerV2.Helpers.LocalFetcher;
using NetworkMapViewerV2.Models;
using NetworkMapViewerV2.Services;
using System.Transactions;

namespace NetworkMapViewerV2.Data
{
    public partial class MapRepository
    {
        // ─── DEVICE GROUPS ──────────────────────────────────────────────

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

        public List<DeviceGroup> GetAllDeviceGroups()
        {
            if (string.IsNullOrEmpty(DbPath))
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

        public void SaveDeviceGroup(DeviceGroup group)
        {
            using var connection = GetOpenConnection();

            if (group.GroupId == 0)
            {
                string sql = @"
                    INSERT INTO Groups (GroupName, IconPath, DefaultCommand, IsMapLink) 
                    VALUES (@GroupName, @IconPath, @DefaultCommand, @IsMapLink);
                    SELECT SCOPE_IDENTITY();";

                using var cmd = new SqlCommand(sql, connection);
                AddGroupParameters(cmd, group);
                group.GroupId = Convert.ToInt32(cmd.ExecuteScalar());

                InsertAuditLogInternal(actionType: "INSERT", target: "Groups", details: $"Created new group: {group.GroupName}", targetId: group.GroupId, mapName: null, oldValue: null, newValue: null, connection: connection, transaction: null);
               
            }
            else
            {
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

                    InsertAuditLogInternal(actionType: "UPDATE", target: "Groups", details: $"Changed: {string.Join(", ", changedFields)}", targetId: group.GroupId, mapName: null, oldValue: null, newValue: null, connection: connection, transaction: null);
                }
            }

            SettingsService.UpdateGroupDefaultCommand(group.GroupId, group.DefaultCommand ?? "Ping");
        }

        public void DeleteDeviceGroup(int groupId)
        {
            using var connection = GetOpenConnection();
            using var cmd = new SqlCommand("DELETE FROM Groups WHERE GroupId = @GroupId", connection);
            cmd.Parameters.AddWithValue("@GroupId", groupId);
            cmd.ExecuteNonQuery();

            InsertAuditLogInternal(actionType: "DELETE", target: "Groups", details: "Device group removed.", targetId: groupId, mapName: null, oldValue: null, newValue: null, connection: connection, transaction: null);
        }

        private static void AddGroupParameters(SqlCommand cmd, DeviceGroup group)
        {
            cmd.Parameters.AddWithValue("@GroupName", group.GroupName ?? "New Group");
            cmd.Parameters.AddWithValue("@IconPath", group.IconPath ?? "");
            cmd.Parameters.AddWithValue("@DefaultCommand", group.DefaultCommand ?? "Ping");
            cmd.Parameters.AddWithValue("@IsMapLink", group.IsMapLink ? 1 : 0);
        }

        private string GetGroupNameInternal(int groupId, SqlConnection connection, SqlTransaction? transaction)
        {
            if (groupId <= 0) return "PC";
            try
            {
                using var cmd = new SqlCommand("SELECT GroupName FROM Groups WHERE GroupId = @GroupId", connection, transaction);
                cmd.Parameters.AddWithValue("@GroupId", groupId);
                object? result = cmd.ExecuteScalar();
                return result != null && result != DBNull.Value ? result.ToString()! : "PC";
            }
            catch
            {
                return "PC";
            }
        }

        // ─── AUDIT LOGS ──────────────────────────────────────────────────

        // --- PUBLIC AUDIT LOG WRITER ---
        public void InsertAuditLog(            string actionType,            string target,            string details,            int targetId = 0,            string? mapName = null,
            string? oldValue = null,            string? newValue = null)
        {
            using var connection = GetOpenConnection();
            InsertAuditLogInternal(actionType, target, details, targetId, mapName, oldValue, newValue, connection, null);
        }

        // --- PRIVATE INTERNAL AUDIT LOG WRITER (POSITIONALLY FIXED) ---
        private void InsertAuditLogInternal(            string actionType,            string target,            string details,            int targetId,            string? mapName,
            string? oldValue,            string? newValue,            SqlConnection connection,            SqlTransaction? transaction = null)
        {
            string sql = @"
        INSERT INTO AuditLogs (TimeStamp, mapName, userName, MachineName, ActionType, Target, TargetID, Details, OldValue, NewValue) 
        VALUES (@TimeStamp, @mapName, @userName, @MachineName, @ActionType, @Target, @TargetID, @Details, @OldValue, @NewValue)";

            using var cmd = new SqlCommand(sql, connection, transaction);

            cmd.Parameters.AddWithValue("@TimeStamp", DateTime.Now);
            cmd.Parameters.AddWithValue("@mapName", string.IsNullOrWhiteSpace(mapName) ? DBNull.Value : mapName);
            cmd.Parameters.AddWithValue("@userName", Environment.UserName);
            cmd.Parameters.AddWithValue("@MachineName", Environment.MachineName);
            cmd.Parameters.AddWithValue("@ActionType", actionType);
            cmd.Parameters.AddWithValue("@Target", target);
            cmd.Parameters.AddWithValue("@TargetID", targetId);
            cmd.Parameters.AddWithValue("@Details", details);
            cmd.Parameters.AddWithValue("@OldValue", string.IsNullOrWhiteSpace(oldValue) ? DBNull.Value : oldValue);
            cmd.Parameters.AddWithValue("@NewValue", string.IsNullOrWhiteSpace(newValue) ? DBNull.Value : newValue);

            cmd.ExecuteNonQuery();
        }


        public List<AuditLog> GetAuditLogs()
        {
            var logs = new List<AuditLog>();
            using var connection = GetOpenConnection();

            string sql = @"
                SELECT TOP (1000) 
                    [id], [TimeStamp], [mapName], [userName], [MachineName], [ActionType], [Target], [Details], [OldValue], [NewValue]
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
                    MachineName = reader.IsDBNull(4) ? "" : reader.GetString(4),
                    ActionType = reader.IsDBNull(5) ? "" : reader.GetString(5),
                    Target = reader.IsDBNull(6) ? "" : reader.GetString(6),
                    Details = reader.IsDBNull(7) ? "" : reader.GetString(7),
                    OldValue = reader.IsDBNull(8) ? "" : reader.GetString(8),
                    NewValue = reader.IsDBNull(9) ? "" : reader.GetString(9)
                });
            }

            return logs;
        }
    }
}