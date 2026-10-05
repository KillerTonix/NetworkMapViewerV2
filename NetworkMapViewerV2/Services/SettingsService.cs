using NetworkMapViewerV2.Models;
using System.IO;
using System.Text.Json;
using System.Windows;

namespace NetworkMapViewerV2.Services
{
    public static class SettingsService
    {       
        // Global Shared Settings: Resides alongside the executable in the shared folder
        private static readonly string GlobalSettingsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings.json");

        // User Personal Settings: Resides in C:\Users\<Username>\AppData\Roaming\NetworkMapViewerV2\settings.user.json
        private static readonly string UserSettingsFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "NetworkMapViewerV2");
        private static readonly string UserSettingsPath = Path.Combine(UserSettingsFolder, "settings.user.json");

        private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

        public static AppSettings Load()
        {
            var combinedSettings = new AppSettings();

            // 1. Load Global Settings (Shared Folder)
            try
            {
                if (File.Exists(GlobalSettingsPath))
                {
                    string globalJson = File.ReadAllText(GlobalSettingsPath);
                    var globalData = JsonSerializer.Deserialize<GlobalSettingsModel>(globalJson);
                    if (globalData != null)
                    {
                        ApplyGlobalSettings(combinedSettings, globalData);
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to load global settings from shared folder: {ex.Message}",
                    "Global Settings Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            }

            // 2. Load User Settings (%APPDATA%)
            try
            {
                if (File.Exists(UserSettingsPath))
                {
                    string userJson = File.ReadAllText(UserSettingsPath);
                    var userData = JsonSerializer.Deserialize<UserSettingsModel>(userJson);
                    if (userData != null)
                    {
                        ApplyUserSettings(combinedSettings, userData);
                    }
                }
                else
                {
                    // First run for user: generate default settings, write settings.user.json, and apply
                    var defaultUserData = CreateDefaultUserSettings();
                    ApplyUserSettings(combinedSettings, defaultUserData);

                    EnsureUserFolderExists();
                    string defaultUserJson = JsonSerializer.Serialize(defaultUserData, JsonOptions);
                    File.WriteAllText(UserSettingsPath, defaultUserJson);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to load personal user settings: {ex.Message}","User Settings Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            }

            return combinedSettings;
        }

        public static void Save(AppSettings settings)
        {
            // 1. Save User-Specific Settings to %APPDATA%
            try
            {
                EnsureUserFolderExists();
                var userData = ExtractUserSettings(settings);
                string userJson = JsonSerializer.Serialize(userData, JsonOptions);
                File.WriteAllText(UserSettingsPath, userJson);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to save user settings: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }

            // 2. Save Global Settings to Shared Folder (Only if write permissions exist)
            try
            {
                var globalData = ExtractGlobalSettings(settings);
                string globalJson = JsonSerializer.Serialize(globalData, JsonOptions);
                File.WriteAllText(GlobalSettingsPath, globalJson);
            }
            catch (UnauthorizedAccessException)
            {
               
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not update global shared settings: {ex.Message}",
                    "Global Settings Save Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private static void EnsureUserFolderExists()
        {
            if (!Directory.Exists(UserSettingsFolder))
            {
                Directory.CreateDirectory(UserSettingsFolder);
            }
        }

        private static UserSettingsModel CreateDefaultUserSettings()
        {
            return new UserSettingsModel
            {
                LastOpenedMapId = 1,
                PingAutostart = true,
                Commands =
                [
                    new() { Name = "Ping",          Icon = "⚡", Path = @"C:\Windows\System32\PING.EXE",                      Arguments = "{Address} -t" },
                    new() { Name = "VNC View",      Icon = "🖥️", Path = @"C:\Program Files\uvnc bvba\UltraVNC\vncviewer.exe", Arguments = "-scale 85/100 -shared -normalcursor -emulate3 -password {VNCPassword} {Address}" },
                    new() { Name = "SSH",           Icon = "🐧", Path = @"C:\Program Files\PuTTY\putty.exe",                  Arguments = "-ssh administrator@{Address} -pw {SSHPassword}" },
                    new() { Name = "Google Chrome", Icon = "🌐", Path = "chrome.exe",                                         Arguments = "http://{Address}"},
                    new() { Name = @"Explorer C:\", Icon = "📁", Path = "explorer.exe",                                       Arguments = @"\\{Address}\C$"},
                    new() { Name = @"Explorer D:\", Icon = "📁", Path = "explorer.exe",                                       Arguments = @"\\{Address}\D$"}
                ]
            };
        }

        // --- DTO Mapping Helpers ---

        private static void ApplyGlobalSettings(AppSettings target, GlobalSettingsModel source)
        {
            target.DatabaseServer = source.DatabaseServer;
            target.DatabaseName = source.DatabaseName;
            target.DatabaseUser = source.DatabaseUser;
            target.DatabasePassword = source.DatabasePassword;

            target.DeviceIconsPath = source.DeviceIconsPath;
            target.HintImagesPath = source.HintImagesPath;
            target.ScriptsPath = source.ScriptsPath;

            target.PrinterPassword = source.PrinterPassword;
            target.GrandstreamPassword = source.GrandstreamPassword;
            target.VNCPassword = source.VNCPassword;
            target.SSHPassword = source.SSHPassword;
            target.ManagersPCPassword = source.ManagersPCPassword;
            target.QMSPassword = source.QMSPassword;
        }

        private static void ApplyUserSettings(AppSettings target, UserSettingsModel source)
        {
            target.LastOpenedMapId = source.LastOpenedMapId;
            target.Commands = source.Commands;
            target.GroupDefaultCommands = source.GroupDefaultCommands;

            target.PingAutostart = source.PingAutostart;
            target.PingPeriodSeconds = source.PingPeriodSeconds;

            target.DeepperSearchMode = source.DeepperSearchMode;
            target.EqualitySearchMode = source.EqualitySearchMode;

            target.ENS_Rules = source.ENS_Rules;
            target.ENS_SaveToLog = source.ENS_SaveToLog;
            target.ENS_ShowMessage = source.ENS_ShowMessage;
            target.ENS_PlayOfflineSound = source.ENS_PlayOfflineSound;
            target.ENS_OfflineSoundFilePath = source.ENS_OfflineSoundFilePath;
            target.ENS_PlayOnlineSound = source.ENS_PlayOnlineSound;
            target.ENS_OnlineSoundFilePath = source.ENS_OnlineSoundFilePath;

            target.CustomVariables = source.CustomVariables ?? new(StringComparer.OrdinalIgnoreCase);
        }

        private static GlobalSettingsModel ExtractGlobalSettings(AppSettings source) => new()
        {
            DatabaseServer = source.DatabaseServer,
            DatabaseName = source.DatabaseName,
            DatabaseUser = source.DatabaseUser,
            DatabasePassword = source.DatabasePassword,

            DeviceIconsPath = source.DeviceIconsPath,
            HintImagesPath = source.HintImagesPath,
            ScriptsPath = source.ScriptsPath,

            PrinterPassword = source.PrinterPassword,
            GrandstreamPassword = source.GrandstreamPassword,
            VNCPassword = source.VNCPassword,
            SSHPassword = source.SSHPassword,
            ManagersPCPassword = source.ManagersPCPassword,
            QMSPassword = source.QMSPassword
        };

        private static UserSettingsModel ExtractUserSettings(AppSettings source) => new()
        {
            LastOpenedMapId = source.LastOpenedMapId,
            Commands = source.Commands,
            GroupDefaultCommands = source.GroupDefaultCommands,
            PingAutostart = source.PingAutostart,
            PingPeriodSeconds = source.PingPeriodSeconds,
            DeepperSearchMode = source.DeepperSearchMode,
            EqualitySearchMode = source.EqualitySearchMode,
            ENS_Rules = source.ENS_Rules,
            ENS_SaveToLog = source.ENS_SaveToLog,
            ENS_ShowMessage = source.ENS_ShowMessage,
            ENS_PlayOfflineSound = source.ENS_PlayOfflineSound,
            ENS_OfflineSoundFilePath = source.ENS_OfflineSoundFilePath,
            ENS_PlayOnlineSound = source.ENS_PlayOnlineSound,
            ENS_OnlineSoundFilePath = source.ENS_OnlineSoundFilePath,

            CustomVariables = source.CustomVariables ?? new(StringComparer.OrdinalIgnoreCase)
        };

        public static void UpdateGroupDefaultCommand(int groupId, string commandName)
        {
            var settings = Load();

            // Ensure the dictionary exists (in case it's an old settings.json file)
            settings.GroupDefaultCommands ??= [];

            // Update the user's personal preference and save to file
            settings.GroupDefaultCommands[groupId] = commandName;
            Save(settings);
        }
    }
}