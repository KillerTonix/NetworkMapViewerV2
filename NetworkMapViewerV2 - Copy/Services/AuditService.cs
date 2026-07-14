using Microsoft.Data.SqlClient;
using NetworkMapViewerV2.Data;
using System;

namespace NetworkMapViewerV2.Services
{
    public static class AuditService
    {
        /// <summary>
        /// Logs an action to the database. Can be used inside an existing transaction, or standalone.
        /// </summary>
        // Changed SqliteTransaction to SqlTransaction
        public static void LogAction(string actionType, string tableName, int recordId, string details, SqlTransaction? transaction = null)
        {
            string insertSql = @"
                INSERT INTO AuditLogs (Timestamp, Username, ActionType, TableName, RecordId, Details) 
                VALUES (@Time, @User, @Action, @Table, @RecordId, @Details);";

            // If we are already inside a transaction (like saving a whole map), use that connection.
            // Otherwise, open a quick new one just for this log.
            if (transaction != null)
            {
                // Changed SqliteCommand to SqlCommand
                using var cmd = new SqlCommand(insertSql, transaction.Connection, transaction);
                ExecuteLogCommand(cmd, actionType, tableName, recordId, details);
            }
            else
            {
                // Changed SqliteConnection to SqlConnection
                using var connection = new SqlConnection(DatabaseService.ConnectionString);
                connection.Open();

                using var cmd = new SqlCommand(insertSql, connection);
                ExecuteLogCommand(cmd, actionType, tableName, recordId, details);
            }
        }

        // Changed SqliteCommand to SqlCommand
        private static void ExecuteLogCommand(SqlCommand cmd, string actionType, string tableName, int recordId, string details)
        {
            // MS SQL natively handles C# DateTime objects, so no string conversion is needed
            cmd.Parameters.AddWithValue("@Time", DateTime.Now);

            cmd.Parameters.AddWithValue("@User", Environment.UserName); // Automatically gets the Windows user!
            cmd.Parameters.AddWithValue("@Action", actionType);
            cmd.Parameters.AddWithValue("@Table", tableName);
            cmd.Parameters.AddWithValue("@RecordId", recordId);

            // Slightly safer null check for the details column
            cmd.Parameters.AddWithValue("@Details", string.IsNullOrEmpty(details) ? DBNull.Value : details);
        }
    }
}