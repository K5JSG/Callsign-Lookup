using System.Globalization;

namespace CallsignLookup.Services.Hrd
{
    // One column of one QSO changing. Label is what the preview calls it.
    public sealed record HrdColumnChange(string Label, string Column, object? Old, object? New);

    public sealed class HrdRowChanges
    {
        public List<HrdColumnChange> Changes { get; } = [];
        public List<string> Warnings { get; } = [];

        public bool Any => Changes.Count > 0;

        public Dictionary<string, object?> Columns => Changes.ToDictionary(c => c.Column, c => c.New);
    }

    // The batch fill's bridge between a QSO row in HRD's log and the planning
    // code written for HRD's Add/Edit window (HrdQsoFiller.Plan):
    //   * ToQsoFields turns a row into the dictionary of window-field values
    //     the planner reads, so the planner - and every rule in it - is the
    //     same for one QSO and for a batch;
    //   * ToColumnUpdates turns the planner's field changes into log columns,
    //     adds what HRD's window does by itself (distance and bearing,
    //     DXCC/continent with a new Country, the county it empties) and
    //     returns only what really differs from the row.
    // Pure code - no HRD, no database - so it's tested directly.
    public static class HrdRowMapper
    {
        // Window field id -> log column, for reading a row.
        private static readonly (string Id, string Column)[] FieldColumns =
        [
            ("edtCALL", "COL_CALL"), ("edtNAME", "COL_NAME"), ("edtQTH", "COL_QTH"), ("memCOMMENT", "COL_COMMENT"),
            ("edtPOTA_REF", "COL_POTA_REF"), ("edtPOTA_REF2", "COL_POTA_REF"),
            ("edtAPP_HAMRADIODELUXE_POTA_NAME", "COL_APP_HAMRADIODELUXE_POTA_NAME"),
            ("edtAPP_HAMRADIODELUXE_POTA_LAT", "COL_APP_HAMRADIODELUXE_POTA_LAT"),
            ("edtAPP_HAMRADIODELUXE_POTA_LON", "COL_APP_HAMRADIODELUXE_POTA_LON"),
            ("cbxCOUNTRY", "COL_COUNTRY"), ("edtSTATE", "COL_STATE"), ("cbxLocationStateProvince", "COL_STATE"),
            ("edtCNTY", "COL_CNTY"), ("edtLAT", "COL_LAT"), ("edtLON", "COL_LON"), ("edtGRIDSQUARE", "COL_GRIDSQUARE"),
            ("edtCQZ", "COL_CQZ"), ("edtITUZ", "COL_ITUZ"), ("cbxARRLSECT", "COL_ARRL_SECT"), ("edtQSL_VIA", "COL_QSL_VIA"),
            ("edtMY_GRIDSQUARE", "COL_MY_GRIDSQUARE"), ("edtMY_LAT", "COL_MY_LAT"), ("edtMY_LON", "COL_MY_LON"),
            ("edtMY_CITY", "COL_MY_CITY"), ("edtSTATION_CALLSIGN", "COL_STATION_CALLSIGN"),
        ];

        // Window fields the planner can change that the log doesn't keep (HRD
        // shows them but doesn't store them, or they're another view of a
        // column that's written anyway).
        private static readonly HashSet<string> NotStored =
            ["edtPOTA_REF2", "edtPOTACountry", "edtPOTAGrid", "cbxLocationStateProvince"];

        private static readonly Dictionary<string, string> WriteColumn =
            FieldColumns.Where(f => !NotStored.Contains(f.Id)).ToDictionary(f => f.Id, f => f.Column);

