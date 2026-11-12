using NetworkMapViewerV2.Models;

namespace NetworkMapViewerV2.Helpers
{
    /// <summary>
    /// Pure helpers the IPAM window uses to read and write device data in the same format the map editor uses:
    /// titles (name lines, with the "%Address" placeholder for the IP), hints (HTML lines such as "&lt;b&gt;NOTE:&lt;/b&gt; text"),
    /// placement of new devices on a map, the planned address blocks ("cut range"), and CSV escaping.
    /// </summary>
    public static class IpamHelper
    {
        public const string AddressPlaceholder = "%Address";
        public const string NotePrefix = "<b>NOTE:</b>";
        private const string NameSeparator = " / ";

        // ─── CUT RANGE  (planned address blocks per device kind) ──────────

        /// <summary>The blocks used until someone edits them in the Range Cut window.</summary>
        public static List<RangeCut> DefaultCutRanges() =>
        [
            new() { Label = "Printers", From = 10, To = 30 },
            new() { Label = "Phones", From = 50, To = 70 },
            new() { Label = "PCs / NUCs", From = 101, To = 140 },
        ];

        /// <summary>
        /// Which planned block an address belongs to, going by its last octet, or null when it is outside every block.
        /// Inside a range larger than a /24 the same blocks apply to every /24.
        /// </summary>
        public static RangeCut? FindRange(IEnumerable<RangeCut> ranges, uint address)
        {
            int last = (int)(address & 0xFF);
            return ranges.FirstOrDefault(r => last >= r.From && last <= r.To);
        }

        /// <summary>Short text such as "Printers .10-.30 · Phones .50-.70".</summary>
        public static string DescribeCutRanges(IEnumerable<RangeCut> ranges) =>
            string.Join(" · ", ranges.Select(r => $"{r.Label} .{r.From}-.{r.To}"));

        /// <summary>Returns what is wrong with the list (shown to the user), or null when it can be saved.</summary>
        public static string? ValidateCutRanges(IReadOnlyList<RangeCut> ranges)
        {
            if (ranges.Count == 0) return "Add at least one range.";

            foreach (var r in ranges)
            {
                string label = (r.Label ?? "").Trim();
                if (label.Length == 0) return "Every range needs a name.";
                if (label.Length > 100) return $"\"{label}\": the name is too long (100 characters at most).";
                if (r.From < 1 || r.From > 254 || r.To < 1 || r.To > 254)
                    return $"\"{label}\": use numbers from 1 to 254 (the last part of the IP address).";
                if (r.From > r.To) return $"\"{label}\": From ({r.From}) must not be bigger than To ({r.To}).";
            }

            var duplicate = ranges.GroupBy(r => r.Label.Trim(), StringComparer.OrdinalIgnoreCase).FirstOrDefault(g => g.Count() > 1);
            if (duplicate != null) return $"The name \"{duplicate.Key}\" is used more than once.";

            // One address can belong to only one block, so blocks must not overlap.
            var sorted = ranges.OrderBy(r => r.From).ToList();
            for (int i = 1; i < sorted.Count; i++)
            {
                if (sorted[i].From <= sorted[i - 1].To)
                    return $"\"{sorted[i - 1].Label.Trim()}\" (.{sorted[i - 1].From}-.{sorted[i - 1].To}) and \"{sorted[i].Label.Trim()}\" (.{sorted[i].From}-.{sorted[i].To}) overlap.";
            }

            return null;
        }

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
