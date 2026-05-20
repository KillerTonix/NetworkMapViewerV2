using NetworkMapViewerV2.Helpers.Passwords;
using NetworkMapViewerV2.Models;
using NetworkMapViewerV2.Services;
using NetworkMapViewerV2.ViewModels;
using NetworkMapViewerV2.Views;
using System.IO;
using System.Windows;

namespace NetworkMapViewerV2.Helpers.LocalFetcher
{
    internal class AutoFill
    {
        private static AppSettings settings = SettingsService.Load();

        private static string ScriptsPath = settings.ScriptsPath ?? "";
        private static string HintImagesPath = settings.HintImagesPath ?? "";
        private static string decryptedPasswordSSH = SecureSettingsHelper.UnprotectPassword(settings.SSHPassword) ?? "";
        private static string decryptedPasswordManagers = SecureSettingsHelper.UnprotectPassword(settings.ManagersPCPassword) ?? "";
        private static string decryptedPasswordQMS = SecureSettingsHelper.UnprotectPassword(settings.QMSPassword) ?? "";

        // Caller must pass the MapCanvasView instance
        public static async Task RunAutoFillScript(MapCanvasView mapCanvas, NetworkDevice device, string scriptName)
        {
            if (string.IsNullOrWhiteSpace(device.Address) || device.Address == "0.0.0.0")
            {
                MessageBox.Show("Please set a valid IP Address for this device first!", "Missing IP", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string scriptPath = Path.Combine(ScriptsPath, scriptName);
            if (!File.Exists(scriptPath))
            {
                MessageBox.Show($"Could not find script at:\n{scriptPath}", "Missing Script", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            try
            {
                device.Hints.Clear();
                device.Hints.Add("<b>STATUS:</b> Scanning WMI via PowerShell...");
                mapCanvas.DrawMap(mapCanvas._currentState);
                string additionalArgs = "";

                if (scriptName.Contains("Linux"))
                    additionalArgs = $"-Password \"{decryptedPasswordSSH}\"";
                else if (scriptName.Contains("Non Domain"))
                    additionalArgs = $"-Password1 \"{decryptedPasswordSSH}\" -Password2 \"{decryptedPasswordManagers}\"";
                else if (scriptName.Contains("Default"))
                    additionalArgs = $"-Password1 \"{decryptedPasswordSSH}\" -Password2 \"{decryptedPasswordQMS}\"";

                string rawCommand = $"& \"{scriptPath}\" -IP \"{device.Address}\" {additionalArgs}";
                byte[] commandBytes = System.Text.Encoding.Unicode.GetBytes(rawCommand);
                string encodedCommand = Convert.ToBase64String(commandBytes);

                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-NoProfile -ExecutionPolicy Bypass -EncodedCommand {encodedCommand}",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using var process = System.Diagnostics.Process.Start(psi);
                if (process == null) return;

                string output = await process.StandardOutput.ReadToEndAsync();
                await process.WaitForExitAsync();

                // ==========================================
                // --- THE FIX: RPC FAILURE DETECTION ---
                // ==========================================
                if (string.IsNullOrWhiteSpace(output) || output.Contains("ERROR="))
                {
                    device.Hints.Clear();
                    device.Hints.Add("<b>STATUS:</b> WMI Blocked. Tunneling via PsExec...");
                    mapCanvas.DrawMap(mapCanvas._currentState);

                    // Trigger the fallback!
                    output = await PsExec.RunPsExecFallbackAsync(device.Address);

                    var lines = output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).ToList();
                    for (int i = 0; i < lines.Count; i++)
                    {
                        if (lines[i].StartsWith("USERNAME="))
                        {
                            string rawUser = lines[i].Substring(9).Trim();
                            if (!string.IsNullOrWhiteSpace(rawUser))
                            {
                                // Show a loading message so you know it's querying AD
                                device.Hints.Clear();
                                device.Hints.Add($"<b>STATUS:</b> Resolving AD User: {rawUser}...");
                                if (mapCanvas._currentState != null) mapCanvas.DrawMap(mapCanvas._currentState);
                                // Replace the raw username with the real AD Name!
                                string adName = await new ADName().ResolveADNameAsync(rawUser);
                                lines[i] = $"USERNAME={adName}";
                            }
                        }
                    }

                    // Re-assemble the text and hand it safely to the parser
                    output = string.Join("\n", lines);
                }

                if (string.IsNullOrWhiteSpace(output))
                {
                    MessageBox.Show("Both WMI and PsExec failed to return data.", "Scan Failed", MessageBoxButton.OK, MessageBoxImage.Error);
                    device.Hints.Clear();
                    device.Hints.Add("<b>STATUS:</b> Scan Failed");
                }
                else
                {
                    var parser = new ParseScriptOutput();
                    parser.ParseScriptOutputToHints(device, output);
                }

                // Mark unsaved changes via mapCanvas's state (adjust visibility if needed)
                // if mapCanvas.GlobalViewModel != null) mapCanvas.GlobalViewModel.HasUnsavedChanges = true;
                mapCanvas.DrawMap(mapCanvas._currentState);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to execute scripts:\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }



        public static async Task RunPrinterAutoFill(MainViewModel? GlobalViewModel, MapCanvasView mapCanvas, NetworkDevice device)
        {
            if (string.IsNullOrWhiteSpace(device.Address) || device.Address == "0.0.0.0") return;

            device.Hints.Clear();
            device.Hints.Add("<b>STATUS:</b> Scraping HP Web Interface...");
            mapCanvas.DrawMap(mapCanvas._currentState);

            // Run Selenium on a background thread so the UI doesn't freeze!
            var results = await Task.Run(() =>
            {
                var fetcher = new Helpers.WebFetcher.PrintersWebFetcher();
                return fetcher.FetchPrinters(device.Address);
            });

            if (results.ContainsKey("ERROR"))
            {
                MessageBox.Show($"Web Scraper failed:\n{results["ERROR"]}", "Scan Failed", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (results.Count > 0)
            {
                device.Hints.Clear();
                device.Hints.Add($"<b>NAME:</b> {results.GetValueOrDefault("Model", "HP Printer")}");
                device.Hints.Add($"<b>MAC:</b> {results.GetValueOrDefault("MAC", "Unknown")}");
                device.Hints.Add($"<b>IP:</b> {device.Address}");
                device.Hints.Add($"<b>Host Name:</b> {results.GetValueOrDefault("HostName", "Unknown")}");

                device.Titles.Clear();
                device.Titles.Add("%Address");
                device.Titles.Add(results.GetValueOrDefault("Model", "HP Printer").Replace("HP LaserJet", "").Trim());
                device.Titles.Add(results.GetValueOrDefault("HostName", "Unknown"));

                device.HintImagePath = HintImagesPath + "\\HP LaserJet.png";

                if (GlobalViewModel != null) mapCanvas._currentState?.HasUnsavedChanges = true;
                mapCanvas.DrawMap(mapCanvas._currentState);
            }
        }

        public static async Task RunGrandstreamAutoFill(MainViewModel? GlobalViewModel, MapCanvasView mapCanvas, NetworkDevice device)
        {
            if (string.IsNullOrWhiteSpace(device.Address) || device.Address == "0.0.0.0") return;

            device.Hints.Clear();
            device.Hints.Add("<b>STATUS:</b> Scraping Grandstream Web Interface...");
            if (mapCanvas._currentState != null) mapCanvas.DrawMap(mapCanvas._currentState);

            var fetcher = new Helpers.WebFetcher.GrandstreamsWebFetcher();
            var results = await fetcher.FetchGrandstreamsAsync(device.Address);

            if (results.TryGetValue("ERROR", out string? errorMessage))
            {
                // Explicitly clear the scraping status if it failed
                device.Hints.Clear();
                device.Hints.Add("<b>STATUS:</b> Scan Failed");
                if (mapCanvas._currentState != null) mapCanvas.DrawMap(mapCanvas._currentState);

                MessageBox.Show($"Web Scraper failed:\n{errorMessage}", "Scan Failed", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }


            if (results.Count > 0)
            {
                string model = results.GetValueOrDefault("Model", "Grandstream Device");
                string mac = results.GetValueOrDefault("MAC", "Unknown");
                string firmware = results.GetValueOrDefault("Firmware", "Unknown");
                string sipNumber = results.GetValueOrDefault("Number", "unknown").Trim();

                device.Hints.Clear();
                device.Hints.Add($"<b>MODEL:</b> {model}");
                device.Hints.Add($"<b>MAC:</b> {mac}");
                device.Hints.Add($"<b>IP:</b> {device.Address}");
                device.Hints.Add($"<b>FIRMWARE:</b> {firmware}");

                device.Titles.Clear();
                device.Titles.Add(sipNumber);

                if (model.Contains("GXP2170"))
                    device.HintImagePath = HintImagesPath + "\\GXP2170.png";
                else if (model.Contains("DP750"))
                    device.HintImagePath = HintImagesPath + "\\DP750.png";
                else if (model.Contains("GXP1628"))
                    device.HintImagePath = HintImagesPath + "\\GXP1628.png";

                GlobalViewModel?.HasUnsavedChanges = true;
                if (mapCanvas._currentState != null) mapCanvas.DrawMap(mapCanvas._currentState);
            }
        }
    }
}