        // My Station profile value -> log column ('S' text, 'I' whole number,
        // 'D' decimal). The registry names and columns were proven by the
        // one-off profile fixes of 2026-10-04.
        private static readonly (string Name, string Column, char Type)[] ProfileColumns =
        [
            ("Altitude", "COL_MY_ALTITUDE", 'D'), ("Antennas", "COL_MY_ANTENNA", 'S'), ("ARRLSECT", "COL_MY_ARRL_SECT", 'S'),
            ("City", "COL_MY_CITY", 'S'), ("County", "COL_MY_CNTY", 'S'), ("Country", "COL_MY_COUNTRY", 'S'),
            ("cqzone", "COL_MY_CQ_ZONE", 'I'), ("FistsID", "COL_MY_FISTS", 'I'), ("Locator", "COL_MY_GRIDSQUARE", 'S'),
            ("ituzone", "COL_MY_ITU_ZONE", 'I'), ("Latitude", "COL_MY_LAT", 'D'), ("Longitude", "COL_MY_LON", 'D'),
            ("Name", "COL_MY_NAME", 'S'), ("Zip", "COL_MY_POSTAL_CODE", 'S'), ("Equipment", "COL_MY_RIG", 'S'),
            ("SigInfo", "COL_MY_SIG_INFO", 'S'), ("Sig", "COL_MY_SIG", 'S'), ("SOTA", "COL_MY_SOTA_REF", 'S'),
            ("State", "COL_MY_STATE", 'S'), ("Street", "COL_MY_STREET", 'S'), ("USACA_COUNTIES", "COL_MY_USACA_COUNTIES", 'S'),
            ("VUCC_GRIDS", "COL_MY_VUCC_GRIDS", 'S'), ("WWFF", "COL_MY_WWFF_REF", 'S'), ("Operator", "COL_OPERATOR", 'S'),
            ("Callsign", "COL_STATION_CALLSIGN", 'S'), ("POTA", "COL_MY_POTA_REF", 'S'),
            ("License", "COL_APP_HAMRADIODELUXE_LICENSE", 'S'),
        ];

        // ---- row -> planner input ---------------------------------------------

        public static Dictionary<string, string> ToQsoFields(IReadOnlyDictionary<string, object?> row)
        {
            var fields = new Dictionary<string, string>();
            foreach (var (id, column) in FieldColumns)
                fields[id] = Text(row, column);

            string iota = Text(row, "COL_IOTA");
            fields["edtIOTARefNo"] = iota.Length > 0 ? iota : "-none-";

            string islandId = Text(row, "COL_IOTA_ISLAND_ID"), islandName = Text(row, "COL_APP_HAMRADIODELUXE_ISLANDNAME");
            fields["edtIOTAIsland"] = islandId.Length == 0 ? "" : islandName.Length > 0 ? $"{islandId} - {islandName}" : islandId;

            fields["chkSILENT_KEY"] = Text(row, "COL_SILENT_KEY") == "1" ? "Yes" : "No";
            return fields;
        }

        // ---- planner output -> columns ------------------------------------------

