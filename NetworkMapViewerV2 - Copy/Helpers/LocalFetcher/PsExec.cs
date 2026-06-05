using NetworkMapViewerV2.Helpers.Passwords;
using NetworkMapViewerV2.Models;
using NetworkMapViewerV2.Services;
using System.Diagnostics;
using System.IO;

namespace NetworkMapViewerV2.Helpers.LocalFetcher
{
    public class PsExec
    {
        private static AppSettings settings = SettingsService.Load();

        private static string ScriptsPath = settings.ScriptsPath ?? "";
        private static string decryptedPasswordSSH = SecureSettingsHelper.UnprotectPassword(settings.SSHPassword) ?? "";
        private static string decryptedPasswordManagers = SecureSettingsHelper.UnprotectPassword(settings.ManagersPCPassword) ?? "";
        private static string decryptedPasswordQMS = SecureSettingsHelper.UnprotectPassword(settings.QMSPassword) ?? "";

        public static async Task<string> RunPsExecFallbackAsync(string ipAddress)
        {
            string localScriptPath = Path.Combine(ScriptsPath, "SystemInfo PSExec.ps1");
            string psExecPath = Path.Combine(ScriptsPath, "psexec.exe");

            if (!File.Exists(localScriptPath) || !File.Exists(psExecPath))
            {
                return "ERROR=PsExec.exe or 'SystemInfo PSExec.ps1' not found in Scripts folder.";
            }

            try
            {
                string scriptContent = await File.ReadAllTextAsync(localScriptPath);
                string encodedCommand = Convert.ToBase64String(System.Text.Encoding.Unicode.GetBytes(scriptContent));

                // 1. Initial attempt (runs under current context / SYSTEM)
                string baseArgs = $"-s -accepteula -n 15 powershell.exe -NoProfile -ExecutionPolicy Bypass -EncodedCommand {encodedCommand}";
                // 1. Initial attempt (runs under current context / SYSTEM)
                var (output, error, exitCode) = await ExecuteProcessAsync(psExecPath, $"\\\\{ipAddress} {baseArgs}");

                // 2. Define the passwords you want to try, in order of priority
                string[] fallbackPasswords = [
                    decryptedPasswordSSH,
                    decryptedPasswordManagers,
                    decryptedPasswordQMS
                ];

                // 3. Loop through the fallbacks if the previous attempt was an auth error
                foreach (string password in fallbackPasswords)
                {
                    // If it is NOT an auth error, it means we succeeded (or hit a totally different error). 
                    // Either way, stop trying passwords.
                    if (!IsAuthError(exitCode, error))
                    {
                        break;
                    }

                    // Try the next password
                    string credentialArgs = $"-u administrator -p \"{password}\" ";
                    var fallbackResult = await ExecuteProcessAsync(psExecPath, $"\\\\{ipAddress} {credentialArgs}{baseArgs}");

                    // Update the variables for the next loop iteration
                    output = fallbackResult.Output;
                    error = fallbackResult.Error;
                    exitCode = fallbackResult.ExitCode;
                }

                // --- Local Helper Function (Put this inside your method, or right below it) ---
                bool IsAuthError(int code, string errText)
                {
                    return code == 1326 || code == 5 ||
                           errText.Contains("user name or password is incorrect", StringComparison.OrdinalIgnoreCase) ||
                           errText.Contains("Access is denied", StringComparison.OrdinalIgnoreCase);
                }

                // Clean up output logic
                if (string.IsNullOrWhiteSpace(output) || output.Contains("ERROR="))
                {
                    return $"ERROR=PsExec returned no data. Error Stream: {error.Trim()}";
                }

                return output;
            }
            catch (Exception ex)
            {
                return $"ERROR=PsExec Failure: {ex.Message}";
            }
        }

        /// <summary>
        /// Helper to run a process and read Output/Error streams concurrently to prevent deadlocks.
        /// </summary>
        private static async Task<(string Output, string Error, int ExitCode)> ExecuteProcessAsync(string fileName, string arguments)
        {
            var psi = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process == null) return (string.Empty, "Failed to start process.", -1);

            // Read concurrently to avoid deadlocks with PsExec buffer limits
            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();

            await Task.WhenAll(outputTask, errorTask, process.WaitForExitAsync());

            return (outputTask.Result, errorTask.Result, process.ExitCode);
        }
    }
}
