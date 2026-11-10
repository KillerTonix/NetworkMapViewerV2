namespace NetworkMapViewerV2.Helpers
{
    /// <summary>
    /// Pure helpers the IPAM window uses to read and write device data in the same format the map editor uses:
    /// titles (name lines, with the "%Address" placeholder for the IP), hints (HTML lines such as "&lt;b&gt;NOTE:&lt;/b&gt; text"),
    /// placement of new devices on a map, and CSV escaping.
    /// </summary>
    /// <summary>The planned address blocks of a branch network.</summary>
    public enum IpPool { None, Printers, Phones, Computers }

    public static class IpamHelper
    {
        public const string AddressPlaceholder = "%Address";
        public const string NotePrefix = "<b>NOTE:</b>";
        private const string NameSeparator = " / ";

        // ─── CUT RANGE  (planned address blocks per device kind) ──────────

        /// <summary>
        /// Branch networks keep one fixed block of the last octet per kind of device. With "cut range" on, the IPAM grid
        /// shows only these blocks. Inside a range larger than a /24 the same blocks repeat in every /24.
        /// </summary>
        public static readonly (IpPool Pool, int From, int To, string Label)[] CutRanges =
        [
            (IpPool.Printers, 10, 30, "Printers"),
            (IpPool.Phones, 50, 70, "Phones"),
            (IpPool.Computers, 101, 140, "PCs / NUCs"),
        ];

        /// <summary>Which planned block an address belongs to, going by its last octet (IpPool.None = outside every block).</summary>
        public static IpPool GetPool(uint address)
        {
            int last = (int)(address & 0xFF);
            foreach (var range in CutRanges)
            {
                if (last >= range.From && last <= range.To) return range.Pool;
            }
            return IpPool.None;
        }

        public static string PoolLabel(IpPool pool) =>
            CutRanges.FirstOrDefault(r => r.Pool == pool).Label ?? "";

        // ─── NAME  (stored in Titles) ─────────────────────────────────────

        /// <summary>Readable device name: the title lines without the "%Address" placeholder, joined with " / ".</summary>
        public static string ExtractName(IEnumerable<string?> titles)
        {
            var parts = titles.SelectMany(t => (t ?? "")
                .Replace(AddressPlaceholder, "", StringComparison.OrdinalIgnoreCase)
                .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
            return string.Join(NameSeparator, parts);
        }

        /// <summary>
        /// New title list for a device renamed to <paramref name="name"/>. If the old titles showed the IP through the
        /// "%Address" placeholder, that line is kept first, so the device still shows its IP on the map.
        /// </summary>
        public static List<string> BuildTitles(IEnumerable<string?> existingTitles, string? name)
        {
            bool showsAddress = existingTitles.Any(t => t != null && t.Contains(AddressPlaceholder, StringComparison.OrdinalIgnoreCase));

            var result = new List<string>();
            if (showsAddress) result.Add(AddressPlaceholder);

            foreach (var line in (name ?? "").Split(NameSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                result.Add(line);
            }
            return result;
        }

        // ─── NOTE  (stored as one hint line) ──────────────────────────────

        private static bool IsNoteLine(string? hint) =>
            hint != null && hint.TrimStart().StartsWith(NotePrefix, StringComparison.OrdinalIgnoreCase);

        public static string ExtractNote(IEnumerable<string?> hints)
        {
            var line = hints.FirstOrDefault(IsNoteLine);
            return line == null ? "" : line.TrimStart()[NotePrefix.Length..].Trim();
        }

        /// <summary>Hints with the note line replaced (or added, or removed when the note is empty). Other hint lines are untouched.</summary>
        public static List<string> BuildHints(IEnumerable<string?> existingHints, string? note)
        {
            var result = new List<string>();
            int noteIndex = -1;

            foreach (var hint in existingHints)
            {
                if (hint == null) continue;
                if (IsNoteLine(hint))
                {
                    if (noteIndex < 0) noteIndex = result.Count; // keep the note where it was
                    continue;
                }
                result.Add(hint);
            }

            if (!string.IsNullOrWhiteSpace(note))
            {
                result.Insert(noteIndex < 0 ? result.Count : noteIndex, $"{NotePrefix} {note.Trim()}");
            }
            return result;
        }

        // ─── PLACEMENT ────────────────────────────────────────────────────

        /// <summary>
        /// First free spot on a map for a new device: scans a grid left-to-right, top-to-bottom and skips spots that are
        /// too close to an existing device, so devices created from IPAM never stack on top of each other.
        /// </summary>
        public static (double Left, double Top) FindFreeSlot(IEnumerable<(double Left, double Top)> existing)
        {
            const double startX = 50, startY = 50, stepX = 110, stepY = 100, maxX = 1000;
            var taken = existing.ToList();

            for (double y = startY; ; y += stepY)
            {
                for (double x = startX; x <= maxX; x += stepX)
                {
                    bool occupied = taken.Any(t => Math.Abs(t.Left - x) < stepX - 10 && Math.Abs(t.Top - y) < stepY - 10);
                    if (!occupied) return (x, y);
                }
            }
        }

        // ─── CSV ──────────────────────────────────────────────────────────

        /// <summary>
        /// Escapes one CSV field. Values starting with = + - @ get a leading apostrophe so a spreadsheet never runs
        /// them as a formula.
        /// </summary>
        public static string CsvEscape(string? value)
        {
            string text = value ?? "";
            if (text.Length > 0 && text[0] is '=' or '+' or '-' or '@') text = "'" + text;

            return text.IndexOfAny([',', '"', '\r', '\n']) >= 0
                ? "\"" + text.Replace("\"", "\"\"") + "\""
                : text;
        }
    }
}
