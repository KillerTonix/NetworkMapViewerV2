using Microsoft.Data.SqlClient;
using NetworkMapViewerV2.Models;
using System.Text.Json;
using System.Windows;

namespace NetworkMapViewerV2.Data
{
    public partial class MapRepository
    {
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

        private void UpdateLabelInternal(NetworkLabel label, SqlConnection connection, SqlTransaction transaction, string mapName)
        {
            string textJson = JsonSerializer.Serialize(label.TextLines);
            string labelSnippet = label.TextLines?.FirstOrDefault() ?? "Empty Label";

            if (label.LabelId == 0) // INSERT
            {
                string sql = @"
            INSERT INTO Labels (MapId, [Left], [Top], Width, Height, Background, BorderBrush, BorderThickness, HorizontalAlignment, VerticalAlignment, FontFamily, FontSize, FontStyle, FontWeight, Foreground, TextJson) 
            VALUES (@MapId, @Left, @Top, @Width, @Height, @Background, @BorderBrush, @BorderThickness, @HorizontalAlignment, @VerticalAlignment, @FontFamily, @FontSize, @FontStyle, @FontWeight, @Foreground, @TextJson);
            SELECT SCOPE_IDENTITY();";

                using var cmd = new SqlCommand(sql, connection, transaction);
                AddLabelParameters(cmd, label, textJson);
                label.LabelId = Convert.ToInt32(cmd.ExecuteScalar());

                InsertAuditLogInternal(
                    actionType: "INSERT",
                    target: "Label",
                    details: $"Added new label: \"{labelSnippet}\"",
                    targetId: label.LabelId,
                    mapName: mapName,
                    oldValue: null,
                    newValue: null,
                    connection: connection,
                    transaction: transaction);
            }
            else // UPDATE
            {
                bool hasChanges = false;
                List<string> changedFields = new();

                // Database baseline variables
                double dbLeft = 0, dbTop = 0, dbWidth = 0, dbHeight = 0, dbFontSize = 12.0;
                int dbBorderThickness = 0;
                string dbTextJson = "[]", dbBackground = "Transparent", dbForeground = "#000000";
                string dbBorderBrush = "Transparent", dbHorizontalAlignment = "Center", dbVerticalAlignment = "Center";
                string dbFontFamily = "Segoe UI", dbFontWeight = "Normal", dbFontStyle = "Normal";

                string checkSql = @"
            SELECT [Left], [Top], Width, Height, TextJson, Background, Foreground, FontSize, FontFamily, FontWeight, FontStyle, BorderBrush, BorderThickness, HorizontalAlignment, VerticalAlignment 
            FROM Labels WHERE LabelId = @LabelId";

                using (var checkCmd = new SqlCommand(checkSql, connection, transaction))
                {
                    checkCmd.Parameters.AddWithValue("@LabelId", label.LabelId);
                    using var reader = checkCmd.ExecuteReader();

                    if (reader.Read())
                    {
                        dbLeft = reader.GetDouble(0);
                        dbTop = reader.GetDouble(1);
                        dbWidth = reader.GetDouble(2);
                        dbHeight = reader.GetDouble(3);
                        dbTextJson = reader.IsDBNull(4) ? "[]" : reader.GetString(4);
                        dbBackground = reader.IsDBNull(5) ? "Transparent" : reader.GetString(5);
                        dbForeground = reader.IsDBNull(6) ? "#000000" : reader.GetString(6);
                        dbFontSize = reader.IsDBNull(7) ? 12.0 : reader.GetDouble(7);
                        dbFontFamily = reader.IsDBNull(8) ? "Segoe UI" : reader.GetString(8);
                        dbFontWeight = reader.IsDBNull(9) ? "Normal" : reader.GetString(9);
                        dbFontStyle = reader.IsDBNull(10) ? "Normal" : reader.GetString(10);
                        dbBorderBrush = reader.IsDBNull(11) ? "Transparent" : reader.GetString(11);
                        dbBorderThickness = reader.IsDBNull(12) ? 0 : reader.GetInt32(12);
                        dbHorizontalAlignment = reader.IsDBNull(13) ? "Center" : reader.GetString(13);
                        dbVerticalAlignment = reader.IsDBNull(14) ? "Center" : reader.GetString(14);

                        if (Math.Abs(dbLeft - label.Left) > 0.01 || Math.Abs(dbTop - label.Top) > 0.01) { hasChanges = true; changedFields.Add("Position"); }
                        if (Math.Abs(dbWidth - label.Width) > 0.01 || Math.Abs(dbHeight - label.Height) > 0.01) { hasChanges = true; changedFields.Add("Size"); }
                        if (dbTextJson != textJson) { hasChanges = true; changedFields.Add("Text"); }
                        if (dbBackground != (label.Background ?? "Transparent")) { hasChanges = true; changedFields.Add("Background"); }
                        if (dbForeground != (label.Foreground ?? "#000000")) { hasChanges = true; changedFields.Add("Foreground"); }
                        if (Math.Abs(dbFontSize - label.FontSize) > 0.01) { hasChanges = true; changedFields.Add($"Font Size: {dbFontSize} -> {label.FontSize}"); }
                        if (dbFontFamily != (label.FontFamily ?? "Segoe UI")) { hasChanges = true; changedFields.Add("Font Family"); }
                        if (dbFontWeight != (label.FontWeight ?? "Normal")) { hasChanges = true; changedFields.Add("Font Weight"); }
                        if (dbFontStyle != (label.FontStyle ?? "Normal")) { hasChanges = true; changedFields.Add("Font Style"); }
                    }
                    else
                    {
                        hasChanges = true;
                        changedFields.Add("Forced Update");
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

                    var jsonOptions = new JsonSerializerOptions { WriteIndented = true };

                    // Construct Old Snapshot
                    var oldSnapshot = new
                    {
                        TextLines = JsonSerializer.Deserialize<List<string>>(dbTextJson) ?? new(),
                        Position = new { Left = Math.Round(dbLeft, 1), Top = Math.Round(dbTop, 1) },
                        Size = new { Width = Math.Round(dbWidth, 1), Height = Math.Round(dbHeight, 1) },
                        Style = new
                        {
                            Background = dbBackground,
                            Foreground = dbForeground,
                            BorderBrush = dbBorderBrush,
                            BorderThickness = dbBorderThickness,
                            FontFamily = dbFontFamily,
                            FontSize = dbFontSize,
                            FontStyle = dbFontStyle,
                            FontWeight = dbFontWeight,
                            HorizontalAlignment = dbHorizontalAlignment,
                            VerticalAlignment = dbVerticalAlignment
                        }
                    };

                    // Construct New Snapshot
                    var newSnapshot = new
                    {
                        TextLines = label.TextLines?.ToList() ?? new(),
                        Position = new { Left = Math.Round(label.Left, 1), Top = Math.Round(label.Top, 1) },
                        Size = new { Width = Math.Round(label.Width, 1), Height = Math.Round(label.Height, 1) },
                        Style = new
                        {
                            Background = label.Background ?? "Transparent",
                            Foreground = label.Foreground ?? "#000000",
                            BorderBrush = label.BorderBrush ?? "Transparent",
                            BorderThickness = label.BorderThickness,
                            FontFamily = label.FontFamily ?? "Segoe UI",
                            FontSize = label.FontSize,
                            FontStyle = label.FontStyle ?? "Normal",
                            FontWeight = label.FontWeight ?? "Normal",
                            HorizontalAlignment = label.HorizontalAlignment ?? "Center",
                            VerticalAlignment = label.VerticalAlignment ?? "Center"
                        }
                    };

                    string oldValueJson = JsonSerializer.Serialize(oldSnapshot, jsonOptions);
                    string newValueJson = JsonSerializer.Serialize(newSnapshot, jsonOptions);
                    string details = $"Label: \"{labelSnippet}\" | Changed: {string.Join(", ", changedFields)}";

                    InsertAuditLogInternal(
                        actionType: "UPDATE",
                        target: "Label",
                        details: details,
                        targetId: label.LabelId,
                        mapName: mapName,
                        oldValue: oldValueJson,
                        newValue: newValueJson,
                        connection: connection,
                        transaction: transaction);
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

        public bool DeleteLabel(int labelId, string mapName)
        {
            try
            {
                if (labelId <= 0) return true;
                using var connection = GetOpenConnection();
                using var cmd = new SqlCommand("DELETE FROM Labels WHERE LabelId = @LabelId", connection);
                cmd.Parameters.AddWithValue("@LabelId", labelId);
                cmd.ExecuteNonQuery();

                InsertAuditLogInternal(actionType: "DELETE", target: "Labels", details: $"Label removed from {mapName}", targetId: labelId, mapName: mapName, oldValue: null, newValue: null, connection: connection, transaction: null);
                return true;
            }
            catch (Exception ex)
            {
                if (ex.Message.Contains("permission was denied"))
                {
                    MessageBox.Show("Failed to delete Label:\nYou don't have permission to modify the database.", "Database Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                else
                {
                    MessageBox.Show($"Failed to delete Label:\n{ex.Message}", "Database Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                return false;
            }
        }

        private static void AddLabelParameters(SqlCommand cmd, NetworkLabel label, string textJson)
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
    }
}