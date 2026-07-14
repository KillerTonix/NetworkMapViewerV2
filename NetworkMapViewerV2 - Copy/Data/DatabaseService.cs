using Microsoft.Data.SqlClient;
using NetworkMapViewerV2.Models;
using NetworkMapViewerV2.Services;
using System.IO;

namespace NetworkMapViewerV2.Data
{
    public static class DatabaseService
    {
        private static AppSettings settings = SettingsService.Load();

        // DbPath is no longer needed for SQL Server initialization, but we keep IconsPath
        private static string IconsPath = Path.Combine(settings.DeviceIconsPath ?? "", "ON");

        // Make sure "NetMapVwr" database is created on the server first!
        public static string ConnectionString => @"Server=localhost\SQLEXPRESS;Database=NetMapVwr;Trusted_Connection=True;TrustServerCertificate=True;";

        public static void InitializeDatabase()
        {
            using var connection = new SqlConnection(ConnectionString);
            connection.Open();

            string Sql = @$"
                -- Maps
                IF OBJECT_ID('dbo.Maps', 'U') IS NULL
                BEGIN
                    CREATE TABLE dbo.Maps
                    (
                        MapId INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                        MapName NVARCHAR(MAX) NOT NULL
                    );
                END;
                
                -- Groups
                IF OBJECT_ID('dbo.Groups', 'U') IS NULL
                BEGIN
                    CREATE TABLE dbo.Groups
                    (
                        GroupId INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                        GroupName NVARCHAR(MAX) NOT NULL,
                        IconPath NVARCHAR(MAX) NULL,
                        DefaultCommand NVARCHAR(MAX) NULL,
                        IsMapLink BIT NOT NULL
                            CONSTRAINT DF_Groups_IsMapLink DEFAULT (0)
                    );
                END;
                
                -- AuditLogs
                IF OBJECT_ID('dbo.AuditLogs', 'U') IS NULL
                BEGIN
                    CREATE TABLE dbo.AuditLogs
                    (
                        LogId INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                        [Timestamp] DATETIME2 NOT NULL,
                        Username NVARCHAR(255) NOT NULL,
                        ActionType NVARCHAR(100) NOT NULL,
                        TableName NVARCHAR(255) NOT NULL,
                        RecordId INT NOT NULL,
                        Details NVARCHAR(MAX) NULL
                    );
                END;
                
                -- Devices
                IF OBJECT_ID('dbo.Devices', 'U') IS NULL
                BEGIN
                    CREATE TABLE dbo.Devices
                    (
                        DeviceId INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                        MapId INT NOT NULL,
                        GroupId INT NOT NULL,
                        [Left] FLOAT NULL,
                        [Top] FLOAT NULL,
                        Address NVARCHAR(MAX) NULL,
                        TitleJson NVARCHAR(MAX) NULL,
                        HintsJson NVARCHAR(MAX) NULL,
                        HintImagePath NVARCHAR(MAX) NULL,
                        TargetMapId INT NULL,

                        CONSTRAINT FK_Devices_Maps
                            FOREIGN KEY (MapId)
                            REFERENCES dbo.Maps(MapId)
                            ON DELETE CASCADE,

                        CONSTRAINT FK_Devices_Groups
                            FOREIGN KEY (GroupId)
                            REFERENCES dbo.Groups(GroupId)
                            ON DELETE CASCADE
                    );
                END;
                
                -- Labels
                IF OBJECT_ID('dbo.Labels', 'U') IS NULL
                BEGIN
                    CREATE TABLE dbo.Labels
                    (
                        LabelId INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
                        MapId INT NOT NULL,
                        [Left] FLOAT NULL,
                        [Top] FLOAT NULL,
                        Width FLOAT NULL,
                        Height FLOAT NULL,
                        Background NVARCHAR(MAX) NULL,
                        BorderBrush NVARCHAR(MAX) NULL,
                        BorderThickness INT NULL,
                        HorizontalAlignment NVARCHAR(50) NULL,
                        VerticalAlignment NVARCHAR(50) NULL,
                        FontFamily NVARCHAR(255) NULL,
                        FontSize FLOAT NULL,
                        FontStyle NVARCHAR(50) NULL,
                        FontWeight NVARCHAR(50) NULL,
                        Foreground NVARCHAR(MAX) NULL,
                        TextJson NVARCHAR(MAX) NULL,

                        CONSTRAINT FK_Labels_Maps
                            FOREIGN KEY (MapId)
                            REFERENCES dbo.Maps(MapId)
                            ON DELETE CASCADE
                    );
                END;

                -- Replicating INSERT OR IGNORE: Check if the first group exists
                IF NOT EXISTS (SELECT 1 FROM Groups WHERE GroupId = 1)
                BEGIN
                    -- Temporarily allow explicit ID insertion into an IDENTITY column
                    SET IDENTITY_INSERT Groups ON;
                    
                    INSERT INTO Groups (GroupId, GroupName, IconPath, IsMapLink) VALUES 
                        (1, 'Computer', '{IconsPath}\Computer.png', 0),
                        (2, 'NUC', '{IconsPath}\NUC.png', 0),
                        (3, 'iMac', '{IconsPath}\iMac.png', 0),
                        (4, 'Laptop', '{IconsPath}\Laptop.png', 0),
                        (5, 'Info Monitor', '{IconsPath}\InfoMon.png', 0),
                        (6, 'Tablo', '{IconsPath}\Tablo.png', 0),
                        (7, 'QMS', '{IconsPath}\QMS.png', 0),
                        (8, 'Grandstream DP750', '{IconsPath}\DP750.png', 0),
                        (9, 'Grandstream GXP', '{IconsPath}\Phone.png', 0),
                        (10, 'DVR', '{IconsPath}\DVR.png', 0),
                        (11, 'FingerPrint', '{IconsPath}\AMG.png', 0),
                        (12, 'Printer', '{IconsPath}\Printer.png', 0),
                        (13, 'FXO', '{IconsPath}\FXO.png', 0),
                        (14, 'City', '{IconsPath}\City.png', 1),
                        (15, 'Town', '{IconsPath}\Town.png', 1),
                        (16, 'Switch', '{IconsPath}\Switch.png', 0),
                        (17, 'WiFi', '{IconsPath}\WiFi.png', 0),
                        (18, 'Fiber Converter', '{IconsPath}\FiberConverter.png', 0),
                        (19, 'Server', '{IconsPath}\Server.png', 0);
                        
                    -- Turn it back off so future inserts auto-increment properly
                    SET IDENTITY_INSERT Groups OFF;
                END
            ";

            // Execute the script using SqlCommand
            using var cmd = new SqlCommand(Sql, connection);
            cmd.ExecuteNonQuery();
        }
    }
}