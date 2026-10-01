using NetworkMapViewerV2.Models;
using NetworkMapViewerV2.Services;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;

namespace NetworkMapViewerV2.Helpers.LocalFetcher
{
    public class ADComputerInfo
    {
        public string Name { get; set; } = string.Empty;
        public string IPAddress { get; set; } = string.Empty;
        public DateTime? WhenCreated { get; set; }
    }

    public class ADComputer
    {
        private static readonly AppSettings Settings = SettingsService.Load();
        private static readonly string ScriptsPath = Settings.ScriptsPath ?? string.Empty;

        public async Task<List<ADComputerInfo>> GetRecentADComputersAsync()
        {
            string scriptPath = Path.Combine(ScriptsPath, "GetADComputerLast15Days.ps1");

            if (!File.Exists(scriptPath))
            {
                return [];
            }

            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{scriptPath}\"",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = Process.Start(psi);
                if (process == null) return [];

                string output = await process.StandardOutput.ReadToEndAsync();
                string error = await process.StandardError.ReadToEndAsync();
                await process.WaitForExitAsync();

                if (!string.IsNullOrWhiteSpace(error))
                {
                    Debug.WriteLine($"PowerShell Error: {error}");
                }

                if (string.IsNullOrWhiteSpace(output))
                {
                    return [];
                }

                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

                // Handle single vs array JSON output from PowerShell
                if (output.TrimStart().StartsWith('['))
                {
                    return JsonSerializer.Deserialize<List<ADComputerInfo>>(output, options) ?? [];
                }

                var singleItem = JsonSerializer.Deserialize<ADComputerInfo>(output, options);
                return singleItem != null ? new List<ADComputerInfo> { singleItem } : new List<ADComputerInfo>();
            }
            catch (Exception e)
            {
                MessageBox.Show($"Error running AD fetch script: {e.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }

            return [];
        }
    }
}