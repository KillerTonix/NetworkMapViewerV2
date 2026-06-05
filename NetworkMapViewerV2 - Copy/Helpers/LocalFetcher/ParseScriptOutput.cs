using NetworkMapViewerV2.Models;
using NetworkMapViewerV2.Services;
using System.Windows;

namespace NetworkMapViewerV2.Helpers.LocalFetcher
{
    internal class ParseScriptOutput
    {
        private static AppSettings settings = SettingsService.Load();

        private static string HintImagesPath = settings.HintImagesPath ?? "";

        public void ParseScriptOutputToHints(NetworkDevice device, string scriptOutput)
        {
            var lines = scriptOutput.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);

            // 1. PARSE INTO TEMPORARY VARIABLES FIRST
            string name = "", mac = "", cpu = "", ram = "", ssd = "", gpu = "", os = "", username = "";
            bool hasError = false;
            string errorMessage = "";

            foreach (var line in lines)
            {
                if (line.StartsWith("ERROR="))
                {
                    hasError = true;
                    errorMessage = line[6..].Trim();
                    break; // Stop parsing, we hit a wall!
                }

                if (line.StartsWith("NAME=")) name = line[5..].Trim();
                else if (line.StartsWith("MAC=")) mac = line[4..].Trim();
                else if (line.StartsWith("CPU=")) cpu = line[4..].Trim();
                else if (line.StartsWith("RAM=")) ram = line[4..].Trim();
                else if (line.StartsWith("SSD=")) ssd = line[4..].Trim();
                else if (line.StartsWith("GRAPHIC=")) gpu = line[8..].Trim();
                else if (line.StartsWith("OS=")) os = line[3..].Trim();
                else if (line.StartsWith("USERNAME=")) username = line[9..].Trim();
            }

            // 2. VALIDATE: Did it fail, or return completely blank data?
            // (If 'name' and 'mac' are both empty, the script likely failed silently)
            if (hasError || (string.IsNullOrEmpty(name) && string.IsNullOrEmpty(mac)))
            {
                // Alert the user, but DO NOT touch the device properties!
                string failReason = hasError ? errorMessage : "The script returned no usable data.";
                MessageBox.Show($"Auto-Fill failed. Your previous data has been preserved.\n\nReason: {failReason}", "Scan Failed", MessageBoxButton.OK, MessageBoxImage.Warning);
                return; // Abort! The old data survives!
            }

            // 3. COMMIT: We have good data! Now it is safe to overwrite.
            device.Hints.Clear();
            device.HintImagePath = "";
            device.Titles.Clear();

            // Build the titles
            device.Titles.Add("%Address");
            if (!string.IsNullOrEmpty(name)) device.Titles.Add(name);
            if (!string.IsNullOrEmpty(username)) device.Titles.Add(username);

            // Build the hints
            device.Hints.Add($"<b>NAME:</b> {name}");
            device.Hints.Add($"<b>MAC:</b> {mac}");
            device.Hints.Add($"<b>IP:</b> {device.Address}");
            device.Hints.Add($"<b>CPU:</b> {cpu}");
            device.Hints.Add($"<b>RAM:</b> {ram}");
            device.Hints.Add($"<b>SSD:</b> {ssd}");
            device.Hints.Add($"<b>GRAPHIC:</b> {gpu}");
            device.Hints.Add($"<b>OS:</b> {os}");

            // Build the tooltip image path safely
            if (!string.IsNullOrEmpty(os))
            {
                if (os.Contains("Ubuntu"))
                    device.HintImagePath = HintImagesPath + "\\Ubuntu.png";
                else
                    device.HintImagePath = HintImagesPath + (os.Contains("11") ? "\\Win11.png" : "\\Win10.png");
            }
        }
    }
}
