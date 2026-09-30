using System.Text.Json;

namespace CallsignLookup.Services
{
    public sealed record CountyMatch(string Fips, string County, string StateAbbrev);

    // Looks up which US county a latitude/longitude point falls in, entirely
    // offline, using the county boundary shapes in Data\counties.json (derived
    // from the Census Bureau's 2017 cartographic boundary files via the
    // "us-atlas" dataset). Ported from POTA Activator Park Activations'
    // CountyLookupService.
    //
    // Only meant for stations QRZ says are in the US (CallsignLookupService
    // decides that) - it always returns some county, falling back to the
    // nearest one, which would be nonsense for a station in Canada or Mexico.
    public static class CountyLookupService
    {
        private sealed class CountyData
        {
            public string Fips { get; set; } = "";
            public string Name { get; set; } = "";
            public string StateFips { get; set; } = "";
            public double MinLon { get; set; }
            public double MinLat { get; set; }
            public double MaxLon { get; set; }
            public double MaxLat { get; set; }
            public List<List<double[]>> Polys { get; set; } = new();
        }

        // Standard FIPS state numeric code -> two-letter postal abbreviation.
        private static readonly Dictionary<string, string> FipsToState = new()
        {
            ["01"] = "AL", ["02"] = "AK", ["04"] = "AZ", ["05"] = "AR", ["06"] = "CA",
            ["08"] = "CO", ["09"] = "CT", ["10"] = "DE", ["11"] = "DC", ["12"] = "FL",
            ["13"] = "GA", ["15"] = "HI", ["16"] = "ID", ["17"] = "IL", ["18"] = "IN",
            ["19"] = "IA", ["20"] = "KS", ["21"] = "KY", ["22"] = "LA", ["23"] = "ME",
            ["24"] = "MD", ["25"] = "MA", ["26"] = "MI", ["27"] = "MN", ["28"] = "MS",
            ["29"] = "MO", ["30"] = "MT", ["31"] = "NE", ["32"] = "NV", ["33"] = "NH",
            ["34"] = "NJ", ["35"] = "NM", ["36"] = "NY", ["37"] = "NC", ["38"] = "ND",
            ["39"] = "OH", ["40"] = "OK", ["41"] = "OR", ["42"] = "PA", ["44"] = "RI",
            ["45"] = "SC", ["46"] = "SD", ["47"] = "TN", ["48"] = "TX", ["49"] = "UT",
            ["50"] = "VT", ["51"] = "VA", ["53"] = "WA", ["54"] = "WV", ["55"] = "WI",
            ["56"] = "WY", ["60"] = "AS", ["66"] = "GU", ["69"] = "MP", ["72"] = "PR",
            ["78"] = "VI"
        };

        // Match "fips" (JSON) to "Fips" (C# property) etc. - without this every
        // county silently deserializes with blank/zero values.
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        private static readonly Lazy<List<CountyData>> Counties = new(() =>
        {
            string json = File.ReadAllText(DataFiles.PathFor("counties.json"));
            return JsonSerializer.Deserialize<List<CountyData>>(json, JsonOptions) ?? new List<CountyData>();
        });

        // The county containing the point. If it's outside every county's
        // outline - a barrier island the simplified outlines miss, a point
        // just offshore, or an approximate grid-square center out in a lake -
        // the county whose boundary is closest instead, preferring counties in
        // stateAbbrev (the state on the QRZ record) when that's given so a
        // point near a state line doesn't land in the neighboring state.
        // Null only if counties.json has no counties at all; throws if it
        // can't be read, so a broken install shows up as an error.
        public static CountyMatch? FindCounty(double latitude, double longitude, string? stateAbbrev = null)
        {
            var counties = Counties.Value;

            foreach (var county in counties)
            {
                if (longitude < county.MinLon || longitude > county.MaxLon ||
                    latitude < county.MinLat || latitude > county.MaxLat)
                    continue;

                if (PolygonMath.Contains(county.Polys, longitude, latitude))
                    return ToMatch(county);
            }

            IEnumerable<CountyData> candidates = counties;
            if (!string.IsNullOrWhiteSpace(stateAbbrev))
            {
                string state = stateAbbrev.Trim().ToUpperInvariant();
                var inState = counties.Where(c => StateOf(c) == state).ToList();
                if (inState.Count > 0) candidates = inState;
            }

            var nearest = candidates.MinBy(c => PolygonMath.DistanceSquared(c.Polys, longitude, latitude));
            return nearest == null ? null : ToMatch(nearest);
        }

        private static string StateOf(CountyData county) =>
            FipsToState.TryGetValue(county.StateFips, out var abbr) ? abbr : "";

        private static CountyMatch ToMatch(CountyData county) =>
            new(county.Fips, county.Name, StateOf(county));
    }
}
