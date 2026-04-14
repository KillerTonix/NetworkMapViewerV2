using NetworkMapViewerV2.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace NetworkMapViewerV2.Services
{
    public static class SettingsService
    {
        private static readonly string SettingsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings.json");

        public static AppSettings Load()
        {
            if (!File.Exists(SettingsPath))
                return CreateDefaultSettings();

            try
            {
                string json = File.ReadAllText(SettingsPath);
                return JsonSerializer.Deserialize<AppSettings>(json) ?? CreateDefaultSettings();
            }
            catch
            {
                return CreateDefaultSettings();
            }
        }

        public static void Save(AppSettings settings)
        {
            try
            {
                string json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(SettingsPath, json);
            }
            catch { }
        }
               

        private static AppSettings CreateDefaultSettings()
        {
            return new AppSettings
            {
                Commands =
                [
                    new() { Name = "Ping",          Icon = "⚡", Path = @"C:\Windows\System32\PING.EXE",                               Arguments = "{Address} -t" },
                    new() { Name = "VNC View",      Icon = "🖥️", Path = @"C:\Program Files\uvnc bvba\UltraVNC\vncviewer.exe",          Arguments = "-fullscreen -scale 95/100 -shared -normalcursor -emulate3 -password prombank {Address}" },
                    new() { Name = "Google Chrome", Icon = "🌐", Path = "chrome.exe",                                                  Arguments = "http://{Address}"}
                ],

                // --- NEW: Define the default double-click actions here! ---
                DefaultDoubleClickCommands = []
            };
        }
    }
}