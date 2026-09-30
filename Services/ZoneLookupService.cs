using System.Text.Json;

namespace CallsignLookup.Services
{
    // CQ and ITU zone lookup for a lat/long, offline, against the zone
    // boundary polygons in Data\cqZones.json and Data\ituZones.json - the same
    // files POTA Activator Park Activations' map uses for its zone readout,
    // simplified from HB9HIL's MIT-licensed hamradio-zones-geojson dataset
    // (the dataset behind zone-check.eu). CQ/ITU zones aren't a formula: their
    // edges partly follow political borders, so this is a point-in-polygon
    // test, not arithmetic.
    //
    // File format: [[zoneNumber, MultiPolygon coordinates], ...] where the
    // coordinates are GeoJSON-style polygons -> rings -> [lon, lat] points.
    public static class ZoneLookupService
    {
        private sealed class ZoneShape
        {
            public int Zone { get; init; }
            public List<List<double[]>> Polys { get; } = new(); // polygon -> ring -> flat [lon,lat,...]
            public double MinLon { get; set; } = double.MaxValue;
            public double MinLat { get; set; } = double.MaxValue;
            public double MaxLon { get; set; } = double.MinValue;
            public double MaxLat { get; set; } = double.MinValue;
        }

        private static readonly Lazy<List<ZoneShape>> CqZones = new(() => Load("cqZones.json"));
        private static readonly Lazy<List<ZoneShape>> ItuZones = new(() => Load("ituZones.json"));

        public static int? FindCqZone(double latitude, double longitude) =>
            FindZone(CqZones.Value, latitude, longitude);

        public static int? FindItuZone(double latitude, double longitude) =>
            FindZone(ItuZones.Value, latitude, longitude);

        private static int? FindZone(List<ZoneShape> zones, double latitude, double longitude)
        {
            // Some zones straddling the antimeridian are drawn with longitudes
            // beyond +/-180 (e.g. -200 for 160E), so also try the point shifted
            // a full turn each way.
            foreach (double lon in new[] { longitude, longitude - 360, longitude + 360 })
            {
                foreach (var zone in zones)
                {
                    if (lon < zone.MinLon || lon > zone.MaxLon ||
                        latitude < zone.MinLat || latitude > zone.MaxLat)
                        continue;

                    if (PolygonMath.Contains(zone.Polys, lon, latitude)) return zone.Zone;
                }
            }
            return null;
        }

        private static List<ZoneShape> Load(string fileName)
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(DataFiles.PathFor(fileName)));
            var zones = new List<ZoneShape>();

            foreach (var entry in doc.RootElement.EnumerateArray())
            {
                var zone = new ZoneShape { Zone = entry[0].GetInt32() };

                foreach (var polygon in entry[1].EnumerateArray())
                {
                    var rings = new List<double[]>();
                    foreach (var ring in polygon.EnumerateArray())
                    {
                        var flat = new double[ring.GetArrayLength() * 2];
                        int k = 0;
                        foreach (var point in ring.EnumerateArray())
                        {
                            double lon = point[0].GetDouble(), lat = point[1].GetDouble();
                            flat[k++] = lon;
                            flat[k++] = lat;
                            zone.MinLon = Math.Min(zone.MinLon, lon);
                            zone.MaxLon = Math.Max(zone.MaxLon, lon);
                            zone.MinLat = Math.Min(zone.MinLat, lat);
                            zone.MaxLat = Math.Max(zone.MaxLat, lat);
                        }
                        rings.Add(flat);
                    }
                    zone.Polys.Add(rings);
                }

                zones.Add(zone);
            }

            return zones;
        }
    }
}