        // continentFor: the continent HRD gives a DXCC entity (the log's own
        // most common value for it), null if unknown.
        public static HrdRowChanges ToColumnUpdates(
            HrdFillPlan plan, IReadOnlyDictionary<string, object?> row, Func<int, string?> continentFor)
        {
            var result = new HrdRowChanges();
            result.Warnings.AddRange(plan.Warnings);
            var now = new Dictionary<string, object?>(row);

            // Sets a column; only a value that differs from what the row had
            // counts as a change (setting it back removes the change).
            void Set(string label, string column, object? value, double tolerance = 1e-6)
            {
                row.TryGetValue(column, out object? original);
                int index = result.Changes.FindIndex(c => c.Column == column);
                if (SameValue(original, value, tolerance))
                {
                    if (index >= 0) result.Changes.RemoveAt(index);
                    now[column] = original;
                    return;
                }
                var change = new HrdColumnChange(label, column, original, value);
                if (index >= 0) result.Changes[index] = change;
                else result.Changes.Add(change);
                now[column] = value;
            }

            foreach (var change in plan.Changes)
            {
                switch (change.ReadId)
                {
                    case "edtIOTARefNo":
                    {
                        string refNo = change.New == "-none-" ? "" : change.Typed.Length > 0 ? change.Typed : change.New;
                        Set(change.Label, "COL_IOTA", refNo);
                        // HRD empties the island when the reference changes; an
                        // island change after this one sets it again.
                        Set("IOTA island", "COL_IOTA_ISLAND_ID", null);
                        Set("IOTA island name", "COL_APP_HAMRADIODELUXE_ISLANDNAME", "");
                        break;
                    }
                    case "edtIOTAIsland":
                    {
                        // "9292 - Great Britain" (typed: 9292).
                        string name = change.New.StartsWith(change.Typed + " - ", StringComparison.Ordinal)
                            ? change.New[(change.Typed.Length + 3)..]
                            : "";
                        if (long.TryParse(change.Typed, NumberStyles.Integer, CultureInfo.InvariantCulture, out long id))
                        {
                            Set(change.Label, "COL_IOTA_ISLAND_ID", id);
                            Set("IOTA island name", "COL_APP_HAMRADIODELUXE_ISLANDNAME", name);
                        }
                        else
                        {
                            result.Warnings.Add($"IOTA island \"{change.New}\" has no island number - left as it is.");
                        }
                        break;
                    }
                    case "cbxCOUNTRY":
                    {
                        string country = change.Typed.Length > 0 ? change.Typed : change.New;
                        Set("Country", "COL_COUNTRY", country);
                        if (HrdCountries.Dxcc(country) is int dxcc)
                        {
                            Set("DXCC", "COL_DXCC", dxcc.ToString(CultureInfo.InvariantCulture));
                            if (continentFor(dxcc) is string continent) Set("Continent", "COL_CONT", continent);
                            else result.Warnings.Add($"The log has no QSO with {country} to take its continent from - Continent left as it is.");
                        }
                        else
                        {
                            result.Warnings.Add($"{country} isn't on this app's list of HRD's countries - DXCC and Continent left as they are.");
                        }
                        break;
                    }
                    case "chkSILENT_KEY":
                        Set(change.Label, "COL_SILENT_KEY", "1");
                        break;
                    default:
                        if (WriteColumn.TryGetValue(change.ReadId, out string? column))
                            Set(change.Label, column, Typed(column, change.New));
                        else if (!NotStored.Contains(change.ReadId))
                            result.Warnings.Add($"{change.Label}: this app doesn't know where the log keeps it - left as it is.");
                        break;
                }
            }

            // Home values the planner couldn't set. HRD's window empties the
            // county itself when the Country changes, so the batch does too;
            // anything else is left for the user.
            bool countryChanged = plan.Changes.Any(c => c.ReadId == "cbxCOUNTRY");
            foreach (var check in plan.LeftoverChecks)
            {
                string column = check.ReadId == "edtCNTY" ? "COL_CNTY" : "COL_ARRL_SECT";
                string value = Text(now, column);
                if (HrdFieldChange.IsBlank(value)) continue;
                if (check.ReadId == "edtCNTY" && countryChanged) Set("US County", "COL_CNTY", "");
                else result.Warnings.Add($"The log has {check.Label} {value}, but {check.Reason} - clear it by hand.");
            }

            if (plan.Profile is HrdStationProfile profile) ApplyProfile(profile, now, Set, result);

            // Distance and bearing: HRD's Recalc, from the My Station locator to
            // the QSO's, grid square centre to grid square centre.
            if (Recalc(Text(now, "COL_MY_GRIDSQUARE"), Text(now, "COL_GRIDSQUARE")) is var (km, bearing))
            {
                Set("Distance (km)", "COL_DISTANCE", km, 0.01);
                Set("Bearing", "COL_ANT_AZ", bearing, 0.05);
                if (Text(now, "COL_ANT_PATH").Length == 0) Set("Path", "COL_ANT_PATH", "S");
            }

            return result;
        }

