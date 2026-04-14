using NetworkMapViewerV2.Models;
using System;
using System.Diagnostics;
using System.Windows;

namespace NetworkMapViewerV2.Helpers
{
    public static class CommandHelper
    {
        public static void ExecuteExternalCommand(ExternalCommand command, string address)
        {
            if (command == null || string.IsNullOrWhiteSpace(command.Path) || string.IsNullOrWhiteSpace(address))
                return;

            try
            {
                // Support both {Address} and %Address depending on how your commands were set up
                string args = command.Arguments?.Replace("{Address}", address).Replace("%Address", address) ?? "";

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