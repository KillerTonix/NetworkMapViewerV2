using NetworkMapViewerV2.Models;
using NetworkMapViewerV2.Services;
using System.IO;
using System.Windows;

namespace NetworkMapViewerV2.Helpers.LocalFetcher
{
    internal class ADName
    {
        private static AppSettings settings = SettingsService.Load();

        private static string ScriptsPath = settings.ScriptsPath ?? "";
        public async Task<string> ResolveADNameAsync(string rawUsername)
        {
            // 1. Clean the username (Strips "DOMAIN\" so it's just "jdoe")
            string cleanUsername = rawUsername;
            if (cleanUsername.Contains('\\'))
            {
                cleanUsername = cleanUsername.Split('\\').Last();
            }

            string scriptPath = Path.Combine(ScriptsPath, "GetADUser.ps1");

            // If the script is missing, just return the raw username so the map doesn't break
            if (!File.Exists(scriptPath)) return rawUsername;

            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{scriptPath}\" -Username \"{cleanUsername}\"",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = System.Diagnostics.Process.Start(psi);
                if (process == null) return rawUsername;

                string output = await process.StandardOutput.ReadToEndAsync();
                await process.WaitForExitAsync();

                // Extract the clean name from your script's output
                foreach (var line in output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
                {
                    if (line.StartsWith("USERNAME="))
                    {
                        string adName = line[9..].Trim();
                        if (!string.IsNullOrWhiteSpace(adName)) return adName;
                    }
                }
            }
            catch (Exception e)
            {
                MessageBox.Show(e.Message);
            }

            return rawUsername; // Fallback on failure
        }

    }
}
