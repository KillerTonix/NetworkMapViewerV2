using Microsoft.Data.SqlClient;
using NetworkMapViewerV2.Helpers.Passwords;
using NetworkMapViewerV2.Models;
using NetworkMapViewerV2.Services;
using System.IO;

namespace NetworkMapViewerV2.Data
{
    public static class DatabaseService
    {
        private static AppSettings settings = SettingsService.Load();
        private static string decryptedPassword = SecureSettingsHelper.UnprotectPassword(settings.DatabasePassword) ?? "";

        // DbPath is no longer needed for SQL Server initialization, but we keep IconsPath
        private static string IconsPath = Path.Combine(settings.DeviceIconsPath ?? "", "ON");

        // Make sure "NetMapVwr" database is created on the server first!
        public static string ConnectionString => @$"Server={settings.DatabaseServer};Database={settings.DatabaseName};User Id={settings.DatabaseUser};Password={decryptedPassword};TrustServerCertificate=True;";
        public static void InitializeDatabase()
        {
            using var connection = new SqlConnection(ConnectionString);
            connection.Open();

            // Send a ping query to verify database response
            using var cmd = new SqlCommand("SELECT 1;", connection);
            cmd.ExecuteScalar();
        }
    }
}