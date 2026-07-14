using NetworkMapViewerV2.Helpers.Passwords;
using NetworkMapViewerV2.Models;
using NetworkMapViewerV2.Services;
using NetworkMapViewerV2.ViewModels;
using System.IO;
using System.Windows;

namespace NetworkMapViewerV2.Helpers.LocalFetcher
{
    internal class AutoFill
    {
        private static readonly AppSettings settings = SettingsService.Load();

        private static readonly string ScriptsPath = settings.ScriptsPath ?? "";
        private static readonly string HintImagesPath = settings.HintImagesPath ?? "";
        private static readonly string decryptedPasswordSSH = SecureSettingsHelper.UnprotectPassword(settings.SSHPassword) ?? "";
        private static readonly string decryptedPasswordManagers = SecureSettingsHelper.UnprotectPassword(settings.ManagersPCPassword) ?? "";
        private static readonly string decryptedPasswordQMS = SecureSettingsHelper.UnprotectPassword(settings.QMSPassword) ?? "";

        // REMOVED MapCanvasView mapCanvas from parameters
        public static async Task RunAutoFillScript(MainViewModel vm, NetworkDevice device, string scriptName)
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
                var oldHintText = device.Hints.ToList();
                device.Hints.Clear();
                device.Hints.Add("<b>STATUS:</b> Scanning WMI via PowerShell...");

                // THE NEW WAY TO REDRAW:
                vm.SelectedTab?.TriggerRedraw?.Invoke();

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

                System.Diagnostics.Process? process = null;
                string output = "";
                string err = "";

                try
                {
                    process = System.Diagnostics.Process.Start(psi);
                    if (process == null) return;

                    output = await process.StandardOutput.ReadToEndAsync();
                    err = await process.StandardError.ReadToEndAsync();

                    await process.WaitForExitAsync();
                }
                finally
                {
                    if (process != null)
                    {
                        if (!process.HasExited)
                        {
                            try { process.Kill(); } catch { }
                        }
                        process.Dispose();
                    }
                }

                if (scriptName.Contains("Linux") && output.Contains("refused"))
                {
                    device.Hints.Clear();
                    foreach (var oldLine in oldHintText)                    
                        device.Hints.Add(oldLine);                    

                    vm?.SelectedTab?.TriggerRedraw?.Invoke();
                    return;
                }

                if (string.IsNullOrWhiteSpace(output) || output.Contains("ERROR="))
                {
                    device.Hints.Clear();
                    device.Hints.Add("<b>STATUS:</b> WMI Blocked. Tunneling via PsExec...");
                    vm.SelectedTab?.TriggerRedraw?.Invoke();

                    output = await PsExec.RunPsExecFallbackAsync(device.Address);

                    var lines = output.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).ToList();
                    for (int i = 0; i < lines.Count; i++)
                    {
                        if (lines[i].StartsWith("USERNAME="))
                        {
                            string rawUser = lines[i][9..].Trim();
                            if (!string.IsNullOrWhiteSpace(rawUser))
                            {
                                device.Hints.Clear();
                                device.Hints.Add($"<b>STATUS:</b> Resolving AD User: {rawUser}...");
                                vm.SelectedTab?.TriggerRedraw?.Invoke();

                                string adName = await new ADName().ResolveADNameAsync(rawUser);
                                if (!adName.Contains("ERROR"))
                                {
                                    lines[i] = $"USERNAME={adName}";
                                }
                            }
                        }
                    }

                    output = string.Join("\n", lines);
                }

                if (string.IsNullOrWhiteSpace(output))
                {
                    // Optional: Comment this MessageBox out if you don't want popups during batch scans!
                    MessageBox.Show("Both WMI and PsExec failed to return data.", "Scan Failed", MessageBoxButton.OK, MessageBoxImage.Error);
                    device.Hints.Clear();
                    device.Hints.Add("<b>STATUS:</b> Scan Failed");
                }
                else
                {
                    var parser = new ParseScriptOutput();
                    parser.ParseScriptOutputToHints(device, output);

                    if (vm?.SelectedTab != null)
                    {
                        vm.SelectedTab.HasUnsavedChanges = true;
                    }
                }

                vm?.SelectedTab?.TriggerRedraw?.Invoke();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to execute scripts:\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }


        // REMOVED MapCanvasView mapCanvas from parameters
        public static async Task RunPrinterAutoFill(MainViewModel? GlobalViewModel, NetworkDevice device)
        {
            if (string.IsNullOrWhiteSpace(device.Address) || device.Address == "0.0.0.0") return;

            device.Hints.Clear();
            device.Hints.Add("<b>STATUS:</b> Scraping HP Web Interface...");
            GlobalViewModel?.SelectedTab?.TriggerRedraw?.Invoke();

            var results = await Task.Run(() =>
            {
                var fetcher = new Helpers.WebFetcher.PrintersWebFetcher();
                return fetcher.FetchPrinters(device.Address);
            });

            if (results.TryGetValue("ERROR", out string? value))
            {
                MessageBox.Show($"Web Scraper failed:\n{value}", "Scan Failed", MessageBoxButton.OK, MessageBoxImage.Warning);
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

                if (GlobalViewModel?.SelectedTab != null) GlobalViewModel.SelectedTab.HasUnsavedChanges = true;
                GlobalViewModel?.SelectedTab?.TriggerRedraw?.Invoke();
            }
        }


        // REMOVED MapCanvasView mapCanvas from parameters
        public static async Task RunGrandstreamAutoFill(MainViewModel? GlobalViewModel, NetworkDevice device)
        {
            if (string.IsNullOrWhiteSpace(device.Address) || device.Address == "0.0.0.0") return;

            device.Hints.Clear();
            device.Hints.Add("<b>STATUS:</b> Scraping Grandstream Web Interface...");
            GlobalViewModel?.SelectedTab?.TriggerRedraw?.Invoke();

            var fetcher = new WebFetcher.GrandstreamsWebFetcher();
            var results = await fetcher.FetchGrandstreamsAsync(device.Address);

            if (results.TryGetValue("ERROR", out string? errorMessage))
            {
                device.Hints.Clear();
                device.Hints.Add("<b>STATUS:</b> Scan Failed");
                GlobalViewModel?.SelectedTab?.TriggerRedraw?.Invoke();

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

                if (GlobalViewModel?.SelectedTab != null) GlobalViewModel.SelectedTab.HasUnsavedChanges = true;
                GlobalViewModel?.SelectedTab?.TriggerRedraw?.Invoke();
            }
        }
    }
}