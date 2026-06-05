using Microsoft.Data.Sqlite;
using NetworkMapViewerV2.Data;
using System;

namespace NetworkMapViewerV2.Services
{
    public static class AuditService
    {
        /// <summary>
        /// Logs an action to the database. Can be used inside an existing transaction, or standalone.
        /// </summary>
        public static void LogAction(string actionType, string tableName, int recordId, string details, SqliteTransaction? transaction = null)
        {
            string insertSql = @"
                INSERT INTO AuditLogs (Timestamp, Username, ActionType, TableName, RecordId, Details) 
                VALUES (@Time, @User, @Action, @Table, @RecordId, @Details);";

            // If we are already inside a transaction (like saving a whole map), use that connection.
            // Otherwise, open a quick new one just for this log.
            if (transaction != null)
            {
                using var cmd = new SqliteCommand(insertSql, transaction.Connection, transaction);
                ExecuteLogCommand(cmd, actionType, tableName, recordId, details);
            }
            else
            {
                using var connection = new SqliteConnection(DatabaseService.ConnectionString);
                connection.Open();

                using var cmd = new SqliteCommand(insertSql, connection);
                ExecuteLogCommand(cmd, actionType, tableName, recordId, details);
            }
        }

        private static void ExecuteLogCommand(SqliteCommand cmd, string actionType, string tableName, int recordId, string details)
        {
            cmd.Parameters.AddWithValue("@Time", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            cmd.Parameters.AddWithValue("@User", Environment.UserName); // Automatically gets the Windows user!
            cmd.Parameters.AddWithValue("@Action", actionType);
            cmd.Parameters.AddWithValue("@Table", tableName);
            cmd.Parameters.AddWithValue("@RecordId", recordId);
            cmd.Parameters.AddWithValue("@Details", details ?? (object)DBNull.Value);

            cmd.ExecuteNonQuery();
        }
    }
}