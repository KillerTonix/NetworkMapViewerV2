namespace NetworkMapViewerV2.Models
{
    /// <summary>How a saved range is used. Head Office always shows its whole range; Branch can use the cut range.</summary>
    public enum SubnetKind { Branch, HeadOffice }

    /// <summary>A named CIDR range kept in the IPAM sidebar, e.g. "Montreal Branch" = 192.168.102.0/24.</summary>
    public class SavedSubnet
    {
        public int SubnetId { get; set; }
        public string Name { get; set; } = "";

        /// <summary>Normalised CIDR (host bits cleared), e.g. 192.168.28.0/22.</summary>
        public string Cidr { get; set; } = "";

        public SubnetKind Kind { get; set; } = SubnetKind.Branch;

        public string KindText => Kind == SubnetKind.HeadOffice ? "Head Office" : "Branch";

        /// <summary>Second line of the sidebar entry.</summary>
        public string Subtitle => $"{Cidr} · {KindText}";

        /// <summary>The value stored in the database.</summary>
        public static string KindToDb(SubnetKind kind) => kind == SubnetKind.HeadOffice ? "HeadOffice" : "Branch";

        public static SubnetKind KindFromDb(string? value) =>
            string.Equals(value, "HeadOffice", StringComparison.OrdinalIgnoreCase) ? SubnetKind.HeadOffice : SubnetKind.Branch;
    }
}
