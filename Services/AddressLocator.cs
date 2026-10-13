using System.Text.RegularExpressions;

namespace CallsignLookup.Services
{
    // The address a QSO is at, as far as it can be told: a street, a PO box, a
    // city and state, a ZIP. ZipMatchesState is false when the ZIP belongs to
    // another state than the one given (W7I: Tacoma, WA with Idaho ZIP 83669),
    // in which case Zip is blank and Note says so.
    public sealed record ParsedAddress(string Street, string City, string State, string Zip, bool PoBox, string Note)
    {
        public bool IsEmpty => Street.Length == 0 && City.Length == 0 && Zip.Length == 0;
    }

    // Works out a location for a station whose QRZ record has none (typically
    // a special-event page: Bigfoot's W7I has grid AA00aa and a wrong ZIP) from
    // its address - the one in HRD's Address box, which the user can correct
    // from the page's certificate address, or failing that QRZ's own city,
    // state and ZIP. In order of how good the answer is:
    //   1. a street address - that point;
    //   2. a PO box or just a ZIP (the ZIP checked against the state) - the
    //      post office for that ZIP, if OpenStreetMap has one;
    //   3. a city and state - the middle of the city.
    // Always marked approximate; the report says which one was used.
    public static class AddressLocator
    {
        private static readonly Regex PoBox =
            new(@"\b(?:P\.?\s*O\.?\s*Box|Post\s+Office\s+Box|PMB)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex CityStateZip =
            new(@"^(?<city>.+?),?\s+(?<st>[A-Za-z]{2})\.?\s+(?<zip>\d{5})(?:-\d{4})?$", RegexOptions.Compiled);

        private static readonly Regex CityState =
            new(@"^(?<city>.+?),\s*(?<st>[A-Za-z]{2})\.?$", RegexOptions.Compiled);

        private static readonly string[] CountryLines =
            ["united states", "united states of america", "usa", "us", "canada"];

        // ---- reading the address -------------------------------------------------

        // HRD's Address box: "Name, CALL / street / City, ST ZIP / Country" as
        // HRD's QRZ lookup writes it, or whatever the user typed in its place.
        // QRZ's own city, state and ZIP fill in what the text doesn't say.
        public static ParsedAddress Parse(string hrdAddress, QrzCallsignRecord record)
        {
            string street = "", city = "", state = "", zip = "";
            bool poBox = false;

            var lines = hrdAddress.Replace("\r", "").Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .Where(l => record.Call.Length == 0 || !l.Contains(record.Call, StringComparison.OrdinalIgnoreCase))
                .Where(l => !CountryLines.Contains(l.ToLowerInvariant()))
                .ToList();

            foreach (string line in lines)
            {
                var match = CityStateZip.Match(line);
                if (!match.Success) match = CityState.Match(line);
                if (match.Success && !PoBox.IsMatch(line))
                {
                    city = match.Groups["city"].Value.Trim();
                    state = match.Groups["st"].Value.ToUpperInvariant();
                    zip = match.Groups["zip"].Success ? match.Groups["zip"].Value : "";
                }
                else if (PoBox.IsMatch(line))
                {
                    poBox = true;
                }
                else if (street.Length == 0 && char.IsAsciiDigit(line[0]))
                {
                    street = line;
                }
            }

            if (city.Length == 0)
            {
                city = record.City.Trim();
                state = record.State.Trim().ToUpperInvariant();
                zip = record.Zip.Trim();
                if (zip.Length > 5) zip = zip[..5];
            }
            if (street.Length == 0 && !poBox)
            {
                if (PoBox.IsMatch(record.Address1)) poBox = true;
                else if (record.Address1.Length > 0 && char.IsAsciiDigit(record.Address1[0])) street = record.Address1.Trim();
            }

            string note = "";
            if (zip.Length > 0 && state.Length > 0 && ZipStates.StateFor(zip) is string zipState && zipState != state)
            {
                note = $"ZIP {zip} is a {zipState} ZIP, not {state} - the ZIP was ignored.";
                zip = "";
            }
            return new ParsedAddress(street, city, state, zip, poBox, note);
        }

        // ---- finding it -------------------------------------------------------------

        // The lookup with the address's location in place of the missing one, or
        // null if nothing could be found. The station's QRZ record stays, but
        // with its county cleared (QRZ's was worked out from the same bad ZIP)
        // and the address's state - county, section and zones follow the place.
        public static async Task<LookupResult?> LocateAsync(
            ParsedAddress address, LookupResult lookup, IGeocoder geocoder, CancellationToken cancellationToken = default)
        {
            if (address.IsEmpty) return null;
            var record = lookup.Qrz;
            string countries = record.IsCanadian ? "ca" : record.IsUnitedStates ? "us" : "us,ca";

            try
            {
                string? how = null;
                GeocodeMatch? match = null;

                if (address.Street.Length > 0 && address.City.Length > 0)
                {
                    match = await geocoder.FindAsync($"{address.Street}, {address.City}, {address.State} {address.Zip}".Trim(), countries, cancellationToken);
                    if (match != null) how = $"the street address {address.Street}, {address.City}, {address.State}";
                }

                if (match == null && address.Zip.Length > 0 && (address.PoBox || address.Street.Length == 0))
                {
                    var office = await geocoder.FindAsync($"post office {address.Zip}", countries, cancellationToken);
                    // A PO-box-only ZIP lives inside a post office whose own address has
                    // another ZIP (98411's boxes are at the Evergreen Post Office, 98413 -
                    // OpenStreetMap has it as 98409), so the ZIP itself can't be required
                    // to match. The same first three digits (the same mail sorting area)
                    // and the same city do.
                    if (office != null && SameMailArea(office, address))
                    {
                        match = office;
                        how = $"a post office near ZIP {address.Zip} ({ShortName(office)})";
                    }
                }

                if (match == null && address.City.Length > 0 && address.State.Length > 0)
                {
                    match = await geocoder.FindAsync($"{address.City}, {address.State}", countries, cancellationToken);
                    if (match != null) how = $"the middle of {address.City}, {address.State}";
                }

                if (match == null || how == null) return null;

                string state = address.State.Length > 0 ? address.State : record.State;
                string source = $"{how} (approximate - QRZ has no location for this station" +
                                (address.Note.Length > 0 ? "; " + address.Note.TrimEnd('.') : "") + ")";
                return CallsignLookupService.ResolveAt(record.At(record.Dxcc, state), match.Latitude, match.Longitude, source);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
            {
                return null;   // no network - no worse off than without it
            }
        }

        private static bool SameMailArea(GeocodeMatch office, ParsedAddress address) =>
            office.Postcode.Length >= 3 && office.Postcode[..3] == address.Zip[..3] &&
            (address.City.Length == 0 || office.City.Equals(address.City, StringComparison.OrdinalIgnoreCase));

        // "US Post Office - Evergreen, 4001, South Pine Street" from Nominatim's long name.
        private static string ShortName(GeocodeMatch match) =>
            string.Join(", ", match.DisplayName.Split(", ", StringSplitOptions.RemoveEmptyEntries).Take(3));

        // For a lookup whose QRZ record had no usable location: try the HRD
        // address (empty string = use QRZ's own city, state and ZIP). Anything
        // else - QRZ had a location, no geocoder, nothing found - is returned as it was.
        public static async Task<LookupResult> UseAddressAsync(
            LookupResult lookup, string hrdAddress, IGeocoder? geocoder, CancellationToken cancellationToken = default)
        {
            if (geocoder == null || lookup.QrzHadLocation) return lookup;
            var located = await LocateAsync(Parse(hrdAddress, lookup.Qrz), lookup, geocoder, cancellationToken);
            return located ?? lookup;
        }
    }

    // The state each 3-digit ZIP prefix belongs to, to tell a ZIP that doesn't
    // fit its state (an Idaho ZIP on a Tacoma, WA address). Approximate at the
    // edges - a prefix not listed counts as "can't tell", and a wrong "doesn't
    // fit" only means the ZIP is ignored while the city is still used.
    internal static class ZipStates
    {
        private static readonly (int From, int To, string State)[] Ranges =
        [
            (5, 5, "NY"), (6, 9, "PR"), (10, 27, "MA"), (28, 29, "RI"), (30, 38, "NH"), (39, 49, "ME"), (50, 59, "VT"),
            (60, 69, "CT"), (70, 89, "NJ"), (100, 149, "NY"), (150, 196, "PA"), (197, 199, "DE"), (200, 205, "DC"),
            (206, 219, "MD"), (220, 246, "VA"), (247, 268, "WV"), (270, 289, "NC"), (290, 299, "SC"), (300, 319, "GA"),
            (320, 339, "FL"), (341, 349, "FL"), (350, 369, "AL"), (370, 385, "TN"), (386, 397, "MS"), (398, 399, "GA"),
            (400, 427, "KY"), (430, 459, "OH"), (460, 479, "IN"), (480, 499, "MI"), (500, 528, "IA"), (530, 549, "WI"),
            (550, 567, "MN"), (570, 577, "SD"), (580, 588, "ND"), (590, 599, "MT"), (600, 629, "IL"), (630, 658, "MO"),
            (660, 679, "KS"), (680, 693, "NE"), (700, 714, "LA"), (716, 729, "AR"), (733, 733, "TX"), (730, 749, "OK"),
            (750, 799, "TX"), (800, 816, "CO"), (820, 831, "WY"), (832, 838, "ID"), (840, 847, "UT"), (850, 865, "AZ"),
            (870, 884, "NM"), (885, 885, "TX"), (889, 898, "NV"), (900, 961, "CA"), (967, 968, "HI"), (969, 969, "GU"),
            (970, 979, "OR"), (980, 994, "WA"), (995, 999, "AK"),
        ];

        // Null when the ZIP isn't five digits or its prefix isn't listed.
        public static string? StateFor(string zip)
        {
            if (zip.Length < 5 || !int.TryParse(zip[..3], out int prefix) || !zip.Take(5).All(char.IsAsciiDigit)) return null;
            foreach (var (from, to, state) in Ranges)
                if (prefix >= from && prefix <= to) return state;
            return null;
        }
    }
}
