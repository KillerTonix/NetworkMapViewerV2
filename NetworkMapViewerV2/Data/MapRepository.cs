using Microsoft.Data.SqlClient;
using NetworkMapViewerV2.Models;
using NetworkMapViewerV2.Services;
using System.Collections.ObjectModel;
using System.Text.Json;
using System.Windows;

namespace NetworkMapViewerV2.Data
{
    public partial class MapRepository
    {
        private static readonly AppSettings settings = SettingsService.Load();
        private static readonly string DbPath = settings.DatabaseServer ?? "";

        private static SqlConnection GetOpenConnection()
        {
            var connection = new SqlConnection(DatabaseService.ConnectionString);
            connection.Open();
            return connection;
        }

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
                    MapName = reader.GetString(1) ?? "Unknown Map",
                    MapType = reader.IsDBNull(2) ? "Head Office" : reader.GetString(2)
                });
            }
            return maps;
        }

        public int CreateNewMap(string mapName, string mapType)
        {
            try
            {
                using var connection = GetOpenConnection();                
                string sql = "INSERT INTO Maps (MapName, MapType, Version) VALUES (@MapName, @MapType, 1); SELECT SCOPE_IDENTITY();";
                using var cmd = new SqlCommand(sql, connection);

                cmd.Parameters.AddWithValue("@MapName", mapName);
                cmd.Parameters.AddWithValue("@MapType", mapType);
                int newMapId = Convert.ToInt32(cmd.ExecuteScalar());

                InsertAuditLogInternal(actionType: "INSERT", target: "Maps", details: $"Created new map: {mapType}", targetId: 0, mapName: mapName, oldValue: null, newValue: null, connection: connection, transaction: null);

                return newMapId;
            }
            catch (Exception ex)
            {
                if (ex.Message.Contains("permission was denied"))
                {
                    MessageBox.Show("Failed to create map:\nYou don't have permission to modify the database.", "Database Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                else
                {
                    MessageBox.Show($"Failed to create map:\n{ex.Message}", "Database Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                return -1; // Indicate failure
            }
        }

        public void DeleteMap(int mapId)
        {
            try
            {
                if (mapId <= 0) return;

                using var connection = GetOpenConnection();

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

                InsertAuditLogInternal(actionType: "DELETE", target: "Maps", details: "Entire map deleted.", targetId: mapId, mapName: mapName, oldValue: null, newValue: null, connection: connection, transaction: null);
            }
            catch (Exception ex)
            {
                if (ex.Message.Contains("permission was denied"))
                {
                    MessageBox.Show("Failed to delete map:\nYou don't have permission to modify the database.", "Database Error", MessageBoxButton.OK, MessageBoxImage.Error);
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

            using (var cmd = new SqlCommand("SELECT MapId, MapName, MapType, Version FROM Maps WHERE MapId = @MapId", connection))
            {
                cmd.Parameters.AddWithValue("@MapId", mapId);
                using var reader = cmd.ExecuteReader();
                if (reader.Read())
                {
                    state.MapId = reader.GetInt32(0);
                    state.MapName = reader.GetString(1);
                    state.MapType = reader.IsDBNull(2) ? "Head Office" : reader.GetString(2);
                    state.Version = reader.IsDBNull(3) ? 1 : reader.GetInt32(3);
                }
                reader.Close();
            }

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

        public async Task<int> GetMapVersionAsync(int mapId)
        {
            await using var connection = new SqlConnection(DatabaseService.ConnectionString);
            await connection.OpenAsync();
            using var cmd = new SqlCommand("SELECT Version FROM Maps WHERE MapId = @MapId", connection);
            cmd.Parameters.AddWithValue("@MapId", mapId);
            object? result = await cmd.ExecuteScalarAsync();
            return result != null && result != DBNull.Value ? Convert.ToInt32(result) : 0;
        }


        /// <summary>
        /// Saves all pending changes of a map in ONE transaction: deletes, inserts/updates, version bump.
        /// If anything fails, nothing is saved. Throws MapConflictException if someone else saved the map first.
        /// </summary>
        public void SaveMapChanges(MapTabState tab)
        {
            using var connection = GetOpenConnection();
            using var transaction = connection.BeginTransaction();

            // Items that will get a new ID from the database. If the save fails, reset them to 0 so the next save inserts them again.
            var newDevices = tab.Devices.Where(d => d.DeviceId == 0).ToList();
            var newLabels = tab.Labels.Where(l => l.LabelId == 0).ToList();

            try
            {
                // 1. Lock this map's row and check that nobody saved since we loaded it
                using (var cmd = new SqlCommand("SELECT Version FROM Maps WITH (UPDLOCK, HOLDLOCK) WHERE MapId = @MapId", connection, transaction))
                {
                    cmd.Parameters.AddWithValue("@MapId", tab.MapId);
                    object? result = cmd.ExecuteScalar();
                    int dbVersion = result is null or DBNull ? 1 : Convert.ToInt32(result);
                    if (dbVersion != tab.Version)
                        throw new MapConflictException($"Map \"{tab.MapName}\" was saved by someone else after you opened it.");
                }

                // 2. Deletes
                foreach (int deviceId in tab.PendingDeletedDeviceIds.Where(id => id > 0))
                {
                    using var cmd = new SqlCommand("DELETE FROM Devices WHERE DeviceId = @Id", connection, transaction);
                    cmd.Parameters.AddWithValue("@Id", deviceId);
                    cmd.ExecuteNonQuery();
                    InsertAuditLogInternal("DELETE", "Devices", $"Device removed from {tab.MapName}", deviceId, tab.MapName, null, null, connection, transaction);
                }
                foreach (int labelId in tab.PendingDeletedLabelIds.Where(id => id > 0))
                {
                    using var cmd = new SqlCommand("DELETE FROM Labels WHERE LabelId = @Id", connection, transaction);
                    cmd.Parameters.AddWithValue("@Id", labelId);
                    cmd.ExecuteNonQuery();
                    InsertAuditLogInternal("DELETE", "Labels", $"Label removed from {tab.MapName}", labelId, tab.MapName, null, null, connection, transaction);
                }

                // 3. Inserts / updates (the existing Internal methods already use the transaction you pass them)
                foreach (var device in tab.Devices) UpdateDeviceInternal(device, connection, transaction, tab.MapName);
                foreach (var label in tab.Labels) UpdateLabelInternal(label, connection, transaction, tab.MapName);

                // 4. Bump the version
                int newVersion;
                using (var cmd = new SqlCommand("UPDATE Maps SET Version = ISNULL(Version, 1) + 1 OUTPUT inserted.Version WHERE MapId = @MapId", connection, transaction))
                {
                    cmd.Parameters.AddWithValue("@MapId", tab.MapId);
                    newVersion = Convert.ToInt32(cmd.ExecuteScalar());
                }

                transaction.Commit();

                // Only after a successful commit
                tab.Version = newVersion;
                tab.PendingDeletedDeviceIds.Clear();
                tab.PendingDeletedLabelIds.Clear();
            }
            catch
            {
                try { transaction.Rollback(); } catch { }   // the connection may already be dead
                foreach (var d in newDevices) d.DeviceId = 0;
                foreach (var l in newLabels) l.LabelId = 0;
                throw;
            }
        }
    }

    public class MapConflictException(string message) : Exception(message);
}