using System.Globalization;
using System.Text.RegularExpressions;

namespace CallsignLookup.Services.Hrd
{
    // A field the fill report always shows (changed or not), so it can be
    // corrected before anything is written.
    public sealed record HrdEditableField(string Label, string Column);

    // The fill report's editing rules: which fields it shows, what a typed
    // value must look like, and what else changes with it (a new grid square
    // moves the latitude and longitude, a new country the DXCC number...).
    // Pure code, tested directly. The values end up as edits on the
    // HrdBatchItem, which win over what the planner worked out.
    public static partial class HrdQsoEdits
    {
        public static readonly HrdEditableField[] MainFields =
        [
            new("Name", "COL_NAME"), new("QTH", "COL_QTH"), new("Country", "COL_COUNTRY"), new("State", "COL_STATE"),
            new("US County", "COL_CNTY"), new("Grid square", "COL_GRIDSQUARE"), new("Latitude", "COL_LAT"),
            new("Longitude", "COL_LON"), new("CQ zone", "COL_CQZ"), new("ITU zone", "COL_ITUZ"),
            new("ARRL section", "COL_ARRL_SECT"), new("IOTA", "COL_IOTA"), new("IOTA island", "COL_IOTA_ISLAND_ID"),
            new("POTA", "COL_POTA_REF"), new("QSL via", "COL_QSL_VIA"), new("Comment", "COL_COMMENT"),
        ];

        // Columns a fill must never lose: HRD's own Lookup has been seen
        // clearing them, and the user's rule is to keep them.
        private static readonly (string Label, string Column)[] Protected =
        [
            ("POTA", "COL_POTA_REF"), ("IOTA", "COL_IOTA"), ("IOTA island", "COL_IOTA_ISLAND_ID"),
            ("IOTA island name", "COL_APP_HAMRADIODELUXE_ISLANDNAME"),
        ];

        [GeneratedRegex(@"^[A-Z]{2}-\d{3}$")]
        private static partial Regex IotaRef();

        [GeneratedRegex(@"^[A-Z0-9]{1,4}-\d{4,5}$")]
        private static partial Regex PotaRef();

        // ---- one typed value -------------------------------------------------------

