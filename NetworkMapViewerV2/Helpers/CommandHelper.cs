using NetworkMapViewerV2.Helpers.Passwords;
using NetworkMapViewerV2.Models;
using NetworkMapViewerV2.Services;
using NetworkMapViewerV2.Views;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Windows;

namespace NetworkMapViewerV2.Helpers
{
    public static class CommandHelper
    {
        // System variables that should not be treated as custom user prompts
        private static readonly HashSet<string> KnownBaseVariables = new(StringComparer.OrdinalIgnoreCase)
        {
            "Address",
            "DatabasePassword",
            "PrinterPassword",
            "GrandstreamPassword",
            "VNCPassword",
            "SSHPassword",
            "ManagersPCPassword",
            "QMSPassword"
        };

        public static void ExecuteExternalCommand(ExternalCommand command, string address)
        {
            if (command == null || string.IsNullOrWhiteSpace(command.Path) || string.IsNullOrWhiteSpace(address))
                return;

            try
            {
                AppSettings settings = SettingsService.Load();
                string decryptedPasswordVNC = SecureSettingsHelper.UnprotectPassword(settings.VNCPassword) ?? "";
                string decryptedPasswordSSH = SecureSettingsHelper.UnprotectPassword(settings.SSHPassword) ?? "";
                // Support both {Address} and %Address depending on how your commands were set up
                string args = command.Arguments?.Replace("{Address}", address).Replace("%Address", address).Replace("{VNCPassword}", decryptedPasswordVNC).Replace("{SSHPassword}", decryptedPasswordSSH) ?? "";
                
                var tempRunVariables = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                var matches = Regex.Matches(args, @"\{([a-zA-Z0-9_]+)\}");

                foreach (Match match in matches)
                {
                    string rawPlaceholder = match.Value;    // e.g. "{user}"
                    string varName = match.Groups[1].Value;   // e.g. "user"

                    if (KnownBaseVariables.Contains(varName)) continue;

                    // Check if variable value is already stored in user settings
                    if (!tempRunVariables.TryGetValue(varName, out string? customValue))
                    {
                        var inputDlg = new InputDialog($"Enter value for custom variable {{{varName}}}:", "Missing Argument");

                        if (inputDlg.ShowDialog() == true && !string.IsNullOrWhiteSpace(inputDlg.InputTextBox.Text))
                        {
                            customValue = inputDlg.InputTextBox.Text.Trim();
                            tempRunVariables[varName] = customValue;
                        }
                        else
                        {
                            // User cancelled the prompt; abort command execution
                            return;
                        }
                    }

                    // Replace custom placeholder with resolved value
                    args = args.Replace(rawPlaceholder, customValue, StringComparison.OrdinalIgnoreCase);
                }
                              

                Process.Start(new ProcessStartInfo
                {
                    FileName = command.Path,
                    Arguments = args,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to execute {command.Name}.\nMake sure the application path is correct.\n\nError: {ex.Message}",
                                "Command Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}