using Microsoft.Win32;

namespace CallsignLookup.Services.Hrd
{
    // DisplayName is how HRD's My Station tab lists the profile: "Home - K5JSG".
    // Locator, Latitude and Longitude are the profile's own position - what is
    // compared with a QSO's My Station to see whether it's the same station
    // (empty / null when the profile has none, like K5JSG/P). Fields holds the
    // rest of the profile's named values (City, Street, Equipment...), by
    // registry name, for writing a QSO's My Station columns straight into the
    // log (the batch fill).
    public sealed record HrdStationProfile(
        string Description, string Callsign, string Locator = "", double? Latitude = null, double? Longitude = null,
        IReadOnlyDictionary<string, string>? Fields = null)
    {
        public string DisplayName => $"{Description} - {Callsign}";
        public override string ToString() => DisplayName;

        // Enough of a position to tell this station from another.
        public bool HasPosition => Locator.Length > 0 && Latitude != null && Longitude != null;
    }

    // HRD's My Station profiles, from the registry (Tools > Configure > My
    // Station). Each callsign has its main profile in its own key and any
    // extra locations in numbered subkeys:
    //   HKCU\Software\Amateur Radio\HRD User Profile\K5JSG      Home
    //   HKCU\Software\Amateur Radio\HRD User Profile\K5JSG\0    Irvine
    // Only the description, callsign, locator, position and the My Station
    // values listed in ProfileValueNames are read - these keys also hold the
    // user's upload passwords, which this app has no business touching.
    public static class HrdStationProfiles
    {
        private const string KeyPath = @"Software\Amateur Radio\HRD User Profile";

        // The My Station values read (everything HRD copies into a QSO's
        // My Station fields when a profile is picked).
        internal static readonly string[] ProfileValueNames =
        [
            "Altitude", "Antennas", "ARRLSECT", "City", "County", "Country", "cqzone", "FistsID", "Locator",
            "ituzone", "Latitude", "Longitude", "Name", "Zip", "Equipment", "SigInfo", "Sig", "SOTA", "State",
            "Street", "USACA_COUNTIES", "VUCC_GRIDS", "WWFF", "Operator", "Callsign", "POTA", "License",
        ];

        // In the order HRD lists them.
        public static List<HrdStationProfile> Load()
        {
            var profiles = new List<HrdStationProfile>();
            using var root = Registry.CurrentUser.OpenSubKey(KeyPath);
            if (root == null) return profiles;

            foreach (string name in root.GetSubKeyNames())
            {
                if (name.Equals("Options", StringComparison.OrdinalIgnoreCase)) continue;
                using var key = root.OpenSubKey(name);
                if (key == null || Read(key, name) is not HrdStationProfile main) continue;
                profiles.Add(main);

                foreach (string sub in key.GetSubKeyNames()
                             .Where(s => int.TryParse(s, out _))
                             .OrderBy(s => int.Parse(s)))
                {
                    using var subKey = key.OpenSubKey(sub);
                    if (subKey != null && Read(subKey, name) is HrdStationProfile extra) profiles.Add(extra);
                }
            }
            return profiles;
        }

        private static HrdStationProfile? Read(RegistryKey key, string fallbackCallsign)
        {
            string callsign = (key.GetValue("Callsign") as string)?.Trim() ?? "";
            string description = (key.GetValue("Description") as string)?.Trim() ?? "";
            if (callsign.Length == 0) callsign = fallbackCallsign;
            if (description.Length == 0) return null;

            var fields = new Dictionary<string, string>();
            foreach (string valueName in ProfileValueNames)
                fields[valueName] = (key.GetValue(valueName) as string)?.Trim() ?? "";
            fields["Callsign"] = callsign;

            string locator = fields["Locator"];
            double? lat = Number(fields["Latitude"]), lon = Number(fields["Longitude"]);
            // 0,0 is HRD's "not set".
            if (lat == 0 && lon == 0) lat = lon = null;
            return new HrdStationProfile(description, callsign, locator, lat, lon, fields);
        }

        private static double? Number(string value) =>
            double.TryParse(value, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out double d) ? d : null;
    }
}
