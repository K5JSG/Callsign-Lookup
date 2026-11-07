using System.Xml;
using System.Xml.Linq;

namespace CallsignLookup.Services.Upload
{
    // A TQSL station location: what LoTW is told about the station the QSOs
    // were made from.
    public sealed record TqslLocation(string Name, string Callsign, string Grid, string State = "", string County = "")
    {
        public string DisplayName =>
            Grid.Length > 0 ? $"{Name}  ({Callsign}, {Grid})" : $"{Name}  ({Callsign})";

        public override string ToString() => DisplayName;
    }

    // TQSL's station locations, from its own file
    // (%AppData%\TrustedQSL\station_data):
    //   <StationDataFile><StationData name="Hamlin Beach SP"><CALL>K5JSG</CALL>
    //     <GRIDSQUARE>FN13AH</GRIDSQUARE>...</StationData></StationDataFile>
    public static class TqslLocations
    {
        public static string StationDataPath => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TrustedQSL", "station_data");

        // By name. Empty if TQSL has none (or isn't installed).
        public static List<TqslLocation> Load(string? path = null)
        {
            path ??= StationDataPath;
            try
            {
                return File.Exists(path) ? Parse(File.ReadAllText(path)) : [];
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or XmlException)
            {
                return [];
            }
        }

        internal static List<TqslLocation> Parse(string xml) =>
            XDocument.Parse(xml).Descendants("StationData")
                .Select(s => new TqslLocation(
                    ((string?)s.Attribute("name") ?? "").Trim(),
                    ((string?)s.Element("CALL") ?? "").Trim().ToUpperInvariant(),
                    ((string?)s.Element("GRIDSQUARE") ?? "").Trim().ToUpperInvariant(),
                    ((string?)s.Element("US_STATE") ?? "").Trim(),
                    ((string?)s.Element("US_COUNTY") ?? "").Trim()))
                .Where(l => l.Name.Length > 0)
                .OrderBy(l => l.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

        // The location to sign a QSO with, made by stationCallsign from
        // myGrid. First HRD's own choice (hrdLocation, its LOTWStation) if
        // that location fits the QSO, then the only location that does. When
        // several fit (two parks in one grid square), a park reference or
        // park name in a location's name settles it. Otherwise null: the user
        // picks one.
        public static TqslLocation? Pick(
            IReadOnlyList<TqslLocation> locations, string stationCallsign, string myGrid, string hrdLocation = "",
            IEnumerable<string>? parkHints = null)
        {
            var fits = locations.Where(l => Fits(l, stationCallsign, myGrid)).ToList();
            if (hrdLocation.Trim().Length > 0 &&
                fits.FirstOrDefault(l => l.Name.Equals(hrdLocation.Trim(), StringComparison.OrdinalIgnoreCase)) is { } hrd)
                return hrd;
            if (fits.Count == 1) return fits[0];
            if (fits.Count == 0) return null;

            var hints = (parkHints ?? []).Select(h => h.Trim()).Where(h => h.Length >= 3).ToList();
            if (hints.Count == 0) return null;
            var named = fits.Where(l => hints.Any(h => l.Name.Contains(h, StringComparison.OrdinalIgnoreCase))).ToList();
            return named.Count == 1 ? named[0] : null;
        }

        // Same callsign and the same grid square, as far as both go (a
        // location's FN13AH fits a QSO's FN13ahXX and FN13).
        public static bool Fits(TqslLocation location, string stationCallsign, string myGrid)
        {
            if (!location.Callsign.Equals(stationCallsign.Trim(), StringComparison.OrdinalIgnoreCase)) return false;
            string a = location.Grid.Trim(), b = myGrid.Trim();
            if (a.Length < 4 || b.Length < 4) return false;
            int n = Math.Min(a.Length, b.Length);
            return string.Compare(a, 0, b, 0, n, StringComparison.OrdinalIgnoreCase) == 0;
        }
    }
}