        // The value to store for what was typed into a column's cell, or why
        // it can't be stored. original: the column's value in the log (its
        // type decides how an unknown column is read).
        public static (object? Value, string? Error) Parse(string column, string text, object? original)
        {
            text = text.Trim();
            if (text == "(blank)") text = "";
            switch (column)
            {
                case "COL_GRIDSQUARE" or "COL_MY_GRIDSQUARE":
                    if (text.Length == 0) return ("", null);
                    return Maidenhead.TryGetCenter(text, out _, out _) && text.Length is 4 or 6 or 8
                        ? (Maidenhead.Normalize(text), null)
                        : (null, "a grid square is 4 or 6 characters, like FN13 or FN13ah");
                case "COL_LAT" or "COL_MY_LAT":
                    return Number(text, -90, 90, "a latitude is a number from -90 to 90 (south is negative)");
                case "COL_LON" or "COL_MY_LON":
                    return Number(text, -180, 180, "a longitude is a number from -180 to 180 (west is negative)");
                case "COL_CQZ" or "COL_MY_CQ_ZONE":
                    return Whole(text, 1, 40, "a CQ zone is 1 to 40");
                case "COL_ITUZ" or "COL_MY_ITU_ZONE":
                    return Whole(text, 1, 90, "an ITU zone is 1 to 90");
                case "COL_IOTA":
                    text = text.ToUpperInvariant();
                    if (text is "" or "-NONE-") return ("", null);
                    return IotaRef().IsMatch(text) ? (text, null) : (null, "an IOTA reference looks like NA-062");
                case "COL_POTA_REF" or "COL_MY_POTA_REF":
                {
                    var refs = text.ToUpperInvariant().Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                    return refs.All(r => PotaRef().IsMatch(r))
                        ? (string.Join(",", refs), null)
                        : (null, "POTA references look like US-1234, with a comma between several");
                }
                case "COL_IOTA_ISLAND_ID":
                {
                    // "4766 - Key West" or 4766.
                    if (text.Length == 0) return (null, null);
                    string id = text.Split(" - ", 2)[0].Trim();
                    return long.TryParse(id, NumberStyles.Integer, CultureInfo.InvariantCulture, out long n) && n > 0
                        ? (n, null)
                        : (null, "pick an island from the list (its number comes first)");
                }
                case "COL_COUNTRY":
                    if (text.Length == 0) return (null, "the country can't be blank");
                    return HrdCountries.Dxcc(text) is int dxcc
                        ? (HrdCountries.Name(dxcc) ?? text, null)
                        : (null, "not one of HRD's country names - pick one from the list");
                case "COL_ARRL_SECT":
                    if (text.Length == 0) return ("", null);
                    text = text.ToUpperInvariant();
                    return SectionKnown(text) ? (text, null) : (null, "not an ARRL or RAC section - pick one from the list");
                case "COL_STATE" or "COL_MY_STATE":
                    return (text.ToUpperInvariant(), null);
            }

            return original switch
            {
                long or int => text.Length == 0 ? (null, null)
                    : long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out long n) ? (n, null)
                    : (null, "a whole number"),
                double or float => text.Length == 0 ? (null, null)
                    : double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double d) ? (d, null)
                    : (null, "a number"),
                _ => (text, null),
            };
        }

        internal static Func<string, bool> SectionKnown { get; set; } =
            s => ArrlSectionService.AllSections().Contains(s, StringComparer.OrdinalIgnoreCase);

        private static (object?, string?) Number(string text, double min, double max, string error)
        {
            if (text.Length == 0) return (null, null);
            return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double d) && d >= min && d <= max
                ? (d, null)
                : (null, error);
        }

        private static (object?, string?) Whole(string text, int min, int max, string error)
        {
            if (text.Length == 0) return (null, null);
            return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) && n >= min && n <= max
                ? (n, null)
                : (null, error);
        }

        // ---- applying an edit ------------------------------------------------------

        // Sets a column of the QSO to what the user typed, and whatever goes
        // with it. Returns why it can't, or null once it's set.
        public static string? Edit(HrdBatchItem item, string label, string column, string text, Func<int, string?> continentFor)
        {
            var row = item.Row ?? new Dictionary<string, object?>();
            var (value, error) = Parse(column, text, row.GetValueOrDefault(column));
            if (error != null) return error;

            // An SK note stays: in this field, or moved to the other one.
            if (column is "COL_COMMENT" or "COL_QSL_VIA")
            {
                string other = column == "COL_COMMENT" ? "COL_QSL_VIA" : "COL_COMMENT";
                if (HrdQsoFiller.IsSilentKeyNote(Text(row.GetValueOrDefault(column))) &&
                    !HrdQsoFiller.IsSilentKeyNote(Text(value)) &&
                    !HrdQsoFiller.IsSilentKeyNote(Text(item.Current(other))))
                    return "this QSO's SK note has to stay (here or in " + (other == "COL_COMMENT" ? "Comment" : "QSL via") + ")";
            }

            item.SetEdit(label, column, value);
            Linked(item, column, value, continentFor);
            return null;
        }

        private static void Linked(HrdBatchItem item, string column, object? value, Func<int, string?> continentFor)
        {
            switch (column)
            {
                case "COL_GRIDSQUARE":
                {
                    string grid = Text(value);
                    // A position already inside the new square stays; otherwise the square's centre.
                    if (Maidenhead.TryGetCenter(grid, out double lat, out double lon) &&
                        !(item.Current("COL_LAT") is double la && item.Current("COL_LON") is double lo &&
                          Maidenhead.ToGridSquare(la, lo).StartsWith(grid, StringComparison.OrdinalIgnoreCase)))
                    {
                        item.SetEdit("Latitude", "COL_LAT", Math.Round(lat, 6));
                        item.SetEdit("Longitude", "COL_LON", Math.Round(lon, 6));
                    }
                    Distance(item);
                    break;
                }
                case "COL_LAT" or "COL_LON":
                {
                    if (item.Current("COL_LAT") is double lat && item.Current("COL_LON") is double lon && !(lat == 0 && lon == 0))
                    {
                        string grid = Maidenhead.ToGridSquare(lat, lon);
                        string now = Text(item.Current("COL_GRIDSQUARE"));
                        if (!now.StartsWith(grid, StringComparison.OrdinalIgnoreCase))
                            item.SetEdit("Grid square", "COL_GRIDSQUARE", Maidenhead.Normalize(grid));
                    }
                    Distance(item);
                    break;
                }
                case "COL_MY_GRIDSQUARE":
                    Distance(item);
                    break;
                case "COL_COUNTRY":
                    if (HrdCountries.Dxcc(Text(value)) is int dxcc)
                    {
                        item.SetEdit("DXCC", "COL_DXCC", dxcc.ToString(CultureInfo.InvariantCulture));
                        if (continentFor(dxcc) is string continent) item.SetEdit("Continent", "COL_CONT", continent);
                    }
                    break;
                case "COL_IOTA":
                    // As HRD does: a new reference empties the island.
                    if (!HrdRowMapper.SameValue(item.Row?.GetValueOrDefault("COL_IOTA"), value, 0))
                    {
                        item.SetEdit("IOTA island", "COL_IOTA_ISLAND_ID", null);
                        item.SetEdit("IOTA island name", "COL_APP_HAMRADIODELUXE_ISLANDNAME", "");
                    }
                    break;
                case "COL_IOTA_ISLAND_ID":
                {
                    string id = Text(value);
                    string name = id.Length == 0 ? ""
                        : IotaService.IslandsOf(Text(item.Current("COL_IOTA"))).FirstOrDefault(i => i.Id == id).Name ?? "";
                    item.SetEdit("IOTA island name", "COL_APP_HAMRADIODELUXE_ISLANDNAME", name);
                    break;
                }
            }
        }

        // HRD's Recalc for the QSO as it now stands.
        private static void Distance(HrdBatchItem item)
        {
            if (HrdRowMapper.Recalc(Text(item.Current("COL_MY_GRIDSQUARE")), Text(item.Current("COL_GRIDSQUARE"))) is var (km, bearing))
            {
                item.SetEdit("Distance (km)", "COL_DISTANCE", km, 0.01);
                item.SetEdit("Bearing", "COL_ANT_AZ", bearing, 0.05);
            }
        }

        // ---- the report's rows ------------------------------------------------------

        public sealed record ReportRow(string Label, string Column, object? Was, object? Now, bool Changes, bool Ticked);

        // One row per column the QSO's fill touches (ticked or not), then the
        // main fields it leaves alone, so any of them can be corrected.
        public static List<ReportRow> Rows(HrdBatchItem item)
        {
            var rows = new List<ReportRow>();
            var proposed = item.Proposed();
            foreach (var change in proposed)
                rows.Add(new ReportRow(change.Label, change.Column, item.Row?.GetValueOrDefault(change.Column) ?? change.Old,
                    change.New, true, !item.Excluded.Contains(change.Column)));
            foreach (var field in MainFields)
            {
                if (proposed.Any(c => c.Column == field.Column)) continue;
                object? value = item.Row?.GetValueOrDefault(field.Column);
                rows.Add(new ReportRow(field.Label, field.Column, value, value, false, false));
            }
            return rows;
        }

        // ---- HRD's Lookup ----------------------------------------------------------

        // What to put back after HRD's own Lookup ran on a QSO: a POTA or IOTA
        // value it cleared or changed, and an SK note it dropped. snapshot is
        // the QSO before the Lookup, current after it. New = the value to put back.
        public static List<HrdColumnChange> Restores(IReadOnlyDictionary<string, object?> snapshot, IReadOnlyDictionary<string, object?> current)
        {
            var restores = new List<HrdColumnChange>();
            foreach (var (label, column) in Protected)
            {
                object? before = snapshot.GetValueOrDefault(column), after = current.GetValueOrDefault(column);
                if (Text(before).Length > 0 && !HrdRowMapper.SameValue(before, after, 1e-9))
                    restores.Add(new HrdColumnChange(label, column, after, before));
            }
            foreach (var (label, column) in new[] { ("Comment", "COL_COMMENT"), ("QSL via", "COL_QSL_VIA") })
            {
                object? before = snapshot.GetValueOrDefault(column), after = current.GetValueOrDefault(column);
                if (HrdQsoFiller.IsSilentKeyNote(Text(before)) && !HrdQsoFiller.IsSilentKeyNote(Text(after)))
                    restores.Add(new HrdColumnChange(label, column, after, before));
            }
            if (Text(snapshot.GetValueOrDefault("COL_SILENT_KEY")) == "1" && Text(current.GetValueOrDefault("COL_SILENT_KEY")) != "1")
                restores.Add(new HrdColumnChange("Silent Key", "COL_SILENT_KEY", current.GetValueOrDefault("COL_SILENT_KEY"), "1"));
            return restores;
        }

        // How a value is shown in an editable cell ("" for nothing).
        public static string EditText(string column, object? value, IReadOnlyDictionary<string, object?>? row = null)
        {
            if (column == "COL_IOTA_ISLAND_ID" && Text(value) is { Length: > 0 } id)
            {
                string refNo = Text(row?.GetValueOrDefault("COL_IOTA"));
                string name = IotaService.IslandsOf(refNo).FirstOrDefault(i => i.Id == id).Name ?? "";
                return name.Length > 0 ? $"{id} - {name}" : id;
            }
            return value switch
            {
                double d => d.ToString("0.######", CultureInfo.InvariantCulture),
                _ => Text(value),
            };
        }

        internal static string Text(object? value) => value switch
        {
            null => "",
            string s => s.Trim(),
            double d => d.ToString("R", CultureInfo.InvariantCulture),
            _ => Convert.ToString(value, CultureInfo.InvariantCulture)?.Trim() ?? "",
        };
    }
}
