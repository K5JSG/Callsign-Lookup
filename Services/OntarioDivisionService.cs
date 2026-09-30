using System.Text.Json;

namespace CallsignLookup.Services
{
    public sealed record OntarioDivisionMatch(string Name, bool InOrSouthOfAlgonquinPark);

    // Which Ontario census division a point is in, from Data\ontarioDivisions.json
    // (Statistics Canada 2021 census divisions plus Algonquin Park's outline -
    // see Tools\build_ontario_divisions.py). RAC's Ontario sections are drawn
    // along these divisions, except Nipissing District, where "inside or south
    // of Algonquin Park" is Ontario East and the rest Ontario North - hence
    // the park check.
    public static class OntarioDivisionService
    {
        private sealed class Shape
        {
            public string Name { get; set; } = "";
            public double MinLon { get; set; }
            public double MinLat { get; set; }
            public double MaxLon { get; set; }
            public double MaxLat { get; set; }
            public List<List<double[]>> Polys { get; set; } = new();
        }

        private sealed class DivisionFile
        {
            public List<Shape> Divisions { get; set; } = new();
            public Shape AlgonquinPark { get; set; } = new();
        }

        private static readonly Lazy<DivisionFile> Data = new(() =>
            JsonSerializer.Deserialize<DivisionFile>(
                File.ReadAllText(DataFiles.PathFor("ontarioDivisions.json")),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new DivisionFile());

        // Only meant for stations QRZ says are in Ontario, so a point outside
        // every division (an island too small to keep, a spot in a lake, an
        // approximate grid-square center) gets the nearest division rather
        // than none.
        public static OntarioDivisionMatch? FindDivision(double latitude, double longitude)
        {
            var data = Data.Value;

            var division = data.Divisions.FirstOrDefault(d =>
                longitude >= d.MinLon && longitude <= d.MaxLon &&
                latitude >= d.MinLat && latitude <= d.MaxLat &&
                PolygonMath.Contains(d.Polys, longitude, latitude))
                ?? data.Divisions.MinBy(d => PolygonMath.DistanceSquared(d.Polys, longitude, latitude));

            if (division == null) return null;

            var park = data.AlgonquinPark.Polys;
            bool algonquin = PolygonMath.Contains(park, longitude, latitude) ||
                             PolygonMath.HasEdgeDueNorth(park, longitude, latitude);
            return new OntarioDivisionMatch(division.Name, algonquin);
        }
    }
}
