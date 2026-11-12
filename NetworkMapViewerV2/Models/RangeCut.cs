namespace NetworkMapViewerV2.Models
{
    /// <summary>
    /// One planned address block of a branch network, e.g. "Printers" = last octet 10 to 30 (x.10 - x.30).
    /// Edited in the Range Cut window and stored in dbo.RangeCuts.
    /// </summary>
    public class RangeCut
    {
        public int RangeCutId { get; set; }
        public string Label { get; set; } = "";

        /// <summary>First last-octet of the block (inclusive).</summary>
        public int From { get; set; }

        /// <summary>Last last-octet of the block (inclusive).</summary>
        public int To { get; set; }
    }
}
