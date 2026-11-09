using System.Globalization;

namespace NetworkMapViewerV2.Helpers
{
    public class IpRangeHelper
    {
        /// <summary>Smallest allowed prefix (/16 = 65,534 hosts). Anything wider is almost certainly a typo.</summary>
        public const int MinPrefix = 16;

        /// <summary>Strict dotted-quad parser: exactly four decimal octets, 0-255 (no "192.168.1" shorthand, no IPv6).</summary>
        public static bool TryParseIPv4(string? text, out uint value)
        {
            value = 0;
            if (string.IsNullOrWhiteSpace(text)) return false;

            var parts = text.Trim().Split('.');
            if (parts.Length != 4) return false;

            uint result = 0;
            foreach (var part in parts)
            {
                if (part.Length is 0 or > 3 ||
                    !byte.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out byte octet))
                {
                    return false;
                }
                result = (result << 8) | octet;
            }

            value = result;
            return true;
        }

        /// <summary>
        /// Parses "a.b.c.d/nn". Host bits are cleared, so "192.168.102.77/24" becomes network 192.168.102.0.
        /// </summary>
        public static bool TryParseCidr(string? text, out uint network, out int prefix, out string error)
        {
            network = 0;
            prefix = 0;
            error = "";

            var parts = (text ?? "").Trim().Split('/');
            if (parts.Length != 2 ||
                !TryParseIPv4(parts[0], out uint ip) ||
                !int.TryParse(parts[1].Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out prefix))
            {
                error = "Enter a range like 192.168.102.0/24.";
                return false;
            }

            if (prefix < MinPrefix || prefix > 32)
            {
                error = $"Prefix length must be between /{MinPrefix} and /32.";
                return false;
            }

            network = ip & MaskFor(prefix);
            return true;
        }

        public static uint MaskFor(int prefix) => prefix <= 0 ? 0u : uint.MaxValue << (32 - prefix);

        /// <summary>
        /// Assignable addresses in the range: everything except the network and broadcast addresses.
        /// /31 (point-to-point, RFC 3021) and /32 (single host) have no network/broadcast, so all addresses count.
        /// </summary>
        public static IEnumerable<uint> GetHostAddresses(uint network, int prefix)
        {
            uint broadcast = network | ~MaskFor(prefix);

            if (prefix >= 31)
            {
                // ulong counter so a range ending at 255.255.255.255 cannot overflow and loop forever.
                for (ulong a = network; a <= broadcast; a++) yield return (uint)a;
                yield break;
            }

            for (uint a = network + 1; a < broadcast; a++) yield return a;
        }

        public static long HostCount(int prefix) =>
            prefix >= 31 ? 1L << (32 - prefix) : (1L << (32 - prefix)) - 2;

        public static string ToIpString(uint value) =>
            $"{value >> 24}.{(value >> 16) & 255}.{(value >> 8) & 255}.{value & 255}";
    }
}