        private static void ApplyProfile(
            HrdStationProfile profile, Dictionary<string, object?> now,
            Action<string, string, object?, double> set, HrdRowChanges result)
        {
            if (!profile.HasPosition || profile.Fields is null)
            {
                result.Warnings.Add($"\"{profile.DisplayName}\" has no location in HRD's My Station settings - My Station left as it is.");
                return;
            }

            foreach (var (name, column, type) in ProfileColumns)
            {
                string value = profile.Fields.TryGetValue(name, out string? v) ? v : "";
                // An existing FT-991a stays: it's the rig that QSO was made with
                // (the user's rule - the FTdx-10 has been in use since June 2024).
                if (column == "COL_MY_RIG" && Text(now, column).Contains("991", StringComparison.Ordinal)) continue;

                object? typed = column == "COL_MY_GRIDSQUARE" ? Maidenhead.Normalize(value) : type switch
                {
                    'I' => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int i) ? i : null,
                    'D' => double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double d) ? d : null,
                    _ => value,
                };
                set(PrettyMyStation(column), column, typed, 1e-6);
            }

            if (HrdCountries.Dxcc(profile.Fields.GetValueOrDefault("Country", "")) is int dxcc)
                set("My DXCC", "COL_MY_DXCC", dxcc.ToString(CultureInfo.InvariantCulture), 1e-6);
        }

        private static string PrettyMyStation(string column) =>
            column.StartsWith("COL_MY_", StringComparison.Ordinal)
                ? "My " + column["COL_MY_".Length..].Replace('_', ' ')
                : "My " + column["COL_".Length..].Replace('_', ' ');

        // ---- helpers --------------------------------------------------------------

        // HRD's Recalc: great-circle distance (km, earth radius 6371) and
        // initial bearing between the centres of two grid squares, rounded as
        // HRD stores them. Null if either isn't a valid grid.
        public static (double Km, double Bearing)? Recalc(string myGrid, string grid)
        {
            if (!Maidenhead.TryGetCenter(myGrid, out double lat1, out double lon1) ||
                !Maidenhead.TryGetCenter(grid, out double lat2, out double lon2))
                return null;

            double p1 = lat1 * Math.PI / 180, p2 = lat2 * Math.PI / 180, dl = (lon2 - lon1) * Math.PI / 180;
            double dp = p2 - p1;
            double a = Math.Sin(dp / 2) * Math.Sin(dp / 2) + Math.Cos(p1) * Math.Cos(p2) * Math.Sin(dl / 2) * Math.Sin(dl / 2);
            double km = 2 * 6371.0 * Math.Asin(Math.Sqrt(a));
            double bearing = (Math.Atan2(Math.Sin(dl) * Math.Cos(p2),
                                  Math.Cos(p1) * Math.Sin(p2) - Math.Sin(p1) * Math.Cos(p2) * Math.Cos(dl)) * 180 / Math.PI + 360) % 360;
            return (Math.Round(km, 2), Math.Round(bearing, 1));
        }

        private static object? Typed(string column, string value) => column switch
        {
            "COL_CQZ" or "COL_ITUZ" => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) ? n : null,
            "COL_LAT" or "COL_LON" => double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double d) ? d : null,
            "COL_GRIDSQUARE" => Maidenhead.Normalize(value),
            _ => value,
        };

        private static string Text(IReadOnlyDictionary<string, object?> row, string column) =>
            row.TryGetValue(column, out object? value) ? Text(value) : "";

        private static string Text(object? value) => value switch
        {
            null => "",
            string s => s.Trim(),
            double d => d.ToString("R", CultureInfo.InvariantCulture),
            _ => Convert.ToString(value, CultureInfo.InvariantCulture)?.Trim() ?? "",
        };

        private static bool IsNumber(object? v) => v is int or long or double or float or decimal;

        // The same value, ignoring case and spacing for text and a hair of
        // difference for numbers; empty and null are the same.
        internal static bool SameValue(object? a, object? b, double tolerance)
        {
            if (IsNumber(a) && IsNumber(b))
                return Math.Abs(Convert.ToDouble(a, CultureInfo.InvariantCulture) - Convert.ToDouble(b, CultureInfo.InvariantCulture)) <= tolerance;
            return Text(a).Equals(Text(b), StringComparison.OrdinalIgnoreCase);
        }
    }
}
