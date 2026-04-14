using Microsoft.Data.Sqlite;
using System;
using System.IO;

namespace NetworkMapViewerV2.Data
{
    public static class DatabaseService
    {
        // Puts the database right next to your .exe file
        public static string DbPath => "\\\\evoca.am\\evoca\\pinger\\Network Map Viewer\\Database\\Database.db";
        public static string IconsPath => "\\\\evoca.am\\evoca\\pinger\\Network Map Viewer\\Device Icons\\ON";
        public static string ConnectionString => $"Data Source={DbPath};";

        public static void InitializeDatabase()
        {
            using var connection = new SqliteConnection(ConnectionString);
            connection.Open();

            using (var pragmaCmd = new SqliteCommand("PRAGMA foreign_keys = ON;", connection))
            {
                pragmaCmd.ExecuteNonQuery();
            }

            string Sql = @$"CREATE TABLE IF NOT EXISTS Maps (
	                        MapId INTEGER PRIMARY KEY AUTOINCREMENT,
	                        MapName TEXT NOT NULL
                        );

                        CREATE TABLE IF NOT EXISTS Groups (
                            GroupId INTEGER PRIMARY KEY AUTOINCREMENT,
                            GroupName TEXT NOT NULL,
                            IconPath TEXT,
                            DefaultCommand TEXT,
                            IsMapLink INTEGER DEFAULT 0
                        );

                        CREATE TABLE IF NOT EXISTS Devices (
                            DeviceId INTEGER PRIMARY KEY AUTOINCREMENT,
                            MapId INTEGER NOT NULL,
                            GroupId INTEGER NOT NULL,
                            Left REAL,
                            Top REAL,    
                            Address TEXT,    
                            TitleJson TEXT,
                            HintsJson TEXT,
                            HintImagePath TEXT,
                            TargetMapId INTEGER,
                            FOREIGN KEY(MapId) REFERENCES Maps(MapId) ON DELETE CASCADE,
                            FOREIGN KEY(GroupId) REFERENCES Groups(GroupId) ON DELETE CASCADE
                        );

                        CREATE TABLE IF NOT EXISTS Labels (
	                        LabelId INTEGER PRIMARY KEY AUTOINCREMENT,
	                        MapId INTEGER NOT NULL,
	                        Left REAL,
	                        Top REAL,
	                        Width REAL,
	                        Height REAL,
	                        Background TEXT,
	                        BorderBrush TEXT,
	                        BorderThickness INTEGER,
	                        HorizontalAlignment TEXT,
	                        VerticalAlignment TEXT,	
	                        FontFamily TEXT,
	                        FontSize REAL,
	                        FontStyle TEXT,
	                        FontWeight TEXT,
	                        Foreground TEXT,	
	                        TextJson TEXT,
	                        FOREIGN KEY(MapId) REFERENCES Maps(MapId) ON DELETE CASCADE
                        );

                        CREATE TABLE IF NOT EXISTS AuditLogs (
	                        LogId INTEGER PRIMARY KEY AUTOINCREMENT,
	                        Timestamp TEXT NOT NULL,
	                        Username TEXT NOT NULL,
	                        ActionType TEXT NOT NULL,
	                        TableName TEXT NOT NULL,
	                        RecordId INTEGER NOT NULL,
	                        Details TEXT
                        );

                        INSERT OR IGNORE INTO Groups (GroupId, GroupName, IconPath, IsMapLink) VALUES 
                            (1, 'Computer', '{IconsPath}\\Computer.png', 0),
                            (2, 'NUC', '{IconsPath}\\NUC.png', 0),
                            (3, 'iMac', '{IconsPath}\\iMac.png', 0),
                            (4, 'Laptop', '{IconsPath}\\Laptop.png', 0),
                            (5, 'Info Monitor', '{IconsPath}\\InfoMon.png', 0),
                            (6, 'Tablo', '{IconsPath}\\Tablo.png', 0),
                            (7, 'QMS', '{IconsPath}\\QMS.png', 0),
                            (8, 'Grandstream DP750', '{IconsPath}\\DP750.png', 0),
                            (9, 'Grandstream GXP', '{IconsPath}\\Phone.png', 0),
                            (10, 'DVR', '{IconsPath}\\DVR.png', 0),
                            (11, 'FingerPrint', '{IconsPath}\\AMG.png', 0),
                            (12, 'Printer', '{IconsPath}\\Printer.png', 0),
                            (13, 'FXO', '{IconsPath}\\FXO.png', 0),
                            (14, 'City', '{IconsPath}\\City.png', 1),
                            (15, 'Town', '{IconsPath}\\Town.png', 1),
                            (16, 'Switch', '{IconsPath}\\Switch.png', 0),
                            (17, 'WiFi', '{IconsPath}\\WiFi.png', 0),
                            (18, 'Fiber Converter', '{IconsPath}\\FiberConverter.png', 0),
                            (19, 'Server', '{IconsPath}\\Server.png', 0);
                        ";

            using var cmd = new SqliteCommand(Sql, connection);
            cmd.ExecuteNonQuery();
        }
    }
}