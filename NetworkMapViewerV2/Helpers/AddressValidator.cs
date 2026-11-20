using System.Net;
using System.Text.RegularExpressions;

namespace NetworkMapViewerV2.Helpers
{
    /// <summary>
    /// An address is safe if it's an IP address or a plain hostname (letters, digits, dot, hyphen, underscore).
    /// Anything else (quotes, spaces, ; & | etc.) could be used to inject commands into PowerShell/PsExec.
    /// </summary>
    public static partial class AddressValidator
    {
        [GeneratedRegex(@"^[A-Za-z0-9][A-Za-z0-9._-]{0,252}$")]
        private static partial Regex HostnameRegex();

        public static bool IsSafe(string? address) =>
            !string.IsNullOrWhiteSpace(address) &&
            (IPAddress.TryParse(address, out _) || HostnameRegex().IsMatch(address));
    }
}